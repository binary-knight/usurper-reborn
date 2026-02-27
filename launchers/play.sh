#!/usr/bin/env bash
# Usurper Reborn — WezTerm Game Launcher (Linux/macOS)
# Launches the game inside a themed WezTerm terminal window.

cd "$(dirname "$0")"
export WEZTERM_CONFIG_FILE="$(pwd)/wezterm.lua"
chmod +x UsurperReborn wezterm/wezterm-gui 2>/dev/null
./wezterm/wezterm-gui &
