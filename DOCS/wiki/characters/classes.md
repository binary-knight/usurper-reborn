---
title: Classes
path: /wiki/en/characters/classes/
checked: 1.3.1
sources: Scripts/Core/GameConfig.cs, Scripts/Locations/LevelMasterLocation.cs, Scripts/Core/Character.cs, Scripts/Core/Items.cs, Scripts/Core/EquipmentEnums.cs, Scripts/Data/SpecializationData.cs, Scripts/Data/EquipmentData.cs, Scripts/Systems/CombatEngine.cs, Scripts/Systems/ClassAbilitySystem.cs, Scripts/Systems/SpellSystem.cs, Scripts/Systems/PotionBonus.cs, Scripts/Systems/LootGenerator.cs, Scripts/Systems/ShopItemGenerator.cs, Scripts/Systems/TrainingSystem.cs, Scripts/Locations/HomeLocation.cs, Scripts/Locations/TeamCornerLocation.cs
history: 1.3.1 | none
history: 1.3.0 | none
history: 1.2.7 | none
history: 1.2.6 | none
history: 1.2.5 | The class help screen at character creation and the Level Master screens are shown in all five languages.
history: 1.2.4 | none
history: 1.2.3 | none
---
Choose a class for the actions you want to take, not just its starting numbers. Compare the per-level growth and specialization options on each class page.

The catalog distinguishes starting classes from prestige classes. Starting availability still depends on race: Mystic Shaman is restricted to Troll, Orc and Gnoll. Prestige classes are unlocked for NG+ through play and are not ordinary first-character choices.

{{table:classes}}

## Growth and resources

Creation values are the class contribution before race bonuses and other character-creation rules. The generated growth table is the base class increase on a level-up; specialization, training, equipment and other rewards are separate.

The table's HP, Defence and mana per level are what one level adds to the base value for that class. They include the 5 maximum HP, 1 Defence and 1 Stamina that every class gains each level.

See [abilities](/wiki/en/characters/abilities/) and [spells](/wiki/en/characters/spells/) for actual resource costs and effects.

## Mana classes

Nine classes have mana: Cleric, Magician, Sage, Mystic Shaman, Tidesworn, Wavecaller, Cyclebreaker, Abysswarden and Voidreaver. The other eight, Alchemist, Assassin, Barbarian, Bard, Jester, Paladin, Ranger and Warrior, have 0 maximum mana and pay for abilities with [combat stamina](/wiki/en/characters/stats/#combat-stamina) only. A Mystic Shaman's totems and enchants cost mana, not stamina.

## Class roles: tank, taunt, healer and damage

The game holds no ranking of classes. It names a role only for a specialization (DPS, Tank, Healer, Utility or Debuff) and, for the five prestige classes, in one fixed list used where an NPC is looked up by role. The sections below give each class's numbers.

### Which classes can tank

Thundering Roar is the first taunt: level 20, 40 stamina, for Warrior, Paladin, Barbarian and Tidesworn. It turns every living monster onto you for 3 rounds, and each round each monster has a {{balance:SoftTauntStickChance}} percent chance to keep to it. The level 40 taunts (Shield Wall Formation, Divine Mandate, Rage Challenge) and the Tidesworn's Undertow Stance and Abyssal Anchor hold at the same {{balance:SoftTauntStickChance}} percent. Eternal Vigil always holds.

Every class also has the basic Taunt action in a fight. It turns one monster onto you for 2 rounds, always holds, and lowers that monster's Defence by 3 and its armor power by 2.

A boss's area attack looks for a tank. If a teammate holds the boss's taunt, that teammate is the tank; if you hold it, you are. With no taunt at all, a Warrior, Paladin, Barbarian or Tidesworn player still counts as the tank. Everyone else then takes 60 percent less from the area attack and the tank takes 30 percent more. With no tank, everyone takes the full amount.

For an NPC teammate, the classes Warrior, Paladin and Barbarian count as party tanks, and so does a tank [specialization](/wiki/en/characters/specializations/#tank-specializations).

### Which classes can heal

Six specializations have the Healer role: Holy (Paladin), Restoration (Cleric), Mystic (Sage), Minstrel (Bard), Apothecary (Alchemist) and Spiritwalker (Mystic Shaman). Each adds {{percent:HealerSpecHealBonus}} percent to ability and spell healing. The game's role list for prestige classes names Wavecaller as Healer. Four classes heal more with abilities whatever their specialization: see the class passives below.

### Role numbers for each class

{{table:class-roles}}

## Class passives

A class passive is always on for that class. No level, specialization or item grants another class's passive.

### Potion Mastery (Alchemist)

Potion Mastery makes every healing potion an Alchemist uses heal {{percent:AlchemistPotionMasteryBonus}} percent more. Only the Alchemist has it.

- Abilities: an Alchemist's abilities heal {{percent:AlchemistPotionMasteryBonus}} percent more. This is added to a shield healing bonus, not multiplied by it.
- Healing Herb: it heals 37.5 percent of maximum HP for an Alchemist, not {{percent:HerbHealPercent}} percent.
- Other herbs: their effect lasts one and a half times as long, rounded down: 7 fights in place of {{balance:HerbBuffDuration}}, and Swiftthistle 4 in place of {{balance:HerbSwiftDuration}}.

### Arcane Mastery (Magician)

Arcane Mastery multiplies a Magician's spell damage by {{balance:MagicianArcaneSpellBonus}}. See [how spells scale](/wiki/en/characters/spells/#how-spells-scale).

### Divine Grace (Cleric)

Divine Grace gives a Cleric {{percent:ClericDivineGraceBonus}} percent more healing from spells and from abilities.

### Divine Resolve (Paladin)

Divine Resolve gives a Paladin {{percent:PaladinDivineResolveDamageBonus}} percent more damage with a basic attack or Power Attack against undead and demons, and a {{percent:PaladinDivineResolveStatusResist}} percent chance to resist each negative status a monster tries to apply.

### Bardic Inspiration (Bard)

After each ability a Bard uses there is a {{balance:BardInspirationChance}} percent chance of Bardic Inspiration: one random living companion or NPC teammate gains {{balance:BardInspirationAttackBonus}} attack for at least {{balance:BardInspirationDuration}} rounds. With no such teammate it does nothing.

### Trickster's Luck (Jester)

Each basic attack a Jester makes has a {{balance:JesterTrickstersLuckChance}} percent chance of Trickster's Luck. One of three results follows, each as likely as the others: {{percent:JesterLuckBonusDamage}} percent more attack power on that swing, a dodge of the next attack against you, or {{balance:JesterLuckStaminaRefund}} combat stamina back.

### Lethal Precision (Assassin)

Lethal Precision works on an Assassin's basic attacks. A critical hit with a dagger in the main hand adds {{percent:AssassinLethalPrecisionCritBonus}} percent of the swing's attack power, and a swing at a poisoned monster adds {{percent:AssassinLethalPrecisionPoisonBonus}} percent.

### Elemental Mastery (Mystic Shaman)

A Mystic Shaman's weapon enchant adds a share of the weapon power of the hitting hand to each hit: {{percent:ShamanEnchantBaseDamage}} percent plus {{percent:ShamanElementalMastery}} percent per point of Intelligence, never more than {{balance:ShamanEnchantPowerCap}} percent. With 80 Intelligence the share is 100 percent. An enchant lasts {{balance:ShamanEnchantDuration}} rounds and a totem {{balance:ShamanTotemBaseDuration}} rounds. See [totems](/wiki/en/characters/abilities/#mystic-shaman-totems).

### Ocean's Blessing and Ocean's Resilience (Tidesworn)

Ocean's Blessing adds {{percent:TideswornOceansBlessingBonus}} percent to the healing of a Tidesworn's abilities. With Ocean's Resilience you regain {{percent:TideswornOceansResiliencePercent}} percent of maximum HP at the end of each combat round, and {{percent:TideswornOceansResilienceBelowHalfBonus}} percentage points more while below half HP, always at least 1 HP.

### Harmonic Resonance and Ocean's Voice (Wavecaller)

Harmonic Resonance adds {{percent:WavecallerHarmonicResonanceBonus}} percent to the healing of a Wavecaller's abilities. With Ocean's Voice, while you hold an attack bonus, a basic attack or ability that did not score a critical hit gets a second roll with a {{percent:WavecallerOceansVoiceCritBonus}} percent chance to make it one. While a Wavecaller has the Reflecting status, a monster that damages it takes {{percent:WavecallerReflectionPercent}} percent of that damage back.

### Probability Manipulation and Cycle Memory (Cyclebreaker)

Probability Manipulation gives a Cyclebreaker a chance of {{balance:CyclebreakerPassiveEvadePercent}} percent to evade each monster attack aimed at it, rolled for every attack, and a chance of {{percent:CyclebreakerDebuffResistChance}} percent to resist each debuff. Cycle Memory adds {{percent:CyclebreakerCycleXPBonus}} percent to the experience of monster kills for each cycle you have completed, at most {{percent:CyclebreakerCycleXPBonusCap}} percent. On the first cycle it adds nothing.

### Abyssal Siphon, Prison Warden's Resilience and Corruption Harvest (Abysswarden)

Abyssal Siphon heals an Abysswarden for {{percent:AbysswardenAbyssalSiphonPercent}} percent of the damage of each hit, at least 1 HP, inside the game's limit on life steal. With Prison Warden's Resilience a monster's hit does {{percent:AbysswardenPrisonWardResist}} percent less damage. Corruption Harvest heals {{percent:AbysswardenCorruptionHealPercent}} percent of maximum HP for each monster that was poisoned when it died, paid when the fight is won.

### Void Hunger, Soul Eater and Pain Threshold (Voidreaver)

With Void Hunger every monster killed heals a Voidreaver for {{percent:VoidreaverVoidHungerPercent}} percent of maximum HP, and with Soul Eater it also restores {{percent:VoidreaverSoulEaterManaPercent}} percent of maximum mana. Both are paid when the fight is won. Pain Threshold makes abilities deal {{percent:VoidreaverPainThresholdBonus}} percent more damage while you are below half HP. While a Voidreaver has the Reflecting status, a monster that damages it takes {{percent:VoidreaverReflectionPercent}} percent of that damage back.

### Class bonus to fleeing

A Ranger or an Assassin adds 15 to the chance to flee a monster fight, and a Jester or a Bard adds 10. The whole chance is under [Dexterity](/wiki/en/characters/stats/#strength-dexterity-and-agility).

### Class combat bonuses

Six classes carry a bonus in every fight. It holds for you and for a teammate or companion of that class; the Backstab, Smite and Rage keys are yours.

- **Warrior:** each basic swing adds level / 5 to its attack power: 2 at level 10, 20 at level 100.
- **Assassin:** each melee hit an Assassin lands on a monster has a 5 percent chance to poison it for 3 rounds, unless it is already poisoned. It does not come from spells, from magical abilities, from ranged abilities, or from a swing made with a bow in hand. Only an Assassin can [Backstab](/wiki/en/combat/#backstab-k-assassin).
- **Barbarian and Mystic Shaman:** a monster's normal attack does 2 less damage, never below the minimum; its special abilities do their full damage. A Barbarian can [Rage](/wiki/en/combat/#rage-g-barbarian).
- **Paladin:** while a living Paladin is in the party, monsters need 2 more on the die to hit you, and teammates and companions have 2 more Defence; two Paladins do not add up. A Paladin has 1 + level / 10 charges a day for [Smite](/wiki/en/combat/#smite-z-paladin).
- **Ranger:** a swing with a bow, a bow ability and the ranged attack each add 4 to their damage.

### Interrupting a boss's channel

Only a companion or NPC teammate tries to interrupt a boss that is channeling; you do not roll for it yourself. The teammate needs Agility plus half its Dexterity of at least {{balance:BossChannelInterruptSpeedThreshold}}. The chance is then {{percent:BossChannelInterruptChance}} percent, plus 20 percentage points for an Assassin or a Ranger and 15 for a Voidreaver.

## Equipment by class: armor weight, weapons and shields

An item can be limited by level, Strength, alignment, a class list and, for armor, its weight. See [equipment slots](/wiki/en/items/equipment-slots/) for the slots themselves.

### Armor weight

Each class has a heaviest armor weight it can wear: Light, Medium or Heavy. The table below shows it class by class, with the weight of each armor type above it.

### Can a Hobbit or Gnome wear heavy armor

Hobbits and Gnomes cannot wear Heavy armor, whatever their class. A prestige class is the exception.

### Strength for heavy armor

A Heavy armor piece with no Strength requirement of its own needs Strength of 15 plus one fifth of the item's level, rounded down. A level 50 piece needs 25.

### Weapons and shields a class can use

There is one rule that ties a weapon type to a class: a Mystic Shaman can hold only a sword, axe, mace, hammer, flail or maul in its main hand. Every other limit comes from class lists, and those bind the Mystic Shaman too. The weapon and shield templates behind shop stock and dungeon loot each carry a class list or are open to every class. The table counts, for each class and type, the templates that class can use out of all templates of the type. Bows are not counted: the templates hold one set of bows for Rangers and one for every class, and four names are in both.

The prestige classes skip every class list and the armor weight limits.

The hand-written base items, the ones with their own [item pages](/wiki/en/items/weapons/), carry no class list. Level, Strength, alignment and armor weight are all that limit them, and the Mystic Shaman's main hand rule applies to them too.

{{table:class-equipment}}

{{table:weapon-templates}}
