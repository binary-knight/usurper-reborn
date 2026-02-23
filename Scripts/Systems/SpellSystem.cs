using UsurperRemake.Utils;
using UsurperRemake.Systems;
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Complete Pascal-compatible spell system
/// Handles all spell mechanics from SPELLSU.PAS and CAST.PAS
/// Spells are learned automatically by class and level
/// </summary>
public static class SpellSystem
{
    /// <summary>
    /// Spell casting result
    /// </summary>
    public class SpellResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public int Damage { get; set; }
        public int Healing { get; set; }
        public string SpecialEffect { get; set; } = "";
        public int ProtectionBonus { get; set; }
        public int AttackBonus { get; set; }
        public bool IsMultiTarget { get; set; }
        public int Duration { get; set; } // Combat rounds
        public int ManaCost { get; set; }

        // D20 Training System additions
        public string RollInfo { get; set; } = "";           // Roll details for display
        public float ProficiencyMultiplier { get; set; } = 1.0f;  // Effect power multiplier
        public bool SkillImproved { get; set; }              // Did skill level up?
        public string NewProficiencyLevel { get; set; } = ""; // New level name if improved
    }
    
    /// <summary>
    /// Spell information
    /// </summary>
    public class SpellInfo
    {
        public int Level { get; set; }
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public int ManaCost { get; set; }
        public int LevelRequired { get; set; }
        public string MagicWords { get; set; } = "";
        public bool IsMultiTarget { get; set; }
        public string SpellType { get; set; } = ""; // Attack, Heal, Buff, Debuff
        
        public SpellInfo(int level, string name, string description, int manaCost, int levelRequired, string magicWords, bool isMultiTarget = false, string spellType = "")
        {
            Level = level;
            Name = name;
            Description = description;
            ManaCost = manaCost;
            LevelRequired = levelRequired;
            MagicWords = magicWords;
            IsMultiTarget = isMultiTarget;
            SpellType = spellType;
        }
    }
    
    // Expanded spell system - 25 spells per class spread across 100 levels
    // Thematically based on game lore: Old Gods, Ocean Philosophy, The Sundering
    private static readonly Dictionary<CharacterClass, Dictionary<int, SpellInfo>> SpellBook = new()
    {
        // ═══════════════════════════════════════════════════════════════════════════════
        // CLERIC SPELLS - Divine magic, healing, holy power
        // Themed around the New Pantheon and Light vs Shadow conflict
        // ═══════════════════════════════════════════════════════════════════════════════
        [CharacterClass.Cleric] = new Dictionary<int, SpellInfo>
        {
            // --- EARLY TIER (Levels 1-25) - Basic Divine Magic ---
            [1] = new SpellInfo(1, "Cure Light", "A gentle prayer heals minor wounds. Effect: 4-7 hp. Duration: 1 turn.", 5, 1, "Sularahamasturie", false, "Heal"),
            [2] = new SpellInfo(2, "Divine Shield", "Call upon the gods for protection. Protection: +5. Duration: whole fight.", 8, 2, "Exmaddurie", false, "Buff"),
            [3] = new SpellInfo(3, "Purify", "Cleanse disease and minor poisons from the body. Cures minor afflictions.", 12, 3, "Purimasarie", false, "Heal"),
            [4] = new SpellInfo(4, "Bless Weapon", "Imbue your weapon with holy power. Attack: +10. Duration: whole fight.", 15, 4, "Sanctiweaparie", false, "Buff"),
            [5] = new SpellInfo(5, "Cure Wounds", "A stronger healing prayer. Effect: 15-25 hp. Duration: 1 turn.", 18, 5, "Aduexusmarie", false, "Heal"),
            [6] = new SpellInfo(6, "Turn Undead", "Holy light sears the walking dead. Damage: 30-40 vs undead. Duration: 1 turn.", 22, 6, "Exorcismarie", false, "Attack"),
            [7] = new SpellInfo(7, "Sanctuary", "Create a holy ward that makes enemies hesitate. Protection: +15. Duration: 3 rounds.", 25, 7, "Sanctuarie", false, "Buff"),

            // --- MID TIER (Levels 26-50) - Advanced Divine Arts ---
            [8] = new SpellInfo(8, "Cure Critical", "Powerful divine healing with chivalry blessing. Effect: 40-55 hp. Duration: 1 turn.", 30, 8, "Majorhealarie", false, "Heal"),
            [9] = new SpellInfo(9, "Holy Smite", "Call down righteous fury. Damage: 35-50. Extra vs evil. Duration: 1 turn.", 35, 9, "Adrealitarieum", false, "Attack"),
            [10] = new SpellInfo(10, "Armor of Faith", "Divine armor surrounds the faithful. Protection: +25. Duration: whole fight.", 40, 10, "Faitharmarie", false, "Buff"),
            [11] = new SpellInfo(11, "Dispel Evil", "Banish dark enchantments and weaken evil creatures. Removes enemy buffs.", 45, 11, "Dispeldarkie", false, "Debuff"),
            [12] = new SpellInfo(12, "Mass Cure", "Heal yourself and allies. Effect: 30-45 hp to all. Duration: 1 turn.", 50, 12, "Masshealarie", true, "Heal"),
            [13] = new SpellInfo(13, "Holy Explosion", "A burst of holy energy strikes all foes. Damage: 50-70. Duration: 1 turn.", 55, 13, "Holyburstarie", true, "Attack"),
            [14] = new SpellInfo(14, "Prayer of Fortitude", "Strengthen body and spirit. +20 to all defenses. Duration: whole fight.", 60, 14, "Fortitudarie", false, "Buff"),

            // --- HIGH TIER (Levels 51-75) - Greater Divine Powers ---
            [15] = new SpellInfo(15, "Invisibility", "Cloak yourself in divine light, unseen by mortal eyes. Protection: +40. Duration: whole fight.", 70, 15, "Exsuamarie", false, "Buff"),
            [16] = new SpellInfo(16, "Summon Angel", "Call a celestial warrior to your aid. Attack: +50. Duration: whole fight.", 80, 16, "Admoriasumumarie", false, "Summon"),
            [17] = new SpellInfo(17, "Divine Lightning", "Channel Aurelion's wrath as lightning. Damage: 80-100. Duration: 1 turn.", 90, 17, "Exdahabarie", false, "Attack"),
            [18] = new SpellInfo(18, "Restoration", "Full divine healing restores body and soul. Effect: 150-200 hp. Duration: 1 turn.", 100, 18, "Exmasumarie", false, "Heal"),
            [19] = new SpellInfo(19, "Holy Word", "Speak a word of power that wounds the unholy. Damage: 100-130. Duration: 1 turn.", 110, 19, "Sanctuverbum", false, "Attack"),
            [20] = new SpellInfo(20, "Divine Intervention", "The gods themselves protect you. Protection: +80. Duration: 5 rounds.", 120, 20, "Divineprotarie", false, "Buff"),

            // --- LEGENDARY TIER (Levels 76-100) - Godlike Powers ---
            [21] = new SpellInfo(21, "Aurelion's Radiance", "Channel the Sun God's blinding light. Damage: 150-180 to all. Duration: 1 turn.", 140, 21, "Aurelionluxarie", true, "Attack"),
            [22] = new SpellInfo(22, "Resurrection Prayer", "Call back the nearly dead. Effect: 300-400 hp. Duration: 1 turn.", 160, 22, "Resurrectarie", false, "Heal"),
            [23] = new SpellInfo(23, "Divine Avatar", "Become a vessel of divine power. All stats +50%. Duration: whole fight.", 180, 23, "Avatardivinarie", false, "Buff"),
            [24] = new SpellInfo(24, "Judgment", "Pass divine judgment on the wicked. Damage: 200-280. Extra vs evil. Duration: 1 turn.", 200, 24, "Judiciumarie", false, "Attack"),
            [25] = new SpellInfo(25, "God's Finger", "The ultimate divine strike from the heavens themselves. Damage: 300-400. Duration: 1 turn.", 250, 25, "Umbarakahstahx", true, "Attack")
        },

        // ═══════════════════════════════════════════════════════════════════════════════
        // MAGICIAN SPELLS - Arcane magic, elemental power, reality manipulation
        // Themed around the Old God Manwe (Creation) and the primal elements
        // ═══════════════════════════════════════════════════════════════════════════════
        [CharacterClass.Magician] = new Dictionary<int, SpellInfo>
        {
            // --- EARLY TIER (Levels 1-25) - Apprentice Magic ---
            [1] = new SpellInfo(1, "Magic Missile", "Steel arrows of force strike the target. Damage: 4-7. Duration: 1 turn.", 5, 1, "Exmamarie", false, "Attack"),
            [2] = new SpellInfo(2, "Arcane Shield", "A shimmering barrier deflects attacks. Protection: +8. Duration: whole fight.", 8, 2, "Exmasumarie", false, "Buff"),
            [3] = new SpellInfo(3, "Spark", "A jolt of electricity stuns and damages. Damage: 8-12. Duration: 1 turn.", 10, 3, "Sparkarie", false, "Attack"),
            [4] = new SpellInfo(4, "Sleep", "Lull the target into magical slumber. Effect: target cannot act. Duration: varies.", 15, 4, "Exdamarie", false, "Debuff"),
            [5] = new SpellInfo(5, "Frost Touch", "Chill your enemy to the bone. Damage: 15-22. Duration: 1 turn.", 18, 5, "Frostarie", false, "Attack"),
            [6] = new SpellInfo(6, "Web", "Conjure sticky strands to trap your foe. Effect: cannot move. Duration: varies.", 22, 6, "Exmasesamamarie", false, "Debuff"),
            [7] = new SpellInfo(7, "Haste", "Accelerate time around yourself. Extra attacks per round. Duration: whole fight.", 25, 7, "Quicksilvarie", false, "Buff"),

            // --- MID TIER (Levels 26-50) - Journeyman Sorcery ---
            [8] = new SpellInfo(8, "Power Hat", "Regenerate health and bolster defenses. Effect: 40-60 hp + 10 protection. Duration: whole fight.", 30, 8, "Excadammarie", false, "Heal"),
            [9] = new SpellInfo(9, "Fireball", "A sphere of flame engulfs your enemy. Damage: 50-65. Duration: 1 turn.", 35, 9, "Exammmarie", false, "Attack"),
            [10] = new SpellInfo(10, "Fear", "Project terror into your enemy's mind. Effect: reduced effectiveness. Duration: varies.", 40, 10, "Examasumarie", false, "Debuff"),
            [11] = new SpellInfo(11, "Lightning Bolt", "A crackling bolt of electricity. Damage: 55-70. Duration: 1 turn.", 45, 11, "Exmasesaexmarie", false, "Attack"),
            [12] = new SpellInfo(12, "Mirror Image", "Create illusory duplicates of yourself. Protection: +30. Duration: whole fight.", 50, 12, "Mirrorimarie", false, "Buff"),
            [13] = new SpellInfo(13, "Ice Storm", "Razor shards of ice assault all enemies. Damage: 45-60 to all. Duration: 1 turn.", 55, 13, "Icestormarie", true, "Attack"),
            [14] = new SpellInfo(14, "Prismatic Shield", "A cage of light protects you. Protection: +40. Duration: whole fight.", 60, 14, "Exmasummasumarie", false, "Buff"),

            // --- HIGH TIER (Levels 51-75) - Master Wizardry ---
            [15] = new SpellInfo(15, "Chain Lightning", "Lightning arcs between multiple foes. Damage: 70-90 to all. Duration: 1 turn.", 75, 15, "Chainlightarie", true, "Attack"),
            [16] = new SpellInfo(16, "Disintegrate", "Unravel the target's physical form. Damage: 100-130. Duration: 1 turn.", 85, 16, "Disintegrarie", false, "Attack"),
            [17] = new SpellInfo(17, "Pillar of Fire", "A column of flame that penetrates all armor. Damage: 110-140. Duration: 1 turn.", 95, 17, "Exdammasumarie", false, "Attack"),
            [18] = new SpellInfo(18, "Time Stop", "Briefly halt time itself. Extra turn + protection. Duration: 1 round.", 110, 18, "Timestoparie", false, "Buff"),
            [19] = new SpellInfo(19, "Meteor Swarm", "Call down flaming rocks from the sky. Damage: 120-150 to all. Duration: 1 turn.", 125, 19, "Meteorarie", true, "Attack"),
            [20] = new SpellInfo(20, "Arcane Immunity", "Become immune to lesser magics. Protection: +60. Duration: whole fight.", 140, 20, "Immunemagarie", false, "Buff"),

            // --- LEGENDARY TIER (Levels 76-100) - Archmage Powers ---
            [21] = new SpellInfo(21, "Power Word: Stun", "A single word paralyzes your enemy. Effect: cannot act 3 rounds. Duration: 3 rounds.", 160, 21, "Stunverbum", false, "Debuff"),
            [22] = new SpellInfo(22, "Manwe's Creation", "Channel the Creator's power to reshape reality. Damage: 180-220. Duration: 1 turn.", 180, 22, "Manwecreatie", false, "Attack"),
            [23] = new SpellInfo(23, "Summon Demon", "Call a demon from the abyss. Attack: +100. Duration: whole fight.", 200, 23, "Excadexsumarie", false, "Summon"),
            [24] = new SpellInfo(24, "Power Word: Kill", "The ultimate death magic. Damage: 250-320. Duration: 1 turn.", 230, 24, "Mattravidduzzievh", false, "Attack"),
            [25] = new SpellInfo(25, "Wish", "Bend reality to your will. All stats doubled. Duration: whole fight.", 300, 25, "Ultimawisharie", false, "Buff")
        },

        // ═══════════════════════════════════════════════════════════════════════════════
        // SAGE SPELLS - Mind magic, nature, shadow arts, forbidden knowledge
        // Themed around Ocean Philosophy, balance, and the wisdom of ages
        // ═══════════════════════════════════════════════════════════════════════════════
        [CharacterClass.Sage] = new Dictionary<int, SpellInfo>
        {
            // --- EARLY TIER (Levels 1-25) - First Awakening (Shore Walker) ---
            [1] = new SpellInfo(1, "Fog of War", "Mist obscures the battlefield. Only you see clearly. Protection: +5. Duration: whole fight.", 5, 1, "Exadmasaxmarie", false, "Buff"),
            [2] = new SpellInfo(2, "Poison Touch", "Inflict magical toxins on your enemy. Damage over time. Duration: varies.", 8, 2, "Exadlimmarie", false, "Debuff"),
            [3] = new SpellInfo(3, "Mind Spike", "A psychic attack that damages and disorients. Damage: 8-14. Duration: 1 turn.", 10, 3, "Mindspikearie", false, "Attack"),
            [4] = new SpellInfo(4, "Freeze", "Encase the target in ice. Effect: cannot move. Duration: varies.", 15, 4, "Excadaliemarie", false, "Debuff"),
            [5] = new SpellInfo(5, "Duplicate", "Create an illusory copy to confuse enemies. Protection: +12. Duration: whole fight.", 18, 5, "Exmassesumarie", false, "Buff"),
            [6] = new SpellInfo(6, "Roast", "Hellfire pierces all armor. Damage: 20-30. Duration: 1 turn.", 22, 6, "Exdamseaxmarie", false, "Attack"),
            [7] = new SpellInfo(7, "Confusion", "Muddle your enemy's thoughts. Effect: may attack self. Duration: varies.", 25, 7, "Confusarie", false, "Debuff"),

            // --- MID TIER (Levels 26-50) - Second Awakening (Tide Reader) ---
            [8] = new SpellInfo(8, "Hit Self", "Force the target to strike itself. Damage: 40-55. Duration: 1 turn.", 30, 8, "Exadliemasumarie", false, "Attack"),
            [9] = new SpellInfo(9, "Escape", "Vanish from battle instantly. Success based on level. Effect: ends combat.", 35, 9, "Exemarie", false, "Escape"),
            [10] = new SpellInfo(10, "Giant Form", "Transform into a mighty giant. Attack: +30. Duration: whole fight.", 40, 10, "Excadmassumarie", false, "Buff"),
            [11] = new SpellInfo(11, "Steal Life", "Drain the enemy's vitality. Damage: 35-50, heals half. Duration: 1 turn.", 45, 11, "Steallifearie", false, "Attack"),
            [12] = new SpellInfo(12, "Psychic Scream", "A mental blast assaults all enemies. Damage: 40-55 to all. Duration: 1 turn.", 50, 12, "Psychscrearie", true, "Attack"),
            [13] = new SpellInfo(13, "Shadow Cloak", "Wrap yourself in living shadow. Protection: +35. Duration: whole fight.", 55, 13, "Shadowcloakarie", false, "Buff"),
            [14] = new SpellInfo(14, "Dominate", "Seize control of a weak-minded foe. Effect: enemy attacks allies. Duration: varies.", 60, 14, "Dominatarie", false, "Debuff"),

            // --- HIGH TIER (Levels 51-75) - Third Awakening (Wave Dancer) ---
            [15] = new SpellInfo(15, "Energy Drain", "Force victim to channel energy into their own destruction. Damage: 90-120. Duration: 1 turn.", 75, 15, "Examdammasaxmarie", false, "Attack"),
            [16] = new SpellInfo(16, "Mind Blank", "Render your thoughts impervious. Protection: +50, immune to mind effects. Duration: whole fight.", 85, 16, "Mindblankarie", false, "Buff"),
            [17] = new SpellInfo(17, "Shadow Step", "Teleport through shadows to strike. Damage: 80-100, ignores defense. Duration: 1 turn.", 95, 17, "Shadowsteparie", false, "Attack"),
            [18] = new SpellInfo(18, "Summon Demon", "Call a servant-demon from the nether. Attack: +70. Duration: whole fight.", 110, 18, "Edujnomed", false, "Summon"),
            [19] = new SpellInfo(19, "Mass Confusion", "Drive all enemies mad with visions. Effect: all enemies confused. Duration: varies.", 125, 19, "Massconfusarie", true, "Debuff"),
            [20] = new SpellInfo(20, "Noctura's Veil", "The Shadow Goddess protects her follower. Protection: +70. Duration: whole fight.", 140, 20, "Nocturaveilarie", false, "Buff"),

            // --- LEGENDARY TIER (Levels 76-100) - Deep Awakening (Abyss Gazer) ---
            [21] = new SpellInfo(21, "Soul Rend", "Tear the very soul from your enemy. Damage: 150-200. Duration: 1 turn.", 160, 21, "Soulrendarie", false, "Attack"),
            [22] = new SpellInfo(22, "Ocean's Memory", "Tap into the infinite wisdom. All spells cost half mana. Duration: whole fight.", 180, 22, "Oceanmemarie", false, "Buff"),
            [23] = new SpellInfo(23, "Temporal Paradox", "Trap the enemy in a time loop. Damage: 180-220. Duration: 1 turn.", 200, 23, "Temporalarie", false, "Attack"),
            [24] = new SpellInfo(24, "Veloura's Embrace", "Channel the lost Goddess of Love. Heals 250 + Protection +80. Duration: whole fight.", 230, 24, "Velouralovearie", false, "Heal"),
            [25] = new SpellInfo(25, "Death Kiss", "The ultimate draining of life force. Damage: 280-380. Duration: 1 turn.", 300, 25, "Exmasdamliemasumarie", false, "Attack")
        }
    };
    
    /// <summary>
    /// Calculate mana cost for a given spell and caster.
    /// Uses StatEffectsSystem for Wisdom-based mana cost reduction.
    ///
    /// BALANCE: Spells have a higher base cost (10 + level*5) to ensure they always
    /// cost meaningful mana even with high Wisdom. This prevents "free" spell spam.
    /// The minimum cost also scales with spell level to keep low-level spells
    /// from becoming trivially cheap at high character levels.
    /// </summary>
    public static int CalculateManaCost(SpellInfo spell, Character caster)
    {
        if (spell == null || caster == null) return 0;

        // Base cost: 10 + (spell level * 5)
        // Level 1 spell = 15 base cost
        // Level 10 spell = 60 base cost
        // Level 25 spell = 135 base cost
        int baseCost = 10 + (spell.Level * 5);

        // Apply Wisdom-based mana cost reduction from StatEffectsSystem (max 50%)
        int reductionPercent = StatEffectsSystem.GetManaCostReduction(caster.Wisdom);
        int cost = baseCost - (baseCost * reductionPercent / 100);

        // Minimum cost scales with spell level to prevent low-level spell spam
        // Level 1 = min 5 mana, Level 10 = min 10 mana, Level 25 = min 15 mana
        int minimumCost = 5 + (spell.Level / 5);

        int finalCost = Math.Max(minimumCost, cost);

        // Ocean's Memory spell effect - halves all mana costs
        if (caster.HasOceanMemory)
        {
            finalCost = Math.Max(1, finalCost / 2);
        }

        return finalCost;
    }
    
    /// <summary>
    /// Get spell cost for character class and spell level
    /// </summary>
    public static int GetSpellCost(CharacterClass characterClass, int spellLevel)
    {
        if (spellLevel < 1 || spellLevel > GameConfig.MaxSpellLevel)
            return 0;
            
        return GameConfig.BaseSpellManaCost + ((spellLevel - 1) * GameConfig.ManaPerSpellLevel);
    }
    
    /// <summary>
    /// Get level requirement for spell - 25 spells spread across 100 levels
    /// Early spells (1-7): Levels 1-25 (every ~3-4 levels)
    /// Mid spells (8-14): Levels 26-50 (every ~3-4 levels)
    /// High spells (15-20): Levels 51-75 (every ~4-5 levels)
    /// Legendary spells (21-25): Levels 76-100 (every ~5 levels)
    /// </summary>
    public static int GetLevelRequired(CharacterClass characterClass, int spellLevel)
    {
        return spellLevel switch
        {
            // EARLY TIER - Levels 1-25
            1 => 1,    // Starting spell
            2 => 4,    // Basic protection
            3 => 8,    // First utility
            4 => 12,   // Enhanced basics
            5 => 16,   // Stronger attacks
            6 => 20,   // Mid-early power
            7 => 25,   // Gateway to mid-tier

            // MID TIER - Levels 26-50
            8 => 28,   // Mid-tier entry
            9 => 32,   // Core damage
            10 => 36,  // Strong buffs
            11 => 40,  // Advanced debuffs
            12 => 44,  // Multi-target
            13 => 47,  // AoE damage
            14 => 50,  // Gateway to high tier

            // HIGH TIER - Levels 51-75
            15 => 54,  // High-tier entry
            16 => 58,  // Summons/major power
            17 => 62,  // Heavy damage
            18 => 66,  // Major healing/utility
            19 => 70,  // Powerful AoE
            20 => 75,  // Gateway to legendary

            // LEGENDARY TIER - Levels 76-100
            21 => 80,  // Legendary entry
            22 => 85,  // Ancient powers
            23 => 90,  // Near-divine
            24 => 95,  // Ultimate power
            25 => 100, // Transcendent mastery

            _ => 100   // Failsafe for unknown spells
        };
    }
    
    /// <summary>
    /// Get spell information for character class and spell level
    /// </summary>
    public static SpellInfo GetSpellInfo(CharacterClass characterClass, int spellLevel)
    {
        if (SpellBook.TryGetValue(characterClass, out var classSpells) &&
            classSpells.TryGetValue(spellLevel, out var spellInfo))
        {
            return spellInfo;
        }
        
        return null;
    }
    
    /// <summary>
    /// Get all available spells for character
    /// Only returns spells that have been learned (Spell[level-1][0] == true)
    /// </summary>
    public static List<SpellInfo> GetAvailableSpells(Character character)
    {
        var availableSpells = new List<SpellInfo>();

        if (character == null)
            return availableSpells;

        if (!SpellBook.TryGetValue(character.Class, out var classSpells))
            return availableSpells;

        foreach (var spell in classSpells.Values)
        {
            // Check level requirement
            if (character.Level < spell.LevelRequired)
                continue;

            // Check if spell has been learned (Spell array holds learned status)
            // Spell[spellLevel-1][0] = true means the spell is learned
            int spellIndex = spell.Level - 1;
            if (character.Spell != null &&
                spellIndex >= 0 &&
                spellIndex < character.Spell.Count &&
                character.Spell[spellIndex].Count > 0 &&
                character.Spell[spellIndex][0])
            {
                availableSpells.Add(spell);
            }
        }

        return availableSpells;
    }

    /// <summary>
    /// Convert a spell level to a quickbar slot ID (e.g. 3 -> "spell:3")
    /// </summary>
    public static string GetQuickbarId(int spellLevel) => $"spell:{spellLevel}";

    /// <summary>
    /// Parse a quickbar ID back to a spell level. Returns null if not a spell ID.
    /// </summary>
    public static int? ParseQuickbarSpellLevel(string? quickbarId)
    {
        if (quickbarId != null && quickbarId.StartsWith("spell:") &&
            int.TryParse(quickbarId.AsSpan(6), out int level))
            return level;
        return null;
    }

    /// <summary>
    /// Get ALL spells for a class (for learning menu - shows all spells regardless of learned status)
    /// </summary>
    public static List<SpellInfo> GetAllSpellsForClass(CharacterClass characterClass)
    {
        if (SpellBook.TryGetValue(characterClass, out var classSpells))
        {
            return classSpells.Values.ToList();
        }
        return new List<SpellInfo>();
    }

    /// <summary>
    /// Check if character can cast specific spell
    /// </summary>
    public static bool CanCastSpell(Character character, int spellLevel)
    {
        if (character == null)
            return false;

        if (spellLevel < 1 || spellLevel > GameConfig.MaxSpellLevel)
            return false;

        // Must be magic-using class
        if (character.Class != CharacterClass.Cleric &&
            character.Class != CharacterClass.Magician &&
            character.Class != CharacterClass.Sage)
            return false;
            
        // Level requirement
        if (character.Level < GetLevelRequired(character.Class, spellLevel))
            return false;
            
        var info = GetSpellInfo(character.Class, spellLevel);
        if (info == null) return false;

        var manaCost = CalculateManaCost(info, character);

        // Drug ManaBonus adds to effective mana for casting checks (e.g., ManaBoost: +50)
        var drugEffects = DrugSystem.GetDrugEffects(character);
        long effectiveMana = character.Mana + drugEffects.ManaBonus;

        return effectiveMana >= manaCost;
    }
    
    /// <summary>
    /// Cast spell - main spell casting function (Pascal CAST.PAS recreation)
    /// </summary>
    public static SpellResult CastSpell(Character caster, int spellLevel, Character? target = null, List<Character>? allTargets = null)
    {
        var result = new SpellResult();
        var random = new Random();

        if (!CanCastSpell(caster, spellLevel))
        {
            result.Success = false;
            result.Message = "Cannot cast this spell!";
            return result;
        }

        var spellInfo = GetSpellInfo(caster.Class, spellLevel);
        if (spellInfo == null)
        {
            result.Success = false;
            result.Message = "Unknown spell!";
            return result;
        }

        // === D20 TRAINING SYSTEM INTEGRATION ===
        // Get spell skill ID for this spell level
        string skillId = TrainingSystem.GetSpellSkillId(caster.Class, spellLevel);
        var proficiency = TrainingSystem.GetSkillProficiency(caster, skillId);

        // Calculate DC based on spell level (higher level spells are harder)
        int baseDC = 8 + spellLevel;

        // Roll for spell success using training system
        var rollResult = TrainingSystem.RollAbilityCheck(caster, skillId, baseDC, random);

        // Store roll info in result message
        result.RollInfo = $"[Roll: {rollResult.NaturalRoll} + {rollResult.Modifier} = {rollResult.Total} vs DC {baseDC}]";

        // Deduct mana cost regardless of success (casting attempt uses mana)
        var manaCost = CalculateManaCost(spellInfo, caster);
        result.ManaCost = manaCost;
        caster.Mana -= manaCost;

        // Check for spell failure
        if (rollResult.IsCriticalFailure || !rollResult.Success)
        {
            result.Success = false;
            if (rollResult.IsCriticalFailure)
            {
                result.Message = $"{caster.Name2} fumbles the spell! The magic fizzles harmlessly.";
                result.Message += $"\n  {result.RollInfo}";
                result.SpecialEffect = "fizzle";
            }
            else
            {
                result.Message = $"{caster.Name2} utters '{spellInfo.MagicWords}'... but the spell fails!";
                result.Message += $"\n  {result.RollInfo}";
                result.SpecialEffect = "fail";
            }

            // Can still learn from failure
            if (TrainingSystem.TryImproveFromUse(caster, skillId, random))
            {
                var newLevel = TrainingSystem.GetSkillProficiency(caster, skillId);
                result.SkillImproved = true;
                result.NewProficiencyLevel = TrainingSystem.GetProficiencyName(newLevel);
            }

            return result;
        }

        result.Success = true;
        result.Message = $"{caster.Name2} utters '{spellInfo.MagicWords}'!";
        result.IsMultiTarget = spellInfo.IsMultiTarget;

        // Track archetype - Magician for spell casting
        bool isTransformative = spellInfo.SpellType == "Buff" || spellInfo.SpellType == "Transform";
        ArchetypeTracker.Instance.RecordSpellCast(isTransformative);

        // Store proficiency multiplier for damage/healing scaling
        result.ProficiencyMultiplier = TrainingSystem.GetEffectMultiplier(proficiency);

        // Apply roll quality bonus (critical success = extra power)
        if (rollResult.IsCriticalSuccess)
        {
            result.ProficiencyMultiplier *= 1.5f; // 50% bonus on critical
            result.Message += " CRITICAL CAST!";
        }
        else if (rollResult.Total >= baseDC + 10)
        {
            result.ProficiencyMultiplier *= 1.25f; // 25% bonus on great roll
        }

        // Execute spell effects based on class and level
        ExecuteSpellEffect(caster, spellLevel, target, allTargets, result);

        // Chance to improve spell proficiency from use
        if (TrainingSystem.TryImproveFromUse(caster, skillId, random))
        {
            var newLevel = TrainingSystem.GetSkillProficiency(caster, skillId);
            result.SkillImproved = true;
            result.NewProficiencyLevel = TrainingSystem.GetProficiencyName(newLevel);
        }

        return result;
    }
    
    /// <summary>
    /// Execute specific spell effects (Pascal CAST.PAS implementation)
    /// Now with level-based scaling for damage and healing!
    /// </summary>
    private static void ExecuteSpellEffect(Character caster, int spellLevel, Character target, List<Character> allTargets, SpellResult result)
    {
        var random = new Random();

        // Get proficiency multiplier (stored in result by CastSpell)
        float profMult = result.ProficiencyMultiplier;

        switch (caster.Class)
        {
            case CharacterClass.Cleric:
                ExecuteClericSpell(caster, spellLevel, target, allTargets, result, random, profMult);
                break;
            case CharacterClass.Magician:
                ExecuteMagicianSpell(caster, spellLevel, target, allTargets, result, random, profMult);
                break;
            case CharacterClass.Sage:
                ExecuteSageSpell(caster, spellLevel, target, allTargets, result, random, profMult);
                break;
        }
    }

    /// <summary>
    /// Calculate level-scaled spell damage/healing
    /// Base damage is multiplied by a scaling factor based on caster level
    /// Level 1: 1.0x, Level 50: 2.5x, Level 100: 4.0x
    /// Now also applies proficiency multiplier from training system!
    /// Uses StatEffectsSystem for consistent stat-based bonuses.
    /// </summary>
    private static int ScaleSpellEffect(int baseEffect, Character caster, Random random, float proficiencyMult = 1.0f)
    {
        // Level scaling: starts at 1.0x, grows to 4.0x at level 100
        // Formula: 1.0 + (level * 0.03) gives 1.0 at level 0, 4.0 at level 100
        double levelMultiplier = 1.0 + (caster.Level * 0.03);

        // Use StatEffectsSystem for Intelligence-based spell damage multiplier
        double statBonus = StatEffectsSystem.GetSpellDamageMultiplier(caster.Intelligence);

        // Wisdom adds a smaller bonus for hybrid casters
        if (caster.Class == CharacterClass.Cleric || caster.Class == CharacterClass.Sage)
        {
            statBonus += (caster.Wisdom - 10) * 0.01; // +1% per Wisdom above 10
        }

        // Cap total stat bonus to prevent exponential damage at endgame
        statBonus = Math.Min(statBonus, 5.0);

        // Add some variance (±10%)
        double variance = 0.9 + (random.NextDouble() * 0.2);

        // Check for spell critical (from Intelligence)
        if (StatEffectsSystem.GetSpellCriticalChance(caster.Intelligence) > random.Next(100))
        {
            proficiencyMult *= 1.5f; // 50% bonus on spell crit
        }

        // Apply drug SpellPowerBonus (e.g., ManaBoost: +20% spell power)
        var drugEffects = DrugSystem.GetDrugEffects(caster);
        double drugSpellMult = 1.0 + drugEffects.SpellPowerBonus / 100.0;

        // Calculate final effect including proficiency bonus
        double scaledEffect = baseEffect * levelMultiplier * statBonus * variance * proficiencyMult * drugSpellMult;

        return Math.Max(1, (int)scaledEffect);
    }

    /// <summary>
    /// Calculate level-scaled healing (balanced with damage scaling)
    /// Level 1: 1.0x, Level 50: 2.25x, Level 100: 3.5x
    /// Now also applies proficiency multiplier from training system!
    /// Uses StatEffectsSystem for Wisdom-based healing bonus.
    /// </summary>
    private static int ScaleHealingEffect(int baseHealing, Character caster, Random random, float proficiencyMult = 1.0f)
    {
        // Healing scales slightly slower than damage but still meaningful
        // 2.5% per level (damage is 3%) gives good progression
        double levelMultiplier = 1.0 + (caster.Level * 0.025);

        // Use StatEffectsSystem for Wisdom-based healing multiplier
        // Drug WisdomBonus adds to effective Wisdom for healing (e.g., ThirdEye: +15)
        var drugEffects = DrugSystem.GetDrugEffects(caster);
        long effectiveWisdom = caster.Wisdom + drugEffects.WisdomBonus;
        double wisdomBonus = StatEffectsSystem.GetHealingMultiplier(effectiveWisdom);

        double variance = 0.95 + (random.NextDouble() * 0.1);

        // Include proficiency bonus from training
        double scaledHealing = baseHealing * levelMultiplier * wisdomBonus * variance * proficiencyMult;

        return Math.Max(1, (int)scaledHealing);
    }
    
    /// <summary>
    /// Execute Cleric spells (healing and divine magic)
    /// 25 spells spread across levels 1-100, themed around the New Pantheon
    /// </summary>
    private static void ExecuteClericSpell(Character caster, int spellLevel, Character target, List<Character> allTargets, SpellResult result, Random random, float profMult = 1.0f)
    {
        switch (spellLevel)
        {
            // --- EARLY TIER (Levels 1-25) ---
            case 1: // Cure Light - Base: 12-22 hp
                int baseHeal1 = 12 + random.Next(11);
                result.Healing = ScaleHealingEffect(baseHeal1, caster, random, profMult);
                result.Message += $" {caster.Name2} regains {result.Healing} hitpoints!";
                break;

            case 2: // Divine Shield - Protection +8
                int baseProtection2 = (int)((8 + (caster.Level / 12)) * profMult);
                result.ProtectionBonus = baseProtection2;
                result.Duration = 999;
                result.Message += $" {caster.Name2} feels protected! (+{result.ProtectionBonus} defense)";
                break;

            case 3: // Purify - Cure disease
                result.SpecialEffect = "cure_disease";
                result.Message += $" {caster.Name2} is cleansed of afflictions!";
                break;

            case 4: // Bless Weapon - Attack +12
                int baseAttack4 = (int)((12 + (caster.Level / 10)) * profMult);
                result.AttackBonus = baseAttack4;
                result.Duration = 999;
                result.Message += $" {caster.Name2}'s weapon glows with holy light! (+{result.AttackBonus} attack)";
                break;

            case 5: // Cure Wounds - Base: 25-40 hp
                int baseHeal5 = 25 + random.Next(16);
                result.Healing = ScaleHealingEffect(baseHeal5, caster, random, profMult);
                result.Message += $" {caster.Name2} regains {result.Healing} hitpoints!";
                break;

            case 6: // Turn Undead - Damage vs undead
                int baseDamage6 = 35 + random.Next(16);
                result.Damage = ScaleSpellEffect(baseDamage6, caster, random, profMult);
                result.SpecialEffect = "holy";
                result.Message += $" Holy light sears undead foes for {result.Damage} damage!";
                break;

            case 7: // Sanctuary - Protection +18
                int baseProtection7 = (int)((18 + (caster.Level / 7)) * profMult);
                result.ProtectionBonus = baseProtection7;
                result.Duration = 3;
                result.Message += $" A holy sanctuary protects {caster.Name2}! (+{result.ProtectionBonus} defense)";
                break;

            // --- MID TIER (Levels 26-50) ---
            case 8: // Cure Critical - Base: 50-75 hp
                int baseHeal8 = 50 + random.Next(26);
                result.Healing = ScaleHealingEffect(baseHeal8, caster, random, profMult);
                if (caster.Darkness > 0) caster.Darkness = Math.Max(0, caster.Darkness - 10);
                else caster.Chivalry += 10;
                result.Message += $" {caster.Name2} feels blessed and regains {result.Healing} hitpoints!";
                break;

            case 9: // Holy Smite - Base: 45-65 damage
                int baseDamage9 = 45 + random.Next(21);
                result.Damage = ScaleSpellEffect(baseDamage9, caster, random, profMult);
                result.SpecialEffect = "holy";
                result.Message += $" Righteous fury strikes {target?.Name2 ?? "the enemy"} for {result.Damage} damage!";
                break;

            case 10: // Armor of Faith - Protection +28
                int baseProtection10 = (int)((28 + (caster.Level / 5)) * profMult);
                result.ProtectionBonus = baseProtection10;
                result.Duration = 999;
                result.Message += $" Divine armor surrounds {caster.Name2}! (+{result.ProtectionBonus} defense)";
                break;

            case 11: // Dispel Evil - Remove enemy buffs
                result.SpecialEffect = "dispel";
                result.Message += $" Dark enchantments are banished!";
                break;

            case 12: // Mass Cure - Base: 35-55 hp to all
                int baseHeal12 = 35 + random.Next(21);
                result.Healing = ScaleHealingEffect(baseHeal12, caster, random, profMult);
                result.IsMultiTarget = true;
                result.Message += $" Healing light restores {result.Healing} hitpoints to all allies!";
                break;

            case 13: // Holy Explosion - Base: 55-80 damage to all
                int baseDamage13 = 55 + random.Next(26);
                result.Damage = ScaleSpellEffect(baseDamage13, caster, random, profMult);
                result.IsMultiTarget = true;
                result.Message += $" A holy explosion deals {result.Damage} damage to all enemies!";
                break;

            case 14: // Prayer of Fortitude - +25 all defenses
                int baseProtection14 = (int)((25 + (caster.Level / 5)) * profMult);
                result.ProtectionBonus = baseProtection14;
                result.Duration = 999;
                result.Message += $" Body and spirit are strengthened! (+{result.ProtectionBonus} defense)";
                break;

            // --- HIGH TIER (Levels 51-75) ---
            case 15: // Invisibility - Protection +45
                int baseProtection15 = (int)((45 + (caster.Level / 4)) * profMult);
                result.ProtectionBonus = baseProtection15;
                result.Duration = 999;
                result.SpecialEffect = "invisible";
                result.Message += $" {caster.Name2} becomes invisible! (+{result.ProtectionBonus} defense)";
                break;

            case 16: // Summon Angel - Attack +55
                int baseAttack16 = (int)((55 + (caster.Level)) * profMult);
                result.AttackBonus = baseAttack16;
                result.Duration = 999;
                result.SpecialEffect = "angel";
                result.Message += $" An Angel descends with golden wings! (+{result.AttackBonus} attack)";
                break;

            case 17: // Divine Lightning - Base: 90-120 damage
                int baseDamage17 = 90 + random.Next(31);
                result.Damage = ScaleSpellEffect(baseDamage17, caster, random, profMult);
                result.Message += $" Divine lightning strikes {target?.Name2 ?? "the enemy"} for {result.Damage} damage!";
                break;

            case 18: // Restoration - Base: 160-220 hp
                int baseHeal18 = 160 + random.Next(61);
                result.Healing = ScaleHealingEffect(baseHeal18, caster, random, profMult);
                result.Message += $" {caster.Name2} is fully restored with {result.Healing} hitpoints!";
                break;

            case 19: // Holy Word - Base: 110-145 damage
                int baseDamage19 = 110 + random.Next(36);
                result.Damage = ScaleSpellEffect(baseDamage19, caster, random, profMult);
                result.SpecialEffect = "holy";
                result.Message += $" The Holy Word wounds the unholy for {result.Damage} damage!";
                break;

            case 20: // Divine Intervention - Protection +85
                int baseProtection20 = (int)((85 + (caster.Level / 2)) * profMult);
                result.ProtectionBonus = baseProtection20;
                result.Duration = 5;
                result.SpecialEffect = "divination";
                result.Message += $" The gods themselves protect {caster.Name2}! (+{result.ProtectionBonus} defense)";
                break;

            // --- LEGENDARY TIER (Levels 76-100) ---
            case 21: // Aurelion's Radiance - Base: 160-220 damage to all
                int baseDamage21 = 160 + random.Next(61);
                result.Damage = ScaleSpellEffect(baseDamage21, caster, random, profMult);
                result.IsMultiTarget = true;
                result.SpecialEffect = "holy";
                result.Message += $" Aurelion's blinding radiance deals {result.Damage} damage to all!";
                break;

            case 22: // Resurrection Prayer - Base: 320-450 hp
                int baseHeal22 = 320 + random.Next(131);
                result.Healing = ScaleHealingEffect(baseHeal22, caster, random, profMult);
                result.Message += $" Life itself is restored! {caster.Name2} regains {result.Healing} hitpoints!";
                break;

            case 23: // Divine Avatar - All stats +55
                int baseBonus23 = (int)((55 + (caster.Level)) * profMult);
                result.AttackBonus = baseBonus23;
                result.ProtectionBonus = baseBonus23;
                result.Duration = 999;
                result.SpecialEffect = "avatar";
                result.Message += $" {caster.Name2} becomes a divine avatar! (+{baseBonus23} to all)";
                break;

            case 24: // Judgment - Base: 220-320 damage
                int baseDamage24 = 220 + random.Next(101);
                result.Damage = ScaleSpellEffect(baseDamage24, caster, random, profMult);
                result.SpecialEffect = "holy";
                if (caster.Darkness > 0) caster.Darkness = Math.Max(0, caster.Darkness - 30);
                else caster.Chivalry += 30;
                result.Message += $" Divine judgment strikes for {result.Damage} damage!";
                break;

            case 25: // God's Finger - Base: 320-450 damage to all
                int baseDamage25 = 320 + random.Next(131);
                result.Damage = ScaleSpellEffect(baseDamage25, caster, random, profMult);
                result.IsMultiTarget = true;
                result.SpecialEffect = "holy";
                result.Message += $" The finger of God strikes down for {result.Damage} damage!";
                break;
        }
    }
    
    /// <summary>
    /// Execute Magician spells (elemental and arcane magic)
    /// 25 spells spread across levels 1-100, themed around Manwe and primal elements
    /// </summary>
    private static void ExecuteMagicianSpell(Character caster, int spellLevel, Character target, List<Character> allTargets, SpellResult result, Random random, float profMult = 1.0f)
    {
        switch (spellLevel)
        {
            // --- EARLY TIER (Levels 1-25) ---
            case 1: // Magic Missile - Base: 10-18 damage
                int baseDamage1 = 10 + random.Next(9);
                result.Damage = ScaleSpellEffect(baseDamage1, caster, random, profMult);
                result.Message += $" Magic missiles strike {target?.Name2 ?? "the target"} for {result.Damage} damage!";
                break;

            case 2: // Arcane Shield - Protection +10
                int baseProtection2 = (int)((10 + (caster.Level / 10)) * profMult);
                result.ProtectionBonus = baseProtection2;
                result.Duration = 999;
                result.Message += $" A shimmering shield surrounds {caster.Name2}! (+{result.ProtectionBonus} defense)";
                break;

            case 3: // Spark - Base: 18-28 damage
                int baseDamage3 = 18 + random.Next(11);
                result.Damage = ScaleSpellEffect(baseDamage3, caster, random, profMult);
                result.SpecialEffect = "lightning";
                result.Message += $" Sparks jolt {target?.Name2 ?? "the target"} for {result.Damage} damage!";
                break;

            case 4: // Sleep
                if (target != null)
                {
                    result.SpecialEffect = "sleep";
                    result.Duration = (int)((random.Next(5) + 3 + (caster.Level / 20)) * profMult);
                    result.Message += $" {target.Name2} falls into magical slumber!";
                }
                break;

            case 5: // Frost Touch - Base: 28-42 damage
                int baseDamage5 = 28 + random.Next(15);
                result.Damage = ScaleSpellEffect(baseDamage5, caster, random, profMult);
                result.SpecialEffect = "frost";
                result.Message += $" Frost chills {target?.Name2 ?? "the target"} for {result.Damage} damage!";
                break;

            case 6: // Web
                if (target != null)
                {
                    result.SpecialEffect = "web";
                    result.Duration = (int)((random.Next(4) + 2 + (caster.Level / 20)) * profMult);
                    result.Message += $" A Magic Web traps {target.Name2}!";
                }
                break;

            case 7: // Haste - Attack bonus
                int baseAttack7 = (int)((18 + (caster.Level / 5)) * profMult);
                result.AttackBonus = baseAttack7;
                result.Duration = 999;
                result.SpecialEffect = "haste";
                result.Message += $" {caster.Name2} accelerates through time! (+{result.AttackBonus} attack)";
                break;

            // --- MID TIER (Levels 26-50) ---
            case 8: // Power Hat - Base: 50-75 hp + protection
                int baseHeal8 = 50 + random.Next(26);
                result.Healing = ScaleHealingEffect(baseHeal8, caster, random, profMult);
                int baseProtection8 = (int)((12 + (caster.Level / 8)) * profMult);
                result.ProtectionBonus = baseProtection8;
                result.Duration = 999;
                result.Message += $" {caster.Name2} regains {result.Healing} hp! (+{result.ProtectionBonus} defense)";
                break;

            case 9: // Fireball - Base: 55-75 damage
                int baseDamage9 = 55 + random.Next(21);
                result.Damage = ScaleSpellEffect(baseDamage9, caster, random, profMult);
                result.SpecialEffect = "fire";
                result.Message += $" A Fireball engulfs {target?.Name2 ?? "the target"} for {result.Damage} damage!";
                break;

            case 10: // Fear
                if (target != null)
                {
                    result.SpecialEffect = "fear";
                    result.Duration = (int)((random.Next(6) + 2 + (caster.Level / 15)) * profMult);
                    result.Message += $" {target.Name2} is overwhelmed by terror!";
                }
                break;

            case 11: // Lightning Bolt - Base: 60-80 damage
                int baseDamage11 = 60 + random.Next(21);
                result.Damage = ScaleSpellEffect(baseDamage11, caster, random, profMult);
                result.SpecialEffect = "lightning";
                result.Message += $" Lightning strikes {target?.Name2 ?? "the target"} for {result.Damage} damage!";
                break;

            case 12: // Mirror Image - Protection +35
                int baseProtection12 = (int)((35 + (caster.Level / 4)) * profMult);
                result.ProtectionBonus = baseProtection12;
                result.Duration = 999;
                result.SpecialEffect = "mirror";
                result.Message += $" Illusory duplicates confuse enemies! (+{result.ProtectionBonus} defense)";
                break;

            case 13: // Ice Storm - Base: 50-70 damage to all
                int baseDamage13 = 50 + random.Next(21);
                result.Damage = ScaleSpellEffect(baseDamage13, caster, random, profMult);
                result.IsMultiTarget = true;
                result.SpecialEffect = "frost";
                result.Message += $" An Ice Storm assaults all foes for {result.Damage} damage!";
                break;

            case 14: // Prismatic Shield - Protection +45
                int baseProtection14 = (int)((45 + (caster.Level / 4)) * profMult);
                result.ProtectionBonus = baseProtection14;
                result.Duration = 999;
                result.Message += $" A prismatic cage protects {caster.Name2}! (+{result.ProtectionBonus} defense)";
                break;

            // --- HIGH TIER (Levels 51-75) ---
            case 15: // Chain Lightning - Base: 80-105 damage to all
                int baseDamage15 = 80 + random.Next(26);
                result.Damage = ScaleSpellEffect(baseDamage15, caster, random, profMult);
                result.IsMultiTarget = true;
                result.SpecialEffect = "lightning";
                result.Message += $" Chain lightning arcs through all enemies for {result.Damage} damage!";
                break;

            case 16: // Disintegrate - Base: 110-150 damage
                int baseDamage16 = 110 + random.Next(41);
                result.Damage = ScaleSpellEffect(baseDamage16, caster, random, profMult);
                result.SpecialEffect = "disintegrate";
                result.Message += $" {target?.Name2 ?? "The target"} is disintegrated for {result.Damage} damage!";
                break;

            case 17: // Pillar of Fire - Base: 120-160 damage
                int baseDamage17 = 120 + random.Next(41);
                result.Damage = ScaleSpellEffect(baseDamage17, caster, random, profMult);
                result.SpecialEffect = "fire";
                result.Message += $" A Pillar of Fire consumes {target?.Name2 ?? "the target"} for {result.Damage} damage!";
                break;

            case 18: // Time Stop - Extra turn
                int baseBonus18 = (int)((35 + (caster.Level / 3)) * profMult);
                result.AttackBonus = baseBonus18;
                result.ProtectionBonus = baseBonus18;
                result.Duration = 1;
                result.SpecialEffect = "timestop";
                result.Message += $" Time itself halts! (+{baseBonus18} to attack and defense)";
                break;

            case 19: // Meteor Swarm - Base: 130-180 damage to all
                int baseDamage19 = 130 + random.Next(51);
                result.Damage = ScaleSpellEffect(baseDamage19, caster, random, profMult);
                result.IsMultiTarget = true;
                result.SpecialEffect = "fire";
                result.Message += $" Meteors rain down for {result.Damage} damage to all!";
                break;

            case 20: // Arcane Immunity - Protection +65
                int baseProtection20 = (int)((65 + (caster.Level / 2)) * profMult);
                result.ProtectionBonus = baseProtection20;
                result.Duration = 999;
                result.SpecialEffect = "immunity";
                result.Message += $" {caster.Name2} becomes immune to lesser magic! (+{result.ProtectionBonus} defense)";
                break;

            // --- LEGENDARY TIER (Levels 76-100) ---
            case 21: // Power Word: Stun
                result.SpecialEffect = "stun";
                result.Duration = 3;
                result.Message += $" A word of power paralyzes {target?.Name2 ?? "the enemy"}!";
                break;

            case 22: // Manwe's Creation - Base: 200-280 damage
                int baseDamage22 = 200 + random.Next(81);
                result.Damage = ScaleSpellEffect(baseDamage22, caster, random, profMult);
                result.SpecialEffect = "creation";
                result.Message += $" Manwe's creative force destroys for {result.Damage} damage!";
                break;

            case 23: // Summon Demon - Attack +100
                int baseAttack23 = (int)((100 + (caster.Level)) * profMult);
                result.AttackBonus = baseAttack23;
                result.Duration = 999;
                result.SpecialEffect = "demon";
                result.Message += $" A demon is summoned from the abyss! (+{result.AttackBonus} attack)";
                break;

            case 24: // Power Word: Kill - Base: 280-380 damage
                int baseDamage24 = 280 + random.Next(101);
                result.Damage = ScaleSpellEffect(baseDamage24, caster, random, profMult);
                result.SpecialEffect = "death";
                result.Message += $" The POWER WORD KILL strikes for {result.Damage} damage!";
                break;

            case 25: // Wish - All stats doubled
                int baseBonus25 = (int)((100 + (caster.Level)) * profMult);
                result.AttackBonus = baseBonus25;
                result.ProtectionBonus = baseBonus25;
                result.Duration = 999;
                result.SpecialEffect = "wish";
                result.Message += $" Reality bends to {caster.Name2}'s will! (+{baseBonus25} to all)";
                break;
        }
    }
    
    /// <summary>
    /// Execute Sage spells (mind and nature magic)
    /// 25 spells spread across levels 1-100, themed around Ocean Philosophy
    /// </summary>
    private static void ExecuteSageSpell(Character caster, int spellLevel, Character target, List<Character> allTargets, SpellResult result, Random random, float profMult = 1.0f)
    {
        switch (spellLevel)
        {
            // --- EARLY TIER (Levels 1-25) - First Awakening ---
            case 1: // Fog of War - Protection +7
                int baseProtection1 = (int)((7 + (caster.Level / 12)) * profMult);
                result.ProtectionBonus = baseProtection1;
                result.Duration = 999;
                result.SpecialEffect = "fog";
                result.Message += $" Mist obscures the battlefield! (+{result.ProtectionBonus} defense)";
                break;

            case 2: // Poison Touch - DoT
                if (target != null)
                {
                    result.SpecialEffect = "poison";
                    result.Duration = (int)((random.Next(6) + 3 + (caster.Level / 20)) * profMult);
                    result.Message += $" {target.Name2} is poisoned!";
                }
                break;

            case 3: // Mind Spike - Base: 12-22 damage
                int baseDamage3 = 12 + random.Next(11);
                result.Damage = ScaleSpellEffect(baseDamage3, caster, random, profMult);
                result.SpecialEffect = "psychic";
                result.Message += $" A psychic spike strikes {target?.Name2 ?? "the target"} for {result.Damage} damage!";
                break;

            case 4: // Freeze
                if (target != null)
                {
                    result.SpecialEffect = "freeze";
                    result.Duration = (int)((random.Next(5) + 2 + (caster.Level / 20)) * profMult);
                    result.Message += $" {target.Name2} is frozen in ice!";
                }
                break;

            case 5: // Duplicate - Protection +14
                int baseProtection5 = (int)((14 + (caster.Level / 8)) * profMult);
                result.ProtectionBonus = baseProtection5;
                result.Duration = 999;
                result.SpecialEffect = "duplicate";
                result.Message += $" An illusory duplicate confuses enemies! (+{result.ProtectionBonus} defense)";
                break;

            case 6: // Roast - Base: 25-38 damage
                int baseDamage6 = 25 + random.Next(14);
                result.Damage = ScaleSpellEffect(baseDamage6, caster, random, profMult);
                result.SpecialEffect = "fire";
                result.Message += $" Hellfire scorches {target?.Name2 ?? "the target"} for {result.Damage} damage!";
                break;

            case 7: // Confusion
                if (target != null)
                {
                    result.SpecialEffect = "confusion";
                    result.Duration = (int)((random.Next(4) + 2 + (caster.Level / 20)) * profMult);
                    result.Message += $" {target.Name2}'s mind becomes muddled!";
                }
                break;

            // --- MID TIER (Levels 26-50) - Second Awakening ---
            case 8: // Hit Self - Base: 45-65 damage
                int baseDamage8 = 45 + random.Next(21);
                result.Damage = ScaleSpellEffect(baseDamage8, caster, random, profMult);
                result.SpecialEffect = "psychic";
                result.Message += $" {target?.Name2 ?? "The target"} strikes themselves for {result.Damage} damage!";
                break;

            case 9: // Escape
                result.SpecialEffect = "escape";
                result.Message += $" {caster.Name2} vanishes from battle!";
                break;

            case 10: // Giant Form - Attack +32
                int baseAttack10 = (int)((32 + (caster.Level / 4)) * profMult);
                result.AttackBonus = baseAttack10;
                result.Duration = 999;
                result.SpecialEffect = "giant";
                result.Message += $" {caster.Name2} transforms into a GIANT! (+{result.AttackBonus} attack)";
                break;

            case 11: // Steal Life - Base: 40-60 damage, heals half
                int baseDamage11 = 40 + random.Next(21);
                result.Damage = ScaleSpellEffect(baseDamage11, caster, random, profMult);
                result.Healing = result.Damage / 2;
                result.SpecialEffect = "drain";
                result.Message += $" Life is stolen for {result.Damage} damage! {caster.Name2} heals {result.Healing}!";
                break;

            case 12: // Psychic Scream - Base: 45-65 damage to all
                int baseDamage12 = 45 + random.Next(21);
                result.Damage = ScaleSpellEffect(baseDamage12, caster, random, profMult);
                result.IsMultiTarget = true;
                result.SpecialEffect = "psychic";
                result.Message += $" A psychic scream assaults all enemies for {result.Damage} damage!";
                break;

            case 13: // Shadow Cloak - Protection +38
                int baseProtection13 = (int)((38 + (caster.Level / 5)) * profMult);
                result.ProtectionBonus = baseProtection13;
                result.Duration = 999;
                result.SpecialEffect = "shadow";
                result.Message += $" Living shadow cloaks {caster.Name2}! (+{result.ProtectionBonus} defense)";
                break;

            case 14: // Dominate
                if (target != null)
                {
                    result.SpecialEffect = "dominate";
                    result.Duration = (int)((random.Next(3) + 2 + (caster.Level / 25)) * profMult);
                    result.Message += $" {caster.Name2} seizes control of {target.Name2}'s mind!";
                }
                break;

            // --- HIGH TIER (Levels 51-75) - Third Awakening ---
            case 15: // Energy Drain - Base: 95-130 damage
                int baseDamage15 = 95 + random.Next(36);
                result.Damage = ScaleSpellEffect(baseDamage15, caster, random, profMult);
                result.SpecialEffect = "drain";
                result.Message += $" Energy is drained from {target?.Name2 ?? "the target"} for {result.Damage} damage!";
                break;

            case 16: // Mind Blank - Protection +55, immune to mind
                int baseProtection16 = (int)((55 + (caster.Level / 3)) * profMult);
                result.ProtectionBonus = baseProtection16;
                result.Duration = 999;
                result.SpecialEffect = "mindblank";
                result.Message += $" {caster.Name2}'s mind becomes impervious! (+{result.ProtectionBonus} defense)";
                break;

            case 17: // Shadow Step - Base: 85-115 damage, ignores defense
                int baseDamage17 = 85 + random.Next(31);
                result.Damage = ScaleSpellEffect(baseDamage17, caster, random, profMult);
                result.SpecialEffect = "shadowstep";
                result.Message += $" {caster.Name2} strikes through shadows for {result.Damage} damage!";
                break;

            case 18: // Summon Demon - Attack +75
                int baseAttack18 = (int)((75 + (caster.Level)) * profMult);
                result.AttackBonus = baseAttack18;
                result.Duration = 999;
                result.SpecialEffect = "demon";
                result.Message += $" A servant-demon answers the call! (+{result.AttackBonus} attack)";
                break;

            case 19: // Mass Confusion - All enemies confused
                result.SpecialEffect = "mass_confusion";
                result.IsMultiTarget = true;
                result.Duration = (int)((random.Next(3) + 2 + (caster.Level / 30)) * profMult);
                result.Message += $" All enemies are driven mad with visions!";
                break;

            case 20: // Noctura's Veil - Protection +75
                int baseProtection20 = (int)((75 + (caster.Level / 2)) * profMult);
                result.ProtectionBonus = baseProtection20;
                result.Duration = 999;
                result.SpecialEffect = "shadow";
                result.Message += $" The Shadow Goddess protects {caster.Name2}! (+{result.ProtectionBonus} defense)";
                break;

            // --- LEGENDARY TIER (Levels 76-100) - Deep Awakening ---
            case 21: // Soul Rend - Base: 170-240 damage
                int baseDamage21 = 170 + random.Next(71);
                result.Damage = ScaleSpellEffect(baseDamage21, caster, random, profMult);
                result.SpecialEffect = "soul";
                result.Message += $" The soul is torn from {target?.Name2 ?? "the target"} for {result.Damage} damage!";
                break;

            case 22: // Ocean's Memory - Half mana cost
                result.SpecialEffect = "ocean_memory";
                result.Duration = 999;
                caster.HasOceanMemory = true;  // Set flag for half mana cost
                result.Message += $" {caster.Name2} taps into infinite wisdom! Spells cost half mana!";
                break;

            case 23: // Temporal Paradox - Base: 200-280 damage
                int baseDamage23 = 200 + random.Next(81);
                result.Damage = ScaleSpellEffect(baseDamage23, caster, random, profMult);
                result.SpecialEffect = "temporal";
                result.Message += $" {target?.Name2 ?? "The target"} is trapped in a time loop for {result.Damage} damage!";
                break;

            case 24: // Veloura's Embrace - Heal 280 + Protection +85
                int baseHeal24 = 240 + random.Next(81);
                result.Healing = ScaleHealingEffect(baseHeal24, caster, random, profMult);
                int baseProtection24 = (int)((85 + (caster.Level / 2)) * profMult);
                result.ProtectionBonus = baseProtection24;
                result.Duration = 999;
                result.Message += $" Veloura's love heals {result.Healing}! (+{result.ProtectionBonus} defense)";
                break;

            case 25: // Death Kiss - Base: 300-420 damage
                int baseDamage25 = 300 + random.Next(121);
                result.Damage = ScaleSpellEffect(baseDamage25, caster, random, profMult);
                result.SpecialEffect = "death";
                result.Message += $" The DEATH KISS drains all life for {result.Damage} damage!";
                break;
        }
    }
    
    /// <summary>
    /// Get magic words for spell (Pascal SPELLSU.PAS recreation)
    /// </summary>
    public static string GetMagicWords(CharacterClass characterClass, int spellLevel)
    {
        var spellInfo = GetSpellInfo(characterClass, spellLevel);
        return spellInfo?.MagicWords ?? "Abracadabra";
    }
    
    /// <summary>
    /// Check if character has any spells available
    /// </summary>
    public static bool HasSpells(Character character)
    {
        return character.Class == CharacterClass.Cleric ||
               character.Class == CharacterClass.Magician ||
               character.Class == CharacterClass.Sage;
    }
    
    /// <summary>
    /// Get highest spell level character can cast
    /// </summary>
    public static int GetMaxSpellLevel(Character character)
    {
        if (!HasSpells(character))
            return 0;
            
        for (int level = GameConfig.MaxSpellLevel; level >= 1; level--)
        {
            if (character.Level >= GetLevelRequired(character.Class, level))
            {
                return level;
            }
        }
        
        return 0;
    }
} 
