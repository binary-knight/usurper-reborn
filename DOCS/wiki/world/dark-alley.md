---
title: The Dark Alley
path: /wiki/en/world/dark-alley/
checked: 1.3.2
sources: Scripts/Locations/DarkAlleyLocation.cs, Scripts/Locations/InnLocation.cs, Scripts/Systems/FactionSystem.cs, Scripts/Systems/GodDeedSystem.cs, Scripts/Systems/MentalSystem.cs, Scripts/Systems/AlignmentSystem.cs, Scripts/Core/GameConfig.cs, Scripts/Core/Character.cs, Scripts/Systems/DailySystemManager.cs
history: 1.3.2 | Loan Shark interest is added once a day on the loan plus the interest owed.
history: 1.3.0 | The Pit's reckless side bet is labelled 2.5x, the amount it pays.
history: 1.2.5 | The Pit monster, the street mugger and the Loan Shark enforcer are stored in English and shown in each player's language.
history: 1.2.4 | In English, the Shadows standing line in the Dark Alley says "use" instead of "access".
---
The Dark Alley combines underground commerce, risky training, drugs, pickpocketing, gambling, the Pit, loans and Shadows faction services. Open it from Main Street with A once the full town menu unlocks. Access and available services depend on your character and faction standing. Martial law can close the alley, and a stranger may approach you on the way in.

## Shops

- **D Drug Palace:** drugs with strong short effects, which add Darkness.
- **S Steroid Shop:** a permanent Strength and Stamina boost, a limited number of times per life.
- **O Orbs Health Club:** a full heal for gold.
- **G Groggo's Magic Services:** information, a fortune or a shadow blessing that raises Dexterity until your next rest.
- **B Bob's Beer Hut:** a cheap drink with a small random effect.
- **A Alchemist's Heaven:** a random potion effect for gold.

## Dark Alley prices

Base prices, before the alignment modifier: Steroid Shop 1,000 gold; Orbs Health Club level x 50 + 100 gold for a full heal; Groggo's information 100, fortune 250 and shadow blessing 500; Bob's beer 10; Alchemist's Heaven 300.

Dark Alley prices reverse the honest shops: Holy x1.5, Good x1.25, Neutral x1.0, Balanced x0.95, Dark x0.9, Evil x0.75. A Dark or Evil character with 250 Darkness or more pays a further x0.95, x0.90 at 450, x0.85 at 650 and x0.80 at 800. World events change these prices too, and Shadows members pay 10 percent less. The shop shows the adjustment. The alignment bands are in [shops](/wiki/en/items/shops/).

### Steroid Shop

Each purchase gives +5 Strength and +3 Stamina for good, {{balance:MaxSteroidShopPurchases}} times per character, and costs you 3 Darkness and 2 Fame.

### Alchemist's Heaven

A drink rolls one of three results: +2 Intelligence for good (at most {{balance:MaxAlchemistINTBoosts}} times per character), +20 HP, or a fizzle that adds 2 Darkness.

### Bob's Beer Hut

A beer rolls one of four results: +10 HP, 5 gold back, or nothing (two of the four).

### Drug Palace prices

Base prices: Berserker Rage 300, Stoneskin 450, Steroids 500, Ironhide 500, QuickSilver 550, Haste 600, Third Eye 650, Mana Boost 700, Dark Essence 1,500, Demon Blood 2,000. Taking a drug while another is active has a {{percent:DrugOverdoseChance}} percent overdose chance: you lose a quarter of your maximum HP (never below 1), your addiction doubles and rises 10 more, and you get no high.

## Gambling Den

C opens the Gambling Den. You can play 10 rounds a day. The smallest bet is 10 gold; the largest is the lower of a tenth of your gold and level x 500. A win pays the multiple below as the total you get back; the bet is taken when you place it.

- **Loaded Dice:** the dice lean secretly high or low, 7 rolls in 10. Each round you call or wait: call in round 1 for 3x against a check of 14, round 2 for 2x against 12, or round 3, which forces the call, for 1.4x against 10. The check is a twenty-sided die plus Wisdom / 15, and you must also call the right direction. A lost call adds 5 Darkness.
- **Three Card Monte:** pays 2.5x. The win chance is 33 percent + Dexterity / 500, at most 75 percent: 53 percent at Dexterity 100.
- **Skull and Bones:** safe pays 2x at 45 percent, risky 3x at 30 percent, reckless 5x at 15 percent. Each chance gains Wisdom / 300, at most 25 points.

The Inn has its own games: see [the Inn](/wiki/en/world/rest/).

## The Pit

T enters the Pit: 3 fights a day, all bare-knuckle, with your armor not counted. Choose a monster built at your level, which pays double its gold, or an NPC within 5 levels of you, where the winner takes 20 percent of the loser's gold, yours included. A Pit loss is an exhibition, not a real death.

Before a fight you can add a side bet of up to the lower of your gold and level x 200. It pays 1.5x, 2x or 2.5x as you choose, and any of them pays when you win the fight.

## Pickpocket chance

P uses your daily thief attempts, {{balance:DefaultThiefAttempts}} a day. You pick a target from a short list with purse hints. The success chance is 40 percent + 0.5 percent per point of Dexterity, +15 for an Assassin, at most 75 percent (Umbrath's boon adds to both). A roll in the lowest 10 percent is always a critical failure that sends you to [prison](/wiki/en/world/law/) for a day; any other miss starts a fight. Success takes 5 to 15 percent of the target's gold.

## Loan shark interest

L visits the Loan Shark. You can borrow up to level x 500 gold, one loan at a time, due in 5 days. At each daily reset the interest owed grows by {{percent:LoanSharkDailyInterest}} percent of the whole amount owed, the loan plus the interest; to clear the loan you pay both. A 1,000 gold loan needs 1,200 to clear after one day and 1,440 after two. Payments go to interest first.

When the loan is overdue, an enforcer meets you as you next enter the alley. Beat him and the loan is cleared. If you lose and live, he takes all your gold on hand and a quarter of your maximum HP (never below 1) and gives you 3 more days.

## Safe house

N is the safe house. It needs 50 Darkness and costs 50 gold. A rest there restores half your maximum HP, and half your maximum mana if your class uses mana. A guest who is not in the Shadows may be robbed of 5 to 10 percent of gold on hand: a 2 percent chance with Darkness over 300, 5 percent over 100, 15 percent with Chivalry over 200, and 8 percent otherwise. Shadows members are not robbed there and are hidden from PvP while offline.

## Shadows faction

J joins the Shadows if you are dark enough and in no other faction. Members get M, the Black Market, and I, an informant who names rich and dark townsfolk for {{balance:InformantCost}} gold. W pays tribute when your standing has fallen below zero. E lists evil deeds with their Darkness, rewards and chance of failure.

Pickpocketing, the Gambling Den, the Pit, the Loan Shark and the safe house close to you if your Shadows standing falls too low.

### Black Market prices

The Black Market sells forged papers that remove Darkness for 1,000 + level x 100 gold, poison vials for 300 + level x 20 (you can hold {{balance:MaxPoisonVials}}) and smoke bombs for 500 + level x 30. Each Shadows rank takes {{percent:BlackMarketRankDiscount}} percent off.

Gear appears there with Dread: 2 items at 250 Darkness, 3 at 450, 4 at 650 and 5 at 800, with a better lowest rarity as Dread rises. Gear costs its value x {{balance:BlackMarketGearMarkup}} x a rarity factor (Common and Uncommon 1.0, Rare 1.35, Epic 1.8, Legendary 2.5, Artifact 3.0) x the Dark Alley alignment modifier, less the rank discount. A dark character who is not in the Shadows but is let in on Dread pays 10 percent more.

## Costs beyond gold

Drug highs can raise Mental temporarily but bring a crash, tolerance and addiction. Addiction lowers the ordinary Mental cap. [Rehab](/wiki/en/world/healer/) addresses that system; buying another high does not.

Theft, violence and other actions can affect alignment, law and your god's Favor. Umbrath rewards qualifying theft; Judicar treats it as a taboo. The Darkness amounts in this guide are base amounts: from 500 Darkness up, the game scales a Darkness gain down, and each gain also takes half its base amount off your Chivalry.

This guide leaves out the alley's hidden easter eggs.
