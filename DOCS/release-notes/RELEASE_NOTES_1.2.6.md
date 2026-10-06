# Usurper Reborn v1.2.6

## What is new in 1.2.6

- **Detailed combat logging** for the coming difficulty pass: each online
  fight against monsters now records the floor, the difficulty mode, the
  party and encounter size, who acted first, damage by kind and target,
  heals, potions, abilities and spells used, teammates lost and the HP
  left at the end. The row is written in the background, so a fight never
  waits for the database. Combat itself plays exactly as before.
- **Logins no longer show whether an account exists:** an unknown name
  and a wrong password get the same message after the same password
  check, and a banned account sees its ban only after the right password.
- **Name rule:** new character, child, team, guild and divine names can no
  longer contain < > & or a double quote. Existing names are kept.
- **The website escapes all player and sponsor text** on every page, its
  tests run in the release build, and a report-only Content-Security-Policy
  is ready for the web server.
- **The balance dashboard** gains Difficulty and Onboarding tabs, and every
  rate is counted over one stated window.

## Combat logging

Online fights against monsters write one row to the combat_events table,
as before. In 1.2.6 the row gains 18 columns:

- the dungeon floor the fight took place on, and the character's
  difficulty mode;
- the party size, and the number of monsters in the encounter;
- who acted first: the player's side or the monsters;
- damage the player took, in four columns: basic attacks, abilities,
  spells, and damage over time;
- damage the player's teammates took;
- damage the player dealt, and damage the teammates dealt;
- healing the player received;
- healing potions, class abilities and spells the player used;
- teammates lost in the fight;
- the player's HP when the fight ended.

How the row is kept:

- The row is built when the fight ends and written in the background. A
  fight no longer waits for the database, and a write that fails is
  logged at most once every 10 minutes. A row still being written when
  the server stops is lost.
- New indexes on the time of the row and on the floor keep the dashboard
  queries fast.
- Fights other than deaths are kept 30 days, and at most the newest 15000
  of them (before: 14 days or 5000 rows). Deaths are kept 90 days and never
  count toward the row limit. The cleanup runs at the daily reset.
- The old columns keep their meaning: dungeon_floor is still the
  monster's level.
- Rows from before 1.2.6 have the new columns empty. A death from a
  mental collapse outside a fight leaves them empty too, and a fight that
  was cut off before its end leaves the end HP empty.
- Single-player and BBS door games write no rows, as before.

How things are counted:

- Damage is the hit as computed, overkill included. Heals are the HP
  actually restored.
- A monster's spell-like abilities (Fireball, Lightning, Hellfire,
  Inferno and similar) count as spells. Old God abilities count as
  abilities.
- Burn and poison ticking on a monster count as the player's damage, also
  when a teammate applied them.
- Abilities used counts class abilities only. Power Attack, Backstab,
  Smite and the other basic combat moves deal ability damage but are not
  counted as abilities used.
- Potions used counts healing potions only, drunk or given to an ally.
  Mana potions are not counted. Herbs count in the healing, not as
  potions.
- Spells used counts every cast attempted, a fizzle included.
- Passive heals (regeneration, lifesteal and similar) are not counted as
  healing.

Left out of the counts:

- PvP fights (they write no row).
- Combat code the game no longer reaches.
- HP a player spends on their own abilities, spells or drugs.
- Passive heals.
- Rescues such as Last Stand, divine favour and resurrection.
- Heals after the fight has ended.

Combat plays the same with logging on: the counting only reads values the
fight has already computed, and tests run the same seeded fight with and
without it and compare the screen and every HP value.

## Logins

- An unknown name and a wrong password now show the same message: "Player
  doesn't exist or wrong password." Some login screens add a second row:
  "Type 'R' to register a new account." Both are in all five languages.
- An unknown name costs the same password check as a real account, so the
  answer takes as long either way.
- A banned account that types a wrong password gets the same general
  message. The ban notice and its reason appear only after the right
  password. Address bans are unchanged and are still checked first.
- A refused sign-up (a taken name or a rejected password) now counts
  toward the per-address login throttle, so repeated failed sign-ups wait
  out the throttle window like failed logins. A locked address cannot
  register.

## Names

New names can no longer contain the characters < > & or a double quote.
The game shows `Names may not contain the characters < > & "` in the
player's language and asks again, or creates nothing:

- character names: online display names, alt characters, new local
  characters, BBS door characters and New Game Plus characters;
- a newborn child's name (the child keeps the name the game gave it),
  and a child's new name at Home;
- a new team (no team is made and no fee is taken) and a new guild with
  /gcreate (likewise);
- the divine name chosen at ascension, and the one an admin sets with
  Immortalize;
- names typed in the offline save editor.

Existing names are kept: nothing is renamed, and characters, children and
NPCs whose names have these characters load and save as before. The rule
is defence in depth. Apostrophes and backslashes stay legal in names, and
the web pages escape every name whatever it contains.

## Website and web security

- The public dashboard, the admin page, the balance dashboard and the
  sponsor lists on the home and Steam pages now escape all player and
  sponsor text, through one shared helper (web/escape.js).
- A sponsor card without an https link shows no link and no image.
- The admin online table shows "-" for an empty address.
- The web page tests run in CI. The build and the server deploy wait for
  them, and the test files are left out of the deploy.
- A report-only Content-Security-Policy for the website is in
  scripts-server/nginx-csp.conf, included from the nginx template. It
  blocks nothing and sends no reports; anything it would block shows in
  the browser console. The deploy does not install nginx configuration, so
  it is applied on the server by hand at release.

## Balance dashboard

The balance dashboard is the server admin's view of the combat and NPC
rows.

- New Difficulty tab, now the first tab: fights in bands of 5 floors, with
  boss fights on their own row. Each row shows fights, players, win, death
  and flee rates, average rounds, 1-round wins, HP lost, damage taken by
  kind, damage dealt, heals, potions, abilities and spells used, party and
  encounter size, how often monsters act first and teammates lost. Filters
  choose the window, the class and the difficulty mode.
- New Onboarding tab: the accounts created in the last 7 and 30 days and
  how many of them created a character, reached town, won a first fight
  and logged in a second time, and the last 7 days by connection type.
- One window per rate: Since 1.2.6 (the default), Last 24 h, Last 7 d or
  Last 30 d. Every card, table and chart states its window. Since 1.2.6
  starts at the first row with a recorded floor. A window never reaches
  back past the oldest kept fight that was not a death, so rates do not
  mix the 30 day and the 90 day retention.
- The rate definitions changed, so numbers from before 1.2.6 and numbers
  from 1.2.6 on are not comparable:
  - 1-Hit Kills are victories in round 1 (before: fights of at most one
    round, deaths included).
  - The 1-Hit Deaths card is kept and counts deaths in round 1 of the
    chosen window (before: over every kept row).
  - The Players card counts players in the chosen window (before: the last
    24 hours).
  - Deaths by floor use the real floor (before: the monster's level), with
    a separate line for deaths without a floor.
  - Damage taken shows the old column as Basic Hits and the full split
    beside it.
  - A player's class is the one in their latest row.
  - The NPC tab covers 30 days, leaves goal rows out of its totals, counts
    outcome percentages over rows with an outcome, and shows Deaths per 100
    Actions and NPCs Seen (30 d).
- The dashboard refreshes every 30 s: the overview and the open tab only.
  A tab loads when it is opened.
- Tabs, the auto refresh and the saved login work again.
- A server error is shown as the server's own text, and empty values show
  blank instead of 0.
- The dashboard token is compared in constant time.

## For developers, translators and server operators

- Translators: new keys auth.err_bad_login, auth.err_bad_login_hint and
  creation.name_bad_chars in all five languages. The keys
  auth.err_unknown_username and auth.err_wrong_password are removed.
- Server log: a failed login is logged as "[MUD] Auth failed for
  '<name>': <reason>", with reasons such as UnknownName or WrongPassword.
  The password is never logged.
- The combat engine raises combat events to an optional observer
  (ICombatObserver in Scripts/Systems/CombatEvents.cs). The game sets
  none; the per-fight counts come from CombatResult.Tally.
- The combat_events columns and indexes are added when the server starts;
  the migration can run more than once.
- The web page tests run with `npm test --prefix web`.

## Coming in 1.2.7 and 1.2.8

1.2.7 is opt-in telemetry: the game asks once whether to share combat
data to help balance the game.

1.2.8 is a difficulty pass: smarter monsters, mixed encounters, elite
monsters and potions that scale with you. Also planned for 1.2.8 is the
betrayal feature, with its scenes reachable in play and its state saved.

## Known and unchanged

- After a victory online, the autosave can wait up to 30 seconds when the
  database is locked. The combat row no longer waits. This is looked at in
  1.2.8.
- News is stored in the language of the session that wrote it, and every
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
