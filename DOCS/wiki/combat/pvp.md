---
title: Player combat and duels
path: /wiki/en/combat/pvp/
checked: 1.2.7
sources: Scripts/Systems/CombatEngine.cs, DOCS/release-notes/RELEASE_NOTES_1.2.0.md
history: 1.2.7 | none
history: 1.2.6 | none
history: 1.2.5 | After a PvP win, players in every language now salvage gold from the loser's worn weapon (30% chance) and armor (25% chance) at half their value.
history: 1.2.4 | none
---
Player fights use a different rule context from ordinary monster encounters. Read the menu and confirmation before entering a duel, arena fight or street conflict.

## Rules that do not carry over

Ordinary monster-fight Mental losses do not apply to player fights. Miracles are not available in player fights. Boons can still apply where their effect fits; Discordia's player-damage boon is one example.

Do not infer PvP healing or control behavior from a monster fight. Sage duel behavior and limits were adjusted in 1.2.0, and the target can have its own defenses.

## Salvage

After a PvP win you may salvage gold from the loser's worn weapon (30% chance) and body armor (25% chance) at half their value. Salvage gold is capped per fight by your level.

## Social consequences

A duel is not permission to murder a townsperson. Murder, arrest, guild standing and god taboos belong to their respective systems. Check [law and prison](/wiki/en/world/law/) before starting street violence.
