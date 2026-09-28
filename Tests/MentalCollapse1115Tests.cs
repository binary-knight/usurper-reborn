using System;
using System.IO;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;
using static UsurperReborn.Tests.MentalRecovery1115Tests;
using static UsurperReborn.Tests.MentalRecoveryB1115Tests;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.15 Mental piece 7, commit 2: collapse at Mental 0 (user decisions 2026-09-27). Shallow
/// floors and town carry the player to the Healer (Broken, Mental 20, 5% of gold on hand); floor 26
/// and deeper is a real death through the existing death path. The Broken affliction costs 25% of
/// damage, defence and XP gained (replacing the band penalty, exempt from the Fatigue cap) and
/// refuses the stairs. Grouped followers get the same from their own Mental.
/// </summary>
[Collection("SharedGameSingletons")]
public class MentalCollapse1115Tests
{
    private static Character Broken(int mental = 20)
    {
        var c = Hero("Broken", mental);
        c.MentalBroken = true;
        return c;
    }

    // The Broken affliction

    [Fact]
    public void Broken_replaces_the_band_penalty()
    {
        GameConfig.MentalBrokenPenaltyPct.Should().Be(25);
        MentalSystem.GetMentalPenalty(Broken(10)).Should().BeApproximately(0.25f, 1e-6f, "replaces Breaking, not added to it");
        MentalSystem.GetMentalPenalty(Broken(90)).Should().BeApproximately(0.25f, 1e-6f);
        MentalSystem.GetMentalPenalty(Hero("Breaking", 10)).Should().BeApproximately(0.10f, 1e-6f);
        var npc = Broken(10); npc.AI = CharacterAI.Computer;
        MentalSystem.GetMentalPenalty(npc).Should().Be(0f);
    }

    [Fact]
    public void Broken_is_exempt_from_the_Fatigue_cap()
    {
        var b = Broken(20);
        b.Fatigue = GameConfig.FatigueExhaustedThreshold;
        MentalSystem.GetCombatMultiplier(b, 0f, online: false, defence: false).Should().BeApproximately(0.65f, 1e-6f);
        var breaking = Hero("Breaking", 10);
        breaking.Fatigue = GameConfig.FatigueExhaustedThreshold;
        MentalSystem.GetCombatMultiplier(breaking, 0f, online: false, defence: true).Should().BeApproximately(0.85f, 1e-6f, "the band is still capped");
    }

    [Fact]
    public void Broken_costs_a_quarter_of_xp_gained()
    {
        MentalSystem.ApplyBrokenXp(Broken(), 1000).Should().Be(750);
        MentalSystem.ApplyBrokenXp(Broken(), 3).Should().Be(3, "25% of 3 rounds down to 0");
        MentalSystem.ApplyBrokenXp(Hero("Well", 20), 1000).Should().Be(1000);
        MentalSystem.ApplyBrokenXp(Broken(), long.MaxValue).Should().Be(6917529027641081856L, "exact, no overflow (MaxValue less 25% rounded down)");
    }

    [Fact]
    public void Broken_refuses_the_stairs()
    {
        MentalSystem.GetDescentRule(Broken(90)).Should().Be(MentalDescent.Refused);
        MentalSystem.GetDescentRule(Broken(10)).Should().Be(MentalDescent.Refused);
        MentalSystem.GetDescentRule(Hero("Breaking", 10)).Should().Be(MentalDescent.AskTwice);
    }

    // Collapse

    [Fact]
    public void Collapse_is_due_at_zero_for_a_living_human()
    {
        MentalSystem.NeedsCollapse(Hero("Zero", 0)).Should().BeTrue();
        MentalSystem.NeedsCollapse(Hero("One", 1)).Should().BeFalse();
        var npc = Hero("Npc", 0); npc.AI = CharacterAI.Computer;
        MentalSystem.NeedsCollapse(npc).Should().BeFalse();
        var dead = Hero("Dead", 0); dead.HP = 0;
        MentalSystem.NeedsCollapse(dead).Should().BeFalse();
    }

    [Fact]
    public void Floor_twenty_six_and_deeper_is_a_death()
    {
        GameConfig.MentalCollapseDeathFloor.Should().Be(26);
        MentalSystem.IsCollapseDeath(0).Should().BeFalse("town");
        MentalSystem.IsCollapseDeath(25).Should().BeFalse();
        MentalSystem.IsCollapseDeath(26).Should().BeTrue();
        MentalSystem.IsCollapseDeath(90).Should().BeTrue();
    }

    [Fact]
    public void The_rescue_sets_twenty_Broken_and_takes_five_percent()
    {
        GameConfig.MentalCollapseRescueMental.Should().Be(20);
        GameConfig.MentalCollapseGoldFeePct.Should().Be(5);
        var c = Hero("Rescued", 0);
        c.Gold = 1019;
        c.Experience = 5000;
        MentalSystem.ApplyCollapseRescue(c).Should().Be(50);
        c.Gold.Should().Be(969);
        c.Mental.Should().Be(20);
        c.MentalBroken.Should().BeTrue();
        c.Experience.Should().Be(5000, "no XP loss");
        c.HP.Should().Be(100, "no death");
        var rich = Hero("Rich", 0);
        rich.Gold = long.MaxValue;
        MentalSystem.ApplyCollapseRescue(rich).Should().Be(461168601842738790L, "exact 5%, no overflow");
    }

    [Fact]
    public void A_collapse_death_leaves_the_player_Broken_at_twenty()
    {
        var c = Hero("Fallen", 0);
        MentalSystem.ApplyCollapseDeathAftermath(c);
        c.MentalBroken.Should().BeTrue();
        c.Mental.Should().Be(20);
    }

    // Followers

    [Fact]
    public void A_follower_on_a_shallow_floor_is_rescued_and_leaves_for_the_Healer()
    {
        using var leaderOut = new MemoryStream();
        using var followerOut = new MemoryStream();
        var f = Hero("Follower", 0);
        f.Gold = 200;
        f.RemoteTerminal = Term(followerOut);
        CombatEngine.ApplyFollowerCollapse(f, 25, Term(leaderOut));
        f.Mental.Should().Be(20);
        f.MentalBroken.Should().BeTrue();
        f.Gold.Should().Be(190);
        f.PendingMentalRescue.Should().BeTrue();
        f.PendingGroupDeath.Should().BeNull();
        Text(followerOut).Should().Contain(Loc.Get("mental.collapse_rescue"));
        Text(leaderOut).Should().Contain(Loc.Get("mental.collapse_other", f.DisplayName));
    }

    [Fact]
    public void A_follower_deep_down_dies_by_the_follower_death_path()
    {
        var f = Hero("Follower", 0);
        f.RemoteTerminal = Term(new MemoryStream());
        CombatEngine.ApplyFollowerCollapse(f, 26, null);
        f.PendingGroupDeath.Should().NotBeNull();
        f.HP.Should().Be(0);
        f.MentalBroken.Should().BeTrue();
        f.PendingMentalRescue.Should().BeFalse();
    }

    [Fact]
    public void A_follower_above_zero_does_not_collapse()
    {
        var f = Hero("Follower", 1);
        f.RemoteTerminal = Term(new MemoryStream());
        CombatEngine.ApplyFollowerCollapse(f, 40, null);
        f.PendingGroupDeath.Should().BeNull();
        f.PendingMentalRescue.Should().BeFalse();
        f.MentalBroken.Should().BeFalse();
    }

    // Wiring

    [Fact]
    public void The_location_loop_checks_for_a_collapse_between_actions()
    {
        var src = Src("Locations", "BaseLocation.cs");
        var loop = MentalBands1115Tests.Method(src, "LocationLoop");
        At(loop, "if (MentalSystem.CollapseDue(currentPlayer))").Should().BeGreaterThan(At(loop, "await NavigateToLocation(GameLocation.Prison);"));
        At(loop, "await HandleMentalCollapse();").Should().BeGreaterThan(At(loop, "if (MentalSystem.CollapseDue(currentPlayer))"));
        var collapse = MentalBands1115Tests.Method(src, "HandleMentalCollapse");
        int deathGate = At(collapse, "if (MentalSystem.IsCollapseDeath(floor))");
        At(collapse, "await new CombatEngine(terminal).HandleMentalCollapseDeath(player)").Should().BeGreaterThan(deathGate);
        int rescue = At(collapse, "long fee = MentalSystem.ApplyCollapseRescue(player);");
        At(collapse, "await NavigateToLocation(GameLocation.Healer);").Should().BeGreaterThan(rescue);
    }

    [Fact]
    public void A_collapse_death_uses_the_normal_death_path_without_Last_Stand()
    {
        var src = Src("Systems", "CombatEngine.cs");
        MentalBands1115Tests.Method(src, "HandlePlayerDeath").Should().Contain("if (!result.MentalCollapseDeath && result.Player.LastStandCheckAndApply(isPvP: false))");
        var death = MentalBands1115Tests.Method(src, "HandleMentalCollapseDeath");
        At(death, "await HandlePlayerDeath(result);").Should().BeGreaterThan(At(death, "MentalCollapseDeath = true,"));
    }

    [Fact]
    public void Followers_collapse_after_the_fight_end_loss()
    {
        var body = MentalBands1115Tests.Method(Src("Systems", "CombatEngine.cs"), "ApplyMentalFightEnd");
        At(body, "ApplyFollowerCollapse(mate, floor, terminal);")
            .Should().BeGreaterThan(At(body, "MentalSystem.ApplyFightEnd(mate, floor, companions, fled, MentalSystem.IsNearDeath(mate), boss, oldGod);"));
        var follow = Src("Locations", "DungeonLocation.cs");
        var join = MentalBands1115Tests.Method(follow, "EnterAsGroupFollower");
        At(join, "throw new LocationExitException(GameLocation.Healer);").Should().BeGreaterThan(At(join, "if (player.PendingMentalRescue)"));
        var loop = MentalBands1115Tests.Method(follow, "GroupFollowerLoop");
        System.Text.RegularExpressions.Regex.Matches(loop, @"if \(player\.PendingGroupDeath != null \|\| player\.PendingMentalRescue\) break;")
            .Count.Should().Be(2, "both reads leave the loop on a rescue, as on a death");
        loop.Should().NotContain("if (player.PendingGroupDeath != null) break;");
    }

    [Fact]
    public void Broken_xp_is_taken_on_every_reward_path()
    {
        var src = Src("Systems", "CombatEngine.cs");
        MentalBands1115Tests.Method(src, "HandleVictory").Should().Contain("expReward = MentalSystem.ApplyBrokenXp(result.Player, expReward);");
        MentalBands1115Tests.Method(src, "HandleVictoryMultiMonster").Should().Contain("MentalSystem.ApplyBrokenXp(result.Player, adjustedExp)");
        MentalBands1115Tests.Method(src, "DistributeGroupRewards").Should().Contain("playerExp = MentalSystem.ApplyBrokenXp(groupedPlayer, playerExp);");
    }

    [Fact]
    public void The_stairs_refuse_before_asking()
    {
        var confirm = MentalBands1115Tests.Method(Src("Locations", "DungeonLocation.cs"), "ConfirmMentalDescent");
        int refused = At(confirm, "if (rule == MentalDescent.Refused)");
        At(confirm, "Loc.Get(\"mental.descend_refused\")").Should().BeGreaterThan(refused);
        At(confirm, "await terminal.AskYesNoAsync(").Should().BeGreaterThan(refused);
    }

    [Fact]
    public void The_status_sheet_shows_Broken()
    {
        var status = MentalBands1115Tests.Method(Src("Locations", "BaseLocation.cs"), "ShowStatus");
        At(status, "Loc.Get(\"base.mental_afflictions_broken\", GameConfig.MentalBrokenPenaltyPct)")
            .Should().BeGreaterThan(At(status, "if (currentPlayer.MentalBroken)"));
    }

    [Fact]
    public void Collapse_lines_exist_in_all_five_languages()
    {
        var keys = new[]
        {
            "mental.descend_refused", "mental.collapse_rescue", "mental.collapse_fee", "mental.collapse_death",
            "mental.collapse_killer", "mental.collapse_other", "base.mental_afflictions_broken",
        };
        foreach (var lang in new[] { "en", "es", "fr", "hu", "it" })
        {
            var text = File.ReadAllText(Path.Combine(RepoRoot(), "Localization", lang + ".json"));
            foreach (var k in keys) text.Should().Contain($"\"{k}\":", $"{lang} needs {k}");
        }
    }
}
