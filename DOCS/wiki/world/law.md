---
title: Crime, prison and the law
path: /wiki/en/world/law/
checked: 1.3.8
sources: Scripts/Locations/PrisonLocation.cs, Scripts/Systems/PrisonActivitySystem.cs, Scripts/Systems/StreetEncounterSystem.cs, Scripts/Locations/BaseLocation.cs, Scripts/Locations/CastleLocation.cs, Scripts/Systems/QuestSystem.cs, Scripts/Locations/QuestHallLocation.cs, Scripts/Locations/PrisonWalkLocation.cs, Scripts/Systems/DailySystemManager.cs, Scripts/Systems/WorldSimulator.cs, Scripts/Locations/MagicShopLocation.cs, Scripts/Locations/ChurchLocation.cs, Scripts/Locations/HomeLocation.cs, Scripts/Locations/InnLocation.cs, Scripts/Locations/DungeonLocation.cs, Scripts/Systems/SocialInfluenceSystem.cs, Scripts/Core/NPC.cs, Scripts/Systems/CombatEngine.cs, Scripts/Core/GameConfig.cs
history: 1.3.8 | none
history: 1.3.7 | none
history: 1.3.6 | none
history: 1.3.5 | none
history: 1.3.4 | none
history: 1.3.3 | none
history: 1.3.2 | The Blood Price damage cut applies on your swings, Status names only that cut, and a jailbreak can be tried once a day.
history: 1.3.0 | An NPC you beat in an honor duel keeps its HP and stays where it is and a kill contract on it is not paid, and town honor duels share the Inn duels' daily cap of 3 wins.
history: 1.2.7 | none
history: 1.2.5 | Prison activity effects now show what each one gives (for example "+1 Strength" for pushups), and a prisoner using a screen reader gets the screen reader prison menu.
history: 1.2.4 | An enraged avenger in a murder grudge fight really has the extra HP the box shows, and the townsperson's own HP is no longer raised.
history: 1.2.3 | none
---
Crime has consequences in town. Theft and murder can be taboos for your god, and a Darkness above 100 makes you a wanted character. The ruler at the [Castle](/wiki/en/world/castle/) controls the prison.

## How much Darkness before guards arrest me

You are wanted when your Darkness is above 100. A guard patrol can stop a wanted character on Main Street and near the Castle; a Crown faction member is let pass. The patrol offers:

- **Surrender (S):** 1 day in prison and a fine of a quarter of your gold on hand plus 50.
- **Fight (F) the guard captain,** a warrior of your level + 3: win and you go free with 30 more Darkness; lose and survive and you get 3 days and 30 more Darkness.
- **Bribe (B):** 100 gold, if you carry it, and you go free.
- **Run (R):** you get away with a chance of 40 + your Dexterity percent; if caught, 2 days.

A badly failed pickpocket attempt or an undercover guard in the Dark Alley can also send you to prison. A ruler can imprison characters, within daily limits.

## What happens if I murder someone in town

After a murder in town that is not a bounty kill, 5 royal guards of your level + 5 come for you. You can surrender or fight. Win the fight and you go free with 100 more Darkness. Lose it, or surrender, and a coin is flipped: on one side you are executed, on the other you go to prison. Execution kills the character; in single-player the normal death and resurrection choices follow. Prison for murder takes half your gold on hand and lasts 2 days with no escape attempt, no bail and no demand for release.

Self-defense against a street attacker is not murder. Neither is an honor duel: the NPC you beat keeps the HP it had before the fight and stays where it is. Honor duels in town and duels at the Inn share one daily cap of 3 wins.

## Blood Price (murder weight)

A permanent killing of a townsperson adds to your Blood Price, a weight carried with your character:

- a murder in the street with no bounty behind it: {{balance:MurderWeightPerDeliberateMurder}};
- a kill by dark magic: {{balance:MurderWeightPerDarkMagicKill}};
- a Shadows contract kill: {{balance:MurderWeightPerShadowContract}}, and none at all {{percent:ShadowContractBloodPriceSkipChance}} percent of the time;
- a Crown or bounty board kill: none.

A victim the town liked (an average impression above 0.3) adds {{balance:MurderWeightLikedNPCBonus}} more. Each kill that adds weight also adds 10 Darkness per point of weight, and every companion with you loses {{balance:CompanionLossPerMurder}} loyalty.

### What the Blood Price does

- At a weight of 8 or more your basic swings deal 10 percent less damage, and at 15 or more 20 percent less.
- Rest at Home, at the Inn and in a dungeon camp heals 75 percent as much at a weight of 3 or more and half as much at 6 or more.
- Gossip about you spreads more easily while you carry any weight.
- Further effects belong to the story; see its [endings spoiler](/wiki/en/world/story/#endings-how-to-get-each-ending).

From a weight of 5 the Status screen names your Blood Price standing, and from 8 its damage cut. No shop and no healer charges more for a Blood Price.

### How do I remove the Blood Price

It falls by {{balance:MurderWeightDecayPerRealDay}} for each full real day, counted at the daily reset. At the Church, confession with a weight to carry offers blood absolution: it costs {{balance:BloodConfessionBaseCost}} + {{balance:BloodConfessionCostPerWeight}} gold per point of weight and removes up to {{balance:MurderWeightConfessionReduction}}. A weight of 6.0 costs 1,700 gold and leaves 3.5.

## Life in prison

While you have days left, every trip ends at the prison. Its menu offers:

- **W** list the prisoners and **S** your status;
- **A** activities;
- **E** escape;
- **B** pay bail;
- **P** petition the ruler for bail or a pardon;
- **D** demand release, and **O** walk out through an open cell door.

Only chat commands work from a cell. Jailed characters do not follow the normal Mental collapse path.

## What do prison activities give

You can do 3 activities a day. Each one but Shadow Boxing raises an attribute by 1 for good:

- **Pushups:** Strength.
- **Yoga:** Dexterity, and half the time Agility as well.
- **Reading:** Intelligence, and 5 mana back.
- **Meditation:** Wisdom, and a tenth of your maximum HP back.
- **Stretching:** Stamina, and a twentieth of your maximum HP back.
- **Planning:** Charisma.
- **Shadow Boxing:** 10 combat stamina back, up to 100.
- **Praying:** 10 to 20 Chivalry when your Chivalry is higher than your Darkness, otherwise 10 to 20 Darkness.

## Getting out of prison

- **Serve the sentence.** When it ends you return to Main Street with your HP raised to at least half your maximum and up to 75 Darkness removed.
- **Escape: chance to escape prison.** An attempt succeeds at 40 + Dexterity / 3 + level / 2 percent, held between 30 and 80 percent: 60 percent with Dexterity 30 at level 20. A success frees you with full HP and leaves your Darkness as it was. A failure adds a day and makes the news. You regain 1 attempt at each daily reset, up to {{balance:MaxPrisonEscapeAttempts}}; a murder convict regains none.
- **Bail.** Pay the bail set by the ruler. Petitioning an NPC ruler for bail sets it at 1,000 + 500 x your level gold. The money goes to the royal treasury.
- **Clemency.** A plea to an NPC ruler frees you at 10 + Chivalry / 50 percent, held between 5 and 40 percent (22 percent at 600 Chivalry), and you can plead again at once. A human ruler receives your request as a message.
- **An open door.** O lets you walk out when your cell door is open; no action in the game opens another player's cell door.

## Bounties

The ruler posts bounties on criminals, and a citizen can report a crime at a royal audience. An unpaid royal loan can put a bounty on you. Read the bounty board at the Quest Hall. Defeating a bounty target pays the reward, but a win in an honor duel does not pay a kill contract on the NPC; a bounty on a player goes to whoever beats them in a duel or the [Arena](/wiki/en/online/arena/). Very feared characters can also meet bounty hunters in the street.

Judicar rewards appropriate prison orders and treats theft as a taboo. See the [god pages](/wiki/en/gods/) for deeds and taboos.
