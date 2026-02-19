using System;
using System.Collections.Generic;
using System.Text;

namespace UsurperRemake.UI
{
    /// <summary>
    /// Canvas-based procedural portrait generator.
    ///
    /// Uses a 2D grid where each cell is exactly 1 visible character + a color.
    /// This GUARANTEES perfect alignment — no more width bugs.
    ///
    /// Portraits are built in layers:
    ///   1. Atmosphere (class-themed ambient particles)
    ///   2. Head shape (race-specific oval with shading gradient)
    ///   3. Eyes, Nose, Mouth (hash-selected variants)
    ///   4. Hair (hash-selected style + color)
    ///   5. Beard (hash-selected, race-weighted)
    ///   6. Ears (race-specific)
    ///   7. Accessories (scars, tusks, eye patches)
    ///
    /// Characters: Only terminal-safe █▀▄░▒▓ and basic ASCII.
    /// Shading: ░ (light) → ▒ (medium) → ▓ (dark) → █ (solid)
    /// Skin gradient: darker ▓ at top (hair shadow), ▒ mid-face, ░ lower face.
    /// </summary>
    public static class PortraitGenerator
    {
        private const int W = 34;   // Canvas width
        private const int H = 14;   // Canvas height
        private const int CX = 16;  // Center X

        #region Public API

        public static string[] GeneratePortrait(Character npc)
        {
            uint seed = DJB2(npc.Name2 ?? npc.Name1 ?? "Unknown");
            var rng = new Rng(seed);

            // Build visual traits from hash + character data
            var t = BuildTraits(npc, rng);

            // Initialize blank canvas
            var canvas = new Cell[H, W];

            // Draw layers back-to-front
            DrawAtmosphere(canvas, t, rng);
            DrawHead(canvas, t);
            DrawEyes(canvas, t);
            DrawNose(canvas, t);
            DrawMouth(canvas, t);
            DrawHair(canvas, t);
            DrawBeard(canvas, t);
            DrawEars(canvas, t);
            DrawAccessories(canvas, t);

            return Render(canvas, npc, t);
        }

        #endregion

        #region Canvas

        private struct Cell
        {
            public char Ch;
            public string Color;
        }

        private static void Plot(Cell[,] c, int x, int y, char ch, string color)
        {
            if (x < 0 || x >= W || y < 0 || y >= H) return;
            c[y, x] = new Cell { Ch = ch, Color = color };
        }

        private static void PlotIfEmpty(Cell[,] c, int x, int y, char ch, string color)
        {
            if (x < 0 || x >= W || y < 0 || y >= H) return;
            if (c[y, x].Ch == '\0' || c[y, x].Ch == ' ')
                c[y, x] = new Cell { Ch = ch, Color = color };
        }

        #endregion

        #region Seeded RNG

        private class Rng
        {
            private uint _s;
            public Rng(uint seed) => _s = seed == 0 ? 1 : seed;

            public int Next(int max)
            {
                _s = _s * 1103515245 + 12345;
                return (int)((_s >> 16) % (uint)max);
            }

            public int Range(int lo, int hi) => lo + Next(hi - lo);
            public T Pick<T>(T[] a) => a[Next(a.Length)];
            public bool Chance(int pct) => Next(100) < pct;
        }

        #endregion

        #region Traits

        private class Traits
        {
            public CharacterRace Race;
            public CharacterSex Sex;
            public string SkinColor = "";
            public string SkinDark = "";
            public string OutlineColor = "";
            public string HairColor = "";
            public string EyeColor = "";
            public string MouthColor = "";
            public string BorderColor = "";
            public string AtmoColor = "";
            public char AtmoChar;
            public int HairStyle;
            public int EyeStyle;
            public int NoseStyle;
            public int MouthStyle;
            public int BeardStyle;
            public int AccessoryBits;
            public int[] HeadWidths = Array.Empty<int>();
            public int EyeRow;
            public int NoseRow;
            public int MouthRow;
        }

        private static Traits BuildTraits(Character npc, Rng rng)
        {
            var t = new Traits
            {
                Race = npc.Race,
                Sex = npc.Sex
            };

            // Skin colors per race
            (t.SkinColor, t.SkinDark, t.OutlineColor) = npc.Race switch
            {
                CharacterRace.Elf or CharacterRace.HalfElf => ("bright_cyan", "cyan", "cyan"),
                CharacterRace.Dwarf => ("bright_yellow", "yellow", "yellow"),
                CharacterRace.Orc => ("green", "green", "green"),
                CharacterRace.Troll => ("bright_green", "green", "green"),
                CharacterRace.Hobbit => ("bright_yellow", "yellow", "yellow"),
                CharacterRace.Gnome => ("bright_yellow", "yellow", "bright_yellow"),
                CharacterRace.Gnoll => ("bright_yellow", "yellow", "yellow"),
                CharacterRace.Mutant => ("bright_magenta", "magenta", "dark_magenta"),
                _ => ("bright_yellow", "yellow", "yellow"), // Human
            };

            // Hair color (race-weighted)
            t.HairColor = npc.Race switch
            {
                CharacterRace.Elf or CharacterRace.HalfElf =>
                    rng.Pick(new[] { "white", "bright_yellow", "gray", "bright_cyan" }),
                CharacterRace.Orc or CharacterRace.Troll =>
                    rng.Pick(new[] { "gray", "dark_magenta", "red" }),
                CharacterRace.Gnoll =>
                    rng.Pick(new[] { "yellow", "bright_yellow", "red" }),
                CharacterRace.Mutant =>
                    rng.Pick(new[] { "bright_magenta", "bright_green", "bright_cyan", "red" }),
                _ => rng.Pick(new[] { "yellow", "red", "gray", "white", "dark_magenta", "bright_yellow" }),
            };

            // Eye + mouth color
            t.EyeColor = rng.Pick(new[] { "bright_cyan", "bright_green", "white", "bright_yellow", "red" });
            t.MouthColor = rng.Pick(new[] { "red", "dark_magenta", "red" });

            // Class-based border + atmosphere
            t.BorderColor = GetClassColor(npc.Class);
            (t.AtmoColor, t.AtmoChar) = npc.Class switch
            {
                CharacterClass.Warrior => ("red", '░'),
                CharacterClass.Paladin => ("bright_yellow", '░'),
                CharacterClass.Magician => ("bright_magenta", '░'),
                CharacterClass.Cleric => ("white", '░'),
                CharacterClass.Assassin => ("gray", '░'),
                CharacterClass.Ranger => ("bright_green", '░'),
                CharacterClass.Barbarian => ("red", '▒'),
                CharacterClass.Bard => ("bright_cyan", '░'),
                CharacterClass.Sage => ("cyan", '░'),
                CharacterClass.Jester => ("bright_yellow", '░'),
                CharacterClass.Alchemist => ("green", '░'),
                _ => ("gray", '░'),
            };

            // Feature variants from hash
            t.HairStyle = rng.Next(10);
            t.EyeStyle = rng.Next(8);
            t.NoseStyle = rng.Next(6);
            t.MouthStyle = rng.Next(6);

            // Beard (race/sex weighted)
            t.BeardStyle = 0;
            if (t.Sex == CharacterSex.Male)
            {
                if (t.Race == CharacterRace.Dwarf)
                    t.BeardStyle = rng.Range(3, 7);    // Dwarves ALWAYS have epic beards
                else if (t.Race == CharacterRace.Orc || t.Race == CharacterRace.Troll)
                    t.BeardStyle = rng.Chance(25) ? rng.Range(1, 3) : 0;
                else if (t.Race == CharacterRace.Gnoll)
                    t.BeardStyle = 0; // Gnolls don't have beards
                else
                    t.BeardStyle = rng.Chance(40) ? rng.Range(1, 5) : 0;
            }

            // Accessories
            t.AccessoryBits = 0;
            if (rng.Chance(15)) t.AccessoryBits |= 1;  // scar
            if (rng.Chance(8))  t.AccessoryBits |= 2;  // eye patch
            if ((t.Race == CharacterRace.Orc || t.Race == CharacterRace.Troll) && rng.Chance(50))
                t.AccessoryBits |= 4; // tusks

            // Head shape + feature rows
            t.HeadWidths = GetHeadWidths(npc.Race);
            (t.EyeRow, t.NoseRow, t.MouthRow) = GetFeatureRows(npc.Race);

            return t;
        }

        // Half-width from center per canvas row. -1 = no head.
        private static int[] GetHeadWidths(CharacterRace race)
        {
            return race switch
            {
                CharacterRace.Elf or CharacterRace.HalfElf =>
                    new[] { -1, 5, 7, 8, 8, 8, 8, 8, 7, 6, 5, 3, -1, -1 },
                CharacterRace.Dwarf =>
                    new[] { -1, 7, 9, 10, 10, 10, 10, 10, 10, 9, 8, 6, -1, -1 },
                CharacterRace.Orc =>
                    new[] { -1, 8, 10, 11, 11, 11, 11, 10, 9, 8, 6, 4, -1, -1 },
                CharacterRace.Troll =>
                    new[] { -1, 8, 10, 12, 12, 12, 12, 11, 10, 9, 7, 5, -1, -1 },
                CharacterRace.Hobbit =>
                    new[] { -1, -1, 5, 7, 8, 8, 8, 8, 7, 5, 3, -1, -1, -1 },
                CharacterRace.Gnome =>
                    new[] { -1, -1, 5, 7, 8, 8, 8, 7, 6, 4, 3, -1, -1, -1 },
                CharacterRace.Gnoll =>
                    new[] { -1, 6, 8, 9, 9, 9, 10, 11, 10, 8, 6, 4, -1, -1 },
                CharacterRace.Mutant =>
                    new[] { -1, 7, 9, 10, 9, 10, 9, 9, 8, 7, 5, 3, -1, -1 },
                _ => // Human
                    new[] { -1, 6, 8, 10, 10, 10, 10, 10, 9, 8, 6, 4, -1, -1 },
            };
        }

        private static (int eye, int nose, int mouth) GetFeatureRows(CharacterRace race)
        {
            return race switch
            {
                CharacterRace.Dwarf => (4, 6, 8),
                CharacterRace.Orc or CharacterRace.Troll => (3, 5, 7),
                CharacterRace.Hobbit => (5, 7, 8),
                CharacterRace.Gnome => (4, 6, 7),
                CharacterRace.Gnoll => (3, 6, 8),
                CharacterRace.Mutant => (4, 6, 8),
                _ => (4, 6, 8), // Human, Elf
            };
        }

        #endregion

        #region Draw Head

        private static void DrawHead(Cell[,] c, Traits t)
        {
            // Find first/last head rows for shading gradient
            int firstY = -1, lastY = -1;
            for (int y = 0; y < H; y++)
            {
                if (t.HeadWidths[y] >= 0)
                {
                    if (firstY < 0) firstY = y;
                    lastY = y;
                }
            }
            if (firstY < 0) return;
            int headH = lastY - firstY + 1;

            for (int y = 0; y < H; y++)
            {
                int hw = t.HeadWidths[y];
                if (hw < 0) continue;

                int left = CX - hw;
                int right = CX + hw;
                int prevHw = (y > 0) ? t.HeadWidths[y - 1] : -1;
                int nextHw = (y < H - 1) ? t.HeadWidths[y + 1] : -1;
                bool isTop = prevHw < 0;
                bool isBot = nextHw < 0;

                if (isTop)
                {
                    // Top curve with ▄ characters
                    Plot(c, left, y, '▄', t.OutlineColor);
                    for (int x = left + 1; x < right; x++)
                        Plot(c, x, y, '▄', t.SkinDark);
                    Plot(c, right, y, '▄', t.OutlineColor);
                }
                else if (isBot)
                {
                    // Bottom curve with ▀ characters
                    Plot(c, left, y, '▀', t.OutlineColor);
                    for (int x = left + 1; x < right; x++)
                        Plot(c, x, y, '▀', t.SkinDark);
                    Plot(c, right, y, '▀', t.OutlineColor);
                }
                else
                {
                    // Normal row: outline edges + shaded skin fill
                    Plot(c, left, y, '█', t.OutlineColor);
                    Plot(c, right, y, '█', t.OutlineColor);

                    // Skin shading based on vertical position
                    float relPos = (float)(y - firstY) / headH;
                    char fillChar;
                    string fillColor;
                    if (relPos < 0.2f)
                    {
                        fillChar = '▓'; fillColor = t.SkinDark;   // Top shadow
                    }
                    else if (relPos > 0.8f)
                    {
                        fillChar = '░'; fillColor = t.SkinColor;  // Lower highlight
                    }
                    else
                    {
                        fillChar = '▒'; fillColor = t.SkinColor;  // Mid skin
                    }

                    for (int x = left + 1; x < right; x++)
                        Plot(c, x, y, fillChar, fillColor);

                    // Handle width expansion (curved sides)
                    if (prevHw >= 0 && hw > prevHw)
                    {
                        // Left expansion
                        for (int x = left; x < CX - prevHw; x++)
                            Plot(c, x, y, '▄', t.OutlineColor);
                        // Right expansion
                        for (int x = CX + prevHw + 1; x <= right; x++)
                            Plot(c, x, y, '▄', t.OutlineColor);
                    }
                    // Handle width contraction (for row above the bottom curve)
                    if (nextHw >= 0 && nextHw < hw)
                    {
                        for (int x = CX - hw; x < CX - nextHw; x++)
                            Plot(c, x, y + 1, '▀', t.OutlineColor);
                        for (int x = CX + nextHw + 1; x <= CX + hw; x++)
                            Plot(c, x, y + 1, '▀', t.OutlineColor);
                    }
                }
            }
        }

        #endregion

        #region Draw Eyes

        private static void DrawEyes(Cell[,] c, Traits t)
        {
            int y = t.EyeRow;
            if (y < 0 || y >= H || t.HeadWidths[y] < 0) return;

            int hw = t.HeadWidths[y];
            int eyeOff = Math.Max(3, hw / 3 + 1);
            int lx = CX - eyeOff;  // left eye center
            int rx = CX + eyeOff;  // right eye center

            switch (t.EyeStyle)
            {
                case 0: // Block eyes ██
                    StampEye(c, lx, y, t.EyeColor, '█', '█');
                    StampEye(c, rx, y, t.EyeColor, '█', '█');
                    break;
                case 1: // Wide eyes ▓█▓
                    Plot(c, lx - 1, y, '▓', t.EyeColor);
                    StampEye(c, lx, y, t.EyeColor, '█', '█');
                    Plot(c, rx + 1, y, '▓', t.EyeColor);
                    StampEye(c, rx, y, t.EyeColor, '█', '█');
                    break;
                case 2: // Narrow ▓
                    Plot(c, lx, y, '▓', t.EyeColor);
                    Plot(c, rx, y, '▓', t.EyeColor);
                    break;
                case 3: // Deep-set with brow shadow
                    if (y > 0)
                    {
                        Plot(c, lx - 1, y - 1, '▓', t.SkinDark);
                        Plot(c, lx, y - 1, '▓', t.SkinDark);
                        Plot(c, lx + 1, y - 1, '▓', t.SkinDark);
                        Plot(c, rx - 1, y - 1, '▓', t.SkinDark);
                        Plot(c, rx, y - 1, '▓', t.SkinDark);
                        Plot(c, rx + 1, y - 1, '▓', t.SkinDark);
                    }
                    StampEye(c, lx, y, t.EyeColor, '█', '█');
                    StampEye(c, rx, y, t.EyeColor, '█', '█');
                    break;
                case 4: // Almond ▄█▄
                    Plot(c, lx - 1, y, '▄', t.EyeColor);
                    Plot(c, lx, y, '█', t.EyeColor);
                    Plot(c, lx + 1, y, '▄', t.EyeColor);
                    Plot(c, rx - 1, y, '▄', t.EyeColor);
                    Plot(c, rx, y, '█', t.EyeColor);
                    Plot(c, rx + 1, y, '▄', t.EyeColor);
                    break;
                case 5: // Glowing ░█░
                    Plot(c, lx - 1, y, '░', t.EyeColor);
                    Plot(c, lx, y, '█', t.EyeColor);
                    Plot(c, lx + 1, y, '░', t.EyeColor);
                    Plot(c, rx - 1, y, '░', t.EyeColor);
                    Plot(c, rx, y, '█', t.EyeColor);
                    Plot(c, rx + 1, y, '░', t.EyeColor);
                    break;
                case 6: // Scarred (right eye damaged)
                    StampEye(c, lx, y, t.EyeColor, '█', '█');
                    Plot(c, rx - 1, y, '─', t.SkinDark);
                    Plot(c, rx, y, '▓', t.SkinDark);
                    Plot(c, rx + 1, y, '─', t.SkinDark);
                    break;
                case 7: // Round with socket shadow
                    Plot(c, lx - 1, y, '▓', t.SkinDark);
                    StampEye(c, lx, y, t.EyeColor, '█', '░');
                    Plot(c, rx - 1, y, '░', t.EyeColor);
                    Plot(c, rx, y, '█', t.EyeColor);
                    Plot(c, rx + 1, y, '▓', t.SkinDark);
                    break;
            }

            // Eye patch accessory (covers right eye)
            if ((t.AccessoryBits & 2) != 0)
            {
                Plot(c, rx - 1, y, '█', "gray");
                Plot(c, rx, y, '█', "gray");
                Plot(c, rx + 1, y, '█', "gray");
                if (y > 0) Plot(c, rx, y - 1, '▓', "gray");
            }
        }

        private static void StampEye(Cell[,] c, int x, int y, string color, char c1, char c2)
        {
            Plot(c, x, y, c1, color);
            Plot(c, x + 1, y, c2, color);
        }

        #endregion

        #region Draw Nose

        private static void DrawNose(Cell[,] c, Traits t)
        {
            int y = t.NoseRow;
            if (y < 0 || y >= H || t.HeadWidths[y] < 0) return;

            switch (t.NoseStyle)
            {
                case 0: // Small bump
                    Plot(c, CX, y, '▓', t.SkinDark);
                    Plot(c, CX + 1, y, '▓', t.SkinDark);
                    break;
                case 1: // Large nose
                    Plot(c, CX - 1, y, '▓', t.SkinDark);
                    Plot(c, CX, y, '█', t.SkinDark);
                    Plot(c, CX + 1, y, '█', t.SkinDark);
                    Plot(c, CX + 2, y, '▓', t.SkinDark);
                    break;
                case 2: // Pointed (two-row)
                    Plot(c, CX, y, '▄', t.SkinDark);
                    Plot(c, CX + 1, y, '▄', t.SkinDark);
                    if (y + 1 < H)
                    {
                        Plot(c, CX, y + 1, '▀', t.SkinDark);
                        Plot(c, CX + 1, y + 1, '▀', t.SkinDark);
                    }
                    break;
                case 3: // Flat/wide
                    for (int x = CX - 1; x <= CX + 2; x++)
                        Plot(c, x, y, '▓', t.SkinDark);
                    break;
                case 4: // Hooked (with bridge)
                    if (y > 0) Plot(c, CX, y - 1, '▄', t.SkinDark);
                    Plot(c, CX, y, '█', t.SkinDark);
                    Plot(c, CX + 1, y, '▄', t.SkinDark);
                    break;
                case 5: // Button (tiny)
                    Plot(c, CX, y, '░', t.SkinDark);
                    Plot(c, CX + 1, y, '░', t.SkinDark);
                    break;
            }
        }

        #endregion

        #region Draw Mouth

        private static void DrawMouth(Cell[,] c, Traits t)
        {
            int y = t.MouthRow;
            if (y < 0 || y >= H || t.HeadWidths[y] < 0) return;

            switch (t.MouthStyle)
            {
                case 0: // Neutral
                    for (int x = CX - 3; x <= CX + 3; x++)
                        Plot(c, x, y, '▓', t.MouthColor);
                    break;
                case 1: // Grin (wide, curved)
                    Plot(c, CX - 4, y, '▄', t.MouthColor);
                    for (int x = CX - 3; x <= CX + 3; x++)
                        Plot(c, x, y, '▓', t.MouthColor);
                    Plot(c, CX + 4, y, '▄', t.MouthColor);
                    break;
                case 2: // Scowl (heavy)
                    for (int x = CX - 3; x <= CX + 3; x++)
                        Plot(c, x, y, '█', t.MouthColor);
                    break;
                case 3: // Smirk (asymmetric)
                    for (int x = CX - 2; x <= CX + 3; x++)
                        Plot(c, x, y, '▓', t.MouthColor);
                    Plot(c, CX + 4, y, '▄', t.MouthColor);
                    break;
                case 4: // Open mouth
                    Plot(c, CX - 3, y, '▓', t.MouthColor);
                    for (int x = CX - 2; x <= CX + 2; x++)
                        Plot(c, x, y, '█', "gray");
                    Plot(c, CX + 3, y, '▓', t.MouthColor);
                    break;
                case 5: // Thin lips
                    for (int x = CX - 2; x <= CX + 2; x++)
                        Plot(c, x, y, '░', t.MouthColor);
                    break;
            }

            // Tusks for orcs/trolls
            if ((t.AccessoryBits & 4) != 0)
            {
                Plot(c, CX - 5, y, '▀', "white");
                Plot(c, CX + 5, y, '▀', "white");
                Plot(c, CX - 5, y - 1, '░', "white");
                Plot(c, CX + 5, y - 1, '░', "white");
            }
        }

        #endregion

        #region Draw Hair

        private static void DrawHair(Cell[,] c, Traits t)
        {
            string hc = t.HairColor;

            // Find the first head row
            int firstY = -1;
            for (int i = 0; i < H; i++)
                if (t.HeadWidths[i] >= 0) { firstY = i; break; }
            if (firstY < 0) return;
            int topHw = t.HeadWidths[firstY];

            switch (t.HairStyle)
            {
                case 0: // Bald — no hair at all
                    break;

                case 1: // Stubble/buzzcut — thin layer on top
                    if (firstY > 0)
                        for (int x = CX - topHw + 2; x <= CX + topHw - 2; x++)
                            PlotIfEmpty(c, x, firstY - 1, '░', hc);
                    break;

                case 2: // Short crop
                    DrawHairMass(c, hc, firstY, topHw, rowsAbove: 1, sideRows: 0, sideExtra: 0);
                    break;

                case 3: // Medium
                    DrawHairMass(c, hc, firstY, topHw, rowsAbove: 2, sideRows: 2, sideExtra: 1);
                    break;

                case 4: // Long flowing
                    DrawHairMass(c, hc, firstY, topHw, rowsAbove: 2, sideRows: 6, sideExtra: 2);
                    break;

                case 5: // Mohawk
                    for (int dy = -2; dy <= 0; dy++)
                    {
                        int y = firstY + dy;
                        if (y < 0) continue;
                        int mhw = 2 - Math.Abs(dy);
                        for (int x = CX - mhw; x <= CX + mhw; x++)
                            Plot(c, x, y, '█', hc);
                    }
                    break;

                case 6: // Spiky
                    DrawHairMass(c, hc, firstY, topHw, rowsAbove: 1, sideRows: 0, sideExtra: 0);
                    // Add spikes above
                    if (firstY >= 2)
                    {
                        for (int x = CX - topHw + 2; x <= CX + topHw - 2; x += 3)
                        {
                            Plot(c, x, firstY - 2, '▀', hc);
                            Plot(c, x + 1, firstY - 2, '▀', hc);
                        }
                    }
                    break;

                case 7: // Wild/messy
                    DrawHairMass(c, hc, firstY, topHw, rowsAbove: 2, sideRows: 3, sideExtra: 2);
                    // Add stray wisps
                    if (firstY >= 3)
                    {
                        PlotIfEmpty(c, CX - topHw - 1, firstY, '░', hc);
                        PlotIfEmpty(c, CX + topHw + 1, firstY, '░', hc);
                        PlotIfEmpty(c, CX - 2, firstY - 3, '▀', hc);
                        PlotIfEmpty(c, CX + 3, firstY - 3, '▀', hc);
                    }
                    break;

                case 8: // Braided sides
                    DrawHairMass(c, hc, firstY, topHw, rowsAbove: 2, sideRows: 5, sideExtra: 1);
                    // Braid accents on sides
                    for (int dy = 2; dy < 7; dy++)
                    {
                        int y = firstY + dy;
                        int hw = (y < H) ? t.HeadWidths[y] : -1;
                        if (hw < 0) continue;
                        char bc = (dy % 2 == 0) ? '▓' : '░';
                        Plot(c, CX - hw - 1, y, bc, hc);
                        Plot(c, CX + hw + 1, y, bc, hc);
                    }
                    break;

                case 9: // Topknot/bun
                    // Small bun above head
                    if (firstY >= 3)
                    {
                        for (int x = CX - 2; x <= CX + 2; x++)
                            Plot(c, x, firstY - 2, '█', hc);
                        for (int x = CX - 3; x <= CX + 3; x++)
                            Plot(c, x, firstY - 1, '▓', hc);
                    }
                    break;
            }
        }

        private static void DrawHairMass(Cell[,] c, string hc, int firstY, int topHw,
            int rowsAbove, int sideRows, int sideExtra)
        {
            // Draw hair mass above the head
            for (int dy = -rowsAbove; dy <= 0; dy++)
            {
                int y = firstY + dy;
                if (y < 0) continue;
                int w = topHw + 1 + sideExtra + dy; // Slightly wider at base, narrower at top
                if (w < 2) w = 2;
                char ch = (dy < 0) ? '▓' : '█';
                for (int x = CX - w; x <= CX + w; x++)
                    Plot(c, x, y, ch, hc);
            }

            // Draw side curtains (for long hair)
            for (int dy = 1; dy <= sideRows; dy++)
            {
                int y = firstY + dy;
                if (y >= H) break;
                int hw = t_headWidthAt(c, y, topHw);
                Plot(c, CX - hw - 1, y, '▓', hc);
                if (sideExtra > 0)
                    Plot(c, CX - hw - 2, y, '░', hc);
                Plot(c, CX + hw + 1, y, '▓', hc);
                if (sideExtra > 0)
                    Plot(c, CX + hw + 2, y, '░', hc);
            }
        }

        // Helper to get head width at a row from canvas (looks for outline blocks)
        private static int t_headWidthAt(Cell[,] c, int y, int fallback)
        {
            // Scan right from center to find the outline
            for (int x = CX; x < W; x++)
            {
                if (c[y, x].Ch == '█')
                    return x - CX;
            }
            return fallback;
        }

        #endregion

        #region Draw Beard

        private static void DrawBeard(Cell[,] c, Traits t)
        {
            if (t.BeardStyle <= 0) return;
            string bc = t.HairColor; // Beard matches hair

            // Find mouth row and chin area
            int mouthY = t.MouthRow;
            int lastY = -1;
            for (int i = H - 1; i >= 0; i--)
                if (t.HeadWidths[i] >= 0) { lastY = i; break; }
            if (lastY < 0) return;

            int startY = mouthY + 1;

            switch (t.BeardStyle)
            {
                case 1: // Stubble
                    for (int y = startY; y <= lastY; y++)
                    {
                        int hw = t.HeadWidths[y];
                        if (hw < 0) continue;
                        for (int x = CX - hw + 2; x <= CX + hw - 2; x += 2)
                            Plot(c, x, y, '░', bc);
                    }
                    break;

                case 2: // Short beard
                    for (int y = startY; y <= lastY; y++)
                    {
                        int hw = t.HeadWidths[y];
                        if (hw < 0) continue;
                        for (int x = CX - hw + 2; x <= CX + hw - 2; x++)
                            Plot(c, x, y, '▒', bc);
                    }
                    break;

                case 3: // Full beard
                    for (int y = startY; y <= lastY + 1; y++)
                    {
                        int hw = (y <= lastY && t.HeadWidths[y] >= 0) ? t.HeadWidths[y] : 4;
                        for (int x = CX - hw + 1; x <= CX + hw - 1; x++)
                            Plot(c, x, y, '▓', bc);
                    }
                    break;

                case 4: // Long beard
                    for (int y = startY; y <= Math.Min(lastY + 2, H - 1); y++)
                    {
                        int hw;
                        if (y <= lastY && t.HeadWidths[y] >= 0) hw = t.HeadWidths[y];
                        else hw = Math.Max(2, 5 - (y - lastY));
                        for (int x = CX - hw + 1; x <= CX + hw - 1; x++)
                            Plot(c, x, y, '▓', bc);
                    }
                    break;

                case 5: // Braided beard (dwarf special)
                    for (int y = startY; y <= Math.Min(lastY + 2, H - 1); y++)
                    {
                        int hw;
                        if (y <= lastY && t.HeadWidths[y] >= 0) hw = t.HeadWidths[y];
                        else hw = Math.Max(2, 5 - (y - lastY));
                        for (int x = CX - hw + 1; x <= CX + hw - 1; x++)
                        {
                            char ch = ((x + y) % 2 == 0) ? '▓' : '░';
                            Plot(c, x, y, ch, bc);
                        }
                    }
                    break;

                case 6: // Massive beard (dwarf epic)
                    for (int y = startY; y <= Math.Min(lastY + 3, H - 1); y++)
                    {
                        int hw;
                        if (y <= lastY && t.HeadWidths[y] >= 0) hw = t.HeadWidths[y] + 1;
                        else hw = Math.Max(3, 7 - (y - lastY));
                        for (int x = CX - hw; x <= CX + hw; x++)
                        {
                            char ch = ((x + y) % 3 == 0) ? '░' : '▓';
                            Plot(c, x, y, ch, bc);
                        }
                    }
                    break;
            }
        }

        #endregion

        #region Draw Ears

        private static void DrawEars(Cell[,] c, Traits t)
        {
            int earRow = t.EyeRow; // Ears at eye level
            int hw = t.HeadWidths[earRow];
            if (hw < 0) return;

            switch (t.Race)
            {
                case CharacterRace.Elf:
                case CharacterRace.HalfElf:
                    // Pointed ears extending outward and up
                    Plot(c, CX - hw - 1, earRow, '▓', t.SkinColor);
                    Plot(c, CX - hw - 2, earRow - 1, '▓', t.SkinColor);
                    Plot(c, CX - hw - 3, earRow - 2, '░', t.SkinColor);
                    Plot(c, CX + hw + 1, earRow, '▓', t.SkinColor);
                    Plot(c, CX + hw + 2, earRow - 1, '▓', t.SkinColor);
                    Plot(c, CX + hw + 3, earRow - 2, '░', t.SkinColor);
                    break;

                case CharacterRace.Gnoll:
                    // Pointed ears up
                    Plot(c, CX - hw - 1, earRow, '▓', t.SkinColor);
                    Plot(c, CX - hw - 1, earRow - 1, '▀', t.SkinColor);
                    Plot(c, CX + hw + 1, earRow, '▓', t.SkinColor);
                    Plot(c, CX + hw + 1, earRow - 1, '▀', t.SkinColor);
                    break;

                case CharacterRace.Orc:
                case CharacterRace.Troll:
                    // Small pointed ears
                    Plot(c, CX - hw - 1, earRow, '▓', t.SkinColor);
                    Plot(c, CX + hw + 1, earRow, '▓', t.SkinColor);
                    break;

                default:
                    // No visible ears for most races (hidden by head width)
                    break;
            }
        }

        #endregion

        #region Draw Accessories

        private static void DrawAccessories(Cell[,] c, Traits t)
        {
            // Scar across face
            if ((t.AccessoryBits & 1) != 0)
            {
                int scarY = t.EyeRow + 1;
                if (scarY < H && t.HeadWidths[scarY] >= 0)
                {
                    int hw = t.HeadWidths[scarY];
                    // Diagonal scar from upper-left to lower-right
                    for (int i = 0; i < 4; i++)
                    {
                        int x = CX - 2 + i;
                        int y = scarY - 1 + i;
                        if (y >= 0 && y < H && x >= CX - hw && x <= CX + hw)
                            Plot(c, x, y, '▒', "red");
                    }
                }
            }
        }

        #endregion

        #region Draw Atmosphere

        private static void DrawAtmosphere(Cell[,] c, Traits t, Rng rng)
        {
            // Scatter 10-16 atmospheric particles around the edges
            int count = rng.Range(10, 17);
            string ac = t.AtmoColor;
            char ach = t.AtmoChar;

            for (int i = 0; i < count; i++)
            {
                int x = rng.Next(W);
                int y = rng.Next(H);

                // Only place in areas that will be empty (edges, not face center)
                bool isEdge = x < 5 || x >= W - 5 || y < 2 || y >= H - 2;
                if (isEdge)
                    PlotIfEmpty(c, x, y, ach, ac);
            }
        }

        #endregion

        #region Render

        private static string[] Render(Cell[,] canvas, Character npc, Traits t)
        {
            string bc = t.BorderColor;
            var lines = new List<string>();

            // Top border
            lines.Add($"[{bc}]   ╔══════════════════════════════════╗");

            // Canvas rows
            for (int y = 0; y < H; y++)
            {
                var sb = new StringBuilder();
                sb.Append($"[{bc}]   ║");

                string curColor = bc;
                for (int x = 0; x < W; x++)
                {
                    var cell = canvas[y, x];
                    char ch = cell.Ch == '\0' ? ' ' : cell.Ch;
                    string color = cell.Color ?? "";

                    if (ch != ' ' && color.Length > 0 && color != curColor)
                    {
                        sb.Append($"[{color}]");
                        curColor = color;
                    }
                    sb.Append(ch);
                }

                sb.Append($"[{bc}]║");
                lines.Add(sb.ToString());
            }

            // Name bar
            string name = npc.Name2 ?? npc.Name1 ?? "Unknown";
            if (name.Length > 32) name = name.Substring(0, 32);

            string raceClass = $"{npc.Race} {npc.Class}";
            if (raceClass.Length > 32) raceClass = raceClass.Substring(0, 32);

            lines.Add($"[{bc}]   ╠══════════════════════════════════╣");
            lines.Add($"[{bc}]   ║[bright_yellow]{CenterText(name, 34)}[{bc}]║");
            lines.Add($"[{bc}]   ║[gray]{CenterText(raceClass, 34)}[{bc}]║");
            lines.Add($"[{bc}]   ╚══════════════════════════════════╝");
            lines.Add("[/]");

            return lines.ToArray();
        }

        #endregion

        #region Utilities

        private static uint DJB2(string s)
        {
            uint h = 5381;
            foreach (char c in s) h = ((h << 5) + h) + c;
            return h;
        }

        private static string GetClassColor(CharacterClass cls)
        {
            return cls switch
            {
                CharacterClass.Warrior => "bright_red",
                CharacterClass.Paladin => "bright_yellow",
                CharacterClass.Magician => "bright_magenta",
                CharacterClass.Cleric => "white",
                CharacterClass.Assassin => "dark_magenta",
                CharacterClass.Ranger => "bright_green",
                CharacterClass.Barbarian => "red",
                CharacterClass.Bard => "bright_cyan",
                CharacterClass.Sage => "cyan",
                CharacterClass.Jester => "yellow",
                CharacterClass.Alchemist => "green",
                _ => "gray"
            };
        }

        private static string CenterText(string text, int width)
        {
            if (text.Length >= width) return text.Substring(0, width);
            int pad = (width - text.Length) / 2;
            return text.PadLeft(text.Length + pad).PadRight(width);
        }

        #endregion
    }
}
