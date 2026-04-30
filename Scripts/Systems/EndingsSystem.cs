using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UsurperRemake.UI;
using UsurperRemake.Utils;
using UsurperRemake.Server;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Endings System - Handles the three main endings plus the secret true ending
    /// Manages credits, epilogues, and transition to New Game+
    /// </summary>
    public class EndingsSystem
    {
        private static EndingsSystem? instance;
        public static EndingsSystem Instance => instance ??= new EndingsSystem();

        public event Action<EndingType>? OnEndingTriggered;
        public event Action? OnCreditsComplete;

        /// <summary>
        /// Determine which ending the player qualifies for
        /// </summary>
        public EndingType DetermineEnding(Character player)
        {
            // Null check for player
            if (player == null)
            {
                return EndingType.Defiant; // Default fallback
            }

            var story = StoryProgressionSystem.Instance;
            var ocean = OceanPhilosophySystem.Instance;
            var amnesia = AmnesiaSystem.Instance;
            var companions = CompanionSystem.Instance;
            var grief = GriefSystem.Instance;

            // Check for Secret Ending (Dissolution) first - requires Cycle 3+
            if (story?.CurrentCycle >= 3 && QualifiesForDissolutionEnding(player))
            {
                return EndingType.Secret;
            }

            // Check for Enhanced True Ending
            if (QualifiesForEnhancedTrueEnding(player))
            {
                return EndingType.TrueEnding;
            }

            // Fallback to legacy true ending check
            if (CycleSystem.Instance?.QualifiesForTrueEnding(player) == true)
            {
                return EndingType.TrueEnding;
            }

            // Calculate alignment
            long alignment = player.Chivalry - player.Darkness;

            // Count saved vs destroyed gods (all 6 Old Gods)
            int savedGods = 0;
            int destroyedGods = 0;

            // Veloura - Goddess of Illusions
            if (story.HasStoryFlag("veloura_saved")) savedGods++;
            if (story.HasStoryFlag("veloura_destroyed")) destroyedGods++;
            // Aurelion - God of Light
            if (story.HasStoryFlag("aurelion_saved")) savedGods++;
            if (story.HasStoryFlag("aurelion_destroyed")) destroyedGods++;
            // Terravok - God of Earth
            if (story.HasStoryFlag("terravok_awakened")) savedGods++;
            if (story.HasStoryFlag("terravok_destroyed")) destroyedGods++;
            // Noctura - Goddess of Night
            if (story.HasStoryFlag("noctura_ally")) savedGods++;
            if (story.HasStoryFlag("noctura_destroyed")) destroyedGods++;
            // Maelketh - God of Chaos
            if (story.HasStoryFlag("maelketh_saved")) savedGods++;
            if (story.HasStoryFlag("maelketh_destroyed")) destroyedGods++;
            // Thorgrim - God of War
            if (story.HasStoryFlag("thorgrim_saved")) savedGods++;
            if (story.HasStoryFlag("thorgrim_destroyed")) destroyedGods++;

            // Determine ending based on choices
            if (alignment < -300 || destroyedGods >= 5)
            {
                return EndingType.Usurper; // Dark path - take Manwe's place
            }
            else if (alignment > 300 || savedGods >= 3)
            {
                return EndingType.Savior; // Light path - redeem the gods
            }
            else
            {
                return EndingType.Defiant; // Independent path - reject all gods
            }
        }

        /// <summary>
        /// Check if player qualifies for the enhanced True Ending
        /// Requirements:
        /// 1. All 7 seals collected
        /// 2. Awakening Level 7 (full Ocean Philosophy understanding)
        /// 3. At least one companion died (experienced loss)
        /// 4. Spared at least 2 gods
        /// 5. Net alignment near zero (balance)
        /// 6. Completed personal quest of deceased companion (optional bonus)
        /// </summary>
        private bool QualifiesForEnhancedTrueEnding(Character player)
        {
            if (player == null)
                return false;

            // Blood Price gate — murderers cannot achieve the True Ending
            if (player.MurderWeight >= GameConfig.MurderWeightEndingBlock)
                return false;

            var story = StoryProgressionSystem.Instance;
            var ocean = OceanPhilosophySystem.Instance;
            var companions = CompanionSystem.Instance;
            var grief = GriefSystem.Instance;

            // 1. All 7 seals collected
            if (story?.CollectedSeals == null || story.CollectedSeals.Count < 7)
                return false;

            // 2. Awakening Level 7
            if (ocean?.AwakeningLevel < 7)
                return false;

            // 3. Experienced companion loss
            if (ocean?.ExperiencedMoments?.Contains(AwakeningMoment.FirstCompanionDeath) != true &&
                grief?.HasCompletedGriefCycle != true)
                return false;

            // 4. Spared at least 2 gods
            int sparedGods = 0;
            if (story.HasStoryFlag("veloura_saved")) sparedGods++;
            if (story.HasStoryFlag("aurelion_saved")) sparedGods++;
            if (story.HasStoryFlag("noctura_ally")) sparedGods++;
            if (story.HasStoryFlag("terravok_awakened")) sparedGods++;
            if (sparedGods < 2)
                return false;

            // 5. Alignment near zero (within +/- 500)
            long alignment = player.Chivalry - player.Darkness;
            if (Math.Abs(alignment) > 500)
                return false;

            return true;
        }

        /// <summary>
        /// Check if player qualifies for the secret Dissolution ending
        /// The ultimate ending - dissolving back into the Ocean
        /// </summary>
        private bool QualifiesForDissolutionEnding(Character player)
        {
            if (player == null)
                return false;

            // Blood Price gate — murderers cannot achieve the Dissolution Ending
            if (player.MurderWeight >= GameConfig.MurderWeightEndingBlock)
                return false;

            var story = StoryProgressionSystem.Instance;
            var ocean = OceanPhilosophySystem.Instance;
            var amnesia = AmnesiaSystem.Instance;

            // Must have completed at least 2 other endings
            if (story?.CompletedEndings == null || story.CompletedEndings.Count < 2)
                return false;

            // Must have max awakening
            if (ocean?.AwakeningLevel < 7)
                return false;

            // Must have full memory recovery (know you are Fragment of Manwe)
            if (amnesia?.TruthRevealed != true)
                return false;

            // Must have all wave fragments
            if (ocean?.CollectedFragments == null || ocean.CollectedFragments.Count < 7)
                return false;

            // Auto-set the ready_for_dissolution flag when all conditions are met
            // This ensures the ending is reachable once the player has completed the journey
            if (!story.HasStoryFlag("ready_for_dissolution"))
            {
                story.SetStoryFlag("ready_for_dissolution", true);
            }

            return true;
        }

        /// <summary>
        /// Trigger an ending sequence
        /// </summary>
        public async Task TriggerEnding(Character player, EndingType ending, TerminalEmulator terminal)
        {
            OnEndingTriggered?.Invoke(ending);

            // Phase 7: emit a graphical end card up front so the JS overlay shows
            // immediately. The per-ending text below still flows through (acceptable
            // bleed for a one-shot terminal narrative — the overlay sits on top and
            // self-dismisses on the final NG+/ascension prompt).
            if (GameConfig.ElectronMode)
            {
                ElectronBridge.EmitEnding(BuildEndingScreenData(player, ending));
            }

            switch (ending)
            {
                case EndingType.Usurper:
                    await PlayUsurperEnding(player, terminal);
                    break;
                case EndingType.Savior:
                    await PlaySaviorEnding(player, terminal);
                    break;
                case EndingType.Defiant:
                    await PlayDefiantEnding(player, terminal);
                    break;
                case EndingType.TrueEnding:
                    await PlayEnhancedTrueEnding(player, terminal);
                    break;
                case EndingType.Secret:
                    await PlayDissolutionEnding(player, terminal);
                    // Record in meta-progression before returning (save is deleted, but meta tracks it)
                    MetaProgressionSystem.Instance.RecordEndingUnlock(ending, player);
                    return; // Dissolution ending doesn't lead to NG+ - save deleted
            }

            // Record ending in story
            StoryProgressionSystem.Instance.RecordChoice("final_ending", ending.ToString(), 0);
            StoryProgressionSystem.Instance.SetStoryFlag($"ending_{ending.ToString().ToLower()}_achieved", true);

            // Play credits
            await PlayCredits(player, ending, terminal);

            // Offer Immortal Ascension or New Game+ — loop until the player picks one
            while (true)
            {
                bool ascended = await OfferImmortality(player, ending, terminal);
                if (ascended) return; // Player became a god

                bool startedNgPlus = await OfferNewGamePlus(player, ending, terminal);
                if (startedNgPlus) return; // Player started NG+

                // Player declined both — loop back and offer again
                terminal.WriteLine("");
                terminal.WriteLine("  You must choose: ascend to godhood or begin the cycle anew.", "bright_yellow");
                terminal.WriteLine("  There is no going back to the mortal world after defeating Manwe.", "gray");
                terminal.WriteLine("");
                await terminal.GetInputAsync("  Press Enter to choose again...");
            }
        }

        #region Ending Sequences

        private async Task PlayUsurperEnding(Character player, TerminalEmulator terminal)
        {
            terminal.Clear();
            await Task.Delay(1000);

            terminal.WriteLine("");
            UIHelper.WriteBoxHeader(terminal, Loc.Get("ending.usurper_header"), "dark_red", 67);
            terminal.WriteLine("");

            await Task.Delay(2000);

            var lines = new[]
            {
                (Loc.Get("ending.usurper_line_1"), "white"),
                (Loc.Get("ending.usurper_line_2"), "white"),
                (Loc.Get("ending.usurper_line_3"), "dark_red"),
                ("", "white"),
                (Loc.Get("ending.usurper_line_4"), "yellow"),
                (Loc.Get("ending.usurper_line_5"), "yellow"),
                (Loc.Get("ending.usurper_line_6"), "yellow"),
                ("", "white"),
                (Loc.Get("ending.usurper_line_7"), "white"),
                (Loc.Get("ending.usurper_line_8"), "dark_red"),
                (Loc.Get("ending.usurper_line_9"), "dark_red"),
                ("", "white"),
                (Loc.Get("ending.usurper_line_10"), "white"),
                (Loc.Get("ending.usurper_line_11"), "white"),
                (Loc.Get("ending.usurper_line_12"), "white"),
                ("", "white"),
                (Loc.Get("ending.usurper_line_13"), "gray"),
                ("", "white"),
                (Loc.Get("ending.usurper_line_14"), "gray"),
                (Loc.Get("ending.usurper_line_15"), "gray"),
                (Loc.Get("ending.usurper_line_16"), "gray"),
                (Loc.Get("ending.usurper_line_17"), "gray"),
                ("", "white"),
                (Loc.Get("ending.usurper_line_18"), "gray"),
                (Loc.Get("ending.usurper_line_19"), "gray"),
                ("", "white"),
                (Loc.Get("ending.usurper_line_20"), "white"),
                (Loc.Get("ending.usurper_line_21"), "dark_red"),
                (Loc.Get("ending.usurper_line_22"), "dark_red")
            };

            foreach (var (line, color) in lines)
            {
                terminal.WriteLine($"  {line}", color);
                await Task.Delay(200);
            }

            terminal.WriteLine("");
            terminal.WriteLine($"  {Loc.Get("ending.usurper_the_end")}", "dark_red");
            terminal.WriteLine($"  {Loc.Get("ending.usurper_subtitle")}", "gray");
            terminal.WriteLine("");

            await terminal.GetInputAsync(Loc.Get("ending.press_enter"));
        }

        private async Task PlaySaviorEnding(Character player, TerminalEmulator terminal)
        {
            terminal.Clear();
            await Task.Delay(1000);

            terminal.WriteLine("");
            UIHelper.WriteBoxHeader(terminal, Loc.Get("ending.savior_header"), "bright_green", 67);
            terminal.WriteLine("");

            await Task.Delay(2000);

            var lines = new[]
            {
                (Loc.Get("ending.savior_line_1"), "white"),
                (Loc.Get("ending.savior_line_2"), "white"),
                ("", "white"),
                (Loc.Get("ending.savior_line_3"), "bright_green"),
                ("", "white"),
                (Loc.Get("ending.savior_line_4"), "cyan"),
                (Loc.Get("ending.savior_line_5"), "cyan"),
                (Loc.Get("ending.savior_line_6"), "cyan"),
                ("", "white"),
                (Loc.Get("ending.savior_line_7"), "bright_yellow"),
                (Loc.Get("ending.savior_line_8"), "bright_yellow"),
                (Loc.Get("ending.savior_line_9"), "yellow"),
                ("", "white"),
                (Loc.Get("ending.savior_line_10"), "cyan"),
                ("", "white"),
                (Loc.Get("ending.savior_line_11"), "bright_magenta"),
                (Loc.Get("ending.savior_line_12"), "bright_magenta"),
                (Loc.Get("ending.savior_line_13"), "bright_magenta"),
                (Loc.Get("ending.savior_line_14"), "bright_magenta"),
                ("", "white"),
                (Loc.Get("ending.savior_line_15"), "bright_green"),
                (Loc.Get("ending.savior_line_16"), "bright_green"),
                (Loc.Get("ending.savior_line_17"), "bright_green"),
                ("", "white"),
                (Loc.Get("ending.savior_line_18"), "bright_green"),
                ("", "white"),
                (Loc.Get("ending.savior_line_19"), "white"),
                (Loc.Get("ending.savior_line_20"), "white"),
                (Loc.Get("ending.savior_line_21"), "white"),
                ("", "white"),
                (Loc.Get("ending.savior_line_22"), "bright_cyan"),
                (Loc.Get("ending.savior_line_23"), "bright_cyan"),
                (Loc.Get("ending.savior_line_24"), "bright_cyan")
            };

            foreach (var (line, color) in lines)
            {
                terminal.WriteLine($"  {line}", color);
                await Task.Delay(200);
            }

            terminal.WriteLine("");
            terminal.WriteLine($"  {Loc.Get("ending.savior_the_end")}", "bright_green");
            terminal.WriteLine($"  {Loc.Get("ending.savior_subtitle")}", "gray");
            terminal.WriteLine("");

            await terminal.GetInputAsync(Loc.Get("ending.press_enter"));
        }

        private async Task PlayDefiantEnding(Character player, TerminalEmulator terminal)
        {
            terminal.Clear();
            await Task.Delay(1000);

            terminal.WriteLine("");
            UIHelper.WriteBoxHeader(terminal, Loc.Get("ending.defiant_header"), "bright_yellow", 67);
            terminal.WriteLine("");

            await Task.Delay(2000);

            var lines = new[]
            {
                (Loc.Get("ending.defiant_line_1"), "cyan"),
                ("", "white"),
                (Loc.Get("ending.defiant_line_2"), "yellow"),
                ("", "white"),
                (Loc.Get("ending.defiant_line_3"), "cyan"),
                (Loc.Get("ending.defiant_line_4"), "cyan"),
                (Loc.Get("ending.defiant_line_5"), "cyan"),
                ("", "white"),
                (Loc.Get("ending.defiant_line_6"), "yellow"),
                (Loc.Get("ending.defiant_line_7"), "yellow"),
                ("", "white"),
                (Loc.Get("ending.defiant_line_8"), "cyan"),
                (Loc.Get("ending.defiant_line_9"), "cyan"),
                ("", "white"),
                (Loc.Get("ending.defiant_line_10"), "bright_red"),
                (Loc.Get("ending.defiant_line_11"), "bright_red"),
                (Loc.Get("ending.defiant_line_12"), "bright_yellow"),
                ("", "white"),
                (Loc.Get("ending.defiant_line_13"), "white"),
                (Loc.Get("ending.defiant_line_14"), "white"),
                (Loc.Get("ending.defiant_line_15"), "white"),
                (Loc.Get("ending.defiant_line_16"), "white"),
                ("", "white"),
                (Loc.Get("ending.defiant_line_17"), "gray"),
                (Loc.Get("ending.defiant_line_18"), "gray"),
                (Loc.Get("ending.defiant_line_19"), "gray"),
                (Loc.Get("ending.defiant_line_20"), "gray"),
                ("", "white"),
                (Loc.Get("ending.defiant_line_21"), "bright_yellow"),
                (Loc.Get("ending.defiant_line_22"), "white"),
                (Loc.Get("ending.defiant_line_23"), "white"),
                ("", "white"),
                (Loc.Get("ending.defiant_line_24"), "bright_yellow"),
                (Loc.Get("ending.defiant_line_25"), "bright_yellow")
            };

            foreach (var (line, color) in lines)
            {
                terminal.WriteLine($"  {line}", color);
                await Task.Delay(200);
            }

            terminal.WriteLine("");
            terminal.WriteLine($"  {Loc.Get("ending.defiant_the_end")}", "bright_yellow");
            terminal.WriteLine($"  {Loc.Get("ending.defiant_subtitle")}", "gray");
            terminal.WriteLine("");

            await terminal.GetInputAsync(Loc.Get("ending.press_enter"));
        }

        private async Task PlayTrueEnding(Character player, TerminalEmulator terminal)
        {
            terminal.Clear();
            await Task.Delay(1000);

            terminal.WriteLine("");
            if (!GameConfig.ScreenReaderMode)
            {
                terminal.WriteLine("╔═══════════════════════════════════════════════════════════════════╗", "bright_magenta");
                terminal.WriteLine("║                   T H E   T R U E   E N D I N G                   ║", "bright_magenta");
                terminal.WriteLine("║                      Seeker of Balance                            ║", "bright_magenta");
                terminal.WriteLine("╚═══════════════════════════════════════════════════════════════════╝", "bright_magenta");
            }
            else
            {
                terminal.WriteLine(Loc.Get("ending.true_sr_title"), "bright_magenta");
            }
            terminal.WriteLine("");

            await Task.Delay(2000);

            var lines = new[]
            {
                (Loc.Get("ending.true_line_1"), "bright_cyan"),
                (Loc.Get("ending.true_line_2"), "bright_cyan"),
                (Loc.Get("ending.true_line_3"), "bright_cyan"),
                (Loc.Get("ending.true_line_4"), "bright_cyan"),
                ("", "white"),
                (Loc.Get("ending.true_line_5"), "bright_yellow"),
                (Loc.Get("ending.true_line_6"), "yellow"),
                (Loc.Get("ending.true_line_7"), "yellow"),
                ("", "white"),
                (Loc.Get("ending.true_line_8"), "cyan"),
                ("", "white"),
                (Loc.Get("ending.true_line_9"), "yellow"),
                (Loc.Get("ending.true_line_10"), "yellow"),
                (Loc.Get("ending.true_line_11"), "yellow"),
                ("", "white"),
                (Loc.Get("ending.true_line_12"), "white"),
                (Loc.Get("ending.true_line_13"), "white"),
                (Loc.Get("ending.true_line_14"), "white"),
                ("", "white"),
                (Loc.Get("ending.true_line_15"), "cyan"),
                (Loc.Get("ending.true_line_16"), "cyan"),
                ("", "white"),
                (Loc.Get("ending.true_line_17"), "yellow"),
                ("", "white"),
                (Loc.Get("ending.true_line_18"), "bright_magenta"),
                ("", "white"),
                (Loc.Get("ending.true_line_19"), "bright_magenta"),
                (Loc.Get("ending.true_line_20"), "bright_magenta"),
                (Loc.Get("ending.true_line_21"), "bright_magenta"),
                ("", "white"),
                (Loc.Get("ending.true_line_22"), "bright_cyan"),
                (Loc.Get("ending.true_line_23"), "bright_cyan"),
                ("", "white"),
                (Loc.Get("ending.true_line_24"), "bright_magenta"),
                (Loc.Get("ending.true_line_25"), "bright_magenta")
            };

            foreach (var (line, color) in lines)
            {
                terminal.WriteLine($"  {line}", color);
                await Task.Delay(200);
            }

            terminal.WriteLine("");
            terminal.WriteLine($"  {Loc.Get("ending.true_the_end")}", "bright_magenta");
            terminal.WriteLine($"  {Loc.Get("ending.true_subtitle")}", "gray");
            terminal.WriteLine("");

            await terminal.GetInputAsync(Loc.Get("ending.press_enter"));
        }

        /// <summary>
        /// Enhanced True Ending with Ocean Philosophy integration
        /// Includes the revelation that player is a fragment of Manwe
        /// </summary>
        private async Task PlayEnhancedTrueEnding(Character player, TerminalEmulator terminal)
        {
            terminal.Clear();
            await Task.Delay(1000);

            terminal.WriteLine("");
            if (!GameConfig.ScreenReaderMode)
            {
                terminal.WriteLine("╔═══════════════════════════════════════════════════════════════════╗", "bright_cyan");
                terminal.WriteLine("║            T H E   T R U E   A W A K E N I N G                    ║", "bright_cyan");
                terminal.WriteLine("║           \"You are the Ocean, dreaming of being a wave\"           ║", "bright_cyan");
                terminal.WriteLine("╚═══════════════════════════════════════════════════════════════════╝", "bright_cyan");
            }
            else
            {
                terminal.WriteLine(Loc.Get("ending.awakening_sr_title_1"), "bright_cyan");
                terminal.WriteLine(Loc.Get("ending.awakening_sr_title_2"), "bright_cyan");
            }
            terminal.WriteLine("");

            await Task.Delay(2000);

            var lines = new[]
            {
                (Loc.Get("ending.awakening_line_1"), "white"),
                (Loc.Get("ending.awakening_line_2"), "white"),
                (Loc.Get("ending.awakening_line_3"), "bright_yellow"),
                ("", "white"),
                (Loc.Get("ending.awakening_line_4"), "yellow"),
                (Loc.Get("ending.awakening_line_5"), "yellow"),
                ("", "white"),
                (Loc.Get("ending.awakening_line_6"), "bright_cyan"),
                ("", "white"),
                (Loc.Get("ending.awakening_line_7"), "bright_cyan"),
                (Loc.Get("ending.awakening_line_8"), "bright_cyan"),
                (Loc.Get("ending.awakening_line_9"), "bright_cyan"),
                (Loc.Get("ending.awakening_line_10"), "bright_cyan"),
                (Loc.Get("ending.awakening_line_11"), "bright_cyan"),
                ("", "white"),
                (Loc.Get("ending.awakening_line_12"), "yellow"),
                (Loc.Get("ending.awakening_line_13"), "yellow"),
                (Loc.Get("ending.awakening_line_14"), "yellow"),
                (Loc.Get("ending.awakening_line_15"), "yellow"),
                ("", "white"),
                (Loc.Get("ending.awakening_line_16"), "cyan"),
                (Loc.Get("ending.awakening_line_17"), "cyan"),
                ("", "white"),
                (Loc.Get("ending.awakening_line_18"), "yellow"),
                ("", "white"),
                (Loc.Get("ending.awakening_line_19"), "bright_magenta"),
                (Loc.Get("ending.awakening_line_20"), "bright_magenta"),
                (Loc.Get("ending.awakening_line_21"), "bright_magenta"),
                ("", "white"),
                (Loc.Get("ending.awakening_line_22"), "bright_cyan"),
                (Loc.Get("ending.awakening_line_23"), "bright_cyan"),
                ("", "white"),
                (Loc.Get("ending.awakening_line_24"), "yellow"),
                (Loc.Get("ending.awakening_line_25"), "yellow"),
                (Loc.Get("ending.awakening_line_26"), "yellow"),
                ("", "white"),
                (Loc.Get("ending.awakening_line_27"), "bright_magenta"),
                ("", "white"),
                (Loc.Get("ending.awakening_line_28"), "bright_white"),
                (Loc.Get("ending.awakening_line_29"), "bright_white"),
                (Loc.Get("ending.awakening_line_30"), "bright_white"),
                (Loc.Get("ending.awakening_line_31"), "bright_white"),
                ("", "white"),
                (Loc.Get("ending.awakening_line_32"), "bright_cyan"),
                (Loc.Get("ending.awakening_line_33"), "bright_cyan"),
                (Loc.Get("ending.awakening_line_34"), "bright_cyan"),
                (Loc.Get("ending.awakening_line_35"), "bright_cyan"),
                ("", "white"),
                (Loc.Get("ending.awakening_line_36"), "bright_magenta"),
                (Loc.Get("ending.awakening_line_37"), "bright_magenta"),
                (Loc.Get("ending.awakening_line_38"), "bright_magenta"),
                ("", "white"),
                (Loc.Get("ending.awakening_line_39"), "bright_yellow"),
                (Loc.Get("ending.awakening_line_40"), "bright_yellow")
            };

            foreach (var (line, color) in lines)
            {
                terminal.WriteLine($"  {line}", color);
                await Task.Delay(150);
            }

            terminal.WriteLine("");
            terminal.WriteLine($"  {Loc.Get("ending.awakening_the_end")}", "bright_cyan");
            terminal.WriteLine($"  {Loc.Get("ending.awakening_subtitle")}", "gray");
            terminal.WriteLine("");

            // Mark Ocean Philosophy complete
            OceanPhilosophySystem.Instance.ExperienceMoment(AwakeningMoment.TrueIdentityRevealed);

            // Moment of silence — the wave remembers
            await UIHelper.MomentOfSilence(terminal, 6000);

            await terminal.GetInputAsync(Loc.Get("ending.press_enter"));
        }

        /// <summary>
        /// Secret Dissolution Ending - available only after Cycle 3+
        /// The ultimate ending: true enlightenment, save deleted
        /// </summary>
        private async Task PlayDissolutionEnding(Character player, TerminalEmulator terminal)
        {
            terminal.Clear();
            await Task.Delay(2000);

            terminal.WriteLine("");
            if (!GameConfig.ScreenReaderMode)
            {
                terminal.WriteLine("╔═══════════════════════════════════════════════════════════════════╗", "white");
                terminal.WriteLine("║                     D I S S O L U T I O N                         ║", "white");
                terminal.WriteLine("║              \"No more cycles. No more grasping.\"                  ║", "white");
                terminal.WriteLine("╚═══════════════════════════════════════════════════════════════════╝", "white");
            }
            else
            {
                terminal.WriteLine(Loc.Get("ending.dissolution_sr_title_1"), "white");
                terminal.WriteLine(Loc.Get("ending.dissolution_sr_title_2"), "white");
            }
            terminal.WriteLine("");

            await Task.Delay(2000);

            var lines = new[]
            {
                (Loc.Get("ending.dissolution_line_1"), "gray"),
                (Loc.Get("ending.dissolution_line_2"), "gray"),
                (Loc.Get("ending.dissolution_line_3"), "gray"),
                ("", "white"),
                (Loc.Get("ending.dissolution_line_4"), "white"),
                ("", "white"),
                (Loc.Get("ending.dissolution_line_5"), "bright_cyan"),
                (Loc.Get("ending.dissolution_line_6"), "bright_cyan"),
                (Loc.Get("ending.dissolution_line_7"), "bright_cyan"),
                ("", "white"),
                (Loc.Get("ending.dissolution_line_8"), "yellow"),
                (Loc.Get("ending.dissolution_line_9"), "yellow"),
                (Loc.Get("ending.dissolution_line_10"), "yellow"),
                ("", "white"),
                (Loc.Get("ending.dissolution_line_11"), "bright_white"),
                ("", "white"),
                (Loc.Get("ending.dissolution_line_12"), "yellow"),
                ("", "white"),
                (Loc.Get("ending.dissolution_line_13"), "bright_white"),
                (Loc.Get("ending.dissolution_line_14"), "bright_white"),
                (Loc.Get("ending.dissolution_line_15"), "bright_white"),
                ("", "white"),
                (Loc.Get("ending.dissolution_line_16"), "yellow"),
                (Loc.Get("ending.dissolution_line_17"), "yellow"),
                (Loc.Get("ending.dissolution_line_18"), "yellow"),
                (Loc.Get("ending.dissolution_line_19"), "yellow"),
                (Loc.Get("ending.dissolution_line_20"), "yellow"),
                (Loc.Get("ending.dissolution_line_21"), "yellow"),
                ("", "white"),
                (Loc.Get("ending.dissolution_line_22"), "white"),
                ("", "white"),
                (Loc.Get("ending.dissolution_line_23"), "bright_white"),
                (Loc.Get("ending.dissolution_line_24"), "bright_white"),
                (Loc.Get("ending.dissolution_line_25"), "bright_white"),
                ("", "white"),
                (Loc.Get("ending.dissolution_line_26"), "white"),
                (Loc.Get("ending.dissolution_line_27"), "white"),
                (Loc.Get("ending.dissolution_line_28"), "white"),
                ("", "white"),
                (Loc.Get("ending.dissolution_line_29"), "bright_cyan"),
                (Loc.Get("ending.dissolution_line_30"), "bright_cyan"),
                ("", "white"),
                (Loc.Get("ending.dissolution_line_31"), "gray")
            };

            foreach (var (line, color) in lines)
            {
                terminal.WriteLine($"  {line}", color);
                await Task.Delay(200);
            }

            terminal.WriteLine("");
            terminal.WriteLine($"  {Loc.Get("ending.dissolution_dots")}", "gray");
            terminal.WriteLine("");

            await Task.Delay(3000);

            terminal.Clear();
            terminal.WriteLine("");
            terminal.WriteLine("");
            terminal.WriteLine("", "white");
            terminal.WriteLine("", "white");
            terminal.WriteLine($"  {Loc.Get("ending.dissolution_save_delete_warn")}", "dark_red");
            terminal.WriteLine($"  {Loc.Get("ending.dissolution_cannot_undo")}", "dark_red");
            terminal.WriteLine("");
            terminal.WriteLine($"  {Loc.Get("ending.dissolution_enlightenment")}", "bright_yellow");
            terminal.WriteLine($"  {Loc.Get("ending.dissolution_letting_go")}", "bright_yellow");
            terminal.WriteLine("");

            var confirm = await terminal.GetInputAsync(Loc.Get("ending.dissolution_confirm"));

            if (confirm.Trim().ToUpper() == "DISSOLVE")
            {
                terminal.WriteLine("");
                terminal.WriteLine($"  {Loc.Get("ending.dissolution_farewell_1")}", "bright_cyan");
                terminal.WriteLine($"  {Loc.Get("ending.dissolution_farewell_2")}", "bright_cyan");
                terminal.WriteLine("");

                // Delete the player's save file - this character's journey is complete
                string playerName = !string.IsNullOrEmpty(player.Name1) ? player.Name1 : player.Name2;
                SaveSystem.Instance.DeleteSave(playerName);

                await Task.Delay(3000);

                // Moment of silence — complete dissolution
                await UIHelper.MomentOfSilence(terminal, 8000);

                terminal.Clear();
                terminal.WriteLine("");
                terminal.WriteLine($"  {Loc.Get("ending.dissolution_the_end")}", "white");
                terminal.WriteLine("");
                terminal.WriteLine($"  {Loc.Get("ending.dissolution_story_finished")}", "gray");
                terminal.WriteLine($"  {Loc.Get("ending.dissolution_save_deleted")}", "gray");
                terminal.WriteLine("");
            }
            else
            {
                terminal.WriteLine("");
                terminal.WriteLine($"  {Loc.Get("ending.dissolution_not_ready_1")}", "yellow");
                terminal.WriteLine($"  {Loc.Get("ending.dissolution_not_ready_2")}", "yellow");
                terminal.WriteLine("");

                // Revert to standard True Ending
                await PlayEnhancedTrueEnding(player, terminal);
            }

            await terminal.GetInputAsync(Loc.Get("ending.dissolution_press_enter"));
        }

        #endregion

        #region Credits

        private async Task PlayCredits(Character player, EndingType ending, TerminalEmulator terminal)
        {
            terminal.Clear();
            await Task.Delay(2000);

            terminal.WriteLine("");
            if (!GameConfig.ScreenReaderMode)
                terminal.WriteLine("═══════════════════════════════════════════════════════════════════", "bright_cyan");
            terminal.WriteLine("");
            terminal.WriteLine($"                        {Loc.Get("ending.credits_title")}", "bright_yellow");
            terminal.WriteLine($"                          {Loc.Get("ending.credits_subtitle")}", "yellow");
            terminal.WriteLine("");
            if (!GameConfig.ScreenReaderMode)
                terminal.WriteLine("═══════════════════════════════════════════════════════════════════", "bright_cyan");
            terminal.WriteLine("");

            await Task.Delay(3000);

            var credits = new[]
            {
                (Loc.Get("ending.credits_original_concept"), "bright_yellow"),
                (Loc.Get("ending.credits_original_author"), "white"),
                (Loc.Get("ending.credits_original_game"), "gray"),
                ("", "white"),
                (Loc.Get("ending.credits_reborn_by"), "bright_yellow"),
                (Loc.Get("ending.credits_reborn_author"), "white"),
                ("", "white"),
                (Loc.Get("ending.credits_story_narrative"), "bright_yellow"),
                (Loc.Get("ending.credits_story_author"), "white"),
                (Loc.Get("ending.credits_story_inspired_1"), "gray"),
                (Loc.Get("ending.credits_story_inspired_2"), "gray"),
                (Loc.Get("ending.credits_story_inspired_3"), "gray"),
                ("", "white"),
                (Loc.Get("ending.credits_systems_design"), "bright_yellow"),
                (Loc.Get("ending.credits_systems_author"), "white"),
                ("", "white"),
                (Loc.Get("ending.credits_artwork"), "bright_yellow"),
                (Loc.Get("ending.credits_artwork_xbit"), "white"),
                (Loc.Get("ending.credits_artwork_xbit_desc"), "gray"),
                (Loc.Get("ending.credits_artwork_sd"), "white"),
                (Loc.Get("ending.credits_artwork_sd_desc"), "gray"),
                (Loc.Get("ending.credits_artwork_cozmo"), "white"),
                (Loc.Get("ending.credits_artwork_cozmo_desc"), "gray"),
                ("", "white"),
                (Loc.Get("ending.credits_contributors"), "bright_yellow"),
                (Loc.Get("ending.credits_contributor_1"), "white"),
                (Loc.Get("ending.credits_contributor_3"), "white"),
                (Loc.Get("ending.credits_contributor_2"), "white"),
                ("", "white"),
                (Loc.Get("ending.credits_alpha_testers"), "bright_yellow"),
                (Loc.Get("ending.credits_testers_1"), "white"),
                (Loc.Get("ending.credits_testers_2"), "white"),
                (Loc.Get("ending.credits_testers_3"), "gray"),
                ("", "white"),
                (Loc.Get("ending.credits_special_thanks"), "bright_yellow"),
                (Loc.Get("ending.credits_thanks_1"), "white"),
                (Loc.Get("ending.credits_thanks_2"), "white"),
                ("", "white"),
                (Loc.Get("ending.credits_and_to_you"), "bright_yellow"),
                (Loc.Get("ending.credits_player", player.Name2), "bright_cyan"),
                (Loc.Get("ending.credits_final_level", player.Level), "cyan"),
                (Loc.Get("ending.credits_ending", GetEndingName(ending)), "cyan"),
                (Loc.Get("ending.credits_cycle", StoryProgressionSystem.Instance.CurrentCycle), "cyan"),
                ("", "white"),
                (Loc.Get("ending.credits_thank_you"), "bright_green")
            };

            foreach (var (line, color) in credits)
            {
                if (string.IsNullOrEmpty(line))
                {
                    terminal.WriteLine("");
                    await Task.Delay(500);
                }
                else
                {
                    terminal.WriteLine($"  {line}", color);
                    await Task.Delay(800);
                }
            }

            terminal.WriteLine("");
            if (!GameConfig.ScreenReaderMode)
                terminal.WriteLine("═══════════════════════════════════════════════════════════════════", "bright_cyan");
            terminal.WriteLine("");

            await Task.Delay(2000);

            // Show stats
            await ShowFinalStats(player, ending, terminal);

            OnCreditsComplete?.Invoke();
        }

        private async Task ShowFinalStats(Character player, EndingType ending, TerminalEmulator terminal)
        {
            var story = StoryProgressionSystem.Instance;

            terminal.WriteLine("");
            terminal.WriteLine($"                    {Loc.Get("ending.final_stats_header")}", "bright_yellow");
            if (!GameConfig.ScreenReaderMode)
                terminal.WriteLine("═══════════════════════════════════════════════════════════════════", "gray");
            terminal.WriteLine("");

            terminal.WriteLine($"  {Loc.Get("ending.final_stats_character", player.Name2, player.Class)}", "white");
            terminal.WriteLine($"  {Loc.Get("ending.final_stats_race", player.Race)}", "white");
            terminal.WriteLine($"  {Loc.Get("ending.final_stats_level", player.Level)}", "cyan");
            terminal.WriteLine("");

            terminal.WriteLine($"  {Loc.Get("ending.final_stats_monsters", player.MKills)}", "red");
            terminal.WriteLine($"  {Loc.Get("ending.final_stats_players", player.PKills)}", "dark_red");
            terminal.WriteLine($"  {Loc.Get("ending.final_stats_gold", player.Gold + player.BankGold)}", "yellow");
            terminal.WriteLine("");

            terminal.WriteLine($"  {Loc.Get("ending.final_stats_chivalry", player.Chivalry)}", "bright_green");
            terminal.WriteLine($"  {Loc.Get("ending.final_stats_darkness", player.Darkness)}", "dark_red");
            terminal.WriteLine("");

            terminal.WriteLine($"  {Loc.Get("ending.final_stats_artifacts", story.CollectedArtifacts.Count)}", "bright_magenta");
            terminal.WriteLine($"  {Loc.Get("ending.final_stats_seals", story.CollectedSeals.Count)}", "bright_cyan");
            terminal.WriteLine($"  {Loc.Get("ending.final_stats_choices", story.MajorChoices.Count)}", "white");
            terminal.WriteLine("");

            terminal.WriteLine($"  {Loc.Get("ending.final_stats_ending", GetEndingName(ending))}", "bright_yellow");
            terminal.WriteLine($"  {Loc.Get("ending.final_stats_cycle", story.CurrentCycle)}", "bright_magenta");
            terminal.WriteLine("");

            if (!GameConfig.ScreenReaderMode)
                terminal.WriteLine("═══════════════════════════════════════════════════════════════════", "gray");
            terminal.WriteLine("");

            await terminal.GetInputAsync(Loc.Get("ending.press_enter"));

            // Show personalized epilogue
            await ShowEpilogue(player, ending, terminal);

            // Show unlocks earned this run
            await ShowUnlocksEarned(player, ending, terminal);
        }

        /// <summary>
        /// Show a personalized epilogue based on player's journey
        /// </summary>
        private async Task ShowEpilogue(Character player, EndingType ending, TerminalEmulator terminal)
        {
            terminal.Clear();
            await Task.Delay(1000);

            terminal.WriteLine("");
            UIHelper.WriteBoxHeader(terminal, Loc.Get("ending.legacy_header"), "bright_cyan", 67);
            terminal.WriteLine("");

            await Task.Delay(500);

            var story = StoryProgressionSystem.Instance;
            var companions = CompanionSystem.Instance;
            var romance = RomanceTracker.Instance;

            // Character summary
            terminal.WriteLine($"  {Loc.Get("ending.legacy_hero_section")}", "bright_yellow");
            terminal.WriteLine($"  {Loc.Get("ending.legacy_hero_desc", player.Name2, player.Race, player.Class)}", "white");
            terminal.WriteLine($"  {Loc.Get("ending.legacy_hero_stats", player.Level, player.MKills)}", "gray");
            terminal.WriteLine("");

            await Task.Delay(300);

            // Alignment-based description
            long alignment = player.Chivalry - player.Darkness;
            string alignDesc;
            if (alignment > 500) alignDesc = Loc.Get("ending.legacy_align_hero");
            else if (alignment > 200) alignDesc = Loc.Get("ending.legacy_align_decent");
            else if (alignment > -200) alignDesc = Loc.Get("ending.legacy_align_neutral");
            else if (alignment > -500) alignDesc = Loc.Get("ending.legacy_align_dark");
            else alignDesc = Loc.Get("ending.legacy_align_evil");
            terminal.WriteLine($"  {Loc.Get("ending.legacy_known_as", alignDesc)}", "white");
            terminal.WriteLine("");

            await Task.Delay(300);

            // Companions
            terminal.WriteLine($"  {Loc.Get("ending.legacy_companions_section")}", "bright_yellow");
            var activeCompanions = companions.GetActiveCompanions();
            var fallenCompanions = companions.GetFallenCompanions().ToList();

            if (activeCompanions.Any())
            {
                terminal.WriteLine($"  {Loc.Get("ending.legacy_companions_active")}", "green");
                foreach (var c in activeCompanions)
                {
                    terminal.WriteLine($"    {Loc.Get("ending.legacy_companion_entry", c.Name, c.Level)}", "white");
                }
            }
            else
            {
                terminal.WriteLine($"  {Loc.Get("ending.legacy_companions_alone")}", "gray");
            }

            if (fallenCompanions.Count > 0)
            {
                terminal.WriteLine($"  {Loc.Get("ending.legacy_companions_fallen")}", "dark_red");
                foreach (var (companion, death) in fallenCompanions)
                {
                    terminal.WriteLine($"    {Loc.Get("ending.legacy_fallen_entry", companion.Name, death.Type)}", "gray");
                }
            }
            terminal.WriteLine("");

            await Task.Delay(300);

            // Romance
            terminal.WriteLine($"  {Loc.Get("ending.legacy_love_section")}", "bright_yellow");
            if (romance.Spouses.Count > 0)
            {
                var spouse = romance.Spouses[0];
                var spouseName = !string.IsNullOrEmpty(spouse.NPCName) ? spouse.NPCName : spouse.NPCId;
                terminal.WriteLine($"  {Loc.Get("ending.legacy_married_to", spouseName)}", "bright_magenta");
                if (spouse.Children > 0)
                {
                    terminal.WriteLine($"  {Loc.Get("ending.legacy_children", spouse.Children, spouse.Children > 1 ? Loc.Get("ending.legacy_children_plural") : "")}", "magenta");
                }
            }
            else if (romance.CurrentLovers.Count > 0)
            {
                terminal.WriteLine($"  {Loc.Get("ending.legacy_lovers", romance.CurrentLovers.Count)}", "magenta");
            }
            else
            {
                terminal.WriteLine($"  {Loc.Get("ending.legacy_no_romance")}", "gray");
            }

            if (romance.ExSpouses.Count > 0)
            {
                terminal.WriteLine($"  {Loc.Get("ending.legacy_divorces", romance.ExSpouses.Count)}", "gray");
            }
            terminal.WriteLine("");

            await Task.Delay(300);

            // World impact
            terminal.WriteLine($"  {Loc.Get("ending.legacy_world_section")}", "bright_yellow");
            await ShowWorldImpact(player, ending, story, terminal);
            terminal.WriteLine("");

            await Task.Delay(300);

            // Achievements unlocked
            terminal.WriteLine($"  {Loc.Get("ending.legacy_achievements_section")}", "bright_yellow");
            await ShowNotableAchievements(player, terminal);
            terminal.WriteLine("");

            await Task.Delay(300);

            // Jungian Archetype reveal
            terminal.WriteLine($"  {Loc.Get("ending.legacy_archetype_section")}", "bright_yellow");
            await ShowArchetypeReveal(player, terminal);
            terminal.WriteLine("");

            // Final quote based on ending
            if (!GameConfig.ScreenReaderMode)
                terminal.WriteLine("═══════════════════════════════════════════════════════════════════", "gray");
            string quote = ending switch
            {
                EndingType.Usurper => Loc.Get("ending.quote_usurper"),
                EndingType.Savior => Loc.Get("ending.quote_savior"),
                EndingType.Defiant => Loc.Get("ending.quote_defiant"),
                EndingType.TrueEnding => Loc.Get("ending.quote_true"),
                EndingType.Secret => Loc.Get("ending.quote_secret"),
                _ => Loc.Get("ending.quote_default")
            };
            terminal.WriteLine("");
            terminal.WriteLine($"  {quote}", "bright_cyan");
            terminal.WriteLine($"  {Loc.Get("ending.quote_attribution", player.Name2)}", "gray");
            terminal.WriteLine("");

            await terminal.GetInputAsync(Loc.Get("ending.press_enter"));
        }

        /// <summary>
        /// Show the impact of the player's choices on the world
        /// </summary>
        private async Task ShowWorldImpact(Character player, EndingType ending, StoryProgressionSystem story, TerminalEmulator terminal)
        {
            await Task.Delay(100);

            // Count gods saved vs destroyed
            int savedGods = 0;
            int destroyedGods = 0;
            foreach (var godState in story.OldGodStates.Values)
            {
                if (godState.Status == GodStatus.Saved || godState.Status == GodStatus.Awakened)
                    savedGods++;
                else if (godState.Status == GodStatus.Defeated)
                    destroyedGods++;
            }

            if (savedGods > destroyedGods)
            {
                terminal.WriteLine($"  {Loc.Get("ending.world_gods_saved", savedGods, destroyedGods)}", "green");
                terminal.WriteLine($"  {Loc.Get("ending.world_gods_saved_desc")}", "white");
            }
            else if (destroyedGods > savedGods)
            {
                terminal.WriteLine($"  {Loc.Get("ending.world_gods_destroyed", destroyedGods, savedGods)}", "dark_red");
                terminal.WriteLine($"  {Loc.Get("ending.world_gods_destroyed_desc")}", "white");
            }
            else
            {
                terminal.WriteLine($"  {Loc.Get("ending.world_gods_uncertain")}", "yellow");
            }

            // Economy impact
            long totalWealth = player.Gold + player.BankGold;
            if (totalWealth > 1000000)
            {
                terminal.WriteLine($"  {Loc.Get("ending.world_wealth_rich")}", "yellow");
            }
            else if (totalWealth > 100000)
            {
                terminal.WriteLine($"  {Loc.Get("ending.world_wealth_decent")}", "yellow");
            }

            // Combat impact
            if (player.MKills > 10000)
            {
                terminal.WriteLine($"  {Loc.Get("ending.world_kills_legend")}", "red");
            }
            else if (player.MKills > 1000)
            {
                terminal.WriteLine($"  {Loc.Get("ending.world_kills_bloody")}", "red");
            }

            // Story choices
            if (story.MajorChoices.Count > 10)
            {
                terminal.WriteLine($"  {Loc.Get("ending.world_choices", story.MajorChoices.Count)}", "bright_magenta");
            }

            // Ending-specific impact
            switch (ending)
            {
                case EndingType.Usurper:
                    terminal.WriteLine($"  {Loc.Get("ending.world_usurper")}", "dark_red");
                    break;
                case EndingType.Savior:
                    terminal.WriteLine($"  {Loc.Get("ending.world_savior")}", "bright_green");
                    break;
                case EndingType.Defiant:
                    terminal.WriteLine($"  {Loc.Get("ending.world_defiant")}", "bright_yellow");
                    break;
                case EndingType.TrueEnding:
                    terminal.WriteLine($"  {Loc.Get("ending.world_true")}", "bright_cyan");
                    break;
            }
        }

        /// <summary>
        /// Show notable achievements from this run
        /// </summary>
        private async Task ShowNotableAchievements(Character player, TerminalEmulator terminal)
        {
            await Task.Delay(100);

            var achievementCount = player.Achievements?.UnlockedCount ?? 0;
            var notableAchievements = new List<string>();

            // Pick up to 5 notable achievements
            if (player.Level >= 100) notableAchievements.Add(Loc.Get("ending.achievement_max_level"));
            if (player.MKills >= 10000) notableAchievements.Add(Loc.Get("ending.achievement_kills"));
            if (StoryProgressionSystem.Instance.CollectedSeals.Count >= 7) notableAchievements.Add(Loc.Get("ending.achievement_seals"));
            if (StoryProgressionSystem.Instance.CollectedArtifacts.Count >= 7) notableAchievements.Add(Loc.Get("ending.achievement_artifacts"));

            var companions = CompanionSystem.Instance;
            if (companions.GetActiveCompanions().Count() >= 3) notableAchievements.Add(Loc.Get("ending.achievement_party"));
            if (RomanceTracker.Instance.Spouses.Count > 0 && RomanceTracker.Instance.Spouses[0].Children > 0)
                notableAchievements.Add(Loc.Get("ending.achievement_family"));

            if (achievementCount >= 25) notableAchievements.Add(Loc.Get("ending.achievement_count", achievementCount));

            if (notableAchievements.Count == 0)
            {
                terminal.WriteLine($"  {Loc.Get("ending.achievement_beginning")}", "gray");
            }
            else
            {
                foreach (var achievement in notableAchievements.Take(5))
                {
                    terminal.WriteLine($"  * {achievement}", "bright_cyan");
                }
            }
        }

        /// <summary>
        /// Show the player's Jungian Archetype based on their playstyle
        /// </summary>
        private async Task ShowArchetypeReveal(Character player, TerminalEmulator terminal)
        {
            await Task.Delay(500);

            var tracker = ArchetypeTracker.Instance;
            var dominant = tracker.GetDominantArchetype();
            var secondary = tracker.GetSecondaryArchetype();

            var (name, title, description, color) = ArchetypeTracker.GetArchetypeInfo(dominant);
            var quote = ArchetypeTracker.GetArchetypeQuote(dominant);

            terminal.WriteLine($"  {Loc.Get("ending.archetype_intro")}", "white");
            terminal.WriteLine("");

            await Task.Delay(500);

            terminal.WriteLine($"  *** {name.ToUpper()} ***", color);
            terminal.WriteLine($"  \"{title}\"", color);
            terminal.WriteLine("");

            await Task.Delay(500);

            // Word wrap the description
            var words = description.Split(' ');
            var currentLine = "  ";
            foreach (var word in words)
            {
                if (currentLine.Length + word.Length + 1 > 68)
                {
                    terminal.WriteLine(currentLine, "white");
                    currentLine = "  " + word;
                }
                else
                {
                    currentLine += (currentLine.Length > 2 ? " " : "") + word;
                }
            }
            if (currentLine.Length > 2)
            {
                terminal.WriteLine(currentLine, "white");
            }
            terminal.WriteLine("");

            await Task.Delay(300);

            // Show secondary archetype
            var (secName, secTitle, _, secColor) = ArchetypeTracker.GetArchetypeInfo(secondary);
            terminal.WriteLine($"  {Loc.Get("ending.archetype_secondary", secName, secTitle)}", "gray");
            terminal.WriteLine("");

            await Task.Delay(300);

            // Show the archetype quote
            terminal.WriteLine($"  {quote}", "bright_cyan");
            terminal.WriteLine("");

            // Show some stats that contributed to this determination
            terminal.SetColor("darkgray");
            terminal.WriteLine($"  {Loc.Get("ending.archetype_stats_header")}");
            if (tracker.MonstersKilled > 0)
                terminal.WriteLine($"    {Loc.Get("ending.archetype_combat", tracker.MonstersKilled, tracker.BossesDefeated)}");
            if (tracker.DungeonFloorsExplored > 0)
                terminal.WriteLine($"    {Loc.Get("ending.archetype_exploration", tracker.DungeonFloorsExplored)}");
            if (tracker.SpellsCast > 0)
                terminal.WriteLine($"    {Loc.Get("ending.archetype_magic", tracker.SpellsCast)}");
            if (tracker.RomanceEncounters > 0)
                terminal.WriteLine($"    {Loc.Get("ending.archetype_romance", tracker.RomanceEncounters, tracker.MarriagesFormed)}");
            if (tracker.SealsCollected > 0 || tracker.ArtifactsCollected > 0)
                terminal.WriteLine($"    {Loc.Get("ending.archetype_wisdom", tracker.SealsCollected, tracker.ArtifactsCollected)}");
        }

        /// <summary>
        /// Show unlocks earned from completing this run
        /// </summary>
        private async Task ShowUnlocksEarned(Character player, EndingType ending, TerminalEmulator terminal)
        {
            terminal.Clear();
            await Task.Delay(500);

            terminal.WriteLine("");
            UIHelper.WriteBoxHeader(terminal, Loc.Get("ending.unlocks_header"), "bright_green", 67);
            terminal.WriteLine("");

            var unlocks = new List<(string name, string description, string color)>();

            // Ending-based unlocks
            switch (ending)
            {
                case EndingType.Usurper:
                    unlocks.Add((Loc.Get("ending.unlock_dark_lord_name"), Loc.Get("ending.unlock_dark_lord_desc"), "dark_red"));
                    unlocks.Add((Loc.Get("ending.unlock_tyrant_name"), Loc.Get("ending.unlock_tyrant_desc"), "red"));
                    unlocks.Add((Loc.Get("ending.unlock_fear_name"), Loc.Get("ending.unlock_fear_desc"), "dark_red"));
                    break;
                case EndingType.Savior:
                    unlocks.Add((Loc.Get("ending.unlock_savior_name"), Loc.Get("ending.unlock_savior_desc"), "bright_green"));
                    unlocks.Add((Loc.Get("ending.unlock_healing_name"), Loc.Get("ending.unlock_healing_desc"), "green"));
                    unlocks.Add((Loc.Get("ending.unlock_commerce_name"), Loc.Get("ending.unlock_commerce_desc"), "yellow"));
                    break;
                case EndingType.Defiant:
                    unlocks.Add((Loc.Get("ending.unlock_defiant_name"), Loc.Get("ending.unlock_defiant_desc"), "bright_yellow"));
                    unlocks.Add((Loc.Get("ending.unlock_mortal_name"), Loc.Get("ending.unlock_mortal_desc"), "cyan"));
                    unlocks.Add((Loc.Get("ending.unlock_key_name"), Loc.Get("ending.unlock_key_desc"), "bright_magenta"));
                    break;
                case EndingType.TrueEnding:
                    unlocks.Add((Loc.Get("ending.unlock_awakened_name"), Loc.Get("ending.unlock_awakened_desc"), "bright_cyan"));
                    unlocks.Add((Loc.Get("ending.unlock_ocean_name"), Loc.Get("ending.unlock_ocean_desc"), "bright_cyan"));
                    unlocks.Add((Loc.Get("ending.unlock_artifact_name"), Loc.Get("ending.unlock_artifact_desc"), "bright_magenta"));
                    unlocks.Add((Loc.Get("ending.unlock_seal_name"), Loc.Get("ending.unlock_seal_desc"), "bright_magenta"));
                    break;
                case EndingType.Secret:
                    unlocks.Add((Loc.Get("ending.unlock_dissolved_name"), Loc.Get("ending.unlock_dissolved_desc"), "white"));
                    break;
            }

            // Level-based unlocks
            if (player.Level >= 50)
                unlocks.Add((Loc.Get("ending.unlock_veteran_name"), Loc.Get("ending.unlock_veteran_desc"), "white"));
            if (player.Level >= 100)
                unlocks.Add((Loc.Get("ending.unlock_master_name"), Loc.Get("ending.unlock_master_desc"), "bright_yellow"));

            // Kill-based unlocks
            if (player.MKills >= 5000)
                unlocks.Add((Loc.Get("ending.unlock_slayer_name"), Loc.Get("ending.unlock_slayer_desc"), "red"));

            // Collection unlocks
            if (StoryProgressionSystem.Instance.CollectedSeals.Count >= 7)
                unlocks.Add((Loc.Get("ending.unlock_seal_master_name"), Loc.Get("ending.unlock_seal_master_desc"), "bright_cyan"));
            if (StoryProgressionSystem.Instance.CollectedArtifacts.Count >= 7)
                unlocks.Add((Loc.Get("ending.unlock_artifact_hunter_name"), Loc.Get("ending.unlock_artifact_hunter_desc"), "bright_magenta"));

            // Companion unlocks
            var companions = CompanionSystem.Instance;
            if (companions.GetFallenCompanions().Any())
                unlocks.Add((Loc.Get("ending.unlock_survivor_name"), Loc.Get("ending.unlock_survivor_desc"), "gray"));

            terminal.WriteLine($"  {Loc.Get("ending.unlocks_intro")}", "white");
            terminal.WriteLine("");

            foreach (var (name, description, color) in unlocks)
            {
                terminal.WriteLine($"  [{name}]", color);
                terminal.WriteLine($"    {description}", "gray");
                terminal.WriteLine("");
                await Task.Delay(300);
            }

            // Track unlocks
            MetaProgressionSystem.Instance.RecordEndingUnlock(ending, player);

            if (!GameConfig.ScreenReaderMode)
                terminal.WriteLine("═══════════════════════════════════════════════════════════════════", "gray");
            terminal.WriteLine("");
            terminal.WriteLine($"  {Loc.Get("ending.unlocks_apply_ngplus")}", "bright_green");
            terminal.WriteLine("");

            await terminal.GetInputAsync(Loc.Get("ending.press_enter"));
        }

        private string GetEndingName(EndingType ending)
        {
            return ending switch
            {
                EndingType.Usurper => Loc.Get("ending.ending_name_usurper"),
                EndingType.Savior => Loc.Get("ending.ending_name_savior"),
                EndingType.Defiant => Loc.Get("ending.ending_name_defiant"),
                EndingType.TrueEnding => Loc.Get("ending.ending_name_true"),
                _ => Loc.Get("ending.ending_name_unknown")
            };
        }

        #endregion

        #region Immortal Ascension

        private async Task<bool> OfferImmortality(Character player, EndingType ending, TerminalEmulator terminal)
        {
            // Phase 7: emit graphical immortality offer overlay. Y/N flows back
            // through the existing GetInputAsync so the parser is unchanged.
            if (GameConfig.ElectronMode)
            {
                ElectronBridge.EmitImmortalAscension(new ElectronBridge.ImmortalAscensionData
                {
                    PromptStage = "offer",
                    CharacterName = player.Name2 ?? player.Name1 ?? "",
                    ClassName = player.ClassName,
                    ManweDialogue = new List<string>
                    {
                        Loc.Get("ending.immortal_manwe_1"),
                        Loc.Get("ending.immortal_manwe_2"),
                        Loc.Get("ending.immortal_manwe_3")
                    },
                    Benefits = new List<string>
                    {
                        Loc.Get("ending.immortal_benefit_1"),
                        Loc.Get("ending.immortal_benefit_2"),
                        Loc.Get("ending.immortal_benefit_3"),
                        Loc.Get("ending.immortal_benefit_4")
                    },
                    IsKing = player.King
                });
            }

            terminal.Clear();
            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            if (!GameConfig.ScreenReaderMode)
                terminal.WriteLine("═══════════════════════════════════════════════════════════════════", "bright_yellow");
            terminal.WriteLine($"              {Loc.Get("ending.immortal_header")}", "bright_yellow");
            if (!GameConfig.ScreenReaderMode)
                terminal.WriteLine("═══════════════════════════════════════════════════════════════════", "bright_yellow");
            terminal.WriteLine("");

            await Task.Delay(1000);

            terminal.WriteLine($"  {Loc.Get("ending.immortal_power")}", "white");
            terminal.WriteLine($"  {Loc.Get("ending.immortal_coil")}", "white");
            terminal.WriteLine("");

            await Task.Delay(800);

            terminal.WriteLine($"  {Loc.Get("ending.immortal_manwe_1")}", "bright_magenta");
            terminal.WriteLine($"  {Loc.Get("ending.immortal_manwe_2")}", "bright_magenta");
            terminal.WriteLine($"  {Loc.Get("ending.immortal_manwe_3")}", "bright_magenta");
            terminal.WriteLine("");

            await Task.Delay(800);

            terminal.WriteLine($"  {Loc.Get("ending.immortal_as_god")}", "bright_cyan");
            terminal.WriteLine($"  {Loc.Get("ending.immortal_benefit_1")}", "white");
            terminal.WriteLine($"  {Loc.Get("ending.immortal_benefit_2")}", "white");
            terminal.WriteLine($"  {Loc.Get("ending.immortal_benefit_3")}", "white");
            terminal.WriteLine($"  {Loc.Get("ending.immortal_benefit_4")}", "white");
            terminal.WriteLine("");
            terminal.WriteLine($"  {Loc.Get("ending.immortal_renounce")}", "gray");
            terminal.WriteLine("");

            string response;
            while (true)
            {
                response = (await terminal.GetInputAsync(Loc.Get("ending.immortal_ascend_prompt"))).Trim().ToUpper();
                if (response == "Y" || response == "YES") break;
                if (response == "N" || response == "NO") return false;
                terminal.WriteLine("  Please enter Y or N.", "gray");
            }

            // Choose divine name
            terminal.WriteLine("");
            terminal.WriteLine($"  {Loc.Get("ending.immortal_choose_name")}", "bright_cyan");
            string divineName = "";
            while (true)
            {
                divineName = (await terminal.GetInputAsync(Loc.Get("ending.immortal_name_prompt"))).Trim();
                if (divineName.Length >= 3 && divineName.Length <= 30)
                    break;
                terminal.WriteLine($"  {Loc.Get("ending.immortal_name_invalid")}", "red");
            }

            // Determine alignment from ending
            string alignment = ending switch
            {
                EndingType.Savior => "Light",
                EndingType.Usurper => "Dark",
                EndingType.Defiant => "Balance",
                EndingType.TrueEnding => "Balance",
                _ => "Balance"
            };

            // Auto-abdicate if player is the king
            if (player.King)
            {
                CastleLocation.AbdicatePlayerThrone(player, "abdicated the throne to ascend to godhood");
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"  {Loc.Get("ending.immortal_abdicated")}", "bright_yellow");
                terminal.WriteLine("");
                await Task.Delay(1500);
            }

            // v0.60.0: Auto-quit any current team. A god on a mortal's roster
            // is a category error: gods are venerated, not hired. Player report:
            // "God characters showing up in team after rolling new character, can
            // be used in dungeons." Some ascensions left the player on their
            // previous team, and team members remained recruitable to others
            // through the recruitment menu (separately fixed in
            // TeamSystem.IsRecruitable).
            if (!string.IsNullOrEmpty(player.Team))
            {
                new TeamSystem().QuitTeam(player);
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"  Your mortal team bonds dissolve in the divine ascension.");
                terminal.WriteLine("");
                await Task.Delay(1000);
            }

            // v0.60.0: Auto-leave any current guild for the same reason. Gods don't
            // hold guild membership; without this, an ascended player would still
            // appear on the guild roster (and the GuildSystem.AddMember/CreateGuild
            // filters added in this release would reject re-joining, leaving them
            // permanently grandfathered into a mortal guild). RemoveMember handles
            // leader-promotion automatically if the ascending player led the guild.
            var ctxAscend = UsurperRemake.Server.SessionContext.Current;
            if (ctxAscend != null && !string.IsNullOrEmpty(ctxAscend.Username))
            {
                var guildSys = Systems.GuildSystem.Instance;
                if (guildSys != null && guildSys.GetPlayerGuild(ctxAscend.Username) != null)
                {
                    guildSys.RemoveMember(ctxAscend.Username);
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine($"  Your guild crest tarnishes and falls away. The faithful have no guild.");
                    terminal.WriteLine("");
                    await Task.Delay(1000);
                }
            }

            // Block alt characters from ascending
            if (SqlSaveBackend.IsAltCharacter(UsurperRemake.BBS.DoorMode.GetPlayerName() ?? ""))
            {
                terminal.WriteLine("");
                terminal.WriteLine($"  {Loc.Get("ending.immortal_alt_blocked")}", "red");
                terminal.WriteLine($"  {Loc.Get("ending.immortal_alt_main_only")}", "gray");
                await Task.Delay(2000);
                return false;
            }

            // Mark the alt slot as earned (persists even if they renounce)
            player.HasEarnedAltSlot = true;

            // Convert to immortal
            player.IsImmortal = true;
            player.DivineName = divineName;
            player.GodLevel = 1;
            player.GodExperience = 0;
            player.DeedsLeft = GameConfig.GodDeedsPerDay[0];
            player.GodAlignment = alignment;
            player.AscensionDate = DateTime.UtcNow;

            terminal.WriteLine("");
            await Task.Delay(500);

            terminal.SetColor("bright_yellow");
            if (!GameConfig.ScreenReaderMode)
                terminal.WriteLine("  ════════════════════════════════════════════════════════════");
            terminal.WriteLine("");
            terminal.WriteLine($"  {Loc.Get("ending.immortal_ascended", divineName, alignment)}", "bright_yellow");
            terminal.WriteLine($"  {Loc.Get("ending.immortal_ascended_msg")}", "bright_yellow");
            terminal.WriteLine("");
            if (!GameConfig.ScreenReaderMode)
                terminal.WriteLine("  ════════════════════════════════════════════════════════════");

            await Task.Delay(1000);

            // Write news
            NewsSystem.Instance?.Newsy(true,
                $"[DIVINE] {player.Name2} has ascended to godhood as {divineName}!");

            // Broadcast to all online players
            if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
            {
                try
                {
                    MudServer.Instance?.BroadcastToAll(
                        $"\r\n\x1b[1;33m  {divineName}, Lesser Spirit of {alignment}\r\n  has ascended to the Divine Realm!\x1b[0m\r\n",
                        excludeUsername: player.DisplayName);
                }
                catch { /* broadcast is optional */ }
            }

            // Achievement
            AchievementSystem.TryUnlock(player, "ascended");

            // Record this ending and advance the cycle — the player completed the game,
            // they just chose godhood instead of immediate reroll. This ensures:
            // 1. CompletedEndings tracks their achievement (gates prestige classes)
            // 2. CurrentCycle increments (gates NG+ bonuses when they renounce)
            StoryProgressionSystem.Instance.CompletedEndings.Add(ending);
            StoryProgressionSystem.Instance.CurrentCycle++;

            // Save immediately (includes the ending/cycle data)
            try
            {
                await SaveSystem.Instance.AutoSave(player);
            }
            catch { /* best effort */ }

            terminal.WriteLine("");
            terminal.WriteLine($"  {Loc.Get("ending.immortal_enter_pantheon")}", "bright_cyan");
            terminal.WriteLine("");

            await terminal.GetInputAsync(Loc.Get("ending.immortal_enter_prompt"));

            // Mark ending sequence as completed before routing to Pantheon
            StoryProgressionSystem.Instance.SetStoryFlag("ending_sequence_completed", true);

            // Route to Pantheon
            GameEngine.Instance.PendingImmortalAscension = true;

            return true;
        }

        #endregion

        #region New Game Plus

        private async Task<bool> OfferNewGamePlus(Character player, EndingType ending, TerminalEmulator terminal)
        {
            // Phase 7: emit graphical NG+ prompt. Y/N flows through existing input.
            if (GameConfig.ElectronMode)
            {
                var cycleNum = StoryProgressionSystem.Instance.CurrentCycle;
                ElectronBridge.EmitNewGamePlusPrompt(
                    currentCycle: cycleNum,
                    nextCycle: cycleNum + 1,
                    carryoverBonuses: new List<string>
                    {
                        Loc.Get("ending.ngplus_bonus_stats"),
                        Loc.Get("ending.ngplus_again_1"),
                        Loc.Get("ending.ngplus_again_2"),
                        Loc.Get("ending.ngplus_again_3")
                    });
            }

            terminal.Clear();
            terminal.WriteLine("");
            if (!GameConfig.ScreenReaderMode)
                terminal.WriteLine("═══════════════════════════════════════════════════════════════════", "bright_magenta");
            terminal.WriteLine($"                  {Loc.Get("ending.ngplus_header")}", "bright_magenta");
            if (!GameConfig.ScreenReaderMode)
                terminal.WriteLine("═══════════════════════════════════════════════════════════════════", "bright_magenta");
            terminal.WriteLine("");

            await Task.Delay(1000);

            terminal.WriteLine($"  {Loc.Get("ending.ngplus_stirs")}", "white");
            terminal.WriteLine($"  {Loc.Get("ending.ngplus_voice")}", "white");
            terminal.WriteLine("");

            await Task.Delay(800);

            terminal.WriteLine($"  {Loc.Get("ending.ngplus_again_1")}", "bright_magenta");
            terminal.WriteLine($"  {Loc.Get("ending.ngplus_again_2")}", "bright_magenta");
            terminal.WriteLine($"  {Loc.Get("ending.ngplus_again_3")}", "bright_magenta");
            terminal.WriteLine("");

            await Task.Delay(1000);

            terminal.WriteLine($"  {Loc.Get("ending.ngplus_wheel")}", "bright_cyan");
            terminal.WriteLine("");

            var cycle = StoryProgressionSystem.Instance.CurrentCycle;
            terminal.WriteLine($"  {Loc.Get("ending.ngplus_current_cycle", cycle)}", "yellow");
            terminal.WriteLine($"  {Loc.Get("ending.ngplus_next_cycle", cycle + 1)}", "green");
            terminal.WriteLine("");

            terminal.WriteLine($"  {Loc.Get("ending.ngplus_bonuses")}", "bright_green");
            terminal.WriteLine($"  {Loc.Get("ending.ngplus_bonus_stats")}", "white");
            terminal.WriteLine($"  {Loc.Get("ending.ngplus_bonus_xp")}", "white");
            terminal.WriteLine($"  {Loc.Get("ending.ngplus_bonus_artifacts")}", "white");
            terminal.WriteLine($"  {Loc.Get("ending.ngplus_bonus_dialogue")}", "white");

            // Show which prestige classes this ending unlocks
            var newClasses = new List<string>();
            switch (ending)
            {
                case EndingType.Savior:
                    newClasses.Add("Tidesworn (Holy)");
                    newClasses.Add("Wavecaller (Good)");
                    break;
                case EndingType.Defiant:
                    newClasses.Add("Cyclebreaker (Neutral)");
                    break;
                case EndingType.Usurper:
                    newClasses.Add("Abysswarden (Dark)");
                    newClasses.Add("Voidreaver (Evil)");
                    break;
                case EndingType.TrueEnding:
                case EndingType.Secret:
                    newClasses.Add("Tidesworn (Holy)");
                    newClasses.Add("Wavecaller (Good)");
                    newClasses.Add("Cyclebreaker (Neutral)");
                    newClasses.Add("Abysswarden (Dark)");
                    newClasses.Add("Voidreaver (Evil)");
                    break;
            }
            if (newClasses.Count > 0)
            {
                terminal.WriteLine($"  {Loc.Get("ending.ngplus_prestige_intro")}", "white");
                foreach (var cls in newClasses)
                    terminal.WriteLine($"      {cls}", "bright_cyan");
            }
            terminal.WriteLine("");

            string response;
            while (true)
            {
                response = (await terminal.GetInputAsync($"  {Loc.Get("ending.ngplus_begin_prompt")} ")).Trim().ToUpper();
                if (response == "Y" || response == "YES" || response == "N" || response == "NO") break;
                terminal.WriteLine("  Please enter Y or N.", "gray");
            }

            if (response == "Y" || response == "YES")
            {
                await CycleSystem.Instance.StartNewCycle(player, ending, terminal);
                // Signal the game to restart with a new character
                GameEngine.Instance.PendingNewGamePlus = true;

                // Mark the ending sequence as fully completed
                StoryProgressionSystem.Instance.SetStoryFlag("ending_sequence_completed", true);
                try { await SaveSystem.Instance.AutoSave(player); } catch { }
                return true;
            }

            return false;
        }

        #endregion

        #region Electron Bridge Helpers

        /// <summary>
        /// Build the structured ending screen payload for the Electron client.
        /// Emitted at the top of TriggerEnding so the JS overlay shows the end
        /// card cinematic while the per-ending text path scrolls behind it.
        /// </summary>
        private ElectronBridge.EndingScreenData BuildEndingScreenData(Character player, EndingType ending)
        {
            string title = ending switch
            {
                EndingType.Usurper => Loc.Get("ending.usurper_header"),
                EndingType.Savior => Loc.Get("ending.savior_header"),
                EndingType.Defiant => Loc.Get("ending.defiant_header"),
                EndingType.TrueEnding => Loc.Get("ending.true_header"),
                EndingType.Secret => Loc.Get("ending.dissolution_header"),
                _ => ending.ToString()
            };

            // Defiant ends mortally; Usurper/Savior/TrueEnding offer immortality.
            // Dissolution returns early in TriggerEnding before we'd reach the loop.
            bool offerImmortal = ending != EndingType.Secret && ending != EndingType.Defiant;
            bool offerNgPlus = ending != EndingType.Secret;

            return new ElectronBridge.EndingScreenData
            {
                EndingType = ending.ToString(),
                Title = title,
                Subtitle = "",
                Credits = new List<string>(),
                Epilogue = new List<string>(),
                FinalLevel = player.Level,
                TotalKills = player.Statistics?.TotalMonstersKilled ?? 0,
                TotalGold = player.Gold,
                FameFinal = (int)player.Fame,
                CycleNumber = StoryProgressionSystem.Instance.CurrentCycle,
                PlayTimeSeconds = (long)(player.Statistics?.TotalPlayTime.TotalSeconds ?? 0),
                ImmortalityOffered = offerImmortal,
                NgPlusOffered = offerNgPlus
            };
        }

        #endregion
    }
}
