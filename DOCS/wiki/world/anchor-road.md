---
title: Anchor Road challenges
path: /wiki/en/world/anchor-road/
checked: 1.3.1
sources: Scripts/Locations/AnchorRoadLocation.cs, Scripts/Locations/MainStreetDistricts.cs, Scripts/Core/GameConfig.cs, Scripts/Data/GauntletChampionData.cs, Scripts/Systems/QuestSystem.cs, Scripts/Systems/AlignmentSystem.cs, Scripts/Systems/MonsterGenerator.cs, Scripts/Systems/CombatEngine.cs
history: 1.3.1 | none
history: 1.3.0 | none
history: 1.2.7 | none
history: 1.2.6 | none
history: 1.2.5 | A refused Sellsword Hall turn-in now says why in words, and the Gauntlet champion broadcast reaches each player in their own language.
history: 1.2.4 | none
history: 1.2.3 | none
---
Anchor Road is the town's challenge ground. From Main Street open the Castle Grounds district with C, then press C for Challenges. It appears once the full town menu unlocks.

## Challenges

- **B Bounty hunting:** fight a townsperson with a dark reputation, or one near your level. It uses one of your daily player fights.
- **G Gang war:** your team fights another team. You need a team and team fights left for the day.
- **T The Gauntlet:** ten waves of monsters and champions, once a day, for an entry fee that grows with your level.
- **M Sellsword Hall:** take freelance contracts from three factions, a few a day.

The Gauntlet is not the [Arena](/wiki/en/online/arena/), which is online player combat.

## Bounty hunting pay

The board lists up to 5 NPCs with more than 200 Darkness, the darkest first; with none, 3 random NPCs up to your level + 5. A fight uses one of your daily player fights. A win pays the target's level x 100 + its Darkness in gold and its level x 50 experience, and leaves the target at 0 HP. A loss leaves you at 1 HP or more.

## The Gauntlet

You need level {{balance:GauntletMinLevel}}, one of your daily player fights and {{balance:GauntletEntryFeeQuadraticCoefficient}} x your level x your level gold (9,000 at level 30). You can run it once a day.

### Gauntlet waves

There are {{balance:GauntletWaveCount}} waves. Waves 1 to 3 are a single monster of your level - 3 + the wave number. Waves 4 to 10 are the seven champions in order, each a monster of your level + its level bonus (at most 100) with its own HP, Strength and Defence multipliers and abilities, fighting as a boss. You fight alone.

{{table:gauntlet-champions}}

Between waves you regain {{percent:GauntletHealBetweenWaves}} percent of your maximum HP and {{percent:GauntletManaRestoreBetweenWaves}} percent of your maximum mana, and before each wave after the first you can stop and keep what you have won.

### Gauntlet rewards

Each wave won pays {{balance:GauntletGoldPerWavePerLevel}} x your level gold and {{balance:GauntletXPPerWave}} x the wave number x your level experience, and 1 Fame for waves 1 to 3, 2 for 4 to 6, 3 for 7 to 9 and 5 for wave 10. At level 30, all ten waves pay 15,000 gold and 41,250 experience before the final bonus. Finishing wave 10 pays a final bonus by your level when you finish, and the title of that tier if it is your best so far:

{{table:gauntlet-tiers}}

A Grand Champion keeps +{{percent:GrandChampionDamageBonus}} percent damage and +{{percent:GrandChampionDefenseBonus}} percent defence for good. The Gauntlet has its own achievements.

### Gauntlet champion loot

Each champion you beat drops its own item, made at your level. One champion per run, chosen at the start (the Tyrant 35 percent of the time, each other champion about 11 percent), drops at your top rarity: Uncommon below level 20, Rare from 20, Epic from 40, Legendary from 60 and Artifact from 80. The others drop 1 or 2 rarities lower.

### Can I die in the Gauntlet

Each wave is to the death {{balance:GauntletDeathChancePercent}} percent of the time: losing that wave is a real death and also takes back the Fame of the run. Losing any other wave costs {{percent:GauntletDragoutXPLossPercent}} percent of your experience and the run's Fame + {{balance:GauntletDragoutFameLossPenalty}}. Fleeing a wave ends the run with what you have won.

## Sellsword Hall contracts

The Crown, the Shadows and the Faith each post contracts on the board, which refreshes once a day; an unclaimed contract leaves after {{balance:MercContractStaleDays}} days. You can claim {{balance:MaxMercContractsPerDay}} contracts a day and have 7 days to finish each one.

### Sellsword ranks

Your rank comes from the contracts you have completed, and it sets how many contracts each faction shows you and your pay. Tier 2 contracts appear from the Sellsword rank.

{{table:merc-ranks}}

### Sellsword contract pay

A contract pays your level x {{balance:MercContractBaseGoldTier1}} x the contract's pay factor in gold, times your rank's pay factor when you turn it in. Its count is the contract's base count + your level / 5. At level 30, Unhallowed Dead (pay factor 1.0, base count 3) pays 4,500 gold for 9 Zombie kills before your rank's factor.

{{table:merc-contracts}}

A completed contract adds 15 standing (tier 1) or 20 (tier 2) with its faction, at most {{balance:MaxDailyMercStandingGain}} a day per faction. A Faith contract also moves you 3 toward Chivalry and a Shadows contract 3 toward Darkness. The count of completed contracts carries over into [New Game Plus](/wiki/en/characters/new-game-plus/).

## Town control

With a team you can press C to claim the town for your team, or F to give up town control.

## Prison grounds and jailbreak

P leads to the prison grounds outside the cells. You can view the prisoners, or try a jailbreak once a day. The chance is 30 + your level + Agility / 5 percent. A success moves you 50 toward Chivalry and makes the news. A failure costs a fifth of your maximum HP, never below 1, and moves you 25 toward Darkness. Nobody is freed either way.

## Status and return

S shows your challenge status, and R returns to Main Street.
