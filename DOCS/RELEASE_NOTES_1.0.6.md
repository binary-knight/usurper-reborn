# Usurper Reborn v1.0.6

One fix: a chat, tell, or world message arriving while you were typing no
longer destroys what you typed. This has been reported since the online server
opened and every earlier attempt fixed one connection type by breaking another.
This time each connection type was measured with a scripted client before any
code changed, and the reproduction harness ships with the game.

## What was actually happening

There was no single bug. Four connection paths failed in four different ways.

**Web terminal.** The server only echoed keystrokes for MUD clients, and the
browser terminal never echoes on its own. Web players have been typing blind
in game since the site moved to the direct TCP proxy: the text you typed was
invisible until a message happened to trigger a redraw, and every broadcast
erased the prompt line and left an empty one. Verified in a browser against the
real page. Login prompts did echo, which is why it was not obvious on first
contact.

**SSH.** The relay that sshd runs read its terminal through .NET's line-mode
console reader, which holds each line locally, echoes it itself, and hands it
over on Enter. The server never saw the text, so when it erased the line to
print a message it wiped text the relay still held. Enter then submitted the
invisible line and the player retyped, getting a doubled command.

**MUD clients (Mudlet, TinTin++, telnet).** Echo and redraw worked, but the
redraw printed the prompt argument the code had passed to the input call,
usually nothing or "Your choice:", not the "[27hp 100st] Main Street >" line
the game had written separately. After every message the visible prompt was
wrong or gone.

**Desktop, Steam, and BBS door clients.** These keep their own line editor and
show the text themselves. The server cannot see it, so erasing the line was
always wrong for them.

## What changed

- The terminal now remembers the text on the current output line (colours
  kept, cursor movement dropped) and redraws exactly that after a message.
- The server owns the input line, echoing keystrokes and redrawing after a
  message, for MUD clients, the web terminal, and SSH. For SSH the relay puts
  its terminal in raw mode (termios via P/Invoke; a spawned `stty` would be
  undone by the .NET runtime), forwards keystrokes one at a time, and asks the
  server to echo with `SSH;echo=1` in the existing AUTH connection-type field.
  An older server ignores the suffix, so relay and server can be upgraded in
  either order.
- For clients that keep their own line, the server never erases it. It moves
  to a fresh line, prints the messages, and redraws the prompt. The desktop
  client re-shows its half-typed text once a prompt is back on screen.
- Screen-reader sessions never receive erase sequences. CP437 clients get the
  redraw through the same translation as everything else. Spectators see it.
- The web proxy now drops the browser's window-resize control message, which
  was being typed into the player's input buffer.

## Terminal-type detection was being thrown away

Found while making the SyncTerm harness case pass. The telnet probe that asks a
client for its terminal type gives it 250 ms to answer. When that window closed,
the cancellation escaped past the block that reads the answer, so a reply that
had already arrived was discarded. CP437 terminals (SyncTerm, NetRunner,
mTelnet, fTelnet) were therefore being sent UTF-8 box glyphs, and screen-reader
clients that identify as VIP, DUMB, or UNKNOWN never got plain-text mode. This
has been the case since v0.47.1. GMCP and echo negotiation were not affected.
Both detections now take effect for the first time, so watch the
`[MUD] TTYPE detected:` server log line on deploy day for client strings that
should not be treated as plain text. This is the likely cause of issue #102
(garbled box art on a Mystic BBS terminal); the reporter should retest on 1.0.6
before it is closed.

## Not changed

- The 500 ms typing grace stays; it only delays a message while a key was
  just pressed.
- Only one row is erased on redraw. The server does not know the terminal
  width, so a wrapped input line still leaves its earlier rows behind.
- BBS door players see their partial text again only after pressing Enter.
- Electron online play is unchanged. It streams keystrokes without local echo
  on the raw path and may have the same blind-typing problem the web had.
- Web spectators now see their own keystroke echo inside the mirrored stream,
  as MUD spectators already did.

## Tests

977 passing, up from 958. New: `OutputLineTrackerTests` (line tracker, the
tracking writer, AUTH parameter parsing).

The reproduction harness is in `Tests/Harness/input-stomping` with a README.
It runs a local server and scripted clients for the MUD, web, SyncTerm,
desktop, and SSH-under-a-PTY cases, types mid-line, sends a tell from a second
account, and prints the bytes the first player's screen received. All five
cases are green on this release: message on its own line, exact prompt restored,
typed text present exactly once (zero on the scripted desktop case, which
measures the server side only). Two further cases were run by hand against a
local sshd gateway container: an SSH terminal through the real ForceCommand
relay, and the actual desktop binary through the SSH gateway, which re-showed
its half-typed text once and submitted the full line on Enter.

## Files Changed

**New**

- `DOCS/RELEASE_NOTES_1.0.6.md`
- `DOCS/STEAM_RELEASE_NOTES_1.0.6.txt`
- `Scripts/Server/TerminalRawMode.cs`
- `Scripts/UI/OutputLineTracker.cs`
- `Tests/Harness/input-stomping/` (README, five scripts)
- `Tests/OutputLineTrackerTests.cs`

**Modified**

- `.gitignore`
- `DOCS/ARCHITECTURE.md`
- `README.md`
- `Scripts/Core/GameConfig.cs`
- `Scripts/Server/MudServer.cs`
- `Scripts/Server/PlayerSession.cs`
- `Scripts/Server/RelayClient.cs`
- `Scripts/Systems/OnlinePlaySystem.cs`
- `Scripts/UI/TerminalEmulator.cs`
- `web/ssh-proxy.js`
