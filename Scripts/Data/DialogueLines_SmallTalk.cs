using System.Collections.Generic;
using static UsurperRemake.Data.NPCDialogueDatabase;

namespace UsurperRemake.Data
{
    /// <summary>
    /// Pre-generated small talk dialogue lines organized by personality type.
    /// Each personality has distinct topics and speech patterns.
    /// </summary>
    public static class DialogueLines_SmallTalk
    {
        public static List<DialogueLine> GetLines()
        {
            var lines = new List<DialogueLine>();

            // ═══════════════════════════════════════════════════════════════
            // AGGRESSIVE - Combat stories, boasts, challenges
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "st_ag1", Text = BuiltInText("st_ag1"), Category = "smalltalk", PersonalityType = "aggressive" });
            lines.Add(new() { Id = "st_ag2", Text = BuiltInText("st_ag2"), Category = "smalltalk", PersonalityType = "aggressive" });
            lines.Add(new() { Id = "st_ag3", Text = BuiltInText("st_ag3"), Category = "smalltalk", PersonalityType = "aggressive" });
            lines.Add(new() { Id = "st_ag4", Text = BuiltInText("st_ag4"), Category = "smalltalk", PersonalityType = "aggressive" });
            lines.Add(new() { Id = "st_ag5", Text = BuiltInText("st_ag5"), Category = "smalltalk", PersonalityType = "aggressive" });
            lines.Add(new() { Id = "st_ag6", Text = BuiltInText("st_ag6"), Category = "smalltalk", PersonalityType = "aggressive" });
            lines.Add(new() { Id = "st_ag7", Text = BuiltInText("st_ag7"), Category = "smalltalk", PersonalityType = "aggressive" });
            lines.Add(new() { Id = "st_ag8", Text = BuiltInText("st_ag8"), Category = "smalltalk", PersonalityType = "aggressive" });
            lines.Add(new() { Id = "st_ag9", Text = BuiltInText("st_ag9"), Category = "smalltalk", PersonalityType = "aggressive" });
            lines.Add(new() { Id = "st_ag10", Text = BuiltInText("st_ag10"), Category = "smalltalk", PersonalityType = "aggressive" });

            // ═══════════════════════════════════════════════════════════════
            // NOBLE - Honor, duty, politics, chivalry
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "st_no1", Text = BuiltInText("st_no1"), Category = "smalltalk", PersonalityType = "noble" });
            lines.Add(new() { Id = "st_no2", Text = BuiltInText("st_no2"), Category = "smalltalk", PersonalityType = "noble" });
            lines.Add(new() { Id = "st_no3", Text = BuiltInText("st_no3"), Category = "smalltalk", PersonalityType = "noble" });
            lines.Add(new() { Id = "st_no4", Text = BuiltInText("st_no4"), Category = "smalltalk", PersonalityType = "noble" });
            lines.Add(new() { Id = "st_no5", Text = BuiltInText("st_no5"), Category = "smalltalk", PersonalityType = "noble" });
            lines.Add(new() { Id = "st_no6", Text = BuiltInText("st_no6"), Category = "smalltalk", PersonalityType = "noble" });
            lines.Add(new() { Id = "st_no7", Text = BuiltInText("st_no7"), Category = "smalltalk", PersonalityType = "noble" });
            lines.Add(new() { Id = "st_no8", Text = BuiltInText("st_no8"), Category = "smalltalk", PersonalityType = "noble" });
            lines.Add(new() { Id = "st_no9", Text = BuiltInText("st_no9"), Category = "smalltalk", PersonalityType = "noble" });
            lines.Add(new() { Id = "st_no10", Text = BuiltInText("st_no10"), Category = "smalltalk", PersonalityType = "noble" });

            // ═══════════════════════════════════════════════════════════════
            // CUNNING - Rumors, schemes, observations
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "st_cu1", Text = BuiltInText("st_cu1"), Category = "smalltalk", PersonalityType = "cunning" });
            lines.Add(new() { Id = "st_cu2", Text = BuiltInText("st_cu2"), Category = "smalltalk", PersonalityType = "cunning" });
            lines.Add(new() { Id = "st_cu3", Text = BuiltInText("st_cu3"), Category = "smalltalk", PersonalityType = "cunning" });
            lines.Add(new() { Id = "st_cu4", Text = BuiltInText("st_cu4"), Category = "smalltalk", PersonalityType = "cunning" });
            lines.Add(new() { Id = "st_cu5", Text = BuiltInText("st_cu5"), Category = "smalltalk", PersonalityType = "cunning" });
            lines.Add(new() { Id = "st_cu6", Text = BuiltInText("st_cu6"), Category = "smalltalk", PersonalityType = "cunning" });
            lines.Add(new() { Id = "st_cu7", Text = BuiltInText("st_cu7"), Category = "smalltalk", PersonalityType = "cunning" });
            lines.Add(new() { Id = "st_cu8", Text = BuiltInText("st_cu8"), Category = "smalltalk", PersonalityType = "cunning" });
            lines.Add(new() { Id = "st_cu9", Text = BuiltInText("st_cu9"), Category = "smalltalk", PersonalityType = "cunning" });
            lines.Add(new() { Id = "st_cu10", Text = BuiltInText("st_cu10"), Category = "smalltalk", PersonalityType = "cunning" });

            // ═══════════════════════════════════════════════════════════════
            // PIOUS - Faith, blessings, morality, philosophy
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "st_pi1", Text = BuiltInText("st_pi1"), Category = "smalltalk", PersonalityType = "pious" });
            lines.Add(new() { Id = "st_pi2", Text = BuiltInText("st_pi2"), Category = "smalltalk", PersonalityType = "pious" });
            lines.Add(new() { Id = "st_pi3", Text = BuiltInText("st_pi3"), Category = "smalltalk", PersonalityType = "pious" });
            lines.Add(new() { Id = "st_pi4", Text = BuiltInText("st_pi4"), Category = "smalltalk", PersonalityType = "pious" });
            lines.Add(new() { Id = "st_pi5", Text = BuiltInText("st_pi5"), Category = "smalltalk", PersonalityType = "pious" });
            lines.Add(new() { Id = "st_pi6", Text = BuiltInText("st_pi6"), Category = "smalltalk", PersonalityType = "pious" });
            lines.Add(new() { Id = "st_pi7", Text = BuiltInText("st_pi7"), Category = "smalltalk", PersonalityType = "pious" });
            lines.Add(new() { Id = "st_pi8", Text = BuiltInText("st_pi8"), Category = "smalltalk", PersonalityType = "pious" });
            lines.Add(new() { Id = "st_pi9", Text = BuiltInText("st_pi9"), Category = "smalltalk", PersonalityType = "pious" });
            lines.Add(new() { Id = "st_pi10", Text = BuiltInText("st_pi10"), Category = "smalltalk", PersonalityType = "pious" });

            // ═══════════════════════════════════════════════════════════════
            // SCHOLARLY - Knowledge, research, theory, discovery
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "st_sc1", Text = BuiltInText("st_sc1"), Category = "smalltalk", PersonalityType = "scholarly" });
            lines.Add(new() { Id = "st_sc2", Text = BuiltInText("st_sc2"), Category = "smalltalk", PersonalityType = "scholarly" });
            lines.Add(new() { Id = "st_sc3", Text = BuiltInText("st_sc3"), Category = "smalltalk", PersonalityType = "scholarly" });
            lines.Add(new() { Id = "st_sc4", Text = BuiltInText("st_sc4"), Category = "smalltalk", PersonalityType = "scholarly" });
            lines.Add(new() { Id = "st_sc5", Text = BuiltInText("st_sc5"), Category = "smalltalk", PersonalityType = "scholarly" });
            lines.Add(new() { Id = "st_sc6", Text = BuiltInText("st_sc6"), Category = "smalltalk", PersonalityType = "scholarly" });
            lines.Add(new() { Id = "st_sc7", Text = BuiltInText("st_sc7"), Category = "smalltalk", PersonalityType = "scholarly" });
            lines.Add(new() { Id = "st_sc8", Text = BuiltInText("st_sc8"), Category = "smalltalk", PersonalityType = "scholarly" });
            lines.Add(new() { Id = "st_sc9", Text = BuiltInText("st_sc9"), Category = "smalltalk", PersonalityType = "scholarly" });
            lines.Add(new() { Id = "st_sc10", Text = BuiltInText("st_sc10"), Category = "smalltalk", PersonalityType = "scholarly" });

            // ═══════════════════════════════════════════════════════════════
            // CYNICAL - Complaints, pessimism, dark humor
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "st_cy1", Text = BuiltInText("st_cy1"), Category = "smalltalk", PersonalityType = "cynical" });
            lines.Add(new() { Id = "st_cy2", Text = BuiltInText("st_cy2"), Category = "smalltalk", PersonalityType = "cynical" });
            lines.Add(new() { Id = "st_cy3", Text = BuiltInText("st_cy3"), Category = "smalltalk", PersonalityType = "cynical" });
            lines.Add(new() { Id = "st_cy4", Text = BuiltInText("st_cy4"), Category = "smalltalk", PersonalityType = "cynical" });
            lines.Add(new() { Id = "st_cy5", Text = BuiltInText("st_cy5"), Category = "smalltalk", PersonalityType = "cynical" });
            lines.Add(new() { Id = "st_cy6", Text = BuiltInText("st_cy6"), Category = "smalltalk", PersonalityType = "cynical" });
            lines.Add(new() { Id = "st_cy7", Text = BuiltInText("st_cy7"), Category = "smalltalk", PersonalityType = "cynical" });
            lines.Add(new() { Id = "st_cy8", Text = BuiltInText("st_cy8"), Category = "smalltalk", PersonalityType = "cynical" });
            lines.Add(new() { Id = "st_cy9", Text = BuiltInText("st_cy9"), Category = "smalltalk", PersonalityType = "cynical" });
            lines.Add(new() { Id = "st_cy10", Text = BuiltInText("st_cy10"), Category = "smalltalk", PersonalityType = "cynical" });

            // ═══════════════════════════════════════════════════════════════
            // CHARMING - Stories, flirting, humor, adventure
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "st_ch1", Text = BuiltInText("st_ch1"), Category = "smalltalk", PersonalityType = "charming" });
            lines.Add(new() { Id = "st_ch2", Text = BuiltInText("st_ch2"), Category = "smalltalk", PersonalityType = "charming" });
            lines.Add(new() { Id = "st_ch3", Text = BuiltInText("st_ch3"), Category = "smalltalk", PersonalityType = "charming" });
            lines.Add(new() { Id = "st_ch4", Text = BuiltInText("st_ch4"), Category = "smalltalk", PersonalityType = "charming" });
            lines.Add(new() { Id = "st_ch5", Text = BuiltInText("st_ch5"), Category = "smalltalk", PersonalityType = "charming" });
            lines.Add(new() { Id = "st_ch6", Text = BuiltInText("st_ch6"), Category = "smalltalk", PersonalityType = "charming" });
            lines.Add(new() { Id = "st_ch7", Text = BuiltInText("st_ch7"), Category = "smalltalk", PersonalityType = "charming" });
            lines.Add(new() { Id = "st_ch8", Text = BuiltInText("st_ch8"), Category = "smalltalk", PersonalityType = "charming" });
            lines.Add(new() { Id = "st_ch9", Text = BuiltInText("st_ch9"), Category = "smalltalk", PersonalityType = "charming" });
            lines.Add(new() { Id = "st_ch10", Text = BuiltInText("st_ch10"), Category = "smalltalk", PersonalityType = "charming" });

            // ═══════════════════════════════════════════════════════════════
            // STOIC - Observations, silence, minimal but meaningful
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "st_st1", Text = BuiltInText("st_st1"), Category = "smalltalk", PersonalityType = "stoic" });
            lines.Add(new() { Id = "st_st2", Text = BuiltInText("st_st2"), Category = "smalltalk", PersonalityType = "stoic" });
            lines.Add(new() { Id = "st_st3", Text = BuiltInText("st_st3"), Category = "smalltalk", PersonalityType = "stoic" });
            lines.Add(new() { Id = "st_st4", Text = BuiltInText("st_st4"), Category = "smalltalk", PersonalityType = "stoic" });
            lines.Add(new() { Id = "st_st5", Text = BuiltInText("st_st5"), Category = "smalltalk", PersonalityType = "stoic" });
            lines.Add(new() { Id = "st_st6", Text = BuiltInText("st_st6"), Category = "smalltalk", PersonalityType = "stoic" });
            lines.Add(new() { Id = "st_st7", Text = BuiltInText("st_st7"), Category = "smalltalk", PersonalityType = "stoic" });
            lines.Add(new() { Id = "st_st8", Text = BuiltInText("st_st8"), Category = "smalltalk", PersonalityType = "stoic" });
            lines.Add(new() { Id = "st_st9", Text = BuiltInText("st_st9"), Category = "smalltalk", PersonalityType = "stoic" });
            lines.Add(new() { Id = "st_st10", Text = BuiltInText("st_st10"), Category = "smalltalk", PersonalityType = "stoic" });

            return lines;
        }
    }
}
