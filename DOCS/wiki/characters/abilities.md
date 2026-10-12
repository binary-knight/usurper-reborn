---
title: Martial and class abilities
path: /wiki/en/characters/abilities/
checked: 1.3.0
sources: Scripts/Systems/ClassAbilitySystem.cs, Scripts/Systems/CombatEngine.cs, Scripts/Core/GameConfig.cs, Scripts/Locations/LevelMasterLocation.cs
history: 1.3.0 | Physical single-target attack abilities roll to hit against monsters, and a dual-wielder's abilities count the main-hand weapon in full and the off-hand weapon at half.
---
Abilities belong to particular classes and can require a weapon type, a shield or a valid target. Costs below are template costs; a description does not override an equipment requirement checked by the game.

Read the combat menu before choosing a target. Healing an ally and attacking a foe are different actions, and not every ability supports either target.

{{table:abilities}}

The table also shows each ability's cooldown in rounds and the base values its template holds: damage, healing, attack bonus, defense bonus and duration.

## How to learn abilities

Abilities are not bought or learned one by one. Every ability of your class becomes available at its required level. In a fight you use them from a quickbar of 9 slots that abilities and spells share; arrange it under Abilities at the Level Master.

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

Damage and healing use both multipliers. A damaging ability also adds a quarter of your weapon power to its base damage before scaling. When you dual wield, that weapon power counts the main-hand weapon in full and the off-hand weapon at half. An attack bonus uses both multipliers only on a Buff ability; on any other type it uses the level scale alone. A defense bonus uses both multipliers only on a Defense ability, where an equipped shield also raises it by the shield's block chance in percent; on any other type it uses the level scale alone.

Damage and healing then vary by up to 10 percent either way, and your training proficiency in the ability multiplies them. Alchemists heal 50 percent more with abilities; Clerics, Tidesworn and Wavecallers heal 25 percent more.

## The hit roll

Against a monster, an Attack ability that deals physical damage to one target rolls to hit, as a basic swing does: see [the hit roll](/wiki/en/combat/#the-hit-roll). Area abilities, magical abilities, abilities that work on the mind and an ability that promises a certain hit do not roll. An ability used from hiding does not roll either.

A missed ability does nothing: no damage, no healing, no bonus and no special effect. When you dual wield, the off-hand swing that follows an ability rolls on its own, also after a miss.

## Worked example: Inspiring Tune

Inspiring Tune is a Bard Buff ability that needs an instrument. Its base values are 25 attack and 20 defense for 5 rounds.

- Attack bonus: 25 times the level scale times the Charisma scale.
- Defense bonus: 20 times the level scale only, because Inspiring Tune is a Buff, not a Defense ability.

A level 20 Bard with 50 Charisma has a level scale of 1.6 and a Charisma scale of 2.0 (1 plus 40 points at 2.5 percent). The Bard gains 80 attack and 32 defense for 5 rounds. Each living teammate gains 60 percent of each: 48 attack and 19 defense.

Bonuses do not stack. Anyone already holding an attack or defense bonus keeps whichever of the old and the new value is higher.

## Song of Rest and other party songs

A Bard song that heals, such as Song of Rest, gives each living teammate 75 percent of the healing the Bard receives, up to the HP that teammate is missing. A song's attack and defense bonuses reach teammates at 60 percent, as in the example above.

## Calm Waters

Calm Waters is a Wavecaller ability that shields you and every living teammate against debuffs for {{balance:CalmWatersShieldDuration}} rounds. While it lasts, each debuff a monster tries to apply has a {{percent:CalmWatersResistChance}} percent chance to be resisted.

## Mystic Shaman totems

The Healing, Earthbind, Searing and Windfury totems last {{balance:ShamanTotemBaseDuration}} rounds.

- Healing Totem heals {{percent:ShamanTotemHealPercent}} percent of maximum HP each round when the Shaman fights alone. With any living teammate it heals the Shaman and each teammate {{percent:ShamanTotemHealPercentGroup}} percent each round.
- Windfury Totem gives a {{balance:ShamanWindfuryProcChance}} percent chance each round of one more attack.
- Spirit Link Totem shares out the party's HP each round, so that every living member holds the same share of its own maximum. A Shaman with no teammates is healed {{percent:ShamanSpiritLinkPercent}} percent of maximum HP instead.

The Shaman's weapon enchants are under [Elemental Mastery](/wiki/en/characters/classes/#elemental-mastery-mystic-shaman).
