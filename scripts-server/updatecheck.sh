#!/bin/bash
# Usurper Reborn — Auto-Update Check Script (Linux)
# Checks GitHub for new releases, backs up current install, and updates.
#
# Usage:
#   ./updatecheck.sh                    # Check and update if available
#   ./updatecheck.sh --check-only       # Just check, don't update
#   ./updatecheck.sh --force            # Force re-download even if current
#
# Cron example (check at 3am daily):
#   0 3 * * * /opt/usurper/updatecheck.sh >> /var/log/usurper-update.log 2>&1
#
# Exit codes:
#   0 = Updated successfully
#   1 = Already up to date
#   2 = Error

set -euo pipefail

# ─── Configuration ───────────────────────────
INSTALL_DIR="${USURPER_DIR:-/opt/usurper}"
BACKUP_DIR="${USURPER_BACKUP_DIR:-/opt/usurper/backups}"
GAME_BINARY="$INSTALL_DIR/UsurperReborn"
GITHUB_REPO="binary-knight/usurper-reborn"
GITHUB_API="https://api.github.com/repos/$GITHUB_REPO/releases/latest"
PLATFORM="Linux-x64"
LOG_PREFIX="[updatecheck]"
SERVICE_NAME="${USURPER_SERVICE:-usurper-mud}"
SSH_SERVICE="${USURPER_SSH_SERVICE:-sshd-usurper}"
MAX_BACKUPS=5

# ─── Parse arguments ────────────────────────
CHECK_ONLY=false
FORCE=false
for arg in "$@"; do
    case "$arg" in
        --check-only) CHECK_ONLY=true ;;
        --force) FORCE=true ;;
        --help|-h)
            echo "Usurper Reborn Auto-Updater"
            echo ""
            echo "Usage: $0 [--check-only] [--force]"
            echo ""
            echo "Options:"
            echo "  --check-only   Check for updates without installing"
            echo "  --force        Force update even if version matches"
            echo ""
            echo "Environment variables:"
            echo "  USURPER_DIR          Install directory (default: /opt/usurper)"
            echo "  USURPER_BACKUP_DIR   Backup directory (default: /opt/usurper/backups)"
            echo "  USURPER_SERVICE      Systemd service name (default: usurper-mud)"
            echo "  USURPER_SSH_SERVICE  SSH service name (default: sshd-usurper)"
            exit 0
            ;;
    esac
done

# ─── Helper functions ────────────────────────
log() { echo "$(date '+%Y-%m-%d %H:%M:%S') $LOG_PREFIX $*"; }
die() { log "ERROR: $*"; exit 2; }

# ─── Get current version ─────────────────────
get_current_version() {
    if [ -f "$INSTALL_DIR/version.txt" ]; then
        head -1 "$INSTALL_DIR/version.txt" | tr -d '[:space:]'
    elif [ -x "$GAME_BINARY" ]; then
        # Try to extract from binary (run with --version if supported)
        echo "unknown"
    else
        echo "none"
    fi
}

# ─── Check GitHub for latest release ─────────
log "Checking for updates..."

CURRENT_VERSION=$(get_current_version)
log "Current version: $CURRENT_VERSION"

# Fetch latest release info from GitHub API
RELEASE_JSON=$(curl -sf -H "Accept: application/vnd.github.v3+json" "$GITHUB_API" 2>/dev/null) || {
    # Fallback to plain HTTP proxy
    log "GitHub API failed, trying fallback..."
    RELEASE_JSON=$(curl -sf "http://usurper-reborn.net/api/releases/latest" 2>/dev/null) || die "Cannot reach update server"
}

LATEST_VERSION=$(echo "$RELEASE_JSON" | grep -o '"tag_name"[[:space:]]*:[[:space:]]*"[^"]*"' | head -1 | cut -d'"' -f4 | sed 's/^v//')
RELEASE_NAME=$(echo "$RELEASE_JSON" | grep -o '"name"[[:space:]]*:[[:space:]]*"[^"]*"' | head -1 | cut -d'"' -f4)

if [ -z "$LATEST_VERSION" ]; then
    die "Could not parse latest version from GitHub"
fi

log "Latest version: $LATEST_VERSION ($RELEASE_NAME)"

# ─── Compare versions ────────────────────────
if [ "$CURRENT_VERSION" = "$LATEST_VERSION" ] && [ "$FORCE" = false ]; then
    log "Already up to date (v$CURRENT_VERSION)"
    exit 1
fi

if [ "$CHECK_ONLY" = true ]; then
    log "Update available: v$CURRENT_VERSION -> v$LATEST_VERSION"
    exit 0
fi

# ─── Find download URL ───────────────────────
DOWNLOAD_URL=$(echo "$RELEASE_JSON" | grep -o '"browser_download_url"[[:space:]]*:[[:space:]]*"[^"]*'"$PLATFORM"'[^"]*\.zip"' | head -1 | cut -d'"' -f4)

if [ -z "$DOWNLOAD_URL" ]; then
    die "No $PLATFORM asset found in release $LATEST_VERSION"
fi

log "Download URL: $DOWNLOAD_URL"

# ─── Create backup ────────────────────────────
log "Creating backup..."
mkdir -p "$BACKUP_DIR"
BACKUP_FILE="$BACKUP_DIR/usurper-v${CURRENT_VERSION}-$(date '+%Y%m%d_%H%M%S').tar.gz"

# Backup the key files (binary + DLL + configs, skip logs/saves)
tar -czf "$BACKUP_FILE" -C "$INSTALL_DIR" \
    --exclude='backups' \
    --exclude='logs' \
    --exclude='saves' \
    --exclude='*.db' \
    --exclude='*.db-journal' \
    --exclude='*.db-wal' \
    UsurperReborn UsurperReborn.dll 2>/dev/null || true

log "Backup saved: $BACKUP_FILE"

# Clean old backups (keep last N)
ls -t "$BACKUP_DIR"/usurper-v*.tar.gz 2>/dev/null | tail -n +$((MAX_BACKUPS + 1)) | xargs -r rm -f
log "Cleaned old backups (keeping last $MAX_BACKUPS)"

# ─── Download update ──────────────────────────
log "Downloading v$LATEST_VERSION..."
TEMP_DIR=$(mktemp -d)
TEMP_ZIP="$TEMP_DIR/update.zip"

curl -sL -o "$TEMP_ZIP" "$DOWNLOAD_URL" || die "Download failed"

ZIP_SIZE=$(stat -c%s "$TEMP_ZIP" 2>/dev/null || stat -f%z "$TEMP_ZIP" 2>/dev/null)
log "Downloaded: $(( ZIP_SIZE / 1024 / 1024 ))MB"

# ─── Stop services ────────────────────────────
log "Stopping services..."
sudo systemctl stop "$SSH_SERVICE" 2>/dev/null || true
sudo systemctl stop "$SERVICE_NAME" 2>/dev/null || true
sleep 2

# ─── Extract update ───────────────────────────
log "Extracting to $INSTALL_DIR..."
unzip -qo "$TEMP_ZIP" -d "$INSTALL_DIR" || die "Extraction failed"
chmod +x "$GAME_BINARY"
chown -R usurper:usurper "$INSTALL_DIR" 2>/dev/null || true

# Write version file
echo "$LATEST_VERSION" > "$INSTALL_DIR/version.txt"

# ─── Restart services ─────────────────────────
log "Starting services..."
sudo systemctl start "$SERVICE_NAME"
sudo systemctl start "$SSH_SERVICE"

# ─── Cleanup ──────────────────────────────────
rm -rf "$TEMP_DIR"

log "Update complete: v$CURRENT_VERSION -> v$LATEST_VERSION"
exit 0
