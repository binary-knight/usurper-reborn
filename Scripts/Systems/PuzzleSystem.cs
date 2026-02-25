using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UsurperRemake.Utils;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Comprehensive Puzzle System for dungeon challenges
    /// Handles logic puzzles, environmental puzzles, and combat puzzles
    /// </summary>
    public class PuzzleSystem
    {
        private static PuzzleSystem? _instance;
        public static PuzzleSystem Instance => _instance ??= new PuzzleSystem();

        private Random random = new Random();

        // Track solved puzzles per floor
        private Dictionary<int, HashSet<string>> solvedPuzzles = new();

        public event Action<PuzzleType, bool>? OnPuzzleCompleted;

        public PuzzleSystem()
        {
            _instance = this;
        }

        /// <summary>
        /// Mark a puzzle as solved on a given floor
        /// </summary>
        public void MarkPuzzleSolved(int floor, string puzzleTitle)
        {
            if (!solvedPuzzles.ContainsKey(floor))
            {
                solvedPuzzles[floor] = new HashSet<string>();
            }
            solvedPuzzles[floor].Add(puzzleTitle);
            OnPuzzleCompleted?.Invoke(PuzzleType.LeverSequence, true);
        }

        /// <summary>
        /// Check if a puzzle has been solved on a floor
        /// </summary>
        public bool IsPuzzleSolved(int floor, string puzzleTitle)
        {
            return solvedPuzzles.ContainsKey(floor) && solvedPuzzles[floor].Contains(puzzleTitle);
        }

        /// <summary>
        /// Generate a puzzle for a room based on type and difficulty
        /// </summary>
        public PuzzleInstance GeneratePuzzle(PuzzleType type, int difficulty, DungeonTheme theme)
        {
            return type switch
            {
                PuzzleType.LeverSequence => GenerateLeverPuzzle(difficulty, theme),
                PuzzleType.SymbolAlignment => GenerateSymbolPuzzle(difficulty, theme),
                PuzzleType.PressurePlates => GeneratePressurePuzzle(difficulty, theme),
                PuzzleType.NumberGrid => GenerateNumberPuzzle(difficulty),
                PuzzleType.MemoryMatch => GenerateMemoryPuzzle(difficulty, theme),
                PuzzleType.LightDarkness => GenerateLightPuzzle(difficulty, theme),
                PuzzleType.ItemCombination => GenerateItemPuzzle(difficulty, theme),
                PuzzleType.EnvironmentChange => GenerateEnvironmentPuzzle(difficulty, theme),
                PuzzleType.ReflectionPuzzle => GenerateReflectionPuzzle(difficulty, theme),
                _ => GenerateLeverPuzzle(difficulty, theme)
            };
        }

        /// <summary>
        /// Get a random puzzle type appropriate for the floor level
        /// </summary>
        public PuzzleType GetRandomPuzzleType(int floorLevel)
        {
            var availableTypes = new List<PuzzleType>
            {
                PuzzleType.LeverSequence,
                PuzzleType.SymbolAlignment,
                PuzzleType.NumberGrid
            };

            // Add more complex puzzles at deeper floors
            if (floorLevel >= 15)
            {
                availableTypes.Add(PuzzleType.PressurePlates);
                availableTypes.Add(PuzzleType.MemoryMatch);
            }

            if (floorLevel >= 30)
            {
                availableTypes.Add(PuzzleType.LightDarkness);
                availableTypes.Add(PuzzleType.ItemCombination);
            }

            if (floorLevel >= 50)
            {
                availableTypes.Add(PuzzleType.EnvironmentChange);
                availableTypes.Add(PuzzleType.ReflectionPuzzle);
            }

            return availableTypes[random.Next(availableTypes.Count)];
        }

        #region Puzzle Generation

        private PuzzleInstance GenerateLeverPuzzle(int difficulty, DungeonTheme theme)
        {
            int leverCount = 3 + difficulty;
            var solution = Enumerable.Range(0, leverCount).OrderBy(_ => random.Next()).ToList();

            // Generate logical hints - the solution order tells which lever (1-indexed) to pull
            var hints = GenerateLeverHints(solution, leverCount, difficulty);

            return new PuzzleInstance
            {
                Type = PuzzleType.LeverSequence,
                Difficulty = difficulty,
                Theme = theme,
                Title = GetLeverPuzzleTitle(theme),
                Description = $"Before you stand {leverCount} levers, numbered 1 through {leverCount}. " +
                             "Study the inscriptions to determine the correct sequence.",
                Solution = solution.Select(i => (i + 1).ToString()).ToList(), // Convert to 1-indexed
                CurrentState = new List<string>(),
                MaxAttempts = 3 + difficulty,
                AttemptsRemaining = 3 + difficulty,
                Hints = hints,
                FailureDamagePercent = 10 + (difficulty * 5),
                SuccessXP = 50 * difficulty
            };
        }

        private List<string> GenerateLeverHints(List<int> solution, int leverCount, int difficulty)
        {
            var hints = new List<string>();
            hints.Add("Ancient mechanisms reveal their secrets:");
            hints.Add("");

            // Generate a hint for each position in the sequence
            for (int i = 0; i < solution.Count; i++)
            {
                int leverNum = solution[i] + 1; // Convert to 1-indexed
                string hint = GetLeverHint(leverNum, i, solution.Count);
                hints.Add($"  {GetPositionNumeral(i + 1)}: {hint}");
            }

            return hints;
        }

        private string GetLeverHint(int leverNum, int position, int total)
        {
            // Create riddles that hint at the number without stating it directly
            var numberRiddles = new Dictionary<int, string[]>
            {
                { 1, new[] {
                    "The lone wolf leads the pack.",
                    "First among equals, standing alone.",
                    "Unity - there is only one."
                }},
                { 2, new[] {
                    "Partners dance as a pair.",
                    "Eyes come in this number.",
                    "The swan's neck curves like this numeral."
                }},
                { 3, new[] {
                    "The triangle's corners count thus.",
                    "Past, present, future - a trinity.",
                    "Wishes granted come in this many."
                }},
                { 4, new[] {
                    "Seasons cycle in this count.",
                    "Cardinal directions number this many.",
                    "Legs of a table, corners of a square."
                }},
                { 5, new[] {
                    "Fingers on one hand.",
                    "Points of a star, senses of man.",
                    "The pentagon's sides."
                }},
                { 6, new[] {
                    "The die's highest face.",
                    "Legs of an insect crawling.",
                    "Half a dozen, no more, no less."
                }},
                { 7, new[] {
                    "Days complete a week.",
                    "Deadly sins, heavenly virtues.",
                    "Colors in the rainbow arc."
                }},
                { 8, new[] {
                    "Spider's legs, octopus arms.",
                    "The infinite symbol standing upright.",
                    "Two fours joined as one."
                }}
            };

            if (numberRiddles.TryGetValue(leverNum, out var riddles))
            {
                return riddles[random.Next(riddles.Length)];
            }

            // Fallback for larger numbers
            return $"Count to {leverNum} and pull.";
        }

        private PuzzleInstance GenerateSymbolPuzzle(int difficulty, DungeonTheme theme)
        {
            var symbols = GetThemedSymbols(theme);
            int panelCount = Math.Min(3 + (difficulty / 2), symbols.Length); // Don't exceed available symbols

            // Shuffle symbols and pick unique ones for the solution (no repeats)
            var shuffledSymbols = symbols.OrderBy(_ => random.Next()).ToList();
            var solution = shuffledSymbols.Take(panelCount).ToList();

            // Generate cryptic clues that allow the player to deduce the solution
            var clues = GenerateSymbolClues(solution, symbols, theme);

            return new PuzzleInstance
            {
                Type = PuzzleType.SymbolAlignment,
                Difficulty = difficulty,
                Theme = theme,
                Title = "Symbol Alignment",
                Description = $"A circular mechanism with {panelCount} rotating panels. " +
                             "Each panel displays various symbols. Study the inscriptions to find the correct sequence.",
                Solution = solution,
                CurrentState = Enumerable.Repeat(symbols[0], panelCount).ToList(),
                AvailableChoices = symbols.ToList(),
                MaxAttempts = 5 + difficulty,
                AttemptsRemaining = 5 + difficulty,
                Hints = clues,
                FailureDamagePercent = 5 + (difficulty * 3),
                SuccessXP = 40 * difficulty
            };
        }

        private PuzzleInstance GeneratePressurePuzzle(int difficulty, DungeonTheme theme)
        {
            int plateCount = 4 + difficulty;
            var solution = Enumerable.Range(0, plateCount).OrderBy(_ => random.Next()).ToList();

            // Generate hints describing wear patterns that reveal the order
            var hints = GeneratePressurePlateHints(solution, plateCount);

            return new PuzzleInstance
            {
                Type = PuzzleType.PressurePlates,
                Difficulty = difficulty,
                Theme = theme,
                Title = "Pressure Plates",
                Description = $"The floor is divided into {plateCount} distinct pressure plates, numbered 1 through {plateCount}. " +
                             "Study the wear patterns to determine the safe path.",
                Solution = solution.Select(i => (i + 1).ToString()).ToList(), // Convert to 1-indexed
                CurrentState = new List<string>(),
                MaxAttempts = 2 + difficulty,
                AttemptsRemaining = 2 + difficulty,
                Hints = hints,
                FailureDamagePercent = 15 + (difficulty * 5),
                SuccessXP = 60 * difficulty,
                RequiresMovement = true
            };
        }

        private List<string> GeneratePressurePlateHints(List<int> solution, int plateCount)
        {
            var hints = new List<string>();
            hints.Add("Dust and wear patterns reveal the path of those who passed before:");
            hints.Add("");

            // Describe wear patterns in terms of position in sequence
            var wearDescriptions = new[]
            {
                "deeply worn, stepped on first by all who passed",
                "second-most worn, the path continues here",
                "moderately worn, midway through the journey",
                "lightly worn, near the end of the path",
                "barely touched, the final step before safety"
            };

            // Create hints based on relative wear
            for (int i = 0; i < solution.Count; i++)
            {
                int plateNum = solution[i] + 1; // Which plate (1-indexed)
                int stepOrder = i; // When in sequence (0-indexed)

                string wearDesc;
                if (stepOrder == 0)
                    wearDesc = "most worn of all - this is where they began";
                else if (stepOrder == solution.Count - 1)
                    wearDesc = "least worn - the final step";
                else if (stepOrder == 1)
                    wearDesc = "second-most worn - they stepped here next";
                else if (stepOrder == solution.Count - 2)
                    wearDesc = "barely touched - nearly at the end";
                else
                    wearDesc = "moderately worn - somewhere in the middle";

                hints.Add($"  Plate {plateNum}: {wearDesc}");
            }

            return hints;
        }

        private PuzzleInstance GenerateNumberPuzzle(int difficulty)
        {
            // Generate a simple math puzzle
            int target = 10 + (difficulty * 5) + random.Next(20);
            var numbers = new List<int>();
            int remaining = target;

            while (remaining > 0)
            {
                int n = random.Next(1, Math.Min(remaining + 1, 10));
                numbers.Add(n);
                remaining -= n;
            }

            // Add some red herrings
            for (int i = 0; i < difficulty; i++)
            {
                numbers.Add(random.Next(1, 15));
            }

            numbers = numbers.OrderBy(_ => random.Next()).ToList();

            return new PuzzleInstance
            {
                Type = PuzzleType.NumberGrid,
                Difficulty = difficulty,
                Theme = DungeonTheme.AncientRuins,
                Title = "The Number Grid",
                Description = $"Ancient numerals are carved into stone tiles. " +
                             $"Select tiles that sum to exactly {target}.",
                Solution = new List<string> { target.ToString() },
                CurrentState = new List<string>(),
                AvailableChoices = numbers.Select(n => n.ToString()).ToList(),
                AvailableNumbers = numbers,
                TargetNumber = target,
                MaxAttempts = 3 + difficulty,
                AttemptsRemaining = 3 + difficulty,
                Hints = new List<string> { $"The answer is {target}. Not all numbers are needed." },
                FailureDamagePercent = 10,
                SuccessXP = 45 * difficulty,
                CustomData = new Dictionary<string, object> { ["target"] = target }
            };
        }

        private PuzzleInstance GenerateMemoryPuzzle(int difficulty, DungeonTheme theme)
        {
            var symbols = GetThemedSymbols(theme);
            int sequenceLength = 3 + difficulty;
            var solution = new List<string>();

            for (int i = 0; i < sequenceLength; i++)
            {
                solution.Add(symbols[random.Next(symbols.Length)]);
            }

            return new PuzzleInstance
            {
                Type = PuzzleType.MemoryMatch,
                Difficulty = difficulty,
                Theme = theme,
                Title = "Memory of the Ancients",
                Description = "Glowing symbols flash before you in sequence. " +
                             "Remember and repeat the pattern exactly.",
                Solution = solution,
                CurrentState = new List<string>(),
                AvailableChoices = symbols.ToList(),
                MaxAttempts = 2 + (difficulty / 2),
                AttemptsRemaining = 2 + (difficulty / 2),
                Hints = new List<string>(),
                FailureDamagePercent = 8 + (difficulty * 3),
                SuccessXP = 55 * difficulty,
                RequiresSequence = true,
                ShowSolutionFirst = true
            };
        }

        private PuzzleInstance GenerateLightPuzzle(int difficulty, DungeonTheme theme)
        {
            int torchCount = 4 + difficulty;
            var solution = new List<string>();

            // Generate pattern (some on, some off)
            for (int i = 0; i < torchCount; i++)
            {
                solution.Add(random.NextDouble() < 0.5 ? "lit" : "unlit");
            }

            // Generate hints that describe which torches should be lit/unlit
            var hints = GenerateLightPuzzleHints(solution, torchCount);

            return new PuzzleInstance
            {
                Type = PuzzleType.LightDarkness,
                Difficulty = difficulty,
                Theme = theme,
                Title = "Dance of Light and Shadow",
                Description = $"{torchCount} ancient torches line the walls, numbered 1 through {torchCount}. " +
                             "Some must burn, some must stay dark. Read the inscriptions carefully.",
                Solution = solution,
                CurrentState = Enumerable.Repeat("unlit", torchCount).ToList(),
                AvailableChoices = new List<string> { "toggle" },
                MaxAttempts = torchCount + difficulty,
                AttemptsRemaining = torchCount + difficulty,
                Hints = hints,
                FailureDamagePercent = 5,
                SuccessXP = 50 * difficulty
            };
        }

        private List<string> GenerateLightPuzzleHints(List<string> solution, int torchCount)
        {
            var hints = new List<string>();
            int litCount = solution.Count(s => s == "lit");

            hints.Add("Faded verses on the wall speak of the torches:");
            hints.Add("");

            // Give a clue for each torch
            for (int i = 0; i < solution.Count; i++)
            {
                bool shouldBeLit = solution[i] == "lit";
                string hint = GetTorchRiddle(i + 1, shouldBeLit);
                hints.Add($"  Torch {i + 1}: {hint}");
            }

            hints.Add("");
            hints.Add($"  (When balanced, {litCount} flames shall dance.)");

            return hints;
        }

        private string GetTorchRiddle(int torchNum, bool shouldBeLit)
        {
            if (shouldBeLit)
            {
                var litRiddles = new[]
                {
                    "Let this one blaze against the dark.",
                    "Fire must kiss this sconce.",
                    "This bearer craves the flame.",
                    "Light shall reign here.",
                    "The spirits demand this one burn.",
                    "Ignite this vessel of light."
                };
                return litRiddles[random.Next(litRiddles.Length)];
            }
            else
            {
                var unlitRiddles = new[]
                {
                    "Darkness must claim this one.",
                    "No flame shall touch this place.",
                    "Let shadows embrace this sconce.",
                    "This one rests in peaceful dark.",
                    "The void demands this stay cold.",
                    "Deny fire to this holder."
                };
                return unlitRiddles[random.Next(unlitRiddles.Length)];
            }
        }

        private PuzzleInstance GenerateItemPuzzle(int difficulty, DungeonTheme theme)
        {
            var (item1, item2, result) = GetItemCombination(theme, difficulty);
            var hints = GenerateItemCombinationHints(item1, item2, result);

            return new PuzzleInstance
            {
                Type = PuzzleType.ItemCombination,
                Difficulty = difficulty,
                Theme = theme,
                Title = "The Alchemist's Lock",
                Description = "A mechanism requires a specific substance to activate. " +
                             "Combine two items from your surroundings to create it.",
                Solution = new List<string> { item1, item2 },
                CurrentState = new List<string>(),
                AvailableChoices = GenerateItemChoices(item1, item2, difficulty),
                MaxAttempts = 3 + difficulty,
                AttemptsRemaining = 3 + difficulty,
                Hints = hints,
                FailureDamagePercent = 15 + (difficulty * 3),
                SuccessXP = 65 * difficulty,
                CustomData = new Dictionary<string, object> { ["result"] = result }
            };
        }

        private List<string> GenerateItemCombinationHints(string item1, string item2, string result)
        {
            var hints = new List<string>();

            // Describe what we need to create
            var resultDescriptions = new Dictionary<string, string>
            {
                { "steam", "A hot mist that rises and obscures" },
                { "awakening_paste", "A substance that stirs the dormant" },
                { "glowing_crystal", "A gem that holds captured light" },
                { "flash_powder", "A mixture that explodes with blinding light" },
                { "blessed_silver", "Metal purified by divine waters" },
                { "twilight_orb", "A sphere balanced between day and night" },
                { "eternal_flame", "Fire that never dies" },
                { "null_essence", "The distilled essence of nothing" }
            };

            var itemDescriptions = new Dictionary<string, string>
            {
                { "water", "The flowing element, giver of life" },
                { "fire_salt", "Crystals that burn with inner heat" },
                { "bone_dust", "Powder ground from the remains of the dead" },
                { "blood", "The crimson river of life" },
                { "crystal_shard", "A fragment of pure, clear stone" },
                { "moonlight", "Captured silver radiance of night" },
                { "sulfur", "Yellow brimite, the devil's element" },
                { "charcoal", "Wood transformed by flame to black powder" },
                { "silver_dust", "Precious metal ground fine as flour" },
                { "holy_water", "Water blessed by the divine" },
                { "shadow_essence", "Darkness made tangible" },
                { "light_fragment", "A piece of captured radiance" },
                { "dragon_scale", "Armor shed from the great serpent" },
                { "phoenix_ash", "Remains of the firebird's rebirth" },
                { "void_shard", "A piece of absolute nothing" },
                { "soul_fragment", "A whisper of departed spirit" }
            };

            string resultDesc = resultDescriptions.GetValueOrDefault(result, $"the mysterious {result}");
            string item1Desc = itemDescriptions.GetValueOrDefault(item1, item1.Replace("_", " "));
            string item2Desc = itemDescriptions.GetValueOrDefault(item2, item2.Replace("_", " "));

            hints.Add("Ancient alchemical texts inscribed nearby reveal:");
            hints.Add("");
            hints.Add($"  \"To create {resultDesc},\"");
            hints.Add($"  \"one must combine {item1Desc}\"");
            hints.Add($"  \"with {item2Desc}.\"");

            return hints;
        }

        private PuzzleInstance GenerateEnvironmentPuzzle(int difficulty, DungeonTheme theme)
        {
            var (description, solution, hint) = GetEnvironmentPuzzle(theme, difficulty);

            return new PuzzleInstance
            {
                Type = PuzzleType.EnvironmentChange,
                Difficulty = difficulty,
                Theme = theme,
                Title = "Elemental Challenge",
                Description = description,
                Solution = solution,
                CurrentState = new List<string>(),
                MaxAttempts = 4 + difficulty,
                AttemptsRemaining = 4 + difficulty,
                Hints = new List<string> { hint },
                FailureDamagePercent = 20 + (difficulty * 5),
                SuccessXP = 70 * difficulty,
                RequiresEnvironmentInteraction = true
            };
        }

        private PuzzleInstance GenerateReflectionPuzzle(int difficulty, DungeonTheme theme)
        {
            int mirrorCount = 3 + (difficulty / 2);
            var solution = new List<string>();

            var angles = new[] { "0", "45", "90", "135" };
            for (int i = 0; i < mirrorCount; i++)
            {
                solution.Add(angles[random.Next(angles.Length)]);
            }

            var hints = GenerateReflectionHints(solution, mirrorCount);

            return new PuzzleInstance
            {
                Type = PuzzleType.ReflectionPuzzle,
                Difficulty = difficulty,
                Theme = theme,
                Title = "Hall of Mirrors",
                Description = $"A beam of light enters from the left and must reach a crystal on the right. " +
                             $"Set each of the {mirrorCount} mirrors to the correct angle (0°, 45°, 90°, or 135°).",
                Solution = solution,
                CurrentState = Enumerable.Repeat("0", mirrorCount).ToList(),
                AvailableChoices = angles.ToList(),
                MaxAttempts = mirrorCount * 2 + difficulty,
                AttemptsRemaining = mirrorCount * 2 + difficulty,
                Hints = hints,
                FailureDamagePercent = 5,
                SuccessXP = 60 * difficulty
            };
        }

        private List<string> GenerateReflectionHints(List<string> solution, int mirrorCount)
        {
            var hints = new List<string>();
            hints.Add("Etchings on the mirror frames describe their proper angles:");
            hints.Add("");

            var angleDescriptions = new Dictionary<string, string[]>
            {
                { "0", new[] {
                    "flat as still water",
                    "parallel to the horizon",
                    "level with the earth"
                }},
                { "45", new[] {
                    "tilted as a roof's slope",
                    "angled like a bird's wing in flight",
                    "halfway between flat and upright"
                }},
                { "90", new[] {
                    "standing straight as a soldier",
                    "perpendicular to the ground",
                    "upright as a tower"
                }},
                { "135", new[] {
                    "leaning back like a resting traveler",
                    "tilted opposite to the common slope",
                    "angled as if looking to the sky behind"
                }}
            };

            for (int i = 0; i < solution.Count; i++)
            {
                string angle = solution[i];
                var descriptions = angleDescriptions[angle];
                string desc = descriptions[random.Next(descriptions.Length)];
                hints.Add($"  Mirror {i + 1}: Set it {desc} ({angle}°)");
            }

            return hints;
        }

        #endregion

        #region Puzzle Interaction

        /// <summary>
        /// Present a puzzle to the player and handle interaction
        /// </summary>
        public async Task<PuzzleResult> PresentPuzzle(PuzzleInstance puzzle, Character player, TerminalEmulator terminal)
        {
            terminal.Clear();
            DisplayPuzzleHeader(puzzle, terminal);

            bool solved = false;
            int totalAttempts = 0;

            while (!solved && puzzle.AttemptsRemaining > 0)
            {
                totalAttempts++;

                // Show current state
                DisplayPuzzleState(puzzle, terminal);

                // Show hints if available and player asks
                if (puzzle.Hints.Count > 0 && totalAttempts > 1)
                {
                    terminal.WriteLine($"[Type 'hint' for a clue, or 'quit' to give up]", "dark_cyan");
                }

                // Get player input based on puzzle type
                var result = await GetPuzzleInput(puzzle, terminal);

                if (result.Action == PuzzleAction.Quit)
                {
                    terminal.WriteLine("You step back from the puzzle, unable to solve it.", "yellow");
                    return new PuzzleResult { Solved = false, Fled = true };
                }

                if (result.Action == PuzzleAction.Hint)
                {
                    ShowHint(puzzle, terminal);
                    continue;
                }

                // Check the answer
                if (CheckPuzzleSolution(puzzle, result.Input))
                {
                    solved = true;
                    await DisplayPuzzleSuccess(puzzle, player, terminal);
                }
                else
                {
                    puzzle.AttemptsRemaining--;
                    await DisplayPuzzleFailure(puzzle, player, terminal);
                }
            }

            OnPuzzleCompleted?.Invoke(puzzle.Type, solved);

            return new PuzzleResult
            {
                Solved = solved,
                Attempts = totalAttempts,
                XPEarned = solved ? puzzle.SuccessXP : 0,
                DamageTaken = solved ? 0 : CalculateFailureDamage(puzzle, player)
            };
        }

        private void DisplayPuzzleHeader(PuzzleInstance puzzle, TerminalEmulator terminal)
        {
            string diffText = puzzle.Difficulty switch
            {
                1 => "Simple",
                2 => "Moderate",
                3 => "Challenging",
                4 => "Difficult",
                _ => "Legendary"
            };

            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════╗", "bright_cyan");
            terminal.WriteLine($"║  {puzzle.Title.PadRight(62)}║", "bright_cyan");
            terminal.WriteLine($"║  Difficulty: {diffText.PadRight(51)}║", "cyan");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════╝", "bright_cyan");
            terminal.WriteLine("");
            terminal.WriteLine(puzzle.Description, "white");
            terminal.WriteLine("");
        }

        private void DisplayPuzzleState(PuzzleInstance puzzle, TerminalEmulator terminal)
        {
            terminal.WriteLine($"Attempts remaining: {puzzle.AttemptsRemaining}",
                puzzle.AttemptsRemaining > 2 ? "green" : "yellow");
            terminal.WriteLine("");

            switch (puzzle.Type)
            {
                case PuzzleType.LeverSequence:
                    DisplayLeverState(puzzle, terminal);
                    break;
                case PuzzleType.SymbolAlignment:
                    DisplaySymbolState(puzzle, terminal);
                    break;
                case PuzzleType.LightDarkness:
                    DisplayLightState(puzzle, terminal);
                    break;
                case PuzzleType.NumberGrid:
                    DisplayNumberState(puzzle, terminal);
                    break;
                case PuzzleType.MemoryMatch:
                    if (puzzle.ShowSolutionFirst && puzzle.CurrentState.Count == 0)
                    {
                        DisplayMemorySequence(puzzle, terminal);
                    }
                    break;
                default:
                    DisplayGenericState(puzzle, terminal);
                    break;
            }
        }

        private void DisplayLeverState(PuzzleInstance puzzle, TerminalEmulator terminal)
        {
            int leverCount = puzzle.Solution.Count;
            terminal.WriteLine("  Levers:", "white");
            for (int i = 0; i < leverCount; i++)
            {
                // CurrentState now stores 1-indexed lever numbers
                bool pulled = puzzle.CurrentState.Contains((i + 1).ToString());
                string status = pulled ? "[PULLED]" : "[------]";
                string color = pulled ? "green" : "gray";
                terminal.WriteLine($"    {i + 1}. {status}", color);
            }
            terminal.WriteLine("");
            terminal.WriteLine("Enter lever number to pull (1-" + leverCount + "):", "cyan");
        }

        private void DisplaySymbolState(PuzzleInstance puzzle, TerminalEmulator terminal)
        {
            terminal.WriteLine("  Current alignment:", "white");
            for (int i = 0; i < puzzle.CurrentState.Count; i++)
            {
                terminal.WriteLine($"    Panel {i + 1}: {puzzle.CurrentState[i]}", "gray");
            }
            terminal.WriteLine("");
            terminal.WriteLine("  Available symbols: " + string.Join(", ", puzzle.AvailableChoices), "cyan");
            terminal.WriteLine("");
            terminal.WriteLine("Enter: [panel number] [symbol] (e.g., '1 sun'):", "cyan");
        }

        private void DisplayLightState(PuzzleInstance puzzle, TerminalEmulator terminal)
        {
            terminal.WriteLine("  Torches:", "white");
            for (int i = 0; i < puzzle.CurrentState.Count; i++)
            {
                bool lit = puzzle.CurrentState[i] == "lit";
                string display = lit ? "* LIT *  " : "  UNLIT  ";
                string color = lit ? "bright_yellow" : "dark_gray";
                terminal.Write($"    Torch {i + 1}: ");
                terminal.WriteLine(display, color);
            }
            terminal.WriteLine("");
            terminal.WriteLine("Enter torch number to toggle (1-" + puzzle.CurrentState.Count + "):", "cyan");
        }

        private void DisplayNumberState(PuzzleInstance puzzle, TerminalEmulator terminal)
        {
            terminal.WriteLine("  Available numbers:", "white");
            terminal.WriteLine("    " + string.Join("  ", puzzle.AvailableChoices), "bright_cyan");
            terminal.WriteLine("");

            if (puzzle.CurrentState.Count > 0)
            {
                int sum = puzzle.CurrentState.Sum(s => int.Parse(s));
                terminal.WriteLine($"  Selected: {string.Join(" + ", puzzle.CurrentState)} = {sum}", "yellow");
            }

            int target = (int)puzzle.CustomData["target"];
            terminal.WriteLine($"  Target sum: {target}", "bright_green");
            terminal.WriteLine("");
            terminal.WriteLine("Enter a number to add/remove, or 'submit' when ready:", "cyan");
        }

        private void DisplayMemorySequence(PuzzleInstance puzzle, TerminalEmulator terminal)
        {
            terminal.WriteLine("  Watch the sequence carefully!", "bright_yellow");
            terminal.WriteLine("");
            terminal.WriteLine("  " + string.Join(" -> ", puzzle.Solution), "bright_magenta");
            terminal.WriteLine("");
            terminal.WriteLine("  (The sequence will be hidden after you begin)", "gray");
            terminal.WriteLine("");
            terminal.WriteLine("Press Enter when ready to begin...", "cyan");
        }

        private void DisplayGenericState(PuzzleInstance puzzle, TerminalEmulator terminal)
        {
            if (puzzle.CurrentState.Count > 0)
            {
                terminal.WriteLine("  Current state: " + string.Join(", ", puzzle.CurrentState), "yellow");
            }
            if (puzzle.AvailableChoices.Count > 0)
            {
                terminal.WriteLine("  Options: " + string.Join(", ", puzzle.AvailableChoices), "cyan");
            }
            terminal.WriteLine("");
        }

        private async Task<PuzzleInputResult> GetPuzzleInput(PuzzleInstance puzzle, TerminalEmulator terminal)
        {
            string input = await terminal.GetInputAsync("> ");
            input = input.Trim().ToLower();

            if (input == "quit" || input == "q" || input == "leave")
                return new PuzzleInputResult { Action = PuzzleAction.Quit };

            if (input == "hint" || input == "h")
                return new PuzzleInputResult { Action = PuzzleAction.Hint };

            return new PuzzleInputResult { Action = PuzzleAction.Attempt, Input = input };
        }

        private bool CheckPuzzleSolution(PuzzleInstance puzzle, string input)
        {
            switch (puzzle.Type)
            {
                case PuzzleType.LeverSequence:
                case PuzzleType.PressurePlates:
                    return CheckSequenceSolution(puzzle, input);

                case PuzzleType.SymbolAlignment:
                    return CheckSymbolSolution(puzzle, input);

                case PuzzleType.LightDarkness:
                    return CheckLightSolution(puzzle, input);

                case PuzzleType.NumberGrid:
                    return CheckNumberSolution(puzzle, input);

                case PuzzleType.MemoryMatch:
                    return CheckMemorySolution(puzzle, input);

                case PuzzleType.ReflectionPuzzle:
                    return CheckMirrorSolution(puzzle, input);

                default:
                    return puzzle.Solution.Contains(input);
            }
        }

        private bool CheckSequenceSolution(PuzzleInstance puzzle, string input)
        {
            // Add to current sequence
            if (int.TryParse(input, out int leverNum))
            {
                // Validate lever number is in valid range (1-indexed input)
                if (leverNum >= 1 && leverNum <= puzzle.Solution.Count)
                {
                    // Store as 1-indexed string to match Solution format
                    puzzle.CurrentState.Add(leverNum.ToString());

                    // Check if sequence matches so far
                    for (int i = 0; i < puzzle.CurrentState.Count; i++)
                    {
                        if (puzzle.CurrentState[i] != puzzle.Solution[i])
                        {
                            puzzle.CurrentState.Clear(); // Reset on wrong order
                            return false;
                        }
                    }

                    // Check if complete
                    return puzzle.CurrentState.Count == puzzle.Solution.Count;
                }
            }
            return false;
        }

        private bool CheckSymbolSolution(PuzzleInstance puzzle, string input)
        {
            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && int.TryParse(parts[0], out int panel))
            {
                panel--; // Convert to 0-indexed
                string symbol = parts[1];

                if (panel >= 0 && panel < puzzle.CurrentState.Count &&
                    puzzle.AvailableChoices.Contains(symbol))
                {
                    puzzle.CurrentState[panel] = symbol;

                    // Check if all panels match solution
                    return puzzle.CurrentState.SequenceEqual(puzzle.Solution);
                }
            }
            return false;
        }

        private bool CheckLightSolution(PuzzleInstance puzzle, string input)
        {
            if (int.TryParse(input, out int torch))
            {
                torch--; // Convert to 0-indexed
                if (torch >= 0 && torch < puzzle.CurrentState.Count)
                {
                    // Toggle
                    puzzle.CurrentState[torch] = puzzle.CurrentState[torch] == "lit" ? "unlit" : "lit";

                    // Check if matches solution
                    return puzzle.CurrentState.SequenceEqual(puzzle.Solution);
                }
            }
            return false;
        }

        private bool CheckNumberSolution(PuzzleInstance puzzle, string input)
        {
            if (input == "submit")
            {
                int sum = puzzle.CurrentState.Sum(s => int.Parse(s));
                int target = (int)puzzle.CustomData["target"];
                return sum == target;
            }

            if (int.TryParse(input, out int num) && puzzle.AvailableChoices.Contains(input))
            {
                if (puzzle.CurrentState.Contains(input))
                    puzzle.CurrentState.Remove(input);
                else
                    puzzle.CurrentState.Add(input);
            }
            return false;
        }

        private bool CheckMemorySolution(PuzzleInstance puzzle, string input)
        {
            puzzle.CurrentState.Add(input);

            // Check if matches so far
            for (int i = 0; i < puzzle.CurrentState.Count; i++)
            {
                if (!puzzle.Solution[i].Equals(puzzle.CurrentState[i], StringComparison.OrdinalIgnoreCase))
                {
                    puzzle.CurrentState.Clear();
                    return false;
                }
            }

            return puzzle.CurrentState.Count == puzzle.Solution.Count;
        }

        private bool CheckMirrorSolution(PuzzleInstance puzzle, string input)
        {
            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && int.TryParse(parts[0], out int mirror))
            {
                mirror--;
                if (mirror >= 0 && mirror < puzzle.CurrentState.Count &&
                    puzzle.AvailableChoices.Contains(parts[1]))
                {
                    puzzle.CurrentState[mirror] = parts[1];
                    return puzzle.CurrentState.SequenceEqual(puzzle.Solution);
                }
            }
            return false;
        }

        private void ShowHint(PuzzleInstance puzzle, TerminalEmulator terminal)
        {
            if (puzzle.Hints.Count > 0)
            {
                string hint = puzzle.Hints[Math.Min(puzzle.HintsUsed, puzzle.Hints.Count - 1)];
                puzzle.HintsUsed++;
                terminal.WriteLine("");
                terminal.WriteLine("═══ HINT ═══", "bright_yellow");
                terminal.WriteLine(hint, "yellow");
                terminal.WriteLine("═════════════", "bright_yellow");
                terminal.WriteLine("");
            }
            else
            {
                terminal.WriteLine("No hints available for this puzzle.", "gray");
            }
        }

        private async Task DisplayPuzzleSuccess(PuzzleInstance puzzle, Character player, TerminalEmulator terminal)
        {
            terminal.WriteLine("");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════╗", "bright_green");
            terminal.WriteLine("║                    P U Z Z L E   S O L V E D !                   ║", "bright_green");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════╝", "bright_green");
            terminal.WriteLine("");

            terminal.WriteLine($"You gain {puzzle.SuccessXP} experience!", "cyan");
            player.Experience += puzzle.SuccessXP;

            // Ocean philosophy tie-in for certain puzzles
            if (puzzle.Difficulty >= 4)
            {
                terminal.WriteLine("");
                terminal.WriteLine("As the mechanism unlocks, a whisper echoes:", "bright_magenta");
                terminal.WriteLine("'The wave that stops struggling finds its way home...'", "magenta");
                OceanPhilosophySystem.Instance.CollectFragment(WaveFragment.TheForgetting);
            }

            await terminal.GetInputAsync("Press Enter to continue...");
        }

        private async Task DisplayPuzzleFailure(PuzzleInstance puzzle, Character player, TerminalEmulator terminal)
        {
            terminal.WriteLine("");
            terminal.WriteLine("The puzzle resets with a grinding sound.", "yellow");

            if (puzzle.FailureDamagePercent > 0 && puzzle.AttemptsRemaining == 0)
            {
                int damage = CalculateFailureDamage(puzzle, player);
                player.HP = Math.Max(1, player.HP - damage);
                terminal.WriteLine($"A trap triggers! You take {damage} damage!", "red");
            }

            if (puzzle.AttemptsRemaining > 0)
            {
                terminal.WriteLine($"{puzzle.AttemptsRemaining} attempts remaining.", "yellow");
            }
            else
            {
                terminal.WriteLine("You have exhausted all attempts. The puzzle remains unsolved.", "dark_red");
            }

            await Task.Delay(500);
        }

        private int CalculateFailureDamage(PuzzleInstance puzzle, Character player)
        {
            return (int)(player.MaxHP * (puzzle.FailureDamagePercent / 100.0));
        }

        #endregion

        #region Helper Methods

        private string[] GetThemedSymbols(DungeonTheme theme)
        {
            return theme switch
            {
                DungeonTheme.Catacombs => new[] { "skull", "bone", "tomb", "cross", "candle", "ghost" },
                DungeonTheme.Sewers => new[] { "rat", "water", "pipe", "grate", "slime", "drain" },
                DungeonTheme.Caverns => new[] { "crystal", "stalactite", "bat", "gem", "pool", "mushroom" },
                DungeonTheme.AncientRuins => new[] { "sun", "moon", "star", "eye", "serpent", "crown" },
                DungeonTheme.DemonLair => new[] { "pentagram", "flame", "horn", "blood", "chain", "claw" },
                DungeonTheme.FrozenDepths => new[] { "snowflake", "icicle", "frost", "wind", "glacier", "aurora" },
                DungeonTheme.VolcanicPit => new[] { "fire", "lava", "ash", "smoke", "ember", "obsidian" },
                DungeonTheme.AbyssalVoid => new[] { "void", "eye", "spiral", "tear", "wave", "infinity" },
                _ => new[] { "circle", "square", "triangle", "diamond", "star", "cross" }
            };
        }

        private string GenerateSymbolHint(List<string> solution, string[] symbols)
        {
            if (solution.Count == 0) return "Study the symbols carefully.";

            // Give hint about first or last symbol
            return random.NextDouble() < 0.5
                ? $"The sequence begins with '{solution[0]}'..."
                : $"The final symbol is '{solution[solution.Count - 1]}'...";
        }

        /// <summary>
        /// Generate riddle-style clues that require thinking to solve.
        /// Each position gets a cryptic clue - no answers given directly.
        /// </summary>
        private List<string> GenerateSymbolClues(List<string> solution, string[] allSymbols, DungeonTheme theme)
        {
            var clues = new List<string>();

            // Theme-specific flavor text
            var flavorIntro = theme switch
            {
                DungeonTheme.Catacombs => "Ancient funeral rites inscribed on the wall speak in riddles:",
                DungeonTheme.Sewers => "Scratched warnings in the grime hint at the sequence:",
                DungeonTheme.Caverns => "Crystalline echoes whisper cryptic truths:",
                DungeonTheme.AncientRuins => "Faded hieroglyphics pose these mysteries:",
                DungeonTheme.DemonLair => "Burning runes seared into bone demand answers:",
                DungeonTheme.FrozenDepths => "Frost patterns form puzzling verses:",
                DungeonTheme.VolcanicPit => "Molten letters glow with hidden meaning:",
                DungeonTheme.AbyssalVoid => "Whispers from beyond speak in enigmas:",
                _ => "Cryptic inscriptions challenge the mind:"
            };

            clues.Add(flavorIntro);
            clues.Add("");

            // Generate a riddle for each position
            for (int i = 0; i < solution.Count; i++)
            {
                string symbol = solution[i];
                string riddle = GetRiddle(symbol);
                clues.Add($"  {GetPositionNumeral(i + 1)}: \"{riddle}\"");
            }

            return clues;
        }

        private string GetPositionNumeral(int num)
        {
            return num switch
            {
                1 => "I",
                2 => "II",
                3 => "III",
                4 => "IV",
                5 => "V",
                _ => num.ToString()
            };
        }

        private string GetRiddle(string symbol)
        {
            // Comprehensive riddles for all symbols - NO ANSWERS GIVEN
            var riddles = new Dictionary<string, string[]>
            {
                // Catacombs
                { "skull", new[] {
                    "I once held dreams and schemes, now hollow I remain.",
                    "The mind's fortress, emptied by time's cruel hand.",
                    "I grinned at kings and beggars alike when life ended."
                }},
                { "bone", new[] {
                    "I gave the body its frame, now stripped of purpose.",
                    "White as snow, I outlast the flesh that clothed me.",
                    "Dogs seek me, scholars study me, the grave reclaims me."
                }},
                { "tomb", new[] {
                    "I am the house that none wish to enter, yet all must.",
                    "Stone walls embrace eternal sleepers within my keep.",
                    "Built for one, visited by many, home to silence."
                }},
                { "cross", new[] {
                    "Two paths meet at my heart, one leads up, one across.",
                    "Salvation's mark, death's companion, hope in geometry.",
                    "I stand where faith and mortality intersect."
                }},
                { "candle", new[] {
                    "I weep as I illuminate, dying to give light.",
                    "Born tall, I shrink with purpose, my tears are wax.",
                    "A wick is my soul, flame my voice, darkness my foe."
                }},
                { "ghost", new[] {
                    "I walk through walls yet cannot hold a cup.",
                    "Death freed me from flesh but chained me to memory.",
                    "The living fear what they cannot touch - that is I."
                }},

                // Sewers
                { "rat", new[] {
                    "I thrive where others flee, my kingdom is filth.",
                    "Whiskers twitch in darkness, my teeth never stop growing.",
                    "Plague's courier, survivor supreme, I own the underground."
                }},
                { "water", new[] {
                    "I have no shape yet fill every vessel perfectly.",
                    "I carve mountains given time, yet a child can hold me.",
                    "Life needs me, yet too much of me brings death."
                }},
                { "pipe", new[] {
                    "Hollow veins of the city, carrying its lifeblood unseen.",
                    "I connect all yet go unnoticed, metal passage underground.",
                    "Through me flows what the surface wishes to forget."
                }},
                { "grate", new[] {
                    "I have many mouths but cannot eat, many eyes but cannot see.",
                    "Iron guardian of the deep, I let liquid pass but halt the rest.",
                    "Walk upon me without thought, but drop something and weep."
                }},
                { "slime", new[] {
                    "Neither solid nor liquid, I cling where nothing should grow.",
                    "Green and patient, I consume the forgotten slowly.",
                    "Touch me and regret it, ignore me and I spread."
                }},
                { "drain", new[] {
                    "The mouth that swallows all the city's tears.",
                    "I spiral down into darkness, everything flows to me.",
                    "Final destination before the journey continues below."
                }},

                // Caverns
                { "crystal", new[] {
                    "I grow in darkness yet capture and bend the light.",
                    "Geometry perfected by patient centuries underground.",
                    "Clear as thought, hard as resolve, I form in solitude."
                }},
                { "stalactite", new[] {
                    "I grow downward, one drip at a time, for millennia.",
                    "The ceiling's slow revenge, given centuries I reach the floor.",
                    "Stone icicles that never melt, we hang in patient rows."
                }},
                { "bat", new[] {
                    "I see with my voice and fly with my hands.",
                    "Night's child, I sleep inverted and hunt by echo.",
                    "Leather wings beat where no light reaches."
                }},
                { "gem", new[] {
                    "Pressure and time made me precious, now I tempt the greedy.",
                    "I sparkle in torchlight, kingdoms have fallen for my kind.",
                    "Earth's treasure, hidden deep, worth more than my weight."
                }},
                { "pool", new[] {
                    "Still and silent in the depths, I mirror the stone above.",
                    "No current stirs me, no sun warms me, yet life finds me.",
                    "Darkness made liquid, I wait in the cave's embrace."
                }},
                { "mushroom", new[] {
                    "I need no sun to grow, darkness is my garden.",
                    "Neither plant nor animal, I feast on decay.",
                    "Pale caps in the darkness, we are the cave's strange harvest."
                }},

                // Ancient Ruins
                { "sun", new[] {
                    "I rise and fall yet never move, giver of life and death.",
                    "Ancient peoples worshipped my face, drew my rays on stone.",
                    "Blinding to behold, my absence brings cold and fear."
                }},
                { "moon", new[] {
                    "I have no light of my own, yet I guide ships home.",
                    "My face changes nightly, my pull moves the seas.",
                    "Silver sentinel of night, I wax and wane eternal."
                }},
                { "star", new[] {
                    "I burn countless leagues away, yet you see me clearly.",
                    "Sailors trusted me, lovers wished upon me, I am ancient fire.",
                    "A point of light in the void, I outlive civilizations."
                }},
                { "eye", new[] {
                    "I see but cannot be seen seeing, truth passes through me.",
                    "Window to the soul, they say, yet I only receive.",
                    "The watchers painted me on temples to guard secrets."
                }},
                { "serpent", new[] {
                    "I shed my skin to be reborn, wisdom coils within me.",
                    "Legless I travel, voiceless I threaten, patient I wait.",
                    "Eden's tempter, medicine's symbol, feared and revered."
                }},
                { "crown", new[] {
                    "Heavy lies the head that wears me, yet all covet me.",
                    "Circle of gold that grants power and invites daggers.",
                    "Kings die, kingdoms fall, yet I pass to the next hand."
                }},

                // Demon Lair
                { "pentagram", new[] {
                    "Five points contain what should not be freed.",
                    "Draw me carefully or what's inside escapes.",
                    "Geometry of binding, each angle holds dark purpose."
                }},
                { "flame", new[] {
                    "I dance without music, consume without hunger eternal.",
                    "Feed me and I grow, starve me and I die.",
                    "Neither solid, liquid, nor gas - I am transformation."
                }},
                { "horn", new[] {
                    "I crown the beast, twisted trophy of dark power.",
                    "The innocent lamb lacks me, the devil bears two.",
                    "Curved and sharp, I mark those fallen from grace."
                }},
                { "blood", new[] {
                    "I am the river within, spilled to seal dark pacts.",
                    "Red is my color, life is my meaning, power my price.",
                    "Sacrifice demands I flow, demons drink deep of me."
                }},
                { "chain", new[] {
                    "Link by link I bind, freedom's opposite.",
                    "Iron circles joined in servitude's cold embrace.",
                    "I am as strong as my weakest part, yet I hold titans."
                }},
                { "claw", new[] {
                    "Curved for rending, sharp for tearing, I end life messily.",
                    "The beast's final argument, I leave marks that scar.",
                    "No sword is faster when the predator strikes."
                }},

                // Frozen Depths
                { "snowflake", new[] {
                    "No two of us are alike, yet we all fall the same.",
                    "Hexagonal perfection, I melt at a touch.",
                    "Winter's delicate art, unique and fleeting."
                }},
                { "icicle", new[] {
                    "I grow downward in the cold, a dagger of frozen tears.",
                    "Drip by drip I form, then crash without warning.",
                    "The cold's slow sword, I hang and wait to fall."
                }},
                { "frost", new[] {
                    "I paint windows with patterns no artist could match.",
                    "The cold's first messenger, I arrive before snow.",
                    "Delicate and deadly, I claim the unprepared."
                }},
                { "wind", new[] {
                    "You cannot see me, only what I move.",
                    "I howl without mouth, push without hands.",
                    "Born from difference, I seek balance eternally."
                }},
                { "glacier", new[] {
                    "I am a river of ice that moves slower than stone erodes.",
                    "Mountains bow to my slow advance over ages.",
                    "Frozen giant, I carved the valleys you now walk."
                }},
                { "aurora", new[] {
                    "I dance in colors where the sky meets the pole.",
                    "Charged particles paint me across the polar night.",
                    "The north's mysterious curtain of shifting light."
                }},

                // Volcanic Pit
                { "fire", new[] {
                    "I am never satisfied, feed me and I only want more.",
                    "Prometheus stole me, humanity was changed forever.",
                    "Warmth, light, destruction - I am all three."
                }},
                { "lava", new[] {
                    "I am the earth's blood, molten and merciless.",
                    "Stone flows like water when I am present.",
                    "Touch me and become ash, flee me or join the earth."
                }},
                { "ash", new[] {
                    "I am what remains when burning has finished its meal.",
                    "Gray and light, I drift on wind, once I was something more.",
                    "The aftermath of destruction, fertile yet desolate."
                }},
                { "smoke", new[] {
                    "I rise from destruction, visible breath of burning.",
                    "I choke and blind, warning of worse to come.",
                    "Where I billow, hungry flames recently danced."
                }},
                { "ember", new[] {
                    "I am the patient heart of burning, waiting for fuel.",
                    "Red glow in ash, I am not yet defeated.",
                    "Stir me and I awaken, neglect me and I sleep forever."
                }},
                { "obsidian", new[] {
                    "I am flowing earth frozen in an instant, dark as void.",
                    "Volcanic glass, I hold edges sharper than steel.",
                    "I was molten once, now I am stone's dark mirror."
                }},

                // Abyssal Void
                { "void", new[] {
                    "I am the absence that contains everything.",
                    "Not darkness, for darkness is something - I am nothing.",
                    "Look into me and see what existence fears most."
                }},
                { "spiral", new[] {
                    "I turn inward forever, there is no bottom to reach.",
                    "Follow me and lose yourself in endless descent.",
                    "Madness takes this shape when geometry fails."
                }},
                { "tear", new[] {
                    "I am a wound in what should be whole.",
                    "Through me, things that should not meet do.",
                    "Reality weeps through my ragged opening."
                }},
                { "wave", new[] {
                    "I pulse through emptiness, carrying meaning in nothing.",
                    "Neither up nor down, I oscillate eternal.",
                    "All energy moves as I do, peak and trough forever."
                }},
                { "infinity", new[] {
                    "Follow me and never arrive, for I have no end.",
                    "Count to me - you cannot, I am beyond number.",
                    "A twisted loop, I represent what cannot be grasped."
                }},

                // Default geometric
                { "circle", new[] {
                    "I have no beginning and no end, perfect and eternal.",
                    "Every point on me is the same distance from my heart.",
                    "Roll me and I go on forever, the wheel's ancestor."
                }},
                { "square", new[] {
                    "Four equal sides, four equal angles, order incarnate.",
                    "I am stability, foundation, the shape of certainty.",
                    "Rotate me four times and I look the same."
                }},
                { "triangle", new[] {
                    "Three points make me, the simplest strength.",
                    "I cannot be collapsed, I am geometry's backbone.",
                    "Point up I aspire, point down I anchor."
                }},
                { "diamond", new[] {
                    "A square turned on its corner, I am balance on edge.",
                    "Four sides, four points, but I stand differently.",
                    "Cards bear me in red, I am precious and proud."
                }}
            };

            // Get riddle for the symbol
            if (riddles.TryGetValue(symbol, out var symbolRiddles))
            {
                return symbolRiddles[random.Next(symbolRiddles.Length)];
            }

            // Fallback - give first and last letter as hint
            return $"I begin with '{symbol[0]}' and end with '{symbol[symbol.Length-1]}'... what am I?";
        }

        private string GenerateLightHint(List<string> solution)
        {
            int litCount = solution.Count(s => s == "lit");
            return $"Exactly {litCount} torches must burn.";
        }

        private (string item1, string item2, string result) GetItemCombination(DungeonTheme theme, int difficulty)
        {
            var combinations = new List<(string, string, string)>
            {
                ("water", "fire_salt", "steam"),
                ("bone_dust", "blood", "awakening_paste"),
                ("crystal_shard", "moonlight", "glowing_crystal"),
                ("sulfur", "charcoal", "flash_powder"),
                ("silver_dust", "holy_water", "blessed_silver"),
                ("shadow_essence", "light_fragment", "twilight_orb"),
                ("dragon_scale", "phoenix_ash", "eternal_flame"),
                ("void_shard", "soul_fragment", "null_essence")
            };

            int maxIndex = Math.Min(combinations.Count, 2 + difficulty);
            return combinations[random.Next(maxIndex)];
        }

        private List<string> GenerateItemChoices(string item1, string item2, int difficulty)
        {
            var choices = new List<string> { item1, item2 };
            var redHerrings = new[] { "moss", "stone", "feather", "iron_dust", "spider_silk",
                                       "mushroom_cap", "rat_tail", "candle_wax" };

            for (int i = 0; i < 2 + difficulty; i++)
            {
                var herring = redHerrings[random.Next(redHerrings.Length)];
                if (!choices.Contains(herring))
                    choices.Add(herring);
            }

            return choices.OrderBy(_ => random.Next()).ToList();
        }

        private (string desc, List<string> solution, string hint) GetEnvironmentPuzzle(DungeonTheme theme, int difficulty)
        {
            return theme switch
            {
                DungeonTheme.Caverns => (
                    "Water flows from multiple channels into a central basin. " +
                    "Three gates control the flow: LEFT, CENTER, and RIGHT. Open them in the correct order.",
                    new List<string> { "left", "center", "right" },
                    "Ancient carvings instruct:\n" +
                    "  \"First, open the gate where the sun sets (LEFT).\n" +
                    "  \"Then, the heart of the room (CENTER).\n" +
                    "  \"Finally, where the sun rises (RIGHT).\""
                ),
                DungeonTheme.VolcanicPit => (
                    "Three lava streams block the path, numbered 1, 2, and 3 from left to right. " +
                    "Cool them in the correct order to create safe passage.",
                    new List<string> { "2", "1", "3" },
                    "A dying explorer's note reads:\n" +
                    "  \"The middle stream (2) must cool first - it feeds the others.\n" +
                    "  \"Then the leftmost (1), lest the center reheat.\n" +
                    "  \"The rightmost (3) last, or all is lost.\""
                ),
                DungeonTheme.FrozenDepths => (
                    "Ice blocks the exit. You can apply heat to three areas: TORCH the ice directly, " +
                    "heat the WALL behind it, or warm the FLOOR beneath it.",
                    new List<string> { "torch", "wall", "floor" },
                    "Frost runes spell out the method:\n" +
                    "  \"Direct flame (TORCH) loosens the grip.\n" +
                    "  \"Warm stone (WALL) weakens the bond.\n" +
                    "  \"Heated ground (FLOOR) completes the thaw.\""
                ),
                _ => (
                    "Three ancient switches stand before you, numbered 1, 2, and 3. " +
                    "Activate them in the correct sequence.",
                    new List<string> { "1", "2", "3" },
                    "Worn inscriptions reveal the order:\n" +
                    "  \"Begin with the first (1).\n" +
                    "  \"Continue to the second (2).\n" +
                    "  \"End with the third (3).\""
                )
            };
        }

        private string GetLeverPuzzleTitle(DungeonTheme theme)
        {
            return theme switch
            {
                DungeonTheme.Catacombs => "The Bone Levers",
                DungeonTheme.AncientRuins => "Mechanism of the Ancients",
                DungeonTheme.DemonLair => "Chains of Torment",
                _ => "The Lever Sequence"
            };
        }

        private string GetOrdinal(int number)
        {
            return number switch
            {
                1 => "first",
                2 => "second",
                3 => "third",
                4 => "fourth",
                5 => "fifth",
                6 => "sixth",
                7 => "seventh",
                _ => $"{number}th"
            };
        }

        #endregion
    }

    #region Puzzle Data Classes

    public class PuzzleInstance
    {
        public PuzzleType Type { get; set; }
        public int Difficulty { get; set; }
        public DungeonTheme Theme { get; set; }
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public List<string> Solution { get; set; } = new();
        public List<string> CurrentState { get; set; } = new();
        public List<string> AvailableChoices { get; set; } = new();
        public int MaxAttempts { get; set; }
        public int AttemptsRemaining { get; set; }
        public List<string> Hints { get; set; } = new();
        public int HintsUsed { get; set; } = 0;
        public int FailureDamagePercent { get; set; }
        public int SuccessXP { get; set; }
        public bool RequiresMovement { get; set; }
        public bool RequiresSequence { get; set; }
        public bool ShowSolutionFirst { get; set; }
        public bool RequiresEnvironmentInteraction { get; set; }
        public Dictionary<string, object> CustomData { get; set; } = new();

        // Additional properties for number puzzles
        public int TargetNumber { get; set; }
        public List<int> AvailableNumbers { get; set; } = new();
    }

    public class PuzzleResult
    {
        public bool Solved { get; set; }
        public bool Fled { get; set; }
        public int Attempts { get; set; }
        public int XPEarned { get; set; }
        public int DamageTaken { get; set; }
    }

    public class PuzzleInputResult
    {
        public PuzzleAction Action { get; set; }
        public string Input { get; set; } = "";
    }

    public enum PuzzleAction
    {
        Attempt,
        Hint,
        Quit
    }

    #endregion
}
