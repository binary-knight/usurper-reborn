using System;
using System.Collections.Generic;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Divine Blessing System: the combat side of worship. 1.2.0 Temple gods piece 2: the flat
    /// alignment table is replaced by each god's distinct boon (GodBoonSystem), scaled by the
    /// follower's Favor tier. The temporary prayer and sacrifice blessings stay, and at Zealot and up
    /// the daily prayer blessing lasts GodPrayerBlessingZealotMultiplier times as long.
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
        private Dictionary<string, int> lastPrayerDay = new(); // Single-player: in-game day number

        /// <summary>
        /// The blessing a character holds now: the god worshipped (canon or player-god), the boon's
        /// domain and strength, and any temporary prayer or sacrifice blessing. NPCs get none.
        /// </summary>
        public DivineBlessing GetBlessings(Character character, GodSystem? gods = null)
        {
            var blessing = new DivineBlessing();
            if (character == null || character.IsNPC) return blessing;

            var god = GodRegistry.GetWorshippedGod(character, gods);
            if (god == null)
                return blessing; // No god = no blessings

            blessing.GodName = god.Value.Name;
            blessing.Domain = GodBoonSystem.GetDomain(character, gods);
            blessing.StrengthPct = GodBoonSystem.GetStrengthPct(character, gods);

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
            float power = (float)Math.Log10(Math.Max(1, god.Experience)) / 7f; // Log10(10M) is about 7
            return Math.Clamp(power, 0.1f, 1.0f);
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

            return blessing;
        }

        /// <summary>
        /// Prayer blessing length in minutes: 120 base, times the Faith's duration multiplier, and
        /// times GodPrayerBlessingZealotMultiplier at Zealot and up.
        /// </summary>
        public static int PrayerBlessingMinutes(GodFavorTier tier, float factionMultiplier)
        {
            float minutes = 120 * factionMultiplier;
            if (tier >= GodFavorTier.Zealot) minutes *= GameConfig.GodPrayerBlessingZealotMultiplier;
            return (int)minutes;
        }

        /// <summary>
        /// A player-god prayer blessing's length in combats: GodPrayerBlessingCombats, times
        /// GodPrayerBlessingZealotMultiplier at Zealot and up (one rule for both kinds of god).
        /// </summary>
        public static int PrayerBlessingCombats(GodFavorTier tier)
        {
            float combats = GameConfig.GodPrayerBlessingCombats;
            if (tier >= GodFavorTier.Zealot) combats *= GameConfig.GodPrayerBlessingZealotMultiplier;
            return (int)combats;
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
            if (!CanPrayToday(character.Name2))
                return null;

            lastPrayerTime[character.Name2] = DateTime.Now;
            lastPrayerDay[character.Name2] = DailySystemManager.Instance.CurrentDay;

            // In online mode, persist to player's LastPrayerRealDate so it survives logout/login
            if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
            {
                var player = GameEngine.Instance?.CurrentPlayer;
                if (player != null)
                    player.LastPrayerRealDate = DateTime.UtcNow;
            }

            var god = godSystem.GetGod(godName);
            if (god == null) return null;

            float godPower = CalculateGodPower(god);

            // Prayer blessing lasts for 2 hours (in-game session), longer for Faith members, twice as long at Zealot and up
            float prayerMultiplier = FactionSystem.Instance?.GetBlessingDurationMultiplier() ?? 1.0f;
            int prayerMinutes = PrayerBlessingMinutes(FavorSystem.GetTier(FavorSystem.GetFavor(character)), prayerMultiplier);

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
            // In online mode, check persisted LastPrayerRealDate against the daily reset boundary
            if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
            {
                var player = GameEngine.Instance?.CurrentPlayer;
                if (player == null) return false;
                var boundary = DailySystemManager.GetCurrentResetBoundary();
                return player.LastPrayerRealDate < boundary;
            }

            // Single-player: compare against in-game day (not real-world date)
            // because multiple in-game days can pass in one real session
            if (lastPrayerDay.TryGetValue(playerName, out var lastDay))
            {
                return DailySystemManager.Instance.CurrentDay > lastDay;
            }
            return true;
        }

        /// <summary>
        /// Bonus damage from divine blessings against a monster: the temporary blessing, Solarius
        /// against undead and demons, Valorian while below GodBoonValorianHpThresholdPct of max HP.
        /// </summary>
        public int CalculateBonusDamage(Character attacker, Monster defender, int baseDamage, GodSystem? gods = null)
        {
            var blessing = GetBlessings(attacker, gods);

            if (!blessing.IsActive || baseDamage <= 0)
                return 0;

            long bonusDamage = 0;

            // Temporary damage bonus
            if (blessing.TemporaryDamageBonus > 0)
                bonusDamage += (int)(baseDamage * blessing.TemporaryDamageBonus / 100f);

            // Solarius: against undead and demons
            if (IsUndeadOrDemon(defender))
                bonusDamage += GodBoonSystem.Bonus(baseDamage, GodBoonSystem.Pct(attacker, GodDomain.Light, GameConfig.GodBoonSolariusUndeadDamagePct, gods));

            // Valorian: below the HP threshold
            if (attacker.MaxHP > 0 && attacker.HP * 100 < attacker.MaxHP * GameConfig.GodBoonValorianHpThresholdPct)
                bonusDamage += GodBoonSystem.Bonus(baseDamage, GodBoonSystem.Pct(attacker, GodDomain.War, GameConfig.GodBoonValorianLowHpDamagePct, gods));

            return (int)Math.Min(bonusDamage, int.MaxValue);
        }

        /// <summary>
        /// Damage reduction from divine blessings: the temporary blessing and Judicar's defence.
        /// Always leaves at least 1 damage.
        /// </summary>
        public int CalculateDamageReduction(Character defender, int incomingDamage, GodSystem? gods = null)
        {
            var blessing = GetBlessings(defender, gods);

            if (!blessing.IsActive || incomingDamage <= 1)
                return 0;

            long reduction = 0;

            // Temporary defense bonus
            if (blessing.TemporaryDefenseBonus > 0)
                reduction += (int)(incomingDamage * blessing.TemporaryDefenseBonus / 100f);

            // Judicar: defence
            reduction += GodBoonSystem.Bonus(incomingDamage, GodBoonSystem.Pct(defender, GodDomain.Law, GameConfig.GodBoonJudicarDefencePct, gods));

            return (int)Math.Min(reduction, incomingDamage - 1); // Always take at least 1 damage
        }

        /// <summary>Critical hit bonus (percentage points) from Umbrath's boon, rounded half up.</summary>
        public int GetCriticalHitBonus(Character attacker, GodSystem? gods = null) =>
            (int)Math.Floor(GodBoonSystem.Pct(attacker, GodDomain.Shadow, GameConfig.GodBoonUmbrathCritPct, gods) + 0.5);

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
        /// Undead or demon, for Solarius: MonsterClass Undead or Demon, the Undead family, or an
        /// undead or demon name.
        /// </summary>
        public static bool IsUndeadOrDemon(Monster monster)
        {
            if (monster == null) return false;
            if (monster.MonsterClass == MonsterClass.Undead || monster.MonsterClass == MonsterClass.Demon) return true;
            if (string.Equals(monster.FamilyName, "Undead", StringComparison.OrdinalIgnoreCase)) return true;

            string name = monster.Name?.ToLower() ?? "";
            return name.Contains("skeleton") || name.Contains("zombie") ||
                   name.Contains("ghost") || name.Contains("wraith") ||
                   name.Contains("vampire") || name.Contains("lich") ||
                   name.Contains("undead") || name.Contains("specter") ||
                   name.Contains("mummy") || name.Contains("banshee") ||
                   name.Contains("demon") || name.Contains("devil") ||
                   name.Contains("archfiend") || name.Contains("hellspawn") || name.Contains("fiend");
        }
    }

    /// <summary>
    /// The divine blessing a character holds: the god, the boon's domain and strength (percent of
    /// the full canon boon), and the temporary prayer or sacrifice blessing.
    /// </summary>
    public class DivineBlessing
    {
        public bool IsActive { get; set; }
        public string GodName { get; set; } = "";
        public GodDomain Domain { get; set; } = GodDomain.None;
        public int StrengthPct { get; set; }

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
}
