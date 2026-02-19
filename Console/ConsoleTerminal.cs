using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UsurperRemake.BBS;

namespace UsurperConsole
{
    /// <summary>
    /// Console implementation of terminal functionality
    /// Replaces Godot UI components for command-line gaming
    /// </summary>
    public class ConsoleTerminal
    {
        private ConsoleColor currentColor = ConsoleColor.White;

        /// <summary>
        /// Convert string color names to ConsoleColor enum
        /// </summary>
        private ConsoleColor ColorNameToConsole(string colorName)
        {
            return colorName?.ToLower() switch
            {
                "black" => ConsoleColor.Black,
                "darkred" => ConsoleColor.DarkRed,
                "darkgreen" => ConsoleColor.DarkGreen,
                "darkyellow" => ConsoleColor.DarkYellow,
                "darkblue" => ConsoleColor.DarkBlue,
                "darkmagenta" => ConsoleColor.DarkMagenta,
                "darkcyan" => ConsoleColor.DarkCyan,
                "gray" => ConsoleColor.Gray,
                "darkgray" => ConsoleColor.DarkGray,
                "red" => ConsoleColor.Red,
                "green" => ConsoleColor.Green,
                "yellow" => ConsoleColor.Yellow,
                "blue" => ConsoleColor.Blue,
                "magenta" => ConsoleColor.Magenta,
                "cyan" => ConsoleColor.Cyan,
                "white" => ConsoleColor.White,
                "bright_white" => ConsoleColor.White,
                "bright_red" => ConsoleColor.Red,
                "bright_green" => ConsoleColor.Green,
                "bright_yellow" => ConsoleColor.Yellow,
                "bright_blue" => ConsoleColor.Blue,
                "bright_magenta" => ConsoleColor.Magenta,
                "bright_cyan" => ConsoleColor.Cyan,
                "brown" => ConsoleColor.DarkYellow,
                _ => ConsoleColor.White
            };
        }

        public void WriteLine(string text)
        {
            // Check for inline color markup tags like [red]text[/]
            if (text != null && text.Contains("[") && text.Contains("[/]"))
            {
                WriteMarkup(text);
                Console.WriteLine();
            }
            else
            {
                Console.WriteLine(text);
            }
        }

        /// <summary>
        /// Parse and render text with inline color markup tags like [red]text[/]
        /// Uses regex-based approach for reliable parsing
        /// </summary>
        public void WriteMarkup(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var oldColor = Console.ForegroundColor;

            // Process text sequentially, finding color tags
            int pos = 0;
            while (pos < text.Length)
            {
                // Look for an opening color tag [colorname]
                var tagMatch = Regex.Match(text.Substring(pos), @"^\[([a-z_]+)\]", RegexOptions.IgnoreCase);
                if (tagMatch.Success)
                {
                    string colorName = tagMatch.Groups[1].Value;
                    var color = ColorNameToConsole(colorName);

                    // Move past the opening tag
                    pos += tagMatch.Length;

                    // Find the content and closing tag [/]
                    int depth = 1;
                    int contentStart = pos;
                    int contentEnd = pos;

                    while (pos < text.Length && depth > 0)
                    {
                        // Check for closing tag [/] - need pos+2 to be a valid index
                        if (pos + 2 <= text.Length - 1 && text[pos] == '[' && text[pos + 1] == '/' && text[pos + 2] == ']')
                        {
                            depth--;
                            if (depth == 0)
                            {
                                contentEnd = pos;
                                pos += 3; // Skip [/]
                                break;
                            }
                            pos += 3;
                            continue;
                        }

                        // Check for nested opening tag
                        var nestedMatch = Regex.Match(text.Substring(pos), @"^\[([a-z_]+)\]", RegexOptions.IgnoreCase);
                        if (nestedMatch.Success)
                        {
                            depth++;
                            pos += nestedMatch.Length;
                            continue;
                        }

                        pos++;
                    }

                    // Extract and render the content with the color
                    string content = text.Substring(contentStart, contentEnd - contentStart);
                    Console.ForegroundColor = color;
                    WriteMarkup(content); // Recursive for nested tags
                    Console.ForegroundColor = oldColor;
                    continue;
                }

                // Check for stray closing tag (shouldn't happen, but handle it)
                if (pos + 2 <= text.Length - 1 && text[pos] == '[' && text[pos + 1] == '/' && text[pos + 2] == ']')
                {
                    pos += 3;
                    continue;
                }

                // Regular character, print it
                Console.Write(text[pos]);
                pos++;
            }

            Console.ForegroundColor = oldColor;
        }

        public void WriteLine(string text, ConsoleColor color)
        {
            var oldColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ForegroundColor = oldColor;
        }

        public void WriteLine(string text, string color)
        {
            WriteLine(text, ColorNameToConsole(color));
        }

        public void Write(string text)
        {
            // Check for inline color markup tags like [red]text[/]
            if (text != null && text.Contains("[") && text.Contains("[/]"))
            {
                WriteMarkup(text);
            }
            else
            {
                Console.Write(text);
            }
        }

        public void Write(string text, ConsoleColor color)
        {
            var oldColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.Write(text);
            Console.ForegroundColor = oldColor;
        }

        public void Write(string text, string color)
        {
            Write(text, ColorNameToConsole(color));
        }

        public void SetColor(ConsoleColor color)
        {
            Console.ForegroundColor = color;
            currentColor = color;
        }

        public void SetColor(string color)
        {
            SetColor(ColorNameToConsole(color));
        }
        
        public async Task<string> GetInput(string prompt = "")
        {
            await Task.CompletedTask;
            if (!string.IsNullOrEmpty(prompt))
            {
                Console.Write(prompt);
            }
            return Console.ReadLine() ?? "";
        }

        public async Task<string> GetStringInput(string prompt = "")
        {
            return await GetInput(prompt);
        }

        public async Task<string> GetKeyInput(string prompt = "")
        {
            await Task.CompletedTask;
            if (!string.IsNullOrEmpty(prompt))
            {
                Console.Write(prompt);
            }
            return Console.ReadKey().KeyChar.ToString();
        }

        public async Task WaitForKeyPress(string message = "Press Enter to continue...")
        {
            await Task.CompletedTask;
            Console.WriteLine(message);
            Console.ReadKey();
        }
        
        public async Task PressAnyKey(string message = "Press Enter to continue...")
        {
            await WaitForKeyPress(message);
        }
        
        public void Clear()
        {
            SafeClearScreen();
        }

        public void ClearScreen()
        {
            SafeClearScreen();
        }

        private void SafeClearScreen()
        {
            if (DoorMode.IsInDoorMode)
            {
                // In BBS door mode with Standard I/O, use ANSI escape codes
                Console.Write("\x1b[2J\x1b[H");
            }
            else
            {
                try
                {
                    Console.Clear();
                }
                catch (System.IO.IOException)
                {
                    // Fallback to ANSI if Console.Clear fails
                    Console.Write("\x1b[2J\x1b[H");
                }
            }
        }

        public async Task WaitForKey()
        {
            Console.ReadKey(true);
            await Task.CompletedTask;
        }

        public async Task WaitForKey(string message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                Console.WriteLine(message);
            }
            Console.ReadKey(true);
            await Task.CompletedTask;
        }

        public void DisplayFile(string filename)
        {
            try
            {
                if (File.Exists(filename))
                {
                    var content = File.ReadAllText(filename);
                    Console.WriteLine(content);
                }
                else
                {
                    Console.WriteLine($"File not found: {filename}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading file {filename}: {ex.Message}");
            }
        }
    }
} 