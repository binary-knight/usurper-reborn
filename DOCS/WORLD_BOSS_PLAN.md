# Plan: the world boss, redone (1.1.4)

Written 2026-09-08 from four council seats answering one brief: the
implementing session (findings and brief), Codex (gpt-6-astra, high effort,
working from the inlined source), two Claude design agents (one on the fight,
one on participation), and the supervisor session (findings, rulings,
invariants). Raw inputs: `~/usurper/evidence/codex/wb-*-20260908.*`.

Tally rules: a ruling with a majority of the four seats ships; a single-seat
mechanic goes to "later" unless it alone answers one of the findings; the
code breaks ties; every ruling names its dissent. Every HP, damage, and XP
number in every input is derived from formulas, not measured: no live
database is on the build box. The plan names the two queries that replace
them.

## What is wrong today

The maintainer's words: "pretty hokey ... make people want to actually
participate and fix the fighting mechanics so it isn't just silly."

1. **Nobody fights together.** Each player runs a private loop against a
   shared HP number; the only shared state is HP and the phase. The raid is a
   leaderboard.
2. **Bosses cannot be killed by the population the game has.** Spawn HP is
   `BaseHP x (1 + 0.07 x online) x 2.25`: the global difficulty scale applies
   (`WorldBossSystem.cs:91`, `GameConfig.cs:263`), which the spawn comment
   calls "15%". A Malachar with two online is 462k; the Nameless Horror with
   ten is 1.9M. A level-40 Warrior lands about 135 a round and, under the
   aura plus two floored basic hits, dies in 10 to 14 rounds; potions cost the
   round. So 2,500 to 5,000 damage per session for a Warrior, 4,000 to 8,000
   for a Magician, and the v0.60 per-spawn lock allows one session. Three
   players remove 2 to 4 percent. The council's correction to the brief: it is
   death that binds, not the 50-round cap, and the factor is 25 to 50, not 10
   to 20. The alpha audit's 92 percent expiry predates the lock. **Replace
   this paragraph with two queries on the live database before tuning:**
   kills divided by spawns since the v0.60.0 deploy, and damage per session
   (`world_boss_damage.hits` counts damaging rounds, not sessions).
3. **The boss is random, not readable.** A 60 percent roll for a random
   ability from the unlocked pool, no telegraph, no counterplay; Defend is
   blind and costs the round while unavoidables still land at three quarters.
   The aura is 60 to 90 percent of incoming damage above level 40.
4. **Things that print but do nothing, or lie.** `SelfHealPercent` prints
   "regenerates" and heals nothing. `IsAoE` is read nowhere. The status screen
   says one hour and "2+ players online". `HasSpecialEffect` counts bag items.
   `GameConfig.cs:1210` describes an XP formula (`bossLevel x playerLevel x
   10`) the code never had (`bossLevel x 10`). The "8 hour cooldown" is
   measured from the previous start. Precise Strike rolls the crit twice.
5. **Rewards are flat, small, and unnormalised.** 400 to 2,400 XP at any
   level; with three participants everyone is top 3 and gets Epic; loot rolls
   at the boss's level; a level 80 out-damages a level 15 ten to one for the
   same effort, so tiers collapse and low levels are excluded by the same
   cause.
6. **Timing is invisible.** No schedule, no notice, nothing to offline
   players or Discord, a random moment in an eight-hour cycle.
7. **The party stays at the door.** Companions, NPC teammates, and grouped
   players cannot join.
8. **The world does not care.** No effect while alive, no consequence when it
   leaves.
9. **Accounting defects** (Codex, confirmed): damage is credited to the
   leaderboard even when the boss row is no longer active; overkill is
   credited in full; a session can overwrite the phase JSON with a stale copy;
   offline non-killers get XP, gold, and mail but no item, fame, statistics,
   or achievements; the killer gets no mail. There are no world boss tests.

## Rulings

### 1. A scheduled boss (four seats)

- **8 PM Eastern, daily, whether or not anyone is online** (three seats;
  Codex said 7:30). At the 7 PM reset the tick picks *tomorrow's* boss and
  writes it to `world_state` (`world_boss_schedule`: definition, level,
  spawn UTC, window, carried boss id and HP), so there is a 25-hour countdown.
  The online-count trigger and the start-based cooldown go.
- **Window: 3 hours** (starting; council decision 2). Log attendance by
  Eastern hour and weekday; a European window waits on that log (council
  decision 1).
- **Pick** by the median level of players active in the last seven days
  (`json_extract(player_data, '$.player.level')`, pattern at
  `SqlSaveBackend.cs:2764`), excluding bosses whose base level exceeds the
  median by more than ten (starting); boss level = max(base, median). A
  level-15 cohort no longer meets the Horror.
- **Notice chain.** At the reset: news, Discord (`DiscordBridge.QueueSystemEvent`),
  and in-game mail to everyone who logged in during the last seven days
  (bounded; `SendMessage` exists; broadcast mail to `*` is filtered at login,
  `GameEngine.cs:4374`). One hour before: broadcast and Discord. At spawn:
  the existing broadcast, news, Discord. Phase, kill, withdrawal, and the
  final outcome: news and Discord. Every notice is logged.
- **Town line.** The five-minute "rampaging" reminder becomes one status line
  from an in-memory snapshot the tick refreshes: before spawn a countdown
  naming the boss; during, HP percent, heroes fighting, time left; relative
  time only (the terminal does not know the player's zone). `/boss` with no
  boss shows the countdown. The three stale keys are corrected in five
  languages.

### 2. The fight: telegraphs, focus, and a self-describing row

The private loops stay. The 30-second tick becomes the boss's clock and the
only writer of boss state; players write single columns with guarded
`UPDATE ... WHERE` preconditions, the shape the kill claim already uses
(`SqlSaveBackend.cs:6296`). The player-side `CheckPhaseTransition` and the
blind `UpdateWorldBossData` overwrite go; phase may lag a tick.

- **Telegraphs.** Every 3 ticks (starting) the tick takes the next ability
  from a fixed per-phase cycle of the boss's existing abilities (no random
  roll) and writes `telegraph_id`, `telegraph_seq` (monotonic), and
  `telegraph_lands_at` (issue plus 2 ticks). A round shows it with its
  answer and its cost: "Tidal Surge lands in 40 s. Brace: 10 percent of your
  HP, no Slow. Take it: 30 percent and Slow." Damage on landing is a percent
  of the player's max HP (10 answered, 30 unanswered, starting; Codex's
  ceilings, the supervisor's percent rule), which is what makes the fight
  readable at 15 and at 80. Nobody reacts in seconds: the answer is chosen on
  the player's own round, and `lands_at` is a floor, not a deadline.
- **Two kinds.** *Strike* telegraphs (damage or status) are personal: Brace
  costs the round. *Channels* (today's `IsUnavoidable` abilities, and every
  `SelfHealPercent` ability) are shared: `interrupts_done` is incremented by
  `UPDATE ... WHERE telegraph_seq = @seq AND interrupts_done <
  interrupts_needed`, with `interrupts_needed = min(2, engaged)`, so a solo
  can still break one and two together break it faster. Interrupt costs the
  round and has no roll (two seats to one). Broken: the boss is **staggered**
  for 2 ticks and the per-round damage cap below is x1.5, the "opening" two
  seats asked for. Not broken: it lands on everyone engaged (30 percent,
  halved for anyone who Braced), and a heal channel restores
  `SelfHealPercent` of max HP through `UPDATE current_hp = MIN(max_hp,
  current_hp + @n)`. That is what "regenerates" now means.
- **Scope and idempotence** (supervisor's shape, which also satisfies Codex's
  "re-entry resumes state"). The damage row carries `engaged_since_seq` (set
  on entry) and `last_resolved_seq`. A round resolves every seq above
  `last_resolved_seq`, at or above `engaged_since_seq`, whose `lands_at` has
  passed, once, and advances the column. A player who retreats before a
  telegraph lands and comes back after it still takes it. A player who
  arrives mid-telegraph sees it with its countdown and may answer.
- **Engaged** = a human damage row with `last_hit_at` within two minutes
  (column exists, `SqlSaveBackend.cs:6272`). `interrupts_needed` and focus
  read that count; the in-memory sets go.
- **Focus** (supervisor; kept as the one single-seat mechanic because it is
  the only one where the boss answers who is present, finding 1). The tick
  sets `focus_player` to the top `window_damage` (a column players add to
  with the same UPDATE that adds `damage_dealt`; the tick zeroes it and
  `window_started_at` every two ticks, so a write after the zeroing belongs
  to the next window) unless a **Challenge** action took it (guarded
  update, 60-second hold). Basic attacks hit the focused player at x1.5 and
  everyone else at x0.5 (starting). The top dealer draws the boss; a tank can
  pull it off a caster on purpose; alone means always focused.
- **Basic attacks.** One per round (two in phase 3) at stat damage through
  the existing formula, floor 20 percent of strength, times the focus
  multiplier. The 60 percent ability roll goes; the telegraphs carry the
  lethality the aura carried. (The count reduction and the aura's deletion
  are milestone A; the focus multiplier and the roll's removal are B.)
- **The screen.** Header (HP bar or plain percent, phase, stagger timer),
  your line, "Fighting now" capped at five names plus "and N more", the
  telegraph line with its answer and count, a one-line menu replacing the
  seven-row box, then results. Under 20 rows including the roster;
  screen-reader mode prints the same lines without the bar. Every line is a
  loc key, including the four hardcoded lines at `WorldBossSystem.cs:341`.

### 3. The boss meets each player at their level; no lock, no aura

- **Normalisation** (Codex's class-neutral reference; the participation seat
  required it as a hand-off; settled one-sided by council decision 5). Let `N(L) = 2L + 1.5 L^1.05`, the at-level monster strength formula at
  `MonsterGenerator.cs:254` taken on the raw level (that site feeds it a
  soft-capped level above 50 to stop deep-floor one-shots, which does not
  apply to a ratio), and `r = N(playerLevel) / N(bossLevel)`, frozen per
  session from the `player_level` column at entry and never recomputed.
  **One-sided** (council decision 5): `r` applies only when the player is
  below the boss's level. For that player's rounds the boss's strength and
  defence are multiplied by `r`, and the player's native damage is divided
  by `r` before it is written to the row; a player at or above the boss's
  level fights it raw under the per-round cap, so level keeps its edge at
  the top and the cap bounds it. Pinned values for the budget test: r(15 vs
  40) = 0.37, so a level 15 lands (70 - 18) / 0.37 = 142 applied against
  the level-40 reference's 120; a capped player does at most 1.35 x
  PerPlayerBudget per window. A level 15 who hits like a level 15 lands the same
  wound as a level 80 who hits like a level 80; better gear and a stronger
  build still land more, because native damage above the at-level norm is
  preserved. Without this a level 20 hits the Iron Titan for 1 to 3 (the
  mechanics seat's own table) and the reward formula would pay effort the
  player cannot deliver.
- **Per-round cap: 0.6 percent of max HP** on *applied* damage, after the
  division by `r` (starting), x1.5 while staggered.
  This is the class-gap fix: a level-80 Magician's 5,700 per cast (about 22
  times a Warrior's round) becomes the cap; a Warrior is under it.
- **HP is a kill budget, not a population multiplier** (four seats). `MaxHP =
  3 x PerPlayerBudget(bossLevel)` with `PerPlayerBudget(L) = (10 + 3L +
  min(82, L) - bossDefence / 2) x 1.8 x 75` (supervisor's derivation: a
  reference at-level fighter, an ability factor, 75 rounds in a window).
  Derived: 42.5k at 35, 48.6k at 40, 101k at 80; caps of 255, 292, 608 a
  round. No 2.25, no online count: someone logging into town must not make
  the fight harder. The participation seat's data-driven budget (measured
  damage per hour over the last ten bosses) replaces the constant once the
  log has ten bosses.
- **No share cap, no round budget** (supervisor; the advisor's discriminator:
  the v0.60 exploit was free chip damage against a boss that never healed).
  A determined solo on a quiet night is the intended path. What answers the
  exploit is **Rally**: after ten minutes with no damage the tick regenerates
  0.5 percent of max HP per tick (starting), only while `status = 'active'`
  (the nightly 20 percent is the only regeneration between windows), with
  the tick's phase write as `phase = max(current, threshold phase)` so HP
  climbing back over 65 percent leaves phase 2 in place; and the nightly
  carry-over regeneration below. No row cooldown can trigger Rally (two and
  five minutes against ten), so a working solo never sees it. Codex's 60 percent share cap and the
  mechanics seat's 150-round budget are the dissent; both would make quiet
  nights unkillable again.
- **Sessions.** The 50-round boundary stays as a rest point (summary,
  autosave). Re-entry two minutes after a retreat or a rest, five after a
  fall (starting), as `cooldown_until` on the damage row; `_deathCooldowns`
  and `_engagedThisSpawn` are deleted, not extended. A fall stays non-lethal
  at 25 percent HP.
- **Carry-over** (three seats). An unkilled boss withdraws at window end with
  its HP (`status = 'withdrawn'`), returns at the next schedule regenerated
  20 percent (starting), up to three nights, then leaves. The tick
  reactivates a withdrawn row before spawning a new one; the damage table
  accumulates under one boss id with `night_damage` zeroed at reactivation.
  Contribution `s` is per boss for the kill and per night for withdrawal
  pay; the contributor count is per night. Withdrawal pay is one guarded
  write, `UPDATE ... SET paid_nights = paid_nights | @bit WHERE (paid_nights
  & @bit) = 0`, with the row count as the pay signal, so a crash cannot pay
  twice.

### 4. Rewards: effort, at the player's level, delivered the same online or offline

- **Contribution** `s = min(1, appliedDamage / PerPlayerBudget(bossLevel))`,
  per player, per boss. Qualified when `s >= 0.1` (starting).
- **XP** = `450 x L^1.5 x (0.25 + 0.75 s)` at the player's level `L`, capped
  at half the player's next-level cost (starting; the participation seat's
  formula, derived from 15 x L^1.5 per monster at about thirty fights an
  hour, so a full contributor earns about an hour in the dungeon). The
  supervisor's alternative, the lost `playerLevel x bossLevel x 10 x s`, pays
  6.5 percent of a level at 80 and is not taken.

  | Level | Full-effort XP | Next level costs | Share | Today (MVP) |
  |---|---:|---:|---:|---:|
  | 15 | 6,400 (cap) | 12,800 | 50% | 1,050 |
  | 40 | 42,000 (cap) | 84,050 | 50% | 1,200 |
  | 80 | 322,000 | 984,150 | 33% | 2,400 |

- **Gold** = `150 x L^1.5 x (0.25 + 0.75 s)` (starting: 8.7k, 38k, 107k).
- **Together bonus** x `(1 + 0.1 x (contributors - 1))`, cap x1.5
  (supervisor, starting), on XP and gold.
- **No rank multipliers** (three seats). MVP is recognition: the news line,
  the achievement, and Legendary only when three or more humans fought.
  **Items by score, not rank:** `s >= 0.75` Epic, `>= 0.5` Rare, `>= 0.25`
  Uncommon, else Common (starting), rolled at the player's level. Fame 15 for
  every qualified player, 10 more for the MVP. `HasSpecialEffect` reads
  equipped items only.
- **Not killed:** at each withdrawal, 25 percent of the night's share
  (two seats), mailed, once per player per night.
- **Settle** replaces the killer-terminal `DistributeWorldBossRewards`:
  a tick-callable `SettleWorldBoss(bossId, outcome)` that writes a reward
  row per qualified player (frozen amounts and item, `settled` flag), then
  delivers: online through the owning session at a save point; offline
  through `pending_inheritance` for the item (`SqlSaveBackend.cs:715`,
  delivered by `GameEngine.cs:4190`) and `AddXPToPlayer` for the rest, with
  statistics, fame, and achievements credited either way. Delivery flips
  `delivered` by `UPDATE ... WHERE delivered = 0` keyed to the row count;
  `settled` protects settle, not delivery. The killer's
  session does not own payout; the tick finishes an unsettled boss after a
  crash. Damage is credited only when the boss update touched an active row,
  clamped to remaining HP.
- **1.2 hook:** `marks` on the reward row (two seats; the supervisor's
  `PlayerData` field is the dissent, and a save field with nothing to spend
  it on is not added in a patch). The faction vendor reads it.

### 5. The party waits; grouped players ride free (four seats)

Companions and NPC teammates are not in 1.1.4: the loop is bespoke, and an
ally needs targeting, non-lethal defeat, healing, stance, ownership, and
disconnect rules before its damage can be trusted. Later slice: companions as
an abstracted contribution under the owner's row and cap, one interrupt
attempt per channel, no HP of their own. NPC teammates never; the tick runs
their lives. Grouped players already have rows and loops: they are named
first on the roster and gain focus and interrupts for free.

### 6. The world, lightly

On a kill: a realm-wide +10 percent XP for 24 hours through the existing
world-event bonus path (supervisor, starting) and a news line naming the
heroes. While alive: nothing to prices (two seats to one). After three nights
unkilled: the treasury pays a 5 percent ransom (two seats to one; the King is
a player with a reason to rally) and a news line records who stood. Nothing
touches dungeon monsters or shops in a patch.

## Deviations recorded during milestone A (2026-09-08)

- The item is not serialized at settle; the reward row freezes the rarity
  and the item is rolled once at delivery, at the player's level and class.
  Settle runs on the tick with no Character in hand, and the class-fitted
  generator needs one. The delivered flag still makes it once.
- Marks live on the reward row only; no `PlayerData` field until 1.2 has
  something to spend them on.
- The next evening's schedule is written the moment a boss ends (window end
  or kill), about 21 hours of notice, instead of a daily-reset hook.
- Every world boss row is keyed by the login name (Name1, lowercased), the
  same key the session table, mail, and the inheritance queue use; the
  display name is stored beside the row for showing.
- Delivery flips the row first and then saves the player unthrottled in the
  same method.

## Deviations recorded during milestone B (2026-09-08)

- Battlefield-wide (`IsAoE`) abilities are channels beside the unavoidable and
  healing ones; without that, Void Colossus and Nidhogg never channel.
- A telegraph live at the window's end expires with it: the withdraw marks it
  resolved and clears the stagger and the focus, so nothing lands at the next
  evening's door. Entering and answering count as engaged (`last_hit_at`),
  so a player who only braces is on the roster and in the interrupt need;
  leaving clears it, so a player who retreated is not.
- Which bosses channel when (strikes/channels per phase, cycles cumulative):
  Leviathan 2/0, 1/1, 1/1; Void Colossus 2/0, 1/1, 1/1; Malachar 2/0, 1/1,
  1/1; Crimson Wyrm 1/1, 1/1, 1/1; Vareth 1/1, 1/1, 1/1; Iron Titan 1/1, 1/1,
  1/1; Nidhogg 2/0, 1/1, 2/0; Nameless Horror 2/0, 2/0, 0/2. Every boss keeps
  a strike in phase 1, so Brace never disappears; every boss has a channel by
  phase 2 except the Nameless Horror, whose first channels (Cosmic Horror,
  Devour Reality) come under 30 percent.
- The seq a player "left in" is read from the boss row at session end, not
  from the session's last round, so a telegraph issued while they sat at the
  prompt and then retreated still follows them.
- Interrupt is one transaction: the shared counter and the player's mark,
  both or neither, so nobody counts twice and nobody counts unmarked.
- A retreat drops a Challenge hold the retreating player held, so a
  challenge-and-retreat cannot keep everyone else at half damage.
- An interrupter is halved on a channel that still lands, the same as a
  bracer: the answer cost the round either way.
- Resolve and its event are two writes; a crash between them loses that
  landing and its heal (the lenient direction).
- Focus is empty until the first sixty-second refresh, so with two or more
  fighting everyone is off-focus at half damage for the first minute.
- Resolved telegraphs are remembered in `world_boss_events` (kind
  `telegraph_resolved`, the seq, and `id|kind|outcome|done/needed|engaged`);
  the loop reads outcomes back from there. No new table.
- Re-entry: the damage row records `engaged_until_seq` when a session ends
  (the telegraph live, or last, at that moment). A returning player takes at
  most that one landing; nothing issued during the absence is theirs. The
  plan's "still takes it" is bounded to one, so a player gone twenty minutes
  does not meet six landings at the door.
- The player's answer is on their damage row (`answered_seq`, `answer_kind`),
  guarded on the seq. Interrupt writes the shared counter first and the
  player's row second, so a crash between the two undercounts the player,
  never the channel.
- A landing never applies a status that stops the player acting (stun,
  freeze, paralysis): a player who cannot act cannot answer the next one.
  Other statuses apply only to an unanswered landing, with the old resist
  roll.
- A heal channel that lands heals the pool once, in the tick, and costs the
  players nothing; its small damage multiplier is ignored.
- The tick issues only while at least one human is engaged, never over a
  live telegraph, and one gap after the last landing: sixty seconds to land,
  thirty of gap, sixty of stagger, so a telegraph every three or four ticks.
  A strike needs no interrupts; a channel needs min(2, engaged).
- Focus is refreshed in one transaction every sixty seconds: the top window
  damage among engaged humans takes it unless a Challenge holds it; the
  window's damage is then zeroed. Challenge holds sixty seconds. Focus is
  last-writer-wins by design. Alone means focused, from the engaged count,
  not the column.
- A stagger raises the per-round cap by half; it does not stop the boss's
  basic attack. The boss's own action is a basic attack only, twice in its
  last phase.
- Other fighters see a player's Brace, Interrupt, and Challenge, and the
  boss's stagger, as a line at their next prompt, in their own language.

## Later, by the same council

Cover (Codex), immunity windows by phase (mechanics), per-boss hand-authored
telegraph scripts, an enrage against the window, companions as above, the
NPC rally trickle (participation; it would let a solo plus NPCs exceed the
budget), siege prices, the world boss currency and vendor (1.2), a trophy in
the Player District (1.4).

## The 1.1.4 slice, in two milestones

**A. Killable and known.** Schedule, pick, notice chain, town line, `/boss`
countdown, loc fixes; HP budget; normalisation and the per-round cap; lock
and aura deleted, cooldowns on the row, the 50-round rest; the boss's
actions cut to one per round (two in phase 3) with the random ability roll
kept and unavoidables capped at 30 percent of the player's max HP
(starting), the same ceiling B's telegraphs use; Rally; carry-over; rewards,
settle, offline delivery, the accounting fixes; every column B needs in the
same migration; logging; tests. The supervisor's check: with the aura at
half strength and three basics per round, a level-40 Warrior still fell in
10 to 15 rounds and the 75-round budget assumption failed, so the aura goes
in A and the count comes down in A. A alone is a damage race a player can
win with potions; B makes it a fight.

**B. A fight.** Tick-owned telegraphs with Strike and Channel, interrupts and
stagger, the heal channel, Focus and Challenge, the compact round screen,
the roster, grouped-player names.

## State

| State | Where | Writer |
|---|---|---|
| Schedule, tomorrow's boss | `world_state` key | tick |
| Phase, scaled stats, telegraph id/seq/lands_at, needed, stagger_until, focus_player, focus_until | columns on `world_bosses` (guarded `ALTER TABLE`, pattern `SqlSaveBackend.cs:770`) | tick; Challenge writes focus |
| `interrupts_done`, `current_hp`, `last_damaged_at` | `world_bosses` | players, guarded UPDATE |
| `engaged_since_seq`, `last_resolved_seq`, `cooldown_until`, `sessions`, `rounds`, `deaths`, `player_level`, `night_damage`, `window_damage`, `paid_nights` (integer bitmask) | `world_boss_damage` | the player's own row |
| Reward rows: player, xp, gold, item json, rarity, fame, marks, settled, delivered | new `world_boss_rewards` | settle |
| `world_boss_events(boss_id, seq, kind, player, detail, at)` | new table | tick and loop |
| `JoinedSeq` (a cache of `engaged_since_seq`; the row is authoritative), `r`, defend rounds, ability cooldowns | `WorldBossCombatState` | session |
| Nothing new on `PlayerData` | | |

Rule: mutable shared state is a column; immutable-after-spawn data (the
definition id, the scaled stats) may stay in `boss_data_json`, since nothing
read-edit-writes it. `window_started_at` sits on `world_bosses`.

`world_bosses` also gains `def_id`, `scheduled_at`, `window_index`,
`nights`, `median_level`, `online_at_spawn`, `first_hit_at`, `killed_at`,
`ended_at`, `hp_at_end`, `peak_engaged`, `regen_total`; `status` gains
`withdrawn` and `left`. Enum additions are append-only.

## Invariants (supervisor, adopted)

- Every shared field is written by a guarded UPDATE with its own
  precondition; never read-edit-write of `boss_data_json`.
- The killing blow stays the status flip; expiry, withdrawal, and defeat all
  guard on `status = 'active'`, so no race pays twice.
- Player HP never lives in the row; the tick never damages a player; the loop
  resolves each seq once per player.
- The row is self-describing at any instant for a session that starts
  mid-mechanic. No mechanic depends on reacting inside seconds.
- UTC in the database; the Eastern schedule converts through the daily-reset
  helper; cooldowns and carry-over are rows, not memory.
- Never read "no active boss" as victory: defeat, withdrawal, expiry, and a
  database failure are four outcomes; a session never switches to a new boss
  silently.
- Door pager: a round under 20 rows; screen reader gets the telegraph and its
  answer as plain lines. All text through loc keys, five languages.
- Tests before merge (none exist today): budget arithmetic at 35, 40, 80
  under the new model with the pinned `r` values; `r` frozen per session;
  `paid_nights` and `delivered` are single guarded writes; telegraph resolution idempotent by seq across
  reconnects; the interrupt counter cannot exceed `needed` under concurrent
  updates; focus is last-writer-wins by design and the test says so; regen
  never exceeds max HP; damage never credited to an inactive boss; settle is
  idempotent and pays offline and online players the same; the finding-2
  model rerun against the new numbers.

## What the log must answer

Kill rate by night and by window index; attendance by Eastern hour and
weekday; damage per hour by level and class (replaces the budget constant);
share of damage capped; telegraph answer rate by kind and level; interrupts
per channel; sessions per player (the solo question); regen as a share of max
HP; reward XP as a fraction of next-level cost by level; notice effectiveness
(mailed players who logged in during the window). One query per question.

## Council decisions (2026-09-08)

The maintainer read the plan and asked the council to make the five
decisions it had left him. Four seats voted (Codex, the mechanics agent, the
participation agent, the supervisor); each ruling carries the number that
would prove it wrong. Ballots under `~/usurper/evidence/codex/wb-decisions-*`
and `wb-supervisor-*`.

1. **One window, 8 PM Eastern** (four seats). The only audience evidence in
   the repository is North American; a second window would split a 2-to-10
   peak and feed Rally between them. Add a European window when logins by
   Eastern hour show a second peak between 1 and 4 PM, or more than 20 to
   25 percent of active players log in during a European evening or carry a
   non-English language column (starting thresholds).
2. **Three hours** (three seats; Codex kept six for late arrivals). The
   window's job is concentration. Proves it wrong: more than 20 percent of
   engaged players first hit in the final 30 minutes, or withdrawals under
   10 percent HP on more than a third of nights; then 4 hours, not 6.
3. **1.1.4 is milestone A; B follows as 1.1.5** (three seats), with B's
   columns in A's migration. The supervisor voted for both together on the
   code fact that A as first written left survivability at today's 10 to 15
   rounds; the plan now moves the attack-count cut and the aura's deletion
   into A, which answers that, and records the supervisor's view that the
   mechanics are what the maintainer called silly, so B should follow
   without a gap. Proves A wrong: a kill rate under 25 to 30 percent with
   two or more humans present, or median rounds per session above 40.
4. **No ransom; the news line and the unbroken list** (three seats; the
   participation seat kept the 5 percent). The King is one player, and the
   loss would charge that player for everyone else's absence through a
   system the boss code has never touched. Proves it wrong: more than 25 to
   30 percent of bosses leaving after three nights in the first month; then
   the ransom returns with a rally reward for the King attached.
5. **Normalisation stands, one-sided** (four seats for protecting low
   levels; the mechanics seat conceded on its own table, where a level 20
   hits the Titan for 1 to 3). The supervisor's one-sided refinement, `r`
   only below the boss's level with a veteran fighting raw under the cap,
   was then put to the other three seats as a yes/no after the supervisor
   pointed out that adopting it on inference would break the tally rule:
   Codex yes, mechanics yes, participation yes. Four to none. Proves it wrong:
   on nights with three or more contributors, an above-level damage share
   over 60 percent while below-level `s` stays under 0.25 means the
   asymmetry is wrong and full two-sided `r` is the fallback; and applied
   damage per round by level band, logged before the cap, with the low band
   under 0.5x or over 1.5x the at-level band meaning `r` is mis-scaled.
