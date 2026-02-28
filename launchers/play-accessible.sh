#!/usr/bin/env bash
# Usurper Reborn — Accessible Launcher (Linux/macOS)
# Runs the game directly in the current terminal for screen reader compatibility.

cd "$(dirname "$0")"
chmod +x UsurperReborn 2>/dev/null
./UsurperReborn --local
