using System.Collections.Generic;
using static UsurperRemake.Data.NPCDialogueDatabase;

namespace UsurperRemake.Data
{
    /// <summary>
    /// Pre-generated memory reference dialogue lines.
    /// These are woven into greetings/conversation when an NPC remembers a past interaction with the player.
    /// </summary>
    public static class DialogueLines_Memory
    {
        public static List<DialogueLine> GetLines()
        {
            var lines = new List<DialogueLine>();

            // ═══════════════════════════════════════════════════════════════
            // HELPED memories - NPC remembers player helped them
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "mem_help_ag1", Text = BuiltInText("mem_help_ag1"), Category = "memory", PersonalityType = "aggressive", MemoryType = "helped" });
            lines.Add(new() { Id = "mem_help_no1", Text = BuiltInText("mem_help_no1"), Category = "memory", PersonalityType = "noble", MemoryType = "helped" });
            lines.Add(new() { Id = "mem_help_cu1", Text = BuiltInText("mem_help_cu1"), Category = "memory", PersonalityType = "cunning", MemoryType = "helped" });
            lines.Add(new() { Id = "mem_help_pi1", Text = BuiltInText("mem_help_pi1"), Category = "memory", PersonalityType = "pious", MemoryType = "helped" });
            lines.Add(new() { Id = "mem_help_sc1", Text = BuiltInText("mem_help_sc1"), Category = "memory", PersonalityType = "scholarly", MemoryType = "helped" });
            lines.Add(new() { Id = "mem_help_cy1", Text = BuiltInText("mem_help_cy1"), Category = "memory", PersonalityType = "cynical", MemoryType = "helped" });
            lines.Add(new() { Id = "mem_help_ch1", Text = BuiltInText("mem_help_ch1"), Category = "memory", PersonalityType = "charming", MemoryType = "helped" });
            lines.Add(new() { Id = "mem_help_st1", Text = BuiltInText("mem_help_st1"), Category = "memory", PersonalityType = "stoic", MemoryType = "helped" });

            // Generic helped (any personality)
            lines.Add(new() { Id = "mem_help_g1", Text = BuiltInText("mem_help_g1"), Category = "memory", MemoryType = "helped" });
            lines.Add(new() { Id = "mem_help_g2", Text = BuiltInText("mem_help_g2"), Category = "memory", MemoryType = "helped" });

            // ═══════════════════════════════════════════════════════════════
            // ATTACKED memories - NPC remembers player attacked them
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "mem_atk_ag1", Text = BuiltInText("mem_atk_ag1"), Category = "memory", PersonalityType = "aggressive", MemoryType = "attacked" });
            lines.Add(new() { Id = "mem_atk_no1", Text = BuiltInText("mem_atk_no1"), Category = "memory", PersonalityType = "noble", MemoryType = "attacked" });
            lines.Add(new() { Id = "mem_atk_cu1", Text = BuiltInText("mem_atk_cu1"), Category = "memory", PersonalityType = "cunning", MemoryType = "attacked" });
            lines.Add(new() { Id = "mem_atk_pi1", Text = BuiltInText("mem_atk_pi1"), Category = "memory", PersonalityType = "pious", MemoryType = "attacked" });
            lines.Add(new() { Id = "mem_atk_cy1", Text = BuiltInText("mem_atk_cy1"), Category = "memory", PersonalityType = "cynical", MemoryType = "attacked" });
            lines.Add(new() { Id = "mem_atk_st1", Text = BuiltInText("mem_atk_st1"), Category = "memory", PersonalityType = "stoic", MemoryType = "attacked" });

            // Generic attacked
            lines.Add(new() { Id = "mem_atk_g1", Text = BuiltInText("mem_atk_g1"), Category = "memory", MemoryType = "attacked" });

            // ═══════════════════════════════════════════════════════════════
            // BETRAYED memories - NPC remembers player betrayed them
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "mem_bet_ag1", Text = BuiltInText("mem_bet_ag1"), Category = "memory", PersonalityType = "aggressive", MemoryType = "betrayed" });
            lines.Add(new() { Id = "mem_bet_no1", Text = BuiltInText("mem_bet_no1"), Category = "memory", PersonalityType = "noble", MemoryType = "betrayed" });
            lines.Add(new() { Id = "mem_bet_cu1", Text = BuiltInText("mem_bet_cu1"), Category = "memory", PersonalityType = "cunning", MemoryType = "betrayed" });
            lines.Add(new() { Id = "mem_bet_pi1", Text = BuiltInText("mem_bet_pi1"), Category = "memory", PersonalityType = "pious", MemoryType = "betrayed" });
            lines.Add(new() { Id = "mem_bet_ch1", Text = BuiltInText("mem_bet_ch1"), Category = "memory", PersonalityType = "charming", MemoryType = "betrayed" });

            // Generic betrayed
            lines.Add(new() { Id = "mem_bet_g1", Text = BuiltInText("mem_bet_g1"), Category = "memory", MemoryType = "betrayed" });

            // ═══════════════════════════════════════════════════════════════
            // SAVED memories - NPC remembers player saved their life
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "mem_sav_ag1", Text = BuiltInText("mem_sav_ag1"), Category = "memory", PersonalityType = "aggressive", MemoryType = "saved" });
            lines.Add(new() { Id = "mem_sav_no1", Text = BuiltInText("mem_sav_no1"), Category = "memory", PersonalityType = "noble", MemoryType = "saved" });
            lines.Add(new() { Id = "mem_sav_pi1", Text = BuiltInText("mem_sav_pi1"), Category = "memory", PersonalityType = "pious", MemoryType = "saved" });
            lines.Add(new() { Id = "mem_sav_cy1", Text = BuiltInText("mem_sav_cy1"), Category = "memory", PersonalityType = "cynical", MemoryType = "saved" });
            lines.Add(new() { Id = "mem_sav_ch1", Text = BuiltInText("mem_sav_ch1"), Category = "memory", PersonalityType = "charming", MemoryType = "saved" });
            lines.Add(new() { Id = "mem_sav_st1", Text = BuiltInText("mem_sav_st1"), Category = "memory", PersonalityType = "stoic", MemoryType = "saved" });

            // Generic saved
            lines.Add(new() { Id = "mem_sav_g1", Text = BuiltInText("mem_sav_g1"), Category = "memory", MemoryType = "saved" });

            // ═══════════════════════════════════════════════════════════════
            // DEFENDED memories - NPC remembers player defended them
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "mem_def_ag1", Text = BuiltInText("mem_def_ag1"), Category = "memory", PersonalityType = "aggressive", MemoryType = "defended" });
            lines.Add(new() { Id = "mem_def_no1", Text = BuiltInText("mem_def_no1"), Category = "memory", PersonalityType = "noble", MemoryType = "defended" });
            lines.Add(new() { Id = "mem_def_st1", Text = BuiltInText("mem_def_st1"), Category = "memory", PersonalityType = "stoic", MemoryType = "defended" });

            // Generic defended
            lines.Add(new() { Id = "mem_def_g1", Text = BuiltInText("mem_def_g1"), Category = "memory", MemoryType = "defended" });

            // ═══════════════════════════════════════════════════════════════
            // TRADED memories
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "mem_trade_cu1", Text = BuiltInText("mem_trade_cu1"), Category = "memory", PersonalityType = "cunning", MemoryType = "traded" });
            lines.Add(new() { Id = "mem_trade_cy1", Text = BuiltInText("mem_trade_cy1"), Category = "memory", PersonalityType = "cynical", MemoryType = "traded" });
            lines.Add(new() { Id = "mem_trade_g1", Text = BuiltInText("mem_trade_g1"), Category = "memory", MemoryType = "traded" });

            // ═══════════════════════════════════════════════════════════════
            // INSULTED memories
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "mem_ins_ag1", Text = BuiltInText("mem_ins_ag1"), Category = "memory", PersonalityType = "aggressive", MemoryType = "insulted" });
            lines.Add(new() { Id = "mem_ins_no1", Text = BuiltInText("mem_ins_no1"), Category = "memory", PersonalityType = "noble", MemoryType = "insulted" });
            lines.Add(new() { Id = "mem_ins_ch1", Text = BuiltInText("mem_ins_ch1"), Category = "memory", PersonalityType = "charming", MemoryType = "insulted" });

            // Generic insulted
            lines.Add(new() { Id = "mem_ins_g1", Text = BuiltInText("mem_ins_g1"), Category = "memory", MemoryType = "insulted" });

            // ═══════════════════════════════════════════════════════════════
            // COMPLIMENTED memories
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "mem_comp_ag1", Text = BuiltInText("mem_comp_ag1"), Category = "memory", PersonalityType = "aggressive", MemoryType = "complimented" });
            lines.Add(new() { Id = "mem_comp_pi1", Text = BuiltInText("mem_comp_pi1"), Category = "memory", PersonalityType = "pious", MemoryType = "complimented" });
            lines.Add(new() { Id = "mem_comp_ch1", Text = BuiltInText("mem_comp_ch1"), Category = "memory", PersonalityType = "charming", MemoryType = "complimented" });

            // Generic complimented
            lines.Add(new() { Id = "mem_comp_g1", Text = BuiltInText("mem_comp_g1"), Category = "memory", MemoryType = "complimented" });

            return lines;
        }
    }
}
