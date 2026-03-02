# Usurper Reborn v0.49.0 - Swords and Lutes

**Release Date**: March 2026

## Overview

Players can now SSH into the online game from phones (Termius, Blink, etc.) and small devices (Steam Deck) with a smooth experience. A new **Compact Mode** toggle activates space-efficient menus across all 15+ locations, compact combat displays, and touch-friendly number-key navigation. Compact Mode works alongside Screen Reader Mode for vision-impaired mobile users.

## New Feature: Compact Mode

### What It Does

When enabled, Compact Mode activates the vertically compact menu layout across the entire game — every location, combat screen, and dungeon view switches to a space-efficient format designed for 80x24 terminals. This is the same proven layout used by BBS door mode users, now available to any player via a toggle.

### How to Enable

Three ways to toggle Compact Mode:

1. **Before login**: Press `[Z]` on the main menu (works before loading any save)
2. **In-game preferences**: Press `~` (tilde) from any location, then select `[9]`
3. **Quick command**: Type `/compact` or `/mobile` from any location

The setting persists across sessions — set it once and it stays on.

### Touch-Friendly Number Keys

In Compact Mode on Main Street (single-player/offline), number keys map to common locations:

| Key | Location |
|-----|----------|
| 1 | Healer |
| 2 | Quest Hall |
| 3 | Weapon Shop |
| 4 | Armor Shop |
| 5 | Temple |
| 6 | Castle |
| 7 | Home |
| 8 | Level Master |
| 0 | Quit |

Online mode retains its existing number-key mappings (Who, Chat, News, Arena, Boss).

### Combat in Compact Mode

Combat automatically uses the condensed BBS-style display:
- Screen clears each round so combat fits one page
- Compact status bars instead of full ASCII art boxes
- Numbered action menu instead of wide letter-key grid
- No monster art silhouettes (saves vertical space)
- Combat tips suppressed to save lines

### Independent from Screen Reader Mode

Compact Mode and Screen Reader Mode are independent toggles — enable both for the optimal phone + screen reader experience. Each MUD session maintains its own CompactMode setting (safe for concurrent online players).

## Bug Fix: Two-Handed Weapons Now Drop from Loot

Two-handed weapons (greatswords, greataxes, polearms, mauls, two-handed staves) existed in the equipment database but were completely missing from the loot generation templates — they could never drop from monsters. Added 35 two-handed weapon templates across all 5 weapon categories covering levels 1-100, class-appropriate (Warriors, Barbarians, Paladins for melee; Magicians, Sages for staves). Two-handed weapons offer higher damage than one-handed equivalents at the cost of not being able to use a shield or dual-wield.

## Bug Fix: Shields Now Drop from Loot

Shields (bucklers, standard shields, tower shields) existed in the equipment database with 27 items across 3 categories but were completely absent from the loot generation system — no shield templates existed and `RollArmorSlot()` had no shield slot in its weighted distribution. Added 24 shield templates covering levels 1-100: 6 bucklers (usable by all classes), 9 standard shields (melee and cleric classes), and 9 tower shields (heavy armor classes). Shields now have a 6% drop chance from the armor loot pool, with other slot weights adjusted slightly to compensate.

## UI Fix: Consistent Prompts and Permanent "look" Hint

Several locations (Healer's Hut, Temple, Love Corner, Anchor Road) displayed "press ? for menu" in their prompts, which was confusing because `?` is globally mapped to the slash command help — not a location menu redraw. All prompts now use the standard "Your choice:" format. Invalid choice messages now say "Type 'look' to redraw menu" instead of "Press ? for menu." The "type look to redraw menu" hint on the quick command bar is now shown permanently for all players instead of only for players below level 5, and the duplicate hint in the MUD prompt has been removed.

## Overhaul: Weapon & Armor Shop Inventory

The weapon and armor shops have been completely overhauled with a new procedurally generated inventory system. Previously, shops used a small hand-crafted list of items with huge price/level gaps that made them useless for mid-to-late game players. Now, every dungeon loot template is available for purchase in shops, with prices that scale naturally with the dungeon economy.

### What Changed

- **Full level 1-100 coverage**: Shop items are generated at regular 10-level intervals from every loot template, eliminating gaps. Every class can find relevant gear at every level bracket.
- **Economy-scaled pricing**: Prices are calibrated to cost ~30-45 minutes of dungeon farming at the item's level. A level 25 weapon costs about 37,500 gold — achievable but not trivial.
- **Shop items are slightly weaker than dungeon loot**: Shop gear has 85% of the power of Common-rarity dungeon drops. Even the worst dungeon loot is better than what you can buy, preserving the incentive to explore. Rare, Epic, and Legendary drops are dramatically superior.
- **Interesting item names**: Shops now use the same evocative names as dungeon loot — "Shadow Fang", "Titan Cleaver", "Crown of the Archmage", "Dragonscale Belt", etc.
- **Level-filtered display**: Shops only show items within ±15 levels of your character, keeping the list manageable instead of showing hundreds of items.
- **All equipment slots covered**: Weapons (one-handed, two-handed), shields (bucklers, standard, tower), and all 9 armor slots (head, body, arms, hands, legs, feet, waist, face, cloak) plus rings and necklaces.
- **Save compatibility**: Old equipped items from previous saves are unaffected.

### Price Examples

| Level | Weapon Price | Body Armor | Head Armor | Waist Armor |
|-------|-------------|------------|------------|-------------|
| 1     | 300g        | 300g       | 210g       | 120g        |
| 10    | 9,487g      | 9,487g     | 6,641g     | 3,795g      |
| 25    | 37,500g     | 37,500g    | 26,250g    | 15,000g     |
| 50    | 106,066g    | 106,066g   | 74,246g    | 42,426g     |
| 100   | 300,000g    | 300,000g   | 210,000g   | 120,000g    |

## Bug Fix: Worshipped God Persists After Save Deletion (Online)

In online mode, when a player deleted their save and created a new character with the same name, the god they had previously worshipped at the Temple was still assigned to the new character. The root cause: the `GodSystem` singleton maintains an in-memory `playerGods` dictionary (player name → god name) that persists across the long-running MUD server process. `DeleteSave()` cleared the database record but never removed the player's entry from the in-memory god mapping. Fixed by clearing the player's god worship in `GodSystem` when a save is deleted.

## New Weapon Type: Bows

A complete new weapon category has been added to the game. Bows are two-handed ranged weapons that scale with Dexterity and Agility, with bonus critical hit chance. Ten bow templates cover levels 1-100:

| Level | Weapon |
|-------|--------|
| 1 | Short Bow |
| 5 | Hunting Bow |
| 10 | Longbow |
| 20 | Composite Bow |
| 30 | War Bow |
| 40 | Elven Bow |
| 50 | Shadow Bow |
| 60 | Dragonbone Bow |
| 75 | Celestial Bow |
| 85 | Bow of the Planes |

Bows drop from dungeon monsters as part of the two-handed weapon loot pool and are available for purchase in the weapon shop under a new **Bows** category (key `[3]`). Any class can equip a bow, but Rangers need one for their shot-based abilities.

## Dual-Wield Setup Removed

The `[D] Dual-Wield Setup` option in the weapon shop has been removed. It was redundant — when you buy any one-handed weapon, the game already asks whether you want it in your Main Hand, Off Hand, or to Cancel. The old Dual-Wield Setup also had bugs: it didn't apply alignment-based price modifiers, world event discounts, level filtering, or class restrictions. Just buy a one-handed weapon and choose Off Hand when prompted.

## Assassins Can Now Use Swords

Assassins can now equip Long Swords, Broadswords, and Bastard Swords (previously restricted to Warrior/Paladin classes). This lets Assassins dual-wield a dagger in the main hand (required for blade abilities) with a sword in the off-hand for extra damage. Short Swords were already available to all classes.

## Universal Abilities for Magician & Sage

Magicians and Sages now have access to all 5 universal combat abilities: Second Wind, Battle Focus, Adrenaline Rush, Thundering Roar, and Last Stand. These were previously limited to non-spellcaster classes, leaving Magicians and Sages with no combat options when they couldn't cast spells (silenced, out of mana, or without a Staff equipped). These abilities have no weapon requirement.

## New Feature: Weapon Requirements for Abilities & Spells

Classes now require specific weapons to use certain abilities and spells. When the required weapon isn't equipped, the ability or spell is greyed out in the combat menu with a clear reason shown (e.g., "Requires Bow", "Need Staff"). Players can still see all their abilities — they just can't activate them without the right gear.

### Class Requirements

**Ranger** — Bow required for ranged abilities:
- Precise Shot, Volley, Arrow Storm, Legendary Shot all require a Bow in the main hand
- Non-combat abilities (Hunter's Mark, Evasive Roll, Nature's Blessing, Camouflage, Terravok's Call) work with any weapon
- Ranged Attack (V) also requires a Bow — greyed out without one

**Assassin** — Dagger required for blade abilities:
- Backstab, Poison Blade, Assassinate, Blade Dance, Death Blossom all require a Dagger
- Utility abilities (Shadow Step, Death Mark, Vanish, Noctura's Embrace) work with any weapon

**Warrior / Paladin** — Shield required for Shield Wall:
- Shield Wall requires a Shield, Buckler, or Tower Shield in the off-hand

**Magician / Sage** — Staff required for all spells:
- All spell casting requires a Staff equipped in the main hand
- Without a Staff, the spell menu shows "You need a Staff equipped to cast spells!"

**Cleric** — No weapon requirement:
- Divine magic works through prayer, no specific weapon needed

**Prestige Classes** — No weapon requirements:
- NG+ earned power transcends mundane weapon limitations

### How It Works in Combat

- **Quickbar**: Unavailable abilities show the reason in parentheses — e.g., `Backstab (Requires Dagger)` or `Fireball (Need Staff)` — and are displayed in dark gray
- **Attempting to use**: Pressing the key for an unavailable action shows a specific red error message explaining what's needed
- **PvP AI**: NPC opponents are also subject to weapon requirements — a Magician NPC without a Staff won't cast spells against you
- **Loot drop warnings**: When a weapon drops from a monster that would break your class requirements, a red warning appears — e.g., "NOTE: Your Magician spells require a Staff. Equipping this Sword will prevent spell casting." The item still drops normally; the warning is informational only.

## New Feature: Dungeon Loot Class Restrictions

Dungeon loot weapons and shields now enforce class restrictions from their templates. Previously, any class could equip any dungeon drop — an Assassin finding a Greatsword could equip it even though Greatswords are restricted to Warriors, Barbarians, and Paladins.

### How It Works

When an **identified** weapon or shield drops:
- **Can use**: Normal prompt — `(E)quip / (T)ake / (P)ass`
- **Cannot use**: Red message explaining why (e.g., "Only Warrior, Barbarian, Paladin can equip this weapon.") — `(T)ake / (P)ass` only (no Equip). Player can still take the item to sell later.
- **Unidentified items**: Always `(T)ake / (P)ass` — no class check since you don't know what it is.

Armor and accessories have no class restrictions — any class can use any armor piece.

### Group Loot: Round-Robin & Pass-Down

Loot distribution in groups has been overhauled:

- **Round-robin first dibs**: Each loot drop cycles through group members. Drop 1 goes to player A, drop 2 to player B, drop 3 back to player A, etc. No duplication — each item is offered to exactly one person at a time.
- **Pass-down chain**: When a player passes `(P)`, the item is offered to the next player in the group, then the next, etc. Players always get priority over NPCs.
- **Companion auto-pickup**: After all players have passed, companions/NPCs cycle through the item. Companions who can't use it (class mismatch) are skipped. Companions who can use it but it's not an upgrade are also skipped. Only after ALL companions have been checked does the item get "left behind."
- **`(L)eave` renamed to `(P)ass`**: All loot prompts now use `(P)ass` instead of `(L)eave`. Pressing L still works as a silent alias for backwards compatibility.

## New Weapon Type: Fist

Unarmed weapons (Dragon Fist Wraps, Celestial Hand Wraps, Nunchaku, Bo Staff) were previously classified as Mace weapon type, getting STR/armor-piercing bonuses meant for heavy blunt weapons. They now have their own `Fist` weapon type with appropriate DEX/AGI/crit bonuses matching a fast-strike combat style. Usable by Assassins and Barbarians.

## Bug Fix: "Monk" Class Templates Unreachable

Multiple weapon and armor templates were restricted to a "Monk" class that doesn't exist in the game. Bo Staffs, Nunchaku, Fist Wraps, Gis, Headbands, Sandals, and other items could never drop for any player. All "Monk"-exclusive items have been reassigned to appropriate classes: unarmed weapons to Assassin/Barbarian, light armor (Gis) to Assassin/Barbarian, accessory pieces to Assassin/Ranger/Barbarian as thematically appropriate. Quarterstaff's Monk reference removed (still usable by Magician, Sage, Cleric).

## Overhaul: Traveling Merchant

Both the dungeon and street traveling merchants have been overhauled to be useful throughout the game.

### Dungeon Merchant

- **Level-scaled monster**: Attacking the merchant is no longer trivially easy. The "Frightened Merchant" (level 1, 30 HP, 0 DEF) has been replaced with a "Traveling Merchant" that scales to the dungeon floor — proper HP, STR, DEF, and weapon/armor power.
- **Real loot items**: The merchant now sells actual equipment generated by the loot system — proper weapons, armor, rings, and necklaces with level-appropriate power, rarity, enchantments, and stat bonuses. No more flat "+12 Weapon Power" items at every level.
- **Inventory variety**: 4 items per visit — a weapon, armor piece, random weapon/armor, and a ring or necklace.
- **Dungeon convenience markup**: Items cost 50% more than their dungeon drop value (you're paying for not having to fight for it).
- **Items go to inventory**: Purchased items are added to your inventory for equipping at your convenience, instead of the old system of immediately forcing equip with fallback stat bonuses.

### Street Merchant

- **Level-scaled consumables**: All items now scale with player level. No more 50g healing potions at level 50.
- **Normal merchant**: Healing Potion, Healing Potions (x5) bulk deal, Antidote (actually cures poison now — was a no-op), Fortifying Elixir (heals 20-70% of max HP based on level).
- **Shady merchant**: Poison Vial, Smoke Bomb, Healing Potions (x3), Dark Tonic (heals 30-100% HP but adds Darkness).
- **Item descriptions**: Each item now shows what it does in the purchase menu.

### Banner Alignment

Fixed misaligned right border `║` on the Traveling Merchant, Merchant's Wares, Beggar, and Attack encounter banners.

## Bug Fix: Dungeon Loot Weapon Types

Dungeon loot drops were all being classified as Swords regardless of their actual weapon type. A "Blazing Longbow of the Phoenix" or "Shadow Fang of Venom" would be treated as a Sword when equipped, getting the wrong stat bonuses and handedness. The root cause: loot items are created as generic `Item` objects with no weapon type field, and the conversion to `Equipment` at equip time looked up the item by exact name in the equipment database — which fails for modified loot names (prefixes and suffixes from rarity). The fallback was always `WeaponType.Sword`. Now uses name-based keyword inference to correctly identify the weapon type and handedness from the item name, so a "Blazing Longbow" is correctly typed as a two-handed Bow and a "Shadow Fang" is correctly typed as a one-handed Dagger.

## Bug Fix: Great Axe / Woodcutter's Axe Classified as One-Handed

The weapon type inference system checked for "Greataxe" (no space) to identify two-handed axes, but two loot templates used "Great Axe" (with space) and "Woodcutter's Axe". These were being classified as one-handed Axe instead of Greataxe, giving them incorrect handedness and stats. Fixed by moving the Greataxe check before the Axe catch-all (same pattern used for Daggers before Swords) and adding the spaced variant.

## New Weapon Templates: Bard, Jester & Alchemist

Three classes previously had no dedicated weapon templates — they could only find generic "All"-class weapons (Dagger, Short Sword, Club) from loot. After level 30, these classes had zero class-specific weapon drops. Added 13 new weapon templates:

**Bard** — Musical instruments and performance blades:
- Rapier (Lv 5-40, shared with Jester/Assassin), Dueling Blade (Lv 20-60, shared with Jester), Songblade (Lv 35-80), Virtuoso's Rapier (Lv 60-100)

**Jester** — Trick weapons and gadgets:
- Throwing Knife (Lv 5-35, shared with Assassin), Trick Blade (Lv 20-60), Jester's Scepter (Lv 40-80), Fool's Edge (Lv 65-100)

**Alchemist** — Chemical and enchanted weapons:
- Pestle Club (Lv 1-30), Alchemist's Blade (Lv 15-50), Venom-Etched Dagger (Lv 30-70, shared with Assassin), Transmuter's Staff (Lv 45-85), Philosopher's Edge (Lv 70-100)

## NG+ Prestige Classes: Universal Loot Access

The 5 prestige classes (Tidesworn, Wavecaller, Cyclebreaker, Abysswarden, Voidreaver) earned through completing NG+ endings now bypass all weapon and armor class restrictions for loot drops. Their NG+ power transcends mundane weapon limitations — any weapon, shield, or armor template can drop for prestige classes regardless of the template's class list. This applies to all loot sources: regular monsters, mini-bosses, bosses, and merchants.

## New Weapon Type: Instruments (Bard-Only)

Bards now have their own signature weapon type: **Musical Instruments**. Like Rangers with Bows and Assassins with Daggers, Bards need an Instrument equipped to activate their musical combat abilities.

**10 instrument templates** covering levels 1-100:
- Wooden Flute (Lv 1-25), Travel Lute (Lv 5-35), Silver Lyre (Lv 15-50), War Drum (Lv 25-60), Enchanted Harp (Lv 35-70), Battle Horn (Lv 45-80), Mythril Lute (Lv 55-85), Celestial Harp (Lv 65-95), Songweaver's Opus (Lv 75-100), Instrument of the Spheres (Lv 85-100)

**Instrument bonuses**: Charisma, Wisdom, and at higher levels Mana and HP — thematically reflecting the Bard's performance-based power.

**Abilities requiring Instrument**:
- Inspiring Tune (Lv 10), Song of Rest (Lv 18), Veloura's Serenade (Lv 58), Legend Incarnate (Lv 85)

**Not requiring Instrument**: Vicious Mockery (Lv 1, verbal insult — no instrument needed), Charming Performance (Lv 26, shared with Jester), Grand Finale (Lv 72, shared with Jester). This ensures new Bards can fight from level 1 and shared abilities work for both classes.

**Gameplay tradeoff**: Bards choose between an Instrument (access to musical abilities + CHA/WIS bonuses) or a Rapier/Sword (higher raw damage but no musical abilities). Instruments are one-handed, allowing shield or dual-wield combinations.

**Available from**: The new Music Shop (`[U]` from Main Street) has a dedicated `[B] Buy Instruments` section for Bards. Instruments also drop from dungeon monsters for Bard characters.

**Available from**: Music Shop `[B] Buy Instruments` (Bard-only), dungeon loot drops for Bard characters. Existing loot warnings automatically tell Bards when equipping a non-Instrument weapon would disable their musical abilities.

## New Location: Music Shop

A dedicated Music Shop has been added to Main Street, accessible via `[U]` from the town menu. Run by **Melodia the Songweaver** and her apprentice **Cadence**, the shop provides four services. When Melodia is recruited as a companion, Cadence takes over running the shop with her own personality and dialogue. If Melodia perma-dies in combat, Cadence runs the shop permanently with memorial dialogue honoring her mentor.

### Buy Instruments (Bard-Only)

Instruments have been moved out of the Weapon Shop into the Music Shop where they thematically belong. Bards can browse and purchase level-appropriate instruments here. Non-Bards are politely turned away with a suggestion to try a performance instead.

### Hire a Performance (All Classes)

Any player can hire a bard performance that grants a **temporary combat buff lasting 5 combats**. Only one song buff can be active at a time — a new performance replaces the old one. Bards get a 25% discount on all performances.

| Song | Effect | Base Price |
|------|--------|-----------|
| **War March** | +15% attack damage | 200 + level × 10 |
| **Lullaby of Iron** | +15% defense | 200 + level × 10 |
| **Fortune's Tune** | +25% gold from kills | 300 + level × 15 |
| **Battle Hymn** | +10% attack AND +10% defense | 400 + level × 20 |

Each performance includes a **lore-rich song** — 12 songs total (3 per buff type), mixing tavern humor and dramatic ballads. War March songs tell tales of famous battles (including the legendary Tully who headbutted a siege gate). Lullaby of Iron songs are moving tales of protection and sacrifice. Fortune's Tune songs are whimsical and satirical stories about gold and greed. Battle Hymn songs are epic or deeply personal ballads. A different song is randomly selected each time you hire a performance.

Song buffs work alongside existing herb buffs and well-rested bonuses. They are applied in both single-monster and multi-monster combat, decrement after each fight, and persist across save/load.

### Talk to Melodia / Recruit Melodia (5th Companion)

Use `[T] Talk to Melodia` at the Music Shop to chat with her. She has different dialogue at different level ranges — casual conversation at low levels, deeper philosophical exchanges as you grow stronger, and a recruitment offer at level 20+.

Melodia is the game's 5th recruitable companion — a master bard who joins your party at level 20+. She's a **Support/Hybrid** companion with music-themed abilities: Inspiring Melody (party ATK buff), Soothing Song (heal), Discordant Note (damage + debuff), and Ballad of Heroes (party-wide buff).

Her personal quest, **"The Lost Opus"**, triggers on dungeon floors 50-60. Discover an ancient music chamber containing a legendary composition that captures the essence of the world — with player choices affecting loyalty and Melodia's power growth.

Melodia has full companion features: Inn dialogue, romance availability, permadeath capability, and an Ocean Philosophy awareness of 2 (musically attuned, senses deeper truths).

When recruited, her apprentice **Cadence** takes over shop operations with her own personality ("I may not have Melodia's years of experience, but I know every instrument in this shop"). If Melodia falls permanently in combat, Cadence keeps the shop running as a tribute to her mentor.

### Lore Songs of the Old Gods (All Classes)

Melodia knows ancient ballads about each of the 7 Old Gods. Songs unlock as you encounter, defeat, save, or ally with each god in the dungeon. Each song is 4-8 lines of verse describing the god's nature and fall, displayed with atmospheric delays.

**Listening to a song for the first time grants +1 awakening point** via the Ocean Philosophy system. Subsequent listens are flavor only. Progress is tracked via `HeardLoreSongs` and persists across save/load.

| God | Song | Unlock Condition |
|-----|------|-----------------|
| Maelketh | "The Broken Blade's Lament" | Defeated or Saved |
| Veloura | "Whispers of the Veil" | Defeated or Saved |
| Thorgrim | "The Hammer's Judgment" | Defeated or Saved |
| Noctura | "Shadow's Lullaby" | Defeated, Saved, or Allied |
| Aurelion | "Dawn's Last Light" | Defeated or Saved |
| Terravok | "The Mountain's Memory" | Defeated or Saved |
| Manwe | "The Ocean's Dream" | Defeated |

## Files Changed

- `Scripts/Core/GameConfig.cs` — Version 0.49.0; `CompactMode` AsyncLocal property (same pattern as ScreenReaderMode) for per-session compact mode in MUD server; `ShopPowerMultiplier` and `ShopPriceMultiplier` constants for shop item generation; `MusicShop = 503` in GameLocation enum; song buff constants (`SongBuffDuration`, `SongWarMarchBonus`, `SongIronLullabyBonus`, `SongFortuneBonus`, `SongBattleHymnBonus`)
- `Scripts/Core/Character.cs` — `CompactMode` property added to base Character class (persisted preference for compact menus); song buff properties (`SongBuffType`, `SongBuffCombats`, `SongBuffValue`, `SongBuffValue2`, `HasActiveSongBuff`); `HeardLoreSongs` HashSet for Old God lore song tracking
- `Scripts/Systems/SaveDataStructures.cs` — `CompactMode` field added to PlayerData for save persistence; song buff fields (`SongBuffType`, `SongBuffCombats`, `SongBuffValue`, `SongBuffValue2`); `HeardLoreSongs` list
- `Scripts/Systems/SaveSystem.cs` — CompactMode serialized in `SerializePlayer()`; `DeleteSave()` now clears in-memory GodSystem worship mapping to prevent stale god data on character recreation; song buff fields and HeardLoreSongs serialized
- `Scripts/Core/GameEngine.cs` — CompactMode restored on load and synced to GameConfig; inherited by new characters; pre-login `[Z]` toggle on both main menu variants (BBS/MUD and local/Steam); song buff and HeardLoreSongs restore on load
- `Scripts/Locations/BaseLocation.cs` — `IsBBSSession` expanded to include `GameConfig.CompactMode`; `[9]` toggle in preferences menu; `/compact` and `/mobile` quick commands; help text updated; "type look to redraw menu" hint now permanent on quick command bar (was level < 5 only); removed duplicate hint from MUD prompt
- `Scripts/Locations/HealerLocation.cs` — Replaced "? for menu" prompt with standard "Your choice:"; invalid choice message now says "type 'look' to redraw menu"
- `Scripts/Locations/TempleLocation.cs` — Replaced "? for menu" prompts with standard "Your choice:"; invalid choice message updated
- `Scripts/Locations/LoveCornerLocation.cs` — Replaced "? for menu" prompts with standard "Your choice:"
- `Scripts/Locations/AnchorRoadLocation.cs` — Invalid choice message changed from "Press ? for menu" to "type 'look' to redraw menu"
- `Scripts/Core/EquipmentEnums.cs` — Added `Bow = 15`, `Fist = 16`, and `Instrument = 17` to `WeaponType` enum (two-handed ranged weapon; hand wraps/unarmed; Bard musical instruments one-handed)
- `Scripts/Systems/ClassAbilitySystem.cs` — Added `RequiredWeaponTypes` and `RequiresShield` fields to `ClassAbility`; weapon/shield checks in `CanUseAbility()`; new `GetWeaponRequirementReason()` method; bow requirement on 4 Ranger abilities (precise_shot, volley, arrow_storm, legendary_shot), dagger requirement on 5 Assassin abilities (backstab, poison_blade, assassinate, blade_dance, death_blossom), shield requirement on shield_wall; instrument requirement on 4 Bard-exclusive abilities (inspiring_tune, song_of_rest, veloura_serenade, legend_incarnate); all 5 universal abilities now include Magician and Sage in their class lists
- `Scripts/Systems/SpellSystem.cs` — New `GetSpellWeaponRequirement()` (Magician/Sage → Staff), `HasRequiredSpellWeapon()` method; weapon check integrated into `CanCastSpell()`
- `Scripts/Systems/CombatEngine.cs` — All 9 `DoorMode.IsInDoorMode` branch points updated to also check `GameConfig.CompactMode` (compact combat status, compact menus, screen clear per round, monster art suppression, combat tip suppression); weapon requirement display in `GetQuickbarActions()` (abilities show "Requires X", spells show "Need Staff"); weapon-specific error messages in both `HandleQuickbarAction()` and `HandleQuickbarActionSingleMonster()`; bow check in `ExecuteRangedAttack()` and `ExecuteRangedAttackMultiMonster()`; Ranged Attack V menu greyed out without bow; spell weapon check in `ProcessSpellCasting()` and `ExecuteSpellMultiMonster()`; dungeon loot weapon type inference fix (falls back to `ShopItemGenerator.InferWeaponType()` instead of defaulting to Sword); loot drop weapon-requirement warnings for Magician/Sage (Staff) and Ranger/Assassin (Bow/Dagger); `_lootRoundRobinIndex` field for group loot cycling; `CheckForEquipmentDrop()` builds player list and assigns round-robin recipient per drop; group dice-roll loot replaced with round-robin flow (follower gets prompt on RemoteTerminal with 30s timeout); `OfferLootToOtherPlayers()` helper for pass-down chain; `DisplayEquipmentDrop()` class restriction check via `LootGenerator.CanClassUseLootItem()` — red message + T/P only when can't use, normal E/T/P when can; `(L)eave` renamed to `(P)ass` everywhere with L alias; `TryTeammatePickupItem()` now skips grouped players (already had their chance) and checks class restrictions before upgrade comparison; `PromptLootWinner()` updated from L to P
- `Scripts/Locations/MainStreetLocation.cs` — Number-key aliases (3-8, 0) for compact mode in single-player; numpad hint line in BBS display
- `Scripts/Systems/LootGenerator.cs` — Added 35 two-handed weapon templates (greatswords, greataxes, staves, polearms, mauls) covering levels 1-100; added 10 bow templates (Short Bow through Bow of the Planes) covering levels 1-100; added 10 instrument templates (Wooden Flute through Instrument of the Spheres) Bard-only covering levels 1-100; added 24 shield templates (bucklers, standard shields, tower shields) covering levels 1-100; added `ObjType.Shield` to `RollArmorSlot()` at 6% weight; two-handed weapons, bows, instruments, and shields can now drop from monsters; Assassin added to Long Sword, Broadsword, Bastard Sword class lists (dual-wield builds); internal template accessor methods for ShopItemGenerator; new `CanClassUseLootItem()` public static method — checks weapon/shield loot items against template class restrictions via longest-name-first matching; all "Monk" class references removed and reassigned to existing classes (Assassin, Barbarian, Ranger) — Monk-exclusive weapons/armor were unreachable since there is no Monk class; renamed "Monk's Headband" → "Fighter's Headband", "Monk's Sandals" → "Traveler's Sandals"; 13 new weapon templates for Bard (Rapier, Dueling Blade, Songblade, Virtuoso's Rapier), Jester (Throwing Knife, Trick Blade, Jester's Scepter, Fool's Edge), and Alchemist (Pestle Club, Alchemist's Blade, Venom-Etched Dagger, Transmuter's Staff, Philosopher's Edge); prestige class bypass in `CanClassUseLootItem()`, `GenerateWeapon()`, `GenerateArmor()`, and `GenerateArmorWithRarity()` — NG+ classes (Tidesworn, Wavecaller, Cyclebreaker, Abysswarden, Voidreaver) can use any weapon/shield/armor
- `Scripts/Systems/ShopItemGenerator.cs` — **NEW** — Procedural shop inventory generator: converts every loot template into shop Equipment items at 10-level intervals; 85% power of Common dungeon loot; economy-scaled pricing (~30-45 min farming per item); rarity tiers by level; class restrictions from templates; public `InferWeaponType()` for name-based weapon classification (used by both shops and dungeon loot conversion); public `InferHandedness()` for weapon type to handedness mapping; `InferShieldType()` for buckler/tower shield detection; Greataxe check moved before Axe to handle "Great Axe" (with space) and "Woodcutter's Axe"; bow detection with case-insensitive matching for "Longbow"; Fist weapon type for hand wraps (Wraps, Fist, Nunchaku, Bo Staff) with DEX/AGI/crit bonuses; Instrument weapon type detection (Flute, Lute, Lyre, Drum, Harp, Horn, Opus) with CHA/WIS/Mana/HP bonuses
- `Scripts/Data/EquipmentData.cs` — Shop-generated item ID range (50000-99999); `GetShopWeapons()`, `GetShopArmor()`, `GetShopShields()` query methods for shop display; `Initialize()` calls `ShopItemGenerator.GenerateAllShopItems()`
- `Scripts/Locations/WeaponShopLocation.cs` — All shop inventory queries switched from legacy `GetWeaponsByHandedness()`/`GetShields()` to new `GetShopWeapons()`/`GetShopShields()` methods; level-range filtering (player ±15 levels); `GetShopItemsForCategory()` helper; new Bows category (key 3), Shields shifted to key 4; Instruments category removed (moved to Music Shop); one-handed category excludes instruments; class restriction tags on item display; purchase blocked for wrong class; `[D] Dual-Wield Setup` removed entirely (~115 lines) — redundant with existing main/off hand prompt on 1h weapon purchase, and had pricing/filtering bugs
- `Scripts/Locations/ArmorShopLocation.cs` — All shop inventory queries switched from legacy `GetBySlot()` to new `GetShopArmor()` method; level-range filtering (player ±15 levels); `GetShopArmorForSlot()` helper; class restriction tags on item display; purchase blocked for wrong class
- `Scripts/Locations/DungeonLocation.cs` — Dungeon traveling merchant overhaul: `CreateMerchantMonster()` now scales to dungeon level (was hardcoded level 1, 30 HP); `GenerateMerchantRareItems()` rewritten to use `LootGenerator` for real weapon/armor/ring/necklace items instead of flat stat bonuses; `PurchaseRareItem()` adds items to inventory with stats tracking; removed `PromptForWeaponSlotDungeon()` (unused); fixed banner alignment on "TRAVELING MERCHANT" and "MERCHANT'S WARES" boxes; `FormatItemBonuses()` and `FormatAccessoryStats()` helpers for item display
- `Scripts/Systems/StreetEncounterSystem.cs` — Street merchant overhaul: `GenerateMerchantItems()` rewritten with level-scaled consumables — normal merchant (Healing Potion, bulk x5, Antidote, Fortifying Elixir) and shady merchant (Poison Vial, Smoke Bomb, bulk x3, Dark Tonic); `ApplyMerchantItem()` now implements all item effects (Antidote was a no-op, most items had no effect); item descriptions added to `MerchantItem` struct and shown in buy menu; purchase statistics tracking; fixed banner alignment on "TRAVELING MERCHANT", "BEGGAR", and "ATTACK" boxes
- `Tests/SpellSystemTests.cs` — Updated `CreateTestMage()` to equip a Staff (required for Magician spell casting after weapon requirement addition)
- `Scripts/Locations/MusicShopLocation.cs` — **NEW** — Music Shop location with 4 services: instrument purchasing (Bard-only, paginated display with level/price filtering), performance buffs (4 songs with level-scaled pricing, Bard discount), Melodia companion recruitment (level 20+ dialogue scene), Old God lore songs (7 ballads gated on story progression with awakening rewards)
- `Scripts/Systems/CompanionSystem.cs` — Added `Melodia` to `CompanionId` enum (value 4); full companion initialization (Support/Hybrid, "The Songweaver", 4 abilities, personal quest "The Lost Opus" on floors 50-60, romance available, permadeath capable, OceanPhilosophyAwareness 2)
- `Scripts/Locations/InnLocation.cs` — Melodia dialogue in both loyalty tiers (high: adventure verse, medium: rhythm observation)
- `Scripts/Systems/LocationManager.cs` — MusicShop registered in `InitializeLocations()`; navigation table entries (MainStreet ↔ MusicShop)
- `Scripts/Locations/MainStreetLocation.cs` — `[U] Music Shop` menu option in both standard and BBS compact displays; `case "U"` key handler
- `Scripts/Systems/CombatEngine.cs` — Song buff application in single-monster damage (War March +15% ATK, Battle Hymn +10% ATK), defense (Lullaby of Iron +15% DEF, Battle Hymn +10% DEF), gold (Fortune's Tune +25%), and multi-monster equivalents; song buff decrement and clear after each combat alongside herb buffs
- `Scripts/Systems/OceanPhilosophySystem.cs` — Added `HeardOldGodLoreSong` to `AwakeningMoment` enum
- `Scripts/Locations/DungeonLocation.cs` — Added `CheckMelodiaQuestEncounter()` for "The Lost Opus" personal quest (floors 50-60, 15% chance per room, 3 player choices affecting loyalty and Melodia's stats); `CompanionId.Melodia` added to quest dispatch switch
