---
title: Races
path: /wiki/en/characters/races/
checked: 1.2.7
sources: Scripts/Core/GameConfig.cs, Scripts/Systems/CharacterCreationSystem.cs, Scripts/Systems/MentalSystem.cs, Scripts/Systems/CombatEngine.cs
history: 1.2.7 | none
history: 1.2.6 | none
history: 1.2.5 | Race descriptions, class restriction reasons and the race help screen are shown in all five languages.
history: 1.2.4 | none
history: 1.2.3 | none
---
Race contributes bonuses at character creation and limits some class choices. Race also affects dungeon Mental strain; inspect your Status rather than assuming two equally leveled characters recover or strain identically.

{{table:races}}

Open a race page for its creation ranges. Appearance fields are generator settings, not combat bonuses. NPC lifespan is a world-simulation value, not a countdown for a player.

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

## Gnoll Poisonous Bite

Gnolls have a passive Poisonous Bite. Each melee hit a Gnoll lands on a monster has a 15 percent chance to poison it for 3 rounds. The bite never poisons a target that is already poisoned. It does not come from spells, from magical abilities, from ranged abilities, or from a swing made with a bow in hand.
