# Usurper Reborn v0.47.1 — MUD Streaming Mode, Client Support & UI Polish

## MUD Streaming Mode (Online Play)

The online server now behaves like a real MUD instead of a BBS door game. Previously, every action wiped the terminal and redrawn the full menu — chat messages from other players got erased, scroll history was lost, and the experience felt like flipping pages rather than inhabiting a living world.

**What changed:**
- Screen no longer clears between actions in online MUD mode. Output flows continuously downward and stays in your terminal's scroll buffer
- Location banners print once when you enter a location, then stay in your history
- Actions, combat, NPC interactions, and chat all accumulate naturally in the scroll buffer
- Chat messages from other players (`/say`, `/shout`, `/tell`) now appear inline between your actions instead of being wiped on the next screen redraw
- Prompt changed from `Your choice: ` to a location-aware `Inn > ` / `Dungeon Fl.5 > ` / `Main Street > ` style
- Type `look` or `l` at any time to reprint the current location's banner (standard MUD convention)
- BBS mode and local/single-player mode are **completely unaffected** — they continue to use full-screen redraws as before

## MUD Client Connections (Raw TCP / Telnet)

Players can now connect using any standard MUD client (Mudlet, TinTin++, BlowTorch, MUSHclient, etc.) in addition to SSH and the browser terminal.

**Connection settings:**
- Host: `usurper-reborn.net`
- Port: `4000`
- Protocol: Telnet or Raw TCP (not SSH)

SSH connections (`ssh usurper@play.usurper-reborn.net -p 4000`) continue to work unchanged. Both protocols are multiplexed on port 4000 via `sslh` — SSH traffic is routed to the SSH gateway, everything else goes directly to the game server.

**Technical details:**
- `sslh` protocol multiplexer on port 4000: SSH → internal port 4022, raw TCP → game server port 4001
- AUTH header detection timeout reduced 3 s → 0.5 s for faster MUD client login screen
- Telnet IAC byte sequences (0xFF option negotiation) filtered and discarded so MUD client handshakes don't corrupt input
- U+FFFD replacement characters (produced by IAC bytes passing through the stream reader) silently discarded

## MUD Client Echo Fixes

Several echo-related bugs caused doubled characters or doubled prompts in MUD clients and the browser terminal. All three are now resolved.

### IAC WILL ECHO — Client Local Echo Suppression

When a raw TCP MUD client connects (Mudlet, TinTin++, VIP Mud, etc.), the server now immediately sends `IAC WILL ECHO` + `IAC WILL SGA` (standard telnet option negotiation). This tells the client "the server will handle echo — stop echoing locally." Without this, clients echoed each keystroke locally while the server also echoed it back, causing every character to appear twice.

### ServerEchoes — Web and SSH Relay Echo Isolation

SSH and web terminal connections route through a relay (the ssh-proxy Node.js process) that uses a PTY (pseudo-terminal). The PTY automatically echoes all keystrokes. Previously the server's input handler *also* echoed those characters back — resulting in double echo in the browser terminal and for SSH users.

The server now tracks whether it should echo per connection type. Direct raw TCP connections (`ServerEchoes = true`) get server-side echo because they have no other echo source. Relay connections via SSH or the web proxy (`ServerEchoes = false`) suppress server echo and let the PTY handle it.

### \r\n Double-Enter Fix

Mudlet (and some other MUD clients) sends `\r\n` for the Enter key rather than just `\r`. The server was treating both `\r` and `\n` as line terminators — so each Enter press generated two inputs, causing the prompt to appear twice with an empty line between them.

Fixed with a `_prevInputWasCR` flag: when `\n` arrives immediately after `\r`, it is discarded as the second half of a CRLF pair rather than treated as a second Enter.

## VIP Mud / Screen Reader Support

Players using VIP Mud (a screen-reader-optimized MUD client for blind/low-vision users) or any terminal that reports itself as `DUMB` or `UNKNOWN` now receive a plain text connection with all ANSI art and box-drawing stripped.

**What plain text mode does:**
- ANSI color escape sequences removed from all output
- `[colorname]...[/]` markup tags stripped
- Box-drawing characters (`╔═╗║╚╝╠╣╦╩╬`) replaced with plain ASCII equivalents (`+`, `-`, `|`)
- Screen-clear sequences replaced with a plain `---` separator line
- Auth/login screen shown as plain ASCII text instead of the ANSI banner

**How detection works:**
After the initial telnet handshake, the server sends `IAC DO TTYPE` + `IAC SB TTYPE SEND` (standard terminal type negotiation). If the client responds with a terminal type starting with `VIP` or equal to `DUMB`/`UNKNOWN`, plain text mode activates automatically. Most graphical MUD clients (Mudlet, TinTin++) respond with `XTERM-256COLOR` or similar and receive full color output as normal.

## Connection Type Display Fix

The `/who` command and the website's Who's Online list were showing `Via ?` for players connected via raw TCP MUD clients instead of `Via MUD`. The connection type string `"MUD"` was missing from the formatter. Fixed — raw TCP clients now correctly show as `MUD`.

## UI: Box-Drawing Alignment Fixes

65+ title lines across 21 location and system files had misaligned right-side borders. Title strings were padded with hardcoded spaces that didn't account for title length, causing the right `║` to land 1–4 characters off from the box edge. All affected boxes now use calculated centering:

```csharp
int left  = (78 - title.Length) / 2;
int right = 78 - title.Length - left;
```

Several boxes also had their border `║` characters rendered in a different color from the top/bottom `╔═╗` lines — fixed so the entire box is one consistent color.

## Code Cleanup — Godot Engine Removed

All legacy Godot game engine scaffolding has been removed from the codebase. The project was originally prototyped in Godot 4.4 before being rewritten as a pure .NET 8.0 console application, but the Godot stub files remained as compatibility shims.

**Removed:**
- `project.godot`, `Main.tscn`, `UsurperReborn.csproj.godot` — Godot project files
- `.godot/` — Engine cache directory
- `Scripts/Utils/GodotStubs.cs` — Stub implementations of Godot types (Node, Control, GD, etc.)
- `Scripts/Utils/GodotHelpers.cs` — `GetNode<T>()` replacement helper
- `using Godot;` removed from 110 source files
- `: Node` / `: Control` base class inheritance removed from 10 classes
- `GD.RandRange()`, `GD.Randf()`, `GD.Randi()` replaced with `Random.Shared` equivalents
- `GD.Print()` / `GD.PrintErr()` debug calls removed

No gameplay changes — purely internal cleanup.

## Website Fix — Prestige Class Names

The website stats dashboard and leaderboard were showing `Unknown` for players playing prestige classes (Tidesworn, Wavecaller, Cyclebreaker, Abysswarden, Voidreaver). The class ID mapping in the web proxy was missing entries 11–15. Fixed.

## Files Changed

- `GameConfig.cs` — Version 0.47.1
- `Scripts/UI/TerminalEmulator.cs` — `IsPlainText` property; `ToPlainText()` static helper (strips ANSI codes, markup tags, box-drawing); `ServerEchoes` property; `_prevInputWasCR` flag; plain text paths in `WriteLineToStream()`, `WriteToStream()`, `WriteRawAnsi()`, `ClearScreen()`; `SetColor()` skipped when `IsPlainText`; `ReadLineInteractiveAsync()` split `\r`/`\n` handling with `_prevInputWasCR` fix, all echo gated on `ServerEchoes`; `ClearScreen()` returns early in MUD server mode; CP437 infrastructure (`UseCp437` property, `Cp437Encoding` lazy field, `PreTranslateCp437()`, `WriteCp437ToStream()`) — present but not currently active
- `Scripts/Server/MudServer.cs` — AUTH header detection timeout 3 s → 0.5 s; IAC byte filtering in `ReadLineAsync()`; U+FFFD filtering in `ReadLineInteractiveAsync()`; `IAC WILL ECHO` + `IAC WILL SGA` sent on interactive connection; `ProbeTtypeAsync()` — sends `IAC DO TTYPE` + `IAC SB TTYPE SEND`, reads response, returns true for VIP Mud/DUMB/UNKNOWN clients; `isPlainText` flag propagated to `InteractiveAuthAsync()` and `PlayerSession`; plain ASCII auth menu for screen reader clients; interactive connection type `"MUD"`
- `Scripts/Server/PlayerSession.cs` — `isPlainText` constructor parameter; sets `ctx.Terminal.IsPlainText` and `ctx.Terminal.ServerEchoes = (ConnectionType == "MUD")` in `RunAsync()`
- `Scripts/Systems/OnlineChatSystem.cs` — Added `"MUD" => "MUD"` to `FormatConnectionType()` so raw TCP MUD connections show correctly in `/who` and on the website
- `Scripts/Systems/OnlineAuthScreen.cs` — Banner box centering fixed; single-color border
- `Scripts/Core/GameEngine.cs` — "While You Were Gone" box centering and border color fixed
- `Scripts/Locations/BaseLocation.cs` — "A Mysterious Encounter" box centering fixed; `_locationEntryDisplayed` flag; `GetUserChoice()` compact MUD prompt; `GetMudPromptName()` virtual method; `look`/`l` global command
- `Scripts/Locations/MainStreetLocation.cs` — `GetMudPromptName()` → "Main Street"
- `Scripts/Locations/DungeonLocation.cs` — `GetMudPromptName()` → "Dungeon Fl.N"
- `Scripts/Locations/InnLocation.cs` — Box alignment fixes
- `Scripts/Locations/TeamCornerLocation.cs` — Title and subtitle centering fixed
- `Scripts/Locations/AnchorRoadLocation.cs` — Box alignment fixes; `GetMudPromptName()` → "Anchor Road"
- `Scripts/Locations/CastleLocation.cs` — Box alignment fixes
- `Scripts/Locations/ChurchLocation.cs` — Box alignment fixes
- `Scripts/Locations/DarkAlleyLocation.cs` — Box alignment fixes; `GetMudPromptName()` → "Dark Alley"
- `Scripts/Locations/DevMenuLocation.cs` — Box alignment fixes
- `Scripts/Locations/HomeLocation.cs` — Box alignment fixes
- `Scripts/Locations/LevelMasterLocation.cs` — Box alignment fixes; `GetMudPromptName()` → "Level Master"
- `Scripts/Locations/MagicShopLocation.cs` — Box alignment fixes; `GetMudPromptName()` → "Magic Shop"
- `Scripts/Locations/SysOpLocation.cs` — Box alignment fixes
- `Scripts/Locations/TempleLocation.cs` — Box alignment fixes
- `Scripts/Locations/WeaponShopLocation.cs` — `GetMudPromptName()` → "Weapon Shop"
- `Scripts/Locations/ArmorShopLocation.cs` — `GetMudPromptName()` → "Armor Shop"
- `Scripts/Locations/LoveStreetLocation.cs` — `GetMudPromptName()` → "Love Street"
- `Scripts/Systems/WizardCommandSystem.cs` — Box alignment fixes
- `Scripts/Systems/IntimacySystem.cs` — Box alignment fixes
- `Scripts/Systems/OnlinePlaySystem.cs` — Box alignment fixes
- `Scripts/Systems/SysOpConsoleManager.cs` — Box alignment fixes
- `Scripts/Systems/VisualNovelDialogueSystem.cs` — Box alignment fixes
- `Scripts/Utils/GodotStubs.cs` — Deleted
- `Scripts/Utils/GodotHelpers.cs` — Deleted
- `web/ssh-proxy.js` — Added prestige class names (IDs 11–15) to CLASS_NAMES mapping
- `web/index.html` — Steam launch date updated to "Sometime in March, 2026"
- 110 additional .cs files — `using Godot;` removed; `GD.*` calls replaced
- **Server config:** `/etc/sslh.cfg` — sslh installed; `on-timeout: "anyprot"`; timeout 1 s; SSH → 127.0.0.1:4022, anyprot → 127.0.0.1:4001
- **Server config:** `/etc/ssh/sshd_config_usurper` — Port 4000 → 4022; ListenAddress hardened to 127.0.0.1
