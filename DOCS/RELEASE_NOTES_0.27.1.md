# Usurper Reborn - v0.27.1 Release Notes

## Bug Fixes & Improvements

### Seth Able Exploit Fix

- **Seth Able exploitable via generic NPC talk menu**: Players could bypass Seth Able's dedicated challenge system (which has a 3 fights/day limit, level scaling, and diminishing returns) by pressing `[0] Talk` at the Inn, selecting Seth from the generic NPC list, and choosing `[C] Challenge`. The generic challenge created a monster with `nr: 100` (level 100 XP/gold rewards) using Seth's weak base stats, with no daily limit and no diminishing returns. Fixed in two places:
  - `InnLocation.ChallengeNPC()` now detects Seth Able and redirects to the dedicated `ChallengeSethAble()` system
  - `BaseLocation.TalkToNPC()` now filters out `IsSpecialNPC` NPCs from the generic talk list, preventing special NPCs with dedicated interaction paths from appearing in the generic menu

### Hall of Fame Leaderboard Cap

- **Website Hall of Fame capped at top 25 players**: The leaderboard on the website previously had no limit and would display every registered player. Now limited to the top 25 by level and experience, both in the SQL query (`LIMIT 25`) and as a frontend safety net (`.slice(0, 25)`).

### Linux Auto-Updater Crash Fix

- **Auto-updater crashes on Linux**: The `CreateUnixUpdater()` method in `VersionChecker.cs` generated shell scripts that failed on Linux due to three bugs:
  1. **CRLF line endings**: C# verbatim strings (`$@""`) embed the source file's line endings. When compiled on Windows (CRLF source), the generated shell script contained `\r\n` line endings, causing `bash: /tmp/.../updater.sh: /bin/bash^M: bad interpreter` errors on Linux. Fixed by using a `string[]` array joined with explicit `"\n"`.
  2. **Process detection failure**: `pgrep -x "UsurperReborn"` silently fails on Linux because `-x` requires an exact 15-character match (Linux truncates process names). Changed to `pgrep -f` which matches the full command line.
  3. **File copy glob failure**: `cp -rf "/path/"*` doesn't expand the glob because it's inside quotes. Changed to `cp -rf "/path/". "/dest/"` which copies directory contents without glob expansion.

### Server Deployment File Fixes

- **CRLF line endings in server config files**: `scripts-server/nginx-usurper.conf` and `scripts-server/usurper-web.service` had Windows CRLF line endings that would cause parse errors on Linux (nginx config errors, systemd service failures). Converted to LF.

### Armor/Weapon Shop Duplicate Items (Studded Belt x200)

- **Shops flooded with duplicate equipment**: The armor and weapon shops displayed thousands of duplicate items (e.g., 200 pages of "Studded Belt") due to a save system bug. Three root causes fixed:
  1. **NPC equipment saved as dynamic**: `SaveSystem.cs` used `>= 10000` to identify dynamic equipment IDs, but base equipment for Waist (10000+), Face (11000+), Cloak (12000+), Neck (13000+), and Ring (14000+) slots all fell in that range. Dynamic equipment actually starts at 100000. Every NPC's base armor was being saved as "dynamic equipment," re-registered on load as duplicates, and snowballing across save/load cycles. Fixed threshold to `>= 100000`.
  2. **Player save included all dynamic items**: The player save captured ALL dynamic equipment in the database (including NPC items), not just equipment the player had equipped. Fixed to only save the player's own equipped dynamic items.
  3. **Shop queries included dynamic items**: `EquipmentDatabase.GetBySlot()` and `GetWeaponsByHandedness()` returned all equipment including dynamic loot items. Now excludes dynamic items by default so shops only display the base catalog.

### Spouse/Lover Death Message Fix

- **Misleading permanent death messages for spouses/lovers**: When a spouse or lover died in combat, the messages said "gone forever" and "will never return" despite them being resurrectable at Home or the Team Corner. Messages now say "has fallen... Perhaps they can still be saved..."

### Dungeon Ascending Restriction Fix

- **Players couldn't ascend past level - 10 floors**: The `ChangeDungeonLevel()` function clamped the target floor to `playerLevel - 10` on the low end, preventing players from ascending to the surface. A level 40 player couldn't go above floor 30. Fixed: players can always ascend to any floor (minimum floor 1). The restriction now only applies to descending (can't go deeper than playerLevel + 10).

### Old God Floor Progression Gates

- **Players could bypass Old God boss fights**: Multiple holes in the dungeon floor gating system allowed players to skip mandatory Old God encounters:
  1. **`RequiresFloorClear()` didn't check Old God floors**: The method only checked `SealFloors` and `SecretBossFloors` (which was missing floors 40, 55, 70, 85, 95, 100). Now checks `OldGodFloors` directly.
  2. **`DescendStairs()` had no floor gate check**: Players could use stairs to descend past undefeated Old God floors without any restriction. Now blocks descent with "A powerful presence blocks your descent" message.
  3. **`DescendDeeper()` had no floor gate check**: Same bypass via the Descend Deeper command. Now blocks descent.
  4. **`ChangeDungeonLevel()` didn't check intermediate floors**: Players on floor 20 could jump to floor 30, bypassing the Old God gate on floor 25. Now caps target floor at the first undefeated Old God.
  5. **Seal floors were hard gates but shouldn't be**: `GetMaxAccessibleFloor()` treated seal floors (15, 30, 45, 60, 80, 99) as mandatory progression gates, blocking players who skipped a seal. Seals are now soft gates - players can skip them (only affects which endings are available). Only Old God floors (25, 40, 55, 70, 85, 95, 100) are hard progression gates.

### Old God Encounter Hint

- **No indication of Old God on boss floors**: When entering a boss room on an Old God floor (25, 40, 55, 70, 85, 95, 100) without meeting the prerequisite (e.g., defeating the previous god), the player just got a regular boss fight with no hint that something special should be there. Now displays: "You sense an ancient presence sealed away in this chamber... Perhaps you must prove yourself elsewhere before it reveals itself."

### Online Play Color Fix

- **Online Play had no colors (black and white text)**: When connecting to the online server via SSH (either through the website terminal or direct SSH), all game text appeared in plain white with no colors. Root cause: the server-side `TerminalEmulator` used `Console.ForegroundColor` (which only affects the local console) instead of ANSI escape codes (which travel through the SSH pipe to the client). The existing `DoorMode.IsInDoorMode` check controlled ANSI output, but online mode (`--online --stdio`) doesn't set a drop file, so `IsInDoorMode` was false. Fix: added a new `DoorMode.ShouldUseAnsiOutput` property that returns true for both BBS door mode AND online mode. Changed 6 locations in `TerminalEmulator.cs` and `GodotStubs.cs` from `IsInDoorMode` to `ShouldUseAnsiOutput`. `IsInDoorMode` itself was left unchanged to avoid side effects on character name locking, save paths, and SysOp detection.

### Duplicate Login Prevention

- **Players could log into the same character twice simultaneously**: No check prevented a player from opening multiple sessions with the same character (e.g., one via website terminal and one via direct SSH). This could cause save data corruption as both sessions read/write the same player record. Fix: added `IsPlayerOnline(username)` check to `SqlSaveBackend.cs` that queries the `online_players` table for an active heartbeat within 120 seconds. The check is enforced at both authentication entry points:
  - `OnlineAuthScreen.DoLogin()` - rejects login after successful password verification if already online
  - `Program.RunDoorModeAsync()` - rejects `--user` pre-authenticated sessions if already online
  - Displays "This character is already logged in from another session" with instructions to disconnect first
  - Fails open on database errors (won't block login if the DB query fails)

### Purify Spell Fix

- **Purify spell had no effect**: The Cleric's Purify spell (spell #3) could succeed its D20 roll but produced no actual game effect. Root cause: the spell set `SpecialEffect = "cure_disease"` but neither spell effect handler in `CombatEngine.cs` had a `case "cure_disease":` - the effect silently fell through. Fix: added a `cure_disease` handler that clears all disease status effects (`Diseased`, `Poisoned`), boolean disease flags (`Blind`, `Plague`, `Smallpox`, `Measles`, `Leprosy`, `LoversBane`), and the poison counter.

### Auto-Combat Potion Usage

- **Auto-combat never used healing potions**: Auto-combat mode was hardcoded to only create `Attack` actions every round, with no consideration for player HP. Players and companions would die in extended auto-combat fights when a potion could have saved them. Fix: auto-combat now checks if HP is below 50% of max and the player has potions available. If so, it uses a QuickHeal action (one potion, no prompts) instead of attacking. Also added missing `Heal`/`QuickHeal` cases to the multi-monster combat action processor. Companion/NPC potion thresholds also raised from 35%/25% to 50% to match.

### Spell Failure Feedback

- **Spell failures gave no explanation**: When spells fizzled, only generic messages like "the spell fails!" were shown. The D20 roll information was calculated internally but never displayed. Fix: spell failure messages now include the roll details (e.g., `[Roll: 3 + 2 = 5 vs DC 11]`), teaching players how the training system works and that improving proficiency increases success rates.

### Troll Racial Regeneration

- **Troll race had no passive regeneration**: Despite being a classic RPG trope (and monster Trolls having a `Regeneration` ability), Troll player characters had no regeneration mechanic - just static stat bonuses. Fix: Troll players now passively regenerate 1-3 HP per combat round (scales with level: `1 + Level/20`, max 3). Race help text updated to mention regeneration, and character creation now shows `*regeneration` tag (matching Gnoll's `*poisonous bite` pattern).

### Boss Room Monster HP Display Bug

- **Boss room monsters showed HP exceeding MaxHP (e.g., 3069/2046)**: When entering a boss room, `DungeonLocation.cs` applied a 1.5x HP multiplier to all monsters in the room (`m.HP = (long)(m.HP * 1.5)`) but never updated `MaxHP` to match. This caused the combat status display to show current HP greater than max HP (e.g., `3069/2046`). The HP bar would also render incorrectly (overfilled). Fix: `MaxHP` is now set to match the boosted HP value after the boss room multiplier is applied.

### Veloura (Floor 40) Old God Never Spawns

- **Second Old God (Veloura) could never be encountered**: After defeating Maelketh on floor 25, the prerequisite check for Veloura on floor 40 looked for story flags `maelketh_defeated` or `maelketh_encountered`, but neither flag was ever set. The actual flags set on Maelketh resolution are `maelketh_destroyed` (defeat path) and `maelketh_saved` (save path), set via `SetStoryFlag($"{boss.Type.ToString().ToLower()}_destroyed")`. Fix: changed Veloura's prerequisite to check `OldGodStates[Maelketh].Status` directly (matching how Thorgrim, Noctura, Aurelion, and Terravok already work), accepting any resolved status (Defeated, Saved, Allied, Awakened, Consumed).

### Veloura Save Quest Chain

- **Sparing Old Gods was instant instead of a quest**: Choosing the empathy/spare dialogue path for an Old God (e.g., Veloura's "What if the curse could be broken?" → "I will find this artifact and free you") immediately set the god to `GodStatus.Saved`, even though the dialogue explicitly tells the player to go find an artifact. The floor was cleared and the god disappeared before the quest could begin. Fix: sparing now sets `GodStatus.Awakened` (quest in progress) instead of `Saved`. The full save quest chain is:
  1. **Floor 40**: Spare Veloura via dialogue → `GodStatus.Awakened`, floor passable, `[QUEST STARTED]` message
  2. **Floor 65**: Discover the Soulweaver's Loom artifact (triggered by `veloura_save_quest` flag). Optional moral paradox about the Loom's cost (Lyris's sacrifice) plays afterward.
  3. **Floor 40 (return)**: With the Loom in hand, Veloura reappears for a special save completion scene → `GodStatus.Saved`, +XP, +100 Chivalry, Ocean Philosophy fragment
  - `CanEncounterBoss()` now allows re-encounter with Awakened gods when the player has the required artifact
  - Floor 65 story event changed from requiring the SoulweaversLoom to granting it (was a chicken-and-egg problem)
  - Moral paradox (`velouras_cure`) no longer changes god state directly; the actual save happens on the return visit

### Quick Potion Command

- **No way to use potions outside combat**: Players had to enter combat or navigate to the Healer to use healing potions. Fix: added `/potion` (alias `/pot`) slash command that works from any location. Uses one potion, shows HP and remaining potion count. Handles edge cases (full HP, no potions). Added to `/help` command list.

### Unidentified Weapon Equip Exploit

- **Unidentified weapons could be equipped immediately**: Picking up an unidentified weapon from a loot drop showed the `(E)quip immediately` option, and equipping it bypassed the identification system entirely, revealing the item's true name and stats. Similarly, unidentified items in the inventory could be equipped via the `[E] Equip Item` option. Fix: the equip option is now hidden on loot drops for unidentified items, and grayed out with "(must identify first)" in the inventory menu. Both paths block equipping with a message directing the player to the Magic Shop. Display names use `GetUnidentifiedName()` throughout to prevent stat/name leaks.

### Hall of Recruitment Removed

- **Hall of Recruitment crash and redundancy**: The Hall of Recruitment at the Inn was a Godot-era leftover that used `GodotHelpers.GetNode<TeamSystem>()`, causing an immediate crash when trying to hire a mercenary. The feature was also redundant with the Team Corner, which already handles NPC team recruitment. The Hall of Recruitment has been fully removed from the Inn menu, location manager, and navigation system.

### Files Changed
- `Scripts/Core/GameConfig.cs` - Version 0.27.1-alpha
- `Scripts/Locations/InnLocation.cs` - Seth Able exploit redirect in ChallengeNPC()
- `Scripts/Locations/BaseLocation.cs` - Filter IsSpecialNPC from generic talk list, /potion quick command
- `Scripts/Core/GameConfig.cs` - Troll race help text updated with regeneration
- `Scripts/Systems/VersionChecker.cs` - Linux auto-updater CRLF, pgrep, and cp fixes
- `Scripts/Systems/SaveSystem.cs` - NPC dynamic equipment threshold fix (10000 → 100000), player save only includes equipped dynamic items
- `Scripts/Data/EquipmentData.cs` - GetBySlot/GetWeaponsByHandedness/GetAccessories/GetBestAffordable exclude dynamic items by default
- `Scripts/Systems/CombatEngine.cs` - Spouse/lover death messages, cure_disease handler, auto-combat potion logic, Troll regen, Heal/QuickHeal in multi-monster combat
- `Scripts/Systems/SpellSystem.cs` - Roll info shown on spell failure messages
- `Scripts/Systems/CharacterCreationSystem.cs` - Troll *regeneration tag in race selection
- `Scripts/Locations/DungeonLocation.cs` - Old God floor progression gates (RequiresFloorClear, DescendStairs, DescendDeeper, ChangeDungeonLevel, GetMaxAccessibleFloor), ascending fix, Old God hint, boss room MaxHP sync fix
- `web/ssh-proxy.js` - LIMIT 25 on leaderboard SQL query
- `web/index.html` - .slice(0, 25) frontend leaderboard cap
- `Scripts/BBS/DoorMode.cs` - Added `ShouldUseAnsiOutput` property for online mode ANSI output
- `Scripts/UI/TerminalEmulator.cs` - 5 locations changed from `IsInDoorMode` to `ShouldUseAnsiOutput`
- `Scripts/Utils/GodotStubs.cs` - SafeClearScreen() uses `ShouldUseAnsiOutput`
- `Scripts/Systems/SqlSaveBackend.cs` - Added `IsPlayerOnline()` duplicate session check
- `Scripts/Systems/OnlineAuthScreen.cs` - Duplicate login check after authentication
- `Console/Bootstrap/Program.cs` - Duplicate login check for `--user` pre-authenticated path
- `Scripts/Systems/OldGodBossSystem.cs` - Veloura prerequisite fix, spared→Awakened, CompleteSaveQuest(), GetArtifactForSave(), CanEncounterBoss allows Awakened re-encounter
- `Scripts/Systems/MoralParadoxSystem.cs` - velouras_cure paradox: removed GodSaved/GodNotSaved effects, updated text to direct player back to floor 40
- `scripts-server/nginx-usurper.conf` - CRLF → LF line ending fix
- `scripts-server/usurper-web.service` - CRLF → LF line ending fix
- `Scripts/Systems/CombatEngine.cs` - Hide equip option for unidentified loot, use unidentified display names
- `Scripts/Systems/InventorySystem.cs` - Gray out equip for unidentified items, block with explanation message
- `Scripts/Locations/HallOfRecruitmentLocation.cs` - **DELETED** - Godot-era leftover causing crashes
- `Scripts/Locations/InnLocation.cs` - Removed Hall of Recruitment menu/nav
- `Scripts/Systems/LocationManager.cs` - Removed Hall of Recruitment registration and navigation
- `Scripts/Systems/DialogueSystem.cs` - FindNode fix for Veloura save quest dialogue tree traversal
