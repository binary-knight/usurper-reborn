# Usurper Reborn v1.1.10

Built from a Discord conversation: a player met Mael'Keth after floors that
went down in one or two turns, lost three companions in one fight, and
asked why the first god was a wall. That question led through the god
fights, the companions who die in them, and several smaller reports from
the same thread.

## Mael'Keth and the road to him

- **Mael'Keth is tuned so a level-30s party can win.** The live server's
  own fight log showed that no party below level 48 had beaten him: every
  recorded attempt at levels 30 to 38 died or fled. He is now 22,000 HP
  and 265 Strength, still two attacks a round. Measured with a level-32
  party built from the live winners' stats, over 100 fights: about 43
  percent wins in around 16 rounds, 0.6 companion deaths a fight, the tank
  dying in 39 percent of them. A level-25 party and a level-32 player alone
  still lose every time. He is still the first party-composition check.
- **The floors before every Old God grow tougher.** Regular monsters on the
  five floors before a god's floor have more HP and damage, +5 percent five
  floors out rising to +25 percent on the floor before the god. Defence is
  not raised, so a weaker party is warned rather than walled.
- **A god's power surge lasts a few rounds.** War Cry, Berserker Rage and
  the rest were written to raise the god's attack "for a few rounds" but
  kept the raise for the whole fight, and stacked it with every cast. They
  last three of the god's rounds now, a second cast only renews one, and a
  stun no longer pauses the countdown.
- **The intro screen shows the HP the god fights with.** It showed the
  unscaled number, 55,000 for Mael'Keth against the 123,750 of the fight.
- **Dialogue answers do what they say.** The answers before a god fight
  promised effects ("+25% damage, -15% defense, +15% crit") and delivered
  almost none: the player's bonuses were wiped as the fight began,
  penalties and crit chance were never applied, and the god's own damage
  change was read by nothing. They all apply now. An answer's effect on
  damage and defence is carried by the god for the whole fight, so it
  counts for the whole party: a damage answer shortens the god's health and
  a defence answer changes how hard it and anything it summons hit. A harsh answer can make a god
  hit at most 10 percent harder until the gods are retuned; the softer
  answers apply in full. The answer does not change the minimum damage a
  god's blow always deals, or the ticks of a curse or other status.

## Companions

- **A special attack on a companion is softened like a normal hit.**
  Monster special abilities that hit a companion skipped the protections a
  basic attack gets: the lower cap in a boss fight's opening rounds, the
  reduction for a second hit in the same round, and Shield Wall Formation.
  A tank that taunted four Gelatinous Cubes took four full Engulfs.
- **A god aiming its own ability at a companion makes its normal attack.**
  It used to become a weak poke that wasted the god's turn and never showed
  the ability.
- **Tanks protect themselves.** From level 40 a tank ally opens with the
  taunt that also protects it (Shield Wall Formation, Divine Mandate, Rage
  Challenge) rather than a bare Thundering Roar. Below that, a healthy tank
  raises Shield Wall first and taunts the next turn. A Cautious ally, told
  not to taunt, no longer taunts with those abilities either.

## Items that wait for room

- **They reach alt characters.** A world boss item that did not fit an
  alt's pack was queued under the alt but looked for under the account, so
  it never arrived, and an alt could collect its main's items instead.
- **They are never lost.** If queueing the item failed, it was gone; it
  goes into the pack past the usual limit instead.
- **New teams' bequests reach their leader.** A team recorded its founder
  by display name, which is not an alt's key and changes with a marriage,
  so when an NPC member died their belongings were queued under a key
  nobody logs in with, and the nightly cleanup deleted them. Teams founded
  from this version record the right key. Teams founded before it are
  unchanged: nothing in the saves says reliably who founded them, so they
  are not guessed at.
- **They arrive during play too.** Open /boss with room in your pack and
  they are handed over, rather than waiting for the next login, and the
  messages now say so. With a full pack you are told how many items wait,
  rather than shown a bequest that delivers nothing.

## Everything else

- **The Inn lists every patron.** It showed only the first eight, always
  the same eight, so the target of a contract could be out of reach. It
  pages now, a number counts across pages, you can type a name, and the
  targets of your own contracts are listed first.
- **Frost slows in PvP, and control can no longer chain.** Frost Touch and
  Ice Storm froze a PvP opponent solid where they only slow a monster. And
  a stun, sleep or freeze in a duel now follows the rules a monster stun
  has: nothing new lands while one holds, a fighter is immune for three
  rounds after, each hold in quick succession is shorter, and none lasts
  more than three rounds.
- **Team HQ upgrades cost what the menu shows.** The menu priced the next
  level at base cost times the level squared but charged base cost times
  the level. The shown price is the intended one.

## Tests

1,364 passing, up from 1,278. The new ones drive the Inn's patron screen,
the god fight's reset and dialogue hook, companion hits from specials and
from gods, the PvP control guards, the inheritance queue against a real
database (alts, failed writes, full packs, new teams), and the ramp.
The Mael'Keth numbers above come from a measurement of the real fight,
kept outside the test suite.
