using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Town NPC Story System - Manages memorable NPCs with mini-arcs
    /// that unfold over time, giving the town a sense of living narrative.
    ///
    /// These are not random NPCs but specific, memorable characters with
    /// their own small stories that intersect with the player's journey.
    /// </summary>
    public class TownNPCStorySystem
    {
        private static TownNPCStorySystem? _instance;
        public static TownNPCStorySystem Instance => _instance ??= new TownNPCStorySystem();

        // Track story progress for each memorable NPC
        public Dictionary<string, MemorableNPCState> NPCStates { get; private set; } = new();

        // Track player's relationship with memorable NPCs
        public Dictionary<string, int> NPCRelationship { get; private set; } = new();

        /// <summary>
        /// All memorable town NPCs with their stories
        /// </summary>
        public static readonly Dictionary<string, MemorableNPCData> MemorableNPCs = new()
        {
            // ====== THE WOUNDED SOLDIER ======
            ["Marcus_WoundedSoldier"] = new MemorableNPCData
            {
                Name = "Marcus",
                Title = "The Wounded Soldier",
                Location = "Healer",
                Description = "A grizzled veteran with a bandaged eye and a haunted expression",

                Story = "Marcus was a captain of the King's Guard who returned from a mission " +
                       "to investigate strange lights in the dungeon. He alone survived. " +
                       "The experience broke something in him - he saw one of the Old Gods.",

                StoryStages = new[]
                {
                    new NPCStoryStage
                    {
                        StageId = 0,
                        Name = "The Broken Man",
                        Trigger = "First visit to Healer",
                        Dialogue = new[] {
                            "Don't go down there. *coughs* I'm serious. The dungeon... it's not just monsters.",
                            "I saw... something. On floor 25. It spoke to me. It KNEW me.",
                            "The others laughed. Said I was a coward. They're all dead now."
                        }
                    },
                    new NPCStoryStage
                    {
                        StageId = 1,
                        Name = "Warning",
                        Trigger = "Player reaches level 15",
                        Dialogue = new[] {
                            "You're getting stronger. I can tell. You have that look.",
                            "When you reach floor 25... you'll understand. The Broken Blade waits there.",
                            "Maelketh. That's his name. The god of war. He's... not what you expect."
                        }
                    },
                    new NPCStoryStage
                    {
                        StageId = 2,
                        Name = "Confession",
                        Trigger = "Player defeats Maelketh",
                        Dialogue = new[] {
                            "*grabs your arm* You... you killed him? Or... saved him?",
                            "I couldn't fight. When I saw him, I just... ran. I left my squad behind.",
                            "All these years, I've been waiting for someone to finish what I couldn't start."
                        },
                        OnComplete = "Marcus_Healed" // Triggers resolution
                    },
                    new NPCStoryStage
                    {
                        StageId = 3,
                        Name = "Peace",
                        Trigger = "After confession",
                        Dialogue = new[] {
                            "*standing straighter* I've made a decision. I'm going back to the dungeon.",
                            "Not to fight. To remember. To honor those I left behind.",
                            "Thank you. You gave me something I lost down there - hope."
                        },
                        Reward = new NPCReward { ChivalryBonus = 50, ItemId = "medal_of_the_fallen" }
                    }
                }
            },

            // ====== THE GRIEVING MOTHER ======
            ["Elena_GrievingMother"] = new MemorableNPCData
            {
                Name = "Elena",
                Title = "The Grieving Mother",
                Location = "Temple",
                Description = "A woman in faded black, always praying, never leaving",

                Story = "Elena's daughter was taken by the Shadows gang for unpaid debts. " +
                       "She's been praying in the temple ever since, unable to act, " +
                       "hoping someone will help her.",

                StoryStages = new[]
                {
                    new NPCStoryStage
                    {
                        StageId = 0,
                        Name = "Silent Prayer",
                        Trigger = "First visit to Temple",
                        Dialogue = new[] {
                            "*doesn't look up* Please... if the gods can hear me... bring her back...",
                            "*notices you* Oh. Forgive me. I thought I was alone.",
                            "My daughter. The Shadows took her. For debts my husband left behind."
                        }
                    },
                    new NPCStoryStage
                    {
                        StageId = 1,
                        Name = "Desperation",
                        Trigger = "Player has 1000+ gold",
                        Dialogue = new[] {
                            "The debt was 500 gold. I've raised 200. But they want more now. Interest.",
                            "They say 1000 gold. I'll never... I can never...",
                            "*quietly* Would you... could you...? No. I can't ask that of a stranger."
                        },
                        Choice = new NPCChoice
                        {
                            Prompt = "Pay Elena's debt (1000 gold)?",
                            Options = new[]
                            {
                                new NPCChoiceOption { Key = "pay", Text = "Pay the debt", GoldCost = 1000, Chivalry = 200 },
                                new NPCChoiceOption { Key = "refuse", Text = "Refuse", Darkness = 20 }
                            }
                        }
                    },
                    new NPCStoryStage
                    {
                        StageId = 2,
                        Name = "Rescue",
                        Trigger = "After paying debt",
                        Dialogue = new[] {
                            "*tears streaming* They... they let her go. You paid. WHY?",
                            "You don't even know me. You don't know Sarah.",
                            "The gods... they sent you. They must have. Thank you. THANK YOU."
                        }
                    },
                    new NPCStoryStage
                    {
                        StageId = 3,
                        Name = "Gratitude",
                        Trigger = "Next visit after rescue",
                        Dialogue = new[] {
                            "*with a young woman* This is Sarah. She wanted to meet you.",
                            "We're leaving Dorashire. Starting over somewhere safer.",
                            "Before we go... my husband had this. He would have wanted you to have it."
                        },
                        Reward = new NPCReward { ItemId = "ring_of_the_good_soul", Wisdom = 3 }
                    },
                    new NPCStoryStage // Alternative if refused
                    {
                        StageId = -1,
                        Name = "Despair",
                        Trigger = "Refused to help",
                        Dialogue = new[] {
                            "*hollow voice* I understand. It was too much to ask.",
                            "They've... they've sold her. To someone in the dungeon. For 'entertainment'.",
                            "I have nothing left to pray for."
                        }
                    }
                }
            },

            // ====== THE PHILOSOPHICAL DRUNK ======
            ["Bartholomew_Drunk"] = new MemorableNPCData
            {
                Name = "Bartholomew",
                Title = "The Philosophical Drunk",
                Location = "Inn",
                Description = "A disheveled man who always has a drink and a theory",

                Story = "Bartholomew was once a scholar at the Academy of Stars before " +
                       "his theories about the Ocean Philosophy got him expelled. " +
                       "Now he drinks and shares his wisdom with anyone who'll listen.",

                StoryStages = new[]
                {
                    new NPCStoryStage
                    {
                        StageId = 0,
                        Name = "First Meeting",
                        Trigger = "First visit to Inn",
                        Dialogue = new[] {
                            "*hiccup* You! Yes, you with the face. Sit down. I have questions.",
                            "What are you? No, really. WHAT are you? Not WHO. WHAT.",
                            "We're all just... vibrations, you know? Waves in an ocean of... *falls asleep*"
                        }
                    },
                    new NPCStoryStage
                    {
                        StageId = 1,
                        Name = "Philosophy 101",
                        Trigger = "Player has Awakening Level 1+",
                        Dialogue = new[] {
                            "*suddenly lucid* You've felt it, haven't you? The edges blurring?",
                            "The Academy called me mad. Said the Ocean Philosophy was heresy.",
                            "But I've seen the truth! We're not separate beings - we're the same being, dreaming!"
                        },
                        AwakeningGain = 1
                    },
                    new NPCStoryStage
                    {
                        StageId = 2,
                        Name = "The Manuscript",
                        Trigger = "Player has Awakening Level 3+",
                        Dialogue = new[] {
                            "*whispers* I have something. Hidden. My life's work.",
                            "A manuscript about Manwe. About what he really is. What WE really are.",
                            "It's too dangerous to keep. The Faith would burn it. The Crown would bury it."
                        },
                        Choice = new NPCChoice
                        {
                            Prompt = "Accept the manuscript?",
                            Options = new[]
                            {
                                new NPCChoiceOption { Key = "accept", Text = "Take the manuscript" },
                                new NPCChoiceOption { Key = "refuse", Text = "It's too dangerous" }
                            }
                        }
                    },
                    new NPCStoryStage
                    {
                        StageId = 3,
                        Name = "Truth",
                        Trigger = "Accepted manuscript",
                        Dialogue = new[] {
                            "*hands you a worn book* 'On the Nature of the Dreamer and the Dream'",
                            "Read it when you're ready. It will change everything.",
                            "*smiles* Now go. Prove I wasn't just a drunk with delusions."
                        },
                        Reward = new NPCReward
                        {
                            ItemId = "manuscript_of_truth",
                            WaveFragment = WaveFragment.ManwesChoice
                        }
                    }
                }
            },

            // ====== THE FORMER ADVENTURER ======
            ["Greta_FormerAdventurer"] = new MemorableNPCData
            {
                Name = "Greta",
                Title = "The Former Adventurer",
                Location = "WeaponShop",
                Description = "A one-armed woman who tests weapons with her remaining hand",

                Story = "Greta was the greatest dungeon delver of her generation until " +
                       "she reached floor 70 and encountered Noctura. She returned " +
                       "missing an arm and most of her memories of what happened.",

                StoryStages = new[]
                {
                    new NPCStoryStage
                    {
                        StageId = 0,
                        Name = "Testing Blades",
                        Trigger = "First visit to Weapon Shop",
                        Dialogue = new[] {
                            "*swings a sword one-handed with perfect form* Hmm. Balance is off.",
                            "You're wondering about the arm. Everyone does.",
                            "I don't remember losing it. Just... waking up without it. And a voice in my head."
                        }
                    },
                    new NPCStoryStage
                    {
                        StageId = 1,
                        Name = "Memories",
                        Trigger = "Player reaches level 30",
                        Dialogue = new[] {
                            "You're getting deep into the dungeon. I can smell it on you.",
                            "I made it to floor 70 once. There's... someone there. A woman. Maybe a goddess.",
                            "She offered me a deal. I don't remember what I answered. Just waking up... changed."
                        }
                    },
                    new NPCStoryStage
                    {
                        StageId = 2,
                        Name = "Warning",
                        Trigger = "Player reaches floor 60+",
                        Dialogue = new[] {
                            "*grabs you* Listen. Noctura - the Shadow Weaver - she's not like the others.",
                            "She doesn't want to fight you. She wants to USE you.",
                            "I think... I think I made a deal with her. And I'm still paying the price."
                        }
                    },
                    new NPCStoryStage
                    {
                        StageId = 3,
                        Name = "Truth",
                        Trigger = "Player encounters Noctura",
                        Dialogue = new[] {
                            "*voice trembling* You've met her. I can see it in your eyes.",
                            "What did she offer you? No - don't tell me. It's always the same thing.",
                            "She offers you what you want most. And takes something you don't know you have."
                        }
                    },
                    new NPCStoryStage
                    {
                        StageId = 4,
                        Name = "Closure",
                        Trigger = "Player allies with or defeats Noctura",
                        Dialogue = new[] {
                            "*sighs* It's over then. One way or another.",
                            "I remember now. What I gave her. My doubt. My hesitation.",
                            "She made me fearless. But fearless isn't the same as brave. Here - take this."
                        },
                        Reward = new NPCReward
                        {
                            ItemId = "gretas_blade",
                            Dexterity = 5
                        }
                    }
                }
            },

            // ====== THE ORPHAN THIEF ======
            ["Pip_OrphanThief"] = new MemorableNPCData
            {
                Name = "Pip",
                Title = "The Orphan Thief",
                Location = "MainStreet",
                Description = "A quick-fingered child who survives by her wits",

                Story = "Pip is one of the countless orphans left by the war between " +
                       "the factions. She steals to survive, but dreams of something more. " +
                       "The player can mentor her or ignore her.",

                StoryStages = new[]
                {
                    new NPCStoryStage
                    {
                        StageId = 0,
                        Name = "The Lift",
                        Trigger = "Random on Main Street (player has 100+ gold)",
                        Dialogue = new[] {
                            "*bumps into you* Sorry, mister/miss! Didn't see you there!",
                            "*You notice your purse feels lighter*",
                            "*A small figure disappears into an alley*"
                        },
                        GoldLost = 50
                    },
                    new NPCStoryStage
                    {
                        StageId = 1,
                        Name = "Caught",
                        Trigger = "Next visit to Main Street",
                        Dialogue = new[] {
                            "*cornered* Okay, okay! You caught me! Please don't tell the guards!",
                            "I'll give it back! Most of it! I already spent some on food...",
                            "Don't look at me like that. You try living on these streets at my age."
                        },
                        Choice = new NPCChoice
                        {
                            Prompt = "What do you do?",
                            Options = new[]
                            {
                                new NPCChoiceOption { Key = "forgive", Text = "Let her keep it", Chivalry = 30 },
                                new NPCChoiceOption { Key = "mentor", Text = "Offer to teach her to fight", Chivalry = 50 },
                                new NPCChoiceOption { Key = "guards", Text = "Turn her in to guards", Darkness = 30 }
                            }
                        }
                    },
                    new NPCStoryStage
                    {
                        StageId = 2,
                        Name = "Student",
                        Trigger = "Chose to mentor",
                        Dialogue = new[] {
                            "You... you'd really teach me? Why?",
                            "Everyone else just hits me and calls me street trash.",
                            "*eyes shining* I'll work hard! I promise! When do we start?"
                        }
                    },
                    new NPCStoryStage
                    {
                        StageId = 3,
                        Name = "Growth",
                        Trigger = "20+ days after mentoring started",
                        Dialogue = new[] {
                            "*practicing sword forms in an alley* Oh! I didn't hear you coming!",
                            "Look what I can do now! *demonstrates a passable defensive stance*",
                            "I've stopped stealing. Mostly. Trying to find honest work."
                        }
                    },
                    new NPCStoryStage
                    {
                        StageId = 4,
                        Name = "Hero",
                        Trigger = "Player reaches level 50+",
                        Dialogue = new[] {
                            "*approaches in worn but clean clothes* Teacher.",
                            "I got a job. At the weapon shop. Greta hired me. Said you recommended me.",
                            "I want to be like you someday. Someone who helps people. Thank you."
                        },
                        Reward = new NPCReward
                        {
                            ChivalryBonus = 100,
                            WaveFragment = WaveFragment.TheReturn // Teaching shows that wisdom lives on
                        }
                    }
                }
            },

            // ====== THE DYING PROPHET ======
            ["Ezra_DyingProphet"] = new MemorableNPCData
            {
                Name = "Ezra",
                Title = "The Dying Prophet",
                Location = "Church",
                Description = "An ancient priest who speaks in riddles and prophecy",

                Story = "Ezra has served the Faith for over sixty years. He's dying now, " +
                       "but his gift of prophecy has never been stronger. He knows " +
                       "more than he should about the player's true nature.",

                StoryStages = new[]
                {
                    new NPCStoryStage
                    {
                        StageId = 0,
                        Name = "First Vision",
                        Trigger = "First visit to Church",
                        Dialogue = new[] {
                            "*blind eyes staring through you* I wondered when you would come.",
                            "The wave walks among us. Dreaming it is mortal. Dreaming it is alone.",
                            "Forgive me. Sometimes I see too much and say too little. Or vice versa."
                        }
                    },
                    new NPCStoryStage
                    {
                        StageId = 1,
                        Name = "Prophecy",
                        Trigger = "Player has collected 1+ seals",
                        Dialogue = new[] {
                            "*clutches your hand* Seven seals. Seven truths. Seven steps to waking.",
                            "You've found one. Or it found you. It matters not which.",
                            "When you hold them all, look in a mirror. See if you recognize the face."
                        }
                    },
                    new NPCStoryStage
                    {
                        StageId = 2,
                        Name = "Revelation",
                        Trigger = "Player has Awakening Level 4+",
                        Dialogue = new[] {
                            "*laughing and weeping* You're starting to remember. I can feel it.",
                            "Before I die - I need you to know - we always knew.",
                            "The Faith. The ORIGINAL faith. We remember what Manwe forgot."
                        }
                    },
                    new NPCStoryStage
                    {
                        StageId = 3,
                        Name = "Passing",
                        Trigger = "Player reaches level 80+",
                        Dialogue = new[] {
                            "*on his deathbed* You came. Good. I've been... waiting...",
                            "The Ocean calls me home. Soon I will remember what I am.",
                            "You... are almost there. When you face Manwe... tell him... we forgive him..."
                        },
                        Reward = new NPCReward
                        {
                            WaveFragment = WaveFragment.TheReturn,
                            AwakeningMoment = AwakeningMoment.AcceptedDeath
                        },
                        OnComplete = "Ezra_Died"
                    }
                }
            }
        };

        public TownNPCStorySystem()
        {
            _instance = this;
            InitializeNPCStates();
        }

        private void InitializeNPCStates()
        {
            foreach (var npc in MemorableNPCs.Keys)
            {
                NPCStates[npc] = new MemorableNPCState
                {
                    NPCId = npc,
                    CurrentStage = 0,
                    CompletedStages = new HashSet<int>(),
                    ChoicesMade = new Dictionary<int, string>()
                };
                NPCRelationship[npc] = 0;
            }
        }

        /// <summary>
        /// Check if an NPC has an available encounter at this location
        /// </summary>
        public MemorableNPCData? GetAvailableNPCEncounter(string location, Character player)
        {
            foreach (var kvp in MemorableNPCs)
            {
                var npc = kvp.Value;
                var state = NPCStates[kvp.Key];

                if (npc.Location != location) continue;
                if (state.IsCompleted) continue;

                var nextStage = GetNextStage(kvp.Key, player);
                if (nextStage != null)
                    return npc;
            }
            return null;
        }

        /// <summary>
        /// Get the next story stage for an NPC based on triggers
        /// </summary>
        public NPCStoryStage? GetNextStage(string npcId, Character player)
        {
            if (!MemorableNPCs.TryGetValue(npcId, out var npc)) return null;
            if (!NPCStates.TryGetValue(npcId, out var state)) return null;

            foreach (var stage in npc.StoryStages)
            {
                if (state.CompletedStages.Contains(stage.StageId)) continue;

                // Check if trigger conditions are met
                if (IsTriggerMet(stage.Trigger, player, state))
                    return stage;
            }
            return null;
        }

        /// <summary>
        /// Check if a trigger condition is met
        /// Handles compound triggers like "Random on Main Street (player has 100+ gold)"
        /// </summary>
        private bool IsTriggerMet(string trigger, Character player, MemorableNPCState state)
        {
            // Parse trigger string - handle compound conditions
            // All conditions in a trigger must be met

            // First visit triggers
            if (trigger.StartsWith("First visit")) return state.CurrentStage == 0;

            // Choice-based triggers: "Chose to X"
            if (trigger.StartsWith("Chose to "))
            {
                var choiceKey = trigger.Replace("Chose to ", "").ToLower().Trim();
                // Check if any previous stage had this choice made
                foreach (var kvp in state.ChoicesMade)
                {
                    if (kvp.Value.ToLower() == choiceKey)
                        return true;
                }
                return false; // Choice wasn't made
            }

            // Next visit triggers - met if we're past the previous stage
            if (trigger.Contains("Next visit"))
            {
                return true;
            }

            // "After X" triggers - check if a specific choice was made or stage completed
            // Examples: "After confession", "After paying debt", "After rescue"
            if (trigger.StartsWith("After "))
            {
                var afterWhat = trigger.Replace("After ", "").ToLower().Trim();

                // Map common "after" triggers to the choice keys or previous stages
                // "After confession" = stage 2 was completed for Marcus
                // "After paying debt" = "pay" choice was made
                // "After rescue" = stage 2 was completed (which requires "pay" choice)

                // Check if it matches a choice that was made
                foreach (var kvp in state.ChoicesMade)
                {
                    // "After paying debt" -> check for "pay" choice
                    if (afterWhat.Contains("paying") && kvp.Value.ToLower() == "pay")
                        return true;
                    if (afterWhat.Contains("rescue") && kvp.Value.ToLower() == "pay")
                        return true;
                }

                // For generic "After X" where X relates to story progression,
                // check if previous stage was completed
                if (state.CurrentStage > 0)
                    return true;

                return false;
            }

            // "Refused to help" or similar negative choice triggers
            if (trigger.Contains("Refused"))
            {
                foreach (var kvp in state.ChoicesMade)
                {
                    if (kvp.Value.ToLower() == "refuse")
                        return true;
                }
                return false;
            }

            // "Accepted X" triggers - check if "accept" choice was made
            if (trigger.StartsWith("Accepted"))
            {
                foreach (var kvp in state.ChoicesMade)
                {
                    if (kvp.Value.ToLower() == "accept")
                        return true;
                }
                return false;
            }

            // Time-based triggers: "X+ days after Y"
            if (trigger.Contains("days after"))
            {
                var parts = trigger.Split(' ');
                int requiredDays = 0;
                foreach (var p in parts)
                {
                    if (int.TryParse(p.Replace("+", ""), out int days))
                    {
                        requiredDays = days;
                        break;
                    }
                }

                // Find the most recently completed stage to measure time from
                int lastCompletedDay = 0;
                if (state.StageCompletedOnDay.Count > 0)
                {
                    lastCompletedDay = state.StageCompletedOnDay.Values.Max();
                }

                int currentDay = DailySystemManager.Instance?.CurrentDay ?? 0;
                int daysSinceLastStage = currentDay - lastCompletedDay;

                return daysSinceLastStage >= requiredDays;
            }

            // For compound triggers, we need to check ALL conditions
            bool allConditionsMet = true;
            bool hasAnyCondition = false;

            // Check gold requirement: "100+ gold" or "player has 100+ gold"
            if (trigger.Contains("gold"))
            {
                hasAnyCondition = true;
                int requiredGold = 0;
                var parts = trigger.Split(' ');
                foreach (var p in parts)
                {
                    var cleanPart = p.Replace("+", "").Replace("(", "").Replace(")", "");
                    if (int.TryParse(cleanPart, out int gold))
                    {
                        requiredGold = gold;
                        break;
                    }
                }
                if (player.Gold < requiredGold)
                    allConditionsMet = false;
            }

            // Check level requirement: "level 50+" or "Player reaches level 50+"
            if (trigger.Contains("level") && !trigger.Contains("Awakening"))
            {
                hasAnyCondition = true;
                int requiredLevel = 0;
                var parts = trigger.Split(' ');
                foreach (var p in parts)
                {
                    var cleanPart = p.Replace("+", "").Replace("(", "").Replace(")", "");
                    if (int.TryParse(cleanPart, out int level))
                    {
                        requiredLevel = level;
                        break;
                    }
                }
                if (player.Level < requiredLevel)
                    allConditionsMet = false;
            }

            // Check awakening level
            if (trigger.Contains("Awakening Level"))
            {
                hasAnyCondition = true;
                var awakening = OceanPhilosophySystem.Instance?.AwakeningLevel ?? 0;
                var parts = trigger.Split(' ');
                foreach (var p in parts)
                {
                    if (int.TryParse(p.Replace("+", ""), out int level))
                    {
                        if (awakening < level)
                            allConditionsMet = false;
                        break;
                    }
                }
            }

            // Check seal count
            if (trigger.Contains("seals"))
            {
                hasAnyCondition = true;
                var seals = StoryProgressionSystem.Instance?.CollectedSeals?.Count ?? 0;
                var parts = trigger.Split(' ');
                foreach (var p in parts)
                {
                    if (int.TryParse(p.Replace("+", ""), out int count))
                    {
                        if (seals < count)
                            allConditionsMet = false;
                        break;
                    }
                }
            }

            // Check boss defeat - Maelketh
            if (trigger.Contains("defeats Maelketh"))
            {
                hasAnyCondition = true;
                if (!(StoryProgressionSystem.Instance?.HasFlag(StoryFlag.DefeatedMaelketh) ?? false))
                    allConditionsMet = false;
            }

            // Check Noctura encounters
            if (trigger.Contains("encounters Noctura"))
            {
                hasAnyCondition = true;
                // Check if player has encountered Noctura (reached floor 70 or has the story flag)
                var hasEncountered = StoryProgressionSystem.Instance?.HasFlag(StoryFlag.EncounteredNoctura) ?? false;
                if (!hasEncountered)
                {
                    // Fallback: check if player has reached floor 70+
                    var maxFloor = GameEngine.Instance?.CurrentPlayer?.Statistics?.DeepestDungeonLevel ?? 0;
                    if (maxFloor < 70)
                        allConditionsMet = false;
                }
            }

            // Check Noctura alliance or defeat
            if (trigger.Contains("allies with or defeats Noctura"))
            {
                hasAnyCondition = true;
                var allied = StoryProgressionSystem.Instance?.HasFlag(StoryFlag.AlliedNoctura) ?? false;
                var defeated = StoryProgressionSystem.Instance?.HasFlag(StoryFlag.DefeatedNoctura) ?? false;
                if (!allied && !defeated)
                    allConditionsMet = false;
            }

            // Check floor requirement: "floor 60+" or "reaches floor 60+"
            if (trigger.Contains("floor") && !trigger.Contains("Noctura"))
            {
                hasAnyCondition = true;
                int requiredFloor = 0;
                var parts = trigger.Split(' ');
                foreach (var p in parts)
                {
                    var cleanPart = p.Replace("+", "").Replace("(", "").Replace(")", "");
                    if (int.TryParse(cleanPart, out int floor))
                    {
                        requiredFloor = floor;
                        break;
                    }
                }

                // Check player's deepest dungeon level
                var maxFloor = GameEngine.Instance?.CurrentPlayer?.Statistics?.DeepestDungeonLevel ?? 0;
                if (maxFloor < requiredFloor)
                    allConditionsMet = false;
            }

            // Random chance - checked LAST so other conditions are verified first
            if (trigger.Contains("Random"))
            {
                hasAnyCondition = true;
                // Only roll if all other conditions are met
                if (allConditionsMet)
                {
                    if (new Random().Next(100) >= 30) // 30% chance
                        allConditionsMet = false;
                }
            }

            // If we found and checked at least one condition, return the result
            if (hasAnyCondition)
                return allConditionsMet;

            // Default: unknown trigger type, don't proceed
            GD.Print($"[TownNPC] Unknown trigger type: {trigger}");
            return false;
        }

        /// <summary>
        /// Complete a story stage
        /// </summary>
        public void CompleteStage(string npcId, int stageId, string? choiceMade = null)
        {
            if (!NPCStates.TryGetValue(npcId, out var state)) return;

            state.CompletedStages.Add(stageId);
            state.CurrentStage = stageId + 1;

            // Track when this stage was completed for time-based triggers
            state.StageCompletedOnDay[stageId] = DailySystemManager.Instance?.CurrentDay ?? 0;

            if (choiceMade != null)
                state.ChoicesMade[stageId] = choiceMade;

            var npc = MemorableNPCs[npcId];
            var stage = npc.StoryStages.FirstOrDefault(s => s.StageId == stageId);

            if (stage?.OnComplete != null)
            {
                // Handle completion flags
                StoryProgressionSystem.Instance?.SetStoryFlag(stage.OnComplete, true);
            }

            // Handle choices that end the NPC's story
            if (choiceMade != null)
            {
                // Pip: Turning her in to guards ends her story
                if (npcId == "Pip_OrphanThief" && choiceMade == "guards")
                {
                    state.CurrentStage = 99; // Mark as completed/gone
                    GD.Print($"[TownNPC] {npc.Name} was turned in to the guards - story ended");
                }
                // Pip: Forgiving her also ends the story (no further stages for that path)
                else if (npcId == "Pip_OrphanThief" && choiceMade == "forgive")
                {
                    state.CurrentStage = 99; // Mark as completed
                    GD.Print($"[TownNPC] {npc.Name} was forgiven and left - story ended");
                }
            }

            GD.Print($"[TownNPC] Completed stage {stageId} for {npc.Name}");
        }

        /// <summary>
        /// Serialize for save
        /// </summary>
        public TownNPCStorySaveData Serialize()
        {
            return new TownNPCStorySaveData
            {
                NPCStates = NPCStates.Values.Select(s => new NPCStateSaveData
                {
                    NPCId = s.NPCId,
                    CurrentStage = s.CurrentStage,
                    CompletedStages = s.CompletedStages.ToList(),
                    ChoicesMade = s.ChoicesMade,
                    StageCompletedOnDay = s.StageCompletedOnDay
                }).ToList(),
                NPCRelationship = NPCRelationship
            };
        }

        /// <summary>
        /// Deserialize from save
        /// </summary>
        public void Deserialize(TownNPCStorySaveData? data)
        {
            if (data == null) return;

            foreach (var saved in data.NPCStates)
            {
                if (NPCStates.ContainsKey(saved.NPCId))
                {
                    NPCStates[saved.NPCId] = new MemorableNPCState
                    {
                        NPCId = saved.NPCId,
                        CurrentStage = saved.CurrentStage,
                        CompletedStages = new HashSet<int>(saved.CompletedStages),
                        ChoicesMade = saved.ChoicesMade,
                        StageCompletedOnDay = saved.StageCompletedOnDay ?? new Dictionary<int, int>()
                    };
                }
            }

            NPCRelationship = data.NPCRelationship ?? new Dictionary<string, int>();
        }

        /// <summary>
        /// Reset all state for a new game
        /// </summary>
        public void Reset()
        {
            InitializeNPCStates();
            GD.Print("[TownNPC] System reset for new game");
        }
    }

    #region Data Classes

    public class MemorableNPCData
    {
        public string Name { get; set; } = "";
        public string Title { get; set; } = "";
        public string Location { get; set; } = "";
        public string Description { get; set; } = "";
        public string Story { get; set; } = "";
        public NPCStoryStage[] StoryStages { get; set; } = Array.Empty<NPCStoryStage>();
    }

    public class NPCStoryStage
    {
        public int StageId { get; set; }
        public string Name { get; set; } = "";
        public string Trigger { get; set; } = "";
        public string[] Dialogue { get; set; } = Array.Empty<string>();
        public NPCChoice? Choice { get; set; }
        public NPCReward? Reward { get; set; }
        public int GoldLost { get; set; }
        public int AwakeningGain { get; set; }
        public string? OnComplete { get; set; }
    }

    public class NPCChoice
    {
        public string Prompt { get; set; } = "";
        public NPCChoiceOption[] Options { get; set; } = Array.Empty<NPCChoiceOption>();
    }

    public class NPCChoiceOption
    {
        public string Key { get; set; } = "";
        public string Text { get; set; } = "";
        public int GoldCost { get; set; }
        public int Chivalry { get; set; }
        public int Darkness { get; set; }
    }

    public class NPCReward
    {
        public string? ItemId { get; set; }
        public int ChivalryBonus { get; set; }
        public int Wisdom { get; set; }
        public int Dexterity { get; set; }
        public WaveFragment? WaveFragment { get; set; }
        public AwakeningMoment? AwakeningMoment { get; set; }
    }

    public class MemorableNPCState
    {
        public string NPCId { get; set; } = "";
        public int CurrentStage { get; set; }
        public HashSet<int> CompletedStages { get; set; } = new();
        public Dictionary<int, string> ChoicesMade { get; set; } = new();
        public Dictionary<int, int> StageCompletedOnDay { get; set; } = new(); // Track when each stage was completed
        public bool IsCompleted => CurrentStage >= 99;
    }

    public class TownNPCStorySaveData
    {
        public List<NPCStateSaveData> NPCStates { get; set; } = new();
        public Dictionary<string, int> NPCRelationship { get; set; } = new();
    }

    public class NPCStateSaveData
    {
        public string NPCId { get; set; } = "";
        public int CurrentStage { get; set; }
        public List<int> CompletedStages { get; set; } = new();
        public Dictionary<int, string> ChoicesMade { get; set; } = new();
        public Dictionary<int, int> StageCompletedOnDay { get; set; } = new();
    }

    #endregion
}
