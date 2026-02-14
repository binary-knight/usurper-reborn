using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Jungian Archetype - The 12 universal character patterns
    /// </summary>
    public enum JungianArchetype
    {
        Hero,       // The warrior who overcomes obstacles
        Caregiver,  // The nurturer who protects others
        Explorer,   // The seeker of new experiences
        Rebel,      // The outlaw who breaks rules
        Sage,       // The seeker of truth and knowledge
        Magician,   // The transformer of reality
        Ruler,      // The leader who controls
        Creator,    // The innovator who builds
        Innocent,   // The optimist seeking paradise
        Lover,      // The romantic seeking connection
        Jester,     // The trickster seeking enjoyment
        Everyman    // The realist seeking belonging
    }

    /// <summary>
    /// Tracks player behavior throughout the game to determine their Jungian archetype.
    /// Scores accumulate based on actions taken during gameplay.
    /// </summary>
    public class ArchetypeTracker
    {
        private static ArchetypeTracker? _fallbackInstance;
        public static ArchetypeTracker Instance
        {
            get
            {
                var ctx = UsurperRemake.Server.SessionContext.Current;
                if (ctx != null) return ctx.Archetype;
                return _fallbackInstance ??= new ArchetypeTracker();
            }
        }

        // Archetype scores - accumulate throughout gameplay
        private Dictionary<JungianArchetype, int> _scores = new();

        // Track specific behaviors for nuanced scoring
        public int BossesDefeated { get; private set; }
        public int MonstersKilled { get; private set; }
        public int CompanionsSaved { get; private set; }
        public int CompanionsLost { get; private set; }
        public int HealingActionsTaken { get; private set; }
        public int DungeonFloorsExplored { get; private set; }
        public int SecretsDiscovered { get; private set; }
        public int RulesDefied { get; private set; }
        public int DarkChoicesMade { get; private set; }
        public int KnowledgeGained { get; private set; }
        public int SpellsCast { get; private set; }
        public int TransformationsMade { get; private set; }
        public int WealthAccumulated { get; private set; }
        public int LeadershipActions { get; private set; }
        public int ItemsCrafted { get; private set; }
        public int RomanceEncounters { get; private set; }
        public int MarriagesFormed { get; private set; }
        public int JokesOrTricksMade { get; private set; }
        public int CharitableActions { get; private set; }
        public int TeamActions { get; private set; }
        public int MercifulChoices { get; private set; }
        public int ArtifactsCollected { get; private set; }
        public int SealsCollected { get; private set; }
        public int PvPVictories { get; private set; }
        public int PvPDefeats { get; private set; }

        public ArchetypeTracker()
        {
            Reset();
        }

        /// <summary>
        /// Reset all archetype scores - call when starting new game
        /// </summary>
        public void Reset()
        {
            _scores = new Dictionary<JungianArchetype, int>();
            foreach (JungianArchetype archetype in Enum.GetValues(typeof(JungianArchetype)))
            {
                _scores[archetype] = 0;
            }

            // Reset behavior counters
            BossesDefeated = 0;
            MonstersKilled = 0;
            CompanionsSaved = 0;
            CompanionsLost = 0;
            HealingActionsTaken = 0;
            DungeonFloorsExplored = 0;
            SecretsDiscovered = 0;
            RulesDefied = 0;
            DarkChoicesMade = 0;
            KnowledgeGained = 0;
            SpellsCast = 0;
            TransformationsMade = 0;
            WealthAccumulated = 0;
            LeadershipActions = 0;
            ItemsCrafted = 0;
            RomanceEncounters = 0;
            MarriagesFormed = 0;
            JokesOrTricksMade = 0;
            CharitableActions = 0;
            TeamActions = 0;
            MercifulChoices = 0;
            ArtifactsCollected = 0;
            SealsCollected = 0;
            PvPVictories = 0;
            PvPDefeats = 0;

            // GD.Print("[Archetype] Tracker reset");
        }

        #region Scoring Methods

        /// <summary>
        /// Add points to a specific archetype
        /// </summary>
        public void AddScore(JungianArchetype archetype, int points)
        {
            if (_scores.ContainsKey(archetype))
            {
                _scores[archetype] += points;
            }
        }

        /// <summary>
        /// Record a monster kill - Hero archetype
        /// </summary>
        public void RecordMonsterKill(int monsterLevel, bool wasRare = false)
        {
            MonstersKilled++;
            int points = 1 + (monsterLevel / 20);
            if (wasRare) points += 2;

            AddScore(JungianArchetype.Hero, points);

            // High kill counts also feed Rebel (violence)
            if (MonstersKilled % 100 == 0)
            {
                AddScore(JungianArchetype.Rebel, 5);
            }
        }

        /// <summary>
        /// Record defeating a boss - Major Hero points
        /// </summary>
        public void RecordBossDefeat(string bossName, int bossLevel)
        {
            BossesDefeated++;
            int points = 10 + (bossLevel / 10);

            AddScore(JungianArchetype.Hero, points);

            // Old Gods grant Sage points (ancient knowledge)
            if (bossName.Contains("God") || bossName.Contains("Maelketh") ||
                bossName.Contains("Manwe") || bossName.Contains("Terravok"))
            {
                AddScore(JungianArchetype.Sage, 5);
                AddScore(JungianArchetype.Magician, 3);
            }

            // GD.Print($"[Archetype] Boss defeated: {bossName} (+{points} Hero)");
        }

        /// <summary>
        /// Record healing someone (self or companion)
        /// </summary>
        public void RecordHealingAction(bool healedOther = false)
        {
            HealingActionsTaken++;
            AddScore(JungianArchetype.Caregiver, healedOther ? 3 : 1);
        }

        /// <summary>
        /// Record saving a companion from death
        /// </summary>
        public void RecordCompanionSaved()
        {
            CompanionsSaved++;
            AddScore(JungianArchetype.Caregiver, 10);
            AddScore(JungianArchetype.Hero, 5);
        }

        /// <summary>
        /// Record losing a companion
        /// </summary>
        public void RecordCompanionLost()
        {
            CompanionsLost++;
            // Doesn't directly add archetype points, but affects final calculation
        }

        /// <summary>
        /// Record exploring a new dungeon floor
        /// </summary>
        public void RecordDungeonExploration(int floorLevel)
        {
            DungeonFloorsExplored++;
            int points = 1 + (floorLevel / 25);

            AddScore(JungianArchetype.Explorer, points);

            // Deep exploration (50+) shows dedication
            if (floorLevel >= 50)
            {
                AddScore(JungianArchetype.Hero, 2);
            }
            if (floorLevel >= 90)
            {
                AddScore(JungianArchetype.Sage, 3); // Seeking ultimate truth
            }
        }

        /// <summary>
        /// Record discovering a secret (hidden room, easter egg, etc.)
        /// </summary>
        public void RecordSecretDiscovered()
        {
            SecretsDiscovered++;
            AddScore(JungianArchetype.Explorer, 5);
            AddScore(JungianArchetype.Sage, 2);
        }

        /// <summary>
        /// Record defying authority or rules
        /// </summary>
        public void RecordRuleDefiance(string context = "")
        {
            RulesDefied++;
            AddScore(JungianArchetype.Rebel, 5);

            // Some defiance shows bravery
            AddScore(JungianArchetype.Hero, 1);

            // GD.Print($"[Archetype] Rule defied: {context} (+5 Rebel)");
        }

        /// <summary>
        /// Record making a dark/evil choice
        /// </summary>
        public void RecordDarkChoice(string context = "")
        {
            DarkChoicesMade++;
            AddScore(JungianArchetype.Rebel, 3);

            // Reduce Innocent and Caregiver
            AddScore(JungianArchetype.Innocent, -2);
            AddScore(JungianArchetype.Caregiver, -1);

            // GD.Print($"[Archetype] Dark choice: {context}");
        }

        /// <summary>
        /// Record gaining knowledge (reading, learning spells, etc.)
        /// </summary>
        public void RecordKnowledgeGained(int points = 1)
        {
            KnowledgeGained++;
            AddScore(JungianArchetype.Sage, points);
        }

        /// <summary>
        /// Record casting a spell
        /// </summary>
        public void RecordSpellCast(bool wasTransformative = false)
        {
            SpellsCast++;
            AddScore(JungianArchetype.Magician, 1);

            if (wasTransformative)
            {
                TransformationsMade++;
                AddScore(JungianArchetype.Magician, 3);
            }
        }

        /// <summary>
        /// Record accumulating wealth
        /// </summary>
        public void RecordWealthGain(long goldAmount)
        {
            WealthAccumulated += (int)Math.Min(goldAmount, int.MaxValue);

            // Wealth in chunks grants Ruler points
            int rulerPoints = (int)(goldAmount / 10000);
            if (rulerPoints > 0)
            {
                AddScore(JungianArchetype.Ruler, rulerPoints);
            }
        }

        /// <summary>
        /// Record becoming or acting as leader (team leader, king, etc.)
        /// </summary>
        public void RecordLeadershipAction(string context = "")
        {
            LeadershipActions++;
            AddScore(JungianArchetype.Ruler, 5);

            // GD.Print($"[Archetype] Leadership: {context} (+5 Ruler)");
        }

        /// <summary>
        /// Record becoming King
        /// </summary>
        public void RecordBecameKing()
        {
            AddScore(JungianArchetype.Ruler, 50);
            AddScore(JungianArchetype.Hero, 10);
            // GD.Print("[Archetype] Became King! (+50 Ruler, +10 Hero)");
        }

        /// <summary>
        /// Record a romantic encounter
        /// </summary>
        public void RecordRomanceEncounter(bool wasIntimate = false)
        {
            RomanceEncounters++;
            AddScore(JungianArchetype.Lover, wasIntimate ? 5 : 2);
        }

        /// <summary>
        /// Record getting married
        /// </summary>
        public void RecordMarriage()
        {
            MarriagesFormed++;
            AddScore(JungianArchetype.Lover, 15);
            AddScore(JungianArchetype.Caregiver, 5); // Commitment to another

            // GD.Print("[Archetype] Marriage recorded (+15 Lover)");
        }

        /// <summary>
        /// Record having a child
        /// </summary>
        public void RecordChildBorn()
        {
            AddScore(JungianArchetype.Caregiver, 10);
            AddScore(JungianArchetype.Creator, 5);
            AddScore(JungianArchetype.Lover, 3);
        }

        /// <summary>
        /// Record making a joke or playing a trick
        /// </summary>
        public void RecordJokeOrTrick()
        {
            JokesOrTricksMade++;
            AddScore(JungianArchetype.Jester, 3);
        }

        /// <summary>
        /// Record a charitable action (giving gold, helping poor, etc.)
        /// </summary>
        public void RecordCharitableAction(long goldGiven = 0)
        {
            CharitableActions++;
            int points = 3 + (int)(goldGiven / 1000);
            AddScore(JungianArchetype.Caregiver, points);
            AddScore(JungianArchetype.Innocent, 2); // Pure heart
        }

        /// <summary>
        /// Record team-based action (joining team, helping teammate)
        /// </summary>
        public void RecordTeamAction()
        {
            TeamActions++;
            AddScore(JungianArchetype.Everyman, 3);
        }

        /// <summary>
        /// Record showing mercy (sparing enemy, forgiving debt, etc.)
        /// </summary>
        public void RecordMercifulChoice()
        {
            MercifulChoices++;
            AddScore(JungianArchetype.Caregiver, 3);
            AddScore(JungianArchetype.Innocent, 2);

            // Mercy can sometimes cost Rebel points
            AddScore(JungianArchetype.Rebel, -1);
        }

        /// <summary>
        /// Record collecting an artifact
        /// </summary>
        public void RecordArtifactCollected()
        {
            ArtifactsCollected++;
            AddScore(JungianArchetype.Explorer, 5);
            AddScore(JungianArchetype.Sage, 3);
        }

        /// <summary>
        /// Record collecting a seal
        /// </summary>
        public void RecordSealCollected()
        {
            SealsCollected++;
            AddScore(JungianArchetype.Sage, 5);
            AddScore(JungianArchetype.Magician, 3);
            AddScore(JungianArchetype.Explorer, 2);
        }

        /// <summary>
        /// Record PvP combat outcome
        /// </summary>
        public void RecordPvPOutcome(bool victory, bool killedOpponent = false)
        {
            if (victory)
            {
                PvPVictories++;
                AddScore(JungianArchetype.Hero, 5);
                AddScore(JungianArchetype.Rebel, 3);

                if (killedOpponent)
                {
                    AddScore(JungianArchetype.Rebel, 5);
                    AddScore(JungianArchetype.Innocent, -3);
                }
            }
            else
            {
                PvPDefeats++;
            }
        }

        /// <summary>
        /// Record choosing the light/good path at a moral choice
        /// </summary>
        public void RecordLightChoice()
        {
            AddScore(JungianArchetype.Hero, 3);
            AddScore(JungianArchetype.Caregiver, 2);
            AddScore(JungianArchetype.Innocent, 2);
        }

        /// <summary>
        /// Record Ocean Philosophy awakening progress
        /// </summary>
        public void RecordOceanAwakening(int level)
        {
            int points = level * 3;
            AddScore(JungianArchetype.Sage, points);
            AddScore(JungianArchetype.Magician, points / 2);
        }

        /// <summary>
        /// Record creating/crafting something
        /// </summary>
        public void RecordCreation()
        {
            ItemsCrafted++;
            AddScore(JungianArchetype.Creator, 5);
        }

        /// <summary>
        /// Record starting a business or economic venture
        /// </summary>
        public void RecordBusinessAction()
        {
            AddScore(JungianArchetype.Creator, 2);
            AddScore(JungianArchetype.Ruler, 2);
        }

        #endregion

        #region Archetype Determination

        /// <summary>
        /// Get the dominant archetype based on accumulated scores
        /// </summary>
        public JungianArchetype GetDominantArchetype()
        {
            return _scores.OrderByDescending(x => x.Value).First().Key;
        }

        /// <summary>
        /// Get the secondary archetype (runner-up)
        /// </summary>
        public JungianArchetype GetSecondaryArchetype()
        {
            return _scores.OrderByDescending(x => x.Value).Skip(1).First().Key;
        }

        /// <summary>
        /// Get all archetype scores sorted by value
        /// </summary>
        public List<(JungianArchetype archetype, int score)> GetAllScores()
        {
            return _scores.OrderByDescending(x => x.Value)
                          .Select(x => (x.Key, x.Value))
                          .ToList();
        }

        /// <summary>
        /// Get the score for a specific archetype
        /// </summary>
        public int GetScore(JungianArchetype archetype)
        {
            return _scores.TryGetValue(archetype, out int score) ? score : 0;
        }

        /// <summary>
        /// Get archetype name and description
        /// </summary>
        public static (string name, string title, string description, string color) GetArchetypeInfo(JungianArchetype archetype)
        {
            return archetype switch
            {
                JungianArchetype.Hero => (
                    "The Hero",
                    "Champion of the Realm",
                    "You faced every challenge head-on, conquering monsters and bosses alike. " +
                    "Your courage in battle defined your journey. The realm will remember you as a warrior who never backed down.",
                    "bright_yellow"
                ),
                JungianArchetype.Caregiver => (
                    "The Caregiver",
                    "Guardian of Souls",
                    "You chose protection over destruction, healing over harm. " +
                    "Your companions survived because of your sacrifice. The realm knows you as one who puts others first.",
                    "bright_green"
                ),
                JungianArchetype.Explorer => (
                    "The Explorer",
                    "Seeker of Horizons",
                    "No dungeon floor was too deep, no secret too hidden. " +
                    "You mapped the unknown and collected what others feared to touch. Curiosity was your compass.",
                    "cyan"
                ),
                JungianArchetype.Rebel => (
                    "The Rebel",
                    "Breaker of Chains",
                    "You refused to play by their rules. Authority meant nothing to you. " +
                    "Whether through darkness or defiance, you carved your own path through a world that tried to contain you.",
                    "bright_red"
                ),
                JungianArchetype.Sage => (
                    "The Sage",
                    "Keeper of Ancient Wisdom",
                    "Knowledge was your true treasure. You collected seals, understood the Ocean's truth, " +
                    "and sought meaning beyond mere combat. The mysteries of the realm revealed themselves to you.",
                    "bright_blue"
                ),
                JungianArchetype.Magician => (
                    "The Magician",
                    "Weaver of Reality",
                    "Magic flowed through your actions. You didn't just fight - you transformed. " +
                    "Spells were your language, and the arcane was your domain. Reality bent to your will.",
                    "bright_magenta"
                ),
                JungianArchetype.Ruler => (
                    "The Ruler",
                    "Master of Dominion",
                    "You accumulated power, wealth, and influence. Leadership came naturally. " +
                    "Whether as king or kingmaker, you understood that true strength lies in control.",
                    "yellow"
                ),
                JungianArchetype.Creator => (
                    "The Creator",
                    "Architect of Dreams",
                    "Building and crafting defined your journey. Where others destroyed, you made something new. " +
                    "Your legacy is not in what you conquered, but in what you created.",
                    "white"
                ),
                JungianArchetype.Innocent => (
                    "The Innocent",
                    "Keeper of Faith",
                    "Despite the darkness, you maintained your purity. Mercy over vengeance, hope over despair. " +
                    "The realm's corruption could not touch your soul. You proved goodness can survive.",
                    "bright_white"
                ),
                JungianArchetype.Lover => (
                    "The Lover",
                    "Heart of Passion",
                    "Connection was your quest. Romance, intimacy, and emotional bonds drove your choices. " +
                    "The realm remembers not your battles, but your loves - and perhaps, your heartbreaks.",
                    "bright_red"
                ),
                JungianArchetype.Jester => (
                    "The Jester",
                    "Fool's Wisdom",
                    "Life was never too serious for you. Jokes, tricks, and unexpected choices marked your path. " +
                    "Where others saw gravity, you found levity. The realm learned to laugh again.",
                    "bright_yellow"
                ),
                JungianArchetype.Everyman => (
                    "The Everyman",
                    "Common Champion",
                    "You weren't the mightiest or the cleverest, but you were reliable. " +
                    "Team player, steady hand, always there when needed. True heroism doesn't require a crown.",
                    "gray"
                ),
                _ => ("Unknown", "Mysterious One", "Your nature defies classification.", "white")
            };
        }

        /// <summary>
        /// Get a personalized quote based on archetype
        /// </summary>
        public static string GetArchetypeQuote(JungianArchetype archetype)
        {
            return archetype switch
            {
                JungianArchetype.Hero => "\"A hero is someone who has given their life to something bigger than themselves.\" - Joseph Campbell",
                JungianArchetype.Caregiver => "\"The greatest gift is a portion of thyself.\" - Ralph Waldo Emerson",
                JungianArchetype.Explorer => "\"Not all those who wander are lost.\" - J.R.R. Tolkien",
                JungianArchetype.Rebel => "\"Well-behaved women seldom make history.\" - Laurel Thatcher Ulrich",
                JungianArchetype.Sage => "\"The only true wisdom is knowing you know nothing.\" - Socrates",
                JungianArchetype.Magician => "\"Magic is believing in yourself. If you can do that, you can make anything happen.\" - Goethe",
                JungianArchetype.Ruler => "\"Heavy is the head that wears the crown.\" - Shakespeare",
                JungianArchetype.Creator => "\"Every act of creation is first an act of destruction.\" - Picasso",
                JungianArchetype.Innocent => "\"Blessed are the pure in heart, for they shall see God.\" - Matthew 5:8",
                JungianArchetype.Lover => "\"Love is composed of a single soul inhabiting two bodies.\" - Aristotle",
                JungianArchetype.Jester => "\"If we couldn't laugh we would all go insane.\" - Robert Frost",
                JungianArchetype.Everyman => "\"It's a dangerous business going out your door.\" - J.R.R. Tolkien",
                _ => "\"Know thyself.\" - Oracle of Delphi"
            };
        }

        #endregion

        #region Serialization

        /// <summary>
        /// Serialize tracker data for saving
        /// </summary>
        public ArchetypeTrackerData Serialize()
        {
            return new ArchetypeTrackerData
            {
                Scores = new Dictionary<int, int>(_scores.Select(x =>
                    new KeyValuePair<int, int>((int)x.Key, x.Value))),
                BossesDefeated = BossesDefeated,
                MonstersKilled = MonstersKilled,
                CompanionsSaved = CompanionsSaved,
                CompanionsLost = CompanionsLost,
                HealingActionsTaken = HealingActionsTaken,
                DungeonFloorsExplored = DungeonFloorsExplored,
                SecretsDiscovered = SecretsDiscovered,
                RulesDefied = RulesDefied,
                DarkChoicesMade = DarkChoicesMade,
                KnowledgeGained = KnowledgeGained,
                SpellsCast = SpellsCast,
                TransformationsMade = TransformationsMade,
                WealthAccumulated = WealthAccumulated,
                LeadershipActions = LeadershipActions,
                ItemsCrafted = ItemsCrafted,
                RomanceEncounters = RomanceEncounters,
                MarriagesFormed = MarriagesFormed,
                JokesOrTricksMade = JokesOrTricksMade,
                CharitableActions = CharitableActions,
                TeamActions = TeamActions,
                MercifulChoices = MercifulChoices,
                ArtifactsCollected = ArtifactsCollected,
                SealsCollected = SealsCollected,
                PvPVictories = PvPVictories,
                PvPDefeats = PvPDefeats
            };
        }

        /// <summary>
        /// Restore tracker data from save
        /// </summary>
        public void Deserialize(ArchetypeTrackerData data)
        {
            if (data == null) return;

            _scores.Clear();
            foreach (var kvp in data.Scores)
            {
                if (Enum.IsDefined(typeof(JungianArchetype), kvp.Key))
                {
                    _scores[(JungianArchetype)kvp.Key] = kvp.Value;
                }
            }

            // Ensure all archetypes have entries
            foreach (JungianArchetype archetype in Enum.GetValues(typeof(JungianArchetype)))
            {
                if (!_scores.ContainsKey(archetype))
                    _scores[archetype] = 0;
            }

            BossesDefeated = data.BossesDefeated;
            MonstersKilled = data.MonstersKilled;
            CompanionsSaved = data.CompanionsSaved;
            CompanionsLost = data.CompanionsLost;
            HealingActionsTaken = data.HealingActionsTaken;
            DungeonFloorsExplored = data.DungeonFloorsExplored;
            SecretsDiscovered = data.SecretsDiscovered;
            RulesDefied = data.RulesDefied;
            DarkChoicesMade = data.DarkChoicesMade;
            KnowledgeGained = data.KnowledgeGained;
            SpellsCast = data.SpellsCast;
            TransformationsMade = data.TransformationsMade;
            WealthAccumulated = data.WealthAccumulated;
            LeadershipActions = data.LeadershipActions;
            ItemsCrafted = data.ItemsCrafted;
            RomanceEncounters = data.RomanceEncounters;
            MarriagesFormed = data.MarriagesFormed;
            JokesOrTricksMade = data.JokesOrTricksMade;
            CharitableActions = data.CharitableActions;
            TeamActions = data.TeamActions;
            MercifulChoices = data.MercifulChoices;
            ArtifactsCollected = data.ArtifactsCollected;
            SealsCollected = data.SealsCollected;
            PvPVictories = data.PvPVictories;
            PvPDefeats = data.PvPDefeats;

            // GD.Print($"[Archetype] Loaded tracker data - Dominant: {GetDominantArchetype()}");
        }

        #endregion
    }

    /// <summary>
    /// Serializable data for archetype tracker
    /// </summary>
    public class ArchetypeTrackerData
    {
        public Dictionary<int, int> Scores { get; set; } = new();
        public int BossesDefeated { get; set; }
        public int MonstersKilled { get; set; }
        public int CompanionsSaved { get; set; }
        public int CompanionsLost { get; set; }
        public int HealingActionsTaken { get; set; }
        public int DungeonFloorsExplored { get; set; }
        public int SecretsDiscovered { get; set; }
        public int RulesDefied { get; set; }
        public int DarkChoicesMade { get; set; }
        public int KnowledgeGained { get; set; }
        public int SpellsCast { get; set; }
        public int TransformationsMade { get; set; }
        public int WealthAccumulated { get; set; }
        public int LeadershipActions { get; set; }
        public int ItemsCrafted { get; set; }
        public int RomanceEncounters { get; set; }
        public int MarriagesFormed { get; set; }
        public int JokesOrTricksMade { get; set; }
        public int CharitableActions { get; set; }
        public int TeamActions { get; set; }
        public int MercifulChoices { get; set; }
        public int ArtifactsCollected { get; set; }
        public int SealsCollected { get; set; }
        public int PvPVictories { get; set; }
        public int PvPDefeats { get; set; }
    }
}
