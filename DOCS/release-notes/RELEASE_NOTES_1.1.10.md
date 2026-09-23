# Usurper Reborn v1.1.10

## Old Gods

- **Mael'Keth** is 22,000 HP and 265 Strength, still two attacks a round,
  tuned for a level-30s party. Measured with a level-32 party over 100
  fights: about 43 percent wins in around 16 rounds, 0.6 companion deaths a
  fight. A level-25 party and a level-32 player alone still lose.
- **The five floors before every Old God** have tougher regular monsters:
  +5 percent HP and damage five floors out, rising to +25 percent on the
  floor before the god. Defence is unchanged. God floors are unchanged.
- **A god's power surge** (War Cry, Berserker Rage, Martial Law, Absolute
  Order, Entomb) lasts three of the god's rounds. A second cast renews it
  instead of stacking, and a stun no longer pauses the countdown.
- **The intro screen shows the HP the god fights with.**
- **Dialogue answers before a god fight apply.** Their penalties, crit
  chance and god-damage changes now take effect. Damage and defence answers
  are carried by the god for the whole fight, so they count for the whole
  party: a damage answer lowers the god's HP, and a defence answer changes
  how hard the god and anything it summons hit. A harsh answer makes a god
  hit at most 10 percent harder; softer answers apply in full. The answer
  does not change the minimum damage a god's blow always deals, or the
  ticks of a curse or other status.

## Companions

- **Monster special attacks on a companion are mitigated like a normal
  hit:** the boss opening-round cap, the reduction for a second hit in the
  same round, and Shield Wall Formation.
- **A god's own ability aimed at a companion is its normal attack.**
- **Tank allies protect themselves.** From level 40 a tank opens with a
  protective taunt (Shield Wall Formation, Divine Mandate, Rage Challenge).
  Below that, a healthy tank raises Shield Wall first and taunts the next
  turn. A Cautious ally does not use these taunts.

## Items that wait for room

- **Alts receive their own waiting items.** Items are queued and looked up
  by character.
- **A waiting item is never lost.** If queueing it fails, it goes into the
  pack past the usual limit.
- **Bequests from teams reach their leader.** Teams founded from this
  version record the leader's character. For teams founded earlier, the
  online admin console has a new Fix Team Leaders screen that sets each
  team's leader to a current member, one team at a time, with a
  confirmation.
- **Waiting items are delivered on /boss** when there is room, not only at
  login. With a full pack you are told how many items wait.

## Other changes

- **The Inn lists every patron.** The list pages, a number counts across
  pages, you can search by name, and the targets of your own contracts are
  listed first.
- **PvP:** frost slows instead of freezing. Stun, sleep and freeze follow
  the monster-stun rules: nothing new lands while one holds, three rounds
  of immunity after, each hold in quick succession is shorter, none lasts
  more than three rounds, and a hold that lands always costs a turn.
- **Team HQ upgrades cost what the menu shows** (base cost times the next
  level squared).

## Tests

1,374 passing, up from 1,278.
