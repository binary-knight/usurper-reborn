---
title: The Old Church
path: /wiki/en/world/church/
checked: 1.3.4
sources: Scripts/Locations/ChurchLocation.cs, Scripts/Systems/GodDeedSystem.cs, Scripts/Systems/AlignmentSystem.cs, Scripts/Systems/RelationshipSystem.cs, Scripts/Systems/FactionSystem.cs, Scripts/Systems/MentalSystem.cs, Scripts/Core/Character.cs, Scripts/Core/GameConfig.cs
history: 1.3.4 | none
history: 1.3.3 | none
history: 1.3.2 | Church blessing lines no longer promise a number of days.
history: 1.2.5 | Church screens and the Electron Church menu are shown in all five languages, and a long refused marriage message wraps at 79 columns.
---
The Church is distinct from the Temple. Its menu offers donations (C), purchased blessings (B), healing services, marriage, confession (F), records and speaking with the Bishop (S).

## Why can I not enter the Church

The Church refuses an Evil character, and a Dark one with more than 600 Darkness. Every other alignment may enter. The alignments are explained under [alignment](/wiki/en/characters/stats/#alignment).

## What does donating to the Church give

A donation gives Chivalry of the gold / 11, from 1 up to {{balance:MaxChivalryGainPerChurchAction}}, lowers your Darkness by 1, and gives Faith standing of the gold / 100, at least 1, with no upper limit. 275 gold gives 25 Chivalry and 2 Faith standing. A donation is not an offering to the god you worship and gives no Favor.

## How much Darkness does a Church blessing remove

A purchased blessing needs at least 1 Darkness. It gives Chivalry of the gold / 15, from 1 up to {{balance:MaxChivalryGainPerChurchAction}}, removes Darkness of the gold / 100 (at least 1, never more than you have), and gives Faith standing of the gold / 75, at least 1. 1,000 gold gives 25 Chivalry, removes 10 Darkness and gives 13 Faith standing. The blessing has no combat effect and no length: those three changes are all it does.

With no Darkness at all and at least {{balance:FreeBlessingMinRenownChivalry}} Chivalry, asking for a blessing gives one free blessing a day instead, which changes nothing in a fight either.

## How much does the Church charge to heal

The base price is 50 x your level + 100 gold, times 0.8 when your Chivalry is above your Darkness and 1.5 when your Darkness is more than twice your Chivalry, never under 50.

- Full HP: half the base price.
- Blindness, smallpox or measles: the base price.
- Plague: twice the base price.
- Leprosy, or all of them at once: three times the base price.

At level 20 with more Chivalry than Darkness the base price is 880, so full HP costs 440.

## How much does confession cost

Confession needs at least 1 Darkness and costs {{balance:ConfessionBaseCost}} + {{balance:ConfessionCostPerDarkness}} x your Darkness gold: 1,600 gold with 300 Darkness. It removes 5 to 10 Darkness, adds 2 to 5 Chivalry and restores {{balance:MentalConfessionGain}} Mental once a day; the Darkness and Chivalry come with every confession, the Mental only with the first of the day. Umbrath treats confession as a taboo. A character who carries a [Blood Price](/wiki/en/world/law/#blood-price-murder-weight) is then offered blood absolution.

## What does the Bishop do

Speaking with the Bishop gives 5 toward Chivalry 25 percent of the time when your Chivalry is more than twice your Darkness. When your Darkness is more than twice your Chivalry he offers confession 30 percent of the time. There is no daily limit.

## How much does a wedding cost

A Church wedding costs {{balance:MarriageCost}} gold for a standard ceremony, 2,000 for an elaborate one and 5,000 for a royal one; the dearer ceremonies give nothing more. A wedding moves you 10 toward Chivalry and gives 5 Charisma for good. Both of you must be 18 or older and unmarried, you must have been intimate at least once, both must be in love, you must have been together for 7 game days, and the NPC must say yes. If the marriage fails, the gold is returned.

The NPC accepts with a chance of 50 percent + 20 x its Romanticism + 15 x its Commitment - 10 x (1 - Commitment) - 8 x (1 - Romanticism), plus your Charisma / 4 (at most 15) and half the days together (at most 15), held between 20 and 95 percent. Marriage can be a deed for Amara or a taboo for Discordia. See [relationships](/wiki/en/world/relationships/) and [Home and family](/wiki/en/world/home/#marriage).
