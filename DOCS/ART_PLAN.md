# Plan: replacing the ANSI art (terminal game)

Written 2026-09-07 from three inputs: a draft by the implementing agent, a
critique with a commissioning brief from Codex (gpt-6-astra, high effort,
working from the inlined renderer source and public sources), and a review by
the supervisor session against the emulator, the TTYPE probe, the pagination
constants, and the web page. Where the two reviewers differed, the code decided.
Raw receipts: `~/usurper/evidence/codex/art-raw-*.txt`. The Electron client is
out of scope; this is the text game on every transport it has.

## What exists, and why it looks the way it does

- Twelve pieces live as C# string arrays in `Scripts/UI/ANSIArt.cs` (title,
  dungeon entrance, death, boss victory, level up, castle, inn, treasure,
  combat start, game over, new hero, skull), plus `Assets/ASCII/TitleScreen.txt`.
  They are hand-typed block glyphs with inline `[color]` markup, shown at about
  ten call sites through `DisplayArt` and `DisplayArtAnimated`.
- Sizes today: 7 to 18 rows; widths 26 to 79 columns. `LevelUp` was 81 wide
  and `DungeonEntrance` 80 until 1.2.4 trimmed them to 79 and 78
  (`Tests/ArtWidth124Tests.cs` holds every row to 79). Most pieces are lettering
  ("YOU DIED", "LEVEL UP", the title) drawn in blocks, which cannot be
  localized and reads as text, not art.
- The markup and color tables have sixteen foreground colors and no background
  color. The splash screen is the exception: it draws a converted `.ans` file
  with background colors by writing raw ANSI escapes
  (`Scripts/UI/SplashScreen.cs`). Scene ANSI art is built on backgrounds, and
  everything else cannot draw them. The CP437 path is a translation (heavy box to double box,
  look-alikes for the rest), not an encoding of what the artist drew.
- Terminal detection is a substring guess on TTYPE (`SYNCTERM`, `NETRUNNER`
  mean CP437; `VIP`, `DUMB`, `UNKNOWN` mean plain text). Nothing parses MTTS
  (the standard MUD clients speak: bit 4 UTF-8, bit 8 256 colors, bit 64 screen
  reader, bit 256 truecolor). Nothing reads NAWS, so "80 columns" is an
  assumption on every MUD client.
- Door sessions are 25 rows with a pager at 23 and a "-- More --" pause. A
  20-row piece plus a caption plus a prompt crosses it.
- The web terminal is xterm.js 5.5 with whatever monospace the browser falls
  back to; fallback fonts break the joins between half-blocks and full blocks.
- Screen-reader mode skips art entirely. Five languages; no art has captions.

So the pipeline is not an optimization ahead of the art. Without background
colors, a real CP437 encoding, and a size contract, commissioned art cannot be
drawn by this emulator at all.

## The contract for every piece

| Rule | Value | Why |
|---|---|---|
| Width | 78 visible columns, one-column left margin, column 80 untouched | SyncTERM wraps immediately after column 80, so a full-width row plus CRLF eats a line |
| Height | Events 16 rows max; location headers 8; compact variants 4 to 6; title up to 20 on web and desktop only | The door pager at 23 rows; a header must leave the menu room |
| Glyphs | The CP437 block, shade, and box set (bytes B0 to DF) plus space and agreed ASCII punctuation; no control-position graphics, no DEL, no NBSP | Control-position glyphs cannot be emitted as text; `Castle` uses one today |
| Colors | Sixteen foreground, eight background; no blink, no iCE, no 256-color, no truecolor | The same file must survive Synchronet and the web identically; anything that only looks right in truecolor ships broken to the door |
| Text | None: no words, block letters, numerals, signs, or signatures inside the art | Five languages; captions and alt text are Loc keys drawn under the piece |
| Format | `.ans`: CP437 bytes, SGR only (0, 1, 22, 30 to 37, 39, 40 to 47, 49, 90 to 97), explicit CRLF, no cursor movement or screen control; SAUCE record required with author, group, date | Editors and artists produce it natively; SyncTERM renders it natively; SAUCE is how the scene carries credit |
| License | CC BY 4.0 on the final art and the editable source, with written agreement covering redistribution inside a GPL v2 game | Scene work defaults to BY-NC-SA, which cannot ship in this game |

## Slice 1: the pipeline

Ships before any art is commissioned, with the twelve existing pieces as its
first fixtures.

- **Assets on disk.** `Assets/ANSI/<piece>[.compact|.tall].ans` plus a manifest
  (`Assets/ANSI/manifest.json`): id, variants with dimensions, caption key,
  alt-text key, category, whether it shows in Minimal, author, license.
- **Loader.** Read bytes, never text. Strip and validate the SAUCE record;
  parse the bounded SGR subset above into an immutable grid of (glyph byte,
  foreground 0 to 15, background 0 to 7); reject anything else with file,
  offset, and reason. Resource limits: 64 KiB per file, 80x20 cells. Reject row
  overflow; do not wrap or crop. A test loads every bundled piece and fails the
  build on any rejection.
- **Emission, owned by the emulator.** Runs of equal color become one SGR each,
  reset plus full attributes for the BBS profile. UTF-8 terminals get the glyph
  mapped to Unicode; CP437 terminals get the stored bytes untouched (SyncTERM
  and NetRunner render raw `.ans` better than any re-encoding); telnet escaping
  of 0xFF happens once, in the transport layer; spectators get the logical
  cells rendered for their own capability. Terminal attributes, including
  background, are restored in a `finally`. Output is serialized so a chat line
  cannot land mid-row, and animation flushes by row and can be cancelled. The
  presenter returns rows consumed and whether art was shown, so captions,
  prompts, pagination, and first-visit bookkeeping have a real result.
- **Correctness check for free.** For a CP437 terminal the raw bytes and the
  grid render must agree on every cell; a test asserts it.
- **Detection.** Parse MTTS: bit 4 decides UTF-8 versus CP437 for clients the
  substring list has never heard of; bit 64 defaults Art to Off. Read NAWS and
  refuse to draw a piece wider than the reported width (fall back to the
  compact variant or the caption). Keep the TTYPE substrings as the fallback.
- **Player setting.** Art: Full, Minimal (title, death, permadeath), Off.
  Persisted with the character at the usual three sites; a local or session
  default covers the title and character creation before a character exists.
  Screen-reader mode and MTTS bit 64 override to alt text only.
- **Localization.** `art.<piece>.caption` and `art.<piece>.alt` in all five
  files for every piece. The English inside today's pieces goes, it is not
  translated.
- **Pagination.** Art bypasses the door pager and resets its row count as one
  unit, so a piece plus caption never triggers "-- More --" mid-picture.
- **Web font.** Bundle a monospace with full CP437 block coverage in the web
  terminal so joins render without seams; set the DOS palette in the xterm
  theme and the WezTerm bundle.
- **Legacy pieces.** The twelve are converted to the format as fixtures. The
  ones that are lettering are marked legacy in the manifest and shown only
  until their commissioned replacement lands; they are not localized.
- **Harness.** Render every piece through the real `TerminalEmulator` on the
  UTF-8 path, the CP437 path, the plain-text path, and the screen-reader path;
  screenshot the web terminal for review. A PNG from an ANSI viewer is a
  reference, not a verification.

## Slice 2: the art

- **Pilot first.** Three paid pieces before the full set: the title, death,
  and one location header. They expose composition and rendering problems at
  the cost of three pieces, and the pilot artist's exports validate the loader
  against a real editor's output (some editors compress spaces with cursor
  movement; the export settings are agreed in the pilot).
- **Then the set.** Nineteen pieces: title; death, permadeath, level up, boss
  victory, treasure, new hero, and combat start if kept; headers for the Inn,
  Castle, Temple, Dungeon entrance, Dark Alley, Bank, Weapon shop, Armor shop,
  Healer, Marketplace, Home. One style guide: dark fantasy, clear silhouette
  and one focal point before shading, black negative space, a light stone and
  iron framing convention that does not spend rows.
- **Where.** The 16colo.rs requests forum carries real 2025 to 2026 commission
  requests; Blocktronics and Impure are where to find artists, not staffed
  services; at least one artist lists public per-line pricing. Put the license
  clause in the brief, not in a negotiation afterward.
- **Budget basis.** Public per-line pricing runs about five dollars a line for
  color ANSI; bespoke game work with revisions and an open license runs higher.
  Planning allowances, not quotes: title 250 to 600 dollars, event pieces 100
  to 350, headers 100 to 225, the whole set roughly 2,200 to 4,900 dollars,
  plus 25 to 50 percent per piece for an independently composed compact
  variant. A 2021 forum post offering 300 dollars for a set of door screens is
  not a current rate.
- **Deliverables per piece.** Editable original, the `.ans` export that passes
  the validator, a PNG reference at an IBM 8x16 cell font, one composition
  review and one finishing revision, and the license and attribution in
  writing. Credits in the manifest, the SAUCE record, the `/art` gallery, and
  the README.

## Slice 3: where art appears

Title on launch. Death and permadeath. Level up. Boss victory. New hero once.
Treasure for significant finds only. The location header on the first entry to
that location in a session, keyed by location id and marked seen only after art
was actually drawn; a reconnect starts a new session. Combat start only for
exceptional encounters, or dropped. A `/art` gallery, paginated, with captions
and credits, that works with Art set to Off. Never inside combat rounds or
menus that repeat.

## Decisions for the maintainer

1. Size budget: 78x16 for events and 78x8 for headers as proposed, or a larger
   budget accepted at the cost of the door pager.
2. License: CC BY 4.0 with the GPL clause in the brief, as proposed.
3. Whether MTTS and NAWS parsing is in slice 1 (recommended) or deferred.
4. Whether to run the three-piece pilot before the full commission
   (recommended) and the budget ceiling for the set.
5. Whether the lettering pieces stay as marked legacy fixtures until replaced,
   or are dropped from Full and Minimal until the commissioned art lands.
