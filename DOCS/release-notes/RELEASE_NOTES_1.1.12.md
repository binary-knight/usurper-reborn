# Usurper Reborn v1.1.12

## Awakening

- **Awakening rises in seven stages, and each rise is shown.** A framed
  AWAKENING screen appears at the next safe moment, never mid-fight or
  mid-dialogue, with a piece of the Ocean's story, the stage's gift and
  what the stage opens.
- **More of what you live through counts.** Stages are reached by points:
  a moment of awakening is worth 3, a Wave Fragment 2, and each distinct
  insight (dreams, discoveries, shrines, lore songs and others) 1. The
  stages need 4, 12, 22, 34, 48 and 62 points; the seventh also needs all
  seven seals or the truth of who you are. Insights are now saved.
- **Moments that never counted now do:** sparing a beaten foe, meeting
  Manwe, accepting Manwe's offer, refusing paradise, taking the darkness,
  hearing an Old God's lore song, and coming to accept a companion's loss.
  Recovering your memories needs four of them, the number that can be
  found.
- **Each stage gives a lasting gift:** stage 1 +3 Wisdom, 2 +5% max mana,
  3 +5% combat XP, 4 +5% max HP, 5 +3% damage dealt, 6 3% less damage
  taken, and at stage 7 each percentage gift grows by 5 more and you are
  "the Awakened".
- **A duel defender fights with the awakening gifts of their own saved
  stage,** as they already fight with their own team's HQ bonuses.
- **[P] Progress shows your stage out of 7** (it said out of 5), and [J]
  opens the Ocean Journal: points to the next stage, the fragments found
  with their lore, and the moments lived.
- **A new character is told that something in them is asleep,** and a
  one-time hint explains the Awakening line on the status screen.
- **Story chapter 1 is "The Drowning".**
- Existing characters keep at least the stage they had.

## Team Corner

- **Pick from a list:** Info, Join, Examine, Sack, Equip, Specialize and
  Resurrect show a paged list; pick by number, by name or by the start of
  a name. Examine lists player members too.
- **Team rankings load at once** (each view used to read every save once
  per team) and show each team's real power and average level, NPC and player
  members together. Teams with no members are not listed.
- **Fixes:**
  - Changing the team password takes effect; only the leader can change
    it (a team founded by an NPC checks the NPC's password).
  - A team dissolved by its last member takes its upgrades and vault with
    it, and a new team starts without any left under its name.
  - Quitting keeps the team's protection from the NPC world while a player
    is still in it, and keeps a team whose NPC members are dead for now.
  - Creating a team charges only when the team is made; names are trimmed
    and compared ignoring case, accented letters included.
  - Sack, Resurrect, Specialize and Recruit act on the current NPC after
    a world reload; Resurrect saves the result.
  - A team war left running by a disconnect ends after 10 minutes; its
    wager is refunded if no round was fought. A war pays or charges only
    when its result is saved.
  - Vault deposits cannot duplicate gold and respect the vault's size;
    an upgrade cannot be bought twice at once or past level 10.
  - Gear moved between you and a teammate is saved at once and cannot be
    duplicated. Equip Best fills every slot in one pass.
  - Teams hold five members, players and NPCs together; a dead member
    keeps their place.
  - A cursed item asks before it is equipped.
- View Inventories is on every menu; rankings and war history page;
  Resurrect and vault withdrawals ask first; Sack offers to take back the
  gear you gave; the Team Corner's text is translated.

## Other fixes

- **NPC teammates no longer attack their sleeping player.**
- The "World event bonus" line shows only a world event's share; it
  appeared on ordinary kills.
- Cursed items are marked in the backpack, and the hint for removing a
  curse names the Magic Shop.
- The dungeon's one rest per floor is kept when you log out and back in,
  and the rest screens say so.
- A one-time hint explains lives when you first enter the dungeon.
- Spellcasters see the stamina bar when they have stamina to spend.
- Gear found in the dungeon, and the dungeon merchant's wares, are
  compared with what you wear.
- The training points hint names the Level Master's key, the auto-combat
  notice says what it does, and Main Street's menu shows the world events
  key.
- Gold sent to an alt character reaches that character.
- Deleting a character:
  - clears the children and quests of a banned character too;
  - keeps mail meant for another character of the same name;
  - does not pay a later character of the same name an old war refund.

## Known and unchanged

- Two players joining a team at the same moment can take it to six.
- Two teammates taking back the same NPC's gear at the same moment can
  both receive it.
- A crash in the middle of a vault deposit loses the deposit.
- A fought team war whose result cannot be saved ends with nothing paid.
- Existing team names that differ only in case stay separate teams.
- The Spanish, French, Hungarian and Italian Awakening text awaits a
  native speaker's review.

## Tests

1,731 passing, up from 1,568.
