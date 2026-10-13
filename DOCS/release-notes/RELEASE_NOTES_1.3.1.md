# Usurper Reborn v1.3.1

## What is new in 1.3.1

- **Loading:** a current character could be handled as a save from before
  base stats existed. The load then replaced the character's base stats with
  its saved totals and equipped an item. A save that carries base stats is
  now always loaded as it was saved. No existing character is changed by this
  release.

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
