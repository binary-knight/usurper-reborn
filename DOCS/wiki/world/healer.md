---
title: The Healer
path: /wiki/en/world/healer/
checked: 1.3.4
sources: Scripts/Locations/HealerLocation.cs, Scripts/Systems/MentalSystem.cs, Scripts/Core/GameConfig.cs, Scripts/Core/Character.cs, Scripts/Systems/CityControlSystem.cs
history: 1.3.4 | none
history: 1.3.3 | none
history: 1.3.2 | none
history: 1.2.5 | The Healer's Electron menu labels and the names of cursed items shown for curse removal are in the player's language.
---
The Healer treats more than missing HP. Decide whether you need wound treatment, supplies, a cure, addiction rehab or Mental therapy. Open it from Main Street with G for Guild Row, then H.

## Healer cost for HP

The Healer restores HP for 2 gold per HP before tax. H heals the amount you choose; F heals you to full.

## Healer tax

Every Healer price has the king's tax and the city tax added, but the two together are capped at 15 percent however high the ruler sets them. With no ruler there is no tax. The Healer's prices do not change with your alignment, faction or difficulty.

## Potions and antidotes

- B, healing potions: 100 + level squared / 2 gold each, 300 at level 20. You can carry 20 + (level - 1).
- M, mana potions: 150 + level squared / 2 gold each, 350 at level 20, restoring 30 + level x 5 mana. You can carry 20 + (level - 1).
- N, antidotes: 75 gold each. You can carry 5 + level / 10, rounded down.

All before tax. See [potions, herbs and supplies](/wiki/en/items/consumables/).

## Cure cost

A cure costs its base price times (1 + level to the power 0.6), before tax. At level 20 the factor is about 7.03, at level 50 about 11.46 and at level 100 about 16.85.

- P, poison: base 200, so 1,406 gold at level 20.
- C, diseases: Lover's Bane 1,000, blindness 1,500, plague 2,000, smallpox 2,500, measles 3,000, leprosy 3,500. You can cure one or all.
- D, a cursed item you wear: base 500. The Healer destroys the item; the [Magic Shop](/wiki/en/items/shops/) removes a curse and keeps the item.

## Talk Therapy

Therapy restores Mental and clears Broken. The bill depends on missing Mental, your level and the Healer's tax. A Broken character has a minimum bill even if ordinary recovery has already raised Mental.

Check the quote and carried gold before confirming. Therapy can lift Mental above the addiction cap, but that surplus does not permanently remove the cap.

## Addiction Rehab

Rehab (A) costs {{balance:RehabBaseCost}} gold plus {{balance:RehabPerAddictionCost}} per point of addiction, before tax. It is offered to addicted characters. It clears addiction, cancels a pending drug crash, resets tolerance and also clears Broken. If you are not addicted, Talk Therapy is the usual Broken cure.

## Willow Draught

Willow Draught (W) is a separate supply for the dungeon. It restores {{balance:MentalWillowDraughtGain}} Mental up to your cap, not HP. It costs as much as {{balance:MentalWillowPotionMultiplier}} healing potions at your level, plus tax: 600 gold at level 20 before tax. You can carry {{balance:MaxWillowDraughts}}.

For the full recovery rules, see [Mental](/wiki/en/characters/mental/).
