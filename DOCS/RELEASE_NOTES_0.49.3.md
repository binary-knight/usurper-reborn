# Usurper Reborn v0.49.3 - Player Experience: Onboarding, Power & Hooks

**Release Date**: March 2026

## Overview

Targeted player experience improvements addressing early-game confusion, mid-game visibility, and power fantasy. New players now receive class-specific combat tips on their first fight and face weaker floor 1 monsters (~50% stats). After defeating an Old God, players earn a temporary "God Slayer" buff (+20% damage, +10% defense for 20 combats) and see a breadcrumb hinting at the next god. Higher-level dungeon floors occasionally spawn straggler monsters from upper floors for easy wins that reinforce progression. Main Street now shows NPC story notifications when memorable NPCs have new content available. Combat hint text corrected for current keybindings. Active buff display added to `/health` command. Online players now wake up where they fell asleep (Inn, Home, or Castle). New Reinforced Door home upgrade (250k gold) lets online players sleep safely at home. Online quit menu overhauled with dormitory/inn/home/cancel options.

## New Features

### First Combat Class Tips

New players (zero kills) now see a class-specific combat tip on their very first fight, displayed as a colored box above the action menu. Each tip teaches the most important action for that class:

| Class | Tip |
|-------|-----|
| Magician | "Your power is in your spells! Press [1] to cast Magic Missile for heavy damage." |
| Cleric | "You can heal yourself in combat! Press [1] to cast Cure Light, or [A] to Attack." |
| Sage | "Press [1] to cast Fog of War for protection, then [A] to Attack!" |
| Warrior | "Use [1] Power Strike for 1.75x damage, or [A] for a regular Attack." |
| Barbarian | "Use [1] Power Strike for 1.75x damage! Your raw strength makes every hit count." |
| Paladin | "Use [1] Lay on Hands to heal yourself, or [2] Power Strike for damage." |
| Assassin | "Use [1] Backstab for critical damage! Make sure you have a dagger equipped." |
| Ranger | "Use [1] Precise Shot -- it never misses! Make sure you have a bow equipped." |
| Jester | "Use [1] Vicious Mockery for quick damage -- low cost, low cooldown!" |
| Bard | "Use [1] Vicious Mockery for quick damage, or [A] to Attack. Charm is your weapon!" |
| Alchemist | "Use [1] Throw Bomb for elemental damage! Alchemy is your edge." |

Shown once per character via the hint system.

### Weaker Floor 1 Monsters

All dungeon floor 1 monsters now have 50% reduced HP, Strength, Defence, Punch, and Weapon Power. This makes the first fight winnable by any class in 1-2 hits, creating a natural tutorial floor without labeling it as one. Floor 2+ monsters are unaffected.

### God Slayer Buff

After defeating or saving an Old God (not Manwe), players receive a temporary combat buff:
- **+20% damage** on all attacks for 20 combats
- **+10% defense** against all monster damage for 20 combats
- Announced with a golden "Divine power surges through you!" message
- Persists across save/load
- Displayed in `/health` active buffs and as a Main Street reminder

This rewards the accomplishment of defeating an Old God and creates a power spike that feels earned.

### Straggler Encounters

On dungeon floors 6 and above, there is a 15% chance of encountering a "straggler from the upper floors" -- a monster 5-8 levels below the current floor. These easy wins break up the grind, reinforce the feeling of progression, and provide a breather between tough fights. Boss rooms are excluded.

### Next God Breadcrumb

After defeating an Old God and returning to town, the reaction scene now includes a mysterious old woman who hints at the next unencountered god lurking deeper in the dungeon. This gives mid-game players a clear "what next" goal after each major accomplishment.

### NPC Story Notifications

Main Street now displays a notification when memorable town NPCs (Marcus, Elena, Bartholomew, Greta, Pip, Ezra) have new story content available. One notification per visit, cycling through available NPCs. Each notification is tailored to the specific NPC and their location.

### Active Buff Display

The `/health` command now shows all active temporary buffs with remaining combat counts: God Slayer, Well-Rested, Song, Herb, Lover's Bliss, and Divine Blessing.

### Reinforced Door (Home Upgrade)

New one-time home upgrade for 250,000 gold. Lets online players sleep safely at home behind a heavy iron-banded door. Available from the Home upgrade shop under Special Purchases. Once purchased, [Z] Sleep appears in the Home menu in online mode, and [H] Sleep at home appears on the quit menu.

### Wake Up at Sleep Location

Online players now wake up where they fell asleep. Sleeping at the Inn puts you back in the Inn on login; sleeping at Home puts you back at Home. Dormitory and street sleepers still wake on Main Street. The sleep report header also correctly labels all sleep locations (was only "INN ROOM" or "DORMITORY").

### Online Quit Menu Overhaul

The quit menu in online mode now shows all sleep options with a cancel option:
- **[D] Dormitory** -- 10 gold, vulnerable to attack
- **[I] Inn** -- protected sleep with guards
- **[H] Home** -- safe sleep (only if Reinforced Door purchased)
- **[C] Cancel** -- return to Main Street

## Bug Fixes

- **First Combat hint text**: Fixed incorrect key reference -- said `'F' to Flee` but the actual key is `[R] to Retreat`. Updated to: "Press [A] to Attack, [R] to Retreat, [H] to use a Healing Potion, or [S] to check your Status."
- **AI Echo spells always fizzling**: Echo team members (player ghosts from previous sessions) had 100% spell fizzle rate because their equipment was never restored from save data. Magician echoes had no Staff equipped, so `HasRequiredSpellWeapon()` always failed. Fixed by restoring dynamic equipment, equipped items, and base stats in `PlayerCharacterLoader.CreateFromSaveData()`, followed by `RecalculateStats()`.
- **AI Echo "You wound" message**: When an echo teammate attacked a monster, the combat log said "You wound Pixie for 67 damage!" instead of "Ted wounds Pixie for 67 damage!" because echoes are `Character` objects (not `NPC`), so they fell through to the player message branch. Fixed by checking `attacker != currentPlayer` instead of `attacker is NPC`.
- **Magic Shop empty brackets**: The Identify Item menu row rendered with empty `[ ]` brackets in the right column. Fixed with single-column layout.
- **Quick Commands help menu broken borders**: The `/help` quick commands screen had inconsistent right-side `║` borders and the Quick Keys section had characters bleeding into the left border. Rewritten with consistent box formatting.
- **MUD idle timeout no warning**: Online players were disconnected after 15 minutes of inactivity with no advance warning -- just an abrupt drop to the main menu. Now shows a bright yellow warning ~2 minutes before disconnect ("You will be disconnected in ~2 minutes due to inactivity! Press any key."). Disconnect message now displays for 2 seconds before TCP close (was 500ms) so it actually reaches the client.
- **Online quit double save**: Quitting in online mode saved the game twice -- once in `QuitGame()` and again in `LocationManager` when catching `LocationExitException(NoWhere)`. Removed the redundant first save.
- **Online quit no dormitory warning**: Quitting in online mode silently deducted 10 gold and put the player in the dormitory (vulnerable to attack) with no warning or choice. Now shows a prompt explaining dormitory sleep is vulnerable and offers [I] to go to the Inn for protected sleep instead.
- **Steam/local splash screen clipping**: When connecting to the online server via Steam or local client, the splash screen was half-missing and showed raw ANSI fragments like `30;47m`. The SSH auth response reader consumed game output that arrived in the same buffer chunk as the "OK" response. Now saves leftover data and feeds it to the display when the game pipe starts.
- **`/tell` with display names**: The `/tell` command now resolves display names (not just usernames), so `/tell Rage hello` works even if the account username differs.
- **News feed NPC spam**: High-volume NPC lifecycle events (births, deaths, divorces) could push player news (kills, levels, achievements) out of the feed. News pruning now uses per-category caps (500 NPC, 200 player) instead of a single global cap.

## Files Changed

| File | Changes |
|------|---------|
| `Scripts/Core/GameConfig.cs` | Version 0.49.3; God Slayer buff constants (duration, damage bonus, defense bonus); Floor 1 monster stat multiplier; Straggler encounter constants (chance, min floor, level reduction range); `ReinforcedDoorCost` constant |
| `Scripts/Core/Character.cs` | `GodSlayerCombats`, `GodSlayerDamageBonus`, `GodSlayerDefenseBonus` properties; `HasGodSlayerBuff` computed property; `HasReinforcedDoor` property |
| `Scripts/Core/GameEngine.cs` | God Slayer buff restore from save data; `HasReinforcedDoor` restore from save data; wake-at-sleep-location spawn logic in both `LoadSaveByFileName()` and `EnterGameWorld()`; sleep report label for all locations |
| `Scripts/Systems/HintSystem.cs` | `HINT_FIRST_COMBAT_CLASS` constant; `GetClassCombatTip()` static method with 11 class-specific messages; fixed HINT_FIRST_COMBAT text (F→R key) |
| `Scripts/Systems/CombatEngine.cs` | First combat class tip display in `GetPlayerActionMultiMonster()`; God Slayer damage bonus in single-monster, defense, and multi-monster attack paths; God Slayer buff decrement in `PlayerVsMonsters()`; fixed echo attack message routing (`attacker is NPC` → `attacker != currentPlayer`) |
| `Scripts/Systems/MonsterGenerator.cs` | 0.5x stat multiplier for floor 1 monsters in `CalculateMonsterStats()` |
| `Scripts/Systems/SaveDataStructures.cs` | `GodSlayerCombats`, `GodSlayerDamageBonus`, `GodSlayerDefenseBonus` fields in PlayerSaveData; `HasReinforcedDoor` field |
| `Scripts/Systems/SaveSystem.cs` | God Slayer buff serialization in save path; `HasReinforcedDoor` serialization |
| `Scripts/Systems/TownNPCStorySystem.cs` | `GetNextNotification()` method with NPC-specific messages, cycling logic, and deduplication |
| `Scripts/Locations/DungeonLocation.cs` | Straggler encounter logic before monster generation; God Slayer buff grant in `HandleGodEncounterResult()`; next god breadcrumb in `ShowTownReactionScene()`; `GetNextUnencounteredGod()` helper |
| `Scripts/Locations/MainStreetLocation.cs` | NPC story notification display; God Slayer buff reminder; online quit menu overhaul with [D] Dormitory, [I] Inn, [H] Home (conditional), [C] Cancel; removed redundant save on quit |
| `Scripts/Locations/HomeLocation.cs` | Reinforced Door one-time upgrade (250,000g); `SleepAtHomeOnline()` method; [Z] Sleep (safe) in online menu when HasReinforcedDoor; shifted Armory/Fountain option numbers |
| `Scripts/Locations/BaseLocation.cs` | Active buff display in `/health` command (God Slayer, Well-Rested, Song, Herb, Lover's Bliss, Divine Blessing); Quick Commands help menu rewritten with consistent box formatting |
| `Scripts/Locations/MagicShopLocation.cs` | Fixed empty brackets on Identify Item menu row |
| `Scripts/Systems/PlayerCharacterLoader.cs` | Full equipment restoration for echo/loaded characters: dynamic equipment registration, equipped items with ID remapping, base stats, and `RecalculateStats()` |
| `Scripts/Server/MudServer.cs` | `IdleWarningBefore` constant; idle watchdog now sends warning ~2 minutes before disconnect; checks every 30s (was 60s); resets warning flag when player becomes active |
| `Scripts/Server/PlayerSession.cs` | `IdleWarningShown` flag for idle warning tracking; `DisconnectAsync` shows formatted message with 2s delay (was 500ms) |
| `Scripts/Systems/OnlinePlaySystem.cs` | `_authLeftover` field; `ReadLineFromShell()` saves game data after OK response; `PipeIO()` flushes leftover data before read loop |
| `Scripts/Systems/OnlineChatSystem.cs` | `/tell` resolves display names via `ResolvePlayerDisplayName()` |
| `Scripts/Systems/SqlSaveBackend.cs` | `ResolvePlayerDisplayName()` method; `PlayerExists()` matches display names; `PruneAllNews()` per-category caps (NPC vs player) |
| `Scripts/Systems/WorldSimService.cs` | News pruning uses per-category caps |
