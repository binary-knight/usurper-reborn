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
using UsurperRemake;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.8: three carries.
/// A new character that is not NG+ starts with no recovered memories; NG+ keeps them across the cycle.
/// A companion's disabled spells and recruit time are kept by the save, single player (file) and online (SQL);
/// a save written without them loads as before.
/// A session's shared save no longer writes its story into the shared story_systems key; the read stays, so
/// LoadAllSharedState decides foundState as before, on a database that holds the key and on a fresh one.
/// </summary>
[Collection("SharedGameSingletons")]
public class CA140Carries : IDisposable
{
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags INP = BindingFlags.NonPublic | BindingFlags.Instance;

    // every fallback singleton RestoreStorySystems or the resets write in these tests
    private static readonly Type[] Swapped =
    {
        typeof(StoryProgressionSystem), typeof(OceanPhilosophySystem), typeof(TownNPCStorySystem),
        typeof(StrangerEncounterSystem), typeof(DreamSystem), typeof(GriefSystem), typeof(MoralParadoxSystem),
        typeof(BetrayalSystem), typeof(AmnesiaSystem), typeof(CompanionSystem), typeof(FactionSystem), typeof(ArchetypeTracker),
    };

    // fallback singletons a new SessionContext's InitializeSystems also replaces
    private static readonly Type[] SessionTouched =
    {
        typeof(CycleDialogueSystem), typeof(IntimacySystem), typeof(RomanceTracker),
    };

    private static readonly FieldInfo MemeField = typeof(CulturalMemeSystem).GetField("_instance", SNP)!;
    private static readonly FieldInfo GodsField = typeof(GodSystemSingleton).GetField("_instance", SNP)!;

    private static readonly DateTime Recruited = new(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
    private const string PlayerName = "ca140player";

    private readonly List<(FieldInfo Field, object? Value)> _old;
    private readonly object? _oldMeme;
    private readonly object? _oldGods;
    private readonly SessionContext? _oldSession;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"usurper-ca140-{Guid.NewGuid():N}");
    private string DbPath => Path.Combine(_dir, "ca140.db");

    public CA140Carries()
    {
        Directory.CreateDirectory(_dir);
        _old = Swapped.Concat(SessionTouched).Select(t => t.GetField("_fallbackInstance", SNP)!).Select(f => (f, f.GetValue(null))).ToList();
        _oldMeme = MemeField.GetValue(null);
        _oldGods = GodsField.GetValue(null);
        _oldSession = SessionContext.Current;
        SessionContext.Current = null;
    }

    public void Dispose()
    {
        SessionContext.Current = null;
        foreach (var (f, v) in _old) f.SetValue(null, v);
        MemeField.SetValue(null, _oldMeme);
        GodsField.SetValue(null, _oldGods);
        SessionContext.Current = _oldSession;
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    // ---------- helpers ----------

    private static void FreshProcess()
    {
        SessionContext.Current = null;
        MemeField.SetValue(null, null);
        GodsField.SetValue(null, new GodSystem());
        foreach (var t in Swapped) t.GetField("_fallbackInstance", SNP)!.SetValue(null, Activator.CreateInstance(t));
    }

    private static string Fields(object o) =>
        string.Join(";", o.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .OrderBy(f => f.Name, StringComparer.Ordinal)
            .Select(f => f.GetValue(f.IsStatic ? null : o) is System.Collections.IEnumerable e && f.FieldType != typeof(string)
                ? $"{f.Name}:[{string.Join(",", e.Cast<object>().Select(x => x.ToString()).OrderBy(s => s, StringComparer.Ordinal))}]"
                : $"{f.Name}:{f.GetValue(o)}"));

    private static AmnesiaData Memories() => new()
    {
        RecoveredMemories = new() { MemoryFragment.Emptiness, MemoryFragment.TheFirstThought, MemoryFragment.CreatingLight },
        ExperiencedDreams = new() { DreamSequence.Drowning, DreamSequence.TheMirror },
        RestCount = 9,
        TruthRevealed = true,
    };

    private static string Source(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { UsurperReborn.Tests.Localization.HardcodedTextScannerTests.RepoRoot() }.Concat(parts).ToArray()));

    private static string Method(string src, string signature)
    {
        int start = src.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BePositive(signature);
        int brace = src.IndexOf('{', start);
        int depth = 0;
        for (int i = brace; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0) return src.Substring(start, i - start + 1);
        }
        throw new InvalidOperationException(signature);
    }

    /// <summary>CreateNewGame split at its NG+ branch: before the else, the else block (not NG+), and after that block.</summary>
    private static (string NgPlus, string NotNgPlus, string After) CreateNewGameBranches()
    {
        var body = Method(Source("Scripts", "Core", "GameEngine.cs"), "private async Task CreateNewGame(");
        int ngPlus = body.IndexOf("if (isNgPlus)", StringComparison.Ordinal);
        ngPlus.Should().BePositive();
        int notNgPlus = body.IndexOf("else", ngPlus, StringComparison.Ordinal);
        notNgPlus.Should().BeLessThan(body.IndexOf("NOT NG+", StringComparison.Ordinal), "the else is the branch that is not NG+");
        int end = BlockEnd(body, body.IndexOf('{', notNgPlus));
        return (body.Substring(0, notNgPlus), body.Substring(notNgPlus, end - notNgPlus), body.Substring(end));
    }

    /// <summary>The index just past the brace that closes the block opened at <paramref name="open"/>.</summary>
    private static int BlockEnd(string src, int open)
    {
        int depth = 0;
        for (int i = open; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0) return i + 1;
        }
        throw new InvalidOperationException("no closing brace");
    }

    // ---------- 1: memories at a new character ----------

    [Fact]
    public void NewCharacter_NotNgPlus_ResetsTheMemories()
    {
        var (_, notNgPlus, _) = CreateNewGameBranches();
        notNgPlus.Should().Contain("UsurperRemake.Systems.AmnesiaSystem.Instance.Reset();");
        notNgPlus.Should().Contain("StoryProgressionSystem.Instance.FullReset();");

        // what that reset leaves: no memories, no dreams, rest count 0, truth not revealed
        FreshProcess();
        var amnesia = AmnesiaSystem.Instance;
        amnesia.Deserialize(Memories());
        amnesia.Reset();
        amnesia.RecoveredMemories.Should().BeEmpty();
        amnesia.ExperiencedDreams.Should().BeEmpty();
        amnesia.RestCount.Should().Be(0);
        amnesia.TruthRevealed.Should().BeFalse();
        Fields(amnesia).Should().Be(Fields(new AmnesiaSystem()));
    }

    [Fact]
    public void NgPlus_KeepsTheMemories()
    {
        var (ngPlus, _, after) = CreateNewGameBranches();
        ngPlus.Should().NotContain("AmnesiaSystem", "NG+ keeps the memories");
        after.Should().NotContain("AmnesiaSystem", "the reset sits in the branch that is not NG+ only");

        // the steps NG+ runs (the cycle start and the narrative reset every new game runs) leave the memories
        FreshProcess();
        var amnesia = AmnesiaSystem.Instance;
        amnesia.Deserialize(Memories());
        var before = Fields(amnesia);
        StoryProgressionSystem.Instance.StartNewCycle(EndingType.Savior);
        GameEngine.ResetNarrativeSystemsForNewGame();
        UsurperRemake.Systems.ArchetypeTracker.Instance.Reset();
        UsurperRemake.Systems.FactionSystem.Instance.Reset();
        Fields(AmnesiaSystem.Instance).Should().Be(before);
        AmnesiaSystem.Instance.TruthRevealed.Should().BeTrue();
    }

    // ---------- 2: the two companion settings ----------

    private static readonly JsonSerializerOptions SaveJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IncludeFields = true,
    };

    private static SaveGameData SaveOf(StorySystemsData story) => new()
    {
        Version = GameConfig.SaveVersion,
        SaveTime = DateTime.Now,
        Player = new PlayerData { Name1 = PlayerName, Name2 = PlayerName, Level = 20 },
        StorySystems = story,
    };

    /// <summary>Aldric recruited, two spells off, recruited at a known time; the save of it.</summary>
    private static SaveGameData SaveWithSettings()
    {
        var aldric = CompanionSystem.Instance.GetCompanion(CompanionId.Aldric)!;
        aldric.IsRecruited = true;
        aldric.Level = 20;
        aldric.DisabledSpells.Add("ca_spell_one");
        aldric.DisabledSpells.Add("ca_spell_two");
        aldric.RecruitedDate = Recruited;
        return SaveOf(SaveSystem.Instance.SerializeStorySystemsPublic());
    }

    private static void SettingsCameBack(Companion aldric)
    {
        aldric.IsRecruited.Should().BeTrue();
        aldric.DisabledSpells.Should().BeEquivalentTo(new[] { "ca_spell_one", "ca_spell_two" });
        aldric.RecruitedDate.Should().Be(Recruited);
    }

    private FileSaveBackend FileBackend()
    {
        var backend = new FileSaveBackend();
        typeof(FileSaveBackend).GetField("baseSaveDirectory", INP)!.SetValue(backend, _dir);
        return backend;
    }

    [Fact]
    public void Conversion_CarriesBothSettings()
    {
        FreshProcess();
        var data = SaveWithSettings().StorySystems;
        var info = data.Companions.Single(c => c.Id == (int)CompanionId.Aldric);
        info.DisabledSpells.Should().BeEquivalentTo(new[] { "ca_spell_one", "ca_spell_two" }, "the save writes the spells");
        info.RecruitedDate.Should().Be(Recruited, "the save writes the recruit time");

        FreshProcess();
        SaveSystem.Instance.RestoreStorySystems(data);
        SettingsCameBack(CompanionSystem.Instance.GetCompanion(CompanionId.Aldric)!);
    }

    [Fact]
    public async Task SinglePlayer_FileSave_RoundTripsBothSettings()
    {
        FreshProcess();
        var backend = FileBackend();
        (await backend.WriteGameData(PlayerName, SaveWithSettings())).Should().BeTrue();
        var loaded = await backend.ReadGameData(PlayerName);
        FreshProcess();
        SaveSystem.Instance.RestoreStorySystems(loaded!.StorySystems);
        SettingsCameBack(CompanionSystem.Instance.GetCompanion(CompanionId.Aldric)!);
    }

    [Fact]
    public async Task Online_SqlSave_RoundTripsBothSettings()
    {
        FreshProcess();
        var writer = new SessionContext { Username = PlayerName, CharacterKey = PlayerName };
        writer.InitializeSystems();
        var reader = new SessionContext { Username = PlayerName, CharacterKey = PlayerName };
        reader.InitializeSystems();
        try
        {
            SessionContext.Current = writer;
            var data = SaveWithSettings();
            var backend = new SqlSaveBackend(DbPath);
            (await backend.WriteGameData(PlayerName, data)).Should().BeTrue();
            var loaded = await backend.ReadGameData(PlayerName);

            SessionContext.Current = reader;
            reader.Companions.GetCompanion(CompanionId.Aldric)!.DisabledSpells.Should().BeEmpty("the reading session starts without them");
            SaveSystem.Instance.RestoreStorySystems(loaded!.StorySystems);
            SettingsCameBack(reader.Companions.GetCompanion(CompanionId.Aldric)!);
        }
        finally { SessionContext.Current = null; }
    }

    /// <summary>The save's JSON with every companion's disabled spells and recruit time taken out, as an older save has it.</summary>
    private static string WithoutSettings(string json)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        var story = root.First(p => p.Key.Equals("storySystems", StringComparison.OrdinalIgnoreCase)).Value!.AsObject();
        var companions = story.First(p => p.Key.Equals("companions", StringComparison.OrdinalIgnoreCase)).Value!.AsArray();
        companions.Count.Should().BePositive();
        foreach (var node in companions)
        {
            var c = node!.AsObject();
            foreach (var name in new[] { "disabledSpells", "recruitedDate" })
            {
                var key = c.Select(p => p.Key).Single(k => k.Equals(name, StringComparison.OrdinalIgnoreCase));
                c.Remove(key);
            }
        }
        return root.ToJsonString();
    }

    private static void OldSaveLoadsAsBefore(SaveGameData loaded, DateTime from)
    {
        FreshProcess();
        SaveSystem.Instance.RestoreStorySystems(loaded.StorySystems);
        var aldric = CompanionSystem.Instance.GetCompanion(CompanionId.Aldric)!;
        aldric.IsRecruited.Should().BeTrue();
        aldric.DisabledSpells.Should().BeEmpty("a save without the field has no spell off, as before");
        aldric.RecruitedDate.Should().BeOnOrAfter(from, "a save without the field is stamped with the clock (CompanionSystem.Deserialize)");
        aldric.RecruitedDate.Should().BeOnOrBefore(DateTime.UtcNow);
    }

    [Fact]
    public void OldSave_WithoutTheSettings_LoadsAsBefore()
    {
        FreshProcess();
        var json = JsonSerializer.Serialize(SaveWithSettings(), SaveJson);
        var loaded = JsonSerializer.Deserialize<SaveGameData>(WithoutSettings(json), SaveJson)!;
        var info = loaded.StorySystems.Companions.Single(c => c.Id == (int)CompanionId.Aldric);
        info.DisabledSpells.Should().BeEmpty();
        info.RecruitedDate.Should().Be(DateTime.MinValue);
        OldSaveLoadsAsBefore(loaded, DateTime.UtcNow);
    }

    [Fact]
    public async Task OldSqlSave_WithoutTheSettings_LoadsAsBefore()
    {
        FreshProcess();
        var backend = new SqlSaveBackend(DbPath);
        (await backend.WriteGameData(PlayerName, SaveWithSettings())).Should().BeTrue();
        using (var conn = new SqliteConnection($"Data Source={DbPath}"))
        {
            conn.Open();
            using var read = conn.CreateCommand();
            read.CommandText = "SELECT player_data FROM players WHERE LOWER(username) = LOWER(@u);";
            read.Parameters.AddWithValue("@u", PlayerName);
            var json = (string)read.ExecuteScalar()!;
            using var write = conn.CreateCommand();
            write.CommandText = "UPDATE players SET player_data = @d WHERE LOWER(username) = LOWER(@u);";
            write.Parameters.AddWithValue("@d", WithoutSettings(json));
            write.Parameters.AddWithValue("@u", PlayerName);
            write.ExecuteNonQuery().Should().Be(1);
        }
        var from = DateTime.UtcNow;
        OldSaveLoadsAsBefore((await backend.ReadGameData(PlayerName))!, from);
    }

    // ---------- 3: the shared story_systems key ----------

    private static OnlineStateManager Osm(SqlSaveBackend db) =>
        (OnlineStateManager)Activator.CreateInstance(typeof(OnlineStateManager), INP, null, new object[] { db, "ca140_session" }, null)!;

    [Fact]
    public async Task SharedSave_DoesNotWriteTheStoryKey_FreshDatabase()
    {
        FreshProcess();
        var db = new SqlSaveBackend(DbPath);
        var osm = Osm(db);
        (await osm.LoadAllSharedState()).Should().BeFalse("a fresh database holds no shared state, before and after 1.3.8");

        AmnesiaSystem.Instance.Deserialize(Memories());   // a session's story, kept in its own save
        await osm.SaveAllSharedState();
        (await db.LoadWorldState(OnlineStateManager.KEY_STORY_SYSTEMS)).Should().BeNullOrEmpty("a session's story is not written to the shared key");
        (await db.LoadWorldState(OnlineStateManager.KEY_WORLD_EVENTS)).Should().NotBeNullOrEmpty("the same save writes the world events");
        (await osm.LoadAllSharedState()).Should().BeTrue("a database a shared save has written reads as state, as when it held the key");
    }

    [Fact]
    public async Task SharedSave_LeavesAnExistingStoryKey_AndItStillReadsAsState()
    {
        FreshProcess();
        var db = new SqlSaveBackend(DbPath);
        var older = JsonSerializer.Serialize(new StorySystemsData { CurrentCycle = 4 });
        await db.SaveWorldState(OnlineStateManager.KEY_STORY_SYSTEMS, older);
        var osm = Osm(db);
        (await osm.LoadAllSharedState()).Should().BeTrue("the key an older version wrote still reads as state");

        long version = db.GetWorldStateVersion(OnlineStateManager.KEY_STORY_SYSTEMS);
        await osm.SaveAllSharedState();
        (await db.LoadWorldState(OnlineStateManager.KEY_STORY_SYSTEMS)).Should().Be(older, "the stored key is kept as it is");
        db.GetWorldStateVersion(OnlineStateManager.KEY_STORY_SYSTEMS).Should().Be(version, "and is not written again");
        (await osm.LoadAllSharedState()).Should().BeTrue();
    }

    [Fact]
    public void NoCodeWritesTheStoryKey()
    {
        var root = UsurperReborn.Tests.Localization.HardcodedTextScannerTests.RepoRoot();
        var calls = Directory.GetFiles(Path.Combine(root, "Scripts"), "*.cs", SearchOption.AllDirectories)
            .SelectMany(f => File.ReadAllLines(f).Where(l => l.Contains("SaveSharedStorySystems(") && !l.TrimStart().StartsWith("//"))
                .Select(l => $"{Path.GetFileName(f)}|{l.Trim()}"))
            .ToList();
        calls.Should().BeEquivalentTo(new[] { "OnlineStateManager.cs|public async Task SaveSharedStorySystems(StorySystemsData storyData)" },
            "the definition only, with no call");
        Method(Source("Scripts", "Systems", "OnlineStateManager.cs"), "public async Task SaveAllSharedState()")
            .Should().NotContain("SerializeStorySystemsPublic").And.NotContain("KEY_STORY_SYSTEMS");
        Method(Source("Scripts", "Systems", "OnlineStateManager.cs"), "public async Task<bool> LoadAllSharedState()")
            .Should().Contain("await LoadSharedStorySystems();", "the read stays");
    }
}
