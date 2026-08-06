using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Xunit;
using UsurperRemake.Systems;

namespace UsurperRemake.Tests
{
    /// <summary>
    /// Player report (Lv.26, Floor 23): "5-lever ancient puzzle is either
    /// unsolvable or I have literal brain damage. Maybe the syntax is odd?"
    ///
    /// These tests reconstruct the intended answer purely from what the game
    /// prints to the player, then feed it through the same parsing the live
    /// handler uses. If a player who reads every hint correctly still cannot
    /// solve it, that is a bug in the puzzle, not the player.
    /// </summary>
    public class LeverPuzzleSolvabilityTests
    {
        static LeverPuzzleSolvabilityTests() => Loc.Initialize();

        /// <summary>Floor 23 produces difficulty 2, which is a 5-lever puzzle.</summary>
        private static int DifficultyForFloor(int floor) => Math.Min(5, 1 + (floor / 15));

        /// <summary>
        /// Maps a rendered riddle line back to the number it points at, using the
        /// same loc keys the generator drew it from.
        /// </summary>
        private static int? RiddleToNumber(string hintLine)
        {
            for (int n = 1; n <= 8; n++)
            {
                for (int v = 0; v < 3; v++)
                {
                    string key = $"puzzle.num_riddle.{n}.{v}";
                    string text = Loc.Get(key);
                    if (text != key && hintLine.Contains(text, StringComparison.Ordinal))
                        return n;
                }
                string fb = Loc.Get("puzzle.num_riddle_fallback", n);
                if (hintLine.Contains(fb, StringComparison.Ordinal)) return n;
            }
            return null;
        }

        /// <summary>Mirrors DungeonLocation.HandleLeverPuzzle's parsing exactly.</summary>
        private static bool HandlerAccepts(PuzzleInstance puzzle, string input)
        {
            int leverCount = puzzle.Solution.Count;
            var parts = input.Split(',', ' ')
                             .Select(s => s.Trim())
                             .Where(s => !string.IsNullOrEmpty(s))
                             .ToList();

            if (parts.Count != leverCount) return false;
            for (int i = 0; i < leverCount; i++)
            {
                if (!int.TryParse(parts[i], out int lever)) return false;
                if (lever.ToString() != puzzle.Solution[i]) return false;
            }
            return true;
        }

        [Fact]
        public void FloorTwentyThreeProducesFiveLevers_MatchingTheReport()
        {
            int difficulty = DifficultyForFloor(23);
            var puzzle = PuzzleSystem.Instance.GeneratePuzzle(
                PuzzleType.LeverSequence, difficulty, DungeonTheme.Catacombs);

            puzzle.Solution.Should().HaveCount(5, "the report described a 5-lever puzzle on floor 23");
        }

        [Fact]
        public void APlayerWhoReadsEveryHintCanAlwaysSolveIt()
        {
            // 200 generations so a rare bad shuffle or riddle collision surfaces.
            for (int iter = 0; iter < 200; iter++)
            {
                var puzzle = PuzzleSystem.Instance.GeneratePuzzle(
                    PuzzleType.LeverSequence, 2, DungeonTheme.Catacombs);

                // Hints are: header, blank, then one line per position in order.
                var riddleLines = puzzle.Hints
                    .Where(h => !string.IsNullOrWhiteSpace(h))
                    .Skip(1)   // header
                    .ToList();

                riddleLines.Should().HaveCount(puzzle.Solution.Count,
                    "every lever position must get exactly one hint line");

                var deduced = riddleLines.Select(RiddleToNumber).ToList();
                deduced.Should().NotContain((int?)null,
                    "every hint must resolve to a number, or the player cannot deduce it");

                string answer = string.Join(",", deduced.Select(d => d!.Value));
                HandlerAccepts(puzzle, answer).Should().BeTrue(
                    $"iteration {iter}: hints said {answer} but the solution was " +
                    $"{string.Join(",", puzzle.Solution)}");
            }
        }

        [Fact]
        public void EveryLeverAppearsExactlyOnce()
        {
            for (int iter = 0; iter < 100; iter++)
            {
                var puzzle = PuzzleSystem.Instance.GeneratePuzzle(
                    PuzzleType.LeverSequence, 2, DungeonTheme.Catacombs);

                var nums = puzzle.Solution.Select(int.Parse).OrderBy(n => n).ToList();
                nums.Should().Equal(Enumerable.Range(1, puzzle.Solution.Count),
                    "the solution must be a permutation of 1..N with no repeats or gaps");
            }
        }

        [Fact]
        public void TheWorkedExampleMatchesTheActualLeverCount()
        {
            // The prompt reads "There are {0} levers. Enter the sequence (e.g., 1,2,3):".
            // The example is a fixed 3 numbers no matter how many levers there are,
            // while the handler rejects anything that is not exactly N entries. A
            // player copying the demonstrated format on a 5-lever puzzle is silently
            // rejected with no explanation of why.
            var puzzle = PuzzleSystem.Instance.GeneratePuzzle(
                PuzzleType.LeverSequence, 2, DungeonTheme.Catacombs);
            int leverCount = puzzle.Solution.Count;

            // Mirrors DungeonLocation.BuildSequenceExample.
            string example = string.Join(",", Enumerable.Range(1, leverCount));
            string prompt = Loc.Get("dungeon.lever_puzzle", leverCount, example);

            prompt.Should().Contain(example,
                "the prompt must demonstrate a sequence of the right length");
            prompt.Should().NotContain("{1}", "the example argument must actually be supplied");

            // And the demonstrated format must be one the handler accepts.
            var demo = prompt.Substring(prompt.IndexOf(example, StringComparison.Ordinal), example.Length);
            var parts = demo.Split(',').Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            parts.Should().HaveCount(leverCount,
                "a player copying the demonstrated format must not be rejected on length");
        }

        [Fact]
        public void PressurePlatesPromptScalesItsExampleToo()
        {
            // Same defect, same shape: the plates example was a fixed four numbers.
            for (int difficulty = 1; difficulty <= 5; difficulty++)
            {
                var puzzle = PuzzleSystem.Instance.GeneratePuzzle(
                    PuzzleType.PressurePlates, difficulty, DungeonTheme.Catacombs);
                int count = puzzle.Solution.Count;

                string example = string.Join(",", Enumerable.Range(1, count));
                string prompt = Loc.Get("dungeon.pressure_plates_prompt", count, example);

                prompt.Should().Contain(example, $"difficulty {difficulty} needs a {count}-entry example");
                prompt.Should().NotContain("{1}");
            }
        }

        [Fact]
        public void EveryLanguageDemonstratesAScaledExample()
        {
            // The hardcoded example was baked into all five translations, so a
            // Spanish or Hungarian player hit the identical trap.
            foreach (var lang in new[] { "en", "es", "fr", "it", "hu" })
            {
                foreach (var key in new[] { "dungeon.lever_puzzle", "dungeon.pressure_plates_prompt" })
                {
                    string tmpl = Loc.GetIn(lang, key, 5, "1,2,3,4,5");
                    tmpl.Should().Contain("1,2,3,4,5",
                        $"{lang}/{key} must render the supplied example, not a baked-in one");
                    tmpl.Should().NotContain("{0}", $"{lang}/{key} left an unsubstituted arg");
                    tmpl.Should().NotContain("{1}", $"{lang}/{key} left an unsubstituted arg");
                }
            }
        }

        [Theory]
        [InlineData("1,2,3,4,5")]
        [InlineData("1 2 3 4 5")]
        [InlineData("1, 2, 3, 4, 5")]
        [InlineData(" 1,2,3,4,5 ")]
        public void CommonInputFormatsAreAllAccepted(string format)
        {
            var puzzle = PuzzleSystem.Instance.GeneratePuzzle(
                PuzzleType.LeverSequence, 2, DungeonTheme.Catacombs);

            // Rewrite the format's separators around the real answer.
            string answer = string.Join(
                format.Contains(',') ? (format.Contains(", ") ? ", " : ",") : " ",
                puzzle.Solution);
            if (format.StartsWith(" ")) answer = " " + answer + " ";

            HandlerAccepts(puzzle, answer).Should().BeTrue(
                $"'{format}' is a format the prompt invites, so it must be accepted");
        }
    }
}
