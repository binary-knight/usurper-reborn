using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Enhanced monster generation system
/// Creates monsters with scalable stats for levels 1-100
/// Integrates with MonsterFamilies for variety and color
/// </summary>
public static class MonsterGenerator
{
    /// <summary>
    /// Generate a monster for a specific dungeon level
    /// Uses monster families and balanced stat scaling
    /// </summary>
    public static Monster GenerateMonster(int dungeonLevel, bool isBoss = false, bool isMiniBoss = false, Random? random = null)
    {
        random ??= new Random();

        // Clamp dungeon level to valid range (1-100)
        dungeonLevel = Math.Max(1, Math.Min(100, dungeonLevel));

        // Get appropriate monster family and tier for this level
        var (family, tier) = MonsterFamilies.GetMonsterForLevel(dungeonLevel, random);

        // Calculate base stats using smooth scaling formulas
        var stats = CalculateMonsterStats(dungeonLevel, tier.PowerMultiplier, isBoss, isMiniBoss);

        // Generate monster name - mini-bosses get "Champion" suffix
        string monsterName = tier.Name;
        if (isMiniBoss && !isBoss)
        {
            monsterName = GetChampionName(tier.Name, family.FamilyName);
        }

        // Create the monster
        var monster = Monster.CreateMonster(
            nr: dungeonLevel,
            name: monsterName,
            hps: stats.HP,
            strength: stats.Strength,
            defence: stats.Defence,
            phrase: isMiniBoss ? GetChampionPhrase(family) : GetMonsterPhrase(family, tier),
            grabweap: random.NextDouble() < 0.15, // 15% chance to disarm
            grabarm: false,
            weapon: GetWeapon(dungeonLevel, random),
            armor: GetArmor(dungeonLevel, random),
            poisoned: false,
            disease: false,
            punch: stats.Punch,
            armpow: stats.ArmorPower,
            weappow: stats.WeaponPower
        );

        // Store family and tier info for combat messages
        monster.FamilyName = family.FamilyName;
        monster.TierName = tier.Name;
        monster.MonsterColor = isMiniBoss ? "bright_yellow" : tier.Color;
        monster.AttackType = family.AttackType;
        monster.Level = dungeonLevel;
        monster.IsBoss = isBoss;
        monster.IsMiniBoss = isMiniBoss;

        // Animals, insects, constructs, and mindless creatures can't speak
        monster.CanSpeak = family.FamilyName switch
        {
            "Beast" or "Insectoid" or "Construct" or "Aquatic" or "Aberration" or "Elemental" => false,
            _ => true
        };

        // Override for specific non-speaking members of speaking families
        if (tier.Name is "Drake")
            monster.CanSpeak = false;

        // Add special abilities
        foreach (var ability in tier.SpecialAbilities)
        {
            monster.SpecialAbilities.Add(ability);
        }

        return monster;
    }

    /// <summary>
    /// Get a champion name for a mini-boss based on the base monster name
    /// </summary>
    private static string GetChampionName(string baseName, string familyName)
    {
        // Check if the name already has a rank suffix
        string[] existingRanks = { "King", "Lord", "Chieftain", "Warlord", "Champion", "Berserker", "Elder" };
        foreach (var rank in existingRanks)
        {
            if (baseName.Contains(rank))
            {
                // Already has a rank, use "Alpha" prefix instead
                return $"Alpha {baseName}";
            }
        }

        // Add appropriate champion suffix based on family
        return familyName switch
        {
            "Goblinoid" => $"{baseName} Champion",
            "Undead" => $"{baseName} Revenant",
            "Orcish" => $"{baseName} Warchief",
            "Draconic" => $"Elder {baseName}",
            "Demonic" => $"{baseName} Overlord",
            "Giant" => $"{baseName} Titan",
            "Beast" => $"Alpha {baseName}",
            "Elemental" => $"Prime {baseName}",
            "Aberration" => $"{baseName} Abomination",
            "Insectoid" => $"{baseName} Hive Lord",
            "Construct" => $"{baseName} Prime",
            "Fey" => $"{baseName} Archfey",
            "Aquatic" => $"{baseName} Leviathan",
            "Celestial" => $"{baseName} Seraph",
            "Shadow" => $"{baseName} Nightlord",
            _ => $"{baseName} Champion"
        };
    }

    /// <summary>
    /// Get a special phrase for champion/mini-boss monsters
    /// </summary>
    private static string GetChampionPhrase(MonsterFamilies.MonsterFamily family)
    {
        return family.FamilyName switch
        {
            "Goblinoid" => "You dare challenge me? I am the strongest of my tribe!",
            "Undead" => "Your soul shall join my legion of the damned...",
            "Orcish" => "BLOOD AND GLORY! I will mount your skull on my pike!",
            "Draconic" => "Foolish mortal! You face the might of dragonkind!",
            "Demonic" => "Your pathetic soul is MINE!",
            "Giant" => "I'll grind your bones to make my bread!",
            "Beast" => "roars with primal fury, the ground trembling beneath its massive form.",
            "Elemental" => "surges with unleashed elemental wrath.",
            "Aberration" => "warps the air around it as reality itself screams.",
            "Insectoid" => "fills the air with a deafening, mind-piercing chittering.",
            "Construct" => "locks onto you with glowing eyes, gears grinding to full power.",
            "Fey" => "You've wandered into my domain, little mortal...",
            "Aquatic" => "unleashes a roar that sends shockwaves through the air.",
            "Celestial" => "You have been judged UNWORTHY!",
            "Shadow" => "Darkness shall consume all...",
            _ => "You face a champion! Prepare yourself!"
        };
    }

    /// <summary>
    /// Monster stats calculated for a given level
    /// </summary>
    private class MonsterStats
    {
        public long HP { get; set; }
        public long Strength { get; set; }
        public long Defence { get; set; }
        public long Punch { get; set; }
        public long WeaponPower { get; set; }
        public long ArmorPower { get; set; }
    }

    /// <summary>
    /// Calculate balanced stats for a monster
    /// REBALANCED: Formulas designed for player to defeat monsters in 3-8 hits
    /// at appropriate level, while monsters deal significant but survivable damage
    ///
    /// Design goals:
    /// - Level 1 player (Str 10, Weap 5) deals ~30 damage -> Monster HP ~80-120
    /// - Level 50 player (Str 100, Weap 100) deals ~400 damage -> Monster HP ~1200-2000
    /// - Level 100 player (Str 200, Weap 200) deals ~800 damage -> Monster HP ~2500-4000
    /// </summary>
    private static MonsterStats CalculateMonsterStats(int level, float powerMultiplier, bool isBoss, bool isMiniBoss = false)
    {
        // Boss multiplier for increased difficulty (2.0x HP/stats)
        // Mini-boss multiplier for moderate difficulty (1.5x HP/stats)
        float bossMultiplier = isBoss ? 2.0f : (isMiniBoss ? 1.5f : 1.0f);
        float totalMultiplier = powerMultiplier * bossMultiplier;

        // REBALANCED HP (v0.47.5: +45% so fights last 4-8 hits; was 35*level + level^1.15*10)
        // Formula: 50*level + level^1.2 * 15
        // Level 1: ~65, Level 10: ~738, Level 25: ~2,058, Level 50: ~5,085, Level 100: ~12,870
        long baseHP = (long)((50 * level) + Math.Pow(level, 1.2) * 15);
        long hp = (long)(baseHP * totalMultiplier);

        // REBALANCED STRENGTH: Reduced to match new player damage scaling
        // Formula: 2*level + level^1.05 * 1.5 (was 4*level + level^1.15 * 2)
        long baseStrength = (long)((2 * level) + Math.Pow(level, 1.05) * 1.5);
        long strength = (long)(baseStrength * totalMultiplier);

        // Monster defence (v0.47.5: removed 0.8x penalty — monsters need real defense)
        // Formula: level + level^1.02 * 0.5
        long baseDefence = (long)((level) + Math.Pow(level, 1.02) * 0.5);
        long defence = (long)(baseDefence * totalMultiplier);

        // REBALANCED PUNCH: Reduced natural attack bonus
        // Formula: level + level^1.02 * 0.5 (was 1.5*level + level^1.1 * 1)
        long basePunch = (long)((level) + Math.Pow(level, 1.02) * 0.5);
        long punch = (long)(basePunch * totalMultiplier);

        // REBALANCED WEAPON POWER: Reduced so monsters don't one-shot players
        // Formula: 1.5*level + level^1.05 * 1 (was 3*level + level^1.15 * 1.5)
        long baseWeaponPower = (long)((1.5 * level) + Math.Pow(level, 1.05) * 1);
        long weaponPower = (long)(baseWeaponPower * totalMultiplier);

        // Monster armor power (v0.41.4: raised from 0.4f to 0.7f so monsters have real armor)
        // Formula: level * 0.5 + level^1.02 * 0.3
        long baseArmorPower = (long)((0.5 * level) + Math.Pow(level, 1.02) * 0.3);
        long armorPower = (long)(baseArmorPower * totalMultiplier * 0.7f);

        // Apply server-wide SysOp HP multiplier
        hp = (long)(hp * GameConfig.MonsterHPMultiplier);

        return new MonsterStats
        {
            HP = Math.Max(15, hp),
            Strength = Math.Max(3, strength),
            Defence = Math.Max(0, defence),
            Punch = Math.Max(1, punch),
            WeaponPower = Math.Max(1, weaponPower),
            ArmorPower = Math.Max(0, armorPower)
        };
    }

    /// <summary>
    /// Get weapon for monster based on level
    /// </summary>
    private static string GetWeapon(int level, Random random)
    {
        return level switch
        {
            <= 10 => random.Next(4) switch
            {
                0 => "Rusty Dagger",
                1 => "Wooden Club",
                2 => "Short Sword",
                _ => "Crude Axe"
            },
            <= 25 => random.Next(4) switch
            {
                0 => "Iron Sword",
                1 => "Battle Axe",
                2 => "Mace",
                _ => "Spear"
            },
            <= 50 => random.Next(4) switch
            {
                0 => "Steel Longsword",
                1 => "War Hammer",
                2 => "Halberd",
                _ => "Greatsword"
            },
            <= 75 => random.Next(4) switch
            {
                0 => "Enchanted Blade",
                1 => "Mythril Axe",
                2 => "Flaming Sword",
                _ => "Dragonbone Mace"
            },
            _ => random.Next(5) switch
            {
                0 => "Legendary Sword",
                1 => "Vorpal Blade",
                2 => "Soul Reaver",
                3 => "Doom Bringer",
                _ => "Ancient Artifact Weapon"
            }
        };
    }

    /// <summary>
    /// Get armor for monster based on level
    /// </summary>
    private static string GetArmor(int level, Random random)
    {
        return level switch
        {
            <= 10 => random.Next(4) switch
            {
                0 => "Leather Armor",
                1 => "Tattered Robes",
                2 => "Hide Armor",
                _ => "None"
            },
            <= 25 => random.Next(4) switch
            {
                0 => "Chain Mail",
                1 => "Studded Leather",
                2 => "Scale Mail",
                _ => "Ring Mail"
            },
            <= 50 => random.Next(4) switch
            {
                0 => "Plate Armor",
                1 => "Splint Mail",
                2 => "Banded Mail",
                _ => "Full Plate"
            },
            <= 75 => random.Next(4) switch
            {
                0 => "Enchanted Plate",
                1 => "Mythril Armor",
                2 => "Dragonscale Mail",
                _ => "Elven Chain"
            },
            _ => random.Next(5) switch
            {
                0 => "Legendary Armor",
                1 => "Adamantine Plate",
                2 => "Divine Vestments",
                3 => "Demon Plate",
                _ => "Ancient Artifact Armor"
            }
        };
    }

    /// <summary>
    /// Get flavor text phrase for monster
    /// </summary>
    private static string GetMonsterPhrase(MonsterFamilies.MonsterFamily family, MonsterFamilies.MonsterTier tier)
    {
        // Family-specific battle cries and phrases
        return family.FamilyName switch
        {
            "Goblinoid" => tier.Name switch
            {
                "Goblin" => "Grrr! Me smash you!",
                "Hobgoblin" => "You die now, weakling!",
                "Goblin Champion" => "For the tribe! Attack!",
                "Goblin Warlord" => "I'll mount your head on my wall!",
                "Goblin King" => "Kneel before your king, or perish!",
                _ => "Prepare to die!"
            },
            "Undead" => "The living shall join the dead...",
            "Orcish" => tier.Name switch
            {
                "Orc" => "Blood and skulls!",
                "Orc Warrior" => "Die, human scum!",
                "Orc Berserker" => "BLOOD! BLOOD! BLOOD!",
                "Orc Chieftain" => "I'll tear you limb from limb!",
                "Orc Warlord" => "Your skull will join my throne!",
                _ => "For the Horde!"
            },
            "Draconic" => tier.Name switch
            {
                "Kobold" => "You no scare kobold!",
                "Drake" => "roars and breathes a gout of fire!",
                "Wyvern" => "You shall burn!",
                "Young Dragon" => "I am fire incarnate!",
                "Ancient Dragon" => "Mortals... such fleeting things.",
                _ => "roars menacingly!"
            },
            "Demonic" => "Your soul is mine!",
            "Giant" => "Fe Fi Fo Fum!",
            "Beast" => "snarls and growls menacingly.",
            "Elemental" => "crackles with raw elemental fury.",
            "Aberration" => "writhes and pulses with unnatural movement.",
            "Insectoid" => "clicks and hisses aggressively.",
            "Construct" => "grinds to life with a mechanical whir.",
            "Fey" => "Let's play a game...",
            "Aquatic" => "lets out a deep, gurgling roar.",
            "Celestial" => "You have been judged and found wanting.",
            "Shadow" => "Embrace the darkness...",
            _ => "Prepare for battle!"
        };
    }

    /// <summary>
    /// Generate a group of monsters for an encounter
    /// Group size and composition based on dungeon level
    /// </summary>
    public static List<Monster> GenerateMonsterGroup(int dungeonLevel, Random? random = null)
    {
        random ??= new Random();
        var monsters = new List<Monster>();

        // Clamp dungeon level to valid range (1-100)
        dungeonLevel = Math.Max(1, Math.Min(100, dungeonLevel));

        // 10% chance for mini-boss/champion encounter (single powerful monster)
        // These are named champions that always drop equipment loot
        if (random.NextDouble() < 0.10)
        {
            monsters.Add(GenerateMonster(dungeonLevel, isBoss: false, isMiniBoss: true, random: random));
            return monsters;
        }

        // Regular encounter - 1 to 5 monsters
        // Higher levels tend toward more monsters
        int groupSize = dungeonLevel switch
        {
            <= 10 => random.Next(1, 3),   // 1-2 monsters early game
            <= 30 => random.Next(1, 4),   // 1-3 monsters mid-early
            <= 60 => random.Next(2, 5),   // 2-4 monsters mid-late
            _ => random.Next(3, 6)        // 3-5 monsters late game
        };

        // Generate monsters (usually same family for thematic encounters)
        bool sameFamily = random.NextDouble() < 0.7; // 70% chance for same family

        if (sameFamily)
        {
            // Pick one family and tier
            var (family, tier) = MonsterFamilies.GetMonsterForLevel(dungeonLevel, random);

            for (int i = 0; i < groupSize; i++)
            {
                var stats = CalculateMonsterStats(dungeonLevel, tier.PowerMultiplier, false);

                var monster = Monster.CreateMonster(
                    nr: dungeonLevel,
                    name: tier.Name,
                    hps: stats.HP,
                    strength: stats.Strength,
                    defence: stats.Defence,
                    phrase: GetMonsterPhrase(family, tier),
                    grabweap: random.NextDouble() < 0.15,
                    grabarm: false,
                    weapon: GetWeapon(dungeonLevel, random),
                    armor: GetArmor(dungeonLevel, random),
                    poisoned: false,
                    disease: false,
                    punch: stats.Punch,
                    armpow: stats.ArmorPower,
                    weappow: stats.WeaponPower
                );

                monster.FamilyName = family.FamilyName;
                monster.TierName = tier.Name;
                monster.MonsterColor = tier.Color;
                monster.AttackType = family.AttackType;
                monster.Level = dungeonLevel;
                monster.CanSpeak = family.FamilyName switch
                {
                    "Beast" or "Insectoid" or "Construct" or "Aquatic" or "Aberration" or "Elemental" => false,
                    _ => true
                };

                // Override for specific non-speaking members of speaking families
                if (tier.Name is "Drake")
                    monster.CanSpeak = false;

                foreach (var ability in tier.SpecialAbilities)
                {
                    monster.SpecialAbilities.Add(ability);
                }

                monsters.Add(monster);
            }
        }
        else
        {
            // Mixed family encounter
            for (int i = 0; i < groupSize; i++)
            {
                monsters.Add(GenerateMonster(dungeonLevel, isBoss: false, isMiniBoss: false, random: random));
            }
        }

        return monsters;
    }
}
