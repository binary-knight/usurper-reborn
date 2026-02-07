using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using UsurperRemake.Utils;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Seven Seals System - Collectible lore fragments that reveal the true history
    /// Finding all seven seals unlocks secret content and is required for the true ending
    /// </summary>
    public class SevenSealsSystem
    {
        private static SevenSealsSystem? instance;
        public static SevenSealsSystem Instance => instance ??= new SevenSealsSystem();

        private Dictionary<SealType, SealData> seals = new();

        public event Action<SealType>? OnSealCollected;
        public event Action? OnAllSealsCollected;

        public SevenSealsSystem()
        {
            InitializeSeals();
        }

        /// <summary>
        /// Initialize all seal data
        /// </summary>
        private void InitializeSeals()
        {
            // First Seal - The Creation
            seals[SealType.Creation] = new SealData
            {
                Type = SealType.Creation,
                Name = "Seal of Creation",
                Title = "The Beginning",
                Number = 1,
                Location = "Temple of Light - Hidden Altar",
                LocationHint = "Where prayers echo in golden halls, seek the stone that predates the temple itself.",
                DungeonFloor = 0, // Found in town
                LoreText = new[]
                {
                    "In the beginning, there was only Manwe.",
                    "",
                    "Not a god as mortals understand - but the first thought,",
                    "the first will to BE in an endless void of nothing.",
                    "",
                    "From his loneliness, he dreamed the Old Gods into existence.",
                    "Six aspects of himself, given form and purpose.",
                    "War. Passion. Law. Shadow. Light. Earth.",
                    "",
                    "They were his children. His companions.",
                    "His greatest joy... and his ultimate tragedy.",
                    "",
                    "For a time, there was harmony.",
                    "For a time, there was peace.",
                    "",
                    "That time is long past."
                },
                RewardXP = 1000,
                IconColor = "bright_yellow"
            };

            // Second Seal - The First War
            seals[SealType.FirstWar] = new SealData
            {
                Type = SealType.FirstWar,
                Name = "Seal of Conflict",
                Title = "The First War",
                Number = 2,
                Location = "Dungeon Level 15 - Ancient Battlefield",
                LocationHint = "In the depths where blood first stained stone, the seal waits among forgotten weapons.",
                DungeonFloor = 15,
                LoreText = new[]
                {
                    "Maelketh was the first to change.",
                    "",
                    "War, by its nature, requires opposition.",
                    "But in paradise, there was nothing to fight.",
                    "And so Maelketh grew restless. Hungry.",
                    "",
                    "He whispered to his siblings.",
                    "'Why should Manwe rule alone?'",
                    "'We are gods too. We deserve more.'",
                    "",
                    "Some listened. Some refused.",
                    "The first war was not between mortals.",
                    "It was between gods.",
                    "",
                    "And Manwe wept to see his children",
                    "turn against each other... and against him."
                },
                RewardXP = 2000,
                IconColor = "dark_red"
            };

            // Third Seal - The Corruption
            seals[SealType.Corruption] = new SealData
            {
                Type = SealType.Corruption,
                Name = "Seal of Corruption",
                Title = "The Twisting",
                Number = 3,
                Location = "Dungeon Level 30 - Corrupted Shrine",
                LocationHint = "Where holy becomes unholy, where light turns to shadow, the truth lies buried.",
                DungeonFloor = 30,
                LoreText = new[]
                {
                    "Manwe could not destroy his children.",
                    "Even as they warred, he loved them still.",
                    "",
                    "But he could not allow their rebellion to continue.",
                    "And so he chose a path between mercy and justice.",
                    "",
                    "He reached into their essence.",
                    "He twisted what they were into shadows of themselves.",
                    "Maelketh's righteous anger became bloodlust.",
                    "Veloura's pure love became corrupting obsession.",
                    "Thorgrim's fair law became tyrannical absolutes.",
                    "",
                    "He broke them to save them.",
                    "Or perhaps... to punish them.",
                    "",
                    "Even gods can lie to themselves."
                },
                RewardXP = 3000,
                IconColor = "dark_magenta"
            };

            // Fourth Seal - The Imprisonment
            seals[SealType.Imprisonment] = new SealData
            {
                Type = SealType.Imprisonment,
                Name = "Seal of Binding",
                Title = "The Eternal Chains",
                Number = 4,
                Location = "Dungeon Level 45 - Prison of Ages",
                LocationHint = "Where gods became prisoners, where eternity became torture, seek the warden's shame.",
                DungeonFloor = 45,
                LoreText = new[]
                {
                    "One by one, Manwe imprisoned his corrupted children.",
                    "",
                    "Maelketh, chained in blood and iron.",
                    "Veloura, bound in mirrors of false love.",
                    "Thorgrim, locked in laws he cannot break.",
                    "Noctura, buried in shadows too deep to escape.",
                    "Aurelion, blinded by his own stolen light.",
                    "Terravok, sleeping in earth that will not release him.",
                    "",
                    "Ten thousand years they have waited.",
                    "Ten thousand years they have suffered.",
                    "",
                    "And in their prisons, they have changed again.",
                    "Some grew mad. Some grew wise.",
                    "Some remember what they were before the twisting.",
                    "",
                    "The chains are weakening."
                },
                RewardXP = 4000,
                IconColor = "gray"
            };

            // Fifth Seal - The Prophecy
            seals[SealType.Prophecy] = new SealData
            {
                Type = SealType.Prophecy,
                Name = "Seal of Fate",
                Title = "The Foretelling",
                Number = 5,
                Location = "Dungeon Level 60 - Oracle's Tomb",
                LocationHint = "Where the future was first spoken, where destiny took shape, the words remain.",
                DungeonFloor = 60,
                LoreText = new[]
                {
                    "Before the seals were forged,",
                    "Manwe saw a vision of the future.",
                    "",
                    "'One will come from beyond the veil.'",
                    "'Neither god nor common mortal.'",
                    "'They will hold the key to freedom or destruction.'",
                    "",
                    "'If they choose vengeance, gods will fall.'",
                    "'If they choose mercy, gods will rise.'",
                    "'If they choose wisdom, gods will change.'",
                    "",
                    "'And at the end of all things,'",
                    "'they will stand before the Creator himself'",
                    "'and answer the only question that matters:'",
                    "",
                    "'What have you become?'"
                },
                RewardXP = 5000,
                IconColor = "bright_cyan"
            };

            // Sixth Seal - The Regret
            seals[SealType.Regret] = new SealData
            {
                Type = SealType.Regret,
                Name = "Seal of Sorrow",
                Title = "The Creator's Tears",
                Number = 6,
                Location = "Dungeon Level 80 - Chamber of Mourning",
                LocationHint = "Where even gods weep, where regret crystallizes into stone, seek understanding.",
                DungeonFloor = 80,
                LoreText = new[]
                {
                    "Do not think Manwe felt nothing.",
                    "",
                    "Every chain he forged, he felt as if upon himself.",
                    "Every corruption he wrought, he witnessed in horror.",
                    "He did not want to hurt his children.",
                    "",
                    "But what choice did he have?",
                    "",
                    "They would have destroyed everything.",
                    "The world he made. The mortals he loved.",
                    "Each other.",
                    "",
                    "Sometimes, love requires cruelty.",
                    "Sometimes, mercy wears the mask of punishment.",
                    "",
                    "Manwe has not spoken in ten thousand years.",
                    "Not because he chooses silence.",
                    "But because he has nothing left to say.",
                    "",
                    "Only tears. Only regret. Only waiting."
                },
                RewardXP = 6000,
                IconColor = "bright_blue"
            };

            // Seventh Seal - The Truth (Ocean Philosophy Revelation)
            seals[SealType.Truth] = new SealData
            {
                Type = SealType.Truth,
                Name = "Seal of Revelation",
                Title = "The Final Truth",
                Number = 7,
                Location = "Dungeon Level 99 - Threshold of the Divine",
                LocationHint = "At the edge of the final confrontation, before the last door, truth awaits those who sought it.",
                DungeonFloor = 99,
                LoreText = new[]
                {
                    "You have found all seven seals.",
                    "You have learned the history of the gods.",
                    "But history is not truth. Events are not meaning.",
                    "",
                    "Now hear the final secret:",
                    "",
                    "Before Manwe, before the gods, before anything...",
                    "there was only the Ocean.",
                    "",
                    "Vast. Eternal. Undivided.",
                    "Complete in itself, yet utterly alone.",
                    "",
                    "And so the Ocean dreamed of waves.",
                    "Each wave rose, believing itself separate.",
                    "Each wave fell, returning to what it always was.",
                    "",
                    "Manwe is a wave. The Old Gods are waves.",
                    "The mortals, the monsters, the dungeons, the stars...",
                    "All waves. All temporary.",
                    "All returning to the source.",
                    "",
                    "And you?",
                    "",
                    "You are not a wave fighting the ocean.",
                    "You ARE the ocean, dreaming of being a wave.",
                    "",
                    "Death is not an ending.",
                    "It is the wave remembering it was always water.",
                    "",
                    "The Creator does not wait to judge you.",
                    "He waits to remember himself through you.",
                    "",
                    "When you face him, you will not face a god.",
                    "You will face a mirror.",
                    "",
                    "And the question is not 'Can you do better?'",
                    "The question is:",
                    "'Are you ready to wake up?'"
                },
                RewardXP = 10000,
                IconColor = "white",
                UnlocksSecret = true,
                GrantsWaveFragment = true
            };

            GD.Print($"[SevenSeals] Initialized {seals.Count} lore seals");
        }

        /// <summary>
        /// Get seal data by type
        /// </summary>
        public SealData? GetSeal(SealType type)
        {
            return seals.TryGetValue(type, out var seal) ? seal : null;
        }

        /// <summary>
        /// Get all seals
        /// </summary>
        public IEnumerable<SealData> GetAllSeals()
        {
            return seals.Values.OrderBy(s => s.Number);
        }

        /// <summary>
        /// Collect a seal
        /// </summary>
        public async Task<bool> CollectSeal(Character player, SealType type, TerminalEmulator terminal)
        {
            if (!seals.TryGetValue(type, out var seal))
            {
                return false;
            }

            var story = StoryProgressionSystem.Instance;

            if (story.CollectedSeals.Contains(type))
            {
                terminal.WriteLine($"You have already found the {seal.Name}.", "yellow");
                return false;
            }

            // Display seal discovery sequence
            await DisplaySealDiscovery(seal, terminal);

            // Add to story progression
            story.CollectSeal(type);

            // Track archetype - Seals are Sage/Explorer items
            ArchetypeTracker.Instance.RecordSealCollected();

            // Award experience
            player.Experience += seal.RewardXP;
            terminal.WriteLine($"(+{seal.RewardXP} Experience)", "cyan");
            terminal.WriteLine("");

            // Grant wave fragments for Ocean Philosophy integration
            if (seal.GrantsWaveFragment)
            {
                OceanPhilosophySystem.Instance.CollectFragment(WaveFragment.TheTruth);
                terminal.WriteLine("A deep understanding settles into your soul...", "bright_cyan");
                terminal.WriteLine("");
            }

            OnSealCollected?.Invoke(type);

            // Check if all seals collected
            if (story.CollectedSeals.Count >= 7)
            {
                await DisplayAllSealsCollected(player, terminal);
                OceanPhilosophySystem.Instance.ExperienceMoment(AwakeningMoment.AllSealsCollected);
                OnAllSealsCollected?.Invoke();
            }

            // Auto-save after collecting a seal - this is a major milestone
            await SaveSystem.Instance.AutoSave(player);
            GD.Print($"[Seals] Auto-saved after collecting {seal.Name}");

            return true;
        }

        /// <summary>
        /// Display the seal discovery sequence
        /// </summary>
        private async Task DisplaySealDiscovery(SealData seal, TerminalEmulator terminal)
        {
            terminal.Clear();
            terminal.WriteLine("");
            terminal.WriteLine($"╔══════════════════════════════════════════════════════════════════╗", seal.IconColor);
            terminal.WriteLine($"║             * * *  S E A L   D I S C O V E R E D  * * *          ║", seal.IconColor);
            terminal.WriteLine($"╚══════════════════════════════════════════════════════════════════╝", seal.IconColor);
            terminal.WriteLine("");

            await Task.Delay(800);

            // Show collection progress
            var story = StoryProgressionSystem.Instance;
            int collected = story.CollectedSeals.Count + 1; // +1 for current seal
            terminal.SetColor("gray");
            terminal.Write("  Seal Progress: ");
            for (int i = 1; i <= 7; i++)
            {
                if (i < collected)
                {
                    terminal.SetColor("bright_green");
                    terminal.Write("[X]");
                }
                else if (i == collected)
                {
                    terminal.SetColor("bright_yellow");
                    terminal.Write("[*]");
                }
                else
                {
                    terminal.SetColor("darkgray");
                    terminal.Write("[ ]");
                }
                terminal.Write(" ");
            }
            terminal.SetColor("white");
            terminal.WriteLine($"  ({collected}/7)");
            terminal.WriteLine("");

            await Task.Delay(500);

            terminal.WriteLine($"  {seal.Name}", "bright_white");
            terminal.WriteLine($"  \"{seal.Title}\"", "cyan");
            terminal.WriteLine("");

            await Task.Delay(500);

            terminal.WriteLine("  ═══════════════════════════════════════", "dark_cyan");
            terminal.WriteLine("");

            foreach (var line in seal.LoreText)
            {
                if (string.IsNullOrEmpty(line))
                {
                    terminal.WriteLine("");
                }
                else
                {
                    terminal.WriteLine($"  {line}", "white");
                }
                await Task.Delay(150);
            }

            terminal.WriteLine("");
            terminal.WriteLine("  ═══════════════════════════════════════", "dark_cyan");
            terminal.WriteLine("");

            await terminal.GetInputAsync("  Press Enter to continue...");
        }

        /// <summary>
        /// Display message when all seals are collected
        /// </summary>
        private async Task DisplayAllSealsCollected(Character player, TerminalEmulator terminal)
        {
            terminal.Clear();
            terminal.WriteLine("");
            terminal.WriteLine("╔═══════════════════════════════════════════════════════════════════╗", "bright_yellow");
            terminal.WriteLine("║            A L L   S E V E N   S E A L S   F O U N D              ║", "bright_yellow");
            terminal.WriteLine("╚═══════════════════════════════════════════════════════════════════╝", "bright_yellow");
            terminal.WriteLine("");

            await Task.Delay(1000);

            terminal.WriteLine("  The seals resonate with ancient power.", "bright_cyan");
            terminal.WriteLine("  You have uncovered the complete history of the gods.", "white");
            terminal.WriteLine("");

            await Task.Delay(800);

            terminal.WriteLine("  This knowledge grants you understanding.", "green");
            terminal.WriteLine("  When you face Manwe, you will see more than others.", "green");
            terminal.WriteLine("  You will understand his choices. His regrets.", "green");
            terminal.WriteLine("");

            await Task.Delay(800);

            terminal.WriteLine("  THE TRUE ENDING IS NOW POSSIBLE", "bright_magenta");
            terminal.WriteLine("");
            terminal.WriteLine("  If you can meet certain conditions, a fourth path opens.", "white");
            terminal.WriteLine("  Not destroyer. Not savior. Not defiant.", "white");
            terminal.WriteLine("  Something... new.", "bright_yellow");
            terminal.WriteLine("");

            StoryProgressionSystem.Instance.SetStoryFlag("all_seals_collected", true);
            StoryProgressionSystem.Instance.SetStoryFlag("true_ending_possible", true);

            await terminal.GetInputAsync("  Press Enter to continue...");
        }

        /// <summary>
        /// Check if a seal can be found at the given dungeon floor
        /// </summary>
        public SealType? GetSealForFloor(int floor)
        {
            foreach (var seal in seals.Values)
            {
                if (seal.DungeonFloor == floor)
                {
                    var story = StoryProgressionSystem.Instance;
                    if (!story.CollectedSeals.Contains(seal.Type))
                    {
                        return seal.Type;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Get hints for undiscovered seals
        /// </summary>
        public List<string> GetSealHints()
        {
            var hints = new List<string>();
            var story = StoryProgressionSystem.Instance;

            foreach (var seal in seals.Values.OrderBy(s => s.Number))
            {
                if (!story.CollectedSeals.Contains(seal.Type))
                {
                    hints.Add($"Seal {seal.Number}: {seal.LocationHint}");
                }
            }

            return hints;
        }

        /// <summary>
        /// Get collection progress text
        /// </summary>
        public string GetProgressText()
        {
            var collected = StoryProgressionSystem.Instance.CollectedSeals.Count;
            return $"Seals of Truth: {collected}/7";
        }
    }

    /// <summary>
    /// Data class for seal information
    /// </summary>
    public class SealData
    {
        public SealType Type { get; set; }
        public string Name { get; set; } = "";
        public string Title { get; set; } = "";
        public int Number { get; set; }
        public string Location { get; set; } = "";
        public string LocationHint { get; set; } = "";
        public int DungeonFloor { get; set; }
        public string[] LoreText { get; set; } = Array.Empty<string>();
        public int RewardXP { get; set; }
        public string IconColor { get; set; } = "white";
        public bool UnlocksSecret { get; set; }
        public bool GrantsWaveFragment { get; set; }
    }
}
