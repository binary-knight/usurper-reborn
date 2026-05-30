---
name: project-npc-brain-v2
description: Comprehensive overhaul plan for NPC AI system. Replaces weighted-Markov picker with utility AI + goal stack + plan execution, wires up the inert NPCBrain that's been dead-code since Phase 21, adds real CombatEngine for triaged NPCs, optional LLM moments (online-only, server-key, cost-bounded). 4-6 shippable slices.
metadata:
  type: project
---

# NPC Brain v2 -- Comprehensive Overhaul Plan

## 0. Honest diagnosis

Six parallel agent audits (game-designer, npc-system-reviewer, save-state-reviewer, combat-reviewer, relationship-system-reviewer, code-explorer) converge on three findings.

**Finding 1: there are already TWO NPC AI systems and one is dead code.** `WorldSimulator.SimulateStep` at line 352 calls `npc.Brain.DecideNextAction(worldState)`, then line 356 calls `ProcessNPCActivities(npc, world)` which runs the weighted-Markov picker (lines 2124-2400+) and dispatches the actual action. The Brain output is captured into a local `action` variable, passed to `ExecuteNPCAction`, and almost nothing happens because `ExecuteNPCAction` handles a small enum (Idle, Explore, Trade, Socialize, Attack, Rest, Train, JoinGang, SeekRevenge) that doesn't map to the real verbs (shop, train, levelup, team_dungeon, dark_alley, bank). **The Brain "decides" then the picker overrides.** The Brain's goal stack, memory feedback, personality routing, and Phase-21 enhanced behaviors all exist but are unconsumed.

**Finding 2: the weighted picker is a tuning bog because there's no commitment.** Every release we tune one weight. There's no "I'm pursuing X over multiple ticks". Every tick is independent. NPCs don't gear up because the picker has no signal for "I'm under-equipped, prioritize shop". NPCs don't train meaningfully because train is a 0.5% lottery. NPCs don't recover from a bad combat because the next tick rolls fresh weights with no memory of what just happened.

**Finding 3: the substrate already supports Brain v2.** Personality (14 traits), MemorySystem (write-side wired, read-side sparse), EmotionalState (decays correctly, but the picker ignores it), GoalSystem (capped at 30 goals, persists through save round-trip, but no consumer), lineage fields (v0.63.0), telemetry table (`npc_decision_log` with `is_ai_driven` column explicitly added for A/B), 5-site NPC save round-trip with v0.63.2 BaseWeapPow/BaseArmPow fixes. The plumbing is there. The decision engine isn't.

**Implication:** Brain v2 is mostly a wiring exercise on top of an inert substrate plus a real combat sim plus targeted LLM moments. It's not a from-scratch year-long rewrite.

## 1. Architecture

**Utility AI with goal stack and plan execution.** Three layers:

**Layer A: Goal stack.** Each NPC carries 1-5 active goals at any time. Goals come from: archetype seeds (a thug wants to dominate territory, a merchant wants to amass wealth), personality-driven needs (high-Greed NPC wants 5000g, high-Ambition wants next class tier), life events (KilledMyParent memory promotes a revenge goal, marriage promotes a "protect spouse" goal), faction membership (Shadows member wants standing, Faith member wants pilgrimage), age (young NPC wants level-up, old NPC wants legacy). Goals decay if not progressed and abort on impossibility (target died, level requirement out of reach).

**Layer B: Utility scorer.** Replaces the weighted-random picker. For each candidate action (existing 17 verbs survive), compute a utility score from: top-goal alignment, current need state (HP, mana, gold, gear gap), personality preference, current emotional state, time of day, world state (open/closed buildings, events), recent outcomes (don't repeat what just failed). Pick the highest scorer, NOT a weighted-random draw.

**Layer C: Plan execution.** Some goals decompose into ordered steps ("reach Lv 30" = train, train, fight, fight, shop, level-up). The NPC commits to the plan across ticks and only re-plans when (a) blocked, (b) outcome diverges from expected, (c) a higher-priority goal preempts (revenge target appears in same room). Plan state persists per-NPC.

**Personality remains the soul.** All 14 traits (Aggression, Greed, Courage, Loyalty, Vengefulness, Impulsiveness, Sociability, Ambition, Trustworthiness, Caution, Intelligence, Mysticism, Patience, plus the 10 romance traits) feed Layer B's utility scoring. The current `ApplyPersonalityWeights` shape becomes "personality preferences" that boost goal alignment, NOT raw action probability.

**Worked example: Lv 8 cowardly-Greedy Magician NPC.**
- Old system per tick: roll weighted picker, ~12% chance dungeon, dies on a tough floor pull, respawns naked, lather rinse repeat. Telemetry shows hundreds of decisions, zero progression.
- New system: top goal "Own 5000g" (Greed-derived), sub-goal "Reach Lv 12 to learn Fireball", sub-goal "Avoid combat below 80% HP" (Courage-derived). Day 1: utility scorer picks `train` (highest goal alignment for level-up sub-goal, NPC has gold). Day 2: HP at 95%, picks `dungeon` for floor 6 (level minus 2). Survives. Day 3: HP at 60%, top goal "Avoid combat" pre-empts, picks `inn` to rest. Day 4: back at full, gold low, picks `dark_alley` (Greed-aligned, low risk). Pattern emerges: 1-2 dungeon runs per week between shopping and training trips, gear accumulates, level rises.

## 2. The 5-layer pipeline

When `SimulateStep` reaches an NPC:

1. **Perception update.** Refresh derived state: HP%, mana%, gold tier, gear-gap-vs-peers, current location, recent memories within 24h, dominant emotion, active jealousy/grudges. Cheap. ~10 us per NPC.

2. **Goal maintenance.** Tick down goal priorities, prune completed/impossible goals, promote any pending memory-driven goals (a `KilledMyParent` memory from yesterday promotes a revenge goal today). Inject new goals from life events. ~50 us per NPC.

3. **Action scoring.** For each of the 17 candidate verbs, compute utility = `goalAlignment * w1 + needSatisfaction * w2 + personalityPreference * w3 + emotionalModulation * w4 - recentFailurePenalty * w5`. The 5 weights are tunable. The picker still exists as the entry point but now consults this scorer instead of rolling. ~100 us per NPC.

4. **Plan commitment.** If a multi-step plan is in progress and the top action matches the plan's current step, advance. If utility-scored top action diverges from plan, either abort plan (if abort condition met) or override plan (if higher-priority goal). ~20 us per NPC.

5. **Action dispatch.** Existing `NPCVisit*` and `NPCExplore*` methods get called. Outcome feedback hook fires after the call returns. Outcomes update memory and emotional state, which feeds back into next tick's scoring. ~varies, dominated by combat.

Total Brain v2 overhead per NPC per tick: ~200 us excluding combat. At 250 NPCs that's 50 ms of new compute per tick. Comfortable inside a 60-second tick budget.

## 3. Goal types

Six goal categories, each with concrete examples and existing-system tie-ins:

| Category | Examples | Source | Existing infra |
|---|---|---|---|
| Survival | Heal to 80%, escape dangerous floor, find safe haven | HP gate, current activity | NPCVisitHealer, NPCExploreDungeon flee gates |
| Economic | Own 5000g, sell loot, upgrade gear, reach gear-tier-3 | Greed trait, gold tier, peer gear gap | NPCGoShopping, NPCVisitMarketplace, NPCVisitBank |
| Progression | Reach Lv N, learn ability X, earn proficiency | Ambition trait, class identity | NPCVisitMaster, NPCTrainAtGym |
| Social | Find partner, marry X, raise child, reconnect with grown kid | Sociability + Commitment, romance traits, lineage | EnhancedNPCBehaviors, NPCVisitLoveStreet, FamilySystem |
| Faction/Status | Climb Crown rank, ascend to king, gain Renown/Dread | Ambition, alignment band | NPCVisitCastle, NPCVisitTemple, FactionSystem |
| Vengeance/Grievance | Revenge for parent, confront affair rival, settle gang grudge | KilledMyParent memory, Enemies list, Jealousy | FamilySystem.HasGrudgeAgainst, MemorySystem, NPCAttacks |

**Goal sourcing rules:**

- Every NPC gets 1-2 archetype-seeded goals at spawn (already happens in `NPCBrain.InitializeRelationshipBehavior` etc., just unconsumed).
- Personality crosses thresholds promote goals: Greed > 0.7 + Gold < target -> Economic goal, Ambition > 0.6 + Level cap not reached -> Progression goal.
- Memory events promote goals on next tick: KilledMyParent memory + grudge target alive -> Vengeance goal at high priority.
- Faction membership promotes a standing-climb goal at moderate priority.
- Aging promotes legacy goals at high age (Patriarch/Matriarch tier from v0.63.0 already exists for players; NPCs get the parallel goal "see my grown children prosper").

## 4. Gear and training loops (closing the broken loops)

The current system can't close these because the picker has no concept of need. Brain v2 closes them:

**Gear loop:**
1. Perception step computes `gearGap = (peerAverageWeapPow * 0.8 - npc.WeapPow)`. If positive, the NPC is under-geared for their level cohort.
2. Economic goal "Upgrade gear" gets priority = gearGap normalized.
3. Action scorer ranks `shop` and `marketplace` high while gearGap > 0.
4. `NPCGoShopping` (rewritten to score items by stat-per-gold value) actually buys an upgrade.
5. Equipped item updates WeapPow/ArmPow; gearGap drops; goal demotes.

**Training loop:**
1. Perception step computes `trainGap = (currentSkillProficiency target for level - currentProficiency)`. If positive, train is wanted.
2. Progression goal scores `train` and `levelup` over move/inn/dark_alley.
3. `NPCTrainAtGym` runs; proficiency increases.
4. After hitting `Experience >= GetExperienceForLevel(Level+1)`, levelup action wins next tick.
5. Level-up advances baseline gear (v0.63.2 already grants +5 WeapPow / +4 ArmPow per level), grows the gearGap loop.

These loops are not "smart" by themselves. They're loops where each step reduces the signal that drove it, instead of the current system where signals are ignored and decisions are random.

## 5. Combat: real CombatEngine for triaged NPCs

The combat-reviewer agent confirmed real CombatEngine is feasible at 50-80 fights per 60-second tick if pacing is stripped and an AI brain replaces input prompts. The current abstract sim (`Math.Max(level*6, Strength + WeapPow - Defence)`) is fast (~1 ms) but lies about gear, abilities, and potions.

**Tiered approach.** Not every NPC gets real combat:

- **Tier A (real CombatEngine):** Named NPCs, immortals, kings, court members, NPCs on a player's team, NPCs with a player-visible reputation tag, NPCs flagged "story-relevant" by emergent role. Target: ~50 NPCs.
- **Tier B (abstract sim, current path):** Everyone else. The fast sim still runs.
- Triage signal computed at perception step.

**Engine refactor required.** ~25 `terminal.GetInput` sites and ~25 `PressAnyKey` sites need a `_simMode` short-circuit plus `INPCCombatBrain.PickCombatAction(state)` injection. Existing teammate AI (`ProcessTeammateActionMultiMonster`, `TryTeammateHealAction`, `TryTeammateOffensiveSpell`, `TryTeammateClassAbility`) is the right substrate; it already operates on `Character`. Combat engine refactor is mechanical, ~2 weeks.

**Boss safety:** keep Tier A NPCs OFF Old God floors. `BossContext` guards layered boss-protection paths; if NPCs trip those, the divine-armor caps tuned for player gear will fire and break the fight. Brain v2 routes NPCs strictly to `MonsterGenerator.GenerateMonster(level, isBoss: false)`. Solo dungeon floors with regular floor-bosses (IsBoss=true via `MonsterGenerator`) are fine because no `BossContext` is set.

**Broadcast suppression:** boss-kill broadcasts (`RoomRegistry.BroadcastActionLocalized` at line 6308) and permadeath eulogies fire on combat outcome. Tier A NPC combats need a sim-mode flag to suppress these or the chat channels flood.

## 6. LLM moments (online-only, server-key)

**Constraint set (per user clarification):**
- LLM is online-mode only. Single-player and BBS modes use heuristics exclusively.
- Self-hosters bring their own API key. Key lives on the server, never on a client.
- Per-server config surface: admin sets key, can disable entirely.
- LLM is NEVER load-bearing. Every LLM moment has a heuristic fallback that still produces sensible output.

**Provider abstraction.** New `LLMProvider` interface with implementations for OpenAI, Anthropic (Haiku), local (Ollama for self-hosters without paid API access). Server config picks the provider; if none configured, all LLM moments fall through to heuristic.

**Cost-bounded budget.** Per-server daily token cap (sysop-configurable, default conservative like 500k tokens/day). When cap hits, LLM moments downgrade to heuristic until reset. Cap is per-server, not per-player, so abuse is bounded.

**Five LLM moment categories (cheap, asynchronous, cached):**

1. **Milestone narrative beats.** NPC marries, has a child, sees parent die, is ascended to court, kills a notable target. Generate 1-3 sentence personal narrative using NPC's name, archetype, personality, recent memories, and the milestone. Input: ~200 tokens. Output: ~80 tokens. Heuristic fallback: existing templated news entries.

2. **Personality summary on first contact.** When a player first examines an NPC at Team Corner or in dialogue, generate one paragraph "what this NPC is like" from personality + archetype + recent activity. Cached on NPC indefinitely; only regenerates on major personality shift. Input: ~300 tokens. Output: ~120 tokens. Heuristic fallback: stat-derived band labels.

3. **Daily news rendering for top events.** World sim emits structured news events (NPC X married NPC Y, faction war started, world boss defeated). LLM passes a small batch (10-20 events) once per day and returns flavored news lines in batch. Input: ~1000 tokens. Output: ~600 tokens. Heuristic fallback: existing templated NewsSystem strings.

4. **Death epitaphs for named NPCs.** When a tracked NPC permadies, generate a 1-2 sentence eulogy from their life history. One-shot per death; cached on the deleted_characters archive. Input: ~250 tokens. Output: ~60 tokens. Heuristic fallback: existing templated permadeath broadcast.

5. **Player-NPC relationship reflection.** Once per week per (player, top-3-relationship-NPC), generate a short "what NPC X thinks about you" reflection visible in dialogue UI. Cached for 7 days. Input: ~300 tokens. Output: ~120 tokens. Heuristic fallback: numeric relationship-level descriptors.

**Cost envelope at 200-NPC live server, fully active LLM:**
- ~30 milestones/day at 280 tokens = 8.4k tokens
- ~50 personality summaries/day at 420 tokens = 21k tokens
- ~1 news batch/day at 1600 tokens = 1.6k tokens
- ~3 epitaphs/day at 310 tokens = 0.9k tokens
- ~20 reflections/day at 420 tokens = 8.4k tokens
- Total: ~40k tokens/day. At Haiku rates: under $1/day. At Sonnet: under $5/day.

The daily cap (500k tokens default) is roughly 10x typical usage. Heavy days don't blow budget.

**What LLM is NEVER used for:**
- Per-tick decisions (cost cliff, latency cliff)
- Combat ability picks (must be deterministic, sub-millisecond)
- Save-affecting state mutations (LLM is purely decorative output)
- Anything required for the game to function

## 7. Save and migration

Per save-state-reviewer audit, Brain v2 adds ~10 KB per NPC across goal records (~8 KB capped at 20 goals), combat history (~1.2 KB capped at 10 fights), plan state (~0.4 KB), and social-graph addition (~0.1 KB). At 250 NPCs that's ~2.4 MB added to world_state.npcs (currently ~15 MB).

**Migration patterns by field:**

| Brain v2 field | Pattern | Why |
|---|---|---|
| Typed goal records | Heal-on-load via archetype seed | Empty stack breaks the scorer; reseed from `InitializeGoals` on first load |
| Combat history | Default-on-load (empty list) | Fights repopulate naturally |
| Plan state | Hybrid (default null, seed on first tick) | Needs world state, not save state |
| Preference cache | Default-on-load, recompute | Pure derived from PersonalityProfile |
| Social-graph fingerprint | Backfill pass | Walks `CharacterImpressions`, computes tags |

**Critical constraints:**
- All Brain v2 state lives ON the NPC. No new MUD-singleton state. The catalog of goal templates and scoring functions are static read-only.
- Per-(npc, player) state (grudges against specific players) goes in `Dictionary<string, X>` fields on the NPC itself, NOT in a player-scoped singleton (RomanceTracker pattern is the trap).
- World-sim merge-back at `WorldSimService.cs:299-379` currently only re-applies pregnancy and equipment after a version-bump reload. Brain v2 goal/plan/history fields need to be added to the capture-and-merge list, OR the player-session write path needs to route through `TryAtomicUpdate` (preferred but requires careful audit).
- Rollback safety: every Brain v2 field must be "safe to lose and re-derive". Pre-v0.63.x reads new saves with `UnmappedMemberHandling = Skip` (verify before ship), so additive fields are forward-compatible.

**Test budget:** ~11 new round-trip tests (~400-600 LOC), same density as `NPCData_RoundTrip_PreservesLineage`.

## 8. Telemetry and A/B rollout

The `npc_decision_log` table already has the `is_ai_driven INTEGER DEFAULT 0` column (added explicitly for Brain v2 A/B per `SqlSaveBackend.cs:677`). Add 8 columns via the established idempotent ALTER TABLE pattern: `goal_id`, `goal_type`, `plan_id`, `plan_step`, `decision_reason`, `score`, `runner_up_action`, `runner_up_score`.

**A/B rollout:** Brain v2 ships behind a per-NPC flag (`Character.IsAIDriven`). At deploy, 0% of NPCs are on Brain v2. Sysop raises the percentage. Telemetry comparison: same `npc_decision_log` table, same dashboard, filter on `is_ai_driven = 1` vs `= 0`. The 14-day comparison cycle the v0.63.2 release used becomes the standard rollout cadence.

**Success metrics:**
- Win rate on team_dungeon climbs from current ~0% to >20% for Tier A NPCs.
- Net gold/NPC/day flips from negative to positive for Lv 10+ NPCs.
- Level-ups per 14 days rise from 138 to 600+.
- death-to-progression ratio drops from 70:1 to under 15:1.
- Player-noticeable: at least one "I remember when X killed my dad and now they're hunting them" or "Y has been climbing toward king for weeks" event per week.

## 9. Player-facing impact

**Within a day of play:** named NPCs use real abilities and potions in combat (visible in dungeon when grouped with them). NPCs have gear that visibly improves over time (Team Corner examine shows it).

**Within a week of play:** specific NPCs feel like recurring characters. Their location pattern reflects goals (the Lv 18 Magician who keeps showing up at the Magic Shop and Level Master, the bounty hunter who keeps showing up wherever you've been). Memorable encounters: an NPC's grown child seeks them out at Inn; an NPC widow remarries; an NPC openly pursues a grudge.

**Within a month:** the world has narrative arcs. Court politics shift because court NPCs have personal goals. The marriage market actually moves. Some NPCs become "famous" in the news because their goal pursuits produce milestone-worthy events.

**Headline screenshot for release notes:** "She remembers." NPC walks up to player. Recognition flavor: "You killed my mother. I've been looking for you." Combat triggers. NPC name, mother's name, and date of the kill are all real from save data.

## 10. Anti-patterns / not in scope

- **No simulated sleep cycles, mealtimes, or biological needs.** NPCs already use a time-of-day modifier; that's enough.
- **No NPC-to-NPC letter writing or out-of-band communication.** Information spreads via existing witness/gossip systems.
- **No procedural job system or economy simulation.** NPCs have goals, not careers.
- **No per-tick LLM calls.** Ever. Triggered/cached only.
- **No replacement of the personality system.** 14 traits stay, they get smarter consumers.
- **No replacement of the memory system.** It works, add readers not writers.
- **No new save format.** Additive fields with rollback safety.
- **No player-controlled NPC scripting.** This is server-side AI, not a player customization surface.

## 11. Phased rollout (5 slices)

**Slice 1 (1 week, observable): Wire up the inert Brain.** No new code. Make `NPCBrain.DecideNextAction` actually drive the dispatcher. Extend `ExecuteNPCAction` from 8 to 17 cases mapping to existing `NPCVisit*` methods. Delete the redundant `ProcessNPCActivities` call OR keep it as a fallback. Telemetry comparison: are the goal-driven decisions different from the picker's? Even before utility AI, this exposes whether the existing Phase-21 goal logic produces any signal. Risk: low. Reversible: trivially.

**Slice 2 (2 weeks): Utility scorer replaces weighted-random.** Build Layer B. Goals from Slice 1's seed logic feed the scorer. Picker becomes "score all 17 verbs, pick top". A/B against a control cohort. Measure: progression rate, gear-up rate, win rate. Risk: medium (scoring weights need tuning).

**Slice 3 (3 weeks): Goal stack with persistence and life-event promotion.** Layer A becomes real. Memory events promote goals (KilledMyParent -> revenge, FamilyMemberBorn -> protect, milestone level -> next-tier). Save fields land. Migration heal-on-load fires. Risk: medium (save round-trip discipline, MUD-singleton contamination class).

**Slice 4 (3-4 weeks): Real CombatEngine for Tier A NPCs.** Combat engine sim-mode refactor. INPCCombatBrain. Triage logic. Telemetry: damage/round, ability usage, potion usage by NPC. Risk: highest (touches the combat-system hot path; combat-reviewer flagged broadcast suppression and `HandleVictory` player-only reward path forking as non-trivial).

**Slice 5 (2 weeks): LLM moments (online mode only).** Provider abstraction. Server-side key config. Five moment types wired with heuristic fallbacks. Daily token budget cap. Risk: low (LLM is decorative; everything degrades gracefully).

**Total estimated effort: 11-13 weeks.** Each slice ships independently and produces measurable telemetry change. If any slice underdelivers, the prior slice's heuristics still drive behavior.

## 12. Risk register

| Risk | Likelihood | Mitigation |
|---|---|---|
| Brain v2 feels WORSE than dice (predictable, less variety) | Medium | A/B from day one; revert flag per-NPC if metrics regress |
| LLM cost surprise on self-hosted server | Low | Per-server token cap, default conservative, sysop override required to raise |
| Combat engine refactor breaks player combat | High if rushed | Sim-mode flag is additive; refactor extracts NEW methods, doesn't modify existing player paths in place |
| Save bloat blows world_state.npcs past safe size | Low | Caps enforced (20 goals, 10 combat history); audit logging on world_state writes |
| MUD-singleton contamination on new Brain v2 state | Medium | All per-NPC state on NPC; no new fallback-singleton patterns; review-gate any Instance.X writes |
| Player-tier NPCs going through `HandleVictory` accidentally apply spouse-bonus from active player (singleton leak) | Real (called out by combat-reviewer) | Fork `HandleNpcVictory` cleanly; don't retrofit guards |
| Boss broadcasts spam from Tier A NPC combat | Medium | Sim-mode suppression flag; suppress unless NPC is on a player's team or in player's room |
| Rollback after Brain v2 ships | Low if disciplined | All fields additive; verify `UnmappedMemberHandling = Skip` before ship; Brain v2 fields safe to lose |
| Race-extinction floor or pop-cap shifts as death rate changes | Medium | Telemetry on race counts during rollout; tune floor if needed |
| Player attachment to current "random and weird" NPC behavior | Low | Behavior gets more coherent, not less interesting; current behavior is bad-random, not good-random |

## 13. Key file anchors

Substrate to keep:
- `Scripts/AI/NPCBrain.cs` (excavate, don't replace)
- `Scripts/AI/PersonalityProfile.cs` (read-only consumer extension)
- `Scripts/AI/MemorySystem.cs` (add readers)
- `Scripts/AI/EmotionalState.cs` (add reader in utility scorer)
- `Scripts/AI/GoalSystem.cs` (add reader in scorer, extend)

Chokepoints to modify:
- `Scripts/Systems/WorldSimulator.cs:348-365` (the per-tick dispatcher)
- `Scripts/Systems/WorldSimulator.cs:2124` (`ProcessNPCActivities` chokepoint)
- `Scripts/Systems/WorldSimulator.cs:3550` (`TelemetryWrap` -- add outcome callback)
- `Scripts/Systems/CombatEngine.cs:445` (`PlayerVsMonsters` -- add sim-mode extraction)

Save sites (all 5):
- `Scripts/Systems/SaveDataStructures.cs:788` (`NPCData`)
- `Scripts/Systems/SaveSystem.cs:1150` (SP write)
- `Scripts/Core/GameEngine.cs:6173` (SP read)
- `Scripts/Systems/OnlineStateManager.cs:1226` (online write)
- `Scripts/Systems/WorldSimService.cs:1254` (online read)

Telemetry:
- `Scripts/Systems/SqlSaveBackend.cs:664-680` (`npc_decision_log` schema)
- `Scripts/Systems/SqlSaveBackend.cs:2054` (`LogNPCDecision` writer)

New files (Slice 5 only):
- `Scripts/Systems/LLMProvider.cs` (provider abstraction)
- `Scripts/Systems/LLMNarrative.cs` (the 5 moment types)
- Server admin UI surface for key config

## 14. What to write next

Before any implementation:

1. Sign-off on Slice 1 scope (the inert-Brain excavation). Smallest possible commit, biggest possible information gain.
2. Decision on Tier A criteria for combat triage (Slice 4 inputs). Need exact predicates before combat refactor begins.
3. Decision on per-server LLM config surface (sysop console additions). Mockup for the admin screen.
4. Spike on `CombatEngine` sim-mode extraction (1-2 day spike to confirm the cost estimate before committing Slice 4).

Reach for [[game-designer]] when fleshing out new goal templates per archetype. Reach for [[npc-system-reviewer]] before any new field commits. Reach for [[save-state-reviewer]] before Slice 3 ships (the migration is the recurring landmine).
