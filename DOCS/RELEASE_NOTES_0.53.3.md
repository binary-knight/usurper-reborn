# Usurper Reborn v0.53.3 Release Notes

**Version Name:** Ancestral Spirits

## Romantic Orientation System

Players can now set their romantic orientation during character creation: Straight, Gay/Lesbian, Bisexual, or Asexual. This affects which NPCs appear as romance options in gossip, mingle parties, and compatibility displays. Existing characters can change their orientation anytime from the Preferences menu (`[O] Orientation`). Orientation is preserved across NG+ cycles.

NPC orientation distribution has been rebalanced (85% straight, 8% gay/lesbian, 5% bisexual, 2% asexual — up from 96/1.5/1.5/1%) to ensure viable romance options for all players. A minimum of 2 gay, 2 lesbian, and 2 bisexual NPCs are guaranteed in every world. Asexual players receive a friendly message when checking gossip instead of an empty singles list.

## Healer Restores Mana

The `[H] Heal` option at the Healer now also restores mana to full. Previously only `[F] Full Heal` restored mana, leaving casters at 0 MP after using partial healing. If HP is already full but mana isn't, healing restores mana for free.

## Dungeon Mana Potion Menu

Mana-using classes now see a `[M] Use Mana Potion` option in the dungeon healing menu (safe havens, rest spots). Shows restore amount and remaining potions. Each potion restores 30 + (level * 5) MP.

## Spell Buffs No Longer Persist Between Fights

Protection spells (Fog of War, Arcane Shield, Prismatic Shield, etc.) and attack buffs with "Duration: whole fight" incorrectly persisted across multiple combats with 999-round durations. `MagicACBonus` and spell status effects (`Protected`, `Blessed`, `Haste`) are now cleared at the start of each combat. These spells still last the entire fight they're cast in, but no longer carry over.

## Quickbar Auto-Fill Fix

Auto-fill in the ability quickbar wiped the entire quickbar and refilled from scratch. For spellcasters like Sage, this replaced all manually-placed abilities with only spells. Auto-fill now adds abilities to empty slots without touching existing entries. Same fix applied to the spell quickbar. Both screens skip duplicates and report when nothing new is available.

## Monster Attack Message Clarity

Monster attack messages previously showed two damage numbers per hit — raw attack power in the flavor text ("Imp strikes you hard for 130 damage!") then actual damage after armor ("The Imp hits you for 80 damage!"). Players read both as separate hits. The flavor message no longer shows a number ("Imp strikes you hard!"), leaving only the actual damage line.

## Temple Global Commands Fix

The Temple location had its own menu loop that didn't call `TryProcessGlobalCommand`, so global commands like `!` (bug report), `/health`, `/gear`, and `?` (help) didn't work there. Now all global commands work in the Temple.

## Prayer Daily Check Fix (Single-Player)

Prayer "already prayed today" check used real-world `DateTime.Now.Date`, which never resets when multiple in-game days pass in one session. Now uses the in-game day counter (`DailySystemManager.CurrentDay`) for single-player mode. Online mode was already correct.

## Dungeon Floor Clear Quest Fix

"Clear Floor" quests were impossible to complete if the player had already cleared the target floor before taking the quest. The quest system only received floor-clear notifications on the first clear (`EverCleared` guard). Now notifies the quest system on every clear.

## Home Resurrect Key Fix

The `[!] Resurrect` option at Home conflicted with the global `[!] Bug Report` command. Changed Resurrect to `[X]` so both features work independently.

## Silent Keypress Prompts Fixed

`WaitForKey()` (used in herb consumption, dreams, chest operations, trophies, and other screens) paused without showing any prompt, making it unclear the game was waiting for input. Now shows the localized "Press any key to continue..." message like `PressAnyKey()`.

## Inn Dream Double-Keypress Fix

Resting at the Inn table with a dream triggered two sequential "press any key" prompts — one after the dream and one at the end of the rest. Now only one keypress is needed.

## Dormitory Sleep Prompt

Sleeping at the Dormitory navigated to Main Street immediately after the sleep messages with no pause. Added a "Press any key" prompt so players can read the recovery messages.

## Wishing Well Invalid Input Fix

Typing an invalid key at the Wishing Well encounter (e.g., "1" instead of "T") silently ended the event. Invalid input now shows an error message and re-prompts instead of treating it as "leave".

## Quest Test Fix

Updated `ClearBossQuest_HasKillBossObjective` test to match the current quest title format ("Defeat" instead of "Slay the").

---

## Files Changed

- `Scripts/Core/GameConfig.cs` — Version 0.53.3
- `Scripts/Core/Character.cs` — `Orientation` property (SexualOrientation)
- `Scripts/Core/GameEngine.cs` — Orientation restore from save data; NG+ preserves orientation across cycles
- `Scripts/Systems/CombatEngine.cs` — `MagicACBonus`/`Protected`/`Blessed`/`Haste` cleared at combat start (spell buff persistence fix)
- `Scripts/Systems/CombatMessages.cs` — Monster attack flavor message no longer shows raw damage number
- `Scripts/Systems/ClassAbilitySystem.cs` — Ability quickbar auto-fill preserves spell entries
- `Scripts/Systems/SpellLearningSystem.cs` — Spell quickbar auto-fill preserves ability entries
- `Scripts/Systems/DivineBlessingSystem.cs` — Single-player prayer check uses in-game day counter instead of real-world date; `lastPrayerDay` dictionary
- `Scripts/Systems/CharacterCreationSystem.cs` — Orientation selection during character creation (Straight/Gay-Lesbian/Bisexual/Asexual)
- `Scripts/Systems/NPCSpawnSystem.cs` — `EnsureOrientationDiversity()` guarantees minimum 2 gay, 2 lesbian, 2 bisexual NPCs
- `Scripts/Systems/SaveDataStructures.cs` — `Orientation` field in PlayerData
- `Scripts/Systems/SaveSystem.cs` — Orientation serialization
- `Scripts/Systems/RareEncounters.cs` — Wishing well input validation loop
- `Scripts/AI/PersonalityProfile.cs` — NPC orientation rates rebalanced (85/8/5/2); `IsMalePresenting`/`IsFemalePresenting` made public
- `Scripts/UI/TerminalEmulator.cs` — `WaitForKey()` now shows "Press any key" prompt instead of silent pause
- `Scripts/Locations/LoveStreetLocation.cs` — `IsPlayerAttractedTo()` helper; player orientation filter on gossip singles, mingle party, and compatibility display; asexual gossip message
- `Scripts/Locations/BaseLocation.cs` — `[O] Orientation` in preferences; `GetOrientationLabel()` helper
- `Scripts/Locations/DungeonLocation.cs` — `[M] Use Mana Potion` in healing menu; floor clear quest notification on every clear (not just first)
- `Scripts/Locations/HomeLocation.cs` — Resurrect key `[!]` changed to `[X]`
- `Scripts/Locations/HealerLocation.cs` — `[H] Heal` also restores mana to full
- `Scripts/Locations/InnLocation.cs` — Dream double-keypress fix in `RestAtTable()`
- `Scripts/Locations/DormitoryLocation.cs` — Added `PressAnyKey()` before navigating to Main Street after sleep
- `Scripts/Locations/TempleLocation.cs` — Added `TryProcessGlobalCommand` for global command support
- `Tests/QuestSystemTests.cs` — Updated quest title assertion ("Defeat" instead of "Slay the")
- `Localization/en.json` — Orientation keys; dungeon mana potion keys; gossip asexual message; ability/spell auto-fill messages; prefs.orientation
- `Localization/es.json` — All new keys translated
- `Localization/fr.json` — All new keys translated
- `Localization/hu.json` — All new keys translated
- `Localization/it.json` — All new keys translated
