using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;
using static UsurperReborn.Tests.MentalRecovery1115Tests;
using static UsurperReborn.Tests.MentalRecoveryB1115Tests;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.15 Mental piece 7 follow-ups: the Exhausted XP penalty is a cut (it was a 10% bonus), and
/// every path that moves the player to a deeper floor goes through the one Mental gate
/// (DungeonLocation.ConfirmMentalDescent): Broken refuses, Breaking asks twice. A fight that ends at
/// Mental 0 is the last one: the next fight in a chain is not entered and the location loop carries
/// out the collapse once.
/// </summary>
[Collection("SharedGameSingletons")]
public class MentalCollapseGaps1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private sealed class FixedRandom : Random
    {
        private readonly int _value;
        public FixedRandom(int value) { _value = value; }
        public override int Next(int maxValue) => Math.Min(_value, maxValue - 1);
        public override int Next(int minValue, int maxValue) => Math.Clamp(_value, minValue, maxValue - 1);
        public override int Next() => _value;
        public override double NextDouble() => 0.0;
    }

    // Fatigue XP

    [Fact]
    public void Exhausted_costs_ten_percent_of_xp_in_single_player()
    {
        var c = Hero("Tired");
        c.Fatigue = GameConfig.FatigueExhaustedThreshold;
        MentalSystem.ApplyFatigueXp(c, 1000, online: false).Should().Be(900);
        MentalSystem.ApplyFatigueXp(c, 1000, online: false).Should().BeLessThan(1000, "Exhausted is a cut, never a bonus");
        MentalSystem.ApplyFatigueXp(c, 1000, online: true).Should().Be(1000, "fatigue is single-player only");
        MentalSystem.ApplyFatigueXp(c, long.MaxValue, online: false).Should().BeLessThan(long.MaxValue).And.BeGreaterThan(0);
        c.Fatigue = GameConfig.FatigueExhaustedThreshold - 1;
        MentalSystem.ApplyFatigueXp(c, 1000, online: false).Should().Be(1000, "below Exhausted nothing is taken");
    }

    [Fact]
    public void Both_victory_paths_take_the_fatigue_cut_through_the_helper()
    {
        var src = Src("Systems", "CombatEngine.cs");
        MentalBands1115Tests.Method(src, "HandleVictory")
            .Should().Contain("expReward = MentalSystem.ApplyFatigueXp(result.Player, expReward, UsurperRemake.BBS.DoorMode.IsOnlineMode);");
        MentalBands1115Tests.Method(src, "HandleVictoryMultiMonster")
            .Should().Contain("MentalSystem.ApplyFatigueXp(result.Player, adjustedExp, false)");
        Regex.IsMatch(src, @"\*\s*GameConfig\.FatigueExhaustedXPPenalty").Should().BeFalse("the signed constant is only read by the helper");
    }

    // The deeper-floor gate

    /// <summary>
    /// Every change of currentDungeonLevel in DungeonLocation.cs must have ConfirmMentalDescent earlier
    /// in its enclosing method (a decrement, one floor up, is not a descent). The only exempt changes
    /// are the field default and the three EnterLocation
    /// lines that resume the saved floor. A new path that moves the player without the gate fails here.
    /// </summary>
    [Fact]
    public void Every_floor_change_in_the_dungeon_goes_through_the_Mental_gate()
    {
        var src = Src("Locations", "DungeonLocation.cs");
        var sigs = Regex.Matches(src, @"\n[ \t]*(?:private|public|internal|protected)[^\n;=]*\([^\n]*\)[ \t]*\r?\n[ \t]*\{").Cast<Match>().ToList();
        int exempt = 0, gated = 0;
        foreach (Match m in Regex.Matches(src, @"\bcurrentDungeonLevel\s*(?:=(?!=)|\+=|\+\+)"))
        {
            int ls = src.LastIndexOf('\n', m.Index) + 1;
            string line = src.Substring(ls, src.IndexOf('\n', m.Index) - ls).Trim();
            if (line == "private int currentDungeonLevel = 1;") { exempt++; continue; }
            var sig = sigs.LastOrDefault(s => s.Index < m.Index);
            sig.Should().NotBeNull($"'{line}' must sit in a method");
            string head = src.Substring(sig!.Index, src.IndexOf('(', sig.Index) - sig.Index);
            if (head.EndsWith(" EnterLocation")) { exempt++; continue; }
            src.Substring(sig.Index, m.Index - sig.Index)
                .Should().Contain("ConfirmMentalDescent(", $"the floor change '{line}' in{head} must go through the Mental gate first");
            gated++;
        }
        exempt.Should().Be(4, "the field default and the three EnterLocation resume lines");
        gated.Should().Be(6, "the stairs, the level jump, the portal (down and up), DescendDeeper and IncreaseDifficulty");
    }

    [Fact]
    public void The_portal_uses_the_gate_before_it_moves_the_player_deeper()
    {
        var portal = MentalBands1115Tests.Method(Src("Locations", "DungeonLocation.cs"), "MysteriousPortalEncounter");
        At(portal, "if (newFloor > currentDungeonLevel && !await ConfirmMentalDescent(currentPlayer, portal: true))")
            .Should().BeLessThan(At(portal, "Loc.Get(\"dungeon.portal_whisks_deeper\")"));
    }

    private static (DungeonLocation dungeon, TerminalEmulator term, MemoryStream output) Dungeon(Character hero, int floor, params string[] input)
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(input), output);
        var dungeon = new DungeonLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(dungeon, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(dungeon, hero);
        typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.SetValue(dungeon, floor);
        typeof(DungeonLocation).GetField("dungeonRandom", F)!.SetValue(dungeon, new FixedRandom(0)); // roll 0: the portal goes 5 floors down
        return (dungeon, term, output);
    }

    private static async Task<(int floor, string shown)> EnterPortal(Character hero, params string[] answers)
    {
        var (dungeon, term, output) = Dungeon(hero, 10, new[] { "E" }.Concat(answers).ToArray());
        await (Task)typeof(DungeonLocation).GetMethod("MysteriousPortalEncounter", F)!.Invoke(dungeon, null)!;
        term.StreamWriterInternal?.Flush();
        string shown = Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");
        return ((int)typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.GetValue(dungeon)!, shown);
    }

    [Fact]
    public async Task The_portal_refuses_a_Broken_player()
    {
        var hero = Hero("Broken", 30);
        hero.MentalBroken = true;
        var (floor, shown) = await EnterPortal(hero);
        floor.Should().Be(10, "Broken never goes deeper");
        shown.Should().Contain(Loc.Get("mental.descend_refused"));
        shown.Should().NotContain(Loc.Get("dungeon.portal_whisks_deeper"));
    }

    [Fact]
    public async Task The_portal_asks_a_Breaking_player_twice()
    {
        var (floor, shown) = await EnterPortal(Hero("Breaking", 10), "Y", "N");
        floor.Should().Be(10, "the second no turns the player back");
        shown.Should().Contain(Loc.Get("mental.portal_confirm_1")).And.Contain(Loc.Get("mental.descend_confirm_2"));
        shown.Should().Contain(Loc.Get("mental.portal_turned_back"));

        (floor, _) = await EnterPortal(Hero("Breaking", 10), "N");
        floor.Should().Be(10, "the first no turns the player back");

        (floor, shown) = await EnterPortal(Hero("Breaking", 10), "Y", "Y");
        floor.Should().Be(15, "two yeses go ahead");
        shown.Should().Contain(Loc.Get("dungeon.portal_whisks_deeper"));
    }

    [Fact]
    public async Task The_portal_does_not_ask_a_steady_player()
    {
        var (floor, shown) = await EnterPortal(Hero("Steady", 80));
        floor.Should().Be(15);
        shown.Should().NotContain(Loc.Get("mental.portal_confirm_1"));
    }

    // Collapse after every fight

    [Fact]
    public void A_collapse_is_due_at_zero_but_not_in_jail_or_the_Pantheon()
    {
        MentalSystem.CollapseDue(Hero("Zero", 0)).Should().BeTrue();
        MentalSystem.CollapseDue(Hero("One", 1)).Should().BeFalse();
        var jailed = Hero("Jailed", 0); jailed.DaysInPrison = 2;
        MentalSystem.CollapseDue(jailed).Should().BeFalse("never while jailed");
        var god = Hero("Ascended", 0); god.IsImmortal = true;
        MentalSystem.CollapseDue(god).Should().BeFalse("never while locked to the Pantheon, or no fight could start");
    }

    [Fact]
    public async Task A_fight_is_not_entered_at_Mental_zero()
    {
        var hero = Hero("Spent", 0);
        var rat = new Monster { Name = "Rat", Level = 1, HP = 10, MaxHP = 10 };
        var output = new MemoryStream();
        var engine = new CombatEngine(new TerminalEmulator(new LineStream(Array.Empty<string>()), output));
        var fight = engine.PlayerVsMonsters(hero, new System.Collections.Generic.List<Monster> { rat });
        (await Task.WhenAny(fight, Task.Delay(15000))).Should().BeSameAs(fight, "the fight must return at once");
        var result = await fight;
        result.Outcome.Should().Be(CombatOutcome.PlayerEscaped);
        result.MentalCollapsePending.Should().BeTrue();
        result.DefeatedMonsters.Should().BeEmpty();
        rat.HP.Should().Be(10, "no blow was struck");
        hero.Mental.Should().Be(0, "the collapse itself is left to the location loop, once");
        hero.MentalBroken.Should().BeFalse();
    }

    [Fact]
    public void The_fight_gate_comes_first_and_the_fight_end_flags_a_collapse()
    {
        var fight = MentalBands1115Tests.Method(Src("Systems", "CombatEngine.cs"), "PlayerVsMonsters");
        int gate = At(fight, "if (!storyFight && MentalSystem.CollapseDue(player))");
        gate.Should().BeLessThan(At(fight, "bool isGodMode"));
        At(fight, "MentalCollapsePending = true,").Should().BeGreaterThan(gate);
        At(fight, "result.MentalCollapsePending = MentalSystem.CollapseDue(player);")
            .Should().BeGreaterThan(At(fight, "ApplyMentalFightEnd(result, mentalFloor, fledThisFight, BossContext != null, terminal, mentalAtFightStart);"));
    }

    [Fact]
    public void A_collapse_is_carried_out_once()
    {
        var shallow = Hero("Shallow", 0);
        shallow.Gold = 1000;
        MentalSystem.ApplyCollapseRescue(shallow).Should().Be(50);
        MentalSystem.CollapseDue(shallow).Should().BeFalse("the loop does not collapse the player again, or take a second fee");
        var deep = Hero("Deep", 0);
        MentalSystem.ApplyCollapseDeathAftermath(deep);
        MentalSystem.CollapseDue(deep).Should().BeFalse();
    }

    [Fact]
    public void A_deep_collapse_death_still_leaves_the_player_Broken_at_twenty()
    {
        MentalSystem.IsCollapseDeath(26).Should().BeTrue();
        MentalSystem.IsCollapseDeath(25).Should().BeFalse();
        var c = Hero("Fallen", 0);
        MentalSystem.ApplyCollapseDeathAftermath(c);
        c.MentalBroken.Should().BeTrue();
        c.Mental.Should().Be(20);
        var collapse = MentalBands1115Tests.Method(Src("Locations", "BaseLocation.cs"), "HandleMentalCollapse");
        At(collapse, "MentalSystem.ApplyCollapseDeathAftermath(player);")
            .Should().BeGreaterThan(At(collapse, "await new CombatEngine(terminal).HandleMentalCollapseDeath(player)"))
            .And.BeLessThan(At(collapse, "long fee = MentalSystem.ApplyCollapseRescue(player);"));
    }

    [Fact]
    public void Portal_lines_exist_in_all_five_languages()
    {
        foreach (var lang in new[] { "en", "es", "fr", "hu", "it" })
        {
            var text = File.ReadAllText(Path.Combine(RepoRoot(), "Localization", lang + ".json"));
            foreach (var k in new[] { "mental.portal_confirm_1", "mental.portal_turned_back" })
                text.Should().Contain($"\"{k}\":", $"{lang} needs {k}");
        }
    }
}
