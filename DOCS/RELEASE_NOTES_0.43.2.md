# v0.43.2 - Relationship, Quest & Accessibility Fix

Critical fixes for NPC relationships not updating, quest objectives not tracking after save/load, screen reader accessibility regression, and NPCs dying while actively engaged with players.

---

## Enhancements

### Buff Spells Can Now Target Party Members
Buff spells (Divine Shield, Bless Weapon, Sanctuary, Armor of Faith, Arcane Shield, Haste, etc.) were previously self-only. When casting a buff spell with party members present, the game now shows a target selection menu allowing you to cast the buff on yourself or any alive ally. All buff effects (protection, attack bonuses, status effects, special effects like Divine Avatar) are applied to the chosen target.

### Equipment Comparison When Equipping from Inventory
When equipping an item from your backpack and the slot is already occupied, the game now shows a side-by-side comparison (matching the dungeon loot format) with the currently equipped item — showing attack/armor/stat differences in green (upgrade), red (downgrade), or yellow (same), plus bonus stat breakdowns. You are then prompted to confirm before equipping.

### Standardized Item Stat Labels
Backpack items now display stats consistently with equipped items: weapons show `WP:` (Weapon Power) instead of `Att:`, and armor pieces now show `AC:` (Armor Class) which was previously missing entirely from the backpack display. Detail views also standardized to "Weapon Power" and "Armor Class".

### Team Members Pick Up Dropped Equipment
When you leave a piece of equipment on the ground after a dungeon fight, your team members will now check if it's an upgrade for them. If it is, the best candidate picks it up and equips it automatically, with a message like "Aldric picks Enchanted Longsword off the ground and equips it — a 35% upgrade!" Only identified items are considered, and teammates respect level requirements.

---

## Bug Fixes

### Relationships Stuck at "Stranger" (v0.42.4 Regression)
Chatting with NPCs, buying drinks, giving gifts, and all other relationship-building interactions had no effect — NPCs stayed at "Stranger" forever. The root cause was a `[ThreadStatic]` annotation on the relationship data store, introduced in v0.42.4 for MUD session isolation. `[ThreadStatic]` is fundamentally incompatible with `async/await` — when an `await` resumes (e.g., after `terminal.GetInput()`), the continuation can run on a **different thread**, which sees a fresh empty dictionary instead of the one populated at save load. Every conversation wrote to a throwaway dictionary that was discarded moments later.

**Fix**: Replaced `[ThreadStatic]` with the `SessionContext` + `AsyncLocal` pattern used by all other per-session systems. `AsyncLocal` correctly flows through async continuations, so relationship data persists across `await` boundaries. Single-player and BBS modes are unaffected (they use the static fallback instance, same as before).

### "Talk to NPC" Quest Objectives Never Complete
Quests that required talking to a specific NPC (Faith faction quests, NPC petitions, rescue follow-ups) could never be completed after a save/load cycle. The `TargetNPCName` field on quests was never serialized — after loading, it was always empty, so the "did the player talk to the right NPC?" check always failed.

**Fix**: Added `TargetNPCName` to `QuestData` serialization. Now properly saved and restored in both single-player (`RestoreFromSaveData`) and MUD mode (`MergePlayerQuests`).

### Screen Reader Mode Lost After Room Changes (v0.42.4 Regression)
Screen reader users experienced intermittent loss of screen reader mode — the simplified menus would randomly revert to the visual ASCII art format after navigating between rooms. Same root cause as the relationship bug: `GameConfig.ScreenReaderMode` used `[ThreadStatic]`, which loses its value across `async/await` boundaries.

**Fix**: Replaced `[ThreadStatic]` with `AsyncLocal` for the per-session screen reader flag.

### Screen Reader Starts Reading Mid-Room Description
When moving between rooms, screen readers would sometimes start reading partway through the room description, missing the beginning. This happened because `ClearScreen()` wipes the terminal buffer — the screen reader loses its reading position during the clear-and-redraw cycle and picks up wherever it happens to catch the new output.

**Fix**: When Screen Reader Mode is ON, `ClearScreen()` no longer clears the terminal. Instead, it outputs a separator line (`────────`) so the screen reader buffer grows naturally. The user hears each new section announced in order without losing content.

### Team NPCs and Lovers Dying During Player Interactions
NPCs who were actively engaged with the player — dungeon party members, companions being equipped at the Inn, or romantic partners during relationship dialogues — could be killed by the world simulator running in the background. The world sim ticks every 30 seconds and can send NPCs on dungeon runs, trigger NPC-vs-NPC combat, or roll permadeath independently of what the player is doing. An `IsInConversation` flag existed on NPCs but was never actually set or checked.

**Fix**: Player interaction code now sets `IsInConversation = true` on engaged NPCs (with `try/finally` cleanup), and the world simulator skips death processing, combat initiation, and dungeon exploration for any NPC with this flag set. Protected interactions include: NPC dialogue, chatting, dungeon party membership, quality time at home, and Love Street encounters.

### Permadeath Too Aggressive
With the world sim ticking every 30 seconds, cumulative permadeath probability was far too high — an active NPC had roughly a 59% chance of permanent death per real-world day from dungeon activity alone. NPCs were dying like flies, especially in multiplayer where the world sim runs continuously.

**Fix**: All permadeath rates halved (dungeon solo 8%→4%, dungeon team 5%→2%, NPC-vs-NPC 4%→2%, player kill 12%→8%, team war 4%→2%). Population floor raised from 40 to 45. Level-based reduction increased from 1% to 1.5% per level, giving higher-level NPCs better survival odds.

### HP Reduced by Half When Equipping or Unequipping Items
Equipping an item (including auto-equip from shop purchases) or unequipping an item cut the player's current HP by roughly half. The root cause was in `RecalculateStats()`: it calls `ApplyToCharacter()` for each equipped item in a loop, and `ApplyToCharacter()` clamps `HP = Min(HP, MaxHP)` after each item. At that point, `MaxHP` only includes `BaseMaxHP + that item's bonus` — the Constitution HP bonus, King bonus, and child bonuses haven't been applied yet. So HP gets clamped to an incomplete MaxHP partway through the calculation.

For example, a player with BaseMaxHP=100, Constitution bonus=80, and equipment bonus=20 would have correct MaxHP=200 and full HP=200. But during `RecalculateStats`, the first item's `ApplyToCharacter` call clamps HP to MaxHP=100 (base only, no Constitution bonus yet). By the time Constitution bonus is added and MaxHP reaches 200, HP is already stuck at 100.

**Fix**: Save HP and Mana before the equipment application loop, then restore them before the final clamp at the end of `RecalculateStats()`. The per-item `ApplyToCharacter` calls still update stats correctly — only their premature HP/Mana clamping is neutralized.

### Royal Marriages Transferring Between Kings
When a king was dethroned (throne challenge, siege, abdication, NPC succession), the old king's royal spouse was never cleared. The Spouse data persisted in `world_state` and save files, causing the next monarch to inherit the previous king's marriage. Additionally, all three royal court load paths (OnlineStateManager, WorldSimService, SaveSystem) only SET the Spouse if world_state had one, but never CLEARED it when the Spouse was null — so even if the new king's `world_state` entry had no Spouse, a stale Spouse from a previous load could stick around.

**Fix**: Added `ClearRoyalMarriage()` helper that clears the outgoing king's `Spouse`, resets the NPC spouse's marriage state (`IsMarried`, `SpouseName`), and removes the marriage registry entry. Called at all five throne transition points: throne challenge victory, siege victory, abdication, claim empty throne, and NPC succession. Also added `else { king.Spouse = null; }` in all three royal court load paths as defense-in-depth.

### No Notification When Dethroned by Another Player
When another player seized the throne (via challenge or siege), the dethroned player received no notification. They only discovered they were no longer king by visiting the castle and seeing someone else on the throne.

**Fix**: Added `NotifyDethronedPlayer()` that sends a system message to the old king when they are dethroned by another player. Fires on both throne challenge victory and siege victory. NPC kings are unaffected (no notification needed).

### Heal Spell on Invalid Party Member Target Silently Casts on Self
When casting a healing spell (e.g., Cure Wounds) in combat with party members, entering an out-of-range target number (e.g., 3 when only 0-2 are valid) silently cast the spell on yourself instead of showing an error.

**Fix**: `SelectHealTarget()` now validates the input and re-prompts with an error message ("Invalid target. Choose 0-N.") when the number is out of range. Non-numeric input also shows an error instead of silently defaulting to self.

### Heal Spell on Ally Shows Caster Regaining HP
When casting a healing spell (e.g., Cure Wounds) on a party member, the output showed both "Stettin regains 138 hitpoints!" (incorrect — caster's name) and "Aldric recovers 138 HP!" (correct). The first message came from SpellSystem which always generates the heal message with the caster's name, displayed before the ally-targeting code runs.

**Fix**: When healing an ally, the self-heal portion of the spell message (e.g., "Stettin regains 138 hitpoints!") is stripped before display, keeping only the incantation and critical cast text. The correct ally heal message is shown separately.

### Mana Potion "Can Afford" Ignores Carrying Capacity
The healer's mana potion shop showed "You can afford up to 3738 potions" even when the player could only carry 29. The healing potion path already capped by carrying capacity, but the mana potion path didn't.

**Fix**: Mana potion "can afford" display now shows `Math.Min(maxAfford, maxCanCarry)`, matching the healing potion behavior.

### Dungeon Rooms Stuck as "Explored" Instead of "Cleared"
When reclearing dungeon levels after monster respawn, rooms without monsters showed as "(exp)" instead of "(clr)" on the map. The auto-clear logic for empty rooms was inside a `if (!IsExplored)` guard — after floor respawn, rooms kept `IsExplored = true` but had `IsCleared` reset to `false`, so re-entering them never triggered the auto-clear. The same issue affected rooms revealed by dungeon knowledge events (wounded adventurer maps, floor visions), which set `IsExplored = true` without also setting `IsCleared = true` for empty rooms.

**Fix**: Moved the auto-clear for non-monster rooms outside the `!IsExplored` guard so it fires on every room entry. Also updated both knowledge events (wounded adventurer map and floor vision) to auto-clear empty rooms when marking them explored.

### Heal Spell Targeting Ally Sometimes Heals Self Instead
When casting a heal spell on a party member, the target was sometimes ignored and the spell healed the caster instead. The root cause: `SelectHealTarget()` returned an index into a filtered list of injured allies, but `ExecuteSpellMultiMonster()` rebuilt that filtered list at execution time. If a teammate's HP changed between selection and execution (e.g., another party member healed them during their turn), the lists would differ — the index would point to a different ally or fall out of bounds, silently defaulting to self.

**Fix**: Both `SelectHealTarget()` and `SelectBuffTarget()` now map the player's selection back to a stable `currentTeammates` index (the full unfiltered teammate list). `ExecuteSpellMultiMonster()` uses this index directly against `currentTeammates` instead of re-filtering. The target reference is now stable regardless of HP changes between selection and execution.

### Standardized Location Header Borders
All location headers now use consistent box-drawing characters (`╔═╗║╚═╝`) with uniform single-color borders and properly centered titles. Previously, the middle text line used a different color (bright yellow) from the border (bright cyan), and some locations (Your Home, Love Street) used completely different border characters (`+===+`/`|...|`). Fixed across all 15+ locations: Main Street, Dungeon, Healing Hut, Level Master, Royal Castle, Inn, Dark Alley, Dormitory, Arena, Love Street, Auction House, and all sub-headers.

### Treasure Chest ASCII Art Centered
The `$$$$$$$$$$$` inner box in the treasure chest ASCII art was off-center (4 spaces left, 6 right). Now properly centered (5/5).

### Auction House Uses [R]eturn Instead of [Q]uit
The online Auction House menu displayed `[Q]uit to Town` which was inconsistent with other locations that use `[R]eturn`. Changed to `[R]eturn to Town` (Q still works as fallback).

### Team Members and Spouse/Lovers Attacking Sleeping Players
NPCs on the player's team, or who are the player's spouse or lover, could attack and kill the player while sleeping at the Inn dormitory or during overnight stays. The world simulator's sleeper-attack logic and the Inn/Dormitory random attack systems had no filtering for friendly NPCs.

**Fix**: Dormitory attacks, Inn sleeping attacks, and world simulator sleeper attacks now skip NPCs who are on the player's team or who are the player's spouse/lover. Uses team name comparison and a new `RelationshipSystem.IsMarriedOrLover()` check.

---

## Files Changed

| File | Changes |
|------|---------|
| `Scripts/Core/GameConfig.cs` | Version 0.43.2; replaced `[ThreadStatic]` with `AsyncLocal` for `ScreenReaderMode`; halved all permadeath rates, raised population floor to 45, increased level reduction to 1.5% |
| `Scripts/Systems/RelationshipSystem.cs` | Replaced `[ThreadStatic]` with `SessionContext`-aware `Instance` property using `AsyncLocal`. Relationship data, daily gain tracking, and daily reset timestamp are now instance fields routed through the per-session `Instance`. |
| `Scripts/Server/SessionContext.cs` | Added `RelationshipSystem Relationships` property; initialized in `InitializeSystems()` |
| `Scripts/Systems/SaveDataStructures.cs` | Added `TargetNPCName` property to `QuestData` class |
| `Scripts/Systems/SaveSystem.cs` | `SerializeQuestList()` now includes `TargetNPCName`; royal court restore now clears `king.Spouse = null` when save data has no Spouse |
| `Scripts/Systems/QuestSystem.cs` | `RestoreFromSaveData()` and `MergePlayerQuests()` now restore `TargetNPCName` from save data |
| `Scripts/UI/TerminalEmulator.cs` | `ClearScreen()` outputs a separator instead of clearing the terminal when Screen Reader Mode is ON |
| `Scripts/Systems/WorldSimulator.cs` | `MarkNPCDead()`, `ExecuteAttack()`, `NPCExploreDungeon()`, and `NPCTeamDungeonRun()` now skip NPCs with `IsInConversation` set; team member selection filters out engaged NPCs |
| `Scripts/Locations/BaseLocation.cs` | `InteractWithNPC()` and `ChatWithNPC()` set `IsInConversation` on the target NPC with `try/finally` cleanup |
| `Scripts/Locations/DungeonLocation.cs` | `EnterLocation()` sets `IsInConversation` on all dungeon party NPC members; auto-clear for non-monster rooms moved outside `!IsExplored` guard; knowledge events now auto-clear empty rooms when marking them explored |
| `Scripts/Locations/HomeLocation.cs` | `SpendQualityTime()` sets `IsInConversation` on the partner NPC with `try/finally` cleanup |
| `Scripts/Locations/LoveStreetLocation.cs` | `MingleWithNPC()` sets `IsInConversation` on the target NPC with `try/finally` cleanup |
| `Scripts/Core/Character.cs` | `RecalculateStats()` saves HP/Mana before equipment loop and restores before final clamp, preventing premature clamping by per-item `ApplyToCharacter()` calls |
| `Scripts/Locations/CastleLocation.cs` | Added `ClearRoyalMarriage()` helper called at all 5 throne transitions; added `NotifyDethronedPlayer()` system message on throne challenge and siege victory |
| `Scripts/Systems/OnlineStateManager.cs` | `LoadRoyalCourtFromWorldState()` now clears `king.Spouse = null` when world_state has no Spouse |
| `Scripts/Systems/WorldSimService.cs` | `LoadRoyalCourtFromWorldState()` now clears `king.Spouse = null` when world_state has no Spouse |
| `Scripts/Systems/CombatEngine.cs` | `SelectHealTarget()` validates input; heal/buff spell on ally strips caster-named effect message; added `SelectBuffTarget()` for buff ally targeting; `HandleQuickbarAction` offers buff target selection; `ExecuteSpellMultiMonster` applies buff effects to chosen ally via `ApplySpellEffects`; team members auto-pickup dropped equipment if it's an upgrade |
| `Scripts/Systems/InventorySystem.cs` | `EquipFromBackpack()` now shows item comparison with confirmation when slot is occupied; backpack list uses `WP:`/`AC:` labels matching equipped items; backpack detail view uses `Weapon Power:`/`Armor Class:` labels |
| `Scripts/Locations/HealerLocation.cs` | Mana potion "can afford" display now capped by `maxCanCarry` |
| `Scripts/Locations/DormitoryLocation.cs` | Attack target list now filters out player's team members and spouse/lover NPCs |
| `Scripts/Locations/InnLocation.cs` | Sleeping guest attack target list now filters out player's team members and spouse/lover NPCs |
| `Scripts/Systems/WorldSimulator.cs` | Sleeper attack logic now filters out team members and spouse/lover NPCs; added team name and relationship checks |
| `Scripts/Systems/SqlSaveBackend.cs` | Added `GetPlayerTeamName()` for lightweight team name lookup via `json_extract` |
| `Scripts/Systems/RelationshipSystem.cs` | Added static `IsMarriedOrLover()` method for name-based relationship lookup |
| `Scripts/UI/ANSIArt.cs` | Centered treasure chest inner box |
| `Scripts/Locations/MainStreetLocation.cs` | Fixed header border alignment |
| `Scripts/Locations/HealerLocation.cs` | Standardized header border (single color, centered title) |
| `Scripts/Locations/LevelMasterLocation.cs` | Standardized header border |
| `Scripts/Locations/CastleLocation.cs` | Standardized header borders (2 headers: inside and outside castle) |
| `Scripts/Locations/HomeLocation.cs` | Replaced `+=+`/`\|..\|` borders with standard `╔═╗`/`║║`/`╚═╝` box-drawing |
| `Scripts/Locations/InnLocation.cs` | Standardized header border |
| `Scripts/Locations/DarkAlleyLocation.cs` | Standardized header border |
| `Scripts/Locations/DormitoryLocation.cs` | Standardized header border |
| `Scripts/Locations/ArenaLocation.cs` | Standardized header border (single color) |
| `Scripts/Locations/LoveStreetLocation.cs` | Replaced `+-+`/`\|..\|` borders with standard box-drawing (2 headers) |
| `Scripts/Locations/MarketplaceLocation.cs` | Standardized sub-header borders (Listings, Your Status) |
