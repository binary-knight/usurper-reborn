# v0.42.2 - Stability & Balance Patch

Fixes daily action exploits in online mode, improves combat balance, healer UX, and resolves several bugs reported during playtesting.

---

## Online Mode: 7 PM ET Daily Reset

Daily resets in online mode are now tied to a fixed **7 PM Eastern Time** server-side boundary, completely decoupled from player actions. Sleeping at the Inn, Dormitory, or Castle only heals — it no longer advances the game day or resets daily counters. Players can log out and back in freely without resetting prayer limits, arena fights, or other daily-gated systems.

- **Daily tracking persists across sessions** — Prayer, Inner Sanctum, Binding of Souls, Seth fights, arm wrestling, and Roy quests now save to your character and survive logout/login.
- **World maintenance at 7 PM ET** — King reign days, treasury income/expenses, guard loyalty, and world events are processed once per day by the server at 7 PM Eastern, not on player sleep.
- **Missed resets applied on login** — If you were offline when 7 PM passed, your daily counters reset automatically when you next log in.
- **Single-player unaffected** — Sleep still advances the day in local/single-player mode as before.

## King Castle Improvements

- **King sleep is now a logout** — In online mode, sleeping in the Royal Chambers saves your game, registers you as sleeping in the castle (protected by royal guards), and ends your session. Previously it just healed and returned to the menu.
- **King menu persistence fixed** — Fixed a bug where leaving the castle and logging out would lock you out of the king's throne room menu on your next login. The king status is now refreshed every time you enter the castle.

## Combat Balance

- **Champion damage reduced** — Removed a double-dipping 1.15x attack multiplier on Champion monsters. Their stats are already boosted 1.5x during generation; the extra multiplier in `GetAttackPower()` was stacking on top, making Champions hit ~72% harder than intended.
- **Non-boss damage cap** — Non-boss monsters (including Champions) can no longer deal more than 75% of your max HP in a single hit. This prevents guaranteed one-shots while still allowing dangerous hits. Actual bosses (Old Gods, floor bosses) are not capped.

## Healer Improvements

- **Prices now include taxes** — The Healer now shows the total cost with taxes in all price displays (HP healing, full heal, potions, mana potions). Previously, the base price was shown but you were charged the taxed amount, leading to confusing "can't afford" rejections.
- **"You can afford" calculation fixed** — The affordability hint now accounts for taxes, so the number shown is accurate.
- **Healing tax capped at 15%** — Combined king + city tax on all healing services (HP restoration, potions, disease cures, curse removal) is now capped at 15%, down from a possible 35%. This prevents a death spiral where high taxes make recovery unaffordable after dying.
- **Healing potion carry limit enforced** — Players can now carry a maximum of 20 + (level - 1) healing potions, matching the existing mana potion cap. Previously there was no limit, allowing thousands of potions to be stockpiled.

## Bug Fixes

- **Gambling Den re-prompts on bad bets** — Entering an invalid or below-minimum bet at the Gambling Den now re-prompts instead of kicking you back to the Dark Alley menu. Enter 0 to leave.
- **Death location in News fixed** — Dying in the dungeon now correctly reports "Dungeon Floor X" in the news feed instead of showing a blank or wrong location.
- **Death penalty warning added** — The "Accept Your Fate" resurrection option now clearly states the penalties: "WARNING: Lose 10-20% XP, 50-75% gold, and possibly an item" instead of the vague "face the consequences."
- **NPC permadeath fixed** — Permanently dead NPCs (aged death, permakill) were being resurrected because the world sim's NPC reload was dropping the `IsPermaDead` flag. Now properly preserved across all save/load cycles.
- **Permadead NPCs can't be resurrected by players** — The Home and Team Corner resurrection features now filter out permanently dead NPCs.

## UX Improvements

- **[R] Return standardized** — All location menus now use [R] as the return key. Conflicting keys were reassigned: Inn [R]umors → [U]pdates, Church [R]ecords → [V]iew Records, Bank [R]ob → r[O]b, Home [R]est → r[E]st.
- **Quick command bar updated** — The bottom-of-screen quick command hint now shows `[R]eturn` instead of `[Q]uick Return`.

---

## Files Changed

| File | Changes |
|------|---------|
| `Scripts/Core/GameConfig.cs` | Version 0.42.2, added `DailyResetHourEastern = 19` |
| `Scripts/Core/Character.cs` | Added `LastDailyResetBoundary`, `LastPrayerRealDate`, `LastInnerSanctumRealDate`, `LastBindingOfSoulsRealDate`, `SethFightsToday`, `ArmWrestlesToday` |
| `Scripts/Systems/SaveDataStructures.cs` | Added 7 new daily tracking fields to `PlayerData` |
| `Scripts/Systems/SaveSystem.cs` | Serialize/restore all new daily tracking fields |
| `Scripts/Systems/DailySystemManager.cs` | Added `GetCurrentResetBoundary()`, online mode daily reset logic, `ProcessPlayerDailyEvents()`, counter resets |
| `Scripts/Systems/MaintenanceSystem.cs` | Reset new daily counters in `ResetDailyParameters()` |
| `Scripts/Systems/WorldSimService.cs` | Added 7 PM ET world daily reset (king, guards, treasury), restored `IsPermaDead` in `RestoreNPCsFromData()` |
| `Scripts/Systems/CityControlSystem.cs` | Added `CalculateHealingTaxedPrice()` with 15% combined tax cap |
| `Scripts/Systems/CombatEngine.cs` | Non-boss damage cap at 75% max HP, death penalty warning on Accept Your Fate, fixed empty `CurrentLocation` in death news |
| `Scripts/Core/Monster.cs` | Removed IsMiniBoss 1.15x double-dip from `GetAttackPower()` |
| `Scripts/Core/GameEngine.cs` | Added `CheckDailyReset()` on login, restore new daily tracking fields |
| `Scripts/Locations/BaseLocation.cs` | Set `player.CurrentLocation` on enter, quick command bar `[Q]` → `[R]` |
| `Scripts/Locations/CastleLocation.cs` | King sleep as logout in online mode, refresh `playerIsKing` on every display |
| `Scripts/Locations/DungeonLocation.cs` | Set `CurrentLocation = "Dungeon Floor X"` at entry and all floor changes |
| `Scripts/Locations/DarkAlleyLocation.cs` | Gambling Den bet retry loop instead of exit on bad bet |
| `Scripts/Locations/HealerLocation.cs` | Show taxed prices, fix affordability calc, healing potion carry limit, use healing tax cap |
| `Scripts/Locations/InnLocation.cs` | Skip `ForceDailyReset` in online mode, [R] Return standardization, use Character daily counters |
| `Scripts/Locations/DormitoryLocation.cs` | Skip `ForceDailyReset` in online mode |
| `Scripts/Locations/MainStreetLocation.cs` | Skip `ForceDailyReset` in online mode |
| `Scripts/Locations/HomeLocation.cs` | Filter permadead NPCs from resurrection, [R] Return standardization |
| `Scripts/Locations/TeamCornerLocation.cs` | Filter permadead NPCs from resurrection |
| `Scripts/Locations/ChurchLocation.cs` | [R] Return standardization |
| `Scripts/Locations/BankLocation.cs` | [R] Return standardization |
| `Scripts/Locations/NewsLocation.cs` | [R] Return in pagination |
| `Scripts/Locations/DarkAlleyLocation.cs` | [R] Return shown in menu |
| `Scripts/Systems/DivineBlessingSystem.cs` | Use `Character.LastPrayerRealDate` in online mode |
| `Scripts/Locations/TempleLocation.cs` | Use `Character.LastInnerSanctumRealDate` in online mode |
| `Scripts/Locations/MagicShopLocation.cs` | Use `Character.LastBindingOfSoulsRealDate` in online mode |
