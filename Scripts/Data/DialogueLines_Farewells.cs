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
            lines.Add(new() { Id = "fw_ag_m1", Text = "Don't be gone too long or I'll come find you. And I won't be gentle about it.", Category = "farewell", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationMarried });
            lines.Add(new() { Id = "fw_ag_l1", Text = "Go on then. But if anyone gives you trouble, send them to me.", Category = "farewell", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationLove });
            lines.Add(new() { Id = "fw_ag_f1", Text = "Later. Try not to die — you're one of the few people I can stand.", Category = "farewell", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "fw_ag_f2", Text = "*punches your arm* Don't be a stranger. Or do. I don't care. *clearly cares*", Category = "farewell", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "fw_ag_n1", Text = "Yeah, yeah, get going. I've got things to hit.", Category = "farewell", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "fw_ag_n2", Text = "We're done? Good. I was getting restless anyway.", Category = "farewell", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "fw_ag_s1", Text = "Get lost. And stay where I can see you.", Category = "farewell", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationSuspicious });
            lines.Add(new() { Id = "fw_ag_a1", Text = "Walk away while you still can.", Category = "farewell", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationAnger });
            lines.Add(new() { Id = "fw_ag_e1", Text = "Next time I see you, things won't be this civil.", Category = "farewell", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationEnemy });

            // ═══ NOBLE ═══
            lines.Add(new() { Id = "fw_no_m1", Text = "Go safely, my love. I will count the moments until your return.", Category = "farewell", PersonalityType = "noble", RelationshipTier = GameConfig.RelationMarried });
            lines.Add(new() { Id = "fw_no_l1", Text = "May honor guide your path, {player_name}. And may it lead you back to me.", Category = "farewell", PersonalityType = "noble", RelationshipTier = GameConfig.RelationLove });
            lines.Add(new() { Id = "fw_no_f1", Text = "Until we meet again, friend. Stay true to your principles.", Category = "farewell", PersonalityType = "noble", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "fw_no_f2", Text = "Fare thee well, {player_name}. The world needs people like you. Don't let it change that.", Category = "farewell", PersonalityType = "noble", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "fw_no_n1", Text = "Safe travels, adventurer. May honor walk with you.", Category = "farewell", PersonalityType = "noble", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "fw_no_a1", Text = "We are done here. I suggest you reflect on your choices.", Category = "farewell", PersonalityType = "noble", RelationshipTier = GameConfig.RelationAnger });
            lines.Add(new() { Id = "fw_no_e1", Text = "Leave, and pray our paths don't cross on the battlefield.", Category = "farewell", PersonalityType = "noble", RelationshipTier = GameConfig.RelationEnemy });

            // ═══ CUNNING ═══
            lines.Add(new() { Id = "fw_cu_m1", Text = "Be careful out there, love. I'd hate to have to avenge you — I have such a busy schedule.", Category = "farewell", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationMarried });
            lines.Add(new() { Id = "fw_cu_f1", Text = "Until next time. I'll be watching — but then, I always am.", Category = "farewell", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "fw_cu_f2", Text = "Keep your eyes open and your mouth shut. That's free advice — the good kind.", Category = "farewell", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "fw_cu_n1", Text = "Going? Fine. I'll know where to find you if I need to.", Category = "farewell", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "fw_cu_s1", Text = "Run along. But know this — I never forget a face. Or a slight.", Category = "farewell", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationSuspicious });
            lines.Add(new() { Id = "fw_cu_e1", Text = "*smiles thinly* Goodbye for now. Emphasis on 'for now.'", Category = "farewell", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationEnemy });

            // ═══ PIOUS ═══
            lines.Add(new() { Id = "fw_pi_m1", Text = "Go with my love and the gods' blessing. Both are with you always.", Category = "farewell", PersonalityType = "pious", RelationshipTier = GameConfig.RelationMarried });
            lines.Add(new() { Id = "fw_pi_f1", Text = "May the light guide your steps, dear friend. I'll keep you in my prayers.", Category = "farewell", PersonalityType = "pious", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "fw_pi_f2", Text = "Be well, {player_name}. Remember — you are never truly alone. The divine walks with you.", Category = "farewell", PersonalityType = "pious", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "fw_pi_n1", Text = "Blessings upon your journey, traveler. May you find what you seek.", Category = "farewell", PersonalityType = "pious", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "fw_pi_n2", Text = "Peace be with you, child. The world is hard, but kindness makes it bearable.", Category = "farewell", PersonalityType = "pious", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "fw_pi_a1", Text = "I will pray for your soul, {player_name}. You need it more than you know.", Category = "farewell", PersonalityType = "pious", RelationshipTier = GameConfig.RelationAnger });

            // ═══ SCHOLARLY ═══
            lines.Add(new() { Id = "fw_sc_m1", Text = "Go on, darling. I'll be here. Probably won't notice you left until I finish this chapter.", Category = "farewell", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationMarried });
            lines.Add(new() { Id = "fw_sc_f1", Text = "Farewell! If you encounter anything unusual in the dungeon, TAKE NOTES. Please.", Category = "farewell", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "fw_sc_f2", Text = "Until next time. I have approximately forty-seven things to research before then.", Category = "farewell", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "fw_sc_n1", Text = "*already turning back to their book* Hmm? Oh, yes, goodbye. The door is... somewhere behind you.", Category = "farewell", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "fw_sc_n2", Text = "Farewell. Try to learn something new today. It's the least we can do.", Category = "farewell", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationNormal });

            // ═══ CYNICAL ═══
            lines.Add(new() { Id = "fw_cy_m1", Text = "Go. Be careful. And don't expect me to say anything mushier than that.", Category = "farewell", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationMarried });
            lines.Add(new() { Id = "fw_cy_f1", Text = "Off you go. Try not to do anything stupid. *beat* Stupider than usual, I mean.", Category = "farewell", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "fw_cy_f2", Text = "See you around. Or not. The world doesn't really care either way, does it?", Category = "farewell", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "fw_cy_n1", Text = "You're leaving? Best decision you've made all day.", Category = "farewell", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "fw_cy_n2", Text = "Bye. Try not to make anything worse on your way out.", Category = "farewell", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "fw_cy_a1", Text = "Good riddance. And I mean that from the bottom of my cold, dead heart.", Category = "farewell", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationAnger });

            // ═══ CHARMING ═══
            lines.Add(new() { Id = "fw_ch_m1", Text = "*dramatic bow* Parting is such sweet sorrow! I shall pine! I shall wilt! I shall probably have a snack.", Category = "farewell", PersonalityType = "charming", RelationshipTier = GameConfig.RelationMarried });
            lines.Add(new() { Id = "fw_ch_l1", Text = "Go on, you magnificent creature. The world isn't going to save itself. ...Unless I do it first.", Category = "farewell", PersonalityType = "charming", RelationshipTier = GameConfig.RelationLove });
            lines.Add(new() { Id = "fw_ch_f1", Text = "Until we meet again! Which, knowing my luck, will be in some ridiculous life-threatening situation.", Category = "farewell", PersonalityType = "charming", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "fw_ch_f2", Text = "Stay dazzling, {player_name}! Though next to me, that's a tall order.", Category = "farewell", PersonalityType = "charming", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "fw_ch_n1", Text = "Ciao! That means 'goodbye' in... actually, I have no idea what language that is. But it sounds great, right?", Category = "farewell", PersonalityType = "charming", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "fw_ch_n2", Text = "*winks* See you around, gorgeous. And remember — life's a party, dress accordingly.", Category = "farewell", PersonalityType = "charming", RelationshipTier = GameConfig.RelationNormal });

            // ═══ STOIC ═══
            lines.Add(new() { Id = "fw_st_m1", Text = "*squeezes your hand once, firmly, then lets go* ...Go.", Category = "farewell", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationMarried });
            lines.Add(new() { Id = "fw_st_f1", Text = "*nods* Stay sharp.", Category = "farewell", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "fw_st_f2", Text = "Watch your back out there. The dungeon doesn't forgive mistakes.", Category = "farewell", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "fw_st_n1", Text = "*brief nod*", Category = "farewell", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "fw_st_n2", Text = "...Goodbye.", Category = "farewell", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "fw_st_e1", Text = "*says nothing, just turns and walks away*", Category = "farewell", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationEnemy });

            return lines;
        }
    }
}
