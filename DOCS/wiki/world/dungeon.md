---
title: Dungeon travel and outposts
path: /wiki/en/world/dungeon/
checked: 1.3.5
sources: Scripts/Locations/DungeonLocation.cs, Scripts/Data/DungeonSettlementData.cs, Scripts/Systems/MentalSystem.cs, Scripts/Systems/DungeonGenerator.cs, Scripts/Systems/MonsterGenerator.cs, Scripts/Systems/OldGodBossSystem.cs, Scripts/Systems/SevenSealsSystem.cs, Scripts/Core/GameConfig.cs
history: 1.3.5 | none
history: 1.3.4 | none
history: 1.3.3 | From monster level 25 monster groups grow a little with each level and monsters have more HP and hit harder, so the deep floors are built for a full team of five.
history: 1.3.2 | Floors keep their outposts and special rooms, and dungeon entry names a saved party member who is dead.
history: 1.3.0 | Rooms you cleared stay cleared after a disconnect, a reconnect or a save taken on the floor, floors 4 to 10 mix monster families in groups of up to three, and elite monsters appear from floor 3.
history: 1.2.5 | Monster names in rooms and group summaries and the dungeon merchant's wares are shown in each player's language, and long dungeon rows wrap at 79 columns.
history: 1.2.4 | The follower room view, reward labels, skill toggle names, the descend message's theme, duelist cries and gear are translated into all five languages.
history: 1.2.3 | Dungeon screens, outpost greetings and lore, the map and group broadcasts are translated into all five languages, and group broadcasts wrap at 79 columns.
---
The dungeon has 100 floors. Dungeon rooms can contain monsters, treasure, features and routes deeper. Progress is not only a damage check: supplies and Mental matter across the entire trip.

What you can meet in a room, from chests, traps and shrines to puzzles and strangers, is on [dungeon features and random events](/wiki/en/world/dungeon-features/). The rarest events have their own page, [rare encounters](/wiki/en/world/rare-encounters/), and the monsters theirs, [monsters](/wiki/en/monsters/).

## Entering and choosing a floor

The first time you enter, the game puts you on the floor equal to your level. Later visits start on the floor you last stood on. On the floor overview, L changes the floor: type a floor number, or a step such as +1 or -1.

## How deep you can go

The deepest floor you may enter is your level + 3 at levels 1 to 5, your level + 5 at levels 6 to 15, your level + 8 at levels 16 to 30 and your level + 10 from level 31, never past floor 100. A level 20 character may go down to floor 28. You may always go back up to any shallower floor. The same limit applies to the stairs down.

Some floors have a guardian that must be dealt with before you can go deeper: you can reach such a floor but not pass it until its guardian is resolved. Which floors these are is in the spoiler below.

:::spoiler Spoiler: guardian floors and the Old Gods
The guardian floors are the Old God floors: Maelketh on floor 25, Veloura on 40, Thorgrim on 55, Noctura on 70, Aurelion on 85, Terravok on 95 and Manwe on 100. A god counts as resolved when it is defeated, saved, allied, awakened or consumed. Aurelion is met in the Deep Temple of the Temple, not in the floor 85 boss room.

An Old God appears in its boss room only when your level is at least its floor minus 10 (15, 30, 45, 60, 75, 85 and 90) and enough other gods are resolved: none for Maelketh, 1 for Veloura and Thorgrim, 2 for Noctura, 3 for Aurelion, 4 for Terravok and 6 for Manwe. Until then the boss room holds an ordinary floor boss, and beating it does not open the way down.

On the 5 floors before each Old God floor, ordinary monsters gain HP and damage but not Defence: 5% five floors out, then 10%, 15%, 20% and 25% on the floor just before the god. The god's own floor and the floors after it are back to normal. Floor bosses are not scaled this way.
:::

## Floor layout

A floor has 15 + floor / 8 rooms, at least 15 and at most 25: floors 1 to 7 have 15 rooms, floor 40 has 20 and floors 80 to 100 have 25. Each floor's layout is the same every time you visit it and the same for every player. The floor has a danger rating of 1 + floor / 10 (at most 10) and each room a danger value of 1 to 3; neither number changes a fight.

### Dungeon floors table

The theme, rooms and floor boss of every floor. The boss's name comes from the floor's theme and its number; its stats come from a monster family of the floor's level, see [floor bosses](/wiki/en/monsters/bosses/#floor-bosses).

{{table:dungeon-floors}}

## Floor clear bonus

When you take the stairs down from a floor whose monster rooms are all cleared, you get a one time bonus of 50 x floor^1.2 experience and 25 x floor^1.15 gold: 792 experience and 353 gold for floor 10. A few special floors pay three times as much (spoiler below). The bonus is paid once per floor per character.

## Do dungeon monsters respawn

A floor's cleared monster rooms fill again when you come back after at least {{balance:FloorRESPAWN_HOURS}} real hour away from that floor. The boss room fills again too, and your once per floor camp comes back. Looted treasure, sprung traps and used events do not come back. A few special floors never respawn once you have cleared them (spoiler below).

:::spoiler Spoiler: floors that pay triple and never respawn
The seal floors (15, 30, 45, 60, 80 and 99) and the secret boss floors (25, 50, 75 and 99) pay a triple floor clear bonus and stay cleared for good once cleared. A seal floor counts as cleared when you hold its seal.
:::

## Rest and exploration

New rooms add depth strain, while already explored rooms do not charge that room strain again. Completed monster fights add their own strain. Camp in a cleared room where the option is available. Camp and Safe Haven rest share a floor allowance; the amounts are on [dungeon features](/wiki/en/world/dungeon-features/#camping-and-safe-rooms). A room you cleared stays cleared after a disconnect, a reconnect or a save taken on the floor.

While Broken you cannot take the stairs down or change to a deeper floor, and in the Breaking band the game asks you twice before you go deeper. [Mental treatment](/wiki/en/characters/mental/) belongs in your expedition plan.

With companions or teammates along, entering a new room has a 15% chance that one of them makes a remark.

## Dungeon outposts

Four floors have a safe outpost room with no monsters or traps: floors 10, 20, 35 and 65. An outpost can heal, sell supplies and tell its lore; not every outpost does all three.

{{table:outposts}}

### Outpost healing

Healing costs (10 + floor x 5) gold times the outpost's price factor, and restores the outpost's share of your missing HP and missing mana and cures poison. You can heal again as often as you pay.

### Outpost supplies

Supplies cost their base price times 1 + floor / 10 (the table shows the price on each outpost's floor). A torch heals 10 HP at once, a lockpick gives +2 Dexterity and a smoke bomb +2 Agility for your next fight. Potions, antidotes and herbs stop at your carrying limit.

### Outpost lore

Each outpost tells its lore one passage at a time, a new passage each time you ask, until you have heard them all.

These are dungeon destinations, distinct from the [Outskirts settlement](/wiki/en/world/settlement/) beyond the city gates.

## Boss readiness

Every floor ends in a boss room. Floor bosses are tougher than the monsters around them and can bring escorts; their numbers and rewards are on [floor bosses](/wiki/en/monsters/bosses/#floor-bosses). Before committing, review equipment, consumables, resources and group condition.

## The seven seals

Seven seals are part of the story. They are not gates: you can pass a seal floor without its seal. Where they are and how a seal is found is in the spoiler.

:::spoiler Spoiler: seal floors and how to find a seal
The first seal is found in town at the Temple; the others lie on dungeon floors 15, 30, 45, 60, 80 and 99. Collecting them pays 1,000, 2,000, 3,000, 4,000, 5,000, 6,000 and 10,000 experience in that order. A seal floor has 3 extra rooms that can hold the seal. On a seal floor the seal is found for certain once 75% of the floor's rooms are explored. Before that it is found on entering a shrine, a lore library, a secret vault, a meditation chamber, the boss room or a room with a shrine event, or a plain chamber 20% of the time; from 50% explored any other room can hold it, with a chance of 15% rising toward 25%.
:::
