# v0.63.2 -- NPC simulation rebalance

A focused pass on the world-sim NPC behavior, driven by 14 days of telemetry pulled from the live server (283,117 decisions across 196 unique NPCs). The picture the data painted was grim: 9,635 NPC deaths against 138 level-ups, a 70-to-1 ratio between dying and progressing. Most NPCs were stuck at exactly Level 50 (the immigrant default) and never advancing, dying in team dungeons they couldn't win, and spending 39% of their time on actions that produced essentially zero outcomes. This release doesn't reshape the world, but it does fix the four most-load-bearing problems.

## XP economy was starving NPC progression

NPCs were earning roughly 14 XP per team-dungeon attempt at the live multiplier (`--npc-xp 3.0`). At Level 50, the next level needs about 150,000 XP. The math didn't work: a top-active NPC running 200 team dungeons in 14 days at 14 XP per attempt accumulates ~2,800 XP, which is less than 2% of what they need to level up once. Even with a perfect zero-death record, NPCs above Level 30 could not progress on the timescale of weeks.

Now: the default NPC XP multiplier is bumped from `0.25f` (BBS default) and `1.0f` (single-player default) to `5.0f` across both code paths. On deploy, the live MUD server's `--npc-xp 3.0` systemd override will also be raised to match. At the new rate, NPCs earn ~70 XP per team-dungeon attempt instead of ~14, so a Level 50 NPC running 200 successful attempts in 14 days accumulates 14,000 XP. Still slow relative to player rates, but now achievable over weeks. The level-up gate itself (`NPCVisitMaster` in `WorldSimulator.cs`) is unchanged because it was always correct: it compares cumulative XP against the cumulative threshold for the next level, which is the right semantics.

## Team dungeon was a meat grinder for low-level NPCs

Team-dungeon outcomes over 14 days: **35.2% died, 16.5% fled, 14.1% aborted wounded, 15.3% completed, 0.14% actually won.** v0.61.5's Gate 0 (block team-dungeon runs for NPCs below Level 5) caught the worst of the slaughter, but the Level 10-19 cohort was still dying at 8-10% per single action. Floor pick at `avgLevel - 7 + random(0, 3)` was still producing matchups naked-equipment NPCs couldn't win.

Three changes:

- **Gate 0 raised from `npc.Level < 5` to `npc.Level < 10`.** Below Level 10, the NPC tells their team they aren't ready and routes to the Inn instead. v0.61.5's floor was set when telemetry showed Lv 1-4 at 100% death rate and Lv 5-9 at 84%; 14 months of additional data shows Lv 5-9 are still effectively unwinnable.
- **Floor pick for sub-Level-20 teams dropped from `avgLevel - 7 to avgLevel - 5` to `avgLevel - 10 to avgLevel - 8`.** Three extra floors of safety margin for naked-gear NPCs.
- **New mid-band floor pick added for Level 20-29 teams** at `avgLevel - 8 to avgLevel - 6` (in between the new low-band and the existing high-band). Lv 30+ teams keep the existing `avgLevel - 5 to avgLevel - 3` pick because they survive at 1-2% death rate per the telemetry.
- **Team-dungeon weight in the action picker is now level-keyed.** Was a flat 0.12 for any NPC with HP > 60% MaxHP. Now: 0.03 for Level < 20 (about a quarter of the old rate), 0.06 for Level 20-29, 0.12 for Level 30+. This shifts the action distribution for low-level NPCs toward inn / dark alley / training instead of routinely walking into the meat grinder.

## Dark Alley and Inn produced almost no outcomes

The two single-most-picked actions in the simulation: dark_alley at 20.1% of all NPC time and inn at 18.8%. Combined: nearly 40% of every NPC action over 14 days. Both were running real handlers (pickpocketing, fencing, drinking, socializing) but the per-action gold deltas averaged near zero and HP deltas were tiny. Telemetry was full of `completed` outcomes with no actual story.

Now there are bigger swings in both:

- **Dark Alley gambling.** Any NPC with > 50 gold has a 30% chance to bet a fraction of their wealth (5-20% scaled by Greed, clamped 10-2000 gold). Win chance is Wisdom-keyed (35% baseline, +0.5% per WIS, capped 65%). Win pays 1.3x-2.0x the bet. Loss is total. WIS-keyed gives high-INT classes a real edge in the alley.
- **Dark Alley mugging gone wrong.** 4% chance of a violent confrontation costing 5%-25% MaxHP. Cowardly NPCs (low Courage) take less damage because they run sooner. Capped so it's a shake-down, not a corpse.
- **Inn drinking tab scales on Sociability.** Was flat 5-20 gold. Now scales with the Sociability trait so social NPCs run tabs up to ~100 gold per visit, capped at a quarter of their gold so the inn can't bankrupt them.
- **Inn drinking too much.** 6% chance the NPC overdoes it: HP loss (3-15% MaxHP, alcohol poisoning) and extra gold lost on the tab. Capped so it's a hangover, not a coffin.
- **Inn brawl.** 5% chance a social interaction at the Inn turns into a fistfight. Both NPCs take HP damage scaled by the other's Aggression. The relationship between them tanks by 3 points. A small news entry posts.

None of these new outcomes kill NPCs outright (HP floors at 1 after each event), because the death telemetry was already inflated by the team-dungeon problem. The point of the new outcomes is to make the 40% of NPC time spent in these two locations actually surface in the data: real gold swings, real HP variance, real relationship changes.

## Bard and prestige NPCs were spawning with no mana

While investigating the telemetry data I ran a sweep of NPC stats on the live server. Found 11 alive caster NPCs (10 Bards and 1 Voidreaver) with `baseMaxMana = 0`. Their songs, lightning bolts, and other spell-equivalent abilities silently fizzled forever. The bug was in the immigrant spawn path, not the v0.63.0 graduation pipeline.

`NPCSpawnSystem.cs` had two places that set initial mana for new NPCs -- the immigrant spawn at line ~1335 and the class-rebalance pass at line ~1192. Both used a hardcoded switch that only knew Magician / Sage / Cleric / Paladin. Everything else fell into `_ => 0`, including Bard, MysticShaman, and all five prestige classes -- all of which the runtime's `Character.IsManaClass` check correctly identifies as needing mana to function. The original four-class list probably came from the very early codebase where those were the only caster classes. The newer classes never got added to this path.

Three "is this class a caster" checks live in the codebase and they don't agree:
- `Character.IsManaClass`: 9 classes (Cleric, Magician, Sage, MysticShaman, all five prestige).
- `WorldSimulator.IsCasterClass` (used at NPC graduation): 6 classes (Magician, Cleric, Sage, Paladin, Bard, MysticShaman).
- The NPCSpawnSystem switch: 4 classes (Magician, Sage, Cleric, Paladin).

Unifying these into a single source of truth is a bigger design pass than belongs in a hotfix, but the immediate fix uses a new `NPCSpawnSystem.GetBaseMaxManaForClass(CharacterClass, int level)` helper. Both NPCSpawnSystem switches now call it instead of inlining the logic. The save-restore path (`WorldSimService.RestoreNPCsFromData`) uses it as the fallback when persisted mana data is missing or zero, which auto-heals the 11 affected NPCs on the first world_state restore after deploy.

Per-class formulas: Magician / Sage stay at `50 + level * 25`. Cleric / Paladin stay at `40 + level * 20`. Bard added at `40 + level * 18`. MysticShaman added at `45 + level * 22`. All five prestige classes added at `50 + level * 22`.

`WorldSimulator.NPCVisitMaster` (the NPC level-up flow) also now grants a class-keyed mana increment alongside the existing HP / STR / DEF increments. Pre-fix, casters that leveled up gained no extra mana, so their spell viability eroded over time as they leveled. Per-level mana gain bands roughly match the spawn formula's scaling.

Players are not affected by any of this. Player casters get their stats from `LevelMasterLocation` and the character creation flow, neither of which goes through these code paths.

## NPCs had no gear, no income, no progression -- the system was structurally broken

After the initial v0.63.2 fixes deployed I pulled fresh telemetry and the numbers were worse than the pre-fix baseline. 3,824 actions across 100 NPCs over several hours: **zero wins, zero level-ups, only 3 rows in the entire dataset earned any XP at all**. Class breakdown showed Paladins were the only class with positive net XP, and net gold was negative or zero for nearly every class. The XP multiplier bump and team-dungeon retuning weren't moving the numbers because of a deeper structural problem nobody had ever audited: **NPCs spawn with no equipment**. `WeapPow = 0`, `ArmPow = 0`. The world-sim combat damage formula is `Math.Max(1, member.Strength + member.WeapPow - target.Defence)`. A naked Lv 50 NPC against a Lv 45 dungeon monster with high Defence does **1 damage per swing** against monsters with 1000+ HP. Combat hits the 40-round cap with no kills landed, telemetry classifies it as `completed` with zero deltas, and nobody ever wins.

Cascading downstream:
1. NPCs spawn naked, so they can't deal damage in combat.
2. They can't win team_dungeons, so they don't earn XP or gold.
3. They can't afford gear, so they stay naked.
4. They can't progress, so they stay stuck at their spawn level.
5. Meanwhile inn drinks (60-100g per visit at the v0.63.2 retune), healer costs, and training fees drain whatever pittance of gold they had to begin with.

Six fixes shipped to break the cycle:

**Fix A: Combat damage floor.** All three world-sim combat damage formulas (`SimulateTeamVsMonsterCombat`, `NPCExploreDungeon`, NPC-vs-NPC) changed from `Math.Max(1, Strength + WeapPow - Defence)` to `Math.Max(Level * 3, Strength + WeapPow - Defence)`. An Lv 50 NPC now does at least 150 damage per swing regardless of gear. The original additive formula still applies when stats + gear outclass the floor.

**Fix B: NPCs spawn with baseline gear.** Immigrant spawn now sets `WeapPow = level * 5` and `ArmPow = level * 4`. A Lv 50 NPC spawns with WeapPow 250, ArmPow 200. Level-up at `NPCVisitMaster` grants +5 WeapPow and +4 ArmPow per level so gear scales with level rather than staying at spawn-time values. The save-restore path (`WorldSimService.RestoreNPCsFromData`) auto-heals existing live NPCs with WeapPow=0 or ArmPow=0 using the same formula, so the live population doesn't have to wait for new immigrants to start contributing in combat.

**Fix C: Inn drinking tab regression.** The earlier v0.63.2 retune scaled the inn tab on Sociability up to ~100 gold per visit, which combined with inn being 19% of all NPC actions was draining the economy faster than any income source could replenish. Tab now scales 5-15g base + up to 10g Sociability scaling (so a sociable NPC drops ~25g instead of ~100g). The tab only fires if the NPC has more than 200g, so poor NPCs skip it rather than going underwater. The "drinking too much" event was also reducing from 6% to 4% trigger rate and the extra-tab portion capped from 50-200g down to 10-30g.

**Fix D: NPC healing is now nominal cost.** `NPCVisitHealer` used the player-grade `(MaxHP - HP) * 2` formula plus city tax. A Lv 50 NPC at half HP was paying 1,500g + tax to heal -- the equivalent of multiple successful gambling sessions, wiped out in one visit. Now NPC healing costs 1g per 10 HP missing (Lv 50 NPC at half HP pays ~75g), capped at a quarter of their carried gold so a poor NPC still gets healed. King's tax is bypassed for NPC heals since the cost is already symbolic. Always heals to full.

**Fix E: Dark Alley income bumped.** Pickpocket trigger 15% -> 25%, Greed threshold 0.6 -> 0.5, payouts roughly doubled (`Math.Min(victim.Gold / 8, 100 + Level * 10)` was `Math.Min(victim.Gold / 10, 50 + Level * 5)`). New "found coin pouch" event at 15% chance with `20 + Level * 3 + random.Next(Level * 2)` payout, giving baseline income to NPCs who'd never pickpocket regardless of personality. Fence trigger 20% -> 30%, payout 33% -> 45% of item value. Gambling win chance 35-65% -> 40-75% (Wisdom-keyed), payout 1.3-2.0x -> 1.5-2.5x. Net effect is that dark_alley actions now have a positive expected value on net gold delta instead of breakeven.

**Fix F: NPCs bank earlier.** Bank action threshold lowered from 1000g to 500g so NPCs stash income earlier in the wealth curve. Deposit percent bumped from 50-80% to 60-90%. Picker weight raised from 0.10 to 0.15 baseline so the bank surfaces more often in the action selection. NPCs build up reserves out of reach of the inn / healer / pickpocket drains.

These six fixes together should restructure the NPC economy from "broke and stuck" to "earning and progressing." Numbers to watch: average net gold per NPC per day flips from negative to positive, average XP per team_dungeon attempt rises from ~0 to ~50+, team_dungeon win rate climbs from 0% to something measurable, NPCs above Lv 30 actually level up. If after a week the numbers still look bad, the next pass will look at whether the combat formula itself needs more rework (currently still a simplified additive model, not the player-grade CombatEngine).

## Post-kill bounty info flashed off-screen too fast

Player report: after killing an NPC for a quest bounty, the post-kill info (looted gold, alignment change, bounty reward, blood price, faction standing drop) appeared briefly then vanished on a 2-second timer before the player could finish reading it. Bounty kills can produce 5-8 lines of stacked output and 2 seconds wasn't enough.

`BaseLocation.AttackNPC` ended with `await Task.Delay(2000)`. Now ends with `await terminal.PressAnyKey()` so the player paces the dismissal. Failed-attack and successful-attack paths both flow through this single chokepoint, so both get the same treatment.

The non-bounty path (`ApplyMurderConsequences`) wasn't affected because it has its own Surrender / Fight prompt that already waits for input.

## History of Monarchs was showing inflated reign days

The Castle's History of Monarchs readout (`[H] History` from the throne) was reporting Days Reigned values higher than the actual reign length. Root cause: `DailySystemManager.ProcessPlayerDailyEvents` was calling `king.ProcessDailyActivities()` once per online player per day boundary. `ProcessDailyActivities` increments `king.TotalReign++` and processes treasury / guard loyalty. With N online players triggering the daily reset each real day, `TotalReign` was being incremented N times instead of 1.

Meanwhile `WorldSimService.ProcessWorldDailyReset` was already doing the same king processing once globally per day, which is the correct behavior. The duplicate call inside `ProcessPlayerDailyEvents` was redundant and inflating both reign days AND treasury changes (guards getting paid N times, treasury crisis checks firing N times, etc.).

Now gated on `!IsOnlineMode`. In online mode, only `WorldSimService.ProcessWorldDailyReset` handles king daily processing. In single-player mode, the original path still runs. The History of Monarchs readout for new monarchs going forward will show correct reign counts. Historical inflated values can't be retroactively corrected (the original true day counts aren't recoverable from the inflated TotalReign), but the bleeding stops on deploy.

## Telemetry has a new column to separate sim writes from external writes

Smaller defensive fix. The `npc_decision_log` table now has a `decision_source` column defaulting to `'sim'`. World-sim writes through `TelemetryWrap` continue to default to `'sim'`. Player murders, PvP, and street encounters that result in NPC death go through different code paths (CombatEngine.HandlePlayerDeath, MarkNPCAsDead) and don't write to `npc_decision_log` at all today, but future work that wires those into the telemetry table will pass `'player'` or `'pvp'` so post-deploy analysis can split sim-driven outcomes from external interference.

A SQLite migration runs on first launch post-deploy: `ALTER TABLE npc_decision_log ADD COLUMN decision_source TEXT DEFAULT 'sim';` -- existing rows pick up the default value, so historic analysis can treat the entire backlog as sim-driven (which is what it was).

## What this isn't

This release does NOT fix everything the telemetry surfaced. Specifically:

- **Settlement is still 10.6% of NPC actions with zero outcomes.** Wiring real outcomes there is a larger settlement-system pass that doesn't belong in a hotfix.
- **The Level-50 immigrant cluster (71,000 actions) is still huge.** Bumping the XP multiplier and reducing team-dungeon deaths should let this cohort progress over the coming weeks, but there's no immediate forced redistribution.
- **The action distribution still skews heavily toward dark_alley + inn.** Tuning weights without watching how the new outcomes affect the distribution would be premature; planning to re-check telemetry 2-3 weeks post-deploy and tune again.
- **Settlement, castle, temple, and move are still 30%+ of NPC time combined with limited outcomes.** Same as above -- watch the data, then tune.

## Expected impact (predictions to check after deploy)

The 14-day baseline I'm comparing against:
- 9,635 deaths, 138 level-ups (70:1 ratio)
- team_dungeon: 35% died, 0.14% won
- dark_alley avg gold delta +1.6, avg XP delta 0
- inn avg gold delta -0.4, avg XP delta 0
- 71K actions at Level 50, zero level-ups

After the changes I expect roughly:
- Death-to-progression ratio drops from 70:1 to 15-25:1 (combination of fewer deaths via Gate 0 and tuned floor pick + more level-ups via 5x XP multiplier)
- team_dungeon death rate drops from 35% to 18-22%
- dark_alley avg gold delta moves to +5 to +15 range (gambling variance), HP variance increases (mugging events)
- inn avg gold delta moves to -3 to -8 range (bigger drink tabs), HP variance increases (drinking-too-much + brawl events)
- Level-50 cluster starts dispersing as NPCs above the cluster begin leveling up
- About 600-1,500 level-ups per 14 days instead of 138

If after 2-3 weeks the numbers don't move into these ranges, the next pass will tune more aggressively. If they move too far (NPCs leveling up too fast, dying too fast, etc.) the pass after that will pull back.

## Files changed

- `Scripts/Core/GameConfig.cs` -- version bump.
- `Scripts/BBS/DoorMode.cs` -- `_npcXpMultiplier` default 0.25 -> 5.0.
- `Scripts/Systems/WorldSimulator.cs` -- `NpcXpMultiplier` default 1.0 -> 5.0; team-dungeon Gate 0 raised to Lv 10; floor pick rebalanced into three bands (sub-20, 20-29, 30+); team-dungeon weight in action picker now level-keyed; `NPCVisitDarkAlley` adds gambling + mugging outcomes (Fix A through F retuned for income, see Fix E); `NPCVisitInn` adds Sociability-scaled drinking tab (Fix C reduced), drinking-too-much penalty (Fix C reduced), and brawl outcome; `NPCVisitMaster` level-up grants class-keyed mana increment + Fix B WeapPow/ArmPow increment; **Fix A combat damage floor** in `SimulateTeamVsMonsterCombat`, `NPCExploreDungeon`, and the NPC-vs-NPC fight path (all three use `Math.Max(Level * 3, Strength + WeapPow - Defence)` instead of `Math.Max(1, ...)`); **Fix D NPCVisitHealer rewritten** to use nominal cost (1g per 10 HP missing, capped at 25% of carried gold, no city tax, always heals to full); **Fix F bank picker threshold** lowered to 500g, weight bumped to 0.15, NPCVisitBank deposit threshold lowered to 500g and percent bumped to 60-90%.
- `Scripts/Systems/SqlSaveBackend.cs` -- `npc_decision_log` schema gains `decision_source TEXT DEFAULT 'sim'`; idempotent ALTER TABLE migration; `LogNPCDecision` accepts new optional `decisionSource` parameter.
- `Scripts/Systems/NPCSpawnSystem.cs` -- new `GetBaseMaxManaForClass` helper as single source of truth; two inline mana switches (immigrant spawn + class rebalance) replaced with calls to it; **Fix B immigrant spawn** now sets `WeapPow = level * 5` and `ArmPow = level * 4`.
- `Scripts/Systems/WorldSimService.cs` -- `RestoreNPCsFromData` heal-on-load for `BaseMaxMana`, `Mana`, `MaxMana` using the new helper as fallback so existing affected NPCs auto-heal on first restore after deploy; **Fix B WeapPow/ArmPow heal-on-load** (`data.WeapPow > 0 ? data.WeapPow : data.Level * 5`, same for ArmPow) so existing live NPCs spawned with 0 gear get backfilled; **BaseWeapPow/BaseArmPow heal-on-load** added so the gear power survives `RecalculateStats()` (which resets WeapPow/ArmPow to 0 and rebuilds from equipped items -- NPCs have no items so they ended up at 0 forever before this fix).
- `Scripts/Core/Character.cs` -- new `BaseWeapPow` / `BaseArmPow` fields; `RecalculateStats()` now starts WeapPow/ArmPow from those base values instead of 0, so the intrinsic gear power for NPCs (who don't equip items) survives the recalc.
- `Scripts/Systems/SaveDataStructures.cs` -- `NPCData` gains `BaseWeapPow` / `BaseArmPow` fields for save round-trip.
- `Scripts/Systems/OnlineStateManager.cs` -- NPC save path serializes `BaseWeapPow` / `BaseArmPow`.
- `Scripts/Systems/DailySystemManager.cs` -- duplicate `king.ProcessDailyActivities()` block in `ProcessPlayerDailyEvents` gated on `!IsOnlineMode` so the king's TotalReign / treasury / guard processing only runs once globally per day in online mode (via `WorldSimService.ProcessWorldDailyReset`) instead of once per online player per day.
- `Scripts/Locations/BaseLocation.cs` -- post-kill `await Task.Delay(2000)` in `AttackNPC` replaced with `await terminal.PressAnyKey()` so the player can read the looted/alignment/bounty/blood-price/faction stack at their own pace.

## Deploy notes

On deploy, the live systemd config `/etc/systemd/system/usurper-mud.service` currently has `--npc-xp 3.0`. Update to either remove the flag (so the new 5.0 code default takes effect) or set explicitly to a chosen value. Keeping the explicit flag in systemd is recommended for operational visibility.

The SQLite migration runs automatically on first launch post-deploy and is idempotent.

## Build status

- Build clean.
- 753/753 tests passing.
