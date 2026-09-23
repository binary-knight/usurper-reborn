using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.11 (player report, level 36: "the gold rewards in the forest/mountain/swamp/beach seem to be
/// low by a factor of 100, 148 in the Stormbreak Coast"): wilderness gold finds grew by 3 to 20 gold a
/// level, while the matching dungeon finds grow by 100 to 300. They now pay on the dungeon's scale.
/// </summary>
public class WildernessGoldTests
{
    [Theory]
    [InlineData("small", 36, 36 * 30, 36 * 30 + 36 * 20)]
    [InlineData("medium", 36, 36 * 100, 36 * 100 + 36 * 200)]
    [InlineData("treasure", 36, 36 * 100, 36 * 100 + 36 * 200)]
    [InlineData("large", 36, 36 * 150, 36 * 150 + 36 * 200)]
    public void AFind_PaysOnTheDungeonsScale(string size, int level, long min, long maxExclusive)
    {
        var rng = new Random(1);
        for (int i = 0; i < 200; i++)
        {
            long gold = WildernessLocation.FindGold(size, level, rng);
            gold.Should().BeGreaterThanOrEqualTo(min).And.BeLessThan(maxExclusive);
        }
    }

    [Fact]
    public void AtLevel36_ARuinsTreasure_IsNoLongerAboutAHundred()
    {
        // The report's 148 was the ruins treasure: 30 + level x 3 + up to 50.
        WildernessLocation.FindGold("treasure", 36, new Random(7)).Should().BeGreaterThan(3000);
    }

    [Fact]
    public void EveryWildernessGoldFind_UsesTheScale()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Scripts"))) dir = dir.Parent;
        string src = File.ReadAllText(Path.Combine(dir!.FullName, "Scripts", "Locations", "WildernessLocation.cs"));
        Regex.Matches(src, @"FindGold\(""(small|medium|large|treasure)"", RewardLevel\(region\)").Count.Should().Be(4);
        src.Should().NotContain("levelScale", "the old region-level scale paid a few gold a level");
    }
}
