using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UsurperRemake.UI;
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
            // v0.57.18 (third-round audit): cap at serialization time. One entry per NPC
            // the player has ever spoken to + per-entry TopicsDiscussed HashSet that grows
            // unbounded with conversation breadth. Long playthroughs talking to many NPCs
            // about many topics easily reach hundreds of KB. Selection: keep the most
            // recently spoken-to NPCs (LastConversationDate desc). Topics are unordered
            // (HashSet) so an arbitrary N is kept — re-discussing an old topic is harmless,
            // OOM is not.
            var ordered = npcConversationStates.Count > GameConfig.MaxSerializedConversationStates
                ? (IEnumerable<KeyValuePair<string, ConversationState>>)npcConversationStates
                    .OrderByDescending(kvp => kvp.Value.LastConversationDate)
                    .Take(GameConfig.MaxSerializedConversationStates)
                : npcConversationStates;

            var result = new List<UsurperRemake.Systems.ConversationStateData>();
            foreach (var kvp in ordered)
            {
                var topics = kvp.Value.TopicsDiscussed.Count > GameConfig.MaxSerializedTopicsDiscussedPerConvo
                    ? kvp.Value.TopicsDiscussed.Take(GameConfig.MaxSerializedTopicsDiscussedPerConvo).ToList()
                    : new List<string>(kvp.Value.TopicsDiscussed);

                result.Add(new UsurperRemake.Systems.ConversationStateData
                {
                    NPCId = kvp.Key,
                    FlirtSuccessCount = kvp.Value.FlirtSuccessCount,
                    LastFlirtWasPositive = kvp.Value.LastFlirtWasPositive,
                    TotalConversations = kvp.Value.TotalConversations,
                    PersonalQuestionsAsked = kvp.Value.PersonalQuestionsAsked,
                    HasConfessed = kvp.Value.HasConfessed,
                    ConfessionAccepted = kvp.Value.ConfessionAccepted,
                    TopicsDiscussed = topics,
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
                    return Loc.Get("dialogue.charisma_exceptional");
                else if (charisma >= CHARISMA_HIGH)
                    return Loc.Get("dialogue.charisma_high");
                else if (charisma < CHARISMA_LOW)
                    return Loc.Get("dialogue.charisma_low_positive");
            }
            else
            {
                if (charisma < CHARISMA_LOW)
                    return Loc.Get("dialogue.charisma_low_negative");
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
            _pendingNPCNarration = null;

            // Get or create conversation state for this NPC
            if (!npcConversationStates.ContainsKey(npc.ID))
                npcConversationStates[npc.ID] = new ConversationState();

            // Get relationship status
            int relationLevel = RelationshipSystem.GetRelationshipStatus(player, npc);
            var romanceType = RomanceTracker.Instance.GetRelationType(npc.ID);

            // Display conversation header (text mode only — Electron renders portrait + name in overlay)
            if (!GameConfig.ElectronMode)
            {
                await ShowConversationHeader(npc, relationLevel, romanceType);
            }

            // Show NPC's greeting based on relationship — captured for Electron emit
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

            if (GameConfig.ElectronMode)
            {
                ElectronBridge.EmitDialogueClose();
            }
        }

        // Holds the NPC's last spoken text (greeting / response). Used by the
        // Electron emit so the graphical client always renders fresh narration
        // alongside the choice buttons. Reset at the start of each conversation.
        private string? _pendingNPCNarration;

        /// <summary>
        /// Display the conversation header with NPC info
        /// </summary>
        private async Task ShowConversationHeader(NPC npc, int relationLevel, RomanceRelationType romanceType)
        {
            terminal!.ClearScreen();
            string relColor = GetRelationColor(relationLevel);
            string romanticStatus = romanceType != RomanceRelationType.None ? $" [{romanceType}]" : "";
            if (!GameConfig.ScreenReaderMode)
            {
                terminal.SetColor("bright_cyan");
                terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                terminal.SetColor(relColor);
                terminal.WriteLine($"║  {npc.Name2}{romanticStatus,-60}  ║");
                terminal.SetColor("bright_cyan");
                terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            }
            else
            {
                terminal.SetColor(relColor);
                terminal.WriteLine($"  {npc.Name2}{romanticStatus}");
            }
            terminal.WriteLine("");

            // NPC description
            terminal.SetColor("gray");
            terminal.WriteLine($"  Level {npc.Level} {npc.Race} {npc.ClassName}");

            // Physical description based on gender and traits
            string physicalDesc = GeneratePhysicalDescription(npc);
            terminal.SetColor("white");
            terminal.WriteLine($"  {physicalDesc}");
            terminal.WriteLine("");

            // v0.62.1 (player report Lv.6 Sage on Main Street): the orange
            // "[DEBUG] Relationship Stats" block that used to render here --
            // exposing Relation Level integers, Flirt Receptiveness percentages,
            // Has Confessed booleans, etc. -- was a dev tool that shipped to
            // players. Removed entirely. The information players genuinely
            // want (orientation, relationship band, romance status, abstracted
            // personality, abstracted flirt receptiveness) is now surfaced
            // through the Level Master's Crystal Ball scry instead, gated
            // behind a small in-game cost so it feels earned.
            await Task.Delay(100);
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

            // Phase 1.5 dialogue enhancer: layer contextual flavor (mood / memory /
            // witness / personality / faction / etc) on top of the base greeting.
            // Language-gated to en/hu inside Enhance() so non-supported languages
            // get the unmodified base greeting.
            if (player != null)
                greeting = UsurperRemake.Systems.DialogueEnhancer.Enhance(greeting, npc, player);

            _pendingNPCNarration = greeting;

            if (GameConfig.ElectronMode)
            {
                // Greeting is folded into the next ShowConversationOptions emit so the
                // Electron overlay opens with portrait + greeting + choices in one render.
                return;
            }

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
            int idx = random.Next(5) + 1;
            return Loc.Get($"dialogue.vn.greeting.spouse_{idx}");
        }

        private string GetLoverGreeting(NPC npc)
        {
            int idx = random.Next(5) + 1;
            return Loc.Get($"dialogue.vn.greeting.lover_{idx}");
        }

        private string GetFWBGreeting(NPC npc)
        {
            int idx = random.Next(5) + 1;
            return Loc.Get($"dialogue.vn.greeting.fwb_{idx}");
        }

        private string GetIntimateGreeting(NPC npc)
        {
            int idx = random.Next(4) + 1;
            return Loc.Get($"dialogue.vn.greeting.intimate_{idx}");
        }

        private string GetFriendlyGreeting(NPC npc)
        {
            int idx = random.Next(4) + 1;
            return Loc.Get($"dialogue.vn.greeting.friendly_{idx}");
        }

        private string GetRespectfulGreeting(NPC npc)
        {
            int idx = random.Next(4) + 1;
            return Loc.Get($"dialogue.vn.greeting.respectful_{idx}", player!.Name);
        }

        private string GetNeutralGreeting(NPC npc)
        {
            int idx = random.Next(4) + 1;
            return Loc.Get($"dialogue.vn.greeting.neutral_{idx}");
        }

        private string GetWaryGreeting(NPC npc)
        {
            int idx = random.Next(4) + 1;
            return Loc.Get($"dialogue.vn.greeting.wary_{idx}");
        }

        private string GetHostileGreeting(NPC npc)
        {
            int idx = random.Next(4) + 1;
            return Loc.Get($"dialogue.vn.greeting.hostile_{idx}");
        }

        /// <summary>
        /// Show conversation options based on relationship
        /// </summary>
        private async Task<bool> ShowConversationOptions(NPC npc, int relationLevel, RomanceRelationType romanceType)
        {
            var options = BuildConversationOptions(npc, relationLevel, romanceType);

            if (GameConfig.ElectronMode)
            {
                EmitConversationDialogue(npc, options, relationLevel, romanceType);
            }
            else
            {
                terminal!.SetColor("cyan");
                terminal.WriteLine($"  {Loc.Get("dialogue.what_to_say")}");
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
                terminal.WriteLine($"  [0] {Loc.Get("dialogue.end_conversation")}");
                terminal.WriteLine("");
            }

            string choice = await terminal!.GetInput(Loc.Get("ui.your_choice"));

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

            if (!GameConfig.ElectronMode)
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  {Loc.Get("dialogue.not_understood")}");
            }
            await Task.Delay(1000);
            return true;
        }

        /// <summary>
        /// Emit the current NPC dialogue state to the Electron overlay: speaker
        /// portrait, last narration text, and the available player choices. The
        /// graphical client renders this as a side-panel with the NPC portrait,
        /// the body text in a speech bubble, and choice buttons below. Input
        /// flows back through the shared GetInput call.
        /// </summary>
        private void EmitConversationDialogue(NPC npc, List<ConversationOption> options,
            int relationLevel, RomanceRelationType romanceType)
        {
            var choices = new List<ElectronBridge.DialogueChoiceData>();
            for (int i = 0; i < options.Count; i++)
            {
                var opt = options[i];
                choices.Add(new ElectronBridge.DialogueChoiceData
                {
                    Key = (i + 1).ToString(),
                    Text = opt.Text,
                    Style = MapConversationStyle(opt),
                    Disabled = opt.Disabled
                });
            }
            choices.Add(new ElectronBridge.DialogueChoiceData
            {
                Key = "0",
                Text = Loc.Get("dialogue.end_conversation"),
                Style = "leave"
            });

            string speakerLabel = npc.Name2;
            string portraitKey = $"npc:{npc.Name2}";
            string narration = _pendingNPCNarration ?? "";
            string relLabel = romanceType != RomanceRelationType.None
                ? romanceType.ToString()
                : DescribeRelationLevel(relationLevel);
            string relColor = GetRelationColor(relationLevel);

            ElectronBridge.EmitDialogue(
                speaker: speakerLabel,
                portraitKey: portraitKey,
                text: narration,
                choices: choices,
                relationLabel: relLabel,
                relationColor: relColor,
                mood: null);
        }

        private static string MapConversationStyle(ConversationOption opt)
        {
            return opt.Type switch
            {
                ConversationType.Flirt => "flirt",
                ConversationType.Compliment => "flirt",
                ConversationType.Confess => "confess",
                ConversationType.Propose => "propose",
                ConversationType.Intimate => "intimate",
                ConversationType.AskToLeave => "danger",
                ConversationType.Provoke => "hostile",
                ConversationType.Personal => "info",
                _ => "normal"
            };
        }

        private static string DescribeRelationLevel(int relationLevel) => relationLevel switch
        {
            <= 20 => "Beloved",
            <= 40 => "Friendly",
            <= 60 => "Respected",
            <= 70 => "Neutral",
            <= 90 => "Wary",
            _ => "Hostile"
        };

        /// <summary>
        /// Build available conversation options - dynamic based on context and history
        /// </summary>
        private List<ConversationOption> BuildConversationOptions(NPC npc, int relationLevel, RomanceRelationType romanceType)
        {
            var options = new List<ConversationOption>();
            var profile = npc.Brain?.Personality;
            var state = npcConversationStates.GetValueOrDefault(npc.ID) ?? new ConversationState();
            bool isAttracted = profile?.IsAttractedTo(player!.Sex == CharacterSex.Female ? GenderIdentity.Female : GenderIdentity.Male) ?? true;

            // v0.63.0 slice 2 C2: family awareness. When the NPC is the player's
            // own adult child, the menu hides Flirt / Intimate / Proposition /
            // Propose entirely (defense in depth + don't even tempt the player)
            // and surfaces a family-specific chat topic in the [Chat] slot.
            // Compliment / Personal / Ask-about questions / Chat about the world
            // still work -- you can still talk to your kid, just not date them.
            var family = UsurperRemake.Systems.FamilySystem.Instance;
            bool isAdultChild = family?.IsAdultChildOf(npc, player!) ?? false;

            // Get a dynamic chat topic. For adult children, swap in a parent-focused
            // topic if available so the player can ask "how have you been?" / "what
            // do you do now?" etc. instead of the random world-topic small talk.
            ConversationOption? familyChatOption = isAdultChild ? BuildAdultChildChatTopic(npc, state) : null;
            if (familyChatOption != null)
            {
                options.Add(familyChatOption);
            }
            else
            {
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
            // v0.63.0 slice 2 C2: hidden entirely for the player's adult child.
            int maxFlirtsThisSession = state.FlirtSuccessCount < 2 ? 5 : 3;
            if (!isAdultChild && relationLevel <= 80 && isAttracted && flirtCountThisSession < maxFlirtsThisSession)
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
            else if (!isAdultChild && flirtCountThisSession >= maxFlirtsThisSession && relationLevel > 30)
            {
                // Too much flirting - they notice
                options.Add(new ConversationOption
                {
                    Type = ConversationType.Flirt,
                    Text = Loc.Get("dialogue.opt_flirt_limit"),
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

            // Romantic options (if in relationship or friendly).
            // v0.63.0 slice 2 C2: hidden entirely for the player's adult child.
            // (Note: legacy saves could have a Spouse / Lover romance type recorded
            // against an NPC who's now resolved as a biological child via the
            // backfill -- the gate hides the menu items, the incest check at
            // PerformMarriage / CheckForPregnancy / etc. blocks the actions.)
            // Note: relationLevel scale is 0=Soulmate, 50=Neutral, 100=Hated
            // So <= 50 means neutral or better
            if (!isAdultChild && (romanceType != RomanceRelationType.None || relationLevel <= 50))
            {
                if (romanceType == RomanceRelationType.Spouse || romanceType == RomanceRelationType.Lover)
                {
                    options.Add(new ConversationOption
                    {
                        Type = ConversationType.Intimate,
                        Text = Loc.Get("dialogue.opt_kiss"),
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
                            Text = Loc.Get("dialogue.opt_confess"),
                            Color = "bright_magenta"
                        });
                    }
                }
            }

            // Physical intimacy (if in romantic relationship)
            // v0.63.0 slice 2 C2: hidden for adult children.
            if (!isAdultChild
                && (romanceType == RomanceRelationType.Spouse
                    || romanceType == RomanceRelationType.Lover
                    || romanceType == RomanceRelationType.FWB))
            {
                options.Add(new ConversationOption
                {
                    Type = ConversationType.Proposition,
                    Text = Loc.Get("dialogue.opt_proposition"),
                    Color = "red"
                });
            }

            // Marriage proposal (if lover and not already married to them)
            // v0.63.0 slice 2 C2: hidden for adult children.
            if (!isAdultChild && romanceType == RomanceRelationType.Lover && relationLevel <= 20)
            {
                options.Add(new ConversationOption
                {
                    Type = ConversationType.Propose,
                    Text = Loc.Get("dialogue.opt_proposal"),
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
                        Text = Loc.Get("dialogue.opt_leave_spouse", npc.SpouseName),
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
                    Text = Loc.Get("dialogue.opt_provoke"),
                    Color = "dark_red"
                });
            }

            return options;
        }

        /// <summary>
        /// v0.63.0 slice 2 C2: when talking to the player's adult child, cycle
        /// through a small set of parent-flavored chat topics (remember when,
        /// how is your mother, what's your trade now, are you well, do you
        /// need anything) instead of the generic small-talk pool. Topic id is
        /// recorded in `topicsDiscussedThisSession` so cycling doesn't repeat.
        /// Returns null when all five topics have already been raised this
        /// session, in which case the caller falls back to a generic chat
        /// topic so the [Chat] slot doesn't disappear.
        /// </summary>
        private ConversationOption? BuildAdultChildChatTopic(NPC adultChild, ConversationState state)
        {
            // The five slots. Each "topic" carries its own id so the dialogue
            // engine can pull the right response variant when the player picks
            // it. Per-session dedup via topicsDiscussedThisSession.
            string[] slotIds = new[]
            {
                "family_child_remember_when",
                "family_child_other_parent",
                "family_child_trade_now",
                "family_child_are_you_well",
                "family_child_do_you_need",
            };

            foreach (var id in slotIds)
            {
                if (topicsDiscussedThisSession.Contains(id)) continue;
                return new ConversationOption
                {
                    Type = ConversationType.Chat,
                    Text = Loc.Get($"dialogue.opt_{id}"),
                    Color = "bright_cyan",
                    TopicId = id,
                };
            }
            return null;
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
                    PlayerLine = Loc.Get("dialogue.opt_generic_continue"),
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
                    topics.Add(new ChatTopic { TopicId = "warrior_training", PlayerLine = Loc.Get("dialogue.vn.chat_topic.warrior_training"), Category = "class", Priority = 3 });
                    topics.Add(new ChatTopic { TopicId = "warrior_battles", PlayerLine = Loc.Get("dialogue.vn.chat_topic.warrior_battles"), Category = "class", Priority = 4 });
                    topics.Add(new ChatTopic { TopicId = "warrior_weapons", PlayerLine = Loc.Get("dialogue.vn.chat_topic.warrior_weapons"), Category = "class", Priority = 2 });
                    break;
                case CharacterClass.Magician:
                case CharacterClass.Sage:
                    topics.Add(new ChatTopic { TopicId = "mage_magic", PlayerLine = Loc.Get("dialogue.vn.chat_topic.mage_magic"), Category = "class", Priority = 4 });
                    topics.Add(new ChatTopic { TopicId = "mage_spells", PlayerLine = Loc.Get("dialogue.vn.chat_topic.mage_spells"), Category = "class", Priority = 3 });
                    topics.Add(new ChatTopic { TopicId = "mage_research", PlayerLine = Loc.Get("dialogue.vn.chat_topic.mage_research"), Category = "class", Priority = 2 });
                    break;
                case CharacterClass.Cleric:
                case CharacterClass.Paladin:
                    topics.Add(new ChatTopic { TopicId = "cleric_faith", PlayerLine = Loc.Get("dialogue.vn.chat_topic.cleric_faith"), Category = "class", Priority = 4 });
                    topics.Add(new ChatTopic { TopicId = "cleric_calling", PlayerLine = Loc.Get("dialogue.vn.chat_topic.cleric_calling"), Category = "class", Priority = 3 });
                    topics.Add(new ChatTopic { TopicId = "cleric_miracles", PlayerLine = Loc.Get("dialogue.vn.chat_topic.cleric_miracles"), Category = "class", Priority = 2 });
                    break;
                case CharacterClass.Assassin:
                    topics.Add(new ChatTopic { TopicId = "rogue_shadows", PlayerLine = Loc.Get("dialogue.vn.chat_topic.rogue_shadows"), Category = "class", Priority = 3 });
                    topics.Add(new ChatTopic { TopicId = "rogue_jobs", PlayerLine = Loc.Get("dialogue.vn.chat_topic.rogue_jobs"), Category = "class", Priority = 4 });
                    topics.Add(new ChatTopic { TopicId = "rogue_secrets", PlayerLine = Loc.Get("dialogue.vn.chat_topic.rogue_secrets"), Category = "class", Priority = 2 });
                    break;
                case CharacterClass.Ranger:
                    topics.Add(new ChatTopic { TopicId = "ranger_wilds", PlayerLine = Loc.Get("dialogue.vn.chat_topic.ranger_wilds"), Category = "class", Priority = 4 });
                    topics.Add(new ChatTopic { TopicId = "ranger_tracking", PlayerLine = Loc.Get("dialogue.vn.chat_topic.ranger_tracking"), Category = "class", Priority = 3 });
                    break;
                case CharacterClass.Bard:
                case CharacterClass.Jester:
                    topics.Add(new ChatTopic { TopicId = "bard_stories", PlayerLine = Loc.Get("dialogue.vn.chat_topic.bard_stories"), Category = "class", Priority = 4 });
                    topics.Add(new ChatTopic { TopicId = "bard_music", PlayerLine = Loc.Get("dialogue.vn.chat_topic.bard_music"), Category = "class", Priority = 3 });
                    break;
                case CharacterClass.Alchemist:
                    topics.Add(new ChatTopic { TopicId = "alchemist_potions", PlayerLine = Loc.Get("dialogue.vn.chat_topic.alchemist_potions"), Category = "class", Priority = 4 });
                    topics.Add(new ChatTopic { TopicId = "alchemist_ingredients", PlayerLine = Loc.Get("dialogue.vn.chat_topic.alchemist_ingredients"), Category = "class", Priority = 3 });
                    break;
                default:
                    topics.Add(new ChatTopic { TopicId = "generic_work", PlayerLine = Loc.Get("dialogue.vn.chat_topic.generic_work"), Category = "generic", Priority = 2 });
                    break;
            }

            // Universal topics
            topics.Add(new ChatTopic { TopicId = "dungeon_rumors", PlayerLine = Loc.Get("dialogue.vn.chat_topic.dungeon_rumors"), Category = "adventure", Priority = 3 });
            topics.Add(new ChatTopic { TopicId = "town_news", PlayerLine = Loc.Get("dialogue.vn.chat_topic.town_news"), Category = "social", Priority = 2 });
            topics.Add(new ChatTopic { TopicId = "weather", PlayerLine = Loc.Get("dialogue.vn.chat_topic.weather"), Category = "generic", Priority = 1 });
            topics.Add(new ChatTopic { TopicId = "life_goals", PlayerLine = Loc.Get("dialogue.vn.chat_topic.life_goals"), Category = "personal", Priority = 3 });
            topics.Add(new ChatTopic { TopicId = "origins", PlayerLine = Loc.Get("dialogue.vn.chat_topic.origins"), Category = "personal", Priority = 4 });
            topics.Add(new ChatTopic { TopicId = "hobbies", PlayerLine = Loc.Get("dialogue.vn.chat_topic.hobbies"), Category = "personal", Priority = 2 });

            // Personality-based topics
            if (profile != null)
            {
                if (profile.Romanticism > 0.6f)
                    topics.Add(new ChatTopic { TopicId = "romance_views", PlayerLine = Loc.Get("dialogue.vn.chat_topic.romance_views"), Category = "romantic", Priority = 3 });
                if (profile.Sociability > 0.7f)
                    topics.Add(new ChatTopic { TopicId = "friends", PlayerLine = Loc.Get("dialogue.vn.chat_topic.friends"), Category = "social", Priority = 3 });
                if (profile.Commitment > 0.7f)
                    topics.Add(new ChatTopic { TopicId = "family", PlayerLine = Loc.Get("dialogue.vn.chat_topic.family"), Category = "personal", Priority = 4 });
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
                int idx = random.Next(5) + 1;
                return Loc.Get($"dialogue.vn.personal.deep_{idx}");
            }
            else if (state.PersonalQuestionsAsked >= 1)
            {
                int idx = random.Next(4) + 1;
                return Loc.Get($"dialogue.vn.personal.followup_{idx}");
            }
            return Loc.Get("dialogue.vn.personal.default");
        }

        /// <summary>
        /// Get a flirt line based on progression and context
        /// </summary>
        private string? GetFlirtLine(NPC npc, ConversationState state, int relationLevel, int sessionFlirts)
        {
            // First flirt of conversation - be subtle
            if (sessionFlirts == 0)
            {
                int idx = random.Next(4) + 1;
                return Loc.Get($"dialogue.vn.flirt.subtle_{idx}");
            }
            // Second flirt - a bit bolder
            else if (sessionFlirts == 1)
            {
                if (state.LastFlirtWasPositive)
                {
                    int idx = random.Next(4) + 1;
                    return Loc.Get($"dialogue.vn.flirt.bolder_{idx}");
                }
                else
                {
                    // They weren't receptive before - be more careful
                    int idx = random.Next(3) + 1;
                    return Loc.Get($"dialogue.vn.flirt.careful_{idx}");
                }
            }
            // Third flirt - make it count
            else if (sessionFlirts == 2)
            {
                // Direct flirts if they've been receptive
                int idx = random.Next(3) + 1;
                return Loc.Get($"dialogue.vn.flirt.direct_{idx}");
            }
            // Allow more flirt attempts if still building relationship
            else if (sessionFlirts >= 3 && state.FlirtSuccessCount < 2)
            {
                int idx = random.Next(3) + 1;
                return Loc.Get($"dialogue.vn.flirt.persistent_{idx}");
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
                        compliments.Add(Loc.Get("dialogue.vn.compliment.warrior_1"));
                        compliments.Add(Loc.Get("dialogue.vn.compliment.warrior_2"));
                        break;
                    case CharacterClass.Magician:
                    case CharacterClass.Sage:
                        compliments.Add(Loc.Get("dialogue.vn.compliment.mage_1"));
                        compliments.Add(Loc.Get("dialogue.vn.compliment.mage_2"));
                        break;
                    case CharacterClass.Cleric:
                    case CharacterClass.Paladin:
                        compliments.Add(Loc.Get("dialogue.vn.compliment.cleric_1"));
                        compliments.Add(Loc.Get("dialogue.vn.compliment.cleric_2"));
                        break;
                    default:
                        compliments.Add(Loc.Get("dialogue.vn.compliment.default_1"));
                        compliments.Add(Loc.Get("dialogue.vn.compliment.default_2"));
                        break;
                }

                if (profile?.Sociability > 0.6f)
                    compliments.Add(Loc.Get("dialogue.vn.compliment.sociable"));
                if (npc.Level > 20)
                    compliments.Add(Loc.Get("dialogue.vn.compliment.high_level", npc.Level));

                return compliments[random.Next(compliments.Count)];
            }
            else
            {
                // Second compliment - more personal
                int idx = random.Next(4) + 1;
                return Loc.Get($"dialogue.vn.compliment.personal_{idx}");
            }
        }

        /// <summary>
        /// Handle the player's conversation choice
        /// </summary>
        private async Task HandleConversationChoice(NPC npc, ConversationOption option, int relationLevel)
        {
            // Text-mode redraws header for visual continuity. Electron mode keeps
            // the overlay open and only refreshes narration via _pendingNPCNarration
            // when ShowConversationOptions emits at the next loop iteration.
            if (!GameConfig.ElectronMode)
            {
                terminal!.ClearScreen();
                await ShowConversationHeader(npc, relationLevel, RomanceTracker.Instance.GetRelationType(npc.ID));
            }

            // Don't process disabled options
            if (option.Disabled)
            {
                _pendingNPCNarration = Loc.Get("dialogue.decide_not_push");
                if (!GameConfig.ElectronMode)
                {
                    terminal!.SetColor("gray");
                    terminal.WriteLine($"  {Loc.Get("dialogue.decide_not_push")}");
                    await terminal.GetInput($"  {Loc.Get("ui.press_enter")}");
                }
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

            // Phase 1.5 dialogue enhancer: layer contextual flavor on the NPC's
            // topic reply. Language-gated inside Enhance().
            if (player != null)
                response = UsurperRemake.Systems.DialogueEnhancer.Enhance(response, npc, player);

            terminal!.SetColor("yellow");
            terminal.WriteLine($"  {Loc.Get("dialogue.narr_considers", npc.Name2)}");
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
                terminal.WriteLine($"  {Loc.Get("dialogue.narr_happy_talk")}");
                RelationshipSystem.UpdateRelationship(player!, npc, 1);
            }
            else if (sociability > 0.4f && random.NextDouble() < 0.5)
            {
                terminal.WriteLine("");
                terminal.SetColor("gray");
                terminal.WriteLine($"  {Loc.Get("dialogue.narr_warm_up")}");
            }

            // Track conversation progress
            state.TopicsDiscussed.Add(topicId ?? "generic");
            state.LastConversationDate = DateTime.Now;

            terminal.WriteLine("");
            await terminal.GetInput($"  {Loc.Get("ui.press_enter")}");
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
                    return Loc.Get(isOpen ? "dialogue.vn.topic.warrior_training_open" : "dialogue.vn.topic.warrior_training_closed");
                case "warrior_battles":
                    return Loc.Get($"dialogue.vn.topic.warrior_battles_{random.Next(4) + 1}");
                case "warrior_weapons":
                    return Loc.Get("dialogue.vn.topic.warrior_weapons");

                // Mage topics
                case "mage_magic":
                    return Loc.Get(isOpen ? "dialogue.vn.topic.mage_magic_open" : "dialogue.vn.topic.mage_magic_closed");
                case "mage_spells":
                    return Loc.Get($"dialogue.vn.topic.mage_spells_{random.Next(3) + 1}");
                case "mage_research":
                    return Loc.Get("dialogue.vn.topic.mage_research");

                // Cleric/Paladin topics
                case "cleric_faith":
                    return Loc.Get($"dialogue.vn.topic.cleric_faith_{random.Next(3) + 1}");
                case "cleric_calling":
                    return Loc.Get(isOpen ? "dialogue.vn.topic.cleric_calling_open" : "dialogue.vn.topic.cleric_calling_closed");
                case "cleric_miracles":
                    return Loc.Get("dialogue.vn.topic.cleric_miracles");

                // Rogue topics
                case "rogue_shadows":
                    return Loc.Get(isOpen ? "dialogue.vn.topic.rogue_shadows_open" : "dialogue.vn.topic.rogue_shadows_closed");
                case "rogue_jobs":
                    return Loc.Get("dialogue.vn.topic.rogue_jobs");
                case "rogue_secrets":
                    return Loc.Get("dialogue.vn.topic.rogue_secrets");

                // Ranger topics
                case "ranger_wilds":
                    return Loc.Get($"dialogue.vn.topic.ranger_wilds_{random.Next(3) + 1}");
                case "ranger_tracking":
                    return Loc.Get("dialogue.vn.topic.ranger_tracking");

                // Monk topics
                case "monk_discipline":
                    return Loc.Get("dialogue.vn.topic.monk_discipline");
                case "monk_meditation":
                    return Loc.Get("dialogue.vn.topic.monk_meditation");

                // Universal topics
                case "dungeon_rumors":
                    return Loc.Get($"dialogue.vn.topic.dungeon_rumors_{random.Next(4) + 1}");
                case "town_news":
                    return Loc.Get($"dialogue.vn.topic.town_news_{random.Next(4) + 1}");
                case "weather":
                    return Loc.Get("dialogue.vn.topic.weather");
                case "life_goals":
                    return Loc.Get(isOpen ? "dialogue.vn.topic.life_goals_open" : "dialogue.vn.topic.life_goals_closed");
                case "origins":
                    return Loc.Get(isOpen ? "dialogue.vn.topic.origins_open" : "dialogue.vn.topic.origins_closed");
                case "hobbies":
                    return Loc.Get($"dialogue.vn.topic.hobbies_{random.Next(3) + 1}");
                case "romance_views":
                    return Loc.Get(isOpen ? "dialogue.vn.topic.romance_views_open" : "dialogue.vn.topic.romance_views_closed");
                case "friends":
                    return Loc.Get("dialogue.vn.topic.friends");
                case "family":
                    return Loc.Get(isOpen ? "dialogue.vn.topic.family_open" : "dialogue.vn.topic.family_closed");

                // v0.63.0 slice 2 C2: adult-child-specific topics. Tone shifts on
                // SoulAtGraduation (the snapshot at coming-of-age, immune to later
                // alignment drift): virtuous (> 100), evil (< -100), neutral.
                case "family_child_remember_when":
                case "family_child_other_parent":
                case "family_child_trade_now":
                case "family_child_are_you_well":
                case "family_child_do_you_need":
                    return GetAdultChildTopicResponse(npc, topicId);

                default:
                    return Loc.Get($"dialogue.vn.topic.generic_{random.Next(3) + 1}");
            }
        }

        /// <summary>
        /// v0.63.0 slice 2 C2: tone-keyed response for a family-flavored chat
        /// topic. Soul band determines the slot: virtuous adult children are
        /// warm, neutrals are casual, evils are guarded. Three variants per
        /// topic per tone (1/2/3) so the conversation doesn't read identical
        /// on repeat playthroughs.
        /// </summary>
        private string GetAdultChildTopicResponse(NPC adultChild, string topicId)
        {
            int soul = adultChild.SoulAtGraduation;
            string tone = soul > 100 ? "virtuous" : (soul < -100 ? "evil" : "neutral");
            // 2 variants per tone per topic (keeps loc volume to 30 response keys
            // instead of 45). Re-encounter dedup is handled by
            // topicsDiscussedThisSession at the calling site so 2 is plenty.
            return Loc.Get($"dialogue.vn.topic.{topicId}_{tone}_{random.Next(2) + 1}");
        }

        private async Task HandlePersonalOption(NPC npc, int relationLevel)
        {
            var state = npcConversationStates.GetValueOrDefault(npc.ID) ?? new ConversationState();
            state.PersonalQuestionsAsked++;
            npcConversationStates[npc.ID] = state;

            terminal!.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("dialogue.narr_ask_personal", npc.Name2)}");
            terminal.WriteLine("");

            await Task.Delay(500);

            var profile = npc.Brain?.Personality;

            // They open up more at better relationships. Phase 1.5 enhancer wraps
            // the NPC's actual personal-story reply with mood / memory / witness /
            // personality / faction flavor.
            if (relationLevel <= 40) // Friend or better
            {
                string personalLine = GeneratePersonalStory(npc, true);
                if (player != null)
                    personalLine = UsurperRemake.Systems.DialogueEnhancer.Enhance(personalLine, npc, player);
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {Loc.Get("dialogue.narr_opens_up", npc.Name2)}");
                terminal.SetColor("white");
                terminal.WriteLine($"  \"{personalLine}\"");

                // Relationship boost for showing interest
                RelationshipSystem.UpdateRelationship(player!, npc, 1);
            }
            else if (relationLevel <= 70)
            {
                string personalLine = GeneratePersonalStory(npc, false);
                if (player != null)
                    personalLine = UsurperRemake.Systems.DialogueEnhancer.Enhance(personalLine, npc, player);
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {Loc.Get("dialogue.narr_shares_little", npc.Name2)}");
                terminal.SetColor("white");
                terminal.WriteLine($"  \"{personalLine}\"");
            }
            else
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {Loc.Get("dialogue.narr_guarded", npc.Name2)}");
                terminal.SetColor("white");
                terminal.WriteLine($"  \"{Loc.Get("dialogue.vn.personal.guarded_reply")}\"");
            }

            terminal.WriteLine("");
            await terminal.GetInput($"  {Loc.Get("ui.press_enter")}");
        }

        private string GeneratePersonalStory(NPC npc, bool intimate)
        {
            int idx = random.Next(4) + 1;
            string keyBase = intimate ? "dialogue.vn.story.intimate_" : "dialogue.vn.story.casual_";
            return Loc.Get(keyBase + idx, npc.ClassName);
        }

        private async Task HandleFlirtOption(NPC npc, int relationLevel)
        {
            // v0.63.0 slice 4 (audit m5): dead-NPC guard. The dialogue layer
            // can be re-entered against a permadied NPC if the menu render
            // raced a permadeath cascade -- refuse to operate.
            if (npc.IsDead) return;

            var profile = npc.Brain?.Personality;
            var state = npcConversationStates.GetValueOrDefault(npc.ID) ?? new ConversationState();
            bool isAttracted = profile?.IsAttractedTo(player!.Sex == CharacterSex.Female ? GenderIdentity.Female : GenderIdentity.Male) ?? true;
            // v0.63.0 slice 4 (audit m1): single flip-once guard. Pre-fix
            // FlirtSuccessCount was incremented in the spouse/lover branch
            // AND in the regular-flirt success branch lower down, allowing
            // one call to double-bump when the conversation re-entered the
            // success branch. Caller-side flirtCountThisSession is unrelated
            // and stays as-is.
            bool flirtSuccessRecorded = false;

            // If NPC is already the player's Spouse or Lover, flirting always succeeds warmly
            var romanceType = RomanceTracker.Instance.GetRelationType(npc.ID);
            if (romanceType == RomanceRelationType.Spouse || romanceType == RomanceRelationType.Lover)
            {
                state.LastFlirtWasPositive = true;
                if (!flirtSuccessRecorded) { state.FlirtSuccessCount++; flirtSuccessRecorded = true; }
                // Note: flirtCountThisSession is incremented by the caller (ProcessConversationChoice)

                terminal!.SetColor("bright_magenta");

                if (flirtCountThisSession == 1)
                    terminal.WriteLine($"  {Loc.Get("dialogue.narr_catch_eye_grin", npc.Name2)}");
                else
                    terminal.WriteLine($"  {Loc.Get("dialogue.narr_lean_closer", npc.Name2)}");
                terminal.WriteLine("");
                await Task.Delay(500);

                terminal.SetColor("yellow");
                int loverIdx = random.Next(5) + 1;
                string loverLine = Loc.Get($"dialogue.vn.flirt.lover_response_{loverIdx}");
                terminal.WriteLine($"  {loverLine}");

                RelationshipSystem.UpdateRelationship(player!, npc, 1, 1, false, true);
                terminal.WriteLine("");
                await terminal.PressAnyKey();
                return;
            }

            // Check NPC's relationship status
            // Use marriage registry as the authoritative source (matches gossip screen)
            // NPC flags (Married/IsMarried) can be stale from dead spouses or registry desyncs
            bool npcIsMarried = NPCMarriageRegistry.Instance?.IsMarriedToNPC(npc.ID) == true;
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
                playerStatusWarning = Loc.Get("dialogue.warn_wedding_ring");
            }
            else if (playerHasLover)
            {
                playerStatusPenalty = 0.10f; // -10% for players in relationships
                playerStatusWarning = Loc.Get("dialogue.warn_involved");
            }
            flirtReceptiveness -= playerStatusPenalty;

            // Apply charisma modifier - high charisma makes flirting more effective
            float charismaModifier = GetCharismaModifier();
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
                terminal.WriteLine($"  {Loc.Get("dialogue.narr_catch_eye", npc.Name2)}");
                terminal.WriteLine("");
                await Task.Delay(500);
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {Loc.Get("dialogue.narr_stops_you", npc.Name2)}");
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
                terminal.WriteLine($"  {Loc.Get("dialogue.narr_catch_eye", npc.Name2)}");
                terminal.WriteLine("");
                await Task.Delay(500);
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {Loc.Get("dialogue.narr_shakes_head", npc.Name2)}");
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

                terminal.WriteLine($"  {Loc.Get("dialogue.narr_catch_eye_married", npc.Name2)}");
                terminal.WriteLine("");
                await Task.Delay(500);

                if (affairResult.Success)
                {
                    state.LastFlirtWasPositive = true;
                    if (!flirtSuccessRecorded) { state.FlirtSuccessCount++; flirtSuccessRecorded = true; }
                    flirtCountThisSession++;

                    // Show milestone-specific dialogue
                    switch (affairResult.Milestone)
                    {
                        case AffairMilestone.BecameLovers:
                            terminal.SetColor("bright_red");
                            terminal.WriteLine(GameConfig.ScreenReaderMode ? $"  {Loc.Get("dialogue.affair_forbidden")}" : $"  ♥ {Loc.Get("dialogue.affair_forbidden")} ♥");
                            terminal.SetColor("yellow");
                            terminal.WriteLine($"  {affairResult.Message}");
                            terminal.WriteLine("");
                            terminal.SetColor("gray");
                            terminal.WriteLine($"  {Loc.Get("dialogue.affair_now", npc.Name2)}");
                            break;

                        case AffairMilestone.SecretRendezvous:
                            terminal.SetColor("red");
                            terminal.WriteLine($"  {Loc.Get("dialogue.affair_tension")}");
                            terminal.SetColor("yellow");
                            terminal.WriteLine($"  {affairResult.Message}");
                            break;

                        case AffairMilestone.EmotionalConnection:
                            terminal.SetColor("magenta");
                            terminal.WriteLine($"  {Loc.Get("dialogue.affair_spark")}");
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
                        if (!GameConfig.ScreenReaderMode)
                            terminal.WriteLine($"  {Loc.Get("dialogue.narr_decision_made_visual")}");
                        else
                            terminal.WriteLine($"  {Loc.Get("dialogue.narr_decision_made")}");
                        terminal.SetColor("yellow");
                        terminal.WriteLine($"  {divorceCheck.Reason}");
                        terminal.WriteLine("");

                        // Offer player a choice - become spouse or just lovers
                        terminal.SetColor("cyan");
                        terminal.WriteLine($"  {Loc.Get("dialogue.affair_marry")}");
                        terminal.WriteLine($"  {Loc.Get("dialogue.affair_lovers")}");
                        terminal.WriteLine($"  {Loc.Get("dialogue.affair_reject")}");
                        terminal.WriteLine("");
                        terminal.Write($"  {Loc.Get("ui.your_choice")}: ");
                        string? affairChoice = await terminal.GetInput("");

                        if (affairChoice?.ToUpper() == "M" || affairChoice?.ToUpper() == "L")
                        {
                            bool marry = affairChoice.ToUpper() == "M";
                            EnhancedNPCBehaviors.ProcessAffairDivorce(npc, player!, marry);

                            if (marry)
                            {
                                terminal.SetColor("bright_red");
                                terminal.WriteLine(GameConfig.ScreenReaderMode ? $"  {Loc.Get("dialogue.affair_will_be_spouse", npc.Name2)}" : $"  ♥ {Loc.Get("dialogue.affair_will_be_spouse", npc.Name2)} ♥");
                                // The actual marriage would be handled by the regular marriage system
                            }
                            else
                            {
                                bool loverAdded = RomanceTracker.Instance.AddLover(npc.ID, 50, false);
                                if (loverAdded)
                                {
                                    terminal.SetColor("red");
                                    terminal.WriteLine(GameConfig.ScreenReaderMode ? $"  {Loc.Get("dialogue.affair_now_lover", npc.Name2)}" : $"  ♥ {Loc.Get("dialogue.affair_now_lover", npc.Name2)} ♥");
                                }
                                else
                                {
                                    terminal.SetColor("gray");
                                    terminal.WriteLine($"  {Loc.Get("dialogue.lover_cap_reached", npc.Name2)}");
                                }
                            }
                        }
                        else
                        {
                            terminal.SetColor("gray");
                            terminal.WriteLine($"  {Loc.Get("dialogue.affair_heartbroken", npc.Name2)}");
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
                        terminal.WriteLine($"  {Loc.Get("dialogue.affair_spouse_noticed")}");
                    }
                }

                terminal.WriteLine("");
                await terminal.PressAnyKey();
                return;
            }

            // Vary the player's action based on the flirt
            if (flirtCountThisSession == 0)
                terminal.WriteLine($"  {Loc.Get("dialogue.narr_catch_eye_hold", npc.Name2)}");
            else if (flirtCountThisSession == 1)
                terminal.WriteLine($"  {Loc.Get("dialogue.narr_move_closer", npc.Name2)}");
            else
                terminal.WriteLine($"  {Loc.Get("dialogue.narr_make_interest_clear")}");

            terminal.WriteLine("");
            await Task.Delay(500);

            float roll = (float)random.NextDouble();

            if (roll < flirtReceptiveness)
            {
                // Positive response
                state.LastFlirtWasPositive = true;
                if (!flirtSuccessRecorded) { state.FlirtSuccessCount++; flirtSuccessRecorded = true; }

                terminal.SetColor("yellow");
                terminal.WriteLine($"  {Loc.Get("dialogue.narr_responds_warmly", npc.Name2)}");
                terminal.SetColor("bright_magenta");

                // Varied positive responses
                if (flirtReceptiveness > 0.5f && state.FlirtSuccessCount >= 2)
                {
                    int idx = random.Next(3) + 1;
                    string line = Loc.Get($"dialogue.vn.flirt.strong_pos_{idx}");
                    terminal.WriteLine($"  {line}");
                    // Strong flirt success - allow breaking through friendship cap
                    RelationshipSystem.UpdateRelationship(player!, npc, 1, 3, false, true);
                }
                else if (state.FlirtSuccessCount == 1)
                {
                    int idx = random.Next(3) + 1;
                    string line = Loc.Get($"dialogue.vn.flirt.med_pos_{idx}");
                    terminal.WriteLine($"  {line}");
                    // Successful flirt - allow progression with override
                    RelationshipSystem.UpdateRelationship(player!, npc, 1, 2, false, true);
                }
                else
                {
                    terminal.WriteLine($"  {Loc.Get("dialogue.vn.flirt.mild_pos")}");
                    // First successful flirt - modest boost with override
                    RelationshipSystem.UpdateRelationship(player!, npc, 1, 1, false, true);
                }
            }
            else if (roll < flirtReceptiveness + 0.25f)
            {
                // Neutral response (narrower range now)
                state.LastFlirtWasPositive = false;

                terminal.SetColor("yellow");
                terminal.WriteLine($"  {Loc.Get("dialogue.narr_unsure", npc.Name2)}");
                terminal.SetColor("gray");

                int idx = random.Next(3) + 1;
                string line = Loc.Get($"dialogue.vn.flirt.neutral_{idx}");
                terminal.WriteLine($"  {line}");
            }
            else
            {
                // Negative response (more likely now)
                state.LastFlirtWasPositive = false;

                terminal.SetColor("yellow");
                terminal.WriteLine($"  {Loc.Get("dialogue.narr_unimpressed", npc.Name2)}");
                terminal.SetColor("dark_red");

                if (!isAttracted)
                {
                    terminal.WriteLine($"  \"{Loc.Get("dialogue.vn.flirt.neg_not_type")}\"");
                    // (already quoted at call site -- pure speech)
                }
                else if (playerIsMarried)
                {
                    int idx = random.Next(3) + 1;
                    string line = Loc.Get($"dialogue.vn.flirt.neg_married_{idx}");
                    // Wrap speech-only lines (those without asterisk action) in quotes for VN format
                    terminal.WriteLine(line.StartsWith("*") ? $"  {line}" : $"  \"{line}\"");
                    RelationshipSystem.UpdateRelationship(player!, npc, -2);
                }
                else if (flirtCountThisSession > 1)
                {
                    terminal.WriteLine($"  \"{Loc.Get("dialogue.vn.flirt.neg_persistent")}\"");
                    RelationshipSystem.UpdateRelationship(player!, npc, -1);
                }
                else if (relationLevel > 60)
                {
                    int idx = random.Next(3) + 1;
                    string line = Loc.Get($"dialogue.vn.flirt.neg_too_soon_{idx}");
                    terminal.WriteLine(line.StartsWith("*") ? $"  {line}" : $"  \"{line}\"");
                }
                else
                {
                    int idx = random.Next(3) + 1;
                    string line = Loc.Get($"dialogue.vn.flirt.neg_general_{idx}");
                    terminal.WriteLine(line.StartsWith("*") ? $"  {line}" : $"  \"{line}\"");
                    RelationshipSystem.UpdateRelationship(player!, npc, -1);
                }
            }

            terminal.WriteLine("");
            await terminal.GetInput($"  {Loc.Get("ui.press_enter")}");
        }

        private async Task HandleComplimentOption(NPC npc, int relationLevel)
        {
            terminal!.SetColor("bright_green");
            terminal.WriteLine($"  {Loc.Get("dialogue.narr_offer_compliment", npc.Name2)}");
            terminal.WriteLine("");

            await Task.Delay(500);

            // Compliments almost always work positively
            terminal.SetColor("yellow");
            terminal.WriteLine($"  {Loc.Get("dialogue.narr_pleased", npc.Name2)}");
            terminal.SetColor("white");
            terminal.WriteLine($"  \"{Loc.Get("dialogue.compliment_reply")}\"");

            RelationshipSystem.UpdateRelationship(player!, npc, 1);

            terminal.WriteLine("");
            await terminal.GetInput($"  {Loc.Get("ui.press_enter")}");
        }

        private async Task HandleConfessionOption(NPC npc, int relationLevel)
        {
            // v0.63.0 slice 4 (audit m5): dead-NPC guard.
            if (npc.IsDead) return;

            var profile = npc.Brain?.Personality;
            bool isAttracted = profile?.IsAttractedTo(player!.Sex == CharacterSex.Female ? GenderIdentity.Female : GenderIdentity.Male) ?? true;
            bool confessionAccepted = false; // v0.63.0 slice 4 (audit m11): success tracker

            terminal!.SetColor("bright_magenta");
            terminal.WriteLine($"  {Loc.Get("dialogue.narr_confess")}");
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine($"  {Loc.Get("dialogue.narr_confess_speech_1", npc.Name2)}");
            terminal.WriteLine($"  {Loc.Get("dialogue.narr_confess_speech_2")}");
            terminal.WriteLine("");

            await Task.Delay(1000);

            float successChance = isAttracted ? 0.5f + (0.7f - relationLevel / 100f) : 0.1f;
            if (profile != null)
            {
                successChance += profile.Romanticism * 0.2f;
            }

            // Apply charisma modifier - confessions are influenced by charm
            float charismaModifier = GetCharismaModifier();
            successChance += charismaModifier;
            successChance = Math.Clamp(successChance, 0.05f, 0.90f);

            float roll = (float)random.NextDouble();

            if (roll < successChance)
            {
                // They reciprocate!
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {Loc.Get("dialogue.narr_eyes_soften", npc.Name2)}");
                terminal.SetColor("bright_magenta");
                terminal.WriteLine($"  {Loc.Get("dialogue.confession_reciprocate")}");
                terminal.WriteLine("");

                // Becoming lovers is a major relationship boost - use override to break friendship cap
                RelationshipSystem.UpdateRelationship(player!, npc, 1, 8, false, true);
                bool loverAdded = RomanceTracker.Instance.AddLover(npc.ID, 30);

                // v0.63.0 slice 4 (audit m11): track success so the bottom
                // postState write doesn't blow the ConfessionAccepted flag.
                // Confession is accepted in feeling either way; the lover cap
                // only blocks the romance formalization, not the emotional beat.
                confessionAccepted = true;

                if (loverAdded)
                {
                    terminal.SetColor("bright_cyan");
                    terminal.WriteLine($"  {Loc.Get("dialogue.new_romance")}");
                }
                else
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine($"  {Loc.Get("dialogue.lover_cap_reached", npc.Name2)}");
                }
            }
            else if (roll < successChance + 0.3f)
            {
                // They need time
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {Loc.Get("dialogue.narr_looks_surprised", npc.Name2)}");
                terminal.SetColor("white");
                terminal.WriteLine($"  \"{Loc.Get("dialogue.confession_need_time")}\"");

                RelationshipSystem.UpdateRelationship(player!, npc, 1);
            }
            else
            {
                // Rejection
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {Loc.Get("dialogue.narr_looks_uncomfortable", npc.Name2)}");
                terminal.SetColor("gray");
                terminal.WriteLine($"  \"{Loc.Get("dialogue.confession_rejected")}\"");
            }

            terminal.WriteLine("");

            // v0.63.0 slice 4 (audit m11): single canonical state write that
            // preserves ConfessionAccepted if it was set in the success branch.
            // Pre-fix the success-branch write happened FIRST, then the bottom
            // post-state write overwrote ConfessionAccepted to false implicitly
            // (the dict re-read fetched the now-saved success state, but the
            // pattern was fragile to refactor).
            var postState = npcConversationStates.GetValueOrDefault(npc.ID) ?? new ConversationState();
            postState.HasConfessed = true;
            if (confessionAccepted) postState.ConfessionAccepted = true;
            npcConversationStates[npc.ID] = postState;

            await terminal.GetInput($"  {Loc.Get("ui.press_enter")}");
        }

        private async Task HandleIntimateOption(NPC npc, int relationLevel)
        {
            terminal!.SetColor("bright_red");
            terminal.WriteLine($"  {Loc.Get("dialogue.narr_lean_kiss", npc.Name2)}");
            terminal.WriteLine("");

            await Task.Delay(500);

            var romanceType = RomanceTracker.Instance.GetRelationType(npc.ID);

            if (romanceType == RomanceRelationType.Spouse || romanceType == RomanceRelationType.Lover)
            {
                terminal.SetColor("white");
                terminal.WriteLine($"  {Loc.Get("dialogue.narr_tender_kiss", npc.Name2)}");
                terminal.SetColor("gray");
                terminal.WriteLine($"  {Loc.Get("dialogue.kiss_part")}");
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {npc.Name2}: \"{Loc.Get("dialogue.kiss_never_tire")}\"");
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
                    terminal.WriteLine($"  {Loc.Get("dialogue.first_kiss_prose_1", npc.Name2)}");
                    terminal.WriteLine($"  {Loc.Get("dialogue.first_kiss_prose_2")}");
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"  {npc.Name2}: {Loc.Get("dialogue.first_kiss_reaction")}");

                    // First kiss is meaningful - use override to allow progression beyond friendship
                    RelationshipSystem.UpdateRelationship(player!, npc, 1, 4, false, true);
                }
                else
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"  {Loc.Get("dialogue.kiss_pushed_away", npc.Name2)}");
                    terminal.SetColor("white");
                    terminal.WriteLine($"  \"{Loc.Get("dialogue.kiss_slow_down")}\"");
                }
            }

            terminal.WriteLine("");
            await terminal.GetInput($"  {Loc.Get("ui.press_enter")}");
        }

        private async Task HandlePropositionOption(NPC npc, int relationLevel)
        {
            terminal!.SetColor("red");
            terminal.WriteLine($"  {Loc.Get("dialogue.narr_suggestive")}");
            terminal.SetColor("white");
            terminal.WriteLine($"  {Loc.Get("dialogue.narr_proposition_line")}");
            terminal.WriteLine("");

            await Task.Delay(500);

            var romanceType = RomanceTracker.Instance.GetRelationType(npc.ID);

            if (romanceType == RomanceRelationType.Spouse ||
                romanceType == RomanceRelationType.Lover ||
                romanceType == RomanceRelationType.FWB)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {Loc.Get("dialogue.narr_eyes_desire", npc.Name2)}");
                terminal.SetColor("bright_red");
                terminal.WriteLine($"  {Loc.Get("dialogue.prop_accept_takes_hand")}");
                terminal.WriteLine("");

                terminal.SetColor("bright_cyan");
                terminal.WriteLine($"  [{Loc.Get("dialogue.intimate_warning")}]");
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
                    terminal.WriteLine($"  {Loc.Get("dialogue.narr_intimate_moment")}");
                    terminal.SetColor("gray");
                    terminal.WriteLine("");
                    terminal.WriteLine($"  {Loc.Get("dialogue.narr_intimate_aftermath")}");
                    await Task.Delay(1500);
                }
            }
            else
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {Loc.Get("dialogue.narr_looks_surprised_prop", npc.Name2)}");
                terminal.SetColor("gray");
                terminal.WriteLine($"  \"{Loc.Get("dialogue.prop_too_early")}\"");

                // Might hurt relationship if too early
                if (relationLevel > 40)
                {
                    RelationshipSystem.UpdateRelationship(player!, npc, -1);
                }
            }

            terminal.WriteLine("");
            await terminal.GetInput($"  {Loc.Get("ui.press_enter")}");
        }

        private async Task HandleProvocationOption(NPC npc, int relationLevel)
        {
            terminal!.SetColor("dark_red");
            terminal.WriteLine($"  {Loc.Get("dialogue.narr_provoke", npc.Name2)}");
            terminal.WriteLine("");

            await Task.Delay(500);

            var profile = npc.Brain?.Personality;
            float aggression = profile?.Aggression ?? 0.5f;

            if (aggression > 0.6f)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {Loc.Get("dialogue.narr_eyes_anger", npc.Name2)}");
                terminal.SetColor("red");
                terminal.WriteLine($"  \"{Loc.Get("dialogue.provoke_threat")}\"");

                RelationshipSystem.UpdateRelationship(player!, npc, -1, 2);
            }
            else
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {Loc.Get("dialogue.narr_scoffs", npc.Name2)}");
                terminal.SetColor("gray");
                terminal.WriteLine($"  \"{Loc.Get("dialogue.provoke_dismiss")}\"");

                RelationshipSystem.UpdateRelationship(player!, npc, -1);
            }

            terminal.WriteLine("");
            await terminal.GetInput($"  {Loc.Get("ui.press_enter")}");
        }

        private async Task HandleAskToLeaveOption(NPC npc, int relationLevel)
        {
            terminal!.SetColor("bright_yellow");
            terminal.WriteLine($"  {Loc.Get("dialogue.narr_take_hand", npc.Name2)}");
            terminal.WriteLine("");
            await Task.Delay(800);

            terminal.SetColor("white");
            terminal.WriteLine($"  {Loc.Get("dialogue.narr_tired_sneaking", npc.SpouseName)}");
            terminal.WriteLine("");
            await Task.Delay(600);

            var affair = NPCMarriageRegistry.Instance.GetAffair(npc.ID, player!.ID);
            var profile = npc.Brain?.Personality;
            if (affair == null || profile == null)
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  {Loc.Get("dialogue.narr_stares_blank", npc.Name2)}");
                await terminal.GetInput($"  {Loc.Get("ui.press_enter")}");
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
                if (!GameConfig.ScreenReaderMode)
                    terminal.WriteLine($"  {Loc.Get("dialogue.narr_decision_made_visual")}");
                else
                    terminal.WriteLine($"  {Loc.Get("dialogue.narr_decision_made")}");
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
                terminal.WriteLine($"  {Loc.Get("dialogue.affair_marry")}");
                terminal.WriteLine($"  {Loc.Get("dialogue.affair_lovers")}");
                terminal.WriteLine($"  {Loc.Get("dialogue.affair_changed_mind")}");
                terminal.WriteLine("");
                terminal.Write($"  {Loc.Get("ui.your_choice")}: ");
                string? choice = await terminal.GetInput("");

                if (choice?.Trim().ToUpper() == "M" || choice?.Trim().ToUpper() == "L")
                {
                    bool marry = choice.Trim().ToUpper() == "M";
                    string exSpouseName = npc.SpouseName ?? "their spouse";
                    EnhancedNPCBehaviors.ProcessAffairDivorce(npc, player!, marry);

                    if (marry)
                    {
                        terminal.SetColor("bright_red");
                        terminal.WriteLine($"  {Loc.Get("dialogue.affair_leaves_spouse", npc.Name2, exSpouseName)}");
                        NewsSystem.Instance?.Newsy(true, $"{npc.Name2} has left {exSpouseName} for {player.Name}!");
                    }
                    else
                    {
                        bool loverAdded = RomanceTracker.Instance.AddLover(npc.ID, 50, false);
                        if (loverAdded)
                        {
                            terminal.SetColor("red");
                            terminal.WriteLine($"  {Loc.Get("dialogue.affair_now_lover", npc.Name2)}");
                            NewsSystem.Instance?.Newsy(true, $"{npc.Name2} has left {exSpouseName} in a scandal involving {player.Name}!");
                        }
                        else
                        {
                            terminal.SetColor("gray");
                            terminal.WriteLine($"  {Loc.Get("dialogue.lover_cap_reached", npc.Name2)}");
                            // Affair-divorce already fired; NPC is now single, not the player's lover.
                            NewsSystem.Instance?.Newsy(true, $"{npc.Name2} has left {exSpouseName} after a scandal, but did not stay with {player.Name}.");
                        }
                    }
                }
                else
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine($"  {Loc.Get("dialogue.affair_confused", npc.Name2)}");
                    terminal.WriteLine($"  \"{Loc.Get("dialogue.spouse_back_out")}\"");
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
                    terminal.WriteLine($"  \"{Loc.Get("dialogue.affair_vows", npc.SpouseName)}\"");
                }
                else if (affair.AffairProgress < 120)
                {
                    terminal.WriteLine($"  {npc.Name2} hesitates, conflicted.");
                    terminal.SetColor("white");
                    terminal.WriteLine($"  \"{Loc.Get("dialogue.affair_not_ready")}\"");
                }
                else
                {
                    terminal.WriteLine($"  {npc.Name2} looks away, torn.");
                    terminal.SetColor("white");
                    terminal.WriteLine($"  \"{Loc.Get("dialogue.affair_afraid")}\"");
                }

                // Asking still pushes the affair forward a bit
                affair.AffairProgress = Math.Min(200, affair.AffairProgress + 10);
                RelationshipSystem.UpdateRelationship(player!, npc, 1);
            }

            terminal.WriteLine("");
            await terminal.GetInput($"  {Loc.Get("ui.press_enter")}");
        }

        private async Task HandleMarriageProposal(NPC npc, int relationLevel)
        {
            terminal!.ClearScreen();

            UIHelper.WriteBoxHeader(terminal, Loc.Get("dialogue.marriage_proposal"), "bright_red");
            terminal.WriteLine("");

            // v0.63.0 slice 1: blood-tie refusal at the top of the proposal flow.
            // Player shouldn't even reach the speech / acceptance roll for a
            // family member; refuse with flavor and bail.
            var fam = UsurperRemake.Systems.FamilySystem.Instance;
            if (fam != null && player != null)
            {
                var rel = fam.GetFamilyRelation(player, npc);
                if (UsurperRemake.Systems.FamilySystem.IsBlockingRelation(rel))
                {
                    terminal.SetColor("red");
                    terminal.WriteLine($"  {GetVNIncestRefusal(rel, npc.DisplayName ?? npc.Name2 ?? npc.Name)}");
                    await terminal.GetInput($"  {Loc.Get("ui.press_enter")}");
                    return;
                }
            }

            // Check player age
            if (player!.Age < GameConfig.MinimumAgeToMarry)
            {
                terminal.SetColor("red");
                terminal.WriteLine($"  {Loc.Get("dialogue.age_requirement", $"{GameConfig.MinimumAgeToMarry}")}");
                await terminal.GetInput($"  {Loc.Get("ui.press_enter")}");
                return;
            }

            // Check if player already married (and not poly)
            if (RomanceTracker.Instance.IsMarried)
            {
                var existingSpouse = RomanceTracker.Instance.PrimarySpouse;
                if (existingSpouse != null && !existingSpouse.AcceptsPolyamory)
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"  {Loc.Get("dialogue.already_married")}");
                    await terminal.GetInput($"  {Loc.Get("ui.press_enter")}");
                    return;
                }
            }

            // Check gold
            long weddingCost = GameConfig.WeddingCostBase;
            if (player.Gold < weddingCost)
            {
                terminal.SetColor("red");
                terminal.WriteLine($"  {Loc.Get("dialogue.wedding_cost", $"{weddingCost}", $"{player.Gold}")}");
                await terminal.GetInput($"  {Loc.Get("ui.press_enter")}");
                return;
            }

            terminal.SetColor("white");
            terminal.WriteLine($"  {Loc.Get("dialogue.narr_take_hands", npc.Name2)}");
            terminal.WriteLine("");
            await Task.Delay(1000);

            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"  {Loc.Get("dialogue.narr_proposal_speech_1")}");
            terminal.WriteLine($"  {Loc.Get("dialogue.narr_proposal_speech_2")}");
            terminal.WriteLine($"  {Loc.Get("dialogue.narr_proposal_speech_3", npc.Name2)}");
            terminal.WriteLine("");

            await Task.Delay(1500);

            // Calculate acceptance chance based on relationship and personality.
            // This still computes -- it's the deterministic fallback the LLM
            // fork below routes to when the LLM is unavailable / times out /
            // returns an unparseable response.
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

            // v0.64.0 Brain v2 Slice 12b: LLM-arbitrated dramatic fork.
            // When LLM is active and online, the NPC makes a character-driven
            // decision (accept / hesitate / refuse) grounded in their full
            // personality + relationship history rather than a pure random
            // roll against the computed acceptChance. The roll above is still
            // used as the deterministic fallback for LLM-disabled / single-
            // player / BBS / timeout / parse-error cases.
            //
            // Forks block on the LLM response (~2-3s typical) because the
            // decision is needed NOW; the player is on a UI screen waiting
            // for the NPC's answer. Acceptable latency for a one-shot
            // narrative moment of this weight.
            int heuristicChoice = roll < acceptChance ? 0
                                : roll < acceptChance + 0.25f ? 1
                                : 2;
            string proposalSituation =
                $"{player.Name} has just proposed marriage. " +
                $"Your relationship sits at level {relationLevel} (lower numbers mean deeper love). " +
                $"Your Commitment trait is {commitment:F2}, Romanticism is {romanticism:F2}, " +
                $"and the proposal landed with charisma modifier {charismaModifier:+0.00;-0.00;0.00}. " +
                $"The heuristic acceptance chance was {acceptChance:P0}. " +
                $"Decide how YOU, this specific NPC, respond.";
            var proposalChoices = new System.Collections.Generic.List<string>
            {
                "Say YES -- accept the proposal and marry them.",
                "Hesitate -- say you're not ready yet, but stay open to the future.",
                "Refuse -- pull away and decline the proposal.",
            };
            int proposalChoice = await UsurperRemake.Systems.LLMMoments.DecideForkAsync(
                npc, "marriage_proposal", proposalSituation, proposalChoices,
                deterministicFallback: heuristicChoice, System.Threading.CancellationToken.None);

            if (proposalChoice == 0)
            {
                // They say yes!
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {Loc.Get("dialogue.narr_tears_joy", npc.Name2)}");
                terminal.WriteLine("");
                await Task.Delay(1000);

                terminal.SetColor("bright_magenta");
                terminal.WriteLine($"  {Loc.Get("dialogue.propose_arms_around", npc.Name2)}");
                terminal.SetColor("white");
                terminal.WriteLine($"  \"{Loc.Get("dialogue.propose_yes")}\"");
                terminal.WriteLine("");

                await Task.Delay(1500);

                // Wedding ceremony
                await PerformWeddingCeremony(npc);
            }
            else if (proposalChoice == 1)
            {
                // Not ready yet
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {Loc.Get("dialogue.narr_torn", npc.Name2)}");
                terminal.WriteLine("");
                await Task.Delay(1000);

                terminal.SetColor("white");
                terminal.WriteLine($"  \"{Loc.Get("dialogue.propose_maybe_1")}");
                terminal.WriteLine($"   {Loc.Get("dialogue.propose_maybe_2")}\"");

                // Small relationship boost for the meaningful moment
                RelationshipSystem.UpdateRelationship(player, npc, 1);
            }
            else
            {
                // Rejection
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {Loc.Get("dialogue.narr_pulls_away", npc.Name2)}");
                terminal.WriteLine("");
                await Task.Delay(1000);

                terminal.SetColor("gray");
                terminal.WriteLine($"  \"{Loc.Get("dialogue.propose_no_1")}");
                terminal.WriteLine($"   {Loc.Get("dialogue.propose_no_2")}\"");

                // Relationship takes a small hit
                RelationshipSystem.UpdateRelationship(player, npc, -1);
            }

            terminal.WriteLine("");
            await terminal.GetInput($"  {Loc.Get("ui.press_enter")}");
        }

        private async Task PerformWeddingCeremony(NPC npc)
        {
            terminal!.ClearScreen();

            UIHelper.WriteBoxHeader(terminal, Loc.Get("dialogue.wedding_ceremony"), "bright_yellow");
            terminal.WriteLine("");

            await Task.Delay(500);

            // Pay for wedding
            player!.Gold -= GameConfig.WeddingCostBase;

            terminal.SetColor("white");
            terminal.WriteLine($"  {Loc.Get("dialogue.narr_rush_temple")}");
            terminal.WriteLine("");
            await Task.Delay(1000);

            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"  {Loc.Get("dialogue.narr_priest_rites")}");
            terminal.WriteLine("");
            await Task.Delay(500);

            terminal.SetColor("yellow");
            terminal.WriteLine($"  {Loc.Get("dialogue.narr_dearly_beloved_1")}");
            terminal.WriteLine($"  {Loc.Get("dialogue.narr_dearly_beloved_2", player.Name, npc.Name2)}");
            terminal.WriteLine("");
            await Task.Delay(1500);

            terminal.SetColor("white");
            terminal.WriteLine($"  {Loc.Get("dialogue.narr_vow_player", player.Name, npc.Name2)}");
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("dialogue.narr_i_do")}");
            terminal.WriteLine("");
            await Task.Delay(1000);

            terminal.SetColor("white");
            terminal.WriteLine($"  {Loc.Get("dialogue.narr_vow_npc", npc.Name2, player.Name)}");
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("dialogue.narr_npc_i_do", npc.Name2)}");
            terminal.WriteLine("");
            await Task.Delay(1000);

            // Random ceremony message
            var ceremonyMessages = GameConfig.GetWeddingCeremonyMessages();
            string ceremonyMessage = ceremonyMessages[random.Next(ceremonyMessages.Length)];

            terminal.SetColor("yellow");
            terminal.WriteLine($"  {ceremonyMessage}");
            terminal.WriteLine("");
            await Task.Delay(1000);

            terminal.SetColor("bright_magenta");
            if (!GameConfig.ScreenReaderMode)
                terminal.WriteLine($"  {Loc.Get("dialogue.narr_kiss_spouse")}");
            else
                terminal.WriteLine($"  {Loc.Get("dialogue.narr_kiss_spouse_sr")}");
            terminal.WriteLine("");
            await Task.Delay(1500);

            // Update relationship to married
            RelationshipSystem.UpdateRelationship(player, npc, 1, 10, true, true);

            // Update player married status
            player.IsMarried = true;
            player.Married = true;
            player.SpouseName = npc.Name2;
            player.MarriedTimes++;

            // Update NPC married status
            npc.IsMarried = true;
            npc.Married = true;
            npc.SpouseName = player.DisplayName;
            npc.MarriedTimes++;

            // Add to RomanceTracker as spouse
            RomanceTracker.Instance.AddSpouse(npc.ID, false);

            // Register in NPCMarriageRegistry (authoritative source for gossip/flirt)
            NPCMarriageRegistry.Instance?.RegisterMarriage(
                player.ID ?? "", npc.ID, player.DisplayName, npc.Name2);

            // Generate news
            NewsSystem.Instance?.Newsy(true, $"{player.Name} and {npc.Name2} have gotten married! Congratulations to the happy couple!");

            terminal.SetColor("bright_green");
            if (!GameConfig.ScreenReaderMode)
                terminal.WriteLine("══════════════════════════════════════════════════════════════════════════════");
            terminal.WriteLine($"  {Loc.Get("dialogue.now_married", npc.Name2.ToUpper())}");
            if (!GameConfig.ScreenReaderMode)
                terminal.WriteLine("══════════════════════════════════════════════════════════════════════════════");
            terminal.WriteLine("");

            // Benefits announcement
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"  {Loc.Get("dialogue.marriage_benefits")}");
            terminal.SetColor("white");
            terminal.WriteLine($"  - {Loc.Get("dialogue.benefit_xp")}");
            terminal.WriteLine($"  - {Loc.Get("dialogue.benefit_combat_ally")}");
            terminal.WriteLine($"  - {Loc.Get("dialogue.benefit_home_children")}");
            terminal.WriteLine($"  - {Loc.Get("dialogue.benefit_intimate")}");
            terminal.WriteLine("");

            // Same-sex marriage note
            if (player.Sex == npc.Sex)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"  ({Loc.Get("dialogue.adopt_children")})");
            }
            else
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"  ({Loc.Get("dialogue.try_for_children")})");
            }

            terminal.WriteLine("");
            await terminal.GetInput($"  {Loc.Get("dialogue.press_enter_new_life")}");
        }

        /// <summary>
        /// Show farewell based on relationship
        /// </summary>
        private async Task ShowFarewell(NPC npc, int relationLevel, RomanceRelationType romanceType)
        {
            terminal!.WriteLine("");
            terminal.SetColor("yellow");

            // Spoken line only (no speaker prefix) so the enhancer can wrap it.
            string spoken = romanceType switch
            {
                RomanceRelationType.Spouse => Loc.Get("dialogue.vn.farewell.spouse"),
                RomanceRelationType.Lover  => Loc.Get("dialogue.vn.farewell.lover"),
                RomanceRelationType.FWB    => Loc.Get("dialogue.vn.farewell.fwb"),
                _ => relationLevel switch
                {
                    <= 30 => Loc.Get("dialogue.vn.farewell.close"),
                    <= 50 => Loc.Get("dialogue.vn.farewell.friend"),
                    <= 70 => Loc.Get("dialogue.vn.farewell.neutral"),
                    _     => Loc.Get("dialogue.vn.farewell.hostile")
                }
            };

            // Phase 1.5 enhancer: layer contextual flavor on the farewell.
            if (player != null)
                spoken = UsurperRemake.Systems.DialogueEnhancer.Enhance(spoken, npc, player);

            // FWB keeps the *winks* action prefix.
            string prefix = romanceType == RomanceRelationType.FWB ? "*winks* " : "";
            terminal.WriteLine($"  {npc.Name2}: {prefix}\"{spoken}\"");
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

        /// <summary>
        /// v0.63.0 slice 1: localized refusal for an incest proposal in VN.
        /// Shares the family.incest_refuse_* loc namespace with the
        /// Church / Castle / RelationshipSystem refusal sites.
        /// </summary>
        private static string GetVNIncestRefusal(UsurperRemake.Systems.FamilySystem.FamilyRelation rel, string targetName)
        {
            return rel switch
            {
                UsurperRemake.Systems.FamilySystem.FamilyRelation.Self =>
                    Loc.Get("family.incest_refuse_self"),
                UsurperRemake.Systems.FamilySystem.FamilyRelation.Parent =>
                    Loc.Get("family.incest_refuse_parent", targetName),
                UsurperRemake.Systems.FamilySystem.FamilyRelation.Child =>
                    Loc.Get("family.incest_refuse_child", targetName),
                UsurperRemake.Systems.FamilySystem.FamilyRelation.Sibling =>
                    Loc.Get("family.incest_refuse_sibling", targetName),
                UsurperRemake.Systems.FamilySystem.FamilyRelation.HalfSibling =>
                    Loc.Get("family.incest_refuse_half_sibling", targetName),
                UsurperRemake.Systems.FamilySystem.FamilyRelation.Grandparent =>
                    Loc.Get("family.incest_refuse_grandparent", targetName),
                UsurperRemake.Systems.FamilySystem.FamilyRelation.Grandchild =>
                    Loc.Get("family.incest_refuse_grandchild", targetName),
                UsurperRemake.Systems.FamilySystem.FamilyRelation.AdoptiveParent =>
                    Loc.Get("family.incest_refuse_adoptive_parent", targetName),
                UsurperRemake.Systems.FamilySystem.FamilyRelation.AdoptiveChild =>
                    Loc.Get("family.incest_refuse_adoptive_child", targetName),
                UsurperRemake.Systems.FamilySystem.FamilyRelation.AdoptiveSibling =>
                    Loc.Get("family.incest_refuse_adoptive_sibling", targetName),
                _ => Loc.Get("family.incest_refuse_generic", targetName),
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
