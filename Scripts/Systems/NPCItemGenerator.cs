using System;
using System.Collections.Generic;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Generates items for NPCs - dungeon loot and merchant stock.
    /// Items are class-appropriate and scaled to NPC level.
    /// </summary>
    public static class NPCItemGenerator
    {
        private static Random random = new();

        #region Weapon Templates

        private static readonly Dictionary<string, (int MinLevel, int MaxLevel, int BasePower, string[] Classes)> WeaponTemplates = new()
        {
            // Universal weapons
            { "Dagger", (1, 20, 3, new[] { "All" }) },
            { "Short Sword", (1, 30, 5, new[] { "All" }) },
            { "Long Sword", (5, 50, 10, new[] { "Warrior", "Paladin", "Barbarian", "Ranger" }) },
            { "Broadsword", (10, 60, 15, new[] { "Warrior", "Paladin", "Barbarian" }) },
            { "Bastard Sword", (20, 80, 25, new[] { "Warrior", "Paladin", "Barbarian" }) },
            { "Great Sword", (30, 100, 35, new[] { "Warrior", "Barbarian" }) },

            // Axes
            { "Hand Axe", (1, 25, 6, new[] { "Warrior", "Barbarian", "Ranger" }) },
            { "Battle Axe", (15, 60, 18, new[] { "Warrior", "Barbarian" }) },
            { "Great Axe", (25, 100, 30, new[] { "Barbarian" }) },

            // Maces and Hammers
            { "Club", (1, 15, 2, new[] { "All" }) },
            { "Mace", (5, 40, 8, new[] { "Cleric", "Paladin", "Warrior" }) },
            { "War Hammer", (15, 70, 20, new[] { "Cleric", "Paladin", "Warrior" }) },
            { "Flail", (20, 80, 25, new[] { "Cleric", "Paladin" }) },

            // Thief weapons
            { "Stiletto", (5, 40, 7, new[] { "Assassin", "Ranger" }) },
            { "Rapier", (10, 50, 12, new[] { "Assassin", "Ranger" }) },
            { "Poisoned Blade", (25, 80, 28, new[] { "Assassin" }) },

            // Staves
            { "Quarterstaff", (1, 30, 4, new[] { "Magician", "Sage", "Cleric", "Monk" }) },
            { "Magic Staff", (15, 60, 15, new[] { "Magician", "Sage" }) },
            { "Staff of Power", (40, 100, 35, new[] { "Magician", "Sage" }) },

            // Monk weapons
            { "Bo Staff", (5, 50, 10, new[] { "Monk" }) },
            { "Nunchaku", (10, 60, 14, new[] { "Monk" }) },

            // Special weapons
            { "Silver Blade", (30, 90, 30, new[] { "Paladin" }) },
            { "Holy Avenger", (50, 100, 50, new[] { "Paladin" }) },
            { "Berserker Axe", (40, 100, 45, new[] { "Barbarian" }) },
            { "Assassin's Fang", (35, 100, 40, new[] { "Assassin" }) },
        };

        #endregion

        #region Armor Templates

        private static readonly Dictionary<string, (int MinLevel, int MaxLevel, int BasePower, string[] Classes)> ArmorTemplates = new()
        {
            // Light armor (all classes)
            { "Leather Vest", (1, 20, 2, new[] { "All" }) },
            { "Studded Leather", (5, 35, 5, new[] { "All" }) },
            { "Hard Leather", (10, 45, 8, new[] { "All" }) },

            // Medium armor
            { "Chain Shirt", (10, 50, 10, new[] { "Warrior", "Paladin", "Cleric", "Ranger" }) },
            { "Scale Mail", (15, 60, 14, new[] { "Warrior", "Paladin", "Cleric" }) },
            { "Chain Mail", (20, 70, 18, new[] { "Warrior", "Paladin", "Cleric" }) },

            // Heavy armor
            { "Banded Mail", (25, 75, 22, new[] { "Warrior", "Paladin" }) },
            { "Splint Mail", (30, 80, 26, new[] { "Warrior", "Paladin" }) },
            { "Plate Mail", (40, 90, 32, new[] { "Warrior", "Paladin" }) },
            { "Full Plate", (50, 100, 40, new[] { "Warrior", "Paladin" }) },

            // Robes (casters)
            { "Simple Robe", (1, 25, 1, new[] { "Magician", "Sage" }) },
            { "Enchanted Robe", (15, 50, 8, new[] { "Magician", "Sage" }) },
            { "Arcane Vestments", (30, 75, 15, new[] { "Magician", "Sage" }) },
            { "Robe of the Archmage", (50, 100, 25, new[] { "Magician", "Sage" }) },

            // Monk armor
            { "Gi", (1, 30, 2, new[] { "Monk" }) },
            { "Reinforced Gi", (15, 60, 8, new[] { "Monk" }) },
            { "Master's Gi", (40, 100, 18, new[] { "Monk" }) },

            // Thief armor
            { "Shadow Cloak", (10, 50, 6, new[] { "Assassin", "Ranger" }) },
            { "Night Leather", (25, 75, 14, new[] { "Assassin" }) },
            { "Assassin's Garb", (40, 100, 22, new[] { "Assassin" }) },

            // Special armor
            { "Holy Vestments", (30, 80, 20, new[] { "Cleric", "Paladin" }) },
            { "Barbarian Hide", (20, 70, 16, new[] { "Barbarian" }) },
            { "Ranger's Cloak", (20, 70, 12, new[] { "Ranger" }) },
        };

        #endregion

        #region Quality Modifiers

        private static readonly string[] QualityPrefixes = new[]
        {
            "", "", "", "",  // 40% chance no prefix
            "Fine ", "Quality ", "Superior ", "Masterwork ",
            "Rusted ", "Worn ", "Battered ",
            "Enchanted ", "Blessed ", "Cursed "
        };

        private static readonly Dictionary<string, (float PowerMod, float ValueMod, bool IsCursed, string? MagicEffect)> QualityEffects = new()
        {
            { "", (1.0f, 1.0f, false, null) },
            { "Fine ", (1.1f, 1.3f, false, null) },
            { "Quality ", (1.15f, 1.5f, false, null) },
            { "Superior ", (1.25f, 2.0f, false, null) },
            { "Masterwork ", (1.4f, 3.0f, false, null) },
            { "Rusted ", (0.7f, 0.4f, false, null) },
            { "Worn ", (0.8f, 0.5f, false, null) },
            { "Battered ", (0.85f, 0.6f, false, null) },
            { "Enchanted ", (1.3f, 2.5f, false, "Magic Damage +5") },
            { "Blessed ", (1.2f, 2.0f, false, "Holy Light") },
            { "Cursed ", (1.35f, 0.5f, true, "Drains Life") },
        };

        #endregion

        /// <summary>
        /// Generate dungeon loot appropriate for an NPC
        /// </summary>
        public static global::Item GenerateDungeonLoot(NPC npc, int dungeonLevel)
        {
            // 60% chance weapon, 40% chance armor
            bool isWeapon = random.NextDouble() < 0.6;

            if (isWeapon)
                return GenerateWeapon(npc.Class, dungeonLevel);
            else
                return GenerateArmor(npc.Class, dungeonLevel);
        }

        /// <summary>
        /// Generate merchant stock for an NPC to sell
        /// </summary>
        public static global::Item GenerateMerchantStock(NPC npc)
        {
            // Merchants have wider variety, not class-specific
            // Use NPC level as the "dungeon level" for scaling
            int level = Math.Max(1, npc.Level + random.Next(-5, 6));

            // 50/50 weapon or armor
            bool isWeapon = random.NextDouble() < 0.5;

            if (isWeapon)
                return GenerateWeapon(CharacterClass.Warrior, level); // Generic selection
            else
                return GenerateArmor(CharacterClass.Warrior, level);
        }

        /// <summary>
        /// Generate a weapon appropriate for a class and level
        /// </summary>
        public static global::Item GenerateWeapon(CharacterClass charClass, int level)
        {
            // Find appropriate weapons
            var candidates = new List<(string Name, int MinLevel, int MaxLevel, int BasePower)>();

            foreach (var kvp in WeaponTemplates)
            {
                var template = kvp.Value;

                // Check level range
                if (level < template.MinLevel || level > template.MaxLevel)
                    continue;

                // Check class compatibility
                bool classMatch = false;
                foreach (var c in template.Classes)
                {
                    if (c == "All" || c == charClass.ToString())
                    {
                        classMatch = true;
                        break;
                    }
                }

                if (classMatch)
                    candidates.Add((kvp.Key, template.MinLevel, template.MaxLevel, template.BasePower));
            }

            // Fallback to dagger if nothing found
            if (candidates.Count == 0)
            {
                return new global::Item { Name = "Dagger", Value = 15, Type = global::ObjType.Weapon, Attack = 3 };
            }

            // Pick a random candidate
            var chosen = candidates[random.Next(candidates.Count)];

            // Apply quality modifier
            string prefix = QualityPrefixes[random.Next(QualityPrefixes.Length)];
            var quality = QualityEffects[prefix];

            // Calculate power scaled by level within the item's range
            float levelProgress = (float)(level - chosen.MinLevel) / Math.Max(1, chosen.MaxLevel - chosen.MinLevel);
            int scaledPower = (int)(chosen.BasePower * (1 + levelProgress * 0.5f)); // Up to 50% bonus at max level
            int finalPower = (int)(scaledPower * quality.PowerMod);

            // Calculate value
            long baseValue = (long)(finalPower * 12 * quality.ValueMod);

            string fullName = prefix + chosen.Name;

            return new global::Item
            {
                Name = fullName,
                Value = baseValue,
                Type = global::ObjType.Weapon,
                Attack = finalPower,
                IsCursed = quality.IsCursed
            };
        }

        /// <summary>
        /// Generate armor appropriate for a class and level
        /// </summary>
        public static global::Item GenerateArmor(CharacterClass charClass, int level)
        {
            // Find appropriate armor
            var candidates = new List<(string Name, int MinLevel, int MaxLevel, int BasePower)>();

            foreach (var kvp in ArmorTemplates)
            {
                var template = kvp.Value;

                // Check level range
                if (level < template.MinLevel || level > template.MaxLevel)
                    continue;

                // Check class compatibility
                bool classMatch = false;
                foreach (var c in template.Classes)
                {
                    if (c == "All" || c == charClass.ToString())
                    {
                        classMatch = true;
                        break;
                    }
                }

                if (classMatch)
                    candidates.Add((kvp.Key, template.MinLevel, template.MaxLevel, template.BasePower));
            }

            // Fallback to leather vest if nothing found
            if (candidates.Count == 0)
            {
                return new global::Item { Name = "Leather Vest", Value = 10, Type = global::ObjType.Body, Armor = 2 };
            }

            // Pick a random candidate
            var chosen = candidates[random.Next(candidates.Count)];

            // Apply quality modifier
            string prefix = QualityPrefixes[random.Next(QualityPrefixes.Length)];
            var quality = QualityEffects[prefix];

            // Calculate power scaled by level within the item's range
            float levelProgress = (float)(level - chosen.MinLevel) / Math.Max(1, chosen.MaxLevel - chosen.MinLevel);
            int scaledPower = (int)(chosen.BasePower * (1 + levelProgress * 0.5f)); // Up to 50% bonus at max level
            int finalPower = (int)(scaledPower * quality.PowerMod);

            // Calculate value
            long baseValue = (long)(finalPower * 15 * quality.ValueMod);

            string fullName = prefix + chosen.Name;

            return new global::Item
            {
                Name = fullName,
                Value = baseValue,
                Type = global::ObjType.Body,
                Armor = finalPower,
                IsCursed = quality.IsCursed
            };
        }

        /// <summary>
        /// Generate starting inventory for an NPC (for merchants or high-level NPCs)
        /// </summary>
        public static List<global::Item> GenerateStartingInventory(NPC npc, int itemCount)
        {
            var items = new List<global::Item>();

            for (int i = 0; i < itemCount && items.Count < npc.MaxMarketInventory; i++)
            {
                // Vary the level a bit for variety
                int itemLevel = Math.Max(1, npc.Level + random.Next(-10, 5));

                global::Item item;
                if (random.NextDouble() < 0.5)
                    item = GenerateWeapon(npc.Class, itemLevel);
                else
                    item = GenerateArmor(npc.Class, itemLevel);

                items.Add(item);
            }

            return items;
        }
    }
}
