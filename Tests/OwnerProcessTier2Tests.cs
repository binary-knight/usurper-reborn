using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.13 Tier 2: NPC memories keep their real times, and the world edits log that the owner process
/// re-applies so a delete's clean-up survives stale and old-binary writes of the shared records.
/// </summary>
[Collection("SharedGameSingletons")]
public class OwnerProcessTier2Tests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-owner2-{Guid.NewGuid():N}.db");
    private readonly SqlSaveBackend _db;
    private readonly List<NPC> _rosterBefore;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IncludeFields = true,
    };

    public OwnerProcessTier2Tests()
    {
        _db = new SqlSaveBackend(_path);
        _rosterBefore = NPCSpawnSystem.Instance.ActiveNPCs.ToList();
        NPCSpawnSystem.Instance.ActiveNPCs.Clear();
    }

    public void Dispose()
    {
        var roster = NPCSpawnSystem.Instance.ActiveNPCs;
        roster.Clear();
        roster.AddRange(_rosterBefore);
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    private static NPC Npc(string id, string name)
    {
        var npc = new NPC { ID = id, Id = id, Name1 = name, Name2 = name, Level = 10, HP = 100, MaxHP = 100 };
        npc.EnsureSystemsInitialized();
        NPCSpawnSystem.Instance.ActiveNPCs.Add(npc);
        return npc;
    }

    private static MemoryEvent Remember(NPC npc, MemoryType type, string who, DateTime at, float impact = 0f, float importance = 0.9f)
    {
        var m = new MemoryEvent { Type = type, InvolvedCharacter = who, Description = type.ToString(), EmotionalImpact = impact, Importance = importance };
        npc.Brain!.Memory.RecordEvent(m);
        m.Timestamp = at;
        return m;
    }

    private static NPC? Find(string name) => NPCSpawnSystem.Instance.ActiveNPCs.FirstOrDefault(n => n.Name2 == name);

    private static List<NPCData> RoundTrip(List<NPCData> data) =>
        JsonSerializer.Deserialize<List<NPCData>>(JsonSerializer.Serialize(data, Json), Json)!;

    private static List<MemoryEvent> MemoriesOf(string name) => Find(name)!.Brain!.Memory.AllMemories;

    // ─── Step 0: memory times ───

    [Fact]
    public async Task AMemory_SurvivesSaveAndLoad_WithItsRealTime()
    {
        var npc = Npc("npc_mt_1", "Keeper");
        var at = DateTime.Now.AddDays(-3);
        Remember(npc, MemoryType.Attacked, "Bob", at);

        var data = RoundTrip(OnlineStateManager.SerializeCurrentNPCs());
        data.Single().MemoryTimesKept.Should().BeTrue("a save of this version marks its times as real");
        await GameEngine.Instance.RestoreNPCs(data);

        MemoriesOf("Keeper").Single().Timestamp.Should().BeCloseTo(at, TimeSpan.FromMilliseconds(5), "the load keeps the recorded time");

        // the world sim's own loader keeps it too
        var sim = new WorldSimService(_db);
        typeof(WorldSimService).GetMethod("RestoreNPCsFromData", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(sim, new object[] { RoundTrip(OnlineStateManager.SerializeCurrentNPCs()) });
        MemoriesOf("Keeper").Single().Timestamp.Should().BeCloseTo(at, TimeSpan.FromMilliseconds(5));
    }

    [Fact]
    public async Task TheFirstLoadUnderThisVersion_StampsOldMemories_AndNothingIsForgotten()
    {
        var npc = Npc("npc_mt_2", "Elder");
        Remember(npc, MemoryType.Attacked, "Bob", DateTime.Now.AddDays(-30), importance: 0.15f);
        var recent = DateTime.Now.AddDays(-2);
        Remember(npc, MemoryType.Helped, "Ann", recent, 0.4f);
        Remember(npc, MemoryType.Insulted, "Cid", default);
        var data = RoundTrip(OnlineStateManager.SerializeCurrentNPCs());
        data.Single().MemoryTimesKept = false;   // written by an older release

        var before = DateTime.Now;
        await GameEngine.Instance.RestoreNPCs(data);

        var memories = MemoriesOf("Elder");
        memories.Single(m => m.InvolvedCharacter == "Bob").Timestamp.Should().BeOnOrAfter(before, "older than 7 days on the first load: stamped now");
        memories.Single(m => m.InvolvedCharacter == "Cid").Timestamp.Should().BeOnOrAfter(before, "a missing time is stamped now");
        memories.Single(m => m.InvolvedCharacter == "Ann").Timestamp.Should().BeCloseTo(recent, TimeSpan.FromMilliseconds(5), "a recent time is kept");

        Find("Elder")!.Brain!.Memory.DecayMemories();
        MemoriesOf("Elder").Should().HaveCount(3, "nothing is forgotten on the day of the update");

        // the next save carries the mark, so the grace runs once
        OnlineStateManager.SerializeCurrentNPCs().Single().MemoryTimesKept.Should().BeTrue();
    }

    [Fact]
    public async Task AfterTheFirstLoad_AMemoryOlderThanSevenDays_Decays()
    {
        var npc = Npc("npc_mt_3", "Fader");
        Remember(npc, MemoryType.Attacked, "Bob", DateTime.Now.AddDays(-8), importance: 0.15f);
        Remember(npc, MemoryType.Helped, "Ann", DateTime.Now.AddDays(-1), 0.4f, importance: 0.15f);
        var data = RoundTrip(OnlineStateManager.SerializeCurrentNPCs());

        await GameEngine.Instance.RestoreNPCs(data);
        Find("Fader")!.Brain!.Memory.DecayMemories();

        MemoriesOf("Fader").Select(m => m.InvolvedCharacter).Should().Equal(new[] { "Ann" }, "the 8-day-old memory decayed, the recent one stays");
    }

    [Fact]
    public async Task ARetriedPurge_CutsOffAtTheDeleteTime_NotTheRetryTime()
    {
        var deletedAt = DateTime.Now.AddMinutes(-10);
        var npc = Npc("npc_mt_4", "Grudger");
        Remember(npc, MemoryType.Attacked, "Bob", deletedAt.AddMinutes(-5));
        await _db.SaveWorldState(OnlineStateManager.KEY_NPCS, JsonSerializer.Serialize(OnlineStateManager.SerializeCurrentNPCs(), Json));
        // another writer's roster: the old grudge, and one the new Bob earned after the delete
        var theirsNpc = Find("Grudger")!;
        var newGrudge = Remember(theirsNpc, MemoryType.Insulted, "Bob", deletedAt.AddMinutes(5));
        var theirs = OnlineStateManager.SerializeCurrentNPCs();
        theirsNpc.Brain!.Memory.AllMemories.Remove(newGrudge);

        int hooks = 0;
        await PermadeathHelper.ForgetCharacterInNpcWorldAsync(_db, new[] { "Bob" }, deletedAt, true,
            beforeWrite: async () => { if (hooks++ == 0) await _db.SaveWorldState(OnlineStateManager.KEY_NPCS, JsonSerializer.Serialize(theirs, Json)); });

        MemoriesOf("Grudger").Select(m => m.Type).Should().Equal(new[] { MemoryType.Insulted },
            "the retry after the reload clears only what was recorded by the delete");
    }
}
