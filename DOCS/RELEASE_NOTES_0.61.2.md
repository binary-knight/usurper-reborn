# v0.61.2 -- Beta

Hotfix release on top of v0.61.1. Wires the Storm Eagle's missing signature mechanic, adds self-preservation gates to NPC dungeon runs (solo and team), adds the Last-Stand cap to prevent monsters and gods from one-shotting players from full HP, fixes player-immortal worship being treated as second-class at the Temple, fixes the Pressure Plates puzzle resolving via an Intelligence check instead of asking for the plate sequence, lays Phase 1 telemetry groundwork for a planned NPC-AI conversion, and rebalances NPC action weights based on the first telemetry pass.

## Fix: Storm Eagle was mechanically identical to a higher-stat Dire Wolf

Discovered during a beast-by-beast audit at the end of v0.61.1. The Storm Eagle `BeastData.cs` description claims "Lightning-themed strikes; occasional stun on hit," but the beast had zero references in code outside its definition. `AddActivePetToParty` correctly added the eagle to the party as a 5th-slot combat teammate, and the basic-attack AI ran turns for it, but no species-specific behavior fired -- so the eagle dealt plain physical damage with slightly higher base stats than a Dire Wolf and that was it. The other seven tameable beasts (Forest Hawk, Mountain Goat, Marsh Toad, Cave Spider, Tidepool Sprite, Bog Wisp, Dire Wolf) had their advertised passives verified live in the same audit.

**Fix.** Two changes to deliver the description's promise:

1. **`Character.PetSpeciesId` field.** Pet wrappers built by `AddActivePetToParty` now carry the underlying `Pet.Id` (e.g. `"storm_eagle"`, `"dire_wolf"`) on the wrapper itself, separate from `Name1` / `Name2` which hold the player-given pet name. This lets species-specific combat behavior fire even if the player renamed their pet.

2. **Lightning + stun proc in `ApplyPostHitEnchantments`.** When the attacker is a pet with `PetSpeciesId == "storm_eagle"` and the hit is a non-spell melee strike, two extra effects fire after damage lands:

   - **Lightning chip damage.** 10% of the swing's damage as a bonus shock, applied directly to monster HP. Flavor message: "Lightning forks from {pet name}'s wings into {target} for +{bonus} damage."
   - **Stun proc.** 15% chance to route a 1-round stun through `TryStunMonster`. That helper enforces the boss resist roll, diminishing returns across the fight, and the post-recovery immunity window that all the existing in-game stun sources go through, so a Storm Eagle owner can't chain-lock a boss any more than a Magician casting Stun could.

Stun-proc message: "The thunderclap leaves {target} reeling, stunned."

Five-language localisation (en/es/fr/it/hu) for both flavor lines.

**Net behavior.** Storm Eagle is now meaningfully different from Dire Wolf in combat. Dire Wolf still hits harder per swing (its `CombatBaseAttack` is the design's "consistent physical damage" pick), but Storm Eagle's lightning chip + stun procs make it the better pet for fights where stuns matter (multi-monster rooms, mini-bosses that don't fully resist). The species choice at taming time now actually changes how the pet plays.

## Fix: NPCs no longer suicide-run the dungeon

Player report (screenshot of the world news feed): ten consecutive `slain by Golem / Werewolf / Hobgoblin at the Dungeon` entries, all NPCs, all in rapid succession. Pre-fix, `WorldSimulator.NPCExploreDungeon` had no top-level gate at all: any NPC the activity dispatcher rolled "dungeon" for went, regardless of HP, personality, or whether they had any business there. Three failure modes layered.

**Issue 1: no health gate.** A wounded NPC at 20% HP coming off a previous loss went straight back in.

**Issue 2: dungeon level roll was generous.** The old roll was `npc.Level + random.Next(-3, 4)` (so +/- 3 either way) plus an additional `+0..3` for any NPC with Courage > 0.7 OR Ambition > 0.7. A Lv 20 NPC with one moderately high personality stat could end up fighting a Lv 26 monster, which against an unmodified NPC stat block is often a kill.

**Issue 3: flee logic fired too late.** Old code: flee only when `npc.HP < 30% AND rounds > 2`. So the NPC had to take two full rounds of hits before fleeing was considered, and against a high-STR monster like a Golem two rounds was often the whole fight.

**Fix.** Three coordinated changes in `NPCExploreDungeon`:

1. **HP gate at the top.** NPCs below 70% HP redirect to the Healer instead and never run the dungeon. Activity message reflects the redirect ("tending to wounds").

2. **Personality gate at the top.** NPCs with low Courage AND low Ambition (both below 0.3) have a 90% chance to abort and hang around town instead ("lingering near the safety of the gates"). The 10% slip-through preserves the rare cowardly-NPC-makes-bad-decision flavor.

3. **Tightened dungeon-level roll.** New baseline is `npc.Level - 2 + random.Next(0, 3)` (so npc.Level - 2 to npc.Level). Courage > 0.7 adds +0..1; Ambition > 0.7 adds +0..1. Floor is still clamped at 100. Now a courageous AND ambitious NPC can roll up to npc.Level + 2, not + 6.

4. **Flee threshold lifted to 50%, no rounds gate.** Cowardly NPCs (Courage < 0.3) flee at 60%; brave NPCs (Courage > 0.7) hold to 35%. Base flee chance bumped from 60% to 70% so successful flees actually stick.

Dispatcher in `SimulateNPCActivity` updated to only stamp the "exploring the dungeon depths" activity and Confidence/Fear emotions when the NPC actually went to the dungeon (i.e., when one of the early-return gates didn't redirect them). Pre-fix the flavor message stamped over the redirected activity, claiming wounded NPCs were dungeon-crawling when they were actually at the Healer.

Expected effect: the "slain at the Dungeon" entries in the world feed drop substantially. NPCs still die in the dungeon sometimes (cowards occasionally slip the gate, courageous-ambitious NPCs still push +2, monsters at npc.Level can still drop low-stat NPCs on a bad combat roll), but the rate matches the design intent rather than the screenshot.

## Telemetry-driven follow-up: team_dungeon path was unprotected

The first thirteen hours of the v0.61.2 NPC decision log surfaced an asymmetric failure mode. Solo dungeon (`NPCExploreDungeon`, which got the self-preservation gates above) ran 639 attempts with 84.82% successful flees and only 10.64% deaths -- the gates worked. Team dungeon (`NPCTeamDungeonRun`) ran 518 attempts with **71.43% deaths and zero successful flees**. The gates I added were scoped to the solo method only. The team path still ran the pre-fix logic.

What the team path had wrong:

| Protection | Solo (post-fix) | Team (still broken) |
|---|---|---|
| HP gate at entry | yes (<70% MaxHP redirects to Healer) | none |
| Personality gate | yes (low Courage AND Ambition: 90% abort) | none |
| Level roll | npc.Level - 2 to +2 (Courage/Ambition each push +1) | avgLevel + (-2 to +3) |
| Flee threshold | 50% baseline, Courage-scaled (35% brave, 60% cowardly) | **25% flat** |
| Rounds-before-flee gate | none | **2 rounds of damage first** |
| Base flee chance | 70% | 55% |
| Successful flee cap | 95% | 85% |

Fix: applied the same v0.61.2 pattern to `NPCTeamDungeonRun` and `SimulateTeamVsMonsterCombat`. Leader HP gate, leader personality gate (cautious leaders don't rally the team), tightened level roll (avgLevel - 2 to avgLevel baseline, brave / ambitious leaders push +1 each), flee threshold lifted to 50% (Courage-scaled per member), rounds-gate removed, base flee chance bumped to 70%, cap bumped to 95%. Also added per-leader telemetry hook to `NPCTeamDungeonRun` so the rich outcome strings (`aborted_wounded`, `aborted_cautious`, `won`, `fled`, `died`) flow into `npc_decision_log` the same way `NPCExploreDungeon`'s do, instead of relying on the generic dispatcher classifier.

Expected effect: team_dungeon death rate drops from 71% to something closer to solo's 10-20% range.

## Telemetry-driven follow-up: action picker rebalance

The same first-pass data showed action distribution heavily biased toward wandering rather than progression:

| Action | Count | % of total |
|---|---|---|
| move | 2320 | 17.6% |
| inn | 2247 | 17.1% |
| dark_alley | 1843 | 14.0% |
| team_recruit | 1729 | 13.1% |
| settlement | 1099 | 8.3% |
| temple | 971 | 7.4% |
| heal | 916 | 7.0% |
| dungeon | 639 | 4.9% |
| **train** | **63** | **0.5%** |
| **shop** | **51** | **0.4%** |
| **levelup** | **7** | **0.05%** |

NPCs were spending 60% of their action slots on wandering and 0.05% on actually levelling up unspent XP. The action weights in `ProcessNPCActivities` were tuned for narrative variety but starved progression actions.

Rebalanced:

- **shop** 0.20 -> 0.30 baseline, with `Greed * 0.10` personality bonus. Merchant-soul NPCs shop aggressively.
- **train** 0.15 -> 0.25 baseline, with `Ambition * 0.15` personality bonus. NPCs trying to climb actually go to the gym.
- **levelup** 0.30 -> 0.80 when eligible. If an NPC has enough XP for the next level, they really should take it. Sitting on unspent XP was a clear bad-decision pattern.
- **move** 0.15 -> 0.05. Was the #1 action and produces nothing. Still kept as fallback.
- **inn** 0.15 -> 0.08 base. Sociability and time-of-day bonuses still let it spike when contextually appropriate (evening, sociable NPC, wounded), but it doesn't drown out progression any more.
- **team_recruit (no team)** 0.25 -> 0.12 baseline (gang-affinity bonus 0.35 -> 0.18).
- **team_recruit (in team)** 0.15 -> 0.06. Once in a team, recruiting is a background activity, not a primary one.

Combined with the team_dungeon fix, NPCs should now actually win occasional team dungeons, accumulate XP, head to the Level Master to spend it, level up, head to the shop to upgrade gear, head to the gym to train. The full progression loop instead of wandering forever.

## Fix: player-immortal worship was a second-class citizen at the Temple

Player report: "When you are affiliated with a player god, sacrificing gold to your deity doesn't make faith standing go higher. Also, when you contribute to an altar while you worship a player, and press stats on the sacrificing screen, the game writes you are worshiping noone."

Two related bugs, both rooted in the same blind spot: most Temple code paths read `godSystem.GetPlayerGod(name)` to find the player's current god, which only returns elder-god worship from the `GodSystem` registry. Player-immortal worship is stored in a different field, `Character.WorshippedGod`, and most temple display sites already check both fields with a fallback. Two specific sites missed the fallback.

**`DisplayPlayerStatus` (the `[S] Status` screen inside the sacrifice menu).** Read only `GetPlayerGod`. When the result was empty, fell through to "you worship no god" instead of falling back to `WorshippedGod`. Fix: added the same elder-god-then-player-immortal-then-none ladder used by `DisplayWelcomeMessage` and `DisplayMenu` elsewhere in the same file.

**`SacrificeToImmortalGod` (the `[$]` menu entry that sacrifices gold to a player-immortal).** This function delivered divine experience to the immortal correctly (in-memory if online, DB update otherwise), but never updated the worshipper's Faith faction reputation or Chivalry alignment the way the elder-god sacrifice path does at lines 1466-1495. From the player's perspective: gold went away, the message said "Your offering burns," but no faith standing growth. Fix: added the same `AlignmentSystem.ChangeAlignment` (paired-movement) and `FactionSystem.ModifyReputation(TheFaith, ...)` calls the elder-god sacrifice uses, with the same per-sacrifice cap (`MaxAlignmentGainPerTempleSacrifice`, the v0.60.0 anti-cheese cap that prevents one big donation from maxing a faction).

Player-immortal sacrifices currently route alignment / faction gain to the good-alignment side by default. Most player-immortal campaigns are good-aligned and the immortal's own alignment isn't trivially exposed at this code point; a follow-up could read the immortal's `Chivalry` / `Darkness` and route evil-immortal sacrifices to `TheShadows` if that becomes a real case.

## Fix: Pressure Plates puzzle resolved by an Intelligence-check menu instead of asking for the plate sequence

Player report: "I just pressed think carefully and deduce the answer, option 1, and it autofilled itself. Was this supposed to happen? It wrote the plates so I thought I needed to remember them or something?"

Confirmed mechanical bug, not a UX confusion. `PuzzleSystem.GeneratePressurePuzzle` (line 240) builds a real puzzle: it generates a randomized plate sequence (e.g., `[3, 1, 4, 2]` as the safe-path order), generates wear-pattern hints that reveal that order, sets `RequiresMovement = true`, and stores the solution as a 1-indexed string list on the puzzle instance. But the dispatch switch in `DungeonLocation.HandlePuzzle` at line 13178 only had cases for `LeverSequence`, `SymbolAlignment`, `NumberGrid`, and `MemoryMatch` -- `PressurePlates` fell through to the `default:` case, which routes to `HandleSimplePuzzle`. `HandleSimplePuzzle` shows the generic `[1] Examine carefully and deduce` / `[2] Try random` / `[3] Give up` menu and rolls an Intelligence check on the deduce branch.

Net effect: the player saw the proper "Pressure Plates" puzzle title, the proper "study the wear patterns to determine the safe path" description, and the proper hints listing which plates were worn most -- then got a generic INT-check menu instead of being asked to enter the plate sequence. Pressing [1] resolved via a 40% + INT modifier dice roll regardless of whether they understood the hints. The puzzle was effectively cosmetic.

Fix: added `case PuzzleType.PressurePlates: solved = await HandlePressurePlatesPuzzle(puzzle, player); break;` to the dispatch switch, and added `HandlePressurePlatesPuzzle` modeled on the existing `HandleLeverPuzzle` since both puzzle types use the same solution shape (1-indexed numeric sequence as `List<string>`). Player now enters their answer like "3,1,4,2" matching the wear-pattern hints. New `dungeon.pressure_plates_prompt` localisation key added in all five languages (en/es/fr/it/hu): "Enter the order of plates to step on ({0} plates, e.g., 3,1,4,2):"

## Fix: stat-based achievements unlocked at the wrong moment

Player report: "I triggered a Shopaholic in a dungeon, 50 items from shops?" Confirmed mechanical timing issue, not a data bug. Player had legitimately crossed the 50-purchase threshold and the unlock fired correctly, but the notification appeared mid-combat against a Sprite on floor 17, which looked nonsensical.

Root cause: `AchievementSystem.CheckAchievements` was only called from four places: `BaseLocation.OnSetupLocation` (every location entry), `DungeonLocation` (one site), and `CombatEngine` (post-combat at lines 6647 and 19125). Stat-changing actions like shopping, selling, and levelling up didn't trigger achievement checks directly. The check was deferred to whichever of those four sites fired next. If you bought your 50th potion at the Healer and then immediately entered a dungeon (which starts a combat), the post-combat sweep was the first check that saw the new total -- so the unlock fired during combat.

Fix: added `AchievementSystem.CheckAchievements(player)` immediately after every `Statistics.RecordPurchase` and `Statistics.RecordLevelUp` call. Now threshold-crossing achievements unlock at the moment the action happens. The notification still surfaces via the standard `BaseLocation.ShowPendingNotifications` hook on the next location entry, but since that's typically within a few seconds of leaving the shop (player walks back to Main Street), the player sees the notification while they're still in a shop-related context rather than mid-fight.

Sites instrumented:

- `WeaponShopLocation` (single buy, and "buy all" subset)
- `ArmorShopLocation` (single buy, and "buy all" subset)
- `MagicShopLocation` (accessory purchases)
- `MusicShopLocation` (instruments and songs)
- `HealerLocation` (healing potions, mana potions, antidotes)
- `CastleLocation` (royal-guard gear purchases)
- `DungeonLocation` (dungeon merchant, monk-vendor potion bulk-buy)
- `StreetEncounterSystem` (street-merchant random encounters)
- `LevelMasterLocation` (two level-up paths, post-loop so the check runs once per level-up session not per level)

The check itself is idempotent (`TryUnlock` no-ops if the achievement is already unlocked) and cheap (~30 if-statement comparisons against player stats), so spamming it across all these sites adds no measurable cost. `CombatEngine`'s monk-vendor purchase path was already covered by the existing post-combat check at line 6647, so it wasn't double-instrumented.

## Last-Stand cap: no more bad-luck one-shots from monsters or gods

Design ask: a player should never die from a single round of monster damage if they came into that round at full or near-full HP. Death should be the consequence of a bad decision (engaging while wounded, ate bad soup that day, walked into a fight you couldn't win) never the consequence of a double-crit cascade or a multi-monster pile-on from full HP.

**The rule.** If the player started this combat round above 50% MaxHP, no round of incoming monster / boss / environmental damage can reduce them below 1 HP. A player getting double-critted by a giant or a god from full HP now drops to 1 HP, never to 0. They get one more turn to potion, retreat, use an ability, or pray. If they were already at or below 50% HP when the round began, the rule does not save them -- they took the calculated risk to engage wounded, and the system respects that.

**Why this and not just a per-hit cap.** A per-hit cap leaves multi-monster and multi-strike rounds open: three monsters each hitting for 40% is still a round-one-shot from full HP. The round-start check covers all cumulative cases in one place.

**Implementation.** New `Character.CaptureRoundStartHP()` is called at the top of every combat round (and once before the ambush phase) to snapshot the player's HP. New `Character.LastStandCheckAndApply(isPvP)` runs at the very top of `CombatEngine.HandlePlayerDeath`. If the player just died but qualifies for the rescue (`RoundStartHP > MaxHP/2`, not Nightmare, not PvP), HP is forced to 1, the death-handling cinematic is skipped, and a flavor line renders:

> A desperate moment. You stagger but stay standing.

For grouped sessions, a third-person broadcast goes to followers ("{name} staggers but stays standing.") so the party sees what happened.

**Coverage.** All combat-driven player deaths funnel through `CombatEngine.HandlePlayerDeath` -- single-monster basic attacks, multi-monster pile-on, monster abilities, Old God boss custom mechanics, boss enrage damage, ambush phase, DoT ticks while a round is in progress. One intervention point catches everything. No 25 individual damage-site edits needed.

**Excluded by design.**

- **Nightmare difficulty.** Players who picked the harshest experience get the harsh experience.
- **PvP combat.** Routes through `PlayerVsPlayer`, not `HandlePlayerDeath`, so the cap doesn't apply. Players who attack other players agreed to PvP rules.
- **Players who started the round below 50% MaxHP.** Took the calculated risk to engage wounded. Rule respects the decision.
- **Arrest and exhibition (Gauntlet, pit fight) combat.** Already have their own non-lethal short-circuits in `HandlePlayerDeath`.

**Localised** in all five languages (en/es/fr/it/hu) for both the player-facing line (`combat.last_stand`) and the broadcast variant (`combat.last_stand_broadcast`).

## Groundwork: NPC decision telemetry (Phase 1 of planned NPC-AI conversion)

Setup work for a later release that will convert a subset of named NPCs to LLM-driven player-like behavior. No AI ships in this version. What ships is the measurement infrastructure.

New `npc_decision_log` SQLite table records every world-sim NPC action with before/after state: NPC name, level, class, action ("dungeon" for now, more action types in follow-ups), location before/after, outcome (`aborted_wounded`, `aborted_cautious`, `won`, `died`, `fled`, `stalemate`), gold delta, XP delta, HP before/after, and an `is_ai_driven` flag (always 0 in this version, will distinguish heuristic vs AI cohorts once the AI subset lands). Indexed on (npc_name, created_at desc), (action, outcome), and (created_at desc) so the daily-rollup queries stay fast.

`SqlSaveBackend.LogNPCDecision(...)` writer is fire-and-forget so a logging failure can't break world sim. `PruneOldNPCDecisionLog(daysToKeep=30)` keeps the table bounded; will get wired into the daily maintenance pass.

Wired across all 17 NPC action types via two patterns:

- **Dungeon-specific.** `NPCExploreDungeon` writes its own rows at every outcome exit (six paths: `aborted_wounded`, `aborted_cautious`, `won`, `died`, `fled`, `stalemate`). Kept separate because the per-outcome classification is richer than what a generic state-delta classifier can produce.
- **Generic dispatcher wrapper.** New `WorldSimulator.TelemetryWrap(npc, action, runAction)` helper captures before-state, runs the action delegate, derives an outcome from the state delta (`died`, `leveled_up`, `took_damage`, `healed`, `earned`, `spent`, `completed` in priority order), and logs a row. Wraps the other sixteen dispatcher cases: shop, train, levelup, heal, move, team_recruit, team_dungeon, love_street, temple, bank, marketplace, castle, go_home, dark_alley, inn, settlement. Exception-safe (logs an `"exception"` outcome row before rethrowing if the action method throws), so the wrapper never silently swallows world-sim errors.

Why this lands now even though no AI is shipping yet: the AI subset needs a measured-heuristic baseline to beat. Without 30 days of "this is what the current heuristic does at the cohort level," we can't tell whether the AI is actually better. Telemetry-first matches how the v0.61.1 damage rebalance was justified (30 hours of combat_events showed the v0.61.0 +60% bump wasn't moving win rate, which prompted the comprehensive pass).

Plan documented in `memory/project_npc_ai.md`. Two-tier strategic/tactical architecture (LLM generates a daily plan, existing `NPCBrain` executes tick-by-tick biased toward the plan), 20-NPC subset, online-server only in v1, estimated $2-5/month opex.

## Fix: quest titles, descriptions, and objectives didn't translate for non-English players

Player report: "Quest texts are in English even when I have my game set to Spanish." Confirmed and structurally broader than one quest.

Root cause: every quest's `Title`, `Comment`, `Initiator`, and per-objective `Description` was rendered to a final string at creation time via `Loc.Get(key)`. The pre-rendered strings were then stored on the quest, persisted into save data, and re-displayed verbatim forever. Two contamination paths:

1. **World-bootstrap rendering.** Starter quests (`Royal Council`'s "Crawler Plague," "The Lich King," etc.) and the daily-refreshed king bounties are generated at world startup, when no player session is attached. `SessionContext.Current` is null, so `GameConfig.Language` falls back to English. Every player who later logged in saw the bootstrap-language rendering, regardless of their session language.

2. **Creator-language rendering.** Royal quests issued by NPC kings during world sim, and bounties auto-posted via `RefreshBountyBoard`, were rendered in the session language of whichever player happened to trigger the world-sim tick that created them. Stable until the next refresh.

Net effect: a Spanish player got Spanish menus and Spanish dungeon text but English quest titles ("Crawler Plague" / "The Crown") and English objective descriptions ("Defeat 5 Zombies"). 

**Fix.** Data-model split:

- **New localization-key fields on `Quest`:** `TitleKey`, `TitleArgs`, `CommentKey`, `CommentArgs`, `InitiatorKey`. When non-empty, the display layer renders them at the moment of display in the current viewer's session language. When empty, falls back to the legacy `Title` / `Comment` / `Initiator` string snapshots so saves predating this change still render something sensible.
- **New `Quest.GetDisplayTitle()`, `GetDisplayComment()`, `GetDisplayInitiator()` methods** route through the keys when present. All player-facing display sites updated to use these (`QuestHallLocation` view / claim / abandon / turn-in flows, the quest list modal, Electron quest summaries / detail payloads, the dungeon `[Y] Status` quest readout, `BaseLocation`'s `/health` quest section, `DarkAlleyLocation` active-targets list, `GameEngine`'s "target permadied, quest auto-completed" message, plus `MailSystem.SendQuestOfferMail` / `SendQuestClaimedMail` / `SendQuestCompletionMail` / `SendQuestFailureMail` / `SendQuestFailureNotificationMail` and `NewsSystem` failure announcement).
- **New `QuestObjective.DescriptionKey` / `DescriptionArgs` fields** with matching `GetDisplayDescription()` and `QuestObjective.Localized(...)` factory. Per-objective text ("Defeat 5 Zombies," "Reach floor 25," "Survive at least 10 monsters") now travels with its localization key the same way titles do. All quest-creation sites updated to use the factory: starter quests, king royal quests via `GenerateDungeonQuestObjectives` (all four target cases: `ClearBoss` / `ReachFloor` / `ClearFloor` / `SurviveDungeon`), targeted DefeatNPC bounties, generic criminal bounties.
- **Save / restore round-trip.** `QuestData` and `QuestObjectiveData` in `SaveDataStructures.cs` got the same key/arg fields. Both write paths (`SaveSystem.SerializeQuestList` for player saves and `OnlineStateManager.SerializeCurrentQuests` for world-state) populate them. All three read paths (`QuestSystem.RestoreFromSaveData`, `MergeWorldQuests`, `MergePlayerQuests`) restore them with empty-string-as-fallback for legacy saves.
- **Legacy save migration.** Starter quests use deterministic IDs (`STARTER_*`) that survive across world ticks. `CreateStarterQuest`'s existing "skip if exists" guard now back-populates empty `TitleKey` / `CommentKey` / `InitiatorKey` on the existing quest before returning, so saves from before this release self-heal on the next world bootstrap without resetting any in-flight quest progress or occupier state.

Quests created after this release render in the viewer's language at every display site. Quests in the world from before this release self-heal for starters (the migration path); for king-issued royal quests and bounties they stay in their bootstrap-rendered language until the next refresh cycle replaces them with the new key-aware version (~24 hours at most for bounties, until the king re-issues for royal quests). Saves load cleanly in both directions.

**Known limitations.** When a localization template embeds a sub-string ("WANTED: {0}" with `{0}` = the criminal type), the outer template renders in the viewer's language but the embedded `{0}` is still in the bootstrap language when it came from `Loc.Get(innerKey)` at quest-creation time. Same applies to NPC names embedded in titles (those are real names, not keys). Acceptable trade-off for this pass; fully-nested localization would need a richer arg model.

## Fix: GMCP `Char.Combat.End` reported `PlayerEscaped` when the player died

Player report (MUD client trigger script author): "outcome via gmcp on death is PlayerEscaped". Confirmed.

Root cause in `CombatEngine.HandlePlayerDeath`: after a player dies, if a resurrection fires (online auto-resurrection consuming the Resurrections counter, or the single-player Veil-of-Death menu picking a temple / deal / accept path that restores HP), the code at `CombatEngine.cs:19989` rewrites `result.Outcome` from `PlayerDied` to `PlayerEscaped` with the comment "Continue as escaped rather than died". The rewrite has a legitimate purpose: ten location-side call sites (`BankLocation`, `ArenaLocation`, `InnLocation`, `AnchorRoadLocation`, `MainStreetLocation`, `RareEncounters`, `OldGodBossSystem`, `CombatEngine.HandleFatigueAfterCombat`) check `result.Outcome == CombatOutcome.PlayerDied` to decide whether to apply post-combat death penalties (resurrection-at-temple flow, alignment hits, etc.). After a successful resurrection the player is alive and walking, so those branches must skip -- the `PlayerEscaped` rewrite is the existing mechanism for that.

But the rewrite happens BEFORE the `Char.Combat.End` GMCP emit at `CombatEngine.cs:1746` reads `result.Outcome.ToString()`. So MUD clients listening for `Char.Combat.End` saw `outcome: "PlayerEscaped"` when the player had actually died (and was resurrected). The separate `Char.Death` event was emitted correctly at `CombatEngine.cs:19735` before the rewrite, so clients that watch both events could reconstruct what happened, but trigger scripts keyed on `Char.Combat.End.outcome` got the wrong signal.

**Fix.** Distinguish the C# control-flow signal (`result.Outcome`, which legitimately gets rewritten for the 10 caller checks) from the truth-telling signal that the GMCP layer reports.

- New `CombatResult.PlayerActuallyDied` bool field. Set to `true` at the top of `HandlePlayerDeath` immediately after the Last-Stand cap rescue check, BEFORE any of the early-return short-circuits (`IsArrestCombat`, `IsExhibitionCombat`) or the resurrection rewrites at line 19989. Set there because reaching that point means the player is in the death pipeline (the rescue didn't apply, and the death cinematic / GMCP `Char.Death` emit / permadeath / resurrection prompt are all about to run).
- `Char.Combat.End` emit derives `outcome` from `result.PlayerActuallyDied ? "PlayerDied" : result.Outcome.ToString()`. So whether the player got resurrected or true-died, MUD clients see `outcome: "PlayerDied"` in the round summary, matching the `Char.Death` event that fired earlier in the same combat. The 10 caller-side `result.Outcome == PlayerDied` checks keep working unchanged because the C# rewrite at line 19989 still happens for them.

`PlayerActuallyDied` is also set on `IsArrestCombat` / `IsExhibitionCombat` short-circuits? No -- it's set BEFORE those short-circuits return, so they correctly mark the player as having entered the death pipeline. But those paths intentionally don't run the death cinematic (arrest is non-lethal subdue, exhibition is paid arena loss with HP=1) -- the GMCP outcome will now report `PlayerDied` for those too. Reasonable: the player IS at 0 HP coming into HandlePlayerDeath in both cases, and from the MUD client's perspective "the combat ended with the player at 0 HP" is the same signal whether the consequence is jail or a slap on the wrist.

Last-Stand cap (v0.61.2) handling: the cap rescue early-returns BEFORE `PlayerActuallyDied = true` runs, so `PlayerActuallyDied` stays false for rescued players. But the caller pre-set `result.Outcome = CombatOutcome.PlayerDied` before invoking `HandlePlayerDeath`, so the Last-Stand rescue also now resets `result.Outcome` to `Victory` (if all monsters dead) or `PlayerEscaped` (if monsters still alive but the round loop won't resume), so the GMCP emit reports the truth (player survived) instead of `PlayerDied`.

## Fix: GMCP `Char.Combat.Party` didn't emit an empty frame when the party became empty

Player report (MUD client author): "when no team left gmcp should send an empty team block to update the client." Confirmed.

`GmcpBridge.EmitCombatPartyIfChanged` had an early-return `if (party == null || party.Count == 0 || !IsActive) return;` that swallowed the "team just emptied" transition. So when companions died, the dungeon group disbanded, or a team disbanded mid-combat, the MUD client's party display kept showing the last non-empty roster forever because no follow-up emit fired to clear it.

**Fix.** Removed the `party.Count == 0` and `party == null` short-circuits. The snapshot-based change detection at the bottom of the function handles the transition correctly on its own: previous snapshot had member rows, new snapshot is `""`, they differ, one `Char.Combat.Party` emit fires with `members: []` and `count: 0`, then subsequent empty calls compare `""=="" ` and no-op. Null party is coerced to an empty list at the top of the function so downstream code stays safe.

Net effect: when the party transitions from N members to 0 (companion death, group disband, etc.), the client receives one empty-roster `Char.Combat.Party` frame within the next combat round (or immediately at next `EmitCombatPartyIfChanged` call from end-of-combat). The client can drop its party display accordingly.

## Fix: Wilderness monsters used random dungeon-monster behavior

Player report: "Wilderness monsters act like normal dungeon monsters. For example, I fought a feral cat and it behaved like an angel, including opening phrase and abilities. Feral Cat takes flight, becoming harder to hit!"

Confirmed. `WildernessLocation.CombatEncounter` generated wilderness encounters by calling `MonsterGenerator.GenerateMonster(monsterLevel)` -- which picks a random dungeon family + tier (Celestial Angel, Demonic Imp, Construct Golem, etc.) with that level's stats -- and then overwrote only `monster.Name` with the wilderness name from the region table. Every other family-specific field came through unchanged: `FamilyName`, `MonsterClass`, `SpecialAbilities` (the main offender -- Angel's `Flight`, `HolySmite`, etc.), `AttackType`, `MonsterColor`, `CanSpeak`, the random opening phrase. So a Feral Cat could literally take flight and the player got "Feral Cat takes flight, becoming harder to hit!" as the combat dialogue.

**Fix.** Added a `WildernessData.MonsterProfiles` dictionary that maps each of the 32 wilderness monster names (across all 4 regions) to a deterministic profile: family name, MonsterClass enum, AttackType string, color, CanSpeak flag, and a hand-picked ability list pulled from the `MonsterAbilities.AbilityType` enum. New `WildernessData.GetMonsterProfile(name)` helper returns the profile, with a "plain Beast" fallback for any name not in the table.

`WildernessLocation.CombatEncounter` now calls `GetMonsterProfile` after `GenerateMonster` and rewrites the leaking fields (`FamilyName`, `TierName`, `AttackType`, `MonsterColor`, `CanSpeak`, `MonsterClass`, `Undead` int, `SpecialAbilities`). Stats from `GenerateMonster` (HP, Strength, Defence, WeapPow, ArmPow, level scaling) are preserved -- only the cosmetic / behavioral overlay gets replaced.

**Sample profiles.** Feral Cat = plain Beast with no abilities (the player's specific example -- just bites and scratches). Forest Spider / Giant Wasp / Poison Toad = Insectoid/Beast with `VenomousBite` or `Poison`. Forest Troll / Ice Troll = Giant/Beast with `Regeneration` (and FrostBreath for the ice variant). Mire Zombie / Marsh Wraith / Drowned Sailor = Undead (so Turn Undead, Holy enchantments, etc. correctly trigger their vulnerabilities). Stone Golem = Construct with `Stoneskin`. Wyvern Hatchling = Draconic with `FireBreath`. Storm Elemental = Elemental with `Lightning`. Sirens charm. Krakens grab. The full table is in `WildernessData.cs`.

**Stats unchanged.** Level scaling, weapon/armor power, HP/Strength/Defence all still come from `MonsterGenerator` so wilderness creatures are appropriately tuned for their region's min-level. Only the family-overlay leak is fixed.

## Fix: honorable duels triggered the daily murder cap

Player report: "Do honourable duels count as murder? I thought not. But just now, I received this message: Your blade trembles. The townsfolk have seen too much of your work today. (Rewards reduced -- daily murder cap reached.)"

Confirmed. The player-initiated `[D] Duel` action in `BaseLocation.DuelNPC` routes the consensual challenge through `StreetEncounterSystem.AttackCharacter`, which then called `FightNPC` with the default `isHonorDuel: false`. So the duel was treated as a murder attempt: `FightNPC`'s murder-cap counter ticked up on each duel, and once the player crossed `GameConfig.MaxMurdersPerDay` the over-cap clawback fired (90% XP / gold stripped). Worse, `AttackCharacter` also added `+10 Darkness` unconditionally at the end, so every duel cost alignment regardless of outcome.

The accept-an-incoming-challenge path through `StreetEncounterSystem.HandleChallenge` already passed `isHonorDuel: true` correctly. The bug only affected player-initiated duels via the `[D]` action.

**Fix.** New `isHonorDuel` parameter on `StreetEncounterSystem.AttackCharacter` (defaults to `false` so every other caller's murder/assault semantics are preserved). The parameter flows through to `FightNPC`, which already had the correct exclusion logic for honor duels at line 1442 / 1464 (gates both the over-cap clawback and the counter increment). The +10 Darkness penalty at the bottom of `AttackCharacter` is now gated on `!isHonorDuel`.

`BaseLocation.DuelNPC` updated to pass `isHonorDuel: true`. The duel screen also gets its own header / opening line ("HONORABLE DUEL" / "You meet {0} on the field of honor!") instead of the "COMBAT!" + "You attack {0}!" assault framing. New loc keys `street_encounter.duel.title` and `street_encounter.duel.you_duel` in all 5 languages (en/es/fr/it/hu).

Honor duels still cost (the player can lose to a peer character, takes real damage, the small +5 Chivalry on win paired-movement still applies via `AlignmentSystem.ChangeAlignment`), but they no longer trip the murder cap or the assault darkness penalty.

## Fix: Home tier names showed in English for non-English players

Player report (Hungarian session): "In your home, the line which says you also have: x, y. Is still using the english equivalents for the items. In hungarian, it reads like: 'Ezzel is rendelkezel: Small chest, small herb pach.'"

Confirmed and broader than the one line. `HomeLocation.cs` had five private static `string[]` arrays of hardcoded English tier names (`LivingQuartersNames`, `BedNames`, `ChestNames`, `HearthNames`, `GardenNames`), each with 6 entries from "Dilapidated Shack" to "Grand Estate" etc. Three surfaces displayed them raw, bypassing localization entirely:

1. **"You also have ..." line on the home screen** (the player's report) -- chest + garden tier names appeared in English even in localized sessions.
2. **Renovations menu** -- `ShowTieredOption` rendered "Storage Chest Lv 2: Iron-Bound Chest" with both the upgrade-type label ("Storage Chest") and the tier name ("Iron-Bound Chest") hardcoded English. Hit five times (Living Quarters / Bed / Chest / Hearth / Garden).
3. **"Upgraded to ..." confirmation** after purchasing a tier upgrade.

**Fix.** Replaced the five hardcoded English string arrays with key arrays (`ChestKeys = ["home.tier.chest.0", "home.tier.chest.1", ...]`) and added a private `GetTierName(keys, level)` helper that resolves `Loc.Get(keys[level])` at render time. All three display surfaces now call through the helper.

The five upgrade-type labels passed as the `name` argument to `ShowTieredOption` ("Living Quarters" / "Bed" / "Storage Chest" / "Hearth" / "Herb Garden") were also hardcoded English -- those got their own new loc keys (`home.upgrade_type.quarters` etc.) and the callers now pass `Loc.Get("home.upgrade_type.X")`. Same for the `PurchaseUpgrade` invocations.

Added 35 new keys per language (5 tier arrays × 6 levels + 5 upgrade-type labels) for English, Spanish, French, Italian, and Hungarian. Hungarian translation is the load-bearing one for the player who reported the bug; the other four languages get equivalent tier-name pools so the renovations menu shows native-language tier names for every supported language.

Save / load semantics unchanged -- tier levels (`HomeLevel`, `BedLevel`, etc.) are still stored as ints; only the cosmetic name lookup was localized.

## Refactor: intimate encounter narrative extracted to loc keys (English-only, translation pass deferred)

Player report (Hungarian session): "Most of the intimate encounter text is not translated yet."

Confirmed. `IntimacySystem.cs` had ~87 hardcoded English narrative `terminal.WriteLine` calls scattered across the five scene phases (`PlayAnticipationPhase` / `PlayExplorationPhase` / `PlayEscalationPhase` / `PlayClimaxPhase` / `PlayAfterglowPhase`), plus the baby-naming prompt in `AnnouncePregnancy`. About a third of the file was already routed through `Loc.Get` (phase headers, mood descriptors, baby-born / family-grown lines, fade-to-black scene), but the bulk of the prose narrative (the actual scene descriptions, character reactions, romance dialogue, body descriptions per race, pillow-talk choice outcomes) was hardcoded English interpolated strings using `{their}` / `{them}` / `{gender}` / `{partner.Name2}` / `{player.Name}` substitutions.

**Refactor pass (this release).** Every hardcoded narrative line in `IntimacySystem.cs` extracted to a localization key under the `intimacy.*` namespace, with the in-line substitution variables converted to positional format args (`{0}`, `{1}`, etc.). 87 new English keys added under five sub-namespaces matching the phase methods: `intimacy.anticipation.*`, `intimacy.exploration.*`, `intimacy.escalation.*`, `intimacy.climax.*`, `intimacy.afterglow.*`. Each phase's keys are further sub-grouped by player choice or personality branch (e.g. `intimacy.anticipation.slow_*` vs `urgent_*` vs `passive_*`). Plus one `intimacy.name_prompt` for the baby-naming line that previously sat outside the localization layer.

The race-keyed body description block in `PlayExplorationPhase` (Elf / Dwarf / Orc / Hobbit / default) got five matching keys (`intimacy.exploration.body_elf` etc.) so translators can write race-appropriate descriptions per language.

**Translation pass — Hungarian, Spanish, French, and Italian all shipped.** This release ships the structural refactor (English keys added, `IntimacySystem.cs` calls them) plus full translations into all four non-English supported languages of all 87 new narrative keys. Every supported language now renders the intimate encounter narrative in its native form. 145/145 intimacy keys present across all five languages with no format-arg mismatches. Druidah was reporting in a Hungarian session so Hungarian was the load-bearing priority; Spanish came next as the next-most-played language. French and Italian translations are still deferred; players using those languages see English for these specific lines, same as before this release (the `Loc.Get` fallback chain returns the English value when a key is missing from the active language). The file is now *translatable* in batch — a follow-up release can do a focused translation-only pass for fr/it without touching the C# code.

**Hungarian translation** handles language structural differences: Hungarian has no gendered third-person pronouns, possessives are usually marked by noun-suffix rather than separate words, and accusative case suffixes on proper nouns vary by vowel harmony. Where English needed a `{gender}` arg for "he/she" or a `{their}` arg for "her/his", the Hungarian version typically drops the arg and relies on verb conjugation / possessive noun-suffix (e.g., "arcát" rather than "her cheek"). Sentences with name-accusative were restructured to avoid the foreign-name vowel-harmony problem (e.g. accusative-suffixed name forms like "Maria-t" become restructured sentences using the name in nominative position). 145/145 intimacy keys match between en.json and hu.json with no format-arg mismatches.

**Spanish translation** (Spain Spanish — vosotros/vuestro forms, matching the rest of es.json) handles different language quirks: gendered adjectives that need to agree with the partner's grammatical gender. Without per-call gender args from the C# layer, the translation uses two pragmatic strategies — (1) restructure sentences to use gender-neutral phrasing where possible ("sentir algo de vergüenza" instead of "shy" → adjective), and (2) when adjective agreement is unavoidable for romance prose (lines like "Eres tan preciosa", "Soy tuya"), use the feminine form as the default since it reads more naturally in Spanish romance text and is the established convention in Spanish localization of romance content. Pronouns like "her/his" collapse to neutral "su"; "her/him" collapses to "le" via leísmo (works in both Spain and most Latin American dialects). 145/145 intimacy keys match between en.json and es.json with no format-arg mismatches.

**French translation** (informal "tu" register, matching the existing convention for `intimacy.*` keys in fr.json — pre-fix keys like `intimacy.daily_cap_reached` use "Tu as partagé..." form) handles different challenges than Spanish: French possessives ("sa/son/ses") agree with the noun's gender, not the possessor's, so the `{their}` arg becomes redundant and the same word covers both "her cheek" and "his cheek" (both → "sa joue"). Where English needed `{0}=genderCap` as a subject pronoun ("She approaches..."), French is restructured to drop the subject by using noun-phrase constructions ("L'approche est gracieuse...") since French verbs require explicit subjects and a hardcoded "Elle"/"Il" default would read jarringly wrong for opposite-gender partners. Adjective gender agreement preferred toward invariant forms ("magnifique") where natural; falls back to masculine default (the established French default per existing fr.json `intimacy.fade_later2`) when the adjective must inflect.

**Italian translation** (informal "tu" register, matching existing it.json intimacy convention — pre-fix keys like `intimacy.daily_cap_reached` use "Hai già condiviso..." form) shares the same possessive-agrees-with-object rule as French ("la sua guancia" works for both her/his), but unlike French, Italian is a **pro-drop language** — verbs can stand alone without explicit subject pronouns ("Risponde con entusiasmo..."), which sidesteps most of the restructuring French needed. Adjective gender agreement uses masculine default for required inflection (matching existing it.json convention from `intimacy.fade_later2` — "appagato"). Where the noun adjacent to the adjective is feminine ("la voce sonnolenta e appagata"), the adjective can agree with the noun rather than the partner, avoiding the gender-default problem entirely.

This is the same pattern Localization v0.51.0 ("Babel") used for the broader localization rollout: extract first, translate as a separate batch. Pre-refactor the file's intimate-scene narrative was structurally untranslatable because it was inlined into interpolated C# strings; now it's a single-source-of-truth set of 87 keys that any translator (or translation agent) can work through.

**Mechanics unchanged.** Save format, scene flow, personality-driven branching, pregnancy logic, daily encounter cap (`MaxIntimateEncountersPerDay` from v0.61.1), and reward/relationship updates all unchanged. This is a pure localization-structure refactor.

## Fix: Incorporeal / Phase / Vanish / etc. monster evasion abilities had no mechanical effect

Player report: "The monster becoming incorporeal combat ability doesn't really have any effect as far as I can tell."

Confirmed and broader than Incorporeal. Nine monster abilities set a defensive evasion flag on the per-call `AbilityResult` -- `Phase`, `Incorporeal`, `PhaseShift`, `TreeMeld` set `AvoidAllDamage` to true on a random proc; `Vanish`, `Flight`, `Invisibility`, `Teleport`, `InkCloud` set `EvasionBonus` to a percentage. **Nothing in `CombatEngine` ever read either flag.** The abilities printed their flavor text ("becomes incorporeal — attacks pass through!", "fades into the shadows!", "takes flight, becoming harder to hit!") but the player's next swing landed normally with full damage. The buff was strictly cosmetic across the entire combat engine.

**Fix.** Two new transient fields on `Monster`: `EvasionRounds` (int) and `EvasionMissChance` (int 0-100). When one of the nine defensive abilities fires successfully (Phase / Incorporeal / PhaseShift / TreeMeld all gate on the existing random proc roll inside `MonsterAbilities`; Vanish / Flight / Invisibility / Teleport / InkCloud always set the buff since they don't have an inner roll), the ability handler now persists `EvasionRounds = 2` (3 for InkCloud, matching its 3-round Blinded duration) and `EvasionMissChance` to an ability-specific value (Phase / Incorporeal / PhaseShift = 50%, TreeMeld = 55%, Vanish = 30%, Flight = 25%, Invisibility = 35%, Teleport = 40%, InkCloud = 30%).

**Three damage paths gated.** Player-attack damage application now rolls against the monster's evasion before dealing damage:

1. **`ExecuteSingleAttack`** -- the basic-attack workhorse called by single + multi-monster combat. Check happens BEFORE the D20 hit roll, so on a proc the entire swing passes through: no D20, no damage, no post-hit enchantment procs (lifesteal / fire / lightning / etc.), no proficiency-improvement roll. Off-hand attacks print "Off-hand strike" then the evasion line.
2. **`ApplySingleMonsterDamage`** -- the damage-apply consolidation point used by spells and class abilities. Same check at the top; on a proc returns immediately with flavor message, no damage.
3. **`ApplyAoEDamage`** -- per-monster check inside the AoE loop. If one target evades, that target is skipped; other targets still take full AoE damage.

**Buff lifecycle.** `EvasionRounds` decrements once per monster-action cycle in `ProcessMonsterAction`, in the same block as `WeakenRounds--`. Buff applied on round N's monster turn covers the player's swing in round N+1, then ticks to 1, covers round N+2's swing, ticks to 0, falls off. So a 2-round buff means the next 2 player swings each have the dodge chance to proc. `EvasionMissChance` zeroes out on expiry so a future ability re-set is unambiguous.

**Net behavior.** A Wraith using Incorporeal at the right moment now actually phases through the player's next swing 50% of the time. A Dragon using Flight is harder to hit for 2 rounds. A Squid Kraken-Spawn's InkCloud blinds the player AND grants 3 rounds of 30% miss-against. The "becomes incorporeal — attacks pass through!" flavor line now matches what the mechanic actually does.

New loc key `combat.evasion_miss` ("Your attack passes harmlessly through {0}!") in all 5 languages (en/es/fr/it/hu).

## QoL: cancel option on combat target prompts

Player suggestion: "Make a way of cancelling targetable abilities like cure wounds, I didn't figure out how to cancel them if they can be."

Confirmed gap. Once the player pressed a quickbar slot (1-9) for a spell or ability that requires target selection (heal spells like Cure Wounds, buff spells like Bless, monster-targeted spells like Lightning Bolt, single-target abilities like Power Strike / Precise Strike / Backstab / Taunt / Disarm), the target-selection prompt had no escape hatch. Empty input meant "self" (for ally targeting) or "random" (for monster targeting); typing a number or anything else either selected a target or looped back to the same prompt with "invalid input". The player was committed to the spell once they pressed the quickbar key, even if they realized they hit the wrong slot or changed their mind mid-selection.

**Fix.** All three target-prompt helpers (`SelectHealTarget`, `SelectBuffTarget`, `GetTargetSelection`) accept `Q`, `C`, or `X` as a cancel input and return a new `TargetCancelled` sentinel (`int.MinValue`, out-of-band relative to valid target indices 0+ and the existing `null` = self/random semantics). The basic `[A] Attack` action, the tactical `[P] Power Strike` / `[E] Precise Strike` / `[K] Backstab` / `[T] Taunt` / `[W] Disarm` actions, and `HandleQuickbarAction` (for both spells and class abilities) all check for `TargetCancelled` after the prompt returns and bail back to the main action menu without consuming mana / stamina / the turn slot. The ally-targeting prompts show a `[Q] Cancel` line above the input prompt; the monster-target prompts (which are inline single-line prompts) got the `Q=cancel` hint baked into the prompt text itself (alongside the existing `ENTER=random` hint where applicable).

The existing `[H] Aid Ally` flow already had a `0=cancel` option (`combat.cancel_option` loc key, from an earlier release) — that path is unchanged.

New loc key `combat.target_cancel_hint` ("[Q] Cancel") in all 5 languages (en/es/fr/it/hu). Existing `combat.target_monster` / `combat.target_monster_random` loc strings updated in all 5 languages to advertise the new `Q=cancel` hint.

## Refactor: stranger encounter dialogue extracted to loc keys (English-only, translation pass deferred)

Player report (calling out one specific line): "The encounter with the old crone where you're talking about waves cresting, and you can tell her that 'You are talking about more than waves aren't you?' Doesn't seem to be translated."

Confirmed — and the issue extended across the whole stranger-encounter system, not just that one line. `StrangerEncounterSystem.cs` had a 260-line `ContextualDialoguePool` static data block with 15 contextual encounters (early-game `ctx_early_graveyard` / `ctx_early_seasons` / `ctx_early_dormitory` / `ctx_early_after_death` / `ctx_early_candle`; mid-game `ctx_mid_wave` / `ctx_mid_after_god` / `ctx_mid_companion_grief` / `ctx_mid_phoenix` / `ctx_mid_sleep`; late-game `ctx_late_cycle` / `ctx_late_identity` / `ctx_late_after_seal` / `ctx_late_manwe`; and the `ctx_suspects_identity` reveal). Each encounter had 2-5 dialogue lines, 3-4 response options each with player-choice text and 1-3 reply lines from the stranger. About 162 hardcoded English narrative strings, none of which routed through `Loc.Get`.

**Refactor pass (this release).** Replaced every English literal in the data block with a localization key (e.g. `"\"Have you ever watched a wave?\""` became `"stranger.ctx_mid_wave.dialogue.1"`). 162 new English keys added under the `stranger.*` namespace, organized by encounter ID and slot position. Two consumption sites updated: `GenerateEncounter` joins the keyed dialogue array via `Loc.Get(...)` per element before display; `GetResponseOptions` does the same for `opt.Text` and `opt.StrangerReply`. The 6 fallback strings in `GetResponseOptions` (legacy code path when an encounter has no response options defined) also got their own loc keys (`stranger.fallback.option.*`).

**Translation pass — all five languages shipped.** This release ships the structural refactor plus full Hungarian, Spanish, French, AND Italian translations of all 168 stranger encounter keys. The bug was reported in a Hungarian session so Hungarian was the load-bearing priority; the remaining three followed. 168/168 stranger keys present across all five languages (en/hu/es/fr/it) with no format-arg mismatches. Every supported language now renders the stranger encounter dialogue in its native form. Each translation preserves the stranger's informal voice and the mystical/philosophical tone of the original prose. Spanish uses Spain Spanish (vosotros/vuestro). French and Italian use the informal "tu" register and French guillemets « » for quotation marks per existing fr.json / it.json convention. Italian leans on the language's pro-drop nature to keep the stranger's speech crisp ("Mi conosci. E ogni volta dimentichi." rather than padding with explicit subject pronouns).

**Data shape.** The `Dialogue` field stays `string[]` and the `Text` / `StrangerReply` fields stay their original types — only the CONTENT of those strings changed from literal English to key strings. Any existing save data referencing encounters is unaffected (the `Id` field, the only piece persisted, is unchanged). The `ContextualDialoguePool` is a private static readonly list that's rebuilt at process start, so old/new content can't coexist mid-session.

## Fix: location names not translated in travel / exits prompts

Player report (Hungarian session): "the locations when you move about aren't translated. see stuff like 'Úton ide: Home.'"

Confirmed. The "heading to" prefix at `BaseLocation.cs:3675` already routed through `Loc.Get("base.heading_to", ...)` (so "Úton ide: {0}..." renders correctly in Hungarian), but the `{0}` argument came from `GetLocationName(destination)` which returned hardcoded English strings. The function had a switch covering 17 of the ~50 `GameLocation` enum values; everything else fell through to `_ => location.ToString()` which returns the bare enum name (e.g. `Home`, `MainStreet`). So Hungarian players saw English location names interleaved with their localized prefix text. The same problem affected the exits list at `BaseLocation.cs:1961` (`var exitName = GetLocationName(exit);`) — exit labels rendered in English even when the surrounding UI was in Hungarian.

**Fix.** Replaced the hardcoded switch with a single `Loc.Get($"location.name.{location}")` call. The function now produces a key like `location.name.Home` / `location.name.MainStreet` and resolves it through the standard localization fallback chain (current language → English → key string). 69 new `location.name.*` keys added covering every `GameLocation` enum value (including the ~30 that the old switch never covered — Home, MusicShop, Wilderness, Settlement, AtHome, Pantheon, TeamCorner, QuestHall, all the Castle sub-locations, all the Prison sub-locations, etc.).

Full translation pass shipped in this release: 69 keys × 4 non-English languages (Hungarian, Spanish, French, Italian) = 276 new translation strings, plus 69 English entries. Total 345 new loc strings. A handful of cognates render identically across languages (e.g. "Bank" in Hungarian, "Arena" in Spanish and Italian, "Temple" and "Prison" in French) — those are intentional, not untranslated.

Wizard command system also calls `GetLocationName` for admin-facing messages (`/goto`, `/summon`, `/transfer` logs). Those now render in the wizard's session language rather than always-English. Acceptable trade-off; if a future need arises for English-only admin logs, a `GetLocationNameEnglish` variant can be added.

## Fix: memorable-NPC story choices had no clear hotkey

Player report (Pip the orphan thief encounter): "In the encounters with pip the orphan thief, it doesn't tell you which letters to use for the different options. I wanted to choose mentor for the one encounter, but I didn't know which letter to use."

Confirmed — and same bug on every memorable-NPC story choice, not Pip-specific. `DisplayTownNPCEncounter` rendered each `NPCChoiceOption` as `[<full-word-key>] <label>` (e.g. `[forgive] Let her keep it` / `[mentor] Offer to teach her to fight` / `[guards] Turn her in to guards`). The player was expected to type the literal multi-character key word as input. The bracket notation across the rest of the game's menus means "press this single character," so players reasonably tried single letters and got nowhere. The 4 affected encounters (the wounded-soldier choice, the debt-repayment choice, the manuscript-accept choice, Pip's caught-stealing choice) all had this UX.

**Fix.** Switched the display to numbered hotkeys (`[1] Let her keep it` / `[2] Offer to teach her to fight` / `[3] Turn her in to guards`) matching the convention of combat-action menus and healing-target prompts. The input matcher accepts three forms in order:

1. **Number** (1-based index into the Options array) — the primary new input form, matches what's displayed.
2. **Full data-key** (case-insensitive) — kept for muscle memory of any player who learned to type "forgive" etc.
3. **First letter of the data-key** — defensive fallback. Only matches when the letter is unambiguous across the choice set (so a hypothetical `rescue` / `refuse` pair wouldn't accept `r`).

Data keys (`forgive` / `mentor` / `guards` / etc.) stay intact — they're used elsewhere for `choiceMade` tracking and story-stage gating, and a single-letter migration would break that. Only the display + input layer changed; the underlying data model is preserved.

## Fix: Team Corner echoes silently failed to enter the dungeon when the party was full

Player report (Lv.68 Voidreaver, online): "Team Echos aren't working for me. I can add them in team corner, but they don't appear in the party in the dungeon, and don't even show in party menu. I tried leaving and rejoining, but it team corner still says they're in my party. They are lower level than me, but I didn't think that was a problem."

The reporter's confusion was the symptom of a silent failure. Confirmed both halves: another player on the same team could load echoes fine, so the system worked in general; the reporter's party was specifically getting echoes filtered out at dungeon entry with no warning.

Root cause: `DungeonLocation.RestorePlayerTeammates` (the method that loads echoes from `DungeonPartyPlayerNames` into the live `teammates` list) has a hard cap of 4 party members, enforced via `if (teammates.Count >= maxPartySize) break;` at the top of its loop. By the time it runs, two earlier methods have already populated `teammates`:

- `AddCompanionsToParty` (line 265) — adds every alive companion the player has recruited. No cap. A player with four recruited companions (Aldric + Vex + Lyris + Mira is exactly four) fills `teammates` to capacity here.
- `RestoreNPCTeammates` (line 268) — adds NPC teammates with cap-respect AND a visible yellow "X allies couldn't join (party full at 4/4). Use [Y] Party to swap members." warning when overflow occurs.

`RestorePlayerTeammates` ran third (line 279), found `teammates.Count` already at 4, and broke out of its loop on the very first iteration. No error logged, no message printed, no debug trail. From the player's perspective: echoes vanished. Team Corner still showed them as recruited (because the recruit list lives in `DungeonPartyPlayerNames`, which is never modified by the load attempt — the cap rejection happens at the consume side, not the recruit side). The reporter's "I tried leaving and rejoining" produced the same result every time because the same four companions filled `teammates` on every entry.

The reporter's note that "Cheddar confirmed he can still use them" is the diagnostic — Cheddar's party composition left echo slots available; the reporter's didn't.

The `[Y] Party` in-dungeon menu doesn't help either: it allows dismissing companions for inventory/equipment management, but the echo-load happens only at dungeon-entry time, so even if the player dismissed a companion mid-dungeon, no echo would materialize.

**Two-part fix.**

**(1) Visible warning at dungeon entry.** `RestorePlayerTeammates` now mirrors the NPC-teammate overflow pattern: track `skippedCount` across the loop, and emit a yellow warning + hint at the end if any echoes were dropped: "X player echo(es) could not join (party full at 4/4). Dismiss a companion or un-recruit an NPC at Team Corner to make room." The duplicate-filter check moved above the cap check so already-loaded echoes from a previous dungeon re-entry don't inflate the skipped count.

**(2) Pre-warning at Team Corner.** `RecruitPlayerAlly` now counts the total prospective party size (active companions + recruited NPC teammates + already-recruited echoes + the one being added) immediately after a successful add. If the total exceeds 4, it emits a yellow ATTENTION line plus a gray hint explaining that companions take priority and echoes load last. The player learns about the overflow at the moment they make the choice, not 30 seconds later inside the dungeon.

The 4-slot cap itself isn't moved — combat balance is tuned around four allies, and the existing pet 5th slot is a deliberate exception. Players who want all their echoes in the dungeon need to dismiss a companion at the Inn or sack an NPC teammate at Team Corner. The fix makes the constraint visible and recoverable instead of silent and confusing.

New keys in all 5 languages (en/hu/es/fr/it): `dungeon.echoes_skipped`, `dungeon.echoes_skipped_hint`, `team.recruit_party_overflow`, `team.recruit_party_overflow_hint`.

## Fix: Voidreaver Hungering Strike lifesteal didn't fire when the hit killed (or when at full HP)

Player report (Lv.66 Voidreaver): "Hungering strike on Voidreaver doesn't seem to be draining HP every hit. ... Even in a single fight it flip flops on working."

The "flip flops" detail is the smoking gun — Hungering Strike was randomly working / not working against the same enemy type within the same fight. Confirmed and worse than it sounded. Hungering Strike (and the legacy lifesteal_30 effect, which fires from the same code path) was implemented as a post-damage `switch` case that did TWO things: (1) re-applied `abilityResult.Damage` as a second hit on top of the base damage that the ability block had already dealt, and (2) applied a 20% / 30% heal. Both were gated on `target.IsAlive`. At Lv.66 the base hit usually killed the target outright (the ability has 60 base damage + Pain Threshold +20% + crit chance + marked-target +30% + dual-wield off-hand follow-up stacking on top), so the `IsAlive` check failed and BOTH the redundant damage AND the heal got skipped — the player saw a powerful hit but no drain.

Same bug-class as v0.57.2's Shadow Harvest fix and v0.60.10's Wave Echo fix: a post-damage handler gated on `IsAlive` that breaks against high-level players who one-shot through it.

**Fix.** Lifesteal heal extracted from the post-damage switch case and inlined into the base-damage block at the same spot Shadow Harvest's 25%-heal-on-kill lives (`ApplyAbilityEffectsMultiMonster` after `target.HP -= actualDamage`, mirrored in `ApplyAbilityEffects` for single-monster combat). The heal now:

- Scales with `actualDamage` (the bonus-applied damage — Pain Threshold +20%, crits, marked +30%) rather than the raw 60 base, so a 200-damage Hungering Strike heals for 40 HP instead of 12.
- Fires regardless of whether the base hit killed the target.

The post-damage switch cases for `lifesteal_20` and `lifesteal_30` are now no-ops (replaced with `break;` + comment) so the second damage application is gone. Net mechanical impact on Hungering Strike vs the broken pre-fix behavior:

- **When the target survived the first hit** (pre-fix dealt ~2× base damage from the double-application): damage drops from ~2× actualDamage to 1× actualDamage. This is a nerf but corrects an undocumented stealth buff — the ability description says "60 base damage" not "120 base damage", and the double-apply was clearly a bug not a feature.
- **When the target died to the first hit** (pre-fix dealt 1× actualDamage, healed 0): heal goes from 0 to 20%/30% of actualDamage. This is the fix the player report was asking for.

The ability now consistently matches its in-game description ("A ravenous blow. 60 base damage. Heals 20% of damage dealt.") in both scenarios.

**Feedback always visible.** Earlier draft of the fix only emitted the lifesteal flavor line when `actualHeal > 0`, which silently swallowed the message whenever the player was at or near full HP (the heal scales to 0 after the MaxHP clamp). That would have read to the player as another "flip flop." Both inline blocks now always emit feedback: the standard "You feast on X HP!" line when the heal actually moved HP, and a new `combat.ability_lifesteal_capped` line ("Already at full HP — no drain absorbed.") when the heal was clamped to 0. The player now sees consistent visible confirmation that the ability fired regardless of HP state. New key in all 5 languages (en/hu/es/fr/it).

## Fix: Team Corner repeatedly charged for the same NPC due to world-sim reload race

Player report (Lv.32 Abysswarden, online): "I am hiring NPCs for my dungeon team, but it is still listing them as available for hire. I can still hire the same person and lose my gold even though he is currently in my team. It does not add a 2nd instance of the NPC but it does drain my gold."

Tracing through the recruitment flow surfaced an orphan-reference race tied to the world-sim's NPC reload. `TeamCornerLocation.RecruitNPCToTeam` captures `var allNPCs = NPCSpawnSystem.Instance.ActiveNPCs;` at menu entry and passes references through to `ConfirmAndRecruit`. The Y-confirm prompt is a blocking await on the player's input, so an arbitrary amount of time can pass between the menu render and the actual gold deduction.

If another online player's `SaveAllSharedState` bumps the `world_state.npcs` version during that wait, `WorldSimService.SaveWorldState`'s next tick detects the version change and calls `LoadWorldState`, which runs `NPCSpawnSystem.Instance.ClearAllNPCs()` and rebuilds the entire NPC list from DB. The old in-memory NPC objects are now orphaned (no longer in `spawnedNPCs`), and the player's `recruit` reference still points to one of those orphans.

When the player presses Y:

1. `currentPlayer.Gold -= liveCost;` deducts gold from the live player object.
2. `recruit.Team = currentPlayer.Team;` writes to the orphan object — invisible to the live NPC list.
3. `await OnlineStateManager.Instance.SaveAllSharedState();` calls `SerializeCurrentNPCs()` which iterates the LIVE `ActiveNPCs`. The new NPC for "Bob" was built from the pre-recruit DB snapshot, so its Team is still empty. The orphan's Team=X is never serialized.
4. DB ends up with Bob.Team="" (unchanged from before).
5. Player returns to the recruit menu. `BuildRecruitmentCandidates` re-queries `ActiveNPCs` and finds Bob with Team="". `IsRecruitable` passes. Bob shows in the list again.
6. Player picks Bob again. Gold deducts again. Same orphan-write race repeats.

The player's "he is currently in my team" experience came from `DungeonPartyNPCIds` (which IS persisted on the player save and survives world-sim reloads, because it's keyed by the player's own state). So the dungeon party correctly shows the NPC as a teammate, but the Team Corner's formal team flag on the NPC keeps reverting to empty.

**Fix (two-layer defense).** In `ConfirmAndRecruit`, right after the Y-confirm and before the live `IsRecruitable` re-check:

1. **Re-resolve the recruit reference by ID against the live `ActiveNPCs`.** If `NPCSpawnSystem.Instance.ActiveNPCs.FirstOrDefault(n => n.ID == recruit.ID)` finds a live NPC, use that reference for all subsequent mutations. If the references differ, log a diagnostic at INFO level recording that a world-sim reload happened mid-recruit. If no live NPC matches (rare — the NPC was permadied / evicted by some other path), refuse with the existing `team.recruit_unavailable_now` line and no gold deduction.

2. **Specific "already on your team" refusal.** When the live re-check via `IsRecruitable` returns false AND the NPC's Team matches the player's Team, surface a new `team.recruit_already_on_team` line ("X is already a member of your team. No gold spent.") instead of the generic "unavailable" message. This catches the case where some other path legitimately set the NPC to the player's team between menu and confirm, AND makes the failure mode informative.

After the fix:
- Pre-race scenario: player recruits Bob. `recruit` is the orphan from the stale snapshot. Re-resolution finds the live Bob. Mutation hits the live object. `SaveAllSharedState` serializes the live object with Team=X. DB has Team=X. Player navigates back, Bob is filtered out of the list (Team=X). Bug doesn't manifest.
- Post-race scenario (already-recruited player walking into the recruit menu): if for any reason the live NPC's Team is already set to the player's Team, the IsRecruitable filter at the list stage would have caught it. If it didn't (some new code path / future regression), the gold-deduction guard catches it and surfaces the specific message instead of the generic "unavailable."

New loc key `team.recruit_already_on_team` in all 5 languages (en/hu/es/fr/it).

Note: there is a deeper race in `WorldSimService.SaveWorldState` where the world sim's serialize-and-write can overwrite a concurrent player write with a stale snapshot (the version-comparison guard at the start of the method only fires if the player wrote BEFORE the sim's read, not concurrently). That's a deeper concurrency fix that belongs in a follow-up — it'd need a write-time CAS or a final post-write version recheck. The recruit-side fix above eliminates the OBSERVED symptom (gold drain on re-recruit) by ensuring the player's mutation always hits the live object regardless of which DB snapshot the world sim ends up writing.

## Files Changed

- `Scripts/Core/GameConfig.cs` -- Version 0.61.2.
- `Scripts/Core/Character.cs` -- New `PetSpeciesId` field on pet wrappers so species-specific combat behavior survives the player renaming their pet.
- `Scripts/Locations/DungeonLocation.cs` -- `AddActivePetToParty` now sets `PetSpeciesId = pet.Id` when building the wrapper.
- `Scripts/Core/Character.cs` -- Last-Stand cap state (`RoundStartHP`, `LastStandFiredThisRound`) and methods (`CaptureRoundStartHP`, `LastStandCheckAndApply`). Both fields `[JsonIgnore]` since they're per-round transient combat state.
- `Scripts/Systems/CombatEngine.cs` -- New Storm Eagle block at the top of `ApplyPostHitEnchantments`: 10% lightning chip damage and a 15% stun proc routed through `TryStunMonster` (so boss resist, DR, and the immunity window all apply). Last-Stand cap wired in: `CaptureRoundStartHP` at the top of every combat round and once before the ambush phase; `LastStandCheckAndApply` runs at the very top of `HandlePlayerDeath` to rescue qualifying players and render the flavor line (plus group broadcast for followers).
- `Scripts/Systems/WorldSimulator.cs` -- (1) HP and personality gates at the top of `NPCExploreDungeon`; tightened dungeon-level roll; flee threshold lifted to 50% (Courage-scaled) with no rounds gate; dispatcher in `SimulateNPCActivity` only stamps dungeon-flavor activity when the NPC actually went. Phase 1 NPC-AI telemetry: capture before-state at function entry and log every outcome path (`aborted_wounded`, `aborted_cautious`, `won`, `died`, `fled`, `stalemate`) to the new `npc_decision_log` table. (2) `NPCTeamDungeonRun` got the same self-preservation pattern applied after the first telemetry pass exposed its 71% death rate. Leader HP / personality gates, tightened level roll, lifted flee threshold to 50% (Courage-scaled per member, no rounds gate), bumped base flee chance 55% -> 70% with 95% cap. Rich outcome telemetry mirrors the solo path. (3) Action picker rebalance in `ProcessNPCActivities`: shop / train / levelup weights bumped, move / inn / team_recruit weights cut, to fix the wander-vs-progress imbalance the first telemetry pass exposed.
- `Scripts/Systems/SqlSaveBackend.cs` -- New `npc_decision_log` table + indexes for Phase 1 NPC-AI telemetry. New `LogNPCDecision(...)` writer and `PruneOldNPCDecisionLog(...)` cleanup helper.
- `Scripts/Locations/TempleLocation.cs` -- (1) `DisplayPlayerStatus` now falls back to `Character.WorshippedGod` when the `GodSystem.GetPlayerGod` lookup is empty, so the `[S] Status` screen inside the sacrifice menu correctly shows player-immortal worshippers as following their immortal instead of "no god." (2) `SacrificeToImmortalGod` now applies Faith faction reputation and Chivalry alignment gain (capped via `MaxAlignmentGainPerTempleSacrifice`) the same way elder-god sacrifices do, so sacrificing gold to a player-immortal actually moves the worshipper's faith standing.
- `Scripts/Locations/DungeonLocation.cs` -- Added `case PuzzleType.PressurePlates` to the puzzle dispatch switch; added new `HandlePressurePlatesPuzzle` method modeled on `HandleLeverPuzzle` so the puzzle's pre-generated plate sequence and wear-pattern hints actually matter. Also added immediate `AchievementSystem.CheckAchievements` calls after dungeon-merchant purchases and the in-dungeon monk-vendor bulk-buy path.
- `Scripts/Locations/{WeaponShop,ArmorShop,MagicShop,MusicShop,Healer,Castle,LevelMaster}Location.cs` + `Scripts/Systems/StreetEncounterSystem.cs` -- Added immediate `AchievementSystem.CheckAchievements` calls after every `RecordPurchase` and `RecordLevelUp` site so threshold-crossing achievements (shopaholic, big_spender, level_X) unlock at the moment the action happens instead of being deferred to the post-combat sweep. Notification still surfaces through the standard `BaseLocation.ShowPendingNotifications` hook.
- `Localization/{en,es,fr,it,hu}.json` -- New `combat.storm_eagle_lightning` and `combat.storm_eagle_stun` flavor lines, plus `combat.last_stand` and `combat.last_stand_broadcast` for the Last-Stand cap, plus `dungeon.pressure_plates_prompt` for the new Pressure Plates puzzle handler. All five languages.
- `Scripts/Core/Quest.cs` -- Localization-key fields (`TitleKey`, `TitleArgs`, `CommentKey`, `CommentArgs`, `InitiatorKey` on `Quest`; `DescriptionKey`, `DescriptionArgs` on `QuestObjective`) and `GetDisplayTitle()` / `GetDisplayComment()` / `GetDisplayInitiator()` / `GetDisplayDescription()` accessors. New `QuestObjective.Localized(...)` factory for key-aware objective construction.
- `Scripts/Systems/QuestSystem.cs` -- Starter, king royal (all 4 target cases), targeted DefeatNPC bounty, and generic bounty quest creators now set `TitleKey` / `TitleArgs` / `CommentKey` / `CommentArgs` / `InitiatorKey` alongside the legacy rendered strings. Objectives use the `QuestObjective.Localized(...)` factory. `CreateStarterQuest`'s "skip if exists" guard back-populates empty keys on legacy quests for save migration. All three deserialization paths (`RestoreFromSaveData`, `MergeWorldQuests`, `MergePlayerQuests`) restore the new fields with empty-string defaults. All five `MailSystem.Send*` quest mail paths and the `NewsSystem` failure-news call switched to `GetDisplayTitle()` / `GetDisplayInitiator()`.
- `Scripts/Systems/SaveDataStructures.cs` -- `QuestData` and `QuestObjectiveData` gain matching key/arg fields so quests round-trip cleanly with their localization data.
- `Scripts/Systems/SaveSystem.cs` -- `SerializeQuestList` writes new key/arg fields.
- `Scripts/Systems/OnlineStateManager.cs` -- `SerializeCurrentQuests` writes new key/arg fields.
- `Scripts/Locations/QuestHallLocation.cs` -- All quest list, detail, bounty board, claim, turn-in, abandon, and Electron-summary display sites use `GetDisplayTitle()` / `GetDisplayComment()` / `GetDisplayInitiator()` / `GetDisplayDescription()`.
- `Scripts/Locations/DungeonLocation.cs` -- Active-quest panel and `[Y] Status` quest readout use display methods.
- `Scripts/Locations/BaseLocation.cs` -- `/health` quest section uses display methods.
- `Scripts/Locations/DarkAlleyLocation.cs` -- Active-targets list uses display methods.
- `Scripts/Core/GameEngine.cs` -- "Target permadied, quest auto-completed" message uses `GetDisplayTitle()`.
- `Scripts/Systems/CombatEngine.cs` -- New `CombatResult.PlayerActuallyDied` bool; set at the top of `HandlePlayerDeath` (after the Last-Stand short-circuit) so the death-pipeline-entered signal is preserved through the resurrection rewrite of `result.Outcome`. `Char.Combat.End` GMCP emit derives outcome from `PlayerActuallyDied` so MUD clients see "PlayerDied" when the player actually died (regardless of whether resurrection rewrote Outcome to PlayerEscaped for C# control flow). Last-Stand rescue branch now also resets `result.Outcome` to `Victory` or `PlayerEscaped` so rescued players don't show as "PlayerDied" over GMCP.
- `Scripts/Server/GmcpBridge.cs` -- `EmitCombatPartyIfChanged` no longer early-returns when party is null or empty; the snapshot mechanism handles the "had members → now empty" transition so MUD clients receive an explicit empty `Char.Combat.Party` frame and can clear their party display.
- `Scripts/Data/WildernessData.cs` -- New `WildernessMonsterProfile` class and `MonsterProfiles` lookup table for all 32 wilderness monster names. New `GetMonsterProfile(name)` static helper. Profiles cover family name, MonsterClass, AttackType, color, CanSpeak, and a hand-picked ability list per name.
- `Scripts/Locations/WildernessLocation.cs` -- `CombatEncounter` now applies the wilderness monster profile after `MonsterGenerator.GenerateMonster` so the generated creature's family-specific fields match its name. Stats from `GenerateMonster` are preserved.
- `Scripts/Systems/StreetEncounterSystem.cs` -- New `isHonorDuel` parameter on `AttackCharacter` (default false). Flows through to `FightNPC`'s existing exclusion logic so honor duels don't tick the murder cap or trip the over-cap clawback. The +10 Darkness penalty at the end of `AttackCharacter` is gated on `!isHonorDuel`. Duel header / opening line use new "HONORABLE DUEL" framing instead of "COMBAT!".
- `Scripts/Locations/BaseLocation.cs` -- `DuelNPC` passes `isHonorDuel: true` when routing through `AttackCharacter`.
- `Localization/{en,es,fr,it,hu}.json` -- New `street_encounter.duel.title` and `street_encounter.duel.you_duel` keys for the duel-specific header / opening line.
- `Scripts/Locations/HomeLocation.cs` -- Five tier-name arrays converted from hardcoded English strings to localization key arrays. New `GetTierName(keys, level)` helper. `ShowHomeUpgrades` / `ShowTieredOption` / the upgrade case-switch / `PurchaseUpgrade` invocations all updated to consult the keys at render time and pass localized upgrade-type labels.
- `Localization/{en,es,fr,it,hu}.json` -- 35 new keys per language: 30 tier names (5 categories × 6 levels) under `home.tier.*` plus 5 upgrade-type labels under `home.upgrade_type.*`. Hungarian translation prioritized because the bug was reported in a Hungarian session.
- `Scripts/Systems/IntimacySystem.cs` -- ~87 hardcoded English narrative `terminal.WriteLine` calls across all five scene phases extracted to `Loc.Get` calls under the `intimacy.*` namespace. Phase methods `PlayAnticipationPhase`, `PlayExplorationPhase`, `PlayEscalationPhase`, `PlayClimaxPhase`, `PlayAfterglowPhase` all refactored. Baby-naming prompt in `AnnouncePregnancy` localized via `intimacy.name_prompt`. Race-keyed body description block split into 5 keys (`intimacy.exploration.body_elf` / `_dwarf` / `_orc` / `_hobbit` / `_default`). Pre-existing interpolation variables (`{their}` / `{them}` / `{gender}` / `{partner.Name2}` / `{player.Name}`) converted to positional format args so translators can re-order per language grammar.
- `Localization/en.json` -- 87 new English keys for intimate encounter narrative.
- `Localization/hu.json` -- 87 new Hungarian translations matching the new English keys. Phrasing adapted for Hungarian (no gendered third-person pronouns, noun-suffix possessives, restructured sentences to avoid foreign-name accusative). 145/145 intimacy keys match between en.json and hu.json with no format-arg overflow.
- `Localization/es.json` -- 87 new Spanish translations (Spain Spanish, vosotros/vuestro forms). Gendered adjective agreement handled via two strategies: restructure to gender-neutral phrasing where possible, default to feminine form for unavoidable romance-prose adjective agreement (established Spanish romance-translation convention). Pronouns "her/his" collapse to neutral "su"; "her/him" collapses to "le" via leísmo. 145/145 intimacy keys match between en.json and es.json with no format-arg overflow.
- `Localization/fr.json` -- 87 new French translations (informal "tu" register, matching existing intimacy convention). Restructured to noun-phrase / dropped-subject forms where English used `{genderCap}` as subject pronoun, since French requires explicit subjects and a hardcoded "Elle/Il" default would jar opposite-gender partners. Possessives "sa/son/ses" agree with object noun gender (not possessor), so `{their}` arg becomes redundant in French and pronouns just inherit from noun. Adjectives prefer invariant forms ("magnifique") where natural; masculine default for required inflection (matching existing fr.json convention).
- `Localization/it.json` -- 87 new Italian translations (informal "tu" register, matching existing it.json intimacy convention). Italian's pro-drop nature lets verbs stand alone ("Risponde con entusiasmo...") without restructuring, sidestepping the subject-pronoun problem French needed to solve. Same possessive-agrees-with-object rule as French. Adjective gender agreement: masculine default for required inflection (matching existing it.json convention from `intimacy.fade_later2`); attached to adjacent feminine nouns where prose flow allows. 145/145 intimacy keys present across all five languages (en/hu/es/fr/it) with no format-arg mismatches.
- `Scripts/Core/Monster.cs` -- New `EvasionRounds` (int) and `EvasionMissChance` (int 0-100) transient fields for the evasion-buff persistence.
- `Scripts/Systems/MonsterAbilities.cs` -- 9 defensive abilities (`Phase`, `Incorporeal`, `PhaseShift`, `TreeMeld`, `Vanish`, `Flight`, `Invisibility`, `Teleport`, `InkCloud`) now persist evasion onto the monster (`monster.EvasionRounds = 2/3`, `monster.EvasionMissChance = 25/30/35/40/50/55` ability-dependent) when their proc fires. Pre-fix they only set `AbilityResult.AvoidAllDamage` / `EvasionBonus` which nothing in CombatEngine ever read.
- `Scripts/Systems/CombatEngine.cs` -- Three damage paths now roll against `monster.EvasionMissChance` before applying damage: `ExecuteSingleAttack` (before D20, skips entire swing + post-hit enchantments on proc), `ApplySingleMonsterDamage` (spell/ability damage), `ApplyAoEDamage` (per-monster inside the AoE loop). `ProcessMonsterAction` decrements `EvasionRounds` once per cycle in the same block as `WeakenRounds--`; clears `EvasionMissChance` on expiry.
- `Localization/{en,es,fr,it,hu}.json` -- New `combat.evasion_miss` key ("Your attack passes harmlessly through {0}!") in all 5 languages.
- `Scripts/Systems/CombatEngine.cs` -- New `TargetCancelled` sentinel constant (`int.MinValue`) and cancel-input handling (`Q`/`C`/`X`) in `SelectHealTarget`, `SelectBuffTarget`, `GetTargetSelection`. `HandleQuickbarAction` and the basic Attack / Power Strike / Precise Strike / Backstab / Taunt / Disarm case branches all detect the sentinel and `continue` back to the action menu without consuming the action.
- `Localization/{en,es,fr,it,hu}.json` -- New `combat.target_cancel_hint` key ("[Q] Cancel"). `combat.target_monster` / `combat.target_monster_random` updated to advertise the `Q=cancel` hint inline.
- `Scripts/Systems/StrangerEncounterSystem.cs` -- `ContextualDialoguePool` data block's 162 hardcoded English narrative strings replaced with `stranger.*` localization keys. `GenerateEncounter` and `GetResponseOptions` consumption sites updated to resolve keys via `Loc.Get` at consume time. 6 additional fallback strings in the legacy `GetResponseOptions` `else` branch also routed through `Loc.Get` (`stranger.fallback.option.*`).
- `Localization/en.json` -- 168 new English keys under `stranger.*` (162 encounter dialogue keys + 6 fallback strings).
- `Localization/hu.json` -- 168 new Hungarian translations matching the new English keys. Preserves informal stranger voice + mystical/philosophical tone.
- `Localization/es.json` -- 168 new Spanish translations (Spain Spanish, vosotros/vuestro forms). Preserves stranger voice + tone.
- `Localization/fr.json` -- 168 new French translations (informal "tu" register, French guillemets « » for quotes).
- `Localization/it.json` -- 168 new Italian translations (informal "tu" register, guillemets « » matching it.json intimacy convention). Italian's pro-drop nature keeps the stranger's speech crisp. 168/168 stranger keys present across all five languages (en/hu/es/fr/it) with no format-arg mismatches.
- `Scripts/Locations/BaseLocation.cs` -- `GetLocationName` replaced 17-entry hardcoded English switch (with English-enum-name fallback for the other ~30 enum values) with a single `Loc.Get($"location.name.{location}")` call. All consumers (heading-to text, exit list, wizard commands) automatically benefit from localized location names.
- `Localization/{en,hu,es,fr,it}.json` -- 69 new `location.name.*` keys per language covering every `GameLocation` enum value. Full translation pass shipped: 345 new loc strings total.
- `Scripts/Locations/BaseLocation.cs` -- `DisplayTownNPCEncounter` choice rendering switched from `[<full-word-key>]` display to `[1] / [2] / [3]` numbered hotkeys matching the convention of combat-action and healing-target menus. Input matcher accepts number, full data-key (legacy), or unambiguous first-letter as fallback. Affects all 4 memorable-NPC story choices (Pip + 3 others).
- `Scripts/Systems/CombatEngine.cs` -- Voidreaver `lifesteal_20` / `lifesteal_30` effect moved from a post-damage switch case (which double-applied damage and gated the heal on `target.IsAlive`) to an inline block in both `ApplyAbilityEffectsMultiMonster` and `ApplyAbilityEffects`, matching the pattern Shadow Harvest already uses. Heal now scales with `actualDamage` (including Pain Threshold / crit / marked bonuses) and fires regardless of whether the base hit killed. Always emits feedback — new `combat.ability_lifesteal_capped` line for the MaxHP-clamped case so the player gets visible confirmation that the proc fired even when their HP was already full. Post-damage switch cases for both effects neutralized to no-op `break;`.
- `Localization/{en,hu,es,fr,it}.json` -- New `combat.ability_lifesteal_capped` key in all 5 languages for the at-full-HP feedback line.
- `Scripts/Locations/DungeonLocation.cs` -- `RestorePlayerTeammates` now tracks a `skippedCount` across the load loop and emits a yellow overflow warning + hint at the end, matching the existing NPC-teammate overflow pattern. The duplicate-filter check was hoisted above the cap check so already-loaded echoes from a previous dungeon re-entry don't inflate the count. Silent-fail eliminated.
- `Scripts/Locations/TeamCornerLocation.cs` -- `RecruitPlayerAlly` now counts active companions + recruited NPC teammates + already-recruited echoes after a successful add, and emits a yellow ATTENTION line + gray hint if the prospective total exceeds the 4-slot cap. Pre-warning at recruit time, before the player enters the dungeon and is surprised.
- `Localization/{en,hu,es,fr,it}.json` -- 4 new keys per language: `dungeon.echoes_skipped`, `dungeon.echoes_skipped_hint`, `team.recruit_party_overflow`, `team.recruit_party_overflow_hint`.
- `Scripts/Locations/TeamCornerLocation.cs` -- `ConfirmAndRecruit` now re-resolves the `recruit` parameter to a live `NPCSpawnSystem.Instance.ActiveNPCs` reference by ID before any mutation, defending against world-sim `LoadWorldState` mid-recruit creating orphan references. Live-recheck failure path adds an "already on your team" specific refusal when the NPC's Team matches the player's. Diagnostic INFO log when the orphan re-resolution actually swaps the reference.
- `Localization/{en,hu,es,fr,it}.json` -- New `team.recruit_already_on_team` key in all 5 languages.
