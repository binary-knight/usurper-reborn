using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using UsurperRemake.Utils;
using UsurperRemake.Data;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Visual Novel Dialogue System - Full VN-style conversations with NPCs
    /// Features branching dialogue, relationship progression, flirtation, romance,
    /// and intimacy paths. NPCs remember conversations and react accordingly.
    /// </summary>
    public class VisualNovelDialogueSystem
    {
        private static VisualNovelDialogueSystem? _instance;
        public static VisualNovelDialogueSystem Instance => _instance ??= new VisualNovelDialogueSystem();

        private TerminalEmulator? terminal;
        private Character? player;
        private NPC? currentNPC;
        private Random random = new();

        // Conversation memory and state
        private Dictionary<string, List<ConversationMemory>> npcMemories = new();
        private Dictionary<string, ConversationState> npcConversationStates = new();

        // Track topics discussed in current conversation to avoid repetition
        private HashSet<string> topicsDiscussedThisSession = new();
        private int flirtCountThisSession = 0;
        private int complimentCountThisSession = 0;

        // Charisma thresholds for romance modifiers
        private const int CHARISMA_LOW = 8;      // Below this: penalty
        private const int CHARISMA_AVERAGE = 12; // Neutral point
        private const int CHARISMA_HIGH = 16;    // Above this: bonus
        private const int CHARISMA_EXCEPTIONAL = 20; // Major bonus

        public VisualNovelDialogueSystem()
        {
            _instance = this;
        }

        /// <summary>
        /// Get conversation states for saving
        /// </summary>
        public List<UsurperRemake.Systems.ConversationStateData> GetConversationStatesForSave()
        {
            var result = new List<UsurperRemake.Systems.ConversationStateData>();
            foreach (var kvp in npcConversationStates)
            {
                result.Add(new UsurperRemake.Systems.ConversationStateData
                {
                    NPCId = kvp.Key,
                    FlirtSuccessCount = kvp.Value.FlirtSuccessCount,
                    LastFlirtWasPositive = kvp.Value.LastFlirtWasPositive,
                    TotalConversations = kvp.Value.TotalConversations,
                    PersonalQuestionsAsked = kvp.Value.PersonalQuestionsAsked,
                    HasConfessed = kvp.Value.HasConfessed,
                    ConfessionAccepted = kvp.Value.ConfessionAccepted,
                    TopicsDiscussed = new List<string>(kvp.Value.TopicsDiscussed),
                    LastConversationDate = kvp.Value.LastConversationDate
                });
            }
            return result;
        }

        /// <summary>
        /// Load conversation states from save data
        /// </summary>
        public void LoadConversationStates(List<UsurperRemake.Systems.ConversationStateData> data)
        {
            if (data == null) return;

            npcConversationStates.Clear();
            foreach (var stateData in data)
            {
                npcConversationStates[stateData.NPCId] = new ConversationState
                {
                    FlirtSuccessCount = stateData.FlirtSuccessCount,
                    LastFlirtWasPositive = stateData.LastFlirtWasPositive,
                    TotalConversations = stateData.TotalConversations,
                    PersonalQuestionsAsked = stateData.PersonalQuestionsAsked,
                    HasConfessed = stateData.HasConfessed,
                    ConfessionAccepted = stateData.ConfessionAccepted,
                    TopicsDiscussed = new HashSet<string>(stateData.TopicsDiscussed),
                    LastConversationDate = stateData.LastConversationDate
                };
            }
        }

        /// <summary>
        /// Calculate a charisma modifier for romance interactions.
        /// Returns a float between -0.25 (very low charisma) and +0.25 (exceptional charisma).
        /// Average charisma returns 0 (no modifier).
        /// </summary>
        private float GetCharismaModifier()
        {
            if (player == null) return 0f;

            long charisma = player.Charisma;

            if (charisma >= CHARISMA_EXCEPTIONAL)
                return 0.25f;  // +25% bonus - exceptionally charming
            else if (charisma >= CHARISMA_HIGH)
                return 0.15f;  // +15% bonus - quite charming
            else if (charisma >= CHARISMA_AVERAGE)
                return 0.05f;  // +5% slight bonus - above average
            else if (charisma >= CHARISMA_LOW)
                return -0.05f; // -5% slight penalty - below average
            else
                return -0.20f; // -20% penalty - socially awkward
        }

        /// <summary>
        /// Get a description of the player's charisma effect for dialogue flavor
        /// </summary>
        private string GetCharismaFlavorText(bool positive)
        {
            if (player == null) return "";

            long charisma = player.Charisma;

            if (positive)
            {
                if (charisma >= CHARISMA_EXCEPTIONAL)
                    return "Your natural charm makes your words irresistible.";
                else if (charisma >= CHARISMA_HIGH)
                    return "Your confident demeanor helps your case.";
                else if (charisma < CHARISMA_LOW)
                    return "Despite your awkward delivery...";
            }
            else
            {
                if (charisma < CHARISMA_LOW)
                    return "Your lack of social grace doesn't help.";
            }

            return "";
        }

        /// <summary>
        /// Start a full visual novel-style conversation with an NPC
        /// </summary>
        public async Task StartConversation(Character player, NPC npc, TerminalEmulator term)
        {
            this.player = player;
            this.currentNPC = npc;
            this.terminal = term;

            // Reset session tracking
            topicsDiscussedThisSession.Clear();
            flirtCountThisSession = 0;
            complimentCountThisSession = 0;

            // Get or create conversation state for this NPC
            if (!npcConversationStates.ContainsKey(npc.ID))
                npcConversationStates[npc.ID] = new ConversationState();

            // Get relationship status
            int relationLevel = RelationshipSystem.GetRelationshipStatus(player, npc);
            var romanceType = RomanceTracker.Instance.GetRelationType(npc.ID);

            // Display conversation header
            await ShowConversationHeader(npc, relationLevel, romanceType);

            // Show NPC's greeting based on relationship
            await ShowGreeting(npc, relationLevel, romanceType);

            // Main conversation loop
            bool continueConversation = true;
            while (continueConversation)
            {
                continueConversation = await ShowConversationOptions(npc, relationLevel, romanceType);

                // Update relation level for next iteration
                relationLevel = RelationshipSystem.GetRelationshipStatus(player, npc);
                romanceType = RomanceTracker.Instance.GetRelationType(npc.ID);
            }
        }

        /// <summary>
        /// Display the conversation header with NPC info
        /// </summary>
        private async Task ShowConversationHeader(NPC npc, int relationLevel, RomanceRelationType romanceType)
        {
            terminal!.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");

            // NPC name with relationship color
            string relColor = GetRelationColor(relationLevel);
            terminal.SetColor(relColor);
            string romanticStatus = romanceType != RomanceRelationType.None ? $" [{romanceType}]" : "";
            terminal.WriteLine($"║  {npc.Name2}{romanticStatus,-60}  ║");

            terminal.SetColor("bright_cyan");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            // NPC description
            terminal.SetColor("gray");
            terminal.WriteLine($"  Level {npc.Level} {npc.Race} {npc.Class}");

            // Physical description based on gender and traits
            string physicalDesc = GeneratePhysicalDescription(npc);
            terminal.SetColor("white");
            terminal.WriteLine($"  {physicalDesc}");
            terminal.WriteLine("");

            // DEBUG: Show relationship stats
            await ShowDebugRelationshipInfo(npc, relationLevel, romanceType);

            await Task.Delay(100);
        }

        /// <summary>
        /// Show debug information about relationship status
        /// </summary>
        private async Task ShowDebugRelationshipInfo(NPC npc, int relationLevel, RomanceRelationType romanceType)
        {
            var profile = npc.Brain?.Personality;
            var state = npcConversationStates.GetValueOrDefault(npc.ID) ?? new ConversationState();

            // Check attraction
            var playerGender = player!.Sex == CharacterSex.Female ? GenderIdentity.Female : GenderIdentity.Male;
            bool isAttracted = profile?.IsAttractedTo(playerGender) ?? true;

            // Get flirt receptiveness
            float flirtReceptiveness = profile?.GetFlirtReceptiveness(relationLevel, isAttracted) ?? 0.5f;
            flirtReceptiveness += GetCharismaModifier();
            flirtReceptiveness = Math.Clamp(flirtReceptiveness, 0.05f, 0.95f);

            // Calculate flirts needed for confession
            int flirtsNeeded = player.Charisma >= CHARISMA_EXCEPTIONAL ? 0 :
                               player.Charisma >= CHARISMA_HIGH ? 1 : 2;

            terminal.SetColor("dark_gray");
            terminal.WriteLine("  ─────────────────────────────────────────────────────────────────────────────");
            terminal.SetColor("yellow");
            terminal.WriteLine("  [DEBUG] Relationship Stats:");
            terminal.SetColor("white");
            terminal.WriteLine($"    Relation Level: {relationLevel} (0=Soulmate, 50=Neutral, 100=Hated)");
            terminal.WriteLine($"    Romance Type: {romanceType}");
            terminal.SetColor(isAttracted ? "bright_green" : "bright_red");
            terminal.WriteLine($"    Attracted to you: {(isAttracted ? "YES" : "NO")} (orientation: {profile?.Orientation})");
            terminal.SetColor("white");
            terminal.WriteLine($"    Your Charisma: {player.Charisma} (modifier: {GetCharismaModifier():+0.00;-0.00})");
            terminal.WriteLine($"    Flirt Success Count: {state.FlirtSuccessCount} (need {flirtsNeeded} for confession)");
            terminal.WriteLine($"    Flirt Receptiveness: {flirtReceptiveness:P0}");
            terminal.WriteLine($"    Has Confessed: {state.HasConfessed}, Accepted: {state.ConfessionAccepted}");
            terminal.SetColor("dark_gray");
            terminal.WriteLine("  ─────────────────────────────────────────────────────────────────────────────");
            terminal.WriteLine("");
        }

        /// <summary>
        /// Generate a physical description for the NPC
        /// </summary>
        private string GeneratePhysicalDescription(NPC npc)
        {
            var profile = npc.Brain?.Personality;
            string gender = npc.Sex == CharacterSex.Female ? "She" : "He";

            var adjectives = new List<string>();

            if (profile != null)
            {
                if (profile.Sensuality > 0.7f)
                    adjectives.Add("alluring");
                if (profile.Passion > 0.7f)
                    adjectives.Add("intense-eyed");
                if (profile.Aggression > 0.7f)
                    adjectives.Add("fierce-looking");
                if (profile.Sociability > 0.7f)
                    adjectives.Add("approachable");
                if (profile.Intelligence > 0.7f)
                    adjectives.Add("sharp-witted");
            }

            if (adjectives.Count == 0)
                adjectives.Add("unremarkable");

            string raceDesc = npc.Race switch
            {
                CharacterRace.Elf => "with graceful elven features",
                CharacterRace.Dwarf => "with sturdy dwarven build",
                CharacterRace.Orc => "with powerful orcish physique",
                CharacterRace.Hobbit => "with a small but nimble frame",
                CharacterRace.Troll => "with massive, intimidating stature",
                _ => "of average build"
            };

            return $"{gender} appears {string.Join(", ", adjectives)} {raceDesc}.";
        }

        /// <summary>
        /// Show NPC's greeting based on relationship
        /// </summary>
        private async Task ShowGreeting(NPC npc, int relationLevel, RomanceRelationType romanceType)
        {
            string greeting = GenerateContextualGreeting(npc, relationLevel, romanceType);

            terminal!.SetColor("yellow");
            terminal.WriteLine($"  {npc.Name2} says:");
            terminal.SetColor("white");
            terminal.WriteLine($"  \"{greeting}\"");
            terminal.WriteLine("");

            await Task.Delay(500);
        }

        /// <summary>
        /// Generate a contextual greeting based on relationship
        /// </summary>
        private string GenerateContextualGreeting(NPC npc, int relationLevel, RomanceRelationType romanceType)
        {
            // Check for romance relationship first
            if (romanceType == RomanceRelationType.Spouse)
                return GetSpouseGreeting(npc);
            if (romanceType == RomanceRelationType.Lover)
                return GetLoverGreeting(npc);
            if (romanceType == RomanceRelationType.FWB)
                return GetFWBGreeting(npc);

            // Standard relationship-based greetings
            return relationLevel switch
            {
                <= 20 => GetIntimateGreeting(npc),  // Love
                <= 40 => GetFriendlyGreeting(npc),  // Friendship
                <= 60 => GetRespectfulGreeting(npc), // Respect
                <= 70 => GetNeutralGreeting(npc),   // Normal
                <= 90 => GetWaryGreeting(npc),      // Suspicious/Anger
                _ => GetHostileGreeting(npc)         // Enemy/Hate
            };
        }

        private string GetSpouseGreeting(NPC npc)
        {
            var greetings = new[]
            {
                $"My love, I've been thinking about you all day.",
                $"There you are! Come here and kiss me.",
                $"I was hoping you'd find me. I've missed your touch.",
                $"My heart beats faster every time I see you, even now.",
                $"Finally! Let me look at you... yes, still the one I love."
            };
            return greetings[random.Next(greetings.Length)];
        }

        private string GetLoverGreeting(NPC npc)
        {
            var greetings = new[]
            {
                $"*smiles warmly* I was just thinking about our last night together...",
                $"There's my favorite person. Come closer.",
                $"*eyes light up* I was hoping to run into you.",
                $"You have no idea how good it is to see you.",
                $"*glances around* Is there somewhere more... private we could talk?"
            };
            return greetings[random.Next(greetings.Length)];
        }

        private string GetFWBGreeting(NPC npc)
        {
            var greetings = new[]
            {
                $"*winks* Back for more?",
                $"Well, if it isn't my favorite distraction.",
                $"*grins* I know that look in your eyes...",
                $"Good timing. I was getting... restless.",
                $"Always a pleasure to see you. Among other things."
            };
            return greetings[random.Next(greetings.Length)];
        }

        private string GetIntimateGreeting(NPC npc)
        {
            var greetings = new[]
            {
                $"*blushes* Oh! You're here. I was hoping to see you.",
                $"My heart skips a beat every time I see you.",
                $"*smiles shyly* I've been thinking about you...",
                $"You always know how to brighten my day."
            };
            return greetings[random.Next(greetings.Length)];
        }

        private string GetFriendlyGreeting(NPC npc)
        {
            var greetings = new[]
            {
                $"Hey there, friend! Good to see you!",
                $"*waves* What's new with you?",
                $"I was just talking about you! Come, let's chat.",
                $"There's a face I'm always happy to see."
            };
            return greetings[random.Next(greetings.Length)];
        }

        private string GetRespectfulGreeting(NPC npc)
        {
            var greetings = new[]
            {
                $"Ah, {player!.Name}. Good to see you.",
                $"*nods respectfully* How can I help you?",
                $"Welcome. What brings you here?",
                $"A pleasant surprise. What's on your mind?"
            };
            return greetings[random.Next(greetings.Length)];
        }

        private string GetNeutralGreeting(NPC npc)
        {
            var greetings = new[]
            {
                $"*looks up* Oh, hello there.",
                $"Can I help you with something?",
                $"Yes? What do you want?",
                $"*glances over* Hmm?"
            };
            return greetings[random.Next(greetings.Length)];
        }

        private string GetWaryGreeting(NPC npc)
        {
            var greetings = new[]
            {
                $"*eyes narrow* What do YOU want?",
                $"I have nothing to say to you.",
                $"*crosses arms* Make it quick.",
                $"*sighs heavily* Fine. Speak."
            };
            return greetings[random.Next(greetings.Length)];
        }

        private string GetHostileGreeting(NPC npc)
        {
            var greetings = new[]
            {
                $"*glares* Get away from me before I hurt you.",
                $"You have some nerve showing your face here.",
                $"*hand moves to weapon* We have nothing to discuss.",
                $"I'd sooner spit on you than speak to you."
            };
            return greetings[random.Next(greetings.Length)];
        }

        /// <summary>
        /// Show conversation options based on relationship
        /// </summary>
        private async Task<bool> ShowConversationOptions(NPC npc, int relationLevel, RomanceRelationType romanceType)
        {
            var options = BuildConversationOptions(npc, relationLevel, romanceType);

            terminal!.SetColor("cyan");
            terminal.WriteLine("  What do you want to say?");
            terminal.WriteLine("");

            for (int i = 0; i < options.Count; i++)
            {
                var opt = options[i];
                terminal.SetColor("darkgray");
                terminal.Write($"  [");
                terminal.SetColor(opt.Color);
                terminal.Write($"{i + 1}");
                terminal.SetColor("darkgray");
                terminal.Write($"] ");
                terminal.SetColor(opt.Color);
                terminal.WriteLine(opt.Text);
            }

            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine("  [0] End conversation");
            terminal.WriteLine("");

            string choice = await terminal.GetInput("Your choice: ");

            if (choice == "0")
            {
                await ShowFarewell(npc, relationLevel, romanceType);
                return false;
            }

            if (int.TryParse(choice, out int optIndex) && optIndex >= 1 && optIndex <= options.Count)
            {
                await HandleConversationChoice(npc, options[optIndex - 1], relationLevel);
                return true;
            }

            terminal.SetColor("gray");
            terminal.WriteLine("  They didn't quite understand what you meant...");
            await Task.Delay(1000);
            return true;
        }

        /// <summary>
        /// Build available conversation options - dynamic based on context and history
        /// </summary>
        private List<ConversationOption> BuildConversationOptions(NPC npc, int relationLevel, RomanceRelationType romanceType)
        {
            var options = new List<ConversationOption>();
            var profile = npc.Brain?.Personality;
            var state = npcConversationStates.GetValueOrDefault(npc.ID) ?? new ConversationState();
            bool isAttracted = profile?.IsAttractedTo(player!.Sex == CharacterSex.Female ? GenderIdentity.Female : GenderIdentity.Male) ?? true;

            // Get a dynamic chat topic
            var chatTopic = GetNextChatTopic(npc, state);
            if (chatTopic != null)
            {
                options.Add(new ConversationOption
                {
                    Type = ConversationType.Chat,
                    Text = chatTopic.PlayerLine,
                    Color = "white",
                    TopicId = chatTopic.TopicId
                });
            }

            // Ask about themselves - varies based on what we've learned
            string personalText = GetPersonalQuestion(npc, state, relationLevel);
            options.Add(new ConversationOption
            {
                Type = ConversationType.Personal,
                Text = personalText,
                Color = "bright_cyan"
            });

            // Flirtation - with cooldown and escalation
            // Allow more attempts if player hasn't reached 2 successful flirts yet (needed for confession)
            int maxFlirtsThisSession = state.FlirtSuccessCount < 2 ? 5 : 3;
            if (relationLevel <= 80 && isAttracted && flirtCountThisSession < maxFlirtsThisSession)
            {
                string flirtText = GetFlirtLine(npc, state, relationLevel, flirtCountThisSession);
                if (flirtText != null)
                {
                    options.Add(new ConversationOption
                    {
                        Type = ConversationType.Flirt,
                        Text = flirtText,
                        Color = "bright_magenta"
                    });
                }
            }
            else if (flirtCountThisSession >= maxFlirtsThisSession && relationLevel > 30)
            {
                // Too much flirting - they notice
                options.Add(new ConversationOption
                {
                    Type = ConversationType.Flirt,
                    Text = "(You've been quite flirty already...)",
                    Color = "dark_gray",
                    Disabled = true
                });
            }

            // Compliment - with variety and limits
            if (complimentCountThisSession < 2)
            {
                string complimentText = GetComplimentLine(npc, state, relationLevel);
                options.Add(new ConversationOption
                {
                    Type = ConversationType.Compliment,
                    Text = complimentText,
                    Color = "bright_green"
                });
            }

            // Romantic options (if in relationship or friendly)
            // Note: relationLevel scale is 0=Soulmate, 50=Neutral, 100=Hated
            // So <= 50 means neutral or better
            if (romanceType != RomanceRelationType.None || relationLevel <= 50)
            {
                if (romanceType == RomanceRelationType.Spouse || romanceType == RomanceRelationType.Lover)
                {
                    options.Add(new ConversationOption
                    {
                        Type = ConversationType.Intimate,
                        Text = "*lean in for a kiss*",
                        Color = "bright_red"
                    });
                }
                else if (relationLevel <= 50 && isAttracted)
                {
                    // Confession available once you're at least neutral and have enough flirts
                    // High charisma can confess with fewer flirts (exceptional charisma = 0 flirts needed)
                    int flirtsNeeded = player!.Charisma >= CHARISMA_EXCEPTIONAL ? 0 :
                                       player.Charisma >= CHARISMA_HIGH ? 1 : 2;

                    if (state.FlirtSuccessCount >= flirtsNeeded)
                    {
                        options.Add(new ConversationOption
                        {
                            Type = ConversationType.Confess,
                            Text = "I have feelings for you...",
                            Color = "bright_magenta"
                        });
                    }
                }
            }

            // Physical intimacy (if in romantic relationship)
            if (romanceType == RomanceRelationType.Spouse ||
                romanceType == RomanceRelationType.Lover ||
                romanceType == RomanceRelationType.FWB)
            {
                options.Add(new ConversationOption
                {
                    Type = ConversationType.Proposition,
                    Text = "Want to find somewhere more... private?",
                    Color = "red"
                });
            }

            // Marriage proposal (if lover and not already married to them)
            if (romanceType == RomanceRelationType.Lover && relationLevel <= 20)
            {
                options.Add(new ConversationOption
                {
                    Type = ConversationType.Propose,
                    Text = "Will you marry me?",
                    Color = "bright_red"
                });
            }

            // Ask affair partner to leave their spouse (active affair with progress >= 100)
            if (npc.IsMarried && !string.IsNullOrEmpty(npc.SpouseName) && npc.SpouseName != player!.Name2)
            {
                var affair = NPCMarriageRegistry.Instance.GetAffair(npc.ID, player!.ID);
                if (affair != null && affair.IsActive)
                {
                    options.Add(new ConversationOption
                    {
                        Type = ConversationType.AskToLeave,
                        Text = $"Leave {npc.SpouseName}. Be with me.",
                        Color = "bright_yellow"
                    });
                }
            }

            // Provocation (for conflict/rivalry paths)
            if (relationLevel >= 60) // Only for neutral or worse
            {
                options.Add(new ConversationOption
                {
                    Type = ConversationType.Provoke,
                    Text = "You think you're so tough, don't you?",
                    Color = "dark_red"
                });
            }

            return options;
        }

        /// <summary>
        /// Get the next available chat topic based on NPC and history
        /// </summary>
        private ChatTopic? GetNextChatTopic(NPC npc, ConversationState state)
        {
            var allTopics = GenerateChatTopicsForNPC(npc);

            // Filter out topics we've discussed this session
            var availableTopics = allTopics.Where(t => !topicsDiscussedThisSession.Contains(t.TopicId)).ToList();

            if (availableTopics.Count == 0)
            {
                // All topics exhausted this session - offer generic continuation
                return new ChatTopic
                {
                    TopicId = "generic_continue",
                    PlayerLine = "So... what else is new?",
                    Category = "generic"
                };
            }

            // Prioritize topics based on relationship and context
            var prioritized = availableTopics
                .OrderByDescending(t => t.Priority)
                .ThenBy(_ => random.Next())
                .First();

            return prioritized;
        }

        /// <summary>
        /// Generate chat topics specific to this NPC
        /// </summary>
        private List<ChatTopic> GenerateChatTopicsForNPC(NPC npc)
        {
            var topics = new List<ChatTopic>();
            var profile = npc.Brain?.Personality;

            // Class-specific topics
            switch (npc.Class)
            {
                case CharacterClass.Warrior:
                case CharacterClass.Barbarian:
                    topics.Add(new ChatTopic { TopicId = "warrior_training", PlayerLine = "How do you stay in such good fighting shape?", Category = "class", Priority = 3 });
                    topics.Add(new ChatTopic { TopicId = "warrior_battles", PlayerLine = "What's the toughest fight you've ever been in?", Category = "class", Priority = 4 });
                    topics.Add(new ChatTopic { TopicId = "warrior_weapons", PlayerLine = "What's your weapon of choice?", Category = "class", Priority = 2 });
                    break;
                case CharacterClass.Magician:
                case CharacterClass.Sage:
                    topics.Add(new ChatTopic { TopicId = "mage_magic", PlayerLine = "How did you learn magic?", Category = "class", Priority = 4 });
                    topics.Add(new ChatTopic { TopicId = "mage_spells", PlayerLine = "What's the most powerful spell you know?", Category = "class", Priority = 3 });
                    topics.Add(new ChatTopic { TopicId = "mage_research", PlayerLine = "Are you researching anything interesting?", Category = "class", Priority = 2 });
                    break;
                case CharacterClass.Cleric:
                case CharacterClass.Paladin:
                    topics.Add(new ChatTopic { TopicId = "cleric_faith", PlayerLine = "Which gods do you follow?", Category = "class", Priority = 4 });
                    topics.Add(new ChatTopic { TopicId = "cleric_calling", PlayerLine = "What drew you to the holy life?", Category = "class", Priority = 3 });
                    topics.Add(new ChatTopic { TopicId = "cleric_miracles", PlayerLine = "Have you ever witnessed a miracle?", Category = "class", Priority = 2 });
                    break;
                case CharacterClass.Assassin:
                    topics.Add(new ChatTopic { TopicId = "rogue_shadows", PlayerLine = "How do you move so quietly?", Category = "class", Priority = 3 });
                    topics.Add(new ChatTopic { TopicId = "rogue_jobs", PlayerLine = "Ever take on a job you regretted?", Category = "class", Priority = 4 });
                    topics.Add(new ChatTopic { TopicId = "rogue_secrets", PlayerLine = "I bet you know a lot of secrets around here...", Category = "class", Priority = 2 });
                    break;
                case CharacterClass.Ranger:
                    topics.Add(new ChatTopic { TopicId = "ranger_wilds", PlayerLine = "What's the strangest thing you've seen in the wilds?", Category = "class", Priority = 4 });
                    topics.Add(new ChatTopic { TopicId = "ranger_tracking", PlayerLine = "How do you track so well?", Category = "class", Priority = 3 });
                    break;
                case CharacterClass.Bard:
                case CharacterClass.Jester:
                    topics.Add(new ChatTopic { TopicId = "bard_stories", PlayerLine = "What's the best tale you know?", Category = "class", Priority = 4 });
                    topics.Add(new ChatTopic { TopicId = "bard_music", PlayerLine = "Do you play any instruments?", Category = "class", Priority = 3 });
                    break;
                case CharacterClass.Alchemist:
                    topics.Add(new ChatTopic { TopicId = "alchemist_potions", PlayerLine = "What's the strangest potion you've ever brewed?", Category = "class", Priority = 4 });
                    topics.Add(new ChatTopic { TopicId = "alchemist_ingredients", PlayerLine = "Where do you find your ingredients?", Category = "class", Priority = 3 });
                    break;
                default:
                    topics.Add(new ChatTopic { TopicId = "generic_work", PlayerLine = "How's work been treating you?", Category = "generic", Priority = 2 });
                    break;
            }

            // Universal topics
            topics.Add(new ChatTopic { TopicId = "dungeon_rumors", PlayerLine = "Heard any rumors about the dungeons?", Category = "adventure", Priority = 3 });
            topics.Add(new ChatTopic { TopicId = "town_news", PlayerLine = "What's the latest gossip in town?", Category = "social", Priority = 2 });
            topics.Add(new ChatTopic { TopicId = "weather", PlayerLine = "Strange weather we've been having...", Category = "generic", Priority = 1 });
            topics.Add(new ChatTopic { TopicId = "life_goals", PlayerLine = "What are you hoping to achieve here?", Category = "personal", Priority = 3 });
            topics.Add(new ChatTopic { TopicId = "origins", PlayerLine = "Where are you from originally?", Category = "personal", Priority = 4 });
            topics.Add(new ChatTopic { TopicId = "hobbies", PlayerLine = "What do you do when you're not adventuring?", Category = "personal", Priority = 2 });

            // Personality-based topics
            if (profile != null)
            {
                if (profile.Romanticism > 0.6f)
                    topics.Add(new ChatTopic { TopicId = "romance_views", PlayerLine = "Do you believe in true love?", Category = "romantic", Priority = 3 });
                if (profile.Sociability > 0.7f)
                    topics.Add(new ChatTopic { TopicId = "friends", PlayerLine = "Who are your closest friends here?", Category = "social", Priority = 3 });
                if (profile.Commitment > 0.7f)
                    topics.Add(new ChatTopic { TopicId = "family", PlayerLine = "Do you have any family?", Category = "personal", Priority = 4 });
            }

            return topics;
        }

        /// <summary>
        /// Get a personal question based on what we already know
        /// </summary>
        private string GetPersonalQuestion(NPC npc, ConversationState state, int relationLevel)
        {
            // If we've asked many questions, offer deeper ones at good relationship
            if (state.PersonalQuestionsAsked >= 3 && relationLevel <= 40)
            {
                var deepQuestions = new[]
                {
                    "What's your deepest fear?",
                    "What do you dream about at night?",
                    "Have you ever loved someone?",
                    "What would you change about your past?",
                    "What makes you truly happy?"
                };
                return deepQuestions[random.Next(deepQuestions.Length)];
            }
            else if (state.PersonalQuestionsAsked >= 1)
            {
                var followUpQuestions = new[]
                {
                    "Tell me more about your past...",
                    "What's your story really?",
                    "How did you end up here?",
                    "What drives you?"
                };
                return followUpQuestions[random.Next(followUpQuestions.Length)];
            }
            return "Tell me about yourself.";
        }

        /// <summary>
        /// Get a flirt line based on progression and context
        /// </summary>
        private string? GetFlirtLine(NPC npc, ConversationState state, int relationLevel, int sessionFlirts)
        {
            // First flirt of conversation - be subtle
            if (sessionFlirts == 0)
            {
                var subtleFlirts = new[]
                {
                    "You have really nice eyes, you know that?",
                    "There's something about you... I can't quite put my finger on it.",
                    "I enjoy talking to you. More than most people, honestly.",
                    "You're interesting. Not like the others around here."
                };
                return subtleFlirts[random.Next(subtleFlirts.Length)];
            }
            // Second flirt - a bit bolder
            else if (sessionFlirts == 1)
            {
                if (state.LastFlirtWasPositive)
                {
                    var bolderFlirts = new[]
                    {
                        "*holds eye contact a bit longer than usual*",
                        "I find myself looking forward to seeing you...",
                        "You're quite attractive, you know.",
                        "I think about you sometimes. When you're not around."
                    };
                    return bolderFlirts[random.Next(bolderFlirts.Length)];
                }
                else
                {
                    // They weren't receptive before - be more careful
                    var carefulFlirts = new[]
                    {
                        "I hope I wasn't too forward earlier...",
                        "Anyway... you're nice to talk to.",
                        "Sorry if I made things awkward."
                    };
                    return carefulFlirts[random.Next(carefulFlirts.Length)];
                }
            }
            // Third flirt - make it count
            else if (sessionFlirts == 2)
            {
                // Direct flirts if they've been receptive
                var directFlirts = new[]
                {
                    "*moves closer* I really enjoy being around you...",
                    "I have to admit, you've been on my mind a lot.",
                    "Is it just me, or is there something between us?"
                };
                return directFlirts[random.Next(directFlirts.Length)];
            }
            // Allow more flirt attempts if still building relationship
            else if (sessionFlirts >= 3 && state.FlirtSuccessCount < 2)
            {
                var persistentFlirts = new[]
                {
                    "I just... I like being around you.",
                    "You make me smile, you know?",
                    "Sorry, I can't help myself around you."
                };
                return persistentFlirts[random.Next(persistentFlirts.Length)];
            }
            return null;
        }

        /// <summary>
        /// Get a compliment line that's contextual
        /// </summary>
        private string GetComplimentLine(NPC npc, ConversationState state, int relationLevel)
        {
            var profile = npc.Brain?.Personality;

            // First compliment - based on their obvious traits
            if (complimentCountThisSession == 0)
            {
                var compliments = new List<string>();

                switch (npc.Class)
                {
                    case CharacterClass.Warrior:
                    case CharacterClass.Barbarian:
                        compliments.Add("You look like you could handle yourself in a fight.");
                        compliments.Add("I can tell you've seen real combat.");
                        break;
                    case CharacterClass.Magician:
                    case CharacterClass.Sage:
                        compliments.Add("You seem very knowledgeable.");
                        compliments.Add("There's a wisdom in your eyes.");
                        break;
                    case CharacterClass.Cleric:
                    case CharacterClass.Paladin:
                        compliments.Add("You have a calming presence about you.");
                        compliments.Add("Your faith is admirable.");
                        break;
                    default:
                        compliments.Add("You carry yourself with confidence.");
                        compliments.Add("You seem like someone who knows what they're doing.");
                        break;
                }

                if (profile?.Sociability > 0.6f)
                    compliments.Add("You have a way of putting people at ease.");
                if (npc.Level > 20)
                    compliments.Add($"Level {npc.Level}? That's impressive.");

                return compliments[random.Next(compliments.Count)];
            }
            else
            {
                // Second compliment - more personal
                var personalCompliments = new[]
                {
                    "I like the way you think.",
                    "You're easy to talk to.",
                    "You have a good heart. I can tell.",
                    "I appreciate your honesty."
                };
                return personalCompliments[random.Next(personalCompliments.Length)];
            }
        }

        /// <summary>
        /// Handle the player's conversation choice
        /// </summary>
        private async Task HandleConversationChoice(NPC npc, ConversationOption option, int relationLevel)
        {
            terminal!.ClearScreen();
            await ShowConversationHeader(npc, relationLevel, RomanceTracker.Instance.GetRelationType(npc.ID));

            // Don't process disabled options
            if (option.Disabled)
            {
                terminal!.SetColor("gray");
                terminal.WriteLine("  You decide not to push it.");
                await terminal.GetInput("  Press Enter to continue...");
                return;
            }

            switch (option.Type)
            {
                case ConversationType.Chat:
                    await HandleChatOption(npc, relationLevel, option.TopicId);
                    break;
                case ConversationType.Personal:
                    await HandlePersonalOption(npc, relationLevel);
                    break;
                case ConversationType.Flirt:
                    await HandleFlirtOption(npc, relationLevel);
                    flirtCountThisSession++;
                    break;
                case ConversationType.Compliment:
                    await HandleComplimentOption(npc, relationLevel);
                    complimentCountThisSession++;
                    break;
                case ConversationType.Confess:
                    await HandleConfessionOption(npc, relationLevel);
                    break;
                case ConversationType.Intimate:
                    await HandleIntimateOption(npc, relationLevel);
                    break;
                case ConversationType.Proposition:
                    await HandlePropositionOption(npc, relationLevel);
                    break;
                case ConversationType.Propose:
                    await HandleMarriageProposal(npc, relationLevel);
                    break;
                case ConversationType.Provoke:
                    await HandleProvocationOption(npc, relationLevel);
                    break;
                case ConversationType.AskToLeave:
                    await HandleAskToLeaveOption(npc, relationLevel);
                    break;
            }

            // Store conversation in memory
            StoreConversationMemory(npc.Name2, option.Type);
        }

        private async Task HandleChatOption(NPC npc, int relationLevel, string? topicId = null)
        {
            var profile = npc.Brain?.Personality;
            float sociability = profile?.Sociability ?? 0.5f;
            var state = npcConversationStates.GetValueOrDefault(npc.ID) ?? new ConversationState();

            // Mark topic as discussed
            if (topicId != null)
                topicsDiscussedThisSession.Add(topicId);

            // Generate response based on topic
            string response = GenerateTopicResponse(npc, topicId ?? "generic", relationLevel);

            terminal!.SetColor("yellow");
            terminal.WriteLine($"  {npc.Name2} considers your question...");
            terminal.WriteLine("");

            await Task.Delay(500);

            terminal.SetColor("white");
            terminal.WriteLine($"  \"{response}\"");

            // Emotional reaction based on sociability and topic relevance
            await Task.Delay(300);

            if (sociability > 0.7f)
            {
                terminal.WriteLine("");
                terminal.SetColor("gray");
                terminal.WriteLine($"  They seem happy to have someone to talk to.");
                RelationshipSystem.UpdateRelationship(player!, npc, 1);
            }
            else if (sociability > 0.4f && random.NextDouble() < 0.5)
            {
                terminal.WriteLine("");
                terminal.SetColor("gray");
                terminal.WriteLine($"  They warm up a bit as you show genuine interest.");
            }

            // Track conversation progress
            state.TopicsDiscussed.Add(topicId ?? "generic");
            state.LastConversationDate = DateTime.Now;

            terminal.WriteLine("");
            await terminal.GetInput("  Press Enter to continue...");
        }

        /// <summary>
        /// Generate a contextual response for a chat topic
        /// </summary>
        private string GenerateTopicResponse(NPC npc, string topicId, int relationLevel)
        {
            var profile = npc.Brain?.Personality;
            bool isOpen = relationLevel <= 50 || (profile?.Sociability ?? 0.5f) > 0.6f;

            // Class and topic specific responses
            switch (topicId)
            {
                // Warrior topics
                case "warrior_training":
                    return isOpen
                        ? "Training every day, fighting often. There's no shortcut to strength. You either earn it or you don't."
                        : "I train. A lot.";
                case "warrior_battles":
                    var battleStories = new[]
                    {
                        "There was this ogre chieftain on the third level... took me three hours and two healing potions. Still have the scar.",
                        "Fought a band of orcs that had cornered a merchant caravan. Seven of them. It was... messy.",
                        "I'd rather not talk about the worst ones. Some things stay with you.",
                        "Every fight is tough when your life's on the line. The easy ones just mean you trained harder."
                    };
                    return battleStories[random.Next(battleStories.Length)];
                case "warrior_weapons":
                    return $"I prefer something with weight to it. When you hit someone, you want them to stay down.";

                // Mage topics
                case "mage_magic":
                    return isOpen
                        ? "Years of study, sleepless nights, and one too many singed eyebrows. Magic isn't learned, it's earned through dedication."
                        : "Books. Lots of books.";
                case "mage_spells":
                    var spellStories = new[]
                    {
                        "I once managed to conjure lightning during a clear sky. The power was... intoxicating. Also terrifying.",
                        "There's a spell I've been working on that could change everything. But it's not ready yet.",
                        "Power isn't about the biggest spell. It's about using the right spell at the right moment."
                    };
                    return spellStories[random.Next(spellStories.Length)];
                case "mage_research":
                    return "Always. The arcane arts are infinite. I'll never run out of questions to answer.";

                // Cleric/Paladin topics
                case "cleric_faith":
                    var faithResponses = new[]
                    {
                        "I follow the light. In all its forms. It guides me even in the darkest places.",
                        "The gods are real. I've felt their power flow through me. Once you experience that, doubt becomes impossible.",
                        "Faith isn't about which god you pray to. It's about believing in something greater than yourself."
                    };
                    return faithResponses[random.Next(faithResponses.Length)];
                case "cleric_calling":
                    return isOpen
                        ? "I was lost once. The temple took me in. Now I try to help others find their way."
                        : "It's a long story.";
                case "cleric_miracles":
                    return "Every healing is a miracle. Every life saved. We just forget to see them that way.";

                // Rogue topics
                case "rogue_shadows":
                    return isOpen
                        ? "*grins* Practice. And learning to read people. Most don't pay attention to what's actually around them."
                        : "Trade secret.";
                case "rogue_jobs":
                    return "Everyone has regrets. The trick is learning from them without drowning in them.";
                case "rogue_secrets":
                    return "*raises eyebrow* Maybe. But secrets lose their value when shared. Why do you ask?";

                // Ranger topics
                case "ranger_wilds":
                    var wildStories = new[]
                    {
                        "A clearing where the trees grew in a perfect circle. Ancient magic, I think. Gave me chills.",
                        "Wolves that watched me for three days but never attacked. Like they were... studying me.",
                        "There are things in the deep forest that don't have names. Some of them aren't hostile. Some are."
                    };
                    return wildStories[random.Next(wildStories.Length)];
                case "ranger_tracking":
                    return "Everything leaves a trail. Broken twigs, bent grass, the way animals go quiet. You just have to learn to see.";

                // Monk topics
                case "monk_discipline":
                    return "The body and mind are one. Train one, and you train the other. Neglect one, and both suffer.";
                case "monk_meditation":
                    return "Daily. It's not about emptying your mind - it's about learning what fills it.";

                // Universal topics
                case "dungeon_rumors":
                    var dungeonRumors = new[]
                    {
                        "They say there's something stirring on the deeper levels. Something old.",
                        "A party went down to level 40 last week. Only one came back. She won't talk about what happened.",
                        "The monsters have been more organized lately. Almost like something's... directing them.",
                        "I heard there's treasure on level 25 that no one's been able to claim. Cursed, some say."
                    };
                    return dungeonRumors[random.Next(dungeonRumors.Length)];
                case "town_news":
                    var gossip = new[]
                    {
                        "The inn's been busier than usual. Lots of new adventurers coming through.",
                        "Someone said the King's raising taxes again. As if we don't pay enough already.",
                        "There was a fight at the arena yesterday. Someone got hurt badly.",
                        "The weapon shop got a new shipment. Might be worth checking out."
                    };
                    return gossip[random.Next(gossip.Length)];
                case "weather":
                    return "It's been strange lately. The old-timers say it means something's changing. Then again, they say that about everything.";
                case "life_goals":
                    return isOpen
                        ? "Survival first. Gold second. Maybe find something worth fighting for along the way."
                        : "Getting by. Same as everyone.";
                case "origins":
                    return isOpen
                        ? $"Somewhere else. Somewhere that's not here anymore, if you take my meaning."
                        : "Does it matter? We're all here now.";
                case "hobbies":
                    var hobbies = new[]
                    {
                        "When you spend your days fighting for your life, relaxing is a hobby in itself.",
                        "I read when I can. There's a bookshop in town that gets interesting things sometimes.",
                        "Drinking at the inn counts, right? *laughs*"
                    };
                    return hobbies[random.Next(hobbies.Length)];
                case "romance_views":
                    return isOpen
                        ? "True love? Maybe. I've seen people do incredible things for love. Terrible things too."
                        : "*looks away* That's... personal.";
                case "friends":
                    return "A few. Quality over quantity. Trust is hard to come by here.";
                case "family":
                    return isOpen
                        ? "That's a complicated question. Family isn't always about blood."
                        : "I'd rather not talk about that.";

                default:
                    var generic = new[]
                    {
                        "Things have been... interesting lately.",
                        "Same as usual, I suppose. Fighting, surviving, trying to make gold.",
                        "Nothing too exciting. Just the usual dangers and drama."
                    };
                    return generic[random.Next(generic.Length)];
            }
        }

        private async Task HandlePersonalOption(NPC npc, int relationLevel)
        {
            terminal!.SetColor("gray");
            terminal.WriteLine($"  You ask {npc.Name2} to tell you about themselves...");
            terminal.WriteLine("");

            await Task.Delay(500);

            var profile = npc.Brain?.Personality;

            // They open up more at better relationships
            if (relationLevel <= 40) // Friend or better
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {npc.Name2} opens up to you:");
                terminal.SetColor("white");
                terminal.WriteLine($"  \"{GeneratePersonalStory(npc, true)}\"");

                // Relationship boost for showing interest
                RelationshipSystem.UpdateRelationship(player!, npc, 1);
            }
            else if (relationLevel <= 70)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {npc.Name2} shares a little:");
                terminal.SetColor("white");
                terminal.WriteLine($"  \"{GeneratePersonalStory(npc, false)}\"");
            }
            else
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {npc.Name2} is guarded:");
                terminal.SetColor("white");
                terminal.WriteLine($"  \"Why do you want to know? We barely know each other.\"");
            }

            terminal.WriteLine("");
            await terminal.GetInput("  Press Enter to continue...");
        }

        private string GeneratePersonalStory(NPC npc, bool intimate)
        {
            if (intimate)
            {
                var stories = new[]
                {
                    $"I became a {npc.Class} because I had no other choice. My family... it's a long story.",
                    $"Sometimes I wonder if there's more to life than fighting and surviving.",
                    $"I trust you, so I'll tell you - I'm looking for someone I lost long ago.",
                    $"Between you and me? I'm not as tough as I seem. Everyone has their vulnerabilities."
                };
                return stories[random.Next(stories.Length)];
            }
            else
            {
                var stories = new[]
                {
                    $"I've been a {npc.Class} for many years now. It's a living.",
                    $"Not much to tell, really. I do my job and try to stay alive.",
                    $"I came to Dorashire looking for opportunity. Still looking.",
                    $"The usual story - trying to make gold and not die."
                };
                return stories[random.Next(stories.Length)];
            }
        }

        private async Task HandleFlirtOption(NPC npc, int relationLevel)
        {
            var profile = npc.Brain?.Personality;
            var state = npcConversationStates.GetValueOrDefault(npc.ID) ?? new ConversationState();
            bool isAttracted = profile?.IsAttractedTo(player!.Sex == CharacterSex.Female ? GenderIdentity.Female : GenderIdentity.Male) ?? true;

            // Check NPC's relationship status
            bool npcIsMarried = npc.Married || npc.IsMarried;
            // Check if NPC has a spouse/partner that isn't the player (makes flirting harder)
            // If NPC is already player's Lover/FWB, they're receptive - don't penalize
            bool npcHasLover = !string.IsNullOrEmpty(npc.SpouseName) && npc.SpouseName != player!.Name2;

            // Get base flirt receptiveness with NPC's relationship status
            float flirtReceptiveness = profile?.GetFlirtReceptiveness(relationLevel, isAttracted, npcIsMarried, npcHasLover) ?? 0.3f;

            // Check player's relationship status - NPCs notice if you're taken!
            bool playerIsMarried = player!.Married || player.IsMarried || RomanceTracker.Instance.IsMarried;
            bool playerHasLover = RomanceTracker.Instance.CurrentLovers?.Count > 0;

            // Player relationship status penalties
            float playerStatusPenalty = 0f;
            string playerStatusWarning = "";
            if (playerIsMarried)
            {
                playerStatusPenalty = 0.25f; // -25% for married players
                playerStatusWarning = "They glance at your wedding ring...";
            }
            else if (playerHasLover)
            {
                playerStatusPenalty = 0.10f; // -10% for players in relationships
                playerStatusWarning = "They seem aware you're already involved with someone...";
            }
            flirtReceptiveness -= playerStatusPenalty;

            // Apply charisma modifier - high charisma makes flirting more effective
            // Charisma matters more now (scaled up)
            float charismaModifier = GetCharismaModifier() * 1.5f;
            flirtReceptiveness += charismaModifier;

            // Adjust receptiveness based on how the conversation's been going
            if (state.FlirtSuccessCount > 0)
                flirtReceptiveness += 0.08f; // They're warming up (reduced from 0.1)
            if (flirtCountThisSession > 1 && !state.LastFlirtWasPositive)
                flirtReceptiveness -= 0.25f; // Pushing too hard (increased penalty)

            // Clamp to reasonable bounds - max is lower now
            flirtReceptiveness = Math.Clamp(flirtReceptiveness, 0.02f, 0.80f);

            terminal!.SetColor("bright_magenta");

            // Show player status warning if applicable
            if (!string.IsNullOrEmpty(playerStatusWarning))
            {
                terminal.SetColor("dark_gray");
                terminal.WriteLine($"  ({playerStatusWarning})");
            }

            // Show charisma flavor text for exceptional or very low charisma
            string charismaFlavor = GetCharismaFlavorText(true);
            if (!string.IsNullOrEmpty(charismaFlavor))
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  ({charismaFlavor})");
                terminal.SetColor("bright_magenta");
            }

            // Check for "impossible" scenarios - NPC outright refuses
            if (npcIsMarried && profile != null && profile.Commitment > 0.7f)
            {
                terminal.WriteLine($"  You try to catch {npc.Name2}'s eye...");
                terminal.WriteLine("");
                await Task.Delay(500);
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {npc.Name2} stops you immediately:");
                terminal.SetColor("red");
                var marriedResponses = new[]
                {
                    $"  \"I'm married. Happily. This conversation is over.\"",
                    $"  *coldly* \"I have a spouse I love dearly. Don't try this again.\"",
                    $"  \"I don't know what you think you're doing, but I'm not interested. I'm married.\""
                };
                terminal.WriteLine(marriedResponses[random.Next(marriedResponses.Length)]);
                RelationshipSystem.UpdateRelationship(player!, npc, -2);
                flirtCountThisSession++;
                terminal.WriteLine("");
                await terminal!.PressAnyKey();
                return;
            }

            // NPC has a lover and is very jealous/committed
            if (npcHasLover && profile != null && (profile.Jealousy > 0.7f || profile.Commitment > 0.65f))
            {
                terminal.WriteLine($"  You try to catch {npc.Name2}'s eye...");
                terminal.WriteLine("");
                await Task.Delay(500);
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {npc.Name2} shakes their head:");
                terminal.SetColor("dark_red");
                var takenResponses = new[]
                {
                    $"  \"I'm with someone. I'm not looking elsewhere.\"",
                    $"  \"Flattering, but my heart belongs to another.\"",
                    $"  *firmly* \"I have someone special in my life. This isn't happening.\""
                };
                terminal.WriteLine(takenResponses[random.Next(takenResponses.Length)]);
                flirtCountThisSession++;
                terminal.WriteLine("");
                await terminal!.PressAnyKey();
                return;
            }

            // AFFAIR HANDLING: Married NPC with lower commitment - affair is possible!
            if (npcIsMarried && profile != null && profile.Commitment <= 0.7f)
            {
                // Process affair attempt through the affair system
                var affairResult = EnhancedNPCBehaviors.ProcessAffairAttempt(npc, player!, flirtReceptiveness);

                terminal.WriteLine($"  You catch {npc.Name2}'s eye, knowing they're married...");
                terminal.WriteLine("");
                await Task.Delay(500);

                if (affairResult.Success)
                {
                    state.LastFlirtWasPositive = true;
                    state.FlirtSuccessCount++;
                    flirtCountThisSession++;

                    // Show milestone-specific dialogue
                    switch (affairResult.Milestone)
                    {
                        case AffairMilestone.BecameLovers:
                            terminal.SetColor("bright_red");
                            terminal.WriteLine($"  ♥ Something forbidden has begun... ♥");
                            terminal.SetColor("yellow");
                            terminal.WriteLine($"  {affairResult.Message}");
                            terminal.WriteLine("");
                            terminal.SetColor("gray");
                            terminal.WriteLine($"  (You are now having an affair with {npc.Name2}!)");
                            break;

                        case AffairMilestone.SecretRendezvous:
                            terminal.SetColor("red");
                            terminal.WriteLine($"  The tension is palpable...");
                            terminal.SetColor("yellow");
                            terminal.WriteLine($"  {affairResult.Message}");
                            break;

                        case AffairMilestone.EmotionalConnection:
                            terminal.SetColor("magenta");
                            terminal.WriteLine($"  There's a spark of something dangerous here...");
                            terminal.SetColor("yellow");
                            terminal.WriteLine($"  {affairResult.Message}");
                            break;

                        default: // Flirting
                            terminal.SetColor("bright_magenta");
                            terminal.WriteLine($"  {affairResult.Message}");
                            break;
                    }

                    // Improve relationship slightly
                    RelationshipSystem.UpdateRelationship(player!, npc, 1);

                    // Check if NPC will leave their spouse for the player
                    var divorceCheck = EnhancedNPCBehaviors.CheckAffairDivorce(npc, player!);
                    if (divorceCheck.WillDivorce)
                    {
                        terminal.WriteLine("");
                        terminal.SetColor("bright_yellow");
                        terminal.WriteLine($"  ═══ A Decision Is Made ═══");
                        terminal.SetColor("yellow");
                        terminal.WriteLine($"  {divorceCheck.Reason}");
                        terminal.WriteLine("");

                        // Offer player a choice - become spouse or just lovers
                        terminal.SetColor("cyan");
                        terminal.WriteLine($"  [M] \"Marry me.\" - Make them your spouse");
                        terminal.WriteLine($"  [L] \"Stay with me.\" - Become lovers");
                        terminal.WriteLine($"  [N] \"I can't do this.\" - Reject them");
                        terminal.WriteLine("");
                        terminal.Write("  Your choice: ");
                        string? affairChoice = await terminal.GetInput("");

                        if (affairChoice?.ToUpper() == "M" || affairChoice?.ToUpper() == "L")
                        {
                            bool marry = affairChoice.ToUpper() == "M";
                            EnhancedNPCBehaviors.ProcessAffairDivorce(npc, player!, marry);

                            if (marry)
                            {
                                terminal.SetColor("bright_red");
                                terminal.WriteLine($"  ♥ {npc.Name2} will become your spouse! ♥");
                                // The actual marriage would be handled by the regular marriage system
                            }
                            else
                            {
                                terminal.SetColor("red");
                                terminal.WriteLine($"  ♥ {npc.Name2} is now your lover. ♥");
                                RomanceTracker.Instance.AddLover(npc.ID, 50, false);
                            }
                        }
                        else
                        {
                            terminal.SetColor("gray");
                            terminal.WriteLine($"  {npc.Name2} looks heartbroken as you walk away...");
                            RelationshipSystem.UpdateRelationship(player!, npc, -3);
                        }
                    }
                }
                else
                {
                    state.LastFlirtWasPositive = false;
                    flirtCountThisSession++;

                    terminal.SetColor("yellow");
                    terminal.WriteLine($"  {affairResult.Message}");

                    if (affairResult.SpouseNoticed)
                    {
                        terminal.SetColor("dark_red");
                        terminal.WriteLine($"  (Their spouse may have noticed something...)");
                    }
                }

                terminal.WriteLine("");
                await terminal.PressAnyKey();
                return;
            }

            // Vary the player's action based on the flirt
            if (flirtCountThisSession == 0)
                terminal.WriteLine($"  You catch {npc.Name2}'s eye and hold it a moment longer than necessary...");
            else if (flirtCountThisSession == 1)
                terminal.WriteLine($"  You move a bit closer to {npc.Name2}...");
            else
                terminal.WriteLine($"  You make your interest clear...");

            terminal.WriteLine("");
            await Task.Delay(500);

            float roll = (float)random.NextDouble();

            if (roll < flirtReceptiveness)
            {
                // Positive response
                state.LastFlirtWasPositive = true;
                state.FlirtSuccessCount++;

                terminal.SetColor("yellow");
                terminal.WriteLine($"  {npc.Name2} responds warmly:");
                terminal.SetColor("bright_magenta");

                // Varied positive responses
                if (flirtReceptiveness > 0.5f && state.FlirtSuccessCount >= 2)
                {
                    var responses = new[]
                    {
                        $"  *moves closer* \"I was hoping you'd say something like that...\"",
                        $"  *blushes deeply* \"You're making it hard to think straight.\"",
                        $"  *laughs softly* \"Keep talking like that and who knows what might happen...\""
                    };
                    terminal.WriteLine(responses[random.Next(responses.Length)]);
                    // Strong flirt success - allow breaking through friendship cap
                    RelationshipSystem.UpdateRelationship(player!, npc, 1, 3, false, true);
                }
                else if (state.FlirtSuccessCount == 1)
                {
                    var responses = new[]
                    {
                        $"  *blushes and smiles* \"Well, aren't you charming...\"",
                        $"  *looks down shyly* \"You certainly know how to make someone feel special.\"",
                        $"  *grins* \"I'm glad someone noticed.\""
                    };
                    terminal.WriteLine(responses[random.Next(responses.Length)]);
                    // Successful flirt - allow progression with override
                    RelationshipSystem.UpdateRelationship(player!, npc, 1, 2, false, true);
                }
                else
                {
                    terminal.WriteLine($"  *smiles* \"That's... sweet of you to say.\"");
                    // First successful flirt - modest boost with override
                    RelationshipSystem.UpdateRelationship(player!, npc, 1, 1, false, true);
                }
            }
            else if (roll < flirtReceptiveness + 0.25f)
            {
                // Neutral response (narrower range now)
                state.LastFlirtWasPositive = false;

                terminal.SetColor("yellow");
                terminal.WriteLine($"  {npc.Name2} seems unsure how to respond:");
                terminal.SetColor("gray");

                var responses = new[]
                {
                    $"  *awkward laugh* \"Oh... um, thanks?\"",
                    $"  *clears throat* \"That's... nice of you.\"",
                    $"  *looks away* \"I'm not sure what to say to that.\""
                };
                terminal.WriteLine(responses[random.Next(responses.Length)]);
            }
            else
            {
                // Negative response (more likely now)
                state.LastFlirtWasPositive = false;

                terminal.SetColor("yellow");
                terminal.WriteLine($"  {npc.Name2} isn't impressed:");
                terminal.SetColor("dark_red");

                if (!isAttracted)
                {
                    terminal.WriteLine($"  \"Flattering, but you're not really my type.\"");
                }
                else if (playerIsMarried)
                {
                    var responses = new[]
                    {
                        $"  \"Aren't you married? I don't appreciate this.\"",
                        $"  *eyes your ring* \"I think your spouse might have something to say about that.\"",
                        $"  \"I don't get involved with married people. Period.\""
                    };
                    terminal.WriteLine(responses[random.Next(responses.Length)]);
                    RelationshipSystem.UpdateRelationship(player!, npc, -2);
                }
                else if (flirtCountThisSession > 1)
                {
                    terminal.WriteLine($"  \"You're being a bit... persistent, aren't you?\"");
                    RelationshipSystem.UpdateRelationship(player!, npc, -1);
                }
                else if (relationLevel > 60)
                {
                    var responses = new[]
                    {
                        $"  \"We barely know each other. Maybe slow down?\"",
                        $"  *steps back* \"I don't know you well enough for this.\"",
                        $"  \"Perhaps we should get to know each other better first.\""
                    };
                    terminal.WriteLine(responses[random.Next(responses.Length)]);
                }
                else
                {
                    var responses = new[]
                    {
                        $"  *frowns* \"I don't appreciate that kind of talk.\"",
                        $"  \"Let's keep things professional, shall we?\"",
                        $"  \"I'm not interested in... whatever that was.\""
                    };
                    terminal.WriteLine(responses[random.Next(responses.Length)]);
                    RelationshipSystem.UpdateRelationship(player!, npc, -1);
                }
            }

            terminal.WriteLine("");
            await terminal.GetInput("  Press Enter to continue...");
        }

        private async Task HandleComplimentOption(NPC npc, int relationLevel)
        {
            terminal!.SetColor("bright_green");
            terminal.WriteLine($"  You offer a sincere compliment to {npc.Name2}...");
            terminal.WriteLine("");

            await Task.Delay(500);

            // Compliments almost always work positively
            terminal.SetColor("yellow");
            terminal.WriteLine($"  {npc.Name2} is pleased:");
            terminal.SetColor("white");
            terminal.WriteLine($"  \"That's very kind of you. Thank you.\"");

            RelationshipSystem.UpdateRelationship(player!, npc, 1);

            terminal.WriteLine("");
            await terminal.GetInput("  Press Enter to continue...");
        }

        private async Task HandleConfessionOption(NPC npc, int relationLevel)
        {
            var profile = npc.Brain?.Personality;
            bool isAttracted = profile?.IsAttractedTo(player!.Sex == CharacterSex.Female ? GenderIdentity.Female : GenderIdentity.Male) ?? true;

            terminal!.SetColor("bright_magenta");
            terminal.WriteLine($"  You take a deep breath and confess your feelings...");
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine($"  \"{npc.Name2}, I have to be honest with you. I have feelings for you.");
            terminal.WriteLine($"   Real feelings. I think about you all the time...\"");
            terminal.WriteLine("");

            await Task.Delay(1000);

            float successChance = isAttracted ? 0.5f + (0.7f - relationLevel / 100f) : 0.1f;
            if (profile != null)
            {
                successChance += profile.Romanticism * 0.2f;
            }

            // Apply charisma modifier - confessions are heavily influenced by charm
            float charismaModifier = GetCharismaModifier();
            successChance += charismaModifier * 1.5f; // Charisma matters more for confessions
            successChance = Math.Clamp(successChance, 0.05f, 0.90f);

            float roll = (float)random.NextDouble();

            if (roll < successChance)
            {
                // They reciprocate!
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {npc.Name2}'s eyes widen, then soften:");
                terminal.SetColor("bright_magenta");
                terminal.WriteLine($"  *takes your hand* \"I... I've felt the same way. I was afraid to say it.\"");
                terminal.WriteLine("");

                // Becoming lovers is a major relationship boost - use override to break friendship cap
                RelationshipSystem.UpdateRelationship(player!, npc, 1, 8, false, true);
                RomanceTracker.Instance.AddLover(npc.ID, 30);

                terminal.SetColor("bright_cyan");
                terminal.WriteLine($"  A new romantic relationship has begun!");
            }
            else if (roll < successChance + 0.3f)
            {
                // They need time
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {npc.Name2} looks surprised:");
                terminal.SetColor("white");
                terminal.WriteLine($"  \"I... I don't know what to say. This is sudden. Can I think about it?\"");

                RelationshipSystem.UpdateRelationship(player!, npc, 1);
            }
            else
            {
                // Rejection
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {npc.Name2} looks uncomfortable:");
                terminal.SetColor("gray");
                terminal.WriteLine($"  \"I'm flattered, truly. But... I don't feel the same way. I'm sorry.\"");
            }

            terminal.WriteLine("");
            await terminal.GetInput("  Press Enter to continue...");
        }

        private async Task HandleIntimateOption(NPC npc, int relationLevel)
        {
            terminal!.SetColor("bright_red");
            terminal.WriteLine($"  You lean in close to {npc.Name2}...");
            terminal.WriteLine("");

            await Task.Delay(500);

            var romanceType = RomanceTracker.Instance.GetRelationType(npc.ID);

            if (romanceType == RomanceRelationType.Spouse || romanceType == RomanceRelationType.Lover)
            {
                terminal.SetColor("white");
                terminal.WriteLine($"  Your lips meet in a tender kiss. {npc.Name2} melts into your embrace.");
                terminal.SetColor("gray");
                terminal.WriteLine($"  *After a long moment, you reluctantly part*");
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {npc.Name2}: \"I never tire of that...\"");
            }
            else
            {
                // Attempting first kiss - charisma affects success
                float kissChance = 0.6f + GetCharismaModifier();
                kissChance = Math.Clamp(kissChance, 0.2f, 0.85f);

                float roll = (float)random.NextDouble();
                if (roll < kissChance && relationLevel <= 30)
                {
                    terminal.SetColor("white");
                    terminal.WriteLine($"  {npc.Name2} doesn't pull away. Your first kiss is soft, questioning,");
                    terminal.WriteLine($"  then deeper as they respond. Time seems to stop.");
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"  {npc.Name2}: *breathless* \"That was... unexpected. But nice.\"");

                    // First kiss is meaningful - use override to allow progression beyond friendship
                    RelationshipSystem.UpdateRelationship(player!, npc, 1, 4, false, true);
                }
                else
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"  {npc.Name2} gently pushes you away.");
                    terminal.SetColor("white");
                    terminal.WriteLine($"  \"Not here. Not yet. Let's... take it slower.\"");
                }
            }

            terminal.WriteLine("");
            await terminal.GetInput("  Press Enter to continue...");
        }

        private async Task HandlePropositionOption(NPC npc, int relationLevel)
        {
            terminal!.SetColor("red");
            terminal.WriteLine($"  You lower your voice suggestively...");
            terminal.SetColor("white");
            terminal.WriteLine($"  \"What do you say we find somewhere more... private?\"");
            terminal.WriteLine("");

            await Task.Delay(500);

            var romanceType = RomanceTracker.Instance.GetRelationType(npc.ID);

            if (romanceType == RomanceRelationType.Spouse ||
                romanceType == RomanceRelationType.Lover ||
                romanceType == RomanceRelationType.FWB)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {npc.Name2}'s eyes darken with desire:");
                terminal.SetColor("bright_red");
                terminal.WriteLine($"  *takes your hand* \"I thought you'd never ask...\"");
                terminal.WriteLine("");

                terminal.SetColor("bright_cyan");
                terminal.WriteLine("  [This will lead to an intimate scene. Continue? Y/N]");
                terminal.WriteLine("");

                string confirm = await terminal.GetInput("  ");
                if (confirm.ToUpper() == "Y")
                {
                    // Trigger intimacy system with full scene
                    await IntimacySystem.Instance.StartIntimateScene(player!, npc, terminal);
                }
                else
                {
                    // Player chose to skip the explicit content, but the encounter still happens
                    // Apply all mechanical benefits (relationship boost, pregnancy check, memory)
                    await IntimacySystem.Instance.ApplyIntimacyBenefitsOnly(player!, npc, terminal);

                    terminal.WriteLine("");
                    terminal.SetColor("dark_magenta");
                    terminal.WriteLine("  [The two of you share an intimate moment together...]");
                    terminal.SetColor("gray");
                    terminal.WriteLine("");
                    terminal.WriteLine("  Some time later, you find yourselves content in each other's arms.");
                    await Task.Delay(1500);
                }
            }
            else
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {npc.Name2} looks surprised:");
                terminal.SetColor("gray");
                terminal.WriteLine($"  \"That's... forward. We're not at that point yet.\"");

                // Might hurt relationship if too early
                if (relationLevel > 40)
                {
                    RelationshipSystem.UpdateRelationship(player!, npc, -1);
                }
            }

            terminal.WriteLine("");
            await terminal.GetInput("  Press Enter to continue...");
        }

        private async Task HandleProvocationOption(NPC npc, int relationLevel)
        {
            terminal!.SetColor("dark_red");
            terminal.WriteLine($"  You challenge {npc.Name2} with a provocative remark...");
            terminal.WriteLine("");

            await Task.Delay(500);

            var profile = npc.Brain?.Personality;
            float aggression = profile?.Aggression ?? 0.5f;

            if (aggression > 0.6f)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {npc.Name2}'s eyes flash with anger:");
                terminal.SetColor("red");
                terminal.WriteLine($"  \"Watch your tongue, or I'll cut it out!\"");

                RelationshipSystem.UpdateRelationship(player!, npc, -1, 2);
            }
            else
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {npc.Name2} scoffs:");
                terminal.SetColor("gray");
                terminal.WriteLine($"  \"Is that supposed to impress me? Pathetic.\"");

                RelationshipSystem.UpdateRelationship(player!, npc, -1);
            }

            terminal.WriteLine("");
            await terminal.GetInput("  Press Enter to continue...");
        }

        private async Task HandleAskToLeaveOption(NPC npc, int relationLevel)
        {
            terminal!.SetColor("bright_yellow");
            terminal.WriteLine($"  You take {npc.Name2}'s hand and look them in the eye...");
            terminal.WriteLine("");
            await Task.Delay(800);

            terminal.SetColor("white");
            terminal.WriteLine($"  \"I'm tired of sneaking around. Leave {npc.SpouseName}. Be with me.\"");
            terminal.WriteLine("");
            await Task.Delay(600);

            var affair = NPCMarriageRegistry.Instance.GetAffair(npc.ID, player!.ID);
            var profile = npc.Brain?.Personality;
            if (affair == null || profile == null)
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  {npc.Name2} stares at you blankly.");
                await terminal.GetInput("  Press Enter to continue...");
                return;
            }

            // CHA-based persuasion check, modified by affair state
            float baseChance = 0.15f;

            // Player charisma is a major factor
            baseChance += Math.Max(0, (player.Charisma - 30) / 150f); // Up to +0.47 at CHA 100

            // Affair depth matters hugely
            baseChance += affair.AffairProgress * 0.002f; // Up to +0.4 at max (200)

            // Low commitment NPCs are easier to convince
            baseChance += (1f - profile.Commitment) * 0.2f;

            // Secret meetings show real connection
            baseChance += Math.Min(0.15f, affair.SecretMeetings * 0.015f);

            // If spouse already suspects, easier to leave ("it's already ruined")
            if (affair.SpouseSuspicion >= 60)
                baseChance += 0.15f;

            // High commitment makes it very hard
            if (profile.Commitment > 0.8f)
                baseChance *= 0.4f;

            baseChance = Math.Clamp(baseChance, 0.05f, 0.85f);

            if (random.NextDouble() < baseChance)
            {
                // They agree to leave!
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"  ═══ A Decision Is Made ═══");
                terminal.WriteLine("");
                await Task.Delay(500);

                terminal.SetColor("yellow");
                if (affair.SpouseSuspicion >= 60)
                    terminal.WriteLine($"  {npc.Name2}'s eyes fill with tears. \"{npc.SpouseName} already suspects. You're right... I choose you.\"");
                else
                    terminal.WriteLine($"  {npc.Name2} squeezes your hand. \"I've been thinking about it too. I can't live this lie anymore.\"");
                terminal.WriteLine("");
                await Task.Delay(800);

                // Offer player a choice - become spouse or just lovers
                terminal.SetColor("cyan");
                terminal.WriteLine($"  [M] \"Marry me.\" - Make them your spouse");
                terminal.WriteLine($"  [L] \"Stay with me.\" - Become lovers");
                terminal.WriteLine($"  [N] \"I changed my mind.\" - Back out");
                terminal.WriteLine("");
                terminal.Write("  Your choice: ");
                string? choice = await terminal.GetInput("");

                if (choice?.Trim().ToUpper() == "M" || choice?.Trim().ToUpper() == "L")
                {
                    bool marry = choice.Trim().ToUpper() == "M";
                    string exSpouseName = npc.SpouseName ?? "their spouse";
                    EnhancedNPCBehaviors.ProcessAffairDivorce(npc, player!, marry);

                    if (marry)
                    {
                        terminal.SetColor("bright_red");
                        terminal.WriteLine($"  {npc.Name2} leaves {exSpouseName} and becomes your spouse!");
                        NewsSystem.Instance?.Newsy(true, $"{npc.Name2} has left {exSpouseName} for {player.Name}!");
                    }
                    else
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine($"  {npc.Name2} is now your lover.");
                        RomanceTracker.Instance.AddLover(npc.ID, 50, false);
                        NewsSystem.Instance?.Newsy(true, $"{npc.Name2} has left {exSpouseName} in a scandal involving {player.Name}!");
                    }
                }
                else
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine($"  {npc.Name2} looks confused as you pull away...");
                    terminal.WriteLine($"  \"You asked ME to leave and now you back out?!\"");
                    RelationshipSystem.UpdateRelationship(player!, npc, -3);
                }
            }
            else
            {
                // They refuse (for now)
                terminal.SetColor("yellow");

                if (profile.Commitment > 0.7f)
                {
                    terminal.WriteLine($"  {npc.Name2} pulls their hand away.");
                    terminal.SetColor("white");
                    terminal.WriteLine($"  \"What we have is... exciting. But I made vows to {npc.SpouseName}. I can't just throw that away.\"");
                }
                else if (affair.AffairProgress < 120)
                {
                    terminal.WriteLine($"  {npc.Name2} hesitates, conflicted.");
                    terminal.SetColor("white");
                    terminal.WriteLine($"  \"I... I'm not ready for that. Not yet. Give me more time.\"");
                }
                else
                {
                    terminal.WriteLine($"  {npc.Name2} looks away, torn.");
                    terminal.SetColor("white");
                    terminal.WriteLine($"  \"I want to... but I'm afraid. What if it all goes wrong?\"");
                }

                // Asking still pushes the affair forward a bit
                affair.AffairProgress = Math.Min(200, affair.AffairProgress + 10);
                RelationshipSystem.UpdateRelationship(player!, npc, 1);
            }

            terminal.WriteLine("");
            await terminal.GetInput("  Press Enter to continue...");
        }

        private async Task HandleMarriageProposal(NPC npc, int relationLevel)
        {
            terminal!.ClearScreen();

            terminal.SetColor("bright_red");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            terminal.WriteLine("║                           MARRIAGE PROPOSAL                                 ║");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            // Check player age
            if (player!.Age < GameConfig.MinimumAgeToMarry)
            {
                terminal.SetColor("red");
                terminal.WriteLine($"  You must be at least {GameConfig.MinimumAgeToMarry} years old to marry!");
                await terminal.GetInput("  Press Enter to continue...");
                return;
            }

            // Check if player already married (and not poly)
            if (RomanceTracker.Instance.IsMarried)
            {
                var existingSpouse = RomanceTracker.Instance.PrimarySpouse;
                if (existingSpouse != null && !existingSpouse.AcceptsPolyamory)
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"  You are already married! Divorce first or discuss polyamory with your spouse.");
                    await terminal.GetInput("  Press Enter to continue...");
                    return;
                }
            }

            // Check gold
            long weddingCost = GameConfig.WeddingCostBase;
            if (player.Gold < weddingCost)
            {
                terminal.SetColor("red");
                terminal.WriteLine($"  A wedding ceremony costs {weddingCost} gold. You only have {player.Gold}.");
                await terminal.GetInput("  Press Enter to continue...");
                return;
            }

            terminal.SetColor("white");
            terminal.WriteLine($"  You take {npc.Name2}'s hands in yours and look into their eyes...");
            terminal.WriteLine("");
            await Task.Delay(1000);

            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"  \"I know we haven't been together long, but every moment");
            terminal.WriteLine($"   with you has been magical. I want to spend my life with you.");
            terminal.WriteLine($"   {npc.Name2}... will you marry me?\"");
            terminal.WriteLine("");

            await Task.Delay(1500);

            // Calculate acceptance chance based on relationship and personality
            var profile = npc.Brain?.Personality;
            float commitment = profile?.Commitment ?? 0.5f;
            float romanticism = profile?.Romanticism ?? 0.5f;

            // Base 60% + commitment bonus + romanticism bonus
            float acceptChance = 0.60f + (commitment * 0.2f) + (romanticism * 0.15f);

            // Relationship bonus if deeply in love
            if (relationLevel <= 15) acceptChance += 0.1f;

            // Apply charisma modifier - a charming proposal is more likely to succeed
            float charismaModifier = GetCharismaModifier();
            acceptChance += charismaModifier;
            acceptChance = Math.Clamp(acceptChance, 0.10f, 0.95f);

            float roll = (float)random.NextDouble();

            if (roll < acceptChance)
            {
                // They say yes!
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {npc.Name2}'s eyes fill with tears of joy...");
                terminal.WriteLine("");
                await Task.Delay(1000);

                terminal.SetColor("bright_magenta");
                terminal.WriteLine($"  *{npc.Name2} throws their arms around you*");
                terminal.SetColor("white");
                terminal.WriteLine($"  \"YES! A thousand times YES! I will marry you!\"");
                terminal.WriteLine("");

                await Task.Delay(1500);

                // Wedding ceremony
                await PerformWeddingCeremony(npc);
            }
            else if (roll < acceptChance + 0.25f)
            {
                // Not ready yet
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {npc.Name2} looks torn, conflicted emotions playing across their face...");
                terminal.WriteLine("");
                await Task.Delay(1000);

                terminal.SetColor("white");
                terminal.WriteLine($"  \"I love you, I really do. But... I'm not ready for marriage.");
                terminal.WriteLine($"   Can we keep things as they are for now? Please don't give up on me.\"");

                // Small relationship boost for the meaningful moment
                RelationshipSystem.UpdateRelationship(player, npc, 1);
            }
            else
            {
                // Rejection
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {npc.Name2} gently pulls their hands away...");
                terminal.WriteLine("");
                await Task.Delay(1000);

                terminal.SetColor("gray");
                terminal.WriteLine($"  \"I'm so sorry. I care about you, but marriage... that's not");
                terminal.WriteLine($"   something I can commit to. I hope we can still be together.\"");

                // Relationship takes a small hit
                RelationshipSystem.UpdateRelationship(player, npc, -1);
            }

            terminal.WriteLine("");
            await terminal.GetInput("  Press Enter to continue...");
        }

        private async Task PerformWeddingCeremony(NPC npc)
        {
            terminal!.ClearScreen();

            terminal.SetColor("bright_yellow");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            terminal.WriteLine("║                      <3<3<3 WEDDING CEREMONY <3<3<3                         ║");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            await Task.Delay(500);

            // Pay for wedding
            player!.Gold -= GameConfig.WeddingCostBase;

            terminal.SetColor("white");
            terminal.WriteLine("  You rush to the Temple to arrange the ceremony...");
            terminal.WriteLine("");
            await Task.Delay(1000);

            terminal.SetColor("bright_cyan");
            terminal.WriteLine("  The priest begins the sacred rites...");
            terminal.WriteLine("");
            await Task.Delay(500);

            terminal.SetColor("yellow");
            terminal.WriteLine($"  \"Dearly beloved, we gather here today to witness the union");
            terminal.WriteLine($"   of {player.Name} and {npc.Name2} in holy matrimony.\"");
            terminal.WriteLine("");
            await Task.Delay(1500);

            terminal.SetColor("white");
            terminal.WriteLine($"  {player.Name}, do you take {npc.Name2} to be your lawful spouse?");
            terminal.SetColor("gray");
            terminal.WriteLine("  \"I do.\"");
            terminal.WriteLine("");
            await Task.Delay(1000);

            terminal.SetColor("white");
            terminal.WriteLine($"  {npc.Name2}, do you take {player.Name} to be your lawful spouse?");
            terminal.SetColor("gray");
            terminal.WriteLine($"  *{npc.Name2} gazes lovingly at you* \"I do.\"");
            terminal.WriteLine("");
            await Task.Delay(1000);

            // Random ceremony message
            var ceremonyMessages = GameConfig.WeddingCeremonyMessages;
            string ceremonyMessage = ceremonyMessages[random.Next(ceremonyMessages.Length)];

            terminal.SetColor("yellow");
            terminal.WriteLine($"  {ceremonyMessage}");
            terminal.WriteLine("");
            await Task.Delay(1000);

            terminal.SetColor("bright_magenta");
            terminal.WriteLine("  <3 You may now kiss your spouse! <3");
            terminal.WriteLine("");
            await Task.Delay(1500);

            // Update relationship to married
            RelationshipSystem.UpdateRelationship(player, npc, 1, 10, true, true);

            // Update player married status
            player.IsMarried = true;
            player.Married = true;
            player.SpouseName = npc.Name2;
            player.MarriedTimes++;

            // Add to RomanceTracker as spouse
            RomanceTracker.Instance.AddSpouse(npc.ID, false);

            // Generate news
            NewsSystem.Instance?.Newsy(true, $"{player.Name} and {npc.Name2} have gotten married! Congratulations to the happy couple!");

            terminal.SetColor("bright_green");
            terminal.WriteLine("══════════════════════════════════════════════════════════════════════════════");
            terminal.WriteLine($"  YOU ARE NOW MARRIED TO {npc.Name2.ToUpper()}!");
            terminal.WriteLine("══════════════════════════════════════════════════════════════════════════════");
            terminal.WriteLine("");

            // Benefits announcement
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("  Marriage Benefits:");
            terminal.SetColor("white");
            terminal.WriteLine("  - +10% XP bonus from combat when your spouse is alive");
            terminal.WriteLine("  - Your spouse may join you in combat as an ally");
            terminal.WriteLine("  - Share a home and have children together");
            terminal.WriteLine("  - Enjoy special intimate moments at home");
            terminal.WriteLine("");

            // Same-sex marriage note
            if (player.Sex == npc.Sex)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("  (You may adopt children at the Royal Orphanage!)");
            }
            else
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("  (Go home and try for children!)");
            }

            terminal.WriteLine("");
            await terminal.GetInput("  Press Enter to continue your new life...");
        }

        /// <summary>
        /// Show farewell based on relationship
        /// </summary>
        private async Task ShowFarewell(NPC npc, int relationLevel, RomanceRelationType romanceType)
        {
            terminal!.WriteLine("");
            terminal.SetColor("yellow");

            string farewell = romanceType switch
            {
                RomanceRelationType.Spouse => $"  {npc.Name2}: \"Don't be gone too long, my love...\"",
                RomanceRelationType.Lover => $"  {npc.Name2}: \"Until next time... I'll be thinking of you.\"",
                RomanceRelationType.FWB => $"  {npc.Name2}: *winks* \"You know where to find me.\"",
                _ => relationLevel switch
                {
                    <= 30 => $"  {npc.Name2}: \"Stay safe out there... for me.\"",
                    <= 50 => $"  {npc.Name2}: \"Good luck, friend!\"",
                    <= 70 => $"  {npc.Name2}: \"Farewell.\"",
                    _ => $"  {npc.Name2}: \"Good riddance.\""
                }
            };

            terminal.WriteLine(farewell);
            terminal.WriteLine("");

            await Task.Delay(1500);
        }

        /// <summary>
        /// Store conversation in NPC's memory
        /// </summary>
        private void StoreConversationMemory(string npcId, ConversationType type)
        {
            if (!npcMemories.ContainsKey(npcId))
                npcMemories[npcId] = new List<ConversationMemory>();

            npcMemories[npcId].Add(new ConversationMemory
            {
                Type = type,
                Date = DateTime.Now
            });

            // Keep only last 20 memories per NPC
            if (npcMemories[npcId].Count > 20)
                npcMemories[npcId].RemoveAt(0);
        }

        private string GetRelationColor(int relationLevel)
        {
            return relationLevel switch
            {
                <= 10 => "bright_red",      // Married
                <= 20 => "bright_magenta",  // Love
                <= 30 => "magenta",         // Passion
                <= 40 => "bright_cyan",     // Friendship
                <= 50 => "cyan",            // Trust
                <= 60 => "bright_green",    // Respect
                <= 70 => "gray",            // Normal
                <= 90 => "yellow",          // Suspicious/Anger
                _ => "red"                  // Enemy/Hate
            };
        }
    }

    /// <summary>
    /// Types of conversation choices
    /// </summary>
    public enum ConversationType
    {
        Chat,
        Personal,
        Flirt,
        Compliment,
        Confess,
        Intimate,
        Proposition,
        Propose,
        Provoke,
        AskToLeave
    }

    /// <summary>
    /// Memory of a conversation
    /// </summary>
    public class ConversationMemory
    {
        public ConversationType Type { get; set; }
        public DateTime Date { get; set; }
    }

    /// <summary>
    /// Tracks the conversation history and state with a specific NPC
    /// </summary>
    public class ConversationState
    {
        public DateTime LastConversationDate { get; set; }
        public HashSet<string> TopicsDiscussed { get; set; } = new();
        public int PersonalQuestionsAsked { get; set; } = 0;
        public int FlirtSuccessCount { get; set; } = 0;
        public bool LastFlirtWasPositive { get; set; } = false;
        public int TotalConversations { get; set; } = 0;
        public bool HasConfessed { get; set; } = false;
        public bool ConfessionAccepted { get; set; } = false;
    }

    /// <summary>
    /// Represents a chat topic with its details
    /// </summary>
    public class ChatTopic
    {
        public string TopicId { get; set; } = "";
        public string PlayerLine { get; set; } = "";
        public string Category { get; set; } = "";
        public int Priority { get; set; } = 1;
    }

    /// <summary>
    /// Represents a conversation choice
    /// </summary>
    public class ConversationOption
    {
        public ConversationType Type { get; set; }
        public string Text { get; set; } = "";
        public string Color { get; set; } = "white";
        public string? TopicId { get; set; }
        public bool Disabled { get; set; } = false;
    }
}
