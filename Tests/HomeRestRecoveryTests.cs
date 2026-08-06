using FluentAssertions;
using Xunit;

namespace UsurperRemake.Tests
{
    /// <summary>
    /// Regression tests for the v1.0.2 Home rest fix.
    ///
    /// Reported by a player: "Resting at home restores a % proportion of missing
    /// health rather than a % of your max health." Correct. The rest formula
    /// multiplied the SHORTFALL by the tier percentage, so every tier below the
    /// top was asymptotic -- each rest closed a fraction of the remaining gap
    /// and full health was unreachable no matter how many rests were spent.
    /// Tier 5 (100%) behaved correctly by coincidence, which hid the bug.
    /// </summary>
    public class HomeRestRecoveryTests
    {
        [Theory]
        [InlineData(0.25f, 250)]  // straw pallet
        [InlineData(0.40f, 400)]
        [InlineData(0.55f, 550)]
        [InlineData(0.70f, 700)]
        public void RecoversShareOfMaximum_NotOfMissing(float pct, long expected)
        {
            // 1000 max, at 100 HP: 900 missing. A percentage of MAX must not
            // vary with how hurt the player happens to be.
            GameConfig.GetRestRecoveryAmount(1000, 100, pct).Should().Be(expected);
        }

        [Fact]
        public void SameTierHealsTheSameAmountRegardlessOfCurrentHealth()
        {
            // The old formula gave 175 at 300hp and 237 at 50hp for the same
            // rest. A share of max is stable.
            long atLowHealth = GameConfig.GetRestRecoveryAmount(1000, 50, 0.25f);
            long atMidHealth = GameConfig.GetRestRecoveryAmount(1000, 300, 0.25f);
            atLowHealth.Should().Be(250);
            atMidHealth.Should().Be(250);
        }

        [Fact]
        public void FullHealthIsReachable_TheAsymptoteIsGone()
        {
            // Three rests at the worst tier must actually reach full from 30%.
            long max = 1000, hp = 300;
            for (int i = 0; i < 3; i++)
                hp += GameConfig.GetRestRecoveryAmount(max, hp, 0.25f);

            hp.Should().Be(max, "repeated rests must be able to reach full health");
        }

        [Fact]
        public void NeverReportsMoreThanWasActuallyMissing()
        {
            // At 950/1000 a 25% tier would nominally give 250; only 50 is real.
            GameConfig.GetRestRecoveryAmount(1000, 950, 0.25f).Should().Be(50);
        }

        [Fact]
        public void AtFullHealthRecoversNothing()
        {
            GameConfig.GetRestRecoveryAmount(1000, 1000, 1.00f).Should().Be(0);
            GameConfig.GetRestRecoveryAmount(1000, 1200, 0.25f).Should().Be(0, "overhealed state is not negative recovery");
        }

        [Fact]
        public void TopTierStillFullyHeals_BehaviourPreserved()
        {
            // Tier 5 was the one case the old formula got right; it must not regress.
            GameConfig.GetRestRecoveryAmount(1000, 1, 1.00f).Should().Be(999);
        }

        [Fact]
        public void BloodPricePenaltyScalesTheShare()
        {
            // Murder weight multiplies the tier percentage, so a 25% tier at the
            // heavy penalty restores 12.5% of max, not 12.5% of the shortfall.
            GameConfig.GetRestRecoveryAmount(1000, 0, 0.25f * 0.50f).Should().Be(125);
        }

        [Fact]
        public void DegenerateInputsAreSafe()
        {
            GameConfig.GetRestRecoveryAmount(0, 0, 0.25f).Should().Be(0);
            GameConfig.GetRestRecoveryAmount(1000, 500, 0f).Should().Be(0);
            GameConfig.GetRestRecoveryAmount(1000, 500, -1f).Should().Be(0);
        }

        [Fact]
        public void TierTableIsAscendingAndEndsAtFull()
        {
            var tiers = GameConfig.HomeRecoveryPercent;
            tiers.Should().HaveCount(6);
            tiers[0].Should().Be(0.25f);
            tiers[^1].Should().Be(1.00f);
            for (int i = 1; i < tiers.Length; i++)
                tiers[i].Should().BeGreaterThan(tiers[i - 1], "upgrading your home must always improve rest");
        }
    }
}
