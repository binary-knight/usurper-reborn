#!/bin/bash
# Usurper Reborn Linux Launcher
# Tries common terminal emulators in order of preference

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GAME_EXE="$SCRIPT_DIR/UsurperRemake"

# Make sure the game is executable
chmod +x "$GAME_EXE" 2>/dev/null

# Function to run the game in a terminal
run_in_terminal() {
    # Try terminals in order of preference
    if command -v gnome-terminal &> /dev/null; then
        gnome-terminal --title="Usurper Reborn" -- "$GAME_EXE"
    elif command -v konsole &> /dev/null; then
        konsole --title "Usurper Reborn" -e "$GAME_EXE"
    elif command -v xfce4-terminal &> /dev/null; then
        xfce4-terminal --title="Usurper Reborn" -e "$GAME_EXE"
    elif command -v mate-terminal &> /dev/null; then
        mate-terminal --title="Usurper Reborn" -e "$GAME_EXE"
    elif command -v terminator &> /dev/null; then
        terminator --title="Usurper Reborn" -e "$GAME_EXE"
    elif command -v xterm &> /dev/null; then
        # -bg/-fg: xterm defaults to black-on-white, which washes out the game's palette (v0.65.5)
        xterm -title "Usurper Reborn" -bg black -fg white -fa "Monospace" -fs 12 -e "$GAME_EXE"
    elif command -v kitty &> /dev/null; then
        kitty --title "Usurper Reborn" "$GAME_EXE"
    elif command -v alacritty &> /dev/null; then
        alacritty --title "Usurper Reborn" -e "$GAME_EXE"
    else
        # Fallback: try to run directly (might work in Steam's terminal)
        echo "No terminal emulator found. Running directly..."
        "$GAME_EXE"
        read -p "Press Enter to exit..."
    fi
}

run_in_terminal
