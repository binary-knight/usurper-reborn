# v0.44.1 - The Eternal Cycle

The game's ending system was completely broken -- defeating the final boss showed a brief text and returned you to the dungeon. The full cinematic endings, credits, and New Game+ were all implemented but never connected. Beyond that, NG+ itself was broken end-to-end: no game restart, no XP multiplier, critical story flags lost on save/load, and cycle data wiped on new character creation. This release fixes the entire ending and NG+ pipeline.

---

## Bug Fixes

### Ending Sequence Never Triggered After Defeating Manwe
Defeating the final boss showed a brief abbreviated ending and a "Congratulations" box, then returned you to the dungeon. The full cinematic ending sequences, credits, and New Game+ offer were implemented in `EndingsSystem` but never actually called -- DungeonLocation had its own duplicate abbreviated versions. Now defeating Manwe triggers the proper ending flow: full cinematic ending text, credits sequence, and the NG+ cycle offer before returning to town.

### Manwe Defeat Dialogue Was Placeholder Text
Defeating Manwe displayed literal placeholder text ("This text depends on which ending path the player is on...") instead of actual defeat dialogue. Replaced with proper thematic dialogue about the Creator's final moments.

### Spell Learning Menu Missing Descriptions
The Level Master's Spell Library showed spell names and mana costs but no descriptions of what spells actually do. All spell sections (quickbar, known, learnable, locked) now display the spell's effect description so players know what they're learning before investing.

### NG+ Never Restarted the Game
After defeating Manwe and accepting New Game Plus, the game applied cycle bonuses to your existing level-100 character and dumped you back on Main Street. There was no character creation, no fresh start, and no opening sequence. Now the full NG+ flow works: accept NG+ -> return to main menu -> create a fresh level-1 character -> receive cycle bonuses (stat boosts + XP multiplier) -> play the NG+ opening sequence -> enter the game world. Works in both local/single-player and online/BBS modes.

### XP Multiplier Never Applied
The cycle bonus system calculated an XP multiplier (1.25x for cycle 2, scaling up) and displayed it to the player, but the value was discarded with a code comment reading "Could add ExpMultiplier property to Character." The property now exists (`CycleExpMultiplier`) and is applied in all three combat XP calculation paths.

### String Story Flags Never Saved
Critical story flags like `veloura_saved`, `noctura_ally`, `truth_revealed`, and `ready_for_dissolution` were stored as string flags but never serialized. The save code had a bug: `new Dictionary<string, bool>(story.StoryFlags)` used the integer bitmask as a dictionary capacity, creating an empty dictionary every time. After any save/load, all string-based story flags were lost, breaking ending detection and the Dissolution ending prerequisites.

### Completed Endings Never Saved
The list of endings a player has achieved had no field in the save data structure and was never written to disk. After any save/load, the game forgot which endings you'd completed, breaking NG+ cycle tracking and the Dissolution ending.

### CreateNewGame Wiped Cycle Data
When NG+ triggered character creation, it called `FullReset()` which reset the cycle counter back to 1 and cleared completed endings — destroying the cycle data that NG+ depends on. Now `FullReset()` is skipped during NG+ since `StartNewCycle()` already handles story state reset.

---

## Files Changed

| File | Changes |
|------|---------|
| `Scripts/Core/GameConfig.cs` | Version 0.44.1 "The Eternal Cycle" |
| `Scripts/Locations/DungeonLocation.cs` | Replaced 3 calls to local abbreviated `ShowEnding()` with `EndingsSystem.Instance.TriggerEnding()`; removed ~220 lines of duplicate abbreviated ending methods; all 3 ending trigger points now throw `LocationExitException(NoWhere)` when NG+ is pending |
| `Scripts/Data/OldGodsData.cs` | Replaced Manwe's placeholder DefeatDialogue with proper thematic text |
| `Scripts/Systems/SpellLearningSystem.cs` | Added spell descriptions to all four display sections |
| `Scripts/Core/Character.cs` | Added `CycleExpMultiplier` property (float, default 1.0) for NG+ XP scaling |
| `Scripts/Systems/StoryProgressionSystem.cs` | Added `ExportStringFlags()`, `ImportStringFlags()`, `AddCompletedEnding()` public methods for proper serialization |
| `Scripts/Systems/SaveDataStructures.cs` | Added `CycleExpMultiplier` to `PlayerData`; added `CompletedEndings` list to `StorySystemsData` |
| `Scripts/Systems/SaveSystem.cs` | Fixed StoryFlags serialization to use `ExportStringFlags()`; added CompletedEndings save/restore; added CycleExpMultiplier save/restore |
| `Scripts/Systems/CombatEngine.cs` | Applied `CycleExpMultiplier` in all 3 XP calculation paths; added missing Study XP bonus to third XP path |
| `Scripts/Systems/OpeningSequence.cs` | Fixed `ApplyCycleBonuses()` to set `CycleExpMultiplier` on character; added `ApplyCycleBonusesToNewCharacter()` public method |
| `Scripts/Systems/EndingsSystem.cs` | Set `PendingNewGamePlus` flag after player accepts NG+ in `OfferNewGamePlus()` |
| `Scripts/Core/GameEngine.cs` | Added `PendingNewGamePlus` flag; modified `MainMenu()` to auto-start `CreateNewGame` on NG+; modified `CreateNewGame()` to skip `FullReset()` and apply cycle bonuses on NG+; added CycleExpMultiplier restore on load; handled NG+ restart in `RunBBSDoorMode()` |
