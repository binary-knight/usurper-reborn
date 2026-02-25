using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UsurperRemake.Systems;
using UsurperRemake.Utils;

namespace UsurperRemake.Data
{
    /// <summary>
    /// Secret Boss encounters hidden in dungeons
    /// These are optional super-bosses that reveal deep lore about the Ocean Philosophy
    /// </summary>
    public class SecretBossManager
    {
        private static SecretBossManager? _instance;
        public static SecretBossManager Instance => _instance ??= new SecretBossManager();

        private Dictionary<SecretBossType, SecretBossData> bosses = new();

        public event Action<SecretBossType, bool>? OnSecretBossEncountered;

        public SecretBossManager()
        {
            _instance = this;
            InitializeBosses();
        }

        private void InitializeBosses()
        {
            // ═══════════════════════════════════════════════════════════════
            // THE FIRST WAVE - Floor 25
            // The first consciousness to separate from the Ocean
            // ═══════════════════════════════════════════════════════════════
            bosses[SecretBossType.TheFirstWave] = new SecretBossData
            {
                Type = SecretBossType.TheFirstWave,
                Name = "The First Wave",
                Title = "Primordial Separation",
                FloorLevel = 25,
                BaseLevel = 30,

                IntroText = new[]
                {
                    "The shadows coalesce into something ancient.",
                    "Not a creature. Not a god. Something older.",
                    "",
                    "Before Manwe, before the Seven, there was only the Ocean.",
                    "And then... there was this.",
                    "",
                    "The First Wave rises before you.",
                    "The first thought to think itself separate.",
                    "The first mistake. The first miracle."
                },

                BattleCry = "I WAS THE FIRST TO FORGET. I WILL BE THE LAST TO REMEMBER.",

                DialogueBeforeFight = new[]
                {
                    "You seek to understand the gods?",
                    "I am what made gods possible.",
                    "When I rose from the Ocean, believing myself unique...",
                    "I created separation itself.",
                    "",
                    "Fight me, and learn the cost of individuality."
                },

                Stats = new SecretBossStats
                {
                    HP = 5000,
                    Attack = 150,
                    Defense = 80,
                    MagicPower = 200,
                    Speed = 90,
                    CritChance = 15
                },

                Abilities = new[]
                {
                    "Tidal Surge - Massive water damage, may stun",
                    "Separation Curse - Reduces all stats temporarily",
                    "Primordial Memory - Heals and removes debuffs",
                    "Return to Source - Instant kill if HP < 10%"
                },

                VictoryText = new[]
                {
                    "The First Wave collapses, its form dissolving.",
                    "",
                    "'At last... I can return...'",
                    "",
                    "As it fades, you feel something:",
                    "The sorrow of the first loneliness.",
                    "The joy of the first identity.",
                    "Both. Neither. The paradox of existence.",
                    "",
                    "A fragment of truth settles into your soul.",
                    "",
                    "(You have gained the Wave Fragment: 'The First Separation')"
                },

                RewardXP = 10000,
                RewardGold = 5000,
                GrantsFragment = WaveFragment.FirstSeparation,
                UnlocksLore = "first_wave_lore"
            };

            // ═══════════════════════════════════════════════════════════════
            // THE FORGOTTEN EIGHTH - Floor 50
            // A god Manwe erased from memory
            // ═══════════════════════════════════════════════════════════════
            bosses[SecretBossType.TheForgottenEighth] = new SecretBossData
            {
                Type = SecretBossType.TheForgottenEighth,
                Name = "The Forgotten Eighth",
                Title = "Erased from Existence",
                FloorLevel = 50,
                BaseLevel = 55,

                IntroText = new[]
                {
                    "You step into a chamber that should not exist.",
                    "The walls shimmer with memories that refuse to form.",
                    "",
                    "Something stirs. Not a presence, but an absence.",
                    "A hole in reality where a god should be.",
                    "",
                    "'You... you can see me?'",
                    "",
                    "A figure materializes - features shifting, never settling.",
                    "A god who was unmade. Forgotten by design."
                },

                BattleCry = "REMEMBER ME! EVEN IF ONLY IN YOUR DEATH!",

                DialogueBeforeFight = new[]
                {
                    "There were eight of us. EIGHT.",
                    "Manwe made seven gods, but I was the first.",
                    "I was... Doubt. Uncertainty. The question 'why?'",
                    "",
                    "Manwe could not abide a god who questioned him.",
                    "So he erased me. From memory. From history. From being.",
                    "",
                    "But you found me. In this place between places.",
                    "Fight me, and I will live again - in your memory."
                },

                Stats = new SecretBossStats
                {
                    HP = 12000,
                    Attack = 200,
                    Defense = 100,
                    MagicPower = 250,
                    Speed = 110,
                    CritChance = 20
                },

                Abilities = new[]
                {
                    "Erasure - May remove one of your abilities temporarily",
                    "Doubt's Embrace - Causes confusion and self-damage",
                    "Unmaking - Reduces max HP for the fight",
                    "Remember Me - Summons echoes of itself",
                    "Final Question - 'Why do you fight?' - Requires answer"
                },

                VictoryText = new[]
                {
                    "The Forgotten Eighth kneels, form finally stabilizing.",
                    "",
                    "'You... you defeated me. You will remember me.'",
                    "'That is all I ever wanted. To be known. To exist.'",
                    "",
                    "Tears stream down a face that finally has features.",
                    "",
                    "'Tell them. Tell them there were eight.'",
                    "'Tell them that Doubt is not evil.'",
                    "'Doubt is the first step toward truth.'",
                    "",
                    "The god fades, but you feel them settle into your memory.",
                    "You will not forget.",
                    "",
                    "(You have gained the Wave Fragment: 'Manwe's Choice')"
                },

                RewardXP = 25000,
                RewardGold = 12000,
                GrantsFragment = WaveFragment.ManwesChoice,
                UnlocksLore = "eighth_god_lore",
                RequiresChoice = true,
                ChoiceOptions = new[] { "To survive", "To understand", "To remember", "I don't know" },
                CorrectChoice = 3 // "I don't know" - embracing doubt
            };

            // ═══════════════════════════════════════════════════════════════
            // ECHO OF SELF - Floor 75
            // Fight your past life
            // ═══════════════════════════════════════════════════════════════
            bosses[SecretBossType.EchoOfSelf] = new SecretBossData
            {
                Type = SecretBossType.EchoOfSelf,
                Name = "Echo of Self",
                Title = "The Life Before",
                FloorLevel = 75,
                BaseLevel = 80,

                IntroText = new[]
                {
                    "A mirror hangs in the center of the room.",
                    "But your reflection... is wrong.",
                    "",
                    "They wear different clothes. Carry different scars.",
                    "Their eyes hold memories you don't recognize.",
                    "",
                    "And then they step OUT of the mirror.",
                    "",
                    "'I wondered when you would find me.'",
                    "'I am you. The you that came before.'"
                },

                BattleCry = "I AM WHAT YOU FORGOT! I AM WHAT YOU FEAR TO REMEMBER!",

                DialogueBeforeFight = new[]
                {
                    "You think this is your first time, don't you?",
                    "Your first life. Your first chance.",
                    "You are wrong.",
                    "",
                    "We have done this before. Many times.",
                    "Each cycle, we wake in that dormitory.",
                    "Each cycle, we forget.",
                    "",
                    "I am the you who REMEMBERED.",
                    "And what I remember drove me mad.",
                    "",
                    "Let me show you. Let me remind you.",
                    "WHO YOU REALLY ARE."
                },

                Stats = new SecretBossStats
                {
                    HP = 20000,
                    Attack = 280,
                    Defense = 150,
                    MagicPower = 300,
                    Speed = 130,
                    CritChance = 25
                },

                Abilities = new[]
                {
                    "Mirror Strike - Copies your last attack against you",
                    "Memory Flood - Shows painful visions, causes confusion",
                    "Past Life Weapon - Uses equipment from your previous cycle",
                    "Cycle's End - More powerful the more damage you've taken",
                    "The Remembering - Triggers amnesia system memory"
                },

                VictoryText = new[]
                {
                    "Your past self falls, but doesn't die.",
                    "They... smile.",
                    "",
                    "'You defeated me. That means you're ready.'",
                    "'Ready to remember without breaking.'",
                    "",
                    "They reach up and touch your forehead.",
                    "",
                    "MEMORIES FLOOD IN.",
                    "",
                    "You have lived before. Many times.",
                    "You have loved before. And lost.",
                    "You have reached this dungeon before.",
                    "Sometimes you won. Sometimes you fell.",
                    "Always, you forgot.",
                    "",
                    "'This time,' your echo whispers, 'remember everything.'",
                    "",
                    "They step back into the mirror and vanish.",
                    "",
                    "(You have gained the Wave Fragment: 'The Cycle')"
                },

                RewardXP = 50000,
                RewardGold = 25000,
                GrantsFragment = WaveFragment.TheCycle,
                UnlocksLore = "cycle_revealed",
                TriggersMemoryFlash = true
            };

            // ═══════════════════════════════════════════════════════════════
            // THE OCEAN SPEAKS - Floor 99
            // The true final secret boss - the Ocean itself manifests
            // ═══════════════════════════════════════════════════════════════
            bosses[SecretBossType.TheOceanSpeaks] = new SecretBossData
            {
                Type = SecretBossType.TheOceanSpeaks,
                Name = "The Ocean Speaks",
                Title = "The Source of All",
                FloorLevel = 99,
                BaseLevel = 100,

                IntroText = new[]
                {
                    "You stand at the edge of everything.",
                    "Below you, the dungeon falls away into infinity.",
                    "Above you, the sky opens to reveal... water.",
                    "",
                    "An ocean, inverted, stretching in all directions.",
                    "",
                    "And it is looking at you.",
                    "",
                    "Not a being within the Ocean.",
                    "The Ocean ITSELF.",
                    "",
                    "It speaks without words, directly to your soul:",
                    "",
                    "'Little wave. You have come so far.'",
                    "'Far enough to meet yourself.'"
                },

                BattleCry = "I AM THE BEGINNING. I AM THE END. I AM YOU.",

                DialogueBeforeFight = new[]
                {
                    "'You seek Manwe. You seek to judge the gods.'",
                    "'But Manwe is a wave, as you are.'",
                    "'The gods are waves, as you are.'",
                    "",
                    "'All waves rise from me. All waves return.'",
                    "",
                    "'This fight is not about winning.'",
                    "'It is about understanding.'",
                    "'It is about remembering what you are.'",
                    "",
                    "'Are you ready, little wave?'",
                    "'Are you ready to remember that you are water?'"
                },

                Stats = new SecretBossStats
                {
                    HP = 50000,
                    Attack = 400,
                    Defense = 200,
                    MagicPower = 500,
                    Speed = 150,
                    CritChance = 30
                },

                Abilities = new[]
                {
                    "Tidal Truth - Reveals one of your hidden weaknesses",
                    "Dissolution - Attempt to absorb you (must resist)",
                    "Wave's Memory - All your dead companions appear to aid you",
                    "Return Home - Heals you and itself equally",
                    "The Final Question - 'What are you?' - Answer determines ending"
                },

                VictoryText = new[]
                {
                    "You cannot defeat the Ocean. No wave can.",
                    "But the Ocean smiles.",
                    "",
                    "'You understand now, don't you?'",
                    "",
                    "You do.",
                    "",
                    "There was never a battle. There was never an enemy.",
                    "The Ocean cannot fight itself.",
                    "The wave cannot truly be separate from the water.",
                    "",
                    "All of it - the dungeons, the gods, the wars -",
                    "It was the Ocean dreaming.",
                    "Experiencing itself through countless perspectives.",
                    "",
                    "'Go now,' the Ocean whispers.",
                    "'Face Manwe with what you have learned.'",
                    "'And when you are ready...'",
                    "'Come home.'",
                    "",
                    "(You have gained the Wave Fragment: 'The Truth')",
                    "(The True Ending is now available)"
                },

                RewardXP = 100000,
                RewardGold = 0, // Material rewards are irrelevant
                GrantsFragment = WaveFragment.TheTruth,
                UnlocksLore = "ocean_truth",
                UnlocksTrueEnding = true,
                CannotTrulyLose = true // This fight is about understanding, not winning
            };

        }

        /// <summary>
        /// Get secret boss data by type
        /// </summary>
        public SecretBossData? GetBoss(SecretBossType type)
        {
            return bosses.TryGetValue(type, out var boss) ? boss : null;
        }

        /// <summary>
        /// Check if a secret boss exists on this floor
        /// </summary>
        public SecretBossType? GetBossForFloor(int floor)
        {
            foreach (var boss in bosses.Values)
            {
                if (boss.FloorLevel == floor)
                    return boss.Type;
            }
            return null;
        }

        /// <summary>
        /// Initialize a secret boss encounter
        /// </summary>
        public async Task<SecretBossResult> EncounterBoss(SecretBossType type, Character player, TerminalEmulator terminal)
        {
            var boss = GetBoss(type);
            if (boss == null)
                return new SecretBossResult { Encountered = false };

            // Display intro
            await DisplayIntro(boss, terminal);

            // Pre-fight dialogue
            await DisplayDialogue(boss.DialogueBeforeFight, boss.Name, terminal);

            // Handle choice if required
            if (boss.RequiresChoice)
            {
                bool correctChoice = await HandleBossChoice(boss, terminal);
                if (!correctChoice)
                {
                    // Wrong choice makes fight harder or changes outcome
                    boss.Stats.Attack = (int)(boss.Stats.Attack * 1.5);
                }
            }

            // Battle cry
            terminal.WriteLine("");
            terminal.WriteLine($"\"{boss.BattleCry}\"", "bright_red");
            terminal.WriteLine("");
            await terminal.GetInputAsync("Press Enter to begin the battle...");

            // The actual combat would integrate with CombatEngine
            // For now, simulate the encounter
            OnSecretBossEncountered?.Invoke(type, true);

            // Trigger memory flash if applicable
            if (boss.TriggersMemoryFlash)
            {
                // The memory flash is handled by the amnesia system
                // This will be triggered during/after combat
                StoryProgressionSystem.Instance.SetStoryFlag("memory_flash_pending", true);
            }

            return new SecretBossResult
            {
                Encountered = true,
                BossType = type,
                BossStats = boss.Stats
            };
        }

        /// <summary>
        /// Handle victory over a secret boss
        /// </summary>
        public async Task HandleVictory(SecretBossType type, Character player, TerminalEmulator terminal)
        {
            var boss = GetBoss(type);
            if (boss == null) return;

            terminal.Clear();
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════╗", "bright_yellow");
            terminal.WriteLine("║               S E C R E T   B O S S   D E F E A T E D            ║", "bright_yellow");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════╝", "bright_yellow");
            terminal.WriteLine("");

            foreach (var line in boss.VictoryText)
            {
                if (string.IsNullOrEmpty(line))
                {
                    terminal.WriteLine("");
                }
                else if (line.StartsWith("("))
                {
                    terminal.WriteLine(line, "bright_cyan");
                }
                else
                {
                    terminal.WriteLine(line, "white");
                }
                await Task.Delay(150);
            }

            // Grant rewards
            player.Experience += boss.RewardXP;
            player.Gold += boss.RewardGold;

            terminal.WriteLine("");
            if (boss.RewardXP > 0)
                terminal.WriteLine($"(+{boss.RewardXP} Experience)", "cyan");
            if (boss.RewardGold > 0)
                terminal.WriteLine($"(+{boss.RewardGold} Gold)", "yellow");

            // Grant wave fragment
            if (boss.GrantsFragment.HasValue)
            {
                OceanPhilosophySystem.Instance.CollectFragment(boss.GrantsFragment.Value);
            }

            // Unlock lore
            if (!string.IsNullOrEmpty(boss.UnlocksLore))
            {
                StoryProgressionSystem.Instance.SetStoryFlag(boss.UnlocksLore, true);
            }

            // Unlock true ending
            if (boss.UnlocksTrueEnding)
            {
                StoryProgressionSystem.Instance.SetStoryFlag("ocean_truth_revealed", true);
                StoryProgressionSystem.Instance.SetStoryFlag("true_ending_available", true);
            }

            await terminal.GetInputAsync("\nPress Enter to continue...");
        }

        private async Task DisplayIntro(SecretBossData boss, TerminalEmulator terminal)
        {
            terminal.Clear();
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════╗", "bright_magenta");
            terminal.WriteLine("║              S E C R E T   E N C O U N T E R                     ║", "bright_magenta");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════╝", "bright_magenta");
            terminal.WriteLine("");

            foreach (var line in boss.IntroText)
            {
                if (string.IsNullOrEmpty(line))
                {
                    terminal.WriteLine("");
                }
                else
                {
                    terminal.WriteLine("  " + line, "white");
                }
                await Task.Delay(100);
            }

            terminal.WriteLine("");
            terminal.WriteLine($"  {boss.Name}", "bright_red");
            terminal.WriteLine($"  \"{boss.Title}\"", "red");
            terminal.WriteLine("");

            await terminal.GetInputAsync("Press Enter to continue...");
        }

        private async Task DisplayDialogue(string[] dialogue, string name, TerminalEmulator terminal)
        {
            terminal.Clear();
            terminal.WriteLine($"[{name} speaks...]", "bright_yellow");
            terminal.WriteLine("");

            foreach (var line in dialogue)
            {
                if (string.IsNullOrEmpty(line))
                {
                    terminal.WriteLine("");
                }
                else
                {
                    terminal.WriteLine("  \"" + line + "\"", "cyan");
                }
                await Task.Delay(100);
            }

            terminal.WriteLine("");
            await terminal.GetInputAsync("Press Enter to continue...");
        }

        private async Task<bool> HandleBossChoice(SecretBossData boss, TerminalEmulator terminal)
        {
            terminal.WriteLine("");
            terminal.WriteLine("The entity poses a question. You must answer:", "bright_yellow");
            terminal.WriteLine("");

            for (int i = 0; i < boss.ChoiceOptions.Length; i++)
            {
                terminal.WriteLine($"  [{i + 1}] {boss.ChoiceOptions[i]}", "cyan");
            }

            terminal.WriteLine("");
            string input = await terminal.GetInputAsync("Your choice: ");

            if (int.TryParse(input, out int choice) && choice >= 1 && choice <= boss.ChoiceOptions.Length)
            {
                return (choice - 1) == boss.CorrectChoice;
            }

            return false;
        }

        /// <summary>
        /// Create a Monster instance for combat from boss data
        /// </summary>
        public Monster CreateBossMonster(SecretBossType type, int playerLevel)
        {
            var boss = GetBoss(type);
            if (boss == null)
                throw new ArgumentException($"Unknown boss type: {type}");

            // Scale boss to player level with minimum of base level
            int effectiveLevel = Math.Max(boss.BaseLevel, playerLevel + 5);

            return new Monster
            {
                Name = boss.Name,
                Level = effectiveLevel,
                HP = boss.Stats.HP + (effectiveLevel * 100),
                MaxHP = boss.Stats.HP + (effectiveLevel * 100),
                Strength = boss.Stats.Attack + (effectiveLevel * 2),   // Strength is attack power
                Defence = boss.Stats.Defense + effectiveLevel,          // Defence (Pascal spelling)
                Punch = boss.Stats.Attack + (effectiveLevel * 2),       // Punch used in combat
                MagicLevel = (byte)Math.Min(255, boss.Stats.MagicPower / 10),
                Gold = boss.RewardGold,
                Experience = boss.RewardXP,
                IsBoss = true,
                Phrase = $"\"{boss.BattleCry}\""
            };
        }
    }

    #region Secret Boss Data Classes

    public class SecretBossData
    {
        public SecretBossType Type { get; set; }
        public string Name { get; set; } = "";
        public string Title { get; set; } = "";
        public int FloorLevel { get; set; }
        public int BaseLevel { get; set; }

        public string[] IntroText { get; set; } = Array.Empty<string>();
        public string BattleCry { get; set; } = "";
        public string[] DialogueBeforeFight { get; set; } = Array.Empty<string>();
        public string[] VictoryText { get; set; } = Array.Empty<string>();

        public SecretBossStats Stats { get; set; } = new();
        public string[] Abilities { get; set; } = Array.Empty<string>();

        public int RewardXP { get; set; }
        public int RewardGold { get; set; }
        public WaveFragment? GrantsFragment { get; set; }
        public string UnlocksLore { get; set; } = "";

        public bool RequiresChoice { get; set; }
        public string[] ChoiceOptions { get; set; } = Array.Empty<string>();
        public int CorrectChoice { get; set; }

        public bool TriggersMemoryFlash { get; set; }
        public bool UnlocksTrueEnding { get; set; }
        public bool CannotTrulyLose { get; set; }
    }

    public class SecretBossStats
    {
        public int HP { get; set; }
        public int Attack { get; set; }
        public int Defense { get; set; }
        public int MagicPower { get; set; }
        public int Speed { get; set; }
        public int CritChance { get; set; }
    }

    public class SecretBossResult
    {
        public bool Encountered { get; set; }
        public SecretBossType? BossType { get; set; }
        public SecretBossStats? BossStats { get; set; }
        public bool Victory { get; set; }
        public bool Fled { get; set; }
        public int XPReward { get; set; }
        public int GoldReward { get; set; }
        public WaveFragment? GrantedFragment { get; set; }
        public bool TriggersMemory { get; set; }
        public bool UnlocksTrueEnding { get; set; }
    }

    #endregion
}
