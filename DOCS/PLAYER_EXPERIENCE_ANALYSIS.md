# Player Experience Analysis: The Level-40 Cliff (2026-07-12)

An end-to-end analysis of why players stop progressing around level 30-40, built from live telemetry
(192 player rows, 1,558 retained combat events, resurrection and deletion records, activity cohorts)
plus the code that generates those numbers (XP curve, death flow, flee mechanics, accuracy clamp).
The short answer: **combat is not too hard per-fight. The game is unwinnable in expectation.** The
per-fight experience is fine (97-99% win rates); the compounding math of a 3-life lifetime budget
against a long, risky grind guarantees ruin before level 50 for essentially every player, and the
mid-game cohort has correctly sensed this and stopped playing.

---

## 1. The observed cliff (marketing view: funnel and cohorts)

Level distribution of all 192 characters:

| Band    | Players | Ever died |
|---------|---------|-----------|
| 1-9     | 106     | 12        |
| 10-19   | 25      | 9         |
| 20-29   | 9       | 5         |
| 30-39   | 9       | 8         |
| 40-49   | **1**   | 1         |
| 50+     | 6       | 6         |

The funnel we fixed in 0.65.x works: account -> character 86%, first kill 65%, second login 61%.
Acquisition and activation are healthy. The leak has moved downstream: **mid-game retention**.
Cohort last-login dates tell the story precisely:

- Of the 16 players at level 30+, **half last logged in during May**, clustered at levels 33-51.
- The 50-100 characters are almost all May-era climbers. Between the May 1 beta wipe and the
  June 11 bounded-accuracy fix (0.65.0 B2), floors 30+ dealt effectively ZERO damage (the
  player-AC-outgrew-monster-accuracy bug). The people above 50 sprinted up a free highway that no
  longer exists. Post-B2, exactly one player has been grinding through the 40s+ legitimately.
- The single most-engaged player in the game (1,055 recorded hours) is level 39 and has not crossed 40.

This is the worst possible churn profile from a marketing standpoint: the players being lost are the
top decile by engagement, churned at their moment of maximum investment. In LTV terms the game
acquires customers well and then destroys its highest-value accounts.

## 2. The resurrection economy (mathematics: absorbing Markov chain / gambler's ruin)

Online rules today: 3 resurrections per character, lifetime. Death consumes one (no gold or XP
penalty; wake at 50% HP). At zero, the next death permanently DELETES the character.
**No gameplay mechanism restores a resurrection. None. Ever.** (Verified: the only writes to the
counter outside creation are the save editor and a wizard command.)

That makes each character an absorbing Markov chain whose state is lives-remaining, with strictly
downward drift. In physics terms: a particle diffusing toward an absorbing barrier with no restoring
force. Absorption (deletion or the rational quit that precedes it) is not a risk; it is a certainty.
The only question is when.

Live data confirms the drift (players level 10+, by lives remaining):

| Lives left | Players | Avg level |
|-----------|---------|-----------|
| 3         | 25      | 23        |
| 2         | 13      | 26        |
| 1         | 7       | 33        |
| 0         | 4       | **39**    |

Average level rises exactly as lives fall. By the mid-30s the typical player has 1-2 lives left and
knows it. One player is currently grinding at level 31 with zero lives: one bad roll from losing a
month of progress. The 7-day deletion archive shows the guillotine falling at a rate of about
**3 character deletions per week** (levels 6, 11, and 25 this week).

### The ruin arithmetic

- Per-fight death hazard (retained window, ~2.5 weeks): 11 deaths / 1,558 fights = 0.71% overall;
  in the level 20-39 push band, 6 / 484 = **1.24% per fight**.
- XP cost per level is 50 x L^2 (cumulative to 50, exponent 2.25 after). Decade costs: 20->30 needs
  329,250 XP; 30->40 needs 634,250; 40->50 needs 1,039,250.
- Observed XP per fight: ~504 at levels 20-29, ~1,500-3,900 at 30-39 (the top figure inflated by
  one team-farming outlier).
- Fights per decade, realistically: **~650 fights for 20->30**, **~200-400 for 30->40**.

So a journey from 20 to 40 is roughly 900-1,000 fights at ~1% death risk each. Expected deaths over
that journey: **6 to 10, against a lifetime budget of 3**. Modeling deaths as Poisson, the
probability a fresh character crosses from 20 to 40 without exhausting all three lives is
**under 5%, plausibly under 1%**. And the observed reality matches the model: one player in the
40s, deaths archived weekly, the res-remaining ladder marching down as levels march up.

The critical insight: no individual fight is unfair. Win rates are 97-99% in every band. Players
never experience "this game is too hard" in a single encounter. They experience the compound
process: a slow, dawning certainty that their character is on death row. That is why the churn
signature is quiet drift-away (May cluster) rather than rage-quit spikes.

## 3. Where the deaths actually come from (physics: variance, not attrition)

All 11 retained deaths are burst-kills: damage-at-death between 100% and 140% of max HP, in fights
averaging 3-4 rounds. Nobody is ground down; they are deleted by tail events:

- **5 of 11 deaths were boss fights** (single boss, one-round bursts after the first-3-rounds
  damage cap expires).
- 1 was a six-monster swarm (concentrated fire in one round).
- The rest were deep pushes (dungeon floor at or above player level, e.g. level 20 on floor 26).

Two mechanics that should protect against exactly this fail at the critical moment:

- **Last Stand** only triggers if the round STARTED above 50% HP. The observed deaths run through
  that window: attrition to ~60-70%, then a 100%+ burst. The guarantee expires precisely when needed.
- **Flee against a boss is a flat 20%**, and a failed flee eats a full boss round. Players have
  learned this: 8 flee outcomes in 1,558 fights (0.5%). The escape valve functionally does not
  exist in the one situation that kills people. (NPCs, ironically, just received predictive-death
  fleeing in 0.65.6; players have nothing equivalent.)

In physics terms the damage distribution is heavy-tailed and the death events live entirely in the
tail. Heavy-tail hazards are the worst kind for perceived fairness: the player cannot learn from a
death that came from variance they never saw coming, so the death buys no skill improvement, only
fear. Risk without counterplay reads as unfair even when the averages are generous.

## 4. The grind curve (computer science: complexity of progression)

Total work to reach level L scales as the integral of 50 x L^2, i.e. **O(L^3)**. Reward per fight
grows much more slowly unless the player climbs floors at matching pace, which raises the death
hazard. The early-game XP multiplier (v0.64.1) compresses levels 1-20 and goes fully transparent at
level 21 -- which is exactly where the observed grind wall begins. The 20->30 decade at ~650 fights
is 15-25 hours of repetitive at-level farming, during which the content novelty per hour is
approximately flat (same verbs, same loops). Engagement-per-hour decays just as risk-of-ruin rises:
churn by boredom races churn by deletion, and May's cohort shows both.

The player's strategic landscape is a forced choice between two losing options:
- **Grind safely** at-level: ~650 fights of low novelty (boredom churn), or
- **Push floors** for XP: superlinear hazard growth (ruin churn).

## 5. The psychology (marketing: loss aversion and the endowment effect)

Prospect theory (Kahneman-Tversky): losses weigh roughly 2x gains, and the endowment effect makes an
owned asset (a 30-hour character with gear, spouse, children, house, reputation) worth far more than
its replacement cost. Permadeath of an invested character is therefore experienced as a catastrophic
loss event, and the RATIONAL response to a visible countdown (1-2 lives left, no way to earn more)
is to stop exposing the asset: stop dungeon-diving, then stop logging in. That is precisely the
observed behavior. This game's own history proves players will grind enormously (1,055 hours!) when
they feel safe; the issue is not effort tolerance, it is irreversibility.

Note also what death does NOT cost today: no gold, no XP, no items. The design concentrates 100% of
the penalty into the one currency that cannot be recovered. Good difficulty design is the opposite:
many small recoverable setbacks, no single irreversible catastrophe outside opt-in modes.

The genre solved this long ago: roguelikes make death produce PROGRESS (meta-currency), and ARPGs
make permadeath an opt-in hardcore mode. Usurper Reborn already has account-level meta-progression
(prestige unlocks, NG+) but it only rewards VOLUNTARY endings; an involuntary deletion at level 25
yields absolutely nothing. All stick, no legacy.

## 6. Recommendations, prioritized

The theme: convert an absorbing chain into a renewal process, cut the tail risk, and pay the grind
fairly. Difficulty is not the problem; irreversibility and variance are.

**R1. Make resurrections renewable (highest impact, small code).**
Grant +1 resurrection (cap 3, or 5) at each decade level-up (10, 20, 30...), and/or sell a Temple
"Rite of Return" for steep, level-scaled gold (gold income is healthy: ~2,300/fight in the 30s).
This preserves scarcity and the sting of death while removing mathematical doom. Expected-lives
drift becomes neutral-to-positive for careful players. This single change re-opens 40+.

**R2. Death's Door (burst protection).**
Once per combat, a hit that would kill a player who was above ~25% HP at the moment of the blow
leaves them at 1 HP instead (skip on Nightmare difficulty). This eliminates the 100-140%-of-MaxHP
one-round deletions that produced most real deaths, without changing attrition difficulty at all.
Pairs with the existing Last Stand (which keeps its start-of-round >50% rule).

**R3. A flee that works when it matters.**
Boss flee: scale with desperation, e.g. 20% base rising to 50-60% below 30% HP; and a failed flee
should cost a half-round (reduced damage) rather than a full free boss round. Mirror of the NPC
predictive-flee work: give players the same escape valve their world just got.

**R4. Flatten the 20s-30s grind.**
Extend the early-game XP multiplier taper: instead of transparent at 21, taper smoothly to 1.0 at
~40 (e.g. ~1.8x at 21 declining linearly). Target: every decade costs 100-200 fights, not 650. This
also shrinks total hazard exposure, compounding with R1/R2.

**R5. Make involuntary permadeath opt-in, or make it pay.**
Preferred: default characters at 0 lives suffer a heavy-but-recoverable death (lose levels and gold,
keep the character), with HARDCORE as an opt-in badge-of-honor mode at creation (industry standard).
Minimum alternative: a deleted character leaves a legacy: meta-progression credit, a memorial, and a
head-start heirloom for the next character, so deletion produces something instead of nothing.

**R6. Make the risk legible (cheap, do with any of the above).**
Show remaining lives prominently (status bar, not just death screens); show floor danger ratings
("Deadly" tags when floor > level); telegraph boss burst windows. Informed risk feels fair;
invisible tail risk feels like betrayal.

### Suggested acceptance metrics (re-run these queries 2-3 weeks post-fix)
- Level-40+ crossings per week (currently ~0)
- Involuntary deletions per week (currently ~3; target ~0 outside hardcore opt-in)
- 30-day retention of the level-20+ cohort (currently: half the 30+ cohort churned)
- Population at 0-1 lives (currently 11 players; should trend to near-zero under R1)
- Flee usage rate and flee success in boss fights (currently 0.5% usage)

---

## Appendix: data provenance and caveats
- combat_events retains ~1,558 rows (Jun 25 onward) plus 90-day death retention; death sample is 11
  events, so per-band hazard carries wide confidence intervals (the ruin conclusion is robust to a
  2x error in either direction: even at 0.5%/fight, expected deaths 20->40 is ~4.7 vs 3 lives).
- total_playtime_minutes under-records for many accounts (known fallback issue); used only where
  large enough to be unambiguous.
- The 30s-band average XP/fight (3,883) is inflated by one heavy team-farmer; decade fight-count
  estimates use a 1,500-3,900 range instead.
- The May-era zero-damage highway (pre-B2) explains the existing 50-100 characters; it is why the
  wall was invisible until now: B2 made floors 30+ honest and revealed the underlying economy.
