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
/// 1.3.3 party survival (PS-2 1.5): Rescue [U]. The combat owner's turn and one healing potion lift a Downed
/// ally, healed as Heal Ally heals. Offered only while an ally is Downed, a potion is held and no Old God
/// potion cooldown runs; never to a group follower.
/// </summary>
[Collection("SharedGameSingletons")]
public class Rescue_PartySurvival133Tests
{
    private static string Menu(Rig r, string which, bool follower = false)
    {
        int before = r.Output.ToArray().Length;
        var info = new List<(string key, string name, bool available)>();
        Call(r.Engine, which, r.Owner, false, false, info, follower, GodDomain.None);
        return System.Text.Encoding.UTF8.GetString(r.Output.ToArray()[before..]);
    }

    private static readonly string[] Menus = { "ShowDungeonCombatMenuStandard", "ShowDungeonCombatMenuBBS", "ShowDungeonCombatMenuScreenReader" };

    private static bool Shows(string text, string which) => which == "ShowDungeonCombatMenuScreenReader"
        ? text.Contains(Loc.Get("combat.rescue_menu_sr"))
        : text.Contains("[U]");

    // ---------- 9. Rescue needs a potion ----------

    [Fact]
    public async Task WithNoPotion_TheUEntryIsAbsent_WithOneItIsShown()
    {
        var ally = Ally();
        var r = NewRig(owner: Hero(potions: 0), team: new[] { ally });
        foreach (var m in Menus) Shows(Menu(r, m), m).Should().BeFalse("{0}: nobody is Downed", m);
        await Down(r, ally);
        r.Engine.RescueOffered(r.Owner).Should().BeFalse();
        foreach (var m in Menus) Shows(Menu(r, m), m).Should().BeFalse("{0}: no potion", m);
        r.Owner.Healing = 1;
        r.Engine.RescueOffered(r.Owner).Should().BeTrue();
        foreach (var m in Menus) Shows(Menu(r, m), m).Should().BeTrue("{0}: a Downed ally and a potion", m);
        foreach (var m in Menus) Shows(Menu(r, m, follower: true), m).Should().BeFalse("{0}: a follower never sees it", m);
    }

    [Fact]
    public async Task ARescue_SpendsExactlyOnePotion()
    {
        var ally = Ally("Mira", 100, 200);
        var r = NewRig(owner: Hero(potions: 3), team: new[] { ally });
        await Down(r, ally);
        await CallAsync(r.Engine, "ExecuteRescue", r.Owner, ally, r.Result);
        r.Owner.Healing.Should().Be(2);
        ally.IsDowned.Should().BeFalse();
        r.Engine.StatsFor(ally).Rescues.Should().Be(1);
        await CallAsync(r.Engine, "ExecuteRescue", r.Owner, ally, r.Result);
        r.Owner.Healing.Should().Be(2, "a standing ally is not rescued again");
    }

    [Fact]
    public async Task TheUKey_WithTwoDowned_AsksWhich()
    {
        var a = Ally("Mira"); var b = Ally("Kade");
        var r = NewRig(owner: Hero(potions: 3), team: new[] { a, b }, script: "2\n");
        await Down(r, a); await Down(r, b);
        var t = (Task<CombatAction?>)Call(r.Engine, "ChooseRescue", r.Owner)!;
        var action = await t;
        r.Text.Should().Contain(Loc.Get("combat.rescue_which"));
        action!.Type.Should().Be(CombatActionType.Rescue);
        action.RescueTarget.Should().BeSameAs(b);

        var one = Ally("Pip");
        var r1 = NewRig(owner: Hero(potions: 3), team: new[] { one });
        await Down(r1, one);
        var single = await (Task<CombatAction?>)Call(r1.Engine, "ChooseRescue", r1.Owner)!;
        single!.RescueTarget.Should().BeSameAs(one);
        r1.Text.Should().NotContain(Loc.Get("combat.rescue_which"), "one Downed ally needs no prompt");
    }

    [Fact]
    public async Task ARescue_ClearsTheFinish()
    {
        var ally = Ally();
        var wolf = Mob();
        var r = NewRig(owner: Hero(potions: 3), team: new[] { ally }, monsters: new[] { wolf });
        await Down(r, ally);
        await r.Engine.TryFinishOrAnnounce(wolf, r.Result);
        r.Engine.HasPendingFinish(wolf).Should().BeTrue();
        await CallAsync(r.Engine, "ExecuteRescue", r.Owner, ally, r.Result);
        r.Engine.HasPendingFinish(wolf).Should().BeFalse();
        r.Result.CurrentRound = 2;
        (await r.Engine.TryFinishOrAnnounce(wolf, r.Result)).Should().BeFalse("nobody is Downed");
        r.Result.Teammates.Should().Contain(ally);
    }

    // ---------- 10. the Old God potion cooldown ----------

    [Fact]
    public async Task Rescue_IsAbsentWhileTheOldGodPotionCooldownRuns()
    {
        var ally = Ally();
        var r = NewRig(owner: Hero(potions: 3), team: new[] { ally });
        await Down(r, ally);
        r.Engine.BossContext = new BossCombatContext();
        r.Owner.PotionCooldownRounds = 2;
        r.Engine.RescueOffered(r.Owner).Should().BeFalse();
        foreach (var m in Menus) Shows(Menu(r, m), m).Should().BeFalse("{0}: cooldown", m);
        r.Owner.PotionCooldownRounds = 0;
        r.Engine.RescueOffered(r.Owner).Should().BeTrue();
        foreach (var m in Menus) Shows(Menu(r, m), m).Should().BeTrue("{0}: the cooldown ended", m);
        await CallAsync(r.Engine, "ExecuteRescue", r.Owner, ally, r.Result);
        r.Owner.PotionCooldownRounds.Should().Be(GameConfig.ModBossPotionCooldownRounds + 1, "the rescue potion starts the cooldown, as a drink does");
        r.Engine.BossContext = null;
        r.Owner.PotionCooldownRounds = 2;
        await Down(r, ally);
        r.Engine.RescueOffered(r.Owner).Should().BeFalse("1.3.6: this ally was already rescued in this fight (the floor boss case is in RescueOnce_LastStandRescue136Tests)");
    }

    // ---------- 11. a rescued ally stands with the potion heal and can be downed again ----------

    [Fact]
    public async Task ARescuedAlly_StandsWithThePotionHeal_AndASecondDownIsADown()
    {
        var ally = Ally("Mira", 100, 500);
        var r = NewRig(owner: Hero(potions: 3), team: new[] { ally });
        await Down(r, ally);
        await CallAsync(r.Engine, "ExecuteRescue", r.Owner, ally, r.Result);
        ally.HP.Should().BeGreaterThanOrEqualTo(100, "a potion gives at least 20 percent of the ally's max HP");
        ally.HP.Should().BeLessThanOrEqualTo(500);
        r.Text.Should().Contain(Loc.Get("combat.rescue_done", ally.DisplayName, ally.HP));
        await Down(r, ally);
        ally.IsDowned.Should().BeTrue("a second down is a down, not a death");
        r.Result.Teammates.Should().Contain(ally);
        r.Engine.StatsFor(ally).Downs.Should().Be(2);
    }
}
