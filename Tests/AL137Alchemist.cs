using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;
using G = UsurperReborn.Tests.HitRoll128Golden;
using S = UsurperReborn.Tests.ClassTexts136Support;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.7 AL (the Alchemist lift): Philosopher's Bomb base 65 to 260, Cataclysm Bomb 85 to 340, Corrosive Cloud 50 to 150.
/// The shared area core is untouched. A teammate Alchemist reads the same ability row. The ability descriptions state
/// no number, so no text changed.
/// </summary>
[Collection("SharedGameSingletons")]
public class AL137Alchemist
{
    private static readonly (string Id, int Base, int Old)[] Rows =
    {
        ("philosophers_bomb", 260, 65),
        ("cataclysm_bomb", 340, 85),
        ("corrosive_cloud", 150, 50),
    };

    public static IEnumerable<object[]> Each() => Rows.Select(r => new object[] { r.Id, r.Base, r.Old });

    private static Character Alchemist(bool npc = false)
    {
        var c = S.Hero(CharacterClass.Alchemist, 100);
        if (npc) c.AI = CharacterAI.Computer;
        return c;
    }

    /// <summary>The window one cast can land in: (base + a quarter of weapon power) * level scale * stat scale * 0.9..1.1.</summary>
    private static (int Low, int High) Window(Character c, int baseDamage)
    {
        double level = 1.0 + c.Level * 0.03;
        double stat = 1.0 + Math.Max(0, (c.Intelligence - 10) * 0.04) + Math.Max(0, (c.Dexterity - 10) * 0.02);
        double scaled = (baseDamage + c.AbilityWeapPow * 0.25) * level * Math.Min(stat, 4.5);
        return ((int)(scaled * 0.9) - 1, (int)(scaled * 1.1) + 1);
    }

    [Theory]
    [MemberData(nameof(Each))]
    public void The_base_is_the_lifted_figure(string id, int expected, int old)
    {
        var a = ClassAbilitySystem.GetAbility(id)!;
        a.BaseDamage.Should().Be(expected, id);
        a.BaseDamage.Should().BeGreaterThan(old, id);
        a.AvailableToClasses.Should().Equal(new[] { CharacterClass.Alchemist }, id);
    }

    [Theory]
    [MemberData(nameof(Each))]
    public void A_cast_lands_in_the_window_of_the_new_base(string id, int expected, int old)
    {
        var hero = Alchemist();
        var (low, high) = Window(hero, expected);
        for (int seed = 1; seed <= 20; seed++)
        {
            int d = ClassAbilitySystem.UseAbility(hero, id, new Random(seed)).Damage;
            d.Should().BeInRange(low, high, id);
        }
        d_old(hero, old).Should().BeLessThan(low, "the old base would land below the window");
    }

    private static int d_old(Character c, int old) => Window(c, old).High;

    [Theory]
    [MemberData(nameof(Each))]
    public void Every_foe_in_the_area_takes_the_new_figure(string id, int expected, int old)
    {
        var hero = Alchemist();
        var foes = new List<Monster> { G.Dummy(), G.Dummy(), G.Dummy() };
        var cast = S.Run(id, hero, foes);
        cast.Lost.Should().HaveCount(3);
        foreach (long lost in cast.Lost)
            lost.Should().BeGreaterThan(Window(hero, old).High, id);
        cast.Result.Damage.Should().BeInRange(Window(hero, expected).Low, Window(hero, expected).High, id);
    }

    [Theory]
    [MemberData(nameof(Each))]
    public void A_teammate_alchemist_reads_the_same_row(string id, int expected, int old)
    {
        var mate = Alchemist(npc: true);
        ClassAbilitySystem.GetAvailableAbilities(mate).Select(a => a.Id).Should().Contain(id);
        ClassAbilitySystem.GetAbility(id)!.BaseDamage.Should().Be(expected);
        var (low, high) = Window(mate, expected);
        ClassAbilitySystem.UseAbility(mate, id, new Random(7)).Damage.Should().BeInRange(low, high, id);
    }

    [Theory]
    [MemberData(nameof(Each))]
    public void The_description_states_no_number_and_is_the_code_text(string id, int expected, int old)
    {
        S.AbilityDescIsCode(id);
        foreach (var lang in S.Langs)
        {
            Loc.HasIn(lang, $"ability.{id}.desc").Should().BeTrue($"{id} {lang}");
            Loc.GetIn(lang, $"ability.{id}.desc").Should().NotMatchRegex(@"\d", $"{id} {lang} states no number");
        }
    }
}
