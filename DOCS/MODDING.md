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
| `monster_families.json`  | 16 monster families × 5 tiers each (HP/STR curves, abilities, loot)      |
| `dreams.json`            | 50 narrative dreams and their trigger conditions                         |
| `achievements.json`      | 75 achievements and their tier rewards                                   |
| `dialogue.json`          | ~500 lines of NPC dialogue keyed by personality                          |
| `balance.json`           | ~30 game-balance constants (crit rate, boss scaling, daily limits…)      |
| `equipment.json`         | **NEW in 0.57.3** — custom weapons, armor, shields, accessories          |

## Starting a mod

Run `UsurperReborn --export-data` once. This writes the current built-in content to `GameData/*.json` as a starting point. Edit what you like. The next time the game runs, your changes take effect.

If you don't want to bother with every file, you can create just the ones you care about. The game loads each independently and falls back to built-ins for anything missing.

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

Works on Windows, Linux, and macOS. Pure console-mode tool; no graphical terminal needed. The editor has six top-level menus:

1. **Player Saves** — pick a save, then dive into 11 nested categories covering almost every editable aspect of a character: Character Info (name/class/race/alignment/fame/knighthood), Stats & Progression (level/XP/core attributes/HP/Mana/resurrections/training), Gold & Economy, Inventory & Equipment (add items from the database, remove, equip/unequip any slot, uncurse), Spells & Abilities (learn individually or grant all), Companions (revive, set loyalty/trust/romance, recruit, dismiss), Quests (mark complete, cancel), Achievements (grant/revoke individually or all), Old Gods & Story (per-god status, seals, artifacts, NG+ cycle), Relationships & Family (per-NPC scores, divine wrath cleanup), and Status & Cleanup (cure diseases, clear poison, reset daily counters, release from prison, clear wanted level / murder weight). Every edit stays in memory until explicit Save; on save the file is backed up to `<name>.json.bak` before overwriting.
2. **Equipment** — CRUD on `equipment.json`. Add new items, edit existing custom items, browse built-ins for reference.
3. **NPCs** — CRUD on `npcs.json`. The 60 built-in town NPCs are seeded if no mod file exists yet.
4. **Balance** — edit any property on `balance.json`. Uses reflection over the config class so newly-added balance properties are editable without editor changes.
5. **Export Defaults** — write all built-in data to `GameData/` as a starting mod template. Same as `--export-data` CLI flag, but from inside the editor.
6. **Open GameData Folder Info** — prints paths for GameData, saves, localization.

## Save compatibility and mods

- If a save references a custom item ID that's no longer in your `equipment.json` (e.g., you deleted a mod item), the game silently drops that reference from the save. Your inventory slot becomes empty for that item; nothing crashes.
- Built-in items are permanent — removing a mod never affects them.
- Modded NPCs that appear in a save reference names, not IDs, so renaming an NPC in a mod effectively removes it from existing saves (the save won't find the renamed NPC on load).
- Balance tweaks apply immediately on next launch. They don't retroactively change existing characters' stats — those values were already applied when the character levelled.

## Concurrency warning

Don't run the editor against a save file while the game is holding it. Save files aren't file-locked, so you could lose writes. Standard practice: exit the game, edit, restart.

For `GameData/*.json` mods, the game only reads them at startup, so editing them mid-game is safe — the changes just don't take effect until the next launch.

## Future phases

Still hardcoded (can't be modded yet) but planned:

- Spells and class abilities
- Location descriptions and flavor text
- Quest templates
- World-state editor for online multiplayer (SQLite)
- Monster editor menu in the GUI editor (the JSON itself already works)
- Dream, achievement, and dialogue editor menus

If any of these block a mod you want to make, open an issue at <https://github.com/binary-knight/usurper-reborn/issues> and we'll bump the priority.
