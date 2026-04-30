#!/bin/bash
#
# nuke-server.sh — beta-launch wipe of the online game state.
#
# Returns the AWS game server to a "fresh launch" state. Wipes all gameplay
# tables (players, NPCs, news, mail, guilds, etc.) but preserves admin
# infrastructure (admin password hash, admin user accounts, active dashboard
# sessions) so the operator stays logged in after the nuke.
#
# Invoked as root via NOPASSWD sudo from the usurper-web Node service when
# the admin clicks the "Wipe Server" button on /admin.html.
#
# Manual invocation:
#   sudo /opt/usurper/scripts/nuke-server.sh
#
# Output goes to stdout AND /var/log/usurper-nuke/nuke-{timestamp}.log so the
# audit trail survives the wipe (the in-DB admin_commands table gets cleared).
#
# The pre-wipe DB backup is preserved at /var/usurper/nuke-backups/ — kept
# indefinitely so a botched nuke can be reverted by stopping services and
# copying the backup back.

set -euo pipefail

TIMESTAMP=$(date +%Y%m%d-%H%M%S)
DB=/var/usurper/usurper_online.db
BACKUP_DIR=/var/usurper/nuke-backups
LOG_DIR=/var/log/usurper-nuke

mkdir -p "$BACKUP_DIR"
mkdir -p "$LOG_DIR"

LOG="$LOG_DIR/nuke-$TIMESTAMP.log"
exec > >(tee -a "$LOG") 2>&1

echo "============================================================"
echo "[$(date -Iseconds)] === USURPER NUKE INITIATED ==="
echo "PID:    $$"
echo "User:   $(whoami)"
echo "Host:   $(hostname)"
echo "DB:     $DB"
echo "Backup: $BACKUP_DIR/pre-nuke-$TIMESTAMP.db"
echo "Log:    $LOG"
echo "============================================================"

if [ ! -f "$DB" ]; then
  echo "ERROR: database file not found at $DB"
  exit 2
fi

# 1. Backup the DB before doing anything destructive. Kept forever (small
#    file, kilobytes-to-megabytes range — 30+ years of nukes wouldn't fill
#    the disk). Uses .backup not cp to avoid a torn copy if the DB is in
#    the middle of a write at this exact moment.
BACKUP="$BACKUP_DIR/pre-nuke-$TIMESTAMP.db"
echo ""
echo "[$(date -Iseconds)] Step 1/5: Backing up DB..."
sqlite3 "$DB" ".backup '$BACKUP'"
echo "  Backup size: $(ls -lh "$BACKUP" | awk '{print $5}')"

# 2. Stop services so no active session is mutating the DB during wipe.
#    Tolerate already-stopped state (||true).
echo ""
echo "[$(date -Iseconds)] Step 2/5: Stopping game services..."
systemctl stop sshd-usurper || echo "  sshd-usurper already stopped"
systemctl stop usurper-mud  || echo "  usurper-mud already stopped"
sleep 2
echo "  Services stopped:"
systemctl is-active sshd-usurper usurper-mud || true

# 3. Wipe all gameplay data inside a single transaction. The schema (CREATE
#    TABLE definitions) stays intact — services will see empty tables on
#    restart and proceed as if launching for the first time.
#
#    Preserved tables (admin infrastructure):
#      - balance_config       (admin password hash)
#      - dashboard_users      (admin login records)
#      - dashboard_sessions   (active admin tokens — keeps the operator
#                              logged in after the nuke)
#
#    Reset rows in admin_config (peak online stats etc.) without dropping
#    the table since that table is keyed by 'key' and other rows might
#    accumulate over time.
echo ""
echo "[$(date -Iseconds)] Step 3/5: Wiping gameplay tables..."

sqlite3 "$DB" <<'SQL'
PRAGMA foreign_keys = OFF;
BEGIN TRANSACTION;

-- Player + character data
DELETE FROM players;
DELETE FROM deleted_characters;
DELETE FROM sleeping_players;
DELETE FROM online_players;

-- World simulation state
DELETE FROM world_state;
DELETE FROM news;

-- Combat / PvP / events
DELETE FROM combat_events;
DELETE FROM pvp_log;
DELETE FROM bounties;
DELETE FROM world_bosses;
DELETE FROM world_boss_damage;
DELETE FROM castle_sieges;

-- Communication
DELETE FROM messages;
DELETE FROM discord_gossip;
DELETE FROM snoop_buffer;

-- Economy / market
DELETE FROM trade_offers;
DELETE FROM auction_listings;

-- Teams + guilds
DELETE FROM player_teams;
DELETE FROM team_upgrades;
DELETE FROM team_vault;
DELETE FROM team_wars;
DELETE FROM guilds;
DELETE FROM guild_members;
DELETE FROM guild_bank_items;

-- Wizard / sysop logs (admin actions, not admin auth)
DELETE FROM wizard_log;
DELETE FROM wizard_flags;
DELETE FROM admin_commands;

-- Reset cumulative stats in admin_config (peak online etc.) without
-- dropping the row so the schema-style key/value layout survives.
UPDATE admin_config SET value = '{"count": 0, "time": null}'
 WHERE key = 'peak_online';

COMMIT;
PRAGMA foreign_keys = ON;
SQL

echo "  SQL transaction committed."

# 4. VACUUM outside the transaction to reclaim the now-empty space. SQLite
#    does NOT shrink the file when rows are deleted unless VACUUM is run.
#    Without this the DB file stays large until the first INSERT churn
#    eventually overwrites the freed pages.
echo ""
echo "[$(date -Iseconds)] Step 4/5: Vacuuming DB..."
sqlite3 "$DB" "VACUUM;"
echo "  DB size after vacuum: $(ls -lh "$DB" | awk '{print $5}')"

# 5. Restart services. usurper-mud's startup creates the schema if missing
#    (CREATE TABLE IF NOT EXISTS pattern) so the now-empty tables are
#    safe; existing schema is unchanged.
echo ""
echo "[$(date -Iseconds)] Step 5/5: Restarting services..."
systemctl start usurper-mud
sleep 3
systemctl start sshd-usurper

# Brief verification that services came up cleanly
sleep 2
echo "  Final status:"
for svc in usurper-mud sshd-usurper; do
  state=$(systemctl is-active "$svc" || true)
  echo "    $svc: $state"
done

echo ""
echo "============================================================"
echo "[$(date -Iseconds)] === NUKE COMPLETE ==="
echo "Backup retained at:  $BACKUP"
echo "Log retained at:     $LOG"
echo "============================================================"
