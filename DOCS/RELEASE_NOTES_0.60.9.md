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

## Bug fix: Marked +30% bonus damage skipped by single-target class abilities

Player report (Lv.25 Cleric, online): *"Shield bash doesn't get the bonus damage from marked. I'm not sure if this is intentional."*

Not Shield-Bash-specific. The Marked +30% bonus is a debuff that ranger / assassin / hunter abilities apply to a target, and any subsequent damage against that target is supposed to deal +30%. Three damage paths apply damage to monsters:

1. Basic attacks -> `ApplySingleMonsterDamage` -> Marked bonus check at line ~11234 ✓
2. AoE abilities -> `ApplyAoEDamage` -> Marked bonus check at line ~11114 ✓
3. Single-target class abilities -> `ApplyAbilityEffects` (single-monster) and `ApplyAbilityEffectsMultiMonster` (multi) -> direct `target.HP -= actualDamage` with no Marked check ✗

So Shield Bash, Backstab, Holy Smite, Power Strike, Riptide Strike, Wave Echo, every single-target class ability silently dropped the Marked bonus. Reporter happened to notice it on Shield Bash (likely a Warrior NPC teammate, since Shield Bash is Warrior-only and they were a Cleric) but every class with an offensive ability was missing the bonus on Marked targets.

### Fix

Both ability damage paths (`ApplyAbilityEffects` line ~20574 and `ApplyAbilityEffectsMultiMonster` line ~12836) now apply the same Marked +30% check the basic-attack path uses, in the same position (after defense reduction, before HP subtraction). Existing flavor line `combat.marked_bonus` reused so the player sees the bonus call out the same way it does on basic attacks.

Files: `Scripts/Systems/CombatEngine.cs` (two ability damage paths).

---

## Bug fix: skill-reset narration assumed male Level Master

Player report (Lv.25 Cleric, online): *"If you want to reset your skills, the message doesn't respect level master gender. I had Seraphina the Radiant and 'he leaned forward', but I suspect it is a she."*

The Level Master cycles between three NPCs by player alignment:

- **Seraphina the Radiant** (Good) -- female
- **Zharkon the Grey** (Neutral) -- male
- **Malachar the Dark** (Evil) -- male

Four narration lines in the skill-reset flow had hardcoded masculine pronouns:

- `training.reset_lore_4` -- "**He** gestures to a shelf..."
- `training.single_reset_lore_2` -- "...behind **him**."
- `training.single_reset_lore_5` -- "**his** thumb to your forehead..."
- `training.all_reset_lore_5` -- "**He** pours an entire flask..."

Good-aligned players (Seraphina) saw misgendered text. Neutral / evil players never noticed because their masters happen to be male.

### Fix

Rewrote the four lines to gender-neutral phrasing across English / Spanish / French / Italian. Hungarian was already pronoun-neutral, no change needed. The recasts mirror the disembodied imagery already used at `training.all_reset_lore_6` ("Both hands press against your temples") so the style stays consistent:

- "He gestures..." → "A hand gestures..."
- "...on the shelf behind him." → "...from a shelf along the wall."
- "his thumb..." → "a thumb..."
- "He pours an entire flask..." → "An entire flask... empties over your head."

Cleaner than threading a `MasterInfo.IsFemale` field through `TrainingSystem`'s static call chain, and avoids the multilingual headache of language-specific pronoun grammar (French / Italian / Spanish all gender adjectives and verbs differently from English).

Files: `Localization/en.json`, `Localization/es.json` (1 line), `Localization/fr.json` (3 lines), `Localization/it.json` (1 line). Hungarian unchanged.

---

## Bug fix: NullReferenceException when changing language in /prefs while in the dungeon

Player report (Lv.73 Mystic Shaman, online): *"If I change language in /prefs from Hungarian to English while in the dungeon, I get an Object reference not set to an instance of an object error."*

`DungeonLocation.InvalidateFloorCache()` (called from `BaseLocation.HandlePreferences` after the player picks a new language) was a one-liner that just set `currentFloor = null!`. The original comment claimed "regenerates with current language on next entry" -- but `EnterLocation` is the only path that null-checks and regenerates, and `/prefs` returns to the dungeon's location loop, NOT to `EnterLocation`. The loop's next redisplay accessed `currentFloor.Rooms` / `GetCurrentRoom()` and NREd.

### Fix

Inlined the regeneration. If `currentFloor != null && currentPlayer != null` (player is mid-dungeon), `InvalidateFloorCache` now calls `SaveFloorState(currentPlayer)` -> `GenerateOrRestoreFloor(currentPlayer, currentDungeonLevel)` -> rebuilds `currentFloor` with the new-language strings baked in. `GenerateOrRestoreFloor` is deterministic per floor level and restores `IsExplored` / `IsCleared` / `TreasureLooted` / `EventCompleted` / `CurrentRoomId` from `DungeonFloorState`, so room progress is preserved across the language switch. `roomsExploredThisFloor` is also recomputed from the restored room states.

If `currentFloor == null` (player never entered the dungeon this session), nulling stays the right behavior -- the next entry generates fresh.

The fix only triggers on language change while in the dungeon. Town location loops were unaffected by the original bug because they don't read dungeon state.

Files: `Scripts/Locations/DungeonLocation.cs`.

---

## Bug fix: poison duration didn't tick down during combat

Player report (Lv.25 Cleric, online): *"Shouldn't poison tick down in combat? I was poisoned from a trap, I moved to the boss room and defeated the boss in 5 rounds. When I arrived to the room I had 3 turns left, when I moved away I had 2."*

The reporter expected 6 ticks consumed (1 from room movement entering the boss + 5 from combat rounds), which would have cleared the poison entirely. Actual: 1 tick consumed total. Long boss fights with poison effectively pinned the duration since combat rounds took zero off the counter.

`CombatEngine`'s round-start poison block (`CombatEngine.cs:1086-1097`) applied poison damage every round but never decremented `PoisonTurns`. The matching path in `BaseLocation.ApplyPoisonDamage` (which fires on room movement) does both the damage AND the tick AND the clear-on-expiry. So poison damage flowed through combat correctly, the duration just never moved.

### Fix

Added the tick + clear logic to `CombatEngine`'s combat-round poison block, mirroring `BaseLocation.ApplyPoisonDamage`:

- Decrement `PoisonTurns` after damage applies
- Legacy-save migration (set `PoisonTurns = max(5, Poison * 2)` if the field was 0 but `Poison > 0`)
- When `PoisonTurns` hits 0, clear `Poison = 0` and print `base.poison_cleared` so the player sees the duration end

5-round boss fights now consume 5 turns of poison. Long fights can clear poison naturally without needing an antidote / temple visit / dungeon shrine. Doesn't change damage values, just makes the duration counter actually count.

PvP combat (`PlayerVsPlayer`) is unaffected -- it doesn't have a per-round `player.Poison` tick block (PvP's poison-related code is the weapon-coating mechanic, a different system). Out of scope for this fix; PvP fights are typically short enough that poison duration mid-fight is a rare scenario.

Files: `Scripts/Systems/CombatEngine.cs`.

---

## Files Changed

- `Scripts/Core/GameConfig.cs` -- Version 0.60.9
- `Scripts/Locations/DungeonLocation.cs` -- Map-reveal events (`MysteryEventEncounter` Vision case, `NPCEncounter` Wounded Adventurer case) now call `TryDiscoverSeal` directly so seal discovery lands the same instant exploration crosses the threshold; `DescendStairs` and `ChangeDungeonLevel` now call `RecordDungeonLevel` after the floor change so `DeepestDungeonLevel` (and bug-report metadata, achievements, bounty-board) reflect post-descent depth.
- `Scripts/Server/GmcpBridge.cs` -- Renamed `Combat.Party` package to `Char.Combat.Party` for naming consistency with sibling combat events; switched `Class.ToString()` to `ClassName` to restore the v0.53.0 display-name fix; status keys now part of the change-detection snapshot so debuff applications re-emit. New `EmitCombatEnemiesIfChanged` / `EmitCombatEnemiesInternal` helpers wrap the `Char.Combat.Enemies` emit with the same change-detection pattern as the other per-round emits.
- `Scripts/Server/SessionContext.cs` -- New `LastGmcpEnemiesSnapshot` field for `Char.Combat.Enemies` per-session delta tracking.
- `Scripts/Locations/DungeonLocation.cs` (above, plus): `InvalidateFloorCache` now regenerates the floor inline when called mid-dungeon (e.g. from `/prefs` language change) instead of just nulling `currentFloor` -- the dungeon loop's next redisplay used to NRE on the null reference.
- `Scripts/Systems/CombatEngine.cs` -- Inline comments at the four `EmitCombatPartyIfChanged` call sites updated to reference `Char.Combat.Party`. Inline `Char.Combat.Enemies` emit at the round-end replaced with a single call to `EmitCombatEnemiesIfChanged`. Marked +30% bonus damage check added to both `ApplyAbilityEffects` (single-monster) and `ApplyAbilityEffectsMultiMonster` (multi-monster) ability damage paths, mirroring the existing basic-attack and AoE pathways. Round-start poison block (`player.Poison` damage tick) now also decrements `PoisonTurns` and clears `Poison = 0` when duration expires, matching `BaseLocation.ApplyPoisonDamage`'s tick+clear logic so poison duration moves forward during combat.
- `Localization/en.json`, `Localization/es.json`, `Localization/fr.json`, `Localization/it.json` -- Four skill-reset narration lines rewritten to gender-neutral phrasing so Seraphina the Radiant (female Good-alignment Level Master) is no longer described with masculine pronouns. Hungarian unchanged (already pronoun-neutral).
- `DOCS/RELEASE_NOTES_0.60.9.md` -- This file.
