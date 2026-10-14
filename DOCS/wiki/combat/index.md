---
title: Combat basics
path: /wiki/en/combat/
checked: 1.3.1
sources: Scripts/Systems/CombatEngine.cs, Scripts/Systems/StatEffectsSystem.cs
history: 1.3.0 | Your physical attacks on monsters roll a twenty-sided die to hit and show the roll and the number needed, and a dual-wielder's swings each use the weapon in their own hand.
history: 1.2.7 | A monster's critical hit line no longer leaves an empty gap where a damage number used to be.
history: 1.2.6 | none
history: 1.2.5 | Monster names in combat are shown in the player's language, and long attack, miss and spell rows wrap inside 79 columns.
history: 1.2.4 | In group combat each player reads the turn screen, menus and round status in their own language.
---
## Before choosing an action

Check HP, resources, enemies and status messages. An ordinary attack, ability, spell, consumable and flee attempt have different costs and restrictions.

Equipment, attributes, training, buffs, boons and Mental all contribute to results. The [balance defaults](/wiki/en/reference/balance/) are not a universal damage formula.

## Damage and defence

A physical attack on a monster first rolls to hit, then works out its damage; criticals have additional rules and bonuses. Defence, armor, blocks and dodges protect in different ways. A miss is not necessarily evidence that your weapon has too little power.

When you dual wield, each swing uses the weapon in its own hand and the off-hand swing deals half damage. Abilities count the main-hand weapon in full and the off-hand weapon at half.

Spells and martial abilities use their own effect definitions. Read [spells](/wiki/en/characters/spells/) and [abilities](/wiki/en/characters/abilities/) for resource costs and target behavior.

## The hit roll

Each physical attack you make on a monster rolls a twenty-sided die: a basic swing (each hand on its own when you dual wield), Power Attack, Precise Strike, Backstab, Smite, the ranged attack and a physical single-target class ability. The game shows the roll and the number needed, for example "[You roll 14; you need 2 or more]".

- Against a monster at your level, below it or up to 5 levels above it, you need 2 or more: only a natural 1 misses.
- Against a monster more than 5 levels above you, you can need up to 8 or more. There your Strength, Dexterity, level, weapon power and basic attack proficiency lower the number, and the monster's level and Defence add to it.
- A natural 1 always misses and a natural 20 always hits. On a basic swing the natural 20 is the critical hit.
- Precise Strike needs 4 less on the die, never less than 2.
- A miss deals no damage and triggers no on-hit effect.

A monster's own evasion is checked first, before the roll. A swing from hiding does not roll. Teammates and companions roll with their own stats. Spells, area abilities and fights between players do not roll.

## When a fight goes wrong

Recover before resources are exhausted. [Fleeing](/wiki/en/combat/fleeing/) can fail and can carry penalties. Low Mental can cost your first action through fear; [treat Mental](/wiki/en/characters/mental/) instead of trying to heal that condition with HP potions.

## Fight types

[Groups and support](/wiki/en/combat/groups/), [party and allies](/wiki/en/combat/party/), [companions and beasts](/wiki/en/combat/companions/), [PvP](/wiki/en/combat/pvp/) and [bosses](/wiki/en/monsters/bosses/) have rules that differ from an ordinary solo monster fight.
