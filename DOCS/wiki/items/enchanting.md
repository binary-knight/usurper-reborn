---
title: Enchanting
path: /wiki/en/items/enchanting/
checked: 1.3.5
sources: Scripts/Locations/MagicShopLocation.cs, Scripts/Core/GameConfig.cs, Scripts/Systems/CombatEngine.cs, Scripts/Systems/AlignmentSystem.cs, Scripts/Systems/FactionSystem.cs, Scripts/Systems/CityControlSystem.cs, Scripts/Systems/DifficultySystem.cs, Scripts/Systems/StatisticsSystem.cs
history: 1.3.5 | Sunbane, Stonebane and Godbane are new counter enchants for weapons, Lifedrinker gives 6 percent life steal, a higher stat tier replaces the lower one on the same stat, and a failed 4th or 5th enchant destroys one random enchant whole.
history: 1.3.4 | none
history: 1.3.3 | none
history: 1.3.2 | Enchant removal refuses a cursed item without charging, and enchanting a worn item or removing its enchant leaves it worn.
---
The Magic Shop enchants equipment. Open it from Main Street with M for Merchant Row, then M, and press E. You can enchant anything you wear and any weapon, armor, shield or accessory in your backpack. There are 17 enchants; the last three, the counters, go on a weapon only.

## Enchant list and costs

{{table:enchants}}

The cost is the base price plus the per-level price times your level, and then the Magic Shop's price modifiers (below). Examples before modifiers: Minor at level 20 costs 3,000 + 20 x 100 = 5,000 gold; Godforged at level 80 costs 500,000 + 80 x 5,000 = 900,000 gold. No sales tax is added on top: you pay the quoted cost. A successful enchant adds the gold you paid to the item's value.

## Stat enchants

Minor, Standard, Greater, Superior, Mythic, Legendary and Godforged each add their bonus to one stat you choose: weapon power, Strength, Dexterity, Defence, Wisdom, armor class, Constitution, Intelligence, Charisma, Agility or Stamina. Mythic, Legendary and Godforged also take crafting materials.

## Named enchants

Divine Blessing adds +3 to Strength, Dexterity, Defence and Wisdom, and +3 weapon power and armor class. Ocean's Touch adds +6 Intelligence and +4 Wisdom, and needs Awakening level 2 or more as well as character level 20. Ward adds +20 magic resistance and +2 Defence. Predator adds +5 percent critical hit chance and +10 percent critical damage. Lifedrinker adds +6 percent life steal.

### Phoenix Fire and Frostbite

Phoenix Fire and Frostbite each add +20 weapon power and need level 60 and crafting materials. With Phoenix Fire, each hit with that weapon has a {{percent:FireEnchantProcChance}} percent chance of extra fire damage equal to {{percent:FireEnchantDamageMultiplier}} percent of the hit. With Frostbite, each hit has a {{percent:FrostEnchantProcChance}} percent chance to slow the monster for {{balance:FrostEnchantDuration}} rounds; a boss or mini boss resists that slow {{balance:BossSoftControlResistPercent}} percent of the time, and on one the slow lasts half as long, at least 1 round. Both work on weapon hits, not on spells, and only from the weapon in the hand that struck; a hit that kills the monster does not trigger them.

## Sunbane, Stonebane and Godbane

Sunbane, Stonebane and Godbane are counter enchants: Sunbane against Aurelion, Stonebane against Terravok and Godbane against Manwe. A counter adds no stat. Carried on your main hand or off hand into the fight it counters, it holds back two named abilities and lifts the divine armor that cuts your damage: Sunbane holds back Aurelion's Light's Embrace and Sacrifice, Stonebane holds back Terravok's Stone Skin and Entomb, and Godbane holds back Manwe's Word of Creation and Time Stop. In any other fight a counter does nothing.

Sunbane costs 300,000 + 3,000 per level and needs level 80. Stonebane costs 400,000 + 4,000 per level and needs level 88. Godbane costs 500,000 + 5,000 per level and needs level 95. Sunbane takes 2 Fading Starlight Dust and 2 Withered Heart Petal. Stonebane takes 2 Terravok's Heartstone and 2 Iron Judgment Link. Godbane takes 2 Eye of Manwe, 1 Heart of the Ocean, 1 Withered Heart Petal and 1 Iron Judgment Link.

A counter goes only on a weapon for the main hand or off hand, worn or in your backpack; on a shield, armor, a ring, an amulet or any other item the shop refuses it and charges nothing. A weapon carries one counter at most: a second counter, the same one or another, is refused and nothing is charged. A counter takes one of the item's enchant slots, and as a 4th or 5th enchant it can fail like any other. Stripping a weapon's enchants at the shop clears its counter as well.

## Enchant limit

An item holds at most {{balance:MaxEnchantments}} enchants, and never the same kind twice. Each stat counts as one kind across every stat tier. A higher tier on a stat the item has replaces the lower one (Minor +2 Dexterity, then Superior: +8, not +10); an equal or lower one is refused. An upgrade costs the new price, refunds nothing and keeps the enchant count, so it never fails. Each named enchant is its own kind. A cursed item cannot be enchanted; remove the curse first.

## Can enchanting fail

The 4th enchant on an item fails {{percent:FourthEnchantFailChance}} percent of the time and the 5th fails {{percent:FifthEnchantFailChance}} percent of the time; the first three never fail. The shop warns you before you confirm. On a failure the shop keeps the gold and the materials and adds no enchant. Instead one of the item's existing enchants, picked at random, is destroyed: its stats, its fire or frost effect and its name tag are taken off, its kind can be enchanted again, and the item's other enchants and stats stay as they were.

## Crafting materials for enchants

Mythic takes 1 Fading Starlight Dust. Legendary takes 1 Heart of the Ocean and 1 Shadow Silk Thread. Godforged takes 1 Eye of Manwe and 1 Terravok's Heartstone. Phoenix Fire takes 1 Crimson War Shard and 1 Fading Starlight Dust. Frostbite takes 1 Shadow Silk Thread and 1 Fading Starlight Dust. Materials are spent when you pay, before the failure roll. See [crafting materials](/wiki/en/items/materials/) for where each drops.

## Enchant removal cost

Enchant removal (W) costs 5,000 + level x 200 gold before the price modifiers: 9,000 at level 20. It works on equipment you are wearing and on enchanted items in your backpack, and returns the item to its base form; see [shops](/wiki/en/items/shops/).

## Magic Shop prices and the loyalty discount

Enchants, enchant removal, potions, accessories and the shop's spells and scrying are priced through the same modifiers: your alignment, any world event, a Crown member's discount, a city controller's 10 percent, the difficulty and the loyalty discount. The [shops guide](/wiki/en/items/shops/) lists the alignment, faction and difficulty numbers.

The loyalty discount counts the gold you have spent at the Magic Shop: 5 percent off from 10,000 gold, 10 percent from 50,000 and 15 percent from 200,000. A failed enchant does not count toward it.
