# Usurper Reborn - v0.30.3 Bug Fixes & Online World Sim Fixes

Fixes NPC chat crash, missing ability descriptions, accessory quest tracking, NPC leveling, NPC polygamy, tension message spam, dead king still ruling, and marriage count on the dashboard.

---

## Bug Fixes

- **NPC chat crash (Object reference not set)**: Chatting with any NPC (option 1) crashed with "Error loading save: Object reference not set to an instance of an object" and kicked the player out of the game. Root cause: `RestorePlayerFromSaveData()` in GameEngine created `new Character` instead of `new Player`. Since `Player` inherits from `Character`, all `currentPlayer as Player` casts returned null, and `NPCDialogueDatabase.GetPlayerContext()` crashed on `player.MaxHP`. Fixed by creating `new Player` in the restore method. Added null guards in `GetPlayerContext()` and `GetRecentMemoryType()` as defense in depth.

- **Accessory purchase quests never track**: Buying an accessory (ring, amulet, belt) from the Magic Shop never updated equipment purchase quest objectives. The Magic Shop (`MagicShopLocation.cs`) was missing the `QuestSystem.OnEquipmentPurchased()` call entirely — only the old unused Advanced Magic Shop had it. Added the call to the Magic Shop's accessory purchase path so "Acquire: Amulet of Wisdom" and similar quests now complete when you buy the item.

- **Ability/spell descriptions missing from Level Master**: The combat abilities menu at the Level Master only showed ability name and stamina cost, with no description of what each ability does. Players had no way to know what abilities did before equipping them. Added ability descriptions (in gray) to all three sections: equipped quickbar slots, available abilities, and locked abilities.

- **NPC XP multiplier silently rejected on online server**: The `--npc-xp` command-line parameter had a validation cap of 2.0, but the MUD service was configured with `--npc-xp 3.0`. The value was silently rejected, defaulting to 0.25x (25% XP). NPCs were leveling at 1/12th the intended rate, resulting in almost all NPCs stuck at level 0-9. Raised the validation cap to 10.0 so the configured 3.0x multiplier takes effect.

- **NPC polygamy bug (one-sided marriages)**: Monogamous NPCs could end up with multiple spouses because `ExecuteNPCMarriage()` didn't verify whether either NPC was already married before setting the marriage. If NPC A married NPC B, then NPC C could also marry NPC B (overwriting B's spouse to C), leaving A still listing B as their spouse — a one-sided marriage. ~12 NPCs on the live server had broken marriage states from this. Fixed by adding a validation guard at the top of `ExecuteNPCMarriage()`: if either NPC is already married, they must have the Polyamorous or Open Relationship preference, otherwise the marriage is aborted.

- **"Tensions are rising" message spam**: Tension messages between rival NPCs dominated 17% of the news feed (34 of 200 entries). Two causes: the cooldown check used `ContainsKey` instead of a proper time-based comparison, so expired cooldowns still blocked new messages after daily resets; and the 30-minute cooldown was too short for the number of active rivalries. Fixed by replacing `ContainsKey` with a `TryGetValue` + elapsed tick comparison, increasing the cooldown from 60 to 120 ticks (~1 hour), and adding a per-tick cap of 2 tension messages maximum so they can never dominate the feed regardless of how many rivalries exist.

- **Dead king still ruling**: When an NPC king died (in dungeon combat, rivalry battles, or old age), the throne was never vacated — the dead NPC stayed listed as ruler with 0 HP. No code checked whether a dying NPC was the current king. Added `CheckKingDeath()` to all 7 NPC death paths in the world simulator, which calls the new `VacateThrone()` method on CastleLocation. The throne is marked inactive, a news announcement is posted, and the world state is persisted. The next player to visit the castle will trigger NPC succession automatically (existing `TriggerNPCSuccession()` logic).

- **Dashboard showing "Marriages: 0"**: The stats API marriage count query was reading from the `players` table (player-to-NPC relationship data), which returned 0 because no players had married NPCs. The actual NPC-to-NPC marriage data lives in the `world_state` table under the `npcs` key. Fixed the query to read NPC data from `world_state`, count entries with `isMarried = true`, and divide by 2 (since each marriage has two entries).

---

## Files Changed

| File | Changes |
|------|---------|
| `Scripts/Core/GameConfig.cs` | Version bump to 0.30.3 |
| `Scripts/Core/GameEngine.cs` | Fixed `RestorePlayerFromSaveData()`: changed `new Character` to `new Player` so `currentPlayer as Player` casts succeed |
| `Scripts/Data/NPCDialogueDatabase.cs` | Added null guards in `GetPlayerContext()` and `GetRecentMemoryType()` for defense in depth |
| `Scripts/Locations/MagicShopLocation.cs` | Added `QuestSystem.OnEquipmentPurchased()` call to accessory purchase path |
| `Scripts/Systems/ClassAbilitySystem.cs` | Added ability descriptions to quickbar, available, and locked ability displays in `ShowAbilityLearningMenu()` |
| `Scripts/BBS/DoorMode.cs` | Raised `--npc-xp` validation cap from 2.0 to 10.0 so higher multipliers aren't silently rejected |
| `Scripts/AI/EnhancedNPCBehaviors.cs` | Added validation guard in `ExecuteNPCMarriage()`: checks both NPCs are either unmarried or poly/open before proceeding |
| `Scripts/Systems/WorldSimulator.cs` | Increased `TENSION_MESSAGE_COOLDOWN_TICKS` from 60 to 120; added `MAX_TENSION_MESSAGES_PER_TICK = 2` cap; replaced `ContainsKey` with time-based cooldown check; added `CheckKingDeath()` helper called from all 7 NPC death paths (aging, solo dungeon, team dungeon, 4 rivalry combat paths) |
| `Scripts/Locations/CastleLocation.cs` | Added `VacateThrone()` static method: marks king inactive, posts news, persists to world_state |
| `web/ssh-proxy.js` | Fixed marriage count query: reads NPC marriage data from `world_state` key `'npcs'` instead of player relationship data from `players` table |
