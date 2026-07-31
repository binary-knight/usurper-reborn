# Release Notes - v0.65.8 (Countdown)

Tenth release of the Beta -> 1.0 "Countdown" cycle. This is the "finish the level-40 cliff"
batch: the three remaining recommendations from `DOCS/PLAYER_EXPERIENCE_ANALYSIS.md` (R3
boss flee, R4 grind flattening, R5 death legacy) plus the last open Tier 1 audit item
(admin default credentials) and a set of launch-scale ops hardening fixes from the 1.0
readiness audit's Tier 2 list.

## R4: The 20s-30s grind is flattened

The early-game XP multiplier used to go fully transparent at level 21 -- exactly where
telemetry showed the ~650-fights-per-decade wall began (the point where the game's most
engaged players stalled and drifted away). The curve now tapers smoothly instead of
falling off a cliff:

- Levels 1-5 (3.0x) and 6-10 (2.0x) are unchanged -- the verified onboarding pacing stays.
- Levels 11-15 rise slightly (1.5x -> 1.8x) so the curve stays monotonic.
- Levels 16-39 taper linearly from 1.8x down to 1.0x at level 40 (about 1.6x at 21,
  1.5x at 25, 1.3x at 30, 1.2x at 35).
- Level 40+ is fully transparent, exactly as before.

The curve is monotonic decreasing, so leveling never makes per-fight XP jump upward.
Combined with easier floor-pushing (bounded accuracy, Death's Door, and the flee fixes
below), a decade should now cost a few hundred fights instead of six hundred fifty.

## R3: A flee that works when it matters

Telemetry showed flee usage at 0.5% of fights -- players had correctly learned that the
escape valve did not function in the one situation that kills people (boss bursts).
Two changes:

- **Desperation scaling.** Old God boss fights used a flat 20% flee chance regardless of
  state. Now, below 50% HP the chance ramps up steadily, reaching 60% near death. A
  desperate player can actually get out; a healthy player still can't trivially skip the
  fight. (Regular floor bosses already used the standard DEX/level formula -- unchanged.)
- **A failed flee costs a guarded half-round, not a full free enemy round.** When a
  retreat attempt fails, the player falls back guarded: all monster damage against them
  is halved for the remainder of that round (basic attacks, monster abilities, life
  drains, boss AoE and channel bursts). Trying to escape a deadly fight is no longer a
  strictly-dominated move. NPCs got predictive-death fleeing in v0.65.6; this is the
  player's half of that bargain.

## R5: Death leaves a legacy

An involuntary permadeath used to yield absolutely nothing -- a level-25 deletion produced
no meta-progression, no record, no head start. All stick, no legacy. Now (online
permadeath mode):

- **The Hall of the Fallen.** Every permadied character's name is carved into a permanent
  memorial at the Temple (`[K] Hall of the Fallen`, online mode): name, level, class,
  killer, and the date the Veil closed. The permadeath cinematic says so at the moment it
  matters most.
- **A level-scaled heirloom.** The next character created on the same account inherits
  500 gold per level of the fallen character (capped at 40,000). Scales with the LEVEL of
  the dead character, never their wealth -- there is no funnel-gold-through-death exploit
  -- and stacks beneath the existing eldest-adult-child inheritance from v0.63.0. The
  claim is atomic (a double-fire can't double-grant) and granted before the new
  character's first save so it can't be lost to a crash.
- New `fallen_legacy` SQLite table (auto-created via CREATE TABLE IF NOT EXISTS; rows are
  never pruned -- the memorial is the point; ~150 rows/year at observed death rates).

## Audit T1-4: Admin dashboard locked while the default password is active

The last open Tier 1 item from `DOCS/RELEASE_1_0_AUDIT.md`. A fresh self-hosted or Docker
deploy used to accept `admin`/`changeme` on the admin dashboard -- which fronts the
full-world-wipe endpoint, bans, and player edits. Now, while the default credential is
still active (no `BALANCE_PASS` env and the stored hash still matches the default), every
authenticated balance/admin route except login and change-password returns 403 with an
instruction to set a real password first. The check handles the bcrypt-migrated-default
case and fails safe (locked) on errors. Changing the password to the default value is
also refused.

## Ops hardening (audit Tier 2, launch-scale)

- **X-IP spoofing closed.** The `X-IP:` forwarded header drives ban and login-throttle
  attribution, and was honored from any peer -- an external client could spoof another
  address to evade its own ban or pin failed-login lockouts on a victim IP. The header is
  now only honored when the raw TCP peer is loopback (the web proxy and SSH relay, the
  only legitimate senders). Non-loopback X-IP lines are consumed (protocol stays in sync)
  but ignored, with a SECURITY log line.
- **WebSocket caps.** The web terminal proxy now sets `maxPayload` (64 KB -- keystrokes
  are tiny) and enforces a per-IP concurrent-connection cap (default 8, env
  `WS_MAX_CONN_PER_IP`).
- **Nginx rate limits.** `scripts-server/nginx-usurper.conf` gains `limit_req` on
  `/api/*` (10 r/s, burst 30) and `limit_conn` on the WebSocket/SSE endpoints (10 per
  IP). Server-side config -- applies at next config deploy.

## LLM pipeline health alerting

The LLM moments/goals pipeline failed silently for three weeks (June 17 - July 11 API-key
outage) because nothing watched the failure rate. The web proxy now checks `llm_usage`
every 30 minutes: if 5+ real attempts in the last 2 hours succeeded under 50% of the
time, it alerts to the Discord gossip channel (when the bridge is configured) and the
console, at most once per 12 hours. `llm_disabled` rows are excluded so intentionally
LLM-less servers never alarm. `DOCS/LLM_CONFIG.md` now also recommends
`USURPER_LLM_TIMEOUT_MS=10000` when strategic goals are in play (the v0.65.6 operational
note, now written down where sysops will find it).

## Dead single-monster combat path: guarded

The entire single-monster PvE chain (`ProcessPlayerAction`, `DetermineCombatOutcome`,
`HandleVictory`, `ApplyAbilityEffects`, and their Execute* twins) is unreachable --
`PlayerVsMonster` delegates 100% to the multi-monster path -- but patching it by mistake
"fixes" nothing, which is exactly how the married/divine XP bonuses went dead (audit
T1-2). The four root methods now carry a loud DO-NOT-PATCH banner naming the live
equivalent, and call a `DeadCombatPathGuard()` that throws instantly if the path is ever
re-wired, so an accidental future call screams in testing instead of diverging quietly.
Full deletion of the chain remains queued as a dedicated pass.

## Localization

13 new keys in all 5 languages (en/es/fr/it/hu): the guarded-flee line, the two
permadeath legacy cinematic lines, the heirloom claim + hint, and the 8 Hall of the
Fallen surface strings. Hungarian keeps all format args in suffix-free positions
(names as nominative appositives after colons, numeric args before suffixed nouns).

## Acceptance metrics (re-run 2-3 weeks post-deploy)

Same queries as the v0.65.6 batch, per `DOCS/PLAYER_EXPERIENCE_ANALYSIS.md`:
- Level-40+ crossings per week (baseline ~0)
- Involuntary deletions per week (baseline ~3)
- Population at 0-1 lives (baseline 11 players)
- Flee usage and success rate in boss fights (baseline 0.5% usage)
- 30-day retention of the level-20+ cohort

## Deploy notes

- Game binary: standard deploy (fallen_legacy table auto-creates on first startup).
- `web/ssh-proxy.js`: restart `usurper-web`. NOTE: on first restart the admin/balance
  dashboards will LOCK if the stored password still equals the default -- the live server
  password is rotated, so no action expected, but self-hosters must change theirs.
- `scripts-server/nginx-usurper.conf`: optional config re-deploy (`nginx -t` + reload).
- Recommended at deploy: raise `USURPER_LLM_TIMEOUT_MS` to `10000` in
  `/etc/systemd/system/usurper-mud.service.d/llm.conf` (v0.65.6 recommendation).

## Files Changed

- `Scripts/Core/GameConfig.cs` -- Version 0.65.8; XP taper extended to level 40;
  `FallenLegacyGoldPerLevel` / `FallenLegacyMaxGold` / `GetFallenLegacyGold` constants
- `Scripts/Core/Character.cs` -- `FleeGraceThisRound` transient combat field, cleared in
  `CaptureRoundStartHP`
- `Scripts/Systems/CombatEngine.cs` -- boss-flee desperation scaling in
  `CalculateFleeChance`; failed-flee sets `FleeGraceThisRound` + guarded-flee line in the
  multi-monster retreat branch; `ApplyFleeGrace` helper applied at all six
  monster-vs-player damage sites (basic attack, DirectDamage / life-steal /
  DamageMultiplier abilities, boss AoE, boss channel); `DeadCombatPathGuard` + banners on
  the four dead single-monster methods
- `Scripts/Systems/PermadeathHelper.cs` -- fallen-legacy record before deletion; two
  legacy cinematic lines
- `Scripts/Systems/SqlSaveBackend.cs` -- `fallen_legacy` table + `RecordFallenLegacy` /
  `ClaimFallenLegacy` (atomic) / `GetFallenMemorials`
- `Scripts/Core/GameEngine.cs` -- heirloom claim in `CreateNewGame` before the first save
- `Scripts/Locations/TempleLocation.cs` -- `[K] Hall of the Fallen` menu entry (visual +
  screen reader), dispatch case, `CanShowHallOfTheFallen` gate, `ShowHallOfTheFallen`
  display
- `Scripts/Server/MudServer.cs` -- raw-peer capture moved above the X-IP parse; X-IP
  honored only from loopback peers
- `web/ssh-proxy.js` -- default-credential lockout (`isDefaultCredentialActive` + gates
  in both balance and admin handlers, default-password re-set refused); WS `maxPayload` +
  per-IP connection cap; LLM health monitor (`checkLlmHealth` on a 30-min interval)
- `scripts-server/nginx-usurper.conf` -- `limit_req` / `limit_conn` zones
- `DOCS/LLM_CONFIG.md` -- timeout recommendation
- `Localization/{en,es,fr,it,hu}.json` -- 13 new keys each
- `Tests/ReleasePrepV0658Tests.cs` -- **NEW** -- 4 tests pinning the XP taper shape
  (early bands unchanged, taper-to-1.0-at-40, monotonicity) and the heirloom formula
  (level scaling, cap, negative clamp)
