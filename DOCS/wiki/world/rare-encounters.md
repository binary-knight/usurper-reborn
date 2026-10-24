---
title: Rare dungeon encounters
path: /wiki/en/world/rare-encounters/
checked: 1.3.7
sources: Scripts/Systems/RareEncounters.cs, Scripts/Locations/DungeonLocation.cs, Scripts/Systems/MonsterGenerator.cs
history: 1.3.7 | none
history: 1.3.6 | none
history: 1.3.5 | none
history: 1.3.3 | The Arena Portal champion on floor 25 or deeper has more HP and hits harder, as the floor's other monsters do.
history: 1.3.2 | none
history: 1.3.0 | none
history: 1.2.5 | The Arena Portal champion's name is stored in English and shown in each player's language.
history: 1.2.4 | none
history: 1.2.3 | The last English lines in rare encounters, the experience gain, damage taken and the Arena champion's name, are translated into all five languages.
---
Rare encounters are unusual random events that can interrupt a dungeon trip. They are separate from ordinary [room features and events](/wiki/en/world/dungeon-features/). Most offer a choice, and most choices carry a risk as well as a reward. Leaving is usually an option. "Floor" in an amount is the floor number, so the gold and experience grow with depth.

## How rare encounters appear

A rare encounter starts {{percent:RareEncounterChance}} percent of the time each time you enter the dungeon and each time you step into a room for the first time, on any floor. When the floor's theme has its own encounters (every theme has four), 30 percent of rare encounters come from the themed pool and 70 percent from the twelve shared ones. Every encounter within a pool is equally likely. You cannot choose or repeat a particular encounter; they come by chance.

Changes to Chivalry and Darkness in these encounters are added directly. Some choices take a share of your HP; check your HP before a gamble.

## Encounters on any floor

### Hidden Tavern

A secret tavern. A drink costs 50 gold and heals a third of your maximum HP. Resting in the back room costs 200 gold, heals you fully and cures poison. The card game bets the lower of a quarter of your gold and 500 (at least 10): a higher card than the dealer's pays twice the bet back, a tie returns it. Talking to a stranger works once a day and gives one of five: 3 healing potions, floor x 100 experience, a tip, an offer of Strength +5 and Defense +5 that you can accept, or a drunk with nothing to say.

### Wandering Minstrel

The minstrel's songs: the first gives Strength +3, the second Defense +3, the third heals half your maximum HP, and the fourth costs 100 gold for 50 Chivalry. Just listening gives floor x 10 experience.

### Old Hermit

Sitting by the fire restores your HP and mana fully. Asking for wisdom gives Intelligence +1. Giving him 50 gold adds 25 Chivalry and one of: Strength +5, a full stock of healing potions, or floor x 300 experience.

### Fairy Circle

Asking for a blessing is good 70 percent of the time (one of: full HP, full mana, a full stock of healing potions, or floor x 200 experience) and otherwise takes a tenth of your gold. Stealing fairy dust works 30 percent of the time for floor x 500 gold and 20 Darkness; otherwise it costs a quarter of your maximum HP and half the time 2 Strength for good. Dancing gives one of five: full HP and mana with floor x 300 experience, floor x 900 gold, Charisma +5, Intelligence +3, or nothing.

### Gambling Demons

You and the demons each roll three six-sided dice; you win only with a higher roll. A gold wager wins or loses 1,000 gold. A soul wager wins Strength +10 and Intelligence +10, or loses 5 Strength and 5 Charisma for good and adds 50 Darkness. A wager of years wins floor x 1,000 experience or loses floor x 500.

### Damsel in Distress

One of four scenes, equally likely:

- **A rescue:** rushing in pays floor x 200 gold and 75 Chivalry; watching from the shadows pays floor x 150 gold and floor x 100 experience; walking away adds 30 Darkness.
- **A trap:** helping her costs a third of your maximum HP and a fifth of your gold; approaching with caution gives floor x 75 experience.
- **A princess:** escorting her pays floor x 1,000 gold, floor x 500 experience and 200 Chivalry; holding her for ransom pays floor x 5,000 gold, adds 100 Darkness and takes 100 Chivalry.
- **A warrior woman:** joining her fight pays floor x 150 gold and floor x 100 experience; only watching takes 20 Chivalry.

### Mysterious Merchant

Four offers. A potion for floor x 50 gold heals you fully. A blessing for floor x 200 gold gives +5 to one random stat of Strength, Intelligence, Wisdom, Dexterity, Constitution or Charisma. A mystery box for floor x 500 gold gives one of five: three times the price back in gold, Strength +10 and Defense +10, nothing, floor x 500 experience, or full potions and mana. Information for 100 gold is only words.

### Time Warp

No choice; one of five happens: floor x 1,500 gold; floor x 300 experience and Strength +2; the loss of half your current HP and floor x 100 experience; Constitution +3 with full HP and mana; or Defense +5.

### Usurper's Ghost

Asking about the old world gives floor x 200 experience. Seeking combat advice gives Intelligence +2 and Wisdom +2. Offering friendship gives floor x 300 gold and Strength +3.

### Ancient Library

Combat techniques give Strength +3 and Dexterity +2. Arcane knowledge gives Intelligence +3 and maximum mana +10 and fills your mana. Ancient history gives floor x 400 experience and Wisdom +2. A search for treasure maps finds floor x 600 gold 60 percent of the time.

### Wishing Well

A coin costs 100 gold for one of six wishes: full HP and mana, 500 gold, Strength +3, nothing, floor x 250 experience, or 150 gold back. Diving for coins finds floor x 200 + 0 to 499 gold 40 percent of the time and otherwise costs a third of your maximum HP.

### Arena Portal

You fight alone against one monster drawn for the floor, as a room's first monster would be (a champion 10 percent of the time), named the Arena Champion. It pays the usual kill reward, and a win adds 25 Chivalry. A loss leaves you at 1 HP.

## Encounters by theme

- **Catacombs** (floors 1 to 10): Bone Oracle, Restless Spirits, Crypt Keeper and Ancient Tomb.
- **Sewers** (11 to 20): Rat King, Lost Child, Alchemist Lab and Treasure Hoard.
- **Caverns** (21 to 35): Crystal Cave, Dragon's Hoard, Dwarven Outpost and Underground Lake.
- **Ancient Ruins** (36 to 50): Ancient Golem, Time Capsule, Magic Fountain and Lost Civilization.
- **Demon Lair** (51 to 65): Demon Bargain, Tortured Souls, Infernal Forge and Succubus.
- **Frozen Depths** (66 to 80): Frozen Adventurer, Ice Queen, Yeti Den and Aurora Vision.
- **Volcanic Pit** (81 to 90): Fire Elemental, Lava Boat, Phoenix Nest and Obsidian Mirror.
- **Abyssal Void** (91 to 100): Void Whisper, Reality Tear, Cosmic Entity and Madness Pool.

### Catacombs encounters

- **Bone Oracle:** asking about your future gives one of floor x 100 experience, Defense +2, 25 Chivalry or Wisdom +2; asking about the dungeon gives Intelligence +1.
- **Restless Spirits:** helping them find peace gives 50 Chivalry and floor x 150 experience; attacking them costs a quarter of your maximum HP and adds 20 Darkness.
- **Crypt Keeper:** 150 gold buys 2 healing potions; asking for information gives a hint about the floors ahead and floor x 50 experience.
- **Ancient Tomb:** opening the sarcophagus is a mummy half the time (a third of your maximum HP, then floor x 1,500 gold) and otherwise floor x 2,400 gold and Strength +2; taking the offerings pays floor x 600 gold.

### Sewers encounters

- **Rat King:** paying the 500 gold tribute returns floor x 900 gold. Fighting the swarm costs floor x 5 HP and half the time pays floor x 1,200 gold and floor x 200 experience.
- **Lost Child:** helping the child pays floor x 600 gold and 100 Chivalry. Checking for traps first finds one 30 percent of the time (a fifth of your maximum HP) and otherwise gives 50 Chivalry. Ignoring the child adds 10 Darkness.
- **Alchemist Lab:** drinking a random potion gives one of Strength +5, a full heal, poison, Intelligence +5 or Dexterity +3. Reading the notes gives Intelligence +2 and floor x 100 experience. Stealing supplies gives floor x 900 gold and 2 healing potions.
- **Treasure Hoard:** taking everything is a trap 40 percent of the time (a quarter of your maximum HP and floor x 900 gold) and otherwise floor x 2,100 gold; taking some carefully pays floor x 600 gold.

### Caverns encounters

- **Crystal Cave:** 60 percent of the time Intelligence +3 and full mana, otherwise the loss of a fifth of your maximum HP.
- **Dragon's Hoard:** floor x 3,000 gold, no fight.
- **Dwarven Outpost:** 3 healing potions.
- **Underground Lake:** full HP and mana.

### Ancient Ruins encounters

- **Ancient Golem:** the right answer to its riddle gives floor x 200 experience; a wrong one costs a quarter of your maximum HP.
- **Time Capsule:** floor x 1,200 gold and floor x 150 experience.
- **Magic Fountain:** full HP and mana and a cure for poison.
- **Lost Civilization:** Intelligence +5 and floor x 300 experience.

### Demon Lair encounters

- **Demon Bargain:** accepting gives Strength +10 and 100 Darkness.
- **Tortured Souls:** 30 Chivalry and floor x 100 experience.
- **Infernal Forge:** +5 weapon power for good.
- **Succubus:** resisting gives Wisdom +5; succumbing costs a third of your maximum HP and a fifth of your gold.

### Frozen Depths encounters

- **Frozen Adventurer:** floor x 900 gold and 2 healing potions.
- **Ice Queen:** Charisma +3 and full mana.
- **Yeti Den:** half the time floor x 1,500 gold, otherwise a quarter of your maximum HP.
- **Aurora Vision:** Wisdom +3 with full HP and mana.

### Volcanic Pit encounters

- **Fire Elemental:** Intelligence +3 and maximum mana +20.
- **Lava Boat:** 500 gold for floor x 300 experience.
- **Phoenix Nest:** Constitution +5 and a full heal.
- **Obsidian Mirror:** Strength +2, Intelligence +2 and Dexterity +2.

### Abyssal Void encounters

- **Void Whisper:** Intelligence +5, Wisdom +5 and 30 Darkness.
- **Reality Tear:** half the time floor x 3,000 gold, otherwise a third of your maximum HP.
- **Cosmic Entity:** floor x 500 experience and Wisdom +10.
- **Madness Pool:** looking deeper gives half the time Intelligence +10 and 50 Darkness, and otherwise takes 5 Intelligence for good.

## Reading the choice

Stat gains from these encounters are permanent. A choice that looks greedy or cruel often adds Darkness, and a kind one often adds Chivalry. When a gamble is offered, the loss can be large, so check your HP and gold before accepting.
