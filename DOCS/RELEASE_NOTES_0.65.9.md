# Release Notes - v0.65.9 (Countdown)

Eleventh release of the Beta -> 1.0 "Countdown" cycle: the localization completeness
pass. Every shipped language (en/es/fr/it/hu) is now at FULL key parity with English --
the "5 languages" store-page claim is finally true without asterisks.

## The es/fr/it dialogue gap is closed (899 translations)

Spanish, French, and Italian were ~290-300 keys behind English -- almost entirely the
`dialogue.vn.*` visual-novel NPC templates (greetings, farewells, chat topics, flirt and
compliment lines, personal questions) and the `dialogue.enhance.*` contextual flavor
pools (mood stage-directions, memory callbacks, witness allusions, faction tension,
grief asides) that only existed in English and Hungarian since v0.61.4. All three
languages received the full set (291 es / 302 fr / 302 it, including a handful of
`base.*` and `dungeon.*` stragglers), translated by parallel language agents with
self-validated format-arg parity, ASCII punctuation, accent integrity, and the exact
`*stage direction,*` shape on mood keys.

**The DialogueEnhancer language gate widens accordingly**: `IsSupportedLanguage()` now
admits es/fr/it alongside en/hu, so Spanish, French, and Italian players get the complete
Phase 1.5 contextual-dialogue experience (localized base templates with localized mood /
memory / witness / state / grief / personality / faction flavor layered on top) instead
of the unmodified base line. The gate remains an enumeration so a future
partially-translated language degrades to clean base lines, not mixed-language output.

## Dungeon tutorial localized (108 keys x 5 languages)

The guided 8-page dungeon tutorial (v0.51.2) and the Floor 5 Dungeon Guardian intro
(v0.52.0) were hardcoded English -- the first substantial thing a brand-new non-English
player reads was untranslated (flagged in the 1.0 readiness audit's Tier 2). All ~108
display strings extracted to `dungeon.tut.*` keys and translated into all 4 target
languages with hotkey letters (`[M]`, `[F]`, `[R]`, ...), map symbols, slash commands,
and layout indents preserved exactly. The welcome banner centers the localized title;
source em-dashes normalized to `--` per project punctuation policy.

## Linux launcher: unkillable-process fix (player report)

A Linux player reported that after quitting, relaunching said the game was "already
running," a leftover process survived End Process, and only a reboot cleared it. Root
cause: the bundled WezTerm AppImage runs FUSE-mounted by default, and a wedged FUSE
mount (a known AppImage failure mode, commonly after suspend/resume or a fusermount
crash) leaves the process in uninterruptible D-state -- unkillable by any signal --
while Steam refuses to relaunch until every process in the launch tree exits. Two
fixes in the Linux launchers (ship with the next desktop/Steam build):

- **`play.sh` always self-extracts the AppImage** (`APPIMAGE_EXTRACT_AND_RUN=1`)
  instead of FUSE-mounting -- about a second of extra startup, and the entire
  D-state failure class is gone. Also removes the libfuse2 install requirement.
- **Both `play.sh` and `play-accessible.sh` sweep stale processes at launch**:
  leftover UsurperReborn processes from this install directory get TERM then KILL
  before the game starts, so any ordinary leftover self-heals on the next launch
  instead of blocking it.

Until the next build reaches players, the manual workaround is a hard kill from a
terminal: `pkill -9 -f UsurperReborn` (and `pkill -9 -f wezterm-gui` if present). If
even that fails, the process is in the D-state described above and a reboot (or
`fusermount -u` on the stale AppImage mount) is the only cure -- which is exactly
what this patch eliminates.

## Validation

- Full key parity: 0 missing real keys in es/fr/it/hu vs en.
- 0 em-dashes / en-dashes / Unicode ellipses in any new value, all 5 languages.
- 0 format-arg mismatches across the new namespaces.
- All 5 language files parse as valid JSON; merges were text-appends (no reformat, no
  historical-duplicate collapse).
- Build clean; 875/875 tests pass.

## Files Changed

- `Scripts/Core/GameConfig.cs` -- Version 0.65.9
- `Scripts/Systems/DialogueEnhancer.cs` -- `IsSupportedLanguage()` admits es/fr/it;
  docstring updated
- `Scripts/Locations/DungeonLocation.cs` -- `RunDungeonTutorial` + `ShowFloor5Guardian`
  display strings routed through `Loc.Get("dungeon.tut.*")`; welcome banner centers the
  localized title
- `Localization/en.json` -- 108 new `dungeon.tut.*` keys (extracted, em-dashes -> `--`)
- `Localization/es.json` -- +399 keys (291 dialogue/base/dungeon + 108 tutorial)
- `Localization/fr.json` -- +410 keys (302 + 108)
- `Localization/it.json` -- +410 keys (302 + 108)
- `Localization/hu.json` -- +108 keys (tutorial; hu was already at parity otherwise)
- `launchers/play.sh` -- AppImage always self-extracts (no FUSE mount); stale-process
  sweep at launch
- `launchers/play-accessible.sh` -- stale-process sweep at launch
