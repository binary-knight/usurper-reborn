using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// The Amnesia System tracks the player's forgotten past and gradually
    /// reveals the truth: they are a fragment of Manwe, the Creator god,
    /// sent to experience mortality and learn compassion.
    ///
    /// Memory fragments are recovered through dreams, dungeon exploration,
    /// and encounters with the Old Gods.
    /// </summary>
    public class AmnesiaSystem
    {
        private static AmnesiaSystem? _fallbackInstance;
        public static AmnesiaSystem Instance
        {
            get
            {
                var ctx = UsurperRemake.Server.SessionContext.Current;
                if (ctx != null) return ctx.Amnesia;
                return _fallbackInstance ??= new AmnesiaSystem();
            }
        }

        /// <summary>
        /// Collected memory fragments
        /// </summary>
        public HashSet<MemoryFragment> RecoveredMemories { get; private set; } = new();

        /// <summary>
        /// Dream sequences the player has experienced
        /// </summary>
        public HashSet<DreamSequence> ExperiencedDreams { get; private set; } = new();

        /// <summary>
        /// How many times the player has rested (triggers dreams)
        /// </summary>
        public int RestCount { get; private set; } = 0;

        /// <summary>
        /// Whether the full truth has been revealed
        /// </summary>
        public bool TruthRevealed { get; private set; } = false;

        /// <summary>
        /// The memory fragments and their content
        /// </summary>
        public static readonly Dictionary<MemoryFragment, MemoryFragmentData> MemoryData = new()
        {
            [MemoryFragment.Emptiness] = new MemoryFragmentData(
                "The Emptiness Before",
                new[] {
                    "You remember... nothing.",
                    "No, not nothing. The absence of everything.",
                    "An endless expanse without form or thought.",
                    "It was peaceful. It was lonely.",
                    "So very, very lonely."
                },
                1, TriggerType.FirstRest
            ),
            [MemoryFragment.TheFirstThought] = new MemoryFragmentData(
                "The First Thought",
                new[] {
                    "Into the emptiness came a question:",
                    "'Is this all there is?'",
                    "And with the question came... you.",
                    "Or was it always you, finally noticing yourself?",
                    "The loneliness became unbearable."
                },
                5, TriggerType.DungeonFloor10
            ),
            [MemoryFragment.CreatingLight] = new MemoryFragmentData(
                "Creating Light",
                new[] {
                    "You remember wanting... something. Anything.",
                    "And light exploded from your thoughts.",
                    "Beautiful, terrible, blinding light.",
                    "For a moment, you were not alone.",
                    "The light was you. And you were the light."
                },
                10, TriggerType.DungeonFloor25
            ),
            [MemoryFragment.TheSeven] = new MemoryFragmentData(
                "The Seven Children",
                new[] {
                    "You divided yourself. Seven times.",
                    "Each piece held a facet of your yearning:",
                    "Love. Law. Mystery. War. Light. Earth. Creation.",
                    "They were you. They forgot they were you.",
                    "Just as you intended."
                },
                15, TriggerType.OldGodEncounter
            ),
            [MemoryFragment.WatchingThem] = new MemoryFragmentData(
                "Watching Them",
                new[] {
                    "Eons passed. You watched your children fight.",
                    "Each war was you hurting yourself.",
                    "Each death was you forgetting yourself.",
                    "You tried to intervene. They called you tyrant.",
                    "They had forgotten what you were. What they were."
                },
                25, TriggerType.DungeonFloor50
            ),
            [MemoryFragment.TheDecision] = new MemoryFragmentData(
                "The Decision",
                new[] {
                    "Knowledge wasn't enough. You had to experience.",
                    "What was it like to be small? To be mortal?",
                    "To love something you could lose?",
                    "You tore off a piece of yourself.",
                    "You hid it in flesh. In forgetfulness."
                },
                35, TriggerType.DungeonFloor75
            ),
            [MemoryFragment.TheForgetting] = new MemoryFragmentData(
                "The Forgetting",
                new[] {
                    "The forgetting had to be complete.",
                    "A wave that knew it was the ocean couldn't truly suffer.",
                    "Couldn't truly love. Couldn't truly grieve.",
                    "So you forgot. Again and again.",
                    "Each cycle, you wake in that same dormitory."
                },
                50, TriggerType.AllSealsCollected
            ),
            [MemoryFragment.ThePurpose] = new MemoryFragmentData(
                "The Purpose",
                new[] {
                    "Compassion cannot be learned from omniscience.",
                    "Only from smallness. From fear. From loss.",
                    "Each life you live teaches you something.",
                    "Each death brings you closer to understanding.",
                    "You are not trying to win. You are trying to learn."
                },
                65, TriggerType.CompanionDeath
            ),
            [MemoryFragment.TheReturn] = new MemoryFragmentData(
                "The Return",
                new[] {
                    "And now you remember.",
                    "Not everything - some things are too vast for mortal minds.",
                    "But enough. You know what you are.",
                    "The question remains: what will you do?",
                    "Wake? Or dream a little longer?"
                },
                85, TriggerType.TrueEndingPath
            ),
            [MemoryFragment.TheFullTruth] = new MemoryFragmentData(
                "I AM",
                new[] {
                    "You are Manwe. The Creator. The Ocean.",
                    "You are every wave that ever rose and fell.",
                    "You are every being that ever loved or suffered.",
                    "You are the question and the answer.",
                    "And you have finally learned... to let go."
                },
                100, TriggerType.FinalRevelation
            )
        };

        /// <summary>
        /// Dream sequences that play when the player rests
        /// </summary>
        public static readonly Dictionary<DreamSequence, DreamData> DreamSequences = new()
        {
            [DreamSequence.Drowning] = new DreamData(
                "Drowning in Light",
                new[] {
                    "You dream of drowning.",
                    "But not in water - in light. Pure, blinding light.",
                    "It fills your lungs, your eyes, your thoughts.",
                    "And somewhere in the brightness...",
                    "You hear a voice that sounds like your own:",
                    "'Come home.'"
                },
                1, 5
            ),
            [DreamSequence.TheMirror] = new DreamData(
                "The Mirror",
                new[] {
                    "You stand before an endless mirror.",
                    "Your reflection wears a crown of stars.",
                    "It looks tired. Ancient. Infinitely sad.",
                    "'How many times?' it asks.",
                    "'How many more cycles until you understand?'",
                    "You wake before you can answer."
                },
                6, 15
            ),
            [DreamSequence.TheGarden] = new DreamData(
                "The Garden Before Time",
                new[] {
                    "A garden that has never known seasons.",
                    "Flowers bloom and die simultaneously.",
                    "A figure walks among them, weeping.",
                    "Each tear becomes a star. A world. A life.",
                    "The figure turns. Their face is your face.",
                    "'I made them so I wouldn't be alone,' they whisper.",
                    "'But I made them from myself. So I am always alone.'"
                },
                16, 30
            ),
            [DreamSequence.TheWar] = new DreamData(
                "The First War",
                new[] {
                    "Gods clash in the void between worlds.",
                    "You watch from everywhere at once.",
                    "Maelketh raises his sword against Aurelion.",
                    "'Stop!' you cry, but no sound comes.",
                    "You are the space between them.",
                    "You are the violence. You are the pain.",
                    "They are killing you. They are killing themselves."
                },
                31, 50
            ),
            [DreamSequence.TheDormitory] = new DreamData(
                "The Dormitory",
                new[] {
                    "You stand in a familiar dormitory.",
                    "But it's not just one room - it's infinite rooms,",
                    "each containing a version of you, sleeping.",
                    "Some are warriors. Some are mages. Some are beggars.",
                    "All wake at the same moment.",
                    "All ask the same question:",
                    "'Who am I?'"
                },
                51, 75
            ),
            [DreamSequence.TheOcean] = new DreamData(
                "The Ocean",
                new[] {
                    "You float in an endless ocean.",
                    "No, that's not right.",
                    "You ARE the ocean.",
                    "Each wave is a thought. Each current, a memory.",
                    "And deep below, something vast is dreaming.",
                    "It's you. You're dreaming of being a wave.",
                    "The dream is beautiful. But it's time to wake."
                },
                76, 100
            )
        };

        public AmnesiaSystem()
        {
            _fallbackInstance = this;
        }

        /// <summary>
        /// Called when player rests - may trigger a dream
        /// </summary>
        public async Task OnPlayerRest(TerminalEmulator terminal, Character player)
        {
            RestCount++;

            // Check if a dream should trigger (30% base chance, increases with awakening)
            var ocean = OceanPhilosophySystem.Instance;
            float dreamChance = 0.30f + (ocean.AwakeningLevel * 0.05f);

            if (new Random().NextDouble() < dreamChance)
            {
                await PlayDreamSequence(terminal, player);
            }
        }

        /// <summary>
        /// Play an appropriate dream sequence
        /// </summary>
        private async Task PlayDreamSequence(TerminalEmulator terminal, Character player)
        {
            // Find dreams we haven't seen yet that match our level
            var availableDreams = DreamSequences
                .Where(d => !ExperiencedDreams.Contains(d.Key))
                .Where(d => player.Level >= d.Value.MinLevel && player.Level <= d.Value.MaxLevel)
                .ToList();

            if (availableDreams.Count == 0)
            {
                // Fall back to showing a random previously-seen dream
                availableDreams = DreamSequences
                    .Where(d => player.Level >= d.Value.MinLevel)
                    .ToList();
            }

            if (availableDreams.Count == 0) return;

            var dream = availableDreams[new Random().Next(availableDreams.Count)];
            var dreamData = dream.Value;

            ExperiencedDreams.Add(dream.Key);

            // Display the dream
            terminal.ClearScreen();
            terminal.SetColor("dark_magenta");
            terminal.WriteLine("");
            terminal.WriteLine("  ~~~~~~~~~~~~~~~~~~~~~~~~~~~~");
            terminal.WriteLine($"    {dreamData.Title}");
            terminal.WriteLine("  ~~~~~~~~~~~~~~~~~~~~~~~~~~~~");
            terminal.WriteLine("");

            terminal.SetColor("magenta");
            foreach (var line in dreamData.Lines)
            {
                terminal.WriteLine($"  {line}");
                await Task.Delay(1500);
            }

            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine("  ...You wake with tears on your face.");
            terminal.WriteLine("");

            await terminal.PressAnyKey("  Press Enter to continue...");

            // Dreams may trigger memory recovery
            CheckMemoryTrigger(TriggerType.Dream, player);
        }

        /// <summary>
        /// Recover a specific memory fragment
        /// </summary>
        public void RecoverMemory(MemoryFragment fragment)
        {
            if (RecoveredMemories.Add(fragment))
            {
                var data = MemoryData[fragment];
                // GD.Print($"[Amnesia] Recovered memory: {data.Title}");

                // Notify the Ocean Philosophy system
                if (RecoveredMemories.Count >= MemoryData.Count - 2)
                {
                    OceanPhilosophySystem.Instance.ExperienceMoment(AwakeningMoment.MemoriesRecovered);
                }

                if (fragment == MemoryFragment.TheFullTruth)
                {
                    TruthRevealed = true;
                    OceanPhilosophySystem.Instance.ExperienceMoment(AwakeningMoment.TrueIdentityRevealed);
                }
            }
        }

        /// <summary>
        /// Check if a trigger should recover a memory
        /// </summary>
        public void CheckMemoryTrigger(TriggerType trigger, Character player)
        {
            foreach (var memory in MemoryData)
            {
                if (RecoveredMemories.Contains(memory.Key)) continue;
                if (memory.Value.Trigger != trigger) continue;
                if (player.Level < memory.Value.RequiredLevel) continue;

                // Additional checks based on trigger type
                bool shouldTrigger = trigger switch
                {
                    TriggerType.FirstRest => RestCount == 1,
                    TriggerType.Dream => new Random().NextDouble() < 0.3,
                    TriggerType.DungeonFloor10 => true,
                    TriggerType.DungeonFloor25 => true,
                    TriggerType.DungeonFloor50 => true,
                    TriggerType.DungeonFloor75 => true,
                    TriggerType.OldGodEncounter => true,
                    TriggerType.CompanionDeath => true,
                    TriggerType.AllSealsCollected => true,
                    TriggerType.TrueEndingPath => true,
                    TriggerType.FinalRevelation => true,
                    _ => false
                };

                if (shouldTrigger)
                {
                    RecoverMemory(memory.Key);
                    break; // Only recover one at a time
                }
            }
        }

        /// <summary>
        /// Display a recovered memory to the player
        /// </summary>
        public async Task DisplayMemory(MemoryFragment fragment, TerminalEmulator terminal)
        {
            if (!MemoryData.TryGetValue(fragment, out var data)) return;

            terminal.ClearScreen();
            terminal.SetColor("cyan");
            terminal.WriteLine("");
            terminal.WriteLine("  ========================================");
            terminal.WriteLine($"    MEMORY: {data.Title}");
            terminal.WriteLine("  ========================================");
            terminal.WriteLine("");

            terminal.SetColor("bright_cyan");
            foreach (var line in data.Lines)
            {
                terminal.WriteLine($"    {line}");
                await Task.Delay(1200);
            }

            terminal.WriteLine("");
            terminal.SetColor("gray");

            await terminal.PressAnyKey("  Press Enter to return to reality...");
        }

        /// <summary>
        /// Get the percentage of memories recovered
        /// </summary>
        public float GetRecoveryProgress()
        {
            return (float)RecoveredMemories.Count / MemoryData.Count;
        }

        /// <summary>
        /// Get a hint about what the player is forgetting
        /// </summary>
        public string GetAmnesiaHint()
        {
            int recovered = RecoveredMemories.Count;

            return recovered switch
            {
                0 => "Your past is a void. You remember nothing before the dormitory.",
                1 or 2 => "Fragments surface in dreams. Something vast lurks in your forgotten past.",
                3 or 4 => "You were... more. Much more. The dreams speak of creation itself.",
                5 or 6 => "The dreams feel less like memories and more like confessions.",
                7 or 8 => "You are beginning to understand what you forgot. And why.",
                _ => "The veil is thin now. The truth waits just beyond..."
            };
        }

        /// <summary>
        /// Reveal a major memory by key (used by moral paradox system and other story events)
        /// </summary>
        public void RevealMajorMemory(string memoryKey)
        {
            // Map string keys to memory fragments
            var memory = memoryKey switch
            {
                "fragment_of_manwe" => MemoryFragment.TheFullTruth,
                "creator_truth" => MemoryFragment.TheFirstThought,
                "divine_origin" => MemoryFragment.CreatingLight,
                "past_lives" => MemoryFragment.TheForgetting,
                "the_decision" => MemoryFragment.TheDecision,
                "the_purpose" => MemoryFragment.ThePurpose,
                "the_return" => MemoryFragment.TheReturn,
                _ => (MemoryFragment?)null
            };

            if (memory.HasValue && !RecoveredMemories.Contains(memory.Value))
            {
                RecoverMemory(memory.Value);
                // GD.Print($"[Amnesia] Major memory revealed: {memoryKey} -> {memory.Value}");

                // Also trigger story flag
                StoryProgressionSystem.Instance.SetStoryFlag($"memory_{memoryKey}", true);

                // Check if this leads to final revelation
                if (RecoveredMemories.Count >= 8)
                {
                    TruthRevealed = true;
                    OceanPhilosophySystem.Instance.ExperienceMoment(AwakeningMoment.MemoriesRecovered);
                }
            }
        }

        /// <summary>
        /// Serialize for saving
        /// </summary>
        public AmnesiaData Serialize()
        {
            return new AmnesiaData
            {
                RecoveredMemories = RecoveredMemories.ToList(),
                ExperiencedDreams = ExperiencedDreams.ToList(),
                RestCount = RestCount,
                TruthRevealed = TruthRevealed
            };
        }

        /// <summary>
        /// Deserialize from save
        /// </summary>
        public void Deserialize(AmnesiaData data)
        {
            if (data == null) return;

            RecoveredMemories = new HashSet<MemoryFragment>(data.RecoveredMemories);
            ExperiencedDreams = new HashSet<DreamSequence>(data.ExperiencedDreams);
            RestCount = data.RestCount;
            TruthRevealed = data.TruthRevealed;
        }
    }

    #region Enums and Data Classes

    public enum MemoryFragment
    {
        Emptiness,          // The void before creation
        TheFirstThought,    // Becoming aware
        CreatingLight,      // First act of creation
        TheSeven,           // Creating the Old Gods
        WatchingThem,       // Observing the gods' conflicts
        TheDecision,        // Choosing to become mortal
        TheForgetting,      // Why amnesia was necessary
        ThePurpose,         // What the cycles are for
        TheReturn,          // Beginning to remember
        TheFullTruth        // Complete revelation
    }

    public enum DreamSequence
    {
        Drowning,           // Drowning in light
        TheMirror,          // Meeting your reflection
        TheGarden,          // The garden before time
        TheWar,             // Witnessing the first war
        TheDormitory,       // Infinite dormitories
        TheOcean            // Becoming the ocean
    }

    public enum TriggerType
    {
        FirstRest,
        Dream,
        DungeonFloor10,
        DungeonFloor25,
        DungeonFloor50,
        DungeonFloor75,
        OldGodEncounter,
        CompanionDeath,
        AllSealsCollected,
        TrueEndingPath,
        FinalRevelation,
        SecretBossDefeated
    }

    public class MemoryFragmentData
    {
        public string Title { get; }
        public string[] Lines { get; }
        public int RequiredLevel { get; }
        public TriggerType Trigger { get; }

        public MemoryFragmentData(string title, string[] lines, int requiredLevel, TriggerType trigger)
        {
            Title = title;
            Lines = lines;
            RequiredLevel = requiredLevel;
            Trigger = trigger;
        }
    }

    public class DreamData
    {
        public string Title { get; }
        public string[] Lines { get; }
        public int MinLevel { get; }
        public int MaxLevel { get; }

        public DreamData(string title, string[] lines, int minLevel, int maxLevel)
        {
            Title = title;
            Lines = lines;
            MinLevel = minLevel;
            MaxLevel = maxLevel;
        }
    }

    public class AmnesiaData
    {
        public List<MemoryFragment> RecoveredMemories { get; set; } = new();
        public List<DreamSequence> ExperiencedDreams { get; set; } = new();
        public int RestCount { get; set; }
        public bool TruthRevealed { get; set; }
    }

    #endregion
}
