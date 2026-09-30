---
title: Mental health
path: /wiki/en/characters/mental/
checked: 1.2.0
sources: Scripts/Systems/MentalSystem.cs, DOCS/release-notes/RELEASE_NOTES_1.2.0.md
---
Mental measures how well your character is coping, separately from HP. Status shows your current Mental and band. It is saved with your character.

{{table:mental}}

## Strain and losses

Entering new dungeon rooms and finishing monster fights add fractional, depth-dependent strain. Revisited rooms do not charge new-room strain. Race, class and living story companions modify the rate.

Fleeing, near-death finishes, bosses, death, grief, disasters and drug effects can cause separate losses. Player fights do not charge the ordinary monster-fight Mental losses. Being in town does not by itself slowly restore Mental.

## Recovery

Recovery requires an action. Options include a new day, dungeon camp, Safe Haven rest, the Inn, Home, prayer, confession, a friend conversation, learning and the Healer.

- Inn sleep restores {{balance:MentalInnSleepGain}} Mental.
- Temple daily prayer restores {{balance:MentalTemplePrayerGain}} Mental.
- Willow Draught restores {{balance:MentalWillowDraughtGain}} Mental when used in the dungeon, up to your cap.
- Dungeon camp and a Safe Haven share the floor's rest allowance.

Once-a-day sources that do nothing at your cap remain available for later. Sleep and daily-reset behavior differs between single-player and online.

## Fear and performance

Low bands can reduce damage and defence and cause fear at the beginning of a monster fight. Fear costs the first action; it is not an enemy spawning or a permanent attribute loss. Harmless hallucination lines are flavor, not extra enemies.

## Collapse and Broken

At zero Mental, collapse occurs after the current activity and before the next chained fight. In town or shallow dungeon floors you are carried to the Healer; deeper collapse follows death rules. Broken persists after Mental recovers and blocks deeper dungeon travel.

Use Talk Therapy at the Healer to clear Broken and restore Mental. Addiction Rehab also clears it when rehab is available. Carrying healing potions alone does not cure Broken.

## Addiction and gods

Addiction lowers your ordinary recovery cap. Drug highs can temporarily exceed it but later crash; therapy can also lift Mental above the cap until the daily reset. Rehab removes addiction and its cap.

From Devout onward, your [god's ward](/wiki/en/gods/favor/) protects a particular Mental loss. It does not remove every source of strain.

:::spoiler Exact hidden effects and collapse boundary
The deeper-collapse boundary is dungeon floor {{balance:MentalCollapseDeathFloor}}. Rescue sets Mental to {{balance:MentalCollapseRescueMental}} and takes {{balance:MentalCollapseGoldFeePct}} percent of carried gold.

Broken reduces damage, defence and experience gained by {{balance:MentalBrokenPenaltyPct}} percent. This replaces the band penalty and is outside the single-player fatigue cap. Exact band fear and hallucination odds are in the [balance reference](/wiki/en/reference/balance/).
:::
