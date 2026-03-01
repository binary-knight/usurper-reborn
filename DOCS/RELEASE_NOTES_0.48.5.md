# Usurper Reborn v0.48.5 — Combat Balance, Day Cycle & Dreams of the Deep

## Power Strike Reworked

Power Strike was strictly worse than a regular attack for dual-wielders — the 1.5x damage multiplier was eaten by a 25% defense penalty, and you lost your off-hand attack entirely. Now:

- **Damage increased from 1.5x to 1.75x** on the main-hand hit
- **Defense penalty removed** — stamina cost is the tradeoff, not reduced accuracy
- **Off-hand follow-up attack** — dual-wielders now swing their off-hand after the empowered main hit, so you're no longer punished for using an ability
- **Enchantment procs apply** — lifesteal, elemental effects, and poison coating now trigger on Power Strike hits (single-monster path was missing these)

Both single-monster and multi-monster combat paths updated consistently.

## Two-Handed Weapon Buff

Two-handed weapons were strictly worse than dual-wielding: 1.25x single hit vs 1.0x + 0.5x = 1.5x total from dual-wield. The damage modifier has been increased from **1.25x to 1.45x**. Combined with the fact that defense is only subtracted once (vs twice for dual-wield), and Power Strike stacks multiplicatively (1.75 x 1.45 = ~2.54x), two-handed weapons are now a competitive choice for big single-hit builds.

## Shield Block Now Halves Damage

Shield blocking was cosmetic — on a successful block (20% chance), it added the shield's bonus AC (5-20 points) to defense, which was negligible against hundreds of incoming damage. Shield block now **halves the final damage** after all defense calculations. A successful block against a 200-damage hit now absorbs 100 damage instead of adding 15 to your defense stat. The block chance (20%) and trigger conditions are unchanged.

## Single-Player Time-of-Day System

Single-player mode now features a full day/night cycle. Your actions advance an in-game clock through **Dawn, Morning, Afternoon, Evening, and Night** — and you can only rest for the night once evening falls.

- **New games start at 8:00 AM**. After resting, the clock resets to 6:00 AM the next day.
- **Every action** advances the clock by 10 minutes. Traveling between locations costs 20 extra minutes. Each combat round takes 3 minutes. Exploring a dungeon room takes 10 minutes.
- **Location headers** show the current time period (color-coded by time of day). Atmospheric messages announce transitions between periods ("The sky turns to gold and crimson as evening approaches.").
- **Dungeon time is hidden** — you lose track underground — but the clock still advances.
- **Rest is gated by time**: You can only rest for the night (advancing the day) at **8 PM or later**. The Inn's quick table rest (small heal, no day advance) is always available regardless of time.
- **[Z] Wait** option at the Inn and Home lets you fast-forward to nightfall without healing or advancing the day. The world continues to simulate while you wait.
- **World simulation** now ticks on hour boundaries instead of every 5 keypresses. NPCs train in the morning, shop in the afternoon, socialize in the evening, and sleep at night — all synced to your game clock. When you sleep, the world advances through each hour of the night.

| Period    | Hours | Header Color |
|-----------|-------|-------------|
| Dawn      | 5-6 AM  | Bright Yellow |
| Morning   | 7-11 AM | Yellow |
| Afternoon | 12-4 PM | White |
| Evening   | 5-8 PM  | Bright Red |
| Night     | 9 PM-4 AM | Dark Cyan |

Online mode is completely unaffected — all time-of-day features are gated to single-player only.

## Rest as Narrative Gateway — Dreams of the Deep

Resting is no longer just a heal button. Dreams now fire roughly every other rest (up from ~1 in 4), making sleep a central part of the story experience. Old Gods speak through your dreams, companions reveal hidden depths, and the game's deepest mysteries unfold while you sleep. **40 narrative dreams** (up from 20) and **15 dungeon visions** (up from 5).

### Old God Dream Communication (5 new dreams)

Each Old God now visits your dreams after you encounter them, responding differently based on whether you defeated, saved, or allied with them:

- **Veloura** — If saved, a white rose blooms and she thanks you for proving love still exists. If defeated, the garden turns to ash: "Was there another way?"
- **Thorgrim** — His empty courtroom echoes. "Perhaps mercy was the law I forgot."
- **Noctura** — If allied, she stops hiding: "I chose you because you'd understand." If defeated, the shadows go quiet — you didn't realize how much you'd miss being watched.
- **Aurelion** — If saved, his warm light says "You took the burden of truth. Nobody asked you to." If defeated, the last candle goes out.

### Companion Campfire Dreams (4 new dreams)

When a companion is alive and traveling with you, they appear in your dreams as people, not plot devices:

- **Lyris** at the fire: "I've seen this before. You, sleeping. Me, watching. But from the other side."
- **Aldric** keeping watch, talking to the ghosts of his dead soldiers: "I won't let it happen again."
- **Mira** healing a wound that doesn't exist: "If you could save everyone, but it cost you everything... would you?"
- **Vex** at dawn, not joking for once: "You know I'm dying, right? Don't be sad about it. I got more than I deserved."

### Story Milestone Dreams (8 new dreams)

Dreams now react to what you've accomplished: first kill, becoming king, marriage, high darkness (3000+), high chivalry (3000+), reaching dungeon floor 50+, collecting all seven seals, and completing a companion's personal quest.

### Expanded Dungeon Visions (10 new, 15 total)

Three times as many environmental narrative moments spread across all floor ranges — from a skeleton kneeling in prayer (floors 5-15) to the dungeon's walls pulsing with a heartbeat that matches yours (floors 90-100). Several award awakening points or wave fragments.

### Atmosphere Dreams (3 new)

General mood dreams that fill the gaps: a warm memory of the Inn (levels 5-25), an endless road where every traveler has your face (levels 25-50), and a throne room with seven empty chairs — one with your name carved into ancient wood (levels 50-80).

## Herb Garden Overhaul — 5 Unique Herbs with Combat Buffs

The herb garden at Home was a useless duplicate of healing potions. It's now a full crafting-lite system with **5 unique herb types**, each gated by garden upgrade level:

| Herb | Garden Level | Effect | Carry Limit |
|------|-------------|--------|-------------|
| **Healing Herb** | 1 | Instant 25% MaxHP heal | 10 |
| **Ironbark Root** | 2 | +15% defense for 5 combats | 5 |
| **Firebloom Petal** | 3 | +15% damage for 5 combats | 5 |
| **Swiftthistle** | 4 | +1 extra attack/round for 3 combats | 3 |
| **Starbloom Essence** | 5 | +20% spell damage & 30% mana restore for 5 combats | 3 |

### Herb Pouch — Use Herbs Anywhere

Herbs are carried in a portable **herb pouch** accessible from:

- **Combat**: `[J] Herb Pouch` action in both single-monster and multi-monster combat menus (costs your turn for the round)
- **Dungeon exploration**: `[J] Herbs` option in the between-combat room menu
- **Any location**: `/herb` or `/j` quick command from any town location
- **Home**: `[J] Use Herb` in the home menu

Buff herbs (Ironbark, Firebloom, Swiftthistle, Starbloom) last a set number of combats and tick down each fight. Only one herb buff can be active at a time — using a new buff herb replaces the current one.

### Gathering Redesigned

The garden's `[G] Gather Herbs` menu now shows all unlocked herb types with carry limits. You choose which herb to gather instead of getting generic potions. Higher-tier herbs unlock at higher garden levels, giving real incentive to upgrade.

## Bug Fixes

- **Quest gold rewards were wildly overtuned**: Multipliers reduced from 1100/5100/11000 to 150/500/1000 per player level. A level 10 player no longer earns 110,000 gold from a single quest.
- **Bank robbery was simulated, not interactive**: Bank heist used a hardcoded power comparison with no player input. Now generates real guard monsters and runs full interactive CombatEngine combat.
- **Quests completed counter reset on every save/load**: `Character.Quests` and `Character.RoyQuests` were never serialized — added to SaveDataStructures, SaveSystem, and GameEngine restore.
- **Dead NPCs appeared at locations**: `GetLiveNPCsAtLocation()` checked `IsAlive` (HP > 0) but not `IsDead` (permanent death flag). Now checks both.
- **Herb gathering ignored potion cap**: Could gather herbs beyond `MaxHealingPotions`. Now checks available space before gathering.
- **Big Spender achievement counted cancelled purchases**: Cancelling a weapon purchase after gold was deducted didn't reverse the statistics. Now undoes `TotalGoldSpent` and `TotalItemsBought` on cancel.
- **Online mode difficulty exploit**: All players inherited Easy mode (1.5x XP/Gold) from legacy character creation. Defense-in-depth: online login now forces Normal difficulty. Database patched.
- **Playtime inflated on every save**: `UpdateSessionTime()` kept resetting `SessionStart`, causing each save to re-add the full session. Now accumulates incrementally since last save.
- **Non-caster classes started with mana**: Mana calculation in character creation applied Intelligence bonus to all classes. Now only grants mana to classes with `MaxMana > 0`.
- **Home description was static**: Descriptions didn't reflect upgrade levels. Now dynamically describes each upgrade tier.
- **Crypt Keeper info was static**: The dungeon Crypt Keeper encounter always said "Seek the third level" regardless of player floor. Now gives dynamic hints about nearby seal floors, Old God locations, and depth-appropriate advice.
- **Dark alignment shadow whisper was static and unscaled**: The shadowy figure event always said "Seek the third level" and gave a flat 50 XP. Now gives floor-aware hints (nearby seals, Old Gods, depth advice) and XP scales with level (`max(50, level × 10)`).
- **Riddle gates gave double XP rewards**: `PresentRiddle()` awarded 50-100 XP on solve, then both `RiddleGateEncounter` and `RiddleEncounter` awarded their own scaled XP on top. Removed the duplicate reward from `PresentRiddle()` — the caller's scaled reward is the intended one.
- **Dungeon poison ticked on invalid keys and double-ticked with guide**: BaseLocation applied poison on every keypress, so pressing X in an empty room or using the dungeon guide (which already ticks per room) caused extra damage. Dungeon now suppresses the base poison tick and handles it only on room movement (N/S/E/W and guide navigation).
- **Poison didn't tick during combat**: The `player.Poison` counter (from traps/monster attacks) never dealt damage during combat rounds. `StatusEffect.Poisoned` (from PvP spells) ticked, but the integer poison system didn't. Now ticks each round using the same formula as the exploration poison tick.
- **"Talk to NPCs" worked in dungeons**: Pressing `0` or typing `talk` in the dungeon opened the NPC talk menu despite no NPCs being present. Now disabled in the dungeon.
- **Quest gold rewards not counted in session statistics**: Gold from quest completion never called `RecordQuestGoldReward()`, so `TotalGoldEarned` and `TotalGoldFromQuests` were never updated. Session summary "Gold Earned" now includes quest rewards.
- **NPC prison sentences expired in ~90 seconds instead of days**: `PrisonActivitySystem.ProcessAllPrisonerActivities()` decremented `DaysInPrison` every world sim tick (30s), so a 3-day sentence lasted only 3 ticks. Prison day countdown now runs once per game day via `DailySystemManager`, matching how player sentences already worked.
- **NPC challenges generated duplicate news entries**: `ExecuteChallenge()` called `RecordWitnesses()` which auto-generated a news entry, then immediately generated its own second news entry for the same duel. The witness news also swapped challenger/target names with winner/loser, creating contradictory reports. Now suppresses the witness auto-news and only generates the explicit challenge result news.
- **NPC brawls generated mirror-image duplicate news**: When two NPCs had each other in their enemy lists, the escalation loop processed both A→B and B→A as separate brawls in the same tick, producing two news entries with swapped names. Now tracks processed pairs per tick to prevent duplicates.
- **Missing person quests completable without dungeon exploration**: The only required objective was `TalkToNPC` — completable by bumping into the NPC in town. Dungeon exploration objectives were optional. Now `ReachDungeonFloor` is the required objective (forces actual investigation), `TalkToNPC` is optional (bonus for finding them alive), and reaching the target floor auto-completes `TalkToNPC` if the NPC is dead.

---

## Files Changed

- `GameConfig.cs` — Version 0.48.5; time-of-day constants; herb constants (HerbHealPercent, HerbDefenseBonus, HerbDamageBonus, HerbExtraAttackCount, HerbSwiftDuration, HerbBuffDuration, HerbManaRestorePercent, HerbSpellBonus, HerbMaxCarry array)
- `Scripts/Core/Character.cs` — Added `GameTimeMinutes`; `HerbType` enum with 6 values; `HerbData` static class (GetName/GetDescription/GetColor/GetGardenLevelRequired); 5 herb inventory properties; 4 herb buff tracking properties; helper methods (GetHerbCount, ConsumeHerb, AddHerb, TotalHerbCount, HasActiveHerbBuff)
- `Scripts/Core/GameEngine.cs` — Restore `GameTimeMinutes`; force Normal difficulty on online login; restore quest counters (Quests, RoyQuests); restore 9 herb fields
- `Scripts/Core/NPC.cs` — `GetWorldState()` uses `GetCurrentGameHour()`
- `Scripts/Core/Quest.cs` — Quest gold reward multipliers reduced (1100→150, 5100→500, 11000→1000)
- `Scripts/Locations/BankLocation.cs` — Bank robbery uses real CombatEngine combat; removed duplicate [Q] menu option
- `Scripts/Locations/BaseLocation.cs` — Time period display; `AdvanceGameTime()` on actions; dead NPC filter fix (`!npc.IsDead`); `/herb` and `/j` quick commands; herb help entry; `SuppressBasePoisonTick` virtual property; skip Talk to NPC in dungeon
- `Scripts/Locations/DungeonLocation.cs` — Time advancement on room exploration; `[J] Herbs` in dungeon room menu; `using UsurperRemake.Locations` for HomeLocation access; `SuppressBasePoisonTick => true`; poison tick on room movement (N/S/E/W)
- `Scripts/Locations/HomeLocation.cs` — Rest gated on `CanRestForNight()`; `[Z] Wait until nightfall`; complete `GatherHerbs()` rewrite with herb type selection; `UseHerbMenu()` and `ApplyHerbEffect()` static methods (shared by all locations); `[J] Use Herb` menu option; dynamic home descriptions; herb garden upgrade display shows newly unlocked herb
- `Scripts/Locations/InnLocation.cs` — Rest gated on `CanRestForNight()`; `[Z] Wait until nightfall`
- `Scripts/Locations/MainStreetLocation.cs` — Preferences updated for time-of-day
- `Scripts/Locations/WeaponShopLocation.cs` — Reverse statistics on cancelled purchases (TotalGoldSpent, TotalItemsBought)
- `Scripts/Systems/CharacterCreationSystem.cs` — Mana only granted to classes with MaxMana > 0
- `Scripts/Systems/CombatEngine.cs` — Power Strike 1.75x + off-hand + enchantments; 2H 1.45x; shield block halves damage; time advancement; `[J] Herb Pouch` in all 6 combat menu variants (BBS/ScreenReader/Standard x single/multi); `UseHerb` action type and input parsing in both combat paths; `ExecuteUseHerb()` method; Firebloom +15% damage bonus in both attack paths; Ironbark +15% defense bonus; Swiftthistle extra attacks in `GetAttackCount()`; herb buff tick-down in both combat paths with cleanup on expiry; `player.Poison` tick per combat round
- `Scripts/Systems/DailySystemManager.cs` — `GameTimePeriod` enum; time-of-day system; `GetCurrentGameHour()` static
- `Scripts/Systems/DreamSystem.cs` — 20 new dreams; 10 new dungeon visions; 11 new requirement fields; reduced dream cooldown
- `Scripts/Systems/FeatureInteractionSystem.cs` — (unchanged from prior commit)
- `Scripts/Systems/LocationManager.cs` — Travel time advancement
- `Scripts/Systems/SaveDataStructures.cs` — Added `GameTimeMinutes`, `Quests`, `RoyQuests`, and 9 herb fields (HerbHealing, HerbIronbark, HerbFirebloom, HerbSwiftthistle, HerbStarbloom, HerbBuffType, HerbBuffCombats, HerbBuffValue, HerbExtraAttacks)
- `Scripts/Systems/SaveSystem.cs` — Serialize/restore time, quest counters, and herb fields
- `Scripts/Systems/SpellSystem.cs` — Starbloom Essence herb +20% spell damage bonus in `ScaleSpellEffect()`
- `Scripts/Systems/StatisticsSystem.cs` — Incremental playtime accumulation (fixes inflation on repeated saves); new `RecordQuestGoldReward()` method increments `TotalGoldEarned` and `TotalGoldFromQuests`
- `Scripts/Systems/QuestSystem.cs` — `ApplyQuestReward()` calls `RecordQuestGoldReward()` for gold rewards
- `Scripts/Systems/AlignmentSystem.cs` — Dark alignment shadow whisper: dynamic floor-aware hints replacing static text; XP scales with level
- `Scripts/Systems/RareEncounters.cs` — Crypt Keeper info: dynamic hints about nearby seals/Old Gods replacing static "noble's tomb" text
- `Scripts/Data/RiddleDatabase.cs` — Removed duplicate XP award from `PresentRiddle()` `DisplaySuccess()` (callers handle scaled rewards)
- `Scripts/Core/Quest.cs` — Removed RescueNPC special alternative completion path from `AreAllObjectivesComplete()` (dead NPC case now handled by auto-complete on floor reach)
- `Scripts/Systems/NPCPetitionSystem.cs` — Missing person quest restructured: `ReachDungeonFloor` now required, `TalkToNPC` now optional
- `Scripts/Systems/PrisonActivitySystem.cs` — Split `ProcessAllPrisonerActivities()` into per-tick activities and `ProcessDailyPrisonCountdown()` for once-per-day sentence decrement
- `Scripts/Systems/WorldSimulator.cs` — Challenge witness news suppressed (was generating duplicate entries with swapped names)
- `Scripts/Systems/SocialInfluenceSystem.cs` — Added `suppressNews` parameter to `RecordWitnesses()` to prevent duplicate news from callers that generate their own
- `Scripts/Systems/WorldSimulator.cs` — `ApplyTimeOfDayWeights()` uses `GetCurrentGameHour()`
