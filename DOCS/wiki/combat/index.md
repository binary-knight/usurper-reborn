---
title: Combat basics
path: /wiki/en/combat/
checked: 1.3.4
sources: Scripts/Systems/CombatEngine.cs, Scripts/Systems/StatEffectsSystem.cs, Scripts/Systems/TrainingSystem.cs, Scripts/Core/GameConfig.cs, Scripts/Core/Character.cs, Scripts/Core/Monster.cs, Scripts/Systems/DifficultySystem.cs, Scripts/Systems/MentalSystem.cs, Scripts/Systems/AlignmentSystem.cs, Scripts/Systems/GodBoonSystem.cs, Scripts/Systems/SpellSystem.cs, Scripts/Systems/DailySystemManager.cs, Scripts/Locations/DungeonLocation.cs, Scripts/Locations/InnLocation.cs, Scripts/Locations/HealerLocation.cs, Scripts/Locations/HomeLocation.cs
history: 1.3.4 | A Cyclebreaker's Probability Shift makes 30 percent of a monster's hits on you miss and turns its natural 20 into an ordinary hit, and the difficulty's monster damage now applies to Manwe's named abilities.
history: 1.3.3 | A companion or NPC ally at 0 HP is Downed, not dead: U spends a healing potion to rescue it, and a Beast, Insectoid, Shadow or Undead monster or a boss can spend its action to finish it.
history: 1.3.2 | Class combat bonuses, divine damage bonuses, the Blood Price cut and the world boss Boss Slayer bonus apply on your swings, a monster's Thorns costs a melee attacker a quarter of the damage dealt, and every party member regains stamina and mana at round end.
history: 1.3.0 | Your physical attacks on monsters roll a twenty-sided die to hit and show the roll and the number needed, and a dual-wielder's swings each use the weapon in their own hand.
history: 1.2.7 | A monster's critical hit line no longer leaves an empty gap where a damage number used to be.
history: 1.2.6 | none
history: 1.2.5 | Monster names in combat are shown in the player's language, and long attack, miss and spell rows wrap inside 79 columns.
history: 1.2.4 | In group combat each player reads the turn screen, menus and round status in their own language.
---
The numbers on this page are for Normal difficulty in a monster fight. [Difficulty](#difficulty-and-combat) changes a few of them, and [PvP](/wiki/en/combat/pvp/) has its own rules.

## Before choosing an action

Check HP, resources, enemies and status messages. An ordinary attack, ability, spell, consumable and flee attempt have different costs and restrictions.

Equipment, attributes, training, buffs, boons and Mental all contribute to results. The [balance defaults](/wiki/en/reference/balance/) are not a universal damage formula.

## Turn order: who goes first

There is no initiative roll and Agility does not decide who acts first. Each round runs in this order:

1. Your statuses tick (poison and the like, see [combat statuses](/wiki/en/characters/afflictions/#combat-statuses)), then HP regeneration from your race, shrine and alignment.
2. You act.
3. Your teammates and companions act, each ticking its own statuses first.
4. Every living monster attacks once. An Old God makes its own number of attacks.
5. Everyone in the party regains combat stamina and mana (see [regeneration in combat](#regeneration-in-combat)).

### Ambushes

When a fight starts as an ambush, each monster rolls its level x 3 plus a twenty-sided die against your party's awareness plus a twenty-sided die, and each monster that rolls higher gets one free attack before round 1. Awareness is the best base Agility in the party / 4, plus the best base Dexterity / 4, plus the highest level x 2. A Swift [elite](/wiki/en/monsters/#elites) always gets one free attack before round 1, ambush or not.

## Damage and defence

A physical attack on a monster first rolls to hit, then works out its damage; criticals have additional rules and bonuses. Defence, armor, blocks and dodges protect in different ways. A miss is not necessarily evidence that your weapon has too little power.

When you dual wield, each swing uses the weapon in its own hand and the off-hand swing deals half damage. Abilities count the main-hand weapon in full and the off-hand weapon at half.

Spells and martial abilities use their own effect definitions. Read [spells](/wiki/en/characters/spells/) and [abilities](/wiki/en/characters/abilities/) for resource costs and target behavior.

## The hit roll

Your hit chance against a monster at most 5 levels above you is 95 percent: you need {{balance:PlayerHitMinNeeded}} or more on a twenty-sided die, so only a natural 1 misses. Each physical attack you make on a monster rolls: a basic swing (each hand on its own when you dual wield), Power Attack, Precise Strike, Backstab and a physical single-target class ability. The game shows the roll and the number needed, for example "[You roll 14; you need 2 or more]".

- Against a monster up to {{balance:AtLevelAccuracyGap}} levels above you, or any level below, you need {{balance:PlayerHitMinNeeded}} or more: only a natural 1 misses.
- Against a monster more than {{balance:AtLevelAccuracyGap}} levels above you, you need between {{balance:PlayerHitMinNeeded}} and {{balance:PlayerHitMaxNeededAbove}}, so you hit 65 to 95 percent of the time.
- A natural 1 always misses and a natural 20 always hits. On a basic swing the natural 20 is the critical hit.
- Precise Strike needs {{balance:PreciseStrikeNeededBonus}} less on the die, never less than {{balance:PlayerHitMinNeeded}}: against a higher monster that needs 8 it needs 4.
- A miss deals no damage and triggers no on-hit effect.

A monster's own evasion is checked first, before the roll. A swing from hiding does not roll. Teammates and companions roll with their own stats. Spells, area abilities and fights between players do not roll.

### How the number you need is worked out

Against a monster more than {{balance:AtLevelAccuracyGap}} levels above you, the number you need is the monster's armor class minus your hit modifier, kept between {{balance:PlayerHitMinNeeded}} and {{balance:PlayerHitMaxNeededAbove}}.

- The monster's armor class is 10 + its level / 2 + its Defence / 10. It is 4 higher while you rage, 2 higher in Power Attack's stance, 6 higher while you are blinded, and 2 lower while you are Blessed or have the King's blessing.
- Your hit modifier is (Strength - 10) / 3 + (Dexterity - 10) / 4 + your basic attack [proficiency](/wiki/en/characters/leveling/) modifier (-2 at Untrained up to +7 at Legendary) + your level / 5 + your weapon power / 20. Above +6, only a third of the rest counts: a raw +15 counts as +9.

Against a monster up to {{balance:AtLevelAccuracyGap}} levels above you none of this counts: the number is always {{balance:PlayerHitMinNeeded}}.

### Monster evasion

A monster with an evasion buff makes each of your hits miss with its own chance, 25 to 55 percent for 2 rounds (Ink Cloud: 30 percent for 3 rounds). The check comes before your roll, also applies to spells and area damage, and is not changed by hiding.

## Critical hits

Your critical hit chance on a basic swing is 5 percent plus 1 percent per 10 Dexterity, plus the crit chance on your gear, at least 5 percent. The ceiling is 50 percent, plus 1 percent per 30 Dexterity, up to 75 percent: Dexterity 100 with no gear gives 15 percent with a ceiling of 53. Every other crit bonus is kept under the same ceiling.

- **Critical damage:** a Dexterity crit multiplies the swing by 1.5, plus 0.02 per point of Dexterity above 10, at most 3 times from Dexterity; your gear's crit damage percent / 100 adds on top of that. Dexterity 60 gives 2.5 times; Dexterity 85 reaches 3.
- **Natural 20:** a natural 20 on the hit die is a critical hit for a random 1.5 to 2 times, whatever your Dexterity. It replaces the Dexterity crit on that swing; the two never stack. The Dexterity crit chance is rolled only when the die did not show 20.
- **From hiding:** the first swing from Hidden is a crit at your Dexterity multiplier.
- **Valorian's Miracle:** the next swing is a crit for {{balance:MiracleCritMultiplier}} times.
- **Power Attack** crits half as often as a basic swing and never on a natural 20.

### Other crit chance bonuses

Besides Dexterity and gear: a drug's crit bonus, a player-god's crit boon, the Noctura shrine attunement (+5), the Cave Spider as active pet (+5), alignment (Dark or Good +3, Evil or Holy +5), and Umbrath's boon on a basic swing. The Creator's Eye artifact adds {{balance:CreatorsEyeCritBonus}}.

:::spoiler Spoiler: the Creator's Eye late in the story
The Creator's Eye bonus doubles in the fight with Manwe while you hold the Void Key.
:::

## Basic attack damage

A basic swing's power is Strength + Strength / 4 + your level, plus the weapon term, plus a random 1 to 20 (from level 44, 1 to level / 2 - 1), plus any temporary attack bonus from an ability, plus a Warrior's level / 5 and a Ranger's 4 on a swing with a bow. A Bard or Jester uses Charisma in place of Strength.

- **Weapon term:** with W = the weapon power of that hand + your level / 10, the swing adds W plus a random 0 to W. A weapon of 100 at level 20 adds 102 to 204.
- **Statuses** change only the Strength and level part: Rage and Power Attack's stance x1.5, Blessed + level / 5 + 2, the King's blessing x1.10, Weakened - (4 + level / 10).
- Then the swing is multiplied, in order, by your [weapon setup](#weapon-setup-two-handed-dual-wield-and-shields), your basic attack proficiency (0.50 at Untrained, 1.00 at Average, 2.00 at Legendary: see the [proficiency table](/wiki/en/characters/leveling/)), the crit, difficulty, and the Grief, Mental and Fatigue penalty, then by the King's +10 percent, your god's damage boons and a Temple blessing's damage bonus, your buffs (well-rested, herbs, food, songs, blessings, poison coating, Knight +5 percent, Grand Champion +3 percent and others), a Blood Price or divine wrath cut, a boss slayer item's +10 percent against a boss or mini-boss, and your god's boon.

### Weapon power cap

Weapon power above 800 counts at half in a swing's damage: 1,000 counts as 900. Your sheet still shows the full number. The cap also applies to the weapon power that abilities and teammates use.

### Monster armor and Defence

A monster takes your damage minus its armor power, at least 1. A basic swing and Backstab ignore its Defence; Power Attack and Precise Strike first take off its Defence plus a random 0 to Defence / 8. A sleeping monster then takes 50 percent more.

Armor piercing on your gear takes its percent off the armor or Defence a monster sets against your basic attacks, abilities and area attacks; the sum over all your gear is at most 50 percent.

### Area damage

An area attack does full damage to the first monster, 75 percent to the second, 50 percent to the third and 25 percent to the fourth and every later one, in the order the monsters stand, not the one you picked first.

## Attacks per round

You get 1 basic swing per Attack, plus:

- a Warrior's extra swings: 1 per 10 levels, at most 3;
- 1 for dual wielding;
- 1 more with 2 percent chance per point of Agility above 15, at most 40 percent, rolled once per Attack;
- the swings a Windfury Totem, the Shadow Crown artifact, drugs or herbs give.

A drug's heavy speed penalty takes 1 away. Then Haste doubles the count and Slow halves it, and the total is never more than 8. With no weapon in your main hand you get 1 swing.

When you dual wield, every swing past the first (and past a Warrior's extra swings and a Windfury swing) is an off-hand swing at half damage: an Agility extra swing and the swings Haste adds go to the off hand.

## Weapon setup: two-handed, dual wield and shields

- **Two-handed:** a two-handed weapon, and any bow, staff, maul or polearm, multiplies your swing damage by {{balance:TwoHandedDamageBonus}} and your defence against monster hits by 0.85.
- **Dual wield:** two one-handed weapons and no shield give 1 extra off-hand swing per Attack at 0.50 damage with the off-hand weapon, and multiply your defence by 0.90.
- **Shield:** a shield gives a 20 percent chance to block a monster hit that landed; a block takes off half of the damage left after defence, never below the minimum. A shield has no defence penalty.

## Tactical actions

These actions work for any class unless stated, and each one ends your turn, even when it fails; only Backstab, Smite or Rage from a class that cannot use it, and Smite with no charge left, ask again. Type the key at the combat prompt. The compact combat menu lists P, E, T, W and L; the standard menu does not list them, but the keys work there too. Every combat menu lists Backstab for an Assassin and Smite for a Paladin with a charge left; none lists Rage.

### Power Attack (P)

Power Attack costs 15 combat stamina and hits for (Strength + Strength / 4 + level + W + a random 0 to W / 2 + a random 1 to 20) x 1.75, where W is your main-hand weapon power plus half the off hand's. Then come your weapon setup, basic attack proficiency, crit and difficulty, and the monster's Defence and armor are taken off. A Bard, Jester or Wavecaller uses Charisma in place of Strength. It rolls to hit, crits half as often as a basic swing, and puts you in Power Stance for the round: the monster's armor class for you is 2 higher. With too little stamina the turn is spent and nothing happens. A dual-wielder follows with an off-hand swing.

### Precise Strike (E)

Precise Strike needs {{balance:PreciseStrikeNeededBonus}} less on the hit die and costs nothing. Its damage is Strength + weapon power + a random 1 to 14, times your weapon setup, minus the monster's Defence and armor. It never crits. It only helps against a monster more than {{balance:AtLevelAccuracyGap}} levels above you.

### Backstab (K, Assassin)

Backstab succeeds 50 percent of the time plus half your Dexterity, at most 95 percent, then rolls to hit. It deals (Strength + weapon power) x 3 + a random 1 to 19, minus the monster's armor. It never crits.

### Smite (Z, Paladin)

A Paladin has 1 + level / 10 smite charges a day. Smite spends one, then rolls to hit, and deals (Strength + weapon power) x 1.5 + your level + a random 10 to 24, minus the monster's armor. It never crits.

### Hide (L)

Hide succeeds 40 percent of the time plus half your Dexterity, at most 90 percent, and makes you Hidden for 2 rounds. Your next basic swing or Power Attack from hiding does not roll to hit and is a critical hit at your Dexterity multiplier. While Hidden, your armor class against monster attacks is 5 higher.

### Disarm (W)

Disarm succeeds 50 percent of the time plus half of (your Dexterity - the monster's Strength), at least 10 percent. Success lowers the monster's weapon power by 5 for the rest of the fight; repeats add up.

### Taunt (T)

Taunt makes the monster attack you for 2 rounds, whoever else is in the party, and lowers its Defence by 3 and its armor by 2 for the rest of the fight. It costs nothing.

### Rage (G, Barbarian)

Rage lasts 5 rounds: the Strength and level part of your basic swing and Power Attack is multiplied by 1.5, your defence against monster hits drops by 4 and the monster's armor class for you is 4 higher. You cannot rage again while raging.

### Defend (D)

Defend halves every monster basic attack on you for the rest of the round, rounded up. It does not halve a monster's special ability. While you defend, monsters are more likely to pick you over your allies (see [who monsters attack](/wiki/en/combat/groups/#who-monsters-attack)).

### Rescue (U)

An ally at 0 HP in a fight (a companion, NPC ally, spouse, lover or fighting beast) is Downed, not dead. U spends one healing potion from your belt to lift a Downed ally: it stands again with the HP your healing potion would give it, and a finish a monster announced on it is called off. With two or more allies Downed you pick which one. U is in the menu only while an ally is Downed and you hold a healing potion, and only for the player leading the fight. In an Old God fight a rescue waits for the potion cooldown and starts it, as a potion does. A rescue counts as healing an ally for a god's deeds.

## When a monster attacks you

### How often monsters hit you

A monster no more than {{balance:AtLevelAccuracyGap}} levels below you hits you 25 to 90 percent of the time: it needs {{balance:MonsterAccuracyMinNeeded}} to {{balance:MonsterAccuracyMaxNeededAtLevel}} on a twenty-sided die. A weaker monster needs at most {{balance:MonsterAccuracyMaxNeededWeak}}, so it still hits at least 10 percent of the time, and a natural 20 always hits.

The number it needs is your armor class minus its modifier. Your armor class is 10 + (Dexterity - 10) / 3 + armor power / 25 + level / 10, plus a ward spell's bonus / 25, plus 10 under Blur or Haste and 5 while Hidden, and 2 more while a living Paladin is in your party, you included; two Paladins do not add up. The monster's modifier is its level / 2 + (its Strength - 10) / 3.

### Dodge, Blur and block

A hit that landed can still be avoided, checked in this order:

1. **Probability Shift:** while a Cyclebreaker's Probability Shift is on the monster (3 rounds), 30 percent of its hits miss, and its natural 20 is an ordinary hit, not a critical.
2. **Blur:** 20 percent of hits miss.
3. **Dodge:** 5 percent plus 1 percent per 2 Agility above 10, at most 35 percent; light armor adds {{percent:LightArmorDodgeBonus}} and medium armor {{percent:MediumArmorDodgeBonus}}, for at most 45 percent. Agility 40 in light armor gives 30 percent.
4. **Shield block:** 20 percent, halves the damage.

Probability Shift, dodge, Blur and block work only against a monster's normal attack, not its special abilities.

### Monster damage

A monster's hit is its Strength + weapon power + punch, plus a random 0 to 9 (0 to 5 for a monster of level 3 or less). A boss hits 1.3 times as hard. The hit does 1.25 times when its roll beat the number needed by 5, 1.5 times when by 10, and double on a natural 20 (not under Probability Shift). A weakened monster hits for 0.70 times.

### Your defence

Your defence against a monster's hit is your Defence + a ward spell's bonus + 75 to 100 percent of your armor power, times your weapon setup (two-handed 0.85, dual wield 0.90), then Blessed +2, the King's blessing +10 percent, Rage -4, the Grief, Mental and Fatigue penalty, and your buffs: King +10 percent, Knight +5 percent, Grand Champion +3 percent and others. The hit does its damage minus your defence. A Barbarian or Mystic Shaman then takes 2 less, never below the minimum.

### Minimum damage

A monster hit that lands does at least the larger of 1, 5 percent of the hit before defence, and 0.25 percent of your maximum HP. Defend can halve it below that.

### Most damage from one hit

An ordinary monster's hit can take at most 75 percent of your maximum HP and a boss's 85 percent. In rounds 1 to {{balance:BossFirstRoundsDamageCapRounds}} of a fight a boss's hit takes at most {{percent:BossFirstRoundsDamageCapPercent}} percent. The same caps apply to monster abilities, except in Old God fights, which have rules of their own (in a spoiler on the [boss reference](/wiki/en/monsters/bosses/)). "Boss" here means floor bosses, Old Gods and the other boss fights.

### Monster special abilities

A monster with abilities uses one on 30 percent of its turns plus 1 percent per 5 levels: a level 50 monster 40 percent of the time. An Old God uses one 70 percent of the time. When every ability would be wasted it makes its normal attack instead ([how monsters choose](/wiki/en/monsters/#how-monsters-fight)).

An ability's direct damage is reduced by your Defence + half your armor power + a ward spell's bonus, with the same minimum and caps as a normal hit. Defend, dodge, Blur and a shield do not help against it.

## Life steal and thorns

- **Life steal:** life steal from gear counts at most {{balance:MaxEquipmentLifeStealPercent}} percent of a hit, and all life steal together never heals more than {{balance:MaxTotalLifestealPercent}} percent of the damage dealt.
- **Thorns:** thorns on your gear return their summed percent of the damage a monster's normal attack did to you, at least 1. The Reflecting status and the Scales of Law artifact (15 percent) work the same way.

## Regeneration in combat

At the end of each round every living member of the party regenerates combat stamina and, for a class with mana, mana:

- **Mana:** 2 plus a tenth of Wisdom, at most 15 a round (Wisdom 130). Arcanus's boon adds to it. A Mystic Shaman regains mana this way too.
- **Combat stamina:** 3 plus a fifteenth of Stamina, plus 2 in light armor or with no armor, or 1 in medium armor. Combat stamina is full at the start of every fight; mana is not.

{{table:combat-regeneration}}

Only you, the fight's leader, also get these:

- **Mana Regen gear:** its summed value in mana every round, on top of Wisdom and not limited to 15 or to spell classes.
- **HP Regen gear:** its summed value in HP every round.
- **Troll:** 2 percent of maximum HP a round, at least 3.
- **Terravok shrine attunement:** 5 HP a round.
- **Alignment:** Good 2 percent and Holy 4 percent of maximum HP a round.
- **Tidesworn:** 3 percent of maximum HP a round, 5 percent below half HP.

The Regenerating status heals 1 to 6 HP a round for whoever has it. There is no other HP regeneration in a fight.

### Mana outside a fight

Mana does not come back over time, except 5 percent of maximum mana per game hour with the Tidepool Sprite as active pet in single-player. A dungeon camp restores a quarter of maximum mana, a sanctuary a third, and a table rest at the Inn half of the mana you are missing. In single-player a night's sleep at the Inn or at Home restores all of it. A heavy Blood Price lowers what camp, the Inn and Home restore. The Healer refills your mana to full at no charge. See [rest](/wiki/en/world/rest/).

## Difficulty and combat

Your damage is multiplied by 1.15 on Easy, 1.0 on Normal and Hard and 0.9 on Nightmare. Monster damage is multiplied by 0.75 on Easy, 1.0 on Normal, 1.25 on Hard and 1.5 on Nightmare. On Nightmare you cannot flee and death is permanent. Online characters play on Normal.

## Fatigue

In single-player each fight adds {{balance:FatigueCostCombat}} Fatigue, 2 in light armor. At {{balance:FatigueTiredThreshold}} Fatigue you are Tired: 5 percent less damage and defence. At {{balance:FatigueExhaustedThreshold}} you are Exhausted: 10 percent less damage and defence and 10 percent less experience from kills. Fatigue counts only on basic swings and against monster normal attacks, and together with Mental it never takes more than {{percent:MentalFatigueCombatCap}} percent unless you are Broken.

Rest removes it: {{balance:FatigueReductionHomeRest}} at a Home rest, {{balance:FatigueReductionInnRest}} at an Inn table rest, {{balance:FatigueReductionDungeonRest}} at a dungeon rest, and all of it at the new day. Online there is no Fatigue.

## Rank bonuses

As King your basic swings deal 10 percent more damage, your defence is 10 percent higher and your maximum HP 5 percent higher. A Knight deals {{percent:KnightDamageBonus}} percent more damage with basic swings and has 5 percent more defence; a Grand Champion of the Gauntlet {{percent:GrandChampionDamageBonus}} percent more of each. Spells, abilities, PvP and teammates do not get these.

## When a fight goes wrong

Recover before resources are exhausted. [Fleeing](/wiki/en/combat/fleeing/) can fail and can carry penalties. Low Mental can cost your first action through fear; [treat Mental](/wiki/en/characters/mental/) instead of trying to heal that condition with HP potions. [Death and resurrection](/wiki/en/combat/death/) explains what happens when your HP reaches 0.

## Fight types

[Groups and support](/wiki/en/combat/groups/), [party and allies](/wiki/en/combat/party/), [companions and beasts](/wiki/en/combat/companions/), [PvP](/wiki/en/combat/pvp/), [death](/wiki/en/combat/death/) and [bosses](/wiki/en/monsters/bosses/) have rules that differ from an ordinary solo monster fight.
