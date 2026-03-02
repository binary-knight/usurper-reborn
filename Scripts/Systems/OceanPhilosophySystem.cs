using System;
using System.Collections.Generic;
using System.Linq;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// The Ocean Philosophy System tracks the player's spiritual awakening
    /// to the core truth: "You are not a wave fighting the ocean. You ARE
    /// the ocean, dreaming of being a wave."
    ///
    /// This system is subtle at first, becoming explicit only near the ending.
    /// The player is a fragment of Manwe, sent to experience mortality.
    /// </summary>
    public class OceanPhilosophySystem
    {
        private static OceanPhilosophySystem? _fallbackInstance;
        public static OceanPhilosophySystem Instance
        {
            get
            {
                var ctx = UsurperRemake.Server.SessionContext.Current;
                if (ctx != null) return ctx.Ocean;
                return _fallbackInstance ??= new OceanPhilosophySystem();
            }
        }

        /// <summary>
        /// Awakening Level (0-7): How close the player is to understanding the truth
        /// </summary>
        public int AwakeningLevel { get; private set; } = 0;

        /// <summary>
        /// Wave Fragments: Cryptic lore pieces found throughout the game
        /// </summary>
        public HashSet<WaveFragment> CollectedFragments { get; private set; } = new();

        /// <summary>
        /// Ocean Insights: Major revelations triggered by key events
        /// </summary>
        public List<OceanInsight> Insights { get; private set; } = new();

        /// <summary>
        /// Tracks if the player has experienced the key moments for awakening
        /// </summary>
        public HashSet<AwakeningMoment> ExperiencedMoments { get; private set; } = new();

        /// <summary>
        /// The ambient wisdom phrases that NPCs might say based on awakening level
        /// </summary>
        private static readonly Dictionary<int, string[]> AmbientWisdom = new()
        {
            [0] = new[] {
                "The world seems solid and separate.",
                "Each being stands alone against the void.",
                "Power is the only truth."
            },
            [1] = new[] {
                "Sometimes, in quiet moments, the boundaries feel thin...",
                "An old saying: 'The river does not push the river.'",
                "All streams flow to the sea, yet the sea is never full."
            },
            [2] = new[] {
                "What is a wave but the ocean in motion?",
                "The flame that burns twice as bright burns half as long.",
                "They say the First Ones knew no separation."
            },
            [3] = new[] {
                "When water meets water, which loses its identity?",
                "The dying often speak of light... and of returning.",
                "Manwe wept when he first felt alone."
            },
            [4] = new[] {
                "You feel... familiar. Have we met in another life?",
                "Some souls are older than the bodies they wear.",
                "The creator dreams of being created."
            },
            [5] = new[] {
                "Child of the deep waters, you are beginning to remember.",
                "The wave rises, crashes, and returns. This is not death.",
                "Your eyes hold the sadness of one who has forgotten home."
            },
            [6] = new[] {
                "The boundaries blur. Self and other merge at the edges.",
                "You carry the weight of worlds. Few mortals could.",
                "The Ocean calls to its fragments. Can you not feel it?"
            },
            [7] = new[] {
                "Welcome back, Dreamer. The dream is ending.",
                "You always knew. You chose to forget.",
                "The wave remembers it is water."
            }
        };

        /// <summary>
        /// Wave Fragment lore texts - these reveal the philosophy piece by piece
        /// </summary>
        public static readonly Dictionary<WaveFragment, WaveFragmentData> FragmentData = new()
        {
            [WaveFragment.Origin] = new WaveFragmentData(
                "The Origin",
                "In the beginning, there was only the Ocean - vast, eternal, undivided. " +
                "It knew itself completely, for there was nothing else to know. " +
                "But complete knowledge became complete loneliness.",
                1
            ),
            [WaveFragment.FirstSeparation] = new WaveFragmentData(
                "The First Separation",
                "And so the Ocean dreamed of waves. Each wave rose, believing itself " +
                "separate and special. Each wave fell, returning to what it always was. " +
                "The Ocean learned to love through loss.",
                2
            ),
            [WaveFragment.TheForgetting] = new WaveFragmentData(
                "The Forgetting",
                "For the dream to feel real, the waves had to forget. " +
                "True separation requires true belief in separation. " +
                "And so the Ocean hid itself from itself.",
                3
            ),
            [WaveFragment.ManwesChoice] = new WaveFragmentData(
                "Manwe's Choice",
                "The first and greatest wave chose the deepest forgetting. " +
                "He became Manwe, Creator of the Seven, Lord of Gods. " +
                "He forgot he was still water. He believed himself stone.",
                4
            ),
            [WaveFragment.TheSevenDrops] = new WaveFragmentData(
                "The Seven Drops",
                "From Manwe's forgetting sprang seven more waves - the Old Gods. " +
                "Veloura for love, Thorgrim for law, Noctura for mystery... " +
                "Each a facet of the Ocean's yearning to understand itself.",
                4
            ),
            [WaveFragment.TheCorruption] = new WaveFragmentData(
                "The Corruption",
                "But belief in separation became pain. The waves fought each other, " +
                "having forgotten they shared the same source. " +
                "War is water fighting water. Cruelty is the ocean hurting itself.",
                5
            ),
            [WaveFragment.TheCycle] = new WaveFragmentData(
                "The Eternal Cycle",
                "Manwe grew weary of watching waves destroy waves. " +
                "In desperation, he sent a fragment of himself to learn what he had forgotten. " +
                "That fragment wakes in a dormitory, remembering nothing...",
                6
            ),
            [WaveFragment.TheReturn] = new WaveFragmentData(
                "The Return",
                "Death is not an ending. It is a wave becoming water again. " +
                "The individual dissolves, but the essence returns to the source. " +
                "We do not lose what we love - we become it.",
                6
            ),
            [WaveFragment.TheTruth] = new WaveFragmentData(
                "The Final Truth",
                "You are not a wave fighting the ocean. You ARE the ocean, " +
                "dreaming of being a wave. When you wake, you will not disappear - " +
                "you will become everything you ever loved.",
                7
            ),
            [WaveFragment.TheChoice] = new WaveFragmentData(
                "The Choice",
                "The dreamer can choose: continue the dream, or wake. " +
                "Waking is not death - it is the end of loneliness. " +
                "But the dream has its own beauty, its own purpose...",
                7
            )
        };

        public OceanPhilosophySystem()
        {
            _fallbackInstance = this;
        }

        /// <summary>
        /// Collect a wave fragment lore piece
        /// </summary>
        public void CollectFragment(WaveFragment fragment)
        {
            if (CollectedFragments.Add(fragment))
            {
                var data = FragmentData[fragment];
                // GD.Print($"[Ocean] Collected fragment: {data.Title}");

                // Fragments contribute to awakening
                CheckAwakeningProgress();
            }
        }

        /// <summary>
        /// Record that the player experienced a key awakening moment
        /// </summary>
        public void ExperienceMoment(AwakeningMoment moment)
        {
            if (ExperiencedMoments.Add(moment))
            {
                // GD.Print($"[Ocean] Experienced moment: {moment}");

                // Add insight based on moment
                var insight = CreateInsightForMoment(moment);
                if (insight != null)
                {
                    Insights.Add(insight);
                }

                CheckAwakeningProgress();
            }
        }

        /// <summary>
        /// Gain or lose awakening insight points directly
        /// Used for choices that affect philosophical understanding
        /// </summary>
        public void GainInsight(int points)
        {
            // Blood Price blocks positive insight — murderers cannot awaken
            if (points > 0)
            {
                var player = GameEngine.Instance?.CurrentPlayer;
                if (player != null && player.MurderWeight > GameConfig.MurderWeightAwakeningBlock)
                    return; // The weight of death clouds your mind
            }

            // Track insight points in a simple way - affects awakening level calculation
            // Positive points move toward awakening, negative move away
            if (points > 0)
            {
                // Create a minor insight
                var insight = new OceanInsight(
                    "Moment of Clarity",
                    "A deeper understanding settles into your consciousness.",
                    points / 10 // Contributes to awakening
                );
                Insights.Add(insight);
                // GD.Print($"[Ocean] Gained {points} insight points");
            }
            else if (points < 0)
            {
                // Grasping moves away from awakening
                // GD.Print($"[Ocean] Lost {-points} insight points (grasping)");
            }

            CheckAwakeningProgress();
        }

        /// <summary>
        /// Check if player qualifies for higher awakening level
        /// </summary>
        private void CheckAwakeningProgress()
        {
            int newLevel = CalculateAwakeningLevel();
            if (newLevel > AwakeningLevel)
            {
                int oldLevel = AwakeningLevel;
                AwakeningLevel = newLevel;
                // GD.Print($"[Ocean] Awakening increased: {oldLevel} -> {newLevel}");
            }
        }

        /// <summary>
        /// Calculate awakening level based on fragments, moments, and insights
        /// </summary>
        private int CalculateAwakeningLevel()
        {
            int level = 0;

            // Fragments contribute
            int fragmentCount = CollectedFragments.Count;
            level += fragmentCount / 2; // Every 2 fragments = +1 level

            // Key moments contribute
            if (ExperiencedMoments.Contains(AwakeningMoment.FirstCompanionDeath)) level++;
            if (ExperiencedMoments.Contains(AwakeningMoment.SacrificedForAnother)) level++;
            if (ExperiencedMoments.Contains(AwakeningMoment.SparedAnEnemy)) level++;
            if (ExperiencedMoments.Contains(AwakeningMoment.MetManwe)) level++;
            if (ExperiencedMoments.Contains(AwakeningMoment.AllSealsCollected)) level++;
            if (ExperiencedMoments.Contains(AwakeningMoment.MemoriesRecovered)) level++;
            if (ExperiencedMoments.Contains(AwakeningMoment.TrueIdentityRevealed)) level = 7; // Auto-max

            return Math.Min(7, level);
        }

        /// <summary>
        /// Create an insight for an awakening moment
        /// </summary>
        private OceanInsight? CreateInsightForMoment(AwakeningMoment moment)
        {
            return moment switch
            {
                AwakeningMoment.FirstCompanionDeath => new OceanInsight(
                    "The Wave Breaks",
                    "Watching them fall, you feel something crack inside. " +
                    "Not just grief - recognition. You have felt this before. " +
                    "Many times. An echo of ancient sorrow...",
                    2
                ),
                AwakeningMoment.SacrificedForAnother => new OceanInsight(
                    "Water Flows Downhill",
                    "In giving of yourself, the boundaries softened. " +
                    "For a moment, there was no 'you' and 'them' - only love in motion. " +
                    "Is this what the Ocean feels?",
                    3
                ),
                AwakeningMoment.SparedAnEnemy => new OceanInsight(
                    "The Wave Recognizes Itself",
                    "Looking into their eyes, you saw... yourself. " +
                    "Not a metaphor. A literal recognition. " +
                    "We are made of the same water.",
                    3
                ),
                AwakeningMoment.MetManwe => new OceanInsight(
                    "The Dreamer Dreams the Dream",
                    "His eyes held exhaustion older than the world. " +
                    "And something else - recognition. He knew you. " +
                    "Or rather... he knew what you are.",
                    5
                ),
                AwakeningMoment.AllSealsCollected => new OceanInsight(
                    "The Story Completes",
                    "Seven seals, seven truths. Together they form a mirror. " +
                    "In it, you see not your face - but the face of the Ocean. " +
                    "You have always known. You chose to forget.",
                    6
                ),
                AwakeningMoment.MemoriesRecovered => new OceanInsight(
                    "The Veil Thins",
                    "They come flooding back - not memories of a life, " +
                    "but memories of being the source of all lives. " +
                    "You remember creating worlds. You remember being alone.",
                    6
                ),
                AwakeningMoment.TrueIdentityRevealed => new OceanInsight(
                    "I AM",
                    "The wave remembers it is water. " +
                    "You are not a fragment of Manwe. " +
                    "You ARE Manwe. You ARE the Ocean. " +
                    "You are everything that has ever loved.",
                    7
                ),
                _ => null
            };
        }

        /// <summary>
        /// Get a random ambient wisdom phrase for the current awakening level
        /// </summary>
        public string GetAmbientWisdom()
        {
            if (AmbientWisdom.TryGetValue(AwakeningLevel, out var phrases))
            {
                return phrases[new Random().Next(phrases.Length)];
            }
            return "";
        }

        /// <summary>
        /// Get wisdom for a specific NPC interaction based on their perception
        /// </summary>
        public string GetNPCWisdom(bool isWise = false, bool isOldGod = false)
        {
            // Wise NPCs can perceive one level higher
            int effectiveLevel = isWise ? Math.Min(7, AwakeningLevel + 1) : AwakeningLevel;

            // Old Gods always perceive the truth
            if (isOldGod && AwakeningLevel >= 4)
            {
                effectiveLevel = Math.Max(effectiveLevel, 5);
            }

            if (AmbientWisdom.TryGetValue(effectiveLevel, out var phrases))
            {
                return phrases[new Random().Next(phrases.Length)];
            }
            return "";
        }

        /// <summary>
        /// Check if player is ready for the True Ending
        /// </summary>
        public bool IsReadyForTrueEnding()
        {
            return AwakeningLevel >= 7 &&
                   ExperiencedMoments.Contains(AwakeningMoment.FirstCompanionDeath) &&
                   CollectedFragments.Count >= 8;
        }

        /// <summary>
        /// Get all fragments as a formatted string for display
        /// </summary>
        public string GetFragmentLore()
        {
            var lines = new List<string>();
            lines.Add("=== The Fragments of Truth ===\n");

            foreach (var fragment in CollectedFragments.OrderBy(f => FragmentData[f].RequiredAwakening))
            {
                var data = FragmentData[fragment];
                lines.Add($"[{data.Title}]");
                lines.Add(data.Text);
                lines.Add("");
            }

            if (CollectedFragments.Count < FragmentData.Count)
            {
                int missing = FragmentData.Count - CollectedFragments.Count;
                lines.Add($"({missing} fragments remain hidden...)");
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Serialize state for saving
        /// </summary>
        public OceanPhilosophyData Serialize()
        {
            return new OceanPhilosophyData
            {
                AwakeningLevel = AwakeningLevel,
                CollectedFragments = CollectedFragments.ToList(),
                ExperiencedMoments = ExperiencedMoments.ToList(),
                Insights = Insights.ToList()
            };
        }

        /// <summary>
        /// Restore state from save data
        /// </summary>
        public void Deserialize(OceanPhilosophyData data)
        {
            if (data == null) return;

            AwakeningLevel = data.AwakeningLevel;
            CollectedFragments = new HashSet<WaveFragment>(data.CollectedFragments);
            ExperiencedMoments = new HashSet<AwakeningMoment>(data.ExperiencedMoments);
            Insights = data.Insights?.ToList() ?? new List<OceanInsight>();
        }

        /// <summary>
        /// Reset all state for a new game
        /// </summary>
        public void Reset()
        {
            AwakeningLevel = 0;
            CollectedFragments = new HashSet<WaveFragment>();
            ExperiencedMoments = new HashSet<AwakeningMoment>();
            Insights = new List<OceanInsight>();
        }
    }

    #region Enums and Data Classes

    /// <summary>
    /// Wave Fragments - collectible lore pieces that reveal the philosophy
    /// </summary>
    public enum WaveFragment
    {
        Origin,             // The beginning - the undivided Ocean
        FirstSeparation,    // The Ocean dreams of waves
        TheForgetting,      // Waves must forget to feel separate
        ManwesChoice,       // The first and greatest wave
        TheSevenDrops,      // The Old Gods as fragments
        TheCorruption,      // How separation becomes pain
        TheCycle,           // Why Manwe sends fragments of himself
        TheReturn,          // Death is returning home
        TheTruth,           // The final understanding
        TheChoice           // The dreamer can choose
    }

    /// <summary>
    /// Key moments that trigger awakening
    /// </summary>
    public enum AwakeningMoment
    {
        FirstCompanionDeath,    // Losing someone you cared about
        SacrificedForAnother,   // Giving something precious for another
        SparedAnEnemy,          // Showing mercy when you could destroy
        MetManwe,               // Encountering the Creator
        AllSealsCollected,      // Understanding the full history
        MemoriesRecovered,      // The amnesia lifts
        TrueIdentityRevealed,   // The final revelation
        LetGoOfPower,           // Choosing not to consume gods
        AcceptedDeath,          // Facing mortality without fear
        CompanionSacrifice,     // A companion sacrifices themselves for you
        ForgaveBetrayerMercy,   // Chose to forgive someone who betrayed you
        AcceptedGrief,          // Completed the grief cycle with wisdom
        RejectedParadise,       // Refused to create a false utopia
        AbsorbedDarkness,       // Took on the world's darkness to save it
        HeardOldGodLoreSong     // Heard a lore song about an Old God at the Music Shop
    }

    /// <summary>
    /// Data for a wave fragment lore piece
    /// </summary>
    public class WaveFragmentData
    {
        public string Title { get; }
        public string Text { get; }
        public int RequiredAwakening { get; }

        public WaveFragmentData(string title, string text, int requiredAwakening)
        {
            Title = title;
            Text = text;
            RequiredAwakening = requiredAwakening;
        }
    }

    /// <summary>
    /// An insight gained from experiencing a key moment
    /// </summary>
    public class OceanInsight
    {
        public string Title { get; set; } = "";
        public string Text { get; set; } = "";
        public int AwakeningContribution { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;

        public OceanInsight() { }

        public OceanInsight(string title, string text, int contribution)
        {
            Title = title;
            Text = text;
            AwakeningContribution = contribution;
            Timestamp = DateTime.Now;
        }
    }

    /// <summary>
    /// Serializable data for save/load
    /// </summary>
    public class OceanPhilosophyData
    {
        public int AwakeningLevel { get; set; }
        public List<WaveFragment> CollectedFragments { get; set; } = new();
        public List<AwakeningMoment> ExperiencedMoments { get; set; } = new();
        public List<OceanInsight> Insights { get; set; } = new();
    }

    #endregion
}
