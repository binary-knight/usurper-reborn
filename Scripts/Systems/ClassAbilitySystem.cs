using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UsurperRemake.Utils;
using UsurperRemake.Systems;

/// <summary>
/// Class Ability System - Manages combat abilities for all classes
/// Spell classes get spells, Non-spell classes get unique combat abilities
/// All classes can learn abilities appropriate to their archetype
/// </summary>
public static class ClassAbilitySystem
{
    /// <summary>
    /// Represents a combat ability that can be used in battle
    /// </summary>
    public class ClassAbility
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public int LevelRequired { get; set; }
        public int StaminaCost { get; set; }
        public int Cooldown { get; set; } // Combat rounds before can use again
        public AbilityType Type { get; set; }
        public CharacterClass[] AvailableToClasses { get; set; } = Array.Empty<CharacterClass>();

        // Effect values
        public int BaseDamage { get; set; }
        public int BaseHealing { get; set; }
        public int DefenseBonus { get; set; }
        public int AttackBonus { get; set; }
        public int Duration { get; set; } // Combat rounds
        public string SpecialEffect { get; set; } = "";
    }

    public enum AbilityType
    {
        Attack,      // Direct damage
        Defense,     // Defensive stance/buff
        Utility,     // Escape, steal, etc.
        Buff,        // Self or ally buff
        Debuff,      // Enemy debuff
        Heal         // Self-healing
    }

    /// <summary>
    /// All available class abilities - expanded to ~10 per class spread across levels 1-100
    /// Themed around game lore: Maelketh (War), Old Gods, and class fantasy
    /// </summary>
    private static readonly Dictionary<string, ClassAbility> AllAbilities = new()
    {
        // ═══════════════════════════════════════════════════════════════════════════════
        // WARRIOR ABILITIES - Masters of martial combat, disciples of Maelketh
        // BALANCE: Base damage values kept moderate since stats now contribute significantly.
        // With 3% per STR above 10 + 1% per DEX above 10, high-stat characters will do
        // much more damage. 20 STR/15 DEX = 1.35x, 30 STR/20 DEX = 1.70x multiplier from stats alone.
        // Early abilities (level 1-20): Base 35-55 → scales strongly with stats
        // Mid abilities (level 30-60): Base 70-100 → scales to strong damage
        // Late abilities (level 70+): Base 140-280 → scales to devastating damage
        // ═══════════════════════════════════════════════════════════════════════════════
        ["power_strike"] = new ClassAbility
        {
            Id = "power_strike",
            Name = "Power Strike",
            Description = "A devastating two-handed blow that deals massive damage.",
            LevelRequired = 1,
            StaminaCost = 15,
            Cooldown = 0,
            Type = AbilityType.Attack,
            BaseDamage = 35,  // Moderate base, scales well with STR
            AvailableToClasses = new[] { CharacterClass.Warrior, CharacterClass.Barbarian, CharacterClass.Paladin }
        },
        ["shield_wall"] = new ClassAbility
        {
            Id = "shield_wall",
            Name = "Shield Wall",
            Description = "Raise your shield high, greatly increasing defense.",
            LevelRequired = 8,
            StaminaCost = 25,
            Cooldown = 3,
            Type = AbilityType.Defense,
            DefenseBonus = 30,
            Duration = 3,
            AvailableToClasses = new[] { CharacterClass.Warrior, CharacterClass.Paladin }
        },
        ["battle_cry"] = new ClassAbility
        {
            Id = "battle_cry",
            Name = "Battle Cry",
            Description = "A thunderous war cry that boosts your attack power.",
            LevelRequired = 16,
            StaminaCost = 30,
            Cooldown = 4,
            Type = AbilityType.Buff,
            AttackBonus = 40,
            Duration = 4,
            AvailableToClasses = new[] { CharacterClass.Warrior, CharacterClass.Barbarian }
        },
        ["execute"] = new ClassAbility
        {
            Id = "execute",
            Name = "Execute",
            Description = "A finishing blow that deals extra damage to wounded enemies.",
            LevelRequired = 28,
            StaminaCost = 40,
            Cooldown = 2,
            Type = AbilityType.Attack,
            BaseDamage = 80,  // Moderate base, scales with STR
            SpecialEffect = "execute",
            AvailableToClasses = new[] { CharacterClass.Warrior, CharacterClass.Barbarian, CharacterClass.Assassin }
        },
        ["last_stand"] = new ClassAbility
        {
            Id = "last_stand",
            Name = "Last Stand",
            Description = "When near death, channel your remaining strength into a counterattack.",
            LevelRequired = 40,
            StaminaCost = 50,
            Cooldown = 5,
            Type = AbilityType.Attack,
            BaseDamage = 120,  // Strong base for emergency use
            SpecialEffect = "last_stand",
            AvailableToClasses = new[] { CharacterClass.Warrior, CharacterClass.Barbarian, CharacterClass.Paladin }
        },
        ["whirlwind"] = new ClassAbility
        {
            Id = "whirlwind",
            Name = "Whirlwind",
            Description = "Spin with weapon extended, striking all nearby enemies.",
            LevelRequired = 55,
            StaminaCost = 60,
            Cooldown = 3,
            Type = AbilityType.Attack,
            BaseDamage = 90,  // AoE base, still scales well
            SpecialEffect = "aoe",
            AvailableToClasses = new[] { CharacterClass.Warrior, CharacterClass.Barbarian }
        },
        ["maelketh_fury"] = new ClassAbility
        {
            Id = "maelketh_fury",
            Name = "Maelketh's Fury",
            Description = "Channel the War God's rage for devastating strikes.",
            LevelRequired = 70,
            StaminaCost = 70,
            Cooldown = 5,
            Type = AbilityType.Attack,
            BaseDamage = 160,  // High tier single target
            AttackBonus = 40,
            Duration = 3,
            SpecialEffect = "fury",
            AvailableToClasses = new[] { CharacterClass.Warrior }
        },
        ["iron_fortress"] = new ClassAbility
        {
            Id = "iron_fortress",
            Name = "Iron Fortress",
            Description = "Become an immovable bastion of steel.",
            LevelRequired = 85,
            StaminaCost = 80,
            Cooldown = 6,
            Type = AbilityType.Defense,
            DefenseBonus = 80,  // Scales with CON now
            Duration = 4,
            AvailableToClasses = new[] { CharacterClass.Warrior }
        },
        ["champion_strike"] = new ClassAbility
        {
            Id = "champion_strike",
            Name = "Champion's Strike",
            Description = "The ultimate warrior technique, perfected through countless battles.",
            LevelRequired = 100,
            StaminaCost = 100,
            Cooldown = 6,
            Type = AbilityType.Attack,
            BaseDamage = 280,  // Capstone - scales massively with high STR
            SpecialEffect = "champion",
            AvailableToClasses = new[] { CharacterClass.Warrior }
        },

        // ═══════════════════════════════════════════════════════════════════════════════
        // BARBARIAN ABILITIES - Primal fury, berserker rage
        // BALANCE: Barbarians trade defense for offense. Stats scale well with abilities,
        // making a high-STR barbarian devastating. Rage provides attack bonus but costs defense.
        // ═══════════════════════════════════════════════════════════════════════════════
        ["rage"] = new ClassAbility
        {
            Id = "rage",
            Name = "Berserker Rage",
            Description = "Enter a blood rage, greatly increasing damage but lowering defense.",
            LevelRequired = 5,
            StaminaCost = 35,
            Cooldown = 5,
            Type = AbilityType.Buff,
            AttackBonus = 60,  // Scales with CHA for buff type
            DefenseBonus = -25,  // Risk for reward
            Duration = 5,
            SpecialEffect = "rage",
            AvailableToClasses = new[] { CharacterClass.Barbarian }
        },
        ["reckless_attack"] = new ClassAbility
        {
            Id = "reckless_attack",
            Name = "Reckless Attack",
            Description = "Throw caution to the wind for a devastating but risky attack.",
            LevelRequired = 12,
            StaminaCost = 25,
            Cooldown = 1,
            Type = AbilityType.Attack,
            BaseDamage = 55,  // Low cooldown bread and butter, scales with STR
            SpecialEffect = "reckless",
            AvailableToClasses = new[] { CharacterClass.Barbarian }
        },
        ["intimidate"] = new ClassAbility
        {
            Id = "intimidate",
            Name = "Intimidating Roar",
            Description = "A terrifying roar that weakens enemies.",
            LevelRequired = 24,
            StaminaCost = 30,
            Cooldown = 4,
            Type = AbilityType.Debuff,
            SpecialEffect = "fear",
            Duration = 3,
            AvailableToClasses = new[] { CharacterClass.Barbarian }
        },
        ["bloodlust"] = new ClassAbility
        {
            Id = "bloodlust",
            Name = "Bloodlust",
            Description = "Each kill fuels your fury, healing you and increasing damage.",
            LevelRequired = 36,
            StaminaCost = 40,
            Cooldown = 6,
            Type = AbilityType.Buff,
            BaseHealing = 25,  // Scales with CON/WIS
            AttackBonus = 20,
            Duration = 5,  // Fixed from 999 (infinite) - now 5 rounds
            SpecialEffect = "bloodlust",
            AvailableToClasses = new[] { CharacterClass.Barbarian }
        },
        ["frenzy"] = new ClassAbility
        {
            Id = "frenzy",
            Name = "Frenzy",
            Description = "Attack in a wild frenzy, striking multiple times.",
            LevelRequired = 48,
            StaminaCost = 55,
            Cooldown = 4,
            Type = AbilityType.Attack,
            BaseDamage = 65,  // Multi-hit base damage per hit, scales well
            SpecialEffect = "multi_hit",
            AvailableToClasses = new[] { CharacterClass.Barbarian }
        },
        ["primal_scream"] = new ClassAbility
        {
            Id = "primal_scream",
            Name = "Primal Scream",
            Description = "A scream from the depths of your soul that damages all enemies.",
            LevelRequired = 60,
            StaminaCost = 65,
            Cooldown = 5,
            Type = AbilityType.Attack,
            BaseDamage = 100,  // AoE base, scales with STR
            SpecialEffect = "aoe",
            AvailableToClasses = new[] { CharacterClass.Barbarian }
        },
        ["unstoppable"] = new ClassAbility
        {
            Id = "unstoppable",
            Name = "Unstoppable",
            Description = "Nothing can stop your rampage. Immune to status effects.",
            LevelRequired = 75,
            StaminaCost = 70,
            Cooldown = 6,
            Type = AbilityType.Buff,
            DefenseBonus = 40,  // Scales with CHA
            Duration = 4,
            SpecialEffect = "immunity",
            AvailableToClasses = new[] { CharacterClass.Barbarian }
        },
        ["avatar_of_destruction"] = new ClassAbility
        {
            Id = "avatar_of_destruction",
            Name = "Avatar of Destruction",
            Description = "Become a living embodiment of primal destruction.",
            LevelRequired = 90,
            StaminaCost = 90,
            Cooldown = 7,
            Type = AbilityType.Buff,
            AttackBonus = 80,  // Capstone buff, scales with CHA
            BaseDamage = 200,  // Instant damage on activation, scales with STR
            Duration = 5,
            SpecialEffect = "avatar",
            AvailableToClasses = new[] { CharacterClass.Barbarian }
        },

        // ═══════════════════════════════════════════════════════════════════════════════
        // PALADIN ABILITIES - Holy warriors, servants of light
        // BALANCE: Paladins are hybrid damage/support. Stats scale abilities, making
        // high-STR paladins deal good damage while high-WIS/CON paladins heal better.
        // ═══════════════════════════════════════════════════════════════════════════════
        ["lay_on_hands"] = new ClassAbility
        {
            Id = "lay_on_hands",
            Name = "Lay on Hands",
            Description = "Channel divine power to heal your wounds.",
            LevelRequired = 1,
            StaminaCost = 25,
            Cooldown = 4,
            Type = AbilityType.Heal,
            BaseHealing = 35,  // Scales with CON/WIS
            AvailableToClasses = new[] { CharacterClass.Paladin }
        },
        ["divine_smite"] = new ClassAbility
        {
            Id = "divine_smite",
            Name = "Divine Smite",
            Description = "Channel holy energy through your weapon. Extra vs undead.",
            LevelRequired = 10,
            StaminaCost = 35,
            Cooldown = 2,
            Type = AbilityType.Attack,
            BaseDamage = 55,  // Bread and butter, scales with STR
            SpecialEffect = "holy",
            AvailableToClasses = new[] { CharacterClass.Paladin }
        },
        ["aura_of_protection"] = new ClassAbility
        {
            Id = "aura_of_protection",
            Name = "Aura of Protection",
            Description = "Project a protective aura that increases defense.",
            LevelRequired = 20,
            StaminaCost = 40,
            Cooldown = 5,
            Type = AbilityType.Defense,
            DefenseBonus = 40,  // Scales with CON
            Duration = 4,
            AvailableToClasses = new[] { CharacterClass.Paladin }
        },
        ["holy_avenger"] = new ClassAbility
        {
            Id = "holy_avenger",
            Name = "Holy Avenger",
            Description = "Call upon divine wrath to smite evil.",
            LevelRequired = 32,
            StaminaCost = 55,
            Cooldown = 5,
            Type = AbilityType.Attack,
            BaseDamage = 95,  // Mid-tier attack, scales with STR
            SpecialEffect = "holy_avenger",
            AvailableToClasses = new[] { CharacterClass.Paladin }
        },
        ["cleansing_light"] = new ClassAbility
        {
            Id = "cleansing_light",
            Name = "Cleansing Light",
            Description = "Purge corruption and heal wounds with holy light.",
            LevelRequired = 44,
            StaminaCost = 50,
            Cooldown = 5,
            Type = AbilityType.Heal,
            BaseHealing = 80,  // Mid-tier heal, scales with CON/WIS
            SpecialEffect = "cleanse",
            AvailableToClasses = new[] { CharacterClass.Paladin }
        },
        ["divine_shield"] = new ClassAbility
        {
            Id = "divine_shield",
            Name = "Divine Shield",
            Description = "Become invulnerable for a short time.",
            LevelRequired = 56,
            StaminaCost = 60,
            Cooldown = 7,
            Type = AbilityType.Defense,
            DefenseBonus = 150,  // Near-invulnerable, scales with CON
            Duration = 2,
            SpecialEffect = "invulnerable",
            AvailableToClasses = new[] { CharacterClass.Paladin }
        },
        ["aurelion_blessing"] = new ClassAbility
        {
            Id = "aurelion_blessing",
            Name = "Aurelion's Blessing",
            Description = "The Sun God's light empowers and heals you.",
            LevelRequired = 68,
            StaminaCost = 70,
            Cooldown = 6,
            Type = AbilityType.Buff,
            BaseHealing = 120,  // Heal component, scales with CON/WIS
            AttackBonus = 40,  // Scales with CHA for buff
            DefenseBonus = 40,
            Duration = 4,
            AvailableToClasses = new[] { CharacterClass.Paladin }
        },
        ["judgment_day"] = new ClassAbility
        {
            Id = "judgment_day",
            Name = "Judgment Day",
            Description = "Call down divine judgment on all enemies.",
            LevelRequired = 80,
            StaminaCost = 80,
            Cooldown = 6,
            Type = AbilityType.Attack,
            BaseDamage = 140,  // High level AoE, scales with STR
            SpecialEffect = "aoe_holy",
            AvailableToClasses = new[] { CharacterClass.Paladin }
        },
        ["avatar_of_light"] = new ClassAbility
        {
            Id = "avatar_of_light",
            Name = "Avatar of Light",
            Description = "Become a vessel of pure divine energy.",
            LevelRequired = 95,
            StaminaCost = 100,
            Cooldown = 7,
            Type = AbilityType.Buff,
            AttackBonus = 70,  // Capstone buff, scales with CHA
            DefenseBonus = 70,
            BaseHealing = 160,  // Heal component, scales with CON/WIS
            Duration = 5,
            SpecialEffect = "avatar_light",
            AvailableToClasses = new[] { CharacterClass.Paladin }
        },

        // ═══════════════════════════════════════════════════════════════════════════════
        // ASSASSIN ABILITIES - Shadow killers, servants of Noctura
        // BALANCE: Assassins have high single-target burst. DEX adds to attack scaling
        // via the formula, so Assassins benefit from both STR and DEX for damage.
        // ═══════════════════════════════════════════════════════════════════════════════
        ["backstab"] = new ClassAbility
        {
            Id = "backstab",
            Name = "Backstab",
            Description = "Strike from the shadows for critical damage.",
            LevelRequired = 1,
            StaminaCost = 20,
            Cooldown = 1,
            Type = AbilityType.Attack,
            BaseDamage = 40,  // Low cooldown, scales with STR+DEX
            SpecialEffect = "critical",
            AvailableToClasses = new[] { CharacterClass.Assassin }
        },
        ["poison_blade"] = new ClassAbility
        {
            Id = "poison_blade",
            Name = "Poison Blade",
            Description = "Coat your weapon with deadly poison.",
            LevelRequired = 10,
            StaminaCost = 30,
            Cooldown = 3,
            Type = AbilityType.Attack,
            BaseDamage = 30,  // Initial hit + poison DoT
            SpecialEffect = "poison",
            Duration = 5,
            AvailableToClasses = new[] { CharacterClass.Assassin }
        },
        ["shadow_step"] = new ClassAbility
        {
            Id = "shadow_step",
            Name = "Shadow Step",
            Description = "Disappear into shadows, becoming nearly impossible to hit.",
            LevelRequired = 18,
            StaminaCost = 35,
            Cooldown = 4,
            Type = AbilityType.Defense,
            DefenseBonus = 50,  // Scales with CON
            Duration = 2,
            SpecialEffect = "evasion",
            AvailableToClasses = new[] { CharacterClass.Assassin }
        },
        ["death_mark"] = new ClassAbility
        {
            Id = "death_mark",
            Name = "Death Mark",
            Description = "Mark a target for death, increasing damage dealt.",
            LevelRequired = 28,
            StaminaCost = 45,
            Cooldown = 5,
            Type = AbilityType.Debuff,
            AttackBonus = 40,  // Scales with INT for debuff
            Duration = 4,
            SpecialEffect = "marked",
            AvailableToClasses = new[] { CharacterClass.Assassin }
        },
        ["assassinate"] = new ClassAbility
        {
            Id = "assassinate",
            Name = "Assassinate",
            Description = "A lethal strike. Can instantly kill weakened enemies.",
            LevelRequired = 42,
            StaminaCost = 70,
            Cooldown = 6,
            Type = AbilityType.Attack,
            BaseDamage = 160,  // Signature move, scales with STR+DEX
            SpecialEffect = "instant_kill",
            AvailableToClasses = new[] { CharacterClass.Assassin }
        },
        ["vanish"] = new ClassAbility
        {
            Id = "vanish",
            Name = "Vanish",
            Description = "Completely disappear, resetting combat advantage.",
            LevelRequired = 52,
            StaminaCost = 50,
            Cooldown = 5,
            Type = AbilityType.Utility,
            DefenseBonus = 80,
            Duration = 1,
            SpecialEffect = "vanish",
            AvailableToClasses = new[] { CharacterClass.Assassin }
        },
        ["noctura_embrace"] = new ClassAbility
        {
            Id = "noctura_embrace",
            Name = "Noctura's Embrace",
            Description = "The Shadow Goddess cloaks you in darkness.",
            LevelRequired = 65,
            StaminaCost = 60,
            Cooldown = 6,
            Type = AbilityType.Buff,
            DefenseBonus = 60,  // Scales with CHA for buff
            AttackBonus = 50,
            Duration = 4,
            SpecialEffect = "shadow",
            AvailableToClasses = new[] { CharacterClass.Assassin }
        },
        ["blade_dance"] = new ClassAbility
        {
            Id = "blade_dance",
            Name = "Blade Dance",
            Description = "A flurry of deadly strikes hitting all enemies.",
            LevelRequired = 78,
            StaminaCost = 75,
            Cooldown = 5,
            Type = AbilityType.Attack,
            BaseDamage = 85,  // AoE, scales with STR+DEX
            SpecialEffect = "aoe",
            AvailableToClasses = new[] { CharacterClass.Assassin }
        },
        ["death_blossom"] = new ClassAbility
        {
            Id = "death_blossom",
            Name = "Death Blossom",
            Description = "The ultimate assassination technique. Lethal to all.",
            LevelRequired = 92,
            StaminaCost = 90,
            Cooldown = 7,
            Type = AbilityType.Attack,
            BaseDamage = 250,  // Capstone AoE - scales heavily with STR+DEX
            SpecialEffect = "execute_all",
            AvailableToClasses = new[] { CharacterClass.Assassin }
        },

        // ═══════════════════════════════════════════════════════════════════════════════
        // RANGER ABILITIES - Masters of bow and nature
        // BALANCE: Rangers have reliable ranged damage with utility. Their guaranteed
        // hit abilities trade raw damage for consistency.
        // ═══════════════════════════════════════════════════════════════════════════════
        ["precise_shot"] = new ClassAbility
        {
            Id = "precise_shot",
            Name = "Precise Shot",
            Description = "Take careful aim for a guaranteed hit.",
            LevelRequired = 1,
            StaminaCost = 15,
            Cooldown = 1,
            Type = AbilityType.Attack,
            BaseDamage = 30,  // Starter ability - scales with STR+DEX
            SpecialEffect = "guaranteed_hit",
            AvailableToClasses = new[] { CharacterClass.Ranger }
        },
        ["hunters_mark"] = new ClassAbility
        {
            Id = "hunters_mark",
            Name = "Hunter's Mark",
            Description = "Mark your prey, increasing damage dealt.",
            LevelRequired = 8,
            StaminaCost = 25,
            Cooldown = 4,
            Type = AbilityType.Debuff,
            AttackBonus = 30,
            Duration = 5,
            SpecialEffect = "marked",
            AvailableToClasses = new[] { CharacterClass.Ranger }
        },
        ["evasive_roll"] = new ClassAbility
        {
            Id = "evasive_roll",
            Name = "Evasive Roll",
            Description = "Roll away from danger, avoiding the next attack.",
            LevelRequired = 16,
            StaminaCost = 30,
            Cooldown = 3,
            Type = AbilityType.Defense,
            DefenseBonus = 100,
            Duration = 1,
            SpecialEffect = "dodge_next",
            AvailableToClasses = new[] { CharacterClass.Ranger, CharacterClass.Assassin }
        },
        ["natures_blessing"] = new ClassAbility
        {
            Id = "natures_blessing",
            Name = "Nature's Blessing",
            Description = "Call upon nature spirits to heal your wounds.",
            LevelRequired = 24,
            StaminaCost = 40,
            Cooldown = 5,
            Type = AbilityType.Heal,
            BaseHealing = 55,  // Healing - scales with CON+WIS
            AvailableToClasses = new[] { CharacterClass.Ranger }
        },
        ["volley"] = new ClassAbility
        {
            Id = "volley",
            Name = "Volley",
            Description = "Fire multiple arrows at all enemies.",
            LevelRequired = 36,
            StaminaCost = 50,
            Cooldown = 4,
            Type = AbilityType.Attack,
            BaseDamage = 45,  // Mid-tier AoE - scales with STR+DEX
            SpecialEffect = "aoe",
            AvailableToClasses = new[] { CharacterClass.Ranger }
        },
        ["camouflage"] = new ClassAbility
        {
            Id = "camouflage",
            Name = "Camouflage",
            Description = "Blend with surroundings, greatly increasing evasion.",
            LevelRequired = 48,
            StaminaCost = 45,
            Cooldown = 5,
            Type = AbilityType.Defense,
            DefenseBonus = 70,
            Duration = 3,
            SpecialEffect = "stealth",
            AvailableToClasses = new[] { CharacterClass.Ranger }
        },
        ["terravok_call"] = new ClassAbility
        {
            Id = "terravok_call",
            Name = "Terravok's Call",
            Description = "The Earth God empowers your connection to nature.",
            LevelRequired = 60,
            StaminaCost = 60,
            Cooldown = 6,
            Type = AbilityType.Buff,
            AttackBonus = 40,  // Buff - scales with CHA
            BaseHealing = 70,  // Healing - scales with CON+WIS
            Duration = 4,
            AvailableToClasses = new[] { CharacterClass.Ranger }
        },
        ["arrow_storm"] = new ClassAbility
        {
            Id = "arrow_storm",
            Name = "Arrow Storm",
            Description = "Rain arrows upon all enemies with devastating effect.",
            LevelRequired = 75,
            StaminaCost = 70,
            Cooldown = 5,
            Type = AbilityType.Attack,
            BaseDamage = 100,  // High level AoE - scales with STR+DEX
            SpecialEffect = "aoe",
            AvailableToClasses = new[] { CharacterClass.Ranger }
        },
        ["legendary_shot"] = new ClassAbility
        {
            Id = "legendary_shot",
            Name = "Legendary Shot",
            Description = "A shot that will be remembered in songs forever.",
            LevelRequired = 88,
            StaminaCost = 80,
            Cooldown = 6,
            Type = AbilityType.Attack,
            BaseDamage = 200,  // Capstone single-target - scales with STR+DEX
            SpecialEffect = "legendary",
            AvailableToClasses = new[] { CharacterClass.Ranger }
        },

        // ═══════════════════════════════════════════════════════════════════════════════
        // JESTER/BARD ABILITIES - Tricksters and performers
        // BALANCE: Support-oriented classes with moderate damage but strong buffs/debuffs.
        // ═══════════════════════════════════════════════════════════════════════════════
        ["mock"] = new ClassAbility
        {
            Id = "mock",
            Name = "Vicious Mockery",
            Description = "Scathing insults that distract and damage.",
            LevelRequired = 1,
            StaminaCost = 10,
            Cooldown = 1,
            Type = AbilityType.Attack,
            BaseDamage = 22,  // Low cost spammable - scales with STR+DEX
            SpecialEffect = "distract",
            AvailableToClasses = new[] { CharacterClass.Jester, CharacterClass.Bard }
        },
        ["inspiring_tune"] = new ClassAbility
        {
            Id = "inspiring_tune",
            Name = "Inspiring Tune",
            Description = "Play an inspiring melody that boosts stats.",
            LevelRequired = 10,
            StaminaCost = 30,
            Cooldown = 4,
            Type = AbilityType.Buff,
            AttackBonus = 20,  // Buff - scales with CHA
            DefenseBonus = 20,  // Defense - scales with CON
            Duration = 4,
            AvailableToClasses = new[] { CharacterClass.Bard }
        },
        ["song_of_rest"] = new ClassAbility
        {
            Id = "song_of_rest",
            Name = "Song of Rest",
            Description = "A soothing melody that heals wounds.",
            LevelRequired = 18,
            StaminaCost = 35,
            Cooldown = 5,
            Type = AbilityType.Heal,
            BaseHealing = 45,  // Healing - scales with CON+WIS
            AvailableToClasses = new[] { CharacterClass.Bard }
        },
        ["charm"] = new ClassAbility
        {
            Id = "charm",
            Name = "Charming Performance",
            Description = "Use charisma to confuse your enemy.",
            LevelRequired = 26,
            StaminaCost = 35,
            Cooldown = 4,
            Type = AbilityType.Debuff,
            SpecialEffect = "charm",
            Duration = 3,
            AvailableToClasses = new[] { CharacterClass.Jester, CharacterClass.Bard }
        },
        ["disappearing_act"] = new ClassAbility
        {
            Id = "disappearing_act",
            Name = "Disappearing Act",
            Description = "Perform a magical trick to escape combat.",
            LevelRequired = 34,
            StaminaCost = 40,
            Cooldown = 6,
            Type = AbilityType.Utility,
            SpecialEffect = "escape",
            AvailableToClasses = new[] { CharacterClass.Jester }
        },
        ["deadly_joke"] = new ClassAbility
        {
            Id = "deadly_joke",
            Name = "Deadly Joke",
            Description = "A joke so bad it causes physical pain.",
            LevelRequired = 46,
            StaminaCost = 45,
            Cooldown = 3,
            Type = AbilityType.Attack,
            BaseDamage = 65,  // Mid-tier damage + confusion - scales with STR+DEX
            SpecialEffect = "confusion",
            AvailableToClasses = new[] { CharacterClass.Jester }
        },
        ["veloura_serenade"] = new ClassAbility
        {
            Id = "veloura_serenade",
            Name = "Veloura's Serenade",
            Description = "Channel the lost Goddess of Love through song.",
            LevelRequired = 58,
            StaminaCost = 55,
            Cooldown = 6,
            Type = AbilityType.Buff,
            BaseHealing = 75,  // Healing - scales with CON+WIS
            AttackBonus = 30,  // Buff - scales with CHA
            DefenseBonus = 30,  // Defense - scales with CON
            Duration = 4,
            AvailableToClasses = new[] { CharacterClass.Bard }
        },
        ["grand_finale"] = new ClassAbility
        {
            Id = "grand_finale",
            Name = "Grand Finale",
            Description = "The ultimate performance that devastates all foes.",
            LevelRequired = 72,
            StaminaCost = 70,
            Cooldown = 6,
            Type = AbilityType.Attack,
            BaseDamage = 110,  // High level AoE - scales with STR+DEX
            SpecialEffect = "aoe",
            AvailableToClasses = new[] { CharacterClass.Bard, CharacterClass.Jester }
        },
        ["legend_incarnate"] = new ClassAbility
        {
            Id = "legend_incarnate",
            Name = "Legend Incarnate",
            Description = "Become the legend you've always sung about.",
            LevelRequired = 85,
            StaminaCost = 80,
            Cooldown = 7,
            Type = AbilityType.Buff,
            AttackBonus = 55,  // Capstone buff - scales with CHA
            DefenseBonus = 55,  // Defense - scales with CON
            Duration = 5,
            SpecialEffect = "legend",
            AvailableToClasses = new[] { CharacterClass.Bard }
        },

        // ═══════════════════════════════════════════════════════════════════════════════
        // ALCHEMIST ABILITIES - Masters of potions and explosives
        // BALANCE: Alchemists have good damage through bombs and strong self-sustain
        // through potions. Their abilities represent prepared concoctions.
        // ═══════════════════════════════════════════════════════════════════════════════
        ["throw_bomb"] = new ClassAbility
        {
            Id = "throw_bomb",
            Name = "Throw Bomb",
            Description = "Hurl an explosive concoction at enemies.",
            LevelRequired = 1,
            StaminaCost = 20,
            Cooldown = 2,
            Type = AbilityType.Attack,
            BaseDamage = 35,  // Starter attack - scales with STR+DEX
            SpecialEffect = "fire",
            AvailableToClasses = new[] { CharacterClass.Alchemist }
        },
        ["healing_elixir"] = new ClassAbility
        {
            Id = "healing_elixir",
            Name = "Healing Elixir",
            Description = "Drink a prepared healing potion.",
            LevelRequired = 8,
            StaminaCost = 20,
            Cooldown = 3,
            Type = AbilityType.Heal,
            BaseHealing = 40,  // Healing - scales with CON+WIS
            AvailableToClasses = new[] { CharacterClass.Alchemist }
        },
        ["smoke_bomb"] = new ClassAbility
        {
            Id = "smoke_bomb",
            Name = "Smoke Bomb",
            Description = "Create smoke to confuse enemies.",
            LevelRequired = 16,
            StaminaCost = 30,
            Cooldown = 4,
            Type = AbilityType.Utility,
            DefenseBonus = 40,
            Duration = 2,
            SpecialEffect = "smoke",
            AvailableToClasses = new[] { CharacterClass.Alchemist }
        },
        ["acid_splash"] = new ClassAbility
        {
            Id = "acid_splash",
            Name = "Acid Splash",
            Description = "Throw acid that melts through armor.",
            LevelRequired = 24,
            StaminaCost = 35,
            Cooldown = 3,
            Type = AbilityType.Attack,
            BaseDamage = 50,  // Armor piercing - scales with STR+DEX
            SpecialEffect = "armor_pierce",
            AvailableToClasses = new[] { CharacterClass.Alchemist }
        },
        ["mutagen"] = new ClassAbility
        {
            Id = "mutagen",
            Name = "Mutagen",
            Description = "Drink a mutagen that enhances physical abilities.",
            LevelRequired = 36,
            StaminaCost = 50,
            Cooldown = 6,
            Type = AbilityType.Buff,
            AttackBonus = 30,  // Buff - scales with CHA
            DefenseBonus = 22,  // Defense - scales with CON
            Duration = 5,
            AvailableToClasses = new[] { CharacterClass.Alchemist }
        },
        ["frost_bomb"] = new ClassAbility
        {
            Id = "frost_bomb",
            Name = "Frost Bomb",
            Description = "A bomb that freezes enemies solid.",
            LevelRequired = 48,
            StaminaCost = 45,
            Cooldown = 4,
            Type = AbilityType.Attack,
            BaseDamage = 65,  // Damage + freeze effect - scales with STR+DEX
            SpecialEffect = "freeze",
            Duration = 2,
            AvailableToClasses = new[] { CharacterClass.Alchemist }
        },
        ["greater_elixir"] = new ClassAbility
        {
            Id = "greater_elixir",
            Name = "Greater Elixir",
            Description = "A masterwork healing potion.",
            LevelRequired = 60,
            StaminaCost = 45,
            Cooldown = 5,
            Type = AbilityType.Heal,
            BaseHealing = 100,  // Strong heal - scales with CON+WIS
            AvailableToClasses = new[] { CharacterClass.Alchemist }
        },
        ["philosophers_bomb"] = new ClassAbility
        {
            Id = "philosophers_bomb",
            Name = "Philosopher's Bomb",
            Description = "An alchemical masterpiece that devastates all.",
            LevelRequired = 74,
            StaminaCost = 70,
            Cooldown = 5,
            Type = AbilityType.Attack,
            BaseDamage = 120,  // High level AoE - scales with STR+DEX
            SpecialEffect = "aoe",
            AvailableToClasses = new[] { CharacterClass.Alchemist }
        },
        ["transmutation"] = new ClassAbility
        {
            Id = "transmutation",
            Name = "Transmutation",
            Description = "The ultimate alchemical transformation.",
            LevelRequired = 88,
            StaminaCost = 85,
            Cooldown = 7,
            Type = AbilityType.Buff,
            AttackBonus = 60,  // Buff - scales with CHA
            DefenseBonus = 50,  // Defense - scales with CON
            BaseHealing = 90,  // Healing - scales with CON+WIS
            Duration = 5,
            SpecialEffect = "transmute",
            AvailableToClasses = new[] { CharacterClass.Alchemist }
        },

        // ═══════════════════════════════════════════════════════════════════════════════
        // UNIVERSAL ABILITIES - Available to all non-spellcaster classes
        // BALANCE: Basic utility abilities available to all martial classes.
        // ═══════════════════════════════════════════════════════════════════════════════
        ["second_wind"] = new ClassAbility
        {
            Id = "second_wind",
            Name = "Second Wind",
            Description = "Catch your breath and recover health.",
            LevelRequired = 1,
            StaminaCost = 25,
            Cooldown = 5,
            Type = AbilityType.Heal,
            BaseHealing = 25,  // Emergency heal - scales with CON+WIS
            AvailableToClasses = new[] {
                CharacterClass.Warrior, CharacterClass.Barbarian, CharacterClass.Paladin,
                CharacterClass.Assassin, CharacterClass.Ranger, CharacterClass.Jester,
                CharacterClass.Bard, CharacterClass.Alchemist
            }
        },
        ["focus"] = new ClassAbility
        {
            Id = "focus",
            Name = "Focus",
            Description = "Concentrate to increase accuracy.",
            LevelRequired = 5,
            StaminaCost = 15,
            Cooldown = 2,
            Type = AbilityType.Buff,
            AttackBonus = 20,  // Buff - scales with CHA
            Duration = 1,
            AvailableToClasses = new[] {
                CharacterClass.Warrior, CharacterClass.Barbarian, CharacterClass.Paladin,
                CharacterClass.Assassin, CharacterClass.Ranger, CharacterClass.Jester,
                CharacterClass.Bard, CharacterClass.Alchemist
            }
        },
        ["rally"] = new ClassAbility
        {
            Id = "rally",
            Name = "Rally",
            Description = "Steel your resolve, recovering health and stamina.",
            LevelRequired = 30,
            StaminaCost = 40,
            Cooldown = 6,
            Type = AbilityType.Heal,
            BaseHealing = 55,  // Stronger heal - scales with CON+WIS
            AvailableToClasses = new[] {
                CharacterClass.Warrior, CharacterClass.Barbarian, CharacterClass.Paladin,
                CharacterClass.Assassin, CharacterClass.Ranger, CharacterClass.Jester,
                CharacterClass.Bard, CharacterClass.Alchemist
            }
        },
        ["desperate_strike"] = new ClassAbility
        {
            Id = "desperate_strike",
            Name = "Desperate Strike",
            Description = "A powerful attack when all seems lost.",
            LevelRequired = 50,
            StaminaCost = 55,
            Cooldown = 4,
            Type = AbilityType.Attack,
            BaseDamage = 90,  // Powerful strike - scales with STR+DEX
            SpecialEffect = "desperate",
            AvailableToClasses = new[] {
                CharacterClass.Warrior, CharacterClass.Barbarian, CharacterClass.Paladin,
                CharacterClass.Assassin, CharacterClass.Ranger, CharacterClass.Jester,
                CharacterClass.Bard, CharacterClass.Alchemist
            }
        },
        ["iron_will"] = new ClassAbility
        {
            Id = "iron_will",
            Name = "Iron Will",
            Description = "Your will becomes unbreakable. Resist all effects.",
            LevelRequired = 70,
            StaminaCost = 60,
            Cooldown = 6,
            Type = AbilityType.Buff,
            DefenseBonus = 50,  // Defense - scales with CON
            Duration = 3,
            SpecialEffect = "resist_all",
            AvailableToClasses = new[] {
                CharacterClass.Warrior, CharacterClass.Barbarian, CharacterClass.Paladin,
                CharacterClass.Assassin, CharacterClass.Ranger, CharacterClass.Jester,
                CharacterClass.Bard, CharacterClass.Alchemist
            }
        }
    };

    /// <summary>
    /// Get all abilities available to a specific class
    /// </summary>
    public static List<ClassAbility> GetClassAbilities(CharacterClass characterClass)
    {
        return AllAbilities.Values
            .Where(a => a.AvailableToClasses.Contains(characterClass))
            .OrderBy(a => a.LevelRequired)
            .ToList();
    }

    /// <summary>
    /// Get abilities that a character can currently use (meets level requirement)
    /// Also updates the character's LearnedAbilities set for tracking
    /// </summary>
    public static List<ClassAbility> GetAvailableAbilities(Character character)
    {
        if (character == null)
            return new List<ClassAbility>();

        var available = AllAbilities.Values
            .Where(a => a.AvailableToClasses.Contains(character.Class) &&
                       character.Level >= a.LevelRequired)
            .OrderBy(a => a.LevelRequired)
            .ToList();

        // Update LearnedAbilities tracking
        foreach (var ability in available)
        {
            if (!character.LearnedAbilities.Contains(ability.Id))
            {
                character.LearnedAbilities.Add(ability.Id);
            }
        }

        return available;
    }

    /// <summary>
    /// Get ability by ID
    /// </summary>
    public static ClassAbility? GetAbility(string abilityId)
    {
        return AllAbilities.TryGetValue(abilityId, out var ability) ? ability : null;
    }

    /// <summary>
    /// Check if character can use an ability (has stamina, not on cooldown)
    /// </summary>
    public static bool CanUseAbility(Character character, string abilityId, Dictionary<string, int> cooldowns)
    {
        if (character == null) return false;

        var ability = GetAbility(abilityId);
        if (ability == null) return false;

        // Check class
        if (!ability.AvailableToClasses.Contains(character.Class)) return false;

        // Check level
        if (character.Level < ability.LevelRequired) return false;

        // Check combat stamina (CurrentCombatStamina is used during combat)
        if (character.CurrentCombatStamina < ability.StaminaCost) return false;

        // Check cooldown
        if (cooldowns.TryGetValue(abilityId, out int remainingCooldown) && remainingCooldown > 0)
            return false;

        return true;
    }

    /// <summary>
    /// Use an ability and return the result
    /// </summary>
    public static ClassAbilityResult UseAbility(Character user, string abilityId, Random? random = null)
    {
        random ??= new Random();
        var ability = GetAbility(abilityId);
        var result = new ClassAbilityResult();

        if (ability == null)
        {
            result.Success = false;
            result.Message = "Unknown ability!";
            return result;
        }

        // Deduct combat stamina
        user.CurrentCombatStamina -= ability.StaminaCost;

        result.Success = true;
        result.AbilityUsed = ability;
        result.CooldownApplied = ability.Cooldown;

        // Calculate scaled effect values
        // BALANCE: Abilities should always be stronger than basic attacks to reward
        // using special moves. Scale is 3% per level and stats contribute significantly
        // to make stat investment feel impactful.
        double levelScale = 1.0 + (user.Level * 0.03); // 3% per level

        // Stat scaling based on ability type - stats are now major contributors
        // Similar to spell scaling where INT gives 4% per point above 10
        double statScale = 1.0;
        if (ability.Type == AbilityType.Attack)
        {
            // Strength contributes 3% per point above 10 (20 STR = 1.30x, 30 STR = 1.60x)
            statScale += Math.Max(0, (user.Strength - 10) * 0.03);
            // Dexterity adds a smaller bonus for accuracy/precision
            statScale += Math.Max(0, (user.Dexterity - 10) * 0.01);
        }
        else if (ability.Type == AbilityType.Heal)
        {
            // Constitution contributes 2.5% per point above 10 for healing
            statScale += Math.Max(0, (user.Constitution - 10) * 0.025);
            // Wisdom adds smaller healing bonus
            statScale += Math.Max(0, (user.Wisdom - 10) * 0.015);
        }
        else if (ability.Type == AbilityType.Defense)
        {
            // Constitution contributes 2% per point above 10 for defense
            statScale += Math.Max(0, (user.Constitution - 10) * 0.02);
        }
        else if (ability.Type == AbilityType.Buff)
        {
            // Charisma affects buff potency
            statScale += Math.Max(0, (user.Charisma - 10) * 0.02);
        }
        else if (ability.Type == AbilityType.Debuff)
        {
            // Intelligence affects debuff potency
            statScale += Math.Max(0, (user.Intelligence - 10) * 0.025);
        }

        double totalScale = levelScale * statScale;

        // Apply effects
        if (ability.BaseDamage > 0)
        {
            // BALANCE: Add weapon power contribution to ability damage
            // This ensures abilities scale with gear and always beat basic attacks
            double weaponBonus = user.WeapPow * 0.25; // 25% of weapon power added
            double scaledDamage = (ability.BaseDamage + weaponBonus) * totalScale;
            result.Damage = (int)(scaledDamage * (0.9 + random.NextDouble() * 0.2));
        }

        if (ability.BaseHealing > 0)
        {
            result.Healing = (int)(ability.BaseHealing * totalScale * (0.9 + random.NextDouble() * 0.2));
        }

        if (ability.AttackBonus > 0)
        {
            // Buff potency scales with stats for buff-type abilities
            double buffScale = ability.Type == AbilityType.Buff ? totalScale : levelScale;
            result.AttackBonus = (int)(ability.AttackBonus * buffScale);
        }

        if (ability.DefenseBonus != 0) // Can be negative for rage
        {
            // Defense bonuses scale with stats for defense-type abilities
            double defScale = ability.Type == AbilityType.Defense ? totalScale : levelScale;
            result.DefenseBonus = (int)(ability.DefenseBonus * defScale);
        }

        result.Duration = ability.Duration;
        result.SpecialEffect = ability.SpecialEffect;

        // Generate message
        result.Message = $"{user.Name2} uses {ability.Name}!";

        return result;
    }

    /// <summary>
    /// Check if a class is a spellcaster (uses SpellSystem instead)
    /// </summary>
    public static bool IsSpellcaster(CharacterClass characterClass)
    {
        return characterClass == CharacterClass.Cleric ||
               characterClass == CharacterClass.Magician ||
               characterClass == CharacterClass.Sage;
    }

    /// <summary>
    /// Display the ability equip/unequip quickbar menu at the Level Master
    /// </summary>
    public static async Task ShowAbilityLearningMenu(Character player, TerminalEmulator terminal)
    {
        if (IsSpellcaster(player.Class))
        {
            // Spellcasters go to spell learning instead
            await SpellLearningSystem.ShowSpellLearningMenu(player, terminal);
            return;
        }

        // Ensure LearnedAbilities is up to date
        GetAvailableAbilities(player);

        // Ensure quickbar is initialized
        if (player.Quickbar == null || player.Quickbar.Count < 9)
        {
            player.Quickbar = new List<string?>(new string?[9]);
        }

        while (true)
        {
            terminal.ClearScreen();
            terminal.WriteLine("═══ COMBAT ABILITIES ═══", "bright_yellow");
            terminal.WriteLine($"Class: {player.Class} | Level: {player.Level} | Stamina: {player.MaxCombatStamina}", "cyan");
            terminal.WriteLine("");

            // Show current quickbar
            terminal.WriteLine("  Your Quickbar:", "bright_white");
            for (int i = 0; i < 9; i++)
            {
                var slotId = player.Quickbar[i];
                if (!string.IsNullOrEmpty(slotId))
                {
                    var ability = GetAbility(slotId);
                    if (ability != null)
                    {
                        terminal.SetColor("bright_yellow");
                        terminal.Write($"  [{i + 1}] ");
                        terminal.SetColor("yellow");
                        terminal.WriteLine($"{ability.Name,-24} ({ability.StaminaCost} ST)");
                    }
                    else
                    {
                        // Invalid ability in slot - clear it
                        player.Quickbar[i] = null;
                        terminal.SetColor("darkgray");
                        terminal.WriteLine($"  [{i + 1}] --- empty ---");
                    }
                }
                else
                {
                    terminal.SetColor("darkgray");
                    terminal.WriteLine($"  [{i + 1}] --- empty ---");
                }
            }

            // Show available (unequipped) abilities
            var available = GetAvailableAbilities(player);
            var equippedIds = player.Quickbar.Where(s => s != null).ToHashSet();
            var unequipped = available.Where(a => !equippedIds.Contains(a.Id)).ToList();

            terminal.WriteLine("");
            if (unequipped.Count > 0)
            {
                terminal.WriteLine("  Available Abilities:", "bright_white");
                for (int i = 0; i < unequipped.Count; i++)
                {
                    char letter = (char)('a' + i);
                    terminal.SetColor("green");
                    terminal.WriteLine($"  [{letter}] {unequipped[i].Name,-24} ({unequipped[i].StaminaCost} ST) Lv{unequipped[i].LevelRequired}");
                }
            }

            // Show locked abilities (not yet available)
            var allClass = GetClassAbilities(player.Class);
            var locked = allClass.Where(a => player.Level < a.LevelRequired).ToList();
            if (locked.Count > 0)
            {
                terminal.WriteLine("");
                terminal.WriteLine("  Locked (level too low):", "darkgray");
                foreach (var ability in locked)
                {
                    terminal.SetColor("darkgray");
                    terminal.WriteLine($"      {ability.Name,-24} ({ability.StaminaCost} ST) Requires Lv{ability.LevelRequired}");
                }
            }

            terminal.WriteLine("");
            terminal.WriteLine("[1-9] Equip/change slot  [C] Clear a slot  [A] Auto-fill  [X] Exit", "yellow");
            var input = await terminal.GetInput("> ");
            if (string.IsNullOrWhiteSpace(input)) continue;

            string cmd = input.Trim().ToUpper();

            if (cmd == "X") break;

            if (cmd == "A")
            {
                // Auto-fill: populate quickbar with available abilities in level order
                GameEngine.AutoPopulateQuickbar(player);
                terminal.WriteLine("Quickbar auto-filled!", "bright_green");
                await SaveSystem.Instance.AutoSave(player);
                await Task.Delay(800);
                continue;
            }

            if (cmd == "C")
            {
                terminal.Write("Clear which slot? (1-9): ", "yellow");
                var clearInput = await terminal.GetInput("");
                if (int.TryParse(clearInput.Trim(), out int clearSlot) && clearSlot >= 1 && clearSlot <= 9)
                {
                    var clearedId = player.Quickbar[clearSlot - 1];
                    if (clearedId != null)
                    {
                        var clearedAbility = GetAbility(clearedId);
                        player.Quickbar[clearSlot - 1] = null;
                        terminal.WriteLine($"Removed {clearedAbility?.Name ?? clearedId} from slot {clearSlot}.", "cyan");
                        await SaveSystem.Instance.AutoSave(player);
                        await Task.Delay(800);
                    }
                }
                continue;
            }

            // Check for slot number (1-9)
            if (int.TryParse(cmd, out int slotNum) && slotNum >= 1 && slotNum <= 9)
            {
                if (unequipped.Count == 0)
                {
                    terminal.WriteLine("All abilities are already on your quickbar!", "yellow");
                    await Task.Delay(800);
                    continue;
                }

                var currentInSlot = player.Quickbar[slotNum - 1];
                if (currentInSlot != null)
                {
                    var currentAbility = GetAbility(currentInSlot);
                    terminal.WriteLine($"Slot {slotNum} has: {currentAbility?.Name ?? currentInSlot}. Pick a replacement:", "cyan");
                }
                else
                {
                    terminal.WriteLine($"Pick an ability for slot {slotNum}:", "cyan");
                }

                for (int i = 0; i < unequipped.Count; i++)
                {
                    char letter = (char)('a' + i);
                    terminal.SetColor("green");
                    terminal.WriteLine($"  [{letter}] {unequipped[i].Name,-24} ({unequipped[i].StaminaCost} ST)");
                }
                terminal.SetColor("gray");
                terminal.WriteLine("  [0] Cancel");

                var pick = await terminal.GetInput("> ");
                if (string.IsNullOrWhiteSpace(pick) || pick.Trim() == "0") continue;

                char pickChar = pick.Trim().ToLower()[0];
                int pickIndex = pickChar - 'a';
                if (pickIndex >= 0 && pickIndex < unequipped.Count)
                {
                    var chosen = unequipped[pickIndex];

                    // If this ability is already in another slot, clear that slot
                    for (int i = 0; i < 9; i++)
                    {
                        if (player.Quickbar[i] == chosen.Id)
                            player.Quickbar[i] = null;
                    }

                    player.Quickbar[slotNum - 1] = chosen.Id;
                    terminal.WriteLine($"Equipped {chosen.Name} to slot {slotNum}!", "bright_green");
                    await SaveSystem.Instance.AutoSave(player);
                    await Task.Delay(800);
                }
                continue;
            }
        }
    }
}

/// <summary>
/// Result of using a class ability (distinct from monster AbilityResult)
/// </summary>
public class ClassAbilityResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public ClassAbilitySystem.ClassAbility? AbilityUsed { get; set; }
    public int Damage { get; set; }
    public int Healing { get; set; }
    public int AttackBonus { get; set; }
    public int DefenseBonus { get; set; }
    public int Duration { get; set; }
    public string SpecialEffect { get; set; } = "";
    public int CooldownApplied { get; set; }
}
