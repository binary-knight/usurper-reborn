# Usurper Reborn v0.48.4 — Difficulty Overhaul, Nightmare Permadeath, XP Rebalance & Online Fixes

## Difficulty Modes Now Matter

All four difficulty modes (Easy, Normal, Hard, Nightmare) now fully affect gameplay. Previously only XP/gold rewards, damage multipliers, and flee restrictions worked — the remaining five defined multipliers were never wired up.

**Newly active multipliers:**
- **Monster HP scaling** — Easy monsters have 15% less HP, Hard +20%, Nightmare +40%. Fights genuinely get harder.
- **Shop prices** — Easy gets a 15% discount at all shops (weapons, armor, magic, healer). Hard pays 15% more, Nightmare 25% more.
- **Potion healing** — Easy potions heal 25% more. Nightmare potions heal 25% less.
- **Death penalties** — Easy loses half the normal XP/gold on death. Hard loses 50% more. Nightmare doesn't matter because...

## Nightmare Mode = Permadeath

Nightmare difficulty now means **permanent death**. If you die, your save is deleted. No resurrections. No second chances. Character creation shows an explicit permadeath warning with double confirmation. On death, a dramatic permadeath screen plays and the save is erased — you're returned to the character select screen with nothing.

Combined with the existing Nightmare penalties (-50% XP/gold, +50% monster damage, +40% monster HP, no fleeing, -25% potion healing, +25% shop prices), this is the ultimate challenge mode.

## Per-Slot XP Percentage Distribution

The old three-mode XP system (Full Each / Even Split / Killer Takes All) has been replaced with a flexible per-slot percentage allocation. Open team management in the dungeon and press `[X]` to access the new XP Distribution submenu. Each party slot (you + up to 4 teammates) can be assigned an XP percentage. The total across all slots cannot exceed 100%, but it *can* be less — if you only allocate 75%, that's all you get.

Use this to power-level a companion (set them to 80%, yourself to 20%), split evenly with `[E]`, or keep everything for yourself at 100/0/0/0/0 (the default). Percentages persist across save/load and apply to all combat victory paths including partial victories.

## XP Level Scaling Rebalance

The XP bonus for fighting monsters above your level was too generous, allowing players to power-level by parking on the highest accessible dungeon floor. The level difference multiplier has been reduced from +15% per level (capped at 2.0x) to +5% per level (capped at 1.25x). The penalty for farming monsters below your level is unchanged (-15% per level, minimum 25%).

This also fixes an inconsistency where multi-monster combat had a level multiplier but single-monster combat did not — all three victory paths (single-monster, multi-monster, grouped combat) now use the same formula.

**Before:** Fighting monsters 10 levels above gave 2.0x XP bonus on top of their inherently higher base XP — a combined ~2.6x advantage over same-level fights.

**After:** Same scenario gives 1.25x bonus — a combined ~1.6x advantage. You're still rewarded for fighting tougher monsters, but it's no longer the only viable strategy.

## Reduced Room Presence Spam

Removed the flood of "arrives", "leaves", "draws their weapon", "defeated a Goblin", and "descends deeper" broadcasts that cluttered the screen when multiple players were online. Boss kills and player deaths are still announced.

## Deferred Online Presence

Players no longer appear as "online" while sitting at the main menu or character selection screen. Online presence registration and the "has entered the realm" login broadcast are now deferred until the player actually loads their character and enters the game world. This also applies to new character creation — the announcement fires after the character is created, not when the connection is first established.

## Stale Session Cleanup on Exit

Exiting the game now performs online session cleanup synchronously before the process terminates. Previously, the cleanup relied on an async `finally` block that could be skipped when `Environment.Exit()` killed the process. This fixes the issue where BBS door users (especially on Mystic BBS) would see "Disconnecting previous session" on relaunch because the old session record was never cleared.

## Idle Timeout on Any Keystroke

The idle timeout tracker now resets on any input activity (typing characters, backspace) — not just pressing Enter. Previously a player could type a long message and still be kicked for inactivity because only submitting a line reset the timer. Keystroke updates are throttled to once per 5 seconds to avoid overhead during fast typing.

## New BBS Listed

Added Lunatics Unleashed (Mystic BBS, lunaticsunleashed.ddns.net:2333) to the in-game BBS list and website.

## Two-Handed Off-Hand Display Fix

When wielding a two-handed weapon, the off-hand equipment slot now shows "(using 2H weapon)" instead of "Empty" across all equipment display screens — player inventory, character status, companion management, team corner, and home/family views. Previously it looked like the off-hand was unequipped, which was confusing.

## Dungeon Event Group Sharing

Dungeon events now split gold and XP rewards with grouped players, matching how treasure chests already worked. Affected events: Mysterious Shrine (pray XP, desecrate gold), Lost Explorer (rescue/rob gold), Riddle encounters (gold + XP), Puzzle encounters (gold + XP), Mystery events (time warp XP, treasure rain gold), and Riddle Gates (XP). Rest spot encounters also share healing with the group. Each follower sees their individual reward share.

## Lost Explorer Alignment Choices

The lost explorer dungeon encounter no longer forces a good-aligned response. Players now choose: **[G] Guide** them to safety (+150g/level, +20 Chivalry), **[R] Rob** them (+200g/level, +25 Darkness), or **[L] Leave** them be (nothing). Dark characters finally have a thematic option.

## Unique Dungeon Theme Lore

Previously only 2 of the 8 dungeon themes (Catacombs and Ancient Ruins) had unique lore fragments — the other 6 all shared the same generic message. Now every theme has its own thematically appropriate lore tied to the cycle/Ocean Philosophy narrative: Sewers (water and time), Caverns (ancient stone and the Ocean beneath), Demon Lair (punished rememberers), Frozen Depths (preserved knowledge), Volcanic Pit (souls in fire), and Abyssal Void (the edge of creation).

## World Boss Notification Throttle

The world boss announcement (`*** Lich King Vareth is rampaging across the realm! ***`) no longer appears on every single screen. It now shows once when a new boss spawns, then reminds every 5 minutes. Players below level 10 don't see it at all since they can't participate.

## Input Stomping Fix

Incoming messages (chat, broadcasts, world boss alerts) no longer interrupt you mid-keystroke. The message delivery system now waits until you've paused typing for 500ms before inserting queued messages. If you're actively typing a chat message or command, messages queue silently and appear the moment you pause or press Enter.

## Menu Cleanup

Removed the `[R] Relations` option from Main Street across all three menu styles (normal, BBS compact, screen reader). The information it showed was redundant with the Status screen.

## Bug Fixes

- **Lightning enchant stun was cosmetic**: The Thunderstrike lightning weapon enchant displayed "Stunned!" but never actually applied stun to the monster. Both single-monster and multi-monster combat paths now set `StunRounds = 1`, causing the target to skip its next turn as intended.
- **Poison didn't tick during Dungeon Guide navigation**: Using the `[G] Guide` system to auto-navigate to stairs, boss rooms, or unexplored areas skipped poison damage ticks between rooms. Poison now applies after each room traversed during guided movement and stops navigation if it kills you.
- **Daily quest limit never reset in online mode**: The `RoyQuestsToday` counter (and all other daily activity counters — fights, thefts, brawls, assassinations, etc.) were inside an `if (mode != Endless)` guard. Since online mode defaults to Endless, these counters never reset. Daily activity resets now run regardless of cycle mode.
- **NPC self-rivalry**: An operator precedence bug in the rival-selection LINQ query allowed NPCs to become their own rival, producing messages like "Tensions are rising between Ragnar Bloodaxe and Ragnar Bloodaxe." Fixed with proper parentheses around the compound boolean.

---

## Files Changed

- `GameConfig.cs` — Version 0.48.4; removed `XPShareMode` enum; added `TeamXPConfig` class with `DefaultTeamXPPercent` and `MaxTeamSlots`
- `Scripts/Core/Character.cs` — Replaced `TeamXPShare` enum with `int[] TeamXPPercent` array (5 slots)
- `Scripts/Systems/SaveDataStructures.cs` — Replaced `TeamXPShare` with nullable `TeamXPPercent` array for migration
- `Scripts/Systems/SaveSystem.cs` — Serialize `TeamXPPercent` array
- `Scripts/Core/GameEngine.cs` — Restore `TeamXPPercent` with null fallback to default; deferred `StartOnlineTracking()` and login broadcast after `IsInGame = true`; synchronous online session cleanup in `QuitGame()`; added Lunatics Unleashed to BBS list; `IsPermadeath` flag for Nightmare exit flow
- `Scripts/Systems/CombatEngine.cs` — Replaced XPShareMode logic in all 3 victory paths (single-monster, multi-monster, partial) with per-slot percentage distribution; new `DistributeTeamSlotXP()` helper; removed arrival/flee/regular-kill broadcasts; lightning enchant now sets `StunRounds = 1` in both `CheckElementalEnchantProcs()` and `CheckElementalEnchantProcsMonster()`; Nightmare permadeath in `HandlePlayerDeath()` (save deleted, no resurrection); death penalty multiplier in `ApplyDeathPenalties()`; potion healing multiplier in `UseHealingPotion()`; `IsPermadeath` flag on `CombatResult`; XP level scaling rebalanced in all 3 victory paths — level difference multiplier reduced from +15%/level (cap 2.0x) to +5%/level (cap 1.25x); added level multiplier to single-monster path (was missing)
- `Scripts/Systems/CompanionSystem.cs` — Added `AwardSpecificCompanionXP()` for per-companion XP awards by slot
- `Scripts/Locations/DungeonLocation.cs` — `[X]` now opens XP Distribution submenu with per-slot percentage editing, even-split shortcut, and aggregate validation; removed dungeon descent broadcast; `ShareEventRewardsWithGroup()` helper for dungeon event gold/XP splitting; lost explorer encounter now offers Guide/Rob/Leave choices; poison tick during Guide auto-navigation
- `Scripts/Locations/BaseLocation.cs` — `ApplyPoisonDamage()` changed from `private` to `protected` for dungeon guide access; two-handed off-hand display in `DisplayEquipmentSlot()`; `IsPermadeath` check in `LocationLoop()` for Nightmare exit; world boss notification throttled to once per spawn + 5-minute reminders, hidden below level 10
- `Scripts/Systems/InventorySystem.cs` — Two-handed off-hand display in `DisplaySlot()`
- `Scripts/Locations/InnLocation.cs` — Two-handed off-hand display in `CompanionDisplayEquipmentSlot()`
- `Scripts/Locations/TeamCornerLocation.cs` — Two-handed off-hand display in `DisplayEquipmentSlot()`
- `Scripts/Locations/HomeLocation.cs` — Two-handed off-hand display in `DisplayEquipmentSlot()`
- `Scripts/Systems/DailySystemManager.cs` — Moved all daily activity counter resets outside the `Endless` mode guard so they reset in online mode
- `Scripts/Systems/WorldSimulator.cs` — Fixed operator precedence in rival-selection LINQ query preventing NPC self-rivalry
- `Scripts/Systems/FeatureInteractionSystem.cs` — Added unique lore fragments for all 6 missing dungeon themes (Sewers, Caverns, DemonLair, FrozenDepths, VolcanicPit, AbyssalVoid)
- `Scripts/Server/RoomRegistry.cs` — Removed arrival and departure broadcasts (kept disconnect)
- `Console/Bootstrap/Program.cs` — Removed early `StartOnlineTracking()` call; saves `DeferredConnectionType` for later use; wrapped cleanup in 5-second timeout to prevent process hang
- `Scripts/Server/PlayerSession.cs` — Removed early `StartOnlineTracking()` and login broadcast; defers to after character load
- `Scripts/Systems/OnlineStateManager.cs` — Added `DeferredConnectionType` property
- `Scripts/UI/TerminalEmulator.cs` — `UpdateMudIdleTimeout()` now called on printable characters and backspace with 5-second throttle; `ReadLineInteractiveAsync()` defers message delivery while user is actively typing (500ms grace period prevents input stomping)
- `Scripts/Systems/DifficultySystem.cs` — Added `GetMonsterHPMultiplier()`, `IsPermadeath()`, `ApplyMonsterHPMultiplier()`; updated difficulty descriptions to reflect all active effects
- `Scripts/Systems/MonsterGenerator.cs` — Monster HP now scaled by difficulty multiplier after SysOp multiplier
- `Scripts/Systems/CharacterCreationSystem.cs` — Nightmare selection shows permadeath warning with double confirmation
- `Scripts/Locations/WeaponShopLocation.cs` — Difficulty-based shop price multiplier (main purchase + dual-wield path)
- `Scripts/Locations/ArmorShopLocation.cs` — Difficulty-based shop price multiplier
- `Scripts/Locations/MagicShopLocation.cs` — Difficulty-based shop price multiplier in `ApplyAllPriceModifiers()` (covers all magic shop purchases)
- `Scripts/Locations/AdvancedMagicShopLocation.cs` — Difficulty-based shop price multiplier
- `Scripts/Locations/HealerLocation.cs` — Difficulty-based price multiplier in `GetAdjustedPrice()`
- `Scripts/Locations/MainStreetLocation.cs` — Removed `[R] Relations` from all three menu displays (normal, BBS compact, screen reader)
- `web/index.html` — Added Lunatics Unleashed to BBS list
