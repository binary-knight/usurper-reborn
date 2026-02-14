# Usurper Reborn - v0.30.2 Bug Fixes & World Sim Pacing

Fixes lever puzzles, NPC respawn spam, location name corruption, self-pickpocketing, broken quest system, and online player tracking. Adds realistic NPC pacing for the online server, overhauls the auction house with listing fees and duration options, and adds real-time chat with gossip channel.

---

## Online World Simulator Pacing

The world simulator was designed for single-player sessions and ran way too fast on a persistent 24/7 server — NPCs were fighting, divorcing, forming teams, and making enemies every 30 seconds. Added comprehensive rate-limiting for online mode only (single-player is unchanged):

- **NPC activity rate**: Reduced from 15% to 5% chance per NPC per tick. NPCs take meaningful actions ~3 times per tick instead of ~9.
- **Rivalry creation**: Reduced from 8% to 2% chance per tick. Enemy list capped at 5 per NPC. New rivalries develop over hours, not minutes.
- **Rivalry escalation**: Reduced from 12% to 3% chance per tick. Per-pair cooldown (5 minutes between escalations). Daily combat cap of 3 fights per NPC per sim-day.
- **"Tensions rising" message spam**: Cooldown of 30 minutes per enemy pair before the same pair generates another tension message.
- **Divorce rate**: Reduced from 0.3% to 0.02% per couple per tick. Average marriage now lasts ~40 hours instead of <3 hours.
- **Team formation**: Reduced join chance from 75% to 30%, form chance from 50% to 15%. Per-NPC cooldown of 15 minutes between team actions.
- **Team abandonment**: Reduced from 1% to 0.1% per tick for likely-to-leave members.
- **Enemy reconciliation**: Every sim-day (~1 hour), enemies have a 10-25% chance to bury the hatchet (based on personality — peaceful NPCs reconcile more, aggressive ones hold grudges).

Overall effect: News feed events reduced from ~20/minute to ~3/minute, with more variety and less repetition.

---

## Bug Fixes

- **Lever puzzles always fail**: Dungeon lever puzzles would never accept the correct answer, even when the player entered the exact sequence shown in the hints. Root cause: the puzzle solution was stored as 1-indexed lever numbers (matching the hints), but the verification code subtracted 1 from the player's input before comparing, converting it to 0-indexed. The comparison always mismatched. Fixed by comparing the player's input directly to the 1-indexed solution.

- **NPCs respawn multiple times (duplicate news messages)**: NPCs like Lady Morgana would generate multiple "returned from the realm of the dead!" news messages for a single death. Two causes:
  1. **Double simulation ticking**: The world simulator was being called twice per interval — once from its own background loop and once from the periodic update handler. Removed the redundant call.
  2. **Multiple simulator instances in online mode**: Each player session created its own WorldSimulator with its own background loop, all operating on the same shared NPC list. With 5 players connected, that's 5 independent loops each detecting and respawning the same dead NPCs. Fixed by reusing the existing WorldSimulator instance when one is already running.

- **Location names corrupted ("theinn", "darkalley", etc.)**: NPC locations in news messages showed garbled names like "at the theinn" instead of "at the Inn". Root cause: `BaseLocation.AddNPC()` was overwriting `NPC.CurrentLocation` with the raw enum name (`GameLocation.TheInn.ToString().ToLower()` = "theinn") after `UpdateLocation()` had already set the correct human-readable name. Removed the override since the location is already set correctly before `AddNPC` is called.

- **NPCs pickpocketing themselves**: News feed showed entries like "Grok the Destroyer was caught pickpocketing Grok the Destroyer." An NPC could end up with its own ID in its enemy list, and the rivalry escalation code didn't check for self-targeting. Added a self-check guard to skip self-referencing enemy entries.

- **New character creation wipes world state in online mode (children, NPCs, marriages)**: When any player created a new character in online mode, the `CreateNewGame()` method reset global shared singletons (`FamilySystem`, `NPCSpawnSystem`, `NPCMarriageRegistry`) that are shared across all players. This wiped all NPC children, marriages, and potentially NPC state for everyone on the server. Fixed by only resetting world-level systems in single-player mode — in online mode, these are managed by the WorldSimService.

- **Quest system completely broken — most quests impossible to complete**: The starter quest system had multiple critical bugs that made nearly every quest impossible to turn in:
  1. **Monster kill quests had no objectives**: `CreateStarterQuest()` added monsters to the quest's display list but never created trackable `KillSpecificMonster` objectives. When the player tried to turn in, `ValidateQuestCompletion()` found no objectives to check and returned false. Fixed by creating proper objectives for each monster type with targetId matching.
  2. **Monster names didn't exist in the game**: Quests referenced "Skeleton", "Skeleton Warrior", "Sewer Rat", "Goblin Scout", "Orc Shaman", "Cave Troll", and "Specter" — none of which are actual monsters in MonsterFamilies (the system that generates dungeon encounters). Players could never find these monsters to kill. Replaced all invalid names with actual MonsterFamilies entries (Zombie, Ghoul, Wolf, Hobgoblin, Orc Berserker, Ogre, Shade, etc.).
  3. **Flavor text referenced non-existent locations**: Quest descriptions mentioned "the old cemetery", "northern villages", "eastern bridge", and "the countryside" — locations that don't exist in the game world. Updated all descriptions to reference the dungeon where combat actually happens.
  4. **FindArtifact quests impossible**: The "Artifact Recovery" quest required finding an artifact, but `OnArtifactFound()` is never called from anywhere in the dungeon code. Converted to a ReachFloor quest ("Deep Exploration").
  5. **ClearBoss quests impossible**: Boss kill objectives had empty TargetIds, but `OnMonsterKilled` always passes the boss name. The objective matching logic skipped any objective whose TargetId didn't match, so empty TargetIds never matched anything. Fixed the matching logic so empty TargetId means "match any target of this type." Also converted boss quests to monster kill quests for clearer objectives.
  6. **Royal audience quests used invalid monster names**: `GetRandomMonsterForLevel()` included "Vampire", "Dark Knight", "Giant Rat", and "Skeleton" which aren't MonsterFamilies entries. Fixed to use valid names.

- **Online player tracking bug — website shows only one player**: When multiple players connected to the MUD server, the website's "Who's Online" section only showed the most recently connected player. Root cause: `OnlineStateManager` used a plain static singleton. In the MUD server (single process, all sessions), each new player's `Initialize()` call overwrote the previous player's instance. When the first player disconnected, cleanup called `Shutdown()` on the *second* player's manager, deleting the second player from the database. Fixed by making `OnlineStateManager` and `OnlineChatSystem` per-session via `SessionContext` (AsyncLocal), with static fallback for SSH-per-process mode.

---

## Quest System Overhaul

The quest system's starter quests were completely non-functional — they could be claimed but never completed. All 11 starter quests have been rebuilt:

- **Monster kill quests now track properly**: Each monster type has its own kill counter objective. Progress updates in real-time as you fight in the dungeon ("Kill 5 Zombies: 3/5").
- **All monster names match actual dungeon encounters**: "Skeleton" → Zombie, "Sewer Rat" → Wolf, "Goblin Scout" → Hobgoblin, etc. Every quest target is a monster you'll actually encounter at the appropriate dungeon level.
- **Quest descriptions reference real locations**: No more "old cemetery" or "northern villages" — descriptions now reference the dungeon where combat happens.
- **Completion hints**: Every quest now shows a hint telling you where to go ("Fight monsters in the Dungeon to complete this quest", "Purchase the item from the appropriate shop in town", etc.).
- **Level ranges match monster spawn floors**: Quest level requirements now align with the dungeon floors where the target monsters actually appear.
- **Equipment quest turn-in removes item**: When completing a Merchant Guild equipment quest ("The Merchant Guild needs a pair of Thief's Boots"), the item is now taken from the player — checked in both equipped slots and inventory. Previously players kept the item for free.
- **Quest rewards now display correctly**: Reward text ("Reward: 1100 gold!") was silently lost due to a type conversion bug — `CompleteQuest` accepted `TerminalUI` which triggered an implicit conversion from `TerminalEmulator` that created a throwaway object. All quest completion output (rewards, equipment handover messages) was writing to nowhere. Fixed by using `TerminalEmulator` directly.
- **Stale quest data refresh in MUD mode**: Quest data loaded from player saves could contain old definitions (wrong monster names, missing objectives). `EnsureQuestsExist()` now runs in `LoadSaveByFileName()` — the actual MUD login path — replacing unclaimed stale quests with corrected versions while preserving any quests the player has already claimed.

---

## Auction House Improvements

The auction house now shows item stats so players can make informed purchases:

- **Compact stats in listings**: Browse view now shows key stats (Attack, Armor, HP, Strength, etc.) for each item directly in the listing table.
- **Detailed item inspection**: Selecting an item number shows a full detail view with all stat bonuses, level requirements, alignment restrictions, and curse warnings before purchasing.
- **Fixed column alignment**: Price, Seller, and Expires columns now align properly regardless of price length (previously used hardcoded spaces that shifted with price width).
- **Gold collection system**: Auction gold is no longer silently added to the seller's save data (which could be overwritten by the seller's next save). Instead, sold listings show `[SOLD - COLLECT GOLD]` in My Listings. Sellers press `[C]` to collect all pending gold at once, which adds it directly to their live session. Total uncollected gold is displayed as a summary. Sellers receive an instant notification when their item sells, telling them to visit the Auction House to collect.
- **Listing fees**: Sellers now pay a listing fee when posting an item. The fee is a percentage of the asking price, based on two factors: a base rate that varies by listing duration, and the king's current tax rate. The base rates are 5% (12h), 4% (24h), 3% (48h), and 2% (72h) — longer listings are cheaper to encourage patience. The king's tax percent is added on top, so at the default 5% tax rate, a 48-hour listing costs 8% of the asking price. Minimum fee is 1 gold. Fee revenue is split between the royal treasury and the city controller via the existing tax system, so the king and city controller both profit from marketplace activity.
- **Listing duration selection**: Sellers choose how long their listing stays up — 12, 24, 48, or 72 hours. Each option shows the calculated fee before confirming. Previously all listings were hardcoded to 48 hours.
- **Global listing announcements**: When a player lists an item, all connected players see "[Auction] PlayerName just listed ItemName for X gold! (duration)" in bright yellow.

---

## Real-Time Chat & Gossip Channel

Chat messages now arrive instantly — no more waiting for the next action to see what someone said. This brings the MUD server in line with classic MUD behavior where communication is immediate.

- **Real-time message delivery**: When another player uses `/say`, `/shout`, `/tell`, `/emote`, or any chat command, the message appears on your screen immediately — even while you're sitting at a prompt. A background message pump runs during all input prompts, polling for incoming messages every 100ms and injecting them into the terminal output. Your partially typed input is preserved in the TCP buffer.
- **Gossip channel** (`/gossip <msg>` or `/gos <msg>`): New global out-of-character chat channel, distinct from `/shout` (which is in-character). Messages appear in bright green with `[Gossip]` prefix. Muted players cannot use it.
- **Room arrivals/departures**: "PlayerName arrives." and "PlayerName departs." messages now appear in real-time too.
- **Login/logout announcements**: When a player connects or disconnects, all other players see "PlayerName has entered the realm." or "PlayerName has left the realm." in bright yellow. Invisible wizards are suppressed.
- **Real-time trade/mail/bounty/auction/PvP notifications**: All player-to-player notifications now deliver instantly via the message pump instead of requiring the recipient to take an action first.
- **Chat commands no longer redraw the screen**: Previously, typing `/say`, `/gos`, or any chat command would clear the screen and redraw the full location menu, wiping the conversation history. Chat commands now skip the menu redraw so the conversation stays visible. Information commands like `/who` still redraw as expected.
- **Single-player unaffected**: The message pump only activates for MUD server connections (stream-backed terminals). Local, BBS, and console modes are completely unchanged.

---

## Files Changed

| File | Changes |
|------|---------|
| `Scripts/Core/GameConfig.cs` | Version bump to 0.30.2 |
| `Scripts/Locations/DungeonLocation.cs` | Fixed lever puzzle comparison: removed erroneous `lever - 1` conversion |
| `Scripts/Systems/WorldSimulator.cs` | Added `IsRunning` property; self-pickpocket guard; online mode rate-limiting (daily combat caps, pair cooldowns, tension message cooldowns, team action cooldowns, reduced activity/rivalry/escalation/divorce/team rates, enemy reconciliation system) |
| `Scripts/Locations/BaseLocation.cs` | Removed `CurrentLocation` override in `AddNPC()` that corrupted location names; auction house: added compact stats display, item detail/inspect view, fixed column alignment, gold collection system, listing fees with `CalculateAuctionFee()` helper, duration selection (12/24/48/72h), fee deduction via `ProcessSaleTax()`, global listing announcements; chat commands skip menu redraw (`_skipNextRedraw`); added `/gossip` to help text |
| `Scripts/Systems/SqlSaveBackend.cs` | Added `gold_collected` column migration; `CollectAuctionGold()` method; updated `GetMyAuctionListings` to include `gold_collected` flag |
| `Scripts/Systems/IOnlineSaveBackend.cs` | Added `GoldCollected` property to `AuctionListing` data class |
| `Scripts/UI/TerminalEmulator.cs` | Added `MessageSource` property and `RunMessagePumpAsync()` for real-time chat delivery during input prompts |
| `Scripts/Server/PlayerSession.cs` | Wired `MessageSource` to `IncomingMessages.TryDequeue` for real-time message pump; global login/logout announcements via `BroadcastToAll` |
| `Scripts/Locations/ArenaLocation.cs` | Real-time PvP attack notification via `MudServer.SendToPlayer` |
| `Scripts/Server/MudChatSystem.cs` | Added `/gossip` and `/gos` commands for global out-of-character chat channel |
| `Scripts/Core/Quest.cs` | Fixed `UpdateObjectiveProgress` matching: empty objective TargetId now matches any target of that type |
| `Scripts/Systems/QuestSystem.cs` | Fixed `CreateStarterQuest()` to create `KillSpecificMonster` objectives for monster quests; replaced all invalid monster names with actual MonsterFamilies entries; fixed descriptions to reference the dungeon; converted impossible FindArtifact/ClearBoss quests to Monster/ReachFloor types; fixed `GetRandomMonsterForLevel()` to use valid monster names; fixed `CompleteQuest`/`ApplyQuestReward`/`RemoveQuestEquipment` parameter types from `TerminalUI` to `TerminalEmulator` (implicit conversion was discarding terminal output); added equipment removal on Buy* quest turn-in; added fallback reward for quests with Reward=0; moved completion banner into `CompleteQuest`; `EnsureQuestsExist()` always regenerates stale unclaimed quests |
| `Scripts/Core/GameEngine.cs` | Removed redundant `SimulateStep()` call from `PeriodicUpdate()`; skip WorldSimulator creation if one is already running; guard world-level system resets in `CreateNewGame()` with `IsOnlineMode` check; moved `QuestSystem.EnsureQuestsExist()` outside NPC count guard in `EnterGameWorld()`; added `QuestSystem.EnsureQuestsExist()` to `LoadSaveByFileName()` for MUD mode |
| `Scripts/Locations/QuestHallLocation.cs` | Added completion hints to `DisplayQuestDetails()` showing where to go for each quest type; removed duplicate completion banner from `TurnInQuest()` (now in `CompleteQuest`) |
| `Scripts/Systems/OnlineStateManager.cs` | Made per-session via SessionContext (AsyncLocal) instead of static singleton; fixed Shutdown to clear correct reference |
| `Scripts/Systems/OnlineChatSystem.cs` | Made per-session via SessionContext (AsyncLocal) instead of static singleton; fixed Shutdown to clear correct reference |
| `Scripts/Server/SessionContext.cs` | Added `OnlineState` and `OnlineChat` per-session properties |
| `Scripts/Server/PlayerSession.cs` | Changed cleanup to use session-local references instead of static singleton; login/logout announcements |
