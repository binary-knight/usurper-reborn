# Usurper Reborn v1.1.1

A bug pass. Five review agents each took a domain of the codebase (combat,
persistence, world simulation and relationships, locations and economy,
server and terminal) and a mechanical scan checked every localized string
against the code that formats it. About sixty-five findings came back; each
was verified against the source before it was fixed, and sixty-three are fixed
here. Nothing new to learn. Some things you were used to living with are gone.

## The ones that matter most

- **Every NPC lost its innate weapon and armor power on load in single-player.**
  The two fields were saved and restored only by the online server. Fixed, with
  the spawn formula as the fallback for existing saves.
- **The world simulator was editing the wrong player's romance data.** Every
  login on the online server replaced the fallback tracker the simulator uses,
  so an NPC death could remove a spouse or lover from whoever had logged in
  last. Sleeper attacks also consulted an empty relationship table and compared
  a character name with a login name, so a spouse or lover could murder you in
  your sleep. Both fixed; the sleeper guard now reads your own save.
- **`/restore` left a character unable to save.** The permadeath erasure mark
  was never cleared, so every save after a restore was silently refused until
  the server restarted.
- **One player's autosave starved everyone else's.** The autosave throttle was a
  single timestamp for the whole server. It is per character now.
- **An alt's permadeath discarded the main's next disconnect save.**
- **The Black Market re-rolled on every relog**, which since 1.1 meant a fresh
  guaranteed Legendary each time. The day's rotation is saved with you.
- **Nine daily counters were being played on one copy and saved from another.**
  The Player class redeclared them; the copy the game changed was never the
  copy the save wrote. Eight more daily counters were never saved at all.

## Exploits closed

- Bank robbery attempts refilled on every restart.
- High-Low dice paid 1.8 to 1, which with the obvious strategy is a 37 percent
  edge per round with no daily cap. It pays 1.2 to 1.
- Three Card Monte and Skull and Bones win chances had no cap; enough Dexterity
  or Wisdom made them certain.
- Auto-buy forwarded the city tax before you chose a hand; cancelling refunded
  the price and kept the tax, which can land in your own bank.
- Attacking a sleeping NPC took its gold twice.
- PvP disarm halved a live defender's weapon power permanently. Repeated duels
  against the king or a sleeping NPC drove it to zero.
- A monster purse above two billion gold crashed the victory roll.

## Rewards that were wrong

- Per-combat buffs (food, herbs, songs, blessings) were consumed at the start
  of a fight before they were read, so a three-combat buff lasted two and a
  one-combat buff never applied.
- Kills made before a retreat counted for nothing: no kill total, no
  statistics, no quest progress, no pet share.
- A Last Stand rescue on a mutual kill paid no victory rewards.
- Team wars loaded both fighters by display name, so a name that differed from
  the login loaded nothing, no round ran, and the challenger lost the wager.
- Listing an item on the marketplace and having it sell paid you nothing; the
  item was gone and so was the gold. Sellers now receive the gold as a bank
  transfer on their next login.
- Selling an accessory paid for the one you chose and deleted the first item of
  the same name, which could be the better one.
- Fence Stolen Goods always had nothing to fence. It reads your backpack now.
- Arm-wrestling wagers were minted on a win and destroyed on a loss instead of
  changing hands.
- PvP fights did not clear stuns and freezes at the end, so a status from one
  duel opened the next one.

## Text and terminal

- Ten messages showed a raw `{0}` or a doubled value because the code passed
  fewer values than the text expected, among them the multi-monster ranged,
  smite and soul-strike lines, the magic shop gold line, and the cursed-item
  binding line. Eight keys used in code existed in no language, including the
  True Ending and Dissolution headers. A test now scans the source for both.
- Names and chat typed on a MUD client or telnet were stored and echoed as
  mojibake for any non-ASCII letter. Input is decoded as UTF-8.
- A pasted line over a kilobyte disconnected you. It is clipped.
- BBS door telnet negotiation replies went out through an ASCII encoder and
  arrived as `??`; subnegotiations from PuTTY, SyncTERM and NetRunner spilled
  into the input line.
- Sixteen chat echo lines (you say, you shout, you tell, gossip, usage, mute,
  invite and spectate notices) were English in every language.
- A name pasted from `/who` now works in `/tell`. `/gkick` and `/gtransfer` on
  an offline member resolve the display name you typed.
- A password containing a colon locked the account out of the SSH relay and the
  desktop client. Refused at registration.

## Online server

- A MUD, web, or SSH client that closed its connection read as an endless stream
  of empty lines, so any prompt that re-asks on empty input (the combat menu, a
  loot drop, the monk's potions, the resurrection choice) spun at full CPU until a
  write finally failed. A closed peer now ends the session the way a failed write
  always has.
- Per-combat buffs are consumed when the fight ends, in a block that runs on every
  exit including a dropped connection, so a disconnect mid-fight cannot keep a buff.
- `/snoop` had no rank guard and a disconnecting snooper stayed wired into the
  target's output.
- The idle watchdog disconnected the same session every tick until its cleanup
  finished, stalling idle checks for everyone.
- NPC succession after a player abdicated built its own king record and never
  set the NPC's own flag, so the world could not tell when that ruler died.
- Marriage cleanup on startup ended live marriages whose partner was inside the
  ten-minute respawn window. Bereavement skipped a spouse who was merely knocked
  down and could pick a namesake. Revenge goals settled on the respawn flag. A
  bequeathed estate could be queued twice. A birth with an unresolvable father
  created a child that was its own father.

## Not fixed in this release

- Grouped players share the leader's ability cooldowns, and a grouped player
  who dies runs no death pipeline. Both need a design pass.
- Haggling has no entry point anywhere in the game.
- The bank's safe contents are shared across all players on a server and reset
  on restart.
- Forty-four intimacy lines in Spanish, French, Italian and Hungarian omit the
  partner's name the English carries.

## Tests

1,010 passing, up from 1,009. New: `LocalizationFormatTests`.
