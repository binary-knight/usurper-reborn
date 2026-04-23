using Xunit;
using FluentAssertions;
using UsurperRemake.Systems;

namespace UsurperReborn.Tests;

/// <summary>
/// Unit tests for v0.57.11 DEX-scaling critical-hit-chance cap and the
/// Creator's Eye flat-bonus rebalance.
///
/// Design:
///   - Base formula 5 + DEX/10 + equipment crit bonus (unchanged)
///   - Cap scales from 50 to 75 based on DEX: cap = 50 + min(25, DEX/30)
///   - Creator's Eye applies a flat +10 (was a 1.5× pre-clamp multiplier)
///   - Creator's Eye doubles to +20 during the Manwe battle with the Void Key
///
/// These tests avoid touching ArtifactSystem (it's a singleton with loaded
/// state) and focus on the baseline DEX-cap scaling. An artifact integration
/// test would need a test fixture for ArtifactSystem.Instance, which isn't
/// currently set up — flagged as a follow-up.
/// </summary>
public class CriticalHitChanceTests
{
    [Fact]
    public void LowDex_ReturnsFloor()
    {
        // DEX 5, no equipment — 5 + 0 + 0 = 5, clamps at 5 (min floor)
        int crit = StatEffectsSystem.GetCriticalHitChance(5);
        crit.Should().Be(5);
    }

    [Fact]
    public void ModerateDex_ComputesBase()
    {
        // DEX 100, no equipment — 5 + 10 = 15, cap = 50 + 100/30 = 53, so 15
        int crit = StatEffectsSystem.GetCriticalHitChance(100);
        crit.Should().Be(15);
    }

    [Fact]
    public void HighDex_ApproachesBaselineCap()
    {
        // DEX 450, no equipment — 5 + 45 = 50, cap = 50 + min(25, 15) = 65, so 50
        int crit = StatEffectsSystem.GetCriticalHitChance(450);
        crit.Should().Be(50);
    }

    [Fact]
    public void VeryHighDex_UnlocksExtendedCap()
    {
        // DEX 600, no equipment — 5 + 60 = 65, cap = 50 + min(25, 20) = 70, so 65
        int crit = StatEffectsSystem.GetCriticalHitChance(600);
        crit.Should().Be(65);
    }

    [Fact]
    public void MaxDex_HitsHardCeiling()
    {
        // DEX 1000, no equipment — 5 + 100 = 105, cap = 50 + min(25, 33) = 75, so 75
        int crit = StatEffectsSystem.GetCriticalHitChance(1000);
        crit.Should().Be(75);
    }

    [Fact]
    public void MaxDex_WithEquipmentStaysAtCeiling()
    {
        // DEX 1000 + 30 equipment crit = 5 + 100 + 30 = 135, cap = 75, so 75
        int crit = StatEffectsSystem.GetCriticalHitChance(1000, 30);
        crit.Should().Be(75);
    }

    [Fact]
    public void LowDex_EquipmentBonus_RespectsBaselineCap()
    {
        // DEX 10 + 60 equipment crit = 5 + 1 + 60 = 66, cap = 50 + min(25, 0) = 50, so 50
        int crit = StatEffectsSystem.GetCriticalHitChance(10, 60);
        crit.Should().Be(50);
    }

    [Fact]
    public void CapScalesLinearlyBelow750()
    {
        // DEX 300 → cap = 50 + 10 = 60
        // Without enough base+equipment to hit the cap, result is base.
        int crit = StatEffectsSystem.GetCriticalHitChance(300);
        crit.Should().Be(35); // 5 + 30

        // DEX 300 + 40 equipment — 5 + 30 + 40 = 75, cap 60 → clamps to 60
        int critCapped = StatEffectsSystem.GetCriticalHitChance(300, 40);
        critCapped.Should().Be(60);
    }

    [Fact]
    public void AboveMaxDex_CapDoesNotExceedSeventyFive()
    {
        // DEX 10000 → 5 + 1000 = 1005, cap = 50 + min(25, 333) = 75
        int crit = StatEffectsSystem.GetCriticalHitChance(10000);
        crit.Should().Be(75);
    }

    [Fact]
    public void CreatorsEyeBonus_IsFlatAndReasonable()
    {
        // v0.57.11: artifact bonus is now a flat +10, not a 1.5× multiplier.
        GameConfig.CreatorsEyeCritBonus.Should().Be(10);
    }

    [Fact]
    public void CritDamageMultiplier_Unchanged()
    {
        // Sanity: v0.57.11 doesn't touch crit-damage multipliers, only crit chance.
        // Verify the damage formula still produces expected values.
        // DEX 10: 1.5 + 0 + 0 = 1.5
        var mult = StatEffectsSystem.GetCriticalDamageMultiplier(10);
        mult.Should().BeApproximately(1.5f, 0.001f);

        // DEX 100: 1.5 + 90 × 0.02 = 1.5 + 1.8 = 3.0 (hits cap)
        var multHigh = StatEffectsSystem.GetCriticalDamageMultiplier(100);
        multHigh.Should().BeApproximately(3.0f, 0.001f);
    }
}
