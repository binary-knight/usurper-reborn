# Usurper Reborn v0.40.2 — Faction Missions, Assassination, Faction Expansion & Sleep Risk

## Real Faction Missions

Faction missions from The Crown, The Shadows, and The Faith now create **real quests** instead of giving instant gold. When you accept a faction mission, you receive half the gold as an advance payment and a quest appears in your quest log.

- **Crown missions** ("Bring to justice"): Hunt and defeat the target NPC. Quest completes when you defeat them in combat.
- **Shadows missions** ("Delicate retrieval"): Collect a target amount of gold. Quest completes when your gold on hand reaches the threshold.
- **Faith missions** ("Bring back to the light"): Talk to the lost soul NPC. Quest completes when you interact with the named NPC.

Turn in completed faction missions at the **Quest Hall** to collect the remaining reward. Completing faction quests now grants **+50 faction standing** with the quest-giving faction.

## NPC Assassination System

A new **[6] Attack** option appears when interacting with NPCs, allowing you to ambush and murder them. Unlike honorable duels, assassination is a brutal act with lasting consequences:

- **Backstab bonus**: Assassin class deals 25% bonus damage on the first strike; all other classes get 10%.
- **Permanent death**: Defeated NPCs are killed permanently (they respawn after ~10 minutes).
- **Gold theft**: Steal 50% of the murdered NPC's gold.
- **Darkness alignment shift**: +25 Darkness for murder (compared to +10 for duels).
- **Witnesses**: NPCs at the same location see the murder and remember it.
- **Quest completion**: Murdering an NPC counts as defeating them for any bounties or faction missions targeting that NPC.
- **News generation**: Murders are broadcast to the news feed.

**Protection rules**: Story NPCs, the King, dead NPCs, and your own teammates cannot be attacked. A level warning appears if the target is 10+ levels above you.

## Murder Revenge System

NPCs don't forgive murder. The consequence encounter system has been enhanced with three tiers of revenge:

### Murder Revenge (Guaranteed)
When a murdered NPC respawns, they will **always** confront you — no random chance roll. They come back enraged with +20% STR and +20% HP. Bribery and apology are **not options** — you can only fight or run (and running is much harder against a murder victim).

### Witness Revenge
NPCs who saw you commit murder may also confront you. They can be bribed at a higher cost (50g per NPC level), but the bribe success chance is reduced. Silencing a witness grants extra Darkness.

### Faction Revenge
Murdering a faction member causes a **-200 standing penalty** with that faction, plus cascading effects to allied factions. NPCs who were friends of the victim (positive impression > 0.3) also become hostile toward you.

## Faction Expansion — Rank Perks & Exclusive Content

Faction ranks now matter. All three factions (Crown, Shadows, Faith) have had their existing bonuses scaled by rank (0-8) and gained new exclusive features.

### Rank-Scaled Bonuses

Existing faction discounts now scale with your rank instead of being flat rates:

| Faction | Bonus | Rank 0 | Rank 8 |
|---------|-------|--------|--------|
| Crown | Shop discount | 5% | 15% |
| Shadows | Fence bonus | 10% | 30% |
| Faith | Healing discount | 15% | 35% |

### Crown Faction — Guard Favor & Royal Armory

- **Guard Favor**: Town guard patrols now recognize Crown members. If you have high Darkness but are in the Crown, guards let you pass without confrontation.
- **Guard Intervention**: When ambushed by a grudge NPC, Crown guards may rush to your aid, dealing 25% of the attacker's max HP before the fight begins. Chance scales from 20% (rank 0) to 60% (rank 8).
- **Tax Exemption**: Crown members lose 20% less gold on death.
- **Royal Armory [L]**: New shop at the Castle selling Crown-exclusive legendary equipment. Prices and stats scale with your level.
  - Crown Blade (MainHand, WeapPow 150+level*2)
  - Royal Guard Plate (Body, ArmPow 120+level*2)
  - Crown Shield (OffHand, ArmPow 80+level)
  - Royal Signet Ring (Finger, +5 CHA/STR)

### Shadows Faction — Black Market, Informant & Assassination Contracts

- **Escape Bonus**: Shadows members get +15 to +45 flee chance (by rank), applied before the 75% cap.
- **Black Market [M]**: New shop in the Dark Alley (Shadows only). Buy contraband with rank-scaled discounts (5% per rank):
  - Forged Papers — reduce Darkness by 100
  - Poison Vial — +20% weapon damage for 5 combats
  - Smoke Bomb — guaranteed escape from non-boss fights (carry max 3)
- **Informant [I]**: Pay 100g for intel on the town's wealthiest NPCs, wanted criminals, and your active quest targets.
- **Assassination Contracts**: Shadows rank 3+ (Operative) have a 50% chance of receiving assassination missions instead of retrieval missions. Kill a specific NPC target for higher gold rewards. Assassin class gets +50% gold bonus on contracts.

### Faith Faction — Divine Favor, Blessing Duration & Inner Sanctum

- **Divine Favor**: Faith members have a chance to survive a killing blow in combat. A golden light restores you to 10% HP. Chance scales from 5% (rank 0) to 25% (rank 8). Triggers once per combat.
- **Blessing Duration**: All temple blessings (sacrifice and prayer) last longer for Faith members. Duration multiplier scales from 1.25x (rank 0) to 1.75x (rank 8).
- **Inner Sanctum [N]**: New room at the Temple (Faith only). Once per day, meditate for 500g to gain a permanent +1 to a random stat (STR, DEF, STA, AGI, CHA, DEX, WIS, INT, or CON).

### Combat Consumables

Two new consumables available from the Black Market:

- **Smoke Bombs**: Consume one to guarantee escape from any non-boss fight. Works in both single and multi-monster combat. Carry up to 3.
- **Poison Coating**: Your weapon deals +20% damage for 5 combats. One coat per use, cant stack — wait for it to wear off before reapplying.

## Offline Player Vulnerability — Sleep Risk System (Online Mode)

Logging out in online/MUD mode is no longer safe. When you quit the game, you must choose where your character sleeps — and that choice has real consequences.

### Sleep Locations

**Dormitory (10 gold)**
The cheap option. Your character sleeps in a communal barracks with no protection. NPCs roam the world at night and may attack sleeping players. Other online players can also attack you while you rest.

**Inn Room (75 gold per level)**
The safe option. Your character rests in a private room with +50% ATK/DEF boost if attacked. You can also hire up to **5 guards** to protect you while you sleep. Attackers must defeat all your guards before reaching you — and each guard fight weakens the attacker.

### Guard Hiring

When renting an Inn room, you can hire guards for extra protection:

| Guard | Base Cost | Base HP | Strength |
|-------|-----------|---------|----------|
| Rookie Guard | 100g | 80 | Low |
| Guard Hound | 150g | 60 | Fast, fragile |
| Veteran Guard | 300g | 150 | Medium |
| Guard Troll | 500g | 200 | Slow tank |
| Elite Guard | 600g | 250 | High |
| Guard Drake | 1000g | 300 | Very powerful |

- All guard costs and HP **scale with your level** (multiplied by `1 + level/10`)
- Each additional guard costs **50% more** than the last (nth-guard multiplier)
- Maximum 5 guards per sleep session
- Guards fight attackers in order — if any guard wins, the attack is repelled entirely

### What Happens While You Sleep

Every ~30 seconds, the world simulator rolls an 8% chance for each dormitory sleeper to be attacked by a wandering NPC (level 5+). If the attack succeeds:

- **50% of your gold on hand is stolen** (bank gold is safe!)
- **1 random item is stolen** from your equipment or inventory
- **10% of your XP is lost**
- Your character is marked as dead (preventing further attacks)

### Quit Warning

Quitting from Main Street now shows a warning and asks where you want to sleep. The default is the Dormitory. You can also choose to head to the Inn for protected sleep, or cancel and stay in town.

### Attack Sleeping Players

Online players can visit the Dormitory and attack other sleeping players directly. If you win, you steal 50% of their gold and a random item. If you lose, you take damage but the sleeper stays asleep.

### Sleep Report on Login

When you log back in, you'll see a detailed **Sleep Report** showing everything that happened while you were away — which guards fought, who attacked you, what was stolen, and whether you survived the night.

## Bug Fix: Auto-Combat Couldn't Be Stopped

Auto-combat mode said "Press Enter to stop" but never actually checked for input. Once you entered auto-combat, you were trapped in it until the fight ended — which could be fatal against tough enemies. The combat loop now polls for key presses every 50ms during the auto-combat delay. Pressing any key immediately stops auto-combat and returns you to the full combat menu. Works across all I/O modes (local console, SSH/MUD, BBS door).

## Files Changed

| File | Change |
|------|--------|
| `Scripts/Core/GameConfig.cs` | Version 0.40.2, murder system constants, faction initiator strings, 12 faction expansion constants, 16 sleep system constants |
| `Scripts/Core/Character.cs` | Added `PoisonCoatingCombats`, `SmokeBombs`, `InnerSanctumLastDay` (serialized), `DivineFavorTriggeredThisCombat` (transient) |
| `Scripts/AI/MemorySystem.cs` | Added `MemoryType.Murdered` with -1.0 impression impact |
| `Scripts/Systems/SaveDataStructures.cs` | Added `PoisonCoatingCombats`, `SmokeBombs`, `InnerSanctumLastDay` to PlayerData |
| `Scripts/Systems/SaveSystem.cs` | Serialize/deserialize new character properties |
| `Scripts/Core/GameEngine.cs` | Restore new character properties on load; `HandleSleepReport()` for login sleep report and death handling |
| `Scripts/Systems/FactionSystem.cs` | Rank-scaled `GetShopPriceModifier()`, `GetHealingPriceModifier()`, `GetFencePriceModifier()`; 12 new accessor methods for faction perks |
| `Scripts/Systems/CombatEngine.cs` | Fixed auto-combat; smoke bomb escape (single + multi); Shadows escape bonus; poison coating damage; divine favor death save (both death checks); Crown tax exemption in death penalties |
| `Scripts/Systems/StreetEncounterSystem.cs` | Murder/witness/grudge encounters; Crown guard favor bypass; Crown guard intervention in grudge fights |
| `Scripts/Systems/DivineBlessingSystem.cs` | Faith blessing duration multiplier for sacrifice and prayer blessings |
| `Scripts/Systems/NPCPetitionSystem.cs` | Rewrote `ExecuteFactionMission()` with real quests, advance payment, Shadows assassination contracts |
| `Scripts/Systems/QuestSystem.cs` | Added `CreateFactionMission()` with DefeatNPC branch for assassinations, `OnNPCTalkedTo()`, faction standing boost |
| `Scripts/Locations/DarkAlleyLocation.cs` | Black Market `[M]` shop (Forged Papers, Poison Vial, Smoke Bomb); Informant `[I]` intel service |
| `Scripts/Locations/TempleLocation.cs` | Inner Sanctum `[N]` daily meditation for permanent +1 stat |
| `Scripts/Locations/CastleLocation.cs` | Royal Armory `[L]` Crown-exclusive equipment shop (4 items) |
| `Scripts/Locations/BaseLocation.cs` | Added `[6] Attack` menu option, `AttackNPC()` method, wired `OnNPCTalkedTo()` |
| `Scripts/Systems/SqlSaveBackend.cs` | `sleeping_players` table, 7 new methods (Register/Unregister/Get/MarkDead/AppendLog/UpdateGuards), `SleepingPlayerInfo` class |
| `Scripts/Systems/HeadlessCombatResolver.cs` | **NEW** — Headless combat resolver for background thread combat (NPC vs guards, NPC vs sleeping players) |
| `Scripts/Systems/WorldSimulator.cs` | `ProcessNPCAttacksOnSleepers()` with guard gauntlet, gold/item/XP theft, attack logging |
| `Scripts/Locations/DormitoryLocation.cs` | Sleep-logout flow (`GoToSleepOnline`), `AttackSleeper()` PvP sleep attacks with gold/item theft |
| `Scripts/Locations/InnLocation.cs` | `RentRoom()` with multi-guard hiring (up to 5), level-scaled costs, nth-guard price multiplier |
| `Scripts/Locations/MainStreetLocation.cs` | Quit warning with sleep location choice (Dormitory/Inn/Cancel) in online mode |
| `Scripts/UI/TerminalEmulator.cs` | Added `IsInputAvailable()` and `FlushPendingInput()` for non-blocking input detection |
