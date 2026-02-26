# Usurper Reborn v0.47.2 — BBS → Online Server Bridge

## BBS Door Mode Can Now Connect to the Online Server

Players running Usurper Reborn through a BBS (Synchronet, EleBBS, Mystic, etc.) can now access the `[O]nline Multiplayer` option from the main menu and connect to the shared online server at `usurper-reborn.net`.

Previously, the `[O]nline Play` option was hidden in BBS door mode on the assumption that BBS users were "already online." That assumption was wrong — BBS door mode is a local single-player session running on the sysop's machine; it has nothing to do with the central online server. Players connecting to a BBS and playing the local door game are playing their own private save, separate from the shared world.

Now BBS door players have the same choice as local/Steam players: play the local BBS game, or jump into the shared server.

## What Changed

### `[O]nline Multiplayer` visible in BBS mode

The main menu now shows `[O]nline Multiplayer` for BBS door users, identical to local and Steam play. The option remains hidden only when the game is already running in online server mode (i.e., a player is already connected to the shared server — no reason to show it again).

### BBS-safe I/O in PipeIO

The relay loop (`PipeIO`) that bridges the local session to the online server's SSH connection has been updated to use BBS-appropriate I/O instead of raw Console APIs:

- **Output** (server → BBS player): Game server output (ANSI-encoded text) is passed through the BBS terminal abstraction (`terminal.WriteRawAnsi()`). In BBS socket mode, the bytes go directly to the BBS socket; in BBS stdio mode, they go to stdout. Either way, the player's terminal receives the ANSI codes and renders them normally.

- **Input** (BBS player → server): Uses `terminal.GetInput()` which routes through the BBS adapter — reading from the BBS socket in socket mode, or from stdin in stdio mode. Input is line-buffered (the player types a command and presses Enter), which is identical to the behavior of SSH and web terminal users.

- **Console encoding**: `Console.OutputEncoding = Encoding.UTF8` is skipped in BBS mode (irrelevant when output goes through the socket, not the console).

### BBS-safe password masking

The login and registration password prompts previously called `TerminalEmulator.ReadLineWithBackspace()` directly (a static, Console-only method). These now use `terminal.GetMaskedInput()`, which already handles MUD stream, BBS socket, BBS stdio, and Console modes — no change in behavior for non-BBS modes.

### Connection type shows as "BBS" on the server

When a BBS door player connects to the online server, their connection type in `/who` and on the website now correctly shows as `BBS` instead of `Local`.

### Server disconnect behavior in BBS mode

When the game server closes the SSH connection (e.g., player quits the game, server restart), a message is printed: `Server closed the connection. Press Enter to return.` The player presses Enter, which unblocks the input wait, and control returns to the BBS main menu. This is consistent with how BBS door games handle external connection events.

## BBS-Compatible ANSI Color Codes

BBS terminals (SyncTERM, NetRunner, mTelnet) connected via telnet were displaying most text as uniform gray instead of the intended colors. Bright yellow, white, dark gray, and other colors all appeared as the same shade.

**Root cause**: The game used extended ANSI color codes (90–97 range, e.g., `ESC[93m` for bright yellow) which modern terminal emulators support but traditional BBS terminals do not. BBS terminals expect the classic "bold + standard color" format (`ESC[1;33m` for bright yellow).

**Fix**: All three ANSI color code dictionaries (TerminalEmulator, BBSTerminalAdapter, SocketTerminal) now use BBS-compatible codes when running in door mode. Bright colors use `1;3X` (bold + color), standard colors use `0;3X` (explicit reset clears bold when transitioning from bright). Gray maps to `0;37` (standard white) and dark gray maps to `1;30` (bold black), both of which render correctly on BBS terminals.

## BBS Compact Menus for Online BBS Connections

When a BBS user connects to the online server through `[O]nline Multiplayer`, their session on the server side now uses compact BBS-style menus (designed for 80x24 terminals) instead of the full-width menus. Previously, `IsBBSSession` only checked `DoorMode.IsInDoorMode` (local door mode), which is false on the server. Now it also detects `ConnectionType == "BBS"` from the MUD server session context.

## Generic Bounty Quests Now Completable

Generic bounty quests from the King (WANTED: Escaped Prisoner, Bandit Leader, Cult Leader, Rogue Mage, Orc Warlord) were impossible to complete. The quests referenced monster names like "Escaped Convict" and "Dark Apprentice" that don't exist in any monster family, and no quest objectives were ever created, so the quest log showed "no objectives tracked."

**Fix**: Generic bounties now create a proper `KillMonsters` objective — "Slay N monsters in the dungeon" — that tracks via the existing `OnMonsterKilled()` hook. Any dungeon monster kill counts toward completion. Kill count scales with bounty difficulty (5–15 kills). The bounty flavor text still tells a story, but the objective is practical and achievable.

## Prestige Ability Kill-Tracking Crash Fix

Killing a boss (including Old God bosses like Veloura) with a Cyclebreaker, Singularity, Tidesworn, Wavecaller, Abysswarden, or Voidreaver prestige ability caused the game to crash with "Error loading save: Sequence contains no elements" after showing "0 MONSTERS SLAIN!" in the victory box.

**Root cause**: Prestige ability special-effect cases in the combat engine applied their own bonus damage to the target on top of the base damage hit. If the base hit didn't kill the enemy but the bonus hit did, the enemy's death went unrecorded in `DefeatedMonsters`. The victory handler then called `.Max()` on an empty collection, throwing `InvalidOperationException`.

**Fix**: All 17 prestige ability cases that apply their own damage now explicitly record kills in `DefeatedMonsters`. Fixed cases span all five prestige classes: Tidesworn (`sanctified_torrent`, `wrath_deep`), Wavecaller (`double_vs_debuffed`, `crescendo_aoe`, `resonance_cascade`, `grand_finale`), Cyclebreaker (`cycles_end`, `singularity`), Abysswarden (`overflow_aoe`, `soul_leech`, `abyssal_eruption`, `consume_soul`, `abyss_unchained`, `shadow_harvest`), and Voidreaver (`lifesteal_30`, `execute_reap`, `devour`, `entropic_blade`, `void_rupture`, `annihilation`). A post-ability death sweep was also added at the end of the ability effects handler as a global defense-in-depth — any monster reduced to 0 HP by any case is caught and recorded even if the individual case missed it. The `.Max()` call in the victory handler is also guarded with `.Any()` to prevent a crash even if somehow `DefeatedMonsters` is empty.

## Prestige Buff Ability Duration Fix (Timeline Split, Chrono Surge, Blood Frenzy)

Three Cyclebreaker and Voidreaver prestige abilities that grant `Haste` (doubled attacks) were giving fewer rounds of benefit than advertised.

**Root cause**: `ProcessStatusEffects()` runs at the **start** of each round, before the player acts. This decrements all status durations by 1 before the player gets to attack. So an ability setting `Haste = 3` would expire after 2 rounds of attacks, not 3.

- **Timeline Split** (Cyclebreaker): "temporal clone attacks for 3 rounds" — was giving 2 rounds of doubled attacks. Fixed: initial duration set to 4.
- **Chrono Surge** (Cyclebreaker): "double actions this round" — was giving 0 rounds of benefit (Haste was immediately decremented to 0 and removed before the player ever attacked). Fixed: initial duration set to 2, giving exactly 1 round.
- **Blood Frenzy** (Voidreaver): "double attack for 3 rounds" — same off-by-one as Timeline Split. Fixed: initial duration set to 4.

## NPC Team Members No Longer Killed by World Simulator

NPC team members recruited by the player (e.g., in the dungeon) could silently disappear from the team shortly after recruitment. The player would recruit an NPC, leave the dungeon, and find them gone with no explanation.

**Root cause**: The world simulator runs NPC-vs-NPC combat every tick (brawls from enemy rivalries, simulated dungeon runs, etc.). Player team NPCs were only protected by the `IsInConversation` flag, which is set while the player is *inside* the dungeon but cleared when the player leaves. Once the player left the dungeon, their team NPCs were fully exposed to world sim combat — they could be targeted by rival NPCs, dragged into autonomous dungeon runs, and killed in simulated fights.

**Fix**: Four `IsPlayerTeam` guards added to `WorldSimulator.cs`:
1. `ExecuteAttack()` — NPC-vs-NPC combat now skips any fight where either participant is on a player's team
2. Enemy rivalry escalation loop — player team NPCs are excluded as both aggressors and targets
3. `ProcessNPCActivities()` — player team NPCs no longer go on autonomous dungeon runs, shopping trips, or other activities that could get them killed
4. `MarkNPCDead()` — defense-in-depth: if a player team NPC somehow reaches 0 HP through the world sim, they are healed to 25% HP instead of being marked dead

## Steam Launch Date & Website

The Steam store page is live — Usurper Reborn launches **March 13, 2026**. The Steam wishlist widget is now embedded near the top of the website at usurper-reborn.net.

## Files Changed

- `GameConfig.cs` — Version 0.47.2
- `Scripts/Core/GameEngine.cs` — Removed `!IsInDoorMode` from `[O]nline Play` menu display and handler conditions; `isOnlineAdmin` check expanded to include "fastfinge"
- `Scripts/Systems/OnlinePlaySystem.cs` — `GetConnectionType()` returns `"BBS"` in door mode; three password prompts use `terminal.GetMaskedInput()` instead of static `ReadLineWithBackspace()`; `PipeIO()`: `Console.OutputEncoding` guarded; read task uses `terminal.WriteRawAnsi()` for BBS mode; write loop split into BBS branch (`terminal.GetInput()`) and local/Steam branch (existing Console polling); read task `finally` block notifies BBS player to press Enter on disconnect; reconnect paths use port 4001 for TCP mode
- `Scripts/Systems/CombatEngine.cs` — All 17 damage-dealing prestige ability cases across 5 classes now record kills in `DefeatedMonsters`; post-ability death sweep added after switch statement as defense-in-depth; `HandleVictoryMultiMonster` `.Max()` guarded with `.Any()`; `timeline_split` Haste duration corrected from 3→4; `chrono_surge` Haste duration corrected from 1→2; `blood_frenzy` Haste duration corrected from 3→4
- `Scripts/Systems/WorldSimulator.cs` — Four `IsPlayerTeam` guards added: `ExecuteAttack()` skips combat involving player team NPCs; enemy escalation loop excludes player team members as aggressors and targets; `ProcessNPCActivities()` early-returns for player team NPCs; `MarkNPCDead()` heals player team NPCs to 25% HP instead of killing them
- `Scripts/Systems/QuestSystem.cs` — `CreateGenericBounty()` rewritten: removed phantom monster names; now creates proper `KillMonsters` objective with scaled kill count; any dungeon monster kill counts toward completion
- `Scripts/Systems/OnlineAdminConsole.cs` — Updated admin access comment
- `Scripts/UI/TerminalEmulator.cs` — Added `BbsAnsiColorCodes` dictionary with BBS-compatible bold-attribute format; `GetAnsiColorCode()` uses BBS codes when `DoorMode.IsInDoorMode`; `WriteRawAnsi()` uses `WriteRaw()` instead of `Write()` for BBS adapter path; BBS password masking fix in `ReadLineInteractiveAsync()`
- `Scripts/BBS/BBSTerminalAdapter.cs` — ANSI color codes updated to BBS-compatible format (bold for bright, explicit reset for standard); added `WriteRaw()` method; added `GetRawOutputStream()` accessor
- `Scripts/BBS/SocketTerminal.cs` — ANSI color codes updated to BBS-compatible format throughout
- `Scripts/Locations/BaseLocation.cs` — Added `IsBBSSession` property that detects both local door mode and MUD server BBS connections
- `Scripts/Locations/MainStreetLocation.cs` — Admin access check expanded to include "fastfinge" alongside "Rage"
- 14 location files (`AnchorRoadLocation.cs`, `ArmorShopLocation.cs`, `BankLocation.cs`, `CastleLocation.cs`, `ChurchLocation.cs`, `DarkAlleyLocation.cs`, `DungeonLocation.cs`, `HealerLocation.cs`, `HomeLocation.cs`, `InnLocation.cs`, `LevelMasterLocation.cs`, `LoveStreetLocation.cs`, `MagicShopLocation.cs`, `MarketplaceLocation.cs`, `TeamCornerLocation.cs`, `WeaponShopLocation.cs`) — Changed `DoorMode.IsInDoorMode` to `IsBBSSession` for compact menu detection
- `scripts-server/usurper-mud.service` — Port corrected from 4000 to 4001 (sslh occupies 4000); added `--admin Rage --admin fastfinge` flags
- `web/index.html` — Steam launch date updated to March 13, 2026; Steam store widget embedded below announcement banner
