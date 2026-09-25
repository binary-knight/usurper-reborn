# Usurper Reborn v1.1.14

## Main Street

- **The classic Main Street layout is back as a setting.** Settings [~],
  key S, switches Main Street between Districts and Classic for each
  character. Districts stays the default.
- Classic mode draws the menu of versions before 1.1.13 with the old keys,
  in the visual, screen reader and BBS modes. It differs from the old menu
  in three ways:
  - the bottom border under the visual menu is left out;
  - Old Church shows from tier 2 in the screen reader and BBS menus (it
    used to show there from tier 3), and the Settlement stays at tier 3 in
    screen reader mode;
  - hints and journal lines still name the district keys (for example
    "Guild Row [G], Level Master [V]").
- In classic mode a new level lists what opened in one line.
- The district menu shows a tip on a character's first 10 visits saying
  the classic layout is in Settings. The notice that the street was
  reorganised now says the same.

## Pauses

- **Every online pause keeps a command typed ahead** for the next prompt;
  in 1.1.13 only some did. All pauses now go through one shared pause, and
  in single-player a single key still continues.

## Combat and the dungeon

- **Engulf is not used on a target that is already held** (stunned,
  webbed, engulfed, frozen, asleep or paralysed); the monster makes a
  normal attack instead. This applies to the player, companions and
  grouped players.
- **A goblin uses its Critical Strike once per fight.** Each goblin in a
  pack has its own. Tournament and Gauntlet champions are unchanged.
- A monster's stun on a teammate follows the same hold rules as on the
  player.
- A chest or shrine from a random room event waits for your choice.
- Dungeon room text wraps at word boundaries, and the danger label is
  printed once.

## Characters

- **The alt character slot opens at level 25,** as well as for an immortal
  main or an earned slot. One alt per account, as before.
- The status sheet uses one separator and one space per label.

## Teams and guilds

- **Every way onto a team keeps the five-member limit:** Team Corner joins
  and hires, street gangs, and NPCs joining or recruiting on their own.
  Two joins at the same moment cannot make six.
- A street gang does not take a king as a member.
- A new team does not inherit the wars and siege cooldown of a removed
  team with the same name.
- A fought team war whose completion failed is settled by its stored
  score.
- A team-vault deposit saves the player and credits the vault in one step.
- Teams whose names differ only in case: the newer one is renamed when the
  server starts, and each member gets one mail about it.
- A guild with no leader passes to a member who can lead once there is
  one, including after an unban or a join. An unban from the web admin
  page passes it at once.

## The royal court

- A petition ruling cannot spend gold the treasury lacks.
- A monarch's death leaves an empty throne that a restart does not
  restore.
- An NPC imprisoned in a street encounter gets a court prison record and
  is released as usual.
- Faction standing stops at its maximum instead of overflowing.
- Sales tax from a court change that gave up is credited by the next one,
  exactly once, and survives a restart.
- Court politics news is posted once, only when the court change is saved,
  and is shown in all five languages.
- A royal decree waits while the NPC roster is loading.

## NPCs, quests and bounties

- An NPC name that already has a Roman numeral has it removed before a
  surname is chosen.
- An NPC restored from an old save with no ID gets the same ID in every
  process.
- Taking an NPC's gear needs a one-time claim, so two players cannot take
  the same piece.
- The login payout for a quest on an NPC who has died is paid once.
- A player bounty from before 1.1.11 is paid when the target is beaten in
  a duel.
- Expired quests leave the shared quest record.

## Items

- No two items share a display name in any language. French, Spanish,
  Italian and Hungarian names changed for the Greataxe; French and Italian
  for the Holy Vestments; Italian for the Archmage Staff; French for the
  Shadow Cloak.

## Online and administration

- Admin commands left running by a game server that stopped are recovered
  when the server starts. A delete whose archive has expired runs its
  clean-up from the request; a delete queued before 1.1.14 that got stuck
  says in the admin panel to delete the account again.
- Admin flags such as frozen or muted are kept by account name when a
  character is deleted and made again.
- Display names are unique in the database.
- SSH.NET is updated to 2026.0.0 for two security advisories.

## Self-hosted servers

- A world sim that loses its lock to another process pauses (no ticks, no
  saves, no final save) and tries to take the lock back on each heartbeat.
- World edits are re-applied until they are pruned after seven days.
- The world sim adopts NPC saves made by its own process instead of
  reloading them.
- On setups with one process per player, a player's team join counts only
  the NPC members its own process has loaded.

## Known and unchanged

- The Spanish, French, Hungarian and Italian text added in 1.1.14 and the
  Awakening text need a native speaker's review.

## Tests

2,210 passing, up from 1,995.
