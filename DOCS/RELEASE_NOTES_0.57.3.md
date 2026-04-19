# v0.57.3 - Sysop Editor + Equipment Modding

Community feature in response to a player question: "I used to run an old Usurper game on DOSBox. I was able to act like a sysop and change different parameters of the game — add equipment, NPCs, edit players. Can I do that in Reborn?"

The answer in v0.57.3 is yes. Two coordinated additions:

1. **Equipment modding** — `GameData/equipment.json` now joins the other six moddable data files (NPCs, monsters, dreams, achievements, dialogue, balance). Modders can add custom weapons, armor, shields, and accessories with full stat control. Built-in items stay untouched for save compatibility.

2. **Standalone editor** — new `UsurperReborn --editor` mode launches an interactive menu-driven editor, analogous to the old DOS-era `USEDIT.EXE` that shipped alongside the original game. Reads and writes the same JSON files the modding system uses. Covers player saves, equipment, NPCs, and balance in this release — more editor menus in future phases.

## Equipment modding

Fully additive: the 300-700 built-in items in `EquipmentDatabase` are unchanged, and custom mod items live in a separate ID range (200,000+). Conflicts with built-in IDs or shop-generated/dungeon-loot IDs are blocked with a warning at load.

File format is straightforward — any public property on the `Equipment` class (name, slot, weapon power, stat bonuses, enchantments, restrictions, rarity) is a JSON field. Missing properties get defaults.

Minimum viable custom item:

```json
[
  {
    "id": 200001,
    "name": "Test Blade",
    "slot": "MainHand",
    "handedness": "OneHanded",
    "weaponType": "Sword",
    "weaponPower": 25,
    "strengthBonus": 3,
    "value": 5000,
    "minLevel": 10,
    "rarity": "Rare"
  }
]
```

`UsurperReborn --export-data` now also writes an example `equipment.json` with two sample items so modders can see the shape of the file without guessing.

## Standalone editor

```
UsurperReborn --editor
```

Works on Windows, Linux, and macOS. Pure console-mode tool — no graphical terminal required, no MUD networking stack involved, no TerminalEmulator plumbing. Just `Console.Write` / `Console.ReadLine` talking to the same JSON files the game reads at startup.

Players don't need to remember the CLI flag, though — the main menu now has a `[G] Game Editor` option that launches the same editor in-process. Only shown in local single-player / Steam contexts; hidden for BBS door sessions (sysops have their own admin tools), the live MUD server process, and MUD relay sessions. When the editor closes, control returns to the main menu so the player can start their freshly-edited game immediately.

## UI: Spectre.Console + arrow-and-number menus

The editor uses [Spectre.Console](https://spectreconsole.net/) for rendering, but every menu and picker goes through one custom controller (`RunArrowMenu`) so the navigation is uniform everywhere:

- **Arrow keys** (up/down/left/right) move a highlight marker. `j`/`k` do the same (vim-style). `Home`/`End`/`PgUp`/`PgDn` jump to ends.
- **Number keys** select directly. Single-digit shortcut for items 1-9; multi-digit with auto-commit for larger menus — typing `1` in a 17-item menu stays buffered (since 10-17 might follow), typing `2` commits instantly (no 20+ possible). `Backspace` edits the buffer.
- **Enter** commits the current highlight. **`0` / `Q` / `Esc`** back out of the current menu (or clear the number buffer on first press if one is active).
- **Redraw is in place**. `\x1b[{N}A\r\x1b[0J` moves up the exact line count of the last render and clears to end of screen, so the menu updates without scrolling. Works regardless of buffer-vs-viewport mismatches on Windows Terminal.

Piped stdin (Git Bash, test harnesses, etc.) falls back automatically to a line-based number prompt — single-key reads aren't possible over a pipe, so we stop trying to fake them.

## UI: pickers everywhere magic strings used to be

The first editor pass had a lot of "type the exact ID" / "type the enum name" free-text fields. Every one of those is now an arrow-key picker backed by a controlled vocabulary:

- **NPC Personality** → picker of the 58 built-in personalities + `Custom...`
- **NPC Alignment** → 3-way picker (Good / Evil / Neutral)
- **NPC StoryRole** → picker of the 10 known faction tags + `(none - regular NPC)` + `Custom...`
- **NPC Sex** → M/F picker
- **Monster BaseColor / tier Color** → picker from 23 ANSI color names + `Custom...`
- **Monster AttackType** → picker from 10 standard attack types
- **Dialogue Category / PersonalityType / Emotion / Context / MemoryType / EventType** → pickers from the taxonomies the dialogue engine actually uses
- **Player save: skill proficiency name** → picker from known combat skills
- **Player save: stat training stat** → picker from the 9 core stats
- **Player save: Old God + status** → enum pickers (no more "type 0 for Maelketh")
- **Player save: artifact grant** → enum picker of the 7 artifact names
- **Player save: companion recruit/dismiss/revive/set-relationship** → `CompanionId` enum picker
- **Player save: quest complete/cancel** → picker of the character's own active quests by title + status
- **Player save: ability grant** → picker from `ClassAbilitySystem.GetAllAbilities()`, shows name + level requirement
- **Player save: ability remove** → picker of abilities the character actually knows
- **Player save: achievement grant** → picker from the registry showing tier + name + ID
- **Player save: achievement revoke** → picker of currently-granted achievements only
- **Edit/delete flows in every sub-editor** → picker of actually-loaded entries (no more "type the exact name")
- **Balance property editor** → picker over all ~30 `BalanceConfig` properties, shows current value alongside each

One source of truth for all the vocabularies: [`Scripts/Editor/EditorVocab.cs`](Scripts/Editor/EditorVocab.cs). Add a new taxonomy entry there and every editor that uses it picks it up automatically.

## Persistence fix: dirty tracking across sub-editors

Initial release had a subtle bug: every game-data editor (NPCs, Equipment, Monsters, Dreams, Achievements, Dialogue, Balance) reloaded the JSON from disk at the top of its menu loop. Any in-memory edits (add/edit/delete) were silently thrown away the next time the user navigated the menu — unless they picked "Save" before going back to list/edit/delete anything else. Editing was basically broken.

Now: load ONCE before the loop, mutate in memory, track a `dirty` flag, warn on exit. New affordances consistent across all editors:

- An **`[UNSAVED]`** badge appears next to the count in the sub-editor header when edits are pending.
- The Save menu label becomes **`Save (writes x.json)  *UNSAVED CHANGES*`** as a second visual cue.
- New **"Reload from disk (discard unsaved changes)"** menu item to deliberately revert.
- **Exit confirmation** — backing out of an editor with unsaved edits prompts `Discard unsaved changes and exit?`.
- **Reset to built-in defaults** saves immediately (it's a one-shot intent, not something to stage).

The `PlayerSaveEditor` already had this model from its initial implementation; the fix brought the game-data editors in line.

## Top-level menu (reorganized into five groups)

1. **Player Saves** — pick a save, dive into 16 nested sub-menus covering nearly every editable aspect of a character:
   - **Character Info**: display/internal name, class (warned), race (warned), sex, age, alignment scales (Chivalry/Darkness), fame, knighthood + noble title, king flag, immortal flag, difficulty.
   - **Stats & Progression**: level, XP, the 9 core attributes (STR/DEX/CON/INT/WIS/CHA/DEF/AGI/STA), HP/Mana caps + current, WeapPow/ArmPow, resurrections (available/max/used), unspent training sessions and points.
   - **Gold & Economy**: gold on hand, bank gold, bank loan, interest, wage, royal loan, lifetime royal tax paid.
   - **Inventory & Equipment**: list inventory with stats, add items by copying from `EquipmentDatabase` (built-in or modded), remove by index, clear all. View equipped slots, equip an item in any slot, unequip a slot, clear all slots, uncurse equipped gear. Adjust potion counts (heal/mana/antidote).
   - **Spells & Abilities**: list known spells, grant all (or all+mastered) for broad caster access, clear spells. List/add/remove learned class abilities. View/clear quickbar.
   - **Companions**: list status of each (recruited/active/dead, level, loyalty/trust/romance), revive a fallen companion, set loyalty/trust/romance by ID, recruit or dismiss by ID, mass-revive all.
   - **Quests**: list active/completed, mark a quest complete by ID, cancel, clear all.
   - **Achievements**: list unlocked, grant/revoke individual, grant ALL known achievements from the registry, revoke all.
   - **Old Gods & Story**: view each god's status, set status by ID (Dormant/Awakened/Defeated/Saved/Allied/Consumed), grant all 7 seals, grant artifact by type ID, edit NG+ cycle number, clear story flags.
   - **Relationships & Family**: list per-NPC relationship scores, set by NPC name, clear all, edit kid count, clear divine wrath state (forgives the betrayed god).
   - **Status & Cleanup**: cure all diseases (Blind/Plague/Smallpox/Measles/Leprosy/LoversBane), clear poison + gnoll poison, clear drug addiction/steroid effects, reset ALL daily counters to fresh values (like a new day began), clear active status effects, release from prison (including murder conviction), clear wanted level, clear murder weight / perma-kill log.
   - **Appearance & Flavor**: height/weight, eyes/hair/skin indices, the 6 combat phrases, 4-line character description, battle cry.
   - **Skills & Training**: unspent training sessions and points, skill proficiency levels by name, gold-based stat training counts, crafting material inventory.
   - **Team / Guild / Factions**: team name, team password, leader flag, turf days, door guard count/type, unpaid NPC wage cleanup.
   - **Settings & Preferences**: AutoHeal, CombatSpeed, SkipIntimateScenes, ScreenReaderMode, CompactMode, Language, ColorTheme, AutoLevelUp, AutoEquipDisabled, DateFormatPreference, AutoRedistributeXP.
   - **World State**: single-player world settings on the save — current ruler name, bank interest rate, town pot value, current game day, turn count, game-time minutes, and bulk-clear buttons for active world events + news entries.

   Every edit is non-destructive until the user picks "Save changes" — the edit loop is a single large menu tree over the in-memory save graph. On save, the file is copied to `<name>.json.bak` before overwriting. Discarding changes at the end reverts cleanly.

2. **Game Data / Modding** (nested submenu, 7 editors):
   - **Equipment** — browse built-ins (read-only reference, ~600 items, paginated + filterable), CRUD on custom items in `equipment.json`.
   - **NPCs** — CRUD on `npcs.json`. Seeds with the 60 built-in town NPCs on first run.
   - **Monsters** — CRUD on `monster_families.json` — add/edit/delete families, per-family tier editing (name, level range, color, power multiplier, special abilities).
   - **Dreams** — CRUD on `dreams.json`. Filter by ID/title, edit unlock conditions (level range, awakening, cycle, floor, chivalry/darkness/kills thresholds, god states, marriage/king/seals requirements), separate content editor for the line-by-line narrative text array.
   - **Achievements** — CRUD on `achievements.json`. Filter by ID/name, edit name/description/secret hint/category/tier/point value/gold+XP rewards.
   - **Dialogue** — CRUD on `dialogue.json` (~500 built-in lines). Filter by category + personality + text substring. Edit personality type, relationship tier, emotion, context, memory type, event type, NPC-specific name binding. "Show vocabulary" mode lists all unique categories/personalities/emotions/contexts across the set so modders can match the existing taxonomy.
   - **Balance** — reflection-driven editor over `BalanceConfig`'s ~30 numeric/bool properties. New balance properties become editable with zero editor code changes.

3. **Save File Management** — list all saves on disk with sizes and last-modified times, clone a save to a new character name (copy with rename), delete a save (double-confirmed, cleans up the `.bak` too), restore a save from its `.bak` backup, open the save directory in the OS file explorer.

4. **Export Defaults** — write all seven built-in data files to `GameData/` as a starting mod template.

5. **File Locations** — prints executable, GameData, saves, and Localization directory paths so modders/players know where things live.

## Design principles

- **The editor is a convenience, not a requirement.** Everything it does can be done by hand in a text editor. JSON is readable.
- **The editor can't corrupt a running game.** It never loads any game system, never opens a network port, never touches shared memory. It reads and writes files. If a game server is running, the only concern is the save file — document says exit first.
- **Save compatibility is preserved.** Built-in equipment IDs never change. Mods adding items use a reserved range. Removing a mod leaves built-ins intact; existing saves referencing deleted modded items silently skip that reference on load (same pattern as dynamic dungeon items).
- **Every edit is undo-able.** Player save edits backup to `.bak` before overwriting. JSON files are always reloadable from backups. The game falls back to built-ins if any mod file is missing or malformed.

## Scope for this release

In: equipment JSON support, seven game-data editors (equipment, NPCs, monsters, dreams, achievements, dialogue, balance), the comprehensive player-save editor (16 sub-categories), save file management (clone/delete/restore), docs, export-data example.

Out (planned for future releases):
- Online multiplayer world-state editor (SQLite — currently edit only per-save world state, not the shared DB)
- Spell and ability modding (still hardcoded in `SpellSystem` / `ClassAbilitySystem`)
- Quest template modding (per-save quest editing works; generating new quest templates for the game doesn't)
- Location description modding
- Localization file editor (edit JSON directly for now)

Each future phase adds one or two files to `GameData/` and one or two menus to the editor. The pattern is established — future additions are mechanical.

## Documentation

New: `DOCS/MODDING.md` — one-page guide covering what's moddable, how to start a mod, equipment schema, save compat, and concurrency warnings.

### Files Changed

- `Scripts/Core/GameConfig.cs` — Version 0.57.3.
- `Scripts/Systems/GameDataLoader.cs` — New `CustomEquipment` field + `equipment.json` load. New `ModdedEquipmentIdStart = 200000` constant. `ExportDefaults` now emits example `equipment.json`. New `GetExampleCustomEquipment` helper. Loader log message updated to show 7/7.
- `Scripts/Data/EquipmentData.cs` — After built-in + shop-generated items initialize, appends any custom items loaded by `GameDataLoader`. Validates IDs are in the 200000+ range with skip-and-warn for collisions. New `GetAll()` helper used by the editor.
- `Scripts/Editor/EditorIO.cs` — **NEW** — Console-based prompt/menu/confirm helpers. No MUD/BBS/terminal plumbing — pure `Console.*`. ~130 lines.
- `Scripts/Editor/EditorMain.cs` — **NEW** — Top-level editor menu loop. Initializes `GameDataLoader` + `EquipmentDatabase`, then dispatches to individual editors. Handles "export defaults" and "show file paths" utility actions.
- `Scripts/Editor/PlayerSaveEditor.cs` — **NEW** — Loads a save via `FileSaveBackend`, deserializes to `SaveGameData`, presents 11 nested sub-menus over the in-memory save graph: Character Info, Stats & Progression, Gold & Economy, Inventory & Equipment, Spells & Abilities, Companions, Quests, Achievements, Old Gods & Story, Relationships & Family, Status & Cleanup. Every edit stays in memory until explicit Save — discard-and-exit reverts cleanly. On save, the file is copied to `.bak` before overwriting. Uses `EquipmentDatabase` to resolve equipment IDs into readable names for the inventory/equipped views and to copy item stats when adding an inventory entry. Tolerates legacy save shapes by defaulting missing collections to empty. Roughly 600 lines organized by region.
- `Scripts/Editor/EquipmentEditor.cs` — **NEW** — CRUD on `equipment.json`. Browse built-ins (paginated, filterable). Add/edit/delete custom items at IDs 200000+. Per-slot field prompting (shields get BlockChance, armor gets WeightClass, weapons get WeaponPower, etc.).
- `Scripts/Editor/NPCEditor.cs` — **NEW** — CRUD on `npcs.json`. Seeds with built-in town NPCs on first run.
- `Scripts/Editor/MonsterEditor.cs` — **NEW** — CRUD on `monster_families.json`. Per-family metadata (name, description, color, attack type) plus nested tier editor (name, level range, color, power multiplier, list of special ability IDs).
- `Scripts/Editor/DreamEditor.cs` — **NEW** — CRUD on `dreams.json`. Filter by ID/title, edit all unlock-condition fields, separate content editor for the per-line narrative text array.
- `Scripts/Editor/AchievementEditor.cs` — **NEW** — CRUD on `achievements.json`. Filter by ID/name, edit category/tier/point value/rewards, toggle secret flag.
- `Scripts/Editor/DialogueEditor.cs` — **NEW** — CRUD on `dialogue.json`. Three-axis filter (category + personality + text), vocabulary-listing mode for discovery.
- `Scripts/Editor/BalanceEditor.cs` — **NEW** — Reflection-driven property editor for `BalanceConfig`. Supports int/long/float/double/bool. Fallback message for other types (edit JSON directly).
- `Scripts/Editor/SaveFileManager.cs` — **NEW** — List/clone/delete save files and restore from the editor's automatic `.bak` backups. Opens the save directory in the OS file explorer on Windows.
- `Console/Bootstrap/Program.cs` — New `--editor` / `--usedit` CLI flag launches `EditorMain.RunAsync` and exits with its return code. Alongside the existing `--export-data` flag.
- `Scripts/Core/GameEngine.cs` — New `[G] Game Editor` entry on the main menu (SR + visual), gated on `!DoorMode.IsInDoorMode && !DoorMode.IsMudServerMode && !DoorMode.IsMudRelayMode` so it only shows in local single-player / Steam builds. Handler launches `EditorMain.RunAsync` in-process under a console-state save/restore so control returns cleanly to the animated main menu on exit.
- `Localization/en.json`, `es.json`, `fr.json`, `hu.json`, `it.json` — Two new keys per language: `engine.menu_editor_full` (visual-menu label) and `engine.main_editor` (screen-reader label).
- `DOCS/MODDING.md` — **NEW** — One-page guide to the modding system and editor.
- `usurper-reloaded.csproj` — Added `Spectre.Console` 0.49.1 NuGet reference; used only by the editor code.
- `Scripts/Editor/EditorIO.cs` — Rewritten to use Spectre.Console panels/rules + a custom `RunArrowMenu` controller that handles BOTH arrow-key navigation AND number shortcuts (1-9 immediate, multi-digit with auto-commit when unambiguous, `0`/`Q`/`Esc` to back out, Backspace to edit the number buffer). Fallback path for piped stdin runs a line-based number menu. `PromptChoice` + `PromptEnum` delegate to `RunArrowMenu` so every picker shares identical keyboard UX. New helper `ReadLineWithEchoCompensation` for the Git Bash / mintty case where `Console.ReadLine` doesn't echo Enter back to stdout.
- `Scripts/Editor/EditorVocab.cs` — **NEW** — One source of truth for the editor's controlled vocabularies: NPC personalities (58 built-in labels), alignments, ANSI color names, monster attack types, dialogue categories/personality-types/emotions/contexts/memory-types/event-types, core stat names, combat skill names, and StoryRole faction tags.
- `Scripts/Systems/ClassAbilitySystem.cs` — New public `GetAllAbilities()` accessor so the editor can enumerate every registered ability without reflection.
- `Scripts/Editor/NPCEditor.cs` + `EquipmentEditor.cs` + `MonsterEditor.cs` + `DreamEditor.cs` + `AchievementEditor.cs` + `DialogueEditor.cs` + `BalanceEditor.cs` — Persistence fix: load data ONCE before the menu loop (the initial revision reloaded on every iteration and silently discarded in-memory edits). Added `dirty` flag + `[UNSAVED]` badge + "Reload from disk" item + exit-when-dirty confirmation across all seven. All `Save()` methods now return bool so the caller clears the dirty flag only on successful write. Replaced every "type the exact ID/name" prompt in edit/delete flows with arrow-key pickers over the loaded entries.
- `Scripts/Editor/PlayerSaveEditor.cs` — Player-side picker upgrades: `OldGodType` + `GodStatus` enum pickers for god state, `ArtifactType` enum picker for artifact grant, `CompanionId` enum picker for recruit/dismiss/revive/set-relationship, quest picker for complete/cancel, `ClassAbilitySystem.GetAllAbilities()` picker for ability grant, `AchievementSystem.GetBuiltInAchievements()` picker for grant, granted-only picker for revoke. Sex field is M/F picker. Core stat training and skill proficiency fields picker-backed.
- `Scripts/Core/GameConfig.cs` — Version bumped to 0.57.3.
