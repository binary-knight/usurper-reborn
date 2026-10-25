---
title: Gods and faith
path: /wiki/en/gods/
checked: 1.3.8
sources: Scripts/Systems/GodSystem.cs, Scripts/Systems/GodBoonSystem.cs, Scripts/Locations/TempleLocation.cs, Scripts/Systems/FaithSystem.cs, Scripts/Locations/MainStreetDistricts.cs, Scripts/Core/Character.cs, Scripts/Locations/DungeonLocation.cs, Scripts/Systems/CombatEngine.cs, Scripts/Core/GameConfig.cs
history: 1.3.8 | none
history: 1.3.7 | none
history: 1.3.6 | none
history: 1.3.5 | none
history: 1.3.4 | none
history: 1.3.3 | none
history: 1.3.2 | Divine wrath cuts your damage and defense by 10, 20 or 30 percent for the whole fight, as the screen states.
history: 1.3.0 | none
history: 1.2.5 | A Temple priest now points you to the first Seal from level 10, Temple screens are shown in all five languages, and sacrifice notices reach a player-god in that god's language.
history: 1.2.4 | The notice a player-god gets when you sacrifice gold at its altar now reaches its mailbox.
---
You worship one god at a time. The Temple's ten gods and the player-gods compete for followers. Your personal Favor with your god, from {{balance:GodFavorMin}} to {{balance:GodFavorMax}}, controls how much of its boon you receive.

{{table:gods}}

## Choose a god at the Temple

1. From Main Street, open Guild Row with G and press T for the Temple. It appears in the menu after your first few levels.
2. Press A to enter the Nave of the Gods.
3. Press W, "Worship a god, or change your god".
4. The altars are listed. Type the god's name, or part of it, and confirm with Y when asked whether you are sure. Press Enter at the list to cancel.
5. The game says you are now a believer. Pray daily with Y in the Nave to build [Favor](/wiki/en/gods/favor/).

The Undercroft (U in the Temple halls) has its own W, which lists only the dark altars. Player-gods appear on the same altar list as the Temple gods. Read [immortals, player-gods and the Pantheon](/wiki/en/gods/player-gods/) for how a player becomes a god and what an immortal does.

## Switch or leave your god

Use the same W in the Nave. The game shows your current god and asks whether you have lost your faith. Answer Y, optionally send your old god a note, then choose the new god from the list. Press Enter at the list instead to stay without a god.

Switching discards all your Favor with the old god, and you start at 0 with the new one. The game shows the cost before you confirm. A change made by the game or by another player, such as a player-god recruiting you, costs nothing more.

### What happens if I change gods

Leaving a Temple god records divine wrath by the Favor you lose: level 1 for 1 to 24 Favor, level 2 for 25 to 49, and level 3 for 50 or more. Leaving with 0 Favor records none. Wrath you already carry adds up, to at most level 3.

Leaving a player-god brings a smite at once instead, from {{percent:GodSmiteMinPercent}} to {{percent:GodSmiteMaxPercent}} percent of your maximum HP by the Favor you lose: 10 percent plus 0.15 percent for each point of Favor. Leaving with 60 Favor costs 19 percent of your maximum HP. The smite never takes you below 1 HP, and leaving with 0 Favor brings none.

## Divine wrath in the dungeon

Divine wrath strikes before a dungeon fight. Each time a fight starts, the chance is 20 percent per wrath level: 20, 40 or 60 percent.

- **Level 1:** the god's anger is shown, and nothing else happens.
- **Level 2:** you lose a quarter of your current HP, never below 1.
- **Level 3:** you lose half your current HP, never below 1, and half your mana, and half the time you are struck blind or catch the plague or measles.

Then your basic swings deal 10, 20 or 30 percent less and your defense against monster hits is 10, 20 or 30 percent lower for that fight, as the screen states. After striking, level 3 drops to level 1, and levels 1 and 2 are cleared.

Wrath that has not struck fades: each fight start that does not strike counts down from 50 + 20 x the level last recorded, and at 0 the level falls by one and the count restarts at 30.

## Build a relationship

Use [Favor, prayer and sacrifice](/wiki/en/gods/favor/) for tiers, devotion, wards and Miracles. A god's individual page lists its generated boon constants, deeds and taboos.
Read [what each boon does](/wiki/en/gods/boons/) and [daily Miracles](/wiki/en/gods/miracles/) for their actual combat and reward contexts.

The Temple's rankings describe a changing world, not a fixed table in this wiki. Player-god domains use the same canonical domain mechanics but have additional strength and activity rules.
