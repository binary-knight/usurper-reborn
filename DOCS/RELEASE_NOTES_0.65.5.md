# Release Notes - v0.65.5 (Countdown)

Seventh release of the Beta -> 1.0 "Countdown" cycle. This one is the first pass of correctness
fixes coming out of the full pre-1.0 readiness audit (see DOCS/RELEASE_1_0_AUDIT.md). It clears all
three Tier-0 blockers that audit found plus six of the seven Tier-1 items (the seventh, the in-game
Beta banner flip, is deliberately a release-day action), a first slice of Tier-2 (save caps + a
documentation accuracy pass), and a Linux terminal fix. No new content; this is a bug-fix and
correctness release ahead of the 1.0 tag.

## Tier 0: the blockers

### Old God AoE damage was ignoring boss immunity and divine armor
Every single-target hit against an Old God correctly ran through the boss's phase-immunity (physical
or magical, depending on the phase) and divine-armor reduction. Area-of-effect damage did not: AoE
spells and abilities (Fireball, Chain Lightning, the Tidesworn's Maelstrom of the Faithful) went
straight to the health bar, so a caster could nuke a boss during an immune phase and shrug off the
divine armor the fight is built around. AoE now applies the same immunity and divine-armor reduction
the single-target path does, per target, in the same order. Maelstrom of the Faithful is treated as
magical for this purpose (it is a faith spell).

### Save contamination could wipe seals, worship, companions, and family
When a save ran on the online (MUD) server with no active player session attached, the story, worship,
companion, and family systems all fall back to shared process-wide state, so the save could persist
empty or another player's data over the real character's collected seals (and their worshipped god,
companion roster, and family). Prior builds only logged this condition; the save still went through. It is now
refused outright when it happens: the on-disk save is left untouched and the anomaly is logged. Normal
play always has a session attached, including the save on logout, so this only ever fires on the
anomalous path it is meant to catch. Single-player is unaffected.

### Background NPC deaths caused phantom widowhood and stranded lovers
When a rare "permanent" death rolled in the background world simulation (a brawl, a gang or team war, a
high-tier dungeon run), the game fired the full consequence cascade (end the marriage, mail the player
spouse a widowing notice, orphan the children, seed a family grudge) but a separate respawn path
brought the NPC back to life minutes later, because the permanent-death flag had been left disabled.
So a player could be told their spouse had died, then run into that spouse alive. Separately, when a
lover or friend-with-benefits died permanently in the background, they were never released from the
player's relationship state and their affair records lingered forever (only the direct-player-kill path
cleaned these up). Both are fixed: a rolled permanent death is now genuinely permanent (the NPC does
not respawn), and the romance cleanup that the direct-combat path already did now runs on every
permanent death, including old age, releasing lovers and clearing affairs, not just marriages. The rare
rolled permadeath stays rare and floor-gated (it will not push any race toward extinction); the common,
non-rolled death still respawns exactly as before.

## Tier 1

- **Marriage and divine-blessing XP bonuses now actually apply.** The 10% married-player XP bonus and
  the divine-blessing XP bonus lived only in an old combat path that is no longer reached, so married
  and blessed players had silently been getting neither. Both now apply on every kill and show on the
  reward screen, and the partial-victory (retreat-after-some-kills) path was given the same bonuses.
  Known limitation: grouped co-op FOLLOWERS still do not receive either bonus on group kills (their
  marriage and blessing state lives in their own session, not the leader's, and is not reachable from
  the leader's reward calculation); followers get both bonuses normally on their own solo kills.
- **Non-English monster-encounter text was rendering as raw template.** In Spanish, French, Italian,
  and Hungarian, the "a monster appears" and "boss blocks your path" lines had stale templates that
  referenced a variable the game no longer passes, so those languages showed literal placeholder text
  on every encounter. Fixed. Three other player-facing lines (the already-married refusal and the two
  daily-murder-cap notices) that were missing in all four languages are now translated, and a Hungarian
  training-reset line that showed a raw placeholder is fixed.
- **Royal orphans could vanish at population cap.** When an orphan came of age while the world was at
  its population cap, the game removed them from the orphanage and the family registry before checking
  whether there was room to create their adult NPC, so at cap they were deleted with no NPC ever made.
  They now wait in the orphanage and graduate on a later tick when a slot opens, matching how regular
  children already behave.
- **Church "this month" figures no longer reroll every visit.** The community donation/marriage/
  blessing/soul counts on the Church records screen were random each time you opened it. They are now
  steady within an in-game month and roll over monthly. (Your own church history was always real.)
- **Silent save failures are now logged.** Several save and autosave calls (bank wire rollback, ending
  completion, pantheon favors, and the save-recovery file scan) swallowed their errors with no trace.
  They now log, so a genuine save failure is visible instead of silently losing progress.
- **NPC teammate bags and shop stock are now capped in saves.** The per-NPC personal inventory and
  market-stock lists were the one per-NPC collection with no serialization cap, so a long career of
  handing items to the same NPC teammate could slowly grow the save (multiplied across the ~200-NPC
  world). Both are now capped like every other per-NPC list, and the online NPC serializer also caps
  the grudge (Enemies) list for parity with the single-player path.

## Linux "all-white window" fix (launcher + game)

A Linux player reported the game as an all-white xterm window. Root cause chain: the bundled WezTerm
failed to start on his system (typically missing libfuse2 or GUI libraries), the launcher fell back to
xterm with no color settings, and xterm defaults to black-on-white, which washes out the game's
dark-background palette. Fixed at three layers:

- **The launcher no longer strands or blinds you when WezTerm fails.** `play.sh` now runs WezTerm
  supervised instead of replacing itself with it, so a crash at GUI startup falls through to a system
  terminal instead of leaving nothing (previously a WezTerm that probed OK but died on launch ended the
  session). When WezTerm cannot start at all, the launcher prints why and what to install (libfuse2
  hint), retries via AppImage self-extraction as before, and skips straight to the current terminal when
  there is no graphical display. Fallback terminal order now prefers desktop terminals (which follow the
  user's theme) with xterm last, and xterm is forced to white-on-black.
- **The game now paints its own dark background on Unix terminals.** On an interactive Linux/macOS
  terminal the game sets the terminal's default background/foreground dark at startup (OSC 11/10, safely
  ignored by terminals that do not support it) and restores the player's colors on exit. Screen clears
  also erase in black as a fallback. This makes the game readable in ANY light-themed terminal -- a
  white xterm, macOS Terminal's default profile, or a fallback path -- regardless of the launcher.
  Windows, BBS door sessions (CP437), the MUD server/relay, and the Electron client are all deliberately
  untouched, and the bundled WezTerm keeps its own theme.
- **The accessible launcher's xterm fallback** gets the same white-on-black flags.

## Store-page accuracy (website + README)

Not part of the game binary, but shipped alongside this release: the website and README misstated
what the game contains. The class count now reads 12 (Mystic Shaman was missing), the companion count
reads 5 (Melodia was missing), and the Magician's spell claim was corrected (it has the 25-spell arcane
library, not "75 spells" which is a game-wide figure). Mystic Shaman and Melodia now have their own
cards on both the landing page and the Steam page, in all five site languages. The README version
reference was 4 releases stale and is updated, with a recent-highlights entry for the 0.62-0.65 arc.
(The in-game "Beta" banner is unchanged; that flip is a release-day action, not a 0.65.5 change.)

## Documentation accuracy pass

The pre-1.0 audit found several docs describing retired architecture or wrong numbers; all corrected:
ARCHITECTURE.md now describes the current haproxy port-4000 topology (the old sslh diagram, the removed
`usurper-world.service`, and a stale Godot reference are gone); MULTIPLAYER_ARCHITECTURE.md is marked as
the historical per-process design with a pointer to the current model, and its password-hashing claim
corrected (PBKDF2, not bcrypt); SERVER_DEPLOYMENT.md points at the production topology as the source of
truth; BBS_DOOR_SETUP.md documents `--screen-reader` and `--auto-provision`, the automatic CP437
encoding, and clarifies the standalone world-sim's status; MODDING.md's data counts corrected (15
monster families, 79 achievements) and `--export-discoveries` documented; DOCKER.md's clone URL fixed;
and a new DOCS/LLM_CONFIG.md documents the optional `USURPER_LLM_*` sysop configuration for the first
time. CLAUDE.md's stale IP-ban security note was also clarified (account bans cover every path; only
SSH-path IP attribution is descoped).

## Files Changed
- `Scripts/Core/GameConfig.cs` -- Version 0.65.5
- `Scripts/Systems/CombatEngine.cs` -- T0-3: boss phase-immunity + divine-armor reduction added to
  `ApplyAoEDamage` (per-target, mirroring `ApplySingleMonsterDamage`); Maelstrom of the Faithful call
  site passes `isSpellDamage: true`. T1-2: spouse + divine-blessing XP bonuses ported into the live
  `HandleVictoryMultiMonster` path (were dead code in the unreachable `HandleVictory`) with reward-screen
  display lines
- `Scripts/Systems/SaveSystem.cs` -- T0-1: `SaveGame` aborts (preserving the on-disk save) when the
  multi-session MUD server has no `SessionContext`, before backup or write. Gated on `IsMudServerMode`
  (not `IsOnlineMode`): the per-process "Simple" deployment also reports online but never sets a context
  and cannot have this bug (one player per process), so guarding on `IsOnlineMode` would break its saves.
  Also adds `DeathDate` to the NPC save write (T0-2 round-trip)
- `Scripts/Systems/WorldSimulator.cs` -- T0-2: `MarkNPCDead` permadeath branch sets the real
  `IsPermaDead` + `DeathDate` + removes from the respawn timers so a rolled permadeath is permanent, and
  re-enables the "will not return" death-feed news + PERMADEATH log (accurate now that the death sticks);
  both the permadeath branch and `ProcessNPCAging` call `RomanceTracker.OnNPCPermadied` +
  `NPCMarriageRegistry.OnNPCPermadied` unconditionally (lover/FWB/affair cleanup, not just spouse).
  T1-6: `ProcessOrphanComingOfAge` checks the living-population cap before removing the orphan / deleting
  the Child, deferring to a later tick at cap
- `Scripts/Systems/SaveDataStructures.cs` / `Scripts/Core/GameEngine.cs` /
  `Scripts/Systems/OnlineStateManager.cs` / `Scripts/Systems/WorldSimService.cs` -- T0-2: `DeathDate`
  added to `NPCData` and all four NPC round-trip sites so the 7-day prune grace window survives the
  world-state reload (previously reset to null on reload, making permadead corpses eligible for immediate
  pruning and shortening the recent-death lookup window for news/grief/recognition)
- `Scripts/Core/GameConfig.cs` (`MaxSerializedNPCInventory`) + `Scripts/Systems/SaveSystem.cs` +
  `Scripts/Systems/OnlineStateManager.cs` -- Tier 2 save-bloat: cap the per-NPC personal inventory +
  market stock in both serialize paths; cap the online NPC `Enemies` list for parity with single-player
- `Scripts/Locations/ChurchLocation.cs` -- T1-7: "this month" church figures seeded on the in-game month
  index instead of `Random.Shared` per visit
- `Scripts/Systems/TrainingSystem.cs` -- T1-3: pass `skillName` to `single_reset_lore_7` (Hungarian word
  order moves the variable into that line; EN ignores the extra arg)
- `Scripts/Locations/BankLocation.cs` / `Scripts/Systems/EndingsSystem.cs` /
  `Scripts/Locations/PantheonLocation.cs` / `Scripts/Core/GameEngine.cs` -- T1-5: log the previously
  swallowed save/autosave/recovery-scan exceptions
- `Localization/{es,fr,it,hu}.json` -- T1-3: `dungeon.monster_appears` + `dungeon.boss_blocks_path_visual`
  + `dungeon.boss_blocks_path_sr` rewritten to single-arg to match the current call site (were rendering
  raw `{1}` template); added `love_corner.already_married`, `street.fight.over_cap_diminished`,
  `street.fight.over_cap_hint`
- `launchers/play.sh` -- rewritten WezTerm section (supervised run with startup-failure fallback,
  no-display short-circuit, libfuse2 diagnostics); fallback terminal order desktop-first with xterm
  last and forced white-on-black
- `launchers/play-accessible.sh` / `launchers/usurper-reborn.sh` -- xterm fallback forced white-on-black
- `Console/Bootstrap/Program.cs` -- dark-terminal paint at startup (OSC 11/10) on interactive Unix
  terminals with restore-on-exit; gated off Windows / WezTerm / BBS / MUD / Electron / worldsim /
  redirected output
- `Scripts/UI/TerminalEmulator.cs` -- `ClearScreen` erases in black (SGR 40) when the dark paint is
  active, covering terminals that ignore OSC 11
- `Scripts/Core/GameConfig.cs` -- new `DarkTerminalPaint` flag (also carries the 0.65.5 version bump
  and `MaxSerializedNPCInventory` listed above)
- `web/index.html` / `web/steam.html` -- T1-1: class count 11 -> 12, companion count 4 -> 5, Magician
  "75 spells" -> "25 arcane spells"; new Mystic Shaman class card + Melodia companion card
- `web/lang/{en,es,fr,hu,it}.json` -- T1-1: same count/spell corrections in all five site languages;
  Mystic Shaman + Melodia card keys added to en.json (other languages fall back to the English HTML)
- `README.md` -- T1-1: version 0.61.4 -> 0.65.5 (three references), recent-highlights entry for 0.62-0.65
- `DOCS/ARCHITECTURE.md` / `DOCS/MULTIPLAYER_ARCHITECTURE.md` / `DOCS/SERVER_DEPLOYMENT.md` /
  `DOCS/BBS_DOOR_SETUP.md` / `DOCS/MODDING.md` / `DOCS/DOCKER.md` / `CLAUDE.md` -- documentation
  accuracy pass (see section above)
- `DOCS/LLM_CONFIG.md` -- new sysop guide for the `USURPER_LLM_*` environment configuration
- `DOCS/RELEASE_1_0_AUDIT.md` -- the full readiness audit this release draws from (progress section updated)
