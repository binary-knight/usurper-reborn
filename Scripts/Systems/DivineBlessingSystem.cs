using System;
using System.Collections.Generic;
using Godot;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Divine Blessing System - Provides gameplay benefits for worshipping gods
    /// Benefits scale with: god's power (Experience), god's nature (Goodness vs Darkness),
    /// and the player's sacrifices/devotion.
    ///
    /// GOOD GODS (high Goodness): Defensive bonuses, healing, protection
    /// DARK GODS (high Darkness): Offensive bonuses, critical hits, lifesteal
    /// BALANCED GODS: Mix of both, unique effects
    /// </summary>
    public class DivineBlessingSystem
    {
        private static DivineBlessingSystem? _fallbackInstance;
        public static DivineBlessingSystem Instance
        {
            get
            {
                var ctx = UsurperRemake.Server.SessionContext.Current;
                if (ctx != null) return ctx.DivineBlessing;
                return _fallbackInstance ??= new DivineBlessingSystem();
            }
        }

        // Track temporary blessings per player (from sacrifices/prayers)
        private Dictionary<string, TemporaryBlessing> temporaryBlessings = new();

        // Track daily prayer status
        private Dictionary<string, DateTime> lastPrayerTime = new();

        private Random random = new();

        /// <summary>
        /// Get the divine blessings for a character based on their worshipped god
        /// </summary>
        public DivineBlessing GetBlessings(Character character)
        {
            var blessing = new DivineBlessing();

            // Get the god system and check if player has a god
            var godSystem = GodSystemSingleton.Instance;
            string godName = godSystem.GetPlayerGod(character.Name2);

            if (string.IsNullOrEmpty(godName))
                return blessing; // No god = no blessings

            var god = godSystem.GetGod(godName);
            if (god == null || !god.IsActive())
                return blessing; // God doesn't exist or inactive

            // Calculate god's alignment (-1 to 1, where -1 is pure dark, 1 is pure good)
            float alignment = CalculateAlignment(god);

            // Calculate blessing power based on god's experience/level (0.1 to 1.0)
            float godPower = CalculateGodPower(god);

            // Base blessing strength (1-10% based on god power)
            float baseStrength = 0.01f + (godPower * 0.09f);

            // Apply alignment-based bonuses
            if (alignment > 0.3f) // Good-aligned god
            {
                // Defensive/healing bonuses
                blessing.DamageReduction = (int)(alignment * godPower * 15); // Up to 15% damage reduction
                blessing.HealingBonus = (int)(alignment * godPower * 20); // Up to 20% healing bonus
                blessing.UndeadDamageBonus = (int)(alignment * godPower * 25); // Up to 25% vs undead
                blessing.DivineSaveChance = (int)(alignment * godPower * 10); // Up to 10% chance to survive lethal hit
                blessing.BlessingType = BlessingType.Holy;
            }
            else if (alignment < -0.3f) // Dark-aligned god
            {
                // Offensive bonuses
                float darkPower = Math.Abs(alignment);
                blessing.CriticalHitBonus = (int)(darkPower * godPower * 15); // Up to 15% crit bonus
                blessing.LifestealPercent = (int)(darkPower * godPower * 10); // Up to 10% lifesteal
                blessing.DarkDamageBonus = (int)(darkPower * godPower * 20); // Up to 20% bonus dark damage
                blessing.FearAura = (int)(darkPower * godPower * 15); // Up to 15% chance to frighten
                blessing.BlessingType = BlessingType.Dark;
            }
            else // Balanced god
            {
                // Mixed bonuses (smaller but versatile)
                blessing.DamageReduction = (int)(godPower * 8); // Up to 8%
                blessing.CriticalHitBonus = (int)(godPower * 8); // Up to 8%
                blessing.AllStatsBonus = (int)(godPower * 3); // Up to +3 all stats
                blessing.LuckBonus = (int)(godPower * 10); // Up to +10% luck (rare drops, etc)
                blessing.BlessingType = BlessingType.Balanced;
            }

            // Apply god-specific bonuses based on god name
            ApplyGodSpecificBonuses(god, blessing, godPower);

            // Add temporary blessing bonuses (from recent sacrifices/prayers)
            if (temporaryBlessings.TryGetValue(character.Name2, out var tempBlessing))
            {
                if (tempBlessing.ExpiresAt > DateTime.Now)
                {
                    blessing.TemporaryDamageBonus += tempBlessing.DamageBonus;
                    blessing.TemporaryDefenseBonus += tempBlessing.DefenseBonus;
                    blessing.TemporaryXPBonus += tempBlessing.XPBonus;
                    blessing.HasTemporaryBlessing = true;
                    blessing.TemporaryBlessingName = tempBlessing.Name;
                    blessing.TemporaryBlessingExpires = tempBlessing.ExpiresAt;
                }
                else
                {
                    temporaryBlessings.Remove(character.Name2);
                }
            }

            blessing.GodName = godName;
            blessing.IsActive = true;

            return blessing;
        }

        /// <summary>
        /// Calculate god's alignment from -1 (pure dark) to +1 (pure good)
        /// </summary>
        private float CalculateAlignment(God god)
        {
            long total = god.Goodness + god.Darkness;
            if (total == 0) return 0f;

            // Returns value from -1 (all darkness) to +1 (all goodness)
            return (float)(god.Goodness - god.Darkness) / total;
        }

        /// <summary>
        /// Calculate god's power level (0.1 to 1.0 based on experience)
        /// </summary>
        private float CalculateGodPower(God god)
        {
            // God levels 1-9, experience ranges from 1 to millions
            // Use log scale for smoother progression
            float power = (float)Math.Log10(Math.Max(1, god.Experience)) / 7f; // Log10(10M) ≈ 7
            return Math.Clamp(power, 0.1f, 1.0f);
        }

        /// <summary>
        /// Apply bonuses specific to each pantheon god
        /// </summary>
        private void ApplyGodSpecificBonuses(God god, DivineBlessing blessing, float godPower)
        {
            switch (god.Name)
            {
                case "Solarius": // Light/Truth god
                    blessing.BlindImmunity = true;
                    blessing.UndeadDamageBonus += (int)(godPower * 15);
                    blessing.SpecialAbility = "Radiant Strike: Attacks occasionally burst with holy light";
                    break;

                case "Valorian": // War/Honor god
                    blessing.StrengthBonus = (int)(godPower * 5);
                    blessing.CriticalHitBonus += (int)(godPower * 10);
                    blessing.SpecialAbility = "Battle Fury: Damage increases as HP decreases";
                    break;

                case "Amara": // Love/Passion god
                    blessing.CharismaBonus = (int)(godPower * 5);
                    blessing.HealingBonus += (int)(godPower * 15);
                    blessing.SpecialAbility = "Lover's Protection: Bonus when fighting alongside allies";
                    break;

                case "Judicar": // Law/Justice god
                    blessing.DamageReduction += (int)(godPower * 10);
                    blessing.CounterattackChance = (int)(godPower * 15);
                    blessing.SpecialAbility = "Righteous Judgment: Bonus damage vs criminals";
                    break;

                case "Umbrath": // Shadow/Secrets god
                    blessing.CriticalHitBonus += (int)(godPower * 15);
                    blessing.EvadeChance = (int)(godPower * 10);
                    blessing.SpecialAbility = "Shadow Step: Chance to avoid first attack in combat";
                    break;

                case "Terran": // Earth/Endurance god
                    blessing.DamageReduction += (int)(godPower * 15);
                    blessing.MaxHPBonus = (int)(godPower * 50);
                    blessing.SpecialAbility = "Stone Skin: Occasional attacks deal reduced damage";
                    break;

                case "Mortis": // Death god
                    blessing.LifestealPercent += (int)(godPower * 15);
                    blessing.DivineSaveChance += (int)(godPower * 5); // Death respects its followers
                    blessing.SpecialAbility = "Death's Touch: Attacks can instantly kill weakened foes";
                    break;

                case "Arcanus": // Magic/Knowledge god
                    blessing.IntelligenceBonus = (int)(godPower * 5);
                    blessing.SpellPowerBonus = (int)(godPower * 20);
                    blessing.SpecialAbility = "Arcane Insight: Bonus XP from magical enemies";
                    break;

                case "Sylvana": // Nature/Wild god
                    blessing.HealingBonus += (int)(godPower * 10);
                    blessing.PoisonImmunity = true;
                    blessing.SpecialAbility = "Nature's Blessing: Regenerate HP over time";
                    break;

                case "Discordia": // Chaos/Change god
                    blessing.LuckBonus += (int)(godPower * 20);
                    blessing.CriticalHitBonus += (int)(godPower * 10);
                    blessing.SpecialAbility = "Chaos Surge: Random powerful effects in combat";
                    break;
            }
        }

        /// <summary>
        /// Grant a temporary blessing from a sacrifice
        /// </summary>
        public TemporaryBlessing GrantSacrificeBlessing(Character character, long sacrificeValue, string godName)
        {
            var godSystem = GodSystemSingleton.Instance;
            var god = godSystem.GetGod(godName);

            if (god == null) return null;

            float alignment = CalculateAlignment(god);
            float godPower = CalculateGodPower(god);

            // Sacrifice power scales logarithmically (100 gold = 1x, 1000 = 1.5x, 10000 = 2x)
            float sacrificePower = (float)Math.Log10(Math.Max(10, sacrificeValue)) / 2f;
            sacrificePower = Math.Clamp(sacrificePower, 0.5f, 3f);

            // Duration: 30 minutes base + more for larger sacrifices
            int durationMinutes = 30 + (int)(sacrificePower * 15);

            // Faith faction members get longer blessings
            float blessingMultiplier = FactionSystem.Instance?.GetBlessingDurationMultiplier() ?? 1.0f;
            durationMinutes = (int)(durationMinutes * blessingMultiplier);

            var blessing = new TemporaryBlessing
            {
                PlayerName = character.Name2,
                GodName = godName,
                GrantedAt = DateTime.Now,
                ExpiresAt = DateTime.Now.AddMinutes(durationMinutes)
            };

            if (alignment > 0.3f) // Good god
            {
                blessing.Name = $"{godName}'s Protection";
                blessing.DefenseBonus = (int)(5 + sacrificePower * godPower * 10);
                blessing.Description = $"Divine protection reduces damage taken by {blessing.DefenseBonus}%";
            }
            else if (alignment < -0.3f) // Dark god
            {
                blessing.Name = $"{godName}'s Fury";
                blessing.DamageBonus = (int)(5 + sacrificePower * godPower * 10);
                blessing.Description = $"Dark power increases damage dealt by {blessing.DamageBonus}%";
            }
            else // Balanced
            {
                blessing.Name = $"{godName}'s Favor";
                blessing.XPBonus = (int)(10 + sacrificePower * godPower * 15);
                blessing.Description = $"Divine favor grants {blessing.XPBonus}% bonus XP";
            }

            temporaryBlessings[character.Name2] = blessing;

            // GD.Print($"[Divine] {character.Name2} received {blessing.Name} for {durationMinutes} minutes");

            return blessing;
        }

        /// <summary>
        /// Grant a daily prayer blessing (once per day at temple)
        /// </summary>
        public TemporaryBlessing? GrantPrayerBlessing(Character character)
        {
            var godSystem = GodSystemSingleton.Instance;
            string godName = godSystem.GetPlayerGod(character.Name2);

            if (string.IsNullOrEmpty(godName))
                return null;

            // Check if already prayed today
            if (lastPrayerTime.TryGetValue(character.Name2, out var lastPrayer))
            {
                if (lastPrayer.Date == DateTime.Now.Date)
                    return null; // Already prayed today
            }

            lastPrayerTime[character.Name2] = DateTime.Now;

            var god = godSystem.GetGod(godName);
            if (god == null) return null;

            float alignment = CalculateAlignment(god);
            float godPower = CalculateGodPower(god);

            // Prayer blessing lasts for 2 hours (in-game session), longer for Faith members
            float prayerMultiplier = FactionSystem.Instance?.GetBlessingDurationMultiplier() ?? 1.0f;
            int prayerMinutes = (int)(120 * prayerMultiplier);

            var blessing = new TemporaryBlessing
            {
                PlayerName = character.Name2,
                GodName = godName,
                GrantedAt = DateTime.Now,
                ExpiresAt = DateTime.Now.AddMinutes(prayerMinutes),
                Name = $"{godName}'s Daily Blessing",
                DamageBonus = (int)(3 + godPower * 5),
                DefenseBonus = (int)(3 + godPower * 5),
                XPBonus = (int)(5 + godPower * 10),
                Description = "Your morning prayers grant you divine favor"
            };

            temporaryBlessings[character.Name2] = blessing;

            return blessing;
        }

        /// <summary>
        /// Check if player can pray today
        /// </summary>
        public bool CanPrayToday(string playerName)
        {
            if (lastPrayerTime.TryGetValue(playerName, out var lastPrayer))
            {
                return lastPrayer.Date != DateTime.Now.Date;
            }
            return true;
        }

        /// <summary>
        /// Check for divine intervention (save from death)
        /// Returns true if the god saves the player
        /// </summary>
        public bool CheckDivineIntervention(Character character, int damageReceived)
        {
            var blessing = GetBlessings(character);

            if (!blessing.IsActive || blessing.DivineSaveChance <= 0)
                return false;

            // Only triggers on lethal damage
            if (character.HP - damageReceived > 0)
                return false;

            // Roll for divine intervention
            if (random.Next(100) < blessing.DivineSaveChance)
            {
                // GD.Print($"[Divine] {blessing.GodName} intervened to save {character.Name2}!");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Calculate bonus damage from divine blessings (against monsters)
        /// </summary>
        public int CalculateBonusDamage(Character attacker, Monster defender, int baseDamage)
        {
            var blessing = GetBlessings(attacker);

            if (!blessing.IsActive)
                return 0;

            int bonusDamage = 0;

            // Dark damage bonus (flat % increase)
            if (blessing.DarkDamageBonus > 0)
            {
                bonusDamage += (int)(baseDamage * blessing.DarkDamageBonus / 100f);
            }

            // Undead bonus (if defender is undead-type)
            if (blessing.UndeadDamageBonus > 0 && IsUndeadMonster(defender))
            {
                bonusDamage += (int)(baseDamage * blessing.UndeadDamageBonus / 100f);
            }

            // Temporary damage bonus
            if (blessing.TemporaryDamageBonus > 0)
            {
                bonusDamage += (int)(baseDamage * blessing.TemporaryDamageBonus / 100f);
            }

            // Strength bonus adds to damage
            if (blessing.StrengthBonus > 0)
            {
                bonusDamage += blessing.StrengthBonus;
            }

            return bonusDamage;
        }

        /// <summary>
        /// Calculate damage reduction from divine blessings
        /// </summary>
        public int CalculateDamageReduction(Character defender, int incomingDamage)
        {
            var blessing = GetBlessings(defender);

            if (!blessing.IsActive)
                return 0;

            int reduction = 0;

            // Base damage reduction
            if (blessing.DamageReduction > 0)
            {
                reduction += (int)(incomingDamage * blessing.DamageReduction / 100f);
            }

            // Temporary defense bonus
            if (blessing.TemporaryDefenseBonus > 0)
            {
                reduction += (int)(incomingDamage * blessing.TemporaryDefenseBonus / 100f);
            }

            return Math.Min(reduction, incomingDamage - 1); // Always take at least 1 damage
        }

        /// <summary>
        /// Calculate lifesteal from divine blessings
        /// </summary>
        public int CalculateLifesteal(Character attacker, int damageDealt)
        {
            var blessing = GetBlessings(attacker);

            if (!blessing.IsActive || blessing.LifestealPercent <= 0)
                return 0;

            return (int)(damageDealt * blessing.LifestealPercent / 100f);
        }

        /// <summary>
        /// Calculate critical hit bonus from divine blessings
        /// </summary>
        public int GetCriticalHitBonus(Character attacker)
        {
            var blessing = GetBlessings(attacker);
            return blessing.IsActive ? blessing.CriticalHitBonus : 0;
        }

        /// <summary>
        /// Calculate XP bonus from divine blessings
        /// </summary>
        public int GetXPBonus(Character character)
        {
            var blessing = GetBlessings(character);

            if (!blessing.IsActive)
                return 0;

            return blessing.TemporaryXPBonus;
        }

        /// <summary>
        /// Check if a monster is undead-type
        /// </summary>
        private bool IsUndeadMonster(Monster monster)
        {
            if (monster == null) return false;

            string name = monster.Name?.ToLower() ?? "";
            return name.Contains("skeleton") || name.Contains("zombie") ||
                   name.Contains("ghost") || name.Contains("wraith") ||
                   name.Contains("vampire") || name.Contains("lich") ||
                   name.Contains("undead") || name.Contains("specter") ||
                   name.Contains("mummy") || name.Contains("banshee");
        }

        /// <summary>
        /// Get blessing description for display
        /// </summary>
        public string GetBlessingDescription(Character character)
        {
            var blessing = GetBlessings(character);

            if (!blessing.IsActive)
                return "You have no divine blessing.";

            var lines = new List<string>
            {
                $"Blessed by {blessing.GodName}"
            };

            switch (blessing.BlessingType)
            {
                case BlessingType.Holy:
                    lines.Add($"  Damage Reduction: {blessing.DamageReduction}%");
                    lines.Add($"  Healing Bonus: {blessing.HealingBonus}%");
                    if (blessing.UndeadDamageBonus > 0)
                        lines.Add($"  Bonus vs Undead: {blessing.UndeadDamageBonus}%");
                    if (blessing.DivineSaveChance > 0)
                        lines.Add($"  Divine Save: {blessing.DivineSaveChance}% chance to survive lethal hit");
                    break;

                case BlessingType.Dark:
                    if (blessing.CriticalHitBonus > 0)
                        lines.Add($"  Critical Hit Bonus: +{blessing.CriticalHitBonus}%");
                    if (blessing.LifestealPercent > 0)
                        lines.Add($"  Lifesteal: {blessing.LifestealPercent}%");
                    if (blessing.DarkDamageBonus > 0)
                        lines.Add($"  Dark Damage: +{blessing.DarkDamageBonus}%");
                    if (blessing.FearAura > 0)
                        lines.Add($"  Fear Aura: {blessing.FearAura}% chance to frighten");
                    break;

                case BlessingType.Balanced:
                    if (blessing.DamageReduction > 0)
                        lines.Add($"  Damage Reduction: {blessing.DamageReduction}%");
                    if (blessing.CriticalHitBonus > 0)
                        lines.Add($"  Critical Hit Bonus: +{blessing.CriticalHitBonus}%");
                    if (blessing.AllStatsBonus > 0)
                        lines.Add($"  All Stats: +{blessing.AllStatsBonus}");
                    if (blessing.LuckBonus > 0)
                        lines.Add($"  Luck: +{blessing.LuckBonus}%");
                    break;
            }

            if (!string.IsNullOrEmpty(blessing.SpecialAbility))
                lines.Add($"  Special: {blessing.SpecialAbility}");

            if (blessing.HasTemporaryBlessing)
            {
                var remaining = blessing.TemporaryBlessingExpires - DateTime.Now;
                lines.Add($"  Active: {blessing.TemporaryBlessingName} ({remaining.TotalMinutes:F0} min remaining)");
            }

            return string.Join("\n", lines);
        }
    }

    /// <summary>
    /// Represents active divine blessings for a character
    /// </summary>
    public class DivineBlessing
    {
        public bool IsActive { get; set; }
        public string GodName { get; set; } = "";
        public BlessingType BlessingType { get; set; } = BlessingType.None;

        // Defensive bonuses (Good gods)
        public int DamageReduction { get; set; }
        public int HealingBonus { get; set; }
        public int UndeadDamageBonus { get; set; }
        public int DivineSaveChance { get; set; } // % chance to survive lethal hit

        // Offensive bonuses (Dark gods)
        public int CriticalHitBonus { get; set; }
        public int LifestealPercent { get; set; }
        public int DarkDamageBonus { get; set; }
        public int FearAura { get; set; } // % chance to frighten enemies

        // Balanced bonuses
        public int AllStatsBonus { get; set; }
        public int LuckBonus { get; set; }

        // Stat bonuses (god-specific)
        public int StrengthBonus { get; set; }
        public int IntelligenceBonus { get; set; }
        public int CharismaBonus { get; set; }
        public int MaxHPBonus { get; set; }
        public int SpellPowerBonus { get; set; }
        public int EvadeChance { get; set; }
        public int CounterattackChance { get; set; }

        // Immunities
        public bool BlindImmunity { get; set; }
        public bool PoisonImmunity { get; set; }

        // Special ability description
        public string SpecialAbility { get; set; } = "";

        // Temporary bonuses (from sacrifices/prayers)
        public int TemporaryDamageBonus { get; set; }
        public int TemporaryDefenseBonus { get; set; }
        public int TemporaryXPBonus { get; set; }
        public bool HasTemporaryBlessing { get; set; }
        public string TemporaryBlessingName { get; set; } = "";
        public DateTime TemporaryBlessingExpires { get; set; }
    }

    /// <summary>
    /// Temporary blessing from sacrifice or prayer
    /// </summary>
    public class TemporaryBlessing
    {
        public string PlayerName { get; set; } = "";
        public string GodName { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public DateTime GrantedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public int DamageBonus { get; set; }
        public int DefenseBonus { get; set; }
        public int XPBonus { get; set; }
    }

    public enum BlessingType
    {
        None,
        Holy,
        Dark,
        Balanced
    }
}
