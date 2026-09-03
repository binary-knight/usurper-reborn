# Input-stomping harness

Scripted clients that reproduce "a message arrived while I was typing" against a
local MUD server, one per transport the game supports. Built for the v1.0.6 fix;
keep it green whenever `TerminalEmulator.ReadLineInteractiveAsync`,
`DeliverPendingMessagesWithRedraw`, `PlayerSession.ServerEchoes`, or the relay change.

## Setup

```
dotnet build usurper-reloaded.csproj -c Release
mkdir -p /tmp/usurper-harness && cd /tmp/usurper-harness
/path/to/bin/Release/net8.0/UsurperReborn --mud-server --mud-port 4999 --db ./test.db &
cd /path/to/repo/Tests/Harness/input-stomping
python3 auto.py alpha secret123 Alpha register    # creates account + character, quick start
python3 auto.py beta  secret123 Beta  register
```

`USURPER_PORT` (default 4999) and `USURPER_EXE` (default `../../bin/Release/net8.0/UsurperReborn`)
override the server port and the relay binary.

## Cases

Each case logs `alpha` in on the transport under test, logs `beta` in as a MUD
client, types `hello wor` on alpha without pressing Enter, has beta send
`/tell Alpha ping`, and prints the raw bytes alpha's screen received. Then it
presses Enter and prints what the server made of the input.

| Case | Command | Expected bytes when the tell lands |
|------|---------|------------------------------------|
| MUD client (Mudlet, TinTin++; answers DO ECHO) | `python3 stomp.py MUD` | `\r\x1b[2K` + message + real prompt line + `hello wor` |
| Web terminal (raw TCP, `X-Client:Web`, keystrokes streamed) | `python3 stomp.py WEB` | same as MUD; typing must echo (`bytes while typing` = `hello wor`) |
| SyncTerm / CP437 (TTYPE reply `SYNCTERM`, answered after the server's TTYPE SEND) | `python3 stomp.py SYNCTERM` | same as MUD, and the box art after Enter must be CP437 bytes (the case prints a check) |
| Desktop / Steam / BBS door client (AUTH on TCP, whole lines, local echo) | `python3 stomp.py DESKTOP` | `\r\n` + message + real prompt line, no typed text, no erase. Server side only: the real client's re-echo of its own buffer (OnlinePlaySystem) is hand-verified, not driven here |
| SSH through `--mud-relay` under a real PTY | `python3 relay_stomp.py` | same as MUD; PTY attrs must show ICANON and ECHO off |

`utf8_name.py` is a separate case for multi-byte names (v1.1.1, when the MUD line
reader started decoding UTF-8 itself): `python3 utf8_name.py jose José MUD register`
creates the account, then `WEB` and `SSH` log the same character in over the other
transports. Each run checks the name in `/who`, a tell addressed by the accented
name, a tell by username, and a `/say`, and prints True for each.

`hardclose.py town|fight` drops the socket at a prompt without logging out and
checks the server log for "Connection lost" and "Session ended" with no CRASH line,
then that server CPU is back at zero (v1.1.1: a closed peer used to read as empty
lines forever and spin every re-prompt loop).

Pass criteria for every case: the message is on its own line, the prompt that was
on screen before the message is redrawn exactly, and the typed text appears at most
once (exactly once on echo transports, zero times on the desktop path, where the
client re-shows its own buffer).

Two more cases were run by hand for v1.0.6 and are worth repeating when the relay
or the desktop client changes: an SSH terminal through a real sshd ForceCommand
relay, and the real desktop binary through the same SSH gateway. A throwaway
Docker image (dotnet runtime + openssh-server, `usurper:play`, ForceCommand
`--mud-relay --mud-port 4999`, MUD server in the same container) is enough; drive
the desktop binary under a PTY, choose Online, custom server, host 127.0.0.1,
port 4000.

The real web page can be driven too: run `web/ssh-proxy.js` with
`MUD_MODE=1 MUD_PORT=4999 DB_PATH=/tmp/usurper-harness/test.db`, add your origin to
`ALLOWED_ORIGINS`, and repeat the WEB case by hand in a browser.

Not covered: a spectator typing while the spectated player's screen renders, and a
browser resize mid-typing (the proxy now drops the resize control message; verify
by resizing the window and checking nothing appears in the input line).
