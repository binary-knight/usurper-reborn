# Usurper Reborn v0.54.0 Release Notes

**Version Name:** The Soul Update

## The Soul Update — Narrative & Immersion

### Vex's Disease Now Claims Him

Vex's core narrative — a dying thief with a bucket list, racing against his own disease — was mechanically broken. `CheckDeathTriggers()` was defined but never called from any game loop. After ~30 in-game days, the disease now actually claims him. Aldric's moral break (turning against a dark player despite high loyalty) also triggers. Death scenes play at natural gameplay moments, once per game day.

### All 15 Awakening Moments Count

8 of 15 spiritual milestones were tracked but never counted toward awakening level: CompanionSacrifice, ForgaveBetrayerMercy, AcceptedGrief, LetGoOfPower, AcceptedDeath, RejectedParadise, AbsorbedDarkness, HeardOldGodLoreSong. All now contribute. The True Ending (Awakening Level 7) is reachable through many different paths of lived experience — not just the original 7 flags. Each new moment has poetic insight text.

### Moments of Silence

Brief blank-screen pauses after the game's most profound moments. The game is about transcending words — now it trusts the player to sit in silence.
- 5 seconds after companion death (between final memory and grief onset)
- 4 seconds after Old God defeat or save
- 6 seconds after True Awakening ending
- 8 seconds after Dissolution ending (before save deletion)
- Skipped in screen reader mode. Forces real ANSI clear in MUD mode.

### 68 Companion Dungeon Idle Comments

Companions now speak during dungeon exploration (~15% chance per room). 13-14 unique lines per companion, context-sensitive to floor depth, player health, post-combat state, and Old God proximity. No repeats within a dungeon visit.
- Aldric notices defensive positions, references his past at Fort Ashwall
- Vex jokes about treasure, makes light of his disease, occasionally drops the mask
- Lyris reads air currents and senses divine presence
- Mira feels suffering in the stones, offers to check wounds
- Melodia hums softly, comments on acoustics, remembers her fallen party

### NPC Overheard Dialogue

When entering locations with 2+ NPCs, ~20% chance to overhear a conversation snippet. Location-specific templates — Inn gossip about dungeon floors, Main Street haggling, Temple prayer and god debates, Healer wound triage. Uses real NPC names from the current location.

### Dynamic Location Flavor

Locations now reflect world state with atmospheric one-liners:
- Blood Moon: red-tinted location descriptions
- Active world boss: distant rumbling
- Recent coup: nervous guards
- NPC death: somber mood
- Time of day: dawn light, afternoon bustle, evening shadows, night quiet

## Gameplay Audit — 17 Bug Fixes

Comprehensive player-journey audit from level 1 to 100 uncovered and fixed 17 bugs across combat, companions, endings, family, multiplayer, and balance.

### High Priority

**Companion sacrifice mechanic restored** — Aldric's core narrative feature (throwing himself in front of a killing blow to save the player) was completely non-functional. The code checked for a "Sacrifice" ability that no companion had in their ability list. Aldric can now always sacrifice when loyalty is sufficient. Other companions can sacrifice at loyalty 90+.

**AmnesiaSystem state now persists across sessions** — The memory fragment recovery system (`RecoveredMemories`, `TruthRevealed`, `RestCount`) was never wired into the save pipeline. All progress reset on every logout, making the Dissolution (secret) ending effectively unreachable. Now serialized alongside other story systems.

### Medium Priority

**Hidden debug combat removed** — Pressing `9` on Main Street triggered a debug "Street Thug" combat encounter with no menu label. Removed.

**MysticShaman death penalty HP fix** — `ReverseClassStatIncrease` was missing the `BaseMaxHP -= 6` for MysticShaman, causing permanent HP inflation through death/relevel cycles.

**Voidreaver NPC stat fix** — `EnsureClassStatsForLevel` still used pre-nerf Voidreaver values (STR 5, INT 5, DEX 4, HP 6, Mana 12). Updated to match the v0.53.4 nerf (STR 4, INT 3, DEX 3, HP 5, Mana 10). Also fixed missing MysticShaman HP in the same method.

**Manwe ending only triggers on victory** — Losing to the final boss (dying in combat) incorrectly triggered the ending cinematic, credits, and NG+ offer. Now requires `Defeated`, `Saved`, or `Allied` outcome.

**Child daily gold bonus now applied** — The `GetChildDailyGoldBonus()` method existed but was never called. Parents now receive +100 gold per child per day as intended.

**Thread-safe random in Wilderness and Settlement** — Both locations used `private static readonly Random _random = new()` which is not thread-safe. Replaced with `Random.Shared` (28 call sites).

**`/tell` and `/group` now resolve character names** — Both commands previously required the target's login username (which other players can't see). Now searches by character display name first, falling back to login username.

**Arena filters dead characters** — Players with HP <= 0 no longer appear as arena opponents. Previously could be fought for free gold/XP.

**Blood Moon uses per-session day counter** — Blood Moon activation was unreliable in multiplayer because it depended on a shared singleton `currentDay` value overwritten by the last player to load. Now uses `GameEngine.SessionCurrentDay`.

### Low Priority

**Prestige/MysticShaman training points** — All 6 classes (Tidesworn, Wavecaller, Cyclebreaker, Abysswarden, Voidreaver, MysticShaman) now receive 1-2 class bonus training points per level instead of 0.

**Manwe objective tracker fix** — Dungeon and Main Street hints checked `manwe_defeated` but the boss system sets `manwe_destroyed`. Objective now correctly clears after defeating or saving Manwe.

**Gambling den bet cap** — Added level-based max bet (`level * 500`) matching the pit fight pattern. Prevents high-gold players from betting millions.

**Lover resurrect guard** — Lovers who died of old age or permadeath no longer appear in the Home resurrect list.

**PvP death penalty order** — Gold theft now applies after the death penalty (not before), so the 25% penalty is calculated on the full gold amount as intended.

## Equipment System Overhaul — 11 Bug Fixes

Comprehensive audit and fix pass across all companion and NPC teammate equipment paths. These fixes address item loss, equipment reversion, and silent data corruption that could occur in online multiplayer mode.

### Critical Fixes

**Inn "Equip Best" item loss** — When using Equip Best on a companion at the Inn, the autosave fired before syncing the character wrapper back to the companion data. If the player disconnected after this save, items were removed from inventory but the companion reverted to old gear. Fixed by syncing companion equipment before every save.

**Dungeon NPC teammate equipment reversion** — When equipping items on NPC teammates (spouses, team members) in the dungeon via [Y] Party, the equipment changes were never synced back to the canonical NPC in the world sim. The next world state reload would overwrite the changes. Fixed by calling `SyncNPCTeammateToActiveNPCs` after equip/unequip for non-companion targets.

**Equipment restrictions lost on save/load** — Six properties were missing from `DynamicEquipmentData` serialization: armor weight class, strength requirements, good/evil alignment restrictions, class restrictions, and uniqueness flag. This meant that after any save/load cycle, a Bard-only instrument could be equipped by a Warrior, and Heavy armor could be worn by a Magician. Fixed across all 3 serialization and 3 deserialization sites.

### Medium Fixes

**Inn companion save throttle** — The 60-second autosave throttle in online mode could silently skip companion equipment saves. Added throttle reset before all companion equipment saves.

**BossSlayer/TitanResolve effects permanently lost** — World boss loot special effects (BossSlayer: +10% damage vs bosses, TitanResolve: +5% defense) were silently dropped when items were equipped. The effects had no field on the Equipment class to store them. Added `HasBossSlayer` and `HasTitanResolve` fields to Equipment and DynamicEquipmentData, wired through all LootEffects conversion paths and serialization sites.

**Missing Waist/Face slots in equipment display** — NPC teammate equipment screens at Team Corner and Home were missing Waist and Face slot displays. Items equipped to those slots were invisible. Added both slots with localized labels in all 5 languages.

### Low-Priority Fixes

**Dungeon equip ordering** — Items were removed from player inventory before the equip attempt. If equip failed, the item was returned via a lossy conversion. Changed to equip-first-remove-second pattern (matching the Inn reference implementation).

**Name-only inventory match** — When removing items from inventory to equip on NPCs, only item name was matched. If the player had two items with the same name but different enchantments, the wrong one could be consumed. Added two-pass matching (name + stats first, name-only fallback).

**Companion death null guard** — If a companion died during an edge case where CurrentPlayer was null (e.g., disconnect), their equipment was silently lost. Added error logging.

**Fire-and-forget saves** — Team Corner and Home NPC equipment saves were fire-and-forget, creating a race window. Changed to awaited saves with error logging.

**Zombie equipment entries** — `UnequipSlot()` in Healer curse removal and Inventory system set equipment IDs to 0 instead of removing entries. Changed to `Remove()`.

## NPC System Overhaul — 10 Bug Fixes

Comprehensive audit and fix pass across the entire NPC lifecycle: death, respawn, persistence, relationships, and world simulation.

### Critical NPC Fixes

**Temporary deaths no longer destroy marriages** — Every NPC death in the world sim (NPC-vs-NPC combat, dungeon runs, king executions) called `HandleSpouseBereavement`, permanently erasing marriages even though the NPC would respawn in 10 minutes. Now bereavement only fires for truly permanent deaths (old age). This was silently destroying marriages across the server on every world sim tick.

**NPC respawn key mismatch fixed** — The respawn dictionary was keyed on `npc.Name` (Name1) but the lookup searched by `n.Name`. If Name1 and Name2 differed, the respawn silently failed and the NPC stayed dead forever. Now keyed on `npc.Id` (CharacterID) with Name fallback.

**Immigration rate increased** — The immigration threshold was raised from 2 to 5 NPCs per race, and the population cap (200) that blocked immigration was removed. Short-lived races (Gnolls, Orcs, Trolls) that die of old age within weeks are now replenished faster.

**Legacy save age migration capped** — NPCs loaded from old saves with `Age = 0` were assigned random ages 18-50. A Gnoll (max age 50) assigned age 48 would die within hours. Now caps assigned age at 60% of the race's max lifespan.

### Online Mode NPC Persistence

**OnlineStateManager serialization gap closed** — The world sim writes NPC state every 5 minutes, but 9 fields were missing from the online serializer compared to the single-player save path. These fields were silently wiped every 5 minutes:
- WorshippedGod, RecentDialogueIds, EmergentRole, RoleStabilityTicks
- SkillProficiencies, SkillTrainingProgress, MarketInventory
- Exhibitionism, Voyeurism personality traits

All now serialized, matching the single-player save path exactly.

**NPC field serialization expanded** — Added 3 previously-unserialized NPC fields to all save/load paths: `IsHostile`, `KnownCharacters` (social knowledge), `GangId`. These previously reset to defaults on every save/load cycle.

**Relationship serialization expanded** — Added 5 previously-unserialized relationship fields: `BannedMarry`, `MarriedTimes`, `Kids`, `KilledBy1`, `KilledBy2`. These previously reset on every save/load.

### Other NPC Fixes

**Street encounters filter player team NPCs** — Player's own NPC teammates and spouse can no longer be selected as hostile street fight opponents.

**Corrupt save NPC handling** — If a save contains no NPC data, the game now logs an error instead of silently leaving the NPC list empty.

### Color Theme Instant Apply

Changed color theme to apply immediately in MUD streaming mode. Previously, changing the color theme in preferences required a relog because MUD mode's `ClearScreen()` is a no-op. Now forces a real ANSI screen clear when exiting preferences.

---

## Files Changed

- `Scripts/Core/GameConfig.cs` — Version 0.53.14
- `Scripts/Core/Items.cs` — Added `HasBossSlayer` and `HasTitanResolve` properties to Equipment
- `Scripts/Core/Character.cs` — BossSlayer/TitanResolve preservation in ConvertEquipmentToItem
- `Scripts/Systems/SaveDataStructures.cs` — 8 new fields on DynamicEquipmentData (WeightClass, StrengthRequired, RequiresGood, RequiresEvil, ClassRestrictions, IsUnique, HasBossSlayer, HasTitanResolve)
- `Scripts/Systems/SaveSystem.cs` — Serialize new DynamicEquipmentData fields (2 sites: player + NPC)
- `Scripts/Systems/OnlineStateManager.cs` — Serialize new DynamicEquipmentData fields for world state
- `Scripts/Core/GameEngine.cs` — Deserialize new DynamicEquipmentData fields (2 sites: player + NPC)
- `Scripts/Systems/WorldSimService.cs` — Deserialize new DynamicEquipmentData fields from world state
- `Scripts/Systems/PlayerCharacterLoader.cs` — Deserialize new DynamicEquipmentData fields for echo characters
- `Scripts/Systems/CombatEngine.cs` — BossSlayer/TitanResolve in LootEffects switch blocks (3 sites)
- `Scripts/Systems/InventorySystem.cs` — BossSlayer/TitanResolve in LootEffects switch blocks (2 sites); zombie ID-0 fix
- `Scripts/Systems/CompanionSystem.cs` — Error logging for companion death with null CurrentPlayer
- `Scripts/Locations/InnLocation.cs` — SyncCompanionEquipment before EquipBest save; throttle reset on both save paths
- `Scripts/Locations/DungeonLocation.cs` — SyncNPCTeammateToActiveNPCs for non-companion targets; equip-first-remove-second ordering
- `Scripts/Locations/TeamCornerLocation.cs` — Awaited SaveAllSharedState; Waist/Face slot display; two-pass inventory match
- `Scripts/Locations/HomeLocation.cs` — Awaited SaveAllSharedState; Waist/Face slot display; localized slot names; two-pass inventory match
- `Scripts/Locations/HealerLocation.cs` — Zombie ID-0 fix (Remove instead of = 0)
- `Scripts/Locations/BaseLocation.cs` — BossSlayer/TitanResolve in LootEffects switch; MUD mode ANSI clear on preferences exit
- `Localization/en.json` — `team.slot_waist` and `team.slot_face` keys
- `Localization/es.json` — Spanish translations for new keys
- `Localization/hu.json` — Hungarian translations for new keys
- `Localization/it.json` — Italian translations for new keys
- `Localization/fr.json` — French translations for new keys

### NPC System
- `Scripts/Systems/WorldSimulator.cs` — Marriage bereavement guarded to permanent deaths only; respawn dictionary keyed by CharacterID; immigration threshold 2→5 and population cap removed; age death path preserved
- `Scripts/Systems/OnlineStateManager.cs` — Added 9 missing NPC serialization fields (WorshippedGod, RecentDialogueIds, EmergentRole, RoleStabilityTicks, SkillProficiencies, SkillTrainingProgress, MarketInventory, Exhibitionism, Voyeurism); added IsHostile, KnownCharacters, GangId
- `Scripts/Systems/RelationshipSystem.cs` — Added 5 missing fields to Export/Import (BannedMarry, MarriedTimes, Kids, KilledBy1, KilledBy2)
- `Scripts/Systems/SaveDataStructures.cs` — Added fields to RelationshipSaveData (5) and NPCData (3: IsHostile, KnownCharacters, GangId)
- `Scripts/Systems/SaveSystem.cs` — Serialize 3 new NPC fields
- `Scripts/Systems/StreetEncounterSystem.cs` — Filter player team NPCs and spouse from hostile encounters
- `Scripts/Systems/WorldSimService.cs` — Legacy age migration capped at 60% of race lifespan; deserialize 3 new NPC fields
- `Scripts/Core/GameEngine.cs` — Legacy age migration cap; deserialize 3 new NPC fields; corrupt save NPC warning

### Gameplay Audit
- `Scripts/Systems/CompanionSystem.cs` — Sacrifice mechanic restored (Aldric always, others at loyalty 90+)
- `Scripts/Systems/AmnesiaSystem.cs` — Serialize/Deserialize methods already existed; now wired into save pipeline
- `Scripts/Systems/SaveSystem.cs` — AmnesiaSystem serialization in SerializeStorySystems/RestoreStorySystems
- `Scripts/Systems/SaveDataStructures.cs` — AmnesiaData property added to StorySystemsData
- `Scripts/Locations/MainStreetLocation.cs` — Removed hidden TestCombat on key "9"; manwe_defeated→manwe_destroyed flag fix
- `Scripts/Locations/LevelMasterLocation.cs` — MysticShaman ReverseClassStatIncrease HP fix; Voidreaver EnsureClassStatsForLevel updated to nerfed values; MysticShaman EnsureClassStatsForLevel HP added
- `Scripts/Locations/DungeonLocation.cs` — Manwe ending only triggers on victory outcomes; manwe_defeated→manwe_destroyed flag fix
- `Scripts/Systems/DailySystemManager.cs` — Child daily gold bonus applied; Blood Moon uses SessionCurrentDay
- `Scripts/Locations/WildernessLocation.cs` — Random.Shared replaces thread-unsafe _random (24 sites)
- `Scripts/Locations/SettlementLocation.cs` — Random.Shared replaces thread-unsafe _random (4 sites)
- `Scripts/Server/MudChatSystem.cs` — FindSessionByNameOrUsername helper; /tell and /group resolve character display names
- `Scripts/Systems/SqlSaveBackend.cs` — Arena opponent query filters HP <= 0
- `Scripts/Systems/TrainingSystem.cs` — Training point class bonuses for 6 missing classes
- `Scripts/Locations/DarkAlleyLocation.cs` — Gambling den level-based bet cap
- `Scripts/Locations/HomeLocation.cs` — Lover resurrect age/permadeath guard
- `Scripts/Locations/ArenaLocation.cs` — PvP death penalty applied before gold theft

### Soul Update
- `Scripts/Locations/BaseLocation.cs` — Vex/Aldric death trigger check in LocationLoop; NPC overheard dialogue; dynamic world state flavor; immersion text hook
- `Scripts/Systems/OceanPhilosophySystem.cs` — All 15 AwakeningMoments integrated into CalculateAwakeningLevel; 8 new insight texts
- `Scripts/Systems/CompanionSystem.cs` — Sacrifice mechanic restored; 68 companion dungeon idle comments (13-14 per companion, context-sensitive); ResetIdleCommentHistory
- `Scripts/Systems/EndingsSystem.cs` — 6-second silence in True Ending; 8-second silence in Dissolution
- `Scripts/Systems/OldGodBossSystem.cs` — 4-second silence after boss defeat and save
- `Scripts/UI/UIHelper.cs` — New MomentOfSilence helper (screen clear + timed pause, SR-safe, MUD-safe)
- `Scripts/Locations/DungeonLocation.cs` — Companion idle comment trigger (15% per room); combat flag tracking
- `Localization/en.json` — 25 new immersion keys (world flavor, overheard dialogue)
- `Localization/es.json` — Spanish translations for immersion keys
- `Localization/hu.json` — Hungarian placeholders for immersion keys
- `Localization/it.json` — Italian placeholders for immersion keys
- `Localization/fr.json` — French placeholders for immersion keys

---

## Balance Pass

Comprehensive balance audit across player scaling, monster scaling, equipment, economy, and class balance.

### Combat Balance

**Monster scaling soft-capped past floor 50** — Monster HP, Strength, and WeaponPower now use a diminishing `effectiveLevel` formula past floor 50: `50 + (level - 50) * 0.6`. At floor 100, monster offense is ~40% lower than before. Monster Defense and ArmorPower are unchanged — monsters are tanky but no longer one-shot players. Floors 1-50 are completely unchanged.

**Artifact weapon soft cap raised** — The WeaponPower soft cap was raised from 800 to 1200. Artifact-tier weapons (~990 WeapPow) now get their full value instead of being diminished. The intended 33% gap between Legendary and Artifact is preserved.

### Economy

**Potion costs scale with level** — Healing potions: 50g flat → `50 + level * 5` (55g at level 1, 300g at level 50, 550g at level 100). Mana potions: same formula with 75g base. Potions are no longer trivially free after floor 5.

**Weapon Reforging** — New `[F] Reforge` option at the Weapon Shop. Rerolls equipped weapon's stat bonuses within +/-15% variance. 20% chance of rarity upgrade (capped at Artifact). Cost: `level² * 50` gold (125K at level 50, 500K at level 100). Provides a meaningful recurring endgame gold sink.

### XP Pacing (Online Mode)

**Session XP diminishing returns** — After earning 50,000 XP in a single online session, XP gains gradually reduce (0.2% per 1000 XP over threshold, floor of 25%). Encourages natural session breaks rather than 5-hour power-grinds. Resets on login. Single-player unaffected (has daily turn limit). Session fatigue shown in `/health` when active.

### Class Balance

**Barbarian** — STR per level reduced from +4 to +3 (matching Warrior). Stamina per level increased from +2 to +3 as compensation, giving the highest stamina pool for ability spam.

**Sage** — Mana per level reduced from +18 to +14. Still the highest mana pool in the game (~8,300 at level 100 vs Cleric's ~4,400), but no longer absurdly double.

**Mystic Shaman** — STR per level reduced from +3 to +2, DEF per level from +2 to +1. Now clearly weaker in pure melee than Warrior, forcing use of totems and enchants. Identity: hybrid that does both melee and casting but neither as well as specialists.

**Shop rarity labels** — Shop items now always labeled Common (level 1-20) or Uncommon (21+). Previously high-level shop items were mislabeled as "Artifact" despite having reduced stats.

### Bug Fixes

**Settlement TrapResist buff** — The Prison trap resistance buff was only checked on riddle/puzzle failures, not on actual dungeon room traps (floor collapse, poison darts, rune explosion). Now applies to all trap types and properly decrements.

**Permadeath news suppressed** — The "will not return" death announcement and `[PERMADEATH]` log entry were firing despite permadeath being disabled (NPCs always respawn in ~10 minutes). Now suppressed.

**XP distribution** — Removed manual XP distribution override. All party members always get even split before modifiers.

### Balance Pass Files Changed

- `Scripts/Systems/MonsterGenerator.cs` — effectiveLevel soft cap on offensive stats past floor 50
- `Scripts/Systems/CombatEngine.cs` — WeapPow soft cap 800→1200; session XP diminishing returns (3 victory paths); even-split XP distribution
- `Scripts/Core/Character.cs` — SessionXPEarned and SessionCombatCount transient properties
- `Scripts/Core/GameConfig.cs` — Potion cost constants; session XP constants; reforge constants
- `Scripts/Locations/HealerLocation.cs` — Level-scaled potion pricing (healing + mana)
- `Scripts/Locations/MagicShopLocation.cs` — Level-scaled mana potion pricing
- `Scripts/Locations/WeaponShopLocation.cs` — Weapon Reforging system (233 lines); [F] menu option
- `Scripts/Locations/LevelMasterLocation.cs` — Barbarian STR 4→3/STA 2→3; Sage Mana 18→14; MysticShaman STR 3→2/DEF 2→1 (Apply/Reverse/Ensure all updated)
- `Scripts/Systems/ShopItemGenerator.cs` — Shop rarity labels fixed (Common/Uncommon only)
- `Scripts/Locations/DungeonLocation.cs` — TrapResist buff applied to all dungeon trap types; buff decrement
- `Scripts/Systems/WorldSimulator.cs` — Permadeath news and log suppressed
- `Scripts/Locations/BaseLocation.cs` — Session fatigue indicator in /health
- `Localization/en.json` — 20 new keys (reforging, trap resist, session fatigue)
- `Localization/es.json` — Session fatigue keys
- `Localization/hu.json` — Session fatigue keys
- `Localization/it.json` — Session fatigue keys
- `Localization/fr.json` — Session fatigue keys

---

## Inn Overhaul

### X-bit the Bartender

New bartender NPC **"X-bit"** — a grizzled ex-adventurer who runs the bar. Talk to him via `[B] Talk to Bartender` for three services:
- **Buy a Drink** — the classic 5g drink with minor stat effects
- **Ask for Rumors** — context-sensitive hints based on game state: Old God progress, companion commentary, spouse questions, recent NPC deaths, or tavern wisdom
- **Ask About Someone** — type an NPC name and X-bit tells you their personality, mood, faction, and who they've been seen with

### Food Variety System

Replaced the old "Order Food" (permanent +5 Stamina for 10g exploit) with 6 dishes offering temporary combat buffs:
- **Hearty Stew** (15g+) — Restores 10% HP
- **Grilled Dragon Steak** (50g+) — +10% damage for 5 combats
- **Elven Honey Bread** (35g+) — +10% defense for 5 combats
- **Dwarven Iron Rations** (40g+) — +15% max HP for 5 combats
- **Mystic Mushroom Soup** (45g+) — +15% spell damage for 5 combats
- **Seth's Mystery Meat** (25g+) — Random effect (any buff above, or food poisoning)

Prices scale with level. One food buff active at a time. Limited to 3 meals per day. Food buffs shown in `/health`.

### Inn Bug Fixes

**Skull & Bones hand display** — Drawn tiles were wiped after each hit due to `playerHand.Clear()` inside the draw loop. Hand now accumulates correctly.

**Seth Able defeats counter persisted** — Lifetime Seth defeats now survive save/load, so the diminishing XP returns system works across sessions.

**Duel counter separated from Seth** — NPC duels at the Inn now use an independent daily counter (3/day) instead of sharing with Seth Able fights.

**Color theme instant apply** — Theme changes in preferences now take effect immediately in MUD streaming mode. Previously required a relog due to AsyncLocal copy-on-write semantics for value types. Fixed by storing theme on SessionContext (matching Language/CompactMode pattern).

### Inn Localization

114 hardcoded English strings converted to `Loc.Get()` keys across: Seth Able combat, gift responses, drinking game, drunk comments, gossip prefixes, companion dialogue, food menu, equipment slots, stat training, gambling tiles, guard names, and bartender dialogue.

### Inn Files Changed
- `Scripts/Locations/InnLocation.cs` — X-bit bartender (rumors, ask-about, drink); food variety system; Skull & Bones fix; Seth counter persistence; separate duel counter; 114 localized strings
- `Scripts/Core/Character.cs` — SethDefeatsTotal, InnDuelsToday, MealsToday, FoodBuffType/Combats/Value
- `Scripts/Systems/SaveDataStructures.cs` — SethDefeatsTotal in PlayerData
- `Scripts/Systems/SaveSystem.cs` — Serialize SethDefeatsTotal
- `Scripts/Core/GameEngine.cs` — Deserialize SethDefeatsTotal
- `Scripts/Systems/DailySystemManager.cs` — Reset InnDuelsToday and MealsToday daily
- `Scripts/Systems/CombatEngine.cs` — Food buff application (attack/defense/HP) and decrement in both combat paths
- `Scripts/Systems/SpellSystem.cs` — Mushroom Soup +15% spell damage
- `Scripts/Locations/BaseLocation.cs` — Food buff in /health Active Buffs
- `Scripts/UI/ColorTheme.cs` — SessionContext storage instead of AsyncLocal
- `Scripts/Server/SessionContext.cs` — ColorTheme property
- `Localization/en.json` — 114 new Inn keys
