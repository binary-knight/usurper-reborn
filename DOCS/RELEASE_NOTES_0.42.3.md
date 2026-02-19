# v0.42.3 - BBS Hotfix

Fixes crashes on Windows BBS systems and teammate equipment bug.

---

## Bug Fixes

### Timezone Crash on Windows BBS (Critical)
Fixed a crash when creating new characters or logging in on Windows-based BBS systems. The daily reset system used the IANA timezone ID `America/New_York` which only works on Linux/macOS. Windows uses `Eastern Standard Time`. The game now tries both, so it works on all platforms.

### Teammate/Companion/Spouse Equipment — Inventory Items Not Showing
When equipping teammates (Team Corner), companions (Inn), or spouses (Home), only items you had equipped on your person appeared in the list. Items held in your inventory (backpack) were invisible because the code looked them up by name in the equipment database — loot drops, dungeon finds, and unequipped gear don't exist in that database. Now converts inventory items directly from their stats, matching how the player's own equip-from-backpack already works.

### SysOp Update Check Showing Stale Results
The SysOp Console's "Check for Updates" was returning cached results from a previous check (up to 4 hours old). If you checked before a new release was published, it would keep showing "You are running the latest version." The manual check now always fetches fresh from GitHub.

---

## Files Changed

| File | Changes |
|------|---------|
| `Scripts/Core/GameConfig.cs` | Version 0.42.3 |
| `Scripts/Systems/DailySystemManager.cs` | Cross-platform timezone fix: try IANA `America/New_York`, fall back to Windows `Eastern Standard Time` |
| `Scripts/Locations/BaseLocation.cs` | Added `ConvertInventoryItemToEquipment()` shared helper for Item-to-Equipment conversion |
| `Scripts/Locations/TeamCornerLocation.cs` | Use `ConvertInventoryItemToEquipment()` instead of `EquipmentDatabase.GetByName()` |
| `Scripts/Locations/HomeLocation.cs` | Use `ConvertInventoryItemToEquipment()` instead of `EquipmentDatabase.GetByName()` |
| `Scripts/Locations/InnLocation.cs` | Use `ConvertInventoryItemToEquipment()` instead of `EquipmentDatabase.GetByName()` |
| `Scripts/Systems/VersionChecker.cs` | Added `forceCheck` parameter to bypass cache on manual SysOp update checks |
| `Scripts/Systems/SysOpConsoleManager.cs` | SysOp "Check for Updates" now forces fresh GitHub fetch |
| `README.md` | Version bump to 0.42.3 |