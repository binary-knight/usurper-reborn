#!/usr/bin/env bash
# Usurper Reborn — Game Launcher (Linux/macOS)
# Tries the bundled WezTerm first, falls back to a system terminal, then to
# direct execution in the current terminal.

cd "$(dirname "$0")"
chmod +x UsurperReborn 2>/dev/null

log() { echo "[usurper] $*" >&2; }

# --- Stale-process sweep ------------------------------------------------------
# v0.65.9 (player report): a leftover UsurperReborn/wezterm process from a
# previous session kept Steam saying "already running" until a reboot. Sweep
# any leftovers launched from THIS install directory before starting. TERM
# first, then KILL for anything that ignores it. (A process stuck in
# uninterruptible D-state — the FUSE-mount hang addressed below — survives
# even SIGKILL; the extract-and-run change removes that failure class.)
if command -v pgrep >/dev/null 2>&1; then
    install_dir="$(pwd)"
    stale=$(pgrep -f "$install_dir/UsurperReborn" 2>/dev/null)
    if [ -n "$stale" ]; then
        log "Cleaning up leftover game process(es) from a previous session: $stale"
        kill $stale 2>/dev/null
        sleep 1
        kill -9 $stale 2>/dev/null
    fi
fi

# Without a graphical session neither WezTerm nor any other GUI terminal can
# start — run the game directly in whatever terminal we're already in.
# (Linux only: macOS WezTerm is native Cocoa and does not use DISPLAY.)
if [ "$(uname)" = "Linux" ] && [ -z "$DISPLAY" ] && [ -z "$WAYLAND_DISPLAY" ]; then
    log "No graphical display detected — running in the current terminal."
    exec ./UsurperReborn --local
fi

# --- Bundled WezTerm (preferred: ships its own dark theme and fonts) ---------
# v0.65.9: on Linux the AppImage now ALWAYS self-extracts instead of
# FUSE-mounting (APPIMAGE_EXTRACT_AND_RUN=1). A FUSE mount that wedges — a
# known AppImage failure mode after suspend/resume or a fusermount crash —
# leaves wezterm in uninterruptible D-state: unkillable, Steam reports the
# game as "already running", and only a reboot clears it (player-reported).
# Self-extraction costs a second at startup and removes the entire failure
# class. The env var is ignored by non-AppImage builds (macOS), so exporting
# it unconditionally is safe.
if [ -x "./wezterm/wezterm-gui" ]; then
    export WEZTERM_CONFIG_FILE="$(pwd)/wezterm.lua"
    export APPIMAGE_EXTRACT_AND_RUN=1

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
    else
        log "Bundled WezTerm could not start on this system."
        log "Common causes: missing GUI libraries (X11/Wayland client libs)."
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
