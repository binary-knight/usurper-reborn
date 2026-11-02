using System;
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
/// 1.3.10 audit, the living world and the court (fix piece FC). C1: a player kill (street murder, death spell,
/// execution) is recorded in the victim's family, so kin refuse to talk and to join; the permadeath cascade
/// (spouse, orphans) stays out. C2: a plot the court discovers stays listed until the king arrests it. C3: a jailed
/// NPC runs no world sim activity and no marriage. C4: an NPC married to the player is not wed to another NPC.
/// C5: a rescued prisoner remembers the rescuer and leaves the king's list; a freed NPC cannot be executed. C6: the
/// world status panel shows no stat change (nothing applies one). C7: an NPC with an empty gang id can join a gang.
/// All in memory.
/// </summary>
[Collection("SharedGameSingletons")]
public class LivingWorld1310Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags S = BindingFlags.NonPublic | BindingFlags.Static;

    private readonly King? _kingBefore = CastleLocation.GetCurrentKing();
    private readonly List<NPC> _rosterBefore = NPCSpawnSystem.Instance.ActiveNPCs.ToList();
    private readonly bool _pacing = Pacing.Disabled;
    private readonly List<string> _partyBefore = GameEngine.Instance.DungeonPartyNPCIds.ToList();
    private readonly List<Spouse> _spousesBefore = RomanceTracker.Instance.Spouses.ToList();
    private readonly List<Lover> _loversBefore = RomanceTracker.Instance.CurrentLovers.ToList();
    private readonly List<Child> _childrenBefore = FamilySystem.Instance.AllChildren.ToList();
    private readonly object? _behaviorRandom = typeof(EnhancedNPCBehaviors).GetField("random", S)!.GetValue(null);

    public LivingWorld1310Tests()
    {
        Pacing.Disabled = true;
        NPCSpawnSystem.Instance.ActiveNPCs.Clear();
        GameEngine.Instance.SetDungeonPartyNPCs(Array.Empty<string>());
        RomanceTracker.Instance.Spouses.Clear();
        RomanceTracker.Instance.CurrentLovers.Clear();
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
        RomanceTracker.Instance.CurrentLovers.Clear(); RomanceTracker.Instance.CurrentLovers.AddRange(_loversBefore);
        FamilySystem.Instance.AllChildren.Clear(); FamilySystem.Instance.AllChildren.AddRange(_childrenBefore);
        typeof(EnhancedNPCBehaviors).GetField("random", S)!.SetValue(null, _behaviorRandom);
    }

    // ---------- helpers ----------

    private sealed class ZeroRandom : Random
    {
        public override int Next() => 0;
        public override int Next(int maxValue) => 0;
        public override int Next(int minValue, int maxValue) => minValue;
        public override double NextDouble() => 0;
    }

    private const string HeroName = "Fcten Hero";

    private static Character Hero() => new()
    {
        Name1 = "fcten", Name2 = HeroName, Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 50,
        HP = 50000, MaxHP = 50000, BaseMaxHP = 50000, Mental = 100, Dexterity = 200, Charisma = 10,
        AI = CharacterAI.Human, Gold = 2_000_000, Sex = CharacterSex.Male, Chivalry = 500, Fame = 500,
    };

    private static NPC Npc(string id, string name, GenderIdentity gender = GenderIdentity.Male, int level = 20,
        RelationshipPreference pref = RelationshipPreference.Monogamous)
    {
        var npc = new NPC
        {
            ID = id, Name1 = name, Name2 = name, Class = CharacterClass.Warrior, Race = CharacterRace.Human,
            Level = level, HP = 500, MaxHP = 500, BaseMaxHP = 500, Strength = 300, BaseStrength = 300,
            Gold = 5000, CurrentLocation = "Main Street",
            Sex = gender == GenderIdentity.Female ? CharacterSex.Female : CharacterSex.Male,
        };
        var profile = PersonalityProfile.GenerateForArchetype("commoner");
        profile.Impulsiveness = 0;
        profile.Gender = gender;
        profile.Orientation = SexualOrientation.Straight;
        profile.RelationshipPref = pref;
        npc.Personality = profile;
        npc.Brain = new NPCBrain(npc, profile);
        return npc;
    }

    private static NPC[] Roster(params NPC[] npcs)
    {
        NPCSpawnSystem.Instance.ActiveNPCs.AddRange(npcs);
        return npcs;
    }

    private static WorldSimulator Sim()
    {
        var sim = new WorldSimulator();
        typeof(WorldSimulator).GetField("random", F)!.SetValue(sim, new ZeroRandom());
        return sim;
    }

    private static int Dispatched(NPC npc) =>
        ((Dictionary<string, DateTime>)typeof(NPCBrain).GetField("lastActivities", F)!.GetValue(npc.Brain)!).Count;

    /// <summary>One activity pass of the live (Brain v2) cohort; true when the NPC ran a verb.</summary>
    private static bool ActivityPass(NPC npc)
    {
        int before = Dispatched(npc);
        typeof(WorldSimulator).GetMethod("BrainV2ProcessActivities", F)!.Invoke(Sim(), new object[] { npc, new WorldState(new List<NPC> { npc }) });
        return Dispatched(npc) > before;
    }

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

    private static King HeroKing()
    {
        var king = King.CreateNewKing(HeroName, CharacterAI.Human, CharacterSex.Male);
        king.Treasury = 10_000_000;
        CastleLocation.SetKing(king);
        return king;
    }

    private static void Jail(NPC npc, int days = 7)
    {
        NPCSpawnSystem.Instance.ImprisonNPC(npc, days);
        CastleLocation.GetCurrentKing().Prisoners[npc.Name2] = new PrisonRecord { CharacterName = npc.Name2, Crime = "Theft", Sentence = days };
    }

    private static string Src(params string[] path) =>
        File.ReadAllText(Path.Combine(new[] { HardcodedTextScannerTests.RepoRoot(), "Scripts" }.Concat(path).ToArray())).Replace("\r\n", "\n");

    /// <summary>The text between the first occurrence of from and the first occurrence of to after it.</summary>
    private static string Between(string text, string from, string to)
    {
        int a = text.IndexOf(from, StringComparison.Ordinal);
        a.Should().BeGreaterThan(0, from);
        int b = text.IndexOf(to, a, StringComparison.Ordinal);
        b.Should().BeGreaterThan(a, to);
        return text.Substring(a, b - a);
    }

    // ---------- C1: a player kill reaches the victim's family ----------

    /// <summary>A married parent with an adult son, a young child whose other parent is unknown (so a permanent
    /// death would orphan it), and the parent jailed so the king can execute them.</summary>
    private static (NPC parent, NPC spouse, NPC son, Child young) Family()
    {
        var parent = Npc("fc_parent", "Fcten Parent");
        var spouse = Npc("fc_spouse", "Fcten Spouse", GenderIdentity.Female);
        var son = Npc("fc_son", "Fcten Son");
        son.FatherName = parent.Name2; son.FatherID = parent.ID;
        son.MotherName = spouse.Name2; son.MotherID = spouse.ID;
        parent.Married = parent.IsMarried = true; parent.SpouseName = spouse.Name2;
        spouse.Married = spouse.IsMarried = true; spouse.SpouseName = parent.Name2;
        Roster(parent, spouse, son);
        var young = new Child { Name = "Fcten Young", Father = parent.Name2, FatherID = parent.ID, Age = 6, Location = GameConfig.ChildLocationHome };
        FamilySystem.Instance.AllChildren.Add(young);
        return (parent, spouse, son, young);
    }

    [Fact]
    public async Task C1_AnExecutedParent_TheSonHoldsAGrudge_RefusesToJoin_AndTheCascadeStaysOut()
    {
        var hero = Hero();
        HeroKing();
        var (parent, spouse, son, young) = Family();
        Jail(parent);
        FamilySystem.HasGrudgeAgainst(son, hero).Should().BeFalse("no one has harmed the family yet");
        TeamSystem.GetRecruitmentBand(hero, son).band.Should().NotBe(TeamSystem.RecruitmentBand.Refused);

        var castle = new CastleLocation();
        At(castle, hero, "1", "Y");
        await Run(castle, "ExecutePrisoner");

        parent.IsDead.Should().BeTrue("the king executed the parent");
        FamilySystem.HasGrudgeAgainst(son, hero).Should().BeTrue("the son remembers who killed his father");
        TeamSystem.GetRecruitmentBand(hero, son).band.Should().Be(TeamSystem.RecruitmentBand.Refused, "the son will not join his father's killer");
        son.Enemies.Should().Contain(HeroName);
        // the permadeath cascade stays inside MarkNPCDead: an execution respawns, so no widowing and no orphan
        parent.IsPermaDead.Should().BeFalse();
        spouse.Married.Should().BeTrue(); spouse.SpouseName.Should().Be(parent.Name2);
        parent.Married.Should().BeTrue(); parent.SpouseName.Should().Be(spouse.Name2);
        young.Location.Should().NotBe(GameConfig.ChildLocationOrphanage);
        spouse.Brain!.Memory!.AllMemories.Should().NotContain(m => m.Type == MemoryType.KilledMyFamily && m.InvolvedCharacter != HeroName);

        // the parent respawns; the marriage holds and the grudge stays
        var sim = Sim();
        sim.TrackDeadNPCsForRespawn();
        var respawn = typeof(WorldSimulator).GetMethod("ProcessNPCRespawns", F)!;
        for (int i = 0; i < 25 && parent.IsDead; i++) respawn.Invoke(sim, null);
        parent.IsDead.Should().BeFalse("an executed NPC respawns, as today");
        spouse.Married.Should().BeTrue(); spouse.SpouseName.Should().Be(parent.Name2);
        young.Location.Should().NotBe(GameConfig.ChildLocationOrphanage);
        FamilySystem.HasGrudgeAgainst(son, hero).Should().BeTrue();
    }

    [Fact]
    public void C1_TheStreetMurderAndTheDeathSpell_RecordTheKillInTheFamily()
    {
        var street = Between(Src("Systems", "StreetEncounterSystem.cs"), "// === PERMANENT DEATH (deliberate murder", "// === GOLD THEFT ===");
        street.Should().Contain("WorldSimulator.RecordFamilyDeath(realNpc, player.Name2)", "a street murder reaches the victim's kin");
        var spell = Between(Src("Locations", "MagicShopLocation.cs"), "// Kill the NPC", "// Blood price for dark magic kill");
        spell.Should().Contain("WorldSimulator.RecordFamilyDeath(targetNPC, player.Name2)", "a death spell reaches the victim's kin");
        foreach (var body in new[] { street, spell })
        {
            body.Should().NotContain("MarkNPCDead", "the permadeath cascade is not run for a death that respawns");
            body.Should().NotContain("CheckForOrphanedChildren").And.NotContain("HandleSpouseBereavement").And.NotContain("OnNPCPermadied");
        }
    }

    [Fact]
    public void C1_RecordFamilyDeath_WithNoRelatives_ChangesNoOne()
    {
        var hero = Hero();
        var victim = Npc("fc_lone", "Fcten Lone");
        var stranger = Npc("fc_stranger", "Fcten Stranger");
        Roster(victim, stranger);
        typeof(WorldSimulator).GetMethod("RecordFamilyDeath", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)!
            .Invoke(null, new object[] { victim, HeroName });
        FamilySystem.HasGrudgeAgainst(stranger, hero).Should().BeFalse("a stranger is not kin");
        stranger.Enemies.Should().NotContain(HeroName);
    }

    // ---------- C2: a discovered plot stays listed until the king arrests it ----------

    private static CourtIntrigue Plot(King king, string type, params string[] conspirators) =>
        new() { PlotType = type, Conspirators = conspirators.ToList(), Target = king.Name, Progress = 10 };

    /// <summary>Advances fresh plots until one is discovered (a 6 percent chance a tick for four conspirators).</summary>
    private static CourtIntrigue DiscoverAPlot(King king, params string[] conspirators)
    {
        var advance = typeof(WorldSimulator).GetMethod("AdvancePlot", F)!;
        var sim = new WorldSimulator();
        for (int i = 0; i < 5000; i++)
        {
            var plot = Plot(king, "Coup", conspirators);
            king.ActivePlots.Add(plot);
            advance.Invoke(sim, new object[] { king, plot, new List<(bool Important, string Text)>() });
            if (plot.IsDiscovered) return plot;
            king.ActivePlots.Remove(plot);
        }
        throw new Exception("no plot was discovered in 5000 tries");
    }

    [Fact]
    public void C2_ADiscoveredPlot_StaysInTheActivePlots()
    {
        var king = HeroKing();
        var plot = DiscoverAPlot(king, "Lord A", "Lady B", "Sir C", "Baron D");
        king.ActivePlots.Should().Contain(plot, "a discovered plot waits for the king's arrest");
        // a discovered plot no longer moves: another tick changes nothing
        int progress = plot.Progress;
        typeof(WorldSimulator).GetMethod("AdvancePlot", F)!.Invoke(new WorldSimulator(), new object[] { king, plot, new List<(bool Important, string Text)>() });
        plot.Progress.Should().Be(progress);
        king.ActivePlots.Should().Contain(plot);
    }

    [Fact]
    public void C2_DiscoveredPlots_DoNotStopNewIntrigue()
    {
        var king = HeroKing();
        for (int i = 0; i < 3; i++) king.ActivePlots.Add(new CourtIntrigue { PlotType = "Scandal", Conspirators = new() { "Gone " + i }, IsDiscovered = true });
        king.CourtMembers.Add(new CourtMember { Name = "Lord Sour", LoyaltyToKing = 10 });
        king.CourtMembers.Add(new CourtMember { Name = "Lady Bitter", LoyaltyToKing = 10 });
        typeof(WorldSimulator).GetMethod("ProcessCourtIntrigue", F)!.Invoke(new WorldSimulator(), new object[] { king });
        king.ActivePlots.Count(p => !p.IsDiscovered).Should().Be(1, "the cap of three counts the plots still in motion");
    }

    [Fact]
    public void C2_AnUndiscoveredPlot_StillCountsTowardTheCap()
    {
        var king = HeroKing();
        for (int i = 0; i < 3; i++) king.ActivePlots.Add(new CourtIntrigue { PlotType = "Scandal", Conspirators = new() { "Busy " + i } });
        king.CourtMembers.Add(new CourtMember { Name = "Lord Sour", LoyaltyToKing = 10 });
        king.CourtMembers.Add(new CourtMember { Name = "Lady Bitter", LoyaltyToKing = 10 });
        typeof(WorldSimulator).GetMethod("ProcessCourtIntrigue", F)!.Invoke(new WorldSimulator(), new object[] { king });
        king.ActivePlots.Should().HaveCount(3);
    }

    [Fact]
    public async Task C2_ArrestPlotter_ActsOnADiscoveredPlot_ImprisoningOnlyARealNpc()
    {
        var hero = Hero();
        var king = HeroKing();
        var real = Npc("fc_plotter", "Fcten Plotter");
        Roster(real);
        var plot = DiscoverAPlot(king, real.Name2, "Lord Invented", "Lady Invented", "Sir Invented");
        long treasury = king.Treasury;

        var castle = new CastleLocation();
        var s = At(castle, hero, "1");
        await Run(castle, "CourtAction_Arrest");

        s.Text.Should().NotContain(Loc.Get("castle.no_discovered_plots"));
        var after = CastleLocation.GetCurrentKing();
        after.ActivePlots.Should().NotContain(p => p.IsDiscovered, "the arrest ends the plot");
        after.Treasury.Should().Be(treasury - GameConfig.ArrestTrialCost);
        after.Prisoners.Keys.Should().Contain(real.Name2, "the real NPC conspirator is jailed");
        after.Prisoners.Keys.Should().NotContain("Lord Invented", "a court title with no NPC behind it is not jailed");
    }

    // ---------- C3: a jailed NPC stays in the cell ----------

    [Fact]
    public void C3_AJailedNpc_RunsNoActivity()
    {
        var prisoner = Npc("fc_jailed", "Fcten Jailed");
        Roster(prisoner);
        NPCSpawnSystem.Instance.ImprisonNPC(prisoner, 7);
        ActivityPass(prisoner).Should().BeFalse("a prisoner shops, trains and explores no more");
        prisoner.CurrentLocation.Should().Be("Prison");
        prisoner.Gold.Should().Be(5000);
    }

    [Fact]
    public void C3_AFreedNpc_RunsItsActivities()
    {
        var free = Npc("fc_free", "Fcten Free");
        Roster(free);
        NPCSpawnSystem.Instance.ImprisonNPC(free, 7);
        NPCSpawnSystem.Instance.ReleaseNPC(free);
        ActivityPass(free).Should().BeTrue("the guard holds only a prisoner");
    }

    private static void ForceMarriageRolls() => typeof(EnhancedNPCBehaviors).GetField("random", S)!.SetValue(null, new ZeroRandom());

    [Fact]
    public void C3_AJailedNpc_NeitherSeeksNorIsChosenForMarriage()
    {
        ForceMarriageRolls();
        var groom = Npc("fc_groom", "Fcten Groom");
        var jailedBride = Npc("fc_jbride", "Fcten Jailed Bride", GenderIdentity.Female);
        var all = Roster(groom, jailedBride).ToList();
        NPCSpawnSystem.Instance.ImprisonNPC(jailedBride, 7);
        EnhancedNPCBehaviors.ProcessNPCRelationships(groom, all);
        groom.Married.Should().BeFalse("a prisoner is not a candidate");
        jailedBride.Married.Should().BeFalse();

        var jailedGroom = Npc("fc_jgroom", "Fcten Jailed Groom");
        var bride = Npc("fc_bride", "Fcten Bride", GenderIdentity.Female);
        var pair = Roster(jailedGroom, bride).ToList();
        NPCSpawnSystem.Instance.ImprisonNPC(jailedGroom, 7);
        EnhancedNPCBehaviors.ProcessNPCRelationships(jailedGroom, pair);
        jailedGroom.Married.Should().BeFalse("a prisoner does not seek a marriage");
        bride.Married.Should().BeFalse();
    }

    [Fact]
    public void C3_TwoFreeNpcs_StillMarry()
    {
        ForceMarriageRolls();
        var groom = Npc("fc_fgroom", "Fcten Free Groom");
        var bride = Npc("fc_fbride", "Fcten Free Bride", GenderIdentity.Female);
        var all = Roster(groom, bride).ToList();
        EnhancedNPCBehaviors.ProcessNPCRelationships(groom, all);
        groom.SpouseName.Should().Be(bride.Name2, "the forced rolls marry a free pair");
        bride.SpouseName.Should().Be(groom.Name2);
    }

    // ---------- C4: an NPC married to the player stays married to the player ----------

    [Fact]
    public void C4_AnNpcMarriedToThePlayer_IsNotWedToAnotherNpc()
    {
        ForceMarriageRolls();
        var groom = Npc("fc_suitor", "Fcten Suitor");
        var wife = Npc("fc_wife", "Fcten Wife", GenderIdentity.Female, pref: RelationshipPreference.Polyamorous);
        wife.Married = wife.IsMarried = true; wife.SpouseName = HeroName;   // as RelationshipSystem marries the player
        RomanceTracker.Instance.Spouses.Add(new Spouse { NPCId = wife.ID, NPCName = wife.Name2 });
        var all = Roster(groom, wife).ToList();
        EnhancedNPCBehaviors.ProcessNPCRelationships(groom, all);
        wife.SpouseName.Should().Be(HeroName, "the player's spouse keeps the player as spouse");
        groom.Married.Should().BeFalse();
    }

    [Fact]
    public void C4_OnlineToo_AnNpcWhoseSpouseIsNoNpc_IsNotWed()
    {
        // online the sim thread's romance tracker is empty: the spouse is known to be a player by naming no NPC
        ForceMarriageRolls();
        var groom = Npc("fc_suitor2", "Fcten Suitor Two");
        var wife = Npc("fc_wife2", "Fcten Wife Two", GenderIdentity.Female, pref: RelationshipPreference.Polyamorous);
        wife.Married = wife.IsMarried = true; wife.SpouseName = "Some Player";
        var all = Roster(groom, wife).ToList();
        EnhancedNPCBehaviors.ProcessNPCRelationships(groom, all);
        wife.SpouseName.Should().Be("Some Player");
        groom.Married.Should().BeFalse();
    }

    [Fact]
    public void C4_APolyNpcMarriedToAnNpc_CanStillTakeAnotherSpouse()
    {
        ForceMarriageRolls();
        var groom = Npc("fc_suitor3", "Fcten Suitor Three");
        var wife = Npc("fc_wife3", "Fcten Wife Three", GenderIdentity.Female, pref: RelationshipPreference.Polyamorous);
        var husband = Npc("fc_husband3", "Fcten Husband Three", level: 50);   // out of the suitor's level range
        wife.Married = wife.IsMarried = true; wife.SpouseName = husband.Name2;
        husband.Married = husband.IsMarried = true; husband.SpouseName = wife.Name2;
        var all = Roster(groom, wife, husband).ToList();
        EnhancedNPCBehaviors.ProcessNPCRelationships(groom, all);
        groom.SpouseName.Should().Be(wife.Name2, "an NPC to NPC poly union is unchanged");
    }

    // ---------- C5: a rescued prisoner remembers, and a freed NPC cannot be executed ----------

    [Fact]
    public async Task C5_ARescuedNpc_RemembersTheRescuer_AndLeavesTheKingsList()
    {
        var hero = Hero();
        HeroKing();
        var prisoner = Npc("fc_rescued", "Fcten Rescued");
        Roster(prisoner);
        Jail(prisoner);
        var s = NewScreen();
        var walk = new PrisonWalkLocation(GameEngine.Instance, s.Term);   // the prison walk keeps its own terminal
        await Run(walk, "FreePrisonerSuccessfully", hero, prisoner);

        prisoner.DaysInPrison.Should().Be(0);
        prisoner.Memory!.AllMemories.Should().Contain(m => m.Type == MemoryType.Saved && m.InvolvedCharacter == HeroName,
            "the rescued NPC remembers who freed them");
        prisoner.Memory.GetCharacterImpression(HeroName).Should().BeGreaterThan(0);
        CastleLocation.GetCurrentKing().Prisoners.Keys.Should().NotContain(prisoner.Name2, "the king's list drops the freed prisoner");
        s.Text.Should().Contain(Loc.Get("prison_walk.friend"));
    }

    [Fact]
    public async Task C5_AFreedNpc_StillOnTheList_IsNotExecuted()
    {
        var hero = Hero();
        HeroKing();
        var freed = Npc("fc_freed", "Fcten Freed");
        Roster(freed);
        Jail(freed);
        NPCSpawnSystem.Instance.ReleaseNPC(freed, "Someone");   // freed by an older release, the record left behind
        long darkness = hero.Darkness;

        var castle = new CastleLocation();
        var s = At(castle, hero, "1", "Y");
        await Run(castle, "ExecutePrisoner");

        freed.IsDead.Should().BeFalse("an NPC already free is not executed");
        freed.CurrentLocation.Should().Be("Main Street");
        s.Text.Should().Contain(Loc.Get("castle.not_in_dungeon"));
        s.Text.Should().NotContain(Loc.Get("castle.executed_confirm", freed.Name2));
        hero.Darkness.Should().Be(darkness, "no execution, no darkness");
        CastleLocation.GetCurrentKing().Prisoners.Keys.Should().NotContain(freed.Name2, "the stale record is cleared");
    }

    [Fact]
    public async Task C5_AJailedNpc_IsStillExecuted()
    {
        var hero = Hero();
        HeroKing();
        var jailed = Npc("fc_doomed", "Fcten Doomed");
        Roster(jailed);
        Jail(jailed);
        var castle = new CastleLocation();
        At(castle, hero, "1", "Y");
        await Run(castle, "ExecutePrisoner");
        jailed.IsDead.Should().BeTrue();
        CastleLocation.GetCurrentKing().Prisoners.Keys.Should().NotContain(jailed.Name2);
    }

    // ---------- C6: the world status panel shows no stat change ----------

    [Fact]
    public void C6_TheWorldStatusPanel_ShowsNoStatsRow()
    {
        var events = WorldEventSystem.Instance;
        var stat = typeof(WorldEventSystem).GetProperty("GlobalStatModifier")!;
        var price = typeof(WorldEventSystem).GetProperty("GlobalPriceModifier")!;
        int statBefore = events.GlobalStatModifier;
        float priceBefore = events.GlobalPriceModifier;
        try
        {
            stat.SetValue(events, -3);
            price.SetValue(events, 0.8f);
            var screen = new Screen();
            screen.Term = new TerminalEmulator(new LineStream(Enumerable.Repeat("", 4)), screen.Output);
            events.DisplayWorldStatus(screen.Term);
            string text = screen.Text;
            text.Should().Contain(Loc.Get("world_event.mod_prices", "-20"), "the applied modifiers are still shown");
            text.Should().NotContain(Loc.Get("world_event.mod_stats", "-3"), "nothing applies the stat modifier, so it is not shown");
        }
        finally
        {
            stat.SetValue(events, statBefore);
            price.SetValue(events, priceBefore);
        }
    }

    // ---------- C7: gangs after a restart ----------

    [Fact]
    public void C7_AnNpcWithAnEmptyGangId_JoinsAGang()
    {
        var leader = Npc("fc_boss", "Fcten Boss");
        var joiner = Npc("fc_joiner", "Fcten Joiner");
        joiner.GangId = "";   // as every loader sets it
        leader.GangMembers.Add("someone");
        var world = new WorldState(new List<NPC> { leader, joiner });
        typeof(WorldSimulator).GetMethod("ExecuteJoinGang", F)!.Invoke(Sim(), new object[] { joiner, leader.Id, world });
        joiner.GangId.Should().Be(leader.Id);
        leader.GangMembers.Should().Contain(joiner.Id);
    }

    [Fact]
    public void C7_AnNpcInAGang_DoesNotJoinAnother()
    {
        var leader = Npc("fc_boss2", "Fcten Boss Two");
        var member = Npc("fc_member", "Fcten Member");
        member.GangId = "another-gang";
        var world = new WorldState(new List<NPC> { leader, member });
        typeof(WorldSimulator).GetMethod("ExecuteJoinGang", F)!.Invoke(Sim(), new object[] { member, leader.Id, world });
        member.GangId.Should().Be("another-gang");
    }

    [Fact]
    public void C7_TheBrain_OffersJoiningAGang_ToAnNpcWithAnEmptyGangId()
    {
        var joiner = Npc("fc_joiner2", "Fcten Joiner Two");
        joiner.Personality!.Loyalty = joiner.Personality.Sociability = joiner.Personality.Ambition = 0.9f;
        joiner.GangId = "";
        var actions = (List<NPCAction>)typeof(NPCBrain).GetMethod("GenerateSocialActions", F)!
            .Invoke(joiner.Brain, new object[] { new WorldState(new List<NPC> { joiner }) })!;
        actions.Should().Contain(a => a.Type == ActionType.JoinGang);
    }
}
