using System;
using System.Collections.Generic;
using System.Linq;
using UsurperRemake.Data;
using UsurperRemake.Systems;
using UsurperRemake.UI;

/// <summary>
/// Dynamic NPC Dialogue Generator
/// Creates unique, personality-driven dialogue based on:
/// - Relationship level with player
/// - NPC personality traits
/// - NPC archetype/profession
/// - Memory of past interactions
/// - Current emotional state
/// - Context (player HP, wealth, level, etc.)
///
/// Now queries the pre-generated NPCDialogueDatabase first for higher-quality lines,
/// falling back to the original template system if no suitable match is found.
///
/// v1.2.5: every line is a Loc key (npc_gen.*). A line is picked first, as a list of keys, with the
/// same random draws in every language; it is written in the current language only when shown, and
/// every piece of it in that one language. Pieces that go into a sentence (a title, a personality
/// prefix or suffix, a topic, a time) are whole-sentence templates, so each language orders its own
/// sentence. Nothing here is stored: the dialogue database keeps only line ids (RecentDialogueIds).
/// </summary>
public static class NPCDialogueGenerator
{
    /// <summary>v1.2.5: the random source of every pick; tests seed it.</summary>
    internal static Random Rng = Random.Shared;

    private const string K = "npc_gen.";

    #region Tables (counts of keyed lines; the text is in Localization/*.json)

    /// <summary>Relationship tiers in ascending order, with the key group of their greetings (5 each).</summary>
    private static readonly (int Tier, string Group)[] GreetingTiers =
    {
        (GameConfig.RelationMarried, "married"),
        (GameConfig.RelationLove, "love"),
        (GameConfig.RelationPassion, "passion"),
        (GameConfig.RelationFriendship, "friendship"),
        (GameConfig.RelationTrust, "trust"),
        (GameConfig.RelationRespect, "respect"),
        (GameConfig.RelationNormal, "normal"),
        (GameConfig.RelationSuspicious, "suspicious"),
        (GameConfig.RelationAnger, "anger"),
        (GameConfig.RelationEnemy, "enemy"),
        (GameConfig.RelationHate, "hate"),
    };
    private const int GreetingsPerTier = 5;

    /// <summary>Archetype vocabularies: how many titles, phrases and topics each has (npc_gen.title/phrase/topic.{archetype}.{n}).</summary>
    private static readonly Dictionary<string, (int Titles, int Phrases, int Topics)> ArchetypeVocabularies = new()
    {
        ["guard"] = (4, 5, 4),
        ["merchant"] = (4, 5, 5),
        ["thief"] = (3, 5, 5),
        ["assassin"] = (2, 5, 4),
        ["priest"] = (4, 5, 5),
        ["noble"] = (4, 5, 5),
        ["thug"] = (3, 5, 4),
        ["citizen"] = (3, 5, 4),
        ["mystic"] = (3, 5, 4),
    };

    /// <summary>Memory references, 3 per memory type (npc_gen.memory.{type}.{n}, {0} is the time).</summary>
    private static readonly MemoryType[] MemoryReferences =
    {
        MemoryType.Helped, MemoryType.Attacked, MemoryType.Betrayed, MemoryType.Traded, MemoryType.SharedDrink,
        MemoryType.Defended, MemoryType.Saved, MemoryType.Insulted, MemoryType.Complimented, MemoryType.SharedItem,
    };
    private const int ReferencesPerMemory = 3;

    /// <summary>Context comments (npc_gen.context.{group}.{n}). has_companion, diseased and powerful_weapon are never chosen (as before).</summary>
    private static readonly Dictionary<string, int> ContextComments = new()
    {
        ["low_hp"] = 4, ["high_level"] = 4, ["low_level"] = 4, ["rich"] = 4, ["poor"] = 4, ["is_king"] = 4,
        ["has_companion"] = 4, ["diseased"] = 4, ["powerful_weapon"] = 4,
        ["morning"] = 3, ["evening"] = 3, ["night"] = 3,
    };

    /// <summary>Emotional indicators (npc_gen.emote.{emotion}.{n}).</summary>
    private static readonly Dictionary<EmotionType, int> EmotionalIndicators = new()
    {
        [EmotionType.Joy] = 4, [EmotionType.Sadness] = 4, [EmotionType.Anger] = 4, [EmotionType.Fear] = 4,
        [EmotionType.Confidence] = 4, [EmotionType.Loneliness] = 3, [EmotionType.Hope] = 3, [EmotionType.Peace] = 3,
    };

    /// <summary>
    /// Personality modifiers, 3 per trait (npc_gen.mod.{trait}.{n}). Each is a template around the line:
    /// {0} the whole line, {1} the line without its closing punctuation, {2} that punctuation.
    /// </summary>
    private static readonly string[] PersonalityModifiers =
    {
        "high_aggression", "low_aggression", "high_intelligence", "high_greed", "high_romanticism",
        "high_humor", "high_loyalty", "high_bravery", "high_deceitfulness",
    };
    private const int ModifiersPerTrait = 3;

    /// <summary>Farewells by relationship, 4 each (npc_gen.farewell.{group}.{n}).</summary>
    private const int FarewellsPerTier = 4;

    /// <summary>Archetypes that add a farewell sentence (npc_gen.farewell_add.{archetype}).</summary>
    private static readonly string[] FarewellAdditions = { "guard", "priest", "merchant", "thief", "noble" };

    private const int GenericSmallTalk = 6;
    private const int TopicTemplates = 5;

    #endregion

    /// <summary>v1.2.5: every key this generator can write (tests check each in five languages).</summary>
    internal static IEnumerable<string> AllKeys()
    {
        foreach (var (_, group) in GreetingTiers)
            for (int i = 1; i <= GreetingsPerTier; i++) yield return $"{K}greet.{group}.{i}";
        foreach (var (arch, v) in ArchetypeVocabularies)
        {
            for (int i = 1; i <= v.Titles; i++) yield return $"{K}title.{arch}.{i}";
            for (int i = 1; i <= v.Phrases; i++) yield return $"{K}phrase.{arch}.{i}";
            for (int i = 1; i <= v.Topics; i++) yield return $"{K}topic.{arch}.{i}";
        }
        foreach (var m in MemoryReferences)
            for (int i = 1; i <= ReferencesPerMemory; i++) yield return $"{K}memory.{m.ToString().ToLower()}.{i}";
        foreach (var t in new[] { "yesterday", "other_day", "recently", "some_time", "while_back", "long_ago" })
            yield return K + "time." + t;
        foreach (var (group, n) in ContextComments)
            for (int i = 1; i <= n; i++) yield return $"{K}context.{group}.{i}";
        foreach (var (emotion, n) in EmotionalIndicators)
            for (int i = 1; i <= n; i++) yield return $"{K}emote.{emotion.ToString().ToLower()}.{i}";
        foreach (var trait in PersonalityModifiers)
            for (int i = 1; i <= ModifiersPerTrait; i++) yield return $"{K}mod.{trait}.{i}";
        foreach (var group in new[] { "married", "love", "friendship", "normal", "anger", "hate" })
            for (int i = 1; i <= FarewellsPerTier; i++) yield return $"{K}farewell.{group}.{i}";
        foreach (var arch in FarewellAdditions) yield return K + "farewell_add." + arch;
        for (int i = 1; i <= GenericSmallTalk; i++) yield return $"{K}smalltalk.{i}";
        for (int i = 1; i <= TopicTemplates; i++) yield return $"{K}topic_line.{i}";
        foreach (var k in new[] { "with_title", "join", "join_emote", "smalltalk_none", "react_none", "react_other" })
            yield return K + k;
        foreach (var (kind, kinds) in new (string, string[])[]
        {
            ("defeat", new[] { "aggressive", "social", "plain" }),
            ("gift", new[] { "love", "friend", "greedy", "plain" }),
            ("insult", new[] { "aggressive", "vengeful", "timid", "plain" }),
            ("compliment", new[] { "romantic", "brave", "social", "plain" }),
            ("threat", new[] { "brave", "aggressive", "timid", "plain" }),
            ("flee", new[] { "aggressive", "brave", "loyal", "timid", "plain" }),
            ("ally_death", new[] { "aggressive", "social", "loyal", "brave", "romantic", "plain" }),
        })
            foreach (var k in kinds) yield return $"{K}rx.{kind}.{k}";
    }

    #region Lines picked now, written later

    /// <summary>
    /// v1.2.5: a generated line: the keys picked, and how to write them. Render writes it in the current
    /// language; every piece comes from that language.
    /// </summary>
    public sealed class NpcLine
    {
        private readonly Func<string> _render;

        /// <summary>The keys picked, in order (the same in every language).</summary>
        public IReadOnlyList<string> Keys { get; }

        internal NpcLine(Func<string> render, List<string> keys)
        {
            _render = render;
            Keys = keys;
        }

        public string Render() => _render();
    }

    private static NpcLine Fixed(string key) => new(() => Loc.Get(key), new List<string> { key });

    private static NpcLine FromDatabase(NPCDialogueDatabase.DialogueLine line, NPC npc, Player player)
        => new(() => NPCDialogueDatabase.RenderLine(line, npc, player), new List<string> { "npc_dialogue." + line.Id });

    #endregion

    #region Main Generation Methods

    /// <summary>
    /// Generate a context-aware greeting for the player
    /// </summary>
    public static string GenerateGreeting(NPC npc, Player player) => PickGreeting(npc, player).Render();

    /// <summary>v1.2.5: the greeting picked, to be written in the reader's language.</summary>
    public static NpcLine PickGreeting(NPC npc, Player player)
    {
        if (npc == null || player == null) return Fixed(K + "greet.normal.3");

        // Try pre-generated dialogue database first
        var dbLine = NPCDialogueDatabase.PickLine("greeting", npc, player, null, Rng);
        if (dbLine != null) return FromDatabase(dbLine, npc, player);

        // Fall back to template system
        var keys = new List<string>();
        int relationshipLevel = GetRelationshipLevel(npc, player);

        // Get base greeting
        string baseKey = GetBaseGreetingKey(relationshipLevel);
        keys.Add(baseKey);
        Func<string> greeting = () => Loc.Get(baseKey, player.ClassName, PlayerTitle(player));

        // Apply archetype vocabulary (30% chance to modify)
        if (Rng.NextDouble() < 0.30)
        {
            greeting = ApplyArchetypeStyle(greeting, npc.Archetype, keys);
        }

        // Apply personality modifiers (40% chance)
        if (npc.Personality != null && Rng.NextDouble() < 0.40)
        {
            greeting = ApplyPersonalityModifiers(greeting, npc.Personality, keys);
        }

        // Add memory reference (20% chance)
        if (npc.Memory != null && Rng.NextDouble() < 0.20)
        {
            var memory = GetMemoryReference(npc.Memory, player);
            if (memory != null)
            {
                var (refKey, timeKey) = memory.Value;
                keys.Add(refKey);
                keys.Add(timeKey);
                var inner = greeting;
                greeting = () => Join(K + "join", inner(), Loc.Get(refKey, Loc.Get(timeKey)));
            }
        }

        // Add context comment (30% chance)
        if (Rng.NextDouble() < 0.30)
        {
            var contextKey = GetContextCommentKey(player);
            if (contextKey != null)
            {
                keys.Add(contextKey);
                var inner = greeting;
                greeting = () => Join(K + "join", inner(), Loc.Get(contextKey, player.ClassName, PlayerTitle(player)));
            }
        }

        // Add emotional indicator (25% chance)
        if (npc.EmotionalState != null && Rng.NextDouble() < 0.25)
        {
            greeting = AddEmotionalIndicator(greeting, npc.EmotionalState, keys);
        }

        return new NpcLine(greeting, keys);
    }

    /// <summary>
    /// Generate a farewell for the player
    /// </summary>
    public static string GenerateFarewell(NPC npc, Player player) => PickFarewell(npc, player).Render();

    /// <summary>v1.2.5: the farewell picked, to be written in the reader's language.</summary>
    public static NpcLine PickFarewell(NPC npc, Player player)
    {
        if (npc == null || player == null) return Fixed(K + "farewell.normal.1");

        // Try pre-generated dialogue database first
        var dbLine = NPCDialogueDatabase.PickLine("farewell", npc, player, null, Rng);
        if (dbLine != null) return FromDatabase(dbLine, npc, player);

        // Fall back to template system
        var keys = new List<string>();
        int relationshipLevel = GetRelationshipLevel(npc, player);
        string baseKey = GetBaseFarewellKey(relationshipLevel);
        keys.Add(baseKey);
        Func<string> farewell = () => Loc.Get(baseKey);

        // Apply archetype flavor
        string arch = npc.Archetype?.ToLower() ?? "";
        if (FarewellAdditions.Contains(arch) && Rng.NextDouble() < 0.5)
        {
            string addKey = K + "farewell_add." + arch;
            keys.Add(addKey);
            var inner = farewell;
            farewell = () => Join(K + "join", inner(), Loc.Get(addKey));
        }

        // Add emotional indicator
        if (npc.EmotionalState != null && Rng.NextDouble() < 0.25)
        {
            farewell = AddEmotionalIndicator(farewell, npc.EmotionalState, keys);
        }

        return new NpcLine(farewell, keys);
    }

    /// <summary>
    /// Generate small talk/conversation topic
    /// </summary>
    public static string GenerateSmallTalk(NPC npc, Player player) => PickSmallTalk(npc, player).Render();

    /// <summary>v1.2.5: the small talk picked, to be written in the reader's language.</summary>
    public static NpcLine PickSmallTalk(NPC npc, Player player)
    {
        if (npc == null) return Fixed(K + "smalltalk_none");

        // Try pre-generated dialogue database first
        var dbLine = NPCDialogueDatabase.PickLine("smalltalk", npc, player, null, Rng);
        if (dbLine != null) return FromDatabase(dbLine, npc, player);

        // Get archetype-appropriate topics
        string arch = npc.Archetype?.ToLower() ?? "";
        if (ArchetypeVocabularies.TryGetValue(arch, out var vocab) && vocab.Topics > 0)
        {
            string topicKey = $"{K}topic.{arch}.{Rng.Next(vocab.Topics) + 1}";
            string templateKey = $"{K}topic_line.{Rng.Next(TopicTemplates) + 1}";
            return new NpcLine(() => CapitalizeFirst(Loc.Get(templateKey, Loc.Get(topicKey))),
                new List<string> { topicKey, templateKey });
        }

        // Fallback generic small talk
        return Fixed($"{K}smalltalk.{Rng.Next(GenericSmallTalk) + 1}");
    }

    /// <summary>
    /// Generate a reaction to an event
    /// </summary>
    public static string GenerateReaction(NPC npc, Player player, string eventType)
        => ReactionInLanguage(npc, player, eventType)();

    /// <summary>
    /// v1.2.4: the reaction is picked once; the returned function writes it in the current language, so
    /// each group member can read the same reaction in their own language.
    /// </summary>
    public static Func<string> ReactionInLanguage(NPC npc, Player player, string eventType)
    {
        if (npc?.Personality == null) return () => Loc.Get(K + "react_none");

        // Try pre-generated dialogue database first
        var line = NPCDialogueDatabase.PickLine("reaction", npc, player, eventType, Rng);
        if (line != null) return () => NPCDialogueDatabase.RenderLine(line, npc, player);

        // v1.2.4: the victory fallback is a key, written in each reader's language
        // v1.2.5: so are the other fallbacks
        string key = FallbackReactionKey(npc, player, eventType);
        return () => Loc.Get(key);
    }

    /// <summary>v1.2.5: the key of the reaction used when no dialogue line fits.</summary>
    internal static string FallbackReactionKey(NPC npc, Player player, string eventType)
    {
        var p = npc.Personality;
        return eventType.ToLower() switch
        {
            "combat_victory" => CombatVictoryFallbackKey(p),
            "combat_defeat" => K + "rx.defeat." + CombatDefeatReaction(p),
            "combat_flee" => K + "rx.flee." + FleeReaction(p),
            "ally_death" => K + "rx.ally_death." + AllyDeathReaction(p),
            "gift_received" => K + "rx.gift." + GiftReaction(p, GetRelationshipLevel(npc, player)),
            "insult" => K + "rx.insult." + InsultReaction(p),
            "compliment" => K + "rx.compliment." + ComplimentReaction(p),
            "threat" => K + "rx.threat." + ThreatReaction(p),
            _ => K + "react_other"
        };
    }

    #endregion

    #region Display

    /// <summary>
    /// v1.2.5: spoken text as rows of at most 79 columns: two spaces and an opening quote, the text
    /// wrapped under itself, the closing quote after the last word.
    /// </summary>
    public static List<string> QuotedRows(string text)
    {
        var rows = UIHelper.WordWrap(text, 75);
        var result = new List<string>();
        for (int i = 0; i < rows.Count; i++)
        {
            string row = (i == 0 ? "  \"" : "   ") + rows[i];
            if (i == rows.Count - 1) row += "\"";
            result.Add(row);
        }
        return result;
    }

    /// <summary>v1.2.5: a narrated line (a shopkeeper's mood) as rows of at most 79 columns.</summary>
    public static List<string> NarrationRows(string text) => UIHelper.WordWrap(text, 79);

    #endregion

    #region Helper Methods

    private static int GetRelationshipLevel(NPC npc, Player player)
    {
        try
        {
            return RelationshipSystem.GetRelationshipStatus(npc, player);
        }
        catch
        {
            return GameConfig.RelationNormal;
        }
    }

    private static string GetBaseGreetingKey(int relationLevel)
    {
        // Find the closest relationship tier
        string group = "normal";
        foreach (var (tier, name) in GreetingTiers)
        {
            group = name;
            if (relationLevel <= tier) break;
        }
        return $"{K}greet.{group}.{Rng.Next(GreetingsPerTier) + 1}";
    }

    private static string GetBaseFarewellKey(int relationLevel)
    {
        string group = relationLevel switch
        {
            <= GameConfig.RelationMarried => "married",
            <= GameConfig.RelationLove => "love",
            <= GameConfig.RelationFriendship => "friendship",
            <= GameConfig.RelationNormal => "normal",
            <= GameConfig.RelationAnger => "anger",
            _ => "hate"
        };
        return $"{K}farewell.{group}.{Rng.Next(FarewellsPerTier) + 1}";
    }

    /// <summary>The player's title as an NPC addresses them: Your Majesty for a ruler, else their class.</summary>
    private static string PlayerTitle(Player player)
        => player.King ? Loc.Get("npc_dialogue.ph.majesty") : player.ClassName;

    private static Func<string> ApplyArchetypeStyle(Func<string> greeting, string archetype, List<string> keys)
    {
        string arch = archetype?.ToLower() ?? "";
        if (!ArchetypeVocabularies.TryGetValue(arch, out var vocab)) return greeting;

        // Sometimes use archetype-specific title
        if (Rng.NextDouble() < 0.5 && vocab.Titles > 0)
        {
            string titleKey = $"{K}title.{arch}.{Rng.Next(vocab.Titles) + 1}";
            keys.Add(titleKey);
            var inner = greeting;
            greeting = () => Wrap(K + "with_title", inner(), Loc.Get(titleKey));
        }

        // Sometimes add archetype phrase
        if (Rng.NextDouble() < 0.3 && vocab.Phrases > 0)
        {
            string phraseKey = $"{K}phrase.{arch}.{Rng.Next(vocab.Phrases) + 1}";
            keys.Add(phraseKey);
            var inner = greeting;
            greeting = () => Join(K + "join", inner(), Loc.Get(phraseKey));
        }

        return greeting;
    }

    private static Func<string> ApplyPersonalityModifiers(Func<string> greeting, PersonalityProfile personality, List<string> keys)
    {
        // Select modifier based on dominant personality trait
        string modifierKey = null;

        if (personality.Aggression > 0.7f) modifierKey = "high_aggression";
        else if (personality.Aggression < 0.3f) modifierKey = "low_aggression";
        else if (personality.Intelligence > 0.7f) modifierKey = "high_intelligence";
        else if (personality.Greed > 0.7f) modifierKey = "high_greed";
        else if (personality.Romanticism > 0.7f) modifierKey = "high_romanticism";
        else if (personality.Sociability > 0.7f) modifierKey = "high_humor";
        else if (personality.Loyalty > 0.7f) modifierKey = "high_loyalty";
        else if (personality.Courage > 0.7f) modifierKey = "high_bravery";
        else if (personality.Trustworthiness < 0.3f) modifierKey = "high_deceitfulness";

        if (modifierKey == null) return greeting;

        string key = $"{K}mod.{modifierKey}.{Rng.Next(ModifiersPerTrait) + 1}";
        keys.Add(key);
        var inner = greeting;
        return () => Wrap(key, inner());
    }

    /// <summary>The memory reference key and the time key, or null when there is no memory of the player.</summary>
    private static (string RefKey, string TimeKey)? GetMemoryReference(MemorySystem memory, Player player)
    {
        if (memory == null || player == null) return null;

        var playerName = player.Name2 ?? player.Name1;
        if (string.IsNullOrEmpty(playerName)) return null;

        // Get recent memories about this player
        var memories = memory.GetMemoriesAboutCharacter(playerName)
            .Where(m => MemoryReferences.Contains(m.Type))
            .OrderByDescending(m => m.Importance)
            .Take(3)
            .ToList();

        if (!memories.Any()) return null;

        var selectedMemory = memories[Rng.Next(memories.Count)];
        string refKey = $"{K}memory.{selectedMemory.Type.ToString().ToLower()}.{Rng.Next(ReferencesPerMemory) + 1}";

        // Determine time reference
        var age = selectedMemory.GetAge();
        string time = age.TotalDays switch
        {
            < 1 => "yesterday",
            < 3 => "other_day",
            < 10 => "recently",
            < 30 => "some_time",
            < 60 => "while_back",
            _ => "long_ago"
        };

        return (refKey, K + "time." + time);
    }

    private static string GetContextCommentKey(Player player)
    {
        if (player == null) return null;

        // Check various player conditions
        var possibleComments = new List<string>();
        void AddGroup(string group)
        {
            for (int i = 1; i <= ContextComments[group]; i++)
                possibleComments.Add($"{K}context.{group}.{i}");
        }

        // Low HP
        if (player.HP < player.MaxHP * 0.25f) AddGroup("low_hp");

        // High level (50+)
        if (player.Level >= 50) AddGroup("high_level");
        // Low level (<5)
        else if (player.Level < 5) AddGroup("low_level");

        // Rich (>10000 gold)
        if (player.Gold > 10000) AddGroup("rich");
        // Poor (<100 gold)
        else if (player.Gold < 100) AddGroup("poor");

        // Is King/Queen
        if (player.King) AddGroup("is_king");

        // Check time of day
        var hour = NPCDialogueDatabase.Hour();
        if (hour >= 5 && hour < 9) AddGroup("morning");
        else if (hour >= 18 && hour < 22) AddGroup("evening");
        else if (hour >= 22 || hour < 5) AddGroup("night");

        if (!possibleComments.Any()) return null;

        return possibleComments[Rng.Next(possibleComments.Count)];
    }

    private static Func<string> AddEmotionalIndicator(Func<string> line, EmotionalState emotionalState, List<string> keys)
    {
        if (emotionalState == null) return line;

        // Get current dominant emotion
        var activeEmotions = emotionalState.GetActiveEmotions();
        if (activeEmotions == null || !activeEmotions.Any()) return line;

        // Find strongest emotion (activeEmotions is Dictionary<EmotionType, Emotion>)
        var strongestPair = activeEmotions
            .OrderByDescending(e => e.Value.Intensity)
            .FirstOrDefault();

        if (strongestPair.Value == null || strongestPair.Value.Intensity < 0.3f) return line;

        if (!EmotionalIndicators.TryGetValue(strongestPair.Key, out int count)) return line;

        string key = $"{K}emote.{strongestPair.Key.ToString().ToLower()}.{Rng.Next(count) + 1}";
        keys.Add(key);
        return () => Join(K + "join_emote", Loc.Get(key), line());
    }

    #endregion

    #region Joining (v1.2.5)

    /// <summary>Two pieces in the current language, by a join template ({0} {1}).</summary>
    internal static string Join(string joinKey, string first, string second)
        => Format(Loc.Get(joinKey), first, second);

    /// <summary>
    /// A line inside a template of the current language: {0} the line, {1} the line without its closing
    /// punctuation, {2} that punctuation, {3} an extra piece (a title). When the template puts words
    /// ending in a comma before the line, the line goes on in lower case (the joined sentence has one
    /// capital, at its start).
    /// </summary>
    internal static string Wrap(string templateKey, string line, string extra = "")
    {
        string template = Loc.Get(templateKey);
        var (body, punct) = SplitClosingPunctuation(line);
        int at = template.IndexOf("{0}", StringComparison.Ordinal);
        string whole = at > 0 && template.Substring(0, at).TrimEnd().EndsWith(",") ? LowerFirst(line) : line;
        return Format(template, whole, body, punct, extra);
    }

    private static string Format(string template, params object[] args)
    {
        try { return string.Format(template, args); }
        catch (FormatException) { return template; }
    }

    /// <summary>The line without its closing . ! ? or ..., and that punctuation (with a French space before it).</summary>
    internal static (string Body, string Punct) SplitClosingPunctuation(string line)
    {
        int end = line.Length;
        while (end > 0 && ".!?\u2026".IndexOf(line[end - 1]) >= 0) end--;
        if (end == line.Length) return (line, "");
        int start = end;
        while (start > 0 && (line[start - 1] == ' ' || line[start - 1] == '\u00A0' || line[start - 1] == '\u202F')) start--;
        return (line.Substring(0, start), line.Substring(start));
    }

    /// <summary>
    /// The first letter in lower case, after any opening marks (¡ ¿ * "). English keeps "I" and its
    /// contractions, and a word in capitals.
    /// </summary>
    internal static string LowerFirst(string line)
    {
        int i = FirstLetter(line);
        if (i < 0) return line;
        int end = i;
        while (end < line.Length && (char.IsLetter(line[end]) || line[end] == '\'')) end++;
        string word = line.Substring(i, end - i);
        if (word.Length > 1 && word.All(c => !char.IsLetter(c) || char.IsUpper(c))) return line;
        if (GameConfig.Language == "en" && (word == "I" || word.StartsWith("I'"))) return line;
        return line.Substring(0, i) + char.ToLower(line[i]) + line.Substring(i + 1);
    }

    /// <summary>The first letter in upper case, after any opening marks.</summary>
    internal static string CapitalizeFirst(string line)
    {
        int i = FirstLetter(line);
        return i < 0 ? line : line.Substring(0, i) + char.ToUpper(line[i]) + line.Substring(i + 1);
    }

    private static int FirstLetter(string line)
    {
        for (int i = 0; i < line.Length; i++)
        {
            if (char.IsLetter(line[i])) return i;
            if ("\u00A1\u00BF*\"'(\u00AB \u201E\u201C".IndexOf(line[i]) < 0) return -1;
        }
        return -1;
    }

    #endregion

    #region Reactions

    /// <summary>v1.2.4: the victory reaction used when no dialogue line fits, as a Loc key.</summary>
    internal static string CombatVictoryFallbackKey(PersonalityProfile personality)
    {
        if (personality.Aggression > 0.7f)
            return "npc_dialogue.rx_cv_fb_aggressive";
        if (personality.Courage > 0.7f)
            return "npc_dialogue.rx_cv_fb_brave";
        if (personality.Sociability > 0.7f)
            return "npc_dialogue.rx_cv_fb_social";
        return "npc_dialogue.rx_cv_fb_plain";
    }

    private static string CombatDefeatReaction(PersonalityProfile personality)
    {
        if (personality.Aggression > 0.7f) return "aggressive";
        if (personality.Sociability > 0.7f) return "social";
        return "plain";
    }

    private static string GiftReaction(PersonalityProfile personality, int relationship)
    {
        if (relationship <= GameConfig.RelationLove) return "love";
        if (relationship <= GameConfig.RelationFriendship) return "friend";
        if (personality.Greed > 0.7f) return "greedy";
        return "plain";
    }

    private static string InsultReaction(PersonalityProfile personality)
    {
        if (personality.Aggression > 0.7f) return "aggressive";
        if (personality.Vengefulness > 0.7f) return "vengeful";
        if (personality.Courage < 0.3f) return "timid";
        return "plain";
    }

    private static string ComplimentReaction(PersonalityProfile personality)
    {
        if (personality.Romanticism > 0.7f) return "romantic";
        if (personality.Courage > 0.7f) return "brave";
        if (personality.Sociability > 0.7f) return "social";
        return "plain";
    }

    private static string ThreatReaction(PersonalityProfile personality)
    {
        if (personality.Courage > 0.7f) return "brave";
        if (personality.Aggression > 0.7f) return "aggressive";
        if (personality.Courage < 0.3f) return "timid";
        return "plain";
    }

    private static string FleeReaction(PersonalityProfile personality)
    {
        if (personality.Aggression > 0.7f) return "aggressive";
        if (personality.Courage > 0.7f) return "brave";
        if (personality.Loyalty > 0.7f) return "loyal";
        if (personality.Courage < 0.3f) return "timid";
        return "plain";
    }

    private static string AllyDeathReaction(PersonalityProfile personality)
    {
        if (personality.Aggression > 0.7f) return "aggressive";
        if (personality.Sociability > 0.7f) return "social";
        if (personality.Loyalty > 0.7f) return "loyal";
        if (personality.Courage > 0.7f) return "brave";
        if (personality.Romanticism > 0.7f) return "romantic";
        return "plain";
    }

    #endregion
}
