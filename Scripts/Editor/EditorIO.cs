using System;
using System.Collections.Generic;
using System.Linq;
using Spectre.Console;

namespace UsurperRemake.Editor;

/// <summary>
/// Editor I/O helpers powered by <see cref="Spectre.Console"/>. Handles the
/// arrow-key navigation, proper prompt echo across every Windows / macOS /
/// Linux terminal (cmd.exe, Windows Terminal, mintty / Git Bash, iTerm,
/// regular xterm), and table / panel rendering. Abstracts the TUI library
/// so sub-editors call helpers like <see cref="Menu"/>, <see cref="PromptString"/>,
/// and <see cref="Confirm"/> without knowing anything about Spectre themselves.
///
/// Design notes:
///   - Menus use <see cref="SelectionPrompt{T}"/> — user navigates with arrow
///     keys and presses Enter to pick. No more "type a number and hit Enter".
///   - Text input uses <see cref="TextPrompt{T}"/>, which correctly handles the
///     Enter echo on every terminal type (including Git Bash / mintty where
///     plain Console.ReadLine does not echo back to stdout).
///   - Enum prompts present a selection list rather than a free-text "type it"
///     prompt, so invalid values are impossible by construction.
///   - Confirm uses <see cref="ConfirmationPrompt"/> for a clean y/N UI.
///
/// Visual style:
///   - Header: thick-ruled panel with cyan border.
///   - Section: italic blue separator.
///   - Info / Warn / Error / Success: color-coded single lines.
/// </summary>
internal static class EditorIO
{
    public static void Header(string title)
    {
        AnsiConsole.WriteLine();
        var panel = new Panel($"[bold cyan]{Markup.Escape(title)}[/]")
        {
            Border = BoxBorder.Double,
            BorderStyle = new Style(Color.Cyan1),
            Padding = new Padding(2, 0, 2, 0),
        };
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    public static void Section(string title)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule($"[yellow]{Markup.Escape(title)}[/]").LeftJustified());
    }

    public static void Info(string text)    => AnsiConsole.MarkupLine($"  {Markup.Escape(text)}");
    public static void Warn(string text)    => AnsiConsole.MarkupLine($"  [yellow]![/] {Markup.Escape(text)}");
    public static void Error(string text)   => AnsiConsole.MarkupLine($"  [red]ERROR[/] {Markup.Escape(text)}");
    public static void Success(string text) => AnsiConsole.MarkupLine($"  [green]OK[/] {Markup.Escape(text)}");

    public static string Prompt(string label)
    {
        return AnsiConsole.Prompt(
            new TextPrompt<string>($"  {Markup.Escape(label)}:")
                .AllowEmpty())
            .Trim();
    }

    /// <summary>Prompt for a string, returning <paramref name="current"/> unchanged when blank.</summary>
    public static string PromptString(string label, string current)
    {
        var result = AnsiConsole.Prompt(
            new TextPrompt<string>($"  {Markup.Escape(label)} [[[grey]{Markup.Escape(current)}[/]]]:")
                .AllowEmpty()
                .DefaultValue(current)
                .ShowDefaultValue(false));
        return string.IsNullOrWhiteSpace(result) ? current : result.Trim();
    }

    /// <summary>Prompt for an int, returning <paramref name="current"/> on blank or parse failure.</summary>
    public static int PromptInt(string label, int current, int? min = null, int? max = null)
    {
        var prompt = new TextPrompt<int>($"  {Markup.Escape(label)} [[[grey]{current}[/]]]:")
            .DefaultValue(current)
            .ShowDefaultValue(false);
        prompt.Validate(v =>
        {
            if (min.HasValue && v < min.Value) return ValidationResult.Error($"min {min.Value}");
            if (max.HasValue && v > max.Value) return ValidationResult.Error($"max {max.Value}");
            return ValidationResult.Success();
        });
        return AnsiConsole.Prompt(prompt);
    }

    /// <summary>Prompt for a long. Same semantics as <see cref="PromptInt"/>.</summary>
    public static long PromptLong(string label, long current, long? min = null, long? max = null)
    {
        var prompt = new TextPrompt<long>($"  {Markup.Escape(label)} [[[grey]{current}[/]]]:")
            .DefaultValue(current)
            .ShowDefaultValue(false);
        prompt.Validate(v =>
        {
            if (min.HasValue && v < min.Value) return ValidationResult.Error($"min {min.Value}");
            if (max.HasValue && v > max.Value) return ValidationResult.Error($"max {max.Value}");
            return ValidationResult.Success();
        });
        return AnsiConsole.Prompt(prompt);
    }

    public static bool PromptBool(string label, bool current)
    {
        return AnsiConsole.Prompt(
            new ConfirmationPrompt($"  {Markup.Escape(label)}")
            {
                DefaultValue = current,
            });
    }

    /// <summary>
    /// Present a numbered menu. Returns 1-based selected index, or 0 for Back.
    /// Supports BOTH arrow-key navigation AND number shortcuts (<c>1</c>-<c>9</c>
    /// picks that row immediately, <c>0</c>/<c>Q</c>/<c>Esc</c> backs out). Falls
    /// back to a plain number-based prompt when stdin is a pipe (Git Bash etc.)
    /// where <see cref="Console.ReadKey"/> can't do single-key reads.
    /// </summary>
    public static int Menu(string title, IList<string> options)
    {
        int idx = RunArrowMenu(title, options);
        return idx < 0 ? 0 : idx + 1;
    }

    /// <summary>
    /// Core interactive menu. Returns the 0-based index of the selected entry,
    /// or <c>-1</c> if the user backed out (<c>0</c>/<c>Q</c>/<c>Esc</c>).
    /// All other pickers (<see cref="PromptChoice"/>, <see cref="PromptEnum"/>)
    /// delegate here so they all share the same keyboard UX.
    ///
    /// Rendering strategy: keep a running count of lines we emitted last render,
    /// and on redraw use the RELATIVE ANSI "cursor up N" sequence (CSI nA) plus
    /// "erase display below" (CSI 0J). Relative movement is immune to the
    /// Windows-Terminal buffer-vs-viewport mismatch that broke absolute
    /// <see cref="Console.SetCursorPosition"/> tracking across menu resizes.
    ///
    /// v0.57.4: viewport-scrolled so long lists don't push the top rows off the
    /// terminal. Only <c>viewportSize</c> rows are rendered at a time; arrow
    /// keys move the window. Number shortcuts still work — they jump directly
    /// to the target index and the viewport follows.
    /// </summary>
    private static int RunArrowMenu(string title, IList<string> labels)
    {
        if (labels == null || labels.Count == 0) return -1;

        // Fallback path for piped stdin (Git Bash / mintty / test harnesses):
        // Console.ReadKey can't do single-keystroke reads over a pipe, so we
        // render a static numbered list and read a line of input.
        if (Console.IsInputRedirected)
            return RunNumberMenuFallback(title, labels);

        int selected = 0;
        int prevLines = 0;
        // Running buffer for multi-digit number entry. When the user types a
        // digit that could still be a prefix of a larger valid selection
        // (e.g. typed "1" in a 17-item menu where 10-17 are also valid), we
        // buffer it and wait for the next keystroke. The buffer is shown live
        // on the title line so the user knows they're mid-entry.
        string numBuffer = "";

        while (true)
        {
            // Redraw in place: move up over whatever we rendered last time,
            // then clear everything from the cursor to the bottom of the
            // screen. \r is the canonical "return to column 0" on any
            // terminal. \x1b[0J erases from cursor to end of display.
            if (prevLines > 0)
            {
                // Batch the positioning into a single Console.Write so the
                // terminal processes it atomically — some emulators flicker if
                // cursor-move and clear arrive in separate flush boundaries.
                Console.Write($"\x1b[{prevLines}A\r\x1b[0J");
            }

            // Size the viewport against the terminal — leave room for title,
            // hint lines, overflow indicators, and a prompt line below. Fall
            // back to a safe default when WindowHeight is unavailable (some
            // redirected terminals return 0 / throw). Cap at labels.Count so
            // short menus don't emit spurious overflow markers.
            int terminalRows;
            try { terminalRows = Console.WindowHeight; } catch { terminalRows = 24; }
            if (terminalRows <= 0) terminalRows = 24;
            const int chromeLines = 7;  // blank + title + blank + 2 overflow + breathing room
            int viewportSize = Math.Max(5, Math.Min(labels.Count, terminalRows - chromeLines));

            // Center the selected row in the viewport when possible; clamp to
            // the ends otherwise so the list never renders off-array.
            int viewStart = Math.Max(0, Math.Min(
                labels.Count - viewportSize,
                selected - viewportSize / 2));
            int viewEnd = Math.Min(labels.Count, viewStart + viewportSize);

            string hint = numBuffer.Length > 0
                ? $"[yellow]typing: {numBuffer}_[/]  [grey](Enter to commit, Esc to clear)[/]"
                : $"[grey](arrows/PgUp/PgDn/Home/End, numbers, Enter picks, 0/Q/Esc back — showing {viewStart + 1}-{viewEnd}/{labels.Count})[/]";
            int lineCount = 0;
            AnsiConsole.MarkupLine(""); lineCount++;
            AnsiConsole.MarkupLine($"  [bold yellow]{Markup.Escape(title)}[/]  {hint}");
            lineCount++;
            AnsiConsole.MarkupLine(""); lineCount++;
            if (viewStart > 0)
            {
                AnsiConsole.MarkupLine($"  [grey]    ... {viewStart} more above (↑ / PgUp / Home)[/]");
                lineCount++;
            }
            for (int i = viewStart; i < viewEnd; i++)
            {
                bool isSelected = i == selected;
                string cursor = isSelected ? "[black on cyan]>[/]" : " ";
                // Show the row's 1-based number shortcut. Single-digit items
                // get the plain digit; items 10+ get the two-digit tag so the
                // user knows a 2-key combo reaches them.
                int num = i + 1;
                string numTag = $"[cyan]{num,3}[/]";
                string text = isSelected
                    ? $"[black on cyan] {Markup.Escape(labels[i])} [/]"
                    : Markup.Escape(labels[i]);
                AnsiConsole.MarkupLine($"  {cursor} {numTag}  {text}");
                lineCount++;
            }
            if (viewEnd < labels.Count)
            {
                AnsiConsole.MarkupLine($"  [grey]    ... {labels.Count - viewEnd} more below (↓ / PgDn / End)[/]");
                lineCount++;
            }
            prevLines = lineCount;

            ConsoleKeyInfo key;
            try { key = Console.ReadKey(intercept: true); }
            catch { return RunNumberMenuFallback(title, labels); }

            if (key.Key == ConsoleKey.UpArrow || key.Key == ConsoleKey.LeftArrow || key.KeyChar == 'k')
            {
                numBuffer = "";
                selected = (selected - 1 + labels.Count) % labels.Count;
            }
            else if (key.Key == ConsoleKey.DownArrow || key.Key == ConsoleKey.RightArrow || key.KeyChar == 'j')
            {
                numBuffer = "";
                selected = (selected + 1) % labels.Count;
            }
            else if (key.Key == ConsoleKey.Home)
            {
                numBuffer = "";
                selected = 0;
            }
            else if (key.Key == ConsoleKey.End)
            {
                numBuffer = "";
                selected = labels.Count - 1;
            }
            else if (key.Key == ConsoleKey.PageUp)
            {
                numBuffer = "";
                selected = Math.Max(0, selected - viewportSize);
            }
            else if (key.Key == ConsoleKey.PageDown)
            {
                numBuffer = "";
                selected = Math.Min(labels.Count - 1, selected + viewportSize);
            }
            else if (key.Key == ConsoleKey.Enter)
            {
                // Enter commits either the buffered number (if any + valid)
                // or the arrow-highlighted row.
                if (numBuffer.Length > 0
                    && int.TryParse(numBuffer, out int bufN)
                    && bufN >= 1 && bufN <= labels.Count)
                {
                    return bufN - 1;
                }
                return selected;
            }
            else if (key.Key == ConsoleKey.Escape || key.KeyChar == 'q' || key.KeyChar == 'Q')
            {
                // Escape clears a partial numeric buffer first; only exits the
                // menu when the buffer was already empty.
                if (numBuffer.Length > 0) { numBuffer = ""; continue; }
                return -1;
            }
            else if (key.Key == ConsoleKey.Backspace)
            {
                if (numBuffer.Length > 0) numBuffer = numBuffer.Substring(0, numBuffer.Length - 1);
            }
            else if (char.IsDigit(key.KeyChar))
            {
                int digit = key.KeyChar - '0';
                // Bare "0" with an empty buffer means "Back" (preserves the
                // documented 0/Q/Esc behavior from the hint line).
                if (numBuffer.Length == 0 && digit == 0) return -1;

                string newBuffer = numBuffer + digit;
                if (!int.TryParse(newBuffer, out int n)) { numBuffer = ""; continue; }

                // If typing this digit produces a number larger than the menu,
                // treat it as a typo — drop the keystroke and keep the
                // previous buffer so the user can continue.
                if (n > labels.Count) { continue; }

                // Preview: highlight the selection the current buffer maps to.
                selected = n - 1;

                // Auto-commit when no larger valid number has this one as a
                // prefix. For a 17-item menu: "2"..."9" commit immediately
                // (no 20+ possible), "1" waits (10-17 still possible), "10"
                // commits (100+ impossible), etc.
                if (n * 10 > labels.Count)
                {
                    return n - 1;
                }
                numBuffer = newBuffer;
            }
        }
    }

    /// <summary>Line-based numeric picker for when we can't do single-key reads.</summary>
    private static int RunNumberMenuFallback(string title, IList<string> labels)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"  [bold yellow]{Markup.Escape(title)}[/]");
        AnsiConsole.WriteLine();
        for (int i = 0; i < labels.Count; i++)
            AnsiConsole.MarkupLine($"  [cyan]{i + 1,2}[/]  {Markup.Escape(labels[i])}");
        AnsiConsole.MarkupLine("  [grey] 0  Back[/]");
        AnsiConsole.WriteLine();
        while (true)
        {
            var input = AnsiConsole.Prompt(
                new TextPrompt<string>("  Choice:")
                    .AllowEmpty()
                    .DefaultValue(""))
                .Trim();
            if (string.IsNullOrEmpty(input) || input.Equals("q", StringComparison.OrdinalIgnoreCase))
                return -1;
            if (int.TryParse(input, out int n))
            {
                if (n == 0) return -1;
                if (n >= 1 && n <= labels.Count) return n - 1;
            }
            AnsiConsole.MarkupLine("  [red]Invalid choice.[/]");
        }
    }

    /// <summary>
    /// Pick a value from a known vocabulary. Arrow keys OR number keys.
    /// If <paramref name="allowCustom"/> is true, the user can pick
    /// "Custom..." to type a freeform value. If <paramref name="allowKeep"/>
    /// is true and <paramref name="current"/> is non-empty, a "(keep current)"
    /// option is presented first and returns <paramref name="current"/>.
    /// </summary>
    public static string PromptChoice(
        string label,
        IReadOnlyList<string> choices,
        string current,
        bool allowCustom = true,
        bool allowKeep = true)
    {
        // Build the list in display order with sentinels, then feed it to the
        // shared RunArrowMenu so keyboard behavior matches every other menu.
        var labels = new List<string>();
        int keepIdx = -1, customIdx = -1;
        if (allowKeep && !string.IsNullOrEmpty(current))
        {
            keepIdx = 0;
            labels.Add($"(keep current: {current})");
        }
        foreach (var c in choices) labels.Add(c);
        if (allowCustom)
        {
            customIdx = labels.Count;
            labels.Add("Custom...");
        }

        string title = string.IsNullOrEmpty(current)
            ? label
            : $"{label}  (current: {current})";
        int picked = RunArrowMenu(title, labels);
        if (picked < 0 || picked == keepIdx) return current;
        if (picked == customIdx)
        {
            // Fall back to a text prompt for the freeform value.
            var custom = AnsiConsole.Prompt(
                new TextPrompt<string>($"  [bold]{Markup.Escape(label)}[/] custom value:")
                    .AllowEmpty()
                    .DefaultValue(current ?? ""));
            return string.IsNullOrWhiteSpace(custom) ? current : custom.Trim();
        }
        return labels[picked];
    }

    /// <summary>Yes/no confirmation with a default of No.</summary>
    public static bool Confirm(string question)
    {
        return AnsiConsole.Prompt(
            new ConfirmationPrompt($"  {Markup.Escape(question)}")
            {
                DefaultValue = false,
            });
    }

    public static void Pause(string msg = "Press Enter to continue")
    {
        AnsiConsole.WriteLine();
        // Spectre's TextPrompt<string>.AllowEmpty() correctly echoes Enter on
        // every terminal — including mintty / Git Bash — so we don't have to
        // guess at IsInputRedirected behavior like plain Console.ReadLine does.
        AnsiConsole.Prompt(
            new TextPrompt<string>($"  [grey]{Markup.Escape(msg)}...[/]")
                .AllowEmpty()
                .DefaultValue(""));
    }

    /// <summary>
    /// Prompt for an enum value via arrow keys or number shortcuts. Invalid
    /// values are impossible by construction. Returns <paramref name="current"/>
    /// if the user presses Escape / picks nothing.
    /// </summary>
    public static TEnum PromptEnum<TEnum>(string label, TEnum current) where TEnum : struct, Enum
    {
        var names = Enum.GetNames<TEnum>();
        string title = $"{label}  (current: {current})";
        int picked = RunArrowMenu(title, names);
        if (picked < 0) return current;
        if (Enum.TryParse<TEnum>(names[picked], ignoreCase: true, out var parsed)) return parsed;
        return current;
    }
}
