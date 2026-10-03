using System.Collections.Generic;
using static UsurperRemake.Data.NPCDialogueDatabase;

namespace UsurperRemake.Data
{
    /// <summary>
    /// Pre-generated farewell dialogue lines organized by personality type and relationship tier.
    /// </summary>
    public static class DialogueLines_Farewells
    {
        public static List<DialogueLine> GetLines()
        {
            var lines = new List<DialogueLine>();

            // ═══ AGGRESSIVE ═══
            lines.Add(new() { Id = "fw_ag_m1", Text = BuiltInText("fw_ag_m1"), Category = "farewell", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationMarried });
            lines.Add(new() { Id = "fw_ag_l1", Text = BuiltInText("fw_ag_l1"), Category = "farewell", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationLove });
            lines.Add(new() { Id = "fw_ag_f1", Text = BuiltInText("fw_ag_f1"), Category = "farewell", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "fw_ag_f2", Text = BuiltInText("fw_ag_f2"), Category = "farewell", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "fw_ag_n1", Text = BuiltInText("fw_ag_n1"), Category = "farewell", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "fw_ag_n2", Text = BuiltInText("fw_ag_n2"), Category = "farewell", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "fw_ag_s1", Text = BuiltInText("fw_ag_s1"), Category = "farewell", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationSuspicious });
            lines.Add(new() { Id = "fw_ag_a1", Text = BuiltInText("fw_ag_a1"), Category = "farewell", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationAnger });
            lines.Add(new() { Id = "fw_ag_e1", Text = BuiltInText("fw_ag_e1"), Category = "farewell", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationEnemy });

            // ═══ NOBLE ═══
            lines.Add(new() { Id = "fw_no_m1", Text = BuiltInText("fw_no_m1"), Category = "farewell", PersonalityType = "noble", RelationshipTier = GameConfig.RelationMarried });
            lines.Add(new() { Id = "fw_no_l1", Text = BuiltInText("fw_no_l1"), Category = "farewell", PersonalityType = "noble", RelationshipTier = GameConfig.RelationLove });
            lines.Add(new() { Id = "fw_no_f1", Text = BuiltInText("fw_no_f1"), Category = "farewell", PersonalityType = "noble", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "fw_no_f2", Text = BuiltInText("fw_no_f2"), Category = "farewell", PersonalityType = "noble", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "fw_no_n1", Text = BuiltInText("fw_no_n1"), Category = "farewell", PersonalityType = "noble", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "fw_no_a1", Text = BuiltInText("fw_no_a1"), Category = "farewell", PersonalityType = "noble", RelationshipTier = GameConfig.RelationAnger });
            lines.Add(new() { Id = "fw_no_e1", Text = BuiltInText("fw_no_e1"), Category = "farewell", PersonalityType = "noble", RelationshipTier = GameConfig.RelationEnemy });

            // ═══ CUNNING ═══
            lines.Add(new() { Id = "fw_cu_m1", Text = BuiltInText("fw_cu_m1"), Category = "farewell", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationMarried });
            lines.Add(new() { Id = "fw_cu_f1", Text = BuiltInText("fw_cu_f1"), Category = "farewell", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "fw_cu_f2", Text = BuiltInText("fw_cu_f2"), Category = "farewell", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "fw_cu_n1", Text = BuiltInText("fw_cu_n1"), Category = "farewell", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "fw_cu_s1", Text = BuiltInText("fw_cu_s1"), Category = "farewell", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationSuspicious });
            lines.Add(new() { Id = "fw_cu_e1", Text = BuiltInText("fw_cu_e1"), Category = "farewell", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationEnemy });

            // ═══ PIOUS ═══
            lines.Add(new() { Id = "fw_pi_m1", Text = BuiltInText("fw_pi_m1"), Category = "farewell", PersonalityType = "pious", RelationshipTier = GameConfig.RelationMarried });
            lines.Add(new() { Id = "fw_pi_f1", Text = BuiltInText("fw_pi_f1"), Category = "farewell", PersonalityType = "pious", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "fw_pi_f2", Text = BuiltInText("fw_pi_f2"), Category = "farewell", PersonalityType = "pious", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "fw_pi_n1", Text = BuiltInText("fw_pi_n1"), Category = "farewell", PersonalityType = "pious", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "fw_pi_n2", Text = BuiltInText("fw_pi_n2"), Category = "farewell", PersonalityType = "pious", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "fw_pi_a1", Text = BuiltInText("fw_pi_a1"), Category = "farewell", PersonalityType = "pious", RelationshipTier = GameConfig.RelationAnger });

            // ═══ SCHOLARLY ═══
            lines.Add(new() { Id = "fw_sc_m1", Text = BuiltInText("fw_sc_m1"), Category = "farewell", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationMarried });
            lines.Add(new() { Id = "fw_sc_f1", Text = BuiltInText("fw_sc_f1"), Category = "farewell", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "fw_sc_f2", Text = BuiltInText("fw_sc_f2"), Category = "farewell", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "fw_sc_n1", Text = BuiltInText("fw_sc_n1"), Category = "farewell", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "fw_sc_n2", Text = BuiltInText("fw_sc_n2"), Category = "farewell", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationNormal });

            // ═══ CYNICAL ═══
            lines.Add(new() { Id = "fw_cy_m1", Text = BuiltInText("fw_cy_m1"), Category = "farewell", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationMarried });
            lines.Add(new() { Id = "fw_cy_f1", Text = BuiltInText("fw_cy_f1"), Category = "farewell", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "fw_cy_f2", Text = BuiltInText("fw_cy_f2"), Category = "farewell", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "fw_cy_n1", Text = BuiltInText("fw_cy_n1"), Category = "farewell", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "fw_cy_n2", Text = BuiltInText("fw_cy_n2"), Category = "farewell", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "fw_cy_a1", Text = BuiltInText("fw_cy_a1"), Category = "farewell", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationAnger });

            // ═══ CHARMING ═══
            lines.Add(new() { Id = "fw_ch_m1", Text = BuiltInText("fw_ch_m1"), Category = "farewell", PersonalityType = "charming", RelationshipTier = GameConfig.RelationMarried });
            lines.Add(new() { Id = "fw_ch_l1", Text = BuiltInText("fw_ch_l1"), Category = "farewell", PersonalityType = "charming", RelationshipTier = GameConfig.RelationLove });
            lines.Add(new() { Id = "fw_ch_f1", Text = BuiltInText("fw_ch_f1"), Category = "farewell", PersonalityType = "charming", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "fw_ch_f2", Text = BuiltInText("fw_ch_f2"), Category = "farewell", PersonalityType = "charming", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "fw_ch_n1", Text = BuiltInText("fw_ch_n1"), Category = "farewell", PersonalityType = "charming", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "fw_ch_n2", Text = BuiltInText("fw_ch_n2"), Category = "farewell", PersonalityType = "charming", RelationshipTier = GameConfig.RelationNormal });

            // ═══ STOIC ═══
            lines.Add(new() { Id = "fw_st_m1", Text = BuiltInText("fw_st_m1"), Category = "farewell", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationMarried });
            lines.Add(new() { Id = "fw_st_f1", Text = BuiltInText("fw_st_f1"), Category = "farewell", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "fw_st_f2", Text = BuiltInText("fw_st_f2"), Category = "farewell", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "fw_st_n1", Text = BuiltInText("fw_st_n1"), Category = "farewell", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "fw_st_n2", Text = BuiltInText("fw_st_n2"), Category = "farewell", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "fw_st_e1", Text = BuiltInText("fw_st_e1"), Category = "farewell", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationEnemy });

            return lines;
        }
    }
}
