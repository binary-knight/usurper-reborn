#!/bin/bash
# Usurper Reborn - Health Check Script
# Usage: /opt/usurper/healthcheck.sh

DB_PATH="/var/usurper/usurper_online.db"

# Check if database exists yet
if [ ! -f "$DB_PATH" ]; then
    echo "OK - No database yet (no players have connected)"
    exit 0
fi

# Check database is accessible
if ! sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM online_players;" > /dev/null 2>&1; then
    echo "FAIL - Database not accessible"
    exit 1
fi

# Get stats
ONLINE=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM online_players WHERE last_heartbeat > datetime('now', '-120 seconds');")
TOTAL=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM players;")
NEWS=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM news;")
DB_SIZE=$(du -h "$DB_PATH" | cut -f1)

echo "OK - $ONLINE players online, $TOTAL total accounts, $NEWS news entries, DB: $DB_SIZE"

# Check disk space
DISK_PCT=$(df /var/usurper | tail -1 | awk '{print $5}' | tr -d '%')
if [ "$DISK_PCT" -gt 90 ]; then
    echo "WARNING - Disk usage at ${DISK_PCT}%"
    exit 1
fi

# Check for stuck game processes (running > 24 hours)
STUCK=$(ps -eo pid,etimes,comm | grep UsurperReborn | awk '$2 > 86400 {print $1}' | wc -l)
if [ "$STUCK" -gt 0 ]; then
    echo "WARNING - $STUCK game process(es) running > 24 hours"
fi
