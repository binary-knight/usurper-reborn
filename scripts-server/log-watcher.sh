#!/bin/bash
# Usurper Reborn - Log Watcher Agent
# Watches debug.log for serious errors and sends Discord notifications.
# Deduplicates repeated errors (same message within cooldown window).
#
# Install: sudo cp log-watcher.sh /opt/usurper/log-watcher.sh && sudo chmod +x /opt/usurper/log-watcher.sh
# Service: sudo systemctl enable --now usurper-log-watcher

LOG_FILE="/opt/usurper/logs/debug.log"
# DISCORD_WEBHOOK_URL is read from the environment — systemd sources
# /etc/usurper/log-watcher.env via EnvironmentFile (see usurper-log-watcher.service).
# Never commit the real URL to this repo. Rotate the webhook if the URL leaks.
WEBHOOK_URL="${DISCORD_WEBHOOK_URL:-}"
STATE_DIR="/var/usurper/log-watcher"
COOLDOWN_SECONDS=300  # 5 min cooldown per unique error signature

if [ -z "$WEBHOOK_URL" ]; then
    echo "[$(date)] ERROR: DISCORD_WEBHOOK_URL is not set. Configure /etc/usurper/log-watcher.env." >&2
    exit 1
fi

mkdir -p "$STATE_DIR"

# Extract a dedup key from an error line — strips timestamps, IDs, player names, numbers
dedup_key() {
    echo "$1" \
        | sed -E 's/^\[[0-9:.]+\] //' \
        | sed -E "s/player=[^ ]*/player=X/g" \
        | sed -E "s/NPC '[^']*'/NPC 'X'/g" \
        | sed -E "s/'[^']*'/'X'/g" \
        | sed -E 's/[0-9]+/N/g' \
        | md5sum | cut -d' ' -f1
}

# Check if we sent this error recently
is_cooled_down() {
    local key="$1"
    local file="$STATE_DIR/$key"
    if [ ! -f "$file" ]; then
        return 0  # Never sent
    fi
    local last_sent=$(cat "$file")
    local now=$(date +%s)
    local diff=$((now - last_sent))
    if [ "$diff" -ge "$COOLDOWN_SECONDS" ]; then
        return 0  # Cooldown expired
    fi
    return 1  # Still in cooldown
}

mark_sent() {
    echo "$(date +%s)" > "$STATE_DIR/$1"
}

# Send a Discord webhook message
send_discord() {
    local title="$1"
    local body="$2"
    local color="$3"  # decimal color

    # Truncate body to 1800 chars (Discord embed limit is 2048)
    if [ ${#body} -gt 1800 ]; then
        body="${body:0:1800}..."
    fi

    # Escape for JSON
    local json_body
    json_body=$(printf '%s' "$body" | python3 -c 'import sys,json; print(json.dumps(sys.stdin.read()))')
    local json_title
    json_title=$(printf '%s' "$title" | python3 -c 'import sys,json; print(json.dumps(sys.stdin.read()))')

    curl -s -o /dev/null -w "%{http_code}" \
        -H "Content-Type: application/json" \
        -d "{\"embeds\":[{\"title\":${json_title},\"description\":${json_body},\"color\":${color},\"footer\":{\"text\":\"Usurper Log Watcher • $(hostname)\"}}]}" \
        "$WEBHOOK_URL"
}

# Classify error severity and decide whether to notify
process_line() {
    local line="$1"

    # Only care about ERR level
    echo "$line" | grep -q '\[ERR\]' || return

    # Skip noisy/expected errors
    echo "$line" | grep -qE 'fail silently|best-effort|not available' && return

    # Extract category and message
    local category=$(echo "$line" | sed -E 's/.*\[ERR\] \[([A-Z_]+)\].*/\1/')
    local message=$(echo "$line" | sed -E 's/.*\[ERR\] //')

    # Classify severity
    local title=""
    local color=16711680  # red default

    case "$category" in
        WORLDSIM)
            title="World Sim Error"
            # NPC reset is critical
            echo "$line" | grep -q "Initializing fresh" && title="CRITICAL: NPC Reset Triggered"
            ;;
        COMBAT|COMBAT_*)
            title="Combat Error"
            ;;
        SAVE|LOAD)
            title="Save/Load Error"
            color=16744448  # orange
            ;;
        ONLINE|MUD)
            title="Online System Error"
            ;;
        EQUIP)
            title="Equipment Error"
            color=16744448  # orange
            ;;
        GOLD|GOLD_AUDIT)
            title="Gold Audit Alert"
            color=16776960  # yellow
            ;;
        *)
            title="Server Error [$category]"
            ;;
    esac

    # Check stack traces — grab the next few lines if this looks like an exception
    local key
    key=$(dedup_key "$message")

    if is_cooled_down "$key"; then
        local timestamp=$(echo "$line" | grep -oE '^\[[0-9:.]+\]' | tr -d '[]')
        local body="**Time:** ${timestamp:-unknown} UTC"
        body="$body\n\`\`\`\n${message}\n\`\`\`"

        local http_code
        http_code=$(send_discord "$title" "$body" "$color")

        if [ "$http_code" = "204" ] || [ "$http_code" = "200" ]; then
            mark_sent "$key"
            echo "[$(date +%H:%M:%S)] Sent: $title"
        else
            echo "[$(date +%H:%M:%S)] Discord webhook failed (HTTP $http_code)"
        fi
    fi
}

# Also watch for crash indicators in journalctl
check_service_crash() {
    local status
    status=$(systemctl is-active usurper-mud 2>/dev/null)
    if [ "$status" != "active" ]; then
        local key="service_crash"
        if is_cooled_down "$key"; then
            local body="The \`usurper-mud\` service is **not running** (status: $status).\nCheck: \`sudo journalctl -u usurper-mud -n 20\`"
            send_discord "CRITICAL: Game Server Down" "$body" 16711680
            mark_sent "$key"
            echo "[$(date +%H:%M:%S)] Sent: Service crash alert"
        fi
    fi
}

# Clean up old cooldown files (older than 1 hour)
cleanup_state() {
    find "$STATE_DIR" -type f -mmin +60 -delete 2>/dev/null
}

echo "[$(date)] Usurper Log Watcher started"
echo "[$(date)] Watching: $LOG_FILE"
echo "[$(date)] Cooldown: ${COOLDOWN_SECONDS}s per unique error"

# Send startup notification
send_discord "Log Watcher Started" "Monitoring \`$LOG_FILE\` for errors.\nCooldown: ${COOLDOWN_SECONDS}s per unique error." 3066993

# Main loop: tail the log file and process new lines
# Use --follow=name to handle log rotation (debug.log -> debug.1.log)
CLEANUP_COUNTER=0
tail -n 0 --follow=name --retry "$LOG_FILE" 2>/dev/null | while IFS= read -r line; do
    process_line "$line"

    # Periodic tasks every ~100 lines
    CLEANUP_COUNTER=$((CLEANUP_COUNTER + 1))
    if [ $((CLEANUP_COUNTER % 100)) -eq 0 ]; then
        check_service_crash
        cleanup_state
    fi
done
