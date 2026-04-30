using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UsurperRemake.Data;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Renders alpha-era founder statues at Pantheon, Castle, and Main Street.
    /// The data comes from FounderStatueData (a frozen list of 11 founders
    /// captured before the May 2026 beta wipe). This system handles the
    /// player-facing list + examine flow, including the cracked-statue
    /// variant for the two founders who accidentally deleted their characters.
    /// </summary>
    public static class FounderStatueSystem
    {
        /// <summary>
        /// Show the statue list at a location, with select-by-number examine.
        /// Blocks until the player exits with [R] or [Q].
        /// </summary>
        public static async Task ShowStatuesAt(
            FounderStatueData.StatueLocationTag location,
            TerminalEmulator terminal)
        {
            var statues = FounderStatueData.GetStatuesAt(location).ToList();
            if (statues.Count == 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine("  No statues stand here.");
                await terminal.PressAnyKey();
                return;
            }

            // Phase 7-style emit for Electron client (graphical overlay where present).
            if (GameConfig.ElectronMode)
            {
                EmitStatueListToElectron(location, statues);
            }

            while (true)
            {
                if (!GameConfig.ElectronMode)
                {
                    RenderStatueListText(terminal, location, statues);
                }

                var input = (await terminal.GetInput("  Examine which statue? (1-" + statues.Count + ", or [R]eturn): ")).Trim().ToUpperInvariant();
                if (input == "R" || input == "Q" || input == "")
                {
                    if (GameConfig.ElectronMode) ElectronBridge.Emit("statue_close", new { });
                    return;
                }

                if (int.TryParse(input, out int idx) && idx >= 1 && idx <= statues.Count)
                {
                    await ShowStatueDetail(statues[idx - 1], terminal);
                }
            }
        }

        private static void RenderStatueListText(
            TerminalEmulator terminal,
            FounderStatueData.StatueLocationTag location,
            List<FounderStatueData.FounderStatue> statues)
        {
            terminal.WriteLine("");
            string locationLabel = location switch
            {
                FounderStatueData.StatueLocationTag.Pantheon => "Pantheon: Hall of the Ascended",
                FounderStatueData.StatueLocationTag.Castle => "Castle Courtyard: The Slayers of Manwe",
                FounderStatueData.StatueLocationTag.MainStreetMini => "Main Square: Founders' Plinths",
                _ => "Statues"
            };

            if (!GameConfig.ScreenReaderMode)
            {
                terminal.WriteLine("═══════════════════════════════════════════════════════════════", "bright_yellow");
                terminal.WriteLine("  " + locationLabel, "bright_yellow");
                terminal.WriteLine("═══════════════════════════════════════════════════════════════", "bright_yellow");
            }
            else
            {
                terminal.WriteLine(locationLabel, "bright_yellow");
            }
            terminal.WriteLine("");

            for (int i = 0; i < statues.Count; i++)
            {
                var s = statues[i];
                string crackTag = s.IsCracked ? " (cracked)" : "";
                string color = s.IsCracked ? "dark_gray" : "white";

                if (s.Location == FounderStatueData.StatueLocationTag.MainStreetMini)
                {
                    // Mini plinths render compactly
                    terminal.Write($"  [{i + 1}] ", "bright_yellow");
                    terminal.WriteLine($"{s.DisplayName}: {s.AchievementTag}{crackTag}", color);
                }
                else
                {
                    terminal.Write($"  [{i + 1}] ", "bright_yellow");
                    string subtitle = !string.IsNullOrEmpty(s.DivineName) && s.Location == FounderStatueData.StatueLocationTag.Pantheon
                        ? s.DivineName
                        : $"Lv.{s.FinalLevel} {s.ClassName}";
                    terminal.WriteLine($"{s.DisplayName}: {subtitle}{crackTag}", color);
                }
            }

            terminal.WriteLine("");
            terminal.WriteLine($"  {FounderStatueData.GetUniqueFounderCount()} alpha-era founders are commemorated across the world.", "gray");
            terminal.WriteLine("");
        }

        /// <summary>
        /// Render a single statue's plaque: the carved-stone narrative plus
        /// character details. The cracked variant gets a different ASCII frame.
        /// </summary>
        public static async Task ShowStatueDetail(
            FounderStatueData.FounderStatue statue,
            TerminalEmulator terminal)
        {
            if (GameConfig.ElectronMode)
            {
                ElectronBridge.Emit("statue_detail", new
                {
                    displayName = statue.DisplayName,
                    divineName = statue.DivineName,
                    className = statue.ClassName,
                    raceName = statue.RaceName,
                    finalLevel = statue.FinalLevel,
                    cycleReached = statue.CycleReached,
                    endingTag = statue.EndingTag,
                    achievementTag = statue.AchievementTag,
                    inscription = statue.Inscription,
                    isCracked = statue.IsCracked,
                    customArt = statue.CustomArt,
                    artColor = statue.ArtColor
                });
                ElectronBridge.EmitPressAnyKey();
                await terminal.PressAnyKey();
                return;
            }

            terminal.WriteLine("");
            RenderStatueArt(terminal, statue);
            terminal.WriteLine("");

            // Plaque header
            string color = statue.IsCracked ? "dark_gray" : "bright_yellow";
            terminal.WriteLine($"  === {statue.AchievementTag} ===", color);
            terminal.WriteLine("");

            // Character info line
            terminal.SetColor("white");
            string identity = !string.IsNullOrEmpty(statue.DivineName) && statue.Location == FounderStatueData.StatueLocationTag.Pantheon
                ? $"  {statue.DisplayName}, known to the heavens as {statue.DivineName}"
                : $"  {statue.DisplayName}";
            terminal.WriteLine(identity);

            if (statue.FinalLevel > 0 && statue.ClassName != "Unknown")
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  Lv.{statue.FinalLevel} {statue.RaceName} {statue.ClassName}, Cycle {statue.CycleReached} {statue.EndingTag}");
            }
            else if (statue.IsCracked)
            {
                terminal.SetColor("dark_gray");
                terminal.WriteLine("  (Their record was lost to misadventure.)");
            }
            terminal.WriteLine("");

            // Inscription — the carved-stone narrative
            terminal.SetColor(statue.IsCracked ? "dark_gray" : "cyan");
            WriteWrappedItalic(terminal, statue.Inscription, 72);
            terminal.WriteLine("");

            await terminal.PressAnyKey();
        }

        private static void RenderStatueArt(TerminalEmulator terminal, FounderStatueData.FounderStatue statue)
        {
            if (GameConfig.ScreenReaderMode || GameConfig.DisableCharacterMonsterArt)
            {
                terminal.WriteLine("  [Statue]");
                return;
            }

            // Each founder has their own themed silhouette (CustomArt), tied to
            // their narrative. Falls back to the generic monument shapes only
            // if a founder is missing custom art (defensive).
            string[] art;
            string color;
            if (statue.CustomArt != null && statue.CustomArt.Length > 0)
            {
                art = statue.CustomArt;
                color = string.IsNullOrEmpty(statue.ArtColor) ? "white" : statue.ArtColor;
            }
            else
            {
                art = statue.IsCracked
                    ? CrackedStatueArt
                    : statue.Location == FounderStatueData.StatueLocationTag.MainStreetMini
                        ? MiniStatueArt
                        : StandardStatueArt;
                color = statue.IsCracked ? "dark_gray"
                    : statue.Location == FounderStatueData.StatueLocationTag.Pantheon ? "bright_yellow"
                    : "white";
            }

            foreach (var line in art)
            {
                terminal.WriteLine(line, color);
            }
        }

        // Generic fallback monument silhouettes (used only if a founder lacks
        // CustomArt). Tall obelisk with a centered icon on a plinth. Reads as
        // "engraved stone" rather than "person".
        private static readonly string[] StandardStatueArt = new[]
        {
            "              .-----.",
            "             /       \\",
            "            |         |",
            "            |    *    |",
            "            |         |",
            "            |   ___   |",
            "            |  |   |  |",
            "            |  |___|  |",
            "            |         |",
            "            |_________|",
            "             |       |",
            "          ___|_______|___",
            "         |               |",
            "         |_______________|",
            "         =================",
        };

        // Same monument shape, but with diagonal cracks running through the
        // engraving and chunks missing from the plinth. Reads as a memorial
        // that didn't quite weather time well — fitting for the two founders
        // who deleted themselves.
        private static readonly string[] CrackedStatueArt = new[]
        {
            "              .-----.",
            "             /    /  \\",
            "            |    /    |",
            "            |   /x    |",
            "            |  /      |",
            "            | /  __   |",
            "            |/  |   | |",
            "            /   |___|/|",
            "           /|       /||",
            "          / |______/_||",
            "             |   /  |",
            "          ___|  /   |___",
            "         |    \\/        |",
            "         |____/_________|",
            "         ====/===========",
        };

        // Compact plinth for the Main Street square — smaller monument since
        // the central plaza wouldn't host the same scale as a Pantheon obelisk.
        private static readonly string[] MiniStatueArt = new[]
        {
            "          .---------.",
            "         |           |",
            "         |     +     |",
            "         |___________|",
            "         =============",
        };

        private static void WriteWrappedItalic(TerminalEmulator terminal, string text, int width)
        {
            // Simple word-wrap. Render as italic-style indented narrative.
            var words = text.Split(' ');
            var line = new System.Text.StringBuilder("    \"");
            foreach (var w in words)
            {
                if (line.Length + w.Length + 1 > width + 4)
                {
                    terminal.WriteLine(line.ToString());
                    line.Clear();
                    line.Append("     ");
                }
                if (line.Length > 5) line.Append(' ');
                line.Append(w);
            }
            if (line.Length > 5)
            {
                line.Append('"');
                terminal.WriteLine(line.ToString());
            }
        }

        private static void EmitStatueListToElectron(
            FounderStatueData.StatueLocationTag location,
            List<FounderStatueData.FounderStatue> statues)
        {
            ElectronBridge.Emit("statue_list", new
            {
                locationLabel = location switch
                {
                    FounderStatueData.StatueLocationTag.Pantheon => "Hall of the Ascended",
                    FounderStatueData.StatueLocationTag.Castle => "The Slayers of Manwe",
                    FounderStatueData.StatueLocationTag.MainStreetMini => "Founders' Plinths",
                    _ => "Statues"
                },
                statues = statues.Select((s, i) => new
                {
                    key = (i + 1).ToString(),
                    displayName = s.DisplayName,
                    subtitle = !string.IsNullOrEmpty(s.DivineName) && s.Location == FounderStatueData.StatueLocationTag.Pantheon
                        ? s.DivineName
                        : $"Lv.{s.FinalLevel} {s.ClassName}",
                    achievementTag = s.AchievementTag,
                    isCracked = s.IsCracked
                }).ToList(),
                totalFounders = FounderStatueData.GetUniqueFounderCount()
            });
        }
    }
}
