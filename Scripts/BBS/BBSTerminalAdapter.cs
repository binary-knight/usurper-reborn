using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace UsurperRemake.BBS
{
    /// <summary>
    /// Terminal implementation for BBS door mode that can work with socket or console I/O.
    /// This class provides the same interface as TerminalEmulator but routes through SocketTerminal.
    ///
    /// Note: Since TerminalEmulator extends Godot.Control, we can't directly inherit from it.
    /// Instead, this class provides the same public API and sets itself as TerminalEmulator.Instance
    /// using duck typing through the static instance pattern.
    /// </summary>
    public class BBSTerminalAdapter
    {
        private readonly SocketTerminal? _socketTerminal;
        private readonly BBSSessionInfo _sessionInfo;
        private string _currentColor = "white";
        private bool _useAnsiForLocal = false; // True when using --stdio (redirected I/O)
        private bool _useWwivColors = false; // True when using WWIV color codes for Synchronet

        // Static instance for compatibility with code that uses TerminalEmulator.Instance
        public static BBSTerminalAdapter? Instance { get; private set; }

        public BBSTerminalAdapter(SocketTerminal socketTerminal, bool useAnsiForLocal = false, bool useWwivColors = false)
        {
            _socketTerminal = socketTerminal;
            _sessionInfo = socketTerminal.SessionInfo;
            _useAnsiForLocal = useAnsiForLocal;
            // WWIV colors are disabled by default - use standard ANSI which works everywhere
            _useWwivColors = useAnsiForLocal && useWwivColors;
            Instance = this;
        }

        // ANSI color codes for stdio mode
        private static readonly Dictionary<string, string> AnsiColorCodes = new()
        {
            { "black", "30" },
            { "red", "31" }, { "bright_red", "91" },
            { "green", "32" }, { "bright_green", "92" },
            { "yellow", "33" }, { "bright_yellow", "93" },
            { "blue", "34" }, { "bright_blue", "94" },
            { "magenta", "35" }, { "bright_magenta", "95" },
            { "cyan", "36" }, { "bright_cyan", "96" },
            { "white", "37" }, { "bright_white", "97" },
            { "gray", "90" }, { "grey", "90" },
            { "darkgray", "90" }, { "dark_gray", "90" },
            { "darkred", "31" }, { "dark_red", "31" },
            { "darkgreen", "32" }, { "dark_green", "32" },
            { "darkyellow", "33" }, { "dark_yellow", "33" }, { "brown", "33" },
            { "darkblue", "34" }, { "dark_blue", "34" },
            { "darkmagenta", "35" }, { "dark_magenta", "35" },
            { "darkcyan", "36" }, { "dark_cyan", "36" }
        };

        // WWIV heart codes: Ctrl-C (ASCII 3) + digit 0-7
        // These are the traditional WWIV color codes that Synchronet can translate
        // 0=Black, 1=Blue, 2=Green, 3=Cyan, 4=Red, 5=Magenta, 6=Brown/Yellow, 7=White/Gray
        // Note: WWIV only has 8 colors, so we map bright colors to their closest match
        private const char CTRL_C = '\x03'; // ASCII 3
        private static readonly Dictionary<string, string> WwivColorCodes = new()
        {
            { "black", $"{CTRL_C}0" },
            { "blue", $"{CTRL_C}1" }, { "dark_blue", $"{CTRL_C}1" }, { "darkblue", $"{CTRL_C}1" }, { "bright_blue", $"{CTRL_C}1" },
            { "green", $"{CTRL_C}2" }, { "dark_green", $"{CTRL_C}2" }, { "darkgreen", $"{CTRL_C}2" }, { "bright_green", $"{CTRL_C}2" },
            { "cyan", $"{CTRL_C}3" }, { "dark_cyan", $"{CTRL_C}3" }, { "darkcyan", $"{CTRL_C}3" }, { "bright_cyan", $"{CTRL_C}3" },
            { "red", $"{CTRL_C}4" }, { "dark_red", $"{CTRL_C}4" }, { "darkred", $"{CTRL_C}4" }, { "bright_red", $"{CTRL_C}4" },
            { "magenta", $"{CTRL_C}5" }, { "dark_magenta", $"{CTRL_C}5" }, { "darkmagenta", $"{CTRL_C}5" }, { "bright_magenta", $"{CTRL_C}5" },
            { "brown", $"{CTRL_C}6" }, { "dark_yellow", $"{CTRL_C}6" }, { "darkyellow", $"{CTRL_C}6" }, { "yellow", $"{CTRL_C}6" }, { "bright_yellow", $"{CTRL_C}6" },
            { "gray", $"{CTRL_C}7" }, { "grey", $"{CTRL_C}7" }, { "light_gray", $"{CTRL_C}7" }, { "dark_gray", $"{CTRL_C}7" }, { "darkgray", $"{CTRL_C}7" },
            { "white", $"{CTRL_C}7" }, { "bright_white", $"{CTRL_C}7" }
        };

        private string GetAnsiColorCode(string color)
        {
            var resolved = ColorTheme.Resolve(color);
            if (AnsiColorCodes.TryGetValue(resolved?.ToLower() ?? "white", out var code))
                return code;
            return "37"; // Default white
        }

        private string GetWwivColorCode(string color)
        {
            if (WwivColorCodes.TryGetValue(color?.ToLower() ?? "white", out var code))
                return code;
            return $"{CTRL_C}7"; // Default white/gray
        }

        public BBSSessionInfo SessionInfo => _sessionInfo;
        public bool IsConnected => _socketTerminal?.IsConnected ?? false;

        #region Output Methods

        public void WriteLine(string text, string color = "white")
        {
            if (_sessionInfo.CommType == ConnectionType.Local)
            {
                if (_useWwivColors)
                {
                    // Use WWIV/Synchronet pipe codes for Standard I/O mode
                    if (text.Contains("[") && text.Contains("[/]"))
                    {
                        WriteMarkupWithWwiv(text);
                        Console.WriteLine();
                    }
                    else
                    {
                        Console.Write(GetWwivColorCode(color));
                        Console.WriteLine(text);
                    }
                }
                else if (_useAnsiForLocal)
                {
                    // Use ANSI escape codes for stdio mode (redirected I/O)
                    if (text.Contains("[") && text.Contains("[/]"))
                    {
                        WriteMarkupWithAnsi(text);
                        Console.WriteLine();
                    }
                    else
                    {
                        Console.Write($"\x1b[{GetAnsiColorCode(color)}m");
                        Console.WriteLine(text);
                        Console.Write("\x1b[0m"); // Reset
                    }
                }
                else
                {
                    // Use console for true local mode
                    var oldColor = Console.ForegroundColor;
                    Console.ForegroundColor = ColorNameToConsole(color);

                    // Handle inline color markup
                    if (text.Contains("[") && text.Contains("[/]"))
                    {
                        WriteMarkupToConsole(text);
                        Console.WriteLine();
                    }
                    else
                    {
                        Console.WriteLine(text);
                    }
                    Console.ForegroundColor = oldColor;
                }
                return;
            }

            // Use socket terminal for remote connections
            if (_socketTerminal != null)
            {
                _socketTerminal.WriteLineAsync(text, color).GetAwaiter().GetResult();
            }
        }

        public void WriteLine()
        {
            WriteLine("", "white");
        }

        public void Write(string text, string color = "white")
        {
            if (_sessionInfo.CommType == ConnectionType.Local)
            {
                if (_useWwivColors)
                {
                    // Use WWIV/Synchronet pipe codes for Standard I/O mode
                    if (text.Contains("[") && text.Contains("[/]"))
                    {
                        WriteMarkupWithWwiv(text);
                    }
                    else
                    {
                        Console.Write(GetWwivColorCode(color));
                        Console.Write(text);
                    }
                }
                else if (_useAnsiForLocal)
                {
                    // Use ANSI escape codes for stdio mode
                    if (text.Contains("[") && text.Contains("[/]"))
                    {
                        WriteMarkupWithAnsi(text);
                    }
                    else
                    {
                        Console.Write($"\x1b[{GetAnsiColorCode(color)}m");
                        Console.Write(text);
                    }
                }
                else
                {
                    var oldColor = Console.ForegroundColor;
                    Console.ForegroundColor = ColorNameToConsole(color);

                    if (text.Contains("[") && text.Contains("[/]"))
                    {
                        WriteMarkupToConsole(text);
                    }
                    else
                    {
                        Console.Write(text);
                    }
                    Console.ForegroundColor = oldColor;
                }
                return;
            }

            // Use socket terminal for remote connections
            if (_socketTerminal != null)
            {
                _socketTerminal.WriteAsync(text, color).GetAwaiter().GetResult();
            }
        }

        public void Write(string text)
        {
            Write(text, _currentColor);
        }

        public void SetColor(string color)
        {
            _currentColor = color ?? "white";

            if (_sessionInfo.CommType == ConnectionType.Local)
            {
                if (_useWwivColors)
                {
                    Console.Write(GetWwivColorCode(_currentColor));
                }
                else if (_useAnsiForLocal)
                {
                    Console.Write($"\x1b[{GetAnsiColorCode(_currentColor)}m");
                }
                else
                {
                    Console.ForegroundColor = ColorNameToConsole(_currentColor);
                }
                return;
            }

            // Use socket terminal for remote connections
            if (_socketTerminal != null)
            {
                _socketTerminal.SetColorAsync(_currentColor).GetAwaiter().GetResult();
            }
        }

        public void ClearScreen()
        {
            if (_sessionInfo.CommType == ConnectionType.Local)
            {
                try
                {
                    Console.Clear();
                }
                catch (System.IO.IOException)
                {
                    // Console.Clear() fails when stdin/stdout are redirected (Standard I/O mode)
                    // Use ANSI escape codes instead
                    Console.Write("\x1b[2J\x1b[H");
                }
                return;
            }

            // Use socket terminal for remote connections
            if (_socketTerminal != null)
            {
                _socketTerminal.ClearScreenAsync().GetAwaiter().GetResult();
            }
        }

        #endregion

        #region Input Methods

        public async Task<string> GetInput(string prompt = "> ")
        {
            if (!string.IsNullOrEmpty(prompt))
            {
                Write(prompt, "bright_white");
            }

            if (_sessionInfo.CommType == ConnectionType.Local)
            {
                // Use robust backspace handling for all modes (PTY, redirected stdin, console)
                var result = TerminalEmulator.ReadLineWithBackspace();
                return result;
            }

            // Use socket terminal for remote connections
            if (_socketTerminal != null)
            {
                return await _socketTerminal.GetInputAsync("");
            }

            return "";
        }

        public string GetInputSync(string prompt = "> ") => GetInput(prompt).GetAwaiter().GetResult();

        public async Task<string> GetKeyInput()
        {
            if (_sessionInfo.CommType == ConnectionType.Local)
            {
                if (_useAnsiForLocal)
                {
                    // Console.ReadKey doesn't work with redirected I/O
                    // Read a single character from stdin instead
                    int ch = Console.Read();
                    if (ch == -1) return "";
                    return ((char)ch).ToString();
                }
                else
                {
                    var key = Console.ReadKey(true);
                    return key.KeyChar.ToString();
                }
            }

            // Use socket terminal for remote connections
            if (_socketTerminal != null)
            {
                return await _socketTerminal.GetKeyInputAsync("");
            }

            return "";
        }

        public async Task<int> GetMenuChoice(List<MenuOption> options)
        {
            WriteLine("");
            for (int i = 0; i < options.Count; i++)
            {
                var opt = options[i];
                SetColor("yellow");
                Write($"[{opt.Key}] ");
                SetColor(opt.Color ?? "white");
                WriteLine(opt.Text);
            }

            WriteLine("");

            while (true)
            {
                var input = await GetInput("> ");
                input = input.Trim().ToUpperInvariant();

                // Find matching option by key
                for (int i = 0; i < options.Count; i++)
                {
                    if (options[i].Key.ToUpperInvariant() == input)
                        return i;
                }

                // Try numeric input
                if (int.TryParse(input, out int num) && num >= 1 && num <= options.Count)
                    return num - 1;

                SetColor("red");
                WriteLine("Invalid choice. Please try again.");
            }
        }

        public async Task<bool> ConfirmAsync(string message, bool defaultValue = false)
        {
            string defaultHint = defaultValue ? " [Y/n]" : " [y/N]";
            SetColor("yellow");
            Write(message + defaultHint + " ");

            var input = await GetInput("");
            input = input.Trim().ToUpperInvariant();

            if (string.IsNullOrEmpty(input))
                return defaultValue;

            return input == "Y" || input == "YES";
        }

        public async Task<int> GetNumberInput(string prompt = "", int min = 0, int max = int.MaxValue)
        {
            while (true)
            {
                var input = await GetInput(prompt);

                if (int.TryParse(input.Trim(), out int num))
                {
                    if (num >= min && num <= max)
                        return num;

                    SetColor("red");
                    WriteLine($"Please enter a number between {min} and {max}.");
                }
                else
                {
                    SetColor("red");
                    WriteLine("Please enter a valid number.");
                }
            }
        }

        public async Task PressAnyKey(string message = "Press Enter to continue...")
        {
            SetColor("gray");
            WriteLine(message);
            await GetInput("");
        }

        public async Task WaitForKey(string message = "Press Enter to continue...")
        {
            await PressAnyKey(message);
        }

        #endregion

        #region Async Output Methods (for compatibility)

        public async Task WriteLineAsync(string text = "")
        {
            await _socketTerminal.WriteLineAsync(text);
        }

        public async Task WriteAsync(string text)
        {
            await _socketTerminal.WriteAsync(text);
        }

        public async Task ClearScreenAsync()
        {
            await _socketTerminal.ClearScreenAsync();
        }

        #endregion

        #region Markup and Color Helpers

        /// <summary>
        /// Write text with inline color markup to console
        /// </summary>
        private void WriteMarkupToConsole(string text)
        {
            var segments = ParseColorMarkup(text);
            var originalColor = Console.ForegroundColor;

            foreach (var (content, color) in segments)
            {
                if (!string.IsNullOrEmpty(color))
                    Console.ForegroundColor = ColorNameToConsole(color);
                else
                    Console.ForegroundColor = originalColor;

                Console.Write(content);
            }

            Console.ForegroundColor = originalColor;
        }

        /// <summary>
        /// Write text with inline color markup using ANSI escape codes (for stdio mode)
        /// </summary>
        private void WriteMarkupWithAnsi(string text)
        {
            var segments = ParseColorMarkup(text);

            foreach (var (content, color) in segments)
            {
                if (!string.IsNullOrEmpty(color))
                    Console.Write($"\x1b[{GetAnsiColorCode(color)}m");
                else
                    Console.Write("\x1b[0m"); // Reset

                Console.Write(content);
            }

            Console.Write("\x1b[0m"); // Reset at end
        }

        /// <summary>
        /// Write text with inline color markup using WWIV heart codes (for Synchronet Standard I/O)
        /// </summary>
        private void WriteMarkupWithWwiv(string text)
        {
            var segments = ParseColorMarkup(text);

            foreach (var (content, color) in segments)
            {
                if (!string.IsNullOrEmpty(color))
                    Console.Write(GetWwivColorCode(color));
                else
                    Console.Write($"{CTRL_C}7"); // Reset to white/gray (default)

                Console.Write(content);
            }
        }

        /// <summary>
        /// Parse color markup like [red]text[/] into segments
        /// </summary>
        private List<(string content, string? color)> ParseColorMarkup(string text)
        {
            var result = new List<(string content, string? color)>();
            var currentContent = new System.Text.StringBuilder();
            string? currentColor = null;
            int i = 0;

            while (i < text.Length)
            {
                if (text[i] == '[')
                {
                    int end = text.IndexOf(']', i + 1);
                    if (end > i)
                    {
                        var tag = text.Substring(i + 1, end - i - 1).ToLowerInvariant();

                        if (tag == "/" || tag == "/color")
                        {
                            if (currentContent.Length > 0)
                            {
                                result.Add((currentContent.ToString(), currentColor));
                                currentContent.Clear();
                            }
                            currentColor = null;
                            i = end + 1;
                            continue;
                        }
                        else if (IsValidColor(tag))
                        {
                            if (currentContent.Length > 0)
                            {
                                result.Add((currentContent.ToString(), currentColor));
                                currentContent.Clear();
                            }
                            currentColor = tag;
                            i = end + 1;
                            continue;
                        }
                    }
                }

                currentContent.Append(text[i]);
                i++;
            }

            if (currentContent.Length > 0)
                result.Add((currentContent.ToString(), currentColor));

            return result;
        }

        private bool IsValidColor(string color)
        {
            return ColorNameToConsole(color) != ConsoleColor.White || color == "white" || color == "bright_white";
        }

        private ConsoleColor ColorNameToConsole(string colorName)
        {
            return colorName?.ToLower() switch
            {
                "black" => ConsoleColor.Black,
                "darkred" or "dark_red" => ConsoleColor.DarkRed,
                "darkgreen" or "dark_green" => ConsoleColor.DarkGreen,
                "darkyellow" or "dark_yellow" or "brown" => ConsoleColor.DarkYellow,
                "darkblue" or "dark_blue" => ConsoleColor.DarkBlue,
                "darkmagenta" or "dark_magenta" => ConsoleColor.DarkMagenta,
                "darkcyan" or "dark_cyan" => ConsoleColor.DarkCyan,
                "gray" or "grey" => ConsoleColor.Gray,
                "darkgray" or "dark_gray" => ConsoleColor.DarkGray,
                "red" or "bright_red" => ConsoleColor.Red,
                "green" or "bright_green" => ConsoleColor.Green,
                "yellow" or "bright_yellow" => ConsoleColor.Yellow,
                "blue" or "bright_blue" => ConsoleColor.Blue,
                "magenta" or "bright_magenta" => ConsoleColor.Magenta,
                "cyan" or "bright_cyan" => ConsoleColor.Cyan,
                "white" or "bright_white" => ConsoleColor.White,
                _ => ConsoleColor.White
            };
        }

        #endregion

        #region Display Methods (for compatibility)

        public void DisplayMessage(string message, string color = "white", bool newLine = true)
        {
            if (newLine)
                WriteLine(message, color);
            else
                Write(message, color);
        }

        public void ShowStatusBar(string playerName, int level, int hp, int maxHp, int gold, int turns)
        {
            SetColor("gray");
            WriteLine("────────────────────────────────────────────────────────");
            Write($" {playerName}", "bright_cyan");
            Write($" | Lv.{level}", "yellow");
            Write($" | HP: ", "gray");

            // Color HP based on percentage
            float hpPercent = maxHp > 0 ? (float)hp / maxHp : 0;
            string hpColor = hpPercent > 0.5f ? "green" : hpPercent > 0.25f ? "yellow" : "red";
            Write($"{hp}/{maxHp}", hpColor);

            Write($" | Gold: {gold}", "yellow");
            Write($" | Turns: {turns}", "cyan");
            WriteLine("");
            SetColor("gray");
            WriteLine("────────────────────────────────────────────────────────");
        }

        public void DrawBox(int x, int y, int width, int height, string color)
        {
            SetColor(color);

            // Top border
            Write("╔");
            for (int i = 0; i < width - 2; i++) Write("═");
            WriteLine("╗");

            // Sides
            for (int row = 0; row < height - 2; row++)
            {
                Write("║");
                for (int i = 0; i < width - 2; i++) Write(" ");
                WriteLine("║");
            }

            // Bottom border
            Write("╚");
            for (int i = 0; i < width - 2; i++) Write("═");
            WriteLine("╝");
        }

        #endregion
    }

    /// <summary>
    /// Menu option for GetMenuChoice
    /// </summary>
    public class MenuOption
    {
        public string Key { get; set; } = "";
        public string Text { get; set; } = "";
        public string? Color { get; set; }

        public MenuOption() { }

        public MenuOption(string key, string text, string? color = null)
        {
            Key = key;
            Text = text;
            Color = color;
        }
    }
}
