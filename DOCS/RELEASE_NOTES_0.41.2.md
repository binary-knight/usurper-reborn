# v0.41.2 - BBS Polish

Bug fixes and improvements for BBS door mode. Removes world simulator console spam, adds a unified SysOp Console that works in both single-player and online modes, and updates the BBS setup documentation.

---

## New Features

### Unified SysOp Console for Online Mode
BBS sysops running `--online` mode now get a full-featured SysOp Console with SQL-backed player management. The console automatically detects the mode and shows the appropriate menu:

**Single-Player Mode** — File-based player management (view, delete, pardon), difficulty settings, MOTD, debug log, NPC viewer, auto-update.

**Online Mode** — SQL-backed player management (list, ban/unban, delete, edit, reset password, pardon), difficulty settings, MOTD, view online players, game statistics, debug log, NPC viewer, clear news, broadcast messages, auto-update, and full game wipe/reset.

Online mode delegates SQL operations to `OnlineAdminConsole` methods, so all existing SQL player management features are available through the SysOp Console without duplication.

### SysOp Game Statistics Dashboard
The SysOp Console's "View Game Statistics" screen has been completely rebuilt for online mode. Instead of just a player count and in-memory NPC data, sysops now see a comprehensive SQL-powered dashboard:

- **Players** — Total, active, online, banned, average level, highest-level player, most popular class, newest player
- **Economy** — Gold in circulation, bank deposits, total earned/spent, items bought/sold, active bounties, active auctions
- **Combat** — Monsters killed, bosses killed, PvP fights/kills, PvE deaths, total damage dealt, deepest dungeon floor reached
- **World** — Active teams, news entries, messages, total player playtime, database file size

### View Recent News
SysOps can now preview the news feed before clearing it. Shows the 50 most recent entries with relative timestamps ("5m ago", "2h ago", "3d ago"), category tags, and message text. Paginated at 15 entries per page with Next/Prev navigation.

### Kick Online Player
SysOps can force-disconnect stuck or misbehaving players. Lists all currently online players (excluding self) with their location, connection type, and session duration. Requires confirmation before kicking.

### MOTD Display on BBS Login Screen
The Message of the Day (MOTD) now displays on the BBS login screen before the character menu. Shown in bright yellow between the version number and the "Welcome" greeting. Default MOTD for new installations: "Thanks for playing Usurper Reborn! Report bugs with the in-game ! command." SysOps can change or clear it from the SysOp Console.

### Custom Character Names in BBS Mode
BBS players can now choose their own character name during character creation instead of being locked to their BBS username. The BBS username (from DOOR32.SYS/DOOR.SYS) is used internally as the save key for authentication and file/database lookups, while the custom display name (Name2) is what other players see in-game, in the news feed, leaderboards, and Who's Online. Works in both single-player BBS mode (file saves) and online multiplayer (SQL database). In online mode, duplicate display names are rejected during character creation. The BBS login screen now shows the character's display name alongside their level and class.

### Change Password Hidden in BBS Mode
The `[C] Change Password` option on the login screen is no longer shown when running as a BBS door in online mode. BBS users authenticate through their BBS software, not through in-game passwords.

### Support the Developer Page
A new `[@] Support the Developer` option appears on both the main menu and the BBS character selection screen for non-Steam players. Shows a page explaining that Usurper Reborn is free and open source, links to the GitHub Sponsors page, suggests starring the GitHub repo, and mentions the upcoming Steam release. Hidden in Steam builds since those users have already contributed.

---

## Bug Fixes

### World Simulator Console Spam in BBS Mode
`[WORLDSIM]` status messages (NPC loads, state saves, king restorations, version changes) were printing directly to the player's terminal in BBS door mode. All 40+ world simulator log messages across WorldSimService, Program.cs, and DoorMode.cs have been redirected to `debug.log` via `DebugLogger`. Players will no longer see internal world sim diagnostics on their screen.

### SysOp Menu Not Showing for BBS Online Mode
BBS sysops running `--online` mode saw a duplicate `[%] Admin Console` entry instead of (or alongside) the `[%] SysOp Administration Console`. The Admin Console took priority, blocking access to BBS-specific features like auto-update, debug log viewer, and NPC monitoring. Fixed by always showing a single `[%] SysOp Administration Console` entry and prioritizing the SysOp Console for BBS sysops.

### Auto-Update Blocked in Online Mode
The background update check was skipped entirely when `--online` was active, preventing BBS sysops from using the auto-update feature. Removed the `IsOnlineMode` skip so sysops can check for and install updates regardless of mode.

### Full Game Wipe Missing 14 Tables
The "Full Game Wipe/Reset" in online mode only cleared 5 of 19 database tables. Now clears all 18 game data tables in a single transaction.

### SysOp Difficulty Settings Had No Effect
The four SysOp difficulty multipliers (XP, Gold, Monster HP, Monster Damage) were saved and displayed in the admin console but never actually applied to gameplay. The `DifficultySystem` per-character difficulty and the `GameConfig` sysop multipliers were completely disconnected. Fixed by wiring the sysop multipliers into all combat reward calculations and monster generation. The sysop multipliers now stack on top of per-character difficulty (e.g., a player on Easy with a server-wide 2x XP multiplier gets 3x XP total).

### BBS Connection Startup Spam
40+ diagnostic messages (`DoorMode.Log()`, drop file info, BBS auto-detection, connection details) were printing to the player's terminal via `Console.Error.WriteLine` when launching the game as a BBS door. All redirected to `debug.log` via `DebugLogger`. Also fixed `GD.PrintErr()` globally, which was the source of the "cannot find DATE.DAT" maintenance error leaking to BBS terminals.

### Opening Story Skip Not Working in BBS Mode
Pressing space to skip or speed up the new character creation story had no effect in BBS door mode. The skip detection used raw `Console.KeyAvailable`/`Console.ReadKey` which don't work when stdin is redirected. Replaced with `TerminalEmulator.IsInputAvailable()` and `FlushPendingInput()` which work across all I/O modes.

### Maintenance DATE.DAT Error on Logout
BBS players saw a "cannot find DATE.DAT" error on logout. The maintenance system's file-based date tracking doesn't apply in online mode (world sim handles maintenance). Added an online mode guard to skip file operations, and fixed the root cause (`GD.PrintErr` leaking to BBS terminal).

---

## Documentation

### BBS Echo Configuration Guide
Added a new "Character Echo Configuration" section to `BBS_DOOR_SETUP.md` explaining that the game does not echo typed characters in BBS door mode. Includes a per-BBS echo settings table and troubleshooting for "no characters when typing" and "double characters" issues.

---

## Files Changed

| File | Changes |
|------|---------|
| `Scripts/Systems/SysOpConsoleManager.cs` | Added online mode detection, `DisplayOnlineConsole()`, `ProcessOnlineChoice()`, refactored `Run()` to branch on mode, fixed stats for SQL, enabled auto-update in online mode |
| `Scripts/Systems/OnlineAdminConsole.cs` | Changed 12 methods from `private` to `internal` for delegation |
| `Scripts/Systems/WorldSimService.cs` | Replaced 36 `Console.Error.WriteLine("[WORLDSIM]` calls with `DebugLogger` |
| `Console/Bootstrap/Program.cs` | Replaced 4 `Console.Error.WriteLine("[WORLDSIM]` calls with `DebugLogger` |
| `Scripts/BBS/DoorMode.cs` | Replaced 3 `Console.Error.WriteLine("[WORLDSIM]` calls with `DebugLogger` |
| `Scripts/Core/GameEngine.cs` | Unified SysOp/Admin console menu, SysOp prioritized for BBS; MOTD display on login; hide Change Password in BBS mode; show character display name on login; update online display name on load/create; `[@] Support the Developer` page on MainMenu and RunBBSDoorMode (non-Steam only) |
| `Scripts/Systems/CharacterCreationSystem.cs` | BBS players choose custom display name (Name2) while Name1 stays as BBS username; duplicate name check in online mode |
| `Scripts/Systems/OnlineStateManager.cs` | Added `UpdateDisplayName()` to sync custom character name to online_players table |
| `Scripts/Systems/IOnlineSaveBackend.cs` | Added `UpdateOnlineDisplayName()` interface method |
| `Scripts/Systems/DifficultySystem.cs` | `ApplyExperienceMultiplier`, `ApplyGoldMultiplier`, `ApplyMonsterDamageMultiplier` now stack `GameConfig` sysop multipliers |
| `Scripts/Systems/CombatEngine.cs` | All 4 reward calculation sites now include `GameConfig.XPMultiplier` and `GameConfig.GoldMultiplier` |
| `Scripts/Systems/MonsterGenerator.cs` | `CalculateMonsterStats()` now applies `GameConfig.MonsterHPMultiplier` to generated monster HP |
| `Scripts/Systems/SqlSaveBackend.cs` | Added `GetGameStatistics()` with `SysOpGameStats` class; `FullGameReset()` now wipes all 18 game tables; added `UpdateOnlineDisplayName()` |
| `Scripts/Core/GameConfig.cs` | Default MOTD for new installations |
| `Scripts/Systems/SysOpConfigSystem.cs` | Default MOTD in config class |
| `Scripts/Systems/OpeningStorySystem.cs` | BBS-compatible skip using `TerminalEmulator.IsInputAvailable()` instead of raw `Console.KeyAvailable` |
| `Scripts/BBS/DoorMode.cs` | (also) Redirected 40+ `Console.Error.WriteLine` and `DoorMode.Log()` calls to `DebugLogger` |
| `Console/Bootstrap/Program.cs` | (also) Redirected crash handler stderr output to `DebugLogger` |
| `Scripts/Utils/GodotStubs.cs` | `GD.PrintErr()` now routes through `DebugLogger` instead of `Console.Error.WriteLine` |
| `Scripts/Systems/MaintenanceSystem.cs` | Skip file-based maintenance in online mode; directory creation guard |
| `Scripts/Core/GameConfig.cs` | (also) Version bump to 0.41.2 |
| `DOCS/BBS_DOOR_SETUP.md` | Added Character Echo Configuration section and Synchronet echo note |
