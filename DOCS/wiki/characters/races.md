---
title: Races
path: /wiki/en/characters/races/
checked: 1.3.6
sources: Scripts/Core/GameConfig.cs, Scripts/Systems/WorldSimulator.cs, Scripts/Systems/CharacterCreationSystem.cs, Scripts/Systems/MentalSystem.cs, Scripts/Systems/CombatEngine.cs, Scripts/Core/Items.cs, Scripts/Systems/HagglingEngine.cs, Scripts/Locations/WeaponShopLocation.cs
history: 1.3.6 | A monster poisoned by the Gnoll's Poisonous Bite no longer hits harder, a boss's physical immunity and divine armor cut that poison, and the bite no longer comes from the Alchemist's thrown bombs.
history: 1.3.5 | The Gnoll's Poisonous Bite, like weapon procs and life steal, no longer comes from the magical class abilities that were counted as melee hits.
history: 1.3.4 | none
history: 1.3.3 | none
history: 1.3.2 | A Troll pays 10 percent less for a weapon at the Weapon Shop.
history: 1.3.1 | none
history: 1.3.0 | The Gnoll Poisonous Bite poisons on melee hits only; it no longer comes from spells, magical abilities, ranged abilities or a swing made with a bow in hand.
history: 1.2.7 | none
history: 1.2.6 | none
history: 1.2.5 | Race descriptions, class restriction reasons and the race help screen are shown in all five languages.
history: 1.2.4 | none
history: 1.2.3 | none
---
Race contributes bonuses at character creation and limits some class choices. Race also affects dungeon Mental strain; inspect your Status rather than assuming two equally leveled characters recover or strain identically.

{{table:races}}

Open a race page for its creation ranges. Appearance fields are generator settings, not combat bonuses.

## How long does an NPC of each race live

The table also shows each race's NPC lifespan. An NPC ages one game year every {{balance:NpcLifecycleHoursPerYear}} real hours and dies of old age at its race's lifespan: an Elf NPC lives 200 game years, about 80 real days from its birth. Your own character does not age this way. [NPC life](/wiki/en/online/npcs/#how-long-does-an-npc-live-lifespan-by-race) has the whole life cycle.

## Creation bonuses

At character creation a race adds its bonus to Strength, Stamina, Defence and hit points only. Agility, Charisma, Dexterity, Wisdom, Intelligence and Constitution come from the dice and your class, with no race bonus. See [stats](/wiki/en/characters/stats/) for the full creation roll.

## Classes each race cannot take

Class compatibility is enforced during [character creation](/wiki/en/getting-started/). These are the starting classes each race cannot choose:

- Human: Mystic Shaman.
- Hobbit: Barbarian, Mystic Shaman.
- Elf: Barbarian, Mystic Shaman.
- Half-Elf: Mystic Shaman.
- Dwarf: Magician, Sage, Mystic Shaman.
- Troll: Alchemist, Assassin, Bard, Cleric, Jester, Magician, Paladin, Sage.
- Orc: Bard, Magician, Paladin, Sage.
- Gnome: Barbarian, Mystic Shaman.
- Gnoll: Alchemist, Cleric, Magician, Paladin, Sage.
- Mutant: Mystic Shaman.

Mystic Shaman is therefore open only to Troll, Orc and Gnoll. Race places no limit on the NG+ prestige classes. Each [class page](/wiki/en/characters/classes/) also lists its compatible races.

## Mental strain

Race scales the Mental strain you take in the dungeon:

- Troll, Orc, Gnoll and Mutant: {{balance:MentalStrainRacePctHardy}} percent of the normal strain.
- Elf and Hobbit: {{balance:MentalStrainRacePctSensitive}} percent.
- Half-Elf and Gnome: {{balance:MentalStrainRacePctUneasy}} percent.
- Human and Dwarf: 100 percent.

Your class scales it again. The [Mental guide](/wiki/en/characters/mental/) shows how the two combine.

## Race passives: Troll and Gnoll

Two races have a passive of their own in a fight: the Troll regenerates and the Gnoll has a poisonous bite. The other eight races differ in their creation bonuses, their class limits and their Mental strain. Hobbits and Gnomes also cannot wear Heavy armor: see [equipment by class](/wiki/en/characters/classes/#equipment-by-class-armor-weight-weapons-and-shields).

Out of a fight, a Troll also pays 10 percent less for a weapon at the Weapon Shop.

## Troll regeneration

A Troll you play regains 2 percent of maximum HP at the end of each combat round, and never less than 3 HP, while below full HP. A drug that raises Constitution adds one tenth of that bonus to the amount.

## Gnoll Poisonous Bite

Gnolls have a passive Poisonous Bite. Each melee hit a Gnoll lands on a monster has a 15 percent chance to poison it for 3 rounds. The bite never poisons a target that is already poisoned. It does not come from spells, from magical abilities, from ranged abilities, or from a swing made with a bow in hand.
