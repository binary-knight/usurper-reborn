---
title: Rare dungeon encounters
path: /wiki/en/world/rare-encounters/
checked: 1.2.3
sources: Scripts/Systems/RareEncounters.cs, Scripts/Locations/DungeonLocation.cs
history: 1.2.3 | Rare encounter text is translated into all five languages.
---
Rare encounters are unusual random events that can interrupt a dungeon trip. They are separate from ordinary [room features and events](/wiki/en/world/dungeon-features/): they can happen when you enter the dungeon and when you step into a room for the first time, on any floor. Most offer a choice, and most choices carry a risk as well as a reward. Leaving is usually an option.

## How they appear

Each check has a small chance to start a rare encounter. The encounter is drawn either from a pool shared by every floor or from a smaller pool that fits the current floor's theme. You cannot choose or repeat a particular encounter; they come by chance.

:::spoiler Spoiler: encounter odds
Each check starts a rare encounter 5% of the time. When the floor's theme has its own encounters, 30% of rare encounters come from the themed pool and the rest from the shared pool. Every encounter within a pool is equally likely.
:::

## Encounters on any floor

- **Hidden Tavern**: a secret tavern in the dark. You can drink, gamble, talk to a stranger, rest or leave.
- **Wandering Minstrel**: a minstrel offers a choice of songs.
- **Old Hermit**: a hermit who can restore you, teach you or ask for a gift.
- **Fairy Circle**: a ring of glowing mushrooms. Accept a blessing, steal fairy dust, dance or leave.
- **Gambling Demons**: demons invite you to play dice for gold, your soul or your years.
- **Damsel in Distress**: one of several scenes in which someone needs help, and not every scene is what it seems.
- **Mysterious Merchant**: a merchant with a small, unusual stock.
- **Time Warp**: time folds around you, for better or worse. There is no choice.
- **Usurper's Ghost**: a ghost from the original game who will share lore, answer a question or befriend you.
- **Ancient Library**: a hidden library with several books to study.
- **Wishing Well**: toss a coin, drink from the well or leave.
- **Arena Portal**: a portal to a fight against a monster suited to your level.

## Encounters by theme

- **Catacombs**: Bone Oracle, Restless Spirits, Crypt Keeper and Ancient Tomb.
- **Sewers**: Rat King, Lost Child, Alchemist Lab and Treasure Hoard.
- **Caverns**: Crystal Cave, Dragon's Hoard, Dwarven Outpost and Underground Lake.
- **Ancient Ruins**: Ancient Golem, Time Capsule, Magic Fountain and Lost Civilization.
- **Demon Lair**: Demon Bargain, Tortured Souls, Infernal Forge and Succubus.
- **Frozen Depths**: Frozen Adventurer, Ice Queen, Yeti Den and Aurora Vision.
- **Volcanic Pit**: Fire Elemental, Lava Boat, Phoenix Nest and Obsidian Mirror.
- **Abyssal Void**: Void Whisper, Reality Tear, Cosmic Entity and Madness Pool.

Themed encounters tend to be tied to the place: a riddle from a golem, a fountain that heals, a bargain with a demon, a mirror that shows a stronger reflection. Rewards scale with the floor, and several choices shift your alignment toward light or darkness.

## Reading the choice

Gold and experience from these encounters grow with the floor. Some choices give permanent stat gains; others take HP, gold or stats. A choice that looks greedy or cruel often adds Darkness, and a kind one often adds Chivalry. When a gamble is offered, the loss can be large, so check your HP and gold before accepting.

:::spoiler Spoiler: outcomes of the encounters on any floor
In the Hidden Tavern a drink heals a third of your HP for a small fee and a paid rest heals fully and cures poison. Gambling there is a card draw against the dealer. The Wandering Minstrel's songs can raise Strength or Defence permanently, heal half your HP or add Chivalry for a fee. The Old Hermit can restore HP and mana, teach a point of Intelligence, or take a gift for a Strength gain, a potion refill or experience. Accepting the fairies' blessing usually restores you or grants experience but sometimes costs a tenth of your gold. Stealing fairy dust rarely succeeds and can cost HP and Strength. The Gambling Demons roll three dice against you: gold wins or loses a thousand gold, a soul wager wins or loses stats with Darkness, and a wager of years wins or loses experience. The escort choice in the princess scene pays well with Chivalry, while ransoming her pays far more but adds Darkness and costs Chivalry. The Usurper's Ghost gives experience for listening, Intelligence and Wisdom for asking, and gold with Strength for friendship. The Ancient Library's books give different stat gains, experience or a treasure map. Tossing a coin into the Wishing Well grants one of several wishes, while drinking from it can pay gold or hurt you. Winning the Arena Portal fight adds Chivalry.
:::

:::spoiler Spoiler: outcomes of the themed encounters
The Bone Oracle's prophecy grants experience, Defence, Chivalry or Wisdom. The Ancient Tomb can hold a mummy or a large sum of gold. Paying the Rat King's toll returns much more gold. Returning the Lost Child adds a great deal of Chivalry, while robbing the child adds Darkness. Drinking in the Alchemist Lab is a gamble between a stat gain, a heal and poison. The Crystal Cave usually grants Intelligence and mana and otherwise costs HP. The Dragon's Hoard pays a large sum of gold without a fight. The Dwarven Outpost gives potions and the Underground Lake restores HP and mana. The Ancient Golem rewards a right answer with experience and punishes a wrong one with damage. The Magic Fountain restores HP and mana and cures poison. Accepting the Demon Bargain gives a large Strength gain with heavy Darkness. Resisting the Succubus grants Wisdom, while succumbing costs HP and gold. The Yeti Den and the Reality Tear are even gambles between gold and damage. The Phoenix Nest grants Constitution and a full heal, and the Obsidian Mirror raises Strength, Intelligence and Dexterity. The Void Whisper grants Intelligence and Wisdom with some Darkness, and the Madness Pool can raise or lower Intelligence.
:::
