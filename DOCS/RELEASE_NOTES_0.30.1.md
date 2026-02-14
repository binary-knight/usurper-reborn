# Usurper Reborn - v0.30.1 Hotfix

Fixes a critical bug where pressing Backspace while connected to the online server through the game executable caused garbage output to flood the screen.

---

## Bug Fixes

- **Backspace causes garbage output in game client (Online Play)**: Pressing Backspace while connected to the online server through the game executable caused hundreds of bytes of corrupted data to appear on screen, requiring a disconnect. Root cause: raw backspace bytes (0x7F) sent through the SSH PTY triggered echo responses that the client's ANSI parser couldn't handle. Fixed by disabling PTY echo and handling all line editing (backspace, character echo) locally on the client. Only complete lines are now sent to the server. Web terminal and direct SSH connections were unaffected.

---

## Files Changed

| File | Changes |
|------|---------|
| `Scripts/Core/GameConfig.cs` | Version bump to 0.30.1 |
| `Scripts/Systems/OnlinePlaySystem.cs` | Disabled PTY echo via SSH terminal modes; replaced per-keystroke sending with local line buffering and backspace handling |
