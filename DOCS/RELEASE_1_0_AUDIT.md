# Usurper Reborn: Beta to 1.0 Readiness Audit (v0.65.4, 2026-07-08)

Full-system audit ahead of leaving Beta. Eight parallel domain reviews (save-state, combat,
NPC, relationship, security/ops, docs, tech-debt, game-design content-completeness) plus a
live-server health pass and a five-language localization parity sweep. Findings are triaged
into four tiers. Tier 0 items are correctness/data-loss bugs that should be fixed before the
1.0 tag; Tier 1 are player-visible or reputational; Tier 2 are tracked/lower-risk; Tier 3 is
the honest 1.1+ roadmap.

Overall verdict: **the game is content-complete and structurally ready.** The core arc
(creation -> first kill -> level-25 specialization -> 7 Old Gods -> 5 endings -> NG+ with
prestige unlocks) is fully wired and reachable, the onboarding funnel is healthy in live
telemetry, and the June "structurally complete" call holds up under code inspection. What
stands between here and an honest 1.0 is a short list of correctness bugs (below), a store-page
text pass, and the release-day banner flip. There is no content gate.

Live server at audit time: v0.65.4 deployed, all services active (usurper-mud, sshd-usurper,
usurper-web, haproxy, nginx), DB 249 MB on 63% disk, 1.9 GB RAM. Prune tables plateau rather
than grow unbounded. Funnel (30-day, ~51-account cohort): 51 accounts -> 44 characters (86%)
-> 33 first-kill (65%) -> 31 second-login (61%). Admin dashboard password is rotated (not the
code default), so the default-credential path is not live-exposed.

## Progress (updated 2026-07-08)

Fixed in v0.65.5 (built, reviewed, 851 tests pass; not yet deployed):
- **All three Tier 0 blockers** (T0-1 seal save guard, T0-2 NPC-death permanence + romance cleanup,
  T0-3 AoE boss protection). Both reviewers signed off; three save-review follow-ups also fixed
  (guard scoped to `IsMudServerMode` so it does not break the per-process deployment, death news
  re-enabled, `DeathDate` added to the NPC round-trip).
- **Tier 1: T1-2** (married/divine XP now applies, plus the two reward-path parity gaps the reviewer
  flagged), **T1-3** (loc template breaks + missing keys), **T1-5** (save-catch logging), **T1-6**
  (orphan-at-cap data loss), **T1-7** (Church random stats).
- **Tier 1: T1-1 store-page factual accuracy** (web/index.html + web/steam.html + all 5 web lang
  files: 12 classes / 5 companions, Mystic Shaman + Melodia cards added, Magician spell claim
  corrected; README version bumped + recent-highlights entry). The in-game BETA banner flip to 1.0
  is intentionally still pending as a release-day action (the store-page factual errors were the
  "dishonest" part; the BETA wording itself is accurate until launch).

- **Tier 2 (started): save caps** -- the per-NPC personal inventory + market stock now cap in both
  serialize paths, and the online NPC `Enemies` list caps for parity. `DeathDate` round-trip (above)
  also closed a Tier 2 world-state-reload gap.
- **Tier 2: documentation pass** -- ARCHITECTURE.md (sslh -> haproxy topology, PROXY v2, Godot ref
  removed, usurper-world removed, version header), MULTIPLAYER_ARCHITECTURE.md (historical-note header
  + bcrypt -> PBKDF2), SERVER_DEPLOYMENT.md (current-topology note), BBS_DOOR_SETUP.md (--screen-reader
  + --auto-provision + CP437 + standalone-worldsim clarification), MODDING.md (15 families not 16, 79
  achievements not 75, --export-discoveries), DOCKER.md (clone URL), CLAUDE.md (IP-ban claim clarified),
  and a new DOCS/LLM_CONFIG.md for sysops. Note: the audit itself said "10 monster families"; the code
  has 15 -- the doc pass used the verified count.

Still open: the release-day banner/version flip (T1-1 remainder), the rest of Tier 2 (world_state
session-side CAS, RelationshipSystem per-session fragmentation [needs a design call], Brain v2 cohort
backfill, trade/auction prune), and all of Tier 3.

---

## TIER 0: True 1.0 blockers (correctness / data loss)

### T0-1. Seal/story-progression save contamination is logged but never prevented
`Scripts/Systems/SaveSystem.cs:1826-1854` (`SerializeStorySystems`). Confirmed firsthand.
The v0.60.10 patch added a `SUSPICIOUS_FALLBACK_SAVE` warning when `SessionContext.Current` is
null during an online save (the process-wide `StoryProgressionSystem._fallbackInstance` gets
serialized instead of the player's real seals), but the save proceeds anyway. This is the exact
mechanism behind the documented v0.60.10 seal-loss incident (a player's `[0,1,2,3,4]` collapsing
to `[0]`), and it is still reachable: only the diagnostic exists, not the guard.
Fix: when `IsOnlineMode && SessionContext.Current == null`, abort the save (retry once a context
is attached), or skip re-serializing the StorySystems block so the on-disk section is preserved
(mirrors the existing FamilySystem/Children online-skip at SaveSystem.cs:2510-2525). Seals are
per-player, so abort-and-retry is the safer choice.

### T0-2. Background NPC deaths run a "permadeath" cascade that is neither permanent nor complete
`Scripts/Systems/WorldSimulator.cs:658-765` (`MarkNPCDead`), `:1161-1197` (`ProcessNPCAging`),
`:1379-1427` (`HandleSpouseBereavement`). Two reviewers hit this same chokepoint from different
angles; it is the single buggiest area found.
- **Romance cleanup skipped (relationship):** the direct player-combat kill path calls the
  unified `RomanceTracker.OnNPCPermadied` + `NPCMarriageRegistry.OnNPCPermadied` (added v0.63.0),
  but every *background* death (Tier-A dungeon deaths added in v0.64.0 for ~50 named NPCs,
  NPC-vs-NPC street violence, gang/team wars, old age) routes through `MarkNPCDead`/`ProcessNPCAging`
  and never calls them. A permadied lover or FWB is never released from `CurrentLovers`/
  `FriendsWithBenefits`, `JealousyLevels` never clears, and affair records keyed on the dead NPC
  persist forever. This re-opens the exact bug class v0.63.0 wrote `OnNPCPermadied` to prevent.
- **Phantom widowhood/orphaning (NPC):** `npc.IsPermaDead = true` is commented out (permadeath is
  disabled; NPCs respawn), yet the `if (permadeath)` branch still runs `HandleSpouseBereavement`
  (unmarries the spouse, mails a player spouse a "you are widowed" notice) and `CheckForOrphanedChildren`
  before the same NPC respawns fully healed minutes later. Background violence can permanently end
  a player's marriage while the "dead" spouse walks around alive.
Fix: fold `OnNPCPermadied` (both stores) into `HandleSpouseBereavement` and call it from every
death path unconditionally (not gated on `Married`), AND gate the bereavement/orphan cascades on a
real permanence signal (restore `IsPermaDead` for these callers, or check the NPC has not respawned)
so they do not fire for respawning NPCs. `ProcessNPCAging`'s aged-death branch is the correct model.

### T0-3. AoE damage bypasses every Old God boss protection
`Scripts/Systems/CombatEngine.cs:11686-11800` (`ApplyAoEDamage`). Confirmed firsthand.
The single-target funnel `ApplySingleMonsterDamage` applies evasion, phase-immunity
(`IsPhysicalImmune`/`IsMagicalImmune` -> `ApplyPhaseImmunityDamage`), and `BossContext.DivineArmorReduction`.
`ApplyAoEDamage` applies only the evasion check, then `monster.HP -= actualDamage` with no immunity
and no divine-armor reduction. Every AoE spell/ability (Fireball, Chain Lightning, Tidesworn
Maelstrom of the Faithful) full-damages an Old God during an immune phase and ignores divine armor,
while the single-target path on the same boss correctly reduces or absorbs. Silent, high-value
exploit against the seven boss fights built around phase immunity.
Fix: mirror the immunity/divine-armor block from `ApplySingleMonsterDamage` into the per-monster
loop before `HP -=`; the `isSpellDamage` param already exists on `ApplyAoEDamage` but is unused for
gating (thread it from the AoE ability call sites the way the spell site already does).

---

## TIER 1: Should fix before 1.0 (player-visible / reputational)

### T1-1. Store-page and in-game banner: factual accuracy + Beta label (release-day gate)
The public front door misstates shipped reality and every player sees "BETA" on login.
- `web/index.html` + `web/steam.html`: "11 classes" should be **12 base + 5 prestige** (Mystic
  Shaman, v0.53.0, is missing from the count). "4 recruitable companions" should be **5**: Melodia
  (Music Shop, v0.49.0) has no card on either page. `steam.html` claims the Magician "commands 75
  spells" (no class has 75; that is a game-wide total at best) -- reword.
- README.md still says "BETA v0.61.4" (lines 5, 331, 343); highlights stop at v0.61.4.
- In-game `engine.alpha_*` banner (7 keys x 5 languages) renders "expect bugs" unconditionally via
  `GameEngine.ShowAlphaBanner()`. Plus `ending.credits_alpha_testers` (x5).
Fix: text edits + one companion card + the banner-key rewrite/gate. Half a day. This is the only
thing that makes dropping the Beta label dishonest today.

### T1-2. Married + Divine-Blessing XP bonuses are dead code
`Scripts/Systems/CombatEngine.cs:6488-6507` (inside `HandleVictory`). Confirmed firsthand.
`HandleVictory`/`DetermineCombatOutcome`/`ProcessPlayerAction` have zero callers (the entire
single-monster PvE path is unreachable; `PlayerVsMonster` delegates 100% to `PlayerVsMonsters`).
The live path `HandleVictoryMultiMonster` mirrors every reward bonus EXCEPT the 10%-if-married
spouse bonus and `DivineBlessingSystem.GetXPBonus` -- those exist only in the dead path. Married
players and divine-blessed players silently get no XP perk, single-player and online.
Fix: port both blocks into `HandleVictoryMultiMonster` (and `DistributeGroupRewards` for parity).

### T1-3. Localization: stale two-argument dungeon keys render raw templates in 4 languages
Confirmed firsthand. `dungeon.monster_appears`, `dungeon.boss_blocks_path_visual`,
`dungeon.boss_blocks_path_sr` in es/fr/it/hu still use the pre-v0.62.1 two-arg `[{0}]{1}[/]` form,
but the call site (`DungeonLocation.cs:7008-7018`) now passes ONE pre-composed `[color]name[/]`
string. `string.Format` throws on the missing `{1}`, `Loc.Get` returns the raw template, and
non-English players see literal `Un [{0}]{1}[/] aparece!` on every monster/boss encounter (a
high-frequency line). Fix: rewrite these 3 keys in 4 languages to the single-arg form matching EN
(`{0} appears!`). Also: 3 keys untranslated in Hungarian (`love_corner.already_married`,
`street.fight.over_cap_diminished`, `street.fight.over_cap_hint`) fall back to English.

### T1-4. Admin default credentials (self-hoster / Docker hardening)
`web/ssh-proxy.js:54-55,125-136`. If `BALANCE_PASS` is unset and no `balance_config.password_hash`
row exists, the admin dashboard (which fronts `/api/admin/nuke` full-world-wipe, bans, edits)
accepts `admin`/`changeme`. The live server is NOT exposed (its password is rotated), but a fresh
self-hosted or Docker deploy is wide open. Fix: refuse to start (or disable all admin routes) when
the stored hash is unset or still equals the default. Belongs with a new sysop LLM/admin doc.

### T1-5. Silent save-loss: log the save-path empty catches
`BankLocation.cs:808`, `EndingsSystem.cs:1663`, `PantheonLocation.cs:491`, `GameEngine.cs:3235/3518/3529`,
plus SqlSaveBackend/SaveFileRepair. Each swallows a save/autosave exception with an empty catch, so
a full disk or locked file loses progress with zero user feedback. Fix: log to DebugLogger and, where
possible, surface a one-line "save failed, retrying" to the player.

### T1-6. Royal orphan graduation deletes children at population cap
`Scripts/Systems/WorldSimulator.cs:1688-1759` (`ProcessOrphanComingOfAge`). Removes the orphan from
`king.Orphans` and sets `Child.Deleted = true` BEFORE `OrphanBecomesNPC` checks the alive-population
cap; when the cap is hit (which is most of the time), the orphan is gone from both lists permanently
with no NPC created. Regular children got an "away at school" park-and-retry in v0.64.0; royal orphans
did not. Fix: check the cap before mutating, or mirror FamilySystem's away-state retry.

### T1-7. Church "this month" community stats are random noise
`Scripts/Locations/ChurchLocation.cs:1303-1308`. Monthly donations/marriages/blessings/souls are
`Random.Shared.Next(...)`, so they change every visit -- immersion-breaking on a second look. Fix:
wire to real aggregate counts or make them static flavor (the per-player records above are real).

---

## TIER 2: Should fix, lower priority / tracked

Persistence and world-state:
- **world_state session-side CAS gap** (`IOnlineSaveBackend.cs:20`, `OnlineStateManager` five call
  sites use blind `SaveWorldState`). Confirmed. The concrete `SaveWorldStateIfVersion` exists and the
  world-sim's own npcs/royal_court writes use it, but session-side player writes race the tick on the
  same keys. Flagged post-1.0 (F5/F7) in the June plan; save-reviewer rates it a blocker, NPC-reviewer
  and June owner rate it post-1.0. At 20-50 accounts it is real but rare -- promote `SaveWorldStateIfVersion`
  to the interface and add a bounded CAS retry to the session-side writes when time allows.
- **NPC teammate inventory + market stock uncapped** (`SaveDataStructures.cs:905-913`; both serialize
  paths). Slow bloat vector; add `MaxSerializedNPCInventory` mirroring the companion cap (30).
- **OnlineStateManager missing Enemies cap parity** (`OnlineStateManager.cs:1346-1414`); reuse
  `MaxSerializedEnemiesPerNpc`. `Enemies.Add` has no in-memory cap and is the real growth risk.
- **RelationshipSystem is per-session, not per-world** (`RelationshipSystem.cs:12-35`); NPC-NPC score
  changes made by the world-sim (fallback instance) are invisible to player sessions, and every touched
  pair duplicates into that player's save forever. Needs a design call: gate online like Children, or
  split player-private vs world-shared pairs. Marriages are unaffected (they live in the global
  NPCMarriageRegistry).
- **trade_offers + auction_listings have no prune** (`SqlSaveBackend.cs:404-434`); add a 30-day prune
  on terminal-status rows to the existing WorldSimService prune pass.

NPC subsystem:
- **Brain v2 cohort dormant on a stable population** (`WorldSimulator.cs:414`, gated on `IsAIDriven`).
  The cohort only grows via immigrant/child/orphan creation, and permadeath-disabled + near-cap
  population means turnover is ~30 days/NPC, so the scorer barely runs. Add a sysop/startup pass to
  flip a configurable % of existing NPCs to `IsAIDriven=true` (the same reasoning that dropped the
  v0.64.0 LLM-goals gate).
- **Dark Alley NPC gold faucet** (`WorldSimulator.cs:5586-5672`); flagged frictionless in v0.61.5
  telemetry, then amplified in v0.63.2. A structural inflation source. Add a daily per-NPC gold cap
  or a real loss tail.
- **NPCTeamDungeonRun has no explicit player-team guard** (`WorldSimulator.cs:3690+`); currently safe
  via two downstream mechanisms, but a double-negative with no test. Add a guard/comment or a regression
  test pinning "player-team NPC never takes a MarkNPCDead hit from an autonomous team_dungeon run."

Relationship:
- **VN marriage-proposal accept bypasses `PerformMarriage` guards** (`VisualNovelDialogueSystem.cs:2912`);
  hand-rolls the four-owner update, skipping `BannedMarry` (king-banned) and `MinDaysBeforeMarriage`.
  Route through `RelationshipSystem.PerformMarriage` instead. Niche (king feature), low urgency.

Combat / maintenance:
- **Delete or guard the dead single-monster PvE path** (`ProcessPlayerAction`, `ApplyAbilityEffects`,
  `HandleVictory`, `DetermineCombatOutcome`, and their `Execute*` twins). This is the trap that produced
  T1-2: a contributor patching the dead copy believes they fixed something. Delete it, or add a test
  proving `PlayerVsMonster` never diverges.

Security / ops (all topology-mitigated today):
- **X-IP header is attacker-spoofable** (`MudServer.cs:437-439,503-505`) and drives ban + throttle
  attribution; only honor it from loopback/trusted-proxy peers.
- **WebSocket proxy: no payload cap, no per-IP connection limit** (`ssh-proxy.js:4052,4080`); set
  `maxPayload` (~64 KB) and cap concurrent connections per IP.
- **CI `set -x` traces the SSH key write** (`ci-cd.yml:767,771`); scope `-x` away from the secret.
- **No nginx rate limit on `/api/*` or the WS upgrade**; add `limit_req`/`limit_conn`.
- **QuestSystem default auto-complete** (`QuestSystem.cs:850` `default: return true`); not reachable via
  shipping content, but change to `return false` so a future quest kind cannot self-complete.

Tech-debt:
- **Three confirmed dead methods** (zero callers): `CombatEngine.CheckElementalEnchantProcsMonster`,
  `TeamSystem.GangWars`, `PuzzleSystem.PresentPuzzle` (the whole PuzzleSystem appears unwired). Delete.
- **CLAUDE.md stale security claim** (lines 123-124): says SSH IP-ban is "stubbed until Phase 2" -- the
  direct-TCP path IS enforced now (`MudServer.cs:506`); only the SSH front-door lacks it (descoped).
  Reword so it does not read as "all IP bans unenforced."
- **~15 raw-English player strings** in the dungeon tutorial (`DungeonLocation.cs:442-629`) and a few
  combat-outcome/menu lines; a half-day loc cleanup, not a blocker.

Documentation (stale, mostly mechanical; see the release-day flip list below):
- ARCHITECTURE.md: sslh -> HAProxy topology, drop `usurper-world.service`, remove Godot reference,
  header "v0.60.7 (Beta)". MULTIPLAYER_ARCHITECTURE.md: rewrite or archive (documents the deprecated
  per-process SSH model, says bcrypt when the code is PBKDF2, omits GMCP/groups/CAS). SERVER_DEPLOYMENT.md:
  align to the HAProxy MUD deployment. MODDING.md: "16 monster families" -> 10; reconcile achievement
  count; document `--export-discoveries`. BBS_DOOR_SETUP.md: add `--screen-reader`/`--auto-provision`,
  CP437 notes, drop the deprecated `--worldsim` guidance. DOCKER.md: clone URL `jknight` -> `binary-knight`.
  New DOCS/LLM_CONFIG.md for the `USURPER_LLM_*` env vars (currently undocumented for sysops).
- es/fr/it are ~297-308 keys behind en.json (mostly comment keys, `dialogue.enhance.*` faction lines,
  and a handful of `base.*` strings); English fallback works, so this is a completeness pass, not a break.

---

## TIER 3: Honestly post-1.0 (1.1+ roadmap)

- **Content depth (no live menu implies these exist, so none read as broken promises):** gear set
  bonuses, faction-locked vendor gear, rally-to-your-fights ally, Dark Alley Monte/Skull redesigns,
  Black Market rarity floor + sell-side fence, Sanctum 6b verbs (Sponsor Pilgrim, Bail Debtor), merc
  contract tiers 3-5 + Legend's Pick, honor-tournament title series, Journal Slice 3, Electron client
  promotion, Arabic/RTL. The one to watch first post-launch is the **gear/reward loop**: ProgressionRoadmap
  made the ability ladder feel great, which by contrast exposes that the gear ladder (no set bonuses, no
  faction vendors) is the thinner midgame reward track. Good 1.1 headline, not a gate.
- **Online prison multiplayer** (`PrisonLocation.cs:231/979`, `PrisonWalkLocation.cs:358/688`): silent
  stubs, no "coming soon" menu, only reachable by being imprisoned by a live king (rare at 5 concurrent).
- **MoreCompat town-claim stub** (`MoreCompat.cs:123`): dead code, zero callers; optionally delete.
- **Low-frequency table prune** (pvp_log, wizard_log, admin_commands, bounties, team_wars, castle_sieges)
  and `PRAGMA incremental_vacuum` for file-size hygiene. Fine for years at current scale.
- **`entered_dungeon` telemetry milestone** reads low (10/51) but is almost certainly a late-added
  instrument, not a funnel cliff (first_kill at 33 requires dungeon combat). Backfill/verify post-launch.

---

## Release-day flip checklist (mechanical)

1. `GameConfig.cs`: `Version` 0.65.4 -> 1.0.0; `VersionName` "Countdown" -> release name.
2. Localization x5: rewrite the 7 `engine.alpha_*` keys to drop BETA wording, or gate `ShowAlphaBanner()`
   behind a config flag so it no-ops at 1.0. Also `ending.credits_alpha_testers` x5.
3. README.md: "BETA v0.61.4" -> 1.0.0 at lines 5, 331, 343; append the 0.62-0.65 highlights.
4. web/index.html + steam.html: class count 11 -> "12 + 5 prestige"; companion count 4 -> 5 + Melodia
   card; Magician "75 spells" reword; matching web/lang/*.json keys.
5. ARCHITECTURE.md header version.
6. Regenerate/delete stale build artifacts: `publish/local/FILE_ID.DIZ` (v0.60.8 [BETA]),
   `publish/local/version.txt` (0.61.4), `build/dist/.../version.txt` (0.57.2); confirm CI substitutes
   `{VERSION}` in `dist/FILE_ID.DIZ`.
7. `WizardCommandSystem.cs:535`: reword "Beta-launch Rage event."
8. Steam store page: clear the Early Access / Beta flag (external to repo).
9. Confirm the GitHub `production` environment has a required-reviewer protection rule (the CI job is
   already gated on `environment: production`; enforcement is a repo setting).

---

## Recommended sequencing

- **Pre-1.0 must-fix (Tier 0 + Tier 1):** T0-1 seal guard, T0-2 MarkNPCDead cascade (romance + phantom
  widowhood together), T0-3 AoE boss protection, T1-1 store/banner, T1-2 married/blessing XP, T1-3 loc
  templates, T1-4 admin default creds, T1-5 save-catch logging, T1-6 orphan cap, T1-7 church stats. All
  are small, surgical, low-risk. Estimate: 2-3 focused days plus the mechanical flip list.
- **Ship 1.0.**
- **Fast-follow 1.0.1 (Tier 2):** the world_state CAS promotion, the save caps, the RelationshipSystem
  design call, the Brain v2 cohort backfill, and the doc rewrites.
- **1.1 (Tier 3):** gear/reward-loop depth first.

Nothing in this audit is a structural or architectural blocker. The critical path is complete and
reachable; the work is correctness cleanup, a text pass, and the banner flip.
