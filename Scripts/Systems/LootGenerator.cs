using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UsurperRemake.Systems;

/// <summary>
/// Epic Loot Generation System
/// Generates weapons, armor, and accessories with:
/// - Rarity-based power scaling (Common to Artifact)
/// - Special effects (elemental damage, lifesteal, etc.)
/// - Cursed items (5-10% chance on rare+ items)
/// - Level-appropriate stats that feel exciting and rewarding
///
/// Power Philosophy (v0.46.1: reduced scaling from level/40 to level/80):
/// - Level 1 common weapon: ~5-10 attack
/// - Level 50 epic weapon: ~120-240 attack
/// - Level 100 legendary weapon: ~400-700 attack
/// - Artifacts can exceed 900+ attack with unique effects
/// </summary>
public static class LootGenerator
    {
        private static Random random = new();

        #region Rarity System

        /// <summary>
        /// Rarity defines the power multiplier and special effect chance
        /// </summary>
        public enum ItemRarity
        {
            Common,      // White  - Base stats, no effects
            Uncommon,    // Green  - 1.3x power, minor bonus
            Rare,        // Blue   - 1.7x power, 1 effect, can be cursed
            Epic,        // Purple - 2.2x power, 1-2 effects, can be cursed
            Legendary,   // Orange - 3.0x power, 2-3 effects, can be cursed
            Artifact     // Gold   - 4.0x power, 3+ effects, unique abilities
        }

        private static readonly Dictionary<ItemRarity, (float PowerMult, float ValueMult, int MinEffects, int MaxEffects, float CurseChance)> RarityStats = new()
        {
            { ItemRarity.Common,    (1.0f, 1.0f, 0, 0, 0.00f) },
            { ItemRarity.Uncommon,  (1.3f, 2.0f, 0, 1, 0.00f) },
            { ItemRarity.Rare,      (1.7f, 4.0f, 1, 1, 0.05f) },  // 5% curse chance
            { ItemRarity.Epic,      (2.2f, 8.0f, 1, 2, 0.08f) },  // 8% curse chance
            { ItemRarity.Legendary, (3.0f, 20.0f, 2, 3, 0.10f) }, // 10% curse chance
            { ItemRarity.Artifact,  (4.0f, 50.0f, 3, 4, 0.05f) }  // 5% curse (but devastating)
        };

        /// <summary>
        /// Roll for item rarity based on dungeon level
        /// Higher levels have better chances for rare items
        /// </summary>
        /// <summary>
        /// Roll for item rarity based on dungeon level.
        /// v0.52.5: Improved mid-game curve — less Common at levels 20-40, more Uncommon/Rare.
        /// At level 1:   60% Common, 30% Uncommon, 10% Rare
        /// At level 25:  30% Common, 35% Uncommon, 25% Rare, 8% Epic, 2% Legendary
        /// At level 50:  15% Common, 30% Uncommon, 30% Rare, 17% Epic, 6% Legendary, 2% Artifact
        /// At level 100: 5% Common, 20% Uncommon, 35% Rare, 25% Epic, 12% Legendary, 3% Artifact
        /// </summary>
        public static ItemRarity RollRarity(int level)
        {
            double roll = random.NextDouble();

            float levelFactor = Math.Min(1.0f, level / 100f);

            float artifactChance = levelFactor * 0.03f;
            float legendaryChance = 0.02f + levelFactor * 0.10f;
            float epicChance = 0.03f + levelFactor * 0.22f;
            float rareChance = 0.10f + levelFactor * 0.25f;
            float uncommonChance = 0.30f - levelFactor * 0.10f;

            if (roll < artifactChance) return ItemRarity.Artifact;
            if (roll < artifactChance + legendaryChance) return ItemRarity.Legendary;
            if (roll < artifactChance + legendaryChance + epicChance) return ItemRarity.Epic;
            if (roll < artifactChance + legendaryChance + epicChance + rareChance) return ItemRarity.Rare;
            if (roll < artifactChance + legendaryChance + epicChance + rareChance + uncommonChance) return ItemRarity.Uncommon;

            return ItemRarity.Common;
        }

        public static string GetRarityColor(ItemRarity rarity) => rarity switch
        {
            ItemRarity.Common => "white",
            ItemRarity.Uncommon => "green",
            ItemRarity.Rare => "cyan",
            ItemRarity.Epic => "magenta",
            ItemRarity.Legendary => "yellow",
            ItemRarity.Artifact => "bright_yellow",
            _ => "white"
        };

        public static string GetRarityPrefix(ItemRarity rarity)
        {
            var key = rarity switch
            {
                ItemRarity.Uncommon => "item.rarity.fine",
                ItemRarity.Rare => "item.rarity.superior",
                ItemRarity.Epic => "item.rarity.exquisite",
                ItemRarity.Legendary => "item.rarity.legendary",
                ItemRarity.Artifact => "item.rarity.mythic",
                _ => null
            };
            return key == null ? "" : Loc.Get(key) + " ";
        }

        /// <summary>
        /// Convert an English template name to a localization key and look it up.
        /// E.g. "Long Sword" → "item.long_sword" → Loc.Get("item.long_sword")
        /// Falls back to the original English name if no key exists.
        /// </summary>
        private static string LocalizeTemplateName(string englishName)
        {
            // Strip apostrophes first (Assassin's → Assassins), then convert remaining non-alphanumeric to underscores
            string stripped = englishName.Replace("'", "").Replace("\u2019", "");
            string key = "item." + Regex.Replace(stripped.ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_');
            string result = Loc.Get(key);
            // If Loc.Get returns the raw key, the template isn't in the localization file — use original name
            return result == key ? englishName : result;
        }

        /// <summary>
        /// Get localized effect prefix/suffix/name via item.effect.* keys.
        /// Converts SpecialEffect enum to snake_case key component.
        /// </summary>
        private static string GetEffectKey(SpecialEffect effect)
        {
            // Convert PascalCase enum to snake_case: FireDamage → fire_damage
            string name = effect.ToString();
            return Regex.Replace(name, @"(?<!^)([A-Z])", "_$1").ToLowerInvariant();
        }

        private static string GetLocalizedEffectPrefix(SpecialEffect effect)
        {
            return Loc.Get($"item.effect.{GetEffectKey(effect)}.prefix");
        }

        private static string GetLocalizedEffectSuffix(SpecialEffect effect)
        {
            return Loc.Get($"item.effect.{GetEffectKey(effect)}.suffix");
        }

        private static string GetLocalizedEffectName(SpecialEffect effect)
        {
            return Loc.Get($"item.effect.{GetEffectKey(effect)}.name");
        }

        #endregion

        #region Special Effects

        public enum SpecialEffect
        {
            None,
            // Offensive
            FireDamage,      // +X fire damage per hit
            IceDamage,       // +X ice damage, chance to slow
            LightningDamage, // +X lightning, chance to stun
            PoisonDamage,    // +X poison damage over time
            HolyDamage,      // +X holy damage (bonus vs undead)
            ShadowDamage,    // +X shadow damage (bonus vs living)
            LifeSteal,       // Heal % of damage dealt
            ManaSteal,       // Restore mana on hit
            CriticalStrike,  // +% critical hit chance
            CriticalDamage,  // +% critical damage multiplier
            ArmorPiercing,   // Ignore % of enemy armor

            // Defensive
            FireResist,      // +% fire resistance
            IceResist,       // +% ice resistance
            LightningResist, // +% lightning resistance
            PoisonResist,    // +% poison resistance
            MagicResist,     // +% all magic resistance
            Thorns,          // Reflect % damage to attackers
            Regeneration,    // Heal X HP per turn
            ManaRegen,       // Restore X mana per turn
            DamageReduction, // Flat damage reduction
            BlockChance,     // +% chance to block attacks

            // Stat boosts
            Strength,        // +X strength
            Dexterity,       // +X dexterity
            Constitution,    // +X constitution (bonus HP)
            Intelligence,    // +X intelligence (bonus mana)
            Wisdom,          // +X wisdom
            AllStats,        // +X to all stats
            MaxHP,           // +X max HP
            MaxMana,         // +X max mana

            // World Boss exclusive
            BossSlayer,      // +X% damage to all bosses
            TitanResolve     // +X% max HP bonus
        }

        private static readonly Dictionary<SpecialEffect, (string Name, string Prefix, string Suffix, bool IsOffensive)> EffectInfo = new()
        {
            { SpecialEffect.FireDamage, ("Fire Damage", "Blazing ", " of Flames", true) },
            { SpecialEffect.IceDamage, ("Ice Damage", "Frozen ", " of Frost", true) },
            { SpecialEffect.LightningDamage, ("Lightning", "Shocking ", " of Thunder", true) },
            { SpecialEffect.PoisonDamage, ("Poison", "Venomous ", " of Venom", true) },
            { SpecialEffect.HolyDamage, ("Holy Damage", "Holy ", " of Light", true) },
            { SpecialEffect.ShadowDamage, ("Shadow", "Shadow ", " of Darkness", true) },
            { SpecialEffect.LifeSteal, ("Life Steal", "Vampiric ", " of the Leech", true) },
            { SpecialEffect.ManaSteal, ("Mana Steal", "Siphoning ", " of Sorcery", true) },
            { SpecialEffect.CriticalStrike, ("Crit Chance", "Keen ", " of Precision", true) },
            { SpecialEffect.CriticalDamage, ("Crit Damage", "Deadly ", " of Devastation", true) },
            { SpecialEffect.ArmorPiercing, ("Armor Pierce", "Piercing ", " of Penetration", true) },

            { SpecialEffect.FireResist, ("Fire Resist", "Fireproof ", " of the Salamander", false) },
            { SpecialEffect.IceResist, ("Ice Resist", "Insulated ", " of the Yeti", false) },
            { SpecialEffect.LightningResist, ("Lightning Resist", "Grounded ", " of the Storm", false) },
            { SpecialEffect.PoisonResist, ("Poison Resist", "Purified ", " of Immunity", false) },
            { SpecialEffect.MagicResist, ("Magic Resist", "Warded ", " of Shielding", false) },
            { SpecialEffect.Thorns, ("Thorns", "Spiked ", " of Retaliation", false) },
            { SpecialEffect.Regeneration, ("HP Regen", "Regenerating ", " of Healing", false) },
            { SpecialEffect.ManaRegen, ("Mana Regen", "Mystical ", " of the Arcane", false) },
            { SpecialEffect.DamageReduction, ("Damage Reduction", "Hardened ", " of Protection", false) },
            { SpecialEffect.BlockChance, ("Block", "Sturdy ", " of the Sentinel", false) },

            { SpecialEffect.Strength, ("Strength", "Mighty ", " of Strength", false) },
            { SpecialEffect.Dexterity, ("Dexterity", "Nimble ", " of Agility", false) },
            { SpecialEffect.Constitution, ("Constitution", "Stalwart ", " of Fortitude", false) },
            { SpecialEffect.Intelligence, ("Intelligence", "Sage ", " of the Mind", false) },
            { SpecialEffect.Wisdom, ("Wisdom", "Wise ", " of Insight", false) },
            { SpecialEffect.AllStats, ("All Stats", "Empowering ", " of Perfection", false) },
            { SpecialEffect.MaxHP, ("Max HP", "Robust ", " of Vitality", false) },
            { SpecialEffect.MaxMana, ("Max Mana", "Arcane ", " of Power", false) },

            { SpecialEffect.BossSlayer, ("Boss Slayer", "Titansbane ", " of the World Slayer", true) },
            { SpecialEffect.TitanResolve, ("Titan's Resolve", "Titan's ", " of the Colossus", false) }
        };

        /// <summary>
        /// Roll random effects for an item based on rarity
        /// </summary>
        private static List<(SpecialEffect effect, int value)> RollEffects(ItemRarity rarity, int level, bool isWeapon)
        {
            var effects = new List<(SpecialEffect, int)>();
            var stats = RarityStats[rarity];

            int numEffects = random.Next(stats.MinEffects, stats.MaxEffects + 1);

            // Get appropriate effects based on item type
            var possibleEffects = EffectInfo.Keys
                .Where(e => e != SpecialEffect.None)
                .Where(e => isWeapon ? EffectInfo[e].IsOffensive : !EffectInfo[e].IsOffensive || e == SpecialEffect.Thorns)
                .ToList();

            for (int i = 0; i < numEffects && possibleEffects.Count > 0; i++)
            {
                var effect = possibleEffects[random.Next(possibleEffects.Count)];
                possibleEffects.Remove(effect); // No duplicate effects

                // Calculate effect value based on level and rarity
                int baseValue = CalculateEffectValue(effect, level, rarity);
                effects.Add((effect, baseValue));
            }

            return effects;
        }

        private static int CalculateEffectValue(SpecialEffect effect, int level, ItemRarity rarity)
        {
            float rarityMult = RarityStats[rarity].PowerMult;

            return effect switch
            {
                // Elemental damage scales with level
                SpecialEffect.FireDamage or SpecialEffect.IceDamage or
                SpecialEffect.LightningDamage or SpecialEffect.PoisonDamage or
                SpecialEffect.HolyDamage or SpecialEffect.ShadowDamage
                    => (int)(5 + level * 0.8f * rarityMult),

                // Percentages cap at reasonable values
                SpecialEffect.LifeSteal or SpecialEffect.ManaSteal
                    => Math.Min(25, (int)(3 + level * 0.15f * rarityMult)),

                SpecialEffect.CriticalStrike
                    => Math.Min(30, (int)(5 + level * 0.2f * rarityMult)),

                SpecialEffect.CriticalDamage
                    => (int)(25 + level * 0.5f * rarityMult),

                SpecialEffect.ArmorPiercing
                    => Math.Min(50, (int)(10 + level * 0.3f * rarityMult)),

                // Resistances cap at 75%
                SpecialEffect.FireResist or SpecialEffect.IceResist or
                SpecialEffect.LightningResist or SpecialEffect.PoisonResist
                    => Math.Min(75, (int)(10 + level * 0.5f * rarityMult)),

                SpecialEffect.MagicResist
                    => Math.Min(50, (int)(5 + level * 0.3f * rarityMult)),

                // Flat bonuses
                SpecialEffect.Thorns
                    => (int)(level * 0.2f * rarityMult),

                SpecialEffect.Regeneration
                    => (int)(2 + level * 0.1f * rarityMult),

                SpecialEffect.ManaRegen
                    => (int)(1 + level * 0.08f * rarityMult),

                SpecialEffect.DamageReduction
                    => (int)(5 + level * 0.3f * rarityMult),

                SpecialEffect.BlockChance
                    => Math.Min(40, (int)(5 + level * 0.25f * rarityMult)),

                // Stat bonuses
                SpecialEffect.Strength or SpecialEffect.Dexterity or
                SpecialEffect.Constitution or SpecialEffect.Intelligence or
                SpecialEffect.Wisdom
                    => (int)(2 + level * 0.15f * rarityMult),

                SpecialEffect.AllStats
                    => (int)(1 + level * 0.08f * rarityMult),

                SpecialEffect.MaxHP
                    => (int)(10 + level * 1.5f * rarityMult),

                SpecialEffect.MaxMana
                    => (int)(5 + level * 0.8f * rarityMult),

                _ => 5
            };
        }

        #endregion

        #region Weapon Templates

        private static readonly List<(string Name, string[] Classes, int MinLevel, int MaxLevel, float BasePower)> WeaponTemplates = new()
        {
            // Daggers - Fast, low damage, high crit
            ("Dagger", new[] { "All" }, 1, 30, 8),
            ("Stiletto", new[] { "Assassin", "Ranger", "Jester" }, 10, 50, 15),
            ("Assassin's Blade", new[] { "Assassin" }, 30, 80, 35),
            ("Shadow Fang", new[] { "Assassin" }, 50, 100, 60),

            // Swords - Balanced (Assassin included for dual-wield builds)
            ("Short Sword", new[] { "All" }, 1, 25, 10),
            ("Long Sword", new[] { "All" }, 10, 50, 20),
            ("Broadsword", new[] { "Warrior", "Paladin", "Barbarian", "Assassin" }, 20, 70, 35),
            ("Bastard Sword", new[] { "Warrior", "Paladin", "Assassin" }, 35, 85, 55),
            ("Greatsword", new[] { "Warrior", "Barbarian" }, 50, 100, 80),
            ("Executioner's Blade", new[] { "Warrior" }, 70, 100, 110),

            // Axes - High damage, slower
            ("Hand Axe", new[] { "Warrior", "Barbarian", "Ranger" }, 1, 30, 12),
            ("Battle Axe", new[] { "Warrior", "Barbarian" }, 15, 60, 28),
            ("Great Axe", new[] { "Barbarian" }, 30, 85, 50),
            ("Berserker Axe", new[] { "Barbarian" }, 55, 100, 90),
            ("Titan Cleaver", new[] { "Barbarian" }, 80, 100, 130),

            // Maces - Anti-armor
            ("Club", new[] { "All" }, 1, 20, 6),
            ("Mace", new[] { "Cleric", "Paladin", "Warrior", "Jester", "Alchemist" }, 10, 45, 18),
            ("War Hammer", new[] { "Cleric", "Paladin", "Warrior", "Alchemist" }, 25, 70, 40),
            ("Flail", new[] { "Cleric", "Paladin", "Alchemist" }, 40, 85, 60),
            ("Holy Mace", new[] { "Cleric", "Paladin" }, 60, 100, 85),
            ("Scepter of Judgment", new[] { "Paladin" }, 80, 100, 120),

            // General weapons - Available to all classes at higher levels
            ("Fine Dagger", new[] { "All" }, 25, 55, 18),
            ("Soldier's Sword", new[] { "All" }, 30, 65, 28),
            ("Forged Mace", new[] { "All" }, 35, 70, 32),
            ("Tempered Blade", new[] { "All" }, 50, 85, 48),
            ("Runed Sword", new[] { "All" }, 65, 100, 65),
            ("Ancient Blade", new[] { "All" }, 80, 100, 85),

            // Staves - Magic focused
            ("Quarterstaff", new[] { "Magician", "Sage", "Cleric", "Alchemist" }, 1, 35, 8),
            ("Magic Staff", new[] { "Magician", "Sage" }, 15, 55, 22),
            ("Staff of Power", new[] { "Magician", "Sage" }, 35, 80, 45),
            ("Archmage's Staff", new[] { "Magician", "Sage" }, 60, 100, 75),
            ("Staff of the Cosmos", new[] { "Sage" }, 85, 100, 110),

            // Holy weapons (Paladin exclusive high tier)
            ("Silver Blade", new[] { "Paladin" }, 35, 75, 50),
            ("Holy Avenger", new[] { "Paladin" }, 60, 100, 90),
            ("Blade of the Righteous", new[] { "Paladin" }, 85, 100, 135),

            // Bard weapons - Performance blades (raw damage, no musical abilities)
            ("Rapier", new[] { "Bard", "Jester", "Assassin" }, 5, 40, 12),
            ("Dueling Blade", new[] { "Bard", "Jester" }, 20, 60, 25),
            ("Songblade", new[] { "Bard" }, 35, 80, 45),
            ("Virtuoso's Rapier", new[] { "Bard" }, 60, 100, 75),

            // Bard instruments - Musical instruments (required for musical abilities)
            ("Wooden Flute", new[] { "Bard" }, 1, 25, 7),
            ("Travel Lute", new[] { "Bard" }, 5, 35, 10),
            ("Silver Lyre", new[] { "Bard" }, 15, 50, 16),
            ("War Drum", new[] { "Bard" }, 25, 60, 22),
            ("Enchanted Harp", new[] { "Bard" }, 35, 70, 30),
            ("Battle Horn", new[] { "Bard" }, 45, 80, 38),
            ("Mythril Lute", new[] { "Bard" }, 55, 85, 48),
            ("Celestial Harp", new[] { "Bard" }, 65, 95, 58),
            ("Songweaver's Opus", new[] { "Bard" }, 75, 100, 70),
            ("Instrument of the Spheres", new[] { "Bard" }, 85, 100, 85),

            // Jester weapons - Trick weapons and gadgets
            ("Throwing Knife", new[] { "Jester", "Assassin" }, 5, 35, 10),
            ("Trick Blade", new[] { "Jester" }, 20, 60, 22),
            ("Jester's Scepter", new[] { "Jester" }, 40, 80, 42),
            ("Chaos Edge", new[] { "Jester" }, 50, 90, 55),
            ("Fool's Edge", new[] { "Jester" }, 65, 100, 70),
            ("Madcap's Razor", new[] { "Jester" }, 80, 100, 90),

            // Alchemist weapons - Enchanted and chemical weapons
            ("Pestle Club", new[] { "Alchemist" }, 1, 30, 8),
            ("Alchemist's Blade", new[] { "Alchemist" }, 15, 50, 20),
            ("Venom-Etched Dagger", new[] { "Alchemist", "Assassin" }, 30, 70, 38),
            ("Transmuter's Staff", new[] { "Alchemist" }, 45, 85, 55),
            ("Elixir-Infused Blade", new[] { "Alchemist" }, 55, 95, 65),
            ("Philosopher's Edge", new[] { "Alchemist" }, 70, 100, 85),

            // === TWO-HANDED WEAPONS START HERE ===

            // Two-Handed Greatswords - High damage, balanced
            ("Wooden Greatsword", new[] { "Warrior", "Barbarian", "Paladin" }, 1, 20, 8),
            ("Iron Greatsword", new[] { "Warrior", "Barbarian", "Paladin" }, 8, 35, 12),
            ("Steel Greatsword", new[] { "Warrior", "Barbarian", "Paladin" }, 15, 50, 16),
            ("Claymore", new[] { "Warrior", "Barbarian", "Paladin" }, 25, 65, 22),
            ("Zweihander", new[] { "Warrior", "Barbarian" }, 40, 80, 28),
            ("Bloodreaver", new[] { "Warrior", "Barbarian" }, 55, 90, 34),
            ("Dragonslayer", new[] { "Warrior", "Barbarian", "Paladin" }, 65, 100, 40),
            ("Flamberge", new[] { "Warrior", "Barbarian" }, 75, 100, 46),

            // Two-Handed Great Axes - Highest damage, crit focused
            ("Woodcutter's Axe", new[] { "Barbarian", "Warrior" }, 1, 25, 10),
            ("Greataxe", new[] { "Barbarian", "Warrior" }, 12, 45, 18),
            ("Barbarian's Greataxe", new[] { "Barbarian" }, 25, 65, 26),
            ("Demon Cleaver", new[] { "Barbarian", "Warrior" }, 40, 80, 35),
            ("Headsman's Pride", new[] { "Barbarian", "Warrior" }, 55, 90, 42),
            ("Annihilator", new[] { "Barbarian" }, 70, 100, 50),

            // Two-Handed Staves (magic) - Mana focused
            ("Wooden Staff", new[] { "Magician", "Sage", "Cleric" }, 1, 20, 5),
            ("Oak Staff", new[] { "Magician", "Sage", "Cleric" }, 8, 35, 8),
            ("Mage's Staff", new[] { "Magician", "Sage" }, 15, 50, 12),
            ("Archmage Staff", new[] { "Magician", "Sage" }, 30, 65, 18),
            ("Soulstaff", new[] { "Magician", "Sage" }, 55, 90, 40),
            ("Void Staff", new[] { "Magician", "Sage" }, 70, 100, 48),

            // Two-Handed Polearms - Reach, versatile
            ("Spear", new[] { "Warrior", "Paladin", "Ranger", "Barbarian" }, 1, 25, 9),
            ("Halberd", new[] { "Warrior", "Paladin" }, 12, 50, 15),
            ("Glaive", new[] { "Warrior", "Paladin", "Ranger" }, 20, 60, 20),
            ("Bardiche", new[] { "Warrior", "Barbarian" }, 35, 75, 28),
            ("Dragon Lance", new[] { "Warrior", "Paladin" }, 50, 90, 36),
            ("Voulge of Rending", new[] { "Warrior", "Barbarian" }, 65, 100, 45),

            // Two-Handed Mauls - Heavy crushing
            ("Sledgehammer", new[] { "Warrior", "Barbarian", "Paladin" }, 5, 35, 14),
            ("War Maul", new[] { "Warrior", "Barbarian" }, 15, 55, 22),
            ("Crusher", new[] { "Warrior", "Barbarian" }, 30, 70, 32),
            ("Earthshaker", new[] { "Warrior", "Barbarian", "Paladin" }, 45, 85, 44),
            ("Skullbreaker", new[] { "Warrior", "Barbarian" }, 65, 100, 54),

            // Ranger Bows - Two-handed ranged (Ranger-exclusive)
            ("Hunting Bow", new[] { "Ranger" }, 5, 40, 16),
            ("Longbow", new[] { "Ranger" }, 25, 70, 38),
            ("Composite Bow", new[] { "Ranger" }, 50, 90, 65),
            ("Elven Bow", new[] { "Ranger" }, 75, 100, 100),
            ("Eagle Eye Bow", new[] { "Ranger" }, 60, 95, 80),
            ("Sniper's Longbow", new[] { "Ranger" }, 80, 100, 115),
            ("Windstrike Bow", new[] { "Ranger" }, 90, 100, 130),

            // Two-Handed Bows - Ranged weapons
            ("Short Bow", new[] { "All" }, 1, 30, 8),
            ("Hunting Bow", new[] { "All" }, 5, 40, 10),
            ("Longbow", new[] { "All" }, 10, 50, 14),
            ("Composite Bow", new[] { "All" }, 20, 60, 18),
            ("War Bow", new[] { "All" }, 30, 70, 22),
            ("Elven Bow", new[] { "All" }, 40, 80, 26),
            ("Shadow Bow", new[] { "All" }, 50, 90, 30),
            ("Dragonbone Bow", new[] { "All" }, 60, 100, 34),
            ("Celestial Bow", new[] { "All" }, 75, 100, 38),
            ("Bow of the Planes", new[] { "All" }, 85, 100, 42),
        };

        #endregion

        #region Armor Templates

        private static readonly List<(string Name, string[] Classes, int MinLevel, int MaxLevel, float BasePower)> ArmorTemplates = new()
        {
            // Cloth (Casters)
            ("Cloth Robe", new[] { "Magician", "Sage" }, 1, 25, 5),
            ("Silk Vestments", new[] { "Magician", "Sage" }, 15, 50, 15),
            ("Enchanted Robe", new[] { "Magician", "Sage" }, 35, 75, 30),
            ("Arcane Vestments", new[] { "Magician", "Sage" }, 55, 90, 50),
            ("Robe of the Archmage", new[] { "Magician", "Sage" }, 80, 100, 80),

            // Leather (Light classes)
            ("Leather Armor", new[] { "All" }, 1, 30, 8),
            ("Studded Leather", new[] { "Ranger", "Assassin", "Barbarian" }, 15, 50, 18),
            ("Hard Leather", new[] { "Ranger", "Assassin" }, 30, 70, 32),
            ("Shadow Leather", new[] { "Assassin" }, 50, 90, 55),
            ("Night Stalker Armor", new[] { "Assassin" }, 75, 100, 85),

            // Ranger specific
            ("Ranger's Cloak", new[] { "Ranger" }, 20, 60, 25),
            ("Forest Guardian Armor", new[] { "Ranger" }, 45, 85, 50),
            ("Elven Chainweave", new[] { "Ranger" }, 70, 100, 80),

            // Light fighter armor
            ("Training Gi", new[] { "Assassin", "Barbarian" }, 5, 35, 10),
            ("Reinforced Gi", new[] { "Assassin", "Barbarian" }, 25, 65, 28),
            ("Master's Gi", new[] { "Assassin", "Barbarian" }, 50, 90, 52),
            ("Dragon Scale Gi", new[] { "Assassin", "Barbarian" }, 80, 100, 88),

            // Chain (Medium classes)
            ("Chain Shirt", new[] { "Warrior", "Paladin", "Cleric", "Ranger" }, 10, 45, 15),
            ("Chain Mail", new[] { "Warrior", "Paladin", "Cleric" }, 25, 65, 30),
            ("Reinforced Chain", new[] { "Warrior", "Paladin", "Cleric" }, 45, 85, 52),

            // Plate (Heavy classes)
            ("Banded Mail", new[] { "Warrior", "Paladin" }, 20, 55, 25),
            ("Splint Mail", new[] { "Warrior", "Paladin" }, 35, 70, 40),
            ("Plate Mail", new[] { "Warrior", "Paladin" }, 50, 85, 60),
            ("Full Plate", new[] { "Warrior", "Paladin" }, 65, 95, 85),
            ("Adamantine Plate", new[] { "Warrior" }, 85, 100, 120),

            // Paladin holy armor
            ("Holy Vestments", new[] { "Paladin", "Cleric" }, 30, 70, 38),
            ("Blessed Plate", new[] { "Paladin" }, 55, 90, 70),
            ("Divine Armor", new[] { "Paladin" }, 80, 100, 110),

            // Cleric specific
            ("Priest's Robes", new[] { "Cleric" }, 10, 45, 12),
            ("Sacred Vestments", new[] { "Cleric" }, 35, 75, 35),
            ("Vestments of the Faith", new[] { "Cleric" }, 65, 100, 68),

            // Barbarian (light but tough)
            ("Barbarian Hide", new[] { "Barbarian" }, 10, 50, 20),
            ("War Paint Armor", new[] { "Barbarian" }, 30, 75, 42),
            ("Berserker's Plate", new[] { "Barbarian" }, 55, 95, 72),
            ("Titan's Harness", new[] { "Barbarian" }, 80, 100, 105),

            // General body armor - Available to all classes at higher levels
            ("Reinforced Leather", new[] { "All" }, 25, 55, 22),
            ("Chainweave Tunic", new[] { "All" }, 35, 70, 35),
            ("Forged Brigandine", new[] { "All" }, 50, 85, 52),
            ("Runed Hauberk", new[] { "All" }, 65, 100, 70),
            ("Ancient Mail", new[] { "All" }, 80, 100, 90),
        };

        // Per-slot armor templates for dungeon loot drops
        private static readonly List<(string Name, string[] Classes, int MinLevel, int MaxLevel, float BasePower)> HeadArmorTemplates = new()
        {
            ("Leather Cap", new[] { "All" }, 1, 25, 4),
            ("Iron Helm", new[] { "Warrior", "Paladin", "Barbarian" }, 10, 45, 12),
            ("Chain Coif", new[] { "Warrior", "Paladin", "Cleric", "Ranger" }, 15, 55, 16),
            ("Reinforced Helm", new[] { "All" }, 20, 50, 14),
            ("Steel Helm", new[] { "Warrior", "Paladin", "Barbarian" }, 25, 65, 25),
            ("Wizard's Hat", new[] { "Magician", "Sage" }, 15, 60, 10),
            ("Fighter's Headband", new[] { "Assassin", "Barbarian", "Ranger" }, 10, 55, 8),
            ("Shadow Hood", new[] { "Assassin", "Ranger" }, 20, 70, 18),
            ("Forged Helm", new[] { "All" }, 35, 70, 30),
            ("Battle Crown", new[] { "Warrior", "Paladin" }, 40, 80, 38),
            ("Mithril Helm", new[] { "All" }, 55, 90, 52),
            ("Runed Helm", new[] { "All" }, 65, 100, 62),
            ("Crown of the Archmage", new[] { "Magician", "Sage" }, 70, 100, 65),
            ("Titan's Greathelm", new[] { "Warrior", "Barbarian" }, 75, 100, 78),
            ("Holy Diadem", new[] { "Paladin", "Cleric" }, 65, 100, 60),
        };

        private static readonly List<(string Name, string[] Classes, int MinLevel, int MaxLevel, float BasePower)> ArmsArmorTemplates = new()
        {
            ("Leather Bracers", new[] { "All" }, 1, 25, 3),
            ("Iron Vambraces", new[] { "Warrior", "Paladin", "Barbarian" }, 10, 45, 10),
            ("Chain Sleeves", new[] { "Warrior", "Paladin", "Cleric" }, 15, 55, 14),
            ("Studded Armguards", new[] { "Ranger", "Assassin" }, 15, 50, 12),
            ("Reinforced Bracers", new[] { "All" }, 20, 50, 12),
            ("Steel Vambraces", new[] { "Warrior", "Paladin" }, 25, 65, 22),
            ("Silk Arm Wraps", new[] { "Magician", "Sage", "Assassin" }, 10, 50, 7),
            ("Barbarian Arm Guards", new[] { "Barbarian" }, 20, 65, 18),
            ("Forged Armguards", new[] { "All" }, 35, 70, 28),
            ("Mithril Armguards", new[] { "All" }, 45, 85, 40),
            ("Runed Bracers", new[] { "All" }, 65, 100, 58),
            ("Shadow Bracers", new[] { "Assassin" }, 40, 80, 35),
            ("Plate Vambraces", new[] { "Warrior", "Paladin" }, 55, 95, 55),
            ("Holy Armguards", new[] { "Paladin", "Cleric" }, 60, 100, 58),
            ("Dragonscale Bracers", new[] { "All" }, 80, 100, 75),
        };

        private static readonly List<(string Name, string[] Classes, int MinLevel, int MaxLevel, float BasePower)> HandsArmorTemplates = new()
        {
            ("Cloth Gloves", new[] { "All" }, 1, 20, 2),
            ("Leather Gloves", new[] { "All" }, 5, 30, 5),
            ("Chain Gauntlets", new[] { "Warrior", "Paladin", "Cleric" }, 15, 55, 14),
            ("Thief's Gloves", new[] { "Assassin", "Ranger" }, 10, 50, 10),
            ("Iron Gauntlets", new[] { "Warrior", "Paladin", "Barbarian" }, 20, 60, 18),
            ("Reinforced Gloves", new[] { "All" }, 20, 50, 10),
            ("Silk Handwraps", new[] { "Magician", "Sage", "Assassin" }, 10, 50, 7),
            ("Steel Gauntlets", new[] { "Warrior", "Paladin" }, 35, 75, 30),
            ("Spiked Fists", new[] { "Barbarian", "Warrior" }, 30, 70, 25),
            ("Forged Gauntlets", new[] { "All" }, 35, 70, 26),
            ("Mithril Gloves", new[] { "All" }, 50, 85, 42),
            ("Runed Gauntlets", new[] { "All" }, 65, 100, 56),
            ("Shadow Handwraps", new[] { "Assassin" }, 45, 85, 38),
            ("Plate Gauntlets", new[] { "Warrior", "Paladin" }, 60, 95, 55),
            ("Dragon Grip", new[] { "All" }, 80, 100, 72),
        };

        private static readonly List<(string Name, string[] Classes, int MinLevel, int MaxLevel, float BasePower)> LegsArmorTemplates = new()
        {
            ("Cloth Leggings", new[] { "All" }, 1, 20, 3),
            ("Leather Leggings", new[] { "All" }, 5, 30, 6),
            ("Chain Leggings", new[] { "Warrior", "Paladin", "Cleric", "Ranger" }, 15, 55, 15),
            ("Studded Legguards", new[] { "Ranger", "Assassin" }, 15, 50, 12),
            ("Iron Greaves", new[] { "Warrior", "Paladin", "Barbarian" }, 20, 60, 20),
            ("Reinforced Leggings", new[] { "All" }, 20, 50, 12),
            ("Silk Trousers", new[] { "Magician", "Sage" }, 10, 50, 7),
            ("Steel Greaves", new[] { "Warrior", "Paladin" }, 35, 75, 32),
            ("Barbarian Legguards", new[] { "Barbarian" }, 25, 70, 22),
            ("Forged Greaves", new[] { "All" }, 35, 70, 28),
            ("Mithril Legguards", new[] { "All" }, 50, 85, 45),
            ("Runed Legguards", new[] { "All" }, 65, 100, 58),
            ("Shadow Leggings", new[] { "Assassin" }, 40, 80, 35),
            ("Plate Greaves", new[] { "Warrior", "Paladin" }, 60, 95, 58),
            ("Titan's Legplates", new[] { "Warrior", "Barbarian" }, 80, 100, 75),
        };

        private static readonly List<(string Name, string[] Classes, int MinLevel, int MaxLevel, float BasePower)> FeetArmorTemplates = new()
        {
            ("Cloth Sandals", new[] { "All" }, 1, 20, 2),
            ("Leather Boots", new[] { "All" }, 5, 30, 5),
            ("Iron Boots", new[] { "Warrior", "Paladin", "Barbarian" }, 15, 50, 12),
            ("Scout's Boots", new[] { "Ranger", "Assassin" }, 10, 50, 9),
            ("Reinforced Boots", new[] { "All" }, 20, 50, 10),
            ("Chain Boots", new[] { "Warrior", "Paladin", "Cleric" }, 20, 60, 16),
            ("Silk Slippers", new[] { "Magician", "Sage" }, 10, 50, 6),
            ("Steel Sabatons", new[] { "Warrior", "Paladin" }, 35, 75, 28),
            ("Shadow Treads", new[] { "Assassin" }, 30, 70, 22),
            ("Forged Boots", new[] { "All" }, 35, 70, 26),
            ("Mithril Boots", new[] { "All" }, 50, 85, 42),
            ("Runed Boots", new[] { "All" }, 65, 100, 55),
            ("Traveler's Sandals", new[] { "Assassin", "Ranger" }, 20, 70, 15),
            ("Plate Sabatons", new[] { "Warrior", "Paladin" }, 60, 95, 52),
            ("Dragonhide Boots", new[] { "All" }, 80, 100, 70),
        };

        private static readonly List<(string Name, string[] Classes, int MinLevel, int MaxLevel, float BasePower)> WaistArmorTemplates = new()
        {
            ("Rope Belt", new[] { "All" }, 1, 20, 2),
            ("Leather Belt", new[] { "All" }, 5, 30, 4),
            ("Chain Belt", new[] { "Warrior", "Paladin", "Cleric" }, 15, 50, 10),
            ("Sash of Focus", new[] { "Magician", "Sage", "Assassin" }, 10, 50, 7),
            ("Reinforced Belt", new[] { "All" }, 20, 50, 10),
            ("War Belt", new[] { "Warrior", "Barbarian" }, 20, 60, 15),
            ("Thief's Girdle", new[] { "Assassin", "Ranger" }, 15, 55, 11),
            ("Forged Girdle", new[] { "All" }, 35, 70, 22),
            ("Steel Girdle", new[] { "Warrior", "Paladin" }, 35, 75, 25),
            ("Mithril Belt", new[] { "All" }, 50, 85, 38),
            ("Runed Belt", new[] { "All" }, 65, 100, 50),
            ("Holy Sash", new[] { "Paladin", "Cleric" }, 40, 80, 30),
            ("Titan's Belt", new[] { "Warrior", "Barbarian" }, 65, 100, 55),
            ("Dragonscale Belt", new[] { "All" }, 80, 100, 65),
        };

        private static readonly List<(string Name, string[] Classes, int MinLevel, int MaxLevel, float BasePower)> FaceArmorTemplates = new()
        {
            ("Cloth Mask", new[] { "All" }, 1, 25, 2),
            ("Leather Face Guard", new[] { "All" }, 10, 40, 6),
            ("Iron Visor", new[] { "Warrior", "Paladin", "Barbarian" }, 15, 55, 12),
            ("Shadow Mask", new[] { "Assassin" }, 15, 60, 10),
            ("War Mask", new[] { "Warrior", "Barbarian" }, 25, 65, 18),
            ("Mystic Veil", new[] { "Magician", "Sage", "Cleric" }, 20, 65, 12),
            ("Forged Visor", new[] { "All" }, 35, 70, 24),
            ("Steel Faceplate", new[] { "Warrior", "Paladin" }, 40, 80, 30),
            ("Mithril Visor", new[] { "All" }, 55, 90, 42),
            ("Runed Mask", new[] { "All" }, 65, 100, 52),
            ("Death Mask", new[] { "Assassin" }, 50, 90, 38),
            ("Dragon Visage", new[] { "All" }, 80, 100, 60),
        };

        private static readonly List<(string Name, string[] Classes, int MinLevel, int MaxLevel, float BasePower)> CloakArmorTemplates = new()
        {
            ("Tattered Cloak", new[] { "All" }, 1, 20, 2),
            ("Traveler's Cloak", new[] { "All" }, 5, 30, 5),
            ("Ranger's Cloak", new[] { "Ranger" }, 10, 50, 10),
            ("Shadow Cloak", new[] { "Assassin", "Ranger" }, 15, 55, 14),
            ("Wizard's Mantle", new[] { "Magician", "Sage" }, 15, 55, 10),
            ("Reinforced Cloak", new[] { "All" }, 20, 50, 12),
            ("War Cloak", new[] { "Warrior", "Paladin", "Barbarian" }, 20, 60, 16),
            ("Elven Cloak", new[] { "All" }, 30, 70, 25),
            ("Forged-Thread Cape", new[] { "All" }, 40, 75, 32),
            ("Cloak of Shadows", new[] { "Assassin" }, 40, 80, 32),
            ("Holy Shroud", new[] { "Paladin", "Cleric" }, 35, 75, 28),
            ("Mithril Weave Cloak", new[] { "All" }, 55, 90, 45),
            ("Cloak of the Archmage", new[] { "Magician", "Sage" }, 65, 100, 55),
            ("Dragonwing Cape", new[] { "All" }, 80, 100, 68),
        };

        private static readonly List<(string Name, string[] Classes, int MinLevel, int MaxLevel, float BasePower)> ShieldTemplates = new()
        {
            // Bucklers (light shields - usable by more classes)
            ("Wooden Buckler", new[] { "All" }, 1, 20, 3),
            ("Iron Buckler", new[] { "All" }, 8, 35, 8),
            ("Steel Buckler", new[] { "All" }, 15, 50, 14),
            ("Reinforced Shield", new[] { "All" }, 25, 60, 18),
            ("Duelist's Buckler", new[] { "Warrior", "Paladin", "Ranger", "Assassin" }, 30, 70, 22),
            ("Forged Shield", new[] { "All" }, 35, 70, 28),
            ("Elven Buckler", new[] { "All" }, 50, 85, 38),
            ("Runed Shield", new[] { "All" }, 65, 100, 50),
            ("Phantom Buckler", new[] { "Assassin", "Ranger" }, 70, 100, 55),

            // Standard Shields (balanced protection)
            ("Leather Shield", new[] { "All" }, 1, 25, 5),
            ("Wooden Shield", new[] { "All" }, 5, 30, 7),
            ("Iron Shield", new[] { "Warrior", "Paladin", "Cleric", "Barbarian" }, 10, 40, 11),
            ("Steel Shield", new[] { "Warrior", "Paladin", "Cleric", "Barbarian" }, 20, 55, 18),
            ("Knight's Shield", new[] { "Warrior", "Paladin" }, 30, 65, 25),
            ("Heater Shield", new[] { "Warrior", "Paladin", "Cleric" }, 40, 75, 32),
            ("Paladin's Shield", new[] { "Paladin", "Cleric" }, 50, 85, 40),
            ("Dragon Scale Shield", new[] { "Warrior", "Paladin" }, 60, 95, 52),
            ("Aegis", new[] { "Warrior", "Paladin" }, 75, 100, 65),

            // Tower Shields (heavy - strength classes)
            ("Tower Shield", new[] { "Warrior", "Paladin", "Barbarian" }, 25, 60, 28),
            ("Fortress Shield", new[] { "Warrior", "Paladin" }, 40, 80, 42),
            ("Wall of Faith", new[] { "Paladin" }, 55, 90, 55),
            ("Titan's Bulwark", new[] { "Warrior", "Barbarian" }, 65, 95, 65),
            ("Wall of Eternity", new[] { "Warrior", "Paladin" }, 80, 100, 80),
        };

        #endregion

        #region Accessory Templates

        private static readonly List<(string Name, int MinLevel, int MaxLevel, float BasePower)> RingTemplates = new()
        {
            // Basic rings
            ("Copper Ring", 1, 20, 3),
            ("Silver Ring", 5, 35, 6),
            ("Gold Ring", 15, 50, 10),
            ("Platinum Ring", 30, 70, 16),
            ("Mithril Ring", 50, 90, 25),
            ("Adamantine Ring", 75, 100, 40),

            // Themed rings
            ("Ring of Strength", 10, 60, 12),
            ("Ring of Protection", 10, 60, 12),
            ("Ring of Wisdom", 15, 70, 15),
            ("Ring of Power", 25, 80, 20),
            ("Ring of Vitality", 20, 75, 18),
            ("Ring of the Mage", 30, 85, 22),
            ("Signet Ring", 40, 90, 28),
            ("Band of Heroes", 55, 100, 35),
            ("Ring of the Hunter", 15, 65, 14),
            ("Marksman's Band", 35, 80, 22),
            ("Windstrike Ring", 55, 100, 35),
            ("Dragon's Eye Ring", 70, 100, 45),
            ("Archmage's Sigil", 85, 100, 55),
        };

        private static readonly List<(string Name, int MinLevel, int MaxLevel, float BasePower)> NecklaceTemplates = new()
        {
            // Basic necklaces
            ("Leather Cord", 1, 15, 2),
            ("Bone Necklace", 1, 25, 4),
            ("Silver Chain", 10, 40, 8),
            ("Gold Chain", 20, 55, 12),
            ("Jeweled Pendant", 30, 70, 18),
            ("Platinum Amulet", 45, 85, 26),
            ("Mithril Torc", 60, 100, 35),
            ("Adamantine Collar", 80, 100, 48),

            // Themed necklaces
            ("Amulet of Health", 15, 65, 14),
            ("Amulet of Warding", 20, 70, 16),
            ("Pendant of Might", 25, 75, 20),
            ("Talisman of Luck", 30, 80, 24),
            ("Medallion of Valor", 40, 85, 30),
            ("Eagle Eye Pendant", 20, 70, 18),
            ("Ranger's Talisman", 40, 85, 30),
            ("Necklace of Fireballs", 50, 95, 38),
            ("Amulet of the Planes", 65, 100, 45),
            ("Heart of the Dragon", 75, 100, 52),
            ("Tear of the Gods", 90, 100, 65),
        };

        #endregion

        #region Template Accessors (for ShopItemGenerator)

        // One-handed weapons are indices 0-60, two-handed start at index 61
        internal const int TwoHandedWeaponStartIndex = 61;

        internal static IReadOnlyList<(string Name, string[] Classes, int MinLevel, int MaxLevel, float BasePower)>
            GetWeaponTemplates() => WeaponTemplates;
        internal static IReadOnlyList<(string Name, string[] Classes, int MinLevel, int MaxLevel, float BasePower)>
            GetBodyArmorTemplates() => ArmorTemplates;
        internal static IReadOnlyList<(string Name, string[] Classes, int MinLevel, int MaxLevel, float BasePower)>
            GetHeadArmorTemplates() => HeadArmorTemplates;
        internal static IReadOnlyList<(string Name, string[] Classes, int MinLevel, int MaxLevel, float BasePower)>
            GetArmsArmorTemplates() => ArmsArmorTemplates;
        internal static IReadOnlyList<(string Name, string[] Classes, int MinLevel, int MaxLevel, float BasePower)>
            GetHandsArmorTemplates() => HandsArmorTemplates;
        internal static IReadOnlyList<(string Name, string[] Classes, int MinLevel, int MaxLevel, float BasePower)>
            GetLegsArmorTemplates() => LegsArmorTemplates;
        internal static IReadOnlyList<(string Name, string[] Classes, int MinLevel, int MaxLevel, float BasePower)>
            GetFeetArmorTemplates() => FeetArmorTemplates;
        internal static IReadOnlyList<(string Name, string[] Classes, int MinLevel, int MaxLevel, float BasePower)>
            GetWaistArmorTemplates() => WaistArmorTemplates;
        internal static IReadOnlyList<(string Name, string[] Classes, int MinLevel, int MaxLevel, float BasePower)>
            GetFaceArmorTemplates() => FaceArmorTemplates;
        internal static IReadOnlyList<(string Name, string[] Classes, int MinLevel, int MaxLevel, float BasePower)>
            GetCloakArmorTemplates() => CloakArmorTemplates;
        internal static IReadOnlyList<(string Name, string[] Classes, int MinLevel, int MaxLevel, float BasePower)>
            GetShieldTemplates() => ShieldTemplates;
        internal static IReadOnlyList<(string Name, int MinLevel, int MaxLevel, float BasePower)>
            GetRingTemplates() => RingTemplates;
        internal static IReadOnlyList<(string Name, int MinLevel, int MaxLevel, float BasePower)>
            GetNecklaceTemplates() => NecklaceTemplates;

        #endregion

        #region Main Generation Methods

        /// <summary>
        /// Generate a weapon drop for dungeon loot
        /// </summary>
        public static Item GenerateWeapon(int dungeonLevel, CharacterClass playerClass)
        {
            // Roll rarity based on dungeon level
            return GenerateWeaponWithRarity(dungeonLevel, playerClass, RollRarity(dungeonLevel));
        }

        /// <summary>
        /// Generate an armor drop for dungeon loot — randomly selects from ALL armor slots
        /// </summary>
        public static Item GenerateArmor(int dungeonLevel, CharacterClass playerClass)
        {
            return GenerateArmorWithRarity(dungeonLevel, playerClass, RollRarity(dungeonLevel));
        }

        /// <summary>
        /// Roll a random armor slot with weighted distribution.
        /// Returns the template list and ObjType for the selected slot.
        /// </summary>
        private static (List<(string Name, string[] Classes, int MinLevel, int MaxLevel, float BasePower)> templates, ObjType objType) RollArmorSlot()
        {
            // Weighted distribution: body favored, shields included, accessories rare
            double roll = random.NextDouble();
            if (roll < 0.22) return (ArmorTemplates, ObjType.Body);        // 22%
            if (roll < 0.32) return (HeadArmorTemplates, ObjType.Head);    // 10%
            if (roll < 0.40) return (ArmsArmorTemplates, ObjType.Arms);    // 8%
            if (roll < 0.48) return (HandsArmorTemplates, ObjType.Hands);  // 8%
            if (roll < 0.57) return (LegsArmorTemplates, ObjType.Legs);    // 9%
            if (roll < 0.66) return (FeetArmorTemplates, ObjType.Feet);    // 9%
            if (roll < 0.72) return (ShieldTemplates, ObjType.Shield);     // 6%
            if (roll < 0.79) return (WaistArmorTemplates, ObjType.Waist);  // 7%
            if (roll < 0.84) return (FaceArmorTemplates, ObjType.Face);    // 5%
            if (roll < 0.92) return (CloakArmorTemplates, ObjType.Abody);  // 8%
            if (roll < 0.96) return (new List<(string, string[], int, int, float)>(), ObjType.Fingers); // 4% — delegates to GenerateRing
            return (new List<(string, string[], int, int, float)>(), ObjType.Neck); // 4% — delegates to GenerateNecklace
        }

        /// <summary>
        /// Generate random loot (weapon or armor for any slot) for dungeon
        /// </summary>
        public static Item GenerateDungeonLoot(int dungeonLevel, CharacterClass playerClass)
        {
            // 45% weapon, 55% armor (distributed across all armor slots including accessories)
            if (random.NextDouble() < 0.45)
                return GenerateWeapon(dungeonLevel, playerClass);
            else
                return GenerateArmor(dungeonLevel, playerClass);
        }

        /// <summary>
        /// Generate a ring drop for dungeon loot
        /// </summary>
        public static Item GenerateRing(int dungeonLevel, ItemRarity? forcedRarity = null)
        {
            var rarity = forcedRarity ?? RollRarity(dungeonLevel);

            var candidates = RingTemplates
                .Where(r => dungeonLevel >= r.MinLevel && dungeonLevel <= r.MaxLevel)
                .ToList();

            if (candidates.Count == 0)
            {
                // Fallback to any ring
                candidates = RingTemplates.ToList();
            }

            var template = candidates[random.Next(candidates.Count)];
            return CreateAccessoryFromTemplate(template, dungeonLevel, rarity, ObjType.Fingers);
        }

        /// <summary>
        /// Generate a necklace drop for dungeon loot
        /// </summary>
        public static Item GenerateNecklace(int dungeonLevel, ItemRarity? forcedRarity = null)
        {
            var rarity = forcedRarity ?? RollRarity(dungeonLevel);

            var candidates = NecklaceTemplates
                .Where(n => dungeonLevel >= n.MinLevel && dungeonLevel <= n.MaxLevel)
                .ToList();

            if (candidates.Count == 0)
            {
                // Fallback to any necklace
                candidates = NecklaceTemplates.ToList();
            }

            var template = candidates[random.Next(candidates.Count)];
            return CreateAccessoryFromTemplate(template, dungeonLevel, rarity, ObjType.Neck);
        }

        /// <summary>
        /// Generate a shield drop for dungeon loot
        /// </summary>
        /// <param name="dungeonLevel"></param>
        /// <param name="forcedRarity"></param>
        /// <returns></returns>
        public static Item GenerateShield(int dungeonLevel, ItemRarity? forcedRarity = null)
        {
            var rarity = forcedRarity ?? RollRarity(dungeonLevel);

            var candidates = ShieldTemplates
                .Where(n => dungeonLevel >= n.MinLevel && dungeonLevel <= n.MaxLevel)
                .ToList();

            if (candidates.Count == 0)
            {
                // Fallback to any necklace
                candidates = ShieldTemplates.ToList();
            }
            int templateIndex = random.Next(candidates.Count);
            var template = candidates[templateIndex];
            WeaponType shieldType = 0;
            if (templateIndex < 9)
            {
                shieldType = WeaponType.Buckler;
            } else if (templateIndex < 18)
            {
                shieldType = WeaponType.Shield;
            } else
            {
                shieldType = WeaponType.TowerShield;
            }
            return CreateShieldFromTemplate(template, shieldType, dungeonLevel, rarity);
        }

        /// <summary>
        /// Generate loot specifically for mini-boss/champion monsters
        /// Mini-bosses ALWAYS drop equipment (weapon or armor for any slot)
        /// </summary>
        public static Item GenerateMiniBossLoot(int dungeonLevel, CharacterClass playerClass)
        {
            // Boost rarity for mini-boss drops (at least Uncommon, better chances for rare+)
            var rarity = RollRarity(dungeonLevel + 10); // +10 level bonus for rarity
            if (rarity == ItemRarity.Common)
                rarity = ItemRarity.Uncommon;

            // 35% weapon, 65% armor (distributed across all slots including rings/necklaces)
            if (random.NextDouble() < 0.35)
                return GenerateWeaponWithRarity(dungeonLevel, playerClass, rarity);
            else
                return GenerateArmorWithRarity(dungeonLevel, playerClass, rarity);
        }

        /// <summary>
        /// Generate loot for actual floor bosses (Old Gods, dungeon bosses)
        /// Bosses drop higher quality items (Epic+) with better stats
        /// </summary>
        public static Item GenerateBossLoot(int dungeonLevel, CharacterClass playerClass)
        {
            // Boss loot is always at least Epic rarity
            var rarity = RollRarity(dungeonLevel + 25); // +25 level bonus for rarity
            if (rarity < ItemRarity.Epic)
                rarity = ItemRarity.Epic;

            // 40% weapon, 60% armor (distributed across all slots including rings/necklaces)
            if (random.NextDouble() < 0.40)
                return GenerateWeaponWithRarity(dungeonLevel, playerClass, rarity);
            else
                return GenerateArmorWithRarity(dungeonLevel, playerClass, rarity);
        }

        /// <summary>
        /// Generate world boss exclusive loot with guaranteed minimum rarity and boss-specific effects.
        /// </summary>
        public static Item GenerateWorldBossLoot(int bossLevel, ItemRarity minRarity, string bossElement, CharacterClass playerClass)
        {
            // Roll rarity with +30 level bonus, then enforce minimum
            var rarity = RollRarity(bossLevel + 30);
            if (rarity < minRarity)
                rarity = minRarity;

            // 50/50 weapon vs armor
            Item item;
            if (random.NextDouble() < 0.50)
                item = GenerateWeaponWithRarity(bossLevel, playerClass, rarity);
            else
                item = GenerateArmorWithRarity(bossLevel, playerClass, rarity);

            // Add element-themed prefix from the boss
            string elementPrefix = UsurperRemake.Data.WorldBossDatabase.GetElementPrefix(bossElement);
            item.Name = $"{elementPrefix} {item.Name}";

            // Legendary+ items get BossSlayer effect (+10% damage to bosses)
            if (rarity >= ItemRarity.Legendary)
            {
                item.LootEffects ??= new List<(int, int)>();
                item.LootEffects.Add(((int)SpecialEffect.BossSlayer, 10));
            }

            // Epic+ armor gets TitanResolve effect (+5% max HP)
            if (rarity >= ItemRarity.Epic && item.Type != ObjType.Weapon)
            {
                item.LootEffects ??= new List<(int, int)>();
                item.LootEffects.Add(((int)SpecialEffect.TitanResolve, 5));
            }

            return item;
        }

        /// <summary>
        /// Generate a weapon with a specific rarity
        /// </summary>
        private static Item GenerateWeaponWithRarity(int dungeonLevel, CharacterClass playerClass, ItemRarity rarity)
        {
            // Find appropriate weapon templates
            // NG+ prestige classes can use any weapon
            bool isPrestige = playerClass >= CharacterClass.Tidesworn;
            var candidates = WeaponTemplates
                .Where(w => dungeonLevel >= w.MinLevel && dungeonLevel <= w.MaxLevel)
                .Where(w => isPrestige || w.Classes.Contains("All") || w.Classes.Contains(playerClass.ToString()))
                .ToList();

            if (candidates.Count == 0)
            {
                candidates = WeaponTemplates
                    .Where(w => dungeonLevel >= w.MinLevel && dungeonLevel <= w.MaxLevel)
                    .ToList();
            }

            if (candidates.Count == 0)
            {
                return CreateBasicWeapon(dungeonLevel, rarity);
            }

            var template = candidates[random.Next(candidates.Count)];
            return CreateWeaponFromTemplate(template, dungeonLevel, rarity);
        }

        /// <summary>
        /// Generate armor with a specific rarity — randomly selects from ALL armor slots
        /// </summary>
        private static Item GenerateArmorWithRarity(int dungeonLevel, CharacterClass playerClass, ItemRarity rarity)
        {
            // Roll a random armor slot with weighted distribution
            var (slotTemplates, objType) = RollArmorSlot();

            // Ring, necklace, and shield delegate to their specialized generators
            if (objType == ObjType.Fingers)
                return GenerateRing(dungeonLevel, rarity);
            if (objType == ObjType.Neck)
                return GenerateNecklace(dungeonLevel, rarity);
            if (objType == ObjType.Shield)
                return GenerateShield(dungeonLevel, rarity);

            bool isPrestige = playerClass >= CharacterClass.Tidesworn;
            var candidates = slotTemplates
                .Where(a => dungeonLevel >= a.MinLevel && dungeonLevel <= a.MaxLevel)
                .Where(a => isPrestige || a.Classes.Contains("All") || a.Classes.Contains(playerClass.ToString()))
                .ToList();

            if (candidates.Count == 0)
            {
                candidates = slotTemplates
                    .Where(a => dungeonLevel >= a.MinLevel && dungeonLevel <= a.MaxLevel)
                    .ToList();
            }

            if (candidates.Count == 0)
            {
                return CreateBasicArmor(dungeonLevel, rarity, objType);
            }

            var template = candidates[random.Next(candidates.Count)];
            return CreateArmorFromTemplate(template, dungeonLevel, rarity, objType);
        }

        #endregion

        #region Item Creation

        private static Item CreateWeaponFromTemplate(
            (string Name, string[] Classes, int MinLevel, int MaxLevel, float BasePower) template,
            int level,
            ItemRarity rarity)
        {
            var stats = RarityStats[rarity];

            // Calculate power with level scaling (v0.46.1: reduced from level/40 to level/80)
            // Base formula: basePower * (1 + level/80) * rarityMult
            float levelScale = 1.0f + (level / 80.0f);
            int basePower = (int)(template.BasePower * levelScale * stats.PowerMult);

            // Add randomness (±15%)
            int variance = (int)(basePower * 0.15f);
            int finalPower = basePower + random.Next(-variance, variance + 1);
            finalPower = Math.Max(5, finalPower);

            // Calculate value
            long value = (long)(finalPower * 15 * stats.ValueMult);

            // Roll for effects
            var effects = RollEffects(rarity, level, isWeapon: true);

            // Roll for curse
            bool isCursed = random.NextDouble() < stats.CurseChance;

            // Build item name
            string name = BuildItemName(template.Name, rarity, effects, isCursed);

            // Create the item
            var item = new Item
            {
                Name = name,
                Type = ObjType.Weapon,
                Value = value,
                Attack = finalPower,
                MinLevel = Math.Max(1, level - 10),
                Cursed = isCursed,
                IsCursed = isCursed,
                Shop = false,
                Dungeon = true
            };

            // Apply effects to item stats
            ApplyEffectsToItem(item, effects, isWeapon: true);

            // Apply name-based thematic stat bonuses (e.g., staves get INT, holy weapons get WIS)
            ApplyWeaponThematicBonuses(item, item.Name, finalPower);

            // If cursed, add penalties but increase power
            if (isCursed)
            {
                item.Attack = (int)(item.Attack * 1.25f); // Cursed items are 25% stronger
                item.Value = (long)(item.Value * 0.5f);   // But worth less
                ApplyCursePenalties(item);
            }

            // Rare+ dungeon drops may be unidentified
            item.IsIdentified = !ShouldBeUnidentified(rarity);

            return item;
        }

        private static Item CreateArmorFromTemplate(
            (string Name, string[] Classes, int MinLevel, int MaxLevel, float BasePower) template,
            int level,
            ItemRarity rarity,
            ObjType armorType = ObjType.Body)
        {
            var stats = RarityStats[rarity];

            float levelScale = 1.0f + (level / 80.0f);
            int basePower = (int)(template.BasePower * levelScale * stats.PowerMult);

            int variance = (int)(basePower * 0.15f);
            int finalPower = basePower + random.Next(-variance, variance + 1);
            finalPower = Math.Max(3, finalPower);

            long value = (long)(finalPower * 20 * stats.ValueMult);

            var effects = RollEffects(rarity, level, isWeapon: false);
            bool isCursed = random.NextDouble() < stats.CurseChance;

            string name = BuildItemName(template.Name, rarity, effects, isCursed);

            var item = new Item
            {
                Name = name,
                Type = armorType,
                Value = value,
                Armor = finalPower,
                MinLevel = Math.Max(1, level - 10),
                Cursed = isCursed,
                IsCursed = isCursed,
                Shop = false,
                Dungeon = true
            };

            ApplyEffectsToItem(item, effects, isWeapon: false);

            // Apply stat bonuses based on full item name including effect suffixes
            // (e.g., "Leather Gloves of the Arcane" → +INT, not ranger stats from "Leather Gloves")
            ApplyThematicBonuses(item, item.Name, finalPower);

            if (isCursed)
            {
                item.Armor = (int)(item.Armor * 1.25f);
                item.Value = (long)(item.Value * 0.5f);
                ApplyCursePenalties(item);
            }

            // Rare+ dungeon drops may be unidentified
            item.IsIdentified = !ShouldBeUnidentified(rarity);

            return item;
        }

        /// <summary>
        /// Applies stat bonuses based on template name keywords, giving armor pieces
        /// thematic identity beyond their base defense value.
        /// </summary>
        internal static void ApplyThematicBonuses(Item item, string templateName, int finalPower)
        {
            var name = templateName.ToLowerInvariant();

            int primaryStat = Math.Max(1, finalPower / 5);
            int secondaryStat = Math.Max(1, finalPower / 7);
            int hpBonus = finalPower;
            int manaBonus = Math.Max(1, finalPower * 3 / 4);

            // Holy/Divine themed → Wisdom + Constitution + Defence
            if (name.Contains("holy") || name.Contains("sacred") || name.Contains("blessed") ||
                     name.Contains("divine") || name.Contains("faith") || name.Contains("priest") ||
                     name.Contains("diadem") || name.Contains("paladin") || name.Contains("celestial") ||
                     name.Contains("light"))
            {
                item.Wisdom += primaryStat;
                item.LootEffects.Add(((int)SpecialEffect.Constitution, hpBonus / 5));
                item.Defence += Math.Max(1, finalPower / 9);
            }
            // Caster/Focus themed → Intelligence + Defence
            else if (name.Contains("focus") || name.Contains("wizard") || name.Contains("archmage") ||
                name.Contains("arcane") || name.Contains("enchanted") || name.Contains("mystic") ||
                name.Contains("mage") || name.Contains("silk") || name.Contains("cloth") ||
                name.Contains("robe") || name.Contains("vestment") || name.Contains("sorcery") ||
                name.Contains("runed") || name.Contains("mind"))
            {
                item.LootEffects.Add(((int)SpecialEffect.Intelligence, primaryStat + manaBonus / 3));
                item.Defence += Math.Max(1, finalPower / 9);
            }
            // Shadow/Stealth themed → Dexterity + Agility
            else if (name.Contains("shadow") || name.Contains("thief") || name.Contains("night ") ||
                     name.Contains("stalker") || name.Contains("death") || name.Contains("phantom") ||
                     name.Contains("assassin") || name.Contains("rogue") || name.Contains("darkness"))
            {
                item.Dexterity += primaryStat;
                item.Agility += secondaryStat;
            }
            // Warrior/Battle themed → Strength + Constitution
            else if (name.Contains("war") || name.Contains("battle") || name.Contains("titan") ||
                     name.Contains("berserker") || name.Contains("barbarian") || name.Contains("spiked") ||
                     name.Contains("knight") || name.Contains("gladiator") || name.Contains("champion") ||
                     name.Contains("fighter") || name.Contains("fortress") || name.Contains("aegis") ||
                     name.Contains("tower") || name.Contains("crusher") || name.Contains("destroyer"))
            {
                item.Strength += primaryStat;
                item.LootEffects.Add(((int)SpecialEffect.Constitution, hpBonus / 5));
            }
            // Dragon themed → Strength + Defence + Constitution
            else if (name.Contains("dragon"))
            {
                item.Strength += secondaryStat;
                item.Defence += secondaryStat;
                item.LootEffects.Add(((int)SpecialEffect.Constitution, hpBonus / 10));
            }
            // Ranger/Scout/Elven themed → Dexterity + Agility + Defence
            else if (name.Contains("ranger") || name.Contains("scout") || name.Contains("elven") ||
                     name.Contains("forest") || name.Contains("traveler") || name.Contains("leather"))
            {
                item.Dexterity += primaryStat;
                item.Agility += secondaryStat;
                item.Defence += Math.Max(1, finalPower / 9);
            }
            // Martial/Agile themed → Dexterity + Agility (gi, duelist, precision, agility)
            else if (name.EndsWith(" gi") || name.Contains("duelist") || name.Contains("precision") ||
                     name.Contains("agility") || name.Contains("nimble"))
            {
                item.Dexterity += primaryStat;
                item.Agility += secondaryStat;
            }
            // Vitality/Endurance themed → Constitution
            else if (name.Contains("vitality") || name.Contains("endurance") || name.Contains("resilience") ||
                     name.Contains("fortitude") || name.Contains("vigor") || name.Contains("stalwart") ||
                     name.Contains("robust") || name.Contains("healing"))
            {
                item.LootEffects.Add(((int)SpecialEffect.Constitution, primaryStat + hpBonus / 5));
            }
            // Premium material themed → Constitution + Defence
            else if (name.Contains("mithril") || name.Contains("adamantine"))
            {
                item.LootEffects.Add(((int)SpecialEffect.Constitution, hpBonus / 5));
                item.Defence += secondaryStat;
            }
            // Metal/Forged/Crafted armor themed → Defence + Constitution
            else if (name.Contains("iron") || name.Contains("steel") || name.Contains("chain") ||
                     name.Contains("plate") || name.Contains("banded") || name.Contains("splint") ||
                     name.Contains("studded") || name.Contains("reinforced") || name.Contains("forged") ||
                     name.Contains("ancient") || name.Contains("brigandine") || name.Contains("hauberk") ||
                     name.Contains("heater") || name.Contains("wooden") || name.Contains("bone") ||
                     name.Contains("copper") || name.Contains("gold") || name.Contains("silver"))
            {
                item.Defence += secondaryStat;
                item.LootEffects.Add(((int)SpecialEffect.Constitution, hpBonus / 10));
            }
        }

        /// <summary>
        /// Applies stat bonuses to weapons based on the final item name (template + effect suffixes).
        /// Ensures weapons like "Staff of Sorcery" get INT, "Holy Avenger" gets WIS, etc.
        /// Uses the full built name so effect suffixes like "of Sorcery" are matched.
        /// </summary>
        internal static void ApplyWeaponThematicBonuses(Item item, string weaponName, int finalPower)
        {
            var name = weaponName.ToLowerInvariant();

            int primaryStat = Math.Max(1, finalPower / 6);
            int secondaryStat = Math.Max(1, finalPower / 9);

            // Caster/Sorcery themed → Intelligence + Wisdom + Defence
            // Matches: staves, "of Sorcery" suffix, archmage, magic, mage weapons, "of the Arcane", "of the Mind"
            if (name.Contains("staff") || name.Contains("sorcery") || name.Contains("archmage") ||
                name.Contains("magic") || name.Contains("mage") || name.Contains("soulstaff") ||
                name.Contains("cosmos") || name.Contains("transmuter") || name.Contains("arcane") ||
                name.Contains("mind") || name.Contains("runed") || name.Contains("void"))
            {
                item.LootEffects.Add(((int)SpecialEffect.Intelligence, primaryStat));
                item.Wisdom += secondaryStat;
                item.Defence += Math.Max(1, finalPower / 12);
            }
            // Holy/Divine themed → Wisdom + Constitution + Defence
            // Matches: holy weapons, paladin swords, blessed, scepter, righteous, "of Light"
            else if (name.Contains("holy") || name.Contains("sacred") || name.Contains("blessed") ||
                     name.Contains("divine") || name.Contains("scepter") || name.Contains("righteous") ||
                     name.Contains("judgment") || name.Contains("avenger") || name.Contains("celestial") ||
                     name.Contains("light"))
            {
                item.Wisdom += primaryStat;
                item.LootEffects.Add(((int)SpecialEffect.Constitution, finalPower / 8));
                item.Defence += Math.Max(1, finalPower / 12);
            }
            // Shadow/Assassin themed → Dexterity + Agility
            // Matches: "of Darkness", "of Venom"
            else if (name.Contains("shadow") || name.Contains("assassin") || name.Contains("phantom") ||
                     name.Contains("night") || name.Contains("death") || name.Contains("venom") ||
                     name.Contains("darkness"))
            {
                item.Dexterity += primaryStat;
                item.Agility += secondaryStat;
            }
            // Bow/Ranger themed → Dexterity + Agility + Defence
            else if (name.Contains("bow") || name.Contains("ranger") || name.Contains("hunter") ||
                     name.Contains("marksman") || name.Contains("archer") || name.Contains("eagle") ||
                     name.Contains("windstrike") || name.Contains("sniper"))
            {
                item.Dexterity += primaryStat;
                item.Agility += secondaryStat;
                item.Defence += Math.Max(1, finalPower / 12);
            }
            // Warrior/Battle themed → Strength
            // Matches: heavy warrior weapons, "Crusher", "Earthshaker", etc.
            else if (name.Contains("berserker") || name.Contains("titan") || name.Contains("executioner") ||
                     name.Contains("annihilator") || name.Contains("bloodreaver") || name.Contains("dragonslayer") ||
                     name.Contains("demon") || name.Contains("crusher") || name.Contains("destroyer") ||
                     name.Contains("earthshaker") || name.Contains("skullbreaker") || name.Contains("sledge") ||
                     name.Contains("maul") || name.Contains("rending"))
            {
                item.Strength += primaryStat;
            }
            // Bard/Music themed → Charisma + Dexterity + Defence
            else if (name.Contains("song") || name.Contains("lute") || name.Contains("lyre") ||
                     name.Contains("harp") || name.Contains("horn") || name.Contains("flute") ||
                     name.Contains("drum") || name.Contains("opus") || name.Contains("virtuoso"))
            {
                item.Charisma += primaryStat;
                item.Dexterity += secondaryStat;
                item.Defence += Math.Max(1, finalPower / 12);
            }
            // Alchemist themed → Intelligence + Constitution + Defence
            else if (name.Contains("alchemist") || name.Contains("elixir") || name.Contains("philosopher") ||
                     name.Contains("pestle"))
            {
                item.LootEffects.Add(((int)SpecialEffect.Intelligence, secondaryStat));
                item.LootEffects.Add(((int)SpecialEffect.Constitution, finalPower / 10));
                item.Defence += Math.Max(1, finalPower / 12);
            }
            // Precision/Agility themed → Dexterity + Agility
            // Matches: "of Precision", "of Agility", nimble prefix, rapier, dueling
            else if (name.Contains("precision") || name.Contains("agility") || name.Contains("nimble") ||
                     name.Contains("rapier") || name.Contains("dueling") || name.Contains("stiletto") ||
                     name.Contains("dagger") || name.Contains("keen"))
            {
                item.Dexterity += primaryStat;
                item.Agility += secondaryStat;
            }
            // Dragon themed → Strength + Defence
            else if (name.Contains("dragon"))
            {
                item.Strength += primaryStat;
                item.Defence += Math.Max(1, finalPower / 12);
            }
        }

        private static Item CreateAccessoryFromTemplate(
            (string Name, int MinLevel, int MaxLevel, float BasePower) template,
            int level,
            ItemRarity rarity,
            ObjType accessoryType)
        {
            var stats = RarityStats[rarity];

            float levelScale = 1.0f + (level / 80.0f);
            int basePower = (int)(template.BasePower * levelScale * stats.PowerMult);

            int variance = (int)(basePower * 0.15f);
            int finalPower = basePower + random.Next(-variance, variance + 1);
            finalPower = Math.Max(2, finalPower);

            // Accessories are worth more per power point
            long value = (long)(finalPower * 30 * stats.ValueMult);

            // Accessories get more stat-focused effects
            var effects = RollEffects(rarity, level, isWeapon: false);
            bool isCursed = random.NextDouble() < stats.CurseChance;

            string name = BuildItemName(template.Name, rarity, effects, isCursed);

            var item = new Item
            {
                Name = name,
                Type = accessoryType,
                Value = value,
                MinLevel = Math.Max(1, level - 10),
                Cursed = isCursed,
                IsCursed = isCursed,
                Shop = false,
                Dungeon = true
            };

            // Accessories primarily give stat bonuses rather than attack/armor
            // Use FINAL item name (with effect suffixes) so "Silver Ring of the Arcane" matches "arcane"
            // and gets INT/WIS instead of falling through to the generic Str/Dex fallback
            string lowerName = name.ToLowerInvariant();
            // Wisdom/Insight themed (includes "of Insight" suffix from Wisdom effect)
            if (lowerName.Contains("wisdom") || lowerName.Contains("insight") || lowerName.Contains("wise"))
            {
                item.Wisdom += finalPower / 2;
                item.LootEffects.Add(((int)SpecialEffect.Intelligence, finalPower / 3));
                item.LootEffects.Add(((int)SpecialEffect.Constitution, finalPower / 5));
                item.Defence += Math.Max(1, finalPower / 10);
            }
            // Strength/Power themed (includes "of Strength" suffix, "of Power" suffix)
            else if (lowerName.Contains("strength") || lowerName.Contains("power") || lowerName.Contains("heroes") ||
                     lowerName.Contains("mighty"))
            {
                item.Strength += finalPower / 2;
                item.LootEffects.Add(((int)SpecialEffect.Constitution, finalPower * 2 / 5));
            }
            // Protection/Shielding themed (includes "of Protection" suffix, "of Shielding" suffix, "of the Sentinel" suffix)
            else if (lowerName.Contains("protection") || lowerName.Contains("ward") || lowerName.Contains("shield") ||
                     lowerName.Contains("sentinel"))
            {
                item.Defence += finalPower / 3;
                item.LootEffects.Add(((int)SpecialEffect.Constitution, finalPower * 2 / 5));
            }
            // Vitality/Health themed (includes "of Vitality" suffix, "of Healing" suffix, "of Fortitude" suffix)
            else if (lowerName.Contains("vitality") || lowerName.Contains("life") || lowerName.Contains("health") ||
                     lowerName.Contains("healing") || lowerName.Contains("fortitude") || lowerName.Contains("stalwart") ||
                     lowerName.Contains("robust") || lowerName.Contains("colossus"))
            {
                item.LootEffects.Add(((int)SpecialEffect.Constitution, finalPower * 3 / 5));
            }
            // Might/Valor themed
            else if (lowerName.Contains("might") || lowerName.Contains("valor"))
            {
                item.Strength += finalPower / 3;
                item.Dexterity += finalPower / 4;
                item.LootEffects.Add(((int)SpecialEffect.Constitution, finalPower / 5));
            }
            // Luck/Fortune themed
            else if (lowerName.Contains("luck") || lowerName.Contains("fortune"))
            {
                item.Dexterity += finalPower / 3;
                item.Charisma += finalPower / 3;
                item.LootEffects.Add(((int)SpecialEffect.Constitution, finalPower / 5));
            }
            // Caster themed (includes "of the Arcane", "of Sorcery", "of the Mind", "Sage" prefix)
            else if (lowerName.Contains("fireball") || lowerName.Contains("planes") || lowerName.Contains("gods") ||
                     lowerName.Contains("mage") || lowerName.Contains("archmage") || lowerName.Contains("arcane") ||
                     lowerName.Contains("sorcery") || lowerName.Contains("sigil") || lowerName.Contains("mind") ||
                     lowerName.Contains("sage") || lowerName.Contains("mystic") || lowerName.Contains("enchanted"))
            {
                item.LootEffects.Add(((int)SpecialEffect.Intelligence, finalPower * 2 / 3));
                item.Wisdom += finalPower / 4;
                item.Defence += Math.Max(1, finalPower / 10);
            }
            // Holy/Light themed (includes "of Light" suffix from HolyDamage)
            else if (lowerName.Contains("holy") || lowerName.Contains("light") || lowerName.Contains("sacred") ||
                     lowerName.Contains("blessed") || lowerName.Contains("divine") || lowerName.Contains("celestial"))
            {
                item.Wisdom += finalPower / 3;
                item.LootEffects.Add(((int)SpecialEffect.Constitution, finalPower / 4));
                item.Defence += Math.Max(1, finalPower / 8);
            }
            // Shadow/Darkness themed (includes "of Darkness" suffix from ShadowDamage)
            else if (lowerName.Contains("shadow") || lowerName.Contains("darkness") || lowerName.Contains("phantom") ||
                     lowerName.Contains("death") || lowerName.Contains("night"))
            {
                item.Dexterity += finalPower / 3;
                item.Agility += finalPower / 4;
                item.Defence += Math.Max(1, finalPower / 8);
            }
            // Dragon themed
            else if (lowerName.Contains("dragon"))
            {
                item.Strength += finalPower / 4;
                item.Defence += finalPower / 4;
                item.LootEffects.Add(((int)SpecialEffect.Constitution, finalPower * 2 / 5));
            }
            // Ranger/Hunter themed
            else if (lowerName.Contains("hunter") || lowerName.Contains("marksman") || lowerName.Contains("windstrike") ||
                     lowerName.Contains("eagle") || lowerName.Contains("ranger") || lowerName.Contains("sniper"))
            {
                item.Dexterity += finalPower / 2;
                item.Agility += finalPower / 3;
                item.Defence += Math.Max(1, finalPower / 8);
            }
            // Agility/Precision themed (includes "of Agility" suffix, "of Precision" suffix, "Nimble" prefix, "Keen" prefix)
            else if (lowerName.Contains("agility") || lowerName.Contains("precision") || lowerName.Contains("nimble") ||
                     lowerName.Contains("keen"))
            {
                item.Dexterity += finalPower / 3;
                item.Agility += finalPower / 3;
            }
            // Elemental themed — fire/ice/lightning (includes "of Flames", "of Frost", "of Thunder")
            else if (lowerName.Contains("flame") || lowerName.Contains("blaz") || lowerName.Contains("fire") ||
                     lowerName.Contains("frost") || lowerName.Contains("frozen") || lowerName.Contains("ice") ||
                     lowerName.Contains("thunder") || lowerName.Contains("lightning") || lowerName.Contains("shock") ||
                     lowerName.Contains("storm") || lowerName.Contains("salamander") || lowerName.Contains("yeti"))
            {
                item.LootEffects.Add(((int)SpecialEffect.Intelligence, finalPower / 3));
                item.Defence += Math.Max(1, finalPower / 8);
            }
            // Runed/Ancient themed
            else if (lowerName.Contains("runed") || lowerName.Contains("ancient"))
            {
                item.LootEffects.Add(((int)SpecialEffect.Intelligence, finalPower / 3));
                item.Wisdom += finalPower / 5;
                item.Defence += Math.Max(1, finalPower / 10);
            }
            // Premium materials
            else if (lowerName.Contains("mithril") || lowerName.Contains("adamantine") || lowerName.Contains("platinum"))
            {
                item.Defence += finalPower / 4;
                item.LootEffects.Add(((int)SpecialEffect.Constitution, finalPower / 3));
            }
            // Generic fallbacks — basic material rings/necklaces with no themed suffix
            else if (accessoryType == ObjType.Fingers)
            {
                item.Strength += finalPower / 4;
                item.Dexterity += finalPower / 4;
                item.LootEffects.Add(((int)SpecialEffect.Constitution, finalPower * 2 / 5));
            }
            else if (accessoryType == ObjType.Neck)
            {
                item.Wisdom += finalPower / 3;
                item.LootEffects.Add(((int)SpecialEffect.Intelligence, finalPower * 2 / 3));
                item.LootEffects.Add(((int)SpecialEffect.Constitution, finalPower / 5));
                item.Defence += Math.Max(1, finalPower / 10);
            }

            ApplyEffectsToItem(item, effects, isWeapon: false);

            if (isCursed)
            {
                // Cursed accessories have higher stats but penalties
                item.Strength = (int)(item.Strength * 1.3f);
                item.Value = (long)(item.Value * 0.5f);
                ApplyCursePenalties(item);
            }

            // Rare+ dungeon drops may be unidentified
            item.IsIdentified = !ShouldBeUnidentified(rarity);

            return item;
        }

        /// <summary>
        /// Generate semi-randomized shield stats derived from authored shield data			
        ///
        /// Rolled artifact stats cap at 5 below the unique items in their class to preserve their awesomeness
        /// </summary>
        /// Might be good to weight the odds of better stats based on power, but out of scope for now
        /// Power influences stat bonuses and value anyway so still serves a purpose without that
        /// <returns></returns>
        private static (int shieldBonus, int blockChance) RollShieldStats(WeaponType shieldType, int power, ItemRarity rarity)
        {
        return shieldType switch
        {
            WeaponType.Buckler => rarity switch
            {
                ItemRarity.Common => (random.Next(1, 7), random.Next(9, 17)),
                ItemRarity.Uncommon => (random.Next(7, 12), random.Next(17, 22)),
                ItemRarity.Rare => (random.Next(12, 17), random.Next(22, 27)),
                ItemRarity.Epic => (random.Next(17, 23), random.Next(27, 33)),
                ItemRarity.Legendary => (random.Next(23, 28), random.Next(33, 38)),
                ItemRarity.Artifact => (random.Next(29 - 36), random.Next(39 - 41)),
                _ => (random.Next(1, 36), random.Next(9, 41)),
            },
            WeaponType.Shield => rarity switch
            {
                ItemRarity.Common => (random.Next(2, 9), random.Next(11, 19)),
                ItemRarity.Uncommon => (random.Next(9, 15), random.Next(19, 24)),
                ItemRarity.Rare => (random.Next(15, 18), random.Next(24, 26)),
                ItemRarity.Epic => (random.Next(18, 28), random.Next(25, 34)),
                ItemRarity.Legendary => (random.Next(28, 37), random.Next(34, 40)),
                ItemRarity.Artifact => (random.Next(37, 46), random.Next(39, 46)),
                _ => (random.Next(2, 46), random.Next(11, 46)),
            },
            WeaponType.TowerShield => rarity switch
            {
                // No authored tower shields below Rare
                // In case they get rolled, may as well default to shield stats
                ItemRarity.Common => (random.Next(2, 9), random.Next(11, 19)),
                ItemRarity.Uncommon => (random.Next(9, 15), random.Next(19, 24)),
                ItemRarity.Rare => (random.Next(21, 27), random.Next(29, 34)),
                ItemRarity.Epic => (random.Next(27, 34), random.Next(34, 39)),
                ItemRarity.Legendary => (random.Next(34, 40), random.Next(39, 44)),
                ItemRarity.Artifact => (random.Next(39, 51), random.Next(44, 51)),
                _ => (random.Next(2, 51), random.Next(11, 51)),
            },
            // Just in case of a mishap in handling the shield type, default to lowest stats (buckler)
            _ => (random.Next(1, 35), random.Next(9, 40)),
        };
    }

        private static Item CreateShieldFromTemplate(
            (string Name, string[] Classes, int MinLevel, int MaxLevel, float BasePower) template,
            WeaponType shieldType,
            int level,
            ItemRarity rarity)
        {
            var (PowerMult, ValueMult, MinEffects, MaxEffects, CurseChance) = RarityStats[rarity];

            float levelScale = 1.0f + (level / 80.0f);
            int basePower = (int)(template.BasePower * levelScale * PowerMult);

            int variance = (int)(basePower * 0.15f);
            int finalPower = basePower + random.Next(-variance, variance + 1);
            finalPower = Math.Max(2, finalPower);

            var (shieldBonus, blockChance) = RollShieldStats(shieldType, finalPower, rarity);

            // Shields are worth about as much as weapons
            long value = (long)(finalPower * 15 * ValueMult);

            // Shields get more stat/defensive effects
            var effects = RollEffects(rarity, level, isWeapon: false);
            bool isCursed = random.NextDouble() < CurseChance;

            string name = BuildItemName(template.Name, rarity, effects, isCursed);

            var item = new Item
            {
                Name = name,
                Type = ObjType.Shield,
                Value = value,
                MinLevel = Math.Max(1, level - 10),
                ShieldBonus = shieldBonus,
                BlockChance = blockChance,
                Cursed = isCursed,
                IsCursed = isCursed,
                Shop = false,
                Dungeon = true
            };

            // For now, shields can use the same effect math as accessories. Maybe these can be tweaked for balance later
            string lowerName = name.ToLowerInvariant();
            // Wisdom/Insight themed (includes "of Insight" suffix from Wisdom effect)
            if (lowerName.Contains("wisdom") || lowerName.Contains("insight") || lowerName.Contains("wise"))
            {
                item.Wisdom += finalPower / 2;
                item.LootEffects.Add(((int)SpecialEffect.Intelligence, finalPower / 3));
                item.LootEffects.Add(((int)SpecialEffect.Constitution, finalPower / 5));
                item.Defence += Math.Max(1, finalPower / 10);
            }
            // Strength/Power themed (includes "of Strength" suffix, "of Power" suffix)
            else if (lowerName.Contains("strength") || lowerName.Contains("power") || lowerName.Contains("heroes") ||
                     lowerName.Contains("mighty"))
            {
                item.Strength += finalPower / 2;
                item.LootEffects.Add(((int)SpecialEffect.Constitution, finalPower * 2 / 5));
            }
            // Protection/Shielding themed (includes "of Protection" suffix, "of Shielding" suffix, "of the Sentinel" suffix)
            else if (lowerName.Contains("protection") || lowerName.Contains("ward") || lowerName.Contains("shield") ||
                     lowerName.Contains("sentinel"))
            {
                item.Defence += finalPower / 3;
                item.LootEffects.Add(((int)SpecialEffect.Constitution, finalPower * 2 / 5));
            }
            // Vitality/Health themed (includes "of Vitality" suffix, "of Healing" suffix, "of Fortitude" suffix)
            else if (lowerName.Contains("vitality") || lowerName.Contains("life") || lowerName.Contains("health") ||
                     lowerName.Contains("healing") || lowerName.Contains("fortitude") || lowerName.Contains("stalwart") ||
                     lowerName.Contains("robust") || lowerName.Contains("colossus"))
            {
                item.LootEffects.Add(((int)SpecialEffect.Constitution, finalPower * 3 / 5));
            }
            // Might/Valor themed
            else if (lowerName.Contains("might") || lowerName.Contains("valor"))
            {
                item.Strength += finalPower / 3;
                item.Dexterity += finalPower / 4;
                item.LootEffects.Add(((int)SpecialEffect.Constitution, finalPower / 5));
            }
            // Luck/Fortune themed
            else if (lowerName.Contains("luck") || lowerName.Contains("fortune"))
            {
                item.Dexterity += finalPower / 3;
                item.Charisma += finalPower / 3;
                item.LootEffects.Add(((int)SpecialEffect.Constitution, finalPower / 5));
            }
            // Caster themed (includes "of the Arcane", "of Sorcery", "of the Mind", "Sage" prefix)
            else if (lowerName.Contains("fireball") || lowerName.Contains("planes") || lowerName.Contains("gods") ||
                     lowerName.Contains("mage") || lowerName.Contains("archmage") || lowerName.Contains("arcane") ||
                     lowerName.Contains("sorcery") || lowerName.Contains("sigil") || lowerName.Contains("mind") ||
                     lowerName.Contains("sage") || lowerName.Contains("mystic") || lowerName.Contains("enchanted"))
            {
                item.LootEffects.Add(((int)SpecialEffect.Intelligence, finalPower * 2 / 3));
                item.Wisdom += finalPower / 4;
                item.Defence += Math.Max(1, finalPower / 10);
            }
            // Holy/Light themed (includes "of Light" suffix from HolyDamage)
            else if (lowerName.Contains("holy") || lowerName.Contains("light") || lowerName.Contains("sacred") ||
                     lowerName.Contains("blessed") || lowerName.Contains("divine") || lowerName.Contains("celestial"))
            {
                item.Wisdom += finalPower / 3;
                item.LootEffects.Add(((int)SpecialEffect.Constitution, finalPower / 4));
                item.Defence += Math.Max(1, finalPower / 8);
            }
            // Shadow/Darkness themed (includes "of Darkness" suffix from ShadowDamage)
            else if (lowerName.Contains("shadow") || lowerName.Contains("darkness") || lowerName.Contains("phantom") ||
                     lowerName.Contains("death") || lowerName.Contains("night"))
            {
                item.Dexterity += finalPower / 3;
                item.Agility += finalPower / 4;
                item.Defence += Math.Max(1, finalPower / 8);
            }
            // Dragon themed
            else if (lowerName.Contains("dragon"))
            {
                item.Strength += finalPower / 4;
                item.Defence += finalPower / 4;
                item.LootEffects.Add(((int)SpecialEffect.Constitution, finalPower * 2 / 5));
            }
            // Ranger/Hunter themed
            else if (lowerName.Contains("hunter") || lowerName.Contains("marksman") || lowerName.Contains("windstrike") ||
                     lowerName.Contains("eagle") || lowerName.Contains("ranger") || lowerName.Contains("sniper"))
            {
                item.Dexterity += finalPower / 2;
                item.Agility += finalPower / 3;
                item.Defence += Math.Max(1, finalPower / 8);
            }
            // Agility/Precision themed (includes "of Agility" suffix, "of Precision" suffix, "Nimble" prefix, "Keen" prefix)
            else if (lowerName.Contains("agility") || lowerName.Contains("precision") || lowerName.Contains("nimble") ||
                     lowerName.Contains("keen"))
            {
                item.Dexterity += finalPower / 3;
                item.Agility += finalPower / 3;
            }
            // Elemental themed — fire/ice/lightning (includes "of Flames", "of Frost", "of Thunder")
            else if (lowerName.Contains("flame") || lowerName.Contains("blaz") || lowerName.Contains("fire") ||
                     lowerName.Contains("frost") || lowerName.Contains("frozen") || lowerName.Contains("ice") ||
                     lowerName.Contains("thunder") || lowerName.Contains("lightning") || lowerName.Contains("shock") ||
                     lowerName.Contains("storm") || lowerName.Contains("salamander") || lowerName.Contains("yeti"))
            {
                item.LootEffects.Add(((int)SpecialEffect.Intelligence, finalPower / 3));
                item.Defence += Math.Max(1, finalPower / 8);
            }
            // Runed/Ancient themed
            else if (lowerName.Contains("runed") || lowerName.Contains("ancient"))
            {
                item.LootEffects.Add(((int)SpecialEffect.Intelligence, finalPower / 3));
                item.Wisdom += finalPower / 5;
                item.Defence += Math.Max(1, finalPower / 10);
            }
            // Premium materials
            else if (lowerName.Contains("mithril") || lowerName.Contains("adamantine") || lowerName.Contains("platinum"))
            {
                item.Defence += finalPower / 4;
                item.LootEffects.Add(((int)SpecialEffect.Constitution, finalPower / 3));
            }
            // Generic fallbacks — basic material rings/necklaces with no themed suffix
            else
            {
                item.Strength += finalPower / 4;
                item.Dexterity += finalPower / 4;
                item.LootEffects.Add(((int)SpecialEffect.Constitution, finalPower * 2 / 5));
            }

            ApplyEffectsToItem(item, effects, isWeapon: false);

            if (isCursed)
            {
                // Cursed accessories have higher stats but penalties
                item.Strength = (int)(item.Strength * 1.3f);
                item.Value = (long)(item.Value * 0.5f);
                ApplyCursePenalties(item);
            }

            // Rare+ dungeon drops may be unidentified
            item.IsIdentified = !ShouldBeUnidentified(rarity);

            return item;
        }

        private static Item CreateBasicWeapon(int level, ItemRarity rarity)
        {
            var stats = RarityStats[rarity];
            float levelScale = 1.0f + (level / 40.0f);
            int power = (int)(10 * levelScale * stats.PowerMult);

            return new Item
            {
                Name = $"{GetRarityPrefix(rarity)}{Loc.Get("item.slot.weapon")}",
                Type = ObjType.Weapon,
                Value = power * 15,
                Attack = power,
                MinLevel = Math.Max(1, level - 10),
                Dungeon = true
            };
        }

        private static Item CreateBasicArmor(int level, ItemRarity rarity, ObjType armorType = ObjType.Body)
        {
            var stats = RarityStats[rarity];
            float levelScale = 1.0f + (level / 40.0f);
            int power = (int)(8 * levelScale * stats.PowerMult);

            string slotName = armorType switch
            {
                ObjType.Head => Loc.Get("item.slot.helm"),
                ObjType.Arms => Loc.Get("item.slot.armguards"),
                ObjType.Hands => Loc.Get("item.slot.gauntlets"),
                ObjType.Legs => Loc.Get("item.slot.greaves"),
                ObjType.Feet => Loc.Get("item.slot.boots"),
                ObjType.Waist => Loc.Get("item.slot.belt"),
                ObjType.Face => Loc.Get("item.slot.mask"),
                ObjType.Abody => Loc.Get("item.slot.cloak"),
                _ => Loc.Get("item.slot.armor")
            };

            return new Item
            {
                Name = $"{GetRarityPrefix(rarity)}{slotName}",
                Type = armorType,
                Value = power * 20,
                Armor = power,
                MinLevel = Math.Max(1, level - 10),
                Dungeon = true
            };
        }

        private static string BuildItemName(string baseName, ItemRarity rarity,
            List<(SpecialEffect effect, int value)> effects, bool isCursed)
        {
            string localizedBase = LocalizeTemplateName(baseName);

            if (isCursed)
            {
                return $"{Loc.Get("item.rarity.cursed")} {localizedBase}";
            }

            if (effects.Count == 0)
            {
                return GetRarityPrefix(rarity) + localizedBase;
            }

            // Use the first effect to name the item
            var primaryEffect = effects[0].effect;

            // 50% chance prefix, 50% chance suffix
            if (random.NextDouble() < 0.5)
            {
                return GetLocalizedEffectPrefix(primaryEffect) + " " + localizedBase;
            }
            else
            {
                return localizedBase + " " + GetLocalizedEffectSuffix(primaryEffect);
            }
        }

        private static void ApplyEffectsToItem(Item item, List<(SpecialEffect effect, int value)> effects, bool isWeapon)
        {
            // Store effects for transfer to Equipment during loot-to-equipment conversion (v0.40.5)
            foreach (var (effect, value) in effects)
            {
                item.LootEffects.Add(((int)effect, value));
            }

            foreach (var (effect, value) in effects)
            {
                switch (effect)
                {
                    // Stat bonuses
                    case SpecialEffect.Strength:
                        item.Strength += value;
                        break;
                    case SpecialEffect.Dexterity:
                        item.Dexterity += value;
                        break;
                    case SpecialEffect.Constitution:
                        // Constitution bonus applied via LootEffects → Equipment.ConstitutionBonus
                        break;
                    case SpecialEffect.Intelligence:
                        // Intelligence bonus applied via LootEffects → Equipment.IntelligenceBonus
                        break;
                    case SpecialEffect.Wisdom:
                        item.Wisdom += value;
                        break;
                    case SpecialEffect.AllStats:
                        item.Strength += value;
                        item.Dexterity += value;
                        item.Agility += value;
                        item.Wisdom += value;
                        item.Charisma += value;
                        // CON and INT applied via LootEffects → Equipment.ConstitutionBonus/IntelligenceBonus
                        break;
                    case SpecialEffect.MaxHP:
                        // HP bonus applied via LootEffects → Equipment.ConstitutionBonus
                        break;
                    case SpecialEffect.MaxMana:
                        // Mana bonus applied via LootEffects → Equipment.IntelligenceBonus
                        break;

                    // Elemental damages add to attack (weapons) or provide description (armor)
                    case SpecialEffect.FireDamage:
                    case SpecialEffect.IceDamage:
                    case SpecialEffect.LightningDamage:
                    case SpecialEffect.PoisonDamage:
                    case SpecialEffect.HolyDamage:
                    case SpecialEffect.ShadowDamage:
                        if (isWeapon)
                        {
                            item.Attack += value / 2; // Elemental adds to base attack
                        }
                        break;

                    // Life/mana steal — effects applied via LootEffects at conversion time
                    case SpecialEffect.LifeSteal:
                    case SpecialEffect.ManaSteal:
                        break;

                    // Critical bonuses add to attack effectiveness
                    case SpecialEffect.CriticalStrike:
                    case SpecialEffect.CriticalDamage:
                        item.Attack += value / 3;
                        item.Dexterity += value / 4;
                        break;

                    // Armor piercing
                    case SpecialEffect.ArmorPiercing:
                        item.Attack += value / 2;
                        break;

                    // Defensive effects
                    case SpecialEffect.FireResist:
                    case SpecialEffect.IceResist:
                    case SpecialEffect.LightningResist:
                    case SpecialEffect.PoisonResist:
                        item.MagicProperties.MagicResistance += value / 4;
                        break;

                    case SpecialEffect.MagicResist:
                        item.MagicProperties.MagicResistance += value;
                        break;

                    case SpecialEffect.Thorns:
                        item.Defence += value;
                        break;

                    case SpecialEffect.Regeneration:
                        // Regen effect applied via LootEffects at conversion time
                        break;

                    case SpecialEffect.ManaRegen:
                        // Mana regen effect applied via LootEffects at conversion time
                        break;

                    case SpecialEffect.DamageReduction:
                        item.Armor += value / 2;
                        item.Defence += value / 2;
                        break;

                    case SpecialEffect.BlockChance:
                        item.Defence += value / 2;
                        item.Armor += value / 3;
                        break;
                }
            }

            // Store effects description
            if (effects.Count > 0)
            {
                var effectDescs = effects.Select(e => $"{GetLocalizedEffectName(e.effect)} +{e.value}");
                if (item.Description.Count > 0)
                    item.Description[0] = string.Join(", ", effectDescs);
            }
        }

        private static void ApplyCursePenalties(Item item)
        {
            // Cursed items have significant stat penalties
            int penalty = Math.Max(5, item.Attack / 10);

            item.Strength -= penalty / 2;
            item.Dexterity -= penalty / 3;
            item.Wisdom -= penalty / 3;
            item.LootEffects.Add(((int)SpecialEffect.Constitution, -(penalty / 2)));

            // Add curse description
            if (item.Description.Count > 1)
                item.Description[1] = "This item is CURSED! Visit the Magic Shop to remove the curse.";
        }

        #endregion

        #region Shop Generation

        /// <summary>
        /// Generate shop inventory appropriate for player level
        /// Shop items are never cursed and slightly higher quality
        /// </summary>
        public static List<Item> GenerateShopWeapons(int playerLevel, int count = 8)
        {
            var items = new List<Item>();

            // Shop offers items around player's level (±10 levels)
            int minLevel = Math.Max(1, playerLevel - 10);
            int maxLevel = Math.Min(100, playerLevel + 10);

            for (int i = 0; i < count; i++)
            {
                int itemLevel = minLevel + random.Next(maxLevel - minLevel + 1);

                // Shop items have boosted rarity (no common)
                var rarity = RollRarity(itemLevel);
                if (rarity == ItemRarity.Common) rarity = ItemRarity.Uncommon;

                var candidates = WeaponTemplates
                    .Where(w => itemLevel >= w.MinLevel && itemLevel <= w.MaxLevel)
                    .ToList();

                if (candidates.Count > 0)
                {
                    var template = candidates[random.Next(candidates.Count)];
                    var item = CreateWeaponFromTemplate(template, itemLevel, rarity);
                    item.Shop = true;
                    item.Cursed = false; // Shop items are never cursed
                    item.IsCursed = false;
                    items.Add(item);
                }
            }

            return items.OrderBy(i => i.Value).ToList();
        }

        /// <summary>
        /// Generate shop armor inventory
        /// </summary>
        public static List<Item> GenerateShopArmor(int playerLevel, int count = 8)
        {
            var items = new List<Item>();

            int minLevel = Math.Max(1, playerLevel - 10);
            int maxLevel = Math.Min(100, playerLevel + 10);

            for (int i = 0; i < count; i++)
            {
                int itemLevel = minLevel + random.Next(maxLevel - minLevel + 1);

                var rarity = RollRarity(itemLevel);
                if (rarity == ItemRarity.Common) rarity = ItemRarity.Uncommon;

                var candidates = ArmorTemplates
                    .Where(a => itemLevel >= a.MinLevel && itemLevel <= a.MaxLevel)
                    .ToList();

                if (candidates.Count > 0)
                {
                    var template = candidates[random.Next(candidates.Count)];
                    var item = CreateArmorFromTemplate(template, itemLevel, rarity);
                    item.Shop = true;
                    item.Cursed = false;
                    item.IsCursed = false;
                    items.Add(item);
                }
            }

            return items.OrderBy(i => i.Value).ToList();
        }

        #endregion

        #region Identification System

        /// <summary>
        /// Determines if a loot drop should be unidentified based on rarity.
        /// Common/Uncommon items are always identified - players know basic gear.
        /// Rare+ items have increasing chance to be unidentified.
        /// </summary>
        private static bool ShouldBeUnidentified(ItemRarity rarity)
        {
            return rarity switch
            {
                ItemRarity.Rare => random.NextDouble() < 0.40,       // 40% chance
                ItemRarity.Epic => random.NextDouble() < 0.60,       // 60% chance
                ItemRarity.Legendary => random.NextDouble() < 0.80,  // 80% chance
                ItemRarity.Artifact => true,                          // Always unidentified
                _ => false                                            // Common/Uncommon always known
            };
        }

        /// <summary>
        /// Get the display name for an unidentified item (hides real name, shows type hint)
        /// </summary>
        public static string GetUnidentifiedName(Item item)
        {
            if (item.IsIdentified) return item.Name;

            string typeHint = item.Type switch
            {
                ObjType.Weapon => "Unidentified Weapon",
                ObjType.Body => "Unidentified Armor",
                ObjType.Head => "Unidentified Helm",
                ObjType.Arms => "Unidentified Bracers",
                ObjType.Hands => "Unidentified Gauntlets",
                ObjType.Legs => "Unidentified Greaves",
                ObjType.Feet => "Unidentified Boots",
                ObjType.Shield => "Unidentified Shield",
                ObjType.Fingers => "Unidentified Ring",
                ObjType.Neck => "Unidentified Amulet",
                ObjType.Waist => "Unidentified Belt",
                ObjType.Face => "Unidentified Mask",
                ObjType.Abody => "Unidentified Cloak",
                _ => "Unidentified Item"
            };

            // Add a rarity hint based on power level (the item "feels" powerful)
            int power = Math.Max(item.Attack, item.Armor);
            if (power > 200) return $"Glowing {typeHint}";
            if (power > 100) return $"Shimmering {typeHint}";
            if (power > 50) return $"Ornate {typeHint}";
            return typeHint;
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Get item rarity from an existing item (for display purposes)
        /// </summary>
        public static ItemRarity GetItemRarity(Item item)
        {
            // Determine rarity based on name or power (check localized prefixes)
            string mythic = Loc.Get("item.rarity.mythic");
            string legendary = Loc.Get("item.rarity.legendary");
            string exquisite = Loc.Get("item.rarity.exquisite");
            string superior = Loc.Get("item.rarity.superior");
            string fine = Loc.Get("item.rarity.fine");
            string cursed = Loc.Get("item.rarity.cursed");

            if (item.Name.StartsWith(mythic + " ") || item.Name.Contains("Artifact"))
                return ItemRarity.Artifact;
            if (item.Name.StartsWith(legendary + " "))
                return ItemRarity.Legendary;
            if (item.Name.StartsWith(exquisite + " ") || item.Name.StartsWith(cursed + " "))
                return ItemRarity.Epic;
            if (item.Name.StartsWith(superior + " "))
                return ItemRarity.Rare;
            if (item.Name.StartsWith(fine + " "))
                return ItemRarity.Uncommon;

            // Check by power level
            int power = Math.Max(item.Attack, item.Armor);
            if (power > 200) return ItemRarity.Legendary;
            if (power > 100) return ItemRarity.Epic;
            if (power > 50) return ItemRarity.Rare;
            if (power > 25) return ItemRarity.Uncommon;

            return ItemRarity.Common;
        }

        /// <summary>
        /// Format item for display with color
        /// </summary>
        public static string FormatItemDisplay(Item item)
        {
            var rarity = GetItemRarity(item);
            string color = GetRarityColor(rarity);

            string stats = item.Type == ObjType.Weapon
                ? $"{Loc.Get("combat.bar_atk")}: {item.Attack}"
                : $"{Loc.Get("combat.bar_def")}: {item.Armor}";

            string curse = item.Cursed || item.IsCursed ? " [CURSED]" : "";

            return $"[{color}]{item.Name}[/] ({stats}, {item.Value:N0}g){curse}";
        }

        /// <summary>
        /// Check if a character class can use a loot item based on weapon/shield template class restrictions.
        /// Returns (true, null) if usable, (false, reason) if not.
        /// Only weapons and shields have class restrictions — armor/accessories return true.
        /// </summary>
        public static (bool canUse, string? reason) CanClassUseLootItem(CharacterClass playerClass, Item lootItem)
        {
            // Only weapons and shields have class restrictions
            if (lootItem.Type != ObjType.Weapon && lootItem.Type != ObjType.Shield)
                return (true, null);

            // NG+ prestige classes can use any weapon/shield
            if (playerClass >= CharacterClass.Tidesworn)
                return (true, null);

            // Build a combined list of all weapon + shield templates with their class arrays
            // Sort by longest name first to avoid false matches (e.g., "Bastard Sword" before "Sword")
            var allTemplates = new List<(string Name, string[] Classes)>();

            foreach (var t in WeaponTemplates)
                allTemplates.Add((t.Name, t.Classes));
            foreach (var t in ShieldTemplates)
                allTemplates.Add((t.Name, t.Classes));

            // Sort longest name first for accurate matching
            allTemplates.Sort((a, b) => b.Name.Length.CompareTo(a.Name.Length));

            // Find the first template whose name appears in the loot item name
            // Handles prefixed/suffixed names like "Blazing Longbow of the Phoenix" matching "Longbow"
            foreach (var (templateName, classes) in allTemplates)
            {
                if (lootItem.Name.Contains(templateName, StringComparison.OrdinalIgnoreCase))
                {
                    // "All" means any class can use it
                    if (classes.Any(c => c.Equals("All", StringComparison.OrdinalIgnoreCase)))
                        return (true, null);

                    // Check if this class is in the allowed list
                    string className = playerClass.ToString();
                    if (classes.Any(c => c.Equals(className, StringComparison.OrdinalIgnoreCase)))
                        return (true, null);

                    // Class not in the allowed list
                    string classList = string.Join(", ", classes);
                    string itemType = lootItem.Type == ObjType.Shield ? "shield" : "weapon";
                    return (false, $"Only {classList} can equip this {itemType}.");
                }
            }

            // No template match found — allow usage (unknown item type)
            return (true, null);
        }

        #endregion
    }
