#!/bin/bash
# Usurper Reborn - Server Update Script
# Usage: sudo ./update-server.sh <path-to-new-files>
# Example: sudo ./update-server.sh /tmp/usurper-update/

set -e

NEW_FILES="${1:-.}"

if [ "$EUID" -ne 0 ]; then
    echo "ERROR: Please run as root (sudo ./update-server.sh <path>)"
    exit 1
fi

if [ ! -f "$NEW_FILES/UsurperReborn" ]; then
    echo "ERROR: UsurperReborn binary not found in $NEW_FILES"
    echo "Usage: sudo ./update-server.sh /path/to/new/files/"
    exit 1
fi

echo "╔══════════════════════════════════════════════════════════╗"
echo "║          Usurper Reborn - Server Update                  ║"
echo "╚══════════════════════════════════════════════════════════╝"
echo ""

# 1. Backup current version
echo "[1/4] Backing up current version..."
BACKUP_DIR="/opt/usurper.bak.$(date +%Y%m%d_%H%M%S)"
cp -r /opt/usurper "$BACKUP_DIR"
echo "  Backed up to $BACKUP_DIR"

# 2. Backup database
echo "[2/4] Backing up database..."
if [ -f /var/usurper/usurper_online.db ]; then
    /opt/usurper/backup.sh 2>/dev/null || true
    echo "  Database backed up."
else
    echo "  No database to back up."
fi

# 3. Copy new files
echo "[3/4] Deploying new binary..."
# Keep server scripts, only replace game files
cp "$NEW_FILES/UsurperReborn" /opt/usurper/
cp "$NEW_FILES"/*.dll /opt/usurper/ 2>/dev/null || true
cp "$NEW_FILES"/*.so /opt/usurper/ 2>/dev/null || true
cp "$NEW_FILES"/*.json /opt/usurper/ 2>/dev/null || true
chmod +x /opt/usurper/UsurperReborn
chown -R usurper:usurper /opt/usurper
echo "  New binary deployed."

# 4. Restart world simulator if running
echo "[4/4] Restarting services..."
if systemctl is-active --quiet usurper-world 2>/dev/null; then
    systemctl restart usurper-world
    echo "  World simulator restarted."
else
    echo "  World simulator not running (skipped)."
fi

echo ""
echo "Update complete! New player connections will use the updated binary."
echo "Existing connections continue with the old version until they disconnect."
echo ""
echo "Rollback if needed:"
echo "  sudo cp -r $BACKUP_DIR/* /opt/usurper/"
echo ""
