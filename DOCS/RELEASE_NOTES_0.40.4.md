# Usurper Reborn v0.40.4 — Enchantments, BBS Online & Dashboard

## Major Fix: Loot Enchantments Now Actually Work

Dungeon loot items with enchantment names like "Siphoning Club", "Blazing Sword of Flames", "Frozen Shield of Frost", etc. were **completely cosmetic** — the named effects never triggered in combat. A "Siphoning Club" was just a club with a small HP bonus. A "Sword of Frost" was just a sword with extra attack power. None of the elemental procs, lifesteal, mana steal, or other effects actually worked.

**Root Cause:** The LootGenerator created items with enchantment names and flat stat bonuses, but when converting loot `Item` objects to equippable `Equipment` objects, only basic stats (STR, DEX, WIS, HP, Mana, Defence) were transferred. The `Equipment` class had properties for fire/frost enchants and lifesteal that combat *did* check, but they were never set on loot-generated equipment.

**What's Fixed:**
- Loot items now track their actual enchantment types (stored on the Item during generation)
- When equipping loot, enchantment effects are properly transferred to Equipment properties
- All enchantment types now have functional combat procs:

### Offensive Enchantments (Weapon Procs)
| Enchantment | Effect | Proc Chance |
|---|---|---|
| **Fire Damage** ("Blazing", "of Flames") | 15% of damage as bonus fire damage | 20% |
| **Ice Damage** ("Frozen", "of Frost") | Reduces enemy defence | 15% |
| **Lightning** ("Shocking", "of Thunder") | 12% bonus damage + stun | 15% |
| **Poison** ("Venomous", "of Venom") | Poison damage on hit | 20% |
| **Holy** ("Holy", "of Light") | 20% bonus damage, 2x vs undead | 25% |
| **Shadow** ("Shadow", "of Darkness") | 15% bonus shadow damage | 20% |
| **Life Steal** ("Vampiric", "of the Leech") | % of damage healed as HP | Always |
| **Mana Steal** ("Siphoning", "of Sorcery") | % of damage restored as mana | Always |
| **Critical Strike** ("Keen", "of Precision") | +% critical hit chance | Passive |
| **Critical Damage** ("Deadly", "of Devastation") | +% critical hit damage | Passive |
| **Armor Piercing** ("Piercing", "of Penetration") | Ignores % of enemy armor | Passive |

### Defensive Enchantments (Armor Procs)
| Enchantment | Effect |
|---|---|
| **Thorns** ("Spiked", "of Retaliation") | Reflects % of damage back to attacker |
| **HP Regen** ("Regenerating", "of Healing") | Restores HP each combat round |
| **Mana Regen** ("Mystical", "of the Arcane") | Restores mana each combat round |
| **Magic Resist** ("Warded", "of Shielding") | Reduces magic damage taken |

### Notes
- Existing loot items already in player inventories will NOT retroactively gain enchantments (only newly dropped items)
- Enchantments on equipment are properly saved/loaded across game sessions
- All enchantment procs work in both single-monster and multi-monster combat
- Holy enchantment does 2x damage against undead monsters (Skeleton, Zombie, Ghost, Lich, Wraith, Vampire, Revenant)

---

## Balance Fix: Power-Based Equipment Level Requirements

High-powered equipment could end up in low-level players' hands through champion mob drops or the Auction House. A level 10 player could equip gear with 360 armor class, making them nearly invincible. This happened because `GenerateMiniBossLoot()` uses the monster's level (not the dungeon floor), and champion mobs can spawn well above the player's level range.

**Fix: Three layers of protection**

1. **Power-Based MinLevel**: All equipment now has a minimum level requirement based on its raw power. Formula: `MinLevel = max(existing MinLevel, power / 10)` where power is the greater of WeaponPower or ArmorClass. A 360 AC armor piece now requires level 36 minimum.

2. **Auction House Check**: Players can no longer purchase equipment from the Auction House that exceeds their level. A clear error message shows the required level.

3. **Login Validation**: On login, any equipped gear that exceeds the player's level is automatically unequipped and returned to inventory. This retroactively fixes existing characters who already have overpowered gear equipped.

| Power Range | Required Level |
|-------------|---------------|
| 1-15 | Level 1 (no restriction) |
| 16-50 | Level 2-5 |
| 51-100 | Level 6-10 |
| 101-200 | Level 11-20 |
| 201-360 | Level 21-36 |
| 361-500 | Level 37-50 |

---

## New Feature: BBS Online Mode (Shared World)

BBS SysOps can now run Usurper Reborn as a **shared multiplayer world** where all callers share the same NPCs, economy, king, chat, PvP arena, and news feed — just by adding `--online` to their door command.

### Setup is two commands:

**Door command:**
```
UsurperReborn --online --door32 %f
```

**World simulator (background service):**
```
UsurperReborn --worldsim
```

The database (`usurper_online.db`) is created automatically next to the executable. No manual database setup needed.

### What activates:
- **Shared NPCs** — All 60 NPCs shared across players with persistent relationships, marriages, deaths
- **Shared Economy** — King, treasury, taxes affect everyone
- **Chat** — `/say`, `/tell`, `/who`, `/news` commands
- **PvP Arena** — Challenge other players (level 5+)
- **News Feed** — Real-time world events
- **Living World** — NPCs age, marry, have children 24/7 via the world simulator
- **While You Were Gone** — Login summary of events since last session

### Authentication:
BBS Online mode trusts the BBS's own authentication. The player name from DOOR32.SYS becomes their online identity — no extra password needed.

See `DOCS/BBS_DOOR_SETUP.md` for full setup instructions including systemd and Windows service configuration.

---

## New Feature: Relationship Network Overhaul (Web Dashboard)

The NPC relationship network graph on the web dashboard has been completely rebuilt from a blob of indistinguishable dots into a useful analysis tool.

### Visual Improvements
- Graph area expanded from 340px to 500px with wider node spacing
- All nodes labeled (not just high-level ones)
- Node size scales with level (`4 + level/4` radius)
- Dead nodes dimmed to 10% opacity

### New Link Types & Styling
- **Family links** (gold dashed) connecting parents to children from the children database
- **Team star clusters** — team members link to the highest-level leader instead of chaining A→B→C
- Marriage links brighter (2.5px red), rival links dashed orange, ally links solid green
- All links have 0.3 minimum opacity for visibility

### New Interactive Features
- **[Family] filter button** alongside All/Marriages/Teams/Allies/Rivals
- **Search input** — type an NPC name to find and center the view on them
- **Ego network mode** — click any node to isolate it + direct connections only, with relationship strength labels. Click "Show All" to exit
- **Color legend** showing what each link color means

### Enhanced Tooltips
- Now shows: class, race, team, relationship count, and top 3 strongest relationships with color-coded values

---

## Files Changed

| File | Changes |
|------|---------|
| `Scripts/Core/Items.cs` | Added `LootEffects` list to Item for tracking enchantment types; added 9 new Equipment properties; added `CalculateMinLevelFromPower()` and `EnforceMinLevelFromPower()` for power-based level requirements |
| `Scripts/Systems/LootGenerator.cs` | `ApplyEffectsToItem()` now stores effects in Item.LootEffects for later transfer to Equipment |
| `Scripts/Systems/CombatEngine.cs` | Loot-to-equipment conversion transfers all enchantment effects + enforces power-based MinLevel; expanded `CheckElementalEnchantProcs()` and `CheckElementalEnchantProcsMonster()` with lightning/poison/holy/shadow/mana-steal procs; added thorns reflection on monster attack; added HP/Mana regen in `ProcessEndOfRoundAbilityEffects()`; added armor piercing to defense calculations in both single and multi-monster combat |
| `Scripts/Core/Character.cs` | Added helper methods: `GetEquipmentManaSteal()`, `GetEquipmentArmorPiercing()`, `GetEquipmentThorns()`, `GetEquipmentHPRegen()`, `GetEquipmentManaRegen()` |
| `Scripts/Core/GameConfig.cs` | Version 0.40.4; updated version name; added constants for lightning/poison/holy/shadow enchant proc chances and damage multipliers |
| `Scripts/Systems/SaveDataStructures.cs` | Added all new enchantment properties to `DynamicEquipmentData` for save persistence |
| `Scripts/Systems/SaveSystem.cs` | Serialize/restore all new enchantment properties (2 sites) |
| `Scripts/Core/GameEngine.cs` | Restore all new enchantment properties (2 sites); login equipment validation auto-unequips overleveled gear |
| `Scripts/Systems/WorldSimService.cs` | Restore all new enchantment properties for NPC equipment |
| `Scripts/Locations/BaseLocation.cs` | Auction House level check prevents buying equipment above player level |
| `Scripts/BBS/DoorMode.cs` | `--online` + `--door32` coexistence for BBS Online mode; smart default database path (next to executable); updated help text with BBS Online examples |
| `Console/Bootstrap/Program.cs` | BBS Online auth bypass (uses BBS username from drop file); connection type detection returns "BBS" for BBS Online sessions |
| `DOCS/BBS_DOOR_SETUP.md` | New "BBS Online Mode (Shared World)" section with setup instructions, world simulator service config (Linux systemd + Windows NSSM), troubleshooting |
| `web/dashboard.html` | Relationship network overhaul: bigger graph (500px), family links from children data, team star clusters, ego network mode, NPC search, color legend, enhanced tooltips, better link visibility and styling |
