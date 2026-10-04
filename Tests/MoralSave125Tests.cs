using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.5: the moral-type counters (how often each kind of moral answer was chosen) are kept in the save.
/// They survive a save and reload through the file save and the SQL save, an old save without them loads
/// with all four at zero, a new character starts at zero, and loading save B after save A gives B's counters.
/// </summary>
[Collection("SharedGameSingletons")]
public class MoralSave125Tests : IDisposable
{
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private const string Paradox = "destroy_darkness";
    private const string PlayerName = "MoralSaveHero";

    private static readonly string[] CounterKeys =
        { "moralUtilitarianChoices", "moralDeontologicalChoices", "moralVirtueChoices", "moralNihilistChoices" };

    private static readonly FieldInfo StoryField = typeof(StoryProgressionSystem).GetField("_fallbackInstance", SNP)!;
    private static readonly FieldInfo OceanField = typeof(OceanPhilosophySystem).GetField("_fallbackInstance", SNP)!;
    private static readonly FieldInfo AmnesiaField = typeof(AmnesiaSystem).GetField("_fallbackInstance", SNP)!;
    private static readonly FieldInfo MoralField = typeof(MoralParadoxSystem).GetField("_fallbackInstance", SNP)!;
    private readonly object? _oldStory = StoryField.GetValue(null);
    private readonly object? _oldOcean = OceanField.GetValue(null);
    private readonly object? _oldAmnesia = AmnesiaField.GetValue(null);
    private readonly object? _oldMoral = MoralField.GetValue(null);
    private readonly string _language = GameConfig.Language;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"usurper-moral-{Guid.NewGuid():N}");
    private string DbPath => Path.Combine(_dir, "saves.db");

    public MoralSave125Tests()
    {
        Directory.CreateDirectory(_dir);
        GameConfig.Language = "en";
    }

    public void Dispose()
    {
        StoryField.SetValue(null, _oldStory);
        OceanField.SetValue(null, _oldOcean);
        AmnesiaField.SetValue(null, _oldAmnesia);
        MoralField.SetValue(null, _oldMoral);
        GameConfig.Language = _language;
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    // ---------- helpers ----------

    private static Character Hero() => new()
    {
        Name1 = PlayerName, Name2 = PlayerName, Class = CharacterClass.Warrior, Level = 100, HP = 5000, MaxHP = 5000,
        AI = CharacterAI.Human, Gold = 1000, Chivalry = 300, Darkness = 300, Wisdom = 40, BaseWisdom = 40,
    };

    /// <summary>A world where the floor 95 paradox is on offer: the Ascension chapter and the Sunforged Blade.</summary>
    private static void FreshWorld()
    {
        var story = new StoryProgressionSystem();
        story.AdvanceChapter(StoryChapter.Ascension);
        story.CollectArtifact(ArtifactType.SunforgedBlade);
        StoryField.SetValue(null, story);
        var ocean = new OceanPhilosophySystem();
        ocean.RestoreFromSave(Array.Empty<WaveFragment>(), Array.Empty<AwakeningMoment>(), null, 3);
        OceanField.SetValue(null, ocean);
        AmnesiaField.SetValue(null, new AmnesiaSystem());
        MoralField.SetValue(null, new MoralParadoxSystem());
    }

    private static (int U, int D, int V, int N) Counters()
    {
        var m = MoralParadoxSystem.Instance;
        return (m.UtilitarianChoices, m.DeontologicalChoices, m.VirtueChoices, m.NihilistChoices);
    }

    private static SaveGameData Snapshot(string name) => new()
    {
        Version = GameConfig.SaveVersion,
        SaveTime = DateTime.Now,
        Player = new PlayerData { Name1 = name, Name2 = name, Level = 100 },
        StorySystems = SaveSystem.Instance.SerializeStorySystemsPublic(),
    };

    /// <summary>
    /// Earlier answers counted (2, 3, 5, 7), then the floor 95 paradox is answered through its screen,
    /// which adds one more. Returns the counters as they stand and the save data.
    /// </summary>
    private static ((int U, int D, int V, int N) Counters, SaveGameData Data) AnswerAndSave()
    {
        var hero = Hero();
        MoralParadoxSystem.Instance.RestoreMoralCounters(2, 3, 5, 7);
        var lines = new[] { "3" }.Concat(Enumerable.Repeat("", 60));
        var term = new TerminalEmulator(new LineStream(lines, _ => { }), new MemoryStream());
        var choice = MoralParadoxSystem.Instance.PresentParadox(Paradox, hero, term).GetAwaiter().GetResult();
        choice.Should().NotBeNull();
        var counters = Counters();
        (counters.U + counters.D + counters.V + counters.N).Should().Be(18, "the answer added one to its moral type");
        return (counters, Snapshot(PlayerName));
    }

    private static void Reload(SaveGameData? loaded)
    {
        loaded.Should().NotBeNull("the save reads back");
        FreshWorld();
        Counters().Should().Be((0, 0, 0, 0), "a fresh session starts at zero");
        SaveSystem.Instance.RestoreStorySystems(loaded!.StorySystems);
    }

    private FileSaveBackend FileBackend()
    {
        var backend = new FileSaveBackend();
        typeof(FileSaveBackend).GetField("baseSaveDirectory", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(backend, _dir);
        return backend;
    }

    private static JsonObject WithoutCounters(string json)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        var story = root["storySystems"]!.AsObject();
        foreach (var key in CounterKeys)
        {
            story.ContainsKey(key).Should().BeTrue($"the save writes {key}");
            story.Remove(key);
        }
        return root;
    }

    private static void OldSaveLoads_WithZeros(SaveGameData? loaded)
    {
        loaded.Should().NotBeNull("an old save without the counters still loads");
        FreshWorld();
        // the session had counted answers in memory; the load replaces them with the save's (none)
        MoralParadoxSystem.Instance.RestoreMoralCounters(4, 4, 4, 4);
        var act = () => SaveSystem.Instance.RestoreStorySystems(loaded!.StorySystems);
        act.Should().NotThrow();
        Counters().Should().Be((0, 0, 0, 0));
        MoralParadoxSystem.Instance.GetDominantMoralType().Should().Be(MoralType.Virtue, "no answer counted reads as the default type");
    }

    private static readonly Type[] NarrativeSystems =
        { typeof(StrangerEncounterSystem), typeof(TownNPCStorySystem), typeof(DreamSystem), typeof(GriefSystem) };

    // ---------- tests ----------

    [Fact]
    public async Task FileSave_MoralCounters_SurviveReload()
    {
        FreshWorld();
        var (counters, data) = AnswerAndSave();
        var backend = FileBackend();
        (await backend.WriteGameData(PlayerName, data)).Should().BeTrue();
        Reload(await backend.ReadGameData(PlayerName));
        Counters().Should().Be(counters);
    }

    [Fact]
    public async Task SqlSave_MoralCounters_SurviveReload()
    {
        FreshWorld();
        var (counters, data) = AnswerAndSave();
        var backend = new SqlSaveBackend(DbPath);
        (await backend.WriteGameData(PlayerName, data)).Should().BeTrue();
        Reload(await backend.ReadGameData(PlayerName));
        Counters().Should().Be(counters);
    }

    [Fact]
    public async Task OldFileSave_WithoutCounters_LoadsWithZeros()
    {
        FreshWorld();
        var (_, data) = AnswerAndSave();
        var backend = FileBackend();
        (await backend.WriteGameData(PlayerName, data)).Should().BeTrue();
        var file = Directory.GetFiles(_dir, "*.json").Single();
        File.WriteAllText(file, WithoutCounters(File.ReadAllText(file)).ToJsonString());
        OldSaveLoads_WithZeros(await backend.ReadGameData(PlayerName));
    }

    [Fact]
    public async Task OldSqlSave_WithoutCounters_LoadsWithZeros()
    {
        FreshWorld();
        var (_, data) = AnswerAndSave();
        var backend = new SqlSaveBackend(DbPath);
        (await backend.WriteGameData(PlayerName, data)).Should().BeTrue();

        using (var conn = new SqliteConnection($"Data Source={DbPath}"))
        {
            conn.Open();
            using var read = conn.CreateCommand();
            read.CommandText = "SELECT player_data FROM players WHERE LOWER(username) = LOWER(@u);";
            read.Parameters.AddWithValue("@u", PlayerName);
            var json = (string)read.ExecuteScalar()!;
            using var write = conn.CreateCommand();
            write.CommandText = "UPDATE players SET player_data = @d WHERE LOWER(username) = LOWER(@u);";
            write.Parameters.AddWithValue("@d", WithoutCounters(json).ToJsonString());
            write.Parameters.AddWithValue("@u", PlayerName);
            write.ExecuteNonQuery().Should().Be(1);
        }

        OldSaveLoads_WithZeros(await backend.ReadGameData(PlayerName));
    }

    [Fact]
    public void NewGame_ResetsTheCounters()
    {
        var fields = NarrativeSystems.Select(t => t.GetField("_fallbackInstance", SNP)!).ToList();
        var old = fields.Select(f => f.GetValue(null)).ToList();
        try
        {
            foreach (var (f, t) in fields.Zip(NarrativeSystems)) f.SetValue(null, Activator.CreateInstance(t));

            FreshWorld();
            AnswerAndSave();
            Counters().Should().NotBe((0, 0, 0, 0), "character A has answers counted");

            GameEngine.ResetNarrativeSystemsForNewGame();

            Counters().Should().Be((0, 0, 0, 0), "character B starts with no answers counted");
        }
        finally
        {
            for (int i = 0; i < fields.Count; i++) fields[i].SetValue(null, old[i]);
        }
    }

    [Fact]
    public async Task LoadingAnotherSave_Replaces_TheCounters()
    {
        var backend = FileBackend();

        FreshWorld();
        var (countersA, dataA) = AnswerAndSave();
        (await backend.WriteGameData("MoralSaveA", dataA)).Should().BeTrue();

        FreshWorld();
        MoralParadoxSystem.Instance.RestoreMoralCounters(1, 0, 0, 0);
        (await backend.WriteGameData("MoralSaveB", Snapshot("MoralSaveB"))).Should().BeTrue();

        SaveSystem.Instance.RestoreStorySystems((await backend.ReadGameData("MoralSaveA"))!.StorySystems);
        Counters().Should().Be(countersA);
        SaveSystem.Instance.RestoreStorySystems((await backend.ReadGameData("MoralSaveB"))!.StorySystems);
        Counters().Should().Be((1, 0, 0, 0), "a load replaces the counters, it does not add to them");
    }

    [Fact]
    public void Restore_ReadsANegativeValue_AsZero()
    {
        FreshWorld();
        MoralParadoxSystem.Instance.RestoreMoralCounters(-3, 2, -1, 0);
        Counters().Should().Be((0, 2, 0, 0));
    }
}
