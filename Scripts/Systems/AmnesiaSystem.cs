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
        // v1.1.12: MemoriesRecovered needs 4. Only the floor 10/25/50/75 memories and the full truth
        // can be recovered (no call passes the rest-, god-, death- or seal-triggers), and the full
        // truth already grants TrueIdentityRevealed, so the four floor memories are the threshold.
        public const int MemoriesRecoveredThreshold = 4;

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
            [MemoryFragment.Emptiness] = new MemoryFragmentData(MemoryFragment.Emptiness, 1, TriggerType.FirstRest),
            [MemoryFragment.TheFirstThought] = new MemoryFragmentData(MemoryFragment.TheFirstThought, 5, TriggerType.DungeonFloor10),
            [MemoryFragment.CreatingLight] = new MemoryFragmentData(MemoryFragment.CreatingLight, 10, TriggerType.DungeonFloor25),
            [MemoryFragment.TheSeven] = new MemoryFragmentData(MemoryFragment.TheSeven, 15, TriggerType.OldGodEncounter),
            [MemoryFragment.WatchingThem] = new MemoryFragmentData(MemoryFragment.WatchingThem, 25, TriggerType.DungeonFloor50),
            [MemoryFragment.TheDecision] = new MemoryFragmentData(MemoryFragment.TheDecision, 35, TriggerType.DungeonFloor75),
            [MemoryFragment.TheForgetting] = new MemoryFragmentData(MemoryFragment.TheForgetting, 50, TriggerType.AllSealsCollected),
            [MemoryFragment.ThePurpose] = new MemoryFragmentData(MemoryFragment.ThePurpose, 65, TriggerType.CompanionDeath),
            [MemoryFragment.TheReturn] = new MemoryFragmentData(MemoryFragment.TheReturn, 85, TriggerType.TrueEndingPath),
            [MemoryFragment.TheFullTruth] = new MemoryFragmentData(MemoryFragment.TheFullTruth, 100, TriggerType.FinalRevelation)
        };

        /// <summary>
        /// Dream sequences that play when the player rests
        /// </summary>
        public static readonly Dictionary<DreamSequence, DreamData> DreamSequences = new()
        {
            [DreamSequence.Drowning] = new DreamData(DreamSequence.Drowning, 1, 5),
            [DreamSequence.TheMirror] = new DreamData(DreamSequence.TheMirror, 6, 15),
            [DreamSequence.TheGarden] = new DreamData(DreamSequence.TheGarden, 16, 30),
            [DreamSequence.TheWar] = new DreamData(DreamSequence.TheWar, 31, 50),
            [DreamSequence.TheDormitory] = new DreamData(DreamSequence.TheDormitory, 51, 75),
            [DreamSequence.TheOcean] = new DreamData(DreamSequence.TheOcean, 76, 100)
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

            if (Random.Shared.NextDouble() < dreamChance)
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

            var dream = availableDreams[Random.Shared.Next(availableDreams.Count)];
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
                await Pacing.Wait(1500);
            }

            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("amnesia.dream_wake")}");
            terminal.WriteLine("");

            await terminal.PressAnyKey(Loc.Get("ending.press_enter"));

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
                if (RecoveredMemories.Count >= MemoriesRecoveredThreshold)
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
                    TriggerType.Dream => Random.Shared.NextDouble() < 0.3,
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
        /// Get the percentage of memories recovered
        /// </summary>
        public float GetRecoveryProgress()
        {
            return (float)RecoveredMemories.Count / MemoryData.Count;
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
        /// <summary>v1.2.5: the stored id; the title and lines are looked up by it in the reader's language.</summary>
        public MemoryFragment Fragment { get; }
        public string Title => Loc.Get(TitleKey(Fragment));
        public string[] Lines => Loc.Get($"amnesia.memory.{Fragment}.lines").Split('\n');
        public int RequiredLevel { get; }
        public TriggerType Trigger { get; }

        public MemoryFragmentData(MemoryFragment fragment, int requiredLevel, TriggerType trigger)
        {
            Fragment = fragment;
            RequiredLevel = requiredLevel;
            Trigger = trigger;
        }

        /// <summary>v1.2.5: two memories share their title with the Wave Fragment of the same name.</summary>
        internal static string TitleKey(MemoryFragment fragment) => fragment switch
        {
            MemoryFragment.TheForgetting => "ocean.fragment.TheForgetting.title",
            MemoryFragment.TheReturn => "ocean.fragment.TheReturn.title",
            _ => $"amnesia.memory.{fragment}.title"
        };
    }

    public class DreamData
    {
        /// <summary>v1.2.5: the stored id; the title and lines are looked up by it in the reader's language.</summary>
        public DreamSequence Dream { get; }
        public string Title => Loc.Get(TitleKey(Dream));
        public string[] Lines => Loc.Get($"amnesia.dream.{Dream}.lines").Split('\n');
        public int MinLevel { get; }
        public int MaxLevel { get; }

        public DreamData(DreamSequence dream, int minLevel, int maxLevel)
        {
            Dream = dream;
            MinLevel = minLevel;
            MaxLevel = maxLevel;
        }

        /// <summary>v1.2.5: two dreams share their title with the rest dream of the same name.</summary>
        internal static string TitleKey(DreamSequence dream) => dream switch
        {
            DreamSequence.Drowning => "dream.dream_drowning.title",
            DreamSequence.TheMirror => "dream.dream_mirror.title",
            _ => $"amnesia.dream.{dream}.title"
        };
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
