using System.Collections.Generic;
using static UsurperRemake.Data.NPCDialogueDatabase;

namespace UsurperRemake.Data
{
    /// <summary>
    /// Pre-generated greeting dialogue lines organized by personality type and relationship tier.
    /// Each line is a complete, natural-sounding greeting written by Claude Opus.
    /// </summary>
    public static class DialogueLines_Greetings
    {
        public static List<DialogueLine> GetLines()
        {
            var lines = new List<DialogueLine>();

            // ═══════════════════════════════════════════════════════════════
            // AGGRESSIVE personality greetings
            // Used by: Grok, Blackthorne, Ragnar, Vex, Brutus, etc.
            // Voice: Direct, physical, combat-focused, impatient
            // ═══════════════════════════════════════════════════════════════

            // Married
            lines.Add(new() { Id = "ag_m1", Text = BuiltInText("ag_m1"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationMarried });
            lines.Add(new() { Id = "ag_m2", Text = BuiltInText("ag_m2"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationMarried });
            lines.Add(new() { Id = "ag_m3", Text = BuiltInText("ag_m3"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationMarried });

            // Love
            lines.Add(new() { Id = "ag_l1", Text = BuiltInText("ag_l1"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationLove });
            lines.Add(new() { Id = "ag_l2", Text = BuiltInText("ag_l2"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationLove });
            lines.Add(new() { Id = "ag_l3", Text = BuiltInText("ag_l3"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationLove });

            // Passion
            lines.Add(new() { Id = "ag_p1", Text = BuiltInText("ag_p1"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationPassion });
            lines.Add(new() { Id = "ag_p2", Text = BuiltInText("ag_p2"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationPassion });
            lines.Add(new() { Id = "ag_p3", Text = BuiltInText("ag_p3"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationPassion });

            // Friendship
            lines.Add(new() { Id = "ag_f1", Text = BuiltInText("ag_f1"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "ag_f2", Text = BuiltInText("ag_f2"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "ag_f3", Text = BuiltInText("ag_f3"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationFriendship });

            // Trust
            lines.Add(new() { Id = "ag_t1", Text = BuiltInText("ag_t1"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationTrust });
            lines.Add(new() { Id = "ag_t2", Text = BuiltInText("ag_t2"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationTrust });
            lines.Add(new() { Id = "ag_t3", Text = BuiltInText("ag_t3"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationTrust });

            // Respect
            lines.Add(new() { Id = "ag_r1", Text = BuiltInText("ag_r1"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationRespect });
            lines.Add(new() { Id = "ag_r2", Text = BuiltInText("ag_r2"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationRespect });
            lines.Add(new() { Id = "ag_r3", Text = BuiltInText("ag_r3"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationRespect });

            // Normal
            lines.Add(new() { Id = "ag_n1", Text = BuiltInText("ag_n1"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "ag_n2", Text = BuiltInText("ag_n2"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "ag_n3", Text = BuiltInText("ag_n3"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationNormal });

            // Suspicious
            lines.Add(new() { Id = "ag_s1", Text = BuiltInText("ag_s1"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationSuspicious });
            lines.Add(new() { Id = "ag_s2", Text = BuiltInText("ag_s2"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationSuspicious });
            lines.Add(new() { Id = "ag_s3", Text = BuiltInText("ag_s3"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationSuspicious });

            // Anger
            lines.Add(new() { Id = "ag_a1", Text = BuiltInText("ag_a1"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationAnger });
            lines.Add(new() { Id = "ag_a2", Text = BuiltInText("ag_a2"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationAnger });
            lines.Add(new() { Id = "ag_a3", Text = BuiltInText("ag_a3"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationAnger });

            // Enemy
            lines.Add(new() { Id = "ag_e1", Text = BuiltInText("ag_e1"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationEnemy });
            lines.Add(new() { Id = "ag_e2", Text = BuiltInText("ag_e2"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationEnemy });
            lines.Add(new() { Id = "ag_e3", Text = BuiltInText("ag_e3"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationEnemy });

            // Hate
            lines.Add(new() { Id = "ag_h1", Text = BuiltInText("ag_h1"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationHate });
            lines.Add(new() { Id = "ag_h2", Text = BuiltInText("ag_h2"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationHate });
            lines.Add(new() { Id = "ag_h3", Text = BuiltInText("ag_h3"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationHate });

            // Aggressive + Emotion overlays
            lines.Add(new() { Id = "ag_ej1", Text = BuiltInText("ag_ej1"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = 0, Emotion = "joy" });
            lines.Add(new() { Id = "ag_ej2", Text = BuiltInText("ag_ej2"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = 0, Emotion = "joy" });
            lines.Add(new() { Id = "ag_ea1", Text = BuiltInText("ag_ea1"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = 0, Emotion = "anger" });
            lines.Add(new() { Id = "ag_ea2", Text = BuiltInText("ag_ea2"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = 0, Emotion = "anger" });
            lines.Add(new() { Id = "ag_es1", Text = BuiltInText("ag_es1"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = 0, Emotion = "sadness" });
            lines.Add(new() { Id = "ag_es2", Text = BuiltInText("ag_es2"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = 0, Emotion = "sadness" });
            lines.Add(new() { Id = "ag_ef1", Text = BuiltInText("ag_ef1"), Category = "greeting", PersonalityType = "aggressive", RelationshipTier = 0, Emotion = "fear" });

            // ═══════════════════════════════════════════════════════════════
            // NOBLE personality greetings
            // Used by: Sir Galahad, Lady Morgana, Kendrick, etc.
            // Voice: Formal, principled, honor-bound, warm but dignified
            // ═══════════════════════════════════════════════════════════════

            // Married
            lines.Add(new() { Id = "no_m1", Text = BuiltInText("no_m1"), Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationMarried });
            lines.Add(new() { Id = "no_m2", Text = BuiltInText("no_m2"), Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationMarried });
            lines.Add(new() { Id = "no_m3", Text = BuiltInText("no_m3"), Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationMarried });

            // Love
            lines.Add(new() { Id = "no_l1", Text = BuiltInText("no_l1"), Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationLove });
            lines.Add(new() { Id = "no_l2", Text = BuiltInText("no_l2"), Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationLove });
            lines.Add(new() { Id = "no_l3", Text = BuiltInText("no_l3"), Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationLove });

            // Passion
            lines.Add(new() { Id = "no_p1", Text = BuiltInText("no_p1"), Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationPassion });
            lines.Add(new() { Id = "no_p2", Text = BuiltInText("no_p2"), Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationPassion });
            lines.Add(new() { Id = "no_p3", Text = BuiltInText("no_p3"), Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationPassion });

            // Friendship
            lines.Add(new() { Id = "no_f1", Text = BuiltInText("no_f1"), Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "no_f2", Text = BuiltInText("no_f2"), Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "no_f3", Text = BuiltInText("no_f3"), Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationFriendship });

            // Trust
            lines.Add(new() { Id = "no_t1", Text = BuiltInText("no_t1"), Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationTrust });
            lines.Add(new() { Id = "no_t2", Text = BuiltInText("no_t2"), Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationTrust });
            lines.Add(new() { Id = "no_t3", Text = BuiltInText("no_t3"), Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationTrust });

            // Respect
            lines.Add(new() { Id = "no_r1", Text = BuiltInText("no_r1"), Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationRespect });
            lines.Add(new() { Id = "no_r2", Text = BuiltInText("no_r2"), Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationRespect });
            lines.Add(new() { Id = "no_r3", Text = BuiltInText("no_r3"), Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationRespect });

            // Normal
            lines.Add(new() { Id = "no_n1", Text = BuiltInText("no_n1"), Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "no_n2", Text = BuiltInText("no_n2"), Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "no_n3", Text = BuiltInText("no_n3"), Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationNormal });

            // Suspicious
            lines.Add(new() { Id = "no_s1", Text = BuiltInText("no_s1"), Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationSuspicious });
            lines.Add(new() { Id = "no_s2", Text = BuiltInText("no_s2"), Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationSuspicious });
            lines.Add(new() { Id = "no_s3", Text = BuiltInText("no_s3"), Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationSuspicious });

            // Anger
            lines.Add(new() { Id = "no_a1", Text = BuiltInText("no_a1"), Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationAnger });
            lines.Add(new() { Id = "no_a2", Text = BuiltInText("no_a2"), Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationAnger });
            lines.Add(new() { Id = "no_a3", Text = BuiltInText("no_a3"), Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationAnger });

            // Enemy
            lines.Add(new() { Id = "no_e1", Text = BuiltInText("no_e1"), Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationEnemy });
            lines.Add(new() { Id = "no_e2", Text = BuiltInText("no_e2"), Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationEnemy });
            lines.Add(new() { Id = "no_e3", Text = BuiltInText("no_e3"), Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationEnemy });

            // Hate
            lines.Add(new() { Id = "no_h1", Text = BuiltInText("no_h1"), Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationHate });
            lines.Add(new() { Id = "no_h2", Text = BuiltInText("no_h2"), Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationHate });
            lines.Add(new() { Id = "no_h3", Text = BuiltInText("no_h3"), Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationHate });

            // Noble + Emotion overlays
            lines.Add(new() { Id = "no_ej1", Text = BuiltInText("no_ej1"), Category = "greeting", PersonalityType = "noble", RelationshipTier = 0, Emotion = "joy" });
            lines.Add(new() { Id = "no_ea1", Text = BuiltInText("no_ea1"), Category = "greeting", PersonalityType = "noble", RelationshipTier = 0, Emotion = "anger" });
            lines.Add(new() { Id = "no_es1", Text = BuiltInText("no_es1"), Category = "greeting", PersonalityType = "noble", RelationshipTier = 0, Emotion = "sadness" });
            lines.Add(new() { Id = "no_ef1", Text = BuiltInText("no_ef1"), Category = "greeting", PersonalityType = "noble", RelationshipTier = 0, Emotion = "fear" });

            // ═══════════════════════════════════════════════════════════════
            // CUNNING personality greetings
            // Used by: Thorn, Shadow Weaver, Whisper, etc.
            // Voice: Calculating, observant, information-focused, veiled threats
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "cu_m1", Text = BuiltInText("cu_m1"), Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationMarried });
            lines.Add(new() { Id = "cu_m2", Text = BuiltInText("cu_m2"), Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationMarried });
            lines.Add(new() { Id = "cu_m3", Text = BuiltInText("cu_m3"), Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationMarried });

            lines.Add(new() { Id = "cu_l1", Text = BuiltInText("cu_l1"), Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationLove });
            lines.Add(new() { Id = "cu_l2", Text = BuiltInText("cu_l2"), Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationLove });
            lines.Add(new() { Id = "cu_l3", Text = BuiltInText("cu_l3"), Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationLove });

            lines.Add(new() { Id = "cu_p1", Text = BuiltInText("cu_p1"), Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationPassion });
            lines.Add(new() { Id = "cu_p2", Text = BuiltInText("cu_p2"), Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationPassion });

            lines.Add(new() { Id = "cu_f1", Text = BuiltInText("cu_f1"), Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "cu_f2", Text = BuiltInText("cu_f2"), Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "cu_f3", Text = BuiltInText("cu_f3"), Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationFriendship });

            lines.Add(new() { Id = "cu_t1", Text = BuiltInText("cu_t1"), Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationTrust });
            lines.Add(new() { Id = "cu_t2", Text = BuiltInText("cu_t2"), Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationTrust });

            lines.Add(new() { Id = "cu_r1", Text = BuiltInText("cu_r1"), Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationRespect });
            lines.Add(new() { Id = "cu_r2", Text = BuiltInText("cu_r2"), Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationRespect });

            lines.Add(new() { Id = "cu_n1", Text = BuiltInText("cu_n1"), Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "cu_n2", Text = BuiltInText("cu_n2"), Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "cu_n3", Text = BuiltInText("cu_n3"), Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationNormal });

            lines.Add(new() { Id = "cu_s1", Text = BuiltInText("cu_s1"), Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationSuspicious });
            lines.Add(new() { Id = "cu_s2", Text = BuiltInText("cu_s2"), Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationSuspicious });

            lines.Add(new() { Id = "cu_a1", Text = BuiltInText("cu_a1"), Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationAnger });
            lines.Add(new() { Id = "cu_a2", Text = BuiltInText("cu_a2"), Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationAnger });

            lines.Add(new() { Id = "cu_e1", Text = BuiltInText("cu_e1"), Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationEnemy });
            lines.Add(new() { Id = "cu_e2", Text = BuiltInText("cu_e2"), Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationEnemy });

            lines.Add(new() { Id = "cu_h1", Text = BuiltInText("cu_h1"), Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationHate });
            lines.Add(new() { Id = "cu_h2", Text = BuiltInText("cu_h2"), Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationHate });

            // Cunning + Emotion overlays
            lines.Add(new() { Id = "cu_ej1", Text = BuiltInText("cu_ej1"), Category = "greeting", PersonalityType = "cunning", RelationshipTier = 0, Emotion = "joy" });
            lines.Add(new() { Id = "cu_ea1", Text = BuiltInText("cu_ea1"), Category = "greeting", PersonalityType = "cunning", RelationshipTier = 0, Emotion = "anger" });
            lines.Add(new() { Id = "cu_es1", Text = BuiltInText("cu_es1"), Category = "greeting", PersonalityType = "cunning", RelationshipTier = 0, Emotion = "sadness" });

            // ═══════════════════════════════════════════════════════════════
            // PIOUS personality greetings
            // Used by: Sister Mercy, Aldwyn, Brother Aldric, etc.
            // Voice: Warm, spiritual, blessing-oriented, gentle
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "pi_m1", Text = BuiltInText("pi_m1"), Category = "greeting", PersonalityType = "pious", RelationshipTier = GameConfig.RelationMarried });
            lines.Add(new() { Id = "pi_m2", Text = BuiltInText("pi_m2"), Category = "greeting", PersonalityType = "pious", RelationshipTier = GameConfig.RelationMarried });

            lines.Add(new() { Id = "pi_l1", Text = BuiltInText("pi_l1"), Category = "greeting", PersonalityType = "pious", RelationshipTier = GameConfig.RelationLove });
            lines.Add(new() { Id = "pi_l2", Text = BuiltInText("pi_l2"), Category = "greeting", PersonalityType = "pious", RelationshipTier = GameConfig.RelationLove });

            lines.Add(new() { Id = "pi_p1", Text = BuiltInText("pi_p1"), Category = "greeting", PersonalityType = "pious", RelationshipTier = GameConfig.RelationPassion });

            lines.Add(new() { Id = "pi_f1", Text = BuiltInText("pi_f1"), Category = "greeting", PersonalityType = "pious", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "pi_f2", Text = BuiltInText("pi_f2"), Category = "greeting", PersonalityType = "pious", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "pi_f3", Text = BuiltInText("pi_f3"), Category = "greeting", PersonalityType = "pious", RelationshipTier = GameConfig.RelationFriendship });

            lines.Add(new() { Id = "pi_t1", Text = BuiltInText("pi_t1"), Category = "greeting", PersonalityType = "pious", RelationshipTier = GameConfig.RelationTrust });
            lines.Add(new() { Id = "pi_r1", Text = BuiltInText("pi_r1"), Category = "greeting", PersonalityType = "pious", RelationshipTier = GameConfig.RelationRespect });

            lines.Add(new() { Id = "pi_n1", Text = BuiltInText("pi_n1"), Category = "greeting", PersonalityType = "pious", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "pi_n2", Text = BuiltInText("pi_n2"), Category = "greeting", PersonalityType = "pious", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "pi_n3", Text = BuiltInText("pi_n3"), Category = "greeting", PersonalityType = "pious", RelationshipTier = GameConfig.RelationNormal });

            lines.Add(new() { Id = "pi_s1", Text = BuiltInText("pi_s1"), Category = "greeting", PersonalityType = "pious", RelationshipTier = GameConfig.RelationSuspicious });
            lines.Add(new() { Id = "pi_a1", Text = BuiltInText("pi_a1"), Category = "greeting", PersonalityType = "pious", RelationshipTier = GameConfig.RelationAnger });
            lines.Add(new() { Id = "pi_e1", Text = BuiltInText("pi_e1"), Category = "greeting", PersonalityType = "pious", RelationshipTier = GameConfig.RelationEnemy });
            lines.Add(new() { Id = "pi_h1", Text = BuiltInText("pi_h1"), Category = "greeting", PersonalityType = "pious", RelationshipTier = GameConfig.RelationHate });

            // Pious + Emotion
            lines.Add(new() { Id = "pi_ej1", Text = BuiltInText("pi_ej1"), Category = "greeting", PersonalityType = "pious", RelationshipTier = 0, Emotion = "joy" });
            lines.Add(new() { Id = "pi_es1", Text = BuiltInText("pi_es1"), Category = "greeting", PersonalityType = "pious", RelationshipTier = 0, Emotion = "sadness" });

            // ═══════════════════════════════════════════════════════════════
            // SCHOLARLY personality greetings
            // Used by: Zephyra, Sage Elyndra, Sera the Seeker, etc.
            // Voice: Intellectual, curious, analytical, occasionally absent-minded
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "sc_m1", Text = BuiltInText("sc_m1"), Category = "greeting", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationMarried });
            lines.Add(new() { Id = "sc_m2", Text = BuiltInText("sc_m2"), Category = "greeting", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationMarried });

            lines.Add(new() { Id = "sc_l1", Text = BuiltInText("sc_l1"), Category = "greeting", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationLove });

            lines.Add(new() { Id = "sc_f1", Text = BuiltInText("sc_f1"), Category = "greeting", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "sc_f2", Text = BuiltInText("sc_f2"), Category = "greeting", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "sc_f3", Text = BuiltInText("sc_f3"), Category = "greeting", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationFriendship });

            lines.Add(new() { Id = "sc_t1", Text = BuiltInText("sc_t1"), Category = "greeting", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationTrust });
            lines.Add(new() { Id = "sc_r1", Text = BuiltInText("sc_r1"), Category = "greeting", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationRespect });

            lines.Add(new() { Id = "sc_n1", Text = BuiltInText("sc_n1"), Category = "greeting", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "sc_n2", Text = BuiltInText("sc_n2"), Category = "greeting", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "sc_n3", Text = BuiltInText("sc_n3"), Category = "greeting", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationNormal });

            lines.Add(new() { Id = "sc_s1", Text = BuiltInText("sc_s1"), Category = "greeting", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationSuspicious });
            lines.Add(new() { Id = "sc_a1", Text = BuiltInText("sc_a1"), Category = "greeting", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationAnger });
            lines.Add(new() { Id = "sc_e1", Text = BuiltInText("sc_e1"), Category = "greeting", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationEnemy });
            lines.Add(new() { Id = "sc_h1", Text = BuiltInText("sc_h1"), Category = "greeting", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationHate });

            // Scholarly + Emotion
            lines.Add(new() { Id = "sc_ej1", Text = BuiltInText("sc_ej1"), Category = "greeting", PersonalityType = "scholarly", RelationshipTier = 0, Emotion = "joy" });
            lines.Add(new() { Id = "sc_es1", Text = BuiltInText("sc_es1"), Category = "greeting", PersonalityType = "scholarly", RelationshipTier = 0, Emotion = "sadness" });

            // ═══════════════════════════════════════════════════════════════
            // CYNICAL personality greetings
            // Used by: Grimbold, Stern NPCs, Greedy NPCs, etc.
            // Voice: Pessimistic, blunt, complaining, distrustful
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "cy_m1", Text = BuiltInText("cy_m1"), Category = "greeting", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationMarried });
            lines.Add(new() { Id = "cy_m2", Text = BuiltInText("cy_m2"), Category = "greeting", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationMarried });

            lines.Add(new() { Id = "cy_l1", Text = BuiltInText("cy_l1"), Category = "greeting", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationLove });

            lines.Add(new() { Id = "cy_f1", Text = BuiltInText("cy_f1"), Category = "greeting", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "cy_f2", Text = BuiltInText("cy_f2"), Category = "greeting", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "cy_f3", Text = BuiltInText("cy_f3"), Category = "greeting", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationFriendship });

            lines.Add(new() { Id = "cy_t1", Text = BuiltInText("cy_t1"), Category = "greeting", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationTrust });
            lines.Add(new() { Id = "cy_r1", Text = BuiltInText("cy_r1"), Category = "greeting", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationRespect });

            lines.Add(new() { Id = "cy_n1", Text = BuiltInText("cy_n1"), Category = "greeting", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "cy_n2", Text = BuiltInText("cy_n2"), Category = "greeting", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "cy_n3", Text = BuiltInText("cy_n3"), Category = "greeting", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationNormal });

            lines.Add(new() { Id = "cy_s1", Text = BuiltInText("cy_s1"), Category = "greeting", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationSuspicious });
            lines.Add(new() { Id = "cy_a1", Text = BuiltInText("cy_a1"), Category = "greeting", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationAnger });
            lines.Add(new() { Id = "cy_e1", Text = BuiltInText("cy_e1"), Category = "greeting", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationEnemy });
            lines.Add(new() { Id = "cy_h1", Text = BuiltInText("cy_h1"), Category = "greeting", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationHate });

            // Cynical + Emotion
            lines.Add(new() { Id = "cy_ej1", Text = BuiltInText("cy_ej1"), Category = "greeting", PersonalityType = "cynical", RelationshipTier = 0, Emotion = "joy" });
            lines.Add(new() { Id = "cy_es1", Text = BuiltInText("cy_es1"), Category = "greeting", PersonalityType = "cynical", RelationshipTier = 0, Emotion = "sadness" });

            // ═══════════════════════════════════════════════════════════════
            // CHARMING personality greetings
            // Used by: Silvertongue, Lucky, Flashy NPCs, etc.
            // Voice: Witty, confident, flirtatious, energetic
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "ch_m1", Text = BuiltInText("ch_m1"), Category = "greeting", PersonalityType = "charming", RelationshipTier = GameConfig.RelationMarried });
            lines.Add(new() { Id = "ch_m2", Text = BuiltInText("ch_m2"), Category = "greeting", PersonalityType = "charming", RelationshipTier = GameConfig.RelationMarried });

            lines.Add(new() { Id = "ch_l1", Text = BuiltInText("ch_l1"), Category = "greeting", PersonalityType = "charming", RelationshipTier = GameConfig.RelationLove });
            lines.Add(new() { Id = "ch_l2", Text = BuiltInText("ch_l2"), Category = "greeting", PersonalityType = "charming", RelationshipTier = GameConfig.RelationLove });

            lines.Add(new() { Id = "ch_p1", Text = BuiltInText("ch_p1"), Category = "greeting", PersonalityType = "charming", RelationshipTier = GameConfig.RelationPassion });

            lines.Add(new() { Id = "ch_f1", Text = BuiltInText("ch_f1"), Category = "greeting", PersonalityType = "charming", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "ch_f2", Text = BuiltInText("ch_f2"), Category = "greeting", PersonalityType = "charming", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "ch_f3", Text = BuiltInText("ch_f3"), Category = "greeting", PersonalityType = "charming", RelationshipTier = GameConfig.RelationFriendship });

            lines.Add(new() { Id = "ch_t1", Text = BuiltInText("ch_t1"), Category = "greeting", PersonalityType = "charming", RelationshipTier = GameConfig.RelationTrust });
            lines.Add(new() { Id = "ch_r1", Text = BuiltInText("ch_r1"), Category = "greeting", PersonalityType = "charming", RelationshipTier = GameConfig.RelationRespect });

            lines.Add(new() { Id = "ch_n1", Text = BuiltInText("ch_n1"), Category = "greeting", PersonalityType = "charming", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "ch_n2", Text = BuiltInText("ch_n2"), Category = "greeting", PersonalityType = "charming", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "ch_n3", Text = BuiltInText("ch_n3"), Category = "greeting", PersonalityType = "charming", RelationshipTier = GameConfig.RelationNormal });

            lines.Add(new() { Id = "ch_s1", Text = BuiltInText("ch_s1"), Category = "greeting", PersonalityType = "charming", RelationshipTier = GameConfig.RelationSuspicious });
            lines.Add(new() { Id = "ch_a1", Text = BuiltInText("ch_a1"), Category = "greeting", PersonalityType = "charming", RelationshipTier = GameConfig.RelationAnger });
            lines.Add(new() { Id = "ch_e1", Text = BuiltInText("ch_e1"), Category = "greeting", PersonalityType = "charming", RelationshipTier = GameConfig.RelationEnemy });
            lines.Add(new() { Id = "ch_h1", Text = BuiltInText("ch_h1"), Category = "greeting", PersonalityType = "charming", RelationshipTier = GameConfig.RelationHate });

            // Charming + Emotion
            lines.Add(new() { Id = "ch_ej1", Text = BuiltInText("ch_ej1"), Category = "greeting", PersonalityType = "charming", RelationshipTier = 0, Emotion = "joy" });
            lines.Add(new() { Id = "ch_es1", Text = BuiltInText("ch_es1"), Category = "greeting", PersonalityType = "charming", RelationshipTier = 0, Emotion = "sadness" });

            // ═══════════════════════════════════════════════════════════════
            // STOIC personality greetings
            // Used by: Silent types, Professional, Disciplined, etc.
            // Voice: Minimal, deliberate, controlled, observant
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "st_m1", Text = BuiltInText("st_m1"), Category = "greeting", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationMarried });
            lines.Add(new() { Id = "st_m2", Text = BuiltInText("st_m2"), Category = "greeting", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationMarried });

            lines.Add(new() { Id = "st_l1", Text = BuiltInText("st_l1"), Category = "greeting", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationLove });

            lines.Add(new() { Id = "st_f1", Text = BuiltInText("st_f1"), Category = "greeting", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "st_f2", Text = BuiltInText("st_f2"), Category = "greeting", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationFriendship });

            lines.Add(new() { Id = "st_t1", Text = BuiltInText("st_t1"), Category = "greeting", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationTrust });
            lines.Add(new() { Id = "st_r1", Text = BuiltInText("st_r1"), Category = "greeting", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationRespect });

            lines.Add(new() { Id = "st_n1", Text = BuiltInText("st_n1"), Category = "greeting", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "st_n2", Text = BuiltInText("st_n2"), Category = "greeting", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "st_n3", Text = BuiltInText("st_n3"), Category = "greeting", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationNormal });

            lines.Add(new() { Id = "st_s1", Text = BuiltInText("st_s1"), Category = "greeting", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationSuspicious });
            lines.Add(new() { Id = "st_a1", Text = BuiltInText("st_a1"), Category = "greeting", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationAnger });
            lines.Add(new() { Id = "st_e1", Text = BuiltInText("st_e1"), Category = "greeting", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationEnemy });
            lines.Add(new() { Id = "st_h1", Text = BuiltInText("st_h1"), Category = "greeting", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationHate });

            // ═══════════════════════════════════════════════════════════════
            // CONTEXT-SPECIFIC greetings (any personality)
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "ctx_lhp1", Text = BuiltInText("ctx_lhp1"), Category = "greeting", Context = "low_hp" });
            lines.Add(new() { Id = "ctx_lhp2", Text = BuiltInText("ctx_lhp2"), Category = "greeting", Context = "low_hp" });
            lines.Add(new() { Id = "ctx_lhp3", Text = BuiltInText("ctx_lhp3"), Category = "greeting", Context = "low_hp" });

            lines.Add(new() { Id = "ctx_king1", Text = BuiltInText("ctx_king1"), Category = "greeting", Context = "is_king" });
            lines.Add(new() { Id = "ctx_king2", Text = BuiltInText("ctx_king2"), Category = "greeting", Context = "is_king" });

            lines.Add(new() { Id = "ctx_rich1", Text = BuiltInText("ctx_rich1"), Category = "greeting", Context = "rich" });
            lines.Add(new() { Id = "ctx_rich2", Text = BuiltInText("ctx_rich2"), Category = "greeting", Context = "rich" });

            lines.Add(new() { Id = "ctx_poor1", Text = BuiltInText("ctx_poor1"), Category = "greeting", Context = "poor" });
            lines.Add(new() { Id = "ctx_poor2", Text = BuiltInText("ctx_poor2"), Category = "greeting", Context = "poor" });

            lines.Add(new() { Id = "ctx_hlvl1", Text = BuiltInText("ctx_hlvl1"), Category = "greeting", Context = "high_level" });
            lines.Add(new() { Id = "ctx_hlvl2", Text = BuiltInText("ctx_hlvl2"), Category = "greeting", Context = "high_level" });

            lines.Add(new() { Id = "ctx_llvl1", Text = BuiltInText("ctx_llvl1"), Category = "greeting", Context = "low_level" });
            lines.Add(new() { Id = "ctx_llvl2", Text = BuiltInText("ctx_llvl2"), Category = "greeting", Context = "low_level" });

            return lines;
        }
    }
}
