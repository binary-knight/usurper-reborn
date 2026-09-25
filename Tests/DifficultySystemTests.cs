using Xunit;
using FluentAssertions;

namespace UsurperReborn.Tests;

/// <summary>
/// Unit tests for DifficultySystem
/// Tests all difficulty multipliers and modifiers
/// v1.1.14: in the shared collection. These tests set the static DifficultySystem.CurrentDifficulty
/// (Nightmare among others), which combat reads (Last Stand, permadeath, damage); run in parallel they
/// could change the difficulty in the middle of another test's fight.
/// </summary>
[Collection("SharedGameSingletons")]
public class DifficultySystemTests
{
    #region Display Name Tests

    [Theory]
    [InlineData(DifficultyMode.Easy, "Easy")]
    [InlineData(DifficultyMode.Normal, "Normal")]
    [InlineData(DifficultyMode.Hard, "Hard")]
    [InlineData(DifficultyMode.Nightmare, "Nightmare")]
    public void GetDisplayName_ReturnsCorrectName(DifficultyMode mode, string expectedName)
    {
        var name = DifficultySystem.GetDisplayName(mode);
        name.Should().Be(expectedName);
    }

    [Fact]
    public void GetDescription_ReturnsNonEmptyStrings()
    {
        foreach (DifficultyMode mode in Enum.GetValues(typeof(DifficultyMode)))
        {
            var description = DifficultySystem.GetDescription(mode);
            description.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public void GetColor_ReturnsValidColors()
    {
        foreach (DifficultyMode mode in Enum.GetValues(typeof(DifficultyMode)))
        {
            var color = DifficultySystem.GetColor(mode);
            color.Should().NotBeNullOrEmpty();
        }
    }

    #endregion

    #region Experience Multiplier Tests

    [Theory]
    [InlineData(DifficultyMode.Easy, 1.5f)]
    [InlineData(DifficultyMode.Normal, 1.0f)]
    [InlineData(DifficultyMode.Hard, 0.75f)]
    [InlineData(DifficultyMode.Nightmare, 0.5f)]
    public void GetExperienceMultiplier_ReturnsCorrectValue(DifficultyMode mode, float expected)
    {
        var multiplier = DifficultySystem.GetExperienceMultiplier(mode);
        multiplier.Should().Be(expected);
    }

    [Fact]
    public void ApplyExperienceMultiplier_CalculatesCorrectly()
    {
        DifficultySystem.CurrentDifficulty = DifficultyMode.Easy;
        var result = DifficultySystem.ApplyExperienceMultiplier(100);
        result.Should().Be(150); // 100 * 1.5 = 150

        DifficultySystem.CurrentDifficulty = DifficultyMode.Hard;
        result = DifficultySystem.ApplyExperienceMultiplier(100);
        result.Should().Be(75); // 100 * 0.75 = 75

        // Reset to normal
        DifficultySystem.CurrentDifficulty = DifficultyMode.Normal;
    }

    #endregion

    #region Gold Multiplier Tests

    [Theory]
    [InlineData(DifficultyMode.Easy, 1.5f)]
    [InlineData(DifficultyMode.Normal, 1.0f)]
    [InlineData(DifficultyMode.Hard, 0.75f)]
    [InlineData(DifficultyMode.Nightmare, 0.5f)]
    public void GetGoldMultiplier_ReturnsCorrectValue(DifficultyMode mode, float expected)
    {
        var multiplier = DifficultySystem.GetGoldMultiplier(mode);
        multiplier.Should().Be(expected);
    }

    [Fact]
    public void ApplyGoldMultiplier_CalculatesCorrectly()
    {
        DifficultySystem.CurrentDifficulty = DifficultyMode.Easy;
        var result = DifficultySystem.ApplyGoldMultiplier(1000);
        result.Should().Be(1500);

        DifficultySystem.CurrentDifficulty = DifficultyMode.Nightmare;
        result = DifficultySystem.ApplyGoldMultiplier(1000);
        result.Should().Be(500);

        DifficultySystem.CurrentDifficulty = DifficultyMode.Normal;
    }

    #endregion

    #region Monster Damage Multiplier Tests

    [Theory]
    [InlineData(DifficultyMode.Easy, 0.75f)]
    [InlineData(DifficultyMode.Normal, 1.0f)]
    [InlineData(DifficultyMode.Hard, 1.25f)]
    [InlineData(DifficultyMode.Nightmare, 1.5f)]
    public void GetMonsterDamageMultiplier_ReturnsCorrectValue(DifficultyMode mode, float expected)
    {
        var multiplier = DifficultySystem.GetMonsterDamageMultiplier(mode);
        multiplier.Should().Be(expected);
    }

    [Fact]
    public void ApplyMonsterDamageMultiplier_CalculatesCorrectly()
    {
        DifficultySystem.CurrentDifficulty = DifficultyMode.Easy;
        var result = DifficultySystem.ApplyMonsterDamageMultiplier(100);
        result.Should().Be(75);

        DifficultySystem.CurrentDifficulty = DifficultyMode.Nightmare;
        result = DifficultySystem.ApplyMonsterDamageMultiplier(100);
        result.Should().Be(150);

        DifficultySystem.CurrentDifficulty = DifficultyMode.Normal;
    }

    #endregion

    #region Player Damage Multiplier Tests

    [Theory]
    [InlineData(DifficultyMode.Easy, 1.15f)]
    [InlineData(DifficultyMode.Normal, 1.0f)]
    [InlineData(DifficultyMode.Hard, 1.0f)]
    [InlineData(DifficultyMode.Nightmare, 0.9f)]
    public void GetPlayerDamageMultiplier_ReturnsCorrectValue(DifficultyMode mode, float expected)
    {
        var multiplier = DifficultySystem.GetPlayerDamageMultiplier(mode);
        multiplier.Should().Be(expected);
    }

    #endregion

    #region Can Flee Tests

    [Theory]
    [InlineData(DifficultyMode.Easy, true)]
    [InlineData(DifficultyMode.Normal, true)]
    [InlineData(DifficultyMode.Hard, true)]
    [InlineData(DifficultyMode.Nightmare, false)]
    public void CanFlee_ReturnsCorrectValue(DifficultyMode mode, bool expected)
    {
        var canFlee = DifficultySystem.CanFlee(mode);
        canFlee.Should().Be(expected);
    }

    #endregion

    #region Healing Multiplier Tests

    [Theory]
    [InlineData(DifficultyMode.Easy, 1.25f)]
    [InlineData(DifficultyMode.Normal, 1.0f)]
    [InlineData(DifficultyMode.Hard, 1.0f)]
    [InlineData(DifficultyMode.Nightmare, 0.75f)]
    public void GetHealingMultiplier_ReturnsCorrectValue(DifficultyMode mode, float expected)
    {
        var multiplier = DifficultySystem.GetHealingMultiplier(mode);
        multiplier.Should().Be(expected);
    }

    [Fact]
    public void ApplyHealingMultiplier_CalculatesCorrectly()
    {
        DifficultySystem.CurrentDifficulty = DifficultyMode.Easy;
        var result = DifficultySystem.ApplyHealingMultiplier(100);
        result.Should().Be(125);

        DifficultySystem.CurrentDifficulty = DifficultyMode.Nightmare;
        result = DifficultySystem.ApplyHealingMultiplier(100);
        result.Should().Be(75);

        DifficultySystem.CurrentDifficulty = DifficultyMode.Normal;
    }

    #endregion

    #region Shop Price Multiplier Tests

    [Theory]
    [InlineData(DifficultyMode.Easy, 0.85f)]
    [InlineData(DifficultyMode.Normal, 1.0f)]
    [InlineData(DifficultyMode.Hard, 1.15f)]
    [InlineData(DifficultyMode.Nightmare, 1.25f)]
    public void GetShopPriceMultiplier_ReturnsCorrectValue(DifficultyMode mode, float expected)
    {
        var multiplier = DifficultySystem.GetShopPriceMultiplier(mode);
        multiplier.Should().Be(expected);
    }

    [Fact]
    public void ApplyShopPriceMultiplier_CalculatesCorrectly()
    {
        DifficultySystem.CurrentDifficulty = DifficultyMode.Easy;
        var result = DifficultySystem.ApplyShopPriceMultiplier(1000);
        result.Should().Be(850);

        DifficultySystem.CurrentDifficulty = DifficultyMode.Nightmare;
        result = DifficultySystem.ApplyShopPriceMultiplier(1000);
        result.Should().Be(1250);

        DifficultySystem.CurrentDifficulty = DifficultyMode.Normal;
    }

    #endregion

    #region Affliction Duration Tests

    [Theory]
    [InlineData(DifficultyMode.Easy, 0.5f)]
    [InlineData(DifficultyMode.Normal, 1.0f)]
    [InlineData(DifficultyMode.Hard, 1.25f)]
    [InlineData(DifficultyMode.Nightmare, 1.5f)]
    public void GetAfflictionDurationMultiplier_ReturnsCorrectValue(DifficultyMode mode, float expected)
    {
        var multiplier = DifficultySystem.GetAfflictionDurationMultiplier(mode);
        multiplier.Should().Be(expected);
    }

    #endregion

    #region Death Penalty Tests

    [Theory]
    [InlineData(DifficultyMode.Easy, 0.5f)]
    [InlineData(DifficultyMode.Normal, 1.0f)]
    [InlineData(DifficultyMode.Hard, 1.5f)]
    [InlineData(DifficultyMode.Nightmare, 2.0f)]
    public void GetDeathPenaltyMultiplier_ReturnsCorrectValue(DifficultyMode mode, float expected)
    {
        var multiplier = DifficultySystem.GetDeathPenaltyMultiplier(mode);
        multiplier.Should().Be(expected);
    }

    #endregion

    #region Relationship Multiplier Tests

    [Theory]
    [InlineData(DifficultyMode.Easy, 1.5f)]
    [InlineData(DifficultyMode.Normal, 1.0f)]
    [InlineData(DifficultyMode.Hard, 0.75f)]
    [InlineData(DifficultyMode.Nightmare, 0.5f)]
    public void GetRelationshipGainMultiplier_ReturnsCorrectValue(DifficultyMode mode, float expected)
    {
        var multiplier = DifficultySystem.GetRelationshipGainMultiplier(mode);
        multiplier.Should().Be(expected);
    }

    [Fact]
    public void ApplyRelationshipMultiplier_ReturnsAtLeastOne()
    {
        DifficultySystem.CurrentDifficulty = DifficultyMode.Nightmare;
        var result = DifficultySystem.ApplyRelationshipMultiplier(1);
        result.Should().BeGreaterThanOrEqualTo(1);

        DifficultySystem.CurrentDifficulty = DifficultyMode.Normal;
    }

    #endregion

    #region Jealousy Decay Tests

    [Theory]
    [InlineData(DifficultyMode.Easy, 2.0f)]
    [InlineData(DifficultyMode.Normal, 1.0f)]
    [InlineData(DifficultyMode.Hard, 0.75f)]
    [InlineData(DifficultyMode.Nightmare, 0.5f)]
    public void GetJealousyDecayMultiplier_ReturnsCorrectValue(DifficultyMode mode, float expected)
    {
        var multiplier = DifficultySystem.GetJealousyDecayMultiplier(mode);
        multiplier.Should().Be(expected);
    }

    [Fact]
    public void ApplyJealousyDecayMultiplier_ReturnsAtLeastOne()
    {
        DifficultySystem.CurrentDifficulty = DifficultyMode.Nightmare;
        var result = DifficultySystem.ApplyJealousyDecayMultiplier(1);
        result.Should().BeGreaterThanOrEqualTo(1);

        DifficultySystem.CurrentDifficulty = DifficultyMode.Normal;
    }

    #endregion

    #region Companion Loyalty Tests

    [Theory]
    [InlineData(DifficultyMode.Easy, 1.5f)]
    [InlineData(DifficultyMode.Normal, 1.0f)]
    [InlineData(DifficultyMode.Hard, 0.75f)]
    [InlineData(DifficultyMode.Nightmare, 0.5f)]
    public void GetCompanionLoyaltyMultiplier_ReturnsCorrectValue(DifficultyMode mode, float expected)
    {
        var multiplier = DifficultySystem.GetCompanionLoyaltyMultiplier(mode);
        multiplier.Should().Be(expected);
    }

    [Fact]
    public void ApplyCompanionLoyaltyMultiplier_ReturnsAtLeastOne()
    {
        DifficultySystem.CurrentDifficulty = DifficultyMode.Nightmare;
        var result = DifficultySystem.ApplyCompanionLoyaltyMultiplier(1);
        result.Should().BeGreaterThanOrEqualTo(1);

        DifficultySystem.CurrentDifficulty = DifficultyMode.Normal;
    }

    #endregion

    #region Difficulty Scaling Tests

    [Fact]
    public void Easy_ProvidesTotalBenefitToPlayer()
    {
        // Easy mode should give player advantages in all areas
        DifficultySystem.GetExperienceMultiplier(DifficultyMode.Easy).Should().BeGreaterThan(1.0f);
        DifficultySystem.GetGoldMultiplier(DifficultyMode.Easy).Should().BeGreaterThan(1.0f);
        DifficultySystem.GetMonsterDamageMultiplier(DifficultyMode.Easy).Should().BeLessThan(1.0f);
        DifficultySystem.GetPlayerDamageMultiplier(DifficultyMode.Easy).Should().BeGreaterThan(1.0f);
        DifficultySystem.GetHealingMultiplier(DifficultyMode.Easy).Should().BeGreaterThan(1.0f);
        DifficultySystem.GetShopPriceMultiplier(DifficultyMode.Easy).Should().BeLessThan(1.0f);
        DifficultySystem.GetDeathPenaltyMultiplier(DifficultyMode.Easy).Should().BeLessThan(1.0f);
        DifficultySystem.CanFlee(DifficultyMode.Easy).Should().BeTrue();
    }

    [Fact]
    public void Nightmare_ProvidesTotalChallengeToPlayer()
    {
        // Nightmare mode should be punishing in all areas
        DifficultySystem.GetExperienceMultiplier(DifficultyMode.Nightmare).Should().BeLessThan(1.0f);
        DifficultySystem.GetGoldMultiplier(DifficultyMode.Nightmare).Should().BeLessThan(1.0f);
        DifficultySystem.GetMonsterDamageMultiplier(DifficultyMode.Nightmare).Should().BeGreaterThan(1.0f);
        DifficultySystem.GetPlayerDamageMultiplier(DifficultyMode.Nightmare).Should().BeLessThan(1.0f);
        DifficultySystem.GetHealingMultiplier(DifficultyMode.Nightmare).Should().BeLessThan(1.0f);
        DifficultySystem.GetShopPriceMultiplier(DifficultyMode.Nightmare).Should().BeGreaterThan(1.0f);
        DifficultySystem.GetDeathPenaltyMultiplier(DifficultyMode.Nightmare).Should().BeGreaterThan(1.0f);
        DifficultySystem.CanFlee(DifficultyMode.Nightmare).Should().BeFalse();
    }

    [Fact]
    public void Normal_IsBalanced()
    {
        // Normal mode should have all multipliers at 1.0
        DifficultySystem.GetExperienceMultiplier(DifficultyMode.Normal).Should().Be(1.0f);
        DifficultySystem.GetGoldMultiplier(DifficultyMode.Normal).Should().Be(1.0f);
        DifficultySystem.GetMonsterDamageMultiplier(DifficultyMode.Normal).Should().Be(1.0f);
        DifficultySystem.GetPlayerDamageMultiplier(DifficultyMode.Normal).Should().Be(1.0f);
        DifficultySystem.GetHealingMultiplier(DifficultyMode.Normal).Should().Be(1.0f);
        DifficultySystem.GetShopPriceMultiplier(DifficultyMode.Normal).Should().Be(1.0f);
        DifficultySystem.GetDeathPenaltyMultiplier(DifficultyMode.Normal).Should().Be(1.0f);
        DifficultySystem.CanFlee(DifficultyMode.Normal).Should().BeTrue();
    }

    #endregion
}
