# Usurper Reborn v0.40.3 — PvP Combat & Quest Fixes

## Bug Fix: Spells & Abilities Missing from PvP Combat

PvP combat (Arena, guard fights, sleep attacks, NPC duels) was missing the full combat initialization that dungeon combat had. This meant:

- **Quickbar slots [1]-[9] were empty** — spells and abilities never appeared in the combat menu
- **Class abilities couldn't be used** — even if triggered, they silently defaulted to a basic attack
- **Combat stamina was never initialized** — abilities showed as unavailable
- **Ability cooldowns carried over** between separate fights
- **Barbarian Rage didn't work** in PvP
- **Fleeing PvP always failed** — pressing [R] Flee said "not available" and forced a basic attack instead

All PvP-style combat (Character vs Character) now has full combat initialization matching dungeon fights: stamina, ability loading, cooldown clearing, temp bonus resets, poison coating, and divine favor. The combat menu now shows your quickbar spells and abilities just like in dungeon combat.

### New PvP Combat Actions

- **[1]-[9] Quickbar** — Cast spells and use abilities directly from quickbar slots. Spells cast immediately without re-showing the selection menu.
- **Class Abilities** — Full ability selection menu with stamina costs, cooldowns, and all ability effects (damage, healing, buffs) working against player/NPC targets.
- **[G] Rage** — Barbarian rage now works in PvP combat.
- **[R] Flee** — Actually attempts to flee (AGI-based, 10-75% chance) instead of forcing a basic attack.
- **Healing spells** — Self-healing spells now restore HP in PvP combat.

## Bug Fix: "Clear Floor X" Quest Never Completed

The "Clear Floor X" dungeon quest required killing an arbitrary number of monsters (based on a formula) that didn't match how many monsters actually existed on the floor. A player could clear every single monster on the target floor and still not complete the objective.

**Before**: Objective required killing `5 + (difficulty * 3)` monsters anywhere, counting each kill as +1 progress. The target number often exceeded the actual monster count on the floor.

**After**: Objective now checks the actual floor-cleared state — when all monster rooms on the target floor are cleared (the same condition that triggers the "Floor Cleared!" message), the quest objective completes immediately.

Note: Existing "Clear Floor" quests created before this fix still use the old kill-count objective. Abandon and pick up a new quest to get the fixed version.

## Bug Fix: NPCs Showing Wrong Location Flavor Text

NPCs could show flavor text from a previous location — for example, Lady Morgana "having a drink at the bar" while standing in the Weapon Shop. This happened because the NPC's `CurrentActivity` was set when they performed an action (like visiting the Inn) but never cleared when they moved to a different location. The old activity text persisted and was displayed instead of the correct location-specific flavor text.

`CurrentActivity` is now cleared whenever an NPC moves to a new location, allowing the location-contextual flavor text system to provide appropriate descriptions (e.g., "examining the weapons" at the Weapon Shop, "browsing potions" at the Healer).

## Bug Fix: NPC Talk Crash for Direct SSH Players

Talking to NPCs could crash the game with a `NullReferenceException` when the player object wasn't fully resolved in the dialogue system. The `SubstitutePlaceholders` method in `NPCDialogueDatabase` dereferenced the player's name and class without null checks, which failed in certain MUD connection paths (notably direct SSH). Added null guards throughout the method so dialogue generation gracefully falls back to defaults ("stranger", "adventurer") when the player reference is unavailable.

## Bug Fix: Admin Password Reset Immediately Failing

The MUD admin console's password reset feature immediately printed "Password must be at least 4 characters. Cancelled." without letting the admin type anything. The password prompt used `TerminalEmulator.ReadLineWithBackspace()` — a static Console method that doesn't work in MUD mode where I/O goes through the terminal abstraction layer. Replaced with the proper `ReadInput()` method that routes through the MUD terminal.

## Bug Fix: Quit Cancel Still Disconnects Player

In online mode, pressing [C] Cancel at the "Where do you rest?" logout menu printed "You decide to stay in town." but immediately disconnected the player anyway. The `QuitGame()` method returned void, and the caller in `ProcessChoice` unconditionally returned `true` (exit location), so cancelling had no effect. Changed `QuitGame()` to return `bool` — cancel and Inn navigation now return `false` (stay in location), while dormitory and regular quit paths throw `LocationExitException` as intended.

## Bug Fix: Missing Person Petition Gave Gold Instead of a Quest

The "Missing Person" NPC petition's [I] Investigate option immediately handed the player gold and XP with no follow-up. The player promised to investigate but there was nothing to actually do. Now choosing [I] creates a proper quest with two objectives: reach the dungeon floor where the missing person was last seen, and slay monsters there to uncover clues. A small gold advance is given for expenses, with the real reward coming on quest completion. If the player already has 5 active quests, they're told to complete one first.

## Bug Fix: Two-Handed Weapons Becoming One-Handed After Unequip/Re-equip

Two-handed weapons (Zweihander, Claymore, Spear, War Maul, and many others) lost their two-handed property when unequipped to inventory and re-equipped. This happened because the Item-to-Equipment conversion in both the inventory system and loot system guessed weapon handedness from a hardcoded name list that was missing most two-handed weapon names. In the loot system, **all** weapons were hardcoded as one-handed regardless of name.

**Impact**: A two-handed weapon that went through inventory (e.g., buying a shield forced unequipping your greatsword, then re-equipping after turning in the quest) would permanently become one-handed in the save data. This also caused cascading slot confusion — equipping armor could compare against the wrong slot or show the wrong currently-equipped item.

**Fix**: Both the inventory equip and loot equip paths now look up the weapon in the equipment database by name to get the correct handedness and weapon type. The name-based fallback heuristic was also expanded as a safety net.

## Bug Fix: Day Counter Going Backwards in Online Mode

In online/MUD mode, the day counter could decrease between sessions (e.g., "Day 5" dropping to "Day 4"). This happened because the day counter was stored on a shared singleton (`DailySystemManager`) that all concurrent player sessions wrote to. When multiple players were online, loading one player's save would overwrite the shared day counter, and other players would then save with the wrong day value.

The day counter is now stored per-session on each player's `GameEngine` instance. Each player's day value is read from their save on login and written back from their own session state, preventing cross-contamination between concurrent players.

## Bug Fix: Quests Wiped on Login in Online Mode

All active quests disappeared every time a player logged in. Three compounding issues:

1. **Shared quest database cleared on every login** — `QuestSystem.RestoreFromSaveData()` called `questDatabase.Clear()`, wiping ALL players' quests from the shared static database whenever any player logged in.
2. **Player's ActiveQuests list never restored** — The quest data was properly saved but never read back during player restore, so the player object always started with an empty quest list.
3. **Cross-contamination** — In MUD mode, when Player B logged in, their restore call would clear Player A's quests from the shared database.

**Fix**: In online mode, player logins now merge their quests into the shared database (removing only their own quests first, then re-adding from save data) instead of clearing the entire database. The player's `ActiveQuests` list is also properly restored by linking to the quest database entries.

## Bug Fix: Dungeon Floor Progress Lost on Login

All dungeon room exploration and cleared state was lost every time a player logged in. Floor 1 would show "0/15 rooms explored" even if the player had fully cleared it in a previous session.

This was caused by a deliberate reset (`player.DungeonFloorStates = new Dictionary()`) added as a workaround for a boss-room display bug that has since been properly fixed via the `BossDefeated` flag. The dungeon floor state was being correctly saved to the database but then discarded on load.

Dungeon floor states (which rooms are explored, cleared, treasure looted, etc.) now persist properly across sessions. Permanent progress (seals, Old Gods) was already preserved and is unaffected. Players who want to re-fight monsters on cleared floors can use a Dungeon Reset Scroll from the Magic Shop.

## Bug Fix: Can't Sell Rings/Amulets at Magic Shop

The Magic Shop refused to buy rings, amulets, and belts, saying "I only deal in magical goods - rings, amulets, belts, and the like." The sell check only accepted items with `ObjType.Magic` or a `MagicType` property set, but accessories from loot and equipment conversions have their actual slot type (`Fingers`, `Neck`, `Waist`) instead. The shop now accepts all accessory types as valid sellable items.

## Balance: Dormitory Sleep Attacks Retuned

NPC attacks on sleeping players were far too aggressive — players were almost guaranteed to be murdered even after logging out for just a few minutes. Additionally, Good and Holy NPCs were attacking sleepers, which made no thematic sense.

**Changes:**
- **Alignment restriction**: Only Dark and Evil alignment NPCs will attack sleeping players. Good, Holy, and Neutral NPCs no longer murder people in their sleep.
- **Dormitory attack rate reduced**: 8% per tick → 2% per tick (~18% chance in 5 minutes, ~70% in 30 minutes instead of the previous ~57% in 5 minutes)
- **Inn now has a separate, much lower attack rate**: 0.5% per tick (~5% chance in 5 minutes, ~26% in 30 minutes). Previously inn sleepers used the same rate as dormitory — the only benefit was a stat boost if attacked.

## Bug Fix: Chat Messages Appearing During Character Creation

Gossip, shouts, and other broadcast messages from other players appeared inline during character creation (race selection, stat rolling, etc.), cluttering the screen and confusing new players. Broadcast messages are now suppressed until the player has fully loaded or created their character and entered the game world.

## Improvement: Bug Report Build Type & Player Name

Bug reports now distinguish between Local, Online, Steam, and BBS Door builds (previously all non-Steam/non-BBS showed as "Standard"). The player's character name is now included in all bug report formats (Discord, local file, GitHub) for easier tracking.

## Improvement: --version CLI Flag

Running `UsurperReborn --version` (or `-v` / `-V`) now prints the version string and exits immediately. Previously, the game didn't recognize this flag and would launch the full game engine with no terminal attached, spinning at 100% CPU indefinitely as a zombie process.

## Rename: Marketplace → Auction House

The old "Marketplace" location has been renamed to "Auction House" throughout the entire game. This affects menu labels, NPC location text, Crystal Ball lookups, news messages, opening story narration, and all internal references. NPCs now show as being "at the Auction House" instead of "at the Market," and the Main Street menu displays `[J] Auction House` instead of `[J] Marketplace`. The old name is still accepted for backward compatibility in commands and lookups.

## Bug Fix: Screen Reader Menus Out of Sync

The screen reader accessibility menus had two discrepancies with the normal visual menus:
- Main Street screen reader menu still listed `Z - Team Area` which was removed from the game (moved to Inn).
- Dungeon combat screen reader menu was missing `V - Attempt to Save (Soulweaver's Loom)` for Old God boss fights.

Both menus now match their visual counterparts exactly.

## Bug Fix: Church NPCs Invisible

The Crystal Ball could show an NPC at "Church of Good Deeds" but visiting the Church showed no one there. This happened because the Church location's internal NPC location string was mapped to "Temple" instead of "Church" — so NPCs placed at the Church were filtered as Temple NPCs and invisible at both locations.

## Bug Fix: Duplicate Faction Mission Targets

Players could receive the same NPC as a faction mission target multiple times from different petition NPCs. For example, getting "Redeem Grog the Destroyer" from one NPC and then the same mission from another. The Faith and Crown faction missions also always picked the same NPC (the first match) instead of randomizing. Faction missions now check your active quests and exclude NPCs you already have missions for, and targets are randomized.

## Bug Fix: Animals and Mindless Creatures "Saying" Dialogue

Non-speaking monsters (wolves, spiders, golems, water creatures, aberrations, elementals) were shown as `The Wolf says: "*Snarls and growls*"` — wrapping their sounds in quotation marks as if they were speech. These creature families now show their actions as narration instead: `The Wolf snarls and growls menacingly.` Speaking creatures (goblins, orcs, demons, dragons, fey, giants, undead, celestials, shadows) still use proper dialogue with `says:` and quotes.

## Files Changed

| File | Change |
|------|--------|
| `Scripts/Core/GameConfig.cs` | Version 0.40.3 |
| `Scripts/Systems/CombatEngine.cs` | Full combat initialization in `PlayerVsPlayer()` (stamina, abilities, cooldowns, temp bonuses); added `ClassAbility`, `Rage`, `RangedAttack`, `Retreat` handlers to `ProcessPlayerVsPlayerAction()`; new `ExecutePvPAbility()` method for abilities against Character targets; `ExecutePvPSpell()` now accepts quickbar-triggered spells directly and handles healing; flee mechanic with AGI-based success chance |
| `Scripts/Systems/QuestSystem.cs` | Changed `ClearFloor` objective from arbitrary kill count to binary floor-cleared check (`RequiredProgress = 1`); removed per-kill `ClearDungeonFloor` increment from `OnMonsterKilled()`; added `OnDungeonFloorCleared()` method |
| `Scripts/Locations/DungeonLocation.cs` | Wired `QuestSystem.OnDungeonFloorCleared()` into `SaveFloorState()` when `EverCleared` is first set |
| `Scripts/Core/NPC.cs` | Clear `CurrentActivity` in `UpdateLocation()` when NPC moves to a new location |
| `Scripts/Data/NPCDialogueDatabase.cs` | Null guards in `SubstitutePlaceholders()` for player name, class, and king status |
| `Scripts/Locations/BaseLocation.cs` | Extract player variable in `ChatWithNPC()` to avoid redundant casts |
| `Scripts/Systems/OnlineAdminConsole.cs` | Password reset uses `ReadInput()` instead of static `ReadLineWithBackspace()` |
| `Scripts/Locations/MainStreetLocation.cs` | `QuitGame()` returns `bool` so cancel/Inn navigation stays in location |
| `Scripts/Systems/BugReportSystem.cs` | Added `IsOnlineMode` flag; `GetBuildTypeString()` for Local/Online/Steam/BBS; player name in all outputs |
| `Scripts/Systems/NPCPetitionSystem.cs` | Missing Person [I] Investigate creates a quest (reach floor + kill monsters) instead of giving immediate gold |
| `Scripts/Systems/QuestSystem.cs` | Added `AddQuestToDatabase()` public method for external quest creation |
| `Scripts/Systems/InventorySystem.cs` | Equip from backpack looks up weapon in equipment database for correct handedness/type instead of name guessing |
| `Scripts/Systems/CombatEngine.cs` | Loot weapon equip looks up equipment database for handedness instead of hardcoding OneHanded |
| `Scripts/Core/GameEngine.cs` | Per-session daily state (`SessionCurrentDay`, `SessionLastResetTime`, `SessionDailyCycleMode`) to prevent cross-contamination between concurrent MUD sessions |
| `Scripts/Systems/DailySystemManager.cs` | Sync per-session day state on daily reset |
| `Scripts/Systems/SaveSystem.cs` | Use per-session day state in online mode instead of shared singleton |
| `Scripts/Systems/QuestSystem.cs` | Added `MergePlayerQuests()` for per-player quest merge without clearing shared database |
| `Scripts/Core/GameEngine.cs` | Restore dungeon floor states from save data instead of resetting; merge player quests in MUD mode; restore `player.ActiveQuests` from quest database on load |
| `Scripts/Locations/MagicShopLocation.cs` | Sell check accepts accessory types (`Fingers`, `Neck`, `Waist`) in addition to `Magic` type |
| `Scripts/Core/GameConfig.cs` | Reduced dormitory attack rate (8% → 2%); added separate inn attack rate (0.5%) |
| `Scripts/Systems/WorldSimulator.cs` | Sleep attacks restricted to Dark/Evil alignment NPCs; dormitory vs inn attack rate differentiation |
| `Scripts/Server/PlayerSession.cs` | Added `IsInGame` flag to suppress broadcasts during login/character creation |
| `Scripts/Server/MudServer.cs` | `BroadcastToAll` skips sessions where `IsInGame` is false |
| `Scripts/Core/GameEngine.cs` | Set `IsInGame = true` after character load/creation completes |
| `Console/Bootstrap/Program.cs` | Handle `--version` / `-v` / `-V` flag: print version and exit immediately |
| `Scripts/Core/GameConfig.cs` | Renamed `Marketplace` enum to `AuctionHouse` |
| `Scripts/Locations/BaseLocation.cs` | Fixed Church NPC location string; updated AuctionHouse display names and NPC activity descriptions |
| `Scripts/Locations/MainStreetLocation.cs` | Menu text `Marketplace` → `Auction House`; removed `Z - Team Area` from screen reader menu |
| `Scripts/Core/GameEngine.cs` | Updated NPC location strings and location lookup aliases for Auction House |
| `Scripts/Core/NPC.cs` | Merchant default location and guard patrol updated to "Auction House"; added "auctionhouse" lookup alias |
| `Scripts/Systems/StreetEncounterSystem.cs` | Updated encounter location key to AuctionHouse |
| `Scripts/Systems\LocationManager.cs` | Updated location registration and navigation table for AuctionHouse |
| `Scripts/Locations/MarketplaceLocation.cs` | Updated display name and description to "Auction House" |
| `Scripts/Server/WizardCommandSystem.cs` | Added "auction"/"auctionhouse" aliases for teleport command |
| `Scripts/Systems/WorldSimulator.cs` | Updated NPC location strings, social locations, news messages to "Auction House" |
| `Scripts/Systems/NPCMaintenanceEngine.cs` | Updated territory location strings |
| `Scripts/Systems/NPCSpawnSystem.cs` | Updated spawn location list |
| `Scripts/Locations/TeamCornerLocation.cs` | Updated town location filter |
| `Scripts/Core/King.cs` | Updated establishment status key |
| `Scripts/Locations/DevMenuLocation.cs` | Updated marketplace stats/clear menu text |
| `Scripts/Systems/StrangerEncounterSystem.cs` | Updated Mysterious Merchant location filter |
| `Scripts/Systems/OpeningStorySystem.cs` | Updated narrative text |
| `Scripts/Systems/MarketplaceSystem.cs` | Updated news messages to "Auction House" |
| `Scripts/Systems/NPCPetitionSystem.cs` | Added duplicate target prevention and randomization for faction missions |
| `Scripts/Systems/CombatEngine.cs` | Added `V - Attempt to Save` to dungeon combat screen reader menu |
