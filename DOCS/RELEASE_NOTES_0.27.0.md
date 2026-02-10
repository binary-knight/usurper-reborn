# Usurper Reborn - v0.27.0 Release Notes

## Online Multiplayer

This release adds full online multiplayer support, allowing players to connect to a shared game world via SSH.

### Online Server Architecture

- **Shared SQLite Backend**: All online players share a single SQLite database for persistent world state, player saves, and cross-player interactions
- **SSH-Based Connectivity**: Players connect via `ssh usurper@<server> -p 4000` using a shared gateway account. Real authentication is handled in-game.
- **Dedicated sshd Instance**: Online play runs on a separate sshd service (port 4000) isolated from the server's admin SSH (port 22)
- **Stdio I/O Mode**: Online connections use `--online --stdio` for SSH transport compatibility

### In-Game Authentication System

- **Login/Register Screen**: When connecting without a `--user` flag, players see an in-game authentication screen with options to login, register a new account, or quit
- **PBKDF2 Password Hashing**: Passwords are hashed with 100,000 iterations of PBKDF2-SHA256 with random 16-byte salts, stored as `salt:hash` base64 pairs
- **Constant-Time Comparison**: Password verification uses `CryptographicOperations.FixedTimeEquals` to prevent timing attacks
- **Case-Insensitive Usernames**: All username lookups use `LOWER()` for consistent matching while preserving display name casing
- **Account Validation**: Usernames must be 2-20 characters (letters, numbers, spaces, hyphens, underscores). Duplicate detection is case-insensitive.
- **Ban Support**: Banned players are rejected at login with a message
- **Max Login Attempts**: 5 failed login attempts before automatic disconnect

### Online State Management

- **OnlineStateManager**: Coordinates player state across the shared database - saves, loads, and syncs player data
- **SqlSaveBackend**: Full SQLite backend for player data, authentication, world state, chat, and leaderboards
- **Player Presence**: Tracks online/offline status with heartbeat timestamps
- **Cross-Player Visibility**: Players can see who else is online in the shared world

### Server Deployment

- **Automated Setup**: `scripts-server/setup-server.sh` configures the entire server (user creation, directory structure, sshd config, firewall rules)
- **Server Scripts**: Includes `usurper-shell.sh` (login shell), `usurper-world.service` (world simulator systemd unit), `usurper-logrotate.conf` (log rotation), `update-server.sh` (update deployment), and `backup.sh` (database backup)
- **Tarball Packaging**: Linux builds include a compressed tarball (`usurper-reborn-server.tar.gz`) for easy server transfer
- **SERVER_DEPLOYMENT.md**: Comprehensive deployment guide covering AWS setup, SSH configuration, firewall rules, and maintenance procedures

### New Files
- `Scripts/Systems/OnlineAuthScreen.cs` - Login/register UI for SSH connections
- `Scripts/Systems/OnlineStateManager.cs` - Online player state coordination
- `Scripts/Systems/SqlSaveBackend.cs` - SQLite shared backend for multiplayer
- `DOCS/SERVER_DEPLOYMENT.md` - Server deployment guide
- `scripts-server/setup-server.sh` - Automated server setup script
- `scripts-server/usurper-shell.sh` - Custom login shell for per-player accounts
- `scripts-server/usurper-world.service` - World simulator systemd service
- `scripts-server/usurper-logrotate.conf` - Log rotation configuration
- `scripts-server/update-server.sh` - Server update script
- `scripts-server/backup.sh` - Database backup script

### Website & Browser Play (usurper-reborn.net)

- **Browser-Based Play**: Players can now play directly in their browser at `https://usurper-reborn.net` using an embedded xterm.js terminal that connects via WebSocket-to-SSH proxy
- **Live Stats Dashboard**: The landing page displays a real-time "World Status" section with:
  - Online player list (name, class, level, location, connection type) with pulsing green indicator
  - Player leaderboard / Hall of Fame (rank, name, class, level, experience) with online indicators
  - Aggregate stats: total players, monsters slain, average level, deepest floor, total gold, marriages/children
  - Highlights: top adventurer, current ruler, most popular class
  - Recent news feed from the game world
  - Auto-refreshes every 30 seconds
- **Stats REST API**: `GET /api/stats` endpoint serves live game statistics from the SQLite database with 30-second cache
- **Landing Page Content**: Comprehensive game info including story lore, all 7 Old Gods, 11 classes with stat bars, 10 races, 4 companions with backstories, 5 endings, game history, BBS list, credits, and SVG favicon
- **Nginx Reverse Proxy**: Serves static files, proxies `/ws` for WebSocket terminal and `/api/` for stats endpoint to Node.js on port 3000
- **SSL Support**: Let's Encrypt certificates via certbot for HTTPS

### New Website Files
- `web/index.html` - Landing page with xterm.js terminal, live stats dashboard, game lore
- `web/ssh-proxy.js` - Node.js HTTP + WebSocket server (ws + ssh2 + better-sqlite3)
- `web/package.json` - Node.js dependencies
- `scripts-server/nginx-usurper.conf` - Nginx reverse proxy configuration
- `scripts-server/usurper-web.service` - Systemd service for web proxy

### Online Chat System

- **Cross-Location Chat**: `/say`, `/tell`, `/who`, and `/news` commands work from any game location, not just Main Street
- **Live Message Display**: Incoming chat messages from other players appear at every input prompt across all locations
- **Message Sent Confirmation**: `/say` and `/tell` commands now show "Message sent!" with a brief pause so you can see your message was sent
- **Press Any Key Pauses**: Who's Online and News Feed screens now wait for a keypress before returning to the menu
- **Connection Type Tracking**: Who's Online shows how each player is connected (Web, SSH, BBS, Steam, or Local) both in-game and on the website

### Hall of Fame / Leaderboard

- **In-Game Hall of Fame**: The Fame menu on Main Street now includes all online players from the shared database, ranked alongside NPCs by level and experience. Online players show an `[ON]` tag.
- **Website Leaderboard**: The landing page displays a "Hall of Fame" section with all registered players ranked by level and experience, showing class, rank (with gold/silver/bronze styling for top 3), and online status indicators.

### News Feed System

- **Live Game Events**: The news feed (`[5]` from Main Street or `/news` from anywhere) now shows real-time events from all players:
  - New character creation (with class)
  - Level ups (every 5 levels + first 3 levels)
  - Boss kills
  - Old God defeats and saves
  - Seal discoveries (with count)
  - Player deaths
  - Marriages
  - Achievement unlocks

### Bug Fixes

- **New player registration save bug**: Newly registered accounts could not load because `RegisterPlayer` inserts `player_data = '{}'`, which was treated as a valid save with version 0. Fixed by skipping empty `'{}'` records in `GetPlayerSaves` and `ReadGameData`, and adding fallback to character creation when load fails in online mode.
- **Username case mismatch causing duplicate records**: Registration stored usernames as lowercase but `WriteGameData` saved with original case, creating duplicate database rows. Fixed by normalizing to lowercase in `WriteGameData`. Read queries now use `ORDER BY LENGTH(player_data) DESC` to prefer records with actual data.
- **Broadcast chat messages repeating infinitely**: Broadcast messages (`to_player = '*'`) were never marked as read, causing them to be re-fetched every 5-second poll cycle. Fixed with an ID watermark system that tracks the highest seen message ID, skipping already-processed messages.
- **Who's Online showing 0 players**: `StartOnlineTracking()` was defined but never called, so players were never registered in the `online_players` table. Wired into `Program.cs` after authentication.
- **Web terminal box drawing characters garbled**: SSH proxy converted UTF-8 data to Latin-1 via `data.toString('binary')`. Fixed by sending raw Buffer objects, preserving UTF-8 encoding for box drawing characters (`║ ═ ╔ ╗`).
- **Web terminal duplicate keystrokes on reconnect**: Each "Play Now" click registered additional `terminal.onData` handlers without disposing old ones, causing multiplied keystrokes. Fixed by tracking and disposing previous handlers before re-registering.
- **Online mode hides [O]nline Play menu**: The "Online Play" menu option is no longer shown when already connected in online mode
- **Character name conflict resolved**: Online mode now correctly uses `RunBBSDoorMode()` flow so the authenticated username becomes the character name automatically
- **Version checking disabled for online mode**: Automatic update checks are skipped in online server mode
- **Who's Online always showing Main Street**: `OnlineStateManager.UpdateLocation()` was never called when changing locations. Fixed by calling it in `BaseLocation.EnterLocation()` after updating the player's location.
- **Private messages not delivered between players**: SQLite string comparison is case-sensitive by default. Messages sent via `/tell Ted` stored `to_player = "Ted"` but the recipient's `GetUnreadMessages` query did exact case match. Fixed with `LOWER()` in both `GetUnreadMessages` and `MarkMessagesRead` queries.
- **Achievement news events**: Achievements now post to the shared news feed when unlocked in online mode.

### Command Line Options

```bash
# Start in online mode (with in-game auth)
UsurperReborn --online --stdio

# Start in online mode (pre-authenticated, for scripted/BBS use)
UsurperReborn --online --user "PlayerName" --stdio

# Start in online mode with custom database path
UsurperReborn --online --stdio --db /var/usurper/usurper_online.db
```

### Files Changed
- `Console/Bootstrap/Program.cs` - Online auth flow, StartOnlineTracking, connection type detection
- `Scripts/BBS/DoorMode.cs` - `SetOnlineUsername()`, online mode detection
- `Scripts/Core/GameEngine.cs` - Online mode uses `RunBBSDoorMode()`, hides online menu, fallback to character creation on load failure, death news
- `Scripts/Core/GameConfig.cs` - Version 0.27.0-alpha, "Online Multiplayer"
- `Scripts/Systems/SqlSaveBackend.cs` - Username normalization, empty record handling, ID watermark queries, GetMaxMessageId, connection_type column, case-insensitive message queries, GetAllPlayerSummaries
- `Scripts/Systems/OnlineStateManager.cs` - Message ID watermark, StartOnlineTracking with connection type, GetAllPlayerSummaries
- `Scripts/Systems/OnlineChatSystem.cs` - PressAnyKey, message sent confirmation, connection type in Who's Online
- `Scripts/Systems/IOnlineSaveBackend.cs` - GetUnreadMessages afterMessageId, GetMaxMessageId, GetAllPlayerSummaries, PlayerSummary class, ConnectionType on OnlinePlayerInfo
- `Scripts/Systems/AchievementSystem.cs` - Achievement unlock news events for online mode
- `Scripts/Systems/CombatEngine.cs` - Boss kill news events
- `Scripts/Systems/RelationshipSystem.cs` - Marriage news events
- `Scripts/Locations/BaseLocation.cs` - Online location tracking on EnterLocation
- `Scripts/Locations/DevMenuLocation.cs` - Separate online mode passcode
- `Scripts/Locations/DungeonLocation.cs` - Old God defeat/save news, seal collection news
- `Scripts/Locations/LevelMasterLocation.cs` - Level up news events
- `Scripts/Locations/MainStreetLocation.cs` - Online menu options, Hall of Fame includes online players
- `Scripts/Systems/VersionChecker.cs` - Skip update checks in online mode
- `Scripts/Locations/SysOpLocation.cs` - Skip update checks in online mode
- `Scripts/Systems/SysOpConsoleManager.cs` - Skip update checks in online mode
- `web/index.html` - Landing page, stats dashboard, leaderboard, connection type column, UTF-8 fix
- `web/ssh-proxy.js` - Stats API with leaderboard and connection type, UTF-8 fix
- `web/package.json` - Added better-sqlite3 dependency
- `scripts-server/nginx-usurper.conf` - Added /api/ proxy location
- `scripts-server/usurper-web.service` - Web proxy systemd service
- `DOCS/SERVER_DEPLOYMENT.md` - Added website setup and stats API documentation