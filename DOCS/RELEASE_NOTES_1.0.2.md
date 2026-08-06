# Usurper Reborn v1.0.2

Fixes ghost marriages, Home rest healing, and trap consistency. Ghost marriages: a player whose NPC spouse died stayed married forever and
never became a widow or widower. Ships together with the v1.0.1 relay login fix,
which was built but never deployed.

## The bug

Reported by a player: "My wife is dead but I'm still married and not a widower."

Confirmed against live production data. The player's marriage lives in three
registers, and only two of them were cleaned up when the spouse died:

| Register | State after the death | Correct? |
|---|---|---|
| `NPCMarriageRegistry` | entry removed | yes |
| `RelationshipSystem` | downgraded Married(10) to Love(20) | yes |
| `RomanceTracker.Spouses` | **still listed the dead spouse** | no |

The player's `romanceData.spouses` still held the spouse record, `exSpouses` was
empty, and the spouse's NPC id appeared nowhere in the 151-record world roster.
The NPC was gone from the world entirely, and the player was still married to
her.

## Root cause

`RomanceTracker.Instance` resolves per session. NPC deaths happen during
world-simulator ticks that run with **no session attached** to an offline player,
so the death cascade's `OnNPCPermadied` call mutated a different instance while
that player's romance data sat untouched in their save blob. Login then reloaded
the stale record verbatim, with no validation against the living world.

Two adjacent latent bugs made it permanent rather than self-correcting:

- `GetSpouseName` and `SyncDeadSpouseState` both tested `spouse != null &&
  spouse.IsDead`. But `GetNPCByName` already excludes dead NPCs and returns
  `null` for them, so that condition was effectively **unreachable** and a dead
  or removed spouse fell through as "alive".
- The player-flag cleanup was gated behind `cleared > 0`, which (given the
  above) could essentially never fire, contradicting its own stated purpose.

`PlayerData` does not persist `IsMarried` or `SpouseName` at all, so the existing
marriage cleanup keyed on `currentPlayer.IsMarried` could never run either.

## The fix

New `RomanceTracker.SyncDeadPartners(roster)` walks spouses, lovers, and
friends-with-benefits, and retires any partner who is dead **or missing from the
world** through the existing `OnNPCPermadied` path. That preserves the marriage
in `ExSpouses` as history, which is what makes the player properly a widow or
widower. It is silent (no mail, news, broadcasts, or alignment changes) and
idempotent.

Called from a new `HealRelationshipStateOnLogin()` used by **both** load paths.
The single-player path had never been healed at all.

Missing now counts as dead in `GetSpouseName` and `SyncDeadSpouseState`, and the
correction is saved immediately rather than waiting for the next autosave.

## Guarding against the fix itself (the part that mattered)

A relationship-system review caught a **critical** flaw in the first draft, and
it is worth recording because the failure mode was worse than the bug.

`NPCSpawnSystem` is a process-wide singleton shared by every session and the
world sim, and its roster is rebuilt **non-atomically**: `ClearAllNPCs()` takes
and releases the write lock, then each NPC is added under its own separate
acquisition. A concurrent reader can therefore observe a perfectly valid,
non-empty, **half-built** roster whose count walks 1, 2, 3 ... 151.

That was harmless while callers only read the roster. It stopped being harmless
the moment login healing began *deleting* state on a missed lookup. A player
logging in during another player's roster rebuild would have found their living
spouse "missing", been permanently widowed, and had it saved. On a launch
weekend, at the highest-concurrency moment there is, on every online login.

Three layers now stand between a missed lookup and a destructive decision:

1. **`NPCSpawnSystem.IsRebuilding`** is set for the whole teardown-and-rebuild
   in both rebuild sites (`GameEngine.RestoreNPCs` and
   `WorldSimService.RestoreNPCsFromData`), cleared in a `finally`. No partner is
   retired while it is set.
2. **A plausibility floor.** A roster must be at least half its high-water mark
   and above a hard floor of 25 before it is trusted to prove an NPC absent. A
   12-of-151 snapshot is never authoritative.
3. **Corroboration before wiping flags.** `IsMarried` / `Married` / `SpouseName`
   are now cleared only on positive evidence: the named spouse fails to resolve
   **and** `RomanceTracker` no longer lists a spouse. An empty `GetSpouseName()`
   alone is not evidence, because it also returns empty when the relationship
   table was never imported.

Also verified during review and left unchanged: `ResolvePartnerNpc` matches the
correct ID field, `OnNPCPermadied` has no side effects inappropriate to a login
pass, and player-to-player marriages do not exist in this codebase, so resolving
spouses through the NPC roster is correct.

## Tests

`Tests/RomanceDeadPartnerHealingTests.cs` (new, 9 tests): the exact reported
shape, dead vs missing, living-spouse negative, idempotency, lovers and FWB,
mixed rosters, and the two guards that matter most, **rebuild-in-flight retires
nothing** and **an implausibly small roster is not authoritative**.

Both this class and `TeamSystemRecruitmentTests` were placed in a shared xUnit
collection. They touch the same process-wide singletons, and xUnit runs test
classes in parallel by default, which made an unrelated test fail intermittently.
The production code was proven clean first: the full suite passed with the fix
applied and these tests excluded.

**937/937 tests pass**, verified stable across repeated consecutive runs.

## Deploy notes

Server redeploy. Existing affected players heal automatically on their next
login, with the correction saved at that moment; no manual data repair is
needed. Ships with the undeployed v1.0.1 relay fix, so the server should go out
before any Steam depot upload.

## Also fixed: Home rest healed a share of missing health, not maximum

Reported by a player: "Resting at home restores a % proportion of missing health
rather than a % of your max health."

Correct. `HomeLocation.DoRest` multiplied the SHORTFALL by the tier percentage,
so every tier below the top was asymptotic: each rest closed a fraction of the
remaining gap, full health was unreachable no matter how many rests were spent,
and an identical rest healed wildly different amounts depending on how hurt the
player happened to be. Tier 5 (100%) behaved correctly by coincidence, which is
why it survived since v0.44.0.

At a straw pallet (25%) with 1000 max HP, resting from 300 HP used to give 175
and converged on roughly 780 over repeated rests; it now gives 250 and reaches
full in three. The reported amount is capped at whatever was actually missing, so
a nearly-healthy player is not told they recovered a full bar.

Extracted to `GameConfig.GetRestRecoveryAmount` so the formula is testable
(`Tests/HomeRestRecoveryTests.cs`, 12 tests).

The full-recovery sleep paths at the Inn and Home deliberately keep the shortfall
formula and were left alone: there the multiplier starts at 1.0 and is only cut
by the Blood Price penalty, so "75% of the way to full" is the intended meaning.
Applying a share of maximum there would have erased the penalty for a
lightly-wounded murderer.

## Also fixed: traps were inconsistent in both feedback and stakes

Reported by a player: "Traps have inconsistent risk-reward and skill roll
displays. Sometimes they happen, sometimes they don't." Both halves were true.

**Feedback.** Only 2 of the 6 traps rolled any check at all, and the two that did
printed the governing stat **only when the player succeeded**. A failed evasion
said "You couldn't react in time!" with no stat and no odds, so the readout
appeared sporadically and vanished exactly when a player most wanted to know what
had happened, with no way to tell whether investing in Agility was doing anything.

Every trap now resolves through one shape: an Agility evasion attempt, then a
single mitigation check against a stat that suits the trap, reported identically
whether it passes or fails, with the stat, its value, and the real percentage.

| Trap | Mitigation stat | On success |
|---|---|---|
| Pit | Agility | half damage |
| Poison darts | Constitution | half damage, no poison |
| Fire | Dexterity | half damage |
| Acid | Dexterity | half gold lost |
| Curse | Wisdom | drain fully negated |
| Broken mechanism | none | it is the lucky one; it pays out |

The evasion roll now reports its odds on failure as well as success.

**Stakes.** Acid was the only trap that scaled on the player's **wealth** rather
than the floor: a flat 10% of gold, ruinous for a rich character and free for a
poor one. It is now capped to a floor-scaled amount, so at floor 22 a player
carrying 50,000 gold loses 3,300 instead of 5,000, and one carrying a million
loses 3,300 instead of 100,000.

**And the reason it felt so random: there are two trap systems.** Room traps
(`TriggerTrap`, 6 outcomes) and random-event traps (`TrapEncounter`, 5 outcomes)
are entirely separate implementations, and the second had no skill checks at all
plus two outcomes that simply did nothing. A player crossing between them saw
completely different behaviour from the same word, which is the likeliest source
of "sometimes they happen, sometimes they don't". Both handlers now use the same
mitigation helper and the same reporting.

Four localization keys across five languages (two new, two that gained an
argument). Adding those arguments turned up a live defect during review: a
second call site for `dungeon.trap_no_react` passed none, which would have
printed the raw `{0}` and `{1}` placeholders to players. Every call site for all
four keys is now argument-count audited.

## Also fixed: text colors carried no meaning

Reported by a player: "Text color in general is just inconsistent as all
get-out. Please stop using darkgray for important readouts like my status
effects and combat damage readouts. Use it for things that are irrelevant to the
flow of the game, like stat rolls."

The diagnosis was right and the cause was structural. Call sites named a
**color**, not a meaning, so across 1,610 of them every author guessed
independently and the same kind of message ended up in different colors in
different places. Nothing enforced that "urgent" looked urgent.

Switching to one of the built-in themes could not have fixed this, because a
theme remaps a color name to another color name, which sits one level *below*
meaning. It can restyle a slot; it cannot move a message into a different one.
ClassicDark makes that concrete: it collapses white, gray, dark_gray and green
all into a single dim green, so under it an urgent readout and an ignorable stat
roll render **identically**.

Call sites now name a role, and each theme decides what that role looks like in
its own palette:

| Role | Meaning |
|---|---|
| Critical | You are in danger or just lost something |
| Success | Something went your way |
| Action | Something you interact with: hotkeys, prompts |
| Notice | Worth reading, not urgent: a buff expiring, a penalty |
| Narration | Body text and flavor |
| Derived | Safe to skip entirely: stat rolls, damage-vs-defense math |
| Disabled | A choice that exists but is withheld right now |

Derived and Disabled are the only roles that may be dim, which is the rule the
report was asking for. Tests assert that in **every** theme, including the two
monochrome ones, Derived never renders the same as Critical, Narration, or any
actionable role. The monochrome themes deliberately collapse several roles into
their three brightness tiers, but never across that line.

Migrated first where reading happens under time pressure: combat damage lines,
the ability list, dice rolls, trap checks, and status-effect messages. The
clearest offender was the **grief penalty** -- a real modifier cutting your
combat effectiveness by up to 15%, printed in dark gray. Damage taken, damage
dealt, and teammate damage now sit at three distinct, deliberate levels instead
of three colors that happened to be chosen separately.

Two defects surfaced during the pass:

- One damage-vs-defense breakdown was **hardcoded English** and never went
  through localization at all, so non-English players saw an untranslated line.
  An exact-match key already existed.
- Status-effect expiry messages were dim, including buffs falling off. Losing a
  buff is a state change you may need to react to.

Literal color names still work everywhere, so the remaining call sites migrate
opportunistically rather than in one risky sweep.

`Tests/ColorRoleTests.cs` (new, 10 tests) covers resolution in all five themes,
the distinctness guarantees, the ClassicDark collapse specifically, and that
every role maps to a color the ANSI renderer actually recognizes -- an
unrecognized name silently falls back to white, so a typo in a theme map would
otherwise have shipped as "everything is white" with no test failure.

These tests establish a `SessionContext` so theme switching stays flow-local.
`ColorTheme.Current` falls back to a process-wide static when no session is
attached, and xUnit runs test classes in parallel; that combination is what
caused the intermittent failure described above, and this avoids repeating it
without having to serialize the class.

**947/947 tests pass.**

## Also fixed: Charming Performance was strictly worse than an ability 8 levels cheaper

Reported by a Lv.26 Jester: "What on earth is the deal with Charming Performance?
50% chance at a 40% stun?"

The compounding was read correctly and the conclusion was right. Laid out against
the ability it competes with:

| | Pratfall | Charming Performance |
|---|---|---|
| Unlocks at | 18 | **26** |
| Stamina | 18 | **35** |
| Cooldown | 2 | **4** |
| Damage | 50 base | **none** |
| Control | 60% stun, 1 round | 40% x 50% = **20% to skip one attack** |

An ability gated eight levels later, costing roughly double on every axis,
dealing no damage, and delivering less control. There was no build in which
pressing it was correct.

**Root cause.** `Duration = 3` had been sitting in the ability data since it was
written, and nothing ever read it. `Charmed` was a plain bool that got cleared on
the target's very next turn whether the skip fired or not, so a three-round
sustained control effect resolved as a single coin flip. The description compounded
the confusion by quoting both percentages without ever stating the net.

**The fix.** Charm now runs its stated duration (new `Monster.CharmedRounds`), and
the application chance moves from 40% to 70%. One cast is now worth roughly one
skipped enemy attack (0.70 x 3 rounds x 0.50 = 1.05) on a four-round cooldown,
which is a defensible trade for dealing no damage. The three tuning values are
named constants rather than magic numbers in two separate handlers.

The `dominate` spell sets `Charmed` directly with no duration, so a zero-round
charm deliberately keeps the old one-shot behavior.

**A latent display bug this surfaced.** The "skip the attacks! message" check
treated `Charmed` like sleep, stun and freeze, which all stop an attack outright.
Charm does not; it is a per-round coin flip resolved later. So on the roughly half
of charmed rounds where the monster *did* attack, it attacked with **no attack line
at all**. That was survivable while charm lasted one round. Extending it to three
would have tripled it. Charmed monsters now announce the attack and then hesitate
out of it, which is both accurate and better narration.

**On the same report's other two points.** Pratfall was described as "a guaranteed
full stun" -- it is actually 60%, so it was being over-rated, though even at 60% it
still dominated Charm. Juggler's Trick was called useless for having no rider; it is
deliberately the zero-cooldown, stamina-efficient filler, and it out-damages Pratfall
per stamina point (3.33 vs 2.78 base per point), which is the reason to press it
while Pratfall is cooling down. Vicious Mockery already occupies the distract-rider
slot at level 1, so adding a rider here would have duplicated it. Left as-is, and a
test now pins that efficiency edge so it cannot silently erode.

`Tests/JesterCharmBalanceTests.cs` (new, 10 tests) covers the old and new expected
values, the domination check against Pratfall, duration ticking, the Dominate
fallback, description-matches-implementation, and the Juggler's Trick efficiency
floor.

**Noted, not fixed:** all 179 class-ability names and descriptions are raw English
written straight to the terminal with no localization path, so non-English players
pick abilities from an English list. That is too large for a hotfix and is recorded
in the localization backlog.

## Also fixed: the game demonstrated a puzzle answer format it then rejected

Reported by a Lv.26 player on floor 23: "5-lever ancient puzzle is either
unsolvable or I have literal brain damage. Maybe the syntax is odd?"

The syntax was odd, and it was our fault. The prompt read:

> There are 5 levers. Enter the sequence (e.g., 1,2,3):

That example is a hardcoded three numbers no matter how many levers the puzzle
has, while the handler rejects anything that is not exactly N entries. So on a
5-lever puzzle the game showed a worked example of an input it would refuse, and
refused it by silently burning one of five attempts with the same "not quite
right" message a genuinely wrong answer gets. Burn all five that way and the
puzzle is, from the player's chair, unsolvable.

The identical defect was in the pressure-plate prompt, whose example was a fixed
four numbers, and in all five translations.

Three fixes:

- The example is now built from the actual count, so a 5-lever puzzle
  demonstrates `1,2,3,4,5`.
- A wrong-length entry is a formatting mistake, not a wrong guess. It now says
  so specifically and re-prompts **without** consuming an attempt. A blank line
  is how you give up.
- The clues are reprinted on every retry. They were shown once before the
  attempt loop, so after a wrong guess (or a screen clear in MUD mode) the player
  was being asked to reproduce a sequence whose only source had scrolled off.

Both sequence puzzles now share one input helper, so they cannot drift apart
again.

**The puzzle itself was fine.** `Tests/LeverPuzzleSolvabilityTests.cs` (new, 10
tests) reconstructs the intended answer purely from the printed hints and feeds
it through the live parsing across 200 generations: the solution is always a
clean permutation and the hints always identify it. The number-riddles are
unambiguous. This was entirely a presentation bug, which is worth stating plainly
because the player's own guess was that they had misread something.

## Files Changed
- `Scripts/Core/GameConfig.cs` - Version 1.0.2
- `Scripts/Systems/RomanceTracker.cs` - New `SyncDeadPartners`
- `Scripts/Systems/NPCSpawnSystem.cs` - `IsRebuilding`, `IsRosterTrustworthy`, `IsCountPlausible`
- `Scripts/Systems/RelationshipSystem.cs` - Missing counts as dead; roster-trust gating; flag clear re-gated on positive evidence; `rosterTrusted` hoisted out of the scan loop
- `Scripts/Core/GameEngine.cs` - New `HealRelationshipStateOnLogin` called from both load paths; rebuild guard around `RestoreNPCs`
- `Scripts/Systems/WorldSimService.cs` - Rebuild guard around `RestoreNPCsFromData`
- `Tests/RomanceDeadPartnerHealingTests.cs` - New, 9 tests
- `Scripts/Locations/HomeLocation.cs` - Home rest recovers a share of maximum, not of missing
- `Scripts/Core/GameConfig.cs` - New `GetRestRecoveryAmount` helper
- `Scripts/Locations/DungeonLocation.cs` - Unified trap resolution; evasion odds shown on failure; acid loss capped to the floor
- `Localization/*.json` - 2 new trap keys, 2 updated (5 languages)
- `Tests/HomeRestRecoveryTests.cs` - New, 12 tests
- `Tests/TeamSystemRecruitmentTests.cs` - Shared-singleton test collection
- `Scripts/UI/ColorTheme.cs` - New `ColorRole` vocabulary; per-theme role maps for all 5 themes; roles resolve ahead of (and deliberately bypass) the literal remap
- `Scripts/Systems/CombatEngine.cs` - Damage taken/dealt/teammate, dice rolls, damage-vs-defense breakdowns, ability list, grief penalty and combat tips migrated to roles; one hardcoded English breakdown routed through localization
- `Scripts/Core/Character.cs` - Status-effect expiry messages: buffs lost read as Notice, debuffs ending as Success
- `Scripts/Locations/DungeonLocation.cs` - Trap mitigation readout migrated to roles
- `Tests/ColorRoleTests.cs` - New, 10 tests
- `Scripts/Core/Monster.cs` - New `CharmedRounds` so charm honors its stated duration
- `Scripts/Core/GameConfig.cs` - New `CharmApplyChance` / `CharmSkipAttackChance` / `CharmDurationRounds`
- `Scripts/Systems/ClassAbilitySystem.cs` - Charming Performance description now states the real numbers
- `Scripts/Systems/CombatEngine.cs` - Charm ticks its duration in both apply paths; `Charmed` removed from the deterministic attack-suppression list
- `Tests/JesterCharmBalanceTests.cs` - New, 16 tests
- `Scripts/Systems/CombatEngine.cs` - New `TryCharmMonster` applies the same boss resist roll and duration cap stuns use, and refuses to refresh an active charm; boss ability announcement relocated past the skip-returns; PvP charm implemented with a per-round roll so it matches PvE instead of the hard lock `PreventsAction` would impose
- `Scripts/Locations/DungeonLocation.cs` - Sequence-puzzle example now scales to the real count; wrong-length input re-prompts instead of burning an attempt; clues reprint on retry
- `Localization/*.json` - Puzzle prompts take a generated example (2 keys), new wrong-length message, new PvP charm line, em-dash swept from `combat.spell_dominate` (5 languages)
- `Tests/LeverPuzzleSolvabilityTests.cs` - New, 10 tests
- `Tests/LLMTests.cs`, `Tests/AnthropicProviderTests.cs`, `Tests/BrainV2SlicesSevenEightNineTests.cs` - Serialized into one collection; they raced on process-global `USURPER_LLM_*` env vars, failing about one full-suite run in four while passing in isolation
