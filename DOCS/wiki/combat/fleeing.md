---
title: Fear, retreat and fleeing
path: /wiki/en/combat/fleeing/
checked: 1.3.7
sources: Scripts/Systems/CombatEngine.cs, Scripts/Systems/MentalSystem.cs, Scripts/Systems/MiracleSystem.cs, Scripts/Systems/FactionSystem.cs, Scripts/Systems/DifficultySystem.cs, Scripts/Systems/GodDeedSystem.cs, Scripts/Core/GameConfig.cs
history: 1.3.7 | none
history: 1.3.6 | The Jester's Disappearing Act and the Sage's Escape now follow the flee rules: they fail on Nightmare and roll the Old God flee chance in an Old God fight or while a floor boss is alive.
history: 1.3.5 | none
history: 1.3.4 | A failed flee now also halves the damage of Manwe's named abilities for the rest of that round.
history: 1.3.3 | Allies still Downed when you flee stand up with half their maximum HP.
history: 1.3.2 | none
history: 1.3.0 | none
history: 1.2.7 | none
history: 1.2.6 | none
history: 1.2.5 | Fear messages and the flee and retreat labels are shown in the player's language, with monster names translated.
history: 1.2.4 | none
---
## Fear

Fear from low Mental is checked at the start of monster combat: {{balance:MentalShakenFearPct}} percent while Shaken and {{balance:MentalBreakingFearPct}} percent while Breaking or Broken. Fear makes you lose the first action. Each grouped player uses their own Mental. Your god or other support can protect against the relevant effect, but ordinary healing does not erase its cause.

## Flee chance

Your chance to flee a monster fight is 40 percent plus half your Dexterity plus a third of your level, at most 75 percent. Press R in the fight to try.

- A Ranger or an Assassin adds 15, a Jester or a Bard adds 10.
- A member of the Shadows adds 15 at rank 0, rising to 45 at the top rank.
- A god's flee boon adds its percent.
- The 75 percent ceiling holds whatever you add. Example: Dexterity 40 at level 15 gives 40 + 20 + 5 = 65 percent.

Floor bosses use this chance too.

### Fleeing an Old God

Your chance to flee an Old God fight is 20 percent. Below half HP it rises by 1.75 for each percentage point of HP you are missing under 50, to at most 60 percent: at 30 percent HP it is 55 percent. No class, faction or boon bonus applies.

### When a flee fails

A failed flee is your action for the round. For the rest of that round every monster hit on you is halved, at least 1.

### Smoke bombs

A Smoke Bomb, if you carry one, is used first when you press R and gets you out without a roll. It does not work in an Old God fight.

### Nightmare

On Nightmare difficulty you cannot flee at all, and a Smoke Bomb cannot be used either.

### Allies pulling back

Your R takes the whole party out. A Cautious ally close to death with no heal left can also try to leave a fight on its own turn, rolling this chance on its own stats; it rejoins you when the fight ends. See [pulling back from a fight](/wiki/en/combat/party/#pulling-back-from-a-fight).

## What fleeing costs

A successful flee from a monster fight costs 1 Fame and {{balance:MentalFleeLoss}} Mental (Umbrath's ward can lower the Mental loss), and Valorian counts it as a taboo: {{balance:GodTabooMinor}} Favor lost for his followers. Read the outcome messages rather than assuming escape is free.

## Divine escape

Umbrath's available Miracle can remove you from a monster fight without the ordinary flee penalties: no Fame, Mental or taboo is lost. Miracles have a daily use, require the relevant Favor tier, and are not an unlimited escape button.

See [Favor and Miracles](/wiki/en/gods/favor/) and [Mental](/wiki/en/characters/mental/) before relying on either protection.

## Escape abilities

The Jester's Disappearing Act and the Sage's Escape spell work by the flee rules on this page. On Nightmare they do not get you out and the turn is spent. In an Old God fight, or while a floor boss is among the living monsters, they succeed at the Old God chance from Fleeing an Old God; a failure is your turn, with the same failure message, and it does not halve the hits on you that round. Against other monsters they always get you out.
