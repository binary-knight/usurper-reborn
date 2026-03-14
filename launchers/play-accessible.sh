#!/usr/bin/env bash
# Usurper Reborn — Accessible Launcher (Linux/macOS)
# Runs the game in a terminal with screen reader mode for NVDA/JAWS/Orca compatibility.
# Does NOT use WezTerm — runs in system terminal for best screen reader support.

cd "$(dirname "$0")"
chmod +x UsurperReborn 2>/dev/null

# Fall back to common Linux terminal emulators
for term_cmd in xterm gnome-terminal konsole xfce4-terminal mate-terminal lxterminal alacritty kitty; do
    if command -v "$term_cmd" >/dev/null 2>&1; then
        case "$term_cmd" in
            gnome-terminal) exec gnome-terminal -- ./UsurperReborn --local --screen-reader ;;
            konsole)        exec konsole -e ./UsurperReborn --local --screen-reader ;;
            xfce4-terminal) exec xfce4-terminal -e "./UsurperReborn --local --screen-reader" ;;
            mate-terminal)  exec mate-terminal -e "./UsurperReborn --local --screen-reader" ;;
            lxterminal)     exec lxterminal -e "./UsurperReborn --local --screen-reader" ;;
            alacritty)      exec alacritty -e ./UsurperReborn --local --screen-reader ;;
            kitty)          exec kitty ./UsurperReborn --local --screen-reader ;;
            xterm)          exec xterm -e ./UsurperReborn --local --screen-reader ;;
        esac
    fi
done

# Last resort: run directly (works if Steam launches in a terminal)
exec ./UsurperReborn --local --screen-reader
