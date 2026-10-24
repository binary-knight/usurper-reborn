---
title: Daily Miracles
path: /wiki/en/gods/miracles/
checked: 1.3.7
sources: Scripts/Systems/MiracleSystem.cs, Scripts/Systems/CombatEngine.cs, Scripts/Data/BeastData.cs, Scripts/Core/GameConfig.cs
history: 1.3.7 | none
history: 1.3.6 | none
history: 1.3.5 | none
history: 1.3.4 | none
history: 1.3.3 | none
history: 1.3.2 | none
---
A Miracle needs the Chosen tier, {{balance:GodFavorTierChosenMin}} Favor or more, and you have one use per daily reset. The combat menu shows the Miracle key only when it would help. Each grouped player has their own use. Saving and reloading does not restore a spent Miracle.

## How much Favor for a Miracle

Chosen starts at {{balance:GodFavorTierChosenMin}} Favor (see the [Favor table](/wiki/en/gods/favor/)). Below it the Temple's Altars screen shows the Miracle as locked. The use comes back at the daily reset, used or not.

## Healing and resources

- **Amara:** you and every living ally are healed to full HP. Offered when someone in the party is hurt.
- **Terran:** your HP is restored to full. Offered when you are hurt.
- **Arcanus:** your mana is refilled. Offered when your mana is not full.

## Offense, control and escape

- **Solarius:** an ordinary undead or demon is banished: it dies at once. A boss, mini-boss or Old God is not banished; it takes {{balance:MiracleBanishBossDamagePct}} percent of its maximum HP as damage instead. Offered only when you fight a living undead or demon.
- **Valorian:** you make an attack whose first swing is a certain critical hit for {{balance:MiracleCritMultiplier}} times the damage.
- **Judicar:** binds the foe you pick, or the one with the most HP, for {{balance:MiracleBindRounds}} rounds; a boss can resist under the usual hold rules.
- **Discordia:** confuses every living foe for {{balance:MiracleConfuseRounds}} rounds, with the usual boss limits of Mass Confusion.
- **Umbrath:** you vanish from the fight without the ordinary flee penalties: no Fame, Mental or Favor is lost. Offered only when the fight can be fled.
- **Sylvana:** a spirit wolf, a dire wolf of your level, joins you for that fight only.

## Mortis and death protection

Mortis acts by itself: when you would die in a monster fight and the day's Miracle is unused, you are left at 1 HP instead and the fight ends, as a win if no monster is left standing and otherwise as an escape. A grouped follower who worships Mortis is kept in the fight at 1 HP. It does not act in arrest or exhibition fights, or on a death from a Mental collapse.

## Where Miracles do not work

Miracles are not available in player fights or world boss fights. Do not plan those encounters around a Miracle key that belongs to another combat context.

A Miracle does not replace supplies or [Mental treatment](/wiki/en/characters/mental/). Its daily use must be available, your Favor must meet the tier, and its effect must be useful in the current fight.
