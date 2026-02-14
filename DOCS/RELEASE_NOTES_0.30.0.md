# Usurper Reborn - v0.30.0 "Band of Brothers"

Massive multiplayer expansion with 10 new features: Player Teams, Offline Mail, Player Trading, Cooperative Dungeons, Player Bounties, Auction House, Team Wars, World Boss Events, Castle Siege, and Team Headquarters. Players can form teams, trade items, place bounties, fight world bosses together, siege the castle as a team, and upgrade their team HQ. Also includes server memory optimization (71% smaller binary, ~50% less RAM per session).

---

## Player Teams (Online)

Players can now create and join teams that are visible to other players across sessions. Previously, only NPC teams existed — player-created teams were invisible to anyone else.

### How It Works
- **Create Team** at the Team Corner costs gold (scales with level). Team is registered in a central database so other players can find and join it.
- **Join Team** by name + password. The system checks player teams first, then falls back to NPC teams.
- **Quit Team** automatically updates the registry. If all members leave, the team is deleted.
- **Team Rankings** now shows both player-created teams and NPC teams, merged and sorted by power.
- **Team Status** shows player members (with online/offline status) alongside NPC members.
- **Login Validation**: If your team was disbanded while you were offline, you're automatically removed on next login.

### Team Name Collision
Team names are checked against both the NPC team list and the player teams database — no duplicates allowed.

---

## Offline Mail (`/mail`)

A full mailbox system for sending messages to any player, online or offline.

### Mailbox UI
Type `/mail` from any location to open your mailbox:
- Paginated inbox showing sender, date, and message preview
- Unread messages marked with `*`
- **[R]ead #** to read a full message
- **[S]end** to compose and send a new message
- **[D]elete #** to delete a message
- **[N]ext/[P]rev Page** for pagination

### Enhanced `/tell`
The `/tell` command now works for offline players too:
- Validates that the recipient exists before sending
- Shows "Message sent!" for online players, or "(offline — they'll see it next login)" for offline players
- Messages always go through the database, so they're never lost

### While You Were Gone
Login summary now shows:
- Unread mail count with "/mail" hint
- Pending trade packages with "/trade" hint

### Spam Protection
- Maximum 20 messages per day per sender
- Message length capped at 200 characters

---

## Player Trading (`/trade`)

An async "package" system for sending items and gold to other players.

### How It Works
1. Player A opens `/trade` and selects **[S]end Package**
2. Picks items from inventory (up to 5) and/or a gold amount
3. Items are removed from inventory and gold is deducted immediately (escrowed)
4. Package is stored in the database with an optional note
5. Recipient gets a notification message
6. Recipient opens `/trade` and sees pending packages
7. **[A]ccept** to receive items and gold, or **[D]ecline** to return everything to sender

### Trade Menu
Type `/trade` from any location:
- **Incoming**: Shows packages waiting for you with sender, contents, and note
- **Sent**: Shows your outgoing pending packages
- **[A]ccept #** / **[D]ecline #** / **[C]ancel #** / **[S]end Package**

### Safety
- Self-trading blocked
- Maximum 10 pending outgoing packages
- Expired packages (7+ days) auto-return gold to sender
- Declined packages return gold to sender immediately

---

## Cooperative Dungeon Runs

Recruit your teammates' characters as AI-controlled "echoes" that fight alongside you in the dungeon.

### How It Works
1. At the Team Corner, select **[W] Recruit Player Ally** (online mode only)
2. See a list of players on your team with their level and class
3. Select an ally — their character is loaded from the database
4. Enter the dungeon — their echo materializes beside you with their real stats, spells, and abilities
5. Echoes fight as AI-controlled allies in combat

### Echo Rules
- Echoes start at full HP from the player's saved stats
- Echoes DO contribute to the 15% team combat bonus
- Echoes do NOT receive XP (their save data is never modified)
- Echo "death" shows "X's echo dissipates into mist..." — no permanent effect
- Echoes are verified to still be on your team at dungeon entry
- Party size cap: 4 total (companions + NPC teammates + player echoes)

### Shared Utility
The `PlayerCharacterLoader` class is a new shared utility that creates combat-ready Characters from saved player data. Used by the PvP Arena, Cooperative Dungeons, and future Team Wars.

---

## Server Memory Optimization

Comprehensive memory optimization for the AWS t3.micro (1GB RAM) server. Player sessions previously used ~185MB each and the world simulator ~213MB, almost entirely from .NET runtime overhead.

### GC Tuning
- Switched from Server GC to **Workstation GC** — Server GC pre-allocates large heap segments designed for multi-core servers, wasting memory on single-vCPU instances
- Disabled Concurrent GC and RetainVM to reduce background memory overhead
- Set **HeapHardLimit to 512MB** — forces aggressive garbage collection instead of letting the managed heap grow unbounded

### IL Trimming & Dependency Cleanup
- Enabled **PublishTrimmed** with partial trim mode — removes unused .NET framework code at publish time
- Removed unused NuGet packages: `Newtonsoft.Json`, `Microsoft.Extensions.DependencyInjection`
- Publish size reduced from **116MB to 34MB** (71% reduction)
- Preserved `System.Text.Json` reflection codepath required for save/load serialization

### Unbounded Collection Caps
- Capped story event log at 500 entries (was 1000), oldest trimmed first
- Capped pending player notifications at 100 (drops oldest when full)
- Capped telemetry event re-queue on failure to prevent infinite buildup
- Capped monarch history at 100 records and royal mail at 50 messages
- Capped recurring duelist cache at 200 entries with LRU eviction
- WorldSimulator emotional cascade rate-limiter now auto-clears every 100 ticks

### Results
- Publish size: 116MB → 34MB (71% smaller)
- Expected runtime memory: ~185MB → ~80-120MB per player session
- More headroom for concurrent players on the 1GB server

---

## Player Bounties (`/bounty`)

Place gold bounties on other players. When someone defeats the target in the PvP Arena, the bounty is automatically collected.

### How It Works
- Type `/bounty` from any location to open the bounty board
- **[P]lace Bounty**: Pick a target player and set a gold amount (minimum 500g). Gold is deducted immediately.
- **[M]y Bounties**: View bounties you've placed and their status
- **Arena Integration**: When you defeat a player in PvP, any active bounties on them are automatically claimed and added to your gold
- Bounty collection is announced in the news feed
- Maximum 3 active bounties per player
- 24-hour cooldown: can't place a new bounty on someone you already have an active bounty on

---

## Auction House (`/auction`)

A public marketplace where players can list items for sale and browse/buy items from other players.

### How It Works
- Type `/auction` from any location to open the Auction House
- **[B]rowse Listings**: See all active auctions with item name, seller, and price
- **[S]ell Item**: Select an item from your inventory, set a price, and list it for 48 hours
- **[M]y Listings**: View your active listings and cancel them if needed
- Buying an item transfers gold to the seller (via `AddGoldToPlayer`) and adds the item to your inventory
- Expired listings are automatically cleaned up
- Maximum 5 active listings per player

---

## Team Wars

Teams can now challenge each other to organized battles with gold wagers.

### How It Works
- At the Team Corner, select **[B] Team Battle (War)** (online mode only)
- **[C]hallenge**: Pick an enemy team and wager gold. Your team must have at least 2 members.
- **Round-Robin Combat**: Each team member fights one opponent in auto-resolved combat based on stats with ±20% randomness
- **Winner Takes All**: The winning team gets 2x the gold wager
- **[H]istory**: View past team war results
- 24-hour cooldown between wars for each team pair
- Results and gold changes posted to the news feed

---

## World Boss Events

Massive bosses that all players on the server can fight together. Damage accumulates across sessions until the boss is defeated or expires.

### How It Works
- On Main Street, select **[7] World Boss** (online mode only)
- A world boss is always active — if none exists, one spawns automatically from 8 boss templates (Level 35-80, HP 40K-200K)
- **HP Bar**: Shows the boss's remaining health as a visual bar
- **Attack**: Fight the boss for 5 rounds. Your damage is recorded in the database.
- **Damage Leaderboard**: See which players have dealt the most damage
- When a boss is defeated, all contributors receive gold rewards proportional to their damage
- Bosses expire after 24 hours if not defeated
- Level 10+ required to participate

---

## Castle Siege

Teams can storm the castle together to overthrow the king. A team-based alternative to the solo Throne Challenge.

### How It Works
- At the Castle (commoner view), select **[B]esiege the Castle** (online mode, team required)
- Your full team is loaded from the database for the assault
- **Phase 1 — Monster Guards**: Your team fights through the king's monster guards together with combined team power
- **Phase 2 — Royal Guards**: Your team fights through NPC guards. Low-loyalty guards may surrender during a siege.
- **Phase 3 — The King**: If all guards are defeated, you face the king in single combat as siege leader
- Victory: You leave your team, claim the throne, and become the new ruler
- Failure: Your team retreats, you lose half your HP
- 24-hour cooldown between siege attempts per team
- All siege outcomes posted to the news feed

---

## Team Headquarters

A new team management hub where teams can invest gold in upgrades and manage a shared vault.

### How It Works
- At the Team Corner, select **[H] Team Headquarters** (online mode, team required)
- **Facilities**: Five upgradeable facilities, each with 10 levels:
  - **Armory**: +2% team attack per level
  - **Barracks**: +5 max team members per level
  - **Training Grounds**: +3% team XP per level
  - **Vault**: +50,000 gold capacity per level
  - **Infirmary**: +5% post-combat healing per level
- **Upgrade**: Invest gold to upgrade facilities. Cost scales with level.
- **Team Vault**: Shared gold storage. Deposit and withdraw gold. Capacity depends on Vault upgrade level.
- Upgrade progress persists in the database

---

## Wizard/Immortal System (MUD Administration style)

Seven tiers of administration power, 35+ wizard commands, WizNet communication channel, and full audit logging. 

### Wizard Hierarchy

| Level | Title | Key Powers |
|-------|-------|------------|
| 0 | Mortal | Normal player |
| 1 | Builder | WizNet chat, `/wizwho`, `/stat`, `/where`, `/holylight` |
| 2 | Immortal | `/invis`, `/goto`, `/godmode`, `/heal`, `/restore`, `/peace`, `/echo` |
| 3 | Wizard | `/summon`, `/transfer`, `/snoop`, `/force`, `/set`, `/slay`, `/freeze`, `/mute` |
| 4 | Archwizard | `/ban`, `/unban`, `/kick`, `/broadcast`, `/promote`, `/demote` |
| 5 | God | `/shutdown`, `/reboot`, `/admin`, `/wizlog` |
| 6 | Implementor 

### Key Features
- **WizNet**: Wizard-only communication channel. Chat messages, system notifications (login/logout), and action alerts (promotions, kicks, bans).
- **Invisibility**: Immortal+ can toggle `/invis` to hide from `/who`, room presence lists, and arrival/departure messages. Higher-level wizards can still see lower-level invisible wizards.
- **Godmode**: Immortal+ can toggle `/godmode` — HP and Mana are fully restored after every combat (PvE and PvP). Wizards cannot die in combat.
- **Player Manipulation**: Wizard+ can `/set` player stats (online or offline), `/force` commands, `/snoop` output, `/freeze` (block all input) and `/mute` (block chat only).
- **Promotion Safety**: Can only promote up to one level below your own tier. No one can promote to Implementor.
- **Audit Trail**: Every wizard action logged to `wizard_log` table with wizard name, action, target, details, and timestamp. Viewable via `/wizlog`.
- **Freeze/Mute Persistence**: Freeze and mute flags persist across sessions via the `wizard_flags` database table.
- **Bootstrap Mechanism**: `--admin` CLI flag now writes God-level wizard status to the database at server startup.

### Database Changes
- `wizard_level` column added to `players` table
- `wizard_log` table for audit trail
- `wizard_flags` table for persistent freeze/mute state

---

## Marketplace / Auction House (Online)

The **Marketplace** is now accessible in online mode via `[J]` from Main Street or `/auction` from any location. Players and NPCs can buy and sell items through a shared SQL-backed auction house.

- **Browse Listings**: View all active listings with item name, price, seller, and time remaining
- **Sell Items**: List inventory items for sale with custom pricing. Max 5 active listings per player. Listings expire after 48 hours.
- **Buy Items**: Purchase items from other players or NPCs. Gold is atomically transferred to the seller (even if offline).
- **Sale Notifications**: Sellers receive an in-game message when their item sells.
- **Cancel Listings**: Cancel active listings to get your item back.
- **NPC Participation**: NPCs list dungeon loot and purchased items at the marketplace. NPCs also browse and buy items they want, creating a living economy.
- **Single-player**: `[J]` opens the original in-memory marketplace (unchanged).

---

## Smarter NPC Behavior

NPCs now make significantly smarter decisions based on past experiences, social bonds, and world state.

### Memory-Driven Behavior
- NPCs who were badly defeated in the dungeon now strongly avoid returning (scaled by severity — a near-death experience creates much stronger avoidance than a minor scuffle)
- Positive and negative experiences at specific locations shape future behavior: successful trades make NPCs return to the marketplace; traumatic events make them avoid that location
- All recent memories are now factored in, not just the 5 most recent

### Relationship-Driven Movement
- NPCs actively seek out friends — they prefer locations where friends are present and avoid locations where enemies lurk
- When choosing where to wander, married couples and close friends gravitate toward each other
- The Inn becomes a true social hub: friends at the Inn pull other friends there
- Enemies at a location act as a repellent, reducing the chance NPCs will visit

### World Event Reactions
- **During War**: Aggressive and courageous NPCs rush to the dungeon to fight; cautious NPCs hide at home and protect their gold at the bank; Crown faction members rally to the castle
- **During Plague**: Everyone seeks more healing and temple visits; cautious NPCs strongly avoid the dungeon; sociable NPCs reduce gatherings at the Inn and Love Street to avoid contagion
- **During Festivals**: Sociable NPCs flock to the Inn to celebrate; marketplace activity increases; dungeon exploration decreases (why fight monsters during a party?)
- **Throne Events**: Crown faction NPCs rally to the castle; Shadows faction celebrates in the Dark Alley; Faith faction prays at the Temple

---

## Bug Fixes

- **Combat tips showing class-specific hints to wrong classes**: Ranger's `[V]ranged attack` and Barbarian's `[G]rage` tips could appear for any class. Class-specific tips now only show to the relevant class.
- **Combat tip wrong key for healing**: Tip said `[I]` for healing potions but the actual key is `[H]`. Fixed.

---

## Files Changed

| File | Changes |
|------|---------|
| `Scripts/Core/GameConfig.cs` | Version bump to 0.30.0 "Band of Brothers" |
| `Scripts/Systems/SqlSaveBackend.cs` | 8 new tables (`player_teams`, `trade_offers`, `bounties`, `auction_listings`, `team_wars`, `world_bosses`, `world_boss_damage`, `castle_sieges`, `team_upgrades`, `team_vault`); ~50 new backend methods for all features |
| `Scripts/Systems/IOnlineSaveBackend.cs` | Data classes: `PlayerTeamInfo`, `TradeOffer`, `BountyInfo`, `AuctionListing`, `TeamWarInfo`, `WorldBossInfo`, `WorldBossDamageEntry`, `CastleSiegeInfo`, `TeamUpgradeInfo` |
| `Scripts/Systems/PlayerCharacterLoader.cs` | **NEW** — shared utility for loading players from DB as AI Characters |
| `Scripts/Locations/TeamCornerLocation.cs` | Online mode in Create/Join/Quit/Rankings/Status; `[W] Recruit Player Ally`; `[B] Team Battle (War)`; `[H] Team Headquarters` with upgrades and vault |
| `Scripts/Locations/BaseLocation.cs` | `/mail`, `/trade`, `/bounty`, `/auction` slash commands with full interactive UI; `/auction` added to help text |
| `Scripts/Locations/ArenaLocation.cs` | Refactored to use shared `PlayerCharacterLoader`; bounty auto-claim on PvP victory |
| `Scripts/Locations/MainStreetLocation.cs` | `[7] World Boss` menu item; `[J] Marketplace` re-enabled (routes to Auction House in online mode) |
| `Scripts/Locations/CastleLocation.cs` | `[B]esiege the Castle` team siege with guard combat phases and king challenge; cap monarch history/royal mail |
| `Scripts/Locations/DungeonLocation.cs` | `RestorePlayerTeammates()` — loads player echoes at dungeon entry; cap recurring duelists at 200 |
| `Scripts/Systems/CombatEngine.cs` | Echo handling: skip XP for echoes, echo death message; class-specific combat tips only shown to relevant class; fixed healing potion tip key |
| `Scripts/Systems/OnlineChatSystem.cs` | `/tell` validates recipient exists, shows online/offline status |
| `Scripts/Core/GameEngine.cs` | `DungeonPartyPlayerNames` field; team validation on login; mail/trade counts in While You Were Gone; cap notifications at 100 |
| `Scripts/Core/Character.cs` | `IsEcho` property for player echo characters |
| `Scripts/Systems/SaveDataStructures.cs` | `DungeonPartyPlayerNames` in save data |
| `Scripts/Systems/SaveSystem.cs` | Serialize/restore `DungeonPartyPlayerNames` |
| `usurper-reloaded.csproj` | GC tuning properties; PublishTrimmed with partial trim mode; removed unused NuGet packages |
| `runtimeconfig.template.json` | **NEW** — HeapHardLimit 512MB, Workstation GC config, JSON reflection flag |
| `Console/Bootstrap/Program.cs` | AppContext.SetSwitch to re-enable JSON reflection at runtime (trimmer override) |
| `Scripts/Systems/StoryProgressionSystem.cs` | Reduced event log cap from 1000 to 500 |
| `Scripts/Systems/TelemetrySystem.cs` | Cap re-queued events on flush failure |
| `Scripts/Systems/WorldSimulator.cs` | Auto-clear cascade rate-limiter every 100 ticks; NPC marketplace uses SQL backend in online mode; enhanced memory-driven behavior with location sentiment; relationship-driven activity weights and movement; world event reactions (war/plague/festival/throne) |
| `Scripts/Systems/MarketplaceSystem.cs` | `EquipOrStoreItem` made internal for WorldSimulator NPC purchases |
| `Scripts/Systems/WorldSimService.cs` | Passes SqlBackend to WorldSimulator for marketplace operations |
| `Scripts/Server/WizardLevel.cs` | **NEW** — WizardLevel enum (Mortal→Implementor) + WizardConstants (titles, colors) |
| `Scripts/Server/WizNet.cs` | **NEW** — Wizard-only communication channel (chat, system notifications, action alerts) |
| `Scripts/Server/WizardCommandSystem.cs` | **NEW** — 35+ wizard commands across all tiers with command router |
| `Scripts/Server/PlayerSession.cs` | Wizard state properties, auto-promote Rage, WizNet login/logout, snoop cleanup, freeze/mute flags |
| `Scripts/Server/SessionContext.cs` | WizardLevel, WizardGodMode, WizardInvisible per-session state |
| `Scripts/Server/MudServer.cs` | `--admin` bootstrap writes God-level to DB; removed runtime AdminUsers HashSet |
| `Scripts/Server/MudChatSystem.cs` | Wizard command routing before chat; freeze/mute checks; `/who` shows wizard titles + hides invisible |
| `Scripts/Server/RoomRegistry.cs` | Invisible wizard filtering in arrivals/departures/presence lists; wizard titles in room presence |
| `Scripts/Systems/SqlSaveBackend.cs` | `wizard_level` column, `wizard_log`/`wizard_flags` tables, 7 new query methods |
| `Scripts/Systems/CombatEngine.cs` | Wizard godmode: HP/Mana restored after PvE and PvP combat |
| `Scripts/Core/GameEngine.cs` | Admin console access uses WizardLevel >= God instead of hardcoded username |

---

## MUD Architecture (True Multi-User Dungeon)

The game can now run as a **true MUD**: a single shared process where all players coexist with instant communication, eliminating the previous one-process-per-SSH-session architecture (~80-120MB per player → one shared process).

### How It Works

**Start the MUD server:**
```bash
UsurperReborn --mud-server --db /var/usurper/usurper_online.db
```

**Players connect via relay:**
```bash
# SSH ForceCommand:
UsurperReborn --mud-relay --user "%u" --mud-port 4001
```

**Or via web browser** (set `MUD_MODE=1` in ssh-proxy.js environment).

### Key Features

- **Single Process**: All players share one .NET process. Memory: ~150MB total instead of 120MB per player.
- **Session Isolation**: Each player gets isolated game state via `AsyncLocal<SessionContext>`. All `SomeSystem.Instance` calls resolve to per-session instances without changing ~500 call sites.
- **Room Presence**: "Also here: PlayerX, PlayerY" shown when entering locations. Entry/exit notifications ("PlayerX arrives", "PlayerX leaves toward the Dungeon").
- **Instant Chat**: `/say` (room-scoped), `/shout` (global), `/tell` (instant private message), `/emote` (room-scoped actions).
- **Combat Visibility**: Players in the same room see each other's combat ("PlayerX draws their weapon and attacks Goblin!", "PlayerX defeated the Goblin.").
- **In-Process World Sim**: World simulator runs as a background task inside the MUD server. No separate `usurper-world.service` needed.
- **Emergency Save**: Players are auto-saved on disconnect.
- **Web Terminal**: Direct TCP mode for web proxy (skip SSH hop, lower latency).

### Backward Compatibility
- **Single-player mode**: Unaffected. `SessionContext.Current` is null, all singletons fall back to static instances.
- **BBS door mode**: Unaffected. Same separate-process-per-BBS behavior.
- **Legacy online mode** (`--online --stdio`): Still works via SSH as before.

### Production Hardening

- **Session Idle Timeout**: Players with no input for 15 minutes are auto-saved and disconnected. Watchdog checks every 60 seconds.
- **Admin Commands**: Server admins can use `/kick <player>`, `/shutdown <seconds>`, `/broadcast <message>`, and `/admin` for help. Admin status is set via `--admin <username>` flags on the server command line.
- **Graceful Shutdown**: `/shutdown` broadcasts countdown warnings at decreasing intervals (5m, 2m, 1m, 30s, 10s, 5, 4, 3, 2, 1) before shutting down.
- **Password Auth for Direct TCP**: The AUTH protocol supports `AUTH:username:password:connectionType` for direct TCP connections (e.g., web proxy in MUD_MODE). Passwords are verified against the SQLite database using PBKDF2. Relay connections (from SSH) remain trusted.
- **Thread-Safe NPC Access**: `NPCSpawnSystem.ActiveNPCs` is now protected by a `ReaderWriterLockSlim`. WorldSim writes (add/remove/clear) take write locks; player session reads get cached snapshots. In single-player/BBS mode there's zero overhead (fast-path bypass).

### New Command-Line Flags

| Flag | Description |
|------|-------------|
| `--mud-server` | Start the TCP game server (MUD mode) |
| `--mud-relay` | Start thin stdin/stdout ↔ TCP bridge |
| `--mud-port <port>` | TCP port for MUD server (default: 4001) |
| `--admin <username>` | Add an admin user (can be repeated) |

### MUD Architecture Files

| File | Description |
|------|-------------|
| `Scripts/Server/SessionContext.cs` | **NEW** — AsyncLocal per-session context with all per-player system instances |
| `Scripts/Server/MudServer.cs` | **NEW** — TCP listener, connection handler, session lifecycle, idle watchdog, admin commands, graceful shutdown |
| `Scripts/Server/PlayerSession.cs` | **NEW** — Per-player session wrapper, system initialization, emergency save, idle tracking, admin flag |
| `Scripts/Server/RelayClient.cs` | **NEW** — Thin stdin/stdout ↔ TCP bridge for SSH ForceCommand |
| `Scripts/Server/RoomRegistry.cs` | **NEW** — In-memory player location tracking, room-scoped broadcasts |
| `Scripts/Server/MudChatSystem.cs` | **NEW** — Instant in-memory chat (/say, /shout, /tell, /emote, /who) + admin commands (/kick, /shutdown, /broadcast) |
| `Scripts/Systems/StoryProgressionSystem.cs` | Session-aware Instance shim |
| `Scripts/Systems/CompanionSystem.cs` | Session-aware Instance shim |
| `Scripts/Systems/*.cs` (21+ files) | Session-aware Instance shim pattern applied to all per-player singletons |
| `Scripts/UI/TerminalEmulator.cs` | Stream-based I/O constructor for TCP sessions; session-aware Instance |
| `Scripts/Core/GameEngine.cs` | Session-aware Instance, PendingNotifications, IsIntentionalExit |
| `Scripts/Systems/LocationManager.cs` | Session-aware Instance; RoomRegistry integration |
| `Scripts/Locations/BaseLocation.cs` | Room presence display; MUD chat routing; message queue draining |
| `Scripts/Systems/CombatEngine.cs` | Combat start/end/flee/death room broadcasts |
| `Scripts/Locations/DungeonLocation.cs` | Dungeon descent room broadcast |
| `Scripts/Systems/NPCSpawnSystem.cs` | ReaderWriterLockSlim for thread-safe NPC list access; cached snapshot pattern |
| `Scripts/BBS/DoorMode.cs` | --mud-server, --mud-relay, --mud-port, --admin flags; session-aware GetPlayerName/OnlineUsername |
| `Console/Bootstrap/Program.cs` | MUD server and relay mode entry points |
| `web/ssh-proxy.js` | MUD_MODE direct TCP connection option |
