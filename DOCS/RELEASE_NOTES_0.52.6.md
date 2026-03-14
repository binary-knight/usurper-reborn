# Usurper Reborn v0.52.6 Release Notes

**Release Date:** March 14, 2026
**Version Name:** The Hook

## Linux Steam Launch Fix

The Linux `play.sh` launcher has been rewritten with a robust fallback chain. Previously, the script relied entirely on the bundled WezTerm AppImage, which requires FUSE to run. Many modern Linux distros (Ubuntu 22.04+, Fedora, Arch) don't ship with FUSE by default, causing the AppImage to silently fail and the game to never launch.

The new launcher:
1. Tries bundled WezTerm normally (FUSE available)
2. Tries WezTerm with `APPIMAGE_EXTRACT_AND_RUN=1` (no FUSE needed)
3. Falls back to system terminal emulators (gnome-terminal, konsole, xfce4-terminal, alacritty, kitty, xterm, and others)
4. Last resort: runs the game binary directly

The accessible launcher (`play-accessible.sh`) has the same terminal fallback chain with `--screen-reader` mode. It intentionally skips WezTerm to use the system terminal for best screen reader (Orca) compatibility.

## NPC Teammate Equipment Persistence Fix (Online Mode)

Fixed a race condition where NPC teammate equipment could revert to an older state after entering the dungeon in online multiplayer mode.

**Root cause:** When a player gave an NPC teammate new gear (at Team Corner or Home), the equipment was updated in memory but not immediately written to the shared `world_state` database. If the world-sim's periodic save cycle triggered between the equipment change and the next world-state write, it would reload all NPCs from the database — preserving only pregnancies during the merge, discarding equipment changes.

**Fix (defense-in-depth):**
- **Immediate persistence:** After equipping an NPC teammate, a fire-and-forget `SaveAllSharedState()` call writes the current NPC data to the database immediately, closing the race window.
- **Equipment merge on reload:** The world-sim's NPC reload logic now captures and restores equipment (in addition to pregnancies) before/after reloading from the database. If the reloaded NPC has different equipment than what was in memory, the in-memory version is preserved since it's always more recent.

Single-player and BBS modes are unaffected (no world-sim).

## Potion Cap Display

The Magic Shop now shows the player's current and maximum potion capacity (e.g., "5/10") instead of just the current count. The potion cap uses the player's `MaxPotions` property instead of the global `GameConfig.MaxHealingPotions` constant. The merchant "Ravanella" is now named in the purchase confirmation message.

---

## Files Changed

- `Scripts/Core/GameConfig.cs` -- Version 0.52.6
- `Scripts/Locations/TeamCornerLocation.cs` -- Fire-and-forget `SaveAllSharedState()` after NPC equipment changes in online mode
- `Scripts/Locations/HomeLocation.cs` -- Fire-and-forget `SaveAllSharedState()` after NPC equipment changes in online mode
- `Scripts/Systems/WorldSimService.cs` -- Capture and restore NPC equipment (in addition to pregnancies) during world-sim reload cycles; debug logging for equipment restoration
- `Scripts/Locations/MagicShopLocation.cs` -- `MaxPotions` instead of `GameConfig.MaxHealingPotions`; potion display shows current/max; merchant name in deal message
- `Localization/en.json` -- Updated `magic_shop.potion_current` and `magic_shop.potion_deal` format args
- `Localization/es.json` -- Updated `magic_shop.potion_current` and `magic_shop.potion_deal` format args
- `Localization/hu.json` -- Updated `magic_shop.potion_current` and `magic_shop.potion_deal` format args
- `Localization/it.json` -- Updated `magic_shop.potion_current` and `magic_shop.potion_deal` format args
- `launchers/play.sh` -- Rewritten with WezTerm FUSE fallback, system terminal chain, and direct execution fallback
- `launchers/play-accessible.sh` -- Rewritten with system terminal chain and `--screen-reader` flag
