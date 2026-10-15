---
title: Castle, king and throne
path: /wiki/en/world/castle/
checked: 1.3.1
sources: Scripts/Locations/CastleLocation.cs, Scripts/Locations/MainStreetDistricts.cs, Scripts/Core/King.cs, Scripts/Systems/ChallengeSystem.cs, Scripts/Systems/QuestSystem.cs, Scripts/Systems/DailySystemManager.cs, Scripts/Locations/BaseLocation.cs, Scripts/Systems/FactionSystem.cs, Scripts/Systems/CombatEngine.cs, Scripts/Systems/TrainingSystem.cs, Scripts/Core/Character.cs, Scripts/Systems/StreetEncounterSystem.cs, Scripts/Locations/DarkAlleyLocation.cs, Scripts/Locations/WeaponShopLocation.cs, Scripts/Locations/ArmorShopLocation.cs, Scripts/Locations/MagicShopLocation.cs, Scripts/Locations/TempleLocation.cs, Scripts/Systems/DivineBlessingSystem.cs, Scripts/Core/GameConfig.cs
history: 1.3.0 | An NPC on a player's team is no longer picked to challenge an NPC ruler or to be crowned on an empty throne, and one kill advances a royal quest objective once.
history: 1.2.5 | A player in any language can no longer take a second royal quest, and a royal quest to clear a dungeon floor now asks you to clear a floor.
history: 1.2.4 | none
---
The Castle is where the realm's ruler holds court. Open the Castle Grounds district from Main Street and press K. It appears in the menu after your first few levels.

## Visiting as a commoner

A character who is not the ruler can view the Royal Guard and the prison cells, donate gold to the royal purse, read the history of monarchs, seek an audience and apply to join the Royal Guard. Characters with enough level and Chivalry and no criminal record can also join the Crown faction, which opens the Royal Armory (see [factions](#factions-the-crown-the-shadows-and-the-faith)).

### Joining the Royal Guard and its pay

You can join the Royal Guard from level 5 if your Darkness is no more than your Chivalry + 50 and fewer than {{balance:MaxRoyalGuards}} guards serve. A guard is paid {{balance:BaseGuardSalary}} + 20 x your level gold at each daily reset from the treasury, and joining moves you 10 toward Chivalry. At level 20 the pay is 700 gold a day.

When someone challenges the throne, a player guard is alerted and the fight waits 2 world ticks. Answering Y takes you to the Castle to defend in person. Answering N costs 25 loyalty, and at 20 loyalty or less you are dismissed; giving no answer in time costs 15.

### What does donating to the royal purse give

A donation to the royal purse gives Chivalry of the gold / 100, at most 50, and Crown standing of the gold / 50 with no limit; for a Crown member that standing also counts toward rank. 5,000 gold gives 50 Chivalry and 100 Crown standing.

## Seeking an audience: reputation

The Castle's reputation for you is (Chivalry + Fame) / 2. An audience needs 25. Higher reputation opens more requests: a royal quest from 50, a royal loan from 75, a royal blessing from 100 and a tax relief petition from 150. Knighthood and a pardon have their own conditions.

### How do I get knighted

Knighthood needs 200 Chivalry, 150 Fame, level 15, and Darkness below your Chivalry or a Balanced alignment. A knight is called Sir or Dame and deals {{percent:KnightDamageBonus}} percent more damage and has {{percent:KnightDefenseBonus}} percent more defence for good.

### How much is a royal pardon

A pardon clears your Darkness for Darkness x 50 x (1 + your level / 10, rounded down) gold: 25 percent off at reputation 100 and 50 percent off at 200. With 300 Darkness at level 25 and reputation 120 it costs 300 x 50 x 3 x 0.75 = 33,750 gold.

### Royal loan: interest and due date

A royal loan is at most the lower of half the treasury and reputation x 10 x your level, and at least 500. You repay it with 10 percent interest within 30 days. While it is overdue it costs {{balance:RoyalLoanChivalryLossEarly}} Chivalry a day for the first 7 days, {{balance:RoyalLoanChivalryLossMid}} a day for days 8 to 14 and {{balance:RoyalLoanChivalryLossLate}} a day after that, and it can put a bounty on you.

### What does the royal blessing do

The royal blessing costs 500 gold x your level, half at reputation 200 and nothing at 300. It restores up to 25 HP and 25 mana at once and lasts {{balance:RoyalBlessingDuration}} combat rounds, counted across fights: 10 percent more attack, 10 percent more defence and a hit roll that treats a monster's armor class as 2 lower.

## How to become king

Both ways to the throne need level {{balance:MinLevelKing}}.

- **Claim an empty throne.** When no one rules, the menu offers C to claim it. You start a fresh court with a treasury of {{balance:DefaultRoyalTreasury}} gold.
- **Challenge the ruler.** When someone rules, press I to infiltrate the castle. You fight the monster guards, then the hired guards, then the ruler, who fights with {{balance:KingDefenderHPBonus}} times the HP and {{balance:KingDefenderDefBonus}} times the defence. You can try once a day, not within 24 real hours of a coronation, and not when the ruler is more than {{balance:KingChallengeLevelRange}} levels above you.

In a challenge fight a hired guard with loyalty below 30 flees 30 percent of the time, and one below 15 may instead turn on its ruler 20 percent of the time. Player guards defend in person, not in that fight.

You must leave your team before you take the throne, and the game asks first. If you lose, you are thrown out to Main Street alive. If you win, you inherit the treasury, prisoners and orphans, but not the guards. Online, a ruler who is offline defends with an AI copy of their character, and a team can besiege the castle with B instead.

### Can NPCs take my throne

Yes. An NPC challenges a player ruler only if it is level {{balance:NPCMinLevelToChallenge}} or more, within {{balance:NPCChallengeLevelRange}} levels of the ruler, has an ambition above {{balance:NPCMinAmbitionToChallenge}}, is not in prison and is not a story character; at most {{balance:MaxNPCChallengesPerDay}} NPC challenge comes a day. A failed challenger goes to prison. An NPC on a player's team is not picked to challenge an NPC ruler, and is not crowned on an empty throne as the named heir or as a claimant.

## What do I get for being king

A player ruler gets a daily stipend of {{balance:KingDailyStipend}} + {{balance:KingStipendPerLevel}} x level gold, {{balance:KingCombatStrengthBonus}} times the attack damage, {{balance:KingCombatDefenseBonus}} times the defence and {{balance:KingCombatHPBonus}} times the maximum HP. At level 40 the stipend is 4,500 gold a day.

## Powers and duties of the ruler

The ruler's menu adds these actions:

- **Prison cells:** imprison, pardon, execute or set bail. A ruler can imprison {{balance:DailyPrisonSentences}} characters and execute {{balance:DailyExecutions}} a day. An NPC can be jailed for up to 30 days. Online, a player can be jailed for 1 day at most when you type a number; an empty or invalid entry gives 7 days.
- **Orders:** open or close town establishments, make a proclamation or post a bounty paid from the treasury.
- **Security:** hire or fire guards and monster guards, and pay guard bonuses.
- **Fiscal matters:** set the citizen tax, the sales tax and the team tax, and move gold in or out of the treasury.
- **Court:** royal magic, the orphanage, a royal wedding, court politics, succession and an heir, and bodyguard mercenaries.
- **Abdicate:** type yes to give up the crown to an NPC successor.

## Treasury: tax income and daily costs

Each day the treasury gains the citizen tax: the tax rate x the taxpayers x {{balance:KingTaxIncomeMultiplier}}. Taxing everyone counts the town's NPCs, at least 45; taxing one alignment counts only the NPCs of it, at least 1. At the default rate of {{balance:DefaultTaxRateNew}} gold and 100 NPCs that is 10,000 gold a day. Sales tax, donations, pardons, loan repayments and other royal fees fill it too.

Each day it pays the guards' salaries, the monster guards' feeding, {{balance:OrphanCareCost}} gold for each orphan and {{balance:BaseCourtMaintenance}} gold of court upkeep, and moves {{balance:DailyMagicReplenishment}} gold into the magic budget, which holds at most {{balance:MaxMagicBudget}}. The treasury never goes below 0. Bodyguard mercenaries are paid from your own gold, not the treasury.

### Castle guards: how many and what they cost

A ruler hires at most {{balance:MaxNPCGuards}} NPC guards, for {{balance:GuardRecruitmentCost}} gold each from the treasury; players who join the Royal Guard fill the rest of its {{balance:MaxRoyalGuards}} places. A hired guard's daily salary is {{balance:BaseGuardSalary}} + {{balance:GuardSalaryPerGuardLevel}} x the ruler's level: 1,100 gold a day under a level 40 ruler.

Each day a guard gains 1 loyalty when the treasury covers the day's costs, 1 more after 30 days of service, and otherwise loses 5. A guard deserts at 10 loyalty or less, and at 25 or less it leaves 10 percent of the time each day. A guard bonus costs the amount for each guard, at most 1,000,000 each, and raises every guard's loyalty by the amount / 100, at most 100: 1,000 gold to each of 4 guards costs 4,000 and gives 10 loyalty.

### Monster guards: price, HP and feeding

A ruler buys at most {{balance:MaxMonsterGuards}} monster guards. Each one you already own adds 500 gold to the price of the next.

{{table:monster-guards}}

### When the treasury cannot pay

When the treasury holds less than a day's costs, every guard loses 3 more loyalty and each monster guard escapes 10 percent of the time.

### Royal magic, the orphanage and court politics

- **Royal magic,** paid from the magic budget: Bless Kingdom 1,000 (25 toward Chivalry), Detect Threats 500, Protection 2,000 (10 loyalty to every guard) and Scry 1,500 (one NPC's level, place and gold).
- **The orphanage** holds up to {{balance:MaxRoyalOrphans}} orphans. Adopting an orphan aged 3 to 12 costs 500 + 100 x the orphans already held, from the treasury, and moves you 15 toward Chivalry. A true orphan aged {{balance:OrphanCommissionAge}} or more can be commissioned for {{balance:OrphanCommissionCost}} gold as a guard (salary 300, loyalty 90, 10 toward Chivalry), as a mercenary, or as a townsperson. An adopted orphan is not a true orphan and cannot be commissioned.
- **Court politics** costs from the treasury: arresting a plotter {{balance:ArrestTrialCost}}, promoting a court member {{balance:PromoteCost}} (+10 loyalty), and a bribe {{balance:BribeBaseCost}} + 50 x (100 minus the member's loyalty). A member at 40 loyalty costs 3,500 to bribe.

## Rebellion: how many executions

A ruler's 5th execution starts a rebellion, with a warning at the 3rd and the 4th; so does closing 75 percent or more of the town's establishments. A rebellion ends the reign and flips a coin. On one side the character is deleted for good. On the other you lose all gold on hand and in the bank and all Fame, your Chivalry falls by 5,000 (so to 0), you move 500 toward Darkness and the execution count starts again.

## Factions: the Crown, the Shadows and the Faith

You can belong to one of three factions, from level 10 and in no other faction:

- **The Crown,** at the Castle: Chivalry above 500, Darkness not above 500, and Crown standing not below 0.
- **The Shadows,** in the [Dark Alley](/wiki/en/world/dark-alley/#shadows-faction): Darkness above 200, or a favor done for them. They refuse only at standing -50 or worse, and not even then with Darkness above 200.
- **The Faith,** in the [Temple](/wiki/en/world/temple/): Faith standing 100 or more.

Joining raises your standing with that faction to at least 100, then 100 more, and lowers your standing with the others. Leaving costs 500 standing with the old faction.

### Faction rank requirements

Your rank rises with the reputation you earn in your faction after you join: Crown reputation from the royal purse, Faith reputation from Church donations and blessings and good Temple offerings, Shadows reputation from dark ones, and the like. A reputation gain also moves your standing with the other two factions.

{{table:faction-ranks}}

### What does each faction give

- **The Crown:** a discount at the Weapon, Armor and Magic shops; a chance that guards step in when a grudge turns into a fight in the street; {{percent:CrownTaxExemptionRate}} percent less gold lost when you die; patrols do not stop a wanted Crown member; the Royal Armory.
- **The Shadows:** more gold when you sell at the Weapon, Armor and Magic shops; a bonus to flee chance; a discount at the Black Market, which opens to members; the informant.
- **The Faith:** longer prayer and sacrifice blessings; a chance to be saved from a killing blow in a monster fight, left at a tenth of your maximum HP, once a fight; the Inner Sanctum.

{{table:faction-bonuses}}

The Faith's listed healing discount is not applied at the Healer: the Healer charges a Faith member the same.

## Royal quests

A royal quest from an audience asks you to kill monsters, reach or clear a dungeon floor, hunt a criminal or investigate. It has a time limit, pays an advance in gold and experience when you accept and 1,000 gold per level when you complete it, and you can hold one at a time. The ruler's Quests screen shows the royal bounties, which you take at the [Quest Hall](/wiki/en/world/town/#guild-row).

See [crime and prison](/wiki/en/world/law/) for what happens to prisoners.
