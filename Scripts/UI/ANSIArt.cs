using System;
using System.Collections.Generic;

namespace UsurperRemake.UI
{
    /// <summary>
    /// ANSI Art System - Provides classic BBS-style ASCII/ANSI art for key game moments
    /// These art pieces evoke the 1990s BBS door game aesthetic
    /// </summary>
    public static class ANSIArt
    {
        /// <summary>
        /// Game title logo - shown at startup
        /// </summary>
        public static readonly string[] TitleLogo = new[]
        {
            "[bright_red]",
            "  ██╗   ██╗███████╗██╗   ██╗██████╗ ██████╗ ███████╗██████╗ ",
            "  ██║   ██║██╔════╝██║   ██║██╔══██╗██╔══██╗██╔════╝██╔══██╗",
            "  ██║   ██║███████╗██║   ██║██████╔╝██████╔╝█████╗  ██████╔╝",
            "  ██║   ██║╚════██║██║   ██║██╔══██╗██╔═══╝ ██╔══╝  ██╔══██╗",
            "  ╚██████╔╝███████║╚██████╔╝██║  ██║██║     ███████╗██║  ██║",
            "   ╚═════╝ ╚══════╝ ╚═════╝ ╚═╝  ╚═╝╚═╝     ╚══════╝╚═╝  ╚═╝",
            "[yellow]           ╔═══════════════════════════════════════╗",
            "[yellow]           ║[white]   H A L L S   O F   A V A R I C E   [yellow]║",
            "[yellow]           ╚═══════════════════════════════════════╝",
            "[gray]                    ~ A BBS Door Game Classic ~",
            "[/]"
        };

        /// <summary>
        /// Entering the dungeon
        /// </summary>
        public static readonly string[] DungeonEntrance = new[]
        {
            "[gray]",
            "                    ▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄",
            "                   █░░░░░░░░░░░░░░░░░░░█",
            "                  █░[white]╔═══════════════╗[gray]░█",
            "                  █░[white]║[red]▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓[white]║[gray]░█",
            "                  █░[white]║[red]▓[black]             [red]▓[white]║[gray]░█",
            "                  █░[white]║[red]▓[black]   DUNGEON   [red]▓[white]║[gray]░█",
            "                  █░[white]║[red]▓[black]    BELOW    [red]▓[white]║[gray]░█",
            "                  █░[white]║[red]▓[black]             [red]▓[white]║[gray]░█",
            "                  █░[white]║[red]▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓[white]║[gray]░█",
            "                  █░[white]╚═══════════════╝[gray]░█",
            "                 ██░░░░░░░░░░░░░░░░░░░░░██",
            "                ████████████████████████████",
            "[yellow]         You descend into the darkness below...[/]"
        };

        /// <summary>
        /// Player death screen
        /// </summary>
        public static readonly string[] Death = new[]
        {
            "[red]",
            "              ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░",
            "              ░                               ░",
            "              ░     █▀▀█  ▀█▀  █▀▀█           ░",
            "              ░     █▄▄▀   █   █▀▀▀           ░",
            "              ░     █  █  ▀█▀  █              ░",
            "              ░                               ░",
            "              ░  ▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄   ░",
            "              ░  █                       █   ░",
            "              ░  █   Y O U   D I E D    █   ░",
            "              ░  █                       █   ░",
            "              ░  ▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀   ░",
            "              ░                               ░",
            "              ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░",
            "[gray]           Your spirit fades into the void...[/]"
        };

        /// <summary>
        /// Victory over a boss
        /// </summary>
        public static readonly string[] BossVictory = new[]
        {
            "[yellow]",
            "           *  *  *  *  *  *  *  *  *  *  *  *",
            "        ╔══════════════════════════════════════════╗",
            "        ║[bright_yellow]  ██╗   ██╗██╗ ██████╗████████╗ ██████╗ ██████╗ ██╗   ██╗  [yellow]║",
            "        ║[bright_yellow]  ██║   ██║██║██╔════╝╚══██╔══╝██╔═══██╗██╔══██╗╚██╗ ██╔╝  [yellow]║",
            "        ║[bright_yellow]  ██║   ██║██║██║        ██║   ██║   ██║██████╔╝ ╚████╔╝   [yellow]║",
            "        ║[bright_yellow]  ╚██╗ ██╔╝██║██║        ██║   ██║   ██║██╔══██╗  ╚██╔╝    [yellow]║",
            "        ║[bright_yellow]   ╚████╔╝ ██║╚██████╗   ██║   ╚██████╔╝██║  ██║   ██║     [yellow]║",
            "        ║[bright_yellow]    ╚═══╝  ╚═╝ ╚═════╝   ╚═╝    ╚═════╝ ╚═╝  ╚═╝   ╚═╝     [yellow]║",
            "        ╚══════════════════════════════════════════╝",
            "           *  *  *  *  *  *  *  *  *  *  *  *",
            "[white]              The beast has been vanquished![/]"
        };

        /// <summary>
        /// Level up celebration
        /// </summary>
        public static readonly string[] LevelUp = new[]
        {
            "[bright_cyan]",
            "        ╔════════════════════════════════════════╗",
            "        ║  ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░  ║",
            "        ║  ░[bright_yellow]  ██╗     ███████╗██╗   ██╗███████╗██╗     ██╗   ██╗██████╗ ██╗  [bright_cyan]░  ║",
            "        ║  ░[bright_yellow]  ██║     ██╔════╝██║   ██║██╔════╝██║     ██║   ██║██╔══██╗██║  [bright_cyan]░  ║",
            "        ║  ░[bright_yellow]  ██║     █████╗  ██║   ██║█████╗  ██║     ██║   ██║██████╔╝██║  [bright_cyan]░  ║",
            "        ║  ░[bright_yellow]  ██║     ██╔══╝  ╚██╗ ██╔╝██╔══╝  ██║     ██║   ██║██╔═══╝ ╚═╝  [bright_cyan]░  ║",
            "        ║  ░[bright_yellow]  ███████╗███████╗ ╚████╔╝ ███████╗███████╗╚██████╔╝██║     ██╗  [bright_cyan]░  ║",
            "        ║  ░[bright_yellow]  ╚══════╝╚══════╝  ╚═══╝  ╚══════╝╚══════╝ ╚═════╝ ╚═╝     ╚═╝  [bright_cyan]░  ║",
            "        ║  ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░  ║",
            "        ╚════════════════════════════════════════╝",
            "[white]               Your power grows stronger![/]"
        };

        /// <summary>
        /// Castle/throne room
        /// </summary>
        public static readonly string[] Castle = new[]
        {
            "[gray]          ▲           ▲           ▲           ▲",
            "         /█\\         /█\\         /█\\         /█\\",
            "        / █ \\       / █ \\       / █ \\       / █ \\",
            "[white]       ██████████████████████████████████████████",
            "       █[yellow]░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░[white]█",
            "       █[yellow]░[white]  ╔═════════════════════════════╗  [yellow]░[white]█",
            "       █[yellow]░[white]  ║   [bright_yellow]THE ROYAL CASTLE[white]          ║  [yellow]░[white]█",
            "       █[yellow]░[white]  ║   [gray]Seat of Power[white]              ║  [yellow]░[white]█",
            "       █[yellow]░[white]  ╚═════════════════════════════╝  [yellow]░[white]█",
            "       █[yellow]░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░[white]█",
            "       ██████████████████████████████████████████",
            "[gray]       █░░░█    █░░░█    █░░░█    █░░░█    █░░░█",
            "       █   █    █   █    █   █    █   █    █   █[/]"
        };

        /// <summary>
        /// The Inn/Tavern
        /// </summary>
        public static readonly string[] Inn = new[]
        {
            "[yellow]              ╔═══════════════════════════════╗",
            "              ║[brown]  ▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄  [yellow]║",
            "              ║[brown] █[yellow]░░░░░░░░░░░░░░░░░░░░░░░░░[brown]█ [yellow]║",
            "              ║[brown] █[yellow]░[white] THE MIDNIGHT INN [yellow]░░░░░░[brown]█ [yellow]║",
            "              ║[brown] █[yellow]░░░░░░░░░░░░░░░░░░░░░░░░░[brown]█ [yellow]║",
            "              ║[brown] █[yellow]░[white]   Food  Drink  Rumors  [yellow]░[brown]█ [yellow]║",
            "              ║[brown] █[yellow]░░░░░░░░░░░░░░░░░░░░░░░░░[brown]█ [yellow]║",
            "              ║[brown]  ▀▀▀▀▀███████████████▀▀▀▀▀  [yellow]║",
            "              ║[brown]       █[yellow]░░░░░░░░░░░[brown]█       [yellow]║",
            "              ╚═══════════════════════════════╝",
            "[gray]        Warm light spills from the windows...[/]"
        };

        /// <summary>
        /// Treasure found!
        /// </summary>
        public static readonly string[] Treasure = new[]
        {
            "[yellow]",
            "               ╔═══════════════════════════╗",
            "              ╔╝[bright_yellow]░░░░░░░░░░░░░░░░░░░░░░░░░[yellow]╚╗",
            "             ╔╝[bright_yellow]░[yellow]╔═══════════════════════╗[bright_yellow]░[yellow]╚╗",
            "            ╔╝[bright_yellow]░░[yellow]║[bright_yellow]  * * * TREASURE * * *  [yellow]║[bright_yellow]░░[yellow]╚╗",
            "            ║[bright_yellow]░░░[yellow]║[bright_yellow]    ╔═══════════╗      [yellow]║[bright_yellow]░░░[yellow]║",
            "            ║[bright_yellow]░░░[yellow]║[bright_yellow]    ║[white]$$$$$$$$$$$[bright_yellow]║      [yellow]║[bright_yellow]░░░[yellow]║",
            "            ║[bright_yellow]░░░[yellow]║[bright_yellow]    ╚═══════════╝      [yellow]║[bright_yellow]░░░[yellow]║",
            "            ╚╗[bright_yellow]░░[yellow]╚═══════════════════════╝[bright_yellow]░░[yellow]╔╝",
            "             ╚╗[bright_yellow]░░░░░░░░░░░░░░░░░░░░░░░░░[yellow]╔╝",
            "              ╚═══════════════════════════╝",
            "[white]             You found something valuable![/]"
        };

        /// <summary>
        /// Combat started
        /// </summary>
        public static readonly string[] CombatStart = new[]
        {
            "[red]",
            "     ╔══════════════════════════════════════════════════╗",
            "     ║[bright_red]  ░░░   ░░░   ░░░   COMBAT   ░░░   ░░░   ░░░  [red]║",
            "     ╠══════════════════════════════════════════════════╣",
            "     ║                                                  ║",
            "     ║    [white]*  A challenger approaches!  *[red]              ║",
            "     ║                                                  ║",
            "     ╚══════════════════════════════════════════════════╝[/]"
        };

        /// <summary>
        /// Game over / permadeath
        /// </summary>
        public static readonly string[] GameOver = new[]
        {
            "[red]",
            "   ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░",
            "   ░                                                     ░",
            "   ░    ██████╗  █████╗ ███╗   ███╗███████╗              ░",
            "   ░   ██╔════╝ ██╔══██╗████╗ ████║██╔════╝              ░",
            "   ░   ██║  ███╗███████║██╔████╔██║█████╗                ░",
            "   ░   ██║   ██║██╔══██║██║╚██╔╝██║██╔══╝                ░",
            "   ░   ╚██████╔╝██║  ██║██║ ╚═╝ ██║███████╗              ░",
            "   ░    ╚═════╝ ╚═╝  ╚═╝╚═╝     ╚═╝╚══════╝              ░",
            "   ░                                                     ░",
            "   ░    ██████╗ ██╗   ██╗███████╗██████╗                 ░",
            "   ░   ██╔═══██╗██║   ██║██╔════╝██╔══██╗                ░",
            "   ░   ██║   ██║██║   ██║█████╗  ██████╔╝                ░",
            "   ░   ██║   ██║╚██╗ ██╔╝██╔══╝  ██╔══██╗                ░",
            "   ░   ╚██████╔╝ ╚████╔╝ ███████╗██║  ██║                ░",
            "   ░    ╚═════╝   ╚═══╝  ╚══════╝╚═╝  ╚═╝                ░",
            "   ░                                                     ░",
            "   ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░",
            "[gray]             Your legend ends here...[/]"
        };

        /// <summary>
        /// New Game / Character Creation
        /// </summary>
        public static readonly string[] NewHero = new[]
        {
            "[bright_cyan]",
            "    ╔═════════════════════════════════════════════════════╗",
            "    ║                                                     ║",
            "    ║   [white]A NEW HERO RISES[bright_cyan]                                ║",
            "    ║                                                     ║",
            "    ║        [yellow]*[bright_cyan]     [yellow]*[bright_cyan]     [yellow]*[bright_cyan]     [yellow]*[bright_cyan]     [yellow]*[bright_cyan]              ║",
            "    ║                                                     ║",
            "    ║   [gray]The Halls of Avarice await your arrival.[bright_cyan]        ║",
            "    ║   [gray]Will you seek glory, wealth, or power?[bright_cyan]          ║",
            "    ║   [gray]Choose wisely - not all who enter survive.[bright_cyan]      ║",
            "    ║                                                     ║",
            "    ╚═════════════════════════════════════════════════════╝[/]"
        };

        /// <summary>
        /// Skull for dangerous areas/warnings
        /// </summary>
        public static readonly string[] Skull = new[]
        {
            "[white]        ██████████████",
            "      ██[gray]░░░░░░░░░░░░░░[white]██",
            "    ██[gray]░░[white]████[gray]░░░░[white]████[gray]░░[white]██",
            "    ██[gray]░░[white]████[gray]░░░░[white]████[gray]░░[white]██",
            "    ██[gray]░░░░░░[white]████[gray]░░░░░░[white]██",
            "      ██[gray]░░░░░░░░░░░░░░[white]██",
            "        ██[gray]░░░░░░░░░░[white]██",
            "      ████[gray]░░[white]██[gray]░░[white]██[gray]░░[white]████",
            "    ██[gray]░░░░░░░░░░░░░░░░░░[white]██",
            "      ██████████████████[/]"
        };

        /// <summary>
        /// Helper method to display art to terminal
        /// </summary>
        public static void DisplayArt(TerminalEmulator terminal, string[] art)
        {
            foreach (var line in art)
            {
                if (line.StartsWith("[") && line.EndsWith("]") && !line.Contains(" "))
                {
                    // This is a color directive only
                    var color = line.Trim('[', ']', '/');
                    if (!string.IsNullOrEmpty(color))
                        terminal.SetColor(color);
                }
                else
                {
                    // Parse inline color codes and display
                    DisplayColoredLine(terminal, line);
                }
            }
        }

        /// <summary>
        /// Display colored markup text WITHOUT trailing newline.
        /// Used for side-by-side rendering where both columns share a line.
        /// Parses [color]text format, same as DisplayColoredLine but no final WriteLine.
        /// </summary>
        public static void DisplayColoredText(TerminalEmulator terminal, string line)
        {
            int i = 0;
            while (i < line.Length)
            {
                if (line[i] == '[')
                {
                    int end = line.IndexOf(']', i);
                    if (end > i)
                    {
                        string colorCode = line.Substring(i + 1, end - i - 1);
                        if (colorCode == "/")
                            terminal.SetColor("white");
                        else
                            terminal.SetColor(colorCode);
                        i = end + 1;
                        continue;
                    }
                }
                terminal.Write(line[i].ToString());
                i++;
            }
        }

        /// <summary>
        /// Display a line with inline color codes like [red]text[white]more text
        /// </summary>
        public static void DisplayColoredLine(TerminalEmulator terminal, string line)
        {
            int i = 0;
            while (i < line.Length)
            {
                if (line[i] == '[')
                {
                    int end = line.IndexOf(']', i);
                    if (end > i)
                    {
                        string colorCode = line.Substring(i + 1, end - i - 1);
                        if (colorCode == "/")
                        {
                            terminal.SetColor("white");
                        }
                        else
                        {
                            terminal.SetColor(colorCode);
                        }
                        i = end + 1;
                        continue;
                    }
                }
                terminal.Write(line[i].ToString());
                i++;
            }
            terminal.WriteLine("");
        }

        /// <summary>
        /// Display art with a dramatic delay between lines
        /// </summary>
        public static async System.Threading.Tasks.Task DisplayArtAnimated(TerminalEmulator terminal, string[] art, int delayMs = 50)
        {
            foreach (var line in art)
            {
                if (line.StartsWith("[") && line.EndsWith("]") && !line.Contains(" "))
                {
                    var color = line.Trim('[', ']', '/');
                    if (!string.IsNullOrEmpty(color))
                        terminal.SetColor(color);
                }
                else
                {
                    DisplayColoredLine(terminal, line);
                    await System.Threading.Tasks.Task.Delay(delayMs);
                }
            }
        }
    }
}
