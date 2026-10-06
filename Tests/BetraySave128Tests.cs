using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.8 betrayal piece 1: the betrayal state is saved and restored (file and SQL), a save written before
/// 1.2.8 loads clean and is seeded only from finished story beats in its flags (no points, nothing pending),
/// a new character starts without it, revenge is once and only on the Stranger and counts no kill,
/// forgiveness is once, the Stranger costs no gold, the Lyris scene grants nothing twice, the state is
/// written before the scene, and the system writes no news. Nothing in the game calls the scenes yet.
/// </summary>
[Collection("SharedGameSingletons")]
public class BetraySave128Tests : IDisposable
{
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags INP = BindingFlags.NonPublic | BindingFlags.Instance;
    private const string PlayerName = "BetraySaveHero";

    private static readonly Type[] Swapped =
    {
        typeof(StoryProgressionSystem), typeof(OceanPhilosophySystem), typeof(AmnesiaSystem), typeof(MoralParadoxSystem),
        typeof(BetrayalSystem), typeof(CompanionSystem), typeof(StrangerEncounterSystem), typeof(TownNPCStorySystem),
        typeof(DreamSystem), typeof(GriefSystem),
    };
    private readonly List<(FieldInfo Field, object? Old)> _old;
    private readonly string _language = GameConfig.Language;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"usurper-betray-{Guid.NewGuid():N}");
    private string DbPath => Path.Combine(_dir, "saves.db");

    public BetraySave128Tests()
    {
        _old = Swapped.Select(t => t.GetField("_fallbackInstance", SNP)!).Select(f => (f, f.GetValue(null))).ToList();
        Directory.CreateDirectory(_dir);
        GameConfig.Language = "en";
    }

    public void Dispose()
    {
        foreach (var (f, v) in _old) f.SetValue(null, v);
        GameConfig.Language = _language;
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    // ---------- helpers ----------

    private sealed class Screen
    {
        public TerminalEmulator Term = null!;
        public MemoryStream Output = new();
        public string Text
        {
            get
            {
                Term.StreamWriterInternal?.Flush();
                return Regex.Replace(Encoding.UTF8.GetString(Output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "");
            }
        }
    }

    private static Screen NewScreen()
    {
        var s = new Screen();
        s.Term = new TerminalEmulator(new LineStream(Enumerable.Repeat("", 60), i =>
        {
            if (i >= 40) throw new InvalidOperationException("the screen asked for input more than 40 times");
            s.Term.StreamWriterInternal?.Flush();
            s.Output.WriteByte((byte)'\n');
        }), s.Output);
        return s;
    }

    /// <summary>A screen whose player drops at the first key prompt.</summary>
    private static Screen DroppingScreen()
    {
        var s = new Screen();
        s.Term = new TerminalEmulator(new LineStream(Enumerable.Repeat("", 5), _ =>
            throw new IOException("the player dropped at the key prompt")), s.Output);
        return s;
    }

    private static Character Hero() => new()
    {
        Name1 = PlayerName, Name2 = PlayerName, Class = CharacterClass.Warrior, Level = 100, HP = 5000, MaxHP = 5000,
        AI = CharacterAI.Human, Gold = 1000, Chivalry = 300, Darkness = 0, Wisdom = 40, BaseWisdom = 40, MKills = 7,
    };

    /// <summary>Fresh story, ocean, amnesia, paradox and companion singletons; a fresh betrayal system unless kept.</summary>
    private static void FreshWorld(bool keepBetrayal = false)
    {
        typeof(StoryProgressionSystem).GetField("_fallbackInstance", SNP)!.SetValue(null, new StoryProgressionSystem());
        var ocean = new OceanPhilosophySystem();
        ocean.RestoreFromSave(Array.Empty<WaveFragment>(), Array.Empty<AwakeningMoment>(), null, 0);
        typeof(OceanPhilosophySystem).GetField("_fallbackInstance", SNP)!.SetValue(null, ocean);
        typeof(AmnesiaSystem).GetField("_fallbackInstance", SNP)!.SetValue(null, new AmnesiaSystem());
        typeof(MoralParadoxSystem).GetField("_fallbackInstance", SNP)!.SetValue(null, new MoralParadoxSystem());
        typeof(CompanionSystem).GetField("_fallbackInstance", SNP)!.SetValue(null, new CompanionSystem());
        if (!keepBetrayal) typeof(BetrayalSystem).GetField("_fallbackInstance", SNP)!.SetValue(null, new BetrayalSystem());
    }

    private static BetrayalSystem B => BetrayalSystem.Instance;
    private static StoryProgressionSystem Story => StoryProgressionSystem.Instance;

    private static Dictionary<string, BetrayalProfile> Profiles(BetrayalSystem s) =>
        (Dictionary<string, BetrayalProfile>)typeof(BetrayalSystem).GetField("betrayalProfiles", INP)!.GetValue(s)!;

    private static List<ActiveBetrayal> Records(BetrayalSystem s) =>
        (List<ActiveBetrayal>)typeof(BetrayalSystem).GetField("activeBetrays", INP)!.GetValue(s)!;

    private static readonly string[] StoryIds = { "TheStranger", "TeamBetrayal", "RomanticBetrayal", "KingsAdvisor", "Lyris" };

    /// <summary>Makes a betrayer pending and runs its scene to the end.</summary>
    private static void Betray(string id, Character hero)
    {
        Profiles(B)[id].IsPendingBetrayal = true;
        B.ExecuteBetrayal(id, hero, NewScreen().Term).GetAwaiter().GetResult().Occurred.Should().BeTrue($"{id} betrays");
    }

    private static string Revenge(string id, Character hero)
    {
        var s = NewScreen();
        B.TakeRevenge(id, hero, s.Term).GetAwaiter().GetResult();
        return s.Text;
    }

    private static string StateJson(BetrayalSystem s) => JsonSerializer.Serialize(s.Serialize());

    private static readonly JsonSerializerOptions SaveJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IncludeFields = true,
    };

    private static SaveGameData SaveOf(StorySystemsData story) => new()
    {
        Version = GameConfig.SaveVersion,
        SaveTime = DateTime.Now,
        Player = new PlayerData { Name1 = PlayerName, Name2 = PlayerName, Level = 100 },
        StorySystems = story,
    };

    private FileSaveBackend FileBackend()
    {
        var backend = new FileSaveBackend();
        typeof(FileSaveBackend).GetField("baseSaveDirectory", INP)!.SetValue(backend, _dir);
        return backend;
    }

    private static JsonObject WithoutBetrayal(string json)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        var story = root["storySystems"]!.AsObject();
        story.ContainsKey("betrayal").Should().BeTrue("the save writes the field");
        story.Remove("betrayal");
        return root;
    }

    /// <summary>
    /// A character with betrayal state in the session (a pending beloved, team points, a departed companion),
    /// saved with a companion at loyalty 0. Returns the save; the session keeps the state.
    /// </summary>
    private static SaveGameData PriorCharacterSave()
    {
        FreshWorld();
        B.RecordInfidelity("someone");
        B.AddBetrayalPoints("TeamBetrayal", 90, "grievance");
        B.DepartedCompanionId = CompanionId.Vex;
        B.HasPendingBetrayal("RomanticBetrayal").Should().BeTrue();
        var story = SaveSystem.Instance.SerializeStorySystemsPublic();
        var aldric = story.Companions.Single(c => c.Id == (int)CompanionId.Aldric);
        aldric.IsRecruited = true;
        aldric.LoyaltyLevel = 0;
        return SaveOf(story);
    }

    /// <summary>The loaded old save: low loyalty came back, and the betrayal state is clean.</summary>
    private static void OldSaveLoadsClean(SaveGameData? loaded)
    {
        loaded.Should().NotBeNull("an old save without the field still loads");
        loaded!.StorySystems.Betrayal.Should().BeNull("the save has no betrayal state");
        // a new session in the same process: the betrayal system still holds the character before
        FreshWorld(keepBetrayal: true);
        B.HasPendingBetrayal("RomanticBetrayal").Should().BeTrue("the session held another character's pending betrayal");
        var act = () => SaveSystem.Instance.RestoreStorySystems(loaded.StorySystems);
        act.Should().NotThrow();

        CompanionSystem.Instance.GetCompanion(CompanionId.Aldric)!.LoyaltyLevel.Should().Be(0, "the old save is at low loyalty");
        foreach (var p in Profiles(B).Values)
        {
            p.BetrayalPoints.Should().Be(0, $"{p.NPCId} counts from the update forward");
            p.IsPendingBetrayal.Should().BeFalse($"{p.NPCId} is not pending after an old save");
            p.HasBetrayed.Should().BeFalse();
        }
        Profiles(B).Keys.Should().BeEquivalentTo(StoryIds);
        Records(B).Should().BeEmpty();
        B.DepartedCompanionId.Should().BeNull();
    }

    // ---------- row 1: the state round trips through the save ----------

    [Fact]
    public void TheBetrayalState_RoundTrips_ThroughTheSave()
    {
        FreshWorld();
        var hero = Hero();
        Story.CurrentGameDay = 12;
        B.TriggerStrangerRevelation();
        B.ExecuteBetrayal("TheStranger", hero, NewScreen().Term).GetAwaiter().GetResult().Occurred.Should().BeTrue();
        Revenge("TheStranger", hero);
        Story.CurrentGameDay = 15;
        B.AddBetrayalPoints("TeamBetrayal", 60, "grievance");
        B.ReduceBetrayalPoints("TeamBetrayal", 10, "kindness");
        Profiles(B)["TeamBetrayal"].WarningShown = true;
        B.RecordInfidelity("someone");
        B.AddBetrayalPoints("some_npc", 20, "grievance");
        B.DepartedCompanionId = CompanionId.Mira;
        var before = StateJson(B);

        var json = JsonSerializer.Serialize(SaveOf(SaveSystem.Instance.SerializeStorySystemsPublic()), SaveJson);
        json.Should().Contain("\"betrayal\":");
        var loaded = JsonSerializer.Deserialize<SaveGameData>(json, SaveJson)!;
        FreshWorld();
        SaveSystem.Instance.RestoreStorySystems(loaded.StorySystems);

        StateJson(B).Should().Be(before, "every saved field comes back");
        var team = Profiles(B)["TeamBetrayal"];
        team.BetrayalPoints.Should().Be(50);
        team.WarningShown.Should().BeTrue();
        team.ActsOfKindness.Should().ContainSingle().Which.GameDay.Should().Be(15, "a kindness keeps its day");
        var romantic = Profiles(B)["RomanticBetrayal"];
        romantic.IsPendingBetrayal.Should().BeTrue();
        romantic.TriggerReason.Should().NotBeEmpty();
        Profiles(B)["some_npc"].BetrayalPoints.Should().Be(20);
        var record = Records(B).Should().ContainSingle().Subject;
        record.NPCId.Should().Be("TheStranger");
        record.NPCName.Should().Be(Profiles(B)["TheStranger"].NPCName, "the stored name is the English one");
        record.Day.Should().Be(12);
        record.IsResolved.Should().BeTrue();
        record.Resolution.Should().Be(BetrayalResolution.Avenged);
        Profiles(B)["TheStranger"].HasBetrayed.Should().BeTrue();
        B.DepartedCompanionId.Should().Be(CompanionId.Mira);
    }

    // ---------- row 2: an old save loads clean, file and SQL ----------

    [Fact]
    public async Task OldFileSave_WithoutBetrayal_LoadsClean()
    {
        var data = PriorCharacterSave();
        var backend = FileBackend();
        (await backend.WriteGameData(PlayerName, data)).Should().BeTrue();
        var file = Directory.GetFiles(_dir, "*.json").Single();
        File.WriteAllText(file, WithoutBetrayal(File.ReadAllText(file)).ToJsonString());

        OldSaveLoadsClean(await backend.ReadGameData(PlayerName));
    }

    [Fact]
    public async Task OldSqlSave_WithoutBetrayal_LoadsClean()
    {
        var data = PriorCharacterSave();
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
            write.Parameters.AddWithValue("@d", WithoutBetrayal(json).ToJsonString());
            write.Parameters.AddWithValue("@u", PlayerName);
            write.ExecuteNonQuery().Should().Be(1);
        }

        OldSaveLoadsClean(await backend.ReadGameData(PlayerName));
    }

    // ---------- row 3: seeding from the betrayal flags ----------

    [Fact]
    public void OldSave_BetrayalFlags_SeedHasBetrayed_AndResolvedRecords()
    {
        FreshWorld();
        Story.SetStoryFlag("betrayed_by_KingsAdvisor", true);
        Story.SetStoryFlag("betrayed_by_TeamBetrayal", true);
        Story.SetStoryFlag("forgave_TeamBetrayal", true);
        Story.SetStoryFlag("killed_TheStranger", true);
        Story.SetStoryFlag("killed_possessed_child", true);

        B.RestoreFromSave(null);

        var p = Profiles(B);
        p["KingsAdvisor"].HasBetrayed.Should().BeTrue();
        p["TeamBetrayal"].HasBetrayed.Should().BeTrue();
        p["TeamBetrayal"].WasForgiven.Should().BeTrue();
        p["TheStranger"].HasBetrayed.Should().BeTrue();
        p["RomanticBetrayal"].HasBetrayed.Should().BeFalse();
        p["Lyris"].HasBetrayed.Should().BeFalse();
        p.Keys.Should().BeEquivalentTo(StoryIds, "a flag of another story (killed_possessed_child) makes no betrayer");
        p.Values.Should().OnlyContain(x => !x.IsPendingBetrayal && x.BetrayalPoints == 0, "seeding never sets points or a pending betrayal");

        Records(B).Select(r => (r.NPCId, r.IsResolved, r.Resolution)).Should().BeEquivalentTo(new[]
        {
            ("TeamBetrayal", true, BetrayalResolution.Forgiven),
            ("TheStranger", true, BetrayalResolution.Avenged),
        }, "a betrayal only flagged (betrayed_by_) has no record");
        B.GetActiveBetrays().Should().BeEmpty("every seeded record is resolved");
    }

    // ---------- row 4: seeding from finished story beats ----------

    [Fact]
    public void OldSave_LyrisSacrifice_AndNocturaBetrayal_SeedHasBetrayed_WithoutRecords()
    {
        FreshWorld();
        Story.SetStoryFlag("lyris_sacrificed_for_veloura", true);
        Story.SetStoryFlag("noctura_betrayed", true);

        B.RestoreFromSave(null);

        Profiles(B)["Lyris"].HasBetrayed.Should().BeTrue("her sacrifice in the paradox is a finished beat");
        Profiles(B)["TheStranger"].HasBetrayed.Should().BeTrue("Noctura already turned in her fight");
        Records(B).Should().BeEmpty("neither gets a Temple record");
        Profiles(B).Values.Should().OnlyContain(x => !x.IsPendingBetrayal && x.BetrayalPoints == 0);
        B.HasPendingBetrayal("TheStranger").Should().BeFalse();
        B.HasPendingBetrayal("Lyris").Should().BeFalse();
    }

    // ---------- row 5: restore order ----------

    [Fact]
    public void OldSave_Seeding_SeesTheFlags_RestoredInTheSameLoad()
    {
        FreshWorld();
        Story.SetStoryFlag("forgave_TeamBetrayal", true);
        Story.SetStoryFlag("noctura_betrayed", true);
        var story = SaveSystem.Instance.SerializeStorySystemsPublic();
        story.Betrayal = null;

        FreshWorld();
        Story.HasStoryFlag("forgave_TeamBetrayal").Should().BeFalse("the new session has no flags before the load");
        SaveSystem.Instance.RestoreStorySystems(story);

        Profiles(B)["TeamBetrayal"].HasBetrayed.Should().BeTrue();
        Profiles(B)["TheStranger"].HasBetrayed.Should().BeTrue();
        Records(B).Should().ContainSingle(r => r.NPCId == "TeamBetrayal" && r.Resolution == BetrayalResolution.Forgiven);
    }

    // ---------- row 6: a new character starts clean ----------

    [Fact]
    public void NewGameReset_ClearsProfiles_Records_AndTheDepartedCompanion()
    {
        foreach (var t in new[] { typeof(StrangerEncounterSystem), typeof(TownNPCStorySystem), typeof(DreamSystem), typeof(GriefSystem) })
            t.GetField("_fallbackInstance", SNP)!.SetValue(null, Activator.CreateInstance(t));
        FreshWorld();
        var hero = Hero();
        Betray("TheStranger", hero);
        B.AddBetrayalPoints("TeamBetrayal", 40, "grievance");
        B.RecordInfidelity("someone");
        B.AddBetrayalPoints("some_npc", 20, "grievance");
        B.DepartedCompanionId = CompanionId.Aldric;

        GameEngine.ResetNarrativeSystemsForNewGame();

        Records(B).Should().BeEmpty();
        B.DepartedCompanionId.Should().BeNull();
        Profiles(B).Keys.Should().BeEquivalentTo(StoryIds);
        Profiles(B).Values.Should().OnlyContain(p => p.BetrayalPoints == 0 && !p.IsPendingBetrayal && !p.HasBetrayed && p.ActsOfKindness.Count == 0);
        StateJson(B).Should().Be(StateJson(new BetrayalSystem()), "the new character starts as a fresh system");
    }

    // ---------- rows 7, 8, 9: revenge ----------

    [Fact]
    public void Revenge_OnTheStranger_IsTakenOnce()
    {
        FreshWorld();
        var hero = Hero();
        Betray("TheStranger", hero);
        long darkness0 = hero.Darkness;

        Revenge("TheStranger", hero).Should().Contain(Loc.Get("betrayal.header_revenge"));
        long darkness1 = hero.Darkness;
        long chivalry1 = hero.Chivalry;
        darkness1.Should().BeGreaterThan(darkness0, "the first revenge moves toward darkness");
        Story.HasStoryFlag("killed_TheStranger").Should().BeTrue();
        Records(B).Single().Resolution.Should().Be(BetrayalResolution.Avenged);

        var second = Revenge("TheStranger", hero);
        second.Should().NotContain(Loc.Get("betrayal.header_revenge"), "a resolved betrayal takes no second revenge");
        hero.Darkness.Should().Be(darkness1);
        hero.Chivalry.Should().Be(chivalry1);
        Records(B).Should().ContainSingle().Which.Resolution.Should().Be(BetrayalResolution.Avenged);
    }

    [Fact]
    public void Revenge_IsOnlyOnTheStranger()
    {
        FreshWorld();
        var hero = Hero();
        foreach (var id in new[] { "TeamBetrayal", "RomanticBetrayal", "Lyris" }) Betray(id, hero);
        long darkness = hero.Darkness, chivalry = hero.Chivalry;

        foreach (var id in new[] { "TeamBetrayal", "RomanticBetrayal", "Lyris" })
        {
            Revenge(id, hero).Should().NotContain(Loc.Get("betrayal.header_revenge"), $"{id} cannot be avenged");
            Story.HasStoryFlag($"killed_{id}").Should().BeFalse();
        }
        hero.Darkness.Should().Be(darkness);
        hero.Chivalry.Should().Be(chivalry);
        Records(B).Should().HaveCount(3).And.OnlyContain(r => !r.IsResolved && r.Resolution == BetrayalResolution.None);
    }

    [Fact]
    public void Revenge_CountsNoKill()
    {
        FreshWorld();
        var hero = Hero();
        Betray("TheStranger", hero);
        Revenge("TheStranger", hero).Should().Contain(Loc.Get("betrayal.header_revenge"));
        hero.MKills.Should().Be(7, "revenge is not a monster kill");
    }

    // ---------- row 10: forgiveness once ----------

    [Fact]
    public void Forgiveness_IsGivenOnce()
    {
        FreshWorld();
        var hero = Hero();
        Betray("KingsAdvisor", hero);
        hero.Chivalry = 900;

        var first = NewScreen();
        B.AttemptForgiveness("KingsAdvisor", hero, first.Term).GetAwaiter().GetResult().Should().BeTrue("the conditions are met");
        first.Text.Should().Contain(Loc.Get("betrayal.header_forgiveness"));
        Story.HasStoryFlag("forgave_KingsAdvisor").Should().BeTrue();
        OceanPhilosophySystem.Instance.ExperiencedMoments.Should().Contain(AwakeningMoment.ForgaveBetrayerMercy);

        var second = NewScreen();
        B.AttemptForgiveness("KingsAdvisor", hero, second.Term).GetAwaiter().GetResult().Should().BeFalse("it is already forgiven");
        second.Text.Should().NotContain(Loc.Get("betrayal.header_forgiveness"));
        second.Text.Should().Contain(Loc.Get("betrayal.cannot_forgive"));
        Records(B).Should().ContainSingle().Which.Resolution.Should().Be(BetrayalResolution.Forgiven);

        // an avenged Stranger is not forgiven either
        Betray("TheStranger", hero);
        Revenge("TheStranger", hero);
        var third = NewScreen();
        B.AttemptForgiveness("TheStranger", hero, third.Term).GetAwaiter().GetResult().Should().BeFalse();
        Records(B).Single(r => r.NPCId == "TheStranger").Resolution.Should().Be(BetrayalResolution.Avenged);
    }

    // ---------- rows 11, 12: effects ----------

    [Fact]
    public void TheStranger_CostsNoGold()
    {
        FreshWorld();
        var hero = Hero();
        Betray("TheStranger", hero);
        hero.Gold.Should().Be(1000, "her fight already settles the account");
    }

    [Fact]
    public void TheLyrisScene_GrantsNoWisdom_AndNoMoment()
    {
        FreshWorld();
        var hero = Hero();
        Betray("Lyris", hero);
        hero.Wisdom.Should().Be(40, "the paradox already gave the Wisdom");
        hero.BaseWisdom.Should().Be(40);
        OceanPhilosophySystem.Instance.ExperiencedMoments.Should().NotContain(AwakeningMoment.CompanionSacrifice,
            "her death already records the moment");
        Story.HasStoryFlag("betrayed_by_Lyris").Should().BeTrue();
    }

    // ---------- row 14: the state is written before the scene ----------

    [Fact]
    public void ADropDuringTheScene_LeavesTheRecord_TheFlag_AndTheEffect()
    {
        FreshWorld();
        var hero = Hero();
        B.RecordInfidelity("someone");
        B.HasPendingBetrayal("RomanticBetrayal").Should().BeTrue();

        var act = () => B.ExecuteBetrayal("RomanticBetrayal", hero, DroppingScreen().Term).GetAwaiter().GetResult();
        act.Should().Throw<Exception>("the player dropped at the key prompt");

        Profiles(B)["RomanticBetrayal"].HasBetrayed.Should().BeTrue();
        Records(B).Should().ContainSingle(r => r.NPCId == "RomanticBetrayal");
        Story.HasStoryFlag("betrayed_by_RomanticBetrayal").Should().BeTrue();
        hero.Wisdom.Should().Be(43, "the Wisdom +3 was granted before the scene");
    }

    // ---------- row 15: no news ----------

    [Fact]
    public void TheBetrayalSystem_WritesNoNews()
    {
        var src = File.ReadAllText(Path.Combine(UsurperReborn.Tests.Localization.HardcodedTextScannerTests.RepoRoot(),
            "Scripts", "Systems", "BetrayalSystem.cs"));
        src.Should().NotContain("NewsSystem").And.NotContain("Newsy(").And.NotContain("AddNews");
    }
}
