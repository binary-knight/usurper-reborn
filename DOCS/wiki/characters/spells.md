---
title: Spells
path: /wiki/en/characters/spells/
checked: 1.3.0
sources: Scripts/Systems/SpellSystem.cs, Scripts/Systems/StatEffectsSystem.cs, Scripts/Core/GameConfig.cs, Scripts/Systems/SpellLearningSystem.cs, Scripts/Systems/TrainingSystem.cs, Scripts/Systems/CharacterCreationSystem.cs, Scripts/Locations/LevelMasterLocation.cs, Scripts/Locations/MagicShopLocation.cs
history: 1.3.1 | none
history: 1.3.0 | The spell table shows the level at which each spell can be learned and cast and the mana a cast costs before Wisdom, and the Cure Critical and Judgment descriptions say that a cast removes Darkness first.
history: 1.2.5 | Spell cast messages are shown in all five languages, and long spell messages wrap inside 79 columns.
history: 1.2.4 | none
---
Spells are class-specific definitions. The table below shows, for each spell, the character level at which you can learn and cast it, the mana a cast costs before your Wisdom lowers it, and the effect text. The spell's catalog slot is not the same as your character level.

A catalog entry is not proof that your current character has learned it. Multi-target effects, wards and status effects also follow their combat rules.

## How to learn spells

You learn a spell in the spell menu, which opens at the Level Master and at the Magic Shop. Learning costs no gold: you need only the spell's required level. A new character already knows the slot 1 spell of its class. The same menu lets you forget a spell.

{{table:spells}}

For Sage party wards and the difference between a shield and healing, read [groups and support](/wiki/en/combat/groups/).

## Level and mana

The required level in the table is the level at which you can learn and cast the spell: the higher of the level listed on the spell and the level of its catalog slot. Slots 1 to 25 open at levels 1, 4, 8, 12, 16, 20, 25, 28, 32, 36, 40, 44, 47, 50, 54, 58, 62, 66, 70, 75, 80, 85, 90, 95 and 100. For the 25-spell classes the slot level decides; for the five-spell prestige classes the listed level can be higher.

A cast costs 10 mana plus 5 per catalog slot; this is the table's mana value. Wisdom lowers that by 1.5 percent per point above 10, up to half, and a cast never costs less than 5 plus one fifth of the slot, rounded down. Ocean's Memory halves the final cost.

## Why a spell fails

A spell that is not a heal can fail, and the mana is spent either way. Two checks are made:

- A fumble, by your [proficiency](/wiki/en/characters/leveling/#proficiency) in that spell: 10 percent at Average, less at higher tiers and none at Legendary.
- A roll of a twenty-sided die plus your modifier, which must reach 8 plus the spell's catalog slot. A natural 20 always passes.

The modifier is your proficiency's roll modifier, plus one tenth of your level rounded down, plus a stat part: half of Intelligence above 10 for a Magician, half of Wisdom above 10 for a Cleric, and for any other class half of the average of Intelligence and Wisdom above 10. A level 20 Magician with 40 Intelligence at Average has plus 17, so a slot 6 spell, which needs 14, fails only by a fumble.

### Can a heal spell fail

No. A spell of the Heal type never fails, and neither do six spells that heal as part of what they do: Deluge of Sanctity, Steal Life, Prison Siphon, Devour Essence, Consume the Fallen and Cycle Rewind.

## How spells scale

Spells do not use the ability scaling, and a spell does not make the [hit roll](/wiki/en/combat/#the-hit-roll) of a physical attack; it has [its own check](/wiki/en/characters/spells/#why-a-spell-fails). Each kind of effect has its own rule, and training proficiency in the spell multiplies all of them.

- Damage: the base damage times 1 plus 3 percent per character level, times your casting stat multiplier. The casting stat is Intelligence; Wavecallers use Charisma. The multiplier is 1 plus 5 percent per point above 10 up to 85, then 1.5 percent per point above 85, and never more than 8 times. Clerics and Sages also gain 1 percent per point of Wisdom above 10, and lose 1 percent per point below 10. Magicians deal 15 percent more spell damage (Arcane Mastery). Damage varies by up to 10 percent either way.
- Spell critical hits, for damage only: the chance is half of your casting stat above 10, in percent, up to 50 percent. A critical spell does 50 percent more damage.
- Healing: the base healing times 1 plus 2.5 percent per character level, times 1 plus 1.5 percent per point of Wisdom above 10, at most 3 times. Healing varies by up to 5 percent either way. Clerics heal 25 percent more with spells (Divine Grace).
- Protection: the base protection times 1 plus 2 percent per character level, times 1 plus 1.5 percent per point of Intelligence above 10 up to 60, then 0.5 percent per point, at most 2.5 times. Wavecallers use Charisma here too.

Drugs, herbs, food and god boons can add to these results. A healer [specialization](/wiki/en/characters/specializations/) adds 20 percent to spell healing.

## Spells that move alignment

Two Cleric spells change your alignment each time you cast them. Cure Critical removes up to 10 Darkness, or adds 10 Chivalry if you have no Darkness. Judgment removes up to 30 Darkness, or adds 30 Chivalry if you have no Darkness. See [alignment](/wiki/en/characters/stats/#alignment).
