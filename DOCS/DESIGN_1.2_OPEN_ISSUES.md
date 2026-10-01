# Design: the eight open issues (1.2 candidates)

Reconciled from two independent designs produced on 2026-09-07: one by Codex
(gpt-6-astra, high effort, working from inlined source excerpts) and one by a
Claude design agent working in the tree. Every file anchor below was checked
against main at `0138c31`. Where the two disagreed, the tree decided. Raw
designs are kept under `~/usurper/evidence/codex/` (outside the repo).

Items A to D are the four deferrals from the 1.1.1 notes; E is the intimacy
translation note from the same list; F, G, H1, H2 are GitHub issues #43, #41,
#97, #68.

Conventions that apply to every item: all player text via `Loc.Get` with keys
in all five `Localization/*.json`; new persisted player fields wired in
`SaveDataStructures.cs`, `SaveSystem.cs`, and `GameEngine.cs` (NPC fields also
in `OnlineStateManager` and `WorldSimService`; daily counters reset in
`DailySystemManager.RunBasicDailyReset`); enums append-only; no new packages;
gate every commit on `~/.claude/scripts/usurper-test.sh`.

---

## A. Grouped players share the leader's ability cooldowns

**What is actually true.** The cooldown store is not in `ClassAbilitySystem`
(its `CanUseAbility` takes the dictionary as a parameter). It is the per-fight
`CombatEngine.abilityCooldowns` field (`CombatEngine.cs:41`), cleared at the
start of every combat. `ProcessGroupedPlayerTurn` (29116) swaps `currentPlayer`
to the follower and runs `ProcessPlayerActionMultiMonster`, whose ability path
reads and writes that same dictionary (13803, 13867). NPC teammates already get
their own dictionaries through `teammateCooldowns` keyed by display name
(18599). So the bug is a same-key collision inside one fight between the leader
and any follower of the same class, not a loss across fights. Nothing needs to
"survive going home".

**Design.** One file. Add `CooldownsFor(actor, result)`: the combat owner
(`result.Player`) keeps `abilityCooldowns`; anyone else gets a dictionary from
`teammateCooldowns` keyed by `GroupPlayerUsername ?? DisplayName` (username so
two followers with the same character name do not collide). Replace the
fourteen `abilityCooldowns` uses on the multi-monster, Chrono Surge, Reap,
menu-render, and slot-input paths with the helper; PvP paths untouched. Use the
same key at 18599 so the AI fallback for a disconnected follower agrees with
the interactive path. Ticking already covers both stores
(`ProcessEndOfRoundAbilityEffects`, 24343 to 24369).

Rejected: Codex's proposal to persist cooldowns on the character across fights.
It fixes the collision by changing a design rule (cooldowns reset per fight)
that applies to everyone, and it needs five persistence sites for no player
benefit.

**Persistence.** None. **Localization.** None.

**Tests.** `Tests/GroupCooldownTests.cs`: owner and follower dictionaries are
distinct; two followers with equal display names and different usernames are
distinct; a follower's cooldown of 3 ticks to 2 after one round, not once per
acting character; the owner resolves to the field itself. After the change a
grep for `abilityCooldowns` must show only the declaration, the `Clear()`
calls, the tick loop, the helper, and PvP sites.

**Slice.** All of it. Small.

---

## B. A grouped player who dies runs no death pipeline

**What is actually true.** The three grouped-player death sites
(`HandleTeammateDeathDispatch` at 19639, inline copies at 19570 and 28913)
print the fallen banner, remove the follower from the fight, and complete their
input channel. None of `HandlePlayerDeath` runs: no resurrection consumption,
no death count, no penalties, no news. The follower's own session sits in
`GroupFollowerLoop` (`DungeonLocation.cs:17944`, not `GroupSystem.cs`), walks
back to town at 0 HP, and is punished at next login by the "saved dead" check
(`GameEngine.cs:3202`, up to 5 levels and 75 percent gold), the wrong penalty
for the wrong reason.

**The constraint that shapes the design.** `PermadeathHelper.ExecutePermadeath`
resolves the character to erase from `SessionContext.Current` (an AsyncLocal),
which on the leader's combat thread is the leader. Any part of the follower's
death pipeline run inline in the leader's fight would erase or save the wrong
character. The pipeline therefore runs on the follower's own session.

**Design.** A persisted `PendingGroupDeath` (the killer's name; null when
none) on the character, saved with the player at the three persistence sites.
Persisting it is what closes the hole for a follower who disconnects before
pressing a key: without it the pending death is lost, the follower walks back
in at 0 HP, and the saved-dead check at `GameEngine.cs:3202` applies the wrong
penalty, the exact case this item exists to fix. The death sites set it,
remove the follower, complete the channel, and flip the follower session's
`IsGroupFollower` off so `GroupFollowerLoop` exits on its next read (first
slice accepts one keypress; there is no cancel hook on
`TerminalEmulator.GetInput`). `EnterAsGroupFollower`, after
`CleanupGroupFollower`, and the login path when the flag is set on load, run
the follower's death on the follower's own context: the same bookkeeping as a solo death (`PlaythroughDeaths`,
`MDefeats`, fame, statistics), then `PermadeathHelper.HandleOnlineDeath`, which
already implements resurrection consumption, permadeath, and the admin's
permadeath-disabled case; save; exit to the Temple instead of Main Street.
Penalty parity with solo online deaths is kept: an auto-resurrected solo player
pays no XP or gold penalty today, so neither does a follower. After every
`PlayerVsMonsters` return in `DungeonLocation`, drop dead grouped players from
`teammates` so the leader's next fight does not list them.

Codex's fuller version (a persisted pending-death record with phases, so a
crash between death and resolution cannot lose the death) is the right full
design; it is deferred because the first slice already closes the punishment
gap and the persisted record needs a schema and a reconnect path.

**Persistence.** `PendingGroupDeath` at the three player sites (`PlayerData`,
`SaveSystem`, `GameEngine` restore); the saved-dead check must run after it so
a pending group death is consumed by the pipeline, not by the wrong penalty.
**Localization.** 3 keys
(`group.follower_death_header`, `group.follower_death_left`,
`group.follower_left_dead`).

**Tests.** `Tests/GroupFollowerDeathTests.cs`: the mark sets the flag, removes
the follower, completes the channel, leaves HP at 0; the extracted penalty
routine matches the instance one on a fixed seed; the companion path is
unchanged. Review rule for the PR, in a form a reviewer can check with a grep:
the follower death pipeline lives in a new class (`GroupFollowerDeath` in
`Scripts/Server/`), and `CombatEngine`'s diff touches only the three death
sites. As a backstop, the counts of `GameEngine.Instance`, `SaveSystem`, and
`PermadeathHelper` references in `CombatEngine.cs` (8, 16, and 4 on main at
`0138c31`) must be unchanged after the PR.

Why the one-keypress loop is an acceptable first slice rather than a hole:
`PlayerSession` runs an emergency save in its finally block on disconnect for
the session's current player unless the character was deleted, so a follower
who drops before pressing a key carries the persisted mark into their save,
and the login branch resolves the death at next login. (Verified in review,
2026-09-07.)

**Slice.** Persisted flag, loop exit, `HandleOnlineDeath`, bookkeeping, save,
Temple. Deferred: the phased pending-death record, spectating at 0 HP, a
no-keypress loop cancel.

---

## C. Haggling has no entry point

**What is actually true.** `HagglingEngine.Haggle` is complete and called by
nothing. Success is deterministic, not a roll: the offer must be at least 80
percent of the price and the discount must be within the Charisma tier (4, 7,
10, 13, 17, 20 percent). Twenty-one `haggle.*` keys exist. `Character.WeapHag`
and `ArmHag` (3 attempts each) are reset daily but never saved, so they refill
on every load, the same class of bug as the 1.1.1 `Wrestlings` fix. Worse, the
weapon and armor shops read `WeapHag < 1` as "kicked out by the trolls"
(`WeaponShopLocation.cs:68, 606`), so wiring haggling naively would bar a
player from the shop the moment they spent their third attempt.

**Design.** Persist first: `WeapHag`, `ArmHag`, and two new
`WeaponShopBarredUntilDay` / `ArmorShopBarredUntilDay` day numbers; the shop
entry gate reads the bar day, not the attempt count. Then hook the weapon and
armor `BuyItem` confirms: the prompt becomes `[Y]es [N]o [H]aggle (n left)`.
`H` runs the existing haggle screen over the pre-tax adjusted price; on success
the agreed price replaces it and tax is recomputed on the agreed amount (Codex's
point: check affordability after negotiation, so a player who can afford only
the discounted price can still try). Each `H` spends one attempt regardless of
outcome. At zero attempts `H` is not offered; pressing it anyway runs the
existing angry lines and, if the player insists, the existing kick-out, which
now sets the bar day and sends them to Main Street. Trolls keep their weapon
discount and cannot haggle there (existing line). Auto-buy paths do not haggle.

**Persistence.** Four player fields at three sites plus the daily reset (bars
clear with the day). **Localization.** 5 keys (`shop.buy_prompt_haggle`,
`shop.buy_prompt_no_haggle`, `shop.haggle_price_agreed`,
`weapon_shop.keeper_name`, `armor_shop.keeper_name`).

**Tests.** `Tests/HagglingTests.cs`: tier boundaries (Charisma 100, price 1000:
900 succeeds, 890 fails, 790 fails even at Charisma 250); exactly one attempt
per haggle; save round trip of all four fields; daily reset restores 3 and
clears the bar.

**Slice.** Weapon and armor shops. Deferred: magic shop (needs
`ShopType.Magic` appended and a third counter), kick-out news broadcast,
Electron menu entry.

---

## D. The bank safe is process-static and shared

**What is actually true.** `BankLocation._safeContents` is a static starting at
500,000 on every process start; deposits add to it, withdrawals subtract,
robbery takes 25 percent of it minus the robber's own deposits, and guard count
scales with it. Online that is one value for the whole server that forgets
itself on restart; robbery loot no longer relates to anyone's deposits.

**Design.** A per-world persisted vault reserve, not per-player (a per-player
vault would leave nothing to rob; robbing your own deposits is already
forbidden). Player bank balances stay untouched by others' robberies, as today,
and the robbery survey says so (`bank.rob_insured_note`). New
`BankVaultSystem`: single-player stores `WorldStateData.BankVaultReserve`,
restored only when not online (the same gate `Settlement` uses at
`GameEngine.cs:6383`, because online saves embed a stale per-player copy of
`WorldStateData`); online uses a `world_state` row through
`IOnlineSaveBackend.TryAtomicUpdate` with retries, and the robber's gold is
credited only for the amount actually deducted (Codex's point: two robbers
against 150,000 must not both take 150,000). Robbery take capped at 250,000.
Daily refill of 25,000 plus 1 percent, capped at 5,000,000, from
`DailySystemManager` in single-player and from the live online daily block in
`WorldSimService.cs:2113`; not from `OnlineStateManager.TryProcessDailyReset`,
which has no callers.

**Persistence.** `WorldStateData.BankVaultReserve` (single-player) and a
`bank_vault` world-state key (online). **Localization.** 2 keys.

**Tests.** `Tests/BankVaultTests.cs` with an in-memory backend stub: deposit,
withdraw to floor, robbery math and cap, refill and cap, a retried atomic update
that lands once, single-player round trip.

**Slice.** Persist, atomic update, cap, refill. Deferred: SysOp reset command,
a "recent heist" world event. 1.2.4 persisted the robbery attempt counter:
`WorldStateData.BankRobberiesToday` and `BankRobberiesDate` (single-player) and
a `bank_robberies` world-state key (online), one count for the world, reset at
the day change (`Tests/BankRobberyCounter124Tests.cs`).

---

## E. The 44 intimacy translations

**What is actually true.** The 1.1.1 note was wrong. Of the 43 keys where a
translation drops a `{0}` or `{1}`, 38 drop a pronoun argument that
`IntimacySystem` fills from `ui.pronoun_*` keys which are deliberately empty in
Spanish, French, Italian, and Hungarian (pro-drop languages; listed in
`LocalizationIntegrityTests.IntentionalEmpty`). Inserting the placeholder would
add a space that `CleanFormat` collapses anyway. Only five keys carry a name,
and only two lines drop it: the Spanish and Hungarian
`intimacy.afterglow.silent_l1`. One Hungarian `love_street` line drops a plural
suffix that does not apply to Hungarian.

**Design.** Fix the two lines. Add a parity test that fails when a translation
drops a placeholder English carries, with a reviewed allowlist of the 43
pronoun-only positions (plus the Hungarian suffix line), each entry naming the
pronoun variable it stands for, and a guard that the allowlist only contains
keys that exist. Codex's stricter parser (index sets, escaped braces, format
suffixes) is adopted for the test.

**Persistence.** None. **Localization.** 2 edited lines, no new keys.

**Slice.** All of it. Small.

---

## F. Relationships degrade with neglect (issue #43)

**What is actually true.** Nothing decays. `RelationshipSystem.DailyMaintenance`
is dead code with no callers, and if wired it would divorce 5 percent of
marriages a day at random. Spouses also carry `RomanceTracker.Spouse.LoveLevel`
(1 best, 100 worst), read by Home for divorce risk, which never moves on its
own.

**Design.** Time is measured in days the player was present: a persisted
`Character.PresentDays` incremented only in `RunBasicDailyReset`, which runs
once per reset while the player is logged in. Implementation constraint:
`RunBasicDailyReset` is called only from the two branches of
`PerformDailyReset`; the world-sim catch-up path calls `RunCatchUpDailyReset`,
a separate method that syncs the day counter and companion flags and never
enters `RunBasicDailyReset`. `PresentDays` goes in `RunBasicDailyReset` and in
no helper the catch-up path shares, so absence stays free. A month away therefore contributes zero, which is
the requirement. Each relationship record gains `LastPlayerContactDay`,
stamped by any positive `UpdateRelationship` from the player, by marriage, by
intimacy, and at the end of a fight for every surviving NPC teammate. Neglect
days = `PresentDays - LastPlayerContactDay`. Non-spouse relations better than
Normal lose one step every 7 neglect days, floor Normal, never hostile. Spouses
keep the married state; `LoveLevel` worsens by 5 per 7 neglect days after a
7-day grace, and Home's greeting shows tiers at 7 and 14 days. At 28 days the
spouse leaves the next time the player enters Home (a scene, then the existing
`ProcessDivorce`), unless any positive contact happened first. Positive contact
resets the clock and improves `LoveLevel` by 5 once. The dead `DailyMaintenance`
gets a comment and stays unwired.

Rejected: Codex's active-minutes clock (five-minute input windows, monotonic
timers, per-pair care scores). It is more precise and far more machinery;
present-days uses a counter the daily reset already owns.

**Persistence.** `PlayerData.PresentDays`; `RelationshipSaveData.LastPlayerContactDay`
through the existing export and import. Not NPC fields.
**Localization.** 10 keys.

**Tests.** `Tests/RelationshipNeglectTests.cs`: no change at 6 days, one step
at 7, floor at Normal by 30; contact stamps the day; spouse `LoveLevel` 20 to
25 to 30 at 14 and 21 days; at 28 the routine reports a pending divorce and
does not divorce by itself; 30 catch-up resets leave `PresentDays` unchanged;
export and import round trip.

**Slice.** Counter, contact stamp, non-spouse decay, spouse `LoveLevel` creep,
Home greeting tiers. Second slice after live data: the 21-day letter, the
28-day leaving scene, the dialogue notice for non-spouses.

---

## G. NPCs resent being left to die (issue #41)

**What is actually true.** Teammate death handling never asks whether the
player could have helped. `MemoryType.Abandoned` exists in the NPC memory
system with weights in `MemorySystem.cs:92` and `RelationshipManager.cs:66`,
and `NPCPetitionSystem.cs:1183` already records one when the king dismisses a
plea for protection; the combat penalty reuses that recording convention. There is no mechanic to resurrect an NPC:
non-permadeath NPCs respawn after about ten minutes of simulation, companions
never return. So the issue's "resurrect them quickly" has nothing to hook to
today.

**Design.** A "could have helped" test evaluated at the death site for the
combat owner only: the player held a usable healing potion (or a heal spell
with the mana for it), the ally was below half HP at the start of the player's
most recent turn, the player's action that turn was not aid to that ally, and
it was not a boss potion-lockout, arrest, or exhibition fight; mercenaries,
echoes, and grouped players excluded. Penalty: the NPC's feeling toward the
player worsens two steps (never below Hate) and an `Abandoned` memory is
recorded; a companion loses 15 loyalty. One extra line after the death banner
tells the player why. Gratitude: healing an ally below half HP gives one step
once per fight, under the existing daily cap. Restore: a Temple service, "pray
for a fallen ally", that fast-tracks a non-permadead NPC's respawn and reverses
the steps in full within one present-day of the death, half within three,
nothing after, for `level x 500` gold. This is the only resurrection path in
the game, so it is what "resurrect them quickly" becomes.

**Persistence.** A player-owned list of abandoned allies (NPC id, death day,
steps applied; max 10, pruned after 3 present-days) at three sites. NPC memory
already persists. **Localization.** 9 keys.

**Tests.** `Tests/AllyAbandonmentTests.cs`: each condition of the test in
isolation; exactly two steps applied with the Hate floor; gratitude capped; the
restore window at 1, 3, and 4 days; list round trip.

**Slice.** The test, the penalty, the companion loyalty drop, the one combat
line. Deferred: Temple restore and forced respawn, the greeting memory line,
gratitude.

---

## H1. Player District (issue #97)

**What is actually true.** No player-owned building exists.
`GameLocation.PlayerMarket = 24` is a legacy Pascal slot used by nothing; do
not reuse it. The trade substrate is `MarketplaceSystem` in single-player
(including `NPCWantsToBuy`, driven from the world simulator) and the SQL
auction house online. Main Street's `8` key is free.

**Design, first slice.** A new `PlayerDistrict` location (append `= 507`) off
Main Street `[8]`. Build a shop for 1,000,000 gold (a `GameConfig` constant;
the issue calls it adjustable). One shop per character, twenty per district. A
ten-slot sell-only stall: stock from inventory at an asking price; visitors buy
at price plus the normal city tax; the pre-tax price goes to the owner's till,
collected at the shop. NPCs on their shopping behaviour browse the district and
buy through `NPCWantsToBuy`, paying into the till, so single-player shops have
customers. Permadeath purges the shop. State: `WorldStateData.PlayerShops` in
single-player; online, one world-state key per shop (`player_shop:{owner}`)
through `TryAtomicUpdate`, read by `WorldSimService` before NPC browsing (Codex's
point: a single blob plus a compare-and-swap is not an atomic transfer of buyer
gold, escrowed item, and seller proceeds; per-shop keys and a sale transaction
in `SqlSaveBackend` are the online path).

**Full version** (later releases): buy filters funded from the till, upgrades
to 15 and 20 slots, inns at 10,000,000 gold with rooms and rest benefits,
hirelings and hours, raids reusing the castle siege call-to-arms, a bank, and a
world-state schema version.

**Persistence.** `PlayerShopData` in `WorldStateData` and per-shop world-state
keys; `PermadeathHelper` purge hook. **Localization.** About 30 keys.

**Tests.** `Tests/PlayerDistrictTests.cs`: build cost and one-per-owner; slot
cap; buy moves gold, till, and tax; NPC purchase; round trip; purge.

**Scope note.** Even the first slice is the largest item here: a new location,
a new system, two persistence paths, and a new online transaction. It is a
release on its own, not a companion to the other seven.

---

## H2. Moddable data (issue #68)

**What is actually true.** The premise is out of date. `GameDataLoader` already
loads seven moddable files from `GameData/` next to the executable (NPCs,
monster families, dreams, achievements, dialogue, balance, additive equipment
at ids 200000 and up), with export from the editor. The repository's `Data/`
folder (`characters.json`, `game_config.json`, `npcs.json`) is read by nothing
and is stale. Still baked in: class starting attributes and names, the spell
book (100 entries), class abilities (182 entries), NPC name pools, and the
built-in equipment literals.

**Design, first slice.** Extend the existing loader with `abilities.json` and
`spells.json`, replace-by-key (an existing id or class-and-level overrides the
built-in numbers, unknown keys append, nothing is ever removed, so saves that
store ids and levels stay valid), with strict validation that rejects a file
rather than half-applying it (Codex's point: tolerant parsing hides mistakes).
Export writes them from the built-ins. Delete or move the stale `Data/` folder.
Document `GameData/` in `CONTRIBUTING.md`.

**Full version.** `classes.json` (needs the starting-attribute table to become
mutable), `names.json`, every `Scripts/Data/*.cs` table behind the same
`loader ?? builtIn` pattern, a schema version per file, `GameData/README.md`.

**Persistence.** None. **Localization.** None (sysop-facing log lines).

**Tests.** Extend `Tests/GameDataLoaderTests.cs`: built-in counts asserted
against the enums, replace-by-key merges, validation rejects, export then
reload is identical.

---

## Order and sizing

| Item | Size | New persisted state | Notes |
|---|---|---|---|
| E | small | none | corrects a false claim in the 1.1.1 notes |
| A | small | none | one file |
| C | medium | 4 player fields | persistence fix is worth shipping even without the prompt |
| D | medium | world state | first online atomic-money path in the bank |
| B | medium | none | runs on the follower's own session; review rule above |
| F | medium | 2 fields | first slice stops at warnings |
| G | medium | 1 list | first slice stops at the penalty |
| H2 | medium | none | extends what exists |
| H1 | large | world state, online transactions | a release of its own |

Suggested grouping: E, A, C, D, B as a "1.1.2 or 1.2 groundwork" pass; F, G,
H2 as the 1.2 feature set alongside the promised gear sets; H1 as 1.3.

## Decisions (taken 2026-09-07)

1. H1: its own release after the other seven (1.3).
2. F: warnings only in the first slice; the 21-day letter and the 28-day
   leaving scene wait for live data.
3. G: penalty first; the Temple restore service is a follow-up.
4. E: the 1.1.1 notes and the GitHub release body were corrected in place.

## Constants for the maintainer to set

These are design numbers, not derived from anything; they are collected here
so they can be changed in one place before or after release.

| Constant | Proposed | Item |
|---|---|---|
| `BankVaultInitial` | 500,000 | D (today's value) |
| `BankRobberyMaxTake` | 250,000 | D |
| `BankVaultDailyRefill` | 25,000 | D |
| `BankVaultRefillRate` | 1 percent per day | D |
| `BankVaultCap` | 5,000,000 | D |
| Haggling attempts per shop per day | 3 (today's value) | C |
| `NeglectStepDays` | 7 | F |
| `SpouseNeglectGraceDays` | 7 | F |
| `SpouseNeglectLovePenalty` | 5 per step | F |
| `SpouseNeglectDivorceDays` | 28 (second slice) | F |
| `AbandonPenaltySteps` | 2 | G |
| `AbandonCompanionLoyaltyPenalty` | 15 | G |
| `AbandonRestoreWindowDays` | 3 (follow-up) | G |
| `PrayForAllyCostPerLevel` | 500 gold (follow-up) | G |
| `PlayerShopBuildCost` | 1,000,000 (1.3) | H1 |
| `PlayerDistrictMaxShops` | 20 (1.3) | H1 |
| `PlayerShopSlots` | 10 (1.3) | H1 |
