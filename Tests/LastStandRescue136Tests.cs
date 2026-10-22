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
/// 1.3.6 rule 1: Rescue [U] lifts each Downed ally once per fight. A second down of the same ally in the
/// same fight cannot be rescued: RescueOffered and the [U] key count only allies not yet rescued, [U] with
/// only such allies Downed prints one line and spends nothing, and a new fight clears the record.
/// </summary>
[Collection("SharedGameSingletons")]
public class RescueOnce_LastStandRescue136Tests
{
    private static readonly string[] Menus = { "ShowDungeonCombatMenuStandard", "ShowDungeonCombatMenuBBS", "ShowDungeonCombatMenuScreenReader" };

    private static string Menu(Rig r, string which)
    {
        int before = r.Output.ToArray().Length;
        var info = new List<(string key, string name, bool available)>();
        Call(r.Engine, which, r.Owner, false, false, info, false, GodDomain.None);
        return System.Text.Encoding.UTF8.GetString(r.Output.ToArray()[before..]);
    }

    private static bool Shows(string text, string which) => which == "ShowDungeonCombatMenuScreenReader"
        ? text.Contains(Loc.Get("combat.rescue_menu_sr"))
        : text.Contains("[U]");

    private static Task<CombatAction?> PressU(Rig r) => (Task<CombatAction?>)Call(r.Engine, "ChooseRescue", r.Owner)!;

    [Fact]
    public async Task AnAllyRescuedThenDownedAgain_CannotBeRescued_AndNoPotionIsSpent()
    {
        var ally = Ally("Mira", 100, 200);
        var r = NewRig(owner: Hero(potions: 3), team: new[] { ally });
        await Down(r, ally);
        r.Engine.RescueOffered(r.Owner).Should().BeTrue("the first down can be rescued");
        await CallAsync(r.Engine, "ExecuteRescue", r.Owner, ally, r.Result);
        r.Owner.Healing.Should().Be(2);
        ally.IsDowned.Should().BeFalse();

        await Down(r, ally);
        ally.IsDowned.Should().BeTrue("a second down is still a down");
        r.Engine.RescuedThisFight(ally).Should().BeTrue();
        r.Engine.RescueOffered(r.Owner).Should().BeFalse("this ally was already rescued in this fight");
        foreach (var m in Menus) Shows(Menu(r, m), m).Should().BeFalse("{0}: no ally left to lift", m);

        int before = r.Output.ToArray().Length;
        var action = await PressU(r);
        action.Should().BeNull();
        string shown = System.Text.Encoding.UTF8.GetString(r.Output.ToArray()[before..]);
        shown.Should().Contain(Loc.Get("combat.rescue_already_used", ally.DisplayName));
        shown.Should().NotContain(Loc.Get("combat.rescue_which"));
        r.Owner.Healing.Should().Be(2, "pressing U spends no potion");

        // a Rescue action that reaches the turn anyway (a queued choice) lifts nobody and spends nothing
        await CallAsync(r.Engine, "ExecuteRescue", r.Owner, ally, r.Result);
        r.Owner.Healing.Should().Be(2);
        ally.IsDowned.Should().BeTrue();
        r.Engine.StatsFor(ally).Rescues.Should().Be(1);
    }

    [Fact]
    public async Task ASecondDownedAlly_NotYetRescued_CanStillBeLifted()
    {
        var a = Ally("Mira"); var b = Ally("Kade");
        var r = NewRig(owner: Hero(potions: 3), team: new[] { a, b });
        await Down(r, a);
        await CallAsync(r.Engine, "ExecuteRescue", r.Owner, a, r.Result);
        await Down(r, a); await Down(r, b);
        r.Engine.DownedAllies().Should().HaveCount(2);
        r.Engine.RescuableAllies().Should().ContainSingle().Which.Should().BeSameAs(b);
        r.Engine.RescueOffered(r.Owner).Should().BeTrue("Kade was not rescued yet");
        foreach (var m in Menus) Shows(Menu(r, m), m).Should().BeTrue("{0}: Kade can be lifted", m);

        int before = r.Output.ToArray().Length;
        var action = await PressU(r);
        string shown = System.Text.Encoding.UTF8.GetString(r.Output.ToArray()[before..]);
        action!.Type.Should().Be(CombatActionType.Rescue);
        action.RescueTarget.Should().BeSameAs(b);
        shown.Should().NotContain(Loc.Get("combat.rescue_which"), "only one ally can be lifted, so there is no prompt");
        await CallAsync(r.Engine, "ExecuteRescue", r.Owner, b, r.Result);
        b.IsDowned.Should().BeFalse();
        a.IsDowned.Should().BeTrue();
        r.Owner.Healing.Should().Be(1);
    }

    [Fact]
    public async Task ThePrompt_ListsOnlyTheAlliesNotYetRescued()
    {
        var a = Ally("Mira"); var b = Ally("Kade"); var c = Ally("Pip");
        var r = NewRig(owner: Hero(potions: 3), team: new[] { a, b, c }, script: "1\n");
        await Down(r, a);
        await CallAsync(r.Engine, "ExecuteRescue", r.Owner, a, r.Result);
        await Down(r, a); await Down(r, b); await Down(r, c);
        int before = r.Output.ToArray().Length;
        var action = await PressU(r);
        string shown = System.Text.Encoding.UTF8.GetString(r.Output.ToArray()[before..]);
        shown.Should().Contain(Loc.Get("combat.rescue_which"));
        shown.Should().Contain("Kade").And.Contain("Pip").And.NotContain("Mira");
        action!.RescueTarget.Should().BeSameAs(b, "row 1 is the first ally not yet rescued");
    }

    [Fact]
    public async Task ANewFight_ClearsTheRecord()
    {
        var ally = Ally("Mira", 100, 200);
        string attacks = string.Concat(Enumerable.Repeat("A\n\n", 20));
        var r = NewRig(owner: Hero(potions: 3), team: new[] { ally }, script: attacks);
        await Down(r, ally);
        await CallAsync(r.Engine, "ExecuteRescue", r.Owner, ally, r.Result);
        r.Engine.RescuedThisFight(ally).Should().BeTrue();

        // a real fight on the same engine starts with the per fight state cleared
        var task = r.Engine.PlayerVsMonsters(r.Owner, new List<Monster> { Mob("Rat", "Goblinoid", hp: 1, str: 1) },
            new List<Character> { ally }, offerMonkEncounter: false);
        (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(60)))).Should().BeSameAs(task, "the fight ends: {0}", r.Text);
        (await task).Outcome.Should().Be(CombatOutcome.Victory, "{0}", r.Text);
        r.Engine.RescuedThisFight(ally).Should().BeFalse("a new fight resets the record");

        // and the record of the new fight starts empty: a down can be rescued again
        typeof(CombatEngine).GetField("currentTeammates", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(r.Engine, r.Team);
        await Down(r, ally);
        r.Engine.RescueOffered(r.Owner).Should().BeTrue();
    }

    [Fact]
    public async Task AFloorBoss_HasNoPotionCooldown_ForAFreshAlly()
    {
        // moved here from Rescue_PartySurvival133Tests, whose same ally is now already rescued
        var first = Ally("Mira"); var fresh = Ally("Kade");
        var r = NewRig(owner: Hero(potions: 3), team: new[] { first, fresh });
        await Down(r, first);
        r.Engine.BossContext = new BossCombatContext();
        await CallAsync(r.Engine, "ExecuteRescue", r.Owner, first, r.Result);
        r.Engine.BossContext = null;
        r.Owner.PotionCooldownRounds = 2;
        await Down(r, fresh);
        r.Engine.RescueOffered(r.Owner).Should().BeTrue("a floor boss has no potion cooldown");
    }

    [Fact]
    public async Task AnAllyAlreadyRescued_IsNotLeftToDie_WhenItCannotBeLiftedAgain()
    {
        var a = Ally("Mira"); var b = Ally("Kade");
        var r = NewRig(owner: Hero(potions: 3), team: new[] { a, b });
        var attack = new CombatAction { Type = CombatActionType.Attack };
        await Down(r, a);
        await CallAsync(r.Engine, "ExecuteRescue", r.Owner, a, r.Result);
        await Down(r, a); await Down(r, b);
        r.Engine.NoteOwnerTurn(r.Owner, r.Team, attack);
        r.Engine.CouldHaveHelped(a, r.Owner).Should().BeFalse("Mira was already rescued in this fight, so U could not lift her");
        r.Engine.CouldHaveHelped(b, r.Owner).Should().BeTrue("Kade could still be lifted");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("it")]
    public void TheAlreadyRescuedLine_IsInEveryLanguage_AndFits79(string lang)
    {
        Loc.HasIn(lang, "combat.rescue_already_used").Should().BeTrue();
        string name = new string('W', 20);
        string row = "  " + Loc.GetIn(lang, "combat.rescue_already_used", name);
        row.Should().Contain(name);
        if (lang == "en" || lang == "hu")
            row.Length.Should().BeLessThanOrEqualTo(79, row);
        foreach (var shown in UsurperRemake.UI.UIHelper.MessageRows(row))
            shown.Length.Should().BeLessThanOrEqualTo(79, shown);
    }
}

/// <summary>
/// 1.3.6 rule 2: Last Stand keeps the fight going. When the fight's owner would die in the round loop and
/// Last Stand qualifies, the player is left at 1 HP, the line prints and the fight goes on; HandlePlayerDeath
/// does not run for that blow. Last Stand fires once per fight; a later killing blow goes to Death's Door if
/// it qualifies, otherwise to death. Death's Door keeps the fight going too (ruling 1). Fights are real
/// PlayerVsMonsters fights on a scripted terminal.
/// </summary>
[Collection("SharedGameSingletons")]
public class LastStandLoop_LastStandRescue136Tests
{
    private static string Attacks(int n = 30) => string.Concat(Enumerable.Repeat("A\n\n", n)) + string.Concat(Enumerable.Repeat("P\n", 10));

    private static Character Hero(long hp, bool exhibition = false) => new Character
    {
        Name1 = "Tester", Name2 = "Tester", AI = CharacterAI.Human,
        Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 10,
        HP = hp, MaxHP = 600, BaseMaxHP = 600,
        Strength = 60, BaseStrength = 60, Defence = 20, BaseDefence = 20,
        Dexterity = 30, BaseDexterity = 30, Agility = 20, BaseAgility = 20,
        Constitution = 30, BaseConstitution = 30, Intelligence = 20, BaseIntelligence = 20,
        Wisdom = 20, BaseWisdom = 20, Stamina = 100, BaseStamina = 100,
        Mental = 80, Gold = 0, Healing = 0, AutoHeal = false,
        CombatSpeed = CombatSpeed.Instant, Resurrections = 3, IsExhibitionCombat = exhibition,
    };

    /// <summary>One blow is held at the per hit cap (510 against this hero, whose MaxHP the fight start makes
    /// 680): it kills a hero at 400 HP (above half: Last Stand) or at 200 (above a quarter: Death's Door).</summary>
    private static Monster Crusher(long hp = 5000, int level = 8) => new Monster
    {
        Name = "Crusher", Level = level, HP = hp, MaxHP = hp, Strength = 60000, Defence = 5, Experience = 1, Gold = 0,
        IsActive = true, FamilyName = "Goblinoid",
    };

    private sealed record Fight(CombatResult? Result, Exception? Error, string Text);

    private static async Task<Fight> Run(Character hero, Monster monster, bool ambush = false, int seed = 136)
    {
        var output = new System.IO.MemoryStream();
        var term = new TerminalEmulator(new ScriptedStream(Attacks()), output);
        var e = new CombatEngine(term) { DivineFavorChanceOverride = () => 0f };
        e.SeedRandomForTests(seed);
        try
        {
            var task = e.PlayerVsMonsters(hero, new List<Monster> { monster }, offerMonkEncounter: false, isAmbush: ambush);
            var done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(60)));
            if (done != task) throw new TimeoutException("the fight did not end");
            return new Fight(await task, null, System.Text.Encoding.UTF8.GetString(output.ToArray()));
        }
        catch (Exception ex) { return new Fight(null, ex, System.Text.Encoding.UTF8.GetString(output.ToArray())); }
    }

    private static int Count(string text, string line)
    {
        int n = 0, i = 0;
        while ((i = text.IndexOf(line, i, StringComparison.Ordinal)) >= 0) { n++; i += line.Length; }
        return n;
    }

    [Fact]
    public async Task LastStand_KeepsTheFightGoing_AndASecondKillingBlowIsADeath()
    {
        // exhibition: the loss ends without the death screens, and Death's Door is off, so the second blow kills
        var hero = Hero(400, exhibition: true);
        var f = await Run(hero, Crusher());
        f.Error.Should().BeNull("{0}", f.Text);
        Count(f.Text, Loc.Get("combat.last_stand")).Should().Be(1, "Last Stand fires once: {0}", f.Text);
        hero.LastStandUsedThisCombat.Should().BeTrue();
        f.Result!.CurrentRound.Should().BeGreaterThanOrEqualTo(2, "the fight went on to a later round: {0}", f.Text);
        f.Text.Should().Contain(Loc.Get("combat.rescue_last_stand_spent"), "the status line of the later round says it is spent");
        f.Result.Outcome.Should().Be(CombatOutcome.PlayerDied, "the second killing blow is a death, not an escape: {0}", f.Text);
        f.Text.Should().NotContain(Loc.Get("combat.deaths_door"));
    }

    [Fact]
    public async Task LastStand_InTheAmbush_ThenThePlayerActsAndWins()
    {
        // the level 200 monster wins the ambush and its blow kills the hero from 400 HP; the hero then acts
        // in round 1 and kills it. A flag left from an earlier fight is cleared at the fight's start.
        var hero = Hero(400);
        hero.LastStandUsedThisCombat = true;
        var crusher = Crusher(hp: 1, level: 200);
        var f = await Run(hero, crusher, ambush: true);
        f.Error.Should().BeNull("{0}", f.Text);
        f.Text.Should().Contain(Loc.Get("combat.last_stand"), "{0}", f.Text);
        crusher.IsAlive.Should().BeFalse("the player acted after Last Stand: {0}", f.Text);
        f.Result!.Outcome.Should().Be(CombatOutcome.Victory, "the fight went on, so it is won, not an escape: {0}", f.Text);
        f.Result.CurrentRound.Should().BeGreaterThanOrEqualTo(1);
        f.Result.PlayerActuallyDied.Should().BeFalse();
        hero.HP.Should().BeGreaterThan(0);
        hero.Resurrections.Should().Be(3);
    }

    [Fact]
    public async Task DeathsDoor_InTheAmbush_AlsoKeepsTheFightGoing()
    {
        var hero = Hero(200);
        var crusher = Crusher(hp: 1, level: 200);
        var f = await Run(hero, crusher, ambush: true);
        f.Error.Should().BeNull("{0}", f.Text);
        f.Text.Should().Contain(Loc.Get("combat.deaths_door"), "{0}", f.Text);
        f.Text.Should().NotContain(Loc.Get("combat.last_stand"));
        crusher.IsAlive.Should().BeFalse("the player acted after Death's Door: {0}", f.Text);
        f.Result!.Outcome.Should().Be(CombatOutcome.Victory, "{0}", f.Text);
        hero.DeathsDoorUsedThisCombat.Should().BeTrue();
        f.Text.Should().Contain(Loc.Get("combat.rescue_deaths_door_spent"), "the round after it shows it spent");
    }

    [Fact]
    public void LastStand_FiresOncePerFight_ThenDeathsDoor_ThenDeath()
    {
        var p = Hero(600);
        p.CaptureRoundStartHP();
        p.HP = 0;
        p.LastStandCheckAndApply().Should().BeTrue("the first killing blow from above half");
        p.LastStandFiredThisRound.Should().BeTrue();
        p.LastStandUsedThisCombat.Should().BeTrue();
        p.HP.Should().Be(1);

        p.HP = 600; p.CaptureRoundStartHP(); p.HP = 0;
        p.LastStandCheckAndApply().Should().BeTrue("the second killing blow from above half goes to Death's Door");
        p.LastStandFiredThisRound.Should().BeFalse("Last Stand did not fire twice");
        p.DeathsDoorFiredThisRound.Should().BeTrue();
        p.HP.Should().Be(1);

        p.HP = 600; p.CaptureRoundStartHP(); p.HP = 0;
        p.LastStandCheckAndApply().Should().BeFalse("both are spent: a death");
        p.HP.Should().Be(0);
    }

    [Fact]
    public void TheStatusLine_SaysLastStandIsSpent()
    {
        var p = Hero(600);
        p.CaptureRoundStartHP();
        CombatEngine.BuildPlayerStatusEntries(p).Select(e => e.text).Should().Contain(Loc.Get("combat.rescue_last_stand_ready"));
        p.LastStandUsedThisCombat = true;
        var shown = CombatEngine.BuildPlayerStatusEntries(p).Select(e => e.text).ToList();
        shown.Should().Contain(Loc.Get("combat.rescue_last_stand_spent"));
        shown.Should().NotContain(Loc.Get("combat.rescue_last_stand_ready"));
        shown.Should().NotContain(Loc.Get("combat.rescue_last_stand_low"));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("it")]
    public void TheSpentLine_IsInEveryLanguage_AndNoWiderThanTheLowLine(string lang)
    {
        Loc.HasIn(lang, "combat.rescue_last_stand_spent").Should().BeTrue();
        string spent = Loc.GetIn(lang, "combat.rescue_last_stand_spent");
        spent.Should().NotBe("combat.rescue_last_stand_spent", "a real line, not the key");
        if (lang != "en") spent.Should().NotBe(Loc.GetIn("en", "combat.rescue_last_stand_spent"), "translated");
        spent.Length.Should().BeLessThanOrEqualTo(Loc.GetIn(lang, "combat.rescue_last_stand_low").Length,
            "the status line takes it where the low line stood, so it fits wherever that fit");
        ("  " + spent).Length.Should().BeLessThanOrEqualTo(79);
    }
}
