using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;
using static UsurperReborn.Tests.PartySurvival133Harness;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.3 party survival (PS-2 1.1 to 1.3, 1.6, 1.7, 1.8): a story companion or NPC ally at 0 HP in a fight
/// is Downed and stays in the party; one idempotent step decides at every combat shape; the fight's end
/// brings a Downed ally up at max(1, MaxHP / 2); a down is not a loss (teammates_lost, grief, sacrifice).
///
/// The !IsAlive inventory (PS-2 1.3): every negated IsAlive line in Scripts at 490e679d, 159 lines, with
/// its class. A: a sweep that turns HP 0 into a death, removal or world action; it treats Downed as nothing
/// to do (CombatEngine through EnterDownedOrDie; the world sim, resurrection lists and counts skip IsDowned).
/// B: HP 0 already reads as nothing to do (no change). C: grouped players only (out of scope, unchanged).
/// D: display that shows Downed (the two allies boxes, 13100 and 13436, draw a Downed ally as DOWN; the others
/// once recovery runs). E: not party related (player, monsters, PvP, world NPC
/// fights). Line numbers are at 490e679d.
/// [E] EnhancedNPCBehaviorSystem.cs:418
/// [E] EnhancedNPCBehaviorSystem.cs:422
/// [E] EnhancedNPCBehaviors.cs:316
/// [E] EnhancedNPCBehaviors.cs:490
/// [E] EnhancedNPCBehaviors.cs:512
/// [E] EnhancedNPCBehaviors.cs:902
/// [E] NPCCombatSimulator.cs:135
/// [E] NPCCombatSimulator.cs:150
/// [E] NPCCombatSimulator.cs:180
/// [E] NPCCombatSimulator.cs:282
/// [E] NPCCombatSimulator.cs:405
/// [E] NPCCombatSimulator.cs:589
/// [E] GameEngine.cs:5337
/// [E] Monster.cs:519
/// [E] BaseLocation.cs:705
/// [E] BaseLocation.cs:719
/// [E] BaseLocation.cs:731
/// [E] BaseLocation.cs:792
/// [E] ChurchLocation.cs:885
/// [E] ChurchLocation.cs:899
/// [E] ChurchLocation.cs:1108
/// [E] DarkAlleyLocation.cs:2372
/// [E] DormitoryLocation.cs:565
/// [E] DormitoryLocation.cs:918
/// [E] DormitoryLocation.cs:923
/// [C] DungeonLocation.cs:4833
/// [C] DungeonLocation.cs:4858
/// [C] DungeonLocation.cs:4909
/// [B] DungeonLocation.cs:7399
/// [B] DungeonLocation.cs:15641
/// [B] DungeonLocation.cs:18631
/// [D] DungeonLocation.cs:19019
/// [C] DungeonLocation.cs:19544
/// [A] HomeLocation.cs:4466
/// [A] HomeLocation.cs:4478
/// [E] InnLocation.cs:5875
/// [A] MainStreetLocation.cs:1025
/// [A] TeamCornerLocation.cs:2801
/// [A] TeamCornerLocation.cs:2808
/// [A] TeamCornerLocation.cs:2856
/// [E] BetrayalSystem.cs:684
/// [D] CombatEngine.cs:104
/// [E] CombatEngine.cs:898
/// [E] CombatEngine.cs:988
/// [E] CombatEngine.cs:1526
/// [E] CombatEngine.cs:1545
/// [E] CombatEngine.cs:1664
/// [E] CombatEngine.cs:1809
/// [E] CombatEngine.cs:1927
/// [E] CombatEngine.cs:1983
/// [E] CombatEngine.cs:1992
/// [E] CombatEngine.cs:2071
/// [E] CombatEngine.cs:2140
/// [E] CombatEngine.cs:2205
/// [E] CombatEngine.cs:2212
/// [E] CombatEngine.cs:2246
/// [E] CombatEngine.cs:2257
/// [E] CombatEngine.cs:5075
/// [E] CombatEngine.cs:5127
/// [E] CombatEngine.cs:5146
/// [E] CombatEngine.cs:5173
/// [E] CombatEngine.cs:5503
/// [E] CombatEngine.cs:5514
/// [E] CombatEngine.cs:6153
/// [B] CombatEngine.cs:7434
/// [B] CombatEngine.cs:7450
/// [E] CombatEngine.cs:7563
/// [E] CombatEngine.cs:7592
/// [E] CombatEngine.cs:8761
/// [E] CombatEngine.cs:9182
/// [E] CombatEngine.cs:9239
/// [E] CombatEngine.cs:9508
/// [E] CombatEngine.cs:9627
/// [B] CombatEngine.cs:11611
/// [C] CombatEngine.cs:11969
/// [E] CombatEngine.cs:12918
/// [D] CombatEngine.cs:13100
/// [E] CombatEngine.cs:13347
/// [D] CombatEngine.cs:13436
/// [E] CombatEngine.cs:13867
/// [E] CombatEngine.cs:15239
/// [E] CombatEngine.cs:15270
/// [E] CombatEngine.cs:15283
/// [E] CombatEngine.cs:15455
/// [E] CombatEngine.cs:15534
/// [E] CombatEngine.cs:15554
/// [E] CombatEngine.cs:15598
/// [E] CombatEngine.cs:15633
/// [E] CombatEngine.cs:15660
/// [B] CombatEngine.cs:18607
/// [B] CombatEngine.cs:18750
/// [E] CombatEngine.cs:19066
/// [B] CombatEngine.cs:19938
/// [A] CombatEngine.cs:19949
/// [E] CombatEngine.cs:20074
/// [E] CombatEngine.cs:20762
/// [A] CombatEngine.cs:21794
/// [A] CombatEngine.cs:21834
/// [A] CombatEngine.cs:22023
/// [A] CombatEngine.cs:22025
/// [C] CombatEngine.cs:23554
/// [C] CombatEngine.cs:27110
/// [E] CombatEngine.cs:27529
/// [E] CombatEngine.cs:28174
/// [E] CombatEngine.cs:28482
/// [E] CombatEngine.cs:28576
/// [E] CombatEngine.cs:28591
/// [E] CombatEngine.cs:29217
/// [B] CombatEngine.cs:30032
/// [B] CombatEngine.cs:30122
/// [B] CombatEngine.cs:31384
/// [B] CombatEngine.cs:31395
/// [B] CombatEngine.cs:31407
/// [B] CombatEngine.cs:31419
/// [B] CombatEngine.cs:31440
/// [E] CombatEngine.cs:31474
/// [E] CombatEngine.cs:31525
/// [E] CombatEngine.cs:31763
/// [A] CombatEngine.cs:31928
/// [D] CombatEngine.cs:32141
/// [A] CombatEngine.cs:32195
/// [E] CombatEvents.cs:203
/// [B] CompanionSystem.cs:2406
/// [E] EliteAffixes.cs:48
/// [E] EliteAffixes.cs:103
/// [E] EliteAffixes.cs:108
/// [B] FeatureInteractionSystem.cs:1140
/// [E] HeadlessCombatResolver.cs:38
/// [E] HeadlessCombatResolver.cs:45
/// [E] HeadlessCombatResolver.cs:46
/// [E] LocationManager.cs:419
/// [B] MiracleSystem.cs:128
/// [E] OpeningSequence.cs:50
/// [E] PrisonActivitySystem.cs:235
/// [E] TeamSystem.cs:1063
/// [E] VisualNovelDialogueSystem.cs:693
/// [E] VisualNovelDialogueSystem.cs:2551
/// [A] WorldSimService.cs:164
/// [A] WorldSimulator.cs:658
/// [A] WorldSimulator.cs:768
/// [A] WorldSimulator.cs:1276
/// [A] WorldSimulator.cs:1281
/// [E] WorldSimulator.cs:2976
/// [E] WorldSimulator.cs:3840
/// [E] WorldSimulator.cs:3905
/// [E] WorldSimulator.cs:4478
/// [E] WorldSimulator.cs:4647
/// [E] WorldSimulator.cs:4739
/// [E] WorldSimulator.cs:5021
/// [E] WorldSimulator.cs:5088
/// [E] WorldSimulator.cs:5718
/// [E] WorldSimulator.cs:5749
/// [E] WorldSimulator.cs:6565
/// [E] WorldSimulator.cs:6588
/// [E] WorldSimulator.cs:6607
/// [E] WorldSimulator.cs:6629
/// [E] WorldSimulator.cs:7402
/// [E] WorldSimulator.cs:7418
/// [E] WorldSimulator.cs:7666
/// </summary>
[Collection("SharedGameSingletons")]
public class DownedState_PartySurvival133Tests
{
    // ---------- 1. an ally in scope at 0 HP is Downed and stays; an out of scope actor still dies ----------

    [Fact]
    public async Task AnNpcAllyAtZero_IsDowned_AndStaysInTheParty()
    {
        var ally = Ally();
        var r = NewRig(team: new[] { ally });
        await Down(r, ally);
        ally.IsDowned.Should().BeTrue();
        ally.HP.Should().Be(0);
        r.Result.Teammates.Should().Contain(ally, "a Downed ally is not removed");
        r.Engine.StatsFor(ally).Downs.Should().Be(1);
        r.Text.Should().Contain(Loc.Get("combat.ally_downed", ally.DisplayName));
        r.Text.Should().NotContain(Loc.Get("combat.npc_fallen", ally.DisplayName).Trim(), "no death scene at a down");
    }

    [Fact]
    public async Task AStoryCompanionAtZero_IsDowned_NotKilled()
    {
        try
        {
            var w = FreshCompanion();
            var r = NewRig(team: new[] { w });
            await Down(r, w);
            w.IsDowned.Should().BeTrue();
            r.Result.Teammates.Should().Contain(w);
            CompanionSystem.Instance.GetCompanion(CompanionId.Aldric)!.IsDead.Should().BeFalse("a down is not a death");
        }
        finally { RestoreSilence(); }
    }

    [Fact]
    public async Task AGroupedPlayerAtZero_StillDies_AsBefore()
    {
        var follower = Ally("Beta");
        follower.GroupPlayerUsername = "beta";
        follower.RemoteTerminal = new TerminalEmulator(new System.IO.MemoryStream(), new System.IO.MemoryStream());
        var r = NewRig(team: new[] { follower });
        await Down(r, follower);
        follower.IsDowned.Should().BeFalse("a grouped player is out of scope");
        r.Result.Teammates.Should().NotContain(follower, "the death dispatch removed them");
        follower.PendingGroupDeath.Should().NotBeNullOrEmpty("their own session resolves the death");
    }

    [Fact]
    public async Task AMercenaryOrEcho_IsOutOfScope()
    {
        var echo = Ally("Echo"); echo.IsEcho = true;
        var r = NewRig(team: new[] { echo });
        await Down(r, echo);
        echo.IsDowned.Should().BeFalse();
        r.Result.Teammates.Should().NotContain(echo);
        CombatEngine.IsDownable(new Character { IsMercenary = true }).Should().BeFalse();
        CombatEngine.IsDownable(new Character { IsMiracleAlly = true }).Should().BeFalse();
        CombatEngine.IsDownable(Ally()).Should().BeTrue();
    }

    // ---------- 2. the step is idempotent ----------

    [Fact]
    public async Task TheStep_OnADownedAlly_ChangesNothing()
    {
        var ally = Ally();
        var r = NewRig(team: new[] { ally });
        await Down(r, ally);
        int len = r.Output.ToArray().Length;
        await r.Engine.EnterDownedOrDie(ally, "Wolf", null, r.Result);
        await r.Engine.EnterDownedOrDie(ally, "Wolf", null, r.Result);
        r.Engine.StatsFor(ally).Downs.Should().Be(1);
        r.Output.ToArray().Length.Should().Be(len, "no second down line");
        ally.HP = 30; ally.Lifecycle = AllyLifecycle.Active;
        await r.Engine.EnterDownedOrDie(ally, "Wolf", null, r.Result);
        ally.IsDowned.Should().BeFalse("above 0 HP the step does nothing");
    }

    // ---------- 3. the boss loop leaves a Downed ally untouched ----------

    [Fact]
    public async Task TheBossLoop_LeavesADownedAllyAlone_ForThreeRounds_AndDownsANewlyFallenOne()
    {
        var downed = Ally("Mira");
        var fresh = Ally("Kade");
        var boss = Mob("Old One", "Demonic", boss: true, hp: 50000);
        var r = NewRig(team: new[] { downed, fresh }, monsters: new[] { boss });
        r.Engine.BossContext = new BossCombatContext();
        await Down(r, downed);
        for (int round = 2; round <= 4; round++)
        {
            r.Result.CurrentRound = round;
            await CallAsync(r.Engine, "ProcessBossRoundMechanics", boss, r.Owner, r.Result, round);
            downed.IsDowned.Should().BeTrue("round {0}", round);
            r.Result.Teammates.Should().Contain(downed, "round {0}", round);
        }
        r.Engine.StatsFor(downed).Downs.Should().Be(1);
        fresh.HP = 0;   // fallen to corruption this round
        await CallAsync(r.Engine, "ProcessBossRoundMechanics", boss, r.Owner, r.Result, 5);
        fresh.IsDowned.Should().BeTrue("the loop hands a new fall to the step");
        r.Result.Teammates.Should().Contain(fresh);
    }

    // ---------- 12. the sacrifice never picks a Downed companion; a sacrifice never downs ----------

    [Fact]
    public void TheSacrifice_SkipsADownedCompanion_AndPicksAStandingOne()
    {
        try
        {
            var w = FreshCompanion(CompanionId.Aldric, loyalty: 100);
            var player = Hero(hp: 10);
            CompanionSystem.Instance.CheckForSacrifice(player, 50, new HashSet<CompanionId>())
                .Should().BeNull("Aldric is not standing in the fight");
            CompanionSystem.Instance.CheckForSacrifice(player, 50, new HashSet<CompanionId> { CompanionId.Aldric })
                .Should().NotBeNull("a loyal Aldric standing in the fight sacrifices");
        }
        finally { RestoreSilence(); }
    }

    [Fact]
    public async Task TheSacrificeSite_PassesOnlyStandingCompanions_AndKillsDirectly()
    {
        try
        {
            var w = FreshCompanion(CompanionId.Aldric, loyalty: 100);
            var r = NewRig(owner: Hero(hp: 10), team: new[] { w }, script: Enters);
            w.HP = 0; w.Lifecycle = AllyLifecycle.Downed;
            var res = await (Task<(bool, CompanionId?)>)Call(r.Engine, "CheckCompanionSacrifice", r.Owner, 50, r.Result)!;
            res.Item1.Should().BeFalse("a Downed companion never sacrifices");
            w.HP = w.MaxHP; w.Lifecycle = AllyLifecycle.Active;
            res = await (Task<(bool, CompanionId?)>)Call(r.Engine, "CheckCompanionSacrifice", r.Owner, 50, r.Result)!;
            res.Item1.Should().BeTrue();
            w.IsDowned.Should().BeFalse("a sacrifice is a direct death, never a down");
            r.Result.Teammates.Should().NotContain(w);
            CompanionSystem.Instance.GetCompanion(CompanionId.Aldric)!.IsDead.Should().BeTrue();
        }
        finally { RestoreSilence(); }
    }

    // ---------- 13. grief never begins at a down ----------

    [Fact]
    public async Task Grief_DoesNotBeginAtADown()
    {
        try
        {
            var w = FreshCompanion();
            var r = NewRig(team: new[] { w });
            await Down(r, w);
            GriefSystem.Instance.IsGrieving.Should().BeFalse("a down begins no grief");
            GriefsBegun().Should().Be(0, "no grief state at all, live or not");
            GriefSystem.Instance.GetActiveGriefDetails().Should().BeEmpty();
        }
        finally { RestoreSilence(); }
    }

    // ---------- 15. recovery: exactly max(1, MaxHP / 2), only for a Downed ally ----------

    [Fact]
    public async Task Recovery_SetsHalfMaxHp_OnlyForADownedAlly()
    {
        var downed = Ally("Mira", 100, 101);
        var low = Ally("Kade", 10, 100);
        var tiny = Ally("Pip", 1, 1);
        var r = NewRig(team: new[] { downed, low, tiny });
        await Down(r, downed);
        await Down(r, tiny);
        r.Engine.ResolveDownedAtFightEnd(r.Result);
        downed.HP.Should().Be(50);
        downed.IsDowned.Should().BeFalse();
        tiny.HP.Should().Be(1, "max(1, MaxHP / 2)");
        low.HP.Should().Be(10, "a standing ally at 10 percent keeps 10 percent");
        r.Text.Should().Contain(Loc.Get("combat.ally_recovers", downed.DisplayName, 50, 101));
        r.Engine.ResolveDownedAtFightEnd(r.Result);
        downed.HP.Should().Be(50, "a second call is a no-op");
    }

    [Fact]
    public async Task Recovery_KeepsTheCompanionMirrorAboveZero()
    {
        try
        {
            var w = FreshCompanion();
            var r = NewRig(team: new[] { w });
            await Down(r, w);
            r.Engine.ResolveDownedAtFightEnd(r.Result);
            var again = CompanionSystem.Instance.GetCompanionsAsCharacters().Single();
            again.HP.Should().Be(Math.Max(1, w.MaxHP / 2), "the mirror holds the recovered HP, never 0");
        }
        finally { RestoreSilence(); }
    }

    // ---------- 16. recovery runs on win, flee, Death's Door and the player's death ----------

    private static async Task<(CombatResult? res, Exception? err, string text)> FightWithDowned(Character hero, List<Monster> monsters, Character ally, string script)
    {
        ally.HP = 0; ally.Lifecycle = AllyLifecycle.Downed;
        var output = new System.IO.MemoryStream();
        var term = new TerminalEmulator(new ScriptedStream(script), output);
        var e = new CombatEngine(term);
        e.SeedRandomForTests(133);
        try
        {
            var task = e.PlayerVsMonsters(hero, monsters, new List<Character> { ally }, offerMonkEncounter: false);
            var done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(60)));
            if (done != task) throw new TimeoutException("the fight did not end");
            return (await task, null, System.Text.Encoding.UTF8.GetString(output.ToArray()));
        }
        catch (Exception ex) { return (null, ex, System.Text.Encoding.UTF8.GetString(output.ToArray())); }
    }

    private static string Attacks(int n = 30) => string.Concat(Enumerable.Repeat("A\n\n", n)) + string.Concat(Enumerable.Repeat("P\n", 10));

    [Fact]
    public async Task Recovery_RunsOnAWin()
    {
        var ally = Ally();
        var (res, err, text) = await FightWithDowned(Hero(), new List<Monster> { Mob("Rat", "Goblinoid", hp: 1, str: 1) }, ally, Attacks());
        err.Should().BeNull("{0}", text);
        res!.Outcome.Should().Be(CombatOutcome.Victory);
        ally.HP.Should().Be(50); ally.IsDowned.Should().BeFalse();
        res.Teammates.Should().Contain(ally);
    }

    [Fact]
    public async Task Recovery_RunsOnAFlee()
    {
        var ally = Ally();
        var (res, err, text) = await FightWithDowned(Hero(), new List<Monster> { Mob("Rat", "Goblinoid", hp: 50000, str: 1) }, ally,
            string.Concat(Enumerable.Repeat("R\nY\n", 40)));
        err.Should().BeNull("{0}", text);
        res!.Outcome.Should().Be(CombatOutcome.PlayerEscaped, "{0}", text);
        ally.HP.Should().Be(50); ally.IsDowned.Should().BeFalse();
    }

    [Fact]
    public async Task Recovery_RunsAtDeathsDoor()
    {
        var ally = Ally();
        var hero = Hero(hp: 200);
        hero.IsExhibitionCombat = true;
        var (res, err, text) = await FightWithDowned(hero, new List<Monster> { Mob("Crusher", "Goblinoid", hp: 400, str: 60000) }, ally, Attacks());
        err.Should().BeNull("{0}", text);
        res!.Outcome.Should().Be(CombatOutcome.PlayerDied, "1.3.6 rule 2: Last Stand keeps the fight going, the next blow kills: {0}", text);
        ally.HP.Should().Be(50); ally.IsDowned.Should().BeFalse();
    }

    [Fact]
    public async Task Recovery_RunsOnThePlayersDeath()
    {
        var ally = Ally();
        var hero = Hero(hp: 20);
        hero.IsExhibitionCombat = true;   // a loss without the death screens
        var (res, err, text) = await FightWithDowned(hero, new List<Monster> { Mob("Brute", "Goblinoid", hp: 400, str: 400) }, ally, Attacks());
        err.Should().BeNull("{0}", text);
        res!.Outcome.Should().Be(CombatOutcome.PlayerDied, "{0}", text);
        ally.HP.Should().Be(50); ally.IsDowned.Should().BeFalse();
    }

    // ---------- 17. teammates_lost counts a finish once; a down alone counts 0 ----------

    [Fact]
    public async Task TeammatesLost_ADownCountsZero_AFinishCountsOnce()
    {
        var ally = Ally();
        var wolf = Mob();
        var r = NewRig(team: new[] { ally }, monsters: new[] { wolf });
        BeginEvents(r);
        ally.HP = 0;
        Call(r.Engine, "EvHurt", ally, 120L, CombatEventKind.Basic);
        await r.Engine.EnterDownedOrDie(ally, wolf.Name, wolf, r.Result);
        r.Result.Tally.TeammatesLost.Should().Be(0, "a down is not a loss");
        (await r.Engine.TryFinishOrAnnounce(wolf, r.Result)).Should().BeTrue("announce");
        r.Result.CurrentRound = 2;
        (await r.Engine.TryFinishOrAnnounce(wolf, r.Result)).Should().BeTrue("finish");
        r.Result.Tally.TeammatesLost.Should().Be(1);
        Call(r.Engine, "EvHurt", ally, 5L, CombatEventKind.Basic);
        r.Result.Tally.TeammatesLost.Should().Be(1, "an ally counts once a fight");
    }

    [Fact]
    public void TeammatesLost_AGroupedPlayerStillFallsOnTheHit()
    {
        var follower = Ally("Beta");
        follower.GroupPlayerUsername = "beta";
        follower.RemoteTerminal = new TerminalEmulator(new System.IO.MemoryStream(), new System.IO.MemoryStream());
        var r = NewRig(team: new[] { follower });
        BeginEvents(r);
        follower.HP = 0;
        Call(r.Engine, "EvHurt", follower, 120L, CombatEventKind.Basic);
        r.Result.Tally.TeammatesLost.Should().Be(1);
    }

    // ---------- the allies box draws a Downed ally as DOWN (the played pilot reads it) ----------

    private static string Plain(string s) => System.Text.RegularExpressions.Regex.Replace(s, "\u001b\\[[0-9;?]*[A-Za-z]", "");

    [Theory]
    [InlineData("DisplayCombatStatus", "en")]
    [InlineData("DisplayCombatStatus", "hu")]
    [InlineData("DisplayCombatStatusScreenReader", "en")]
    [InlineData("DisplayCombatStatusScreenReader", "hu")]
    public async Task TheAlliesBox_ShowsADownedAllyAsDown_AndAStandingRowAsBefore(string box, string lang)
    {
        var before = GameConfig.Language;
        GameConfig.Language = lang;
        try
        {
            var downed = Ally("Mira"); var standing = Ally("Kade", 80, 100);
            var wolf = Mob();
            var r = NewRig(team: new[] { downed, standing }, monsters: new[] { wolf });
            Call(r.Engine, box, r.Result.Monsters, r.Owner, 1);
            string standingBefore = Plain(r.Text).Split('\n').Single(l => l.Contains("Kade"));
            await Down(r, downed);
            int at = r.Output.ToArray().Length;
            Call(r.Engine, box, r.Result.Monsters, r.Owner, 1);
            var rows = Plain(System.Text.Encoding.UTF8.GetString(r.Output.ToArray()[at..])).Split('\n');
            string marker = Loc.Get("combat.box_down");
            var downRow = rows.Single(l => l.Contains("Mira"));
            downRow.TrimEnd().Should().EndWith(marker, "the marker stands in place of the HP");
            downRow.Should().NotContain(Loc.Get("combat.bar_hp") + ":");
            downRow.TrimEnd('\r').Length.Should().BeLessThanOrEqualTo(79);
            ("║ " + new string('W', 20) + " " + marker).Length.Should().BeLessThanOrEqualTo(79, "a long name still fits");
            rows.Single(l => l.Contains("Kade")).Should().Be(standingBefore, "a standing ally's row is unchanged");
            rows.Single(l => l.Contains("Kade")).Should().NotContain(marker);
        }
        finally { GameConfig.Language = before; }
    }

    // ---------- 21. the summary prints the downs line only when Downs > 0 ----------

    [Fact]
    public async Task TheSummary_PrintsTheDownsLine_OnlyForAnAllyThatWentDown()
    {
        var a = Ally("Mira"); var b = Ally("Kade");
        var r = NewRig(team: new[] { a, b });
        await Down(r, a);
        r.Engine.ResolveDownedAtFightEnd(r.Result);
        r.Engine.PrintPartyFightSummary(r.Result);
        r.Text.Should().Contain(Loc.Get("combat.party_summary_downs", 1, 0));
        System.Text.RegularExpressions.Regex.Matches(r.Text, System.Text.RegularExpressions.Regex.Escape(Loc.Get("combat.party_summary_downs", 1, 0)))
            .Count.Should().Be(1, "Kade never went down");
        r.Text.Should().NotContain(Loc.Get("combat.party_summary_downs", 0, 0), "no downs line for an ally with no downs");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void TheNewRows_FitSeventyNineColumns(string lang)
    {
        string name = new string('W', 20);
        var rows = new[]
        {
            "  " + Loc.GetIn(lang, "combat.ally_downed", name),
            "  " + Loc.GetIn(lang, "combat.ally_downed_group", name),
            "  " + Loc.GetIn(lang, "combat.finish_announce", "Ancient Shadow Wyrm", name),
            "  " + Loc.GetIn(lang, "combat.finish_lands", "Ancient Shadow Wyrm", name),
            "  " + Loc.GetIn(lang, "combat.finish_lost_taunt", "Ancient Shadow Wyrm", name),
            "  " + Loc.GetIn(lang, "combat.finish_lost_held", "Ancient Shadow Wyrm", name),
            "  " + Loc.GetIn(lang, "combat.rescue_done", name, 99999),
            "  " + Loc.GetIn(lang, "combat.rescue_no_potion"),
            "  " + Loc.GetIn(lang, "combat.ally_recovers", name, 99999, 99999),
            "    " + Loc.GetIn(lang, "combat.party_summary_downs_fell", 99, 99),
            Loc.GetIn(lang, "combat.rescue_menu_sr"),
            Loc.GetIn(lang, "combat.rescue_which"),
        };
        // every row is written through UIHelper.WriteRow (MessageRows wraps at 79)
        foreach (var row in rows)
            foreach (var shown in UsurperRemake.UI.UIHelper.MessageRows(row))
                shown.Length.Should().BeLessThanOrEqualTo(79, shown);
        ("    " + Loc.GetIn(lang, "combat.party_summary_downs_fell", 99, 99)).Length.Should().BeLessThanOrEqualTo(79, "the summary row is written as is");
        Loc.GetIn(lang, "combat.rescue_menu").Length.Should().BeLessThanOrEqualTo(34, "the standard menu pads the label to 34");
    }
}
