# Usurper Reborn v1.1.9

Two player reports, fixed: the feet slot offered weapons instead of boots,
and gear sets ignored most of the gear people were actually wearing.

## The feet slot

Choosing the feet slot to equip from the backpack listed your weapons, not
your boots. The slot filter had no entry for feet, so it fell through to its
default, which is weapons. Boots are now offered for the feet slot.

The same was reported for companions. The companion screens at the Inn
already offered boots for the feet slot and a test now holds them to it; if
you see weapons there, tell us which screen you were on.

## Body armor labelled as a shield

Armor, rings and weapons with the "of Shielding" suffix were labelled
[Shield] in the backpack, because the check looked for the letters "Shield"
anywhere in the name. It now looks for whole words, so "Shielding",
"Towering", "Target" and "Theater" no longer count, and real shields,
bucklers, aegises and bulwarks still do.

## Gear sets count the gear you already have

A set piece is recognised by the template it dropped as, which every item
has carried since v1.1.0. Gear from before v1.1.0 carries nothing, so it
never counted, even when it was the same Leather Cap as a newer one. On our
server that was 94 percent of the gear being worn. Two identical pieces
could be a set piece or not depending on when they dropped.

Gear without a recorded template now has it read from its name, and that
applies to your bonuses and to the set line on the item screen. On our
server, 118 set bonuses switch on or go up for players when this version
starts, and 103 for NPCs.

How the name is read, so it cannot hand out a set that was not earned:

- A recorded template always wins. The name is read only when there is
  none, and what is read is never written into your save.
- The name has to be one the game actually generates for that piece: the
  plain name, a rarity or curse, an effect before or after it, a world boss
  element in front, and any enchantments from the magic shop at the end.
  Every one of those, in all five languages, is checked by a test.
- Every item the game knows is compared, set pieces or not, and the
  closest match wins. So a Studded Leather Cap is not a Leather Cap, and
  neither is a Forged-Thread Cape or a Cloak of Shadows a set piece.
- A helmet's name can only match a helmet, never boots or a ring.

Starter gear that has exactly a set piece's name, such as a plain Leather
Cap or Chain Coif, counts too: same name, same set, whoever wears it. Most
of it is worn by NPCs, who pick it up when they spawn and when they shop.

One exception: in French, the Shadow Cloak and the Cloak of Shadows are both
"Cape des Ombres". An old French Shadow Cloak cannot say which of the two it
is, so it does not count toward the Shadow set. Anything that dropped since
v1.1.0 is unaffected.

## For server operators

The v1.1.8 release was built and shipped, but its automatic deploy failed
before touching the server, so a server that relies on that deploy is still
on v1.1.7. The fault is fixed, and this release deploys normally, including
everything v1.1.8 carried: the database indexes, compression and the
website. Nothing extra is needed.

## Tests

1,277 passing, up from 1,198. The new ones check every name the game can
give each gear template, in every language and with every enchantment,
against the set it belongs to: over 800,000 names. They also generate
30,000 real drops, erase their recorded template and read it back from the
name, and pin each counterexample above.
