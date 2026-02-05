using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

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
        private static CycleDialogueSystem? _instance;
        public static CycleDialogueSystem Instance => _instance ??= new CycleDialogueSystem();

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
                "You again? No, wait... you're new. Aren't you?",
                "*stares* For a moment, I thought... never mind.",
                "The strangest feeling just came over me.",
                "My grandmother used to speak of souls that return..."
            },
            [4] = new[] { // Fourth cycle - growing awareness
                "I've been waiting for you. Why do I feel that way?",
                "Every time I see you, I forget I've seen you before.",
                "Do you believe in fate? I'm starting to.",
                "The pattern repeats. Even if we don't remember."
            },
            [5] = new[] { // Fifth+ cycle - full acknowledgment
                "Again. You're back again. How many times now?",
                "I remember you. I remember ALL of you.",
                "The cycle continues. Will it ever end?",
                "Welcome back, Dreamer. You always return."
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
                "Of course you know. You always know. You always have.",
                "I've stopped being surprised by what you know.",
                "Tell me - how many times have we had this conversation?"
            },
            [5] = new[] {
                "There's no point in explanations, is there? You remember everything.",
                "I wonder if I'm the same person I was last time we met. Are you?",
                "The only thing that changes is which of us remembers."
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
                    "You... I've been waiting for someone like you.",
                    "The stars told me you were coming. I didn't believe them.",
                    "Will you let me join you? I need to find the truth."
                },
                Cycle2Dialogue = new[] {
                    "We've met before. In a dream, perhaps?",
                    "My visions showed me your face long before today.",
                    "Something tells me this isn't our first journey together."
                },
                Cycle3PlusDialogue = new[] {
                    "Every cycle, I wait for you here. Every cycle, you come.",
                    "I remember dying for you. I'd do it again.",
                    "The stars don't just predict - they remember."
                }
            },

            ["recruit_aldric"] = new CycleMomentDialogue
            {
                MomentId = "recruit_aldric",
                FirstCycleDialogue = new[] {
                    "I've failed everyone I've protected. Maybe you'll be different.",
                    "My shield arm is still strong. Let me prove it.",
                    "I need a purpose. You seem to have one."
                },
                Cycle2Dialogue = new[] {
                    "I dreamed of this moment. Of you.",
                    "My instincts say I can trust you. I don't know why.",
                    "It feels like I've made this oath before."
                },
                Cycle3PlusDialogue = new[] {
                    "How many times have I sworn to protect you?",
                    "The shield remembers, even when I forget.",
                    "Again, then. Until we finally get it right."
                }
            },

            // ===== OLD GOD ENCOUNTERS =====
            ["encounter_maelketh"] = new CycleMomentDialogue
            {
                MomentId = "encounter_maelketh",
                FirstCycleDialogue = new[] {
                    "ANOTHER CHALLENGER. THEY NEVER STOP COMING.",
                    "DO YOU KNOW HOW MANY I'VE KILLED? I DON'T. I STOPPED COUNTING.",
                    "FIGHT ME. DIE. IT'S ALL I REMEMBER HOW TO DO."
                },
                Cycle2Dialogue = new[] {
                    "YOU. I'VE KILLED YOU BEFORE. OR DID YOU KILL ME?",
                    "THE BLOOD ON MY BLADE... SOME OF IT IS YOURS.",
                    "WE'VE DANCED THIS DANCE. THE STEPS ARE THE SAME."
                },
                Cycle3PlusDialogue = new[] {
                    "BROTHER. ENEMY. SELF. WHAT DOES IT MATTER?",
                    "HOW MANY TIMES MUST WE DO THIS?",
                    "PERHAPS THIS TIME... ONE OF US WILL FINALLY REST."
                }
            },

            ["encounter_noctura"] = new CycleMomentDialogue
            {
                MomentId = "encounter_noctura",
                FirstCycleDialogue = new[] {
                    "At last. I've been waiting for someone worthy.",
                    "The shadows have shown me many futures. You appear in all of them.",
                    "Shall we discuss what you really are?"
                },
                Cycle2Dialogue = new[] {
                    "You've returned. I wondered if you would.",
                    "Different face. Same soul. The cycle continues.",
                    "Do you remember me yet? Give it time."
                },
                Cycle3PlusDialogue = new[] {
                    "Again we meet at this junction. Again we must choose.",
                    "I've watched you die. I've watched you transcend. Both are beautiful.",
                    "This time, perhaps, you'll finally understand my gift."
                }
            },

            ["encounter_manwe"] = new CycleMomentDialogue
            {
                MomentId = "encounter_manwe",
                FirstCycleDialogue = new[] {
                    "You've come. I've waited so long.",
                    "Do you know what you are? No... not yet. But soon.",
                    "I'm tired, child. So very tired."
                },
                Cycle2Dialogue = new[] {
                    "Again. You always find your way back here.",
                    "The dream repeats. Have you learned anything?",
                    "I send myself out, and myself returns. Forever."
                },
                Cycle3PlusDialogue = new[] {
                    "How many cycles now? I've lost count.",
                    "Each time you come closer to remembering. Each time you hesitate.",
                    "This time... will you finally wake up? Or do we dance again?"
                }
            },

            // ===== KEY STORY MOMENTS =====
            ["first_seal"] = new CycleMomentDialogue
            {
                MomentId = "first_seal",
                FirstCycleDialogue = new[] {
                    "A seal... what is this power?",
                    "Something ancient stirs at your touch.",
                    "The seal recognizes you. But how?"
                },
                Cycle2Dialogue = new[] {
                    "The seal welcomes you back.",
                    "You've held this before. The stone remembers.",
                    "One of seven. Again."
                },
                Cycle3PlusDialogue = new[] {
                    "Yes, yes. The seals. Your old friends.",
                    "They've been waiting. They always wait.",
                    "Perhaps this time, you'll understand what they really are."
                }
            },

            ["all_seals"] = new CycleMomentDialogue
            {
                MomentId = "all_seals",
                FirstCycleDialogue = new[] {
                    "Seven seals. Seven truths. The pattern is complete.",
                    "The history of creation lies in your hands.",
                    "Now you know what happened. But do you know what you ARE?"
                },
                Cycle2Dialogue = new[] {
                    "Again, you've gathered them all.",
                    "The story never changes. Only your understanding of it.",
                    "Seven seals, held by the same hands, across endless time."
                },
                Cycle3PlusDialogue = new[] {
                    "These seals have seen more of you than you have of yourself.",
                    "Creation. War. Corruption. Binding. Fate. Regret. Truth.",
                    "And now, the eighth truth: There is no beginning. There is no end."
                }
            },

            // ===== ENDINGS =====
            ["ending_usurper"] = new CycleMomentDialogue
            {
                MomentId = "ending_usurper",
                FirstCycleDialogue = new[] {
                    "POWER. ABSOLUTE POWER. IT'S ALL THAT MATTERS.",
                    "I CONSUME. I BECOME. I RULE.",
                    "LET THE CYCLE BREAK ON MY TERMS."
                },
                Cycle2Dialogue = new[] {
                    "I've done this before. I'll do it again.",
                    "Power corrupts. I welcome the corruption.",
                    "Perhaps THIS time, it will be enough."
                },
                Cycle3PlusDialogue = new[] {
                    "How many times have I become the monster?",
                    "The throne awaits. It always awaits.",
                    "Fine. If the cycle demands a tyrant, I'll BE the tyrant."
                }
            },

            ["ending_savior"] = new CycleMomentDialogue
            {
                MomentId = "ending_savior",
                FirstCycleDialogue = new[] {
                    "The gods can be healed. I believe it.",
                    "Love endures. That's the only truth I need.",
                    "I choose mercy. I choose hope."
                },
                Cycle2Dialogue = new[] {
                    "I saved them once. I'll save them again.",
                    "The path of light never gets easier. Just more familiar.",
                    "Every cycle, I choose love. Every cycle, it costs everything."
                },
                Cycle3PlusDialogue = new[] {
                    "Salvation without end. Is that mercy or cruelty?",
                    "I keep saving a world that keeps needing saving.",
                    "Perhaps the real salvation... is breaking the cycle itself."
                }
            },

            ["ending_true"] = new CycleMomentDialogue
            {
                MomentId = "ending_true",
                FirstCycleDialogue = new[] {
                    "I understand now. I am the wave. I am the ocean.",
                    "Not destruction. Not salvation. Dissolution.",
                    "Time to wake up."
                },
                Cycle2Dialogue = new[] {
                    "Again I reach this moment. Again I hesitate.",
                    "Waking up means ending the dream. Am I ready?",
                    "The ocean calls. It has always been calling."
                },
                Cycle3PlusDialogue = new[] {
                    "Every cycle brings me here. To this choice.",
                    "Perhaps THIS time, I'll have the courage to dissolve.",
                    "The wave does not fear becoming water. Only remembering."
                }
            }
        };

        public CycleDialogueSystem()
        {
            _instance = this;
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
                return "You feel like you've been here before. The feeling fades quickly.";
            }
            else if (CurrentCycle == 3)
            {
                return "The paths seem familiar. The choices... you've made them before.";
            }
            else
            {
                return "Another cycle begins. Another chance to remember.";
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
                return $"{originalDialogue}\n\n*Their eyes hold the weight of countless cycles*";
            }

            if (isWiseNPC && CurrentCycle >= 3)
            {
                var additions = new[] {
                    "\n\n*pauses* \"Wait... haven't we...?\"",
                    "\n\n\"Forgive me. I felt a strange echo just now.\"",
                    "\n\n\"The words I'm about to say... they feel worn, somehow.\"",
                    "\n\n*distant look* \"Dreams within dreams within dreams...\""
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
