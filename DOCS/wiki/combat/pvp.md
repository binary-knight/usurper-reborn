---
title: Player combat and duels
path: /wiki/en/combat/pvp/
checked: 1.3.1
sources: Scripts/Systems/CombatEngine.cs, DOCS/release-notes/RELEASE_NOTES_1.2.0.md
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

## Duels with townspeople

An honor duel in town and a duel at the Inn are fights with an NPC. The two share one daily cap of 3 wins. An NPC you beat in an honor duel keeps the HP it had before the fight and stays where it is, and a kill contract on it is not paid. In a duel, S shows your status and asks for your action again; it does not spend your turn.

## Salvage

After a PvP win you may salvage gold from the loser's worn weapon (30% chance) and body armor (25% chance) at half their value. Salvage gold is capped per fight by your level.

## Social consequences

A duel is not permission to murder a townsperson. Murder, arrest, guild standing and god taboos belong to their respective systems. Check [law and prison](/wiki/en/world/law/) before starting street violence.
