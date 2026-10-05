---
title: Potions, herbs and supplies
path: /wiki/en/items/consumables/
checked: 1.2.6
sources: Scripts/Locations/HealerLocation.cs, Scripts/Systems/CombatEngine.cs, Scripts/Systems/PotionBonus.cs
history: 1.2.6 | none
history: 1.2.5 | Healer menus and the potion and herb labels are shown in all five languages.
history: 1.2.4 | none
---
Consumables solve particular problems. Healing supplies restore HP, mana supplies restore casting resources, and Willow Draught restores Mental in the dungeon. One does not substitute for the others.

## Use the correct supply

Check the combat and room menus for available consumable actions. Boss potion cooldowns and fight context can limit repeated use. Herbs and potions can interact with healing bonuses; the final amount is not always the raw item label.

Willow Draught stops at your ordinary Mental cap and is kept if there is nothing to restore. Addiction and Broken require their own treatment decisions.

## Keep a reserve

Bring enough supplies to return safely, not just to win the next fight. Camp or town treatment can save consumables, but neither is available in every situation.

Fixed equipment data does not contain a complete live consumable inventory. Follow the Healer's current prices and capacity messages rather than an invented catalog price.
