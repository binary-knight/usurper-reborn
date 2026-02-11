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

            lines.Add(new() { Id = "rx_cv_ag1", Text = "HA! Did you SEE that?! That's how it's done! Who's next?!", Category = "reaction", PersonalityType = "aggressive", EventType = "combat_victory" });
            lines.Add(new() { Id = "rx_cv_ag2", Text = "*roars* THAT'S what I live for! More! Bring me MORE!", Category = "reaction", PersonalityType = "aggressive", EventType = "combat_victory" });
            lines.Add(new() { Id = "rx_cv_ag3", Text = "*wipes blood from face with a grin* Beautiful. Absolutely beautiful.", Category = "reaction", PersonalityType = "aggressive", EventType = "combat_victory" });

            lines.Add(new() { Id = "rx_cv_no1", Text = "Victory! But let us not celebrate — there will be harder battles ahead.", Category = "reaction", PersonalityType = "noble", EventType = "combat_victory" });
            lines.Add(new() { Id = "rx_cv_no2", Text = "Well fought, {player_name}. You carry yourself with honor on the battlefield.", Category = "reaction", PersonalityType = "noble", EventType = "combat_victory" });
            lines.Add(new() { Id = "rx_cv_no3", Text = "The day is ours. Let us press forward with renewed courage.", Category = "reaction", PersonalityType = "noble", EventType = "combat_victory" });

            lines.Add(new() { Id = "rx_cv_cu1", Text = "Excellent. Everything went exactly as I calculated.", Category = "reaction", PersonalityType = "cunning", EventType = "combat_victory" });
            lines.Add(new() { Id = "rx_cv_cu2", Text = "*already looting* While you're celebrating, I'm making this worthwhile.", Category = "reaction", PersonalityType = "cunning", EventType = "combat_victory" });

            lines.Add(new() { Id = "rx_cv_pi1", Text = "Thank the gods. We are blessed to have survived.", Category = "reaction", PersonalityType = "pious", EventType = "combat_victory" });
            lines.Add(new() { Id = "rx_cv_pi2", Text = "I take no pleasure in the kill, but I am grateful we still draw breath.", Category = "reaction", PersonalityType = "pious", EventType = "combat_victory" });

            lines.Add(new() { Id = "rx_cv_sc1", Text = "Fascinating! Did you see how it reacted when we flanked? I must document this.", Category = "reaction", PersonalityType = "scholarly", EventType = "combat_victory" });
            lines.Add(new() { Id = "rx_cv_sc2", Text = "Victory through superior tactics and preparation. As expected.", Category = "reaction", PersonalityType = "scholarly", EventType = "combat_victory" });

            lines.Add(new() { Id = "rx_cv_cy1", Text = "Great, we won. Now let's collect whatever's useful before something worse shows up.", Category = "reaction", PersonalityType = "cynical", EventType = "combat_victory" });
            lines.Add(new() { Id = "rx_cv_cy2", Text = "We're alive. Don't get excited — there'll be plenty more chances to die.", Category = "reaction", PersonalityType = "cynical", EventType = "combat_victory" });

            lines.Add(new() { Id = "rx_cv_ch1", Text = "And THAT, my friends, is how legends are born! You're welcome!", Category = "reaction", PersonalityType = "charming", EventType = "combat_victory" });
            lines.Add(new() { Id = "rx_cv_ch2", Text = "*takes a theatrical bow* I'd like to thank my weapon, my incredible reflexes, and my stunning good looks.", Category = "reaction", PersonalityType = "charming", EventType = "combat_victory" });

            lines.Add(new() { Id = "rx_cv_st1", Text = "*cleans weapon silently, nods once*", Category = "reaction", PersonalityType = "stoic", EventType = "combat_victory" });
            lines.Add(new() { Id = "rx_cv_st2", Text = "It's done. Keep moving.", Category = "reaction", PersonalityType = "stoic", EventType = "combat_victory" });

            // ═══════════════════════════════════════════════════════════════
            // COMBAT DEFEAT reactions
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "rx_cd_ag1", Text = "NO! Get up! We're not finished yet! GET UP!", Category = "reaction", PersonalityType = "aggressive", EventType = "combat_defeat" });
            lines.Add(new() { Id = "rx_cd_ag2", Text = "*punches the ground* DAMN IT! I'll rip them apart next time!", Category = "reaction", PersonalityType = "aggressive", EventType = "combat_defeat" });

            lines.Add(new() { Id = "rx_cd_no1", Text = "Retreat is not defeat — it is wisdom. We will return stronger.", Category = "reaction", PersonalityType = "noble", EventType = "combat_defeat" });
            lines.Add(new() { Id = "rx_cd_no2", Text = "We must regroup. There is no shame in living to fight another day.", Category = "reaction", PersonalityType = "noble", EventType = "combat_defeat" });

            lines.Add(new() { Id = "rx_cd_cu1", Text = "This changes my calculations entirely. We need a different approach.", Category = "reaction", PersonalityType = "cunning", EventType = "combat_defeat" });
            lines.Add(new() { Id = "rx_cd_pi1", Text = "We live. That is the miracle. Let us be grateful and learn from this.", Category = "reaction", PersonalityType = "pious", EventType = "combat_defeat" });
            lines.Add(new() { Id = "rx_cd_sc1", Text = "Interesting... I underestimated the variables. Won't happen again.", Category = "reaction", PersonalityType = "scholarly", EventType = "combat_defeat" });
            lines.Add(new() { Id = "rx_cd_cy1", Text = "Called it. I knew this was a bad idea before we even started.", Category = "reaction", PersonalityType = "cynical", EventType = "combat_defeat" });
            lines.Add(new() { Id = "rx_cd_ch1", Text = "Okay, so that didn't go as planned. At all. But my hair still looks fantastic.", Category = "reaction", PersonalityType = "charming", EventType = "combat_defeat" });
            lines.Add(new() { Id = "rx_cd_st1", Text = "*breathing hard* ...Next time.", Category = "reaction", PersonalityType = "stoic", EventType = "combat_defeat" });

            // ═══════════════════════════════════════════════════════════════
            // COMBAT FLEE reactions
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "rx_cf_ag1", Text = "RUNNING?! We're RUNNING?! I didn't sign up for running!", Category = "reaction", PersonalityType = "aggressive", EventType = "combat_flee" });
            lines.Add(new() { Id = "rx_cf_ag2", Text = "Cowardice! I could have taken them! All of them!", Category = "reaction", PersonalityType = "aggressive", EventType = "combat_flee" });

            lines.Add(new() { Id = "rx_cf_no1", Text = "A tactical withdrawal. There's no dishonor in choosing the right moment to fight.", Category = "reaction", PersonalityType = "noble", EventType = "combat_flee" });
            lines.Add(new() { Id = "rx_cf_cu1", Text = "Smart. Live today, strike tomorrow. I approve.", Category = "reaction", PersonalityType = "cunning", EventType = "combat_flee" });
            lines.Add(new() { Id = "rx_cf_pi1", Text = "The gods preserve us. We were not meant to fall today.", Category = "reaction", PersonalityType = "pious", EventType = "combat_flee" });
            lines.Add(new() { Id = "rx_cf_sc1", Text = "The odds were 23-to-1 against us. Retreating was the only logical option.", Category = "reaction", PersonalityType = "scholarly", EventType = "combat_flee" });
            lines.Add(new() { Id = "rx_cf_cy1", Text = "Finally, someone making sense. Running is severely underrated as a strategy.", Category = "reaction", PersonalityType = "cynical", EventType = "combat_flee" });
            lines.Add(new() { Id = "rx_cf_ch1", Text = "We're not fleeing! We're... advancing in the opposite direction! Very bravely!", Category = "reaction", PersonalityType = "charming", EventType = "combat_flee" });
            lines.Add(new() { Id = "rx_cf_st1", Text = "*running in silence, saving breath for what matters*", Category = "reaction", PersonalityType = "stoic", EventType = "combat_flee" });

            // ═══════════════════════════════════════════════════════════════
            // ALLY DEATH reactions
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "rx_ad_ag1", Text = "*screams in rage* NO! THEY'LL PAY FOR THIS! EVERY LAST ONE OF THEM!", Category = "reaction", PersonalityType = "aggressive", EventType = "ally_death" });
            lines.Add(new() { Id = "rx_ad_ag2", Text = "*kneels, shaking with fury* Get up. GET UP! ...Damn it.", Category = "reaction", PersonalityType = "aggressive", EventType = "ally_death" });

            lines.Add(new() { Id = "rx_ad_no1", Text = "*removes helmet, bows head* They died with honor. We will honor them with victory.", Category = "reaction", PersonalityType = "noble", EventType = "ally_death" });
            lines.Add(new() { Id = "rx_ad_no2", Text = "Rest now, brave soul. Your sacrifice will not be forgotten. I swear it.", Category = "reaction", PersonalityType = "noble", EventType = "ally_death" });

            lines.Add(new() { Id = "rx_ad_cu1", Text = "*goes very still* ...That shouldn't have happened. I miscalculated. I NEVER miscalculate.", Category = "reaction", PersonalityType = "cunning", EventType = "ally_death" });

            lines.Add(new() { Id = "rx_ad_pi1", Text = "*tears flowing* Return to the light, dear one. You are free now. You are at peace.", Category = "reaction", PersonalityType = "pious", EventType = "ally_death" });
            lines.Add(new() { Id = "rx_ad_pi2", Text = "*whispers a prayer* May the gods welcome you home. We will carry your memory forward.", Category = "reaction", PersonalityType = "pious", EventType = "ally_death" });

            lines.Add(new() { Id = "rx_ad_sc1", Text = "*hands trembling* I... I studied every variable. This wasn't supposed... I can't...", Category = "reaction", PersonalityType = "scholarly", EventType = "ally_death" });

            lines.Add(new() { Id = "rx_ad_cy1", Text = "*looks away* ...Another one. Damn this place. Damn all of it.", Category = "reaction", PersonalityType = "cynical", EventType = "ally_death" });
            lines.Add(new() { Id = "rx_ad_cy2", Text = "This is why you don't get attached. This is exactly why.", Category = "reaction", PersonalityType = "cynical", EventType = "ally_death" });

            lines.Add(new() { Id = "rx_ad_ch1", Text = "*for once, no joke, no smile, just silence and a single tear*", Category = "reaction", PersonalityType = "charming", EventType = "ally_death" });
            lines.Add(new() { Id = "rx_ad_ch2", Text = "*voice breaking* Hey... hey, come on. This isn't funny anymore. Wake up. Please.", Category = "reaction", PersonalityType = "charming", EventType = "ally_death" });

            lines.Add(new() { Id = "rx_ad_st1", Text = "*closes their eyes gently* ...You fought well.", Category = "reaction", PersonalityType = "stoic", EventType = "ally_death" });
            lines.Add(new() { Id = "rx_ad_st2", Text = "*long silence, then picks up their weapon and keeps walking*", Category = "reaction", PersonalityType = "stoic", EventType = "ally_death" });

            return lines;
        }
    }
}
