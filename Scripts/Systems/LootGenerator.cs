using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Epic Loot Generation System
/// Generates weapons, armor, and accessories with:
/// - Rarity-based power scaling (Common to Artifact)
/// - Special effects (elemental damage, lifesteal, etc.)
/// - Cursed items (5-10% chance on rare+ items)
/// - Level-appropriate stats that feel exciting and rewarding
///
/// Power Philosophy:
/// - Level 1 common weapon: ~5-10 attack
/// - Level 50 epic weapon: ~200-400 attack
/// - Level 100 legendary weapon: ~800-1200 attack
/// - Artifacts can exceed 1500+ attack with unique effects
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
        public static ItemRarity RollRarity(int level)
        {
            double roll = random.NextDouble();

            // Base chances modified by level
            // At level 1: 70% Common, 25% Uncommon, 5% Rare
            // At level 50: 30% Common, 35% Uncommon, 25% Rare, 8% Epic, 2% Legendary
            // At level 100: 10% Common, 25% Uncommon, 35% Rare, 20% Epic, 8% Legendary, 2% Artifact

            float levelFactor = Math.Min(1.0f, level / 100f);

            float artifactChance = levelFactor * 0.02f;
            float legendaryChance = levelFactor * 0.08f;
            float epicChance = 0.02f + levelFactor * 0.18f;
            float rareChance = 0.05f + levelFactor * 0.30f;
            float uncommonChance = 0.25f + levelFactor * 0.10f;

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

        public static string GetRarityPrefix(ItemRarity rarity) => rarity switch
        {
            ItemRarity.Common => "",
            ItemRarity.Uncommon => "Fine ",
            ItemRarity.Rare => "Superior ",
            ItemRarity.Epic => "Exquisite ",
            ItemRarity.Legendary => "Legendary ",
            ItemRarity.Artifact => "Mythic ",
            _ => ""
        };

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
            MaxMana          // +X max mana
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
            { SpecialEffect.MaxMana, ("Max Mana", "Arcane ", " of Power", false) }
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
            ("Stiletto", new[] { "Assassin", "Ranger" }, 10, 50, 15),
            ("Assassin's Blade", new[] { "Assassin" }, 30, 80, 35),
            ("Shadow Fang", new[] { "Assassin" }, 50, 100, 60),

            // Swords - Balanced
            ("Short Sword", new[] { "All" }, 1, 25, 10),
            ("Long Sword", new[] { "Warrior", "Paladin", "Ranger" }, 10, 50, 20),
            ("Broadsword", new[] { "Warrior", "Paladin", "Barbarian" }, 20, 70, 35),
            ("Bastard Sword", new[] { "Warrior", "Paladin" }, 35, 85, 55),
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
            ("Mace", new[] { "Cleric", "Paladin", "Warrior" }, 10, 45, 18),
            ("War Hammer", new[] { "Cleric", "Paladin", "Warrior" }, 25, 70, 40),
            ("Flail", new[] { "Cleric", "Paladin" }, 40, 85, 60),
            ("Holy Mace", new[] { "Cleric", "Paladin" }, 60, 100, 85),
            ("Scepter of Judgment", new[] { "Paladin" }, 80, 100, 120),

            // Staves - Magic focused
            ("Quarterstaff", new[] { "Magician", "Sage", "Monk", "Cleric" }, 1, 35, 8),
            ("Magic Staff", new[] { "Magician", "Sage" }, 15, 55, 22),
            ("Staff of Power", new[] { "Magician", "Sage" }, 35, 80, 45),
            ("Archmage's Staff", new[] { "Magician", "Sage" }, 60, 100, 75),
            ("Staff of the Cosmos", new[] { "Sage" }, 85, 100, 110),

            // Monk weapons
            ("Bo Staff", new[] { "Monk" }, 5, 45, 15),
            ("Nunchaku", new[] { "Monk" }, 20, 65, 30),
            ("Dragon Fist Wraps", new[] { "Monk" }, 45, 90, 55),
            ("Celestial Hand Wraps", new[] { "Monk" }, 75, 100, 95),

            // Ranger weapons
            ("Hunting Bow", new[] { "Ranger" }, 5, 40, 16),
            ("Longbow", new[] { "Ranger" }, 25, 70, 38),
            ("Composite Bow", new[] { "Ranger" }, 50, 90, 65),
            ("Elven Bow", new[] { "Ranger" }, 75, 100, 100),

            // Holy weapons (Paladin exclusive high tier)
            ("Silver Blade", new[] { "Paladin" }, 35, 75, 50),
            ("Holy Avenger", new[] { "Paladin" }, 60, 100, 90),
            ("Blade of the Righteous", new[] { "Paladin" }, 85, 100, 135),
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
            ("Studded Leather", new[] { "Ranger", "Assassin", "Monk" }, 15, 50, 18),
            ("Hard Leather", new[] { "Ranger", "Assassin" }, 30, 70, 32),
            ("Shadow Leather", new[] { "Assassin" }, 50, 90, 55),
            ("Night Stalker Armor", new[] { "Assassin" }, 75, 100, 85),

            // Ranger specific
            ("Ranger's Cloak", new[] { "Ranger" }, 20, 60, 25),
            ("Forest Guardian Armor", new[] { "Ranger" }, 45, 85, 50),
            ("Elven Chainweave", new[] { "Ranger" }, 70, 100, 80),

            // Monk specific
            ("Training Gi", new[] { "Monk" }, 5, 35, 10),
            ("Reinforced Gi", new[] { "Monk" }, 25, 65, 28),
            ("Master's Gi", new[] { "Monk" }, 50, 90, 52),
            ("Dragon Scale Gi", new[] { "Monk" }, 80, 100, 88),

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
            ("Necklace of Fireballs", 50, 95, 38),
            ("Amulet of the Planes", 65, 100, 45),
            ("Heart of the Dragon", 75, 100, 52),
            ("Tear of the Gods", 90, 100, 65),
        };

        #endregion

        #region Main Generation Methods

        /// <summary>
        /// Generate a weapon drop for dungeon loot
        /// </summary>
        public static Item GenerateWeapon(int dungeonLevel, CharacterClass playerClass)
        {
            // Roll rarity based on dungeon level
            var rarity = RollRarity(dungeonLevel);

            // Find appropriate weapon templates
            var candidates = WeaponTemplates
                .Where(w => dungeonLevel >= w.MinLevel && dungeonLevel <= w.MaxLevel)
                .Where(w => w.Classes.Contains("All") || w.Classes.Contains(playerClass.ToString()))
                .ToList();

            if (candidates.Count == 0)
            {
                // Fallback to any weapon in level range
                candidates = WeaponTemplates
                    .Where(w => dungeonLevel >= w.MinLevel && dungeonLevel <= w.MaxLevel)
                    .ToList();
            }

            if (candidates.Count == 0)
            {
                // Ultimate fallback
                return CreateBasicWeapon(dungeonLevel, rarity);
            }

            // Pick random template
            var template = candidates[random.Next(candidates.Count)];

            return CreateWeaponFromTemplate(template, dungeonLevel, rarity);
        }

        /// <summary>
        /// Generate an armor drop for dungeon loot
        /// </summary>
        public static Item GenerateArmor(int dungeonLevel, CharacterClass playerClass)
        {
            var rarity = RollRarity(dungeonLevel);

            var candidates = ArmorTemplates
                .Where(a => dungeonLevel >= a.MinLevel && dungeonLevel <= a.MaxLevel)
                .Where(a => a.Classes.Contains("All") || a.Classes.Contains(playerClass.ToString()))
                .ToList();

            if (candidates.Count == 0)
            {
                candidates = ArmorTemplates
                    .Where(a => dungeonLevel >= a.MinLevel && dungeonLevel <= a.MaxLevel)
                    .ToList();
            }

            if (candidates.Count == 0)
            {
                return CreateBasicArmor(dungeonLevel, rarity);
            }

            var template = candidates[random.Next(candidates.Count)];

            return CreateArmorFromTemplate(template, dungeonLevel, rarity);
        }

        /// <summary>
        /// Generate random loot (weapon or armor) for dungeon
        /// </summary>
        public static Item GenerateDungeonLoot(int dungeonLevel, CharacterClass playerClass)
        {
            // 55% weapon, 45% armor
            if (random.NextDouble() < 0.55)
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
        /// Generate loot specifically for mini-boss/champion monsters
        /// Mini-bosses ALWAYS drop equipment (weapon, armor, ring, or necklace)
        /// </summary>
        public static Item GenerateMiniBossLoot(int dungeonLevel, CharacterClass playerClass)
        {
            // Boost rarity for mini-boss drops (at least Uncommon, better chances for rare+)
            var rarity = RollRarity(dungeonLevel + 10); // +10 level bonus for rarity
            if (rarity == ItemRarity.Common)
                rarity = ItemRarity.Uncommon;

            // 35% weapon, 30% armor, 20% ring, 15% necklace
            double roll = random.NextDouble();
            if (roll < 0.35)
                return GenerateWeaponWithRarity(dungeonLevel, playerClass, rarity);
            else if (roll < 0.65)
                return GenerateArmorWithRarity(dungeonLevel, playerClass, rarity);
            else if (roll < 0.85)
                return GenerateRing(dungeonLevel, rarity);
            else
                return GenerateNecklace(dungeonLevel, rarity);
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

            // 40% weapon, 35% armor, 15% ring, 10% necklace
            double roll = random.NextDouble();
            if (roll < 0.40)
                return GenerateWeaponWithRarity(dungeonLevel, playerClass, rarity);
            else if (roll < 0.75)
                return GenerateArmorWithRarity(dungeonLevel, playerClass, rarity);
            else if (roll < 0.90)
                return GenerateRing(dungeonLevel, rarity);
            else
                return GenerateNecklace(dungeonLevel, rarity);
        }

        /// <summary>
        /// Generate a weapon with a specific rarity
        /// </summary>
        private static Item GenerateWeaponWithRarity(int dungeonLevel, CharacterClass playerClass, ItemRarity rarity)
        {
            var candidates = WeaponTemplates
                .Where(w => dungeonLevel >= w.MinLevel && dungeonLevel <= w.MaxLevel)
                .Where(w => w.Classes.Contains("All") || w.Classes.Contains(playerClass.ToString()))
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
        /// Generate armor with a specific rarity
        /// </summary>
        private static Item GenerateArmorWithRarity(int dungeonLevel, CharacterClass playerClass, ItemRarity rarity)
        {
            var candidates = ArmorTemplates
                .Where(a => dungeonLevel >= a.MinLevel && dungeonLevel <= a.MaxLevel)
                .Where(a => a.Classes.Contains("All") || a.Classes.Contains(playerClass.ToString()))
                .ToList();

            if (candidates.Count == 0)
            {
                candidates = ArmorTemplates
                    .Where(a => dungeonLevel >= a.MinLevel && dungeonLevel <= a.MaxLevel)
                    .ToList();
            }

            if (candidates.Count == 0)
            {
                return CreateBasicArmor(dungeonLevel, rarity);
            }

            var template = candidates[random.Next(candidates.Count)];
            return CreateArmorFromTemplate(template, dungeonLevel, rarity);
        }

        #endregion

        #region Item Creation

        private static Item CreateWeaponFromTemplate(
            (string Name, string[] Classes, int MinLevel, int MaxLevel, float BasePower) template,
            int level,
            ItemRarity rarity)
        {
            var stats = RarityStats[rarity];

            // Calculate power with level scaling (v0.41.4: reduced from level/25 to level/40)
            // Base formula: basePower * (1 + level/40) * rarityMult
            float levelScale = 1.0f + (level / 40.0f);
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
            ItemRarity rarity)
        {
            var stats = RarityStats[rarity];

            float levelScale = 1.0f + (level / 40.0f);
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
                Type = ObjType.Body,
                Value = value,
                Armor = finalPower,
                MinLevel = Math.Max(1, level - 10),
                Cursed = isCursed,
                IsCursed = isCursed,
                Shop = false,
                Dungeon = true
            };

            ApplyEffectsToItem(item, effects, isWeapon: false);

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

        private static Item CreateAccessoryFromTemplate(
            (string Name, int MinLevel, int MaxLevel, float BasePower) template,
            int level,
            ItemRarity rarity,
            ObjType accessoryType)
        {
            var stats = RarityStats[rarity];

            float levelScale = 1.0f + (level / 40.0f);
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
            // Apply base power as a mix of stats
            if (accessoryType == ObjType.Fingers) // Ring
            {
                item.Strength += finalPower / 4;
                item.Dexterity += finalPower / 4;
                item.HP += finalPower * 2;
            }
            else if (accessoryType == ObjType.Neck) // Necklace
            {
                item.Wisdom += finalPower / 3;
                item.Mana += finalPower * 2;
                item.HP += finalPower;
            }

            ApplyEffectsToItem(item, effects, isWeapon: false);

            if (isCursed)
            {
                // Cursed accessories have higher stats but penalties
                item.Strength = (int)(item.Strength * 1.3f);
                item.HP = (int)(item.HP * 1.3f);
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
                Name = $"{GetRarityPrefix(rarity)}Weapon",
                Type = ObjType.Weapon,
                Value = power * 15,
                Attack = power,
                MinLevel = Math.Max(1, level - 10),
                Dungeon = true
            };
        }

        private static Item CreateBasicArmor(int level, ItemRarity rarity)
        {
            var stats = RarityStats[rarity];
            float levelScale = 1.0f + (level / 40.0f);
            int power = (int)(8 * levelScale * stats.PowerMult);

            return new Item
            {
                Name = $"{GetRarityPrefix(rarity)}Armor",
                Type = ObjType.Body,
                Value = power * 20,
                Armor = power,
                MinLevel = Math.Max(1, level - 10),
                Dungeon = true
            };
        }

        private static string BuildItemName(string baseName, ItemRarity rarity,
            List<(SpecialEffect effect, int value)> effects, bool isCursed)
        {
            if (isCursed)
            {
                return $"Cursed {baseName}";
            }

            if (effects.Count == 0)
            {
                return GetRarityPrefix(rarity) + baseName;
            }

            // Use the first effect to name the item
            var primaryEffect = effects[0].effect;
            var info = EffectInfo[primaryEffect];

            // 50% chance prefix, 50% chance suffix
            if (random.NextDouble() < 0.5)
            {
                return info.Prefix + baseName;
            }
            else
            {
                return baseName + info.Suffix;
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
                        item.HP += value * 5; // Constitution gives HP
                        break;
                    case SpecialEffect.Intelligence:
                        item.Mana += value * 3; // Intelligence gives mana
                        break;
                    case SpecialEffect.Wisdom:
                        item.Wisdom += value;
                        break;
                    case SpecialEffect.AllStats:
                        item.Strength += value;
                        item.Dexterity += value;
                        item.Wisdom += value;
                        item.Charisma += value;
                        break;
                    case SpecialEffect.MaxHP:
                        item.HP += value;
                        break;
                    case SpecialEffect.MaxMana:
                        item.Mana += value;
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
                            item.MagicProperties.Mana += value / 5; // Some mana bonus
                        }
                        break;

                    // Life/mana steal
                    case SpecialEffect.LifeSteal:
                    case SpecialEffect.ManaSteal:
                        item.HP += value; // Represented as HP bonus for now
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
                        item.HP += value * 2;
                        break;

                    case SpecialEffect.ManaRegen:
                        item.Mana += value * 2;
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
                var effectDescs = effects.Select(e => $"{EffectInfo[e.effect].Name} +{e.value}");
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
            item.HP -= penalty * 2;

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
            // Determine rarity based on name or power
            if (item.Name.StartsWith("Mythic ") || item.Name.Contains("Artifact"))
                return ItemRarity.Artifact;
            if (item.Name.StartsWith("Legendary "))
                return ItemRarity.Legendary;
            if (item.Name.StartsWith("Exquisite ") || item.Name.Contains("Cursed"))
                return ItemRarity.Epic;
            if (item.Name.StartsWith("Superior "))
                return ItemRarity.Rare;
            if (item.Name.StartsWith("Fine "))
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
                ? $"Atk: {item.Attack}"
                : $"Def: {item.Armor}";

            string curse = item.Cursed || item.IsCursed ? " [CURSED]" : "";

            return $"[{color}]{item.Name}[/] ({stats}, {item.Value:N0}g){curse}";
        }

        #endregion
    }
