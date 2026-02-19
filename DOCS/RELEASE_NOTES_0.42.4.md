# v0.42.4 - Balance & Bug Fixes

XP curve adjustment, dungeon respawn overhaul, loot balance, companion equipment fix, relationship bug fix, and quality-of-life improvements.

---

## Balance Changes

### XP Curve Softened
The XP curve has been softened from `level^2.2` to `level^2.0`.

This will primarily affect players who are close to level 40+.

### Dungeon Floor Respawn (1 Hour)
Dungeon floors now respawn every **1 hour** instead of 6 hours. Once an hour has passed since you cleared a floor, monsters will return when you revisit it. Treasure chests remain looted, and boss/seal floors stay permanently cleared. A flavor message is broadcast to all players when the dungeon stirs.

The **Dungeon Reset Scroll** has been removed from the Magic Shop — it's no longer needed.

### Loot Drop Rate Increased
Monster equipment drop rates have been increased to make dungeon crawling more rewarding:

- **Regular monsters**: 12% base + 0.5% per level, capped at 30% (was 8% + 0.3%/level, capped at 25%)
- At level 10: **17%** drop chance (was 11%)
- At level 20: **22%** (was 14%)
- Named monsters, mini-bosses, and floor bosses are unchanged.

## Bug Fixes

### Companion Equipment Disappearing (Critical)
Equipment given to story companions (Lyris, Aldric, Mira, Vex) from your inventory or dungeon loot would vanish after logging out and back in. The game saved companion equipment slot references correctly, but the actual item definitions for dungeon loot and inventory items were only saved from the player's own equipment — not from companion equipment. On reload, the companion had empty references pointing to items that no longer existed. All dynamic equipment on companions is now saved alongside the player's own items.

### Relationship Progress Resetting (Critical)
Relationship progress with NPCs was resetting back to "Strangers" (70) in two ways:

1. **Different locations forgot your progress.** Building a relationship in one place (e.g., the Inn) was invisible when you found the same NPC elsewhere. The relationship dictionary used argument order as the key — `(player, npc)` and `(npc, player)` created two separate records instead of finding the same one. All lookups now check both orderings.

2. **Other players logging in wiped your relationships (online mode).** The relationship dictionary was shared across all MUD sessions. When any player logged in, `ImportAllRelationships` cleared the entire dictionary and loaded only that player's data. Each player's relationships are now isolated per-session.

---

## Shop Improvements

### Level Requirements Shown in Shops
The Weapon Shop, Armor Shop, and Magic Shop (accessories) now display a **Lvl** column showing each item's minimum level requirement. Items you can't equip yet due to level show the requirement in red. Items with no level requirement show a dash.

---

## UI Improvements

### Dungeon Map Rewrite
The dungeon map now centers on your current room and shows rooms within 3 steps in every direction. Rooms are placed using their actual exit directions, so the map always matches what you see. Moving west puts you to the left; moving south puts you below. The old map tried to lay out the entire floor at once, which caused rooms to jump around and connectors to point nowhere.

### Trade Menu Clarified
The trade packages menu now clearly explains that commands require a letter + number together (e.g., `A1` to accept package #1, `D2` to decline package #2). Shows a contextual example when you have incoming packages.

---

## Files Changed

| File | Changes |
|------|---------|
| `Scripts/Core/GameConfig.cs` | Version 0.42.4 |
| `Scripts/Systems/SaveSystem.cs` | `CollectAllDynamicEquipmentIds()` — include companion dynamic equipment IDs in save |
| `Scripts/Systems/RelationshipSystem.cs` | ThreadStatic isolation for MUD mode; bidirectional key lookup in GetRelationshipStatus, GetRelationship, GetOrCreateRelationship; direction-aware UpdateRelationship |
| `Scripts/Locations/BaseLocation.cs` | XP formula: 2.2 -> 2.0; trade menu UI clarified with explicit A#/D#/C# instructions |
| `Scripts/Locations/WeaponShopLocation.cs` | Added Lvl column to weapon and shield listings |
| `Scripts/Locations/ArmorShopLocation.cs` | Added Lvl column to armor listings |
| `Scripts/Locations/MagicShopLocation.cs` | Added Lvl column to accessory listings; removed Dungeon Reset Scroll |
| `Scripts/Locations/LevelMasterLocation.cs` | XP formula: 2.2 -> 2.0 |
| `Scripts/Locations/DungeonLocation.cs` | XP formula: 2.2 -> 2.0; map rewrite (current-room-centered local BFS); dungeon respawn broadcast with flavor text bank |
| `Scripts/Systems/DungeonGenerator.cs` | `RESPAWN_HOURS`: 6 -> 1 |
| `Scripts/Systems/CombatEngine.cs` | XP formula: 2.2 -> 2.0; loot drop rate: 12%+0.5%/lvl cap 30% (was 8%+0.3%/lvl cap 25%) |
| `Scripts/Locations/MainStreetLocation.cs` | XP formula: 2.2 -> 2.0 |
| `Scripts/Core/GameEngine.cs` | XP formula: 2.2 -> 2.0 |
| `Scripts/Core/NPC.cs` | XP formula: 2.2 -> 2.0 |
| `Scripts/Systems/CompanionSystem.cs` | XP formula: 2.2 -> 2.0 |
| `Scripts/Systems/NPCSpawnSystem.cs` | XP formula: 2.2 -> 2.0 |
| `Scripts/Systems/WorldInitializerSystem.cs` | XP formula: 2.2 -> 2.0 |
| `Scripts/Systems/WorldSimulator.cs` | XP formula: 2.2 -> 2.0 |
| `Scripts/Systems/WorldSimService.cs` | XP formula: 2.2 -> 2.0 |
