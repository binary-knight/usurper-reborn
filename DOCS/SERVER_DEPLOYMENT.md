# Usurper Reborn - Online Server Deployment Guide

## Overview

Host your own Usurper Reborn online multiplayer server. Players connect via SSH to a shared game world with persistent characters, NPCs, economy, and inter-player chat.

> DEPLOYMENT NOTE: the canonical production server (usurper-reborn.net) runs the **MUD Server** model
> behind an haproxy multiplexer on port 4000 (SSH-version-line sniff routes "SSH-..." to sshd-usurper
> on 4022, everything else to the MudServer on 4001 with a PROXY v2 header for real-IP visibility).
> That live setup, including exact service names, ports, and deploy commands, is documented at the top
> of `CLAUDE.md` and in `DOCS/ARCHITECTURE.md`; treat those as the source of truth for the recommended
> topology. The **Simple (Per-Process)** model below is still fully supported for small self-hosted
> communities (it runs one game process per SSH connection with `--online --stdio` and needs no
> haproxy), but the MUD Server model is what the production server uses and what scales.

There are two deployment models:

| Model | Best For | RAM per Player | World Simulation |
|-------|----------|----------------|------------------|
| **Simple (Per-Process)** | Small communities, 1-5 players | ~80-120 MB | Only when players are online |
| **MUD Server** | Larger communities, 5+ players | ~5-10 MB | 24/7 (NPCs active even when empty) |

Both models use a **shared gateway SSH account** on a dedicated port (default 4000). Players SSH in with a shared password, then register their own in-game account. Admin SSH stays on port 22.

## Requirements

- Linux server (Ubuntu 22.04+ LTS recommended)
- 1 GB RAM minimum (Simple model: ~5 concurrent players; MUD model: ~20+)
- 10 GB disk space
- SSH access on port 22 (for admin)
- Self-contained game binary (no .NET runtime needed on the server)

## Quick Start (Simple Model)

The simple model spawns a new game process per SSH connection. It's the easiest to set up and works great for small communities.

```bash
# 1. Build the Linux binary on your dev machine
dotnet publish usurper-reloaded.csproj -c Release -r linux-x64 --self-contained -o publish/linux-x64

# 2. Upload to your server
scp -r publish/linux-x64/* root@your-server:/opt/usurper/

# 3. Run the setup script (default port 4000)
ssh root@your-server
sudo /opt/usurper/setup-server.sh

# 4. Players connect with:
#    ssh -p 4000 usurper@your-server   (password: play)
#    Then register/login in-game.
```

That's it! The setup script handles everything: user creation, SSH config, firewall, backups, and monitoring.

## How It Works (Simple Model)

```
Player's PC                          Your Server
+--------------+                     +----------------------+
| SSH Client   |   SSH port 4000     | sshd-usurper service |
| or Game      |-------------------->| ForceCommand:        |
| Client       |  user: usurper      |  UsurperReborn       |
|              |  pass: play         |  --online --stdio    |
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

Each SSH connection spawns a separate game process. All processes share the same SQLite database for persistent state.

## Detailed Setup

### 1. Build and Upload

On your development machine:

```bash
# Build the self-contained Linux binary
dotnet publish usurper-reloaded.csproj -c Release -r linux-x64 --self-contained -o publish/linux-x64

# Upload to server (direct copy)
scp -r publish/linux-x64/* root@your-server:/opt/usurper/

# Or create a tarball first
cd publish/linux-x64
tar czf /tmp/usurper-reborn-server.tar.gz .
scp /tmp/usurper-reborn-server.tar.gz root@your-server:/tmp/

# Then on server:
mkdir -p /opt/usurper
tar xzf /tmp/usurper-reborn-server.tar.gz -C /opt/usurper
```

### 2. Run Setup Script

```bash
ssh root@your-server

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
  usurper-mud.service      # MUD server service file (advanced)
  usurper-web.service      # Web terminal service file
  usurper-logrotate.conf   # Log rotation config
  web/                     # Website files (optional)
    index.html             # Landing page + live stats dashboard
    ssh-proxy.js           # WebSocket-to-SSH bridge + Stats API
    package.json           # Node.js dependencies
    node_modules/          # Installed dependencies

/var/usurper/              # Game data (persists across updates)
  usurper_online.db        # SQLite database (all player data)
  logs/                    # Game logs
    debug.log
    mud-server.log         # MUD server logs (if using MUD model)
  backups/                 # Daily database backups
    usurper_online_YYYYMMDD_HHMMSS.db

/etc/ssh/sshd_config_usurper    # Game SSH daemon config
/etc/nginx/sites-available/usurper  # Nginx reverse proxy config (optional)
/etc/systemd/system/
  sshd-usurper.service          # Game SSH service
  usurper-mud.service           # MUD game server (advanced)
  usurper-web.service           # Web terminal proxy (optional)
```

## Updating the Game

```bash
# On your dev machine: build and upload
dotnet publish usurper-reloaded.csproj -c Release -r linux-x64 --self-contained -o publish/linux-x64
scp -r publish/linux-x64/* root@your-server:/tmp/usurper-update/

# On the server: run update
sudo /opt/usurper/update-server.sh /tmp/usurper-update/
```

The update script:
1. Backs up the current binary to `/opt/usurper.bak.TIMESTAMP/`
2. Backs up the database
3. Copies new binary and libraries
4. Restarts services (MUD server and/or sshd-usurper)
5. New player connections use the updated binary immediately
6. For the simple model, existing connections continue with the old binary until disconnect

**Version checking is disabled in online mode** - the game won't prompt players to update. You handle updates manually via the update script.

## Website & Browser Play (Optional)

### Web Terminal (Browser-Based SSH)

Players can connect from a web browser using an embedded terminal. This requires nginx, Node.js, and the web proxy service.

**Architecture**:
```
Browser -> https://your-domain.com (nginx)
  +-- Static landing page (index.html)
  +-- /api/stats -> Node.js (port 3000) -> SQLite game database
  +-- /ws -> Node.js WebSocket (port 3000)
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

**SSL (Let's Encrypt)** - recommended for production:
```bash
sudo apt install certbot python3-certbot-nginx
sudo certbot --nginx -d your-domain.com -d www.your-domain.com
```

### Stats API

The web proxy serves a `GET /api/stats` endpoint with live game statistics:

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

### Web Service Management

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

## Advanced: MUD Server (High-Capacity)

For larger communities or if you want NPCs to be active 24/7 (even when no players are online), use the MUD server model. Instead of spawning a process per connection, a single persistent game server handles all player sessions and runs the world simulation in-process.

### How It Works (MUD Model)

```
Player's PC                          Your Server
+--------------+                     +----------------------------+
| SSH Client   |   SSH port 4000     | sshd-usurper service       |
| or Game      |-------------------->| ForceCommand:              |
| Client       |  user: usurper      |  UsurperReborn             |
|              |  pass: play         |  --mud-relay               |
+--------------+                     |  --mud-port 4001           |
                                     +----------|------- ---------+
                                                |  TCP relay
                                     +----------v-----------------+
                                     | usurper-mud service        |
                                     | (single process)           |
                                     |                            |
                                     | TCP port 4001:             |
                                     |  All player sessions       |
                                     |  World simulator           |
                                     |  NPC AI & economy          |
                                     |                            |
                                     | SQLite Database:           |
                                     |  /var/usurper/             |
                                     |  usurper_online.db         |
                                     +----------------------------+
```

- **sshd-usurper** handles SSH authentication, then relays the connection to the MUD server via TCP
- **usurper-mud** is a single persistent process that manages all player sessions and runs the world simulation
- Much lower memory per player (~5-10 MB vs ~80-120 MB in the simple model)
- NPCs explore the dungeon, shop, form teams, and interact 24/7

### MUD Server Setup

After running the basic `setup-server.sh`, add the MUD server:

```bash
# 1. Update the sshd ForceCommand to use relay mode instead of direct mode
sudo sed -i 's|ForceCommand /opt/usurper/UsurperReborn --online --stdio|ForceCommand /opt/usurper/UsurperReborn --mud-relay --mud-port 4001|' /etc/ssh/sshd_config_usurper

# 2. Install the MUD server service
sudo cp /opt/usurper/usurper-mud.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable usurper-mud

# 3. Edit the service file to customize admin names and settings
sudo systemctl edit usurper-mud --full
# Key flags:
#   --mud-port 4001        TCP port for player relay connections
#   --db <path>            SQLite database path
#   --sim-interval 30      World simulation tick interval (seconds)
#   --npc-xp 3.0           NPC XP gain multiplier
#   --save-interval 5      State persistence interval (minutes)
#   --admin <name>         Grant admin privileges to a player (repeatable)

# 4. Start the MUD server, then restart sshd to pick up the new ForceCommand
sudo systemctl start usurper-mud
sudo systemctl restart sshd-usurper

# 5. Verify
sudo systemctl status usurper-mud
sudo journalctl -u usurper-mud -f
```

### MUD Server Management

```bash
# Status
sudo systemctl status usurper-mud

# View live logs
sudo journalctl -u usurper-mud -f
# or
tail -f /var/usurper/logs/mud-server.log

# Restart (disconnects all players - they can reconnect immediately)
sudo systemctl restart usurper-mud

# Stop gracefully (60-second timeout for player saves)
sudo systemctl stop usurper-mud
```

### Updating with MUD Server

When updating the game binary with the MUD server model, you need to restart both services:

```bash
# Deploy new binary (update-server.sh handles this)
sudo /opt/usurper/update-server.sh /tmp/usurper-update/

# The update script automatically restarts usurper-mud and sshd-usurper
```

## Optional: Log Rotation

```bash
sudo cp /opt/usurper/usurper-logrotate.conf /etc/logrotate.d/usurper
```

This rotates logs daily, keeps 7 days, and compresses old logs.

## Monitoring

```bash
# Service status
sudo systemctl status sshd-usurper
sudo systemctl status usurper-mud    # if using MUD model

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

# Game process count (simple model: one per player; MUD model: just one)
pgrep -c UsurperReborn
```

## Alternative: Per-Player OS Accounts

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

# MUD game server (if using MUD model)
sudo systemctl start usurper-mud
sudo systemctl stop usurper-mud
sudo systemctl restart usurper-mud
sudo systemctl status usurper-mud

# Web terminal proxy (optional, for browser play)
sudo systemctl start usurper-web
sudo systemctl stop usurper-web
sudo systemctl restart usurper-web
sudo systemctl status usurper-web

# Nginx (reverse proxy for website, optional)
sudo systemctl status nginx
sudo nginx -t && sudo systemctl reload nginx

# View logs
journalctl -u sshd-usurper -f
journalctl -u usurper-mud -f
journalctl -u usurper-web -f
tail -f /var/usurper/logs/mud-server.log
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

### MUD server: players connect but see nothing
- Check MUD server is running: `sudo systemctl status usurper-mud`
- Check MUD server logs: `journalctl -u usurper-mud -n 50`
- Verify sshd ForceCommand uses `--mud-relay --mud-port 4001` (not `--online --stdio`)
- Test relay locally: `/opt/usurper/UsurperReborn --mud-relay --mud-port 4001`

### Database locked errors
- SQLite WAL mode handles most concurrent access automatically
- Check for stuck processes: `fuser /var/usurper/usurper_online.db`
- Restart stuck processes if needed

### High memory usage
- **Simple model**: Each game process uses ~80-120 MB RAM. Check count: `pgrep -c UsurperReborn`
- **MUD model**: Single process, ~200-300 MB total regardless of player count
- Consider upgrading RAM if consistently over 80%
- Consider switching to MUD model if running out of memory with many players

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

**Recommendation**: Start with **AWS Lightsail $5/month** for simplicity, or **Oracle Cloud Free Tier** for zero-cost testing. The simple model works well on 1 GB for a few players. If you expect 5+ concurrent players, use the MUD server model or upgrade to 2+ GB RAM.
