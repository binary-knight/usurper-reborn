# v0.41.1 - BBS Terminal Overhaul

Comprehensive BBS 80x25 terminal support. Every game screen now fits within the 80-column, 25-row BBS terminal constraint. Includes automatic pagination, compact location menus, compact combat display, input echo fixes, and session management improvements.

---

## Bug Fixes

### Duplicate Main Street Header
Main Street displayed two separate "MAIN STREET" box headers — one from the location banner and one from the menu section. Removed the duplicate so only one header is shown.

### NPC Encounter Header Alignment
The right-side border on NPC encounter headers (e.g. "GRETA - THE FORMER ADVENTURER") was misaligned because the text was padded with a fixed-width format string that didn't account for variable name lengths. Headers are now dynamically centered within the box.

### World Sim Spam in Single-Player
`[WORLDSIM] Birth:` messages were printing to the console in single-player mode due to redundant `Console.Error.WriteLine` calls. Removed — the DebugLogger already captures the same information to debug.log.

### Royal Armory Equipment Not Working
Purchasing items from the Royal Armory (Crown faction exclusive shop in the Castle) took your gold but the item never appeared in your equipment or backpack. The equipment was created without being registered in the equipment database, so it was assigned ID 0 and lost on equip. Fixed by registering Royal Armory items as dynamic equipment before equipping.

### Double Echo in SyncTERM / Synchronet
Typing characters through SyncTERM connected to Synchronet BBS showed each character twice (e.g., pressing 'd' displayed 'dd'). Synchronet echoes characters via its telnet WILL ECHO negotiation, and the game was also echoing in `ReadLineWithBackspace()`. Fixed by suppressing game-side echo when running in BBS door mode.

### Backspace Shows House Character (⌂) in SyncTERM
Pressing backspace through SyncTERM showed a visible ⌂ glyph instead of erasing the previous character. Synchronet echoes the raw 0x7F (DEL) byte, which renders as ⌂ in CP437. Fixed by sending a double-erase sequence (`\b \b\b \b`) in BBS mode to erase both the echoed glyph and the previous character.

### Session Lockout on Reconnect
Reconnecting to the online server after a disconnect showed "player already logged in — rejecting duplicate session" and kicked the player out. Fixed across all three connection paths (direct online, auth screen, MUD relay) to clear stale sessions instead of rejecting new ones. The 120-second heartbeat timeout now properly allows reconnection.

---

## BBS 80x25 Screen Pagination

BBS terminals display 80 columns by 25 rows. Previously, output that exceeded 25 lines would scroll off the top of the screen, causing players to miss critical information — especially during multi-monster combat rounds.

### Automatic "-- More --" Prompt
The terminal now tracks lines written since the last screen clear or user input. When output reaches row 23 (leaving room for the prompt), a `-- More --` prompt appears and waits for a keypress before continuing. This works universally across all game screens — combat, inventory, stats, journal, and every other location.

### Combat Page Breaks
Combat rounds now clear the screen at the start of each round in BBS mode. Each round's status display, action results, and menu fit within a single 25-row page instead of continuously scrolling.

### Monster Art Skipped in BBS Mode
ASCII monster silhouettes and Old God art (10-15 lines each) are automatically skipped in BBS door mode to conserve screen space. The art still displays normally in local play, online SSH, and Steam.

---

## Compact BBS Location Menus

Every game location now has a dedicated compact BBS display that fits within the 80x25 constraint. In BBS door mode, locations use compressed 1-line headers, inline NPC lists, dense menu grids, and a compact status bar — reducing typical 30-40 line displays down to 12-18 lines.

### Shared BBS Helpers (BaseLocation)
New reusable helper methods for all locations:
- `ShowBBSHeader()` — 1-line centered title with decorative borders
- `ShowBBSNPCs()` — 1-line NPC summary (2 names + "and N others")
- `ShowBBSStatusLine()` — 1-line HP/Gold/Mana/Level with XP%
- `ShowBBSQuickCommands()` — 1-line quick command bar
- `ShowBBSMenuRow()` — Dense menu item renderer
- `ShowBBSFooter()` — Status + commands combo

### Compact Combat Display
Combat status and action menus have BBS-specific versions:
- `DisplayCombatStatusBBS()` — Monsters 2-per-line with 8-char HP bars, player on 2 lines
- `ShowCombatMenuBBS()` — Single-monster actions in 2 compact lines
- `ShowDungeonCombatMenuBBS()` — Multi-monster actions in 2 compact lines
- Skills shown as `[1-9]Skills(N)` instead of listing each individually

### No Impact on Other Modes
All compact displays are gated on BBS door mode (`DoorMode.IsInDoorMode`). Single-player, online SSH, MUD server, and Steam modes are completely unaffected.

---

## Dungeon Map Redesign

The dungeon map has been completely rewritten in a compact roguelike style. Rooms are now single characters (`@`, `#`, `█`, `>`, `B`, `·`, `?`) connected by box-drawing line characters (`───`, `│`). The legend appears alongside the map on the right side instead of below. Much easier to read at a glance, and fits comfortably within 80x25 BBS terminals.

---

## Combat Potion Fix

Mana potions were inaccessible in single-monster combat — the [H]eal key only offered healing potions. Now pressing H in any combat mode routes through the full potion choice system: if you have both HP and mana potions and both are needed, you get a submenu; if only one type is needed, it auto-uses that type. The BBS combat menu now shows `[H]Pot(HP:5/MP:3)` with counts for both potion types.

---

## Maintenance Date Path Fix

Daily maintenance failed to save its completion date due to a double `DATA/` path (`DATA/DATA/DATE.DAT`). Fixed by using the correct path constant. This prevented the game from remembering when maintenance last ran.

---

## Files Changed

| File | Changes |
|------|---------|
| `Scripts/Core/GameConfig.cs` | Version 0.41.1, version name "BBS Terminal Overhaul" |
| `Scripts/Locations/BaseLocation.cs` | BBS helper methods (ShowBBSHeader, ShowBBSNPCs, ShowBBSStatusLine, ShowBBSQuickCommands, ShowBBSMenuRow, ShowBBSFooter); NPC encounter header alignment fix |
| `Scripts/Locations/MainStreetLocation.cs` | Compact BBS display with online multiplayer row; removed duplicate header |
| `Scripts/Locations/DungeonLocation.cs` | Compact BBS room view and floor overview; roguelike dungeon map redesign |
| `Scripts/Locations/InnLocation.cs` | Compact BBS display |
| `Scripts/Locations/WeaponShopLocation.cs` | Compact BBS display |
| `Scripts/Locations/ArmorShopLocation.cs` | Compact BBS display |
| `Scripts/Locations/MagicShopLocation.cs` | Compact BBS display |
| `Scripts/Locations/CastleLocation.cs` | Compact BBS king/visitor displays; Royal Armory equipment fix |
| `Scripts/Locations/HealerLocation.cs` | Compact BBS display |
| `Scripts/Locations/ChurchLocation.cs` | Compact BBS display |
| `Scripts/Locations/BankLocation.cs` | Compact BBS display |
| `Scripts/Locations/LevelMasterLocation.cs` | Compact BBS display |
| `Scripts/Locations/HomeLocation.cs` | Compact BBS display |
| `Scripts/Locations/DarkAlleyLocation.cs` | Compact BBS display |
| `Scripts/Locations/AnchorRoadLocation.cs` | Compact BBS display |
| `Scripts/Locations/LoveStreetLocation.cs` | Compact BBS display |
| `Scripts/Locations/MarketplaceLocation.cs` | Compact BBS display |
| `Scripts/Locations/TeamCornerLocation.cs` | Compact BBS display |
| `Scripts/UI/TerminalEmulator.cs` | BBS line counter with auto "-- More --" prompt; echo suppression for BBS mode; backspace double-erase for CP437 |
| `Scripts/Systems/CombatEngine.cs` | Compact BBS combat status and action menus; ClearScreen at round start; skip monster art; mana potions accessible in single-monster combat; BBS potion display shows HP+MP counts |
| `Scripts/Systems/OldGodBossSystem.cs` | Skip Old God art in BBS mode |
| `Scripts/Systems/WorldSimulator.cs` | Removed redundant Console.Error birth spam |
| `Console/Bootstrap/Program.cs` | Clear stale online session on reconnect instead of rejecting |
| `Scripts/Systems/OnlineAuthScreen.cs` | Clear stale session on login instead of rejecting |
| `Scripts/Server/MudServer.cs` | Kick-and-replace for duplicate sessions |
| `Scripts/Systems/MaintenanceSystem.cs` | Fixed double DATA/ path for maintenance date file |
| `README.md` | Version bump to 0.41.1 |
