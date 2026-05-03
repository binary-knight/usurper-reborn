# v0.60.5 -- Beta

Substantial release on top of v0.60.4. One contributor PR fixing an XP regression I shipped, plus a bot-detection / snoop fix, plus a complete rewrite of the ban system from "account name only" to "account + IP + CIDR + rate-limit + active-session kick + permadeath world-state purge." The ban work was driven by a player report that revealed a permadeath leak (guild membership and god worship survived account erasure), which expanded into a full audit of how identity-keyed state cascades when a character ends.

---

## XP threshold desync at Lv.51+ (PR #98)

Root cause: when the late-game XP curve was tuned in v0.60.4 (levels 51+ now use exponent `2.25` instead of `2.0`), only `LevelMasterLocation.GetExperienceForLevel` was updated. The same function had been duplicated **12 times** across the codebase and 11 of those copies stayed at the flat `^2.0` exponent.

Visible desync for any player past level 50:
- The Trainer (correct formula) showed Coosh needed ~8.6M XP to reach Lv.74
- The Status window, Dungeon eligibility check, and every NPC/companion XP calculation (stale formulas) computed the threshold as ~6.9M
- The XP bar displayed negative progress for high-level characters
- The auto-level-up check in `BaseLocation.LocationLoop` was using one of the stale copies, so it never fired even when the player had crossed the real threshold

`GameConfig.GetSessionXPThreshold` had the same hardcoded `^2.0` inline and was also stale.

### Fix (PR #98 by Coosh)

Coosh added `GameConfig.GetExperienceForLevel(int level)` as the single canonical implementation with the correct dual-exponent curve, then replaced all 11 stale copies across the codebase with calls to it. `LevelMasterLocation.GetExperienceForLevel` is kept as a public pass-through to preserve its existing call API for any external consumers. `GameConfig.GetSessionXPThreshold` was also updated to use the same dual-exponent formula.

32 call sites redirected to the canonical method. Net diff: +52 / -211 lines.

Files touched: `Scripts/Core/GameConfig.cs`, `Scripts/Locations/{LevelMasterLocation,BaseLocation,DungeonLocation,MainStreetLocation}.cs`, `Scripts/Systems/{CombatEngine,CompanionSystem,NPCSpawnSystem,WorldSimulator,WorldInitializerSystem,WorldSimService}.cs`, `Scripts/Core/{NPC,GameEngine}.cs`.

Existing characters retain their current `Level` field. `CheckAutoLevelUp` only ever levels UP, never DOWN, so nobody de-levels on deploy -- they just see the correct threshold for their next level-up going forward. Anyone soft-locked at Lv.50+ in v0.60.4 is unstuck on v0.60.5.

---

## Snoop SSE keepalive + nginx pass-through hardening

The snoop SSE handler in `web/ssh-proxy.js` had no keepalive ping. If the snooped player was idle for 60+ seconds, nginx's default `proxy_read_timeout = 60s` killed the connection silently. The admin saw an overlay with no data and no error. There was also no server-side logging to diagnose any of this from the outside.

Three fixes:

1. **20-second keepalive ping** in the SSE handler -- sends a `: keepalive\n\n` SSE comment (silently ignored by EventSource clients) that keeps the TCP connection warm. Idle player no longer = dead snoop.
2. **Nginx `/api/` block tightened** -- added `proxy_buffering off`, `proxy_cache off`, `proxy_http_version 1.1`, `proxy_read_timeout 600s`, `proxy_send_timeout 600s`. Belt-and-suspenders: even if the keepalive ever misses, nginx now waits 10 minutes instead of 60 seconds before closing.
3. **Defensive lowercase + diagnostic logging** -- `snoopTarget = decodeURIComponent(...).toLowerCase()` so any future caller hitting the API with mixed case still matches the buffer rows (`WriteSnoopLine` C#-side stores everything as `LOWER()`). Plus `console.log` on snoop open/close with line counts so future reports can be diagnosed from `journalctl -u usurper-web`.

Files: `web/ssh-proxy.js`, `/etc/nginx/sites-available/usurper` (server config patched in place via awk).

---

## Full ban system rewrite


The pre-v0.60.5 ban system was account-name-only: `players.is_banned` was a boolean checked once at the auth password step. A banned player could (a) keep playing their current session until they disconnected on their own (no kick), (b) trivially register a new account with a different name, (c) connect from any IP without restriction. It existed, it discouraged, but it didn't actually ban anyone.

Full rewrite into a layered system:

### Schema

- New `banned_ips` table: `ip_address PRIMARY KEY`, `reason`, `banned_at`, `banned_by`, `associated_username`. Index on `associated_username` so unban-by-account can lift IP bans atomically.
- New `players.last_login_ip TEXT` column -- ALTER TABLE migration on first startup. Persisted on every login via `UpdatePlayerSession(username, isLogin: true, ipAddress: X)`. Lets `BanPlayer` find the IP to ban even when the player isn't currently online (offline-ban scenario from the admin dashboard).
- New `players.created_ip TEXT` column -- same migration pattern. Set during registration. Lets the per-IP registration rate-limiter count accounts created from a given address.

### Behavior

**`BanPlayer(username, reason)`** now does five things instead of two:

1. `UPDATE players SET is_banned = 1, ban_reason = @reason` (existing)
2. `DELETE FROM online_players` (existing)
3. **Capture the player's IP** -- current online IP first, fall back to persisted `last_login_ip`
4. **`BanIp(ip, ...)`** -- inserts/upserts the IP into `banned_ips` with `associated_username = X` so unban can find it. Loopback addresses (`127.0.0.1`, `::1`, `localhost`) are explicitly skipped to avoid locking out the local game-engine process.
5. **Kick the active session** -- via a new `SqlSaveBackend.KickActiveSessionHook` static delegate wired by `MudServer` at startup. The hook looks up `ActiveSessions[lower(username)]` and calls `DisconnectAsync($"Banned: {reason}")`. The TCP socket dies on the player's end immediately.

**`UnbanPlayer(username)`** lifts both the account ban AND any associated IP bans (`DELETE FROM banned_ips WHERE LOWER(associated_username) = LOWER(?)`). Single click in the dashboard restores everything.

**`MudServer` accept-time check** added at the earliest practical point in the session lifecycle -- right after the X-IP-forwarded-header / TCP-peer-address determination, before auth. A banned IP gets:

```
  Connection refused: this address is banned from the server.
  Reason: <reason>
```

...and a closed socket. No auth attempt, no register attempt, no session ever created. The check runs against both single-IP rows and CIDR rows (see below).

**`AuthenticatePlayer` and `RegisterPlayer`** both gained an `ipAddress` parameter and check `IsIpBanned` at the top of their flows. This is defense-in-depth -- the accept-time check should already block banned IPs from reaching either method, but if a future code path bypasses the accept gate (e.g., a direct HTTP login), the ban still applies.

### CIDR range bans

`banned_ips.ip_address` accepts CIDR notation: `1.2.3.0/24` covers 256 addresses, `1.2.3.0/16` covers 65k. Catches the common ban-evasion path of "dynamic IP from same ISP, log in 5 minutes later."

`SqlSaveBackend.IsIpBanned` does fast exact-match first, then scans CIDR rows (filtered by `LIKE '%/%'` so it only walks the CIDR subset, expected to be a handful at most). New `CidrContains(cidr, ip)` helper does the bit-mask comparison; supports both IPv4 and IPv6.

API endpoint validates IP/CIDR format with regex; refuses `0.0.0.0/0` and `::/0` with a polite "refusing to ban every IP on the internet" error.

Trade-off: CIDR bans hit innocent NAT-mates (mobile carrier blocks, shared housing, coffee shops). Use sparingly per-incident -- the dashboard's "+ Ban IP" modal mentions this explicitly.

### Per-IP registration rate limit

`RegisterPlayer` checks `COUNT(*) FROM players WHERE created_ip = X AND created_at > datetime('now', '-24 hours')` before allowing the insert. New `GameConfig.MaxRegistrationsPerIpPer24h = 3` constant -- generous enough for legit shared homes/dorms, tight enough to bite ban-evaders trying to "ban -> reregister -> repeat." 4th attempt within 24h gets `"Too many accounts registered from this address recently. Try again tomorrow."`

Loopback IPs skipped so local dev/testing isn't rate-limited.

### Web admin dashboard

- New "Banned IPs" section under Bot Detection. Table shows IP, reason, associated account (or "(direct IP ban)" for IP-only entries), banned-at timestamp, banned-by, and a "Lift" button per row.
- "+ Ban IP" button opens a modal accepting single IPs OR CIDR notation, with a help line explaining the trade-off.
- Player panel surfaces IP info inline next to the badges: `connected from 1.2.3.4` if online, `last login from 1.2.3.4` otherwise. Sysop can see what would be banned BEFORE clicking Ban.
- Ban modal has a checkbox `Also ban IP (full ban: blocks new accounts from this address)` -- defaults to ON. Unchecking gives a soft account-only ban for the rare case you want it.
- Unban via the dashboard automatically lifts the IP ban too.

### Audit trail

Every ban / unban / IP-ban / IP-unban writes to `wizard_log` with the action and details (including the IP that got banned). `journalctl -u usurper-mud` and the dashboard's Wizard Log section both show the full chain.

### What "ban" doesn't (and shouldn't) do

- **Permadeath the character** -- the player_data is preserved exactly. Unbanning restores everything as it was. Not a punishment escalation, just an access lock.
- **VPN/proxy detection** -- requires paid threat-intel services (IPHub, MaxMind, IPQualityScore) and an external API dependency. Would catch determined evaders using VPNs to bypass IP bans. Not built; flagged as the natural next layer if the existing one proves inadequate.

### Files

- `Scripts/Systems/SqlSaveBackend.cs` -- `banned_ips` table + index in schema, `last_login_ip` and `created_ip` migrations, `BanIp` / `UnbanIp` / `IsIpBanned` / `GetIpBanReason` / `GetLastLoginIpForPlayer` / `GetCurrentOnlineIpForPlayer` / `GetBannedIps` / `CidrContains` methods, enhanced `BanPlayer` (capture IP + kick + ban-IP cascade), enhanced `UnbanPlayer` (lift associated IP bans), `RegisterPlayer` rate limit + `created_ip` storage, `AuthenticatePlayer` defense-in-depth IP check, `KickActiveSessionHook` + `PermadeathPurgeHook` static delegates.
- `Scripts/Server/MudServer.cs` -- accept-time IP check, `KickActiveSessionHook` wiring at startup, `PermadeathPurgeHook` wiring at startup.
- `Scripts/Systems/OnlineAuthScreen.cs` -- threads the session IP into `AuthenticatePlayer` / `RegisterPlayer` calls.
- `Scripts/Systems/OnlineStateManager.cs` -- passes IP through `UpdatePlayerSession` so `last_login_ip` is persisted on every login.
- `Scripts/Systems/IOnlineSaveBackend.cs` -- updated `UpdatePlayerSession` interface signature.
- `Scripts/Core/GameConfig.cs` -- `MaxRegistrationsPerIpPer24h = 3` constant.
- `web/ssh-proxy.js` -- enhanced `/players/:username/ban` endpoint with optional IP-cascade, IP-aware unban, new `/banned-ips` GET / POST / DELETE endpoints with CIDR validation.
- `web/admin.html` -- Banned IPs section, refresh wired into the 30s dashboard cycle, IP-info inline in player panel, ban modal IP checkbox.

---

## Permadeath leaks shared world-state references

Same flow surfaced the other half of the bug above: when permadeath fires, `DeleteGameData` was clearing the player's `player_data` JSON blob and archiving it for the 7-day `/restore` window, but it wasn't touching any of the per-username rows in shared world-state tables. So a new character with the same username inherited:

- Guild membership (`guild_members.username`)
- God worship (`GodSystem.playerGods` in-memory dict + the god's persisted `Believers` count)
- Bounties on or by them (`bounties.target_player`, `placed_by`, `claimed_by`)
- Pending trades (`trade_offers.from_player`, `to_player`)
- Auction listings (`auction_listings.seller`, `buyer`)
- Sleeping-player state (`sleeping_players.username`)
- World boss damage records (`world_boss_damage.player_name`)
- Frozen / muted wizard flags (`wizard_flags.username`)
- Mail in/out (`messages.from_player`, `to_player`)
- Relationship cache (`RelationshipSystem` per-player NPC opinion entries)

### Fix

New **`SqlSaveBackend.PurgePlayerWorldState(username)`** does a single-transaction purge against 9 tables:

- `guild_members`, `online_players`, `sleeping_players`, `wizard_flags` (single-username rows)
- `messages` (from_player OR to_player)
- `trade_offers` (from_player OR to_player)
- `bounties` (target_player OR placed_by OR claimed_by)
- `auction_listings` (seller OR buyer)
- `world_boss_damage` (player_name)

Each table gets its own try/catch so a missing/renamed table doesn't abort the rest. All under one transaction so partial purge state isn't visible to readers.

**Deliberately NOT purged** (these survive permadeath as historical record):

- `pvp_log` (combat history)
- `wizard_log` (audit trail)
- `news` (the in-game newspaper)

New **`PermadeathPurgeHook`** static delegate fires from `PurgePlayerWorldState` after the SQL purge, for in-memory state cleanup that doesn't live in SQLite. Wired in `MudServer` startup to:

- `GodSystemSingleton.Instance.SetPlayerGod(username, "")` -- removes the worship dict entry AND decrements the god's believer count
- `RelationshipSystem.Instance.ResetPlayerRelationships(username)` -- clears NPC opinion cache

Order in `ExecutePermadeath`: `PurgePlayerWorldState(username)` -> `DeleteGameData(username)`. Purge runs first so any joined queries in the hooks still resolve the username while the row is intact.

Logs go to a new `PERMADEATH` debug category. Example output:

```
[PERMADEATH] Purged shared world-state references for 'rage' (guild, bounties, trades, auctions, etc.)
[PERMADEATH]   guild_members: removed 1 row(s) for 'rage'
[PERMADEATH]   bounties: removed 3 row(s) for 'rage'
[PERMADEATH]   sleeping_players: removed 1 row(s) for 'rage'
```

Files: `Scripts/Systems/SqlSaveBackend.cs` (`PurgePlayerWorldState` + `ExecPurge` helper + `PermadeathPurgeHook` delegate), `Scripts/Systems/PermadeathHelper.cs` (calls `PurgePlayerWorldState` before `DeleteGameData` in `ExecutePermadeath`), `Scripts/Server/MudServer.cs` (wires the hook).

---

## Beta launch banner refresh

The pre-v0.60.5 main-menu banner still said "BETA LAUNCHES MAY 1, 2026 -- ONLINE SERVER WIPE INCOMING" -- which was true for ~24 hours and then became outdated. Updated across all 5 languages (en/es/fr/hu/it) to the post-launch tone with bug-reporting instructions:

- **Compact (BBS one-liner)**: `[!] BETA - Expect bugs. Press ! key or type /bug to report them.`
- **Screen reader (3 lines)**: BETA / how to report (`!` key + `/bug`) / Discord URL
- **Box layout (4 lines)**: TITLE: `[!] BETA - GAME IN ACTIVE DEVELOPMENT [!]` / "Expect bugs. The game is being actively tuned and patched daily." / "Press ! key in-game or type /bug. Or join our Discord:" / discord URL

Tone matches the alpha banner -- "expect bugs, here's how to report them" -- but updated for the post-launch beta state.

Files: `Localization/{en,es,fr,hu,it}.json` (7 keys per language).

---

## Coosh added to credits

In-game credits screen, website (usurper-reborn.net), and Steam landing page all now list Coosh as a community code contributor. Citation reflects the bug-detective story: "Found and fixed an XP-formula desync regression by tracing his own soft-locked Lv.73 character to 12 duplicated copies of the same function across the codebase, then centralizing them into one canonical implementation."

Files: `Scripts/Core/GameEngine.cs` (engine credits screen call), `Localization/{en,es,fr,hu,it}.json` (`engine.credits_coosh` key in 5 languages), `web/index.html` + `web/lang/{en,es,fr,hu,it}.json` (website credits card + 5 languages), `web/steam.html` (Steam landing page credits card).

---

## Screen reader auto-detect false positive on Steam (WezTerm)

Player report (sighted Steam player on a clean v0.60.4 launch via the default Play.bat): *"Screen reader detected. Accessible mode enabled automatically."* showing on the title screen with no SR actually running.

Root cause: `AccessibilityDetection.IsScreenReaderActive` calls `SystemParametersInfo(SPI_GETSCREENREADER, ...)` -- the documented Microsoft API that NVDA/JAWS/Narrator set while running. The flag is stickier than the docs imply: browsers, accessibility tools, and apps that crashed without clearing it can leave `SPI_SCREENREADER = TRUE` set on the desktop session. `Console/Bootstrap/Program.cs` was firing the auto-detect for every non-headless launch, so any Steam player on a system where the flag had ever been set saw the message and got flipped into accessible mode against their will.

Fix: skip the auto-detect when running inside WezTerm (`TERM_PROGRAM == "WezTerm"`, set by WezTerm for all child processes). The default Steam launcher (`Play.bat`) wraps WezTerm; screen reader users on Steam are expected to use `Play-Accessible.bat` which passes `--screen-reader` explicitly (and short-circuits this whole code path via the existing `!GameConfig.ScreenReaderMode` guard). The auto-detect still runs for the standard console branch (non-WezTerm desktop console / direct `.exe` double-click) where it's the only path a screen reader user has to discover the mode without command-line flags.

Macros'n'Linux unaffected -- the underlying Win32 API returns false on those platforms anyway, so the auto-detect was already a no-op there.

Files: `Console/Bootstrap/Program.cs`.

---

## Files Changed (cumulative)

### New SQLite schema additions
- `banned_ips` table (created via `CREATE TABLE IF NOT EXISTS` on first startup)
- `players.last_login_ip` column (ALTER TABLE migration, idempotent)
- `players.created_ip` column (ALTER TABLE migration, idempotent)

### Modified files (game)
- `Console/Bootstrap/Program.cs` -- skip screen reader auto-detect when `TERM_PROGRAM == "WezTerm"`.
- `Scripts/Core/GameConfig.cs` -- version bump to 0.60.5; canonical `GetExperienceForLevel` (PR #98); fixed `GetSessionXPThreshold` exponent (PR #98); `MaxRegistrationsPerIpPer24h = 3` constant.
- `Scripts/Core/GameEngine.cs` -- 1 stale XP duplicate removed (PR #98); engine credits screen adds Coosh.
- `Scripts/Core/NPC.cs` -- 1 stale XP duplicate removed (PR #98).
- `Scripts/Locations/{LevelMasterLocation,BaseLocation,DungeonLocation,MainStreetLocation}.cs` -- 4 stale XP duplicates removed (PR #98).
- `Scripts/Systems/{CombatEngine,CompanionSystem,NPCSpawnSystem,WorldSimulator,WorldInitializerSystem,WorldSimService}.cs` -- 6 stale XP duplicates removed (PR #98).
- `Scripts/Systems/SqlSaveBackend.cs` -- `banned_ips` schema, IP-ban methods (`BanIp` / `UnbanIp` / `IsIpBanned` / `GetIpBanReason` / `GetLastLoginIpForPlayer` / `GetCurrentOnlineIpForPlayer` / `GetBannedIps` / `CidrContains`); enhanced `BanPlayer` (capture IP + kick + ban-IP cascade), enhanced `UnbanPlayer` (lift associated IP bans); `RegisterPlayer` rate limit + `created_ip` storage; `AuthenticatePlayer` defense-in-depth IP check; `UpdatePlayerSession` accepts and persists `ipAddress`; `KickActiveSessionHook` + `PermadeathPurgeHook` static delegates; `PurgePlayerWorldState` + `ExecPurge` helper.
- `Scripts/Systems/PermadeathHelper.cs` -- calls `PurgePlayerWorldState` before `DeleteGameData` in `ExecutePermadeath`.
- `Scripts/Server/MudServer.cs` -- accept-time IP check; `KickActiveSessionHook` and `PermadeathPurgeHook` wired at startup; `InteractiveAuthAsync` plumbs `effectiveIp` into auth/register calls.
- `Scripts/Systems/OnlineAuthScreen.cs` -- threads session IP into `AuthenticatePlayer` / `RegisterPlayer`.
- `Scripts/Systems/OnlineStateManager.cs` -- passes IP through `UpdatePlayerSession` so `last_login_ip` is persisted on every login.
- `Scripts/Systems/IOnlineSaveBackend.cs` -- `UpdatePlayerSession` signature gains optional `ipAddress`.

### Modified files (web)
- `web/ssh-proxy.js` -- snoop SSE keepalive, defensive lowercase, diagnostic logging; enhanced `/players/:username/ban` endpoint with IP cascade; IP-aware unban; new `/banned-ips` GET / POST / DELETE endpoints with CIDR validation; `last_login_ip` and `currentOnlineIp` surfaced in player detail.
- `web/admin.html` -- Banned IPs section, refresh wired into 30s dashboard cycle, IP-info inline in player panel, ban modal IP checkbox.
- `web/index.html` -- new credits card for Coosh.
- `web/lang/{en,es,fr,hu,it}.json` -- `credits.coosh_*` keys.
- `web/steam.html` -- new credits card for Coosh.

### Modified files (localization)
- `Localization/{en,es,fr,hu,it}.json` -- 7 beta-banner keys updated per language; `engine.credits_coosh` key added per language.

### Server config
- `/etc/nginx/sites-available/usurper` -- `/api/` block tightened with `proxy_buffering off`, `proxy_cache off`, `proxy_http_version 1.1`, `proxy_read_timeout 600s`, `proxy_send_timeout 600s`. Patched in place via awk; backup at `/etc/nginx/sites-available/usurper.bak.<timestamp>`.

---
## Credits

- **PR #98** authored by Coosh. Diagnosed the XP-formula desync from his own soft-locked Lv.73 character, traced the root cause to 12 duplicated copies of the same function, and submitted a clean centralized fix in a single PR. Merged and deployed within minutes.
- Player reports from Coosh and Rage drove this release.
