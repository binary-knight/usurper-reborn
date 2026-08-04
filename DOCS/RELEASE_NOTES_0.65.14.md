# Usurper Reborn v0.65.14 - Security Audit Response

Response to an external source audit of v0.65.10. Every finding was re-verified
against the tree before acting; four are fixed here, two were already correct in
code, one is architectural and documented, one was not reproducible.

## Fixed

### F2 (HIGH) - Default admin credentials disarmed their own lockout
`web/ssh-proxy.js` `isDefaultCredentialActive()` returned false whenever
`BALANCE_PASS` was merely SET, regardless of value. `docker-compose.yml` shipped
`BALANCE_PASS=changeme`, so on every stock Docker deploy the v0.65.8 lockout
never engaged and `admin` / `changeme` authenticated straight through to the
admin dashboard, whose endpoints include `/api/admin/nuke` (world wipe), bans,
and player edits.

- New `SHIPPED_DEFAULT_PASS` constant, never derived from the environment.
- The env bypass now requires the operator's password to DIFFER from the
  shipped default; setting it to `changeme` counts as unconfigured and keeps
  the admin surface locked to the change-password flow (fail closed), with a
  warning logged at startup.
- `docker-compose.yml` no longer ships a working password. `BALANCE_PASS` is
  commented out with instructions, so an unconfigured deploy is locked by
  default rather than open by default.

### F1 (HIGH on fresh deploys) - Implementor account was registrable
`PlayerSession` auto-promotes the configured Implementor username to the top
wizard tier on every login, and Implementor can never be demoted. The name was
a hardcoded constant with no registration guard, so on a fresh self-hosted or
Docker deploy anyone who registered it first became permanent superuser.

- New `SqlSaveBackend.IsReservedUsername()`, applied at BOTH account-creation
  paths (`RegisterPlayer` and `AutoProvisionPlayer`). Exact, case-insensitive,
  trimmed. The operator provisions the account out of band.
- `WizardConstants.ImplementorUsername` is now operator-configurable via the
  `USURPER_IMPLEMENTOR` env var (normalized to lowercase), so self-hosters
  point the superuser at an account they control instead of inheriting the
  canonical server's. The old `IMPLEMENTOR_USERNAME` name is kept as an alias.

Note: existing populated servers were never exposed here. `RegisterPlayer`
already rejected duplicates case-insensitively
(`WHERE LOWER(username) = LOWER(@username)`), usernames are lowercased at
INSERT, and `username` is the table's PRIMARY KEY. Only deploys where the name
was still unclaimed were at risk.

### F5 (MEDIUM) - WebSocket origin allowlist was prefix-matched
`origin.startsWith(allowed)` accepted `https://usurper-reborn.net.evil.com`.
New `isAllowedOrigin()` parses both sides and compares scheme + hostname
exactly (with port when the allowlist pins one); `file://` / `app://` are
matched on scheme alone, which is what the Electron client actually sends.
Missing-Origin connections remain allowed on purpose: Electron and MUD clients
send none, and terminal sessions still require in-game credentials.

### F3 (MEDIUM) - SSH gateway credential was hardcoded
`SSH_USER` / `SSH_PASS` are now env-overridable per deploy (defaults preserved
for back-compat). This account is a dumb pipe -- `sshd-usurper` ForceCommands
the relay and real authentication happens in-game -- but a credential shared by
every deploy shouldn't live in source. Only used when `MUD_MODE=0`.

## Verified correct already (no change)

- **Player password hashing**: PBKDF2, 100k iterations, random per-user salt.
  The audit agreed; noted here because the same report's F4 is easy to misread
  as applying to game accounts. It does not.
- **X-IP spoofing**: forwarded client IPs are honored only from loopback peers
  (fixed v0.65.8), so the registration rate limit cannot be bypassed remotely.

## Assessed, not reproducible (F8, ANSI/terminal injection)

The audit flagged this as an unverified review item. It is not exploitable on
the MUD input path: `TerminalEmulator.ReadLineInteractiveAsync` runs an escape
state machine that consumes `\x1b` sequences, and the append branch is gated on
`c >= ' '`, so control bytes never reach the input buffer that feeds display
names, tells, or bug reports. Every online transport (web terminal, SSH relay,
raw TCP) reads through that one function.

## Accepted risk, documented (F4)

The `[O] Online Play` client's saved-credential file is Base64 (reversible),
and its AUTH line crosses the wire in plaintext on the direct-TCP path. The SSH
and web transports are encrypted; direct TCP is not. Fixing properly means
per-deploy key wrapping for the local file and TLS (or SSH-only auth) for the
transport -- both are real work, neither is a same-day change, and the exposure
is local-attacker / same-network. Tracked for 1.0.x.

## Tests
13 new tests (908/908 total) in `Tests/SecurityAuditV06514Tests.cs`: reserved
username case-insensitivity and exact-match boundaries (prefix / suffix / empty
must NOT be reserved), the legacy constant alias, and env-configurability.
The two JavaScript predicates were exercised out-of-process against the live
source text (9/9 origin cases including the reported bypass, 3/3 lockout
semantics).

## Files Changed
- `Scripts/Core/GameConfig.cs` - Version 0.65.14
- `Scripts/Server/WizardLevel.cs` - Implementor username env-configurable; alias kept
- `Scripts/Systems/SqlSaveBackend.cs` - IsReservedUsername + guard at both registration paths
- `web/ssh-proxy.js` - Fail-closed default-credential lockout; exact-hostname origin check; env-injected SSH gateway creds
- `docker-compose.yml` - No working default admin password; hardening instructions
- `Tests/SecurityAuditV06514Tests.cs` - New
