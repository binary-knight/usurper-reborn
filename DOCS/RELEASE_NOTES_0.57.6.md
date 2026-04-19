# v0.57.6 - Hotfix Rollup (Aldric / Mira / Murder / Dungeon Respawn)

Rollup of four reports that came in after v0.57.5 shipped, plus the cleanup pass on community PR #82 (murder-mechanics rework).

## 1. Aldric auto-equipped a weak weapon over his Sword of Thunder

**Report (Lumina, Lv.72 Elf Magician, online mode):**

> "Aldric just auto equipped worst weapon than he was using. I choose Pass after a fight and he grabbed it.
> Used: Soldier's Sword of Thunder (Blessed) [WP:407 AP:3 Def:+3 Str:+3 Dex:+3 Wis:+3 Crit:5% Leech:5%]
> Exchanged to: Fine Tempered Blade [WP:123] No bonuses.
> I fixed that, thankfully. The original weapon was in my inventory."

The auto-pickup code in `CombatEngine.TryTeammatePickupItem` is supposed to only equip items on teammates when they're strict upgrades. WP 123 < WP 407 — clearly not an upgrade — so something was bypassing the comparison.

**Root cause.** The upgrade check reads the teammate's current weapon via `GetEquipment(slot)`, which returns null for TWO distinct cases: genuinely empty slot, and "phantom slot" where an ID is stored in `EquippedItems` but `EquipmentDatabase.GetById()` can't resolve it. Case two happens in MUD mode when dynamic-equipment registration drifts between session ticks. The old code collapsed both cases to `slotIsEmpty = true`, which let any non-zero-power loot become a "100% upgrade."

**Fix.** `TryTeammatePickupItem` now distinguishes the two nulls. If `GetEquipment(slot)` returns null AND `EquippedItems[slot]` has a non-zero stored ID, the slot is treated as occupied-but-unknown and the auto-pickup skips it rather than risk replacing something valuable we can't introspect. A warning log under category `COMPANION_EQUIP` fires whenever the phantom path triggers, naming teammate + slot + unresolved ID, so future upstream registration gaps have concrete evidence.

## 2. "Spending time with children is broken"

**Report (Lumina):**

> "The game claims I spent time with the chosen child, even if I did not that day. I could spend time with newly born child, but not the rest. Now with any of them."

The "Spend Time with Child" interaction at Home has a daily cooldown. The old check compared `Child.LastParentingDay` against `DailySystemManager.Instance.CurrentDay` — a process-wide singleton. In MUD mode the singleton is reloaded from each logging-in player's save (`currentDay = saveData.CurrentDay`), so other players' logins flip its value, and `lastResetTime` is similarly reloaded so real daily ticks get deferred. Net effect: `currentDay` doesn't reliably advance per player, and once `LastParentingDay` caught up to the singleton's value, the `>=` stayed true indefinitely. A freshly-born child (default `LastParentingDay = 0`) worked exactly once — the first interaction set it to `N`, then `N >= N` blocked forever.

**Fix.** Wall-clock cooldown. `Child` gains a `LastParentingTime` DateTime field; the check requires 20 real hours elapsed since the last interaction. No singleton dependency, no cross-session contamination, advances by definition every real-world day. Legacy `LastParentingDay` kept for save-reader compatibility but no longer consulted for the check. Pre-v0.57.6 saves have `LastParentingTime = DateTime.MinValue` (~19 years ago) so the cooldown is trivially expired — any stuck children unblock on next interaction and the system self-heals from there.

## 3. Dungeon hourly respawn never fired

**Player report:** "I run out of enemies to fight before the boss, making the battle very very difficult to unachievable. Dungeons are already supposed to reset every hour with a thematic message — is that not happening?"

It wasn't. The hourly-respawn logic was coded correctly and the thematic "the dungeon stirs" message was already wired up at [DungeonLocation.cs:183](Scripts/Locations/DungeonLocation.cs#L183), but the gate in `DungeonFloorState.ShouldRespawn()` checked `LastClearedAt` — a timestamp that's only set INSIDE `if (isNowCleared)` at [DungeonLocation.cs:5720](Scripts/Locations/DungeonLocation.cs#L5720), which requires every monster room on the floor to be cleared including the boss.

Players who cleared most rooms but couldn't beat the boss (the exact case the reporter described) never triggered that branch, so `LastClearedAt` stayed at `DateTime.MinValue`, `ShouldRespawn()` short-circuited, and the respawn + thematic message never fired — leaving them to either push a boss they weren't ready for or give up on the floor.

**Fix.** One-line change in `ShouldRespawn()` and `TimeUntilRespawn()` — both now key on `LastVisitedAt` instead of `LastClearedAt`. `LastVisitedAt` is already tracked and persisted, and updates on every floor entry (not per-room), so:

- Clearing 3 rooms, retreating to town, and coming back 70 minutes later now correctly triggers the respawn + fires the thematic message.
- Grinding straight through a floor without leaving doesn't loop "monsters respawn behind you," because `LastVisitedAt` isn't touched while you're on the floor.
- Boss rooms and permanent-clear floors (seals, secret bosses) are unaffected — they still gate on `IsPermanentlyClear` / `BossDefeated` flags.

Players with floors that have been stuck uncleared for hours will see monsters back immediately on next entry, with the thematic respawn message playing for the first time.

## 4. Daily murder cap (3 per day)

With PR #82 removing the execution-deletion consequence, the remaining disincentive against a player going on a Crown-defying killing spree was "you might go to prison for 2 days" — not a huge deterrent for a committed evil build, and definitely not enough to protect the populated town when players could now respawn from "execution" via the normal resurrection flow.

Added `GameConfig.MaxMurdersPerDay = 3`. `Character` gains a `MurdersToday` counter that increments on every successful non-bounty NPC kill and resets with the rest of the daily counters in `DailySystemManager.ApplyDailyReset`. `BaseLocation.AttackNPC` now blocks the interaction up front if the player has already murdered 3 people today — they see a short message ("Your hands tremble. You have already murdered N soul(s) today — even your conscience has limits.") and the attack is refused. Bounty contracts are exempt from the cap — sanctioned kills aren't "murder" for this purpose.

The editor's "Reset daily counters" action also clears `MurdersToday`. A round-trip test in `SaveRoundTripTests` locks the save-persistence contract in.

## 5. Murder mechanics — no more permadeath (community PR #82 + cleanup)

**Huge thanks to [LowLevelJavaCoder](https://github.com/LowLevelJavaCoder)** for submitting [PR #82](https://github.com/binary-knight/usurper-reborn/pull/82) — the first external code contribution of this scale. They've been added to the in-game credits (press `[C]` at the main menu) and the project's credits pages on the website and Steam landing page.

The PR reworked the murder system to remove the character-deletion consequence introduced in v0.53.11. Merged with follow-up cleanup.

**What the PR ships.** Executing the player no longer calls `SaveSystem.DeleteSave` or the matching multi-table online `DELETE` — instead it calls `Player.Die()`, routing the character through the normal death/resurrection flow. Prison sentence reduced 3 days → 2 days. Prison punishment softened: half of on-hand gold taken, equipment and inventory and bank gold kept (was: everything stripped, including BankGold). New mid-sentence "surrender or fight" branch — before the 50/50 execution/prison roll, the player can elect to fight 5 scaling "Murder Guards." Win and you escape capture entirely (at +100 darkness). Lose but survive and you're captured. Die and you go through normal resurrection. `MurderDarknessGain` constant bumped 50 → 250, paired with a matching -125 chivalry.

**Cleanup on top of the merge.**

- Removed a **duplicate `AlignmentSystem.ChangeAlignment(MurderDarknessGain, "murder")` call** that the PR added alongside the existing call at `BaseLocation.cs:4803`. With the bumped 250 value, the duplicate would have granted +500 darkness per murder, not +250.
- Reverted `StreetEncounterSystem.CreateRandomHostileNPC` visibility `internal` → `private`. The PR bumped it but nothing in the PR calls it — the 5 Murder Guards are constructed as raw `new Monster { ... }` literals. Leftover from an earlier approach.
- Wrapped the `[S]/[F]` prompt in an input-validation loop. The original code treated any non-S keystroke as "F," so a misclick silently threw the player into the guards fight.
- Removed `Environment.Exit(0)` in the single-player execution path. PR changed execution from "delete save" to `Die()` — force-quitting the process afterward is wrong UX now that the character survives to revive at the Temple. Online mode still throws `"CHARACTER_EXECUTED"` to drop the session; the server routes that through the standard death handler.
- Fixed typo `ressurect` → `resurrect` in an inline comment.
- Dropped a second typo comment (`usualy`) along with the removed duplicate-alignment block.

### Files Changed

- `Scripts/Core/GameConfig.cs` — Version 0.57.6. `MurderDarknessGain` 50 → 250 (PR #82). New `MaxMurdersPerDay = 3` constant.
- `Scripts/Core/Character.cs` — New `MurdersToday` daily counter.
- `Scripts/Systems/SaveDataStructures.cs` — `MurdersToday` added to `PlayerData`.
- `Scripts/Systems/SaveSystem.cs` — Serialize `MurdersToday`.
- `Scripts/Core/GameEngine.cs` — Restore `MurdersToday` on load.
- `Scripts/Systems/DailySystemManager.cs` — Reset `MurdersToday` with the other daily counters.
- `Scripts/Editor/PlayerSaveEditor.cs` — Include `MurdersToday` in the editor's "Reset daily counters" action.
- 5 localization files (en/es/fr/hu/it) — Two new keys: `base.murder_daily_cap_reached` and `base.murder_daily_cap_hint`.
- `Tests/SaveRoundTripTests.cs` — `MurdersToday` added to the `PreservesDailyCounters_DarkAlley` round-trip test.
- `Scripts/Systems/DungeonGenerator.cs` — `DungeonFloorState.ShouldRespawn` + `TimeUntilRespawn` now key on `LastVisitedAt` instead of `LastClearedAt`, so hourly respawn fires as originally intended even when the player hasn't fully cleared the floor.
- `Scripts/Systems/CombatEngine.cs` — `TryTeammatePickupItem` phantom-slot guard for Lumina's Aldric bug.
- `Scripts/Core/Child.cs` — New `DateTime LastParentingTime` field; old `LastParentingDay` kept for save-reader compatibility.
- `Scripts/Systems/SaveDataStructures.cs` — `LastParentingTime` added to `ChildData`.
- `Scripts/Systems/FamilySystem.cs` — Serialize + deserialize the new field.
- `Scripts/Locations/HomeLocation.cs` — `InteractWithChild` gates on wall-clock time, not day counter.
- `Scripts/Locations/BaseLocation.cs` — PR #82 murder rework + five cleanup fixes (duplicate alignment removed, input loop on S/F, typo fix, single-player Environment.Exit removed).
- `Scripts/Systems/StreetEncounterSystem.cs` — Reverted `CreateRandomHostileNPC` to `private`.
- 5 localization files (en/es/fr/hu/it) — prison sentence text 3 days → 2 days + key rename from PR #82.
