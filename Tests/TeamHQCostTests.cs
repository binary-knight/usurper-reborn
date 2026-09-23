using FluentAssertions;
using UsurperRemake.Locations;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.10: the Team HQ menu showed a facility's next level at base cost x next level squared, but
/// buying it charged base cost x next level. The shown price is the intended one (maintainer); the
/// menu and the purchase both read TeamCornerLocation.UpgradeCost now.
/// </summary>
public class TeamHQCostTests
{
    [Theory]
    [InlineData(5000, 0, 5_000)]      // level 1
    [InlineData(5000, 4, 125_000)]    // level 5: it charged 25,000
    [InlineData(5000, 9, 500_000)]    // level 10: it charged 50,000
    [InlineData(8000, 2, 72_000)]     // training to level 3
    public void TheNextLevel_CostsBaseTimesTheLevelSquared(long baseCost, int current, long expected) =>
        TeamCornerLocation.UpgradeCost(baseCost, current).Should().Be(expected);
}
