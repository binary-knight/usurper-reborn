using System;
using System.Collections.Generic;
using System.Linq;

namespace UsurperRemake.Data
{
    /// <summary>
    /// Ability used by a world boss during combat.
    /// </summary>
    public class WorldBossAbility
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public float DamageMultiplier { get; set; } = 1.0f;
        public StatusEffect? AppliedStatus { get; set; }
        public int StatusDuration { get; set; }
        public bool IsAoE { get; set; }
        public bool IsUnavoidable { get; set; }
        public float SelfHealPercent { get; set; }
    }

    /// <summary>
    /// Full definition for a world boss encounter.
    /// </summary>
    public class WorldBossDefinition
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Title { get; set; } = "";
        public string Element { get; set; } = "";
        public string ThemeColor { get; set; } = "bright_red";
        public int BaseLevel { get; set; }
        public long BaseHP { get; set; }
        public long BaseStrength { get; set; }
        public long BaseDefence { get; set; }
        public long BaseAgility { get; set; }
        public int AttacksPerRound { get; set; } = 2;
        public float AuraBaseDamagePercent { get; set; } = 0.05f;
        public string LootTheme { get; set; } = "";

        public List<WorldBossAbility> Phase1Abilities { get; set; } = new();
        public List<WorldBossAbility> Phase2Abilities { get; set; } = new();
        public List<WorldBossAbility> Phase3Abilities { get; set; } = new();

        public string[] SpawnDialogue { get; set; } = Array.Empty<string>();
        public string[] Phase2Dialogue { get; set; } = Array.Empty<string>();
        public string[] Phase3Dialogue { get; set; } = Array.Empty<string>();
        public string[] DefeatDialogue { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// Database of all 8 world bosses with stats, phases, and abilities.
    /// </summary>
    public static class WorldBossDatabase
    {
        private static readonly List<WorldBossDefinition> AllBosses = new()
        {
            GetAbyssalLeviathan(),
            GetVoidColossus(),
            GetShadowlordMalachar(),
            GetCrimsonWyrm(),
            GetLichKingVareth(),
            GetIronTitan(),
            GetDreadSerpentNidhogg(),
            GetNamelessHorror()
        };

        public static List<WorldBossDefinition> GetAllBosses() => AllBosses;

        public static WorldBossDefinition? GetBossById(string id)
            => AllBosses.FirstOrDefault(b => b.Id == id);

        public static WorldBossDefinition GetRandomBoss(Random? rng = null)
        {
            rng ??= new Random();
            return AllBosses[rng.Next(AllBosses.Count)];
        }

        // ═══════════════════════════════════════════════════════════════
        // Boss Definitions
        // ═══════════════════════════════════════════════════════════════

        private static WorldBossDefinition GetAbyssalLeviathan() => new()
        {
            Id = "abyssal_leviathan",
            Name = "The Abyssal Leviathan",
            Title = "Terror of the Deep",
            Element = "Water",
            ThemeColor = "bright_cyan",
            BaseLevel = 40,
            BaseHP = 200_000,
            BaseStrength = 140,
            BaseDefence = 100,
            BaseAgility = 80,
            AttacksPerRound = 2,
            AuraBaseDamagePercent = 0.05f,
            LootTheme = "Water",
            Phase1Abilities = new()
            {
                new() { Name = "Tidal Surge", Description = "A crushing wave slams into you!", DamageMultiplier = 1.2f, AppliedStatus = StatusEffect.Slow, StatusDuration = 3 },
                new() { Name = "Frost Bolt", Description = "A spear of ice pierces through you!", DamageMultiplier = 1.0f, AppliedStatus = StatusEffect.Frozen, StatusDuration = 2 },
            },
            Phase2Abilities = new()
            {
                new() { Name = "Whirlpool", Description = "The waters churn violently around you!", DamageMultiplier = 1.5f, IsAoE = true, IsUnavoidable = true, AppliedStatus = StatusEffect.Slow, StatusDuration = 2 },
                new() { Name = "Deep Freeze", Description = "The Leviathan encases you in ice!", DamageMultiplier = 1.3f, AppliedStatus = StatusEffect.Frozen, StatusDuration = 3 },
            },
            Phase3Abilities = new()
            {
                new() { Name = "Drown", Description = "The Leviathan drags you beneath the waves!", DamageMultiplier = 2.0f, AppliedStatus = StatusEffect.Stunned, StatusDuration = 1 },
                new() { Name = "Tsunami", Description = "A tidal wave crashes over the battlefield!", DamageMultiplier = 1.8f, IsAoE = true, IsUnavoidable = true },
            },
            SpawnDialogue = new[] { "The earth trembles as dark waters surge from below...", "The Abyssal Leviathan rises from the depths, its eyes burning with ancient fury!" },
            Phase2Dialogue = new[] { "The Leviathan shrieks in rage, the waters around it beginning to churn violently!" },
            Phase3Dialogue = new[] { "The Leviathan's eyes glow with desperate fury as the deep itself answers its call!" },
            DefeatDialogue = new[] { "The Abyssal Leviathan lets out a final roar before sinking back into the depths..." },
        };

        private static WorldBossDefinition GetVoidColossus() => new()
        {
            Id = "void_colossus",
            Name = "Void Colossus",
            Title = "Harbinger of Nothing",
            Element = "Void",
            ThemeColor = "bright_magenta",
            BaseLevel = 50,
            BaseHP = 280_000,
            BaseStrength = 170,
            BaseDefence = 130,
            BaseAgility = 70,
            AttacksPerRound = 2,
            AuraBaseDamagePercent = 0.06f,
            LootTheme = "Void",
            Phase1Abilities = new()
            {
                new() { Name = "Gravity Crush", Description = "Invisible force slams you into the ground!", DamageMultiplier = 1.2f, AppliedStatus = StatusEffect.Stunned, StatusDuration = 1 },
                new() { Name = "Void Bolt", Description = "A bolt of nothingness sears through your mind!", DamageMultiplier = 1.0f, AppliedStatus = StatusEffect.Silenced, StatusDuration = 2 },
            },
            Phase2Abilities = new()
            {
                new() { Name = "Dimensional Rift", Description = "Reality tears around you!", DamageMultiplier = 1.3f, AppliedStatus = StatusEffect.Confused, StatusDuration = 2 },
                new() { Name = "Null Zone", Description = "A sphere of void energy expands!", DamageMultiplier = 1.5f, IsAoE = true, AppliedStatus = StatusEffect.Weakened, StatusDuration = 3 },
            },
            Phase3Abilities = new()
            {
                new() { Name = "Reality Tear", Description = "The fabric of existence shreds around you!", DamageMultiplier = 2.0f, AppliedStatus = StatusEffect.Confused, StatusDuration = 2, IsAoE = true },
                new() { Name = "Annihilate", Description = "The Colossus focuses all its power on destroying you!", DamageMultiplier = 2.5f },
            },
            SpawnDialogue = new[] { "A crack appears in the sky, and a massive form steps through...", "The Void Colossus has crossed into our reality!" },
            Phase2Dialogue = new[] { "The Colossus roars soundlessly, reality warping around its form!" },
            Phase3Dialogue = new[] { "The Void Colossus begins to unravel, unleashing chaotic energy!" },
            DefeatDialogue = new[] { "The Void Colossus shatters like glass, fragments dissolving into nothing..." },
        };

        private static WorldBossDefinition GetShadowlordMalachar() => new()
        {
            Id = "shadowlord_malachar",
            Name = "Shadowlord Malachar",
            Title = "The Undying Shadow",
            Element = "Shadow",
            ThemeColor = "gray",
            BaseLevel = 35,
            BaseHP = 180_000,
            BaseStrength = 120,
            BaseDefence = 90,
            BaseAgility = 110,
            AttacksPerRound = 3,
            AuraBaseDamagePercent = 0.05f,
            LootTheme = "Shadow",
            Phase1Abilities = new()
            {
                new() { Name = "Shadow Strike", Description = "Malachar's blade cuts through shadow itself!", DamageMultiplier = 1.1f },
                new() { Name = "Fear", Description = "Malachar's gaze fills you with dread!", DamageMultiplier = 0.5f, AppliedStatus = StatusEffect.Feared, StatusDuration = 2 },
            },
            Phase2Abilities = new()
            {
                new() { Name = "Dark Pact", Description = "Malachar feeds on the shadows, healing himself!", DamageMultiplier = 0.3f, SelfHealPercent = 0.03f },
                new() { Name = "Soul Drain", Description = "Dark tendrils pull at your very essence!", DamageMultiplier = 1.4f, AppliedStatus = StatusEffect.Exhausted, StatusDuration = 3 },
            },
            Phase3Abilities = new()
            {
                new() { Name = "Eclipse", Description = "Darkness engulfs everything!", DamageMultiplier = 1.8f, IsAoE = true, AppliedStatus = StatusEffect.Blinded, StatusDuration = 2 },
                new() { Name = "Shadow Devour", Description = "Malachar's shadow swallows you whole!", DamageMultiplier = 2.2f },
            },
            SpawnDialogue = new[] { "The light dims as shadows coalesce into a towering figure...", "Shadowlord Malachar steps from the darkness, his eyes gleaming!" },
            Phase2Dialogue = new[] { "Malachar laughs as the shadows grow deeper around him!" },
            Phase3Dialogue = new[] { "\"You will join my shadow!\" Malachar's form grows massive!" },
            DefeatDialogue = new[] { "Malachar's form dissipates like smoke on the wind..." },
        };

        private static WorldBossDefinition GetCrimsonWyrm() => new()
        {
            Id = "crimson_wyrm",
            Name = "The Crimson Wyrm",
            Title = "Flame of the Ancient World",
            Element = "Fire",
            ThemeColor = "bright_red",
            BaseLevel = 45,
            BaseHP = 250_000,
            BaseStrength = 160,
            BaseDefence = 120,
            BaseAgility = 90,
            AttacksPerRound = 2,
            AuraBaseDamagePercent = 0.07f,
            LootTheme = "Fire",
            Phase1Abilities = new()
            {
                new() { Name = "Fire Breath", Description = "The Wyrm unleashes a torrent of flame!", DamageMultiplier = 1.3f, IsAoE = true, AppliedStatus = StatusEffect.Burning, StatusDuration = 3 },
                new() { Name = "Tail Swipe", Description = "The Wyrm's massive tail slams into you!", DamageMultiplier = 1.0f, AppliedStatus = StatusEffect.Stunned, StatusDuration = 1 },
            },
            Phase2Abilities = new()
            {
                new() { Name = "Wing Buffet", Description = "The Wyrm's wings generate a fiery blast wave!", DamageMultiplier = 1.2f, AppliedStatus = StatusEffect.Vulnerable, StatusDuration = 3 },
                new() { Name = "Molten Armor", Description = "The Wyrm coats itself in liquid flame!", DamageMultiplier = 0.3f, SelfHealPercent = 0.02f },
            },
            Phase3Abilities = new()
            {
                new() { Name = "Inferno", Description = "The Wyrm's fury engulfs the entire battlefield in fire!", DamageMultiplier = 2.0f, IsAoE = true, IsUnavoidable = true, AppliedStatus = StatusEffect.Burning, StatusDuration = 4 },
                new() { Name = "Eruption", Description = "The ground erupts in pillars of flame beneath you!", DamageMultiplier = 2.5f },
            },
            SpawnDialogue = new[] { "The sky turns crimson as a massive winged shape descends...", "The Crimson Wyrm lands with a thunderous roar, its scales glowing like embers!" },
            Phase2Dialogue = new[] { "The Wyrm spreads its wings and the air itself ignites!" },
            Phase3Dialogue = new[] { "The Crimson Wyrm rears back, its body erupting in white-hot flame!" },
            DefeatDialogue = new[] { "The Crimson Wyrm crashes to the ground, its flames slowly dying..." },
        };

        private static WorldBossDefinition GetLichKingVareth() => new()
        {
            Id = "lich_king_vareth",
            Name = "Lich King Vareth",
            Title = "Lord of the Undying Legion",
            Element = "Undead",
            ThemeColor = "bright_green",
            BaseLevel = 55,
            BaseHP = 320_000,
            BaseStrength = 150,
            BaseDefence = 140,
            BaseAgility = 60,
            AttacksPerRound = 2,
            AuraBaseDamagePercent = 0.06f,
            LootTheme = "Undead",
            Phase1Abilities = new()
            {
                new() { Name = "Death Bolt", Description = "A bolt of necrotic energy strikes you!", DamageMultiplier = 1.3f, AppliedStatus = StatusEffect.Cursed, StatusDuration = 3 },
                new() { Name = "Poison Cloud", Description = "A cloud of noxious gas surrounds you!", DamageMultiplier = 0.8f, IsAoE = true, AppliedStatus = StatusEffect.Poisoned, StatusDuration = 4 },
            },
            Phase2Abilities = new()
            {
                new() { Name = "Raise Dead", Description = "Vareth raises fallen warriors to fight for him!", DamageMultiplier = 0.5f, SelfHealPercent = 0.05f },
                new() { Name = "Soul Shatter", Description = "Vareth reaches into your very soul!", DamageMultiplier = 1.6f, AppliedStatus = StatusEffect.Silenced, StatusDuration = 2 },
            },
            Phase3Abilities = new()
            {
                new() { Name = "Death's Embrace", Description = "Death itself wraps around you!", DamageMultiplier = 2.2f, AppliedStatus = StatusEffect.Paralyzed, StatusDuration = 2 },
                new() { Name = "Plague Wave", Description = "A wave of pestilence washes over the battlefield!", DamageMultiplier = 1.5f, IsAoE = true, AppliedStatus = StatusEffect.Diseased, StatusDuration = 5 },
            },
            SpawnDialogue = new[] { "The ground cracks as skeletal hands claw their way upward...", "Lich King Vareth rises from his throne of bones, his phylactery pulsing!" },
            Phase2Dialogue = new[] { "\"Rise, my servants!\" Vareth commands the dead to fight!" },
            Phase3Dialogue = new[] { "Vareth screams in fury, unleashing the full power of undeath!" },
            DefeatDialogue = new[] { "Vareth's body crumbles to dust, his phylactery shattering..." },
        };

        private static WorldBossDefinition GetIronTitan() => new()
        {
            Id = "iron_titan",
            Name = "The Iron Titan",
            Title = "The Unbreakable",
            Element = "Physical",
            ThemeColor = "white",
            BaseLevel = 60,
            BaseHP = 380_000,
            BaseStrength = 200,
            BaseDefence = 180,
            BaseAgility = 50,
            AttacksPerRound = 2,
            AuraBaseDamagePercent = 0.08f,
            LootTheme = "Physical",
            Phase1Abilities = new()
            {
                new() { Name = "Crushing Blow", Description = "The Titan's fist crashes down on you!", DamageMultiplier = 1.4f, AppliedStatus = StatusEffect.Vulnerable, StatusDuration = 3 },
                new() { Name = "Ground Slam", Description = "The earth shatters beneath the Titan's fist!", DamageMultiplier = 1.1f, IsAoE = true, AppliedStatus = StatusEffect.Stunned, StatusDuration = 1 },
            },
            Phase2Abilities = new()
            {
                new() { Name = "Armor Break", Description = "The Titan shatters your defenses!", DamageMultiplier = 0.8f, AppliedStatus = StatusEffect.Weakened, StatusDuration = 4 },
                new() { Name = "Seismic Wave", Description = "Shockwaves radiate from the Titan's every step!", DamageMultiplier = 1.5f, IsAoE = true, IsUnavoidable = true },
            },
            Phase3Abilities = new()
            {
                new() { Name = "Berserk Rage", Description = "The Titan enters a berserking frenzy!", DamageMultiplier = 0.5f, SelfHealPercent = 0.02f },
                new() { Name = "Devastate", Description = "The Titan brings both fists down with apocalyptic force!", DamageMultiplier = 2.5f, AppliedStatus = StatusEffect.Stunned, StatusDuration = 2 },
            },
            SpawnDialogue = new[] { "The ground shakes rhythmically as something massive approaches...", "The Iron Titan appears, a colossus of living metal and ancient stone!" },
            Phase2Dialogue = new[] { "The Titan's armor begins to crack, revealing a molten core within!" },
            Phase3Dialogue = new[] { "The Iron Titan roars, its eyes blazing as it enters a berserking frenzy!" },
            DefeatDialogue = new[] { "The Iron Titan topples, the ground shaking as it crashes down for the last time..." },
        };

        private static WorldBossDefinition GetDreadSerpentNidhogg() => new()
        {
            Id = "dread_serpent_nidhogg",
            Name = "Dread Serpent Nidhogg",
            Title = "World Eater",
            Element = "Poison",
            ThemeColor = "green",
            BaseLevel = 70,
            BaseHP = 420_000,
            BaseStrength = 190,
            BaseDefence = 150,
            BaseAgility = 100,
            AttacksPerRound = 3,
            AuraBaseDamagePercent = 0.07f,
            LootTheme = "Poison",
            Phase1Abilities = new()
            {
                new() { Name = "Venom Fang", Description = "Nidhogg's venomous fangs sink into you!", DamageMultiplier = 1.2f, AppliedStatus = StatusEffect.Poisoned, StatusDuration = 4 },
                new() { Name = "Constrict", Description = "The serpent wraps its coils around you!", DamageMultiplier = 1.0f, AppliedStatus = StatusEffect.Stunned, StatusDuration = 2 },
            },
            Phase2Abilities = new()
            {
                new() { Name = "Toxic Cloud", Description = "A cloud of deadly toxin surrounds the battlefield!", DamageMultiplier = 1.0f, IsAoE = true, AppliedStatus = StatusEffect.Poisoned, StatusDuration = 5 },
                new() { Name = "Acid Spit", Description = "Nidhogg spits corrosive acid!", DamageMultiplier = 1.5f, AppliedStatus = StatusEffect.Vulnerable, StatusDuration = 3 },
            },
            Phase3Abilities = new()
            {
                new() { Name = "World Serpent's Coil", Description = "Nidhogg's body encircles the entire area!", DamageMultiplier = 2.0f, AppliedStatus = StatusEffect.Paralyzed, StatusDuration = 2 },
                new() { Name = "Ragnarok Venom", Description = "The most lethal poison known to existence courses through you!", DamageMultiplier = 2.3f, AppliedStatus = StatusEffect.Poisoned, StatusDuration = 6 },
            },
            SpawnDialogue = new[] { "The ground splits open as a colossal serpent slithers forth...", "Dread Serpent Nidhogg, the World Eater, has awakened!" },
            Phase2Dialogue = new[] { "Nidhogg hisses, venom dripping from its massive fangs!" },
            Phase3Dialogue = new[] { "Nidhogg coils around the battlefield, its scales oozing deadly toxin!" },
            DefeatDialogue = new[] { "Nidhogg lets out a final hiss and coils lifelessly..." },
        };

        private static WorldBossDefinition GetNamelessHorror() => new()
        {
            Id = "nameless_horror",
            Name = "The Nameless Horror",
            Title = "That Which Cannot Be Named",
            Element = "Eldritch",
            ThemeColor = "bright_magenta",
            BaseLevel = 80,
            BaseHP = 500_000,
            BaseStrength = 220,
            BaseDefence = 160,
            BaseAgility = 90,
            AttacksPerRound = 3,
            AuraBaseDamagePercent = 0.08f,
            LootTheme = "Eldritch",
            Phase1Abilities = new()
            {
                new() { Name = "Mind Blast", Description = "Your mind reels from an incomprehensible assault!", DamageMultiplier = 1.1f, AppliedStatus = StatusEffect.Confused, StatusDuration = 2 },
                new() { Name = "Tentacle Lash", Description = "Writhing tentacles strike from every direction!", DamageMultiplier = 0.7f },
            },
            Phase2Abilities = new()
            {
                new() { Name = "Madness", Description = "Your grip on reality slips away!", DamageMultiplier = 1.3f, AppliedStatus = StatusEffect.Feared, StatusDuration = 2 },
                new() { Name = "Void Gaze", Description = "The Horror's gaze burns into your soul!", DamageMultiplier = 1.5f, AppliedStatus = StatusEffect.Silenced, StatusDuration = 3 },
            },
            Phase3Abilities = new()
            {
                new() { Name = "Cosmic Horror", Description = "The full weight of cosmic dread crashes upon you!", DamageMultiplier = 2.0f, IsAoE = true, AppliedStatus = StatusEffect.Feared, StatusDuration = 3 },
                new() { Name = "Devour Reality", Description = "Reality itself is consumed around you!", DamageMultiplier = 3.0f, IsUnavoidable = true },
            },
            SpawnDialogue = new[] { "The sky ripples like water and something... wrong... pushes through.", "The Nameless Horror manifests, its form defying comprehension!" },
            Phase2Dialogue = new[] { "The Horror shifts, its form becoming even more terrible!" },
            Phase3Dialogue = new[] { "Reality cracks around the Horror as it unleashes its full power!" },
            DefeatDialogue = new[] { "The Nameless Horror folds in on itself, collapsing back into whatever dimension spawned it..." },
        };

        // ═══════════════════════════════════════════════════════════════
        // Element-themed loot prefixes
        // ═══════════════════════════════════════════════════════════════

        public static string GetElementPrefix(string element) => element switch
        {
            "Water" => "Abyssal",
            "Void" => "Void-Touched",
            "Shadow" => "Shadowforged",
            "Fire" => "Embersteel",
            "Undead" => "Deathwoven",
            "Physical" => "Titanic",
            "Poison" => "Venomsteel",
            "Eldritch" => "Eldritch",
            _ => "World Boss"
        };
    }
}
