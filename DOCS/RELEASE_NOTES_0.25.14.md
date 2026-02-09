# Usurper Reborn - v0.25.14 Release Notes

## Bug Fixes

### EleBBS Socket Connection Fix
- **EleBBS door mode now works**: EleBBS passes valid socket handles with overlapped (async) I/O, but three issues prevented the game from using them:
  - **Zero-length test write killed the connection**: The socket validation used `_stream.Write(bytes, 0, 0)` which throws on overlapped socket handles even when the socket is valid. Replaced with `Socket.Poll(SelectMode.SelectWrite)` which verifies the socket without sending any bytes - safe for both telnet and SSH.
  - **Raw handle fallback assumed synchronous I/O**: The `FileStream` fallback used `isAsync: false`, but EleBBS opens sockets for overlapped I/O. Now tries sync first, then falls back to async mode.
  - **Socket init returned success when broken**: `Initialize()` returned `true` even when both socket and raw handle failed, causing the game to silently fall through to local `Console.Write` - remote users saw nothing. Now properly returns `false` so the game can fall back to local console mode with appropriate error messaging.
- **Verbose mode no longer blocks BBS connections**: Removed 8 intermediate "Press Enter to continue" pauses from `--verbose` debug output. These required the sysop to blindly press Enter multiple times, during which the telnet connection could time out (one sysop's connection died after 4 minutes of pauses). Now only pauses once at the very end of initialization so the sysop can read all diagnostics before the game starts.

### BBS Socket Mode Output Routing Fix
- **Game output now reaches remote BBS users in socket mode**: The entire game engine used `TerminalEmulator` which always wrote to `Console.Write`/`Console.ReadLine`. In socket mode (EleBBS, Mystic, etc.), `Console` output goes to the hidden local console window, not to the remote user over the socket. `BBSTerminalAdapter` had the correct socket routing through `SocketTerminal`, but was never wired into the game engine. Now `TerminalEmulator` detects BBS socket mode and delegates all output (`WriteLine`, `Write`, `ClearScreen`), input (`GetInput`, `GetKeyInput`), and color (`SetColor`) operations through `BBSTerminalAdapter`, which routes them over the socket to the remote user.

### SSH/Encrypted Transport Auto-Detection
- **Mystic BBS over SSH now works**: When a BBS handles SSH (or TLS) encryption, it passes the raw TCP socket handle in DOOR32.SYS. Writing directly to this socket bypasses the encryption layer, causing SSH clients to receive unencrypted bytes and report "Bad packet length" errors. The game now auto-detects when stdin/stdout are redirected (which all BBS software does for encrypted transports) and automatically switches to Standard I/O mode. This routes all game output through the BBS's transport layer, which handles encryption transparently. Works for Mystic, and any other BBS software that uses SSH or TLS.
- **SSH-safe socket initialization**: Socket verification no longer sends any bytes during initialization. Previously used IAC NOP (telnet no-op bytes), which would corrupt SSH streams. Now uses `Socket.Poll(SelectMode.SelectWrite)` for zero-byte verification. Telnet negotiation (WILL ECHO, WILL SGA) is only sent for confirmed TCP sockets, not for pipes or raw handles that might be SSH-wrapped.

### Telnet Keystroke Echo Fix
- **Keystrokes now visible in BBS socket mode**: The telnet negotiation handler was refusing all options, including ECHO and SGA (Suppress Go-Ahead). When the client asked "DO ECHO?", the game responded "WON'T ECHO", telling the client the server won't echo keystrokes. But the game actually does echo in its line-reading code, creating a mismatch that left users unable to see what they type. Now the game proactively sends "WILL ECHO" and "WILL SGA" after socket initialization, and correctly accepts these options during negotiation. This tells telnet clients to rely on server-side echo, which the game provides character-by-character during input.

### ANSI Color Fix for Socket Mode
- **Bright colors now display correctly over BBS sockets**: Colors like `bright_red`, `bright_green`, `bright_cyan`, etc. were rendering as gray/white on EleBBS and other socket-mode BBS connections. The ANSI color code lookup in `SocketTerminal.GetAnsiColorCode()` was stripping underscores from color names *before* the dictionary lookup, so `"bright_red"` became `"brightred"` which didn't match the dictionary key `"bright_red"`, falling through to the default white (code 37). Base colors without underscores (red, cyan, yellow) worked fine. Fixed by preserving underscores for the initial exact-match lookup.

### In-Game Bug Report System (BBS Compatible)
- **Bug reports now work on BBS connections**: The `!` bug report command previously tried to open a web browser to GitHub Issues, which doesn't work for remote BBS users. The system has been completely rewritten:
  - **Always saves locally**: Reports are saved to `bug_reports/` directory with timestamps for SysOp review
  - **Discord webhook**: Reports are automatically posted to the developer's Discord channel with full diagnostic info (version, platform, build type, BBS name, player stats, debug log excerpt)
  - **Browser fallback**: Non-BBS users (local/Steam) still get the browser-based GitHub Issues form as before
  - **Build type identification**: Reports clearly identify whether the game is running as Steam, BBS Door, or Standard build
  - **Cancel support**: Empty descriptions cancel the report without sending anything

### Wave Fragment Collection Fixes
- **Grief System resurrection attempts now grant wave fragments**: The 4th and 5th resurrection attempts had philosophical dialogue about letting go and love's permanence, but never actually called `CollectFragment()`. The 3rd attempt correctly granted `TheReturn`, but the 4th (river metaphor) and 5th+ (permanence of love) were missing. Now grants `TheForgetting` (4th attempt) and `TheChoice` (5th+ attempt).
- **Old God dialogue trees now grant wave fragments**: Three Old God dialogue paths that represented the deepest philosophical engagement now reward players with wave fragments:
  - Veloura (choosing to save her from corruption) grants `TheCorruption`
  - Thorgrim (breaking his logic with a justice paradox) grants `TheSevenDrops`
  - Noctura (forging the shadow alliance pact) grants `ManwesChoice`

### Files Changed
- `TerminalEmulator.cs` - Added BBS socket mode delegation: all I/O routes through BBSTerminalAdapter when CommType != Local
- `SocketTerminal.cs` - SSH-safe socket verification (Socket.Poll instead of IAC NOP); async raw handle fallback; proper failure return; telnet WILL ECHO + WILL SGA negotiation (TCP sockets only); fixed negotiation handler to accept ECHO and SGA options; fixed ANSI color code lookup (underscore stripping before dictionary match)
- `DoorMode.cs` - Auto-detect redirected I/O for SSH/encrypted transports; removed 8 blocking verbose pauses; kept single final pause; added Mystic BBS help section
- `BugReportSystem.cs` - Rewritten: saves locally to bug_reports/ + posts to Discord webhook + browser only for non-BBS users; added build type (Steam/BBS/Standard) identification
- `BBS_DOOR_SETUP.md` - Updated Mystic BBS status to fully tested (telnet + SSH); added SSH auto-detection docs; added "Bad packet length" troubleshooting
- `GriefSystem.cs` - Added CollectFragment calls for 4th and 5th resurrection attempts
- `DialogueSystem.cs` - Added CollectWaveFragment effects to Veloura, Thorgrim, and Noctura dialogue trees
