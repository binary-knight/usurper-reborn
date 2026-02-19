# v0.42.5 - SysOp & BBS Polish

SysOp update check fix and BBS quick command bar improvement.

---

## Bug Fixes

### SysOp Update Check Broken in Online Mode
The SysOp Console's "Check for Updates" option always showed "You are running the latest version!" with a blank "Latest:" field when running in online mode. The version checker was skipping the GitHub API call entirely for online mode — even when the SysOp explicitly requested a check. The online mode skip now only applies to the automatic background check, not to manual checks from the SysOp Console.

---

## UI Improvements

### Preferences Shortcut on BBS Quick Bar
The `[~]Prefs` shortcut is now shown on the compact BBS quick command bar across all locations, including Main Street. Previously it was only visible on the full-size (non-BBS) display.

---

## Files Changed

| File | Changes |
|------|---------|
| `Scripts/Core/GameConfig.cs` | Version 0.42.5 |
| `Scripts/Systems/VersionChecker.cs` | `CheckForUpdatesAsync`: online mode skip now respects `forceCheck` parameter |
| `Scripts/Locations/BaseLocation.cs` | Added `[~]Prefs` to `ShowBBSQuickCommands()` compact bar |
| `Scripts/Locations/MainStreetLocation.cs` | Added `[~]Prefs` to Main Street's inline BBS quick bar |
