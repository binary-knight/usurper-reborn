# Usurper Reborn v1.2.2

## What is new in 1.2.2

- **Combat is translated.** The whole combat system, class ability names
  and descriptions included, shows in the player's language. In group combat,
  each member reads the lines in their own language.
- **Item comparison screens, status effects and poison are translated.**
- **Temple offerings:** gold offerings no longer need a good deed left, and
  offerings no longer give a good or dark deed back.
- **The wiki** has new guides for reporting bugs, dungeon features and rare
  encounters, and search finds pages that match a phrase first.

## Fixes and balance

- **Temple gold offerings no longer need a good deed left.** An offering
  follows the Favor rules only: the gold, the daily gold Favor cap and the
  wrong god handling.
- **Offerings no longer give back a deed (balance change).** Gold and item
  offerings, to the Temple gods and to player-gods, no longer add a good or
  dark deed to the day's count. Alignment, faction standing and Favor from
  offerings are unchanged.
- The in-game `/help` line for `/bug`, in all five languages, no longer says
  it opens GitHub; reports go to the developer.

## Translation

- **Item comparison screens:** backpack equip, the slot screen, loot pickup,
  the Magic Shop and companion equip show upgrade and downgrade, stat names,
  slots and rarity in the player's language.
- **Beside the comparisons:** the inventory summary and the Home, Inn and
  Magic Shop lines next to the gear comparisons are translated. The French
  slot picker fits the screen.
- **Status effects and poison:** poison, burn, disease and status effect
  messages, their tick and expiry lines, and group death broadcasts are
  translated, and each group member reads them in their own language.
- **The whole combat system:** group lines, compact menus, procs, loot,
  status panels, ability lines, menus, fight summaries, death lines, boss
  mechanics, XP and PvP lines, rage events, Old God and world boss lines,
  spell learning and the quickbar.
- **Class ability names and descriptions** show in the player's language.
- **Group combat in each player's language:** lines shown on other group
  members' screens appear in each member's own language, including 28
  existing combat lines that used the leader's language. The captured
  status, loot notice, follower cannot-act and status tick lines, and Old
  God area and channel names, reach each group member in their own language.
- Compact combat menu rows fit 80 columns in all five languages.

### English wording changes

- Item bonuses show as `HP+n` and `MR+n`. The comparison screen says
  `Max HP` (was `MaxHP`) and `MR` (was `MagicRes`).
- The group ambush line uses a colon. The COMBAT STATUS and ALLIES titles
  are centred.
- The echo line adds "into mist". The Balanced line uses a colon. Death
  counts use singular and plural. Two box titles are centred.
- "She has escaped, for now." and the grand remedy "curative:" line no
  longer contain a dash.

## The wiki

- **Reporting bugs:** a guide to reporting a bug with `!` or `/bug` in the
  game, or as an issue on GitHub.
- **Dungeon features** and **rare encounters:** two new guides. Rare
  encounter outcomes are behind spoiler disclosures.
- **Search** scores matching word pairs, so a phrase like "monster
  families" finds its page first.
- **Discord Suggest** accepts `suggestion:` as well as `suggest:`.

## Known and unchanged

- Still in the group leader's language after the combat translation: the
  follower's full combat screen, the round-status line and other captured
  broadcasts.
- Ability names in the wiki and in client data (GMCP) are in English.
- 23 Electron client messages from combat are not translated yet.
- A murder grudge still raises the town NPC's HP, up to 1.5 times its max
  HP. The fight uses its own opponent, so this has no effect in the fight.
- Wiki guides are in English only. Some reference pages list translated
  names where the game has them.

## Internal

- A scanner lists hardcoded player-visible English in `Scripts/`
  (`Tests/Localization/loc-scan.sh`). At this release it counts 1,721 sites
  in output calls, in 81 files, and 4,755 sites outside output calls (data
  tables and Electron payloads), in 91 files.
- Two ratchet tests, one for output calls and one for text outside them,
  keep each file's count from rising and require the baseline to be lowered
  when a count falls.
