# Release Notes - v0.64.1 (The Brain)

Two changes on top of v0.64.0.

## Pre-release audit pass (5 parallel reviewers, ~25 findings fixed)

Before cutting this release, the full v0.64.1 changeset was audited by the project's four specialized reviewers (combat-reviewer, npc-system-reviewer, relationship-system-reviewer, save-state-reviewer) plus a general pass over the LLM layer and localization. The fixes, grouped by severity:

**Release blockers fixed:**
- **OpponentSpared punished as a loss at 6 PvP call sites.** Every pre-existing caller treated `!= Victory` as defeat: sparing a pit-fight opponent cost 20% of carried gold + a recorded loss; sparing the last gang-war enemy forfeited the entire war with a public defeat broadcast; sparing the beaten king printed "YOU WERE DEFEATED." New `allowSurrender` parameter on `PlayerVsPlayer` suppresses the surrender prompt in staged-fight contexts (pit fights, gang wars, throne challenges, bounty hunts); the open-world sites that keep surrender (dormitory/inn sleeping attacks) gained explicit `OpponentSpared` outcome branches so the spare narration isn't followed by a contradictory "they fought you off" line.
- **Real teammate combat deaths were being swallowed.** `CombatEngine.HandleNpcTeammateDeath` deliberately bypasses the `IsPlayerTeam` protection before calling `MarkNPCDead`, but not the `IsInConversation` protection -- and this release guarantees every dungeon-party NPC carries that flag. The guard healed the legitimately-killed teammate to MaxHP/4 and skipped the entire death cascade, while CombatEngine independently set `IsDead=true`: an inverse zombie. The engaged flag is now suspended around the call exactly like Team.
- **The spouse-death notification was inverted.** It ran AFTER `HandleSpouseBereavement` cleared the deceased's marriage flags (so its own gate made it dead code on every permanent death), and it ran unconditionally after the temp-death respawn branch (so players got "your spouse has died" mail for spouses who respawn in ~10 minutes). Now fires before bereavement inside the permadeath branch + the aging path only, and skips NPC-NPC marriages (spouse name resolving to any pool NPC) so name collisions with player display names can't trigger false widow mail.
- **The avenge-loop fix needed a third pass.** The audit found the AddGoal completed-goal dedup was structurally hollow (UpdateGoals prunes completed goals at the top of EVERY tick, not just at the MaxGoals cap) and the per-instance throttle HashSet reset on every online world reload (minutes apart on a busy server), not just restart. Replaced with a process-wide static `SettledRevengeTargets` registry keyed `owner|target`, consulted at BOTH goal-add time (stops the loop at the source) and the news-fire seam. A revenge goal that would be born-complete (target confirmed dead in the pool) is refused outright. The LLM strategic-goal callback also no longer mutates the goals list from its background thread -- it enqueues to a `ConcurrentQueue` drained on the tick thread (a swallowed-exception/list-corruption race at ~244 NPCs x 4 refreshes/day).
- **`Goal.TargetCharacter` now round-trips.** It was never serialized (harmless decoration in v0.64.0; load-bearing in v0.64.1 for Slices 13/14b/19/20 + revenge completion detection). A restored in-flight Avenge goal had a null target -- a zombie that could never complete and silently killed the target-keyed features after any reload. Added to `GoalData` + both writers + both restore sites; additive, pre-existing saves default to empty.
- **Slice 20's player-self guard was dead code, and quest claim failures leaked.** The guard compared `Character.ID` values that are never populated for players, so an NPC whose Avenge goal targets the PLAYER would offer a kill quest against a same-named NPC bystander. Replaced with name matching against the player's identities, applied in both the option builder and the handler. Separately, `ClaimQuest`'s result was ignored: a refusal (level too low, daily cap, royals) still printed "accepted!" and left an unclaimed, claimable-by-anyone quest in the shared database. The result is now checked, the quest is restricted via `OfferedTo` as defense in depth, and refusals show an honest failure beat.
- **LLM caches were not thread-safe.** All six new per-NPC caches were plain `Dictionary<string,string>` on shared NPC objects -- two sessions conversing with the same NPC concurrently could corrupt the dictionary, and the cache reads sat OUTSIDE the try/catch so the exception would propagate into the conversation flow instead of falling back to the template. All six converted to `ConcurrentDictionary`, initialized at declaration.

**Should-fix issues fixed:**
- `IsInConversation` releases now also clear the LIVE ActiveNPCs object (v0.57.2 orphan pattern) at the dungeon-exit finally and both mid-dungeon remove sites; the sync re-assert flags the live twin too. Pre-fix, a mid-dungeon world reload left the live object permanently protected (immortal + un-interactable) because every release path wrote only to the party's orphaned reference -- and the new reload-preservation would then re-assert the leak forever.
- Target steering now routes through `NPC.UpdateLocation` (the raw field write skipped the LocationManager presence registry, accumulating ghost entries per steer), and refuses to steer NPCs who are engaged with a player or themselves in the Dungeon.
- The online quest writer (`SerializeCurrentQuests`) now copies `TargetNPCName` -- without it, the world_state copy of DefeatNPC bounties couldn't track its target across a server restart.
- The reaction-style LLM generators (romance reactions, news comments, NPC flirts, quest requests) had reused the goal-greeting system prompt, whose "the player is the named target of your strategic goal... a threat, a plea" framing was factually wrong for a compliment-thanks and directly contradicted the quest-request user prompt. New neutral `SystemPromptSpokenLine`.
- The Slice 19 hail was silently dead for any online player whose account name differs from display name: the greeting cache was written under a Name1-first key but read back Name2-first. Keys aligned.
- `SanitizeLLMOutput`'s hardcoded 300-char cap silently overrode the 500/600-char caps the topic-response and quest-request callers applied afterwards (mid-sentence truncation at 297 chars). The cap is now a parameter.
- Four pre-existing `flirtCountThisSession` double-counts removed (married-refusal, taken-refusal, and both affair branches incremented in-handler on top of the caller's increment, burning 2 of the 5-per-session flirt budget each).
- NPC-initiated flirts now skip asexual players, skip NPCs whose `SpouseName` is the player (id-drift name fallback, v0.57.7 pattern), and married low-Commitment initiators get an honest "you ARE married, but your commitment wavers" prompt instead of speaking as singles.
- The Spare/Finish prompt accepts localized affirmatives (Y/S/O/I per the v0.57.7 precedent) so a Hungarian "I" (igen) no longer executes the NPC the player meant to spare; the spared NPC's combat statuses + persistent poison are scrubbed and they heal to MaxHP/4 (a leaked DoT was a delayed, unattributed kill that silently undid the mercy).
- The affair-fork decline choice text told the LLM "the affair ends here" while the code keeps the affair alive -- text matched to mechanics.
- Slice 15's bounty news no longer claims the NPC "claimed the bounty" (it stays on the player board by design); "Quest Hall" added to the NPC location mapper; one em-dash in a debug log replaced per project convention.

**Second should-fix pass (remaining audit items):**
- **TTL on the engaged-NPC reload preservation.** A hard crash mid-dungeon (no finally) can leave `IsInConversation` stuck true, and the new reload-preservation would have immortalized the leak. The preservation now tracks when each name was FIRST seen engaged and stops carrying the protection across reloads after 2 hours -- far longer than any dungeon run, short enough that a leaked flag self-heals.
- **Confession at the lover cap is no longer a permanent dead end.** Pre-fix, an accepted confession that hit `MaxConcurrentLovers` still set `HasConfessed` + `ConfessionAccepted` with no Lover record and no retry path (the Confess option gates on not-yet-confessed) -- even after the player freed a slot, that NPC could never be formalized. Now a cap rejection doesn't consume the confession: neither flag is written, and the player can free a slot and confess again.
- **No more false public revenge claims against players.** A revenge goal whose target never appears in the NPC pool (player killers, transient hostiles) auto-completes by design -- but the celebration broadcast "X took blood for blood from Y" was a false, public claim against a possibly-living player the NPC never touched. Celebrations now fire only when the target is CONFIRMED dead in the pool; unconfirmed targets complete silently (goal closes, internal catharsis fires, no news).
- **Flirt-success reactions respect warmth tiers.** The LLM cache bucket is now `success_mild` / `success_medium` / `success_strong` -- pre-fix a single "success" bucket meant the first landed flirt's cached line replayed verbatim for later, deeper successes.
- **Seven hardcoded English romance lines loc-keyed** in all 5 languages: the married/taken flirt refusal fallbacks (the ONLY lines non-LLM sessions ever see) and five affair-conversation narration beats (tearful "I choose you", hand-squeeze, pulls-away, hesitates, looks-away).
- **Daily cap on spare-mercy alignment farming.** New `SparesToday` counter (full MurdersToday-style save plumbing) caps the +10 paired-good alignment reward at `MaxAlignedSparesPerDay = 3`; sparing itself is never blocked, only the alignment pump stops -- consistent with every other alignment lever (murders 3/day, tribute 3/day, Love Street 2/day).
- **Slice 11's dialogue-flavor cache converted to ConcurrentDictionary** -- the v0.64.0 original locked its in-flight dedup set but read/wrote the cache dictionary itself unguarded, the same concurrent-corruption exposure the six new caches had.

**Verified clean by the audit:** XP-multiplier path parity (no double application across single/multi/grouped victory paths), spare-path reward/kill-hook isolation, combat-transient field isolation from saves, all six LLM caches JsonIgnore + naturally excluded by the DTO save pattern, SQL parameterization on the new username resolver, all 27 new loc keys present in all 5 languages with matching format args and zero em-dashes, Electron-mode ordering in ShowGreeting, fork fallback range safety, and the train-weight tuning numbers.

## Two new player-facing dramatic forks (Slice 12b extension)

Slice 12b shipped the LLM-arbitrated dramatic fork pattern with one wired site: marriage proposal NPC response. v0.64.1 adds two more, both player-facing, both mirroring the marriage proposal shape (compute heuristic choice from existing roll, route through `LLMMoments.DecideForkAsync` with the deterministic fallback). LLM is consulted only on a one-shot UI moment the player is already waiting on; latency budget ~2-3s; deterministic fallback always works when LLM is disabled / single-player / BBS / timeout / parse error.

**Confession of love fork** (`fork_confession`, 3-branch) -- when the player picks "Confess feelings" in a Visual Novel conversation, the LLM weighs the NPC's Romanticism trait, attraction to player's gender, current relationship depth, and charisma modifier to pick reciprocate / need-time / reject. Replaces the existing single roll against `successChance`.

**Affair leave-spouse fork** (`fork_affair_leave_spouse`, 2-branch) -- when the player asks an affair partner to leave their spouse and commit to the player, the LLM weighs the NPC's Commitment trait, current affair depth, secret-meeting count, and spouse suspicion level to pick leave / decline. Replaces the existing single coin flip on what is one of the highest-stakes narrative moments in the romance system (it breaks an existing marriage).

Both forks fire roughly as often as marriage proposal (one-shot per player-NPC encounter), so combined cost is below 10 LLM calls per day at the current scale -- negligible. Telemetry surfaces under the new `fork_confession` and `fork_affair_leave_spouse` moment types on the LLM Moments dashboard.

## Brain v2 Slice 13: target-aware dispatch

Slice 12a shipped LLM-generated strategic goals -- 1-3 long-arc life goals per NPC, regenerated every 6 hours, populating `npc.Brain.Goals` for the scorer to consult. The output looked dramatic on the dashboard ("Crush Lucinda Foxglove utterly", "Expose Alaric Ashwick's crimes publicly", "Reconcile with Lady Morgana"). But the actual in-game effect was thin: the `BrainV2Scorer` only consumed the goal's TYPE bucket (Combat / Social / Personal / Economic / Exploration), which biased the NPC toward a verb family. A "Crush Lucinda" Combat goal made the NPC visit the dungeon more often -- where they fought random monsters, not Lucinda. The specifics in the goal name were dashboard flavor, not behavior.

Slice 13 closes the loop. The `Goal.TargetCharacter` field has existed since the family-revenge work in Slice 3, but until now only `Avenge`-family goals populated it (set by family-memory promotion) and only the completion-detection layer read it (via `IsTargetCharacterDead`). Two changes wire it through end-to-end for LLM-generated strategic goals:

**LLM prompt broadened** -- `SystemPromptStrategicGoals` previously asked for `target` only on revenge/romance goals. Broadened to encourage targets on revenge, romance, rivalry, mentorship, protection, exposure, surpassing-a-legend, etc. -- anything goal that genuinely centers on a specific named NPC. Goals that are truly target-less ("build a shrine", "master arcana") still get empty string. JSON parser already extracted the field; this just changes what the model writes into it.

**New pre-pick steer pass** in `BrainV2ProcessActivities`. New `TryTargetSteerToTarget(npc)` runs after the brain state update but before candidate building. If the priority goal has a `TargetCharacter` and that target resolves to a living NPC via the new `FindLivingNPCByName` helper (case-insensitive over Name2 / Name1 / Name), the dispatcher probabilistically steers the NPC to the target's current location. Steer probability scales with goal priority -- priority 0.9 = 36% chance per tick, priority 0.5 = 20%, priority 0.3 = 12% -- so a single tick almost never pursues but over many ticks the NPC accumulates time near the target.

Goal-type-keyed flavor stamps an appropriate `CurrentActivity` and adds an emotional impact:
- **Combat** goal -> "shadowing {target} with hostile intent" + Anger
- **Social** goal -> "seeking out {target} to talk" (if Reconcile/Protect/Friend) or "watching {target} from across the room" (otherwise) + Hope or Envy accordingly
- **Personal** goal -> "keeping watch over {target}" + Hope
- **Other** -> "trailing {target}" + Confidence

Skipped automatically when: target is dead, target is in the Dungeon (verb has its own combat/floor-pick semantics), target is at no resolvable location, the NPC is already at the target's location, or the NPC is a settler (Settlement snap-back would undo the move anyway).

Telemetry logs as `action=target_steer` with `outcome=goal:{GoalType}` so dashboard pivots can isolate the new behavior. Visible in two places: the dashboard recent-decisions feed will show `target_steer` rows, and players will see the steered NPC show up in the "Also here:" list at locations they wouldn't otherwise frequent.

Net effect: a Brain v2 NPC whose strategic goal stack contains "Crush Lucinda Foxglove utterly (Combat 0.82)" will, on roughly 1 in 3 of their post-LLM-update ticks, physically appear wherever Lucinda is -- the Inn, the Castle, Love Street -- with their CurrentActivity reading "shadowing Lucinda Foxglove with hostile intent" and an Anger emotion stamped on them. Dashboard goal text becomes literal in-game behavior. No new LLM cost (the strategic goal generation already pays for the names); pure mechanical use of an LLM output that was previously decorative.

Settler snap-back, dungeon-floor-skip, dead-target skip, and same-location skip all prevent the steer from breaking existing behavior or producing nonsense.

## Brain v2 Slice 14: LLM-generated topic responses

Slice 11 (in v0.64.0) added per-NPC LLM-rendered flavor lines decorating templated dialogue: mood prefixes, memory callbacks, witness allusions, etc. The DECORATION is personalized; the underlying topic responses (when the player picks "Tell me about your life goals" from a chat menu) are still templates that read the same across every NPC of the same class. Slice 14 begins the migration from decorated-template to LLM-replaced for the highest-value relational topics.

This release wires FIVE topics through a new `LLMMoments.GenerateTopicResponseAsync`: `life_goals`, `origins`, `family`, `friends`, `romance_views`. When a player picks any of these from an NPC chat menu, the LLM plays the NPC and writes a 2-4 sentence in-character spoken reply grounded in that specific NPC's actual goal stack (including LLM-designed strategic goals from Slice 12a, with named targets from v0.64.1's Slice 13 work), personality traits (Romanticism / Commitment / Sociability / Compassion / etc. per topic), class, archetype, and relationship level to the asking player. A cold acquaintance gets a guarded evasion; a spouse-tier confidant gets a revealing answer.

Per-(NPC, topic, player) cached on success in a transient `NPC.LLMTopicResponseCache` dict. The first ask of a topic by a player pays the LLM latency (~2-3s typical). Subsequent asks of the same topic by the same player return the cached line instantly. Cache is JsonIgnore so saves stay clean and post-restart caches rebuild naturally on first contact.

**Latency hidden by concurrent UI pause.** `HandleChatOption` kicks the LLM call BEFORE the "considers..." narration line and the 500ms `Task.Delay`, then awaits the task AFTER the delay. The pause runs concurrently with the HTTP round-trip, hiding up to ~500ms of perceived latency on the first ask. On cached re-asks the task is near-instantly complete so the await is a no-op.

Falls through to the existing templated response on every failure path: LLM disabled, single-player, BBS, timeout, parse error, exception. Caller always gets a non-null string to print. Slice 11 dialogue-enhancer flavor still layers on top of either response.

New `IsLLMTopicResponseEnabled(topicId)` whitelist in `VisualNovelDialogueSystem` controls which topics route through the LLM path. Topics outside the whitelist (class-specific topics like warrior_training, mage_magic, town news, weather, hobbies) keep the existing templates -- they're not relational enough to need LLM personalization. Whitelist is one-line extensible as more relational topics emerge as worth promoting.

Telemetry surfaces under `moment_type='topic_response_{topicId}'` (so `topic_response_life_goals`, `topic_response_origins`, etc.) on the LLM Moments dashboard. Cost scales with player exploration (how many distinct NPCs they ask about each of these 5 topics), not with population, since each (NPC, topic, player) tuple is one-shot.

## Slice 13b: target-steered NPCs become visible to the player

Slice 13 (above) made NPCs with a `TargetCharacter` goal physically steer toward the target's location -- a `Crush Lucinda` NPC would actually show up at the Inn where Lucinda was. But Lucinda saw nothing in the UI; the steered NPC just appeared in the generic "Also here" list mixed in with everyone else. The behavior fired correctly; the player had no signal.

Slice 13b emits a tone-keyed line on location entry for each NPC at the current location whose top strategic goal targets the entering player. Color and flavor key off the goal's type:

| Goal type | Tone | Color | Example line |
|---|---|---|---|
| `Combat` | hostile | red | "Magnus Hawkridge watches you with naked hostility from across the room." |
| `Social` + (Reconcile / Protect / Friend / Court / Bond) | welcoming | bright cyan | "Lyra Ashwick catches your eye and looks like they want to talk." |
| `Social` (other -- Expose, Surpass, Crush) | watchful | yellow | "Magnus Hawkridge watches you closely, weighing something behind their eyes." |
| `Personal` | guarded | gray | "Petra Greystone keeps a quiet, careful eye on you." |
| Other | neutral | gray | "Erland Northgate is watching you intently." |

Pure mechanical read of state already populated by Slice 12a (LLM strategic goals populate `Goal.TargetCharacter`) and Slice 13 (steers NPC to target's physical location). No new save data, no LLM cost, no LLM call -- the LLM-generated goal name and target are already on the NPC's goal stack.

Gated on online mode (strategic goals are LLM-driven and only populated for online Brain v2 NPCs). Skipped in single-player and BBS. Failure swallowed at the entry point so a broken NPC state can't break location entry.

5 new loc keys (`base.target_npc_hostile` / `_watchful` / `_friendly` / `_guarded` / `_neutral`) translated across all 5 languages (en/es/fr/it/hu). Hungarian keeps `{0}` in suffix-free nominative-subject position per [[feedback_hungarian_suffixes]].

The user-facing experience: a Brain v2 NPC with goal "Crush Lumina Starbloom utterly (Combat 0.82)" steers (Slice 13) to the Inn where Lumina is, and when Lumina returns to the Inn she sees the red line "Magnus Hawkridge watches you with naked hostility from across the room." Slice 13's invisible behavior becomes a visible threat. The dashboard `target_steer` rows finally pay off in-game.

## Slice 14b: goal-aware greetings

Slice 13b made it visible to the player that an NPC at the current location is targeting them ("Magnus watches you with naked hostility from across the room"). Slice 14b closes the conversational loop: when that targeted player actually clicks Talk to the NPC, the NPC's GREETING line is generated by the LLM in the voice of someone who has a reason to remember this specific player -- not the standard relationship-tier templated greeting that every NPC of that tier uses.

A `Crush Lumina Starbloom utterly` NPC might open with "You. I've been waiting." A `Reconcile with Magnus Hawkridge` NPC might open with "I wasn't sure you'd come find me. Sit. Please." A `Surpass Spud Northgate's legend` NPC might open with a loaded remark that reads as a challenge under a courtesy. Personality, goal type, and relationship level all feed the prompt.

Implementation mirrors Slice 14 (topic responses): per-(NPC, player, goalName) cache in `NPC.LLMGoalGreetingCache`. Cache key includes goalName so when the goal cycles (Slice 12a 6-hour refresh, or completion), the next greeting regenerates. Cheap-tier model (Haiku) -- greetings are short flavor lines, not narrative. Falls through to the standard templated greeting on every failure path (LLM disabled, single-player, BBS, timeout, exception). Slice 11 dialogue-enhancer mood/witness/etc layers still apply on top of either greeting.

Telemetry surfaces under `moment_type='goal_aware_greeting'`. Cost shape: one LLM call per (NPC, player) per goal cycle, amortized across all subsequent Talk re-encounters within that cycle. Negligible at the current player count.

The end-to-end loop now reads: Slice 12a generates goal -> Slice 13 steers NPC to target's location -> Slice 13b warns the targeted player visually -> Slice 14b opens the conversation in voice when the player clicks Talk. Each piece works independently with a deterministic fallback; together they make a Brain v2 NPC with a named target feel like a person who walked across town to find you and has something to say.

## Slice 15: NPC bounty questing

The biggest "NPCs don't behave like players" hole closed. Players visit Quest Hall, claim bounties from the board (kill N goblins, clear floor 12, etc), hunt the target, and collect the reward. NPCs never did. They wandered, fought monsters, leveled up, and accumulated gold -- but the QuestHall door might as well not have existed for them.

Slice 15 adds the missing verb. New `quest` entry in `BuildCandidateActivities` (base weight 0.08), boosted by personality traits (Aggression x1.5 -- combat-leaning NPCs hunt; Greed x1.4 -- bounties pay; Courage x1.3 -- brave NPCs answer the board). New dispatch case routes to new `NPCTakeBountyQuest(npc)`:

- NPC moves to "Quest Hall" location
- Reads available board quests via `QuestSystem.GetBountyBoardQuests(npc)` (level-filtered)
- Picks the quest closest to the NPC's level
- Rolls success: 55% baseline, +4% per level the NPC has over the quest's MinLevel (cap +35%), -3% per level the quest is over the NPC. Clamped [15%, 92%] so even a perfect match has a small surprise-failure window and even an outclassed NPC has slim odds
- On success: applies the quest's gold + XP reward (scales on quest's intended level so big bounties pay like big bounties), stamps "collecting the bounty on {target}" activity, adds Pride + Joy emotional state, posts a news entry like "Magnus Hawkridge has claimed the bounty on Bandit Captain!"
- On failure: stamps "returning empty-handed from the hunt" activity, adds Sadness + Anger, no gold/XP penalty (low-friction so NPCs can retry next cycle)

**Read-only, parallel to player quest tracking.** The NPC does not claim the quest into `questDatabase` and does not modify `Occupier` -- players still see the same bounty on the board. This is "narrative theater" sized to ship safely: NPCs feel like they're doing the same activity without breaking player quest tracking. A future slice could promote this to real competition (NPC sets Occupier, quest disappears from player board, takes N ticks to complete) but that needs careful interaction with `QuestSystem.ClaimQuest`'s `Player`-typed signature and is out of MVP scope.

Telemetry surfaces under `action=quest` with the standard outcome classifier reading the gold/XP delta -- successful claims show as `earned` rows; failures show as `completed` with zero delta. Dashboard pivots can isolate the new behavior. News feed shows the same NPC names players already see in /who and the also-here listings, now also collecting bounties alongside human players.

Net effect: the Quest Hall feels populated. A player walking in to claim a bounty might see an NPC there scanning the same board. The news feed mixes player and NPC bounty completions. The world feels less like "players are the only ones doing things." Pure mechanical, zero LLM cost.

## Slice 16: LLM-generated romance / social reaction lines

Three of the most emotionally weighted player-to-NPC interactions were entirely templated, with the same words coming out of every NPC's mouth: complimenting an NPC, provoking an NPC, and getting refused by a married or taken NPC when flirting. Slice 14 covered topic responses (paragraph-length); Slice 16 covers the short reaction lines that fire when the player makes an emotional move and the NPC responds.

Five buckets wired across three handlers via new `LLMMoments.GenerateRomanceReactionAsync(npc, player, interactionType, bucket, situationHint, templatedFallback, ct)`:

| Handler | Buckets | Was |
|---|---|---|
| `HandleComplimentOption` | `compliment / pleased` | Single shared `dialogue.compliment_reply` line for every NPC |
| `HandleProvocationOption` | `provocation / aggressive`, `provocation / dismissive` | Two shared `dialogue.provoke_threat` / `_dismiss` lines |
| `HandleFlirtOption` (married NPC w/ Commitment > 0.7) | `flirt / married_refuse` | Random pick from a 3-line **hardcoded English** array (not even localized) |
| `HandleFlirtOption` (NPC has lover w/ high Jealousy or Commitment) | `flirt / taken_refuse` | Random pick from another 3-line **hardcoded English** array |

For each, the LLM call kicks **before** the existing 500ms narration pause, runs concurrently with it, then awaits after -- the same latency-hiding trick as Slice 14's `HandleChatOption`. First-time interactions pay ~0-500ms of perceived latency (depending on Haiku response speed); subsequent encounters of the same (NPC, player, interaction, bucket) tuple hit the per-NPC cache (`NPC.LLMRomanceReactionCache`) and return instantly.

Cheap-tier model (Haiku) -- these are short spoken reaction lines, not narrative. Cache key is `{interactionType}|{bucket}|{playerKey}` so different players get different in-character reactions from the same NPC, and different emotional buckets (a flirt-refusal vs a provocation-dismissal) cache independently. Falls through to the templated fallback on every failure path (LLM disabled, single-player, BBS, timeout, exception). Slice 11 dialogue-enhancer mood/witness/etc layers still apply on top of either the LLM reaction or the template fallback.

Telemetry under `moment_type='romance_{interactionType}_{bucket}'` (so `romance_compliment_pleased`, `romance_flirt_married_refuse`, etc.) -- 5 distinct moment types on the LLM Moments dashboard.

**Notably this also localizes the two hardcoded English flirt-refusal arrays.** Pre-Slice-16 a Hungarian player getting refused by a married NPC saw 3 hardcoded English options regardless of session language. Now they get an LLM-generated refusal that the prompt instructs in their session-relevant tone (English-only on the prompt side; the templated fallback still falls back to the existing hardcoded English, but the LLM path is the default when active).

The net effect: complimenting a brave warrior vs a guarded sage vs a gold-hungry merchant produces three different "thank you" lines. Provoking an aggressive barbarian vs a calm cleric produces appropriately different fury vs disdain. And a married NPC refusing a flirt now refuses in their voice, with their commitment, naming their actual spouse. Romance interactions feel like they're happening with specific people, not interchangeable templates.

### Slice 17: flirt success + awkward branches

Two more buckets extend Slice 16's flirt coverage. Slice 16 wired the refusal branches (married/taken); Slice 17 wires the much more commonly-encountered positive and neutral branches:

- **`flirt / success`** — fires when the receptiveness roll lands. Warmth level (MILD / MEDIUM / STRONG, derived from existing `FlirtSuccessCount` tier logic) passed to the LLM via `situationHint` so a first-success line reads cautious while a third-success line reads as deepening attraction. The 7 existing templated tier-keyed lines (`flirt.mild_pos` / `flirt.med_pos_{1-3}` / `flirt.strong_pos_{1-3}`) stay as the deterministic fallback for the right warmth tier.
- **`flirt / awkward`** — fires when the receptiveness roll just misses. Three templated `flirt.neutral_{1-3}` lines stay as fallback.

Same cheap-tier (Haiku), same per-(NPC, player, bucket) cache, same latency-hiding (LLM call kicks before the existing 500ms narration pause; awaited after). Negative subtypes (`neg_not_type`, `neg_married_{1-3}`, `neg_persistent`) deliberately stay templated — those are edge cases with stable loc keys; not worth additional cache surface.

After Slices 16 + 17, the flirt handler's full reaction surface is LLM-covered: spouse/lover warm responses still use existing templates (already varied by `flirt.lover_response_{1-5}` keys), positive / awkward / married-refuse / taken-refuse all LLM. Only the narrow "wrong gender / married player / persistent" sub-rejections stay templated.

## Slice 18: PvP NPC surrender mechanic

When a player brought a non-monster NPC to 0 HP in PvP combat, the NPC died. Always. No surrender, no mercy choice, no plead-for-life. One of the most emotionally weighted player actions in the game (the killing blow against someone real) was binary and silent. Slice 18 adds the missing mercy moment.

When the player's attack drops the NPC to 0 HP AND the NPC entered the round with > 35% MaxHP (the killing blow has to come from a position the NPC could plausibly recover from -- "nibble-killed at low HP" doesn't qualify), the NPC's decision routes through a new `combat_surrender` LLM fork: **fight to the death** (0) or **beg for mercy** (1). Heuristic fallback weighs Courage + Aggression + Vengefulness -- brave / aggressive / vengeful NPCs rarely beg; cowardly / cautious NPCs almost always do.

On **fight to the death**: standard death, no UI change. A new flavor line ("X sneers and meets your blade head-on, dying with weapon raised") plays before the standard outcome.

On **beg for mercy**: NPC revived to 1 HP, an LLM-generated in-character begging line plays ("Please... I yield. Spare me, and I will trouble you no more."), and the player gets a 2-choice prompt:

- **[1] Spare**: NPC walks away alive (HP=1), new `CombatOutcome.OpponentSpared` outcome fires. +10 Chivalry (routed through AlignmentSystem's paired movement, so it pulls Darkness down too), +1 relationship to the spared NPC (broken through the friendship cap so it's a real reset, not just smoothing the existing scar), public news entry "X spared Y's life at the [location]." NO XP / NO gold reward -- mercy isn't a kill.
- **[2] Finish**: NPC's HP set back to 0, standard Victory path fires (XP / gold / news / blood-price all as before).

Per-combat one-shot via `Character.HasSurrenderedThisCombat` -- an NPC can't keep begging if the player chose Finish then dealt another lethal blow. New `Character.HpAtRoundStart` captured at the top of every PvP round so the threshold check can distinguish "the killing blow was sudden" from "ground down across many rounds." Both fields are transient (combat-only state, not serialized).

Gated to PvP combat (`PlayerVsPlayer` loop only). Player-vs-monster combat unchanged -- monsters don't have personality vectors to drive the fork. Self-defense vs unprovoked aggression also unchanged -- the surrender mechanic fires regardless of how combat started; the player's choice to spare or finish is the moral weight, not the engine's call.

Telemetry: `moment_type='fork_combat_surrender'` for the NPC decision; `moment_type='romance_combat_surrender_begs_mercy'` for the begging line generation (yes, mis-classified-into-romance-bucket due to reusing GenerateRomanceReactionAsync for the spoken line -- functionally correct but the naming will read odd on the dashboard; cosmetic).

LLM cost: one fork call + one reaction call per qualifying PvP killing blow, both cheap-tier (Haiku). PvP killing blows above the 35% threshold are rare events at the current player count; budget impact negligible.

7 new loc keys translated across all 5 languages (en/es/fr/it/hu). Hungarian keeps `{0}` in suffix-free nominative-subject position per [[feedback_hungarian_suffixes]].

The net effect: PvP combat now has a moral inflection point that wasn't there before. A brave Barbarian opponent will still die fighting; a cautious Sage might drop to their knees and beg. Players who choose mercy build alignment toward Holy and earn a relationship reset; players who finish carry the kill weight. The mechanic is dormant in single-player and BBS (no LLM there, surrender check just returns false in those paths via the LLM gate).

## Slice 19: targeting NPCs hail the player on re-entry

Slice 13b made it visible that an NPC was targeting the player ("Magnus watches you with hostility"). Slice 14b made the NPC's GREETING -- played when the player clicked Talk -- LLM-generated and goal-aware. Slice 19 closes the loop between them: the goal-aware greeting that the player heard once during conversation is now CALLED OUT to them on subsequent entries to the same location, without requiring another Talk click.

Mechanically: `WriteTargetingNPCNotifications` (the location-entry hot path) checks `NPC.LLMGoalGreetingCache` for the priority goal's cached LLM line. If present, it emits a second yellow line right after the threat observation: `Magnus Hawkridge calls out: "You. I've been waiting."` Cache miss is silent -- first-time encounters show only the threat line; the hail appears on the SECOND and subsequent entries (after Slice 14b's first-Talk populates the cache).

Zero LLM cost. Pure read of a dictionary already populated by Slice 14b. No new API surface, no new prompt, no telemetry row -- this is a presentation tweak that makes existing LLM output more visible.

The progression now reads as a natural escalation:
- **First encounter**: player walks in, sees the threat line, sees NPC in the "Also here" list. Targeting is visible but the NPC is silent.
- **First conversation**: player clicks Talk. Slice 14b's goal-aware LLM greeting plays ("You. I've been waiting."). Player engages or backs out.
- **Subsequent re-entries** (Slice 19): player walks in. The threat line shows AND the NPC's previously-generated opener calls out across the room. The NPC's "voice" carries -- they recognize the player and have something to say.

1 new loc key (`base.target_npc_calls_out`) translated across all 5 languages. Hungarian keeps `{0}` (NPC name) in nominative-subject position; `{1}` (the LLM-generated speech) is wrapped in quotation marks so internal punctuation in the LLM output doesn't collide with the host phrase.

## Slice 20: NPC-issued quests

The first slice where the NPC asks the PLAYER for help instead of the other way around. Closes the loop between Slice 12a (LLM-generated strategic goals with named targets) and the player's existing quest infrastructure.

When the player talks to an NPC whose priority strategic goal is Combat-type with a named TargetCharacter that resolves to a living NPC, a new conversation option appears: "Is there something you need? I can help." (yellow, after the standard Personal question). Selecting it plays an LLM-generated in-character ask: the NPC names the target, hints at the grievance, asks the player to hunt them. Player gets `[Y] Yes / [N] Not this time` prompt.

On **Yes**: a real `DefeatNPC` bounty quest is created and claimed for the player (mirrors `QuestSystem.CreateBountyForCriminalNPC`'s shape: scaled difficulty + gold reward, 7-day window, kill objective on the named target). NPC says "When it is done, find me." Relationship +1 (taking up someone's burden is bond-forming). Quest appears in the player's normal quest list and shows up at QuestHall under "Active Quests."

On **No**: NPC nods, disappointment hidden. No relationship hit -- it's a polite ask, polite decline. The NPC may make the offer again on a later conversation if the goal is still active.

**Crucially, quest completion advances the NPC's goal automatically.** Slice 3's `IsTargetCharacterDead` completion detection already watches for the named target to die. The player killing the target satisfies that check, the NPC's Avenge / Crush / Revenge goal completes naturally, and the existing goal-completion handlers fire (catharsis emotion clear, news posts, etc). No special wiring needed -- the chain is already in place; Slice 20 just opens the player-driven path to satisfying it.

**Skipped automatically when:**
- The target NPC isn't alive / can't be found in `ActiveNPCs` (no point offering a hunt the player can't complete)
- The player already has an active quest targeting the same NPC (no double-offer)
- The conversation partner is the player's own adult child (consistent with Slice 14b family handling)
- The NPC has no priority Combat goal with TargetCharacter (most NPCs most of the time)

**LLM details:** New `LLMMoments.GenerateQuestRequestAsync` reuses the `SystemPromptGoalGreeting` "spoken line" prompt shape. Per-(NPC, player, goalName) cached in new `NPC.LLMQuestRequestCache`. Cheap-tier (Haiku). Telemetry as `moment_type='npc_quest_request'`. Falls through to templated fallback on every failure path.

**End-to-end flow now reads as a complete arc:**
1. **Slice 12a** -> LLM gives the NPC the goal: "Avenge Halvar's death at the hands of Magnus Hawkridge"
2. **Slice 13** -> NPC physically steers toward Magnus's location
3. **Slice 13b** -> Player walking in sees "X watches you with naked hostility from across the room"
4. **Slice 14b** -> Player clicks Talk; NPC opens with goal-aware greeting
5. **Slice 19** -> On re-entry, NPC hails: "You again. Have you found him yet?"
6. **Slice 20** -> Player picks "Is there something you need?" -> NPC asks them to kill Magnus
7. Player accepts, hunts Magnus, kills him
8. **Slice 3** -> NPC's Avenge goal completes via `IsTargetCharacterDead`; Anger cleared, news posts cinematic, optional `PostAvengeNewsAsync` LLM moment fires

NPCs and players are now in the same narrative loop: NPCs can ask players for help, players can choose to be the hand of someone else's vengeance, and the world's strategic-goal infrastructure tracks it through to completion. Goal 1 (NPCs play like players) closes a major gap: they now MOVE PLAYERS too, not just react to them.

11 new loc keys translated across all 5 languages (en/es/fr/it/hu). Hungarian keeps all `{0}` / `{1}` placeholders in suffix-free nominative or appositive positions per [[feedback_hungarian_suffixes]] (e.g. `{0} arra kért, hogy vess véget a következő életnek: {1}` -- `{0}` is nominative subject of `kért`, `{1}` is after a colon).

## Slice 21: NPCs comment on world news

The news feed is the game's collective memory: when an NPC dies, a king is overthrown, a world boss falls, a marriage happens, the realm-wide ticker carries it. Players read the news. NPCs lived through the news. Pre-Slice-21 they never SAID anything about it.

When the player initiates a conversation with an NPC, there's now an 18% per-conversation chance that the NPC follows up their greeting with a one-sentence in-character reaction to a recent major news event. The reaction lands as a quiet supplementary beat -- a gray italic stage-direction line ("Magnus lowers their voice, glancing at the door:") followed by a yellow quoted line ("Hear about the king? Strange days. The realm needs steadier hands.").

**Major-event filter** -- only the dramatic news qualifies; mundane "Magnus bought a sword at the Auction House" entries don't trigger commentary. Two-tier filter:

1. Category whitelist: `death`, `boss`, `kingship`, `throne`, `marriage`, `world_event`, `war`
2. Headline keyword scan (defense in depth, since news write-paths don't always set category consistently): `slain`, `killed`, `defeated`, `king `, `queen `, `throne`, `world boss`, `wed `, `married`

Picks the freshest qualifying entry. Skipped if older than 36 hours (stale -- commenting on week-old news reads weird).

**Per-(NPC, news-entry-id, player) cached** in new `NPC.LLMNewsCommentCache`. The same NPC won't re-generate commentary on the same news for the same player across multiple conversations within the news window. Different NPCs commenting on the same news event get their own LLM calls (their personalities shape the take). Different players hearing from the same NPC about the same news also get their own LLM calls (one player might be the killer the news is about; the comment can lean differently).

Cheap-tier (Haiku). Falls through to silent skip on every failure path -- no templated commentary fallback; the conversation just doesn't have the news beat that turn. Telemetry as `moment_type='npc_news_comment'`.

**Online-mode only.** Single-player and BBS skip the news comment path entirely -- there's no shared news stream worth commenting on in single-player; mock-news scrolls only.

The net effect: a player who witnesses or hears about a world event will, over the next few conversations they have with NPCs, hear those NPCs talking about it. Different NPCs offer different angles: a brave Warrior might cheer a boss kill, a cautious Sage might note its implications, a Greedy merchant might wonder what it means for trade. The world feels like everyone is reading the same paper.

1 new loc key (`dialogue.npc_news_aside`) translated across all 5 languages. Hungarian keeps `{0}` (NPC name) in nominative-subject position.

## Slice 22: NPC-initiated romance

The biggest "NPCs are passive" gap in the conversation layer closes. Pre-Slice-22, the entire romance loop ran one direction: player picks Flirt, NPC reacts (Slice 16/17). NPCs never initiated. A single, attracted, warmly-disposed, high-Romanticism NPC could spend years orbiting the player and never make a first move.

Now, when an NPC's greeting fires and they pass the eligibility gate, there's a 14% per-greeting chance they volunteer an opening flirt as a supplementary beat -- a glance held a beat longer than necessary, a compliment that lands warmer than friendship, a question that invites them closer. Player can then respond by picking the standard Flirt option from the conversation menu (Slice 16/17 handles the reciprocate / awkward branches), or simply move on.

**Eligibility gate** -- all must pass:

| Check | Threshold | Why |
|---|---|---|
| Adult-child of player | NOT | No flirting at your kids |
| Romanticism trait | > 0.6 | Below this, the NPC isn't a flirt-initiator personality |
| Attracted to player's gender | Yes | Defaults to orientation matching |
| Relationship level | <= 40 (warm friendship or closer) | Below this, an unsolicited flirt reads creepy not endearing |
| Already player's Spouse / Lover | NOT | They're already together; the Slice 17 lover-tier path handles in-relationship warmth |
| Married NPC with high Commitment | NOT (Commitment > 0.6) | Loyal married NPCs wouldn't initiate (mirrors the Slice 16 married-refusal logic on the receiving side) |

When ALL gates pass AND the 14% roll lands, an LLM-generated opening flirt prints as a dark-magenta narration line followed by a bright-magenta quoted line. Per-(NPC, player) cached so the same NPC's first-move to the same player stays consistent across re-encounters -- "this is how they always greet you, charmingly persistent" rather than rotating openers.

**Personality shapes the move.** The prompt instructs: "a shy NPC ventures softly; a bold NPC swings; a witty NPC teases." A Lv.40 Bard with high Charisma and high Flirtatiousness opens with an outright compliment; a guarded Sage with high Romanticism but low Sociability ventures a hesitant question; a brash Barbarian goes direct.

Cheap-tier (Haiku). Falls through to silent skip on every failure path -- no templated fallback; silence reads better than canned flirts. Telemetry as `moment_type='npc_initiated_flirt'`.

**Net effect:** the romance system stops being one-directional. Players who built warm relationships with high-Romanticism NPCs without ever flirting first will discover those NPCs eventually making the first move. Players who've been ignoring an NPC's flirt-receptive personality will get noticed back. The flirt loop now has two valid entry points -- player-initiated (Slice 17) OR NPC-initiated (Slice 22) -- with the same downstream reciprocation / awkward / rejection branches handling both.

1 new loc key (`dialogue.npc_initiated_flirt_intro`) translated across all 5 languages. Hungarian keeps `{0}` (NPC name) in nominative-subject position per [[feedback_hungarian_suffixes]].

This is the final slice in the v0.64.1 cycle. Stopping here to let the live server breathe and accumulate dashboard telemetry before any further LLM expansion.

## Bug fix: spouse in dungeon party dying while walking beside the player

Player report (Lv.7 Elf Ranger): spouse in the dungeon party showed nearly full health throughout the run, then on returning Home was simply gone -- dead, resurrection screen. Reproduced three times: once on the official server, twice offline. Investigation found three interlocking holes in the world-sim protection for player-partied NPCs:

**Hole 1 -- mid-dungeon party joins were never protected.** Dungeon entry sets `IsInConversation = true` on every NPC teammate (a guard the world sim respects), released in a `finally` on exit. But that sweep runs ONCE, at entry. A spouse/lover/team NPC added mid-dungeon via the `[Y] Party` menu joined the `teammates` list without ever getting the flag -- so the world simulator kept simulating them independently while they walked beside the player: rolling their own dungeon runs (and dying in them), being targeted by NPC-NPC revenge attacks, etc. Fix: `SyncNPCTeammatesToGameEngine` (called after every party mutation) now re-asserts `IsInConversation = true` across the current roster, making it the single chokepoint. Both mid-dungeon REMOVE sites now explicitly clear the flag so a removed NPC isn't stuck protected (and un-interactable) until restart.

**Hole 2 -- online world-state reloads stripped the protection.** `IsInConversation` is transient (not serialized). When another player's save bumps the world-state version, `WorldSimService` rebuilds the entire NPC list from DB -- every new NPC object defaults to `IsInConversation = false`. The player's party holds the old protected object; the world sim processes the new unprotected twin and kills it. The player sees full health all run (their party reference is the orphan); the death becomes visible at Home when the spouse re-resolves against the live list. Fix: the reload-preservation block (same pattern as the existing pregnancy/equipment capture) now captures the engaged-NPC set before `LoadWorldState()` and re-applies the flag after.

**Hole 3 -- the death guard left zombies at 0 HP.** Even when `MarkNPCDead`'s `IsInConversation` guard fired correctly, it only skipped setting `IsDead` -- it left HP wherever the killing blow put it (0). `IsAlive` is computed as `HP > 0`, so the "protected" NPC still read as dead at every downstream consumer (home resolution, party filters, resurrection screen). Fix: the guard now heals to `max(1, MaxHP/4)`, mirroring the adjacent `IsPlayerTeam` branch which always did this correctly.

All three combined explain the report exactly: offline occurrences came from holes 1+3, the online occurrence from holes 2+3.

**Follow-up (same reporter): targeted spouse-death notifications.** When the player's NPC spouse legitimately dies (world-sim kill, NPC duel, old age), the only signal was a generic line buried in the world news scroll -- the player typically discovered the loss via the resurrection screen at Home with zero context. New `WorldSimulator.NotifyPlayerSpouseOfDeath` fires from both death-commit seams (`MarkNPCDead` + the aging-death path, which doesn't route through MarkNPCDead). If the deceased's `SpouseName` resolves to a player: online sends in-game mail from "The Town Crier" (persists; unread-mail notice on next login) via new `SqlSaveBackend.ResolvePlayerUsername`; single-player queues a `PendingNotifications` entry (shown at next location entry). The notice carries who died, the cause/killer, the location, and the UTC timestamp. New `worldsim.spouse_death_notice` loc key in all 5 languages.

## Bug fix: avenge-news re-fire loop (two-stage fix)

Live telemetry showed individual NPCs firing "X has avenged the blood of their kin" news 40-63 times each over four days -- the LLM avenge moment (and its token cost) re-firing endlessly for the same long-dead target. Cause: the Slice 12a LLM strategic-goal generator re-adds "Avenge X" goals on every 6-hour refresh; the goal auto-completes on the next tick (X is still dead) and fires the news again. Two-stage fix: (1) `AddGoal` now refuses to re-add a Revenge-family goal when a COMPLETED goal with the same name + target exists (the original dedup only checked ACTIVE goals, and `Complete()` sets `IsActive = false`); (2) because the `MaxGoals = 30` cap prunes completed goals to make room -- erasing the dedup's memory -- a per-NPC `_recentlyFiredAvengeTargets` set now throttles at the news-fire seam itself: one celebration per (NPC, target) pair, ever. Gossip and emotional catharsis still fire (cheap); only the news entry + LLM call are throttled.

## NPC behavior tuning: train de-weighted

Post-v0.64.0 telemetry showed `train` at 42% of ALL NPC actions -- the compounded result of historical base-weight bumps (0.15 -> 0.25 -> 0.50, each justified at the time by train being too rare) stacking with personality multipliers (Aggression 1.3x / Intelligence 1.4x / Mysticism 1.2x), the morning time-of-day bonus (1.3x), and Brain v2 scorer layers (Personal-goal 1.6x / Combat-goal 1.3x / low-level need 1.3x). Every NPC in the world read as a gym rat. Tuning pass cut the base 0.50 -> 0.22, Ambition contribution 0.15 -> 0.08, and shaved every multiplier (1.15-1.25 range). Post-tune telemetry: train at 25-29%, inn 17%, dark_alley 16%, shop 10% -- a much more believable town.

## Player progression: early-game XP multiplier

The retention data was stark: combat win rates at low levels are 95-100% (difficulty is NOT the problem), but the XP grind was -- reaching Lv 10 took ~200 combats, and engaged players were quitting mid-grind (one player spent 345 hours to reach Lv 14 before giving up; another spent 27 hours and never cleared Lv 2). New `GameConfig.GetEarlyGameXPMultiplier(level)`: 3.0x at Lv 1-5, 2.0x at Lv 6-10, 1.5x at Lv 11-15, 1.2x at Lv 16-20, transparent at Lv 21+. Applied at the end of the XP-modifier chain in all three victory paths (single-monster, multi-monster, per-grouped-player -- each grouped player's own level keys the curve, so a low-level follower benefits even in a high-level leader's party). Gold rewards untouched. Post-deploy telemetry confirmed: a new player reached Lv 13 in 101 combats (pre-boost reference: 103 combats only reached Lv 10), and a returning player went Lv 2 -> Lv 6 in 18 combats / 24 minutes.

## Model tiering (cheap / premium per moment type)

Until now every LLM call -- decorative mood prefixes, fork digit picks, premium strategic-goal narrative arcs -- went to whatever model `USURPER_LLM_MODEL` was set to (currently `claude-sonnet-4-6`). That was wasteful: a dialogue-mood prefix like "*scowling,*" doesn't need Sonnet quality, but ate Sonnet pricing ($3/MTok input, $15/MTok output) all the same.

v0.64.1 adds **per-request model selection**. New `LLMRequest.Model` (string, optional) overrides the provider default when set. New `USURPER_LLM_MODEL_CHEAP` env var lets sysops configure a second model identifier for routing low-stakes calls (e.g. `claude-haiku-4-5-20251001` at $1/MTok input, $5/MTok output -- roughly 1/3 the Sonnet cost). New `LLMSettings.GetCheapModelOrDefault()` returns `CheapModel ?? Model`, so leaving the cheap env var unset is a zero-impact no-op (all calls keep using the premium default).

**Routing by moment type:**

| Moment type | Tier | Why |
|---|---|---|
| `PostAvengeNewsAsync` | cheap | short news flavor, decoration |
| `PostDeathEpitaphAsync` (disabled) | cheap | configured for re-enable; flavor only |
| `GenerateDialogueFlavorAsync` | **cheap** | highest-volume call site (per-NPC mood / memory / witness / state / grief / trait / faction); short decoration lines |
| `DecideForkAsync` | cheap | just picks a single digit |
| `GeneratePersonalitySummaryAsync` | premium | shown to player on `[X]` Examine, narrative weight |
| `GenerateTopicResponseAsync` | premium | full in-character paragraph for player conversation |
| `GenerateStrategicGoalsAsync` | premium | long-arc behavior driver, narrative shape matters |

The cheap-tier sites dominate the call volume (dialogue flavor alone fires per-NPC across all 7 enhancer layers per conversation). Expected cost reduction: ~50-60% on total LLM spend when Haiku is configured, with no perceptible quality drop on the decoration paths.

To opt in, set the env var (no code change needed):

```
USURPER_LLM_MODEL_CHEAP=claude-haiku-4-5-20251001
```

To stay on Sonnet across the board, leave the env var unset.

## Files Changed

- `Scripts/Core/GameConfig.cs` -- Version 0.64.1
- `Scripts/Core/NPC.cs` -- New `LLMTopicResponseCache` transient (JsonIgnore) Dictionary field for per-(topic, player) LLM response caching; new `LLMGoalGreetingCache` transient Dictionary field for per-(goal, player) LLM greeting caching (Slice 14b); new `LLMRomanceReactionCache` transient Dictionary field for per-(interaction, bucket, player) LLM reaction caching (Slice 16)
- `Scripts/Systems/WorldSimulator.cs` -- Removed call to `LLMMoments.PostDeathEpitaphAsync` in `MarkNPCAsDead`; new `FindLivingNPCByName` helper; new `TryTargetSteerToTarget(npc)` pre-pick steer pass; wired into `BrainV2ProcessActivities` before candidate building. New `quest` verb added to `BuildCandidateActivities` (base 0.08) with Aggression/Greed/Courage personality boosts; new `case "quest"` in `DispatchVerb`; new `NPCTakeBountyQuest(npc)` method (Slice 15 -- read-only bounty hunting against `QuestSystem.GetBountyBoardQuests`, level-matched success roll, news post on win)
- `Scripts/Systems/LLMMoments.cs` -- Broadened `SystemPromptStrategicGoals` to encourage `target` on more goal types (revenge / romance / rivalry / mentorship / protection / exposure / surpassing-a-legend); new `SystemPromptTopicResponse` constant; new `GenerateTopicResponseAsync(npc, topicId, player, templatedFallback, ct)` method with per-(topic, player) caching, topic prompts for 5 relational topics (`life_goals` / `origins` / `family` / `friends` / `romance_views`), and `topic_response_{topicId}` telemetry. New `SystemPromptGoalGreeting` constant; new `GenerateGoalAwareGreetingAsync(npc, player, goalName, goalType, templatedFallback, ct)` method with per-(goal, player) caching and `goal_aware_greeting` telemetry (Slice 14b). New `GenerateRomanceReactionAsync(npc, player, interactionType, bucket, situationHint, templatedFallback, ct)` method with per-(interaction, bucket, player) caching and `romance_{interaction}_{bucket}` telemetry (Slice 16). Cheap-tier model routing added to `PostAvengeNewsAsync`, `PostDeathEpitaphAsync`, `GenerateDialogueFlavorAsync`, `DecideForkAsync`, `GenerateGoalAwareGreetingAsync`, `GenerateRomanceReactionAsync` via `LLMSettings.GetCheapModelOrDefault()`; premium calls (PersonalitySummary, TopicResponse, StrategicGoals) untouched
- `Scripts/Systems/LLMProvider.cs` -- New `LLMRequest.Model` optional override field; `HttpChatCompletionsProvider.CompleteAsync` uses `request.Model ?? _model` for the per-call model selection
- `Scripts/Systems/LLMSettings.cs` -- New `CheapModel` property reading `USURPER_LLM_MODEL_CHEAP` env var; new `GetCheapModelOrDefault()` helper returning `CheapModel ?? Model` for safe opt-in behavior
- `Scripts/Systems/VisualNovelDialogueSystem.cs` -- `HandleConfessionOption` rewritten with `fork_confession` LLM fork (3-branch: reciprocate / need-time / reject); `HandleAskToLeaveOption` rewritten with `fork_affair_leave_spouse` LLM fork (2-branch: leave / decline); both keep the deterministic roll as fallback. New `IsLLMTopicResponseEnabled(topicId)` whitelist with 5 relational topics; `HandleChatOption` routes whitelisted topics through `LLMMoments.GenerateTopicResponseAsync` and overlaps the LLM await with the existing 500ms "considers..." pause to hide latency. `ShowGreeting` now checks for a priority goal targeting the conversation partner and, if matched, replaces the templated greeting with `LLMMoments.GenerateGoalAwareGreetingAsync` (Slice 14b). Slice 16: `HandleComplimentOption`, `HandleProvocationOption`, and `HandleFlirtOption`'s married/taken refusal branches now route through `LLMMoments.GenerateRomanceReactionAsync` (cheap-tier Haiku, per-(NPC, player, interaction, bucket) cache, latency hidden behind existing 500ms narration pauses). Also localizes/replaces the two hardcoded English flirt-refusal arrays that pre-Slice-16 always rendered English regardless of session language
- `Scripts/Locations/BaseLocation.cs` -- New `WriteTargetingNPCNotifications(player, term)` helper called on location entry right after the "Also here" players block. Walks NPCs at the current location, surfaces a tone-keyed line for any NPC whose top strategic goal's `TargetCharacter` matches the entering player. Pure mechanical read of Slice 12a + 13 state; online-only; no LLM cost; failures swallowed
- `Localization/en.json` / `es.json` / `fr.json` / `it.json` / `hu.json` -- 5 new `base.target_npc_*` loc keys per language (hostile / watchful / friendly / guarded / neutral) for the targeting-NPC notifications. Hungarian keeps `{0}` in nominative-subject position per [[feedback_hungarian_suffixes]]
- `DOCS/RELEASE_NOTES_0.64.1.md` -- This file
