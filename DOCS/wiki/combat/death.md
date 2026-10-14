---
title: Death and resurrection
path: /wiki/en/combat/death/
checked: 1.3.1
sources: Scripts/Systems/CombatEngine.cs, Scripts/Core/Character.cs, Scripts/Core/GameConfig.cs, Scripts/Systems/FactionSystem.cs, Scripts/Systems/MentalSystem.cs, Scripts/Systems/GodBoonSystem.cs, Scripts/Systems/DifficultySystem.cs, Scripts/Systems/CharacterCreationSystem.cs, Scripts/Locations/TempleLocation.cs
---
This page is about dying in a monster fight. Player fights have their own rules: see [PvP](/wiki/en/combat/pvp/). For allies who fall see [when an ally falls](/wiki/en/combat/party/#when-an-ally-falls) and [when a companion dies](/wiki/en/combat/companions/#when-a-companion-dies).

## What happens when you die

When your HP reaches 0 in a monster fight, the game first tries to save you, in this order. The first that works ends the fight with you alive.

1. A companion may take the killing blow and die in your place: see [a companion who dies for you](/wiki/en/combat/companions/#a-companion-who-dies-for-you).
2. Divine Favor, for members of the Faith (see below).
3. Last Stand, then Death's Door (see below).
4. Mortis's Miracle: see [Mortis and death protection](/wiki/en/gods/miracles/#mortis-and-death-protection).

If none of these saves you, you die. In an arrest fight or an exhibition fight (the Gauntlet, pit fights) you are left at 1 HP instead and nothing is spent.

## Last Stand: why you survived at 1 HP

Last Stand leaves you at 1 HP when a killing blow lands in a round you began above half your maximum HP. It works every round and every fight, and the fight then ends with you alive: as a win if every monster is dead, otherwise as an escape.

### Death's Door

Death's Door does the same once per fight when you began the round above a quarter of your maximum HP. It does not work in arrest or exhibition fights.

Neither works on Nightmare difficulty, in a player fight, or when the death comes from a Mental collapse. If you began the round at a quarter of your HP or less, nothing guards that round.

### Divine Favor

A member of the Faith has a {{percent:DivineFavorBaseChance}} percent chance at rank 0, rising to {{percent:DivineFavorMaxChance}} percent at the top rank, to survive a death with a tenth of maximum HP, once per fight.

## What a death costs

Every real death in a monster fight costs {{balance:MentalDeathLoss}} Mental and up to 3 Fame. Then it depends on the mode.

## Resurrect in single-player: the death menu

In single-player you choose how to come back from a menu:

- **Divine Intervention:** shown while you have resurrections left. It spends one and you come back where you fell with half your maximum HP. A new character has 3 resurrections, and in single-player you never get more.
- **Temple Resurrection:** half of the gold you carry. You wake at the Temple with three quarters of your maximum HP. Each character can use it 3 times in all, ever; it is offered even with no gold.
- **Deal with Death:** from level 5 with 500 Darkness or more. It costs 500 Darkness and permanently takes 2 to 5 points from one random stat among Strength, Defence, Stamina, Agility and Charisma, or 10 to 25 maximum HP. You come back where you fell with a quarter of your maximum HP.
- **Accept Fate:** always offered. You lose up to 5 levels (never below level 1) and the stats those levels gave, your experience is set to the start of your new level, you lose 75 percent of the gold you carry (Mortis's boon lowers this), one random equipped item is destroyed, and gear above your new level moves to your pack. You wake at the Temple with a tenth of your maximum HP.

Gold in the bank is never touched. The menu counts your deaths out of 5; a single-player character is never erased for dying, except on Nightmare.

### Nightmare

On Nightmare difficulty death is permanent: the save is deleted. There is no menu. Nightmare is a single-player difficulty; online characters play on Normal.

## Resurrect online: resurrections

Online, each death spends one resurrection automatically and you wake at the Temple with half your maximum HP. A death with no resurrections left erases the character. A new character starts with 3; the server can set this from 0 to 99.

### Getting resurrections back

- You regain 1 each time you reach a level that is a multiple of 10, up to your starting number.
- The Rite of Return at the Temple restores 1 for {{balance:RiteOfReturnBaseCost}} gold + your level squared x {{balance:RiteOfReturnCostPerLevelSquared}}: 50,000 gold at level 20. You can use it once every {{balance:RiteOfReturnCooldownHours}} hours, and not above your starting number.

A server can switch online permadeath off. Online deaths then use the single-player death menu and no character is erased.
