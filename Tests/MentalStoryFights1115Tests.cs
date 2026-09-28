using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;
using static UsurperReborn.Tests.MentalRecovery1115Tests;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.15 Mental piece 7 follow-up: a one-time story fight is fought even at Mental 0. The fight
/// gate in PlayerVsMonsters refuses a fight at Mental 0 (the collapse is due), which is right for a
/// fight that can be had again, but the Noctura betrayal comes straight after the Manwe kill, in the
/// same action, and a refusal there is recorded for good as a loss (noctura_escaped_with_power and
/// MetaProgression). The caller opts out with storyFight: true; the collapse still follows the fight,
/// once, from the location loop.
/// </summary>
[Collection("SharedGameSingletons")]
public class MentalStoryFights1115Tests
{
    /// <summary>The one-time story fights that are fought at Mental 0: file and method of each caller.</summary>
    private static readonly (string File, string Method)[] ExemptFights =
    {
        ("Systems/OldGodBossSystem.cs", "HandleNocturaBetrayal"),
    };

    private static Character Fighter(string name) => new Character
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Human, Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 10,
        HP = 500, MaxHP = 500, BaseMaxHP = 500, Strength = 80, BaseStrength = 80, Defence = 40, BaseDefence = 40,
        Dexterity = 30, BaseDexterity = 30, Agility = 25, BaseAgility = 25, Constitution = 30, BaseConstitution = 30,
        Stamina = 100, Gold = 100, CombatSpeed = CombatSpeed.Instant, MentalHintShown = true, Mental = 0,
    };

    private static Monster Rat() => new Monster { Name = "Sewer Rat", Level = 1, HP = 1, MaxHP = 1, Strength = 1, Defence = 0, Experience = 5, Gold = 3 };

    private static CombatEngine Engine(MemoryStream output)
    {
        var script = new MemoryStream(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("A\n", 12)) + string.Concat(Enumerable.Repeat("P\n", 6))));
        var engine = new CombatEngine(new TerminalEmulator(script, output));
        engine.SeedRandomForTests(1115);
        return engine;
    }

    private static void AssertFoughtAndCollapseLeftToTheLoop(Character hero, Monster rat, CombatResult result, MemoryStream output)
    {
        string tail = Text(output);
        if (tail.Length > 1500) tail = tail[^1500..];
        result.MentalCollapseNotFought.Should().BeFalse(tail);
        result.Outcome.Should().Be(CombatOutcome.Victory, tail);
        rat.HP.Should().BeLessThanOrEqualTo(0, "the fight was fought");
        result.MentalCollapsePending.Should().BeTrue("the fight end still flags the collapse for the location loop");
        MentalSystem.CollapseDue(hero).Should().BeTrue("the collapse is not applied inside the fight, the loop does it once");
        hero.MentalBroken.Should().BeFalse();
    }

    [Fact]
    public async Task A_story_fight_is_fought_at_Mental_zero()
    {
        var hero = Fighter("Storied");
        var rat = Rat();
        var output = new MemoryStream();
        var fight = Engine(output).PlayerVsMonsters(hero, new List<Monster> { rat }, offerMonkEncounter: false, storyFight: true);
        (await Task.WhenAny(fight, Task.Delay(30000))).Should().BeSameAs(fight);
        AssertFoughtAndCollapseLeftToTheLoop(hero, rat, await fight, output);
    }

    [Fact]
    public async Task The_single_monster_call_passes_the_story_flag_on()
    {
        var hero = Fighter("Single");
        var rat = Rat();
        var output = new MemoryStream();
        var fight = Engine(output).PlayerVsMonster(hero, rat, null, false, storyFight: true);
        (await Task.WhenAny(fight, Task.Delay(30000))).Should().BeSameAs(fight);
        AssertFoughtAndCollapseLeftToTheLoop(hero, rat, await fight, output);
    }

    [Fact]
    public async Task Any_other_fight_is_still_refused_at_Mental_zero()
    {
        var hero = Fighter("Refused");
        var rat = Rat();
        var output = new MemoryStream();
        var result = await Engine(output).PlayerVsMonsters(hero, new List<Monster> { rat }, offerMonkEncounter: false);
        result.MentalCollapseNotFought.Should().BeTrue();
        result.Outcome.Should().Be(CombatOutcome.PlayerEscaped);
        rat.HP.Should().Be(1, "no blow was struck");
    }

    [Fact]
    public void The_fight_gate_skips_the_refusal_only_for_a_story_fight()
    {
        var fight = MentalBands1115Tests.Method(Src("Systems", "CombatEngine.cs"), "PlayerVsMonsters");
        fight.Should().Contain("if (!storyFight && MentalSystem.CollapseDue(player))");
        var single = MentalBands1115Tests.Method(Src("Systems", "CombatEngine.cs"), "PlayerVsMonster");
        single.Should().Contain("storyFight: storyFight");
    }

    [Fact]
    public void The_exempt_story_fights_are_pinned()
    {
        // every storyFight: true in the game, outside the combat engine itself
        var hits = Directory.GetFiles(Path.Combine(RepoRoot(), "Scripts"), "*.cs", SearchOption.AllDirectories)
            .Select(f => (File: Path.GetRelativePath(Path.Combine(RepoRoot(), "Scripts"), f).Replace('\\', '/'), Code: Code(File.ReadAllText(f))))
            .SelectMany(f => Regex.Matches(f.Code, @"storyFight:\s*true").Select(_ => f.File))
            .ToList();
        hits.Should().BeEquivalentTo(ExemptFights.Select(e => e.File), "each exempt story fight is listed here with the reason, and nothing else is exempt");

        foreach (var (file, method) in ExemptFights)
        {
            var body = MentalBands1115Tests.Method(Src(file.Split('/')), method);
            Regex.IsMatch(body, @"\.PlayerVsMonsters?\([^;]*storyFight:\s*true\)").Should().BeTrue($"{method} fights as a story fight");
        }
    }
}
