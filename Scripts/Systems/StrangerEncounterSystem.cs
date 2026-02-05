using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// The Stranger Encounter System - Tracks mysterious encounters with Noctura
    /// throughout the game. She appears in various disguises, dropping cryptic hints
    /// and manipulating events from the shadows.
    ///
    /// "She set these events in motion. Her true motives remain hidden."
    /// </summary>
    public class StrangerEncounterSystem
    {
        private static StrangerEncounterSystem? _instance;
        public static StrangerEncounterSystem Instance => _instance ??= new StrangerEncounterSystem();

        // Encounter tracking
        public int EncountersHad { get; private set; } = 0;
        public HashSet<StrangerDisguise> EncounteredDisguises { get; private set; } = new();
        public List<StrangerEncounter> EncounterLog { get; private set; } = new();
        public bool PlayerSuspectsStranger { get; private set; } = false;
        public bool PlayerKnowsTruth { get; private set; } = false;

        // Cooldown tracking (prevent too many encounters in a row)
        private int _actionsSinceLastEncounter = 0;
        private const int MinActionsBetweenEncounters = 20;

        // Random for encounter chances
        private readonly Random _random = new();

        /// <summary>
        /// All possible Stranger disguises with their contexts
        /// </summary>
        public static readonly Dictionary<StrangerDisguise, StrangerDisguiseData> Disguises = new()
        {
            [StrangerDisguise.HoodedTraveler] = new StrangerDisguiseData(
                "A Hooded Traveler",
                "A cloaked figure with eyes that seem to hold starlight",
                new[] { "MainStreet", "Inn", "DarkAlley" },
                1, 10
            ),
            [StrangerDisguise.OldBeggar] = new StrangerDisguiseData(
                "An Old Beggar",
                "A weathered crone with knowing eyes too sharp for her apparent age",
                new[] { "MainStreet", "Temple", "Church" },
                1, 20
            ),
            [StrangerDisguise.TavernPatron] = new StrangerDisguiseData(
                "A Quiet Patron",
                "A shadowy figure in the corner, nursing a drink that never empties",
                new[] { "Inn", "DarkAlley" },
                5, 30
            ),
            [StrangerDisguise.MysteriousMerchant] = new StrangerDisguiseData(
                "A Mysterious Merchant",
                "A seller of curiosities with wares that seem to shift when you look away",
                new[] { "Marketplace", "MagicShop", "DarkAlley" },
                10, 40
            ),
            [StrangerDisguise.WoundedKnight] = new StrangerDisguiseData(
                "A Wounded Knight",
                "A figure in battered armor, but their wounds don't bleed quite right",
                new[] { "Healer", "Temple", "MainStreet" },
                15, 50
            ),
            [StrangerDisguise.DreamVisitor] = new StrangerDisguiseData(
                "A Figure in Dreams",
                "A shadow that speaks with a voice like distant thunder",
                new[] { "Inn", "Home", "Dormitory" },
                20, 60
            ),
            [StrangerDisguise.TempleSupplicant] = new StrangerDisguiseData(
                "A Temple Supplicant",
                "A worshipper who prays to no altar, yet the candles flicker at their presence",
                new[] { "Temple", "Church" },
                25, 70
            ),
            [StrangerDisguise.ShadowInMirror] = new StrangerDisguiseData(
                "A Shadow in the Mirror",
                "Your reflection blinks when you don't",
                new[] { "Home", "Inn" },
                40, 80
            ),
            [StrangerDisguise.TrueForm] = new StrangerDisguiseData(
                "The Shadow Weaver",
                "Noctura reveals herself, darkness coiling around her like a living cloak",
                new[] { "DarkAlley", "Dungeon" },
                50, 100
            )
        };

        /// <summary>
        /// Dialogue options based on encounter number and player state
        /// </summary>
        private static readonly List<StrangerDialogue> DialoguePool = new()
        {
            // Early game (encounters 1-2)
            new StrangerDialogue(1, 2, 0, false, new[] {
                "Have we met? No... no, we haven't. But we will.",
                "Interesting. You walk like someone who doesn't know where they're going.",
                "The dormitory has seen many who wake without memories. Few remember... eventually.",
                "The deeper you go, the more you'll find. Whether you want to or not."
            }),

            // Early-mid game (encounters 3-4)
            new StrangerDialogue(3, 4, 0, false, new[] {
                "You're progressing nicely. Exactly as expected.",
                "The seals call to you, don't they? Of course they do.",
                "The other gods fear what's coming. I... anticipate it.",
                "Do you ever wonder why YOU woke up in that dormitory?"
            }),

            // Mid game (encounters 5-6)
            new StrangerDialogue(5, 6, 0, false, new[] {
                "Maelketh will try to break you. Let him. Breaking can be... illuminating.",
                "Veloura still loves, you know. Even now. Especially now.",
                "The wave does not know it is the ocean. Not yet.",
                "I set all of this in motion. The question is: why did I choose YOU?"
            }),

            // Late game (encounters 7-8)
            new StrangerDialogue(7, 8, 0, false, new[] {
                "You're beginning to remember, aren't you? Little flashes. Echoes.",
                "Manwe is tired. So very tired. Can you feel it?",
                "The cycle must break. Or perhaps... it must complete. I wonder which.",
                "When you face the Creator, ask him about the first wave. Watch his face."
            }),

            // Endgame (encounters 9+)
            new StrangerDialogue(9, 99, 0, false, new[] {
                "We are alike, you and I. Both fragments trying to remember the whole.",
                "I was there at the beginning. I will be there at the end. Will you?",
                "The Ocean dreams, and we are its nightmares. Or its hopes. Perhaps both.",
                "When you finally wake up... will you remember this conversation?"
            }),

            // If player suspects (high wisdom or multiple encounters)
            new StrangerDialogue(3, 99, 30, true, new[] {
                "*Their smile widens* Clever. But knowing my name changes nothing.",
                "Noctura? Yes. The Shadow Weaver. The Necessary Evil. The Only One Who Remembers.",
                "I could have hidden better. I CHOSE to let you see. Think about why.",
                "You suspect, but you don't understand. That's fine. Understanding comes later."
            }),

            // If player knows the truth about themselves
            new StrangerDialogue(1, 99, 0, false, new[] {
                "Ah. You've started to remember. Good. The dream is almost over.",
                "Father... brother... self. Words fail when boundaries dissolve.",
                "I've waited so long for you to wake. The others gave up. I never did."
            }, requiresAwakening: 5)
        };

        public StrangerEncounterSystem()
        {
            _instance = this;
        }

        /// <summary>
        /// Increment action counter and check for random encounter
        /// Called from various game actions
        /// </summary>
        public void OnPlayerAction(string location, Character player)
        {
            _actionsSinceLastEncounter++;
        }

        /// <summary>
        /// Check if a stranger encounter should trigger at this location
        /// </summary>
        public bool ShouldTriggerEncounter(string location, Character player)
        {
            // Don't trigger too frequently
            if (_actionsSinceLastEncounter < MinActionsBetweenEncounters)
                return false;

            // Don't trigger if already revealed identity
            if (PlayerKnowsTruth && EncountersHad > 10)
                return false;

            // Find valid disguises for this location
            var validDisguises = Disguises
                .Where(d => d.Value.ValidLocations.Contains(location) &&
                           player.Level >= d.Value.MinLevel &&
                           player.Level <= d.Value.MaxLevel &&
                           !EncounteredDisguises.Contains(d.Key))
                .ToList();

            if (!validDisguises.Any())
            {
                // Fall back to repeatable disguises
                validDisguises = Disguises
                    .Where(d => d.Value.ValidLocations.Contains(location) &&
                               player.Level >= d.Value.MinLevel)
                    .ToList();
            }

            if (!validDisguises.Any())
                return false;

            // Base chance increases with player level and decreases with recent encounters
            float baseChance = 0.05f + (player.Level * 0.002f);
            float cooldownFactor = Math.Min(1.0f, _actionsSinceLastEncounter / 50.0f);

            // Higher chance if player hasn't met the Stranger yet
            if (EncountersHad == 0)
                baseChance *= 2.0f;

            // Lower chance after many encounters
            if (EncountersHad > 5)
                baseChance *= 0.5f;

            return _random.NextDouble() < (baseChance * cooldownFactor);
        }

        /// <summary>
        /// Get the next encounter for this location and player
        /// </summary>
        public StrangerEncounter? GetEncounter(string location, Character player)
        {
            var validDisguises = Disguises
                .Where(d => d.Value.ValidLocations.Contains(location) &&
                           player.Level >= d.Value.MinLevel)
                .OrderByDescending(d => EncounteredDisguises.Contains(d.Key) ? 0 : 1)
                .ThenBy(_ => _random.Next())
                .ToList();

            if (!validDisguises.Any())
                return null;

            var disguise = validDisguises.First().Key;
            var disguiseData = Disguises[disguise];

            // Determine if player suspects
            bool playerSuspects = PlayerSuspectsStranger ||
                                 player.Wisdom > 50 ||
                                 EncountersHad >= 5;

            // Get awakening level
            int awakening = OceanPhilosophySystem.Instance?.AwakeningLevel ?? 0;

            // Find appropriate dialogue
            var dialogue = GetDialogue(player, playerSuspects, awakening);

            return new StrangerEncounter
            {
                Disguise = disguise,
                DisguiseData = disguiseData,
                Dialogue = dialogue,
                Location = location,
                EncounterNumber = EncountersHad + 1,
                PlayerSuspects = playerSuspects
            };
        }

        /// <summary>
        /// Record that an encounter happened
        /// </summary>
        public void RecordEncounter(StrangerEncounter encounter)
        {
            EncountersHad++;
            _actionsSinceLastEncounter = 0;
            EncounteredDisguises.Add(encounter.Disguise);
            EncounterLog.Add(encounter);

            // Set story flag on first encounter
            if (EncountersHad == 1)
            {
                StoryProgressionSystem.Instance?.SetFlag(StoryFlag.MetStranger);
            }

            // After enough encounters, player starts to suspect
            if (EncountersHad >= 4 && !PlayerSuspectsStranger)
            {
                PlayerSuspectsStranger = true;
            }

            GD.Print($"[Stranger] Encounter #{EncountersHad} recorded: {encounter.Disguise}");
        }

        /// <summary>
        /// Reveal the Stranger's true identity
        /// </summary>
        public void RevealTruth()
        {
            PlayerKnowsTruth = true;
            StoryProgressionSystem.Instance?.SetFlag(StoryFlag.KnowsNocturaTruth);
            GD.Print("[Stranger] Player now knows Noctura's true identity");
        }

        /// <summary>
        /// Get appropriate dialogue for current state
        /// </summary>
        private string GetDialogue(Character player, bool suspects, int awakening)
        {
            var validDialogues = DialoguePool
                .Where(d => EncountersHad >= d.MinEncounter &&
                           EncountersHad <= d.MaxEncounter &&
                           player.Wisdom >= d.MinWisdom &&
                           (!d.RequiresPlayerSuspects || suspects) &&
                           (d.RequiresAwakening == 0 || awakening >= d.RequiresAwakening))
                .ToList();

            if (!validDialogues.Any())
            {
                // Fallback
                return "The figure watches you with eyes that seem to hold secrets.";
            }

            var chosen = validDialogues[_random.Next(validDialogues.Count)];
            return chosen.Lines[_random.Next(chosen.Lines.Length)];
        }

        /// <summary>
        /// Get player response options for an encounter
        /// </summary>
        public List<(string key, string text, string response)> GetResponseOptions(StrangerEncounter encounter, Character player)
        {
            var options = new List<(string, string, string)>();

            // Always available
            options.Add(("1", "Ask who they are",
                encounter.PlayerSuspects ?
                    "\"Names are masks we wear to hide the void within. You may call me... a friend.\"" :
                    "\"Nobody of consequence. A traveler, like yourself.\""));

            options.Add(("2", "Ask what they want",
                EncountersHad < 3 ?
                    "\"To watch. To wait. To see if the pattern holds.\"" :
                    "\"The same thing I've always wanted. For someone to finally break the cycle.\""));

            // If player suspects
            if (encounter.PlayerSuspects)
            {
                options.Add(("3", "Accuse them of being the Stranger",
                    "Their smile doesn't waver. \"The Stranger? That's what they call me? How... quaint.\""));
            }

            // High wisdom option
            if (player.Wisdom >= 40)
            {
                options.Add(("4", "Ask about the Ocean",
                    "Their eyes widen almost imperceptibly. \"Ah. You've heard the whispers. Good. Very good.\""));
            }

            // If player knows truth
            if (PlayerKnowsTruth)
            {
                options.Add(("5", "Call them Noctura",
                    "\"You remembered.\" Her form shifts, shadows peeling away. \"I wondered when you would.\""));
            }

            // Always available
            options.Add(("0", "Say nothing and leave",
                "They watch you go with an expression that might be amusement, or might be sorrow."));

            return options;
        }

        /// <summary>
        /// Serialize for save
        /// </summary>
        public StrangerEncounterData Serialize()
        {
            return new StrangerEncounterData
            {
                EncountersHad = EncountersHad,
                EncounteredDisguises = EncounteredDisguises.Cast<int>().ToList(),
                PlayerSuspectsStranger = PlayerSuspectsStranger,
                PlayerKnowsTruth = PlayerKnowsTruth,
                ActionsSinceLastEncounter = _actionsSinceLastEncounter
            };
        }

        /// <summary>
        /// Deserialize from save
        /// </summary>
        public void Deserialize(StrangerEncounterData? data)
        {
            if (data == null) return;

            EncountersHad = data.EncountersHad;
            EncounteredDisguises = new HashSet<StrangerDisguise>(
                data.EncounteredDisguises.Cast<StrangerDisguise>());
            PlayerSuspectsStranger = data.PlayerSuspectsStranger;
            PlayerKnowsTruth = data.PlayerKnowsTruth;
            _actionsSinceLastEncounter = data.ActionsSinceLastEncounter;
        }

        /// <summary>
        /// Reset all state for a new game
        /// </summary>
        public void Reset()
        {
            EncountersHad = 0;
            EncounteredDisguises = new HashSet<StrangerDisguise>();
            EncounterLog = new List<StrangerEncounter>();
            PlayerSuspectsStranger = false;
            PlayerKnowsTruth = false;
            _actionsSinceLastEncounter = 0;
            GD.Print("[Stranger] System reset for new game");
        }
    }

    #region Data Classes

    public enum StrangerDisguise
    {
        HoodedTraveler,
        OldBeggar,
        TavernPatron,
        MysteriousMerchant,
        WoundedKnight,
        DreamVisitor,
        TempleSupplicant,
        ShadowInMirror,
        TrueForm
    }

    public class StrangerDisguiseData
    {
        public string Name { get; }
        public string Description { get; }
        public string[] ValidLocations { get; }
        public int MinLevel { get; }
        public int MaxLevel { get; }

        public StrangerDisguiseData(string name, string description, string[] locations, int minLevel, int maxLevel)
        {
            Name = name;
            Description = description;
            ValidLocations = locations;
            MinLevel = minLevel;
            MaxLevel = maxLevel;
        }
    }

    public class StrangerDialogue
    {
        public int MinEncounter { get; }
        public int MaxEncounter { get; }
        public int MinWisdom { get; }
        public bool RequiresPlayerSuspects { get; }
        public int RequiresAwakening { get; }
        public string[] Lines { get; }

        public StrangerDialogue(int minEnc, int maxEnc, int minWis, bool reqSuspects, string[] lines, int requiresAwakening = 0)
        {
            MinEncounter = minEnc;
            MaxEncounter = maxEnc;
            MinWisdom = minWis;
            RequiresPlayerSuspects = reqSuspects;
            RequiresAwakening = requiresAwakening;
            Lines = lines;
        }
    }

    public class StrangerEncounter
    {
        public StrangerDisguise Disguise { get; set; }
        public StrangerDisguiseData DisguiseData { get; set; } = null!;
        public string Dialogue { get; set; } = "";
        public string Location { get; set; } = "";
        public int EncounterNumber { get; set; }
        public bool PlayerSuspects { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class StrangerEncounterData
    {
        public int EncountersHad { get; set; }
        public List<int> EncounteredDisguises { get; set; } = new();
        public bool PlayerSuspectsStranger { get; set; }
        public bool PlayerKnowsTruth { get; set; }
        public int ActionsSinceLastEncounter { get; set; }
    }

    #endregion
}
