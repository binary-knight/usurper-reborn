# v0.57.18 - Single-Player Save Reliability

Critical reliability pass on the single-player save system after multiple Steam players reported saves bloating, vanishing from the load menu, or refusing to load entirely.

## Player Reports That Drove This

> "Saves seem to just run out of memory after I quit. Only way I can keep going is if I leave the game on."

> "I just loaded up the game and my single player file's gone." — files were intact on disk; the load menu just showed empty.

> "I stopped playing on the .exe and switched to steam and there were no issues all day yesterday." — then both binaries showed empty load menus on next launch.

These are the same bug-family v0.57.14 (load-path resilience) and v0.57.16 (NPC memory cap) tried to fix. The earlier fixes addressed the symptoms surfaced at the time but missed several adjacent code paths and additional bloat surfaces. This pass closes all the remaining gaps.

## Symptom 1 — Saves Disappeared From the Load Menu

**Root cause: three separate silent-drop bugs across `FileSaveBackend`'s listing methods.**

The load menu calls `GetAllPlayerNames()` to populate the character list. That method silently swallowed any exception during deserialize (no fallback at all), so a single bloated save would vanish from the menu, and if ALL of a player's saves were bloated the menu fell through to "no saves found" → straight to character creation. From the player's seat: saves "gone."

`GetPlayerSaves()` (the per-character recent-saves picker) had the same shape — `catch { /* silently log */ }`, no filename-derived fallback. `GetAllSaves()` had a fallback added in v0.57.14, but the fallback's own catch block could silently drop the slot if any of `Path.GetFileNameWithoutExtension` / `new FileInfo` threw on a race or odd filesystem state.

**Fix:** all three listing methods now have a guaranteed fallback. If JSON deserialize throws, we synthesize a `SaveInfo` from the filename + `LastWriteTime`. The slot ALWAYS appears in the load menu — it's marked with a new `IsRecovered` flag so the UI can render `[RECOVERY]` and the click handler can short-circuit straight to `ShowLoadFailureWithRecovery` (the v0.57.14 recovery menu) instead of a redundant load-attempt-then-fail.

`GetAllPlayerNames` specifically: previously emergency saves (`emergency_*.json`) were filtered out entirely. Now if a player's regular `<Name>.json` is missing or unparseable but `emergency_<Name>_<timestamp>.json` exists on disk, that name is extracted from the filename and surfaced. The character is no longer "gone" — just available through the recovery slot, marked `[EMERGENCY SAVE]`.

## Symptom 2 — "Saves Run Out of Memory After I Quit"

**Root cause: v0.57.16 capped one NPC unbounded field (memories) but four MORE per-NPC fields and three RoyalCourt lists were still uncapped.**

Audit turned up:

- **`RecentDialogueIds`** — `NPCDialogueDatabase` tracks every dialogue line ever delivered to each NPC, with no in-memory cap. Across 100+ sessions × 130 NPCs that's thousands of entries each. Serialized in full on every save.
- **`CharacterImpressions`** — per-NPC relationship dict. Grows as NPCs meet each other; never prunes when NPCs die.
- **`KnownCharacters`** — per-NPC social graph list. Same shape — grows unboundedly.
- **`Enemies`** — per-NPC grudge list. Appended on every wronging; no decay.
- **RoyalCourt `Prisoners` / `Orphans` / `MonarchHistory`** — three world-state lists with no caps. Accumulate across reigns/imprisonments/orphan cohorts.

Combined: with 130 NPCs each leaking a few KB per session across these fields, a single-player save can hit 50+ MB after a long playthrough. `JsonSerializer.Deserialize` then OOMs on the next load — the file looks like it "vanished" because the load failed silently before the v0.57.18 listing fixes above.

**Fix — caps applied at serialization time** (mirrors the v0.57.16 pattern; existing bloated saves self-heal on next save):

| Field | Cap | New `GameConfig` constant |
|---|---|---|
| `RecentDialogueIds` per NPC | 50 | `MaxSerializedDialogueIdsPerNpc` |
| `CharacterImpressions` per NPC | 100 (top by \|strength\|) | `MaxSerializedRelationshipsPerNpc` |
| `KnownCharacters` per NPC | 80 (most recent) | `MaxSerializedKnownCharactersPerNpc` |
| `Enemies` per NPC | 30 (most recent) | `MaxSerializedEnemiesPerNpc` |
| RoyalCourt `Prisoners` | 50 | `MaxSerializedRoyalCourtPrisoners` |
| RoyalCourt `Orphans` | 100 (most recent) | `MaxSerializedRoyalCourtOrphans` |
| RoyalCourt `MonarchHistory` | 30 (most recent) | `MaxSerializedMonarchHistory` |

Selection strategies are tuned per-field — `CharacterImpressions` keeps the strongest absolute values (most narrative weight), `Enemies` / `KnownCharacters` / `MonarchHistory` keep the recent tail (older entries fade naturally), `Prisoners` / `Orphans` keep the first N (these are slow-churn lists).

In-memory state is unchanged — only the serialized footprint is trimmed. NPCs still know about every other NPC they've met during the session; they just don't all survive the save/reload boundary if a particular NPC has accumulated more than the cap. For the typical cap (80 known characters per NPC × 130 NPCs = 10,400 names = ~200 KB), no real player will hit the limit organically.

Audited but already bounded: `WorldEventSystem` (events auto-expire via `OnEventEnds`), `FamilySystem.SerializeChildren` (filters `!Deleted`), `CulturalMemeSystem` (`MAX_ACTIVE_MEMES` cap + dead-meme cleanup).

## Symptom 3 — Cascading Failures During the Load Sequence

The actual save load happens through `LoadSaveByFileName` → `LoadSaveByFileNameWithError` → on failure, `ShowLoadFailureWithRecovery` (added in v0.57.14). That recovery menu was already comprehensive — it lists `<name>_backup.json`, the last 3 `<name>_autosave_*.json` files, and `emergency_autosave.json`. The v0.57.14 fix was solid but only useful if the player got that far.

The bug-chain that prevented players from reaching the recovery menu was Symptoms 1 + 2 combined — the corrupted slot vanished from the load list before the player could click it. With this version's listing fixes, every save on disk is now visible (with a `[RECOVERY]` or `[EMERGENCY SAVE]` tag), the click handler short-circuits straight to `ShowLoadFailureWithRecovery` for known-bad slots, and the recovery menu does the rest.

**For affected players whose saves currently won't load:** launch v0.57.18, go to Load Game, and the slot should now appear (likely tagged `[RECOVERY]`). Clicking it opens the recovery menu showing your backup, autosaves, and emergency files. Pick the most recent recoverable file and the game restores it as the primary save. The bloat caps then run on the next save and shrink the file back to a reasonable size for future loads.

## SAVE_AUDIT Telemetry

New `SAVE_AUDIT` log lines on every save record the file size in KB, and emit a loud warning when a save crosses the new `GameConfig.SaveSizeWarningBytes = 5 MB` threshold. Mirrors the existing `GOLD_AUDIT SUSPICIOUS:` pattern. Goal: surface the next bloat surface BEFORE it reaches OOM territory, so future "saves vanished" reports come with a forensic trail showing when the save started growing and which audit warning flagged first.

## SaveDirectory Namespace — Audit

The research agent flagged a possible Steam vs standalone divergence via `DoorMode.GetSaveNamespace()`. Audit confirmed this isn't a single-player risk: the namespace is gated on `IsInDoorMode`, which requires a parsed BBS drop file (`_sessionInfo != null && _sessionInfo.SourceType != DropFileType.None`). Steam and standalone single-player both resolve to `%APPDATA%\UsurperReloaded\saves` — same directory regardless of binary. No change needed.

## Second-Round Audit — Write-Path Corruption + Additional Bloat Surfaces

After the first round shipped, a second comprehensive audit looked at write-path failure modes the listing/cap fixes don't protect against. Three new corruption classes plus three more bloat surfaces, all closed in the same release.

### Atomic Write + Concurrent-Save Guard + Pre-Serialize OOM Guard

`FileSaveBackend.WriteGameData` previously did a single `JsonSerializer.SerializeAsync` straight to a `FileStream` opened on the primary save path. Three latent corruption modes:

1. **Torn file on crash mid-write.** If the process died (Ctrl+C, OOM, power loss, OS panic) partway through serialization, the primary save was left half-written — readable but truncated, and the next load would throw `JsonException` somewhere mid-stream. With the v0.57.14 backup-on-write pattern the player could recover from `<name>_backup.json`, but the *primary* slot was still toast and would surface as a `[RECOVERY]` slot on next load.

2. **Concurrent saves stomping each other.** `SaveSystem.SaveGame` is called from many places (autosave throttle, location transitions, manual save, post-combat, world-sim tick). Most callers `await` the result, but a few are fire-and-forget. Two saves to the same file path could overlap inside `WriteGameData` — the second `FileStream(FileMode.Create)` truncates the file the first call is mid-write to. Result: corrupted JSON.

3. **OOM during serialization writes a half-formatted file.** When the in-memory NPC tree was bloated (the v0.57.16 + v0.57.18-round-1 caps fixed this, but pre-fix saves still exist), `JsonSerializer.SerializeAsync` could OOM mid-stream — file is half-written, primary slot corrupted, next load fails. The bloat caps prevent the *cause*, but the write path should be defensive.

**Fix — three layers in `WriteGameData`:**

- **`SemaphoreSlim _writeLock = new(1, 1)`** wraps the entire write body. One save to the file backend at a time, full stop. Other write paths (`DeleteGameData`, `CreateBackup`) take the same lock so backups can't race deletes either. Performance impact: negligible — saves are millisecond-scale and the player isn't issuing concurrent saves intentionally.

- **MemoryStream pre-serialize.** Serialize to `MemoryStream` first, capture `byte[]`. If serialization OOMs, the primary file is *never touched*. Caller gets `false`, the load menu still has the previous good save. Without this, even with the bloat caps, an unexpected serialization spike could still trash the file.

- **Atomic write via temp + flush + rename.** Serialized bytes go to `<file>.tmp` first, `FlushAsync()` forces the kernel buffers out to disk, then `File.Move(tmp, primary, overwrite: true)` atomically swaps. NTFS, ext4, and APFS all guarantee `File.Move` with overwrite is atomic at the directory-entry level — readers either see the old file or the new file, never a half-written one. If the process dies between flush and rename, `<file>.tmp` is left on disk (cleanup happens on next save) but the primary save is intact and still loadable.

The combination means: bloated save serializing OOMs → `false` returned, primary intact. Process killed mid-write → temp file orphaned, primary intact. Concurrent save attempts → serialized through the lock, no race.

### Emergency Save — Per-Character + Per-Timestamp + Auto-Rotation

Ctrl+C / `CTRL_CLOSE_EVENT` handler in `Console/Bootstrap/Program.cs` previously wrote to a single fixed key `"emergency_autosave"`. Two issues with that:

1. **Different characters clobbered each other.** Player plays Character A, Ctrl+C, emergency save written. Switches to Character B, Ctrl+C, emergency save written — Character A's emergency dump is gone. The recovery menu had no way to know *whose* dump it was either, since the filename had no character info — so it surfaced as a generic "emergency_autosave.json" option in every character's recovery menu.

2. **Repeated Ctrl+Cs all overwrote each other.** A player who triggers emergency saves 10 times across a session ends up with exactly one — the most recent. No history.

**Fix — per-character + per-timestamp naming + auto-rotation:**

- New format: `emergency_<sanitized-charname>_<yyyy-MM-dd_HH-mm-ss>.json`. Each Ctrl+C produces a distinct file, scoped to the character that was active.
- The first-round `FileSaveBackend.GetAllPlayerNames` / `GetAllSaves` emergency-aware listing parses this back into a recovery slot for the specific character — `[EMERGENCY SAVE]` tag in the load menu only on that character's row.
- New `FileSaveBackend.RotateEmergencySaves(playerName)` keeps only the 3 most recent emergency dumps per character (deletes the rest by `LastWriteTime`). Called from Program.cs immediately after the successful save. A player who Ctrl+Cs repeatedly during a chaotic session ends up with up-to-3 recovery slots, not 50.

Player-facing message rewritten to name the character explicitly: `"Look for '<charname>' in the save menu — it will appear marked [EMERGENCY SAVE] (or [RECOVERY] if other saves exist)."`

### Three More Bloat Surfaces

The first-round audit covered NPC fields and RoyalCourt lists. The second pass turned up three more unbounded-list surfaces in player-scope state:

| Field | Location | Cap | New `GameConfig` constant |
|---|---|---|---|
| `RomanceTracker.EncounterHistory` | per-player intimate-encounter log | 100 (most recent) | `MaxSerializedEncounterHistory` |
| `Companion.Inventory` | per-companion bag | 30 (most recent) | `MaxSerializedCompanionInventory` |
| `StrangerEncounterSystem.UsedDialogueIds` | per-player dialogue dedup set | 50 | `MaxSerializedStrangerDialogueIds` |
| `StrangerEncounterSystem.RecentGameEvents` | per-player event context queue | 20 (most recent) | `MaxSerializedStrangerRecentEvents` |

`EncounterHistory` is the largest of these — each entry is ~200 bytes once partner names + watcher IDs are included, and a long playthrough with active romance can accumulate hundreds. `Companion.Inventory` is dangerous because combat loot can pile up over a long dungeon run if the player never empties the bag. `StrangerEncounterSystem` normally clears `RecentGameEvents` after each encounter (`StrangerEncounterSystem.cs:873`) but drifts upward if encounters are interrupted mid-flow.

All four caps are serialization-time only — in-memory state is unchanged, the player keeps every encounter / item / dialogue ID during the session, only what survives a save/reload boundary is trimmed. Selection: lists keep the most-recent N (newest content matters most); the `UsedDialogueIds` HashSet has no insertion order so an arbitrary N is kept (re-using an old line after 50+ new ones is preferable to OOM).

## Third-Round Audit — Conversation States, Royal Court Collections, NPC Restore Resilience

A third pass turned up two more growth surfaces and a defense-in-depth opportunity in the load path.

**`VisualNovelDialogueSystem.npcConversationStates`** is a per-NPC dict that grows one entry per NPC the player has ever conversed with. Each entry carries a `TopicsDiscussed` HashSet that itself grows one entry per dialogue topic discussed. Long playthroughs talking to many NPCs about many topics easily reach hundreds of KB worth of conversation-state entries that never expire. Capped at serialization time in `GetConversationStatesForSave` — most-recently-conversed-with NPCs (by `LastConversationDate desc`) survive up to `MaxSerializedConversationStates = 100`; per-conversation `TopicsDiscussed` capped at `MaxSerializedTopicsDiscussedPerConvo = 30` (HashSet has no insertion order, so an arbitrary 30 are kept — re-discussing an old topic is harmless, OOM is not).

**RoyalCourt CourtMembers / Heirs / MonsterGuards.** First-round capped Prisoners / Orphans / MonarchHistory but missed three more lists in the same `RoyalCourtSaveData` block. `CourtMembers` is appended in `WorldSimulator` as NPCs cycle through political positions across reigns with no obvious decay path; `Heirs` and `MonsterGuards` similarly grow without a guaranteed prune. New caps: `MaxSerializedCourtMembers = 50` (selected by `Influence desc` — the politically relevant survive), `MaxSerializedHeirs = 20` (designated heir always survives, then ranked by `ClaimStrength desc`), `MaxSerializedMonsterGuards = 30` (most recent purchases via `TakeLast` — keeps the active garrison). `Guards` already has a runtime cap (`MaxGuards = 15`) so was left alone; `ActivePlots` similarly capped at 3 at runtime.

**`NPCMarriageRegistry.affairs`** ConcurrentDictionary accumulates one entry per flirt-on-married-NPC — only `ClearAffair` removes them and few callers actually do. Capped at `MaxSerializedAffairs = 50` in `GetAllAffairs()`. Selection: active affairs (`IsActive=true`) survive first (narratively load-bearing), then most-recently-touched. Older inactive entries dropped from the persisted snapshot.

**`RestoreNPCs` per-iteration try/catch (defense-in-depth).** The 500-line per-NPC restore loop was unguarded — a single bad NPC entry (corrupt enum, malformed dictionary, unexpected null in deserialized data) would throw and abort the entire restore, leaving `NPCSpawnSystem` partially populated and the world half-built. Same shape of bug as the original `FileSaveBackend.GetAllPlayerNames` silent-drop. Wrapped each iteration's body in try/catch; bad entries log a warning under `NPC` category and the restore keeps going. The world loses one NPC instead of all of them.

**`JsonSerializerOptions.MaxDepth = 256`** added to `FileSaveBackend.jsonOptions`. Defensive — the .NET 8 default is 64, which is deep enough for any current realistic save graph (the deepest nesting is `WorldEvent.Parameters` dictionary chains at ~5-7 deep), but the explicit override prevents any future deeply-nested addition from silently breaking saves with an opaque `JsonException: maximum depth`.

## Files Changed

- `Scripts/Core/GameConfig.cs` — version bump 0.57.17 → 0.57.18; first-round constants `MaxSerializedDialogueIdsPerNpc=50`, `MaxSerializedRelationshipsPerNpc=100`, `MaxSerializedKnownCharactersPerNpc=80`, `MaxSerializedEnemiesPerNpc=30`, `MaxSerializedRoyalCourtPrisoners=50`, `MaxSerializedRoyalCourtOrphans=100`, `MaxSerializedMonarchHistory=30`, `SaveSizeWarningBytes=5MB`; second-round constants `MaxSerializedEncounterHistory=100`, `MaxSerializedCompanionInventory=30`, `MaxSerializedStrangerDialogueIds=50`, `MaxSerializedStrangerRecentEvents=20`; third-round constants `MaxSerializedConversationStates=100`, `MaxSerializedTopicsDiscussedPerConvo=30`, `MaxSerializedCourtMembers=50`, `MaxSerializedHeirs=20`, `MaxSerializedMonsterGuards=30`, `MaxSerializedAffairs=50`.
- `Scripts/Systems/SaveDataStructures.cs` — `SaveInfo` gained `IsRecovered` and `IsEmergency` flags so the load menu can distinguish recovery slots from normal loads.
- `Scripts/Systems/FileSaveBackend.cs` — `GetAllPlayerNames`, `GetAllSaves`, `GetPlayerSaves` all gained guaranteed filename-fallback when JSON deserialize throws (no more silent drops). `GetAllSaves` and `GetAllPlayerNames` also surface `emergency_*.json` files when no primary save for that character exists. New `ParseCharacterNameFromEmergencyFile` helper. `WriteGameData` now logs `SAVE_AUDIT` lines on every save, with a loud warning above 5 MB. Second round: `_writeLock` SemaphoreSlim added; `WriteGameData` rewritten to MemoryStream-pre-serialize (catches OOM before touching files) → temp file → flush → atomic `File.Move(overwrite: true)`. `DeleteGameData` and `CreateBackup` wrapped in the same lock so backups/deletes can't race writes. New `RotateEmergencySaves(playerName)` keeps only the 3 most recent emergency dumps per character. `RotateAutosaves` switched from `CreationTime` to `LastWriteTime` (correct ordering after atomic-rename writes). Third round: explicit `MaxDepth = 256` on `JsonSerializerOptions` (defensive — .NET 8 default is 64, no current save approaches that, but prevents a future deep-nesting addition from breaking saves with an opaque `JsonException`).
- `Scripts/Core/GameEngine.cs` — load-menu rendering now displays `[RECOVERY]` (yellow) or `[EMERGENCY SAVE]` (bright red) tags on `IsRecovered` / `IsEmergency` slots in both the player-name list and the per-character save list. Slot selection short-circuits straight to `ShowLoadFailureWithRecovery` for known-bad slots, skipping the redundant load-attempt-then-fail step. Third round: `RestoreNPCs` per-iteration try/catch — bad NPC entries log a warning and skip instead of aborting the entire restore (was a latent silent-failure mode that could leave `NPCSpawnSystem` half-populated).
- `Scripts/Systems/SaveSystem.cs` — per-NPC serialization caps applied at the four sites (`RecentDialogueIds`, `Enemies`, `KnownCharacters`, `SerializeNPCRelationships`). RoyalCourt `MonarchHistory`, `Prisoners`, `Orphans` capped at the top-level `RoyalCourt` serialization site. Third round: RoyalCourt `CourtMembers` (by `Influence desc`), `Heirs` (designated first, then `ClaimStrength desc`), and `MonsterGuards` (`TakeLast`) also capped at the `RoyalCourt` serialization site.
- `Scripts/Systems/RomanceTracker.cs` — `EncounterHistory` capped at `MaxSerializedEncounterHistory` (most recent) at serialization time via `TakeLast`.
- `Scripts/Systems/CompanionSystem.cs` — per-companion `Inventory` capped at `MaxSerializedCompanionInventory` (most recent) at serialization time via reverse-take-reverse.
- `Scripts/Systems/StrangerEncounterSystem.cs` — `UsedDialogueIds` (HashSet, arbitrary-N when over cap) and `RecentGameEvents` (List, most-recent-N) capped at serialization time.
- `Scripts/Systems/VisualNovelDialogueSystem.cs` — `GetConversationStatesForSave` caps `npcConversationStates` at `MaxSerializedConversationStates` (sorted by `LastConversationDate desc`) and per-entry `TopicsDiscussed` at `MaxSerializedTopicsDiscussedPerConvo`.
- `Scripts/AI/EnhancedNPCBehaviors.cs` — `NPCMarriageRegistry.GetAllAffairs()` caps the affair snapshot at `MaxSerializedAffairs`. Active affairs (`IsActive=true`) survive first; remainder ranked by `LastInteraction desc`.
- `Console/Bootstrap/Program.cs` — Ctrl+C emergency save renamed from fixed `"emergency_autosave"` to `emergency_<charname>_<timestamp>.json`. After successful save, `RotateEmergencySaves(charName)` keeps only the 3 most recent dumps per character. Player-facing message names the character explicitly.

## Deploy Notes

Game binary only. No save format change. Existing bloated saves self-heal on next save thanks to the serialization-time caps. Existing players with vanished saves will see them re-appear in the load menu after upgrading — they'll be tagged `[RECOVERY]` and clicking opens the v0.57.14 recovery menu (backup + autosaves + emergency files). Pre-existing `emergency_autosave.json` files (from prior versions) still load through the recovery menu's `emergency_autosave.json` entry; new emergency saves are per-character. Orphaned `<file>.tmp` files left behind by prior crashes are harmless — they're cleaned up on the next save to that slot.
