---
title: Monsters and bosses
path: /wiki/en/monsters/
checked: 1.3.6
sources: Scripts/Data/MonsterFamilies.cs, Scripts/Systems/MonsterGenerator.cs, Scripts/Systems/MonsterAbilities.cs, Scripts/Systems/MonsterTactics.cs, Scripts/Systems/EliteAffixes.cs, Scripts/Core/Monster.cs, Scripts/Core/GameConfig.cs, Scripts/Systems/CombatEngine.cs, Scripts/Systems/DifficultySystem.cs, Scripts/Locations/DungeonLocation.cs, Scripts/Data/WildernessData.cs, Scripts/Data/GauntletChampionData.cs
history: 1.3.6 | A monster's breath and spell damage is now magical and cut by your gear's magic resistance, except an Old God's, a monster you poison no longer hits harder, and Thorns no longer reflects the Alchemist's thrown bombs or the Bard's Cutting Words.
history: 1.3.5 | The Multiattack and Time Stop messages announce the one strike these abilities make.
history: 1.3.4 | none
history: 1.3.3 | From level 25 monsters have more HP and hit harder and groups grow a little each level with no jump at level 31 or 61, monsters keep growing past level 50, and Beast, Insectoid, Shadow and Undead monsters can finish a Downed ally.
history: 1.3.2 | Thorns costs a melee attacker a quarter of the damage dealt for 3 rounds, summon, flee and drain abilities no longer land a hit, and difficulty multiplies kill experience and gold once.
history: 1.3.0 | Monsters choose their abilities with more sense and five early monsters gain a signature ability, dungeon floors 4 to 10 mix families in groups of up to three with a possible Shaman, elite monsters with named traits appear from floor 3, and the Orc is weaker except as a floor boss.
---
Ordinary monsters are organized into 15 families with 5 level tiers each. A dungeon monster is drawn from the families that have a tier covering its level, each family equally likely, and takes that family's tier for the level. The generated family pages show normal non-boss stat samples at each tier's endpoints.

{{table:monsters}}

A sample is not a fixed stat line for every encounter. Difficulty, NG+, server mods and the encounter rules below change live values.

The Orc has 90 percent of the power it had before 1.3.0; as a floor boss it keeps its full power.

## How monster HP, Strength and Defense are worked out

A monster's stats follow its level L, at every level from 1 to 100:

- HP = 50 x L + 15 x L^1.2
- Strength = 2 x L + 1.5 x L^1.05
- Defense = L + 0.5 x L^1.02

Each is multiplied by the tier's power and then by {{balance:BaseMonsterDifficultyScale}}. A level 20 monster of power 1.0 has 3,478 HP, 166 Strength and 67 Defense.

A floor boss is built from its tier's boss power where the tier has one (the Orc's is 1.0 against its 0.9), then has 2.8 times the HP, 1.25 times the Strength, punch and weapon power and 1.2 times the Defense and armor of a monster of its level and tier; the deep factors below multiply on top.

The deep floors from 25 are built for a full team of five: a tank, a healer and damage dealers. From level 25 HP is also multiplied by a deep factor that rises in straight lines from 1.0 at level 25 to 1.44 at 35, 1.605 at 50 and 1.66 at 100, and Strength, punch and weapon power by one that rises from 1.0 at 25 to 1.44 at 35, 1.605 at 50, 1.715 at 75, 1.97 at 80 and 2.31 at 100. Defense and armor are not. A level 60 monster of power 1.0 has 18,328 HP and 852 Strength.

### Monsters on the first floors

Monsters of level 1 to 6 are softened: their HP, Strength, Defense, punch and weapon power are multiplied by {{balance:Floor1MonsterStatMultiplier}} at level 1, {{balance:Floor2MonsterStatMultiplier}} at level 2, {{balance:Floor3MonsterStatMultiplier}} at level 3, {{balance:Floor4MonsterStatMultiplier}} at level 4, {{balance:Floor5MonsterStatMultiplier}} at level 5 and {{balance:Floor6MonsterStatMultiplier}} at level 6. From level 7 they are at full strength.

## Difficulty and monster HP

Monster HP is multiplied by 0.85 on Easy, 1.0 on Normal, 1.2 on Hard and 1.4 on Nightmare. Experience and gold from kills are multiplied by 1.5 on Easy, 1.0 on Normal, 0.75 on Hard and 0.5 on Nightmare, once, at the win. The damage monsters deal by difficulty is on [combat](/wiki/en/combat/#difficulty-and-combat).

## How many monsters are in a fight

Outside floors 4 to 10 a regular dungeon group holds, by the monsters' level, 1 or 2 monsters at level 1 to 10 and 1 to 3 at 11 to 24, each size equally likely. From level 25 the average group grows a little each level: 2 monsters at 25, 2.6 at 35, 3.5 at 50 and 4 from 61, never fewer than 1 or more than 5. A whole average draws three sizes equally often: 1 to 3 at 25 and 3 to 5 from 61. 70 percent of these groups are one family and one tier; otherwise each monster is drawn on its own and families can mix.

### Encounters on dungeon floors 4 to 10

On dungeon floors 4 to 10 a regular encounter holds one monster 35 percent of the time, two 45 percent and three 20 percent, never more. Half the time a group is one family; otherwise its monsters are drawn one by one and can mix families. Each monster in a group of three has 80 percent of its HP and 90 percent of its Strength, punch and weapon power. A mixed group can include one spellcaster, shown as a Shaman, for example "Kobold Shaman": one Draconic or Elemental monster casts Fireball, an Undead or Goblinoid one Spellcasting, and a Demonic one Hellfire, and the caster keeps 85 percent of its Strength. Boss rooms are not built this way. These rules are always on.

## Champions

10 percent of ordinary dungeon room fights, on every floor, are a single champion in place of a group. A champion has 2.2 times the HP, 1.3 times the Strength, punch and weapon power and 1.2 times the Defense and armor of an ordinary monster of the same tier and level. A champion encounter never holds an elite. A won fight with a champion has a {{percent:MaterialDropChanceMiniBoss}} percent chance of a [crafting material](/wiki/en/items/materials/).

A champion's name adds a word by family: Champion (Goblinoid), Revenant (Undead), Warchief (Orcish), Elder before the name (Draconic), Overlord (Demonic), Titan (Giant), Alpha before the name (Beast), Prime before the name (Elemental), Abomination (Aberration), Hive Lord (Insectoid), Prime (Construct), Archfey (Fey), Leviathan (Aquatic), Seraph (Celestial) and Nightlord (Shadow), for example "Zombie Revenant" or "Elder Drake". A tier whose name already holds King, Lord, Chieftain, Warlord, Champion, Berserker or Elder becomes "Alpha" plus its name.

## Elites

From dungeon floor 3 a regular encounter can hold one elite: {{percent:EliteChanceFloors3To10}} percent of regular encounters on floors 3 to 10, {{percent:EliteChanceFloors11To30}} percent on floors 11 to 30 and {{percent:EliteChanceFloors31Plus}} percent from floor 31. An elite has {{balance:EliteHpMultiplier}} times the HP, {{balance:EliteDamageMultiplier}} times the Strength, punch and weapon power and {{balance:EliteDefenceMultiplier}} times the Defense and armor of the monster it was, and gives {{balance:EliteExperienceMultiplier}} times the experience and {{balance:EliteGoldMultiplier}} times the gold. A group of three or more has one monster fewer when it holds an elite. The elite's trait is part of its name and is announced when the fight starts. From floor {{balance:EliteSecondAffixFloor}} an elite has two different traits.

### Elite traits

Each of the five traits is equally likely: 1 in 5 for the first trait and 1 in 4 for the second.

- **Frenzied:** the first time it falls below {{balance:FrenzyBelowHpPercent}} percent of its HP its Strength is multiplied by {{balance:FrenzyStrengthMultiplier}}, once per fight.
- **Venomous:** a normal hit that does damage poisons the target for {{balance:VenomRounds}} rounds {{balance:VenomChancePercent}} percent of the time, unless the target is already poisoned.
- **Commanding:** its living allies deal {{balance:CommandDamageMultiplier}} times damage while it lives; the elite itself gets no bonus.
- **Mending:** heals the living ally with the smallest share of its HP, if that share is below {{balance:MendBelowHpPercent}} percent, for {{balance:MendHealPercent}} percent of the ally's maximum HP in place of its own attack, then waits {{balance:MendEveryRounds}} rounds; it never heals itself.
- **Swift:** it gets one free attack before the first round.

Boss rooms and champion encounters never hold an elite. Elites are always on.

## Stragglers: weaker monsters on deep floors

On floor {{balance:StragglerMinFloor}} and deeper, {{percent:StragglerEncounterChance}} percent of ordinary room fights hold stragglers: monsters 5 to 8 levels below the floor, announced by a line. The elite chance and the floor 4 to 10 rules still follow the real floor. Boss rooms never hold stragglers.

## Monster special abilities

A monster with abilities uses one on 30 percent of its turns plus 1 percent per 5 levels: a level 50 monster 40 percent of the time. It picks evenly among its abilities that would not be wasted; the full rule and how ability damage meets your defense are on [combat](/wiki/en/combat/#monster-special-abilities).

An ability that hits, drains or ends the monster's turn replaces its normal attack; one that does none of these (an evasion buff, a heal over time) is followed by the normal attack. A hit multiplies the monster's attack power (Strength + weapon power + punch, 1.3 times for a boss); a multiplier of 1 is a normal strength hit, so a status ability with x1 also hits. A fixed direct damage replaces the hit. Most direct damage is (the monster's level x 3 + its Strength / 2) times the ability's own factor, plus 5 to 14; that damage is magical, so your gear's [magic resistance](/wiki/en/items/enchanting/#magic-resistance) cuts it, except an Old God's. A status lands at the chance shown unless you resist it. Summoned minions join the fight only up to 5 living monsters in all; a minion has 40 + 3 x the summoner's level HP and is 8 levels lower.

The table shows what one use does for a level {{balance:AbilityProbeLevel}} monster at half its HP with 166 Strength, against a target with {{balance:AbilityProbeTargetHP}} HP and {{balance:AbilityProbeTargetStrength}} Strength. A range means the number is rolled each use. "Changes when" marks an ability whose effect differs when the monster or the target is badly hurt, or when it is used again in the same fight.

{{table:monster-abilities}}

### Monster abilities that depend on the situation

- **Multiattack, Frenzy, Swarm and Time Stop** strike once; the table shows that one hit.
- **Thorns** covers the monster in thorns for 3 rounds, and the monster then makes its normal attack. While they last, each melee hit on it (a weapon swing or a melee ability, yours, a teammate's or a companion's) costs the attacker 25 percent of the damage it dealt, at least 1, but never below 1 HP. Spells, bow shots, the Ranged Attack, the Alchemist's thrown bombs and the Bard's Cutting Words are not reflected. Only the Gauntlet champion Korr Stonewarden has Thorns.
- **Possess** deals direct damage equal to half your Strength.
- **Devour** deals damage equal to your current HP when you are below a fifth of your maximum HP; **Devour Soul** does so 5 percent of the time. The most damage one hit can do still applies.
- **Phylactery** heals a third of the monster's HP once it is below a quarter, and **Rebirth** restores it fully once below a fifth, once per fight; otherwise both hit.
- **Overload** costs the monster a fifth of its maximum HP, never below 1.
- **Rally** adds the monster's level / 3 to its Strength, **Armor Harden** half its level to its armor and **Stoneskin** its level, each once per fight.

## How monsters fight

A monster leaves out an ability that would be wasted: a heal on itself while it has 40 percent of its HP or more, a status its target already has, an evasion buff while one is active, and a one-time buff it has already used. When nothing is left it makes its normal attack. In a party, Beast, Insectoid, Shadow and Undead monsters prefer a target at 35 percent of its HP or less, and Goblinoid, Fey and Demonic monsters prefer a Magician, Sage, Cleric, Alchemist, Wavecaller or Abysswarden. A taunt still comes first. These rules are always on.

From monster level {{balance:SignatureMinLevel}}, five early monsters have a signature ability: Goblin (Rally), Zombie (Enfeeble), Kobold (Poison), Spark (Burn) and Animated Armor (Stoneskin). Old God fights keep their own rules.

## Experience and gold from a kill

An ordinary monster's kill is worth 15 x level^1.5 experience plus (Strength + Defense + weapon power + armor power) / 8, at least 15. The level part alone is 474 at level 10, 1,875 at level 25, 5,303 at level 50 and 15,000 at level 100.

The experience then changes with the level gap: +5 percent per level the monster is above you, at most +25 percent, and -15 percent per level it is below you, never under 25 percent of the base. A kill is never worth less than 10 experience.

Its gold is 6 x level^1.5 + 10 to 50 up to level 50, and 2,121 + 38 per level above 50, + 10 to 50, above that, at least 10. At the win a further 0 to 50 percent of it is added. Before the random parts that is about 189 at level 10, 2,121 at level 50 and 4,021 at level 100. An elite pays the multipliers above.

Difficulty, world events, your god's boons and other bonuses then apply to the total; see [leveling](/wiki/en/characters/leveling/).

[Bosses](/wiki/en/monsters/bosses/) use separate base definitions and phases. Secret and story bosses are kept behind a disclosure, outside search and Discord excerpts.
