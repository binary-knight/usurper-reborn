#!/bin/bash
# Usurper Reborn - Server Setup Script
# Run as root: sudo ./setup-server.sh [port]
# Default game port: 4000 (admin SSH stays on 22)

set -e

GAME_PORT="${1:-4000}"

echo "╔══════════════════════════════════════════════════════════╗"
echo "║          Usurper Reborn - Server Setup                   ║"
echo "╚══════════════════════════════════════════════════════════╝"
echo ""
echo "  Game SSH port: $GAME_PORT"
echo "  Admin SSH port: 22 (unchanged)"
echo ""

# Check if running as root
if [ "$EUID" -ne 0 ]; then
    echo "ERROR: Please run as root (sudo ./setup-server.sh)"
    exit 1
fi

# 0. Install dependencies
echo "[0/9] Installing dependencies..."
apt-get update -qq
apt-get install -y -qq ufw sqlite3 fail2ban > /dev/null 2>&1
echo "  ufw, sqlite3, fail2ban installed."

# 1. Create game user
echo "[1/9] Creating usurper user..."
if id "usurper" &>/dev/null; then
    echo "  User 'usurper' already exists, skipping."
else
    useradd -m -d /home/usurper -s /bin/bash usurper
    echo "  User 'usurper' created."
fi

# 2. Create directories
echo "[2/9] Creating directories..."
mkdir -p /opt/usurper
mkdir -p /var/usurper
mkdir -p /var/usurper/logs
mkdir -p /var/usurper/backups
chown -R usurper:usurper /opt/usurper
chown -R usurper:usurper /var/usurper
echo "  /opt/usurper (binary), /var/usurper (data) created."

# 3. Set binary permissions
echo "[3/9] Setting binary permissions..."
if [ -f /opt/usurper/UsurperReborn ]; then
    chmod +x /opt/usurper/UsurperReborn
    chown -R usurper:usurper /opt/usurper
    echo "  UsurperReborn binary is executable."
else
    echo "  WARNING: UsurperReborn binary not found in /opt/usurper/"
    echo "  Upload it with: scp -r publish/linux-x64/* root@server:/opt/usurper/"
fi

# 4. Create dedicated sshd config for the game (separate from admin SSH on port 22)
echo "[4/9] Configuring game SSH on port $GAME_PORT..."
SSHD_GAME_CONF="/etc/ssh/sshd_config_usurper"
cat > "$SSHD_GAME_CONF" << SSHEOF
# Usurper Reborn - Dedicated SSH daemon on port $GAME_PORT
# This runs separately from the admin SSH on port 22

Port $GAME_PORT
AddressFamily any
ListenAddress 0.0.0.0

# Host keys (shared with main sshd)
HostKey /etc/ssh/ssh_host_rsa_key
HostKey /etc/ssh/ssh_host_ecdsa_key
HostKey /etc/ssh/ssh_host_ed25519_key

# Only allow the usurper user
AllowUsers usurper

# Force the game to launch on connect
ForceCommand /opt/usurper/UsurperReborn --online --stdio

# Security - no tunneling, no forwarding, no X11
AllowTcpForwarding no
X11Forwarding no
PermitTunnel no
AllowAgentForwarding no
PermitOpen none

# Authentication
PasswordAuthentication yes
PubkeyAuthentication yes
PermitRootLogin no
MaxAuthTries 3
LoginGraceTime 30

# Logging
SyslogFacility AUTH
LogLevel INFO

# Session
MaxSessions 50
ClientAliveInterval 120
ClientAliveCountMax 3

# PID file (separate from main sshd)
PidFile /run/sshd_usurper.pid
SSHEOF
echo "  Game SSH config written to $SSHD_GAME_CONF"

# 5. Create systemd service for the game sshd
echo "[5/9] Creating game SSH systemd service..."
cat > /etc/systemd/system/sshd-usurper.service << SERVICEEOF
[Unit]
Description=Usurper Reborn SSH Server (port $GAME_PORT)
After=network.target

[Service]
Type=notify
ExecStartPre=/usr/sbin/sshd -t -f /etc/ssh/sshd_config_usurper
ExecStart=/usr/sbin/sshd -D -f /etc/ssh/sshd_config_usurper
ExecReload=/bin/kill -HUP \$MAINPID
Restart=on-failure
RestartSec=5

[Install]
WantedBy=multi-user.target
SERVICEEOF

systemctl daemon-reload
systemctl enable sshd-usurper
echo "  Service sshd-usurper created and enabled."

# 6. Set shared gateway password
echo "[6/9] Setting shared gateway password..."
echo "usurper:play" | chpasswd
echo "  Shared account password set to 'play'"
echo "  Players SSH in with this password, then register/login in-game."
echo "  The game handles real authentication (per-player passwords in SQLite)."

# 7. Install backup cron
echo "[7/9] Setting up daily backups..."
BACKUP_SCRIPT="/opt/usurper/backup.sh"
cat > "$BACKUP_SCRIPT" << 'BACKUPEOF'
#!/bin/bash
BACKUP_DIR="/var/usurper/backups"
DB_PATH="/var/usurper/usurper_online.db"
DATE=$(date +%Y%m%d_%H%M%S)

if [ -f "$DB_PATH" ]; then
    sqlite3 "$DB_PATH" ".backup '$BACKUP_DIR/usurper_online_$DATE.db'"
    find "$BACKUP_DIR" -name "*.db" -mtime +14 -delete
    echo "$(date): Backup completed: usurper_online_$DATE.db"
else
    echo "$(date): No database found at $DB_PATH"
fi
BACKUPEOF
chmod +x "$BACKUP_SCRIPT"
chown usurper:usurper "$BACKUP_SCRIPT"

CRON_FILE="/etc/cron.d/usurper-backup"
echo "0 4 * * * usurper /opt/usurper/backup.sh >> /var/usurper/logs/backup.log 2>&1" > "$CRON_FILE"
echo "  Daily backup at 4 AM configured."

# 8. Install health check
echo "[8/9] Installing health check script..."
HEALTH_SCRIPT="/opt/usurper/healthcheck.sh"
cat > "$HEALTH_SCRIPT" << 'HEALTHEOF'
#!/bin/bash
DB_PATH="/var/usurper/usurper_online.db"

if [ ! -f "$DB_PATH" ]; then
    echo "OK - No database yet (no players have connected)"
    exit 0
fi

if sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM online_players;" > /dev/null 2>&1; then
    ONLINE=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM online_players WHERE last_heartbeat > datetime('now', '-120 seconds');")
    TOTAL=$(sqlite3 "$DB_PATH" "SELECT COUNT(*) FROM players;")
    DB_SIZE=$(du -h "$DB_PATH" | cut -f1)
    echo "OK - $ONLINE players online, $TOTAL total accounts, DB size: $DB_SIZE"
else
    echo "FAIL - Database not accessible"
    exit 1
fi
HEALTHEOF
chmod +x "$HEALTH_SCRIPT"
chown usurper:usurper "$HEALTH_SCRIPT"
echo "  Health check: /opt/usurper/healthcheck.sh"

# 9. Configure firewall & fail2ban
echo "[9/9] Configuring firewall & fail2ban..."
ufw allow 22/tcp > /dev/null 2>&1          # Admin SSH
ufw allow $GAME_PORT/tcp > /dev/null 2>&1  # Game SSH
ufw --force enable > /dev/null 2>&1
echo "  UFW enabled. Allowed: port 22 (admin), port $GAME_PORT (game)."

# Configure fail2ban for the game port
if [ -d /etc/fail2ban/jail.d ]; then
    cat > /etc/fail2ban/jail.d/usurper.conf << F2BEOF
[sshd-usurper]
enabled = true
port = $GAME_PORT
filter = sshd
logpath = /var/log/auth.log
maxretry = 5
bantime = 3600
findtime = 600
F2BEOF
    systemctl restart fail2ban 2>/dev/null || true
    echo "  fail2ban configured for port $GAME_PORT (5 attempts = 1 hour ban)."
fi

echo ""
echo "  NOTE: If using AWS, you must also open port $GAME_PORT in your"
echo "  EC2 Security Group / Lightsail Firewall via the AWS console."

# Start the game SSH service
echo ""
echo "Starting game SSH service..."
systemctl start sshd-usurper
echo "  sshd-usurper is running on port $GAME_PORT"

echo ""
echo "╔══════════════════════════════════════════════════════════╗"
echo "║                  Setup Complete!                         ║"
echo "╚══════════════════════════════════════════════════════════╝"
echo ""
echo "  Admin SSH:   port 22 (your normal login)"
echo "  Game SSH:    port $GAME_PORT (players connect here)"
echo ""
echo "  Binary:      /opt/usurper/UsurperReborn"
echo "  Database:    /var/usurper/usurper_online.db (created on first connect)"
echo "  Logs:        /var/usurper/logs/"
echo "  Backups:     /var/usurper/backups/ (daily at 4 AM)"
echo ""
IP=$(hostname -I | awk '{print $1}')
echo "  Players connect with:"
echo "    ssh -p $GAME_PORT usurper@$IP   (password: play)"
echo ""
echo "  Or from the game client:"
echo "    Main Menu → [O]nline Play → Server: $IP, Port: $GAME_PORT"
echo "    Username: usurper, Password: play"
echo ""
echo "  Players register their own account/password in-game on first connect."
echo ""
echo "  Test locally:"
echo "    sudo -u usurper /opt/usurper/UsurperReborn --online --user TestPlayer --stdio"
echo ""
echo "  Service management:"
echo "    sudo systemctl status sshd-usurper"
echo "    sudo systemctl restart sshd-usurper"
echo "    sudo systemctl stop sshd-usurper"
echo ""
echo "  Health check:"
echo "    /opt/usurper/healthcheck.sh"
echo ""
