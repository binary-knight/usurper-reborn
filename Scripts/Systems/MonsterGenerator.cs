using System;
using System.Collections.Generic;
using System.Linq;
using UsurperRemake.Systems;

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
    /// <param name="approachScale">v1.1.10: extra HP and damage for a dungeon floor that leads up to an Old
    /// God (OldGodApproachScale); 1.0 everywhere else. Defence is not scaled.</param>
    public static Monster GenerateMonster(int dungeonLevel, bool isBoss = false, bool isMiniBoss = false, Random? random = null, double approachScale = 1.0)
    {
        random ??= Random.Shared;

        // Clamp dungeon level to valid range (1-100)
        dungeonLevel = Math.Max(1, Math.Min(100, dungeonLevel));

        // Get appropriate monster family and tier for this level
        var (family, tier) = MonsterFamilies.GetMonsterForLevel(dungeonLevel, random);

        // Calculate base stats using smooth scaling formulas
        var stats = CalculateMonsterStats(dungeonLevel, isBoss ? (tier.BossPowerMultiplier ?? tier.PowerMultiplier) : tier.PowerMultiplier, isBoss, isMiniBoss, approachScale);

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
        // v0.60.10 (druidah Lv.22 Cleric report): "I used turn undead on a ghoul and the
        // game says ghoul is not an undead." Pre-fix, MonsterGenerator only set FamilyName
        // and never assigned monster.MonsterClass / monster.Undead, so every monster shipped
        // with MonsterClass.Normal (0) and Undead=0. Turn Undead's gate at CombatEngine:15609
        // (and ~20 similar undead/demon-vulnerable checks across the codebase) all read those
        // fields, so they all answered "no" for Ghoul / Wight / Wraith / Lich / etc. Map the
        // family name onto the enum + legacy Undead int here so every downstream check works.
        ApplyMonsterClassification(monster, family.FamilyName);

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
        monster.SignatureAbilities.AddRange(tier.SignatureAbilities ?? new());   // 1.2.8: read by MonsterTactics only

        // NG+ cycle monster buff (v0.52.0) — monsters get stronger each cycle
        // v0.56.1: also scale WeapPow and ArmPow (were missed in the original implementation
        // so NG+ monsters dealt less weapon damage and had less armor than intended)
        int cycle = StoryProgressionSystem.Instance?.CurrentCycle ?? 1;
        if (cycle >= 2)
        {
            double monsterMult = GameConfig.GetNGPlusMonsterMultiplier(cycle);
            monster.MaxHP = (long)(monster.MaxHP * monsterMult);
            monster.HP = monster.MaxHP;
            monster.Strength = (long)(monster.Strength * monsterMult);
            monster.Defence = (int)(monster.Defence * monsterMult);
            monster.Punch = (long)(monster.Punch * monsterMult);
            monster.WeapPow = (long)(monster.WeapPow * monsterMult);
            monster.ArmPow = (long)(monster.ArmPow * monsterMult);
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
    /// Assign Monster.MonsterClass (and legacy Undead int) from the family name. v0.60.10
    /// fix for the Turn-Undead-on-Ghoul report: pre-fix, MonsterClass was never assigned
    /// during generation, so every monster carried MonsterClass.Normal regardless of its
    /// family. The Undead and Demon enum values are the load-bearing ones (Turn Undead,
    /// Divine Smite, Paladin alignment bonuses, Holy enchant scaling all read them); the
    /// rest are mapped opportunistically for future class-keyed mechanics. Sets the legacy
    /// int field too for any check that uses `Undead > 0` instead of the enum.
    /// </summary>
    private static void ApplyMonsterClassification(Monster monster, string familyName)
    {
        monster.MonsterClass = familyName switch
        {
            "Undead"     => MonsterClass.Undead,
            "Demonic"    => MonsterClass.Demon,
            "Draconic"   => MonsterClass.Dragon,
            "Beast"      => MonsterClass.Beast,
            "Insectoid"  => MonsterClass.Beast,
            "Aquatic"    => MonsterClass.Beast,
            "Elemental"  => MonsterClass.Elemental,
            "Construct"  => MonsterClass.Construct,
            "Aberration" => MonsterClass.Aberration,
            "Shadow"     => MonsterClass.Aberration,
            "Fey"        => MonsterClass.Plant,
            "Celestial"  => MonsterClass.Humanoid,
            "Giant"      => MonsterClass.Humanoid,
            "Goblinoid"  => MonsterClass.Humanoid,
            "Orcish"     => MonsterClass.Humanoid,
            _            => MonsterClass.Normal
        };
        if (monster.MonsterClass == MonsterClass.Undead)
            monster.Undead = 1;
    }

    /// <summary>
    /// Calculate balanced stats for a monster.
    /// REBALANCED: Formulas designed for player to defeat monsters in 3-8 hits at
    /// appropriate level, while monsters deal significant but survivable damage.
    ///
    /// Design goals:
    /// - Level 1 player (Str 10, Weap 5) deals ~30 damage -> Monster HP ~80-120
    /// - Level 50 player (Str 100, Weap 100) deals ~400 damage -> Monster HP ~1200-2000
    /// - Level 100 player (Str 200, Weap 200) deals ~800 damage -> Monster HP ~2500-4000
    /// </summary>
    private static MonsterStats CalculateMonsterStats(int level, float powerMultiplier, bool isBoss, bool isMiniBoss = false, double approachScale = 1.0,
        float? sysopHpMultiplier = null, DifficultyMode? difficulty = null)
    {
        // v0.56.1 — separate HP/damage/defense multipliers so we can buff HP aggressively
        // without turning bosses into one-shot machines.
        // Bosses: 2.8x HP, 1.25x damage, 1.2x defense (was 2.0x everything — died to one crit).
        // Champions: 2.2x HP, 1.3x damage, 1.2x defense (was 1.5x everything — felt like trash).
        float hpMultiplier = isBoss ? 2.8f : (isMiniBoss ? 2.2f : 1.0f);
        float damageMultiplier = isBoss ? 1.25f : (isMiniBoss ? 1.3f : 1.0f);
        float defenseMultiplier = isBoss ? 1.2f : (isMiniBoss ? 1.2f : 1.0f);

        float hpTotal = powerMultiplier * hpMultiplier;
        float damageTotal = powerMultiplier * damageMultiplier;
        float defenseTotal = powerMultiplier * defenseMultiplier;

        // Soft cap: full formula up to floor 50, then diminishing growth beyond
        // This prevents monster offense from exponentially outpacing player stats at high floors
        int effectiveLevel = level <= 50 ? level : 50 + (int)((level - 50) * 0.6);

        // REBALANCED HP (v0.47.5: +45% so fights last 4-8 hits; v0.54.0: soft-capped past floor 50)
        // Formula: 50*level + level^1.2 * 15, using effectiveLevel for offensive stats
        long baseHP = (long)((50 * effectiveLevel) + Math.Pow(effectiveLevel, 1.2) * 15);
        long hp = (long)(baseHP * hpTotal);

        // REBALANCED STRENGTH: uses effectiveLevel to prevent one-shots at high floors
        long baseStrength = (long)((2 * effectiveLevel) + Math.Pow(effectiveLevel, 1.05) * 1.5);
        long strength = (long)(baseStrength * damageTotal);

        // Monster defence — uses raw level (monsters should be tanky, not one-shotting)
        // Formula: level + level^1.02 * 0.5
        long baseDefence = (long)((level) + Math.Pow(level, 1.02) * 0.5);
        long defence = (long)(baseDefence * defenseTotal);

        // REBALANCED PUNCH: uses effectiveLevel
        long basePunch = (long)((effectiveLevel) + Math.Pow(effectiveLevel, 1.02) * 0.5);
        long punch = (long)(basePunch * damageTotal);

        // REBALANCED WEAPON POWER: uses effectiveLevel
        long baseWeaponPower = (long)((1.5 * effectiveLevel) + Math.Pow(effectiveLevel, 1.05) * 1);
        long weaponPower = (long)(baseWeaponPower * damageTotal);

        // Monster armor power (v0.41.4: raised from 0.4f to 0.7f so monsters have real armor)
        // Formula: level * 0.5 + level^1.02 * 0.3
        long baseArmorPower = (long)((0.5 * level) + Math.Pow(level, 1.02) * 0.3);
        long armorPower = (long)(baseArmorPower * defenseTotal * 0.7f);

        // Apply post-beta-launch baseline difficulty correction to all stats.
        // Telemetry showed 99.7% server-wide win rate and 2.7 avg rounds; this
        // 15% buff applies to HP, attack, and defense so monster durability,
        // damage output, and damage resistance all rise together. Sysop and
        // player-difficulty knobs below stack on top.
        float baseScale = GameConfig.BaseMonsterDifficultyScale;
        hp = (long)(hp * baseScale);
        strength = (long)(strength * baseScale);
        defence = (long)(defence * baseScale);
        punch = (long)(punch * baseScale);
        weaponPower = (long)(weaponPower * baseScale);
        armorPower = (long)(armorPower * baseScale);

        // v1.1.10: the floors that lead up to an Old God grow gradually tougher, HP and damage only, so
        // the god is not the first real fight in five floors (player report: the monsters on the way
        // down went in one or two turns, then Mael'Keth killed three companions). Defence is left alone
        // so a weaker party is warned, not walled.
        if (approachScale != 1.0)
        {
            hp = (long)(hp * approachScale);
            strength = (long)(strength * approachScale);
            punch = (long)(punch * approachScale);
            weaponPower = (long)(weaponPower * approachScale);
        }

        // Apply server-wide SysOp HP multiplier (the wiki export passes the default explicitly)
        hp = (long)(hp * (sysopHpMultiplier ?? GameConfig.MonsterHPMultiplier));

        // Apply difficulty-based HP multiplier
        hp = difficulty.HasValue
            ? (long)(hp * DifficultySystem.GetMonsterHPMultiplier(difficulty.Value))
            : DifficultySystem.ApplyMonsterHPMultiplier(hp);

        // Early-floor reduction (v0.49.3 floor-1 step -> v0.65.3 graduated ramp). Telemetry showed
        // permadeaths clustering at character level 1-3: clearing floor 1 then hitting a full-strength
        // floor 2 was a difficulty cliff. Floors 1-3 now ramp 0.5x / 0.65x / 0.8x. Keyed on the
        // absolute dungeon floor (not a player-vs-floor gap), so a high-level player replaying low
        // floors gets no new advantage -- those fights were already trivial and the loot/XP is
        // negligible and floored by Math.Max below.
        if (level >= 1 && level <= GameConfig.EarlyFloorSofteningMaxFloor)
        {
            float fm = GameConfig.GetEarlyFloorMonsterStatMultiplier(level); // v1.1.13: floors 1-6, from the table
            hp = (long)(hp * fm);
            strength = (long)(strength * fm);
            defence = (long)(defence * fm);
            punch = (long)(punch * fm);
            weaponPower = (long)(weaponPower * fm);
        }

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

    /// <summary>Normal, non-boss monster stats from the combat generator for a wiki level sample, at the
    /// default server HP multiplier and Normal difficulty whatever the current settings are.</summary>
    internal static (long HP, long Strength, long Defence, long Punch, long WeaponPower, long ArmorPower)
        GetWikiStats(int level, float powerMultiplier)
    {
        var stats = CalculateMonsterStats(level, powerMultiplier, false, sysopHpMultiplier: 1.0f, difficulty: DifficultyMode.Normal);
        return (stats.HP, stats.Strength, stats.Defence, stats.Punch, stats.WeaponPower, stats.ArmorPower);
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
    /// <summary>
    /// v1.1.10: the HP and damage scale for regular monsters on a dungeon floor: 1.0, except on the
    /// GameConfig.OldGodApproachFloors floors before an Old God's floor, where it rises by
    /// GameConfig.OldGodApproachStepPerFloor a floor (1.05 five floors out to 1.25 on the floor
    /// before the god). The god's own floor and every floor after it are 1.0.
    /// </summary>
    public static double OldGodApproachScale(int floor)
    {
        // a god's own floor is never scaled, even floor 95, which is also five floors before 100
        if (Array.IndexOf(UsurperRemake.Systems.ProgressionRoadmap.OldGodFloors, floor) >= 0) return 1.0;
        foreach (int godFloor in UsurperRemake.Systems.ProgressionRoadmap.OldGodFloors)
        {
            int before = godFloor - floor;
            if (before >= 1 && before <= GameConfig.OldGodApproachFloors)
                return 1.0 + GameConfig.OldGodApproachStepPerFloor * (GameConfig.OldGodApproachFloors + 1 - before);
        }
        return 1.0;
    }

    /// <summary>1.2.8: the dungeon floors with encounter templates (GameConfig.EncounterTemplatesEnabled).</summary>
    public const int TemplateMinFloor = 4;
    public const int TemplateMaxFloor = 10;

    /// <summary>1.2.8: a templated regular group is 1 (35%), 2 (45%) or 3 (20%) monsters, never more.</summary>
    public const double TemplateSizeOneShare = 0.35;
    public const double TemplateSizeTwoShare = 0.45;
    public const int TemplateMaxGroupSize = 3;

    /// <summary>The chance that a regular group is one family and one tier: 0.5 with templates, 0.7 without.</summary>
    public const double TemplateSameFamilyChance = 0.5;
    public const double SameFamilyChance = 0.7;

    /// <summary>1.2.8 threat budget: each member of a templated group of 3 keeps this share of its HP and of
    /// its Strength, Punch and WeapPow, so three are about 1.3 times two, not 1.5.</summary>
    public const double TemplateBudgetHp = 0.8;
    public const double TemplateBudgetDamage = 0.9;

    /// <summary>1.2.8 caster role: the member that gets its family's spell keeps this share of its Strength.</summary>
    public const double CasterStrength = 0.85;

    /// <summary>1.2.8 caster role: the spell of each family that has one. A family not listed here (Fey and
    /// Aberration by design, and every other one) never gives a caster. All three are in
    /// CombatEvents.MonsterSpellAbilities, so the damage is recorded as spell damage.</summary>
    internal static readonly Dictionary<string, string> CasterSpells = new(StringComparer.Ordinal)
    {
        ["Draconic"] = "Fireball", ["Elemental"] = "Fireball",
        ["Undead"] = "Spellcasting", ["Goblinoid"] = "Spellcasting",
        ["Demonic"] = "Hellfire",
    };

    /// <summary>True when the encounter templates apply: the switch is on, the caller is the dungeon (rules
    /// given) and the real dungeon floor is in the template band. The band is read from the rules' floor,
    /// never from the monster level: a straggler room on floor 14 asks for level 7 monsters and must keep
    /// the draws made before 1.2.8.</summary>
    internal static bool TemplatesApply(EncounterRules? rules) =>
        GameConfig.EncounterTemplatesEnabled && rules != null
        && rules.RealFloor >= TemplateMinFloor && rules.RealFloor <= TemplateMaxFloor;

    /// <summary>1.2.8 elites: the six affix ids, in the order the draw indexes them. An elite's name is its
    /// current name in monster.elite.{id}; its description is monster.elite_desc.{id}.</summary>
    internal static readonly string[] EliteAffixIds = { "frenzied", "warded", "venomous", "commanding", "mending", "swift" };

    /// <summary>The chance that a regular dungeon encounter on this real floor holds an elite.</summary>
    internal static double EliteChance(int realFloor) =>
        realFloor < GameConfig.EliteMinFloor ? 0.0
        : realFloor <= 10 ? GameConfig.EliteChanceFloors3To10
        : realFloor <= 30 ? GameConfig.EliteChanceFloors11To30
        : GameConfig.EliteChanceFloors31Plus;

    /// <summary>True when an encounter can hold an elite: the switch is on, the caller is the dungeon (rules
    /// given; a boss room gives none) and the real floor has a chance. False draws nothing.</summary>
    internal static bool ElitesApply(EncounterRules? rules) =>
        GameConfig.EliteMonstersEnabled && rules != null && EliteChance(rules.RealFloor) > 0.0;

    /// <summary>The size of a templated regular group from one roll in [0, 1).</summary>
    internal static int TemplateGroupSize(double roll) =>
        roll < TemplateSizeOneShare ? 1 : roll < TemplateSizeOneShare + TemplateSizeTwoShare ? 2 : TemplateMaxGroupSize;

    /// <param name="rules">1.2.8: given only by the dungeon (EncounterRules.Dungeon with the real floor). With
    /// GameConfig.EncounterTemplatesEnabled on and that floor in 4 to 10 the group follows the encounter
    /// templates: sizes 1, 2 and 3, one family half the time, a budgeted group of 3 and one caster in a
    /// mixed group. Without rules, with the switch off or on any other floor the method draws the same
    /// random numbers in the same order as before 1.2.8. With GameConfig.EliteMonstersEnabled on and the
    /// floor 3 or deeper, a regular group then rolls for an elite (ElitesApply, PromoteElite): the last
    /// draws of the call, after everything above.</param>
    public static List<Monster> GenerateMonsterGroup(int dungeonLevel, Random? random = null, double approachScale = 1.0, EncounterRules? rules = null)
    {
        random ??= Random.Shared;
        var monsters = new List<Monster>();
        bool templates = TemplatesApply(rules);

        // Clamp dungeon level to valid range (1-100)
        dungeonLevel = Math.Max(1, Math.Min(100, dungeonLevel));

        // 10% chance for mini-boss/champion encounter (single powerful monster)
        // These are named champions that always drop equipment loot
        if (random.NextDouble() < 0.10)
        {
            monsters.Add(GenerateMonster(dungeonLevel, isBoss: false, isMiniBoss: true, random: random, approachScale: approachScale));
            return monsters;
        }

        // Regular encounter - 1 to 5 monsters
        // Higher levels tend toward more monsters
        // 1.2.8: a templated dungeon floor rolls its own sizes (one draw, as before)
        int groupSize = templates ? TemplateGroupSize(random.NextDouble()) : dungeonLevel switch
        {
            <= 10 => random.Next(1, 3),   // 1-2 monsters early game
            <= 30 => random.Next(1, 4),   // 1-3 monsters mid-early
            <= 60 => random.Next(2, 5),   // 2-4 monsters mid-late
            _ => random.Next(3, 6)        // 3-5 monsters late game
        };

        // Generate monsters (usually same family for thematic encounters)
        bool sameFamily = random.NextDouble() < (templates ? TemplateSameFamilyChance : SameFamilyChance); // 70% chance for same family (50% templated)

        if (sameFamily)
        {
            // Pick one family and tier
            var (family, tier) = MonsterFamilies.GetMonsterForLevel(dungeonLevel, random);

            for (int i = 0; i < groupSize; i++)
            {
                var stats = CalculateMonsterStats(dungeonLevel, tier.PowerMultiplier, false, false, approachScale);

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
                // v0.60.10 (druidah report): see twin in GenerateMonster above.
                ApplyMonsterClassification(monster, family.FamilyName);
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
                monster.SignatureAbilities.AddRange(tier.SignatureAbilities ?? new());   // 1.2.8: read by MonsterTactics only

                // NG+ cycle monster buff (v0.52.0) — same scaling as GenerateMonster()
                // v0.56.1: include WeapPow and ArmPow to match the single-monster path
                int cycle = StoryProgressionSystem.Instance?.CurrentCycle ?? 1;
                if (cycle >= 2)
                {
                    double monsterMult = GameConfig.GetNGPlusMonsterMultiplier(cycle);
                    monster.MaxHP = (long)(monster.MaxHP * monsterMult);
                    monster.HP = monster.MaxHP;
                    monster.Strength = (long)(monster.Strength * monsterMult);
                    monster.Defence = (int)(monster.Defence * monsterMult);
                    monster.Punch = (long)(monster.Punch * monsterMult);
                    monster.WeapPow = (long)(monster.WeapPow * monsterMult);
                    monster.ArmPow = (long)(monster.ArmPow * monsterMult);
                }

                monsters.Add(monster);
            }
        }
        else
        {
            // Mixed family encounter
            for (int i = 0; i < groupSize; i++)
            {
                monsters.Add(GenerateMonster(dungeonLevel, isBoss: false, isMiniBoss: false, random: random, approachScale: approachScale));
            }
        }

        if (templates)
        {
            if (monsters.Count >= TemplateMaxGroupSize) ApplyGroupBudget(monsters);
            if (!sameFamily && monsters.Count >= 2) AssignCaster(monsters, random);
        }

        // 1.2.8 elites: the last step, so every draw above is the one made without it. The champion
        // encounter returned earlier and never reaches this.
        if (ElitesApply(rules) && random.NextDouble() < EliteChance(rules!.RealFloor))
            PromoteElite(monsters, random, rules.RealFloor);

        return monsters;
    }

    /// <summary>
    /// 1.2.8: promotes one member of a built regular group to an elite. Draws: the member, then one affix,
    /// then from GameConfig.EliteSecondAffixFloor a second one among the other five. Budget: the elite takes
    /// its member's place, and a group of 3 or more loses one further member, the last one that is not the
    /// elite (no draw). The elite keeps its member's stats (a templated budget and the caster's Strength
    /// included) times the GameConfig elite multipliers, and its stored reward times the reward ones. Its
    /// name is its full current name in monster.elite.{affix}, written in English through
    /// MonsterNames.FromKey and shown in each reader's language, so a caster stays one ("Frenzied Kobold
    /// Shaman"). TierName and FamilyName stay; IsMiniBoss is not touched. What an affix does is not here.
    /// </summary>
    internal static Monster PromoteElite(List<Monster> monsters, Random random, int realFloor)
    {
        var elite = monsters[random.Next(monsters.Count)];
        if (monsters.Count >= 3)
            monsters.RemoveAt(ReferenceEquals(monsters[monsters.Count - 1], elite) ? monsters.Count - 2 : monsters.Count - 1);

        elite.IsElite = true;
        elite.MaxHP = Math.Max(1, (long)(elite.MaxHP * GameConfig.EliteHpMultiplier));
        elite.HP = elite.MaxHP;
        elite.Strength = (long)(elite.Strength * GameConfig.EliteDamageMultiplier);
        elite.Punch = (long)(elite.Punch * GameConfig.EliteDamageMultiplier);
        elite.WeapPow = (long)(elite.WeapPow * GameConfig.EliteDamageMultiplier);
        elite.Defence = (int)(elite.Defence * GameConfig.EliteDefenceMultiplier);
        elite.ArmPow = (long)(elite.ArmPow * GameConfig.EliteDefenceMultiplier);
        // the reward a victory reads is the one stored when the monster was built
        elite.Experience = (long)(elite.Experience * GameConfig.EliteExperienceMultiplier);
        elite.Gold = (long)(elite.Gold * GameConfig.EliteGoldMultiplier);

        var pool = EliteAffixIds.ToList();
        int affixes = realFloor >= GameConfig.EliteSecondAffixFloor ? 2 : 1;
        for (int i = 0; i < affixes; i++)
        {
            int pick = random.Next(pool.Count);
            elite.EliteAffixes.Add(pool[pick]);
            pool.RemoveAt(pick);
        }
        // the first affix is the outermost word of the name
        for (int i = elite.EliteAffixes.Count - 1; i >= 0; i--)
            elite.Name = MonsterNames.FromKey("monster.elite." + elite.EliteAffixes[i], elite.Name);
        return elite;
    }

    /// <summary>1.2.8 threat budget of a templated group of 3: HP x0.8, Strength, Punch and WeapPow x0.9
    /// for every member. Draws nothing.</summary>
    private static void ApplyGroupBudget(List<Monster> monsters)
    {
        foreach (var m in monsters)
        {
            m.MaxHP = Math.Max(1, (long)(m.MaxHP * TemplateBudgetHp));
            m.HP = m.MaxHP;
            m.Strength = (long)(m.Strength * TemplateBudgetDamage);
            m.Punch = (long)(m.Punch * TemplateBudgetDamage);
            m.WeapPow = (long)(m.WeapPow * TemplateBudgetDamage);
        }
    }

    /// <summary>
    /// 1.2.8 caster role of a mixed templated group: one member whose family has a spell (CasterSpells),
    /// picked with one draw when there is any (none otherwise), gets the spell added to its abilities and
    /// its Strength x0.85. Its name is its tier name in monster.role.caster, written in English through
    /// MonsterNames.FromKey and shown in each reader's language; TierName and FamilyName stay.
    /// </summary>
    private static void AssignCaster(List<Monster> monsters, Random random)
    {
        var eligible = monsters.Where(m => m.FamilyName != null && CasterSpells.ContainsKey(m.FamilyName)).ToList();
        if (eligible.Count == 0) return;
        MakeCaster(eligible[random.Next(eligible.Count)]);
    }

    private static void MakeCaster(Monster caster)
    {
        string spell = CasterSpells[caster.FamilyName];
        if (!caster.SpecialAbilities.Contains(spell)) caster.SpecialAbilities.Add(spell);
        caster.Strength = (long)(caster.Strength * CasterStrength);
        caster.Name = MonsterNames.FromKey("monster.role.caster", caster.TierName);
    }
}
