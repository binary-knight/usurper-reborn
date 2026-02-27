# Usurper Reborn v0.47.4 — Playtest Fixes & Onboarding Polish

## Arena Portal: Real Interactive Combat

The dungeon arena portal encounter previously used simulated combat — three rounds resolved instantly with "VICTORY!" or "DEFEAT!" and no player input. Now the arena portal generates a real arena champion monster (scaled to dungeon floor level, mini-boss tier) and runs full interactive combat through CombatEngine. Players get their full action set: attacks, spells, abilities, items, fleeing — exactly like regular dungeon combat. Winning awards a Chivalry bonus (+25) on top of normal combat rewards.

## Weapon Shop: Dual-Wield Works from Category View

The [D] Dual-Wield Setup option in the Weapon Shop only worked from the main shop menu. If you were browsing a weapon category (One-Handed, Two-Handed, or Shields), pressing 'D' did nothing. Now 'D' and 'S' (Sell) work from any weapon shop sub-menu.

## Day Tick No Longer Interrupts Shop Interactions

The daily reset message ("ENDLESS ADVENTURE CONTINUES!") could fire between inputs while browsing shops or mid-interaction, disrupting the UI flow. The daily reset logic still runs on schedule (restoring stats, processing events), but the banner display is now deferred to the next clean location-display boundary — typically when you return to a location's main screen. Manually triggered daily resets (from the settings menu) still display immediately.

## Expanded Onboarding Hints

Five new contextual hints help new players discover game systems:

- **Tax Info**: Explains the King's Tax (5%) and City Tax (2%) on your first shop purchase
- **Training Available**: After your first level-up, explains the Level Master and training points
- **Magic Tip**: If your class has mana but you haven't learned any spells yet, suggests visiting the Magic Shop
- **Quests Available**: After your first monster kill, points you to the Quest Hall for bounties
- **Getting Started**: A one-time summary after character creation showing the core gameplay loop (shops, dungeons, quests, training, healer)

All hints appear once per character and are stored in saves.

## Magician Spell Damage Scaling

Spell damage from Intelligence previously hit a hard cap at 4.0x (around INT 85 / level 15), after which additional INT had zero effect. Warriors with uncapped STR scaling would outpace Magicians in raw damage at higher levels, undermining the glass cannon identity.

Now uses a soft cap with diminishing returns:
- **Below INT 85**: Full scaling at 0.04 per point (unchanged, reaches 4.0x)
- **Above INT 85**: Diminished scaling at 0.015 per point (keeps growing, just slower)
- **Hard ceiling at 8.0x** (prevents truly extreme values)

This means a level 50 Magician now hits ~6.1x instead of being stuck at 4.0x, while mana constraints still limit total burst output. Magicians are the highest single-hit damage class; Warriors win on sustained DPS.

## Files Changed

- `GameConfig.cs` — Version 0.47.4
- `Scripts/Systems/RareEncounters.cs` — Arena portal rewritten: generates arena champion monster via MonsterGenerator, runs real CombatEngine.PlayerVsMonster combat, awards +25 Chivalry on victory, prevents permadeath (HP floored at 1)
- `Scripts/Locations/WeaponShopLocation.cs` — Added `case "D"` (DualWieldSetup) and `case "S"` (SellWeapon) to ProcessCategoryChoice so they work from any sub-menu; added tax hint trigger after first purchase
- `Scripts/Systems/DailySystemManager.cs` — Added `PendingDailyResetDisplay` flag; `PerformDailyReset()` now defers display for automatic resets (sets flag) while forced resets still display immediately; `DisplayDailyResetMessage()` made public
- `Scripts/Locations/BaseLocation.cs` — Added pending daily reset check at top of LocationLoop (before display); shows deferred banner then clears flag; added Level Master hint after auto-level-up
- `Scripts/Systems/HintSystem.cs` — Added 5 new hints: HINT_FIRST_PURCHASE_TAX, HINT_LEVEL_MASTER, HINT_MANA_SPELLS, HINT_QUEST_SYSTEM, HINT_GETTING_STARTED with definitions
- `Scripts/Locations/MainStreetLocation.cs` — Added mana/spell hint trigger (mana > 0, no spells learned) and quest hint trigger (level 1-3, has kills) in DisplayLocation
- `Scripts/Locations/ArmorShopLocation.cs` — Added tax hint trigger after first purchase
- `Scripts/Core/GameEngine.cs` — Added Getting Started hint display after opening story sequence for new characters (0 kills)
- `Scripts/Systems/StatEffectsSystem.cs` — Spell damage multiplier: replaced hard 4.0x cap with soft cap (full scaling to INT 85, diminished 0.015/point above, hard ceiling 8.0x)
- `usurper-reloaded.csproj` — Added `ApplicationIcon` property for Windows exe icon
- `app.ico` — Windows application icon (embedded in exe at build time)
