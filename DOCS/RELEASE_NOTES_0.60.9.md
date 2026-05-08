# v0.60.9 -- Beta

Two bug fixes on top of v0.60.8, both surfaced by one player report about the Regret seal on dungeon floor 80.

---

## Bug fix: map-reveal events did not trigger seal discovery

Player report (Lv.73 Mystic Shaman, online): *"I'm on level 80, looking for the Regret seal. Before I finished exploring, I got a flash of insight showing me all the map. After that, I've explored this floor up and down, not finding the seal."*

Two map-reveal events exist in the dungeon: the `MysteryEventEncounter` Vision case (case 0 of 5) marks every room on the floor as `IsExplored = true`, and the `NPCEncounter` Wounded Adventurer case (case 1 of 5) marks the first half of the floor's rooms as explored. Both events flow through normal event handling: print flavor text, mutate room state, fall back to the location loop. Neither event moves the player.

`TryDiscoverSeal` runs **only** from `MoveToRoom`. Its core logic is correct: once `explorationProgress >= 0.75`, `guaranteedDiscovery` is true and the next room entry finds the seal regardless of room type. But because the trigger is gated on a fresh `MoveToRoom` call, a Vision event that flips exploration to 100% does not actually find the seal until the player happens to walk somewhere afterward. If the player was already standing in a seal-eligible room when the Vision fired, the discovery never landed at all unless they left and re-entered.

The reporter's symptom matches this exactly. Vision fires (matching the "flash of insight showing me all the map" wording and the `dungeon.rooms_revealed` localization key). Player explores adjacent rooms but each subsequent `MoveToRoom` runs `TryDiscoverSeal` against rooms that have been explored for some time, and the deterministic-seed branch of the per-room scaling check lands the same way each visit. With no seal-eligible room between current position and where Vision happened to fire, the seal stays uncollected.

### Fix

Both map-reveal cases now call `TryDiscoverSeal(player, currentFloor.GetCurrentRoom())` immediately after marking rooms explored. The seal lands on the same screen as the map reveal instead of waiting for the player to walk through a follow-up room. The check is identical to the existing `MoveToRoom` callsite, so all gating (uncollected-seal flag, seal-room type, exploration progress, dungeon-random scaled chance) still applies. The fix only closes the path where the trigger condition was satisfied but the trigger never ran.

Files: `Scripts/Locations/DungeonLocation.cs` (Vision case in `MysteryEventEncounter`, Wounded Adventurer case in `NPCEncounter`).

---

## Bug fix: bug-report metadata showed wrong dungeon floor

While investigating the seal report above, the bug header read *"Location: Dungeons (Floor 73)"* despite the reporter explicitly stating they were on floor 80. Tracking the metadata source: `BugReportSystem.GetDungeonFloor()` returns `player.Statistics.DeepestDungeonLevel`, and `RecordDungeonLevel(currentDungeonLevel)` was only called from the initial dungeon-entry path in `DungeonLocation.EnterLocation`. Neither `DescendStairs` (`[D]` from a room menu, descends one floor) nor `ChangeDungeonLevel` (`[L]`-key absolute / relative floor select) updated the stat.

Net effect: a player who entered the dungeon at floor N and descended via stairs kept `DeepestDungeonLevel = N` regardless of how deep they actually went. The bug report header for any player exploring the dungeon was generally wrong by the depth they had descended that session. This delayed root-cause investigation on the seal report (the floor-73 header initially read as "the player is confused about which floor has Regret") until the metadata bug itself was understood.

### Fix

Both `DescendStairs` and `ChangeDungeonLevel` now call `player.Statistics?.RecordDungeonLevel(currentDungeonLevel)` after the floor change resolves. `RecordDungeonLevel` itself is a one-line `if (level > DeepestDungeonLevel) DeepestDungeonLevel = level` so re-calls are cheap and idempotent. Bug-report metadata, the `Statistics.DeepestDungeonLevel`-keyed Steam achievement gate, and the bounty-board floor cap (`QuestSystem.RefreshBountyBoard` reads `DeepestDungeonLevel`) all now reflect the actual deepest floor the player has reached, not just their session-entry floor.

Files: `Scripts/Locations/DungeonLocation.cs` (descent paths).

---

## GMCP Char.Combat.Party (post-merge cleanup of PR #105)

PR #105 by Coosh added a new GMCP frame so MUD clients can render party HP bars and detect teammate deaths without text-pattern matching the combat output. Three follow-up issues caught in post-merge review and fixed in this release:

- **Package name aligned with the rest of the combat family.** Original PR used `Combat.Party` as a top-level package; the existing combat events live under `Char.Combat.*` (`Char.Combat.Start`, `Char.Combat.End`, `Char.Combat.Killed`). Mudlet scripts that subscribe by `Char.Combat.*` prefix would have missed `Combat.Party`. Renamed to `Char.Combat.Party`.
- **Class display string regression.** Payload built `@class = m.Class.ToString()`, which emits raw enum names (`MysticShaman`, `HalfElf`). The v0.53.0 audit (15+ files) deliberately moved every display path off `.ToString()` onto `Character.ClassName`, which routes through `GameConfig.ClassNames[]` for the player-facing display string (`Mystic Shaman`). Switched to `m.ClassName`.
- **Status changes did not trigger re-emits.** Snapshot string for change detection was `name|hp|maxHp|mana|maxMana|alive;` per member, but the payload includes `statuses`. A teammate getting poisoned, stunned, or hit with a debuff mid-round without HP movement would not re-push the new `statuses` list to the client until something else changed -- defeating the whole point of the frame for clients trying to render status icons. Snapshot now includes a sorted, comma-joined status-key list per member.

Comments at the four `EmitCombatPartyIfChanged` call sites in `CombatEngine.cs` updated to reference the new package name.

Files: `Scripts/Server/GmcpBridge.cs`, `Scripts/Systems/CombatEngine.cs` (comment updates).

---

## GMCP Char.Combat.Enemies (post-merge cleanup of PR #104)

PR #104 by Coosh added a per-round `Char.Combat.Enemies` emit so MUD clients see live enemy HP gauges throughout a fight (without it, the gauges seeded by `Char.Combat.Start` would freeze at starting HP until `Char.Combat.End`). The package name was right out of the gate, payload shape is a strict superset of `Char.Combat.Start` (adds `alive` and a top-level `round`), and routing `PlayerVsMonster` through `PlayerVsMonsters` covers both single and multi-monster PvE in one site. One follow-up:

- **No change-detection.** The PR fired the emit unconditionally at end of every round, breaking the change-detected pattern shared by every other per-round GMCP emit in this file (`EmitVitalsIfChanged`, `EmitCombatPartyIfChanged`). Wrapped in a new `EmitCombatEnemiesIfChanged(monsters, currentRound)` helper in `GmcpBridge.cs` that keys the snapshot on `name|hp|maxHp|alive;` per monster (the only fields that change between rounds; level / isBoss are stable across a fight). Round number is intentionally NOT in the snapshot -- including it would force an emit every round and defeat the point. Skips redundant frames on stunned / fully-resisted / heal-only rounds. New `LastGmcpEnemiesSnapshot` field on `SessionContext` holds the per-session delta.

PvP combat (`CombatEngine.cs:339`) is intentionally not covered -- it doesn't go through `PlayerVsMonsters`, and emitting "enemies" for a single PvP opponent would need its own scaffolding. Out of scope for this fix.

Files: `Scripts/Server/GmcpBridge.cs` (new helper + internal emit), `Scripts/Server/SessionContext.cs` (new snapshot field), `Scripts/Systems/CombatEngine.cs` (round-end call site swapped from inline emit to helper).

---

## Files Changed

- `Scripts/Core/GameConfig.cs` -- Version 0.60.9
- `Scripts/Locations/DungeonLocation.cs` -- Map-reveal events (`MysteryEventEncounter` Vision case, `NPCEncounter` Wounded Adventurer case) now call `TryDiscoverSeal` directly so seal discovery lands the same instant exploration crosses the threshold; `DescendStairs` and `ChangeDungeonLevel` now call `RecordDungeonLevel` after the floor change so `DeepestDungeonLevel` (and bug-report metadata, achievements, bounty-board) reflect post-descent depth.
- `Scripts/Server/GmcpBridge.cs` -- Renamed `Combat.Party` package to `Char.Combat.Party` for naming consistency with sibling combat events; switched `Class.ToString()` to `ClassName` to restore the v0.53.0 display-name fix; status keys now part of the change-detection snapshot so debuff applications re-emit. New `EmitCombatEnemiesIfChanged` / `EmitCombatEnemiesInternal` helpers wrap the `Char.Combat.Enemies` emit with the same change-detection pattern as the other per-round emits.
- `Scripts/Server/SessionContext.cs` -- New `LastGmcpEnemiesSnapshot` field for `Char.Combat.Enemies` per-session delta tracking.
- `Scripts/Systems/CombatEngine.cs` -- Inline comments at the four `EmitCombatPartyIfChanged` call sites updated to reference `Char.Combat.Party`. Inline `Char.Combat.Enemies` emit at the round-end replaced with a single call to `EmitCombatEnemiesIfChanged`.
- `DOCS/RELEASE_NOTES_0.60.9.md` -- This file.
