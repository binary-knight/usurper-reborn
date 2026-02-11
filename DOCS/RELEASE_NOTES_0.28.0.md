# Usurper Reborn - v0.28.0 Release Notes

## PvP Combat Arena (Online Multiplayer)

### Arena Location
- **New "Arena (PvP)" location** accessible from Main Street option `[6]` in online mode only. Players challenge other characters to asynchronous PvP combat in a gladiatorial arena setting.
- Level 5 minimum required to enter the Arena.
- Maximum 5 PvP attacks per day to prevent farming.
- Cannot attack the same player twice in one day to prevent targeted harassment.

### Opponent Selection
- Browse all eligible players (online and offline) within ±20 levels of your character.
- Opponent list shows: Name, Level, Class, and `[ONLINE]` tag for currently connected players.
- Already-attacked-today players are filtered out of the list.
- Confirmation prompt warns that damage persists and shows gold steal percentage.

### Asynchronous Combat
- Defender is loaded from their saved data at full HP and AI-controlled (`CharacterAI.Computer`).
- Defender's spells and learned abilities are restored so the AI can use them in combat.
- Uses the existing `CombatEngine.PlayerVsPlayer()` system - same combat mechanics as NPC duels.
- Defender's save data is never modified by the combat itself (only gold theft is applied atomically).

### Gold Theft (Bidirectional)
- **Attacker wins**: Steals 10% of the defender's gold on hand. Gold is atomically deducted from the defender's save via `DeductGoldFromPlayer()` (SQL `json_set` with `MAX(0, ...)`).
- **Defender wins**: Steals 10% of the attacker's gold on hand. Gold is atomically added to the defender's save via new `AddGoldToPlayer()` method.
- Gold stolen is recorded in the `pvp_log` table for leaderboard tracking.
- Bank gold is safe from PvP theft (only gold on hand is at risk).

### Persistent Damage & Death Handling
- **Attacker's HP/mana damage persists** after combat - classic Usurper risk/reward. Attacker is autosaved after every fight.
- **PvP death handled in-Arena** with appropriate messaging (no more "Monster defeats" counter incrementing from PvP losses):
  - 10% of attacker's gold stolen by defender
  - 10% XP loss (standard death penalty)
  - 25% gold loss (standard death penalty, applied after PvP theft)
  - Resurrected at Inn with half HP
  - Player is set alive before returning to game loop to prevent the standard `HandleDeath()` from double-triggering

### XP Rewards
- Attacker earns XP on victory, scaled by level difference (0.5x-2.0x multiplier).
- XP range clamped to 25-5,000 to prevent extremes.
- XP is handled by `CombatEngine.DeterminePvPOutcome()` - no double-counting.

### Defender Notifications
- When attacked, the defender receives an in-game message via `SendMessage()`:
  - Win: `[Arena] PlayerName attacked you in the Arena and won! They stole X of your gold.`
  - Loss: `[Arena] PlayerName attacked you in the Arena but your shadow defeated them! You gained X gold.`
- Messages appear at the defender's next input prompt via the existing chat/message system.

### PvP Leaderboard
- In-game leaderboard shows top 20 players ranked by wins.
- Table columns: Rank, Name, Level, Class, W/L record, Gold Stolen.
- Top 3 players get color highlighting (gold/white/yellow).

### Fight History
- View the last 10 PvP fights with attacker vs defender, winner highlighted, gold stolen amount.

### Personal Stats
- View your PvP kills, deaths, and win rate percentage (from `PlayerStatistics.TotalPlayerKills/Deaths`).

### News Integration
- PvP victories and defeats are posted to the shared news feed visible to all online players.
- News messages include gold stolen amount when applicable.

### While You Were Gone
- On login, online players see a summary of events since their last session.
- Shows PvP attacks against the player (wins/losses with gold stolen amounts).
- Shows unread direct messages (up to 5).
- Shows recent world news up to 10 items (level ups, boss kills, PvP fights, marriages, etc.).
- Color-coded by event type (combat=red, pvp=yellow, politics=cyan, romance=magenta, etc.).
- Only displayed when there are events to show; skipped silently on first login.
- Data sourced from `last_logout` timestamp in `players` table, querying `pvp_log`, `messages`, and `news` tables.

### PvP Achievements
- **Arena Initiate** - Win your first PvP fight (Bronze, 10pts, 100g reward)
- **Arena Veteran** - Win 10 PvP fights (Gold, 50pts, 2,000g reward)

### Website PvP Leaderboard
- PvP leaderboard section added to the website landing page between Hall of Fame and News Feed.
- Shows: Rank, Player, Class, Level, Wins, Losses, Gold Stolen.
- Top 3 ranks get gold/silver/bronze styling.
- Data sourced from `/api/stats` endpoint.

### Stats API
- PvP leaderboard added to the `GET /api/stats` response as `pvpLeaderboard` array.
- Each entry: `rank`, `name`, `level`, `className`, `wins`, `losses`, `goldStolen`.
- Uses UNION ALL query joining `pvp_log` with `players` table, grouped by player.

## NPC Lifecycle: Children, Aging & Natural Death

The world of Usurper is now truly alive. Married NPCs have children, children grow up and join the realm, adults age over time, and eventually die of old age. Age-related death is permanent -- no resurrection, no respawn. The soul moves on.

### NPC Pregnancies
- Married female NPCs (ages 18-45) have a 1% chance per world tick to become pregnant.
- Dynamic pregnancy rate adjusts based on population:
  - 3% when population drops below 40 (to prevent extinction)
  - 0.5% when population exceeds 80 (to prevent overcrowding)
  - 1% at normal population levels
- Maximum 4 children per couple.
- Same-sex couples do not have natural pregnancies (keeping the medieval fantasy setting consistent).
- Pregnancy announcements posted to the news feed.
- Gestation period: ~7 real hours.

### NPC Children
- Children are born using the existing `Child` class and `FamilySystem`.
- Birth announcements posted to the news feed.
- Children age using the accelerated lifecycle rate (9.6 real hours = 1 game-year).
- When a child reaches age 18, `ConvertChildToNPC()` creates a full NPC with:
  - Stats based on level (18-22 range) and racial modifiers
  - Soul-based trait inheritance from parents
  - Random class assignment
  - Coming-of-age news announcement
- 80 fantasy names available (40 male, 40 female) with Roman numeral suffixes for duplicates.

### Accelerated NPC Aging
- `NpcLifecycleHoursPerYear = 9.6` -- approximately 1 game-year every 9.6 real hours.
- This gives a Human NPC (~75 year lifespan) approximately 30 real days from birth to natural death.
- Elves live much longer (~80 real days), while Orcs/Gnolls cycle faster (~20-22 real days).
- Age is computed from `BirthDate` using `TotalHours / NpcLifecycleHoursPerYear` -- drift-proof.

### Race Lifespans
| Race | Max Age | Real Days (approx) |
|------|---------|-------------------|
| Human | 75 | 30 |
| Hobbit | 90 | 36 |
| Elf | 200 | 80 |
| Half-Elf | 120 | 48 |
| Dwarf | 150 | 60 |
| Troll | 60 | 24 |
| Orc | 55 | 22 |
| Gnome | 130 | 52 |
| Gnoll | 50 | 20 |
| Mutant | 65 | 26 |

### Natural Death
- When an NPC exceeds their race's lifespan, they die permanently.
- `IsAgedDeath = true` -- prevents `ProcessNPCRespawns()` from ever resurrecting them.
- Widowed spouses have marriage state cleared and receive a bereavement memory.
- Natural death news posted to the feed.
- Story NPCs (`IsStoryNPC == true`) are exempt to prevent breaking narrative quests.

### Birthday Announcements
- When an NPC's age increments (every ~9.6 hours), a birthday announcement is posted.
- Uses proper ordinal suffixes ("21st", "42nd", "73rd", etc.).
- ~6 birthday events per hour across all 60 NPCs (one every ~10 minutes).

### NPC Divorce
- Married NPC couples have a base 0.3% chance per world tick to divorce.
- Personality-driven modifiers:
  - Low commitment (< 0.3) increases divorce chance by +0.5% per spouse
  - High flirtatiousness (> 0.7) increases divorce chance by +0.3% per spouse
  - Alignment mismatch (one good, one evil) adds +0.4%
  - Both high-commitment (> 0.7) reduces chance by 80%
- On divorce: marriage state cleared, pregnancies cancelled, both NPCs free to remarry.
- Divorce news posted to the feed.

### Polyamorous Relationships
- NPCs with `RelationshipPreference.Polyamorous` or `OpenRelationship` personality can seek additional partners while already married.
- Both parties must be poly/open for a polyamorous union to form.
- Uses the existing personality system -- these preferences were already defined but never used in marriage logic.
- Distinct news: "X and Y have entered a polyamorous union!" vs "Wedding Bells!"

### Affairs
- Flirtatious married NPCs (Flirtatiousness > 0.6) have a 15% chance of conceiving with someone other than their spouse.
- Unmarried flirtatious NPCs (> 0.5) can conceive casually with any compatible partner.
- `_pregnancyFathers` dictionary tracks the actual father for affair babies (separate from SpouseName).
- Affair partner selection: prefers other flirtatious NPCs, requires mutual attraction.
- Scandal news: "Scandal! X and Y are having a secret affair!"

### NPC Level-Up News
- NPC level-ups now appear in the news feed with race and class info.
- Format: "NpcName the Race Class has achieved Level X!"

### Legacy Save Migration
- Existing NPCs without `BirthDate` (all current saves) are assigned:
  - Random age between 18-50 if `Age` was not previously saved
  - Computed `BirthDate` from age: `DateTime.Now.AddHours(-age * NpcLifecycleHoursPerYear)`
- Ensures seamless upgrade from pre-lifecycle saves.

## The Living World (Website)

### SSE Live Feed
- Server-Sent Events (`/api/feed`) replace 30-second polling for instant delivery.
- Separate NPC activity and player news streams pushed to browser clients.
- 5-second server-side polling of SQLite with high-water mark tracking.
- Auto-reconnect after 5 seconds on disconnect.

### Lifecycle Event Color Coding
| Event | Color | Pattern |
|-------|-------|---------|
| Natural death | Muted purple (#9966cc) | "passed away", "soul moves on" |
| Combat death | Red (#ff4444) | dagger symbol |
| Pregnancy | Soft pink (#ffaacc) | "expecting a child" |
| Birth | Bright pink (#ff88dd) | "proud parents", "born" |
| Coming-of-age | Teal (#44ddaa) | "come of age", "joined the realm" |
| Birthday | Gold (#ffdd44) | "birthday", "celebrates their" |
| Level up | Gold-orange (#ffaa00) | "Level" + number |
| Divorce | Dark red (#cc4444) | "divorced" |
| Affair | Hot pink (#ff4488) | "affair", "Scandal" |
| Polyamory | Violet (#dd66ff) | "polyamorous" |
| Respawn | Green (#88ff88) | "returned from the realm of the dead" |

### Side-by-Side Feed Layout
- NPC activity and player news feeds displayed in a CSS grid layout.
- Each feed scrolls independently with its own SSE event stream.

## Bug Fixes

### Backspace in SSH/Online Mode
- Backspace key sent raw `\x7F`/`\x08` bytes through `Console.ReadLine()` in SSH mode, causing garbage characters in input. Fixed with custom `ReadLineWithBackspace()` that handles DEL/BS character-by-character and discards ANSI escape sequences.

### Duplicate Players in PvP Arena & Leaderboards
- Emergency autosave rows (`emergency_*`) appeared as duplicate entries in PvP opponent list, website leaderboard, and stats. Fixed by adding `username NOT LIKE 'emergency_%'` filter to `GetAllPlayerSummaries()`, `GetAllPlayerNames()`, and all website stats queries.

### Website "Most Popular Class" Showing Unknown
- NULL `class_id` from empty/test accounts winning the GROUP BY query. Fixed by adding `json_extract(player_data, '$.player.class') IS NOT NULL` filter.

### PvP Double Kill/XP/Gold Counting
- `CombatEngine.DeterminePvPOutcome()` already calls `RecordPlayerKill()`, awards XP, and awards gold. `ArenaLocation.ProcessPvPResult()` was duplicating all three. Fixed by removing duplicate calls from ArenaLocation.

### PvP Gold Theft Never Worked
- `CreateCombatCharacterFromSave()` never set `opponent.Gold` from `playerData.Gold`, so it defaulted to 0. Fixed by adding `Gold = playerData.Gold` to the character builder.

### PvP Death Shows "Monster Defeats"
- Dying in PvP triggered the standard `HandleDeath()` which incremented the "Monster defeats" counter. Fixed by handling death entirely in ArenaLocation.

### Bidirectional Gold Theft
- Only attacker-wins gold theft was implemented initially. Fixed by adding defender-wins gold theft via `AddGoldToPlayer()`.

## Database Changes

### New Table: `pvp_log`
```sql
CREATE TABLE IF NOT EXISTS pvp_log (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    attacker TEXT NOT NULL,
    defender TEXT NOT NULL,
    attacker_level INTEGER NOT NULL,
    defender_level INTEGER NOT NULL,
    winner TEXT NOT NULL,
    gold_stolen INTEGER DEFAULT 0,
    xp_gained INTEGER DEFAULT 0,
    attacker_hp_remaining INTEGER DEFAULT 0,
    rounds INTEGER DEFAULT 0,
    created_at TEXT DEFAULT (datetime('now'))
);
```

## Serialization

### New NPCData Fields
```csharp
public int Age { get; set; }
public DateTime BirthDate { get; set; } = DateTime.MinValue;
public bool IsAgedDeath { get; set; }
public DateTime? PregnancyDueDate { get; set; }
```

## Game Balance Constants

```csharp
MaxPvPAttacksPerDay = 5       // Daily attack limit
MinPvPLevel = 5               // Minimum level to enter Arena
PvPGoldStealPercent = 0.10    // 10% of loser's gold on hand
PvPLevelRangeLimit = 20       // ±20 levels for opponent eligibility
PvPMinXPReward = 25           // Minimum XP for a PvP win
PvPMaxXPReward = 5000         // Maximum XP for a PvP win
```

## Files Changed

### New Files
- `Scripts/Locations/ArenaLocation.cs` - PvP arena location with opponent selection, combat, leaderboard, history, stats

### Modified Files
- `Scripts/Core/GameConfig.cs` - `Arena = 501` enum, PvP constants, `RaceLifespan` dictionary, `NpcLifecycleHoursPerYear`, version 0.28.0
- `Scripts/Core/NPC.cs` - `BirthDate`, `IsAgedDeath`, `PregnancyDueDate` properties; BirthDate calculation in constructor
- `Scripts/Core/GameEngine.cs` - `ShowWhileYouWereGone()` login summary; restore lifecycle fields in `RestoreNPCs()` with migration
- `Scripts/Systems/IOnlineSaveBackend.cs` - `PvPLeaderboardEntry` and `PvPLogEntry` data classes
- `Scripts/Systems/SqlSaveBackend.cs` - `pvp_log` table, PvP query methods, `AddGoldToPlayer()`, "While You Were Gone" queries, `emergency_%` filter on player queries
- `Scripts/Systems/SaveDataStructures.cs` - `Age`, `BirthDate`, `IsAgedDeath`, `PregnancyDueDate` on NPCData
- `Scripts/Systems/SaveSystem.cs` - Serialize lifecycle fields in NPC save block
- `Scripts/Systems/WorldSimulator.cs` - `ProcessNPCAging()`, `ProcessNPCPregnancies()`, `HandleSpouseBereavement()`, `ProcessNPCDivorces()`, birthday detection, affair system with `_pregnancyFathers`
- `Scripts/Systems/FamilySystem.cs` - `CreateNPCChild()`, `GenerateNPCChildName()`, accelerated aging in `ProcessDailyAging()`, BirthDate carry-over in `ConvertChildToNPC()`
- `Scripts/Systems/NewsSystem.cs` - `WriteNaturalDeathNews()`, `WriteComingOfAgeNews()`, `WriteBirthdayNews()`, `WriteNPCLevelUpNews()`, `WriteAffairNews()`, `GetOrdinalSuffix()`
- `Scripts/AI/EnhancedNPCBehaviors.cs` - Polyamorous marriage support in `AttemptNPCMarriage()` and `ExecuteNPCMarriage()`
- `Scripts/UI/TerminalEmulator.cs` - `ReadLineWithBackspace()` for SSH/online mode backspace handling
- `Scripts/Systems/LocationManager.cs` - Arena registration and navigation
- `Scripts/Locations/MainStreetLocation.cs` - `[6] Arena (PvP)` menu item (online mode only)
- `Scripts/Systems/AchievementSystem.cs` - 2 PvP achievements (`pvp_first_fight`, `pvp_10_wins`)
- `web/index.html` - PvP leaderboard, SSE live feed, lifecycle color coding, side-by-side layout
- `web/ssh-proxy.js` - PvP leaderboard in stats API, SSE `/api/feed` endpoint, `emergency_%` filter
- `scripts-server/nginx-usurper.conf` - SSE proxy configuration
- `scripts-server/usurper-world.service` - Updated to `--sim-interval 30 --npc-xp 3.0`
- `README.md` - Version bump and v0.28.0 changelog
