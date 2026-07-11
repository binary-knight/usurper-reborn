#!/usr/bin/env bash
# Usurper Reborn — Game Launcher (Linux/macOS)
# Tries the bundled WezTerm first, falls back to a system terminal, then to
# direct execution in the current terminal.

cd "$(dirname "$0")"
chmod +x UsurperReborn 2>/dev/null

log() { echo "[usurper] $*" >&2; }

# Without a graphical session neither WezTerm nor any other GUI terminal can
# start — run the game directly in whatever terminal we're already in.
# (Linux only: macOS WezTerm is native Cocoa and does not use DISPLAY.)
if [ "$(uname)" = "Linux" ] && [ -z "$DISPLAY" ] && [ -z "$WAYLAND_DISPLAY" ]; then
    log "No graphical display detected — running in the current terminal."
    exec ./UsurperReborn --local
fi

# --- Bundled WezTerm (preferred: ships its own dark theme and fonts) ---------
# The AppImage needs FUSE2 to mount itself; when FUSE is missing we retry with
# APPIMAGE_EXTRACT_AND_RUN=1, which self-extracts instead of mounting.
if [ -x "./wezterm/wezterm-gui" ]; then
    export WEZTERM_CONFIG_FILE="$(pwd)/wezterm.lua"

    run_wezterm() {
        # Run (not exec) so a crash at GUI startup falls through to the system
        # terminals below instead of leaving the player with nothing. A non-zero
        # exit within 10 seconds counts as a startup failure; anything longer
        # means the game actually ran and the player closed the window.
        local start rc
        start=$(date +%s)
        ./wezterm/wezterm-gui
        rc=$?
        if [ $rc -eq 0 ] || [ $(( $(date +%s) - start )) -ge 10 ]; then
            exit $rc
        fi
        log "WezTerm exited immediately (status $rc) — falling back to a system terminal."
        return 1
    }

    if ./wezterm/wezterm-gui --version >/dev/null 2>&1; then
        run_wezterm
    elif APPIMAGE_EXTRACT_AND_RUN=1 ./wezterm/wezterm-gui --version >/dev/null 2>&1; then
        export APPIMAGE_EXTRACT_AND_RUN=1
        run_wezterm
    else
        log "Bundled WezTerm could not start on this system."
        log "Common causes: missing libfuse2 (Debian/Ubuntu: sudo apt install libfuse2) or missing GUI libraries."
        log "Run ./wezterm/wezterm-gui directly to see the exact error."
        log "Falling back to a system terminal."
    fi
fi

# --- System terminal fallback -------------------------------------------------
# Desktop terminals first (they follow the user's theme); xterm last, forced to
# light-on-dark because its default is black-on-white, which makes the game's
# bright palette unreadable. The game also paints its own dark background on
# ANSI terminals as of v0.65.5, so the xterm flags are belt and suspenders.
for term_cmd in gnome-terminal konsole xfce4-terminal mate-terminal lxterminal alacritty kitty xterm; do
    if command -v "$term_cmd" >/dev/null 2>&1; then
        case "$term_cmd" in
            gnome-terminal) exec gnome-terminal -- ./UsurperReborn --local ;;
            konsole)        exec konsole -e ./UsurperReborn --local ;;
            xfce4-terminal) exec xfce4-terminal -e "./UsurperReborn --local" ;;
            mate-terminal)  exec mate-terminal -e "./UsurperReborn --local" ;;
            lxterminal)     exec lxterminal -e "./UsurperReborn --local" ;;
            alacritty)      exec alacritty -e ./UsurperReborn --local ;;
            kitty)          exec kitty ./UsurperReborn --local ;;
            xterm)          exec xterm -bg black -fg white -e ./UsurperReborn --local ;;
        esac
    fi
done

# Last resort: run directly (works if Steam launches in a terminal)
exec ./UsurperReborn --local
