# Usurper Reborn - Online Multiplayer Architecture

## Overview

Centralized multiplayer server where Steam, Local, and BBS users all connect via SSH to a shared game world. Characters exist only on the server - no local save crossover.

## Architecture

```
┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐
│ Steam Client│  │ Local Client│  │  BBS User   │  │   Browser   │
│  [O]nline   │  │  [O]nline   │  │  (SSH/telnet│  │  (xterm.js) │
│   Play      │  │   Play      │  │   client)   │  │   Web Play  │
└──────┬──────┘  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘
       │ SSH            │ SSH            │ SSH        WSS  │
       └────────┬───────┘────────────────┘                 │
                │                                          │
                ▼                                          ▼
       ┌──────────────────────────────────────────────────────────┐
       │                  AWS EC2 (Linux x64)                     │
       │                                                          │
       │  nginx (ports 80/443)                                    │
       │    ├── Static files (/opt/usurper/web/index.html)        │
       │    ├── /api/stats → Node.js (port 3000) → SQLite DB     │
       │    └── /ws → Node.js WebSocket → SSH localhost:4000      │
       │                                                          │
       └──────────────────────────┬───────────────────────────────┘
                                  │
                                  ▼
       ┌──────────────────────────────────────┐
       │        AWS EC2 (Linux x64)           │
       │                                      │
       │  OpenSSH Server                      │
       │    ├─ User A ──→ UsurperReborn       │
       │    │              --online            │
       │    │              --user "PlayerA"    │
       │    ├─ User B ──→ UsurperReborn       │
       │    │              --online            │
       │    │              --user "PlayerB"    │
       │    └─ User C ──→ UsurperReborn       │
       │                   --online            │
       │                   --user "PlayerC"    │
       │                                      │
       │  SQLite Database:                    │
       │    /var/usurper/usurper_online.db     │
       │                                      │
       │  Game Binary:                        │
       │    /opt/usurper/UsurperReborn         │
       └──────────────────────────────────────┘
```

## Save Architecture - Complete Separation

### Local/Steam Mode (Unchanged)
- JSON file saves on player's machine
- Single-player experience
- Existing `SaveSystem.cs` with `FileSaveBackend`
- Player controls everything (dev menu, cheats, etc.)

### Online Mode (New)
- Characters exist ONLY on the server in SQLite
- No import from local saves, no export to local
- Server controls all game logic
- No dev menu access (or SysOp-only)
- Shared world state: king, NPCs, economy, news

### Why No Crossover
- Prevents cheated characters from entering online world
- Server is authoritative - all game logic runs server-side
- Client is a dumb SSH terminal sending keystrokes
- Local saves can't be hex-edited to gain online advantage

## Database Schema (SQLite)

```sql
-- Player characters (private per player)
CREATE TABLE players (
    username TEXT PRIMARY KEY,
    display_name TEXT NOT NULL,
    player_data TEXT NOT NULL,        -- Full PlayerData as JSON blob
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    last_login DATETIME,
    last_logout DATETIME,
    total_playtime_minutes INTEGER DEFAULT 0,
    is_banned INTEGER DEFAULT 0,
    ban_reason TEXT
);

-- Shared world state (one row per subsystem)
CREATE TABLE world_state (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL,              -- JSON blob per subsystem
    version INTEGER DEFAULT 1,       -- Optimistic locking
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_by TEXT                   -- Which player last modified
);
-- Keys: 'king', 'npcs', 'economy', 'events', 'teams',
--        'daily_state', 'dungeon_state', 'marriages'

-- News feed (shared, append-only)
CREATE TABLE news (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    message TEXT NOT NULL,
    category TEXT,                    -- 'combat', 'politics', 'romance', etc.
    player_name TEXT,                 -- Who triggered this news
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- Currently connected players
CREATE TABLE online_players (
    username TEXT PRIMARY KEY,
    display_name TEXT,
    location TEXT,                    -- Current game location
    node_id TEXT,                     -- Process ID or node identifier
    connection_type TEXT DEFAULT 'Unknown', -- Web, SSH, BBS, Steam, or Local
    connected_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    last_heartbeat DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- Inter-player messages (chat, trade offers, duel challenges)
CREATE TABLE messages (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    from_player TEXT NOT NULL,
    to_player TEXT NOT NULL,          -- '*' for broadcast
    message_type TEXT NOT NULL,       -- 'chat', 'trade', 'duel', 'system'
    message TEXT NOT NULL,
    is_read INTEGER DEFAULT 0,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- Player-to-player trade offers
CREATE TABLE trade_offers (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    from_player TEXT NOT NULL,
    to_player TEXT NOT NULL,
    offer_data TEXT NOT NULL,         -- JSON: items/gold offered
    request_data TEXT NOT NULL,       -- JSON: items/gold requested
    status TEXT DEFAULT 'pending',    -- pending, accepted, rejected, expired
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    expires_at DATETIME
);

-- Indexes for performance
CREATE INDEX idx_news_created ON news(created_at DESC);
CREATE INDEX idx_messages_to ON messages(to_player, is_read);
CREATE INDEX idx_online_heartbeat ON online_players(last_heartbeat);
```

## Save Backend Interface

Phase 1 implemented the core `ISaveBackend` interface with `FileSaveBackend`:

```csharp
// ISaveBackend - both local (JSON) and online (SQLite) implement this
public interface ISaveBackend
{
    // Core save/load operations
    Task<bool> WriteGameData(string playerName, SaveGameData data);
    Task<SaveGameData?> ReadGameData(string playerName);
    Task<SaveGameData?> ReadGameDataByFileName(string fileName);
    bool GameDataExists(string playerName);
    bool DeleteGameData(string playerName);

    // Save enumeration
    List<SaveInfo> GetAllSaves();
    List<SaveInfo> GetPlayerSaves(string playerName);
    SaveInfo? GetMostRecentSave(string playerName);
    List<string> GetAllPlayerNames();

    // Autosave with rotation
    Task<bool> WriteAutoSave(string playerName, SaveGameData data);
    void CreateBackup(string playerName);
    string GetSaveDirectory();
}
```

Phase 2 adds the `SqlSaveBackend` which extends this with online-specific operations:

```csharp
// Additional online-specific operations (SqlSaveBackend only)
public interface IOnlineSaveBackend : ISaveBackend
{
    // World state (shared across all players)
    Task SaveWorldState(string key, object data);
    Task<T?> LoadWorldState<T>(string key);
    Task<bool> TryAtomicUpdate(string key, Func<string, string> transform);

    // News
    Task AddNews(string message, string category, string playerName);
    Task<List<string>> GetRecentNews(int count = 20);

    // Online tracking
    Task RegisterOnline(string username, string displayName, string location, string connectionType = "Unknown");
    Task UpdateHeartbeat(string username, string location);
    Task UnregisterOnline(string username);
    Task<List<OnlinePlayerInfo>> GetOnlinePlayers();

    // Messaging
    Task SendMessage(string from, string to, string type, string message);
    Task<List<MessageInfo>> GetUnreadMessages(string username);
    Task MarkMessagesRead(string username);
}
```

## Browser Play (Web Terminal)

Players can connect directly from a web browser at `https://usurper-reborn.net`.

### Web Architecture
- **nginx** serves the static landing page and reverse-proxies WebSocket/API traffic
- **ssh-proxy.js** (Node.js) runs on port 3000, providing:
  - WebSocket-to-SSH bridge: browser connects via WebSocket, proxy SSH's to game on localhost:4000
  - Stats REST API: `GET /api/stats` returns live game data from the SQLite database
- **xterm.js** in the browser provides the terminal emulator UI
- **Live Stats Dashboard** on the landing page shows who's online, aggregate stats, highlights, and news

### Stats API Response
```json
{
  "online": [{"name": "...", "level": 15, "className": "Warrior", "location": "Dungeon F12", "connectionType": "SSH", "connectedAt": "..."}],
  "onlineCount": 1,
  "stats": {"totalPlayers": 42, "totalKills": 1337, "avgLevel": 8.5, "deepestFloor": 45, "totalGold": 500000, "marriages": 3, "children": 7},
  "highlights": {"topPlayer": {...}, "king": "Rage", "popularClass": "Warrior"},
  "leaderboard": [{"rank": 1, "name": "Rage", "level": 15, "className": "Warrior", "experience": 12500, "isOnline": true}],
  "news": [{"message": "...", "playerName": "Rage", "time": "..."}]
}
```

### Key Files
- `web/index.html` - Landing page with embedded xterm.js terminal + live stats dashboard
- `web/ssh-proxy.js` - Node.js HTTP + WebSocket server (ws + ssh2 + better-sqlite3)
- `web/package.json` - Node.js dependencies
- `scripts-server/nginx-usurper.conf` - nginx reverse proxy config
- `scripts-server/usurper-web.service` - systemd service for web proxy

## Client-Side Changes

### Main Menu Addition
```
Main Menu:
  [N]ew Game           → Local single-player (JSON saves)
  [L]oad Game          → Local single-player (JSON saves)
  [O]nline Play        → Connect to server via SSH
  [S]ettings           → Game settings
  [Q]uit
```

### SSH Connection Library: SSH.NET (Renci.SshNet)

Instead of spawning a system SSH process (which requires SSH to be installed on
the user's machine), the game uses **SSH.NET** (`Renci.SshNet`) - a pure C# SSH
library available as a NuGet package. This means:

- **Zero external dependencies** - SSH is handled natively inside the game binary
- **Works on any Windows machine** - no need for OpenSSH client, PuTTY, or WSL
- **Cross-platform** - works on Windows, Linux, and macOS identically
- **Full control** - we manage the SSH stream directly, can handle reconnection,
  show connection status, and handle errors gracefully in-game

```xml
<!-- NuGet package reference -->
<PackageReference Include="SSH.NET" Version="2024.1.0" />
```

```csharp
// Simplified connection flow
using Renci.SshNet;

var client = new SshClient("play.usurper-reborn.net", 22, "usurper", password);
client.Connect();
var stream = client.CreateShellStream("usurper", 80, 24, 800, 600, 1024);

// Game reads/writes to stream - all I/O encrypted via SSH
stream.WriteLine(keystroke);
var output = stream.Read();
```

### Online Play Flow (Client)
1. Player selects [O]nline Play
2. Game prompts for username/password (or loads saved credentials)
3. Game connects to `play.usurper-reborn.net` via SSH.NET (native C# SSH)
4. SSH stream established - server launches game in online mode
5. Player creates character or loads existing server-side character
6. All I/O piped through SSH stream (encrypted, no external SSH client needed)
7. On disconnect, server auto-saves and cleans up

### Alternative: Direct Connection via External SSH Client
For BBS users or those who prefer their own SSH client:
```
Server: play.usurper-reborn.net
Port: 22 (or custom port like 2222)
Username: usurper
Password: (set during first connection, or use SSH keys)
```

## Server-Side Changes

### New Command Line Flag
```bash
UsurperReborn --online --user "PlayerName"
```

- `--online` flag enables SQLite backend instead of JSON files
- `--user` identifies which player this session is for
- SSH forced command ensures this is always set

### SSH Server Configuration
```
# /etc/ssh/sshd_config (or separate config)
Match User usurper
    ForceCommand /opt/usurper/UsurperReborn --online --stdio
    AllowTcpForwarding no
    X11Forwarding no
    PermitTunnel no
```

Or use a login shell approach:
```bash
# /etc/shells - add custom shell
/opt/usurper/usurper-shell.sh

# usurper-shell.sh
#!/bin/bash
exec /opt/usurper/UsurperReborn --online --user "$USER" --stdio
```

### User Account Management
Option A: System accounts (simple)
- Create Linux user per player: `useradd -s /opt/usurper/usurper-shell.sh playerA`
- SSH password auth or key-based
- Player username = Linux username

Option B: Single shared account with in-game auth (simpler)
- One Linux user: `usurper`
- SSH ForceCommand launches game
- Game handles its own login/registration screen
- Username/password stored in SQLite `players` table
- This is probably the better approach - easier to manage

### Online-Specific Game Behavior
- No dev menu (or admin-only with server-side flag)
- No local file access
- Autosave on every location change and every 60 seconds
- Heartbeat every 30 seconds (update `online_players` table)
- Check for messages every 5 seconds
- Display "X players online" in status bar
- "Who's Online" command shows connected players and locations
- PvP: can challenge players who are in the same location
- Chat: send messages to players in same location or globally

### Daily Reset Coordination
- First player to trigger daily boundary acquires a database lock
- Processes daily events (NPC aging, respawns, economy, etc.)
- Sets `daily_state.last_reset_day` to prevent double-processing
- Other players see "Daily maintenance in progress..." briefly

### Crash/Disconnect Recovery
- `online_players` entries expire after 120 seconds without heartbeat
- Background cleanup process removes stale entries
- Player data auto-saved before any risky operation
- SQLite WAL mode prevents corruption on process crash

## Implementation Phases

### Phase 1: ISaveBackend Interface (Foundation) ✅ COMPLETE
- Created `ISaveBackend` interface (`Scripts/Systems/ISaveBackend.cs`)
- Created `FileSaveBackend` implementing JSON file persistence (`Scripts/Systems/FileSaveBackend.cs`)
- Refactored `SaveSystem` to delegate all I/O to backend (serialization logic stays in SaveSystem)
- Constructor injection: `SaveSystem(ISaveBackend)` allows swapping backends
- Default constructor uses `FileSaveBackend` - zero behavior change for existing code
- Build verified: 0 errors

### Phase 2: SQLite Backend ✅ COMPLETE
- Added `Microsoft.Data.Sqlite` v8.0.11 NuGet package
- Created `IOnlineSaveBackend` interface extending `ISaveBackend` with online features
- Implemented `SqlSaveBackend` (~550 lines) with full SQLite persistence:
  - Auto-creates schema on first run (players, world_state, news, online_players, messages + indexes)
  - WAL mode for concurrent read/write safety
  - World state with optimistic locking (version numbers)
  - Online player tracking with 120s heartbeat timeout
  - Inter-player messaging (direct + broadcast)
  - Player ban system, login/logout tracking, playtime accumulation
- Build verified: 0 errors

### Phase 3: Online Mode Flag ✅ COMPLETE
- Added `--online` flag to `DoorMode.ParseCommandLineArgs()` (enables SQLite backend)
- Added `--user <name>` flag for SSH ForceCommand player identification
- Added `--db <path>` flag for custom database location (default: `/var/usurper/usurper_online.db`)
- `DoorMode.IsOnlineMode`, `DoorMode.OnlineUsername`, `DoorMode.OnlineDatabasePath` static properties
- `SaveSystem.InitializeWithBackend()` allows injecting SqlSaveBackend before first use
- `Program.cs` wires online mode: creates SqlSaveBackend and injects into SaveSystem on startup
- Help text updated with online mode documentation
- Build verified: 0 errors
- **Remaining**: Online main menu with in-game auth (deferred to Phase 8)

### Phase 4: Shared World State ✅ COMPLETE
- Created `OnlineStateManager` singleton (`Scripts/Systems/OnlineStateManager.cs`)
- Shared world state stored in `world_state` table with JSON blobs per subsystem:
  - `npcs` - All NPC data (stats, death, marriage, faction, equipment)
  - `world_events` - Active world events
  - `quests` - Active quest data
  - `story_systems` - Story progression, companions, Old Gods, etc.
  - `daily_state` - Daily reset coordination
  - `marriages` - NPC marriage registry
- `SaveAllSharedState()` / `LoadAllSharedState()` for bulk operations
- Daily reset coordination via `TryProcessDailyReset()` with atomic locking
- `SaveSystem.SerializeStorySystemsPublic()` exposed for shared state serialization
- Initialized in `Program.cs` alongside SqlSaveBackend when `--online` is active
- Build verified: 0 errors

### Phase 5: Online Player Tracking ✅ COMPLETE
- Integrated into `OnlineStateManager`:
  - `StartOnlineTracking()` registers player and starts background timers
  - Heartbeat timer: updates `online_players` table every 30 seconds
  - Message check timer: polls for unread messages every 5 seconds
  - Stale cleanup timer: removes entries with no heartbeat for 120+ seconds
  - `UpdateLocation()` called when player changes game location
  - `GetOnlinePlayers()` / `GetOnlinePlayerCount()` for "Who's Online" display
- `SendMessage()` / `BroadcastMessage()` for inter-player messaging
- `AddNews()` / `GetRecentNews()` for shared news feed
- Graceful `Shutdown()` on disconnect (unregisters, stops timers, updates session)
- Build verified: 0 errors

### Phase 6: Inter-Player Features ✅ COMPLETE
- Created `OnlineChatSystem` singleton (`Scripts/Systems/OnlineChatSystem.cs`)
- Chat commands: `/say` (broadcast), `/tell` (private), `/who` (who's online), `/news` (town news)
- "Who's Online" display with player name, location, and connected duration
- Color-coded news feed by category (combat/politics/romance/economy/quest)
- Message queue system: incoming messages queued by `OnlineStateManager`, displayed at safe points
- `TryProcessCommand()` integrates with any location's input loop
- `DisplayPendingMessages()` called between turns/menus to show queued messages
- `OnlineStateManager.ProcessIncomingMessage()` forwards to `OnlineChatSystem.QueueIncomingMessage()`
- Initialized in `Program.cs` alongside OnlineStateManager, shut down in finally block
- Build verified: 0 errors

### Phase 7: Server Deployment ✅ COMPLETE
- Created comprehensive `DOCS/SERVER_DEPLOYMENT.md` deployment guide
- Two SSH approaches documented: shared account with in-game auth (recommended) vs per-player accounts
- ForceCommand SSH config for security (no shell access, no port forwarding)
- Automated daily SQLite backups with 14-day rotation (+ optional S3)
- Systemd service template for background world simulation
- Log rotation, health check script, and monitoring setup
- Fail2ban and UFW firewall configuration
- Update procedure (zero-downtime for existing connections)
- DNS setup and hosting provider comparison (Lightsail, DigitalOcean, Hetzner, Vultr, Oracle)

### Phase 8: Client Integration ✅ COMPLETE
- Added `SSH.NET` v2024.1.0 NuGet package for native C# SSH
- Created `OnlinePlaySystem` (`Scripts/Systems/OnlinePlaySystem.cs`) - full client connection system:
  - `[O]nline Play` menu option added to main menu (hidden in BBS door mode)
  - Server/port/username/password prompt with defaults
  - Credential storage with base64 password obfuscation (`online_credentials.json`)
  - SSH connection via `Renci.SshNet.SshClient` with 15s timeout and retry
  - Shell stream (80x24 terminal) for remote game I/O
  - Console-level I/O piping: reads ANSI output from server, sends keystrokes back
  - Windows VT100 processing enabled via `SetConsoleMode` for ANSI color support
  - Key translation: Enter, Backspace, Escape, Tab, arrow keys mapped correctly
  - Ctrl+] disconnect (classic telnet escape sequence)
  - Graceful error handling: auth failure, connection error, timeout, network error
  - Clean disconnect and resource cleanup
- Build verified: 0 errors

## EC2 Instance Sizing

### Why This Is Lightweight
- Text-based game: bytes per second, not megabytes
- Turn-based: CPU mostly idle (waiting for player input)
- Each game process: ~30-50MB RAM
- SQLite: minimal overhead, no separate database server
- SSH: ~5MB overhead per connection

### Recommended Starting Instance

**t3.micro** ($8.50/month) or **t3.small** ($17/month)

| Resource | t3.micro | t3.small | Capacity |
|----------|----------|----------|----------|
| vCPUs | 2 | 2 | More than enough |
| RAM | 1 GB | 2 GB | ~15-20 / ~30-40 players |
| Network | Up to 5 Gbps | Up to 5 Gbps | Overkill for text |
| Storage | 8-20 GB EBS | 8-20 GB EBS | Plenty |
| CPU Credits | Burstable | Burstable | Fine for turn-based |

### Cost Breakdown (t3.micro)
- Instance: ~$8.50/month (or free tier eligible for first year)
- EBS Storage (20GB gp3): ~$1.60/month
- Data Transfer: negligible (text-based)
- **Total: ~$10/month**

### When to Scale Up
- 20+ concurrent players → t3.small ($17/month)
- 50+ concurrent players → t3.medium ($34/month)
- 100+ concurrent players → consider t3.large or dedicated instance

### Alternative: Lightsail
AWS Lightsail is even simpler for this use case:
- $3.50/month: 512MB RAM, 1 vCPU, 20GB SSD (~10 players)
- $5/month: 1GB RAM, 1 vCPU, 40GB SSD (~20 players)
- $10/month: 2GB RAM, 1 vCPU, 60GB SSD (~40 players)
- Includes static IP, SSH, and simple firewall
- No surprise billing - flat monthly rate

### Recommendation
**Start with Lightsail $5/month** (1GB RAM). Simple, predictable cost,
no AWS complexity. Upgrade to EC2 t3.small if you outgrow it.

## Server Setup Checklist

1. Launch Lightsail/EC2 instance (Ubuntu 22.04 LTS, x64)
2. SSH in, update packages: `sudo apt update && sudo apt upgrade`
3. Install .NET 8 runtime: `sudo apt install dotnet-runtime-8.0`
   (or deploy self-contained binary - no runtime needed)
4. Create game user: `sudo useradd -m -s /bin/bash usurper`
5. Deploy game binary to `/opt/usurper/`
6. Create data directory: `/var/usurper/` (for SQLite DB, logs)
7. Configure SSH for game access (ForceCommand or custom shell)
8. Set up systemd service for world simulation background tasks
9. Configure firewall: allow SSH (port 22 or custom)
10. Set up daily backup of SQLite database to S3

## Security Considerations

- SSH handles encryption and authentication (SSH.NET on client, OpenSSH on server)
- No direct database access from outside
- Game binary runs as unprivileged `usurper` user
- SQLite file permissions: 600 (owner only)
- Rate limiting on SSH connections (fail2ban)
- Player passwords hashed with bcrypt (if using in-game auth)
- No shell access - ForceCommand only runs the game
- Input validation on all player commands (already exists)
- SSH.NET handles host key verification to prevent MITM attacks
- Credentials can be stored locally (encrypted) for convenience
