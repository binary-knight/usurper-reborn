---
title: Equipment and economy
path: /wiki/en/items/
checked: 1.3.6
sources: Scripts/Data/EquipmentData.cs, Scripts/Systems/ShopItemGenerator.cs, Scripts/Systems/LootGenerator.cs, Scripts/Systems/CombatEngine.cs, Scripts/Core/GameConfig.cs
history: 1.3.6 | Titan's Resolve on world boss armor gives 5 percent more maximum HP in place of 5 percent more defense, once however many worn items carry it.
history: 1.3.5 | Shadowfang, Venomstrike and Serpent's Fang can now deal their poison damage on a hit, the Max HP, Max Mana and all stats loot effects count as shown, and gear regeneration is shown as a percent of the pool.
history: 1.3.4 | none
history: 1.3.3 | none
history: 1.3.2 | Loot for a Mystic Shaman follows its own class rules, and the Steel Faceplate no longer lists the Mystic Shaman.
history: 1.2.3 | Shop rings and necklaces that had only armor now also give a small stat: Dexterity on rings and Wisdom on necklaces, +1, or +2 from level 30.
---
This catalog lists fixed built-in equipment templates. Shops can generate stock that is not in this list, and server mods can replace definitions. A catalog entry does not promise a drop source or current stock. Shop rings and necklaces with no stat of their own give +1 Dexterity (rings) or +1 Wisdom (necklaces), +2 from item level 30.

- [Equipment slots](/wiki/en/items/equipment-slots/)
- [Weapons](/wiki/en/items/weapons/)
- [Armor, accessories and gear sets](/wiki/en/items/armor/)
- [Consumables and herbs](/wiki/en/items/consumables/)
- [Gold and banking](/wiki/en/items/banking/)
- [Shops, magic services and auctions](/wiki/en/items/shops/)
- [Enchanting](/wiki/en/items/enchanting/)
- [Crafting materials](/wiki/en/items/materials/)

## Loot drop chance

Each ordinary monster you kill rolls for an item on its own: {{percent:LootBaseDropChance}} percent, plus 0.5 percent per monster level, plus {{percent:LootPartyBonusPerMember}} percent for each living ally in your party who is not another player, plus a god's luck boon, at most {{percent:LootMaxDropChance}} percent. A level 30 monster with 2 allies: 45 percent.

A monster whose name holds Boss, Chief, Lord or King has a fixed 35 percent chance, whatever its level or your party.

## What bosses and mini-bosses drop

A mini-boss (a champion) always drops 2 items. Each is at least Uncommon, with its rarity rolled as if the monster were 10 levels higher. A floor boss always drops 1 item of at least Epic rarity, rolled as if 25 levels higher.

A [world boss](/wiki/en/online/world-boss/) armor item (not a weapon) of Epic rarity or better carries Titan's Resolve: 5 percent more maximum HP for whoever wears it, a companion or ally included, once however many worn items carry it.

## Weapon or armor, and which slot

An ordinary drop is a weapon 45 percent of the time and armor 55 percent; a mini-boss item 35 and 65, a boss item 40 and 60. An armor drop's slot: body 22 percent, head 10, legs 9, feet 9, arms 8, hands 8, cloak 8, waist 7, shield 6, face 5, ring 4, necklace 4.

With allies in your party who are not players, {{percent:LootTeammateTargetChance}} percent of ordinary drops are made for a random ally's class instead of yours, so you can see loot for other classes.

## Item rarity

{{table:loot-rarity}}

A drop's power is the template's power x (1 + level / 80) x the rarity's power multiplier, then plus or minus 15 percent; a weapon has at least 5 and armor at least 3. Its value is power x 15 for a weapon and x 20 for armor, x the rarity's value multiplier. The level an item needs is its level minus 10, raised to match its power.

## Chance of legendary loot

The rarity of a drop depends on the monster's level L, with f = L / 100 and at most 1: Artifact 3 x f percent, Legendary 2 + 10 x f, Epic 3 + 22 x f, Rare 10 + 25 x f, Uncommon 30 - 10 x f, and Common the rest.

- Level 25: Artifact 0.75, Legendary 4.5, Epic 8.5, Rare 16.25, Uncommon 27.5, Common 42.5 percent.
- Level 50: Artifact 1.5, Legendary 7, Epic 14, Rare 22.5, Uncommon 25, Common 30 percent.
- Level 100 and above: Artifact 3, Legendary 12, Epic 25, Rare 35, Uncommon 20, Common 5 percent.

## Cursed items

A cursed weapon drop has 25 percent more attack and half the value. It also loses a penalty of 5 or a tenth of its attack, whichever is more: half of it off Strength, a third off Dexterity, a third off Wisdom and half off Constitution. Cursed armor works the same way with 25 percent more armor and a penalty of 5. A cursed item cannot be sold or enchanted. The Magic Shop can lift the curse and keep the item, and the Healer destroys it; see [shops](/wiki/en/items/shops/).

## Identification

New drops are always identified. Identification at the Magic Shop is for items found before that rule.

## Built-in equipment catalog

{{table:items}}

Base values are definition values, not guaranteed purchase quotes. Prices and restrictions shown in the live shop are authoritative for that transaction.
