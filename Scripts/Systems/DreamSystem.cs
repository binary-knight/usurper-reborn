using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Dream System - Handles dreams during rest, dungeon visions, and
    /// environmental narrative beats between major story events.
    ///
    /// Dreams become more vivid and prophetic as the player approaches
    /// the truth about their identity. The system maintains narrative
    /// tension between Old God encounters.
    /// </summary>
    public class DreamSystem
    {
        private static DreamSystem? _fallbackInstance;
        public static DreamSystem Instance
        {
            get
            {
                var ctx = UsurperRemake.Server.SessionContext.Current;
                if (ctx != null) return ctx.Dreams;
                return _fallbackInstance ??= new DreamSystem();
            }
        }

        // Track experienced dreams
        public HashSet<string> ExperiencedDreams { get; private set; } = new();

        // Track seen dungeon visions (per floor to allow revisiting on different floors)
        public HashSet<string> SeenDungeonVisions { get; private set; } = new();

        // Track last dream to avoid repetition
        private string _lastDreamId = "";
        private int _restsSinceLastDream = 0;

        /// <summary>
        /// All possible dreams organized by player progress
        /// </summary>
        public static readonly List<NarrativeDreamData> Dreams = new()
        {
            // ====== EARLY GAME DREAMS (Levels 1-20) ======

            new NarrativeDreamData
            {
                Id = "dream_drowning",
                Title = "Drowning in Light",
                MinLevel = 1, MaxLevel = 15,
                MinAwakening = 0, MaxAwakening = 3,
                Priority = 10,
                Content = new[] {
                    "You dream of drowning.",
                    "Not in water though. In light. Bright, warm light everywhere.",
                    "For a second it doesnt feel like drowning. It feels like home.",
                    "Then you wake up gasping, grabbing at nothing."
                },
                PhilosophicalHint = "That light. You know that light from somewhere."
            },

            new NarrativeDreamData
            {
                Id = "dream_mirror",
                Title = "The Mirror",
                MinLevel = 5, MaxLevel = 25,
                MinAwakening = 0, MaxAwakening = 4,
                Priority = 10,
                Content = new[] {
                    "You stand before a mirror that shouldn't exist.",
                    "Your reflection wears a crown of stars.",
                    "It smiles sadly. 'Remember,' it says.",
                    "You wake with the word 'Manwe' on your lips, though you don't know why."
                },
                AwakeningGain = 1,
                PhilosophicalHint = "The face in the mirror was yours... wasn't it?"
            },

            new NarrativeDreamData
            {
                Id = "dream_voices",
                Title = "A Chorus of Voices",
                MinLevel = 1, MaxLevel = 30,
                MinAwakening = 0, MaxAwakening = 5,
                Priority = 5,
                Content = new[] {
                    "In the dream, you hear voices. Hundreds of them.",
                    "All calling your name. Except the name keeps changing.",
                    "Different name every time. None of them feel wrong.",
                    "You wake up not sure which one is yours."
                },
                PhilosophicalHint = "How many names have you had?"
            },

            // ====== MID GAME DREAMS (Levels 20-50) ======

            new NarrativeDreamData
            {
                Id = "dream_maelketh_before",
                Title = "The Blade Before",
                MinLevel = 20, MaxLevel = 30,
                RequiredFloor = 20, MaxFloor = 24,
                Priority = 20, // High priority - foreshadowing
                Content = new[] {
                    "You dream of a warrior kneeling in a field of swords.",
                    "He looks exhausted. Like hes been fighting for centuries.",
                    "'I cant even remember why I started,' he says.",
                    "He looks up. His eyes are yours. 'Youre coming. I know you are.'"
                },
                PhilosophicalHint = "He looked tired. Really tired."
            },

            new NarrativeDreamData
            {
                Id = "dream_maelketh_after",
                Title = "The Broken Blade",
                MinLevel = 25, MaxLevel = 45,
                RequiresGodDefeated = OldGodType.Maelketh,
                Priority = 15,
                Content = new[] {
                    "Maelketh visits your dreams. Not as an enemy. As a question.",
                    "'Did I deserve peace? Or just an end?'",
                    "You have no answer. Neither does he.",
                    "He fades, still waiting, still wondering."
                },
                AwakeningGain = 1,
                PhilosophicalHint = "You dont have an answer either."
            },

            new NarrativeDreamData
            {
                Id = "dream_veloura_before",
                Title = "Withered Petals",
                MinLevel = 30, MaxLevel = 45,
                RequiredFloor = 35, MaxFloor = 39,
                Priority = 20,
                Content = new[] {
                    "You dream of a garden where roses bleed.",
                    "A woman kneels among them, hands stained red.",
                    "'They used to love me,' she says. 'Now they just take.'",
                    "She looks at you like youre supposed to fix it. You dont know how."
                },
                PhilosophicalHint = "She was begging. But not to you."
            },

            new NarrativeDreamData
            {
                Id = "dream_ocean_first",
                Title = "The Ocean Speaks",
                MinLevel = 30, MaxLevel = 60,
                MinAwakening = 2, MaxAwakening = 5,
                Priority = 15,
                Content = new[] {
                    "You dream of standing at the edge of an ocean.",
                    "Something in the waves seems to recognize you.",
                    "'I dont know what I am,' you say to nobody.",
                    "The sound of the waves changes. Almost like laughing. 'You always say that.'"
                },
                AwakeningGain = 1,
                WaveFragment = WaveFragment.FirstSeparation,
                PhilosophicalHint = "The ocean knew you. How?"
            },

            // ====== LATE GAME DREAMS (Levels 50-80) ======

            new NarrativeDreamData
            {
                Id = "dream_thorgrim_before",
                Title = "The Scales Tip",
                MinLevel = 45, MaxLevel = 60,
                RequiredFloor = 50, MaxFloor = 54,
                Priority = 20,
                Content = new[] {
                    "In the dream, you stand in a courtroom that goes on forever.",
                    "A judge sits on a bone throne. Teh scales in his hand are empty.",
                    "'ORDER,' he thunders. 'WITHOUT ORDER, CHAOS.'",
                    "You want to argue but you cant find the words."
                },
                PhilosophicalHint = "You talked back to a god. In a dream. Bold."
            },

            new NarrativeDreamData
            {
                Id = "dream_noctura_shadow",
                Title = "Shadows Speak",
                MinLevel = 50, MaxLevel = 75,
                RequiredFloor = 60, MaxFloor = 69,
                Priority = 20,
                Content = new[] {
                    "The dream is darkness. But darkness that thinks. That remembers.",
                    "'I've been watching you,' the shadow says. 'Since the beginning.'",
                    "'Why?' you ask.",
                    "'Because you're the only one who might understand. When the time comes.'"
                },
                PhilosophicalHint = "Something has been watching you. For a long time."
            },

            new NarrativeDreamData
            {
                Id = "dream_creation",
                Title = "In the Beginning",
                MinLevel = 60, MaxLevel = 90,
                MinAwakening = 4, MaxAwakening = 7,
                Priority = 15,
                Content = new[] {
                    "You dream of being alone. Completely alone.",
                    "Not lonely. Theres nobody to miss. Just... nothing else exists.",
                    "And then you think: 'What if I made something?'",
                    "You wake up with the strangest feeling that you just remembered how the world began."
                },
                AwakeningGain = 2,
                WaveFragment = WaveFragment.Origin,
                PhilosophicalHint = "Being alone like that. You cant even imagine it."
            },

            // ====== ENDGAME DREAMS (Levels 80+) ======

            new NarrativeDreamData
            {
                Id = "dream_aurelion_fading",
                Title = "The Light Dims",
                MinLevel = 75, MaxLevel = 90,
                RequiredFloor = 80, MaxFloor = 84,
                Priority = 20,
                Content = new[] {
                    "You dream of a single candle in total darkness.",
                    "The flame keeps guttering. Almost going out.",
                    "'Dont forget,' something whispers. 'Please dont forget.'",
                    "You dont know what youre supposed to remember."
                },
                PhilosophicalHint = "It was begging you to remember. Desperate."
            },

            new NarrativeDreamData
            {
                Id = "dream_terravok_deep",
                Title = "The Mountain Dreams",
                MinLevel = 85, MaxLevel = 100,
                RequiredFloor = 90, MaxFloor = 94,
                Priority = 20,
                Content = new[] {
                    "You dream of being a mountain. Heavy. Still. Ancient.",
                    "Youve been here for so long you forgot how to move.",
                    "Somewhere far away, you hear waves.",
                    "'When I wake up,' you think, 'everything changes.' You cant tell if thats good or bad."
                },
                PhilosophicalHint = "Mountains dont dream. But this one did."
            },

            new NarrativeDreamData
            {
                Id = "dream_manwe_waiting",
                Title = "The Weary Creator",
                MinLevel = 90, MaxLevel = 100,
                RequiredFloor = 95, MaxFloor = 99,
                MinAwakening = 5,
                Priority = 25, // Highest priority
                Content = new[] {
                    "You dream of a throne at the bottom of everything.",
                    "Someone sits there. Hes been waiting a very long time.",
                    "'Almost,' he says. 'Youre almost here.'",
                    "You open your mouth and the word that comes out is 'Father.' He just nods."
                },
                AwakeningGain = 2,
                WaveFragment = WaveFragment.TheTruth,
                PhilosophicalHint = "He called you Father. And Child. And Self."
            },

            // ====== COMPANION DEATH DREAMS ======

            new NarrativeDreamData
            {
                Id = "dream_grief_lyris",
                Title = "Starlight Fades",
                RequiresCompanionDeath = "Lyris",
                Priority = 30,
                Content = new[] {
                    "You dream of Lyris.",
                    "Shes standing somewhere bright. She looks peaceful.",
                    "'Its okay,' she says. 'I knew this would happen eventually.'",
                    "She smiles at you. Then the light takes her and shes gone."
                },
                AwakeningGain = 1,
                PhilosophicalHint = "She didnt seem sad. Thats the strange part."
            },

            new NarrativeDreamData
            {
                Id = "dream_grief_aldric",
                Title = "The Shield Rests",
                RequiresCompanionDeath = "Aldric",
                Priority = 30,
                Content = new[] {
                    "Aldric shows up in your dreams. Shield down for once.",
                    "'Shouldve ducked,' he says. Almost laughing.",
                    "Then hes serious. 'Finish this. For both of us.'",
                    "He salutes and fades out."
                },
                AwakeningGain = 1,
                PhilosophicalHint = "He saluted you. Even dead, still a soldier."
            },

            new NarrativeDreamData
            {
                Id = "dream_grief_mira",
                Title = "The Last Healing",
                RequiresCompanionDeath = "Mira",
                Priority = 30,
                Content = new[] {
                    "Mira visits your dreams. She looks tired but calm.",
                    "'I couldnt save myself,' she says. 'Funny, right?'",
                    "'But I saved you. Thats enough.'",
                    "She touches your forehead and a warmth spreads through you."
                },
                AwakeningGain = 2,
                PhilosophicalHint = "She was still trying to heal people. Even dead."
            },

            new NarrativeDreamData
            {
                Id = "dream_grief_vex",
                Title = "One Last Laugh",
                RequiresCompanionDeath = "Vex",
                Priority = 30,
                Content = new[] {
                    "Vex appears in your dreams, laughing.",
                    "'Don't look so glum! I was dying before we met, remember?'",
                    "'You gave me more adventure in one lifetime than most get in ten.'",
                    "'Besides...' he winks. 'The best jokes always have a punchline. Mine was LEGENDARY.'"
                },
                AwakeningGain = 1,
                PhilosophicalHint = "Typical Vex. Still cracking jokes."
            },

            // ====== CYCLE-SPECIFIC DREAMS (NG+) ======

            new NarrativeDreamData
            {
                Id = "dream_cycle_deja_vu",
                Title = "Haven't We Done This Before?",
                MinCycle = 2,
                Priority = 10,
                Content = new[] {
                    "The dream feels familiar. Way too familiar.",
                    "Youve been here before. Done all this before.",
                    "'How many times?' you mutter.",
                    "Nobody answers. But you get the feeling its been a lot."
                },
                PhilosophicalHint = "How many times have you done this?"
            },

            new NarrativeDreamData
            {
                Id = "dream_cycle_fragments",
                Title = "Fragments of Previous Lives",
                MinCycle = 3,
                MinAwakening = 3,
                Priority = 15,
                Content = new[] {
                    "You dream of all the other times.",
                    "Different faces every time. Same road though.",
                    "Someone always has to reach the end.",
                    "Someone always has to make the choice. This time its you. Again."
                },
                AwakeningGain = 2,
                PhilosophicalHint = "Different face every time. Same ending."
            }
        };

        /// <summary>
        /// Environmental visions triggered by dungeon exploration
        /// </summary>
        public static readonly List<DungeonVision> DungeonVisions = new()
        {
            new DungeonVision
            {
                Id = "vision_wall_writing",
                FloorMin = 10, FloorMax = 25,
                Description = "Ancient writing on the wall",
                Content = new[] {
                    "Scratched into the stone, barely visible:",
                    "\"HE FORGOT WHAT HE WAS. WE DIDN'T.\"",
                    "Below it, in different handwriting: \"I was here before. Ill be here again.\""
                }
            },
            new DungeonVision
            {
                Id = "vision_candles",
                FloorMin = 30, FloorMax = 50,
                Description = "Seven unlit candles",
                Content = new[] {
                    "Seven candles stand in a circle, cold and dark.",
                    "As you pass, they flicker to life for just a moment.",
                    "Seven flames. Seven gods. Seven pieces of something broken."
                }
            },
            new DungeonVision
            {
                Id = "vision_mirror_room",
                FloorMin = 50, FloorMax = 75,
                MinAwakening = 3,
                Description = "A room of mirrors",
                Content = new[] {
                    "The room is filled with mirrors, all angled differently.",
                    "In each one, you see a different face. All of them yours.",
                    "One mirror shows a face wearing a crown of stars.",
                    "It winks at you before the room goes dark."
                },
                AwakeningGain = 1
            },
            new DungeonVision
            {
                Id = "vision_crying_statue",
                FloorMin = 70, FloorMax = 90,
                Description = "A weeping statue",
                Content = new[] {
                    "A statue of a robed figure kneels here, hands over its face.",
                    "Stone tears have worn channels down its cheeks.",
                    "\"I NEVER MEANT FOR THEM TO SUFFER,\" is carved at its base.",
                    "The statue looks... familiar."
                }
            },
            new DungeonVision
            {
                Id = "vision_ocean_sound",
                FloorMin = 85, FloorMax = 100,
                MinAwakening = 5,
                Description = "The sound of waves",
                Content = new[] {
                    "Impossible, this deep underground, but you hear it clearly.",
                    "Waves. The rhythm of an endless ocean.",
                    "For a moment, the dungeon walls shimmer like water.",
                    "You remember - no. You ALMOST remember."
                },
                AwakeningGain = 2,
                WaveFragment = WaveFragment.TheReturn
            }
        };

        public DreamSystem()
        {
            _fallbackInstance = this;
        }

        /// <summary>
        /// Get a dream for the player when they rest
        /// </summary>
        public NarrativeDreamData? GetDreamForRest(Character player, int currentFloor)
        {
            _restsSinceLastDream++;

            // Don't dream every rest
            if (_restsSinceLastDream < 2) return null;
            if (new Random().NextDouble() > 0.5) return null;

            var awakening = OceanPhilosophySystem.Instance?.AwakeningLevel ?? 0;
            var cycle = StoryProgressionSystem.Instance?.CurrentCycle ?? 1;

            // Filter eligible dreams
            var eligible = Dreams
                .Where(d => !ExperiencedDreams.Contains(d.Id))
                .Where(d => d.MinLevel <= player.Level && d.MaxLevel >= player.Level)
                .Where(d => d.MinAwakening <= awakening && d.MaxAwakening >= awakening)
                .Where(d => d.MinCycle <= cycle)
                .Where(d => CheckDreamRequirements(d, currentFloor, player))
                .OrderByDescending(d => d.Priority)
                .ThenBy(_ => Guid.NewGuid()) // Randomize within priority
                .ToList();

            if (!eligible.Any()) return null;

            var dream = eligible.First();
            _lastDreamId = dream.Id;
            _restsSinceLastDream = 0;

            return dream;
        }

        /// <summary>
        /// Mark a dream as experienced
        /// </summary>
        public void ExperienceDream(string dreamId)
        {
            ExperiencedDreams.Add(dreamId);

            var dream = Dreams.FirstOrDefault(d => d.Id == dreamId);
            if (dream != null)
            {
                // Apply awakening gain
                if (dream.AwakeningGain > 0)
                {
                    OceanPhilosophySystem.Instance?.GainInsight(dream.AwakeningGain * 10);
                }

                // Grant wave fragment
                if (dream.WaveFragment.HasValue)
                {
                    OceanPhilosophySystem.Instance?.CollectFragment(dream.WaveFragment.Value);
                }
            }

            GD.Print($"[Dream] Player experienced dream: {dreamId}");
        }

        /// <summary>
        /// Get a dungeon vision for the current floor (only shows each vision once per playthrough)
        /// </summary>
        public DungeonVision? GetDungeonVision(int floor, Character player)
        {
            var awakening = OceanPhilosophySystem.Instance?.AwakeningLevel ?? 0;

            // Filter to eligible visions that haven't been seen yet
            var eligible = DungeonVisions
                .Where(v => v.FloorMin <= floor && v.FloorMax >= floor)
                .Where(v => v.MinAwakening <= awakening)
                .Where(v => !SeenDungeonVisions.Contains(v.Id))  // Don't repeat seen visions
                .ToList();

            if (!eligible.Any()) return null;

            // 30% chance to trigger a vision when entering a new room
            if (GD.RandRange(0, 100) > 30) return null;

            var vision = eligible[GD.RandRange(0, eligible.Count - 1)];

            // Mark as seen so it won't repeat
            SeenDungeonVisions.Add(vision.Id);

            return vision;
        }

        /// <summary>
        /// Reset seen dungeon visions (e.g., for New Game+)
        /// </summary>
        public void ResetDungeonVisions()
        {
            SeenDungeonVisions.Clear();
        }

        /// <summary>
        /// Check if dream requirements are met
        /// </summary>
        private bool CheckDreamRequirements(NarrativeDreamData dream, int currentFloor, Character player)
        {
            // Check floor requirements
            if (dream.RequiredFloor > 0 && currentFloor < dream.RequiredFloor) return false;
            if (dream.MaxFloor > 0 && currentFloor > dream.MaxFloor) return false;

            // Check god defeat requirements
            if (dream.RequiresGodDefeated.HasValue)
            {
                var godState = StoryProgressionSystem.Instance?.OldGodStates
                    .GetValueOrDefault(dream.RequiresGodDefeated.Value);
                if (godState?.Status != GodStatus.Defeated && godState?.Status != GodStatus.Saved)
                    return false;
            }

            // Check companion death requirements
            if (!string.IsNullOrEmpty(dream.RequiresCompanionDeath))
            {
                // Check if companion is dead via GriefSystem
                var griefSystem = GriefSystem.Instance;
                var griefData = griefSystem?.Serialize();
                if (griefData?.ActiveGrief?.All(g => g.CompanionName != dream.RequiresCompanionDeath) ?? true)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Serialize for save
        /// </summary>
        public DreamSaveData Serialize()
        {
            return new DreamSaveData
            {
                ExperiencedDreams = ExperiencedDreams.ToList(),
                SeenDungeonVisions = SeenDungeonVisions.ToList(),
                RestsSinceLastDream = _restsSinceLastDream
            };
        }

        /// <summary>
        /// Deserialize from save
        /// </summary>
        public void Deserialize(DreamSaveData? data)
        {
            if (data == null) return;

            ExperiencedDreams = new HashSet<string>(data.ExperiencedDreams);
            SeenDungeonVisions = new HashSet<string>(data.SeenDungeonVisions ?? new List<string>());
            _restsSinceLastDream = data.RestsSinceLastDream;
        }

        /// <summary>
        /// Reset all state for a new game
        /// </summary>
        public void Reset()
        {
            ExperiencedDreams = new HashSet<string>();
            SeenDungeonVisions = new HashSet<string>();
            _lastDreamId = "";
            _restsSinceLastDream = 0;
            GD.Print("[Dream] System reset for new game");
        }
    }

    #region Data Classes

    public class NarrativeDreamData
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public int MinLevel { get; set; } = 0;
        public int MaxLevel { get; set; } = 100;
        public int MinAwakening { get; set; } = 0;
        public int MaxAwakening { get; set; } = 7;
        public int MinCycle { get; set; } = 1;
        public int RequiredFloor { get; set; } = 0;
        public int MaxFloor { get; set; } = 0;
        public int Priority { get; set; } = 10;
        public string[] Content { get; set; } = Array.Empty<string>();
        public int AwakeningGain { get; set; } = 0;
        public WaveFragment? WaveFragment { get; set; }
        public OldGodType? RequiresGodDefeated { get; set; }
        public string? RequiresCompanionDeath { get; set; }
        public string PhilosophicalHint { get; set; } = "";
    }

    public class DungeonVision
    {
        public string Id { get; set; } = "";
        public int FloorMin { get; set; }
        public int FloorMax { get; set; }
        public int MinAwakening { get; set; } = 0;
        public string Description { get; set; } = "";
        public string[] Content { get; set; } = Array.Empty<string>();
        public int AwakeningGain { get; set; } = 0;
        public WaveFragment? WaveFragment { get; set; }
    }

    public class DreamSaveData
    {
        public List<string> ExperiencedDreams { get; set; } = new();
        public List<string> SeenDungeonVisions { get; set; } = new();
        public int RestsSinceLastDream { get; set; }
    }

    #endregion
}
