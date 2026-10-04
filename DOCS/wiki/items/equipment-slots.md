---
title: Equipment slots
path: /wiki/en/items/equipment-slots/
checked: 1.2.5
sources: Scripts/Core/EquipmentEnums.cs, Scripts/Core/Character.cs, Scripts/Systems/InventorySystem.cs, Scripts/Locations/BaseLocation.cs, Scripts/Core/Items.cs
history: 1.2.5 | Equipment and backpack rows mark weapons [1H] or [2H] and armor weight as [Lgt], [Med] or [Hvy], with stats separated by spaces.
history: 1.2.4 | none
history: 1.2.3 | none
---
Your character wears equipment in named slots. Open your inventory from a location menu with `*` or the `/inventory` command to see every slot and change what is in it.

## Slots on the inventory screen

The inventory screen groups the slots in three sections. The key before each slot selects it.

**Weapons**

- 1, Main Hand: your main weapon.
- 2, Off Hand: a second one-handed weapon or a shield.

**Armor**

- 3, Head: helms.
- 4, Body: chest armor and robes.
- 5, Arms: bracers.
- 6, Hands: gloves.
- 7, Legs: greaves.
- 8, Feet: boots.
- 9, Waist: belts.
- F, Face: masks.
- C, Cloak: cloaks.

**Accessories**

- N, Neck: an amulet or necklace.
- L, Left Ring: a ring.
- R, Right Ring: a ring.

## Weapon slots and handedness

There are two weapon slots, Main Hand and Off Hand. Each weapon is one-handed, two-handed or off-hand only. Shields, bucklers and tower shields are off-hand only.

- A one-handed weapon can go in either hand.
- Two one-handed weapons and no shield is dual wielding.
- A two-handed weapon fills both hands. Equipping one moves whatever was in both hands to your inventory.
- Equipping a shield while you hold a two-handed weapon takes the two-handed weapon off.
- A one-handed weapon cannot go in the off hand while you hold a two-handed weapon; the game refuses with a message.
- Staves, mauls, polearms and bows always count as two-handed.

## Rings

A ring fits either finger. The game fills an empty finger first.

## Restrictions

Level, class, strength and armor weight limits still apply to each slot. Prestige classes ignore the class and armor weight limits. Browse the [weapon list](/wiki/en/items/weapons/), [armor and accessories](/wiki/en/items/armor/) and the full [equipment catalog](/wiki/en/items/) by slot.
