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
