# Usurper Reborn v0.48.4 — Online Session Fixes

## Deferred Online Presence

Players no longer appear as "online" while sitting at the main menu or character selection screen. Online presence registration and the "has entered the realm" login broadcast are now deferred until the player actually loads their character and enters the game world. This also applies to new character creation — the announcement fires after the character is created, not when the connection is first established.

## Stale Session Cleanup on Exit

Exiting the game now performs online session cleanup synchronously before the process terminates. Previously, the cleanup relied on an async `finally` block that could be skipped when `Environment.Exit()` killed the process. This fixes the issue where BBS door users (especially on Mystic BBS) would see "Disconnecting previous session" on relaunch because the old session record was never cleared.

## Idle Timeout on Any Keystroke

The idle timeout tracker now resets on any input activity (typing characters, backspace) — not just pressing Enter. Previously a player could type a long message and still be kicked for inactivity because only submitting a line reset the timer. Keystroke updates are throttled to once per 5 seconds to avoid overhead during fast typing.

---

## Files Changed

- `GameConfig.cs` — Version 0.48.4
- `Console/Bootstrap/Program.cs` — Removed early `StartOnlineTracking()` call; saves `DeferredConnectionType` for later use
- `Scripts/Server/PlayerSession.cs` — Removed early `StartOnlineTracking()` and login broadcast; defers to after character load
- `Scripts/Systems/OnlineStateManager.cs` — Added `DeferredConnectionType` property to hold connection type between auth and character load
- `Scripts/Core/GameEngine.cs` — `LoadSaveByFileName()`: fires deferred `StartOnlineTracking()` and login broadcast after `IsInGame = true`; `CreateNewGame()`: same deferred presence for new characters; `QuitGame()`: synchronous online session cleanup before `Environment.Exit()`
- `Scripts/UI/TerminalEmulator.cs` — `UpdateMudIdleTimeout()` now called on printable characters and backspace with 5-second throttle; Enter/newline uses `force: true` for immediate update
