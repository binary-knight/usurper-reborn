using System;
using System.Collections.Generic;
using System.Text;

namespace UsurperRemake.UI
{
    /// <summary>
    /// Terminal display width of text. A Latin character occupies one column; an East Asian
    /// character (Chinese, Japanese, Korean), a fullwidth form or an emoji occupies two, and a
    /// combining mark or zero width joiner occupies none. Layout that pads or measures by
    /// string length lines up for Latin text but shears for these scripts, so boxes, centred
    /// titles and column lists measure here instead of with String.Length.
    ///
    /// Nothing here strips markup: callers measure text they have already reduced to what the
    /// terminal draws (UIHelper.VisibleLength strips ANSI first, TerminalEmulator.AdvanceColumn
    /// strips ANSI and [colour] markup first).
    /// </summary>
    public static class TextWidth
    {
        /// <summary>
        /// Whether this code point is drawn in two columns (East Asian Wide and Fullwidth).
        /// Ranges follow the Unicode EastAsianWidth property's W and F classes, plus the emoji
        /// blocks terminals draw double wide.
        /// </summary>
        public static bool IsWideCodePoint(int cp)
        {
            // Control characters never advance the column.
            if (cp < 32 || (cp >= 0x7F && cp < 0xA0)) return false;

            return cp switch
            {
                >= 0x1100 and <= 0x115F => true,     // Hangul Jamo (leading)
                0x2329 or 0x232A => true,            // angle brackets
                >= 0x2E80 and <= 0x303E => true,     // CJK radicals, Kangxi, CJK symbols and punctuation
                >= 0x3041 and <= 0x33FF => true,     // Hiragana, Katakana, Bopomofo, Hangul compat jamo, CJK strokes
                >= 0x3400 and <= 0x4DBF => true,     // CJK unified ideographs extension A
                >= 0x4E00 and <= 0x9FFF => true,     // CJK unified ideographs
                >= 0xA000 and <= 0xA4CF => true,     // Yi syllables
                >= 0xA960 and <= 0xA97F => true,     // Hangul Jamo extension A
                >= 0xAC00 and <= 0xD7A3 => true,     // Hangul syllables
                >= 0xF900 and <= 0xFAFF => true,     // CJK compatibility ideographs
                >= 0xFE10 and <= 0xFE19 => true,     // vertical forms
                >= 0xFE30 and <= 0xFE6F => true,     // CJK compatibility forms
                >= 0xFF00 and <= 0xFF60 => true,     // fullwidth forms
                >= 0xFFE0 and <= 0xFFE6 => true,     // fullwidth signs
                >= 0x1F300 and <= 0x1F64F => true,   // emoji and pictographs
                >= 0x1F900 and <= 0x1F9FF => true,   // supplemental symbols and pictographs
                >= 0x1FA70 and <= 0x1FAFF => true,   // symbols and pictographs extended-A
                >= 0x20000 and <= 0x2FFFD => true,   // CJK unified ideographs extension B and later
                >= 0x30000 and <= 0x3FFFD => true,   // CJK unified ideographs extension G and later
                _ => false,
            };
        }

        /// <summary>
        /// Whether this code point occupies no column: combining marks, variation selectors,
        /// zero width joiners and other format characters.
        /// </summary>
        public static bool IsZeroWidthCodePoint(int cp) => cp switch
        {
            >= 0x0300 and <= 0x036F => true,   // combining diacritical marks
            >= 0x0483 and <= 0x0489 => true,   // Cyrillic combining marks
            >= 0x0591 and <= 0x05BD => true,   // Hebrew accents
            >= 0x0610 and <= 0x061A => true,   // Arabic marks
            >= 0x064B and <= 0x065F => true,   // Arabic marks
            >= 0x0E31 and <= 0x0E31 => true,   // Thai vowel sign
            >= 0x0E34 and <= 0x0E3A => true,   // Thai vowel signs and tone marks
            >= 0x0E47 and <= 0x0E4E => true,   // Thai marks
            >= 0x1AB0 and <= 0x1AFF => true,   // combining diacritical marks extended
            >= 0x1DC0 and <= 0x1DFF => true,   // combining diacritical marks supplement
            >= 0x200B and <= 0x200F => true,   // zero width space, joiners, direction marks
            >= 0x20D0 and <= 0x20F0 => true,   // combining marks for symbols
            0x2060 => true,                    // word joiner
            0xFEFF => true,                    // byte order mark / zero width no-break space
            >= 0xFE00 and <= 0xFE0F => true,   // variation selectors
            >= 0xFE20 and <= 0xFE2F => true,   // combining half marks
            >= 0xE0100 and <= 0xE01EF => true, // variation selectors supplement
            _ => false,
        };

        /// <summary>
        /// The columns one code point occupies. A C0/C1 control character occupies none: the
        /// layout code measures single rows, and a stray newline must not count as a column.
        /// </summary>
        public static int CodePointWidth(int cp)
        {
            if (cp < 0) return 0;
            if (cp < 32 || (cp >= 0x7F && cp < 0xA0)) return 0;
            if (IsZeroWidthCodePoint(cp)) return 0;
            if (IsWideCodePoint(cp)) return 2;
            return 1;
        }

        /// <summary>
        /// The columns this text occupies as a terminal draws it. Surrogate pairs count once
        /// as their code point, so an emoji is two columns and never three or four.
        /// </summary>
        public static int Width(string? text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int width = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    width += CodePointWidth(char.ConvertToUtf32(c, text[i + 1]));
                    i++;
                }
                else if (char.IsLowSurrogate(c))
                {
                    // An unpaired low surrogate draws as one replacement column.
                    width += 1;
                }
                else
                {
                    width += CodePointWidth(c);
                }
            }
            return width;
        }

        /// <summary>Whether the text contains any double column character.</summary>
        public static bool HasWide(string? text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    if (CodePointWidth(char.ConvertToUtf32(c, text[i + 1])) == 2) return true;
                    i++;
                }
                else if (IsWideCodePoint(c)) return true;
            }
            return false;
        }

        /// <summary>
        /// The text padded on the right to at least <paramref name="width"/> display columns.
        /// Text already wider is returned unchanged; callers that need a hard limit truncate first.
        /// </summary>
        public static string PadRight(string? text, int width, char pad = ' ')
        {
            text ??= "";
            int missing = width - Width(text);
            return missing > 0 ? text + new string(pad, missing) : text;
        }

        /// <summary>The text padded on the left to at least <paramref name="width"/> display columns.</summary>
        public static string PadLeft(string? text, int width, char pad = ' ')
        {
            text ??= "";
            int missing = width - Width(text);
            return missing > 0 ? new string(pad, missing) + text : text;
        }

        /// <summary>
        /// The longest prefix of the text that fits in <paramref name="width"/> display columns,
        /// with an optional ASCII ellipsis when the text was cut. A double column character is
        /// never split across the boundary.
        /// </summary>
        public static string Truncate(string? text, int width, string? ellipsis = null)
        {
            text ??= "";
            if (width <= 0) return "";
            if (Width(text) <= width) return text;

            string suffix = ellipsis ?? "";
            int suffixWidth = Width(suffix);
            int budget = width - suffixWidth;
            if (budget <= 0) return CutTo(text, width);

            var sb = new StringBuilder();
            int used = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                int cp;
                int consumed = 1;
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    cp = char.ConvertToUtf32(c, text[i + 1]);
                    consumed = 2;
                }
                else
                {
                    cp = c;
                }
                int w = CodePointWidth(cp);
                if (used + w > budget) break;
                sb.Append(c);
                if (consumed == 2) sb.Append(text[i + 1]);
                used += w;
                i += consumed - 1;
            }
            return sb.ToString() + suffix;
        }

        /// <summary>Cut without an ellipsis, never splitting a double column character.</summary>
        private static string CutTo(string text, int width)
        {
            var sb = new StringBuilder();
            int used = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                int cp;
                int consumed = 1;
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    cp = char.ConvertToUtf32(c, text[i + 1]);
                    consumed = 2;
                }
                else
                {
                    cp = c;
                }
                int w = CodePointWidth(cp);
                if (used + w > width) break;
                sb.Append(c);
                if (consumed == 2) sb.Append(text[i + 1]);
                used += w;
                i += consumed - 1;
            }
            return sb.ToString();
        }

        /// <summary>
        /// The text cut into pieces no wider than <paramref name="width"/> display columns, each
        /// continuing where the previous one ended. Used to break a run of text with no spaces in
        /// it, which East Asian prose is: every piece after the first starts a new row. A single
        /// character wider than the limit still gets its own piece so no text is lost.
        /// </summary>
        public static List<string> SplitToFit(string? text, int width)
        {
            var pieces = new List<string>();
            if (string.IsNullOrEmpty(text)) return pieces;
            if (width <= 0) { pieces.Add(text); return pieces; }

            var sb = new StringBuilder();
            int used = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                int cp;
                int consumed = 1;
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    cp = char.ConvertToUtf32(c, text[i + 1]);
                    consumed = 2;
                }
                else
                {
                    cp = c;
                }
                int w = CodePointWidth(cp);
                if (used + w > width && sb.Length > 0)
                {
                    pieces.Add(sb.ToString());
                    sb.Clear();
                    used = 0;
                }
                sb.Append(c);
                if (consumed == 2) sb.Append(text[i + 1]);
                used += w;
                i += consumed - 1;
            }
            if (sb.Length > 0) pieces.Add(sb.ToString());
            return pieces;
        }

        /// <summary>
        /// The index of the last space in the longest prefix that fits in
        /// <paramref name="width"/> columns, or -1 when no space fits. For single width text this
        /// is the index LastIndexOf(' ', width - 1) finds; for text with wide characters the two
        /// differ, because a column is not a char.
        /// </summary>
        public static int LastSpaceWithin(string? text, int width)
        {
            if (string.IsNullOrEmpty(text) || width <= 0) return -1;
            int columns = 0;
            int best = -1;
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                int cp = c;
                int consumed = 1;
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    cp = char.ConvertToUtf32(c, text[i + 1]);
                    consumed = 2;
                }
                int w = CodePointWidth(cp);
                if (columns + w > width) break;
                if (c == ' ') best = i;
                columns += w;
                i += consumed;
            }
            return best;
        }

        /// <summary>
        /// The longest prefix of the text that fits in <paramref name="width"/> columns, returned;
        /// <paramref name="remainder"/> receives the text after it. The prefix is never empty unless
        /// the text is, so a caller that loops on the remainder always makes progress; a single
        /// character wider than the limit is taken whole.
        /// </summary>
        public static string BreakAt(string? text, int width, out string remainder)
        {
            text ??= "";
            if (text.Length == 0) { remainder = ""; return ""; }
            if (width <= 0)
            {
                remainder = text.Substring(1);
                return text.Substring(0, 1);
            }
            if (Width(text) <= width) { remainder = ""; return text; }

            int used = 0;
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                int cp = c;
                int consumed = 1;
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    cp = char.ConvertToUtf32(c, text[i + 1]);
                    consumed = 2;
                }
                int w = CodePointWidth(cp);
                if (used + w > width) break;
                used += w;
                i += consumed;
            }
            if (i == 0) i = 1;   // never return an empty line for non-empty text
            remainder = text.Substring(i);
            return text.Substring(0, i);
        }
    }
}
