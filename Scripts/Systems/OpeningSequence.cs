using System;
using System.Threading.Tasks;
using Godot;
using UsurperRemake.Utils;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Opening Sequence System - Handles the narrative hook for new players
    /// Triggers the mysterious stranger encounter and sets up the main story
    /// </summary>
    public class OpeningSequenceSystem
    {
        private static OpeningSequenceSystem? instance;
        public static OpeningSequenceSystem Instance => instance ??= new OpeningSequenceSystem();

        private bool strangerEncounterPending = false;
        private int daysSinceCreation = 0;

        /// <summary>
        /// Check if player should trigger opening sequence events
        /// Called each time player enters a location
        /// </summary>
        public async Task<bool> CheckOpeningSequenceTriggers(Character player, string locationId, TerminalEmulator terminal)
        {
            var story = StoryProgressionSystem.Instance;

            // If already met the stranger, no need for opening sequence
            if (story.HasStoryFlag("met_mysterious_stranger"))
            {
                return false;
            }

            // Trigger stranger encounter at level 3+ and after some gameplay
            // This gives player time to learn the basics first
            if (player.Level >= 3 && !strangerEncounterPending)
            {
                // Higher chance on Main Street, Inn, or Dark Alley
                var triggerChance = GetTriggerChance(locationId, player);

                if (new Random().NextDouble() < triggerChance)
                {
                    strangerEncounterPending = true;
                }
            }

            // Execute pending encounter
            if (strangerEncounterPending && CanTriggerHere(locationId))
            {
                strangerEncounterPending = false;
                await TriggerStrangerEncounter(player, terminal);
                return true;
            }

            // After meeting stranger, check for follow-up hooks
            if (story.HasStoryFlag("met_mysterious_stranger"))
            {
                return await CheckFollowUpHooks(player, locationId, terminal);
            }

            return false;
        }

        /// <summary>
        /// Get trigger chance based on location and player state
        /// </summary>
        private double GetTriggerChance(string locationId, Character player)
        {
            double baseChance = 0.05; // 5% base chance

            // Location modifiers
            switch (locationId.ToLower())
            {
                case "main street":
                    baseChance = 0.15; // 15% on main street
                    break;
                case "inn":
                    baseChance = 0.12; // 12% at inn
                    break;
                case "dark alley":
                    baseChance = 0.25; // 25% in dark alley (mysterious!)
                    break;
                case "tavern":
                    baseChance = 0.10;
                    break;
            }

            // Level modifier - higher level = more likely
            baseChance += player.Level * 0.02;

            // Monster kills modifier - active player more likely
            baseChance += Math.Min(player.MKills * 0.005, 0.15);

            return Math.Min(baseChance, 0.5); // Cap at 50%
        }

        /// <summary>
        /// Check if location is appropriate for stranger encounter
        /// </summary>
        private bool CanTriggerHere(string locationId)
        {
            var validLocations = new[]
            {
                "main street", "inn", "dark alley", "tavern",
                "temple", "market"
            };

            return Array.Exists(validLocations,
                loc => locationId.Equals(loc, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Trigger the mysterious stranger encounter
        /// </summary>
        private async Task TriggerStrangerEncounter(Character player, TerminalEmulator terminal)
        {
            // Set up the atmosphere
            terminal.WriteLine("");
            terminal.WriteLine("═══════════════════════════════════════════════════", "dark_magenta");
            terminal.WriteLine("");

            await Task.Delay(1000);

            terminal.WriteLine("The air grows cold.", "gray");
            await Task.Delay(800);

            terminal.WriteLine("Shadows lengthen unnaturally.", "dark_gray");
            await Task.Delay(800);

            terminal.WriteLine("Time itself seems to... pause.", "white");
            await Task.Delay(1200);

            terminal.WriteLine("");
            terminal.WriteLine("You are not alone.", "bright_magenta");
            terminal.WriteLine("");

            await Task.Delay(1500);

            // Run the dialogue
            var dialogue = DialogueSystem.Instance;
            await dialogue.StartDialogue(player, "mysterious_stranger_intro", terminal);

            // After dialogue, set story state
            StoryProgressionSystem.Instance.AdvanceChapter(StoryChapter.TheFirstSeal);

            // Log event
            GD.Print($"[OpeningSequence] Player {player.Name2} completed stranger encounter");
        }

        /// <summary>
        /// Check for follow-up story hooks after the initial encounter
        /// </summary>
        private async Task<bool> CheckFollowUpHooks(Character player, string locationId, TerminalEmulator terminal)
        {
            var story = StoryProgressionSystem.Instance;

            // Check for dungeon hints at specific levels
            if (player.Level >= 10 && !story.HasStoryFlag("first_seal_hint"))
            {
                if (locationId.Equals("temple", StringComparison.OrdinalIgnoreCase))
                {
                    await ShowFirstSealHint(player, terminal);
                    return true;
                }
            }

            // Check for god awakening warnings
            if (player.Level >= 25 && !story.HasStoryFlag("maelketh_stirring_warning"))
            {
                if (locationId.Equals("tavern", StringComparison.OrdinalIgnoreCase) ||
                    locationId.Equals("inn", StringComparison.OrdinalIgnoreCase))
                {
                    await ShowGodStirringWarning(player, terminal, "Maelketh");
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Show hint about the first seal location
        /// </summary>
        private async Task ShowFirstSealHint(Character player, TerminalEmulator terminal)
        {
            terminal.WriteLine("");
            terminal.WriteLine("A weathered priest approaches you with knowing eyes.", "cyan");
            terminal.WriteLine("");

            await Task.Delay(500);

            terminal.WriteLine("\"You carry the mark of destiny, young one.\"", "yellow");
            terminal.WriteLine("\"I have seen it in my visions.\"", "yellow");
            terminal.WriteLine("");

            await Task.Delay(800);

            terminal.WriteLine("\"The First Seal lies in the dungeons below.\"", "white");
            terminal.WriteLine("\"Around the 25th level, where shadows grow deep.\"", "white");
            terminal.WriteLine("\"There, the God of War stirs in his prison.\"", "white");
            terminal.WriteLine("");

            await Task.Delay(800);

            terminal.WriteLine("\"Be ready. Be strong. The fate of all rests upon you.\"", "cyan");
            terminal.WriteLine("");

            await Task.Delay(500);

            terminal.WriteLine("The priest fades back into the temple shadows.", "gray");
            terminal.WriteLine("");

            StoryProgressionSystem.Instance.SetStoryFlag("first_seal_hint", true);

            await terminal.GetInputAsync("Press Enter to continue...");
        }

        /// <summary>
        /// Show warning about a god beginning to stir
        /// </summary>
        private async Task ShowGodStirringWarning(Character player, TerminalEmulator terminal, string godName)
        {
            terminal.WriteLine("");
            terminal.WriteLine("A tremor runs through the ground.", "red");
            terminal.WriteLine("Tankards rattle. Conversations halt.", "white");
            terminal.WriteLine("");

            await Task.Delay(800);

            terminal.WriteLine("A grizzled veteran turns to you, eyes wide.", "gray");
            terminal.WriteLine($"\"Did you feel that? Something stirs below.\"", "yellow");
            terminal.WriteLine($"\"They say it's {godName}. The God of War awakens.\"", "yellow");
            terminal.WriteLine("");

            await Task.Delay(1000);

            terminal.WriteLine("\"The dungeons grow more dangerous each day.\"", "white");
            terminal.WriteLine("\"Whatever you're planning... do it soon.\"", "white");
            terminal.WriteLine("");

            StoryProgressionSystem.Instance.SetStoryFlag($"{godName.ToLower()}_stirring_warning", true);

            await terminal.GetInputAsync("Press Enter to continue...");
        }

        /// <summary>
        /// Force trigger the stranger encounter (for testing or specific story points)
        /// </summary>
        public void ForceStrangerEncounter()
        {
            strangerEncounterPending = true;
        }

        /// <summary>
        /// Check if the opening sequence is complete
        /// </summary>
        public bool IsOpeningComplete()
        {
            return StoryProgressionSystem.Instance.HasStoryFlag("met_mysterious_stranger");
        }

        /// <summary>
        /// Handle daily progression for opening sequence
        /// </summary>
        public void OnDayPassed()
        {
            daysSinceCreation++;

            // After 3 days, increase trigger chance significantly
            if (daysSinceCreation >= 3)
            {
                strangerEncounterPending = true;
            }
        }
    }

    /// <summary>
    /// Cycle/Prestige System - Handles New Game+ mechanics
    /// </summary>
    public class CycleSystem
    {
        private static CycleSystem? _fallbackInstance;
        public static CycleSystem Instance
        {
            get
            {
                var ctx = UsurperRemake.Server.SessionContext.Current;
                if (ctx != null) return ctx.Cycle;
                return _fallbackInstance ??= new CycleSystem();
            }
        }

        /// <summary>
        /// Start a new cycle (New Game+)
        /// </summary>
        public async Task StartNewCycle(Character player, EndingType endingAchieved, TerminalEmulator terminal)
        {
            var story = StoryProgressionSystem.Instance;
            var currentCycle = story.CurrentCycle;

            terminal.Clear();
            terminal.WriteLine("");
            terminal.WriteLine("╔═══════════════════════════════════════════════════════════════════╗", "bright_yellow");
            terminal.WriteLine("║               T H E   E T E R N A L   C Y C L E                   ║", "bright_yellow");
            terminal.WriteLine("╚═══════════════════════════════════════════════════════════════════╝", "bright_yellow");
            terminal.WriteLine("");

            await Task.Delay(1000);

            terminal.WriteLine("The world fades to white...", "white");
            await Task.Delay(1500);

            terminal.WriteLine("And from the void, you hear a familiar voice.", "bright_magenta");
            terminal.WriteLine("");

            await Task.Delay(1000);

            terminal.WriteLine("\"Did you think it would end? It never ends.\"", "bright_magenta");
            terminal.WriteLine("\"The wheel turns. The cycle continues.\"", "bright_magenta");
            terminal.WriteLine("\"But you... you remember. You carry forward.\"", "bright_magenta");
            terminal.WriteLine("");

            await Task.Delay(1500);

            // Calculate bonuses based on ending
            var bonuses = CalculateCycleBonuses(player, endingAchieved, currentCycle);

            terminal.WriteLine($"ENTERING CYCLE {currentCycle + 1}", "bright_cyan");
            terminal.WriteLine("");
            terminal.WriteLine("You will carry forward:", "green");
            terminal.WriteLine($"  +{bonuses.StrengthBonus} Starting Strength", "white");
            terminal.WriteLine($"  +{bonuses.DefenceBonus} Starting Defence", "white");
            terminal.WriteLine($"  +{bonuses.StaminaBonus} Starting Stamina", "white");
            terminal.WriteLine($"  +{bonuses.GoldBonus} Starting Gold", "yellow");
            terminal.WriteLine($"  +{bonuses.ExpMultiplier * 100 - 100}% Experience Gain", "cyan");
            terminal.WriteLine("");

            if (bonuses.KeepsArtifactKnowledge)
            {
                terminal.WriteLine("  * Artifact locations revealed from start", "bright_magenta");
            }
            if (bonuses.StartWithKey)
            {
                terminal.WriteLine("  * Begin with the Ancient Iron Key", "bright_magenta");
            }

            await terminal.GetInputAsync("Press Enter to begin the new cycle...");

            // Reset story with cycle bonuses
            story.StartNewCycle(endingAchieved);

            // Apply bonuses to player
            ApplyCycleBonuses(player, bonuses);

            GD.Print($"[Cycle] Started cycle {currentCycle + 1} with ending {endingAchieved}");
        }

        /// <summary>
        /// Calculate bonuses for the new cycle
        /// </summary>
        private CycleBonuses CalculateCycleBonuses(Character player, EndingType ending, int cycle)
        {
            var bonuses = new CycleBonuses();

            // Base bonuses scale with cycle
            bonuses.StrengthBonus = 5 * cycle;
            bonuses.DefenceBonus = 5 * cycle;
            bonuses.StaminaBonus = 5 * cycle;
            bonuses.GoldBonus = 500 * cycle;
            bonuses.ExpMultiplier = 1.0f + (0.1f * cycle);

            // Ending-specific bonuses
            switch (ending)
            {
                case EndingType.Usurper:
                    // Dark path - more power, less luck
                    bonuses.StrengthBonus += 10;
                    bonuses.DarknessBonus = 100;
                    break;

                case EndingType.Savior:
                    // Light path - balanced bonuses
                    bonuses.ChivalryBonus = 100;
                    bonuses.KeepsArtifactKnowledge = true;
                    break;

                case EndingType.Defiant:
                    // Independent path - unique bonuses
                    bonuses.ExpMultiplier += 0.25f;
                    bonuses.StartWithKey = true;
                    break;

                case EndingType.TrueEnding:
                    // Perfect path - all bonuses
                    bonuses.StrengthBonus += 15;
                    bonuses.DefenceBonus += 15;
                    bonuses.StaminaBonus += 15;
                    bonuses.KeepsArtifactKnowledge = true;
                    bonuses.StartWithKey = true;
                    bonuses.ExpMultiplier += 0.5f;
                    break;
            }

            return bonuses;
        }

        /// <summary>
        /// Apply cycle bonuses to player
        /// </summary>
        private void ApplyCycleBonuses(Character player, CycleBonuses bonuses)
        {
            player.Strength += bonuses.StrengthBonus;
            player.Defence += bonuses.DefenceBonus;
            player.Stamina += bonuses.StaminaBonus;
            player.Gold += bonuses.GoldBonus;
            player.Chivalry += bonuses.ChivalryBonus;
            player.Darkness += bonuses.DarknessBonus;

            // Store exp multiplier in a way combat can use it
            // (Could add ExpMultiplier property to Character)

            if (bonuses.StartWithKey)
            {
                StoryProgressionSystem.Instance.SetStoryFlag("has_ancient_key", true);
            }

            if (bonuses.KeepsArtifactKnowledge)
            {
                StoryProgressionSystem.Instance.SetStoryFlag("knows_artifact_locations", true);
            }
        }

        /// <summary>
        /// Get a list of current cycle bonuses for display purposes
        /// </summary>
        public List<string> GetCurrentCycleBonuses()
        {
            var bonuses = new List<string>();
            var story = StoryProgressionSystem.Instance;
            int cycle = story.CurrentCycle;

            if (cycle <= 1)
                return bonuses; // No bonuses on first cycle

            // Calculate base bonuses
            int strBonus = 5 * (cycle - 1);
            int defBonus = 5 * (cycle - 1);
            int staBonus = 5 * (cycle - 1);
            int goldBonus = 500 * (cycle - 1);
            float expMult = 1.0f + (0.1f * (cycle - 1));

            bonuses.Add($"+{strBonus} Strength from past cycles");
            bonuses.Add($"+{defBonus} Defence from past cycles");
            bonuses.Add($"+{staBonus} Stamina from past cycles");
            bonuses.Add($"+{goldBonus} Starting Gold");
            bonuses.Add($"+{(expMult - 1) * 100:0}% Experience gain");

            // Check for special bonuses from endings
            if (story.HasStoryFlag("keeps_artifact_knowledge") || story.HasStoryFlag("knows_artifact_locations"))
            {
                bonuses.Add("Artifact locations are revealed");
            }
            if (story.HasStoryFlag("has_ancient_key"))
            {
                bonuses.Add("You begin with the Ancient Iron Key");
            }
            if (story.CompletedEndings.Contains(EndingType.TrueEnding))
            {
                bonuses.Add("The Ocean remembers you...");
            }

            return bonuses;
        }

        /// <summary>
        /// Check if player qualifies for true ending
        /// </summary>
        public bool QualifiesForTrueEnding(Character player)
        {
            var story = StoryProgressionSystem.Instance;

            // Must have completed at least 3 cycles (cycles 3, 4, 5... qualify)
            if (story.CurrentCycle < 3) return false;

            // Must have saved at least 2 gods
            int savedGods = 0;
            if (story.HasStoryFlag("veloura_saved")) savedGods++;
            if (story.HasStoryFlag("aurelion_saved")) savedGods++;
            if (story.HasStoryFlag("terravok_awakened")) savedGods++;
            if (savedGods < 2) return false;

            // Must have allied with Noctura
            if (!story.HasStoryFlag("noctura_ally")) return false;

            // Must have collected all Seven Seals
            if (story.CollectedSeals.Count < 7) return false;

            // Must have balanced alignment
            var alignment = player.Chivalry - player.Darkness;
            if (Math.Abs(alignment) > 200) return false;

            return true;
        }
    }
}
