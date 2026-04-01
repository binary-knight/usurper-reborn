# Usurper Reborn v0.53.11 Release Notes

**Version Name:** Ancestral Spirits (Murder, NPC Survival & Bug Fixes)

## Murder is Now a Capital Offense

Non-bounty NPC murders now carry severe consequences enforced by the Crown:

**Capital offense warning** — Before attacking a non-bounty NPC, players receive a detailed multi-line warning explaining the consequences. They must press Y and then type "MURDER" to confirm. Bounty Board kills, Crown bounties, Shadow faction contracts, royal executions, and duelist encounters are all exempt.

**50% chance of execution** — If the murder succeeds, the player faces a coin flip: execution permanently deletes the character from the database (online and offline). A server-wide news broadcast announces the execution.

**50% chance of imprisonment** — If spared, the player is sentenced to 3 IRL days in maximum security prison. ALL equipment is stripped, inventory cleared, gold and bank account seized. No escape attempts, bail, cell door opening, or demands are available to murder convicts.

**Maximum security prison** — Murder convicts have a new `IsMurderConvict` flag that blocks all prison escape mechanics: escape attempts ("You are in MAXIMUM SECURITY for murder"), bail ("Murder convicts are not eligible for bail"), cell door ("Sealed with enchanted locks"), and demand release ("Murderers don't make demands"). The flag persists through saves/logouts and clears automatically when the sentence expires.

**Failed murder attempts** — Even if the player loses the fight, they are arrested and imprisoned for 1 day for attempted murder.

**Dark arts kills** — Magic Shop death spell assassinations now carry the same consequences as street murder.

**Character deletion** — Execution uses a database transaction to delete from `players`, `online_players`, `sleeping_players`, and `guild_members` tables in one operation. Also matches by display name (case-insensitive) for safety.

## NPC Permadeath Disabled

NPC permadeath has been completely disabled. Dead NPCs will now respawn through the world simulator's natural respawn cycle instead of being permanently removed from the game.

**Why:** Player-driven NPC murders were draining the server population — all 60 NPCs had been reduced to low-level replacements (avg level 10 vs player avg level 60+). The immigration system couldn't keep up, and fresh immigrants started at level 5.

**What changed:** The `IsPermaDead = true` flag is no longer set in any of the four kill paths:
- Street murder (StreetEncounterSystem)
- Dark arts assassination (MagicShopLocation)
- Royal execution (CastleLocation)
- World sim NPC-on-NPC violence (WorldSimulator)

Dead NPCs will now be respawned by the world sim with their original stats, relationships, and memories intact (minus the death state).

## Player Team NPC Protection

NPCs on player teams can no longer be removed by autonomous game systems:

**Throne challenge blocked** — NPCs on player teams no longer leave to challenge for the throne. Previously, an ambitious NPC (ambition >= 0.8) could autonomously leave a player's team to try to become king, frustrating players who invested time in leveling and equipping them.

**Unpaid wages disabled** — NPCs no longer leave player teams due to unpaid wages. The wage system was causing frustration for players who spent time building their teams. Player-team NPCs are now permanent — they can only be removed by the player manually sacking them.

## Team XP Redistribution Fix

Fixed XP distribution when a teammate dies mid-combat. Previously, `AutoDistributeTeamXP` counted dead teammates as eligible XP recipients, wasting their share. For example, with 4 teammates and 1 dead: the split was 20/20/20/20/20 (player gets 20%), but the dead teammate's 20% was lost. Now only alive teammates (`IsAlive && HP > 0`) are counted, so the split becomes 25/25/25/25 (player + 3 survivors). If all teammates die, player gets 100%.

## Bard Charm Resist Message

Charming Performance (Bard/Jester ability) had a 60% failure rate that produced zero feedback — the ability fired but nothing happened and no text appeared. Now shows "X resists the charm!" on failure in both single-monster and multi-monster combat paths.

## Searing Totem AoE Fix

Three fixes to the Mystic Shaman's Searing Totem:

**Now AoE** — Searing Totem previously only damaged the first living monster. Now damages ALL living monsters each round, matching the ability description ("blasts enemies for fire damage").

**Applies burn** — Searing Totem hits now set `IsBurning = true` on affected monsters, enabling fire DoT damage.

**NPC teammate totems tick** — Totem per-round effects (`ProcessShamanTotemEffects`) were only processed for the player, never for NPC Shaman teammates. NPC teammate totems now tick each combat round.

## Companion Loot Equip Fix

Fixed the companion equipment flow in `InnLocation.CompanionEquipItemToCharacter()`:

**Equip-first, remove-second** — Previously, the source item was removed from the player's inventory BEFORE attempting to equip it to the companion. If equip failed (level/class/weight restriction), a new converted item was created and returned — but this conversion could produce subtle differences, causing item duplication or loss. Now the equip is attempted first; the source item is only removed on success. On failure, nothing is touched.

**Better item matching** — Inventory removal now matches by name + stats (attack/defense) instead of just name, preventing wrong-item removal when the player has multiple items with the same name.

## Curse Detection Fix (Ravanella)

Ravanella's curse removal service now checks three sources for cursed items:
1. Player **backpack** (was the only check before)
2. Player **equipped gear** (NEW — was completely missing)
3. Companion/teammate equipment (already worked)

Previously, a cursed item equipped on the player would not be detected — Ravanella would say "no cursed items found" even though the healer could see it. The display now groups items clearly: "Cursed items in backpack", "Cursed gear you are wearing", "Cursed gear on team members".

## Music Shop Instrument Level Fix

Instruments can now be purchased for companions regardless of the player's level. Previously, `PurchaseInstrument()` checked the player's level against the instrument's `MinLevel`, blocking purchases for higher-level companions (e.g., Melodia at level 25 when the player is level 21). The level check has been removed since instruments are companion items, and the shop display range widened from +15 to +30 levels above the player.

## Electron Client: Darkest Dungeon-Style Combat UI

Major graphical overhaul for the Electron desktop client:

**HD Sprites** — 32 new 192px transparent-background sprites generated via PixelLab Pixflux API (17 player classes + 15 monster families). Fallback chain: HD → east-facing → default → placeholder.

**DD-style combat** — Characters anchored at bottom with overlapping formation, large sprites (220px default, 280px bosses), dynamic density scaling for 3-5 combatants per side, dark combat vignette, torch flicker animation.

**Ability bar** — Bottom panel with character portrait, mini HP/MP/ST bars, and skill slot grid replacing text buttons.

**Combat log** — Scrolling text panel on the right side showing damage/heal/buff events with color coding.

**Graphical overlays** — Dungeon Map (canvas-based room grid with corridor lines), Inventory (paperdoll with equipment slots + backpack grid), Character Status (portrait + stats + attributes), Party Management (member cards with portraits), Potions Menu (HP bars + action buttons), Dungeon Room Info (features, exits, all action buttons).

**Sprite generator** — `electron-client/generate-sprites.js` with `--force`, `--classes`, `--monsters`, `--name=X` flags. 192px is the sweet spot for transparent backgrounds.

---

## Files Changed

- `Scripts/Core/GameConfig.cs` — Version 0.53.11
- `Scripts/Core/Character.cs` — Added `IsMurderConvict` property (maximum security flag)
- `Scripts/Locations/BaseLocation.cs` — Complete rewrite of `AttackNPC()` with capital offense warning, double confirmation (Y + type "MURDER"), bounty target check; new `ApplyMurderConsequences()` method with 50/50 execution/prison logic, equipment strip, gold seizure, database deletion, server broadcast; failed murder attempt → 1 day prison
- `Scripts/Locations/MagicShopLocation.cs` — Dark arts kill calls `ApplyMurderConsequences()` after assassination; disabled `IsPermaDead`; curse detection now checks player equipped gear + backpack + companions
- `Scripts/Locations/CastleLocation.cs` — Disabled `IsPermaDead` on royal executions
- `Scripts/Locations/PrisonLocation.cs` — Murder convict blocks on `HandleEscapeAttempt()`, `HandleOpenCellDoor()`, `HandleDemandRelease()`, `HandlePayBail()`
- `Scripts/Locations/InnLocation.cs` — Companion equip: equip-first-remove-second pattern; better item matching by name+stats
- `Scripts/Locations/MusicShopLocation.cs` — Removed player level check on instrument purchase; widened shop level filter +15 → +30
- `Scripts/Locations/DungeonLocation.cs` — Enhanced `dungeon_room` event with features list, exit details (dir/label/cleared/desc), potion counts; `dungeon_map` event with full room grid data for canvas rendering
- `Scripts/Systems/StreetEncounterSystem.cs` — Added `WasBountyKill` to `EncounterResult`; disabled `IsPermaDead`
- `Scripts/Systems/WorldSimulator.cs` — Disabled `IsPermaDead` on NPC-on-NPC deaths
- `Scripts/Systems/ChallengeSystem.cs` — Player team NPCs blocked from autonomous throne challenges
- `Scripts/Systems/MaintenanceSystem.cs` — Unpaid wage NPC departure disabled for player teams
- `Scripts/Systems/CombatEngine.cs` — Team XP redistribution counts only alive teammates; charm resist message on failure; Searing Totem AoE + burn; NPC teammate totem per-round processing; `combat_action` event handling
- `Scripts/Systems/DailySystemManager.cs` — Murder convicts don't gain escape attempts; `IsMurderConvict` cleared on sentence expiry
- `Scripts/Systems/InventorySystem.cs` — Electron interactive inventory loop with `EQUIP:`, `UNEQUIP:`, `DROP:` commands, slot picker for rings/1H weapons, full `CanEquip` validation, `EquipmentDatabase.RegisterDynamic` for proper IDs
- `Scripts/Systems/SaveDataStructures.cs` — Added `IsMurderConvict` field
- `Scripts/Systems/SaveSystem.cs` — `IsMurderConvict` serialization
- `Scripts/Core/GameEngine.cs` — `IsMurderConvict` restore on load
- `Scripts/UI/ElectronBridge.cs` — No changes (events emitted from location/system files)
- `electron-client/src/game-ui.js` — DD-style combat renderer, dungeon map canvas, inventory paperdoll, character status, party management, potions menu, combat log, ability bar
- `electron-client/styles/gui.css` — Full combat/overlay/inventory/status/party/potions CSS
- `electron-client/generate-sprites.js` — Updated to 192px, added --force/--classes/--monsters/--name flags
- `electron-client/assets/classes-hd/` — 17 new HD class sprites (192px transparent)
- `electron-client/assets/monsters-hd/` — 15 new HD monster sprites (192px transparent)
