---
title: Dungeon features and random events
path: /wiki/en/world/dungeon-features/
checked: 1.3.6
sources: Scripts/Locations/DungeonLocation.cs, Scripts/Systems/DungeonGenerator.cs, Scripts/Systems/FeatureInteractionSystem.cs, Scripts/Systems/DiscoverySystem.cs, Scripts/Data/DiscoveryData.cs, Scripts/Systems/PuzzleSystem.cs, Scripts/Data/RiddleDatabase.cs, Scripts/Core/GameConfig.cs
history: 1.3.6 | none
history: 1.3.5 | A living companion now takes 75 percent of the experience from a room feature you examine, as an NPC teammate does.
history: 1.3.4 | none
history: 1.3.3 | none
history: 1.3.2 | Floors keep their special rooms, a failed riddle charges its damage once, and the Outskirts Prison buff halves room and chest trap damage.
history: 1.3.0 | Rooms you cleared stay cleared after a disconnect, a reconnect or a save taken on the floor, a beaten boss included, and from floor 3 a room's monsters can include an elite, announced when the fight starts.
history: 1.2.5 | Monster names in rooms, merchant wares and crafting materials are shown in the player's language, and long merchant and material rows wrap at 79 columns.
history: 1.2.4 | When the leader examines a feature, followers see it in their own language, and the merchant purchase confirm wraps at 79 columns.
history: 1.2.3 | Dungeon rooms, features, puzzle titles and hints, and feature stat checks are translated into all five languages, and puzzle text wraps to fit the screen.
---
Every dungeon floor is a set of connected rooms. A room can hold monsters, treasure, a random event, a trap, features you can examine and stairs deeper. This page covers what you can meet in a room and the numbers behind it. "Floor" in a formula is the floor number. For supplies, depth limits, respawns and outposts see [dungeon travel](/wiki/en/world/dungeon/); for the rarest events see [rare encounters](/wiki/en/world/rare-encounters/).

## The room menu

The room view lists exits and hints about what the room holds. The menu then shows only the actions that apply:

- **Fight the monsters** when the room is guarded.
- **Collect treasure** once the room is cleared or was never guarded.
- **Investigate the event** when the room has a random event you have not resolved.
- **Examine features** to list the objects in the room you can interact with.
- **Descend stairs** in the room that has them, once it is cleared.
- **Make camp and recover** in a cleared room, once per floor.
- Map, the dungeon guide, inventory, potions, herbs, Willow Draught, party and status are always at hand.

Monsters guard treasure and stairs, so a guarded room must be cleared first. A cleared room stays cleared after a disconnect, a reconnect or a save taken on the floor; this holds for the room of your last fight and for a boss you beat. From floor 3 the monsters of a room can include an [elite](/wiki/en/monsters/#elites), announced when the fight starts. Leaving the dungeon returns you to the floor overview, where you can change level or return to town.

## What a room holds: monster, event and trap odds

When a floor is generated, each ordinary room has a 60% chance of monsters, a 30% chance of an event and a 15% chance of a trap, each rolled on its own. A floor is generated the same way on every visit, so a room's contents depend on the floor number, not on you.

Entering a room with living monsters starts the fight at once 30% of the time (an ambush); otherwise you choose when to fight. The boss room never ambushes you.

## Random room events

Some rooms hold an event marked in the room view. Choose **Investigate the event** to face it. An event is used up by the choice you make, not by opening the prompt, so a mistyped key simply asks again.

An ordinary event room holds one of seven kinds with equal weight: a treasure chest, a shrine, a trap event, a stranger, a puzzle or riddle, a resting place or a mystery. A trap event is one of six small scenes with equal weight: a treasure chest, a potion cache, a shrine, a gambling ghost, a beggar or a wounded man.

## Collect treasure: treasure room gold

Each floor has one treasure room, always guarded and trapped; arena rooms and secret vaults also hold treasure. Collecting treasure pays floor x 100 gold plus 0 to floor x 200 - 1 more, and floor x 50 experience plus 0 to 99 more. On floor 20 that is 2,000 to 5,999 gold and 1,000 to 1,099 experience. 30% of the time you also find 1 or 2 healing potions, up to your carrying limit; a mana class gets mana potions for what does not fit. Every treasure room pays the same formula.

## Treasure chests

A chest holds treasure 70% of the time, a trap 20% of the time and a mimic 10% of the time, and this is decided before you choose. It offers **Open**, **Search for Traps** and **Leave It**.

### Chest treasure

Chest treasure pays floor x 150 gold plus 0 to floor x 100 - 1 more, and 20 x floor^1.5 experience plus 0 to floor x 5 - 1 more. On floor 20 that is 3,000 to 4,999 gold and 1,788 to 1,887 experience. A treasure chest also has an {{percent:MaterialDropChanceTreasure}}% chance of one [crafting material](/wiki/en/items/materials/) that drops on that floor.

### Search for traps

Search for Traps is free and can be used once per chest. The search chance is (Dexterity + Wisdom) / 6, at most 75%, minus 1 point per 5 floors. Assassins add 15 + Dexterity / 8, Rangers add 10 + Dexterity / 12, Jesters and Bards add 5, and the result stays between 5% and 85%. A found trap is disarmed and the chest opens to treasure; a found mimic is revealed before you touch it. A failed search looks the same as a clean chest.

### Chest traps

A chest trap you open first gives you the room trap evade roll (below). If it hits, it is one of three with equal weight: poison gas for floor x 5 damage and poison for 5 + floor / 5 rounds unless you resist the poison, spikes for floor x 8 damage, or acid that takes a tenth of your gold. The Outskirts Prison buff halves the gas and spike damage. Chest traps never take you below 1 HP.

### Mimic

A mimic is a champion fight. Its HP, Strength, Defense, punch and weapon power are 1.5 times the plain monster formulas of the floor's level, without the monster difficulty scale of {{balance:BaseMonsterDifficultyScale}} or the early floor softening that ordinary monsters get.

## Room traps

A room trap fires the first time you enter that room, before you see what is inside. There is no disarm action for room traps; Search for Traps applies to chests. Room traps never kill: they stop at 1 HP.

### Trap evade chance

The evade chance is Agility / 3, at most 75%, minus 1 point per 5 floors. Assassins add 15 + Dexterity / 8, Rangers add 10 + Dexterity / 12, Jesters and Bards add 5, and the result stays between 5% and 85%. Agility 60 on floor 20 with no class bonus gives 16%.

### Trap damage by type

A trap you do not evade is one of six with equal weight. Most then allow a follow up check on a stat: that stat / 3, at most 70%, minus 1 point per 6 floors, kept between 5% and 80%. The Outskirts Prison buff halves the pit, dart and fire damage before that check.

- **Pit:** floor x 3 + 0 to 9 damage; an Agility check halves it.
- **Poison darts:** floor x 2 + 0 to 7 damage and poison for 5 + floor / 5 rounds; a Constitution check halves the damage and stops the poison, and resisting poison also stops it.
- **Fire:** floor x 4 + 0 to 11 damage; a Dexterity check halves it.
- **Acid:** destroys a tenth of the gold you carry, at most floor x 150; a Dexterity check halves the loss.
- **Curse:** drains the least of floor x 50, 5% of your experience (at least 50) and your level x 25 experience. Constitution lowers the drain by Constitution percent, at most 40%, and the drain is at least 10. A Wisdom check cancels it.
- **Broken mechanism:** harmless, and you salvage floor x 20 gold.

## Shrines

A mysterious shrine offers **Pray**, **Desecrate** or **Leave**.

### Shrine prayer

Prayer picks one of six results with equal weight: a full heal; a permanent Strength gain of 1 to 5; 50 + floor x 15 experience (350 on floor 20); nothing; the loss of a quarter of your current HP; or the loss of a fifth of the gold you carry.

### Desecrate a shrine

Desecration gives floor x 200 gold plus 0 to 499 more (4,000 to 4,499 on floor 20) and moves you 30 to 79 points toward Darkness. 30% of the time a Vengeful Spirit attacks: a champion of the floor's level + 5 with floor x 12 HP and floor x 4 Strength.

## Small scenes in a trap event

- **Potion cache:** 1 to 5 potions, healing potions first up to your limit, the rest as mana potions for a mana class.
- **Gambling ghost:** bet 100 gold up to half the gold you carry; you and the ghost each roll two six-sided dice, the higher roll wins the bet and a tie returns it.
- **Beggar:** giving costs the higher of 10 and your level x 2 gold and moves you 5 toward Chivalry, and 15% of the time the beggar repays three times the gift plus 50 to 149 gold. Robbing takes your level + 5 to 3 x your level + 4 gold and moves you 10 toward Darkness; 20% of the time the beggar hits back for your level + 5 to 14 damage (at least 5), never below 1 HP.
- **Wounded man:** healing him costs one healing potion and pays floor x 500 gold plus 0 to 999 more and 20 to 59 toward Chivalry. Without a potion a bandage gives 10 toward Chivalry half the time. Robbing him takes 100 to 599 gold and 60 to 139 toward Darkness.

## Examining room features

Many rooms contain objects you can interact with, such as a reliquary, a grate, a crystal or an altar. Choose **Examine features** to list them. Each shows the way you interact with it: examine, open, search, read, take, use, break or enter. Features fit the floor's theme, so the Catacombs hold different objects from the Frozen Depths. A used feature disappears from the list. Some scripted discoveries happen only once per character, and those do not return on later visits.

### How many features a room has

A room has no features 50% of the time, one feature 37% of the time and two features 13% of the time. Each feature is a scripted discovery with its own scene 70% of the time when the floor's theme has any; otherwise it is a plain feature whose result is rolled when you use it.

### What a plain feature does

A roll of 0 to 99 is checked against the result kinds in this order, each taking the next share: lore 15 + floor / 10, skill check 20, moral choice 10 + floor / 20, class bonus 15, risk and reward 20, memory 5 + floor / 15, insight 5 + floor / 25, and anything left is a plain result. Reading adds 30 to lore and 15 to insight, examining adds 10 to lore and 10 to memory, and opening or breaking adds 20 to risk and 10 to skill check. Because the shares are taken in order, a large lore share leaves little room for the kinds after it.

### Feature skill check

A skill check rolls a twenty-sided die plus the stat / 10 (at most +40) against 8 + floor / 4. Floor 20 needs 13, and a stat of 400 or more passes every check. Opening and breaking test Strength, searching and reading Intelligence, taking and entering Dexterity, and using and examining Wisdom.

### Feature class bonus

Warriors and Barbarians gain 5 + floor / 10 attack for the next fight, Magicians and Sages regain 10 + floor / 5 mana, Clerics and Paladins heal a quarter of their maximum HP and so does each living teammate, Assassins gain 1 + floor / 25 poison vials, Rangers find gold and one healing potion, Bards gain experience, and every other class finds gold.

### Risk and reward features

A risk and reward offer states its gold and its chance before you choose. The success chance is 50 + Dexterity / 5 + Intelligence / 10, kept between 20% and 85%. Failure costs floor x 3 + 10 to 29 HP, never below 1, and 30% of the time you still keep a third of the gold.

### Feature rewards by floor

Gold and experience from features are a base amount times floor^1.5 / 10 (at least 1), times {{balance:DungeonFeatureRewardMultiplier}}. A base of 10 pays 20 on floor 1, about 178 on floor 20 and about 707 on floor 50. A risk and reward offer uses a base of 200 to 500 gold. Feature damage never kills.

## Puzzles and riddles

A puzzle event is a riddle 50% of the time and a mechanical puzzle 50% of the time. Solving pays gold and experience; failing costs HP but never kills.

### Riddles

A riddle allows 3 attempts. You type the answer; alternate phrasings are accepted, and a hint is whispered after the first wrong answer. Riddle difficulty is 1 + floor / 20, at most 5. A solved riddle pays floor x 50 + difficulty x floor x 20 gold and 15 x floor^1.5 x (1 + 0.5 x difficulty) experience: 1,800 gold and 2,683 experience on floor 20. A failed riddle costs its own damage (10 to 60), never below 1 HP, and the Outskirts Prison buff halves it. Typing quit, flee or leave gives up and costs nothing. At difficulty 3 or more a solved riddle has a 30% chance to reveal a lore fragment.

### Mechanical puzzles

Puzzle difficulty is 1 + floor / 15, at most 5. A solved puzzle pays floor x 30 + difficulty x floor x 15 gold and 15 x floor^1.5 x (1 + 0.3 x difficulty) experience: 1,200 gold and 2,146 experience on floor 20. Its clues stay on screen after each wrong try. The kinds, with their tries and the share of your maximum HP a failure costs (d is the difficulty):

- Lever sequence: 3 + d tries, 10 + 5 x d percent.
- Symbol alignment: 5 + d tries, 5 + 3 x d percent.
- Number grid: 3 + d tries, 10 percent.
- Pressure plates, from floor 15: 2 + d tries, 15 + 5 x d percent.
- Memory test, from floor 15: 2 + d / 2 tries, 8 + 3 x d percent.
- Light and darkness, from floor 30: one try per torch + d, 5 percent.
- Item combination, from floor 30: 3 + d tries, 15 + 3 x d percent.
- Elemental puzzle, from floor 50: 4 + d tries, 20 + 5 x d percent.
- Mirror puzzle, from floor 50: two tries per mirror + d, 5 percent.

## Camping and safe rooms

**Make camp and recover** restores a quarter of your maximum HP, mana and stamina and a quarter of each teammate's, recovers {{balance:MentalDungeonCampGain}} Mental and, in single-player, takes two hours of game time and removes {{balance:FatigueReductionDungeonRest}} Fatigue. You can camp once per floor, in a cleared room. A Blood Price weight of 3 or more gives you 75% of the amount and 6 or more 50%; teammates always get the full quarter.

A safe haven or resting place event restores a third of maximum HP, mana and stamina to you and each teammate, cures poison, recovers {{balance:MentalSafeHavenGain}} Mental and in single-player removes {{balance:FatigueReductionDungeonRest}} Fatigue. It uses the same once per floor rest as camping, so after a camp on the floor it gives nothing. Plan where you spend it.

## Strangers

A stranger event is one of five with equal weight.

- **Pixie:** catching it succeeds 35 + Dexterity percent of the time, at most 85%. A caught pixie fully restores your HP and mana, gives Well-Rested (15% for 5 fights, or 3 more fights if you already have it) and floor x 100 gold plus 0 to 199 more. A missed catch poisons you for 5 + floor / 5 rounds unless you resist, and the pixie steals up to floor x 50 + 0 to 99 gold.
- **Wounded adventurer:** shares a map that marks half of the floor's rooms as explored, and clears those without monsters.
- **Rival adventurer:** fight a rival of the floor's level with 80 + 38 x floor HP, pay floor x 200 gold to pass, or leave.
- **Lost explorer:** guiding them out pays floor x 150 gold and 20 toward Chivalry; robbing them pays floor x 200 gold and 25 toward Darkness.
- **Mysterious stranger:** half the time heals half your maximum HP, otherwise only a warning.

## Mystery events

A mystery event is one of five with equal weight: a vision that reveals the whole floor map, a time shift worth 22 x floor^1.5 experience (1,967 on floor 20), a ghostly message, a teleport to a random room on the floor, or a shower of floor x 100 + 0 to 499 gold (2,000 to 2,499 on floor 20).

## Floor themes and special rooms

Each band of floors has a theme that changes the rooms, features and encounters: the Catacombs cover floors 1 to 10, the Sewers 11 to 20, the Caverns 21 to 35, the Ancient Ruins 36 to 50, the Demon Lair 51 to 65, the Frozen Depths 66 to 80, the Volcanic Pit 81 to 90 and the Abyssal Void 91 to 100. Crypt rooms appear only in the Catacombs and Ancient Ruins. The [dungeon floors table](/wiki/en/world/dungeon/#dungeon-floors-table) lists the rooms and floor boss of every floor.

A floor always starts in a hall and ends in the boss room. The number of monsters in a room is decided when its fight starts; see [monsters](/wiki/en/monsters/).

### Special rooms

- **Treasure room:** one per floor, always guarded and trapped, with treasure to collect.
- **Secret vaults:** guarded and trapped, with treasure. There are none on floors 1 to 4, one on floors 5 to 24, two from floor 25, three from 50, four from 75 and five on floor 100, with extra ones on a few story floors.
- **Meditation chamber:** from floor 10, one per floor: a resting place without monsters that uses the once per floor rest.
- **Lore library:** from floor 15, one per floor, guarded 30 percent of the time. It tells you one lore fragment you have not read yet, among those your awakening allows.
- **Memory room:** on floors 30, 45, 60, 75 and 90, without monsters; it can bring back a memory of your past.
- **Outpost:** on floors 10, 20, 35 and 65; see [dungeon outposts](/wiki/en/world/dungeon/#dungeon-outposts).
- **Arena room:** guarded, with treasure; only on a few story floors (spoiler below).

:::spoiler Spoiler: rooms on the seal floors
The seal floors (15, 30, 45, 60, 80 and 99) each have an arena room and extra shrines, secret vaults or meditation chambers where the seal can be found.
:::
