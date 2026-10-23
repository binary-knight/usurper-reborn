---
title: Home, the Inn and rest
path: /wiki/en/world/rest/
checked: 1.3.6
sources: Scripts/Locations/HomeLocation.cs, Scripts/Locations/InnLocation.cs, Scripts/Systems/MentalSystem.cs, Scripts/Core/GameConfig.cs
history: 1.3.6 | none
history: 1.3.5 | none
history: 1.3.4 | none
history: 1.3.3 | none
history: 1.3.2 | none
history: 1.3.0 | At the Inn, Seth Able fights at the level his challenge screen shows and a win pays the Inn's flat reward only, Inn duels and town honor duels share one daily cap of 3 wins, and a forgiven companion waits there.
history: 1.2.6 | none
history: 1.2.5 | After an online sleep, attacks by NPCs now appear in the wake-up report, and NPC attackers fight your surviving hired Inn guards.
history: 1.2.4 | none
---
The Inn and Home provide different rest and social actions. Recovering HP, recovering Mental and starting a new day are distinct effects.

## The Inn

A table rest restores Mental, with a better result when a close NPC friend is present. Sleep in single-player or renting a room online gives another recovery source. Paid options warn when Mental recovery would do nothing at the cap.

## Stat training at the Inn

The Master Trainer at the Inn (W) sells +1 to a base attribute: Strength, Dexterity, Constitution, Intelligence, Wisdom, Charisma, Agility or Stamina, {{balance:MaxStatTrainingsPerStat}} times per attribute. The price is level x {{balance:StatTrainingBaseCostPerLevel}} x (trainings of that attribute so far + 1) squared, paid from gold on hand with no tax. At level 20 the five trainings of one attribute cost 10,000, 40,000, 90,000, 160,000 and 250,000 gold.

The 4th training of an attribute also takes 1 Heart of the Ocean, and the 5th takes 1 Heart of the Ocean and 1 Eye of Manwe. See [crafting materials](/wiki/en/items/materials/).

## Inn room cost

Online, a room at the Inn (N) costs level x {{balance:InnRoomCostPerLevel}} gold, 1,500 at level 20, paid from gold on hand and then your bank account. It saves and logs you out.

With a room you can hire up to {{balance:MaxSleepGuards}} guards to fight anyone who attacks you in your sleep. Base price and HP: rookie 100 and 80, hound 150 and 60, veteran 300 and 150, troll 500 and 200, elite 600 and 250, drake 1,000 and 300. Both are multiplied by (1 + level / 10), and each guard already hired adds 50 percent to the next one's price. A sleeper at the Inn also fights with 50 percent more Strength and Defense.

## Attacked while sleeping

Online, a player who beats a sleeper at the Inn (K) takes {{percent:SleeperGoldTheftPercent}} percent of the sleeper's gold on hand, and the sleeper loses {{balance:SleeperXPLossPercent}} percent of their experience.

## Inn games

- **Drinking game (G):** 20 gold a game, {{balance:MaxDrinkingGamesPerDay}} games a day.
- **High-Low dice:** a right guess pays 1.2 times the pot, a tie returns the pot, and you can let the pot ride for at most {{balance:GamblingMaxDoubleDown}} wins in a row. Only your stake leaves your purse.
- **Skull and Bones:** a tile game to 21 against a dealer who draws to 17. A win pays {{balance:BlackjackPayoutMultiplier}}x, your bet back and as much again, and a 21 with your first two tiles pays {{balance:BlackjackBonusPayout}}x; a tie costs nothing.
- **Arm wrestling:** {{balance:MaxArmWrestlesPerDay}} matches a day, for a wager of the opponent's level x {{balance:ArmWrestleBetPerLevel}} gold.

L opens the Inn's games of chance. The Dark Alley has its own Gambling Den; see [the Dark Alley](/wiki/en/world/dark-alley/).

## Fights at the Inn

Seth Able fights at the level his challenge screen shows. A win pays the Inn's flat reward and nothing else: no separate combat reward and no loot roll. Duels with NPCs at the Inn share one daily cap of 3 wins with honor duels in town. A companion who left you and was forgiven at the [Temple](/wiki/en/world/temple/) waits at the Inn.

## Home

Home rest, sleep and time with a spouse or lover can restore Mental. Some recovery is once per day. In single-player sleep also starts a new day; online uses the shared world's daily behavior. A Home rest heals a share of your maximum HP and mana that grows with your Living Quarters; the numbers are in [Home and family](/wiki/en/world/home/).

## Daily recovery

The daily reset grants Mental recovery, but standing idle in town is not a recovery action. Once-a-day recovery that has no effect at your cap remains available for later.

If you are Broken, sleep alone is not the cure. Go to the [Healer](/wiki/en/characters/afflictions/) for therapy or applicable rehab.
