using System;
using System.Linq;
using FluentAssertions;
using UsurperRemake;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.10: regular dungeon monsters on the five floors before an Old God grow tougher, HP and damage
/// only, +5% a floor up to +25% on the floor before the god (player report: the floors before
/// Mael'Keth went in one or two turns, then he killed three companions). Defence is not scaled.
/// </summary>
[Collection("SharedGameSingletons")]
public class OldGodApproachRampTests
{
    [Theory]
    [InlineData(19, 1.00)]
    [InlineData(20, 1.05)]
    [InlineData(22, 1.15)]
    [InlineData(24, 1.25)]
    [InlineData(25, 1.00)]   // Mael'Keth's own floor
    [InlineData(26, 1.00)]
    [InlineData(39, 1.25)]   // before Veloura
    [InlineData(95, 1.00)]   // a god's floor, though it is also five before Manwe's 100
    [InlineData(99, 1.25)]
    public void TheScale_RisesOverTheFiveFloorsBeforeAGod(int floor, double expected) =>
        MonsterGenerator.OldGodApproachScale(floor).Should().BeApproximately(expected, 1e-9);

    [Fact]
    public void AScaledGroup_HasMoreHPAndDamage_ButTheSameDefence()
    {
        // The scale draws nothing from the random source, so the same seed gives the same group.
        var plain = MonsterGenerator.GenerateMonsterGroup(24, new Random(7), 1.0);
        var ramped = MonsterGenerator.GenerateMonsterGroup(24, new Random(7), 1.25);
        ramped.Should().HaveCount(plain.Count);
        foreach (var (a, b) in plain.Zip(ramped))
        {
            b.Name.Should().Be(a.Name);
            ((double)b.MaxHP / a.MaxHP).Should().BeApproximately(1.25, 0.02, a.Name);
            ((double)b.Strength / a.Strength).Should().BeApproximately(1.25, 0.03, a.Name);
            b.Defence.Should().Be(a.Defence, "defence is not scaled");
            b.ArmPow.Should().Be(a.ArmPow);
        }
    }
}
