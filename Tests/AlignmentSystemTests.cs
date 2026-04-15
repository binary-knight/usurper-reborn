using Xunit;
using FluentAssertions;
using UsurperRemake.Systems;

namespace UsurperReborn.Tests;

/// <summary>
/// Unit tests for AlignmentSystem
/// Tests alignment calculations, price modifiers, and combat bonuses
/// </summary>
public class AlignmentSystemTests
{
    private readonly AlignmentSystem _alignmentSystem = AlignmentSystem.Instance;

    #region Alignment Type Tests

    [Fact]
    public void GetAlignment_Holy_WhenHighChivalryLowDarkness()
    {
        var character = new Character
        {
            Chivalry = 800,
            Darkness = 50
        };

        var alignment = _alignmentSystem.GetAlignment(character);

        alignment.Should().Be(AlignmentSystem.AlignmentType.Holy);
    }

    [Fact]
    public void GetAlignment_Evil_WhenHighDarknessLowChivalry()
    {
        var character = new Character
        {
            Chivalry = 50,
            Darkness = 800
        };

        var alignment = _alignmentSystem.GetAlignment(character);

        alignment.Should().Be(AlignmentSystem.AlignmentType.Evil);
    }

    [Fact]
    public void GetAlignment_Good_WhenChivalryExceedsDarknessByMuch()
    {
        var character = new Character
        {
            Chivalry = 600,
            Darkness = 100
        };

        var alignment = _alignmentSystem.GetAlignment(character);

        alignment.Should().Be(AlignmentSystem.AlignmentType.Good);
    }

    [Fact]
    public void GetAlignment_Dark_WhenDarknessExceedsChivalryByMuch()
    {
        var character = new Character
        {
            Chivalry = 100,
            Darkness = 600
        };

        var alignment = _alignmentSystem.GetAlignment(character);

        alignment.Should().Be(AlignmentSystem.AlignmentType.Dark);
    }

    [Fact]
    public void GetAlignment_Balanced_WhenBothAreHighAndCloseTogether()
    {
        // v0.57.0: 300/300 now maps to "Balanced" (both > 100, diff < 100) — the explicit
        // neutral-between-light-and-dark state that unlocks access to dialogue on both sides.
        var character = new Character
        {
            Chivalry = 300,
            Darkness = 300
        };

        var alignment = _alignmentSystem.GetAlignment(character);

        alignment.Should().Be(AlignmentSystem.AlignmentType.Balanced);
    }

    [Fact]
    public void GetAlignment_Neutral_WhenBothAreLow()
    {
        var character = new Character
        {
            Chivalry = 50,
            Darkness = 50
        };

        var alignment = _alignmentSystem.GetAlignment(character);

        alignment.Should().Be(AlignmentSystem.AlignmentType.Neutral);
    }

    [Theory]
    [InlineData(800, 99, AlignmentSystem.AlignmentType.Holy)]   // Just under darkness threshold for Holy
    [InlineData(800, 100, AlignmentSystem.AlignmentType.Good)]  // At darkness threshold, falls to Good
    [InlineData(99, 800, AlignmentSystem.AlignmentType.Evil)]   // Just under chivalry threshold for Evil
    [InlineData(100, 800, AlignmentSystem.AlignmentType.Dark)]  // At chivalry threshold, falls to Dark
    public void GetAlignment_BoundaryConditions(long chivalry, long darkness, AlignmentSystem.AlignmentType expected)
    {
        var character = new Character
        {
            Chivalry = chivalry,
            Darkness = darkness
        };

        var alignment = _alignmentSystem.GetAlignment(character);

        alignment.Should().Be(expected);
    }

    #endregion

    #region Alignment Display Tests

    [Theory]
    [InlineData(AlignmentSystem.AlignmentType.Holy, "Holy", "bright_yellow")]
    [InlineData(AlignmentSystem.AlignmentType.Good, "Good", "bright_green")]
    [InlineData(AlignmentSystem.AlignmentType.Neutral, "Neutral", "gray")]
    [InlineData(AlignmentSystem.AlignmentType.Dark, "Dark", "red")]
    [InlineData(AlignmentSystem.AlignmentType.Evil, "Evil", "bright_red")]
    [InlineData(AlignmentSystem.AlignmentType.Balanced, "Balanced", "bright_magenta")]
    public void GetAlignmentDisplay_ReturnsCorrectTextAndColor(
        AlignmentSystem.AlignmentType targetAlignment,
        string expectedText,
        string expectedColor)
    {
        // Create character with appropriate alignment
        var character = CreateCharacterWithAlignment(targetAlignment);

        var (text, color) = _alignmentSystem.GetAlignmentDisplay(character);

        text.Should().Be(expectedText);
        color.Should().Be(expectedColor);
    }

    #endregion

    #region Price Modifier Tests - Legitimate Shops

    [Theory]
    [InlineData(AlignmentSystem.AlignmentType.Holy, 0.8f)]
    [InlineData(AlignmentSystem.AlignmentType.Good, 0.9f)]
    [InlineData(AlignmentSystem.AlignmentType.Neutral, 1.0f)]
    [InlineData(AlignmentSystem.AlignmentType.Balanced, 0.95f)]
    [InlineData(AlignmentSystem.AlignmentType.Dark, 1.15f)]
    [InlineData(AlignmentSystem.AlignmentType.Evil, 1.4f)]
    public void GetPriceModifier_LegitimateShop_ReturnsCorrectValue(
        AlignmentSystem.AlignmentType alignment,
        float expectedModifier)
    {
        var character = CreateCharacterWithAlignment(alignment);

        var modifier = _alignmentSystem.GetPriceModifier(character, isShadyShop: false);

        modifier.Should().Be(expectedModifier);
    }

    [Fact]
    public void GetPriceModifier_LegitimateShop_HolyGetsBestPrice()
    {
        var holyChar = CreateCharacterWithAlignment(AlignmentSystem.AlignmentType.Holy);
        var evilChar = CreateCharacterWithAlignment(AlignmentSystem.AlignmentType.Evil);

        var holyPrice = _alignmentSystem.GetPriceModifier(holyChar, isShadyShop: false);
        var evilPrice = _alignmentSystem.GetPriceModifier(evilChar, isShadyShop: false);

        holyPrice.Should().BeLessThan(evilPrice);
    }

    #endregion

    #region Price Modifier Tests - Shady Shops

    [Theory]
    [InlineData(AlignmentSystem.AlignmentType.Holy, 1.5f)]
    [InlineData(AlignmentSystem.AlignmentType.Good, 1.25f)]
    [InlineData(AlignmentSystem.AlignmentType.Neutral, 1.0f)]
    [InlineData(AlignmentSystem.AlignmentType.Balanced, 0.95f)]
    [InlineData(AlignmentSystem.AlignmentType.Dark, 0.9f)]
    [InlineData(AlignmentSystem.AlignmentType.Evil, 0.75f)]
    public void GetPriceModifier_ShadyShop_ReturnsCorrectValue(
        AlignmentSystem.AlignmentType alignment,
        float expectedModifier)
    {
        var character = CreateCharacterWithAlignment(alignment);

        var modifier = _alignmentSystem.GetPriceModifier(character, isShadyShop: true);

        modifier.Should().Be(expectedModifier);
    }

    [Fact]
    public void GetPriceModifier_ShadyShop_EvilGetsBestPrice()
    {
        var holyChar = CreateCharacterWithAlignment(AlignmentSystem.AlignmentType.Holy);
        var evilChar = CreateCharacterWithAlignment(AlignmentSystem.AlignmentType.Evil);

        var holyPrice = _alignmentSystem.GetPriceModifier(holyChar, isShadyShop: true);
        var evilPrice = _alignmentSystem.GetPriceModifier(evilChar, isShadyShop: true);

        evilPrice.Should().BeLessThan(holyPrice);
    }

    #endregion

    #region Combat Modifier Tests

    [Theory]
    [InlineData(AlignmentSystem.AlignmentType.Holy, 1.0f, 1.1f)]
    [InlineData(AlignmentSystem.AlignmentType.Good, 1.05f, 1.05f)]
    [InlineData(AlignmentSystem.AlignmentType.Neutral, 1.0f, 1.0f)]
    [InlineData(AlignmentSystem.AlignmentType.Balanced, 1.05f, 1.05f)]
    [InlineData(AlignmentSystem.AlignmentType.Dark, 1.1f, 0.95f)]
    [InlineData(AlignmentSystem.AlignmentType.Evil, 1.2f, 0.9f)]
    public void GetCombatModifiers_ReturnsCorrectValues(
        AlignmentSystem.AlignmentType alignment,
        float expectedAttack,
        float expectedDefense)
    {
        var character = CreateCharacterWithAlignment(alignment);

        var (attackMod, defenseMod) = _alignmentSystem.GetCombatModifiers(character);

        attackMod.Should().Be(expectedAttack);
        defenseMod.Should().Be(expectedDefense);
    }

    [Fact]
    public void GetCombatModifiers_Evil_HighAttackLowDefense()
    {
        var evilChar = CreateCharacterWithAlignment(AlignmentSystem.AlignmentType.Evil);

        var (attackMod, defenseMod) = _alignmentSystem.GetCombatModifiers(evilChar);

        attackMod.Should().BeGreaterThan(1.0f); // Evil has attack bonus
        defenseMod.Should().BeLessThan(1.0f);   // Evil has defense penalty
    }

    [Fact]
    public void GetCombatModifiers_Holy_DefenseBonus()
    {
        var holyChar = CreateCharacterWithAlignment(AlignmentSystem.AlignmentType.Holy);

        var (attackMod, defenseMod) = _alignmentSystem.GetCombatModifiers(holyChar);

        defenseMod.Should().BeGreaterThan(1.0f); // Holy has defense bonus
    }

    #endregion

    #region Alignment Abilities Tests

    [Fact]
    public void GetAlignmentAbilities_ReturnsAbilitiesForEachAlignment()
    {
        foreach (AlignmentSystem.AlignmentType alignmentType in Enum.GetValues(typeof(AlignmentSystem.AlignmentType)))
        {
            var character = CreateCharacterWithAlignment(alignmentType);
            var abilities = _alignmentSystem.GetAlignmentAbilities(character);

            abilities.Should().NotBeNull();
            abilities.Should().NotBeEmpty();
        }
    }

    [Fact]
    public void GetAlignmentAbilities_Holy_HasMostAbilities()
    {
        var holyChar = CreateCharacterWithAlignment(AlignmentSystem.AlignmentType.Holy);
        var neutralChar = CreateCharacterWithAlignment(AlignmentSystem.AlignmentType.Neutral);

        var holyAbilities = _alignmentSystem.GetAlignmentAbilities(holyChar);
        var neutralAbilities = _alignmentSystem.GetAlignmentAbilities(neutralChar);

        holyAbilities.Count.Should().BeGreaterThan(neutralAbilities.Count);
    }

    [Fact]
    public void GetAlignmentAbilities_Evil_HasMostAbilities()
    {
        var evilChar = CreateCharacterWithAlignment(AlignmentSystem.AlignmentType.Evil);
        var neutralChar = CreateCharacterWithAlignment(AlignmentSystem.AlignmentType.Neutral);

        var evilAbilities = _alignmentSystem.GetAlignmentAbilities(evilChar);
        var neutralAbilities = _alignmentSystem.GetAlignmentAbilities(neutralChar);

        evilAbilities.Count.Should().BeGreaterThan(neutralAbilities.Count);
    }

    #endregion

    #region Modify Alignment Tests

    [Fact]
    public void ModifyAlignment_IncreasesChivalry()
    {
        var character = new Character
        {
            Chivalry = 500,
            Darkness = 100,
            Name2 = "TestPlayer"
        };

        _alignmentSystem.ModifyAlignment(character, chivalryChange: 50, darknessChange: 0, "test");

        character.Chivalry.Should().Be(550);
        character.Darkness.Should().Be(100); // Unchanged
    }

    [Fact]
    public void ModifyAlignment_IncreasesDarkness()
    {
        var character = new Character
        {
            Chivalry = 100,
            Darkness = 500,
            Name2 = "TestPlayer"
        };

        _alignmentSystem.ModifyAlignment(character, chivalryChange: 0, darknessChange: 50, "test");

        character.Chivalry.Should().Be(100); // Unchanged
        character.Darkness.Should().Be(550);
    }

    [Fact]
    public void ModifyAlignment_ClampsToMaximum()
    {
        var character = new Character
        {
            Chivalry = 990,
            Darkness = 990,
            Name2 = "TestPlayer"
        };

        _alignmentSystem.ModifyAlignment(character, chivalryChange: 100, darknessChange: 100, "test");

        character.Chivalry.Should().Be(1000); // Clamped to max
        character.Darkness.Should().Be(1000); // Clamped to max
    }

    [Fact]
    public void ModifyAlignment_ClampsToMinimum()
    {
        var character = new Character
        {
            Chivalry = 10,
            Darkness = 10,
            Name2 = "TestPlayer"
        };

        _alignmentSystem.ModifyAlignment(character, chivalryChange: -100, darknessChange: -100, "test");

        character.Chivalry.Should().Be(0); // Clamped to min
        character.Darkness.Should().Be(0); // Clamped to min
    }

    #endregion

    #region Helper Methods

    private static Character CreateCharacterWithAlignment(AlignmentSystem.AlignmentType alignment)
    {
        return alignment switch
        {
            AlignmentSystem.AlignmentType.Holy => new Character { Chivalry = 850, Darkness = 50 },
            AlignmentSystem.AlignmentType.Good => new Character { Chivalry = 600, Darkness = 100 },
            // v0.57.0: Neutral now means both scales low — 300/300 became "Balanced" under the new paired-movement rules.
            AlignmentSystem.AlignmentType.Neutral => new Character { Chivalry = 50, Darkness = 50 },
            AlignmentSystem.AlignmentType.Balanced => new Character { Chivalry = 300, Darkness = 300 },
            AlignmentSystem.AlignmentType.Dark => new Character { Chivalry = 100, Darkness = 600 },
            AlignmentSystem.AlignmentType.Evil => new Character { Chivalry = 50, Darkness = 850 },
            _ => new Character { Chivalry = 50, Darkness = 50 }
        };
    }

    #endregion
}
