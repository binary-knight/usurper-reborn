# v0.57.7 - Parenting Cooldown Hotfix

Another report from Lumina:

> "Spending time with children seems to be broken. The game claims I spent time with the chosen child, even if I did not that day. I could spend time with newly born child, but not the rest. Now with any of them."

## The bug

The "Spend Time with Child" interaction at Home uses a daily cooldown so players can only parent each child once per day. The old check:

```
int currentDay = DailySystemManager.Instance?.CurrentDay ?? 0;
if (selectedChild.LastParentingDay >= currentDay && currentDay > 0) { ...block... }
```

That relied on `DailySystemManager.Instance.CurrentDay` advancing once per real-world day. Works fine in single-player. Breaks in MUD mode because:

- `DailySystemManager` is a process-wide singleton.
- Every player login restores `currentDay` from *that player's* save via `currentDay = saveData.CurrentDay`.
- So a different player logging in flips the singleton to their day count.
- And `lastResetTime` is also reloaded from save, which can defer the next real-time daily tick past 24 hours.

End result: the singleton's `currentDay` doesn't reliably increment for any given player. Once `LastParentingDay` was written to the singleton's current value, `LastParentingDay >= currentDay` stayed true on every subsequent check until a daily reset actually fired. Sometimes for days. Sometimes indefinitely.

Lumina's specific progression matches this exactly: a freshly-born child has `LastParentingDay = 0`, so the check `0 >= currentDay (N > 0)` is false → interaction allowed. After that first interaction sets it to `N`, `N >= N` is true on every subsequent visit → blocked forever until the daily tick.

## Fix

Switched to a wall-clock cooldown. `Child` now carries a `LastParentingTime` DateTime field; the interaction check requires 20 real-world hours elapsed since the last interaction (gives a 4-hour tolerance before "tomorrow"). No singleton dependency, no cross-session contamination, advances by definition every real-world day.

Existing saves' `LastParentingDay` field is preserved for reader compatibility but no longer consulted for the check. `LastParentingTime` defaults to `DateTime.MinValue` on legacy saves, which is ~19 years in the past, so `UtcNow - MinValue > 20 hours` is trivially true — players with stuck cooldowns can interact once on login, which sets a real timestamp and the system self-heals from there.

### Files Changed

- `Scripts/Core/GameConfig.cs` — Version 0.57.7.
- `Scripts/Core/Child.cs` — New `DateTime LastParentingTime` field; old `LastParentingDay` kept for save-reader compatibility (documented as legacy).
- `Scripts/Systems/SaveDataStructures.cs` — `LastParentingTime` added to `ChildData`.
- `Scripts/Systems/FamilySystem.cs` — Serialize + deserialize the new field.
- `Scripts/Locations/HomeLocation.cs` — `InteractWithChild` now gates on `DateTime.UtcNow - LastParentingTime < 20h` instead of the singleton day counter. Post-interaction writes both the new timestamp (authoritative) and the legacy day counter (backward-compat).
