# Usurper Reborn v1.2.4

## What is new in 1.2.4

- **Reconnecting keeps you online.** A reconnect no longer drops you from
  /who, /tell, group invites, spectating and broadcasts while you keep
  playing, and the old session no longer saves over the new one.
- **Chat history:** /history (or /hist) shows your last 50 chat lines.
- **Homes are private.** Room chat, emotes and notices no longer reach
  players in their own homes, and "Also here" no longer appears there.
- **Group combat in your language:** a follower's turn screen, menus, round
  status and group lines show in each player's own language.
- **Magic Shop haggling** over rings and necklaces.
- **A neglected spouse writes you a letter.**
- **Mail** counts and delivers correctly for players whose account name and
  character name differ.
- **A wiki guide to immortals**, and fixes for ascending and renouncing.

## Online play

- Reconnecting no longer drops a player from /who, /tell, group invites,
  spectating and broadcasts while they keep playing. A reconnect now waits
  for the old session to finish saving (usually under a second, at most 15
  seconds), and the old session no longer saves over the new one or leaves
  a sleeper behind.
- New: /history (or /hist) shows your last 50 chat lines: tells you sent and
  received, gossip, say, shout, emote and guild chat. Filter with
  /history tell, /history gossip and so on. History is kept in server memory
  only, never saved, and lasts through a reconnect for 15 minutes. After a
  screen redraw, a line tells you how many new messages arrived.
- Homes are private: "Also here" no longer appears at home, and say, emote,
  disconnect notices and other room lines no longer reach players in their
  own homes.
- A player no longer sees their own name in "Also here" when their shown
  name differs from their character name, for example after taking a family
  name.
- Closing the connection during character creation no longer shows a "try
  again" prompt or logs a server error, and a dropped session with no
  character no longer leaves a dormitory sleeper.

## Mail and marriage

- The login summary and the inbox now count the same unread mail for
  players whose account name and character name differ.
- System notices sent to your account (team renames, divine and death
  notices) now appear in your mailbox. Each message reaches exactly one
  player.
- A spouse left alone for 21 days you were present writes you a letter. It
  arrives in the online mailbox and also shows on screen once, at the next
  screen after the day changes.
- The mail inbox row cuts long sender names to fit 79 columns.

## Group combat

- A follower's turn screen and menus, the round status, the group lines and
  the fight's opening phrase are shown in each player's own language.
- Lines about another player's attack, miss or defence are in the third
  person in every language.
- An NPC ally's victory line, including the fallback line, is shown to each
  player in their own language.
- English wording for followers: "Facing:" now reads "You are facing:" and
  "Defeated N enemies" reads "Defeated N monster(s)!", as on the leader's
  screen.
- Party rows wrap to a second row before 79 columns.

## Shops, town and the law

- Magic Shop: you can now haggle over rings and necklaces, with 3 tries a
  day like the Weapon and Armor shops. Being thrown out bars you from the
  Magic Shop until the next day.
- The Bank's robbery attempts count is kept for the day across server
  restarts.
- Murder revenge: the rage HP now goes on the fight, so the enraged
  townsperson really fights with the extra HP, and the box shows the fight's
  HP. The town NPC's own HP is no longer raised above its maximum, and stays
  wounded after a Crown guard steps in.

## Immortals

- The Pantheon status shows the believer experience multiplier the game
  really pays (x2).
- Renouncing immortality now also releases player followers, who are told.
- An alt character is refused ascension before it gives up its throne, team
  or guild.
- The alt slot earned by ascending is kept after renouncing.
- Pantheon boon rows fit 79 columns.

## Translation and text

- Dungeon text left in English in 1.2.3 is translated: the follower room
  view, item bonus labels, reward labels, skill toggle names, the descend
  message's theme, duelist cries and gear.
- French text no longer shows "&lt;" and "&gt;" in place of < and >.
- Translations and on-screen text from the game code use plain dashes and
  three dots: no em-dash, en-dash or ellipsis characters remain in any
  language. Combat log entries use "--". A few lines that grew too wide
  were shortened.

### English wording changes

- The Dark Alley standing line says "use" instead of "access".
- A street encounter line reads "considers confronting you, thinks better
  of it".
- The secret boss intro no longer reads "The The".

## Screen layout

- The level-up and dungeon entrance art fit 79 columns.
- News rows, the wilderness tame text, the merchant purchase confirm, the
  fight intro rows and battle cries wrap at 79 columns.
- In French, "!", "?", ":" and ";" stay with their word when a line wraps.
- /who no longer splits names such as "SysOp Console".

## Wiki and docs

- The wiki has a full guide to immortals: how to ascend, what changes, the
  Pantheon and renouncing.
- Wiki search no longer drops the right page when a question has extra
  words like "info".
- The modding guide lists all nine game data files and what tuning can and
  cannot do. The removed portraits document is gone.
- A design plan for the 2.0 graphical client is added.
- The editor's Export Defaults prompt states the number of files it writes.

## Known and unchanged

- The online location "Dungeon (Group: ...)" stays in English on /who and
  the website, and monster names stay in English.
- A few old mail messages whose recipient name matches more than one player
  are shown to no one.
- In group combat, some lines about another player stay in English for
  non-English followers, and monster info and NPC reactions are English.
- Chat history times are shown in UTC.
- Incoming chat lines in /history are in English, as they are when they
  arrive.
- Chat lines that arrive during a screen redraw are counted in the
  new-messages hint and then also printed right below it.
- The ascension broadcast in the endings is also shown to the player who
  ascends, and an achievement broadcast can be shown to its earner when
  their character name differs from the account name.
- The spouse's leaving scene after 28 days is not part of this release.
- When the old session of a reconnect takes longer than 15 seconds to
  finish, five things can still act on the new session:
  - a save that started before the new session registered can land after
    the new session loaded
  - group cleanup by account name can remove or disband the new session's
    group
  - the "has left the realm" broadcast is sent
  - the online tracker's shutdown removes the online row (the heartbeat
    re-registers it within 30 seconds)
  - the input-timing buffer is reset
