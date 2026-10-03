using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UsurperRemake.Utils;
using UsurperRemake.Systems;
using UsurperRemake.UI;

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
                return new DialogueResult { Completed = false, EndNode = null };
            }

            RecordDialogueMoments(treeId); // v1.1.12

            currentNode = tree.RootNode;
            var result = await ProcessDialogueTree(tree);

            OnDialogueComplete?.Invoke(treeId);
            dialogueHistory.Add(treeId);

            return result;
        }

        /// <summary>v1.1.12: meeting the Creator is an awakening moment.</summary>
        internal static void RecordDialogueMoments(string treeId)
        {
            if (treeId == "manwe_encounter")
                OceanPhilosophySystem.Instance.ExperienceMoment(AwakeningMoment.MetManwe);
        }

        /// <summary>
        /// Process the dialogue tree until completion
        /// </summary>
        private async Task<DialogueResult> ProcessDialogueTree(DialogueTree tree)
        {
            while (currentNode != null)
            {
                // If this is an end node, we're done — emit final text (no choices) and finish.
                if (currentNode.IsEndNode)
                {
                    if (GameConfig.ElectronMode)
                    {
                        EmitDialogueNode(currentNode, new List<DialogueChoice>(), endOfDialogue: true);
                    }
                    else
                    {
                        await DisplayDialogueNode(currentNode);
                    }
                    ApplyNodeEffects(currentNode);
                    if (GameConfig.ElectronMode) ElectronBridge.EmitDialogueClose();
                    return new DialogueResult { Completed = true, EndNode = currentNode };
                }

                // Get available choices (filter by conditions)
                var availableChoices = GetAvailableChoices(currentNode);

                if (availableChoices.Count == 0)
                {
                    // No choices available, render text and auto-proceed to next node
                    if (GameConfig.ElectronMode)
                    {
                        EmitDialogueNode(currentNode, availableChoices, endOfDialogue: false);
                    }
                    else
                    {
                        await DisplayDialogueNode(currentNode);
                    }
                    // v1.2.5: a node passed on the way applies its effects too (before, only an end
                    // node did, so the Stranger's defiant_to_stranger and willing_hero flags were never set)
                    ApplyNodeEffects(currentNode);
                    if (!string.IsNullOrEmpty(currentNode.NextNodeId))
                    {
                        currentNode = FindNode(currentNode.NextNodeId);
                        await Pacing.Wait(1500);
                    }
                    else
                    {
                        break;
                    }
                }
                else if (availableChoices.Count == 1 && availableChoices[0].IsAutoSelect)
                {
                    // Single auto-select choice, render text and proceed automatically
                    if (GameConfig.ElectronMode)
                    {
                        EmitDialogueNode(currentNode, availableChoices, endOfDialogue: false);
                    }
                    else
                    {
                        await DisplayDialogueNode(currentNode);
                    }
                    ApplyNodeEffects(currentNode);
                    ApplyChoiceEffects(availableChoices[0]);
                    currentNode = FindNode(availableChoices[0].NextNodeId);
                    await Pacing.Wait(1000);
                }
                else
                {
                    // Render node text + choices together for Electron, separately for text path
                    if (GameConfig.ElectronMode)
                    {
                        EmitDialogueNode(currentNode, availableChoices, endOfDialogue: false);
                    }
                    else
                    {
                        await DisplayDialogueNode(currentNode);
                    }
                    ApplyNodeEffects(currentNode);

                    // Present choices to the player
                    var selectedChoice = await PresentChoices(availableChoices);
                    if (selectedChoice == null)
                    {
                        // Player aborted dialogue
                        if (GameConfig.ElectronMode) ElectronBridge.EmitDialogueClose();
                        return new DialogueResult { Completed = false, EndNode = null };
                    }

                    ApplyChoiceEffects(selectedChoice);
                    OnChoiceMade?.Invoke(currentNode.Id, selectedChoice);
                    currentNode = FindNode(selectedChoice.NextNodeId);
                }
            }

            if (GameConfig.ElectronMode) ElectronBridge.EmitDialogueClose();
            return new DialogueResult { Completed = true, EndNode = currentNode };
        }

        /// <summary>
        /// Emit a dialogue node + choices to the Electron client. Text rendering
        /// is suppressed in Electron mode (DisplayDialogueNode is bypassed); the
        /// graphical client renders speaker portrait + body text + choice buttons.
        /// </summary>
        private void EmitDialogueNode(DialogueNode node, List<DialogueChoice> choices, bool endOfDialogue)
        {
            if (node == null) return;

            string body = string.Join("\n", NodeLines(node).Select(line => ProcessDialogueVariables(line)));

            var choiceList = new List<ElectronBridge.DialogueChoiceData>();
            for (int i = 0; i < choices.Count; i++)
            {
                choiceList.Add(new ElectronBridge.DialogueChoiceData
                {
                    Key = (i + 1).ToString(),
                    Text = ChoiceText(choices[i]),
                    Style = MapChoiceStyle(choices[i])
                });
            }
            // Always offer "Say nothing" / dismiss as choice 0 unless this is an end node with no choices
            if (!endOfDialogue)
            {
                choiceList.Add(new ElectronBridge.DialogueChoiceData
                {
                    Key = "0",
                    Text = Loc.Get("dialogue.say_nothing_option").Trim('[', ']', '0', ' '),
                    Style = "leave"
                });
            }

            string? portraitKey = !string.IsNullOrEmpty(node.Speaker) ? $"npc:{node.Speaker}" : null;

            ElectronBridge.EmitDialogue(
                speaker: SpeakerLabel(node.Speaker),
                portraitKey: portraitKey,
                text: body,
                choices: choiceList,
                relationLabel: null,
                relationColor: node.TextColor,
                mood: null);
        }

        private static string MapChoiceStyle(DialogueChoice choice)
        {
            // DialogueChoice doesn't expose a type; fall back to color hint if any.
            return "normal";
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
                terminal.WriteLine($"[{SpeakerLabel(node.Speaker)}]", speakerColor);
            }

            // Display each line of dialogue with slight delay
            foreach (var line in NodeLines(node))
            {
                var processedLine = ProcessDialogueVariables(line);
                foreach (var row in FitRows(processedLine, ""))
                    terminal.WriteLine(row, node.TextColor ?? "white");
                await Pacing.Wait(50 * Math.Min(processedLine.Length, 80)); // Typing effect delay
            }

            terminal.WriteLine("");
        }

        /// <summary>
        /// Present choices to the player and get selection
        /// </summary>
        private async Task<DialogueChoice?> PresentChoices(List<DialogueChoice> choices)
        {
            if (terminal == null) return null;

            // Electron mode emitted choices via EmitDialogueNode in the caller; skip text menu.
            if (!GameConfig.ElectronMode)
            {
                terminal.WriteLine(Loc.Get("dialogue.what_do_you_say"), "cyan");
                terminal.WriteLine("");

                for (int i = 0; i < choices.Count; i++)
                {
                    var choice = choices[i];
                    var prefix = $"[{i + 1}] ";
                    var color = GetChoiceColor(choice);
                    foreach (var row in FitRows(prefix + ChoiceText(choice), new string(' ', prefix.Length)))
                        terminal.WriteLine(row, color);
                }

                terminal.WriteLine(Loc.Get("dialogue.say_nothing_option"), "dark_gray");
                terminal.WriteLine("");
            }

            while (true)
            {
                var input = await terminal.GetInputAsync(Loc.Get("ui.your_choice"));

                if (input == "0")
                {
                    return null;
                }

                if (int.TryParse(input, out int choice) && choice >= 1 && choice <= choices.Count)
                {
                    return choices[choice - 1];
                }

                if (!GameConfig.ElectronMode)
                {
                    terminal.WriteLine(Loc.Get("dialogue.invalid_choice"), "red");
                }
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
                    // v0.57.0 — Balanced players satisfy BOTH AlignmentAbove and AlignmentBelow gates
                    // so NPCs with good-aligned dialogue branches open to them, matching the plan's
                    // "access to both good and evil NPC dialogue paths" intent.
                    if (UsurperRemake.Systems.AlignmentSystem.Instance.IsBalanced(currentPlayer))
                        return true;
                    var netAlignment = currentPlayer.Chivalry - currentPlayer.Darkness;
                    return netAlignment > condition.IntValue;

                case ConditionType.AlignmentBelow:
                    if (UsurperRemake.Systems.AlignmentSystem.Instance.IsBalanced(currentPlayer))
                        return true;
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
                    return OceanPhilosophySystem.Instance.InsightIds.Count >= condition.IntValue; // v1.1.12: distinct insights

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
                ApplyEffect(effect, node.Id);
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
                ApplyEffect(effect, currentNode?.Id ?? "");
            }
        }

        /// <summary>
        /// Apply a single dialogue effect
        /// </summary>
        private void ApplyEffect(DialogueEffect effect, string sourceNodeId)
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
                    // v0.57.12: paired movement — dialogue-triggered chivalry also reduces darkness
                    AlignmentSystem.Instance.ChangeAlignment(currentPlayer, effect.IntValue, isGood: true, "dialogue.add_chivalry");
                    currentPlayer.ChivNr++;
                    terminal?.WriteLine(Loc.Get("dialogue.effect_chivalry", effect.IntValue), "bright_green");
                    break;

                case EffectType.AddDarkness:
                    // v0.57.12: paired movement — dialogue-triggered darkness also reduces chivalry
                    AlignmentSystem.Instance.ChangeAlignment(currentPlayer, effect.IntValue, isGood: false, "dialogue.add_darkness");
                    currentPlayer.DarkNr++;
                    terminal?.WriteLine(Loc.Get("dialogue.effect_darkness", effect.IntValue), "dark_red");
                    break;

                case EffectType.AddGold:
                    currentPlayer.Gold += effect.IntValue;
                    if (effect.IntValue > 0)
                        terminal?.WriteLine(Loc.Get("dialogue.effect_gold_received", effect.IntValue), "yellow");
                    else
                        terminal?.WriteLine(Loc.Get("dialogue.effect_gold_lost", -effect.IntValue), "red");
                    break;

                case EffectType.AddExperience:
                    currentPlayer.Experience += effect.IntValue;
                    terminal?.WriteLine(Loc.Get("dialogue.effect_experience", effect.IntValue), "cyan");
                    break;

                case EffectType.Heal:
                    currentPlayer.HP = Math.Min(currentPlayer.HP + effect.IntValue, currentPlayer.MaxHP);
                    terminal?.WriteLine(Loc.Get("dialogue.effect_healed", effect.IntValue), "green");
                    break;

                case EffectType.Damage:
                    currentPlayer.HP = Math.Max(currentPlayer.HP - effect.IntValue, 0);
                    terminal?.WriteLine(Loc.Get("dialogue.effect_damage", effect.IntValue), "red");
                    break;

                case EffectType.GiveItem:
                    // Item inventory add not implemented for dialogue rewards
                    terminal?.WriteLine(Loc.Get("dialogue.effect_item", RewardName(effect.StringValue ?? "")), "bright_yellow");
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
                            ? Loc.Get("dialogue.effect_loyalty_up", effect.StringValue ?? "")
                            : Loc.Get("dialogue.effect_loyalty_down", effect.StringValue ?? ""), "cyan");
                    }
                    break;

                case EffectType.ModifyCompanionTrust:
                    if (Enum.TryParse<CompanionId>(effect.StringValue, out var trustCompId))
                    {
                        CompanionSystem.Instance.ModifyTrust(trustCompId, effect.IntValue);
                        terminal?.WriteLine(effect.IntValue > 0
                            ? Loc.Get("dialogue.effect_trust_up", effect.StringValue ?? "")
                            : Loc.Get("dialogue.effect_trust_down", effect.StringValue ?? ""), "cyan");
                    }
                    break;

                case EffectType.AdvanceRomance:
                    if (Enum.TryParse<CompanionId>(effect.StringValue, out var romCompId))
                    {
                        CompanionSystem.Instance.AdvanceRomance(romCompId);
                        terminal?.WriteLine(Loc.Get("dialogue.effect_romance", effect.StringValue ?? ""), "magenta");
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
                    OceanPhilosophySystem.Instance.GainInsight("dialogue:" + sourceNodeId); // v1.1.12: one insight per node
                    terminal?.WriteLine(Loc.Get("dialogue.effect_insight"), "bright_cyan");
                    break;

                case EffectType.CollectWaveFragment:
                    if (Enum.TryParse<WaveFragment>(effect.StringValue, out var waveFragment))
                    {
                        OceanPhilosophySystem.Instance.CollectFragment(waveFragment);
                        terminal?.WriteLine(Loc.Get("dialogue.effect_wave_fragment"), "cyan");
                    }
                    break;

                case EffectType.TriggerAwakeningMoment:
                    if (Enum.TryParse<AwakeningMoment>(effect.StringValue, out var awakeningMoment))
                    {
                        OceanPhilosophySystem.Instance.ExperienceMoment(awakeningMoment);
                        terminal?.WriteLine(Loc.Get("dialogue.effect_awakening"), "bright_cyan");
                    }
                    break;

                // Amnesia effects
                case EffectType.RevealMemory:
                    AmnesiaSystem.Instance.RevealMajorMemory(effect.StringValue ?? "");
                    terminal?.WriteLine(Loc.Get("dialogue.effect_memory"), "cyan");
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
            foreach (var node in tree.AllNodes.Values)
            {
                node.TreeId = tree.Id;
                foreach (var choice in node.Choices)
                    choice.TextKey = ChoiceKey(tree.Id, node.Id, choice.Id);
            }
            dialogueTrees[tree.Id] = tree;
        }

        // v1.2.5: the node and choice text lives in Localization under keys built from the tree, node and
        // choice ids, so a tree is shown in the reader's language while every id, condition, flag and
        // recorded choice stays English. The keys never depend on the text or on its position in a list.
        internal const int MaxRowWidth = 79;

        internal static string NodeTextKey(string treeId, string nodeId) => $"dialogue.{treeId}.{nodeId}.text";

        internal static string ChoiceKey(string treeId, string nodeId, string choiceId) => $"dialogue.{treeId}.{nodeId}.{choiceId}";

        /// <summary>The node's text in the reader's language, one entry per row as written.</summary>
        internal static string[] NodeLines(DialogueNode node) =>
            Loc.Get(NodeTextKey(node.TreeId, node.Id)).Replace("\r\n", "\n").Split('\n');

        /// <summary>The choice's text in the reader's language.</summary>
        internal static string ChoiceText(DialogueChoice choice) => Loc.Get(choice.TextKey);

        // Speaker titles shown through a key; proper names show as they are. The stored speaker stays
        // English: it picks the colour and the Electron portrait.
        private static readonly Dictionary<string, string> SpeakerKeys = new()
        {
            ["Mysterious Stranger"] = "dialogue.speaker.mysterious_stranger",
        };

        internal static string SpeakerLabel(string? speaker) =>
            string.IsNullOrEmpty(speaker) ? "" : SpeakerKeys.TryGetValue(speaker, out var key) ? Loc.Get(key) : speaker;

        // Dialogue rewards are stored English (GiveItem) and shown by key.
        private static readonly Dictionary<string, string> RewardKeys = new()
        {
            ["Ancient Iron Key"] = "dialogue.reward.ancient_iron_key",
            ["Shadow Cloak"] = "item.shadow_cloak",
        };

        internal static string RewardName(string stored) =>
            RewardKeys.TryGetValue(stored, out var key) ? Loc.Get(key) : stored;

        /// <summary>A row that fits is written as it is; a longer one is wrapped at spaces to the row width,
        /// each later row indented by <paramref name="indent"/>.</summary>
        internal static List<string> FitRows(string text, string indent)
        {
            if (UIHelper.VisibleLength(text) <= MaxRowWidth) return new List<string> { text };
            var rows = UIHelper.WordWrap(text, MaxRowWidth - indent.Length);
            for (int i = 1; i < rows.Count; i++) rows[i] = indent + rows[i];
            return rows;
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
                TextColor = "white",
                Choices = new List<DialogueChoice>
                {
                    new()
                    {
                        Id = "choice_1",
                        NextNodeId = "stranger_identity",
                        Tone = DialogueTone.Suspicious
                    },
                    new()
                    {
                        Id = "choice_2",
                        NextNodeId = "stranger_dismissive",
                        Tone = DialogueTone.Neutral
                    },
                    new()
                    {
                        Id = "choice_3",
                        NextNodeId = "stranger_defiant",
                        Tone = DialogueTone.Defiant,
                        Effects = new List<DialogueEffect>
                        {
                            new() { Type = EffectType.AddDarkness, IntValue = 5 }
                        }
                    },
                    new()
                    {
                        Id = "choice_4",
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
                NextNodeId = "stranger_prophecy"
            };
            tree.AllNodes[identity.Id] = identity;

            // Dismissive branch
            var dismissive = new DialogueNode
            {
                Id = "stranger_dismissive",
                Speaker = "Mysterious Stranger",
                NextNodeId = "stranger_prophecy"
            };
            tree.AllNodes[dismissive.Id] = dismissive;

            // Defiant branch
            var defiant = new DialogueNode
            {
                Id = "stranger_defiant",
                Speaker = "Mysterious Stranger",
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
                TextColor = "bright_cyan",
                Choices = new List<DialogueChoice>
                {
                    new()
                    {
                        Id = "choice_1",
                        NextNodeId = "stranger_why_me",
                        Tone = DialogueTone.Humble
                    },
                    new()
                    {
                        Id = "choice_2",
                        NextNodeId = "stranger_killing_gods",
                        Tone = DialogueTone.Suspicious
                    },
                    new()
                    {
                        Id = "choice_3",
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
                NextNodeId = "stranger_gift"
            };
            tree.AllNodes[whyMe.Id] = whyMe;

            // Killing gods branch
            var killingGods = new DialogueNode
            {
                Id = "stranger_killing_gods",
                Speaker = "Mysterious Stranger",
                NextNodeId = "stranger_gift"
            };
            tree.AllNodes[killingGods.Id] = killingGods;

            // Refuse branch
            var refuse = new DialogueNode
            {
                Id = "stranger_refuse",
                Speaker = "Mysterious Stranger",
                NextNodeId = "stranger_gift"
            };
            tree.AllNodes[refuse.Id] = refuse;

            // The stranger's gift
            var gift = new DialogueNode
            {
                Id = "stranger_gift",
                Speaker = "Mysterious Stranger",
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

            // Aurelion - God of Light
            RegisterAurelionDialogue();

            // Terravok - God of Earth
            RegisterTerravokDialogue();

            // Manwe - The Creator (Final Boss)
            RegisterManweDialogue();
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
                TextColor = "dark_red",
                Choices = new List<DialogueChoice>
                {
                    new()
                    {
                        Id = "choice_1",
                        NextNodeId = "maelketh_fight",
                        Tone = DialogueTone.Aggressive
                    },
                    new()
                    {
                        Id = "choice_2",
                        NextNodeId = "maelketh_peace",
                        Tone = DialogueTone.Friendly
                    },
                    new()
                    {
                        Id = "choice_3",
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
                NextNodeId = "maelketh_fight"
            };
            tree.AllNodes[peace.Id] = peace;

            var teach = new DialogueNode
            {
                Id = "maelketh_teach",
                Speaker = "Maelketh",
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
                TextColor = "bright_magenta",
                Choices = new List<DialogueChoice>
                {
                    new()
                    {
                        Id = "choice_1",
                        NextNodeId = "veloura_fight",
                        Tone = DialogueTone.Aggressive
                    },
                    new()
                    {
                        Id = "choice_2",
                        NextNodeId = "veloura_story",
                        Tone = DialogueTone.Friendly,
                        Effects = new List<DialogueEffect>
                        {
                            new() { Type = EffectType.SetStoryFlag, StringValue = "veloura_empathy" }
                        }
                    },
                    new()
                    {
                        Id = "choice_3",
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
                Choices = new List<DialogueChoice>
                {
                    new()
                    {
                        Id = "choice_1",
                        NextNodeId = "veloura_mercy_kill",
                        Tone = DialogueTone.Neutral
                    },
                    new()
                    {
                        Id = "choice_2",
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
                NextNodeId = "veloura_save_path"
            };
            tree.AllNodes[hope.Id] = hope;

            var savePath = new DialogueNode
            {
                Id = "veloura_save_path",
                Speaker = "Veloura",
                Choices = new List<DialogueChoice>
                {
                    new()
                    {
                        Id = "choice_1",
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
                        Id = "choice_2",
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
                TextColor = "gray",
                Choices = new List<DialogueChoice>
                {
                    new()
                    {
                        Id = "choice_1",
                        NextNodeId = "thorgrim_defiance",
                        Tone = DialogueTone.Defiant
                    },
                    new()
                    {
                        Id = "choice_2",
                        NextNodeId = "thorgrim_challenge",
                        Tone = DialogueTone.Neutral
                    },
                    new()
                    {
                        Id = "choice_3",
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
                Choices = new List<DialogueChoice>
                {
                    new()
                    {
                        Id = "choice_1",
                        NextNodeId = "thorgrim_question_laws",
                        Tone = DialogueTone.Wise
                    },
                    new()
                    {
                        Id = "choice_2",
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
                TextColor = "dark_magenta",
                Choices = new List<DialogueChoice>
                {
                    // HIGH RECEPTIVITY (50+) → Full reunion, direct alliance
                    new()
                    {
                        Id = "choice_1",
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
                        Id = "choice_2",
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
                        Id = "choice_3",
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
                        Id = "choice_4",
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
                TextColor = "dark_magenta",
                Choices = new List<DialogueChoice>
                {
                    new()
                    {
                        Id = "choice_1",
                        NextNodeId = "noctura_full_alliance",
                        Tone = DialogueTone.Wise
                    },
                    new()
                    {
                        Id = "choice_2",
                        NextNodeId = "noctura_terms",
                        Tone = DialogueTone.Neutral
                    },
                    new()
                    {
                        Id = "choice_3",
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
                TextColor = "dark_magenta",
                Choices = new List<DialogueChoice>
                {
                    new()
                    {
                        Id = "choice_1",
                        NextNodeId = "noctura_test_passed",
                        Tone = DialogueTone.Wise
                    },
                    new()
                    {
                        Id = "choice_2",
                        NextNodeId = "noctura_test_partial",
                        Tone = DialogueTone.Humble
                    },
                    new()
                    {
                        Id = "choice_3",
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
                NextNodeId = "noctura_terms"
            };
            tree.AllNodes[testPassed.Id] = testPassed;

            var testPartial = new DialogueNode
            {
                Id = "noctura_test_partial",
                Speaker = "Noctura",
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
                TextColor = "dark_magenta",
                Choices = new List<DialogueChoice>
                {
                    new()
                    {
                        Id = "choice_1",
                        NextNodeId = "noctura_fight_enraged",
                        Tone = DialogueTone.Aggressive
                    },
                    new()
                    {
                        Id = "choice_2",
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
                TextColor = "dark_magenta",
                Choices = new List<DialogueChoice>
                {
                    new()
                    {
                        Id = "choice_1",
                        NextNodeId = "noctura_fight",
                        Tone = DialogueTone.Aggressive
                    },
                    new()
                    {
                        Id = "choice_2",
                        NextNodeId = "noctura_teach",
                        Tone = DialogueTone.Humble
                    },
                    new()
                    {
                        Id = "choice_3",
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
                Choices = new List<DialogueChoice>
                {
                    new()
                    {
                        Id = "choice_1",
                        NextNodeId = "noctura_terms",
                        Tone = DialogueTone.Neutral
                    },
                    new()
                    {
                        Id = "choice_2",
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
                Choices = new List<DialogueChoice>
                {
                    new()
                    {
                        Id = "choice_1",
                        NextNodeId = "noctura_deal",
                        Tone = DialogueTone.Neutral,
                        Effects = new List<DialogueEffect>
                        {
                            new() { Type = EffectType.SetStoryFlag, StringValue = "noctura_ally" }
                        }
                    },
                    new()
                    {
                        Id = "choice_2",
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

        private void RegisterAurelionDialogue()
        {
            var tree = new DialogueTree
            {
                Id = "aurelion_encounter",
                Name = "Aurelion, God of Light",
                AllNodes = new Dictionary<string, DialogueNode>()
            };

            var intro = new DialogueNode
            {
                Id = "aurelion_intro",
                Speaker = "Aurelion",
                TextColor = "bright_yellow",
                Choices = new List<DialogueChoice>
                {
                    new()
                    {
                        Id = "choice_1",
                        NextNodeId = "aurelion_fight_aggressive",
                        Tone = DialogueTone.Aggressive
                    },
                    new()
                    {
                        Id = "choice_2",
                        NextNodeId = "aurelion_free",
                        Tone = DialogueTone.Friendly
                    },
                    new()
                    {
                        Id = "choice_3",
                        NextNodeId = "aurelion_truth",
                        Tone = DialogueTone.Humble
                    }
                }
            };
            tree.AllNodes[intro.Id] = intro;
            tree.RootNode = intro;

            var fightAggressive = new DialogueNode
            {
                Id = "aurelion_fight_aggressive",
                Speaker = "Aurelion",
                IsEndNode = true,
                Effects = new List<DialogueEffect>
                {
                    new() { Type = EffectType.SetStoryFlag, StringValue = "aurelion_combat_start" },
                    new() { Type = EffectType.SetStoryFlag, StringValue = "aurelion_defiant" }
                }
            };
            tree.AllNodes[fightAggressive.Id] = fightAggressive;

            var free = new DialogueNode
            {
                Id = "aurelion_free",
                Speaker = "Aurelion",
                IsEndNode = true,
                Effects = new List<DialogueEffect>
                {
                    new() { Type = EffectType.SetStoryFlag, StringValue = "aurelion_combat_start" },
                    new() { Type = EffectType.SetStoryFlag, StringValue = "aurelion_humble" }
                }
            };
            tree.AllNodes[free.Id] = free;

            var truth = new DialogueNode
            {
                Id = "aurelion_truth",
                Speaker = "Aurelion",
                TextColor = "bright_yellow",
                Choices = new List<DialogueChoice>
                {
                    new()
                    {
                        Id = "choice_1",
                        NextNodeId = "aurelion_alethia",
                        Tone = DialogueTone.Humble
                    },
                    new()
                    {
                        Id = "choice_2",
                        NextNodeId = "aurelion_spare",
                        Tone = DialogueTone.Friendly
                    },
                    new()
                    {
                        Id = "choice_3",
                        NextNodeId = "aurelion_fight_reluctant",
                        Tone = DialogueTone.Neutral
                    }
                }
            };
            tree.AllNodes[truth.Id] = truth;

            var alethia = new DialogueNode
            {
                Id = "aurelion_alethia",
                Speaker = "Aurelion",
                TextColor = "bright_yellow",
                Choices = new List<DialogueChoice>
                {
                    new()
                    {
                        Id = "choice_1",
                        NextNodeId = "aurelion_spare",
                        Tone = DialogueTone.Friendly
                    },
                    new()
                    {
                        Id = "choice_2",
                        NextNodeId = "aurelion_spare",
                        Tone = DialogueTone.Humble
                    },
                    new()
                    {
                        Id = "choice_3",
                        NextNodeId = "aurelion_fight_reluctant",
                        Tone = DialogueTone.Neutral
                    }
                }
            };
            tree.AllNodes[alethia.Id] = alethia;

            var spare = new DialogueNode
            {
                Id = "aurelion_spare",
                Speaker = "Aurelion",
                IsEndNode = true,
                Effects = new List<DialogueEffect>
                {
                    new() { Type = EffectType.SetStoryFlag, StringValue = "aurelion_spared" },
                    new() { Type = EffectType.GainOceanInsight, IntValue = 15 }
                }
            };
            tree.AllNodes[spare.Id] = spare;

            var fightReluctant = new DialogueNode
            {
                Id = "aurelion_fight_reluctant",
                Speaker = "Aurelion",
                IsEndNode = true,
                Effects = new List<DialogueEffect>
                {
                    new() { Type = EffectType.SetStoryFlag, StringValue = "aurelion_combat_start" }
                }
            };
            tree.AllNodes[fightReluctant.Id] = fightReluctant;

            RegisterDialogueTree(tree);
        }

        private void RegisterTerravokDialogue()
        {
            var tree = new DialogueTree
            {
                Id = "terravok_encounter",
                Name = "Terravok, God of Earth",
                AllNodes = new Dictionary<string, DialogueNode>()
            };

            var intro = new DialogueNode
            {
                Id = "terravok_intro",
                Speaker = "Terravok",
                TextColor = "yellow",
                Choices = new List<DialogueChoice>
                {
                    new()
                    {
                        Id = "choice_1",
                        NextNodeId = "terravok_fight_aggressive",
                        Tone = DialogueTone.Aggressive
                    },
                    new()
                    {
                        Id = "choice_2",
                        NextNodeId = "terravok_peaceful",
                        Tone = DialogueTone.Friendly
                    },
                    new()
                    {
                        Id = "choice_3",
                        NextNodeId = "terravok_spare",
                        Tone = DialogueTone.Humble
                    }
                }
            };
            tree.AllNodes[intro.Id] = intro;
            tree.RootNode = intro;

            var fightAggressive = new DialogueNode
            {
                Id = "terravok_fight_aggressive",
                Speaker = "Terravok",
                IsEndNode = true,
                Effects = new List<DialogueEffect>
                {
                    new() { Type = EffectType.SetStoryFlag, StringValue = "terravok_combat_start" },
                    new() { Type = EffectType.SetStoryFlag, StringValue = "terravok_destructive" }
                }
            };
            tree.AllNodes[fightAggressive.Id] = fightAggressive;

            var peaceful = new DialogueNode
            {
                Id = "terravok_peaceful",
                Speaker = "Terravok",
                IsEndNode = true,
                Effects = new List<DialogueEffect>
                {
                    new() { Type = EffectType.SetStoryFlag, StringValue = "terravok_combat_start" },
                    new() { Type = EffectType.SetStoryFlag, StringValue = "terravok_respectful" }
                }
            };
            tree.AllNodes[peaceful.Id] = peaceful;

            var spare = new DialogueNode
            {
                Id = "terravok_spare",
                Speaker = "Terravok",
                IsEndNode = true,
                Effects = new List<DialogueEffect>
                {
                    new() { Type = EffectType.SetStoryFlag, StringValue = "terravok_spared" },
                    new() { Type = EffectType.GainOceanInsight, IntValue = 15 }
                }
            };
            tree.AllNodes[spare.Id] = spare;

            RegisterDialogueTree(tree);
        }

        private void RegisterManweDialogue()
        {
            var tree = new DialogueTree
            {
                Id = "manwe_encounter",
                Name = "Manwe, The Weary Creator",
                AllNodes = new Dictionary<string, DialogueNode>()
            };

            var intro = new DialogueNode
            {
                Id = "manwe_intro",
                Speaker = "Manwe",
                TextColor = "bright_yellow",
                Choices = new List<DialogueChoice>
                {
                    new()
                    {
                        Id = "choice_1",
                        NextNodeId = "manwe_fight_aggressive",
                        Tone = DialogueTone.Aggressive
                    },
                    new()
                    {
                        Id = "choice_2",
                        NextNodeId = "manwe_fight_righteous",
                        Tone = DialogueTone.Neutral
                    },
                    new()
                    {
                        Id = "choice_3",
                        NextNodeId = "manwe_peaceful",
                        Tone = DialogueTone.Friendly
                    }
                }
            };
            tree.AllNodes[intro.Id] = intro;
            tree.RootNode = intro;

            var fightAggressive = new DialogueNode
            {
                Id = "manwe_fight_aggressive",
                Speaker = "Manwe",
                IsEndNode = true,
                Effects = new List<DialogueEffect>
                {
                    new() { Type = EffectType.SetStoryFlag, StringValue = "manwe_combat_start" }
                }
            };
            tree.AllNodes[fightAggressive.Id] = fightAggressive;

            var fightRighteous = new DialogueNode
            {
                Id = "manwe_fight_righteous",
                Speaker = "Manwe",
                IsEndNode = true,
                Effects = new List<DialogueEffect>
                {
                    new() { Type = EffectType.SetStoryFlag, StringValue = "manwe_combat_start" },
                    new() { Type = EffectType.SetStoryFlag, StringValue = "manwe_righteous" }
                }
            };
            tree.AllNodes[fightRighteous.Id] = fightRighteous;

            var peaceful = new DialogueNode
            {
                Id = "manwe_peaceful",
                Speaker = "Manwe",
                TextColor = "bright_yellow",
                Choices = new List<DialogueChoice>
                {
                    new()
                    {
                        Id = "choice_1",
                        NextNodeId = "manwe_fight_compassion",
                        Tone = DialogueTone.Humble
                    },
                    new()
                    {
                        Id = "choice_2",
                        NextNodeId = "manwe_alliance",
                        Tone = DialogueTone.Friendly
                    }
                }
            };
            tree.AllNodes[peaceful.Id] = peaceful;

            var fightCompassion = new DialogueNode
            {
                Id = "manwe_fight_compassion",
                Speaker = "Manwe",
                IsEndNode = true,
                Effects = new List<DialogueEffect>
                {
                    new() { Type = EffectType.SetStoryFlag, StringValue = "manwe_combat_start" },
                    new() { Type = EffectType.SetStoryFlag, StringValue = "manwe_compassion" }
                }
            };
            tree.AllNodes[fightCompassion.Id] = fightCompassion;

            var alliance = new DialogueNode
            {
                Id = "manwe_alliance",
                Speaker = "Manwe",
                IsEndNode = true,
                Effects = new List<DialogueEffect>
                {
                    new() { Type = EffectType.SetStoryFlag, StringValue = "manwe_ally" },
                    new() { Type = EffectType.GainOceanInsight, IntValue = 30 },
                    new() { Type = EffectType.CollectWaveFragment, StringValue = "TheChoice" } // v1.1.12: was "CreatorsRest", not a fragment
                }
            };
            tree.AllNodes[alliance.Id] = alliance;

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
        public string TreeId { get; internal set; } = "";
        public string Speaker { get; set; } = "";
        public string? TextColor { get; set; }
        public List<DialogueChoice> Choices { get; set; } = new();
        public string? NextNodeId { get; set; }
        public bool IsEndNode { get; set; }
        public List<DialogueEffect> Effects { get; set; } = new();
    }

    public class DialogueChoice
    {
        /// <summary>v1.2.5: stable id within its node; the text is shown from Localization by this id.</summary>
        public string Id { get; set; } = "";
        public string TextKey { get; internal set; } = "";
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
