using System;
using System.Text;

namespace UsurperRemake.UI
{
    /// <summary>
    /// Standardized UI helper for consistent box drawing and formatting
    /// Uses 78-char inner width (80 total with borders) - classic terminal standard
    /// </summary>
    public static class UIHelper
    {
        // Standard box width (inner content width, excluding borders)
        public const int BoxWidth = 78;
        public const int TotalWidth = 80;

        // Box drawing characters
        private const char TopLeft = '╔';
        private const char TopRight = '╗';
        private const char BottomLeft = '╚';
        private const char BottomRight = '╝';
        private const char Horizontal = '═';
        private const char Vertical = '║';
        private const char TeeLeft = '╠';
        private const char TeeRight = '╣';

        /// <summary>
        /// Draw the top of a box with optional centered title
        /// </summary>
        public static void DrawBoxTop(TerminalEmulator terminal, string title = "", string color = "bright_blue")
        {
            if (string.IsNullOrEmpty(title))
            {
                terminal.WriteLine($"{TopLeft}{new string(Horizontal, BoxWidth)}{TopRight}", color);
            }
            else
            {
                // Center the title within the box
                int padding = (BoxWidth - title.Length - 2) / 2; // -2 for spaces around title
                string left = new string(Horizontal, Math.Max(1, padding));
                string right = new string(Horizontal, Math.Max(1, BoxWidth - padding - title.Length - 2));
                terminal.WriteLine($"{TopLeft}{left} {title} {right}{TopRight}", color);
            }
        }

        /// <summary>
        /// Draw a line of content inside the box, properly padded
        /// </summary>
        public static void DrawBoxLine(TerminalEmulator terminal, string text, string borderColor = "bright_blue", string textColor = "white")
        {
            // Word-wrap if too long instead of truncating
            if (text.Length > BoxWidth)
            {
                // Find leading whitespace to preserve indent on wrapped lines
                int indent = 0;
                while (indent < text.Length && text[indent] == ' ') indent++;
                string indentStr = new string(' ', indent);

                // Word-wrap the text
                string remaining = text;
                while (remaining.Length > 0)
                {
                    if (remaining.Length <= BoxWidth)
                    {
                        string padded = remaining.PadRight(BoxWidth);
                        terminal.Write($"{Vertical}", borderColor);
                        terminal.Write(padded, textColor);
                        terminal.WriteLine($"{Vertical}", borderColor);
                        break;
                    }

                    // Find last space within BoxWidth
                    int breakAt = remaining.LastIndexOf(' ', BoxWidth - 1);
                    if (breakAt <= indent) breakAt = BoxWidth; // No good break point, force break

                    string line = remaining.Substring(0, breakAt).PadRight(BoxWidth);
                    terminal.Write($"{Vertical}", borderColor);
                    terminal.Write(line, textColor);
                    terminal.WriteLine($"{Vertical}", borderColor);

                    remaining = indentStr + remaining.Substring(breakAt).TrimStart();
                }
                return;
            }

            // Pad to fill the box
            string paddedText = text.PadRight(BoxWidth);

            terminal.Write($"{Vertical}", borderColor);
            terminal.Write(paddedText, textColor);
            terminal.WriteLine($"{Vertical}", borderColor);
        }

        /// <summary>
        /// Draw a line with colored content segments (format: "label: value")
        /// </summary>
        public static void DrawBoxLabelValue(TerminalEmulator terminal, string label, string value,
            string borderColor = "bright_blue", string labelColor = "cyan", string valueColor = "white")
        {
            string combined = $"{label}: {value}";
            if (combined.Length > BoxWidth)
                combined = combined.Substring(0, BoxWidth);

            terminal.Write($"{Vertical}", borderColor);
            terminal.Write(label + ": ", labelColor);
            terminal.Write(value.PadRight(BoxWidth - label.Length - 2), valueColor);
            terminal.WriteLine($"{Vertical}", borderColor);
        }

        /// <summary>
        /// Draw an empty line inside the box
        /// </summary>
        public static void DrawBoxEmpty(TerminalEmulator terminal, string color = "bright_blue")
        {
            terminal.WriteLine($"{Vertical}{new string(' ', BoxWidth)}{Vertical}", color);
        }

        /// <summary>
        /// Draw a horizontal separator inside the box
        /// </summary>
        public static void DrawBoxSeparator(TerminalEmulator terminal, string color = "bright_blue")
        {
            terminal.WriteLine($"{TeeLeft}{new string(Horizontal, BoxWidth)}{TeeRight}", color);
        }

        /// <summary>
        /// Draw the bottom of a box
        /// </summary>
        public static void DrawBoxBottom(TerminalEmulator terminal, string color = "bright_blue")
        {
            terminal.WriteLine($"{BottomLeft}{new string(Horizontal, BoxWidth)}{BottomRight}", color);
        }

        /// <summary>
        /// Draw a menu option line (e.g., "[A] Attack")
        /// </summary>
        public static void DrawMenuOption(TerminalEmulator terminal, string key, string description,
            string borderColor = "bright_blue", string keyColor = "bright_yellow", string textColor = "white")
        {
            string line = $"  [{key}] {description}";
            if (line.Length > BoxWidth)
                line = line.Substring(0, BoxWidth);

            terminal.Write($"{Vertical}  [", borderColor);
            terminal.Write(key, keyColor);
            terminal.Write($"] {description}".PadRight(BoxWidth - key.Length - 5), textColor);
            terminal.WriteLine($"{Vertical}", borderColor);
        }

        /// <summary>
        /// Draw a simple complete box with title and content lines
        /// </summary>
        public static void DrawSimpleBox(TerminalEmulator terminal, string title, string[] lines,
            string borderColor = "bright_blue", string textColor = "white")
        {
            DrawBoxTop(terminal, title, borderColor);
            DrawBoxEmpty(terminal, borderColor);
            foreach (var line in lines)
            {
                DrawBoxLine(terminal, "  " + line, borderColor, textColor);
            }
            DrawBoxEmpty(terminal, borderColor);
            DrawBoxBottom(terminal, borderColor);
        }

        /// <summary>
        /// Draw a centered text line inside the box
        /// </summary>
        public static void DrawBoxCentered(TerminalEmulator terminal, string text,
            string borderColor = "bright_blue", string textColor = "white")
        {
            if (text.Length > BoxWidth)
                text = text.Substring(0, BoxWidth);

            int padding = (BoxWidth - text.Length) / 2;
            string paddedText = new string(' ', padding) + text + new string(' ', BoxWidth - padding - text.Length);

            terminal.Write($"{Vertical}", borderColor);
            terminal.Write(paddedText, textColor);
            terminal.WriteLine($"{Vertical}", borderColor);
        }

        /// <summary>
        /// Draw a stat bar (HP/MP style) inside the box
        /// </summary>
        public static void DrawStatBar(TerminalEmulator terminal, string label, long current, long max,
            string borderColor = "bright_blue", string labelColor = "cyan", string barColor = "bright_green")
        {
            int barWidth = 30;
            double ratio = max > 0 ? (double)current / max : 0;
            int filled = (int)(ratio * barWidth);

            string bar = new string('█', Math.Max(0, Math.Min(barWidth, filled))) +
                         new string('░', Math.Max(0, barWidth - filled));
            string stats = $"{current}/{max}";

            string line = $"  {label}: [{bar}] {stats}";
            if (line.Length > BoxWidth)
                line = line.Substring(0, BoxWidth);

            terminal.Write($"{Vertical}  ", borderColor);
            terminal.Write($"{label}: ", labelColor);
            terminal.Write("[", "white");
            terminal.Write(new string('█', Math.Max(0, Math.Min(barWidth, filled))), barColor);
            terminal.Write(new string('░', Math.Max(0, barWidth - filled)), "gray");
            terminal.Write($"] {stats}".PadRight(BoxWidth - label.Length - barWidth - 7), "white");
            terminal.WriteLine($"{Vertical}", borderColor);
        }

        /// <summary>
        /// Format a number with thousands separators
        /// </summary>
        public static string FormatNumber(long number)
        {
            return number.ToString("N0");
        }

        /// <summary>
        /// Format gold with color code
        /// </summary>
        public static string FormatGold(long amount)
        {
            return $"[bright_yellow]{amount:N0}[/] gold";
        }

        /// <summary>
        /// Create a progress indicator string
        /// </summary>
        public static string CreateProgressBar(double ratio, int width = 20)
        {
            int filled = (int)(ratio * width);
            return $"[{new string('█', filled)}{new string('░', width - filled)}]";
        }
    }
}
