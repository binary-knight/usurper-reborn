# Usurper Reborn v0.53.9 Release Notes

**Version Name:** Ancestral Spirits

## Old God Boss Difficulty Overhaul

All seven Old God bosses have been massively buffed. Players were face-rolling through boss encounters that should be the hardest fights in the game. Every boss now demands real preparation, good gear, a full party, and sustained DPS to beat the enrage timer.

### Stat Changes

| Boss | Floor | HP | STR | DEF |
|------|-------|----|-----|-----|
| Maelketh | 25 | 25K -> **100K** | 150 -> **600** | 80 -> **320** |
| Veloura | 40 | 50K -> **200K** | 140 -> **560** | 100 -> **400** |
| Thorgrim | 55 | 100K -> **400K** | 200 -> **1,200** | 200 -> **800** |
| Noctura | 70 | 150K -> **600K** | 220 -> **1,400** | 140 -> **560** |
| Aurelion | 85 | 250K -> **800K** | 840 -> **1,600** | 180 -> **500** |
| Terravok | 95 | 350K -> **1M** | 1,050 -> **1,800** | 300 -> **600** |
| Manwe | 100 | 500K -> **1.5M** | 1,200 -> **2,400** | 300 -> **800** |

### Enrage Timer Changes

Enrage timers extended to match the increased HP pools while still functioning as a meaningful DPS check:

| Boss | Old Timer | New Timer | Required DPS/round |
|------|-----------|-----------|-------------------|
| Maelketh | 25 | **40** | 2,500 |
| Veloura | 22 | **35** | 5,714 |
| Thorgrim | 20 | **35** | 11,429 |
| Noctura | 18 | **30** | 20,000 |
| Aurelion | 16 | **28** | 28,571 |
| Terravok | 14 | **25** | 40,000 |
| Manwe | 12 | **25** | 60,000 |

## Combat Clarity: Armor Power vs Hit Roll AC

Player feedback: the defense display was confusing because "Armor Class" on equipment screens, "DEF" on the combat status bar, and "AC" in combat rolls were three different numbers measuring different things.

### Terminology Changes
- **Equipment screens**: "Armor Class" renamed to **"Armor Power"** (the raw damage reduction value from gear)
- **Per-item stats**: "AC" label changed to **"AP"** (Armor Power)
- **Combat rolls**: `vs AC 118` now reads `vs **Hit Roll AC** 118` (clarifies this is the D20 target number)
- **Combat status bar**: Now shows **Hit Roll AC** in bright green alongside ATK and DEF, so players can see what monsters roll against

### What the numbers mean
- **DEF** (combat status bar): Total damage reduction pool (Defence stat + Armor Power + Magic AC). Higher = more damage absorbed per hit.
- **Hit Roll AC** (combat status bar): D20-scale number (10 + DEX/3 + ArmPow/25 + Level/10 + MagicAC/25). Determines whether monster attacks *land* at all.
- **AP** (per-item): How much armor power this specific piece of equipment provides.

## XP Auto-Redistribute Toggle

The XP redistribution on teammate death (added in v0.53.8) now has a toggle. Access it from the XP Distribution menu in the dungeon (`[X]` then `[R]`). Defaults to On.

This prevents unwanted redistribution for players who intentionally set uneven XP splits (e.g., 80/5/5/5/5 to funnel XP to themselves).

## XP Redistribution Player-Inclusive Fix

The teammate death XP redistribution was only redistributing orphaned XP among surviving NPC teammates, not including the player. Example: 20/20/20/20/20 split, 3 NPCs die -> went to 20/80 (player/NPC) instead of the correct 50/50. Now redistributes evenly among the player AND surviving teammates.

## Website Stats Cache Performance

The live stats API (`/api/stats`) was taking ~20 seconds on first load with 275+ NPCs in the database. Three fixes:
- **Cache TTL increased** from 30 seconds to 2 minutes
- **Pre-warm on startup** — stats cache built 2 seconds after server start
- **Background refresh** — cache rebuilt automatically before expiry so no visitor ever hits the cold query path

## NPC Orientation Diversity Fix

The `EnsureOrientationDiversity()` method guaranteed minimum gay/lesbian/bisexual NPCs in the population, but it ran against the full NPC list including dead NPCs. With 40+ dead NPCs in the pool, the diversity check could pass even when no living NPCs had the required orientations. Now filters to living NPCs only before checking diversity minimums.

Orientation distribution adjusted to better reflect real-world dynamics: ~2% homosexual, ~3% bisexual, ~95% heterosexual.

## Loot Staff Handedness Fix

Dungeon loot staves were sometimes generated as one-handed weapons, allowing players to equip a shield alongside their staff. All staves and quarterstaves are now forced to two-handed in `InferHandedness()`, matching their intended design.

## Loot Item Slot Safety

Items with invalid or mismatched `ObjType` for their equipment slot (e.g., a cloak ending up in MainHand) are now caught and rejected during equip. Added validation in the auto-pickup path to prevent slot mismatches.

## Blur Status Persistence Fix

The Blur status effect (`BLR`) was not being cleared between combats, unlike Blessed (`BLS`) which was properly reset. Both are now cleared at combat start along with other spell buffs (Protected, Haste, MagicACBonus).

## Defense Calculation Fix (Critical)

The combat defense system had a major discrepancy between displayed and actual values. The status bar showed DEF as `Defence + ArmPow + MagicACBonus` (e.g., 1834), but actual damage reduction used a sqrt-scaled formula on ArmPow alone (giving ~178 max from 1265 ArmPow). Players saw "DEF: 1834" but were only getting ~600 actual damage reduction per hit.

### Regular monster attacks on player
Was using `sqrt(ArmPow) * 5` for armor absorption. Now uses the same 75-100% variance on full ArmPow that player-attacks-monster uses, plus Defence stat and MagicACBonus. Defense now roughly matches the DEF number on the status bar.

### Monster special abilities (DirectDamage)
Were using `Defence / 3` only — completely ignoring ArmPow and MagicACBonus. Abilities like "strikes with deadly venom" dealt near-full damage regardless of armor. Now uses `Defence + 50% ArmPow + MagicACBonus`. Abilities still partially bypass armor (50% ArmPow instead of 75-100%) so they remain scarier than regular attacks.

### Monster special abilities (DamageMultiplier)
Were using `Defence` stat only. "Rallies, surging with renewed vigor!" attacks ignored all armor. Now uses the same `Defence + 50% ArmPow + MagicACBonus` formula plus shows the `[X damage vs Y defense]` calculation line.

### Monster abilities on companions
Were using `sqrt(Defence) * 3`. Now uses `Defence + 50% ArmPow`.

## Spell Buff Display Fixes

- **"+403 AC for 999 rounds"** — Protection spell messages now say "+403 DEF" instead of "+403 AC", and "999 rounds" displays as "whole fight"
- **"+50% damage for 999 rounds"** — Attack buff messages now show the actual bonus value ("+48 attack") instead of a hardcoded "+50% damage", and "whole fight" instead of "999 rounds"
- **Status bar PWR(999)** — Whole-fight status effects no longer show the duration number (just "PWR" instead of "PWR(999)")

## Abyssal Anchor Rework (Tidesworn)

Abyssal Anchor (level 50) reworked from a self-defense-only ability into a proper tank taunt:
- **AoE taunt** — All enemies forced to attack you for 3 rounds
- **+80 DEF** self buff (unchanged)
- **-20% enemy damage** weaken (unchanged)
- **Cooldown reduced** from 5 to 4 rounds

This lets Tidesworn alternate between Thundering Roar (3-round taunt, 5 CD) and Abyssal Anchor (3-round taunt, 4 CD) for near-permanent taunt uptime. Thundering Roar is a pure AoE taunt; Abyssal Anchor adds self-defense and enemy weaken.

## Healing Elixir Ally Targeting (Alchemist)

Healing Elixir (Alchemist, level 8) can now target teammates. When used by a player, a target prompt shows all party members with their current HP. When used by an NPC Alchemist teammate, AI auto-targets the most injured party member. This gives Alchemist NPCs a way to keep the party alive with single-target heals before their AoE healing abilities at higher levels.

## Localization: ui.on / ui.off Keys

Added missing `ui.on` and `ui.off` localization keys used by toggle displays (e.g., XP auto-redistribute). Translated in all 5 languages.

---

## Files Changed

- `Scripts/Core/GameConfig.cs` — Version 0.53.9
- `Scripts/Core/Character.cs` — `AutoRedistributeXP` property
- `Scripts/Core/GameEngine.cs` — `AutoRedistributeXP` restore on load
- `Scripts/Data/OldGodsData.cs` — All 7 Old God bosses: HP 4x (first 4), STR scaled up, DEF scaled up, AGI/CHA scaled (where applicable)
- `Scripts/Systems/OldGodBossSystem.cs` — Enrage timers extended: Maelketh 25->40, Veloura 22->35, Thorgrim 20->35, Noctura 18->30, Aurelion 16->28, Terravok 14->25, Manwe 12->25
- `Scripts/Systems/TrainingSystem.cs` — Extracted `CalculatePlayerHitRollAC()` public helper from `RollMonsterAttack()`
- `Scripts/Systems/CombatEngine.cs` — Defense calculation fix: regular attacks use 75-100% ArmPow + Defence + MagicACBonus (was sqrt-scaled ArmPow only); DirectDamage abilities use Defence + 50% ArmPow + MagicACBonus (was Defence/3); DamageMultiplier abilities same formula + defense calc line; companion DirectDamage uses Defence + 50% ArmPow (was sqrt); protection spell messages "AC"->"DEF"; 999-round durations show "whole fight"; attack buff shows actual value; status bar hides duration for whole-fight effects; Hit Roll AC display in combat status bar; "vs Hit Roll AC" in monster attack rolls; XP redistribution includes player; auto-redistribute toggle check; Abyssal Anchor AoE taunt + weaken handler; Healing Elixir ally targeting with player prompt and NPC AI
- `Scripts/Systems/ClassAbilitySystem.cs` — `CanTargetAlly` property on ClassAbility; Healing Elixir `CanTargetAlly = true`; Abyssal Anchor: cooldown 5->4, description updated for AoE taunt
- `Scripts/Systems/SaveDataStructures.cs` — `AutoRedistributeXP` field
- `Scripts/Systems/SaveSystem.cs` — `AutoRedistributeXP` serialization
- `Scripts/Locations/BaseLocation.cs` — Preferences: date format; equipment stats use "AP" label
- `Scripts/Locations/DungeonLocation.cs` — `[R]` auto-redistribute toggle in XP Distribution menu; staff handedness fix; item slot validation
- `Scripts/Locations/MagicShopLocation.cs` — "Armor Class" -> "Armor Power" in enchantment stat names
- `Scripts/Systems/NPCSpawnSystem.cs` — `EnsureOrientationDiversity()` filters to living NPCs; orientation rates adjusted (~2% gay, ~3% bi, ~95% straight)
- `web/ssh-proxy.js` — Stats cache TTL 30s->120s; pre-warm on startup; background refresh interval
- `web/index.html` — Minor text update
- `Localization/en.json` — `ui.on`, `ui.off`, `combat.bar_ac` keys; "Armor Class" -> "Armor Power"; "AC" -> "AP"
- `Localization/es.json` — All new keys translated
- `Localization/hu.json` — All new keys translated
- `Localization/it.json` — All new keys translated
- `Localization/fr.json` — All new keys translated
