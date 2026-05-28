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

    // v0.62.x Renown ladder: Good/Holy characters get a deeper honest-shop discount scaled by their
    // Renown tier (the test Good char is Chiv 600 = Paragon x0.90 -> 0.9*0.90=0.81; the Holy char is
    // Chiv 850 = Legend x0.80 -> 0.8*0.80=0.64). Dark/Evil/Neutral/Balanced honest prices are unchanged.
    [Theory]
    [InlineData(AlignmentSystem.AlignmentType.Holy, 0.64f)]
    [InlineData(AlignmentSystem.AlignmentType.Good, 0.81f)]
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

        modifier.Should().BeApproximately(expectedModifier, 0.001f);
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

    // v0.62.x Dread ladder: Dark/Evil characters get a deeper shady-shop discount scaled by their
    // Dread tier (the test Dark char is Dark 600 = Marauder x0.90 -> 0.9*0.90=0.81; the Evil char is
    // Dark 850 = Nightmare x0.80 -> 0.75*0.80=0.60). Holy/Good/Neutral/Balanced shady prices unchanged.
    [Theory]
    [InlineData(AlignmentSystem.AlignmentType.Holy, 1.5f)]
    [InlineData(AlignmentSystem.AlignmentType.Good, 1.25f)]
    [InlineData(AlignmentSystem.AlignmentType.Neutral, 1.0f)]
    [InlineData(AlignmentSystem.AlignmentType.Balanced, 0.95f)]
    [InlineData(AlignmentSystem.AlignmentType.Dark, 0.81f)]
    [InlineData(AlignmentSystem.AlignmentType.Evil, 0.6f)]
    public void GetPriceModifier_ShadyShop_ReturnsCorrectValue(
        AlignmentSystem.AlignmentType alignment,
        float expectedModifier)
    {
        var character = CreateCharacterWithAlignment(alignment);

        var modifier = _alignmentSystem.GetPriceModifier(character, isShadyShop: true);

        modifier.Should().BeApproximately(expectedModifier, 0.001f);
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

    #region Dread / Renown Notoriety Ladders (v0.62.x "Light and Dark" Phase 2)

    [Theory]
    [InlineData(0, AlignmentSystem.DreadTier.None)]
    [InlineData(249, AlignmentSystem.DreadTier.None)]
    [InlineData(250, AlignmentSystem.DreadTier.Cutthroat)]
    [InlineData(449, AlignmentSystem.DreadTier.Cutthroat)]
    [InlineData(450, AlignmentSystem.DreadTier.Marauder)]
    [InlineData(649, AlignmentSystem.DreadTier.Marauder)]
    [InlineData(650, AlignmentSystem.DreadTier.Terror)]
    [InlineData(799, AlignmentSystem.DreadTier.Terror)]
    [InlineData(800, AlignmentSystem.DreadTier.Nightmare)]
    [InlineData(1000, AlignmentSystem.DreadTier.Nightmare)]
    public void GetDreadTier_BreakpointsAreCorrect(long darkness, AlignmentSystem.DreadTier expected)
    {
        var character = new Character { Darkness = darkness, Chivalry = 0 };
        _alignmentSystem.GetDreadTier(character).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, AlignmentSystem.RenownTier.None)]
    [InlineData(249, AlignmentSystem.RenownTier.None)]
    [InlineData(250, AlignmentSystem.RenownTier.Defender)]
    [InlineData(449, AlignmentSystem.RenownTier.Defender)]
    [InlineData(450, AlignmentSystem.RenownTier.Paragon)]
    [InlineData(649, AlignmentSystem.RenownTier.Paragon)]
    [InlineData(650, AlignmentSystem.RenownTier.Hero)]
    [InlineData(799, AlignmentSystem.RenownTier.Hero)]
    [InlineData(800, AlignmentSystem.RenownTier.Legend)]
    [InlineData(1000, AlignmentSystem.RenownTier.Legend)]
    public void GetRenownTier_BreakpointsAreCorrect(long chivalry, AlignmentSystem.RenownTier expected)
    {
        var character = new Character { Chivalry = chivalry, Darkness = 0 };
        _alignmentSystem.GetRenownTier(character).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 1.0f)]      // None
    [InlineData(250, 0.95f)]   // Cutthroat
    [InlineData(450, 0.90f)]   // Marauder
    [InlineData(650, 0.85f)]   // Terror
    [InlineData(800, 0.80f)]   // Nightmare
    public void GetDreadPriceMultiplier_ScalesWithTier(long darkness, float expected)
    {
        var character = new Character { Darkness = darkness, Chivalry = 0 };
        _alignmentSystem.GetDreadPriceMultiplier(character).Should().BeApproximately(expected, 0.001f);
    }

    [Theory]
    [InlineData(0, 1.0f)]      // None
    [InlineData(250, 0.95f)]   // Defender
    [InlineData(450, 0.90f)]   // Paragon
    [InlineData(650, 0.85f)]   // Hero
    [InlineData(800, 0.80f)]   // Legend
    public void GetRenownPriceMultiplier_ScalesWithTier(long chivalry, float expected)
    {
        var character = new Character { Chivalry = chivalry, Darkness = 0 };
        _alignmentSystem.GetRenownPriceMultiplier(character).Should().BeApproximately(expected, 0.001f);
    }

    [Fact]
    public void GetNotorietyStandingLine_EvilPlayer_ShowsDreadStanding()
    {
        var character = new Character { Darkness = 850, Chivalry = 50 }; // Evil band, Nightmare
        _alignmentSystem.GetNotorietyStandingLine(character).Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetNotorietyStandingLine_HolyPlayer_ShowsRenownStanding()
    {
        var character = new Character { Chivalry = 850, Darkness = 50 }; // Holy band, Legend
        _alignmentSystem.GetNotorietyStandingLine(character).Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetNotorietyStandingLine_BalancedLineWalker_HasNoStanding()
    {
        // High Darkness AND high Chivalry within 100 of each other = Balanced band. The line-walker
        // must NOT collect a Dread standing even though raw Darkness would map to Nightmare -- the
        // pole gating in GetNotorietyStandingLine keys off the alignment band, not the raw scale.
        var character = new Character { Darkness = 850, Chivalry = 800 };
        _alignmentSystem.GetAlignment(character).Should().Be(AlignmentSystem.AlignmentType.Balanced);
        _alignmentSystem.GetNotorietyStandingLine(character).Should().BeEmpty();
    }

    [Fact]
    public void GetNotorietyStandingLine_NeutralPlayer_HasNoStanding()
    {
        var character = new Character { Darkness = 50, Chivalry = 50 }; // Neutral band
        _alignmentSystem.GetNotorietyStandingLine(character).Should().BeEmpty();
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

    #region v0.57.12 — Setter Clamp & HealOverflow

    [Fact]
    public void CharacterSetter_ClampsChivalryToCap()
    {
        var character = new Character();
        character.Chivalry = 99999;
        character.Chivalry.Should().Be(GameConfig.AlignmentCap);
    }

    [Fact]
    public void CharacterSetter_ClampsDarknessToCap()
    {
        var character = new Character();
        character.Darkness = 50000;
        character.Darkness.Should().Be(GameConfig.AlignmentCap);
    }

    [Fact]
    public void CharacterSetter_ClampsNegativeChivalryToZero()
    {
        var character = new Character { Chivalry = 500 };
        character.Chivalry = -100;
        character.Chivalry.Should().Be(0);
    }

    [Fact]
    public void CharacterSetter_ClampsNegativeDarknessToZero()
    {
        var character = new Character { Darkness = 500 };
        character.Darkness = -100;
        character.Darkness.Should().Be(0);
    }

    [Fact]
    public void CharacterSetter_PreservesInRangeValue()
    {
        var character = new Character();
        character.Chivalry = 750;
        character.Darkness = 250;
        character.Chivalry.Should().Be(750);
        character.Darkness.Should().Be(250);
    }

    [Fact]
    public void CharacterSetter_DirectPlusEqualsOverflowClamps()
    {
        // Simulates the pre-v0.57.12 bypass pattern (e.g. ChurchLocation donations using `+=`).
        // Even if callers bypass ChangeAlignment, the setter now catches overflow.
        var character = new Character { Chivalry = 950 };
        character.Chivalry += 200;
        character.Chivalry.Should().Be(GameConfig.AlignmentCap);
    }

    [Fact]
    public void HealOverflow_InRangeReturnsUnchanged()
    {
        var (chiv, dark) = AlignmentSystem.HealOverflow(500, 300);
        chiv.Should().Be(500);
        dark.Should().Be(300);
    }

    [Fact]
    public void HealOverflow_ExcessChivalryClampsAndReducesDarkness()
    {
        // 15,000 chivalry with 0 darkness (the reported bug scenario):
        // excess = 14,000, darkness should be reduced by 7,000 but floors at 0.
        var (chiv, dark) = AlignmentSystem.HealOverflow(15000, 0);
        chiv.Should().Be(GameConfig.AlignmentCap);
        dark.Should().Be(0);
    }

    [Fact]
    public void HealOverflow_ExcessChivalryReducesDarknessByHalfExcess()
    {
        // Chivalry 1500 (excess 500), Darkness 600 → darkness should become 600 - 250 = 350.
        var (chiv, dark) = AlignmentSystem.HealOverflow(1500, 600);
        chiv.Should().Be(GameConfig.AlignmentCap);
        dark.Should().Be(350);
    }

    [Fact]
    public void HealOverflow_ExcessDarknessClampsAndReducesChivalry()
    {
        var (chiv, dark) = AlignmentSystem.HealOverflow(600, 1400);
        dark.Should().Be(GameConfig.AlignmentCap);
        chiv.Should().Be(400); // 600 - (400/2) = 600 - 200 = 400
    }

    [Fact]
    public void HealOverflow_BothSidesOverflowClampsBoth()
    {
        var (chiv, dark) = AlignmentSystem.HealOverflow(5000, 3000);
        chiv.Should().Be(GameConfig.AlignmentCap);
        dark.Should().Be(GameConfig.AlignmentCap);
    }

    [Fact]
    public void HealOverflow_NegativeInputsClampToZero()
    {
        var (chiv, dark) = AlignmentSystem.HealOverflow(-50, -100);
        chiv.Should().Be(0);
        dark.Should().Be(0);
    }

    [Fact]
    public void HealOverflowChivalry_ReturnsHealedChivalry()
    {
        AlignmentSystem.HealOverflowChivalry(2000, 500).Should().Be(GameConfig.AlignmentCap);
    }

    [Fact]
    public void HealOverflowDarkness_ReturnsHealedDarkness()
    {
        AlignmentSystem.HealOverflowDarkness(500, 2000).Should().Be(GameConfig.AlignmentCap);
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
