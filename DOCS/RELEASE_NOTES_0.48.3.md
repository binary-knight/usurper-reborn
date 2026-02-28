# Usurper Reborn v0.48.3 — Desktop Window Polish

## Game-Themed Custom Title Bar

The WezTerm game window now features a fully custom title bar replacing the standard OS chrome. Dark navy background with bold gold "Usurper Reborn" title text, gold window control buttons (minimize/maximize/close), and a thin gold border frame around the entire window. The title bar dims to dark gold when the window loses focus.

## Window Positioning

The game window now spawns centered horizontally and pinned near the top of the screen, keeping the full 80x50 terminal visible above the taskbar on all screen sizes.

## Hidden Terminal Chrome

The new tab button is hidden and the tab bar is styled to blend seamlessly with the title bar — the window looks like a standalone game application, not a terminal emulator.

---

## Files Changed

- `GameConfig.cs` — Version 0.48.3
- `wezterm.lua` — `INTEGRATED_BUTTONS | RESIZE` replaces OS title bar; `window_frame` with dark navy/gold theme; `tab_bar` colors styled to match; `format-tab-title` event for bold gold title text; `gui-startup` positions window centered horizontally, pinned near top of screen; new tab button hidden
