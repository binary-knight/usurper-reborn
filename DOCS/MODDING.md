# Modding Usurper Reborn

Usurper Reborn ships with a modding system and a standalone editor tool, analogous to the old DOS-era `USEDIT.EXE` companion tool that shipped with the original game. There are two ways to modify game content:

1. **Edit JSON files directly** in the `GameData/` folder next to the executable. Any text editor works.
2. **Run the editor** — `UsurperReborn --editor` (or `--usedit`) launches an interactive menu-driven editor that reads and writes the same files.

Both approaches produce the same result. The editor is a convenience for people who prefer menus over JSON editing.

## What's moddable today

Drop any of these files into `GameData/` next to the game executable. Missing files fall back to built-in defaults. Malformed files log an error and also fall back — the game never crashes on a bad mod.

| File                     | What it controls                                                         |
|--------------------------|--------------------------------------------------------------------------|
| `npcs.json`              | 60 built-in town NPCs (classes, stats, personality, story role)          |
| `monster_families.json`  | 15 monster families × 5 tiers each (HP/STR curves, abilities, loot)      |
| `dreams.json`            | narrative dreams and their trigger conditions                            |
| `achievements.json`      | 79 achievements and their tier rewards                                   |
| `dialogue.json`          | ~500 lines of NPC dialogue keyed by personality                          |
| `balance.json`           | ~30 game-balance constants (crit rate, boss scaling, daily limits…)      |
| `equipment.json`         | custom weapons, armor, shields, accessories (additive, IDs 200000+)      |
| `abilities.json`         | number tuning of existing class abilities, by ability id                 |
| `spells.json`            | number and text tuning of existing spells, by class and spell level      |

The loader reads these nine files and no others.

## Starting a mod

Run `UsurperReborn --export-data` once. This writes the current built-in content to `GameData/*.json` as a starting point. Edit what you like. The next time the game runs, your changes take effect.

(For localizers: `UsurperReborn --export-discoveries [path]` emits the dungeon Discovery localization source keys in English, using the engine's exact key scheme, as a starting point for translating the ~600 `discovery.*` keys.)

If you don't want to bother with every file, you can create just the ones you care about. The game loads each independently and falls back to built-ins for anything missing.

## Tuning abilities and spells

`abilities.json` and `spells.json` replace numbers on built-in entries. An entry names what it changes and sets only the fields it wants to change; every other field keeps its built-in value. `UsurperReborn --export-data` writes every built-in ability and spell with its current numbers as a template: delete the entries you do not change.

- **abilities.json** entries are keyed by `id`. Settable fields: `cooldown`, `staminaCost`, `manaCost`, `levelRequired`, `baseDamage`, `baseHealing`, `defenseBonus`, `attackBonus`, `duration`.
- **spells.json** entries are keyed by `class` and `level` (the spell's slot in that class's spell list). Settable fields: `name`, `description`, `manaCost`, `levelRequired`, `magicWords`.
- Ranges are checked: cooldown 0 to 20, costs 0 to 1000 (spell mana cost 1 to 1000), damage and healing 0 to 100000, bonuses -100000 to 100000, duration 0 to 50, level required 1 to 100.
- If any entry in a file is invalid (unknown id, no spell at that class and level, duplicate key, value out of range), the whole file is rejected, every problem is written to the log, and the built-in values stay. A file is never half applied.

What tuning cannot do today: add a new spell or ability, remove one, change an ability's name, description or effect type, or change what a spell does beyond its mana cost, level and text. There is no editor menu for these two files; edit the JSON by hand.

## Equipment modding

Custom equipment lives in `equipment.json`. Each entry is a full `Equipment` object — name, slot, stats, restrictions. Rules:

- **IDs must be 200,000 or higher.** IDs below 200000 are reserved for built-in items (1000–14999), shop-generated items (50000–99999), and dungeon-generated loot (100000–199999). The game will refuse to load custom items with conflicting IDs.
- **Built-in items stay read-only.** You can't edit or delete the starter equipment (Rusty Dagger, etc.). This preserves save compatibility across game versions — an existing save referencing item #1023 will always find the same item.
- **You can add as many custom items as you want.** They'll appear in the world alongside built-ins once wired into loot tables, shops, or quest rewards.

A minimum example:

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

Full schema: any public property on the `Equipment` class in `Scripts/Core/Items.cs`. All enums (slot, rarity, weapon type, etc.) accept their name in JSON — `"MainHand"`, `"Legendary"`, `"TwoHanded"` and so on.

## Running the editor

```
UsurperReborn --editor
```

Works on Windows, Linux, and macOS. Pure console-mode tool; no graphical terminal needed. The editor has five top-level menus. Equipment, NPCs, Monsters, Dreams, Dialogue and Balance sit under **Game Data / Modding**:

1. **Player Saves** — pick a save, then dive into 11 nested categories covering almost every editable aspect of a character: Character Info (name/class/race/alignment/fame/knighthood), Stats & Progression (level/XP/core attributes/HP/Mana/resurrections/training), Gold & Economy, Inventory & Equipment (add items from the database, remove, equip/unequip any slot, uncurse), Spells & Abilities (learn individually or grant all), Companions (revive, set loyalty/trust/romance, recruit, dismiss), Quests (mark complete, cancel), Achievements (grant/revoke individually or all), Old Gods & Story (per-god status, seals, artifacts, NG+ cycle), Relationships & Family (per-NPC scores, divine wrath cleanup), and Status & Cleanup (cure diseases, clear poison, reset daily counters, release from prison, clear wanted level / murder weight). Every edit stays in memory until explicit Save; on save the file is backed up to `<name>.json.bak` before overwriting.
2. **Game Data / Modding**, a nested menu with one editor per data file:
   - **Equipment**: CRUD on `equipment.json`. Add new items, edit existing custom items, browse built-ins for reference.
   - **NPCs**: CRUD on `npcs.json`. The 60 built-in town NPCs are seeded if no mod file exists yet.
   - **Monsters**: monster families and tiers (`monster_families.json`).
   - **Dreams**: narrative dreams (`dreams.json`).
   - **Achievements**: disabled in the editor so Steam achievements cannot be cheated; `achievements.json` can still be edited by hand.
   - **Dialogue**: NPC dialogue lines (`dialogue.json`).
   - **Balance**: edit any property on `balance.json`. Uses reflection over the config class so newly-added balance properties are editable without editor changes.
3. **Save File Management**: clone, delete, restore saves.
4. **Export Defaults**: write built-in data to `GameData/` as a starting mod template, like the `--export-data` CLI flag. Its confirmation text still says 7 files; the export writes 9.
5. **File Locations**: prints paths for GameData, saves, localization.

## Save compatibility and mods

- If a save references a custom item ID that's no longer in your `equipment.json` (e.g., you deleted a mod item), the game silently drops that reference from the save. Your inventory slot becomes empty for that item; nothing crashes.
- Built-in items are permanent — removing a mod never affects them.
- Modded NPCs that appear in a save reference names, not IDs, so renaming an NPC in a mod effectively removes it from existing saves (the save won't find the renamed NPC on load).
- Balance tweaks apply immediately on next launch. They don't retroactively change existing characters' stats — those values were already applied when the character levelled.

## Concurrency warning

Don't run the editor against a save file while the game is holding it. Save files aren't file-locked, so you could lose writes. Standard practice: exit the game, edit, restart.

For `GameData/*.json` mods, the game only reads them at startup, so editing them mid-game is safe — the changes just don't take effect until the next launch.

## Future phases

Shipped since this section was first written: number tuning of spells and abilities by key (`spells.json`, `abilities.json`), and editor menus for monsters, dreams and dialogue (the achievements menu is deliberately disabled).

Still not built:

- Adding new spells or abilities (only existing ones can be tuned)
- Location descriptions and flavor text
- Quest templates
- World-state editor for online multiplayer (SQLite)
- Editor menus for `abilities.json` and `spells.json`

If any of these block a mod you want to make, open an issue at <https://github.com/binary-knight/usurper-reborn/issues> and we'll bump the priority.
