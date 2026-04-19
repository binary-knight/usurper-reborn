using System.Collections.Generic;

namespace UsurperRemake.Editor;

/// <summary>
/// Shared vocabularies / known-value lists for editor pickers. Extracted into a
/// single file so sub-editors can't drift — e.g. if we add a new personality to
/// the dialogue engine's recognized set, we update it here and every editor
/// that asks about personality picks it up automatically.
/// </summary>
internal static class EditorVocab
{
    /// <summary>
    /// Personality labels used by the built-in town NPCs. The dialogue engine
    /// groups many of these into 8 "personality types" (aggressive, noble,
    /// cunning, pious, scholarly, cynical, charming, stoic) — modders adding
    /// custom NPCs can use anything from this list and the dialogue system
    /// will map them to a known type, or they can invent new strings via the
    /// editor's "Custom..." escape hatch.
    /// </summary>
    public static readonly IReadOnlyList<string> NpcPersonalities = new[]
    {
        "Aggressive", "Ambitious", "Arrogant", "Brave", "Brooding", "Brutal",
        "Charming", "Cold", "Compassionate", "Cowardly", "Cruel", "Cunning",
        "Curious", "Deadly", "Devout", "Disciplined", "Eccentric", "Fanatical",
        "Fierce", "Flashy", "Free-spirited", "Gentle", "Greedy", "Gruff",
        "Honorable", "Insane", "Kind", "Loyal", "Lucky", "Merciless",
        "Mysterious", "Nervous", "Noble", "Obsessed", "Optimistic", "Peaceful",
        "Pious", "Professional", "Radiant", "Resolute", "Righteous", "Ruthless",
        "Scheming", "Scholarly", "Secretive", "Serene", "Sharp", "Silent",
        "Sinister", "Sneaky", "Solitary", "Stern", "Strict", "Stubborn",
        "Tormented", "Tough", "Wise", "Zealous",
    };

    /// <summary>Fixed 3-way alignment for NPCs, dialogue tiers, and similar gates.</summary>
    public static readonly IReadOnlyList<string> Alignments = new[]
    {
        "Good", "Evil", "Neutral",
    };

    /// <summary>
    /// ANSI color names understood by TerminalEmulator / loot generators /
    /// monster rendering. Used as the vocabulary for monster family / tier
    /// colors so modders don't have to guess whether it's "yellow" or "gold".
    /// </summary>
    public static readonly IReadOnlyList<string> AnsiColorNames = new[]
    {
        "black", "blue", "green", "cyan", "red", "magenta", "yellow", "white",
        "gray", "darkgray",
        "bright_blue", "bright_green", "bright_cyan", "bright_red",
        "bright_magenta", "bright_yellow", "bright_white",
        "dark_blue", "dark_green", "dark_cyan", "dark_red",
        "dark_magenta", "dark_yellow",
    };

    /// <summary>Monster family attack categories that combat effects key off of.</summary>
    public static readonly IReadOnlyList<string> MonsterAttackTypes = new[]
    {
        "physical", "magic", "poison", "fire", "frost", "lightning", "holy",
        "shadow", "arcane", "necrotic",
    };

    /// <summary>Dialogue line categories used by the NPC dialogue system.</summary>
    public static readonly IReadOnlyList<string> DialogueCategories = new[]
    {
        "greeting", "farewell", "smalltalk", "reaction", "mood_prefix", "memory",
    };

    /// <summary>
    /// Personality types the dialogue engine explicitly recognizes and uses for
    /// line lookup. Maps from the free-form NPC <see cref="NpcPersonalities"/>
    /// label via the dialogue database's internal PersonalityMapping table.
    /// </summary>
    public static readonly IReadOnlyList<string> DialoguePersonalityTypes = new[]
    {
        "aggressive", "noble", "cunning", "pious", "scholarly", "cynical",
        "charming", "stoic",
    };

    /// <summary>Emotion tags on dialogue lines. null/empty means any emotion.</summary>
    public static readonly IReadOnlyList<string> DialogueEmotions = new[]
    {
        "anger", "fear", "joy", "sadness", "confidence", "disgust", "surprise",
        "trust", "anticipation",
    };

    /// <summary>Context tags on dialogue lines. null/empty means any context.</summary>
    public static readonly IReadOnlyList<string> DialogueContexts = new[]
    {
        "low_hp", "rich", "is_king", "after_combat", "night", "day", "in_tavern",
        "in_dungeon", "player_wounded", "player_low_fame", "player_high_fame",
    };

    /// <summary>Memory types on reaction-style dialogue lines.</summary>
    public static readonly IReadOnlyList<string> DialogueMemoryTypes = new[]
    {
        "helped", "attacked", "betrayed", "saved", "spoken_to",
    };

    /// <summary>Event types on reaction-style dialogue lines.</summary>
    public static readonly IReadOnlyList<string> DialogueEventTypes = new[]
    {
        "combat_victory", "combat_defeat", "ally_death", "level_up",
        "became_king", "divorced", "got_married", "companion_recruited",
    };

    /// <summary>Nine core stats the gold-based stat-training system tracks counts for.</summary>
    public static readonly IReadOnlyList<string> CoreStatNames = new[]
    {
        "STR", "DEX", "CON", "INT", "WIS", "CHA", "DEF", "AGI", "STA",
    };

    /// <summary>Common combat skill names the proficiency system recognizes.</summary>
    public static readonly IReadOnlyList<string> CombatSkillNames = new[]
    {
        "sword", "axe", "mace", "dagger", "spear", "bow", "staff", "unarmed",
        "shield", "lockpicking", "stealth", "magic", "archery",
    };

    /// <summary>
    /// StoryRole tags that hook into the faction system. If an NPC is flagged
    /// with one of these, their faction is determined automatically (e.g.
    /// HighPriest → The Faith, ShadowAgent → The Shadows, FallenPaladin →
    /// The Crown). Anything else is treated as a regular citizen.
    /// </summary>
    public static readonly IReadOnlyList<string> StoryRoles = new[]
    {
        "FallenPaladin",
        "HighPriest",
        "Lysandra",
        "MaelkethChampion",
        "Mordecai",
        "OceanOracle",
        "SealScholar",
        "ShadowAgent",
        "Sylvana",
        "TheStranger",
    };
}
