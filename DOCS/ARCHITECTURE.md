# Usurper Reborn - Architecture Document

> A comprehensive technical reference for the entire Usurper Reborn codebase.

**Runtime**: .NET 8.0 LTS | **Language**: C# 12 | **License**: GPL v2

---

## Table of Contents

1. [Entry Points & Execution Modes](#1-entry-points--execution-modes)
2. [Core Game Engine](#2-core-game-engine)
3. [Character Hierarchy](#3-character-hierarchy)
4. [Equipment & Inventory](#4-equipment--inventory)
5. [Location System](#5-location-system)
6. [Dungeon System](#6-dungeon-system)
7. [Combat Engine](#7-combat-engine)
8. [Spell & Ability Systems](#8-spell--ability-systems)
9. [Monster System](#9-monster-system)
10. [NPC AI & Behavior](#10-npc-ai--behavior)
11. [World Simulator](#11-world-simulator)
12. [NPC Lifecycle (Children, Aging, Death)](#12-npc-lifecycle)
13. [Save System & Serialization](#13-save-system--serialization)
14. [Online Multiplayer](#14-online-multiplayer)
15. [Story & Narrative Systems](#15-story--narrative-systems)
16. [Quest System](#16-quest-system)
17. [Companion System](#17-companion-system)
18. [Relationship & Family Systems](#18-relationship--family-systems)
19. [Achievement & Statistics](#19-achievement--statistics)
20. [Terminal & UI Layer](#20-terminal--ui-layer)
21. [BBS Door Mode](#21-bbs-door-mode)
22. [Website & Web Proxy](#22-website--web-proxy)
23. [Server Infrastructure](#23-server-infrastructure)
24. [CI/CD Pipeline](#24-cicd-pipeline)
25. [Configuration & Constants](#25-configuration--constants)
26. [Enums Reference](#26-enums-reference)

---

## 1. Entry Points & Execution Modes

**File**: `Console/Bootstrap/Program.cs`

The game has four distinct execution modes:

### Console Mode (Default)
Standard local play. Initializes Steam integration if available, runs `GameEngine.RunConsoleAsync()` → splash screen → version check → main menu → save select → game loop.

### BBS Door Mode (`--door`, `--door32`, `--doorsys`, `--node`)
Runs as a BBS door game. Parses DOOR32.SYS/DOOR.SYS drop files, sets up `BBSTerminalAdapter` for socket/stdio I/O, runs `GameEngine.RunBBSDoorMode()` (auto-loads player by BBS username, skips menus).

### Online Multiplayer Mode (`--online`)
SSH-based multiplayer. Initializes `SqlSaveBackend` (SQLite), shows `OnlineAuthScreen` for login/register, starts presence tracking via `OnlineStateManager`, then runs `GameEngine.RunBBSDoorMode()`.

### World Simulator Mode (`--worldsim`)
Headless 24/7 NPC simulation service. No terminal, no auth. Loads NPC state from SQLite, runs continuous simulation ticks, saves state periodically. Managed by systemd.

### Command-Line Flags
```
--door <path>          Auto-detect drop file type
--door32 <path>        Explicit DOOR32.SYS
--doorsys <path>       Explicit DOOR.SYS
--node <dir>           Search directory for drop files
--local                Local testing mode (no BBS)
--stdio                Standard I/O mode (Synchronet, SSH)
--fossil <port>        FOSSIL/serial mode on COM port
--verbose / -v         Detailed debug output
--sysop-level <N>      SysOp security level threshold (default: 100)
--online               Online multiplayer mode
--user <username>      Pre-set online username (skip auth)
--db <path>            SQLite database path
--worldsim             Headless world simulator mode
--sim-interval <sec>   Simulation tick interval (default: 60)
--npc-xp <mult>        NPC XP multiplier (default: 0.25)
--save-interval <min>  Save interval for world sim (default: 5)
```

---

## 2. Core Game Engine

**File**: `Scripts/Core/GameEngine.cs`
**Pattern**: Thread-safe singleton via `Lazy<GameEngine>`

### Key Properties
```csharp
public static GameEngine Instance { get; }
public Character? CurrentPlayer { get; set; }
public TerminalEmulator Terminal { get; }
public LocationManager locationManager;
public DailySystemManager dailyManager;
public CombatEngine combatEngine;
public WorldSimulator worldSimulator;
public List<string> DungeonPartyNPCIds { get; }
```

### Game Flow

**Console Mode**: `RunMainGameLoop()` → splash → version check → `MainMenu()` → load/create → game loop
**BBS/Online Mode**: `RunBBSDoorMode()` → auto-load by username → game loop

### Initialization
- `InitializeGame()` → reads config, initializes LocationManager, items, monsters, NPCs, levels, guards
- `InitializeItems()` / `InitializeMonsters()` / `InitializeNPCs()` / `InitializeLevels()` / `InitializeGuards()`
- `CreateNewGame(playerName)` → character creation → starts game
- `LoadSaveByFileName(filename)` → loads saved character → starts game

### Periodic Updates
`PeriodicUpdate()` is called every 30 seconds during gameplay:
- Daily reset check (via `DailySystemManager`)
- World simulation tick
- NPC maintenance

### Save/Load Integration
- `RestoreNPCs()` restores full NPC state including AI memory, goals, emotions, lifecycle fields
- Legacy migration: NPCs without `BirthDate` get random age 18-50 and computed `BirthDate`
- `ShowWhileYouWereGone()` summarizes events since last logout (online mode)

---

## 3. Character Hierarchy

```
Character (base class)
├── Player (the player character)
├── NPC (town NPCs, romance interests, enemies)
└── Monster (dungeon creatures - standalone, not extending Character)
```

### Character (Base Class)
**File**: `Scripts/Core/Character.cs`

**Core Stats** (Pascal `UserRec` compatible):
- Identity: `Name1`, `Name2`, `Race`, `Class`, `Level`, `Age`, `Sex`
- Resources: `HP`, `MaxHP`, `Mana`, `MaxMana`, `Gold`, `BankGold`, `Experience`
- Attributes: `Strength`, `Defence`, `Stamina`, `Agility`, `Charisma`, `Dexterity`, `Wisdom`, `Intelligence`, `Constitution`
- Combat: `WeapPow`, `ArmPow`, `Punch`, `Absorb`
- AI: `CharacterAI AI` (Computer/Human/Civilian)

**Equipment** (dual system):
- Legacy: `LHand`, `RHand`, `Head`, `Body`, `Arms`, `Legs`, `Feet`, `Waist`, `Neck`, `Face`, `Shield`, `Hands`, `ABody` (int item IDs)
- Modern: `Dictionary<EquipmentSlot, int> EquippedItems`
- Computed: `IsDualWielding`, `HasShieldEquipped`, `IsTwoHanding`

**Spells & Abilities**:
- `List<List<bool>> Spell` (2D: [spell index][known/mastered])
- `HashSet<string> LearnedAbilities` (non-caster class abilities)
- `List<int> Skill` (close combat skills)

**Status Effects**: `Blind`, `Plague`, `Smallpox`, `Measles`, `Leprosy`, `LoversBane`, `Poison`, `Mental`, `Addict`

**Combat Stamina** (resource for abilities):
```
MaxCombatStamina = 50 + (Stamina * 2) + (Level * 3)
RegenPerRound = 5 + (Stamina / 10)
```

**Key Methods**: `EquipItem()`, `UnequipSlot()`, `RecalculateStats()`, `InitializeBaseStats()`, `InitializeCombatStamina()`, `RegenerateCombatStamina()`

### Player
**File**: `Scripts/Core/Player.cs`

Extends Character with: `RealName`, `LastLogin`, `TotalLogins`, `TotalPlayTime`, `PvPWins`, `PvPLosses`, `DungeonLevel`, `IsOnline`, `UnlockedAbilities`, `PlayerStatistics`, `PlayerAchievements`, `Preferences` (CombatSpeed, AutoHeal, SkipIntimateScenes, ScreenReaderMode).

### NPC
**File**: `Scripts/Core/NPC.cs`

Extends Character with:
- **AI**: `NPCBrain Brain`, `PersonalityProfile Personality`, `MemorySystem Memory`, `EmotionalState EmotionalState`, `GoalSystem Goals`
- **Social**: `Archetype`, `StoryRole`, `Faction`, `GangId`, `KnownCharacters`, `Enemies`, `GangMembers`
- **Death**: `IsDead` (permanent flag, separate from `IsAlive` which is `HP > 0`)
- **Lifecycle**: `BirthDate`, `IsAgedDeath`, `PregnancyDueDate`
- **Marriage**: `IsMarried`, `Married`, `SpouseName`, `MarriedTimes`
- **Commerce**: `MarketInventory` (items for sale)

---

## 4. Equipment & Inventory

### Legacy Item System
**File**: `Scripts/Core/Items.cs`

Pascal-compatible items with `ObjType` enum (Head=1, Body=2, ... Potion=17). Properties include `Name`, `Value`, `HP`, `Strength`, `Defence`, `Armor`, `Attack`, `Cursed`, `OnlyOne`, `Cure`, `Shop`, `Dungeon`, `MinLevel`, `MaxLevel`, `ClassRestrictions`.

### Modern Equipment System
**File**: `Scripts/Core/Equipment.cs`

Full RPG equipment with:
- **15 Slots**: Head, Body, Arms, Hands, Legs, Feet, Waist, Neck, Neck2, Face, Cloak, LFinger, RFinger, MainHand, OffHand
- **Rarity**: Common, Uncommon, Rare, Epic, Legendary, Artifact
- **Weapon Types**: Sword, Axe, Mace, Dagger, Rapier, Staff, Bow, Crossbow, Spear, Greatsword
- **Weapon Handedness**: OneHanded, TwoHanded, OffHandOnly
- **Armor Types**: Cloth, Leather, Chain, Scale, Plate, Magic, Artifact
- **Level Requirements**: Epic requires 45+, Legendary requires 65+

Both systems coexist. Legacy items use integer IDs stored in slot properties; modern equipment uses `EquippedItems` dictionary.

---

## 5. Location System

**File**: `Scripts/Locations/BaseLocation.cs` (abstract base)
**File**: `Scripts/Systems/LocationManager.cs` (singleton router)

### BaseLocation Abstract Class

All locations implement:
- `EnterLocation(player, terminal)` → setup, presence tracking, enter loop
- `LocationLoop()` → display → input → process → auto-save → turn increment → world sim every 5 turns
- `DisplayLocation()` → render location screen
- `ProcessChoice(string choice)` → handle menu selection (return true to exit)
- `TryProcessGlobalCommand(input)` → handle universal commands

### Global Commands (Available Everywhere)
| Command | Aliases | Function |
|---------|---------|----------|
| `/stats` | `/s` | Character statistics |
| `/inventory` | `/i`, `*` | Inventory screen |
| `/quests` | `/q` | Active quests |
| `/time` | `/t` | Remaining turns |
| `/gold` | `/g` | Gold and bank balance |
| `/health` | `/hp` | HP/MP with percentages |
| `/map` | `/m` | Town location overview |
| `/prefs` | `/p` | Preferences menu |
| `/help` | `/?`, `H` | Available commands |

### All Locations (31 Total)

**Main Hub**: MainStreet (1)
**Commerce**: WeaponShop (5), ArmorShop (18), MagicShop (7), AdvancedMagicShop (32), Bank (19), Marketplace (22)
**Social**: Inn (2), DarkAlley (3), LoveStreet (200), LoveCorner (77), TeamCorner (41)
**Religious**: Church (4), Temple (47)
**Services**: Healer (21), LevelMaster (6)
**Combat**: Dungeons (8), Arena/PvP (501)
**Housing**: Home (201)
**Governance**: Castle (70), Prison (90), PrisonWalk (94)
**Quests**: QuestHall (75), News (20)
**Admin**: SysOpConsole (500), DevMenu
**Other**: AnchorRoad (27), Dormitory (26), CharacterCreation, GodWorld (400)

### Navigation
LocationManager maintains a navigation graph (dictionary of location → list of accessible exits). Each location declares its possible exits. Location transitions go through `GameEngine.NavigateToLocation(GameLocation)`.

---

## 6. Dungeon System

**File**: `Scripts/Locations/DungeonLocation.cs` (~6000+ lines, largest file)

### Structure
- 100 procedurally generated floors with **deterministic seeding**: `new Random(level * 31337 + 42)` ensures consistent layouts
- Floor themes: Underground, Cavern, Temple, Ancient, Corrupted, Crystalline
- Room types: MonsterRoom, TreasureChest, Shrine, LoreLibrary, Trap, SecretVault, MeditationChamber, Guardian
- Floor state persisted in `player.DungeonFloorStates` dictionary

### Access Restrictions
```csharp
int minAccessible = Math.Max(1, playerLevel - 10);
int maxAccessible = Math.Min(maxDungeonLevel, playerLevel + 10);
```

### Special Floors

**Seal Floors** (6): Floors 15, 30, 45, 60, 80, 99 — contain collectible Seals of Truth
**Old God Floors** (7): Floors 25, 40, 55, 70, 85, 95, 100 — boss encounters

| Floor | God | Domain | Artifact |
|-------|-----|--------|----------|
| 25 | Maelketh | War & Conquest | Creator's Eye |
| 40 | Veloura | Love & Passion | Soulweaver's Loom |
| 55 | Thorgrim | Law & Order | Scales of Law |
| 70 | Noctura | Shadow & Secrets | Shadow Crown |
| 85 | Aurelion | Light & Truth | Sunforged Blade |
| 95 | Terravok | Earth & Endurance | Worldstone |
| 100 | Manwe | Creation & Balance | Void Key |

### Floor Locking
Special floors lock the player until cleared:
- Old God floors: must defeat/save the Old God
- Seal floors: must collect the seal
- `BossDefeated` flag tracks whether the floor boss was actually defeated (not just any monster)

### Teammate System
Companions, spouses, lovers, and team members can join the dungeon party. Team combat grants +15% XP/gold bonus. Party cleared at dungeon entry to prevent save/load leaks.

### Dungeon Reset Scroll
Purchasable at Magic Shop (1000 + level×200 gold). Instantly respawns monsters on a cleared floor.

---

## 7. Combat Engine

**File**: `Scripts/Systems/CombatEngine.cs`

### Combat Modes
- `PlayerVsMonsters()` — primary single/multi-monster combat
- `PlayerVsPlayer()` — PvP combat (arena or duels)
- Team combat with companions/NPCs

### Combat Flow
```
1. Initialize (reset flags, stamina, show intro)
2. LOOP while player alive && monsters remain:
   a. Display combat status
   b. Get player action
   c. Process player action
   d. Each surviving monster attacks
   e. Each teammate acts
   f. Check end conditions
3. Determine outcome (victory/defeat/fled)
```

### Combat Actions
Attack, Defend (+50% defense), Heal (potion), CastSpell, UseAbility, Backstab, Retreat (flee), PowerAttack, PreciseStrike, Rage (Barbarian), Smite, Disarm, Taunt, Hide, RangedAttack, HealAlly, BegForMercy, Status

### Damage Formula
```
attackPower = Strength + StrengthBonus + Level*2 + WeapPow + Random(WeapPow) + Random(1, Level/2)
Modifiers: Raging ×1.5, TwoHanded ×1.3, DualWield offhand ×0.75, Blessed, Weakened, Grief, Divine
defense = monster.Defence + Random(Defence/8) + ArmPow + Random(ArmPow)
damage = max(1, attackPower - defense)
```

### Hit Determination (D20 System)
```
monsterAC = 10 + (Level/5) + (Defence/20) + situational modifiers
attackRoll via TrainingSystem.RollAttack()
Natural 20 = critical hit, Natural 1 = critical miss
```

### Caps & Limits (Anti-Exploit)
| Mechanic | Cap | Reason |
|----------|-----|--------|
| Backstab success | 75% | Prevent guaranteed exploit |
| Mercy escape | 75% | Prevent guaranteed escape |
| Critical hit chance | 5-50% | Balanced variance |
| Flee chance | 75% | Risk/reward for escape |
| Boss HP multiplier | 2.0x | Challenging encounters |
| Mini-boss multiplier | 1.5x | Elite encounters |

### Victory Rewards
```
Base XP/Gold from monster → world event modifiers → difficulty modifiers
Bonuses: Spouse (+10%), Divine blessing, Children (10-30%), Team (+15%)
Companions get 50% of XP, NPC teammates get 50% of XP
```

### PvP Combat
Attacker vs AI-controlled defender (loaded from save at full HP). Same combat engine. Death handled in-Arena with PvP-specific penalties (10% gold stolen, 10% XP loss, 25% gold penalty, resurrect at Inn). Gold theft is bidirectional and atomic via SQL.

### Monster Targeting Intelligence
Monsters use weighted targeting: Paladin=180, Barbarian=170, Warrior=160 (tanks draw aggro) down to Magician=60, Sage=65 (squishy casters stay back). Modifiers for armor, HP, health percentage, and defending stance.

---

## 8. Spell & Ability Systems

### Spells
**File**: `Scripts/Data/SpellDatabase.cs`, `Scripts/Systems/SpellSystem.cs`

Three spellcasting classes with **25 spells each** spread across levels 1-100:

**Cleric (Divine Magic)**: Cure Light → Cure Wounds → Holy Smite → Restoration → Aurelion's Radiance → God's Finger
**Magician (Arcane Magic)**: Magic Missile → Fireball → Lightning Bolt → Disintegrate → Meteor Swarm → Wish
**Sage (Mind/Shadow Magic)**: Mind Spike → Steal Life → Energy Drain → Shadow Step → Temporal Paradox → Death Kiss

**Mana Cost**: `baseCost = 10 + (spellLevel * 5)`, reduced by Wisdom (max 50%), halved by Ocean's Memory spell.

### Class Abilities
**File**: `Scripts/Systems/ClassAbilitySystem.cs`

~10 abilities per class spread across levels 1-100, costing Combat Stamina:

**Warrior**: Power Strike → Shield Wall → Battle Cry → Execute → Whirlwind → Champion's Strike
**Barbarian**: Berserker Rage → Reckless Attack → Intimidating Roar → Bloodlust (5 rounds) → Frenzy → Avatar of Destruction
**Paladin**: Lay on Hands → Divine Smite → Aura of Protection → Holy Avenger → Light's Vengeance → Avatar of Light
**Assassin**: Backstab → Evasion → Poison Strike → Shadow Clone → Assassinate → Nightblade Dance
**Ranger**: Multi-Shot → Aimed Shot → Barrage → Beast Companion → Volley → Master of Bow

---

## 9. Monster System

**Files**: `Scripts/Core/Monster.cs`, `Scripts/Data/MonsterFamilies.cs`, `Scripts/Systems/MonsterGenerator.cs`

### Monster Families (10 families × 5 tiers = 50 unique types)

| Family | Tiers (Low → High) | Attack Type |
|--------|---------------------|-------------|
| Goblinoid | Goblin → Hobgoblin → Champion → Warlord → King | Physical |
| Undead | Zombie → Ghoul → Wight → Wraith → Lich | Necrotic |
| Orcish | Orc → Warrior → Berserker → Chieftain → Warlord | Physical |
| Draconic | Kobold → Drake → Wyvern → Young Dragon → Ancient Dragon | Fire |
| Demonic | Imp → Demon → Greater Demon → Demon Lord → Archfiend | Fire |
| Giant | Ogre → Troll → Hill Giant → Stone Giant → Titan | Physical |
| Beast | Wolf → Dire Wolf → Werewolf → Alpha → Fenrir | Physical |
| Elemental | Spark → Fire Elemental → Inferno → Fire Lord → Phoenix | Fire |
| Aberration | Ooze → Slime → Gelatinous Cube → Elder Ooze → Shoggoth | Acid |
| Insectoid | (Additional family) | Various |

### Stat Generation
```
HP = (25*level + level^1.1 * 8) × powerMultiplier × bossMultiplier
Strength = (2*level + level^1.05 * 1.5) × multiplier
Defence = (level + level^1.02 * 0.5) × multiplier × 0.5
```

### Group Encounters
10% chance single mini-boss, 70% same-family thematic encounter, 30% mixed families. Group size scales: 1-2 (levels 1-10), 1-3 (11-30), 2-4 (31-60), 3-5 (61-100).

### Boss vs Mini-Boss
- **IsBoss** = true: Only for actual floor bosses in boss rooms (2.0x multiplier)
- **IsMiniBoss** = true: Champions, guardians, mimics (1.5x multiplier, +15% attack, +10% defense, 1.5x XP/gold)

---

## 10. NPC AI & Behavior

### NPCBrain
**File**: `Scripts/AI/NPCBrain.cs`

Goal-based AI with 15-minute decision cooldown:
```
1. Process enhanced behaviors
2. Update emotions from recent memory
3. Decay old memories
4. Re-evaluate goals
5. Get priority goal → generate actions → score → select best
```

### Memory System
**File**: `Scripts/AI/MemorySystem.cs`

- Max 100 memories per NPC, decay after 7 days
- Memory types with impression impact: Attacked (-0.8), Betrayed (-0.9), Helped (+0.4), Saved (+0.8)
- Character impressions tracked as floating-point values (-1.0 to +1.0)
- Query methods: `GetMemoriesAboutCharacter()`, `GetCharacterImpression()`, `HasMemoryOfEvent()`

### Enhanced NPC Behaviors
**File**: `Scripts/AI/EnhancedNPCBehaviors.cs`

Phase 21 processing per simulation tick:
1. **Inventory Management** — evaluate new items, swap if 20% better
2. **Shopping AI** — class-based purchasing (Warriors buy weapons, Magicians buy mana potions)
3. **Gang Management** — dissolve small gangs, recruit members (skip player teams)
4. **Relationships** — 2% marriage chance per tick (level 5+), friendship development
5. **Gang Warfare** — automated battles between rival gangs

### Personality System
13 core personality traits (0.0-1.0): Aggression, Loyalty, Intelligence, Greed, Compassion, Courage, Honesty, Ambition, Vengefulness, Impulsiveness, Caution, Mysticism, Patience.

10 romance traits: Romanticism, Flirtatiousness, Commitment, attraction preferences, gender preferences, relationship style.

---

## 11. World Simulator

**File**: `Scripts/Systems/WorldSimulator.cs` (in-game), `Scripts/Systems/WorldSimService.cs` (headless)

### Simulation Tick (every 30 seconds)
```
1. ProcessNPCRespawns()         — respawn dead NPCs (20 ticks = ~10 min)
2. FamilySystem.ProcessDailyAging()  — age children, convert to NPCs at 18
3. ProcessNPCAging()            — age NPCs, check natural death
4. ProcessNPCPregnancies()      — check births, start new pregnancies
5. ProcessNPCDivorces()         — personality-driven divorce chance
6. For each alive NPC:
   a. Brain.DecideNextAction()  — AI decision
   b. ExecuteNPCAction()        — carry out decision
   c. ProcessNPCActivities()    — random daily activities
   d. ProcessNPCRelationships() — marriages, friendships
7. Track dead NPCs for respawn
8. ProcessWorldEvents()
9. UpdateSocialDynamics()
```

### Headless Mode (WorldSimService)
Runs as systemd service. Initializes minimal systems (no terminal, no auth). Routes NPC news to SQLite via `NewsSystem.DatabaseCallback`. Saves world state every 5 minutes (NPCs, royal court, children, marriages, world events, economy). Prunes old NPC activity entries (24-hour retention). Independent version tracking for NPCs and royal court prevents stale overwrites from player sessions.

### Respawn System
```csharp
QueueNPCForRespawn(npcName, ticks)  // Called on NPC death
ProcessNPCRespawns()                 // Decrements timers, respawns at 0
// IsAgedDeath NPCs are NEVER respawned
```

---

## 12. NPC Lifecycle

### Aging
- `NpcLifecycleHoursPerYear = 9.6` — ~1 game-year every 9.6 real hours
- Age computed from `BirthDate`: `(DateTime.Now - BirthDate).TotalHours / 9.6`
- Race lifespans: Human 75 (~30 days), Elf 200 (~80 days), Orc 55 (~22 days), Gnoll 50 (~20 days)

### Pregnancies
- 1% per tick for eligible married females (age 18-45)
- Dynamic rate: 3% if underpopulated (<40), 0.5% if overpopulated (>80)
- Gestation: ~7 real hours
- Max 4 children per couple
- Same-sex couples: no natural pregnancy

### Children
Born via `FamilySystem.CreateNPCChild()`, age using accelerated lifecycle rate, auto-convert to full NPCs at age 18 via `ConvertChildToNPC()`. 80 fantasy names (40 male, 40 female) with Roman numeral suffixes for duplicates.

### Natural Death
When NPC exceeds race lifespan: `IsAgedDeath = true` → permanent, no respawn. Widowed spouse gets marriage cleared and bereavement memory. Story NPCs exempt.

### Divorce
0.3% base chance per tick. Modifiers: low commitment +0.5%, high flirtatiousness +0.3%, alignment mismatch +0.4%, both high-commitment −80%.

### Affairs
Flirtatious married NPCs (>0.6) have 15% chance of conceiving with someone other than spouse. `_pregnancyFathers` dictionary tracks actual father.

### Polyamory
NPCs with `RelationshipPreference.Polyamorous` or `OpenRelationship` can seek partners while married. Both parties must be poly/open.

---

## 13. Save System & Serialization

**Files**: `Scripts/Systems/SaveSystem.cs`, `Scripts/Systems/SaveDataStructures.cs`

### Architecture
Pluggable backend: `ISaveBackend` interface with `FileSaveBackend` (local JSON) and `SqlSaveBackend` (online SQLite). Selected via `SaveSystem.InitializeWithBackend()`.

### Save Data Structure
```csharp
SaveGameData {
    int Version;
    DateTime SaveTime, LastDailyReset;
    int CurrentDay;
    PlayerData Player;           // 100+ properties
    List<NPCData> NPCs;         // Full NPC state including AI
    WorldStateData WorldState;   // Economy, ruler, events, shops
    DailySettings Settings;      // Reset config
    StorySystemsData StorySystems; // All narrative systems
}
```

### PlayerData (100+ fields)
Identity, stats, attributes, equipment (legacy + modern), inventory, status effects, game state, daily limits, home upgrades, dungeon progression, preferences, statistics, achievements.

### NPCData
Identity, stats, class/race, lifecycle (Age, BirthDate, IsAgedDeath, PregnancyDueDate), marriage state, faction, AI state (personality 13 core traits + 10 romance traits, memories, goals, emotions), inventory, equipment.

### StorySystemsData
Ocean Philosophy (awakening, fragments), Seven Seals, Story Progression (chapters, flags, god states, cycle), Companions (active, fallen, quests), Grief (stages, memories), Family (children), Relationships (bidirectional values), NPC Marriages, Royal Court (members, heirs, plots), Factions, Town NPC Stories, Dreams, Stranger Encounters, God Worship.

### Serialization Format
JSON via `System.Text.Json` with `camelCase` naming policy.

---

## 14. Online Multiplayer

### SQLite Schema

**players** — Primary accounts and saves
```sql
CREATE TABLE players (
    username TEXT PRIMARY KEY,        -- Normalized lowercase
    display_name TEXT NOT NULL,
    password_hash TEXT NOT NULL,      -- PBKDF2-SHA256, 100k iterations
    player_data TEXT NOT NULL,        -- Complete SaveGameData as JSON
    created_at TEXT, last_login TEXT, last_logout TEXT,
    total_playtime_minutes INTEGER, is_banned INTEGER, ban_reason TEXT
);
```

**world_state** — Shared multiplayer world (keys: npcs, king, royal_court, world_events, quests, marketplace, story_systems, daily_state, marriages, children, economy)
```sql
CREATE TABLE world_state (
    key TEXT PRIMARY KEY, value TEXT NOT NULL,
    version INTEGER DEFAULT 1, updated_at TEXT, updated_by TEXT
);
```

**online_players** — Live presence tracking (120-second heartbeat window)
```sql
CREATE TABLE online_players (
    username TEXT PRIMARY KEY, display_name TEXT, location TEXT,
    node_id TEXT, connection_type TEXT, connected_at TEXT, last_heartbeat TEXT
);
```

**messages** — Inter-player messaging (broadcast `to_player='*'` uses ID watermark, direct uses `is_read` flag)
```sql
CREATE TABLE messages (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    from_player TEXT, to_player TEXT, message_type TEXT, message TEXT,
    is_read INTEGER DEFAULT 0, created_at TEXT
);
```

**news** — Shared game events (categories: boss_killed, level_up, marriage, npc, etc.)
```sql
CREATE TABLE news (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    message TEXT, category TEXT, player_name TEXT, created_at TEXT
);
```

**pvp_log** — PvP combat history
```sql
CREATE TABLE pvp_log (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    attacker TEXT, defender TEXT, attacker_level INT, defender_level INT,
    winner TEXT, gold_stolen INT, xp_gained INT, attacker_hp_remaining INT,
    rounds INT, created_at TEXT
);
```

### Authentication
PBKDF2-SHA256 with 100k iterations, 16-byte salt, constant-time comparison. Username 2-20 chars alphanumeric. Password minimum 4 chars.

### Key Classes
- `OnlineAuthScreen` — Login/register UI
- `SqlSaveBackend` — All SQLite operations, atomic gold operations via `json_set()`
- `OnlineStateManager` — Presence tracking, world state coordination, message watermarks
- `OnlineChatSystem` — `/say`, `/tell`, `/who`, `/news` commands

### PvP Daily Limits
Tracked via `pvp_log` table: `created_at >= date('now')` (resets at midnight UTC).

---

## 15. Story & Narrative Systems

### Story Progression
**File**: `Scripts/Systems/StoryProgressionSystem.cs`

**11 chapters**: Awakening → FirstBlood → TheStranger → TheFirstSeal → FactionChoice → RisingPower → TheWhispers → FirstGod → GodWar → TheChoice → Ascension → FinalConfrontation → Epilogue

**6 acts**: The Newcomer, Rising Power, The Awakening, The Corruption, The Ascension, The Final

**22+ story flags** (bitmask): MetStranger, JoinedChurch, BecameKing, AllSealsCollected, KnowsNocturaTruth, SavedVeloura, etc.

**Old God States**: Unknown → Imprisoned/Dormant/Dying/Corrupted/Neutral → Awakened/Hostile/Allied → Saved/Defeated/Consumed

### Endings System
**File**: `Scripts/Systems/EndingsSystem.cs`

5 endings:
1. **Usurper** (Dark) — Alignment < -300 or destroyed 5+ gods → consume all, become sole god
2. **Savior** (Light) — Alignment > 300 or saved 3+ gods → restore pantheon, join as equal
3. **Defiant** (Independent) — Balanced alignment → kill all gods, stay mortal
4. **True Ending** — All 7 seals + Awakening 7 + companion grief + spared 2+ gods + balanced alignment → become "The Bridge"
5. **Dissolution** (Secret) — Cycle 3+ + completed 2+ endings + max awakening + truth revealed + all wave fragments → dissolve into Ocean, save deleted

### Ocean Philosophy
**File**: `Scripts/Systems/OceanPhilosophySystem.cs`

8 awakening levels (0-7), 10 wave fragments, 13 awakening moments. Core theme: "You are not a wave fighting the ocean. You ARE the ocean, dreaming of being a wave."

### Seven Seals
**File**: `Scripts/Systems/SevenSealsSystem.cs`

7 seals on floors 0(town), 15, 30, 45, 60, 80, 99. Each reveals lore about Manwe and the gods. Collecting all 7 + max awakening unlocks True Ending.

### Other Narrative Systems
- **DreamSystem** — 20+ dreams triggered during Inn rest, scale with awakening level
- **StrangerEncounterSystem** — 10 Noctura disguises, escalating encounters
- **TownNPCStorySystem** — 6 NPCs with 4-5 story stages each (Marcus, Elena, Aldric, Merchant's Daughter, Old Adventurer, Pip)
- **CycleDialogueSystem** — NG+ aware dialogue (Cycle 1: normal, Cycle 2: deja vu, Cycle 5+: full acknowledgment)

---

## 16. Quest System

**File**: `Scripts/Systems/QuestSystem.cs`

### Quest Types
**Combat**: KillMonsters, KillSpecificMonster, KillBoss
**Dungeon**: ReachDungeonFloor, ClearDungeonFloor, FindArtifact, ExploreRooms
**Equipment**: BuyWeapon, BuyArmor, BuyAccessory, BuyShield
**Social**: TalkToNPC, DeliverItem, Assassinate, Seduce, DefeatNPC
**Collection**: CollectGold, CollectItems, CollectPotions

### Quest Difficulty (1-4)
Adjusts target monster level: `playerLevel + (difficulty - 2) * 3`, capped to `playerLevel ± 10`.

### Monster Selection
Uses `MonsterFamilies.GetMonsterForLevel()` for level-appropriate selection. Dungeon objectives cap floors to player's accessible range.

---

## 17. Companion System

**File**: `Scripts/Systems/CompanionSystem.cs`

### Four Companions

| Companion | Type | Recruit Lvl | Role | Can Die | Personal Quest |
|-----------|------|-------------|------|---------|----------------|
| **Lyris** | Romance | 15 | Hybrid | Yes | Recover artifact for Aurelion |
| **Aldric** | Combat | 10 | Tank | Yes (moral trigger) | Confront demon from past |
| **Mira** | Support | 20 | Healer | Yes (inevitable) | Accept meaning of mercy |
| **Vex** | Utility | 25 | Damage | Yes (disease, ~30 days) | Complete before-I-die list |

Mira's death is narratively inevitable (teaches letting go). Vex has a wasting disease with ~30-day timer. Both deaths serve the game's themes of loss and acceptance.

Companion death triggers the Grief System, which applies combat modifiers and contributes to Ocean Philosophy awakening.

---

## 18. Relationship & Family Systems

### Relationships
**File**: `Scripts/Systems/RelationshipSystem.cs`

Bidirectional relationship tracking (Relation1/Relation2). Levels: Hated → Normal → Friendship (~40) → Married (~200).

**Rebalanced in v0.26**:
- Daily cap: max 2 relationship steps per NPC per day
- Minimum 7 days acquaintance before marriage proposal
- NPC proposal acceptance based on personality (20-95% chance)
- Intimacy: 3 steps per encounter (was 10)

### Family
**File**: `Scripts/Systems/FamilySystem.cs`

Children provide stat bonuses: +2% XP per child, +50 HP, +5 STR, +3 CHA, +100 daily gold. Royal children add +5 CHA and +500 gold.

### Factions
**File**: `Scripts/Systems/FactionSystem.cs`

Three factions:
- **The Crown** (Castle): 10% shop discount, requires Chivalry > 500
- **The Shadows** (Dark Alley): 20% better fence prices, requires Darkness > 200
- **The Faith** (Temple): 25% healing discount

### Castle Politics
**File**: `Scripts/Locations/CastleLocation.cs`, `Scripts/Core/King.cs`

Full political system: guard loyalty, treasury management, court factions (Loyalists, Reformists, Militarists, Merchants, Faithful), intrigue plots (assassination, coup, scandal, sabotage), political marriage, succession/heir system.

---

## 19. Achievement & Statistics

### Achievements
**File**: `Scripts/Systems/AchievementSystem.cs`

34+ achievements across 7 categories: Combat, Exploration, Economy, Social, Progression, Challenge, Secret.

5 tiers: Bronze, Silver, Gold, Platinum, Diamond.

Notable: `first_blood`, `monster_slayer_1000`, `boss_killer`, `level_100`, `married`, `ruler`, `pvp_veteran`, `nightmare_master`, `completionist`.

### Statistics
**File**: `Scripts/Systems/StatisticsSystem.cs`

Tracked via `PlayerStatistics` methods:
```csharp
RecordMonsterKill(), RecordDamageDealt(), RecordDamageTaken(),
RecordPurchase(), RecordGoldSpent(), RecordSale(),
RecordGoldChange(), RecordLevelUp(), RecordDungeonLevel(),
RecordPotionUsed(), RecordHealthRestored(), RecordResurrection(), RecordDiseaseCured()
```

Session summary on logout shows duration, combat stats, progress, economy, exploration.

---

## 20. Terminal & UI Layer

### TerminalEmulator
**File**: `Scripts/UI/TerminalEmulator.cs`

Four output modes (auto-detected):
1. **Godot Mode** — RichTextLabel with BBCode colors (when `display != null`)
2. **BBS Socket Mode** — Routes through `BBSTerminalAdapter` → `SocketTerminal`
3. **ANSI Mode** — ANSI escape codes when `DoorMode.ShouldUseAnsiOutput` is true
4. **Console Fallback** — `Console.ForegroundColor`

**Color system**: 30+ named colors (black, white, red, green, yellow, blue, cyan, magenta + bright_* and dark_* variants). Inline markup: `[bright_red]text[/]`.

**Input**: `GetInput()`, `ReadLineAsync()`, `PressAnyKey()`. Custom `ReadLineWithBackspace()` handles SSH backspace bytes (0x7F/0x08) when stdin is redirected.

### UIHelper
**File**: `Scripts/UI/UIHelper.cs`

Static utility for consistent 80-char-wide box drawing:
```
╔══════════════════════════════════════════════════════════════════════════════╗
║  Content with padding                                                      ║
╠══════════════════════════════════════════════════════════════════════════════╣
║  [K] Menu Option                                                           ║
╚══════════════════════════════════════════════════════════════════════════════╝
```

Methods: `DrawBoxTop/Bottom/Line/Separator()`, `DrawMenuOption()`, `DrawStatBar()`, `DrawBoxLabelValue()`, `FormatNumber()`, `FormatGold()`.

---

## 21. BBS Door Mode

**Files**: `Scripts/BBS/DoorMode.cs`, `Scripts/BBS/SocketTerminal.cs`, `Scripts/BBS/BBSTerminalAdapter.cs`, `Scripts/BBS/DropFileParser.cs`

### I/O Modes
1. **Socket I/O** (default): Direct TCP socket from DOOR32.SYS handle. Works for unencrypted telnet.
2. **Standard I/O** (`--stdio`): stdin/stdout with ANSI codes. Required for SSH, Synchronet, GameSrv, ENiGMA, WWIV.
3. **FOSSIL/Serial** (`--fossil`): COM port for legacy BBS software.

### Auto-Detection
BBS software auto-detected from DOOR32.SYS: Synchronet, GameSrv, ENiGMA, WWIV → auto-enable stdio mode. Console.IsInputRedirected/IsOutputRedirected catches SSH/pipe-based transports.

### Drop File Parsing
`BBSSessionInfo`: CommType, SocketHandle, UserName, SecurityLevel, TimeLeft, BBSName, Emulation, ScreenWidth/Height.

### Save Isolation
BBS saves stored in `saves/{BBSName}/` to prevent conflicts across different BBSes.

### Critical SSH Pitfall
BBS passes raw TCP socket handle for BOTH telnet and SSH. For SSH, writing to raw socket bypasses encryption → "Bad packet length" errors. Fix: detect redirected I/O and use stdio mode.

---

## 22. Website & Web Proxy

### Landing Page
**File**: `web/index.html`

Dark-themed responsive page with:
- Embedded xterm.js 5.5.0 web terminal (WebSocket → SSH bridge)
- Live stats dashboard (fetches `/api/stats` every 30s)
- SSE live feed (`/api/feed`) for real-time NPC activity and player news
- PvP leaderboard, Hall of Fame, event color coding
- Game lore sections: story, Old Gods, classes, races, companions, endings, BBS history

### Web Proxy
**File**: `web/ssh-proxy.js`

Node.js server (port 3000) with:
- **HTTP** serving `/api/stats` (SQLite queries, 30s cache) and `/api/feed` (SSE)
- **WebSocket** bridge: browser → SSH to localhost:4000 (game server)
- **Dependencies**: ws, ssh2, better-sqlite3

### Stats API Response (`GET /api/stats`)
```json
{
  "online": [...],           // Current players with connection type
  "onlineCount": N,
  "stats": { totalPlayers, totalKills, avgLevel, ... },
  "highlights": { topPlayer, king, popularClass },
  "leaderboard": [...],      // Top 25 by level/XP
  "pvpLeaderboard": [...],   // Top 10 by PvP wins
  "news": [...],             // Recent player events
  "npcActivities": [...]     // Recent NPC activities
}
```

### SSE Feed (`GET /api/feed`)
5-second polling of `news` table with high-water marks (`lastNpcId`, `lastNewsId`). Broadcasts new entries to all connected SSE clients. Auto-reconnect after 5 seconds.

### Event Color Coding
Deaths (red), births (bright pink), coming-of-age (teal), birthdays (gold), level-ups (gold-orange), natural death (purple), divorce (dark red), affairs (hot pink), polyamory (violet), marriages (pink), respawns (green).

### NPC Analytics Dashboard ("The Observatory")
**File**: `web/dashboard.html`

Hidden authenticated dashboard at `/dashboard` for observing the NPC simulation in real-time:
- **Auth**: PBKDF2 password hashing, 7-day session tokens in SQLite, HttpOnly cookies
- **World Map**: Location grid with NPC dots colored by faction, sized by level
- **NPC Detail Panel**: Personality radar (13 traits), emotional state bars, goals, memories, relationships
- **Demographics**: Class/race/faction doughnut charts, level histogram (Chart.js)
- **Relationship Network**: D3.js force-directed graph — marriage (red), team (blue), affinity (green/orange)
- **Live Event Timeline**: SSE-powered scrolling feed via `/api/dash/feed`
- **Trend Charts**: Events/hour stacked bar, population alive/dead doughnut
- **Dashboard API**: 8 endpoints under `/api/dash/` served by `ssh-proxy.js`

### Emotional State System
**File**: `Scripts/AI/EmotionalState.cs`

NPC emotional states displayed on the dashboard are derived from personality traits with antagonistic suppression:
- 12 emotion types: Happiness, Anger, Fear, Confidence, Sadness, Greed, Hope, Peace, Trust, Loneliness, Envy, Pride
- Personality-derived baselines (e.g., happiness from sociability, patience, low aggression)
- Antagonistic suppression: high anger dampens happiness/peace, high fear dampens confidence
- Transient emotions from world sim events modulate baselines by ±15%
- Serialized for dashboard via `OnlineStateManager.SerializeEmotionalStateForDashboard()`

### Tax System
**File**: `Scripts/Systems/CityControlSystem.cs`

Visible tax breakdown on every city purchase:
- King's tax (yellow) + city control team tax (cyan) displayed before confirmation
- 27 player purchase points across 6 shop files
- Tax-free zones: Dark Alley, Love Street, Church, Temple, Castle, Bank, Home, Dungeon
- NPCs pay full tax (affects their purchasing power)
- NPCs with high greed pursue city control for tax revenue
- `ProcessSaleTax()` deposits city tax to player's bank when they lead the controlling team

---

## 23. Server Infrastructure

### Architecture
```
Browser → https://usurper-reborn.net (nginx, SSL)
  ├── Static files (web/index.html)
  ├── /api/* → Node.js (port 3000) → SQLite
  └── /ws → WebSocket → SSH → game (port 4000)

Direct SSH → play.usurper-reborn.net:4000 (sshd-usurper)
```

### Nginx Reverse Proxy
**File**: `scripts-server/nginx-usurper.conf`

- Static files from `/opt/usurper/web/`
- `/api/` → proxy to `127.0.0.1:3000`
- `/ws` → WebSocket proxy with 24-hour timeout
- SSL via Let's Encrypt certbot (auto-renewing)

### Systemd Services

**usurper-web.service** — Node.js web proxy
```
ExecStart=/usr/bin/node ssh-proxy.js
User=usurper, WorkingDirectory=/opt/usurper/web
Restart=on-failure, MemoryMax=128M
```

**usurper-world.service** — Headless NPC world simulator
```
ExecStart=/opt/usurper/UsurperReborn --worldsim --sim-interval 30 --npc-xp 3.0 --save-interval 5
User=usurper, WorkingDirectory=/opt/usurper
Restart=on-failure, MemoryMax=256M, TimeoutStopSec=30
```

**sshd-usurper** — Dedicated game SSH daemon (port 4000)
- Only allows `usurper` user
- `ForceCommand /opt/usurper/UsurperReborn --online --stdio`
- Max 50 concurrent sessions

### Deployment Scripts
- `setup-server.sh` — Full server initialization (users, dirs, services, firewall, fail2ban)
- `update-server.sh` — Deploy new binary with backup and restart
- `backup.sh` — Daily SQLite backup (14-day retention, cron at 4 AM)
- `healthcheck.sh` — System health monitoring (database, players, disk)

### Server Paths
| Path | Content |
|------|---------|
| `/opt/usurper/UsurperReborn` | Game binary |
| `/opt/usurper/web/` | Website files |
| `/var/usurper/usurper_online.db` | SQLite database |
| `/var/usurper/logs/` | World sim logs |
| `/var/usurper/backups/` | Daily backups |

---

## 24. CI/CD Pipeline

**File**: `.github/workflows/ci-cd.yml`

### Jobs
1. **Test** — Build + run unit tests
2. **Build** — Publish to 6 platforms:
   - Windows x64/x86
   - Linux x64/ARM64
   - macOS Intel/Apple Silicon
3. **Smoke Tests** — Run `--help` on target platforms
4. **Release** — Attach zips to GitHub release
5. **Steam** — Build with `STEAM_BUILD` flag, extract native libraries

### Build Command
```bash
dotnet publish usurper-reloaded.csproj -c Release -r <rid> \
  -p:PublishSingleFile=true -p:SelfContained=true -p:InvariantGlobalization=true
```

### Project Configuration
**File**: `usurper-reloaded.csproj`

- Target: .NET 8.0
- NuGet: Newtonsoft.Json, Microsoft.Data.Sqlite, SSH.NET, System.IO.Ports
- Steam builds: Steamworks.NET + native libraries (steam_api64.dll, libsteam_api.so)
- Configurations: Debug, Release, Steam, SteamRelease

---

## 25. Configuration & Constants

**File**: `Scripts/Core/GameConfig.cs`

### Version
```csharp
Version = "0.29.1"
VersionName = "The Observatory"
```

### Core Constants
```csharp
MaxLevel = 200
TurnsPerDay = 325
MaxTeamMembers = 5
MaxDungeonLevel = 100
DailyDungeonFights = 10
DailyPlayerFights = 3
MaxPvPAttacksPerDay = 5
MinPvPLevel = 5
PvPGoldStealPercent = 0.10
PvPLevelRangeLimit = 20
BackstabMultiplier = 3
NpcLifecycleHoursPerYear = 9.6
```

### SysOp-Configurable
MonsterHPMultiplier, MonsterDamageMultiplier, XPMultiplier, GoldMultiplier, MaxDungeonLevel, MOTD

### Class Starting Attributes
11 classes with HP, STR, DEF, STA, AGI, CHA, DEX, WIS, INT, CON, Mana base values.

### Race Attributes
10 races with HP/STR/DEF/STA bonuses, min/max age, height, weight, appearance.

### Race Lifespans
Human 75, Hobbit 90, Elf 200, HalfElf 120, Dwarf 150, Troll 60, Orc 55, Gnome 130, Gnoll 50, Mutant 65.

---

## 26. Enums Reference

### Character Enums
```csharp
CharacterClass { Alchemist=0, Assassin, Barbarian, Bard, Cleric, Jester, Magician, Paladin, Ranger, Sage, Warrior }
CharacterRace { Human, Hobbit, Elf, HalfElf, Dwarf, Troll, Orc, Gnome, Gnoll, Mutant }
CharacterAI { Computer='C', Human='H', Civilian='N' }
CharacterSex { Male=1, Female=2 }
```

### Item & Equipment Enums
```csharp
ObjType { Head=1, Body=2, Arms=3, Hands=4, Fingers=5, Legs=6, Feet=7, Waist=8, Neck=9, Face=10, Shield=11, Food=12, Drink=13, Weapon=14, Abody/Cloak=15, Magic=16, Potion=17 }
EquipmentSlot { Head=1, Body, Arms, Hands, Legs, Feet, Waist, Neck, Neck2, Face, Cloak, LFinger, RFinger, MainHand=14, OffHand=15 }
EquipmentRarity { Common, Uncommon, Rare, Epic, Legendary, Artifact }
WeaponHandedness { OneHanded=1, TwoHanded=2, OffHandOnly=3 }
```

### Location Enum (Key Values)
```csharp
GameLocation { MainStreet=1, TheInn=2, DarkAlley=3, Church=4, WeaponShop=5, Master=6, MagicShop=7, Dungeons=8, ArmorShop=18, Bank=19, Healer=21, Marketplace=22, TeamCorner=41, Temple=47, Castle=70, QuestHall=75, Prison=90, LoveStreet=200, Home=201, GodWorld=400, SysOpConsole=500, Arena=501 }
```

### Combat Enums
```csharp
CombatActionType { Attack, Defend, Heal, CastSpell, UseAbility, Backstab, Retreat, PowerAttack, Rage, Smite, Disarm, Taunt, BegForMercy, ... }
CombatOutcome { Win, Loss, Fled }
CombatSpeed { Normal=0, Fast=1, Instant=2 }
```

### Story Enums
```csharp
StoryChapter { Awakening, FirstBlood, TheStranger, TheFirstSeal, FactionChoice, RisingPower, TheWhispers, FirstGod, GodWar, TheChoice, Ascension, FinalConfrontation, Epilogue }
EndingType { Usurper, Savior, Defiant, TrueEnding, Dissolution }
OldGodType { Maelketh, Veloura, Thorgrim, Noctura, Aurelion, Terravok, Manwe }
GodStatus { Unknown, Imprisoned, Dormant, Dying, Corrupted, Neutral, Awakened, Hostile, Allied, Saved, Defeated, Consumed }
ArtifactType { CreatorsEye, SoulweaversLoom, ScalesOfLaw, ShadowCrown, SunforgedBlade, Worldstone, VoidKey }
SealType { Creation, FirstWar, Corruption, Imprisonment, Prophecy, Regret, Truth }
```

### Difficulty
```csharp
DifficultyMode { Easy, Normal, Hard, Nightmare }
```

---

## File Structure

```
Console/Bootstrap/
  Program.cs                    # Entry point, mode selection

Scripts/
├── Core/
│   ├── GameEngine.cs           # Main game loop, state management (singleton)
│   ├── GameConfig.cs           # Constants, class/race attributes, version
│   ├── Character.cs            # Base character class + all enums
│   ├── Player.cs               # Player-specific logic
│   ├── NPC.cs                  # NPC with AI, brain, personality, lifecycle
│   ├── Monster.cs              # Combat entity (standalone)
│   ├── Items.cs                # Legacy Pascal item system
│   ├── Equipment.cs            # Modern RPG equipment
│   └── King.cs                 # Ruler system, treasury, guards
│
├── Systems/ (68+ files)
│   ├── LocationManager.cs      # Location routing & navigation graph
│   ├── SaveSystem.cs           # Save/load with pluggable backends
│   ├── SaveDataStructures.cs   # All serialization data classes
│   ├── CombatEngine.cs         # Turn-based combat (all modes)
│   ├── DailySystemManager.cs   # Daily resets, maintenance
│   ├── WorldSimulator.cs       # Background NPC AI simulation
│   ├── WorldSimService.cs      # Headless simulation service
│   ├── SpellSystem.cs          # Spell casting mechanics
│   ├── ClassAbilitySystem.cs   # Class abilities (stamina-based)
│   ├── MonsterGenerator.cs     # Level-scaled monster creation
│   ├── QuestSystem.cs          # Quest generation & tracking
│   ├── AchievementSystem.cs    # 34+ achievements
│   ├── StatisticsSystem.cs     # Player stats tracking
│   ├── RelationshipSystem.cs   # NPC relationships, marriage
│   ├── FamilySystem.cs         # Children, aging, NPC-NPC children
│   ├── CompanionSystem.cs      # 4 recruitable companions
│   ├── FactionSystem.cs        # 3 joinable factions
│   ├── StoryProgressionSystem.cs # Main narrative tracking
│   ├── EndingsSystem.cs        # 5 game endings
│   ├── OceanPhilosophySystem.cs # Spiritual awakening
│   ├── SevenSealsSystem.cs     # Collectible lore fragments
│   ├── GriefSystem.cs          # Companion death consequences
│   ├── BetrayalSystem.cs       # NPC betrayal mechanics
│   ├── MoralParadoxSystem.cs   # Complex moral choices
│   ├── DreamSystem.cs          # Dream narratives at Inn
│   ├── StrangerEncounterSystem.cs # Noctura in disguise
│   ├── TownNPCStorySystem.cs   # 6 NPCs with story arcs
│   ├── CycleDialogueSystem.cs  # NG+ aware dialogue
│   ├── CityControlSystem.cs    # Economic control
│   ├── TournamentSystem.cs     # Combat tournaments
│   ├── NewsSystem.cs           # Event feed (file + SQLite)
│   ├── DebugLogger.cs          # File-based debug logging
│   ├── TelemetrySystem.cs      # PostHog opt-in telemetry
│   ├── SqlSaveBackend.cs       # SQLite for online mode
│   ├── IOnlineSaveBackend.cs   # Online backend interface
│   ├── OnlineStateManager.cs   # Player presence tracking
│   ├── OnlineChatSystem.cs     # Cross-location messaging
│   ├── OnlineAuthScreen.cs     # Login/register UI
│   ├── NPCSpawnSystem.cs       # NPC management singleton
│   ├── NPCMarriageRegistry.cs  # NPC marriage tracking
│   └── ...
│
├── Locations/ (31 files)
│   ├── BaseLocation.cs         # Abstract base (global commands, loop)
│   ├── MainStreetLocation.cs   # Central hub
│   ├── DungeonLocation.cs      # 100-floor dungeon (~6000 lines)
│   ├── CastleLocation.cs       # Politics, throne, court
│   ├── InnLocation.cs          # Rest, companions, tournaments
│   ├── HomeLocation.cs         # Housing, family, trophies
│   ├── ArenaLocation.cs        # PvP combat (online)
│   ├── TempleLocation.cs       # Gods, worship
│   └── ...
│
├── Data/
│   ├── MonsterFamilies.cs      # 10 families × 5 tiers
│   ├── SpellDatabase.cs        # 75 spells (25 per caster class)
│   └── EquipmentData.cs        # Equipment definitions
│
├── AI/
│   ├── NPCBrain.cs             # Goal-based NPC AI
│   ├── MemorySystem.cs         # NPC memory (100 max, 7-day decay)
│   └── EnhancedNPCBehaviors.cs # Shopping, gangs, relationships
│
├── BBS/
│   ├── DoorMode.cs             # Command-line parsing, mode detection
│   ├── DropFileParser.cs       # DOOR32.SYS / DOOR.SYS parsing
│   ├── SocketTerminal.cs       # TCP socket I/O for telnet
│   └── BBSTerminalAdapter.cs   # Terminal adapter (ANSI/WWIV/Console)
│
└── UI/
    ├── TerminalEmulator.cs     # Console/Godot/BBS output abstraction
    └── UIHelper.cs             # 80-char box drawing utilities

web/
├── index.html                  # Landing page + terminal + stats
├── dashboard.html              # NPC Analytics Dashboard (Chart.js + D3.js)
├── ssh-proxy.js                # HTTP + WebSocket + SSE server + dashboard API
└── package.json                # ws, ssh2, better-sqlite3

scripts-server/
├── nginx-usurper.conf          # Nginx reverse proxy
├── usurper-web.service         # Web proxy systemd service
├── usurper-world.service       # World simulator systemd service
├── setup-server.sh             # Server initialization
├── update-server.sh            # Binary deployment
├── backup.sh                   # Daily database backup
└── healthcheck.sh              # System monitoring

Tests/
└── Tests.csproj                # Unit tests (MonsterTests, etc.)

DOCS/
├── BBS_DOOR_SETUP.md           # SysOp setup guide
├── RELEASE_NOTES_*.md          # Version release notes
└── examples/                   # Example drop files
```

---

## Key Design Patterns

1. **Singleton Pattern** — Thread-safe `Lazy<T>` for GameEngine, SaveSystem, LocationManager, NPCSpawnSystem, NewsSystem, etc.

2. **Pluggable Backend** — `ISaveBackend` with `FileSaveBackend` (local) and `SqlSaveBackend` (online) implementations.

3. **Location Loop** — Each location inherits `BaseLocation`, implements `DisplayLocation()` and `ProcessChoice()`, gets global commands for free.

4. **Deterministic Generation** — Dungeon floors use seeded Random (`level * 31337 + 42`) for consistent layouts across visits.

5. **Dual Equipment** — Legacy Pascal item IDs coexist with modern Equipment objects during migration.

6. **Atomic SQL Operations** — Gold theft uses `json_set()` with `MAX(0, ...)` to prevent negative values without loading full save.

7. **Message Watermark** — Broadcast messages use ID-based watermark instead of `is_read` flag to prevent re-delivery.

8. **News Callback** — `NewsSystem.DatabaseCallback` routes NPC activity to SQLite for the website's SSE feed.

9. **Heartbeat Presence** — Online players tracked with 120-second heartbeat window for Who's Online.

10. **NPC Death Duality** — `IsDead` (permanent flag) vs `IsAlive` (computed `HP > 0`). Must check both for different scenarios.

11. **World State Authority** — World sim is the authority for shared state (NPCs, children, marriages, world events, economy). Player sessions load from `world_state` on login and push changes back on save. Independent version tracking prevents stale overwrites.

12. **Antagonistic Emotions** — NPC emotional states use personality-derived baselines with antagonistic suppression (anger dampens happiness, fear dampens confidence) rather than independent values.
