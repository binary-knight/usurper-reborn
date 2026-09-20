# Usurper Reborn v1.1.7

A repair release for the economy. A bug closed in v0.65.3 left items on the
live server with stats and prices in the hundreds of millions, and the ways
they were made were not the only ways left open. This release bounds what an
item can be, repairs the ones that already exist, and closes every printer
the review found.

## Why

A player report in June (issue #112) showed that unequipping a reforged item
reset its quality while keeping its stats. That was fixed, but the items it
made are still in the world, and the reforge itself still multiplied a
weapon's numbers from their own current values with no ceiling, no daily
limit, and a price of fifty gold at level one. Reviewing that turned up four
more holes of the same shape: enchanting multiplied an item's value rather
than adding to it, the limit meant to stop it was forgotten every time an
item went into the backpack, the arena's gold cap applied only to accounts
whose name ended in the alt suffix, and a PvP win could mint gold from half
the value of an item looked up by name across every item in the game.

## Items have limits now

- **Every number on an item is bounded**, wherever an item enters play: on
  load, in the shops, through the converters, out of an auction, a guild
  bank, or an inheritance. Penalties on cursed gear stay penalties, and many
  small bonuses of one kind can no longer sum past the limit.
- **Items already over the line are repaired the next time they are loaded**,
  and each repair is written to the server's own log so the maintainer can
  see what was corrected and on whose character.
- **Nothing legitimate is touched.** The strongest weapon the dungeon can
  drop is near fourteen hundred power at level one hundred, and the most
  valuable item in the game's own tables is four million gold. The limits sit
  well above both; a test walks every item in the game to prove it.

## Enchanting, reforging, and the arena

- **An enchant adds what it cost.** It used to add half again the item's own
  worth for a flat fee, which paid for itself many times over on resale.
  Reselling an enchanted item now always loses gold, at any price and at the
  best fence in the game. The stat bonuses are unchanged.
- **An item remembers its enchants.** The five-enchant limit and the rule of
  one enchant of each kind lived in a note that was thrown away every time an
  item went into the backpack. They hold now, and an enchant that is lost or
  paid to remove frees its kind again.
- **Reforging is a bounded roll:** three a day, a real price at every level,
  a value that follows its formula instead of only ever climbing, and a
  ceiling of half again the best weapon the dungeon can drop for someone of
  your level. A weapon already beyond that is handed back untouched rather
  than made worse. The smith's daily count is saved with your character.
- **One PvP fight can move only so much gold**, by the level of whoever
  receives it, on the winning side and the losing side alike, with the
  tighter cap on alt accounts still layered under it. The winner receives
  exactly what the loser lost, so a spent purse cannot mint gold.
- **Salvage is priced from what the loser is wearing**, not from the first
  item in the game with the same name.

## The audit

The old check compared a player's wealth to their lifetime earnings, which
said nothing about an account whose earnings were the sale of a duped item.
It now also watches gold per item sold, lifetime sales and the highest hit
against the player's level, and any item over the limits. These are notes in
the server's own log for the maintainer to read. Nothing is corrected
automatically, no character is punished, and nothing leaves the machine.

## Not in this release

The ten percent arena steal, the throne challenge rules, and the cost of
lifting a curse are unchanged; all three were working as designed. No
character's saved data was edited.

## Tests

1,189 passing, up from 1,154. Four new files cover the item bounds and the
repair, enchant pricing and memory, the reforge including ten thousand
simulated reforges at three levels, the arena caps against a real database
including twelve simultaneous winners, and the audit.
