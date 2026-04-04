using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Monster family system - defines monster types with tier progressions
/// Monsters scale from level 1-100 with distinct families and variations
///
/// LORE: The dungeons beneath Dorashire were not always so infested with monsters.
/// When the Sundering shattered the divine order, the corruption that seeped from
/// the broken gods took physical form. Each monster family reflects an aspect of
/// divine corruption - the Undead rose from Mortis's broken domain, the Demonic
/// crawled through tears in reality, and the Celestials fell when Aurelion's
/// light began to fade. The deeper one descends, the closer one gets to the
/// source of corruption - and the Old Gods themselves.
/// </summary>
public static class MonsterFamilies
{
    /// <summary>
    /// Monster family definition with tier progression
    /// </summary>
    public class MonsterFamily
    {
        public string FamilyName { get; set; } = "";
        public string Description { get; set; } = "";
        public string BaseColor { get; set; } = "white";
        public List<MonsterTier> Tiers { get; set; } = new();
        public string AttackType { get; set; } = "physical"; // physical, magic, poison, fire, etc.
    }

    /// <summary>
    /// Individual tier within a monster family
    /// </summary>
    public class MonsterTier
    {
        public string Name { get; set; } = "";
        public int MinLevel { get; set; }
        public int MaxLevel { get; set; }
        public string Color { get; set; } = "white";
        public float PowerMultiplier { get; set; } = 1.0f;
        public List<string> SpecialAbilities { get; set; } = new();
    }

    /// <summary>
    /// All monster families in the game. Checks GameDataLoader first for moddable overrides.
    /// </summary>
    public static List<MonsterFamily> AllFamilies =>
        UsurperRemake.Systems.GameDataLoader.MonsterFamilies ?? _builtInFamilies;

    /// <summary>
    /// Returns built-in hardcoded families, bypassing GameDataLoader.
    /// Used by ExportDefaults() so --export-data always exports the originals.
    /// </summary>
    public static List<MonsterFamily> GetBuiltInFamilies() => _builtInFamilies;

    /// <summary>
    /// Built-in hardcoded monster families (used when no GameData override exists)
    /// </summary>
    private static readonly List<MonsterFamily> _builtInFamilies = new()
    {
        // GOBLINOID FAMILY - Twisted by Maelketh's endless war
        new MonsterFamily
        {
            FamilyName = "Goblinoid",
            Description = "Twisted creatures born from Maelketh's corruption. Once peaceful cave-dwellers, the goblins were warped by the mad war-god's influence into cunning, violent warriors who live only for conquest.",
            BaseColor = "green",
            AttackType = "physical",
            Tiers = new List<MonsterTier>
            {
                new() { Name = "Goblin", MinLevel = 1, MaxLevel = 10, Color = "green", PowerMultiplier = 0.8f },
                new() { Name = "Hobgoblin", MinLevel = 11, MaxLevel = 25, Color = "yellow", PowerMultiplier = 1.0f },
                new() { Name = "Goblin Champion", MinLevel = 26, MaxLevel = 50, Color = "bright_yellow", PowerMultiplier = 1.3f, SpecialAbilities = new() { "CriticalStrike" } },
                new() { Name = "Goblin Warlord", MinLevel = 51, MaxLevel = 75, Color = "bright_red", PowerMultiplier = 1.6f, SpecialAbilities = new() { "CriticalStrike", "Rally" } },
                new() { Name = "Goblin King", MinLevel = 76, MaxLevel = 100, Color = "magenta", PowerMultiplier = 2.0f, SpecialAbilities = new() { "CriticalStrike", "Rally", "CommandArmy" } }
            }
        },

        // UNDEAD FAMILY - Death's servants when Mortis lost control
        new MonsterFamily
        {
            FamilyName = "Undead",
            Description = "When the god Mortis was weakened by the Sundering, the boundary between life and death grew thin. The undead are souls that cannot pass through the Final Gate, trapped in rotting bodies by divine corruption.",
            BaseColor = "gray",
            AttackType = "necrotic",
            Tiers = new List<MonsterTier>
            {
                new() { Name = "Zombie", MinLevel = 1, MaxLevel = 15, Color = "gray", PowerMultiplier = 0.9f },
                new() { Name = "Ghoul", MinLevel = 16, MaxLevel = 30, Color = "cyan", PowerMultiplier = 1.1f, SpecialAbilities = new() { "Paralyze" } },
                new() { Name = "Wight", MinLevel = 31, MaxLevel = 50, Color = "blue", PowerMultiplier = 1.4f, SpecialAbilities = new() { "LifeDrain" } },
                new() { Name = "Wraith", MinLevel = 51, MaxLevel = 75, Color = "bright_blue", PowerMultiplier = 1.7f, SpecialAbilities = new() { "LifeDrain", "Incorporeal" } },
                new() { Name = "Lich", MinLevel = 76, MaxLevel = 100, Color = "bright_magenta", PowerMultiplier = 2.2f, SpecialAbilities = new() { "LifeDrain", "Spellcasting", "Phylactery" } }
            }
        },

        // ORCISH FAMILY - Maelketh's chosen warriors
        new MonsterFamily
        {
            FamilyName = "Orcish",
            Description = "Unlike goblins who were corrupted, orcs willingly embraced Maelketh's war-blessing. They are the Broken Blade's most devoted followers, believing that dying in glorious battle will earn them a place in his eternal army.",
            BaseColor = "red",
            AttackType = "physical",
            Tiers = new List<MonsterTier>
            {
                new() { Name = "Orc", MinLevel = 5, MaxLevel = 20, Color = "red", PowerMultiplier = 1.0f },
                new() { Name = "Orc Warrior", MinLevel = 21, MaxLevel = 40, Color = "bright_red", PowerMultiplier = 1.3f, SpecialAbilities = new() { "Rage" } },
                new() { Name = "Orc Berserker", MinLevel = 41, MaxLevel = 60, Color = "bright_red", PowerMultiplier = 1.6f, SpecialAbilities = new() { "Rage", "Frenzy" } },
                new() { Name = "Orc Chieftain", MinLevel = 61, MaxLevel = 80, Color = "magenta", PowerMultiplier = 1.9f, SpecialAbilities = new() { "Rage", "Frenzy", "Warcry" } },
                new() { Name = "Orc Warlord", MinLevel = 81, MaxLevel = 100, Color = "bright_magenta", PowerMultiplier = 2.3f, SpecialAbilities = new() { "Rage", "Frenzy", "Warcry", "Cleave" } }
            }
        },

        // DRACONIC FAMILY - Ancient servants of the Creator
        new MonsterFamily
        {
            FamilyName = "Draconic",
            Description = "Dragons existed before the Old Gods, created directly by Manwe to guard the world. When the Creator grew distant, many dragons fell to corruption. The Ancient Dragons remember the truth, but their minds are clouded by millennia of isolation.",
            BaseColor = "bright_red",
            AttackType = "fire",
            Tiers = new List<MonsterTier>
            {
                new() { Name = "Kobold", MinLevel = 1, MaxLevel = 12, Color = "yellow", PowerMultiplier = 0.7f },
                new() { Name = "Drake", MinLevel = 13, MaxLevel = 30, Color = "bright_yellow", PowerMultiplier = 1.2f, SpecialAbilities = new() { "FireBreath" } },
                new() { Name = "Wyvern", MinLevel = 31, MaxLevel = 55, Color = "bright_red", PowerMultiplier = 1.5f, SpecialAbilities = new() { "FireBreath", "Flight" } },
                new() { Name = "Young Dragon", MinLevel = 56, MaxLevel = 80, Color = "bright_red", PowerMultiplier = 1.9f, SpecialAbilities = new() { "FireBreath", "Flight", "DragonFear" } },
                new() { Name = "Ancient Dragon", MinLevel = 81, MaxLevel = 100, Color = "bright_magenta", PowerMultiplier = 2.5f, SpecialAbilities = new() { "FireBreath", "Flight", "DragonFear", "AncientMagic" } }
            }
        },

        // DEMONIC FAMILY - Creatures from beyond the Veil
        new MonsterFamily
        {
            FamilyName = "Demonic",
            Description = "When the Sundering tore holes in reality, demons crawled through from the Void beyond. They are not of this world - they are what exists in the spaces between Manwe's creation, hungry to consume what the Creator made.",
            BaseColor = "red",
            AttackType = "fire",
            Tiers = new List<MonsterTier>
            {
                new() { Name = "Imp", MinLevel = 8, MaxLevel = 20, Color = "red", PowerMultiplier = 0.9f, SpecialAbilities = new() { "Invisibility" } },
                new() { Name = "Demon", MinLevel = 21, MaxLevel = 40, Color = "bright_red", PowerMultiplier = 1.3f, SpecialAbilities = new() { "Teleport" } },
                new() { Name = "Greater Demon", MinLevel = 41, MaxLevel = 65, Color = "magenta", PowerMultiplier = 1.7f, SpecialAbilities = new() { "Teleport", "Hellfire" } },
                new() { Name = "Demon Lord", MinLevel = 66, MaxLevel = 85, Color = "bright_magenta", PowerMultiplier = 2.1f, SpecialAbilities = new() { "Teleport", "Hellfire", "Corruption" } },
                new() { Name = "Archfiend", MinLevel = 86, MaxLevel = 100, Color = "bright_magenta", PowerMultiplier = 2.6f, SpecialAbilities = new() { "Teleport", "Hellfire", "Corruption", "Dominate" } }
            }
        },

        // GIANT FAMILY - Children of Terravok
        new MonsterFamily
        {
            FamilyName = "Giant",
            Description = "The giants were born from Terravok's dreams as he slept beneath the mountains. When the Earth God fell into his deep slumber, his children lost their purpose. Now they wander, confused and angry, awaiting a father who may never wake.",
            BaseColor = "bright_white",
            AttackType = "physical",
            Tiers = new List<MonsterTier>
            {
                new() { Name = "Ogre", MinLevel = 10, MaxLevel = 25, Color = "white", PowerMultiplier = 1.2f },
                new() { Name = "Troll", MinLevel = 26, MaxLevel = 45, Color = "green", PowerMultiplier = 1.5f, SpecialAbilities = new() { "Regeneration" } },
                new() { Name = "Hill Giant", MinLevel = 46, MaxLevel = 65, Color = "bright_white", PowerMultiplier = 1.8f, SpecialAbilities = new() { "Boulder" } },
                new() { Name = "Stone Giant", MinLevel = 66, MaxLevel = 85, Color = "gray", PowerMultiplier = 2.2f, SpecialAbilities = new() { "Boulder", "Stoneskin" } },
                new() { Name = "Titan", MinLevel = 86, MaxLevel = 100, Color = "bright_yellow", PowerMultiplier = 2.7f, SpecialAbilities = new() { "Lightning", "Earthquake", "Stoneskin" } }
            }
        },

        // BEAST FAMILY - Nature corrupted by divine war
        new MonsterFamily
        {
            FamilyName = "Beast",
            Description = "When the Old Gods warred, their divine energy poisoned the natural world. Beasts grew larger, fiercer, hungrier. The worst are the Lycanthropes - mortals cursed to transform under Noctura's moon, caught between human and animal.",
            BaseColor = "yellow",
            AttackType = "physical",
            Tiers = new List<MonsterTier>
            {
                new() { Name = "Wolf", MinLevel = 1, MaxLevel = 15, Color = "white", PowerMultiplier = 0.8f, SpecialAbilities = new() { "PackTactics" } },
                new() { Name = "Dire Wolf", MinLevel = 16, MaxLevel = 35, Color = "gray", PowerMultiplier = 1.1f, SpecialAbilities = new() { "PackTactics", "Bite" } },
                new() { Name = "Werewolf", MinLevel = 36, MaxLevel = 60, Color = "bright_white", PowerMultiplier = 1.5f, SpecialAbilities = new() { "PackTactics", "Lycanthropy", "Regeneration" } },
                new() { Name = "Alpha Werewolf", MinLevel = 61, MaxLevel = 80, Color = "bright_yellow", PowerMultiplier = 1.9f, SpecialAbilities = new() { "PackTactics", "Lycanthropy", "Regeneration", "Howl" } },
                new() { Name = "Fenrir", MinLevel = 81, MaxLevel = 100, Color = "bright_cyan", PowerMultiplier = 2.4f, SpecialAbilities = new() { "Devour", "Moonlight", "Regeneration" } }
            }
        },

        // ELEMENTAL FAMILY - Pure creation energy given form
        new MonsterFamily
        {
            FamilyName = "Elemental",
            Description = "Elementals are raw creation energy given form - the building blocks Manwe used to forge the world. Without the Creator's guiding will, they have become chaotic and dangerous, burning with purpose they can no longer remember.",
            BaseColor = "bright_red",
            AttackType = "fire",
            Tiers = new List<MonsterTier>
            {
                new() { Name = "Spark", MinLevel = 5, MaxLevel = 18, Color = "yellow", PowerMultiplier = 0.7f },
                new() { Name = "Fire Elemental", MinLevel = 19, MaxLevel = 38, Color = "bright_yellow", PowerMultiplier = 1.2f, SpecialAbilities = new() { "Burn" } },
                new() { Name = "Inferno", MinLevel = 39, MaxLevel = 60, Color = "bright_red", PowerMultiplier = 1.6f, SpecialAbilities = new() { "Burn", "Immolate" } },
                new() { Name = "Fire Lord", MinLevel = 61, MaxLevel = 85, Color = "bright_red", PowerMultiplier = 2.0f, SpecialAbilities = new() { "Burn", "Immolate", "Fireball" } },
                new() { Name = "Phoenix", MinLevel = 86, MaxLevel = 100, Color = "bright_magenta", PowerMultiplier = 2.5f, SpecialAbilities = new() { "Rebirth", "Immolate", "Inferno" } }
            }
        },

        // ABERRATION FAMILY - What should not exist
        new MonsterFamily
        {
            FamilyName = "Aberration",
            Description = "Aberrations are mistakes - things that slipped through when Manwe created reality. They exist outside natural law, their very presence an affront to creation. The scholars say they came from the same Void as the demons, but are far older.",
            BaseColor = "green",
            AttackType = "acid",
            Tiers = new List<MonsterTier>
            {
                new() { Name = "Ooze", MinLevel = 3, MaxLevel = 15, Color = "green", PowerMultiplier = 0.6f, SpecialAbilities = new() { "Corrosion" } },
                new() { Name = "Slime", MinLevel = 16, MaxLevel = 32, Color = "bright_green", PowerMultiplier = 1.0f, SpecialAbilities = new() { "Corrosion", "Split" } },
                new() { Name = "Gelatinous Cube", MinLevel = 33, MaxLevel = 55, Color = "cyan", PowerMultiplier = 1.4f, SpecialAbilities = new() { "Engulf", "Corrosion" } },
                new() { Name = "Elder Ooze", MinLevel = 56, MaxLevel = 80, Color = "bright_cyan", PowerMultiplier = 1.8f, SpecialAbilities = new() { "Engulf", "Corrosion", "Absorb" } },
                new() { Name = "Shoggoth", MinLevel = 81, MaxLevel = 100, Color = "bright_magenta", PowerMultiplier = 2.3f, SpecialAbilities = new() { "Engulf", "ShapeShift", "Madness" } }
            }
        },

        // INSECTOID FAMILY - Noctura's children
        new MonsterFamily
        {
            FamilyName = "Insectoid",
            Description = "The spider-kin are Noctura's favored children, dwelling in darkness and spinning webs of shadow. The Shadow Weaver blessed them with cunning and venom, making them perfect hunters in the lightless depths where secrets are kept.",
            BaseColor = "white",
            AttackType = "poison",
            Tiers = new List<MonsterTier>
            {
                new() { Name = "Giant Spider", MinLevel = 5, MaxLevel = 18, Color = "white", PowerMultiplier = 0.9f, SpecialAbilities = new() { "WebTrap" } },
                new() { Name = "Phase Spider", MinLevel = 19, MaxLevel = 38, Color = "gray", PowerMultiplier = 1.2f, SpecialAbilities = new() { "WebTrap", "PhaseShift" } },
                new() { Name = "Spider Queen", MinLevel = 39, MaxLevel = 62, Color = "bright_white", PowerMultiplier = 1.6f, SpecialAbilities = new() { "WebTrap", "Poison", "SummonSpiders" } },
                new() { Name = "Arachnid Horror", MinLevel = 63, MaxLevel = 85, Color = "bright_red", PowerMultiplier = 2.0f, SpecialAbilities = new() { "DeadlyVenom", "WebTrap", "SummonSpiders" } },
                new() { Name = "Broodmother", MinLevel = 86, MaxLevel = 100, Color = "bright_magenta", PowerMultiplier = 2.4f, SpecialAbilities = new() { "DeadlyVenom", "Swarm", "Cocoon" } }
            }
        },

        // CONSTRUCT FAMILY - Thorgrim's enforcers
        new MonsterFamily
        {
            FamilyName = "Construct",
            Description = "Golems were created by Thorgrim to enforce his absolute law. When the god of justice became the god of tyranny, his constructs continued to follow their programming - destroy all who break the law. But whose law do they serve now?",
            BaseColor = "gray",
            AttackType = "physical",
            Tiers = new List<MonsterTier>
            {
                new() { Name = "Animated Armor", MinLevel = 8, MaxLevel = 22, Color = "white", PowerMultiplier = 1.0f },
                new() { Name = "Golem", MinLevel = 23, MaxLevel = 45, Color = "gray", PowerMultiplier = 1.4f, SpecialAbilities = new() { "ImmuneMagic" } },
                new() { Name = "Iron Golem", MinLevel = 46, MaxLevel = 68, Color = "bright_white", PowerMultiplier = 1.8f, SpecialAbilities = new() { "ImmuneMagic", "PoisonGas" } },
                new() { Name = "Adamantine Golem", MinLevel = 69, MaxLevel = 88, Color = "bright_cyan", PowerMultiplier = 2.2f, SpecialAbilities = new() { "ImmuneMagic", "Indestructible" } },
                new() { Name = "Ancient Construct", MinLevel = 89, MaxLevel = 100, Color = "bright_yellow", PowerMultiplier = 2.6f, SpecialAbilities = new() { "ImmuneMagic", "SelfRepair", "Overload" } }
            }
        },

        // FEY FAMILY - Remnants of a kinder age
        new MonsterFamily
        {
            FamilyName = "Fey",
            Description = "The Fey remember the world before the Sundering, when the gods walked openly and love was not yet twisted into jealousy. Some say they are Veloura's first children, born from joy itself. Now they guard what remains of the old beauty.",
            BaseColor = "green",
            AttackType = "magic",
            Tiers = new List<MonsterTier>
            {
                new() { Name = "Pixie", MinLevel = 1, MaxLevel = 15, Color = "cyan", PowerMultiplier = 0.6f, SpecialAbilities = new() { "Invisibility" } },
                new() { Name = "Sprite", MinLevel = 16, MaxLevel = 32, Color = "bright_cyan", PowerMultiplier = 0.9f, SpecialAbilities = new() { "Invisibility", "Sleep" } },
                new() { Name = "Dryad", MinLevel = 33, MaxLevel = 55, Color = "green", PowerMultiplier = 1.3f, SpecialAbilities = new() { "TreeMeld", "Charm" } },
                new() { Name = "Treant", MinLevel = 56, MaxLevel = 80, Color = "bright_green", PowerMultiplier = 1.8f, SpecialAbilities = new() { "AnimateTrees", "RootEntangle" } },
                new() { Name = "Archfey", MinLevel = 81, MaxLevel = 100, Color = "bright_magenta", PowerMultiplier = 2.3f, SpecialAbilities = new() { "Dominate", "TimeStop", "WildShape" } }
            }
        },

        // AQUATIC FAMILY - The Ocean's memories
        new MonsterFamily
        {
            FamilyName = "Aquatic",
            Description = "The sea creatures know the truth that mortals have forgotten - that all existence is like water, and separation is an illusion. The Elder Krakens remember when Manwe first dreamed of waves, and they wait for the dreamer to wake.",
            BaseColor = "blue",
            AttackType = "physical",
            Tiers = new List<MonsterTier>
            {
                new() { Name = "Merrow", MinLevel = 12, MaxLevel = 28, Color = "blue", PowerMultiplier = 1.0f },
                new() { Name = "Sea Troll", MinLevel = 29, MaxLevel = 50, Color = "bright_blue", PowerMultiplier = 1.4f, SpecialAbilities = new() { "Regeneration" } },
                new() { Name = "Kraken Spawn", MinLevel = 51, MaxLevel = 72, Color = "bright_cyan", PowerMultiplier = 1.7f, SpecialAbilities = new() { "TentacleGrab", "InkCloud" } },
                new() { Name = "Leviathan", MinLevel = 73, MaxLevel = 90, Color = "bright_blue", PowerMultiplier = 2.1f, SpecialAbilities = new() { "TentacleGrab", "Devour", "Whirlpool" } },
                new() { Name = "Elder Kraken", MinLevel = 91, MaxLevel = 100, Color = "bright_magenta", PowerMultiplier = 2.5f, SpecialAbilities = new() { "TentacleGrab", "Devour", "TidalWave" } }
            }
        },

        // CELESTIAL FAMILY - Aurelion's fallen servants
        new MonsterFamily
        {
            FamilyName = "Celestial",
            Description = "Once the guardians of Aurelion's light, the celestials have dimmed with their master. Some still serve the Fading Light, desperately trying to preserve what truth remains. Others have gone mad, judging all mortals guilty of the lies that weakened their god.",
            BaseColor = "bright_white",
            AttackType = "holy",
            Tiers = new List<MonsterTier>
            {
                new() { Name = "Angel", MinLevel = 20, MaxLevel = 38, Color = "bright_white", PowerMultiplier = 1.2f, SpecialAbilities = new() { "Heal", "Flight" } },
                new() { Name = "Archangel", MinLevel = 39, MaxLevel = 58, Color = "bright_yellow", PowerMultiplier = 1.6f, SpecialAbilities = new() { "Heal", "Flight", "HolySmite" } },
                new() { Name = "Seraph", MinLevel = 59, MaxLevel = 78, Color = "bright_yellow", PowerMultiplier = 2.0f, SpecialAbilities = new() { "Heal", "Flight", "HolySmite", "Purify" } },
                new() { Name = "Throne", MinLevel = 79, MaxLevel = 92, Color = "bright_cyan", PowerMultiplier = 2.4f, SpecialAbilities = new() { "Flight", "DivineJudgment", "Sanctuary" } },
                new() { Name = "Empyrean", MinLevel = 93, MaxLevel = 100, Color = "bright_magenta", PowerMultiplier = 2.8f, SpecialAbilities = new() { "Flight", "DivineJudgment", "Resurrection" } }
            }
        },

        // SHADOW FAMILY - Noctura's truest servants
        new MonsterFamily
        {
            FamilyName = "Shadow",
            Description = "Shadow creatures are extensions of Noctura herself - pieces of her being that walk the world gathering secrets. The most powerful among them, the Void Entities, are said to know Noctura's true plan... and the truth about who the player really is.",
            BaseColor = "gray",
            AttackType = "necrotic",
            Tiers = new List<MonsterTier>
            {
                new() { Name = "Shadow", MinLevel = 15, MaxLevel = 32, Color = "gray", PowerMultiplier = 1.0f, SpecialAbilities = new() { "Incorporeal" } },
                new() { Name = "Shade", MinLevel = 33, MaxLevel = 52, Color = "blue", PowerMultiplier = 1.3f, SpecialAbilities = new() { "Incorporeal", "StrengthDrain" } },
                new() { Name = "Nightshade", MinLevel = 53, MaxLevel = 72, Color = "bright_blue", PowerMultiplier = 1.7f, SpecialAbilities = new() { "Incorporeal", "StrengthDrain", "Terror" } },
                new() { Name = "Umbral Horror", MinLevel = 73, MaxLevel = 90, Color = "magenta", PowerMultiplier = 2.1f, SpecialAbilities = new() { "Incorporeal", "Possess", "Nightmare" } },
                new() { Name = "Void Entity", MinLevel = 91, MaxLevel = 100, Color = "bright_magenta", PowerMultiplier = 2.6f, SpecialAbilities = new() { "Incorporeal", "DevourSoul", "RealityBreak" } }
            }
        }
    };

    /// <summary>
    /// Get appropriate monster tier for a given level
    /// </summary>
    public static (MonsterFamily family, MonsterTier tier) GetMonsterForLevel(int level, Random? random = null)
    {
        random ??= Random.Shared;

        // Get all families that have tiers for this level
        var suitableFamilies = AllFamilies
            .Where(f => f.Tiers.Any(t => level >= t.MinLevel && level <= t.MaxLevel))
            .ToList();

        if (suitableFamilies.Count == 0)
        {
            // Fallback to goblin
            var goblinFamily = AllFamilies.First(f => f.FamilyName == "Goblinoid");
            var fallbackTier = goblinFamily.Tiers.First();
            return (goblinFamily, fallbackTier);
        }

        // Pick random family
        var family = suitableFamilies[random.Next(suitableFamilies.Count)];

        // Get appropriate tier for this level
        var tier = family.Tiers.FirstOrDefault(t => level >= t.MinLevel && level <= t.MaxLevel)
                   ?? family.Tiers.Last();

        return (family, tier);
    }

    /// <summary>
    /// Get monster name with color
    /// </summary>
    public static string GetColoredMonsterName(string name, string color)
    {
        return $"[{color}]{name}[/]";
    }
}
