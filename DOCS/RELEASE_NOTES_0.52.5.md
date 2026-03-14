# Usurper Reborn v0.52.5 - Loot & Progression Overhaul

---

## Loot Drop Rate Buff

Base dungeon loot drop chance increased from **12% to 20%** per monster kill. The per-level scaling remains at +0.5% per monster level, and the drop cap has been raised from 45% to 50%. Players should now see roughly **double the loot** per floor compared to previous versions.

At level 25, expected drops per floor increase from ~4 items to ~7-8 items — enough to meaningfully gear both the player and their party members.

## Party Loot Bonus

Running with a party now directly increases loot drops. Each alive teammate (companions and NPC recruits) adds **+5% drop chance** per monster killed. A full party of 3 teammates grants +15% on top of the base rate, making group play the most rewarding way to gear up.

This stacks with the existing divine boon luck bonus and named monster bonuses.

## Teammate-Targeted Drops

When loot drops during party combat, there is now a **30% chance** the item will be generated for one of your teammates' classes instead of your own. This means your Paladin companion might see a holy sword drop, or your Magician teammate might get a staff — instead of everything being tuned to the player's class.

This dramatically reduces the tedium of gearing party members, since relevant equipment for them now drops naturally during dungeon runs.

## Shop Inventory Expansion

Town shops now generate items at **every 5 levels** instead of every 10, with up to **8 tiers per item template** (was 4). This eliminates the dead zones where shops had nothing new to offer. Previously, a player at level 26 would see the same level-20 items until level 30; now they'll find level-25 items available.

Combined with tightened shop rarity bands (Uncommon items now start at level 11 instead of 16, Rare at level 21 instead of 31), shops remain a viable gearing option deeper into the mid-game.

## Improved Dungeon Loot Rarity

The dungeon loot rarity curve has been significantly improved for the mid-game. At level 25:

| Rarity | Before | After |
|---|---|---|
| Common | ~55% | ~30% |
| Uncommon | ~30% | ~35% |
| Rare | ~12% | ~25% |
| Epic+ | ~3% | ~10% |

Players in the floor 20-40 range will find far fewer useless Common drops and significantly more Uncommon and Rare equipment worth keeping.

## Bard Class Overhaul

The Bard class received a comprehensive balance pass addressing a 32-level ability gap (levels 26-58) and overall lack of class identity in the mid-game.

### 5 New Abilities

| Level | Ability | Type | Effect |
|-------|---------|------|--------|
| 14 | Cutting Words | Attack+Debuff | CHA-scaled damage + strips 25% of target's DEF for the fight |
| 34 | Ballad of Blades | Attack | Strong CHA-scaled single-target (75 base damage) |
| 42 | Countercharm | Party Buff | Cleanse all negative status effects from self and teammates (requires Instrument) |
| 50 | Dissonant Whispers | Attack | CHA-scaled damage (65 base) + Fear (2 rounds) |
| 65 | War Drummer's Cadence | Party Buff | +45 ATK, +30 DEF to entire party for 5 rounds (requires Instrument) |

Bards now have 12 class abilities covering all level ranges instead of the previous 7 with a massive dead zone.

### Bardic Inspiration Passive

New class passive: **15% chance** after using any ability to inspire a random alive teammate, granting **+20 ATK for 2 rounds**. Displayed on `/health`. This is the Bard's equivalent of Alchemist's Potion Mastery, Magician's Arcane Mastery, and Jester's Trickster's Luck.

### Starting Weapon: Old Lute

New Bard characters now start with an **Old Lute** (Instrument) instead of a Dull Sword. Since 4 of the Bard's 12 abilities require an Instrument, this prevents the awkward situation where a new Bard couldn't use Inspiring Tune at level 10 without first visiting the Music Shop.

### Vicious Mockery Fix (Companions)

The -5 to hit "distract" effect from Vicious Mockery was only checked when monsters attacked the **player** — if a distracted monster targeted a **companion/teammate** instead, the penalty was completely ignored. Now distracted monsters have a 25% miss chance against companions, and the distraction is consumed regardless.

---

## Bug Fixes

### Teammate Combat Message Perspective Fix

When NPC teammates triggered enchantment effects (Lifedrinker, Phoenix Fire, Thunderstrike, Mana Siphon, Dark Power drain, etc.) or used party abilities (Bard songs, Alchemist party effects, Cleric divine abilities), combat messages incorrectly said "Your weapon drains..." or "You feel the stimulant..." instead of showing the teammate's name. Fixed across 6 methods: `ApplyPostHitEnchantments()`, `CheckElementalEnchantProcs()`, `ApplyPoisonEffectsOnHit()`, `ApplyBardSongToParty()`, `ApplyAlchemistPartyEffect()`, and `ApplyClericPartyEffect()` — all now accept an `isPlayer` parameter and display the correct actor name for ~25 combat messages.

### MUD Client Auth Fix (MUSHclient, Mudlet)

Fixed `ReadLineAsync` in the auth screen losing trailing `\n` bytes across calls. MUD clients send `\r\n` line endings — the old code returned on `\r` but left the `\n` in the stream buffer, causing the next ReadLineAsync call (username prompt) to immediately return empty and loop back to the menu. Fixed by consuming the trailing `\n` inline via `stream.DataAvailable` after reading `\r`. Also reverted server-side echo that was added for SyncTerm — it's unnecessary now that the `\r\n` handling is correct.

### Non-Caster Mana Potions

Non-mana classes (Warrior, Barbarian, Ranger, etc.) can now purchase mana potions from the Healer, dungeon monks, and dungeon merchants. Previously gated behind `MaxMana > 0` / `HasSpells()` checks. Non-casters can carry mana potions and give them to spellcaster teammates via the Aid Ally combat action.

### Steam Achievements in Online Multiplayer

Steam achievements now unlock when playing online via `[O]nline Play` from the Steam client. The server embeds invisible OSC markers (`ESC]99;ACH:id BEL`) in the terminal stream when achievements unlock. The client-side I/O relay intercepts these markers, strips them from display output, and calls `SteamIntegration.UnlockAchievement()` on the player's local Steam client. Players who earned achievements via web/SSH will get them synced next time they connect from Steam.

### Comprehensive Loot Thematic Stats Overhaul

All three loot theming systems have been expanded so items with themed names grant stats that match their theme. Armor, weapon, and accessory theming each gained ~10-15 new keyword groups covering martial/agile (gi, duelist, precision), healing, crafted materials (wooden, bone, copper, gold, silver), runed/ancient, light/darkness, holy/shadow, elemental (fire/ice/lightning), precision/agility weapons (rapier, stiletto, dagger, keen), dragon-themed, crusher/destroyer, and more.

Accessories with effect suffixes like "Silver Ring of the Arcane" were themed based on the base template name ("Silver Ring") instead of the full name. Now uses the final item name (including effect suffixes), so "of the Arcane" correctly matches the mage keyword group and grants INT+WIS.

The "gi" keyword (for martial arts gi armor) used `name.Contains("gi")` which falsely matched inside "leggings", "girdle", "magic", etc. Now uses word-boundary matching so only actual gi items receive martial/agile bonuses.

### Equipment Stat Display Missing Cha/Wis/Agi/Sta

Equipment with Charisma, Wisdom, Agility, Stamina, or Defence bonuses didn't show those stats in numerous display locations throughout the game. The stats were correctly applied to the character, but the display made it look like they were silently dropped. Comprehensive audit found and fixed **10 display locations**:

- Equipped items list (`/health`) — missing Agi, Cha, Def, Sta in totals
- Equipment `GetDisplayString()` — missing Agi, Con, Int, Cha, Def, Sta
- Inventory comparison screen — missing Agi, Cha, Def, Sta in summary; missing Agi, Sta in totals
- Dungeon loot drop display — missing Agi, Cha, Sta
- Dungeon loot accessory comparison — asymmetric stat totals (new item missing Agi, Cha, HP, Mana, Sta)
- Weapon shop — missing Sta; Cha not localized
- Armor shop — missing Sta
- Magic Shop enchantment list — missing Agi, Cha, HP, Sta
- Magic Shop compact accessory display — missing Agi, Cha, Sta
- Magic Shop detailed accessory stats — missing Sta
- Magic Shop accessory score — missing Sta

### Dead NPC Quest Auto-Completion

Quests targeting permadead NPCs (bounties, rescue missions) are now automatically completed with gold rewards on login. Previously, if a quest target NPC died while the player was offline, the quest became permanently stuck — the player would search every location and never find them. Now on login, any active quest whose target NPC is dead is auto-completed with a message explaining what happened and the quest reward is paid out.

### Cutting Words Debuff Not Applied

The Bard's Cutting Words ability (level 14) dealt CHA-scaled damage but its "weaken" debuff was completely non-functional — the `case "weaken"` handler was missing from both the single-monster and multi-monster ability effect switches. The effect only existed in the PvP spell handler, which abilities don't use. Additionally, monsters don't support the `StatusEffect` system used by the original implementation. Now applies a direct 25% defense reduction to the target monster for the remainder of the fight, with a visible message showing the DEF lost.

### Expedition Quest Free Reward Exploit

Expedition quests ("Reach Floor X") generated target floors within the player's already-accessible range, meaning a player who had already cleared floor 15 could get a quest to reach floor 10 and collect free gold by simply walking in. Target floors now always exceed the player's deepest cleared floor. The kill objective (defeat monsters on the expedition) is now mandatory instead of optional, requiring actual combat even if the floor was previously visited.

### Memory Puzzle Trivially Solvable

The dungeon memory puzzle showed a sequence of symbols, waited 3 seconds, then asked the player to type the full sequence back. Players could simply scroll up in their terminal to see the answer, making it trivial. Reworked to ask 2 random position questions (e.g., "What was the 3rd symbol?") after clearing the screen, which can't be cheated via scrollback. Display time now scales with sequence length (2s + 0.5s per symbol).

### Old God Save Quest Broken (Aurelion and others)

Multiple interlocking bugs prevented completing save quests for awakened Old Gods:

1. **`Awakened` treated as "resolved"**: When returning to a boss room, the `Awakened` god status was included in the "already dealt with" check, causing the room to show "The chamber lies empty" instead of triggering the save quest completion. Removed `Awakened` from the resolved status list.

2. **Save quest reminder hardcoded wrong artifact**: The dungeon entry reminder for all awakened gods checked for the Soulweaver's Loom (Veloura's artifact) regardless of which god was awakened. Now checks the correct artifact per god (Soulweaver's Loom for Veloura, Sunforged Blade for Aurelion).

3. **`CanSave` combat check hardcoded wrong artifact**: The boss combat context's `CanSave` flag checked for `SoulweaversLoom` for all gods instead of using the per-god artifact lookup. Now uses `GetArtifactForSave()`.

4. **No Sunforged Blade discovery event**: Unlike Veloura's Loom (discoverable on floor 65), the Sunforged Blade had no discovery event, making it impossible to obtain for Aurelion's save quest. Added floor 90 discovery event with narrative and artifact grant.

5. **No Aurelion save quest return handler**: Unlike Veloura (floor 40 return handler), there was no floor 85 return handler for completing Aurelion's save quest when the player returns with the Sunforged Blade. Added floor 85 save quest return with narrative and quest completion.

### Faction Ambush Death Not Applied

Losing a faction ambush fight (PvP combat during travel) didn't apply death penalties — the player continued traveling to their destination with 0 HP, then died again when entering the next location. The PvP combat engine (`DeterminePvPOutcome`) sets `CombatOutcome.PlayerDied` but doesn't call `HandlePlayerDeath()` like monster combat does. The ambush handler assumed death was already handled. Now explicitly calls death handling after PvP loss, and redirects the player to the Inn instead of continuing to the destination.

---

## Files Changed

- `Scripts/Core/GameEngine.cs` -- `CleanupDeadNPCQuests()` auto-completes quests targeting permadead NPCs on login with gold reward; called after NPC restoration in `LoadSaveByFileName()`
- `Scripts/Core/GameConfig.cs` -- Version 0.52.5; 5 new loot progression constants (LootBaseDropChance, LootLevelDropScale, LootMaxDropChance, LootPartyBonusPerMember, LootTeammateTargetChance); 3 Bard passive constants (BardInspirationChance, BardInspirationAttackBonus, BardInspirationDuration)
- `Scripts/Core/Items.cs` -- `GetDisplayString()` now shows Agi, Con, Int, Cha, Def, Sta bonuses (were missing)
- `Scripts/Systems/CharacterCreationSystem.cs` -- Bard starting weapon changed from Dull Sword to Old Lute (Instrument)
- `Scripts/Locations/BaseLocation.cs` -- Bardic Inspiration passive display in /health; `GetEquipmentStatSummary()` now shows Agi, Wis, Cha, Def, Sta bonuses; `DisplayEquipmentTotals()` now shows Agi, Cha, Def, Sta; faction ambush death now calls `HandlePlayerDeathPublic()` and redirects to Inn instead of continuing travel
- `Scripts/Systems/InventorySystem.cs` -- `GetItemStatSummary()` now shows Agi, Cha, Def, Sta; `DisplayStatsSummary()` now shows Agi, Sta in totals
- `Scripts/Systems/CombatEngine.cs` -- `HandlePlayerDeathPublic()` public wrapper for PvP death handling; Drop chance uses new GameConfig constants; party loot bonus (+5% per alive teammate); teammate-targeted drops (30% chance to generate for teammate's class); monk mana potion purchase unlocked for non-casters; Bardic Inspiration passive (15% proc after ability use); `ApplyBardCountercharm()` party cleanse; `CleanseCombatStatuses()` helper; Vicious Mockery distract fix in `MonsterAttacksCompanion()` (25% miss chance); loot drop display now shows Agi, Cha, Sta; accessory comparison now symmetric (both sides include all stats); teammate combat message perspective fix across `ApplyPostHitEnchantments()`, `CheckElementalEnchantProcs()`, `ApplyPoisonEffectsOnHit()`, `ApplyBardSongToParty()`, `ApplyAlchemistPartyEffect()`, `ApplyClericPartyEffect()`, and multi-monster ability effect announcements (~25 messages now show teammate name instead of "You/Your"); added `case "weaken"` to both ability effect switches (was completely missing — Cutting Words debuff never applied); weaken reduces monster DEF by 25% for the fight
- `Scripts/Locations/WeaponShopLocation.cs` -- Weapon shop stat display now shows Sta bonus and localized Cha
- `Scripts/Locations/ArmorShopLocation.cs` -- Armor shop stat display now shows Sta bonus
- `Scripts/Locations/MagicShopLocation.cs` -- Enchantment equipment list now shows Agi, Cha, HP, Sta; compact accessory display now shows Agi, Cha, Sta; detailed accessory stats now shows Sta; accessory score includes Sta
- `Scripts/Systems/ShopItemGenerator.cs` -- LevelInterval 10→5; MaxItemsPerTemplate 4→8; shop rarity bands tightened (Common ≤10, Uncommon ≤20, Rare ≤35, Epic ≤55, Legendary ≤75, Artifact 76+)
- `Scripts/Systems/LootGenerator.cs` -- RollRarity() reworked: higher base Uncommon/Rare chances, reduced Common weight at mid-levels; Artifact chance increased at high levels; comprehensive theming overhaul across all 3 theming methods with ~25 new keyword groups; accessory theming uses final item name with effect suffixes; "gi" keyword uses `EndsWith(" gi")` to prevent false matches on "leggings"/"girdle"/"magic"; merged duplicate caster groups
- `Tests/ThematicBonusTests.cs` -- New martial/agile and wooden template tests; "gi" substring false-match regression test; updated test data for expanded keyword groups
- `Scripts/Server/MudServer.cs` -- ReadLineAsync `\r\n` fix: consumes trailing `\n` via DataAvailable after `\r`; reverted echo/maskChar parameters from auth calls
- `Scripts/Systems/AchievementSystem.cs` -- TryUnlock sends OSC achievement marker through session terminal in online mode for Steam client sync
- `Scripts/Systems/OnlinePlaySystem.cs` -- `InterceptAchievementMarkers()` scans PipeIO stream for OSC `ACH:` markers, calls SteamIntegration.UnlockAchievement(), strips markers from display
- `Scripts/Systems/ClassAbilitySystem.cs` -- 5 new Bard abilities: Cutting Words (lv14, -25% DEF), Ballad of Blades (lv34, damage), Countercharm (lv42, party cleanse), Dissonant Whispers (lv50, fear), War Drummer's Cadence (lv65, party buff); Cutting Words description updated to reflect actual mechanic
- `Scripts/Systems/QuestSystem.cs` -- Expedition quests now target floors beyond player's deepest cleared floor; kill objective mandatory instead of optional (3 + difficulty*2 kills required); `RefreshBountyBoard()` and `CreateDungeonQuest()` accept `deepestFloor` parameter
- `Scripts/Systems/DailySystemManager.cs` -- Passes `DeepestDungeonLevel` to `RefreshBountyBoard()`
- `Scripts/Locations/DungeonLocation.cs` -- Passes `DeepestDungeonLevel` to `RefreshBountyBoard()`; memory puzzle reworked: asks 2 random position questions instead of full sequence recall; display time scales with sequence length; removed `HasSpells()` gate from dungeon merchant mana potion purchase; Old God save quest fixes: removed `Awakened` from resolved status check (was showing "chamber empty" for in-progress save quests); save quest reminder now checks correct per-god artifact instead of hardcoded SoulweaversLoom; added floor 90 Sunforged Blade discovery event for Aurelion save quest; added floor 85 save quest return handler; added floor 90 hint text
- `Scripts/Systems/OldGodBossSystem.cs` -- `CanSave` boss combat flag now uses `GetArtifactForSave()` per god instead of hardcoded SoulweaversLoom check
- `Scripts/Locations/HealerLocation.cs` -- Removed `MaxMana <= 0` class gate from BuyManaPotions
