using System.Collections.Generic;
using System.Text;
using UsurperRemake.Systems;

namespace UsurperRemake.UI
{
    /// <summary>
    /// Canvas-based Old God boss art for dramatic boss introductions.
    /// Each god gets a unique 40x16 piece rendered with block characters and
    /// multi-color shading for maximum visual impact during animated reveal.
    /// </summary>
    public static class OldGodArtDatabase
    {
        private const int W = 40;
        private const int H = 16;

        private struct Cell { public char Ch; public string Color; }

        private static void Plot(Cell[,] c, int x, int y, char ch, string color)
        {
            if (x >= 0 && x < W && y >= 0 && y < H)
                c[y, x] = new Cell { Ch = ch, Color = color };
        }

        private static void HLine(Cell[,] c, int x1, int x2, int y, char ch, string color)
        {
            for (int x = x1; x <= x2; x++) Plot(c, x, y, ch, color);
        }

        private static void VLine(Cell[,] c, int x, int y1, int y2, char ch, string color)
        {
            for (int y = y1; y <= y2; y++) Plot(c, x, y, ch, color);
        }

        private static void FillRect(Cell[,] c, int x1, int y1, int x2, int y2, char ch, string color)
        {
            for (int y = y1; y <= y2; y++)
                for (int x = x1; x <= x2; x++)
                    Plot(c, x, y, ch, color);
        }

        /// <summary>
        /// Draw a filled diamond centered at (cx, cy) with given radius.
        /// </summary>
        private static void Diamond(Cell[,] c, int cx, int cy, int radius, char ch, string color)
        {
            for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -(radius - System.Math.Abs(dy)); dx <= radius - System.Math.Abs(dy); dx++)
                    Plot(c, cx + dx, cy + dy, ch, color);
        }

        public static string[]? GetArtForGod(OldGodType godType)
        {
            if (!Builders.ContainsKey(godType)) return null;

            var canvas = new Cell[H, W];
            Builders[godType](canvas);
            return Render(canvas, Taglines[godType]);
        }

        private static string[] Render(Cell[,] canvas, string tagline)
        {
            var lines = new List<string>();
            for (int y = 0; y < H; y++)
            {
                var sb = new StringBuilder();
                string cur = "";
                for (int x = 0; x < W; x++)
                {
                    var cell = canvas[y, x];
                    char ch = cell.Ch == '\0' ? ' ' : cell.Ch;
                    string color = cell.Color ?? "";
                    if (ch != ' ' && color.Length > 0 && color != cur)
                    {
                        sb.Append($"[{color}]");
                        cur = color;
                    }
                    sb.Append(ch);
                }
                lines.Add("    " + sb.ToString()); // 4-char indent
            }
            lines.Add("    " + tagline);
            lines.Add("[/]");
            return lines.ToArray();
        }

        private delegate void DrawFunc(Cell[,] c);

        private static readonly Dictionary<OldGodType, DrawFunc> Builders = new()
        {
            [OldGodType.Maelketh] = DrawMaelketh,
            [OldGodType.Veloura] = DrawVeloura,
            [OldGodType.Thorgrim] = DrawThorgrim,
            [OldGodType.Noctura] = DrawNoctura,
            [OldGodType.Aurelion] = DrawAurelion,
            [OldGodType.Terravok] = DrawTerravok,
            [OldGodType.Manwe] = DrawManwe,
        };

        private static readonly Dictionary<OldGodType, string> Taglines = new()
        {
            [OldGodType.Maelketh] = "[red]~~ [bright_yellow]E T E R N A L   W A R[red] ~~",
            [OldGodType.Veloura] = "[magenta]~~ [bright_magenta]L O V E ' S   D E C A Y[magenta] ~~",
            [OldGodType.Thorgrim] = "[white]~~ [bright_yellow]J U D G M E N T   F A L L S[white] ~~",
            [OldGodType.Noctura] = "[dark_magenta]~~ [magenta]S H A D O W S   S H I F T[dark_magenta] ~~",
            [OldGodType.Aurelion] = "[bright_yellow]~~ [yellow]T H E   L I G H T   F A D E S[bright_yellow] ~~",
            [OldGodType.Terravok] = "[bright_green]~~ [green]T H E   E A R T H   W A K E S[bright_green] ~~",
            [OldGodType.Manwe] = "[bright_cyan]~~ [white]T H E   C R E A T O R   S E E S[bright_cyan] ~~",
        };

        #region Old God Drawings

        /// <summary>
        /// MAELKETH — God of War, Corrupted
        /// Massive armored warrior with a flaming greatsword held overhead.
        /// Heavy helm with glowing eye slits, broad pauldrons, flames rising.
        /// </summary>
        private static void DrawMaelketh(Cell[,] c)
        {
            string r = "red", br = "bright_red", y = "bright_yellow", dy = "yellow";

            // === Flames above sword (rows 0-1) ===
            Plot(c, 17, 0, '░', y); Plot(c, 19, 0, '░', dy); Plot(c, 21, 0, '░', y);
            Plot(c, 23, 0, '░', dy);
            Plot(c, 16, 1, '▒', y); Plot(c, 18, 1, '░', dy); Plot(c, 20, 1, '▒', y);
            Plot(c, 22, 1, '░', dy); Plot(c, 24, 1, '▒', y);

            // === Greatsword blade (rows 2-4, held overhead) ===
            HLine(c, 14, 26, 2, '█', br);
            Plot(c, 13, 2, '▓', r); Plot(c, 27, 2, '▓', r);
            HLine(c, 15, 25, 3, '▓', br);
            Plot(c, 20, 3, '█', y); // blade highlight
            HLine(c, 17, 23, 4, '▒', r);
            Plot(c, 20, 4, '▓', y); // blade center

            // === Crossguard ===
            HLine(c, 16, 24, 5, '█', dy);
            Plot(c, 15, 5, '▓', dy); Plot(c, 25, 5, '▓', dy);

            // === Helm (rows 5-7) ===
            HLine(c, 17, 23, 5, '▄', r);
            Plot(c, 16, 6, '█', r); HLine(c, 17, 23, 6, '▓', br); Plot(c, 24, 6, '█', r);
            // Eye slits
            Plot(c, 18, 6, '█', y); Plot(c, 19, 6, '█', y);
            Plot(c, 21, 6, '█', y); Plot(c, 22, 6, '█', y);
            // Face guard
            Plot(c, 16, 7, '█', r); HLine(c, 17, 23, 7, '▒', r); Plot(c, 24, 7, '█', r);
            HLine(c, 18, 22, 7, '▓', br); // visor slats

            // === Pauldrons (rows 8-9) ===
            // Left pauldron
            FillRect(c, 10, 8, 15, 9, '▓', r);
            Plot(c, 10, 8, '█', br); Plot(c, 11, 8, '█', br);
            Plot(c, 10, 9, '█', br);
            // Right pauldron
            FillRect(c, 25, 8, 30, 9, '▓', r);
            Plot(c, 29, 8, '█', br); Plot(c, 30, 8, '█', br);
            Plot(c, 30, 9, '█', br);
            // Neck
            HLine(c, 17, 23, 8, '▀', r);

            // === Chest armor (rows 9-12) ===
            FillRect(c, 15, 9, 25, 12, '▒', r);
            // Outline
            VLine(c, 14, 9, 12, '█', br); VLine(c, 26, 9, 12, '█', br);
            // Chest emblem (war skull)
            Plot(c, 19, 10, '▓', y); Plot(c, 20, 10, '█', y); Plot(c, 21, 10, '▓', y);
            Plot(c, 19, 11, '▒', y); Plot(c, 20, 11, '▓', y); Plot(c, 21, 11, '▒', y);

            // === Arms ===
            // Left arm
            VLine(c, 12, 9, 12, '▓', r); VLine(c, 13, 9, 12, '▒', br);
            Plot(c, 11, 10, '▓', r); Plot(c, 11, 11, '▒', r);
            Plot(c, 10, 12, '░', r); // fist
            // Right arm
            VLine(c, 27, 9, 12, '▒', br); VLine(c, 28, 9, 12, '▓', r);
            Plot(c, 29, 10, '▓', r); Plot(c, 29, 11, '▒', r);
            Plot(c, 30, 12, '░', r); // fist

            // === Leg armor (rows 13-15) ===
            FillRect(c, 16, 13, 19, 15, '▒', r);
            VLine(c, 15, 13, 15, '█', br); VLine(c, 20, 13, 15, '█', br);
            FillRect(c, 21, 13, 24, 15, '▒', r);
            VLine(c, 21, 13, 15, '█', br); VLine(c, 25, 13, 15, '█', br);

            // === Ground fire (row 15) ===
            Plot(c, 8, 15, '░', y); Plot(c, 10, 15, '░', dy);
            Plot(c, 12, 14, '░', y); Plot(c, 13, 15, '▒', dy);
            Plot(c, 27, 14, '░', y); Plot(c, 28, 15, '▒', dy);
            Plot(c, 30, 15, '░', y); Plot(c, 32, 15, '░', dy);
        }

        /// <summary>
        /// VELOURA — Goddess of Love, Dying
        /// A crumbling feminine figure with a massive cracked heart behind her,
        /// thorny vines wrapping around, petals falling.
        /// </summary>
        private static void DrawVeloura(Cell[,] c)
        {
            string m = "magenta", bm = "bright_magenta", r = "red", g = "green", w = "white";

            // === Giant heart silhouette (background, rows 0-9) ===
            // Top lobes
            HLine(c, 10, 14, 0, '░', m); HLine(c, 25, 29, 0, '░', m);
            HLine(c, 8, 16, 1, '▒', m); HLine(c, 23, 31, 1, '▒', m);
            HLine(c, 7, 17, 2, '▓', m); HLine(c, 22, 32, 2, '▓', m);
            HLine(c, 7, 32, 3, '▒', m);
            HLine(c, 8, 31, 4, '▒', m);
            HLine(c, 9, 30, 5, '░', m);
            HLine(c, 10, 29, 6, '░', m);
            HLine(c, 12, 27, 7, '░', m);
            HLine(c, 14, 25, 8, '░', m);
            HLine(c, 16, 23, 9, '░', m);

            // Cracks in the heart
            Plot(c, 18, 2, '░', r); Plot(c, 19, 3, '▒', r); Plot(c, 20, 4, '░', r);
            Plot(c, 21, 3, '░', r); Plot(c, 22, 2, '░', r);
            Plot(c, 14, 4, '░', r); Plot(c, 13, 5, '░', r);
            Plot(c, 26, 5, '░', r); Plot(c, 27, 4, '░', r);

            // === Veloura figure (overlaid, rows 3-13) ===
            // Head
            HLine(c, 18, 21, 3, '▄', bm);
            Plot(c, 17, 4, '█', bm); HLine(c, 18, 21, 4, '▒', w); Plot(c, 22, 4, '█', bm);
            Plot(c, 19, 4, '▓', bm); Plot(c, 21, 4, '▓', bm); // closed eyes (weeping)
            Plot(c, 17, 5, '█', bm); HLine(c, 18, 21, 5, '░', w); Plot(c, 22, 5, '█', bm);
            HLine(c, 18, 21, 6, '▀', bm);

            // Hair flowing down
            Plot(c, 16, 4, '▓', bm); Plot(c, 15, 5, '▒', bm); Plot(c, 14, 6, '░', bm);
            Plot(c, 23, 4, '▓', bm); Plot(c, 24, 5, '▒', bm); Plot(c, 25, 6, '░', bm);
            Plot(c, 15, 6, '▒', bm); Plot(c, 24, 6, '▒', bm);

            // Neck + body
            VLine(c, 19, 7, 7, '▒', w); VLine(c, 20, 7, 7, '▒', w);
            // Shoulders + torso
            FillRect(c, 17, 8, 22, 11, '▒', bm);
            Plot(c, 16, 8, '█', bm); Plot(c, 23, 8, '█', bm);
            VLine(c, 16, 9, 11, '█', bm); VLine(c, 23, 9, 11, '█', bm);

            // Arms reaching out
            Plot(c, 14, 8, '░', bm); Plot(c, 15, 8, '▒', bm);
            Plot(c, 13, 9, '░', bm); Plot(c, 14, 9, '▒', bm);
            Plot(c, 25, 8, '▒', bm); Plot(c, 26, 8, '░', bm);
            Plot(c, 26, 9, '▒', bm); Plot(c, 27, 9, '░', bm);

            // Flowing skirt (rows 12-14)
            FillRect(c, 15, 12, 24, 13, '░', bm);
            Plot(c, 14, 12, '░', m); Plot(c, 25, 12, '░', m);
            FillRect(c, 13, 14, 26, 14, '░', m);
            Plot(c, 12, 14, '░', m); Plot(c, 27, 14, '░', m);

            // === Thorny vines wrapping around ===
            Plot(c, 12, 8, '▓', g); Plot(c, 11, 9, '█', g); Plot(c, 11, 10, '▓', g);
            Plot(c, 12, 11, '▒', g); Plot(c, 13, 12, '░', g);
            Plot(c, 28, 8, '▓', g); Plot(c, 29, 9, '█', g); Plot(c, 29, 10, '▓', g);
            Plot(c, 28, 11, '▒', g); Plot(c, 27, 12, '░', g);

            // Thorns
            Plot(c, 10, 9, '░', g); Plot(c, 12, 10, '░', g);
            Plot(c, 30, 9, '░', g); Plot(c, 28, 10, '░', g);

            // === Falling petals ===
            Plot(c, 9, 11, '░', bm); Plot(c, 31, 10, '░', bm);
            Plot(c, 7, 13, '░', bm); Plot(c, 33, 12, '░', bm);
            Plot(c, 11, 14, '░', r); Plot(c, 29, 13, '░', r);
            Plot(c, 6, 15, '░', m); Plot(c, 34, 15, '░', m);
            Plot(c, 10, 15, '░', r); Plot(c, 30, 15, '░', r);
        }

        /// <summary>
        /// THORGRIM — God of Law, Corrupted
        /// Towering robed figure holding massive scales of justice.
        /// Chains hang from each scale pan. Cold, impassive mask face.
        /// </summary>
        private static void DrawThorgrim(Cell[,] c)
        {
            string w = "white", g = "gray", by = "bright_yellow", y = "yellow";

            // === Scales beam (row 0-1) ===
            HLine(c, 5, 35, 0, '▄', g);
            HLine(c, 5, 35, 1, '▀', g);
            Plot(c, 20, 0, '█', by); // pivot
            Plot(c, 20, 1, '█', by);

            // === Left scale pan (rows 2-5) ===
            VLine(c, 8, 1, 3, '▒', g); // chain
            HLine(c, 5, 11, 3, '▄', g);
            FillRect(c, 5, 4, 11, 4, '▓', g);
            HLine(c, 5, 11, 5, '▀', g);
            // Skull in left pan (corrupted justice)
            Plot(c, 7, 4, '░', w); Plot(c, 8, 4, '█', w); Plot(c, 9, 4, '░', w);

            // === Right scale pan (rows 2-5) ===
            VLine(c, 32, 1, 3, '▒', g);
            HLine(c, 29, 35, 3, '▄', g);
            FillRect(c, 29, 4, 35, 4, '▓', g);
            HLine(c, 29, 35, 5, '▀', g);
            // Sword in right pan
            Plot(c, 31, 4, '░', by); Plot(c, 32, 4, '█', by); Plot(c, 33, 4, '░', by);

            // === Thorgrim figure (center, rows 2-15) ===
            // Mask-like face (rows 2-5)
            HLine(c, 17, 23, 2, '▄', w);
            Plot(c, 16, 3, '█', w); HLine(c, 17, 23, 3, '▓', w); Plot(c, 24, 3, '█', w);
            // Eyes (cold, unblinking)
            Plot(c, 18, 3, '█', by); Plot(c, 19, 3, '█', by);
            Plot(c, 21, 3, '█', by); Plot(c, 22, 3, '█', by);
            Plot(c, 16, 4, '█', w); HLine(c, 17, 23, 4, '▒', w); Plot(c, 24, 4, '█', w);
            // Stern mouth line
            HLine(c, 18, 22, 4, '▓', g);
            HLine(c, 17, 23, 5, '▀', w);

            // === Robed body (rows 6-14) ===
            // Expanding robe silhouette
            FillRect(c, 16, 6, 24, 7, '▒', w);
            VLine(c, 15, 6, 7, '█', g); VLine(c, 25, 6, 7, '█', g);
            FillRect(c, 14, 8, 26, 10, '▒', w);
            VLine(c, 13, 8, 10, '█', g); VLine(c, 27, 8, 10, '█', g);
            FillRect(c, 13, 11, 27, 13, '░', w);
            VLine(c, 12, 11, 13, '█', g); VLine(c, 28, 11, 13, '█', g);
            FillRect(c, 11, 14, 29, 15, '░', g);
            Plot(c, 10, 14, '░', g); Plot(c, 30, 14, '░', g);

            // === Chains hanging from robe ===
            VLine(c, 15, 8, 12, '▒', g);
            VLine(c, 25, 8, 12, '▒', g);
            Plot(c, 15, 13, '░', g); Plot(c, 25, 13, '░', g);

            // === Robe emblem (scales of justice) ===
            Plot(c, 19, 9, '▓', by); Plot(c, 20, 8, '█', by); Plot(c, 21, 9, '▓', by);
            Plot(c, 20, 9, '█', by);
            Plot(c, 18, 10, '░', by); Plot(c, 22, 10, '░', by);

            // === Arms extending to hold scales (rows 6-7) ===
            // Left arm reaching to left scale
            HLine(c, 11, 15, 6, '▓', w);
            HLine(c, 9, 12, 7, '▒', g);
            // Right arm reaching to right scale
            HLine(c, 25, 29, 6, '▓', w);
            HLine(c, 28, 31, 7, '▒', g);
        }

        /// <summary>
        /// NOCTURA — Goddess of Shadow, Neutral
        /// Two overlapping faces emerging from darkness — one light, one dark.
        /// Shifting shadow tendrils, multiple watching eyes scattered.
        /// </summary>
        private static void DrawNoctura(Cell[,] c)
        {
            string dm = "dark_magenta", m = "magenta", bm = "bright_magenta", w = "white", g = "gray";

            // === Scattered shadow particles (atmosphere) ===
            Plot(c, 3, 0, '░', dm); Plot(c, 7, 1, '░', dm); Plot(c, 36, 0, '░', dm);
            Plot(c, 33, 2, '░', dm); Plot(c, 5, 4, '░', dm); Plot(c, 35, 5, '░', dm);
            Plot(c, 2, 7, '░', dm); Plot(c, 37, 8, '░', dm);
            Plot(c, 4, 11, '░', dm); Plot(c, 36, 12, '░', dm);

            // === Left face (light aspect, rows 1-7) ===
            HLine(c, 10, 16, 1, '▄', m);
            Plot(c, 9, 2, '█', m); HLine(c, 10, 16, 2, '▒', bm); Plot(c, 17, 2, '█', m);
            Plot(c, 9, 3, '█', m); HLine(c, 10, 16, 3, '▒', bm); Plot(c, 17, 3, '█', m);
            Plot(c, 9, 4, '█', m); HLine(c, 10, 16, 4, '░', bm); Plot(c, 17, 4, '█', m);
            Plot(c, 9, 5, '█', m); HLine(c, 10, 16, 5, '░', bm); Plot(c, 17, 5, '█', m);
            HLine(c, 10, 16, 6, '▀', m);
            // Left face eyes (gentle)
            Plot(c, 12, 3, '█', w); Plot(c, 14, 3, '█', w);
            // Left face mouth (serene)
            Plot(c, 12, 5, '▒', m); Plot(c, 13, 5, '▓', m); Plot(c, 14, 5, '▒', m);

            // === Right face (dark aspect, rows 3-9, overlapping) ===
            HLine(c, 22, 30, 3, '▄', dm);
            Plot(c, 21, 4, '█', dm); HLine(c, 22, 30, 4, '▓', dm); Plot(c, 31, 4, '█', dm);
            Plot(c, 21, 5, '█', dm); HLine(c, 22, 30, 5, '▓', dm); Plot(c, 31, 5, '█', dm);
            Plot(c, 21, 6, '█', dm); HLine(c, 22, 30, 6, '▒', dm); Plot(c, 31, 6, '█', dm);
            Plot(c, 21, 7, '█', dm); HLine(c, 22, 30, 7, '▒', dm); Plot(c, 31, 7, '█', dm);
            HLine(c, 22, 30, 8, '▀', dm);
            // Right face eyes (sinister, glowing)
            Plot(c, 24, 5, '█', "red"); Plot(c, 28, 5, '█', "red");
            // Right face mouth (twisted grin)
            Plot(c, 24, 7, '▒', m); Plot(c, 25, 7, '▓', m); Plot(c, 26, 7, '█', m);
            Plot(c, 27, 7, '▓', m); Plot(c, 28, 7, '▒', m);

            // === Overlap zone — faces merging (rows 4-6, cols 17-22) ===
            FillRect(c, 17, 4, 21, 6, '▒', m);
            Plot(c, 18, 4, '▓', dm); Plot(c, 19, 5, '█', bm); Plot(c, 20, 4, '▓', m);

            // === Watching eyes in darkness ===
            Plot(c, 6, 3, '█', "red"); Plot(c, 34, 4, '█', "red");
            Plot(c, 8, 8, '█', bm); Plot(c, 32, 9, '█', bm);
            Plot(c, 4, 6, '█', "red");

            // === Shadow tendrils below (rows 9-15) ===
            // Merging body of shadow
            HLine(c, 12, 28, 9, '▒', dm);
            HLine(c, 10, 30, 10, '▓', dm);
            FillRect(c, 9, 11, 31, 12, '▒', dm);
            HLine(c, 8, 32, 13, '░', dm);

            // Tendrils reaching out
            Plot(c, 6, 13, '░', dm); Plot(c, 7, 12, '▒', dm);
            Plot(c, 34, 13, '░', dm); Plot(c, 33, 12, '▒', dm);
            Plot(c, 5, 14, '░', dm); Plot(c, 8, 14, '░', m);
            Plot(c, 35, 14, '░', dm); Plot(c, 32, 14, '░', m);
            Plot(c, 10, 15, '░', dm); Plot(c, 14, 15, '░', dm);
            Plot(c, 20, 15, '░', dm); Plot(c, 26, 15, '░', dm);
            Plot(c, 30, 15, '░', dm);
        }

        /// <summary>
        /// AURELION — God of Light, Dying
        /// A radiant figure with a cracked and dimming halo overhead.
        /// Rays of light stream outward but fade. Cracks run through the body.
        /// Stars and sparkles falling like tears.
        /// </summary>
        private static void DrawAurelion(Cell[,] c)
        {
            string by = "bright_yellow", y = "yellow", dy = "yellow", w = "white", g = "gray";

            // === Cracked halo (rows 0-2) ===
            HLine(c, 15, 25, 0, '░', by);
            Plot(c, 13, 0, '░', dy); Plot(c, 27, 0, '░', dy);
            HLine(c, 14, 26, 1, '▒', by);
            Plot(c, 13, 1, '▓', y); Plot(c, 27, 1, '▓', y);
            // Cracks in halo
            Plot(c, 17, 1, '░', g); Plot(c, 23, 0, '░', g); // dim spots

            // === Radiating light beams (rows 1-3) ===
            // Upper-left rays
            Plot(c, 10, 1, '░', y); Plot(c, 8, 2, '░', dy);
            Plot(c, 6, 3, '░', y);
            // Upper-right rays
            Plot(c, 30, 1, '░', y); Plot(c, 32, 2, '░', dy);
            Plot(c, 34, 3, '░', y);

            // === Head (rows 2-5) ===
            HLine(c, 17, 23, 2, '▄', y);
            Plot(c, 16, 3, '█', y); HLine(c, 17, 23, 3, '▓', by); Plot(c, 24, 3, '█', y);
            // Eyes (radiant white)
            Plot(c, 18, 3, '█', w); Plot(c, 19, 3, '█', w);
            Plot(c, 21, 3, '█', w); Plot(c, 22, 3, '█', w);
            Plot(c, 16, 4, '█', y); HLine(c, 17, 23, 4, '▒', by); Plot(c, 24, 4, '█', y);
            HLine(c, 17, 23, 5, '▀', y);

            // === Robed body (rows 6-12) ===
            FillRect(c, 15, 6, 25, 8, '▒', by);
            VLine(c, 14, 6, 8, '█', y); VLine(c, 26, 6, 8, '█', y);
            FillRect(c, 14, 9, 26, 11, '░', by);
            VLine(c, 13, 9, 11, '█', y); VLine(c, 27, 9, 11, '█', y);
            FillRect(c, 12, 12, 28, 13, '░', y);
            Plot(c, 11, 12, '░', dy); Plot(c, 29, 12, '░', dy);

            // === Cracks through body (fading) ===
            Plot(c, 19, 7, '░', g); Plot(c, 20, 8, '░', g); Plot(c, 19, 9, '░', g);
            Plot(c, 22, 7, '░', g); Plot(c, 23, 9, '░', g);
            Plot(c, 17, 10, '░', g); Plot(c, 25, 10, '░', g);

            // === Arms outstretched, light dimming ===
            // Left arm
            HLine(c, 9, 14, 7, '▒', by);
            Plot(c, 8, 7, '░', y); Plot(c, 7, 7, '░', dy);
            // Right arm
            HLine(c, 26, 31, 7, '▒', by);
            Plot(c, 32, 7, '░', y); Plot(c, 33, 7, '░', dy);

            // === Falling stars / tears of light (rows 13-15) ===
            Plot(c, 10, 13, '░', by); Plot(c, 15, 14, '░', y);
            Plot(c, 30, 13, '░', by); Plot(c, 25, 14, '░', y);
            Plot(c, 8, 14, '░', dy); Plot(c, 32, 14, '░', dy);
            Plot(c, 12, 15, '░', y); Plot(c, 18, 15, '░', dy);
            Plot(c, 22, 15, '░', dy); Plot(c, 28, 15, '░', y);
            Plot(c, 5, 15, '░', g); Plot(c, 35, 15, '░', g); // fully dimmed
        }

        /// <summary>
        /// TERRAVOK — God of Earth, Dormant
        /// A massive mountain-shaped figure with crystal eyes.
        /// Stone layers, visible geological strata, roots at base.
        /// Crystals emerging from shoulders and head.
        /// </summary>
        private static void DrawTerravok(Cell[,] c)
        {
            string bg = "bright_green", g = "green", y = "yellow", dy = "yellow", br = "bright_yellow";

            // === Mountain peak / crown (rows 0-3) ===
            Plot(c, 20, 0, '▄', g);
            Plot(c, 19, 1, '█', g); Plot(c, 20, 1, '▓', bg); Plot(c, 21, 1, '█', g);
            HLine(c, 18, 22, 2, '▓', g);
            Plot(c, 17, 2, '█', g); Plot(c, 23, 2, '█', g);
            HLine(c, 16, 24, 3, '▒', g);
            Plot(c, 15, 3, '█', g); Plot(c, 25, 3, '█', g);

            // === Crystal protrusions from head ===
            Plot(c, 16, 0, '░', br); Plot(c, 17, 1, '▒', br);
            Plot(c, 24, 0, '░', br); Plot(c, 23, 1, '▒', br);

            // === Face (rows 4-6) ===
            HLine(c, 14, 26, 4, '▒', bg);
            VLine(c, 13, 4, 6, '█', g); VLine(c, 27, 4, 6, '█', g);
            HLine(c, 14, 26, 5, '▓', g);
            HLine(c, 14, 26, 6, '▒', g);
            // Crystal eyes (bright, gemlike)
            Plot(c, 17, 4, '█', br); Plot(c, 18, 4, '█', br);
            Plot(c, 22, 4, '█', br); Plot(c, 23, 4, '█', br);
            // Stone mouth
            HLine(c, 18, 22, 6, '▓', bg);

            // === Massive body / mountain torso (rows 7-11) ===
            FillRect(c, 11, 7, 29, 8, '▒', g);
            VLine(c, 10, 7, 8, '█', g); VLine(c, 30, 7, 8, '█', g);
            FillRect(c, 9, 9, 31, 10, '▓', g);
            VLine(c, 8, 9, 10, '█', g); VLine(c, 32, 9, 10, '█', g);
            FillRect(c, 7, 11, 33, 12, '▒', g);
            VLine(c, 6, 11, 12, '█', g); VLine(c, 34, 11, 12, '█', g);

            // === Geological strata (color banding) ===
            HLine(c, 12, 28, 8, '░', bg);
            HLine(c, 10, 30, 10, '░', bg);
            HLine(c, 8, 32, 12, '░', bg);

            // === Crystal shoulder clusters ===
            // Left shoulder
            Plot(c, 8, 7, '▓', br); Plot(c, 9, 7, '█', br); Plot(c, 7, 8, '░', br);
            Plot(c, 8, 6, '░', br);
            // Right shoulder
            Plot(c, 31, 7, '█', br); Plot(c, 32, 7, '▓', br); Plot(c, 33, 8, '░', br);
            Plot(c, 32, 6, '░', br);

            // === Arms (stone pillars) ===
            VLine(c, 9, 8, 10, '▓', g);
            VLine(c, 31, 8, 10, '▓', g);
            Plot(c, 8, 10, '░', g); Plot(c, 32, 10, '░', g);

            // === Base / roots (rows 13-15) ===
            FillRect(c, 5, 13, 35, 14, '░', g);
            Plot(c, 4, 13, '░', g); Plot(c, 36, 13, '░', g);
            // Roots spreading
            Plot(c, 3, 14, '░', g); Plot(c, 37, 14, '░', g);
            Plot(c, 2, 15, '░', g); Plot(c, 6, 15, '░', bg);
            Plot(c, 10, 15, '░', g); Plot(c, 15, 15, '░', bg);
            Plot(c, 25, 15, '░', bg); Plot(c, 30, 15, '░', g);
            Plot(c, 34, 15, '░', bg); Plot(c, 38, 15, '░', g);

            // === Emblem: earth rune on chest ===
            Plot(c, 19, 9, '█', br); Plot(c, 20, 9, '█', br); Plot(c, 21, 9, '█', br);
            Plot(c, 20, 10, '█', br);
            Plot(c, 19, 11, '▒', br); Plot(c, 20, 11, '█', br); Plot(c, 21, 11, '▒', br);
        }

        /// <summary>
        /// MANWE — God of Creation, Final Boss
        /// A massive cosmic eye surrounded by a starfield.
        /// Concentric rings of energy radiate outward. Stars scattered throughout.
        /// The most dramatic piece — this is the final encounter.
        /// </summary>
        private static void DrawManwe(Cell[,] c)
        {
            string bc = "bright_cyan", cy = "cyan", w = "white", by = "bright_yellow", y = "yellow";

            // === Starfield background ===
            Plot(c, 2, 0, '░', w); Plot(c, 8, 0, '░', cy); Plot(c, 15, 0, '░', w);
            Plot(c, 25, 0, '░', w); Plot(c, 31, 0, '░', cy); Plot(c, 37, 0, '░', w);
            Plot(c, 5, 1, '░', cy); Plot(c, 35, 1, '░', cy);
            Plot(c, 1, 3, '░', w); Plot(c, 39, 3, '░', w);
            Plot(c, 3, 5, '░', cy); Plot(c, 37, 5, '░', cy);
            Plot(c, 0, 8, '░', w); Plot(c, 39, 7, '░', w);
            Plot(c, 2, 10, '░', cy); Plot(c, 38, 10, '░', cy);
            Plot(c, 4, 13, '░', w); Plot(c, 36, 13, '░', w);
            Plot(c, 1, 15, '░', cy); Plot(c, 10, 15, '░', w);
            Plot(c, 30, 15, '░', w); Plot(c, 38, 15, '░', cy);

            // === Outer energy ring (rows 2-13) ===
            HLine(c, 13, 27, 1, '░', cy);
            HLine(c, 10, 30, 2, '▒', cy);
            Plot(c, 9, 3, '▒', cy); Plot(c, 31, 3, '▒', cy);
            Plot(c, 8, 4, '▒', cy); Plot(c, 32, 4, '▒', cy);
            Plot(c, 7, 5, '▒', cy); Plot(c, 33, 5, '▒', cy);
            VLine(c, 6, 6, 9, '▒', cy); VLine(c, 34, 6, 9, '▒', cy);
            Plot(c, 7, 10, '▒', cy); Plot(c, 33, 10, '▒', cy);
            Plot(c, 8, 11, '▒', cy); Plot(c, 32, 11, '▒', cy);
            Plot(c, 9, 12, '▒', cy); Plot(c, 31, 12, '▒', cy);
            HLine(c, 10, 30, 13, '▒', cy);
            HLine(c, 13, 27, 14, '░', cy);

            // === Inner energy ring (rows 3-12) ===
            HLine(c, 14, 26, 3, '▓', bc);
            Plot(c, 12, 4, '▓', bc); Plot(c, 28, 4, '▓', bc);
            Plot(c, 11, 5, '▓', bc); Plot(c, 29, 5, '▓', bc);
            VLine(c, 10, 6, 9, '▓', bc); VLine(c, 30, 6, 9, '▓', bc);
            Plot(c, 11, 10, '▓', bc); Plot(c, 29, 10, '▓', bc);
            Plot(c, 12, 11, '▓', bc); Plot(c, 28, 11, '▓', bc);
            HLine(c, 14, 26, 12, '▓', bc);

            // === Cosmic Eye (rows 4-11) — the centerpiece ===
            // Upper eyelid curve
            HLine(c, 15, 25, 4, '▄', w);
            Plot(c, 14, 5, '█', w); Plot(c, 26, 5, '█', w);
            HLine(c, 15, 25, 5, '▒', bc);

            // Eye interior (rows 6-9)
            FillRect(c, 13, 6, 27, 9, '░', bc);
            Plot(c, 12, 6, '█', w); Plot(c, 28, 6, '█', w);
            Plot(c, 12, 7, '█', w); Plot(c, 28, 7, '█', w);
            Plot(c, 12, 8, '█', w); Plot(c, 28, 8, '█', w);
            Plot(c, 12, 9, '█', w); Plot(c, 28, 9, '█', w);

            // Iris (bright ring)
            HLine(c, 17, 23, 5, '▓', bc);
            Plot(c, 16, 6, '▓', bc); Plot(c, 24, 6, '▓', bc);
            Plot(c, 15, 7, '▓', bc); Plot(c, 25, 7, '▓', bc);
            Plot(c, 15, 8, '▓', bc); Plot(c, 25, 8, '▓', bc);
            Plot(c, 16, 9, '▓', bc); Plot(c, 24, 9, '▓', bc);
            HLine(c, 17, 23, 10, '▓', bc);

            // Pupil (the Creator's gaze — golden)
            FillRect(c, 18, 6, 22, 9, '▒', by);
            Plot(c, 19, 7, '█', by); Plot(c, 20, 7, '█', by); Plot(c, 21, 7, '█', by);
            Plot(c, 19, 8, '█', by); Plot(c, 20, 8, '█', by); Plot(c, 21, 8, '█', by);
            // Central point of light
            Plot(c, 20, 7, '█', w); Plot(c, 20, 8, '█', w);

            // Lower eyelid curve
            HLine(c, 15, 25, 10, '▒', bc);
            Plot(c, 14, 10, '█', w); Plot(c, 26, 10, '█', w);
            HLine(c, 15, 25, 11, '▀', w);

            // === Energy wisps emanating (rows 14-15) ===
            Plot(c, 16, 14, '░', bc); Plot(c, 20, 14, '░', by); Plot(c, 24, 14, '░', bc);
            Plot(c, 14, 15, '░', cy); Plot(c, 18, 15, '░', bc);
            Plot(c, 22, 15, '░', bc); Plot(c, 26, 15, '░', cy);
        }

        #endregion
    }
}
