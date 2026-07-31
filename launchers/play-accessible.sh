#!/usr/bin/env bash
# Usurper Reborn — Accessible Launcher (Linux/macOS)
# Runs the game in a terminal with screen reader mode for NVDA/JAWS/Orca compatibility.
# Does NOT use WezTerm — runs in system terminal for best screen reader support.

cd "$(dirname "$0")"
chmod +x UsurperReborn 2>/dev/null

# v0.65.9: sweep leftover game processes from a previous session (see play.sh)
# so Steam's "already running" state self-heals on the next launch.
if command -v pgrep >/dev/null 2>&1; then
    install_dir="$(pwd)"
    stale=$(pgrep -f "$install_dir/UsurperReborn" 2>/dev/null)
    if [ -n "$stale" ]; then
        echo "[usurper] Cleaning up leftover game process(es): $stale" >&2
        kill $stale 2>/dev/null
        sleep 1
        kill -9 $stale 2>/dev/null
    fi
fi

# Fall back to common Linux terminal emulators
for term_cmd in gnome-terminal konsole xfce4-terminal mate-terminal lxterminal alacritty kitty xterm; do
    if command -v "$term_cmd" >/dev/null 2>&1; then
        case "$term_cmd" in
            gnome-terminal) exec gnome-terminal -- ./UsurperReborn --local --screen-reader ;;
            konsole)        exec konsole -e ./UsurperReborn --local --screen-reader ;;
            xfce4-terminal) exec xfce4-terminal -e "./UsurperReborn --local --screen-reader" ;;
            mate-terminal)  exec mate-terminal -e "./UsurperReborn --local --screen-reader" ;;
            lxterminal)     exec lxterminal -e "./UsurperReborn --local --screen-reader" ;;
            alacritty)      exec alacritty -e ./UsurperReborn --local --screen-reader ;;
            kitty)          exec kitty ./UsurperReborn --local --screen-reader ;;
            xterm)          exec xterm -bg black -fg white -e ./UsurperReborn --local --screen-reader ;;
        esac
    fi
done

# Last resort: run directly (works if Steam launches in a terminal)
exec ./UsurperReborn --local --screen-reader
