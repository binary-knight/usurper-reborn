---
title: Leveling and training
path: /wiki/en/characters/leveling/
checked: 1.3.8
sources: Scripts/Locations/LevelMasterLocation.cs, DOCS/release-notes/RELEASE_NOTES_1.2.0.md, Scripts/Core/GameConfig.cs, Scripts/Systems/SysOpConfigSystem.cs, Scripts/Systems/EndingsSystem.cs, Scripts/Core/Character.cs, Scripts/Locations/BaseLocation.cs, Scripts/Systems/TrainingSystem.cs, Scripts/Systems/ClassAbilitySystem.cs, Scripts/Systems/SpellSystem.cs, Scripts/Systems/CombatEngine.cs
history: 1.3.8 | none
history: 1.3.7 | A Voidreaver gains Constitution on each level, 2 on an even level and 1 on an odd one, and an existing Voidreaver received what its levels would have given.
history: 1.3.6 | none
history: 1.3.5 | none
history: 1.3.4 | none
history: 1.3.3 | none
history: 1.3.2 | Each Attack action against monsters can raise your basic attack proficiency up to Good, and prestige class spells start at Average.
history: 1.3.1 | none
history: 1.3.0 | none
history: 1.2.5 | The Level Master's description, crystal page navigation and level-up news are shown in all five languages.
---
Leveling is automatic by default: as soon as you have the experience for the next level, the game raises your level between actions. Each level applies your class growth. Your class page shows the generated base growth, not a complete formula for an equipped character.

## Do I have to visit the Level Master

No, unless you turn automatic leveling off. The Auto-Level Up preference in the Preferences menu is on for a new character. With it off, you gain no level until you choose Level Raise at the Level Master.

The Level Master is still where you arrange your abilities and spells, spend training points, reset training and pick a specialization.

## What a level gives

Gaining a level restores your HP and mana to full. Every class gains 5 maximum HP, 1 Defense and 1 Stamina per level before its own growth; the [class table](/wiki/en/characters/classes/) shows the totals. You also gain training points and an [alignment bonus](/wiki/en/characters/stats/#alignment).

## Experience for each level

The total experience a level needs is the sum, for every level from 2 up to it, of 50 times that level squared. Above level 50 the power is 2.25 in place of 2. The table below has the total for every level.

Monster kills pay more experience at low levels. The multiplier is 3 at levels 1 to 5, 2 at levels 6 to 10 and 1.8 at levels 11 to 15, then falls by one thirtieth a level to 1 at level 40.

{{table:xp-levels}}

## Training points per level

Each level gives 3 training points, plus a class bonus, plus 1 for every 40 points of Intelligence and Wisdom combined.

- Sage: 3 more, so 6 before the stat bonus.
- Magician, Cleric, Alchemist, Mystic Shaman, Wavecaller, Cyclebreaker and Voidreaver: 2 more, so 5.
- Bard, Ranger, Assassin, Paladin, Tidesworn and Abysswarden: 1 more, so 4.
- Warrior, Barbarian and Jester: no class bonus, so 3.

## Proficiency

Every ability and spell has its own proficiency, in nine tiers. The effect multiplier multiplies the damage and healing of that ability or spell. The roll modifier and the fumble chance belong to the [spell check](/wiki/en/characters/spells/#why-a-spell-fails).

{{table:proficiency}}

Your basic attack, your class abilities and your spells start at Average. Raising one skill from Average to Legendary costs 165 training points in all.

### Does using a skill train it

Yes, up to a point. Each use of an ability, cast of a spell or Attack action against monsters has the chance in the table to add one step of progress. An Attack action counts once however many swings it makes, and it counts when its swings miss. A tier needs 1 step at Untrained, 2 at Poor and 3 at Average. An ability and the basic attack stop improving by use once they are Good, and so does a spell cast that fails. A spell that is cast with success can keep improving by use at the chances in the table. Power Attack and the other special attacks do not train the basic attack.

### Companions and NPC teammates

A companion or an NPC teammate does not spend training points. Its class skills rise with its level: Good at level 10, Skilled at 20, Expert at 35 and Superb at 50. Use raises a companion's skill no higher than Superb and an NPC teammate's no higher than Expert.

### Cost to reset training

Resetting training at the Level Master costs {{balance:RespecBaseGoldCost}} gold plus {{balance:RespecGoldPerLevel}} gold per level, so 24,000 gold at level 20. You can reset one skill or all of them. A reset skill returns to its starting tier and you get back the training point cost of each tier and step above it.

## Rewards that last

In this release, permanent stat rewards are applied to base attributes so gear changes and save/load cycles do not wipe them out. A resource refill is different from a permanent increase.

## Specialization

When Specialize appears at the Level Master, review the roles, passives and future growth before choosing. See the [specialization guide](/wiki/en/characters/specializations/). Changing specialization does not rewrite growth earned on earlier levels.

## Level cap

The highest level is 100. At level 100 the Level Master shows that you have reached the maximum, and experience no longer raises your level. The dungeon has 100 floors by default; a server's operator can set fewer, never more.

## Beyond the first journey

NG+ and prestige classes are advanced progression. Their unlocks depend on how your journey ends. Discovering those paths is part of the story, so this guide does not list ending outcomes in a search result.

When the main story ends, most endings ask you to choose between ascending to godhood and starting a new cycle; there is no return to ordinary play with the finished character. [New Game Plus](/wiki/en/characters/new-game-plus/) explains both paths.
