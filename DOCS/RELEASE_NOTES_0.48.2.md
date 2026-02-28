# Usurper Reborn v0.48.2 — World Boss Raid System

## World Boss Raids (Online Mode)

The placeholder world boss has been completely replaced with a full raid-boss experience. Massive bosses now spawn on a schedule, require multiple players to defeat, and reward participants based on their damage contribution.

### How It Works

1. **Boss spawns** automatically when 2+ players are online (once per day, 1-hour fight window)
2. **Server-wide broadcast** notifies all online players when a boss appears
3. **Type `/boss` from any location** (or `[7]` from Main Street) to enter combat
4. **Full round-by-round combat**: Attack, Cast Spell, Use Ability, Defend, Use Item, Power Attack, Precise Strike, Retreat
5. **Shared HP pool** — every player's damage counts toward the same boss HP bar
6. **Phase transitions** at 65% and 30% HP with escalating abilities
7. **Contribution-based rewards** when the boss falls — everyone who helped gets XP, gold, and loot

### Never Soloable

- **Presence aura**: Unavoidable 5-8% MaxHP damage per round (scales with phase)
- **Boss attacks**: Average 15-20% MaxHP per round through defense
- Combined: solo player loses 20-28% MaxHP/round — dies in 4-5 rounds
- **Death cooldown**: 60 seconds before re-entering combat
- Players can retreat and re-enter to heal between attempts

### Boss Roster (8 Bosses)

| Boss | Element | Level | Base HP | Key Mechanics |
|------|---------|-------|---------|---------------|
| The Abyssal Leviathan | Water | 40 | 200K | Freeze, Tidal Wave AoE, Drown |
| Void Colossus | Void | 50 | 280K | Silence, Gravity Crush stun, Reality Tear |
| Shadowlord Malachar | Shadow | 35 | 180K | Fear, Life Drain, Self-heal |
| The Crimson Wyrm | Fire | 45 | 250K | Burning DoT, Wing Buffet stun, Inferno AoE |
| Lich King Vareth | Undead | 55 | 320K | Poison Cloud, Paralysis, Raise Dead adds |
| The Iron Titan | Physical | 60 | 380K | Armor Break debuff, Berserk Rage, Devastate |
| Dread Serpent Nidhogg | Poison | 70 | 420K | Stacking poison, Constrict stun, AoE venom |
| The Nameless Horror | Eldritch | 80 | 500K | Confusion, Random effects, Cosmic Horror fear |

Each boss has 3 phases with escalating abilities. Boss HP scales with online player count: `baseHP * (1 + 0.1 * players)`.

### Reward Tiers

Based on damage contribution ranking:

| Rank | XP/Gold Multiplier | Guaranteed Loot |
|------|-------------------|----------------|
| MVP (#1) | 3.0x | Legendary |
| Top 3 | 2.5x | Epic |
| Top 25% | 2.0x | Rare |
| Top 50% | 1.5x | Uncommon |
| Any contribution | 1.0x | Common |

Offline contributors receive rewards applied directly to their save data plus a notification message on next login.

### World Boss Exclusive Loot

Items that ONLY drop from world bosses:

- **BossSlayer** weapons: +10% damage against all boss monsters (dungeon bosses and world bosses)
- **Titan's Resolve** armor: +5% defense bonus
- **Element-themed prefixes**: Fire boss drops Inferno/Blazing gear, Water boss drops Tidal/Frozen gear, etc.

## `/boss` Command

Type `/boss` from any location to view the world boss status screen:
- Current boss HP, phase, and time remaining
- Damage leaderboard showing top contributors
- Enter combat directly from the status screen

## World Boss Achievements

- **World Slayer** (Gold, 5000g) — Participate in defeating a world boss
- **Boss Hunter** (Platinum, 25000g) — Help defeat 5 different world bosses
- **Legend Killer** (Diamond, 100000g) — Participate in 25 world boss kills
- **MVP** (Gold, 10000g) — Deal the most damage in a world boss fight

## World Boss Statistics

New statistics tracked per player:
- World bosses killed (total)
- Unique world boss types defeated
- Total world boss damage dealt
- MVP count

## BossSlayer & Titan's Resolve in Regular Combat

World boss exclusive loot effects now also work in regular dungeon combat:
- **BossSlayer**: +10% damage against dungeon bosses and mini-bosses
- **Titan's Resolve**: +5% defense bonus when taking damage from monsters

## Bug Fixes

### Auto-Updater Downloading Wrong Package

The SysOp auto-updater was downloading the WezTerm desktop bundle (99MB, nested folder structure) instead of the plain server build (24MB, flat files). The `GetPlatformAsset()` method used `FirstOrDefault` which matched `WezTerm-Windows-x64.zip` before `Windows-x64.zip` because GitHub's API returns assets in creation order. The WezTerm ZIP contains a nested subfolder, so the update script copied a subfolder instead of overwriting the game files — resulting in a "successful" update that changed nothing.

- Asset selection now explicitly excludes WezTerm and Desktop builds
- Added `FlattenExtractedDirectory()` as defense-in-depth: if a ZIP extracts with a single nested folder containing the game binary, that folder is used as the source
- Windows update batch script now retries file copies 3 times with 2-second delays and writes `update.log` for diagnostics
- Unix update shell script also writes `update.log`

### Monsters Attacking After Successful Flee

When a player successfully fled combat, monsters still got their full round of attacks before the flee took effect. The `globalEscape` flag was only checked at the top of the `while` loop, meaning after the player's flee action, all teammates' turns AND all monsters' turns would still execute before the loop exited.

- Multi-monster combat: added immediate `break` after player action when `globalEscape` is set (before teammates act)
- Multi-monster combat: added immediate `break` after teammates' turns (before monsters act)
- PvP combat: defender no longer gets a turn when attacker has fled

### Unarmed Characters Getting Bonus Attacks

Companions and other characters with no weapon equipped were receiving bonus attacks from agility procs, class modifiers, and other sources. For example, Lyris (Agility 35) had a 40% chance per round to get an agility-based extra attack despite having no weapon — resulting in frequent double attacks with bare fists.

- `GetAttackCount()` now returns 1 immediately if the character has no weapon in MainHand
- All bonus attack sources (class extra swings, dual-wield, agility procs, artifacts, drugs, haste/slow) are skipped for unarmed characters

### Companion Ability Count Mismatch

The companion interaction screen displayed abilities from a hardcoded static array (e.g., "Shield Wall, Taunt, Last Stand, Sacrifice" — 4 abilities) while the `[A] Manage Combat Skills` toggle menu used the dynamic `ClassAbilitySystem.GetAvailableAbilities()` lookup (which returns all class abilities for the companion's level — often 8). Players saw "4 abilities" on one screen and "8 abilities" on another.

- Companion interaction screen now uses the same dynamic class ability lookup as the toggle menu, showing the actual count and names

### Companion Romance Spammable

The `[R] Deepen your bond` romance option could be spammed indefinitely with no cooldown or failure chance. Players could max out romance from 0 to 10 in a single sitting by repeatedly selecting the option. At max romance (10/10), the option still showed advancement text.

- Romance is now **once per day** per companion — a `RomancedToday` flag is set on interaction and reset on daily reset
- Romance advancement is now **Charisma-based**: 30% base chance + 1% per CHA point (capped at 80%). On failure, the player sees flavor text and a hint that higher Charisma improves chances
- At max romance (10/10), the option shows "Your bond is as deep as it can be" instead of advancement text
- Menu label updates to reflect daily limit and max bond status

## Companion Equipment Persistence Fix

Companion equipment picked up during dungeon combat (e.g., a Shadow Shortsword dropped by a monster) was silently lost when leaving the dungeon. The `SyncCompanionState()` method only synced HP and potions back from the temporary Character wrapper to the Companion object — equipment changes were never copied back.

- Added `SyncCompanionEquipment()` to CompanionSystem that copies equipment from the combat Character wrapper back to the persistent Companion object
- `SyncCompanionState()` now calls `SyncCompanionEquipment()` alongside existing HP/potion sync
- Immediate equipment sync after teammate loot pickup in combat

## Training Respec Cost Increase

Player feedback indicated that skill respec was too cheap (2,400 gold at level 19 was trivial). Respec costs increased significantly:

- Base cost: 500 → 4,000 gold
- Per-level cost: 100 → 1,000 gold per level
- Example: Level 19 respec now costs 23,000 gold (was 2,400)

## Auto-Level Toggle

Auto-levelling can now be toggled from the `[~] Preferences` menu. On by default — when disabled, players must visit the Level Master manually to level up. This gives players control over when they spend stat points, useful for banking XP or planning specific builds.

- `[8] Auto-Level Up` toggle in Preferences menu
- Setting persists across save/load

## Team XP Distribution Mode

Players can now control how combat XP is shared among party members. Set from the `[X] XP Distribution` option in the dungeon Team Management menu. Three modes:

- **Full Each** (default) — Everyone gets full XP (current behavior)
- **Even Split** — XP divided equally among all party members
- **Killer Takes** — Only the player gets XP; companions and NPC teammates receive nothing

Combined with the Auto-Level Toggle, players who want to power-level a specific companion can set Killer Takes to hoard XP, then manually distribute it via the Level Master. Combat reward display shows which mode is active.

## Flee Formula Unified

The flee chance formula was inconsistent between single-monster and multi-monster combat. Single-monster flee had a 40% base chance plus level scaling and class bonuses, while multi-monster flee only used DEX/2 with no base or bonuses — making multi-monster retreat significantly harder for no good reason.

All flee paths now use a shared `CalculateFleeChance()` formula:
- **Base**: 40%
- **DEX scaling**: +DEX/2
- **Level scaling**: +Level/3
- **Class bonus**: Ranger/Assassin +15%, Jester/Bard +10%
- **Shadows faction**: +15-45% based on standing
- **Divine boon**: bonus from temple blessings
- **Boss fights**: flat 20% regardless of stats
- **Hard cap**: 75%

## Files Changed

- `GameConfig.cs` — Version 0.48.2; world boss constants (spawn timing, HP scaling, aura damage, death cooldown, reward multipliers, min level, max rounds)
- `Scripts/Data/WorldBossData.cs` — **NEW** — 8 boss definitions with stats, 3 phases each, abilities with damage/status effects, loot themes
- `Scripts/Systems/WorldBossSystem.cs` — **NEW** — Main world boss system: spawn conditions, boss status UI, full round-by-round combat loop with shared HP pool, phase transitions, contribution-based reward distribution, world boss exclusive loot generation
- `Scripts/Systems/SqlSaveBackend.cs` — Extended `SpawnWorldBoss()` with `boss_data_json`; extended `GetActiveWorldBoss()` to return `BossDataJson`; new `UpdateWorldBossData()` for atomic phase transitions; new `GetOnlinePlayerCount()` and `GetAverageOnlineLevel()`; new `AddXPToPlayer()` method
- `Scripts/Systems/LootGenerator.cs` — Added `BossSlayer` and `TitanResolve` to `SpecialEffect` enum; new `GenerateWorldBossLoot()` method with element-themed prefixes and guaranteed minimum rarity
- `Scripts/Locations/MainStreetLocation.cs` — Removed ~200 lines of old world boss code (templates, menu, attack logic); replaced with single delegation to `WorldBossSystem.Instance.ShowWorldBossUI()`
- `Scripts/Locations/BaseLocation.cs` — Added `/boss` and `/worldboss` slash commands; added `/boss` to quick commands help list; world boss active notification on location entry (once per visit)
- `Scripts/Systems/WorldSimService.cs` — Added `WorldBossSystem.CheckSpawnConditions()` call in tick loop after daily reset check
- `Scripts/Systems/AchievementSystem.cs` — Registered 4 new world boss achievements: world_boss_first, world_boss_5_unique, world_boss_25_total, world_boss_mvp
- `Scripts/Systems/StatisticsSystem.cs` — Added `WorldBossesKilled`, `WorldBossDamageDealt`, `WorldBossMVPCount`, `UniqueWorldBossTypes` tracking; `RecordWorldBossKill()` method; reset support
- `Scripts/Core/Character.cs` — Added `AutoLevelUp` (bool, default true) and `TeamXPShare` (XPShareMode enum) properties
- `Scripts/Core/GameConfig.cs` — Version 0.48.2; world boss constants; `XPShareMode` enum (FullEach, EvenSplit, KillerTakes); respec costs increased (4000 base + 1000/level)
- `Scripts/Systems/CombatEngine.cs` — BossSlayer/TitanResolve effects; unified `CalculateFleeChance()` helper used by all 3 flee paths (single-monster, multi-monster, grouped player); `globalEscape` immediate break after player action and after teammate turns; unarmed characters capped at 1 attack in `GetAttackCount()`; `!globalEscape` guard on PvP defender turn; XP share mode applied to both single-monster and multi-monster victory paths (EvenSplit divides XP by party size, KillerTakes skips companion/teammate XP); XP share mode display in combat rewards; immediate companion equipment sync after teammate loot pickup
- `Scripts/Systems/VersionChecker.cs` — Auto-updater: excluded WezTerm/Desktop builds from `GetPlatformAsset()`; added `FlattenExtractedDirectory()` for nested ZIP defense-in-depth; retry logic and `update.log` in updater scripts
- `Scripts/Systems/CompanionSystem.cs` — `RomancedToday` flag on Companion; `AdvanceRomance()` returns false when already at max (10); `ResetDailyFlags()` method for daily reset; new `SyncCompanionEquipment()` method copies equipment from Character wrapper back to Companion; `SyncCompanionState()` calls `SyncCompanionEquipment()`
- `Scripts/Systems/DailySystemManager.cs` — Calls `CompanionSystem.ResetDailyFlags()` on daily reset
- `Scripts/Systems/SaveSystem.cs` — Serialize/restore `AutoLevelUp` and `TeamXPShare`
- `Scripts/Systems/SaveDataStructures.cs` — Added `AutoLevelUp` and `TeamXPShare` fields
- `Scripts/Core/GameEngine.cs` — Restore `AutoLevelUp` and `TeamXPShare` from save data
- `Scripts/Locations/BaseLocation.cs` — Auto-level guard (`&& currentPlayer.AutoLevelUp`); `[8] Auto-Level Up` toggle in preferences menu
- `Scripts/Locations/DungeonLocation.cs` — `[X] XP Distribution` option in team management menu with 3-mode cycling
- `Scripts/Locations/InnLocation.cs` — Companion ability display uses dynamic `ClassAbilitySystem.GetAvailableAbilities()` instead of hardcoded array; romance interaction: once-per-day limit, CHA-based success chance (30% + 1%/CHA, cap 80%), max bond feedback; menu labels update for daily limit and max status
