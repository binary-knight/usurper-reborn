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

        // v1.1.12: the points model. A moment is worth 3, a Wave Fragment 2, each distinct insight 1.
        // Stage N needs StageThresholds[N] points. Expected pacing:
        //   1 (4):  levels 1-10. The first companion (3) plus one early insight (the wave_self room
        //           feature, the mirror dream, a catacomb discovery), or sparing a beaten foe.
        //   2 (12): about level 20-30. A few more moments (a spared foe, a lore song, a companion's
        //           death), the first fragments and a handful of dreams, discoveries and visions.
        //           dream_ocean_first and the Magic Shop's sixth tier open here.
        //   3 (22): the mid game, the first Old Gods and their fragments.
        //   4 (34), 5 (48), 6 (62): the later Old Gods, the seals, the deep visions and dreams.
        //   7: the stage 6 points plus TrueIdentityRevealed or AllSealsCollected.
        //      TrueIdentityRevealed alone still grants 7 at once.
        // The level never falls: a save restores at least the level it was saved with.
        public const int PointsPerMoment = 3;
        public const int PointsPerFragment = 2;
        public const int PointsPerInsight = 1;
        public static readonly int[] StageThresholds = { 0, 4, 12, 22, 34, 48, 62 };
        public const int MaxStage = 7;

        /// <summary>v1.1.12: the distinct insights gained, by source id. Saved.</summary>
        public HashSet<string> InsightIds { get; private set; } = new();

        /// <summary>v1.1.12: the highest stage risen to and not yet announced (0 = none).</summary>
        public int PendingAnnouncementStage { get; private set; }
        /// <summary>v1.1.12: the stage the pending rise started from.</summary>
        public int PendingAnnouncementFromStage { get; private set; }

        // v1.1.12: set while a save is replayed, so a restore never queues an announcement
        private bool _restoring;

        /// <summary>v1.1.12: the awakening points from moments, fragments and insights.</summary>
        public int Points =>
            ExperiencedMoments.Count * PointsPerMoment
            + CollectedFragments.Count * PointsPerFragment
            + InsightIds.Count * PointsPerInsight;

        /// <summary>v1.1.12: the points the next stage needs, or -1 at the last stage.</summary>
        public int PointsForNextStage =>
            AwakeningLevel + 1 < StageThresholds.Length ? StageThresholds[AwakeningLevel + 1]
            : AwakeningLevel < MaxStage ? StageThresholds[StageThresholds.Length - 1]
            : -1;

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
        /// v1.1.12: gain an insight. Each distinct source id counts once toward the awakening.
        /// Used for dreams, visions, discoveries, room features, riddles, songs and choices.
        /// </summary>
        public void GainInsight(string insightId)
        {
            if (string.IsNullOrEmpty(insightId)) return;

            // Blood Price blocks insight: murderers cannot awaken
            var player = GameEngine.Instance?.CurrentPlayer;
            if (player != null && player.MurderWeight > GameConfig.MurderWeightAwakeningBlock)
                return; // The weight of death clouds your mind

            if (InsightIds.Add(insightId))
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
                OnStageRose(oldLevel, newLevel);
            }
        }

        /// <summary>
        /// v1.1.12: a stage rise queues its announcement for the next safe point and applies the
        /// run-time boosts. A restore does neither: the stages were announced when they happened.
        /// </summary>
        private void OnStageRose(int oldLevel, int newLevel)
        {
            if (_restoring) return;
            if (PendingAnnouncementStage == 0) PendingAnnouncementFromStage = oldLevel;
            PendingAnnouncementStage = Math.Max(PendingAnnouncementStage, newLevel);
            try { GameEngine.Instance?.CurrentPlayer?.RecalculateStats(); } catch { /* boosts apply at the next recalculation */ }
        }

        /// <summary>
        /// v1.1.12: take the pending announcement, once. Returns (from, to) or null.
        /// </summary>
        public (int From, int To)? TakePendingAnnouncement()
        {
            if (PendingAnnouncementStage == 0) return null;
            var result = (PendingAnnouncementFromStage, PendingAnnouncementStage);
            PendingAnnouncementStage = 0;
            PendingAnnouncementFromStage = 0;
            return result;
        }

        /// <summary>
        /// v1.1.12: the stage the points and moments reach. Stages 1-6 by points; stage 7 needs the
        /// stage 6 points and AllSealsCollected, or TrueIdentityRevealed alone.
        /// </summary>
        private int CalculateAwakeningLevel()
        {
            if (ExperiencedMoments.Contains(AwakeningMoment.TrueIdentityRevealed)) return MaxStage;

            int points = Points;
            int level = 0;
            for (int stage = 1; stage < StageThresholds.Length; stage++)
                if (points >= StageThresholds[stage]) level = stage;

            if (level == StageThresholds.Length - 1 && ExperiencedMoments.Contains(AwakeningMoment.AllSealsCollected))
                level = MaxStage;

            return level;
        }

        /// <summary>
        /// v1.1.12: replay a save. Nothing is announced, and the level is at least the saved level.
        /// </summary>
        public void RestoreFromSave(IEnumerable<WaveFragment> fragments, IEnumerable<AwakeningMoment> moments,
            IEnumerable<string>? insightIds, int savedLevel)
        {
            _restoring = true;
            try
            {
                foreach (var f in fragments) CollectFragment(f);
                foreach (var m in moments) ExperienceMoment(m);
                if (insightIds != null)
                    foreach (var id in insightIds)
                        if (!string.IsNullOrEmpty(id) && InsightIds.Add(id)) CheckAwakeningProgress();
                int floor = Math.Clamp(savedLevel, 0, MaxStage);
                if (floor > AwakeningLevel) AwakeningLevel = floor;
            }
            finally { _restoring = false; }
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
                // v1.1.12: recorded when a companion joins you; the enum name stays for old saves
                AwakeningMoment.SacrificedForAnother => new OceanInsight(
                    "Water Joins Water",
                    "Another chose to walk your road. " +
                    "For a moment, there was no 'you' and 'them' - two currents in one stream. " +
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
                AwakeningMoment.LetGoOfPower => new OceanInsight(
                    "Open Hands",
                    "Power flows through open hands, not clenched fists. " +
                    "In releasing your grip, you felt the current of something " +
                    "far greater than any throne or weapon could contain.",
                    3
                ),
                AwakeningMoment.AcceptedDeath => new OceanInsight(
                    "The Still Water",
                    "You looked into the void and did not flinch. " +
                    "Death is not the opposite of life — it is the shore " +
                    "where every wave finally rests.",
                    4
                ),
                AwakeningMoment.CompanionSacrifice => new OceanInsight(
                    "The Gift of Depth",
                    "They gave everything so you could continue. " +
                    "In their sacrifice, the boundary between self and other " +
                    "dissolved entirely. Love is not a transaction — it is water " +
                    "pouring itself into water.",
                    4
                ),
                AwakeningMoment.ForgaveBetrayerMercy => new OceanInsight(
                    "The River Forgives the Stone",
                    "To forgive is to remember that the one who hurt you " +
                    "is also hurting. Every wave crashes against every other wave, " +
                    "but beneath the surface, they are the same water.",
                    3
                ),
                AwakeningMoment.AcceptedGrief => new OceanInsight(
                    "The Tide Returns",
                    "Grief is not a wound to heal — it is the ocean mourning " +
                    "a wave that has returned home. You carried the weight " +
                    "until it became wisdom.",
                    4
                ),
                AwakeningMoment.RejectedParadise => new OceanInsight(
                    "The Dreamer Stays Awake",
                    "You were offered a perfect dream, and chose the imperfect truth. " +
                    "The ocean does not hide from its storms. " +
                    "Only in accepting all of it does the water become whole.",
                    5
                ),
                AwakeningMoment.AbsorbedDarkness => new OceanInsight(
                    "The Deep Accepts All",
                    "You took the world's shadow into yourself. " +
                    "The ocean does not reject its deepest trenches. " +
                    "Light and dark are both water, seen from different angles.",
                    5
                ),
                AwakeningMoment.HeardOldGodLoreSong => new OceanInsight(
                    "The Song Beneath",
                    "The melody carried truths older than words. " +
                    "For a moment, you heard the sound the ocean makes " +
                    "when it remembers what it lost.",
                    2
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
                return phrases[Random.Shared.Next(phrases.Length)];
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
                return phrases[Random.Shared.Next(phrases.Length)];
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
            InsightIds = new HashSet<string>();
            PendingAnnouncementStage = 0;
            PendingAnnouncementFromStage = 0;
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
        SacrificedForAnother,   // v1.1.12: a companion joined you (name kept for old saves)
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
