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
                terminal.WriteLine(Loc.Get("founder.no_statues"));
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

                var input = (await terminal.GetInput(Loc.Get("founder.examine_prompt", statues.Count))).Trim().ToUpperInvariant();
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

        /// <summary>
        /// v1.2.5: a statue's stored class name (FounderStatueData, English, "Mystic Shaman") in the player's
        /// language; a name that is not a class is shown as stored.
        /// </summary>
        internal static string ClassLabel(string stored) =>
            stored == "Unknown" ? Loc.Get("founder.unknown")
            : Enum.TryParse<CharacterClass>(stored.Replace(" ", ""), out var c) ? GameConfig.GetLocalizedClassName(c) : stored;

        /// <summary>v1.2.5: a statue's stored race name ("Half-Elf") in the player's language; another one as stored.</summary>
        internal static string RaceLabel(string stored) =>
            stored == "Unknown" ? Loc.Get("founder.unknown")
            : Enum.TryParse<CharacterRace>(stored.Replace("-", "").Replace(" ", ""), out var r) ? GameConfig.GetLocalizedRaceName(r) : stored;

        /// <summary>v1.2.5: a statue's stored plaque header (AchievementTag, English) in the player's language; another one as stored.</summary>
        internal static string TagLabel(string stored) => stored switch
        {
            "Immortal" => Loc.Get("founder.tag_immortal"),
            "Immortal (Lost)" => Loc.Get("founder.tag_immortal_lost"),
            "Manwe-Slayer" => Loc.Get("founder.tag_manwe_slayer"),
            "Manwe-Slayer (Lost)" => Loc.Get("founder.tag_manwe_slayer_lost"),
            "Long-Walker" => Loc.Get("founder.tag_long_walker"),
            "Cycle-Walker" => Loc.Get("founder.tag_cycle_walker"),
            "First Shaman" => Loc.Get("founder.tag_first_shaman"),
            "Lv.100 Founder" => Loc.Get("founder.tag_lv100_founder"),
            _ => stored
        };

        /// <summary>
        /// v1.2.5: a statue's inscription in the player's language, keyed founder.inscription_{n} where n is the place in
        /// FounderStatueData.Statues of the first statue carved with the same English text. The record keeps its English.
        /// </summary>
        internal static string InscriptionText(FounderStatueData.FounderStatue statue)
        {
            var key = InscriptionKey(statue);
            return key == null ? statue.Inscription : Loc.Get(key);
        }

        /// <summary>v1.2.5: the key of a statue's inscription when its English is the stored text, else null.</summary>
        internal static string? InscriptionKey(FounderStatueData.FounderStatue statue)
        {
            int n = FounderStatueData.Statues.FindIndex(s => s.Inscription == statue.Inscription);
            if (n < 0) return null;
            string key = $"founder.inscription_{n}";
            return Loc.HasIn("en", key) && Loc.GetIn("en", key) == statue.Inscription ? key : null;
        }

        /// <summary>v1.2.5: the place heading of a statue list in the player's language.</summary>
        internal static string PlaceLabel(FounderStatueData.StatueLocationTag location) => Loc.Get(location switch
        {
            FounderStatueData.StatueLocationTag.Pantheon => "founder.place_pantheon",
            FounderStatueData.StatueLocationTag.Castle => "founder.place_castle",
            FounderStatueData.StatueLocationTag.MainStreetMini => "founder.place_main_street",
            _ => "founder.place_other"
        });

        /// <summary>v1.2.5: a statue's row in the list (after "  [n] "), in the player's language.</summary>
        internal static string ListRow(FounderStatueData.FounderStatue s)
        {
            string crackTag = s.IsCracked ? Loc.Get("founder.cracked_tag") : "";
            if (s.Location == FounderStatueData.StatueLocationTag.MainStreetMini)
                return $"{s.DisplayName}: {TagLabel(s.AchievementTag)}{crackTag}";
            return $"{s.DisplayName}: {Subtitle(s)}{crackTag}";
        }

        /// <summary>v1.2.5: the god name of a Pantheon statue, else its level and class in the player's language.</summary>
        internal static string Subtitle(FounderStatueData.FounderStatue s) =>
            !string.IsNullOrEmpty(s.DivineName) && s.Location == FounderStatueData.StatueLocationTag.Pantheon
                ? s.DivineName
                : Loc.Get("founder.level_class", s.FinalLevel, ClassLabel(s.ClassName));

        /// <summary>v1.2.5: a statue's stored ending tag in the player's language; another one as stored.</summary>
        internal static string EndingLabel(string stored) => stored switch
        {
            "Savior" => Loc.Get("founder.ending_savior"),
            "Usurper" => Loc.Get("founder.ending_usurper"),
            "Multiple" => Loc.Get("founder.ending_multiple"),
            "Pre-NG+" => Loc.Get("founder.ending_pre_ng"),
            "Lost" => Loc.Get("founder.ending_lost"),
            _ => stored
        };

        private static void RenderStatueListText(
            TerminalEmulator terminal,
            FounderStatueData.StatueLocationTag location,
            List<FounderStatueData.FounderStatue> statues)
        {
            terminal.WriteLine("");
            string locationLabel = PlaceLabel(location);

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
                string color = s.IsCracked ? "dark_gray" : "white";
                // Mini plinths render compactly (the plaque header instead of level and class)
                terminal.Write($"  [{i + 1}] ", "bright_yellow");
                terminal.WriteLine(ListRow(s), color);
            }

            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("founder.commemorated", FounderStatueData.GetUniqueFounderCount()), "gray");
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
                    // v1.2.5: shown text in the player's language; the record itself stays English
                    className = ClassLabel(statue.ClassName),
                    raceName = RaceLabel(statue.RaceName),
                    finalLevel = statue.FinalLevel,
                    cycleReached = statue.CycleReached,
                    endingTag = EndingLabel(statue.EndingTag),
                    achievementTag = TagLabel(statue.AchievementTag),
                    inscription = InscriptionText(statue),
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
            terminal.WriteLine($"  === {TagLabel(statue.AchievementTag)} ===", color);
            terminal.WriteLine("");

            // Character info line
            terminal.SetColor("white");
            string identity = !string.IsNullOrEmpty(statue.DivineName) && statue.Location == FounderStatueData.StatueLocationTag.Pantheon
                ? Loc.Get("founder.known_to_heavens", statue.DisplayName, statue.DivineName)
                : $"  {statue.DisplayName}";
            terminal.WriteLine(identity);

            if (statue.FinalLevel > 0 && statue.ClassName != "Unknown")
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("founder.record_line", statue.FinalLevel, RaceLabel(statue.RaceName), ClassLabel(statue.ClassName),
                    statue.CycleReached, EndingLabel(statue.EndingTag)));
            }
            else if (statue.IsCracked)
            {
                terminal.SetColor("dark_gray");
                terminal.WriteLine(Loc.Get("founder.record_lost"));
            }
            terminal.WriteLine("");

            // Inscription — the carved-stone narrative
            terminal.SetColor(statue.IsCracked ? "dark_gray" : "cyan");
            foreach (var row in InscriptionRows(InscriptionText(statue), 72))
                terminal.WriteLine(row);
            terminal.WriteLine("");

            await terminal.PressAnyKey();
        }

        private static void RenderStatueArt(TerminalEmulator terminal, FounderStatueData.FounderStatue statue)
        {
            if (GameConfig.ScreenReaderMode || GameConfig.DisableCharacterMonsterArt)
            {
                terminal.WriteLine(Loc.Get("founder.statue_art_sr"));
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

        /// <summary>
        /// v1.2.5: an inscription as quoted, indented rows (the rows WriteWrappedItalic wrote, now returned so they
        /// can be measured).
        /// </summary>
        internal static List<string> InscriptionRows(string text, int width)
        {
            // Simple word-wrap. Render as italic-style indented narrative.
            var rows = new List<string>();
            var words = text.Split(' ');
            var line = new System.Text.StringBuilder("    \"");
            foreach (var w in words)
            {
                if (line.Length + w.Length + 1 > width + 4)
                {
                    rows.Add(line.ToString());
                    line.Clear();
                    line.Append("     ");
                }
                if (line.Length > 5) line.Append(' ');
                line.Append(w);
            }
            if (line.Length > 5)
            {
                line.Append('"');
                rows.Add(line.ToString());
            }
            return rows;
        }

        private static void EmitStatueListToElectron(
            FounderStatueData.StatueLocationTag location,
            List<FounderStatueData.FounderStatue> statues)
        {
            ElectronBridge.Emit("statue_list", new
            {
                // v1.2.5: the terminal's heading and labels, in the player's language
                locationLabel = PlaceLabel(location),
                statues = statues.Select((s, i) => new
                {
                    key = (i + 1).ToString(),
                    displayName = s.DisplayName,
                    subtitle = Subtitle(s),
                    achievementTag = TagLabel(s.AchievementTag),
                    isCracked = s.IsCracked
                }).ToList(),
                totalFounders = FounderStatueData.GetUniqueFounderCount()
            });
        }
    }
}
