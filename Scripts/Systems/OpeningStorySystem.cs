using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
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

        public OpeningStorySystem()
        {
            _instance = this;
        }

        /// <summary>
        /// Delay that can be skipped by pressing space bar
        /// </summary>
        private async Task SkippableDelay(int milliseconds)
        {
            if (_skipMode)
            {
                await Task.Delay(10); // Minimal delay even in skip mode for readability
                return;
            }

            // Check for space press during delay
            // In BBS door mode, Console.KeyAvailable is not available (stdin is redirected)
            bool canCheckConsole = !UsurperRemake.BBS.DoorMode.IsInDoorMode;

            int elapsed = 0;
            const int checkInterval = 50;

            while (elapsed < milliseconds)
            {
                if (canCheckConsole)
                {
                    try
                    {
                        if (Console.KeyAvailable)
                        {
                            var key = Console.ReadKey(true);
                            if (key.Key == ConsoleKey.Spacebar)
                            {
                                _skipMode = true;
                                return;
                            }
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        canCheckConsole = false; // Console not available, stop checking
                    }
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

            // Show skip hint
            terminal.Clear();
            terminal.WriteLine("");
            terminal.WriteLine("  (Press SPACE at any time to skip through text faster)", "dark_gray");
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
                ("...", "dark_gray"),
                ("", ""),
                ("You are drowning.", "gray"),
                ("", ""),
                ("Not in water, but in light.", "bright_cyan"),
                ("Endless, blinding, warm.", "bright_cyan"),
                ("", ""),
                ("A voice speaks - your voice, but ancient:", "white"),
                ("", ""),
                ("\"Remember...\"", "bright_yellow"),
                ("", ""),
                ("Remember what?", "gray"),
                ("", ""),
                ("\"Remember what you are.\"", "bright_yellow"),
                ("", ""),
                ("The light fades. Darkness takes you.", "gray"),
                ("And then...", "gray")
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
            terminal.WriteLine("╔═══════════════════════════════════════════════════════════════════╗", "dark_cyan");
            terminal.WriteLine("║                      THE AWAKENING                                ║", "dark_cyan");
            terminal.WriteLine("╚═══════════════════════════════════════════════════════════════════╝", "dark_cyan");
            terminal.WriteLine("");

            await SkippableDelay(1000);

            var sceneLines = new[]
            {
                ("You wake with a gasp.", "white"),
                ("", ""),
                ("Cold stone beneath you. The smell of damp hay.", "gray"),
                ("A dormitory - rows of empty beds stretching into shadow.", "gray"),
                ("", ""),
                ("Your head pounds. You try to remember...", "white"),
                ("", ""),
                ("Nothing.", "bright_red"),
                ("", ""),
                ("Your name. Your past. Your family.", "gray"),
                ("All of it - gone.", "gray"),
                ("", ""),
                ("Only fragments remain:", "white"),
                ("", ""),
                ($"  You are called {player.Name2}.", "bright_yellow"),
                ($"  You are {GetRaceDescription(player.Race)}.", "yellow"),
                ($"  You know how to {GetClassDescription(player.Class)}.", "yellow"),
                ("", ""),
                ("But who you WERE? Why you're HERE?", "white"),
                ("The answers slip away like water through fingers.", "cyan"),
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
            await terminal.GetInputAsync("  Press Enter to continue...");
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
                ("You stand, unsteady. Look around.", "white"),
                ("", ""),
                ("On a table by your bed: a scrap of parchment.", "gray"),
                ("The handwriting is unmistakably your own.", "bright_yellow"),
                ("But you don't remember writing it.", "white"),
                ("", ""),
                ("The message reads:", "gray"),
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
            terminal.WriteLine("  ┌─────────────────────────────────────────────────────────┐", "yellow");
            await SkippableDelay(200);
            terminal.WriteLine("  │                                                         │", "yellow");
            terminal.WriteLine("  │  To myself, if I survive:                               │", "yellow");
            await SkippableDelay(500);
            terminal.WriteLine("  │                                                         │", "yellow");
            terminal.WriteLine("  │  The gods are broken. Corrupted. Manwe has gone mad,    │", "yellow");
            await SkippableDelay(300);
            terminal.WriteLine("  │  and the Old Gods fight an endless war in his shadow.   │", "yellow");
            await SkippableDelay(300);
            terminal.WriteLine("  │                                                         │", "yellow");
            terminal.WriteLine("  │  There are SEVEN SEALS hidden in the dungeons below.    │", "yellow");
            await SkippableDelay(300);
            terminal.WriteLine("  │  Collect them. Break the cycle. End the suffering.      │", "yellow");
            await SkippableDelay(300);
            terminal.WriteLine("  │                                                         │", "yellow");
            terminal.WriteLine("  │  Trust no one. Especially not the Stranger.             │", "yellow");
            await SkippableDelay(300);
            terminal.WriteLine("  │                                                         │", "yellow");
            terminal.WriteLine("  │  And remember this:                                     │", "yellow");
            terminal.WriteLine("  │  You are not what you think you are.                    │", "bright_cyan");
            terminal.WriteLine("  │  You never were.                                        │", "bright_cyan");
            await SkippableDelay(500);
            terminal.WriteLine("  │                                                         │", "yellow");
            terminal.WriteLine("  │                                    - You (Before)       │", "gray");
            terminal.WriteLine("  │                                                         │", "yellow");
            terminal.WriteLine("  └─────────────────────────────────────────────────────────┘", "yellow");

            terminal.WriteLine("");
            await SkippableDelay(1000);

            terminal.WriteLine("");
            terminal.WriteLine("  The parchment crumbles to dust in your hands.", "gray");
            terminal.WriteLine("  But the words burn in your mind.", "white");

            terminal.WriteLine("");
            await terminal.GetInputAsync("  Press Enter to continue...");
        }

        /// <summary>
        /// Phase 4: The goal is established, player is released into the world
        /// </summary>
        private async Task PlayGoalReveal(Character player, TerminalEmulator terminal)
        {
            terminal.Clear();
            await SkippableDelay(500);

            terminal.WriteLine("");
            terminal.WriteLine("╔═══════════════════════════════════════════════════════════════════╗", "bright_cyan");
            terminal.WriteLine("║                     YOUR JOURNEY BEGINS                           ║", "bright_cyan");
            terminal.WriteLine("╚═══════════════════════════════════════════════════════════════════╝", "bright_cyan");
            terminal.WriteLine("");

            await SkippableDelay(500);

            var goalLines = new[]
            {
                ("You step out of the dormitory into the morning light.", "white"),
                ("", ""),
                ("The town of Dorashire spreads before you.", "gray"),
                ("An Inn. An Auction House. A Temple to gods you don't remember.", "gray"),
                ("And beyond the walls - the entrance to the Dungeons.", "bright_red"),
                ("", ""),
                ("Somewhere down there, in the darkness:", "white"),
                ("Seven Seals wait to be found.", "bright_yellow"),
                ("Old Gods wage their secret war.", "yellow"),
                ("And the truth of your identity lies buried.", "bright_cyan"),
                ("", ""),
                ("You don't know who erased your memory.", "gray"),
                ("You don't know why you wrote yourself that warning.", "gray"),
                ("You don't know what 'the cycle' means.", "gray"),
                ("", ""),
                ("But you know one thing:", "white"),
                ("", ""),
                ("You will find out.", "bright_white"),
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
            terminal.WriteLine("  ─────────────────────────────────────────────────────────", "dark_cyan");
            terminal.WriteLine("");
            terminal.WriteLine("  WHAT TO DO:", "bright_green");
            terminal.WriteLine("  - Explore the town and talk to NPCs", "green");
            terminal.WriteLine("  - Equip yourself at the shops", "green");
            terminal.WriteLine("  - Descend into the Dungeons to grow stronger", "green");
            terminal.WriteLine("  - Find the Seven Seals to unlock the true ending", "green");
            terminal.WriteLine("  - Pay attention to dreams and strange visions...", "cyan");
            terminal.WriteLine("");
            terminal.WriteLine("  ─────────────────────────────────────────────────────────", "dark_cyan");
            terminal.WriteLine("");

            await terminal.GetInputAsync("  Press Enter to begin your journey...");

            // Final transition
            terminal.Clear();
            terminal.WriteLine("");
            terminal.WriteLine("");
            terminal.WriteLine("", "white");
            terminal.WriteLine("        Welcome to the world of Usurper.", "bright_cyan");
            terminal.WriteLine("", "white");
            terminal.WriteLine("        May you find what you seek...", "cyan");
            terminal.WriteLine("        ...and remember what you've forgotten.", "gray");
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
                CharacterRace.Human => "a human, unremarkable yet adaptable",
                CharacterRace.Elf => "an elf, touched by ancient magic",
                CharacterRace.Dwarf => "a dwarf, with the strength of stone in your bones",
                CharacterRace.Hobbit => "a hobbit, small but surprisingly resilient",
                CharacterRace.HalfElf => "a half-elf, caught between two worlds",
                CharacterRace.Orc => "an orc, with savage strength coursing through your veins",
                CharacterRace.Gnome => "a gnome, clever and quick-witted",
                CharacterRace.Troll => "a troll, massive and nearly unkillable",
                CharacterRace.Gnoll => "a gnoll, with the cunning of a hyena",
                CharacterRace.Mutant => "a mutant, changed by forces unknown",
                _ => "of uncertain origin"
            };
        }

        /// <summary>
        /// Get a narrative description for the player's class
        /// </summary>
        private string GetClassDescription(CharacterClass charClass)
        {
            return charClass switch
            {
                CharacterClass.Warrior => "fight with blade and fury",
                CharacterClass.Magician => "wield arcane forces beyond mortal ken",
                CharacterClass.Assassin => "move unseen and strike from shadow",
                CharacterClass.Paladin => "channel divine power through righteous steel",
                CharacterClass.Ranger => "track prey through any terrain",
                CharacterClass.Cleric => "invoke the blessings of forgotten gods",
                CharacterClass.Barbarian => "crush enemies with primal rage",
                CharacterClass.Bard => "weave magic through song and story",
                CharacterClass.Jester => "confuse and confound your enemies",
                CharacterClass.Alchemist => "transmute the world to your will",
                CharacterClass.Sage => "unlock secrets long forgotten",
                _ => "survive by any means necessary"
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

            terminal.Clear();
            terminal.WriteLine("");
            terminal.WriteLine("  (Press SPACE at any time to skip through text faster)", "dark_gray");
            await SkippableDelay(2000);

            terminal.Clear();
            await SkippableDelay(1000);

            int cycle = StoryProgressionSystem.Instance.CurrentCycle;

            terminal.WriteLine("");
            terminal.WriteLine("╔═══════════════════════════════════════════════════════════════════╗", "bright_magenta");
            terminal.WriteLine($"║                      CYCLE {cycle}                                     ║", "bright_magenta");
            terminal.WriteLine("╚═══════════════════════════════════════════════════════════════════╝", "bright_magenta");
            terminal.WriteLine("");

            await SkippableDelay(1000);

            var ngPlusLines = new[]
            {
                ("You wake with a gasp.", "white"),
                ("", ""),
                ("The dormitory. Again.", "gray"),
                ("", ""),
                ("This time, something is different.", "bright_yellow"),
                ("Fragments of the past cycle cling to your mind.", "yellow"),
                ("Not memories, exactly. More like... echoes.", "cyan"),
                ("", ""),
                ("The letter on the table is the same.", "gray"),
                ("But now you notice something you missed before:", "white"),
                ("A second page, hidden beneath the first.", "bright_yellow"),
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
            terminal.WriteLine("  ┌─────────────────────────────────────────────────────────┐", "bright_magenta");
            terminal.WriteLine("  │                                                         │", "bright_magenta");
            terminal.WriteLine("  │  This is not the first time.                            │", "magenta");
            terminal.WriteLine("  │  You have done this before.                             │", "magenta");
            terminal.WriteLine("  │  Again and again.                                       │", "magenta");
            terminal.WriteLine("  │                                                         │", "bright_magenta");
            terminal.WriteLine("  │  The cycle is not punishment.                           │", "bright_cyan");
            terminal.WriteLine("  │  It is the ocean, learning to understand its waves.     │", "bright_cyan");
            terminal.WriteLine("  │                                                         │", "bright_magenta");
            terminal.WriteLine("  │  Each time, you remember a little more.                 │", "white");
            terminal.WriteLine("  │  Each time, you get closer to the truth.                │", "white");
            terminal.WriteLine("  │                                                         │", "bright_magenta");
            terminal.WriteLine($"  │  This is Cycle {cycle}. How many more until you wake?       │", "bright_yellow");
            terminal.WriteLine("  │                                                         │", "bright_magenta");
            terminal.WriteLine("  └─────────────────────────────────────────────────────────┘", "bright_magenta");

            terminal.WriteLine("");
            await SkippableDelay(1500);

            // NG+ bonuses reminder
            var bonuses = CycleSystem.Instance.GetCurrentCycleBonuses();
            if (bonuses != null && bonuses.Count > 0)
            {
                terminal.WriteLine("");
                terminal.WriteLine("  Echoes of past cycles grant you power:", "bright_green");
                foreach (var bonus in bonuses)
                {
                    terminal.WriteLine($"    - {bonus}", "green");
                }
            }

            terminal.WriteLine("");
            await terminal.GetInputAsync("  Press Enter to continue the cycle...");

            // Mark opening complete
            StoryProgressionSystem.Instance.SetStoryFlag("opening_complete", true);
            StoryProgressionSystem.Instance.SetStoryFlag($"cycle_{cycle}_started", true);
        }
    }
}
