# v0.57.8 - Safe Havens Removed + Teammate Auto-Equip Class Check

Follow-up to v0.57.7. One design rip and one bug fix.

## Report 1 — Safe Havens Removed

Started with a bug report: the dungeon guide pointed to "Heart-Fire Sanctuary" as the nearest safe haven while Lumina was standing in Magma Chamber, which was itself also flagged as a safe haven. The v0.57.7 patch printed a "you're already in one" line above the destination list and kept everything else. Going back through that fix made it clear the whole feature earned its confusion.

## The feature didn't earn its slot

`IsSafeRoom` was a separate boolean flag layered on top of two different room types (`RoomType.MeditationChamber` and `RoomType.Settlement`). Two independent generator rules set it — one for Settlement rooms, one for MeditationChamber rooms — with no coordination, so a floor could end up with multiple. Settlement rooms hit it in `ConfigureSettlementRooms`; MeditationChamber rooms hit it in the per-type configuration pass.

Mechanically:

- Generic "make camp" in any cleared room: 25% HP / MP / STA restore, with a Blood Price penalty for murderers that drops it to 12.5-18.75%.
- Safe haven camp via `RestSpotEncounter`: 33% HP / MP / STA restore, cures poison, no Blood Price penalty, plus narrative content (Amnesia dream sequences, ghost companion buffs if Survivor's Guilt is active, Alethia appearances on floors 60-85).

Both gated on the same `hasCampedThisFloor` boolean. One camp per floor, no matter which room. So the second safe haven a floor generated was literally unreachable content after the first rest of any kind. And the mechanical delta between a safe haven and a generic cleared room was small enough that most players never noticed which they were in — the feedback showed up as "why does the guide say the safe haven is elsewhere when I'm in one."

## What changed

Ripped `IsSafeRoom` out entirely.

- Field deleted from `DungeonRoom`.
- Both generator sites that set it no longer touch the flag (`ConfigureSettlementRooms` and the per-type config for `RoomType.MeditationChamber`).
- `ShowDungeonNavigator` `[H] Nearest safe haven` entry removed from the dungeon guide. The "you're already in a safe haven" line added in v0.57.7 removed with it.
- `GetRoomStatusText` "Safe room" branch removed (rooms fall through to "Cleared" / "Explored").
- Map legend `~` marker removed.
- Loc keys `dungeon.status_safe`, `dungeon.nav_safe_haven`, and `dungeon.nav_safe_haven_here` dropped from all 5 language files.

## What stays

The narrative payload was never on the `IsSafeRoom` flag — it was on `HasEvent = true` + `EventType = RestSpot`, both of which MeditationChamber rooms still carry. Walking into a Meditation Chamber and investigating still fires `RestSpotEncounter` with its full effect: the 33% restore, the poison cure, the Amnesia dream sequences, the ghost companion buffs, the Alethia appearances between floors 60 and 85. Nothing in that encounter has changed. It just doesn't advertise itself through a second "safe haven" layer anymore.

Settlement rooms (The Outskirts, floor 10) are also unaffected — they route through `RoomType.Settlement` / `DungeonEventType.Settlement`, not through the safe-room flag.

### Save impact

None. `IsSafeRoom` was computed at dungeon-generation time and never serialized. Existing saves load and regenerate floors without the flag; the `hasCampedThisFloor` state they carry is preserved.

## Report 2 — Teammate auto-equip offered Aldric a bow

Krunch's screenshot: a Fine Short Bow dropped, the loot screen showed it was a -127 downgrade for the player so they picked `(P)ass`, and the v0.57.7 teammate auto-equip confirmation fired:

```
Aldric could equip this — Fine Short Bow would be a 800% upgrade.
Currently wearing:  Fine Leather Shield [Block:11 Def:+2 Con:+2]
Would replace with: Fine Short Bow [WP:11 Def:+1 Dex:+1 Agi:+1]

Let Aldric take it? (Y/N) y
  You nod. Aldric gears up.
The item is left behind.
```

Aldric is a Tank-role companion and cannot equip bows — the check is right there at [Items.cs:1011-1022](Scripts/Core/Items.cs#L1011-L1022) where Aldric is restricted to swords/axes/maces/hammers/flails/mauls/greatswords/greataxes. But `TryTeammatePickupItem` never called `Equipment.CanEquip`; it did its own ad-hoc scan via `CanClassUseLootItem` (template-name matching) plus an ability-weapon-requirement check, neither of which sees the companion-specific restriction in `CanEquip`. The bow slipped through as a candidate, got a huge upgrade score (800% because Aldric's OffHand had a shield, not a weapon, and a shield scores 0 in the weapon-scoring formula), the prompt fired, the player said yes, the actual `EquipItem` call then failed silently on the restriction, and the player saw the contradictory "Aldric gears up" / "The item is left behind" pair.

**Fix.** Two changes.

1. In `TryTeammatePickupItem`, after the class-restriction and level checks, build the hypothetical Equipment via `ConvertLootItemToEquipment` and call `Equipment.CanEquip(teammate, out _)`. If it returns false — for *any* reason: class restriction, companion-specific weapon list, Mystic Shaman weapon list, alignment, armor weight, level, strength — skip this teammate entirely. This is the authoritative check the actual equip path uses; using the same function guarantees the prompt and the equip agree.

2. Moved "You nod. Aldric gears up." out of `ConfirmTeammateAutoEquip`. The helper used to print it as soon as the player pressed Y, before `EquipItem` ran. Now it prints only in the success branch of both loot paths (single-monster and grouped multi-monster), immediately before the "picks up" success broadcast. If the equip falls through for some reason, the player sees only "The item is left behind" instead of the contradictory pair. The helper still prints the "You shake your head" decline line when the player presses N.

Between fix #1 (which eliminates the known class-restriction silent-fail) and fix #2 (which prevents the specific UX artifact from recurring even if some other late validation fires), the contradictory output is gone in both the expected path and any edge cases.

## Files Changed

- `Scripts/Core/GameConfig.cs` — Version 0.57.8.
- `Scripts/Systems/DungeonGenerator.cs` — `DungeonRoom.IsSafeRoom` field deleted. Both generator sites that set it (`ConfigureSettlementRooms`, MeditationChamber per-type config) no longer touch it.
- `Scripts/Locations/DungeonLocation.cs` — `ShowDungeonNavigator` no longer emits the `[H] Nearest safe haven` entry or the "you're already in one" line. `GetRoomStatusText` "Safe room" branch removed. Dungeon map legend `~` marker removed.
- `Scripts/Systems/CombatEngine.cs` — `TryTeammatePickupItem` now calls `Equipment.CanEquip` on a hypothetical conversion of the loot item before accepting the teammate as a candidate. `ConfirmTeammateAutoEquip` no longer prints the approved line; both loot paths (`~line 7869` single-monster and `~line 8315` grouped multi-monster) now print "You nod. X gears up." only after `EquipItem` returns true.
- 5 localization files (en/es/fr/hu/it) — Keys `dungeon.status_safe`, `dungeon.nav_safe_haven`, `dungeon.nav_safe_haven_here` dropped.
- Tests: 596 / 596 passing.
