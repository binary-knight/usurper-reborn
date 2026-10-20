---
title: Shopping, magic services and auctions
path: /wiki/en/items/shops/
checked: 1.3.5
sources: Scripts/Locations/WeaponShopLocation.cs, Scripts/Locations/ArmorShopLocation.cs, Scripts/Locations/MagicShopLocation.cs, Scripts/Locations/MainStreetDistricts.cs, Scripts/Systems/ShopItemGenerator.cs, Scripts/Systems/HagglingEngine.cs, Scripts/Systems/AlignmentSystem.cs, Scripts/Systems/FactionSystem.cs, Scripts/Systems/DifficultySystem.cs, Scripts/Systems/CityControlSystem.cs, Scripts/Core/King.cs, Scripts/Locations/BaseLocation.cs, Scripts/Systems/MarketplaceSystem.cs
history: 1.3.5 | Enchant removal lists enchanted items in your backpack, a failed 4th or 5th enchant destroys one random enchant whole, and items sold at the auction keep their loot effects, such as fire, frost and life steal.
history: 1.3.4 | none
history: 1.3.3 | none
history: 1.3.2 | A Troll pays 10 percent less for a weapon, purifying any cursed armor piece removes the curse's armor boost, and the Magic Shop refuses to remove an enchant from a cursed item.
history: 1.2.5 | Magic Shop enchant removal now returns an item to its base form, taking off every stat, effect, value and name tag its enchants added.
history: 1.2.4 | The Magic Shop lets you haggle over rings and necklaces, 3 tries a day, and being thrown out bars you from it until the next day.
history: 1.2.3 | The Magic Shop shows armor on rings and necklaces and counts it toward the upgrade marker, and sell all sells only the accessories it lists.
---
Weapon and armor shops inspect and sell different equipment. A shop's generated stock is not identical to the fixed [equipment catalog](/wiki/en/items/).

## Buying equipment

Check the quoted price, level and class restrictions, strength requirement and slot. Buying a weapon does not mean that you can use every ability with it. Check its weapon type and whether it is two-handed.

The Weapon and Armor shops show in grey an item whose class list leaves out your class, unless you play a prestige class. The Weapon Shop also shows in grey a weapon that a Mystic Shaman cannot hold in its main hand.

Higher value is not a universal upgrade score. Compare nonzero bonuses with the attributes and effects your build needs.

## Shop stock and when shops get new gear

The Weapon and Armor shops build their stock from the same templates as dungeon loot. Each template is stocked at its own minimum level and then at every multiple of 5 levels, 8 versions at most. A shop item has {{percent:ShopPowerMultiplier}} percent of the power of a Common dungeon drop of the same level and is never cursed. It is Common up to level 20 and Uncommon above, never higher. Shop pieces count toward [gear sets](/wiki/en/items/armor/).

## Shop prices

A shop item's list price is its level to the power 1.5, times {{balance:ShopPriceMultiplier}}, times a slot factor, and at least 10 gold. Slot factors: main hand and body 1.0, off hand 0.8, head 0.7, legs 0.65, neck and rings 0.6, arms, hands and feet 0.5, cloak 0.45, waist and face 0.4. A level 20 main hand weapon lists at 26,832 gold, a level 20 helm at 18,782.

The price you pay at the Weapon and Armor shops is the list price times your alignment modifier and any world event modifier, then less a city controller's discount, a Crown member's discount and a god's shop discount, then times the difficulty modifier, and then the sales tax is added.

### Alignment prices

Honest shops (the Weapon, Armor and Magic shops) charge by alignment: Holy x0.8, Good x0.9, Balanced x0.95, Neutral x1.0, Dark x1.15, Evil x1.4. A Good or Holy character with 250 Chivalry or more pays a further x0.95, x0.90 at 450, x0.85 at 650 and x0.80 at 800. The Dark Alley reverses the table; see [the Dark Alley](/wiki/en/world/dark-alley/).

You are Holy at 800 Chivalry or more with Darkness under 100, and Evil at 800 Darkness or more with Chivalry under 100. Otherwise you are Good when Chivalry is 400 or more above Darkness, Dark when Darkness is 400 or more above Chivalry, Balanced when both are over 100 and within 100 of each other, and Neutral in every other case.

### Other price modifiers

- Crown faction members pay 5 percent less at rank 0, rising to 15 percent less at rank 8.
- The member of the team that controls the city pays 10 percent less.
- Difficulty: Easy x0.85, Normal x1.0, Hard x1.15, Nightmare x1.25.
- A Troll pays 10 percent less for a weapon at the Weapon Shop, after every other modifier and before the tax, and the Weapon Shop does not haggle with a Troll.

### Shop tax

While a ruler sits on the throne, a king's tax and a city tax are added on top of the price, each at least 1 gold. The defaults are 5 percent and 2 percent, and the ruler can change both. The Healer's tax is capped at 15 percent in total; see [the Healer](/wiki/en/world/healer/).

## Sell price

The Weapon and Armor shops, and the Magic Shop for rings and necklaces, buy an item for half its value. A member of the Shadows gets 10 percent more at rank 0, rising to 30 percent more at rank 8. A cursed item cannot be sold, and the Magic Shop refuses an unidentified accessory.

## Haggling

At the buy prompt in the Weapon and Armor shops, and for rings and necklaces in the Magic Shop, H haggles over the price before tax. Each of the three shops gives you 3 tries a day, reset with the new day.

A haggle is not a roll. You name a price, and the keeper accepts it when the discount is within your Charisma band: up to 4 percent at Charisma 1 to 25, 7 at 26 to 75, 10 at 76 to 125, 13 at 126 to 175, 17 at 176 to 200 and 20 at 201 or more. An offer more than 20 percent off is always refused. Every offer uses a try, accepted or not.

You are thrown out only when you ask to haggle with no tries left and then insist. That shop then stays closed to you until the next day.

## Magic services

The Magic Shop offers class-relevant magic and services such as identification, curse removal and enchanting. These have different target and cost rules. Removing a curse from an owned item is not the same action as replacing its base template. Enchant removal (W) works on equipment you are wearing and on enchanted items in your backpack, and returns the item to its base form: the stats, fire and frost effects, value and name tags its enchants added are taken off, and the enchant limit applies again from that form. If the item's base form is not on record, the shop refuses and charges nothing. A cursed item is refused too, as it is for enchanting, and nothing is charged; have the curse removed first. An enchant or an enchant removal on a worn item puts the changed item straight back in the slot it came from, even when you could not equip that item now, and nothing else you wear moves; a backpack item goes back to its place in the backpack.

Read the selected item and confirmation, especially when a service can act on teammate equipment.

### Enchanting

Enchant costs, limits, failure chances and the materials each enchant needs are in [enchanting](/wiki/en/items/enchanting/).

### Magic Shop prices

- Identification (I) costs 100 + level x 50 gold. New dungeon drops are always identified, so this is for older items.
- Curse removal (C) costs twice the item's value plus 100 gold for every point of negative Strength, Defence, Dexterity and Wisdom, at least 500 gold, plus tax.
- Purifying a worn item, or a weapon or any armor piece in your backpack (body, shield, helm, arms, hands, legs, feet, waist, face or cloak), leaves it 80 percent of the power it had before the curse, which is 64 percent of its cursed power.
- Scrying (G) costs 1,000 + level x 50 gold before the Magic Shop's modifiers.
- A dungeon reset scroll (D) costs 1,000 + 5 x level squared gold plus tax: 3,000 at level 20 and 13,500 at level 50, before tax.
- Enchant removal (W) costs 5,000 + level x 200 gold before the modifiers.

## Auctions

Offline, the Auction House lists items at your own price beside listings from NPCs. A listing expires 30 real days after it is posted. An NPC prices its item at 100 to 150 percent of the item's value by its greed, then plus or minus 5 percent.

Online, a listing runs 12, 24, 48 or 72 hours for a fee of 5, 4, 3 or 2 percent of your asking price, plus the king's tax percent, at least 1 gold. You can have 5 listings active at once. Check the actual lot before bidding or selling; a template in this wiki is not a promise that it is listed.
