# v0.46.2 - Ascension

Accessibility improvements, balance tuning, BBS online mode default.

---

## Balance Changes

### Weapon Power Soft Cap Retuned

The v0.46.1 weapon power soft cap (500 threshold, 30% above) was too aggressive for normal high-level players. A level 80 Barbarian with 2,470 WeapPow lost 56% of attack power AND 49% of defense, making level-appropriate dungeons nearly unplayable even with fully geared companions.

**Threshold raised from 500 to 800** — Normal high-level progression is no longer punished. The cap only kicks in for players with extreme equipment stacking.

**Diminishing rate raised from 30% to 50%** — Power above the threshold retains half its value instead of less than a third, making the curve feel less like hitting a wall.

**Armor power cap removed** — Defense was never the balance problem (the original issue was 57k offensive crits). ArmPow now passes through at full value, so players aren't taking extra damage on top of dealing less.

Impact comparison at the new settings:

| Player | WeapPow | Old Effective | New Effective | ArmPow Change |
|--------|---------|---------------|---------------|---------------|
| Quent (L80) | 2,470 | 1,091 (-56%) | 1,635 (-34%) | Full (was -49%) |
| fastfinge (L95) | 1,710 | 863 (-50%) | 1,255 (-27%) | Full (was -31%) |

---

## New Features

### BBS Doors Default to Online Mode

BBS door mode now automatically enables online/shared-world mode. The `--online` flag is no longer required — any BBS door command (`--door32`, `--door`, `--doorsys`, `--node`) implies online mode. Existing commands with `--online` continue to work for backwards compatibility.

SysOps can simplify their door commands from:
```
UsurperReborn --online --door32 %f
```
to just:
```
UsurperReborn --door32 %f
```

### Dungeon Navigator (`[N]`)

New `[N] Nav` option in the dungeon gives step-by-step compass directions to key points of interest. Available to all players alongside the existing `[M] Map`.

Select a destination to get the shortest path:

- `[U]` Nearest unexplored room
- `[C]` Nearest uncleared room (monsters)
- `[S]` Stairs down (if discovered)
- `[B]` Boss room (if discovered and undefeated)

Each option shows the destination room name and distance. Selecting one prints directions like "Path to Dark Corridor: North, North, East". Uses BFS pathfinding through explored rooms. Originally built for screen reader accessibility based on blind player feedback, but useful for all players navigating large dungeon floors.

In screen reader mode, pressing `[M]` also opens the navigator instead of the ASCII map.

---

## Accessibility

### "Fully Cleared" Exit Annotations (Screen Reader Mode)

Dungeon exits now show `(all clear)` or `(fully cleared)` instead of `(clr)`/`(cleared)` when every reachable room in that direction has been cleared. This lets screen reader players skip entire branches they've already finished when going for a floor clear, without needing to explore dead ends to verify.

---

### Companion Navigation Comments

Companions and teammates now comment on dungeon navigation, adding flavor and subtle guidance:

- **After clearing a room**, a companion may suggest which direction to go next (toward uncleared or unexplored rooms). Each companion has their own personality: Vex is sarcastic ("So are we going north or are we just standing around?"), Aldric is tactical ("Form up. We move east."), Lyris is mystical ("I sense something to the west..."), and Mira is gentle ("Maybe we should try south?"). Generic teammates get straightforward lines.

- **When backtracking into fully-cleared areas**, companions may comment on the wasted effort. Vex mocks you ("Are you lost or something? There's nothing left this way."), Aldric stays focused ("This ground is cleared. We should press forward."), Lyris senses the emptiness, and Mira gently redirects.

Comments trigger randomly (not every room) to avoid spam.

---

## Files Changed

| File | Changes |
|------|---------|
| `Scripts/Core/GameConfig.cs` | Version 0.46.2 |
| `Scripts/Systems/CombatEngine.cs` | Weapon power soft cap retuned: threshold 500→800, rate 30%→50%; armor power cap removed (pass-through) |
| `Scripts/BBS/DoorMode.cs` | BBS doors auto-enable online mode; `--online` flag still accepted for backwards compatibility; updated help text |
| `Scripts/Locations/DungeonLocation.cs` | Dungeon navigator (`[N]`): `ShowDungeonNavigator()` with BFS pathfinding, `BuildDirectionPath()`, quicknav menu; `IsDirectionFullyCleared()` for exit annotations; `TryCompanionNavigationComment()` and `TryCompanionBacktrackComment()` for companion dialogue |
