using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;
using static UsurperReborn.Tests.MentalRecovery1115Tests;
using static UsurperReborn.Tests.MentalRecoveryB1115Tests;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.15 Mental piece 7, commit 1: band effects. The combat penalty combine rule (worse of Grief
/// and Mental, Mental plus Fatigue capped in single-player), fear at combat start, room lines
/// (Strained uneasy, Shaken and worse hallucinations) and the Breaking stairs that ask twice.
/// </summary>
[Collection("SharedGameSingletons")]
public class MentalBands1115Tests
{
    private const BindingFlags NF = BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>The brace-balanced body of the first method declared with this name, any return type.</summary>
    internal static string Method(string src, string name)
    {
        var m = Regex.Match(src, @"(?:private|public|internal|protected)[^\n;=(]*\s" + Regex.Escape(name) + @"\s*\(");
        m.Success.Should().BeTrue($"{name} must be defined");
        int brace = src.IndexOf('{', m.Index); int depth = 0;
        for (int i = brace; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0) return src.Substring(brace, i - brace + 1);
        }
        throw new InvalidOperationException("unbalanced braces");
    }

    /// <summary>A Random whose Next(max) always returns value (clamped below max).</summary>
    private sealed class FixedRandom : Random
    {
        private readonly int _value;
        public FixedRandom(int value) { _value = value; }
        public override int Next(int maxValue) => Math.Min(_value, maxValue - 1);
        public override int Next(int minValue, int maxValue) => Math.Clamp(_value, minValue, maxValue - 1);
        public override int Next() => _value;
    }

    // Combine rule

    [Fact]
    public void Grief_and_Mental_take_the_worse_never_the_sum()
    {
        MentalSystem.CombinePenalties(0.10f, 0.05f, 0f, true).Should().BeApproximately(0.10f, 1e-6f);
        MentalSystem.CombinePenalties(0.05f, 0.20f, 0f, true).Should().BeApproximately(0.20f, 1e-6f);
        MentalSystem.CombinePenalties(0.10f, 0.10f, 0f, false).Should().BeApproximately(0.10f, 1e-6f, "never the sum");
    }

    [Fact]
    public void Mental_plus_Fatigue_stops_at_fifteen_percent_in_single_player()
    {
        GameConfig.MentalFatigueCombatCap.Should().Be(0.15f);
        MentalSystem.CombinePenalties(0.10f, 0f, 0.10f, true).Should().BeApproximately(0.15f, 1e-6f);
        MentalSystem.CombinePenalties(0.05f, 0f, 0.05f, true).Should().BeApproximately(0.10f, 1e-6f, "under the cap");
        MentalSystem.CombinePenalties(0.10f, 0f, 0.10f, false).Should().BeApproximately(0.20f, 1e-6f, "no cap when not asked");
    }

    [Fact]
    public void Grief_plus_Fatigue_keeps_its_old_size()
    {
        MentalSystem.CombinePenalties(0.10f, 0.20f, 0.10f, true).Should().BeApproximately(0.30f, 1e-6f);
        MentalSystem.CombinePenalties(0f, 0f, 0.10f, true).Should().BeApproximately(0.10f, 1e-6f, "Fatigue alone is unchanged");
    }

    [Fact]
    public void Fatigue_penalty_is_single_player_only()
    {
        var c = Hero("Tired", 100);
        c.Fatigue = GameConfig.FatigueTiredThreshold;
        MentalSystem.GetFatiguePenalty(c, online: false, defence: false).Should().BeApproximately(0.05f, 1e-6f);
        c.Fatigue = GameConfig.FatigueExhaustedThreshold;
        MentalSystem.GetFatiguePenalty(c, online: false, defence: true).Should().BeApproximately(0.10f, 1e-6f);
        MentalSystem.GetFatiguePenalty(c, online: true, defence: false).Should().Be(0f);
        c.Fatigue = 0;
        MentalSystem.GetFatiguePenalty(c, online: false, defence: false).Should().Be(0f);
    }

    [Fact]
    public void Combat_multiplier_keeps_a_grief_bonus_and_reads_each_own_mental()
    {
        var shaken = Hero("Shaken", 40);
        MentalSystem.GetCombatMultiplier(shaken, 0f, online: true, defence: false).Should().BeApproximately(0.95f, 1e-6f);
        MentalSystem.GetCombatMultiplier(shaken, 0.20f, online: true, defence: false).Should().BeApproximately(1.20f * 0.95f, 1e-5f, "an Anger bonus stays");
        MentalSystem.GetCombatMultiplier(shaken, -0.15f, online: true, defence: false).Should().BeApproximately(0.85f, 1e-6f, "grief is worse");
        var breaking = Hero("Breaking", 10);
        breaking.Fatigue = GameConfig.FatigueExhaustedThreshold;
        MentalSystem.GetCombatMultiplier(breaking, 0f, online: false, defence: false).Should().BeApproximately(0.85f, 1e-6f, "capped");
        var npc = Hero("Npc", 10); npc.AI = CharacterAI.Computer;
        MentalSystem.GetMentalPenalty(npc).Should().Be(0f, "NPCs have no Mental penalty");
        MentalSystem.GetCombatMultiplier(Hero("Stable", 90), 0f, online: true, defence: true).Should().Be(1f);
    }

    // Chances

    [Fact]
    public void Band_chances_match_the_design()
    {
        MentalSystem.GetHallucinationChancePct(80).Should().Be(0);
        MentalSystem.GetHallucinationChancePct(60).Should().Be(0);
        MentalSystem.GetHallucinationChancePct(40).Should().Be(4);
        MentalSystem.GetHallucinationChancePct(10).Should().Be(8);
        MentalSystem.GetUneasyChancePct(60).Should().Be(3);
        MentalSystem.GetUneasyChancePct(40).Should().Be(0);
        MentalSystem.GetUneasyChancePct(80).Should().Be(0);
        MentalSystem.GetFearChancePct(60).Should().Be(0);
        MentalSystem.GetFearChancePct(40).Should().Be(10);
        MentalSystem.GetFearChancePct(10).Should().Be(20);
    }

    [Fact]
    public void Fear_rolls_under_the_band_chance()
    {
        MentalSystem.RollFear(Hero("Shaken", 40), new FixedRandom(9)).Should().BeTrue();
        MentalSystem.RollFear(Hero("Shaken", 40), new FixedRandom(10)).Should().BeFalse();
        MentalSystem.RollFear(Hero("Breaking", 10), new FixedRandom(19)).Should().BeTrue();
        MentalSystem.RollFear(Hero("Strained", 60), new FixedRandom(0)).Should().BeFalse();
        var npc = Hero("Npc", 10); npc.AI = CharacterAI.Computer;
        MentalSystem.RollFear(npc, new FixedRandom(0)).Should().BeFalse();
    }

    [Fact]
    public void Room_lines_come_from_the_band()
    {
        MentalSystem.PickRoomLine(Hero("Shaken", 40), new FixedRandom(3)).Should().StartWith("mental.hallucination_");
        MentalSystem.PickRoomLine(Hero("Shaken", 40), new FixedRandom(4)).Should().BeNull();
        MentalSystem.PickRoomLine(Hero("Breaking", 10), new FixedRandom(7)).Should().StartWith("mental.hallucination_");
        MentalSystem.PickRoomLine(Hero("Strained", 60), new FixedRandom(2)).Should().StartWith("mental.uneasy_");
        MentalSystem.PickRoomLine(Hero("Strained", 60), new FixedRandom(3)).Should().BeNull();
        MentalSystem.PickRoomLine(Hero("Stable", 90), new FixedRandom(0)).Should().BeNull();
    }

    [Fact]
    public void Breaking_asks_twice_at_the_stairs()
    {
        MentalSystem.GetDescentRule(Hero("Breaking", 10)).Should().Be(MentalDescent.AskTwice);
        MentalSystem.GetDescentRule(Hero("Shaken", 40)).Should().Be(MentalDescent.Allowed);
        MentalSystem.GetDescentRule(Hero("Stable", 90)).Should().Be(MentalDescent.Allowed);
    }

    // Fear in the engine

    [Fact]
    public void A_feared_player_loses_one_action_only()
    {
        using var output = new MemoryStream();
        var term = Term(output);
        var engine = new CombatEngine(term);
        typeof(CombatEngine).GetField("random", NF)!.SetValue(engine, new FixedRandom(0));
        var leader = Hero("Leader", 10);
        using var followerOut = new MemoryStream();
        var follower = Hero("Follower", 40);
        follower.RemoteTerminal = Term(followerOut);
        var calm = Hero("Calm", 90);
        calm.RemoteTerminal = Term(new MemoryStream());
        engine.RollMentalFear(leader, new List<Character> { follower, calm });
        engine.ConsumeMentalFear(leader, term).Should().BeTrue();
        engine.ConsumeMentalFear(leader, term).Should().BeFalse("only the first action");
        engine.ConsumeMentalFear(calm, calm.RemoteTerminal).Should().BeFalse("Stable never fears");
        engine.ConsumeMentalFear(follower, follower.RemoteTerminal).Should().BeTrue();
        Text(followerOut).Should().Contain(Loc.Get("mental.fear_1"));
        Text(output).Should().Contain(Loc.Get("mental.fear_other", follower.DisplayName));
    }

    // Wiring

    [Fact]
    public void The_damage_and_defence_sites_use_the_combine_rule()
    {
        var src = Src("Systems", "CombatEngine.cs");
        var single = Method(src, "ExecuteSingleAttack");
        single.Should().Contain("MentalSystem.GetCombatMultiplier(attacker,");
        single.Should().NotContain("FatigueTiredDamagePenalty", "Fatigue is inside the combine rule");
        var swing = Method(src, "ComputePlayerSwingDamage");
        swing.Should().Contain("MentalSystem.GetCombatMultiplier(player,");
        swing.Should().NotContain("FatigueTiredDamagePenalty");
        var defence = Method(src, "ProcessMonsterAction");
        defence.Should().Contain("MentalSystem.GetCombatMultiplier(player,");
        defence.Should().Contain("defence: true");
        defence.Should().NotContain("FatigueTiredDefensePenalty");
    }

    [Fact]
    public void Fear_is_rolled_at_combat_start_and_spent_on_the_first_turn()
    {
        var src = Src("Systems", "CombatEngine.cs");
        var fight = Method(src, "PlayerVsMonsters");
        int roll = At(fight, "RollMentalFear(player, result.Teammates);");
        At(fight, "if (ConsumeMentalFear(player, terminal))").Should().BeGreaterThan(roll);
        var follower = Method(src, "ProcessGroupedPlayerTurn");
        At(follower, "if (ConsumeMentalFear(teammate, remoteTerminal))").Should().BeGreaterThan(At(follower, "if (!teammate.CanAct())"));
    }

    [Fact]
    public void Room_lines_and_the_stairs_are_wired_in_the_dungeon()
    {
        var src = Src("Locations", "DungeonLocation.cs");
        var move = Method(src, "MoveToRoom");
        At(move, "ShowRoomMindLines();").Should().BeGreaterThan(At(move, "ApplyRoomMentalStrain();"));
        var stairs = Method(src, "DescendStairs");
        At(stairs, "if (!await ConfirmMentalDescent(player))").Should().BeGreaterThan(At(stairs, "Loc.Get(\"dungeon.nowhere_descend\")"));
        var jump = Method(src, "ChangeDungeonLevel");
        At(jump, "if (targetLevel > currentDungeonLevel && !await ConfirmMentalDescent(player))")
            .Should().BeLessThan(At(jump, "var floorResult = GenerateOrRestoreFloor(player, targetLevel);"));
        var confirm = Method(src, "ConfirmMentalDescent");
        At(confirm, "await terminal.AskYesNoAsync(Loc.Get(\"mental.descend_confirm_2\"))")
            .Should().BeGreaterThan(At(confirm, "await terminal.AskYesNoAsync(Loc.Get(portal ? \"mental.portal_confirm_1\" : \"mental.descend_confirm_1\"))"));
    }

    [Fact]
    public void Band_lines_exist_in_all_five_languages()
    {
        var keys = new List<string> { "mental.fear_other", "mental.descend_confirm_1", "mental.descend_confirm_2", "mental.descend_turned_back" };
        for (int i = 1; i <= GameConfig.MentalHallucinationLineCount; i++) keys.Add("mental.hallucination_" + i);
        for (int i = 1; i <= GameConfig.MentalUneasyLineCount; i++) keys.Add("mental.uneasy_" + i);
        for (int i = 1; i <= GameConfig.MentalFearLineCount; i++) keys.Add("mental.fear_" + i);
        foreach (var lang in new[] { "en", "es", "fr", "hu", "it" })
        {
            var text = File.ReadAllText(Path.Combine(RepoRoot(), "Localization", lang + ".json"));
            foreach (var k in keys) text.Should().Contain($"\"{k}\":", $"{lang} needs {k}");
        }
    }
}
