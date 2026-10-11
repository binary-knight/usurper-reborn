# Usurper Reborn v1.3.0 "Reckoning"

## The first step of a difficulty rework

1.3.0 is the first step of a longer rework of dungeon difficulty. In this
step monsters use their abilities and spells more, encounters on dungeon
floors 4 to 10 mix monster types and sizes, elite monsters carry traits, the
player's attacks roll to hit, and healing potions scale with the character.
Its targets were set for dungeon floors 1 to 10, measured with a combat
simulator. Elites, monster tactics, the hit roll and scaled potions are also
active on deeper floors, which have not been tuned yet.

Next steps: 1.3.1 brings tuning and fixes from play on 1.3.0; 1.3.2 covers
dungeon floors 11 to 25, 1.3.3 floors 26 to 50 and 1.3.4 floors 51 to 100.

A character whose level equals the dungeon floor will find floors 6 to 10
harder than before, most of all with a companion. The first time you enter
the dungeon the game starts you on the floor equal to your level; you can
pick a lower floor at the entrance.

## Dungeon difficulty

- Your physical attacks on monsters now roll to hit. Each swing and each
  physical single-target ability rolls a twenty-sided die and shows the roll
  and the number needed, for example "[You roll 14; you need 2 or more]".
  Against a monster at your level or up to 5 levels above you always need 2
  or more: only a natural 1 misses (a 95% chance to hit). Against a monster
  more than 5 levels above you need up to 8 or more (65%), and there your
  Dexterity and the monster's armour count. A natural 1 always misses and a
  natural 20 always hits; on a basic swing the natural 20 is the critical
  hit. A miss deals no damage and triggers no on-hit effect. Teammates and
  companions roll too. Spells, area abilities, Soul Strike, Precise Shot and
  player against player fights do not roll. The monster's evasion check
  comes first, as before.
- Precise Strike now needs 4 less on the hit roll in place of ignoring 25%
  of the monster's Defence. The roll never needs less than 2, so this
  changes the roll only against a monster more than 5 levels above you.
- Monsters use their abilities with more sense: they no longer repeat a
  status the target already has, heal or guard at full strength, or reuse an
  ability that is spent. In a party some monster families prefer a badly
  wounded target, and some prefer casters and healers; a taunt still comes
  first. From monster level 3, five early monsters have a signature ability:
  Goblin (Rally), Zombie (Enfeeble), Kobold (Poison), Spark (Burn), Animated
  Armor (Stoneskin). Old God fights are unchanged.
- Dungeon floors 4 to 10: regular rooms can hold up to three monsters (about
  one room in five), and groups mix monster families more often. Each
  monster in a group of three is weaker (80% HP, 90% damage). A mixed group
  can include one spellcaster, shown as a "Shaman" (for example "Kobold
  Shaman"). Boss rooms and floors 1 to 3 and 11 and deeper are unchanged.
- Elite monsters: from dungeon floor 3, a regular room can hold one elite
  (about 1 room in 14 on floors 3 to 10, 1 in 11 on floors 11 to 30, 1 in 9
  deeper). An elite has 60% more HP, 15% more damage and 10% more defence
  than the monster it was, gives 60% more experience and 50% more gold, and
  carries a named trait (Frenzied, Venomous, Commanding, Mending or Swift;
  two different traits from floor 51), announced when the fight starts.
  Frenzied: below half health it hits harder. Venomous: its blows can
  poison. Commanding: its allies deal 10% more damage while it lives.
  Mending: every third round it can heal a badly wounded ally in place of
  its own attack. Swift: it acts before the first round. A group of three or
  more has one monster fewer when an elite leads it. Boss rooms and champion
  encounters never have an elite.
- Healing potions heal at least 20% of the drinker's maximum HP (the old
  flat amount still applies when it is larger), so potions stay useful at
  high levels; a potion given to an ally is based on the ally's maximum HP.
- The Orc is a little weaker (90% of its former strength), easing the step
  at dungeon floor 5; floor bosses keep their strength.
- Dual-wielding: each swing uses the weapon in its own hand (the off-hand
  swing at half strength), and abilities use the main-hand weapon plus half
  the off-hand one; before, every swing used both weapons' power added
  together. This applies to players, teammates and PvP and arena opponents.
  Dual-wield now deals about 1.5 times the weapon damage of a weapon and
  shield (about 1.1 times for a Warrior from level 30); a two-handed weapon
  gives 1.45 times. The character sheet shows each hand's swing power.
- Fixed: the Gnoll bite poisoned on spell, magical ability and ranged hits;
  it now fires on melee hits only.

## Betrayal

- Betrayal is now part of play and saved with the character: the Stranger
  turns on you before her fight (no gold is taken), Lyris says her farewell
  when you choose to sacrifice her, and taking a new lover without telling
  an exclusive partner brings a heartbreak scene in town (Wisdom +3; the
  marriage and lover records are not changed).
- Characters from before 1.3.0 start with no betrayal points; story flags
  already earned are kept.
- Companions remember: murders they witness and being left to die build
  resentment, which gifts and other loyalty gains of 5 or more reduce. A
  warning comes first; if it goes on, the least loyal companion leaves in
  town (never in a dungeon or a fight), handing back their gear and keeping
  their level and your bond.
- The Temple's Faith room has a new B option to settle a betrayal: forgive a
  companion who left once you have shown kindness (loyalty gains with your
  other companions, or an atonement at the Temple costing 100 gold per
  level, at least 100, once a day), after which they rejoin at the Inn with
  loyalty 50; or forgive the Stranger, or take revenge once if she escaped.

## Teams and hired NPCs

- Hiring NPCs for your team costs less below level 25. The per-level part of
  the price now grows with the NPC's level (a level 10 NPC about 9,700 gold
  where it was about 21,700; level 13 about 16,000 from 28,500; level 19
  about 31,800 from 40,900); level 25 and higher are unchanged. An NPC above
  your own level now costs 5% more per level above you, up to 50% more at 10
  levels above; before, any NPC above you cost 50% more. The daily wage
  follows the same curve (level 10: 400 gold a day from 1,000; level 19:
  1,444 from 1,900; level 25 and higher unchanged), and NPCs already on your
  team pay the new wage from the next day.
- Team Corner: the town now keeps a pool of up to 18 low-level NPCs to hire,
  six each at levels 1 to 5, 6 to 10 and 11 to 15. New arrivals aged 18 to
  22 fill it while the town holds fewer than 190 NPCs, and NPC teams recruit
  a level 15 or lower NPC only when more than six of its level band are
  free. The hire list shows the NPCs nearest your level first. NPCs waiting
  in the pool gain levels more slowly. An NPC on your team does not age
  while it serves you; an NPC past its lifespan dies seven days after
  leaving your team.
- Hired NPCs and friends in your dungeon party now carry the oath-mark of
  your banner. New story text tells you so when you hire, in your team view
  and when someone joins your party. When one of them falls in your fight
  and will recover, the game now says the oath-mark held, in place of
  telling you they made the ultimate sacrifice. When one is lost for good, a
  short scene plays: the mark breaks and they speak a last line. The chance
  of losing a teammate for good in your own fights is unchanged, and NPCs on
  your team still cannot die while you are away.
- When your spouse, lover or affair partner fell in your fight and was going
  to recover, the game ended the marriage or romance anyway. It now ends
  only if they are lost for good.
- Throne: an NPC on your team could leave it by itself to challenge an NPC
  king, or be crowned on an empty throne as a named heir or as the last
  claimant. NPCs on a player's team are no longer picked for any of the
  three.
- Online and BBS door play: the world simulation now learns every player's
  team from the saved characters before each of its steps. Before, a team
  created in another door process, an NPC-founded team a player had joined
  (after a server restart, until that player logged in), or any team after a
  failed load was treated as an NPC team: its hired NPCs could act alone,
  die, walk out or see the team dissolved. Nothing is written to the
  database for this.

## Town, duels and quests

- Seth Able fights at the level his challenge screen shows. A win pays the
  Inn's flat reward only.
- A Seth win no longer adds the small combat reward it paid before (about 17
  XP and some gold) or a loot roll.
- Town honor duels and Inn duels share one daily cap of 3 wins.
- An NPC you beat in an honor duel, whether you challenged it in town or it
  challenged you in the street, keeps its HP and stays where it is: no
  respawn, no lost gold, no "returned from the realm of the dead" news, and
  a kill contract on it is not paid.
- The Pit's reckless side bet label says 2.5x, the amount it pays.
- In a duel, [S] Stats shows the status and asks again; it no longer spends
  the turn.
- A held fighter's line in a duel reads "You are stunned and cannot act!",
  and the cannot-act lines in duels and at the dungeon prompt name the
  status in the player's language.
- The loot comparison for a shield warns when the shield would move out the
  weapon your spells or class abilities need, shows the shield's Shield
  Bonus, and compares a new shield's own Shield Bonus and Block Chance.
- The Cure Critical and Judgment descriptions state that a cast removes
  Darkness first and adds Chivalry only with no Darkness.
- One kill or one defeat now advances each quest objective once; a boss, a
  champion or a named NPC could advance an objective twice.
- The Quest Hall quest to slay a monster type's champion is now completed by
  killing that champion in the dungeon.
- The Quest Hall's champion quest now shows the champion's own name (for
  example "Slay the Zombie Revenant"), the name it has in the fight, in
  every language.
- Spanish, French and Italian: the champion quest's title and objective no
  longer put a fixed masculine article before the champion's name.

## Dungeon and display

- Rooms you cleared on a dungeon floor stay cleared after a disconnect, a
  reconnect or a save taken on the floor, including the room of the last
  fight and a won boss.
- Dungeon encounter lines that name several different monsters ("You face 1
  A, 1 B and 1 C!" and "You encounter ...") now wrap at 79 columns; with
  four or five long names they ran past the edge of the screen.
- Online, door and console play: the wandering encounter lines ("You
  encounter ...") showed their colour codes as text, for example "[red]1
  Wolf[/]". They now show each monster group in its colour.
- BBS door sessions over a socket now show inline colours instead of the
  colour tags as text; an ASCII session shows the text alone.
- A row with an inline colour no longer counts three extra columns per
  colour when the rest of the row is laid out.

## Online play

- Online Play no longer stores your password. "Remember me" now saves a
  login token from the server, kept in your own user folder, used only for
  the server it came from; it expires after 90 days and ends when the
  password is changed or reset, or the account is banned or deleted. A
  password saved by an older version is used once for its own server and
  then erased from disk. A server older than 1.3.0 cannot remember logins;
  the game asks for the password there.
- Online Play over SSH checks the server's identity: the official server's
  host key fingerprints ship with the game and any other key is refused
  there; for other servers the key is remembered on the first connection and
  a later change is refused, with the file and line to remove if the change
  is expected.
- Logging in by hand over SSH or the website terminal no longer shows the
  raw "ERR:..." or "OK" protocol lines; the game's Online Play client still
  receives them.
- An online permadeath waits up to 10 seconds for the fight's telemetry row
  to be written before the character is erased.
- The balance records and opt-in telemetry now include fights that end in
  permadeath, an arrest or exhibition loss, or a rescue (Last Stand, Death's
  Door, Mortis's Miracle, Divine Favor).

## Website, dashboard and wiki

- The website's Content-Security-Policy is enforced (it was report-only
  since 1.2.6), with the same policy. Before the switch, every page and the
  web terminal, signed in and out, were checked with a violation observer: 0
  violations. The 1.2.6 check had read the browser console, which does not
  show these notices. The enforcing header reaches the live site when the
  server's nginx snippet is updated at release.
- Admin page: the SSL card checks the certificate and shows "Certificate
  failed validation" with the reason when it does not pass.
- A player password reset from the web admin page now saves the password in
  the game's own format; before, a player reset there could not log in.
- The dashboard password is stored only with bcrypt; an older stored hash is
  converted at the next dashboard login.
- Balance dashboard: the class charts show how many players each bar comes
  from and open on a level band (levels 1-9), with a selector for other
  bands.
- The wiki spells table shows the level at which a spell can be learned and
  cast and the mana a cast costs before Wisdom; the world boss, duel and Inn
  companion spell lists show the mana the cast charges.

## For BBS sysops and server operators

- Sysops: back up the database before upgrading. The clean roll-back is
  restoring that backup. Running 1.2.7 on a 1.3.0 database loads but loses
  betrayal state, the hired-NPC ageing grace and the remembered login. Two
  versions on one database at once is not supported.
- Sysops: the combat log table (combat_events) gains three columns when the
  server first starts on 1.3.0: player_swings, player_misses and
  elite_count. They are added automatically; rows written by earlier
  versions keep them empty. The opt-in telemetry sent from other installs is
  unchanged.
- The server log records a SAVE_SLOW warning when an autosave takes over 2
  seconds to build or write, with both durations and the calling site.
- Sysops: if the world simulation cannot read the player teams when it
  starts, it holds its steps and logs the reason until a read succeeds;
  players can keep playing meanwhile.
- The door help for --npc-xp states the accepted range 0.01 to 10.0, the
  default 5.0, and that recruit pool NPCs earn at x1.0.

## For developers

- The build pipeline's GitHub token is read-only for every job except the
  one that attaches release files.
- Code cleanup: unused stat helpers with outdated formulas were removed from
  StatEffectsSystem (no gameplay change).

## Known issues

- An NPC that has declared a challenge on a player king can still be hired
  for about a minute, and leaves the team when the challenge runs.
- A party member or companion whose name holds a word in square brackets
  (for example "[DK] Bob") shows empty lines for its hit, miss and death
  lines in a fight.
- In group combat, a combat row long enough to wrap onto a second row
  reaches a follower in the leader's language.
- Some combat rows that carry a long monster name can pass 79 columns.
- The world boss line for a turn lost to a status shows the status name in
  English in every language.
- The duel spell list prints a spell's slot number as its level.
- French and Italian: some champion names join a feminine noun to a
  masculine form (for example "Étincelle Primordial" and "Scintilla Primo").
- Losing an honor duel is an ordinary death with its penalties: online it
  uses a Resurrection and is permanent with none left, and on Nightmare the
  save is deleted.
- A win in a duel that an NPC challenged you to is not counted toward the
  shared cap of 3 duels a day.
- The Defiant ending text says Manwe is gone, and the ascension offer that
  follows has Manwe speak.
- NPC prisoners: a jailed NPC keeps acting in the world and can be met
  outside the prison while serving. An NPC's sentence counts down at each
  player's daily reset, so it ends sooner with more players, and it is not
  saved, so a server restart frees NPC prisoners.
- MUD clients: the GMCP Char.Skills.List message sends each spell's listed
  mana cost and level, not the mana a cast charges or the level at which the
  spell can be cast.
- Projected by the combat simulator and accepted for this release: deaths on
  dungeon floors 6 to 10 may run above the design ceiling of 1% of fights
  (shop gear alone about 3.5%, shop gear with one teammate about 1.3%, well
  geared alone about 1.1%); a party of three on floor 50 loses more boss
  fights than intended; elite rooms on floors 3 to 10 deal less damage than
  the design aims for; a solo Warrior in shop gear on floor 30 wins about
  2.4 points fewer regular fights.
- Hired NPCs neither die nor age in the world, so they hold their places in
  the town.
- Sysops: DOCS/DOCKER.md, DOCS/BBS_DOOR_SETUP.md and
  scripts-server/usurper-world.service give the --npc-xp default as 0.25. It
  is 5.0, as the door help says.
- Sysops and modders: a manaCost override in spells.json changes nothing a
  cast charges.
- Sysops: with several door processes on one shared database, a door process
  that does not host the world simulation still runs one of its own every 30
  seconds, and its saves put its own state of the NPCs it changed over the
  stored one. Players' hired NPCs are not affected. No switch stops it in a
  door process; --no-worldsim does not. What a sysop can do: serve every
  player from one game process, the single-process server (--mud-server,
  with sessions through --mud-relay; see DOCS/SERVER_DEPLOYMENT.md), which
  runs one world simulation.