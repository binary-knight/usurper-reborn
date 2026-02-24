# v0.46.3 - Ascension

Accessibility improvements, balance tuning, BBS online mode default.

---

## Balance Changes

### Weapon Power Soft Cap Retuned

The v0.46.1 weapon power soft cap (500 threshold, 30% above) was too aggressive for normal high-level players. A level 80 Barbarian with 2,470 WeapPow lost 56% of attack power AND 49% of defense, making level-appropriate dungeons nearly unplayable even with fully geared companions.

**Threshold raised from 500 to 800** — Normal high-level progression is no longer punished. The cap only kicks in for players with extreme equipment stacking.

**Diminishing rate raised from 30% to 50%** — Power above the threshold retains half its value instead of less than a third, making the curve feel less like hitting a wall.

**Armor power cap removed** — Defense was never the balance problem (the original issue was 57k offensive crits). ArmPow now passes through at full value, so players aren't taking extra damage on top of dealing less.

Impact comparison at the new settings:

| Player | WeapPow | Old Effective | New Effective | ArmPow Change |
|--------|---------|---------------|---------------|---------------|
| Quent (L80) | 2,470 | 1,091 (-56%) | 1,635 (-34%) | Full (was -49%) |
| fastfinge (L95) | 1,710 | 863 (-50%) | 1,255 (-27%) | Full (was -31%) |

### Floor Boss Damage Rebalanced

Floor bosses could one-shot same-level players. A level 36 Awakened Golem dealt 3,442 damage in a single hit to a Warrior with 1,371 HP — over 2.5x their max health. Three stacking multipliers were the cause:

1. **Redundant STR boost removed** — Boss room monsters got a 1.3x Strength boost at creation on top of `GetAttackPower()`'s own 1.3x IsBoss multiplier. This double-dipping is now fixed (the creation boost is removed, the runtime multiplier stays).

2. **Boss damage cap added** — Bosses now have a per-hit damage cap of 85% of player max HP. Non-bosses were already capped at 75%, but bosses had no cap at all. Bosses are still the most dangerous enemies, but can't instantly kill a full-health player in one swing.

3. **Special ability damage capped** — Monster special abilities (Backstab 2.5x, Berserk 2.0x, life drain, direct damage) bypassed the normal attack damage cap entirely. All three ability damage paths now apply the same per-hit cap.

---

## New Features

### Assassin Poison Vial System

Assassins now collect **poison vials** from dungeon class-specific features and can coat their blade during combat with different poison types. Six tiers of poison unlock as the assassin levels up, each with a unique combat effect:

| Poison | Unlock | Effect | Duration |
|--------|--------|--------|----------|
| Serpent Venom | Level 5 | +20% attack damage | 3 combats |
| Nightshade Extract | Level 15 | Stuns enemy (free opening) | 3 combats |
| Hemlock Draught | Level 30 | Weakens enemy (-25% STR, distracted) | 3 combats |
| Siphoning Venom | Level 45 | Lifesteal on your attacks | 3 combats |
| Widow's Kiss | Level 60 | Paralyzes enemy (skip turn) | 2 combats |
| Deathbane | Level 80 | +30% damage, poison, weaken | 2 combats |

**How it works:**
- Carry up to 10 vials at a time
- Press **[B] Coat Blade** during combat (costs your turn for the round)
- Choose which poison to apply from your unlocked options
- Each hit while coated applies the poison's effect to the enemy
- Coating lasts for the listed number of combats

**Getting vials:**
- **Assassin dungeon bonus**: Finding hidden supplies now gives poison vials (was incorrectly giving healing potions labeled as "poison")
- **Dark Alley black market**: Buy 3 vials per purchase (replaces the old instant blade coating)

Any class can use poison vials — but only Assassins find them in dungeons. Other classes can buy them from the Dark Alley.

---

### BBS Doors Default to Online Mode

BBS door mode now automatically enables online/shared-world mode. The `--online` flag is no longer required — any BBS door command (`--door32`, `--door`, `--doorsys`, `--node`) implies online mode. Existing commands with `--online` continue to work for backwards compatibility.

SysOps can simplify their door commands from:
```
UsurperReborn --online --door32 %f
```
to just:
```
UsurperReborn --door32 %f
```

### Dungeon Guide (`[G]`)

New `[G] Guide` option in the dungeon gives step-by-step compass directions to key points of interest. Available to all players alongside the existing `[M] Map`.

Select a destination to get the shortest path:

- `[U]` Nearest unexplored room
- `[C]` Nearest uncleared room (monsters)
- `[S]` Stairs down (if discovered)
- `[B]` Boss room (if discovered and undefeated)

Each option shows the destination room name and distance. Selecting one prints directions like "Path to Dark Corridor: North, North, East". Uses BFS pathfinding through explored rooms. Originally built for screen reader accessibility based on blind player feedback, but useful for all players navigating large dungeon floors.

In screen reader mode, pressing `[M]` also opens the navigator instead of the ASCII map.

---

## Accessibility

### "Fully Cleared" Exit Annotations (Screen Reader Mode)

Dungeon exits now show `(all clear)` or `(fully cleared)` instead of `(clr)`/`(cleared)` when every reachable room in that direction has been cleared. This lets screen reader players skip entire branches they've already finished when going for a floor clear, without needing to explore dead ends to verify.

---

### Companion Navigation Comments

Companions and teammates now comment on dungeon navigation, adding flavor and subtle guidance:

- **After clearing a room**, a companion may suggest which direction to go next (toward uncleared or unexplored rooms). Each companion has their own personality: Vex is sarcastic ("So are we going north or are we just standing around?"), Aldric is tactical ("Form up. We move east."), Lyris is mystical ("I sense something to the west..."), and Mira is gentle ("Maybe we should try south?"). Generic teammates get straightforward lines.

- **When backtracking into fully-cleared areas**, companions may comment on the wasted effort. Vex mocks you ("Are you lost or something? There's nothing left this way."), Aldric stays focused ("This ground is cleared. We should press forward."), Lyris senses the emptiness, and Mira gently redirects.

Comments trigger randomly (not every room) to avoid spam.

---

## Bug Fixes

### Companions and Bodyguards Now Fight in Street Encounters

Street encounters (ambushes, grudge confrontations, muggings, assassins, brawls, jealous spouses, throne challengers, city control contests — all 40+ encounter types) were 1v1 fights even if you had companions and royal bodyguards. The combat engine accepts a teammates parameter, but street encounters never passed it. A king with 5 top-tier mercenaries would still fight alone when ambushed on Main Street.

All street combat now assembles your available allies (active companions + royal mercenary bodyguards) and passes them to the combat engine, matching how dungeon combat already works.

### Sleep Attack Rebalanced

Players were dying nearly every time they slept at the Inn, even with 5 top-tier guards. Two root causes:

**Attacker level unrestricted** — Any Dark/Evil NPC level 5+ could attack sleeping players regardless of level difference. A level 80 NPC would steamroll through 5 elite guards (which scale to the sleeper's level) in the gauntlet and still have enough HP to kill the sleeping player. Sleep attackers (both NPC and player) now must be within ±5 levels of the sleeper. This applies to NPC world sim attacks, Inn sleep attacks, and Dormitory sleep attacks.

**Sleeping player clone far too weak** — The headless combat resolver (used for offline sleep fights) calculated damage as `STR + WeapPow/2` and defense as `DEF/3 + ArmPow/4`. The real combat engine uses `STR + STR/4 bonus + Level + WeapPow(with variance)` for attack and `DEF(with variance) + sqrt-scaled ArmPow absorption` for defense. A player's sleep clone was fighting at roughly 30-40% of their real combat power. The headless resolver now mirrors the real combat engine's formulas, so your sleeping character fights back with stats that actually match your build.

### Dungeon Traps and Stat Checks Can't Kill Outright

Environmental hazards (traps, failed stat checks, risky features, shrine drains, puzzle failures) could instantly kill low-level characters. A level 1 Jester with ~15 HP could die to a risk/reward trap dealing 13-32 damage, or a rune trap dealing 5-19 damage. All non-combat dungeon damage now floors HP at 1 instead of allowing it to reach 0. Players can only die from monster combat, not from examining a bookshelf.

### Immortal Ascension Crash Fixed

Ascending to godhood in the dungeon caused a NullReferenceException crash. After the ascension sequence, the code tried to navigate to Main Street instead of exiting the dungeon cleanly. Now properly exits to the Pantheon after ascension. Also fixed the `PendingImmortalAscension` handler in `RunBBSDoorMode()` to actually route the player to the Pantheon instead of silently ending the session.

### Old God Bosses Deal 1 Damage to Bodyguards

All Old God boss fights (Terravok, Manwe, etc.) only dealt 1 damage per hit to royal bodyguards, making them trivially easy with mercenaries. Two root causes:

1. **Missing diminishing returns on companion armor** — Player defense uses sqrt-scaled armor absorption (added in v0.41.4 to prevent "untouchable" builds), but companion defense used raw linear `ArmPow/2`. A level 90 Tank bodyguard had 692 effective defense vs Terravok's 265 attack power, always flooring to 1 damage. Companion defense now uses the same sqrt scaling as player defense.

2. **Old God abilities never fired against companions** — Boss abilities use custom names ("Earthquake", "Stone Skin", "Mountain's Weight") that don't match the `MonsterAbilities.AbilityType` enum, so the `Enum.TryParse` silently failed and no special attacks ever executed. Boss monsters now fall back to level-scaled direct damage when their ability names don't match the enum.

Additionally, boss monsters now have a minimum damage floor (`level * 3` per hit) against companions, ensuring divine beings can never be fully stonewalled by mortal defenses.

### Seal Progress Bar Shows Wrong Positions

The seal discovery progress bar showed ordinal count (`[X][X][X][X][*][ ][ ]` for 5th seal found) instead of actual seal positions. A player who found seals 1-4 and then jumped to seal 7 would see it displayed as position 5 instead of position 7. The bar now maps each slot to its specific seal — skipped seals show as empty in their correct position.

Additionally, collecting the 7th seal (Seal of Revelation) always displayed "You have found all seven seals" regardless of how many were actually collected. Now shows "You have found the final seal — but X still elude you" when seals are missing.

### Alt Characters Can't See Their Own God at the Temple

Alt characters on the same account as an immortal god couldn't see or worship that god at the Temple. The filter intended to prevent a god from worshipping themselves was matching by account username, which is shared between the immortal and their alt. Now only filters out the god when the current player IS the immortal (by divine name), so alt characters can properly find and worship their own account's god.

### Alt Character Shows Account Name in "Also Here"

When playing as an alt character, the "Also here" display on Main Street (and all locations) showed the account's main character name instead of the alt's name. The player's god character "fastfinge" appeared as present even though only their alt "Fasterfinge" was logged in. Root cause: `RoomRegistry` tracked sessions by the immutable account username, and `SwitchToAltCharacter` only updated the database presence — not the in-memory room registry. Added `ActiveCharacterName` to `PlayerSession` that updates on alt switch. Room presence, arrival/departure broadcasts, and exclusion checks now use this active name.

### BBS Door Process Hangs on Exit

With online mode now enabled by default for BBS doors, background threads (world sim, heartbeat timers) could keep the process alive after the game ended, leaving the BBS caller stuck. The process now forces a clean exit after shutdown, ensuring the BBS immediately regains control.

---

## Files Changed

| File | Changes |
|------|---------|
| `Scripts/Core/GameConfig.cs` | Version 0.46.3; `MaxPoisonVials`, `DeathbaneDamageBonus` constants |
| `Scripts/Core/Character.cs` | Added `PoisonVials`, `ActivePoisonType` properties; `PoisonType` enum (6 tiers + None); `PoisonData` static helper class with unlock levels, names, descriptions, colors, coating durations, damage bonus info |
| `Scripts/Systems/SaveDataStructures.cs` | Added `PoisonVials`, `ActivePoisonType` to PlayerData |
| `Scripts/Systems/SaveSystem.cs` | Serialize/restore poison vial and active poison type fields |
| `Scripts/Systems/CombatEngine.cs` | Weapon power soft cap retuned: threshold 500→800, rate 30%→50%; armor power cap removed (pass-through); companion defense now uses sqrt-scaled diminishing returns matching player formula; boss minimum damage floor (`level * 3`) against companions; Old God ability fallback generates level-scaled damage when ability names don't match enum; **Boss damage cap**: bosses now capped at 85% max HP per hit (non-bosses 75%); same cap applied to all 3 special ability damage paths (direct damage, life steal, multiplier attacks); **Poison system**: `CoatBlade` action type; `[B]` option in all 7 combat menu functions; `"B"` in 3 input parsers; `CoatBlade` case in 2 action processors; `ExecuteCoatBlade()` method with poison selection submenu; `ApplyPoisonEffectsOnHit()` in both single and multi-monster attack paths; poison-type-aware damage bonus (replaces flat bonus); `ActivePoisonType` cleared on coating expiry |
| `Scripts/Systems/FeatureInteractionSystem.cs` | Assassin dungeon bonus changed from healing potions to poison vials |
| `Scripts/Locations/DarkAlleyLocation.cs` | Black market poison purchase changed from instant blade coating to 3-vial inventory addition |
| `Scripts/BBS/DoorMode.cs` | BBS doors auto-enable online mode; `--online` flag still accepted for backwards compatibility; updated help text |
| `Scripts/Core/GameEngine.cs` | `PendingImmortalAscension` handler now routes player to Pantheon instead of ending session; `SwitchToAltCharacter` now updates `PlayerSession.ActiveCharacterName` so room registry shows alt's name |
| `Scripts/Locations/DungeonLocation.cs` | Dungeon guide (`[G]`): `ShowDungeonNavigator()` with BFS pathfinding, `BuildDirectionPath()`, quicknav menu; `IsDirectionFullyCleared()` for exit annotations; `TryCompanionNavigationComment()` and `TryCompanionBacktrackComment()` for companion dialogue; fixed post-ending crash when `PendingImmortalAscension` is set (3 locations); all trap/hazard damage now floors HP at 1 (room traps, chest traps, shrine drain, riddle/puzzle failures, orb touch); removed redundant 1.3x boss room STR boost (was double-dipping with `GetAttackPower()`) |
| `Scripts/Systems/FeatureInteractionSystem.cs` | All feature interaction damage now floors HP at 1: skill challenge failures, risk/reward trap failures, choice-based health changes, treasure traps |
| `Scripts/Systems/SevenSealsSystem.cs` | Seal progress bar now maps each slot to its specific seal number instead of ordinal count; seal 7 lore text conditional on actually having all seals |
| `Scripts/Locations/TempleLocation.cs` | Alt characters can now see and worship their own account's immortal god; filter changed from account-username match to divine-name match on current player |
| `Scripts/Server/PlayerSession.cs` | Added `ActiveCharacterName` property (mutable, defaults to account username); used by RoomRegistry for correct alt character display |
| `Scripts/Server/RoomRegistry.cs` | "Also here", arrival/departure broadcasts, and exclusion checks now use `ActiveCharacterName` instead of immutable `Username`; `BroadcastToRoom` exclusion checks both dictionary key and active character name |
| `Scripts/Systems/StreetEncounterSystem.cs` | New `GetStreetCombatTeammates()` helper builds companion+mercenary list for street combat; `FightNPC()` now passes teammates to `PlayerVsMonster()`; mugger and murder encounters also pass teammates |
| `Scripts/Systems/WorldSimulator.cs` | Sleep attack NPC level filter: attackers must be within ±5 levels of sleeper; cached sleeper level to avoid redundant DB reads |
| `Scripts/Systems/HeadlessCombatResolver.cs` | `CalculateDamage()` rewritten to approximate real CombatEngine formula: STR + STR/4 bonus + Level + WeapPow(with variance) for attack; DEF(with variance) + sqrt-scaled ArmPow absorption for defense |
| `Scripts/Locations/InnLocation.cs` | Sleep attack target list now filters by ±5 level range for both NPC and player targets; shows target level in listing |
| `Scripts/Locations/DormitoryLocation.cs` | Sleep attack target list now filters by ±5 level range for both NPC and player targets; shows target level in listing |
| `Console/Bootstrap/Program.cs` | Force `Environment.Exit(0)` after BBS door cleanup to prevent process hanging from background threads |
