using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.BBS;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.8 betrayal piece 3: the team betrayal and the Temple. A murder the party witnesses or a companion
/// left to die adds the team's points once per event; a real loyalty gain takes them off. At the warning the
/// least loyal companion grows cold (shown once in town); at the threshold they leave at the next town safe
/// point, never in a dungeon, with nobody in the party, or mid fight, taking nothing the player's pack can
/// hold and keeping their level and bond. Forgiveness in the Temple's Faith room (after kindness or acts of
/// atonement since the betrayal) brings them back at loyalty 50. The Faith room lists only the betrayals it
/// can settle: the Stranger (revenge once, only if she escaped her fight) and the companion who left.
/// </summary>
[Collection("SharedGameSingletons")]
public class BetrayTeamTemple128Tests : IDisposable
{
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags INP = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int MaxWidth = 79;
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };

    private static readonly Type[] Swapped =
    {
        typeof(StoryProgressionSystem), typeof(OceanPhilosophySystem), typeof(BetrayalSystem), typeof(CompanionSystem),
        typeof(GriefSystem), typeof(ArchetypeTracker),
    };
    private static readonly FieldInfo WorldSimField = typeof(DoorMode).GetField("_worldSimMode", SNP)!;

    private readonly List<(FieldInfo Field, object? Old)> _old;
    private readonly object? _worldSim = WorldSimField.GetValue(null);
    private readonly SessionContext? _session = SessionContext.Current;
    private readonly string _language = GameConfig.Language;
    private readonly bool _screenReader = GameConfig.ScreenReaderMode;
    private readonly DifficultyMode _difficulty = DifficultySystem.CurrentDifficulty;

    public BetrayTeamTemple128Tests()
    {
        _old = Swapped.Select(t => t.GetField("_fallbackInstance", SNP)!).Select(f => (f, f.GetValue(null))).ToList();
        GameConfig.Language = "en";
        GameConfig.ScreenReaderMode = false;
        SessionContext.Current = null;
        DifficultySystem.CurrentDifficulty = DifficultyMode.Normal;
        FreshWorld();
    }

    public void Dispose()
    {
        SessionContext.Current = _session;
        WorldSimField.SetValue(null, _worldSim);
        foreach (var (f, v) in _old) f.SetValue(null, v);
        GameConfig.Language = _language;
        GameConfig.ScreenReaderMode = _screenReader;
        DifficultySystem.CurrentDifficulty = _difficulty;
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
                return Regex.Replace(Encoding.UTF8.GetString(Output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "").Replace("\r", "");
            }
        }
    }

    private static Screen NewScreen(params string[] lines)
    {
        var s = new Screen();
        s.Term = new TerminalEmulator(new LineStream(lines.Concat(Enumerable.Repeat("", 60)), i =>
        {
            if (i >= 50) throw new InvalidOperationException("the screen asked for input more than 50 times");
            s.Term.StreamWriterInternal?.Flush();
            s.Output.WriteByte((byte)'\n');
        }), s.Output);
        return s;
    }

    private static Character Hero() => new()
    {
        Name1 = "BetrayTeamHero", Name2 = "BetrayTeamHero", Class = CharacterClass.Warrior, Level = 30, HP = 5000, MaxHP = 5000,
        AI = CharacterAI.Human, Gold = 100_000, Chivalry = 300, Darkness = 100, Experience = 123456,
    };

    private static void FreshWorld()
    {
        typeof(StoryProgressionSystem).GetField("_fallbackInstance", SNP)!.SetValue(null, new StoryProgressionSystem());
        var ocean = new OceanPhilosophySystem();
        ocean.RestoreFromSave(Array.Empty<WaveFragment>(), Array.Empty<AwakeningMoment>(), null, 0);
        typeof(OceanPhilosophySystem).GetField("_fallbackInstance", SNP)!.SetValue(null, ocean);
        typeof(CompanionSystem).GetField("_fallbackInstance", SNP)!.SetValue(null, new CompanionSystem());
        typeof(GriefSystem).GetField("_fallbackInstance", SNP)!.SetValue(null, new GriefSystem());
        typeof(BetrayalSystem).GetField("_fallbackInstance", SNP)!.SetValue(null, new BetrayalSystem());
        typeof(ArchetypeTracker).GetField("_fallbackInstance", SNP)!.SetValue(null, new ArchetypeTracker());
        StoryProgressionSystem.Instance.CurrentGameDay = 10;
    }

    private static BetrayalSystem B => BetrayalSystem.Instance;
    private static CompanionSystem C => CompanionSystem.Instance;
    private static StoryProgressionSystem Story => StoryProgressionSystem.Instance;

    private static Dictionary<string, BetrayalProfile> Profiles(BetrayalSystem s) =>
        (Dictionary<string, BetrayalProfile>)typeof(BetrayalSystem).GetField("betrayalProfiles", INP)!.GetValue(s)!;

    private static List<ActiveBetrayal> Records(BetrayalSystem s) =>
        (List<ActiveBetrayal>)typeof(BetrayalSystem).GetField("activeBetrays", INP)!.GetValue(s)!;

    private static BetrayalProfile Team => Profiles(B)["TeamBetrayal"];

    /// <summary>Companions recruited and in the party, each at its loyalty.</summary>
    private static void Party(params (CompanionId Id, int Loyalty)[] members)
    {
        foreach (var (id, loyalty) in members)
        {
            var c = C.GetCompanion(id)!;
            c.IsRecruited = true;
            c.IsDead = false;
            c.LoyaltyLevel = loyalty;
        }
        C.SetActiveCompanions(members.Select(m => m.Id).ToList()).Should().BeTrue();
    }

    private static string SafePoint(Character hero, bool isDungeon = false, bool inPrison = false)
    {
        var s = NewScreen();
        B.ShowPendingAtSafePoint(hero, s.Term, isDungeon, inPrison).GetAwaiter().GetResult();
        return s.Text;
    }

    /// <summary>Points to the threshold: pending, not yet shown.</summary>
    private static void SevenGrievances()
    {
        for (int i = 0; i < 7; i++) B.AddTeamGrievance("test grievance");
        B.HasPendingBetrayal("TeamBetrayal").Should().BeTrue();
    }

    private static int Equip(string name, EquipmentSlot slot) => EquipmentDatabase.RegisterDynamic(new Equipment
    {
        Name = name, Slot = slot, ArmorClass = 3, MinLevel = 1,
    });

    private static Item Bagged(string name) => new() { Name = name, Type = ObjType.Body, Value = 10 };

    private static string Source(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", "..", "Scripts" }.Concat(parts).ToArray()));

    private static string CodeOnly(string src) =>
        Regex.Replace(Regex.Replace(src, @"/\*.*?\*/", "", RegexOptions.Singleline), @"//[^\n]*", "");

    private static string MethodBody(string src, string signature)
    {
        int start = src.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, $"{signature} is in the source");
        int open = src.IndexOf('{', start);
        int depth = 0;
        for (int i = open; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0) return src.Substring(start, i - start + 1);
        }
        throw new InvalidOperationException("unbalanced method");
    }

    private static string En(string key, params object[] args) => Loc.GetIn("en", key, args);

    /// <summary>The screen's rows joined with single spaces, so a wrapped sentence reads whole.</summary>
    private static string Flat(string text) => string.Join(" ", text.Split('\n').Select(r => r.Trim()).Where(r => r.Length > 0));

    private static void EveryRowFits(string text, string screen)
    {
        foreach (var row in text.Split('\n'))
            row.Length.Should().BeLessOrEqualTo(MaxWidth, $"every row of {screen} fits: \"{row}\"");
    }

    /// <summary>A Temple on a scripted screen, its player set.</summary>
    private static (TempleLocation Temple, Screen Screen) Temple(Character hero, params string[] lines)
    {
        var s = NewScreen(lines);
        var temple = new TempleLocation(s.Term, null!, UsurperRemake.GodSystemSingleton.Instance);
        typeof(BaseLocation).GetField("currentPlayer", INP)!.SetValue(temple, hero);
        return (temple, s);
    }

    private static List<string> FaithKeys(TempleLocation temple)
    {
        var m = typeof(TempleLocation).GetMethod("RoomItems", INP)!;
        var items = ((Task<List<TempleLocation.TempleMenuItem>>)m.Invoke(temple, new object[] { TempleLocation.TempleRoom.Faith })!)
            .GetAwaiter().GetResult();
        return items.Select(i => i.Key).ToList();
    }

    private static void ShowBetrayals(TempleLocation temple)
    {
        var m = typeof(TempleLocation).GetMethod("ShowBetrayals", INP)!;
        ((Task)m.Invoke(temple, Array.Empty<object>())!).GetAwaiter().GetResult();
    }

    /// <summary>A betrayal record as the betrayal scene leaves it, without the scene.</summary>
    private static void Betrayed(string id)
    {
        var p = Profiles(B)[id];
        p.IsPendingBetrayal = true;
        B.ExecuteBetrayal(id, Hero(), NewScreen().Term).GetAwaiter().GetResult().Occurred.Should().BeTrue();
    }

    /// <summary>Mira leaves: the party is Aldric (loyal) and Mira (least loyal); the team betrayal runs in town.</summary>
    private static (Character Hero, string Text) MiraLeaves()
    {
        var hero = Hero();
        Party((CompanionId.Aldric, 80), (CompanionId.Mira, 10));
        SevenGrievances();
        return (hero, SafePoint(hero));
    }

    // ---------- row 33: points once per event ----------

    [Fact]
    public void AMurderWitnessedByThree_AddsTheTeamPointsOnce_AndNoneWithNobodyThere()
    {
        var hero = Hero();
        var victim = new NPC { ID = "npc_b3_victim", Name1 = "Victim", Name2 = "Victim", Level = 5, HP = 0, MaxHP = 50 };

        WorldSimulator.ApplyBloodPrice(hero, victim, 1f, isDeliberate: true);
        Team.BetrayalPoints.Should().Be(0, "nobody in the party saw it");

        Party((CompanionId.Aldric, 80), (CompanionId.Mira, 80), (CompanionId.Vex, 80));
        WorldSimulator.ApplyBloodPrice(hero, victim, 1f, isDeliberate: true);
        Team.BetrayalPoints.Should().Be(GameConfig.TeamBetrayalPointsPerEvent, "one murder is one event, not one per companion");
        GameConfig.TeamBetrayalPointsPerEvent.Should().Be(15);
        Team.Grievances.Should().ContainSingle();
    }

    [Fact]
    public void ACompanionLeftToDie_AddsTheTeamPointsOnce_FromTheFight()
    {
        C.RecordAbandonedGrievance("left a companion to die");
        Team.BetrayalPoints.Should().Be(15);

        // the fight's death handler records it once, inside the "could have helped" branch, through the companion system
        var body = CodeOnly(MethodBody(Source("Systems", "CombatEngine.cs"), "private async Task HandleCompanionDeath("));
        int branch = body.IndexOf("CouldHaveHelped(companion, result.Player)", StringComparison.Ordinal);
        int call = body.IndexOf("companionSystem.RecordAbandonedGrievance(", StringComparison.Ordinal);
        int kill = body.IndexOf("await companionSystem.KillCompanion(", StringComparison.Ordinal);
        branch.Should().BeGreaterThan(0);
        call.Should().BeGreaterThan(branch).And.BeLessThan(kill);
        Regex.Matches(body, "RecordAbandonedGrievance\\(").Count.Should().Be(1);
    }

    [Fact]
    public void NoSession_InAServerForManyPlayers_AddsNoTeamPoints()
    {
        WorldSimField.SetValue(null, true);
        B.AddTeamGrievance("world sim");
        Party((CompanionId.Aldric, 80));
        Team.BetrayalPoints = 30;
        C.ModifyLoyalty(CompanionId.Aldric, 10, "gift");
        WorldSimField.SetValue(null, _worldSim);
        Team.BetrayalPoints.Should().Be(30, "the world simulator thread has no player");
        Team.ActsOfKindness.Should().BeEmpty();
    }

    // ---------- row 34: the warning ----------

    [Fact]
    public void AtTheWarning_TheLeastLoyalCompanionGrowsCold_ShownOnceInTown()
    {
        var hero = Hero();
        Party((CompanionId.Aldric, 80), (CompanionId.Mira, 30));
        for (int i = 0; i < 3; i++) B.AddTeamGrievance("g");
        Flat(SafePoint(hero)).Should().NotContain(En("betrayal.team_warning", "Mira"), "45 is below the warning");

        B.AddTeamGrievance("g");
        Team.BetrayalPoints.Should().Be(60);
        Flat(SafePoint(hero, isDungeon: true)).Should().NotContain(En("betrayal.team_warning", "Mira"), "never in a dungeon");
        Flat(SafePoint(hero)).Should().Contain(En("betrayal.team_warning", "Mira"));
        Team.WarningShown.Should().BeTrue();
        Flat(SafePoint(hero)).Should().NotContain(En("betrayal.team_warning", "Mira"), "once");
        Team.HasBetrayed.Should().BeFalse("a warning, nobody left");
        C.IsCompanionActive(CompanionId.Mira).Should().BeTrue();
    }

    // ---------- row 35: the trigger ----------

    [Fact]
    public void AtTheThreshold_ItIsPending_AndTheNextTownLoopRunsTheDeparture()
    {
        var hero = Hero();
        Party((CompanionId.Aldric, 80), (CompanionId.Mira, 10));
        for (int i = 0; i < 6; i++) B.AddTeamGrievance("g");
        B.HasPendingBetrayal("TeamBetrayal").Should().BeFalse("90 is below the threshold");
        B.AddTeamGrievance("g");
        B.HasPendingBetrayal("TeamBetrayal").Should().BeTrue();

        var text = SafePoint(hero);
        text.Should().Contain(En("betrayal.turns_to_face", En("betrayal.TeamBetrayal.name")));
        text.Should().Contain(En("betrayal.team_leaves", "Mira"));
        Team.HasBetrayed.Should().BeTrue();
        B.DepartedCompanionId.Should().Be(CompanionId.Mira);
        Story.HasStoryFlag("betrayed_by_TeamBetrayal").Should().BeTrue();
    }

    // ---------- row 36: gains reduce ----------

    [Fact]
    public void ARealGainReducesThePoints_ReadBeforeTheDifficulty_TheTrickleNever()
    {
        Party((CompanionId.Aldric, 40));
        for (int i = 0; i < 4; i++) B.AddTeamGrievance("g");

        C.ModifyLoyalty(CompanionId.Aldric, 5, "a gift");
        Team.BetrayalPoints.Should().Be(55);
        Team.ActsOfKindness.Should().ContainSingle(k => k.Reason == "a gift" && k.GameDay == 10);

        C.ModifyLoyalty(CompanionId.Aldric, 1, "level up");
        Team.BetrayalPoints.Should().Be(55, "the +1 level trickle is not kindness");

        DifficultySystem.CurrentDifficulty = DifficultyMode.Easy;
        C.ModifyLoyalty(CompanionId.Aldric, 4, "small gift");
        C.GetCompanion(CompanionId.Aldric)!.LoyaltyLevel.Should().Be(52, "Easy scales 4 up to 6");
        Team.BetrayalPoints.Should().Be(55, "a raw 4 does not count, however Easy scales it");

        DifficultySystem.CurrentDifficulty = DifficultyMode.Nightmare;
        C.ModifyLoyalty(CompanionId.Aldric, 6, "quest");
        Team.BetrayalPoints.Should().Be(49, "a raw 6 counts in full, though Nightmare halves the loyalty");
        Team.ActsOfKindness.Should().HaveCount(2);

        var body = CodeOnly(MethodBody(Source("Systems", "CompanionSystem.cs"), "public void ModifyLoyalty("));
        body.Should().Contain("BetrayalSystem.Instance.RecordTeamKindness(amount, reason)");
    }

    // ---------- row 37: the least loyal leaves ----------

    [Fact]
    public void TheLeastLoyalCompanionLeaves_TiesById_NeverAnNpcTeammate()
    {
        var hero = Hero();
        Party((CompanionId.Aldric, 40), (CompanionId.Vex, 20), (CompanionId.Mira, 20));
        SevenGrievances();
        SafePoint(hero);
        B.DepartedCompanionId.Should().Be(CompanionId.Mira, "Mira and Vex tie at 20; Mira comes first by id");
        C.IsCompanionActive(CompanionId.Aldric).Should().BeTrue();
        C.IsCompanionActive(CompanionId.Vex).Should().BeTrue();
        C.IsCompanionActive(CompanionId.Mira).Should().BeFalse();

        // only the companion system's party is read: NPC teammates (spouses, hired NPCs) are never chosen
        var src = CodeOnly(Source("Systems", "BetrayalSystem.cs"));
        int at = src.IndexOf("internal static Companion? LeastLoyalActive()", StringComparison.Ordinal);
        at.Should().BeGreaterThan(0);
        var pick = src.Substring(at, src.IndexOf(';', at) - at);
        pick.Should().Contain("CompanionSystem.Instance.GetActiveCompanions()");
        pick.Should().NotContain("Team").And.NotContain("Teammates").And.NotContain("NPC");
    }

    // ---------- row 38: what is lost and what returns ----------

    [Fact]
    public void TheDeparture_ReturnsWhatFits_KeepsTheRestWithThem_AndSaysSo()
    {
        var hero = Hero();
        for (int i = 0; i < GameConfig.MaxInventoryItems - 3; i++) hero.Inventory.Add(Bagged("filler " + i));
        Party((CompanionId.Aldric, 80), (CompanionId.Mira, 10));
        var mira = C.GetCompanion(CompanionId.Mira)!;
        mira.EquippedItems[EquipmentSlot.Body] = Equip("Mira's Robe", EquipmentSlot.Body);
        mira.EquippedItems[EquipmentSlot.Head] = Equip("Mira's Hood", EquipmentSlot.Head);
        mira.Inventory.Add(Bagged("Healing Herb"));
        mira.Inventory.Add(Bagged("Silver Charm"));
        mira.Level = 42; mira.Experience = 987654; mira.TrustLevel = 77; mira.RomanceLevel = 33;
        SevenGrievances();

        var text = SafePoint(hero);

        hero.Inventory.Should().HaveCount(GameConfig.MaxInventoryItems);
        hero.Inventory.Select(i => i.Name).Should().Contain(new[] { "Mira's Robe", "Mira's Hood", "Healing Herb" });
        mira.EquippedItems.Should().BeEmpty();
        mira.Inventory.Select(i => i.Name).Should().Equal("Silver Charm");
        (mira.Level, mira.Experience, mira.TrustLevel, mira.RomanceLevel).Should().Be((42, 987654L, 77, 33));
        mira.IsRecruited.Should().BeFalse();
        mira.IsActive.Should().BeFalse();
        C.GetActiveCompanionIds().Should().Equal(CompanionId.Aldric);

        text.Should().Contain(En("betrayal.team_leaves", "Mira"));
        text.Should().Contain(En("companion.equipment_returned", "Mira", 3).TrimStart('\n'));
        text.Should().Contain(En("betrayal.team_items_kept", "Mira", 1));
        text.Should().Contain(En("betrayal.team_keeps", "Mira", 42));
        text.Should().Contain(En("betrayal.team_rejoin_hint", "Mira", 50));
    }

    // ---------- row 39: the departed stays gone ----------

    [Fact]
    public void TheDeparted_CannotBeRecruitedOrActivated()
    {
        var (hero, _) = MiraLeaves();
        C.GetRecruitableCompanions(100).Select(c => c.Id).Should().NotContain(CompanionId.Mira);
        C.GetRecruitableCompanions(100).Select(c => c.Id).Should().Contain(CompanionId.Vex, "others are still met as before");
        C.RecruitCompanion(CompanionId.Mira, hero, NewScreen().Term).GetAwaiter().GetResult().Should().BeFalse();
        C.ActivateCompanion(CompanionId.Mira).Should().BeFalse();
        C.SetActiveCompanions(new List<CompanionId> { CompanionId.Mira }).Should().BeFalse();
        C.GetRecruitedCompanions().Select(c => c.Id).Should().Equal(CompanionId.Aldric);
        C.IsDeparted(CompanionId.Mira).Should().BeTrue();
    }

    [Fact]
    public void TheDeparted_IsNotOfferedTheirRecruitmentScene_Again()
    {
        var gates = new (string File, string Signature, string Check)[]
        {
            ("InnLocation.cs", "aldric.IsRecruited || aldric.IsDead", "CompanionSystem.Instance.IsDeparted(CompanionId.Aldric)"),
            ("PrisonLocation.cs", "private bool CanMeetVex(", "companionSystem.IsDeparted(CompanionId.Vex)"),
            ("TempleLocation.cs", "private bool CanMeetMira(", "companionSystem.IsDeparted(UsurperRemake.Systems.CompanionId.Mira)"),
            ("DungeonLocation.cs", "private async Task<bool> TryLyrisRecruitment(", "companionSystem.IsDeparted(UsurperRemake.Systems.CompanionId.Lyris)"),
            ("DungeonLocation.cs", "private async Task<bool> TryRecruitCompanionInDungeon(", "companionSystem.IsDeparted(companionId)"),
            ("MusicShopLocation.cs", "private MelodiaState GetMelodiaState(", "cs.IsDeparted(CompanionId.Melodia)"),
            ("MusicShopLocation.cs", "private async Task RecruitMelodia(", "IsDeparted(CompanionId.Melodia)"),
        };
        foreach (var (file, signature, check) in gates)
        {
            var src = CodeOnly(Source("Locations", file));
            if (signature.StartsWith("private"))
                MethodBody(src, signature).Should().Contain(check, $"{file} {signature}");
            else
            {
                int at = src.IndexOf(signature, StringComparison.Ordinal);
                at.Should().BeGreaterThan(0);
                src.Substring(at, 200).Should().Contain(check, $"{file} Aldric's bandit scene");
            }
        }
        // the Inn's Aldric sighting too
        CodeOnly(Source("Locations", "InnLocation.cs")).Should().Contain("&& !CompanionSystem.Instance.IsDeparted(CompanionId.Aldric)\n");

        // the Temple's own check, run
        var (hero, _) = MiraLeaves();
        var mira = C.GetCompanion(CompanionId.Mira)!;
        hero.Level = 100;
        var (temple, _) = Temple(hero);
        var canMeet = typeof(TempleLocation).GetMethod("CanMeetMira", INP)!;
        mira.IsRecruited.Should().BeFalse();
        ((bool)canMeet.Invoke(temple, Array.Empty<object>())!).Should().BeFalse("Mira left; she is not met again at the Temple");
    }

    // ---------- row 40: rejoin restores ----------

    [Fact]
    public void Forgiveness_BringsThemBack_AsTheyLeft_AtLoyaltyFifty_AfterALoad()
    {
        var hero = Hero();
        for (int i = 0; i < GameConfig.MaxInventoryItems - 1; i++) hero.Inventory.Add(Bagged("filler " + i));
        Party((CompanionId.Aldric, 80), (CompanionId.Mira, 10));
        var mira = C.GetCompanion(CompanionId.Mira)!;
        mira.Inventory.Add(Bagged("Healing Herb"));
        mira.Inventory.Add(Bagged("Silver Charm"));
        mira.Level = 42; mira.Experience = 987654; mira.TrustLevel = 77; mira.RomanceLevel = 33;
        SevenGrievances();
        SafePoint(hero);
        mira.Inventory.Select(i => i.Name).Should().Equal("Silver Charm");

        // a save and a load in between
        var companions = C.Serialize();
        var betrayal = JsonSerializer.Deserialize<BetrayalSaveData>(JsonSerializer.Serialize(B.Serialize()))!;
        typeof(CompanionSystem).GetField("_fallbackInstance", SNP)!.SetValue(null, new CompanionSystem());
        typeof(BetrayalSystem).GetField("_fallbackInstance", SNP)!.SetValue(null, new BetrayalSystem());
        C.Deserialize(companions);
        B.RestoreFromSave(betrayal);
        mira = C.GetCompanion(CompanionId.Mira)!;
        mira.IsRecruited.Should().BeFalse();
        B.DepartedCompanionId.Should().Be(CompanionId.Mira);

        Story.CurrentGameDay = 11;
        for (int i = 0; i < 5; i++) C.ModifyLoyalty(CompanionId.Aldric, 5, "gift " + i);
        var s = NewScreen();
        B.AttemptForgiveness("TeamBetrayal", hero, s.Term).GetAwaiter().GetResult().Should().BeTrue();

        mira.IsRecruited.Should().BeTrue();
        mira.IsActive.Should().BeFalse("they wait at the Inn");
        (mira.Level, mira.Experience, mira.TrustLevel, mira.RomanceLevel).Should().Be((42, 987654L, 77, 33));
        mira.LoyaltyLevel.Should().Be(50);
        mira.Inventory.Select(i => i.Name).Should().Equal("Silver Charm");
        B.DepartedCompanionId.Should().BeNull();
        C.GetInactiveCompanions().Select(c => c.Id).Should().Contain(CompanionId.Mira);
        C.ActivateCompanion(CompanionId.Mira).Should().BeTrue();
        s.Text.Should().Contain(En("betrayal.team_rejoins", "Mira", 50));
        s.Text.Should().Contain(En("betrayal.approach_npc", "Mira"));
        Records(B).Single(r => r.NPCId == "TeamBetrayal").Resolution.Should().Be(BetrayalResolution.Forgiven);
    }

    // ---------- row 41: nobody in the party ----------

    [Fact]
    public void APendingTeamBetrayal_WithNobodyInTheParty_WaitsForASafePointWithOne()
    {
        var hero = Hero();
        SevenGrievances();
        SafePoint(hero).Should().NotContain(En("betrayal.TeamBetrayal.name"));
        Team.HasBetrayed.Should().BeFalse();
        B.HasPendingBetrayal("TeamBetrayal").Should().BeTrue("kept");
        Records(B).Should().BeEmpty();

        Party((CompanionId.Vex, 40));
        SafePoint(hero).Should().Contain(En("betrayal.team_leaves", "Vex"));
        B.DepartedCompanionId.Should().Be(CompanionId.Vex);
    }

    // ---------- row 42: never in a dungeon ----------

    [Fact]
    public void APendingTeamBetrayal_ReachedInADungeon_DepartsOnlyInTown()
    {
        var hero = Hero();
        Party((CompanionId.Aldric, 80), (CompanionId.Mira, 10));
        SevenGrievances();
        SafePoint(hero, isDungeon: true).Should().NotContain(En("betrayal.team_leaves", "Mira"));
        SafePoint(hero, inPrison: true).Should().NotContain(En("betrayal.team_leaves", "Mira"));
        C.IsCompanionActive(CompanionId.Mira).Should().BeTrue();
        Team.HasBetrayed.Should().BeFalse();
        SafePoint(hero).Should().Contain(En("betrayal.team_leaves", "Mira"));
    }

    // ---------- row 43: the Temple lists only what it can settle ----------

    [Fact]
    public void TheFaithRoom_ListsB_OnlyForABetrayalItCanSettle()
    {
        var hero = Hero();
        var (temple, _) = Temple(hero);
        FaithKeys(temple).Should().NotContain("B", "nothing to settle");

        Betrayed("RomanticBetrayal");
        Betrayed("Lyris");
        FaithKeys(temple).Should().NotContain("B", "the beloved cannot be forgiven and Lyris's was a sacrifice");

        Betrayed("TheStranger");
        FaithKeys(temple).Should().Contain("B");

        FreshWorld();
        MiraLeaves();
        FaithKeys(temple).Should().Contain("B", "a companion left");
        Source("Locations", "TempleLocation.cs").Should().Contain("(TempleRoom.Faith, \"B\") => ShowBetrayals");
    }

    // ---------- row 44: revenge once, and only if she escaped ----------

    [Fact]
    public void RevengeOnTheStranger_OnceAtTheTemple_ThenTheEntryIsGone()
    {
        var hero = Hero();
        Betrayed("TheStranger");
        Story.SetStoryFlag("noctura_escaped_with_power", true);
        long darkness = hero.Darkness;

        var (temple, s) = Temple(hero, "2", "Y");
        ShowBetrayals(temple);
        s.Text.Should().Contain(En("betrayal.temple_forgive", En("betrayal.TheStranger.name")));
        s.Text.Should().Contain(En("betrayal.temple_revenge", En("betrayal.TheStranger.name")));
        s.Text.Should().Contain(En("betrayal.debt_paid"));
        s.Text.Should().Contain(En("betrayal.temple_nothing"), "nothing is left to settle after it");
        hero.Darkness.Should().BeGreaterThan(darkness);
        long after = hero.Darkness;
        Story.HasStoryFlag("killed_TheStranger").Should().BeTrue();

        FaithKeys(temple).Should().NotContain("B");
        var (again, s2) = Temple(hero, "1", "Y", "2", "Y");
        ShowBetrayals(again);
        hero.Darkness.Should().Be(after, "once");
        s2.Text.Should().NotContain(En("betrayal.debt_paid"));
    }

    [Fact]
    public void RevengeOnTheStranger_IsNotOffered_WhenSheWasDefeatedInHerFight()
    {
        var hero = Hero();
        Betrayed("TheStranger");
        Story.SetStoryFlag("noctura_escaped_with_power", false);
        Story.SetStoryFlag("noctura_betrayal_defeated", true);
        B.CanTakeRevengeAtTemple().Should().BeFalse();

        var (temple, s) = Temple(hero, "R");
        ShowBetrayals(temple);
        s.Text.Should().Contain(En("betrayal.temple_forgive", En("betrayal.TheStranger.name")), "forgive only");
        s.Text.Should().NotContain(En("betrayal.temple_revenge", En("betrayal.TheStranger.name")));

        Story.SetStoryFlag("noctura_betrayal_defeated", false);
        Story.SetStoryFlag("noctura_escaped_with_power", true);
        B.CanTakeRevengeAtTemple().Should().BeTrue("she escaped");
    }

    // ---------- row 45: forgiveness with the conditions unmet ----------

    [Fact]
    public void ForgivenessAtTheTemple_WithTheConditionsUnmet_SaysWhatIsNeeded_AndChangesNothing()
    {
        var (hero, _) = MiraLeaves();
        var (temple, s) = Temple(hero, "1");
        ShowBetrayals(temple);

        s.Text.Should().Contain(En("betrayal.temple_forgive", "Mira"));
        s.Text.Should().Contain(En("betrayal.conditions_not_met"));
        s.Text.Should().Contain(En("betrayal.path_to_redemption", "").TrimEnd());
        C.GetCompanion(CompanionId.Mira)!.IsRecruited.Should().BeFalse();
        B.DepartedCompanionId.Should().Be(CompanionId.Mira);
        Records(B).Single(r => r.NPCId == "TeamBetrayal").IsResolved.Should().BeFalse();
        Story.HasStoryFlag("forgave_TeamBetrayal").Should().BeFalse();
    }

    // ---------- row 46: kindness since the betrayal only ----------

    [Fact]
    public void OnlyKindnessSinceTheBetrayal_Counts()
    {
        var hero = Hero();
        Party((CompanionId.Aldric, 80), (CompanionId.Mira, 10));
        for (int i = 0; i < 4; i++) B.AddTeamGrievance("g");
        Story.CurrentGameDay = 8;
        for (int i = 0; i < 5; i++) C.ModifyLoyalty(CompanionId.Aldric, 5, "old gift");
        Story.CurrentGameDay = 10;
        C.ModifyLoyalty(CompanionId.Aldric, 5, "gift the same day, before");
        for (int i = 0; i < 5; i++) B.AddTeamGrievance("g");
        B.HasPendingBetrayal("TeamBetrayal").Should().BeTrue();
        Team.ActsOfKindness.Should().HaveCount(6);
        SafePoint(hero);
        Team.BetrayalDay.Should().Be(10);
        Team.ActsOfKindness.Should().HaveCount(5, "the same day's earlier kindness is dropped at the departure");

        B.AttemptForgiveness("TeamBetrayal", hero, NewScreen().Term).GetAwaiter().GetResult()
            .Should().BeFalse("five acts, all before the betrayal");

        for (int i = 0; i < 4; i++) C.ModifyLoyalty(CompanionId.Aldric, 5, "new gift");
        B.AttemptForgiveness("TeamBetrayal", hero, NewScreen().Term).GetAwaiter().GetResult().Should().BeFalse("four since");
        C.ModifyLoyalty(CompanionId.Aldric, 5, "fifth");
        B.AttemptForgiveness("TeamBetrayal", hero, NewScreen().Term).GetAwaiter().GetResult().Should().BeTrue();
    }

    // ---------- atonement ----------

    [Fact]
    public void Atonement_CostsTheFormula()
    {
        var hero = Hero();
        hero.Level = 1;
        BetrayalSystem.AtonementCost(hero).Should().Be(100);
        hero.Level = 30;
        BetrayalSystem.AtonementCost(hero).Should().Be(3000);
        hero.Level = 0;
        BetrayalSystem.AtonementCost(hero).Should().Be(100, "never below the minimum");
        (GameConfig.BetrayalAtonementMinCost, GameConfig.BetrayalAtonementCostPerLevel).Should().Be((100, 100));
    }

    [Fact]
    public void Atonement_OnceAGameDay_CountsAsKindness_AndForgivenessFollows()
    {
        var (hero, _) = MiraLeaves();
        long gold = hero.Gold;
        var (temple, s) = Temple(hero, "2");
        ShowBetrayals(temple);
        s.Text.Should().Contain(En("betrayal.temple_atone", "Mira", 3000));
        s.Text.Should().Contain(En("betrayal.atone_done", 3000, "Mira"));
        hero.Gold.Should().Be(gold - 3000);
        Team.ActsOfKindness.Should().ContainSingle(k => k.Reason == "temple_atonement" && k.GameDay == 10);

        var (again, s2) = Temple(hero, "2");
        ShowBetrayals(again);
        s2.Text.Should().Contain(En("betrayal.atone_already"));
        hero.Gold.Should().Be(gold - 3000, "once a game day");
        Team.ActsOfKindness.Should().HaveCount(1);

        for (int day = 11; day <= 14; day++)
        {
            Story.CurrentGameDay = day;
            B.Atone(hero).Should().Be(BetrayalSystem.AtonementOutcome.Done);
        }
        B.AttemptForgiveness("TeamBetrayal", hero, NewScreen().Term).GetAwaiter().GetResult().Should().BeTrue("five acts of atonement");
        C.GetCompanion(CompanionId.Mira)!.IsRecruited.Should().BeTrue();
        B.CanAtone.Should().BeFalse("nothing left to atone for");
        B.Atone(hero).Should().Be(BetrayalSystem.AtonementOutcome.NotOffered);
    }

    [Fact]
    public void Atonement_WithTooLittleGold_IsRefused_AndChangesNothing()
    {
        var (hero, _) = MiraLeaves();
        hero.Gold = 2999;
        var (temple, s) = Temple(hero, "2");
        ShowBetrayals(temple);
        s.Text.Should().Contain(En("betrayal.atone_no_gold", 3000));
        hero.Gold.Should().Be(2999);
        Team.ActsOfKindness.Should().BeEmpty();
    }

    [Fact]
    public void Atonement_IsNotOffered_WithoutADepartureToForgive()
    {
        var hero = Hero();
        Betrayed("TheStranger");
        B.CanAtone.Should().BeFalse();
        B.Atone(hero).Should().Be(BetrayalSystem.AtonementOutcome.NotOffered);
        hero.Gold.Should().Be(100_000);
        Team.ActsOfKindness.Should().BeEmpty();
        var (temple, s) = Temple(hero, "R");
        ShowBetrayals(temple);
        s.Text.Should().NotContain(En("betrayal.temple_atone", En("betrayal.TeamBetrayal.name"), 3000));
    }

    // ---------- widths: every new row fits 79 columns in all five languages ----------

    [Fact]
    public void EveryTeamAndTempleRow_FitsInFiveLanguages()
    {
        foreach (var lang in Langs)
            foreach (var id in new[] { CompanionId.Aldric, CompanionId.Melodia })
            {
                GameConfig.Language = lang;
                FreshWorld();
                var hero = Hero();
                for (int i = 0; i < GameConfig.MaxInventoryItems - 1; i++) hero.Inventory.Add(Bagged("filler " + i));
                Party((CompanionId.Vex, 90), (id, 30));
                var c = C.GetCompanion(id)!;
                c.Level = 100;
                c.Inventory.Add(Bagged("a"));
                c.Inventory.Add(Bagged("b"));
                for (int i = 0; i < 4; i++) B.AddTeamGrievance("g");
                var warning = SafePoint(hero);
                Flat(warning).Should().Contain(Loc.Get("betrayal.team_warning", c.Name));
                for (int i = 0; i < 3; i++) B.AddTeamGrievance("g");
                var departure = SafePoint(hero);
                Flat(departure).Should().Contain(Loc.Get("betrayal.team_items_kept", c.Name, 1));

                var (temple, s) = Temple(hero, "2", "", "1", "", "");
                ShowBetrayals(temple);
                hero.Gold = 0;
                var (poor, s2) = Temple(hero, "2", "R");
                Story.CurrentGameDay = 11;
                ShowBetrayals(poor);

                Story.CurrentGameDay = 12;
                hero.Gold = 1_000_000;
                for (int day = 12; day <= 16; day++) { Story.CurrentGameDay = day; B.Atone(hero); }
                var back = NewScreen();
                B.AttemptForgiveness("TeamBetrayal", hero, back.Term).GetAwaiter().GetResult().Should().BeTrue();

                foreach (var (text, screen) in new[] { (warning, "warning"), (departure, "departure"), (s.Text, "temple"), (s2.Text, "temple, no gold"), (back.Text, "rejoin") })
                    EveryRowFits(text, $"[{lang}] {id} {screen}");
            }
        GameConfig.Language = "en";

        // the Stranger's rows in the Temple
        foreach (var lang in Langs)
        {
            GameConfig.Language = lang;
            FreshWorld();
            var hero = Hero();
            Betrayed("TheStranger");
            Story.SetStoryFlag("noctura_escaped_with_power", true);
            var (temple, s) = Temple(hero, "2", "N", "1");
            ShowBetrayals(temple);
            Flat(s.Text).Should().Contain(Loc.Get("betrayal.temple_revenge_confirm", Loc.Get("betrayal.TheStranger.name")));
            EveryRowFits(s.Text, $"[{lang}] the Stranger at the Temple");
        }
        GameConfig.Language = "en";
    }

    // ---------- row 47: the keys in five languages ----------

    private static readonly string[] NewKeys =
    {
        "temple.faith_hall.betrayals", "betrayal.temple_header", "betrayal.temple_intro", "betrayal.temple_forgive",
        "betrayal.temple_revenge", "betrayal.temple_revenge_confirm", "betrayal.temple_atone", "betrayal.temple_nothing",
        "betrayal.atone_done", "betrayal.atone_counted", "betrayal.atone_already", "betrayal.atone_no_gold",
        "betrayal.team_warning", "betrayal.team_leaves", "betrayal.team_items_kept", "betrayal.team_keeps",
        "betrayal.team_rejoin_hint", "betrayal.team_rejoins",
    };

    [Fact]
    public void TheNewKeys_AreInFiveLanguages_WithTheSameArguments()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Localization");
        var docs = Langs.ToDictionary(l => l, l => JsonDocument.Parse(File.ReadAllText(Path.Combine(root, l + ".json"))));
        foreach (var key in NewKeys)
        {
            string Args(string lang)
            {
                docs[lang].RootElement.TryGetProperty(key, out var v).Should().BeTrue($"{lang} has {key}");
                return string.Join(",", Regex.Matches(v.GetString()!, @"\{(\d+)\}").Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x));
            }
            string en = Args("en");
            foreach (var lang in Langs.Skip(1))
            {
                Args(lang).Should().Be(en, $"{lang} {key} has the same arguments");
                docs[lang].RootElement.GetProperty(key).GetString().Should().NotBe(docs["en"].RootElement.GetProperty(key).GetString(), $"{lang} {key} is translated");
            }
        }
        foreach (var d in docs.Values) d.Dispose();
    }
}
