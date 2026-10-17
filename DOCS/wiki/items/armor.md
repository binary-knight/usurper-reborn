---
title: Armor and accessories
path: /wiki/en/items/armor/
checked: 1.3.3
sources: Scripts/Data/EquipmentData.cs, Scripts/Core/Items.cs, Scripts/Systems/GearSetSystem.cs, Scripts/Core/Character.cs, Scripts/Locations/BaseLocation.cs, Scripts/Systems/InventorySystem.cs
history: 1.3.3 | none
history: 1.3.2 | A Mystic Shaman follows its own armor weight limit.
history: 1.2.5 | Armor names show in your language, and shop and equipment rows mark armor weight as [Lgt], [Med] or [Hvy].
---
Armor class, defence bonuses, resistances and block effects are different fields. More expensive equipment can trade one kind of protection for another. Your class's armor rules still apply.

Use the [full equipment catalog](/wiki/en/items/) to compare slots and open an item for every nonzero stat and restriction.

## Evaluate the whole item

A shield can support a shield-required ability. An accessory can add attributes or resource capacity without being body armor. A cursed item can have consequences that a single armor number does not reveal; the [loot guide](/wiki/en/items/) lists what a curse does.

Server-generated stock can differ from the fixed reference. Read the shop inspection before buying, and check Status after equipping.

## Gear sets

There are 8 gear sets: Leather, Chain, Silk, Shadow, Steel, Reinforced, Forged and Mithril. Wearing pieces of one set gives bonuses at 2, 4 and 6 pieces (Silk at 2, 4 and 5), and every tier you reach adds up. A piece belongs to a set by its base name, so a shop piece and a dungeon drop of the same name both count, whatever their rarity or enchants.

{{table:gear-sets}}

## How set pieces are counted

Pieces are counted by the slot they fill, so two rings of one set count as 2. A Constitution bonus also raises maximum HP through Constitution, on top of any Max HP the set gives. NPCs, companions and PvP opponents get the same set bonuses. The `/gear` command (or `/eq`) lists each set you wear, with its pieces and which tiers are active, and an item's own screen names its set.
