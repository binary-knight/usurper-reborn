---
title: The Arena
path: /wiki/en/online/arena/
checked: 1.3.1
sources: Scripts/Locations/ArenaLocation.cs, Scripts/Locations/MainStreetDistricts.cs, Scripts/Core/GameConfig.cs, Scripts/Systems/CombatEngine.cs
history: 1.3.1 | none
history: 1.3.0 | In an Arena fight a dual-wielder's swing uses the weapon in its own hand; before, every swing used both weapons' power added together.
history: 1.2.7 | none
history: 1.2.6 | none
history: 1.2.5 | After an Arena win, players in every language now salvage gold from the target's worn weapon and armor; before, English players never did.
history: 1.2.4 | The notice a defender gets after your Arena attack now reaches their mailbox.
history: 1.2.3 | none
---
The Arena is the online game's player-versus-player ground. It exists only in online play.

## Where is the Arena

From Main Street open the Online district with O, then press A for the Arena. The Online district does not appear in single-player. The Anchor Road Gauntlet is a different, monster-based challenge.

## How a fight works

Arena fights are asynchronous. You fight an AI-controlled copy of another player's saved character, and that copy starts at full HP. Your own damage stays with you after the fight, so heal first.

Press A to attack. The list of targets leaves out:

- players who are online right now;
- you and your own alternate characters;
- anyone you already attacked today;
- a player who lost a PvP fight today and has not logged in since;
- a player asleep at home or resting in the Shadows safe house.

## What level for the Arena

You need level {{balance:MinPvPLevel}} to fight, you can attack {{balance:MaxPvPAttacksPerDay}} times a day, and the target must be within {{balance:PvPLevelRangeLimit}} levels of you.

## Winning and losing

### How much gold do I win in the Arena

A win takes {{percent:PvPGoldStealPercent}} percent of the gold the target carries, at most {{balance:PvPGoldPerFightCapPerLevel}} gold x your level in one fight; an alternate character's take is capped at {{balance:PvPAltGoldStealBase}} + {{balance:PvPAltGoldStealPerLevel}} x its level. A level 20 main can take up to 100,000 gold and a level 20 alt up to 3,000. A win can also salvage gold from the target's worn weapon (30% chance) and armor (25% chance) at half their value, and salvage counts toward the same cap. The win also pays 10 to 29 x the target's level in bonus gold, claims any bounty on the target and adds 10 Fame. If the target logs in during the fight, the gold taken from them is voided; the win, experience and bounty stand.

### How much experience for a PvP win

A win pays 50 x the target's level x (1 + 0.15 x the target's level minus yours) experience, with the level factor held between 0.25 and 2, at least 10. Beating a level 24 target at level 20: 1,200 x 1.6 = 1,920 experience. A team's Training headquarters adds to it.

### What do I lose if I lose in the Arena

A loss costs 10 percent of your experience, 25 percent of your gold and 5 Fame, and you revive at the Inn with half your maximum HP. The defender then also takes {{percent:PvPGoldStealPercent}} percent of the gold you have left, under the same cap at the defender's level.

## Records

- L shows the leaderboard of wins, losses and gold taken.
- H shows recent fights.
- S shows your own record.

PvP wins count toward achievements.

Arena fights follow [player combat rules](/wiki/en/combat/pvp/): ordinary Mental loss and daily Miracles do not apply. A dual-wielder's swing uses the weapon in its own hand, for you and for the copy you fight.
