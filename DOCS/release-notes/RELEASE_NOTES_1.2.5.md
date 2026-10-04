# Usurper Reborn v1.2.5

## What is new in 1.2.5

- **The whole game is translated** into Spanish, French, Hungarian and
  Italian: every screen, message and data table, from the town, castle and
  online server to NPC speech, item and monster names, quests,
  achievements, the story and the endings. A short list of text stays in
  English on purpose; it is below.
- **Every message, shop, equipment and status row fits 79 columns** in
  all five languages, and shop, equipment and backpack rows are tighter.
- **Mailbox cleanup:** the world boss notice is no longer mailed every
  night, existing world boss mail is deleted once, and system mail older
  than 30 days is deleted. Mail between players is never deleted.
- **Fixes:** PvP salvage for English players, moral paradoxes saved with the character, full Magic Shop enchant removal, Inn
  guards in NPC attacks, and the same loot bonuses in every language.
- **A new wiki guide** to accounts and character slots.

## The game in your language

The translation that began with combat in 1.2.2 and the dungeon in 1.2.3
now covers the rest of the game, in all five languages (English, Spanish,
French, Hungarian, Italian):

- Town: every location on Main Street (both layouts), the Inn, the Bank,
  Love Street, street encounters, the Magic Shop (menus, enchanting,
  haggling, love and death spells, identify, scrying), the weapon and
  armor shops, the Temple, Pantheon, Anchor Road, Dark Alley, Dormitory,
  Marketplace, Sanctum, Arena, Healer, Love Corner, Church and Music Shop,
  with their help screens, the MUD prompt, buffs, trade, bounty and
  auction screens.
- The castle: throne room, court, treasury, guards, prison orders, royal
  quests, orphanage, decrees and succession; the prison, the prison walk
  and its activities, royal petitions, throne challenges, city control
  news and the home screens.
- Online: chat, /who, tells, groups, rooms, login and relay screens, news,
  teams, mail, guilds, fees and account messages. Every server line
  reaches each player in that player's own language. The SSH relay menu
  gains [G] to choose the language of the relay screens.
- The game engine: the main menu, save and load, recovery, the splash,
  story, support and BBS screens, and the terminal prompts. Yes and no
  prompts show the letters your language accepts (for example I/n in
  Hungarian).
- The world simulation: world events, decrees, news, maintenance, NPC
  behaviour, gossip, memes, goals, hints and the bug report screens.
- Story and characters: the realm history, every ending (including NG+
  and Dissolution), amnesia moments, VN conversations, the Old Gods' and
  story characters' dialogue trees, moral paradoxes, ocean philosophy,
  archetypes, companions, relationships, romance, family, the character
  sheet, training, the Level Master and character creation.
- NPC speech: generated lines, greetings, shop moods, small talk,
  reactions, farewells and memory lines. The same line is chosen whatever
  your language.
- Items and monsters: weapon, armour and accessory names and descriptions,
  loot, monster and Old God names, artifacts and the combat tables.
- Achievements, quests, factions, founder statue inscriptions, spell cast
  messages, divine boons and blessings, specializations and the remaining
  game settings, save and version messages.
- The wiki's item, monster, achievement and specialization pages show the
  translated names per language.

Mail is written in the recipient's language: royal, divine, maintenance
and world mail, Inn murder mail, the declined package mail and expired
trade mail. Broadcasts such as the blood moon, the eulogy, achievements
and castle announcements reach each player in their own language. News is
written in the writer's language.

### Still in English on purpose

- Slash commands, BBS names, addresses and URLs, server protocol replies,
  the IP ban notice and admin lines.
- Words drawn into ASCII art: the founder statues and six art pieces
  (dungeon entrance, death, boss victory, level up, treasure).
- God rank titles ("Lesser Spirit" to "God"), which appear in shared news
  and chat names.
- The marriage place in marriage news ("Church", "Castle").
- Online locations on /who and the website, including
  "Dungeon (Group: ...)".
- Enchant parts in item names (such as " +4 Dex" and " (Blessed)"), so
  saved items keep matching their gear sets.
- God names, and the "Unknown" and "Unknown Ruler" fallback names, which
  are stored and shared.

Some names are stored in English inside the save and shown in your
language wherever a screen shows them: noble titles (King, Queen, Sir,
Dame), court roles, the castle armory items, saved item names, monster
names and guild ranks.

### English wording changes

- Broken joins in NPC speech are fixed ("Hello there., Citizen." now reads
  "Hello there, Citizen."; ", my dear." and ", if you don't mind." go before
  the closing mark; "Look, Greetings" reads "Look, greetings"), and twelve
  misspellings are corrected (Dont, youd, Youve, necomers, Heres, Im, Ill,
  Whats, lets, Let em).
- Two street lines gain apostrophes ("I've heard about you", "You'll do").
- "Half-Elf" shows with its hyphen and class names with spaces ("Mystic
  Shaman").
- Royal petition news calls a queen "Queen" (it said "King").
- Prison activity effects show what they give ("+1 Strength", it said
  "+1-2 Strength").
- Team news shows the headline and the event (it said
  "Team <headline>:").
- Alignment news names the deed (it printed tags like
  "castle.knighthood"), and a refused mercenary turn-in says why in words.
- A refusal line says "her eyes" or "his eyes" (it said "she eyes"); ally
  attack verbs read "whiffs at" and "DEMOLISHES"; artifact stat labels
  read "Max Mana" and "Weapon Power".
- "One of the The Crown" now reads correctly.
- The compact BBS Magic Shop menu reads "[R]eturn to street",
  "[W] Remove Ench", "[V] Love Spells", "[K] Dark Arts", "[Y] Study" and
  "[G] Scry", and every language shows each key next to a whole word.
- The catch-up summary no longer counts a word inside a longer word (for
  example "king" in "lurking").

## Gameplay fixes

- **Mailbox.** The world boss notice is no longer mailed to every recent
  player each night; it stays in the news, the live broadcast, Discord and
  /boss. Existing world boss mail is deleted once when the server first
  starts on 1.2.5. Mail from System older than 30 days is deleted at
  startup and at the daily reset; mail between players is never deleted.
  A throne challenge threat no longer mails the king; the dethroned and
  defended mails remain. A seller gets one Auction House mail per day
  (7 PM to 7 PM Eastern) that counts that day's sales. Mail previews show
  a mail of several lines on one line.
- **PvP salvage.** After a PvP win, players in every language salvage gold
  from the loser's worn weapon (30% chance) and armour (25% chance) at half
  their value. English players never did before.
- **Dialogue effects.** In the Old Gods' and story conversations, a step
  you pass through now applies its effects, not only the step a
  conversation ends on.
- **Moral paradoxes** you answer are saved with the character, so one no
  longer appears again or applies its effects a second time after a reload
  (for example the floor 95 paradox). A new character starts with none
  answered.
- **Magic Shop enchant removal** returns an item to its base form, as the
  warning says: it takes off the stat bonuses, weapon power, fire and frost
  effects and value the enchants added, and strips every enchant tag from
  the name, Phoenix Fire and Frostbite included. The enchant limit applies
  again from base. A reforge after enchanting is kept. An item stolen in
  the dormitory keeps its enchant record. Items enchanted before 1.2.5 are
  stripped only when they match their built-in template exactly;
  otherwise removal is refused and nothing is charged.
- **Curses.** Lifting a curse checks the item's curse flag, not the word
  "CURSED", and renames an older Spanish, French, Hungarian or Italian
  cursed item in its own language.
- **Inn guards.** After a player attacks a sleeper, NPC attackers now
  fight the surviving guards (they skipped them before). Attackers see the
  guard type rather than "Guard". NPC sleep attacks appear in the wake-up
  report.
- **Loot.** Drops rolled in Spanish, French, Hungarian or Italian get the
  same stat bonuses as English drops.
- **Royal quests.** A player in any language can no longer take a second
  royal quest, and "Clear a dungeon floor" quests are floor quests. Quests
  already saved keep their target.
- A prisoner using a screen reader gets the screen reader prison menu.
- Birthday, guard and marriage mail accept the answer letters each
  language shows.
- A damaged save is offered the same repair in every language.

## Stored data

These changes keep saves, trading and shared screens the same for every
reader. Data saved before 1.2.5 keeps its text.

- New loot drops, special-fight monsters (the Dark Alley pit and mugger,
  arena and Sanctum champions), boss quest champion names, hired Inn sleep
  guards, the Dark Alley enforcer, bounty board quest comments, three Inn
  NPC memories and new loot descriptions are stored in English and shown
  in each player's language.
- Because new drops are stored in English, the website and other places
  that show stored item names show English where a non-English player's
  drop used to show their language.
- Saved items keep their English names, so gear sets, enchant removal,
  trading and older saves work as before. No two items share a translated
  name.
- Answered moral paradoxes and the moral-type counters are now saved. A
  paradox answered before 1.2.5 is not recorded in older saves, and the
  counters start at zero.
- Guards hired before 1.2.5 keep their saved names, and a guard's HP is
  read under both of its saved spellings.

## Screen layout

Every message, shop, equipment, backpack and status row now fits 79
columns in all five languages, tested at the longest names and largest
numbers. Long messages, broadcasts, news, ability lines and prompts wrap
onto further rows; the combat share line stays on one row so it can be
copied whole. Login boxes are 79 columns wide. On BBS terminals a shop
page fits 24 lines.

Shop lists are tighter: columns are sized to their contents, weight tags
are shortened to [Lgt], [Med] and [Hvy], weapon types are written in full
(Greatsword rather than "Greatswo"), and a row whose stats still do not
fit continues on a second row under the bonus column. A long name in a
shop column is shortened with a period.

Weapon shop, before:

```
  #   Name                        Lvl  Pow  Type      Price       Bonus
  2. Hand Axe                   10    11  Axe            9,486  CritD+7% Str+1 [War/Bar/Rng/Sha]
```

After:

```
  #  Name                      Lvl  Pow Type     Price Bonus
  2. Hand Axe                   10   11 Axe      9,486 CritD+7% Str+1
                                                       [War/Bar/Rng/Sha]
```

Equipment and backpack rows show [1H] and [2H], stats are separated by
spaces, and a long row wraps under its text. Before:

```
  [1] Main Hand   : Staff of the Heavens (WP:55, Int:+16, Wis:+16, HP:+60) [Two-Handed]
  [B1] Staff of the Heavens - 900,000g (WP:55, Wis:+16, Int:+16) [Two-Handed]
```

After:

```
  [1] Main Hand   : Staff of the Heavens (WP:55 Int:+16 Wis:+16 HP:+60) [2H]
  [B1] Staff of the Heavens - 900,000g (WP:55 Wis:+16 Int:+16) [2H]
```

The equip screens and the status screen follow the same rules. Before:

```
  Cloak       : Cloak of Eternity [AP:28 Def:+4 HP:+100 Int:+12 MP:+100 MR:45% Str:+12 Wis:+12]
Main Hand:  Staff of the Heavens (WP:55, Int:+16, Wis:+16, HP:+60)
```

After:

```
  Cloak       : Cloak of Eternity [AP:28 Def:+4 HP:+100 Int:+12 MP:+100 MR:45%
                Str:+12 Wis:+12]
Main Hand:  Staff of the Heavens (WP:55 Int:+16 Wis:+16 HP:+60)
```

Class tags and weapon types appear in the player's language. Other rows
that now wrap or line up inside 79 columns include buff rows, the auction
list and bounty amounts, Magic Shop spell lists and
enchant fields, the Inn skills list and room gold summary, achievements,
the citizen list, the Pantheon menu, castle boxes and lists, the save
list, the who list and chat tags, event descriptions, decrees, endings,
NPC speech, combat rows, character creation, help, materials and training
rows. The mailbox key bar uses whole word labels.

## Wiki and Discord

- New guide: "Accounts and character slots" in Getting Started, covering
  online accounts and logins, the main and alt character slots and how the
  alt slot opens, what you can set before entering the game, single-player
  and BBS differences, and the server options an owner can set.
- The Discord Suggest command can trust more than one helper role: the
  server owner lists the role ids, and a member with any one of them can
  suggest.

## For developers, translators and modders

- Translators: creation.race_restricted, creation.preview.be_class and
  creation.preview.be_class_yn now take the article and the class as one
  argument in all five languages; the English shown text is unchanged.
  The two death.* keys that had no reader now hold the text the engine
  prints.
- Stored ids are unchanged: achievement ids (and Steam achievements),
  quest ids and faction ids.
- Kept but never shown: 45 NPC memory lines and 10 combat defeat reactions
  have no caller in the game. They are translated like the rest and stay
  in the data for the editor and mods.
- Release packaging: the WezTerm version lookup in the desktop and Steam
  packaging jobs authenticates with the workflow token, so it no longer
  hits the GitHub API rate limit.

## Coming in 1.2.6

A difficulty pass: smarter monsters, mixed encounters, elite monsters and
potions that scale with you. Also planned is the betrayal feature, with
its scenes reachable in play and its state saved.

## Known and unchanged

- News is stored in the language of the player who wrote it, and every
  reader sees that text.
- In group combat, a combat or spell message long enough to wrap onto a
  second row reaches a follower in the leader's language.
- Outside English, the Electron client does not show its castle
  background, because it looks for the English word in the location
  name.
- A few old mail messages whose recipient name matches more than one
  player are shown to no one.
- Chat history times are shown in UTC, and chat lines that arrive during a
  screen redraw are counted in the new-messages hint and then also printed
  right below it.
- The ascension broadcast in the endings is also shown to the player who
  ascends, and an achievement broadcast can be shown to its earner when
  their character name differs from the account name.
- The Manwe fight bonuses for answering the Stranger (+50 damage when
  defiant, +30 defence when willing) are set only by the Stranger's opening
  conversation, which is not reached in play.
- The spouse's leaving scene after 28 days is not part of this release.
- When the old session of a reconnect takes longer than 15 seconds to
  finish, the five effects listed in the 1.2.4 notes can still act on the
  new session.
