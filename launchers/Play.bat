@echo off
:: Usurper Reborn — WezTerm Game Launcher (Windows)
:: Launches the game inside a themed WezTerm terminal window.

cd /d "%~dp0"
set WEZTERM_CONFIG_FILE=%~dp0wezterm.lua
start "" "wezterm\wezterm-gui.exe"
