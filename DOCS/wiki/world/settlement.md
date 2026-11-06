---
title: The Outskirts settlement
path: /wiki/en/world/settlement/
checked: 1.3.10
sources: Scripts/Locations/SettlementLocation.cs, Scripts/Systems/SettlementSystem.cs, Scripts/Core/GameConfig.cs, Scripts/Locations/MainStreetDistricts.cs, Scripts/Systems/CombatEngine.cs, Scripts/Locations/DungeonLocation.cs, Scripts/Systems/GodBoonSystem.cs
history: 1.3.10 | The Barracks sends a settler militia fighter into each of your next 3 fights, the Smugglers' Cache takes 10 percent off your next 5 shop purchases, and the Market Stall trades potions and buys backpack gear.
history: 1.3.9 | none
history: 1.3.8 | none
history: 1.3.7 | none
history: 1.3.6 | none
history: 1.3.5 | none
history: 1.3.4 | none
history: 1.3.3 | none
history: 1.3.2 | The Workshop and Arena damage buffs apply to your attacks in fights, the Prison buff also halves trap damage, the Herbalist Hut gives only herbs below their limit, and the Mystic Circle spends its day only when it gives something.
history: 1.2.5 | Watchtower scout reports name monsters in the player's language, and the Electron settlement menu labels are translated into all five languages.
history: 1.2.4 | none
history: 1.2.3 | The settlement workshop, the watchtower scout report and settlement news are translated into all five languages, and long scout report rows wrap.
---
The Outskirts is an NPC-built community beyond the city gates. With the classic Main Street, which is the default, press > for the Outskirts. With the district layout, open the Castle Grounds district with C, then press E for the Settlement. It is not one of the safe [outposts](/wiki/en/world/dungeon/#dungeon-outposts) found inside the dungeon. The numbers below are per step of the world simulation, the same steps in which NPCs act.

## When the Outskirts appears

The Outskirts shows in the town menu once {{balance:SettlementMinNPCs}} NPC settlers live there, and at most {{balance:SettlementMaxNPCs}} settle. Each step, an NPC that is sociable or loyal (Sociability or Loyalty above 0.4), not aggressive (Aggression under 0.6) and above 70 percent of its HP may move in, with a 2 percent chance; a vengeful NPC with enemies does not. A settler leaves when its HP falls below half, or when it is very aggressive (above 0.7) and has enemies.

## Settlement buildings and their cost

The settlers build seven core buildings: Palisade, Tavern, Market Stall, Shrine, Workshop, Watchtower and Council Hall. Each goes through three tiers, and each tier costs its own amount from the pooled gold: 5,000 for the Foundation, 25,000 to be Built and 100,000 to be Upgraded. The settlers choose the next core building to work on by the sum of their traits for it: Courage for the Palisade, Sociability for the Tavern, Greed for the Market Stall, Patience for the Shrine, Ambition for the Workshop, Courage and Intelligence for the Watchtower and Intelligence for the Council Hall.

Each step, every settler with more than 100 gold gives (1 - its Greed) x 1 percent of its gold, never going below 100, to the building under construction.

## Contribute gold

Your gold goes to the core building under construction; with none, to the proposed building under construction; with neither, to the communal treasury that the Council Hall pays out. Every contribution is recorded as yours. Terran recognizes qualifying settlement donations as a deed. This uses your shared daily deed cap, not an unlimited Favor loop.

## Settlement services

A service opens when its building reaches the tier below. You can hold only one of the timed settlement buffs at a time (Tavern, Palisade, Arena, Thieves' Den, Prison, Library or Barracks); it lasts {{balance:SettlementBuffDuration}} fights, the Barracks 3.

- **Tavern (Built):** +10 percent experience from fights.
- **Market Stall (Built):** sells healing and mana potions one at a time at the [Healer's](/wiki/en/world/healer/) full price for your level, tax included (the Faith's Healer discount does not apply here), up to what you can carry, and buys an identified, uncursed weapon, shield, armor piece or accessory from your backpack for half its value, as the town shops do (the Shadows fence bonus included).
- **Council Hall (Built):** once a day, 10 percent of (the treasury x your share of all player contributions), at least 50 gold and at most the treasury; with no contribution of yours, 50 gold. Terran's boon can add to it.
- **Shrine (Upgraded):** once a day, heals 30 percent of your maximum HP.
- **Palisade (Upgraded):** +5 percent defense.
- **Workshop (Upgraded):** once a day, sharpens your weapon for 10 fights: +20 percent damage on your basic attacks, not on spells or class abilities.
- **Watchtower (Upgraded):** scouts any dungeon floor from 1 to 100 room by room, showing each room's monsters, traps, treasure, events, stairs and special rooms, with a sample monster.

## Settlement proposals

Once 3 core buildings stand at Foundation or better and at least 3 settlers live there, the settlers can propose a new building. The one their traits favour most is proposed, if its condition is met, and they deliberate for {{balance:SettlementProposalDeliberationTicks}} steps. You can endorse it for {{balance:SettlementEndorsementCost}} gold, which counts as 2 votes for, or oppose it for free, 2 votes against; one vote per player. It passes with more votes for than against. A passed proposal is built one tier, with the same costs as a core building, and needs to pass again for its next tier. A failed one cannot come back for {{balance:SettlementProposalCooldownTicks}} steps.

### Proposed buildings

Each is listed with what it needs to be proposed and the tier its service needs.

- **Arena** (needs 8 settlers; service when Upgraded): +10 percent damage on your basic attacks for 5 fights, not on spells or class abilities.
- **Thieves' Den** (needs no Watchtower Built yet; Upgraded): +15 percent gold from fights.
- **Mystic Circle** (needs a Shrine Foundation; Upgraded): once a day, 30 percent of your maximum mana, or of your maximum HP for a class without mana. When that is already full it restores nothing and the day is not spent.
- **Prison** (needs a Palisade Built; Upgraded): halves the damage of dungeon room traps, chest traps and failed riddles and puzzles, for the next 5 times. The gold and experience a trap takes are not reduced.
- **Scouts' Lodge** (needs a Watchtower Foundation; Upgraded): a sample monster for 3 floors in a row.
- **Library** (needs a Council Hall Foundation; Upgraded): +5 percent experience from fights, and the day's learning Mental recovery.
- **Herbalist Hut** (needs 6 settlers; Built): one random herb a day, drawn among the herbs you carry fewer of than their limit. When every herb is at its limit it gives nothing and the day is not spent.
- **Gambling Hall** (needs a Tavern Built; Built): bet up to {{balance:SettlementGambleMaxBet}} gold; you win double the bet back 50 percent of the time.
- **Oracle's Sanctum** (needs a Mystic Circle Built; Upgraded): a hint about the dungeon.
- **Memorial** (always possible): no service. When it passes, settler gifts are 25 percent larger for 10 steps.
- **Barracks** (needs a Palisade Built; Built): a settler militia fighter at your level minus 2 (at least level 1) fights at your side in each of your next 3 monster fights and leaves at the end of each; it takes no experience and never joins your dungeon party. It does not join the Pit, the Gauntlet, the bank guards or an arrest, and a duel counts one of the 3 fights, as it does for every timed settlement buff.
- **Smugglers' Cache** (needs no Council Hall Built yet; Built): your next 5 purchases in the Weapon, Armor and Magic shops cost 10 percent less (see [shops](/wiki/en/items/shops/)). It is not a timed buff and does not count fights; you can take it again once the 5 are spent.

## Changing world state

The settlement develops with its population and resources. Check the live building list rather than expecting the wiki to prescribe a fixed construction order or treasury balance.
