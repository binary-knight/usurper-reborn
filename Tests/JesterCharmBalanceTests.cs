using System;
using FluentAssertions;
using Xunit;

namespace UsurperRemake.Tests
{
    /// <summary>
    /// v1.0.2 Charming Performance rebalance.
    ///
    /// Reported by a Lv.26 Jester: "What on earth is the deal with Charming
    /// Performance? 50% chance at a 40% stun?"
    ///
    /// The compounding read was correct. The ability rolled 40% to apply, then
    /// 50% to skip, and resolved exactly once -- a 20% chance to skip a single
    /// attack, for 35 stamina and a 4-round cooldown, dealing no damage. Pratfall
    /// unlocks EIGHT LEVELS EARLIER for half the stamina and half the cooldown,
    /// deals damage, and stuns at 60%. The level 26 ability was strictly
    /// dominated by the level 18 one.
    ///
    /// The root cause was that `Duration = 3` had sat in the ability data since
    /// it was written and nothing ever read it: `Charmed` was a plain bool that
    /// got cleared on the target's very next turn whether the skip fired or not.
    /// </summary>
    public class JesterCharmBalanceTests
    {
        private static ClassAbilitySystem.ClassAbility Charm() =>
            ClassAbilitySystem.GetAbility("charm")!;

        private static ClassAbilitySystem.ClassAbility Pratfall() =>
            ClassAbilitySystem.GetAbility("pratfall")!;

        /// <summary>Expected enemy attacks skipped by one cast.</summary>
        private static double ExpectedSkips(int applyPct, int skipPct, int rounds) =>
            (applyPct / 100.0) * rounds * (skipPct / 100.0);

        [Fact]
        public void TheReportedMath_WasReallyThatBad()
        {
            // The old numbers, stated plainly. One cast bought a 20% chance at a
            // single skipped attack.
            ExpectedSkips(40, 50, 1).Should().BeApproximately(0.20, 0.001);
        }

        [Fact]
        public void OneCastIsNowWorthRoughlyOneSkippedAttack()
        {
            double now = ExpectedSkips(
                GameConfig.CharmApplyChance,
                GameConfig.CharmSkipAttackChance,
                GameConfig.CharmDurationRounds);

            now.Should().BeGreaterThan(ExpectedSkips(40, 50, 1) * 4,
                "the rebalance must be a step change, not a nudge");
            now.Should().BeInRange(0.9, 1.3,
                "a no-damage control ability on a 4-round cooldown should buy about one skipped attack");
        }

        [Fact]
        public void CharmIsNoLongerDominatedByAnAbilityEightLevelsCheaper()
        {
            var charm = Charm();
            var pratfall = Pratfall();

            // Establish the premise of the complaint is real: Charm costs more
            // on every axis and deals no damage.
            charm.LevelRequired.Should().BeGreaterThan(pratfall.LevelRequired);
            charm.StaminaCost.Should().BeGreaterThan(pratfall.StaminaCost);
            charm.Cooldown.Should().BeGreaterThan(pratfall.Cooldown);
            charm.BaseDamage.Should().Be(0);

            // Given all that, its control output must clearly beat Pratfall's,
            // which is a 60% chance at a single stunned round.
            double charmSkips = ExpectedSkips(
                GameConfig.CharmApplyChance,
                GameConfig.CharmSkipAttackChance,
                GameConfig.CharmDurationRounds);
            double pratfallSkips = ExpectedSkips(60, 100, 1);

            charmSkips.Should().BeGreaterThan(pratfallSkips,
                "paying more stamina, more cooldown and all of the damage must buy better control");
        }

        [Fact]
        public void DurationIsDeclaredOnTheAbility_NotJustHardcoded()
        {
            // The bug was that this value existed and was ignored. If someone
            // later removes it, the fallback still applies a real duration.
            Charm().Duration.Should().BeGreaterThan(1,
                "charm is a sustained control effect, not a one-shot coin flip");

            // The handler prefers the ability's own Duration and only falls back
            // to the constant. If those two drift apart, the description test
            // below would still pass while the game did something else.
            Charm().Duration.Should().Be(GameConfig.CharmDurationRounds,
                "the ability's duration and the tuning constant must not diverge");
        }

        [Fact]
        public void CharmedRoundsTicksDownAndClears()
        {
            // Mirrors ProcessMonsterAction's consumption: skip is rolled, then
            // the duration ticks regardless of the outcome.
            var m = new Monster { Charmed = true, CharmedRounds = GameConfig.CharmDurationRounds };

            for (int i = GameConfig.CharmDurationRounds; i > 0; i--)
            {
                m.Charmed.Should().BeTrue($"charm must still be active with {i} rounds left");
                m.CharmedRounds--;
                if (m.CharmedRounds <= 0) m.Charmed = false;
            }

            m.Charmed.Should().BeFalse("charm must expire exactly when its rounds run out");
            m.CharmedRounds.Should().Be(0);
        }

        [Fact]
        public void ZeroRoundsPreservesTheLegacyOneShotPath()
        {
            // Dominate sets Charmed directly with no rounds. That must keep
            // clearing on first contact rather than becoming permanent.
            var m = new Monster { Charmed = true, CharmedRounds = 0 };

            if (m.CharmedRounds > 0)
            {
                m.CharmedRounds--;
                if (m.CharmedRounds <= 0) m.Charmed = false;
            }
            else
            {
                m.Charmed = false;
            }

            m.Charmed.Should().BeFalse("a charm with no duration must not survive its first tick");
        }

        /// <summary>
        /// Mirrors TryCharmMonster's guards. A combat review caught that the
        /// original rebalance shipped charm with none of the boss protections
        /// stuns go through: no resist roll, no duration cap, and re-casting
        /// simply overwrote the clock. At 3 rounds of 50%-per-round denial on a
        /// 4-round cooldown that is ~75% uptime, which let a level 26 ability
        /// soft-lock an Old God.
        /// </summary>
        private static bool TryCharm(Monster t, int requested, Func<int, int> roll)
        {
            if (t == null || !t.IsAlive) return false;
            if (t.Charmed) return false;

            int duration = Math.Max(1, requested);
            if (t.IsBoss || t.IsMiniBoss)
            {
                if (roll(100) < (int)(GameConfig.BossStunResistChance * 100)) return false;
                duration = Math.Min(duration, GameConfig.MaxStunDurationBoss);
            }

            t.Charmed = true;
            t.CharmedRounds = duration;
            return true;
        }

        [Fact]
        public void RecastingCannotRefreshAnActiveCharm()
        {
            // Refresh-stacking is what TryStunMonster calls "the primary
            // perma-stun path". Charm must not reopen it.
            var m = new Monster { HP = 100, MaxHP = 100, Charmed = true, CharmedRounds = 1 };

            TryCharm(m, 3, _ => 99).Should().BeFalse("an already-charmed target must reject a new charm");
            m.CharmedRounds.Should().Be(1, "the existing clock must not be extended");
        }

        [Fact]
        public void BossesGetAResistRollAndADurationCap()
        {
            var boss = new Monster { HP = 100, MaxHP = 100, IsBoss = true };

            // Roll below the resist threshold: the boss shrugs it off entirely.
            TryCharm(boss, 3, _ => 0).Should().BeFalse("bosses must be able to resist charm outright");
            boss.Charmed.Should().BeFalse();

            // Roll above it: charm lands, but capped to the boss stun ceiling.
            TryCharm(boss, 3, _ => 99).Should().BeTrue();
            boss.CharmedRounds.Should().Be(GameConfig.MaxStunDurationBoss,
                "boss charm must obey the same duration cap as boss stun");
            boss.CharmedRounds.Should().BeLessThan(GameConfig.CharmDurationRounds,
                "the cap must actually bite, otherwise it is decoration");
        }

        [Fact]
        public void MiniBossesAreProtectedToo()
        {
            var mini = new Monster { HP = 100, MaxHP = 100, IsMiniBoss = true };
            TryCharm(mini, 3, _ => 0).Should().BeFalse("mini-bosses share the boss resist path");
        }

        [Fact]
        public void OrdinaryMonstersGetTheFullDuration()
        {
            var m = new Monster { HP = 100, MaxHP = 100 };
            TryCharm(m, GameConfig.CharmDurationRounds, _ => 0).Should().BeTrue(
                "ordinary monsters have no resist roll");
            m.CharmedRounds.Should().Be(GameConfig.CharmDurationRounds);
        }

        [Fact]
        public void ADeadMonsterCannotBeCharmed()
        {
            var dead = new Monster { HP = 0, MaxHP = 100 };
            TryCharm(dead, 3, _ => 99).Should().BeFalse();
            dead.Charmed.Should().BeFalse();
        }

        [Fact]
        public void BossUptimeStaysWellUnderALock()
        {
            // The number that mattered: with the cap, a boss loses at most this
            // fraction of its turns to one cast on a 4-round cooldown.
            double bossSkips = (GameConfig.CharmApplyChance / 100.0)
                             * GameConfig.MaxStunDurationBoss
                             * (GameConfig.CharmSkipAttackChance / 100.0);
            int cooldown = Charm().Cooldown;

            (bossSkips / cooldown).Should().BeLessThan(0.20,
                "a level 26 ability must not deny a boss anything close to its turns");
        }

        [Fact]
        public void FreshMonstersAreNeverBornCharmed()
        {
            var m = new Monster();
            m.Charmed.Should().BeFalse();
            m.CharmedRounds.Should().Be(0, "charm must not leak into a newly generated monster");
        }

        [Fact]
        public void TuningConstantsStayInLegalRanges()
        {
            GameConfig.CharmApplyChance.Should().BeInRange(1, 100);
            GameConfig.CharmSkipAttackChance.Should().BeInRange(1, 100);
            GameConfig.CharmDurationRounds.Should().BeGreaterThan(0);

            GameConfig.CharmApplyChance.Should().BeLessThan(100,
                "a resist outcome must remain possible so the resist message is reachable");
            GameConfig.CharmSkipAttackChance.Should().BeLessThan(100,
                "charm is a soft control; a hard lock belongs to stun");
        }

        [Fact]
        public void DescriptionMatchesTheImplementedNumbers()
        {
            // The old description described the compounding without ever stating
            // the net, which is what left the player doing the multiplication
            // himself and concluding the ability was broken. It was.
            var desc = Charm().Description;
            desc.Should().Contain(GameConfig.CharmApplyChance.ToString());
            desc.Should().Contain(GameConfig.CharmSkipAttackChance.ToString());
            desc.Should().Contain(GameConfig.CharmDurationRounds.ToString());
        }

        [Fact]
        public void JestersFillerStillBeatsABasicAttack()
        {
            // The same report called Juggler's Trick useless. It is not: it is the
            // stamina-efficient, zero-cooldown filler. Pin that it out-damages
            // Pratfall per stamina point, which is the reason to press it.
            var juggler = ClassAbilitySystem.GetAbility("jugglers_trick")!;
            var pratfall = Pratfall();

            juggler.Cooldown.Should().Be(0, "the filler's identity is that it is always available");

            double jugglerPerStamina = (double)juggler.BaseDamage / juggler.StaminaCost;
            double pratfallPerStamina = (double)pratfall.BaseDamage / pratfall.StaminaCost;

            jugglerPerStamina.Should().BeGreaterThan(pratfallPerStamina,
                "Juggler's Trick trades control for stamina efficiency; if it loses that it has no role");
        }
    }
}
