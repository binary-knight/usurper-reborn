# v0.43.1 - BBS Compatibility & Prison Fixes

Bug fixes for the Castle system and a critical display fix for BBS terminals running on Linux.

---

## Bug Fixes

### CP437 Encoding for BBS Terminals
BBS users connecting via SyncTERM, NetRunner, or other classic terminals saw garbled box-drawing characters — Unicode multi-byte UTF-8 sequences were being interpreted as individual CP437 glyphs, producing garbage output. When BBS stdio mode is auto-detected (Synchronet, Mystic SSH, redirected I/O), the game now sets `Console.OutputEncoding` to CP437, which makes .NET automatically convert all Unicode box-drawing characters (╔═╗║╚╝ etc.) to their correct single-byte CP437 equivalents. Web terminal, direct SSH, and local mode are unaffected — they continue using UTF-8.

### Prison System Not Working
The king's "Imprison" command only added a bookkeeping record to the prison list but never actually imprisoned anyone. The `DaysInPrison` field on the NPC/player character was never set, so imprisoned characters could still move freely. Now:
- **NPCs**: `DaysInPrison` is set and `CurrentLocation` changed to "Prison" — imprisoned NPCs are removed from the world
- **Players (online)**: `DaysInPrison` is atomically set in the player's save data via `SqlSaveBackend.ImprisonPlayer()`, so they see the prison screen on next login
- **Pardon**: Now properly clears `DaysInPrison` on NPCs and players
- **Execute**: NPCs are permanently killed (`IsPermaDead = true`); players are released with a gold penalty and notification

### Prison Commands Use Numbered Selection
All prison commands (Imprison, Pardon, Execute, Set Bail) previously required typing the exact prisoner/target name. Now they use numbered selection matching the displayed list — just enter the `#` shown next to the name.

### Imprison Shows Target List
The "Imprison" command now shows a numbered list of NPCs (and players in online mode) instead of requiring free-text name entry. Each target shows their level and class for easy identification.

---

## Files Changed

| File | Changes |
|------|---------|
| `Scripts/Core/GameConfig.cs` | Version bump to 0.43.1 |
| `Scripts/BBS/DoorMode.cs` | Auto-detect CP437 mode for BBS stdio — registers `CodePagesEncodingProvider` and sets `Console.OutputEncoding` to CP437 when BBS door mode uses stdio (excludes MUD server/relay/worldsim) |
| `Scripts/Locations/CastleLocation.cs` | `ImprisonSomeone()` rewritten with numbered NPC/player list and actual `DaysInPrison` enforcement; `PardonPrisoner()` clears `DaysInPrison` on NPC and player; `ExecutePrisoner()` permadeaths NPC, releases player with gold penalty; all prison commands converted from name typing to `#` selection; online mode sends notification messages and persists royal court |
| `Scripts/Systems/SqlSaveBackend.cs` | New `ImprisonPlayer()` method — atomically sets `$.player.daysInPrison` in a player's save data via `json_set` |
| `usurper-reloaded.csproj` | Added `System.Text.Encoding.CodePages` NuGet package (provides CP437 encoding for .NET 8) |
