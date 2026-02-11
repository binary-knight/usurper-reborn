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
            lines.Add(new() { Id = "mp_joy1", Text = "{npc_name} is humming a tune, practically glowing. \"Welcome! What a wonderful day!\"", Category = "mood_prefix", Emotion = "joy" });
            lines.Add(new() { Id = "mp_joy2", Text = "{npc_name} looks up with a warm smile. \"Ah, a customer! Perfect timing — I'm in a generous mood.\"", Category = "mood_prefix", Emotion = "joy" });
            lines.Add(new() { Id = "mp_joy3", Text = "{npc_name} is whistling cheerfully as they arrange their wares. \"Come in, come in! Everything's better today.\"", Category = "mood_prefix", Emotion = "joy" });

            // Anger
            lines.Add(new() { Id = "mp_ang1", Text = "{npc_name} is muttering furiously under their breath. \"WHAT? Oh. A customer. Fine. What do you want?\"", Category = "mood_prefix", Emotion = "anger" });
            lines.Add(new() { Id = "mp_ang2", Text = "{npc_name} slams something down on the counter. \"Don't test me today. Just tell me what you need.\"", Category = "mood_prefix", Emotion = "anger" });
            lines.Add(new() { Id = "mp_ang3", Text = "{npc_name}'s jaw is tight, eyes sharp. \"If you're here to waste my time, turn around now.\"", Category = "mood_prefix", Emotion = "anger" });

            // Sadness
            lines.Add(new() { Id = "mp_sad1", Text = "{npc_name} sits behind the counter looking distant. \"Oh... hello. Sorry, I was miles away. What can I do for you?\"", Category = "mood_prefix", Emotion = "sadness" });
            lines.Add(new() { Id = "mp_sad2", Text = "{npc_name} barely looks up. Eyes red-rimmed. \"...Yes? What do you need?\"", Category = "mood_prefix", Emotion = "sadness" });
            lines.Add(new() { Id = "mp_sad3", Text = "{npc_name} manages a tired smile. \"Welcome. Forgive me — it's been a hard day.\"", Category = "mood_prefix", Emotion = "sadness" });

            // Fear
            lines.Add(new() { Id = "mp_fear1", Text = "{npc_name} glances at the door nervously. \"Quick, what do you need? I don't want to be alone for long.\"", Category = "mood_prefix", Emotion = "fear" });
            lines.Add(new() { Id = "mp_fear2", Text = "{npc_name} startles when you enter. \"Oh! You scared me. Sorry — I've been jumpy all day.\"", Category = "mood_prefix", Emotion = "fear" });

            // Confidence
            lines.Add(new() { Id = "mp_con1", Text = "{npc_name} stands tall behind the counter, looking pleased with themselves. \"Welcome! You've come to the right place.\"", Category = "mood_prefix", Emotion = "confidence" });
            lines.Add(new() { Id = "mp_con2", Text = "{npc_name} greets you with an assured nod. \"I have exactly what you need. I always do.\"", Category = "mood_prefix", Emotion = "confidence" });

            // Gratitude (NPC remembers player helped them)
            lines.Add(new() { Id = "mp_grat1", Text = "{npc_name} brightens immediately. \"Oh, it's you! After what you did for me, you get the VIP treatment.\"", Category = "mood_prefix", Emotion = "gratitude" });
            lines.Add(new() { Id = "mp_grat2", Text = "{npc_name} waves you over warmly. \"My favorite customer! I never forget a kindness.\"", Category = "mood_prefix", Emotion = "gratitude" });

            // Greed
            lines.Add(new() { Id = "mp_greed1", Text = "{npc_name}'s eyes light up when they see your coin purse. \"Well, well! A customer with means! Let me show you the premium selection.\"", Category = "mood_prefix", Emotion = "greed" });

            // Loneliness
            lines.Add(new() { Id = "mp_lone1", Text = "{npc_name} seems relieved to see anyone at all. \"Oh thank goodness — another person. It's been dead in here all day.\"", Category = "mood_prefix", Emotion = "loneliness" });

            // ═══════════════════════════════════════════════════════════════
            // Mood prefixes with relationship context
            // ═══════════════════════════════════════════════════════════════

            // Positive impression
            lines.Add(new() { Id = "mp_pos1", Text = "{npc_name} smiles when they recognize you. \"Good to see you again, {player_name}. What'll it be today?\"", Category = "mood_prefix", Context = "positive_impression" });
            lines.Add(new() { Id = "mp_pos2", Text = "{npc_name} nods approvingly. \"Back again? I like a loyal customer. Let me see what I've got for you.\"", Category = "mood_prefix", Context = "positive_impression" });

            // Negative impression
            lines.Add(new() { Id = "mp_neg1", Text = "{npc_name} stiffens when they see you. \"...You. Business is business, I suppose. What do you want?\"", Category = "mood_prefix", Context = "negative_impression" });
            lines.Add(new() { Id = "mp_neg2", Text = "{npc_name} gives you a flat look. \"I'll serve you because I serve everyone. Don't mistake that for friendship.\"", Category = "mood_prefix", Context = "negative_impression" });

            // Player is king
            lines.Add(new() { Id = "mp_king1", Text = "{npc_name} straightens up immediately. \"{player_title}! I didn't expect you! Please, take your time — everything's at your disposal.\"", Category = "mood_prefix", Context = "is_king" });

            // Player is wounded
            lines.Add(new() { Id = "mp_wound1", Text = "{npc_name} winces at the sight of you. \"You look like something the dungeon chewed up. Maybe see the healer before shopping?\"", Category = "mood_prefix", Context = "low_hp" });

            // Neutral / no strong mood
            lines.Add(new() { Id = "mp_neutral1", Text = "{npc_name} looks up from the counter. \"Welcome. Take a look around — let me know if anything catches your eye.\"", Category = "mood_prefix" });
            lines.Add(new() { Id = "mp_neutral2", Text = "{npc_name} gestures toward the display. \"Good {time_of_day}. See anything you like?\"", Category = "mood_prefix" });
            lines.Add(new() { Id = "mp_neutral3", Text = "{npc_name} wipes their hands on an apron. \"Another customer. Step right up.\"", Category = "mood_prefix" });

            return lines;
        }
    }
}
