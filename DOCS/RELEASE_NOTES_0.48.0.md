# Usurper Reborn v0.48.0 — WezTerm Desktop Terminal

## WezTerm Branded Terminal

The game now ships with an optional **WezTerm** terminal emulator, turning the game into a standalone desktop application with a branded window. Double-click `Play.bat` (Windows) or `play.sh` (Linux/macOS) and the game launches inside a custom-themed terminal — no command prompt, no configuration, no terminal emulator needed.

WezTerm is configured via `wezterm.lua` in the game directory:

- **Window title**: "Usurper Reborn"
- **Clean chrome**: No tab bar, no scroll bar, minimal decorations
- **80x30 layout**: Classic BBS-sized terminal window
- **Dark theme**: Black background with vivid ANSI colors for art fidelity
- **Custom icon**: Windows builds have the game icon patched onto the WezTerm executable
- **Auto-close**: Terminal window closes immediately when the game exits

WezTerm is bundled alongside the game — it does not replace the plain executable. BBS sysops, server operators, and users who prefer their own terminal can still run `UsurperReborn.exe` / `UsurperReborn` directly as before.

## Bundled Fonts & In-Game Font Selector

Five monospace fonts are bundled in the `fonts/` directory and loaded by WezTerm automatically — no system font installation required:

- **JetBrains Mono** (default) — clean, modern developer font
- **Cascadia Code** — Microsoft's terminal font with ligatures
- **Fira Code** — popular programming font with excellent readability
- **Iosevka** — narrow, space-efficient font ideal for dense text
- **Hack** — classic terminal font designed for source code

Players can switch fonts from the in-game **Preferences** menu (`[~]` → `[7] Terminal Font`). The font cycles through all five options. Changes take effect within 1-2 seconds — WezTerm watches `font-choice.txt` and auto-reloads.

Font selection is only available in standalone/local mode (not in BBS door mode or online multiplayer, where the terminal is controlled by the BBS or SSH client).

## Desktop Launchers

Two new launchers handle the WezTerm startup:

- **`Play.bat`** (Windows) — Sets the WezTerm config path and launches the branded terminal
- **`play.sh`** (Linux/macOS) — Same, with executable permissions set automatically

These launchers are the default entry point for Steam builds. Steam players always get the branded terminal experience.

## Hint Keybinding Corrections

Several onboarding hints referenced outdated menu keybindings from before the Main Street menu was reorganized:

- Level Master hint: `[M]` → `[V]`
- Magic Shop hint: `[G]` → `[M]`
- Quest Hall hint: `[Q]` → `[2]`
- Getting Started hint: Updated all references to match current keybindings

## Alpha Build Banner Fix

The alpha build warning banner on the main menu now renders as a proper enclosed box with `╔═╗║╚═╝` borders instead of open-ended lines. Right-side `║` characters align correctly at column 80.

## CI/CD: Desktop Build Pipeline

The GitHub Actions workflow now produces **desktop bundles** alongside the existing plain builds:

### New `build-desktop` Job
- Downloads WezTerm portables for Windows, Linux, and macOS
- Bundles game executable + WezTerm + `wezterm.lua` + fonts + launchers
- Patches the Windows WezTerm icon with the game icon (via wine + rcedit on Linux CI)
- Produces 3 new artifacts: `Desktop-Windows-x64.zip`, `Desktop-Linux-x64.tar.gz`, `Desktop-macOS-x64.zip`
- WezTerm downloads cached by version to avoid re-downloading on every build

### Release Assets
GitHub releases now include **9 artifacts** (up from 6):
- 6 plain builds (unchanged): Windows x64/x86, Linux x64/ARM64, macOS Intel/Apple Silicon
- 3 new desktop builds: Windows x64, Linux x64, macOS x64

### Steam Depot Updates
- Steam depots now bundle WezTerm in `wezterm/` with fonts and config
- Steam launch executables changed from `UsurperReborn.bat`/`usurper-reborn.sh` to `Play.bat`/`play.sh`
- Windows Steam depot gets icon-patched WezTerm executable

All existing plain builds remain **completely unchanged** for BBS sysops, server deployments, and users who prefer their own terminal.

## Files Changed

- `GameConfig.cs` — Version 0.48.0
- `Scripts/Core/GameEngine.cs` — Alpha build banner box-drawing fix (proper `╔═╗║╚═╝` borders)
- `Scripts/Locations/BaseLocation.cs` — `[7] Terminal Font` in preferences menu; `ReadCurrentFont()` and `WriteTerminalFont()` helpers; font cycling through 5 bundled fonts; gated on `!IsInDoorMode && !IsOnlineMode`
- `Scripts/Systems/HintSystem.cs` — Corrected keybinding references: Level Master `[M]`→`[V]`, Magic Shop `[G]`→`[M]`, Quest Hall `[Q]`→`[2]`
- `wezterm.lua` — **NEW** — WezTerm configuration: branded window, dark theme, 80x30 layout, font_dirs loading, font-choice.txt reading, auto-close on exit
- `launchers/Play.bat` — **NEW** — Windows WezTerm launcher
- `launchers/play.sh` — **NEW** — Linux/macOS WezTerm launcher
- `fonts/` — **NEW** — 7 font files: CascadiaCode.ttf, FiraCode-Regular.ttf, FiraCode-Bold.ttf, Hack-Regular.ttf, Hack-Bold.ttf, Iosevka-Regular.ttf, Iosevka-Bold.ttf
- `.github/workflows/ci-cd.yml` — New `build-desktop` job (WezTerm bundling, icon patching, 3 platform archives); `attach-to-release` updated with desktop artifacts; `steam-prep` updated with WezTerm bundling, icon patching, Play.bat/play.sh launchers, STEAM_LAUNCH_CONFIG.md; `report` job includes `build-desktop`
- `build/package-wezterm.sh` — Local WezTerm packaging script (fonts copy, icon patching for Windows)
