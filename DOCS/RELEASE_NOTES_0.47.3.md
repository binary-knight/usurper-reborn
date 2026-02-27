# Usurper Reborn v0.47.3 — Onboarding, All-Slot Dungeon Loot & Balance Fixes

## New Player Onboarding Improvements

Retention audit: 33 of 102 players (32%) never killed a single monster — they bounced off the UI before engaging with combat. Four changes address this:

### Progressive Menu Disclosure

Main Street now shows fewer options to new players, expanding as they level up:

- **Level 1-2 (Tier 1)**: Core combat loop — Dungeons, Inn, Weapon/Armor/Magic shops, Healer, Quest Hall, Level Master, Quit, Settings
- **Level 3-4 (Tier 2)**: Town services added — Bank, Temple, Castle, Home, News, Fame
- **Level 5+ (Tier 3)**: Full menu — Dark Alley, Love Street, Old Church, Auction, Challenges, Lodging, Stats, Progress, Relations

All keys still work at every level — experienced players creating new characters can type hidden keys. Only the display is gated, not functionality.

### Dungeon Guidance Banner

Level 1 players with zero kills see a bright banner: "New adventurer? Press [D] to enter the Dungeons!" The banner vanishes naturally after the first kill.

### Reduced Death Penalties for New Players

Death was devastating at low levels (50-75% gold loss could wipe a level 1 player). Now tiered by level:

- **Level 1-3**: 5% XP loss, 15% gold loss, no item loss
- **Level 4-5**: 10% XP loss, 30% gold loss, no item loss
- **Level 6+**: Unchanged (10-20% XP, 50-75% gold, 20% item loss chance)

Crown tax exemption still applies at all tiers.

### First Kill Bonus

Killing your very first monster awards a "FIRST BLOOD!" celebration with +500 bonus gold — enough to buy a decent starter weapon.

## Automatic Level-Up After Combat

Leveling up no longer requires manually visiting the Level Master. When you earn enough XP from any source — combat, quests, seals, dungeon events, town encounters — you automatically level up with proper class-based stat increases, alignment bonuses, and training points. A "LEVEL UP!" message appears immediately when it happens. The Level Master still exists for spending training points on skill proficiencies and using the crystal ball.

This also fixes grouped (party) combat level-ups, which previously gave random stat bonuses instead of the correct class-based increases.

Main Street now shows a training points reminder ("You have X training points!") when applicable.

## PvP Arena: AI Defenders Now Use Class Abilities

PvP arena defenders were only using basic attacks for 8 of 11 classes (all except Magician, Sage, and Cleric). Warriors, Assassins, Barbarians, Rangers, Paladins, Bards, Jesters, and Alchemists now use their full combat ability repertoire when defending in PvP — including Backstab, Power Attack, Rage, and more. AI defenders have a 50% chance per turn to use their strongest available ability instead of a basic attack, with proper stamina costs and cooldown tracking across rounds.

## PvP: Backstab, Power Attack, and Precise Strike Unblocked

Player-initiated Backstab, Power Attack, and Precise Strike actions were explicitly blocked in PvP with a "not available in PvP" message. These actions now route through the class ability system, executing the corresponding ability (with stamina cost and cooldown) just like in PvE combat. If the player doesn't have the matching ability learned, they fall back to a basic attack with a message.

## Treasury Income Fix

The King's daily treasury income was bugged when the NPC spawn system hadn't loaded yet — the NPC count defaulted to 10, producing only 400 gold/day against 1,960+ gold in guard salaries and expenses. This caused newly crowned monarchs to immediately go bankrupt. The fallback now uses the population floor (45 NPCs minimum), guaranteeing at least 1,800 gold/day of income even during startup.

## NPC Race Extinction Prevention

With 125 of 185 NPCs permadead, 5+ races had zero living NPCs (Human, Troll, Gnome, Hobbit, Mutant). The lifecycle system (children growing up) can't create races with no living parents, so extinct races stayed extinct forever.

Two new systems fix this:

- **Race Permadeath Floor**: Permadeath rolls are now blocked if the NPC's race has 3 or fewer living members. This prevents any race from going completely extinct through permadeath.

- **NPC Immigration**: Each world sim tick, any race with fewer than 2 alive NPCs receives immigrant NPCs (1 male, 1 female) — new travelers arriving in town with random classes, ages 20-35, and levels scaled to the server average. Immigration news posts to the feed: "A Human traveler named Aldric Ashford has arrived in town."

## Playtime Tracking Fallback

Most players showed 0 minutes of total playtime because the playtime update only fired through `OnlineStateManager.Shutdown()`, which requires the online state to be fully initialized. If the state manager was null during disconnect (unclean disconnects, early exits), playtime was never recorded. A fallback now calls `UpdatePlayerSession` directly when the state manager is unavailable.

## Instant Disconnect Detection

Disconnecting from the online server (quitting, server restart, network drop) previously required pressing Enter multiple times. Now the disconnect is detected instantly and the client returns after a brief 1.5-second message — no keypresses needed. The relay loop uses blocking reads instead of polling, and streams are explicitly closed to unblock in-progress reads.

## Dungeon Loot: All-Slot Armor Drops

Dungeon loot previously only dropped weapons (55%) and body armor (45%) from regular monsters. Mini-bosses added rings and necklaces, but 8 armor slots — head, arms, hands, legs, feet, waist, face, and cloak — never dropped from any source. Players had to buy all non-body armor from shops.

Now every armor slot can drop from dungeon combat:

### Regular Monster Drops
Changed from 55% weapon / 45% body armor to **45% weapon / 55% armor** distributed across all slots:
- Body 25%, Head 12%, Arms 9%, Hands 9%, Legs 9%, Feet 9%, Waist 7%, Cloak 7%, Face 5%, Ring 4%, Necklace 4%

### Mini-Boss & Boss Drops
Mini-boss and boss loot now uses the same all-slot distribution instead of separate weapon/body/ring/necklace branches:
- **Mini-boss**: 35% weapon, 65% armor (all slots)
- **Boss**: 40% weapon, 60% armor (all slots)

Rings and necklaces are still weighted at 4% each within the armor pool, so they remain rare finds — but now head, arms, hands, legs, feet, waist, face, and cloak gear also have a chance to drop.

### 93 New Armor Templates

Each armor slot has its own template progression covering levels 1-100:
- **Head** (8 templates): Leather Cap → Crown of Ages
- **Arms** (8 templates): Leather Bracers → Astral Vambraces
- **Hands** (7 templates): Cloth Gloves → Ethereal Gauntlets
- **Legs** (7 templates): Leather Leggings → Celestial Greaves
- **Feet** (7 templates): Sandals → Boots of the Planes
- **Waist** (7 templates): Rope Belt → Cosmic Girdle
- **Face** (7 templates): Cloth Mask → Veil of the Beyond
- **Cloak** (7 templates): Traveler's Cloak → Cloak of Realities

Templates include class-specific drops (Assassin gloves, Paladin helms, Magician cloaks, etc.) mixed with universal items available to all classes. Each template follows the existing rarity/enchantment system — drops can roll Common through Legendary with random stat bonuses.

### Ring Slot Support

All 26 rings in the database use the left finger slot, but the right finger slot was empty. Rather than duplicating ring data, the game's equip logic already handles this: when you equip a ring and your left finger is occupied, it automatically goes to your right finger. This means dungeon ring drops and shop purchases both work for two-ring builds.

## Thundering Roar: AoE Taunt for Tank Classes

Tank classes now have a proper taunt mechanic. Previously, the `T` (Taunt) combat action only reduced a single enemy's defense — it never forced targeting. Now taunting actually works:

### New Ability: Thundering Roar
- **Classes**: Warrior, Paladin, Barbarian, Tidesworn (prestige)
- **Level**: 20
- **Cost**: 40 stamina, 5-round cooldown
- **Effect**: Forces ALL living enemies to attack you for 3 rounds

### Taunt Mechanic
Monsters now track who taunted them and for how long. A taunted monster MUST attack the taunter — the weighted aggro system is bypassed entirely. If the taunter dies or flees, the taunt clears and monsters resume normal targeting.

### Existing Abilities Updated
- **Basic Taunt (T action)**: Now forces the targeted monster to attack you for 2 rounds (in addition to the defense reduction)
- **Eternal Vigil** (Tidesworn, level 90): Now forces all enemies to target you for 2 rounds while invulnerable — making it a true "invincible guardian" cooldown instead of just personal immunity

## Ascension Broadcast

When a player ascends to godhood after defeating Manwe, all other online players now see a real-time broadcast announcement of the ascension with the player's chosen divine name and alignment.

## Files Changed

- `GameConfig.cs` — Version 0.47.3; added `PermadeathRaceFloor = 3` constant; added onboarding constants (`MenuTier2Level`, `MenuTier3Level`, `FirstKillGoldBonus`, death penalty tier thresholds and rates)
- `Scripts/Systems/LootGenerator.cs` — Added 8 per-slot armor template lists (93 templates total: HeadArmorTemplates, ArmsArmorTemplates, HandsArmorTemplates, LegsArmorTemplates, FeetArmorTemplates, WaistArmorTemplates, FaceArmorTemplates, CloakArmorTemplates); added `RollArmorSlot()` weighted distribution method; modified `GenerateArmor()` and `GenerateArmorWithRarity()` to use all slots; changed `GenerateDungeonLoot()` from 55/45 to 45/55 weapon/armor; changed `GenerateMiniBossLoot()` from 35/30/20/15 to 35/65; changed `GenerateBossLoot()` from 40/35/15/10 to 40/60; modified `CreateArmorFromTemplate()` and `CreateBasicArmor()` to accept ObjType parameter
- `Scripts/Locations/LevelMasterLocation.cs` — Extracted `ApplyClassStatIncreases(Character)` and `ApplyAlignmentBonuses(Character)` as public static methods; made `DetermineAlignment()` and `GetExperienceForLevel()` public; added `CheckAutoLevelUp(Character)` public static method that handles the full level-up loop (class stats, alignment bonuses, training points, HP/mana restore, quickbar update, telemetry, news)
- `Scripts/Locations/BaseLocation.cs` — Added centralized auto-level-up check in `LocationLoop()` before display logic; catches all XP sources (combat, quests, seals, events, etc.)
- `Scripts/Locations/MainStreetLocation.cs` — Added `GetMenuTier()` helper; progressive menu disclosure in all 3 display methods (`DisplayLocationBBS`, `ShowClassicMenu`, `ShowScreenReaderMenu`) — Tier 1 (Lv1-2) shows 8 core options, Tier 2 (Lv3-4) adds town services, Tier 3 (Lv5+) shows full menu; added dungeon guidance banner for Level 1 players with zero kills; replaced "Visit your Master!" notification with training points reminder
- `Scripts/Locations/DungeonLocation.cs` — Removed "Visit your Master!" notification (training points reminder only shown on Main Street)
- `Scripts/Core/Monster.cs` — Added `TauntedBy` and `TauntRoundsLeft` properties for taunt tracking
- `Scripts/Systems/ClassAbilitySystem.cs` — Added "Thundering Roar" AoE taunt ability (level 20, Warrior/Paladin/Barbarian/Tidesworn)
- `Scripts/Systems/EndingsSystem.cs` — Added `MudServer.BroadcastToAll()` call on immortal ascension for real-time online player notification
- `Scripts/Systems/CombatEngine.cs` — Taunt enforcement in `SelectMonsterTarget()` (taunted monsters must attack taunter); taunt duration tick-down each monster round; `aoe_taunt` special effect implementation for multi-monster and single-monster combat; Eternal Vigil now forces all-monster targeting; basic T taunt now forces single-target for 2 rounds; added `pvpDefenderCooldowns` field; defender stamina initialization and ability learning in `PlayerVsPlayer()`; cooldown ticking each PvP round; full AI ability usage block in `ProcessComputerPlayerAction()` for non-spellcaster classes (50% chance, picks highest-damage ability, applies damage/healing/buffs, tracks cooldowns); unblocked Backstab/PowerAttack/PreciseStrike in PvP (routes through `MapCombatActionToAbility()` helper); replaced grouped combat random-stat level-up with `LevelMasterLocation.CheckAutoLevelUp()` for proper class-based stats; tiered death penalties by level in `ApplyDeathPenalties()` (Lv1-3 gentle, Lv4-5 moderate, Lv6+ unchanged); first kill bonus in `HandleVictory()` and `HandleVictoryMultiMonster()` with `ShowFirstKillBonus()` method (+500 gold)
- `Scripts/Core/King.cs` — Treasury income NPC count fallback changed from hardcoded 10 to `Math.Max(PermadeathPopulationFloor, ...)` ensuring minimum 45 NPCs for income calculation
- `Scripts/Systems/WorldSimulator.cs` — Added race-level permadeath floor check in `RollPermadeath()` (blocks permadeath if race has <= 3 alive members); added `ProcessNPCImmigration()` method (generates immigrant NPCs for races with < 2 alive members, posts news, logs events); wired immigration into world sim tick
- `Scripts/Systems/NPCSpawnSystem.cs` — Added `GenerateImmigrantNPC()` method (creates fresh NPC with random name, specified race/sex, random class, age 20-35, level scaled to server average, random personality, proper BirthDate); added immigrant name arrays and `ToRomanNumeral()` helper for duplicate name handling
- `Scripts/Server/PlayerSession.cs` — Added fallback playtime update in cleanup when `OnlineStateManager` is null; calls `UpdatePlayerSession(username, isLogin: false)` directly via `_sqlBackend`
- `Scripts/Systems/OnlinePlaySystem.cs` — `PipeIO()` rewritten: blocking reads instead of polling; stream close before await to unblock reads; write wrapped in try-catch; replaced PressAnyKey with Task.Delay after disconnect
