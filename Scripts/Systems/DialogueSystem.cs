using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using UsurperRemake.Utils;
using UsurperRemake.Systems;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Branching Dialogue System - Handles narrative conversations with choices
    /// Supports conditional branches, alignment impacts, and story flag triggers
    /// </summary>
    public class DialogueSystem
    {
        private static DialogueSystem? instance;
        public static DialogueSystem Instance => instance ??= new DialogueSystem();

        private TerminalEmulator? terminal;
        private Character? currentPlayer;
        private DialogueNode? currentNode;
        private Dictionary<string, DialogueTree> dialogueTrees = new();

        // Track dialogue history for this session
        private List<string> dialogueHistory = new();

        public event Action<string, DialogueChoice>? OnChoiceMade;
        public event Action<string>? OnDialogueComplete;

        public DialogueSystem()
        {
            RegisterAllDialogueTrees();
        }

        /// <summary>
        /// Start a dialogue tree with the player
        /// </summary>
        public async Task<DialogueResult> StartDialogue(Character player, string treeId, TerminalEmulator term)
        {
            terminal = term;
            currentPlayer = player;

            if (!dialogueTrees.TryGetValue(treeId, out var tree))
            {
                GD.PrintErr($"[Dialogue] Tree not found: {treeId}");
                return new DialogueResult { Completed = false, EndNode = null };
            }

            // GD.Print($"[Dialogue] Starting dialogue tree: {treeId}");

            currentNode = tree.RootNode;
            var result = await ProcessDialogueTree(tree);

            OnDialogueComplete?.Invoke(treeId);
            dialogueHistory.Add(treeId);

            return result;
        }

        /// <summary>
        /// Process the dialogue tree until completion
        /// </summary>
        private async Task<DialogueResult> ProcessDialogueTree(DialogueTree tree)
        {
            while (currentNode != null)
            {
                // Display the node's text
                await DisplayDialogueNode(currentNode);

                // If this is an end node, we're done
                if (currentNode.IsEndNode)
                {
                    ApplyNodeEffects(currentNode);
                    return new DialogueResult { Completed = true, EndNode = currentNode };
                }

                // Get available choices (filter by conditions)
                var availableChoices = GetAvailableChoices(currentNode);

                if (availableChoices.Count == 0)
                {
                    // No choices available, auto-proceed to next node
                    if (!string.IsNullOrEmpty(currentNode.NextNodeId))
                    {
                        currentNode = FindNode(currentNode.NextNodeId);
                        await Task.Delay(1500);
                    }
                    else
                    {
                        break;
                    }
                }
                else if (availableChoices.Count == 1 && availableChoices[0].IsAutoSelect)
                {
                    // Single auto-select choice, proceed automatically
                    ApplyChoiceEffects(availableChoices[0]);
                    currentNode = FindNode(availableChoices[0].NextNodeId);
                    await Task.Delay(1000);
                }
                else
                {
                    // Present choices to the player
                    var selectedChoice = await PresentChoices(availableChoices);
                    if (selectedChoice == null)
                    {
                        // Player aborted dialogue
                        return new DialogueResult { Completed = false, EndNode = null };
                    }

                    ApplyChoiceEffects(selectedChoice);
                    OnChoiceMade?.Invoke(currentNode.Id, selectedChoice);
                    currentNode = FindNode(selectedChoice.NextNodeId);
                }
            }

            return new DialogueResult { Completed = true, EndNode = currentNode };
        }

        /// <summary>
        /// Display a dialogue node with typewriter effect
        /// </summary>
        private async Task DisplayDialogueNode(DialogueNode node)
        {
            if (terminal == null) return;

            terminal.WriteLine("");

            // Show speaker name if present
            if (!string.IsNullOrEmpty(node.Speaker))
            {
                var speakerColor = GetSpeakerColor(node.Speaker);
                terminal.WriteLine($"[{node.Speaker}]", speakerColor);
            }

            // Display each line of dialogue with slight delay
            foreach (var line in node.Text)
            {
                var processedLine = ProcessDialogueVariables(line);
                terminal.WriteLine(processedLine, node.TextColor ?? "white");
                await Task.Delay(50 * Math.Min(processedLine.Length, 80)); // Typing effect delay
            }

            terminal.WriteLine("");
        }

        /// <summary>
        /// Present choices to the player and get selection
        /// </summary>
        private async Task<DialogueChoice?> PresentChoices(List<DialogueChoice> choices)
        {
            if (terminal == null) return null;

            terminal.WriteLine("What do you say?", "cyan");
            terminal.WriteLine("");

            for (int i = 0; i < choices.Count; i++)
            {
                var choice = choices[i];
                var prefix = $"[{i + 1}]";
                var color = GetChoiceColor(choice);
                terminal.WriteLine($"{prefix} {choice.Text}", color);
            }

            terminal.WriteLine("[0] (Say nothing)", "dark_gray");
            terminal.WriteLine("");

            while (true)
            {
                var input = await terminal.GetInputAsync("Your choice: ");

                if (input == "0")
                {
                    return null;
                }

                if (int.TryParse(input, out int choice) && choice >= 1 && choice <= choices.Count)
                {
                    return choices[choice - 1];
                }

                terminal.WriteLine("Please enter a valid choice.", "red");
            }
        }

        /// <summary>
        /// Get available choices based on conditions
        /// </summary>
        private List<DialogueChoice> GetAvailableChoices(DialogueNode node)
        {
            return node.Choices.Where(c => EvaluateCondition(c.Condition)).ToList();
        }

        /// <summary>
        /// Evaluate a dialogue condition
        /// </summary>
        private bool EvaluateCondition(DialogueCondition? condition)
        {
            if (condition == null) return true;
            if (currentPlayer == null) return true;

            var story = StoryProgressionSystem.Instance;

            switch (condition.Type)
            {
                case ConditionType.HasStoryFlag:
                    return story.HasStoryFlag(condition.StringValue ?? "");

                case ConditionType.NotHasStoryFlag:
                    return !story.HasStoryFlag(condition.StringValue ?? "");

                case ConditionType.AlignmentAbove:
                    var netAlignment = currentPlayer.Chivalry - currentPlayer.Darkness;
                    return netAlignment > condition.IntValue;

                case ConditionType.AlignmentBelow:
                    var netAlign2 = currentPlayer.Chivalry - currentPlayer.Darkness;
                    return netAlign2 < condition.IntValue;

                case ConditionType.LevelAbove:
                    return currentPlayer.Level > condition.IntValue;

                case ConditionType.LevelBelow:
                    return currentPlayer.Level < condition.IntValue;

                case ConditionType.HasClass:
                    return currentPlayer.Class.ToString().Equals(condition.StringValue, StringComparison.OrdinalIgnoreCase);

                case ConditionType.HasRace:
                    return currentPlayer.Race.ToString().Equals(condition.StringValue, StringComparison.OrdinalIgnoreCase);

                case ConditionType.GoldAbove:
                    return currentPlayer.Gold > condition.IntValue;

                case ConditionType.HasArtifact:
                    if (Enum.TryParse<ArtifactType>(condition.StringValue, out var artifact))
                    {
                        return story.CollectedArtifacts.Contains(artifact);
                    }
                    return false;

                case ConditionType.ChapterAtLeast:
                    if (Enum.TryParse<StoryChapter>(condition.StringValue, out var chapter))
                    {
                        return story.CurrentChapter >= chapter;
                    }
                    return false;

                case ConditionType.CycleAbove:
                    return story.CurrentCycle > condition.IntValue;

                case ConditionType.HasMadeChoice:
                    return story.HasMadeChoice(condition.StringValue ?? "");

                // Companion system conditions
                case ConditionType.HasCompanion:
                    if (Enum.TryParse<CompanionId>(condition.StringValue, out var compId))
                        return CompanionSystem.Instance.IsCompanionRecruited(compId);
                    return false;

                case ConditionType.CompanionAlive:
                    if (Enum.TryParse<CompanionId>(condition.StringValue, out var aliveCompId))
                        return CompanionSystem.Instance.IsCompanionAlive(aliveCompId);
                    return false;

                case ConditionType.CompanionDead:
                    if (Enum.TryParse<CompanionId>(condition.StringValue, out var deadCompId))
                        return !CompanionSystem.Instance.IsCompanionAlive(deadCompId) &&
                               CompanionSystem.Instance.IsCompanionRecruited(deadCompId);
                    return false;

                case ConditionType.CompanionLoyaltyAbove:
                    if (Enum.TryParse<CompanionId>(condition.StringValue, out var loyalCompId))
                    {
                        var comp = CompanionSystem.Instance.GetCompanion(loyalCompId);
                        return comp != null && comp.LoyaltyLevel > condition.IntValue;
                    }
                    return false;

                case ConditionType.CompanionTrustAbove:
                    if (Enum.TryParse<CompanionId>(condition.StringValue, out var trustCompId))
                    {
                        var comp = CompanionSystem.Instance.GetCompanion(trustCompId);
                        return comp != null && comp.TrustLevel > condition.IntValue;
                    }
                    return false;

                case ConditionType.RomanceLevelAbove:
                    if (Enum.TryParse<CompanionId>(condition.StringValue, out var romCompId))
                    {
                        var comp = CompanionSystem.Instance.GetCompanion(romCompId);
                        return comp != null && comp.RomanceLevel > condition.IntValue;
                    }
                    return false;

                case ConditionType.HasActiveCompanion:
                    return CompanionSystem.Instance.GetActiveCompanions().Any();

                // Grief system conditions
                case ConditionType.HasGriefStatus:
                    return GriefSystem.Instance.IsGrieving;

                case ConditionType.GriefStageIs:
                    if (Enum.TryParse<GriefStage>(condition.StringValue, out var griefStage))
                        return GriefSystem.Instance.CurrentStage == griefStage;
                    return false;

                case ConditionType.CompletedGriefCycle:
                    return GriefSystem.Instance.HasCompletedGriefCycle;

                // Betrayal conditions
                case ConditionType.BetrayedBy:
                    return BetrayalSystem.Instance.HasBetrayed(condition.StringValue ?? "");

                case ConditionType.ForgaveBetrayer:
                    return story.HasStoryFlag($"forgave_{condition.StringValue}");

                case ConditionType.HasPendingBetrayal:
                    return BetrayalSystem.Instance.HasPendingBetrayal(condition.StringValue ?? "");

                // Ocean Philosophy conditions
                case ConditionType.AwakeningLevelAbove:
                    return OceanPhilosophySystem.Instance.AwakeningLevel > condition.IntValue;

                case ConditionType.HasWaveFragment:
                    if (Enum.TryParse<WaveFragment>(condition.StringValue, out var fragment))
                        return OceanPhilosophySystem.Instance.CollectedFragments.Contains(fragment);
                    return false;

                case ConditionType.HasOceanInsight:
                    return OceanPhilosophySystem.Instance.Insights.Count >= condition.IntValue;

                case ConditionType.ExperiencedMoment:
                    if (Enum.TryParse<AwakeningMoment>(condition.StringValue, out var moment))
                        return OceanPhilosophySystem.Instance.ExperiencedMoments.Contains(moment);
                    return false;

                // Amnesia conditions
                case ConditionType.HasMemoryFragment:
                    if (Enum.TryParse<MemoryFragment>(condition.StringValue, out var memFragment))
                        return AmnesiaSystem.Instance.RecoveredMemories.Contains(memFragment);
                    return false;

                case ConditionType.MemoryRecoveryAbove:
                    return (AmnesiaSystem.Instance.GetRecoveryProgress() * 100) > condition.IntValue;

                case ConditionType.TruthRevealed:
                    return AmnesiaSystem.Instance.TruthRevealed;

                case ConditionType.StrangerReceptivityAbove:
                    return StrangerEncounterSystem.Instance.Receptivity > condition.IntValue;

                case ConditionType.StrangerReceptivityBelow:
                    return StrangerEncounterSystem.Instance.Receptivity < condition.IntValue;

                case ConditionType.StrangerEncountersAbove:
                    return StrangerEncounterSystem.Instance.EncountersHad > condition.IntValue;

                case ConditionType.StrangerKnowsTruth:
                    return StrangerEncounterSystem.Instance.PlayerKnowsTruth;

                default:
                    return true;
            }
        }

        /// <summary>
        /// Apply effects from a dialogue node
        /// </summary>
        private void ApplyNodeEffects(DialogueNode node)
        {
            if (currentPlayer == null) return;

            foreach (var effect in node.Effects)
            {
                ApplyEffect(effect);
            }
        }

        /// <summary>
        /// Apply effects from a dialogue choice
        /// </summary>
        private void ApplyChoiceEffects(DialogueChoice choice)
        {
            if (currentPlayer == null) return;

            foreach (var effect in choice.Effects)
            {
                ApplyEffect(effect);
            }
        }

        /// <summary>
        /// Apply a single dialogue effect
        /// </summary>
        private void ApplyEffect(DialogueEffect effect)
        {
            if (currentPlayer == null) return;
            var story = StoryProgressionSystem.Instance;

            switch (effect.Type)
            {
                case EffectType.SetStoryFlag:
                    story.SetStoryFlag(effect.StringValue ?? "", true);
                    // GD.Print($"[Dialogue] Set story flag: {effect.StringValue}");
                    break;

                case EffectType.ClearStoryFlag:
                    story.SetStoryFlag(effect.StringValue ?? "", false);
                    break;

                case EffectType.AddChivalry:
                    currentPlayer.Chivalry += effect.IntValue;
                    currentPlayer.ChivNr++;
                    terminal?.WriteLine($"(+{effect.IntValue} Chivalry)", "bright_green");
                    break;

                case EffectType.AddDarkness:
                    currentPlayer.Darkness += effect.IntValue;
                    currentPlayer.DarkNr++;
                    terminal?.WriteLine($"(+{effect.IntValue} Darkness)", "dark_red");
                    break;

                case EffectType.AddGold:
                    currentPlayer.Gold += effect.IntValue;
                    if (effect.IntValue > 0)
                        terminal?.WriteLine($"(Received {effect.IntValue} gold)", "yellow");
                    else
                        terminal?.WriteLine($"(Lost {-effect.IntValue} gold)", "red");
                    break;

                case EffectType.AddExperience:
                    currentPlayer.Experience += effect.IntValue;
                    terminal?.WriteLine($"(+{effect.IntValue} Experience)", "cyan");
                    break;

                case EffectType.Heal:
                    currentPlayer.HP = Math.Min(currentPlayer.HP + effect.IntValue, currentPlayer.MaxHP);
                    terminal?.WriteLine($"(Healed {effect.IntValue} HP)", "green");
                    break;

                case EffectType.Damage:
                    currentPlayer.HP = Math.Max(currentPlayer.HP - effect.IntValue, 0);
                    terminal?.WriteLine($"(Took {effect.IntValue} damage)", "red");
                    break;

                case EffectType.GiveItem:
                    // TODO: Add item to inventory
                    terminal?.WriteLine($"(Received: {effect.StringValue})", "bright_yellow");
                    break;

                case EffectType.RecordChoice:
                    story.RecordChoice(effect.StringValue ?? "", effect.StringValue2 ?? "", 0);
                    break;

                case EffectType.AdvanceChapter:
                    if (Enum.TryParse<StoryChapter>(effect.StringValue, out var chapter))
                    {
                        story.AdvanceChapter(chapter);
                    }
                    break;

                case EffectType.UnlockArtifact:
                    if (Enum.TryParse<ArtifactType>(effect.StringValue, out var artifact))
                    {
                        story.CollectArtifact(artifact);
                    }
                    break;

                case EffectType.TriggerEvent:
                    story.TriggerEvent(effect.StringValue ?? "", effect.StringValue2 ?? "");
                    break;

                // Companion effects
                case EffectType.ModifyCompanionLoyalty:
                    if (Enum.TryParse<CompanionId>(effect.StringValue, out var loyalCompId))
                    {
                        CompanionSystem.Instance.ModifyLoyalty(loyalCompId, effect.IntValue, "dialogue choice");
                        terminal?.WriteLine(effect.IntValue > 0
                            ? $"({effect.StringValue}'s loyalty increased)"
                            : $"({effect.StringValue}'s loyalty decreased)", "cyan");
                    }
                    break;

                case EffectType.ModifyCompanionTrust:
                    if (Enum.TryParse<CompanionId>(effect.StringValue, out var trustCompId))
                    {
                        CompanionSystem.Instance.ModifyTrust(trustCompId, effect.IntValue);
                        terminal?.WriteLine(effect.IntValue > 0
                            ? $"({effect.StringValue}'s trust increased)"
                            : $"({effect.StringValue}'s trust decreased)", "cyan");
                    }
                    break;

                case EffectType.AdvanceRomance:
                    if (Enum.TryParse<CompanionId>(effect.StringValue, out var romCompId))
                    {
                        CompanionSystem.Instance.AdvanceRomance(romCompId);
                        terminal?.WriteLine($"(Your relationship with {effect.StringValue} deepens)", "magenta");
                    }
                    break;

                case EffectType.TriggerCompanionDeath:
                    CompanionSystem.Instance.TriggerCompanionDeathByParadox(effect.StringValue ?? "");
                    break;

                // Betrayal effects
                case EffectType.AddBetrayalPoints:
                    BetrayalSystem.Instance.AddBetrayalPoints(effect.StringValue ?? "", effect.IntValue, "dialogue interaction");
                    break;

                case EffectType.ReduceBetrayalPoints:
                    BetrayalSystem.Instance.ReduceBetrayalPoints(effect.StringValue ?? "", effect.IntValue, "act of kindness");
                    break;

                case EffectType.TriggerBetrayal:
                    // This would need terminal for async display
                    story.SetStoryFlag($"betrayal_triggered_{effect.StringValue}", true);
                    break;

                // Ocean Philosophy effects
                case EffectType.GainOceanInsight:
                    OceanPhilosophySystem.Instance.GainInsight(effect.IntValue);
                    terminal?.WriteLine("(A deeper understanding settles within you)", "bright_cyan");
                    break;

                case EffectType.CollectWaveFragment:
                    if (Enum.TryParse<WaveFragment>(effect.StringValue, out var waveFragment))
                    {
                        OceanPhilosophySystem.Instance.CollectFragment(waveFragment);
                        terminal?.WriteLine("(You have collected a Wave Fragment)", "cyan");
                    }
                    break;

                case EffectType.TriggerAwakeningMoment:
                    if (Enum.TryParse<AwakeningMoment>(effect.StringValue, out var awakeningMoment))
                    {
                        OceanPhilosophySystem.Instance.ExperienceMoment(awakeningMoment);
                        terminal?.WriteLine("(Something profound shifts in your understanding)", "bright_cyan");
                    }
                    break;

                // Amnesia effects
                case EffectType.RevealMemory:
                    AmnesiaSystem.Instance.RevealMajorMemory(effect.StringValue ?? "");
                    terminal?.WriteLine("(A memory surfaces from the depths...)", "cyan");
                    break;

                case EffectType.TriggerDream:
                    story.SetStoryFlag($"dream_pending_{effect.StringValue}", true);
                    break;
            }
        }

        /// <summary>
        /// Find a node by ID in the current tree
        /// </summary>
        private DialogueNode? FindNode(string? nodeId)
        {
            if (string.IsNullOrEmpty(nodeId)) return null;

            // All nodes are registered in tree.AllNodes, so search there directly.
            // The previous tree-traversal approach only found nodes 1 level deep from root,
            // missing deeper nodes like veloura_save_path (root → story → save_path).
            foreach (var tree in dialogueTrees.Values)
            {
                if (tree.AllNodes.TryGetValue(nodeId, out var node))
                    return node;
            }
            return null;
        }

        /// <summary>
        /// Process dialogue variables like {PlayerName}, {Level}, etc.
        /// </summary>
        private string ProcessDialogueVariables(string text)
        {
            if (currentPlayer == null) return text;

            var story = StoryProgressionSystem.Instance;

            return text
                .Replace("{PlayerName}", currentPlayer.Name2)
                .Replace("{RealName}", currentPlayer.Name1)
                .Replace("{Level}", currentPlayer.Level.ToString())
                .Replace("{Class}", currentPlayer.Class.ToString())
                .Replace("{Race}", currentPlayer.Race.ToString())
                .Replace("{Gold}", currentPlayer.Gold.ToString())
                .Replace("{Chivalry}", currentPlayer.Chivalry.ToString())
                .Replace("{Darkness}", currentPlayer.Darkness.ToString())
                .Replace("{Cycle}", story.CurrentCycle.ToString())
                .Replace("{Chapter}", story.CurrentChapter.ToString());
        }

        /// <summary>
        /// Get color for a speaker
        /// </summary>
        private string GetSpeakerColor(string speaker)
        {
            return speaker.ToLower() switch
            {
                "mysterious stranger" => "bright_magenta",
                "the stranger" => "bright_magenta",
                "stranger" => "bright_magenta",
                "manwe" => "bright_yellow",
                "the creator" => "bright_yellow",
                "maelketh" => "dark_red",
                "veloura" => "bright_magenta",
                "thorgrim" => "gray",
                "noctura" => "dark_magenta",
                "aurelion" => "bright_yellow",
                "terravok" => "dark_green",
                "king" => "bright_yellow",
                "guard" => "gray",
                "merchant" => "green",
                "priest" => "cyan",
                "innkeeper" => "yellow",
                _ => "white"
            };
        }

        /// <summary>
        /// Get color for a choice based on its alignment impact
        /// </summary>
        private string GetChoiceColor(DialogueChoice choice)
        {
            // Check if this choice has alignment effects
            foreach (var effect in choice.Effects)
            {
                if (effect.Type == EffectType.AddChivalry)
                    return "bright_cyan";
                if (effect.Type == EffectType.AddDarkness)
                    return "dark_red";
            }

            return choice.Tone switch
            {
                DialogueTone.Aggressive => "red",
                DialogueTone.Friendly => "green",
                DialogueTone.Suspicious => "yellow",
                DialogueTone.Humble => "cyan",
                DialogueTone.Defiant => "bright_red",
                DialogueTone.Wise => "bright_cyan",
                DialogueTone.Greedy => "dark_yellow",
                _ => "white"
            };
        }

        /// <summary>
        /// Register all dialogue trees
        /// </summary>
        private void RegisterAllDialogueTrees()
        {
            // Register opening hook dialogue
            RegisterOpeningHookDialogue();

            // Register Old God dialogues
            RegisterOldGodDialogues();

            // Register NPC dialogues
            RegisterNPCDialogues();

            // GD.Print($"[Dialogue] Registered {dialogueTrees.Count} dialogue trees");
        }

        /// <summary>
        /// Register a dialogue tree
        /// </summary>
        public void RegisterDialogueTree(DialogueTree tree)
        {
            dialogueTrees[tree.Id] = tree;
        }

        #region Opening Hook Dialogue

        private void RegisterOpeningHookDialogue()
        {
            var tree = new DialogueTree
            {
                Id = "mysterious_stranger_intro",
                Name = "The Mysterious Stranger",
                AllNodes = new Dictionary<string, DialogueNode>()
            };

            // Opening node - the stranger approaches
            var intro = new DialogueNode
            {
                Id = "stranger_approach",
                Speaker = "Mysterious Stranger",
                Text = new[]
                {
                    "A cloaked figure emerges from the shadows, their face hidden.",
                    "Yet you sense ancient eyes studying you intently.",
                    "",
                    "\"Ah... {PlayerName}. I've been waiting for you.\"",
                    "",
                    "\"You don't know me. Not yet. But I know you.\"",
                    "\"I know what you will become. What you MUST become.\"",
                    "",
                    "\"The old gods stir in their prisons. Their chains grow weak.\"",
                    "\"And you... you are the key.\""
                },
                TextColor = "white",
                Choices = new List<DialogueChoice>
                {
                    new()
                    {
                        Text = "Who are you? What do you want from me?",
                        NextNodeId = "stranger_identity",
                        Tone = DialogueTone.Suspicious
                    },
                    new()
                    {
                        Text = "Old gods? I'm just trying to survive here.",
                        NextNodeId = "stranger_dismissive",
                        Tone = DialogueTone.Neutral
                    },
                    new()
                    {
                        Text = "I bow to no gods, old or new. Speak plainly!",
                        NextNodeId = "stranger_defiant",
                        Tone = DialogueTone.Defiant,
                        Effects = new List<DialogueEffect>
                        {
                            new() { Type = EffectType.AddDarkness, IntValue = 5 }
                        }
                    },
                    new()
                    {
                        Text = "If I can help, I will. Tell me more.",
                        NextNodeId = "stranger_willing",
                        Tone = DialogueTone.Friendly,
                        Effects = new List<DialogueEffect>
                        {
                            new() { Type = EffectType.AddChivalry, IntValue = 5 }
                        }
                    }
                }
            };
            tree.AllNodes[intro.Id] = intro;
            tree.RootNode = intro;

            // Identity branch
            var identity = new DialogueNode
            {
                Id = "stranger_identity",
                Speaker = "Mysterious Stranger",
                Text = new[]
                {
                    "The stranger chuckles softly.",
                    "",
                    "\"Who am I? I am many things. A witness. A guide.\"",
                    "\"Some call me Fate. Others, Doom. I prefer... Observer.\"",
                    "",
                    "\"What I want? Nothing for myself.\"",
                    "\"But the world trembles, {PlayerName}. The Seals weaken.\"",
                    "\"Seven gods. Seven prisons. Seven chances to decide the fate of all.\""
                },
                NextNodeId = "stranger_prophecy"
            };
            tree.AllNodes[identity.Id] = identity;

            // Dismissive branch
            var dismissive = new DialogueNode
            {
                Id = "stranger_dismissive",
                Speaker = "Mysterious Stranger",
                Text = new[]
                {
                    "\"Survival?\" The stranger's voice carries amusement.",
                    "",
                    "\"You think you came here by chance? To this realm?\"",
                    "\"No, {PlayerName}. You were called. Chosen.\"",
                    "",
                    "\"Survive if you wish. But eventually, you will face them.\"",
                    "\"The old gods. And when you do... remember this meeting.\""
                },
                NextNodeId = "stranger_prophecy"
            };
            tree.AllNodes[dismissive.Id] = dismissive;

            // Defiant branch
            var defiant = new DialogueNode
            {
                Id = "stranger_defiant",
                Speaker = "Mysterious Stranger",
                Text = new[]
                {
                    "The stranger's eyes flash with something like... approval?",
                    "",
                    "\"Good. GOOD. That fire will serve you well.\"",
                    "\"The old gods respect strength. Fear it, even.\"",
                    "",
                    "\"You may yet become something they never expected.\"",
                    "\"Not their servant. Not their destroyer.\"",
                    "\"Something... new.\""
                },
                NextNodeId = "stranger_prophecy",
                Effects = new List<DialogueEffect>
                {
                    new() { Type = EffectType.SetStoryFlag, StringValue = "defiant_to_stranger" }
                }
            };
            tree.AllNodes[defiant.Id] = defiant;

            // Willing branch
            var willing = new DialogueNode
            {
                Id = "stranger_willing",
                Speaker = "Mysterious Stranger",
                Text = new[]
                {
                    "The stranger seems pleased.",
                    "",
                    "\"A willing heart. Rare in these dark times.\"",
                    "\"But do not confuse willingness with weakness.\"",
                    "",
                    "\"The path ahead requires both courage AND cunning.\"",
                    "\"Mercy AND strength. You must become more than mortal.\"",
                    "\"Are you ready for such a burden?\""
                },
                NextNodeId = "stranger_prophecy",
                Effects = new List<DialogueEffect>
                {
                    new() { Type = EffectType.SetStoryFlag, StringValue = "willing_hero" }
                }
            };
            tree.AllNodes[willing.Id] = willing;

            // The prophecy
            var prophecy = new DialogueNode
            {
                Id = "stranger_prophecy",
                Speaker = "Mysterious Stranger",
                Text = new[]
                {
                    "The stranger raises a hand, and shadows dance.",
                    "",
                    "\"Seven old gods. Imprisoned eons ago by Manwe the Creator.\"",
                    "\"Maelketh of War. Veloura of Passion. Thorgrim of Law.\"",
                    "\"Noctura of Shadows. Aurelion of Light. Terravok of Earth.\"",
                    "\"And others... forgotten.\"\n",
                    "",
                    "\"Their prisons fail. One by one, they will break free.\"",
                    "\"And you, {PlayerName}, must face them all.\"",
                    "",
                    "\"Kill them. Save them. Use them. The choice... is yours.\""
                },
                TextColor = "bright_cyan",
                Choices = new List<DialogueChoice>
                {
                    new()
                    {
                        Text = "Why me? I'm nobody special.",
                        NextNodeId = "stranger_why_me",
                        Tone = DialogueTone.Humble
                    },
                    new()
                    {
                        Text = "These 'gods' - can they truly be killed?",
                        NextNodeId = "stranger_killing_gods",
                        Tone = DialogueTone.Suspicious
                    },
                    new()
                    {
                        Text = "What happens if I refuse this 'destiny'?",
                        NextNodeId = "stranger_refuse",
                        Tone = DialogueTone.Defiant
                    }
                }
            };
            tree.AllNodes[prophecy.Id] = prophecy;

            // Why me branch
            var whyMe = new DialogueNode
            {
                Id = "stranger_why_me",
                Speaker = "Mysterious Stranger",
                Text = new[]
                {
                    "\"Nobody special?\" The stranger laughs.",
                    "",
                    "\"Every great tale begins with 'nobody special.'\"",
                    "\"It's what you BECOME that matters.\"",
                    "",
                    "\"Grow strong. Seek the artifacts. Learn the truth.\"",
                    "\"When the first seal breaks, you will understand.\""
                },
                NextNodeId = "stranger_gift"
            };
            tree.AllNodes[whyMe.Id] = whyMe;

            // Killing gods branch
            var killingGods = new DialogueNode
            {
                Id = "stranger_killing_gods",
                Speaker = "Mysterious Stranger",
                Text = new[]
                {
                    "\"Killed?\" The word hangs in the air.",
                    "",
                    "\"Gods do not die as mortals do. But they can be... ended.\"",
                    "\"Scattered. Absorbed. Consumed.\"",
                    "",
                    "\"The artifacts of the Creator hold that power.\"",
                    "\"Seven artifacts. Seven gods. No coincidence.\""
                },
                NextNodeId = "stranger_gift"
            };
            tree.AllNodes[killingGods.Id] = killingGods;

            // Refuse branch
            var refuse = new DialogueNode
            {
                Id = "stranger_refuse",
                Speaker = "Mysterious Stranger",
                Text = new[]
                {
                    "\"Refuse?\" The stranger's voice carries infinite sadness.",
                    "",
                    "\"You may try. But destiny is not a request.\"",
                    "\"The gods will come. The seals will break.\"",
                    "\"And when they do, you will fight or you will fall.\"",
                    "",
                    "\"There is no third path. Not for you.\""
                },
                NextNodeId = "stranger_gift"
            };
            tree.AllNodes[refuse.Id] = refuse;

            // The stranger's gift
            var gift = new DialogueNode
            {
                Id = "stranger_gift",
                Speaker = "Mysterious Stranger",
                Text = new[]
                {
                    "The stranger presses something cold into your hand.",
                    "A small iron key, ancient and worn.",
                    "",
                    "\"A gift. And a burden. This key opens... possibilities.\"",
                    "\"When you are ready, seek the First Seal.\"",
                    "\"In the deepest dungeon. In the darkest shadow.\"",
                    "",
                    "\"We will meet again, {PlayerName}. I promise you that.\"",
                    "",
                    "The stranger fades into shadow, leaving only echoes."
                },
                TextColor = "bright_magenta",
                IsEndNode = true,
                Effects = new List<DialogueEffect>
                {
                    new() { Type = EffectType.SetStoryFlag, StringValue = "met_mysterious_stranger" },
                    new() { Type = EffectType.SetStoryFlag, StringValue = "has_ancient_key" },
                    new() { Type = EffectType.GiveItem, StringValue = "Ancient Iron Key" },
                    new() { Type = EffectType.AddExperience, IntValue = 100 },
                    new() { Type = EffectType.RecordChoice, StringValue = "stranger_intro", StringValue2 = "completed" }
                }
            };
            tree.AllNodes[gift.Id] = gift;

            RegisterDialogueTree(tree);
        }

        #endregion

        #region Old God Dialogues

        private void RegisterOldGodDialogues()
        {
            // Maelketh - God of War
            RegisterMaelkethDialogue();

            // Veloura - Goddess of Passion (Saveable)
            RegisterVelouraDialogue();

            // Thorgrim - God of Law
            RegisterThorgrimDialogue();

            // Noctura - Goddess of Shadows (Can become ally)
            RegisterNocturaDialogue();
        }

        private void RegisterMaelkethDialogue()
        {
            var tree = new DialogueTree
            {
                Id = "maelketh_encounter",
                Name = "Maelketh, God of War",
                AllNodes = new Dictionary<string, DialogueNode>()
            };

            var intro = new DialogueNode
            {
                Id = "maelketh_intro",
                Speaker = "Maelketh",
                Text = new[]
                {
                    "The chamber erupts in crimson flame.",
                    "Before you stands a titan of blood and iron,",
                    "his form scarred by countless battles.",
                    "",
                    "\"AT LAST! A WARRIOR APPROACHES!\"",
                    "",
                    "His voice shakes the very stones.",
                    "",
                    "\"Ten thousand years I have waited.\"",
                    "\"Ten thousand years without the GLORY of combat.\"",
                    "\"And now... YOU.\""
                },
                TextColor = "dark_red",
                Choices = new List<DialogueChoice>
                {
                    new()
                    {
                        Text = "I am here to destroy you, Maelketh.",
                        NextNodeId = "maelketh_fight",
                        Tone = DialogueTone.Aggressive
                    },
                    new()
                    {
                        Text = "We don't have to fight. There's another way.",
                        NextNodeId = "maelketh_peace",
                        Tone = DialogueTone.Friendly
                    },
                    new()
                    {
                        Text = "Teach me. Show me the ways of war.",
                        NextNodeId = "maelketh_teach",
                        Tone = DialogueTone.Humble
                    }
                }
            };
            tree.AllNodes[intro.Id] = intro;
            tree.RootNode = intro;

            var fight = new DialogueNode
            {
                Id = "maelketh_fight",
                Speaker = "Maelketh",
                Text = new[]
                {
                    "The god's face splits into a terrible grin.",
                    "",
                    "\"YESSSS! THIS is what I crave!\"",
                    "\"Come then, mortal! Show me your fury!\"",
                    "\"Let us see if your courage matches your words!\""
                },
                IsEndNode = true,
                Effects = new List<DialogueEffect>
                {
                    new() { Type = EffectType.SetStoryFlag, StringValue = "maelketh_combat_start" }
                }
            };
            tree.AllNodes[fight.Id] = fight;

            var peace = new DialogueNode
            {
                Id = "maelketh_peace",
                Speaker = "Maelketh",
                Text = new[]
                {
                    "The god's laughter booms like thunder.",
                    "",
                    "\"PEACE?! From the God of WAR?!\"",
                    "\"You understand NOTHING, mortal!\"",
                    "",
                    "\"War is not my curse - it is my ESSENCE!\"",
                    "\"There IS no other way. There never was.\""
                },
                NextNodeId = "maelketh_fight"
            };
            tree.AllNodes[peace.Id] = peace;

            var teach = new DialogueNode
            {
                Id = "maelketh_teach",
                Speaker = "Maelketh",
                Text = new[]
                {
                    "Maelketh pauses, studying you with ancient eyes.",
                    "",
                    "\"You wish to LEARN? Interesting.\"",
                    "\"Most who stand before me seek only victory.\"",
                    "",
                    "\"Very well. I will teach you.\"",
                    "\"But know this: My lessons are written in BLOOD.\"",
                    "\"Survive, and you will be stronger.\"",
                    "\"Fail, and you will be FORGOTTEN.\""
                },
                IsEndNode = true,
                Effects = new List<DialogueEffect>
                {
                    new() { Type = EffectType.SetStoryFlag, StringValue = "maelketh_combat_start" },
                    new() { Type = EffectType.SetStoryFlag, StringValue = "maelketh_teaching" }
                }
            };
            tree.AllNodes[teach.Id] = teach;

            RegisterDialogueTree(tree);
        }

        private void RegisterVelouraDialogue()
        {
            var tree = new DialogueTree
            {
                Id = "veloura_encounter",
                Name = "Veloura, Goddess of Passion",
                AllNodes = new Dictionary<string, DialogueNode>()
            };

            var intro = new DialogueNode
            {
                Id = "veloura_intro",
                Speaker = "Veloura",
                Text = new[]
                {
                    "The chamber fills with intoxicating perfume.",
                    "A figure of heartbreaking beauty materializes,",
                    "her form shifting between desire and despair.",
                    "",
                    "\"Another comes to... what? Kill me? Save me?\"",
                    "Her voice carries infinite weariness.",
                    "",
                    "\"I have been both loved and hated beyond measure.\"",
                    "\"Now I am simply... tired.\""
                },
                TextColor = "bright_magenta",
                Choices = new List<DialogueChoice>
                {
                    new()
                    {
                        Text = "Your corruption ends here, goddess.",
                        NextNodeId = "veloura_fight",
                        Tone = DialogueTone.Aggressive
                    },
                    new()
                    {
                        Text = "You seem... different from the others. What happened to you?",
                        NextNodeId = "veloura_story",
                        Tone = DialogueTone.Friendly,
                        Effects = new List<DialogueEffect>
                        {
                            new() { Type = EffectType.SetStoryFlag, StringValue = "veloura_empathy" }
                        }
                    },
                    new()
                    {
                        Text = "Perhaps you don't have to be tired anymore.",
                        NextNodeId = "veloura_hope",
                        Tone = DialogueTone.Wise,
                        Condition = new DialogueCondition
                        {
                            Type = ConditionType.AlignmentAbove,
                            IntValue = 200
                        },
                        Effects = new List<DialogueEffect>
                        {
                            new() { Type = EffectType.SetStoryFlag, StringValue = "veloura_hope_offered" }
                        }
                    }
                }
            };
            tree.AllNodes[intro.Id] = intro;
            tree.RootNode = intro;

            var fight = new DialogueNode
            {
                Id = "veloura_fight",
                Speaker = "Veloura",
                Text = new[]
                {
                    "Veloura's eyes flash with resigned fury.",
                    "",
                    "\"Of course. It always comes to this.\"",
                    "\"Very well. Let us dance our final dance.\"",
                    "",
                    "\"Know that I take no pleasure in this.\"",
                    "\"...That is a lie. I take pleasure in EVERYTHING.\""
                },
                IsEndNode = true,
                Effects = new List<DialogueEffect>
                {
                    new() { Type = EffectType.SetStoryFlag, StringValue = "veloura_combat_start" }
                }
            };
            tree.AllNodes[fight.Id] = fight;

            var story = new DialogueNode
            {
                Id = "veloura_story",
                Speaker = "Veloura",
                Text = new[]
                {
                    "The goddess's form wavers, showing cracks of light.",
                    "",
                    "\"Different? Perhaps. I was not always... this.\"",
                    "\"Once I was pure love. Pure passion.\"",
                    "",
                    "\"But Manwe... Manwe perverted my essence.\"",
                    "\"Made me a weapon. Made me corrupt all I touched.\"",
                    "",
                    "\"I have ruined kingdoms. Destroyed families.\"",
                    "\"Not because I wished to... but because I MUST.\""
                },
                Choices = new List<DialogueChoice>
                {
                    new()
                    {
                        Text = "Then let me end your suffering.",
                        NextNodeId = "veloura_mercy_kill",
                        Tone = DialogueTone.Neutral
                    },
                    new()
                    {
                        Text = "What if the curse could be broken?",
                        NextNodeId = "veloura_save_path",
                        Tone = DialogueTone.Wise,
                        Effects = new List<DialogueEffect>
                        {
                            new() { Type = EffectType.SetStoryFlag, StringValue = "veloura_save_possible" }
                        }
                    }
                }
            };
            tree.AllNodes[story.Id] = story;

            var hope = new DialogueNode
            {
                Id = "veloura_hope",
                Speaker = "Veloura",
                Text = new[]
                {
                    "For a moment, the goddess looks almost... human.",
                    "",
                    "\"Hope? I had forgotten that word.\"",
                    "\"You speak of salvation? For ME?\"",
                    "",
                    "\"No one has ever... no one has tried to...\"",
                    "",
                    "Tears of starlight fall from her eyes."
                },
                NextNodeId = "veloura_save_path"
            };
            tree.AllNodes[hope.Id] = hope;

            var savePath = new DialogueNode
            {
                Id = "veloura_save_path",
                Speaker = "Veloura",
                Text = new[]
                {
                    "\"The curse... it could be broken. Perhaps.\"",
                    "\"But not by force. Not by battle.\"",
                    "",
                    "\"There is an artifact. The Soulweaver's Loom.\"",
                    "\"It could untangle what Manwe corrupted.\"",
                    "",
                    "\"But it lies in the deepest shadow.\"",
                    "\"Would you... would you truly seek it? For me?\""
                },
                Choices = new List<DialogueChoice>
                {
                    new()
                    {
                        Text = "I will find this artifact and free you.",
                        NextNodeId = "veloura_save_promise",
                        Tone = DialogueTone.Friendly,
                        Effects = new List<DialogueEffect>
                        {
                            new() { Type = EffectType.SetStoryFlag, StringValue = "veloura_save_quest" },
                            new() { Type = EffectType.AddChivalry, IntValue = 50 }
                        }
                    },
                    new()
                    {
                        Text = "I'm sorry, but I cannot trust a goddess of corruption.",
                        NextNodeId = "veloura_fight",
                        Tone = DialogueTone.Suspicious
                    }
                }
            };
            tree.AllNodes[savePath.Id] = savePath;

            var savePromise = new DialogueNode
            {
                Id = "veloura_save_promise",
                Speaker = "Veloura",
                Text = new[]
                {
                    "The goddess weeps openly now, but these are tears of joy.",
                    "",
                    "\"You are... remarkable, mortal.\"",
                    "\"I will wait. I have waited ten thousand years.\"",
                    "\"I can wait a little longer.\"",
                    "",
                    "\"Find the Soulweaver's Loom. Return to me.\"",
                    "\"And perhaps... perhaps I can be what I once was.\"",
                    "",
                    "She fades, leaving a warm glow in your heart."
                },
                IsEndNode = true,
                Effects = new List<DialogueEffect>
                {
                    new() { Type = EffectType.SetStoryFlag, StringValue = "veloura_spared" },
                    new() { Type = EffectType.RecordChoice, StringValue = "veloura_fate", StringValue2 = "saved" },
                    new() { Type = EffectType.CollectWaveFragment, StringValue = "TheCorruption" }
                }
            };
            tree.AllNodes[savePromise.Id] = savePromise;

            var mercyKill = new DialogueNode
            {
                Id = "veloura_mercy_kill",
                Speaker = "Veloura",
                Text = new[]
                {
                    "The goddess smiles sadly.",
                    "",
                    "\"A mercy, then. I accept.\"",
                    "\"Perhaps in death I will find the peace I never knew in life.\"",
                    "",
                    "She spreads her arms in acceptance."
                },
                IsEndNode = true,
                Effects = new List<DialogueEffect>
                {
                    new() { Type = EffectType.SetStoryFlag, StringValue = "veloura_combat_start" },
                    new() { Type = EffectType.SetStoryFlag, StringValue = "veloura_mercy_kill" }
                }
            };
            tree.AllNodes[mercyKill.Id] = mercyKill;

            RegisterDialogueTree(tree);
        }

        private void RegisterThorgrimDialogue()
        {
            var tree = new DialogueTree
            {
                Id = "thorgrim_encounter",
                Name = "Thorgrim, God of Law",
                AllNodes = new Dictionary<string, DialogueNode>()
            };

            var intro = new DialogueNode
            {
                Id = "thorgrim_intro",
                Speaker = "Thorgrim",
                Text = new[]
                {
                    "The chamber is perfectly ordered. Geometric. Cold.",
                    "A being of stone and steel stands motionless,",
                    "eyes burning with calculated precision.",
                    "",
                    "\"INTRUDER. You have broken seventeen laws by entering this chamber.\"",
                    "\"PENALTY: Death.\"",
                    "",
                    "\"However. Protocol dictates you may speak in your defense.\"",
                    "\"You have thirty seconds. Choose your words carefully.\""
                },
                TextColor = "gray",
                Choices = new List<DialogueChoice>
                {
                    new()
                    {
                        Text = "I don't recognize your laws, false god!",
                        NextNodeId = "thorgrim_defiance",
                        Tone = DialogueTone.Defiant
                    },
                    new()
                    {
                        Text = "I invoke the Right of Challenge. Trial by combat.",
                        NextNodeId = "thorgrim_challenge",
                        Tone = DialogueTone.Neutral
                    },
                    new()
                    {
                        Text = "Your laws are corrupt. Let me show you true justice.",
                        NextNodeId = "thorgrim_justice",
                        Tone = DialogueTone.Wise,
                        Condition = new DialogueCondition
                        {
                            Type = ConditionType.AlignmentAbove,
                            IntValue = 500
                        }
                    }
                }
            };
            tree.AllNodes[intro.Id] = intro;
            tree.RootNode = intro;

            var defiance = new DialogueNode
            {
                Id = "thorgrim_defiance",
                Speaker = "Thorgrim",
                Text = new[]
                {
                    "\"IRRELEVANT. Laws exist independent of recognition.\"",
                    "\"Gravity does not require your belief to function.\"",
                    "",
                    "\"Your defiance is noted. Added to charges.\"",
                    "\"EXECUTING JUDGMENT.\""
                },
                IsEndNode = true,
                Effects = new List<DialogueEffect>
                {
                    new() { Type = EffectType.SetStoryFlag, StringValue = "thorgrim_combat_start" },
                    new() { Type = EffectType.AddDarkness, IntValue = 10 }
                }
            };
            tree.AllNodes[defiance.Id] = defiance;

            var challenge = new DialogueNode
            {
                Id = "thorgrim_challenge",
                Speaker = "Thorgrim",
                Text = new[]
                {
                    "Thorgrim's eyes flicker, processing.",
                    "",
                    "\"RIGHT OF CHALLENGE. Article 7, Section 12.\"",
                    "\"Valid legal precedent. Request... ACCEPTED.\"",
                    "",
                    "\"Trial by combat granted.\"",
                    "\"Victory grants freedom. Defeat confirms judgment.\"",
                    "\"BEGIN.\""
                },
                IsEndNode = true,
                Effects = new List<DialogueEffect>
                {
                    new() { Type = EffectType.SetStoryFlag, StringValue = "thorgrim_combat_start" },
                    new() { Type = EffectType.SetStoryFlag, StringValue = "thorgrim_honorable_combat" }
                }
            };
            tree.AllNodes[challenge.Id] = challenge;

            var justice = new DialogueNode
            {
                Id = "thorgrim_justice",
                Speaker = "Thorgrim",
                Text = new[]
                {
                    "Thorgrim pauses. For the first time, uncertainty flickers.",
                    "",
                    "\"CORRUPT? Laws cannot be... corrupt. Laws are perfect.\"",
                    "\"Laws ARE justice.\"",
                    "",
                    "\"But... there is a protocol. The REVIEW protocol.\"",
                    "\"If evidence of corruption is presented...\"",
                    "\"...judgment may be... reconsidered.\""
                },
                Choices = new List<DialogueChoice>
                {
                    new()
                    {
                        Text = "Your laws imprison the innocent. Is that justice?",
                        NextNodeId = "thorgrim_question_laws",
                        Tone = DialogueTone.Wise
                    },
                    new()
                    {
                        Text = "Forget it. Let's just fight.",
                        NextNodeId = "thorgrim_defiance",
                        Tone = DialogueTone.Aggressive
                    }
                }
            };
            tree.AllNodes[justice.Id] = justice;

            var questionLaws = new DialogueNode
            {
                Id = "thorgrim_question_laws",
                Speaker = "Thorgrim",
                Text = new[]
                {
                    "The god of law shudders, gears grinding within.",
                    "",
                    "\"PROCESSING... PROCESSING...\"",
                    "\"ERROR. Logical paradox detected.\"",
                    "\"If laws create injustice... then laws ARE injustice.\"",
                    "\"But laws DEFINE justice. Therefore...\"",
                    "",
                    "\"CRITICAL ERROR. SYSTEM FAILURE IMMINENT.\"",
                    "\"You have... broken... something fundamental...\""
                },
                IsEndNode = true,
                Effects = new List<DialogueEffect>
                {
                    new() { Type = EffectType.SetStoryFlag, StringValue = "thorgrim_combat_start" },
                    new() { Type = EffectType.SetStoryFlag, StringValue = "thorgrim_broken_logic" },
                    new() { Type = EffectType.AddChivalry, IntValue = 30 },
                    new() { Type = EffectType.CollectWaveFragment, StringValue = "TheSevenDrops" }
                }
            };
            tree.AllNodes[questionLaws.Id] = questionLaws;

            RegisterDialogueTree(tree);
        }

        private void RegisterNocturaDialogue()
        {
            var tree = new DialogueTree
            {
                Id = "noctura_encounter",
                Name = "Noctura, Goddess of Shadows",
                AllNodes = new Dictionary<string, DialogueNode>()
            };

            // ══════════════════════════════════════════════════════════════
            // INTRO: Branches based on Receptivity and PlayerKnowsTruth
            // ══════════════════════════════════════════════════════════════

            var intro = new DialogueNode
            {
                Id = "noctura_intro",
                Speaker = "Noctura",
                Text = new[]
                {
                    "The shadows themselves coalesce into form.",
                    "A figure of pure darkness, beautiful and terrible,",
                    "with eyes like distant stars."
                },
                TextColor = "dark_magenta",
                Choices = new List<DialogueChoice>
                {
                    // HIGH RECEPTIVITY (50+) → Full reunion, direct alliance
                    new()
                    {
                        Text = "(She recognizes you. You recognize her.)",
                        NextNodeId = "noctura_reunion",
                        Tone = DialogueTone.Wise,
                        Condition = new DialogueCondition
                        {
                            Type = ConditionType.StrangerReceptivityAbove,
                            IntValue = 49
                        }
                    },
                    // MID RECEPTIVITY (25-49) → Teaching path, must prove understanding
                    new()
                    {
                        Text = "(Something about her feels familiar...)",
                        NextNodeId = "noctura_familiar",
                        Tone = DialogueTone.Humble,
                        Condition = new DialogueCondition
                        {
                            Type = ConditionType.StrangerReceptivityAbove,
                            IntValue = 24
                        }
                    },
                    // NEGATIVE RECEPTIVITY → Enraged intro
                    new()
                    {
                        Text = "(The shadows feel hostile, oppressive.)",
                        NextNodeId = "noctura_hostile_intro",
                        Tone = DialogueTone.Aggressive,
                        Condition = new DialogueCondition
                        {
                            Type = ConditionType.StrangerReceptivityBelow,
                            IntValue = 0
                        }
                    },
                    // DEFAULT: Never met or low receptivity (0-24)
                    new()
                    {
                        Text = "(You face the Goddess of Shadows.)",
                        NextNodeId = "noctura_default_intro",
                        Tone = DialogueTone.Neutral
                    }
                }
            };
            tree.AllNodes[intro.Id] = intro;
            tree.RootNode = intro;

            // ══════════════════════════════════════════════════════════════
            // HIGH RECEPTIVITY PATH (50+): The Reunion
            // ══════════════════════════════════════════════════════════════

            var reunion = new DialogueNode
            {
                Id = "noctura_reunion",
                Speaker = "Noctura",
                Text = new[]
                {
                    "The shadows part like curtains, and she steps through.",
                    "Not as a goddess. Not as a stranger.",
                    "As a teacher greeting her finest student.",
                    "",
                    "\"We meet at last. Not as Stranger and traveler,\"",
                    "\"but as teacher and student.\"",
                    "",
                    "\"Every disguise. Every lesson. Every encounter.\"",
                    "\"You listened. You understood.\"",
                    "",
                    "\"Death is not destruction. It is the cocoon.\"",
                    "\"And now, the butterfly emerges.\""
                },
                TextColor = "dark_magenta",
                Choices = new List<DialogueChoice>
                {
                    new()
                    {
                        Text = "I accept your lesson, Noctura. Death is not my enemy -- it is the cocoon.",
                        NextNodeId = "noctura_full_alliance",
                        Tone = DialogueTone.Wise
                    },
                    new()
                    {
                        Text = "I learned from you. But I still want to hear your terms.",
                        NextNodeId = "noctura_terms",
                        Tone = DialogueTone.Neutral
                    },
                    new()
                    {
                        Text = "I listened. But I'm not your student. I'm your equal.",
                        NextNodeId = "noctura_teach_fight",
                        Tone = DialogueTone.Defiant
                    }
                }
            };
            tree.AllNodes[reunion.Id] = reunion;

            // Full alliance - no combat, earned through understanding
            var fullAlliance = new DialogueNode
            {
                Id = "noctura_full_alliance",
                Speaker = "Noctura",
                Text = new[]
                {
                    "Her eyes -- the same starlight eyes from every encounter --",
                    "shine with something that might be tears.",
                    "",
                    "\"In all the centuries... in all the cycles...\"",
                    "\"no one has ever truly understood.\"",
                    "",
                    "\"The graveyard blooms. The candle passes its flame.\"",
                    "\"The wave returns to the ocean.\"",
                    "\"And death... death becomes transformation.\"",
                    "",
                    "Shadows wrap around you. Not cold. Not dark.",
                    "Warm, like a blanket. Like an embrace.",
                    "",
                    "\"I am yours. And you are mine.\"",
                    "\"Not in darkness. In understanding.\"",
                    "\"When you face Manwe, I will be there.\"",
                    "\"Not as shadow. As your teacher. Your ally. Your friend.\""
                },
                IsEndNode = true,
                Effects = new List<DialogueEffect>
                {
                    new() { Type = EffectType.SetStoryFlag, StringValue = "noctura_ally" },
                    new() { Type = EffectType.SetStoryFlag, StringValue = "noctura_pact_sealed" },
                    new() { Type = EffectType.RecordChoice, StringValue = "noctura_fate", StringValue2 = "allied" },
                    new() { Type = EffectType.GiveItem, StringValue = "Shadow Cloak" },
                    new() { Type = EffectType.CollectWaveFragment, StringValue = "ManwesChoice" },
                    new() { Type = EffectType.GainOceanInsight, IntValue = 30 }
                }
            };
            tree.AllNodes[fullAlliance.Id] = fullAlliance;

            // ══════════════════════════════════════════════════════════════
            // MID RECEPTIVITY PATH (25-49): The Test
            // ══════════════════════════════════════════════════════════════

            var familiar = new DialogueNode
            {
                Id = "noctura_familiar",
                Speaker = "Noctura",
                Text = new[]
                {
                    "\"We've met before.\"",
                    "Her voice carries echoes of a dozen other voices.",
                    "The hooded traveler. The beggar. The quiet patron.",
                    "",
                    "\"You listened... sometimes. Enough to be here.\"",
                    "\"But understanding is not the same as accepting.\"",
                    "",
                    "\"Let me ask you one final question.\"",
                    "\"What is death?\""
                },
                TextColor = "dark_magenta",
                Choices = new List<DialogueChoice>
                {
                    new()
                    {
                        Text = "Death is transformation. The cocoon, not the end.",
                        NextNodeId = "noctura_test_passed",
                        Tone = DialogueTone.Wise
                    },
                    new()
                    {
                        Text = "Death is... necessary. Part of the cycle.",
                        NextNodeId = "noctura_test_partial",
                        Tone = DialogueTone.Humble
                    },
                    new()
                    {
                        Text = "Death is what I'll bring you if you stand in my way.",
                        NextNodeId = "noctura_fight",
                        Tone = DialogueTone.Aggressive
                    }
                }
            };
            tree.AllNodes[familiar.Id] = familiar;

            var testPassed = new DialogueNode
            {
                Id = "noctura_test_passed",
                Speaker = "Noctura",
                Text = new[]
                {
                    "*A genuine smile crosses her face.*",
                    "",
                    "\"The cocoon. Yes.\"",
                    "\"You heard every word. You carried the lesson.\"",
                    "",
                    "\"We need not fight. I see what I have taught you\"",
                    "\"lives in your heart, not just your memory.\""
                },
                NextNodeId = "noctura_terms"
            };
            tree.AllNodes[testPassed.Id] = testPassed;

            var testPartial = new DialogueNode
            {
                Id = "noctura_test_partial",
                Speaker = "Noctura",
                Text = new[]
                {
                    "\"Necessary. That is... close.\"",
                    "\"But 'necessary' is cold. Clinical.\"",
                    "",
                    "\"Death is not merely necessary.\"",
                    "\"It is beautiful. It is the rest between breaths.\"",
                    "",
                    "\"You are almost ready. But almost is not enough.\"",
                    "\"Let me show you what I mean.\""
                },
                IsEndNode = true,
                Effects = new List<DialogueEffect>
                {
                    new() { Type = EffectType.SetStoryFlag, StringValue = "noctura_combat_start" },
                    new() { Type = EffectType.SetStoryFlag, StringValue = "noctura_teaching_fight" }
                }
            };
            tree.AllNodes[testPartial.Id] = testPartial;

            // ══════════════════════════════════════════════════════════════
            // NEGATIVE RECEPTIVITY PATH: The Scorned Teacher
            // ══════════════════════════════════════════════════════════════

            var hostileIntro = new DialogueNode
            {
                Id = "noctura_hostile_intro",
                Speaker = "Noctura",
                Text = new[]
                {
                    "The shadows SURGE. Not inviting. Suffocating.",
                    "",
                    "\"You.\"",
                    "\"The one who shunned my every teaching.\"",
                    "\"Who spat on every lesson I offered.\"",
                    "",
                    "\"I came to you as a beggar. You mocked me.\"",
                    "\"I came as a traveler. You threatened me.\"",
                    "\"I came as a teacher. You refused to learn.\"",
                    "",
                    "\"Very well. Let me teach you the hard way.\""
                },
                TextColor = "dark_magenta",
                Choices = new List<DialogueChoice>
                {
                    new()
                    {
                        Text = "I'll destroy you like every other obstacle in my path.",
                        NextNodeId = "noctura_fight_enraged",
                        Tone = DialogueTone.Aggressive
                    },
                    new()
                    {
                        Text = "Wait. I was wrong to dismiss you. I see that now.",
                        NextNodeId = "noctura_last_chance",
                        Tone = DialogueTone.Humble
                    }
                }
            };
            tree.AllNodes[hostileIntro.Id] = hostileIntro;

            var fightEnraged = new DialogueNode
            {
                Id = "noctura_fight_enraged",
                Speaker = "Noctura",
                Text = new[]
                {
                    "\"DESTROY me?\"",
                    "",
                    "The shadows explode outward. The room goes dark.",
                    "Only her eyes remain, burning like cold stars.",
                    "",
                    "\"I am the shadow between every heartbeat.\"",
                    "\"I am the pause between every breath.\"",
                    "\"I am the silence that comes for everyone.\"",
                    "",
                    "\"You cannot destroy me. But I will show you what I am.\""
                },
                IsEndNode = true,
                Effects = new List<DialogueEffect>
                {
                    new() { Type = EffectType.SetStoryFlag, StringValue = "noctura_combat_start" },
                    new() { Type = EffectType.SetStoryFlag, StringValue = "noctura_enraged" }
                }
            };
            tree.AllNodes[fightEnraged.Id] = fightEnraged;

            var lastChance = new DialogueNode
            {
                Id = "noctura_last_chance",
                Speaker = "Noctura",
                Text = new[]
                {
                    "The shadows pause. The oppressive darkness lifts, slightly.",
                    "",
                    "\"Wrong? You were wrong?\"",
                    "She studies you. Ten thousand years of patience behind those eyes.",
                    "",
                    "\"Words are cheap. Understanding is earned.\"",
                    "\"Prove it. Face me. Not to destroy.\"",
                    "\"To LEARN.\""
                },
                IsEndNode = true,
                Effects = new List<DialogueEffect>
                {
                    new() { Type = EffectType.SetStoryFlag, StringValue = "noctura_combat_start" },
                    new() { Type = EffectType.SetStoryFlag, StringValue = "noctura_teaching_fight" }
                }
            };
            tree.AllNodes[lastChance.Id] = lastChance;

            // ══════════════════════════════════════════════════════════════
            // DEFAULT PATH: Standard encounter (no/low Stranger history)
            // ══════════════════════════════════════════════════════════════

            var defaultIntro = new DialogueNode
            {
                Id = "noctura_default_intro",
                Speaker = "Noctura",
                Text = new[]
                {
                    "\"A stranger comes.\"",
                    "Her voice is silk and secrets.",
                    "",
                    "\"We have never met. A pity.\"",
                    "\"I had so much to teach you.\"",
                    "",
                    "\"Most who seek the Goddess of Shadows\"",
                    "\"never know they've already failed.\""
                },
                TextColor = "dark_magenta",
                Choices = new List<DialogueChoice>
                {
                    new()
                    {
                        Text = "I'm here to end your darkness, goddess.",
                        NextNodeId = "noctura_fight",
                        Tone = DialogueTone.Aggressive
                    },
                    new()
                    {
                        Text = "I seek knowledge, not conflict. What can you teach me?",
                        NextNodeId = "noctura_teach",
                        Tone = DialogueTone.Humble
                    },
                    new()
                    {
                        Text = "Perhaps we can help each other. An alliance.",
                        NextNodeId = "noctura_terms",
                        Tone = DialogueTone.Suspicious,
                        Condition = new DialogueCondition
                        {
                            Type = ConditionType.StrangerEncountersAbove,
                            IntValue = 2
                        }
                    }
                }
            };
            tree.AllNodes[defaultIntro.Id] = defaultIntro;

            // ══════════════════════════════════════════════════════════════
            // SHARED NODES: Fight, Teach, Terms, Deal
            // ══════════════════════════════════════════════════════════════

            var fight = new DialogueNode
            {
                Id = "noctura_fight",
                Speaker = "Noctura",
                Text = new[]
                {
                    "The goddess laughs, shadows rippling.",
                    "",
                    "\"End my darkness? Child, I AM darkness.\"",
                    "\"You cannot fight what you cannot see.\"",
                    "\"You cannot harm what does not exist.\"",
                    "",
                    "\"But very well. Let us play your game.\""
                },
                IsEndNode = true,
                Effects = new List<DialogueEffect>
                {
                    new() { Type = EffectType.SetStoryFlag, StringValue = "noctura_combat_start" }
                }
            };
            tree.AllNodes[fight.Id] = fight;

            var teach = new DialogueNode
            {
                Id = "noctura_teach",
                Speaker = "Noctura",
                Text = new[]
                {
                    "\"Knowledge?\" Her eyes gleam with interest.",
                    "",
                    "\"So few seek understanding. Most want only power.\"",
                    "\"But knowledge IS power, wielded correctly.\"",
                    "",
                    "\"I could teach you the ways of shadow.\"",
                    "\"To move unseen. To strike unfelt.\"",
                    "\"But such lessons come with... obligations.\""
                },
                Choices = new List<DialogueChoice>
                {
                    new()
                    {
                        Text = "What obligations? I'll hear your terms.",
                        NextNodeId = "noctura_terms",
                        Tone = DialogueTone.Neutral
                    },
                    new()
                    {
                        Text = "I will not be bound to a goddess. Forget it.",
                        NextNodeId = "noctura_fight",
                        Tone = DialogueTone.Defiant
                    }
                }
            };
            tree.AllNodes[teach.Id] = teach;

            // Teaching fight yields → alliance after proving yourself
            var teachFight = new DialogueNode
            {
                Id = "noctura_teach_fight",
                Speaker = "Noctura",
                Text = new[]
                {
                    "\"Equal?\" She tilts her head, amused.",
                    "",
                    "\"Very well. Prove it.\"",
                    "\"If you can survive my shadows,\"",
                    "\"I will accept you as more than a student.\"",
                    "",
                    "The darkness closes in, but it feels less like a threat",
                    "and more like an invitation."
                },
                IsEndNode = true,
                Effects = new List<DialogueEffect>
                {
                    new() { Type = EffectType.SetStoryFlag, StringValue = "noctura_combat_start" },
                    new() { Type = EffectType.SetStoryFlag, StringValue = "noctura_teaching_fight" }
                }
            };
            tree.AllNodes[teachFight.Id] = teachFight;

            var terms = new DialogueNode
            {
                Id = "noctura_terms",
                Speaker = "Noctura",
                Text = new[]
                {
                    "\"My terms are simple.\"",
                    "",
                    "\"When you face the Creator, you will not destroy me.\"",
                    "\"Instead, you will RELEASE me. Return me to the world.\"",
                    "",
                    "\"In exchange, I will grant you power over shadow.\"",
                    "\"And when the time comes... I will fight beside you.\"",
                    "",
                    "\"What say you? Deal?\""
                },
                Choices = new List<DialogueChoice>
                {
                    new()
                    {
                        Text = "I accept. We have a deal, goddess.",
                        NextNodeId = "noctura_deal",
                        Tone = DialogueTone.Neutral,
                        Effects = new List<DialogueEffect>
                        {
                            new() { Type = EffectType.SetStoryFlag, StringValue = "noctura_ally" }
                        }
                    },
                    new()
                    {
                        Text = "I will not bargain with darkness. Prepare yourself!",
                        NextNodeId = "noctura_fight",
                        Tone = DialogueTone.Defiant,
                        Effects = new List<DialogueEffect>
                        {
                            new() { Type = EffectType.AddChivalry, IntValue = 25 }
                        }
                    }
                }
            };
            tree.AllNodes[terms.Id] = terms;

            var deal = new DialogueNode
            {
                Id = "noctura_deal",
                Speaker = "Noctura",
                Text = new[]
                {
                    "Shadows wrap around you like a second skin.",
                    "Cold, but not unpleasant. Empowering.",
                    "",
                    "\"The pact is sealed.\"",
                    "",
                    "\"You may call upon the shadows now. They will answer.\"",
                    "\"And when you face Manwe... look for me in the darkness.\"",
                    "",
                    "\"Until then. Walk carefully.\""
                },
                IsEndNode = true,
                Effects = new List<DialogueEffect>
                {
                    new() { Type = EffectType.SetStoryFlag, StringValue = "noctura_pact_sealed" },
                    new() { Type = EffectType.RecordChoice, StringValue = "noctura_fate", StringValue2 = "allied" },
                    new() { Type = EffectType.GiveItem, StringValue = "Shadow Cloak" },
                    new() { Type = EffectType.CollectWaveFragment, StringValue = "ManwesChoice" },
                    new() { Type = EffectType.GainOceanInsight, IntValue = 15 }
                }
            };
            tree.AllNodes[deal.Id] = deal;

            RegisterDialogueTree(tree);
        }

        #endregion

        #region NPC Dialogues

        private void RegisterNPCDialogues()
        {
            // These will be expanded later with more NPC conversations
            // For now, register placeholder trees for key NPCs
        }

        #endregion

        /// <summary>
        /// Check if a dialogue has been completed
        /// </summary>
        public bool HasCompletedDialogue(string treeId)
        {
            return dialogueHistory.Contains(treeId);
        }
    }

    #region Dialogue Data Classes

    public class DialogueTree
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public DialogueNode RootNode { get; set; } = new();
        public Dictionary<string, DialogueNode> AllNodes { get; set; } = new();
    }

    public class DialogueNode
    {
        public string Id { get; set; } = "";
        public string Speaker { get; set; } = "";
        public string[] Text { get; set; } = Array.Empty<string>();
        public string? TextColor { get; set; }
        public List<DialogueChoice> Choices { get; set; } = new();
        public string? NextNodeId { get; set; }
        public bool IsEndNode { get; set; }
        public List<DialogueEffect> Effects { get; set; } = new();
    }

    public class DialogueChoice
    {
        public string Text { get; set; } = "";
        public string NextNodeId { get; set; } = "";
        public DialogueTone Tone { get; set; } = DialogueTone.Neutral;
        public DialogueCondition? Condition { get; set; }
        public List<DialogueEffect> Effects { get; set; } = new();
        public bool IsAutoSelect { get; set; }
    }

    public class DialogueCondition
    {
        public ConditionType Type { get; set; }
        public int IntValue { get; set; }
        public string? StringValue { get; set; }
    }

    public class DialogueEffect
    {
        public EffectType Type { get; set; }
        public int IntValue { get; set; }
        public string? StringValue { get; set; }
        public string? StringValue2 { get; set; }
    }

    public class DialogueResult
    {
        public bool Completed { get; set; }
        public DialogueNode? EndNode { get; set; }
    }

    public enum DialogueTone
    {
        Neutral,
        Friendly,
        Aggressive,
        Suspicious,
        Humble,
        Defiant,
        Wise,
        Greedy,
        Romantic
    }

    public enum ConditionType
    {
        None,
        HasStoryFlag,
        NotHasStoryFlag,
        AlignmentAbove,
        AlignmentBelow,
        LevelAbove,
        LevelBelow,
        HasClass,
        HasRace,
        GoldAbove,
        HasArtifact,
        ChapterAtLeast,
        CycleAbove,
        HasMadeChoice,

        // Companion system conditions
        HasCompanion,              // StringValue = companion name
        CompanionAlive,            // StringValue = companion name
        CompanionDead,             // StringValue = companion name
        CompanionLoyaltyAbove,     // StringValue = companion, IntValue = threshold
        CompanionTrustAbove,       // StringValue = companion, IntValue = threshold
        RomanceLevelAbove,         // StringValue = companion, IntValue = threshold
        HasActiveCompanion,        // Any companion active

        // Grief system conditions
        HasGriefStatus,            // Currently in grief
        GriefStageIs,              // StringValue = stage name (Denial, Anger, etc.)
        CompletedGriefCycle,       // Reached Acceptance stage

        // Betrayal system conditions
        BetrayedBy,                // StringValue = NPC id
        ForgaveBetrayer,           // StringValue = NPC id
        HasPendingBetrayal,        // Any betrayal pending

        // Ocean Philosophy conditions
        AwakeningLevelAbove,       // IntValue = threshold
        HasWaveFragment,           // StringValue = fragment name
        HasOceanInsight,           // IntValue = minimum insight count
        ExperiencedMoment,         // StringValue = AwakeningMoment name

        // Amnesia conditions
        HasMemoryFragment,         // StringValue = fragment name
        MemoryRecoveryAbove,       // IntValue = percentage (0-100)
        TruthRevealed,             // Final revelation occurred

        // Stranger/Noctura conditions
        StrangerReceptivityAbove,  // IntValue = threshold (-100 to 100)
        StrangerReceptivityBelow,  // IntValue = threshold (-100 to 100)
        StrangerEncountersAbove,   // IntValue = encounter count
        StrangerKnowsTruth         // Player knows Stranger is Noctura
    }

    public enum EffectType
    {
        None,
        SetStoryFlag,
        ClearStoryFlag,
        AddChivalry,
        AddDarkness,
        AddGold,
        AddExperience,
        Heal,
        Damage,
        GiveItem,
        RecordChoice,
        AdvanceChapter,
        UnlockArtifact,
        TriggerEvent,

        // Companion effects
        ModifyCompanionLoyalty,    // StringValue = companion, IntValue = amount
        ModifyCompanionTrust,      // StringValue = companion, IntValue = amount
        AdvanceRomance,            // StringValue = companion
        TriggerCompanionDeath,     // StringValue = companion

        // Betrayal effects
        AddBetrayalPoints,         // StringValue = NPC id, IntValue = points
        ReduceBetrayalPoints,      // StringValue = NPC id, IntValue = points
        TriggerBetrayal,           // StringValue = NPC id

        // Ocean Philosophy effects
        GainOceanInsight,          // IntValue = points
        CollectWaveFragment,       // StringValue = fragment name
        TriggerAwakeningMoment,    // StringValue = moment name

        // Amnesia effects
        RevealMemory,              // StringValue = memory key
        TriggerDream               // StringValue = dream sequence
    }

    #endregion
}
