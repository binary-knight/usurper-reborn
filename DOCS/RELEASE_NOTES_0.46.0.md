# v0.46.0 - Ascension

Players who defeat Manwe (or don't fight) on floor 100 and complete any ending can now ascend to immortality and become a worshippable god. Based on the original 1993 Usurper god system (VARGODS.PAS, GODWORLD.PAS, INITGODS.PAS, TEMPLE.PAS).

---

## New Features

### Immortal Ascension System

**Becoming Immortal**

After defeating Manwe and completing any ending (Usurper, Savior, Defiant, or True), the player is offered the choice to ascend to immortality as an alternative to New Game+. If accepted, the player chooses a divine name and is converted to an immortal god. Their alignment (Light, Dark, or Balance) is determined by which ending they completed.

**The Pantheon (Immortal Location)**

Immortal players are locked to the Pantheon -- they cannot visit any mortal location. The Pantheon menu provides all divine management:

- **[S] Status** -- View divine stats (rank, experience, believers, alignment, deeds remaining)
- **[B] Believers** -- View and manage your flock of worshippers
- **[D] Divine Deeds** -- Perform divine interventions (costs 1 deed each, reset daily):
  - **Recruit Believer** -- Convert a pagan NPC (33% chance) or steal from a rival god (25% base, modified by level difference)
  - **Bless Follower** -- Grant a believer +10% combat damage/defense for 10 combats
  - **Smite Mortal** -- Deal 10-25% MaxHP damage to a non-believer
  - **Poison Relationship** -- Worsen the bond between two NPCs (33% chance)
  - **Free Prisoner** -- Release an imprisoned character
  - **Divine Proclamation** -- Broadcast a message to all online players
- **[I] Immortals** -- View god rankings (all immortals sorted by experience)
- **[N] News** -- Read mortal and divine news
- **[C] Comment** -- Send a message recorded in the annals
- **[V] Visit Manwe** -- Level up when experience threshold is reached
- **[R] Renounce** -- Give up immortality and reroll (double confirmation required)
- **[Q] Quit** -- Leave the game

**God Ranks (9 Tiers)**

| Level | Title | Experience | Deeds/Day |
|-------|-------|-----------|-----------|
| 1 | Lesser Spirit | 0 | 3 |
| 2 | Minor Spirit | 5,000 | 4 |
| 3 | Spirit | 15,000 | 5 |
| 4 | Major Spirit | 50,000 | 6 |
| 5 | Minor Deity | 90,000 | 7 |
| 6 | Deity | 150,000 | 8 |
| 7 | Major Deity | 300,000 | 10 |
| 8 | DemiGod | 600,000 | 12 |
| 9 | God | 1,000,000 | 15 |

**Experience Sources**:
- **Believer kills** (primary): When a believer kills a monster, 5% of their combat XP is granted to their god as divine power. The believer sees "You sacrifice the remains to [god], granting them X divine power." If the god is online, they receive a real-time notification showing who sacrificed what. Works across all combat types (single, multi-monster, partial victory, group combat). Each grouped player feeds their own god independently.
- Believers (passive, daily): Each believer grants `level * 5` exp per daily reset
- Recruiting pagans: +150 exp
- Stealing believers: +50 exp
- Blessings bestowed: +10 exp
- Smiting mortals: +20 exp
- Poisoning relationships: +20 exp
- Freeing prisoners: +10 exp
- Proclamations: +5 exp

### Temple Worship (Mortal Side)

Mortals can now worship immortal gods at the Temple:

- **[J] Worship an Immortal** -- Choose an ascended god to follow. Shows all immortals with their rank, believer count, and online status (`[ONLINE]`/`[OFFLINE]`). When you start worshipping a god who is online, they receive an instant notification.
- **[$] Sacrifice Gold** -- Give gold to your god. Power granted scales with amount (1-6 power, matching original tiers). Online gods receive the experience instantly with a notification; offline gods receive it via atomic DB update and a message waiting at their next login.
- **[L] Leave Faith** -- Abandon your immortal god and become a free spirit again.

These options only appear when at least one immortal god exists.

### Interactive God-Mortal System (Online)

In MUD mode, the god-mortal relationship is fully interactive between real players:

**Gods targeting players (Pantheon Divine Deeds):**
- **Recruit Believer** now shows both NPC and player targets. Player targets appear with `[PLAYER]` tags and online status. Recruitment chance is 75% of the NPC rate (harder to sway a real player), but grants 2x experience.
- **Bless Follower** now shows player believers alongside NPC believers. Blessing a player grants 3x experience. Online players receive the buff instantly with a notification; offline players receive it via atomic DB update.
- **Smite Mortal** now shows both NPC and player targets. Smiting a player grants 2.5x experience. Anti-grief: 30-minute cooldown per target (can't repeatedly smite the same player). Online players take damage instantly with a notification; offline players take damage via atomic DB update.

**Dual-path effect delivery:**
- Online targets: direct in-memory field modification + instant ANSI notification via `EnqueueMessage()`
- Offline targets: atomic `json_set` SQL update + message visible at next login via `SendMessage()`

**News announcements** for all player interactions:
- `[DIVINE] {god} blessed {player}!`
- `[DIVINE] {god} struck {player} with divine lightning!`
- `[DIVINE] {player} has converted to {god}!`

**Believer counts** now include both NPC and player believers in online mode.

**Immortal Rankings** now show all ascended player-gods from the database with online status.

### Divine Blessing Combat Buff

When a god blesses a follower, that follower receives a combat buff (+10% damage and defense for 10 combats). Works identically to Well-Rested and Lover's Bliss buffs -- applied in both attack power and defense calculations, decremented per combat.

### Who List God Tags

Immortal players appear in the `/who` list with their god title tag in bright yellow. Their divine name is displayed instead of their username:

```
  Solarius [Minor Deity] - The Divine Realm [SSH]
```

### Divine Boon System

Gods can now actively configure what benefits their followers receive, creating a competitive marketplace where mortals choose gods based on the boons offered.

**Configuring Boons (Pantheon)**

Immortal players select `[F] Configure Favors` from the Pantheon menu to allocate boons using a points budget:

- **Budget**: `god level × 10` base points, plus a concentration bonus of up to +20 for gods with fewer believers (`20 - believers × 2`, minimum 0). A level 1 god with no believers has 30 points; a level 9 god with 20 believers has 90 points.
- **13 boons** across Combat, Economy, and Utility categories, each with 3 tiers
- **Alignment-gated boons**: Some boons are exclusive to Light, Dark, or Balance gods
- Changes take effect immediately -- online followers are notified and receive updated effects

**Boon Menu**

| Boon | Alignments | Cost | Tier 1 | Tier 2 | Tier 3 |
|------|-----------|------|--------|--------|--------|
| Warrior's Fury | Light, Balance | 8/tier | +5% damage | +10% damage | +15% damage |
| Shadow Strike | Dark | 8/tier | +5% crit | +10% crit | +15% crit |
| Divine Shield | Light | 10/tier | +5% defense | +10% defense | +15% defense |
| Lifedrain | Dark, Balance | 10/tier | +3% lifesteal | +6% lifesteal | +10% lifesteal |
| Battle Rage | Any | 6/tier | +3 attack | +6 attack | +10 attack |
| Golden Touch | Any | 6/tier | +5% gold | +10% gold | +15% gold |
| Merchant's Favor | Light, Balance | 8/tier | +3% shop discount | +6% discount | +10% discount |
| Scholar's Wisdom | Any | 10/tier | +5% XP | +10% XP | +15% XP |
| Divine Vitality | Light | 8/tier | +5% max HP | +10% max HP | +15% max HP |
| Shadow Veil | Dark | 6/tier | +5% flee | +10% flee | +15% flee |
| Fortune's Smile | Any | 6/tier | +5% luck | +10% luck | +15% luck |
| Ironhide | Balance | 6/tier | +3 defense | +6 defense | +10 defense |
| Mana Well | Any | 8/tier | +5% max mana | +10% max mana | +15% max mana |

**Mortal View (Temple)**

When browsing immortal gods at the Temple, mortals now see a prose description of each god's boon offering plus individual boon bullet points:

```
1. Solarius the Minor Deity  [ONLINE]  —  3 believers  —  Light
   "A radiant spirit of martial prowess and scholarly wisdom."
   • Warrior's Fury II — +10% damage bonus
   • Scholar's Wisdom I — +5% experience bonus
```

**Prayer Integration**

Praying at the Temple while worshipping a player-god now doubles passive boon effects for 2 hours (120 minutes). The god gains +10 experience from the prayer, and online gods receive an instant notification.

**Passive Effects**

Boon effects are applied automatically:
- **Combat**: Damage, defense, crit chance, and lifesteal bonuses in all combat
- **Economy**: XP and gold bonuses from monster kills; shop discounts at weapon, armor, and magic shops
- **Cached on login**: Boon effects are queried from the database and cached on the player's character when they log in

### Daily God Maintenance

On daily reset, immortal gods receive:
- Deeds reset to their tier's maximum
- Passive experience from believers (`believers * level * 2`)
- Automatic level recalculation from experience thresholds
- Level-up announcements posted to news

### Immortal Alt Character System (Online)

Immortal players are locked to the Pantheon and can only perform divine deeds. To keep these players engaged in the mortal world, they can now create a second mortal character on the same account.

**How It Works**

- After ascending to immortality, the character selection menu shows a new option: `[M] Create Mortal Alt Character`
- The alt character is a completely separate mortal with its own name, class, race, level, and inventory
- Both characters appear on the login menu: `[1]` for main (shows `[IMMORTAL]` tag), `[2]` for alt
- Each character has independent saves, autosaves, and emergency saves
- The alt slot persists forever -- even if the main character renounces immortality, the alt remains playable
- Maximum 2 characters per account: 1 main + 1 alt

**Restrictions**

- Alt characters cannot ascend to immortality (only your main can become a god)
- Alt characters cannot PvP against your main character in the Arena
- Creating a new main character (overwrite) does not affect the alt
- Only available in online/MUD mode

**Technical Details**

Alt characters are stored as separate player rows with key `{username}__alt`. Zero schema changes -- all existing features (leaderboards, /who, news, dormitory, etc.) work unchanged because the alt is just another player in the database.

### Meaningful Intimate Scene Choices

Player choices during intimate scenes now matter mechanically. Each of the three choice points (Anticipation, Escalation, Afterglow) is evaluated against the NPC's personality traits (Passion, Tenderness, Sensuality, Romanticism, Adventurousness, RomanceStyle). After each choice, a colored reaction line shows whether your choice resonated:

- **Match** (green): The NPC's eyes light up -- this is exactly what they wanted
- **Mismatch** (yellow): The NPC smiles, going along with your lead

Your total matches across all three phases determine the relationship bonus:

| Matches | Relationship Steps |
|---------|-------------------|
| 0 | 2 (reduced) |
| 1 | 3 (base) |
| 2 | 5 (bonus) |
| 3 (perfect) | 7 (big bonus) + "Lover's Bliss" buff |

**Lover's Bliss**: A perfect connection grants +10% damage and defense for 5 combats. Persists through save/load. Displayed alongside the Well-Rested buff in combat.

---

## Improvements

### Companion Equipment Stats Now Visible

The companion equipment management screen at the Inn ([E] Manage Equipment) previously showed only the item name for each equipped slot, with no indication of what the item actually does. Now shows full stat details for every equipped item: attack power, armor class, shield bonus, defence, all stat bonuses (STR/DEX/AGI/CON/INT/WIS/CHA), HP/MP bonuses, crit chance, life steal, magic resistance, and poison damage. Item names are also color-coded by rarity (Common=white, Uncommon=green, Rare=cyan, Epic=magenta, Legendary=yellow, Artifact=bright yellow).

### Abandon Quest

Players can now abandon quests from the Quest Hall using the new `[X] Abandon Quest` menu option. Select the quest to drop, confirm, and it's removed from your active quest log with no penalty. Useful for clearing out quests that are no longer completable (removed quest types, permadead targets, etc.).

### Children Now Inherit Surnames

NPC children are now born with proper full names. Children inherit their father's surname when the father has one (e.g., a child of "Ragnar Bloodaxe" might be "Celeste Bloodaxe"). For fathers with alias-style names (rogues, monks, single-name NPCs), a fantasy surname is generated deterministically so all siblings share the same family name. Roman numeral suffixes have been removed entirely -- every child has a unique first name + surname combination.

---

## Balance Changes

### Endgame Damage Scaling Caps

High-level characters with extreme stats (200+) were dealing exponentially scaling damage that trivialized all content, including bosses. Several uncapped multiplier formulas have been reined in:

- **Critical hit damage** capped at 3.0x (was uncapped -- DEX 256 gave 6.42x)
- **Class ability stat scaling** capped at 5.0x (was uncapped -- STR 616 gave 20.74x)
- **Spell damage multiplier** capped at 4.0x from Intelligence (was uncapped -- INT 250 gave 10.6x)
- **Spell critical chance** capped at 50% (was uncapped -- INT 120+ gave guaranteed crits)
- **Healing multiplier** capped at 3.0x from Wisdom (was uncapped -- WIS 250 gave 4.6x)
- **Spell stat bonus** (INT + WIS combined for Cleric/Sage) capped at 5.0x total

These caps still allow endgame characters to deal significant damage (5,000-12,000 per hit) but prevent the 30,000-50,000 damage one-shots that were trivializing boss encounters.

### Warrior Extra Attacks Capped

Warrior's bonus attacks from `Level / 10` had no upper limit -- a level 100 Warrior would get 10 extra attacks per round (11 total base, or 22 with Haste). Extra attacks from class are now capped at 3 (so a max of 4 base swings from the Warrior class alone). Combined with dual-wield, agility, artifacts, and Haste, a fully buffed Warrior can still reach 8 attacks/round (the new hard cap on `GetAttackCount()`), but the unchecked linear scaling is gone.

### Multi-Monster Combat Damage Formula Unified

The multi-monster (dungeon) combat damage formula was using a simplified calculation that was missing most of the modifiers from the single-combat path. This caused significant inconsistency: the same character would deal ~550 damage per hit in single combat but only ~450 per hit in dungeon combat. The multi-monster path now uses the same full formula including: weapon scaling with randomness, proficiency multiplier, critical hit rolls, status effect buffs (Rage, PowerStance, Blessed, etc.), difficulty modifier, grief effects, Royal Authority, Well-Rested, Lover's Bliss, Divine Blessing, Poison Coating, Permanent Damage Bonus, and Sunforged Blade bonuses.

### Attack Count Hard Cap

Total attacks per round are now hard-capped at 8 regardless of how many bonus sources stack (class, dual-wield, agility, artifacts, drugs, Haste). Previously Haste alone could double 6+ attacks to 12+, and with all sources stacking a theoretical 15+ attacks per round was possible.

### Dungeon Feature Stat Checks Rebalanced

Dungeon feature interactions (open doors, search relics, break barriers) use stat checks that were trivially easy at endgame. The stat bonus was `stat / 10` (giving 50+ at high stats) against a DC of `10 + floor/2` (max 60), meaning high-level characters auto-passed every check. Stat bonus is now capped at 20 and the DC formula adjusted to `8 + floor/4` so checks remain meaningful: ~70% pass rate at appropriate level, ~40-50% on deep floors even with maxed stats.

---

## Bug Fixes

### Group Loot Roll No Longer Auto-Equips Downgrades

When a group member won a loot roll, the item was auto-equipped regardless of whether it was better or worse than their current gear. A player with a powerful sword could win the roll on a weaker drop and have their good weapon swapped out (moved to inventory) and replaced with the inferior item. The loot system now only auto-equips items that are actual upgrades (`upgradeValue > 0`). If the winner's current gear is equal or better, the item goes to their inventory instead (or is left behind for NPCs).

### Solo Dungeon Rewards No Longer Split Among NPC Teammates

Non-combat dungeon rewards (floor treasure, treasure chests, boss bonuses, floor clear bonuses, feature interactions) were being split among NPC teammates (mercenaries, spouse) even when the player wasn't in a group with other human players. A solo player with a hired mercenary would receive only half the gold from every chest and treasure pile. Rewards are now only split when actual grouped human players are present. NPC teammates fighting alongside a solo player no longer reduce the leader's dungeon rewards.

### Dormitory and Inn Now Draw from Bank Gold

Players could exploit the sleep system by depositing all gold in the bank and logging out broke -- sleeping on the street for free with nothing to steal. The dormitory, inn room rental, and logout sleep check now cascade payment: gold on hand first, then bank gold. Players are only forced to sleep on the street if they truly have no gold anywhere. This applies to all three sleep payment points: Main Street logout, Dormitory location, and Inn room rental (including guard hiring).

### Unidentified Items Blocked from Companions

Unidentified items could be seen with full stats and equipped onto companions through the Inn's equipment management screen. Unidentified items now display as "(unidentified)" with no stats visible, and attempting to equip one shows a message that it must be identified first.

### Permadead NPCs Removed from World Map

The observability dashboard world map was counting permadead NPCs in location totals, making it look like NPCs were present (especially in the dungeon) when they were actually permanently dead.

### World Announcement Interrupts Player Input (Online)

When a world announcement arrived while a player was typing, the broadcast message erased the player's partially-typed text. The message pump used `\r\x1b[2K` (carriage return + erase entire line) which destroyed whatever the player had been composing. Fixed by using `\r\n` (new line) to preserve the player's in-progress input.

### Permadead NPCs Staying Married

When an NPC was permanently killed (permadeath), their spouse was never notified and the marriage registry was never cleaned up. The bereavement handler was only called from the aging/natural death path, not from `MarkNPCDead()`. Fixed by calling `HandleSpouseBereavement()` on permadeath, and also fixed the bereavement handler to always clear the marriage registry even when the spouse object can't be found. A startup migration now cleans up any orphaned marriages involving dead NPCs.

### Quests Lost on Save/Load (Single Player)

All active quests disappeared when quitting and reloading a save. Two bugs: (1) Player's claimed quests were serialized into `PlayerData.ActiveQuests` on save, but on load the restore code tried to read them from the quest database -- which hadn't been populated yet. (2) The world state restore then called `questDatabase.Clear()` which wiped any quests that had been loaded. Fixed by using `MergePlayerQuests` to load player quests directly from save data into the database, and replacing the destructive `Clear()` with an additive `MergeWorldQuests` that preserves player quests while loading unclaimed board quests.

### Old God Floor Hints Wrong

Dungeon floor guidance showed Old God boss hints on the wrong floors. Terravok was shown on floor 80 instead of floor 95. Maelketh had an erroneous second hint on floor 60. Veloura (40), Thorgrim (55), Noctura (70), and Aurelion (85) had no boss hints at all. Fixed both the full and compact (BBS) guidance displays to show correct boss hints on all 7 Old God floors.

### Cross-Player Marriage Contamination (Online)

In MUD mode, the `NPCMarriageRegistry` is a global singleton shared across all player sessions. Each player's save/load cycle was calling `RestoreMarriages()` which cleared the entire global dictionary and repopulated it from that player's save data -- wiping every other player's marriage state. Fixed by making the world sim the sole authority for NPC marriages in online mode: player sessions no longer save or restore the marriage registry. Also converted the registry to use `ConcurrentDictionary` for thread safety.

### Weapon/Armor Power Bonuses Lost on Stats Recalculation

Several encounters and systems granted permanent weapon or armor power bonuses by directly modifying `WeapPow` or `ArmPow`, but these values are reset to zero and rebuilt from equipment every time stats are recalculated (equipping items, leveling up, etc.). This meant bonuses from the Infernal Forge dungeon encounter, artifact collection, magic shop enchantments, dungeon merchant fallback purchases, Ring of Protection, and prison shadow boxing were all silently lost.

**Fix**: Added persistent `BonusWeapPow` and `BonusArmPow` fields that survive stat recalculation. All affected encounters now use these fields instead of directly modifying computed weapon/armor power. The Infernal Forge encounter text has been reworded to better convey it as a one-time discovery rather than a permanent location.

---

## Files Changed

| File | Changes |
|------|---------|
| `Scripts/Systems/DivineBoonRegistry.cs` | **NEW** -- 13 boon definitions with alignment gating and 3 tiers; `ActiveBoonEffects` class; budget calculation with concentration bonus; parse/serialize config; `GenerateDescription()` prose; `GetEffectSummaryLines()` bullet points |
| `Scripts/Locations/PantheonLocation.cs` | **NEW** -- Full immortal location with divine menu, 6 deed types, believer management, rankings, news, renounce; `DeedTarget` class for unified NPC/player targets; `ApplyBlessToPlayer()`, `ApplySmiteToPlayer()`, `ApplyRecruitToPlayer()` dual-path helpers; smite cooldown tracking; player believers in `CountBelievers()` and `GetBelieverListAsync()`; DB-backed `GetAllImmortalsAsync()`; `[F] Configure Favors` boon configuration UI with budget display, add/upgrade/remove flow; `NotifyOnlineFollowers()` for instant boon cache refresh |
| `Scripts/Core/Character.cs` | Added `IsImmortal`, `DivineName`, `GodLevel`, `GodExperience`, `DeedsLeft`, `GodAlignment`, `AscensionDate`, `WorshippedGod`, `DivineBlessingCombats`, `DivineBlessingBonus`, `BonusWeapPow`, `BonusArmPow`, `DivineBoonConfig`, `CachedBoonEffects`, `HasEarnedAltSlot`; capped Warrior ExtraAttacks at 3; `BonusWeapPow`/`BonusArmPow` applied in `RecalculateStats()` after equipment |
| `Scripts/Core/GameConfig.cs` | Version 0.46.0; `Pantheon = 502` enum; god rank arrays (exp thresholds, deeds/day, titles); recruitment/blessing/smite constants; sacrifice power tiers; `GodRecruitPlayerMultiplier`, `GodRecruitPlayerExpMultiplier`, `GodBlessPlayerExpMultiplier`, `GodSmitePlayerExpMultiplier`, `GodSmitePlayerCooldownMinutes`; boon system constants; `GodBelieverKillXPPercent = 0.05f` (5% kill XP to god); `GodBelieverExpPerLevel` raised from 2 to 5 |
| `Scripts/Core/SaveDataStructures.cs` | All 10 immortal fields added to PlayerData; `BonusWeapPow`, `BonusArmPow`, `DivineBoonConfig`, `HasEarnedAltSlot` fields |
| `Scripts/Systems/SaveSystem.cs` | Serialize/restore all immortal fields, `BonusWeapPow`/`BonusArmPow`, `DivineBoonConfig`, and `HasEarnedAltSlot`; fixed AutoSave key in online mode to use `DoorMode.GetPlayerName()` instead of `player.Name2`; gated marriage registry save/restore behind `!IsOnlineMode` |
| `Scripts/Core/GameEngine.cs` | Restore immortal fields, `BonusWeapPow`/`BonusArmPow`, `DivineBoonConfig`, and `HasEarnedAltSlot` on load; cache boon effects from DB on login; `PendingImmortalAscension` flag; route immortals to Pantheon; removed `RestoreMarriages()`/`RestoreAffairs()` from `LoadSaveByFileName()`; unified dual-character menu in `RunBBSDoorMode()` with `[1]`/`[2]`/`[M]`/`[N]` options; `SwitchToAltCharacter()` async helper switches DoorMode identity + SessionContext.CharacterKey + OnlineStateManager; `WriteMenuKey()` display helper |
| `Scripts/Systems/EndingsSystem.cs` | `OfferImmortality()` method after ending sequence, before NG+ offer; sets `HasEarnedAltSlot = true` on ascension; blocks alt characters from ascending |
| `Scripts/Locations/TempleLocation.cs` | Added `[J]` Worship Immortal, `[$]` Sacrifice Gold, `[L]` Leave Faith menu options and handlers; `GetImmortalGodsAsync()` queries DB for all ascended player-gods with online status; `SacrificeToImmortalGod()` dual-path delivery (online instant + offline DB); worship notification to online gods; boon description display in worship screen; boon effects cached on worship change; player-god prayer doubles boon effects for 2 hours |
| `Scripts/Systems/IOnlineSaveBackend.cs` | Added `ImmortalPlayerInfo` (with `DivineBoonConfig`), `MortalPlayerInfo` data classes; 9 new interface methods: `GetImmortalPlayers()`, `GetMortalPlayers()`, `CountPlayerBelievers()`, `ApplyDivineBlessing()`, `ApplyDivineSmite()`, `SetPlayerWorshippedGod()`, `AddGodExperience()`, `GetGodBoonConfig()`, `SetGodBoonConfig()` |
| `Scripts/Systems/SqlSaveBackend.cs` | Implemented 9 divine system methods using `json_extract`/`json_set` SQL patterns for atomic offline player modifications; boon config in `GetImmortalPlayers()`; added `GetAltKey()`, `GetAccountUsername()`, `IsAltCharacter()` static helpers; blocked `__alt` in registration |
| `Scripts/Systems/LocationManager.cs` | Register PantheonLocation; immortal location lock redirects all non-Pantheon navigation |
| `Scripts/Locations/BaseLocation.cs` | Immortal redirect check in `EnterLocation` -- blocks mortal locations with message |
| `Scripts/Server/SessionContext.cs` | Added `CharacterKey` property for alt character save key routing |
| `Scripts/Server/PlayerSession.cs` | `/kick` now closes TCP connection in `DisconnectAsync()`; snoop cleanup removes spectator streams; `CharacterKey` initialized from `Username`; emergency save and dormitory registration use `CharacterKey` |
| `Scripts/Server/MudChatSystem.cs` | God title tags and divine name display in `/who`; immortals shown in bright yellow |
| `Scripts/Systems/DailySystemManager.cs` | `ProcessGodDailyMaintenance()` -- reset deeds, grant believer exp, recalculate levels |
| `Scripts/Systems/CombatEngine.cs` | Divine Blessing buff in attack power, defense, and per-combat decrement (both single and multi-monster paths); unified multi-monster damage formula with single-combat (proficiency, crits, status buffs, all modifiers); hard cap of 8 attacks/round in `GetAttackCount()`; group loot roll only auto-equips upgrades; boon damage/defense/crit/lifesteal/XP/gold bonuses; `GrantGodKillXP()` helper grants 5% of kill XP to worshipped god (all 4 victory paths + group combat); online god gets real-time notification + in-memory XP update + level-up check |
| `Scripts/Systems/StatEffectsSystem.cs` | Boon crit bonus in `RollCriticalHit()`; capped `GetCriticalDamageMultiplier` at 3.0x; capped `GetSpellDamageMultiplier` at 4.0x; capped `GetSpellCriticalChance` at 50%; capped `GetHealingMultiplier` at 3.0x |
| `Scripts/Core/God.cs` | Fixed GodTitles array indexing (0-based adjustment) |
| `Scripts/Systems/GodSystem.cs` | Fixed GodTitles array indexing (0-based adjustment) |
| `Scripts/Systems/IntimacySystem.cs` | Added personality matching to intimate scene choices; variable relationship steps by match count; Lover's Bliss buff on perfect match |
| `Scripts/Systems/ClassAbilitySystem.cs` | Capped ability `statScale` at 5.0x to prevent exponential damage at high STR/DEX |
| `Scripts/Systems/SpellSystem.cs` | Capped total spell stat bonus at 5.0x in `ScaleSpellEffect()` |
| `Scripts/Systems/WorldSimulator.cs` | Added `HandleSpouseBereavement()` call on permadeath; added `CleanUpDeadNPCMarriages()` startup migration |
| `Scripts/AI/EnhancedNPCBehaviors.cs` | Converted `NPCMarriageRegistry` to use `ConcurrentDictionary` for thread safety |
| `Scripts/Systems/FeatureInteractionSystem.cs` | Capped stat bonus at 20 and rebalanced DC formula for dungeon feature checks |
| `Scripts/Systems/QuestSystem.cs` | Added `MergeWorldQuests()` for additive quest loading without clearing player quests; added `AbandonQuest()` method |
| `Scripts/Locations/QuestHallLocation.cs` | Added `[X] Abandon Quest` menu option and handler |
| `Scripts/Locations/DungeonLocation.cs` | Fixed Old God boss hints on wrong floors; solo rewards no longer split among NPC teammates; merchant weapon/armor/ring fallbacks use `BonusWeapPow`/`BonusArmPow`/`BonusMaxHP` instead of direct stat modification |
| `Scripts/Locations/InnLocation.cs` | Companion equipment shows full stats with rarity colors; blocks unidentified items; room rental and guard hiring now draw from bank gold |
| `Scripts/Locations/MainStreetLocation.cs` | Dormitory checkout draws from bank gold before forcing street sleep |
| `Scripts/Locations/DormitoryLocation.cs` | Dormitory sleep payment draws from bank gold as fallback |
| `Scripts/Server/WizardCommandSystem.cs` | `/snoop` now wires into spectator output forwarding; cleanup on disconnect; all 7 player-targeting commands use `Engine.CurrentPlayer` instead of never-set `Context.Player` |
| `Scripts/UI/TerminalEmulator.cs` | `/force` dequeues `ForcedCommands` at top of `GetInput()` before blocking on stream read |
| `Scripts/Systems/OnlineAdminConsole.cs` | `ApplyEditsToLiveSession()` syncs admin edits to in-memory player session |
| `Scripts/Systems/FamilySystem.cs` | Children inherit father's surname; fantasy surname generation for single-name parents |
| `Scripts/Systems/RareEncounters.cs` | Infernal Forge uses `BonusWeapPow` instead of `WeapPow`; encounter text reworded as one-time discovery |
| `Scripts/Locations/WeaponShopLocation.cs` | Divine boon shop discount applied to main purchase and dual-wield purchase |
| `Scripts/Locations/ArmorShopLocation.cs` | Divine boon shop discount applied to main purchase and auto-buy affordability filter |
| `Scripts/Locations/AdvancedMagicShopLocation.cs` | Divine boon shop discount applied to identify, potions, and item purchases; `ApplyMagicItemStats()` uses `BonusWeapPow`/`BonusArmPow` instead of direct `WeapPow`/`ArmPow` |
| `Scripts/Locations/ArenaLocation.cs` | Same-account PvP blocked (main cannot fight alt, alt cannot fight main) |
| `Scripts/Systems/OnlineStateManager.cs` | `username` field now mutable; added `SwitchIdentity()` for alt character switching (unregisters old key, registers new key) |
| `Scripts/Systems/ArtifactSystem.cs` | Artifact `weappow` bonus uses `BonusWeapPow`; added `RecalculateStats()` call after applying bonuses |
| `Scripts/Systems/PrisonActivitySystem.cs` | Shadow boxing uses `BonusWeapPow`/`BaseDefence` instead of direct `WeapPow`/`Defence` |
| `web/dashboard.html` | Filter permadead NPCs from world map location counts |
