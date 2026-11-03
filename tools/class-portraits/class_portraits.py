#!/usr/bin/env python3
"""Class portrait converter (1.3.10).

Turns the class sprites in electron-client/assets/classes-hd/<class>.png into the terminal portraits the class
preview shows during character creation, and writes them as the C# data file Scripts/UI/ClassPortraitArt.cs.
The game never reads a PNG at runtime; it reads only that generated file.

What it makes, for each of the 13 classes that have no hand drawn portrait (xbit's Warrior, Paladin, Magician and
Assassin portraits live in Scripts/UI/RacePortraits.cs and are not touched):
  - 18 rows of 38 columns of half block cells (each cell is two pixels stacked: upper half block, lower half block,
    full block or space), from a 38 by 36 pixel grid;
  - the head and shoulders: the sprite is cropped to its opaque box, then to a square 58 percent of its height from
    the top, centred on the opaque mass of that top part;
  - a 256 colour set: brightness 1.35 and contrast 1.25, each pixel matched to the xterm 256 palette entries 16 to
    255 by redmean distance, SGR 38;5;n and 48;5;n;
  - a 16 colour set for BBS sessions: brightness 1.8 and contrast 1.25, foreground from the 16 CGA colours (bright
    ones as bold), background from the 8 dark ones, SGR 30 to 37, 40 to 47 and 1;
  - a pixel with alpha under 110 is transparent and shows the terminal's own background.
Every row starts and ends with a colour reset (ESC [0m).

Run from the repository root:
    uv run --no-project --with pillow==12.3.0 python tools/class-portraits/class_portraits.py
or, with pip:
    python3 -m pip install pillow==12.3.0
    python3 tools/class-portraits/class_portraits.py
Pillow is pinned because its LANCZOS resampling can change between versions, and the committed data must equal
this script's output on the committed PNGs (the receipt for 1.3.10 diffs the two). Options:
    --out <path>   write the C# file somewhere else (default Scripts/UI/ClassPortraitArt.cs)
    --check        write nothing; exit 1 when the committed file differs from a fresh conversion
"""
import argparse
import os
import sys

from PIL import Image, ImageEnhance

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
SRC = os.path.join(ROOT, 'electron-client', 'assets', 'classes-hd')
OUT = os.path.join(ROOT, 'Scripts', 'UI', 'ClassPortraitArt.cs')

# CharacterClass enum name -> sprite file name, in enum order. xbit's four are left out on purpose.
CLASSES = [
    ('Alchemist', 'alchemist'), ('Barbarian', 'barbarian'), ('Bard', 'bard'), ('Cleric', 'cleric'),
    ('Jester', 'jester'), ('Ranger', 'ranger'), ('Sage', 'sage'), ('Tidesworn', 'tidesworn'),
    ('Wavecaller', 'wavecaller'), ('Cyclebreaker', 'cyclebreaker'), ('Abysswarden', 'abysswarden'),
    ('Voidreaver', 'voidreaver'), ('MysticShaman', 'mystic-shaman'),
]

COLS, ROWS = 38, 18          # character cells
BUST = 0.58                  # the top part of the sprite kept, as a share of its height
CONTRAST = 1.25
BRIGHT_256 = 1.35
BRIGHT_16 = 1.8
ALPHA_MIN = 110
UPPER, LOWER, FULL = '\u2580', '\u2584', '\u2588'
RESET = '\x1b[0m'

# CGA 16 palette, index 0..15 in ANSI order
P16 = [(0, 0, 0), (170, 0, 0), (0, 170, 0), (170, 85, 0), (0, 0, 170), (170, 0, 170), (0, 170, 170), (170, 170, 170),
       (85, 85, 85), (255, 85, 85), (85, 255, 85), (255, 255, 85), (85, 85, 255), (255, 85, 255), (85, 255, 255),
       (255, 255, 255)]


def xterm256():
    c = list(P16)
    lv = [0, 95, 135, 175, 215, 255]
    for r in lv:
        for g in lv:
            for b in lv:
                c.append((r, g, b))
    for i in range(24):
        v = 8 + 10 * i
        c.append((v, v, v))
    return c


P256 = xterm256()


def near(rgb, pal, idxs):
    """Nearest palette index by redmean distance; the first index wins a tie."""
    best = None
    bd = 1e9
    for i in idxs:
        p = pal[i]
        rm = (p[0] + rgb[0]) / 2
        dr = p[0] - rgb[0]
        dg = p[1] - rgb[1]
        db = p[2] - rgb[2]
        d = (2 + rm / 256) * dr * dr + 4 * dg * dg + (2 + (255 - rm) / 256) * db * db
        if d < bd:
            bd = d
            best = i
    return best


def grid(path, boost):
    """The 38 by 36 RGBA pixel grid of the sprite's head and shoulders, brightened by boost."""
    im = Image.open(path).convert('RGBA')
    im = im.crop(im.getbbox())
    w, h = im.size
    side = int(h * BUST)
    top = im.crop((0, 0, w, side))
    bb = top.getbbox() or (0, 0, w, side)
    cx = (bb[0] + bb[2]) // 2
    x0 = max(0, min(w - side, cx - side // 2)) if w > side else 0
    im = im.crop((x0, 0, x0 + min(side, w), side))
    a = im.split()[3]
    rgb = ImageEnhance.Brightness(im.convert('RGB')).enhance(boost)
    rgb = ImageEnhance.Contrast(rgb).enhance(CONTRAST)
    im = rgb.convert('RGBA')
    im.putalpha(a)
    w, h = im.size
    s = max(w, h)
    sq = Image.new('RGBA', (s, s), (0, 0, 0, 0))
    sq.paste(im, ((s - w) // 2, (s - h) // 2))
    sm = sq.resize((COLS, ROWS * 2), Image.LANCZOS)
    return [[sm.getpixel((x, y)) for x in range(COLS)] for y in range(ROWS * 2)]


def cell16(top, bot):
    """(char, fg 0..15 or None, bg 0..7 or None) for two stacked pixels; None is transparent."""
    t = None if top[3] < ALPHA_MIN else near(top, P16, range(0, 16))
    b = None if bot[3] < ALPHA_MIN else near(bot, P16, range(0, 16))
    if t is None and b is None:
        return (' ', None, None)
    if b is None:
        return (UPPER, t, None)
    if t is None:
        return (LOWER, b, None)
    if t == b:
        return (FULL, t, None)
    if b < 8:
        return (UPPER, t, b)
    if t < 8:
        return (LOWER, b, t)
    bd = near(P16[b], P16, range(0, 8))   # both bright: the lower one darkened to the nearest dark colour
    return (UPPER, t, bd)


def cell256(top, bot):
    t = None if top[3] < ALPHA_MIN else near(top, P256, range(16, 256))
    b = None if bot[3] < ALPHA_MIN else near(bot, P256, range(16, 256))
    if t is None and b is None:
        return (' ', None, None)
    if b is None:
        return (UPPER, t, None)
    if t is None:
        return (LOWER, b, None)
    if t == b:
        return (FULL, t, None)
    return (UPPER, t, b)


def cells(path, sixteen):
    g = grid(path, BRIGHT_16 if sixteen else BRIGHT_256)
    f = cell16 if sixteen else cell256
    return [[f(g[2 * y][x], g[2 * y + 1][x]) for x in range(COLS)] for y in range(ROWS)]


def row256(row):
    """One row as text with 256 colour SGR, written only where the colour changes."""
    out = [RESET]
    fg, bg = None, None
    for ch, f, b in row:
        params = []
        if b != bg:
            params.append('49' if b is None else '48;5;%d' % b)
            bg = b
        if ch != ' ' and f != fg:
            params.append('38;5;%d' % f)
            fg = f
        if params:
            out.append('\x1b[' + ';'.join(params) + 'm')
        out.append(ch)
    out.append(RESET)
    return ''.join(out)


def row16(row):
    """One row as text with 16 colour SGR. Each change resets and sets the whole state, as BBS terminals expect."""
    out = [RESET]
    fg, bg = None, None
    for ch, f, b in row:
        if ch == ' ':
            if bg is not None:
                out.append(RESET)
                fg, bg = None, None
        elif f != fg or b != bg:
            params = ['0']
            if f >= 8:
                params.append('1')
            params.append('3%d' % (f % 8))
            if b is not None:
                params.append('4%d' % b)
            out.append('\x1b[' + ';'.join(params) + 'm')
            fg, bg = f, b
        out.append(ch)
    out.append(RESET)
    return ''.join(out)


def cs_literal(s):
    """A C# string literal: ESC as \\u001b, quotes and backslashes escaped, block characters kept as they are."""
    return '"' + s.replace('\\', '\\\\').replace('"', '\\"').replace('\x1b', '\\u001b') + '"'


def render():
    lines = [
        '// <auto-generated>',
        '// Generated by tools/class-portraits/class_portraits.py from electron-client/assets/classes-hd/*.png.',
        '// Do not edit by hand: change the converter or the sprite and run it again (see its header).',
        '// </auto-generated>',
        'using System.Collections.Generic;',
        '',
        'namespace UsurperRemake.UI',
        '{',
        '    /// <summary>',
        '    /// 1.3.10: class preview portraits for the 13 classes without a hand drawn one, converted from the',
        '    /// class sprites. Each is 18 rows of 38 visible columns; every row starts and ends with ESC [0m.',
        '    /// Xterm256 uses SGR 38;5;n and 48;5;n; Ansi16 uses only SGR 0, 1, 30 to 37 and 40 to 47.',
        '    /// </summary>',
        '    internal static class ClassPortraitArt',
        '    {',
    ]
    for name, sixteen in (('Xterm256', False), ('Ansi16', True)):
        lines.append('        internal static readonly Dictionary<CharacterClass, string[]> %s = new()' % name)
        lines.append('        {')
        for enum_name, file_name in CLASSES:
            rows = cells(os.path.join(SRC, file_name + '.png'), sixteen)
            fmt = row16 if sixteen else row256
            lines.append('            [CharacterClass.%s] = new[]' % enum_name)
            lines.append('            {')
            for r in rows:
                lines.append('                ' + cs_literal(fmt(r)) + ',')
            lines.append('            },')
        lines.append('        };')
        if not sixteen:
            lines.append('')
    lines.append('    }')
    lines.append('}')
    return '\n'.join(lines) + '\n'


def main():
    ap = argparse.ArgumentParser(description='Convert the class sprites into terminal class portraits.')
    ap.add_argument('--out', default=OUT)
    ap.add_argument('--check', action='store_true')
    args = ap.parse_args()
    text = render()
    if args.check:
        with open(args.out, encoding='utf-8', newline='') as fh:
            same = fh.read() == text
        print('class portraits: committed file %s the converter output' % ('equals' if same else 'DIFFERS FROM'))
        return 0 if same else 1
    with open(args.out, 'w', encoding='utf-8', newline='\n') as fh:
        fh.write(text)
    print('wrote %s (%d classes, 2 sets)' % (os.path.relpath(args.out, ROOT), len(CLASSES)))
    return 0


if __name__ == '__main__':
    sys.exit(main())
