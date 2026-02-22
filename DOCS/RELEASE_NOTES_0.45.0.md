# v0.45.0 - Band of Brothers

Adds real-time cooperative dungeon exploration for online play (MUD only). Form a group, enter the dungeon together, and fight side by side -- each player controls their own character in combat.

---

## New Features

### Group Dungeon System (Online MUD Mode ONLY)

**Forming a Group**

Use `/group <player>` to invite another player to your dungeon group. The invite is non-blocking -- you can keep playing, invite multiple players simultaneously, and get notified when they accept or the invite expires. The invited player types `/accept` to join or `/deny` to refuse. Invites expire after 60 seconds. The first player to send an invite becomes the group leader. All group members must be on the same team and at least level 5.

- `/group` (no args) -- view your current group and members
- `/group <player>` -- invite a player to your group (non-blocking, can invite multiple)
- `/leave` -- leave the group (non-leaders)
- `/disband` -- disband the group (leader only)
- Up to 5 players in a group (leader + 4)
- Group tags shown in `/who` list

**Dungeon Exploration**

The leader enters the dungeon normally. All group members are notified and can follow by going to the Dungeons themselves. Each player has their own independent terminal session -- no screen sharing. Followers receive broadcast messages about dungeon events as they happen: room transitions, floor descents, combat encounters, rest, and loot drops.

The leader drives all exploration: moving between rooms, descending floors, interacting with features. Followers see key events via broadcast messages. Slash commands (`/say`, `/tell`, `/who`) work normally for followers between combats. The leader can also fill remaining party slots with NPC teammates from their team.

**Player-Controlled Combat**

When combat begins, each grouped player controls their own character on their turn. The follower sees the full combat display (HP bars, status effects, teammate info) and the complete BBS-style action menu showing all available actions including quickbar skills. Available actions: [A]ttack (A2 = target monster #2), [C]ast (C1/C2/C3 for specific spell, C1T2 for spell 1 on target 2), [D]efend, [I]tem (healing/mana potion), [H]eal ally, [P]ower attack, [E]xact strike, [T]aunt, [W] Disarm, [L] Hide, [R]etreat (individual — only YOU leave combat), [1-9] quickbar slots (class spells and abilities).

Combat events are broadcast with correct perspective: you see "You slash the orc for 45 damage!" on your turn, and "PlayerName slashes the orc for 45 damage!" on others' turns. Monster attacks, kills, and victory/defeat are all broadcast to the group.

If a grouped player doesn't respond within 30 seconds, they automatically attack the weakest monster. This prevents AFK players from stalling combat for the entire group.

**Independent XP Calculation**

Each player in the group earns XP calculated independently based on their own level versus the monsters fought. Standard multipliers (difficulty, world events, NG+, team bonus) all apply per-player.

**Group Level Gap Penalty** deters high-level players from power-leveling low-level ones:

| Level Gap | XP Rate |
|-----------|---------|
| 0-5 levels | 100% |
| 6-10 levels | 75% |
| 11-15 levels | 50% |
| 16-20 levels | 25% |
| 21-30 levels | 15% |
| 31+ levels | 10% |

The gap is measured from the highest-level player in the group to each individual player. So a level 10 player grouped with a level 50 player would receive only 10% XP, while the level 50 player gets full XP (since the monsters are far below their level anyway).

Gold is split evenly among all players in the group.

**Boss Floor Access**

Dungeon boss and seal floor locks use the leader's progression. Followers temporarily bypass their own boss locks while grouped, but do NOT earn boss/seal progress -- they need to clear those floors on their own later.

**Leader Death**

If the leader dies in combat, the fight isn't over — surviving grouped players continue the battle. Monsters redirect their attacks to remaining party members, combat rounds keep flowing, and if the party wins, it's still a victory with full rewards. If all party members fall, combat ends as a defeat. This means a warrior can tank until death while the mage finishes off the boss.

**Disconnect Handling**

- If a follower disconnects, they are cleanly removed from the leader's party and spectator streams
- If the leader disconnects, the entire group is disbanded and all followers are notified
- Emergency saves on disconnect capture the current state of all players

### Group Loot Rolling

When equipment drops in a group dungeon, all eligible players roll for the loot. Eligibility is determined by class restrictions, level requirements, strength, and alignment. If only one player can use the item, they get it automatically. If multiple players are eligible, each rolls 1-100 and the highest roller wins. The winner gets a 30-second prompt to equip, take, or leave the item. Rolls are displayed to the entire group so everyone sees the result.

---

## Enhancements

### Immersive Group Following

Followers now feel like they're physically exploring the dungeon alongside the leader. Every time the leader moves to a new room or descends to a new floor, all followers receive a rich room description showing the room name in a themed header, floor and danger info, the room description and atmosphere text, room contents (monsters, treasure, events, features, stairs), and available exits with exploration status. No more single-line "The group enters: Cave Chamber" messages.

### Universal Reward Splitting

All dungeon rewards are now split among the entire party -- not just combat XP and gold. This includes:

- **Floor treasure** (gold and gems found on the ground)
- **Treasure chests** (good, trapped, and mimic -- followers see what happened)
- **Boss defeat bonuses** (bonus gold and XP from boss kills)
- **Floor clear bonuses** (XP and gold for clearing all rooms on a floor)
- **Feature interactions** (examining altars, investigating statues, etc.)

Gold is divided evenly among all party members including NPC teammates (spouses, mercenaries). Grouped players receive full XP with the standard level gap penalty. NPC teammates receive 75% XP (matching the combat XP rate). Companions are handled separately by the companion system.

Combat gold splitting also now includes NPC teammates in the denominator, so mercenaries and spouses get their fair share of combat gold too.

### Group Rest Healing

When the leader rests in the dungeon, all grouped followers are healed too: +25% MaxHP, +25% MaxMana, +25% MaxCombatStamina. Each follower receives a personalized message showing their recovery amounts and updated stats. The blood price rest penalty only affects the leader -- followers get the full 25% recovery.

### Full Combat Display for Followers

Grouped followers now see the exact same combat interface as the leader on their turn — full HP bars, status effects, teammate info, and the complete action menu (Attack, Cast, Item, Defend, Flee). The stripped-down percentage-only combat view has been replaced entirely.

### Follower Self-Management Between Combats

Followers can now manage their character between combats without leaving the group:

- **[I]nventory** — View equipped items by slot and backpack contents, equip items from backpack by number, unequip items back to backpack
- **[P]otions** — Use a healing or mana potion (prompts for choice if both types available)
- **[=]Status** — View full character stats: level, class, race, HP/MP/Stamina bars, all attributes, gold, XP
- **Slash commands** — `/stats`, `/health`, `/gold`, `/quests`, `/help` for quick info without leaving the room view
- **Invalid input feedback** — Unrecognized keys show a helpful hint instead of being silently ignored

After each overlay action (inventory, status), the room view re-renders so the follower stays oriented in the dungeon.

### Group Persists When Leader Leaves Dungeon

When the leader exits the dungeon, all followers are returned to Main Street but the group stays intact. The leader can go level up, buy potions, visit the healer, then re-enter the dungeon — followers can follow again without needing to be re-invited. Previously, leaving the dungeon disbanded the entire group.

### Combat Immersion for Followers

Followers now experience the full flow of combat, not just their own turn:

- **Combat introduction** — When combat starts, followers see the full monster roster (names, levels, HP), any monster phrases or taunts, and the teammate lineup. Ambush warnings are broadcast too.
- **Status effects** — Leader's poison ticks, troll regeneration, drug drain, and plague damage are broadcast to followers each round with proper third-person perspective ("Rage regenerates 3 HP!" instead of "You regenerate 3 HP!").
- **Boss phase transitions** — When a boss enters a new phase, followers see the phase announcement with the boss's current HP percentage.
- **Victory sequence** — Followers see a rich victory banner with the defeated enemy count, not just a single line.
- **Loot drops** — When equipment drops, followers see the full rarity banner (LEGENDARY/EPIC/RARE), item name, stats, bonuses, and cursed warnings — the same info the leader sees.
- **Flee/retreat** — When the leader retreats, followers see a retreat announcement instead of silence.
- **Follower death** — A grouped player who dies receives a dramatic "YOU HAVE FALLEN" death notification from their attacker.

### Follower Auto-Heal After Combat

Grouped followers now automatically use their own healing and mana potions after combat victory, using the same smart auto-heal system as the leader (skips trivial deficits, doesn't waste potions on small HP gaps). Each follower sees their own heal messages on their terminal.

### Group Party Status (/party)

New `/party` slash command shows all group members' real-time HP, MP, level, and class. The leader is marked with a star (★), and your own entry is tagged "(you)". HP colors shift from green to yellow to red as members take damage.

### Dungeon Event Broadcasting

Followers now receive broadcasts for all dungeon events that were previously invisible:

- **Traps** — Followers see what type of trap triggered (pit, poison darts, flame, acid, curse) and the damage/effect on the leader. Trap evasion is shown too.
- **Room events** — When the leader encounters a treasure chest, shrine, puzzle, merchant, or other room event, followers see a descriptive broadcast of what was discovered.
- **Feature interactions** — Examining altars, statues, and other dungeon features now broadcasts the interaction narrative including lore discoveries, damage taken, and spiritual insights — not just "examined Altar."

### Round-by-Round Combat Status for Followers

Followers no longer go blind between their own turns. At the start of every combat round, all followers receive a compact status panel showing:
- **Round number** — "Round 3" so everyone knows how long the fight has been going
- **Monster HP** — Each alive monster with color-coded HP percentage (green/yellow/red)
- **Party HP** — All party members' current HP with color-coding, separated by "|"

This means followers always know the state of the battle — not just during their own turn.

### Full Tactical Combat for Followers

Followers now have access to the complete set of combat actions, not just basic attack/defend. The combat menu shows and `ParseGroupCombatInput` accepts:
- **[P]ower Attack** — Heavy hit with optional target (P2 = power attack monster #2)
- **[E]xact Strike** — Precise strike with optional target
- **[T]aunt** — Debuff enemy defense with optional target
- **[W] Disarm** — Disarm attempt with optional target
- **[L] Hide** — Stealth ability

All tactical actions support target selection (e.g., P2 = power attack monster #2), matching the leader's combat options.

### Spell Selection for Followers

Spellcasting followers now see their available spells listed in the combat menu with C# shortcuts:
```
Spells: C1=Fireball(15mp) C2=HealingLight(10mp) C3=Shield(8mp)
```
Type `C1` to cast spell 1 at the strongest monster, `C1T2` to cast spell 1 at monster #2. Buff and heal spells auto-target self. AOE spells auto-target all. Bare `C` still auto-casts the best offensive spell as a quick option.

### Individual Retreat

Followers can now retreat from combat independently. When a follower types `R` and succeeds the flee check (DEX-based, capped at 75%), only THEY leave combat — the rest of the party fights on. Previously, any retreat ended combat for everyone.

### Monster Special Abilities Target All Party Members

Monsters can now use their special abilities (poison breath, mana drain, status effects, life steal) against ALL party members — not just the leader. Previously, special abilities only fired when the monster targeted the leader, making followers immune to the deadliest attacks.

### Follower Level-Up Notification

When a grouped follower gains enough XP to level up after combat, they now receive a bright "LEVEL UP!" notification showing their new level, with HP restored to full and stats increased. The entire group sees a level-up announcement broadcast. Level-ups also post to the game's news feed.

### Smarter Post-Battle Auto-Healing

Healing and mana potions are no longer wasted on trivial deficits after combat. The auto-heal system now requires you to be missing at least half a potion's worth of HP/MP before it kicks in. The threshold scales with level — at level 1 you need to be down ~28 HP, at level 50 you need to be down ~150 HP. The system also stops using additional potions mid-heal once the remaining deficit becomes trivial, preventing the last potion from being burned on a sliver of missing health. Manual potion use from the dungeon menu is unaffected.

---

## Bug Fixes

### Equipment Disappearing on Logout (Critical)

**Root cause**: In MUD mode, all player sessions share a single `EquipmentDatabase` in memory. Dynamic equipment (dungeon loot) was registered using saved IDs, but different players could have overlapping IDs from separate sessions. When Player B loaded their save, their equipment definitions would overwrite Player A's equipment at the same IDs. The next time Player A saved, their save would contain Player B's equipment stats. On reload, a MinLevel check would then strip the foreign equipment — and the stripped items were permanently destroyed (never returned to inventory).

**Fix**: Dynamic equipment IDs are now remapped to fresh unique IDs on every load in MUD mode, preventing cross-player ID collisions. The aggressive `EnforceMinLevelFromPower()` recalculation on load has been removed — MinLevel is already enforced at equip time, so the redundant check was only catching equipment corrupted by the ID collision bug. Orphaned equipment references (IDs not found in the database) are now safely cleaned up instead of causing silent failures.

### Group Follower Disconnect on Dungeon Entry

When a group follower entered the dungeon, they were immediately disconnected. The bare `return` from `EnterAsGroupFollower()` unwound the entire call stack back through `LocationManager`, `LoadSaveByFileName`, and `RunBBSDoorMode`, ending the session. Fixed by throwing `LocationExitException(MainStreet)` instead, which the LocationManager catches and redirects the player back to town.

### Group Dungeon "Stream in Use" Error

Two concurrent access issues on the follower's TCP stream caused "The stream is currently in use by a previous operation on the stream" errors, crashing the follower's session:

1. **Concurrent writes**: The leader's `ForwardToSpectators()` wrote to the follower's `StreamWriter` while the follower's own terminal simultaneously wrote (autosave, chat). Fixed by adding per-terminal write locks (`_streamWriterLock`) and changing spectator tracking to store `TerminalEmulator` references so each spectator's lock is acquired before forwarding.

2. **Concurrent reads**: The follower loop had a pending `ReadLineAsync()` on its `StreamReader`, and when combat started, the combat engine's `ProcessGroupedPlayerTurn()` tried to read from the same stream simultaneously. Fixed by redesigning the follower loop to use polling instead of stream reads — the follower loop now sleeps and checks exit flags, while all follower input is handled exclusively by the combat engine during their turn. A `SemaphoreSlim` read lock was also added to `GetInput()` as defense-in-depth against any future concurrent read scenarios.

### Unidentified Loot Auto-Discarded in Group Dungeon

When an unidentified equipment drop occurred in a group dungeon, the game checked class restrictions, level requirements, strength, and alignment on an item whose properties should be unknown. This caused unidentified epic drops to be automatically discarded with "No one in the group can use this item. Left behind." Fixed by skipping eligibility checks on unidentified items -- all players are now eligible to roll for unidentified loot.

### Screen Reader Room Description Cutoff in Dungeon

Screen reader users reported that room descriptions were intermittently cut off when moving between rooms or descending floors in the dungeon. The root cause was double separator lines: dungeon transitions (room movement, floor descent, combat, rest, exploration) each called `ClearScreen()` which in screen reader mode writes a separator line, and then the main location loop's `DisplayLocation()` immediately called `ClearScreen()` again — producing two separators in rapid succession. Screen readers interpret these as section boundaries and could skip content between them. Fixed by suppressing the intermediate `ClearScreen()` calls during dungeon transitions when screen reader mode is active, so only the single separator from `DisplayLocation()` introduces each new room view.

---

## Files Changed

| File | Changes |
|------|---------|
| `Scripts/Core/GameConfig.cs` | Version 0.45.0 "Band of Brothers"; added `GroupMaxSize`, `GroupMinLevel`, `GroupInviteTimeoutSeconds`, `GroupCombatInputTimeoutSeconds`, `GroupXPPenaltyTiers`, `GroupXPPenaltyMinimum` constants |
| `Scripts/Server/GroupSystem.cs` | **NEW** -- `GroupSystem` (singleton with group management), `DungeonGroup` (group state), `GroupInvite` (pending invite with TaskCompletionSource), `GetGroupXPMultiplier()` static helper; `BroadcastToGroupSessions()` for perspective-correct messaging (2nd person for actor, 3rd person for observers); `BroadcastToAllGroupSessions()` for uniform broadcasts |
| `Scripts/Core/Character.cs` | Added `[JsonIgnore]` properties: `RemoteTerminal` (TerminalEmulator for grouped player combat I/O), `IsGroupedPlayer` (computed from RemoteTerminal), `GroupPlayerUsername` (username for tracking), `CombatInputChannel` (Channel\<string\> for follower combat input routing), `IsAwaitingCombatInput` (signals follower loop to forward input to channel) |
| `Scripts/Server/PlayerSession.cs` | Added `PendingGroupInvite`, `IsGroupFollower`, `GroupLeaderSession` properties; added group cleanup in `RunAsync()` finally block (disband if leader, remove if member) |
| `Scripts/Server/MudServer.cs` | Initialize `GroupSystem` on startup; skip `IsGroupFollower` sessions in `BroadcastToAll()` and idle timeout watchdog |
| `Scripts/Server/MudChatSystem.cs` | Added `/group`, `/leave`, `/disband` commands; refactored `/accept` and `/deny` to check group invites first then spectate requests; added group tags to `/who` list |
| `Scripts/Locations/DungeonLocation.cs` | Changed `teammates` from `private` to `internal`; added group follower detection at top of `EnterLocation()`; added `SetupGroupDungeon()`, `EnterAsGroupFollower()`, `GroupFollowerLoop()`, `CleanupGroupFollower()`, `CleanupGroupDungeonOnLeaderExit()`, `BroadcastDungeonEvent()`, `PushRoomToFollowers()`, `PushRoomToSingleFollower()`, `BuildRoomAnsi()`, `GetAnsiThemeColor()`, `SplitPartyRewards()`, `AwardDungeonReward()` methods; immersive room view broadcasts on every room move and floor descent; universal reward splitting at all non-combat reward sites; follower rest healing (+25% HP/MP/ST per follower); added `ShowFollowerInventory()`, `UseFollowerPotion()`, `ShowFollowerStatus()`, `RePushRoomToFollower()`, `ProcessFollowerSlashCommand()` for follower self-management between combats (I/P/=/Q keys and /party /stats /health /gold /quests slash commands); `TriggerTrap()` now broadcasts trap type, damage, and evasion to followers; `HandleRoomEvent()` broadcasts event type descriptions to followers; `InteractWithFeature()` broadcasts narrative outcome (lore, damage, spiritual insight); updated quickbar with `/party`; `CleanupGroupDungeonOnLeaderExit()` now returns followers to town without disbanding the group; skip intermediate ClearScreen in screen reader mode |
| `Scripts/Systems/CombatEngine.cs` | Modified teammate turn loop to check `IsGroupedPlayer` and call `ProcessGroupedPlayerTurn()` (channel-based input with 30s timeout); `ProcessGroupedPlayerTurn()` now swaps terminal before display and shows full `DisplayCombatStatus()` + `ShowDungeonCombatMenuBBS()` for grouped players with spell list display; added `ParseGroupCombatInput()` with full action support (A/C/C#/C#T#/D/I/H/R/V/P/E/T/W/L/1-9 quickbar), `BroadcastGroupedPlayerAction()`, `BroadcastGroupCombatEvent()`, `BroadcastLeaderAction()`; round-by-round combat status broadcast (round number, monster HP, party HP) to all followers; status effects/troll regen/drug drain captured and broadcast to followers with third-person perspective; combat introduction broadcast; boss phase transition broadcast; rich victory sequence broadcast; loot drop broadcast with rarity banner and stats; individual follower retreat (only retreating player leaves combat); follower death handling with proper grouped player cleanup (channel close, remove from teammates, no NPC-specific logic); leader death now allows combat to continue if grouped players survive (modified loop condition, monster retargeting, victory with dead leader); monster special abilities fire against companions (not just leader); `DistributeGroupRewards()` with follower level-up detection, stat gains, HP restore, news, and group broadcast; `AutoHealWithPotions()` and `AutoRestoreManaWithPotions()` now skip trivial deficits; unidentified loot makes all players eligible to roll |
| `Scripts/Systems/TeamBalanceSystem.cs` | `CalculateXPMultiplier()` now skips `IsGroupedPlayer` teammates so grouped real players don't trigger the NPC-carry XP penalty on the leader |
| `Scripts/UI/TerminalEmulator.cs` | Added `_streamWriterLock` for thread-safe stream writes; changed spectator tracking from `List<StreamWriter>` to `List<TerminalEmulator>` with per-terminal locking in `ForwardToSpectators()` (used by `/spectate`, not group dungeons); wrapped all `_streamWriter.Write/Flush` call sites with lock; refactored `WriteLineToStream()` markup branch to build full ANSI string before atomic write |
| `Scripts/Systems/CompanionSystem.cs` | Companion equipment restore now applies dynamic equipment ID remapping for MUD mode collision avoidance |
