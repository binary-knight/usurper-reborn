using System;
using System.Collections.Generic;
using System.Linq;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Cycle Dialogue System - Manages NG+ specific dialogue variations.
    ///
    /// As players complete multiple cycles, NPCs begin to show subtle awareness
    /// of the repetition. This creates an uncanny deja vu effect and reinforces
    /// the cyclical nature of the story.
    ///
    /// "Haven't I seen you before? No... that's not right. I've BEEN you before."
    /// </summary>
    public class CycleDialogueSystem
    {
        private static CycleDialogueSystem? _fallbackInstance;
        public static CycleDialogueSystem Instance
        {
            get
            {
                var ctx = UsurperRemake.Server.SessionContext.Current;
                if (ctx != null) return ctx.CycleDialogue;
                return _fallbackInstance ??= new CycleDialogueSystem();
            }
        }

        private readonly Random _random = new();

        /// <summary>
        /// Get the current cycle from the story system
        /// </summary>
        private int CurrentCycle => StoryProgressionSystem.Instance?.CurrentCycle ?? 1;

        /// <summary>
        /// Cycle-aware greetings that NPCs might say
        /// </summary>
        public static readonly Dictionary<int, string[]> CycleGreetings = new()
        {
            [1] = new[] { // First playthrough - normal greetings
                "Welcome, stranger.",
                "Haven't seen you around before.",
                "New in town, are you?",
                "Watch yourself out there."
            },
            [2] = new[] { // Second cycle - subtle deja vu
                "Welcome... have we met before?",
                "You look familiar. Very familiar.",
                "I could swear I've seen those eyes somewhere.",
                "Ah, a new face. Or... is it?"
            },
            [3] = new[] { // Third cycle - NPCs notice something wrong
                "You again? No, wait... youre new. Arent you?",
                "*stares* For a second I thought... never mind.",
                "Weirdest feeling just hit me.",
                "My gran used to talk about souls coming back..."
            },
            [4] = new[] { // Fourth cycle - growing awareness
                "I was waiting for you. Dont ask me how I knew.",
                "I keep forgetting Ive met you. Then I remember again.",
                "Youve been here before, havent you?",
                "This keeps happening. You keep showing up."
            },
            [5] = new[] { // Fifth+ cycle - full acknowledgment
                "Again. How many times now?",
                "I remember you. All the yous.",
                "Does this ever stop? The coming back?",
                "Yeah. I knew youd be here. You always are."
            }
        };

        /// <summary>
        /// NPC reactions when player mentions something they shouldn't know yet
        /// </summary>
        public static readonly Dictionary<int, string[]> MetaKnowledgeReactions = new()
        {
            [1] = new[] {
                "How would you know that?",
                "I haven't told anyone that...",
                "Who told you about that?"
            },
            [2] = new[] {
                "How did you... no. Lucky guess, surely.",
                "That's unsettling. How could you possibly know?",
                "I feel like you've heard this story before."
            },
            [3] = new[] {
                "You KNOW. Somehow, you always know.",
                "Stop. The way you finish my sentences... it's disturbing.",
                "Have we done this before? Be honest with me."
            },
            [4] = new[] {
                "Course you know. You always do.",
                "Im not even surprised anymore.",
                "How many times have we done this exact conversation?"
            },
            [5] = new[] {
                "No point explaining. You already know.",
                "Am I the same me as last time? Are you?",
                "You remember everything. I only remember pieces."
            }
        };

        /// <summary>
        /// Dialogue for specific memorable moments on repeated playthroughs
        /// </summary>
        public static readonly Dictionary<string, CycleMomentDialogue> CycleMoments = new()
        {
            // ===== COMPANION RECRUITMENT =====
            ["recruit_lyris"] = new CycleMomentDialogue
            {
                MomentId = "recruit_lyris",
                FirstCycleDialogue = new[] {
                    "You. Ive been waiting for someone like you.",
                    "I had a feeling youd show up. Dont ask me how.",
                    "Let me come with you. I need answers."
                },
                Cycle2Dialogue = new[] {
                    "Weve met. I know we have.",
                    "I saw your face in a dream. Weeks ago.",
                    "This isnt the first time, is it?"
                },
                Cycle3PlusDialogue = new[] {
                    "Im always here. Youre always coming. You know that.",
                    "I died for you last time. Id do it again.",
                    "Lets just go. We both know how this part works."
                }
            },

            ["recruit_aldric"] = new CycleMomentDialogue
            {
                MomentId = "recruit_aldric",
                FirstCycleDialogue = new[] {
                    "Everyone Ive protected ends up dead. Maybe youll be different.",
                    "I can still fight. Let me prove it.",
                    "I need something to do. You look like trouble."
                },
                Cycle2Dialogue = new[] {
                    "I had a dream about this. About you.",
                    "Something tells me I can trust you. Gut feeling.",
                    "Feel like Ive sworn this oath before."
                },
                Cycle3PlusDialogue = new[] {
                    "How many times have I done this? Sworn to protect you?",
                    "I dont remember but my arms do. Muscle memory.",
                    "Alright. Lets go. Again."
                }
            },

            // ===== OLD GOD ENCOUNTERS =====
            ["encounter_maelketh"] = new CycleMomentDialogue
            {
                MomentId = "encounter_maelketh",
                FirstCycleDialogue = new[] {
                    "ANOTHER ONE. THEY NEVER STOP COMING.",
                    "I STOPPED COUNTING THE DEAD A LONG TIME AGO.",
                    "FIGHT ME. THATS ALL I KNOW HOW TO DO."
                },
                Cycle2Dialogue = new[] {
                    "YOU. IVE KILLED YOU BEFORE. OR YOU KILLED ME.",
                    "THERES YOUR BLOOD ON MY BLADE. FROM LAST TIME.",
                    "SAME FIGHT. SAME OUTCOME. LETS GET ON WITH IT."
                },
                Cycle3PlusDialogue = new[] {
                    "YEAH. YOU AGAIN.",
                    "HOW MANY TIMES WE GONNA DO THIS?",
                    "JUST KILL ME ALREADY. OR LET ME KILL YOU. IM TIRED."
                }
            },

            ["encounter_noctura"] = new CycleMomentDialogue
            {
                MomentId = "encounter_noctura",
                FirstCycleDialogue = new[] {
                    "Finally. Took you long enough.",
                    "I see you in every future. All of them.",
                    "Want to know what you really are?"
                },
                Cycle2Dialogue = new[] {
                    "Youre back. Thought you might be.",
                    "Different face. Same person underneath.",
                    "You remember me yet? No? Give it time."
                },
                Cycle3PlusDialogue = new[] {
                    "Here we are again. Same crossroads.",
                    "Ive seen you die. Ive seen you figure it out. Both times.",
                    "Maybe this time youll actually listen to me."
                }
            },

            ["encounter_manwe"] = new CycleMomentDialogue
            {
                MomentId = "encounter_manwe",
                FirstCycleDialogue = new[] {
                    "Youre here. Took a long time.",
                    "You dont know what you are yet. But you will.",
                    "Im tired. Im so tired."
                },
                Cycle2Dialogue = new[] {
                    "Again. You always end up here.",
                    "Did you learn anything this time?",
                    "I keep sending myself out. I keep coming back."
                },
                Cycle3PlusDialogue = new[] {
                    "Lost count of the cycles. You?",
                    "Every time you get a little closer. Then you hesitate.",
                    "You gonna do it this time? Or we doing this again?"
                }
            },

            // ===== KEY STORY MOMENTS =====
            ["first_seal"] = new CycleMomentDialogue
            {
                MomentId = "first_seal",
                FirstCycleDialogue = new[] {
                    "A seal. Whats this?",
                    "Something old wakes up when you touch it.",
                    "It knows you. How does it know you?"
                },
                Cycle2Dialogue = new[] {
                    "Another seal. Feels familiar.",
                    "Youve held this before. You know you have.",
                    "One of seven. Here we go again."
                },
                Cycle3PlusDialogue = new[] {
                    "Right. The seals. You know the drill.",
                    "Six more to go.",
                    "You know what theyre for this time?"
                }
            },

            ["all_seals"] = new CycleMomentDialogue
            {
                MomentId = "all_seals",
                FirstCycleDialogue = new[] {
                    "Seven seals. The whole story.",
                    "Now you know what happened. How it all went wrong.",
                    "But do you know what YOU are?"
                },
                Cycle2Dialogue = new[] {
                    "All seven again.",
                    "Same story every time. You just understand it better.",
                    "Same seals. Same hands."
                },
                Cycle3PlusDialogue = new[] {
                    "Yeah. You got em all. Again.",
                    "You could probably recite the whole history from memory by now.",
                    "So what are you gonna DO about it this time?"
                }
            },

            // ===== ENDINGS =====
            ["ending_usurper"] = new CycleMomentDialogue
            {
                MomentId = "ending_usurper",
                FirstCycleDialogue = new[] {
                    "POWER. ALL OF IT. MINE.",
                    "I TAKE WHAT I WANT. THATS HOW THIS WORKS.",
                    "THE CYCLE ENDS WHEN I SAY IT ENDS."
                },
                Cycle2Dialogue = new[] {
                    "Done this before. Doing it again.",
                    "Corrupt? Sure. I dont care.",
                    "Maybe this time itll stick."
                },
                Cycle3PlusDialogue = new[] {
                    "How many times have I taken the throne? Lost count.",
                    "Its always here waiting for me.",
                    "Fine. Ill be the bad guy. Again. Whatever."
                }
            },

            ["ending_savior"] = new CycleMomentDialogue
            {
                MomentId = "ending_savior",
                FirstCycleDialogue = new[] {
                    "The gods can be healed. I know it.",
                    "Mercy. Thats the answer.",
                    "Im doing this. No matter what it costs."
                },
                Cycle2Dialogue = new[] {
                    "Saved em once. Ill do it again.",
                    "Doesnt get easier. Just more familiar.",
                    "Same choice every time. Same price."
                },
                Cycle3PlusDialogue = new[] {
                    "I keep saving them. They keep needing saving.",
                    "Is this mercy or am I just stuck?",
                    "Maybe the real answer is to stop the whole thing."
                }
            },

            ["ending_true"] = new CycleMomentDialogue
            {
                MomentId = "ending_true",
                FirstCycleDialogue = new[] {
                    "I get it now. I know what I am.",
                    "Not killing. Not saving. Just... ending it.",
                    "Time to wake up."
                },
                Cycle2Dialogue = new[] {
                    "Here again. Same moment. Same hesitation.",
                    "Waking up means its over. Really over.",
                    "Am I ready? I dont know."
                },
                Cycle3PlusDialogue = new[] {
                    "Same crossroads. Every time.",
                    "Maybe this time Ill actually go through with it.",
                    "Just let go. How hard can it be?"
                }
            }
        };

        public CycleDialogueSystem()
        {
            _fallbackInstance = this;
        }

        /// <summary>
        /// Get a cycle-appropriate greeting for an NPC
        /// </summary>
        public string GetCycleGreeting()
        {
            int effectiveCycle = Math.Min(CurrentCycle, 5);
            var greetings = CycleGreetings[effectiveCycle];
            return greetings[_random.Next(greetings.Length)];
        }

        /// <summary>
        /// Get NPC reaction to player showing meta-knowledge
        /// </summary>
        public string GetMetaKnowledgeReaction()
        {
            int effectiveCycle = Math.Min(CurrentCycle, 5);
            var reactions = MetaKnowledgeReactions[effectiveCycle];
            return reactions[_random.Next(reactions.Length)];
        }

        /// <summary>
        /// Get dialogue for a specific cycle moment
        /// </summary>
        public string[] GetCycleMomentDialogue(string momentId)
        {
            if (!CycleMoments.TryGetValue(momentId, out var moment))
                return new[] { "..." };

            if (CurrentCycle >= 3)
                return moment.Cycle3PlusDialogue;
            if (CurrentCycle == 2)
                return moment.Cycle2Dialogue;
            return moment.FirstCycleDialogue;
        }

        /// <summary>
        /// Check if the player has earned a specific title based on cycles
        /// </summary>
        public string GetCycleTitle()
        {
            return CurrentCycle switch
            {
                >= 10 => "The Eternal Dreamer",
                >= 7 => "The Awakened",
                >= 5 => "The Rememberer",
                >= 3 => "The Recurring",
                2 => "The Returned",
                _ => ""
            };
        }

        /// <summary>
        /// Get a hint about the cycle for new players
        /// </summary>
        public string? GetCycleHint(Character player)
        {
            if (CurrentCycle == 1) return null;

            // Give subtle hints to experienced players
            if (CurrentCycle == 2)
            {
                return "Weird feeling of deja vu. Goes away fast.";
            }
            else if (CurrentCycle == 3)
            {
                return "You know this place. Youve made these choices before.";
            }
            else
            {
                return "Here we go again.";
            }
        }

        /// <summary>
        /// Modify NPC dialogue based on cycle
        /// Returns modified dialogue or null if no modification needed
        /// </summary>
        public string? ModifyDialogueForCycle(string originalDialogue, string npcType)
        {
            if (CurrentCycle < 2) return null;

            // 20% chance to add cycle awareness
            if (_random.NextDouble() > 0.2) return null;

            // NPCs of certain types are more aware
            bool isWiseNPC = npcType == "priest" || npcType == "sage" || npcType == "prophet";
            bool isOldGod = npcType == "god";

            if (isOldGod)
            {
                // Gods always remember
                return $"{originalDialogue}\n\n*They stare at you like theyve seen you a hundred times before*";
            }

            if (isWiseNPC && CurrentCycle >= 3)
            {
                var additions = new[] {
                    "\n\n*pauses* \"Wait... havent we...?\"",
                    "\n\n\"Sorry. Got the strangest feeling just now.\"",
                    "\n\n\"Feels like Ive said this before. Have I said this before?\"",
                    "\n\n*stares off into the distance for a moment*"
                };
                return originalDialogue + additions[_random.Next(additions.Length)];
            }

            return null;
        }
    }

    #region Data Classes

    public class CycleMomentDialogue
    {
        public string MomentId { get; set; } = "";
        public string[] FirstCycleDialogue { get; set; } = Array.Empty<string>();
        public string[] Cycle2Dialogue { get; set; } = Array.Empty<string>();
        public string[] Cycle3PlusDialogue { get; set; } = Array.Empty<string>();
    }

    #endregion
}
