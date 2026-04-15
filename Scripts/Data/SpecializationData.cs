using System.Collections.Generic;
using System.Linq;

namespace UsurperRemake.Data;

/// <summary>
/// Role categories for class specializations.
/// </summary>
public enum SpecRole
{
    DPS,
    Tank,
    Healer,
    Utility,
    Debuff
}

/// <summary>
/// Defines a class specialization's stat growth modifiers and combat AI behavior.
/// All stat bonuses are additive on top of base class growth per level-up.
/// </summary>
public class SpecDefinition
{
    public ClassSpecialization Spec { get; init; }
    public CharacterClass ForClass { get; init; }
    public string Name { get; init; } = "";
    public string DescriptionKey { get; init; } = "";
    public SpecRole Role { get; init; }

    // Stat growth modifiers (additive per level-up on top of base class growth)
    public int BonusStrength { get; init; }
    public int BonusConstitution { get; init; }
    public int BonusMaxHP { get; init; }
    public int BonusDefence { get; init; }
    public int BonusIntelligence { get; init; }
    public int BonusWisdom { get; init; }
    public int BonusCharisma { get; init; }
    public int BonusMaxMana { get; init; }
    public int BonusDexterity { get; init; }
    public int BonusAgility { get; init; }
    public int BonusStamina { get; init; }

    // Combat AI behavior
    public ClassAbilitySystem.AbilityType[] PreferredAbilityTypes { get; init; } = [];
    public ClassAbilitySystem.AbilityType[] RestrictedAbilityTypes { get; init; } = [];
    public string[] DisabledAbilityIds { get; init; } = [];
    public double HealThreshold { get; init; }      // 0.0-1.0, HP% below which NPC heals allies
    public double AbilityUseChance { get; init; } = 0.50;  // Override base 50% chance
    public bool PreferAoE { get; init; }
    public bool PreferSingleTarget { get; init; }
}

/// <summary>
/// Static data for all 24 NPC class specializations.
/// Data-driven: combat code reads spec definitions instead of per-spec switch statements.
/// </summary>
public static class SpecializationData
{
    private static readonly Dictionary<ClassSpecialization, SpecDefinition> Specs = new()
    {
        // ═══════════════════════════════════════════
        // WARRIOR: Arms (DPS) / Protection (Tank)
        // ═══════════════════════════════════════════
        [ClassSpecialization.Arms] = new SpecDefinition
        {
            Spec = ClassSpecialization.Arms,
            ForClass = CharacterClass.Warrior,
            Name = "Arms",
            DescriptionKey = "spec.warrior.arms.desc",
            Role = SpecRole.DPS,
            BonusStrength = 2,
            BonusDexterity = 1,
            BonusMaxHP = 3,
            HealThreshold = 0.40,
            AbilityUseChance = 0.65,
            PreferredAbilityTypes = [ClassAbilitySystem.AbilityType.Attack],
            PreferSingleTarget = true
        },
        [ClassSpecialization.Protection] = new SpecDefinition
        {
            Spec = ClassSpecialization.Protection,
            ForClass = CharacterClass.Warrior,
            Name = "Protection",
            DescriptionKey = "spec.warrior.protection.desc",
            Role = SpecRole.Tank,
            BonusConstitution = 2,
            BonusDefence = 2,
            BonusMaxHP = 8,
            HealThreshold = 0.50,
            AbilityUseChance = 0.70,
            PreferredAbilityTypes = [ClassAbilitySystem.AbilityType.Defense, ClassAbilitySystem.AbilityType.Buff],
            RestrictedAbilityTypes = [ClassAbilitySystem.AbilityType.Utility]
        },

        // ═══════════════════════════════════════════
        // PALADIN: Retribution (DPS) / Holy (Healer)
        // ═══════════════════════════════════════════
        [ClassSpecialization.Retribution] = new SpecDefinition
        {
            Spec = ClassSpecialization.Retribution,
            ForClass = CharacterClass.Paladin,
            Name = "Retribution",
            DescriptionKey = "spec.paladin.retribution.desc",
            Role = SpecRole.DPS,
            BonusStrength = 2,
            BonusMaxHP = 4,
            BonusWisdom = 1,
            HealThreshold = 0.40,
            AbilityUseChance = 0.60,
            PreferredAbilityTypes = [ClassAbilitySystem.AbilityType.Attack],
            PreferSingleTarget = true
        },
        [ClassSpecialization.Holy] = new SpecDefinition
        {
            Spec = ClassSpecialization.Holy,
            ForClass = CharacterClass.Paladin,
            Name = "Holy",
            DescriptionKey = "spec.paladin.holy.desc",
            Role = SpecRole.Healer,
            BonusWisdom = 2,
            BonusConstitution = 1,
            BonusMaxMana = 4,
            BonusMaxHP = 3,
            HealThreshold = 0.80,
            AbilityUseChance = 0.55,
            PreferredAbilityTypes = [ClassAbilitySystem.AbilityType.Heal, ClassAbilitySystem.AbilityType.Buff]
        },

        [ClassSpecialization.Guardian] = new SpecDefinition
        {
            Spec = ClassSpecialization.Guardian,
            ForClass = CharacterClass.Paladin,
            Name = "Guardian",
            DescriptionKey = "spec.paladin.guardian.desc",
            Role = SpecRole.Tank,
            BonusConstitution = 2,
            BonusDefence = 2,
            BonusMaxHP = 6,
            BonusWisdom = 1,
            HealThreshold = 0.50,
            AbilityUseChance = 0.70,
            PreferredAbilityTypes = [ClassAbilitySystem.AbilityType.Defense, ClassAbilitySystem.AbilityType.Buff],
            RestrictedAbilityTypes = [ClassAbilitySystem.AbilityType.Utility]
        },

        // ═══════════════════════════════════════════
        // RANGER: Marksmanship (DPS) / Survival (Utility)
        // ═══════════════════════════════════════════
        [ClassSpecialization.Marksmanship] = new SpecDefinition
        {
            Spec = ClassSpecialization.Marksmanship,
            ForClass = CharacterClass.Ranger,
            Name = "Marksmanship",
            DescriptionKey = "spec.ranger.marksmanship.desc",
            Role = SpecRole.DPS,
            BonusDexterity = 2,
            BonusAgility = 1,
            BonusStrength = 1,
            HealThreshold = 0.35,
            AbilityUseChance = 0.65,
            PreferredAbilityTypes = [ClassAbilitySystem.AbilityType.Attack],
            PreferSingleTarget = true
        },
        [ClassSpecialization.Survival] = new SpecDefinition
        {
            Spec = ClassSpecialization.Survival,
            ForClass = CharacterClass.Ranger,
            Name = "Survival",
            DescriptionKey = "spec.ranger.survival.desc",
            Role = SpecRole.Utility,
            BonusAgility = 2,
            BonusConstitution = 1,
            BonusStamina = 1,
            HealThreshold = 0.50,
            AbilityUseChance = 0.60,
            PreferredAbilityTypes = [ClassAbilitySystem.AbilityType.Debuff, ClassAbilitySystem.AbilityType.Utility]
        },

        // ═══════════════════════════════════════════
        // ASSASSIN: Subtlety (DPS) / Toxicology (Debuff)
        // ═══════════════════════════════════════════
        [ClassSpecialization.Subtlety] = new SpecDefinition
        {
            Spec = ClassSpecialization.Subtlety,
            ForClass = CharacterClass.Assassin,
            Name = "Subtlety",
            DescriptionKey = "spec.assassin.subtlety.desc",
            Role = SpecRole.DPS,
            BonusDexterity = 2,
            BonusAgility = 2,
            BonusStrength = 1,
            HealThreshold = 0.30,
            AbilityUseChance = 0.70,
            PreferredAbilityTypes = [ClassAbilitySystem.AbilityType.Attack],
            PreferSingleTarget = true
        },
        [ClassSpecialization.Toxicology] = new SpecDefinition
        {
            Spec = ClassSpecialization.Toxicology,
            ForClass = CharacterClass.Assassin,
            Name = "Toxicology",
            DescriptionKey = "spec.assassin.toxicology.desc",
            Role = SpecRole.Debuff,
            BonusDexterity = 1,
            BonusIntelligence = 2,
            BonusAgility = 1,
            HealThreshold = 0.35,
            AbilityUseChance = 0.65,
            PreferredAbilityTypes = [ClassAbilitySystem.AbilityType.Debuff, ClassAbilitySystem.AbilityType.Attack]
        },

        // ═══════════════════════════════════════════
        // BARBARIAN: Berserker (DPS) / Juggernaut (Tank)
        // ═══════════════════════════════════════════
        [ClassSpecialization.Berserker] = new SpecDefinition
        {
            Spec = ClassSpecialization.Berserker,
            ForClass = CharacterClass.Barbarian,
            Name = "Berserker",
            DescriptionKey = "spec.barbarian.berserker.desc",
            Role = SpecRole.DPS,
            BonusStrength = 3,
            BonusMaxHP = 4,
            BonusStamina = 1,
            HealThreshold = 0.30,
            AbilityUseChance = 0.70,
            PreferredAbilityTypes = [ClassAbilitySystem.AbilityType.Attack],
            PreferAoE = true
        },
        [ClassSpecialization.Juggernaut] = new SpecDefinition
        {
            Spec = ClassSpecialization.Juggernaut,
            ForClass = CharacterClass.Barbarian,
            Name = "Juggernaut",
            DescriptionKey = "spec.barbarian.juggernaut.desc",
            Role = SpecRole.Tank,
            BonusConstitution = 3,
            BonusDefence = 2,
            BonusMaxHP = 10,
            HealThreshold = 0.50,
            AbilityUseChance = 0.65,
            PreferredAbilityTypes = [ClassAbilitySystem.AbilityType.Defense, ClassAbilitySystem.AbilityType.Buff],
            RestrictedAbilityTypes = [ClassAbilitySystem.AbilityType.Utility]
        },

        // ═══════════════════════════════════════════
        // CLERIC: Smite (DPS) / Restoration (Healer)
        // ═══════════════════════════════════════════
        [ClassSpecialization.Smite] = new SpecDefinition
        {
            Spec = ClassSpecialization.Smite,
            ForClass = CharacterClass.Cleric,
            Name = "Smite",
            DescriptionKey = "spec.cleric.smite.desc",
            Role = SpecRole.DPS,
            BonusStrength = 1,
            BonusWisdom = 2,
            BonusMaxMana = 4,
            HealThreshold = 0.30,  // Emergency-only healer
            AbilityUseChance = 0.60,
            PreferredAbilityTypes = [ClassAbilitySystem.AbilityType.Attack],
            RestrictedAbilityTypes = [ClassAbilitySystem.AbilityType.Heal]
        },
        [ClassSpecialization.Restoration] = new SpecDefinition
        {
            Spec = ClassSpecialization.Restoration,
            ForClass = CharacterClass.Cleric,
            Name = "Restoration",
            DescriptionKey = "spec.cleric.restoration.desc",
            Role = SpecRole.Healer,
            BonusWisdom = 3,
            BonusConstitution = 1,
            BonusMaxMana = 6,
            HealThreshold = 0.80,  // Aggressive healer
            AbilityUseChance = 0.55,
            PreferredAbilityTypes = [ClassAbilitySystem.AbilityType.Heal, ClassAbilitySystem.AbilityType.Buff],
            RestrictedAbilityTypes = [ClassAbilitySystem.AbilityType.Attack]
        },

        // ═══════════════════════════════════════════
        // MAGICIAN: Destruction (DPS) / Arcane (Utility)
        // ═══════════════════════════════════════════
        [ClassSpecialization.Destruction] = new SpecDefinition
        {
            Spec = ClassSpecialization.Destruction,
            ForClass = CharacterClass.Magician,
            Name = "Destruction",
            DescriptionKey = "spec.magician.destruction.desc",
            Role = SpecRole.DPS,
            BonusIntelligence = 3,
            BonusMaxMana = 6,
            HealThreshold = 0.35,
            AbilityUseChance = 0.70,
            PreferredAbilityTypes = [ClassAbilitySystem.AbilityType.Attack],
            PreferAoE = true
        },
        [ClassSpecialization.Arcane] = new SpecDefinition
        {
            Spec = ClassSpecialization.Arcane,
            ForClass = CharacterClass.Magician,
            Name = "Arcane",
            DescriptionKey = "spec.magician.arcane.desc",
            Role = SpecRole.Utility,
            BonusIntelligence = 2,
            BonusWisdom = 2,
            BonusMaxMana = 4,
            HealThreshold = 0.50,
            AbilityUseChance = 0.60,
            PreferredAbilityTypes = [ClassAbilitySystem.AbilityType.Buff, ClassAbilitySystem.AbilityType.Debuff]
        },

        // ═══════════════════════════════════════════
        // SAGE: Elementalist (DPS) / Mystic (Healer)
        // ═══════════════════════════════════════════
        [ClassSpecialization.Elementalist] = new SpecDefinition
        {
            Spec = ClassSpecialization.Elementalist,
            ForClass = CharacterClass.Sage,
            Name = "Elementalist",
            DescriptionKey = "spec.sage.elementalist.desc",
            Role = SpecRole.DPS,
            BonusIntelligence = 3,
            BonusWisdom = 1,
            BonusMaxMana = 5,
            HealThreshold = 0.35,
            AbilityUseChance = 0.70,
            PreferredAbilityTypes = [ClassAbilitySystem.AbilityType.Attack],
            PreferAoE = true
        },
        [ClassSpecialization.Mystic] = new SpecDefinition
        {
            Spec = ClassSpecialization.Mystic,
            ForClass = CharacterClass.Sage,
            Name = "Mystic",
            DescriptionKey = "spec.sage.mystic.desc",
            Role = SpecRole.Healer,
            BonusWisdom = 3,
            BonusIntelligence = 1,
            BonusMaxMana = 5,
            BonusConstitution = 1,
            HealThreshold = 0.75,
            AbilityUseChance = 0.55,
            PreferredAbilityTypes = [ClassAbilitySystem.AbilityType.Heal, ClassAbilitySystem.AbilityType.Buff]
        },

        // ═══════════════════════════════════════════
        // BARD: Virtuoso (DPS) / Minstrel (Healer)
        // ═══════════════════════════════════════════
        [ClassSpecialization.Virtuoso] = new SpecDefinition
        {
            Spec = ClassSpecialization.Virtuoso,
            ForClass = CharacterClass.Bard,
            Name = "Virtuoso",
            DescriptionKey = "spec.bard.virtuoso.desc",
            Role = SpecRole.DPS,
            BonusCharisma = 2,
            BonusDexterity = 2,
            BonusStrength = 1,
            HealThreshold = 0.40,
            AbilityUseChance = 0.65,
            PreferredAbilityTypes = [ClassAbilitySystem.AbilityType.Attack, ClassAbilitySystem.AbilityType.Debuff],
            PreferSingleTarget = true
        },
        [ClassSpecialization.Minstrel] = new SpecDefinition
        {
            Spec = ClassSpecialization.Minstrel,
            ForClass = CharacterClass.Bard,
            Name = "Minstrel",
            DescriptionKey = "spec.bard.minstrel.desc",
            Role = SpecRole.Healer,
            BonusCharisma = 3,
            BonusWisdom = 1,
            BonusConstitution = 1,
            HealThreshold = 0.75,
            AbilityUseChance = 0.55,
            PreferredAbilityTypes = [ClassAbilitySystem.AbilityType.Heal, ClassAbilitySystem.AbilityType.Buff],
            RestrictedAbilityTypes = [ClassAbilitySystem.AbilityType.Attack]
        },

        // ═══════════════════════════════════════════
        // ALCHEMIST: Demolition (DPS) / Apothecary (Healer)
        // ═══════════════════════════════════════════
        [ClassSpecialization.Demolition] = new SpecDefinition
        {
            Spec = ClassSpecialization.Demolition,
            ForClass = CharacterClass.Alchemist,
            Name = "Demolition",
            DescriptionKey = "spec.alchemist.demolition.desc",
            Role = SpecRole.DPS,
            BonusIntelligence = 2,
            BonusDexterity = 1,
            BonusStrength = 1,
            HealThreshold = 0.35,
            AbilityUseChance = 0.70,
            PreferredAbilityTypes = [ClassAbilitySystem.AbilityType.Attack],
            PreferAoE = true
        },
        [ClassSpecialization.Apothecary] = new SpecDefinition
        {
            Spec = ClassSpecialization.Apothecary,
            ForClass = CharacterClass.Alchemist,
            Name = "Apothecary",
            DescriptionKey = "spec.alchemist.apothecary.desc",
            Role = SpecRole.Healer,
            BonusIntelligence = 2,
            BonusConstitution = 2,
            BonusWisdom = 1,
            HealThreshold = 0.75,
            AbilityUseChance = 0.55,
            PreferredAbilityTypes = [ClassAbilitySystem.AbilityType.Heal, ClassAbilitySystem.AbilityType.Buff]
        },

        // ═══════════════════════════════════════════
        // JESTER: Chaos (DPS) / Trickster (Debuff)
        // ═══════════════════════════════════════════
        [ClassSpecialization.Chaos] = new SpecDefinition
        {
            Spec = ClassSpecialization.Chaos,
            ForClass = CharacterClass.Jester,
            Name = "Chaos",
            DescriptionKey = "spec.jester.chaos.desc",
            Role = SpecRole.DPS,
            BonusCharisma = 3,
            BonusStrength = 1,
            BonusDexterity = 1,
            HealThreshold = 0.35,
            AbilityUseChance = 0.70,
            PreferredAbilityTypes = [ClassAbilitySystem.AbilityType.Attack],
            PreferAoE = true
        },
        [ClassSpecialization.Trickster] = new SpecDefinition
        {
            Spec = ClassSpecialization.Trickster,
            ForClass = CharacterClass.Jester,
            Name = "Trickster",
            DescriptionKey = "spec.jester.trickster.desc",
            Role = SpecRole.Debuff,
            BonusCharisma = 1,
            BonusAgility = 2,
            BonusDexterity = 2,
            BonusConstitution = 1,
            HealThreshold = 0.40,
            AbilityUseChance = 0.65,
            PreferredAbilityTypes = [ClassAbilitySystem.AbilityType.Debuff, ClassAbilitySystem.AbilityType.Utility]
        },

        // ═══════════════════════════════════════════
        // MYSTIC SHAMAN: Elemental (DPS) / Spiritwalker (Healer)
        // ═══════════════════════════════════════════
        [ClassSpecialization.Elemental] = new SpecDefinition
        {
            Spec = ClassSpecialization.Elemental,
            ForClass = CharacterClass.MysticShaman,
            Name = "Elemental",
            DescriptionKey = "spec.shaman.elemental.desc",
            Role = SpecRole.DPS,
            BonusIntelligence = 2,
            BonusStrength = 1,
            BonusMaxMana = 3,
            HealThreshold = 0.35,
            AbilityUseChance = 0.70,
            PreferredAbilityTypes = [ClassAbilitySystem.AbilityType.Attack],
            PreferAoE = true
        },
        [ClassSpecialization.Spiritwalker] = new SpecDefinition
        {
            Spec = ClassSpecialization.Spiritwalker,
            ForClass = CharacterClass.MysticShaman,
            Name = "Spiritwalker",
            DescriptionKey = "spec.shaman.spiritwalker.desc",
            Role = SpecRole.Healer,
            BonusWisdom = 2,
            BonusIntelligence = 1,
            BonusMaxMana = 4,
            BonusConstitution = 1,
            HealThreshold = 0.75,
            AbilityUseChance = 0.55,
            PreferredAbilityTypes = [ClassAbilitySystem.AbilityType.Heal, ClassAbilitySystem.AbilityType.Buff]
        }
    };

    /// <summary>
    /// Get the spec definition for a given specialization.
    /// Returns null for ClassSpecialization.None.
    /// </summary>
    public static SpecDefinition? GetSpec(ClassSpecialization spec)
    {
        if (spec == ClassSpecialization.None) return null;
        return Specs.TryGetValue(spec, out var def) ? def : null;
    }

    /// <summary>
    /// Get both available specs for a given base class.
    /// Returns empty list for prestige classes (no specs yet).
    /// </summary>
    public static List<SpecDefinition> GetSpecsForClass(CharacterClass charClass)
    {
        return Specs.Values.Where(s => s.ForClass == charClass).ToList();
    }

    /// <summary>
    /// Check if a specialization is valid for a given class.
    /// </summary>
    public static bool IsValidSpecForClass(ClassSpecialization spec, CharacterClass charClass)
    {
        if (spec == ClassSpecialization.None) return true;
        var def = GetSpec(spec);
        return def != null && def.ForClass == charClass;
    }

    /// <summary>
    /// Check if a spec makes this NPC count as a healer for combat AI purposes.
    /// </summary>
    public static bool IsHealerSpec(ClassSpecialization spec)
    {
        var def = GetSpec(spec);
        return def?.Role == SpecRole.Healer;
    }

    /// <summary>
    /// Check if a spec makes this NPC count as a tank for combat AI purposes.
    /// </summary>
    public static bool IsTankSpec(ClassSpecialization spec)
    {
        var def = GetSpec(spec);
        return def?.Role == SpecRole.Tank;
    }

    /// <summary>
    /// v0.57.0 — damage multiplier granted to spec tanks when a shield is equipped.
    /// Protection (Warrior): +20%. Guardian (Paladin): +15%. Juggernaut (Barbarian) gets 0% —
    /// Juggernauts are rage-based, no-shield by design.
    /// </summary>
    public static float GetShieldDamageBonus(ClassSpecialization spec)
    {
        return spec switch
        {
            ClassSpecialization.Protection => 0.20f,
            ClassSpecialization.Guardian => 0.15f,
            _ => 0f
        };
    }

    /// <summary>
    /// v0.57.0 — heal multiplier granted to spec tanks when a shield is equipped.
    /// Only Guardian Paladin gets this — they're the hybrid tank/support spec that should be
    /// meaningfully rewarded for sword-and-board play on the healing side too.
    /// </summary>
    public static float GetShieldHealBonus(ClassSpecialization spec)
    {
        return spec switch
        {
            ClassSpecialization.Guardian => 0.15f,
            _ => 0f
        };
    }
}
