# Usurper Reborn v1.2.7

## What is new in 1.2.7

- **Opt-in combat data sharing (telemetry):** the game asks once whether
  to share combat data for balancing. Single player and Steam ask at
  start; a BBS or a self hosted server asks each player after login, and
  only when its operator has turned the question on. It is off by default
  for every BBS and server. The privacy note below says exactly what is
  sent and kept.
- **Preferences** has a new line, Share combat data: On or Off, to change
  the answer at any time.
- **A monster's critical hit line** no longer leaves an empty gap where
  a damage number used to be, in all five languages (in English it ends
  "lands a CRITICAL HIT!"). The damage shows on the lines below, as
  before.
- **The balance dashboard** gains a Source choice on its Difficulty tab:
  the server's own fights, or the rows sent by copies of the game, shown
  apart and marked Unverified.

## Privacy note

This is the full privacy note. The same text is in the README.

Telemetry (opt in). From 1.2.7 the game asks once whether to share
combat data. Nothing is sent unless you say yes. With sharing on, each
monster fight adds one row of whole numbers with these values and no
others: the outcome (victory, fled or death); your class, level,
maximum HP, HP at the end of the fight, strength, dexterity, weapon
power and armor power; the monster's family, level, maximum HP,
strength and defence, and whether it was a boss; the number of
monsters and the encounter size; who acted first; the dungeon floor
and difficulty; whether you had teammates, the party size and
teammates lost; rounds; total damage dealt and taken; damage to you by
kind (basic, ability, spell, damage over time); damage to your team;
damage by you and by your team; healing you received; potions,
abilities and spells used; XP and gold gained. No player or monster
names, no chat, no save contents, no hardware ids. Each upload also
carries the game version, the kind of install (single player, Steam,
BBS door or server) and a random id.

On single player and Steam the id is made when you say yes, covers
every character on that copy of the game, and you can replace it
under Preferences. On a BBS or self hosted server the id belongs to
that server and is shared by its players; no player can replace it.
The operator must allow sharing there, each player is still asked,
and the answer is for that player's account.

The data is pseudonymous, not anonymous. Exact stat values can link
one character's fights, even across a new id, and on a small BBS or
server the rows can be told apart by anyone who knows it. Addresses
are not stored with the rows.
Rows are kept for up to 30 days, including rows sent before you turn
sharing off. Turning it off deletes anything not yet sent.

## The question

- **Single player and Steam:** the question comes once per copy of the
  game, at start, before the main menu. The answer covers every
  character on that copy.
- **BBS door and self hosted server:** each player is asked once, after
  login and the message of the day, and only while the operator allows
  it. The answer is stored for that player's account only.
- Only a yes or a no answers: the yes letter or word of your language
  (the question shows the letter), or N. Enter alone and any other key
  ask again, and another language's yes letter counts as any other key.
- No answer is never a yes. When the tries run out, the connection
  drops or the input ends, the question stays unanswered and comes back
  at the next start or login. A run with no one at the keyboard (input
  from a file or a pipe) is never asked.
- The question and the Preferences line read as plain lines in screen
  reader mode.

## Changing the answer

Preferences (`~`) has a line Share combat data: On or Off, on the key U.

- Off: U asks the question again, and only a yes turns it on.
- On, single player and Steam: U offers Turn off, or New random id,
  which replaces this copy's id.
- On, BBS door and server: U turns it off at once. The id belongs to
  the operator, so a player cannot replace it.
- On a BBS door or server the line is shown only while the operator
  allows the question.
- If an answer cannot be saved, the game says "Your choice could not be
  saved. Please try again."

## For BBS sysops and server operators

The question is off by default. Nobody on a BBS or a self hosted server
is asked, and nothing is sent, until the operator turns it on:

- **BBS door:** the SysOp console, key E (Telemetry:ON or OFF), under
  Settings.
- **Self hosted server:** the web admin's server settings, Privacy,
  "Combat Data Sharing (telemetry)", stored as telemetry_prompt in the
  server_config table. It takes effect at once; players are asked at
  their next login.

With it on, each player is still asked, and only the fights of players
who say yes are queued. Turning it off deletes the queued rows and the
id at once, or at the next start when the queue is busy.

A BBS door keeps the answers in a telemetry folder inside its save
folder (`players/<name>.json`). A server keeps them in a telemetry_consent
table in the game database, made only when the first answer is stored.
Deleting a character removes its answer, so a new character with the
same name is asked again.

The official server keeps the question off. Its fights are recorded on
the server itself, as in 1.2.6.

## How rows are sent

- A row is built when a monster fight ends, from the same values as the
  1.2.6 combat row, and written to the local queue in the background.
  The fight never waits for it, and fights play the same with sharing on
  or off.
- The queue is a file in a telemetry folder inside the save folder;
  single player and Steam keep the answer there too. Save lists ignore
  that folder, and save files are unchanged.
- The queue holds at most 2000 rows and drops rows older than 30 days.
- Uploads go in the background over HTTPS to usurper-reborn.net: once at
  start, then whenever 100 rows are waiting, at most once an hour. Never
  during a fight and never at exit. At most 100 rows go in one request.
- When an upload fails, the rows stay queued for the next try.
- The website can answer stop. A copy that gets it deletes its queue and
  sends nothing more until the game version changes.

## Balance dashboard

The Difficulty tab has a Source choice: Official (this server's own
fights), or the remote rows, all of them or by source (single player,
Steam, BBS, server). Remote rows are never mixed with the official ones.
A remote view is marked "Unverified: sent by players' copies, not
checked by the server", counts installs instead of players (a BBS or a
server sends one id for all its players) and shows the largest single
install's share.

## For developers, translators and website operators

- Translators: new keys telemetry.prompt_title,
  telemetry.prompt_body_single, telemetry.prompt_body_shared,
  telemetry.prompt_ask, telemetry.pref_row, telemetry.pref_on,
  telemetry.pref_off, telemetry.pref_turn_off, telemetry.pref_new_id and
  telemetry.not_saved in all five languages. combat.monster_critical now
  takes one value, the monster's name.
- The website code (web/ssh-proxy.js) has the endpoint POST
  /api/telemetry/v1/combat. It writes only to its own SQLite file,
  REMOTE_TELEMETRY_DB_PATH (default
  /var/usurper/telemetry/remote_telemetry.db), never to the game
  database. It never creates that file: until the file exists it answers
  a valid batch with 503 and stores nothing.
- A stop file, REMOTE_TELEMETRY_STOP_FILE (default remote_telemetry.stop
  beside the database), makes the endpoint answer every valid batch with
  stop while it exists. It is read again every few seconds, so no
  restart is needed.
- The endpoint takes a body of at most 128 KB and 100 rows, checks every
  field and its bounds, refuses a batch with any unknown field, and caps
  the rows it stores per day. Stored rows are pruned daily, by age and
  to the newest 200000.
- Tests never reach the network: the real sender is blocked in the test
  run.

## Coming in 1.2.8

A difficulty pass: smarter monsters, mixed encounters, elite monsters and
potions that scale with you. Also planned is the betrayal feature, with
its scenes reachable in play and its state saved.

## Known and unchanged

- After a victory online, the autosave can wait up to 30 seconds when the
  database is locked. The combat row does not wait. This is looked at in
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
