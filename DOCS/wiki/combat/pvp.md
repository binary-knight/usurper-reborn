---
title: Player combat and duels
path: /wiki/en/combat/pvp/
checked: 1.3.4
sources: Scripts/Systems/CombatEngine.cs, Scripts/Core/GameConfig.cs, Scripts/Locations/ArenaLocation.cs, Scripts/Locations/InnLocation.cs, Scripts/Locations/DormitoryLocation.cs, DOCS/release-notes/RELEASE_NOTES_1.2.0.md
history: 1.3.4 | none
history: 1.3.3 | none
history: 1.3.2 | none
history: 1.3.0 | Fights between players make no hit roll, a dual-wielder's swings each use the weapon in their own hand, S in a duel shows your status without spending the turn, and duels with townspeople share one daily cap of 3 wins and spare the beaten NPC in an honor duel.
history: 1.2.7 | none
history: 1.2.6 | none
history: 1.2.5 | After a PvP win, players in every language now salvage gold from the loser's worn weapon (30% chance) and armor (25% chance) at half their value.
history: 1.2.4 | none
---
Player fights use a different rule context from ordinary monster encounters. Read the menu and confirmation before entering a duel, arena fight or street conflict.

## Rules that do not carry over

Ordinary monster-fight Mental losses do not apply to player fights. The [hit roll](/wiki/en/combat/#the-hit-roll) of a monster fight is not made in a fight between players. A dual-wielder's swings each use the weapon in their own hand here too. Miracles are not available in player fights. Boons can still apply where their effect fits; Discordia's player-damage boon is one example.

Do not infer PvP healing or control behavior from a monster fight. Sage duel behavior and limits were adjusted in 1.2.0, and the target can have its own defenses.

## PvP damage

Your swing in a player fight does Strength + the weapon power of that hand + a random 1 to 15, times your weapon setup (two-handed bonus, off hand at half), minus the defender's Defence + a random 0 to Defence / 8, at least 1. Armor power is not taken off in a player fight.

- **Crit:** 5 percent plus 1 percent per 10 Dexterity, with no ceiling, for 1.5 times the damage.
- **Defending:** the defender's Defence counts 1.5 times.
- **Shield:** the defender blocks 20 percent of the time and adds its shield's bonus to Defence for that hit.
- **Blinded:** a blinded fighter misses {{balance:PvPBlindedMissPercent}} percent of weapon swings.

Your opponent's swing at you, when the game fights for it (an NPC, or the Arena's copy of a player), is one swing a turn with no crit, and it does half damage while you defend.

## PvP rewards

A win pays experience: 50 x the loser's level, times 1 + 0.15 x (the loser's level - your level), kept between 0.25 and 2, at least 10. Beating a level 12 at level 10 pays 50 x 12 x 1.3 = 780. Nothing is paid for a spare, an escape or a loss.

Gold: half of the gold the loser carries plus 10 to 29 gold per level of the loser. In the Arena and when you attack a sleeper at the Inn or the Dormitory the half is not taken; that place takes its own share instead (the [Arena](/wiki/en/online/arena/) takes 10 percent).

## Duels with townspeople

An honor duel in town and a duel at the Inn are fights with an NPC. The two share one daily cap of 3 wins. An NPC you beat in an honor duel keeps the HP it had before the fight and stays where it is, and a kill contract on it is not paid. In a duel, S shows your status and asks for your action again; it does not spend your turn.

## Salvage

After a PvP win you may salvage gold from the loser's worn weapon (30% chance) and body armor (25% chance) at half their value. Salvage gold is capped per fight by your level.

## Social consequences

A duel is not permission to murder a townsperson. Murder, arrest, guild standing and god taboos belong to their respective systems. Check [law and prison](/wiki/en/world/law/) before starting street violence.
