# v0.44.3 - Spectator Mode

Adds Spectator Mode for online play (MUD only) — watch another player's session in real-time, perfect for Twitch streaming and learning the game.

---

## New Features

### Spectator Mode (Online MUD Mode ONLY)
Select **[S] Spectate a Player** from the main menu to enter Spectator Mode. You'll see a list of online players and can request to watch anyone currently in-game. The target player must accept your request (via `/accept`) before spectating begins — privacy is respected.

Once accepted, you see everything the watched player sees in real-time: combat, exploration, menus, dialogue — their full terminal output streams directly to your screen. Type **Q** to stop watching at any time.

**For the watched player:**
- You're notified when someone starts and stops watching
- `/spectators` — see who's currently watching you
- `/nospec` — kick all spectators immediately
- `/deny` — reject a pending spectate request

Multiple spectators can watch the same player simultaneously. Spectators are exempt from the idle timeout since they're passively watching.

---

## Bug Fixes

### Equipment and Progress Lost on Disconnect (Online Mode)
Players could lose recently acquired equipment and other progress when disconnecting from the online server. The emergency save on disconnect was writing to a temporary key (`emergency_*`) that was never loaded on the next login. Combined with a 60-second autosave throttle, any changes made in the last minute before a disconnect were silently lost. Emergency saves now write directly to the player's main save key, ensuring all progress is preserved even on unexpected disconnects (browser close, SSH timeout, network drop).

### Forced Save on Quit via LocationManager
Quitting the game through certain exit paths (such as LocationExitException with NoWhere destination) could skip the save step entirely. A forced save now runs before the quit completes, ensuring no progress is lost regardless of which exit path the player takes.

---

## Files Changed

| File | Changes |
|------|---------|
| `Scripts/Core/GameConfig.cs` | Version 0.44.3 |
| `Scripts/Core/GameEngine.cs` | Added `[S]pectate` menu option to both branches of RunBBSDoorMode(); added `RunSpectatorModeUI()` and `RunSpectatorLoop()` methods |
| `Scripts/UI/TerminalEmulator.cs` | Added spectator stream forwarding: `_spectatorStreams` list, `AddSpectatorStream()`, `RemoveSpectatorStream()`, `ForwardToSpectators()`, `RenderMarkupToStringBuilder()`; modified `WriteLineToStream`, `WriteToStream`, `WriteRawAnsi`, `ClearScreen` to duplicate output to spectators |
| `Scripts/Server/PlayerSession.cs` | Added `Spectators`, `SpectatingSession`, `IsSpectating`, `PendingSpectateRequest` fields; added `SpectateRequest` class; added spectator cleanup in disconnect handler; emergency save now writes to main player key |
| `Scripts/Server/MudChatSystem.cs` | Added `/accept`, `/deny`, `/spectators`, `/nospec` commands for spectator consent and management |
| `Scripts/Server/MudServer.cs` | Skip spectating sessions in `BroadcastToAll()`; exempt spectators from idle timeout |
| `Scripts/Systems/LocationManager.cs` | Added forced save on NoWhere/quit path before marking intentional exit |
