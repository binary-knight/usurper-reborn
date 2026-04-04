using System;

namespace UsurperRemake.Data
{
    /// <summary>
    /// Balance-tunable game constants loaded from GameData/balance.json.
    /// Only includes values safe to modify — structural constants (MaxLevel, MaxClasses, etc.) are excluded.
    /// </summary>
    public class BalanceConfig
    {
        // Combat
        public int CriticalHitChance { get; set; } = GameConfig.CriticalHitChance;
        public float CriticalHitMultiplier { get; set; } = GameConfig.CriticalHitMultiplier;
        public float BackstabMultiplier { get; set; } = GameConfig.BackstabMultiplier;
        public float BerserkMultiplier { get; set; } = GameConfig.BerserkMultiplier;

        // Boss fights
        public int BossPotionCooldownRounds { get; set; } = GameConfig.BossPotionCooldownRounds;
        public double BossEnrageDamageMultiplier { get; set; } = GameConfig.BossEnrageDamageMultiplier;
        public double BossEnrageDefenseMultiplier { get; set; } = GameConfig.BossEnrageDefenseMultiplier;
        public int BossEnrageExtraAttacks { get; set; } = GameConfig.BossEnrageExtraAttacks;
        public int BossCorruptionDamageBase { get; set; } = GameConfig.BossCorruptionDamageBase;
        public int BossCorruptionMaxStacks { get; set; } = GameConfig.BossCorruptionMaxStacks;
        public int BossDoomDefaultRounds { get; set; } = GameConfig.BossDoomDefaultRounds;
        public double BossTankAoEAbsorption { get; set; } = GameConfig.BossTankAoEAbsorption;
        public double BossPhaseImmunityResidual { get; set; } = GameConfig.BossPhaseImmunityResidual;
        public int BossChannelInterruptSpeedThreshold { get; set; } = GameConfig.BossChannelInterruptSpeedThreshold;
        public double BossChannelInterruptChance { get; set; } = GameConfig.BossChannelInterruptChance;

        // Daily limits
        public int DefaultGymSessions { get; set; } = GameConfig.DefaultGymSessions;
        public int DefaultDrinksAtOrbs { get; set; } = GameConfig.DefaultDrinksAtOrbs;
        public int DefaultIntimacyActs { get; set; } = GameConfig.DefaultIntimacyActs;
        public int DefaultMaxWrestlings { get; set; } = GameConfig.DefaultMaxWrestlings;
        public int DefaultPickPocketAttempts { get; set; } = GameConfig.DefaultPickPocketAttempts;

        /// <summary>
        /// Apply balance values to GameConfig with validation/clamping.
        /// </summary>
        public void ApplyToGameConfig()
        {
            // Combat
            GameConfig.ModCriticalHitChance = Math.Clamp(CriticalHitChance, 0, 100);
            GameConfig.ModCriticalHitMultiplier = Math.Clamp(CriticalHitMultiplier, 1.0f, 10.0f);
            GameConfig.ModBackstabMultiplier = Math.Clamp(BackstabMultiplier, 1.0f, 10.0f);
            GameConfig.ModBerserkMultiplier = Math.Clamp(BerserkMultiplier, 1.0f, 10.0f);

            // Boss fights
            GameConfig.ModBossPotionCooldownRounds = Math.Clamp(BossPotionCooldownRounds, 0, 10);
            GameConfig.ModBossEnrageDamageMultiplier = Math.Clamp(BossEnrageDamageMultiplier, 1.0, 10.0);
            GameConfig.ModBossEnrageDefenseMultiplier = Math.Clamp(BossEnrageDefenseMultiplier, 1.0, 10.0);
            GameConfig.ModBossEnrageExtraAttacks = Math.Clamp(BossEnrageExtraAttacks, 0, 10);
            GameConfig.ModBossCorruptionDamageBase = Math.Clamp(BossCorruptionDamageBase, 1, 100);
            GameConfig.ModBossCorruptionMaxStacks = Math.Clamp(BossCorruptionMaxStacks, 1, 50);
            GameConfig.ModBossDoomDefaultRounds = Math.Clamp(BossDoomDefaultRounds, 1, 20);
            GameConfig.ModBossTankAoEAbsorption = Math.Clamp(BossTankAoEAbsorption, 0.0, 1.0);
            GameConfig.ModBossPhaseImmunityResidual = Math.Clamp(BossPhaseImmunityResidual, 0.0, 1.0);
            GameConfig.ModBossChannelInterruptSpeedThreshold = Math.Clamp(BossChannelInterruptSpeedThreshold, 0, 200);
            GameConfig.ModBossChannelInterruptChance = Math.Clamp(BossChannelInterruptChance, 0.0, 1.0);

            // Daily limits
            GameConfig.ModDefaultGymSessions = Math.Clamp(DefaultGymSessions, 1, 100);
            GameConfig.ModDefaultDrinksAtOrbs = Math.Clamp(DefaultDrinksAtOrbs, 1, 100);
            GameConfig.ModDefaultIntimacyActs = Math.Clamp(DefaultIntimacyActs, 1, 100);
            GameConfig.ModDefaultMaxWrestlings = Math.Clamp(DefaultMaxWrestlings, 1, 100);
            GameConfig.ModDefaultPickPocketAttempts = Math.Clamp(DefaultPickPocketAttempts, 1, 100);
        }
    }
}
