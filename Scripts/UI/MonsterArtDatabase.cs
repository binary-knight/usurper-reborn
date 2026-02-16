using System.Collections.Generic;
using System.Text;

namespace UsurperRemake.UI
{
    /// <summary>
    /// Canvas-based monster family silhouettes for combat introductions.
    /// Uses the same block-art approach as PortraitGenerator for consistent quality.
    /// Each family gets a distinctive silhouette built on a 30x10 grid.
    /// </summary>
    public static class MonsterArtDatabase
    {
        private const int W = 30;
        private const int H = 10;

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

        public static string[]? GetArtForFamily(string familyName)
        {
            if (string.IsNullOrEmpty(familyName)) return null;
            if (!Builders.ContainsKey(familyName)) return null;

            var canvas = new Cell[H, W];
            Builders[familyName](canvas);
            return Render(canvas);
        }

        private static string[] Render(Cell[,] canvas)
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
                lines.Add("      " + sb.ToString()); // 6-char indent for centering
            }
            lines.Add("[/]");
            return lines.ToArray();
        }

        private delegate void DrawFunc(Cell[,] c);

        private static readonly Dictionary<string, DrawFunc> Builders = new()
        {
            ["Goblinoid"] = DrawGoblinoid,
            ["Undead"] = DrawUndead,
            ["Orcish"] = DrawOrcish,
            ["Draconic"] = DrawDraconic,
            ["Demonic"] = DrawDemonic,
            ["Giant"] = DrawGiant,
            ["Beast"] = DrawBeast,
            ["Elemental"] = DrawElemental,
            ["Aberration"] = DrawAberration,
            ["Insectoid"] = DrawInsectoid,
            ["Construct"] = DrawConstruct,
            ["Fey"] = DrawFey,
            ["Aquatic"] = DrawAquatic,
            ["Celestial"] = DrawCelestial,
            ["Shadow"] = DrawShadow,
        };

        #region Family Drawings

        private static void DrawGoblinoid(Cell[,] c)
        {
            string g = "green", bg = "bright_green";
            // Ears
            Plot(c, 12, 1, '▄', bg); Plot(c, 18, 1, '▄', bg);
            // Head
            HLine(c, 13, 17, 1, '▄', g);
            Plot(c, 12, 2, '█', g); HLine(c, 13, 17, 2, '▒', bg); Plot(c, 18, 2, '█', g);
            // Eyes + mouth
            Plot(c, 14, 2, '█', "bright_yellow"); Plot(c, 16, 2, '█', "bright_yellow");
            Plot(c, 12, 3, '█', g); HLine(c, 13, 17, 3, '▒', bg); Plot(c, 18, 3, '█', g);
            Plot(c, 14, 3, '▓', "red"); Plot(c, 15, 3, '▓', "red"); Plot(c, 16, 3, '▓', "red");
            HLine(c, 13, 17, 4, '▀', g);
            // Body (hunched)
            FillRect(c, 12, 5, 18, 7, '▒', g);
            Plot(c, 12, 5, '█', g); Plot(c, 18, 5, '█', g);
            Plot(c, 12, 6, '█', g); Plot(c, 18, 6, '█', g);
            Plot(c, 12, 7, '█', g); Plot(c, 18, 7, '█', g);
            // Arms
            Plot(c, 10, 5, '░', g); Plot(c, 11, 5, '▓', g);
            Plot(c, 19, 5, '▓', g); Plot(c, 20, 5, '░', g);
            Plot(c, 10, 6, '▓', g); Plot(c, 20, 6, '▓', g);
            // Legs
            VLine(c, 13, 8, 9, '█', g); VLine(c, 17, 8, 9, '█', g);
        }

        private static void DrawUndead(Cell[,] c)
        {
            string g = "gray", w = "white";
            // Skull
            HLine(c, 12, 18, 0, '▄', g);
            Plot(c, 11, 1, '█', g); HLine(c, 12, 18, 1, '▒', w); Plot(c, 19, 1, '█', g);
            Plot(c, 13, 1, '█', "red"); Plot(c, 17, 1, '█', "red"); // eye sockets
            Plot(c, 11, 2, '█', g); HLine(c, 12, 18, 2, '▒', w); Plot(c, 19, 2, '█', g);
            Plot(c, 14, 2, '▓', g); Plot(c, 15, 2, '▓', g); Plot(c, 16, 2, '▓', g);
            HLine(c, 12, 18, 3, '▀', g);
            // Spine
            VLine(c, 15, 4, 5, '█', w);
            // Ribs
            HLine(c, 12, 18, 4, '░', g);
            HLine(c, 13, 17, 5, '░', g);
            Plot(c, 12, 4, '▓', g); Plot(c, 18, 4, '▓', g);
            // Tattered robe
            FillRect(c, 11, 6, 19, 8, '░', g);
            Plot(c, 11, 6, '▓', g); Plot(c, 19, 6, '▓', g);
            Plot(c, 10, 7, '▓', g); Plot(c, 20, 7, '▓', g);
            Plot(c, 10, 8, '░', g); Plot(c, 20, 8, '░', g);
            HLine(c, 10, 20, 9, '▀', g);
        }

        private static void DrawOrcish(Cell[,] c)
        {
            string g = "green", bg = "bright_green";
            // Head (wide)
            HLine(c, 11, 19, 0, '▄', g);
            FillRect(c, 10, 1, 20, 3, '▒', bg);
            Plot(c, 10, 1, '█', g); Plot(c, 20, 1, '█', g);
            Plot(c, 10, 2, '█', g); Plot(c, 20, 2, '█', g);
            Plot(c, 10, 3, '█', g); Plot(c, 20, 3, '█', g);
            // Heavy brow
            HLine(c, 11, 19, 1, '▓', g);
            // Eyes
            Plot(c, 13, 2, '█', "red"); Plot(c, 14, 2, '█', "red");
            Plot(c, 17, 2, '█', "red"); Plot(c, 18, 2, '█', "red");
            // Tusks + mouth
            Plot(c, 13, 3, '▓', "red"); HLine(c, 14, 16, 3, '▓', "red"); Plot(c, 17, 3, '▓', "red");
            Plot(c, 12, 3, '▀', "white"); Plot(c, 18, 3, '▀', "white"); // tusks
            HLine(c, 11, 19, 4, '▀', g);
            // Massive body
            FillRect(c, 9, 5, 21, 8, '▒', g);
            Plot(c, 9, 5, '█', g); Plot(c, 21, 5, '█', g);
            Plot(c, 9, 6, '█', g); Plot(c, 21, 6, '█', g);
            Plot(c, 9, 7, '█', g); Plot(c, 21, 7, '█', g);
            Plot(c, 9, 8, '█', g); Plot(c, 21, 8, '█', g);
            // Arms
            Plot(c, 7, 5, '░', g); Plot(c, 8, 5, '▓', g);
            Plot(c, 22, 5, '▓', g); Plot(c, 23, 5, '░', g);
            Plot(c, 7, 6, '▓', g); Plot(c, 23, 6, '▓', g);
            Plot(c, 7, 7, '░', g); Plot(c, 23, 7, '░', g);
            // Legs
            VLine(c, 12, 9, 9, '█', g); VLine(c, 13, 9, 9, '█', g);
            VLine(c, 17, 9, 9, '█', g); VLine(c, 18, 9, 9, '█', g);
        }

        private static void DrawDraconic(Cell[,] c)
        {
            string r = "red", br = "bright_red", y = "bright_yellow";
            // Wings
            Plot(c, 4, 0, '▄', r); Plot(c, 5, 0, '▄', r);
            Plot(c, 3, 1, '█', r); Plot(c, 4, 1, '▓', br); Plot(c, 5, 1, '▓', br);
            Plot(c, 2, 2, '█', r); Plot(c, 3, 2, '▓', br); Plot(c, 4, 2, '░', br);
            Plot(c, 1, 3, '█', r); Plot(c, 2, 3, '▓', br); Plot(c, 3, 3, '░', br);
            Plot(c, 25, 0, '▄', r); Plot(c, 26, 0, '▄', r);
            Plot(c, 25, 1, '▓', br); Plot(c, 26, 1, '▓', br); Plot(c, 27, 1, '█', r);
            Plot(c, 26, 2, '░', br); Plot(c, 27, 2, '▓', br); Plot(c, 28, 2, '█', r);
            Plot(c, 27, 3, '░', br); Plot(c, 28, 3, '▓', br); Plot(c, 29, 3, '█', r);
            // Neck + Head
            HLine(c, 17, 22, 0, '▄', r);
            Plot(c, 16, 1, '█', r); HLine(c, 17, 22, 1, '▒', br); Plot(c, 23, 1, '█', r);
            Plot(c, 19, 1, '█', y); Plot(c, 21, 1, '█', y); // eyes
            Plot(c, 16, 2, '█', r); HLine(c, 17, 22, 2, '▒', br); Plot(c, 23, 2, '█', r);
            Plot(c, 23, 2, '▓', y); Plot(c, 24, 2, '░', y); // breath
            HLine(c, 17, 22, 3, '▀', r);
            // Body
            FillRect(c, 10, 3, 20, 7, '▒', r);
            Plot(c, 10, 3, '█', r); Plot(c, 20, 3, '█', r);
            Plot(c, 10, 4, '█', r); Plot(c, 20, 4, '█', r);
            Plot(c, 10, 5, '█', r); Plot(c, 20, 5, '█', r);
            Plot(c, 10, 6, '█', r); Plot(c, 20, 6, '█', r);
            Plot(c, 10, 7, '█', r); Plot(c, 20, 7, '█', r);
            // Belly
            FillRect(c, 12, 5, 18, 7, '░', br);
            // Legs
            VLine(c, 11, 8, 9, '█', r); VLine(c, 12, 8, 9, '▓', r);
            VLine(c, 18, 8, 9, '▓', r); VLine(c, 19, 8, 9, '█', r);
            // Tail
            Plot(c, 9, 7, '▓', r); Plot(c, 8, 8, '▓', r); Plot(c, 7, 9, '░', r);
        }

        private static void DrawDemonic(Cell[,] c)
        {
            string r = "red", br = "bright_red", dm = "dark_magenta";
            // Horns
            Plot(c, 11, 0, '▄', r); Plot(c, 10, 1, '▓', r);
            Plot(c, 19, 0, '▄', r); Plot(c, 20, 1, '▓', r);
            // Head
            HLine(c, 12, 18, 1, '▄', dm);
            Plot(c, 11, 2, '█', dm); HLine(c, 12, 18, 2, '▓', r); Plot(c, 19, 2, '█', dm);
            Plot(c, 13, 2, '█', "bright_yellow"); Plot(c, 17, 2, '█', "bright_yellow");
            Plot(c, 11, 3, '█', dm); HLine(c, 12, 18, 3, '▒', r); Plot(c, 19, 3, '█', dm);
            HLine(c, 13, 17, 3, '▓', br); // mouth
            HLine(c, 12, 18, 4, '▀', dm);
            // Wings
            Plot(c, 6, 3, '▄', dm); Plot(c, 7, 3, '▄', dm); Plot(c, 8, 4, '▓', dm);
            Plot(c, 6, 4, '█', dm); Plot(c, 7, 4, '▓', dm);
            Plot(c, 5, 5, '█', dm); Plot(c, 6, 5, '░', dm); Plot(c, 7, 5, '░', dm);
            Plot(c, 22, 3, '▄', dm); Plot(c, 23, 3, '▄', dm); Plot(c, 22, 4, '▓', dm);
            Plot(c, 23, 4, '▓', dm); Plot(c, 24, 4, '█', dm);
            Plot(c, 23, 5, '░', dm); Plot(c, 24, 5, '░', dm); Plot(c, 25, 5, '█', dm);
            // Body
            FillRect(c, 11, 5, 19, 7, '▒', r);
            Plot(c, 11, 5, '█', dm); Plot(c, 19, 5, '█', dm);
            Plot(c, 11, 6, '█', dm); Plot(c, 19, 6, '█', dm);
            Plot(c, 11, 7, '█', dm); Plot(c, 19, 7, '█', dm);
            // Legs
            VLine(c, 13, 8, 9, '█', dm); VLine(c, 17, 8, 9, '█', dm);
            // Tail
            Plot(c, 20, 7, '▓', r); Plot(c, 21, 8, '▓', r); Plot(c, 22, 9, '▄', r);
        }

        private static void DrawGiant(Cell[,] c)
        {
            string y = "yellow", by = "bright_yellow";
            // Head (massive)
            HLine(c, 10, 20, 0, '▄', y);
            FillRect(c, 9, 1, 21, 2, '▒', by);
            Plot(c, 9, 1, '█', y); Plot(c, 21, 1, '█', y);
            Plot(c, 9, 2, '█', y); Plot(c, 21, 2, '█', y);
            Plot(c, 12, 1, '█', "white"); Plot(c, 18, 1, '█', "white");
            HLine(c, 13, 17, 2, '▓', "red"); // mouth
            HLine(c, 10, 20, 3, '▀', y);
            // HUGE body
            FillRect(c, 7, 3, 23, 8, '▒', y);
            for (int row = 3; row <= 8; row++)
            {
                Plot(c, 7, row, '█', y); Plot(c, 23, row, '█', y);
            }
            // Arms (thick)
            FillRect(c, 4, 4, 6, 7, '▓', y);
            FillRect(c, 24, 4, 26, 7, '▓', y);
            Plot(c, 4, 4, '█', y); Plot(c, 26, 4, '█', y);
            Plot(c, 3, 7, '░', y); Plot(c, 27, 7, '░', y);
            // Legs
            FillRect(c, 10, 9, 13, 9, '█', y);
            FillRect(c, 17, 9, 20, 9, '█', y);
        }

        private static void DrawBeast(Cell[,] c)
        {
            string y = "yellow", by = "bright_yellow";
            // Head
            HLine(c, 19, 24, 2, '▄', y);
            Plot(c, 18, 3, '█', y); HLine(c, 19, 24, 3, '▒', by); Plot(c, 25, 3, '█', y);
            Plot(c, 21, 3, '█', "bright_green"); Plot(c, 23, 3, '█', "bright_green"); // eyes
            Plot(c, 18, 4, '█', y); HLine(c, 19, 24, 4, '▒', by); Plot(c, 25, 4, '█', y);
            Plot(c, 25, 4, '▓', y); Plot(c, 26, 4, '░', y); // maw
            HLine(c, 19, 24, 5, '▀', y);
            // Body (four-legged)
            FillRect(c, 7, 4, 19, 7, '▒', y);
            HLine(c, 7, 19, 4, '▄', y);
            Plot(c, 7, 5, '█', y); Plot(c, 19, 5, '█', y);
            Plot(c, 7, 6, '█', y); Plot(c, 19, 6, '█', y);
            Plot(c, 7, 7, '█', y); Plot(c, 19, 7, '█', y);
            FillRect(c, 9, 6, 17, 7, '░', by); // belly
            // Legs (four)
            VLine(c, 8, 8, 9, '█', y); VLine(c, 11, 8, 9, '█', y);
            VLine(c, 16, 8, 9, '█', y); VLine(c, 19, 8, 9, '█', y);
            // Tail
            Plot(c, 6, 4, '▓', y); Plot(c, 5, 3, '░', y); Plot(c, 4, 2, '░', y);
        }

        private static void DrawElemental(Cell[,] c)
        {
            string cy = "bright_cyan", w = "white", b = "cyan";
            // Swirling energy form
            Plot(c, 14, 0, '░', cy); Plot(c, 16, 0, '░', cy);
            Plot(c, 12, 1, '░', b); HLine(c, 13, 17, 1, '▒', cy); Plot(c, 18, 1, '░', b);
            Plot(c, 11, 2, '▒', b); HLine(c, 12, 18, 2, '▓', cy); Plot(c, 19, 2, '▒', b);
            Plot(c, 14, 2, '█', w); Plot(c, 16, 2, '█', w); // eyes
            HLine(c, 10, 20, 3, '▓', cy);
            Plot(c, 10, 3, '░', b); Plot(c, 20, 3, '░', b);
            // Core
            FillRect(c, 11, 4, 19, 6, '▒', cy);
            Plot(c, 14, 5, '█', w); Plot(c, 15, 5, '█', w); Plot(c, 16, 5, '█', w); // bright core
            // Tendrils
            Plot(c, 8, 4, '░', b); Plot(c, 9, 4, '▒', b); Plot(c, 10, 4, '▓', cy);
            Plot(c, 22, 4, '░', b); Plot(c, 21, 4, '▒', b); Plot(c, 20, 4, '▓', cy);
            Plot(c, 7, 5, '░', b); Plot(c, 8, 5, '▒', b);
            Plot(c, 23, 5, '░', b); Plot(c, 22, 5, '▒', b);
            // Base dissipation
            HLine(c, 10, 20, 7, '▒', b);
            HLine(c, 11, 19, 8, '░', b);
            Plot(c, 12, 9, '░', b); Plot(c, 14, 9, '░', b);
            Plot(c, 16, 9, '░', b); Plot(c, 18, 9, '░', b);
        }

        private static void DrawAberration(Cell[,] c)
        {
            string m = "magenta", bm = "bright_magenta";
            // Central mass
            HLine(c, 11, 19, 1, '▄', m);
            FillRect(c, 10, 2, 20, 6, '▒', bm);
            for (int row = 2; row <= 6; row++)
            {
                Plot(c, 10, row, '█', m); Plot(c, 20, row, '█', m);
            }
            // Multiple eyes
            Plot(c, 13, 2, '█', "bright_yellow"); Plot(c, 17, 2, '█', "bright_cyan");
            Plot(c, 15, 3, '█', "bright_green"); // third eye
            Plot(c, 12, 4, '█', "red");
            HLine(c, 11, 19, 7, '▀', m);
            // Tentacles
            Plot(c, 9, 7, '▓', m); Plot(c, 8, 8, '▓', m); Plot(c, 7, 9, '░', m);
            Plot(c, 12, 7, '▓', m); Plot(c, 11, 8, '▒', m); Plot(c, 10, 9, '░', m);
            Plot(c, 15, 7, '▓', m); Plot(c, 15, 8, '▒', m); Plot(c, 15, 9, '░', m);
            Plot(c, 18, 7, '▓', m); Plot(c, 19, 8, '▒', m); Plot(c, 20, 9, '░', m);
            Plot(c, 21, 7, '▓', m); Plot(c, 22, 8, '▓', m); Plot(c, 23, 9, '░', m);
        }

        private static void DrawInsectoid(Cell[,] c)
        {
            string dy = "yellow", by = "bright_yellow";
            // Antennae
            Plot(c, 13, 0, '░', dy); Plot(c, 17, 0, '░', dy);
            Plot(c, 14, 1, '▓', dy); Plot(c, 16, 1, '▓', dy);
            // Head
            HLine(c, 13, 17, 2, '▄', dy);
            Plot(c, 12, 3, '█', dy); HLine(c, 13, 17, 3, '▓', by); Plot(c, 18, 3, '█', dy);
            Plot(c, 14, 3, '█', "red"); Plot(c, 16, 3, '█', "red"); // compound eyes
            Plot(c, 12, 4, '█', dy); HLine(c, 13, 17, 4, '▒', by); Plot(c, 18, 4, '█', dy);
            // Thorax
            FillRect(c, 11, 5, 19, 6, '▒', dy);
            Plot(c, 11, 5, '█', dy); Plot(c, 19, 5, '█', dy);
            Plot(c, 11, 6, '█', dy); Plot(c, 19, 6, '█', dy);
            // Abdomen
            FillRect(c, 10, 7, 20, 8, '▓', dy);
            Plot(c, 10, 7, '█', dy); Plot(c, 20, 7, '█', dy);
            HLine(c, 10, 20, 9, '▀', dy);
            // Legs (6)
            Plot(c, 8, 5, '░', dy); Plot(c, 9, 5, '▓', dy);
            Plot(c, 21, 5, '▓', dy); Plot(c, 22, 5, '░', dy);
            Plot(c, 7, 6, '░', dy); Plot(c, 8, 6, '▓', dy);
            Plot(c, 22, 6, '▓', dy); Plot(c, 23, 6, '░', dy);
            Plot(c, 8, 7, '░', dy); Plot(c, 9, 7, '▓', dy);
            Plot(c, 21, 7, '▓', dy); Plot(c, 22, 7, '░', dy);
        }

        private static void DrawConstruct(Cell[,] c)
        {
            string w = "white", g = "gray";
            // Head (angular)
            HLine(c, 12, 18, 0, '▄', g);
            Plot(c, 11, 1, '█', g); HLine(c, 12, 18, 1, '▓', w); Plot(c, 19, 1, '█', g);
            Plot(c, 13, 1, '█', "bright_cyan"); Plot(c, 17, 1, '█', "bright_cyan"); // eyes
            Plot(c, 11, 2, '█', g); HLine(c, 12, 18, 2, '▓', w); Plot(c, 19, 2, '█', g);
            HLine(c, 12, 18, 3, '▀', g);
            // Body (geometric)
            FillRect(c, 9, 4, 21, 8, '░', g);
            for (int row = 4; row <= 8; row++)
            {
                Plot(c, 9, row, '█', g); Plot(c, 21, row, '█', g);
            }
            HLine(c, 9, 21, 4, '▄', g);
            // Rune core
            Plot(c, 14, 5, '█', "bright_cyan"); Plot(c, 15, 5, '█', "bright_cyan"); Plot(c, 16, 5, '█', "bright_cyan");
            Plot(c, 14, 6, '█', "bright_cyan"); Plot(c, 16, 6, '█', "bright_cyan");
            // Arms (thick, mechanical)
            FillRect(c, 6, 4, 8, 7, '▓', g);
            FillRect(c, 22, 4, 24, 7, '▓', g);
            // Legs
            FillRect(c, 11, 9, 13, 9, '█', g); FillRect(c, 17, 9, 19, 9, '█', g);
        }

        private static void DrawFey(Cell[,] c)
        {
            string bg = "bright_green", g = "green", w = "white";
            // Sparkles
            Plot(c, 6, 0, '░', bg); Plot(c, 23, 1, '░', bg);
            Plot(c, 8, 2, '░', bg); Plot(c, 22, 0, '░', bg);
            // Wings
            Plot(c, 8, 2, '░', bg); Plot(c, 9, 3, '▒', bg); Plot(c, 10, 4, '▓', bg);
            Plot(c, 7, 3, '▒', bg); Plot(c, 8, 4, '▓', bg);
            Plot(c, 22, 2, '░', bg); Plot(c, 21, 3, '▒', bg); Plot(c, 20, 4, '▓', bg);
            Plot(c, 23, 3, '▒', bg); Plot(c, 22, 4, '▓', bg);
            // Head
            HLine(c, 13, 17, 1, '▄', g);
            Plot(c, 12, 2, '█', g); HLine(c, 13, 17, 2, '▒', bg); Plot(c, 18, 2, '█', g);
            Plot(c, 14, 2, '█', w); Plot(c, 16, 2, '█', w); // eyes
            HLine(c, 13, 17, 3, '▀', g);
            // Slender body
            FillRect(c, 13, 4, 17, 7, '▒', bg);
            Plot(c, 13, 4, '█', g); Plot(c, 17, 4, '█', g);
            Plot(c, 13, 5, '█', g); Plot(c, 17, 5, '█', g);
            Plot(c, 13, 6, '█', g); Plot(c, 17, 6, '█', g);
            Plot(c, 13, 7, '█', g); Plot(c, 17, 7, '█', g);
            // Flowing dress
            FillRect(c, 12, 8, 18, 9, '░', bg);
            Plot(c, 11, 9, '░', bg); Plot(c, 19, 9, '░', bg);
        }

        private static void DrawAquatic(Cell[,] c)
        {
            string cy = "cyan", bc = "bright_cyan";
            // Wave effects
            Plot(c, 5, 0, '░', cy); Plot(c, 9, 0, '░', cy); Plot(c, 20, 0, '░', cy); Plot(c, 25, 0, '░', cy);
            // Fin
            Plot(c, 15, 0, '▄', cy); Plot(c, 14, 1, '▓', cy); Plot(c, 15, 1, '█', cy); Plot(c, 16, 1, '▓', cy);
            // Head
            HLine(c, 11, 19, 2, '▄', cy);
            Plot(c, 10, 3, '█', cy); HLine(c, 11, 19, 3, '▒', bc); Plot(c, 20, 3, '█', cy);
            Plot(c, 13, 3, '█', "bright_yellow"); Plot(c, 17, 3, '█', "bright_yellow"); // eyes
            Plot(c, 10, 4, '█', cy); HLine(c, 11, 19, 4, '▒', bc); Plot(c, 20, 4, '█', cy);
            // Body (streamlined)
            FillRect(c, 9, 5, 21, 7, '▒', cy);
            Plot(c, 9, 5, '█', cy); Plot(c, 21, 5, '█', cy);
            Plot(c, 9, 6, '█', cy); Plot(c, 21, 6, '█', cy);
            Plot(c, 9, 7, '█', cy); Plot(c, 21, 7, '█', cy);
            FillRect(c, 11, 6, 19, 7, '░', bc); // belly
            // Side fins
            Plot(c, 7, 5, '░', cy); Plot(c, 8, 5, '▓', cy);
            Plot(c, 22, 5, '▓', cy); Plot(c, 23, 5, '░', cy);
            // Tail
            HLine(c, 9, 21, 8, '▀', cy);
            Plot(c, 13, 9, '░', cy); Plot(c, 15, 9, '░', cy); Plot(c, 17, 9, '░', cy); // bubbles
        }

        private static void DrawCelestial(Cell[,] c)
        {
            string by = "bright_yellow", y = "yellow", w = "white";
            // Halo
            HLine(c, 13, 17, 0, '░', by);
            Plot(c, 12, 0, '░', y); Plot(c, 18, 0, '░', y);
            // Head
            HLine(c, 13, 17, 1, '▄', y);
            Plot(c, 12, 2, '█', y); HLine(c, 13, 17, 2, '▒', by); Plot(c, 18, 2, '█', y);
            Plot(c, 14, 2, '█', w); Plot(c, 16, 2, '█', w); // eyes
            HLine(c, 13, 17, 3, '▀', y);
            // Wings (radiant)
            Plot(c, 6, 2, '░', by); Plot(c, 7, 3, '▒', by); Plot(c, 8, 3, '▓', by);
            Plot(c, 5, 3, '░', by); Plot(c, 6, 4, '▒', by); Plot(c, 7, 4, '▓', by);
            Plot(c, 4, 4, '░', by); Plot(c, 5, 5, '▒', by); Plot(c, 6, 5, '▓', by);
            Plot(c, 24, 2, '░', by); Plot(c, 23, 3, '▒', by); Plot(c, 22, 3, '▓', by);
            Plot(c, 25, 3, '░', by); Plot(c, 24, 4, '▒', by); Plot(c, 23, 4, '▓', by);
            Plot(c, 26, 4, '░', by); Plot(c, 25, 5, '▒', by); Plot(c, 24, 5, '▓', by);
            // Body (robed)
            FillRect(c, 12, 4, 18, 7, '▒', by);
            Plot(c, 12, 4, '█', y); Plot(c, 18, 4, '█', y);
            Plot(c, 12, 5, '█', y); Plot(c, 18, 5, '█', y);
            Plot(c, 12, 6, '█', y); Plot(c, 18, 6, '█', y);
            Plot(c, 12, 7, '█', y); Plot(c, 18, 7, '█', y);
            // Robe bottom
            FillRect(c, 11, 8, 19, 9, '░', by);
            Plot(c, 10, 9, '░', y); Plot(c, 20, 9, '░', y);
        }

        private static void DrawShadow(Cell[,] c)
        {
            string dm = "dark_magenta", g = "gray";
            // Wisps above
            Plot(c, 13, 0, '░', dm); Plot(c, 17, 0, '░', dm);
            Plot(c, 11, 1, '░', g); Plot(c, 19, 1, '░', g);
            // Form (amorphous, shifting)
            HLine(c, 12, 18, 2, '░', dm);
            HLine(c, 11, 19, 3, '▒', dm);
            // Eyes (only visible feature)
            Plot(c, 13, 3, '█', "red"); Plot(c, 17, 3, '█', "red");
            FillRect(c, 10, 4, 20, 6, '▓', dm);
            Plot(c, 10, 4, '▒', g); Plot(c, 20, 4, '▒', g);
            // Dark core
            FillRect(c, 12, 5, 18, 5, '█', dm);
            // Dissipating edges
            HLine(c, 10, 20, 7, '▒', dm);
            Plot(c, 9, 7, '░', g); Plot(c, 21, 7, '░', g);
            HLine(c, 11, 19, 8, '░', dm);
            Plot(c, 12, 9, '░', g); Plot(c, 15, 9, '░', g); Plot(c, 18, 9, '░', g);
        }

        #endregion
    }
}
