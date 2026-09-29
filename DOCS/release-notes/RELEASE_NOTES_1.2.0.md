# Usurper Reborn v1.2.0 "Devotion"

## What is new in 1.2.0

- **Mental Health is now a real part of the game.** Your mind wears down in
  the dungeon, in hard fights and through loss, and it recovers through rest,
  friends, prayer, learning and the Healer. Let it fall too far and you
  collapse. The full guide is below.
- **The Sage is rebuilt as the party's mind.** New crowd control spells,
  wards that cover the whole party, a mark that makes every ally hit harder,
  and Sage teammates who know how to use all of it.
- **Yes/no questions no longer guess.** A stray key or a bare Enter no longer
  counts as No; the question is asked again.
- **Single-player menus now take Enter after a key,** as online menus
  always have. One-key menus are one setting away.
- **The gods matter now.** Every character has Favor with their god, earned
  by prayer, offerings and deeds that fit the god. Favor unlocks the god's
  boon, a shield for your mind, and at the top a daily Miracle. The Temple's
  gods and player-gods share one list and compete for followers, and the Temple
  is rebuilt as rooms. The full guide is below.
- **Stat rewards now last.** Many permanent stat rewards were lost at the
  next fight, level-up or login. They now stay.

## Mental Health

Mental Health measures how well your character's mind is holding up. It runs
from 0 to 100, and every character starts at 100. Existing characters load
at 100 the first time they are played in 1.2.0. It works the same way in
single-player and online.

### The bands

| Band | Mental | What it does |
|---|---|---|
| Stable | 75 to 100 | Nothing. |
| Strained | 50 to 74 | Flavour only: now and then a new dungeon room gives you an uneasy line (3% per room). |
| Shaken | 25 to 49 | You deal 5% less damage and have 5% less defence. A new dungeon room may show a harmless hallucination (4%). Each fight has a 10% chance that fear costs you your first action. |
| Breaking | 1 to 24 | 10% less damage and defence. Hallucinations 8% per new room. A 20% chance of panic that costs your first action in a fight. Going down the stairs or through a dimensional portal asks you twice. |
| Broken | 0 | You collapse (see Collapse below). |

- **Hallucinations are harmless.** They are a line of text and nothing
  more.
- **Grief and Mental penalties do not stack.** When you are grieving and
  Shaken or Breaking, the worse of the two applies, not the sum.
- **In single-player, Mental and Fatigue together cost at most 15%** of your
  damage or defence. (Fatigue does not apply online.)

### What lowers Mental

**Dungeon depth.** Every new room you enter and every monster fight you
finish in the dungeon strains your mind, and the deeper the floor, the more.
Strain builds up in small fractions; you lose a whole point each time it adds
up to one, and the leftover carries over (it is saved with your character).
For a character with the normal 100% rate:

| Floor | Lost per 10 new rooms | Lost per 10 fights |
|---|---|---|
| 10 | 0.4 | 1.2 |
| 25 | 1 | 3 |
| 50 | 2 | 6 |
| 75 | 3 | 9 |
| 100 | 4 | 12 |

Rooms you have already explored cost nothing. Each story companion alive in
your party cuts the strain by 10%, up to 20% with two.

**Single events.**

| Event | Mental lost |
|---|---|
| Fleeing a monster fight | 2 |
| Ending a monster fight at or below 15% of your max HP | 4 |
| Fighting a floor boss or mini boss | 3 |
| Fighting an Old God (instead of the boss loss) | 8 |
| Dying in a monster fight | 12 |
| A story companion dies | 10 |
| An NPC teammate, your spouse or your lover dies | 6 |
| A grief reaches the Depression stage | 8 |
| Seeing a death or disaster (see below), once a day | 3 |
| A drug overdose | 8 |
| Drug withdrawal, each day | 3 per level of severity |
| A drug crash | see Drugs below |

- **Seeing death and disaster** means a townsperson killed in a fight you
  started, a prisoner executed at the castle, or reading the news on Main
  Street while a world disaster is active (plague, cursed land, bandit raid,
  monster invasion, demon portal, dragon sighting). This costs 3 at most once
  a day.
- **Fights outside the dungeon** (the Inn, the streets, the castle, the bank,
  the wilderness and so on) cause no depth strain, but fleeing, a near-death
  finish, a boss and dying still cost their usual amounts.
- **Duels and other player fights cost no Mental,** and neither do arrest
  fights or exhibition fights. An Old God met only in dialogue costs nothing.

### What restores Mental

Every gain prints its amount, for example "Your mind eases. (+6 Mental)".
There is no slow recovery just from standing around in town; you recover by
doing something.

| Source | Gain | Limit |
|---|---|---|
| New day | 10 | once a day, automatic |
| Dungeon camp, [R] in a cleared room | 6 | one rest per floor, shared with a Safe Haven |
| Safe Haven rest spot | 10 | one rest per floor, shared with a camp |
| Inn, rest at a table | 5 (8 if an NPC friend is there) | once a day |
| Inn, sleep (single-player) or rent a room (online) | 15 | each time |
| Home, rest | 8 | once a day |
| Home, sleep | 20 | each time in single-player, once a day online |
| Time with your spouse or lover at Home | 8 | once a day |
| Temple, daily prayer | 10 | once a day |
| Church, confession | 5 | once a day |
| Talking with an NPC friend | 5 | once a day |
| First wilderness exploration of the day | 6 | once a day |
| Learning: a new spell, a training session or reading at the Library | 4 | once a day, shared by all three |
| A grief reaching Acceptance | 10 | when it happens |
| Recovering a lost memory fragment | 1 | when it happens |
| Willow Draught, drunk in the dungeon | 20 | carry up to 3 |
| Healer, Addiction Rehab [A] | 15 | with the rehab |
| Healer, Talk Therapy [T] | back to 100 | costs gold |

- **An NPC friend** is someone you think well of: the same people the Inn
  calls your close friends.
- **Once-a-day sources are only spent when they do something.** Using one at
  your cap gives nothing and leaves it available for later that day. Paid
  ones (a rented room, confession) warn you before you pay.
- **In single-player, sleeping at the Inn or at Home also starts a new day,**
  so you get the new-day 10 on top of the sleep gain, and every once-a-day
  source is available again.
- **Time with your spouse or lover** counts for dinner, a walk or an evening
  by the fire at Home. Spouse and lover share the one daily use.

### The addiction cap

Drug addiction lowers the highest Mental you can reach: your cap is 100 minus
half your addiction. At addiction 40, for example, your cap is 80. Ordinary
recovery stops at the cap. Only two things can lift you above it: a drug high
and Healer talk therapy. Any surplus above the cap drops back to it at the
next new day. Losses always come off your current value, so being above the
cap does not protect you. Addiction Rehab at the Healer clears the addiction,
and the cap with it.

### Drugs

- **A drug lifts Mental by 8,** or 15 for Dark Essence and Demon Blood. The
  high can go above your addiction cap (never above 100).
- **When the drug wears off, you crash** for twice the high.
- **Tolerance:** each further use within 3 days of the last gives a smaller
  high (25% less per use, down to a quarter of the normal high) and a bigger
  crash (half the high more per use).
- **An overdose** costs 8 and gives no high. **Withdrawal** costs 3 per level
  of severity each day.
- **Addiction Rehab** also cancels a crash that has not happened yet and
  resets your tolerance.
- Drug messages now appear in all five languages.

### Collapse and Broken

When your Mental reaches 0, you collapse. The collapse happens as soon as you
finish what you were doing, and before the next fight in a chain of fights
(a gauntlet or tournament round, for example) starts. A fight skipped this
way does not count as fleeing.

- **In town, or on dungeon floors 1 to 25:** rescuers carry you to the
  Healer. You wake at Mental 20 with the Broken affliction, and the rescuers
  take 5% of the gold you carry. You lose no experience and no floor
  progress.
- **On floor 26 or deeper:** no one can reach you, and the collapse is a
  death under the normal death rules. You come back Broken at Mental 20.

**Broken** stays until it is treated, whatever your Mental rises to:

- You deal 25% less damage, have 25% less defence and gain 25% less
  experience. This replaces the band penalty and is not limited by the 15%
  single-player cap.
- You cannot go deeper in the dungeon, by the stairs or a portal.
- **To cure it,** use Talk Therapy [T] at the Healer. If you are addicted or
  on drugs, Addiction Rehab [A] also clears it. Rehab is only offered to
  addicted players, so for everyone else talk therapy is the cure.

- **A collapse that is due when you arrive somewhere** (after loading a save
  or reconnecting) happens before any street encounter, so a grudge or other
  encounter can no longer be lost without a fight. The Dark Alley loan
  enforcer is not fought at Mental 0 after loading a save either: the
  collapse happens and your loan, gold and health are untouched.
- **If a murder drops you to Mental 0** just before the guards arrive, the
  arrest counts as a surrender and sends you to prison without the chance of
  execution. Choosing Surrender at Mental 0 does the same. A Surrender above
  Mental 0, or a lost fight with the guards, still carries the chance.

Jailed characters and immortals do not collapse. The Noctura betrayal after
the Manwe fight always plays out; if that fight leaves you at 0, the collapse
follows the betrayal.

### The Healer

- **Talk Therapy [T]** restores Mental to 100, even above your addiction
  cap, and clears Broken. It costs (100 minus your Mental) x (10 + 2 x your
  level) gold, plus the Healer's usual tax. At level 30 and Mental 40, for
  example, that is 60 x 70 = 4,200 gold before tax. While Broken it costs at
  least as much as 10 missing points. With nothing to treat, the Healer says
  so and charges nothing.
- **Willow Draught [W]** costs twice the price of a healing potion at your
  level, plus tax. You buy one at a time and can carry up to 3. Drink one in
  a dungeon room with **U** for 20 Mental (up to your cap). If you are already
  at your cap, you keep the draught.

### Groups

- **Grouped followers take their own strain:** each follower pays for new
  rooms and for each fight's end by their own race, class and HP.
- **A follower who dies in the leader's fight** loses 12, like the leader,
  plus the fight's strain and any boss loss.
- **Camp and Safe Haven gains are shared** with grouped followers.
- **A follower who collapses** on floors 1 to 25 leaves the group and is
  carried to the Healer; on floor 26 or deeper it is their death.
- Each player's fear, hallucinations and penalties follow their own Mental.

### Race and class

Some minds are harder than others. Your dungeon strain is your race rate
times your class rate. Only strain is affected; single events and recovery
are the same for everyone.

| Race | Strain rate |
|---|---|
| Troll, Orc, Gnoll, Mutant | 80% |
| Human, Dwarf | 100% |
| Half-Elf, Gnome | 110% |
| Elf, Hobbit | 120% |

| Class | Strain rate |
|---|---|
| Assassin, Abysswarden, Voidreaver, Sage | 85% |
| Barbarian, Cleric, Paladin, Tidesworn | 90% |
| Alchemist, Magician, Ranger, Warrior, Cyclebreaker, Wavecaller, Mystic Shaman | 100% |
| Bard, Jester | 110% |

Examples: a Troll Sage takes 68% of the normal strain, a Human Warrior 100%,
an Elf Bard 132%.

### Reading your Mental

- **The status sheet** shows "Mental Health: 62/100 (Strained)", coloured by
  band. When addiction lowers your cap, a cap note is added. From Shaken
  down, an Afflictions line lists the penalty, and Broken lists its full
  effect. Willow Draughts carried are shown as "Willow Draughts: 2/3".
- **Band tags** appear in town, on the dungeon status bar and at the start of
  a fight when you are not Stable.
- **Losses print no number;** you see a line when you move to a worse or
  better band. Gains print their amount.
- The first time you drop below Stable, a short hint explains Mental Health.
- Screen readers hear the band first.
- MUD clients receive mental and maxMental in the Char.Vitals GMCP message.

### Tips

- Depth is the biggest drain. A session on floors 10 to 20 barely moves
  Mental; a long session past floor 50 needs planning.
- Camp or use a Safe Haven on each floor you clear, and carry Willow
  Draughts on deep runs.
- Spend your once-a-day sources in town: pray at the Temple, confess at the
  Church, eat at the Inn with a friend, talk to a friend, see your partner,
  explore the wilderness and learn something. Together they are worth over
  50 points a day.
- Watch the floor 26 line. Below it a collapse is a death, so turn back when
  you reach Breaking.
- Drugs buy a short lift and cost you twice as much later. Using them every
  day makes each high smaller and each crash bigger.
- If you are Broken, talk therapy is the fastest way back into the depths.

## The Sage

The Sage is now the party's mind: it blunts whole packs with soft control,
reveals weak points so every ally hits harder, and wards the whole party.
Its damage and drain spells are unchanged, so a Sage can still fight alone.
Existing Sages keep their spell levels, training and quickbar, since the new
spells use the old slots and level requirements.

### New and changed spells

| Level | Spell | What it does now |
|---|---|---|
| 1 | Fog of War | Protection for the whole party for the fight (5 + level/15, scaled by proficiency). Protection only, no longer Blur. |
| 12 | Freeze | Holds one enemy 1 to 3 rounds, under the hold rules (below). |
| 16 | Dulling Mist (was Duplicate) | Slows every enemy for 2 rounds. |
| 20 | Scholar's Mark (was Roast) | One enemy takes 30% more damage from every ally for 3 rounds. |
| 25 | Confusion | Unchanged, but half as long on bosses. |
| 36 | Slumber Mist (was Giant Form) | Puts every enemy to sleep for 2 rounds. Any damage wakes a sleeper, including damage over time. Bosses and mini bosses are immune. |
| 44 | Psychic Scream | Still damages every enemy, and now also lowers their accuracy for 2 rounds (by 5 + level/5 + Wisdom/10). |
| 47 | Shadow Cloak | Protection and Blur for the whole party. |
| 50 | Compel (was Dominate) | Every enemy is taunted onto your party's tank for 2 rounds and weakened. Old Gods resist. |
| 58 | Mind Blank | Protection and full immunity to status effects for the whole party. |
| 66 | Unveil the Pattern (was Summon Demon) | Marks every enemy for 2 rounds: 30% more damage from every ally. |
| 70 | Mass Confusion | Unchanged, but a boss is confused for at most 2 rounds. |
| 75 | Noctura's Veil | Protection and Blur for the whole party. |
| 85 | Ocean's Memory | Every ally's spells cost half mana for the fight. |
| 95 | Veloura's Embrace | Heals and wards the whole party. |

- **Compel's tank** is the living party member built to tank (by class,
  companion role or specialization) with the most max HP; with no tank, the
  member with the most max HP.
- **Bosses and mini bosses** shrug off soft control (slow, accuracy loss,
  mark, taunt, confusion) 25% of the time, and otherwise take it for half as
  long, at least 1 round.
- **Area spells that deal no damage** no longer deal a small fallback hit.

### Party wards

- **Fog of War, Shadow Cloak, Mind Blank and Noctura's Veil now cover the
  whole party:** you, your teammates and companions, and a grouped leader.
  Fallen allies are skipped.
- **Wards do not stack.** The highest ward wins, including against Cleric and
  Wavecaller wards; a weaker cast tells you the stronger ward holds. An equal
  ward only replaces one that has fewer rounds left.
- **Ocean's Memory and Blur now end with the fight.** Before, they never
  wore off. Blur now also protects teammates (20% of attacks miss them).
- **Old God seals strengthen your wards:** each seal you have collected adds
  5% to your own Fog of War, Shadow Cloak, Mind Blank and Noctura's Veil, up
  to 35% with all seven. The seal bonus also applies to Veloura's Embrace
  and to wards cast by your Sage teammates (using your seals); a grouped Sage
  uses their own seals.
- **The Settlement Library sharpens your marks:** while your Library buff is
  active (after a visit), Scholar's Mark and Unveil the Pattern make the
  target take 45% more damage instead of 30%. The spell descriptions say so.

### Sage teammates

- **A Sage teammate casts a party ward the party lacks,** strongest first,
  and does not recast one that is already up.
- **Against a pack of 3 or more,** it uses area control (Mass Confusion,
  Unveil the Pattern, Compel, Slumber Mist, Dulling Mist) when it will land
  on most of the pack.
- **Against one strong enemy** (a boss or a monster at least its level), it
  uses Freeze, Confusion or Scholar's Mark.
- It never casts Slumber Mist on a boss or Compel on an Old God.
- A teammate's Veloura's Embrace heals and wards the party.
- **Spells you disabled for a Sage teammate stay disabled** under their new
  names.

### Duels

- Slumber Mist puts your opponent to sleep, Dulling Mist slows them and
  Psychic Scream blinds them.
- Scholar's Mark, Unveil the Pattern and Compel do nothing in a duel.
- Wards cover only the caster.
- **Blinded now matters in duels:** a Blinded fighter misses 25% of weapon
  swings. This also applies to the Magician's blinding.

## Combat

- **Freeze, Magician Sleep and the Alchemist's Frost Bomb follow the hold
  rules,** as stuns and webs do, and all of them share one budget per
  monster: no recast on a held monster, at most 3 rounds, the first hold in
  a fight at full length, the second at half, the third at a quarter, then
  the monster is immune for the fight. After a hold ends the monster is
  immune for 3 rounds. Bosses resist half the time and are held for 1 round.
- **Weakening effects count once and end on time.** Wavecaller's Siren's
  Lament, Abysswarden's Noctura's Whisper, the Bard's Cutting Words and the
  weaken spell effect no longer lower a monster's base stats for the rest of
  the fight. Casting one again resets its timer instead of stacking.
- Dissonant Wave's group stun no longer lands on a frozen monster.
- **Heal Ally** now asks for the spell before the ally. A party heal
  (Veloura's Embrace) chosen there skips the ally choice, heals and wards the
  whole party, and takes effect on your turn as it does from the spell menu.
- **A heal spell that carries a ward** (the Magician's Power Hat) now wards
  its target, whether you cast it from Heal Ally or a teammate casts it.
- A teammate's party heal heals everyone it wards and no longer heals a
  fallen player.
- **Exhausted fatigue now costs 10% of experience** in single-player. It gave
  10% extra before.

## The dungeon

- **A secret boss stays until you beat it.** Fleeing, losing or dying leaves
  the boss in its chamber to challenge again. The secret counts as found when
  you win.
- **A wrong choice before a secret boss fight** makes only that fight harder.
  Before, it made the boss stronger for every player, and more so on each
  retry.
- **A secret boss pays its experience and gold once** (it paid twice). In a
  group, every member who fought and is still standing at the end has the
  chamber cleared on their own map, counts the secret and gets the reward.
- **The dimensional portal** asks twice while you are Breaking and refuses
  you while you are Broken, as the stairs do.
- **The dungeon status bar stays within 80 columns.** HP, potions, gold and
  experience move to a second line when they would run past it (common in
  Hungarian at high levels), and the fatigue, Mental and danger tags wrap the
  same way.

## Menus and prompts

- **Menu keys need Enter in single-player.** On a local console, menus now
  wait for Enter after the key, as online menus do. To go back to one-key
  menus, open Settings [~] and press E. The setting is only shown in
  single-player on a local console. Pauses still continue on a single key.
- **Yes/no questions ask again on a stray key.** Across the whole game
  (dungeon discoveries, shops, the Inn, the Church and Temple, the castle,
  the Dark Alley, the Magic Shop, home, the bank, combat, character creation
  and the rest), a key that is not yes or no, or a bare Enter, no longer
  counts as No. You see "Please answer Y or N." in your language and the
  question is asked again. A few questions still say that Enter picks an
  answer, such as accepting your rolled character.
- **Seven yes/no questions that were always in English are translated:**
  joining the Crown at the castle, the home upgrade, the Magic Shop's curse
  removal and proceed questions, and the sysop ban, unban and kick prompts.

## Characters

- **Your character creation roll is kept if you disconnect** online. The
  stats you rolled and the rerolls you have left come back when you
  reconnect, until the new character is first saved. Deleting a character
  also clears a stored roll.
- **The startup menu entry is now "Create Alt Character"** (it read "Create
  Mortal Alt Character"), in all five languages.

## Online

- **Players with a family name** (taken at marriage) or an alt character no
  longer see their own name in "Also here" at every location, and a seller
  no longer receives their own auction announcement.
- A session that drops before entering the world no longer posts a
  departure message or records a logout.
- Sleeping at home online gives its Mental gain once a day; a rented Inn room
  gives it on every rental.

## Translations

- **Prison activity results** (pushups, yoga, reading, meditation, prayer and
  the rest) now appear in your language.
- **Spell names and descriptions for every class** now appear in your chosen
  language (English, Spanish, French, Hungarian, Italian), in combat, spell
  menus, the quickbar, the spell library, training, the teammate skill
  editor and the MUD client spell list. The Sage's cast messages are
  translated too.

## Fixes

- Escaping a murder revenge attack cleanly no longer costs a fifth of your
  gold.
- Blur and Ocean's Memory no longer last past the end of a fight.
- Blur protects teammates, not only the player.
- A Mental collapse that skips a fight (the gauntlet, the tournament, the
  arena portal, Seth, a bank robbery, the throne guards) no longer prints
  flee text, and an Old God fight skipped this way counts as not fought.

## The gods

The gods of the Temple used to give a small edge and little else. In 1.2.0
your god is a relationship you build. The ten gods of the Temple (Solarius, Valorian,
Amara, Judicar, Umbrath, Terran, Mortis, Arcanus, Sylvana and Discordia) and
every player-god are one list: you worship exactly one of them, and they all
compete for followers.

### Favor

Every character has Favor with the god they worship, from 0 to 100. It is
saved with your character. Existing characters start at Favor 10 with the
god they already follow; a character with no god starts at 0 when they choose
one.

| Tier | Favor | What it gives |
|---|---|---|
| Follower | 0 to 24 | Your god's boon at one third strength. |
| Devout | 25 to 49 | The boon at two thirds, and your god's Mental ward. |
| Zealot | 50 to 74 | The full boon. Your daily prayer blessing lasts twice as long. You deal 10% more damage to the Old God your god echoes. |
| Chosen | 75 to 100 | The full boon and one Miracle a day. Townsfolk notice your god's mark. |

A line tells you whenever your Favor changes, and your stats update at once
when your tier changes.

**Gaining Favor**

| Source | Favor | Limit |
|---|---|---|
| Daily prayer at the Temple | 3 | once a day |
| Gold offering to your own god | 1 per (your level x 100) gold | 5 a day |
| Item offering to your own god | 1 to 4, by the item's value | 4 a day |
| Deeds that fit your god (below) | 1 or 2 each | 4 a day for all deeds together |
| A bless from your player-god | 2 | 2 a day |

An item offering really gives the item up (worn weapon or body armor, or a
potion); cursed and unique items are refused. Prayer, offerings and deeds
all count as devotion.

**Losing Favor**

- **Neglect:** after 3 days without devotion, you lose 1 Favor each day.
- **Taboos** (below) cost 3, 5 or 10.
- **Leaving your god** costs all of it (see Switching).
- **A chastise** from your player-god costs 5.

### Deeds and taboos

Deeds give +1 (or +2 where marked) and count toward the shared limit of 4 a
day. Taboos cost the amount shown. A player-god's followers follow the
domain their immortal chose.

| God | Deeds | Taboos |
|---|---|---|
| Solarius | undead or demons slain | drug use (3) |
| Valorian | a fight won against a higher level foe | fleeing a monster fight (3) |
| Amara | healing an ally in combat, a new friendship, marriage (+2) | murder (10) |
| Judicar | a bounty collected (+2), sending someone to prison as ruler (+2) | theft (3), murder (5), being sent to prison (5) |
| Umbrath | theft, a backstab kill, desecrating an altar (+3) | confession (5) |
| Terran | gathering herbs, giving gold to the settlement | desecrating an altar (10) |
| Mortis | witnessing or grieving a death, desecrating an altar (+3) | reciting an undead summon scroll (5) |
| Arcanus | learning a spell (+2), reading at the settlement library | a spellcaster going 7 days without casting (5) |
| Sylvana | a wilderness expedition | none |
| Discordia | an arena win against a player (+2), a street brawl won, desecrating an altar (+3) | marriage (5) |

- Killing someone who attacked you in the street is self defence, not
  murder: it costs no Favor.
- In a group fight, every living grouped player earns their own god's
  victory deeds, within their own limit.

### Boons

Each god gives its own boon. The numbers are at full strength (Zealot and
Chosen); a Follower gets one third and a Devout two thirds.

| God | Boon |
|---|---|
| Solarius | +15% damage against undead and demons, and +15% to your heals while fighting them (spells, abilities, potions, herbs) |
| Valorian | +10% damage while below half HP |
| Amara | +15% to every heal you cast, on yourself or an ally (spells, abilities, bard songs), and to the party wards you raise |
| Judicar | 10% less damage from monsters, and +20% bounty rewards |
| Umbrath | +10% critical hit chance, and better pickpocketing in the Dark Alley |
| Terran | +10% max HP, and +20% garden herbs and settlement council share |
| Mortis | 25% less gold lost when you die |
| Arcanus | +10% spell damage and +10% mana regained each round |
| Sylvana | double gold and experience from the wilderness |
| Discordia | +10% damage against players, and a 15% chance that each monster's first action fails |

- The boons work in monster fights, player fights where they apply, and
  world boss fights.
- Discordia's first action roll is made for each grouped player with the
  boon, and a monster fails its first action at most once.
- Wards and the boon changes to max HP and max mana follow you through
  logins, level-ups and gear changes.

**Player-gods.** An immortal chooses one of the ten domains for their god.
Their followers get that domain's boon, scaled by the god's standing against
the strongest Temple god (up to 120% of a Temple god's boon). It fades if the
immortal stops playing. It comes on top of the boons the immortal
configures.

### Mental wards

From Devout up, your god halves one kind of Mental loss:

| God | Halves |
|---|---|
| Solarius | boss fights |
| Valorian | near death finishes |
| Amara | grief |
| Judicar | drug withdrawal |
| Umbrath | fleeing |
| Terran | drug crashes |
| Mortis | dying, and seeing death and disaster |
| Arcanus | Old God fights |
| Sylvana | dungeon strain (cut by 10%) |
| Discordia | fear in fights (the chance is halved) |

### Miracles

At Chosen, your god grants one Miracle a day. A used Miracle stays used if
you save and reload, and is ready again after the daily reset. Call it with
**[M]** in any monster, boss or Old God fight; the key only shows when the
Miracle would help. Each grouped player has their own. Miracles do not work
in player fights or world boss fights.

| God | Miracle |
|---|---|
| Solarius | Banishes an undead or demon foe. A boss, mini boss or Old God is not banished; it takes 10% of its max HP as holy damage. |
| Valorian | Your next hit is a certain critical hit. |
| Amara | Heals you and every living ally to full. |
| Judicar | Binds a foe for two rounds (bosses resist as with other holds). |
| Umbrath | You vanish from the fight with no flee penalty (no Fame, Mental or Favor loss), even from a boss. |
| Terran | Restores you to full HP. |
| Arcanus | Refills your mana. |
| Sylvana | Calls a spirit wolf to fight at your side for that fight. |
| Discordia | Confuses every foe for three rounds (bosses resist as with Mass Confusion). |
| Mortis | Acts by itself: the first time that day you would die in a monster fight, you are left at 1 HP instead. Not in arrest or exhibition fights. |

The divine save at 1 HP that 1.1 gave every worshipper is gone; it returns
as Mortis's Miracle. Prayer lifesteal is gone too.

### Switching gods

Leaving your god costs all your Favor with it; your new god starts at 0. The
Temple shows the cost and asks before anything changes.

- **Leaving a Temple god** by choice brings its Divine Wrath, by the Favor you
  lose: level 1 below 25, level 2 from 25, level 3 from 50. Wrath can strike
  before dungeon fights until it passes.
- **Leaving a player-god** by choice brings its lightning at once: 10% to 25%
  of your max HP, by the Favor you lose.
- Leaving with no Favor brings no wrath. A change you did not make (your
  player-god is gone, or claims you) costs the Favor but brings no wrath.

### Standing and the god of the week

A god's standing is the sum of its followers' Favor, plus 5 for every
townsperson who follows it. The Temple ranks its own gods and player-gods
together by standing, with real follower counts.

- **Desecrating an altar** lowers that god's standing by 5 until the next
  weekly reset. It gives followers of Umbrath, Mortis and Discordia (and
  player-gods with the Shadow, Death or Chaos domain) 3 Favor, within the
  daily deed limit, and costs Terran's followers 10.
- **The god of the week:** at each weekly reset (Sunday at 7 PM Eastern
  online) the god with the highest standing becomes the god of the week.
  Its followers gain 5% more experience until the next weekly reset. A
  player-god can win.
- A player-god's boon strength compares players' standing only, so
  townsfolk do not weaken player-gods.

### Townsfolk faith

Every townsperson now follows one of the ten gods, chosen by their alignment
and class.

- On your first talk of the day, an NPC who shares your god warms to you a
  little; one whose god opposes yours cools a little.
- They remark on a Chosen player's mark.
- Believers convert townsfolk whose god is a weak fit for them. A weak fit
  means a god that is neither on their side nor their class's god; most
  exactly neutral townsfolk are weak fits for almost every god.

### The Old Gods

The gods of the Temple are the new gods mortals made after the Old Gods were
sealed away, pale echoes of the divine; the Old Gods themselves are the ones
you face in the dungeon. Seven Temple gods echo an Old God: Solarius (Aurelion), Valorian (Maelketh), Amara
(Veloura), Judicar (Thorgrim), Umbrath (Noctura), Terran (Terravok) and
Arcanus (Manwe). A player-god echoes through its domain. At Zealot and up you
hear an extra line before that fight and deal 10% more damage to that Old
God, with weapons and spells. At Chosen your god speaks when it falls.

### Player-gods (immortals)

- **Bless** gives your follower 2 Favor (at most 2 a day) and a combat
  blessing that follows their tier: 5% Follower, 7% Devout, 10% Zealot and
  Chosen. A bless on a townsperson gives 10%. A new bless never weakens a
  stronger blessing that is still running.
- **Chastise** is a new Divine Deed: one of your followers loses 5 Favor, at
  most once a day per follower, for one deed.
- **Smite** can no longer strike your own followers.
- **Recruit:** a townsperson whose god is a weak fit recruits as easily as a
  godless one (150 god experience); the list marks them "(wavering)". Others
  are steals.
- If the heavens cannot reach an offline follower, the deed says so and
  costs nothing.

## The Temple

The Temple is rebuilt as rooms, in line with the story: the house of the new
gods, sharing its halls with the Faith, on stones older than the Temple.

| Key | Room | What is there |
|---|---|---|
| A | Nave of the Gods | Every altar (the Temple's gods and player-gods). **W** worship or switch, **Y** pray, **O** offer gold or items, **A** the Altars screen (your Favor, tier, boon, ward, Miracle, the rankings and this week's god). |
| U | Undercroft | The altars of Umbrath, Mortis, Discordia and dark player-gods. Worship, offerings, prayer for dark gods, and **D** desecrate. |
| F | The Faith | Mirael and the oath, and the Cloister (formerly the Inner Sanctum). |
| P | Old Stones | Prophecies and visions, and the foundation stones. |
| H | Halls of Memory | The Hall of the Fallen, the Rite of Return, the Hall of the Ascended, and Aurelion's memory, each shown only where it applies. |
| M | Meditation Chapel | While Mira is there. |
| T | Deep Temple | Aurelion, shown only when his fight can start. |
| R | Return | |

- **Evil players may now enter the Temple.** The priests of the Nave refuse
  their offerings to good gods other than their own and send them to the
  Undercroft, where they are welcome. The Church keeps its ward.
- **Temple Confession is removed.** Confession is at the Church.
- **Gold offerings** can go to any Temple god's altar (with a warning when it is
  not your god) or to your own player-god; items only to your own god.
- **An old Temple key** (for example C, I, $, J, L, S, G) prints where that
  action moved.
- **Aurelion is fought in the Deep Temple,** which opens only when his fight
  can actually start (level 75 and three Old Gods faced), with the full story
  results. Dungeon floor 85 points you back to the Temple (or tells you the
  Deep Temple is sealed until you are ready), and Main Street, the Inn
  rumors, the save quest and the oracle all name the Deep Temple. A resolved
  Aurelion is remembered in the Halls of Memory. Characters who already
  resolved him are unaffected.
- Manwe no longer appears on the altars or in the rankings.
- God rank titles, epithets and descriptions appear in your language.

## Stat rewards that now last

Many permanent stat rewards were written to a value the game rebuilt at the
next monster fight, equipment change, level-up or login, so they vanished.
They now last:

- the Temple (sacrifice blessings, the Cloister), the Dark Alley (steroids,
  alchemist Intelligence, shady merchant elixir), the dungeon (Old God
  alliance Wisdom, shrine Strength), the Wilderness shrine, the Church
  wedding and town NPC stories;
- rare dungeon encounters, betrayals, moral paradox choices, the street
  romance encounter and prison activities (penalties now last too);
- artifacts: the Strength, Defence, Stamina, Agility, Charisma, Dexterity,
  Wisdom and Max HP an artifact gives (defeating an Old God grants them like
  every other way of collecting);
- the NG+ bonus of +5 Strength, Defence and Stamina per completed cycle,
  which no character ever kept before;
- the dark bargain resurrection stat loss is now permanent, as its text
  says, and the birthday Love gift is a permanent +5 Charisma (it was +500
  and vanished at the next fight).

**Rewards lost from old saves cannot be restored, except artifacts and the
NG+ cycle bonus, which are restored once at your next login.**

- Temporary effects now work during fights: the Inn ale, the evil alignment
  event and Groggo's Shadow Blessing last until your next rest; the
  settlement lockpick and smoke bomb last through your next fight. A second
  ale of the same kind refreshes instead of stacking, and every rest ends
  these effects.
- Resting no longer tops Stamina up to twice Constitution; that top-up never
  lasted into a fight.
- The time warp youth, crystal cave and aurora encounters leave HP and mana
  full at the new maximum.
- The street romance Charisma cap of 30 counts base Charisma only, not gear.

## For server operators

Two new tables are created automatically the first time this version
starts: `creation_rolls` (a character creation roll kept across a
disconnect, cleared at the character's first save) and
`god_standing_penalties` (desecration penalties to a god's standing, one row
per god and week, older weeks removed as new ones are written). The god of
the week is stored in the existing `world_state` table under the key
`god_of_week`. Nothing else is required, and no configuration or
`scripts-server/` file changed.
