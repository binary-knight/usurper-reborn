# Usurper Reborn v1.0.5

A polish release from a full walk through the codebase after v1.0.4: five
pieces of in-game guidance that pointed players at the wrong key, three hints
that were written but never shown, a dungeon tag that labelled monster rooms
as cleared, and a set of online fixes for messages that were silently lost.
No new systems. No save format changes.

## The dungeon labelled monster rooms CLEARED

The `dungeon.tag_danger` string, shown in the room header and status line when a
room still has monsters in it, has read `[CLEARED]` in English, Spanish, French,
and Italian since v0.60.0. It was identical to the cleared tag, so a room full of
monsters and a room you had just emptied looked the same. Hungarian had the
correct word all along. It now reads DANGER, PELIGRO, DANGER, and PERICOLO.

## Five hints pointed at the wrong key

All five languages, all five hints:

- The journal ticker at low HP said "Visit the Healer [H]". H is Home. The
  Healer is 1.
- The low-HP hint said to use healing potions with [I] in combat. I is Disarm.
  Heal is H. Both of these fired on the same Main Street screen, side by side.
- The level-up hint said "Visit your Master (M)". The Level Master is V.
- The mana hint, which every caster sees, said spells are cast with 'C'. There
  is no C key in combat. Spells are cast from the quickbar with the number keys.
  The unused first-spell hint said the same thing and is corrected too.
- The save hint said you can save "from the game menu" without saying where.
  It now names Quit from Main Street.

## Three hints that never fired, and one that was wiped

The first-level-up, inventory, and save-game hints were defined and localized
when the hint system shipped but had no trigger anywhere. Main Street now shows
them: the level-up nudge only when auto-level is off, the inventory nudge the
first time you carry anything, the save hint in single-player only. Main Street
also caps itself at one newly shown hint per visit; a first return from the
dungeon at low HP with loot could otherwise stack four boxes and push the menu
off a 24-row screen.

Declining the dungeon tutorial set the seen flag and skipped the short movement
hint, so decliners got no guidance at all. Players who accepted did not do much
better: the hint printed on their next floor-1 entry and the room render cleared
the screen before they could read it. Both paths now hold the hint for a
keypress.

## The progression roadmap was hidden below level 5

`/path` and `/roadmap` were the only way to reach the progression roadmap before
level 5 and neither appeared in the help list. They do now. Help also stops
advertising `/t` as the time alias on the online server, where chat dispatch
takes `/t` as tell first and players got a "Usage: /tell" error for following
the help text. Chat aliases are unchanged.

## Revives are visible in single-player

The status bar showed remaining lives only under online permadeath. Single-player
consumes the same counter, one free revive per death at the death prompt, but
never showed it. It now does, labelled Revives rather than Lives, because running
out in single-player means the Veil of Death penalties rather than losing the
character. It is hidden on Nightmare, which offers no revive at all. A Temple
comment claiming single-player never consults the counter was wrong and is
corrected.

## Online: messages that were silently lost

- **Tell to an offline player** printed "X is not online" and dropped the text.
  It now goes to their mailbox under the mailbox's 20-per-day cap, and multi-word
  character names resolve correctly. Unknown names get a plain error.
- **Team Corner's Send Message** printed "Message sent to team!" and discarded
  the text, with a comment saying the mail system could be integrated "here". It
  now mails every other team member, one row per member against the daily cap,
  with a live push to members who are online.
- **The live mail push never delivered.** The mailbox compose flow has always
  sent an in-terminal "[Mail]" line to an online recipient, but it looked the
  recipient up by character name where sessions are keyed by login name, so it
  reached nobody whose two names differ. Both the compose push and the new team
  push resolve the login name first.
- **Group dungeon notices** ("X has joined your dungeon group", "Following X",
  the /who location string) showed the raw account username. They use the
  character name now, as the follower footer already did.

## Online: arena gold duplication

Arena PvP fights a saved snapshot of the defender and deducts the stolen gold
from their saved row. If the defender was online, their next autosave wrote their
in-memory gold back over the deduction, so they lost nothing while the attacker
kept the gold. The eligible list now excludes players who are online, checked
against both the heartbeat table and the live session list. If the defender logs
in during the fight itself, the theft is voided and the attacker is told; the
win, XP, and any bounty still stand.

## Not changed

- The follower's screen is still cleared on every leader move. That is a design
  choice, not a bug, and it stays until there is a reason to revisit it.
- The offline tell fallback does not honour the recipient's tell mute. Mailbox
  compose never did either; the two now match.
- The arena's "online" badge in the target list is unreachable now that online
  players are filtered out. Harmless; left in place.
- Input stomping (a message arriving while you type) is not in this release. The
  cause differs by transport and any fix needs a four-client test pass before it
  can ship.

## Known issues

- The 80-column double-spacing report in issue #115 and the Mystic BBS CP437
  report in issue #102 both still need the reporter to name a terminal.

## Tests

958 passing, unchanged from v1.0.4. Every change in this release is terminal
output, localized text, or a database write behind the online server, none of
which has an input seam in the test project. Each change was reviewed twice by
inspection and verified against the full suite.

## Files Changed

**New**

- `DOCS/RELEASE_NOTES_1.0.5.md`
- `DOCS/STEAM_RELEASE_NOTES_1.0.5.txt`

**Modified**

- `Localization/en.json`
- `Localization/es.json`
- `Localization/fr.json`
- `Localization/hu.json`
- `Localization/it.json`
- `README.md`
- `Scripts/Core/GameConfig.cs`
- `Scripts/Locations/ArenaLocation.cs`
- `Scripts/Locations/BaseLocation.cs`
- `Scripts/Locations/DungeonLocation.cs`
- `Scripts/Locations/MainStreetLocation.cs`
- `Scripts/Locations/TeamCornerLocation.cs`
- `Scripts/Locations/TempleLocation.cs`
- `Scripts/Server/MudChatSystem.cs`
