---
title: Stats and the character sheet
path: /wiki/en/characters/stats/
checked: 1.3.4
sources: Scripts/Systems/StatEffectsSystem.cs, Scripts/Core/Character.cs, Scripts/Systems/CharacterCreationSystem.cs, Scripts/Systems/CombatEngine.cs, Scripts/Systems/TrainingSystem.cs, Scripts/Systems/SpellSystem.cs, Scripts/Systems/HagglingEngine.cs, Scripts/Locations/LevelMasterLocation.cs, Scripts/Core/GameConfig.cs, Scripts/Systems/AwakeningBonus.cs, Scripts/Locations/BaseLocation.cs, Scripts/Systems/AlignmentSystem.cs
history: 1.3.4 | none
history: 1.3.3 | none
history: 1.3.2 | Status shows the buff block for a Bard, an Assassin and a Mystic Shaman, its Blood Price lines name only the damage cut, and each finished story companion grief adds 5 Wisdom, at most 25.
history: 1.3.1 | none
history: 1.3.0 | Physical attacks on monsters roll to hit, with Strength, Dexterity, level, weapon power and basic attack proficiency lowering the number needed against a monster more than 5 levels above you, and Status shows a dual-wielder the weapon power of each hand.
history: 1.2.6 | none
history: 1.2.5 | Equip messages and the equipment summary show item names in the player's language.
history: 1.2.4 | none
---
Each stat is read by particular actions. The lists below give what each one does in play; a stat that is not mentioned for an action has no effect on it.

## Strength, Dexterity and Agility

Strength:

- Your basic attack and Power Attack add Strength plus a quarter of Strength to your damage. Bards and Jesters use Charisma here instead, and Wavecallers use Charisma for Power Attack.
- Attack abilities of most classes grow 4 percent per point above 10 (see [abilities](/wiki/en/characters/abilities/)).
- Strength checks outside combat, such as forcing open dungeon features and arm wrestling at the Inn.
- The [hit roll](/wiki/en/combat/#the-hit-roll) of a physical attack, only against a monster more than 5 levels above you: Strength lowers the number you need.

Dexterity:

- Critical hit chance: 5 percent plus one percent per 10 Dexterity, plus equipment. The ceiling is 50 percent, raised by one percent per 30 Dexterity up to 75 percent.
- Critical damage: 1.5 times, plus 0.02 per point above 10, plus equipment, at most 3 times.
- Your armor class against monster attacks, which makes their swings miss more often.
- Fleeing a monster fight: 40 percent plus half your Dexterity plus a third of your level. A Ranger or an Assassin adds 15 and a Jester or a Bard adds 10. The chance is never more than 75 percent. Floor bosses use the same chance; an Old God fight has its own (see [fleeing an Old God](/wiki/en/combat/fleeing/#fleeing-an-old-god)).
- Backstab, Hide and Disarm success, many dungeon traps and several street and Dark Alley checks.
- A smaller share of attack ability scaling.
- The [hit roll](/wiki/en/combat/#the-hit-roll) of a physical attack, only against a monster more than 5 levels above you: Dexterity lowers the number you need.

Agility:

- Dodging a monster's hit: 5 percent plus one percent per 2 points above 10, at most 35 percent. Light armor adds 10 and medium armor 5, for at most 45 percent.
- An extra attack per round: 2 percent per point above 15, at most 40 percent, only with a weapon in your main hand.
- Defence: your sheet adds a third of your Agility above 10, and subtracts it below 10.
- Avoiding pit and other traps, and fleeing a player fight.

Agility does not decide who acts first.

## Defence

When a monster hits you, your Defence, magic armor bonus and most of your armor power are taken off the damage. A hit always does at least a small minimum, and an ordinary single hit cannot take more than 75 percent of your maximum HP (85 percent from a boss). Defence and armor reduce damage; neither makes you immune.

## Constitution, Intelligence and Wisdom

Constitution:

- Maximum HP: 3 per point above 10, plus your level times a fifteenth of your Constitution.
- Healing abilities and Defense abilities scale with it.
- The check against poison dart traps.

Constitution gives no general resistance to poison or disease, and it does not speed stamina recovery.

Intelligence:

- Maximum mana for spellcasting classes: 5 per point above 10, plus your level times an eighth of your Intelligence.
- Spell damage, spell critical chance and protection spells (see [spells](/wiki/en/characters/spells/)).
- Alchemist attack abilities and Debuff abilities scale with it.
- [Training points](/wiki/en/characters/leveling/#training-points-per-level) at each level: one more for every 40 points of Intelligence and Wisdom combined.

Wisdom:

- Maximum mana for spellcasting classes: 3 per point above 10.
- Spell mana cost: 1.5 percent cheaper per point above 10, up to half.
- Spell healing, and the spell damage of Clerics and Sages.
- Mana regained at the end of each combat round: 2 plus a tenth of your Wisdom, at most 15.
- Healing abilities scale with it, and so do a Paladin's Defense abilities.
- The checks against curse and rune traps.

Wisdom gives no general resistance to spells.

## Charisma

- Bard and Jester basic attacks and Power Attack use Charisma in place of Strength, and their attack abilities scale with it. Wavecallers cast with Charisma in place of Intelligence.
- Buff abilities, such as Inspiring Tune, scale with it.
- Haggling: the largest discount a shopkeeper accepts rises with Charisma, from 4 percent below 26 Charisma to 20 percent at 201 or more. Charisma does not lower a shop's listed price.
- Romance, marriage and relationship conversations, NPC petitions, bribes and talking your way out of street encounters.

## Combat stamina

Stamina, the stat, sets your combat stamina, the pool that pays for martial abilities.

- Maximum combat stamina: 50 plus twice your Stamina plus three times your level, plus 20 in light armor or 10 in medium armor. Heavy armor adds nothing. With no armor you count as wearing light armor.
- Combat stamina is full at the start of each fight.
- In monster fights you regain 3 plus a fifteenth of your Stamina at the end of each round, plus 2 in light armor or 1 in medium armor.

## Resources

- HP is your current health. Maximum HP is your capacity, not your current health.
- When you dual wield, Status shows the weapon power of your main hand and of your off hand. Each swing uses the weapon in its own hand.
- Mana pays for spells and some abilities. Nine classes have mana and the other eight have none: see [mana classes](/wiki/en/characters/classes/#mana-classes).

## Character creation

Creation rolls three six-sided dice for each of Strength, Stamina, Agility, Charisma, Dexterity, Wisdom, Intelligence and Constitution, and adds your class's starting value to each. Your race adds its bonus to Strength, Stamina, Defence and HP only.

- A 3d6 roll averages 10.5. A 3 or an 18 each comes up once in 216 rolls; results near the middle are far more common.
- Defence is not rolled: it is your class value plus your race bonus.
- HP is two six-sided dice, plus three times your class's HP value, plus your race's HP bonus, plus a third of your Constitution.
- A spellcasting class starts with its class mana plus a quarter of your Intelligence.
- Your sheet then adds the usual Constitution, Intelligence and Wisdom bonuses above.

You may reroll up to 5 times; the last roll must be accepted. Online, a roll in progress is kept if your connection drops and comes back with the rerolls you had left. Quick Start makes a single roll with no reroll.

### Starting gold and potions

A new character starts at level 1 with {{balance:DefaultStartingGold}} gold and {{balance:DefaultStartingHealing}} healing potions.

## Alignment

Alignment comes from your Chivalry and Darkness, each from 0 to {{balance:AlignmentCap}}. Every new character starts with both at 0; no race or class adds to either. Your deeds change them, and so do two Cleric spells (see [spells](/wiki/en/characters/spells/#spells-that-move-alignment)).

### Alignment types: how much Chivalry to be Holy

The game reads six alignment types, checked in this order:

1. **Holy:** 800 Chivalry or more with Darkness under 100.
2. **Evil:** 800 Darkness or more with Chivalry under 100.
3. **Good:** Chivalry at least 400 above Darkness.
4. **Dark:** Darkness at least 400 above Chivalry.
5. **Balanced:** both above 100 and within 100 of each other.
6. **Neutral:** anything else.

The type sets shop prices (see [alignment prices](/wiki/en/items/shops/#alignment-prices)), whether the Church and the Sanctum let you in, and these effects in a fight:

{{table:alignment-types}}

The healing and fear come from your own melee hits; the extra damage hits undead and demon monsters; the HP comes back at the end of each round while you are hurt. A fear lasts 2 rounds and never takes a boss or mini-boss.

### Why is Chivalry so slow to gain at high values

A gain shrinks as the scale it raises grows: in full below 500, 75 percent from 500, 50 percent from 700, 25 percent from 850 and 10 percent from 950, never below 1. Each gain also lowers the opposite scale by half the unscaled amount, at least 1. A deed worth 20 Chivalry at 600 Chivalry gives 15 and takes 10 Darkness. One Church donation or blessing gives at most {{balance:MaxChivalryGainPerChurchAction}} Chivalry, and one Temple offering moves you at most {{balance:MaxAlignmentGainPerTempleSacrifice}}.

### The level-up bonus by alignment

Levelling up uses a simpler reading. When Chivalry and Darkness are both 0, or their shares of the total differ by less than 20 percentage points, you count as Neutral. Otherwise the larger one counts as Good or Evil. Each level-up adds a small bonus:

- Good: 1 Wisdom, 1 Charisma and 2 maximum HP.
- Evil: 1 Strength, 1 Constitution and 1 maximum HP.
- Neutral: 1 Intelligence, 1 Dexterity and 3 maximum mana.

## Awakening boons

Each stage of the [Awakening](/wiki/en/world/story/#the-awakening) adds a lasting boon, and you keep the boons of the stages before it:

- Stage 1: {{balance:AwakeningBonusWisdom1}} Wisdom.
- Stage 2: {{percent:AwakeningBonusMana2}} percent more maximum mana.
- Stage 3: {{percent:AwakeningBonusXP3}} percent more experience from fights.
- Stage 4: {{percent:AwakeningBonusHP4}} percent more maximum HP.
- Stage 5: {{percent:AwakeningBonusDamage5}} percent more damage.
- Stage 6: {{percent:AwakeningBonusDefense6}} percent less damage taken.
- Stage 7: each of the five percentages above rises by {{percent:AwakeningBonusStage7Extra}} percentage points.

## Permanent versus temporary

Base stats, equipped stats and current resources are different. A permanent reward belongs to your base character; a gear bonus disappears when you remove that gear. Boons, drugs, status effects and combat buffs can change the current outcome.

See [leveling](/wiki/en/characters/leveling/) for growth and [Mental](/wiki/en/characters/mental/) for a separate condition that changes combat performance.
