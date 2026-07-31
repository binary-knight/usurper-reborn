# Release Notes - v0.65.12 (Countdown)

Fourteenth release of the Beta -> 1.0 "Countdown" cycle: the pre-1.0 localization slice
from the comprehensive audit (`DOCS/LOCALIZATION_AUDIT.md`).

## The English gate is open: the login screen speaks five languages

The online login/register flow (the first thing every raw-TCP, telnet, and web-terminal
player sees) was 100% hardcoded English -- a fully-translated game sat behind an English
door. The whole flow (menu, prompts, validation errors, throttle message, welcome) now
renders via localization, and a new **[G] Language** option on the auth menu cycles
through the installed languages BEFORE login. On registration, the chosen language is
persisted to the account so the first session and first character start localized;
existing accounts keep their saved preference exactly as before. The ANSI menu box
self-pads around translated label lengths.

## The permadeath cinematic speaks five languages -- both copies

The most emotionally loaded screen in the game ("The threads that bind your soul to the
world fray...") was hardcoded English in BOTH its implementations: PermadeathHelper
(online) and the independent CombatEngine single-player death-cap copy. All ~30 lines
now localize (solemn, mythic register preserved per language), the "Resurrections
remaining" warning that fires on every online death is localized, and the server-wide
eulogy broadcast now renders **per-recipient in each player's own language** via
BroadcastLocalized. (The CombatEngine copy's broadcast also gains the ESC-byte fix it
had missed since v0.60.2 -- its color codes were printing as literal `[1;31m` text.)

## Hungarian is readable on real BBS terminals

`ő ű Ő Ű` do not exist in CP437 and were degrading to `?` on every BBS output path --
7,500+ occurrences across 24% of all Hungarian strings. All three encoders
(TerminalEmulator.PreTranslateCp437, SocketTerminal's CP437 table, MudServer's
WriteAnsiAsync) now transliterate to the umlaut forms (`ő`->`ö`, `ű`->`ü`), which
Hungarians read without difficulty.

## Defect fixes from the audit's scripted layer

- `training.single_reset_lore_7` (hu) referenced a format arg English never passes --
  a live FormatException / raw-template bug. Fixed.
- `base.dungeon_get_stronger` + `base.dungeon_watch_floor` (es) had dropped the level/
  floor number and one had broken quote-escaping that rendered literal backslashes.
  Both retranslated with args restored.
- 11 web landing-page keys (the Mystic Shaman class card + Melodia companion card,
  added EN-only in v0.65.5) translated into all four languages.

## The localization CI gate

New `Tests/LocalizationIntegrityTests.cs` -- four assertions that run with every build:
1. Every English key exists in every language (parity).
2. No translation references a format arg English never passes (the crash direction).
3. No empty translations outside the documented intentional list (pro-drop pronouns,
   no-plural-after-numeral suffixes).
4. Banned-punctuation occurrence counts per file may never grow (current debt recorded
   as ceilings; new keys must use ASCII `--` / `...`).
Every historical loc-regression class is now a build failure instead of a player report.
The gate caught its first real discrepancy during its own commissioning run.

## Localization

31 new in-game keys (auth.* + death.* + permadeath.*) and 11 web keys translated into
es/fr/it/hu by four parallel agents, all self-validated (arg parity incl. the 4-arg
eulogy reordered per language grammar, suffix-safe Hungarian arg positions, zero banned
punctuation, accent integrity).

## Files Changed

- `Scripts/Core/GameConfig.cs` -- Version 0.65.12
- `Scripts/Server/MudServer.cs` -- InteractiveAuthAsync fully localized + [G] language
  cycle + registration language persistence; CP437 transliteration in WriteAnsiAsync
- `Scripts/Systems/SqlSaveBackend.cs` -- `SetAccountLanguage`
- `Scripts/Systems/PermadeathHelper.cs` -- cinematic + warnings localized; eulogy via
  BroadcastLocalized
- `Scripts/Systems/CombatEngine.cs` -- duplicate cinematic localized; eulogy via
  BroadcastLocalized (+ missing ESC-byte fix)
- `Scripts/UI/TerminalEmulator.cs` / `Scripts/BBS/SocketTerminal.cs` -- ő/ű/Ő/Ű CP437
  transliteration
- `Localization/en.json` -- 38 new auth/death/permadeath keys
- `Localization/{es,fr,it,hu}.json` -- +31 keys each; hu crash-key fix; es dungeon-key
  fixes
- `web/lang/{es,fr,it,hu}.json` -- +11 keys each (Shaman + Melodia cards)
- `Tests/LocalizationIntegrityTests.cs` -- **NEW** -- the 4-test loc CI gate
- `DOCS/LOCALIZATION_AUDIT.md` -- **NEW** (written during the audit) -- full inventory

## Deploy notes

Game binary (standard deploy) + the four `web/lang/*.json` files (static copy, no
restart). Remaining audit tiers (chat/social layer, Active Buffs panel, Castle
cinematics, news-feed decision) tracked in DOCS/LOCALIZATION_AUDIT.md for 1.0.x/1.1.
