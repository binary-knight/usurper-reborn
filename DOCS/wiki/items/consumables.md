---
title: Potions, herbs and supplies
path: /wiki/en/items/consumables/
checked: 1.3.3
sources: Scripts/Locations/HealerLocation.cs, Scripts/Systems/CombatEngine.cs, Scripts/Systems/PotionBonus.cs, Scripts/Core/GameConfig.cs, Scripts/Core/Character.cs, Scripts/Locations/HomeLocation.cs, Scripts/Locations/MagicShopLocation.cs, Scripts/Locations/BaseLocation.cs, Scripts/Systems/CityControlSystem.cs, Scripts/Systems/MentalSystem.cs, Scripts/Systems/DifficultySystem.cs, Scripts/Systems/MaintenanceSystem.cs, Scripts/Locations/DungeonLocation.cs, Scripts/Systems/OldGodBossSystem.cs
history: 1.3.3 | U spends one healing potion to rescue a Downed ally, and in an Old God fight a rescue starts the potion cooldown.
history: 1.3.2 | The difficulty's healing multiplier applies to every healing potion you or your party drink, and a Mystic Shaman drinks a mana potion automatically like the other mana classes.
history: 1.3.0 | A healing potion heals at least 20 percent of the drinker's maximum HP, and a potion given to an ally at least 20 percent of the ally's maximum HP.
history: 1.2.7 | none
history: 1.2.6 | none
history: 1.2.5 | Healer menus and the potion and herb labels are shown in all five languages.
history: 1.2.4 | none
---
Consumables solve particular problems. Healing supplies restore HP, mana supplies restore casting resources, antidotes cure poison, Willow Draught restores Mental in the dungeon and herbs give a short buff. One does not substitute for the others.

## Use the correct supply

Check the combat and room menus for available consumable actions. Herbs and potions can interact with healing bonuses; the final amount is not always the raw item label.

## How much a healing potion heals

A healing potion heals 30 + 5 x your level + a random 10 to 29, or {{percent:ScaledPotionMaxHPFraction}} percent of the drinker's maximum HP when that is more. At level 10 with 800 maximum HP the flat part is 90 to 109 and 20 percent is 160, so a potion heals 160. It never heals more than the HP you are missing. A potion given to an ally uses the ally's maximum HP for the 20 percent. Outside a fight in the dungeon the random part is 10 to 30.

### Potion Mastery (Alchemist)

An Alchemist's healing potions heal {{percent:AlchemistPotionMasteryBonus}} percent more, also when the Alchemist gives one to an ally. Mana potions do not get the bonus.

## Potions in a fight

In a fight, I drinks healing potions: as many as it takes to fill your HP, all in one action, each healing the same rolled amount. With both kinds of potion and HP and mana both missing, I asks which kind. A mana potion restores 30 + 5 x your level mana, one per action. Drinking is your action for the round. H heals an ally with a potion or a spell, and U spends one potion to lift a [Downed ally](/wiki/en/combat/party/#downed-allies).

### Potion cooldown in Old God fights

In an Old God fight, after you drink a healing or mana potion, or use one to rescue a Downed ally, you cannot drink another for {{balance:bossPotionCooldownRounds}} round; trying spends your action, and Rescue is not offered until the cooldown ends. Ordinary fights and floor bosses have no potion cooldown, and herbs have none anywhere.

### Difficulty and potions

On Easy every healing potion you or your party drink heals 25 percent more, and on Nightmare 25 percent less, in a fight or out of it. The multiplier comes before Potion Mastery and the Infirmary, and the heal and the number of potions to full HP that a menu shows include it. An opponent's potion in a fight against another player is not changed.

### Auto-combat and Auto Heal

Auto-combat drinks one healing potion when your HP is at or below your threshold: {{balance:AutoCombatHealPercentDefault}} percent by default, set from {{balance:AutoCombatHealPercentMin}} to {{balance:AutoCombatHealPercentMax}} in steps of 10.

With Auto Heal on, after a fight you drink healing potions until the HP you are missing is less than half an average potion. The average potion is 50 + 5 x your level, or 20 percent of your maximum HP when that is more, times the difficulty multiplier above (with Potion Mastery, 50 percent more). A class with mana does the same with mana potions, by 30 + 5 x level.

Willow Draught stops at your ordinary Mental cap and is kept if there is nothing to restore. Addiction and Broken require their own treatment decisions.

## Healing potion price and how many you can carry

A healing potion costs {{balance:HealingPotionBaseCost}} + level squared / 2 gold before tax: 300 at level 20, 1,350 at level 50 and 5,100 at level 100. You can carry 20 + (level - 1) healing potions: 39 at level 20. That is also how many the Healer and the Magic Shop let you buy up to. At the daily maintenance, when you carry 2 or more healing potions above 20, half of those above 20 spoil. The Healer (B) and the Magic Shop (H) both sell them; the Magic Shop puts its own price modifiers on the same base price.

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
