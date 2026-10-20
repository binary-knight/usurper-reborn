---
title: Mental health
path: /wiki/en/characters/mental/
checked: 1.3.5
sources: Scripts/Systems/MentalSystem.cs, Scripts/Core/GameConfig.cs, DOCS/release-notes/RELEASE_NOTES_1.2.0.md, Scripts/Systems/GriefSystem.cs, Scripts/Systems/CombatEngine.cs, Scripts/Core/Character.cs
history: 1.3.5 | none
history: 1.3.4 | none
history: 1.3.3 | Grief and its Mental loss for a Downed companion or NPC ally come only when a monster finishes it, never when it goes down.
history: 1.3.2 | Each story companion whose grief reached Acceptance gives you 5 Wisdom, at most 25, and Main Street shows that Wisdom whenever it applies.
history: 1.3.1 | none
history: 1.3.0 | none
---
Mental measures how well your character is coping, separately from HP. Status shows your current Mental and band. It is saved with your character.

{{table:mental}}

## Strain and losses

Entering new dungeon rooms and finishing monster fights add fractional, depth-dependent strain. Revisited rooms do not charge new-room strain. Race, class and living story companions modify the rate.

Fleeing, near-death finishes, bosses, death, grief, disasters and drug effects can cause separate losses. Player fights do not charge the ordinary monster-fight Mental losses. Being in town does not by itself slowly restore Mental.

### How fast Mental drops in the dungeon

Strain is counted in thousandths of a Mental point: every full {{balance:MentalStrainPerPoint}} costs 1 Mental. A new room adds the floor number times {{balance:MentalRoomStrainPerFloor}}, and the end of a monster fight adds the floor number times {{balance:MentalFightStrainPerFloor}}, each times your race and class percent below. On floor 20 at 100 percent that is 80 for a room and 240 for a fight: 1 Mental for every 12.5 new rooms, or for a little more than 4 fights.

### Mental lost at the end of a fight

A monster fight can also cost whole points when it ends:

- You fled: {{balance:MentalFleeLoss}}.
- You ended it at or below {{balance:MentalNearDeathHpPct}} percent of maximum HP: {{balance:MentalNearDeathLoss}}.
- It was a floor boss or a mini-boss: {{balance:MentalBossLoss}}.
- It was an Old God: {{balance:MentalOldGodLoss}}, in place of the boss loss.
- You died: {{balance:MentalDeathLoss}}.

## Strain by race and class

Your race and your class each scale dungeon strain, as a percent of the normal amount:

- Race: Troll, Orc, Gnoll and Mutant {{balance:MentalStrainRacePctHardy}}; Elf and Hobbit {{balance:MentalStrainRacePctSensitive}}; Half-Elf and Gnome {{balance:MentalStrainRacePctUneasy}}; Human and Dwarf 100.
- Class: Assassin, Abysswarden and Voidreaver {{balance:MentalStrainClassPctDark}}; Sage {{balance:MentalStrainClassPctSage}}; Barbarian {{balance:MentalStrainClassPctBarbarian}}; Cleric, Paladin and Tidesworn {{balance:MentalStrainClassPctDevout}}; Bard and Jester {{balance:MentalStrainClassPctPerformer}}; every other class 100.

Your rate is the race percent times the class percent, divided by 100 and rounded to the nearest whole percent, halves rounding up. A Mutant Sage, an Orc Assassin, a Gnoll Assassin and a Mutant Assassin each take 68 percent; an Elf Bard takes 132 percent. Each living story companion in your party then cuts strain by {{balance:MentalStrainCompanionCutPct}} percent, up to {{balance:MentalStrainCompanionCutMaxPct}} percent. The percentages apply to dungeon strain only, not to the separate losses above. [Races](/wiki/en/characters/races/) lists the classes each race cannot take.

## Recovery

Recovery requires an action. Options include a new day, dungeon camp, Safe Haven rest, the Inn, Home, prayer, confession, a friend conversation, learning and the Healer.

- Inn sleep restores {{balance:MentalInnSleepGain}} Mental.
- Temple daily prayer restores {{balance:MentalTemplePrayerGain}} Mental.
- Willow Draught restores {{balance:MentalWillowDraughtGain}} Mental when used in the dungeon, up to your cap.
- Dungeon camp and a Safe Haven share the floor's rest allowance.

Once-a-day sources that do nothing at your cap remain available for later. Sleep and daily-reset behavior differs between single-player and online.

### How much Mental each source restores

{{table:mental-recovery}}

Every gain stops at your cap. The Inn table rest and the rest with a friend share one use a day.

### Talk Therapy cost

For each point of Mental you are missing, Talk Therapy at the Healer costs {{balance:MentalTherapyCostBase}} gold plus {{balance:MentalTherapyCostPerLevel}} gold per level of yours, before the Healer's tax. Missing Mental is 100 less your Mental. A Broken character is billed for at least {{balance:MentalTherapyBrokenMinPoints}} points. At level 30 with 40 Mental missing the price is 40 times 70: 2,800 gold.

## Fear and performance

Low bands can reduce damage and defence and cause fear at the beginning of a monster fight. Fear costs the first action; it is not an enemy spawning or a permanent attribute loss. Harmless hallucination lines are flavor, not extra enemies.

### What Shaken and Breaking do

- Shaken: {{percent:MentalShakenCombatPenalty}} percent less damage with basic attacks and {{percent:MentalShakenCombatPenalty}} percent less Defence against monster hits, and a {{balance:MentalShakenFearPct}} percent chance of fear at the start of a monster fight.
- Breaking: {{percent:MentalBreakingCombatPenalty}} percent less of both, and a {{balance:MentalBreakingFearPct}} percent chance of fear.

Stable and Strained carry no penalty. In single-player the Mental and Fatigue penalties together never cost more than {{percent:MentalFatigueCombatCap}} percent; online play has no Fatigue.

### Do grief and Mental penalties stack

No. They are never added to each other. The game works out grief plus Fatigue and Mental plus Fatigue and uses the worse of the two.

## Collapse and Broken

At zero Mental, collapse occurs after the current activity and before the next chained fight. In town or shallow dungeon floors you are carried to the Healer; deeper collapse follows death rules. Broken persists after Mental recovers and blocks deeper dungeon travel.

Use Talk Therapy at the Healer to clear Broken and restore Mental. Addiction Rehab also clears it when rehab is available. Carrying healing potions alone does not cure Broken.

## Addiction and gods

Addiction lowers your ordinary recovery cap. Drug highs can temporarily exceed it but later crash; therapy can also lift Mental above the cap until the daily reset. Rehab removes addiction and its cap.

### How much addiction lowers Mental

Your ordinary Mental cap is 100 less half your addiction value. An ordinary gain stops at the cap, and a gain does nothing while your Mental is already at or above it.

### Drug high and crash

A drug gives a high of {{balance:MentalDrugHighGain}} Mental, or {{balance:MentalDrugHighStrongGain}} for Dark Essence and Demon Blood. Each further use within {{balance:MentalDrugToleranceWindowDays}} days of the last one gives {{balance:MentalDrugHighStepPct}} percent less, down to {{balance:MentalDrugHighMinPct}} percent of the full high. The crash that follows takes twice the high you got, plus half of it again for each of those further uses.

From Devout onward, your [god's ward](/wiki/en/gods/favor/) reduces a particular Mental loss; it does not block it. It does not remove every source of strain.

## Grief

Grief begins when a story companion, an NPC teammate, a spouse or a lover dies. It passes through stages, and each stage changes your fights until the next one begins:

- Denial, 3 days: 20 percent less damage with basic attacks.
- Anger, 5 days: 30 percent more damage with basic attacks and 20 percent less Defence against monster hits.
- Bargaining, 3 days: 10 percent less damage with basic attacks.
- Depression, 7 days: 30 percent less of both.
- Acceptance: the grief is over and no penalty remains.

Online, each stage lasts 1 day and every number above is halved. In single-player the penalties of several griefs add up, to at most 50 percent; online only the worst one counts.

Each grief for a story companion that reaches Acceptance adds 5 to your Wisdom. A grief for an NPC teammate, a spouse or a lover adds none.

### Mental lost to grief

The death of a story companion costs {{balance:MentalCompanionGriefLoss}} Mental, and the death of an NPC teammate, a spouse or a lover costs {{balance:MentalNpcGriefLoss}}. Entering Depression costs {{balance:MentalGriefDepressionLoss}} more. Reaching Acceptance restores {{balance:MentalGriefAcceptanceGain}}.

:::spoiler Exact hidden effects and collapse boundary
The deeper-collapse boundary is dungeon floor {{balance:MentalCollapseDeathFloor}}. Rescue sets Mental to {{balance:MentalCollapseRescueMental}} and takes {{balance:MentalCollapseGoldFeePct}} percent of carried gold.

Broken reduces damage, defence and experience gained by {{balance:MentalBrokenPenaltyPct}} percent. This replaces the band penalty and is outside the single-player fatigue cap. Exact band fear and hallucination odds are in the [balance reference](/wiki/en/reference/balance/).
:::
