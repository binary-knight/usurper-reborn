# v0.44.0 - Home Sweet Home

Your home has been completely overhauled with a tiered upgrade system. New characters start in a dilapidated shack and can invest gold to transform it into a grand estate.

---

## New Features

### Tiered Home Upgrades

Your home now features five upgradable systems, each with 5 levels:

**Living Quarters** — Upgrade from a drafty shack to a grand estate. Rest recovery scales from 25% HP/Mana (1x/day) at level 0 to 100% HP/Mana (5x/day) at level 5.

**Bed** — Your moth-eaten straw pile actually hurts your chances of having children (-50% fertility). Upgrade through five tiers up to a Royal Canopy Bed that boosts fertility by +50%.

**Storage Chest** — You start with no chest at all. Buy a Wooden Crate (10 items) and upgrade all the way to a Dimensional Vault (100 items). Chest contents now persist across saves (previously lost on logout).

**Hearth** — Build a hearth to gain the "Well-Rested" combat buff after resting at home. Scales from +3% damage/defense for 3 combats to +12% damage/defense for 15 combats.

**Herb Garden** — Grow healing herbs in your garden. Gather 1-5 free healing potions per day depending on garden level.

### New Special Purchases

**Study / Library** (750,000 gold) — A magnificent study lined with ancient tomes grants +5% XP from all combat encounters.

**Servants' Quarters** (500,000 gold) — A loyal staff collects rent and provides services, generating daily passive gold income (100 + level × 10 gold per day).

### Dynamic Home Descriptions

Your home's appearance changes based on your Living Quarters level, from "a drafty, dilapidated shack with thin walls and a leaky roof" to "a grand estate befitting a hero, where every room is beautifully appointed."

### Redesigned Home Menu

The home menu has been completely reorganized into a clean 3-column grid layout. All options are always visible in fixed positions (unavailable options are dimmed instead of hidden). No more shifting layout when you unlock new upgrades. Both full-size and BBS compact menus updated.

---

## Bug Fixes

### Fountain of Vitality HP Bonus Never Applied
The Fountain of Vitality upgrade stored a `BonusMaxHP` value but never actually added it to your max HP during stat calculation. Now properly adds bonus HP in `RecalculateStats()`.

### Legendary Armory Damage/Defense Never Applied
The Legendary Armory upgrade stored `PermanentDamageBonus` and `PermanentDefenseBonus` values but CombatEngine never read them. Now properly applies +5% damage on attacks and +5% defense when hit.

### Chest Contents Lost on Logout
Home chest was stored in a static in-memory dictionary that was never serialized. All chest items were lost every time you logged out. Chest contents are now properly saved and restored.

---

## Balance Changes

- Home rest is now limited to 1-5 uses per day (was unlimited)
- Home rest recovery starts at 25% (was 100%)
- Teleportation Circle removed from upgrade menu (was purchasable but non-functional)

---

## Files Changed

| File | Changes |
|------|---------|
| `Scripts/Locations/HomeLocation.cs` | Complete overhaul: tiered DoRest() with daily limits; 5-tier upgrade system for Living Quarters, Bed, Chest, Hearth, Herb Garden; GatherHerbs() method; dynamic home descriptions; chest capacity enforcement; Study/Library and Servants' Quarters purchases; removed Teleportation Circle; redesigned home menu with fixed 3-column grid; upgrade shop shows what you're buying (next level stats) |
| `Scripts/Core/Character.cs` | Added BedLevel, HearthLevel, HomeRestsToday, HerbsGatheredToday, WellRestedCombats, WellRestedBonus, HasStudy, HasServants properties; BonusMaxHP now applied in RecalculateStats() |
| `Scripts/Core/GameConfig.cs` | Version 0.44.0; home upgrade constants (HomeRecoveryPercent, HomeRestsPerDay, BedFertilityModifier, ChestCapacity, HearthDamageBonus, HearthCombatDuration, HerbsPerDay, HerbHealPercent, StudyXPBonus, ServantsDailyGold) |
| `Scripts/Systems/SaveDataStructures.cs` | Added BedLevel, HearthLevel, HomeRestsToday, HerbsGatheredToday, WellRestedCombats, WellRestedBonus, HasStudy, HasServants, ChestContents to PlayerData |
| `Scripts/Systems/SaveSystem.cs` | Serialize/restore new home fields and chest contents via SerializeChestContents() |
| `Scripts/Core/GameEngine.cs` | Restore new home properties and chest contents on load |
| `Scripts/Systems/CombatEngine.cs` | Well-Rested damage/defense buff; PermanentDamageBonus applied to attacks; PermanentDefenseBonus applied to player defense; Study XP bonus; WellRestedCombats decrement per combat |
| `Scripts/Systems/DailySystemManager.cs` | Reset HomeRestsToday and HerbsGatheredToday; Servants' Quarters daily gold income |
| `Scripts/Systems/IntimacySystem.cs` | Bed fertility modifier applied to pregnancy chance |
