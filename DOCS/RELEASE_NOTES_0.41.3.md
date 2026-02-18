# v0.41.3 - Love Street Overhaul

Complete overhaul of the Love Street location. Replaces three underused features with engaging, love-themed gameplay that gives players reasons to visit daily.  Users can now set their own 'theme' by visiting the ~ preference menu off main street...classic Usurper theme created by taking ANSI codes directly from old PASCAL source code.

---

## New Features

### Mingle [M] (replaces "Meet NPCs")
A romance-focused NPC interaction system. Shows NPCs currently at Love Street, padded with compatible NPCs from other locations (up to 8 total) so the list is never empty. Each NPC displays their level, race, class, relationship tag ([Spouse], [Lover], [Friend], [Stranger]), and a personality hint based on their top trait.

Selecting an NPC opens a sub-menu:
- **[F] Flirt** — CHA-based success check using personality receptiveness. Success grants +2 relationship steps (+4 with Allure potion). Failure produces an awkward moment. Once per NPC per visit.
- **[C] Compliment** — Always succeeds, +1 relationship step. Race and class-specific flavor text.
- **[B] Buy a Drink (50g)** — +2 relationship steps and reveals one personality trait (romanticism, commitment, or sensuality level). Revealed traits persist for the session.
- **[D] Ask on a Date** — Redirects to the existing date system.
- **[T] Talk** — Falls back to the standard dialogue system.

### Gossip Corner [V] — Complete Overhaul
Now uses real NPC relationship data from the world simulation instead of showing only the player's own romances.

- **[1] Who's Together (100g)** — Lists all NPC-NPC marriages and player marriages from the marriage registry.
- **[2] Juicy Scandals (200g)** — Shows active affairs with suspicion level hints ("Word is X has been seen with Y behind Z's back!").
- **[3] Who's Available (100g)** — Lists single NPCs compatible with the player's orientation. Shows personality hints and current relationship level.
- **[4] Investigate Someone (300g)** — Pick an NPC from a numbered list to learn their orientation, romanticism, commitment style, sensuality, relationship preference, current status, their opinion of you, and a compatibility assessment.

### Love Potions [L] (replaces "Romance Stats")
A witch's potion shop offering temporary romance buffs. All effects are session-scoped (no save data changes).

- **[1] Philter of Charm (500g)** — +3 CHA bonus to all Love Street flirt and date checks for this visit.
- **[2] Elixir of Allure (2,000g)** — Next flirt auto-succeeds with +4 relationship steps instead of +2. Single use.
- **[3] Draught of Forgetting (3,000g)** — Immediately reduces all partner jealousy by 30 points. Permanent effect.
- **[4] Passion Potion (5,000g)** — Next date guarantees an intimacy invitation regardless of relationship level. Single use.
- **[5] My Romance Stats** — The old Romance Stats screen (spouses, lovers, FWB, exes, encounters) is preserved here as a sub-option.

Active potion effects are shown as indicators on the Love Street display.

### Gift Shop [G] — Improvements
- **Numbered NPC list** — No more typing names. NPCs you know are listed with numbers, relationship tags, and sorted by closeness.
- **3 new luxury gift tiers** — Enchanted Locket (25,000g, +25 relationship), Moonstone Tiara (50,000g, +30 relationship), Star of Eternity (100,000g, +40 relationship).
- **Reaction preview** — Before confirming a purchase, see how the NPC would react based on attraction and relationship level ("They would love this!", "They'd appreciate it", "They might not accept...").

### Player Color Themes
Players can now choose from 5 color themes via the Preferences menu (`/prefs` or `[4]` from Preferences). Themes remap all game colors before output, affecting every screen in the game.

- **Default** — Bright, modern colors (current look)
- **Classic Dark** — Muted colors inspired by the original 1993 Usurper. Bright colors become normal equivalents for a darker, retro BBS feel.
- **Amber Retro** — Monochrome amber CRT phosphor look. Everything rendered in shades of yellow/brown.
- **Green Phosphor** — Monochrome green CRT phosphor look. Everything rendered in shades of green.
- **High Contrast** — Maximum brightness for readability. All colors pushed to their brightest variants.

Theme selection persists across sessions and works in all modes (local, BBS door, online, MUD).

### New Title Screen
New Title Screen.  If you're an ANSI artist and want to make one for me, I will gladly requsition your talents!!!

### SysOp Default Color Theme
SysOps can now set a default color theme for their BBS via the SysOp Console (`[T] Color Theme`). New characters automatically start with the SysOp's chosen theme. Players can still change their own theme in preferences. Setting persists in `sysop_config.json`.

### BBS Idle Timeout & Session Time Limit
BBS callers who are idle for too long are now automatically saved and disconnected. Also enforces the session time limit from the BBS drop file (DOOR32.SYS / DOOR.SYS `TimeLeftMinutes`).

- **Default timeout**: 15 minutes of inactivity
- **Warning**: Players see a warning 1 minute before being disconnected
- **Auto-save**: Game is saved before disconnecting so no progress is lost
- **Session time limit**: If the BBS drop file specifies a time limit, it is now enforced. Players are auto-saved and disconnected when their session time expires.
- **SysOp configurable**: Idle timeout can be set from 1-60 minutes via:
  - `--idle-timeout <minutes>` command-line flag
  - `[I] Idle Timeout` option in the SysOp Console (persisted to `sysop_config.json`)
- Applies to both BBS door mode and `--online` mode
- MUD server mode is unaffected (has its own 15-minute watchdog)

---

## Improvements

### Gold Tracking
Added `RecordGoldSpent()` calls to all Love Street gold-spending paths that were previously untracked: courtesans, gigolos, dates, gifts, potions, gossip payments, and drinks.

### NPC Location Mapping
Added explicit `GameLocation.LoveCorner => "Love Street"` mapping in `GetNPCLocationString()` instead of relying on the default fallback.

---

## Files Changed

| File | Changes |
|------|---------|
| `Scripts/Locations/LoveStreetLocation.cs` | Complete overhaul: Mingle system (NPC sourcing, flirt/compliment/drink sub-menu, personality hints), Gossip overhaul (marriages, affairs, availability, investigation from real registry data), Love Potions shop (4 potions + old stats as sub-option), Gift Shop improvements (numbered list, luxury tiers, reaction preview), per-visit potion state fields, EnterLocation override, active potion indicators, RecordGoldSpent on all purchase paths |
| `Scripts/Locations/BaseLocation.cs` | Added `GameLocation.LoveCorner => "Love Street"` to `GetNPCLocationString()` switch; added `[6] Color Theme` to Preferences menu with cycle-through selection |
| `Scripts/Core/GameConfig.cs` | Version bump to 0.41.3, added Love Street constants, BBS idle timeout constants, `DefaultColorTheme` static property for SysOp-configurable default |
| `Scripts/UI/SplashScreen.cs` | Complete rewrite — dramatic ASCII art title with fire effects, block letters, animated reveal, 80x25 BBS compatible |
| `Scripts/UI/ColorTheme.cs` | **NEW** — `ColorThemeType` enum (Default, ClassicDark, AmberRetro, GreenPhosphor, HighContrast), `ColorTheme` static class with `Resolve()` color remapping, 4 theme dictionaries, `[ThreadStatic]` for MUD session safety, helper methods |
| `Scripts/UI/TerminalEmulator.cs` | `ColorTheme.Resolve()` wired into `GetAnsiColorCode()` and `ColorNameToConsole()`; idle/session timeout checks in `GetInput()`, 1-minute warning, `HandleIdleTimeout()`, `HandleSessionExpired()`, `AutoSaveAndExit()` |
| `Scripts/BBS/BBSTerminalAdapter.cs` | `ColorTheme.Resolve()` wired into `GetAnsiColorCode()` |
| `Scripts/Core/Character.cs` | Added `ColorTheme` property (persisted preference) |
| `Scripts/Systems/SaveDataStructures.cs` | Added `ColorTheme` to `PlayerData` |
| `Scripts/Systems/SaveSystem.cs` | Save player's `ColorTheme` preference |
| `Scripts/Core/GameEngine.cs` | Restore player's `ColorTheme` on load, apply `ColorTheme.Current`; show splash screen once per session in BBS/online mode; apply SysOp default theme to new characters |
| `Scripts/BBS/DoorMode.cs` | `LastInputTime`, `SessionStartTime`, `IdleTimeoutMinutes` properties; `IsIdleTimedOut`, `IsSessionExpired` computed properties; `--idle-timeout` CLI flag |
| `Scripts/Systems/SysOpConfigSystem.cs` | `IdleTimeoutMinutes` and `DefaultColorTheme` in `SysOpConfig` class, applied on config load, synced on save |
| `Scripts/Systems/SysOpConsoleManager.cs` | `[I] Idle Timeout` and `[T] Color Theme` menu options in both local and online SysOp Console menus; `SetIdleTimeout()` and `SetDefaultColorTheme()` methods |
