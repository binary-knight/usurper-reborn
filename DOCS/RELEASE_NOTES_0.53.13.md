# Usurper Reborn v0.53.13 Release Notes

**Version Name:** Ancestral Spirits (Beta Hardening)

## Trade Item Data Loss Fix

When a player sent items via the trade system and the recipient declined, cancelled, or let the offer expire, the items vanished. Only gold was being returned — items were permanently lost.

**Root cause:** The decline, cancel, and expiry code paths in the trade system only returned gold via `AddGoldToPlayer()`. The item JSON was stored in the database but never deserialized and returned to the sender's inventory.

**Fix:** All three trade resolution paths (decline, cancel, expiry) now return items to the sender. For online players who may be offline when their trade is declined/expired, a new `AddItemsToPlayerSave()` method safely merges returned items into their saved inventory via JSON manipulation.

## Save Data Persistence Fix

Five daily-reset properties were never serialized, causing them to reset to defaults on every login:

- **DrinksLeft** — Tavern drink counter reset on logout
- **PrisonsLeft** — King imprisonment limit reset on logout
- **ExecuteLeft** — King execution limit reset on logout
- **QuestsLeft** — Quest board daily limit reset on logout
- **PrisonActivitiesToday** — Prison activity counter reset on logout

All five are now properly serialized, saved, and restored on load.

## Combat Engine Crash Prevention

Three `.First()` LINQ calls in the combat engine could throw `InvalidOperationException` if their source collections were empty:

- PvP AI ability selection
- Healer AI doom dispel target selection
- Healer AI corruption cleanse target selection

All changed to `.FirstOrDefault()` with null guards for defense-in-depth.

## Server Stability Improvements

**Fire-and-forget connection handling:** The MUD server's connection handler was called as a fire-and-forget task (`_ = HandleConnectionAsync()`). If the handler threw an unexpected exception, it was silently swallowed — no logging, no cleanup. Now wrapped with try/catch and logged via DebugLogger.

**Exception logging in hot paths:** 22 empty catch blocks in the four highest-traffic files (CombatEngine, GameEngine, BaseLocation, DungeonLocation) now log exceptions to the debug log. Previously these silently swallowed errors, making post-mortem debugging impossible.

## Moddable Game Data System (Phase 1)

Game data can now be loaded from external JSON files, enabling modding without recompilation. Run the game with `--export-data` to generate editable JSON files in a `GameData/` folder next to the executable.

**Six data sources are now moddable:**
- **npcs.json** — Town NPC templates (names, classes, races, personalities, romance traits)
- **monster_families.json** — Monster families and tier progressions (16 families, 80 tiers)
- **dreams.json** — Narrative dreams and visions with trigger conditions
- **achievements.json** — Achievement definitions with categories, tiers, and rewards
- **dialogue.json** — NPC dialogue lines with personality/context matching
- **balance.json** — Combat balance constants (crit chance, backstab multiplier, boss tuning, daily limits)

**How it works:** If a `GameData/` folder exists next to the exe with any of these JSON files, the game loads them instead of built-in defaults. Missing files fall back to defaults. Bad JSON logs an error and uses defaults — never crashes. Enum values in JSON use human-readable strings (`"Warrior"` not `10`) with case-insensitive matching.

**Precedence:** Built-in C# defaults < balance.json (SysOp config controls different settings like MOTD and difficulty multipliers)

**For modders:** Run `UsurperReborn --export-data` to generate all default JSON files as a starting point. Edit what you want, delete files you don't need to change. Restart the game to apply.

## Inventory Crash Fix

Viewing an equipment slot with no matching items in the backpack (e.g., pressing R for rings when no rings are in inventory) crashed the session with `ArgumentException: '0' cannot be greater than -1`.

**Root cause:** `Math.Clamp(page, 0, totalPages - 1)` threw when `totalPages` was 0 (empty filtered inventory), making `max = -1` which is less than `min = 0`.

**Fix:** Added `totalPages > 0` guard before clamping on both filtered and unfiltered inventory paths.

## Quick Command % Fix

The `%` key for status display (added in v0.53.12) was not working — it showed "Invalid choice!" instead of the status screen. The keybinding was added to a dead code switch block instead of the active `TryProcessGlobalCommand()` method. Now works from every location.

## Screen Reader Preference Persistence (by evanofficial)

The `--screen-reader` CLI flag now only needs to be passed once. The preference is saved to `sysop_config.json` and restored on future launches. Toggling screen reader mode from the pre-login menu or main menu also persists immediately. The CLI flag always takes precedence over the stored value.

## Localization Sync

Added missing `combat.loot_displaced_to_player` key to Spanish, Hungarian, Italian, and French. All 5 language files are now fully synced at 16,769 keys.

## Test Suite Expansion

115 new automated tests (469 → 584 total):

**CombatEngineTests** (42 tests): Combat result data structures, boss phase transitions (including per-boss custom thresholds), monster combat state, player combat properties, combat action types, and MonsterGenerator integration.

**SaveRoundTripTests** (40 tests): Comprehensive serialization round-trip coverage for all daily-reset properties, combat buff counters, herb inventory, login streaks, weekly rankings, Blood Moon state, home upgrades, immortal/god system, faction consumables, prison state, diseases, equipped items, inventory, preferences, team XP distribution, chest contents, and a full "all daily reset" property sweep.

**TolerantEnumConverterTests** (7 tests): Case-insensitive enum deserialization, unknown value fallback, nullable enum handling, and round-trip preservation for all 17 character classes.

**GameDataLoaderTests** (10 tests): Built-in data source counts, balance config defaults/application/clamping, and JSON enum serialization with GameDataLoader options.

---

## Files Changed

### Moddable Data System (new)
- `Scripts/Systems/GameDataLoader.cs` — **NEW** — Central data loader with file discovery, JSON deserialization, export, and caching
- `Scripts/Utils/TolerantEnumConverter.cs` — **NEW** — Case-insensitive enum JSON converter with unknown-value fallback
- `Scripts/Data/BalanceConfig.cs` — **NEW** — Balance constant POCO with validation/clamping and ApplyToGameConfig()
- `Console/Bootstrap/Program.cs` — `GameDataLoader.Initialize()` call at startup; `--export-data` CLI flag
- `Scripts/Core/GameConfig.cs` — Version 0.53.13; 20 `Mod*` balance properties for runtime override
- `Scripts/Data/ClassicNPCs.cs` — `GetClassicNPCs()` checks GameDataLoader first
- `Scripts/Data/MonsterFamilies.cs` — `AllFamilies` property routes through GameDataLoader; built-in data as fallback
- `Scripts/Systems/DreamSystem.cs` — Dreams property routes through GameDataLoader; `GetBuiltInDreams()` for export
- `Scripts/Systems/AchievementSystem.cs` — `Initialize()` loads from GameDataLoader; `GetBuiltInAchievements()` for export
- `Scripts/Data/NPCDialogueDatabase.cs` — `Initialize()` checks GameDataLoader; `GetAllBuiltInLines()` for export

### Bug Fixes
- `Scripts/Systems/InventorySystem.cs` — `Math.Clamp` crash fix when viewing empty equipment slots
- `Scripts/Locations/BaseLocation.cs` — `%` quick command added to active TryProcessGlobalCommand switch; trade decline/cancel returns items; logging added to 10 empty catch blocks
- `Scripts/Core/GameEngine.cs` — Restore 5 daily counter properties on load; logging added to 10 empty catch blocks; persist screen reader preference on toggle (by evanofficial)
- `Scripts/Systems/SysOpConfigSystem.cs` — `ScreenReaderMode` property added to config; CLI flag priority over stored value (by evanofficial)
- `Scripts/Systems/SaveDataStructures.cs` — Added DrinksLeft, PrisonsLeft, ExecuteLeft, QuestsLeft, PrisonActivitiesToday to PlayerData
- `Scripts/Systems/SaveSystem.cs` — Serialize/deserialize 5 new daily counter fields
- `Scripts/Systems/CombatEngine.cs` — `.First()` → `.FirstOrDefault()` with null guards (3 sites); logging added to 4 empty catch blocks
- `Scripts/Systems/SqlSaveBackend.cs` — New `AddItemsToPlayerSave()` method for returning trade items to offline players; trade expiry now returns items + gold
- `Scripts/Locations/DungeonLocation.cs` — Logging added to 5 empty catch blocks
- `Scripts/Server/MudServer.cs` — `HandleConnectionAsync` fire-and-forget wrapped with try/catch + logging
- `Localization/es.json` — Added `combat.loot_displaced_to_player`
- `Localization/hu.json` — Added `combat.loot_displaced_to_player`
- `Localization/it.json` — Added `combat.loot_displaced_to_player`
- `Localization/fr.json` — Added `combat.loot_displaced_to_player`

### Tests
- `Tests/CombatEngineTests.cs` — **NEW** — 42 combat engine tests (58 with parameterized data)
- `Tests/SaveRoundTripTests.cs` — **NEW** — 40 save serialization round-trip tests
- `Tests/TolerantEnumConverterTests.cs` — **NEW** — 7 enum converter tests
- `Tests/GameDataLoaderTests.cs` — **NEW** — 10 data loader and balance config tests
