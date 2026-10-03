using System.Collections.Generic;
using static UsurperRemake.Data.NPCDialogueDatabase;

namespace UsurperRemake.Data
{
    /// <summary>
    /// Pre-generated mood prefix lines for shopkeeper greetings.
    /// These are narrative descriptions of the NPC's current state shown when entering shops.
    /// </summary>
    public static class DialogueLines_MoodPrefixes
    {
        public static List<DialogueLine> GetLines()
        {
            var lines = new List<DialogueLine>();

            // ═══════════════════════════════════════════════════════════════
            // Generic mood prefixes by emotion (any NPC)
            // ═══════════════════════════════════════════════════════════════

            // Joy
            lines.Add(new() { Id = "mp_joy1", Text = BuiltInText("mp_joy1"), Category = "mood_prefix", Emotion = "joy" });
            lines.Add(new() { Id = "mp_joy2", Text = BuiltInText("mp_joy2"), Category = "mood_prefix", Emotion = "joy" });
            lines.Add(new() { Id = "mp_joy3", Text = BuiltInText("mp_joy3"), Category = "mood_prefix", Emotion = "joy" });

            // Anger
            lines.Add(new() { Id = "mp_ang1", Text = BuiltInText("mp_ang1"), Category = "mood_prefix", Emotion = "anger" });
            lines.Add(new() { Id = "mp_ang2", Text = BuiltInText("mp_ang2"), Category = "mood_prefix", Emotion = "anger" });
            lines.Add(new() { Id = "mp_ang3", Text = BuiltInText("mp_ang3"), Category = "mood_prefix", Emotion = "anger" });

            // Sadness
            lines.Add(new() { Id = "mp_sad1", Text = BuiltInText("mp_sad1"), Category = "mood_prefix", Emotion = "sadness" });
            lines.Add(new() { Id = "mp_sad2", Text = BuiltInText("mp_sad2"), Category = "mood_prefix", Emotion = "sadness" });
            lines.Add(new() { Id = "mp_sad3", Text = BuiltInText("mp_sad3"), Category = "mood_prefix", Emotion = "sadness" });

            // Fear
            lines.Add(new() { Id = "mp_fear1", Text = BuiltInText("mp_fear1"), Category = "mood_prefix", Emotion = "fear" });
            lines.Add(new() { Id = "mp_fear2", Text = BuiltInText("mp_fear2"), Category = "mood_prefix", Emotion = "fear" });

            // Confidence
            lines.Add(new() { Id = "mp_con1", Text = BuiltInText("mp_con1"), Category = "mood_prefix", Emotion = "confidence" });
            lines.Add(new() { Id = "mp_con2", Text = BuiltInText("mp_con2"), Category = "mood_prefix", Emotion = "confidence" });

            // Gratitude (NPC remembers player helped them)
            lines.Add(new() { Id = "mp_grat1", Text = BuiltInText("mp_grat1"), Category = "mood_prefix", Emotion = "gratitude" });
            lines.Add(new() { Id = "mp_grat2", Text = BuiltInText("mp_grat2"), Category = "mood_prefix", Emotion = "gratitude" });

            // Greed
            lines.Add(new() { Id = "mp_greed1", Text = BuiltInText("mp_greed1"), Category = "mood_prefix", Emotion = "greed" });

            // Loneliness
            lines.Add(new() { Id = "mp_lone1", Text = BuiltInText("mp_lone1"), Category = "mood_prefix", Emotion = "loneliness" });

            // ═══════════════════════════════════════════════════════════════
            // Mood prefixes with relationship context
            // ═══════════════════════════════════════════════════════════════

            // Positive impression
            lines.Add(new() { Id = "mp_pos1", Text = BuiltInText("mp_pos1"), Category = "mood_prefix", Context = "positive_impression" });
            lines.Add(new() { Id = "mp_pos2", Text = BuiltInText("mp_pos2"), Category = "mood_prefix", Context = "positive_impression" });

            // Negative impression
            lines.Add(new() { Id = "mp_neg1", Text = BuiltInText("mp_neg1"), Category = "mood_prefix", Context = "negative_impression" });
            lines.Add(new() { Id = "mp_neg2", Text = BuiltInText("mp_neg2"), Category = "mood_prefix", Context = "negative_impression" });

            // Player is king
            lines.Add(new() { Id = "mp_king1", Text = BuiltInText("mp_king1"), Category = "mood_prefix", Context = "is_king" });

            // Player is wounded
            lines.Add(new() { Id = "mp_wound1", Text = BuiltInText("mp_wound1"), Category = "mood_prefix", Context = "low_hp" });

            // Neutral / no strong mood
            lines.Add(new() { Id = "mp_neutral1", Text = BuiltInText("mp_neutral1"), Category = "mood_prefix" });
            lines.Add(new() { Id = "mp_neutral2", Text = BuiltInText("mp_neutral2"), Category = "mood_prefix" });
            lines.Add(new() { Id = "mp_neutral3", Text = BuiltInText("mp_neutral3"), Category = "mood_prefix" });

            return lines;
        }
    }
}
