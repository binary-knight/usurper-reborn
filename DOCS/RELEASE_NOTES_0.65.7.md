# Release Notes - v0.65.7 (Countdown)

Ninth release of the Beta -> 1.0 "Countdown" cycle. Two features: **new NPC
portraits**, replacing the flat procedural block-faces on the `[T] Talk` screen with real
painted, Darkest-Dungeon-style character portraits -- rendered into the terminal at whatever
fidelity the player's connection supports, all in the same screen footprint as before
(full design/config documentation in `DOCS/PORTRAITS.md`) -- and **visible, changeable
difficulty** for single-player (see "Difficulty" section below).

## What players see

Talking to an NPC now shows a painted head-and-shoulders portrait: a dwarf magician gets a
blue hood, braided white beard, and dramatic side lighting instead of a flat yellow oval
with a purple rectangle beard. Portraits are unique per NPC and derived from who they
actually are: race, sex, class, age band, alignment bearing (sinister eyes past 400
Darkness, noble bearing past 400 Chivalry), and a crown for the monarch.

Rendering adapts to the connection automatically, always inside the classic 34x14 portrait
box:

- **Web terminal / Steam / local**: 24-bit truecolor half-block art. Each character cell
  carries two pixels (the upper-half-block trick), doubling vertical resolution.
- **SSH / raw-TCP MUD clients**: xterm-256 half-blocks with a shadow gamma lift (the
  256-color cube has no colors between 0 and 95 per channel; without the lift, dark tones
  crush to black).
- **Real BBS terminals (CP437)**: authentic 16-color ANSI-scene art. CP437 has the half
  blocks (0xDF/0xDC); per cell the encoder picks the best of upper-half, lower-half, or
  shade-block mix, with backgrounds held to the low 8 colors so nothing blinks.
- **Screen readers / plain text / Electron**: unchanged (no art / classic path).

## How it works (and what it costs)

Portraits are generated ONCE per NPC, in the background, by the PixelLab image API the first time a player talks to
an NPC without a cached portrait, the classic procedural portrait renders as always and a
background task paints the real one for every later visit. Generated PNGs are cached to
disk forever, keyed on name, race, sex, and class (aging and leveling do not regenerate;
a class change or rename does).

Safety and cost architecture mirrors the LLM moments system: env-var configuration, OFF by
default, per-server daily attempt cap (default 60), one in-flight generation per NPC, a
6-hour per-NPC cooldown after failures, downloads validated before caching so a corrupt
file can never poison a session, and the procedural fallback ALWAYS works -- no key, no
network, no problem. A full 200-NPC world costs about two dollars of API credit, spread
over days by the cap. Sysops can also pre-generate offline, copy the cache folder between
machines, or drop in hand-made PNGs (the cache is plain files).

## Engineering notes

- **`Scripts/UI/PortraitEncoder.cs`** -- pure, deterministic 3-tier ANSI encoder ported
  from a Python prototype that was visually validated tier-by-tier first (prototype and
  preview sheets kept in `tools/portrait_halfblock_poc.py` / `tools/portrait_poc_out/`).
  Perceptually weighted palette matching; box-filter downsample composites alpha over the
  house dark backdrop; no dithering (at 34x28 scale error diffusion reads as glitter --
  a finding from the prototype iterations).
- **`Scripts/UI/MiniPng.cs`** -- dependency-free PNG decoder (ZLibStream inflate, filters
  0-4, color types 0/2/3/4/6, 8-bit, no interlace). Deliberately NOT a general decoder:
  adding an imaging NuGet package would bloat every self-contained BBS build for one
  34x28 downsample. Out-of-profile files throw; callers fall back to procedural art.
- **`Scripts/Systems/NPCPortraitSystem.cs`** -- settings, disk cache, tier resolution,
  framed rendering (borders through the CP437-safe markup writer, image rows through
  `WriteRawAnsi`, the split the race-portrait screen has used since v0.43.3), budget,
  fire-and-forget generation with pre-async trait snapshotting, prompt builder.
- The C# pipeline's output was verified by re-rendering its actual `.ans` output back to
  images (`tools/ans_to_png_check.py`) and comparing against the Python reference.
- No save-format changes, no new loc keys (the frame reuses the existing name-bar layout),
  no combat paths touched. Single new call site in `BaseLocation.InteractWithNPC`.

## Difficulty: visible everywhere, changeable in single-player

Player report: a new character created through Quick Start never saw the difficulty
screen and had no way to find out (or change) what they were playing on. Root cause was
a Quick Start design gap -- the flow's promise is "every skipped choice has a Preferences
mirror," but difficulty was the one exception (it was silently defaulted to Normal with
the only readout buried at the bottom of the `=` Statistics screen, and the only changer
being the out-of-game save editor).

- **New: change difficulty in `[~]` Preferences** (option 5, Gameplay section,
  single-player only). Same E/N/H/! letters as character creation, localized one-line
  descriptions of each mode's modifiers, and switching INTO Nightmare replays the
  creation flow's permadeath warning + confirmation plus a note that Nightmare disables
  the Last Stand and Death's Door survival guarantees. Applies immediately (live
  `DifficultySystem.CurrentDifficulty` + save field) and saves on the spot. Hidden and
  input-guarded in online mode, where difficulty is forced Normal at login and server
  admins tune global multipliers instead.
- **Difficulty is now displayed** on the `[%]` Character Status sheet (next to Fatigue)
  and in `/health`, both single-player only (online is always Normal, so a readout there
  would be noise). The existing `=` Statistics line stays.
- **Mode names localized for the first time**: new `DifficultySystem.GetLocalizedName`
  reads `difficulty.mode_*` keys (all 5 languages) with English fallback; all display
  sites including the pre-existing Statistics screen now route through it.

Accepted behavior, noted for the record: switching to Nightmare mid-run makes the
Nightmare-tier achievements reachable without playing the whole run on Nightmare. The
standalone save editor has always allowed the same edit, and single-player saves belong
to the player; not worth an anti-cheese counter.

## Level Master menu: full-label conversion (community PR follow-up)

Community PR #113 fixed a doubled letter on the Level Master's `[P]Path` menu row: the
services menu uses the classic BBS split-word convention (the code prints the bracketed
hotkey, the label omits its first letter -- `[L]` + `"evel Raise"`), and the two rows
added in v0.65.4 (Path, Specialize) shipped with full words, rendering `[P]Path` and
`[S]Specialize`. The PR corrected the English Path label within the old convention;
this follow-up retires the convention itself, the same way Main Street was converted in
v0.52.4:

- The 8 services-menu rows now print `[X] ` with a space, and all 8 labels are full words
  in all 5 languages. This also fixes the previously hidden `[S]Specialize` double
  (visible only at level 25+) and the same doubling in every translation
  (`[P]Camino`, `[S]Especializar`, ...).
- The conversion repaired real translation damage the split-word convention had caused:
  Spanish rendered `[L]ubir Nivel` (the S of "Subir" chopped by an L hotkey), Italian and
  Spanish had similar first-letter casualties on every row, and French had simply kept
  untranslated English ("Level Raise", "Abilities", "Crystal Ball", ...) -- now properly
  translated. Hungarian was already full-word (rendering mismatched-letter jank) and now
  displays correctly. En-dash separators in these values were normalized to ASCII.
- The Electron client's Level Master menu reads the same keys and previously displayed the
  chopped labels ("evel Raise") in its graphical UI; it gets full words for free.

## Same-release fix: portraits rendered as color noise in the desktop online client

First live test showed the portrait frame rendering correctly but the image as random
saturated color noise in WezTerm via [O] Online Play. Systematic elimination (independent
pyte VT-parser validation of the encoded rows, an in-process stream-writer byte dump, and
transport review) proved the encoder, decoder, and server write path correct -- the fault
was in the CLIENT: `OnlinePlaySystem.WriteAnsiToConsole`'s SGR handler split parameters on
';' and applied each number independently. A truecolor sequence like `38;2;29;33;43` was
read as codes 38 (ignored), 2 (ignored), 29 (ignored), 33 (yellow fg), 43 (yellow bg) --
dark pixels whose RGB components land in the 30-47 range painted random 16-colors, which
is exactly the observed noise. Worse, the client's `vtProcessingEnabled` flag had existed
since the online-play client shipped but was never set anywhere, so ALL server output went
through this legacy parser unconditionally.

Three-layer fix:
1. **VT passthrough enabled for real**: `TryEnableVtPassthrough` sets
   ENABLE_VIRTUAL_TERMINAL_PROCESSING on the stdout handle (Win10+; non-Windows is
   VT-native), so modern terminals parse the server's ANSI natively. Benefits all server
   output, not just portraits.
2. **Legacy parser fixed**: `ProcessSgr` now consumes 38;2;r;g;b and 38;5;n sequences as
   units and approximates to the nearest ConsoleColor -- the pre-Win10 fallback renders a
   16-color approximation instead of noise.
3. **Server-side tier demotion for old clients**: connections identifying as Steam / Local /
   BBS get the 16-color portrait tier, because every pre-0.65.7 desktop client in the field
   still has the broken parser and 16-color SGR is the dialect it parses correctly (TODO in
   `NPCPortraitSystem.ResolveTier`: promote to TrueColor once old clients age out). Web
   keeps truecolor; SSH / MUD keep xterm-256.

## Files Changed
- `Scripts/Core/GameConfig.cs` -- Version 0.65.7
- `Scripts/UI/PortraitEncoder.cs` -- NEW: 3-tier terminal art encoder
- `Scripts/UI/MiniPng.cs` -- NEW: minimal PNG decoder
- `Scripts/Systems/NPCPortraitSystem.cs` -- NEW: cache + generation + display orchestration
- `Scripts/Locations/BaseLocation.cs` -- Talk-screen portrait site tries the AI portrait
  first, falls back to `PortraitGenerator`
- `Scripts/Systems/OnlinePlaySystem.cs` -- VT passthrough actually enabled in PipeIO
  (`TryEnableVtPassthrough`); legacy SGR parser consumes extended 38;2/38;5 color
  sequences as units with nearest-ConsoleColor approximation
- `Scripts/Systems/DifficultySystem.cs` -- `GetLocalizedName` (difficulty.mode_* keys,
  English fallback)
- `Scripts/Locations/BaseLocation.cs` (difficulty) -- `[~]` Preferences option 5 +
  `ChangeDifficultyPreference` (visual + SR, Nightmare confirmation); difficulty line on
  the `[%]` status sheet and `/health` (single-player only)
- `Scripts/Locations/MainStreetLocation.cs` -- `=` Statistics difficulty line uses the
  localized name
- `Localization/{en,es,fr,it,hu}.json` -- 16 additional difficulty keys each (4 mode
  names, 4 mode descriptions, 7 prefs strings, 1 status label); 8 `level_master.menu_*`
  values per language converted to full-word labels (translations repaired, dashes
  normalized)
- `Scripts/Locations/LevelMasterLocation.cs` -- services menu rows print `[X] ` with a
  space (full-label convention)
- `Scripts/Core/GameEngine.cs` / `Localization/*.json` / `web/index.html` /
  `web/lang/*.json` -- TestingBytes added to the contributor credits (in-game credits
  screen + website credits cards, all 5 languages)
- `Tests/PortraitTests.cs` -- NEW: 16 tests (decoder pixel-exactness and rejection, tier
  shape/determinism/BBS-safety, cache-key stability, prompt traits, opt-in gating);
  870/870 pass
- `tools/portrait_halfblock_poc.py`, `tools/portrait_gen_poc.py`,
  `tools/portrait_proc_face_poc.py`, `tools/ans_to_png_check.py` -- prototype + QA tooling
- `DOCS/PORTRAITS.md` -- NEW: configuration and operations guide

---

## Pre-Ship Audit Pass (comprehensive review)

Three parallel review agents (NPC lifecycle, difficulty/menu surfaces, portrait stack +
client render fix) plus a manual sweep audited everything in 0.65.7 before ship. All
confirmed findings were fixed in this release; the PR #113 Level Master merge is pulled
into the release branch (merge commit keeps the full-word menu conversion as the final
state).

### Fixed: NPC / portrait lifecycle
- **King-crown cache key**: an NPC who gains or loses the crown now gets a distinct
  portrait cache entry (the crowned prompt renders regalia), instead of reusing the
  commoner image forever. (`NPCPortraitSystem.GetCacheKey` conditional `|king` suffix)
- **Dead NPCs could be talked to via the static location list**: the Talk menu now
  filters `IsDead` in addition to `IsAlive`. (`BaseLocation.TalkToNPC`)

### Fixed: portrait stack hardening
- **Electron online sessions no longer receive truecolor portraits.** The Electron
  client's ANSI parser has no SGR 48 (background) support, so an Electron user playing
  online through the web relay would have seen the same color-noise corruption this
  release fixed elsewhere. Portrait display now bails to the procedural portrait for
  `ConnectionType == "Electron"`. (HIGH)
- **Corrupt cached PNG self-heals**: a cache file that fails to decode (out-of-profile
  sysop-provided PNG, torn write) is quarantined as `.bad` so the next visit
  regenerates, instead of being a permanent silent dead-end.
- **Cache-dir writability proven before the paid API call**: an unwritable
  `USURPER_PORTRAIT_DIR` no longer burns real PixelLab generations that are then
  discarded on the disk write.
- **Budget/dedup ordering**: two sessions talking to the same NPC in the same instant
  now consume one daily-budget slot, not two.
- **In-place PNG replacement shows fresh art without a restart** (encoded-row cache key
  includes file mtime).
- **Unique tmp filenames + cleanup** for the atomic cache write (two processes sharing
  one cache dir can no longer collide on a fixed `.tmp` name).
- **Timeout floor**: a zero/negative `USURPER_PORTRAIT_TIMEOUT_MS` is clamped to 1s
  instead of instantly cancelling or throwing.
- **MiniPng**: long arithmetic in the chunk truncation check (crafted length can no
  longer wrap past the bounds check) and IHDR minimum-length validation.
- **Online Play client**: the auth-leftover first screen now honors VT passthrough
  (no approximated colors / stranded partial escape at session start), and the
  disconnect path resets SGR attributes so a drop mid-portrait can't leave the local
  disconnect banner on a color smear.

### Fixed: difficulty / menus
- **Nightmare achievement anchor** (new `NightmareStartLevel` save field, full
  round-trip + round-trip test): with the new preferences difficulty changer, a
  high-level character switching into Nightmare could instantly cheese the "reach level
  10 / 50 on Nightmare" achievements. The achievements now require gaining those levels
  *while on Nightmare* (anchor set at creation or at the moment of switching in;
  pre-0.65.7 Nightmare saves are grandfathered as creation-borne).
- The prefs changer no longer prints the creation-flow "your fate is sealed" line after
  a reversible switch.
- The `=` Statistics difficulty line is hidden online (online is always Normal).
- Character creation's difficulty descriptions are now localized
  (`DifficultySystem.GetLocalizedDescription`, shared with the prefs changer).
- Hungarian `level_master.path_desc` shortened so the `[P]` row fits 79 columns.
- Level Master screen-reader, BBS-compact, and Electron menus now list `[P] Path` and
  the level-gated `[S] Specialize` rows (previously reachable but invisible on those
  surfaces); Electron labels are localized; the specialization gate is a single shared
  helper across all four menu surfaces.
- Em-dash sweep of all new strings and comments (project style rule).

### Deferred (documented, not blocking)
- Portrait cache orphan cleanup (unbounded dir growth, ~50 MB/year at current rates;
  any cleanup must be online-only because single-player shares the dir).
- OSC achievement marker split across a 4096-byte read boundary is lost (pre-existing,
  needs stateful reassembly in the relay client).
- Portrait name-bar race/class text is unlocalized (byte-for-byte parity with the
  classic procedural portrait; both surfaces need one shared loc pass).
- `USURPER_PORTRAIT_TIMEOUT_MS=90000` recommended at next server deploy (PixelLab
  latency has drifted to ~43s against the 45s default).


### Files Changed (audit pass)
- `Scripts/Systems/NPCPortraitSystem.cs` -- Electron-online bail, PNG quarantine, cache-dir probe, budget/dedup ordering, mtime cache key, unique tmp, timeout floor, king cache-key suffix
- `Scripts/UI/MiniPng.cs` -- long-arithmetic chunk bounds check, IHDR length validation
- `Scripts/Systems/OnlinePlaySystem.cs` -- auth-leftover VT branch, disconnect SGR reset
- `Scripts/Core/Character.cs` -- `NightmareStartLevel` field
- `Scripts/Systems/SaveDataStructures.cs` / `Scripts/Systems/SaveSystem.cs` / `Scripts/Core/GameEngine.cs` -- `NightmareStartLevel` save round-trip + legacy-save heal
- `Scripts/Systems/AchievementSystem.cs` -- Nightmare achievements measure levels gained on Nightmare
- `Scripts/Systems/CharacterCreationSystem.cs` -- anchor set at creation; localized difficulty descriptions
- `Scripts/Systems/DifficultySystem.cs` -- `GetLocalizedDescription`
- `Scripts/Locations/BaseLocation.cs` -- prefs changer anchor set/clear, sealed-line removed, dead-NPC talk filter, localized difficulty descriptions, comment style sweep
- `Scripts/Locations/MainStreetLocation.cs` -- statistics difficulty line hidden online
- `Scripts/Locations/LevelMasterLocation.cs` -- `HasSpecializationOption()` shared gate; P/S rows on SR, BBS, and Electron menus; Electron labels localized
- `Localization/{en,es,fr,it,hu}.json` -- `level_master.path` + `level_master.specialize` short labels; Hungarian `path_desc` shortened
- `Tests/SaveRoundTripTests.cs` -- `PlayerData_RoundTrip_PreservesNightmareStartLevel`

**Audit result: build clean, 871/871 tests green (870 prior + 1 new round-trip test),
localization integrity verified across all 5 languages.**
