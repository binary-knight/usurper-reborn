# Usurper Reborn v0.54.1 - The Soul Update (Hotfix)

**Release Date**: April 6, 2026

## Balance Changes

### Session XP Diminishing Returns Now Scale with Level

The session XP fatigue threshold was a flat 50,000 XP regardless of level. At level 71, the XP needed for a single level is ~259,000 — meaning players hit diminishing returns at just 19% of a level. High-level players couldn't even complete a full level in one session without penalties.

**Fix**: The threshold now scales with level using the formula `max(50,000, XP_for_next_level * 2)`, always allowing ~2 full levels of XP per session before any reduction begins.

| Level | Old Threshold | New Threshold |
|-------|--------------|---------------|
| 10    | 50,000       | 50,000        |
| 30    | 50,000       | 96,100        |
| 50    | 50,000       | 260,100       |
| 71    | 50,000       | 518,400       |
| 100   | 50,000       | 1,020,100     |

The diminishing rate (0.2% per 1000 XP over threshold) and floor (25% minimum) are unchanged.

### NPC Catch-Up XP Uncapped

NPC teammate catch-up XP was capped at 4x. Severely underleveled NPCs (e.g., 50 levels behind) were stuck at the same 4x multiplier as NPCs only 30 levels behind, making it very slow for them to close the gap. The cap has been removed — underleveled NPCs now scale freely at +10% per level behind (e.g., 50 levels behind = 6x XP).

## Bug Fixes

### MysticShaman World Sim Crash (BBS Sysops)

BBS sysops running online mode saw a daily error: `Failed to load world state: The given key 'MysticShaman' was not present in the dictionary. Initializing fresh.` This caused the world sim to reinitialize all NPCs from scratch every day, wiping NPC state.

**Root cause**: `NPCSpawnSystem.RebalanceClassDistribution()` built a dictionary of class counts for the 11 base classes (0-10) but didn't account for MysticShaman (enum 16). When any MysticShaman NPC existed, the LINQ donor pool queries accessed the dictionary with the MysticShaman key, throwing `KeyNotFoundException`. The exception propagated up to `WorldSimService.LoadWorldState()` which caught it and reinitialized fresh.

**Fix**: All dictionary accesses in `RebalanceClassDistribution()` now use `ContainsKey()` checks. MysticShaman and prestige class NPCs are excluded from the rebalancing pool (they should never be reassigned to a different class anyway).

## Files Changed

- `Scripts/Core/GameConfig.cs` — Version 0.54.1; `SessionXPDiminishThreshold` replaced with `SessionXPDiminishBaseThreshold` + `GetSessionXPThreshold(int level)` method; `CatchUpMaxMultiplier` uncapped
- `Scripts/Systems/CombatEngine.cs` — Session XP diminishing uses level-scaled threshold in all 3 paths (solo victory, multi-monster victory, grouped player); updated catch-up XP comment
- `Scripts/Locations/BaseLocation.cs` — `/health` session fatigue display uses level-scaled threshold
- `Scripts/Systems/NPCSpawnSystem.cs` — `RebalanceClassDistribution()` guards all `classCounts` dictionary accesses with `ContainsKey()` to prevent crash on MysticShaman/prestige class NPCs
