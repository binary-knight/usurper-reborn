using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UsurperRemake.UI;
using UsurperRemake.Utils;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Opening Story System - Presents the player with a compelling narrative hook
    /// after character creation. Establishes the mystery, the goal, and hints at
    /// the deeper Ocean Philosophy truth.
    /// </summary>
    public class OpeningStorySystem
    {
        private static OpeningStorySystem? _instance;
        public static OpeningStorySystem Instance => _instance ??= new OpeningStorySystem();

        // Skip mode - when true, text appears instantly
        private bool _skipMode = false;
        private TerminalEmulator? _terminal;

        public OpeningStorySystem()
        {
            _instance = this;
        }

        /// <summary>
        /// Delay that can be skipped by pressing space bar.
        /// Works in local, BBS socket, BBS stdio, and MUD modes via TerminalEmulator.IsInputAvailable().
        /// </summary>
        private async Task SkippableDelay(int milliseconds)
        {
            if (_skipMode)
            {
                await Task.Delay(10); // Minimal delay even in skip mode for readability
                return;
            }

            int elapsed = 0;
            const int checkInterval = 50;

            while (elapsed < milliseconds)
            {
                try
                {
                    if (_terminal != null && _terminal.IsInputAvailable())
                    {
                        _terminal.FlushPendingInput();
                        _skipMode = true;
                        return;
                    }
                }
                catch
                {
                    // Input check failed, just continue with the delay
                }
                await Task.Delay(checkInterval);
                elapsed += checkInterval;
            }
        }

        /// <summary>
        /// Play the opening story sequence for a new character
        /// </summary>
        public async Task PlayOpeningSequence(Character player, TerminalEmulator terminal)
        {
            // Reset skip mode for this playthrough
            _skipMode = false;
            _terminal = terminal;

            // Show skip hint — MUD/BBS uses line input so ENTER is needed, not SPACE
            terminal.Clear();
            terminal.WriteLine("");
            bool isLineInput = UsurperRemake.BBS.DoorMode.IsOnlineMode || UsurperRemake.BBS.DoorMode.IsInDoorMode;
            terminal.WriteLine($"  ({Loc.Get(isLineInput ? "opening_story.skip_hint_enter" : "opening_story.skip_hint")})", "dark_gray");
            await SkippableDelay(2000);

            // Phase 1: The Awakening (mysterious dream-like intro)
            await PlayAwakening(player, terminal);

            // Phase 2: The Dormitory Scene
            await PlayDormitoryScene(player, terminal);

            // Phase 3: The First Mystery
            await PlayFirstMystery(player, terminal);

            // Phase 4: The Goal Revealed
            await PlayGoalReveal(player, terminal);

            // Mark that player has seen the intro
            StoryProgressionSystem.Instance.SetStoryFlag("opening_complete", true);
        }

        /// <summary>
        /// Phase 1: A haunting dream sequence before the player wakes
        /// Plants seeds for the Ocean Philosophy revelation
        /// </summary>
        private async Task PlayAwakening(Character player, TerminalEmulator terminal)
        {
            terminal.Clear();
            await SkippableDelay(1000);

            // Fade in from darkness
            terminal.WriteLine("");
            terminal.WriteLine("");
            terminal.WriteLine("", "white");
            await SkippableDelay(2000);

            // The dream
            var dreamLines = new[]
            {
                (Loc.Get("opening_story.dream_ellipsis"), "dark_gray"),
                ("", ""),
                (Loc.Get("opening_story.dream_drowning"), "gray"),
                ("", ""),
                (Loc.Get("opening_story.dream_not_water"), "bright_cyan"),
                (Loc.Get("opening_story.dream_endless"), "bright_cyan"),
                ("", ""),
                (Loc.Get("opening_story.dream_voice"), "white"),
                ("", ""),
                (Loc.Get("opening_story.dream_remember"), "bright_yellow"),
                ("", ""),
                (Loc.Get("opening_story.dream_remember_what"), "gray"),
                ("", ""),
                (Loc.Get("opening_story.dream_remember_answer"), "bright_yellow"),
                ("", ""),
                (Loc.Get("opening_story.dream_fades"), "gray"),
                (Loc.Get("opening_story.dream_and_then"), "gray")
            };

            foreach (var (line, color) in dreamLines)
            {
                if (string.IsNullOrEmpty(line))
                {
                    terminal.WriteLine("");
                }
                else
                {
                    terminal.WriteLine($"  {line}", color);
                }
                await SkippableDelay(800);
            }

            await SkippableDelay(1500);
            terminal.Clear();
        }

        /// <summary>
        /// Phase 2: Waking up in the dormitory with no memory
        /// </summary>
        private async Task PlayDormitoryScene(Character player, TerminalEmulator terminal)
        {
            terminal.Clear();
            await SkippableDelay(500);

            terminal.WriteLine("");
            UIHelper.WriteBoxHeader(terminal, Loc.Get("opening_story.the_awakening"), "dark_cyan", 67);
            terminal.WriteLine("");

            await SkippableDelay(1000);

            var sceneLines = new[]
            {
                (Loc.Get("opening_story.dorm_gasp"), "white"),
                ("", ""),
                (Loc.Get("opening_story.dorm_cold_stone"), "gray"),
                (Loc.Get("opening_story.dorm_rows"), "gray"),
                ("", ""),
                (Loc.Get("opening_story.dorm_head_pounds"), "white"),
                ("", ""),
                (Loc.Get("opening_story.dorm_nothing"), "bright_red"),
                ("", ""),
                (Loc.Get("opening_story.dorm_name_past"), "gray"),
                (Loc.Get("opening_story.dorm_all_gone"), "gray"),
                ("", ""),
                (Loc.Get("opening_story.dorm_fragments"), "white"),
                ("", ""),
                ($"  {Loc.Get("opening_story.dorm_called", player.Name2)}", "bright_yellow"),
                ($"  {Loc.Get("opening_story.dorm_race", GetRaceDescription(player.Race))}", "yellow"),
                ($"  {Loc.Get("opening_story.dorm_class", GetClassDescription(player.Class))}", "yellow"),
                ("", ""),
                (Loc.Get("opening_story.dorm_who_were"), "white"),
                (Loc.Get("opening_story.dorm_slip_away"), "cyan"),
            };

            foreach (var (line, color) in sceneLines)
            {
                if (string.IsNullOrEmpty(line))
                {
                    terminal.WriteLine("");
                }
                else
                {
                    terminal.WriteLine($"  {line}", color);
                }
                await SkippableDelay(400);
            }

            terminal.WriteLine("");
            await terminal.GetInputAsync($"  {Loc.Get("ui.press_enter")}");
        }

        /// <summary>
        /// Phase 3: The first mystery - something strange about this place
        /// </summary>
        private async Task PlayFirstMystery(Character player, TerminalEmulator terminal)
        {
            terminal.Clear();
            await SkippableDelay(500);

            var mysteryLines = new[]
            {
                (Loc.Get("opening_story.mystery_stand"), "white"),
                ("", ""),
                (Loc.Get("opening_story.mystery_parchment"), "gray"),
                (Loc.Get("opening_story.mystery_handwriting"), "bright_yellow"),
                (Loc.Get("opening_story.mystery_dont_remember"), "white"),
                ("", ""),
                (Loc.Get("opening_story.mystery_reads"), "gray"),
                ("", ""),
            };

            foreach (var (line, color) in mysteryLines)
            {
                if (string.IsNullOrEmpty(line))
                {
                    terminal.WriteLine("");
                }
                else
                {
                    terminal.WriteLine($"  {line}", color);
                }
                await SkippableDelay(400);
            }

            // The letter - the hook
            if (!GameConfig.ScreenReaderMode)
            {
                terminal.WriteLine("  ┌─────────────────────────────────────────────────────────┐", "yellow");
                await SkippableDelay(200);
                terminal.WriteLine("  │                                                         │", "yellow");
                terminal.WriteLine($"  │  {Loc.Get("opening_story.letter_to_myself"),-55}│", "yellow");
                await SkippableDelay(500);
                terminal.WriteLine("  │                                                         │", "yellow");
                terminal.WriteLine($"  │  {Loc.Get("opening_story.letter_gods_broken"),-55}│", "yellow");
                await SkippableDelay(300);
                terminal.WriteLine($"  │  {Loc.Get("opening_story.letter_old_gods"),-55}│", "yellow");
                await SkippableDelay(300);
                terminal.WriteLine("  │                                                         │", "yellow");
                terminal.WriteLine($"  │  {Loc.Get("opening_story.letter_seven_seals"),-55}│", "yellow");
                await SkippableDelay(300);
                terminal.WriteLine($"  │  {Loc.Get("opening_story.letter_collect"),-55}│", "yellow");
                await SkippableDelay(300);
                terminal.WriteLine("  │                                                         │", "yellow");
                terminal.WriteLine($"  │  {Loc.Get("opening_story.letter_trust"),-55}│", "yellow");
                await SkippableDelay(300);
                terminal.WriteLine("  │                                                         │", "yellow");
                terminal.WriteLine($"  │  {Loc.Get("opening_story.letter_remember"),-55}│", "yellow");
                terminal.WriteLine($"  │  {Loc.Get("opening_story.letter_not_what"),-55}│", "bright_cyan");
                terminal.WriteLine($"  │  {Loc.Get("opening_story.letter_never_were"),-55}│", "bright_cyan");
                await SkippableDelay(500);
                terminal.WriteLine("  │                                                         │", "yellow");
                terminal.WriteLine($"  │  {Loc.Get("opening_story.letter_signed"),55}│", "gray");
                terminal.WriteLine("  │                                                         │", "yellow");
                terminal.WriteLine("  └─────────────────────────────────────────────────────────┘", "yellow");
            }
            else
            {
                terminal.WriteLine($"  --- {Loc.Get("opening_story.letter_title")} ---", "yellow");
                await SkippableDelay(200);
                terminal.WriteLine("", "yellow");
                terminal.WriteLine($"  {Loc.Get("opening_story.letter_to_myself")}", "yellow");
                await SkippableDelay(500);
                terminal.WriteLine("", "yellow");
                terminal.WriteLine($"  {Loc.Get("opening_story.letter_gods_broken")}", "yellow");
                await SkippableDelay(300);
                terminal.WriteLine($"  {Loc.Get("opening_story.letter_old_gods")}", "yellow");
                await SkippableDelay(300);
                terminal.WriteLine("", "yellow");
                terminal.WriteLine($"  {Loc.Get("opening_story.letter_seven_seals")}", "yellow");
                await SkippableDelay(300);
                terminal.WriteLine($"  {Loc.Get("opening_story.letter_collect")}", "yellow");
                await SkippableDelay(300);
                terminal.WriteLine("", "yellow");
                terminal.WriteLine($"  {Loc.Get("opening_story.letter_trust")}", "yellow");
                await SkippableDelay(300);
                terminal.WriteLine("", "yellow");
                terminal.WriteLine($"  {Loc.Get("opening_story.letter_remember")}", "yellow");
                terminal.WriteLine($"  {Loc.Get("opening_story.letter_not_what")}", "bright_cyan");
                terminal.WriteLine($"  {Loc.Get("opening_story.letter_never_were")}", "bright_cyan");
                await SkippableDelay(500);
                terminal.WriteLine("", "yellow");
                terminal.WriteLine($"                                    {Loc.Get("opening_story.letter_signed")}", "gray");
                terminal.WriteLine("", "yellow");
            }

            terminal.WriteLine("");
            await SkippableDelay(1000);

            terminal.WriteLine("");
            terminal.WriteLine($"  {Loc.Get("opening_story.parchment_crumbles")}", "gray");
            terminal.WriteLine($"  {Loc.Get("opening_story.words_burn")}", "white");

            terminal.WriteLine("");
            await terminal.GetInputAsync($"  {Loc.Get("ui.press_enter")}");
        }

        /// <summary>
        /// Phase 4: The goal is established, player is released into the world
        /// </summary>
        private async Task PlayGoalReveal(Character player, TerminalEmulator terminal)
        {
            terminal.Clear();
            await SkippableDelay(500);

            terminal.WriteLine("");
            UIHelper.WriteBoxHeader(terminal, Loc.Get("opening_story.journey_begins"), "bright_cyan", 67);
            terminal.WriteLine("");

            await SkippableDelay(500);

            var goalLines = new[]
            {
                (Loc.Get("opening_story.goal_step_out"), "white"),
                ("", ""),
                (Loc.Get("opening_story.goal_dorashire"), "gray"),
                (Loc.Get("opening_story.goal_locations"), "gray"),
                (Loc.Get("opening_story.goal_dungeons"), "bright_red"),
                ("", ""),
                (Loc.Get("opening_story.goal_somewhere"), "white"),
                (Loc.Get("opening_story.goal_seals"), "bright_yellow"),
                (Loc.Get("opening_story.goal_old_gods"), "yellow"),
                (Loc.Get("opening_story.goal_truth"), "bright_cyan"),
                ("", ""),
                (Loc.Get("opening_story.goal_dont_know_memory"), "gray"),
                (Loc.Get("opening_story.goal_dont_know_warning"), "gray"),
                (Loc.Get("opening_story.goal_dont_know_cycle"), "gray"),
                ("", ""),
                (Loc.Get("opening_story.goal_but_know"), "white"),
                ("", ""),
                (Loc.Get("opening_story.goal_find_out"), "bright_white"),
            };

            foreach (var (line, color) in goalLines)
            {
                if (string.IsNullOrEmpty(line))
                {
                    terminal.WriteLine("");
                }
                else
                {
                    terminal.WriteLine($"  {line}", color);
                }
                await SkippableDelay(350);
            }

            terminal.WriteLine("");
            await SkippableDelay(1000);

            // Quick gameplay tips
            terminal.WriteLine("");
            if (!GameConfig.ScreenReaderMode)
                terminal.WriteLine("  ─────────────────────────────────────────────────────────", "dark_cyan");
            terminal.WriteLine("");
            terminal.WriteLine($"  {Loc.Get("opening_story.what_to_do")}", "bright_green");
            terminal.WriteLine($"  - {Loc.Get("opening_story.tip_explore")}", "green");
            terminal.WriteLine($"  - {Loc.Get("opening_story.tip_equip")}", "green");
            terminal.WriteLine($"  - {Loc.Get("opening_story.tip_dungeon")}", "green");
            terminal.WriteLine($"  - {Loc.Get("opening_story.tip_seals")}", "green");
            terminal.WriteLine($"  - {Loc.Get("opening_story.tip_dreams")}", "cyan");
            terminal.WriteLine("");
            if (!GameConfig.ScreenReaderMode)
                terminal.WriteLine("  ─────────────────────────────────────────────────────────", "dark_cyan");
            terminal.WriteLine("");

            await terminal.GetInputAsync($"  {Loc.Get("opening_story.press_enter_journey")}");

            // Final transition
            terminal.Clear();
            terminal.WriteLine("");
            terminal.WriteLine("");
            terminal.WriteLine("", "white");
            terminal.WriteLine($"        {Loc.Get("opening_story.welcome")}", "bright_cyan");
            terminal.WriteLine("", "white");
            terminal.WriteLine($"        {Loc.Get("opening_story.may_you_find")}", "cyan");
            terminal.WriteLine($"        {Loc.Get("opening_story.remember_forgotten")}", "gray");
            terminal.WriteLine("");
            await SkippableDelay(3000);
        }

        /// <summary>
        /// Get a narrative description for the player's race
        /// </summary>
        private string GetRaceDescription(CharacterRace race)
        {
            return race switch
            {
                CharacterRace.Human => Loc.Get("opening_story.race_human"),
                CharacterRace.Elf => Loc.Get("opening_story.race_elf"),
                CharacterRace.Dwarf => Loc.Get("opening_story.race_dwarf"),
                CharacterRace.Hobbit => Loc.Get("opening_story.race_hobbit"),
                CharacterRace.HalfElf => Loc.Get("opening_story.race_halfelf"),
                CharacterRace.Orc => Loc.Get("opening_story.race_orc"),
                CharacterRace.Gnome => Loc.Get("opening_story.race_gnome"),
                CharacterRace.Troll => Loc.Get("opening_story.race_troll"),
                CharacterRace.Gnoll => Loc.Get("opening_story.race_gnoll"),
                CharacterRace.Mutant => Loc.Get("opening_story.race_mutant"),
                _ => Loc.Get("opening_story.race_unknown")
            };
        }

        /// <summary>
        /// Get a narrative description for the player's class
        /// </summary>
        private string GetClassDescription(CharacterClass charClass)
        {
            return charClass switch
            {
                CharacterClass.Warrior => Loc.Get("opening_story.class_warrior"),
                CharacterClass.Magician => Loc.Get("opening_story.class_magician"),
                CharacterClass.Assassin => Loc.Get("opening_story.class_assassin"),
                CharacterClass.Paladin => Loc.Get("opening_story.class_paladin"),
                CharacterClass.Ranger => Loc.Get("opening_story.class_ranger"),
                CharacterClass.Cleric => Loc.Get("opening_story.class_cleric"),
                CharacterClass.Barbarian => Loc.Get("opening_story.class_barbarian"),
                CharacterClass.Bard => Loc.Get("opening_story.class_bard"),
                CharacterClass.Jester => Loc.Get("opening_story.class_jester"),
                CharacterClass.Alchemist => Loc.Get("opening_story.class_alchemist"),
                CharacterClass.Sage => Loc.Get("opening_story.class_sage"),
                _ => Loc.Get("opening_story.class_unknown")
            };
        }

        /// <summary>
        /// Check if this is the player's first time (new game vs NG+)
        /// </summary>
        public bool IsFirstCycle()
        {
            return StoryProgressionSystem.Instance.CurrentCycle == 1;
        }

        /// <summary>
        /// Play the NG+ opening (different from first playthrough)
        /// </summary>
        public async Task PlayNewGamePlusOpening(Character player, TerminalEmulator terminal)
        {
            // Reset skip mode for this playthrough
            _skipMode = false;
            _terminal = terminal;

            terminal.Clear();
            terminal.WriteLine("");
            bool isLineInput2 = UsurperRemake.BBS.DoorMode.IsOnlineMode || UsurperRemake.BBS.DoorMode.IsInDoorMode;
            terminal.WriteLine($"  ({Loc.Get(isLineInput2 ? "opening_story.skip_hint_enter" : "opening_story.skip_hint")})", "dark_gray");
            await SkippableDelay(2000);

            terminal.Clear();
            await SkippableDelay(1000);

            int cycle = StoryProgressionSystem.Instance.CurrentCycle;

            terminal.WriteLine("");
            UIHelper.WriteBoxHeader(terminal, Loc.Get("opening_story.cycle_header", cycle), "bright_magenta", 67);
            terminal.WriteLine("");

            await SkippableDelay(1000);

            var ngPlusLines = new[]
            {
                (Loc.Get("opening_story.ngplus_gasp"), "white"),
                ("", ""),
                (Loc.Get("opening_story.ngplus_again"), "gray"),
                ("", ""),
                (Loc.Get("opening_story.ngplus_different"), "bright_yellow"),
                (Loc.Get("opening_story.ngplus_fragments"), "yellow"),
                (Loc.Get("opening_story.ngplus_echoes"), "cyan"),
                ("", ""),
                (Loc.Get("opening_story.ngplus_same_letter"), "gray"),
                (Loc.Get("opening_story.ngplus_notice"), "white"),
                (Loc.Get("opening_story.ngplus_second_page"), "bright_yellow"),
                ("", ""),
            };

            foreach (var (line, color) in ngPlusLines)
            {
                if (string.IsNullOrEmpty(line))
                {
                    terminal.WriteLine("");
                }
                else
                {
                    terminal.WriteLine($"  {line}", color);
                }
                await SkippableDelay(400);
            }

            // NG+ additional revelation
            if (!GameConfig.ScreenReaderMode)
            {
                terminal.WriteLine("  ┌─────────────────────────────────────────────────────────┐", "bright_magenta");
                terminal.WriteLine("  │                                                         │", "bright_magenta");
                terminal.WriteLine($"  │  {Loc.Get("opening_story.ngplus_not_first"),-55}│", "magenta");
                terminal.WriteLine($"  │  {Loc.Get("opening_story.ngplus_done_before"),-55}│", "magenta");
                terminal.WriteLine($"  │  {Loc.Get("opening_story.ngplus_again_again"),-55}│", "magenta");
                terminal.WriteLine("  │                                                         │", "bright_magenta");
                terminal.WriteLine($"  │  {Loc.Get("opening_story.ngplus_not_punishment"),-55}│", "bright_cyan");
                terminal.WriteLine($"  │  {Loc.Get("opening_story.ngplus_ocean"),-55}│", "bright_cyan");
                terminal.WriteLine("  │                                                         │", "bright_magenta");
                terminal.WriteLine($"  │  {Loc.Get("opening_story.ngplus_remember_more"),-55}│", "white");
                terminal.WriteLine($"  │  {Loc.Get("opening_story.ngplus_closer"),-55}│", "white");
                terminal.WriteLine("  │                                                         │", "bright_magenta");
                terminal.WriteLine($"  │  {Loc.Get("opening_story.ngplus_cycle_count", cycle),-55}│", "bright_yellow");
                terminal.WriteLine("  │                                                         │", "bright_magenta");
                terminal.WriteLine("  └─────────────────────────────────────────────────────────┘", "bright_magenta");
            }
            else
            {
                terminal.WriteLine($"  {Loc.Get("opening_story.ngplus_not_first")}", "magenta");
                terminal.WriteLine($"  {Loc.Get("opening_story.ngplus_done_before")}", "magenta");
                terminal.WriteLine($"  {Loc.Get("opening_story.ngplus_again_again")}", "magenta");
                terminal.WriteLine("", "bright_magenta");
                terminal.WriteLine($"  {Loc.Get("opening_story.ngplus_not_punishment")}", "bright_cyan");
                terminal.WriteLine($"  {Loc.Get("opening_story.ngplus_ocean")}", "bright_cyan");
                terminal.WriteLine("", "bright_magenta");
                terminal.WriteLine($"  {Loc.Get("opening_story.ngplus_remember_more")}", "white");
                terminal.WriteLine($"  {Loc.Get("opening_story.ngplus_closer")}", "white");
                terminal.WriteLine("", "bright_magenta");
                terminal.WriteLine($"  {Loc.Get("opening_story.ngplus_cycle_count", cycle)}", "bright_yellow");
            }

            terminal.WriteLine("");
            await SkippableDelay(1500);

            // NG+ bonuses reminder
            var bonuses = CycleSystem.Instance.GetCurrentCycleBonuses();
            if (bonuses != null && bonuses.Count > 0)
            {
                terminal.WriteLine("");
                terminal.WriteLine($"  {Loc.Get("opening_story.echoes_power")}", "bright_green");
                foreach (var bonus in bonuses)
                {
                    terminal.WriteLine($"    - {bonus}", "green");
                }
            }

            terminal.WriteLine("");
            await terminal.GetInputAsync($"  {Loc.Get("opening_story.press_enter_cycle")}");

            // Mark opening complete
            StoryProgressionSystem.Instance.SetStoryFlag("opening_complete", true);
            StoryProgressionSystem.Instance.SetStoryFlag($"cycle_{cycle}_started", true);
        }
    }
}
