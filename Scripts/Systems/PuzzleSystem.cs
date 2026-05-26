using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UsurperRemake.UI;
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

        private Random random = Random.Shared;

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
                Description = Loc.Get("puzzle.lever.desc", leverCount),
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
            hints.Add(Loc.Get("puzzle.lever.hint_header"));
            hints.Add("");

            for (int i = 0; i < solution.Count; i++)
            {
                int leverNum = solution[i] + 1; // Convert to 1-indexed
                string hint = GetLeverHint(leverNum, i, solution.Count);
                string ordinal = i < 8 ? Loc.Get($"puzzle.ordinal.{i + 1}") : $"#{i + 1}";
                hints.Add(Loc.Get("puzzle.lever.hint_line", ordinal, hint));
            }

            return hints;
        }

        private string GetLeverHint(int leverNum, int position, int total)
        {
            // Number-riddles in loc keys puzzle.num_riddle.{n}.0..2 (n=1..8). Each localized riddle
            // must still point to the same number. Fallback states the number plainly for n>8.
            const int variants = 3;
            string key = $"puzzle.num_riddle.{leverNum}.{random.Next(variants)}";
            string v = Loc.Get(key);
            if (v != key) return v;
            return Loc.Get("puzzle.num_riddle_fallback", leverNum);
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
                Description = Loc.Get("puzzle.symbol.desc", panelCount),
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
                Description = Loc.Get("puzzle.pressure.desc", plateCount),
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
            hints.Add(Loc.Get("puzzle.pressure.hint_header"));
            hints.Add("");

            for (int i = 0; i < solution.Count; i++)
            {
                int plateNum = solution[i] + 1; // Which plate (1-indexed)
                int stepOrder = i; // When in sequence (0-indexed)

                string wearKey;
                if (stepOrder == 0) wearKey = "puzzle.wear.first";
                else if (stepOrder == solution.Count - 1) wearKey = "puzzle.wear.last";
                else if (stepOrder == 1) wearKey = "puzzle.wear.second";
                else if (stepOrder == solution.Count - 2) wearKey = "puzzle.wear.near_end";
                else wearKey = "puzzle.wear.middle";

                hints.Add(Loc.Get("puzzle.pressure.hint_line", plateNum, Loc.Get(wearKey)));
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
                Description = Loc.Get("puzzle.number.desc", target),
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
                Description = Loc.Get("puzzle.memory.desc"),
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
                Description = Loc.Get("puzzle.light.desc", torchCount),
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

            hints.Add(Loc.Get("puzzle.light.hint_header"));
            hints.Add("");

            for (int i = 0; i < solution.Count; i++)
            {
                bool shouldBeLit = solution[i] == "lit";
                string hint = GetTorchRiddle(i + 1, shouldBeLit);
                hints.Add(Loc.Get("puzzle.light.hint_line", i + 1, hint));
            }

            hints.Add("");
            hints.Add(Loc.Get("puzzle.light.balance", litCount));

            return hints;
        }

        private string GetTorchRiddle(int torchNum, bool shouldBeLit)
        {
            // 6 lit + 6 unlit flavor variants in loc keys puzzle.torch_lit.0..5 / puzzle.torch_unlit.0..5.
            string prefix = shouldBeLit ? "puzzle.torch_lit" : "puzzle.torch_unlit";
            return Loc.Get($"{prefix}.{random.Next(6)}");
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
                Description = Loc.Get("puzzle.alchemy.desc"),
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

            // Result/item flavor descriptions in loc keys puzzle.alch_result.{key} / puzzle.alch_item.{key}.
            // Items/results themselves are internal ids; fall back to a readable form if no key exists.
            string LocOr(string key, string fallback) { var v = Loc.Get(key); return v == key ? fallback : v; }
            string resultDesc = LocOr($"puzzle.alch_result.{result}", Loc.Get("puzzle.alch_result_fallback", result));
            string item1Desc = LocOr($"puzzle.alch_item.{item1}", item1.Replace("_", " "));
            string item2Desc = LocOr($"puzzle.alch_item.{item2}", item2.Replace("_", " "));

            hints.Add(Loc.Get("puzzle.alchemy.hint_header"));
            hints.Add("");
            hints.Add(Loc.Get("puzzle.alchemy.line1", resultDesc));
            hints.Add(Loc.Get("puzzle.alchemy.line2", item1Desc));
            hints.Add(Loc.Get("puzzle.alchemy.line3", item2Desc));

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
                Description = Loc.Get("puzzle.mirror.desc", mirrorCount),
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
            hints.Add(Loc.Get("puzzle.mirror.hint_header"));
            hints.Add("");

            for (int i = 0; i < solution.Count; i++)
            {
                string angle = solution[i];
                // 3 flavor variants per angle in puzzle.angle.{0|45|90|135}.0..2
                string desc = Loc.Get($"puzzle.angle.{angle}.{random.Next(3)}");
                hints.Add(Loc.Get("puzzle.mirror.hint_line", i + 1, desc, angle));
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
                    terminal.WriteLine(Loc.Get("puzzle.hint_or_quit"), "dark_cyan");
                }

                // Get player input based on puzzle type
                var result = await GetPuzzleInput(puzzle, terminal);

                if (result.Action == PuzzleAction.Quit)
                {
                    terminal.WriteLine(Loc.Get("puzzle.step_back"), "yellow");
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
                1 => Loc.Get("puzzle.diff_simple"),
                2 => Loc.Get("puzzle.diff_moderate"),
                3 => Loc.Get("puzzle.diff_challenging"),
                4 => Loc.Get("puzzle.diff_difficult"),
                _ => Loc.Get("puzzle.diff_legendary")
            };

            if (!GameConfig.ScreenReaderMode)
            {
                terminal.WriteLine("╔══════════════════════════════════════════════════════════════════╗", "bright_cyan");
                terminal.WriteLine($"║  {puzzle.Title.PadRight(62)}║", "bright_cyan");
                terminal.WriteLine($"║  {Loc.Get("puzzle.difficulty_label", diffText).PadRight(62)}║", "cyan");
                terminal.WriteLine("╚══════════════════════════════════════════════════════════════════╝", "bright_cyan");
            }
            else
            {
                terminal.WriteLine(puzzle.Title, "bright_cyan");
                terminal.WriteLine(Loc.Get("puzzle.difficulty_label", diffText), "cyan");
            }
            terminal.WriteLine("");
            terminal.WriteLine(puzzle.Description, "white");
            terminal.WriteLine("");
        }

        private void DisplayPuzzleState(PuzzleInstance puzzle, TerminalEmulator terminal)
        {
            terminal.WriteLine(Loc.Get("puzzle.attempts_remaining", puzzle.AttemptsRemaining),
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
            terminal.WriteLine(Loc.Get("puzzle.levers_label"), "white");
            for (int i = 0; i < leverCount; i++)
            {
                // CurrentState now stores 1-indexed lever numbers
                bool pulled = puzzle.CurrentState.Contains((i + 1).ToString());
                string status = pulled ? Loc.Get("puzzle.lever_pulled") : Loc.Get("puzzle.lever_empty");
                string color = pulled ? "green" : "gray";
                terminal.WriteLine($"    {i + 1}. {status}", color);
            }
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("puzzle.enter_lever", leverCount), "cyan");
        }

        private void DisplaySymbolState(PuzzleInstance puzzle, TerminalEmulator terminal)
        {
            terminal.WriteLine(Loc.Get("puzzle.current_alignment"), "white");
            for (int i = 0; i < puzzle.CurrentState.Count; i++)
            {
                terminal.WriteLine(Loc.Get("puzzle.panel_label", i + 1, puzzle.CurrentState[i]), "gray");
            }
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("puzzle.available_symbols", string.Join(", ", puzzle.AvailableChoices)), "cyan");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("puzzle.enter_symbol"), "cyan");
        }

        private void DisplayLightState(PuzzleInstance puzzle, TerminalEmulator terminal)
        {
            terminal.WriteLine(Loc.Get("puzzle.torches_label"), "white");
            for (int i = 0; i < puzzle.CurrentState.Count; i++)
            {
                bool lit = puzzle.CurrentState[i] == "lit";
                string display = lit ? Loc.Get("puzzle.torch_lit") : Loc.Get("puzzle.torch_unlit");
                string color = lit ? "bright_yellow" : "dark_gray";
                terminal.Write(Loc.Get("puzzle.torch_label", i + 1));
                terminal.WriteLine(display, color);
            }
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("puzzle.enter_torch", puzzle.CurrentState.Count), "cyan");
        }

        private void DisplayNumberState(PuzzleInstance puzzle, TerminalEmulator terminal)
        {
            terminal.WriteLine(Loc.Get("puzzle.available_numbers"), "white");
            terminal.WriteLine("    " + string.Join("  ", puzzle.AvailableChoices), "bright_cyan");
            terminal.WriteLine("");

            if (puzzle.CurrentState.Count > 0)
            {
                int sum = puzzle.CurrentState.Sum(s => int.Parse(s));
                terminal.WriteLine(Loc.Get("puzzle.selected", string.Join(" + ", puzzle.CurrentState), sum), "yellow");
            }

            int target = (int)puzzle.CustomData["target"];
            terminal.WriteLine(Loc.Get("puzzle.target_sum", target), "bright_green");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("puzzle.enter_number"), "cyan");
        }

        private void DisplayMemorySequence(PuzzleInstance puzzle, TerminalEmulator terminal)
        {
            terminal.WriteLine(Loc.Get("puzzle.watch_sequence"), "bright_yellow");
            terminal.WriteLine("");
            terminal.WriteLine("  " + string.Join(" -> ", puzzle.Solution), "bright_magenta");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("puzzle.sequence_hidden"), "gray");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("puzzle.press_enter_begin"), "cyan");
        }

        private void DisplayGenericState(PuzzleInstance puzzle, TerminalEmulator terminal)
        {
            if (puzzle.CurrentState.Count > 0)
            {
                terminal.WriteLine(Loc.Get("puzzle.current_state", string.Join(", ", puzzle.CurrentState)), "yellow");
            }
            if (puzzle.AvailableChoices.Count > 0)
            {
                terminal.WriteLine(Loc.Get("puzzle.options", string.Join(", ", puzzle.AvailableChoices)), "cyan");
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
                if (!GameConfig.ScreenReaderMode)
                    terminal.WriteLine("═══ HINT ═══", "bright_yellow");
                else
                    terminal.WriteLine(Loc.Get("puzzle.hint_header"), "bright_yellow");
                terminal.WriteLine(hint, "yellow");
                if (!GameConfig.ScreenReaderMode)
                    terminal.WriteLine("═════════════", "bright_yellow");
                terminal.WriteLine("");
            }
            else
            {
                terminal.WriteLine(Loc.Get("puzzle.no_hints"), "gray");
            }
        }

        private async Task DisplayPuzzleSuccess(PuzzleInstance puzzle, Character player, TerminalEmulator terminal)
        {
            terminal.WriteLine("");
            UIHelper.WriteBoxHeader(terminal, Loc.Get("puzzle.solved_header"), "bright_green", 66);
            terminal.WriteLine("");

            terminal.WriteLine(Loc.Get("puzzle.xp_gained", puzzle.SuccessXP), "cyan");
            player.Experience += puzzle.SuccessXP;

            // Ocean philosophy tie-in for certain puzzles
            if (puzzle.Difficulty >= 4)
            {
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("puzzle.whisper_echo"), "bright_magenta");
                terminal.WriteLine(Loc.Get("puzzle.wave_quote"), "magenta");
                OceanPhilosophySystem.Instance.CollectFragment(WaveFragment.TheForgetting);
            }

            await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
        }

        private async Task DisplayPuzzleFailure(PuzzleInstance puzzle, Character player, TerminalEmulator terminal)
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("puzzle.reset_sound"), "yellow");

            if (puzzle.FailureDamagePercent > 0 && puzzle.AttemptsRemaining == 0)
            {
                int damage = CalculateFailureDamage(puzzle, player);
                player.HP = Math.Max(1, player.HP - damage);
                terminal.WriteLine(Loc.Get("puzzle.trap_damage", damage), "red");
            }

            if (puzzle.AttemptsRemaining > 0)
            {
                terminal.WriteLine(Loc.Get("puzzle.attempts_left", puzzle.AttemptsRemaining), "yellow");
            }
            else
            {
                terminal.WriteLine(Loc.Get("puzzle.exhausted"), "dark_red");
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

            string flavorIntro = Loc.Get($"puzzle.sym_intro.{theme}");
            if (flavorIntro == $"puzzle.sym_intro.{theme}")
                flavorIntro = Loc.Get("puzzle.sym_intro.default");
            clues.Add(flavorIntro);
            clues.Add("");

            for (int i = 0; i < solution.Count; i++)
            {
                string symbol = solution[i];
                string riddle = GetRiddle(symbol);
                clues.Add(Loc.Get("puzzle.sym_clue_line", GetPositionNumeral(i + 1), riddle));
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
            // Riddles live in loc keys puzzle.sym_riddle.{symbol}.0..2 (3 variants per symbol), so they
            // render in the player's language. The symbol WORDS themselves stay canonical (they double
            // as the typed answer); each localized riddle must still clearly evoke its symbol concept.
            const int variants = 3;
            string key = $"puzzle.sym_riddle.{symbol}.{random.Next(variants)}";
            string v = Loc.Get(key);
            if (v != key) return v;
            // Fallback - first/last letter hint
            return Loc.Get("puzzle.sym_riddle_fallback", symbol[0], symbol[symbol.Length - 1]);
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
            // Description + multi-line hint localized per theme; the solution tokens (gate/stream/area
            // names and numbers) stay canonical because the player types them to solve.
            return theme switch
            {
                DungeonTheme.Caverns => (
                    Loc.Get("puzzle.env.caverns.desc"),
                    new List<string> { "left", "center", "right" },
                    Loc.Get("puzzle.env.caverns.hint")
                ),
                DungeonTheme.VolcanicPit => (
                    Loc.Get("puzzle.env.volcanic.desc"),
                    new List<string> { "2", "1", "3" },
                    Loc.Get("puzzle.env.volcanic.hint")
                ),
                DungeonTheme.FrozenDepths => (
                    Loc.Get("puzzle.env.frozen.desc"),
                    new List<string> { "torch", "wall", "floor" },
                    Loc.Get("puzzle.env.frozen.hint")
                ),
                _ => (
                    Loc.Get("puzzle.env.default.desc"),
                    new List<string> { "1", "2", "3" },
                    Loc.Get("puzzle.env.default.hint")
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
