# v0.54.3 - Bug Fixes & CI/CD Overhaul

## XP Distribution Reset Fix

Custom team XP distribution settings (e.g., 12% player, 22% per companion) were silently overwritten to an even split after every combat. The `AutoDistributeTeamXP` function was supposed to only auto-distribute when all teammate XP slots were at 0% (i.e., teammates just joined and had no distribution set), but it unconditionally redistributed every time. Players' custom settings were wiped after every fight.

- **Custom distributions are now preserved** across combat — `AutoDistributeTeamXP` only kicks in when all teammate slots are at 0%
- **Dead teammate share redistribution** — if a teammate dies mid-combat with a custom distribution, only their share is moved to the player instead of resetting everything

## Children Sent to Orphanage When Player Offline

Player characters' children were incorrectly sent to the Royal Orphanage whenever the NPC parent (e.g., Sir Galahad) temporarily died during world simulation combat — but only when the player parent was offline.

Root cause: `IsParentDeadOrMissing()` checked `GameEngine.Instance?.CurrentPlayer` to detect player parents, but in the MUD server's world sim thread, `CurrentPlayer` is always null. The code fell through to the NPC lookup, didn't find the player there (they're not an NPC), and concluded "parent not found = dead" — orphaning the child.

- **Player parents now detected by ID format** — NPC IDs start with `npc_`, player IDs don't. If the other parent has a non-NPC ID, they're a player character and always considered alive.

## Food Buff Lost on Logout

Inn meal buffs (Dragon Steak, Honey Bread, Iron Rations, Mushroom Soup) were lost on logout. The food buff properties (`FoodBuffType`, `FoodBuffCombats`, `FoodBuffValue`) were explicitly marked as "transient, not serialized" and never added to the save pipeline. The daily meal counter (`MealsToday`) was also not serialized, so it reset on login — players could eat unlimited meals by relogging.

- **Food buffs now persist across sessions** — all 4 food buff properties added to SaveDataStructures, SaveSystem serialization, and GameEngine restore
- **Daily meal counter persists** — `MealsToday` now saved and restored, preventing the relog exploit

## CI/CD Pipeline Overhaul

Complete rewrite of the GitHub Actions workflow (1,275 lines to 763 lines) with 10 improvements:

- **Parallel platform builds** — 6 platform builds now run concurrently via matrix strategy instead of sequentially in one job
- **Eliminated redundant Steam rebuilds** — Steam builds are a separate matrix, no longer duplicating the 6 plain builds
- **Deduplicated WezTerm downloads** — shared cache key between desktop bundling and Steam packaging (was downloaded twice independently)
- **Automated server deploy** — new `deploy-server` job runs on release or manual `workflow_dispatch`, uploads tarball via SCP and restarts services
- **Gated Steam builds to releases only** — `steam-prep` no longer runs on every push to main
- **Removed fake report job** — printed hardcoded "SUCCESS" regardless of actual results
- **Removed test existence guards** — unnecessary `if [ -d "Tests" ]` wrappers on every test step
- **Fixed macOS Info.plist version** — was hardcoded "1.0", now uses release tag
- **Checked in GPL/README files** — `dist/GPL_NOTICE.txt` and `dist/README.txt` replace 20 inline `echo` commands
- **Removed stale Steam release notes** — hardcoded "21 Major Phases / 4 classes / 300 tests" text deleted

---

### Files Changed

- `Scripts/Core/GameConfig.cs` — Version 0.54.3
- `Scripts/Core/Character.cs` — Removed "transient, not serialized" comment from food buff properties
- `Scripts/Core/GameEngine.cs` — Restore FoodBuffType, FoodBuffCombats, FoodBuffValue, MealsToday on load
- `Scripts/Systems/CombatEngine.cs` — AutoDistributeTeamXP preserves custom distributions; only auto-distributes when all teammate slots are 0%; dead teammate share goes to player
- `Scripts/Systems/SaveDataStructures.cs` — Added FoodBuffType, FoodBuffCombats, FoodBuffValue, MealsToday
- `Scripts/Systems/SaveSystem.cs` — Serialize food buff properties on save
- `Scripts/Systems/WorldSimulator.cs` — IsParentDeadOrMissing detects player parents by non-NPC ID format; no longer requires CurrentPlayer to be set
- `.github/workflows/ci-cd.yml` — Complete rewrite: matrix builds, automated deploy, deduplicated downloads, removed dead code
- `dist/GPL_NOTICE.txt` — **NEW** — GPL compliance notice (checked into repo)
- `dist/README.txt` — **NEW** — Distribution readme (checked into repo)
