---
title: Spells
path: /wiki/en/characters/spells/
checked: 1.3.6
sources: Scripts/Systems/SpellSystem.cs, Scripts/Systems/StatEffectsSystem.cs, Scripts/Core/GameConfig.cs, Scripts/Systems/SpellLearningSystem.cs, Scripts/Systems/TrainingSystem.cs, Scripts/Systems/CharacterCreationSystem.cs, Scripts/Locations/LevelMasterLocation.cs, Scripts/Locations/MagicShopLocation.cs, Scripts/Systems/CombatEngine.cs
history: 1.3.6 | Frost Touch and Ice Storm slow a boss at most 1 round, Steal Life and Energy Drain heal half of the damage dealt, Purifying Surge cures an ally's poison and disease, Cycle Rewind and Blood Pact act only on the caster, Summon Angel and Summon Demon ask for no target and give their attack bonus, and Death Kiss and Power Word: Kill no longer kill mini bosses outright.
history: 1.3.5 | Cycle Rewind heals exactly 30 percent of the caster's maximum HP, as its text says, with no level, Wisdom or proficiency scaling.
history: 1.3.4 | Probability Shift makes 30 percent of the target's hits miss and stops its critical hits in place of confusing and slowing it, Echo of Tomorrow ignores half the target's armor, and Paradox Collapse's text names its bonus damage.
history: 1.3.3 | none
history: 1.3.2 | Prestige class spells start at Average, Purify cures a poisoned or diseased ally, and party healers no longer cast Purify as a heal.
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
- Healing: the base healing times 1 plus 2.5 percent per character level, times 1 plus 1.5 percent per point of Wisdom above 10, at most 3 times. Healing varies by up to 5 percent either way. Clerics heal 25 percent more with spells (Divine Grace). Cycle Rewind is the exception: it heals exactly 30 percent of your maximum HP, with no level, Wisdom or proficiency scaling.
- Protection: the base protection times 1 plus 2 percent per character level, times 1 plus 1.5 percent per point of Intelligence above 10 up to 60, then 0.5 percent per point, at most 2.5 times. Wavecallers use Charisma here too.

Frost Touch slows its target, and Ice Storm every enemy still standing, for 2 rounds. A boss or champion shrugs the slow off {{balance:BossSoftControlResistPercent}} percent of the time and is otherwise slowed 1 round. The slow counts down once a round, and a slowed enemy loses its action half the time.

Steal Life and Energy Drain heal you half of the HP the target lost to the hit, after its armor, a boss's protections and its remaining HP.

Drugs, herbs, food and god boons can add to these results. A healer [specialization](/wiki/en/characters/specializations/) adds 20 percent to spell healing.

## Casting on an ally

A single-target heal or buff spell can be aimed at an ally and lands on that ally. A spell that cures, Purify or Purifying Surge, also removes the ally's Poisoned and Diseased statuses and trap poison. Cycle Rewind and Blood Pact act only on the caster: no ally can be chosen for them, and the cast always lands on the one who casts it. Summon Angel and Summon Demon are cast on yourself too: no target is asked for, the attack bonus lasts the whole fight, and each makes you dodge the next attack.

Deluge of Sanctity and Unmaking each have a cooldown in a fight. It counts down once a round for every party member who cast one, a grouped player or an NPC ally as well as you.

## Spells that move alignment

Two Cleric spells change your alignment each time you cast them. Cure Critical removes up to 10 Darkness, or adds 10 Chivalry if you have no Darkness. Judgment removes up to 30 Darkness, or adds 30 Chivalry if you have no Darkness. See [alignment](/wiki/en/characters/stats/#alignment).
