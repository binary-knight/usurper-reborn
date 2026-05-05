# v0.60.8 -- Beta

Two bug fixes on top of v0.60.7. One PvP-protection gap reported by a player who was getting attacked while sleeping at home behind the Reinforced Door, and one single-player Linux quality-of-life report where the SysOpConfig file was showing up as a phantom save in the load menu.

---

## Bug fix: poison and burn DoTs cancelling each other out

Player report (Lumina, Lv.23 Sage): *"Poison status does not work when burning status is active. Existing poison status is erased when Roast is cast. Does not work at all if I cast Poison after Roast."*

Architectural cause: monster fire DoT and poison DoT shared the **same** `PoisonRounds` counter on `Monster.cs`, with `IsBurning` as a flag-based "discriminator." The tick handler only fired ONE DoT per round (whichever the flag pointed at). Casting Roast on a poisoned target overwrote the burn state -- `IsBurning = true; PoisonRounds = N` -- which erased the poison's tick branch. Casting Poison after Roast did the inverse: set `Poisoned = true` but `IsBurning` stayed true so the tick still rendered as a burn.

The same monster-side burn-apply pattern existed in 4 places: `case "fire"` in single-monster spell handler, single-monster ability handler, multi-monster ability handler, and the engulfed-in-flames spell. All four set `IsBurning = true` and overwrote `PoisonRounds`. (The Mystic Shaman's Searing Totem additionally set `IsBurning = true` without ANY duration, so the burn flag was true but the tick gate `PoisonRounds > 0` was false -- the burn never actually fired.)

### Fix

New independent `BurnRounds` field on `Monster` (alongside existing `PoisonRounds`). Burn-apply sites now set `IsBurning = true; BurnRounds = duration` and leave `PoisonRounds` / `Poisoned` alone. Poison-apply sites unchanged.

Tick handler in `ProcessMonsterStatusEffects` rewritten as two independent blocks. Both burn and poison can fire on the same round if both are active. Burn deals slightly more per-tick than poison (matches the higher-damage nature of fire). Each block has its own death-message and depleted-flag-reset path.

Status display updated: instead of showing one of `BURN(N)` or `PSN(N)` based on the discriminator flag, it now shows both independently when both are active. Searing Totem now properly seeds `BurnRounds` so the totem's fire DoT actually ticks.

Player side (Character.cs) was unaffected -- the player uses the generic `ActiveStatuses` dictionary where `Poisoned` and `Burning` are already independent entries with their own per-round damage logic.

Files: `Scripts/Core/Monster.cs` (new `BurnRounds` field, reset on combat-state-clear), `Scripts/Systems/CombatEngine.cs` (tick handler split, 4 apply sites migrated, Searing Totem fix, status display).

---

## Bug fix: home-sleepers attackable in the PvP arena

Player report: "Players who go to sleep in their houses with doors upgrade are still available for attacking in the PvP arena. Sleeping in the house is supposed to prevent all PvP while offline."

The Reinforced Door home upgrade is sold explicitly as the safe-sleep option (the Inn and Dormitory sleeper-attack flows already filter by `SleepLocation == "inn"` / `== "dormitory"`, so home-sleepers were never vulnerable through those paths). But `ArenaLocation.ChooseOpponent` had 6 opponent-eligibility filters and none of them checked `sleeping_players`. Anyone asleep at home still appeared in the arena's opponent list and was attackable.

Fix: `ChooseOpponent` now fetches the sleeping-players list alongside the other backend reads, builds a case-insensitive `HashSet<string>` of usernames where `SleepLocation == "home"` and not dead, and adds a `.Where(p => !sleepingAtHome.Contains(p.Username))` clause to the eligibility chain. Home-sleepers are now PvP-immune across all three vectors (arena + the already-correct inn / dormitory flows).

Files: `Scripts/Locations/ArenaLocation.cs`.

---

## FILE_ID.DIZ included in download zips

Sysop request: a BBS file database expects `FILE_ID.DIZ` inside the downloaded archive to populate the door's description. Previously the zip only contained `LICENSE`, `GPL_NOTICE.txt`, and `README.txt`.

Added: `dist/FILE_ID.DIZ` (10 lines, max 46 chars wide, ASCII, CRLF line endings -- the standard BBS file-database format). The CI workflow now copies it into the build output alongside the existing compliance files, so it ships in all 6 plain platform zips (and transitively in the WezTerm desktop bundles for Windows x64 / Linux x64). No effect on Steam build or live-server deploy.

Content patterned after the original 1993 Usurper FILE_ID.DIZ (kept the *kicking fantasy* tagline) but updated for the v0.60.x feature set: 100-floor dungeon, seven Old Gods, online multiplayer, ascend-to-godhood path, GPL v2.

Files: `dist/FILE_ID.DIZ` (new), `.github/workflows/ci-cd.yml` (one extra `cp` in the GPL-compliance step).

---

## Bug fix: SysOpConfig file appearing as a phantom save

Player report (single-player Linux): *"The game creates `~/.local/share/UsurperReloaded/saves/sysop_config.json` when it starts up, and that file is picked up as a save."*

The file is the SysOpConfig (admin tunables, MOTD, difficulty multipliers, BBS-mode persistence layer). It lives in the save directory intentionally so BBS sysops get per-BBS-namespaced configuration, but `FileSaveBackend`'s three listing paths (`GetAllSaves`, `GetPlayerSaves`, `GetAllPlayerNames`) only filtered by `_autosave` / `_backup` / `emergency_` patterns. `sysop_config.json` slipped through, failed `SaveGameData` deserialization, and fell into the v0.57.18 filename-fallback path -- showing up as a phantom "sysop_config" character in the load menu.

Fix: new `IsAuxiliaryFile(fileName)` private static helper at the top of `FileSaveBackend` returning true for `sysop_config.json` (and easy to extend if any future auxiliary files land in the save dir). All three listing methods now skip auxiliary files alongside the existing rotation / backup / emergency filters. The file's persistence path is unchanged -- BBS namespacing is preserved.

Existing single-player saves with the phantom slot will see it disappear automatically on next launch.

Files: `Scripts/Systems/FileSaveBackend.cs`.

---

## Files Changed

### No SQLite schema changes
Code-only release. Existing `server_config` / `server_config_schema` / `server_config_apply_queue` schema from v0.60.7 carries forward unchanged.

### New files
- `dist/FILE_ID.DIZ` -- BBS file-database description (10 lines, CRLF).

### Modified files (game)
- `Scripts/Core/GameConfig.cs` -- version bump to 0.60.8.
- `Scripts/Core/Monster.cs` -- new `BurnRounds` field; reset to 0 alongside `IsBurning` on combat-state-clear.
- `Scripts/Locations/ArenaLocation.cs` -- `ChooseOpponent` skips home-sleepers from the PvP opponent list.
- `Scripts/Systems/CombatEngine.cs` -- burn DoT separated from poison DoT (independent counters, both can tick per round); 4 burn-apply sites + Searing Totem migrated; status display shows BURN and PSN independently.
- `Scripts/Systems/FileSaveBackend.cs` -- new `IsAuxiliaryFile` predicate filters `sysop_config.json` from all three save-listing paths.

### Modified files (CI)
- `.github/workflows/ci-cd.yml` -- copy `dist/FILE_ID.DIZ` into the build output so it ships in every platform zip.

---

## Deploy notes

- Standard recipe: publish linux-x64, tar, scp, restart `usurper-mud` and `sshd-usurper`. Web service unchanged.
- The SysOpConfig phantom-save fix only matters for single-player file-backed saves. The live MUD server uses `SqlSaveBackend` and was unaffected.
- The home-sleeper PvP fix matters for any MUD server with players who use the Reinforced Door upgrade.
