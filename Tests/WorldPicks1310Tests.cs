using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using UsurperReborn.Tests.Localization;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.10 audit picks, the living world (round 2 piece FC2). P3: the player grieves a spouse who dies away from
/// the player's fights (a permanent death or old age in the world, or one found at login). P4: the murder news and
/// warning say the victim will be back. P5: Blessed Rain makes healing at the Healer free, a Holy Day makes the
/// Temple's Cloister meditation and atonement free, and the event descriptions say what the events do. P7: a dying
/// wish message is a talk task to the named person, paid on delivery. P15: Detect Threats uncovers an active plot
/// on half its casts. Members added in 1.3.10 are reached by reflection so the class also runs on the release
/// before it (the repro). All in memory.
/// </summary>
[Collection("SharedGameSingletons")]
public class WorldPicks1310Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags S = BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags PS = BindingFlags.Public | BindingFlags.Static;
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };

    private readonly King? _kingBefore = CastleLocation.GetCurrentKing();
    private readonly List<NPC> _rosterBefore = NPCSpawnSystem.Instance.ActiveNPCs.ToList();
    private readonly bool _pacing = Pacing.Disabled;
    private readonly List<string> _partyBefore = GameEngine.Instance.DungeonPartyNPCIds.ToList();
    private readonly List<Spouse> _spousesBefore = RomanceTracker.Instance.Spouses.ToList();
    private readonly List<ExSpouse> _exSpousesBefore = RomanceTracker.Instance.ExSpouses.ToList();
    private readonly List<Lover> _loversBefore = RomanceTracker.Instance.CurrentLovers.ToList();
    private readonly List<Child> _childrenBefore = FamilySystem.Instance.AllChildren.ToList();
    private readonly GriefSystemData _griefBefore = GriefSystem.Instance.Serialize();
    private readonly Character? _playerBefore = GameEngine.Instance.CurrentPlayer;
    private readonly List<string> _noticesBefore = GameEngine.PendingNotifications.ToList();
    private readonly List<WorldEventSystem.WorldEvent> _eventsBefore = WorldEventSystem.Instance.GetActiveEvents();
    private readonly object? _simBefore = typeof(WorldSimulator).GetField("_instance", S)!.GetValue(null);
    private readonly object? _petitionRandomBefore = typeof(NPCPetitionSystem).GetField("_random", F)!.GetValue(NPCPetitionSystem.Instance);
    private readonly object? _rollBefore = typeof(CastleLocation).GetField("DetectThreatsRoll", S)?.GetValue(null);

    public WorldPicks1310Tests()
    {
        Pacing.Disabled = true;
        NPCSpawnSystem.Instance.ActiveNPCs.Clear();
        GameEngine.Instance.SetDungeonPartyNPCs(Array.Empty<string>());
        RomanceTracker.Instance.Spouses.Clear();
        RomanceTracker.Instance.ExSpouses.Clear();
        RomanceTracker.Instance.CurrentLovers.Clear();
        GriefSystem.Instance.Reset();
        GameEngine.PendingNotifications.Clear();
        Events().Clear();
    }

    public void Dispose()
    {
        CastleLocation.SetKing(_kingBefore!);
        var active = NPCSpawnSystem.Instance.ActiveNPCs;
        active.Clear();
        active.AddRange(_rosterBefore);
        Pacing.Disabled = _pacing;
        GameEngine.Instance.SetDungeonPartyNPCs(_partyBefore);
        RomanceTracker.Instance.Spouses.Clear(); RomanceTracker.Instance.Spouses.AddRange(_spousesBefore);
        RomanceTracker.Instance.ExSpouses.Clear(); RomanceTracker.Instance.ExSpouses.AddRange(_exSpousesBefore);
        RomanceTracker.Instance.CurrentLovers.Clear(); RomanceTracker.Instance.CurrentLovers.AddRange(_loversBefore);
        FamilySystem.Instance.AllChildren.Clear(); FamilySystem.Instance.AllChildren.AddRange(_childrenBefore);
        GriefSystem.Instance.Reset();
        GriefSystem.Instance.Deserialize(_griefBefore);
        GameEngine.Instance.CurrentPlayer = _playerBefore;
        GameEngine.PendingNotifications.Clear();
        foreach (var n in _noticesBefore) GameEngine.PendingNotifications.Enqueue(n);
        Events().Clear(); Events().AddRange(_eventsBefore);
        typeof(WorldEventSystem).GetMethod("RecalculateGlobalModifiers", F)!.Invoke(WorldEventSystem.Instance, null);
        typeof(WorldSimulator).GetField("_instance", S)!.SetValue(null, _simBefore);
        typeof(NPCPetitionSystem).GetField("_random", F)!.SetValue(NPCPetitionSystem.Instance, _petitionRandomBefore);
        typeof(CastleLocation).GetField("DetectThreatsRoll", S)?.SetValue(null, _rollBefore);
    }

    // ---------- helpers ----------

    private sealed class ZeroRandom : Random
    {
        public override int Next() => 0;
        public override int Next(int maxValue) => 0;
        public override int Next(int minValue, int maxValue) => minValue;
        public override double NextDouble() => 0;
    }

    private const string HeroName = "Fcii Hero";

    private static Character Hero() => new()
    {
        Name1 = "fcii", Name2 = HeroName, Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 50,
        HP = 50000, MaxHP = 50000, BaseMaxHP = 50000, Mental = 100, Dexterity = 200, Charisma = 10,
        AI = CharacterAI.Human, Gold = 100_000, Sex = CharacterSex.Male, Chivalry = 500, Fame = 500,
    };

    private static NPC Npc(string id, string name, int level = 20)
    {
        var npc = new NPC
        {
            ID = id, Name1 = name, Name2 = name, Class = CharacterClass.Warrior, Race = CharacterRace.Human,
            Level = level, HP = 500, MaxHP = 500, BaseMaxHP = 500, Strength = 300, BaseStrength = 300,
            Gold = 5000, CurrentLocation = "Main Street", Sex = CharacterSex.Female, BirthDate = DateTime.Now,
        };
        var profile = PersonalityProfile.GenerateForArchetype("commoner");
        npc.Personality = profile;
        npc.Brain = new NPCBrain(npc, profile);
        npc.Memory = npc.Brain.Memory;
        return npc;
    }

    /// <summary>A roster large enough for the permadeath floors (45 alive, more than 3 of the race).</summary>
    private static void Crowd(int count = 50, string tag = "crowd")
    {
        for (int i = 0; i < count; i++) NPCSpawnSystem.Instance.ActiveNPCs.Add(Npc($"fcii_{tag}_{i}", $"Fcii {tag} {i}"));
    }

    private static List<WorldEventSystem.WorldEvent> Events() =>
        (List<WorldEventSystem.WorldEvent>)typeof(WorldEventSystem).GetField("_activeEvents", F)!.GetValue(WorldEventSystem.Instance)!;

    private static void StartEvent(WorldEventSystem.EventType type) =>
        Events().Add(new WorldEventSystem.WorldEvent { Type = type, Title = type.ToString(), Description = "", DaysRemaining = 2 });

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

    private static Screen NewScreen(params string[] lines)
    {
        var s = new Screen();
        s.Term = new TerminalEmulator(new LineStream(lines.Concat(Enumerable.Repeat("", 12)), _ =>
        {
            s.Term.StreamWriterInternal?.Flush();
            s.Output.WriteByte((byte)'\n');
        }), s.Output);
        return s;
    }

    private static Screen At(BaseLocation place, Character hero, params string[] lines)
    {
        var s = NewScreen(lines);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(place, s.Term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(place, hero);
        if (place is CastleLocation) typeof(CastleLocation).GetField("playerIsKing", F)!.SetValue(place, true);
        return s;
    }

    private static async Task Run(object target, string method, params object?[] args)
    {
        var m = target.GetType().GetMethod(method, F)!;
        var r = m.Invoke(target, args);
        if (r is Task t) await t;
    }

    private static string Src(params string[] path) =>
        File.ReadAllText(Path.Combine(new[] { HardcodedTextScannerTests.RepoRoot(), "Scripts" }.Concat(path).ToArray())).Replace("\r\n", "\n");

    private static string Between(string text, string from, string to)
    {
        int a = text.IndexOf(from, StringComparison.Ordinal);
        a.Should().BeGreaterThan(0, from);
        int b = text.IndexOf(to, a, StringComparison.Ordinal);
        b.Should().BeGreaterThan(a, to);
        return text.Substring(a, b - a);
    }

    private static void FitsInEnAndHu(string key, string prefix, params object[] args)
    {
        foreach (var lang in new[] { "en", "hu" })
            (prefix + string.Format(Loc.GetIn(lang, key), args)).Length.Should().BeLessOrEqualTo(79, $"{lang} {key}");
    }

    private static async Task<List<string>> News(Func<Task> act)
    {
        var posted = new List<string>();
        NewsSystem.Instance.SetCatchUpBuffer(posted);
        try { await act(); }
        finally { NewsSystem.Instance.ClearCatchUpBuffer(); }
        return posted;
    }

    // ---------- P4: the murder news and warning say the victim will be back ----------

    [Fact]
    public void P4_TheMurderNews_SaysTheVictimWillBeBack()
    {
        Loc.GetIn("en", "street_encounter.news.murdered").Should().NotContain("will not return").And.Contain("will be back");
        foreach (var lang in Langs.Skip(1))
            Loc.GetIn(lang, "street_encounter.news.murdered").Should().NotBe(Loc.GetIn("en", "street_encounter.news.murdered"), lang);
        string es = Loc.GetIn("es", "street_encounter.news.murdered");
        es.Should().NotContain("No volverá");
        Loc.GetIn("hu", "street_encounter.news.murdered").Should().NotContain("Soha nem tér vissza");
        Loc.GetIn("it", "street_encounter.news.murdered").Should().NotContain("Non tornerà più");
        Loc.GetIn("fr", "street_encounter.news.murdered").Should().NotContain("ne reviendra pas");
    }

    [Fact]
    public void P4_TheMurderWarning_SaysTheVictimWillComeBack()
    {
        string en = Loc.GetIn("en", "base.murder_cannot_undo");
        en.Should().NotContain("CANNOT be undone").And.Contain("come back");
        Loc.GetIn("es", "base.murder_cannot_undo").Should().NotContain("NO se puede deshacer");
        Loc.GetIn("fr", "base.murder_cannot_undo").Should().NotContain("IRRÉVERSIBLE");
        Loc.GetIn("hu", "base.murder_cannot_undo").Should().NotContain("NEM lehet visszavonni");
        Loc.GetIn("it", "base.murder_cannot_undo").Should().NotContain("NON PUÒ essere annullata");
        FitsInEnAndHu("base.murder_cannot_undo", "  ");
        // the stale comments that called the murder a permanent death are gone
        Src("Systems", "StreetEncounterSystem.cs").Should().NotContain("deliberate murder = always permadeath")
            .And.NotContain("deliberate murder is always permanent");
    }

    // ---------- P15: Detect Threats uncovers an active plot on half its casts ----------

    private static King PlotKing()
    {
        var king = King.CreateNewKing(HeroName, CharacterAI.Human, CharacterSex.Male);
        king.Treasury = 1_000_000;
        king.MagicBudget = 10_000;
        king.CourtMembers.Clear();
        king.ActivePlots.Clear();
        foreach (var n in new[] { "Lord Early", "Lady Early", "Lord Late", "Lady Late", "Sir Loyal" })
            king.CourtMembers.Add(new CourtMember { Name = n, LoyaltyToKing = 50 });
        king.ActivePlots.Add(new CourtIntrigue { PlotType = "Scandal", Conspirators = new() { "Lord Early", "Lady Early" }, Target = king.Name, Progress = 20 });
        king.ActivePlots.Add(new CourtIntrigue { PlotType = "Coup", Conspirators = new() { "Lord Late", "Lady Late" }, Target = king.Name, Progress = 60 });
        CastleLocation.SetKing(king);
        return king;
    }

    private static void SetRoll(double roll)
    {
        var field = typeof(CastleLocation).GetField("DetectThreatsRoll", S);
        field.Should().NotBeNull("Detect Threats rolls for a plot");
        field!.SetValue(null, (Func<double>)(() => roll));
    }

    [Fact]
    public void P15_TheChance_IsHalf()
    {
        var chance = typeof(GameConfig).GetField("DetectThreatsPlotChance", PS);
        chance.Should().NotBeNull();
        chance!.GetValue(null).Should().Be(0.5);
    }

    [Fact]
    public async Task P15_ARollUnderHalf_UncoversTheMostAdvancedPlot_AndItsPlottersLeaveTheCourt()
    {
        var hero = Hero();
        PlotKing();
        SetRoll(0.49);
        var castle = new CastleLocation();
        var s = At(castle, hero);
        var news = await News(() => Run(castle, "CastDetectThreats"));

        var after = CastleLocation.GetCurrentKing();
        after.MagicBudget.Should().Be(9_500);
        var coup = after.ActivePlots.Single(p => p.PlotType == "Coup");
        coup.IsDiscovered.Should().BeTrue("the most advanced plot is uncovered");
        coup.DaysDiscovered.Should().Be(0);
        after.ActivePlots.Single(p => p.PlotType == "Scandal").IsDiscovered.Should().BeFalse("one plot a cast");
        after.CourtMembers.Select(m => m.Name).Should().NotContain(new[] { "Lord Late", "Lady Late" })
            .And.Contain(new[] { "Lord Early", "Sir Loyal" });
        s.Text.Should().Contain(Loc.Get("castle.detect_plot_found", CastleLocation.PlotTypeLabel("Coup")))
            .And.Contain("Lord Late, Lady Late");
        news.Should().Contain(n => n.Contains(Loc.Get("worldsim.court.plot_discovered.coup", Loc.Get("castle.king"), HeroName)));
        FitsInEnAndHu("castle.detect_plot_found", "", "Assassination");
        FitsInEnAndHu("castle.detect_plotters", "", "Lady Ravencroft, Countess Nightingale, Duchess Silverhart");
        FitsInEnAndHu("castle.detect_plot_arrest", "");
        FitsInEnAndHu("castle.detect_plot_hidden", "");
    }

    [Fact]
    public async Task P15_ARollOfHalfOrMore_LeavesThePlotsSecret()
    {
        var hero = Hero();
        PlotKing();
        SetRoll(0.5);
        var castle = new CastleLocation();
        var s = At(castle, hero);
        await Run(castle, "CastDetectThreats");

        var after = CastleLocation.GetCurrentKing();
        after.MagicBudget.Should().Be(9_500, "the spell is paid either way");
        after.ActivePlots.Should().OnlyContain(p => !p.IsDiscovered);
        after.CourtMembers.Should().HaveCount(5);
        s.Text.Should().Contain(Loc.Get("castle.detect_plot_hidden"));
    }

    [Fact]
    public async Task P15_TheUncoveredPlot_CanBeArrested()
    {
        var hero = Hero();
        PlotKing();
        SetRoll(0.1);
        var castle = new CastleLocation();
        At(castle, hero);
        await Run(castle, "CastDetectThreats");
        var s = At(castle, hero, "1");
        await Run(castle, "CourtAction_Arrest");
        s.Text.Should().NotContain(Loc.Get("castle.no_discovered_plots"));
        CastleLocation.GetCurrentKing().ActivePlots.Should().ContainSingle(p => p.PlotType == "Scandal");
    }

    // ---------- P3: the player grieves a spouse who dies away from the player's fights ----------

    private (Character hero, NPC spouse) MarriedHero()
    {
        var hero = Hero();
        GameEngine.Instance.CurrentPlayer = hero;
        Crowd();
        var spouse = Npc("fcii_spouse", "Fcii Spouse");
        spouse.Married = spouse.IsMarried = true;
        spouse.SpouseName = HeroName;
        NPCSpawnSystem.Instance.ActiveNPCs.Add(spouse);
        RomanceTracker.Instance.Spouses.Add(new Spouse { NPCId = spouse.ID, NPCName = spouse.Name2 });
        return (hero, spouse);
    }

    private static WorldSimulator Sim()
    {
        var sim = new WorldSimulator();
        typeof(WorldSimulator).GetField("random", F)!.SetValue(sim, new ZeroRandom());
        return sim;
    }

    private static bool GrievesFor(string name) => GriefSystem.Instance.GetActiveGriefDetails().Any(g => g.name == name);

    [Fact]
    public void P3_ASpouseKilledInTheWorld_IsGrieved_WithTheMentalLoss_AfterTheNotice()
    {
        var (hero, spouse) = MarriedHero();
        Sim().MarkNPCDead(spouse, 1f, "Fcii Brigand", "Main Street").Should().BeTrue("a permanent death");

        GrievesFor(spouse.Name2).Should().BeTrue();
        hero.Mental.Should().Be(100 - GameConfig.MentalNpcGriefLoss);
        var notices = GameEngine.PendingNotifications.ToList();
        notices.Should().Contain(Loc.Get("grief.spouse_onset", spouse.Name2));
        notices.FindIndex(n => n.Contains(Loc.Get("grief.spouse_onset", spouse.Name2)))
            .Should().BeGreaterThan(notices.FindIndex(n => n.Contains(spouse.Name2) && n != Loc.Get("grief.spouse_onset", spouse.Name2)),
                "the grief line follows the death notice");
        FitsInEnAndHu("grief.spouse_onset", "  ", "Fcii Twenty Chars Nm");
    }

    [Fact]
    public void P3_ASpouseWhoDiesOfOldAge_IsGrieved()
    {
        var (hero, spouse) = MarriedHero();
        spouse.BirthDate = DateTime.Now.AddHours(-GameConfig.NpcLifecycleHoursPerYear * (GameConfig.RaceLifespan[CharacterRace.Human] + 2));
        typeof(WorldSimulator).GetMethod("ProcessNPCAging", F)!.Invoke(Sim(), null);

        spouse.IsAgedDeath.Should().BeTrue();
        GrievesFor(spouse.Name2).Should().BeTrue();
        hero.Mental.Should().Be(100 - GameConfig.MentalNpcGriefLoss);
    }

    [Fact]
    public void P3_ASpouseFallingInThePlayersOwnFight_IsNotGrievedByTheWorld()
    {
        var (hero, spouse) = MarriedHero();
        // CombatEngine.HandleNpcTeammateDeath begins that grief and shows its lines itself
        Sim().MarkNPCDead(spouse, 1f, "Fcii Beast", "the Dungeon", atPlayersSide: true).Should().BeTrue();
        GrievesFor(spouse.Name2).Should().BeFalse();
        hero.Mental.Should().Be(100);
    }

    [Fact]
    public void P3_AnNpcNotMarriedToThePlayer_IsNotGrieved()
    {
        var hero = Hero();
        GameEngine.Instance.CurrentPlayer = hero;
        Crowd();
        var stranger = Npc("fcii_stranger", "Fcii Stranger");
        NPCSpawnSystem.Instance.ActiveNPCs.Add(stranger);
        Sim().MarkNPCDead(stranger, 1f, "Fcii Brigand", "Main Street").Should().BeTrue();
        GriefSystem.Instance.IsGrieving.Should().BeFalse();
        hero.Mental.Should().Be(100);
    }

    [Fact]
    public void P3_ASpouseFoundDeadAtLogin_IsGrieved()
    {
        var (hero, spouse) = MarriedHero();
        Crowd(300, "town");   // the login sync acts only on a roster that looks complete
        spouse.IsDead = spouse.IsPermaDead = true;
        var sync = typeof(RomanceTracker).GetMethods().FirstOrDefault(m => m.Name == "SyncDeadPartners" && m.GetParameters().Length == 2);
        sync.Should().NotBeNull("the login sync names the spouses it retires");
        var retired = new List<(string Id, string Name, bool Aged)>();
        sync!.Invoke(RomanceTracker.Instance, new object?[] { NPCSpawnSystem.Instance.ActiveNPCs, retired });
        retired.Should().ContainSingle().Which.Should().Be((spouse.ID, spouse.Name2, false));

        // the login grieves each one (GameEngine.HealRelationshipStateOnLogin)
        var heal = Between(Src("Core", "GameEngine.cs"), "private void HealRelationshipStateOnLogin()", "Make the correction durable");
        heal.Should().Contain("SyncDeadPartners(NPCSpawnSystem.Instance?.ActiveNPCs, retiredSpouses)")
            .And.Contain("GriefSystem.BeginSpouseGrief(currentPlayer, id, name,");
        var begin = typeof(GriefSystem).GetMethod("BeginSpouseGrief", PS)!;
        begin.Invoke(null, new object?[] { hero, spouse.ID, spouse.Name2, DeathType.Combat }).Should().Be(true);
        GrievesFor(spouse.Name2).Should().BeTrue();
        hero.Mental.Should().Be(100 - GameConfig.MentalNpcGriefLoss);
        begin.Invoke(null, new object?[] { hero, spouse.ID, spouse.Name2, DeathType.Combat }).Should().Be(false, "one grief a death");
        hero.Mental.Should().Be(100 - GameConfig.MentalNpcGriefLoss);
    }

    // ---------- P5: world events ----------

    private static async Task<long> FullHealCost(bool blessedRain)
    {
        if (blessedRain) StartEvent(WorldEventSystem.EventType.BlessedRain);
        var king = King.CreateNewKing("Fcii Taxer", CharacterAI.Computer, CharacterSex.Male);
        king.KingTaxPercent = 5; king.CityTaxPercent = 2;
        CastleLocation.SetKing(king);
        var hero = Hero();
        hero.MaxHP = hero.BaseMaxHP = 1000;
        hero.HP = 100;
        var healer = new HealerLocation();
        At(healer, hero, "Y");
        await Run(healer, "FullHeal");
        hero.HP.Should().Be(hero.MaxHP);
        return 100_000 - hero.Gold;
    }

    [Fact]
    public async Task P5_BlessedRain_MakesAFullHealFree_TaxesIncluded()
    {
        (await FullHealCost(blessedRain: true)).Should().Be(0);
    }

    [Fact]
    public async Task P5_WithoutBlessedRain_AFullHealIsPaid()
    {
        (await FullHealCost(blessedRain: false)).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task P5_BlessedRain_MakesHealingSomeHpFree()
    {
        StartEvent(WorldEventSystem.EventType.BlessedRain);
        var hero = Hero();
        hero.HP = 100;
        var healer = new HealerLocation();
        var s = At(healer, hero, "500");
        await Run(healer, "HealHP");
        hero.HP.Should().Be(600);
        hero.Gold.Should().Be(100_000);
        s.Text.Should().Contain(Loc.Get("healer.blessed_rain_free"));
        FitsInEnAndHu("healer.blessed_rain_free", "  ");
    }

    [Fact]
    public void P5_AHolyDay_MakesTheCloisterAndAtonementFree()
    {
        var hero = Hero();
        var cloister = typeof(TempleLocation).GetProperty("CloisterCost", S);
        cloister.Should().NotBeNull();
        long paidAtonement = (long)typeof(BetrayalSystem).GetMethod("AtonementCost", S)!.Invoke(null, new object[] { hero })!;
        ((long)cloister!.GetValue(null)!).Should().Be(GameConfig.InnerSanctumCost);
        paidAtonement.Should().BeGreaterThan(0);

        StartEvent(WorldEventSystem.EventType.HolyDay);
        ((long)cloister.GetValue(null)!).Should().Be(0);
        ((long)typeof(BetrayalSystem).GetMethod("AtonementCost", S)!.Invoke(null, new object[] { hero })!).Should().Be(0);
        // the meditation reads that price at every step (shown, checked, charged)
        Between(Src("Locations", "TempleLocation.cs"), "private async Task VisitInnerSanctum()", "Grant +1 to a random stat")
            .Should().NotContain("GameConfig.InnerSanctumCost");
        FitsInEnAndHu("temple.holy_day_free", "");
    }

    [Fact]
    public void P5_TheEventDescriptions_SayWhatTheEventsDo()
    {
        string D(string id, string lang = "en") => Loc.GetIn(lang, $"world_event.{id}.desc");
        D("blessed_rain").Should().NotContain("stats").And.Contain("free");
        D("plague_outbreak").Should().NotContain("Max HP");
        D("cursed_land").Should().NotContain("stats");
        D("eclipse_darkness").Should().NotContain("magic");
        D("meteor_shower").Should().NotContain("Magical items").And.Contain("experience");
        D("ancient_relic_found").Should().NotContain("rare items");
        D("winter_solstice").Should().NotContain("stronger").And.Contain("gold");
        D("merchant_caravan").Should().NotContain("rare items").And.Contain("20%");
        D("plague_ends").Should().NotContain("Healers");
        D("holy_day").Should().Contain("Temple services are free");
        foreach (var lang in Langs.Skip(1))
            foreach (var id in new[] { "blessed_rain", "plague_outbreak", "cursed_land", "eclipse_darkness" })
                D(id, lang).Should().NotContain("%", $"{lang} {id} announces no stat or magic change");
    }

    // ---------- P7: a dying wish message is a talk task, paid on delivery ----------

    private static IList Messages(Character c)
    {
        var p = typeof(Character).GetProperty("DyingMessages");
        p.Should().NotBeNull("the player carries the messages promised");
        return (IList)p!.GetValue(c)!;
    }

    private static string Field(object o, string name) => o.GetType().GetProperty(name)!.GetValue(o)!.ToString()!;

    private static (Character hero, NPC elder, NPC recipient) Petition()
    {
        var hero = Hero();
        var elder = Npc("fcii_elder", "Fcii Elder", level: 30);
        elder.Age = 74;
        var recipient = Npc("fcii_recipient", "Fcii Recipient");
        NPCSpawnSystem.Instance.ActiveNPCs.AddRange(new[] { elder, recipient });
        typeof(NPCPetitionSystem).GetField("_random", F)!.SetValue(NPCPetitionSystem.Instance, new ZeroRandom());
        return (hero, elder, recipient);
    }

    private static async Task<Screen> Promise(Character hero, NPC elder)
    {
        var s = NewScreen("P");
        await (Task)typeof(NPCPetitionSystem).GetMethod("ExecuteDyingWish", F)!
            .Invoke(NPCPetitionSystem.Instance, new object[] { elder, hero, s.Term })!;
        return s;
    }

    [Fact]
    public async Task P7_APromise_PaysNothingYet_AndRecordsATaskToTheNamedPerson()
    {
        var (hero, elder, recipient) = Petition();
        long chivalry = hero.Chivalry;
        var s = await Promise(hero, elder);

        hero.Gold.Should().Be(100_000, "the savings are paid on delivery");
        hero.Chivalry.Should().Be(chivalry);
        recipient.Memory.HasMemoryOfEvent(MemoryType.Helped, HeroName, hoursAgo: 24).Should().BeFalse("nothing is delivered yet");
        var message = Messages(hero).Cast<object>().Should().ContainSingle().Subject;
        Field(message, "Recipient").Should().Be(recipient.Name2);
        Field(message, "Elder").Should().Be(elder.Name2);
        Field(message, "Gold").Should().Be((500 + 30 * 50).ToString());
        s.Text.Should().Contain(Loc.Get("petition.dying.legacy_tell", recipient.Name2))
            .And.Contain(Loc.Get("petition.dying.legacy_task", recipient.Name2));
        FitsInEnAndHu("petition.dying.legacy_task", "  ", "Fcii Twenty Chars Nm");
        FitsInEnAndHu("petition.dying.legacy_savings", "  ");
    }

    private static bool Deliver(Character hero, NPC npc, Screen s)
    {
        var deliver = typeof(NPCPetitionSystem).GetMethod("DeliverDyingMessages", PS);
        deliver.Should().NotBeNull();
        return (bool)deliver!.Invoke(null, new object[] { hero, npc, s.Term })!;
    }

    [Fact]
    public async Task P7_TalkingToTheRecipient_DeliversTheMessage_AndPaysTheSavings()
    {
        var (hero, elder, recipient) = Petition();
        await Promise(hero, elder);
        long chivalry = hero.Chivalry;
        var s = NewScreen();
        var news = await News(() => { Deliver(hero, recipient, s).Should().BeTrue(); return Task.CompletedTask; });

        hero.Gold.Should().Be(100_000 + 500 + 30 * 50);
        hero.Chivalry.Should().BeGreaterThan(chivalry);
        Messages(hero).Count.Should().Be(0);
        recipient.Memory.HasMemoryOfEvent(MemoryType.Helped, HeroName, hoursAgo: 24).Should().BeTrue();
        s.Text.Should().Contain(Loc.Get("petition.dying.deliver", recipient.Name2, elder.Name2));
        news.Should().Contain(n => n.Contains(Loc.Get("petition.news_dying_wish", HeroName, elder.Name2)));
        Deliver(hero, recipient, NewScreen()).Should().BeFalse("a message is delivered once");
        hero.Gold.Should().Be(100_000 + 500 + 30 * 50);
        FitsInEnAndHu("petition.dying.deliver", "  ", "Fcii Twenty Chars Nm", "Fcii Twenty Chars Nm");
        FitsInEnAndHu("petition.dying.deliver_moved", "  ", "Fcii Twenty Chars Nm");
        FitsInEnAndHu("petition.dying.legacy_gold", "  ", "Fcii Twenty Chars Nm", "12,500");
    }

    [Fact]
    public async Task P7_TalkingToSomeoneElse_DeliversNothing()
    {
        var (hero, elder, _) = Petition();
        await Promise(hero, elder);
        var other = Npc("fcii_other", "Fcii Other");
        Deliver(hero, other, NewScreen()).Should().BeFalse();
        hero.Gold.Should().Be(100_000);
        Messages(hero).Count.Should().Be(1);
        // the town's talk screen delivers it, beside the talk quests
        Between(Src("Locations", "BaseLocation.cs"), "QuestSystem.OnNPCTalkedTo(currentPlayer, npc.Name2);", "npc.GetGreeting(currentPlayer)")
            .Should().Contain("NPCPetitionSystem.DeliverDyingMessages(currentPlayer, npc, terminal)");
    }

    [Fact]
    public async Task P7_TheMessage_SurvivesTheSave_AndAnOldSaveLoadsWithNone()
    {
        var (hero, elder, _) = Petition();
        await Promise(hero, elder);
        var data = (PlayerData)typeof(SaveSystem).GetMethod("SerializePlayer", F)!.Invoke(SaveSystem.Instance, new object[] { hero })!;
        var json = System.Text.Json.JsonSerializer.Serialize(data);
        json.Should().Contain("\"DyingMessages\"").And.Contain("Fcii Recipient");
        var back = System.Text.Json.JsonSerializer.Deserialize<PlayerData>(json)!;
        var saved = (IList)typeof(PlayerData).GetProperty("DyingMessages")!.GetValue(back)!;
        Field(saved[0]!, "Gold").Should().Be((500 + 30 * 50).ToString());
        // the restore copies them back
        Src("Core", "GameEngine.cs").Should().Contain("player.DyingMessages = playerData.DyingMessages?.Select(");
        // a save from before 1.3.10 has no field
        var old = System.Text.Json.JsonSerializer.Deserialize<PlayerData>("{\"Name1\":\"fcii\"}")!;
        ((IList)typeof(PlayerData).GetProperty("DyingMessages")!.GetValue(old)!).Count.Should().Be(0);
    }
}
