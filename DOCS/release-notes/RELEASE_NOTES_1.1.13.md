# Usurper Reborn v1.1.13

## Main Street

- **Main Street is laid out in districts.** It shows its places (Dungeons,
  Inn, Explore, Team Corner, Dark Alley), then Merchant Row, Guild Row,
  Home & Hearth, Castle Grounds, Status, the Notice Board and, online,
  the Online hub. Each opens a short screen of its places:
  - Merchant Row: Weapon, Armor, Magic and Music shops, the Auction House
  - Guild Row: Healer, Temple, Old Church, Bank, Level Master, Quest Hall
  - Home & Hearth: Home, Lodging, Love Street
  - Castle Grounds: Castle, Challenges, Sanctum, and the Settlement once
    it is founded
  - Status: Status, Progress, Stats Record, Fame, Good Deeds
  - Notice Board: News and World Events
  - Online: Who's Online, Chat, Arena, World Boss, Guilds
- Every screen shows only the keys it uses, and every key does what it
  shows. [R] returns to Main Street. The old hidden keys are gone.
- Places still open at the same levels. A district appears once anything
  in it opens, and the first visit after a new level names what opened
  and where ("New in Guild Row: Temple, Old Church, Bank.").
- [?] on Main Street shows a map of every open place and its keys.
- Characters from earlier versions are told once that the street was
  reorganised.

## Combat and the dungeon

- **The XP lines add up.** The world event lines say they are included in
  the XP gained, one line names the other XP modifiers, and the party
  share line matches the gain.
- **Auto-combat drinks a potion at the HP share you choose** (20 to 70
  percent, default 50), set in the preferences.
- **Floors 4 to 6 are softened** on the way to full strength at floor 7.
- **A monster's stun or web follows the same hold rules as PvP:** nothing
  new lands while one holds, and a hold is followed by a short immunity,
  so they cannot chain.
- **The combat status line** shows each effect's turns left, damage per
  turn for bleeding and poison, the turns a hold costs, and whether Last
  Stand and Death's Door are ready.
- **The chest's Search for Traps works.** A trap it finds is disarmed and
  the chest keeps its treasure.
- **An invalid choice asks again** in menus and room events instead of
  sending you back to the room, and a room event is spent only once you
  choose. Room actions that cannot be used say why.
- **Keys typed during an online pause are kept** and run next; the online
  pause says "Press Enter".
- **The tutorial states the Old God rule:** you may retreat from a god's
  floor, but you cannot go deeper until the god is defeated or won over.
- **Equip Best picks for a companion's role:** defence and health for a
  tank, wisdom, intelligence and mana for a caster, wisdom and mana for a
  healer.

## Levels

- `/train` takes you to the Level Master from Main Street, or says how to
  get there. Level 10 is announced.

## NPCs

- **New NPCs no longer get Roman numerals** in their names. A name that
  is taken gets a surname, and a child whose name is taken gets another
  first name with the family surname. Existing NPCs keep their names.
- **NPC memories keep the time they happened** and fade after seven days
  as intended. Memories older than that when this version first starts
  are kept and start their seven days then.
- **Deleting a character clears NPC grudges against it and NPC marriages
  to it** again.

## The royal court

- **Every treasury and court change is saved in one step,** and a
  purchase's cost and effect are saved together; your gold changes only
  once the court's change is saved.
- **A new king is saved at once,** whether by challenge, siege, an empty
  throne, abdication, rebellion or ascension.
- A failed throne challenge keeps the guards and monsters it beat.
- Guard bonuses that would overflow are refused.
- Sales tax is credited to the treasury once, not twice.
- A sales tax set to zero stays zero.
- Dismissing or promoting a courtier acts on the one chosen, not a
  namesake.
- Bail granted by an NPC can be paid at once, and an arrest with no
  prison record can be pardoned.
- An orphan who comes of age becomes one new NPC, not a new one every
  day.
- Throne challenges face the real king, not a stand-in.

## Other fixes

- Equip Best and every equipment menu take the item you chose, not the
  first of that name, and gear moved to or from a companion is saved in
  an order that cannot copy it.
- Deleting a character keeps mail sent by a living character of the same
  name.
- Deleting a player from the web admin page runs the full clean-up.

## Self-hosted servers

- Setups that run one game process per player keep a log of world edits
  that the world-sim process re-applies, and save NPCs and the court only
  over the version they loaded. Such setups must run player processes
  with `--no-worldsim` beside one `--worldsim` process.

## Known and unchanged

- Royal petitions can spend more treasury gold than the treasury holds.
- A monarch who dies leaves a throne that a restart can restore.
- An NPC imprisoned in a street encounter is not released online.
- A very large donation can overflow Crown standing.
- A retried court change can repeat its news line.
- A sales-tax write that gives up is logged, not refunded.
- An admin delete whose game server stops mid-way stays pending, and the
  web page reports a time-out.
- On self-hosted setups with one process per player, an NPC created with
  no ID under a name removed earlier in the same session is not saved.

## Tests

1,995 passing, up from 1,731.
