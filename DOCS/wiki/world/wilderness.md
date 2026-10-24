---
title: Wilderness exploration
path: /wiki/en/world/wilderness/
checked: 1.3.7
sources: Scripts/Data/WildernessData.cs, Scripts/Locations/WildernessLocation.cs, Scripts/Data/DruidShrineData.cs, Scripts/Core/GameConfig.cs, Scripts/Core/Character.cs, Scripts/Systems/GodBoonSystem.cs, Scripts/Systems/MentalSystem.cs, Scripts/Systems/MonsterGenerator.cs, Scripts/Data/BeastData.cs, Scripts/Systems/CombatEngine.cs, Scripts/Systems/StatEffectsSystem.cs, Scripts/Systems/AlignmentSystem.cs
history: 1.3.7 | none
history: 1.3.6 | none
history: 1.3.5 | none
history: 1.3.4 | none
history: 1.3.3 | A wilderness monster of level 25 or more has more HP and hits harder, as a dungeon monster of that level does.
history: 1.3.2 | The Mountain Goat passive text and three druid shrine texts now state only what they give.
history: 1.2.5 | Wild beast and monster names are shown in the player's language, and beast and monster encounter rows wrap at 79 columns.
history: 1.2.4 | The tame success text wraps at 79 columns.
history: 1.2.3 | Wilderness scouting, beast species, the shrine list and discovery news are translated into all five languages.
---
The wilderness lies outside the town and the dungeon. It has four regions, each opened by your level, with fights, foraging, ruins, travellers, wayside shrines, discoveries you can revisit and five druid shrines for a pilgrimage.

## Wilderness regions and the level you need

{{table:wilderness-regions}}

A region refuses you below its level. Wilderness fights do not charge dungeon depth strain.

## How many trips a day

You can make {{balance:WildernessMaxDailyExplorations}} exploration trips a day and {{balance:WildernessMaxDailyRevisits}} discovery revisits a day. In single-player a trip takes {{balance:WildernessTimeCostMinutes}} game minutes and adds {{balance:WildernessFatigueCost}} Fatigue; online it costs neither. Your first wilderness exploration of the day restores {{balance:MentalWildernessGain}} Mental.

## What happens on a trip

Each trip is one encounter: a fight 40 percent of the time, foraging 25 percent, ruins 15 percent, a traveller 10 percent and a wayside shrine 10 percent. After it there is a 10 percent chance to find a new discovery of that region and a separate {{balance:BeastEncounterChancePercent}} percent chance to meet a tameable beast (see [beasts](/wiki/en/combat/companions/)).

Sylvana's boon adds up to {{balance:GodBoonSylvanaWildernessPct}} percent, at full Favor strength, to the wilderness bonus gold of a won fight, foraged gold, ruins gold and shrine experience. It does not change the kill reward of a fight.

### Wilderness fights

The monster is one of the region's eight named creatures, at the higher of the region's level and your level - 2 + 0 to 4, built like a dungeon monster of that level with its own family and abilities. Your companions fight with you. A win pays the usual kill reward plus bonus gold of 10 to 29 times (1 + region level / 10): 1.1 times in the forest, 2 in the mountains, 3 in the swamp and 4 on the coast (40 to 116 gold).

### Foraging

Foraging picks one of the region's five results with equal weight (listed in the table above). A herb is added only while you carry fewer than its limit. A small heal restores a tenth of your maximum HP and a medium heal a fifth. Gold uses L, the higher of the region's level and yours: small gold is 30 x L plus 0 to 20 x L - 1, medium gold 100 x L plus 0 to 200 x L - 1, and large gold 150 x L plus 0 to 200 x L - 1. At level 30 large gold is 4,500 to 10,499.

### Ruins

If you search ruins: 60 percent treasure (medium gold, and a 30 percent chance of one healing potion up to your limit), 20 percent a trap for a tenth of your maximum HP (never below 1 HP), 20 percent nothing.

### Travellers

If you talk: 40 percent of the time the traveller offers a healing potion for 20 to 49 gold, 30 percent shares a hint, and 30 percent heals an eighth of your maximum HP.

### Wayside shrines

If you pray: 30 percent heals a quarter of your maximum HP and mana, 25 percent gives a permanent +1 to Strength, Dexterity or Wisdom, 20 percent gives 10 + 5 x your level experience, and 25 percent nothing.

## Wilderness discoveries

Each region has two discoveries, each with a level (listed in the table above). A discovery is found by the 10 percent roll after a trip, picked among those of the region you have not found and whose level you have. A discovery revisit (D) plays that discovery's kind of encounter (fight, shrine, ruins or traveller) with the same odds as on a trip. A revisit uses none of your trips, takes no time or Fatigue, and rolls no new discovery or beast.

## Druid shrines: pilgrimage and attunement

Pilgrimage (P) attunes you to one of five druid shrines. An attunement lasts {{balance:ShrineAttunementHours}} real hours online, or to the end of the next game day in single-player, and you can hold only one at a time: you cannot start another until it ends. Each visit adds 1 to your favor with that shrine and can move your alignment.

{{table:druid-shrines}}

### What each attunement does

- **Stone Circle (Iron Mountains):** {{balance:ShrineTerravokHpRegenPerRound}} HP back at the start of each combat round.
- **Broken Blade Altar (Iron Mountains):** your basic attacks deal {{percent:ShrineMaelkethMeleeBonus}} percent more damage.
- **Moonwell (Blackmire Swamp):** +{{balance:ShrineNocturaCritChanceBonus}} critical hit chance.
- **Lantern Shrine (Whispering Forest):** holy ability damage +{{percent:ShrineAurelionHolyDamageBonus}} percent and holy enchant hits +{{percent:ShrineAurelionHolyProcBonus}} percent.
- **Heart of the Tide (Stormbreak Coast):** +{{balance:ShrineVeloraCharismaBonus}} Charisma and NPC reactions {{percent:ShrineVeloraReactionBonus}} percent stronger.

### Shrine favor gifts

Your 10th visit to a shrine gives a lasting gift: the Stone Circle +25 maximum HP, the Broken Blade Altar +3 Strength, the Moonwell +3 Dexterity, the Lantern Shrine +5 Wisdom and the Heart of the Tide +5 Charisma. The 25th and 50th visits give a message and nothing more.

## What the reference cannot promise

A built-in region definition does not establish that a named item always drops there. This wiki does not invent a drop source from flavor text or rarity. For fixed gear requirements, use the [equipment catalog](/wiki/en/items/).
