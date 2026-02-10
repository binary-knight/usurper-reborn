#!/bin/bash
# Usurper Reborn - Daily Backup Script
# Installed by setup-server.sh to /opt/usurper/backup.sh
# Runs via cron: 0 4 * * * usurper /opt/usurper/backup.sh

BACKUP_DIR="/var/usurper/backups"
DB_PATH="/var/usurper/usurper_online.db"
DATE=$(date +%Y%m%d_%H%M%S)

if [ ! -f "$DB_PATH" ]; then
    echo "$(date): No database found at $DB_PATH - nothing to back up"
    exit 0
fi

# SQLite online backup (safe even while database is in use)
sqlite3 "$DB_PATH" ".backup '$BACKUP_DIR/usurper_online_$DATE.db'"

# Keep only last 14 days of backups
find "$BACKUP_DIR" -name "*.db" -mtime +14 -delete

echo "$(date): Backup completed: usurper_online_$DATE.db ($(du -h "$BACKUP_DIR/usurper_online_$DATE.db" | cut -f1))"

# Optional: Copy to S3 (uncomment and configure if desired)
# aws s3 cp "$BACKUP_DIR/usurper_online_$DATE.db" s3://your-bucket/usurper-backups/
