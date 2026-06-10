# v0.64.0 -- The Brain

## TL;DR

- NPCs in the world simulator finally have agency. They look at their goals, their personality, their recent memories, their gear, and what happened to them last time they did this exact thing -- and they pick the next action based on all of that. The weighted-random Markov picker that has been driving 250 NPCs since the BBS port is replaced.
- The world stops being a meat grinder. Two weeks of telemetry showed NPCs were dying at 70 times the rate they were progressing, spawning into the dungeon with no gear, and burning their pittance of gold on inn drinks before they could ever afford a sword. The economy is structurally reworked so NPCs actually get somewhere.
- High-stakes NPCs (kings, your teammates, anyone at level 30 or higher, court members) now fight real combat. They cast spells. They drink potions. They cleave with AoE abilities. They taunt monsters. They die to status effects you watched the player learn to fear three patches ago.
- LLM moments. Optional. Online-only. Server-key. Daily token cap. Heuristic fallback always works so the game runs identically when no API key is configured. When the LLM IS configured, it writes the dramatic news flash when an NPC kills the one who killed their parent, the eulogy when a notable NPC falls, the first-impression flavor when you examine someone at Team Corner, the personalized mood prefix and memory callback and witness allusion and faction-tension line that wraps every line of NPC dialogue, the long-arc strategic goal stack that shapes an NPC's behavior across thousands of subsequent ticks, and the accept/hesitate/refuse decision when you propose marriage.
- A live LLM telemetry dashboard at https://usurper-reborn.net/balance.html tracks every call: success rate, token spend, cost in dollars, what each NPC actually said. Sysops can watch the LLM activity in real time and tune from the data.
- Net effect for the player: the simulated world feels less like a screensaver and more like a place where people are doing things for reasons. The NPCs around you are progressing, dying for real reasons, gossipping in their own voices, holding grudges that lead somewhere, and occasionally making decisions that surprise you.

---

## What it means at the table

For a long time the world simulator has been a beautifully detailed Rube Goldberg machine that mostly produced nothing. NPCs had personalities, relationships, memory systems, emotional states, a goal stack. They had jobs to do. They had places to go. And then, every tick, a weighted-random number generator picked one of seventeen verbs and the NPC went and did that thing regardless of anything else. They spawned naked, they fought monsters they couldn't damage, they got drained by inn tabs they couldn't afford, they died.

Walk into the dungeon now and the NPCs you bump into are actually advancing. The Lv 14 fighter you saw last week is Lv 16 now because she's been winning her team dungeons instead of dying in them. Her wounds heal at the healer for a sane price. Her share of the gambling pot at the Dark Alley pays off two times out of three because she has the Wisdom for it. She bought a sword. She's saving for armor.

Walk past the Inn and a sociable Bard is running a forty-gold tab instead of getting bankrupt at a hundred. Three tables over a brawl is breaking out because two NPCs with bad blood between them ended up at the same bar. Tomorrow the news feed will mention it.

Engage one of them in conversation and the line they greet you with is colored by their current mood. *Scowling, they say...* if they're angry. *Eyes alight, they say...* if they're proud. If you bailed them out of trouble last week the line that follows mentions you "haven't forgotten" what you did for them. If you killed their cousin in a back alley and they witnessed it, the line that follows alludes to it sideways. None of it spells out the event. The NPC just sounds like someone who remembers.

Propose to them and the response isn't a die roll against personality stats. It's a decision, made by an actor that read your full relationship history, your charisma modifier, the heuristic acceptance chance, and their own personality, then picked accept or hesitate or refuse on character grounds. Sometimes they'll surprise you.

Examine a notable NPC at Team Corner and the first-impression paragraph at the top reads like something a stranger would say after spending five minutes with them. *"Korr Stonewarden carries himself like a man who has counted out coin in every shop in the city and is still not done counting. His hands are merchant's hands; his eyes are not."* This is generated once per NPC and cached for the next examine.

Die in the dungeon while pursuing an Old God and the news feed will sometimes carry a eulogy that talks about who you were, what you were doing, and who put the blade in. Not always. Not for every commoner. But for named characters and people who mattered.

None of this is the game's narrative spine. The game still plays the same way. But the world around you is noisier, more specific, more alive.

---

## What it means under the hood

For the sysop running the live server: this release introduces an optional dependency on an external LLM API (Anthropic, OpenAI, OpenRouter, or any OpenAI-compatible endpoint -- the existing http chat-completions provider works with all of them). The dependency is opt-in via environment variables. Without a key configured, every LLM code path falls through to its heuristic template and the game runs identically to the pre-Brain-v2 baseline.

When configured, the cost envelope at a 250-NPC population with a moderately active player base lands at roughly $1-3 per day on Sonnet 4.6, with a per-server daily token cap (default 500,000 tokens) that hard-blocks runaway spending. Every LLM call writes to a new `llm_usage` SQLite table -- one row per attempt with moment type, NPC name, success/failure, token counts, latency, rendered text, failure reason -- and the existing balance dashboard gets a new LLM Moments tab that reads it. You can see every call in real time, what it cost, what it produced, what failed and why.

The deploy step adds one systemd drop-in (`/etc/systemd/system/usurper-mud.service.d/llm.conf`) with five `Environment=` directives. The game restart picks it up and the LLM starts firing within seconds. Killing the file and restarting the service turns it off. No code change, no rebuild, no save migration.

The Brain v2 work itself is rollout-safe. A new `IsAIDriven` flag on Character defaults to false; pre-v0.64.0 NPCs in the save deserialize with it false and continue running through the legacy weighted-random picker exactly as before. New arrivals (immigrants, child graduates, orphans aging out) spawn with it true and run through the new utility scorer. The cohort grows organically over weeks as the population turns over. Telemetry on every NPC decision logs the cohort tag so dashboard pivots can compare them directly.

One caveat worth knowing: the LLM strategic-goal layer (Slice 12a) originally gated on the same `IsAIDriven` cohort flag, but a live audit after first deploy showed the cohort had stayed at zero NPCs across 244 in the population (organic turnover hadn't fired the creation paths). The gate was dropped for strategic goals specifically, since that layer is additive (it augments the existing reactive goal stack rather than replacing the picker) and the cohort split was only meaningful for picker comparability. A startup stagger spreads first-time refreshes across six hours so a restart doesn't fire 244 simultaneous HTTP calls.

The combat engine players use is completely untouched. All Brain v2 combat code is a parallel headless module that runs without terminal I/O, without GMCP emits, without broadcast plumbing. The two paths share nothing structurally.

---

## How this release got the way it is

This release rolls two bodies of work together. The v0.63.2 release notes were drafted, the code was built and tested, and then it stayed on the shelf instead of going to the live server -- because the architectural problem the v0.63.2 telemetry pass uncovered was bigger than the tuning fixes could solve, and starting Brain v2 became the right call. So the v0.63.2 fixes ship inside v0.64.0 as the foundational layer that makes the Brain v2 layer's job possible. The two halves are not independent; they were always going to ship together.

---

## Part 1: Making the NPC economy actually work

Fourteen days of telemetry from the live server before this release: 283,117 NPC decisions, 9,635 deaths, 138 level-ups. The death-to-progression ratio was seventy to one. Seventy-one thousand of those actions were performed by NPCs stuck at exactly Level 50 (the immigrant default) and never advancing. The team-dungeon path was a meat grinder where over a third of attempts ended in death and less than a sixth of one percent ended in a win. Dark Alley and Inn together took up nearly forty percent of all NPC time and produced average gold deltas near zero. The whole simulation was running, every system was firing, and almost nothing was happening.

A handful of structurally-load-bearing problems turned out to be doing most of the damage.

### The math wasn't working

At the live server's NPC XP multiplier of 3.0, a successful team-dungeon attempt was worth about fourteen XP. The next level at Level 50 needs about a hundred and fifty thousand XP. A maximally-active NPC running two hundred team dungeons in two weeks at fourteen XP per attempt accumulates twenty-eight hundred XP -- less than two percent of what they need to level up once. Even if every single attempt was a win, NPCs above Level 30 mathematically could not progress on the timescale of weeks.

The fix is unromantic: the default NPC XP multiplier is bumped from 1.0 (single-player) and 0.25 (BBS) to 5.0 across both code paths, and the live systemd `--npc-xp` flag is raised to match. At the new rate, NPCs earn around seventy XP per attempt. Two hundred successful attempts in two weeks now accumulates fourteen thousand XP. Still slow by player standards, but achievable over weeks instead of impossible over months. The level-up gate itself was always correct; what was broken was the rate it was being fed.

### Team dungeons were eating low-level NPCs

The Level 1-4 cohort was dying in team dungeons at 100% per attempt. The Level 5-9 cohort at 84%. The Level 10-19 cohort at 8-10% per single action. v0.61.5's Gate 0 (block team-dungeon for NPCs under Level 5) caught the worst of it, but the gate was set too low. Naked-equipment NPCs in their early teens were still walking into floor matchups they could not win.

Now Gate 0 blocks team dungeons until Level 10. Below that, the NPC tells their team they aren't ready and routes to the Inn instead. Floor pick was rebalanced into three bands: sub-Level-20 teams pick `avgLevel - 10 to avgLevel - 8` (three extra floors of safety margin), Level 20-29 teams pick `avgLevel - 8 to avgLevel - 6` (a new mid-band that didn't exist), Level 30+ teams keep the existing `avgLevel - 5 to avgLevel - 3` pick because telemetry showed they survive it. Team-dungeon weight in the action picker is now level-keyed: 0.03 for Level < 20 (about a quarter of the old flat rate), 0.06 for Level 20-29, 0.12 for Level 30+. Low-level NPCs now spend their time in the Inn, the Dark Alley, the training yard -- places where they can build gear and gold instead of routinely dying.

### Dark Alley and Inn were dead zones

The two single-most-picked NPC actions were Dark Alley (20.1% of all NPC time) and Inn (18.8%). Combined, nearly forty percent of every NPC action over two weeks. Both were running real handlers -- pickpocketing, fencing, drinking, socializing -- but the average gold and HP deltas per action were close to zero. Telemetry was full of `completed` outcomes with no actual story behind them.

Both now produce real outcomes. Dark Alley gets a gambling pool: any NPC with more than fifty gold has a thirty percent chance to bet a fraction of their wealth (Greed-scaled), with a Wisdom-keyed win chance (35-65%, scales with INT) and a 1.3x to 2.0x payout on win. Loss takes the whole bet. Mugging-gone-wrong fires four percent of the time, costing 5-25% of MaxHP (less for cowardly NPCs because they run sooner). Pickpocket rates and payouts are bumped, fencing payouts are bumped, a new "found coin pouch" event gives baseline income to NPCs who'd never pickpocket on personality grounds.

The Inn gets a drinking tab that scales on Sociability up to about twenty-five gold per visit (the initial v0.63.2 retune had it scaling to a hundred, which combined with Inn being a fifth of all NPC actions was draining the economy faster than any income source could replenish; that got cut back). A six percent chance of drinking-too-much costs HP and extra gold. A five percent chance of a brawl during a social interaction has both NPCs take damage scaled by the other's Aggression, tanks the relationship between them, and posts a news entry.

None of the new outcomes can kill outright. HP floors at one after each event. The point isn't to add to the death count (already too high from the team-dungeon problem); the point is to make the forty percent of NPC time spent in these places produce real swings instead of zero deltas.

### Bards were spawning with no mana

A sweep of live NPC stats turned up eleven alive caster NPCs (ten Bards and a Voidreaver) with `baseMaxMana = 0`. Their songs, lightning bolts, and other spell-equivalent abilities had been silently fizzling forever. The bug was in the immigrant spawn path: a hardcoded switch that initialized mana for only Magician, Sage, Cleric, and Paladin. Everything else fell to a default zero, including every newer caster class.

A new `NPCSpawnSystem.GetBaseMaxManaForClass(class, level)` helper is the single source of truth now. Both spawn paths use it. The save-restore path uses it as the fallback when persisted mana is missing or zero, so the eleven affected NPCs auto-heal on the first world-state restore after deploy. Per-class formulas are Magician/Sage at `50 + level * 25`, Cleric/Paladin at `40 + level * 20`, Bard at `40 + level * 18`, MysticShaman at `45 + level * 22`, prestige classes at `50 + level * 22`. NPC level-up also now grants a class-keyed mana increment alongside the existing HP/STR/DEF gains, so casters that level up don't have their spell viability erode over time.

(Three different "is this class a caster" checks live in the codebase and they don't agree with each other. Unifying them needs a real design call about Paladin/Bard/Alchemist's mana-vs-stamina status, which doesn't belong in a fix that's already this big. Logged for later.)

### NPCs spawning naked

This one is the worst. The initial v0.63.2 fixes were locally tested and the numbers were *worse* than before: 3,824 actions, zero wins, zero level-ups, only three rows in the entire dataset earning any XP at all. The XP multiplier bump didn't matter because nobody was getting XP. The reason: NPCs spawn with no equipment. `WeapPow = 0`, `ArmPow = 0`. The world-sim combat formula is `Math.Max(1, Strength + WeapPow - Defence)`. A naked Lv 50 NPC against a Lv 45 dungeon monster with high Defence does *one damage per swing*. Combat hits the forty-round cap with no kills, telemetry classifies it as `completed` with zero deltas, and nothing ever happens.

The cascade: NPCs spawn naked, can't deal damage, can't win team dungeons, can't earn XP or gold, can't afford gear, stay naked, stay stuck. Meanwhile inn drinks and healer fees burn whatever pittance they had. Six fixes break the cycle:

The combat damage floor in all three world-sim combat formulas changes from `Math.Max(1, ...)` to `Math.Max(Level * 3, ...)`. A Lv 50 NPC now does at least 150 damage per swing regardless of gear. Stats-plus-gear still apply when they exceed the floor.

Immigrant spawn now sets `WeapPow = level * 5` and `ArmPow = level * 4`. A Lv 50 NPC spawns with WeapPow 250, ArmPow 200. Level-up grants +5 WeapPow and +4 ArmPow per level. The save-restore path heals existing live NPCs with WeapPow or ArmPow at zero, so the live population doesn't have to wait for new immigrants to start contributing.

NPC healing cost goes from the player-grade `(MaxHP - HP) * 2` (plus city tax) to a nominal 1g per 10 HP missing, capped at a quarter of the NPC's carried gold, no tax. A Lv 50 NPC at half HP now pays around seventy-five gold to heal to full instead of fifteen hundred plus tax.

Bank action threshold lowers from 1000g to 500g so NPCs stash income earlier. Deposit percent bumps from 50-80% to 60-90%. Picker weight raises from 0.10 to 0.15 so the bank surfaces more often in selection. NPCs build reserves out of reach of the inn/healer/pickpocket drains.

Dark Alley income bumps: pickpocket trigger 15% → 25% with payouts doubled, a new found-coin-pouch event at 15% giving baseline income to NPCs who wouldn't pickpocket on personality grounds, fence trigger 20% → 30% with payout 33% → 45%, gambling win chance lifted, payouts increased.

Net effect: average net gold per NPC per day flips from negative to positive, average XP per team-dungeon attempt rises from near zero to something measurable, NPCs above Level 30 actually start leveling up. (To be re-checked against telemetry two to three weeks after deploy.)

### Smaller fixes that came along for the ride

The post-kill bounty info screen used to dismiss itself on a two-second timer. Bounty kills can produce five to eight lines of stacked output (looted gold, alignment change, bounty reward, blood price, faction standing drop) and two seconds was not enough. Now ends with PressAnyKey so the player paces the dismissal.

The Castle's History of Monarchs readout was reporting inflated reign-day counts. `DailySystemManager.ProcessPlayerDailyEvents` was calling `king.ProcessDailyActivities()` once per online player per day boundary; the method increments `TotalReign`, processes treasury, pays guards. With N online players triggering daily resets each real day, all of that was happening N times instead of one. Meanwhile `WorldSimService.ProcessWorldDailyReset` was already doing the same king processing once globally per day. The duplicate call is now gated on `!IsOnlineMode`; in online mode only the world-sim path runs, in single-player the original path still works.

The `npc_decision_log` table gains a `decision_source` column defaulting to `'sim'`. World-sim writes default to `'sim'`. Player murders and PvP kills don't write to this table today, but future work that wires them in will pass `'player'` or `'pvp'` so post-deploy analysis can split sim outcomes from external interference. An idempotent ALTER TABLE migration adds the column on first launch; existing rows get the default.

---

## Part 2: Brain v2

The premise of Brain v2 is that the world simulator's NPCs already have everything they need to make real decisions -- they just aren't using it. The NPCBrain class has been in the codebase since Phase 21 with a goal stack, an emotional state, a memory system, a personality profile, a relationship manager, and a scorer that ranks actions by goal alignment and personality preference. Every tick, `WorldSimulator.SimulateStep` calls `npc.Brain.DecideNextAction(worldState)`, captures the result into a local variable, passes it to a handler that recognizes eight of the twenty-seven possible actions, and then immediately calls a different function (`ProcessNPCActivities`) that rolls fresh weighted-random verbs from a seventeen-verb picker. The picker wins every round. The Brain's output is discarded.

Brain v2 is the work of stopping that discard, then building on top of what's underneath.

### Wiring up the Brain

A new `Character.IsAIDriven` flag (default false) splits the NPC population into two cohorts. Pre-existing NPCs in the save deserialize with the flag false and continue through the legacy picker; new arrivals (immigrants, child graduates, orphans aging out) spawn with it true and run through the Brain v2 path. The cohort grows organically as the population turns over. Telemetry on every decision tags which cohort it came from, so the live balance dashboard can pivot on the column and compare progression metrics directly.

For Brain v2 NPCs, a new `DispatchBrainAction` helper takes the Brain's chosen action and routes it through the picker's rich `NPC*` methods (`NPCGoShopping`, `NPCTrainAtGym`, `NPCVisitHealer`, `NPCVisitInn`, `NPCVisitLoveStreet`, `NPCVisitDarkAlley`, `NPCTeamRecruitment`) instead of the thin legacy handlers (which were things like "+MaxHP/4 self-heal" for Rest, or "30% chance of `Random(10,31)` XP" for Train). The Brain's coarse action enum maps onto the picker's fine-grained verbs: Explore → move, Trade → shop, Socialize → inn or love_street, Rest → heal or inn (HP-gated), Train → train, Steal → dark_alley, JoinGang → team_recruit. Dispatched actions use the same verb strings as the legacy picker so dashboard pivots work uniformly across both cohorts.

### A scorer that picks actions like a person would

The legacy picker is a weighted-random Markov chain over seventeen verbs with no awareness of what the NPC actually wants. The Brain v2 scorer replaces the random pick with a multi-layer ranking:

The first layer reads `npc.Brain.Goals.GetPriorityGoal()` and boosts verb families by goal type. Economic goals lift shop / marketplace / bank / dark_alley. Social goals lift inn / love_street / team_recruit / go_home / castle / settlement. Personal goals lift train / levelup / heal / temple. Combat goals lift dungeon / team_dungeon / train / heal. Name-keyed fine boosts go further: a "Find Better Weapons" goal lifts shop 1.4x, "Find Life Partner" lifts love_street 1.4x, "Defend Territory" lifts team_dungeon 1.3x, "Make Friends" lifts inn 1.3x, "Gang" lifts team_recruit 1.5x, "Health" lifts heal 1.5x, "Spread Faith" lifts temple 1.4x.

The second layer is need satisfaction. An NPC at less than 30% HP gets a 5x boost on heal and a 0.15x penalty on dungeon -- it will not pick a fight while bleeding out. At less than 50% HP, 2.5x heal and 0.5x dungeon. At less than 70%, 1.3x heal. An NPC with the XP to level up gets a 3x boost on the levelup verb. A gear gap (live `WeapPow < Level * 12` or `ArmPow < Level * 10`) plus enough gold gets a 1.8x boost on shop (a 40% gap reads as critical and dominates the pick; a smaller gap reads as moderate and contributes more gently). A broke NPC with bank deposits gets a 2x boost on bank. A low-level NPC with gold gets a 1.3x boost on train.

The third layer is recency. An NPC who did the same verb within the past five minutes gets a 0.3x penalty on picking it again; within thirty minutes, 0.6x; within two hours, 0.85x. This stops NPCs from locking onto a single high-utility verb and doing it sixty times in a row.

The fourth layer (from Slice 3) is combat-outcome feedback. Recent Attacked memories from the past four hours suppress combat verbs proportional to the total importance of the recent damage taken. An NPC who just got beat up will pick something other than another fight, for a while.

Final score is `baseWeight * goalAlignment * needSatisfaction * recencyPenalty * combatFeedback`. Argmax wins. NPCs with Impulsiveness above 0.7 have a 20% chance to softmax-sample over the full set instead, preserving the impulsive-NPC variety the legacy picker had baked in.

The combined effect: NPCs with Economic goals bias toward shop and marketplace. NPCs with Personal goals bias toward train and heal. Wounded NPCs aggressively heal. XP-ready NPCs reliably level up. Under-geared NPCs with gold consistently shop until the gap closes. The same verb doesn't repeat. The whole pipeline took 13 new scorer tests to nail down.

### Goals that come from things that happened

The reactive goal triggers in the existing GoalSystem are good at picking up immediate needs (low gold → "Earn Money", low HP → "Heal Wounds"). They're worse at promoting goals from things that happened to the NPC's life -- which is exactly the kind of thing that should drive long-arc behavior.

Three new family-event triggers leverage the v0.63.0 memory types. A `KilledMyParent` or `KilledMyFamily` memory from the past month, top two by importance, promotes an "Avenge {killer}" Combat goal at priority `min(1.0, Vengefulness * 1.2 + 0.1)` with the killer set as TargetCharacter. A `FamilyMemberBorn` memory from the past week promotes a "Protect Family" Social goal at priority `min(1.0, Loyalty * 0.8 + 0.2)`. A `LostFamilyMember` memory from the past five days promotes a "Mourn the Dead" Personal goal at priority 0.6 and adds a three-day Sadness emotion so the emotional state matches.

Six new goal-completion detectors stop stale goals from biasing decisions after the underlying need is met. Weapon and equipment goals complete when `BaseWeapPow >= Level * 8`. Magic-item goals complete at `Level * 6`. Mana potion goals at five-plus in stash. Health goals at 90%+ HP. Life partner goals when married. Friends goals at five-plus known characters. Earn money at 1000g. Elite status at Level 30. Family revenge completes when the target NPC is dead (or no longer in the world).

When a goal completes, `OnGoalCompleted` fires emotional consequences and gossip. Avenge clears Anger and grants Confidence -- catharsis. Mourn grants Peace and Hope, clears Sadness. Protect Family grants Pride and Joy. Life Partner grants Joy and Peace. The family-revenge case posts a weighty news beat: "{NPC} has avenged the blood of their kin."

### Real combat for NPCs that matter

The legacy world-sim combat is a damage trade: ally HP minus monster HP minus rounds. No abilities, no potions, no status effects, no buffs. Good enough for two hundred NPCs running in the background; not good enough for the king, your teammates, the court members, the named characters at Level 30+. For those NPCs (Tier A, about fifty of a two-hundred-NPC population), Brain v2 ships a parallel headless combat module.

`NPCCombatBrain` is a per-round action picker. It looks at the monster lineup, the ally lineup, the NPC's class, the cooldowns, and decides: use a healing potion (self or hurt ally, with class-keyed thresholds -- Cleric/Paladin/Alchemist/Sage heal at 70%, Warrior/Barbarian tough it out to 35%), use a mana potion (caster classes when low), use an ability (best affordable damage or heal versus the basic-attack baseline), or basic attack. AoE is preferred when three or more monsters are alive. The lowest-HP monster is targeted.

`NPCCombatSimulator` runs the actual combat. Round by round: each ally picks via the brain and dispatches; each alive monster attacks a random ally with halved-damage formula (matches the abstract-sim convention to keep cross-cohort telemetry comparable); win check; cooldown decrement. Max twenty rounds. Partial XP credit on death or flee, proportional to the damage the dying NPC dealt.

Ability dispatch handles three kinds. Heals scale on Wisdom: `BaseHealing * (1 + (WIS - 10) * 0.03)`. Attack and Debuff abilities scale on Strength: `BaseDamage * (1 + (STR - 10) * 0.04)`. AoE abilities tag every alive monster at 60% damage. Status effects (stun, poison, burn, slow, weaken) apply via the ability's SpecialEffect string. Buff and defense abilities use the existing Character TempAttackBonus / TempDefenseBonus / TempDamageReductionPercent / HasStatusImmunity fields, so an NPC casting Shield Wall actually gets the 30% damage reduction; an NPC casting Resist All actually becomes status-immune; an NPC casting Battle Cry actually swings 20% harder. Equipment-enchant procs read `GetEquipmentLifeSteal()` and `GetEquipmentManaSteal()` (existing summing methods); an NPC who bought a Lifedrinker weapon via the Slice 6 gear loop actually procs lifesteal in combat.

A Tier A team-leader brings the whole team through real combat. A king with four Lv 5 immigrant teammates fights as a five-character party. The Magician casts Fireball. The Warrior drinks a healing potion at 50%. The party wins in five rounds, earns XP and gold, drops loot via the existing NPCItemGenerator. The dashboard tags the row `is_ai_driven=true` and shows the win.

Compare the same scenario on a Tier B (legacy) party: a fifteen-round damage trade with no abilities, around a 35% win rate. The cohort split makes the difference visible.

(Deliberately not in this slice: boss mechanics. Tier A NPCs are kept off Old God floors. Adding boss support is its own design pass.)

### Closing the gear loop

The scorer's gear-gap signal was structurally broken on first ship. The comparison was `npc.BaseWeapPow < Level * 5`, but `BaseWeapPow` is a spawn-time intrinsic value that's never modified by equipment. A well-equipped NPC with `WeapPow = 200` from a real sword still read as exactly-at-baseline because the comparison ignored gear entirely. The gear-gap boost fired approximately never. NPCs shopped randomly, the gear loop never closed.

The fix reads live `npc.WeapPow` and `npc.ArmPow` (post-RecalculateStats, gear-influenced values), raises the expected baseline to `Level * 12` for weapon and `Level * 10` for armor so spawn-intrinsic gear registers as a 40% gap and triggers the critical boost, and adds an armor-gap boost mirroring the weapon-gap (critical 1.5x, moderate 1.2x; smaller than weapon's because weapon matters more for win rate). The loop closes for real: a Lv 10 immigrant spawns with critical gaps in both → scorer 2.7x boost dominates → picks shop → NPCGoShopping buys and equips → live WeapPow grows → scorer sees the improvement → shop boost decays → NPC shifts to train/levelup/dungeon per top goal. Iterative gear progression replaces random shopping.

### Cleanup pass (Slice 7)

The NPC class had two parallel sets of brain-state fields: `npc.EmotionalState` / `npc.Memory` / `npc.Goals` / `npc.Relationships`, and `npc.Brain.Emotions` / `npc.Brain.Memory` / `npc.Brain.Goals` / `npc.Brain.Relationships`. They were separate instances. The picker wrote to `npc.EmotionalState`; the DialogueEnhancer and the Brain pipeline read from `npc.Brain.Emotions`. Writes were invisible to reads. Fixed by initializing the Brain first and aliasing the four convenience fields to the Brain's internal instances. Single source of truth.

Court members were missed in the Tier A predicate. The original IsTierANPC was `king + player-team + Level 30+`, and court members (Royal Advisor, Spymaster, Marshal, Chaplain) were deferred because there was no single bool. Fixed by walking `CastleLocation.GetCurrentKing()?.CourtMembers` and matching by name. Court members now get real combat.

---

## Part 3: LLM-rendered moments

Optional. Online-mode only. Server-side key. Daily token cap. Heuristic fallback ALWAYS works so the game runs identically to the pre-Brain-v2 baseline if no key is configured.

The LLM is decorative for dialogue (Slice 11) and additive for goals and forks (Slices 12a-b). Sync paths are unchanged. Background calls fail silently and the caller already returned the templated fallback. Cache is transient (JsonIgnore fields), so server restart re-warms naturally with no save bloat. Rollback safe: redeploy any prior 0.64.0 build.

### The provider abstraction (Slice 5)

Four new files under `Scripts/Systems/`. `LLMSettings` reads environment variables: `USURPER_LLM_ENABLED`, `USURPER_LLM_ENDPOINT`, `USURPER_LLM_API_KEY`, `USURPER_LLM_MODEL`, `USURPER_LLM_DAILY_TOKEN_CAP` (default 500,000), `USURPER_LLM_TIMEOUT_MS` (default 3000). `IsActive()` returns true only when all four required vars are set and the process is in online mode. Single-player and BBS skip the LLM entirely.

`LLMBudget` tracks per-server daily token usage, thread-safe via Interlocked and a lock. `TokensUsedToday`, `CanSpend(estimated)`, `RecordUsage(actual)`, `Reset()` -- automatic UTC date rollover. In-memory only for now; server restart resets the day counter.

`LLMProvider` defines the interface plus an OpenAI-compatible HTTP implementation targeting the standard `/v1/chat/completions` request shape. The same provider works against OpenAI direct, Anthropic's OpenAI-compatible endpoint, OpenRouter, or local Ollama running in OpenAI-compat mode. Bearer auth. Per-request timeout via linked cancellation tokens. Budget gate before the HTTP call (estimated as `(input chars + max_tokens) / 4`, the usual heuristic). Budget commit after the response, using actual token counts from the API response if present or estimates as fallback. `Get()` returns null when disabled, misconfigured, or offline. `CompleteAsync` returns null on any failure -- timeout, network error, non-success HTTP, parse error, budget exhausted.

`LLMMoments` is where the in-world generators live. Each generator:
1. Captures a context snapshot from the NPC before awaiting anything, so the async path sees stable values if world-sim state changes mid-call.
2. Composes the templated fallback up front.
3. Fires `Task.Run` to do the LLM call. Null provider → fallback. Null result → fallback. Valid result → sanitize and use.
4. Records the attempt to telemetry in a `finally` block so success and failure both land in the dashboard.

The first moment shipped (also Slice 5) was `PostAvengeNewsAsync`: when the Slice 3 family-revenge goal completes (an NPC kills the one who killed their parent), this fires a dramatic news flash. The same pattern grows from there.

### Watching it all work (Slice 10)

Slice 5's `LLMBudget` was in-memory and deferred persistent telemetry to a follow-up. Slice 10 is that follow-up: a new `llm_usage` SQLite table records one row per LLM attempt. Schema: moment_type, npc_name, succeeded, prompt_tokens, completion_tokens, total_tokens, response_ms, rendered_text, failure_reason, created_at. Indexed on created_at and on (moment_type, created_at). Created idempotently on startup.

`SqlSaveBackend.RecordLLMUsage` writes the row. The recording call is wrapped in try/catch so a telemetry failure never breaks the moment generator.

`LLMResponse` extended from `string?` to a class with Text + PromptTokens + CompletionTokens + TotalTokens + ResponseMs. The HTTP provider populates these via Stopwatch and API usage parsing (with fallback estimates).

A new `/api/balance/llm-stats` endpoint on the web proxy returns the aggregate: last 24 hours (calls, success rate, tokens, average latency, estimated cost in USD using Sonnet 4.6 pricing of $3/M prompt + $15/M completion), today (calls, tokens, percent-of-cap, remaining), all time, byMomentType breakdown, failure histogram, last ten successful renders with rendered text.

A new LLM Moments tab on `web/balance.html` renders all of it: a status banner color-coded by success rate, an eight-card overview grid, a budget bar, per-moment-type and failure tables, recent renders styled with a border-left accent. Every subsequent slice (11, 12a, 12b) writes new moment_type values; the byMomentType breakdown picks them up automatically. No web change needed for new moments.

Activating the live LLM took one systemd drop-in (`/etc/systemd/system/usurper-mud.service.d/llm.conf`) with the five Environment= directives and a `daemon-reload + restart`. The first deploy of the drop-in had spaces between directives instead of newlines; systemd parsed them as one giant variable and every call recorded `llm_disabled`. A `sed` replacing the spaces with newlines fixed it. End-to-end verified within thirty seconds of the corrected restart: two death epitaphs at 203 and 208 tokens, 2.3 and 2.7 seconds.

### Em-dash sanitization

Sonnet 4.6 likes em-dashes. Project rule forbids them in player-facing strings. Defense in depth: every system prompt gets a "Use ASCII punctuation only" rule appended, and the output sanitizer collapses any em-dash / en-dash / horizontal ellipsis / curly quote that slips through anyway. Source kept ASCII-clean by using `\u` escape sequences in the replacement code. Verified zero Unicode punctuation in any rendered_text row post-fix.

### Voices in the crowd (Slice 11)

The existing DialogueEnhancer (the Phase 1.5 contextual flavor layer that wraps every line of NPC dialogue) has seven probabilistic flavor layers. Each one picks from a small pool of hardcoded English/Hungarian variants:

- **Mood prefix** -- `*scowling,*`, `*eyes alight,*`, `*voice tight with grief,*`. Twelve emotion types.
- **Memory callback** -- "I haven't forgotten what you did for me." Three valences.
- **Witness allusion** -- "I saw what you did. Don't think no one noticed." Two valences.
- **Player-state observation** -- "You look ready to drop." Three sub-types (hurt-hostile, hurt-friendly, wealth-greedy).
- **Grief aside** -- a non-specific reference to a recent death. One layer.
- **Personality trait reveal** -- "I always have time for paying customers." Six trait types.
- **Faction tension** -- "Don't tell your priests we spoke." Eight faction-shape variants.

Slice 11 makes those template pools LLM-personalized per NPC, while keeping the templates as the always-available fallback. Architecture: each layer's existing pool-pick is wrapped in a helper that consults a per-NPC LLM cache first. Cache hit returns the LLM-rendered line immediately. Cache miss returns the localized template (existing behavior, zero latency) AND fires a background Task.Run that calls the LLM, sanitizes the response, and writes the result to the cache under the same key. Subsequent enhances of the same key for the same NPC use the cached LLM version (typically within three seconds of first contact).

The cache is per-NPC, transient (JsonIgnore), and regenerates naturally after restart. In-flight guard prevents concurrent ticks from firing parallel HTTP calls for the same key. LLM-disabled sessions early-out before any cache or HTTP work.

Cache key shape differs by layer. Mood is per-NPC: the same NPC sounds the same when angry every time, no matter who they're talking to. Memory and witness are per-(NPC, player): two players who chat the same NPC don't share cached lines because the relational history is different. State, grief, trait, faction are per-NPC: observational and self-referential.

For memory and witness layers, the actual memory description is passed to the LLM as `extraContext` so the line gets grounded in what actually happened. "I haven't forgotten what you did" → the LLM might render "I still owe you for that night in the alley" if the original memory was about being rescued there.

Twenty-two distinct moment_type values land in `llm_usage` for dialogue (12 mood_* + 3 memory_* + 2 witness_* + 3 state_* + 1 grief + 6 trait_* + 8 fac_*). Per-(NPC, key) one-time generation at about $0.001 each. Most NPCs cycle through a small handful of emotion + tone + memory + trait combinations in any given session, so the cache warms quickly and most exchanges are free after the first. Population-wide ceiling estimate sits at $5-10/day worst-case.

(One hotfix in flight: the first batch of mood prefixes from Sonnet rendered as `*jaw set, eyes cold,*,` -- the sanitizer was adding a comma OUTSIDE the closing asterisk when the convention is INSIDE before the closing asterisk. Fixed and redeployed.)

### Long arcs from short prompts (Slice 12a)

The existing reactive goal system is good at promoting goals from concrete world state: low gold triggers Earn Money, low HP triggers Heal Wounds, available XP triggers Become Ruler if Ambition is high enough. It's worse at picking the long-arc strategic goals that define an NPC over weeks of play: "Avenge my parent's death within the month", "Build influence in the Faith faction", "Find a spouse before my thirtieth year."

A new `LLMMoments.GenerateStrategicGoalsAsync` returns one to three LLM-designed strategic goals tailored to an NPC's personality, class, level, archetype, current goal stack, top recent memories (last week by importance), and family status (mother, father, spouse names if known). System prompt restricts output to a JSON array of goal objects: name (short imperative phrase under 40 chars), type (Personal/Social/Economic/Combat/Exploration), priority (0.0-1.0), target (optional NPC name for revenge/romance goals). A robust parser tolerates leading prose, trailing commas, case differences, and returns an empty list on any parse failure rather than throwing.

`GoalSystem.TryRefreshStrategicGoals(owner)` hooks into the existing `UpdateGoals` flow at the end. Gated on: LLM provider available, refresh interval (six wall-clock hours) elapsed for this NPC, no refresh currently in flight. When eligible, it stamps the refresh time BEFORE kicking the background Task.Run (so a thundering herd of concurrent ticks doesn't all fire HTTP calls before the first one returns), then the background task calls the generator, builds Goal objects from the returned candidates, tags them `IsLLMGenerated = true`, and adds them via `AddGoal` (the existing name-keyed dedup makes the refresh idempotent if a long-arc goal name persists across cycles).

The BrainV2Scorer doesn't need to change. It already consults `GetPriorityGoal()` every tick. LLM-designed strategic goals land in the same goal stack as the reactive ones and feed the same lookup. One LLM call ends up shaping hundreds of subsequent tick decisions because the scorer keeps consulting the same goal stack until the goal completes, decays, or gets pruned.

Cost: ~250 NPCs × 1 refresh per 6 hours × ~$0.002/call = ~$2/day population-wide.

One audit-driven adjustment: the slice originally gated strategic goals on the same `IsAIDriven` cohort flag the rest of Brain v2 uses. Live audit after first deploy showed the cohort had stayed at zero NPCs out of 244 -- organic turnover hadn't fired the creation paths enough to grow the cohort. The strategic-goal layer is purely additive (it augments the reactive goal stack alongside existing triggers, both feed the same `GetPriorityGoal` lookup), and the cohort split was specifically about picker comparability, which strategic goals don't touch. The gate was dropped. To avoid a 244-NPC simultaneous HTTP burst on restart, a startup stagger sets each NPC's first refresh anchor to a uniformly-random point in the past six hours, spreading initial refreshes over the next six hours at about forty per hour at peak instead of all at once.

### Character-defining choices (Slice 12b)

Where Slice 12a's strategic goals influence NPC behavior over many ticks, Slice 12b lets the LLM make specific high-stakes decisions in the moment they happen.

`LLMMoments.DecideForkAsync(npc, forkType, situation, choices, deterministicFallback, ct)` is the general-purpose API. Takes an NPC, a fork type tag for telemetry, a situation description, a list of choice strings, and a deterministic fallback index. Returns the LLM's 0-indexed choice OR the fallback on any failure. Always returns a valid index into `choices`. System prompt constrains output to a single number; lower temperature (0.6) than dialogue or goals because we want consistent character decisions, not creative variety. MaxTokens 10 (only need a digit). The parser extracts the first digit run from the response and validates it against the choice count.

Unlike the dialogue and strategic-goal slices, forks BLOCK on the LLM response. The caller awaits because the decision is needed right now -- the player just proposed marriage, or the NPC is about to flee or press on, or someone is about to choose to spare or kill. The 2-3 second latency is acceptable for one-shot narrative moments where the player is already waiting on a UI screen. It would not be acceptable for per-round combat decisions; those stay in BrainV2Scorer where they're deterministic by design.

First fork wired: marriage proposal. When the player proposes to an NPC, `HandleMarriageProposal` used to compute an `acceptChance` from Commitment + Romanticism + relationship level + charisma modifier, clamp it to [0.10, 0.95], roll a random number against it, and branch into accept (`roll < acceptChance`) / not-ready-yet (`roll < acceptChance + 0.25`) / refuse (else). The roll still computes; it's the deterministic fallback. The LLM gets the NPC's full personality, the relationship level, the trait scores, the heuristic acceptance chance, and the player's charisma modifier, and picks accept / hesitate / refuse based on character. Sometimes it agrees with the heuristic; sometimes it surprises.

Pattern is established. The next forks to wire (each about thirty minutes of work):
- Combat flee vs press on for Tier A NPCs in dungeon
- Affair partner selection in WorldSimulator.FindAffairPartner
- Royal court actions (challenge throne, betray king)
- Spare or kill on defeated NPC

---

## What didn't ship

Things that the work surfaced but didn't fix here:

The forty-percent share of NPC time spent in Dark Alley + Inn is now producing real outcomes, but the action distribution is still heavily skewed toward those two locations. Whether that's a problem to tune away or a feature to embrace needs two to three weeks of post-deploy telemetry before it's safe to touch the weights again.

Settlement is still ~10% of NPC actions with essentially no outcomes attached. Wiring real settlement outcomes is a larger settlement-system pass that doesn't belong in this release.

The Level-50 immigrant cluster (about 71,000 actions in the pre-release dataset) should start dispersing as NPCs above it begin actually leveling up. If it doesn't, the next pass forces redistribution. If it does, the immigrant default level itself becomes worth revisiting.

The three different "is this class a caster" checks in the codebase still disagree with each other. Unifying them needs a real design call about Paladin / Bard / Alchemist's mana-vs-stamina status. Hotfix shipped a single-source-of-truth helper for the mana initialization path; the broader audit is logged for later.

The Brain v2 cohort flag (`IsAIDriven`) was originally going to grow organically via natural turnover, then telemetry showed it had grown to zero NPCs out of 244 after weeks. The strategic-goals slice was forced to drop the gate as a result. For other slices that still use the gate (the BrainV2Scorer for legacy NPCs, real combat for Tier A), this is fine -- they were always intended to be additive cohorts not whole-population rollouts. But if the cohort fails to grow organically for the rest of the year, a bulk migration is going to be the right call.

The strategic goals layer has a 6-hour refresh interval and a startup stagger, both of which were sized for sanity rather than telemetry. Two to three weeks of data should tell us whether refreshes need to be more or less frequent.

Slice 12b is one fork deep. Combat flee, affair partner, royal challenge, and spare/kill are all marked for next.

LLM moments deliberately don't ship with a sysop console UI for live reconfig. Env vars at process start are how it's configured today. A small admin tab on the dashboard is a reasonable follow-up; it wasn't necessary for shipping.

---

## Rolling out

The deploy is a normal binary rollout. Stop `usurper-mud` and `sshd-usurper`, extract the new binary, restart both. Online players get kicked briefly.

The systemd `usurper-mud.service` currently has `--npc-xp 3.0`. Either remove the flag (so the new 5.0 code default takes effect) or set it explicitly to 5.0. Keeping the explicit flag in systemd is recommended for operational visibility.

The SQLite migration for `npc_decision_log.decision_source` runs automatically on first launch post-deploy and is idempotent. The new `llm_usage` table is created idempotently the first time the LLM telemetry layer initializes. No manual schema work needed.

To activate the LLM layer, create `/etc/systemd/system/usurper-mud.service.d/llm.conf` as a drop-in with the five `Environment=` directives. Each on its own line. `daemon-reload`, restart `usurper-mud`. The dashboard's LLM Moments tab should show calls landing within minutes (death epitaphs are the first to fire because the world sim produces Tier A deaths periodically). To deactivate, remove the file and restart.

The `llm.conf` drop-in needs the API key in plaintext on disk. Set file mode to 0600 owned by root. Rotate the key if it appears in any tool output during diagnosis.

Pre-existing player saves load with the new code unchanged. Pre-existing NPCs load with the new code unchanged; they continue running through the legacy picker because `IsAIDriven` defaults to false. New NPCs (immigrants, child graduates, orphans) spawn with `IsAIDriven=true` and run through the Brain v2 path. Telemetry on every decision tags the cohort so the dashboard can pivot on it.

Rollback: redeploy any prior 0.64.0 build or revert to 0.63.1. Pre-v0.64.0 saves work with the new code; v0.64.0 saves ignore unknown fields gracefully when read by older binaries. No save schema changes beyond additive JSON fields.

---

## The numbers

830 tests, all passing. About 3,500 lines of code added across `Scripts/AI/`, `Scripts/Systems/`, `web/`, and tests. ~140 new tests written during the Brain v2 work (Slices 1-9); the LLM continuation slices (10, 11, 12a, 12b) added zero new tests because every LLM path has a deterministic fallback that the existing suite already exercises.

Six new files: `BrainV2Scorer.cs`, `NPCCombatBrain.cs`, `NPCCombatSimulator.cs`, `LLMSettings.cs`, `LLMBudget.cs`, `LLMProvider.cs`, `LLMMoments.cs` (yes, that's seven; I miscounted).

Two new save fields with full five-site NPC save round-trip (`Character.IsAIDriven` + `NPCData.IsAIDriven`), and two new gear fields from the v0.63.2 work (`BaseWeapPow` + `BaseArmPow`). All four are additive JSON fields that older binaries ignore gracefully.

One new SQLite table (`llm_usage`) and one idempotent SQLite migration (`npc_decision_log.decision_source` column).

CombatEngine: zero lines changed.

LLM cost envelope at 250 NPCs and moderate player activity: about $1-3/day on Sonnet 4.6, hard-capped by the daily token budget.

The full Brain v2 design and remaining slice plan is documented in `memory/project_npc_brain_v2.md`.
