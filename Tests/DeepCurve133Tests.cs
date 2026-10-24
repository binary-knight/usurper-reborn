using System;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.3: the regular group size grows a little each floor from floor 25 (no step at 31 or 61), and floors below
/// 25 keep the 1-3 draw; monster HP, Strength, punch and weapon power keep growing past floor 50 at a full floor
/// a floor, with floors 1 to 50 unchanged.
/// </summary>
public class DeepCurve133Tests
{
    [Theory]
    [InlineData(24, 2.0)]
    [InlineData(25, 2.0)]
    [InlineData(30, 2.3)]
    [InlineData(31, 2.36)]
    [InlineData(35, 2.6)]
    [InlineData(50, 3.5)]
    [InlineData(61, 4.0)]
    [InlineData(100, 4.0)]
    public void DeepGroupMean_RisesEvenlyFrom25To61(int floor, double mean)
        => MonsterGenerator.DeepGroupMean(floor).Should().BeApproximately(mean, 1e-9);

    [Fact]
    public void DeepGroupMean_NeverStepsMoreThanAFifthOfAMonsterFromOneFloorToTheNext()
    {
        for (int f = 2; f <= 100; f++)
            (MonsterGenerator.DeepGroupMean(f) - MonsterGenerator.DeepGroupMean(f - 1)).Should().BeInRange(0, 0.2, $"floor {f}");
    }

    [Theory]
    [InlineData(25, 1, 3)]
    [InlineData(61, 3, 5)]
    [InlineData(100, 3, 5)]
    public void DeepGroupSize_AtAWholeMean_IsTheOldDrawAtAThirdEach(int floor, int low, int high)
    {
        const int n = 30000;
        var sizes = Enumerable.Range(0, n).Select(i => MonsterGenerator.DeepGroupSize(floor, (i + 0.5) / n)).ToList();
        sizes.Min().Should().Be(low);
        sizes.Max().Should().Be(high);
        foreach (int s in Enumerable.Range(low, high - low + 1))
            ((double)sizes.Count(x => x == s) / n).Should().BeApproximately(1.0 / 3, 1e-3, $"size {s}");
    }

    [Theory]
    [InlineData(24)]
    [InlineData(25)]
    [InlineData(30)]
    [InlineData(31)]
    [InlineData(35)]
    [InlineData(50)]
    [InlineData(51)]
    [InlineData(60)]
    [InlineData(61)]
    [InlineData(75)]
    [InlineData(100)]
    public void GenerateMonsterGroup_RegularGroupMean_FollowsTheFloor(int floor)
    {
        var random = new Random(1330 + floor);
        var sizes = Enumerable.Range(0, 10000)
            .Select(_ => MonsterGenerator.GenerateMonsterGroup(floor, random))
            .Where(g => !(g.Count == 1 && g[0].IsMiniBoss))
            .Select(g => g.Count).ToList();
        sizes.Average().Should().BeApproximately(MonsterGenerator.DeepGroupMean(floor), 0.03, $"floor {floor}");
        sizes.Should().OnlyContain(s => s >= 1 && s <= 5);
    }

    [Fact]
    public void GenerateMonsterGroup_Floor24_KeepsTheOneToThreeDraw()
    {
        var random = new Random(24);
        var sizes = Enumerable.Range(0, 10000)
            .Select(_ => MonsterGenerator.GenerateMonsterGroup(24, random))
            .Where(g => !(g.Count == 1 && g[0].IsMiniBoss))
            .Select(g => g.Count).ToList();
        foreach (int s in new[] { 1, 2, 3 })
            ((double)sizes.Count(x => x == s) / sizes.Count).Should().BeApproximately(1.0 / 3, 0.02, $"size {s}");
        sizes.Max().Should().Be(3);
    }

    // GetWikiStats (power 1.0, Normal, no sysop multiplier): HP, Strength, Defence, Punch, WeaponPower, ArmorPower
    [Theory]
    [InlineData(25, 4416, 211, 85, 85, 148, 31)]
    public void MonsterStats_AtFloor25_AreUnchanged(int level, long hp, long str, long def, long punch, long wp, long ap)
        => MonsterGenerator.GetWikiStats(level, 1.0f).Should().Be((hp, str, def, punch, wp, ap));

    // 1.3.3: these also carry the deep ramp from floor 26 (DeepRamp133Tests); without it they were 9315 at 50, 9515 at
    // 51, 11342 at 60, 15484 at 80 and 19725 at 100
    [Theory]
    [InlineData(50, 14949, 688, 173, 276, 486, 63)]
    [InlineData(51, 15282, 704, 175, 281, 499, 65)]
    [InlineData(60, 18328, 852, 207, 339, 603, 76)]
    [InlineData(80, 25362, 1368, 276, 544, 969, 103)]    // 1.3.7: was str 1244, punch 495, weapon 882
    [InlineData(100, 32744, 2016, 346, 798, 1428, 128)]  // 1.3.7: was str 1831, punch 726, weapon 1298
    public void MonsterStats_PastFloor50_GrowAtAFullFloorAFloor(int level, long hp, long str, long def, long punch, long wp, long ap)
    {
        MonsterGenerator.MonsterLevelSlopePast50.Should().Be(1.0);
        MonsterGenerator.GetWikiStats(level, 1.0f).Should().Be((hp, str, def, punch, wp, ap));
    }
}
