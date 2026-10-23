using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using Xunit;
using G = UsurperReborn.Tests.HitRoll128Golden;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.7 LH: the Voidreaver's Void Hunger (heal a share of max HP) and Soul Eater (restore a share of max mana) act at
/// the kill, inside the fight, once a fallen foe, a teammate's kill included, and the victory block does not repeat
/// them. Fights are real PlayerVsMonsters fights read from the terminal.
/// </summary>
[Collection("SharedGameSingletons")]
public class LH137VoidHunger
{
    private static readonly Regex Ansi = new("\u001b\\[[0-9;?]*[A-Za-z]", RegexOptions.Compiled);

    private static Character Reaver(CharacterClass cls = CharacterClass.Voidreaver)
    {
        var c = G.Hero(2000, 300, 10, cls);
        c.BaseMaxMana = 200; c.MaxMana = 200;
        c.RecalculateStats();
        c.MaxMana = Math.Max(c.MaxMana, 200); c.Mana = 10; c.HP = 1000;
        return c;
    }

    private static long Heal(Character c) => Math.Max(1, (long)(c.MaxHP * GameConfig.VoidreaverVoidHungerPercent));
    private static long Mana(Character c) => Math.Max(1, (int)(c.MaxMana * GameConfig.VoidreaverSoulEaterManaPercent));

    private static async Task<(string Text, G.Run Run)> Go(Character hero, List<Monster> foes, List<Character>? team, string script)
    {
        var run = await G.Fight(hero, foes, team, 137, script);
        run.Error.Should().BeNull();
        return (Ansi.Replace(run.Transcript, ""), run);
    }

    private static int Count(string text, string needle) => Regex.Matches(text, Regex.Escape(needle)).Count;
    private const string HungerLine = "Void Hunger: Consumed";
    private const string EaterLine = "Soul Eater: Devoured";

    private static string Rats(int n) => string.Concat(Enumerable.Repeat("A\n1\n", n)) + string.Concat(Enumerable.Repeat("P\n", 10));

    [Fact]
    public async Task LH137_AGroupFight_HealsAtTheFirstKill_WhileFoesRemain()
    {
        var hero = Reaver();
        var (text, _) = await Go(hero, new List<Monster> { G.Foe("Rat", 1, 1), G.Foe("Tank", 400, 1) }, null, Rats(40));
        int feed = text.IndexOf(HungerLine, StringComparison.Ordinal);
        int round2 = text.IndexOf("Round 2", StringComparison.Ordinal);
        feed.Should().BeGreaterThan(0);
        round2.Should().BeGreaterThan(0);
        feed.Should().BeLessThan(round2, "the first kill heals in round 1, while the tank still stands");
    }

    [Fact]
    public async Task LH137_EachFoeFeedsOnce_AndNothingMoreAtVictory()
    {
        var hero = Reaver();
        long start = hero.HP;
        var foes = new List<Monster> { G.Foe("Rat", 1, 1), G.Foe("Rat", 1, 1), G.Foe("Rat", 1, 1) };
        var (text, run) = await Go(hero, foes, null, Rats(40));
        run.Result!.Outcome.Should().Be(CombatOutcome.Victory);
        Count(text, HungerLine).Should().Be(3, "one feed per fallen foe, none repeated by the victory block");
        Count(text, EaterLine).Should().Be(3);
        hero.HP.Should().BeGreaterOrEqualTo(start, "three feeds");
        var amounts = Regex.Matches(text, @"Consumed (\d+) HP").Select(m => long.Parse(m.Groups[1].Value)).ToList();
        amounts.Sum().Should().Be(3 * Heal(hero));
    }

    [Fact]
    public async Task LH137_ATeammatesKill_FeedsToo()
    {
        var hero = Reaver();
        var mate = G.Mate("Ally", 3000, 10, 5000);
        var foes = new List<Monster> { G.Foe("Rat", 1, 1), G.Foe("Rat", 1, 1) };
        // the hero defends every round; only the teammate kills
        var (text, run) = await Go(hero, foes, new List<Character> { mate }, string.Concat(Enumerable.Repeat("D\n", 40)) + string.Concat(Enumerable.Repeat("P\n", 10)));
        run.Result!.Outcome.Should().Be(CombatOutcome.Victory);
        Count(text, HungerLine).Should().Be(2, "a teammate's kill feeds, once a foe");
        text.IndexOf(HungerLine, StringComparison.Ordinal).Should().BeLessThan(text.IndexOf("Round 2", StringComparison.Ordinal) < 0 ? int.MaxValue : text.IndexOf("Round 2", StringComparison.Ordinal),
            "the first teammate kill feeds inside the fight");
    }

    [Fact]
    public async Task LH137_AClassOtherThanVoidreaver_GetsNothing()
    {
        var hero = Reaver(CharacterClass.Warrior);
        long mana = hero.Mana;
        var (text, run) = await Go(hero, new List<Monster> { G.Foe("Rat", 1, 1), G.Foe("Rat", 1, 1) }, null, Rats(40));
        run.Result!.Outcome.Should().Be(CombatOutcome.Victory);
        Count(text, HungerLine).Should().Be(0);
        Count(text, EaterLine).Should().Be(0);
        hero.Mana.Should().Be(mana);
    }

    [Fact]
    public async Task LH137_ASingleMonsterFight_FeedsOnce()
    {
        var hero = Reaver();
        var (text, run) = await Go(hero, new List<Monster> { G.Foe("Rat", 1, 1) }, null, Rats(40));
        run.Result!.Outcome.Should().Be(CombatOutcome.Victory);
        Count(text, HungerLine).Should().Be(1);
        Count(text, EaterLine).Should().Be(1);
    }

    [Fact]
    public async Task LH137_SoulEater_RestoresManaPerFoe_AtTheKill()
    {
        var hero = Reaver();
        long before = hero.Mana;
        var (text, run) = await Go(hero, new List<Monster> { G.Foe("Rat", 1, 1), G.Foe("Rat", 1, 1) }, null, Rats(40));
        run.Result!.Outcome.Should().Be(CombatOutcome.Victory);
        var amounts = Regex.Matches(text, @"Devoured (\d+) mana").Select(m => long.Parse(m.Groups[1].Value)).ToList();
        amounts.Should().HaveCount(2);
        amounts.Sum().Should().Be(2 * Mana(hero));
        text.IndexOf(EaterLine, StringComparison.Ordinal).Should().BeLessThan(text.IndexOf("Round 2", StringComparison.Ordinal) is var r2 && r2 < 0 ? int.MaxValue : r2);
        hero.Mana.Should().BeGreaterThan(before);
    }
}
