using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.8 piece P1: the Orc tier is 0.9 for regular monsters and champions, and a floor boss that rolls the
/// Orc tier keeps the old 1.0 through BossPowerMultiplier. A tier without the field uses its own power.
/// </summary>
[Collection("SharedGameSingletons")]
public class OrcTier128Tests
{
    private static (long HP, long Strength, long Defence) Expected(int level, float power, bool boss, bool mini)
    {
        var m = typeof(MonsterGenerator).GetMethod("CalculateMonsterStats", BindingFlags.NonPublic | BindingFlags.Static)!;
        var s = m.Invoke(null, new object?[] { level, power, boss, mini, 1.0, null, null })!;
        long Get(string n) => Convert.ToInt64(s.GetType().GetProperty(n)!.GetValue(s) ?? s.GetType().GetField(n)!.GetValue(s));
        return (Get("HP"), Get("Strength"), Get("Defence"));
    }

    /// <summary>First seed whose first draw (the family and tier pick) is the Orc tier.</summary>
    private static int OrcSeed()
    {
        for (int seed = 0; seed < 5000; seed++)
        {
            var (_, tier) = MonsterFamilies.GetMonsterForLevel(5, new Random(seed));
            if (tier.Name == "Orc") return seed;
        }
        throw new InvalidOperationException("no Orc seed");
    }

    [Fact]
    public void A1_OrcTierIsNinetyPercentForRegularMonstersAndChampions()
    {
        MonsterFamilies.AllFamilies.SelectMany(f => f.Tiers).First(t => t.Name == "Orc").PowerMultiplier.Should().Be(0.9f);

        // a regular group of Orcs (same-family branch): stats equal the 0.9 formula
        Monster? regular = null;
        for (int seed = 0; seed < 20000 && regular == null; seed++)
            regular = MonsterGenerator.GenerateMonsterGroup(5, new Random(seed))
                .FirstOrDefault(m => m.TierName == "Orc" && !m.IsMiniBoss);
        regular.Should().NotBeNull();
        var e = Expected(5, 0.9f, false, false);
        regular!.MaxHP.Should().Be(e.HP);
        regular.Strength.Should().Be(e.Strength);

        // a champion follows the tier
        var champ = MonsterGenerator.GenerateMonster(5, isBoss: false, isMiniBoss: true, random: new Random(OrcSeed()));
        champ.TierName.Should().Be("Orc");
        var ec = Expected(5, 0.9f, false, true);
        champ.MaxHP.Should().Be(ec.HP);
        champ.Strength.Should().Be(ec.Strength);
    }

    [Fact]
    public void A2_BossThatRollsTheOrcTierKeepsFullPower()
    {
        var boss = MonsterGenerator.GenerateMonster(5, isBoss: true, random: new Random(OrcSeed()));
        boss.TierName.Should().Be("Orc");
        var e = Expected(5, 1.0f, true, false);
        boss.MaxHP.Should().Be(e.HP);
        boss.Strength.Should().Be(e.Strength);
        boss.MaxHP.Should().BeGreaterThan(Expected(5, 0.9f, true, false).HP);
    }

    [Fact]
    public void A3_TierWithoutBossPowerUsesItsOwnPowerForBosses()
    {
        const string json = "[{\"FamilyName\":\"Orcish\",\"Description\":\"d\",\"AttackType\":\"Slash\",\"Tiers\":[" +
            "{\"Name\":\"Orc\",\"MinLevel\":5,\"MaxLevel\":20,\"Color\":\"red\",\"PowerMultiplier\":0.8}]}]";
        var families = JsonSerializer.Deserialize<List<MonsterFamilies.MonsterFamily>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        families[0].Tiers[0].BossPowerMultiplier.Should().BeNull();

        var prop = typeof(GameDataLoader).GetProperty("MonsterFamilies", BindingFlags.Public | BindingFlags.Static)!;
        var saved = prop.GetValue(null);
        prop.GetSetMethod(true)!.Invoke(null, new object?[] { families });
        try
        {
            var boss = MonsterGenerator.GenerateMonster(5, isBoss: true, random: new Random(1));
            var e = Expected(5, 0.8f, true, false);
            boss.MaxHP.Should().Be(e.HP);
            boss.Strength.Should().Be(e.Strength);
        }
        finally { prop.GetSetMethod(true)!.Invoke(null, new object?[] { saved }); }
    }
}
