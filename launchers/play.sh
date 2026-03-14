#!/usr/bin/env bash
# Usurper Reborn — Game Launcher (Linux/macOS)
# Tries WezTerm first, falls back to system terminal, then direct execution.

cd "$(dirname "$0")"
chmod +x UsurperReborn 2>/dev/null

# Try bundled WezTerm first (AppImage requires FUSE — may fail on some distros)
if [ -x "./wezterm/wezterm-gui" ]; then
    export WEZTERM_CONFIG_FILE="$(pwd)/wezterm.lua"
    # Try extracting the AppImage if FUSE is unavailable
    if ./wezterm/wezterm-gui --help >/dev/null 2>&1; then
        exec ./wezterm/wezterm-gui
    elif APPIMAGE_EXTRACT_AND_RUN=1 ./wezterm/wezterm-gui --help >/dev/null 2>&1; then
        export APPIMAGE_EXTRACT_AND_RUN=1
        exec ./wezterm/wezterm-gui
    fi
fi

# Fall back to common Linux terminal emulators
for term_cmd in xterm gnome-terminal konsole xfce4-terminal mate-terminal lxterminal alacritty kitty; do
    if command -v "$term_cmd" >/dev/null 2>&1; then
        case "$term_cmd" in
            gnome-terminal) exec gnome-terminal -- ./UsurperReborn --local ;;
            konsole)        exec konsole -e ./UsurperReborn --local ;;
            xfce4-terminal) exec xfce4-terminal -e "./UsurperReborn --local" ;;
            mate-terminal)  exec mate-terminal -e "./UsurperReborn --local" ;;
            lxterminal)     exec lxterminal -e "./UsurperReborn --local" ;;
            alacritty)      exec alacritty -e ./UsurperReborn --local ;;
            kitty)          exec kitty ./UsurperReborn --local ;;
            xterm)          exec xterm -e ./UsurperReborn --local ;;
        esac
    fi
done

# Last resort: run directly (works if Steam launches in a terminal)
exec ./UsurperReborn --local
