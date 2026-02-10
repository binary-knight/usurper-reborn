using System;
using System.IO;
using System.Threading.Tasks;

namespace UsurperRemake.UI
{
    /// <summary>
    /// Displays the USURPER REBORN ASCII art splash screen
    /// </summary>
    public static class SplashScreen
    {
        public static async Task Show(dynamic terminal)
        {
            terminal.ClearScreen();

            // Display the ASCII art title
            var lines = new[]
            {
                "████████████████████████████████████████████████████████████████████████████████",
                "████████████████████████████████████████████████████████████████████████████████",
                "████████████████████████████████████████████████████████████████████████████████",
                "███                                                                          ███",
                "███    ██    ██ ███████ ██    ██ ██████  ██████  ███████ ██████            ███",
                "███    ██    ██ ██      ██    ██ ██   ██ ██   ██ ██      ██   ██           ███",
                "███    ██    ██ ███████ ██    ██ ██████  ██████  █████   ██████            ███",
                "███    ██    ██      ██ ██    ██ ██   ██ ██      ██      ██   ██           ███",
                "███     ██████  ███████  ██████  ██   ██ ██      ███████ ██   ██           ███",
                "███                                                                          ███",
                "███           ██████  ███████ ██████   ██████  ██████  ███    ██           ███",
                "███           ██   ██ ██      ██   ██ ██    ██ ██   ██ ████   ██           ███",
                "███           ██████  █████   ██████  ██    ██ ██████  ██ ██  ██           ███",
                "███           ██   ██ ██      ██   ██ ██    ██ ██   ██ ██  ██ ██           ███",
                "███           ██   ██ ███████ ██████   ██████  ██   ██ ██   ████           ███",
                "███                                                                          ███",
                "████████████████████████████████████████████████████████████████████████████████",
                "███                                                                          ███",
                "███              A Classic BBS Door Game - Reimagined for 2026               ███",
                "███                                                                          ███",
                "███                    Based on the original by Jakob Dangarden             ███",
                "███                                                                          ███",
                "████████████████████████████████████████████████████████████████████████████████",
                "████████████████████████████████████████████████████████████████████████████████",
                "████████████████████████████████████████████████████████████████████████████████",
            };

            // Insert version line dynamically
            var versionText = $"Alpha v{GameConfig.Version.Replace("-alpha", "")}";
            var linesList = new System.Collections.Generic.List<string>(lines);
            linesList.Insert(linesList.Count - 3, $"███{versionText}███"); // placeholder, will be padded below
            lines = linesList.ToArray();

            // Normalize all lines to consistent width (match solid border lines)
            int targetWidth = lines[0].Length;
            for (int j = 0; j < lines.Length; j++)
            {
                if (lines[j].Length >= 6 && lines[j].StartsWith("███") && lines[j].EndsWith("███")
                    && lines[j].Length != targetWidth)
                {
                    // Framed line with wrong width - pad content between borders
                    string innerContent = lines[j].Substring(3, lines[j].Length - 6);
                    int contentWidth = targetWidth - 6;
                    // Center the content if it's a short text line (like version)
                    if (innerContent.Trim().Length > 0 && innerContent.Trim().Length < contentWidth / 2)
                    {
                        int totalPad = contentWidth - innerContent.Trim().Length;
                        int leftPad = totalPad / 2;
                        innerContent = new string(' ', leftPad) + innerContent.Trim() + new string(' ', contentWidth - leftPad - innerContent.Trim().Length);
                    }
                    else
                    {
                        innerContent = innerContent.PadRight(contentWidth);
                    }
                    lines[j] = "███" + innerContent + "███";
                }
            }

            // Animated reveal with segment-level coloring
            // Borders are subtle darkgray, letter art pops in bright colors
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                bool isSolidBorder = !line.Contains(' ');

                if (isSolidBorder)
                {
                    // Solid border line - subtle frame
                    terminal.SetColor("darkgray");
                    terminal.WriteLine(line);
                }
                else
                {
                    // Framed line - color border and content separately
                    string leftBorder = line.Substring(0, 3);
                    string content = line.Substring(3, line.Length - 6);
                    string rightBorder = line.Substring(line.Length - 3);

                    // Determine content color by section
                    string contentColor;
                    if (i >= 4 && i <= 8)
                        contentColor = "bright_yellow";     // USURPER block letters
                    else if (i >= 10 && i <= 14)
                        contentColor = "bright_cyan";       // REBORN block letters
                    else if (line.Contains("BBS Door Game"))
                        contentColor = "white";
                    else if (line.Contains("Jakob Dangarden"))
                        contentColor = "gray";
                    else if (line.Contains("Alpha v"))
                        contentColor = "bright_green";
                    else
                        contentColor = "darkgray";          // Empty frame spacers

                    terminal.SetColor("darkgray");
                    terminal.Write(leftBorder);
                    terminal.SetColor(contentColor);
                    terminal.Write(content);
                    terminal.SetColor("darkgray");
                    terminal.WriteLine(rightBorder);
                }

                // Small delay for dramatic effect on title lines
                if (i >= 4 && i <= 14)
                {
                    await Task.Delay(50);
                }
            }

            terminal.WriteLine("");
            terminal.WriteLine("");
            terminal.SetColor("bright_white");
            terminal.Write("                           Press Enter to begin...");
            terminal.SetColor("white");

            await terminal.WaitForKey("");
            terminal.ClearScreen();
        }
    }
}
