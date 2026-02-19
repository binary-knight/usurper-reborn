# v0.42.7 - BBS Disconnect Detection

Fixes a CPU spike caused by BBS users disconnecting or being kicked for inactivity.

---

## Bug Fixes

### BBS Process Hangs at 100% CPU After User Disconnect
When a BBS user disconnected (dropped carrier, kicked for inactivity, or closed their terminal), the UsurperReborn.exe process continued running in a tight loop at 100% CPU. The game was receiving empty reads from the dead connection and spinning through the game loop indefinitely. The idle timeout never fired because the timer was being reset on every read — even empty ones from dead connections.

**Fix**: Added multi-layer disconnect detection:
- **Socket layer**: Detects TCP connection closed (0-byte reads) and broken pipe (write exceptions)
- **Stdio layer**: Detects stdin EOF (`Read()` returns -1, `ReadLine()` returns null)
- **Input layer**: Only resets the idle timer on real (non-empty) input, so the existing idle timeout now works as a fallback
- **Safety net**: After 5 consecutive empty reads, the connection is flagged as dead

When a disconnect is detected, the game auto-saves the player and exits cleanly.

---

## Files Changed

| File | Changes |
|------|---------|
| `Scripts/Core/GameConfig.cs` | Version 0.42.7 |
| `Scripts/BBS/DoorMode.cs` | Added `IsDisconnected` flag (ThreadStatic for MUD mode) |
| `Scripts/BBS/SocketTerminal.cs` | Set `IsDisconnected` on 0-byte reads and write exceptions across all I/O paths |
| `Scripts/UI/TerminalEmulator.cs` | Disconnect check in `GetInput()`, consecutive empty input counter, idle timer only resets on non-empty input, stdin EOF detection in `ReadLineWithBackspace()` |
