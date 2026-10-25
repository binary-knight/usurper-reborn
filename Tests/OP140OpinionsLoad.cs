using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.8: an NPC's opinions (MemorySystem.CharacterImpressions) after a load equal the ones it held when the
/// game was saved, on every NPC load (GameEngine.RestoreNPCs for a player save and for the shared roster,
/// WorldSimService.RestoreNPCsFromData). The saved Relationships replace what the memory replay adds; a key
/// the save does not hold is gone; a record before 1.1.13 with no saved opinions loads as before.
/// </summary>
[Collection("SharedGameSingletons")]
public class OP140OpinionsLoad : IDisposable
{
    // the scale rounding: the writer multiplies by 100 and the load divides by 100, both in float
    private const float Bound = 1e-5f;

    private const string Hero = "OP140 Hero";
    private const string Other = "OP140 Other";
    private const string Faded = "OP140 Faded";

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-op140-{Guid.NewGuid():N}.db");
    private SqlSaveBackend? _db;
    private readonly List<NPC> _rosterBefore;

    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IncludeFields = true,
    };

    public OP140OpinionsLoad()
    {
        _rosterBefore = NPCSpawnSystem.Instance.ActiveNPCs.ToList();
        NPCSpawnSystem.Instance.ActiveNPCs.Clear();
        OnlineStateManager.NoteRosterRestored(null);
    }

    public void Dispose()
    {
        var roster = NPCSpawnSystem.Instance.ActiveNPCs;
        roster.Clear();
        roster.AddRange(_rosterBefore);
        OnlineStateManager.NoteRosterRestored(null);
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    // ---------- helpers ----------

    private static NPC Npc(string name)
    {
        var npc = new NPC { ID = "op140_" + name, Id = "op140_" + name, Name1 = name, Name2 = name, Level = 10, HP = 100, MaxHP = 100 };
        npc.EnsureSystemsInitialized();
        NPCSpawnSystem.Instance.ActiveNPCs.Add(npc);
        return npc;
    }

    private static NPC Find(string name) => NPCSpawnSystem.Instance.ActiveNPCs.Single(n => n.Name2 == name);

    private static Dictionary<string, float> Opinions(NPC npc) => new(npc.Brain.Memory.CharacterImpressions);

    private static void Remember(NPC npc, MemoryType type, string who) =>
        npc.Brain.Memory.RecordEvent(new MemoryEvent { Type = type, Description = "op140", InvolvedCharacter = who, Importance = 0.9f });

    private static void Decay(NPC npc, int ticks)
    {
        for (int i = 0; i < ticks; i++) npc.Brain.Memory.DecayMemories();
    }

    /// <summary>One memory-backed opinion decayed some ticks, one opinion set with no memory.</summary>
    private static NPC Seeded(string name)
    {
        var npc = Npc(name);
        Remember(npc, MemoryType.Helped, Hero);
        Decay(npc, 10);
        npc.Brain.Memory.CharacterImpressions[Other] = -0.5f;
        return npc;
    }

    private static List<NPCData> RoundTrip(List<NPCData> data) =>
        JsonSerializer.Deserialize<List<NPCData>>(JsonSerializer.Serialize(data, Json), Json)!;

    private static List<NPCData> PlayerSaveNpcs()
    {
        var m = typeof(SaveSystem).GetMethod("SerializeNPCs", Priv)!;
        return RoundTrip(((Task<List<NPCData>>)m.Invoke(SaveSystem.Instance, null)!).GetAwaiter().GetResult());
    }

    private static List<NPCData> SharedNpcs() => RoundTrip(OnlineStateManager.SerializeCurrentNPCs());

    private static async Task LoadPlayerSave(List<NPCData> data)
    {
        NPCSpawnSystem.Instance.ActiveNPCs.Clear();
        await GameEngine.Instance.RestoreNPCs(data);
    }

    private static async Task LoadShared(List<NPCData> data)
    {
        NPCSpawnSystem.Instance.ActiveNPCs.Clear();
        await GameEngine.Instance.RestoreNPCs(data, 7);
    }

    private Task LoadWorldSim(List<NPCData> data)
    {
        NPCSpawnSystem.Instance.ActiveNPCs.Clear();
        _db ??= new SqlSaveBackend(_path);
        var sim = new WorldSimService(_db);
        try { typeof(WorldSimService).GetMethod("RestoreNPCsFromData", Priv)!.Invoke(sim, new object[] { data }); }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
        return Task.CompletedTask;
    }

    private static void ShouldEqual(Dictionary<string, float> loaded, Dictionary<string, float> before)
    {
        loaded.Keys.Should().BeEquivalentTo(before.Keys, "the load holds the keys the save held");
        foreach (var (k, v) in before) loaded[k].Should().BeApproximately(v, Bound, $"the opinion of {k} is the saved one");
    }

    // ---------- a load equals the state before the save ----------

    [Fact]
    public async Task PlayerSave_Load_OpinionsEqualBeforeSave()
    {
        var before = Opinions(Seeded("OP140 Ann"));
        var replay = MemorySystem.ImpressionChangeFor(MemoryType.Helped);
        before[Hero].Should().BeLessThan(replay - 0.01f, "ten decay ticks moved the opinion off the replay value");

        await LoadPlayerSave(PlayerSaveNpcs());

        var loaded = Find("OP140 Ann");
        ShouldEqual(Opinions(loaded), before);
        loaded.Brain.Memory.GetMemoriesAboutCharacter(Hero).Should().ContainSingle("the memory itself is still replayed");
    }

    [Fact]
    public async Task SharedRoster_Load_OpinionsEqualBeforeSave()
    {
        var before = Opinions(Seeded("OP140 Bea"));

        await LoadShared(SharedNpcs());

        ShouldEqual(Opinions(Find("OP140 Bea")), before);
    }

    [Fact]
    public async Task WorldSim_Load_OpinionsEqualBeforeSave()
    {
        var before = Opinions(Seeded("OP140 Cal"));

        await LoadWorldSim(SharedNpcs());

        ShouldEqual(Opinions(Find("OP140 Cal")), before);
    }

    [Fact]
    public async Task EveryPath_MemoryBackedOpinion_IsNotCountedTwice()
    {
        var replay = MemorySystem.ImpressionChangeFor(MemoryType.Helped);
        foreach (var (load, shared) in new (Func<List<NPCData>, Task>, bool)[]
                 { (LoadPlayerSave, false), (LoadShared, true), (LoadWorldSim, true) })
        {
            NPCSpawnSystem.Instance.ActiveNPCs.Clear();
            float saved = Opinions(Seeded("OP140 Dot"))[Hero];
            await load(shared ? SharedNpcs() : PlayerSaveNpcs());
            float got = Opinions(Find("OP140 Dot"))[Hero];
            got.Should().BeApproximately(saved, Bound, "the saved value, not the saved value plus the replay");
            got.Should().NotBeApproximately(Math.Min(1f, saved + replay), 0.01f);
            got.Should().NotBeApproximately(replay, 0.01f, "nor the replay alone");
        }
    }

    // ---------- the keys rule: a key the save does not hold is gone ----------

    private static NPC FadedSeed(string name)
    {
        var npc = Npc(name);
        Remember(npc, MemoryType.SharedDrink, Faded);
        for (int i = 0; i < 500 && npc.Brain.Memory.CharacterImpressions.ContainsKey(Faded); i++) npc.Brain.Memory.DecayMemories();
        npc.Brain.Memory.CharacterImpressions.Should().NotContainKey(Faded, "the opinion decayed away");
        npc.Brain.Memory.GetMemoriesAboutCharacter(Faded).Should().NotBeEmpty("its memory is still held");
        npc.Brain.Memory.CharacterImpressions[Other] = 0.7f;
        return npc;
    }

    [Fact]
    public async Task EveryPath_DecayedAwayOpinion_DoesNotComeBack()
    {
        foreach (var (load, shared) in new (Func<List<NPCData>, Task>, bool)[]
                 { (LoadPlayerSave, false), (LoadShared, true), (LoadWorldSim, true) })
        {
            NPCSpawnSystem.Instance.ActiveNPCs.Clear();
            var before = Opinions(FadedSeed("OP140 Eve"));
            await load(shared ? SharedNpcs() : PlayerSaveNpcs());
            var loaded = Find("OP140 Eve");
            loaded.Brain.Memory.GetMemoriesAboutCharacter(Faded).Should().NotBeEmpty("the memory is replayed");
            ShouldEqual(Opinions(loaded), before);
        }
    }

    [Fact]
    public async Task WorldSim_KeyBeyondTheWritersCap_DoesNotComeBack()
    {
        var npc = Npc("OP140 Fay");
        for (int i = 0; i < 20; i++) npc.Brain.Memory.CharacterImpressions[$"OP140 Strong {i}"] = 0.9f - i * 0.01f;
        Remember(npc, MemoryType.SharedDrink, Faded);   // the weakest key, memory-backed: the shared writer keeps 20
        var data = SharedNpcs();
        data.Single(d => d.Name == "OP140 Fay").Relationships.Should().HaveCount(20).And.NotContainKey(Faded);

        await LoadWorldSim(data);

        var loaded = Opinions(Find("OP140 Fay"));
        loaded.Should().HaveCount(20).And.NotContainKey(Faded, "a key the save dropped does not come back");
    }

    [Fact]
    public async Task EveryPath_EmptySavedOpinions_From113On_LeaveNone()
    {
        foreach (var (load, shared) in new (Func<List<NPCData>, Task>, bool)[]
                 { (LoadPlayerSave, false), (LoadShared, true), (LoadWorldSim, true) })
        {
            NPCSpawnSystem.Instance.ActiveNPCs.Clear();
            var npc = Npc("OP140 Gus");
            Remember(npc, MemoryType.SharedDrink, Faded);
            npc.Brain.Memory.CharacterImpressions.Clear();
            var data = shared ? SharedNpcs() : PlayerSaveNpcs();
            var rec = data.Single(d => d.Name == "OP140 Gus");
            rec.Relationships.Should().BeEmpty();
            rec.MemoryTimesKept.Should().BeTrue();

            await load(data);

            Opinions(Find("OP140 Gus")).Should().BeEmpty("the save held no opinion");
        }
    }

    // ---------- a save without Relationships loads as before ----------

    private static List<NPCData> WithoutRelationships(List<NPCData> data, string name)
    {
        var arr = JsonNode.Parse(JsonSerializer.Serialize(data, Json))!.AsArray();
        var rec = arr.Single(n => (string?)n!["name"] == name)!.AsObject();
        rec.Remove("relationships");
        rec["memoryTimesKept"] = false;
        return JsonSerializer.Deserialize<List<NPCData>>(arr.ToJsonString(), Json)!;
    }

    [Fact]
    public async Task EveryPath_OldSaveWithoutRelationships_KeepsTheReplay()
    {
        var replay = MemorySystem.ImpressionChangeFor(MemoryType.Helped);
        foreach (var (load, shared) in new (Func<List<NPCData>, Task>, bool)[]
                 { (LoadPlayerSave, false), (LoadShared, true), (LoadWorldSim, true) })
        {
            NPCSpawnSystem.Instance.ActiveNPCs.Clear();
            Seeded("OP140 Hal");
            var data = WithoutRelationships(shared ? SharedNpcs() : PlayerSaveNpcs(), "OP140 Hal");
            data.Single(d => d.Name == "OP140 Hal").Relationships.Should().BeEmpty();

            await load(data);

            // as at the base: the replay of the one memory, and the memory-less opinion is not in the save
            var loaded = Opinions(Find("OP140 Hal"));
            loaded.Should().ContainSingle();
            loaded[Hero].Should().BeApproximately(replay, Bound);
        }
    }

    // ---------- single player: a second character ----------

    [Fact]
    public async Task SinglePlayer_SecondCharacter_GetsItsOwnSavedOpinions()
    {
        const string first = "OP140 First", second = "OP140 Second";
        var npc = Npc("OP140 Ivy");
        npc.Brain.Memory.CharacterImpressions[first] = 0.6f;
        var firstSave = PlayerSaveNpcs();

        NPCSpawnSystem.Instance.ActiveNPCs.Clear();
        npc = Npc("OP140 Ivy");
        npc.Brain.Memory.CharacterImpressions[second] = -0.3f;
        npc.Brain.Memory.CharacterImpressions[first] = 0.2f;
        var secondSave = PlayerSaveNpcs();

        await LoadPlayerSave(firstSave);
        var a = Opinions(Find("OP140 Ivy"));
        a.Should().HaveCount(1);
        a[first].Should().BeApproximately(0.6f, Bound);

        await LoadPlayerSave(secondSave);
        var b = Opinions(Find("OP140 Ivy"));
        b.Should().HaveCount(2, "the second character's save, nothing carried from the first load");
        b[second].Should().BeApproximately(-0.3f, Bound);
        b[first].Should().BeApproximately(0.2f, Bound, "a key for the first character is what the second save holds");
    }

    // ---------- one method on every load ----------

    [Fact]
    public void EveryNpcLoad_RestoresOpinions_AfterTheReplay_ThroughOneMethod()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Scripts"))) dir = dir.Parent;
        var scripts = Path.Combine(dir!.FullName, "Scripts");
        const string call = "SaveSystem.RestoreNPCRelationships(npc.Brain.Memory, data);";
        const string replay = "npc.Brain.Memory?.RecordEvent(memory, keepTimestamp: true);";

        foreach (var (file, method) in new[] { ("Core/GameEngine.cs", "private void RestoreNPCsLocked("),
                                               ("Systems/WorldSimService.cs", "private void RestoreNPCsFromData(") })
        {
            var src = File.ReadAllText(Path.Combine(scripts, file));
            int start = src.IndexOf(method, StringComparison.Ordinal);
            start.Should().BeGreaterThan(0, file);
            int r = src.IndexOf(replay, start, StringComparison.Ordinal);
            int c = src.IndexOf(call, start, StringComparison.Ordinal);
            r.Should().BeGreaterThan(start, file);
            c.Should().BeGreaterThan(r, $"{file} restores the opinions after the replay");
        }

        var callers = Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("RecordEvent(memory, keepTimestamp: true)")).Select(Path.GetFileName);
        callers.Should().BeEquivalentTo(new[] { "GameEngine.cs", "WorldSimService.cs" }, "these two loaders replay saved memories");
    }
}
