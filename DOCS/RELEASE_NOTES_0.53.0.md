# Usurper Reborn v0.53.0 Release Notes

## New Class: Mystic Shaman

A new base class joins the roster -- the **Mystic Shaman**, a tribal melee-caster who summons totems and enchants weapons with elemental power. Inspired by the shamanic traditions of the game's more primal races.

### Class Overview

- **Races**: Troll, Orc, and Gnoll only (race-locked)
- **Armor**: Medium weight class
- **Weapons**: Melee only (swords, axes, maces, mauls -- no bows, no staves)
- **Primary Stats**: Strength (melee damage) and Intelligence (enchantment/totem power)
- **Resource**: Mana (abilities cost mana, no spell list)
- **Role**: Melee hybrid with party utility through totems and self-buffing through weapon enchantments

### Weapon Enchantments (4 abilities)

Temporary elemental buffs applied to your weapon, lasting 5 rounds. Only one enchantment active at a time. Bonus damage scales with Intelligence.

- **Flametongue** (Level 1) -- Fire damage on every melee hit
- **Rockbiter** (Level 10) -- Earth damage + bonus armor (+15% DEF)
- **Frostbrand** (Level 25) -- Ice damage + enemy slow effect
- **Stormstrike** (Level 40) -- Lightning damage + mana restored on each hit

### Totems (5 abilities)

Summoned objects that persist for 3-4 rounds, providing ongoing effects. Only one totem active at a time. Replacing a totem destroys the previous one.

- **Healing Totem** (Level 5) -- Heals all allies for 10% MaxHP each round for 4 rounds
- **Earthbind Totem** (Level 15) -- Reduces enemy attack by 20% for 4 rounds
- **Searing Totem** (Level 30) -- Deals fire damage to enemies each round for 4 rounds
- **Windfury Totem** (Level 50) -- Grants 30% chance for an extra melee attack each round for 4 rounds
- **Spirit Link Totem** (Level 75) -- Redistributes HP among all party members each round for 3 rounds

### Direct Abilities (3 abilities)

- **Lightning Bolt** (Level 20) -- INT-scaled ranged lightning damage
- **Chain Lightning** (Level 60) -- AoE lightning that hits all enemies in multi-monster combat
- **Ancestral Guidance** (Level 90) -- For 4 rounds, 25% of all damage dealt is converted to healing for the party

### Class Passive: Elemental Mastery

+3% elemental damage per Intelligence point, affecting all weapon enchantment bonus damage.

### Starting Equipment

New characters begin with a Tribal Mace (one-handed) and 25 starting mana.

### Per-Level Growth

STR +3, INT +3, DEF +2, CON +2, STA +2, AGI +1, MaxMana +8 per level.

---

## Bug Fixes (from v0.52.14)

### Shield Stat Doubling Fix
Shields had their AC and Block stats doubled on every equip/unequip cycle due to a round-trip corruption in the Item-to-Equipment conversion pipeline. Fixed across 3 conversion paths.

### Companion Loot Displaced Item Loss Fix
When a companion auto-equipped a loot drop, the item they were previously wearing disappeared. Displaced items now transfer to the player's inventory.

### MUD Client Input Fix
Reverted the v0.52.13 input delivery change that caused game freezes and random NPC encounter bleed-through for Mudlet/TinTin++ users. Restored the 500ms grace period.

### Main Street Menu Alignment
Menu columns now align vertically across all rows using a fixed 16-char column width.

### Monk Potion Vendor Labels
Added missing `[H]` and `[M]` labels to the healing and mana potion options in all 5 languages.

### NPC Population Control
Pregnancy hard cap at 120 alive NPCs with steeper fertility tiers. Immigration diversity gating at 80 alive NPCs.

### Stale Team XP Slot Cleanup
Teammate XP distribution slots now zeroed when fighting solo, preventing >100% total after teammate departure.

### Throne Challenge Daily Limit
Players can now only challenge the throne once per day to prevent guard attrition exploits.

### News Board Cap
News file capped at 100 entries with automatic trimming every 50 writes. Server pruned from 150,000+ entries.

### Aldric Quest Boss Fight Fix
Aldric now has a proper scaled HP pool (2000 + level x 50) instead of his tiny BaseStats.HP (~200). Option 2 ("support from behind") is now viable.

### Login Screen Alignment Fix
Online login box right borders fixed (inner lines were 2 chars short).

### MUD Stream Output Flush
Explicit stream flush before blocking on input prevents edge cases where prompt text sits in a TCP buffer.

### Opening Story Skip Hint
MUD/BBS players now see "Press ENTER to skip" instead of "Press SPACE to skip" since line-based input requires Enter.

### Dormitory Stolen Item Fix
Items stolen from sleeping players now actually appear in the attacker's inventory instead of being deleted.

### Dungeon Party Equipment Persistence
Equipment changes on party members in the dungeon now persist immediately via save + SharedState sync.

### Auto-Heal Potion Toggle
The existing auto-heal preference now also controls post-combat automatic potion usage. Label updated to "Auto-use Potions (in & after combat)".

### Equipment Corruption Guard
Duplicate equipment IDs across slots are now prevented in EquipItem() and auto-detected/fixed in RecalculateStats().

### Group Loot Follower Input Fix
Grouped followers can now respond to loot prompts. Input routes through CombatInputChannel instead of racing with the follower loop.

### Quest Title Localization Fix
Bounty quest titles now use hardcoded English "WANTED:" instead of Loc.Get(), preventing Hungarian "KOROZOTT:" from appearing for English players.

### Starting Weapon Registration Fix
New character starting weapons (all classes) were created with hardcoded `Id = 1` but never registered with EquipmentDatabase. The weapon existed in EquippedItems but couldn't be looked up at runtime, appearing as no weapon equipped. Now properly registered via `RegisterDynamic()`.

### Frostbrand Slow Effect
Frostbrand weapon enchant (Mystic Shaman) claimed to "slow the enemy" but had no implementation. Now reduces enemy Defence on each hit, matching the regular frost enchant behavior.

### Healing Totem Party Healing
Healing Totem now heals all alive companions and teammates each round, not just the player.

### Spirit Link Totem Solo Fix
Spirit Link Totem was completely non-functional when fighting solo (no teammates). Now heals the player for 15% MaxHP per round when solo.

### Shaman Armor Shop Visibility
MysticShaman added to all Light and Medium armor templates in LootGenerator so armor appears in shops with `[Sha]` class tag. Added to 35+ armor templates across all equipment slots plus 9 shield templates.

### Equipment Purchase Quests Removed
Equipment purchase quests (Buy Weapon/Armor/Accessory/Shield from the Merchant Guild) were fundamentally broken with procedural shop inventory — quest-referenced items never matched shop-generated items. Removed entirely.

### World Boss Screen Reader Menu
World boss combat menu in screen reader mode now uses vertical list format (`A. Attack`, `C. Cast Spell`, etc.) instead of horizontal bracket format. Number keys 1-9 now trigger class abilities directly.

### Companion Management Screen Reader
Companion detail screen at the Inn now uses `R. Deepen your bond` format instead of `[R] Deepen your bond` for screen reader users, making key associations clear.

### Inn Patron Count Fix
Inn "Talk to patrons" showed mismatched counts — header claimed 15 present but only listed 4, and prompt said "0-8" regardless. Now shows accurate displayed count in header and correct range in prompt.

### Minstrel XP Display
The "Just Listen" option at minstrel encounters now shows the XP gained explicitly (e.g., "+10 XP") instead of just "You feel slightly wiser."

### Home Upgrade Save
All home upgrades (Living Quarters, Bed, Chest, Hearth, Herb Garden, etc.) now trigger an immediate save after purchase to prevent data loss on disconnect.

### Save Cheesing Prevention
Players can no longer disconnect to undo negative outcomes. Immediate auto-saves now fire after: player death (after penalties applied), companion permanent death, NPC teammate death in combat, dark magic assassination, street murder, and royal execution. All saves are online-mode only and fire-and-forget.

### Blood Price Escalation
Murder weight consequences now scale in 3 tiers instead of a flat 20% markup that maxes out at weight 5:
- **Weight 5+ (Known Killer)**: +20% shop prices, NPCs refuse team
- **Weight 8+ (Notorious Killer)**: +50% shop prices, -10% combat damage
- **Weight 15+ (Mass Murderer)**: +100% shop prices (double), -20% combat damage, +50% healer costs
All tiers visible in `/health` Active Buffs with murder weight displayed and "Confess at the Church to reduce" guidance.

### Online Heartbeat Fix
Stale player cleanup threshold increased from 120 seconds to 300 seconds. With heartbeats every 30 seconds and each player running their own cleanup timer, the 2-minute window was too tight — causing active players to appear offline and lose their heartbeat row. 5-minute window gives 10 heartbeats of buffer.

### macOS Steam Support
CI/CD pipeline now builds macOS depots with both Intel (x64) and Apple Silicon (arm64) binaries, universal launcher script, and bundled WezTerm with game icon.

---

## Files Changed

- `Scripts/Core/GameConfig.cs` -- Version 0.53.0 "Ancestral Spirits"; MaxClasses 16->17; MysticShaman starting stats, race restrictions, armor weight, 7 balance constants, ClassHelpText; 8 blood price escalation constants
- `Scripts/Core/NPC.cs` -- Tiered blood price shop markup (20%/50%/100%); price clamp raised to 2.0x
- `Scripts/Systems/SqlSaveBackend.cs` -- Heartbeat stale threshold 120s->300s
- `Scripts/Core/Character.cs` -- `MysticShaman = 16` enum; IsManaClass; ActiveTotemType/Rounds/Power + ShamanEnchantType/Rounds/Power transient combat properties; equipment corruption guard in EquipItem and RecalculateStats; ThroneChallengedToday; shield ConvertEquipmentToItem fix
- `Scripts/Systems/ClassAbilitySystem.cs` -- ManaCost property on ClassAbility; 12 Mystic Shaman abilities; universal ability access
- `Scripts/Systems/CombatEngine.cs` -- Mana cost deduction (3 paths); 13 Shaman special effect handlers (both combat paths); ProcessShamanTotemEffects per-round method; weapon enchant bonus damage on attacks; Earthbind monster debuff; Windfury extra attacks; Frostbrand slow; Ancestral Guidance healing; ability menu mana display; blood price combat penalty; save cheesing prevention (player death, NPC teammate death); shield CreateShield fix; companion loot displaced item transfer; stale XP slot cleanup; group loot follower CombatInputChannel; auto-heal toggle
- `Scripts/Systems/CompanionSystem.cs` -- Save cheesing prevention (companion death saves)
- `Scripts/Systems/StreetEncounterSystem.cs` -- Save cheesing prevention (street murder save)
- `Scripts/Locations/MagicShopLocation.cs` -- Save cheesing prevention (dark magic assassination save)
- `Scripts/Systems/CharacterCreationSystem.cs` -- MysticShaman menu position 11; Tribal Mace starting weapon; mana preview; class description/strengths; ClassName display fix; starting weapon RegisterDynamic fix (all classes)
- `Scripts/Locations/LevelMasterLocation.cs` -- MysticShaman per-level stat growth (STR+3, INT+3, DEF+2, CON+2, STA+2, AGI+1, MaxMana+8)
- `Scripts/Locations/BaseLocation.cs` -- Shaman passive in /health; blood price tier display in /health; shield ConvertInventoryItemToEquipment fix
- `Scripts/Locations/ArenaLocation.cs` -- ClassNames array updated (all 17 classes)
- `Scripts/Locations/CastleLocation.cs` -- ThroneChallengedToday check and set; save cheesing prevention (royal execution save)
- `Scripts/Locations/DungeonLocation.cs` -- Dungeon equip/unequip persistence; Aldric quest HP fix
- `Scripts/Locations/DormitoryLocation.cs` -- Stolen items added to attacker inventory
- `Scripts/Locations/MainStreetLocation.cs` -- Fixed 16-char column width in ShowClassicMenu
- `Scripts/Systems/NPCSpawnSystem.cs` -- MysticShaman in NPC class pool (Troll/Orc/Gnoll)
- `Scripts/Systems/FamilySystem.cs` -- MysticShaman in children class pool (race-gated)
- `Scripts/Systems/LootGenerator.cs` -- MysticShaman added to melee weapon templates + 35 Light/Medium armor templates + 9 shield templates
- `Scripts/Locations/ArmorShopLocation.cs` -- "Sha" class tag abbreviation for MysticShaman
- `Scripts/Locations/WeaponShopLocation.cs` -- "Sha" class tag abbreviation for MysticShaman
- `Scripts/Locations/InnLocation.cs` -- SR companion menu format; patron count/range fix
- `Scripts/Systems/WorldBossSystem.cs` -- SR vertical menu; quickbar number key support
- `Scripts/Systems/RareEncounters.cs` -- Minstrel XP display
- `Scripts/Locations/HomeLocation.cs` -- Immediate save after upgrades
- `Scripts/Systems/WorldSimulator.cs` -- Pregnancy hard cap; immigration gating
- `Scripts/Systems/NewsSystem.cs` -- 100-entry cap with periodic trimming
- `Scripts/Systems/DailySystemManager.cs` -- ThroneChallengedToday reset
- `Scripts/Systems/QuestSystem.cs` -- English-only bounty quest titles; equipment purchase quests disabled
- `Scripts/Systems/OnlineAdminConsole.cs` -- ClassNames updated (17 entries); MysticShaman stat increases for admin level edits
- `Scripts/Systems/SysOpConsoleManager.cs` -- ClassNames updated (17 entries)
- `Scripts/Systems/SpellSystem.cs` -- Default case in ExecuteSpell switch
- `Scripts/Systems/OpeningStorySystem.cs` -- ENTER vs SPACE skip hint
- `Scripts/UI/TerminalEmulator.cs` -- Input delivery 500ms grace period; stream flush before input
- `Scripts/Server/MudServer.cs` -- Login screen alignment fix
- `Scripts/Server/RelayClient.cs` -- Login screen alignment fix
- `web/ssh-proxy.js` -- Class name mapping (16: Mystic Shaman)
- `Localization/en.json` -- ~30 Shaman combat keys; monk vendor labels; skip hint; auto-heal label; class description
- `Localization/es.json` -- Monk vendor labels; skip hint
- `Localization/hu.json` -- Monk vendor labels; skip hint
- `Localization/it.json` -- Monk vendor labels; skip hint
- `Localization/fr.json` -- Monk vendor labels; skip hint
- `.github/workflows/ci-cd.yml` -- macOS Steam depot (x64+arm64); WezTerm icon patch; Localization copy
- `launchers/play-mac.sh` -- Universal macOS launcher (arch detection)
- `launchers/play-mac-accessible.sh` -- macOS accessible launcher
