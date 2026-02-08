# Usurper Reborn - v0.25.12 Release Notes

## Bug Fixes

### Critical: Unequip Item Loss (GitHub #52)
- **Unequipping items no longer destroys them**: When unequipping a single item via the inventory screen, the item was removed from the equipment slot but never added back to the player's backpack. The item was effectively deleted. The text even said "(Item returned to shop inventory)" but no such transfer occurred. Fixed by properly converting the unequipped item to a legacy Item and adding it to the player's backpack. Also fixed the "Unequip All" slot-to-type mapping which had incorrect mappings for Hands (was Arms), Feet (was Legs), and was missing Cloak, Waist, and Face.

### NPC Team Membership Fixes
- **NPCs no longer randomly leave the player's team**: Three systems were incorrectly processing player team members as NPC-only gangs:
  - `WorldSimulator.CheckTeamBetrayals()` - 1% chance per 30s tick for low-loyalty NPCs to leave any team, including the player's
  - `NPCMaintenanceEngine.ProcessGangLoyalty()` - 30% chance for loyalty<20 NPCs to defect from player's team
  - `IsNPCOnlyGang()` in both `EnhancedNPCBehaviorSystem` and `NPCMaintenanceEngine` - Player's team appeared as NPC-only since the player isn't in the NPC list, allowing gang dissolution
  - All three systems now skip player team members
- **Random NPCs no longer autonomously join the player's team**: Three additional code paths allowed NPCs to recruit or join the player's team without the player's consent, causing "wrong NPC on my team" reports:
  - `WorldSimulator.NPCTryRecruitForTeam()` - Player's team members could autonomously recruit random solo NPCs
  - `WorldSimulator.NPCTryJoinOrFormTeam()` - Solo NPCs could autonomously join the player's team at the same location
  - `NPCMaintenanceEngine.ProcessGangLoyalty()` - High-loyalty NPCs on the player's team could recruit others
  - `NPCMaintenanceEngine.FindBetterGang()` - NPCs from weaker gangs could defect into the player's team
  - All four paths now exclude the player's team from autonomous NPC team operations

### Town NPC Story Fixes
- **Pip story stage skip fixed**: After a game reset, Pip would immediately jump to Stage 1 ("Caught") instead of starting at Stage 0 ("The Lift"). The "Next visit" trigger in `IsTriggerMet()` was unconditionally returning `true` without checking if a previous stage had been completed. Now requires at least one completed stage before firing.
- **Pip pickpocket encounter rate fixed**: Pip's Stage 0 random encounter (30% chance) was re-rolling every time the player entered Main Street. Since Main Street is the central hub, the cumulative probability approached 100% after just a few visits. Random triggers now roll once per game day and cache the result, making Pip feel like a genuine surprise encounter.
- **Elena "Next visit after rescue" trigger also fixed** by the same "Next visit" trigger fix above.

### Cloak Equip Slot Fix
- **Cloaks no longer equip as weapons**: Taking a cloak from an NPC teammate and equipping it would put it in the MainHand weapon slot instead of the Cloak slot. The `ObjType.Abody` (cloak) was missing from the ObjType-to-EquipmentSlot mapping in 4 locations: `HomeLocation.cs`, `InventorySystem.cs`, and two mappings in `CombatEngine.cs`. All now correctly map `Abody` to `EquipmentSlot.Cloak`.

### Stranger Encounter Dialogue Fix
- **First Stranger encounter now shows actual dialogue**: The Stranger (Noctura in disguise) encounter was showing a narrator description ("The figure watches you with eyes that seem to hold secrets.") in dialogue quotes instead of the NPC speaking. Caused by an off-by-one error: `GetDialogue()` was checking `EncountersHad` (still 0) against the dialogue pool (which starts at encounter 1), so no dialogue matched and it fell through to a fallback that was a narrator description rather than speech. Fixed by using `EncountersHad + 1` for the lookup and replacing the fallback with actual dialogue.

### Journal Spoiler Removal
- **Seal locations no longer spoiled in journal**: The Story Journal was showing exact dungeon floor numbers for every seal (e.g., "Dungeon Floor 15", "Dungeon Floor 30"). Uncollected seals now show "Hidden in the dungeon depths" with only the thematic hint to guide players. Collected seals still show their full title.
- **Old God locations hidden until encountered**: The journal was revealing all 7 Old God floor numbers from the start. Gods the player hasn't encountered yet now show "???: Something stirs in the depths..." instead of exact floor and name. Once encountered, full details are revealed.
- **Suggested steps no longer reveal seal floors**: The "Suggested Next Steps" section was showing exact floor numbers for the next seal to find. Now shows "Seek the [seal name] in the dungeon depths" instead.

### Files Changed
- `InventorySystem.cs` - Fixed single-item unequip to return item to backpack instead of deleting it; fixed Unequip All slot mappings (Hands, Feet, Cloak, Waist, Face); added `ObjType.Abody => EquipmentSlot.Cloak` equip mapping
- `TownNPCStorySystem.cs` - Fixed "Next visit" trigger validation; added once-per-day random trigger caching
- `WorldSimulator.cs` - Skip player team members in `CheckTeamBetrayals()`; block autonomous NPC recruitment/joining into player's team
- `NPCMaintenanceEngine.cs` - Skip player team members in `ProcessGangLoyalty()`; block autonomous recruitment into player's team; fixed `IsNPCOnlyGang()`; fixed `FindBetterGang()` to exclude player's team
- `EnhancedNPCBehaviorSystem.cs` - Fixed `IsNPCOnlyGang()` to recognize player teams
- `DungeonLocation.cs` - Removed seal floor numbers, Old God floor spoilers, and seal floor from suggested steps
- `HomeLocation.cs` - Added `ObjType.Abody => EquipmentSlot.Cloak` mapping
- `CombatEngine.cs` - Added `ObjType.Abody => EquipmentSlot.Cloak` mapping (2 locations)
- `StrangerEncounterSystem.cs` - Fixed off-by-one in dialogue pool lookup; replaced narrator fallback with speech
