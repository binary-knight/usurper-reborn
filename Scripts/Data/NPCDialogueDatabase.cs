using System;
using System.Collections.Generic;
using System.Linq;

namespace UsurperRemake.Data
{
    /// <summary>
    /// Pre-generated NPC dialogue database. Contains thousands of Opus-quality dialogue lines
    /// organized by personality type, relationship tier, emotion, and context.
    /// At runtime, the existing NPC AI brain determines context and this system selects
    /// the best-matching pre-generated line. Zero runtime LLM cost, instant lookup.
    /// </summary>
    public static class NPCDialogueDatabase
    {
        /// <summary>
        /// A single pre-generated dialogue line with metadata for contextual matching.
        /// </summary>
        public class DialogueLine
        {
            public string Id { get; set; } = "";
            public string Text { get; set; } = "";
            public string Category { get; set; } = "";         // greeting, farewell, smalltalk, reaction, mood_prefix, memory
            public string? NpcName { get; set; }               // null = generic, "Grok the Destroyer" = NPC-specific
            public string? PersonalityType { get; set; }       // aggressive, noble, cunning, pious, scholarly, cynical, charming, stoic
            public int RelationshipTier { get; set; }          // GameConfig.RelationMarried..RelationHate, 0 = any
            public string? Emotion { get; set; }               // anger, fear, joy, sadness, confidence, etc. null = any/neutral
            public string? Context { get; set; }               // low_hp, rich, is_king, after_combat, etc. null = any
            public string? MemoryType { get; set; }            // helped, attacked, betrayed, etc. null = any
            public string? EventType { get; set; }             // combat_victory, ally_death, etc. (for reactions)
        }

        // All pre-generated lines, loaded once at startup
        private static List<DialogueLine>? _allLines;

        // Per-NPC tracking of recently used line IDs to prevent repetition
        // Key = NPC name, Value = circular buffer of recently used IDs
        private static readonly Dictionary<string, List<string>> _recentlyUsed = new();
        private const int MaxRecentPerNpc = 20;

        // Personality type mapping from NPCTemplate.Personality strings
        private static readonly Dictionary<string, string> PersonalityMapping = new(StringComparer.OrdinalIgnoreCase)
        {
            // aggressive
            {"aggressive", "aggressive"}, {"fierce", "aggressive"}, {"brutal", "aggressive"},
            {"cruel", "aggressive"}, {"ruthless", "aggressive"}, {"merciless", "aggressive"},
            {"deadly", "aggressive"}, {"insane", "aggressive"}, {"tough", "aggressive"},
            {"gruff", "aggressive"}, {"wild", "aggressive"}, {"fanatical", "aggressive"},
            // noble
            {"honorable", "noble"}, {"noble", "noble"}, {"brave", "noble"},
            {"loyal", "noble"}, {"righteous", "noble"}, {"resolute", "noble"},
            {"radiant", "noble"}, {"chivalrous", "noble"}, {"proud", "noble"}, {"regal", "noble"},
            // cunning
            {"cunning", "cunning"}, {"mysterious", "cunning"}, {"sneaky", "cunning"},
            {"scheming", "cunning"}, {"sinister", "cunning"}, {"ambitious", "cunning"},
            {"cold", "cunning"}, {"secretive", "cunning"}, {"sly", "cunning"},
            {"clever", "cunning"}, {"shrewd", "cunning"},
            // pious
            {"pious", "pious"}, {"devout", "pious"}, {"compassionate", "pious"},
            {"kind", "pious"}, {"gentle", "pious"}, {"zealous", "pious"},
            {"peaceful", "pious"}, {"holy", "pious"},
            // scholarly
            {"wise", "scholarly"}, {"scholarly", "scholarly"}, {"curious", "scholarly"},
            {"eccentric", "scholarly"}, {"enigmatic", "scholarly"}, {"obsessed", "scholarly"},
            {"studious", "scholarly"}, {"thoughtful", "scholarly"}, {"observant", "scholarly"},
            // cynical
            {"stubborn", "cynical"}, {"stern", "cynical"}, {"strict", "cynical"},
            {"brooding", "cynical"}, {"tormented", "cynical"}, {"solitary", "cynical"},
            {"greedy", "cynical"}, {"bitter", "cynical"}, {"pessimistic", "cynical"},
            // charming
            {"charming", "charming"}, {"flashy", "charming"}, {"optimistic", "charming"},
            {"lucky", "charming"}, {"free-spirited", "charming"}, {"arrogant", "charming"},
            {"witty", "charming"}, {"flirtatious", "charming"}, {"roguish", "charming"},
            {"smooth", "charming"}, {"social", "charming"},
            // stoic
            {"silent", "stoic"}, {"professional", "stoic"}, {"disciplined", "stoic"},
            {"nervous", "stoic"}, {"cowardly", "stoic"}, {"serene", "stoic"},
            {"sharp", "stoic"}, {"calm", "stoic"}, {"quiet", "stoic"},
            {"reserved", "stoic"}, {"solemn", "stoic"},
        };

        /// <summary>
        /// Get the personality type for dialogue selection from an NPC's personality string.
        /// </summary>
        public static string GetPersonalityType(string? personality)
        {
            if (string.IsNullOrEmpty(personality)) return "stoic";
            return PersonalityMapping.TryGetValue(personality, out var type) ? type : "stoic";
        }

        /// <summary>
        /// Initialize the database by collecting lines from all dialogue source files.
        /// Called once at startup.
        /// </summary>
        public static void Initialize()
        {
            if (_allLines != null) return;

            _allLines = new List<DialogueLine>();

            // Collect from all dialogue source files
            _allLines.AddRange(DialogueLines_Greetings.GetLines());
            _allLines.AddRange(DialogueLines_SmallTalk.GetLines());
            _allLines.AddRange(DialogueLines_Farewells.GetLines());
            _allLines.AddRange(DialogueLines_Reactions.GetLines());
            _allLines.AddRange(DialogueLines_MoodPrefixes.GetLines());
            _allLines.AddRange(DialogueLines_Memory.GetLines());
            _allLines.AddRange(DialogueLines_StoryNPCs.GetLines());
        }

        /// <summary>
        /// Query the database for the best matching dialogue line.
        /// Returns null if no suitable match found (caller should fall back to template system).
        /// </summary>
        public static string? GetBestLine(string category, NPC npc, Player player, string? eventType = null)
        {
            Initialize();
            if (_allLines == null || _allLines.Count == 0) return null;

            string npcName = npc.Name2 ?? npc.Name1 ?? "Unknown";
            string personalityType = GetNpcPersonalityType(npc);
            int relationshipTier = GetRelationshipTier(npc, player);
            string? dominantEmotion = GetDominantEmotionString(npc);
            string? context = GetPlayerContext(player);
            string? memoryType = GetRecentMemoryType(npc, player);

            // Filter to matching category
            var candidates = _allLines.Where(l => l.Category == category).ToList();
            if (candidates.Count == 0) return null;

            // Score each candidate
            var scored = new List<(DialogueLine line, int score)>();
            var recentIds = GetRecentIds(npcName);

            foreach (var line in candidates)
            {
                int score = ScoreLine(line, npcName, personalityType, relationshipTier,
                    dominantEmotion, context, memoryType, eventType, recentIds);

                if (score > 0) // Must have at least some relevance
                    scored.Add((line, score));
            }

            if (scored.Count == 0) return null;

            // Pick from top candidates with slight randomness
            scored.Sort((a, b) => b.score.CompareTo(a.score));
            int topScore = scored[0].score;
            var topCandidates = scored.Where(s => s.score >= topScore - 2).ToList();

            var random = new Random();
            var chosen = topCandidates[random.Next(topCandidates.Count)].line;

            // Mark as recently used
            MarkUsed(npcName, chosen.Id);

            // Substitute placeholders
            return SubstitutePlaceholders(chosen.Text, npc, player);
        }

        /// <summary>
        /// Score a dialogue line based on how well it matches the current context.
        /// Higher score = better match.
        /// </summary>
        private static int ScoreLine(DialogueLine line, string npcName, string personalityType,
            int relationshipTier, string? emotion, string? context, string? memoryType,
            string? eventType, HashSet<string> recentIds)
        {
            int score = 0;

            // NPC-specific lines are the best match
            if (line.NpcName != null)
            {
                if (string.Equals(line.NpcName, npcName, StringComparison.OrdinalIgnoreCase))
                    score += 10;
                else
                    return 0; // NPC-specific line for a different NPC = no match
            }

            // Personality match
            if (line.PersonalityType != null)
            {
                if (line.PersonalityType == personalityType)
                    score += 5;
                else if (line.NpcName == null) // Generic lines with wrong personality = skip
                    return 0;
            }
            else
            {
                score += 1; // Generic (any personality) line = weak match
            }

            // Relationship tier
            if (line.RelationshipTier != 0)
            {
                if (line.RelationshipTier == relationshipTier)
                    score += 3;
                else if (Math.Abs(line.RelationshipTier - relationshipTier) <= 10) // Adjacent tier
                    score += 1;
                else
                    return 0; // Wrong relationship tier = no match for tier-specific lines
            }

            // Emotion match
            if (line.Emotion != null)
            {
                if (emotion != null && line.Emotion == emotion)
                    score += 3;
                else
                    score -= 1; // Emotion-specific line when NPC has different emotion = slight penalty
            }

            // Context match
            if (line.Context != null)
            {
                if (context != null && line.Context == context)
                    score += 2;
                else
                    score -= 1; // Context-specific line when context doesn't match = slight penalty
            }

            // Memory type match
            if (line.MemoryType != null)
            {
                if (memoryType != null && line.MemoryType == memoryType)
                    score += 2;
                else
                    return 0; // Memory-specific line when no matching memory = no match
            }

            // Event type match (for reactions)
            if (line.EventType != null)
            {
                if (eventType != null && line.EventType == eventType)
                    score += 3;
                else
                    return 0; // Wrong event type = no match
            }

            // Freshness bonus - big reward for lines not recently used
            if (!recentIds.Contains(line.Id))
                score += 4;

            return score;
        }

        /// <summary>
        /// Replace placeholders in dialogue text with actual values.
        /// </summary>
        private static string SubstitutePlaceholders(string text, NPC npc, Player player)
        {
            var result = text;
            result = result.Replace("{player_name}", player.Name2 ?? player.Name1 ?? "stranger");
            result = result.Replace("{player_class}", player.Class.ToString());
            result = result.Replace("{npc_name}", npc.Name2 ?? npc.Name1 ?? "someone");

            // Player title
            if (player.King)
                result = result.Replace("{player_title}", "Your Majesty");
            else
                result = result.Replace("{player_title}", "adventurer");

            // Time of day
            var hour = DateTime.Now.Hour;
            string timeOfDay = hour switch
            {
                < 6 => "night",
                < 12 => "morning",
                < 18 => "afternoon",
                _ => "evening"
            };
            result = result.Replace("{time_of_day}", timeOfDay);

            return result;
        }

        #region State Extraction Helpers

        private static string GetNpcPersonalityType(NPC npc)
        {
            // Try to get personality string from the NPC's PersonalityProfile
            if (npc.Personality != null && !string.IsNullOrEmpty(npc.Personality.Archetype))
            {
                var mapped = GetPersonalityType(npc.Personality.Archetype);
                if (mapped != "stoic") return mapped; // Found a specific match
            }

            // Try personality traits to infer type
            if (npc.Personality != null)
            {
                if (npc.Personality.Aggression > 0.7f) return "aggressive";
                if (npc.Personality.Loyalty > 0.7f && npc.Personality.Courage > 0.6f) return "noble";
                if (npc.Personality.Intelligence > 0.7f && npc.Personality.Caution > 0.6f) return "cunning";
                if (npc.Personality.Mysticism > 0.6f && npc.Personality.Patience > 0.6f) return "pious";
                if (npc.Personality.Intelligence > 0.7f) return "scholarly";
                if (npc.Personality.Sociability > 0.7f) return "charming";
                if (npc.Personality.Patience < 0.3f || npc.Personality.Greed > 0.7f) return "cynical";
            }

            return "stoic"; // Default fallback
        }

        private static int GetRelationshipTier(NPC npc, Player player)
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

        private static string? GetDominantEmotionString(NPC npc)
        {
            if (npc.EmotionalState == null) return null;

            var dominant = npc.EmotionalState.GetDominantEmotion();
            if (dominant == null) return null;

            return dominant.Value.ToString().ToLower(); // "anger", "joy", "fear", etc.
        }

        private static string? GetPlayerContext(Player player)
        {
            if (player == null) return null;
            // Return the most notable context about the player
            float hpPercent = player.MaxHP > 0 ? (float)player.HP / player.MaxHP : 1f;
            if (hpPercent < 0.25f) return "low_hp";

            if (player.King)
                return "is_king";

            if (player.Gold > 10000) return "rich";
            if (player.Gold < 50) return "poor";
            if (player.Level >= 50) return "high_level";
            if (player.Level <= 3) return "low_level";

            return null;
        }

        private static string? GetRecentMemoryType(NPC npc, Player player)
        {
            if (npc.Memory == null || player == null) return null;

            string playerName = player.Name2 ?? player.Name1 ?? "";
            var memories = npc.Memory.GetMemoriesAboutCharacter(playerName);
            if (memories == null || memories.Count == 0) return null;

            // Get the most recent significant memory
            var recent = memories
                .OrderByDescending(m => m.Importance)
                .FirstOrDefault();

            if (recent == null) return null;

            return recent.Type switch
            {
                MemoryType.Helped => "helped",
                MemoryType.Attacked => "attacked",
                MemoryType.Betrayed => "betrayed",
                MemoryType.Saved => "saved",
                MemoryType.Defended => "defended",
                MemoryType.Traded => "traded",
                MemoryType.Insulted => "insulted",
                MemoryType.Complimented => "complimented",
                MemoryType.SharedDrink => "shared_drink",
                MemoryType.SharedItem => "shared_item",
                _ => null
            };
        }

        #endregion

        #region Recently-Used Tracking

        private static HashSet<string> GetRecentIds(string npcName)
        {
            if (_recentlyUsed.TryGetValue(npcName, out var list))
                return new HashSet<string>(list);
            return new HashSet<string>();
        }

        private static void MarkUsed(string npcName, string lineId)
        {
            if (!_recentlyUsed.ContainsKey(npcName))
                _recentlyUsed[npcName] = new List<string>();

            var list = _recentlyUsed[npcName];
            list.Add(lineId);

            // Circular buffer - keep only the most recent
            while (list.Count > MaxRecentPerNpc)
                list.RemoveAt(0);
        }

        /// <summary>
        /// Get recently used dialogue IDs for an NPC (for serialization).
        /// </summary>
        public static List<string>? GetRecentlyUsedIds(string npcName)
        {
            return _recentlyUsed.TryGetValue(npcName, out var list) ? list : null;
        }

        /// <summary>
        /// Restore recently used dialogue IDs for an NPC (from deserialization).
        /// </summary>
        public static void RestoreRecentlyUsedIds(string npcName, List<string>? ids)
        {
            if (ids != null && ids.Count > 0)
                _recentlyUsed[npcName] = new List<string>(ids);
        }

        /// <summary>
        /// Clear all recently-used tracking (for new game).
        /// </summary>
        public static void ClearAllTracking()
        {
            _recentlyUsed.Clear();
        }

        #endregion
    }
}
