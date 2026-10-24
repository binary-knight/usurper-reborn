---
title: Favor, prayer and sacrifice
path: /wiki/en/gods/favor/
checked: 1.3.7
sources: Scripts/Systems/FaithSystem.cs, Scripts/Systems/GodBoonSystem.cs, Scripts/Systems/GodDeedSystem.cs, Scripts/Systems/DivineBlessingSystem.cs, Scripts/Locations/TempleLocation.cs, Scripts/Systems/GodSystem.cs, Scripts/Systems/FactionSystem.cs, Scripts/Systems/CombatEngine.cs, Scripts/Systems/MiracleSystem.cs, Scripts/Core/GameConfig.cs
history: 1.3.7 | none
history: 1.3.6 | none
history: 1.3.5 | none
history: 1.3.4 | none
history: 1.3.3 | Rescuing a Downed ally counts as healing an ally, a deed Amara likes.
history: 1.3.2 | A Temple blessing's damage bonus, from daily prayer or a gold sacrifice, applies on your basic swings, and divine wrath cuts your damage and defense by the percent the screen states for the whole fight.
---
Favor is your relationship with the god you worship: a number from {{balance:GodFavorMin}} to {{balance:GodFavorMax}}, saved with your character. Your tier sets how much of your god's boon you receive, and it changes the moment you cross a tier.

{{table:favor}}

## How to gain Favor

First choose a god at the Temple; the [gods and faith](/wiki/en/gods/#choose-a-god-at-the-temple) page gives the steps. Favor comes from four sources, each with its own daily cap:

- **Daily prayer:** {{balance:GodFavorPrayerGain}} Favor, once a day, with Y in the Nave.
- **Gold offerings:** up to {{balance:GodFavorGoldDailyCap}} Favor a day.
- **Item offerings:** up to {{balance:GodFavorItemDailyCap}} Favor a day.
- **Deeds your god likes:** up to {{balance:GodFavorDeedDailyCap}} Favor a day together.

Taboos lose Favor without using any daily allowance. The daily allowances are saved, so a reload cannot repeat them.

### Gold offering: how much Favor for gold

A gold offering to your own god gives 1 Favor for each (your level x {{balance:GodFavorGoldPerLevel}}) gold, at most {{balance:GodFavorGoldDailyCap}} Favor a day. At level 10, 1,000 gold buys 1 Favor and 5,000 gold reaches the day's cap. Gold you give beyond the cap still goes to the god but buys no more Favor that day.

### Item offering: how much Favor for an item

An item offering is worth the Favor its resale value, half its price, would buy as gold, never less than {{balance:GodFavorItemMin}} and never more than {{balance:GodFavorItemMax}}. At level 10 an item priced 6,000 gold resells for 3,000, which is 3 Favor. The item is taken from the slot you offer and is gone. Cursed and unique items are refused.

### Deeds and taboos

A deed your god likes gives {{balance:GodDeedMinor}} Favor for a common act and {{balance:GodDeedMajor}} for a rarer one, such as a bounty or a spell learned; a desecration pleases the dark gods for {{balance:GodDeedDesecration}}. A taboo takes {{balance:GodTabooMinor}}, {{balance:GodTabooMajor}} or {{balance:GodTabooGrave}} Favor by its weight. Each god's own list is on its page, for example [Arcanus](/wiki/en/gods/arcanus/), whose followers break a major taboo after {{balance:GodTabooNoCastDays}} daily resets without casting a spell.

## Do I lose Favor if I stop praying

Yes. After more than {{balance:GodNeglectGraceDays}} daily resets in a row without devotion, each daily reset takes {{balance:GodNeglectDailyLoss}} Favor. Devotion is a prayer, a gold or item offering to your god, or a deed your god likes; any one of them starts the count again.

## The daily prayer blessing

Daily Temple prayer gives {{balance:GodFavorPrayerGain}} Favor and, from a Temple god, a timed blessing on top of it. It lasts 120 real minutes, {{balance:GodPrayerBlessingZealotMultiplier}} times as long at Zealot and Chosen, and a member of the Faith faction has it longer again (see [factions](/wiki/en/world/castle/#factions-the-crown-the-shadows-and-the-faith)). While it lasts:

- the damage of each monster hit on you is cut by 3 + 5 x the god's power percent, rounded down;
- your experience from a won monster fight rises by 5 + 10 x the god's power percent, rounded down.

A god's power is its divine experience on a scale of 0.1 to 1.0: the logarithm (base 10) of its experience divided by 7. A Temple god starts with 5,000 to 20,000 experience, a power of about 0.53 to 0.61, so its blessing cuts 5 or 6 percent of the damage and adds 10 or 11 percent experience. The blessing also carries a damage bonus of the same size as the cut on your basic swings.

Prayer to a player-god takes the larger of the damage and defense percent of that god's configured Favors, doubles it, and gives it as a blessing that raises both your attack and your defense by that much for {{balance:GodPrayerBlessingCombats}} fights, twice as many at Zealot and Chosen. Favors of 10 percent damage give a 20 percent blessing. A god with no damage or defense Favor gives no blessing. See [player-gods](/wiki/en/gods/player-gods/#favors).

### The gold sacrifice blessing

A gold offering of 100 gold or more to your own Temple god also gives a blessing. Its strength is the logarithm (base 10) of the gold divided by 2, from 0.5 to 3, and it lasts 30 + 15 x that strength minutes, longer for a Faith member. What it gives depends on the god:

- **Solarius, Valorian, Amara, Judicar, Terran and Sylvana:** the damage of monster hits on you is cut by 5 + strength x god's power x 10 percent.
- **Arcanus:** experience from won fights rises by 10 + strength x god's power x 15 percent.
- **Umbrath, Mortis and Discordia:** a damage bonus of 5 + strength x god's power x 10 percent on your basic swings.

1,000 gold has a strength of 1.5: to a good god of power 0.5 it gives 12 percent less damage for 52 minutes. You hold one blessing at a time: a sacrifice blessing replaces an active prayer blessing, and a prayer replaces a sacrifice blessing.

## Offering gold to another god

Gold you give to a god other than your own buys no Favor and records divine wrath against you: level 1 when the other god is on the same side as yours, level 2 when it is on the opposite side, and level 3 for 500 gold or more to the opposite side. A god is on the dark side when its Darkness is above its Goodness: Umbrath, Mortis and Discordia. What divine wrath does is on [gods and faith](/wiki/en/gods/#divine-wrath-in-the-dungeon).

## Mental wards

From Devout, your domain protects one kind of Mental loss. A ward cuts that loss by {{balance:GodWardLossCutPct}} percent; Discordia's cuts the chance of fear by {{balance:GodWardLossCutPct}} percent instead, and Sylvana's cuts dungeon strain by {{balance:GodWardSylvanaStrainCutPct}} percent. Mortis guards two kinds. A ward does not protect against every kind of harm; the god pages list each ward.

## Miracles

Chosen, from {{balance:GodFavorTierChosenMin}} Favor, unlocks one Miracle a day. The combat key appears when the Miracle can help. The domains give healing, mana, control, a certain critical hit, an escape, a spirit ally or a death save; [daily Miracles](/wiki/en/gods/miracles/) gives each one's numbers. Miracles work in monster and story boss fights, not player fights or world boss fights. Saving and reloading does not restore a used Miracle.

## Switching gods

Leaving a god discards all your Favor with it; the new god starts at 0. Leaving with Favor brings wrath or a player-god's smite, by the Favor lost, as [gods and faith](/wiki/en/gods/#switch-or-leave-your-god) explains. The Temple shows the cost and asks before switching.

See [Temple rooms](/wiki/en/world/temple/) and [player-gods](/wiki/en/gods/player-gods/).
