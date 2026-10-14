---
title: Boss reference
path: /wiki/en/monsters/bosses/
checked: 1.3.1
sources: Scripts/Data/WorldBossData.cs, Scripts/Data/OldGodsData.cs, Scripts/Data/SecretBosses.cs, Scripts/Locations/DungeonLocation.cs, Scripts/Systems/MonsterGenerator.cs, Scripts/Core/Monster.cs, Scripts/Systems/CombatEngine.cs, Scripts/Systems/OldGodBossSystem.cs, Scripts/Systems/DungeonGenerator.cs, Scripts/Core/GameConfig.cs, Scripts/Systems/MiracleSystem.cs
history: 1.2.5 | Boss names, titles, world boss elements and Old God ability names are shown in the player's language, and Old God names on the wiki come in all five languages.
history: 1.2.4 | In group combat, a secret boss's battle cry is shown in each player's own language.
---
Every dungeon floor ends in a boss room. Bosses hit harder and last longer than the monsters around them, and some change their abilities in phases during the fight. The base definitions below are not a prediction of a live scaled encounter.

## Floor bosses

A floor boss is a monster of the floor's level from a family tier that covers that level, with 2.8 times the HP, 1.25 times the Strength, punch and weapon power and 1.2 times the Defence and armor of an ordinary monster of the same tier. On floor 20 with a tier power of 1.0 an ordinary monster has 3,478 HP and a floor boss 9,738. A tier can have a separate boss power: the Orc keeps its full power as a boss. Its attack power counts 1.3 times, as for every boss, and the boss limits on one hit are on [combat](/wiki/en/combat/#most-damage-from-one-hit).

The boss's name comes from the floor's theme and floor number, so a floor always has the same boss name; the [dungeon floors table](/wiki/en/world/dungeon/#dungeon-floors-table) lists them. It uses the abilities of the tier it was drawn from. The boss room never ambushes you.

### Floor boss escorts

The boss room draws a group as any room does, without elites, stragglers or the floor 4 to 10 rules, and the boss takes the first place in it. The boss therefore fights alone or with the rest of that group: no escort or one on floors 1 to 10, up to 2 escorts on floors 11 to 30, 1 to 3 on 31 to 60 and 2 to 4 from 61. When the draw was a champion, the boss fights alone.

### Floor boss rewards

Beating the boss room pays a bonus on top of the kills: floor x 500 gold plus 0 to 999 more, and floor x 300 experience. On floor 20 that is 10,000 to 10,999 gold and 6,000 experience. In a group of players the gold is split evenly and each player's experience share follows the group rules. A boss of level 25 or more also gives 10 Fame, a lower one 5. On floors 20 and deeper a won boss room always drops {{balance:MaterialDropCountBossMin}} or {{balance:MaterialDropCountBossMax}} of one [crafting material](/wiki/en/items/materials/).

### Guardians that block the way down

Some floors have a guardian that must be dealt with before you can go deeper; see [how deep you can go](/wiki/en/world/dungeon/#how-deep-you-can-go), whose spoiler names those floors. How those fights scale is in a spoiler below.

## World bosses

World bosses belong to the shared online system. See the [online guide](/wiki/en/online/) for the distinction from a personal dungeon fight. Daily Miracles do not work in world boss fights.

{{table:bosses}}

## Story and secret encounters

The disclosures contain names, base stats and phase definitions that can spoil discoveries. Their contents do not appear in search results.

:::spoiler Spoiler: secret bosses
Four secret bosses wait in a secret vault on floors 25, 50, 75 and 99. A secret boss fights at the higher of its base level and your level + 5. Its HP is its listed base HP + 100 per level, its Strength and its punch are each its listed attack + 2 per level, and its Defence is its listed defence + 1 per level. Its other listed values (magic power, speed, critical chance and abilities) are not used in the fight: it makes normal attacks.

The floor 50 boss asks you a question first. The right answer is the fourth; any other answer multiplies its attack by 1.5 for that fight only. The kill itself pays nothing; a win pays the boss's reward: 10,000 experience and 5,000 gold on floor 25, 25,000 and 12,000 on floor 50, 50,000 and 25,000 on floor 75, and 100,000 experience and no gold on floor 99. Grouped players who fought with you are paid the same. Only a win spends the encounter: after a flight or a loss you can try again.
:::

:::spoiler Spoiler: Old God fights
An Old God's HP, Strength and Defence in the table below are multiplied by {{balance:BaseMonsterDifficultyScale}} in the fight; its Strength is then split evenly into Strength and weapon power, and its Defence into Defence and armor. Each artifact you hold adds 10 percent to its HP (at most 40 percent) and 5 percent to its damage (at most 20 percent).

Aurelion, Terravok and Manwe resist unenchanted weapons: your damage to them is 25, 35 and 50 percent lower. Any enchantment on your main hand or off hand weapon halves that reduction, and a weapon whose name holds Artifact, Godforged, Sunforged or Voidtouched removes it.
:::

{{table:secret-bosses}}
