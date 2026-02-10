# Usurper Reborn - Online Server Deployment Guide

## Overview

Deploy Usurper Reborn as an online multiplayer server. Players connect via SSH to a shared game world with persistent characters, shared NPCs/economy, and inter-player chat.

The server runs a **dedicated SSH daemon on port 4000** (configurable) with a shared gateway account. Players register their own in-game accounts on first connect. Admin SSH stays on port 22.

## Requirements

- Linux server (Ubuntu 22.04+ LTS recommended)
- 1 GB RAM minimum (supports ~20 concurrent players)
- 20 GB disk space
- SSH access on port 22 (for admin)
- Self-contained game binary (no .NET runtime needed)

## Quick Start

```bash
# 1. Upload the tarball to the server
scp publish/linux-x64/usurper-reborn-server.tar.gz root@your-server:/tmp/

# 2. Extract and install
ssh root@your-server
mkdir -p /opt/usurper
tar xzf /tmp/usurper-reborn-server.tar.gz -C /opt/usurper

# 3. Run the setup script (default port 4000)
sudo /opt/usurper/setup-server.sh

# 4. Players connect with:
#    ssh -p 4000 usurper@your-server   (password: play)
#    Then register/login in-game.
```

## How It Works

```
Player's PC                          Your Server
+--------------+                     +----------------------+
| Game Client  |   SSH port 4000     | sshd-usurper service |
| [O]nline Play|-------------------->|  ForceCommand:       |
| or ssh client|  user: usurper      |   UsurperReborn      |
|              |  pass: play         |   --online --stdio   |
+--------------+                     |                      |
                                     | In-Game Auth Screen: |
                                     |  [L]ogin / [R]egister|
                                     |  Password: ****      |
                                     |                      |
                                     | SQLite Database:     |
                                     |  /var/usurper/       |
                                     |  usurper_online.db   |
                                     +----------------------+
```

- **Port 22**: Admin SSH (your normal login) - untouched
- **Port 4000**: Game SSH (players connect here) - dedicated sshd instance
- **`usurper` account**: Shared gateway with password "play" - just gets you to the game
- **In-game auth**: Players create their own account/password on first connect (stored as PBKDF2 hashes in SQLite)

## Detailed Setup

### 1. Build and Upload

On your development machine:

```bash
# Build the self-contained Linux binary
dotnet publish usurper-reloaded.csproj -c Release -r linux-x64 --self-contained -o publish/linux-x64

# Upload tarball to server
scp publish/linux-x64/usurper-reborn-server.tar.gz root@your-server:/tmp/

# Or upload all files directly
scp -r publish/linux-x64/* root@your-server:/opt/usurper/
```

### 2. Run Setup Script

```bash
ssh root@your-server

# Extract if using tarball
mkdir -p /opt/usurper
tar xzf /tmp/usurper-reborn-server.tar.gz -C /opt/usurper

# Run setup (default port 4000)
sudo /opt/usurper/setup-server.sh

# Or specify a custom port
sudo /opt/usurper/setup-server.sh 2222
```

The setup script automatically:
- Installs dependencies (ufw, sqlite3, fail2ban)
- Creates `usurper` user and directories
- Creates dedicated sshd config on port 4000
- Creates `sshd-usurper` systemd service
- Sets shared password to "play"
- Configures UFW firewall (ports 22 + game port)
- Configures fail2ban for brute-force protection
- Sets up daily database backups at 4 AM
- Installs health check script

### 3. Cloud Firewall (AWS/Lightsail/etc.)

If using a cloud provider, you must **also** open port 4000 in:
- **AWS EC2**: Security Group > Inbound Rules > Add TCP 4000
- **AWS Lightsail**: Networking tab > Firewall > Add TCP 4000
- **DigitalOcean**: Networking > Firewalls > Add TCP 4000

### 4. Verify

```bash
# Check service is running
sudo systemctl status sshd-usurper

# Test locally on the server
sudo -u usurper /opt/usurper/UsurperReborn --online --user "TestPlayer" --stdio

# Test SSH connection from another machine
ssh -p 4000 usurper@your-server-ip
```

## Directory Layout

```
/opt/usurper/              # Game binary and scripts
  UsurperReborn            # Main executable
  *.dll, *.so              # .NET runtime libraries
  setup-server.sh          # Initial setup (run once)
  update-server.sh         # Update script
  backup.sh                # Database backup
  healthcheck.sh           # Health check
  usurper-shell.sh         # Custom shell (Option B only)
  usurper-world.service    # World sim service file
  usurper-logrotate.conf   # Log rotation config
  web/                     # Website files
    index.html             # Landing page + live stats dashboard
    ssh-proxy.js           # WebSocket-to-SSH bridge + Stats API
    package.json           # Node.js dependencies
    node_modules/          # Installed dependencies

/var/usurper/              # Game data (persists across updates)
  usurper_online.db        # SQLite database (all player data)
  logs/                    # Game logs
    debug.log
    world-sim.log
  backups/                 # Daily database backups
    usurper_online_YYYYMMDD_HHMMSS.db

/etc/ssh/sshd_config_usurper    # Game SSH daemon config
/etc/nginx/sites-available/usurper  # Nginx reverse proxy config
/etc/systemd/system/
  sshd-usurper.service          # Game SSH service
  usurper-web.service           # Web terminal proxy service
  usurper-world.service         # World simulator (optional)
```

## Updating the Game

```bash
# On your dev machine: build and upload
dotnet publish usurper-reloaded.csproj -c Release -r linux-x64 --self-contained -o publish/linux-x64
scp publish/linux-x64/usurper-reborn-server.tar.gz root@your-server:/tmp/

# On the server: extract and run update
mkdir -p /tmp/usurper-update
tar xzf /tmp/usurper-reborn-server.tar.gz -C /tmp/usurper-update
sudo /opt/usurper/update-server.sh /tmp/usurper-update/
```

The update script:
1. Backs up the current binary to `/opt/usurper.bak.TIMESTAMP/`
2. Backs up the database
3. Copies new binary and libraries
4. Restarts the world simulator if running
5. New player connections use the updated binary immediately
6. Existing connections continue with the old binary until disconnect

**Version checking is disabled in online mode** - the game won't prompt players to update. Admin handles updates manually via the update script.

## Website & Browser Play

### Web Terminal (Browser-Based SSH)

Players can connect directly from a web browser at `https://usurper-reborn.net`.

**Architecture**:
```
Browser -> https://usurper-reborn.net (nginx)
  ├── Static landing page (index.html)
  ├── /api/stats -> Node.js (port 3000) -> SQLite game database
  └── /ws -> Node.js WebSocket (port 3000)
              -> SSH to localhost:4000 (game)
```

**Components**:
- `ssh-proxy.js` - Node.js server providing WebSocket-to-SSH bridge + Stats REST API
- `index.html` - Landing page with xterm.js terminal emulator + live stats dashboard
- nginx proxies `/ws` (WebSocket) and `/api/` (HTTP) to Node.js on port 3000

**Setup**:
```bash
# Install Node.js (if not present)
curl -fsSL https://deb.nodesource.com/setup_20.x | sudo -E bash -
sudo apt install -y nodejs

# Install web files
sudo mkdir -p /opt/usurper/web
sudo cp web/index.html web/ssh-proxy.js web/package.json /opt/usurper/web/
sudo chown -R usurper:usurper /opt/usurper/web

# Install dependencies (needs build tools for better-sqlite3)
sudo apt install -y python3 make g++
cd /opt/usurper/web && sudo -u usurper npm install

# Install nginx config
sudo cp scripts-server/nginx-usurper.conf /etc/nginx/sites-available/usurper
sudo ln -sf /etc/nginx/sites-available/usurper /etc/nginx/sites-enabled/
sudo nginx -t && sudo systemctl reload nginx

# Install and start the web proxy service
sudo cp scripts-server/usurper-web.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable usurper-web
sudo systemctl start usurper-web

# Verify
curl http://localhost:3000/api/stats
sudo systemctl status usurper-web
```

**SSL (Let's Encrypt)**:
```bash
sudo apt install certbot python3-certbot-nginx
sudo certbot --nginx -d usurper-reborn.net -d www.usurper-reborn.net
```

### Stats API

The web proxy serves a `GET /api/stats` endpoint that returns live game statistics:

```json
{
  "online": [{"name": "Rage", "level": 15, "className": "Warrior", "location": "Dungeon F12"}],
  "onlineCount": 1,
  "stats": {
    "totalPlayers": 42,
    "totalKills": 1337,
    "avgLevel": 8.5,
    "deepestFloor": 45,
    "totalGold": 500000,
    "marriages": 3,
    "children": 7
  },
  "highlights": {
    "topPlayer": {"name": "Rage", "level": 15, "className": "Warrior"},
    "king": "Rage",
    "popularClass": "Warrior"
  },
  "news": [{"message": "defeated a Dragon", "playerName": "Rage", "time": "2026-02-10T06:00:00"}]
}
```

- Queries the game's SQLite database (read-only)
- 30-second server-side cache
- Used by the landing page's live stats dashboard (auto-refreshes every 30s)

### Web Proxy Management

```bash
# Check web proxy status
sudo systemctl status usurper-web
sudo journalctl -u usurper-web -f

# Restart after updating ssh-proxy.js
sudo systemctl restart usurper-web

# Update web files
sudo cp /tmp/index.html /opt/usurper/web/index.html
sudo cp /tmp/ssh-proxy.js /opt/usurper/web/ssh-proxy.js
sudo chown usurper:usurper /opt/usurper/web/*
sudo systemctl restart usurper-web
```

## Optional: World Simulator

Run background world simulation (NPC movement, economy, daily events) independent of player connections:

```bash
# Install the service
sudo cp /opt/usurper/usurper-world.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable usurper-world
sudo systemctl start usurper-world

# Monitor
sudo systemctl status usurper-world
tail -f /var/usurper/logs/world-sim.log
```

## Optional: Log Rotation

```bash
sudo cp /opt/usurper/usurper-logrotate.conf /etc/logrotate.d/usurper
```

## Monitoring

```bash
# Service status
sudo systemctl status sshd-usurper

# Health check (online players, total accounts, DB size)
/opt/usurper/healthcheck.sh

# View connected players
sqlite3 /var/usurper/usurper_online.db \
  "SELECT username, last_heartbeat FROM online_players WHERE last_heartbeat > datetime('now', '-120 seconds');"

# View all accounts
sqlite3 /var/usurper/usurper_online.db \
  "SELECT username, display_name, created_at, last_login FROM players;"

# Active connections
ss -tnp | grep :4000

# Game process count
pgrep -c UsurperReborn
```

## Alternative: Per-Player OS Accounts (Option B)

Instead of the shared gateway account, you can create individual Linux accounts for each player. This skips in-game auth (the OS username becomes the game username).

```bash
# Add the custom shell to valid shells
echo "/opt/usurper/usurper-shell.sh" | sudo tee -a /etc/shells

# Create a player account
sudo useradd -m -s /opt/usurper/usurper-shell.sh playerA
sudo passwd playerA

# Player connects with: ssh playerA@your-server -p 4000
```

## Service Management

```bash
# Game SSH daemon
sudo systemctl start sshd-usurper
sudo systemctl stop sshd-usurper
sudo systemctl restart sshd-usurper
sudo systemctl status sshd-usurper

# Web terminal proxy (website + stats API)
sudo systemctl start usurper-web
sudo systemctl stop usurper-web
sudo systemctl restart usurper-web
sudo systemctl status usurper-web

# World simulator (optional)
sudo systemctl start usurper-world
sudo systemctl stop usurper-world
sudo systemctl restart usurper-world

# Nginx (reverse proxy for website)
sudo systemctl status nginx
sudo nginx -t && sudo systemctl reload nginx

# View logs
journalctl -u sshd-usurper -f
journalctl -u usurper-web -f
tail -f /var/usurper/logs/debug.log
```

## Troubleshooting

### Players can't connect
1. Check game SSH is running: `sudo systemctl status sshd-usurper`
2. Check firewall: `sudo ufw status` (port 4000 must be ALLOW)
3. Check cloud firewall (AWS Security Group, etc.)
4. Test locally: `ssh -p 4000 usurper@localhost`
5. Check sshd logs: `journalctl -u sshd-usurper -n 50`

### "Connection refused" on port 4000
- Service not running: `sudo systemctl start sshd-usurper`
- Port blocked by firewall: `sudo ufw allow 4000/tcp`
- Cloud firewall blocking: open port 4000 in AWS/Lightsail console

### Database locked errors
- SQLite WAL mode handles most concurrent access automatically
- Check for stuck processes: `fuser /var/usurper/usurper_online.db`
- Restart stuck processes if needed

### High memory usage
- Each game process uses ~30-50 MB RAM
- Check process count: `pgrep -c UsurperReborn`
- Consider upgrading if consistently over 80% RAM

### Game crashes on connect
- Check logs: `tail -f /var/usurper/logs/debug.log`
- Check binary permissions: `ls -la /opt/usurper/UsurperReborn`
- Ensure self-contained publish was used (includes .NET runtime)

## Recommended Hosting

| Provider | Plan | RAM | Cost | Notes |
|----------|------|-----|------|-------|
| AWS Lightsail | $5/mo | 1 GB | $5/mo | Simple, predictable |
| DigitalOcean | Basic | 1 GB | $6/mo | Good for indie games |
| Hetzner | CX22 | 4 GB | ~$5/mo | Great value, EU-based |
| Vultr | Cloud Compute | 1 GB | $6/mo | Many locations |
| Oracle Cloud | Free Tier | 1 GB | Free | ARM (use linux-arm64 build) |

**Recommendation**: Start with **AWS Lightsail $5/month** for simplicity, or **Oracle Cloud Free Tier** for zero-cost testing.
