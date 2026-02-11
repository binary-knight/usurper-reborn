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

            lines.Add(new() { Id = "mem_help_ag1", Text = "You helped me out before. Don't think that makes us even — but it does mean I won't punch you first.", Category = "memory", PersonalityType = "aggressive", MemoryType = "helped" });
            lines.Add(new() { Id = "mem_help_no1", Text = "I haven't forgotten your kindness, {player_name}. Such deeds speak louder than any words.", Category = "memory", PersonalityType = "noble", MemoryType = "helped" });
            lines.Add(new() { Id = "mem_help_cu1", Text = "You helped me once. I keep careful accounts of such things. Your balance is favorable.", Category = "memory", PersonalityType = "cunning", MemoryType = "helped" });
            lines.Add(new() { Id = "mem_help_pi1", Text = "The kindness you showed me... it was the gods working through you. I'm certain of it.", Category = "memory", PersonalityType = "pious", MemoryType = "helped" });
            lines.Add(new() { Id = "mem_help_sc1", Text = "I recall your assistance with precise clarity. It's catalogued under 'reasons to trust you.'", Category = "memory", PersonalityType = "scholarly", MemoryType = "helped" });
            lines.Add(new() { Id = "mem_help_cy1", Text = "You helped me once. That makes you better than... well, everyone else around here.", Category = "memory", PersonalityType = "cynical", MemoryType = "helped" });
            lines.Add(new() { Id = "mem_help_ch1", Text = "Remember when you helped me out? I tell that story at every tavern. You're basically famous.", Category = "memory", PersonalityType = "charming", MemoryType = "helped" });
            lines.Add(new() { Id = "mem_help_st1", Text = "I remember what you did for me. *nods* I won't forget.", Category = "memory", PersonalityType = "stoic", MemoryType = "helped" });

            // Generic helped (any personality)
            lines.Add(new() { Id = "mem_help_g1", Text = "You were there for me when I needed it. That means something.", Category = "memory", MemoryType = "helped" });
            lines.Add(new() { Id = "mem_help_g2", Text = "I still owe you for what you did. Haven't forgotten.", Category = "memory", MemoryType = "helped" });

            // ═══════════════════════════════════════════════════════════════
            // ATTACKED memories - NPC remembers player attacked them
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "mem_atk_ag1", Text = "Last time you came at me with a weapon. Want to try again? I'm ready this time.", Category = "memory", PersonalityType = "aggressive", MemoryType = "attacked" });
            lines.Add(new() { Id = "mem_atk_no1", Text = "You attacked me. That wound — to my body and my trust — has not fully healed.", Category = "memory", PersonalityType = "noble", MemoryType = "attacked" });
            lines.Add(new() { Id = "mem_atk_cu1", Text = "I remember your little attack. I've been preparing for the possibility of a repeat. You won't find me so easy a target.", Category = "memory", PersonalityType = "cunning", MemoryType = "attacked" });
            lines.Add(new() { Id = "mem_atk_pi1", Text = "You raised your hand against me. I've prayed for the strength to forgive you. I'm... working on it.", Category = "memory", PersonalityType = "pious", MemoryType = "attacked" });
            lines.Add(new() { Id = "mem_atk_cy1", Text = "Oh, you. The one who decided to make things personal. Trust me, I haven't forgotten.", Category = "memory", PersonalityType = "cynical", MemoryType = "attacked" });
            lines.Add(new() { Id = "mem_atk_st1", Text = "*hand moves to weapon instinctively* Last time you drew steel on me. Don't try it again.", Category = "memory", PersonalityType = "stoic", MemoryType = "attacked" });

            // Generic attacked
            lines.Add(new() { Id = "mem_atk_g1", Text = "I still remember you coming at me with that weapon. Hard to forget something like that.", Category = "memory", MemoryType = "attacked" });

            // ═══════════════════════════════════════════════════════════════
            // BETRAYED memories - NPC remembers player betrayed them
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "mem_bet_ag1", Text = "You stabbed me in the back. Figuratively. Next time it won't be figurative.", Category = "memory", PersonalityType = "aggressive", MemoryType = "betrayed" });
            lines.Add(new() { Id = "mem_bet_no1", Text = "Your betrayal cut deeper than any blade. I once believed in you. That was my mistake.", Category = "memory", PersonalityType = "noble", MemoryType = "betrayed" });
            lines.Add(new() { Id = "mem_bet_cu1", Text = "You betrayed me. Impressive, actually — I don't usually get blindsided. Won't happen twice.", Category = "memory", PersonalityType = "cunning", MemoryType = "betrayed" });
            lines.Add(new() { Id = "mem_bet_pi1", Text = "You broke my trust. That... that hurt more than anything physical ever could.", Category = "memory", PersonalityType = "pious", MemoryType = "betrayed" });
            lines.Add(new() { Id = "mem_bet_ch1", Text = "You know, betrayal really ruins a good friendship. And we had such potential.", Category = "memory", PersonalityType = "charming", MemoryType = "betrayed" });

            // Generic betrayed
            lines.Add(new() { Id = "mem_bet_g1", Text = "After what you did... trust doesn't come easy anymore. Not with you.", Category = "memory", MemoryType = "betrayed" });

            // ═══════════════════════════════════════════════════════════════
            // SAVED memories - NPC remembers player saved their life
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "mem_sav_ag1", Text = "You saved my life. I HATE owing people, but... yeah. I owe you. Big time.", Category = "memory", PersonalityType = "aggressive", MemoryType = "saved" });
            lines.Add(new() { Id = "mem_sav_no1", Text = "You saved my life. I am honor-bound to repay that debt, and I intend to.", Category = "memory", PersonalityType = "noble", MemoryType = "saved" });
            lines.Add(new() { Id = "mem_sav_pi1", Text = "You saved me. I believe the gods sent you. That day, you were my miracle.", Category = "memory", PersonalityType = "pious", MemoryType = "saved" });
            lines.Add(new() { Id = "mem_sav_cy1", Text = "You pulled me out of death's grip. Annoying, really — I was just getting comfortable.", Category = "memory", PersonalityType = "cynical", MemoryType = "saved" });
            lines.Add(new() { Id = "mem_sav_ch1", Text = "You literally saved my life! I've been telling everyone. You're my favorite person. After me, obviously.", Category = "memory", PersonalityType = "charming", MemoryType = "saved" });
            lines.Add(new() { Id = "mem_sav_st1", Text = "*meets your eyes directly* You saved me. That's not something I'll ever forget.", Category = "memory", PersonalityType = "stoic", MemoryType = "saved" });

            // Generic saved
            lines.Add(new() { Id = "mem_sav_g1", Text = "You saved my life. I don't take that lightly. Ever.", Category = "memory", MemoryType = "saved" });

            // ═══════════════════════════════════════════════════════════════
            // DEFENDED memories - NPC remembers player defended them
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "mem_def_ag1", Text = "You had my back in that fight. That's all I need to know about a person.", Category = "memory", PersonalityType = "aggressive", MemoryType = "defended" });
            lines.Add(new() { Id = "mem_def_no1", Text = "You defended me when others would have walked away. That kind of courage is rare.", Category = "memory", PersonalityType = "noble", MemoryType = "defended" });
            lines.Add(new() { Id = "mem_def_st1", Text = "You stood between me and danger. *pauses* I remember.", Category = "memory", PersonalityType = "stoic", MemoryType = "defended" });

            // Generic defended
            lines.Add(new() { Id = "mem_def_g1", Text = "I remember you standing up for me. Not everyone would've done that.", Category = "memory", MemoryType = "defended" });

            // ═══════════════════════════════════════════════════════════════
            // TRADED memories
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "mem_trade_cu1", Text = "Our last deal was profitable. I like profitable relationships.", Category = "memory", PersonalityType = "cunning", MemoryType = "traded" });
            lines.Add(new() { Id = "mem_trade_cy1", Text = "We traded before. You didn't rip me off, which puts you ahead of most.", Category = "memory", PersonalityType = "cynical", MemoryType = "traded" });
            lines.Add(new() { Id = "mem_trade_g1", Text = "Good doing business with you last time. Shall we see about another deal?", Category = "memory", MemoryType = "traded" });

            // ═══════════════════════════════════════════════════════════════
            // INSULTED memories
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "mem_ins_ag1", Text = "You mouthed off to me before. One more word and I'll rearrange your face for free.", Category = "memory", PersonalityType = "aggressive", MemoryType = "insulted" });
            lines.Add(new() { Id = "mem_ins_no1", Text = "Your words were... hurtful. I'd like to think you've reconsidered since then.", Category = "memory", PersonalityType = "noble", MemoryType = "insulted" });
            lines.Add(new() { Id = "mem_ins_ch1", Text = "Your insult? Cute. I've been called worse by better. But I did remember it, so... points for creativity.", Category = "memory", PersonalityType = "charming", MemoryType = "insulted" });

            // Generic insulted
            lines.Add(new() { Id = "mem_ins_g1", Text = "I still remember what you said to me. Words leave marks too, you know.", Category = "memory", MemoryType = "insulted" });

            // ═══════════════════════════════════════════════════════════════
            // COMPLIMENTED memories
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "mem_comp_ag1", Text = "You said some nice things about me before. I'm not good with... that stuff. But I heard you.", Category = "memory", PersonalityType = "aggressive", MemoryType = "complimented" });
            lines.Add(new() { Id = "mem_comp_pi1", Text = "Your kind words still warm my heart. Such generosity of spirit is a gift.", Category = "memory", PersonalityType = "pious", MemoryType = "complimented" });
            lines.Add(new() { Id = "mem_comp_ch1", Text = "You said I was amazing, remember? I mean, you were RIGHT, but it was still nice to hear.", Category = "memory", PersonalityType = "charming", MemoryType = "complimented" });

            // Generic complimented
            lines.Add(new() { Id = "mem_comp_g1", Text = "That kind thing you said? I've been thinking about it. Thank you.", Category = "memory", MemoryType = "complimented" });

            return lines;
        }
    }
}
