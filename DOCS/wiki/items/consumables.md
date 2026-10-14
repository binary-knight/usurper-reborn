---
title: Potions, herbs and supplies
path: /wiki/en/items/consumables/
checked: 1.3.1
sources: Scripts/Locations/HealerLocation.cs, Scripts/Systems/CombatEngine.cs, Scripts/Systems/PotionBonus.cs, Scripts/Core/GameConfig.cs, Scripts/Core/Character.cs, Scripts/Locations/HomeLocation.cs, Scripts/Locations/MagicShopLocation.cs, Scripts/Locations/BaseLocation.cs, Scripts/Systems/CityControlSystem.cs, Scripts/Systems/MentalSystem.cs
history: 1.3.0 | A healing potion heals at least 20 percent of the drinker's maximum HP, and a potion given to an ally at least 20 percent of the ally's maximum HP.
history: 1.2.7 | none
history: 1.2.6 | none
history: 1.2.5 | Healer menus and the potion and herb labels are shown in all five languages.
history: 1.2.4 | none
---
Consumables solve particular problems. Healing supplies restore HP, mana supplies restore casting resources, antidotes cure poison, Willow Draught restores Mental in the dungeon and herbs give a short buff. One does not substitute for the others.

## Use the correct supply

Check the combat and room menus for available consumable actions. Boss potion cooldowns and fight context can limit repeated use. Herbs and potions can interact with healing bonuses; the final amount is not always the raw item label.

A healing potion heals a flat amount that grows with level, or 20 percent of the drinker's maximum HP when that is more. A potion given to an ally uses the ally's maximum HP.

Willow Draught stops at your ordinary Mental cap and is kept if there is nothing to restore. Addiction and Broken require their own treatment decisions.

## Healing potion price and how many you can carry

A healing potion costs {{balance:HealingPotionBaseCost}} + level squared / 2 gold before tax: 300 at level 20, 1,350 at level 50 and 5,100 at level 100. You can carry 20 + (level - 1) healing potions: 39 at level 20. The Healer (B) and the Magic Shop (H) both sell them; the Magic Shop puts its own price modifiers on the same base price.

## Mana potion

A mana potion restores 30 + level x 5 mana and costs {{balance:ManaPotionBaseCost}} + level squared / 2 gold before tax: 130 mana for 350 gold at level 20. You can carry 20 + (level - 1). Any class can buy them, to give to a teammate. The Healer (M) and the Magic Shop (M) sell them.

## Antidotes

An antidote costs 75 gold at the Healer (N) before tax and cures poison. You can carry 5 + level / 10, rounded down: 7 at level 20 and 15 at level 100.

## Willow Draught

A Willow Draught costs as much as {{balance:MentalWillowPotionMultiplier}} healing potions at your level, plus tax: 600 gold at level 20 before tax. It restores {{balance:MentalWillowDraughtGain}} Mental in the dungeon, and you can carry {{balance:MaxWillowDraughts}}. Buy it at the [Healer](/wiki/en/world/healer/) (W).

## Herbs

Herbs grow in the Herb Garden at [Home](/wiki/en/world/home/). Each Garden level unlocks the next herb and one more herb a day to gather, up to 5 a day at level 5. Use a herb with `/herb` (or `/j`) anywhere, from J at Home, or in combat.

{{table:herbs}}

Only one herb buff is active at a time; a new herb buff replaces the old one. A buff counts down by fights. An Alchemist's Healing Herb heals {{percent:AlchemistPotionMasteryBonus}} percent more (37.5 percent of maximum HP), and an Alchemist's herb buffs last half as long again: 7 fights in place of {{balance:HerbBuffDuration}}, 4 in place of {{balance:HerbSwiftDuration}}.

## Keep a reserve

Bring enough supplies to return safely, not just to win the next fight. Camp or town treatment can save consumables, but neither is available in every situation.
