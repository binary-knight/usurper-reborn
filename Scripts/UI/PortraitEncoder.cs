using System;
using System.Collections.Generic;
using System.Text;

namespace UsurperRemake.UI
{
    /// <summary>
    /// v0.65.7 NPC portraits: encodes an RGBA image into terminal ANSI art at
    /// three capability tiers, all in the SAME 34x14-cell footprint the classic
    /// PortraitGenerator uses. Ported from the validated Python proof of concept
    /// (tools/portrait_halfblock_poc.py) -- see the previews in
    /// tools/portrait_poc_out for what each tier looks like.
    ///
    /// The core trick: the UPPER HALF BLOCK character (U+2580) paints two
    /// vertically stacked pixels per cell (foreground = top, background =
    /// bottom), turning 34x14 cells into a 34x28 pixel canvas.
    ///
    /// Tiers:
    ///   TrueColor -- 24-bit SGR (38;2 / 48;2). Web terminal, WezTerm, modern
    ///                MUD clients, local console.
    ///   Xterm256  -- 256-color SGR (38;5 / 48;5) with a shadow gamma lift
    ///                (the 6x6x6 cube has no colors between 0 and 95 per
    ///                channel, so dark tones crush to black without it).
    ///                Conservative default for SSH relays.
    ///   Ansi16    -- classic 16-color BBS ANSI. CP437 has the half blocks
    ///                (0xDF/0xDC) -- this is how the ANSI art scene gets its
    ///                resolution. Per cell we pick the best of upper-half,
    ///                lower-half, or a shade-block mix, with backgrounds
    ///                restricted to the low 8 colors (bright bg blinks on
    ///                real BBS terminals). No dithering: at this scale error
    ///                diffusion reads as glitter, not smoothness.
    ///
    /// Pure and deterministic: same pixels in, same strings out. No I/O.
    /// Each returned row is raw ANSI ending in a reset (\x1b[0m).
    /// </summary>
    public static class PortraitEncoder
    {
        public const int Cols = 34;          // cell columns (matches PortraitGenerator canvas)
        public const int Rows = 14;          // cell rows
        public const int PixelW = Cols;      // pixel canvas width
        public const int PixelH = Rows * 2;  // pixel canvas height via half-blocks

        public enum Tier
        {
            TrueColor,
            Xterm256,
            Ansi16
        }

        // Backdrop the source is composited over (dark violet-black, matches
        // the moody portrait frames the PoC validated).
        private const int BgR = 12, BgG = 10, BgB = 18;

        // Classic VGA 16-color palette as BBS terminals render it.
        private static readonly (int R, int G, int B)[] Ansi16Palette =
        {
            (0, 0, 0), (170, 0, 0), (0, 170, 0), (170, 85, 0),
            (0, 0, 170), (170, 0, 170), (0, 170, 170), (170, 170, 170),
            (85, 85, 85), (255, 85, 85), (85, 255, 85), (255, 255, 85),
            (85, 85, 255), (255, 85, 255), (85, 255, 255), (255, 255, 255),
        };

        // xterm-256 palette entries 16..255 (6x6x6 cube + 24 grays).
        private static readonly Lazy<(int R, int G, int B)[]> Xterm256Palette = new(() =>
        {
            var pal = new (int, int, int)[240];
            int[] steps = { 0, 95, 135, 175, 215, 255 };
            int i = 0;
            for (int r = 0; r < 6; r++)
                for (int g = 0; g < 6; g++)
                    for (int b = 0; b < 6; b++)
                        pal[i++] = (steps[r], steps[g], steps[b]);
            for (int g = 0; g < 24; g++)
            {
                int v = 8 + g * 10;
                pal[i++] = (v, v, v);
            }
            return pal;
        });

        // Ansi16 shade-mix candidates: (mix color, fg, bg, glyph, glitter penalty).
        // Bg restricted to low 8. Penalty discourages half-mixes of two distant
        // colors, which read as speckle at cell scale.
        private static readonly Lazy<List<(double R, double G, double B, int Fg, int Bg, char Ch, double Penalty)>> ShadeMixes = new(() =>
        {
            var list = new List<(double, double, double, int, int, char, double)>();
            (char Ch, double Cov)[] shades = { ('░', 0.25), ('▒', 0.5), ('▓', 0.75) };
            for (int f = 0; f < 16; f++)
            {
                for (int b = 0; b < 8; b++)
                {
                    double fgbgDist = WeightedDistSq(
                        Ansi16Palette[f].R, Ansi16Palette[f].G, Ansi16Palette[f].B,
                        Ansi16Palette[b].R, Ansi16Palette[b].G, Ansi16Palette[b].B);
                    foreach (var (ch, cov) in shades)
                    {
                        double mr = Ansi16Palette[f].R * cov + Ansi16Palette[b].R * (1 - cov);
                        double mg = Ansi16Palette[f].G * cov + Ansi16Palette[b].G * (1 - cov);
                        double mb = Ansi16Palette[f].B * cov + Ansi16Palette[b].B * (1 - cov);
                        list.Add((mr, mg, mb, f, b, ch, 0.25 * cov * (1 - cov) * fgbgDist));
                    }
                }
            }
            return list;
        });

        /// <summary>
        /// Encode an RGBA image (any size) into Rows raw-ANSI strings at the
        /// requested tier. Alpha is composited over the dark backdrop, then
        /// the image is box-downsampled to the 34x28 pixel canvas.
        /// </summary>
        public static string[] Encode(byte[] rgba, int width, int height, Tier tier)
        {
            if (rgba == null || width <= 0 || height <= 0 || rgba.Length < width * height * 4)
                throw new ArgumentException("Invalid image buffer");

            var px = Downsample(rgba, width, height);
            return tier switch
            {
                Tier.TrueColor => EncodeTrueColor(px),
                Tier.Xterm256 => EncodeXterm256(px),
                _ => EncodeAnsi16(px),
            };
        }

        // ─── Downsample ─────────────────────────────────────────────

        /// <summary>
        /// Box-filter downsample to PixelW x PixelH with alpha composite over
        /// the backdrop. Box averaging over the (much larger) source region is
        /// effectively the "render high, sample down" step that gives the art
        /// its anti-aliased look.
        /// </summary>
        private static (byte R, byte G, byte B)[,] Downsample(byte[] rgba, int width, int height)
        {
            var outPx = new (byte, byte, byte)[PixelH, PixelW];
            for (int ty = 0; ty < PixelH; ty++)
            {
                int sy0 = ty * height / PixelH;
                int sy1 = Math.Max(sy0 + 1, (ty + 1) * height / PixelH);
                for (int tx = 0; tx < PixelW; tx++)
                {
                    int sx0 = tx * width / PixelW;
                    int sx1 = Math.Max(sx0 + 1, (tx + 1) * width / PixelW);
                    long r = 0, g = 0, b = 0;
                    int n = 0;
                    for (int sy = sy0; sy < sy1; sy++)
                    {
                        for (int sx = sx0; sx < sx1; sx++)
                        {
                            int o = (sy * width + sx) * 4;
                            int a = rgba[o + 3];
                            // Composite over backdrop
                            r += (rgba[o] * a + BgR * (255 - a)) / 255;
                            g += (rgba[o + 1] * a + BgG * (255 - a)) / 255;
                            b += (rgba[o + 2] * a + BgB * (255 - a)) / 255;
                            n++;
                        }
                    }
                    outPx[ty, tx] = ((byte)(r / n), (byte)(g / n), (byte)(b / n));
                }
            }
            return outPx;
        }

        // ─── Tier 1: truecolor half-blocks ─────────────────────────

        private static string[] EncodeTrueColor((byte R, byte G, byte B)[,] px)
        {
            var rows = new string[Rows];
            var sb = new StringBuilder(Cols * 40);
            for (int row = 0; row < Rows; row++)
            {
                sb.Clear();
                (int, int, int) lastFg = (-1, -1, -1), lastBg = (-1, -1, -1);
                for (int col = 0; col < Cols; col++)
                {
                    var t = px[row * 2, col];
                    var b = px[row * 2 + 1, col];
                    if ((t.R, t.G, t.B) != lastFg)
                    {
                        sb.Append("\x1b[38;2;").Append(t.R).Append(';').Append(t.G).Append(';').Append(t.B).Append('m');
                        lastFg = (t.R, t.G, t.B);
                    }
                    if ((b.R, b.G, b.B) != lastBg)
                    {
                        sb.Append("\x1b[48;2;").Append(b.R).Append(';').Append(b.G).Append(';').Append(b.B).Append('m');
                        lastBg = (b.R, b.G, b.B);
                    }
                    sb.Append('▀');
                }
                sb.Append("\x1b[0m");
                rows[row] = sb.ToString();
            }
            return rows;
        }

        // ─── Tier 2: xterm-256 half-blocks ─────────────────────────

        private static string[] EncodeXterm256((byte R, byte G, byte B)[,] px)
        {
            var rows = new string[Rows];
            var sb = new StringBuilder(Cols * 24);
            for (int row = 0; row < Rows; row++)
            {
                sb.Clear();
                int lastFg = -1, lastBg = -1;
                for (int col = 0; col < Cols; col++)
                {
                    int fg = NearestXterm256(GammaLift(px[row * 2, col]));
                    int bg = NearestXterm256(GammaLift(px[row * 2 + 1, col]));
                    if (fg != lastFg) { sb.Append("\x1b[38;5;").Append(fg).Append('m'); lastFg = fg; }
                    if (bg != lastBg) { sb.Append("\x1b[48;5;").Append(bg).Append('m'); lastBg = bg; }
                    sb.Append('▀');
                }
                sb.Append("\x1b[0m");
                rows[row] = sb.ToString();
            }
            return rows;
        }

        private static (int R, int G, int B) GammaLift((byte R, byte G, byte B) c)
        {
            return (Lift(c.R), Lift(c.G), Lift(c.B));
            static int Lift(byte v) => (int)Math.Round(255.0 * Math.Pow(v / 255.0, 0.82));
        }

        private static int NearestXterm256((int R, int G, int B) c)
        {
            var pal = Xterm256Palette.Value;
            int best = 0;
            double bd = double.MaxValue;
            for (int i = 0; i < pal.Length; i++)
            {
                double d = WeightedDistSq(c.R, c.G, c.B, pal[i].R, pal[i].G, pal[i].B);
                if (d < bd) { bd = d; best = i; }
            }
            return best + 16;
        }

        // ─── Tier 3: 16-color CP437 half-blocks + shades ───────────

        private static string[] EncodeAnsi16((byte R, byte G, byte B)[,] px)
        {
            var rows = new string[Rows];
            var sb = new StringBuilder(Cols * 16);
            for (int row = 0; row < Rows; row++)
            {
                sb.Clear();
                int lastFg = -1, lastBg = -1;
                for (int col = 0; col < Cols; col++)
                {
                    var top = px[row * 2, col];
                    var bot = px[row * 2 + 1, col];

                    // Candidate A: upper half-block (fg paints top, bg paints bottom)
                    int fa = Nearest16(top, 16), ba = Nearest16(bot, 8);
                    double ea = DistTo16(top, fa) + DistTo16(bot, ba);
                    // Candidate B: lower half-block (fg paints bottom, bg paints top)
                    int fb = Nearest16(bot, 16), bb = Nearest16(top, 8);
                    double eb = DistTo16(bot, fb) + DistTo16(top, bb);
                    // Candidate C: best shade mix applied to the whole cell
                    double avgR = (top.R + bot.R) / 2.0, avgG = (top.G + bot.G) / 2.0, avgB = (top.B + bot.B) / 2.0;
                    double ec = double.MaxValue;
                    int cf = 0, cb = 0;
                    char cch = '░';
                    foreach (var m in ShadeMixes.Value)
                    {
                        double d = WeightedDistSq(avgR, avgG, avgB, m.R, m.G, m.B) * 2 + m.Penalty;
                        if (d < ec) { ec = d; cf = m.Fg; cb = m.Bg; cch = m.Ch; }
                    }

                    int fg, bg;
                    char ch;
                    if (ea <= eb && ea <= ec) { fg = fa; bg = ba; ch = '▀'; }
                    else if (eb <= ec) { fg = fb; bg = bb; ch = '▄'; }
                    else { fg = cf; bg = cb; ch = cch; }

                    if (fg != lastFg || bg != lastBg)
                    {
                        sb.Append("\x1b[").Append(fg >= 8 ? '1' : '0')
                          .Append(';').Append(30 + (fg % 8))
                          .Append(';').Append(40 + bg).Append('m');
                        lastFg = fg;
                        lastBg = bg;
                    }
                    sb.Append(ch);
                }
                sb.Append("\x1b[0m");
                rows[row] = sb.ToString();
            }
            return rows;
        }

        private static int Nearest16((byte R, byte G, byte B) c, int count)
        {
            int best = 0;
            double bd = double.MaxValue;
            for (int i = 0; i < count; i++)
            {
                double d = DistTo16(c, i);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        private static double DistTo16((byte R, byte G, byte B) c, int idx) =>
            WeightedDistSq(c.R, c.G, c.B, Ansi16Palette[idx].R, Ansi16Palette[idx].G, Ansi16Palette[idx].B);

        /// <summary>Perceptually weighted squared color distance (approx redmean weights).</summary>
        private static double WeightedDistSq(double r1, double g1, double b1, double r2, double g2, double b2)
        {
            double dr = r1 - r2, dg = g1 - g2, db = b1 - b2;
            return 2 * dr * dr + 4 * dg * dg + 3 * db * db;
        }
    }
}
