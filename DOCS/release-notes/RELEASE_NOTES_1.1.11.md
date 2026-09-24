# Usurper Reborn v1.1.11

## Team HQ

- **Every upgrade reaches everything it names.**
  - The Armory adds 5 percent per level to every hit a player makes, PvE
    and PvP. Effects that follow a hit (riders, reflect, damage-over-time
    ticks) are not boosted again.
  - The Barracks divides every hit an enemy lands on a player by 1 plus 5
    percent per level (level 2: about 9 percent less). Status ticks
    (poison, disease) and self-inflicted drains are not reduced.
  - The Infirmary adds 10 percent per level to what every healing potion
    restores, in and out of combat.
  - Training adds 5 percent per level to every combat XP award, once, on
    the player's own share.
- **The levels are the team's current ones.** A player who joins a team
  gets its upgrades at once, a teammate's upgrade reaches everyone within
  a minute, and a player who leaves loses them straight away.
- **The status screen always shows the team's HQ bonuses.**
- **Team rankings count the real members.** A team that no player, NPC
  or online player belongs to is removed, with its upgrades and vault, so
  a later team of the same name starts fresh.
- **Leadership passes on when a leader leaves.** When a team or guild
  leader quits, is deleted or dies for good, the highest-level remaining
  player member leads. Teams already led by a former member are fixed by
  the world save.

## Bounties and quests

- **Beating a WANTED target counts wherever it happens:** duels in the
  Dormitory, the Dark Alley and its pit, the Inn and the Arena, sparing an
  NPC who surrenders, and the Inn challenge.
- **A Crown bounty on a player is paid to whoever beats that player in a
  duel.** It is paid once, never to the target themselves, and at the
  amount posted.
- **A bounty is not lost when the database is busy;** it stays open.

## Deleting a character

- **A new character with a deleted one's name starts clean.** It no
  longer inherits the old character's quests, bounties or deity.
- **Deleting a character also ends its reign, passes on the team or
  guild it led, and removes its mail and auction listings.** Listings and
  bounties of another player or NPC with the same name are left alone.
- **The permadeath for too many deaths deletes the character that died,**
  not the account's main character.

## Rewards

- **An Old God kill pays its reward once.** The party leader's share is
  Level x 6000 XP and Level x 1500 gold, paid in two parts: the fight's
  share and the rest after the god's scene.
- **Wilderness gold finds pay on the dungeon's scale,** at the level the
  wilderness fights use.
- **The Alchemist's Potion Mastery reaches every healing potion.**
- **World boss rewards use the team's current Training level.**

## Other changes

- **A spell short of mana says how much it needs,** for example "Magic
  Missile (need 14 MP, have 6)".
- **Menu lines no longer show their key twice** (the Inn patron list, an
  item's Equip and Drop options, the Dark Alley dice, the screen-reader
  language line).
- **Winning the throne from an NPC king is not a kill,** so it does not
  complete an Assassin contract on them.

## Known and unchanged

- Deleting a character does not yet clear NPC grudges against it or an
  NPC's marriage to it; that comes in 1.1.12.
- A Crown bounty posted on a player before 1.1.11 is not paid in a duel;
  bounties posted from this version are.
- Noctura's betrayal pays both of its rewards, and secret bosses pay
  their flat XP on top of the fight, as designed.

## Tests

1,567 passing, up from 1,377.
