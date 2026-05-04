# Usurper Reborn -- Architecture Document

> A comprehensive technical reference for the Usurper Reborn codebase as of v0.60.7 (Beta).

**Runtime**: .NET 8.0 LTS | **Language**: C# 12 | **License**: GPL v2

**Repository scale**: ~228 C# source files (~106 in `Scripts/Systems`, 33 in `Scripts/Locations`), plus the Node web proxy, Electron client, and 5-language localization payloads.

This doc is updated per major release. If a section disagrees with the code, the code is the truth and this doc has rotted. Open an issue or PR.

---

## Table of Contents

1. [Entry Points & Execution Modes](#1-entry-points--execution-modes)
2. [Core Game Engine](#2-core-game-engine)
3. [Character Hierarchy](#3-character-hierarchy)
4. [Classes & Specializations](#4-classes--specializations)
5. [Equipment & Inventory](#5-equipment--inventory)
6. [Location System](#6-location-system)
7. [Dungeon System](#7-dungeon-system)
8. [Combat Engine](#8-combat-engine)
9. [Spell & Ability Systems](#9-spell--ability-systems)
10. [Monster System](#10-monster-system)
11. [NPC AI & Behavior](#11-npc-ai--behavior)
12. [World Simulator](#12-world-simulator)
13. [NPC Lifecycle](#13-npc-lifecycle)
14. [Save System & Serialization](#14-save-system--serialization)
15. [MUD Server Architecture](#15-mud-server-architecture)
16. [Online Multiplayer Schema](#16-online-multiplayer-schema)
17. [Server Settings Registry (Admin Tunables)](#17-server-settings-registry)
18. [Permadeath & Resurrection](#18-permadeath--resurrection)
19. [Bot Detection](#19-bot-detection)
20. [GMCP Integration](#20-gmcp-integration)
21. [Group Cooperative Dungeons & Spectator Mode](#21-group--spectator)
22. [Guild System](#22-guild-system)
23. [World Boss Raid System](#23-world-boss-raid-system)
24. [Discord Bridge](#24-discord-bridge)
25. [Story & Narrative Systems](#25-story--narrative-systems)
26. [Quest System](#26-quest-system)
27. [Companion System](#27-companion-system)
28. [Relationship & Family Systems](#28-relationship--family-systems)
29. [Achievement & Statistics](#29-achievement--statistics)
30. [Localization](#30-localization)
31. [Terminal & UI Layer](#31-terminal--ui-layer)
32. [BBS Door Mode](#32-bbs-door-mode)
33. [Steam Integration](#33-steam-integration)
34. [WezTerm Desktop Launcher](#34-wezterm-desktop-launcher)
35. [Electron Graphical Client (Beta)](#35-electron-graphical-client)
36. [Website & Web Proxy](#36-website--web-proxy)
37. [Web Admin Dashboard](#37-web-admin-dashboard)
38. [Server Infrastructure](#38-server-infrastructure)
39. [CI/CD Pipeline](#39-cicd-pipeline)
40. [Configuration & Constants](#40-configuration--constants)
41. [Enums Reference](#41-enums-reference)
42. [File Structure](#42-file-structure)
43. [Key Design Patterns](#43-key-design-patterns)

---

## 1. Entry Points & Execution Modes

**File**: `Console/Bootstrap/Program.cs`

Six distinct execution modes selected by command-line flags:

### Console Mode (default, no flags)
Standard local play. Initializes Steam integration if available, runs `GameEngine.RunConsoleAsync()` -> splash -> version check -> main menu -> save select -> game loop.

### BBS Door Mode (`--door`, `--door32`, `--doorsys`, `--node`)
Runs as a BBS door. Parses DOOR32.SYS / DOOR.SYS drop files, sets up `BBSTerminalAdapter` for socket / stdio I/O, runs `GameEngine.RunBBSDoorMode()` (auto-loads player by BBS username, skips menus). The "BBS door" name is a misnomer at this point: this entry point is also what every MUD session enters through.

### Online / `--online`
Single-instance file-backed multi-user mode (intended for self-hosted BBS sysops). Uses `SqlSaveBackend` against a local SQLite file. Each `--online` BBS gets its own self-contained world.

### MUD Server (`--mud-server --mud-port <port>`)
The canonical multiplayer deployment. One process serves N concurrent player sessions over TCP / SSH-forwarded socket. Backed by SQLite. Hosts `MudServer`, `BotDetectionSystem`, `DiscordBridge`, settings apply-queue poller, idle watchdog, all from the same process.

### MUD Relay (`--mud-relay --mud-port <port>`)
Lightweight client process that connects to a MUD server. The SSH gateway (`sshd-usurper`) launches this with `ForceCommand` so SSH players speak text to the relay, the relay speaks AUTH protocol to the MUD server. Web terminal also reaches the MUD via the relay.

### World Simulator (`--worldsim`)
Headless 24/7 NPC simulation (deprecated since v0.60.0; the MUD server has integrated world sim). Kept for legacy single-server deployments.

### Command-Line Flags (selection)
```
--door <path>              Auto-detect drop file type
--door32 / --doorsys       Explicit drop-file type
--node <dir>               Search directory for drop files
--local                    Local testing (no BBS), routes through RunDoorModeAsync
--stdio                    stdio mode (Synchronet / SSH-piped BBS)
--fossil <port>            Legacy FOSSIL serial mode
--online                   --online single-instance multiplayer
--mud-server               Run as multiplayer game server
--mud-relay                Run as relay client (used by sshd-usurper)
--mud-port <N>             Port for server / relay
--admin <username>         Mark this account as admin in the MUD process
--auto-provision           Auto-create accounts for trusted BBS auth
--user <username>          Pre-set username (skip auth)
--db <path>                SQLite database path
--worldsim                 Headless simulator (deprecated)
--editor                   Standalone game / save editor (single-player only)
--screen-reader            Force accessible mode (NVDA / JAWS / Narrator)
--electron                 Emit OSC events for the Electron graphical client
--public                   Self-listing on a future server registry (placeholder; deferred)
--private                  Opt out of public listing (placeholder)
--verbose / -v             Verbose debug output
```

The `--screen-reader` flag has automatic siblings: on Windows, `Console/Bootstrap/Program.cs` calls `AccessibilityDetection.IsScreenReaderActive()` (via `SystemParametersInfo(SPI_GETSCREENREADER, ...)`) and enables the mode if a screen reader is running. Auto-detect is skipped in any door mode and inside WezTerm to avoid host-flag false positives (v0.60.7).

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
- **Console**: `RunMainGameLoop()` -> splash -> version check -> `MainMenu()` -> load/create -> game loop
- **BBS / MUD / Online**: `RunBBSDoorMode()` -> auto-load by username -> game loop

### Initialization
- `InitializeGame()` -> reads config, initializes LocationManager, items, monsters, NPCs, levels, guards
- `CreateNewGame(playerName)` -> character creation -> starts game
- `LoadSaveByFileName(filename)` -> loads saved character -> starts game; on failure, shows recovery menu (backup file, 3 most recent autosaves, emergency dumps)
- `ShowLoadFailureWithRecovery()` -> file-size + error-string detection; offers `[R] Auto-repair` for bloated saves (>10 MB)

### Save File Repair (v0.57.19+)
**File**: `Scripts/Systems/SaveFileRepair.cs`

In-place repair for bloated saves (e.g. 71 MB). Reads via `JsonDocument.Parse(byte[])` (~2-3x file size in RAM, vs `JsonSerializer.Deserialize<T>` which materializes 10x+). Walks the tree via `Utf8JsonWriter`, applies hard-coded path-to-cap map for 17 known-bloated arrays (NPC memories, recent dialogue, relationships, enemies, market inventory, royal court collections, romance encounter history, etc.). Atomic `<file>.repair.tmp` -> `File.Move(overwrite: true)`.

### Periodic Updates
`PeriodicUpdate()` runs every 30 seconds during gameplay: daily reset check, world sim tick, NPC maintenance.

---

## 3. Character Hierarchy

```
Character (base)
├── Player    (the player character)
├── NPC       (town NPCs, romance, enemies)
└── Monster   (dungeon creatures, standalone -- does NOT extend Character)
```

### Character (base)
**File**: `Scripts/Core/Character.cs` (~2000 lines)

Stats follow Pascal `UserRec` layout for source-game compatibility. Modern fields layered on top:

- **Identity**: `Name1` (account / login), `Name2` (display name), `Race`, `Class`, `Specialization`, `Level`, `Age`, `Sex`, `Orientation`
- **Resources**: `HP`, `MaxHP`, `Mana`, `MaxMana`, `Stamina` (combat resource), `MaxCombatStamina`, `Gold`, `BankGold`, `Experience`
- **Attributes**: `Strength`, `Defence`, `Dexterity`, `Wisdom`, `Intelligence`, `Constitution`, `Charisma`, `Agility` (8 core, all `long`)
- **Combat**: `WeapPow`, `ArmPow`, `Punch`, `Absorb`
- **Equipment**: dual system (legacy slot fields + modern `Dictionary<EquipmentSlot, int> EquippedItems`)
- **Spells & Abilities**: `List<List<bool>> Spell` (2D: index x known/mastered), `HashSet<string> LearnedAbilities`, `List<int> Skill`
- **Status Effects**: 17+ flags (`Blind`, `Poisoned`, `Stunned`, `Frozen`, `Sleep`, `Feared`, `Confused`, `Slowed`, `Cursed`, `Diseased`, `Burning`, `Marked`, `Corroded`, `IsArrestCombat`, `HasStatusImmunity`, `Hidden`, `IsBerserk`, ...)
- **Online Death**: `Resurrections`, `MaxResurrections`, `ResurrectionsUsed`, `TempleResurrectionsUsed`, `IsArrestCombat`, `PlaythroughDeaths`
- **Alignment**: `Chivalry`, `Darkness` (clamped to `[0, 1000]` via setter, paired-movement helper in `AlignmentSystem.ChangeAlignment`)
- **Daily counters**: `MurdersToday`, `TeamWarsToday`, `DrinkingGamesToday`, `WildernessRevisitsToday`, `DesecrationsToday`, ...
- **Knighthood / Fame**: `IsKnighted`, `Fame`, `WeeklyRank`, `PreviousWeeklyRank`, `RivalName`, `RivalLevel`
- **Login streaks**: `LoginStreak`, `LongestLoginStreak`, `LastLoginDate`
- **Blood Moon**: `BloodMoonDay`, `IsBloodMoon`
- **Settlement buffs**: `SettlementGoldClaimedToday`, `HQArmoryLevel`, `HQBarracksLevel`, `HQTrainingLevel`, `HQInfirmaryLevel` (transient; cached from DB on login)
- **NG+**: `CycleExpMultiplier`, NG+ cycle level
- **Preferences**: `CombatSpeed`, `AutoHeal`, `SkipIntimateScenes`, `ScreenReaderMode`, `CompactMode`, `Language`, `DisableCharacterMonsterArt`, `MutedChannels`

`RecalculateStats()` is the canonical re-derivation entry point: resets derived fields to `Base*`, layers race / class growth, applies equipment, applies child bonuses, applies specialization bonuses, and clamps. Called on every character mutation.

### Player
**File**: `Scripts/Core/Player.cs`

Adds player-specific fields: `RealName`, `LastLogin`, `TotalLogins`, `TotalPlayTime`, `PvPWins`, `PvPLosses`, `DungeonLevel`, `IsOnline`, `UnlockedAbilities`, `PlayerStatistics`, `PlayerAchievements`.

### NPC
**File**: `Scripts/Core/NPC.cs`

Adds AI / social state:
- **AI**: `NPCBrain Brain`, `PersonalityProfile Personality`, `MemorySystem Memory`, `EmotionalState EmotionalState`, `GoalSystem Goals`
- **Social**: `Archetype`, `StoryRole`, `Faction`, `GangId`, `KnownCharacters`, `Enemies`, `GangMembers`
- **Death**: `IsDead` (permanent flag, distinct from `IsAlive` which is `HP > 0`)
- **Lifecycle**: `BirthDate`, `IsAgedDeath`, `PregnancyDueDate`, `PregnancyFatherName` (for affair attribution)
- **Marriage**: `IsMarried`, `Married`, `SpouseName`, `MarriedTimes`, `BannedMarry`
- **Commerce**: `MarketInventory` (auction listings)
- **Specialization**: `Specialization` (enum), drives AI ability priorities and per-spec stat bonuses

### Monster
**File**: `Scripts/Core/Monster.cs`

Standalone class (not extending Character). Combat-relevant fields only: `Level`, `HP`, `Strength`, `Defence`, `Punch`, `WeapPow`, status flags (`PoisonRounds`, `IsBurning`, `StunRounds`, `Frozen`, `Sleep`, `Feared`, `Confused`, `Slowed`, `Marked`, `Corroded`, `WeakenRounds`, `IsChanneling`, `IsPhysicalImmune`, `IsMagicalImmune`, `PhaseImmunityRounds`, `IsEnraged`), boss-specific (`TauntedBy`, `TauntRoundsLeft`, `TauntStickChance`).

---

## 4. Classes & Specializations

### 17 Total Classes (12 base + 5 prestige)

Defined in `CharacterClass` enum. Per-class starting stats and per-level growth in `GameConfig.cs` and `LevelMasterLocation.cs`.

**Base classes (12)**: Alchemist, Assassin, Barbarian, Bard, Cleric, Jester, Magician, Paladin, Ranger, Sage, Warrior, Mystic Shaman.

**Prestige classes (5; unlocked by completing different endings):**
| Class | Ending Required | Theme |
|---|---|---|
| Tidesworn | (any) | Defensive warrior-cleric hybrid |
| Wavecaller | (any) | Ocean-themed CHA-scaling spellcaster |
| Cyclebreaker | NG+ Cycle 2+ | Time-bending hybrid |
| Abysswarden | (Usurper) | Dark prison-themed lifesteal |
| Voidreaver | (Defiant) | Pain-based glass cannon |

Mystic Shaman is race-locked (Troll / Orc / Gnoll only) and is a melee-caster hybrid with totems and elemental weapon enchantments.

### Specialization System
**File**: `Scripts/Data/SpecializationData.cs`, `Scripts/Systems/CompanionSystem.cs`

24 specializations (2 per base class). Selectable at the Level Master, free swap. Each specialization carries:
- `SpecRole` enum (Tank / DPS / Healer / Utility / Debuff)
- Stat bonus deltas applied additively in `RecalculateStats()`
- AI ability priorities (75% weight to preferred-type abilities for NPC teammates)

`IsHealerSpec()`, `IsTankSpec()` helpers used by combat code for spec-specific bonuses (e.g. healer specs get +20% on `Type == Heal` abilities, Protection Warrior gets +20% damage with shield equipped, Guardian Paladin gets +15% damage and heal with shield).

### Character Creation
**File**: `Scripts/Systems/CharacterCreationSystem.cs`

ANSI portrait + stats card on selection screens (10 races, 10+ classes have portraits; rest fall back to text card). Stat-roll loop with 5-reroll cap. Romantic orientation selection (Straight / Gay / Bisexual / Asexual). NG+ starting bonuses applied via `MetaProgressionSystem.GetStartingLevelBonus()` (Veteran = level 5, Master = level 10).

---

## 5. Equipment & Inventory

### Legacy Item System (Pascal-compatible)
**File**: `Scripts/Core/Items.cs`

`ObjType` enum: Head=1, Body=2, ... Potion=17. Properties: `Name`, `Value`, stat bonuses, `Cursed`, `OnlyOne`, `Cure`, `Shop`, `Dungeon`, `MinLevel`, `MaxLevel`, `ClassRestrictions`, plus `LootEffects` for procedural drops (LifeSteal, ManaSteal, 6 elemental enchants, ArmorPiercing, Thorns, HPRegen, ManaRegen, MagicResist, PoisonDamage, BossSlayer, TitanResolve, etc.).

### Modern Equipment System
**File**: `Scripts/Core/Equipment.cs`, `Scripts/Data/EquipmentData.cs`

- **15 slots**: Head, Body, Arms, Hands, Legs, Feet, Waist, Neck, Neck2, Face, Cloak, LFinger, RFinger, MainHand, OffHand
- **Rarity**: Common, Uncommon, Rare, Epic, Legendary, Artifact
- **Weapon types**: Sword, Axe, Mace, Dagger, Rapier, Staff, Bow, Crossbow, Spear, Greatsword, Greataxe, Hammer, Maul, Flail, Buckler, Shield, TowerShield, Instrument
- **Armor weight class**: Light / Medium / Heavy (gates classes; affects dodge / stamina / fatigue)
- **Handedness**: OneHanded / TwoHanded / OffHandOnly

Both systems coexist. Legacy uses int IDs in slot fields; modern uses `EquippedItems` dict keyed by `EquipmentSlot`. `EquipItem(item, slot)` validates class restrictions, weight class, level requirement, handedness, and shield-vs-2H conflicts.

### Loot Generation
**File**: `Scripts/Systems/LootGenerator.cs`, `Scripts/Systems/ShopItemGenerator.cs`

- Per-slot drop pools so monsters drop head / arms / hands / legs / feet / waist / face / cloak / body, not just weapons + body
- Class-weighted thematic enchantments (caster weapons trend INT/WIS, Bard instruments CHA/DEX, etc.)
- Shop inventory is fully procedural (`ShopItemGenerator`) with pricing scaled to economy multipliers
- Drop-time `EnforceMinLevelFromPower()` so a Lv.100-power weapon doesn't appear with a misleading "Requires Level 80" tag

### Inventory System
**File**: `Scripts/Systems/InventorySystem.cs`

Per-slot equip with slot validation (the `Chain Shirt in MainHand` exploit was fixed in v0.60.2). Ring multi-slot carve-out (LFinger/RFinger fungible). Comparison overlay vs current equipment. Companion / NPC / spouse / team-NPC bag inspection ("Party Inventory Viewer", v0.57.2). Take-back from companion bags persists via `CompanionSystem.SyncCompanionEquipment(target)` in MUD mode.

---

## 6. Location System

**File**: `Scripts/Locations/BaseLocation.cs` (~3000 lines), `Scripts/Systems/LocationManager.cs` (singleton router)

### BaseLocation Lifecycle
Every location:
- `EnterLocation(player, terminal)` -> setup + presence + `LocationLoop()`
- `LocationLoop()` -> display -> input -> process -> auto-save -> turn increment -> world-sim every 5 turns -> GMCP `Char.Vitals` if dirty
- `DisplayLocation()` -> render
- `ProcessChoice(string choice)` -> handle menu
- `TryProcessGlobalCommand(input)` -> universal commands (run before per-location dispatch)
- `GetMudPromptName()` -> contextual MUD prompt fragment (e.g. "Inn", "Dungeon Fl.5")

### Global Slash Commands
| Command | Aliases | Function |
|---|---|---|
| `/stats` | `/s` | Character statistics |
| `/inventory` | `/i`, `*` | Inventory screen |
| `/quests` | `/q` | Active quests |
| `/health` | `/hp`, `%` | HP/MP/SP with active buffs |
| `/gear` | `/eq` | Detailed equipment + per-team-member breakdown |
| `/time` | `/t` | Game-time and turn count |
| `/gold` | `/g` | Gold + bank |
| `/map` | `/m` | Town location overview |
| `/herb` | (n/a) | Use herb from quick-buf |
| `/prefs` | `/p`, `~` | Preferences (the SAFE per-player menu) |
| `/help` | `/?`, `H` | Help |
| `/founders`/`/statues`/`/hall` | | Founder hall hub |
| `/town`/`/townhall` | | Team Town Hall (turf controllers) |
| `/restore` | | (admin-only) Restore deleted character |
| `/look` / `look` / `l` | | Reprint location banner (MUD mode) |

Global slash channels (MUD): `/say`, `/tell`, `/gossip`, `/shout`, `/gc` (guild), `/who`, `/news`, `/group`, `/leave`, `/disband`, `/accept`, `/deny`, `/spectate`, `/spectators`, `/nospec`, `/settings`, `/compact`, `/bug`. Channels auto-mute by typing the slash command with no message body.

### All Locations (~30 total)
**Hub**: MainStreet
**Commerce**: WeaponShop, ArmorShop, MagicShop, MusicShop (v0.49.0), Bank, Marketplace
**Social**: Inn, DarkAlley, LoveStreet, LoveCorner, TeamCorner
**Religious**: Church, Temple, Pantheon (immortals only), GodWorld
**Services**: Healer, LevelMaster
**Combat**: Dungeon, Arena (PvP), Wilderness (v0.49.4)
**Housing**: Home (with 5-tier upgrade system), Dormitory
**Governance**: Castle, Prison, PrisonWalk, AnchorRoad
**Quests**: QuestHall, News
**Settlement** (v0.49.5): Settlement / Outskirts (NPC-built shops, votes, services)
**Admin**: SysOpConsole, CharacterCreation
**Legacy**: GodWorld

### Travel
`LocationManager` maintains a navigation graph (per-location exit list). Transitions go through `GameEngine.NavigateToLocation(GameLocation)` which validates the edge in `navigationTable`. `LocationExitException` is the bypass for special teleports (royal-guard defense alert, immortal-locked-to-Pantheon, prison sentencing). Caught at the top of `LocationManager.EnterLocation`.

---

## 7. Dungeon System

**File**: `Scripts/Locations/DungeonLocation.cs` (~13,000 lines, the largest single file)

### Structure
- 100 procedurally generated floors with **deterministic seeding**: `new Random(level * 31337 + 42)` ensures reproducible layouts across visits
- Floor themes (8): Underground, Cavern, Temple, Ancient, Corrupted, Crystalline, Magma, Frozen
- Room types: MonsterRoom, TreasureChest, Shrine, LoreLibrary, Trap, SecretVault, MeditationChamber, Settlement, PuzzleRoom, Riddle, LeverPuzzle, MemoryFragment, Boss, Stair
- Floor state persisted in `player.DungeonFloorStates` dictionary (per-floor: monsters, cleared, last visited / cleared, boss defeated)

### Access & Progression
```csharp
int minAccessible = Math.Max(1, playerLevel - 10);
int maxAccessible = Math.Min(GameConfig.MaxDungeonLevel, playerLevel + 10);
```

`maxDungeonLevel` is a property reading `GameConfig.MaxDungeonLevel` live so admin / SysOp changes take effect on next floor change (v0.60.7).

### Hourly Floor Respawn
Floors with `LastVisitedAt > 1 hour ago` repopulate non-boss monster rooms on next entry, with thematic message. Boss rooms / special floors honor `IsPermanentlyClear` / `BossDefeated`.

### Special Floors

**Seal Floors (7)**: 0 (town), 15, 30, 45, 60, 80, 99 -- Ancient Seals of Truth.

**Old God Floors (7)**: 25, 40, 55, 70, 85, 95, 100 -- boss encounters with phase mechanics.

| Floor | God | Domain | Artifact |
|---|---|---|---|
| 25 | Maelketh | War & Conquest | Creator's Eye |
| 40 | Veloura | Love & Passion | Soulweaver's Loom |
| 55 | Thorgrim | Law & Order | Scales of Law |
| 70 | Noctura | Shadow & Secrets | Shadow Crown |
| 85 | Aurelion | Light & Truth | Sunforged Blade |
| 95 | Terravok | Earth & Endurance | Worldstone |
| 100 | Manwe | Creation & Balance | Void Key |

### Old God Boss Mechanics
**File**: `Scripts/Systems/OldGodBossSystem.cs`, `Scripts/Data/OldGodsData.cs`

Per-boss `BossContext` carries: 3 phase HP thresholds, per-phase ability lists, AoE % MaxHP cap (with first-3-rounds 15% per-hit damage cap), channel/interrupt mechanics, status immunities (physical / magical), corruption stacks, doom countdown, divine armor (boss self-protection scaled by player's equipped artifact count), enrage timer with extra-attack count, potion cooldown, phase-transition dialogue. Companion damage per round capped at `OldGodTeammateDamageCapPercent` (75%) of MaxHP. Solo Old God fights apply +15% boss damage AND -20% player damage (preserves the 1v1 tension while staying winnable). NG+ "Divine Scaling" buffs remaining gods +10% HP / +5% damage per artifact already collected (cap +40% / +20%).

### Floor Locking
Special floors lock the player until cleared:
- Old God floors require defeating / saving the Old God (not just any monster); `BossDefeated` flag tracks the actual boss
- Seal floors require collecting the seal; collection adds floor to `player.ClearedSpecialFloors` AND `StoryProgressionSystem.CollectedSeals`

### Teammate System
Companions, spouses, lovers, and team NPCs join the dungeon party; live grouped players also join via the Group System. Team combat grants +15% XP/gold bonus. Party state is canonical in `GameEngine.DungeonPartyNPCIds` and `GameEngine.DungeonPartyPlayerNames`; `SyncNPCTeammatesToGameEngine()` rebuilds both from the live `teammates` collection. Removed echoes do not respawn on dungeon re-entry (v0.57.6).

### Floor 5 Dungeon Guardian
Mini-boss encounter on first floor-5 entry. Tied to the v0.52.0 onboarding hook arc.

### Settlements
NPC-built community at floor 1. 7 building types (Palisade, Tavern, Market Stall, Shrine, Workshop, Watchtower, Council Hall) x 3 tiers each. NPCs propose builds, vote, contribute gold; players can endorse / oppose. Buffs: Tavern XP, Workshop +20% ATK / 5 combats, Watchtower full floor reveal, Council Hall daily gold for the controlling team, etc.

---

## 8. Combat Engine

**File**: `Scripts/Systems/CombatEngine.cs` (~28,000 lines, the largest single system)

### Combat Modes
- `PlayerVsMonsters()` -- single & multi-monster
- `PlayerVsPlayer()` -- arena, duels, gang wars (basic-attack-only AI for the defender, scaled by class)
- Group dungeon combat -- each grouped player runs their own action loop, monsters attack the whole party
- Group loot -- dice-roll system with per-item Pass/Equip/Take prompts to followers

### Combat Flow
```
1. Initialize (reset transient buffs, stamina, init session, show intro)
2. Loop while player alive AND monsters alive:
   a. Display combat status (mode-aware: visual / compact / SR / Electron)
   b. Get player action (interactive or quickbar)
   c. Process player action (with ability cooldown, stamina, mana checks)
   d. Each surviving monster: select target -> roll attack -> apply damage
   e. Each NPC teammate: process status -> CanAct -> AI ability/spell/attack
   f. Decrement durations (status effects, taunt rounds, buff timers)
   g. Check end conditions (HP / monster count / fled flag / arrest combat)
3. Determine outcome and route rewards / death
```

### Combat Actions
Attack, Defend (+50% defense), Heal (potion), CastSpell, UseAbility, Backstab, Retreat (flee), PowerAttack (1.75x dmg, 15 STA, off-hand follow-up, enchant procs), PreciseStrike, Rage (Barbarian), Smite, Disarm, Taunt (single-target hard), Hide, RangedAttack, AidAlly (heal-or-mana-potion), BegForMercy, Status, UseHerb (J).

### Damage Formula (basic attack)
```
attackPower = Strength + StrengthBonus + Level*2 + WeapPow + Random(WeapPow) + Random(1, Level/2)
modifiers   x Raging 1.5, x TwoHanded 1.45 (post-v0.48), x DualWield off-hand 0.5
            +Knight 5%, +Shaman elemental rider, +Bard performance, +Specialization, +Settlement
defense     = monster.Defence + Random(Defence/8) + ArmPow + Random(ArmPow) -- 75-100% variance
damage      = max(MinIncomingDamage(target, rawAttack), attackPower - defense)
MinIncomingDamage = max(1, rawAttack/20, MaxHP * 0.25%)  // tank-floor scaling
```

### Hit Determination (D20)
`monsterAC = 10 + Level/5 + Defence/20 + situational`. Attack roll via `TrainingSystem.RollAttack()`. Natural 20 = crit, Natural 1 = miss. Crit chance cap is DEX-scaled (`50 + min(25, DEX/30)`, max 75% at 750 DEX) plus +10 from Creator's Eye (was a 1.5x multiplier; flat now to avoid pinning everyone at the cap).

### Anti-Exploit Caps
| Mechanic | Cap | Reason |
|---|---|---|
| Backstab success | 75% | Prevent guaranteed exploit |
| Backstab multiplier | 1.75x | Was 2.0x, double-crit fix |
| Mercy escape | 75% | Prevent guaranteed escape |
| Crit chance | DEX-scaled 50-75% | Diminishing returns |
| Flee chance | 75% | Risk/reward |
| Boss HP multiplier | 2.8x (regular boss), 1.5x champion solo, 2.2x champion solo (post-v0.56) | Scaling |
| Bank robbery | Real combat, scaled guard difficulty, full party | Anti-grind |
| Daily murder cap | 3/day | Keeps the town from being wiped in a session |
| Daily team wars | 3/day, 6h cooldown per opponent, 1.5x reward | Anti-grind |
| Daily drinking games | 5/day | Anti-grind |

### Status Effects on Player Path
`ProcessStatusEffects()` ticks at the start of every player turn. `CanAct()` short-circuits stunned / frozen / paralyzed / asleep with the matching message. Same logic mirrored on NPC teammates after the v0.57.4 fix (was previously enemy-only).

### Victory Rewards
Base XP / Gold from monster -> world event modifiers (Blood Moon 2x XP / 3x gold) -> difficulty modifiers -> NG+ cycle multipliers (`CycleExpMultiplier`) -> spec multipliers -> party split (auto-redistribution on teammate death; equal-or-percentage configurable by player). Companions get 50%, NPC teammates split a configurable allotment.

### PvP
Attacker vs AI-controlled defender (loaded from save at full HP). Same engine. Death handled in-arena: 10% gold stolen, 10% XP loss, 25% gold penalty, resurrect at Inn. Gold theft is bidirectional and atomic via `json_set()` SQL with `MAX(0, ...)`.

### Boss Rewards
+50 Fame for Old God defeat, +25 Fame for World Boss kill credit. Floor boss XP/gold +40% in v0.56.1.

### Monster Targeting Intelligence
Per-class threat weight (Paladin=180, Barbarian=170, Warrior=160 ... Magician=60, Sage=65) plus modifiers for armor, HP %, defending stance, and taunt state. Taunt force-targets at `TauntStickChance` (75% by default for AoE class taunts, 100% for hard `[T]` and Eternal Vigil).

---

## 9. Spell & Ability Systems

### Spells
**Files**: `Scripts/Data/SpellDatabase.cs`, `Scripts/Systems/SpellSystem.cs`

3 base caster classes with **25 spells each** (75 total) plus **5 prestige classes with 5 spells each** (25 total) = **100 spells**:

- **Cleric (Divine)**: Cure Light -> Cure Wounds -> Holy Smite -> Restoration -> Aurelion's Radiance -> God's Finger
- **Magician (Arcane)**: Magic Missile -> Fireball -> Lightning Bolt -> Disintegrate -> Meteor Swarm -> Wish
- **Sage (Mind/Shadow)**: Mind Spike -> Steal Life -> Energy Drain -> Shadow Step -> Temporal Paradox -> Death Kiss
- **Tidesworn**: Alethia's Ward, Ocean's Resilience, Calm Waters (party debuff shield), Deluge, Sanctified Torrent
- **Wavecaller**: CHA-scaling spells -- Tidal Strike, Siren's Lament, Wave Echo (double dmg vs debuffed), Symphony of the Depths, Harmonic Crescendo
- **Cyclebreaker**: Time-bending utility
- **Abysswarden**: Lifesteal / DoT
- **Voidreaver**: Glass-cannon nukes (Unmaking, Void Bolt)

**Mana cost**: `baseCost = 10 + (spellLevel * 5)`, reduced by Wisdom (max 50%). Healing spells never fizzle; offensive spells preserve the original fizzle gradient. Prestige spell scaling soft-capped at 4.0x INT (was hard 4.0x cap; v0.50.5 reworked to soft 8.0x ceiling).

### Class Abilities (44+ across 17 classes)
**File**: `Scripts/Systems/ClassAbilitySystem.cs`

~10 abilities per class spread levels 1-100, costing Combat Stamina. Each carries `RequiredWeaponTypes`, `RequiresShield`, cooldown, mana / stamina cost, target type, and tier-scaled stamina cost (L50+ +25%, L75+ +40%).

**Notable kits:**
- **Warrior**: Power Strike -> Shield Wall -> Battle Cry -> Shield Wall Formation (level 40 AoE taunt) -> Shield Bash (L16) -> Iron Fortress -> Champion's Strike
- **Paladin**: Lay on Hands -> Divine Smite -> Aura of Protection -> Holy Shield Slam (L28) -> Divine Mandate (L40 AoE thorn taunt) -> Avatar of Light
- **Barbarian**: Berserker Rage -> Reckless Attack -> Intimidating Roar -> Rage Challenge (L40 AoE taunt + regen) -> Bloodlust -> Avatar of Destruction
- **Assassin**: Backstab -> Evasion -> Poison Strike -> Biaxin (L35 poison + corrosion) -> Shadow Clone -> Assassinate -> Nightblade Dance (passive: Lethal Precision)
- **Tidesworn**: Undertow Stance -> Breakwater -> Abyssal Anchor (AoE taunt) -> Eternal Vigil (invuln-with-forced-aggro)
- **Mystic Shaman**: 4 enchantments (fire/frost/earth/storm) + 5 totems (healing/earthbind/searing/windfury/spirit link) + Lightning Bolt + Chain Lightning + Ancestral Guidance

Universal abilities (available to most classes): Second Wind, Battle Focus, etc. Spellcasters can equip these too (v0.50.5).

### Class Passives (selection)
- Warrior Iron Fortress: shield-equipped damage reduction
- Bard Bardic Inspiration: 15% proc +20 ATK to teammate
- Mystic Shaman Elemental Mastery: INT-scaling enchant rider (capped at `ShamanEnchantPowerCap = 250`)
- Paladin Divine Resolve: +10% vs undead/demons, 15% status resist
- Tidesworn Ocean's Voice: sequential +20% backup crit roll

---

## 10. Monster System

**Files**: `Scripts/Core/Monster.cs`, `Scripts/Data/MonsterFamilies.cs`, `Scripts/Systems/MonsterGenerator.cs`

### Monster Families (10 x 5 tiers = 50 unique types)
Goblinoid, Undead, Orcish, Draconic, Demonic, Giant, Beast, Elemental, Aberration, Insectoid. Tiers low-to-high per family.

### Stat Generation
```
HP        = (25*level + level^1.1 * 8) x bossMultiplier x GameConfig.MonsterHPMultiplier
Strength  = (2*level + level^1.05 * 1.5) x multiplier
Defence   = (level + level^1.02 * 0.5) x multiplier x 0.5
```

`GameConfig.MonsterHPMultiplier` is admin-tunable via the web Server Settings panel; effective at next monster spawn.

### Group Encounters
10% chance single mini-boss, 70% same-family thematic encounter, 30% mixed families (mixed-floor restricted to floor 30+ to keep early floors thematic). Group size scales: 1-2 / 1-3 / 2-4 / 3-5.

### Champion / Mini-Boss / Boss Tiers
| Tier | HP mult | Damage | Defense | Loot guarantee |
|---|---|---|---|---|
| Solo Champion | 2.2x | +30% | +20% | 2 items (was 1) |
| Group Champion | 1.5x | -- | -- | 1 item |
| Mini-Boss | 1.5x | +15% | +10% | Boosted |
| Floor Boss | 2.8x | x1.25 | x1.2 | 4.2x XP/gold; first-3-rounds 15% MaxHP per-hit damage cap |
| Old God | per-boss in `OldGodsData.cs` | per-boss | per-boss | 1-of-7 artifact + boss-themed loot |

### Boss Damage Multiplier
`DifficultySystem.ApplyMonsterDamageMultiplier(baseDmg)` is applied to monster basic-attack damage. Monster special abilities (DirectDamage, life drain, AoE) currently bypass it (existing pre-v0.60 gap, called out in the registry's descriptor description for honesty).

---

## 11. NPC AI & Behavior

### NPCBrain
**File**: `Scripts/AI/NPCBrain.cs`

Goal-based AI with 15-min decision cooldown:
1. Process enhanced behaviors
2. Update emotions from recent memory
3. Decay old memories
4. Re-evaluate goals
5. Get priority goal -> generate actions -> score -> select best

### Memory System
**File**: `Scripts/AI/MemorySystem.cs`

- Max **30 memories** per NPC (was 100; v0.57.16 reduced for save-size sanity), importance-weighted trim
- Decay after 7 days
- Memory types and impression deltas: Attacked (-0.8), Betrayed (-0.9), Helped (+0.4), Saved (+0.8), etc.
- Character impressions are floats in `[-1.0, +1.0]`
- Save-time defensive trim independent of in-memory cap

### Personality System
**File**: `Scripts/AI/PersonalityProfile.cs`

13 core personality traits (0.0-1.0): Aggression, Loyalty, Intelligence, Greed, Compassion, Courage, Honesty, Ambition, Vengefulness, Impulsiveness, Caution, Mysticism, Patience.

10 romance traits: Romanticism, Flirtatiousness, Commitment, attraction preferences, gender preferences, relationship style.

NPC orientation rates: 85% straight, 8% gay, 5% lesbian, 2% bi, with minimum-diversity guarantee (>=2 of each non-straight). Orientation persists per-NPC in saves.

### Enhanced Behaviors
**File**: `Scripts/AI/EnhancedNPCBehaviors.cs`

Per simulation tick:
1. Inventory swap if 20% better
2. Class-based shopping AI
3. Gang management (recruit, dissolve small)
4. Relationship dynamics (2% marriage chance/tick at L5+)
5. Gang warfare automation
6. Conway-style neighbor pressure (isolation / overcrowding modulates location selection)

### Emotional State
**File**: `Scripts/AI/EmotionalState.cs`

12 emotion types (Happiness, Anger, Fear, Confidence, Sadness, Greed, Hope, Peace, Trust, Loneliness, Envy, Pride). Personality-derived baselines + transient world-event modulation (+/-15%) + antagonistic suppression (high anger dampens happiness/peace, etc.). Serialized for the web admin / dashboard.

---

## 12. World Simulator

**Files**: `Scripts/Systems/WorldSimulator.cs` (in-game, single-player + MUD), `Scripts/Systems/WorldSimService.cs` (legacy headless deployment)

### Tick (every 30 seconds)
```
1. ProcessNPCRespawns
2. FamilySystem.ProcessDailyAging          -- age children, convert to NPCs at 18
3. ProcessNPCAging                         -- age NPCs, check natural death
4. ProcessNPCPregnancies                   -- check births, start new pregnancies
5. ProcessNPCDivorces                      -- personality-driven divorce chance
6. For each alive NPC:
   - Brain.DecideNextAction
   - ExecuteNPCAction
   - ProcessNPCActivities
   - ProcessNPCRelationships
7. Track dead NPCs for respawn
8. ProcessWorldEvents (Blood Moon, World Boss spawn check, etc.)
9. UpdateSocialDynamics
10. CulturalMemeSystem tick
11. ProcessNPCImmigration (if race extinction floor reached)
```

### Race Extinction Floor
NPCs of any race below `PermadeathRaceFloor = 3` trigger immigration; new NPCs of that race spawn from a name pool to keep the population diverse. `IsAgedDeath` is a hard NO-respawn flag.

### World State Authority
The world sim is the authoritative source for shared state (NPCs, royal court, economy, settlement, marriages, children, world events) in MUD mode. Player saves write player-specific data only; world state is loaded from `world_state` SQLite rows on every login and pushed back on mutating actions. Concurrent online edits are protected by row-level versioning and `OnlineStateManager.SaveAllSharedState()`.

---

## 13. NPC Lifecycle

### Aging
- `NpcLifecycleHoursPerYear = 9.6` (one in-game year per ~9.6 real hours)
- Age computed from `BirthDate`: `(DateTime.Now - BirthDate).TotalHours / 9.6`
- Race lifespans: Human 75 (~30 d), Elf 200 (~80 d), Orc 55 (~22 d), Gnoll 50 (~20 d), etc.

### Pregnancies
- 1% per tick for eligible married females (age 18-45). Dynamic 3% if underpopulated, 0.5% if overpopulated
- Gestation ~7 real hours
- Max 4 children per couple
- Same-sex couples: no natural pregnancy
- Affair pregnancy father is `NPC.PregnancyFatherName` (serialized; replaced an unserialized `_pregnancyFathers` dict that lost data on reloads, v0.52.11)

### Children
Born via `FamilySystem.CreateNPCChild()`. Auto-convert to full NPCs at age 18 via `ConvertChildToNPC()`. 80 fantasy names (40 male, 40 female) with Roman numeral suffixes for duplicates. Per-player children get full save plumbing; renaming or birthing immediately persists `world_state.children` to avoid race-condition wipes from concurrent logins.

### Natural Death, Divorce, Polyamory
- Natural death: `IsAgedDeath = true` -> permanent, never respawned; widowed spouse gets bereavement memory
- Divorce: 0.3% base/tick, modulated by personality and alignment compatibility
- Affairs: flirtatious married NPCs (>0.6) have 15% chance of conceiving with non-spouse
- Polyamory: NPCs with `RelationshipPreference.Polyamorous` / `OpenRelationship` can seek partners while married

### Permadeath Cleanup (v0.60.5+)
On NPC permadeath: marriage cleared, faction membership purged, gang affiliation dropped, pregnancy cleared, relationship cache pruned. The same purge runs for player permadeath via `SqlSaveBackend.PurgePlayerWorldState(username)` -- 9 tables in one transaction (guild_members, online_players, sleeping_players, wizard_flags, messages, trade_offers, bounties, auction_listings, world_boss_damage) plus in-memory hooks for `GodSystem.SetPlayerGod` and `RelationshipSystem.ResetPlayerRelationships`.

---

## 14. Save System & Serialization

**Files**: `Scripts/Systems/SaveSystem.cs`, `Scripts/Systems/SaveDataStructures.cs`, `Scripts/Systems/SaveFileRepair.cs`

### Architecture
Pluggable: `IOnlineSaveBackend` interface with two implementations:
- `FileSaveBackend` -- per-player JSON files in `saves/` (single-player and BBS sysop deployments)
- `SqlSaveBackend` -- SQLite (online / MUD multiplayer)

Selected at startup via `SaveSystem.InitializeWithBackend()`.

### Save Data
```csharp
SaveGameData {
  int Version, MinSaveVersion;
  DateTime SaveTime, LastDailyReset;
  int CurrentDay, GameTimeMinutes;
  PlayerData Player;            // 200+ fields
  List<NPCData> NPCs;            // Full state including AI brain
  WorldStateData WorldState;     // Economy, ruler, events, shops
  StorySystemsData StorySystems; // All narrative systems
  DailySettings Settings;        // Reset config
}
```

### PlayerData (selection of major fields)
Identity, stats, attributes, equipment (legacy + modern), inventory, status, daily counters, home upgrades, dungeon progression, preferences, statistics, achievements, login streak, weekly rank/rival, online death state (Resurrections, ResurrectionsUsed, MaxResurrections, TempleResurrectionsUsed, IsArrestCombat), Knighthood/Fame, Blood Moon state, NG+ cycle, child support data, herb pouch, settlement buffs, language/compact mode, mute channels, romantic orientation.

### NPCData
Identity, stats, class/race, lifecycle (Age, BirthDate, IsAgedDeath, PregnancyDueDate, PregnancyFatherName), marriage, faction, gang, full AI state (13 core personality + 10 romance traits, memories, goals, emotions), inventory, equipment, specialization.

### Bloat Caps (v0.57.16+, v0.57.18+)
Serialization-time caps prevent save inflation on long sessions:
- `MaxSerializedMemoriesPerNpc = 30` (top by Importance desc)
- `MaxSerializedDialogueIdsPerNpc = 50`
- `MaxSerializedRelationshipsPerNpc = 100` (by `|strength|` desc)
- `MaxSerializedKnownCharactersPerNpc = 80`
- `MaxSerializedEnemiesPerNpc = 30`
- Royal court collections (Prisoners 50, Orphans 100, Monarchs 30, Court 50, Heirs 20, Monster Guards 30)
- Romance encounter history 100
- Companion inventory 30
- Stranger encounter dialogue IDs 50, recent events 20
- Affairs 50
- Conversation states 100, topics-discussed per convo 30

### Atomic Writes (v0.57.18)
`FileSaveBackend.WriteGameData` uses `<file>.tmp` -> flush -> `File.Move(overwrite: true)`. Reader sees old or new, never half-written. Single-writer semaphore (`_writeLock`) across save / delete / backup. `MemoryStream` pre-serialize catches OOM before touching the primary file.

### Emergency Saves
Ctrl+C / disconnect path writes `emergency_<charname>_<timestamp>.json` (3 most recent retained per character). Restored by the recovery menu when the primary save fails to load.

### Format
JSON via `System.Text.Json` with `JsonNamingPolicy.CamelCase`, `IncludeFields = true`, `MaxDepth = 256`. `TolerantEnumConverter` accepts case-insensitive enum strings (forward-compat for moddable JSON).

### 7-Day Deleted Characters Archive
**Table**: `deleted_characters` (SQLite)

On any `DeleteGameData()`, the player_data JSON is archived for 7 days before the row is purged from `players`. `/restore` command (admin-only since v0.60.6) can reverse a deletion within the window. Permadeath uses `bypassArchive: false` so archived rows go through the same flow.

---

## 15. MUD Server Architecture

**Folder**: `Scripts/Server/` (entirely new since v0.29; authored across the v0.30-v0.60 arc)

### MudServer
**File**: `Scripts/Server/MudServer.cs`

Single-process multi-user game server. Listens on a configurable TCP port. Each accepted connection becomes a `PlayerSession` task running in parallel. Maintains:

- `ActiveSessions` dictionary keyed by lowercase username
- `IdleTimeout` (reads `DoorMode.IdleTimeoutMinutes` live since v0.60.7)
- `IdleWatchdogAsync` -- 30s loop, kicks idle players, sends 2-min warning before disconnect
- `BotDetectionSnapshotLoopAsync` -- 30s, dumps `BotDetectionSystem` snapshot to SQLite
- `AdminCommandPollerAsync` -- 3s, drains web admin command queue
- `DiscordBridgePollerAsync` -- 250ms, drains inbound Discord -> in-game
- `ServerSettingsApplyLoopAsync` -- 1s, drains `server_config_apply_queue` (v0.60.7)
- `ActiveBroadcast` -- volatile string for sysop's persistent banner
- `BroadcastToAll(msg, excludeUsername, channelKey)` -- per-channel mute respected
- Static delegate hooks: `KickActiveSessionHook` (called by `BanPlayer`), `PermadeathPurgeHook` (called by `PurgePlayerWorldState` for in-memory cleanup)

### PlayerSession
**File**: `Scripts/Server/PlayerSession.cs`

Per-connection state: `Username`, `ConnectionType` (Web / SSH / BBS / Steam / MUD / Local), `RemoteIP`, `IsPlainText`, `GmcpEnabled`, `IsCp437`, `IsGroupFollower`, `GroupLeaderSession`, `Spectators`, `SpectatingSession`, `MutedChannels`, `SuppressDisconnectSave`, `IdleWarningShown`. Wraps the `TerminalEmulator` and tracks idle time. On disconnect: emergency save (unless suppressed by permadeath), presence cleanup, group cleanup, spectator cleanup, GMCP shutdown.

### SessionContext
**File**: `Scripts/Server/SessionContext.cs`

Per-session AsyncLocal context: `Username`, `CharacterKey`, `RemoteIP`, `IsAdmin`, `IsIntentionalExit`, `Language`, `CompactMode`, `DisableCharacterMonsterArt`, `ScreenReaderMode`, `GmcpEnabled`. AsyncLocal lets session-scoped state propagate through async calls without explicit threading.

### Authentication Protocol

Two paths:

1. **Trusted AUTH (loopback only as of v0.60.6)**: 3-part `AUTH:user:type` form with no password. Used by the SSH gateway relay (`sshd-usurper` -> `--mud-relay` -> `127.0.0.1:4001`) and local BBS gateway. Validated against `IPAddress.IsLoopback(rawTcpPeer)` -- the raw socket peer cannot be spoofed by external clients (X-IP forwarded headers don't bypass the gate). External callers get `ERR:Authentication required. Trusted auth is only available from local relays.` and a logged rejection.

2. **Password AUTH (4-part)**: `AUTH:user:password:type` or `AUTH:user:password:REGISTER:type`. Routed through `SqlSaveBackend.AuthenticatePlayer` / `RegisterPlayer` with PBKDF2-SHA256 (100k iterations, 16-byte salt, constant-time comparison).

3. **Interactive auth**: raw telnet without an AUTH header times out the 500ms probe and falls into a register / login menu over TCP. Used by direct-MUD-client connections (Mudlet, MUSHclient, TinTin++).

The accept-time IP-ban check runs before any auth path; banned single IPs and CIDR rows immediately get a polite `Connection refused` and a closed socket.

### Telnet Negotiation
On interactive connect, server sends `IAC WILL ECHO` + `IAC WILL SGA` + `IAC WILL GMCP` (0xFF 0xFB 0x01 0x03 0xC9) in a single 9-byte packet. `ProbeTtypeAsync` reads back any `IAC SB TTYPE ...` and `IAC DO/DONT GMCP` for ~250ms in the same window. Detects: screen-reader / plain-text clients, CP437 BBS terminals, GMCP-capable clients.

### MudChatSystem
**File**: `Scripts/Server/MudChatSystem.cs`

Per-channel slash commands with rate limits, mute support, name resolution (greedy longest-prefix matching for multi-word display names like "Lumina Starbloom"), and broadcast routing. Channels: `say`, `tell`, `gossip`, `shout`, `gc` (guild), `who`, `news`, `group`, plus admin channels `accept`, `deny`, `spectators`, `nospec`, `pardon`, `restore`.

### Group System
**File**: `Scripts/Server/GroupSystem.cs`

Cooperative dungeon parties. Detailed in [Section 21](#21-group--spectator).

### Wizard Commands
**File**: `Scripts/Server/WizardCommandSystem.cs`

Tiered admin commands: `/restore`, `/freeze`, `/mute`, `/broadcast`, `/rage` (memorial event toggle), `/sysop`, etc. Audit-logged to `wizard_log`.

---

## 16. Online Multiplayer Schema

**File**: `Scripts/Systems/SqlSaveBackend.cs`

All tables auto-created via `CREATE TABLE IF NOT EXISTS` on backend init. Schema migrations via `ALTER TABLE ADD COLUMN` (idempotent).

### Core Tables

```sql
-- Player accounts and saves
CREATE TABLE players (
  username TEXT PRIMARY KEY,             -- Lowercase
  display_name TEXT,
  password_hash TEXT,                    -- PBKDF2-SHA256, 100k
  player_data TEXT,                      -- Full SaveGameData JSON
  created_at TEXT, last_login TEXT, last_logout TEXT,
  total_playtime_minutes INTEGER,
  is_banned INTEGER, ban_reason TEXT,
  noble_title TEXT,                      -- Sir / Dame / etc.
  last_login_ip TEXT,                    -- v0.60.5
  created_ip TEXT                        -- v0.60.5 (for reg rate-limit)
);

-- Shared world state (NPCs, royal court, economy, settlement, etc.)
CREATE TABLE world_state (
  key TEXT PRIMARY KEY, value TEXT,
  version INTEGER, updated_at TEXT, updated_by TEXT
);

-- Active sessions (120s heartbeat window)
CREATE TABLE online_players (
  username TEXT PRIMARY KEY, display_name TEXT, location TEXT,
  node_id TEXT, connection_type TEXT,    -- Web, SSH, BBS, Steam, MUD, Local
  connected_at TEXT, last_heartbeat TEXT,
  ip_address TEXT                        -- v0.60.5
);

-- Per-player sleeping state (Inn / Home / Castle)
CREATE TABLE sleeping_players (
  username TEXT PRIMARY KEY,
  sleep_location TEXT DEFAULT 'dormitory',
  sleeping_since TEXT,
  is_dead INTEGER,
  guards TEXT, inn_defense_boost INTEGER, attack_log TEXT
);
```

### Communication

```sql
CREATE TABLE messages (id, from_player, to_player, message_type, message, is_read, created_at);
CREATE TABLE news (id, message, category, player_name, created_at);
CREATE TABLE pvp_log (id, attacker, defender, levels, winner, gold_stolen, xp_gained, hp_remaining, rounds, created_at);
CREATE TABLE discord_gossip (id, direction, author, message, created_at, processed);
```

### Trading & Economy

```sql
CREATE TABLE trade_offers (id, from_player, to_player, items, gold, status, created_at);
CREATE TABLE auction_listings (id, seller, buyer, item_data, starting_bid, current_bid, expires_at, status);
CREATE TABLE bounties (id, target_player, placed_by, claimed_by, reward, ...);
```

### World Bosses & Permadeath Memorial

```sql
CREATE TABLE world_bosses (id, boss_data_json, current_hp, status, spawned_at);
CREATE TABLE world_boss_damage (id, boss_id, player_name, damage_dealt, ...);
```

### Wizard Tools

```sql
CREATE TABLE wizard_log (id, wizard_name, action, target, details, created_at);
CREATE TABLE wizard_flags (username PRIMARY KEY, is_frozen, is_muted, ...);
```

### Bans (v0.60.5)

```sql
CREATE TABLE banned_ips (
  ip_address TEXT PRIMARY KEY,           -- Single IP or CIDR
  reason TEXT, banned_at TEXT, banned_by TEXT,
  associated_username TEXT               -- Indexed; lifts on /unban
);
```

`IsIpBanned` does fast exact-match first, then scans CIDR rows (filtered by `LIKE '%/%'`). `CidrContains(cidr, ip)` does bit-mask comparison; supports IPv4 and IPv6.

Per-IP registration rate limit: `MaxRegistrationsPerIpPer24h = 3`, enforced in `RegisterPlayer`. Loopback IPs skipped.

### Permadeath Archive (v0.60.0)

```sql
CREATE TABLE deleted_characters (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  username TEXT, display_name TEXT,
  player_data TEXT,                      -- Archived JSON
  deleted_at TEXT, expires_at TEXT       -- 7-day retention
);
```

Indexed on `LOWER(username)` and `expires_at`. Opportunistic purge of expired rows on each delete.

### Server Settings (v0.60.7)

```sql
CREATE TABLE server_config (
  key TEXT PRIMARY KEY, value TEXT,
  updated_at TEXT, updated_by TEXT
);

CREATE TABLE server_config_schema (
  id INTEGER PRIMARY KEY,
  schema_json TEXT,                      -- Full ServerSettingsRegistry JSON
  published_at TEXT
);

CREATE TABLE server_config_apply_queue (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  key TEXT, value TEXT, requested_at TEXT
);
```

Detailed in [Section 17](#17-server-settings-registry).

### Bot Detection (v0.60.4)

```sql
CREATE TABLE bot_detection_snapshot (
  id INTEGER PRIMARY KEY,
  snapshot_at TEXT, snapshot_json TEXT
);
```

Single-row, UPSERTed every 30s.

### Guilds (v0.52.0)

```sql
CREATE TABLE guilds (id, name, leader_username, bank_gold, motd, created_at);
CREATE TABLE guild_members (guild_id, username, role, joined_at);
```

### Atomic Operations
- Gold theft via `json_set()` + `MAX(0, ...)` (no full save load required)
- Trade resolution: compare-and-set `UPDATE trade_offers SET status WHERE id AND status = 'pending'` with returned row count to determine winner of accept-vs-cancel races (v0.57.6 dup fix)
- World boss kill credit: `UPDATE world_bosses SET status = 'defeated' WHERE id AND status = 'active'` -- exactly one caller wins, becomes the killing-blow attributor (v0.57.6)

---

## 17. Server Settings Registry (Admin Tunables)

**File**: `Scripts/Systems/ServerSettingsRegistry.cs` (v0.60.7)

Schema-driven framework for admin-tunable runtime settings. Each setting is a `ServerSettingDescriptor`:

```csharp
public class ServerSettingDescriptor {
  string Key, Label, Category;
  SettingType Type;                      // Bool, Int, Float, String
  string DefaultValue;
  double? MinValue, MaxValue;
  int? MaxLength;
  string Description, ChangeImpact;
  Func<string> CurrentValue;             // Read from GameConfig
  Action<string> Apply;                  // Write to GameConfig
  Func<string, (bool ok, string? error)>? CustomValidator;
}
```

The registry's `All` list is the single source of truth. The same descriptor drives:
- Web UI form rendering (Bool -> select, Int/Float -> number input with HTML5 min/max, String -> text)
- Schema publish to `server_config_schema` (read by `web/ssh-proxy.js` GET endpoint)
- Server-side validation in `ServerSettingsRegistry.Validate(key, value)`
- Live apply via `ApplyConfigValue(key, value)` (drained from `server_config_apply_queue` every 1s by `MudServer.ServerSettingsApplyLoopAsync`)

### Phase 1 Settings (9 tunables)

**Death**:
- `default_starting_resurrections` (int 0-99, default 3)
- `online_permadeath_enabled` (bool, default true)

**Difficulty**:
- `xp_multiplier` (float 0.1-10.0)
- `gold_multiplier` (float 0.1-10.0)
- `monster_hp_multiplier` (float 0.1-10.0)
- `monster_damage_multiplier` (float 0.1-10.0; basic-attack-only by descriptor)

**Access**:
- `disable_online_play` (bool)
- `idle_timeout_minutes` (int 1-60)

**Communication**:
- `motd` (string max 500)

`max_dungeon_level` was deliberately excluded from the registry (capping below 100 breaks the Old God boss floors and ending sequence). It remains tunable via SysOpConfig file for BBS sysops.

### Apply Pipeline
```
Web admin POST /api/admin/server-settings/:key { value }
  -> Schema-side validation (web/ssh-proxy.js, mirrors C# Validate)
  -> UPSERT server_config (persists)
  -> INSERT server_config_apply_queue (live-apply request)
Game (every 1s):
  DrainServerConfigApplyQueue()
  -> for each row: ApplyServerConfigToGameConfig -> registry.ApplyConfigValue
  -> DELETE row
```

### Persistence on Startup
`SqlSaveBackend.LoadServerConfigIntoGameConfig()` runs in the backend constructor (after `InitializeDatabase`, before `PublishServerSettingsSchema`). Reads every `server_config` row, applies via the registry. Admin choices are in effect by the time the first session connects.

### BBS / SysOpConfig Bypass (v0.60.7)
`SysOpConfigSystem.LoadConfig()` and `SaveConfig()` early-return in MUD/online mode. The file-based config is BBS-only; MUD uses `server_config` as the single source of truth. Without this, the file load would overwrite registry-applied values on every session init (e.g. blanking the MOTD an admin had set via the web UI).

---

## 18. Permadeath & Resurrection

**File**: `Scripts/Systems/PermadeathHelper.cs`

Online characters start with `Resurrections = GameConfig.DefaultStartingResurrections` (admin-tunable, default 3). Each death decrements the counter and full-heals to 50% MaxHP. At 0, the next death is permadeath: the character file is deleted, archived in `deleted_characters` for 7 days, broadcast server-wide in red, and entered in the news feed.

### Three Death Entry Points
- `CombatEngine.HandlePlayerDeath` -- combat
- `LocationManager.HandlePlayerDeath` -- non-combat location event
- `GameEngine.HandleDeath` -- system-initiated

All three short-circuit on `SessionContext.IsIntentionalExit == true` (set by the first handler that processes the death) to prevent double-fire.

### Online vs Single-Player Behavior

- **Online + permadeath enabled (default)**: combat death decrements `Resurrections`. At 0 -> permadeath. Single auto-revive flow, no Temple/Deal/Accept menu.
- **Online + permadeath disabled** (admin toggle): combat death routes through the legacy `PresentResurrectionChoices` menu (Divine Intervention / Temple / Deal with Death / Accept Fate). Resurrection counter no longer consulted; no character is ever erased.
- **Single-player**: always uses the legacy menu.

### Arrest Combat
Royal Guards "subdue" murderers non-lethally. `Character.IsArrestCombat = true` flags the fight; `HandlePlayerDeath` short-circuits at HP=1 with `Outcome = PlayerEscaped`, no resurrection consumed, no permadeath check, no broadcast. Player is hauled to prison via `LocationExitException(GameLocation.Prison)`.

### World-State Purge on Permadeath
`SqlSaveBackend.PurgePlayerWorldState(username)` runs BEFORE `DeleteGameData` so joined queries still resolve the username. Single-transaction purge of 9 tables (guild_members, online_players, sleeping_players, wizard_flags, messages, trade_offers, bounties, auction_listings, world_boss_damage). In-memory hooks (via `PermadeathPurgeHook`) clear `GodSystem.playerGods[username]` (decrementing the god's believer count) and `RelationshipSystem` per-player NPC opinion cache. `pvp_log`, `wizard_log`, `news` deliberately preserved as historical record.

### Suppress-Disconnect-Save
Permadeath sets `SessionContext.IsIntentionalExit = true` and `PlayerSession.SuppressDisconnectSave = true` before deleting the row. Prevents the emergency-save-on-disconnect path from re-INSERTing the freshly-deleted character. Plus an in-memory blacklist (`SqlSaveBackend.MarkUsernameErased`) checked at the top of every `WriteGameData` call as belt-and-suspenders.

---

## 19. Bot Detection

**File**: `Scripts/Server/BotDetectionSystem.cs` (v0.60.0+)

Per-player combat-input cadence tracking. Rolling 30-sample window of inter-input intervals; tracks mean, stddev, consecutive-fast streaks, total flag count.

### Flag Thresholds (tunable in `GameConfig`)
- Mean interval below `BotMeanThresholdMs` (200ms) -> 1 flag
- Stddev below `BotStdDevThresholdMs` (50ms) -> 1 flag (low variance = bot)
- Consecutive-fast streak above `BotConsecutiveFastThreshold` (10) -> 1 flag

A session crossing all three thresholds simultaneously is "BOT_SUSPECT" (logged). Single-flag sessions show as "flagged" on the dashboard but no automated action is taken.

### Snapshot to DB (v0.60.4)
`BotDetectionSnapshotLoopAsync` in `MudServer` runs every 30s, writes the full `Snapshot()` (thresholds + per-session metrics) as JSON to a single `bot_detection_snapshot` row. The web admin polls `GET /api/admin/bot-stats` and renders a sortable table with per-cell flag-state highlighting.

### Read-Only by Design
The system is instrumentation-only. No automated kicks, throttles, or rate limits. The intent is to give the sysop visibility before tuning thresholds against real data. v0.57.21 prototyped a "press X to continue" anti-bot prompt and reverted same-day after community pushback; the lesson is to keep anti-automation server-side rather than as visible prompt friction.

---

## 20. GMCP Integration

**File**: `Scripts/Server/GmcpBridge.cs` (v0.60.0)

Generic MUD Communication Protocol support for Mudlet, MUSHclient, TinTin++, and other MUD clients with structured-data panes.

### Wire Format
Standard GMCP framing per spec: `IAC SB GMCP "Package.Name" SP "{json}" IAC SE` with 0xFF byte doubling inside SB. JSON payloads use `JsonNamingPolicy.CamelCase` for Achaea-compatible client scripts.

### Negotiation
Bundled into the existing TTYPE probe at session connect. Initial telnet packet extends from 6 bytes to 9 to add `IAC WILL GMCP` (0xFF 0xFB 0xC9). `ProbeTtypeAsync` reads back `IAC DO GMCP` or `IAC DONT GMCP` in the same 250ms window. `gmcpEnabled` flag flows through to `SessionContext.GmcpEnabled`.

### Packages Shipped
| Package | Trigger | Payload |
|---|---|---|
| `Char.Vitals` | Top of `BaseLocation.LocationLoop` (delta-tracked, only when changed) | `{ hp, maxHp, mp, maxMp, sp }` |
| `Char.Status` | Every location change | `{ name, class, level, race, gold, bank, xp, location }` |
| `Room.Info` | Every location change | `{ num, name, area, exits }` (exits placeholder) |
| `Comm.Channel.Text` | All chat broadcasts | `{ channel, talker, text }` |
| `Char.Death` | At top of `HandlePlayerDeath` | `{ killer, resurrectionsLeft }` |

Strict opt-in: SSH-relayed, web terminal, BBS, and Electron sessions skip GMCP entirely via early-return on `!ctx.GmcpEnabled`. Synchronous per-stream lock prevents interleaved frames.

### Not Shipped (Deferred)
`Char.Items` (inventory deltas), `Char.Skills`, full combat events, `Room.Exits` enumeration, `Core.Supports.Set` client-side package negotiation.

---

## 21. Group Cooperative Dungeons & Spectator Mode

### Group System
**File**: `Scripts/Server/GroupSystem.cs`

Cooperative dungeon parties for MUD mode. Form via `/group <player>`. Player accepts via `/accept`; declines via `/deny`. Group max size `GroupMaxSize = 4`, min level `GroupMinLevel = 5`, invite timeout `GroupInviteTimeoutSeconds = 60`, combat input timeout `GroupCombatInputTimeoutSeconds = 90`.

### Combat Architecture
Each player runs their own action loop with the full action set (spells with target selection, tactical actions, individual retreat, quickbar abilities). Round-by-round status broadcast to all members. Leader death doesn't end combat -- surviving players fight on. Monster special abilities target every party member. `BroadcastGroupCombatEvent`, `BroadcastGroupedPlayerAction`, `MonsterAttacksCompanion` all carry the dungeon-only filter so followers in town don't get spammed with combat lines.

### Loot
Dice-roll system. Each item rolls each player; higher roll wins. Per-item Pass / Equip / Take prompts to followers. Cascade through party if leader passes. Equipment comparison overlay on each prompt.

### XP / Gold Distribution
`DistributeGroupRewards` -- equal split across alive party members. Followers get same multipliers as leader (Blood Moon, NG+ cycle, settlement buffs, knight bonus, etc.). Catch-up XP for underleveled teammates (+10% per level gap, 4x cap). Per-slot percentage (auto-redistributed on teammate death).

### Spectator Mode
**Files**: `Scripts/Core/GameEngine.cs` (RunSpectatorModeUI, RunSpectatorLoop), `Scripts/UI/TerminalEmulator.cs` (`AddSpectatorStream`, `RemoveSpectatorStream`, `ForwardToSpectators`)

Live read-only viewport into another player's session. Pre-login menu offers `[S] Spectate a Player`. Target player accepts via `/accept`, denies via `/deny`. Once active: target's full terminal output streams to the spectator's session via `_spectatorStreams`. Multiple spectators per target supported. Target can `/spectators` (list), `/nospec` (disable). Spectators are exempt from the idle watchdog and excluded from global broadcasts. Web admin "snoop" feature uses the same plumbing (admin-side via SSE, target sees nothing).

---

## 22. Guild System

**File**: `Scripts/Systems/GuildSystem.cs` (v0.52.0)

Player-formed guilds with persistent membership and shared bank.

### Schema
```sql
guilds (id, name, leader_username, bank_gold, motd, created_at)
guild_members (guild_id, username, role, joined_at)
```

### Slash Commands (8)
`/guild`, `/gcreate`, `/ginvite`, `/gleave`, `/gkick`, `/ginfo`, `/gc` (guild chat), `/gbank`, `/gtransfer`. Invite flow uses `PendingGroupInvite` on `PlayerSession`; expires on timeout. Membership cache (`guildDisplayNameCache`) for `/who` rendering.

### Mechanics
- Guild XP bonus: +2% per member (cap +10%), applied in 3 XP paths in `CombatEngine`
- Item bank deposit / withdrawal with SQLite transaction (single-writer race fix v0.52.10)
- Guild leader transfer with broadcast (`gtransfer`)
- Public guild board on Main Street showing ranked guilds by member count, bank, online status

---

## 23. World Boss Raid System

**File**: `Scripts/Systems/WorldBossSystem.cs`, `Scripts/Data/WorldBossData.cs` (v0.48.2)

Server-wide raid bosses spawning automatically when 2+ players are online (1 boss per day, 1-hour fight window, 4-hour cooldown after defeat / expiry).

### 8 Bosses
Each with 3 phases, escalating abilities, themed loot. Spawned with `boss_data_json` snapshot for full data preservation across restarts.

### Combat
Each player runs their own combat loop against a **shared HP pool** atomically tracked via `SqlSaveBackend.RecordWorldBossDamage`. Returns `(remainingHp, wasKillingBlow)` -- exactly one player wins the kill credit via the conditional UPDATE described in Section 16. World-boss-exclusive loot affixes: `BossSlayer` (+10% damage vs bosses) and `TitanResolve` (+5% defense). Both work in regular combat too.

### Reward Tiers
By contribution share: MVP 3.0x, Top 3 2.5x, Top 25% 2.0x, Top 50% 1.5x, Any 1.0x. Plus +25 Fame for kill credit, +15 Fame for any contribution. Achievements: World Slayer, Boss Hunter, Legend Killer, MVP.

### Anti-Solo
Presence aura (5-8% MaxHP/round) plus boss attacks make solo attempts lethal in 4-5 rounds. 60s death cooldown prevents corpse-running.

---

## 24. Discord Bridge

**Files**: `Scripts/Systems/DiscordBridge.cs`, `web/ssh-proxy.js` (Discord bot side), shared SQLite `discord_gossip` table.

### Bidirectional Mirror
`/gossip` channel mirrors to a designated Discord channel (`#in-game-gossip`) and back. The C# game writes outbound rows to `discord_gossip` on `/gos`. The Node bot (running inside `usurper-web`) polls inbound from Discord, claims rows in a single transaction, and posts to in-game via `MudServer.BroadcastToAll`. Polling interval 250ms. Re-entrant `_discordPollInFlight` guard prevents overlap (the v0.57.13 hotfix that stopped duplicate posts).

### Configuration
`DISCORD_BOT_TOKEN` + `DISCORD_GOSSIP_CHANNEL_ID` env vars on `usurper-web` systemd service. Optional `DISCORD_STATS_CHANNEL_ID` for the auto-updating live `#server-status` embed (60s timer, edits in place, posts on bot ready, self-heals on missing message).

### Anti-Abuse
Inbound rate limit 1 msg / 3s per Discord user, 200-char truncate, `@everyone` / `@here` / role mentions neutered with zero-width space, control chars stripped. `allowedMentions: { parse: [] }` on outbound. Discord users appear in-game as `Name (Discord)`.

### Login / Logout Announcements
`DiscordBridge.QueueSystemEvent(message)` writes to `discord_gossip` with sentinel author `__SYSTEM__`. Node poll detects and renders as italic `*Name has entered the world.*` rather than the standard `**Name** *(in-game)*: msg` format. Wired into `OnlineStateManager.StartOnlineTracking` (after `RegisterOnline`) and `Shutdown`. Alt-character switching does not fire events.

### Discord Commands
`!who` / `!online` -- current online list (bypasses gossip relay path, no rate limit).
`!help` -- command list.

---

## 25. Story & Narrative Systems

### Story Progression
**File**: `Scripts/Systems/StoryProgressionSystem.cs`

11 chapters: Awakening -> FirstBlood -> TheStranger -> TheFirstSeal -> FactionChoice -> RisingPower -> TheWhispers -> FirstGod -> GodWar -> TheChoice -> Ascension -> FinalConfrontation -> Epilogue.

22+ story flags (bitmask) plus arbitrary string flags via `StoryProgressionSystem.SetStringFlag(key, value)`. Per-Old-God states: Unknown -> Imprisoned/Dormant/Dying/Corrupted/Neutral -> Awakened/Hostile/Allied -> Saved/Defeated/Consumed.

### Endings
**File**: `Scripts/Systems/EndingsSystem.cs`

5 endings:
1. **Usurper** (Dark) -- alignment < -300 OR destroyed 5+ gods -> consume all, become sole god
2. **Savior** (Light) -- alignment > 300 OR saved 3+ gods -> restore pantheon, join as equal
3. **Defiant** (Independent) -- balanced alignment, kill all gods, stay mortal
4. **True Ending** -- all 7 seals + Awakening 7 + companion grief + spared 2+ gods + balanced alignment -> become "The Bridge"
5. **Dissolution** (Secret) -- Cycle 3+ + completed 2+ endings + max awakening + truth revealed + all wave fragments -> dissolve into Ocean, save deleted

NG+ ending records persist in `MetaProgressionSystem.UnlockedEndings` (account-scope) AND `StoryProgressionSystem.CompletedEndings` (character-scope). Prestige class unlocks check the union (so a re-rolled character keeps prestige access from a previous immortal).

### Ocean Philosophy
**File**: `Scripts/Systems/OceanPhilosophySystem.cs`

8 awakening levels (0-7), 10 wave fragments, 13+ awakening moments. Triggered by companion grief, dream sequences, Old God boss outcomes, location-specific lore, Old God lore songs at Music Shop. Core theme woven through dialogue: "You are not a wave fighting the ocean. You ARE the ocean, dreaming of being a wave."

### Seven Seals
**File**: `Scripts/Systems/SevenSealsSystem.cs`

Floors 0 (town), 15, 30, 45, 60, 80, 99. Each reveals a fragment of the truth about Manwe and the broken pantheon. All 7 + max awakening unlocks the True Ending.

### Other Narrative Systems
- **DreamSystem** (~30+ dreams) -- triggered at Inn rest, scale with awakening level, narrative arcs (Amnesia, Companion Grief, Old God prophecy, etc.)
- **StrangerEncounterSystem** -- 10 Noctura disguises, escalating encounters
- **TownNPCStorySystem** -- 6 NPCs with 4-5 story stages each (Marcus, Elena, Aldric, Merchant's Daughter, Old Adventurer, Pip)
- **CycleDialogueSystem** -- NG+ aware (Cycle 1: normal, Cycle 2: deja vu, Cycle 5+: full acknowledgment)
- **CulturalMemeSystem** -- world-meme propagation across NPC populations
- **AmnesiaSystem** -- Vex-storyline disease that surfaces missing memories
- **GriefSystem** -- companion / NPC death consequences (combat penalties, dream triggers, awakening)
- **BetrayalSystem** -- NPC betrayal mechanics (loyalty, revenge, faction)
- **MoralParadoxSystem** -- complex moral choices with alignment implications
- **DivineBlessingSystem** -- prayer / lifesteal / combat buff via worshipped god
- **GodSystem** -- player-godhood and elder-god worship registry
- **ArtifactSystem** -- 7 Old God artifacts with stat bonuses and divine-armor scaling
- **MetaProgressionSystem** -- account-scope ending unlocks, NG+ starting bonuses, account achievements

---

## 26. Quest System

**File**: `Scripts/Systems/QuestSystem.cs`

### Quest Types
Combat (KillMonsters, KillSpecificMonster, KillBoss), Dungeon (ReachDungeonFloor, ClearDungeonFloor, ExploreRooms), Equipment (BuyWeapon, BuyArmor, BuyAccessory, BuyShield), Social (TalkToNPC, DeliverItem, Assassinate, Seduce, DefeatNPC), Collection (CollectGold, CollectItems, CollectPotions). Removed in v0.53.7: FindArtifact (impossible), RescueNPC (broken).

### Quest Difficulty (1-4)
Adjusts target monster level: `playerLevel + (difficulty - 2) * 3`, capped to `playerLevel +/- 10`. Floor objectives capped to player's accessible range AND `GameConfig.MaxDungeonLevel`.

### Quest Sources
- **Quest Hall** -- general quests + bounties
- **King's Bounties** -- monarch posts contracts on criminals / enemies
- **Royal Audience** -- per-king custom quests at Castle
- **Open Contract Bounties** -- kill any NPC with a bounty for instant payout (no claiming required)

### Daily Quest Limit (`MaxDailyQuests = 3`)
Tracked at claim time via `Character.QuestsToday`. Reset at daily reset.

### Quest Completion
Gold rewards reduced 7-10x in v0.52.5 (was overtuned). Failure penalties enabled. Abandoned quests serialize with `QuestStatus.Abandoned`.

---

## 27. Companion System

**File**: `Scripts/Systems/CompanionSystem.cs`

### 5 Companions

| Companion | Recruit Lvl | Role / Class | Can Die | Personal Quest |
|---|---|---|---|---|
| **Aldric** | 10 (Inn) | Tank / Warrior | Yes (moral trigger) | Confront demon from past (floors 55-65) |
| **Mira** | 20 (Healer) | Healer / Cleric | Yes (inevitable narrative) | Accept meaning of mercy (floors 40-50) |
| **Lyris** | 15 (Inn) | Hybrid / Ranger | Yes | Recover artifact for Aurelion (floors 80-90) |
| **Vex** | 25 (Inn) | DPS / Assassin | Yes (disease, ~30 days) | Complete before-I-die list (floor 70+ OR daysWithVex >= 10) |
| **Melodia** | 20 (Music Shop, v0.49.0) | Support / Hybrid Bard | Yes | The Lost Opus (floors 50-60) |

### Death Consequences
- **Mira**: narratively inevitable; teaches letting go
- **Vex**: wasting disease with timer
- **Aldric**: triggered by player's moral choice (sacrifice mechanic)
- **Lyris / Melodia**: combat death possible

Companion death triggers `GriefSystem`, which applies combat modifiers (damage / defense / resist) and contributes to Ocean Philosophy awakening. Grief is "live" only if the deceased is still dead -- if revived (via admin console), grief auto-clears (v0.57.9).

### Equipment Sync
`CompanionSystem.SyncCompanionEquipment(wrapper)` is called after every successful equip/unequip on a companion-Character wrapper. Without it, edits to the wrapper don't flow back to the real `Companion` because the wrapper is rebuilt on every `GetCompanionsAsCharacters()` call. Originally affected 9 surfaces (Inn / Home / Team Corner equip and unequip and take-all), all wired in v0.57.7. Combat-loot equip uses the same sync (v0.53.13).

### Companion AI
Per-companion preferred-weapon-types whitelist gates auto-pickup so Aldric won't auto-equip a bow (v0.57.7). Uses `Equipment.CanEquip()` for full validation including class restrictions. Comparison prompt asks the player before equipping the auto-pickup target.

### Specialization
Companions can be assigned a specialization at Team Corner. Drives ability priorities (75% weight), spec-role bonuses, AI behavior. Free swap.

---

## 28. Relationship & Family Systems

### Relationships
**File**: `Scripts/Systems/RelationshipSystem.cs`

Bidirectional tracking. Levels: Hated -> Normal -> Friendship (~40) -> Married (~200). Daily cap: 2 steps per NPC per day (bypassed for milestone events: confession, marriage). Minimum 7 days acquaintance before NPC accepts marriage proposal. NPC proposal acceptance based on personality (20-95%).

### Romance Tracker
**File**: `Scripts/Systems/RomanceTracker.cs`

Per-player relationship registry: Spouses, Lovers, FWBs, Exes, Crushes. Multi-spouse polyamory supported. `GetRelationType` consulted by mingle / flirt filters and dialogue gating. ExSpouses tracked separately so dead/divorced spouses aren't shown as eligible romance candidates.

### Marriage Sync
On marriage / divorce / death, four data stores must agree:
1. `RelationshipSystem` (bidirectional values)
2. `RomanceTracker` (per-player Spouses list)
3. `NPCMarriageRegistry` (NPC-NPC marriages)
4. `Character.IsMarried` / `SpouseName` flags

The v0.53.7 / v0.53.10 audit found 17 sync bugs across 5 passes. All four stores are now updated atomically through `RelationshipSystem.ProcessDivorce()`, `ChurchLocation.PerformMarriage()`, etc.

### Family
**File**: `Scripts/Systems/FamilySystem.cs`

Children provide stat bonuses: +50 HP, +5 STR, +3 CHA, +100 daily gold per child, +2% XP per child (cap +10%). Royal children +5 CHA and +500 gold. Children inherit traits from both parents. Aging via `NpcLifecycleHoursPerYear`. At 18, children become adult NPCs (`ConvertChildToNPC()`). Custody handled at divorce.

### CK-Style Parenting
24 age-appropriate moral dilemma scenarios at Home with Soul alignment effects. Each child has `LastParentingDay` (deprecated) / `LastParentingTime` (current, wall-clock since v0.57.6) cooldown. Birth alignment inheritance from parents.

### Factions
**File**: `Scripts/Systems/FactionSystem.cs`

Three factions: **The Crown** (Castle, requires Chivalry > 500, 10% shop discount), **The Shadows** (Dark Alley, requires Darkness > 200, 20% better fence prices), **The Faith** (Temple, 25% healing discount). NPCs can join and rotate based on alignment shifts.

### Castle Politics
**File**: `Scripts/Locations/CastleLocation.cs`, `Scripts/Core/King.cs`

Full political system: guard loyalty, treasury management, court factions (Loyalists, Reformists, Militarists, Merchants, Faithful), intrigue plots (assassination, coup, scandal, sabotage), political marriage, succession / heir system. NPC kings and player kings both have full systems. Knighthood (+5% damage / defense, "Sir" / "Dame" prefix) granted via Castle.

---

## 29. Achievement & Statistics

### Achievements
**File**: `Scripts/Systems/AchievementSystem.cs`

50+ achievements across 7 categories: Combat, Exploration, Economy, Social, Progression, Challenge, Secret.

5 tiers: Bronze, Silver, Gold, Platinum, Diamond. Tier-scaled Fame rewards.

Notable: `first_steps`, `monster_slayer_1000`, `boss_killer`, `level_100`, `married`, `ruler`, `pvp_veteran`, `nightmare_master`, `completionist`, `first_blood`, `guardian_slayer`, `dedicated_adventurer`, `devoted_champion`, `legendary_devotion`, `world_slayer`, `world_boss_first`, `world_boss_5_unique`, `world_boss_25_total`, `world_boss_mvp`. Steam achievements wired through `OSC` markers in online mode.

### Statistics
**File**: `Scripts/Systems/StatisticsSystem.cs`

Per-player tracker via `PlayerStatistics` methods:
```
RecordMonsterKill, RecordDamageDealt, RecordDamageTaken,
RecordPurchase, RecordGoldSpent, RecordSale, RecordGoldChange,
RecordLevelUp, RecordDungeonLevel,
RecordPotionUsed, RecordHealthRestored, RecordResurrection, RecordDiseaseCured,
RecordWorldBossKill, RecordWorldBossDamage, RecordWorldBossMVP
```

Session summary on logout shows duration, combat stats, progress, economy, exploration. Incremental playtime accumulation (no inflation on save).

---

## 30. Localization

**Files**: `Scripts/Systems/LocalizationSystem.cs` (`Loc.Get` API), `Localization/{en,es,fr,hu,it}.json`

### 5 Languages
English (~16,500 keys, source of truth), Spanish, French, Hungarian, Italian. Each language file loaded lazily on first `Loc.Get` call (auto-detected from `Localization/` directory).

### API
```csharp
Loc.Get("key", arg0, arg1, ...)         // Format args via {0}, {1}
Loc.Initialize()                         // Eager-load all languages at startup
```

Fallback chain: current language -> English -> raw key. Format args must match between languages. New en.json keys must get matching keys in all 5 files. Translation batch limit ~700 keys per agent (32K output token limit on Claude).

### Per-Session Language
`GameConfig.Language` is `AsyncLocal<string?>` so concurrent MUD players each see their own language. Language preference persists per-character in `PlayerData.Language`, restored on login.

### Compact Mode
Per-session via `SessionContext.CompactMode`. Toggled by `[Z]` key, `/compact` slash command, or in preferences. Uses tighter BBS-style menus. Independent of language and screen-reader mode.

### Screen Reader Mode
Strips box-drawing, decorative Unicode, color-bracketed menus. All locations have plain-text paths. Auto-detected on Windows console launches (skipped in WezTerm or door modes due to false-positive risk -- v0.60.7).

### Translation Quality
Hungarian (v0.52.13), Italian (v0.52.13), French (v0.52.13) fully translated from English placeholders with multiple audit passes. v0.60.0 French audit found ~10 genuine gaps (most "untranslated" keys turned out to be intentional cognates like "Combat", "Faction", "Confession"). UTF-8 defense in depth: `Console.OutputEncoding = UTF8` set explicitly at startup since v0.60.0 (protects against systemd-on-Linux services running without UTF-8 locale where `Console.Out` defaults to ASCII).

---

## 31. Terminal & UI Layer

### TerminalEmulator
**File**: `Scripts/UI/TerminalEmulator.cs` (~2500 lines)

Output modes (auto-detected):
1. **MUD Mode** -- ANSI to network stream; supports per-spectator forwarding; `_prevInputWasCR` flag fixes Mudlet's `\r\n` double-Enter
2. **BBS Socket Mode** -- routes through `BBSTerminalAdapter` -> `SocketTerminal` (CP437 for SyncTERM / NetRunner)
3. **BBS Native Winsock Mode** (v0.54.7) -- raw `send()`/`recv()` P/Invoke on inherited handle, bypasses .NET socket finalizers (fixes EleBBS / Mystic relaunch bug)
4. **stdio Mode** -- ANSI codes via Console for SSH, Synchronet stdio
5. **Console Fallback** -- `Console.ForegroundColor`
6. **Plain Text Mode** -- screen-reader-friendly; strips box drawing, Unicode, and color tags

**Color system**: 30+ named colors (black, white, red, green, yellow, blue, cyan, magenta + bright_* and dark_* variants). Inline markup: `[bright_red]text[/]`. BBS-compatible ANSI palette uses bold+color pairs instead of 90-97 extended range (v0.47.2).

**Input**: `GetInput`, `ReadLineAsync`, `PressAnyKey`, `GetMaskedInput` (passwords, including BBS support). Custom backspace handling for SSH / pipe-redirected stdin.

**Spectator forwarding**: `_spectatorStreams` set; output methods call `ForwardToSpectators(text)`. Markup is rendered to a string before duplication.

**ServerEchoes flag**: controls server-side echo for ReadLineInteractiveAsync. True only for direct raw TCP MUD clients (Mudlet, TinTin++, VIP Mud); false for all relay connections (Web, SSH) where the PTY handles echo.

### UIHelper
**File**: `Scripts/UI/UIHelper.cs`

Static utilities for 80-char box drawing:
```
╔══════════════════════════════════════════════════════════════════════════════╗
║  Content with padding                                                        ║
╠══════════════════════════════════════════════════════════════════════════════╣
║  [K] Menu Option                                                             ║
╚══════════════════════════════════════════════════════════════════════════════╝
```

Methods: `DrawBoxTop/Bottom/Line/Separator`, `DrawMenuOption`, `DrawStatBar`, `DrawBoxLabelValue`, `FormatNumber`, `FormatGold`, `WriteBoxHeader`, `WriteSectionHeader`. Compact-mode and SR-mode have their own simpler layouts.

### ANSI Art
**File**: `Scripts/UI/ANSIArt.cs`, `Scripts/UI/RacePortraits.cs`

Race / class portraits at character creation (5 races: Orc, Dwarf, Elf, Half-Elf, Troll, Gnoll, Gnome, Human, Hobbit, Mutant; 5 classes: Magician, Assassin, Warrior, Cleric, Paladin). Plus monster silhouettes, Old God boss reveal art, splash screen, Death art, Treasure / Boss Victory / Level Up event art. SR mode short-circuits at `DisplayArt` / `DisplayArtAnimated` to plain text.

### AccessibilityDetection
**File**: `Scripts/UI/AccessibilityDetection.cs`

Win32 wrapper for `SystemParametersInfo(SPI_GETSCREENREADER, ...)`. Returns true on Windows when NVDA / JAWS / Narrator is active. macOS / Linux always return false (no equivalent system flag).

---

## 32. BBS Door Mode

**Files**: `Scripts/BBS/DoorMode.cs`, `Scripts/BBS/SocketTerminal.cs`, `Scripts/BBS/BBSTerminalAdapter.cs`, `Scripts/BBS/DropFileParser.cs`

### Modes
1. **Native Winsock** (v0.54.7) -- raw `send`/`recv` P/Invoke on inherited socket handle. Bypasses .NET Socket / NetworkStream wrappers. Fixed long-standing relaunch bugs on EleBBS / Mystic. `WSAStartup(2.2)` + `WSAEWOULDBLOCK` retry pattern. `DrainNativeInputBuffer` with `MSG_PEEK` flushes telnet line-mode `\r\n`. `NativeExitProcess` skipped for stdio-mode (NFU, Synchronet) since the child owns no socket.
2. **Socket I/O (legacy)** -- direct TCP socket from DOOR32.SYS handle. For unencrypted telnet (EleBBS, Mystic telnet).
3. **stdio Mode** (`--stdio`) -- stdin/stdout with ANSI. Required for SSH, Synchronet stdio, GameSrv, ENiGMA, WWIV.
4. **FOSSIL/Serial** (`--fossil`) -- COM port for legacy BBS software.

### Auto-Detection
DOOR32.SYS BBS-name auto-detection: Synchronet, GameSrv, ENiGMA, WWIV -> auto-enable stdio mode. `Console.IsInputRedirected` / `IsOutputRedirected` catches SSH / pipe-based transports. CP437 encoding auto-set for Synchronet stdio (`Console.OutputEncoding = CodePagesEncodingProvider.Instance.GetEncoding(437)`) to avoid garbled box-drawing.

### Drop File Parsing
`BBSSessionInfo`: `CommType`, `SocketHandle`, `UserName`, `SecurityLevel`, `TimeLeft`, `BBSName`, `Emulation`, `ScreenWidth`, `ScreenHeight`.

### Save Isolation
BBS saves stored in `saves/{BBSName}/` to prevent cross-BBS conflicts. Single-player saves in `saves/` (no namespace).

### Critical SSH Pitfall
BBS passes raw TCP socket handle for BOTH telnet and SSH. Writing to the raw socket on SSH bypasses encryption -> "Bad packet length" errors. Mitigation: `Console.IsInputRedirected` detection auto-routes SSH BBSes to stdio mode.

### `--auto-provision`
Trusted BBS sysops can pass `--auto-provision` so BBS-authenticated users get their game-server account auto-created on first connect. Only valid in trusted-AUTH path (loopback only as of v0.60.6).

---

## 33. Steam Integration

**File**: `Scripts/Systems/SteamIntegration.cs`

Steamworks.NET wrapper for Steam achievements and lifecycle. Initialized in `Console/Bootstrap/Program.cs` BEFORE the door-mode branch so Steam is active for both standard console mode and the WezTerm-bundled `--local` Steam launch path. `steam_appid.txt` shipped in builds; auto-detected on first launch.

### Achievement Sync
In-game achievements that should sync to Steam are tagged via `OSC` markers in online mode. The Steam build path picks them up and calls `SteamUserStats.SetAchievement()`. Tier-Gold-and-above achievements broadcast server-wide AND to Steam.

### `steam_appid.txt` Pitfall
Stray `steam_appid.txt` in BBS game directories used to trigger the "Steam handles updates" message on auto-update (false positive). Fixed in v0.49.7: `VersionChecker` now skips Steam-build detection in BBS door mode regardless of the file's presence.

---

## 34. WezTerm Desktop Launcher

**Files**: `wezterm.lua`, `launchers/Play.bat`, `launchers/play.sh`, `launchers/Play-Accessible.bat`, `launchers/play-accessible.sh`

### Bundled Terminal
Standalone and Steam builds ship with WezTerm AppImage / .exe + 7 monospace fonts (JetBrains Mono, Cascadia Code, Fira Code, Iosevka, Hack). Double-click `Play.bat` / `play.sh` launches the game in a branded terminal window with custom title bar, dark navy / gold theme, integrated buttons, no tab bar.

### Font Selection
In-game `[7] Terminal Font` preference writes `font-choice.txt`; WezTerm reads and auto-reloads. Font scaling 0.85x-1.4x persists in localStorage (Electron client) or via the same prefs file.

### Linux Launcher Fallback
`play.sh` rewritten in v0.52.6 with FUSE-fallback chain: WezTerm AppImage -> WezTerm with `APPIMAGE_EXTRACT_AND_RUN=1` -> system terminal emulators (gnome-terminal, konsole, alacritty, kitty, xterm) -> direct execution. Same chain in `play-accessible.sh` plus `--screen-reader` flag.

### Steam Integration
Steam launches via `Play.bat`. Title screen shows "Press any key" then enters the splash. Pre-character-load preferences (font, language, etc.) loaded from per-player credentials file. Saved characters' explicit `ScreenReaderMode` wins on character load.

---

## 35. Electron Graphical Client (Beta)

**Folder**: `electron-client/`

Optional Electron-based graphical client with Darkest Dungeon-inspired combat UI. Spawns the standard game binary as a child process with `--electron`, parses ANSI + OSC sequences via `src/ansi-parser.js`, and renders structured events into HTML overlays.

### Structured Events
`Scripts/UI/ElectronBridge.cs` emits `OSC]1337;usurper:{json}BEL` sequences (invisible in regular terminals). JS parser extracts the JSON payload and routes to `GameUI.handleGameEvent()`.

### Rendered Surfaces
- **Combat**: DD-style battlefield with party left, monsters right, ability bar at bottom, combat log
- **Dungeon Map**: canvas grid with corridor lines
- **Inventory**: paperdoll + backpack; equip flow uses command-protocol pattern (multi-step C# <-> JS)
- **Character Status**: portrait + stats + attribute grid
- **Party Management**: member cards with portraits and HP bars
- **Potions Menu**: HP bars + action buttons
- **Dungeon Room**: info panel with features, exits, all action buttons
- **Settings overlay** (`/settings`, `/set` slash commands)
- **Audio**: Web Audio receiver with sfx / music / ui channel mixer; lazy `.ogg` loading; emits at level-up, death, achievement unlock, boss phase transitions

### HD Sprites
`electron-client/generate-sprites.js` calls PixelLab API (192px transparent backgrounds). Fallback chain: HD -> east-facing -> default -> placeholder. 17 classes + 15 monsters in `assets/classes-hd/` and `assets/monsters-hd/`.

### Localization
`electron-client/src/i18n.js` with `window.i18n.t(key, ...args)` API. Embedded English baseline (~75 keys). Translations dropped into `electron-client/lang/{es,fr,hu,it}.json`.

### Status
Beta. Text mode is the supported way to play; Electron is optional. Phase 9.5 polish landed in v0.60.0.

---

## 36. Website & Web Proxy

### Landing Page
**File**: `web/index.html`

Dark-themed responsive page with:
- Embedded xterm.js 5.5.0 web terminal (WebSocket -> SSH bridge)
- Live stats dashboard (`/api/stats`, 30s cache)
- SSE live feed (`/api/feed`) for NPC activity and player news
- PvP leaderboard, Hall of Fame, event color coding
- Game lore: story, Old Gods, classes, races, companions, endings, BBS history, Founder Hall

### Web Proxy
**File**: `web/ssh-proxy.js` (Node.js, port 3000)

- **HTTP**: serves `/api/stats`, `/api/feed` (SSE), and `/api/admin/*` family
- **WebSocket bridge**: browser -> SSH to localhost:4000 (game)
- **Stats API**: 30s server-side cache + 15s browser cache
- **SSE feed**: 5s poll of `news` table with high-water marks; auto-reconnect after 5s
- **Discord bot**: discord.js v14 bidirectional gossip relay (Section 24)
- **Live `#server-status` embed**: 60s timer, edits in place
- **Dependencies**: ws, ssh2, better-sqlite3, discord.js

### Stats API Response
```json
{
  "online": [...],            // Current players with connectionType, level, class, location, IP
  "onlineCount": N,
  "stats": { totalPlayers, totalKills, avgLevel, ... },
  "highlights": { topPlayer, king, popularClass },
  "leaderboard": [...],       // Top 25 by level/XP, with rank, isOnline
  "news": [...]
}
```

### NPC Analytics Dashboard ("The Observatory")
**File**: `web/dashboard.html`

Public dashboard at `/dashboard`. Real-time observation of the NPC simulation:
- World Map: location grid with NPC dots colored by faction, sized by level
- NPC Detail: personality radar (13 traits), emotional state bars, goals, memories, relationships
- Demographics: class / race / faction doughnut charts, level histogram (Chart.js)
- Relationship Network: D3.js force-directed graph
- Live Event Timeline: SSE feed via `/api/dash/feed`
- Trend Charts: events/hour, alive/dead breakdown
- 8 endpoints under `/api/dash/`

---

## 37. Web Admin Dashboard

**File**: `web/admin.html`

Sysop-only dashboard at `/admin`. Bearer-token auth (admin balance token). Sections:

### Overview
System uptime, web proxy uptime, memory usage, online players, total players, WS connections, SSE clients.

### Bot Detection (v0.60.4)
Sortable table from `/api/admin/bot-stats`. Per-player metrics: mean interval, stddev, consecutive-fast count, total flags. Suspect sessions float to top in red, flagged-but-not-suspect in amber. Per-cell threshold-meeting highlight.

### Banned IPs (v0.60.5)
Table of `banned_ips` rows: IP / CIDR, reason, associated account, banned-at, banned-by, Lift button. "+ Ban IP" modal accepting single IPs or CIDR. Player panel surfaces `connected from` (online) or `last login from` (offline) IP info.

### Server Settings (v0.60.7)
Schema-driven form rendered from `/api/admin/server-settings`. Per-row Save buttons. Bool -> select, Int/Float -> number with min/max, String -> text with maxlength. Refreshes on the 30s dashboard cycle.

### Player Management
List / search / filter players. Edit any field via dot-path JSON editor, raw JSON edit, ban / unban / kick / reset password / delete.

### Live Snoop
Open SSE stream of any player's terminal output. 20s keepalive ping (v0.60.5 fixed dead-screen-after-60s issue).

### Other Sections
Database (table sizes, integrity), SSL cert info, recent logins, game news, services (systemd status), wizard log audit trail.

---

## 38. Server Infrastructure

### Architecture
```
Browser -> https://usurper-reborn.net (nginx, SSL via Let's Encrypt)
  ├── Static (web/index.html, dashboard.html, admin.html)
  ├── /api/* -> Node (port 3000) -> SQLite
  └── /ws -> WebSocket -> SSH session -> game (port 4000 via sshd-usurper)

sslh (port 4000) -> SSH to port 4022, raw TCP to port 4001
sshd-usurper (port 4022) -> ForceCommand --mud-relay --mud-port 4001
usurper-mud (port 4001) -> game server (multi-user, SQLite-backed)
```

### Nginx
**File**: `scripts-server/nginx-usurper.conf`

Static files from `/opt/usurper/web/`. `/api/*` -> `127.0.0.1:3000`. `/ws` -> WebSocket with 24-hour timeout. SSL via Let's Encrypt certbot (auto-renewing). `/api/` block tightened in v0.60.5 for SSE: `proxy_buffering off`, `proxy_cache off`, `proxy_http_version 1.1`, `proxy_read_timeout 600s`, `proxy_send_timeout 600s`. `/api/releases/latest` exempted from HTTPS redirect (Win7 TLS fallback path; v0.49.7).

### Systemd Services
- **usurper-web.service** -- Node web proxy (port 3000)
- **usurper-mud.service** -- game server (port 4001), `HeapHardLimit=512MB` in `runtimeconfig.template.json`
- **sshd-usurper.service** -- SSH daemon on port 4022, ForceCommand to `--mud-relay`
- **usurper-world.service** -- DEPRECATED (world sim folded into usurper-mud since v0.60.0)

Discord bot env vars: `DISCORD_BOT_TOKEN`, `DISCORD_GOSSIP_CHANNEL_ID`, optional `DISCORD_STATS_CHANNEL_ID`. Stored in `/etc/systemd/system/usurper-web.service.d/discord.conf` (600 perms).

### Deployment
- **Standard recipe**: publish linux-x64 self-contained -> tar -> scp -> stop usurper-mud + sshd-usurper -> extract -> chmod / chown -> start usurper-mud (after 2s) -> start sshd-usurper
- **Web changes**: scp web files -> systemctl restart usurper-web (no game restart needed)
- **`/opt/usurper/version.txt`**: written on every deploy, surfaced by admin dashboard

### Server Paths
| Path | Content |
|---|---|
| `/opt/usurper/UsurperReborn` | Game binary |
| `/opt/usurper/version.txt` | Current deployed version |
| `/opt/usurper/web/` | Website (index.html, admin.html, ssh-proxy.js, dashboard.html, language packs) |
| `/var/usurper/usurper_online.db` | SQLite database |
| `/var/usurper/sysop_config.json` | BBS-mode SysOpConfig (bypassed in MUD mode since v0.60.7) |
| `/var/usurper/logs/debug.log` | Debug log (auto-rotates at 5MB; 4 archives kept) |
| `/var/usurper/backups/` | Daily SQLite backups (14-day retention, 4 AM cron) |
| `/etc/nginx/sites-available/usurper` | Nginx site config |

### Deployment Scripts
- `setup-server.sh` -- full server bootstrap (users, dirs, services, firewall, fail2ban)
- `update-server.sh` -- deploy new binary with backup + restart
- `backup.sh` -- daily SQLite backup
- `healthcheck.sh` -- system health (DB, players, disk)

---

## 39. CI/CD Pipeline

**File**: `.github/workflows/ci-cd.yml`

### Jobs
1. **Test** -- build + unit tests (`Tests/Tests.csproj`)
2. **Build** -- publish 6 platforms (Windows x64/x86, Linux x64/ARM64, macOS Intel/Apple Silicon)
3. **Build-desktop** (v0.48.0+) -- bundle WezTerm + fonts + Play.bat / play.sh -> 3 desktop artifacts
4. **Smoke Tests** -- `--help` on target platforms
5. **Release** -- attach 9 artifacts to GitHub release (6 plain + 3 desktop)
6. **Steam** -- build with `STEAM_BUILD` flag, extract Steamworks native libraries, bundle WezTerm + fonts + launchers

### Build Command
```bash
dotnet publish usurper-reloaded.csproj -c Release -r <rid> --self-contained -o publish/<rid>
```

`--self-contained` is mandatory; without it the binary won't run on machines without .NET 8 runtime installed.

### IL Trimming (Disabled)
`PublishTrimmed` is intentionally false. Trimming removes runtime-reflected methods (`Path.GetExtension`, `DbConnection.OpenAsync`) that the BBS deploy path relies on. v0.51.0 disabled it permanently.

### Project Configuration
**File**: `usurper-reloaded.csproj`

- Target: .NET 8.0
- NuGet: Newtonsoft.Json, Microsoft.Data.Sqlite, SSH.NET, System.IO.Ports, System.Text.Encoding.CodePages
- Steam: Steamworks.NET + native libraries (steam_api64.dll, libsteam_api.so)
- Configurations: Debug, Release, Steam, SteamRelease

---

## 40. Configuration & Constants

**File**: `Scripts/Core/GameConfig.cs` (~2500 lines)

### Version
```csharp
Version = "0.60.7"
VersionName = "Beta"
```

### Core Constants
```
MaxLevel = 100               (was 200, reduced in v0.53.7)
TurnsPerDay = 325
MaxTeamMembers = 5
MaxDungeonLevel = 100
DailyDungeonFights = 10
DailyPlayerFights = 3
MaxPvPAttacksPerDay = 5
MinPvPLevel = 5
PvPGoldStealPercent = 0.10
PvPLevelRangeLimit = 20
NpcLifecycleHoursPerYear = 9.6
AlignmentCap = 1000          (v0.57.12)
MaxRegistrationsPerIpPer24h = 3   (v0.60.5)
SoftTauntStickChance = 75    (v0.60.3)
PermadeathRaceFloor = 3
WorldBossSpawnCooldownHours = 4.0
DefaultStartingResurrections = 3   (admin-tunable, v0.60.7)
OnlinePermadeathEnabled = true     (admin-tunable, v0.60.7)
```

### Admin-Tunable (via Server Settings Registry, v0.60.7)
`DefaultStartingResurrections`, `OnlinePermadeathEnabled`, `XPMultiplier`, `GoldMultiplier`, `MonsterHPMultiplier`, `MonsterDamageMultiplier`, `DisableOnlinePlay`, `IdleTimeoutMinutes`, `MessageOfTheDay`. (`MaxDungeonLevel` deliberately NOT exposed -- breaks story.)

### Class Starting Attributes
12 base classes + 5 prestige with HP, STR, DEF, STA, AGI, CHA, DEX, WIS, INT, CON, Mana base values and per-level growth.

### Race Attributes
10 races with HP/STR/DEF/STA bonuses, min/max age, height, weight, appearance.

### Race Lifespans
Human 75, Hobbit 90, Elf 200, HalfElf 120, Dwarf 150, Troll 60, Orc 55, Gnome 130, Gnoll 50, Mutant 65.

---

## 41. Enums Reference

### Character Enums
```csharp
CharacterClass {
  Alchemist=0, Assassin, Barbarian, Bard, Cleric,
  Jester, Magician, Paladin, Ranger, Sage, Warrior,
  Tidesworn, Wavecaller, Cyclebreaker, Abysswarden, Voidreaver,
  MysticShaman = 16
}
CharacterRace { Human, Hobbit, Elf, HalfElf, Dwarf, Troll, Orc, Gnome, Gnoll, Mutant }
CharacterAI { Computer='C', Human='H', Civilian='N' }
CharacterSex { Male=1, Female=2 }
RomanticOrientation { Straight, Gay, Bisexual, Asexual }
ClassSpecialization { ... 24 specs ... }
SpecRole { Tank, DPS, Healer, Utility, Debuff }
```

### Item & Equipment
```csharp
ObjType { Head=1, Body, Arms, Hands, Fingers, Legs, Feet, Waist, Neck, Face, Shield,
          Food, Drink, Weapon, Abody/Cloak, Magic, Potion=17 }
EquipmentSlot { Head=1, Body, Arms, Hands, Legs, Feet, Waist, Neck, Neck2, Face,
                Cloak, LFinger, RFinger, MainHand=14, OffHand=15 }
EquipmentRarity { Common, Uncommon, Rare, Epic, Legendary, Artifact }
WeaponHandedness { OneHanded=1, TwoHanded=2, OffHandOnly=3 }
WeaponType { Sword=1, Axe, Mace, Dagger, Rapier, Staff, Bow, Crossbow, Spear,
             Greatsword, Greataxe, Hammer, Maul, Flail, Buckler, Shield, TowerShield, Instrument }
ArmorWeightClass { Light, Medium, Heavy }
```

### Locations
```csharp
GameLocation {
  MainStreet=1, TheInn=2, DarkAlley=3, Church=4, WeaponShop=5, Master=6, MagicShop=7,
  Dungeons=8, ArmorShop=18, Bank=19, News=20, Healer=21, Marketplace=22,
  AnchorRoad=27, Dormitory=26, TeamCorner=41, Temple=47, Castle=70, QuestHall=75,
  LoveCorner=77, Prison=90, PrisonWalk=94, LoveStreet=200, Home=201,
  Pantheon, Settlement, Wilderness, MusicShop=503, GodWorld=400,
  SysOpConsole=500, Arena=501, CharacterCreation
}
```

### Combat
```csharp
CombatActionType { Attack, Defend, Heal, CastSpell, UseAbility, Backstab, Retreat,
                   PowerAttack, Rage, Smite, Disarm, Taunt, Hide, RangedAttack,
                   AidAlly, BegForMercy, UseHerb, Status, ... }
CombatOutcome { Victory, Loss, Fled, PlayerEscaped }
CombatSpeed { Normal=0, Fast=1, Instant=2 }
DifficultyMode { Easy, Normal, Hard, Nightmare }
StatusEffect { Stunned, Frozen, Confused, Sleep, Feared, Slowed, Marked, Cursed,
               Diseased, Burning, Bleeding, Poisoned, Paralyzed, Lifesteal, ... }
```

### Story
```csharp
StoryChapter { Awakening, FirstBlood, TheStranger, TheFirstSeal, FactionChoice,
               RisingPower, TheWhispers, FirstGod, GodWar, TheChoice, Ascension,
               FinalConfrontation, Epilogue }
EndingType { Usurper, Savior, Defiant, TrueEnding, Dissolution }
OldGodType { Maelketh, Veloura, Thorgrim, Noctura, Aurelion, Terravok, Manwe }
GodStatus { Unknown, Imprisoned, Dormant, Dying, Corrupted, Neutral, Awakened,
            Hostile, Allied, Saved, Defeated, Consumed }
ArtifactType { CreatorsEye, SoulweaversLoom, ScalesOfLaw, ShadowCrown,
               SunforgedBlade, Worldstone, VoidKey }
SealType { Creation, FirstWar, Corruption, Imprisonment, Prophecy, Regret, Truth }
```

### Daily / Time
```csharp
DailyCycleMode { SessionBased, RealTime24Hour, Accelerated4Hour,
                 Accelerated8Hour, Accelerated12Hour, Endless }
ConnectionType { Local, Telnet, SSH, BBS, Web, Steam, MUD }
SettingType { Bool, Int, Float, String }    // ServerSettingsRegistry
RecruitmentBand { Hidden, Friend, Neutral, Rival, Refused }   // Team recruitment
```

---

## 42. File Structure

```
Console/Bootstrap/
  Program.cs                       Entry point, mode selection, accessibility detect

Scripts/
├── Core/
│   ├── GameEngine.cs               Singleton, main game loop, init, save load
│   ├── GameConfig.cs               Constants, class/race attrs, version, admin tunables
│   ├── Character.cs                Base class (~2000 lines) + all enums
│   ├── Player.cs                   Player-specific state
│   ├── NPC.cs                      NPC with AI brain, lifecycle
│   ├── Monster.cs                  Combat entity (standalone)
│   ├── Items.cs                    Legacy Pascal item system
│   ├── Equipment.cs                Modern RPG equipment
│   ├── Item.cs                     (deprecated, dual-class merged)
│   ├── King.cs                     Monarch / treasury / guards
│   └── Quest.cs                    Quest data model
│
├── Server/                         MUD multiplayer server
│   ├── MudServer.cs                TCP listener, sessions, watchdogs, pollers
│   ├── PlayerSession.cs            Per-connection state
│   ├── SessionContext.cs           AsyncLocal session context
│   ├── MudChatSystem.cs            Slash-command channels
│   ├── GroupSystem.cs              Cooperative dungeon parties
│   ├── BotDetectionSystem.cs       Combat-input cadence tracking
│   ├── GmcpBridge.cs               GMCP wire-format and emit
│   ├── RelayClient.cs              --mud-relay implementation
│   ├── RoomRegistry.cs             Per-location presence
│   ├── WizNet.cs                   Wizard / admin tooling
│   ├── WizardCommandSystem.cs      Wizard commands (/restore, /freeze, /broadcast)
│   └── WizardLevel.cs              Tier definitions
│
├── Systems/                        ~106 game systems
│   ├── LocationManager.cs          Location routing & navigation graph
│   ├── SaveSystem.cs               Pluggable backend dispatcher
│   ├── SaveDataStructures.cs       All serialization data classes
│   ├── SaveFileRepair.cs           Bloated-save in-place repair
│   ├── FileSaveBackend.cs          Local JSON saves
│   ├── SqlSaveBackend.cs           SQLite for online / MUD (~6000 lines)
│   ├── IOnlineSaveBackend.cs       Backend interface
│   ├── ServerSettingsRegistry.cs   v0.60.7 admin-tunable framework
│   ├── PermadeathHelper.cs         Online death + permadeath
│   ├── CombatEngine.cs             Turn-based combat (~28000 lines)
│   ├── DailySystemManager.cs       Daily resets, time-of-day, maintenance
│   ├── WorldSimulator.cs           Background NPC simulation
│   ├── WorldSimService.cs          (deprecated headless service)
│   ├── SpellSystem.cs              Spell casting mechanics
│   ├── SpellLearningSystem.cs      Spell learning / training
│   ├── ClassAbilitySystem.cs       Class abilities (stamina-based)
│   ├── MonsterGenerator.cs         Level-scaled monster creation
│   ├── QuestSystem.cs              Quest generation & tracking
│   ├── AchievementSystem.cs        50+ achievements
│   ├── StatisticsSystem.cs         Player stats tracking
│   ├── RelationshipSystem.cs       NPC relationships, daily caps
│   ├── RomanceTracker.cs           Per-player romance registry
│   ├── NPCMarriageRegistry.cs      NPC-NPC marriages
│   ├── FamilySystem.cs             Children, aging, parenting
│   ├── IntimacySystem.cs           Romance encounter system
│   ├── CompanionSystem.cs          5 recruitable companions
│   ├── FactionSystem.cs            3 factions
│   ├── StoryProgressionSystem.cs   Main narrative tracking
│   ├── EndingsSystem.cs            5 game endings
│   ├── OceanPhilosophySystem.cs    Spiritual awakening
│   ├── SevenSealsSystem.cs         Collectible lore fragments
│   ├── GriefSystem.cs              Companion death consequences
│   ├── BetrayalSystem.cs           NPC betrayal mechanics
│   ├── MoralParadoxSystem.cs       Complex moral choices
│   ├── DreamSystem.cs              Dreams at Inn / Home
│   ├── StrangerEncounterSystem.cs  Noctura disguises
│   ├── TownNPCStorySystem.cs       6 NPCs with story arcs
│   ├── CycleDialogueSystem.cs      NG+ aware dialogue
│   ├── CityControlSystem.cs        Economic / tax control
│   ├── TournamentSystem.cs         Combat tournaments
│   ├── NewsSystem.cs               Event feed (file + SQLite)
│   ├── DebugLogger.cs              File-based debug logging
│   ├── OnlineStateManager.cs       Player presence tracking
│   ├── OnlineChatSystem.cs         Cross-location messaging
│   ├── OnlineAuthScreen.cs         Login/register UI
│   ├── OnlinePlaySystem.cs         Online Play menu (server picker)
│   ├── OnlineAdminConsole.cs       In-game admin console
│   ├── NPCSpawnSystem.cs           NPC management singleton
│   ├── CharacterCreationSystem.cs  New character flow
│   ├── WorldInitializerSystem.cs   World init at first start
│   ├── WorldBossSystem.cs          World boss raids
│   ├── WorldEventSystem.cs         Blood Moon, NG+ modifiers
│   ├── GuildSystem.cs              Player guilds
│   ├── AlignmentSystem.cs          Chivalry/Darkness paired movement
│   ├── FounderStatueSystem.cs      Alpha-era memorial statues
│   ├── DiscordBridge.cs            C# side of Discord bridge
│   ├── DialogueSystem.cs           NPC dialogue trees
│   ├── VisualNovelDialogueSystem.cs  Visual-novel style romance
│   ├── OpeningStorySystem.cs       Pre-game intro
│   ├── OpeningSequence.cs          Title sequence
│   ├── DailyLoginRewardSystem.cs   Streak bonuses (in DailySystemManager)
│   ├── HintSystem.cs               Onboarding hints
│   ├── DifficultySystem.cs         Difficulty multipliers
│   ├── TrainingSystem.cs           Skill / proficiency training
│   ├── ChallengeSystem.cs          Throne challenge
│   ├── PuzzleSystem.cs             Lever / riddle / memory rooms
│   ├── SettlementSystem.cs         Outskirts NPC settlement
│   ├── DivineBlessingSystem.cs     Prayer / lifesteal
│   ├── GodSystem.cs                Pantheon registry
│   ├── ArtifactSystem.cs           7 artifacts + divine armor
│   ├── AmnesiaSystem.cs            Memory recovery story
│   ├── MetaProgressionSystem.cs    Account-scope progression
│   ├── BotDetectionSystem.cs       (in Scripts/Server/)
│   ├── BugReportSystem.cs          ! key reports to Discord
│   ├── StreetEncounterSystem.cs    Random Main Street encounters
│   ├── NPCPetitionSystem.cs        Royal audience flows
│   ├── FeatureInteractionSystem.cs DC-roll feature checks
│   ├── StatEffectsSystem.cs        Stat -> effect mappings (crit chance, etc.)
│   ├── PrisonActivitySystem.cs     Prison day-tasks
│   ├── MarketplaceSystem.cs        Auction / trade
│   ├── CulturalMemeSystem.cs       NPC meme propagation
│   ├── SocialInfluenceSystem.cs    Charisma / reputation effects
│   ├── TeamBalanceSystem.cs        Team composition heuristics
│   ├── MaintenanceSystem.cs        Daily admin maintenance tasks
│   ├── UsurperHistorySystem.cs     In-game history reader
│   ├── RageEventSystem.cs          Alpha-era memorial event (dormant)
│   ├── MetaProgressionSystem.cs    Cross-character account progression
│   └── LocalizationSystem.cs       Loc.Get + JSON loaders
│
├── Locations/                      33 location files
│   ├── BaseLocation.cs             Abstract base (~3000 lines)
│   ├── MainStreetLocation.cs       Central hub (post-v0.60.7: trimmed to ~3000 lines)
│   ├── DungeonLocation.cs          100-floor dungeon (~13000 lines)
│   ├── CastleLocation.cs           Politics, throne, court
│   ├── InnLocation.cs              Rest, companions, tournaments
│   ├── HomeLocation.cs             5-tier home upgrades, family
│   ├── ArenaLocation.cs            PvP combat
│   ├── TempleLocation.cs           Gods, worship, Confession
│   ├── ChurchLocation.cs           Marriage, baptism
│   ├── HealerLocation.cs           Healing services
│   ├── BankLocation.cs             Bank + robbery
│   ├── WeaponShopLocation.cs       Procedural weapons
│   ├── ArmorShopLocation.cs        Procedural armor
│   ├── MagicShopLocation.cs        Spells, accessories
│   ├── MusicShopLocation.cs        Instruments, Melodia, lore songs
│   ├── MarketplaceLocation.cs      Auction house
│   ├── DarkAlleyLocation.cs        Crime, fence, drugs
│   ├── LoveStreetLocation.cs       Romance hub
│   ├── LoveCornerLocation.cs       Marriage office
│   ├── TeamCornerLocation.cs       Team / guild recruitment
│   ├── QuestHallLocation.cs        Quest board
│   ├── NewsLocation.cs             News feed reader
│   ├── LevelMasterLocation.cs      Level up + spec selection
│   ├── PrisonLocation.cs           Prison interior
│   ├── PrisonWalkLocation.cs       Prison walk yard
│   ├── AnchorRoadLocation.cs       Gang turf wars
│   ├── DormitoryLocation.cs        Cheap sleep
│   ├── PantheonLocation.cs         Immortal-only
│   ├── GodWorldLocation.cs         God realm
│   ├── WildernessLocation.cs       4-region wilderness exploration
│   ├── SettlementLocation.cs       Outskirts NPC settlement
│   ├── SysOpLocation.cs            BBS-mode sysop console
│   └── CharacterCreationLocation.cs
│
├── Data/
│   ├── MonsterFamilies.cs          10 families x 5 tiers
│   ├── SpellDatabase.cs            100 spells
│   ├── EquipmentData.cs            Equipment definitions + shop queries
│   ├── OldGodsData.cs              7 Old God boss data
│   ├── WorldBossData.cs            8 world boss definitions
│   ├── FounderStatueData.cs        11 alpha-era founder records
│   └── SpecializationData.cs       24 specializations
│
├── AI/
│   ├── NPCBrain.cs                 Goal-based NPC AI
│   ├── MemorySystem.cs             Memory (30 max, 7-day decay)
│   ├── EmotionalState.cs           12-emotion model with antagonistic suppression
│   ├── PersonalityProfile.cs       13 core + 10 romance traits
│   ├── GoalSystem.cs               Goal generation / scoring
│   ├── EnhancedNPCBehaviors.cs     Shopping, gangs, relationships
│   ├── EnhancedNPCBehaviorSystem.cs (entry point)
│   └── (related)
│
├── BBS/
│   ├── DoorMode.cs                 Command-line parsing, mode detection
│   ├── DropFileParser.cs           DOOR32.SYS / DOOR.SYS parsing
│   ├── SocketTerminal.cs           TCP socket I/O
│   └── BBSTerminalAdapter.cs       Terminal adapter (ANSI / WWIV / Console)
│
├── Editor/
│   └── PlayerSaveEditor.cs         Standalone game / save editor (--editor)
│
├── UI/
│   ├── TerminalEmulator.cs         Console / Godot / BBS abstraction (~2500 lines)
│   ├── UIHelper.cs                 80-char box drawing
│   ├── ANSIArt.cs                  Race / class portraits, monster art
│   ├── RacePortraits.cs            ANSI portrait registry
│   ├── ElectronBridge.cs           OSC event emit for Electron client
│   └── AccessibilityDetection.cs   SPI_GETSCREENREADER wrapper
│
└── Utils/
    └── (helpers)

web/
├── index.html                      Landing page + terminal + stats
├── dashboard.html                  NPC Analytics Dashboard
├── admin.html                      Admin console (bots, bans, settings, players)
├── steam.html                      Steam landing page
├── ssh-proxy.js                    HTTP + WS + SSE + Discord + admin API
├── package.json                    ws, ssh2, better-sqlite3, discord.js
└── lang/{en,es,fr,hu,it}.json     Website localization

electron-client/                    Optional graphical client (beta)
├── main.js                         Electron main process
├── src/
│   ├── ansi-parser.js              ANSI + OSC event parsing
│   ├── game-ui.js                  All graphical rendering
│   ├── audio.js                    Web Audio receiver
│   └── i18n.js                     JS-side localization
├── styles/gui.css                  All graphical styling
├── lang/{es,fr,hu,it}.json         Translation drops
├── assets/classes-hd/              17 HD class sprites
└── assets/monsters-hd/             15 HD monster sprites

Localization/
├── en.json                         English (~16500 keys, source of truth)
├── es.json                         Spanish
├── fr.json                         French
├── hu.json                         Hungarian
└── it.json                         Italian

scripts-server/
├── nginx-usurper.conf              Nginx site config
├── usurper-web.service             Web proxy systemd
├── usurper-mud.service             MUD server systemd
├── setup-server.sh                 Server bootstrap
├── update-server.sh                Binary deployment
├── backup.sh                       Daily DB backup
└── healthcheck.sh                  System monitoring

Tests/
└── Tests.csproj                    Unit tests

DOCS/
├── ARCHITECTURE.md                 (this file)
├── BBS_DOOR_SETUP.md               Sysop setup
├── DOCKER.md                       Docker self-hosting
├── MODDING.md                      JSON modding guide
├── SERVER_DEPLOYMENT.md            Server ops
├── RELEASE_NOTES_*.md              Per-version changelogs
└── STEAM_RELEASE_NOTES_*.txt       Steam-formatted release notes (BBCode)

launchers/
├── Play.bat / play.sh              WezTerm launcher
└── Play-Accessible.bat / play-accessible.sh   Screen-reader launcher

GameData/
└── (optional JSON mod overrides per DOCS/MODDING.md)
```

---

## 43. Key Design Patterns

1. **Singleton via `Lazy<T>`** -- thread-safe lazy init for GameEngine, SaveSystem, LocationManager, NPCSpawnSystem, NewsSystem, DiscordBridge, GodSystem, FamilySystem, RomanceTracker, etc.

2. **Pluggable Backend** -- `IOnlineSaveBackend` with `FileSaveBackend` (local) and `SqlSaveBackend` (online) implementations. Selected at startup; same API.

3. **Location Loop** -- `BaseLocation` provides display/input/dispatch loop; subclasses implement `DisplayLocation` and `ProcessChoice`; global commands run before per-location dispatch.

4. **Schema-Driven Tunables** -- `ServerSettingsRegistry` descriptors drive form rendering, validation, persistence, and live apply from one source of truth.

5. **Deterministic Generation** -- dungeon floors use seeded `Random(level * 31337 + 42)` for consistent layouts.

6. **Dual Equipment** -- legacy Pascal item IDs coexist with modern Equipment objects.

7. **Atomic SQL** -- gold theft via `json_set()` + `MAX(0, ...)` (no full save load); trade resolution via compare-and-set with row-count winner; world boss kill via conditional UPDATE.

8. **Static Delegate Hooks** -- `KickActiveSessionHook`, `PermadeathPurgeHook` -- defined in `SqlSaveBackend`, wired by `MudServer` at startup. Decouples backend from MUD-server-only operations.

9. **Heartbeat Presence** -- 120-second window for "online" detection.

10. **NPC Death Duality** -- `IsDead` (permanent) vs `IsAlive` (computed `HP > 0`). Always check the right one for the scenario.

11. **World State Authority** -- world sim is canonical for shared state in MUD mode; player saves carry only player-specific data; concurrent edits via row-level versioning.

12. **AsyncLocal Session Context** -- `SessionContext.Current` propagates per-session state through async calls without explicit threading.

13. **Apply-Queue Bridge** -- web process can write to SQLite but cannot reach the running game's in-memory statics. `server_config_apply_queue` rows queued by web -> drained by game every 1s -> applied via registry. Same pattern (in spirit) for Discord bridge polling and admin command queue.

14. **Defense in Depth on Permadeath** -- 4 layers prevent re-INSERT of erased characters: in-memory blacklist (`MarkUsernameErased` checked at WriteGameData), session `SuppressDisconnectSave`, world-state purge before account delete, 7-day archive.

15. **Loopback-Only Trusted AUTH** -- raw TCP peer (cannot be spoofed) gates the no-password trusted-AUTH path. X-IP forwarded headers (which can be spoofed) are used for ban tracking, never for trust.

16. **Markdown-Style Sentinel for System Events** -- Discord bridge uses `__SYSTEM__` author sentinel rather than schema change to differentiate login/logout from chat.

17. **Per-Channel Mute Filter** -- `BroadcastToAll(msg, excludeUsername, channelKey)` skips sessions whose `MutedChannels` contains the key. Null channelKey always delivers.

18. **Antagonistic Emotions** -- NPC emotional baselines from personality, suppressed by antagonistic emotions (anger dampens happiness, fear dampens confidence) rather than tracked as independent values.

19. **Order-Sensitive Init** -- `SqlSaveBackend` constructor: `InitializeDatabase` -> `LoadServerConfigIntoGameConfig` -> `PublishServerSettingsSchema`. Schema must exist before reads; load before publish so the published schema reflects the DB-applied values; load before any session connects.

20. **Greedy Longest-Prefix Name Resolution** -- multi-word display names like "Lumina Starbloom" resolved by trying the full input as a name, then dropping the trailing word, repeat. Prevents the v0.57.5 "first word eaten" bug in `/tell`.

21. **Per-Item Save Buttons** -- web admin Server Settings panel saves each setting individually rather than bulk-saving the whole form. One typo doesn't trash everything.

22. **Schema Re-Publish on Restart** -- `server_config_schema` is rewritten on every game startup so the schema in the DB always matches the binary's registry. Prevents the web admin from rendering stale form fields after a binary upgrade that adds / removes settings.
