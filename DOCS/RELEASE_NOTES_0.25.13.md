# Usurper Reborn - v0.25.13 Release Notes

## Bug Fixes

### EleBBS Socket Connection Fix
- **EleBBS door mode now works**: EleBBS passes valid socket handles with overlapped (async) I/O, but three issues prevented the game from using them:
  - **Zero-length test write killed the connection**: The socket validation used `_stream.Write(bytes, 0, 0)` which throws on overlapped socket handles even when the socket is valid. Replaced with an IAC NOP (telnet no-op) which actually tests the connection without side effects.
  - **Raw handle fallback assumed synchronous I/O**: The `FileStream` fallback used `isAsync: false`, but EleBBS opens sockets for overlapped I/O. Now tries sync first, then falls back to async mode.
  - **Socket init returned success when broken**: `Initialize()` returned `true` even when both socket and raw handle failed, causing the game to silently fall through to local `Console.Write` - remote users saw nothing. Now properly returns `false` so the game can fall back to local console mode with appropriate error messaging.
- **Verbose mode no longer blocks BBS connections**: Removed 8 intermediate "Press Enter to continue" pauses from `--verbose` debug output. These required the sysop to blindly press Enter multiple times, during which the telnet connection could time out (one sysop's connection died after 4 minutes of pauses). Now only pauses once at the very end of initialization so the sysop can read all diagnostics before the game starts.

### Wave Fragment Collection Fixes
- **Grief System resurrection attempts now grant wave fragments**: The 4th and 5th resurrection attempts had philosophical dialogue about letting go and love's permanence, but never actually called `CollectFragment()`. The 3rd attempt correctly granted `TheReturn`, but the 4th (river metaphor) and 5th+ (permanence of love) were missing. Now grants `TheForgetting` (4th attempt) and `TheChoice` (5th+ attempt).
- **Old God dialogue trees now grant wave fragments**: Three Old God dialogue paths that represented the deepest philosophical engagement now reward players with wave fragments:
  - Veloura (choosing to save her from corruption) grants `TheCorruption`
  - Thorgrim (breaking his logic with a justice paradox) grants `TheSevenDrops`
  - Noctura (forging the shadow alliance pact) grants `ManwesChoice`

### Files Changed
- `SocketTerminal.cs` - Fixed test write (IAC NOP instead of zero-length); async raw handle fallback; proper failure return
- `DoorMode.cs` - Removed 8 blocking verbose pauses; kept single final pause for diagnostics
- `GriefSystem.cs` - Added CollectFragment calls for 4th and 5th resurrection attempts
- `DialogueSystem.cs` - Added CollectWaveFragment effects to Veloura, Thorgrim, and Noctura dialogue trees
