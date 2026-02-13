# Usurper Reborn - v0.29.1 "The Observatory"

A hidden analytics dashboard for observing the NPC "Game of Life" simulation in real-time. Watch 60+ autonomous NPCs live their lives through interactive charts, relationship networks, and live event feeds.

---

## NPC Analytics Dashboard

A hidden, authenticated web dashboard at `/dashboard` that visualizes the 24/7 NPC simulation. Population demographics, personality profiles, emotional states, relationship networks, and live event timelines -- all updating in real-time via Server-Sent Events.

### Authentication
- Registration open to anyone who finds the `/dashboard` URL (not linked from the main site).
- PBKDF2 password hashing (100k iterations, SHA-256, random 32-byte salt).
- Session tokens stored in SQLite with 7-day expiry. HttpOnly cookies.

### Population Overview Bar
Real-time counters at the top of the dashboard: Alive, Dead, Married, Pregnant, Teams, Average Level, Total Gold, and current King. Updates every 30 seconds via SSE.

### World Map
Grid of location cards showing every game location. Each NPC rendered as a colored dot:
- **Color** = faction (gold = Crown, purple = Shadows, blue = Faith, gray = None).
- **Size** = level (larger dots for higher-level NPCs).
- **Indicators**: Gold glow for king, pink halo for pregnant, faded for dead.
- Click any dot to open the NPC detail panel.

### NPC Detail Panel
Click any NPC to see:
- **Personality Radar**: Chart.js radar chart with 8 core traits (Aggression, Loyalty, Intelligence, Greed, Compassion, Courage, Ambition, Patience).
- **Emotional State**: Horizontal bars for Happiness, Anger, Fear, and Trust.
- **Active Goals**: List with goal name, type, and priority.
- **Recent Memories**: Last 10 memories with type, description, and importance.
- **Relationships**: Affinity bars for known characters (positive = green, negative = red).
- Gracefully degrades with "Awaiting AI data..." if brain data hasn't populated yet.

### Demographic Charts (Chart.js)
- **Class Distribution**: Doughnut chart of all 11 classes.
- **Race Distribution**: Doughnut chart of all 8 races.
- **Faction Breakdown**: Horizontal bar chart (Crown, Shadows, Faith, None).
- **Level Distribution**: Histogram with 10-level buckets.

### Relationship Network (D3.js)
Force-directed graph visualizing the social web:
- **Nodes** = NPCs, colored by faction, sized by level.
- **Edges**: Thick red for marriages, blue for teams, green for positive affinity (>30), orange for rivalry (<-30).
- Dead NPCs rendered faded. Married NPCs have pink stroke.
- Drag to rearrange, zoom/pan, click nodes to select NPCs.

### Live Event Timeline
SSE-powered scrolling feed of all game events. Color-coded using the same palette as the main website (death = red, marriage = pink, dungeon = magenta, commerce = green, gossip = amber, etc.). New events slide in with animation.

### Trend Charts
- **Events/Hour**: Stacked bar chart of event categories over the last 24 hours.
- **Population Overview**: Alive vs Dead doughnut chart.

### Real-Time Updates (SSE)
Dedicated `/api/dash/feed` endpoint (separate from the public `/api/feed`):
- `npc-snapshot` events every 30 seconds with MD5 change detection.
- `news` events every 5 seconds for new game activity.
- 15-second heartbeat to keep connections alive.
- Nginx configured with `proxy_buffering off` for instant delivery.

---

## Enhanced NPC Serialization

`OnlineStateManager.SerializeCurrentNPCs()` now includes full AI state for each NPC, making personality, emotions, goals, memories, and relationships available in the shared database.

### New Fields in NPC Data Blob
- **PersonalityProfile**: All 13 core traits (Aggression, Loyalty, Intelligence, Greed, Compassion, Courage, Honesty, Ambition, Vengefulness, Impulsiveness, Caution, Mysticism, Patience) plus 10 romance traits.
- **EmotionalState**: Happiness, Anger, Fear, Trust (mapped from Joy, Anger, Fear, Gratitude).
- **CurrentGoals**: All active goals with name, type, priority, progress, and status.
- **Memories**: Last 10 memories with type, description, involved character, importance, and timestamp.
- **Relationships**: Character impressions dictionary (name to affinity value).

---

## NPC Death & Combat Overhaul

NPCs were effectively immortal -- dead counter always showed 0. Five root causes identified and fixed.

### Death State Fixes
- **IsDead flag now set on death**: Dungeon deaths, team dungeon deaths, team wars, and NPC-vs-NPC combat all now properly set `IsDead = true` and queue the NPC for respawn. Previously only HP dropped to 0 but the permanent death flag was never set.
- **Respawn timer increased**: Dead NPCs now stay dead for ~10 minutes (20 ticks) instead of ~2.5 minutes (5 ticks), making deaths visible on the dashboard and in the news feed.

### Combat Lethality
- **NPC-vs-NPC combat is now multi-round**: `ExecuteAttack()` changed from a single hit (~1-10 damage, harmless) to a full 30-round duel. NPCs can now actually kill each other. Victor loots 25% of the fallen's gold.
- **Dungeon combat extended**: Solo dungeon fights increased from 20 to 50 rounds. Team vs monster fights from 25 to 40 rounds. Team vs team wars from 15 to 30 rounds.
- **NPCs take more risks**: Dungeon entry HP threshold lowered from 70% to 40%. Dungeon exploration weight increased from 0.25 to 0.30.
- **Deeper dungeon exploration**: NPCs now explore dungeons up to 5 levels above their own (was +2). Ambitious/courageous NPCs push even deeper (+5 more).
- **Rivalry escalation doubled**: Enemy NPCs at the same location now have 12% chance per tick to fight (was 5%).

### NPC Location Diversity
- **Gym**: NPCs now actually move to the Gym when training (was staying at previous location).
- **Level Master**: NPCs now move to Level Master when leveling up (location update was missing).
- **Dark Alley**: New activity -- shady NPCs pickpocket, fence stolen goods, and meet in the shadows. Weighted by darkness alignment, class (Assassin/Ranger), greed, and aggression. Higher weight at night.
- **Inn**: New activity -- NPCs rest, drink, socialize, and gossip. Weighted by HP (wounded NPCs seek rest), sociability, and time of day (evening/night).

---

## Visible Tax System

Every city purchase now shows a detailed tax breakdown. Tax is added ON TOP of the item price -- players pay more, creating real economic pressure and motivation to overthrow high-tax kings or control the city.

### Tax Breakdown Display
When buying anything in the city (weapons, armor, healing, potions, enchantments, drinks, meals), the player sees:
```
  Broadsword: 100 gold
  King's Tax (5%): 5 gold
  City Tax (2% to The Iron Fists): 2 gold
  ─────────────────────────────────
  Total: 107 gold
```
King's tax shown in yellow, city tax in cyan (with team name if applicable), total in bright white. Only displayed when at least one tax rate is > 0%.

### Tax Coverage
- **27 player purchase points** modified across 6 shop files (WeaponShop, ArmorShop, Healer, MagicShop, AdvancedMagicShop, Inn)
- **5 NPC purchase points** modified (weapon/armor shopping, gym training, healer visits)
- **7 missing ProcessSaleTax calls** added (MagicShop: identify, curse removal, dungeon scroll, love spell, death spell, scrying; Inn: drinks, meals)
- **Tax-free zones**: Dark Alley, Love Street (underground economy), Church, Temple (religious), Castle (government), Bank, Home, Dungeon

### NPC Tax Awareness
- **"Control the City" goal**: NPCs with Greed > 0.6 who have a team will pursue city control for the tax revenue
- **Enhanced "Become Ruler" motivation**: When the king's tax rate is high (> 10%), NPCs get a priority boost to seize the throne
- NPCs pay full tax on top of purchases (can't afford items when taxes are high)

---

## Data Persistence Fixes

### Royal Court Persistence (Critical)
Player changes to the king's treasury (deposits, tax policy changes) were being lost on save/reload. The world sim only tracked NPC data version changes but not royal court version changes. When a player deposited gold (only changing `royal_court`, not NPCs), the world sim didn't detect it and overwrote with stale data.

**Fix**: Added independent `lastRoyalCourtVersion` tracking in WorldSimService. The world sim now checks both NPC and royal court versions independently before saving, reloading either when a player session has made changes.

### Dashboard Showing 0 Teams / No City Controller (Critical)
The dashboard always showed 0 teams and no city controller even when the game had 5+ active teams. Root cause: the game server never pushed NPC changes (team formation, combat, etc.) to the shared `world_state` table. `OnlineStateManager.SaveAllSharedState()` existed but was never called. NPC changes only existed in the player's personal save file.

**Fixes**:
1. **Game server NPC sync**: `SaveSystem.SaveGame()` and `AutoSave()` now push NPC state to `world_state` in online mode, so team changes, combat results, and other NPC modifications reach the shared database.
2. **Game server royal court sync**: Both save methods also push royal court state on every save.
3. **Online NPC loading**: Player sessions now load NPCs from `world_state` (authoritative, maintained 24/7 by world sim) instead of from the player's potentially stale save file.

### Player Not Recognized as City Control Leader (Critical)
When a player created a team and controlled the city, the system only looked at NPCs to determine the team leader. Tax revenue went to the highest-level NPC on the team instead of the player, and the dashboard showed the NPC as leader.

**Fixes**:
1. **Player-aware city control**: `GetControllingTeam()`, `GetCityControlInfo()`, and new `GetCityControlLeader()` method now check the player first before falling back to NPCs.
2. **Tax revenue to player**: `ProcessSaleTax()` now deposits city tax to the player's bank account when they're the highest-level team member.
3. **Economy state from game server**: Game server now pushes economy data (including player-as-leader info) to `world_state` on save/autosave, so the dashboard can show the correct leader.

### Children Vanish on World Sim Restart (Critical)
NPC children (born from pregnancies processed by the world sim) were stored only in `FamilySystem._children` in memory. When the world sim service restarted, all children were permanently lost because `SaveChildrenState()` only wrote display-friendly data (for the dashboard) with no way to deserialize it back.

**Fix**: `SaveChildrenState()` now includes a `childrenRaw` array with full `ChildData` objects alongside the display data. New `LoadChildrenState()` method reads this on startup and populates FamilySystem. Children now survive world sim restarts.

### NPC Marriage Registry Empty on Restart (Critical)
The `NPCMarriageRegistry` singleton tracking all NPC-NPC marriages and affairs was never persisted to `world_state`. On world sim restart, the registry started empty. While NPC objects still had `IsMarried`/`SpouseName` from NPC serialization, the centralized registry (needed for divorce processing, affair tracking, and duplicate marriage prevention) was gone.

**Fix**: New `SaveMarriageRegistryState()`/`LoadMarriageRegistryState()` methods in WorldSimService. Marriage and affair data now round-trips through `world_state["marriages"]`. Falls back to rebuilding marriages from NPC `IsMarried`/`SpouseName` fields if no registry data exists.

### Player Sessions Show Stale Children and Marriages (Critical)
When a player logged in, the load order was: (1) Restore NPCs from player save, (2) Override NPCs from world_state (correct), (3) Restore story systems from player save. Step 3 called `FamilySystem.DeserializeChildren()` with the player's **old** children list, and restored marriages from the player's save — overwriting any children born or marriages formed by the world sim while the player was offline.

**Fix**: After `RestoreStorySystems()`, online mode now calls `LoadSharedChildrenAndMarriages()` which loads authoritative children, marriages, affairs, and world events from `world_state`, overriding the stale player save data.

### World Events Lost on World Sim Restart (High)
Active world events (plagues, festivals, economic events, wars) were never persisted by the world sim. On restart, all active events vanished and their global modifiers (price, XP, gold) reset to defaults.

**Fix**: New `SaveWorldEventsState()`/`LoadWorldEventsState()` methods in WorldSimService. World events now use the existing `WorldEventData` format and `RestoreFromSaveData()` for round-trip serialization.

---

## Bug Fixes

### NPC Personality Traits Incomplete (High)
5 of 13 core personality traits (Intelligence, Patience, Mysticism, Trustworthiness, Caution) were never initialized in `PersonalityProfile.GenerateRandom()`, defaulting to 0.0 for all NPCs. The dashboard radar chart also only displayed 8 of 13 traits.

**Fixes**:
- All 13 traits now initialized per archetype in `GenerateRandom()` (thug, merchant, noble, guard, priest, mystic, craftsman, commoner each get appropriate values)
- Migration in `WorldSimService.RestoreNPCsFromData()` detects existing NPCs with all-zero traits and fills them in based on archetype with deterministic random
- Dashboard personality radar expanded from 8 to 13 traits (added Vengefulness, Impulsiveness, Mysticism, Trust, Caution)

### NPC Emotional States Unrealistic (High)
All NPCs showed 95%+ happiness, confidence, greed, and fear simultaneously. Emotional states had no variance and made no logical sense (e.g., a necromancer at 97% joy and 77% fear).

**Root causes**:
- `GetEmotionIntensity()` returned raw intensity instead of time-faded value, so emotions never decayed
- Transient emotions from constant world sim events (combat, gold, friendships) accumulated to ~1.0
- `Math.Max(transient, baseline)` meant the saturated transient value always won
- No antagonistic suppression: contradictory emotions (happy + fearful + angry) coexisted at maximum

**Fixes**:
- `GetEmotionIntensity()` now returns `GetCurrentIntensity()` which fades with elapsed time
- Personality-derived baselines expanded to full 0.0-1.0 range (was capped at 0.5)
- Added antagonistic suppression: high anger dampens happiness/peace, high fear dampens confidence, high happiness dampens sadness
- Transient emotions now modulate baselines by ±15% instead of overriding them
- Result: NPCs now have distinct emotional profiles that reflect their personality (e.g., aggressive NPCs show high anger/low peace, sociable NPCs show high happiness/trust)


### Backspace in SSH/Online Mode (Critical)
Backspace key produced garbage characters instead of erasing text. Affected both the login screen and in-game input.

**Root cause**: In online mode, `Console.IsInputRedirected` returns `false` on a PTY (SSH allocates a pseudo-terminal), so the code fell through to `Console.ReadLine()` which doesn't handle raw `\x7F` (DEL) bytes from SSH terminals.

**Fix**: Rewrote `ReadLineWithBackspace()` as a shared static method with two code paths:
- **Redirected stdin** (pipe): reads raw bytes with manual `0x7F`/`0x08` handling
- **PTY/Console**: uses `Console.ReadKey(intercept: true)` which properly interprets `ConsoleKey.Backspace`

All input paths now use this method in online/door mode:
- `TerminalEmulator.GetInput()` -- main game input
- `OnlineAuthScreen.ReadLineAsync()` -- login screen username
- `OnlineAuthScreen.ReadPasswordAsync()` -- login screen password (with asterisk masking)
- `BBSTerminalAdapter.GetInput()` -- BBS adapter fallback

---

## Password Management

### Admin Password Reset
Server administrators can now reset any player's password from the Online Admin Console (`[P] Reset Player Password`). Useful when players forget their password and contact the admin. Requires only the username and a new password (minimum 4 characters). PBKDF2 hashing applied automatically. All resets logged to debug.log with the admin's username.

### In-Game Password Change
Players can change their own password from the character selection screen (`[C] Change Password`), before entering the game world. Requires current password verification, new password (minimum 4 characters), and confirmation. Available in online mode only. Account actions are kept separate from gameplay.

---

## New Files
- `web/dashboard.html` - NPC Analytics Dashboard SPA (Chart.js + D3.js, all inline)

## Modified Files
- `web/ssh-proxy.js` - Dashboard auth system, 8 API endpoints, dedicated SSE feed
- `Scripts/Systems/OnlineStateManager.cs` - AI state serialization (personality, memories, goals, emotions, relationships); `SaveEconomyToWorldState()` for player-aware economy sync
- `Scripts/Systems/WorldSimulator.cs` - NPC death fixes, multi-round NPC combat, new Dark Alley/Inn activities, Gym/Level Master location updates, NPC tax-on-top purchases
- `Scripts/Systems/WorldSimService.cs` - Independent royal court version tracking, prevents stale overwrites of player changes, player-aware economy data
- `Scripts/Systems/SaveSystem.cs` - NPC, royal court, and economy sync to world_state on save/autosave in online mode
- `Scripts/Systems/CityControlSystem.cs` - `CalculateTaxedPrice()` static method, `DisplayTaxBreakdown()` formatted display, player-aware `GetCityControlLeader()`, `ProcessSaleTax()` deposits to player
- `Scripts/Core/GameEngine.cs` - Online NPC loading from world_state (authoritative), online royal court sync, `[C] Change Password` on character selection screen
- `Scripts/Locations/WeaponShopLocation.cs` - Tax display + charge total at 3 purchase points
- `Scripts/Locations/ArmorShopLocation.cs` - Tax display + charge total at 2 purchase points
- `Scripts/Locations/HealerLocation.cs` - Tax display + charge total at 7 purchase points
- `Scripts/Locations/MagicShopLocation.cs` - Tax display + charge total at 10 purchase points; added 5 missing ProcessSaleTax calls
- `Scripts/Locations/AdvancedMagicShopLocation.cs` - Tax display + charge total at 3 purchase points
- `Scripts/Locations/InnLocation.cs` - Tax display + charge total at 2 purchase points; added 2 missing ProcessSaleTax calls
- `Scripts/AI/GoalSystem.cs` - "Control the City" goal for greedy NPCs, enhanced "Become Ruler" with tax awareness
- `Scripts/UI/TerminalEmulator.cs` - Robust `ReadLineWithBackspace()` for all SSH/online input
- `Scripts/Systems/OnlineAuthScreen.cs` - Backspace-aware login and password input
- `Scripts/BBS/BBSTerminalAdapter.cs` - Backspace-aware input for BBS local mode
- `Scripts/Systems/SqlSaveBackend.cs` - `AdminResetPassword()` for admin force-reset without old password
- `Scripts/Systems/OnlineAdminConsole.cs` - `[P] Reset Player Password` menu item
- `Scripts/Systems/SaveDataStructures.cs` - NPC AI state serialization structures
- `Scripts/AI/EmotionalState.cs` - Emotional state mapping for dashboard
- `Scripts/Systems/WorldSimService.cs` - Children/marriage/world events round-trip through world_state; `LoadChildrenState()`, `LoadMarriageRegistryState()`, `LoadWorldEventsState()`, `SaveMarriageRegistryState()`, `SaveWorldEventsState()`; enhanced `SaveChildrenState()` with raw ChildData
- `Scripts/Systems/OnlineStateManager.cs` - `LoadSharedChildren()`, `LoadSharedMarriages()` for player session world_state loading
- `Scripts/Core/GameEngine.cs` - `LoadSharedChildrenAndMarriages()` overrides stale player save data with world_state in online mode
- `Scripts/AI/PersonalityProfile.cs` - All 13 core traits initialized per archetype in `GenerateRandom()`
- `web/dashboard.html` - Personality radar expanded to 13 traits; compare radar updated to match
- `scripts-server/nginx-usurper.conf` - `/dashboard` clean URL route, SSE unbuffered proxy for `/api/dash/feed`
