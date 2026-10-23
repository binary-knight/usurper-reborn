using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>1.3.7: deep monster damage keeps its 1.3.3 line up to level 75 and then rises to 1.97 by level 80 and 2.31 by level 100.</summary>
public class DD137DeepRamp
{
    [Theory]
    [InlineData(25, 1.0)]
    [InlineData(35, 1.44)]
    [InlineData(50, 1.605)]
    [InlineData(75, 1.715)]
    public void Damage_UpToLevel75_IsTheBaseLine(int level, double factor)
        => MonsterGenerator.DeepMonsterDamageFactor(level).Should().BeApproximately(factor, 1e-9);

    [Fact]
    public void Damage_UpToLevel75_EqualsTheBaseLine()
    {
        // base line: 1.0 at 25 to 1.44 at 35 to 1.605 at 50 to 1.715 at 75, straight lines
        for (int level = 1; level <= 75; level++)
        {
            double expected = level <= 25 ? 1.0
                : level <= 35 ? 1.0 + 0.44 * (level - 25) / 10.0
                : level <= 50 ? 1.44 + 0.165 * (level - 35) / 15.0
                : 1.605 + 0.11 * (level - 50) / 25.0;
            MonsterGenerator.DeepMonsterDamageFactor(level).Should().BeApproximately(expected, 1e-9, $"level {level}");
        }
    }

    [Theory]
    [InlineData(80, 1.97)]
    [InlineData(90, 2.14)]
    [InlineData(100, 2.31)]
    public void Damage_AfterLevel75_ReachesTheNewWaypoints(int level, double factor)
        => MonsterGenerator.DeepMonsterDamageFactor(level).Should().BeApproximately(factor, 1e-9);

    [Fact]
    public void Damage_NeverFallsFromLevelToLevel()
    {
        for (int level = 2; level <= 100; level++)
            MonsterGenerator.DeepMonsterDamageFactor(level).Should().BeGreaterThanOrEqualTo(MonsterGenerator.DeepMonsterDamageFactor(level - 1), $"level {level}");
    }
}
