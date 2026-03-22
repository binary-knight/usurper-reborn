# Usurper Reborn v0.53.5 Release Notes

**Version Name:** Ancestral Spirits

## Loot Stat Transfer Fix (CHA/AGI/STA)

Critical bug: when equipping dungeon loot, the Item-to-Equipment conversion only transferred Strength, Dexterity, Wisdom, and Defence from Item fields. **Charisma, Agility, and Stamina** were silently dropped. This means ALL dungeon loot with thematic CHA, AGI, or STA bonuses has been losing those stats when equipped. Fixed in all 3 loot-to-equipment conversion paths in CombatEngine.

This also explains why the new CHA gear templates from v0.53.4 appeared to have no stats — the templates were generating correctly, but the bonuses were discarded during equip.

## Siren's Lament Base Damage

Siren's Lament (Wavecaller debuff spell) now deals base damage on every cast, not just on critical casts. Non-crit base damage is `25-40 + level/2`, scaled with CHA and proficiency. At level 35, expect ~500-700 damage on a regular cast alongside the weaken debuff. Critical casts still get the bonus psychic damage on top.

## Wave Echo Base Damage Buff

Wave Echo ability base damage increased from 70 to 100, improving its relevance in mid-game. The "double damage vs debuffed" mechanic means effective damage is 200 base against weakened targets.

## Hostile NPC Adventurer Scaling

Street encounter hostile NPCs (thugs, rogues, brigands, etc.) had flat stat scaling that couldn't keep up with player power. At level 35 they had ~600 HP against players with 3000+. Stats now use quadratic scaling: at level 35 they have ~1600 HP, at level 60 ~3500 HP. Still weaker than players (who have gear, abilities, spells) but no longer trivial.

## Soulweaver's Loom Narrative Fix

The lore was contradictory — the Loom was supposedly "hidden by Manwe on floor 65" but also dropped by Veloura on death. Rewritten: Veloura found the Loom in ancient ruins and tried to use it to heal her own fractured heart, but it bound her corruption deeper.

- **Kill Veloura**: Loot the Loom from her body (unchanged)
- **Save Veloura**: She now entrusts the Loom to you directly as thanks
- **Floor 65 discovery event removed** — no longer need to search for something Veloura already had
- **All saved Old Gods** now give their artifact as a gift during the save scene

The Moral Paradox event (Veloura's Cure) on floor 65 is preserved.

## Dual-Wielding Companion Off-Hand Loot Fix

Companions who dual-wield weapons only compared loot drops against their main hand, ignoring the off-hand entirely. A weapon that was a clear upgrade for the off-hand was silently passed over. Now compares against whichever hand is weaker and equips to that slot.

## Combat Tips Accuracy Fix

Three combat tips were inaccurate:
- `[S] to cast spells` → spells are on quickbar `[1]-[9]`, not `S` (which is Status display)
- `[L]hide to escape or reposition` → Hide gives a stealth bonus to your next attack, not escape
- Minor formatting fixes for consistency

## XP Distribution Override Fix

Setting XP distribution to 100% for yourself and 0% for companions was ignored — the auto-distribute system saw "all teammate slots are 0" and assumed you never configured it, overwriting with an even split. Now respects the player's explicit 100% self allocation.

## AoE Attack Spell Self-Heal Fix

AoE attack spells that also heal the caster (Deluge of Sanctity) never applied the healing. The multi-monster AoE damage path handled damage and special effects but skipped `spellResult.Healing`. Self-heal now applies after AoE damage.

## Dungeon Merchant Removed

The dungeon merchant encounter has been removed. Items sold by the merchant had missing `WeaponType`, causing purchased staves to not satisfy spell weapon requirements — spellcasters who bought a staff couldn't cast spells. MerchantDen rooms now generate as treasure chests instead.

## Deluge of Sanctity Cooldown

Deluge of Sanctity (Tidesworn level 5 spell — AoE damage + self-heal) now has a 2-round cooldown, matching Voidreaver's Unmaking. Quickbar shows `(CD:X)` when on cooldown, and attempting to cast shows the remaining rounds.

## Tank Class Passive AoE Absorption

Tank classes (Warrior, Paladin, Barbarian, Tidesworn) now passively absorb boss AoE damage for the party even without an active taunt. Previously, boss AoE absorption only triggered if someone had specifically taunted the boss that round. Tidesworn in particular was not recognized as a tank for Old God encounters despite having the highest aggro weight in the game.

## Player Stun Duration Fix

Boss stun effects (Veloura's Charm, Manwe's Time Freeze) applied stun with duration 1, but `ProcessStatusEffects` ticked it down to 0 before the action check — so the player never actually lost a turn. Duration increased to 2 to account for the off-by-one, matching existing patterns for other timed effects.

## Runed Gear Stat Diversity Fix

"Runed" items (10 templates across all slots, levels 65-100) were classified as caster-themed, so ~75% of high-level loot dropped with INT+CON regardless of class. "Runed" moved from the caster theme to the generic crafted/forged theme (DEF+CON), significantly diversifying late-game loot stats. "Ancient" items retain INT+WIS.

## Death & Resurrection Overhaul

The Veil Between Worlds death screen has been completely reworked:

- **Divine Intervention**: Unchanged (uses resurrection charges)
- **Temple Resurrection**: Now costs 50% of gold on hand (was flat 500 + level*100). Limited to 3 uses total per character.
- **Deal with Death**: Now requires 10,000 Darkness (was 100) and causes permanent stat loss of 2-5 to a random stat. A true dark bargain.
- **Accept Your Fate**: No longer ends the game. Instead: lose 5 levels (with full stat decreases per level), 75% gold, and a random equipped item. Over-leveled gear is automatically unequipped to inventory. Revive at 10% HP.

`ReverseClassStatIncrease()` method added to properly undo per-level stat gains when losing levels, ensuring stats accurately reflect the new level.

Three layers of save cheesing prevention: save on death (HP=0 persisted before Veil screen), save after choice (penalty persisted immediately), and login dead check (closing during Veil screen auto-applies worst penalty on next login).

## Cursed Item Purification Fix

Two bugs in curse removal at the Magic Shop:
- **Phantom HP bonus**: Purification added `HP += penalty * 2` to "reverse" a curse penalty that was never applied to HP. `ApplyCursePenalties` only subtracts STR/DEX/WIS and adds negative CON via LootEffects — it never touches HP. This caused purified items to gain a fake +20-30 MaxHP bonus.
- **Negative CON persisting**: The curse's -CON was stored in `LootEffects` but purification never cleaned it up. Purified items kept the -CON penalty permanently. Now removes all negative CON LootEffects during purification.

## King/Queen Title

Players on the throne now automatically receive the "King" or "Queen" title (based on sex). The title appears in the Preferences title selection list and is cleared when abdicating or being overthrown.

## Death Summary Details

## Serialization Audit (20 Properties)

20 persistent Character properties were silently resetting to defaults on save/load — they existed on the Character class but were never added to the save pipeline. Now properly serialized:

- **Resurrection/Church:** `Resurrections`, `ResurrectionsUsed`, `MaxResurrections`, `BannedFromChurch`, `BlessingsReceived`, `ChurchDonations`, `SacrificesMade`, `HealingsReceived`, `HasHolyWater`, `BardSongsLeft`
- **Kingdom/Crime:** `DaysInPower`, `Thievery`, `WantedLvl`, `TaxRelief`, `RoyTaxPaid`, `KingVotePoll`, `KingLastVote`
- **Other:** `Kids`, `DisRes` (disease resistance), `AgePlus` (age progression counter)

Legacy saves load fine — missing JSON fields default to 0/false. `MaxResurrections` has a guard to prevent old saves from getting 0.

## Death Summary Details

All three resurrection options now show detailed summaries: Temple shows gold paid, remaining gold, HP restored, and resurrections left. Dark Bargain shows darkness consumed (before/after), which stat was lost and by how much, and HP restored. Accept Fate shows levels lost (before/after), gold lost with remaining, item destroyed, full stat change breakdown (STR -X, DEF -Y, etc.), and any over-leveled gear unequipped to inventory.

## Smart Sell Filters (by DJLunacy)

New `[F] Filtered Sell` option in all three shops (Weapon, Armor, Magic). Three filter modes:
- **By Level**: sell items X+ levels below you
- **By Value**: sell items worth less than X gold
- **By Rarity**: sell all Common items only

Preview shows item count and total gold before confirming. Skips cursed and unidentified items automatically.

## Companion Auto-Equip Best Gear (by DJLunacy)

New `[B] Auto-equip Best Gear` option on the companion equipment screen at the Inn. Scans player inventory and equips the best available item per slot based on a scoring system (weapon power, armor class, stat bonuses). Displaced items return to player inventory. Skips off-hand slot when companion is using a two-handed weapon.

## Single-Player Save OOM Fix

`FileSaveBackend.WriteGameData` used `JsonSerializer.Serialize()` which builds the entire JSON string in memory before writing. With 200+ NPCs carrying relationship/memory/story data, the save data can exceed available memory. Replaced with `JsonSerializer.SerializeAsync()` which streams directly to the file, avoiding the large memory allocation.

## Code Health: Dead Code Removal

6 unused system files deleted (4,777 lines): AdvancedCombatEngine (1,283), AdvancedMagicShopLocation (925), NPCMaintenanceEngine (887), OnlineDuelSystem (719), TournamentSystem (665), EnhancedNPCSystem (298). All had zero class references.

## Code Health: TODO Cleanup

All 13 remaining TODOs resolved. 1 dead method removed (TestMagicShopSystem), 5 converted to descriptive comments (prison online features, dialogue rewards, trade item return), 1 fixed (minstrel buff was already implemented), 6 removed (stale references to systems that now exist). Zero TODOs remain in the codebase.

## Code Health: Exception Logging

63 empty catch blocks across SaveSystem, GameEngine, WorldSimulator, NewsSystem, MaintenanceSystem, and FileSaveBackend now log at Debug level via DebugLogger. Previously these silently swallowed exceptions, making subsystem serialization failures invisible in post-mortem debugging. CS0168 ("variable declared but never used") warnings dropped from 106 to 0.

## Code Health: Thread Safety (Random.Shared)

116 `new Random()` instances replaced with `Random.Shared` across 57 files. The MUD server runs concurrent player sessions — `new Random()` without a seed is not thread-safe and could produce duplicate sequences. Only 1 intentionally seeded instance remains (DungeonGenerator deterministic floor layouts).

---

## Files Changed

- `Scripts/Core/GameConfig.cs` — Version 0.53.5
- `Scripts/Core/Character.cs` — `TempleResurrectionsUsed` property; `DelugeCooldown` property
- `Scripts/Core/GameEngine.cs` — Restore 20 persistent properties from save data; HP<=0 login dead check with auto-penalty
- `Scripts/Systems/SaveDataStructures.cs` — 20 new persistent fields (resurrection, church, kingdom, crime, family, misc)
- `Scripts/Systems/SaveSystem.cs` — Serialize 20 new persistent properties
- `Scripts/Systems/CombatEngine.cs` — CHA/AGI/STA loot stat transfer; dual-wield companion off-hand loot; combat tips fix; XP distribution 100% self; AoE attack spell self-heal; Deluge cooldown; tank class passive AoE absorption; player stun duration fix; death/resurrection overhaul with summaries; save cheesing on death; Runed gear reclassified
- `Scripts/Systems/SpellSystem.cs` — Siren's Lament base damage; Deluge cooldown set
- `Scripts/Systems/ClassAbilitySystem.cs` — Wave Echo BaseDamage 70→100
- `Scripts/Systems/StreetEncounterSystem.cs` — Hostile NPC quadratic stat scaling
- `Scripts/Systems/OldGodBossSystem.cs` — `HandleBossSaved` grants artifact directly
- `Scripts/Systems/ArtifactSystem.cs` — Soulweaver's Loom lore rewritten
- `Scripts/Systems/DungeonGenerator.cs` — Merchant removed; MerchantDen→treasure chests
- `Scripts/Systems/LootGenerator.cs` — Runed items reclassified from caster to crafted theme
- `Scripts/Locations/DungeonLocation.cs` — Floor 65 Loom discovery removed; hint simplified
- `Scripts/Locations/BaseLocation.cs` — King/Queen title in preferences
- `Scripts/Locations/CastleLocation.cs` — Auto-set King/Queen title on throne; clear on abdication/overthrow; throne difficulty increases
- `Scripts/Locations/LevelMasterLocation.cs` — `ReverseClassStatIncrease()` for death level loss
- `Scripts/Locations/MagicShopLocation.cs` — Cursed item purification: removed phantom HP, cleans negative CON/INT LootEffects
