# Localization Deep Audit (v0.65.11, 2026-07-28)

Comprehensive audit of every localization aspect: scripted integrity checks over all five
language surfaces (in-game, web, Electron), three parallel code sweeps (Locations,
Systems/Server, infrastructure/architecture), and an assessment of what is missing.

**Headline verdict: the infrastructure is genuinely excellent; the content coverage has a
quantified tail.** 23,659 keys at 100% key parity across en/es/fr/it/hu, per-session
language isolation, drop-in support for new languages, display-time quest rendering, and
zero drift in the Electron shim. Against that: ~580 hardcoded player-facing English
strings remain in code (inventoried below with the worst offenders being the ONLINE LOGIN
FLOW and the PERMADEATH CINEMATIC), one rendering defect makes 24% of Hungarian strings
partially unreadable on BBS terminals, and the news feed remains the one accepted
architectural limitation.

---

## What is fully covered (verified, no action)

- **Key parity**: all 23,659 real keys present in all 4 target languages. 0 missing, 0 extra.
- **Language plumbing**: `GameConfig.Language` is per-session (SessionContext) — safe for
  concurrent MUD players. Selection available pre-login (main menu + BBS door menu),
  in-game preferences, and the save editor; persisted per character.
- **Drop-in new languages**: `Loc.AvailableLanguages` scans `Localization/*.json`; a new
  `de.json` would appear automatically ("de" → "Deutsch" already in KnownLanguageNames).
- **Quest embedded args**: SOLVED (better than the historical limitation) — `GetDisplayTitle`
  renders TitleKey+args at display time in the viewer's language, with a `loc:` arg prefix
  for nested keys (Quest.cs:81-113).
- **Electron client**: 81/81 keys, exact set match in _template/es/fr/hu/it. Zero drift.
- **Name layers** (monster/item/champion/NPC names): deliberately English game-wide; grammar
  leakage is mitigated (indefinite-article logic is language-gated at DungeonLocation.cs:7042).
- **GMCP**: machine-readable, correctly English; no player-display prose is GMCP-only.
- **Dates**: explicit player preference (MM/DD, DD/MM, ISO) applied via GameConfig.FormatDate.
- **Dialogue enhancer + VN templates**: all five languages as of v0.65.9.

## Scripted integrity findings (fix list)

- **1 crash-risk arg mismatch**: `training.single_reset_lore_7` (hu) references a format arg
  EN does not pass -> FormatException -> raw template at runtime. FIX.
- **47 "info-loss" arg mismatches** are overwhelmingly the INTENTIONAL v0.61.5 pronoun-drop
  design (intimacy.* in pro-drop languages). Verify only: `base.dungeon_get_stronger` +
  `base.dungeon_watch_floor` (es).
- **Empty values**: `ui.pronoun_*` empties are intentional (pro-drop). Verify the plural-suffix
  empties (`main_street.player_plural`, `love_corner.child_count_ren`, `home.children_ren`,
  `ending.legacy_children_plural` — likely intentional no-plural-after-numeral) and
  `engine.story_begins_4` (hu — possible genuine miss).
- **Banned punctuation debt**: em-dashes in values — en 215 (!), es 280, fr 382, it 339,
  hu 277; plus en-dashes (hu 117, fr 22) and ellipsis chars (fr 48). NOTE: these DO render
  on BBS (PreTranslateCp437 transliterates dashes), so this is style-rule debt, not a
  rendering bug — clean up with a scripted normalization pass (em -> `--`, en -> `-`,
  `…` -> `...`) when convenient.
- **Web lang drift**: 11 keys missing in all four web files — `classes.shaman_*` and
  `companions.melodia_*` (added EN-only in the v0.65.5 store-page accuracy fix). The Shaman
  class card and Melodia companion card render English on non-EN landing pages. FIX (small).

## Rendering defect: CP437 + Hungarian (top value-per-effort fix)

`ő`/`ű`/`Ő`/`Ű` (7,576 occurrences across 24% of Hungarian strings) do not exist in CP437
and degrade to `?` on every BBS output path. No transliteration branch exists for them:

- `TerminalEmulator.PreTranslateCp437` (UI/TerminalEmulator.cs:191-218) — has dash/quote/
  box-drawing mapping but no Latin-letter branch.
- `SocketTerminal.ConvertToCp437` table (BBS/SocketTerminal.cs:132-271) — covers á í ó ú ö ü
  é etc. but not the double-acutes; line 888 emits `?`.
- `DoorMode` sets `Encoding.GetEncoding(437)` with default `?` fallback (DoorMode.cs:815, 856).

Fix: map `ő->ö, ű->ü, Ő->Ö, Ű->Ü` in all three places (Hungarians read the umlaut forms
without difficulty; `?` is unreadable). Four dictionary entries per site.

## Hardcoded English in code: ~580 player-facing strings (three sweeps)

### Tier A — the gate and the peaks (do first)
1. **The entire online login/register flow** (MudServer.cs:793-984, ~22 strings): menu,
   Username/Password prompts, validation errors, "Welcome". A translated game is currently
   unreachable behind an English gate. Also disconnect/idle/kick/shutdown broadcasts
   (MudServer.cs:1551-1613, PlayerSession.cs:455-457).
2. **The permadeath cinematic — BOTH copies** (PermadeathHelper.cs:99-282, ~19 strings; the
   independent duplicate at CombatEngine.cs:20437-20526). The most emotionally loaded screen
   in the game, 100% English, including the server-wide eulogy broadcast (use
   BroadcastLocalized, which already exists) and the every-death "Resurrections remaining"
   warning.
3. **/who + say/shout/tell/gossip frames + /group** (MudChatSystem.cs, ~90 strings): the
   entire social layer of the server.
4. **Active Buffs/Effects panel** on the stats screen (BaseLocation.cs:6691-6923, ~35
   strings): every player, every stats view — the single biggest win in Locations.

### Tier B — high-traffic surfaces
5. Castle cinematics: Rebellion/execution/Walk of Shame (~100), knighting ceremony (~25),
   prison/throne flows (CastleLocation.cs — 126 total).
6. Magic Shop: destructive enchant-failure WARNING (2036-2063), browse labels, love-potion
   and dark-magic flows (~41).
7. Boss mechanics lines in the LIVE combat path (CombatEngine.cs ~40 confirmed: AoE/channel
   damage lines, enrage warnings, DOOM/corruption, victory summary, death epitaph).
8. Inn companion roster/status (~12); Dungeon "ACQUIRED:", map header, legend (~9);
   Settlement sharpening + scout report (~8); WorldBossSystem leftovers (~12);
   GameEngine death penalty + save-recovery screen (~23); 10 bare GetInput prompts;
   BugReportSystem headers (2).

### Structural sub-issues discovered in the sweep
- **Split-hotkey menus are untranslatable by construction**: `Write("[A]"); Write("ttack")`
  in WorldBossSystem.cs:699-730 (whose SCREEN-READER branch is already localized — a
  divergence) and CombatEngine.cs:2273/2373. Requires a small render refactor, not just keys.
- **Inlined English pluralization/ordinals**: `{(n != 1 ? "s" : "")}` (MudServer.cs:1551,
  CombatEngine.cs:20083) and `"th time"` (CombatEngine.cs:20437) must move into keys.

### Tier C — the news feed (architectural decision required)
~369 call sites (335 Newsy + 34 AddNews across 56 files; only 16 localized) render English
at CREATION time into shared world_state, shown identically to all languages. Heaviest:
WorldSimulator (50), CastleLocation (45), NewsSystem (27), NPCPetitionSystem (22),
ChallengeSystem (21). A key+args refactor = all ~369 sites + storage schema + render-at-read
+ web/Discord consumers. The web dashboard and Discord bridge are genuinely shared
multi-language channels where English is defensible; the in-game news reader is what hurts.
**Options:** (a) accept and scope the claim ("world news appears in English") for 1.0;
(b) phased refactor post-1.0 starting with the ~30 highest-frequency templates.
This remains the standing DECISION PENDING from the June plan.

## Number formatting (post-1.0, careful)

769 `:N0`/`ToString("N0")` sites format under host-OS culture, decoupled from
GameConfig.Language (a French player sees `1,000` instead of `1 000`). CurrentCulture is
never assigned anywhere. Recommended approach: a `GameConfig.FormatNumber(long)` helper
keyed on session language, adopted opportunistically — NOT a global
DefaultThreadCurrentCulture assignment (would risk perturbing parse paths). Player harm is
mild (a separator); wide blast radius; schedule deliberately.

## steam.html

English-only, zero data-i18n wiring (2,272 lines). Recommendation: accept for 1.0 (it is
the Steam-safe page; Steam browsing context is store-locale anyway); wire it to the web
i18n system post-1.0 if Steam wishlist traffic warrants.

## Do we need to ADD anything?

1. **A localization CI gate** (the highest-value addition): a unit test asserting, for every
   language file — key parity vs en, format-arg superset-compatibility, no banned
   punctuation in new keys, no empty values outside the intentional list. Every historical
   loc regression class (T1-3 raw templates, missing keys, arg drift) becomes a build
   failure instead of a player report.
2. **CP437 transliteration for Hungarian** (above) — an addition to the encoder, tiny.
3. **New languages**: infrastructure is drop-in ready. German is the strongest candidate
   (BBS-scene affinity; "de" already registered), then pt-BR and pl. ~24k keys ≈ 35 agent
   batches per language using the established workflow — a real but proven project.
   Arabic/RTL remains its own major infra plan (see memory/project_arabic_rtl_plan.md).
4. **A "loc-string lint" habit**: the sweep found the same idioms recur (split-hotkey menus,
   inline pluralization, sentence-tail fragments like `WriteLine(" gold.")`). A short
   contributor note in MODDING.md/CLAUDE.md naming these anti-patterns prevents re-growth.

## Recommended sequencing

- **Pre-1.0 (small, high leverage):** the 1 crash-risk key; the 11 web keys; CP437
  Hungarian transliteration; the login/register flow (Tier A-1); the permadeath cinematic
  both copies + eulogy via BroadcastLocalized (Tier A-2); the loc CI gate.
- **1.0.x fast-follow:** /who + chat frames + /group; Active Buffs panel; Magic Shop
  warning; punctuation normalization pass.
- **1.1:** Castle cinematics, remaining Tier B, split-hotkey refactor, number formatting
  helper, news-feed decision executed.
- **Post-1.0 (business call):** German.
