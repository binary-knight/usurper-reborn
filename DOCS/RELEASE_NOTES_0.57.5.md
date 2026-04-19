# v0.57.5 - Editor Menu Redraw Hotfix

Follow-up hotfix for v0.57.4. A Steam player reported the editor's arrow-key menu stacking down the screen instead of redrawing in place — every arrow press appended a fresh copy of the menu below the previous one, producing a "staircase" of Main Menu titles marching down the window.

## The staircase bug

`RunArrowMenu` redraws by moving the cursor up N lines (relative ANSI `CSI nA`) and erasing below. The N counter is incremented once per `AnsiConsole.MarkupLine` call, under the assumption that one MarkupLine call = one visible terminal row.

That assumption holds until a rendered line exceeds the terminal width and wraps. Two places produced lines longer than 80 cols on the default WezTerm window:

1. **Title + hint crammed onto one line.** The concatenation `"  Main Menu  (arrows/PgUp/PgDn/Home/End, numbers, Enter picks, 0/Q/Esc back — showing 1-5/5)"` ran to ~93 visible chars. WezTerm wrapped it to 2 rows; `lineCount++` counted 1. Next redraw moved up 1 row too few, leaving one stale row behind. Stacked up after a few arrow presses.
2. **Long item labels.** Rows like `Player Saves            — edit characters (stats, inventory, quests, companions, etc.)` ran to ~85 chars after the cursor/number prefix. Same wrap-undercount.

## Fix

- **Split the title + hint onto two short lines** that individually fit in 80 cols. Title + counter (`Main Menu   (1-5/5)`) on one line, key reference (`arrows, numbers, Enter picks, 0/Q/Esc back`) on another.
- **Truncate over-wide item labels** at render time using `Console.BufferWidth - 11` as the budget (the `  > NN  ` prefix is ~9 visible chars plus a 2-col safety margin for the selected-row reverse-video padding). Labels longer than the budget get sliced with a `…` suffix so no row ever wraps.
- **Dropped the one Unicode bullet (`•`)** from the hint line for plain ASCII commas. Not every bundled WezTerm font has that glyph, and a missing-glyph fallback can render double-wide and push the visible width past 80 even on an otherwise-short line.

### Files Changed

- `Scripts/Core/GameConfig.cs` — Version 0.57.5.
- `Scripts/Editor/EditorIO.cs` — `RunArrowMenu` title/hint split onto separate lines with ASCII-only key reference. Item labels measured against `Console.BufferWidth` and truncated with `…` before rendering. Viewport-count now matches actual emitted rows 1:1 regardless of terminal width, so the relative-cursor redraw sequence lands cleanly every iteration.
