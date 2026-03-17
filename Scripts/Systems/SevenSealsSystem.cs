using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UsurperRemake.UI;
using UsurperRemake.Utils;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Seven Seals System - Collectible lore fragments that reveal the true history
    /// Finding all seven seals unlocks secret content and is required for the true ending
    /// </summary>
    public class SevenSealsSystem
    {
        private static SevenSealsSystem? _fallbackInstance;
        public static SevenSealsSystem Instance
        {
            get
            {
                var ctx = UsurperRemake.Server.SessionContext.Current;
                if (ctx != null) return ctx.SevenSeals;
                return _fallbackInstance ??= new SevenSealsSystem();
            }
        }

        private Dictionary<SealType, SealData> seals = new();

        public event Action<SealType>? OnSealCollected;
        public event Action? OnAllSealsCollected;

        public SevenSealsSystem()
        {
            InitializeSeals();
        }

        /// <summary>
        /// Get localized lore text for a seal
        /// </summary>
        private string[] GetLocalizedLore(string sealId, int totalLines)
        {
            var lines = new string[totalLines];
            for (int i = 0; i < totalLines; i++)
            {
                lines[i] = Loc.Get($"seals.{sealId}.lore.{i}");
            }
            return lines;
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
                Name = Loc.Get("seals.creation.name"),
                Title = Loc.Get("seals.creation.title"),
                Number = 1,
                Location = Loc.Get("seals.creation.location"),
                LocationHint = Loc.Get("seals.creation.hint"),
                DungeonFloor = 0, // Found in town
                LoreText = GetLocalizedLore("creation", 16),
                RewardXP = 1000,
                IconColor = "bright_yellow"
            };

            // Second Seal - The First War
            seals[SealType.FirstWar] = new SealData
            {
                Type = SealType.FirstWar,
                Name = Loc.Get("seals.first_war.name"),
                Title = Loc.Get("seals.first_war.title"),
                Number = 2,
                Location = Loc.Get("seals.first_war.location"),
                LocationHint = Loc.Get("seals.first_war.hint"),
                DungeonFloor = 15,
                LoreText = GetLocalizedLore("first_war", 16),
                RewardXP = 2000,
                IconColor = "dark_red"
            };

            // Third Seal - The Corruption
            seals[SealType.Corruption] = new SealData
            {
                Type = SealType.Corruption,
                Name = Loc.Get("seals.corruption.name"),
                Title = Loc.Get("seals.corruption.title"),
                Number = 3,
                Location = Loc.Get("seals.corruption.location"),
                LocationHint = Loc.Get("seals.corruption.hint"),
                DungeonFloor = 30,
                LoreText = GetLocalizedLore("corruption", 16),
                RewardXP = 3000,
                IconColor = "dark_magenta"
            };

            // Fourth Seal - The Imprisonment
            seals[SealType.Imprisonment] = new SealData
            {
                Type = SealType.Imprisonment,
                Name = Loc.Get("seals.imprisonment.name"),
                Title = Loc.Get("seals.imprisonment.title"),
                Number = 4,
                Location = Loc.Get("seals.imprisonment.location"),
                LocationHint = Loc.Get("seals.imprisonment.hint"),
                DungeonFloor = 45,
                LoreText = GetLocalizedLore("imprisonment", 17),
                RewardXP = 4000,
                IconColor = "gray"
            };

            // Fifth Seal - The Prophecy
            seals[SealType.Prophecy] = new SealData
            {
                Type = SealType.Prophecy,
                Name = Loc.Get("seals.prophecy.name"),
                Title = Loc.Get("seals.prophecy.title"),
                Number = 5,
                Location = Loc.Get("seals.prophecy.location"),
                LocationHint = Loc.Get("seals.prophecy.hint"),
                DungeonFloor = 60,
                LoreText = GetLocalizedLore("prophecy", 16),
                RewardXP = 5000,
                IconColor = "bright_cyan"
            };

            // Sixth Seal - The Regret
            seals[SealType.Regret] = new SealData
            {
                Type = SealType.Regret,
                Name = Loc.Get("seals.regret.name"),
                Title = Loc.Get("seals.regret.title"),
                Number = 6,
                Location = Loc.Get("seals.regret.location"),
                LocationHint = Loc.Get("seals.regret.hint"),
                DungeonFloor = 80,
                LoreText = GetLocalizedLore("regret", 20),
                RewardXP = 6000,
                IconColor = "bright_blue"
            };

            // Seventh Seal - The Truth (Ocean Philosophy Revelation)
            seals[SealType.Truth] = new SealData
            {
                Type = SealType.Truth,
                Name = Loc.Get("seals.truth.name"),
                Title = Loc.Get("seals.truth.title"),
                Number = 7,
                Location = Loc.Get("seals.truth.location"),
                LocationHint = Loc.Get("seals.truth.hint"),
                DungeonFloor = 99,
                LoreText = GetLocalizedLore("truth", 38),
                RewardXP = 10000,
                IconColor = "white",
                UnlocksSecret = true,
                GrantsWaveFragment = true
            };

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
                terminal.WriteLine(Loc.Get("seals.already_found", seal.Name), "yellow");
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
            terminal.WriteLine(Loc.Get("seals.xp_gained", seal.RewardXP), "cyan");
            terminal.WriteLine("");

            // Grant wave fragments for Ocean Philosophy integration
            if (seal.GrantsWaveFragment)
            {
                OceanPhilosophySystem.Instance.CollectFragment(WaveFragment.TheTruth);
                terminal.WriteLine(Loc.Get("seals.deep_understanding"), "bright_cyan");
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

            return true;
        }

        /// <summary>
        /// Display the seal discovery sequence
        /// </summary>
        private async Task DisplaySealDiscovery(SealData seal, TerminalEmulator terminal)
        {
            terminal.Clear();
            terminal.WriteLine("");
            UIHelper.WriteBoxHeader(terminal, Loc.Get("seals.header_discovered"), seal.IconColor, 66);
            terminal.WriteLine("");

            await Task.Delay(800);

            // Show collection progress — each slot maps to a specific seal, not ordinal count
            var story = StoryProgressionSystem.Instance;
            int collected = story.CollectedSeals.Count + 1; // +1 for current seal
            var sealOrder = new[] { SealType.Creation, SealType.FirstWar, SealType.Corruption,
                SealType.Imprisonment, SealType.Prophecy, SealType.Regret, SealType.Truth };
            terminal.SetColor("gray");
            terminal.Write($"  {Loc.Get("seals.progress")}: ");
            for (int i = 0; i < 7; i++)
            {
                if (sealOrder[i] == seal.Type)
                {
                    // This is the seal being collected right now
                    terminal.SetColor("bright_yellow");
                    terminal.Write("[*]");
                }
                else if (story.CollectedSeals.Contains(sealOrder[i]))
                {
                    terminal.SetColor("bright_green");
                    terminal.Write("[X]");
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

            if (!GameConfig.ScreenReaderMode)
                terminal.WriteLine("  ═══════════════════════════════════════", "dark_cyan");
            terminal.WriteLine("");

            foreach (var line in seal.LoreText)
            {
                string displayLine = line;

                // Seal 7 lore starts with "You have found all seven seals" — fix when not all collected
                if (seal.Type == SealType.Truth && line == Loc.Get("seals.truth.lore.0") && collected < 7)
                {
                    displayLine = Loc.Get("seals.lore_alt_final", 7 - collected);
                }

                if (string.IsNullOrEmpty(displayLine))
                {
                    terminal.WriteLine("");
                }
                else
                {
                    terminal.WriteLine($"  {displayLine}", "white");
                }
                await Task.Delay(150);
            }

            terminal.WriteLine("");
            if (!GameConfig.ScreenReaderMode)
                terminal.WriteLine("  ═══════════════════════════════════════", "dark_cyan");
            terminal.WriteLine("");

            await terminal.GetInputAsync($"  {Loc.Get("ui.press_enter")}");
        }

        /// <summary>
        /// Display message when all seals are collected
        /// </summary>
        private async Task DisplayAllSealsCollected(Character player, TerminalEmulator terminal)
        {
            terminal.Clear();
            terminal.WriteLine("");
            UIHelper.WriteBoxHeader(terminal, Loc.Get("seals.header_all_found"), "bright_yellow", 67);
            terminal.WriteLine("");

            await Task.Delay(1000);

            terminal.WriteLine($"  {Loc.Get("seals.resonate_power")}", "bright_cyan");
            terminal.WriteLine($"  {Loc.Get("seals.uncovered_history")}", "white");
            terminal.WriteLine("");

            await Task.Delay(800);

            terminal.WriteLine($"  {Loc.Get("seals.knowledge_understanding")}", "green");
            terminal.WriteLine($"  {Loc.Get("seals.face_manwe_see_more")}", "green");
            terminal.WriteLine($"  {Loc.Get("seals.understand_choices")}", "green");
            terminal.WriteLine("");

            await Task.Delay(800);

            terminal.WriteLine($"  {Loc.Get("seals.true_ending_possible")}", "bright_magenta");
            terminal.WriteLine("");
            terminal.WriteLine($"  {Loc.Get("seals.fourth_path")}", "white");
            terminal.WriteLine($"  {Loc.Get("seals.not_destroyer")}", "white");
            terminal.WriteLine($"  {Loc.Get("seals.something_new")}", "bright_yellow");
            terminal.WriteLine("");

            StoryProgressionSystem.Instance.SetStoryFlag("all_seals_collected", true);
            StoryProgressionSystem.Instance.SetStoryFlag("true_ending_possible", true);

            await terminal.GetInputAsync($"  {Loc.Get("ui.press_enter")}");
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
                    hints.Add(Loc.Get("seals.hint_prefix", seal.Number, seal.LocationHint));
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
            return Loc.Get("seals.progress_text", collected);
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
