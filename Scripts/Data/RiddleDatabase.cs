using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UsurperRemake.Systems;
using UsurperRemake.Utils;

namespace UsurperRemake.Data
{
    /// <summary>
    /// Database of riddles for dungeon RiddleGate rooms
    /// Includes classic riddles, Ocean philosophy riddles, and theme-specific riddles
    /// </summary>
    public class RiddleDatabase
    {
        private static RiddleDatabase? _instance;
        public static RiddleDatabase Instance => _instance ??= new RiddleDatabase();

        private List<Riddle> riddles = new();
        private Random random = new();
        private HashSet<int> usedRiddlesThisSession = new();

        public RiddleDatabase()
        {
            _instance = this;
            InitializeRiddles();
        }

        /// <summary>
        /// Get a random riddle appropriate for the difficulty and theme
        /// </summary>
        public Riddle GetRandomRiddle(int difficulty, DungeonTheme? theme = null)
        {
            var candidates = riddles
                .Where(r => r.Difficulty <= difficulty + 1 && r.Difficulty >= difficulty - 1)
                .Where(r => !usedRiddlesThisSession.Contains(r.Id))
                .Where(r => theme == null || r.Theme == null || r.Theme == theme)
                .ToList();

            if (candidates.Count == 0)
            {
                // Reset used riddles if we've exhausted them
                usedRiddlesThisSession.Clear();
                candidates = riddles.Where(r => r.Difficulty <= difficulty + 1).ToList();
            }

            var selected = candidates[random.Next(candidates.Count)];
            usedRiddlesThisSession.Add(selected.Id);
            return selected;
        }

        /// <summary>
        /// Get an Ocean Philosophy riddle (for late-game)
        /// </summary>
        public Riddle GetOceanPhilosophyRiddle()
        {
            var oceanRiddles = riddles.Where(r => r.IsOceanPhilosophy).ToList();
            return oceanRiddles[random.Next(oceanRiddles.Count)];
        }

        /// <summary>
        /// Present a riddle to the player
        /// </summary>
        public async Task<RiddleResult> PresentRiddle(Riddle riddle, Character player, TerminalEmulator terminal)
        {
            terminal.Clear();

            // Display the riddler
            DisplayRiddler(riddle, terminal);

            terminal.WriteLine("");
            terminal.WriteLine("═══════════════════════════════════════════════════════════════", "dark_cyan");
            terminal.WriteLine("");

            // Display the riddle
            foreach (var line in riddle.Text)
            {
                terminal.WriteLine($"  \"{line}\"", "bright_cyan");
                await Task.Delay(100);
            }

            terminal.WriteLine("");
            terminal.WriteLine("═══════════════════════════════════════════════════════════════", "dark_cyan");
            terminal.WriteLine("");

            int attempts = riddle.MaxAttempts;
            bool solved = false;

            while (attempts > 0 && !solved)
            {
                if (attempts < riddle.MaxAttempts)
                {
                    terminal.WriteLine($"({attempts} attempts remaining)", "yellow");
                }

                terminal.WriteLine("");
                string input = await terminal.GetInputAsync("Your answer: ");
                input = input.Trim().ToLower();

                if (input == "quit" || input == "flee" || input == "leave")
                {
                    terminal.WriteLine("");
                    terminal.WriteLine("You turn away from the guardian.", "gray");
                    await ApplyFleeConsequence(riddle, player, terminal);
                    return new RiddleResult { Solved = false, Fled = true };
                }

                if (CheckAnswer(riddle, input))
                {
                    solved = true;
                    await DisplaySuccess(riddle, player, terminal);
                }
                else
                {
                    attempts--;
                    if (attempts > 0)
                    {
                        string wrongResponse = GetWrongAnswerResponse(riddle);
                        terminal.WriteLine("");
                        terminal.WriteLine(wrongResponse, "yellow");

                        // Give hint after first wrong answer
                        if (attempts == riddle.MaxAttempts - 1 && !string.IsNullOrEmpty(riddle.Hint))
                        {
                            terminal.WriteLine("");
                            terminal.WriteLine($"A whisper: \"{riddle.Hint}\"", "dark_magenta");
                        }
                    }
                }
            }

            if (!solved)
            {
                await DisplayFailure(riddle, player, terminal);
            }

            return new RiddleResult
            {
                Solved = solved,
                Attempts = riddle.MaxAttempts - attempts,
                XPEarned = solved ? riddle.RewardXP : 0,
                DamageTaken = solved ? 0 : riddle.FailureDamage
            };
        }

        private void DisplayRiddler(Riddle riddle, TerminalEmulator terminal)
        {
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════╗", "bright_magenta");
            terminal.WriteLine("║                   THE GUARDIAN SPEAKS                            ║", "bright_magenta");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════╝", "bright_magenta");
            terminal.WriteLine("");

            string guardianDesc = riddle.GuardianType switch
            {
                GuardianType.Specter => "A ghostly figure materializes before you, its hollow eyes burning with ancient knowledge.",
                GuardianType.Sphinx => "A massive sphinx reclines across your path, its human face twisted in an enigmatic smile.",
                GuardianType.Demon => "A demon of shadow coalesces from the darkness, its voice echoing from everywhere at once.",
                GuardianType.Spirit => "A spirit of pure light blocks your way, its form shifting between countless faces.",
                GuardianType.Golem => "An ancient stone golem animates, runes glowing across its body as it speaks in grinding tones.",
                GuardianType.VoidEntity => "The darkness itself speaks, a presence without form, without beginning, without end.",
                _ => "An entity of unknown origin bars your passage, demanding tribute of wit."
            };

            terminal.WriteLine(guardianDesc, "white");
        }

        private bool CheckAnswer(Riddle riddle, string input)
        {
            // Check primary answer
            if (riddle.Answer.Equals(input, StringComparison.OrdinalIgnoreCase))
                return true;

            // Check alternate answers
            return riddle.AlternateAnswers.Any(alt =>
                alt.Equals(input, StringComparison.OrdinalIgnoreCase));
        }

        private string GetWrongAnswerResponse(Riddle riddle)
        {
            var responses = new[]
            {
                "The guardian's eyes narrow. 'That is not the answer I seek.'",
                "'Incorrect,' the voice echoes. 'Think deeper.'",
                "A cold laugh. 'Try again, mortal.'",
                "The entity shakes its head slowly. 'You have not understood.'",
                "'No. The truth eludes you still.'"
            };
            return responses[random.Next(responses.Length)];
        }

        private async Task DisplaySuccess(Riddle riddle, Character player, TerminalEmulator terminal)
        {
            terminal.WriteLine("");
            terminal.WriteLine("The guardian bows its head in acknowledgment.", "bright_green");
            terminal.WriteLine("");
            terminal.WriteLine("'You speak true. The way is open to you.'", "green");
            terminal.WriteLine("");

            player.Experience += riddle.RewardXP;
            terminal.WriteLine($"(+{riddle.RewardXP} Experience)", "cyan");

            // Ocean philosophy revelation for special riddles
            if (riddle.IsOceanPhilosophy)
            {
                terminal.WriteLine("");
                terminal.WriteLine("As the guardian fades, you feel a strange resonance...", "bright_magenta");
                terminal.WriteLine("A piece of forgotten truth settles into your soul.", "magenta");
                OceanPhilosophySystem.Instance.CollectFragment(WaveFragment.TheCycle);
            }

            await terminal.GetInputAsync("Press Enter to continue...");
        }

        private async Task DisplayFailure(Riddle riddle, Character player, TerminalEmulator terminal)
        {
            terminal.WriteLine("");
            terminal.WriteLine("'Your wit has failed you, mortal.'", "red");
            terminal.WriteLine("");

            if (riddle.FailureDamage > 0)
            {
                int damage = Math.Min(riddle.FailureDamage, (int)player.HP - 1);
                player.HP -= damage;
                terminal.WriteLine($"The guardian's wrath strikes you for {damage} damage!", "bright_red");
            }

            terminal.WriteLine("");
            terminal.WriteLine($"The answer was: {riddle.Answer}", "gray");

            await terminal.GetInputAsync("Press Enter to continue...");
        }

        private async Task ApplyFleeConsequence(Riddle riddle, Character player, TerminalEmulator terminal)
        {
            if (riddle.FleeAllowed)
            {
                terminal.WriteLine("The guardian lets you pass... for now.", "yellow");
            }
            else
            {
                int damage = riddle.FailureDamage / 2;
                player.HP = Math.Max(1, player.HP - damage);
                terminal.WriteLine($"Fleeing angers the guardian! You take {damage} damage!", "red");
            }
            await Task.Delay(1000);
        }

        #region Riddle Initialization

        private void InitializeRiddles()
        {
            int id = 0;

            // ═══════════════════════════════════════════════════════════════
            // CLASSIC RIDDLES (Difficulty 1-2)
            // ═══════════════════════════════════════════════════════════════

            riddles.Add(new Riddle
            {
                Id = ++id,
                Difficulty = 1,
                Text = new[] { "I have cities, but no houses live there.",
                              "I have mountains, but no trees grow there.",
                              "I have water, but no fish swim there.",
                              "I have roads, but no cars drive there.",
                              "What am I?" },
                Answer = "map",
                AlternateAnswers = new[] { "a map" },
                Hint = "You might use me to find your way...",
                RewardXP = 50,
                FailureDamage = 10,
                GuardianType = GuardianType.Sphinx
            });

            riddles.Add(new Riddle
            {
                Id = ++id,
                Difficulty = 1,
                Text = new[] { "The more you take, the more you leave behind.",
                              "What am I?" },
                Answer = "footsteps",
                AlternateAnswers = new[] { "steps", "footprints" },
                Hint = "Think about what you do as you walk...",
                RewardXP = 50,
                FailureDamage = 10,
                GuardianType = GuardianType.Spirit
            });

            riddles.Add(new Riddle
            {
                Id = ++id,
                Difficulty = 1,
                Text = new[] { "I speak without a mouth and hear without ears.",
                              "I have no body, but I come alive with the wind.",
                              "What am I?" },
                Answer = "echo",
                AlternateAnswers = new[] { "an echo" },
                Hint = "Your voice might come back to you...",
                RewardXP = 50,
                FailureDamage = 10,
                GuardianType = GuardianType.Specter
            });

            riddles.Add(new Riddle
            {
                Id = ++id,
                Difficulty = 1,
                Text = new[] { "What has keys but no locks,",
                              "Space but no room,",
                              "And you can enter but can't go inside?" },
                Answer = "keyboard",
                AlternateAnswers = new[] { "a keyboard" },
                Hint = "Think of what a scribe might use...",
                RewardXP = 50,
                FailureDamage = 10,
                GuardianType = GuardianType.Golem
            });

            riddles.Add(new Riddle
            {
                Id = ++id,
                Difficulty = 1,
                Text = new[] { "I have hands but cannot clap.",
                              "What am I?" },
                Answer = "clock",
                AlternateAnswers = new[] { "a clock", "watch", "a watch" },
                Hint = "I help you know when it's time to act...",
                RewardXP = 50,
                FailureDamage = 10,
                GuardianType = GuardianType.Spirit
            });

            // ═══════════════════════════════════════════════════════════════
            // MODERATE RIDDLES (Difficulty 2-3)
            // ═══════════════════════════════════════════════════════════════

            riddles.Add(new Riddle
            {
                Id = ++id,
                Difficulty = 2,
                Text = new[] { "I am not alive, but I grow;",
                              "I don't have lungs, but I need air;",
                              "I don't have a mouth, but water kills me.",
                              "What am I?" },
                Answer = "fire",
                AlternateAnswers = new[] { "flame", "flames" },
                Hint = "I dance but have no feet...",
                RewardXP = 75,
                FailureDamage = 15,
                GuardianType = GuardianType.Demon,
                Theme = DungeonTheme.VolcanicPit
            });

            riddles.Add(new Riddle
            {
                Id = ++id,
                Difficulty = 2,
                Text = new[] { "The poor have me, the rich need me,",
                              "If you eat me you will die.",
                              "What am I?" },
                Answer = "nothing",
                AlternateAnswers = new[] { "none", "emptiness" },
                Hint = "What is the absence of all things?",
                RewardXP = 75,
                FailureDamage = 15,
                GuardianType = GuardianType.VoidEntity
            });

            riddles.Add(new Riddle
            {
                Id = ++id,
                Difficulty = 2,
                Text = new[] { "I fly without wings.",
                              "I cry without eyes.",
                              "Wherever I go, darkness follows me.",
                              "What am I?" },
                Answer = "cloud",
                AlternateAnswers = new[] { "clouds", "a cloud" },
                Hint = "Look to the sky when storms gather...",
                RewardXP = 75,
                FailureDamage = 15,
                GuardianType = GuardianType.Spirit
            });

            riddles.Add(new Riddle
            {
                Id = ++id,
                Difficulty = 2,
                Text = new[] { "What can travel around the world",
                              "while staying in a corner?" },
                Answer = "stamp",
                AlternateAnswers = new[] { "a stamp", "postage stamp" },
                Hint = "Think of letters and journeys...",
                RewardXP = 75,
                FailureDamage = 15,
                GuardianType = GuardianType.Sphinx
            });

            riddles.Add(new Riddle
            {
                Id = ++id,
                Difficulty = 2,
                Text = new[] { "What has a head and a tail",
                              "but no body?" },
                Answer = "coin",
                AlternateAnswers = new[] { "a coin", "coins" },
                Hint = "Flip me to make a decision...",
                RewardXP = 75,
                FailureDamage = 15,
                GuardianType = GuardianType.Golem
            });

            riddles.Add(new Riddle
            {
                Id = ++id,
                Difficulty = 2,
                Text = new[] { "What can fill a room",
                              "but takes up no space?" },
                Answer = "light",
                AlternateAnswers = new[] { "darkness", "sound", "air" },
                Hint = "Without me, you cannot see...",
                RewardXP = 75,
                FailureDamage = 15,
                GuardianType = GuardianType.Spirit
            });

            // ═══════════════════════════════════════════════════════════════
            // CHALLENGING RIDDLES (Difficulty 3-4)
            // ═══════════════════════════════════════════════════════════════

            riddles.Add(new Riddle
            {
                Id = ++id,
                Difficulty = 3,
                Text = new[] { "I am the beginning of the end,",
                              "And the end of time and space.",
                              "I am essential to creation,",
                              "And I surround every place.",
                              "What am I?" },
                Answer = "e",
                AlternateAnswers = new[] { "the letter e", "letter e" },
                Hint = "Think not of meaning, but of letters...",
                RewardXP = 100,
                FailureDamage = 25,
                GuardianType = GuardianType.Sphinx
            });

            riddles.Add(new Riddle
            {
                Id = ++id,
                Difficulty = 3,
                Text = new[] { "I can be cracked, made, told, and played.",
                              "What am I?" },
                Answer = "joke",
                AlternateAnswers = new[] { "a joke", "jokes" },
                Hint = "Laughter is my purpose...",
                RewardXP = 100,
                FailureDamage = 25,
                GuardianType = GuardianType.Spirit
            });

            riddles.Add(new Riddle
            {
                Id = ++id,
                Difficulty = 3,
                Text = new[] { "What disappears as soon as you say its name?" },
                Answer = "silence",
                AlternateAnswers = new[] { "quiet", "the silence" },
                Hint = "Listen to the absence of sound...",
                RewardXP = 100,
                FailureDamage = 25,
                GuardianType = GuardianType.VoidEntity
            });

            riddles.Add(new Riddle
            {
                Id = ++id,
                Difficulty = 3,
                Text = new[] { "Forward I am heavy, but backward I am not.",
                              "What am I?" },
                Answer = "ton",
                AlternateAnswers = new[] { "a ton" },
                Hint = "Spell me backward and see what you get...",
                RewardXP = 100,
                FailureDamage = 25,
                GuardianType = GuardianType.Golem
            });

            riddles.Add(new Riddle
            {
                Id = ++id,
                Difficulty = 3,
                Text = new[] { "What is seen in the middle of March and April",
                              "that can't be seen at the beginning or end of either month?" },
                Answer = "r",
                AlternateAnswers = new[] { "the letter r", "letter r" },
                Hint = "Again, think of letters, not time...",
                RewardXP = 100,
                FailureDamage = 25,
                GuardianType = GuardianType.Sphinx
            });

            // ═══════════════════════════════════════════════════════════════
            // DUNGEON THEME-SPECIFIC RIDDLES (Difficulty 2-4)
            // ═══════════════════════════════════════════════════════════════

            riddles.Add(new Riddle
            {
                Id = ++id,
                Difficulty = 2,
                Theme = DungeonTheme.Catacombs,
                Text = new[] { "I am always hungry and will die if not fed,",
                              "But whatever I touch will soon turn red.",
                              "What am I?" },
                Answer = "fire",
                AlternateAnswers = new[] { "flame", "flames" },
                Hint = "I consume all and leave only ash...",
                RewardXP = 80,
                FailureDamage = 20,
                GuardianType = GuardianType.Specter
            });

            riddles.Add(new Riddle
            {
                Id = ++id,
                Difficulty = 3,
                Theme = DungeonTheme.Catacombs,
                Text = new[] { "What walks on four legs in the morning,",
                              "Two legs at noon,",
                              "And three legs in the evening?" },
                Answer = "human",
                AlternateAnswers = new[] { "man", "person", "a human", "a man", "a person" },
                Hint = "The legs change as life progresses...",
                RewardXP = 100,
                FailureDamage = 30,
                GuardianType = GuardianType.Sphinx
            });

            riddles.Add(new Riddle
            {
                Id = ++id,
                Difficulty = 3,
                Theme = DungeonTheme.DemonLair,
                Text = new[] { "I am the black child of a white father,",
                              "A wingless bird, flying even to the clouds of heaven.",
                              "I give birth to tears of mourning in pupils that meet me,",
                              "Even though there is no cause for grief,",
                              "And at once on my birth I am dissolved into air.",
                              "What am I?" },
                Answer = "smoke",
                AlternateAnswers = new[] { "the smoke" },
                Hint = "Born from fire, I rise and vanish...",
                RewardXP = 100,
                FailureDamage = 30,
                GuardianType = GuardianType.Demon
            });

            riddles.Add(new Riddle
            {
                Id = ++id,
                Difficulty = 2,
                Theme = DungeonTheme.FrozenDepths,
                Text = new[] { "I have no life, but I can die.",
                              "I have no eyes, but I once could see.",
                              "What am I?" },
                Answer = "ice",
                AlternateAnswers = new[] { "frozen water", "a snowman" },
                Hint = "I am water transformed by cold...",
                RewardXP = 80,
                FailureDamage = 20,
                GuardianType = GuardianType.Spirit,
            });

            riddles.Add(new Riddle
            {
                Id = ++id,
                Difficulty = 3,
                Theme = DungeonTheme.Caverns,
                Text = new[] { "What builds up castles, tears down mountains,",
                              "Makes some blind, but helps others see?" },
                Answer = "sand",
                AlternateAnswers = new[] { "the sand", "time" },
                Hint = "Think of hourglasses and beaches...",
                RewardXP = 100,
                FailureDamage = 25,
                GuardianType = GuardianType.Golem
            });

            riddles.Add(new Riddle
            {
                Id = ++id,
                Difficulty = 4,
                Theme = DungeonTheme.AncientRuins,
                Text = new[] { "I am not alive, yet I grow.",
                              "I don't have eyes, but once I did see.",
                              "Once I had thoughts, but now I'm white and empty." },
                Answer = "skull",
                AlternateAnswers = new[] { "a skull", "bone", "skeleton" },
                Hint = "What remains after the flesh is gone?",
                RewardXP = 125,
                FailureDamage = 35,
                GuardianType = GuardianType.Specter
            });

            // ═══════════════════════════════════════════════════════════════
            // OCEAN PHILOSOPHY RIDDLES (Difficulty 4-5, Special)
            // ═══════════════════════════════════════════════════════════════

            riddles.Add(new Riddle
            {
                Id = ++id,
                Difficulty = 4,
                IsOceanPhilosophy = true,
                Text = new[] { "I am the wave that knows itself as ocean.",
                              "I am the drop that contains the sea.",
                              "I am the dreamer and the dream.",
                              "What am I?" },
                Answer = "self",
                AlternateAnswers = new[] { "you", "me", "i", "soul", "consciousness", "the self", "myself", "yourself" },
                Hint = "Look inward, not outward...",
                RewardXP = 200,
                FailureDamage = 40,
                GuardianType = GuardianType.VoidEntity
            });

            riddles.Add(new Riddle
            {
                Id = ++id,
                Difficulty = 4,
                IsOceanPhilosophy = true,
                Text = new[] { "When does the wave become the ocean?" },
                Answer = "always",
                AlternateAnswers = new[] { "never", "now", "death", "when it stops", "it always was", "it never stopped being" },
                Hint = "Perhaps the question contains a false premise...",
                RewardXP = 200,
                FailureDamage = 40,
                GuardianType = GuardianType.VoidEntity
            });

            riddles.Add(new Riddle
            {
                Id = ++id,
                Difficulty = 5,
                IsOceanPhilosophy = true,
                Text = new[] { "Manwe created six, and from six came countless more.",
                              "Yet all were always one.",
                              "What is the truth that the wave forgot?" },
                Answer = "it is the ocean",
                AlternateAnswers = new[] { "it was always the ocean", "they are one", "separation is illusion", "there is no separation", "all is one", "unity", "oneness" },
                Hint = "What the wave forgot was never truly lost...",
                RewardXP = 300,
                FailureDamage = 50,
                GuardianType = GuardianType.VoidEntity
            });

            riddles.Add(new Riddle
            {
                Id = ++id,
                Difficulty = 5,
                IsOceanPhilosophy = true,
                Text = new[] { "I am the pain of separation.",
                              "I am the joy of return.",
                              "I am the forgetting that makes the dream real.",
                              "I am the remembering that ends it.",
                              "What am I?" },
                Answer = "death",
                AlternateAnswers = new[] { "life", "love", "awakening", "sleep", "the cycle" },
                Hint = "I am both ending and beginning...",
                RewardXP = 300,
                FailureDamage = 50,
                GuardianType = GuardianType.VoidEntity
            });

            riddles.Add(new Riddle
            {
                Id = ++id,
                Difficulty = 5,
                IsOceanPhilosophy = true,
                Text = new[] { "You seek to destroy the one who imprisoned the gods.",
                              "But consider: who truly holds the chains?",
                              "Who made the wave forget it was water?",
                              "Name the true jailer." },
                Answer = "yourself",
                AlternateAnswers = new[] { "me", "i", "you", "self", "ego", "the wave", "desire", "grasping", "attachment" },
                Hint = "The prisoner and the jailer share the same face...",
                RewardXP = 400,
                FailureDamage = 60,
                GuardianType = GuardianType.VoidEntity,
                FleeAllowed = false
            });

            // ═══════════════════════════════════════════════════════════════
            // ADDITIONAL CLASSIC RIDDLES
            // ═══════════════════════════════════════════════════════════════

            riddles.Add(new Riddle
            {
                Id = ++id,
                Difficulty = 2,
                Text = new[] { "What has roots that nobody sees,",
                              "Is taller than trees,",
                              "Up, up it goes,",
                              "And yet never grows?" },
                Answer = "mountain",
                AlternateAnswers = new[] { "a mountain", "mountains" },
                Hint = "I am ancient and still...",
                RewardXP = 75,
                FailureDamage = 15,
                GuardianType = GuardianType.Golem
            });

            riddles.Add(new Riddle
            {
                Id = ++id,
                Difficulty = 2,
                Text = new[] { "Voiceless it cries,",
                              "Wingless flutters,",
                              "Toothless bites,",
                              "Mouthless mutters." },
                Answer = "wind",
                AlternateAnswers = new[] { "the wind" },
                Hint = "Feel me, but never see me...",
                RewardXP = 75,
                FailureDamage = 15,
                GuardianType = GuardianType.Spirit
            });

            riddles.Add(new Riddle
            {
                Id = ++id,
                Difficulty = 3,
                Text = new[] { "Alive without breath,",
                              "As cold as death,",
                              "Never thirsty, ever drinking,",
                              "All in mail, never clinking." },
                Answer = "fish",
                AlternateAnswers = new[] { "a fish", "fishes" },
                Hint = "I swim but never walk...",
                RewardXP = 100,
                FailureDamage = 25,
                GuardianType = GuardianType.Spirit
            });

            riddles.Add(new Riddle
            {
                Id = ++id,
                Difficulty = 3,
                Text = new[] { "This thing all things devours:",
                              "Birds, beasts, trees, flowers;",
                              "Gnaws iron, bites steel;",
                              "Grinds hard stones to meal;",
                              "Slays king, ruins town,",
                              "And beats high mountain down." },
                Answer = "time",
                AlternateAnswers = new[] { "the time" },
                Hint = "I wait for no one...",
                RewardXP = 100,
                FailureDamage = 30,
                GuardianType = GuardianType.VoidEntity
            });

            riddles.Add(new Riddle
            {
                Id = ++id,
                Difficulty = 1,
                Text = new[] { "What can run but never walks,",
                              "Has a mouth but never talks,",
                              "Has a head but never weeps,",
                              "Has a bed but never sleeps?" },
                Answer = "river",
                AlternateAnswers = new[] { "a river", "stream", "water" },
                Hint = "I flow through the land...",
                RewardXP = 50,
                FailureDamage = 10,
                GuardianType = GuardianType.Spirit
            });

            riddles.Add(new Riddle
            {
                Id = ++id,
                Difficulty = 2,
                Text = new[] { "I have no legs, but I can dance.",
                              "I have no lungs, but I need air to live.",
                              "I can be small or I can be big.",
                              "Touch me and I will consume you." },
                Answer = "fire",
                AlternateAnswers = new[] { "flame", "flames" },
                Hint = "I am born of sparks...",
                RewardXP = 75,
                FailureDamage = 20,
                GuardianType = GuardianType.Demon
            });

            riddles.Add(new Riddle
            {
                Id = ++id,
                Difficulty = 3,
                Text = new[] { "I am the shadow that follows you home.",
                              "I am the doubt that clouds your mind.",
                              "I am strongest when you are weakest.",
                              "What am I?" },
                Answer = "fear",
                AlternateAnswers = new[] { "doubt", "anxiety", "worry" },
                Hint = "Face me, and I grow smaller...",
                RewardXP = 100,
                FailureDamage = 25,
                GuardianType = GuardianType.Demon
            });

            riddles.Add(new Riddle
            {
                Id = ++id,
                Difficulty = 4,
                Text = new[] { "What is greater than the gods,",
                              "More evil than demons,",
                              "The poor have it,",
                              "The rich need it,",
                              "And if you eat it, you will die?" },
                Answer = "nothing",
                AlternateAnswers = new[] { "none", "emptiness", "the void" },
                Hint = "Think of absence, not presence...",
                RewardXP = 125,
                FailureDamage = 35,
                GuardianType = GuardianType.VoidEntity
            });

            // ═══════════════════════════════════════════════════════════════
            // GODLY/MYTHOLOGICAL RIDDLES
            // ═══════════════════════════════════════════════════════════════

            riddles.Add(new Riddle
            {
                Id = ++id,
                Difficulty = 4,
                Theme = DungeonTheme.AncientRuins,
                Text = new[] { "Maelketh rules it, but cannot escape it.",
                              "Veloura embodies it, but is consumed by it.",
                              "Thorgrim denies it, but enforces it.",
                              "What is the chain that binds even gods?" },
                Answer = "passion",
                AlternateAnswers = new[] { "desire", "want", "emotion", "longing" },
                Hint = "It moves us all, mortal and divine...",
                RewardXP = 150,
                FailureDamage = 35,
                GuardianType = GuardianType.Spirit
            });

            riddles.Add(new Riddle
            {
                Id = ++id,
                Difficulty = 4,
                Theme = DungeonTheme.AncientRuins,
                Text = new[] { "Before Manwe, there was only this.",
                              "After all ends, only this remains.",
                              "Manwe emerged from it, and all shall return.",
                              "What is the source and the destination?" },
                Answer = "ocean",
                AlternateAnswers = new[] { "the ocean", "void", "nothing", "unity", "oneness" },
                Hint = "All waves know it, though they forget...",
                RewardXP = 150,
                FailureDamage = 35,
                GuardianType = GuardianType.VoidEntity,
                IsOceanPhilosophy = true
            });

            riddles.Add(new Riddle
            {
                Id = ++id,
                Difficulty = 3,
                Text = new[] { "Kings and peasants, warriors and scholars -",
                              "All must kneel before me.",
                              "I am the great equalizer.",
                              "What am I?" },
                Answer = "death",
                AlternateAnswers = new[] { "time", "fate" },
                Hint = "None escape my embrace...",
                RewardXP = 100,
                FailureDamage = 25,
                GuardianType = GuardianType.Specter
            });

            riddles.Add(new Riddle
            {
                Id = ++id,
                Difficulty = 3,
                Text = new[] { "I am always coming but never arrive.",
                              "I am awaited but never seen.",
                              "When I finally appear, I am something else.",
                              "What am I?" },
                Answer = "tomorrow",
                AlternateAnswers = new[] { "future", "the future", "the tomorrow" },
                Hint = "I become today the moment you reach me...",
                RewardXP = 100,
                FailureDamage = 25,
                GuardianType = GuardianType.Spirit
            });

            // More quick riddles to reach 50+
            AddQuickRiddles();

        }

        private void AddQuickRiddles()
        {
            int id = riddles.Count;

            // Simple riddles for variety
            riddles.Add(new Riddle { Id = ++id, Difficulty = 1, Text = new[] { "What has an eye but cannot see?" }, Answer = "needle", AlternateAnswers = new[] { "a needle", "storm", "hurricane" }, RewardXP = 50, FailureDamage = 10, GuardianType = GuardianType.Sphinx });

            riddles.Add(new Riddle { Id = ++id, Difficulty = 1, Text = new[] { "What comes down but never goes up?" }, Answer = "rain", AlternateAnswers = new[] { "the rain", "snow" }, RewardXP = 50, FailureDamage = 10, GuardianType = GuardianType.Spirit });

            riddles.Add(new Riddle { Id = ++id, Difficulty = 2, Text = new[] { "What breaks yet never falls, and what falls yet never breaks?" }, Answer = "day and night", AlternateAnswers = new[] { "daybreak and nightfall", "dawn and dusk" }, RewardXP = 75, FailureDamage = 15, GuardianType = GuardianType.Spirit });

            riddles.Add(new Riddle { Id = ++id, Difficulty = 2, Text = new[] { "I have teeth but cannot bite. What am I?" }, Answer = "comb", AlternateAnswers = new[] { "a comb", "saw", "a saw", "gear" }, RewardXP = 75, FailureDamage = 15, GuardianType = GuardianType.Golem });

            riddles.Add(new Riddle { Id = ++id, Difficulty = 2, Text = new[] { "The more I dry, the wetter I become. What am I?" }, Answer = "towel", AlternateAnswers = new[] { "a towel" }, RewardXP = 75, FailureDamage = 15, GuardianType = GuardianType.Spirit });

            riddles.Add(new Riddle { Id = ++id, Difficulty = 3, Text = new[] { "What word becomes shorter when you add two letters to it?" }, Answer = "short", AlternateAnswers = new[] { "the word short" }, RewardXP = 100, FailureDamage = 25, GuardianType = GuardianType.Sphinx });

            riddles.Add(new Riddle { Id = ++id, Difficulty = 1, Text = new[] { "What invention lets you look right through a wall?" }, Answer = "window", AlternateAnswers = new[] { "a window", "glass" }, RewardXP = 50, FailureDamage = 10, GuardianType = GuardianType.Golem });

            riddles.Add(new Riddle { Id = ++id, Difficulty = 2, Text = new[] { "What gets sharper the more you use it?" }, Answer = "brain", AlternateAnswers = new[] { "mind", "your brain", "your mind", "wit" }, RewardXP = 75, FailureDamage = 15, GuardianType = GuardianType.Sphinx });

            riddles.Add(new Riddle { Id = ++id, Difficulty = 3, Text = new[] { "I am taken from a mine and shut in a wooden case,", "from which I am never released,", "yet almost everyone uses me. What am I?" }, Answer = "pencil lead", AlternateAnswers = new[] { "lead", "graphite", "pencil" }, RewardXP = 100, FailureDamage = 25, GuardianType = GuardianType.Golem });

            riddles.Add(new Riddle { Id = ++id, Difficulty = 2, Text = new[] { "What belongs to you but others use it more than you do?" }, Answer = "name", AlternateAnswers = new[] { "your name", "my name" }, RewardXP = 75, FailureDamage = 15, GuardianType = GuardianType.Spirit });

            riddles.Add(new Riddle { Id = ++id, Difficulty = 3, Text = new[] { "What can you catch but not throw?" }, Answer = "cold", AlternateAnswers = new[] { "a cold", "illness", "disease" }, RewardXP = 100, FailureDamage = 25, GuardianType = GuardianType.Specter });

            riddles.Add(new Riddle { Id = ++id, Difficulty = 2, Text = new[] { "What goes up and down but doesn't move?" }, Answer = "stairs", AlternateAnswers = new[] { "staircase", "temperature", "ladder" }, RewardXP = 75, FailureDamage = 15, GuardianType = GuardianType.Golem });
        }

        #endregion
    }

    #region Riddle Data Classes

    public class Riddle
    {
        public int Id { get; set; }
        public int Difficulty { get; set; } = 1;
        public DungeonTheme? Theme { get; set; }
        public string[] Text { get; set; } = Array.Empty<string>();
        public string Answer { get; set; } = "";
        public string[] AlternateAnswers { get; set; } = Array.Empty<string>();
        public string Hint { get; set; } = "";
        public int MaxAttempts { get; set; } = 3;
        public int RewardXP { get; set; } = 50;
        public int FailureDamage { get; set; } = 10;
        public GuardianType GuardianType { get; set; } = GuardianType.Spirit;
        public bool IsOceanPhilosophy { get; set; } = false;
        public bool FleeAllowed { get; set; } = true;
    }

    public class RiddleResult
    {
        public bool Solved { get; set; }
        public bool Fled { get; set; }
        public int Attempts { get; set; }
        public int XPEarned { get; set; }
        public int DamageTaken { get; set; }
    }

    public enum GuardianType
    {
        Specter,
        Sphinx,
        Demon,
        Spirit,
        Golem,
        VoidEntity
    }

    #endregion
}
