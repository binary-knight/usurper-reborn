---
title: Dungeon features and random events
path: /wiki/en/world/dungeon-features/
checked: 1.2.5
sources: Scripts/Locations/DungeonLocation.cs, Scripts/Systems/DungeonGenerator.cs, Scripts/Systems/FeatureInteractionSystem.cs, Scripts/Systems/DiscoverySystem.cs, Scripts/Data/DiscoveryData.cs, Scripts/Systems/PuzzleSystem.cs, Scripts/Data/RiddleDatabase.cs
history: 1.2.5 | Monster names in rooms, merchant wares and crafting materials are shown in the player's language, and long merchant and material rows wrap at 79 columns.
history: 1.2.4 | When the leader examines a feature, followers see it in their own language, and the merchant purchase confirm wraps at 79 columns.
history: 1.2.3 | Dungeon rooms, features, puzzle titles and hints, and feature stat checks are translated into all five languages, and puzzle text wraps to fit the screen.
---
Every dungeon floor is a set of connected rooms. A room can hold monsters, treasure, a random event, a trap, features you can examine and stairs deeper. This page covers what you can meet in a room and how to deal with it. For supplies, Mental and outposts see [dungeon travel](/wiki/en/world/dungeon/); for the rarest events see [rare encounters](/wiki/en/world/rare-encounters/).

## The room menu

The room view lists exits and hints about what the room holds. The menu then shows only the actions that apply:

- **Fight the monsters** when the room is guarded.
- **Collect treasure** once the room is cleared or was never guarded.
- **Investigate the event** when the room has a random event you have not resolved.
- **Examine features** to list the objects in the room you can interact with.
- **Descend stairs** in the room that has them, once it is cleared.
- **Make camp and recover** in a cleared room, once per floor.
- Map, the dungeon guide, inventory, potions, herbs, Willow Draught, party and status are always at hand.

Monsters guard treasure and stairs, so a guarded room must be cleared first. Leaving the dungeon returns you to the floor overview, where you can change level or return to town.

## Random room events

Some rooms hold an event marked in the room view. Choose **Investigate the event** to face it. An event is used up by the choice you make, not by opening the prompt, so a mistyped key simply asks again. Event rooms can hold a treasure chest, a shrine, a stranger, a puzzle or riddle, a safe resting place or a mystery.

:::spoiler Spoiler: how event rooms are rolled
When a floor is generated, each ordinary room has a 60% chance of monsters, a 30% chance of an event and a 15% chance of a trap. An event room picks one of seven kinds with equal weight: treasure chest, shrine, trap, stranger, puzzle, resting place or mystery. A trap event becomes one of six small scenes instead: a chest, a potion cache, a shrine, a gambling ghost, a beggar or a wounded man.
:::

## Treasure chests

A chest offers **Open**, **Search for Traps** and **Leave It**. Search for Traps is free and can be used once per chest. A found trap is disarmed and the chest becomes safe; a hidden mimic is revealed before you touch it. If the search finds nothing, the chest may still be trapped, because a missed trap looks the same as a clean chest. Dexterity and Wisdom help the search, and Assassins, Rangers, Jesters and Bards are better at it.

:::spoiler Spoiler: chest odds and search chance
A chest holds treasure 70% of the time, a trap 20% of the time and a mimic 10% of the time, and this is decided before you choose. Treasure gives gold and experience that scale with the floor, and it can include a crafting material. A chest trap is poison gas, spikes or acid that eats a tenth of your gold, and an evasion roll can avoid it. A mimic is a mini boss fight with stronger than normal stats. The search chance is (Dexterity plus Wisdom) divided by six, capped at 75%, minus one point per five floors. Assassins add 15 plus a share of Dexterity, Rangers add 10 plus a smaller share, Jesters and Bards add 5, and the result stays between 5% and 85%.
:::

## Traps

Some rooms are trapped. A room trap fires the first time you enter that room, before you see what is inside. Agility decides whether you evade it, and deeper floors make evading harder. Assassins, Rangers, Jesters and Bards evade better. If a trap catches you, a second check on a matching stat can halve or cancel its effect. Room traps never kill: they stop at 1 HP. There is no disarm action for room traps; Search for Traps applies to chests.

:::spoiler Spoiler: trap types and checks
The evade chance is Agility divided by three, capped at 75%, minus one point per five floors, with the same class bonuses as the chest search, kept between 5% and 85%. A trap that is not evaded is one of six with equal weight. A pit deals floor scaled damage and an Agility check halves it. Poison darts deal damage and poison you, and a Constitution check halves the damage and stops the poison. A fire trap deals the most damage and a Dexterity check halves it. An acid trap destroys part of your gold and a Dexterity check halves the loss. A curse drains experience, Constitution reduces the drain and a Wisdom check cancels it. A broken mechanism is harmless and gives some salvage gold. The follow up check is the stat divided by three, capped at 70%, minus one point per six floors, kept between 5% and 80%.
:::

## Shrines

A mysterious shrine offers **Pray**, **Desecrate** or **Leave**. Prayer is a gamble between a blessing and a misfortune. Desecration takes the offerings for gold, moves you toward darkness and can anger what the shrine protects. Leave if you do not want the risk.

:::spoiler Spoiler: shrine outcomes
Prayer picks one of six results with equal weight: a full heal, a permanent Strength gain of one to five, experience that grows with the floor, nothing, the loss of a quarter of your current HP, or the loss of a fifth of your gold. Desecration gives gold equal to 200 per floor plus up to 499 more and 30 to 79 Darkness, and 30% of the time a Vengeful Spirit mini boss attacks.
:::

## Examining room features

Many rooms contain objects you can interact with, such as a reliquary, a grate, a crystal or an altar. Choose **Examine features** to list them. Each shows the way you interact with it: examine, open, search, read, take, use, break or enter. Features fit the floor's theme, so the Catacombs hold different objects from the Frozen Depths.

What happens depends on the object. Some are scripted discoveries with their own scene, some ask for a choice between a kind and a selfish act, some test a stat, and some show the risk and reward before asking whether to try. A used feature disappears from the list. Some discoveries happen only once per character, and those do not return on later visits.

:::spoiler Spoiler: feature odds and checks
A room has no features half the time, one feature 37% of the time and two features 13% of the time. Each feature is a scripted discovery 70% of the time. Other features roll their result type. Reading favours lore, examining favours lore and memories, and opening or breaking favours risk and stat tests. A stat test uses Strength to open or break, Intelligence to search or read, Dexterity to take or enter and Wisdom to use or examine, against a difficulty that rises with the floor. Class bonuses give Warriors and Barbarians an attack boost, Mages and Sages mana, Clerics and Paladins a party heal, Assassins poison vials, Rangers gold and a potion and Bards experience. A risk and reward offer states its success chance, which rises with Dexterity and Intelligence and stays between 20% and 85%. Feature damage never kills.
:::

## Puzzles and riddles

A puzzle event is either a riddle or a mechanical puzzle. For a riddle you type the answer; alternate phrasings are accepted, and a hint is whispered after the first wrong answer. A mechanical puzzle is a lever sequence, a symbol alignment, a number grid, a memory test, pressure plates or a simpler challenge, and its clues stay on screen after each wrong try. Both have limited attempts. Solving pays gold and experience; failing costs HP but never kills. Puzzles and riddles grow harder on deeper floors.

:::spoiler Spoiler: puzzle and riddle details
Riddles allow three attempts. Riddle difficulty rises by one every 20 floors and puzzle difficulty by one every 15 floors, up to five. Pressure plates and memory tests join the pool from floor 15, light and item puzzles from floor 30, and elemental and mirror puzzles from floor 50. Harder riddles can also reveal a fragment of deeper lore.
:::

## Camping and safe rooms

**Make camp and recover** restores part of your HP, mana and stamina and your companions' HP, recovers {{balance:MentalDungeonCampGain}} Mental and passes some game time. You can camp once per floor. A sanctuary or safe haven event heals more, cures poison and also recovers Mental, but it uses the same once per floor rest. Plan where you spend it.

:::spoiler Spoiler: rest amounts
Camping restores a quarter of maximum HP, mana and stamina and takes two hours of game time. A heavy Blood Price lowers what camping restores. A sanctuary restores a third and cures poison.
:::

## Strangers, merchants and mysteries

Stranger events bring you someone in the dark: a pixie you can try to catch, a wounded adventurer, a rival, a lost explorer or a mysterious stranger. Exploring can also turn up a travelling merchant who will trade, or whom you can attack and fight. Mystery events are sudden: a vision, a shift in time, a ghostly message, a teleport to another room on the floor, or a shower of coins.

:::spoiler Spoiler: stranger and mystery outcomes
A pixie is caught more easily with higher Dexterity. A vision reveals the whole floor map. A time shift grants experience scaled to the floor. The teleport takes you to a random room on the same floor. Killing a merchant gives gold scaled to the floor.
:::

## Floor themes and special rooms

Each band of floors has a theme that changes the rooms, features and encounters: Catacombs, Sewers, Caverns, Ancient Ruins, Demon Lair, Frozen Depths, Volcanic Pit and the Abyssal Void, in that order going down. Crypt rooms appear only in the Catacombs and Ancient Ruins.

A floor always starts in a hall and ends in the boss room. One room holds guarded treasure of better quality. Deeper floors add special rooms: an arena room with several monsters and treasure, a meditation chamber that works as a resting place, a lore library, and secret vaults. Some floors also hold story discoveries; see [the story](/wiki/en/world/story/) and [boss references](/wiki/en/monsters/bosses/).

:::spoiler Spoiler: floor bands and room counts
The Catacombs cover floors 1 to 10, the Sewers 11 to 20, the Caverns 21 to 35, the Ancient Ruins 36 to 50, the Demon Lair 51 to 65, the Frozen Depths 66 to 80, the Volcanic Pit 81 to 90 and the Abyssal Void 91 and below. A floor has 15 to 25 rooms. Arena rooms appear from floor 5, meditation chambers from floor 10 and lore libraries from floor 15. The guarded treasure room has two or three monsters and a trap. Each floor's layout is the same every time you visit it.
:::
