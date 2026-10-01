# Usurper Reborn v1.2.3

## What is new in 1.2.3

- **The dungeon is translated.** Room views, settlements, the wilderness,
  rare encounters, puzzles, merchants, the map, party and follower screens,
  secret bosses, companion quest scenes and group broadcasts show in the
  player's language.
- **Screens fit 79 columns.** Group dungeon broadcasts, the Quick Commands
  bar and the status line under location menus wrap to a second row instead
  of running off the screen.
- **Magic Shop accessories:** basic rings and necklaces show their armor and
  give a small stat, and sell all sells only the accessories it lists.
- **The wiki** can show, on a guide page, what changed in the feature it
  describes, version by version.

## Magic Shop

- Basic rings and necklaces (Leather Cord, Bone Necklace, Silver Chain,
  Copper Ring, Silver Ring, Gold Ring) show their armor in the shop list and
  the purchase detail, and armor counts toward the upgrade marker.
- Basic rings and necklaces that had only armor now also give a small stat:
  Dexterity on rings and Wisdom on necklaces, +1, or +2 from level 30.
- Sell all sells only the accessories it lists. Cursed and unidentified
  accessories stay in the backpack, and the gold paid matches the quoted
  total.

## Dungeon translation

- **Dungeon screens:** the room view, exits, floor notices, the NG+ notice,
  the Guardian victory, Mira's reaction, the fallen adventurer journal,
  whispers, respawn news and duelist weapons are translated in all five
  languages.
- **Merchants and exploring:** merchant and rare items, puzzles, settlements
  and trade goods, the map and its legend, party and follower screens,
  follower inventory and equipment, secret bosses, companion quest scenes,
  camp and group notices are translated.
- **Settlements, wilderness, rare encounters and puzzles:** settlement
  screens, NPC titles, greetings and lore, wilderness scouting and beast
  species, rare encounter and puzzle text, and feature stat checks are
  translated.
- **Group dungeon broadcasts** reach each follower in their own language.
  The notice that a follower left the dungeon dead is shown in the leader's
  language.
- The dungeon map header and the watchtower show the theme's short name
  (for example "Ancient Ruins" rather than "AncientRuins").

## Screen layout

- Group dungeon broadcasts and group notices wrap at 79 columns, so long
  player names no longer run off the screen. Continuation rows keep the
  color and indent.
- Settlement, workshop, shrine, puzzle and beast encounter text wraps at 79
  columns.
- The Quick Commands bar and the status line under location menus wrap to a
  second row when they would pass 79 columns; rows that fit are unchanged.
  In English the Quick Commands bar now takes two rows everywhere except
  Main Street.
- The BBS status line wraps when very large gold would push it past 79
  columns.
- The ally list shows HP on its own row, the potion menu moves a caster's
  mana to its own row when the line is too long, and the divine "Remember
  this agony" line wraps.
- The "They know what you did down there" line on returning to town is split
  over two rows so it fits the screen.

## Fixes

- The dungeon status row no longer doubles the space and colon before
  Potions, Gold and Level: it reads `Potions:0/39 Gold:0` (was
  `Potions::0/39  Gold::0`).
- The BBS status line shows `Lv:99` (was `Lv::99`).
- The invalid-choice hint reads "Try: [%] Status, [*] Inventory, [R] Return,
  or [?] for help" in all five languages (was "Try: [: [%] Status ,
  [*] Inventory  , [R] Return , , or [ [?] for help").
- The rival line shows `(Lv: 12)` (was `( Lv: 12)`).

### English wording changes

- Eleven lines use "--" in place of a long dash: Mira's prayer, two dungeon
  respawn news lines, the map revealed broadcast and seven settlement
  greeting and lore lines.

## The wiki

- Guide pages can show a "Changes by version" section listing what changed
  in the feature they describe, newest first. The history starts with 1.2.3.

## Known and unchanged

- Some dungeon text is still English: duelist garb and battle cries, gear
  names, dungeon reward labels and feature examine broadcasts. The dungeon
  theme name in the descend broadcast is not translated.
- Wilderness monster names, tamed beast names and Gauntlet champion item
  names stay in English.
- The follower room view still has English: the Floor line with the raw
  theme name, the ">>" hints, "You notice:", "Exits:", the `(clr)` and
  `(exp)` markers and the footer.
- Room text in a mixed-language group shows in the language of the session
  that generated the floor (settlement room descriptions and room flavor are
  set when the floor is generated).
- Item bonus stat labels and the "Accessory" fallback, reward source labels,
  skill names on the skill toggles and the "Dungeon (Group: ...)" online
  location are English.
- The tame success text in the wilderness does not wrap.
- On the news board the Seal line runs past 79 columns with its date prefix.
- In French a wrapped broadcast can leave "!" alone on its row.
- Screen-reader status and quick-command rows stay on one line by design,
  and run to 120 columns in English and 127 in Hungarian.
- Wiki tooling: a guide history line with a version newer than the game
  makes the drift check exit with a parse error (2) rather than 1; it fails
  either way.
