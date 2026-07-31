using System;
using Xunit;
using UsurperRemake;

namespace UsurperRemake.Tests
{
    /// <summary>
    /// v0.65.8 release-prep math: the mid-game XP taper (R4) and the fallen
    /// legacy heirloom formula (R5). Pins the curve shape so future tuning is
    /// deliberate, not accidental.
    /// </summary>
    public class ReleasePrepV0658Tests
    {
        [Fact]
        public void EarlyGameXPMultiplier_EarlyBands_Unchanged()
        {
            Assert.Equal(3.0, GameConfig.GetEarlyGameXPMultiplier(1));
            Assert.Equal(3.0, GameConfig.GetEarlyGameXPMultiplier(5));
            Assert.Equal(2.0, GameConfig.GetEarlyGameXPMultiplier(6));
            Assert.Equal(2.0, GameConfig.GetEarlyGameXPMultiplier(10));
        }

        [Fact]
        public void EarlyGameXPMultiplier_TapersToOneAtForty()
        {
            // R4: the old curve went transparent at 21, exactly where the
            // ~650-fights-per-decade wall began. Now it tapers to 1.0 at 40.
            Assert.True(GameConfig.GetEarlyGameXPMultiplier(21) > 1.5);
            Assert.True(GameConfig.GetEarlyGameXPMultiplier(30) > 1.2);
            Assert.True(GameConfig.GetEarlyGameXPMultiplier(39) > 1.0);
            Assert.Equal(1.0, GameConfig.GetEarlyGameXPMultiplier(40));
            Assert.Equal(1.0, GameConfig.GetEarlyGameXPMultiplier(100));
        }

        [Fact]
        public void EarlyGameXPMultiplier_MonotonicDecreasing()
        {
            // Leveling must never make per-fight XP jump upward.
            double prev = double.MaxValue;
            for (int level = 1; level <= 60; level++)
            {
                double mult = GameConfig.GetEarlyGameXPMultiplier(level);
                Assert.True(mult <= prev,
                    $"Multiplier increased from {prev} to {mult} at level {level}");
                Assert.True(mult >= 1.0, $"Multiplier below 1.0 at level {level}");
                prev = mult;
            }
        }

        [Fact]
        public void FallenLegacyGold_ScalesWithLevelAndCaps()
        {
            Assert.Equal(0, GameConfig.GetFallenLegacyGold(0));
            Assert.Equal(GameConfig.FallenLegacyGoldPerLevel * 10, GameConfig.GetFallenLegacyGold(10));
            Assert.Equal(GameConfig.FallenLegacyGoldPerLevel * 30, GameConfig.GetFallenLegacyGold(30));
            // Cap: a Lv.100 death can't mint a fortune.
            Assert.Equal(GameConfig.FallenLegacyMaxGold, GameConfig.GetFallenLegacyGold(100));
            Assert.True(GameConfig.GetFallenLegacyGold(100) <= GameConfig.FallenLegacyMaxGold);
            // Negative levels are clamped to zero, never negative gold.
            Assert.Equal(0, GameConfig.GetFallenLegacyGold(-5));
        }
    }
}
