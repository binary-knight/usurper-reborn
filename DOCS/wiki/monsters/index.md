---
title: Monsters and bosses
path: /wiki/en/monsters/
checked: 1.3.0
sources: Scripts/Data/MonsterFamilies.cs, Scripts/Systems/MonsterGenerator.cs
history: 1.3.0 | Monsters choose their abilities with more sense and five early monsters gain a signature ability, dungeon floors 4 to 10 mix families in groups of up to three with a possible Shaman, elite monsters with named traits appear from floor 3, and the Orc is weaker except as a floor boss.
---
Ordinary monsters are organized into families with level tiers and abilities. The generated family pages show normal non-boss stat samples at each tier's endpoints.

{{table:monsters}}

A sample is not a fixed stat line for every encounter. Difficulty, NG+, server mods and encounter-specific scaling can alter results.

The Orc has 90 percent of the power it had before 1.3.0; as a floor boss it keeps its full power.

## How monsters fight

A monster leaves out an ability that would be wasted: a heal on itself while it has 40 percent of its HP or more, a status its target already has, an evasion buff while one is active, and a one-time buff it has already used. When nothing is left it makes its normal attack. In a party, Beast, Insectoid, Shadow and Undead monsters prefer a target at 35 percent of its HP or less, and Goblinoid, Fey and Demonic monsters prefer a Magician, Sage, Cleric, Alchemist, Wavecaller or Abysswarden. A taunt still comes first.

From monster level 3, five early monsters have a signature ability: Goblin (Rally), Zombie (Enfeeble), Kobold (Poison), Spark (Burn) and Animated Armor (Stoneskin). Old God fights keep their own rules.

## Encounters on dungeon floors 4 to 10

On dungeon floors 4 to 10 a regular encounter holds one, two or three monsters. Half the time a group is one family; otherwise its monsters are drawn one by one and can mix families. Each monster in a group of three has 80 percent of its HP and 90 percent of its damage. A mixed group can include one spellcaster, shown as a Shaman, for example "Kobold Shaman". Boss rooms are not built this way.

## Elites

From dungeon floor 3 a regular encounter can hold one elite. An elite has 60 percent more HP, 15 percent more damage and 10 percent more defence than the monster it was, and gives 60 percent more experience and 50 percent more gold. A group of three or more has one monster fewer when it holds an elite. The elite's trait is part of its name and is announced when the fight starts. From floor 51 an elite has two different traits.

- Frenzied: below half its HP it hits harder.
- Venomous: its blows can poison.
- Commanding: its allies deal 10 percent more damage while it lives.
- Mending: every third round it can heal a badly wounded ally in place of its own attack.
- Swift: it acts before the first round.

Boss rooms and champion encounters never hold an elite.

[Bosses](/wiki/en/monsters/bosses/) use separate base definitions and phases. Secret and story bosses are kept behind a disclosure, outside search and Discord excerpts.
