---
title: Combat basics
path: /wiki/en/combat/
checked: 1.2.5
sources: Scripts/Systems/CombatEngine.cs, Scripts/Systems/StatEffectsSystem.cs
history: 1.2.5 | Monster names in combat are shown in the player's language, and long attack, miss and spell rows wrap inside 79 columns.
history: 1.2.4 | In group combat each player reads the turn screen, menus and round status in their own language.
---
## Before choosing an action

Check HP, resources, enemies and status messages. An ordinary attack, ability, spell, consumable and flee attempt have different costs and restrictions.

Equipment, attributes, training, buffs, boons and Mental all contribute to results. The [balance defaults](/wiki/en/reference/balance/) are not a universal damage formula.

## Damage and defence

Physical attacks check hit and damage rules; criticals have additional rules and bonuses. Defence, armor, blocks and dodges protect in different ways. A miss is not necessarily evidence that your weapon has too little power.

Spells and martial abilities use their own effect definitions. Read [spells](/wiki/en/characters/spells/) and [abilities](/wiki/en/characters/abilities/) for resource costs and target behavior.

## When a fight goes wrong

Recover before resources are exhausted. [Fleeing](/wiki/en/combat/fleeing/) can fail and can carry penalties. Low Mental can cost your first action through fear; [treat Mental](/wiki/en/characters/mental/) instead of trying to heal that condition with HP potions.

## Fight types

[Groups and support](/wiki/en/combat/groups/), [companions and beasts](/wiki/en/combat/companions/), [PvP](/wiki/en/combat/pvp/) and [bosses](/wiki/en/monsters/bosses/) have rules that differ from an ordinary solo monster fight.
