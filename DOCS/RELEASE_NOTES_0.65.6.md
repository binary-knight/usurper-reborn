# Release Notes - v0.65.6 (Countdown)

Eighth release of the Beta -> 1.0 "Countdown" cycle. Two major passes in one release. **Part 1** is
a focused NPC-behavior pass driven by a full deep-dive into the live decision telemetry (~331k NPC
decisions over 14 days) plus a code-level read of the whole decision pipeline (candidate builder,
Brain v2 scorer, abstract combat sim, Tier-A real combat sim, LLM strategic-goal flow). Two goals:
NPCs make good, life-preserving choices, and the LLM-backed brain drives behavior for the whole
population. **Part 2** is the first slice of the player-experience overhaul driven by the
end-to-end analysis in `DOCS/PLAYER_EXPERIENCE_ANALYSIS.md`: renewable resurrections, burst-death
protection, and visible risk (see "Part 2" below).

# Part 1: NPC behavior

## What the telemetry showed

- ~1,124 NPC dungeon deaths in 14 days (about 14% of expeditions ended in death). NPCs fought until
  20% HP before even rolling to flee, with no awareness that the next hit would kill them; most deaths
  were "failed one flee roll while one swing from death."
- A near-death NPC learned nothing: dungeon losses never wrote a memory, so the scorer's
  "recently attacked, avoid combat" caution layer (built in v0.64.0) never fired for monster fights,
  and an NPC that barely escaped would heal and walk straight back in.
- 98.2% of shop visits bought nothing (84,858 fruitless trips): the built-in shop stock tops out
  around 80 weapon power, but the scorer expected Level*12, so every NPC above about level 10 was
  flagged "under-geared" forever and sent window-shopping for eternity.
- 96.7% of gym visits did nothing: the candidate gate required 20 gold but a session costs
  Level*10+50, so mid-level NPCs kept walking to a gym they could not afford.
- Good news: ~91% of the population was already on the Brain v2 goal-driven scorer, and LLM strategic
  goals are flowing into behavior (goal-family verb boosts + physical target pursuit) now that the
  API key works again.

## Life preservation

- **Predictive-death flee.** All three combat simulations (solo abstract, team, and the Tier-A real
  combat sim) now estimate the enemy's expected next hit using the exact damage math that combat
  actually applies. If the NPC could not survive roughly 1.5 such hits, they panic-flee at a flat 95%
  regardless of personality; self-preservation beats Courage at the brink. Fights the NPC is winning
  are untouched -- the existing 20% flee threshold still governs those, so kill-completion (the reason
  the threshold was lowered in v0.63.2) is preserved.
- **Survival lessons.** Dying (NPCs respawn from ordinary deaths, and the brain survives the respawn)
  or fleeing a fight below 35% HP now writes a new "survived danger" memory. The Brain v2 scorer's
  existing combat-caution layer reads those memories for 4 hours and discounts dungeon / team-dungeon /
  dark-alley / throne-challenge verbs, so a mauled NPC spends the afternoon healing, shopping, and
  socializing instead of marching straight back down the stairs. This closes the gap explicitly
  deferred in v0.64.0 Slice 3 ("dungeon-loss outcomes don't currently write Attacked memories").
  A near-death also now registers as fear in the NPC's emotional state for the same 4-hour window.
  (Review note: the lesson uses a NEW memory type rather than reusing "Attacked" -- the Attacked type
  feeds the revenge-goal generators, which assume the attacker is a real person; a monster name there
  would have minted instantly-self-completing "revenge against Golem" goals and spammed the news feed.)
- **Player-team guard.** `NPCTeamDungeonRun` now has an explicit player-team early-return (previously
  it relied entirely on upstream dispatcher gates; a future dispatcher change could have silently
  exposed player teammates to autonomous dungeon deaths).

## Realistic decisions (no more do-nothing loops)

- **Shops can say "come back later."** A shop visit that ends with no purchase marks a 6-hour
  "already checked the shops" cooldown that the scorer respects. The gear-gap shopping boost now only
  fires at level 12 and below, the band where the built-in shop stock can actually close a gap; above
  that, gearing comes from dungeon loot and level-up gains, exactly as the game already provides it.
  And a shopping trip now checks BOTH the weapon and armor shops before giving up, instead of flipping
  a coin and browsing only one.
- **The gym gate matches the price.** NPCs only head to the gym when they can afford the session.
- **Whole-population brain.** The remaining ~9% of NPCs still on the legacy weighted-random picker are
  promoted to the Brain v2 goal-driven scorer. The v0.64.0 A/B cohort served its purpose; there is no
  reason to keep a slice of the world dumber than the rest. LLM strategic goals, need-driven scoring
  (heal when hurt, level when ready), and target pursuit now apply to everyone. (Review note: the
  promotion is applied where the live roster is actually built -- both NPC restore sites -- because the
  MUD server never runs the world-sim's startup path and reloads NPCs from shared state continuously;
  a startup-only backfill would have been dead code on the live server.)

## LLM brain status (server-side, no code change)

The deep-dive confirmed the strategic-goal pipeline is healthy end to end: the LLM proposes long-arc
goals per NPC, the scorer biases verb selection toward the active goal family, and targeted goals
steer NPCs to physically seek out the person their goal names. Two operational notes: the API key
outage (June 17 to July 11) meant every call fell back to templates for three weeks -- worth watching
the dashboard's LLM tab after any key rotation; and the 3-second request timeout is marginal for
strategic-goal calls (historical successes averaged 2.4s), so raising `USURPER_LLM_TIMEOUT_MS` to
10000 in the systemd drop-in is recommended at next deploy.

# Part 2: Player experience (the level-40 cliff)

## Renewable resurrections

Lives now regenerate. Two paths, both online-permadeath mode only (single-player death still uses
the Veil-of-Death penalty menu and never consults the resurrection counter):

- **One life back at every 10th level.** Reaching level 10, 20, 30, and so on restores one lost
  resurrection, capped at the character's maximum (normally 3). Applied at the single level-up
  chokepoint so every path (auto-level, Level Master, grouped dungeon combat) grants it exactly
  once per decade crossing. The grant is announced at the next clean screen boundary.
- **The Temple's Rite of Return.** A new `[U] Rite of Return` service at the Temple restores one
  lost life for steep, level-scaled gold (10,000 + level squared x 100: about 50,000 at level 20,
  100,000 at level 30, 170,000 at level 40 -- roughly 45 fights of gold income in the mid-30s, so
  death now costs something recoverable instead of something irreplaceable). One rite per 20
  real-world hours per character (wall-clock cooldown, relog-proof, persisted in the save). The
  option is hidden while lives are full, and the purchase saves immediately so a crash cannot eat
  a six-figure life.

The death screen now says so: after a resurrection is consumed, a hint line explains both recovery
paths, so the countdown reads as a setback instead of a death spiral.

Design intent: the resurrection economy changes from an absorbing chain (every character
mathematically guaranteed to run out) into a renewal process. Careful players trend life-positive;
death still stings (a life or a pile of gold) but no longer compounds toward inevitable deletion.

## Death's Door (burst protection)

Telemetry showed every real player death in the retained window was a one-round burst: 100-140% of
max HP in a single round, most often a boss right after its opening damage cap expired. The
existing Last Stand rule (started the round above 50% HP -> cannot be reduced below 1 HP) expired
exactly when needed, because attrition had already pulled the player into the 25-50% window before
the burst landed.

New rule: **once per combat**, a killing blow against a player who started the round above 25% max
HP leaves them at 1 HP instead ("Death's Door") and the fight ends with them alive -- staggering
out at 1 HP to heal, restock, and choose their next move. The guarantee is spent for that combat;
a later burst in the same expedition kills for real, and a player who engages a round below 25% HP
has accepted the risk, exactly as before. Attrition difficulty is unchanged; only the
no-counterplay one-round deletions are gone. Skipped in Nightmare difficulty and PvP (same as Last
Stand) and in exhibition combat (Gauntlet, Tournament of Honor, pit fights) and arrest combat --
exhibition waves are each a fresh combat, so an unguarded once-per-combat rescue would have reset
every wave and quietly bypassed the Gauntlet's deliberate death-roll stakes (caught by the
combat-reviewer audit and fixed before ship; those surfaces never consume a resurrection anyway).
Grouped parties see a broadcast line when it fires.

## Visible risk

Three legibility changes so danger is informed instead of ambush:

- **Lives in the status bar.** In online permadeath mode the standard status line now shows
  `Lives: N/M` everywhere, color-coded (green at 2+, yellow at 1, bright red at 0). Previously the
  counter was only visible on death screens.
- **Floor danger ratings.** Entering a dungeon floor above your level now prints a rating on the
  floor banner -- Caution (+1-2 levels), Dangerous (+3-5), DEADLY (+6 or more) -- and a compact
  colored tag (e.g. `DEADLY +8`) rides the in-dungeon status bar for every room, covering all
  floor-change paths (entry, stairs, floor select).
- **Boss burst telegraph.** The round after a boss's first-3-rounds damage cap expires (the exact
  moment most deaths happened), the fight prints "{boss} stops holding back!" so the damage spike
  is announced instead of a surprise. Broadcast to grouped parties too.


## Part 2 numbers (expected)

With renewable lives, the expected 20->40 journey (~900 fights, ~6-10 deaths) is survivable: the
crossing itself grants +2 lives at levels 30 and 40, the Temple sells recovery at a real but
attainable price, and Death's Door removes the deaths that came purely from damage variance.
Acceptance metrics to re-check 2-3 weeks after deploy (queries in the analysis doc): level-40+
crossings per week (was ~0), involuntary deletions per week (was ~3), population at 0-1 lives
(was 11 players), 30-day retention of the level-20+ cohort.

## Part 2 localization

24 new keys in all 5 languages (en/es/fr/it/hu): Death's Door flavor + broadcast, boss telegraph,
status-bar lives label, 3 floor danger lines + 3 compact tags, 12 Temple rite strings, the decade
grant announcement, and the death-screen recovery hint. Hungarian keeps all format args in
suffix-free positions (numeric args and nominative subjects).

## Files Changed

Part 1 (NPC behavior):
- `Scripts/Core/GameConfig.cs` -- Version 0.65.6
- `Scripts/Systems/WorldSimulator.cs` -- `RecordSurvivalLesson` helper (new SurvivedDanger memory type)
  + calls at all solo/Tier-A/team death and near-death-flee sites; predictive-death flee in the solo
  abstract loop and the team combat loop (team sums the top-2 strongest monsters to model concentrated
  fire); explicit player-team guard in `NPCTeamDungeonRun`; train candidate gate uses the real taxed
  gym cost via `CityControlSystem.CalculateTaxedPrice`; `NPCGoShopping` two-shop visit + fruitless
  marker; Brain v2 cohort backfill in `StartSimulation` (fresh worlds)
- `Scripts/Core/GameEngine.cs` / `Scripts/Systems/WorldSimService.cs` -- `IsAIDriven` forced true at
  both NPC restore sites (the live-roster construction points; see review note above)
- `Scripts/AI/MemorySystem.cs` -- new `MemoryType.SurvivedDanger` (appended; serialized by name, so
  save-compatible)
- `Scripts/AI/EmotionalState.cs` -- SurvivedDanger maps to 4 hours of fear
- `Scripts/AI/NPCCombatSimulator.cs` -- `ShouldFlee` takes the monster list and adds the
  predictive-death check using the sim's exact damage math (defense buffs, damage reduction included;
  top-2 monster sum)
- `Scripts/AI/BrainV2Scorer.cs` -- fruitless-shop 6-hour cooldown; gear-gap boosts gated to the
  shop-relevant level band (<= 12); combat-caution layer reads SurvivedDanger alongside Attacked
- `Tests/BrainV2ScorerTests.cs` -- gear-gap test pinned to the shop band; new differential
  above-band no-boost test; new fruitless-shop suppression test

Part 2 (player experience):
- `Scripts/Core/GameConfig.cs` -- `RiteOfReturnBaseCost` / `RiteOfReturnCostPerLevelSquared` /
  `RiteOfReturnCooldownHours` constants + `GetRiteOfReturnCost(level)` helper
- `Scripts/Core/Character.cs` -- Death's Door transient fields + second rescue tier in
  `LastStandCheckAndApply` (exhibition/arrest excluded); `CaptureRoundStartHP` clears the per-round
  flag; decade resurrection grant in `RaiseLevel` (the single Level++ chokepoint) +
  `PendingResurrectionGrants` announce counter; new persisted `LastRiteOfReturnUtc`
- `Scripts/Systems/CombatEngine.cs` -- Death's Door per-combat reset at combat start; rescue branch
  in `HandlePlayerDeath` picks Death's Door vs Last Stand flavor + broadcast (tier-colored); boss
  burst telegraph in the round loop; recovery hint on the divine-intervention resurrection message
- `Scripts/Systems/PermadeathHelper.cs` -- recovery hint on the online death resurrection message
- `Scripts/Locations/BaseLocation.cs` -- pending decade-grant announcement in the location loop
  (fires regardless of which path leveled the player); `Lives: N/M` in the status line (visual +
  screen reader branches, online permadeath mode only)
- `Scripts/Locations/DungeonLocation.cs` -- floor danger rating on the floor entry banner; compact
  danger tag in the in-dungeon quick status bar
- `Scripts/Locations/TempleLocation.cs` -- `[U] Rite of Return` menu entry (visual + screen reader),
  `case "U"` dispatch, `CanShowRiteOfReturn` gate, `ProcessRiteOfReturn` handler with wall-clock
  cooldown, cost display, confirmation, immediate save
- `Scripts/Systems/SaveDataStructures.cs` / `Scripts/Systems/SaveSystem.cs` / `Scripts/Core/GameEngine.cs` --
  `LastRiteOfReturnUtc` save round-trip (PlayerData field, write site, restore site)
- `Tests/SaveRoundTripTests.cs` -- `PlayerData_RoundTrip_PreservesLastRiteOfReturnUtc` (854 tests pass)
- `Localization/{en,es,fr,it,hu}.json` -- 24 new keys each
- `DOCS/PLAYER_EXPERIENCE_ANALYSIS.md` -- the analysis that drove Part 2

Part 1 changes verified by the npc-system-reviewer agent; its four findings (Attacked-type reuse would
have spawned phantom revenge-goal news, startup-only backfill was unreachable on the MUD server,
single-monster estimates understated group-fight risk, flat tax margin could still under-gate the gym)
were all fixed in this release before shipping. Part 2 changes verified by the combat-reviewer and
save-state-reviewer agents (see "Part 2 audit outcome" above).
