using System;
using System.Threading.Tasks;
using UsurperRemake.Locations;
using UsurperRemake.UI;
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

                if (Random.Shared.NextDouble() < triggerChance)
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
            if (!GameConfig.ScreenReaderMode)
                terminal.WriteLine("═══════════════════════════════════════════════════", "dark_magenta");
            terminal.WriteLine("");

            await Task.Delay(1000);

            terminal.WriteLine(Loc.Get("opening.air_grows_cold"), "gray");
            await Task.Delay(800);

            terminal.WriteLine(Loc.Get("opening.shadows_lengthen"), "dark_gray");
            await Task.Delay(800);

            terminal.WriteLine(Loc.Get("opening.time_pauses"), "white");
            await Task.Delay(1200);

            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("opening.not_alone"), "bright_magenta");
            terminal.WriteLine("");

            await Task.Delay(1500);

            // Run the dialogue
            var dialogue = DialogueSystem.Instance;
            await dialogue.StartDialogue(player, "mysterious_stranger_intro", terminal);

            // After dialogue, set story state
            StoryProgressionSystem.Instance.AdvanceChapter(StoryChapter.TheFirstSeal);

            // Log event
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
            terminal.WriteLine(Loc.Get("opening.priest_approaches"), "cyan");
            terminal.WriteLine("");

            await Task.Delay(500);

            terminal.WriteLine(Loc.Get("opening.priest_mark"), "yellow");
            terminal.WriteLine(Loc.Get("opening.priest_visions"), "yellow");
            terminal.WriteLine("");

            await Task.Delay(800);

            terminal.WriteLine(Loc.Get("opening.priest_first_seal"), "white");
            terminal.WriteLine(Loc.Get("opening.priest_25th_level"), "white");
            terminal.WriteLine(Loc.Get("opening.priest_god_of_war"), "white");
            terminal.WriteLine("");

            await Task.Delay(800);

            terminal.WriteLine(Loc.Get("opening.priest_be_ready"), "cyan");
            terminal.WriteLine("");

            await Task.Delay(500);

            terminal.WriteLine(Loc.Get("opening.priest_fades"), "gray");
            terminal.WriteLine("");

            StoryProgressionSystem.Instance.SetStoryFlag("first_seal_hint", true);

            await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
        }

        /// <summary>
        /// Show warning about a god beginning to stir
        /// </summary>
        private async Task ShowGodStirringWarning(Character player, TerminalEmulator terminal, string godName)
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("opening.tremor"), "red");
            terminal.WriteLine(Loc.Get("opening.tankards_rattle"), "white");
            terminal.WriteLine("");

            await Task.Delay(800);

            terminal.WriteLine(Loc.Get("opening.veteran_turns"), "gray");
            terminal.WriteLine(Loc.Get("opening.veteran_feel_that"), "yellow");
            terminal.WriteLine(Loc.Get("opening.veteran_god_awakens", godName), "yellow");
            terminal.WriteLine("");

            await Task.Delay(1000);

            terminal.WriteLine(Loc.Get("opening.veteran_dangerous"), "white");
            terminal.WriteLine(Loc.Get("opening.veteran_do_it_soon"), "white");
            terminal.WriteLine("");

            StoryProgressionSystem.Instance.SetStoryFlag($"{godName.ToLower()}_stirring_warning", true);

            await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
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
            UIHelper.WriteBoxHeader(terminal, Loc.Get("opening.eternal_cycle"), "bright_yellow", 67);
            terminal.WriteLine("");

            await Task.Delay(1000);

            terminal.WriteLine(Loc.Get("opening.world_fades"), "white");
            await Task.Delay(1500);

            terminal.WriteLine(Loc.Get("opening.familiar_voice"), "bright_magenta");
            terminal.WriteLine("");

            await Task.Delay(1000);

            terminal.WriteLine(Loc.Get("opening.cycle_never_ends"), "bright_magenta");
            terminal.WriteLine(Loc.Get("opening.wheel_turns"), "bright_magenta");
            terminal.WriteLine(Loc.Get("opening.you_remember"), "bright_magenta");
            terminal.WriteLine("");

            await Task.Delay(1500);

            // Calculate bonuses based on ending
            var bonuses = CalculateCycleBonuses(player, endingAchieved, currentCycle);

            terminal.WriteLine(Loc.Get("opening.entering_cycle", currentCycle + 1), "bright_cyan");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("opening.carry_forward"), "green");
            terminal.WriteLine(Loc.Get("opening.bonus_strength", bonuses.StrengthBonus), "white");
            terminal.WriteLine(Loc.Get("opening.bonus_defence", bonuses.DefenceBonus), "white");
            terminal.WriteLine(Loc.Get("opening.bonus_stamina", bonuses.StaminaBonus), "white");
            terminal.WriteLine(Loc.Get("opening.bonus_gold", bonuses.GoldBonus), "yellow");
            terminal.WriteLine(Loc.Get("opening.bonus_exp", bonuses.ExpMultiplier * 100 - 100), "cyan");
            terminal.WriteLine("");

            if (bonuses.KeepsArtifactKnowledge)
            {
                terminal.WriteLine($"  * {Loc.Get("opening.bonus_artifacts")}", "bright_magenta");
            }
            if (bonuses.StartWithKey)
            {
                terminal.WriteLine($"  * {Loc.Get("opening.bonus_key")}", "bright_magenta");
            }

            await terminal.GetInputAsync(Loc.Get("opening.press_enter_new_cycle"));

            // Reset story with cycle bonuses
            story.StartNewCycle(endingAchieved);

            // Apply bonuses to player
            ApplyCycleBonuses(player, bonuses);

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

            // Store exp multiplier on character so CombatEngine can apply it
            player.CycleExpMultiplier = bonuses.ExpMultiplier;

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
        /// Apply cycle bonuses to a fresh NG+ character (called from CreateNewGame)
        /// </summary>
        public void ApplyCycleBonusesToNewCharacter(Character player, int cycle, EndingType lastEnding)
        {
            // cycle is already incremented (e.g., 2 for first NG+), use cycle-1 for bonus calculation
            var bonuses = CalculateCycleBonuses(player, lastEnding, cycle - 1);
            ApplyCycleBonuses(player, bonuses);

            // Apply starting level bonus from MetaProgressionSystem
            int startingLevel = MetaProgressionSystem.Instance.GetStartingLevelBonus();
            if (startingLevel > 1)
            {
                for (int i = 1; i < startingLevel; i++)
                {
                    player.Level++;
                    LevelMasterLocation.ApplyClassStatIncreases(player);
                    player.TrainingPoints += TrainingSystem.CalculateTrainingPointsPerLevel(player);
                }
                // Set XP to match the new level so they don't instantly level again
                player.Experience = LevelMasterLocation.GetExperienceForLevel(startingLevel);
                player.RecalculateStats();
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

            bonuses.Add(Loc.Get("opening.cycle_str_bonus", strBonus));
            bonuses.Add(Loc.Get("opening.cycle_def_bonus", defBonus));
            bonuses.Add(Loc.Get("opening.cycle_sta_bonus", staBonus));
            bonuses.Add(Loc.Get("opening.cycle_gold_bonus", goldBonus));
            bonuses.Add(Loc.Get("opening.cycle_exp_bonus", $"{(expMult - 1) * 100:0}"));

            // Starting level bonus from meta-progression
            int startingLevel = MetaProgressionSystem.Instance.GetStartingLevelBonus();
            if (startingLevel > 1)
            {
                bonuses.Add(Loc.Get("opening.cycle_level_bonus", startingLevel));
            }

            // Check for special bonuses from endings
            if (story.HasStoryFlag("keeps_artifact_knowledge") || story.HasStoryFlag("knows_artifact_locations"))
            {
                bonuses.Add(Loc.Get("opening.cycle_artifacts_revealed"));
            }
            if (story.HasStoryFlag("has_ancient_key"))
            {
                bonuses.Add(Loc.Get("opening.cycle_ancient_key"));
            }
            if (story.CompletedEndings.Contains(EndingType.TrueEnding))
            {
                bonuses.Add(Loc.Get("opening.cycle_ocean_remembers"));
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
