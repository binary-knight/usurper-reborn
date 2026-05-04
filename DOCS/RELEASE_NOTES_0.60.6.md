# v0.60.6 -- Beta

Hotfix release on top of v0.60.5. One critical security fix (account impersonation via the trusted-AUTH path), the BBS Online Play migration that fix required, a quality-of-life addition (`[O] Online Play` server picker so players can connect to friends' servers), and a Steam + BBS launch UX fix for sighted players who were getting flipped into accessible mode against their will.

---

## SECURITY: trusted-auth account impersonation via direct TCP

Reporter found a critical authentication bypass against the MUD server's protocol-mode AUTH handler. Reproduction: telnet to the public game port and immediately send `AUTH:targetUsername:Web` (or any connection-type string). The 3-part AUTH form `AUTH:user:type` was the trusted-auth path -- intended for the local SSH relay (`sshd-usurper` -> `--mud-relay` -> `127.0.0.1:4001`) and the local BBS gateway -- and skipped password verification entirely. Anyone could log in as any account on the public server, with no auth attempt logged as a failure (the server treated it as a successful trusted login).

The 4-part form `AUTH:user:password:type` and the interactive login menu were unaffected -- those always run real password verification.

Root cause: the trusted-auth design assumed the only connections without a password would come from local relays. There was no enforcement of that assumption -- the gate was implicit ("anyone hitting this port without a password must be trusted") rather than explicit.

### Fix

`MudServer.HandleConnectionAsync` now captures the raw TCP socket peer separately from the public-facing `effectiveIp` (which honors the `X-IP` forwarded header for ban-tracking). The TCP peer cannot be spoofed by external clients -- only the actual socket source determines whether `IPAddress.IsLoopback` returns true. New gate after AUTH parsing, before password verification:

```
if (password == null && !isLoopbackPeer) {
    log SECURITY rejection with peer IP and claimed username
    return ERR:Authentication required. Trusted auth is only available from local relays.
}
```

Legitimate flows that still work:
- SSH relay path (sshd-usurper from localhost) -- loopback peer, gate passes.
- Web proxy (`ssh-proxy.js` on the same host) -- loopback peer, gate passes.
- Local BBS Online Play connecting to localhost:4001 -- loopback peer, gate passes.
- Desktop / Steam client with password (the 4-part AUTH form) -- password != null, gate is skipped, real password verification runs.
- Interactive login menu (raw telnet without an AUTH header) -- always password-verified, unaffected.

Every rejected attempt is logged with the peer IP and the impersonated username so post-incident audit can identify probes:

```
[MUD] SECURITY: rejected trusted-auth from non-loopback peer 203.0.113.42 (claimed user='rage', type='Web')
```

Files: `Scripts/Server/MudServer.cs`.

---

## BBS Online Play migrated to password auth

The `[O] Online Play` menu in BBS door mode used to send the no-password trusted form (the same vector exploited above). It now goes through the same login/register flow as desktop and Steam clients: the user picks Login or Register, types a username and password, and the server runs real password verification.

The transport stays direct TCP for BBS (a door environment can't host an SSH client), but the AUTH wire format is the standard 4-part `AUTH:user:password:type`. Convenience touches:

- The username prompt is pre-filled with the BBS handle from the drop file -- the user can press Enter to accept it as their game-server username, or type a different one.
- Registration runs against the public server's normal flow, including the per-IP rate limit (3 new accounts per IP per 24h) added in v0.60.5.
- Saved-credential auto-login is intentionally disabled in BBS door mode -- on a multi-user BBS box, the credentials file is per-install, not per-user, so caching a login would let the next door user inherit the previous one's account. Each BBS user logs in fresh.

What this does NOT change (the common BBS deployment):
- BBSes running with the `--online` flag, which gives the BBS its own local SQLite-backed shared world for the players ON that BBS. Those players are already inside the local online world; no remote AUTH involved.
- BBSes running in single-player file-save mode (no `--online`).
- Any sysop running both their BBS door AND a private MUD server on the same box, since the BBS-to-MUD connection is over loopback.
- The public game server's normal player paths: SSH gateway, web terminal, desktop/Steam (password auth), Mudlet/MUSHclient (password auth or interactive login), and the in-game admin tools.

**Sysop action required:** BBS sysops on v0.60.5 or earlier whose users hit `[O] Online Play` will see the new server-side rejection until they upgrade their BBS door binary to v0.60.6+ (win-x86 build). The new binary's `[O] Online Play` menu prompts for username + password instead of the old trusted passthrough.

Files: `Scripts/Systems/OnlinePlaySystem.cs`.

---

## Online Play server picker

The `[O] Online Play` menu used to drop the player straight onto the official server. Now it shows a server picker first:

```
  Choose a server:

  [1] Official server  (play.usurper-reborn.net:4000)
  [2] Connect to a different server (enter host and port)
  [B] Back to Main Menu
```

`[1]` is one keystroke for the common case. `[2]` prompts for hostname then port (defaults to 4000 if blank; validates 1-65535). After picking, the existing auth + connection flow runs unchanged.

This is groundwork for community-run servers. The minimum viable shape: players can already paste their friend's hostname and play together, no central registry needed. A full P2P / phone-home server discovery system was scoped and deferred -- the picker is the immediate win that doesn't require any backend infrastructure or moderation work.

Six new localization keys per language (`online.choose_server`, `online.server_official`, `online.server_custom`, `online.custom_host_prompt`, `online.custom_port_prompt`, `online.invalid_port`) translated for en/es/fr/hu/it.

Files: `Scripts/Systems/OnlinePlaySystem.cs`, `Localization/{en,es,fr,hu,it}.json`.

---

## Screen reader auto-detect false positives (Steam + BBS)

Two reports of *"Screen reader detected. Accessible mode enabled automatically."* showing on launch with no SR actually running:

- Sighted Steam player on a clean v0.60.4 launch via the default `Play.bat` (WezTerm wrapper).
- BBS player connecting through Synchronet to a sysop's BBS hosting Usurper Reborn as a door, getting bumped into accessible mode before the title screen even rendered.

Root cause (same for both): `AccessibilityDetection.IsScreenReaderActive` calls `SystemParametersInfo(SPI_GETSCREENREADER, ...)` -- the documented Microsoft API that NVDA/JAWS/Narrator set while running. The flag is stickier than the docs imply: browsers, accessibility tools, and apps that crashed without clearing it can leave `SPI_SCREENREADER = TRUE` set on the desktop session. `Console/Bootstrap/Program.cs` was firing the auto-detect on every non-headless launch -- on the BBS path, that meant the *sysop's* Windows session flag was being read and applied to every player who connected through the door.

The BBS case in particular has nothing to do with the actual player. They're on a remote terminal at the other end of a telnet/SSH session; whatever flags are stuck on the BBS host's Windows desktop are completely unrelated to whether they have a screen reader running. The previous gate `(IsInDoorMode && CommType != Local)` failed to skip Synchronet stdio mode because that mode reports `CommType = Local` (it's local from the door's perspective even though the user is remote).

### Fix

Two-layer:

1. **Skip the auto-detect for any door mode** (`useDoorMode = true`). Door modes always run on a host machine separate from the player, so reading the host's Windows flag is meaningless. Door players who need accessible mode have three working paths: (a) MUD-client TTYPE negotiation, (b) the in-game preferences toggle (saved per-character), or (c) launching via `Play-Accessible.bat` which passes `--screen-reader` explicitly.
2. **Skip the auto-detect when running inside WezTerm** (`TERM_PROGRAM == "WezTerm"`, set by WezTerm for all child processes). The default Steam launcher (`Play.bat`) wraps WezTerm; SR users on Steam use `Play-Accessible.bat`. The auto-detect still runs for the standard console branch (non-WezTerm desktop console / direct `.exe` double-click) where it's the only path a SR user has to discover the mode without command-line flags.

macOS and Linux unaffected -- the underlying Win32 API returns false on those platforms anyway, so the auto-detect was already a no-op there.

Files: `Console/Bootstrap/Program.cs`.

---

## Files Changed

### No SQLite schema changes
This release is code-only. v0.60.5 schema (banned_ips, last_login_ip, created_ip) carries forward unchanged.

### Modified files (game)
- `Console/Bootstrap/Program.cs` -- skip screen reader auto-detect for any door mode (`useDoorMode = true`); skip when `TERM_PROGRAM == "WezTerm"`.
- `Scripts/Core/GameConfig.cs` -- version bump to 0.60.6.
- `Scripts/Server/MudServer.cs` -- raw TCP peer captured separately from `effectiveIp`; loopback gate on trusted-AUTH; SECURITY rejection logged with peer IP + claimed username.
- `Scripts/Systems/OnlinePlaySystem.cs` -- BBS path uses unified login/register flow over TCP; BBS username pre-fill; saved-credential auto-load skipped in BBS mode. `StartOnlinePlay` rewritten as a server picker (Official / Custom / Back) with hostname + port prompts and 1-65535 validation.

### Modified files (localization)
- `Localization/{en,es,fr,hu,it}.json` -- 6 server-picker keys added per language (`online.choose_server`, `online.server_official`, `online.server_custom`, `online.custom_host_prompt`, `online.custom_port_prompt`, `online.invalid_port`).

---

## Deploy notes

- Standard recipe: publish linux-x64, tar, scp, restart `usurper-mud` and `sshd-usurper`. Web service unchanged. No SQLite migration.
- BBS sysops should pull the new win-x86 build to upgrade their `[O] Online Play` flow. BBSes running `--online` only (no remote Online Play menu in use) need no upgrade.

---

## Credits

- Reporter who diagnosed the trusted-AUTH bypass and submitted the reproduction privately rather than publicly. Thanks for the responsible disclosure.
