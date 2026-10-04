using System.Collections.Generic;
using static UsurperRemake.Data.NPCDialogueDatabase;

namespace UsurperRemake.Data
{
    /// <summary>
    /// Pre-generated reaction dialogue lines for combat events, organized by personality type.
    /// </summary>
    public static class DialogueLines_Reactions
    {
        public static List<DialogueLine> GetLines()
        {
            var lines = new List<DialogueLine>();

            // ═══════════════════════════════════════════════════════════════
            // COMBAT VICTORY reactions
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "rx_cv_ag1", Text = BuiltInText("rx_cv_ag1"), Category = "reaction", PersonalityType = "aggressive", EventType = "combat_victory" });
            lines.Add(new() { Id = "rx_cv_ag2", Text = BuiltInText("rx_cv_ag2"), Category = "reaction", PersonalityType = "aggressive", EventType = "combat_victory" });
            lines.Add(new() { Id = "rx_cv_ag3", Text = BuiltInText("rx_cv_ag3"), Category = "reaction", PersonalityType = "aggressive", EventType = "combat_victory" });

            lines.Add(new() { Id = "rx_cv_no1", Text = BuiltInText("rx_cv_no1"), Category = "reaction", PersonalityType = "noble", EventType = "combat_victory" });
            lines.Add(new() { Id = "rx_cv_no2", Text = BuiltInText("rx_cv_no2"), Category = "reaction", PersonalityType = "noble", EventType = "combat_victory" });
            lines.Add(new() { Id = "rx_cv_no3", Text = BuiltInText("rx_cv_no3"), Category = "reaction", PersonalityType = "noble", EventType = "combat_victory" });

            lines.Add(new() { Id = "rx_cv_cu1", Text = BuiltInText("rx_cv_cu1"), Category = "reaction", PersonalityType = "cunning", EventType = "combat_victory" });
            lines.Add(new() { Id = "rx_cv_cu2", Text = BuiltInText("rx_cv_cu2"), Category = "reaction", PersonalityType = "cunning", EventType = "combat_victory" });

            lines.Add(new() { Id = "rx_cv_pi1", Text = BuiltInText("rx_cv_pi1"), Category = "reaction", PersonalityType = "pious", EventType = "combat_victory" });
            lines.Add(new() { Id = "rx_cv_pi2", Text = BuiltInText("rx_cv_pi2"), Category = "reaction", PersonalityType = "pious", EventType = "combat_victory" });

            lines.Add(new() { Id = "rx_cv_sc1", Text = BuiltInText("rx_cv_sc1"), Category = "reaction", PersonalityType = "scholarly", EventType = "combat_victory" });
            lines.Add(new() { Id = "rx_cv_sc2", Text = BuiltInText("rx_cv_sc2"), Category = "reaction", PersonalityType = "scholarly", EventType = "combat_victory" });

            lines.Add(new() { Id = "rx_cv_cy1", Text = BuiltInText("rx_cv_cy1"), Category = "reaction", PersonalityType = "cynical", EventType = "combat_victory" });
            lines.Add(new() { Id = "rx_cv_cy2", Text = BuiltInText("rx_cv_cy2"), Category = "reaction", PersonalityType = "cynical", EventType = "combat_victory" });

            lines.Add(new() { Id = "rx_cv_ch1", Text = BuiltInText("rx_cv_ch1"), Category = "reaction", PersonalityType = "charming", EventType = "combat_victory" });
            lines.Add(new() { Id = "rx_cv_ch2", Text = BuiltInText("rx_cv_ch2"), Category = "reaction", PersonalityType = "charming", EventType = "combat_victory" });

            lines.Add(new() { Id = "rx_cv_st1", Text = BuiltInText("rx_cv_st1"), Category = "reaction", PersonalityType = "stoic", EventType = "combat_victory" });
            lines.Add(new() { Id = "rx_cv_st2", Text = BuiltInText("rx_cv_st2"), Category = "reaction", PersonalityType = "stoic", EventType = "combat_victory" });

            // ═══════════════════════════════════════════════════════════════
            // COMBAT DEFEAT reactions
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "rx_cd_ag1", Text = BuiltInText("rx_cd_ag1"), Category = "reaction", PersonalityType = "aggressive", EventType = "combat_defeat" });
            lines.Add(new() { Id = "rx_cd_ag2", Text = BuiltInText("rx_cd_ag2"), Category = "reaction", PersonalityType = "aggressive", EventType = "combat_defeat" });

            lines.Add(new() { Id = "rx_cd_no1", Text = BuiltInText("rx_cd_no1"), Category = "reaction", PersonalityType = "noble", EventType = "combat_defeat" });
            lines.Add(new() { Id = "rx_cd_no2", Text = BuiltInText("rx_cd_no2"), Category = "reaction", PersonalityType = "noble", EventType = "combat_defeat" });

            lines.Add(new() { Id = "rx_cd_cu1", Text = BuiltInText("rx_cd_cu1"), Category = "reaction", PersonalityType = "cunning", EventType = "combat_defeat" });
            lines.Add(new() { Id = "rx_cd_pi1", Text = BuiltInText("rx_cd_pi1"), Category = "reaction", PersonalityType = "pious", EventType = "combat_defeat" });
            lines.Add(new() { Id = "rx_cd_sc1", Text = BuiltInText("rx_cd_sc1"), Category = "reaction", PersonalityType = "scholarly", EventType = "combat_defeat" });
            lines.Add(new() { Id = "rx_cd_cy1", Text = BuiltInText("rx_cd_cy1"), Category = "reaction", PersonalityType = "cynical", EventType = "combat_defeat" });
            lines.Add(new() { Id = "rx_cd_ch1", Text = BuiltInText("rx_cd_ch1"), Category = "reaction", PersonalityType = "charming", EventType = "combat_defeat" });
            lines.Add(new() { Id = "rx_cd_st1", Text = BuiltInText("rx_cd_st1"), Category = "reaction", PersonalityType = "stoic", EventType = "combat_defeat" });

            // ═══════════════════════════════════════════════════════════════
            // COMBAT FLEE reactions
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "rx_cf_ag1", Text = BuiltInText("rx_cf_ag1"), Category = "reaction", PersonalityType = "aggressive", EventType = "combat_flee" });
            lines.Add(new() { Id = "rx_cf_ag2", Text = BuiltInText("rx_cf_ag2"), Category = "reaction", PersonalityType = "aggressive", EventType = "combat_flee" });

            lines.Add(new() { Id = "rx_cf_no1", Text = BuiltInText("rx_cf_no1"), Category = "reaction", PersonalityType = "noble", EventType = "combat_flee" });
            lines.Add(new() { Id = "rx_cf_cu1", Text = BuiltInText("rx_cf_cu1"), Category = "reaction", PersonalityType = "cunning", EventType = "combat_flee" });
            lines.Add(new() { Id = "rx_cf_pi1", Text = BuiltInText("rx_cf_pi1"), Category = "reaction", PersonalityType = "pious", EventType = "combat_flee" });
            lines.Add(new() { Id = "rx_cf_sc1", Text = BuiltInText("rx_cf_sc1"), Category = "reaction", PersonalityType = "scholarly", EventType = "combat_flee" });
            lines.Add(new() { Id = "rx_cf_cy1", Text = BuiltInText("rx_cf_cy1"), Category = "reaction", PersonalityType = "cynical", EventType = "combat_flee" });
            lines.Add(new() { Id = "rx_cf_ch1", Text = BuiltInText("rx_cf_ch1"), Category = "reaction", PersonalityType = "charming", EventType = "combat_flee" });
            lines.Add(new() { Id = "rx_cf_st1", Text = BuiltInText("rx_cf_st1"), Category = "reaction", PersonalityType = "stoic", EventType = "combat_flee" });

            // ═══════════════════════════════════════════════════════════════
            // ALLY DEATH reactions
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "rx_ad_ag1", Text = BuiltInText("rx_ad_ag1"), Category = "reaction", PersonalityType = "aggressive", EventType = "ally_death" });
            lines.Add(new() { Id = "rx_ad_ag2", Text = BuiltInText("rx_ad_ag2"), Category = "reaction", PersonalityType = "aggressive", EventType = "ally_death" });

            lines.Add(new() { Id = "rx_ad_no1", Text = BuiltInText("rx_ad_no1"), Category = "reaction", PersonalityType = "noble", EventType = "ally_death" });
            lines.Add(new() { Id = "rx_ad_no2", Text = BuiltInText("rx_ad_no2"), Category = "reaction", PersonalityType = "noble", EventType = "ally_death" });

            lines.Add(new() { Id = "rx_ad_cu1", Text = BuiltInText("rx_ad_cu1"), Category = "reaction", PersonalityType = "cunning", EventType = "ally_death" });

            lines.Add(new() { Id = "rx_ad_pi1", Text = BuiltInText("rx_ad_pi1"), Category = "reaction", PersonalityType = "pious", EventType = "ally_death" });
            lines.Add(new() { Id = "rx_ad_pi2", Text = BuiltInText("rx_ad_pi2"), Category = "reaction", PersonalityType = "pious", EventType = "ally_death" });

            lines.Add(new() { Id = "rx_ad_sc1", Text = BuiltInText("rx_ad_sc1"), Category = "reaction", PersonalityType = "scholarly", EventType = "ally_death" });

            lines.Add(new() { Id = "rx_ad_cy1", Text = BuiltInText("rx_ad_cy1"), Category = "reaction", PersonalityType = "cynical", EventType = "ally_death" });
            lines.Add(new() { Id = "rx_ad_cy2", Text = BuiltInText("rx_ad_cy2"), Category = "reaction", PersonalityType = "cynical", EventType = "ally_death" });

            lines.Add(new() { Id = "rx_ad_ch1", Text = BuiltInText("rx_ad_ch1"), Category = "reaction", PersonalityType = "charming", EventType = "ally_death" });
            lines.Add(new() { Id = "rx_ad_ch2", Text = BuiltInText("rx_ad_ch2"), Category = "reaction", PersonalityType = "charming", EventType = "ally_death" });

            lines.Add(new() { Id = "rx_ad_st1", Text = BuiltInText("rx_ad_st1"), Category = "reaction", PersonalityType = "stoic", EventType = "ally_death" });
            lines.Add(new() { Id = "rx_ad_st2", Text = BuiltInText("rx_ad_st2"), Category = "reaction", PersonalityType = "stoic", EventType = "ally_death" });

            return lines;
        }
    }
}
