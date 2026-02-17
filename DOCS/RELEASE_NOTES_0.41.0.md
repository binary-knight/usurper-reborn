# v0.41.0 - Auto World Simulator, Dark Alley Overhaul

The Dark Alley has been completely transformed from a simple drug shop into a thriving criminal underworld hub. This update fixes critical broken systems, adds 8 new features, and turns the Dark Alley into one of the most content-rich locations in the game. Also includes the auto world simulator for BBS Online Mode and major server performance fixes.

---

## Embedded World Simulator for BBS Online Mode

**The world simulator now runs automatically inside player sessions — no separate process needed.**

Previously, BBS SysOps running Online Mode had to set up a separate `--worldsim` process as a systemd service (Linux) or NSSM service (Windows) to keep the NPC world alive. This was the biggest barrier to adoption. Now it just works:

```
UsurperReborn --online --door32 %f
```

That's it. One flag. No background services, no database setup, no systemd configuration.

### How It Works

The game uses database-level leader election so only one player session runs the world simulator at a time:

1. When a player connects, their session checks for a worldsim lock in the database
2. If no lock exists (or the existing lock's heartbeat is stale), this session becomes the worldsim host
3. The worldsim runs as a background thread, sharing NPCs and game state directly with the player session
4. The host updates a heartbeat every tick so other sessions know it's active
5. When the host disconnects, the next session to connect takes over automatically
6. When nobody is playing, the world pauses until someone connects

### Backward Compatible

- The standalone `--worldsim` process still works for SysOps who want 24/7 simulation even when no players are online
- When a standalone worldsim is running, player sessions detect its active heartbeat and skip the auto worldsim
- New `--no-worldsim` flag explicitly disables the auto worldsim for SysOps who prefer the separate process approach

---

## Critical Bug Fixes

### Drug System Now Actually Works
- **Drugs no longer last forever**: `DrugSystem.ProcessDailyDrugEffects()` is now called during daily resets. Drug durations decrement properly, drugs expire, withdrawal messages appear, and addiction recovery functions as intended.
- **10 drug effects now work in combat**: Previously, only 8 of 18 drug effect properties were wired into gameplay. The following now properly affect combat:
  - **CritBonus**: Increases critical hit chance (capped at 50%)
  - **SpellPowerBonus**: Multiplies spell damage/healing by bonus percentage
  - **MagicResistBonus**: Reduces incoming magic/ability damage by bonus percentage
  - **HPDrain**: Drains HP each combat round (won't kill you, minimum 1 HP)
  - **ManaBonus**: Adds to effective mana for spellcasting checks
  - **WisdomBonus**: Increases effective Wisdom for healing spell calculations
  - **DexterityBonus**: Adds to effective DEX for backstabs and critical hits
  - **ConstitutionBonus**: Boosts poison resistance and Troll regeneration
  - **AgilityBonus**: Increases dodge chance (offset by AgilityPenalty)
  - **DarknessBonus**: Temporarily boosts Darkness for alignment combat modifiers

### Permanent Stat Exploits Fixed
- **Groggo's Shadow Blessing**: Was giving permanent +3 DEX with no removal mechanism despite saying "until next rest." Now properly tracks the buff and removes it when resting at the Inn.
- **Steroid Shop**: Gave unlimited permanent +5 STR / +3 STA. Now capped at 3 lifetime purchases.
- **Alchemist's Heaven**: Gave unlimited permanent +2 INT on lucky rolls. Now capped at 3 lifetime boosts.

### NPC Goal System Fixed (19 MB to ~500 KB)
- NPC goals accumulated unboundedly (91,000+ goals across 60 NPCs) due to missing duplicate checking in `GoalSystem.AddGoal()`. Every deserialization cycle re-added archetype goals on top of existing ones.
- Goals now deduplicated by name and hard-capped at 30 per NPC with aggressive pruning.
- Goal serialization only saves active, non-completed goals sorted by priority.

---

## New Features

### Gambling Den [C]
Three street-hustle games distinct from the Inn's tavern games:
- **Loaded Dice**: Guess over/under 7 on 2d6. ~45% win rate, 1.8x payout. CHA gives a small edge.
- **Three Card Monte**: Find the queen. 33% base chance + DEX bonus. 2.5x payout.
- **Skull & Bones**: Choose your risk multiplier (2x/3x/5x) for corresponding chances.
- Daily limit of 10 rounds. Minimum bet 10 gold, maximum 10% of gold on hand.

### Pickpocket NPCs [P]
- Browse NPCs in town and attempt to lift their purses
- DEX-based success: 40% base + DEX scaling, capped at 75%. Assassins get +15%.
- Success: Steal 5-15% of NPC gold, gain Darkness and reputation
- Failure: NPC attacks you in combat, relationship drops
- Critical failure (roll < 10%): 1-day prison sentence
- Uses existing daily thieving counter

### Underground Arena - The Pit [T]
- 3 fights per day limit
- **Monster Fight**: Face a level-appropriate monster with armor temporarily removed. 2x gold reward.
- **NPC Challenge**: Fight NPCs within your level range. Winner takes 20% of loser's gold.
- **Spectator Betting**: Wager on your own fights with risk multipliers.
- Armor is safely saved and restored after each fight.

### Loan Shark [L]
- Borrow up to (level x 500) gold
- 20% daily compound interest, 5-day repayment window
- Repay full or partial amounts
- Overdue loans trigger Enforcer encounters when entering the Dark Alley
  - Win vs enforcer: loan forgiven
  - Lose: all gold taken, 25% HP damage, 3 more days added

### Fence Stolen Goods [F]
- Sell equipment at 70% value (vs 50% at normal shops)
- Shadows faction members get 80% value
- Accepts cursed items that normal shops refuse
- Browse your backpack and sell individual items

### Safe House [N]
- Rest for 50 gold (cheaper than the Inn)
- Restores 50% HP (vs Inn's 100%)
- 10% chance of robbery (lose 5-10% gold), Shadows members exempt
- Requires Darkness >= 50 to access
- **PvP Protection**: Shadows faction members who rest here are hidden from Arena attacks while offline. The protection clears when you log back in.

### Shady Encounters on Entry
15% chance of a random encounter when entering the Dark Alley:
- **Mugger**: Fight or pay 50 gold tribute
- **Beggar with a Tip**: Reveals useful information about the dungeon or NPCs
- **Undercover Guard**: High-Darkness players risk arrest (1-day prison)
- **Shady Merchant**: Random discounted item for sale

Encounter quality scales with your Dark Alley Reputation.

### Drug System Enhancements
- **Overdose Risk**: Using drugs while already on drugs now has a 30% overdose chance (25% max HP loss, doubled addiction). Otherwise safely replaces current drug.
- **Tolerance**: Each use of the same drug type reduces its duration by 1 day (minimum 1 day). Tracked per drug type.
- **Rehab at Healer**: New [A]ddiction Rehab option at the Healing Hut. Costs 2,000g + (addiction level x 50g). Completely cures addiction, clears all drug tolerances, and purges active drug effects.

### Dungeon Ambush First-Strike
When monsters ambush you in the dungeon (30% chance on room entry), they now get a free attack round before you can act. All living monsters in the ambush attack once before the regular combat menu appears. This makes ambushes a genuine threat instead of just a flavor message.

---

## New Achievements

| Achievement | Condition | Reward |
|-------------|-----------|--------|
| High Roller | Win 1,000g gambling in the Dark Alley | 500g |
| Pit Champion | Win 10 fights in The Pit | 1,000g |
| Light Fingers | Successfully pickpocket 20 times | 500g |
| Debt Free | Pay off a loan from the Loan Shark | 200g |
| King of the Alley | Reach 1,000 Dark Alley reputation | 2,000g |

---

## New Character Properties

- **Dark Alley Reputation** (0-1000): Earned through underground activities. Higher reputation unlocks better shady encounters and benefits.
- **Drug Tolerance**: Per-drug-type tracking that reduces effectiveness of repeated use.
- **Loan tracking**: Active loan amount, days remaining, and accrued interest.
- **Activity counters**: Daily gambling rounds and pit fight limits.

---

## Faction Standing & Underground Access

### Faction Joining Requires Positive Standing
- All three factions (Crown, Shadows, Faith) now reject players with negative standing
- You must improve your reputation before they'll consider your application
- Standing labels shown on rejection: Unfriendly, Hostile, or Hated

### Underground Services Gated by Shadows Standing
If the Shadows consider you hostile (standing below -50), underground services are locked out. The shady establishments (Drug Palace, Steroid Shop, etc.) remain open, but the criminal network won't deal with you:
- **Locked services**: Pickpocket, Fence, Gambling Den, The Pit, Loan Shark, Safe House
- **Menu shows locks**: Locked services display in gray with current standing info
- **Rejection flavor**: NPCs physically block you with a warning when you try locked services

### Rebuilding Standing
Two ways to get back in the Shadows' good graces:
- **[W] Pay Tribute**: Available when standing is negative. A cloaked figure accepts gold bribes — costs 100 + |standing| x 2 gold for +50 standing. Shows how many tributes needed to unlock services.
- **Patronize shady shops**: Spending gold at the Drug Palace, Steroid Shop, Orbs Health Club, Groggo's, Beer Hut, or Alchemist's Heaven passively grants +3 Shadows standing per purchase (up to Friendly tier).

---

## Server Performance Fixes

### Location Changes No Longer Lag (5-7s to instant)
- **Autosave throttled in online mode**: Autosave was firing on every location redraw, serializing ~5 MB of player data and writing to SQLite each time. Now throttled to once per 60 seconds. Single-player mode is unaffected.
- **Removed redundant NPC world_state sync from autosave**: Each autosave was also re-serializing all 60 NPCs (~18 MB) and writing to the world_state table — duplicating work the world sim already does every 5 minutes. Removed from autosave; explicit saves (on quit) still sync.

### World Sim Database Optimizations
- **NPC save dirty-checking**: The world sim now hashes the serialized NPC JSON and skips the database write when nothing has changed, avoiding unnecessary 18 MB writes.
- **Global news pruning**: Previously only "npc" category news was pruned. All news categories now pruned with a 48-hour age limit and 500-entry global cap, preventing unbounded table growth.
- **NPC relationship serialization capped**: Each NPC's relationship dictionary is now capped at the 20 most significant relationships during serialization.

### Save Data Size (18 MB to ~200 KB per player)
- **Quest serialization fixed**: Every player save was dumping the entire quest database (3,300+ quests = 2.5 MB) into both `player.activeQuests` AND `worldState.activeQuests` — 5 MB of quest spam per save. Player saves now only contain their own claimed quests; world state only saves unclaimed board quests.
- **Quest database pruning**: Unclaimed quests older than 7 days are now automatically removed. Hard cap of 200 quests prevents unbounded growth.
- **Database size reduction**: Combined with VACUUM, the online database drops from ~170 MB to well under 25 MB.

### Node.js Web Proxy Fix
- Stats API was independently parsing the 19 MB NPC blob every 30 seconds for marriage counts. Now reuses the cached NPC data.

---

## NPC Talk Hint Visibility

The `[0] Talk (N)` hint is now shown at all locations where NPCs can be present: WeaponShop, ArmorShop, Healer, MagicShop, Castle, and Anchor Road. The talk command already worked via `[0]` but players couldn't see it.

---

## Balance & Statistics

- New statistics tracked: gambling rounds, pickpocket attempts/successes, pit fights won/lost
- Gambling and pit fight counters reset daily
- Loan interest accrues during daily events with player notifications
- Drug effects processing integrated into daily reset cycle

---

## Files Changed

| File | Changes |
|------|---------|
| `Scripts/Core/GameConfig.cs` | Version 0.41.0; ~40 new balance constants for all Dark Alley features |
| `Scripts/Core/Character.cs` | 10 new properties (reputation, loan, tolerance, counters); DrugSystem overdose/tolerance |
| `Scripts/Systems/SaveDataStructures.cs` | 10 new PlayerData fields for persistence |
| `Scripts/Systems/SaveSystem.cs` | Serialize all new fields; throttle autosaves to 60s in online mode; remove redundant NPC world_state sync from autosave; split quest serialization into per-player and per-world (fixes 5MB bloat per save) |
| `Scripts/Systems/QuestSystem.cs` | Enhanced quest cleanup: prune unclaimed quests after 7 days, hard cap at 200 quests |
| `Scripts/Core/GameEngine.cs` | Restore all new fields on load |
| `Scripts/Systems/DailySystemManager.cs` | Wire drug processing; reset gambling/pit counters; loan interest accrual |
| `Scripts/Systems/CombatEngine.cs` | Wire HPDrain, MagicResistBonus, DarknessBonus, DexterityBonus, ConstitutionBonus; ambush first-strike logic |
| `Scripts/Systems/StatEffectsSystem.cs` | Wire CritBonus, AgilityBonus, DexterityBonus into RollCriticalHit/RollDodge |
| `Scripts/Systems/SpellSystem.cs` | Wire SpellPowerBonus, ManaBonus, WisdomBonus into spell calculations |
| `Scripts/Systems/StatisticsSystem.cs` | 5 new tracking fields + 4 new Record methods |
| `Scripts/Locations/DarkAlleyLocation.cs` | Fix Groggo/Steroid/Alchemist exploits; add 8 new features; revised menu; entry encounters; Safe House PvP protection; underground service gating by Shadows standing; tribute system; passive standing boost from shop purchases |
| `Scripts/Systems/FactionSystem.cs` | Reject faction joining when standing is negative (all factions) |
| `Scripts/Locations/BaseLocation.cs` | Clear SafeHouseResting flag on location entry |
| `Scripts/Locations/ArenaLocation.cs` | Block PvP attacks against players resting in Safe House |
| `Scripts/Locations/InnLocation.cs` | Remove Groggo Shadow Blessing DEX buff on rest (both rest methods) |
| `Scripts/Locations/HealerLocation.cs` | Add Addiction Rehab option with drug/addiction status display; add `[0] Talk` hint |
| `Scripts/Locations/WeaponShopLocation.cs` | Add `[0] Talk` hint via ShowStatusLine |
| `Scripts/Locations/ArmorShopLocation.cs` | Add `[0] Talk` hint via ShowStatusLine |
| `Scripts/Locations/MagicShopLocation.cs` | Add `[0] Talk` hint via ShowStatusLine |
| `Scripts/Locations/CastleLocation.cs` | Add `[0] Talk` hint via ShowStatusLine (both royal and commoner menus) |
| `Scripts/Locations/AnchorRoadLocation.cs` | Add `[0] Talk` hint via ShowStatusLine |
| `Scripts/Locations/DungeonLocation.cs` | Pass ambush flag to FightRoomMonsters and CombatEngine |
| `Scripts/Systems/AchievementSystem.cs` | 5 new Dark Alley achievements with stat-based checks |
| `Scripts/Systems/WorldSimService.cs` | NPC save dirty-checking (SHA256 hash comparison); prune all news categories with global cap; worldsim lock/heartbeat for embedded mode |
| `Scripts/Systems/SqlSaveBackend.cs` | Worldsim lock methods: `TryAcquireWorldSimLock()`, `UpdateWorldSimHeartbeat()`, `ReleaseWorldSimLock()`, `IsWorldSimLockActive()`; `PruneAllNews()` |
| `Scripts/Systems/OnlineStateManager.cs` | Cap NPC relationship serialization to 20 most significant entries; goal serialization cap at 30 |
| `Scripts/AI/GoalSystem.cs` | Duplicate goal checking by name; hard cap at 30 goals; aggressive pruning of completed/inactive |
| `Console/Bootstrap/Program.cs` | Embedded worldsim startup in BBS Online flow: lock acquisition, background thread, initialization wait, graceful shutdown |
| `Scripts/BBS/DoorMode.cs` | Added `--no-worldsim` flag to disable auto worldsim |
| `DOCS/BBS_DOOR_SETUP.md` | Rewrote BBS Online Mode section: auto worldsim is now the default |
| `web/ssh-proxy.js` | NPC cache reuse for stats API (fixes Node.js memory growth) |
