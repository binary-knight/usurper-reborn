---
title: Martial and class abilities
path: /wiki/en/characters/abilities/
checked: 1.2.7
sources: Scripts/Systems/ClassAbilitySystem.cs, Scripts/Systems/CombatEngine.cs, Scripts/Core/GameConfig.cs
---
Abilities belong to particular classes and can require a weapon type, a shield or a valid target. Costs below are template costs; a description does not override an equipment requirement checked by the game.

Read the combat menu before choosing a target. Healing an ally and attacking a foe are different actions, and not every ability supports either target.

{{table:abilities}}

The listed required level is an availability condition, not a promise that the action is usable without enough stamina or mana. Abilities that need level 50 or more cost 25 percent more stamina than the listed cost, and those that need level 75 or more cost 40 percent more. See [combat basics](/wiki/en/combat/) and the [combat stamina rules](/wiki/en/characters/stats/#combat-stamina).

## How abilities scale

An ability's damage, healing and bonuses grow from its listed base value. Two multipliers apply:

- Level scale: 1 plus 3 percent per character level, so level 20 gives 1.6 times.
- Stat scale: 1 plus a share for each point of the governing stats above 10, never more than 4.5 times.

Which stats count depends on the ability's type, shown on each ability page:

- Attack abilities: Strength (4 percent a point) and Dexterity (1.5 percent). Bard and Jester attacks use Charisma (4 percent) and Dexterity (2 percent) instead. Alchemist attacks use Intelligence (4 percent) and Dexterity (2 percent).
- Healing abilities: Constitution (3 percent) and Wisdom (2 percent).
- Defense abilities: Constitution (2.5 percent). A Paladin also adds Wisdom (2 percent).
- Buff abilities: Charisma (2.5 percent).
- Debuff abilities: Intelligence (3 percent).

Damage and healing use both multipliers. A damaging ability also adds a quarter of your weapon power to its base damage before scaling. An attack bonus uses both multipliers only on a Buff ability; on any other type it uses the level scale alone. A defense bonus uses both multipliers only on a Defense ability, where an equipped shield also raises it by the shield's block chance in percent; on any other type it uses the level scale alone.

Damage and healing then vary by up to 10 percent either way, and your training proficiency in the ability multiplies them. Alchemists heal 50 percent more with abilities; Clerics, Tidesworn and Wavecallers heal 25 percent more.

## Worked example: Inspiring Tune

Inspiring Tune is a Bard Buff ability that needs an instrument. Its base values are 25 attack and 20 defense for 5 rounds.

- Attack bonus: 25 times the level scale times the Charisma scale.
- Defense bonus: 20 times the level scale only, because Inspiring Tune is a Buff, not a Defense ability.

A level 20 Bard with 50 Charisma has a level scale of 1.6 and a Charisma scale of 2.0 (1 plus 40 points at 2.5 percent). The Bard gains 80 attack and 32 defense for 5 rounds. Each living teammate gains 60 percent of each: 48 attack and 19 defense.

Bonuses do not stack. Anyone already holding an attack or defense bonus keeps whichever of the old and the new value is higher.
