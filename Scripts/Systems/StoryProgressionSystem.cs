using System;
using System.Collections.Generic;
using System.Linq;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Story Progression System - Tracks the main narrative, chapters, and player choices
    /// Manages the journey from newcomer to godhood with branching paths
    /// </summary>
    public class StoryProgressionSystem
    {
        private static StoryProgressionSystem? _fallbackInstance;
        public static StoryProgressionSystem Instance
        {
            get
            {
                var ctx = UsurperRemake.Server.SessionContext.Current;
                if (ctx != null) return ctx.Story;
                return _fallbackInstance ??= new StoryProgressionSystem();
            }
        }

        // Current story state
        public StoryChapter CurrentChapter { get; private set; } = StoryChapter.Awakening;
        public StoryAct CurrentAct { get; private set; } = StoryAct.Act1_TheNewcomer;
        public int StoryFlags { get; private set; } = 0;

        // Major choice tracking
        public Dictionary<string, StoryChoice> MajorChoices { get; private set; } = new();

        // Old Gods state
        public Dictionary<OldGodType, OldGodState> OldGodStates { get; private set; } = new();

        // Artifact collection
        public HashSet<ArtifactType> CollectedArtifacts { get; private set; } = new();

        // Seven Seals (lore collectibles)
        public HashSet<SealType> CollectedSeals { get; private set; } = new();

        // Relationship with key NPCs
        public Dictionary<string, int> KeyNPCRelationships { get; private set; } = new();

        // Prestige/Cycle tracking
        public int CurrentCycle { get; internal set; } = 1;
        public HashSet<EndingType> CompletedEndings { get; private set; } = new();
        public List<string> CycleCarryoverItems { get; private set; } = new();

        // Game day tracking (for companion/grief systems)
        public int CurrentGameDay { get; set; } = 1;

        // Story event log
        public List<StoryEvent> EventLog { get; private set; } = new();

        // String-based story flags for dialogue system
        private HashSet<string> StringStoryFlags { get; set; } = new();

        public StoryProgressionSystem()
        {
            InitializeOldGods();
            InitializeKeyNPCs();
        }

        private void InitializeOldGods()
        {
            OldGodStates[OldGodType.Maelketh] = new OldGodState
            {
                Name = "Maelketh",
                Title = "The Broken Blade",
                Domain = "War & Conquest",
                Status = GodStatus.Corrupted,
                CanBeSaved = false,
                DungeonFloor = 25, // First god encounter
                Description = "Once the noble god of honorable combat, Maelketh's mind shattered during the Sundering. Now he knows only endless war."
            };

            OldGodStates[OldGodType.Veloura] = new OldGodState
            {
                Name = "Veloura",
                Title = "The Withered Heart",
                Domain = "Love & Passion",
                Status = GodStatus.Dying,
                CanBeSaved = true,
                DungeonFloor = 40, // Second god
                Location = "Love Corner",
                Description = "The goddess of love fades with each broken heart. She can still be saved... if one proves love endures."
            };

            OldGodStates[OldGodType.Thorgrim] = new OldGodState
            {
                Name = "Thorgrim",
                Title = "The Hollow Judge",
                Domain = "Law & Order",
                Status = GodStatus.Corrupted,
                CanBeSaved = false,
                DungeonFloor = 55, // Third god
                Location = "Castle Throne Room",
                Description = "The god of justice became the god of tyranny. His scales weigh only power now."
            };

            OldGodStates[OldGodType.Noctura] = new OldGodState
            {
                Name = "Noctura",
                Title = "The Shadow Weaver",
                Domain = "Shadow & Secrets",
                Status = GodStatus.Neutral,
                CanBeSaved = true, // Can be allied with
                DungeonFloor = 70, // Fourth god
                Location = "Dark Alley",
                Description = "The mysterious orchestrator. She set these events in motion. Her true motives remain hidden."
            };

            OldGodStates[OldGodType.Aurelion] = new OldGodState
            {
                Name = "Aurelion",
                Title = "The Fading Light",
                Domain = "Light & Truth",
                Status = GodStatus.Dying,
                CanBeSaved = true,
                DungeonFloor = 85, // Fifth god
                Location = "Temple",
                Description = "The god of truth speaks only in whispers now. His light dims with every lie told in the realm."
            };

            OldGodStates[OldGodType.Terravok] = new OldGodState
            {
                Name = "Terravok",
                Title = "The Sleeping Mountain",
                Domain = "Earth & Endurance",
                Status = GodStatus.Dormant,
                CanBeSaved = true,
                DungeonFloor = 95, // Second-to-last god
                Description = "The oldest god sleeps beneath the dungeon. He can be awakened... but should he be?"
            };

            OldGodStates[OldGodType.Manwe] = new OldGodState
            {
                Name = "Manwe",
                Title = "The Weary Creator",
                Domain = "Creation & Balance",
                Status = GodStatus.Unknown,
                CanBeSaved = false, // Final choice
                DungeonFloor = 100,
                Description = "The Supreme Creator. He waits at the end of all paths, weary of eternity."
            };
        }

        private void InitializeKeyNPCs()
        {
            // The Stranger (Noctura in disguise)
            KeyNPCRelationships["TheStranger"] = 0;

            // Potential romantic interests tied to story
            KeyNPCRelationships["Lysandra"] = 0; // Light path romance
            KeyNPCRelationships["Mordecai"] = 0; // Dark path romance
            KeyNPCRelationships["Sylvana"] = 0;  // Neutral path romance

            // Faction leaders
            KeyNPCRelationships["KingRegent"] = 0;
            KeyNPCRelationships["GangLord"] = 0;
            KeyNPCRelationships["HighPriest"] = 0;
        }

        /// <summary>
        /// Check if player meets requirements to advance story
        /// </summary>
        public bool CanAdvanceStory(Character player)
        {
            return CurrentChapter switch
            {
                StoryChapter.Awakening => player.Level >= 1, // Always can start
                StoryChapter.FirstBlood => player.MKills >= 1,
                StoryChapter.TheStranger => HasFlag(StoryFlag.MetStranger),
                StoryChapter.FactionChoice => player.Level >= 10,
                StoryChapter.RisingPower => player.Level >= 15 && HasAnyFaction(player),
                StoryChapter.TheWhispers => player.Level >= 25 && HasFlag(StoryFlag.HeardWhispers),
                StoryChapter.FirstGod => player.Level >= 50 && CollectedArtifacts.Count >= 1,
                StoryChapter.GodWar => player.Level >= 65 && GetDefeatedGodCount() >= 1,
                StoryChapter.TheChoice => player.Level >= 85 && GetDefeatedGodCount() >= 3,
                StoryChapter.Ascension => player.Level >= 95 && HasFlag(StoryFlag.ReadyForAscension),
                StoryChapter.FinalConfrontation => player.Level >= 100,
                _ => false
            };
        }

        /// <summary>
        /// Advance to the next story chapter
        /// </summary>
        public void AdvanceChapter(Character player)
        {
            if (!CanAdvanceStory(player)) return;

            var previousChapter = CurrentChapter;
            CurrentChapter = GetNextChapter();

            // Update act based on chapter
            UpdateCurrentAct();

            // Log the advancement
            LogEvent(new StoryEvent
            {
                Type = StoryEventType.ChapterAdvance,
                Chapter = CurrentChapter,
                Description = $"Advanced from {previousChapter} to {CurrentChapter}",
                Timestamp = DateTime.Now
            });

            // GD.Print($"[Story] Advanced to chapter: {CurrentChapter}");
        }

        private StoryChapter GetNextChapter()
        {
            return CurrentChapter switch
            {
                StoryChapter.Awakening => StoryChapter.FirstBlood,
                StoryChapter.FirstBlood => StoryChapter.TheStranger,
                StoryChapter.TheStranger => StoryChapter.FactionChoice,
                StoryChapter.FactionChoice => StoryChapter.RisingPower,
                StoryChapter.RisingPower => StoryChapter.TheWhispers,
                StoryChapter.TheWhispers => StoryChapter.FirstGod,
                StoryChapter.FirstGod => StoryChapter.GodWar,
                StoryChapter.GodWar => StoryChapter.TheChoice,
                StoryChapter.TheChoice => StoryChapter.Ascension,
                StoryChapter.Ascension => StoryChapter.FinalConfrontation,
                StoryChapter.FinalConfrontation => StoryChapter.Epilogue,
                _ => CurrentChapter
            };
        }

        private void UpdateCurrentAct()
        {
            CurrentAct = CurrentChapter switch
            {
                StoryChapter.Awakening or StoryChapter.FirstBlood or StoryChapter.TheStranger
                    => StoryAct.Act1_TheNewcomer,
                StoryChapter.FactionChoice or StoryChapter.RisingPower
                    => StoryAct.Act2_RisingPower,
                StoryChapter.TheWhispers or StoryChapter.FirstGod
                    => StoryAct.Act3_TheAwakening,
                StoryChapter.GodWar
                    => StoryAct.Act4_TheCorruption,
                StoryChapter.TheChoice or StoryChapter.Ascension
                    => StoryAct.Act5_TheAscension,
                StoryChapter.FinalConfrontation or StoryChapter.Epilogue
                    => StoryAct.Act6_TheFinal,
                _ => CurrentAct
            };
        }

        /// <summary>
        /// Record a major story choice
        /// </summary>
        public void RecordChoice(string choiceId, string option, int alignmentImpact = 0)
        {
            MajorChoices[choiceId] = new StoryChoice
            {
                ChoiceId = choiceId,
                SelectedOption = option,
                AlignmentImpact = alignmentImpact,
                Timestamp = DateTime.Now
            };

            LogEvent(new StoryEvent
            {
                Type = StoryEventType.MajorChoice,
                Description = $"Made choice '{choiceId}': {option}",
                Timestamp = DateTime.Now
            });

            // GD.Print($"[Story] Recorded choice: {choiceId} = {option}");
        }

        /// <summary>
        /// Set a story flag
        /// </summary>
        public void SetFlag(StoryFlag flag)
        {
            StoryFlags |= (int)flag;
            // GD.Print($"[Story] Set flag: {flag}");
        }

        /// <summary>
        /// Check if a story flag is set
        /// </summary>
        public bool HasFlag(StoryFlag flag)
        {
            return (StoryFlags & (int)flag) != 0;
        }

        /// <summary>
        /// Set a string-based story flag (for dialogue system)
        /// </summary>
        public void SetStoryFlag(string flag, bool value)
        {
            if (value)
            {
                StringStoryFlags.Add(flag);
                // GD.Print($"[Story] Set string flag: {flag}");
            }
            else
            {
                StringStoryFlags.Remove(flag);
                // GD.Print($"[Story] Cleared string flag: {flag}");
            }
        }

        /// <summary>
        /// Check if a string-based story flag is set
        /// </summary>
        public bool HasStoryFlag(string flag)
        {
            return StringStoryFlags.Contains(flag);
        }

        /// <summary>
        /// Export all string-based story flags for serialization
        /// </summary>
        public Dictionary<string, bool> ExportStringFlags()
        {
            var result = new Dictionary<string, bool>();
            foreach (var flag in StringStoryFlags)
                result[flag] = true;
            return result;
        }

        /// <summary>
        /// Import string-based story flags from serialized data
        /// </summary>
        public void ImportStringFlags(Dictionary<string, bool> flags)
        {
            if (flags == null) return;
            foreach (var kvp in flags)
            {
                if (kvp.Value) StringStoryFlags.Add(kvp.Key);
                else StringStoryFlags.Remove(kvp.Key);
            }
        }

        /// <summary>
        /// Add a completed ending to the cycle history
        /// </summary>
        public void AddCompletedEnding(EndingType ending) => CompletedEndings.Add(ending);

        /// <summary>
        /// Check if a specific choice was made
        /// </summary>
        public bool HasMadeChoice(string choiceId)
        {
            return MajorChoices.ContainsKey(choiceId);
        }

        /// <summary>
        /// Trigger a story event (for dialogue system)
        /// </summary>
        public void TriggerEvent(string eventId, string description)
        {
            LogEvent(new StoryEvent
            {
                Type = StoryEventType.MajorChoice,
                Description = $"Event triggered: {eventId} - {description}",
                Timestamp = DateTime.Now
            });
            // GD.Print($"[Story] Event triggered: {eventId}");
        }

        /// <summary>
        /// Advance directly to a specific chapter
        /// </summary>
        public void AdvanceChapter(StoryChapter chapter)
        {
            var previousChapter = CurrentChapter;
            CurrentChapter = chapter;
            UpdateCurrentAct();

            LogEvent(new StoryEvent
            {
                Type = StoryEventType.ChapterAdvance,
                Chapter = CurrentChapter,
                Description = $"Advanced from {previousChapter} to {CurrentChapter}",
                Timestamp = DateTime.Now
            });

            // GD.Print($"[Story] Advanced to chapter: {CurrentChapter}");
        }

        /// <summary>
        /// Collect an artifact
        /// </summary>
        public void CollectArtifact(ArtifactType artifact)
        {
            if (CollectedArtifacts.Add(artifact))
            {
                LogEvent(new StoryEvent
                {
                    Type = StoryEventType.ArtifactCollected,
                    Description = $"Collected artifact: {artifact}",
                    Timestamp = DateTime.Now
                });
                // GD.Print($"[Story] Collected artifact: {artifact}");
            }
        }

        /// <summary>
        /// Collect a seal (lore item)
        /// </summary>
        public void CollectSeal(SealType seal)
        {
            if (CollectedSeals.Add(seal))
            {
                LogEvent(new StoryEvent
                {
                    Type = StoryEventType.SealCollected,
                    Description = $"Collected seal: {seal}",
                    Timestamp = DateTime.Now
                });
                // GD.Print($"[Story] Collected seal: {seal}");
            }
        }

        /// <summary>
        /// Update an Old God's state (defeated, saved, etc.)
        /// </summary>
        public void UpdateGodState(OldGodType god, GodStatus newStatus)
        {
            if (OldGodStates.TryGetValue(god, out var state))
            {
                var oldStatus = state.Status;
                state.Status = newStatus;

                LogEvent(new StoryEvent
                {
                    Type = StoryEventType.GodStateChange,
                    Description = $"{god} changed from {oldStatus} to {newStatus}",
                    Timestamp = DateTime.Now
                });

                // GD.Print($"[Story] God {god} status: {oldStatus} -> {newStatus}");
            }
        }

        /// <summary>
        /// Get count of defeated gods
        /// </summary>
        public int GetDefeatedGodCount()
        {
            return OldGodStates.Values.Count(g =>
                g.Status == GodStatus.Defeated || g.Status == GodStatus.Saved);
        }

        /// <summary>
        /// Determine which ending path the player is on
        /// </summary>
        public EndingPath GetCurrentEndingPath(Character player)
        {
            // Check alignment balance
            long alignmentScore = player.Chivalry - player.Darkness;

            // Check choices made
            bool madeUsurperChoices = MajorChoices.Values.Count(c => c.AlignmentImpact < 0) >
                                      MajorChoices.Values.Count(c => c.AlignmentImpact > 0);

            // Count saved vs defeated gods
            int savedGods = OldGodStates.Values.Count(g => g.Status == GodStatus.Saved);
            int defeatedGods = OldGodStates.Values.Count(g => g.Status == GodStatus.Defeated);

            if (alignmentScore > 3000 && savedGods >= 2)
                return EndingPath.Savior;
            else if (alignmentScore < -3000 || madeUsurperChoices)
                return EndingPath.Usurper;
            else
                return EndingPath.Defiant;
        }

        /// <summary>
        /// Complete the game with a specific ending
        /// </summary>
        public void CompleteEnding(EndingType ending)
        {
            CompletedEndings.Add(ending);

            LogEvent(new StoryEvent
            {
                Type = StoryEventType.EndingReached,
                Description = $"Completed ending: {ending}",
                Timestamp = DateTime.Now
            });

            // GD.Print($"[Story] Completed ending: {ending}");
        }

        /// <summary>
        /// Full reset for new character - clears ALL story state including seals
        /// Call this when creating a brand new character (not New Game+)
        /// </summary>
        public void FullReset()
        {
            CurrentChapter = StoryChapter.Awakening;
            CurrentAct = StoryAct.Act1_TheNewcomer;
            StoryFlags = 0;
            CurrentGameDay = 1;
            CurrentCycle = 1;

            MajorChoices.Clear();
            CollectedArtifacts.Clear();
            CollectedSeals.Clear(); // Clear seals for new character
            StringStoryFlags.Clear();
            CompletedEndings.Clear();
            CycleCarryoverItems.Clear();
            EventLog.Clear();

            InitializeOldGods();
            InitializeKeyNPCs();

        }

        /// <summary>
        /// Start a new cycle (prestige)
        /// </summary>
        public void StartNewCycle(List<string> carryoverItems)
        {
            CurrentCycle++;
            CycleCarryoverItems = carryoverItems;

            // Reset story state but keep cycle data
            CurrentChapter = StoryChapter.Awakening;
            CurrentAct = StoryAct.Act1_TheNewcomer;
            StoryFlags = 0;
            CurrentGameDay = 1; // Reset game day counter
            MajorChoices.Clear();
            CollectedArtifacts.Clear();
            StringStoryFlags.Clear();
            // Keep CollectedSeals - they persist across cycles

            InitializeOldGods(); // Reset god states
            InitializeKeyNPCs(); // Reset relationships

            LogEvent(new StoryEvent
            {
                Type = StoryEventType.NewCycle,
                Description = $"Started cycle {CurrentCycle}",
                Timestamp = DateTime.Now
            });

        }

        /// <summary>
        /// Start a new cycle with ending type (for endings system)
        /// </summary>
        public void StartNewCycle(EndingType previousEnding)
        {
            CompletedEndings.Add(previousEnding);
            StartNewCycle(new List<string>());
        }

        /// <summary>
        /// Get cycle bonuses based on current cycle number
        /// </summary>
        public CycleBonuses GetCycleBonuses()
        {
            return new CycleBonuses
            {
                ExperienceMultiplier = 1.0f + (CurrentCycle - 1) * 0.05f, // +5% per cycle
                StartingStatBonus = Math.Min(CurrentCycle - 1, 10), // +1 per cycle, max 10
                GoldCarryoverPercent = Math.Min(10 + (CurrentCycle - 1) * 2, 50), // 10-50%
                ShopDiscount = Math.Min((CurrentCycle - 1) * 2, 20), // 0-20%
                UnlockedHiddenClass = CurrentCycle >= 3,
                SecretEndingAvailable = CurrentCycle >= 10 && CompletedEndings.Count >= 3,
                NightmareMode = CurrentCycle >= 5,
                DevCommentary = CurrentCycle >= 50
            };
        }

        /// <summary>
        /// Check if player has joined any faction
        /// </summary>
        private bool HasAnyFaction(Character player)
        {
            return !string.IsNullOrEmpty(player.Team) ||
                   player.King ||
                   HasFlag(StoryFlag.JoinedChurch);
        }

        /// <summary>
        /// Log a story event
        /// </summary>
        private void LogEvent(StoryEvent evt)
        {
            EventLog.Add(evt);
            // Keep log size manageable
            if (EventLog.Count > 500)
                EventLog.RemoveAt(0);
        }

        /// <summary>
        /// Get current story summary for display
        /// </summary>
        public string GetStorySummary()
        {
            return $"Act {(int)CurrentAct}: {CurrentAct.ToString().Replace("_", " ")}\n" +
                   $"Chapter: {CurrentChapter}\n" +
                   $"Artifacts: {CollectedArtifacts.Count}/3\n" +
                   $"Seals: {CollectedSeals.Count}/7\n" +
                   $"Gods Confronted: {GetDefeatedGodCount()}/7\n" +
                   $"Cycle: {CurrentCycle}";
        }

        /// <summary>
        /// Save story state to dictionary for serialization
        /// </summary>
        public Dictionary<string, object> SaveState()
        {
            return new Dictionary<string, object>
            {
                ["CurrentChapter"] = (int)CurrentChapter,
                ["CurrentAct"] = (int)CurrentAct,
                ["StoryFlags"] = StoryFlags,
                ["CurrentCycle"] = CurrentCycle,
                ["MajorChoices"] = MajorChoices.ToDictionary(k => k.Key, v => v.Value.SelectedOption),
                ["CollectedArtifacts"] = CollectedArtifacts.Select(a => (int)a).ToList(),
                ["CollectedSeals"] = CollectedSeals.Select(s => (int)s).ToList(),
                ["CompletedEndings"] = CompletedEndings.Select(e => (int)e).ToList(),
                ["GodStates"] = OldGodStates.ToDictionary(k => (int)k.Key, v => (int)v.Value.Status)
            };
        }

        /// <summary>
        /// Load story state from dictionary
        /// </summary>
        public void LoadState(Dictionary<string, object> state)
        {
            if (state.TryGetValue("CurrentChapter", out var chapter))
                CurrentChapter = (StoryChapter)(int)chapter;
            if (state.TryGetValue("CurrentAct", out var act))
                CurrentAct = (StoryAct)(int)act;
            if (state.TryGetValue("StoryFlags", out var flags))
                StoryFlags = (int)flags;
            if (state.TryGetValue("CurrentCycle", out var cycle))
                CurrentCycle = (int)cycle;

            // Load collections...
            // (Additional deserialization logic would go here)
        }
    }

    #region Enums and Data Classes

    public enum StoryChapter
    {
        Awakening,              // Waking up in dormitory
        FirstBlood,             // First monster kill
        TheStranger,            // Meeting Noctura in disguise
        TheFirstSeal,           // First seal quest unlocked
        FactionChoice,          // Choosing a faction at level 10
        RisingPower,            // Building influence
        TheWhispers,            // First god contact
        FirstGod,               // First artifact, first god encounter
        GodWar,                 // The gods awaken fully
        TheChoice,              // Choose your path
        Ascension,              // Becoming divine
        TheFinalConfrontation,  // Facing Manwe
        FinalConfrontation,     // Alias for compatibility
        Epilogue                // After the ending
    }

    public enum StoryAct
    {
        Act1_TheNewcomer = 1,
        Act2_RisingPower = 2,
        Act3_TheAwakening = 3,
        Act4_TheCorruption = 4,
        Act5_TheAscension = 5,
        Act6_TheFinal = 6
    }

    [Flags]
    public enum StoryFlag
    {
        None = 0,
        MetStranger = 1 << 0,
        HeardWhispers = 1 << 1,
        JoinedChurch = 1 << 2,
        JoinedGang = 1 << 3,
        BecameKing = 1 << 4,
        FirstRomance = 1 << 5,
        WitnessedDeath = 1 << 6,
        ReadyForAscension = 1 << 7,
        KnowsNocturaTruth = 1 << 8,
        AllSealsCollected = 1 << 9,
        AllArtifactsCollected = 1 << 10,
        SavedVeloura = 1 << 11,
        SavedAurelion = 1 << 12,
        AwakenedTerravok = 1 << 13,
        AlliedNoctura = 1 << 14,
        DefeatedMaelketh = 1 << 15,
        DefeatedThorgrim = 1 << 16,
        TrueEndingAvailable = 1 << 17,
        EncounteredNoctura = 1 << 18,
        DefeatedNoctura = 1 << 19,
        Marcus_Healed = 1 << 20,  // Town NPC: Marcus story completion
        Ezra_Died = 1 << 21       // Town NPC: Ezra story completion
    }

    public enum OldGodType
    {
        Maelketh,   // War - Corrupted
        Veloura,    // Love - Dying (saveable)
        Thorgrim,   // Law - Corrupted
        Noctura,    // Shadow - Neutral (ally-able)
        Aurelion,   // Light - Dying (saveable)
        Terravok,   // Earth - Dormant (awakeneable)
        Manwe       // Creation - Final
    }

    public enum GodStatus
    {
        Unknown,
        Imprisoned,  // In divine prison
        Dormant,
        Dying,
        Corrupted,
        Neutral,
        Awakened,
        Hostile,
        Allied,
        Saved,
        Defeated,
        Consumed
    }

    public enum ArtifactType
    {
        CreatorsEye,        // From Maelketh - sees through illusions
        SoulweaversLoom,    // Can save corrupted gods
        ScalesOfLaw,        // From Thorgrim - karmic balance
        ShadowCrown,        // From Noctura - shadow powers
        SunforgedBlade,     // From Aurelion - light weapon
        Worldstone,         // From Terravok - earth power
        VoidKey             // Unlocks Manwe's prison
    }

    public enum SealType
    {
        Creation,           // Seal 1 - The Beginning
        FirstWar,           // Seal 2 - The First War
        Corruption,         // Seal 3 - The Twisting
        Imprisonment,       // Seal 4 - The Eternal Chains
        Prophecy,           // Seal 5 - The Foretelling
        Regret,             // Seal 6 - The Creator's Tears
        Truth               // Seal 7 - The Final Truth
    }

    public enum EndingType
    {
        Usurper,      // Consume all, become sole god
        Savior,       // Restore pantheon, join as equal
        Defiant,      // Kill all gods, stay mortal
        TrueEnding,   // The True Ending - balance achieved
        Secret        // The Awakening - discover you are Manwe
    }

    public enum EndingPath
    {
        Usurper,
        Savior,
        Defiant
    }

    public class OldGodState
    {
        public string Name { get; set; } = "";
        public string Title { get; set; } = "";
        public string Domain { get; set; } = "";
        public GodStatus Status { get; set; }
        public bool CanBeSaved { get; set; }
        public string Location { get; set; } = "";
        public int DungeonFloor { get; set; }
        public string Description { get; set; } = "";
        public bool HasBeenEncountered { get; set; }
    }

    public class StoryChoice
    {
        public string ChoiceId { get; set; } = "";
        public string SelectedOption { get; set; } = "";
        public int AlignmentImpact { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public enum StoryEventType
    {
        ChapterAdvance,
        MajorChoice,
        ArtifactCollected,
        SealCollected,
        GodStateChange,
        EndingReached,
        NewCycle
    }

    public class StoryEvent
    {
        public StoryEventType Type { get; set; }
        public StoryChapter Chapter { get; set; }
        public string Description { get; set; } = "";
        public DateTime Timestamp { get; set; }
    }

    public class CycleBonuses
    {
        public float ExperienceMultiplier { get; set; } = 1.0f;
        public float ExpMultiplier { get; set; } = 1.0f;
        public int StartingStatBonus { get; set; }
        public int StrengthBonus { get; set; }
        public int DefenceBonus { get; set; }
        public int StaminaBonus { get; set; }
        public int GoldBonus { get; set; }
        public int ChivalryBonus { get; set; }
        public int DarknessBonus { get; set; }
        public int GoldCarryoverPercent { get; set; }
        public int ShopDiscount { get; set; }
        public bool UnlockedHiddenClass { get; set; }
        public bool SecretEndingAvailable { get; set; }
        public bool NightmareMode { get; set; }
        public bool DevCommentary { get; set; }
        public bool KeepsArtifactKnowledge { get; set; }
        public bool StartWithKey { get; set; }
    }

    #endregion
}
