# Usurper Reborn - v0.29.2 Bug Fixes & Public Dashboard

Bug fixes for children/world event persistence and stale NPC team cleanup. The NPC Analytics Dashboard ("The Observatory") is now public.

---

## Public NPC Analytics Dashboard

The Observatory dashboard at `/dashboard` is now publicly accessible — no login required. Anyone can watch 60+ NPCs live their lives in real-time through personality radars, relationship networks, demographic charts, and live event feeds.

- Removed PBKDF2 authentication, session tokens, login/register UI
- Dashboard loads immediately on page visit
- Added "Observatory" link in main site navigation
- Added Observatory connect card in the "How to Connect" section

---

## Bug Fixes

### Children/World Events Lost on Player Login (Critical)
When loading shared data from `world_state` (children, world events), the `Count > 0` guard prevented empty lists from being deserialized. This meant:
- If all children aged to 18 and were converted to NPCs, the empty list wouldn't clear stale children from the player's save
- If no world events were active, stale events from the player's save persisted

**Fix**: Removed `Count > 0` checks from `GameEngine.LoadSharedChildrenAndMarriages()` and `WorldSimService.LoadChildrenState()`. Empty lists now properly clear stale data.

### 1-Member NPC Teams Persist Indefinitely (Medium)
8 of 19 NPC teams had only 1 member. When team members left via betrayal or death, the code only dissolved teams that dropped to **exactly 0 members** — a team of 1 was never cleaned up. Solo NPCs in dead-end teams couldn't participate in team wars, recruit new members, or leave.

**Fix**:
- `CheckTeamBetrayals()` now dissolves 1-member teams when a member departs (added `else if (remainingMembers == 1)` branch)
- Added periodic cleanup pass at the start of `CheckTeamBetrayals()` that finds and dissolves all existing 1-member teams
- Solo members get their team/password/turf cleared and a disbandment news message is posted

### NPC Goal Completion News Spam (Critical)
Two bugs combined to spam "formed a powerful new alliance!" every 30 seconds:

1. **Re-completion**: `UpdateGoals()` iterated all goals without checking `IsCompleted` or `IsActive`, so completed goals whose condition remained true re-fired `OnGoalCompleted()` every tick.
2. **Dual team system mismatch**: NPCs have both a legacy `Team` field and a modern `GangId` field. Gang goal *creation* in `NPCBrain` and `NPC` only checked `Team` (empty = "not in a gang"), but goal *completion* in `GoalSystem` checked `GangId`. An NPC with a `GangId` but no `Team` would get a new "Find Gang to Join" goal every tick that instantly completed.

**Fix**:
- Added `if (goal.IsCompleted || !goal.IsActive) continue;` at the top of the goal update loop
- All three `ProcessGangBehavior()` methods now check both `Team` AND `GangId` before creating gang goals
- Added duplicate goal guards — no new gang goal if one already exists (active or completed)
- Added periodic pruning of completed goals older than 24 hours when goal count exceeds 30

### NPC Pregnancies Never Produce Children (Critical)
20+ pregnancy announcements were generated but zero children were ever born. Two bugs:

1. **Player session autosave race condition**: When a player session autosaves, it writes ALL NPCs to `world_state` — including stale data from when the player loaded. The world sim detects the version change and reloads all NPCs, losing any `PregnancyDueDate` values it had set since the last save. This meant pregnancies were created, then silently wiped every time a player autosaved.
2. **Father-not-found silently discards pregnancy**: When a pregnancy reaches its due date, the code looked up the father (alive and not dead). If the father died during the 7-hour gestation, the pregnancy was silently discarded (`PregnancyDueDate = null` was set outside the success check).

**Fix**:
- World sim now captures active pregnancies before reloading NPCs from DB, then merges them back after reload
- Birth code now falls back to dead fathers (for the child record) and even creates the child if the father is completely gone
- Added console logging for births and pregnancy restoration
- Dashboard stats API children count now reads from `world_state["children"]` (authoritative) instead of player saves (stale)

---

## Files Changed

| File | Changes |
|------|---------|
| `Scripts/Core/GameConfig.cs` | Version bump to 0.29.2 |
| `Scripts/Core/GameEngine.cs` | Removed `Count > 0` guard on children and world events loading in `LoadSharedChildrenAndMarriages()` |
| `Scripts/Systems/WorldSimService.cs` | Removed `Count > 0` guard on children loading; preserve active pregnancies during player-triggered NPC reload |
| `Scripts/Systems/WorldSimulator.cs` | Added 1-member team cleanup; fixed birth code to handle dead/missing fathers instead of silent discard |
| `web/ssh-proxy.js` | Removed dashboard auth; fixed children count to read from `world_state` instead of player saves |
| `web/dashboard.html` | Removed login screen, auth CSS/JS; dashboard loads immediately |
| `web/index.html` | Added "Observatory" nav link and connect card |
| `Scripts/AI/GoalSystem.cs` | Skip completed/inactive goals in `UpdateGoals()` loop; prune old completed goals |
| `Scripts/AI/NPCBrain.cs` | Check both `Team` and `GangId` in `InitializeGangBehavior()` and `ProcessGangBehavior()`; duplicate goal guards |
| `Scripts/Core/NPC.cs` | Check both `Team` and `GangId` in `ProcessGangBehavior()`; duplicate goal guards |
