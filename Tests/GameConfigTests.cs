using Xunit;
using FluentAssertions;

namespace UsurperReborn.Tests;

/// <summary>
/// Unit tests for GameConfig constants and configuration
/// </summary>
public class GameConfigTests
{
    [Fact]
    public void GameConfig_HasValidVersion()
    {
        GameConfig.Version.Should().NotBeNullOrEmpty();
        GameConfig.Version.Should().MatchRegex(@"\d+\.\d+");
    }

    [Fact]
    public void GameConfig_HasVersionName()
    {
        GameConfig.VersionName.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GameConfig_PlayerLimits_AreReasonable()
    {
        GameConfig.MaxPlayers.Should().BeGreaterThan(0);
        GameConfig.MaxTeamMembers.Should().BeGreaterThan(0);
        GameConfig.MaxLevel.Should().BeGreaterThanOrEqualTo(100);
    }

    [Fact]
    public void GameConfig_CombatSettings_ArePositive()
    {
        GameConfig.CriticalHitChance.Should().BeGreaterThan(0);
        GameConfig.CriticalHitMultiplier.Should().BeGreaterThan(1.0f);
        GameConfig.BackstabMultiplier.Should().BeGreaterThan(1.0f);
        GameConfig.BerserkMultiplier.Should().BeGreaterThan(1.0f);
    }

    [Fact]
    public void GameConfig_InventoryLimits_AreReasonable()
    {
        GameConfig.MaxInventoryItems.Should().BeGreaterThan(10);
        GameConfig.MaxItems.Should().BeGreaterThan(100);
        GameConfig.MaxWeapons.Should().BeGreaterThan(10);
        GameConfig.MaxArmor.Should().BeGreaterThan(10);
    }

    [Fact]
    public void GameConfig_DungeonSettings_AreReasonable()
    {
        GameConfig.MaxLevels.Should().BeGreaterThan(10);
        GameConfig.MaxMonsters.Should().BeGreaterThan(10);
    }

    [Fact]
    public void GameConfig_SpellSettings_AreReasonable()
    {
        GameConfig.MaxSpells.Should().BeGreaterThan(10);
        GameConfig.MaxClasses.Should().BeGreaterThan(5);
        GameConfig.MaxRaces.Should().BeGreaterThan(5);
    }

    [Fact]
    public void GameConfig_DailyLimits_ArePositive()
    {
        GameConfig.DefaultGymSessions.Should().BeGreaterThan(0);
        GameConfig.DefaultDrinksAtOrbs.Should().BeGreaterThan(0);
        GameConfig.DefaultIntimacyActs.Should().BeGreaterThan(0);
        GameConfig.DefaultPickPocketAttempts.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GameConfig_TurnsPerDay_IsReasonable()
    {
        GameConfig.TurnsPerDay.Should().BeGreaterThan(100);
    }

    [Fact]
    public void GameConfig_MaxChildren_IsPositive()
    {
        GameConfig.MaxChildren.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GameConfig_DataPath_IsSet()
    {
        GameConfig.DataPath.Should().NotBeNullOrEmpty();
    }
}
