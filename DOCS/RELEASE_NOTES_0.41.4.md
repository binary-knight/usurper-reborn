# v0.41.4 - Balance & Bugfixes

Major balance overhaul addressing rapid leveling, overpowered dungeon loot, "untouchable" players, and companion survivability. Includes a complete combat math rebalance.

---

## Bug Fixes

### Auto-Buy Gold Loss Fix
Fixed a bug where auto-buy in the Armor Shop would deduct gold even when items couldn't be equipped (e.g., due to level requirements). Players would see "Purchased 0 armor pieces for X gold" -- gold gone, nothing equipped. Now:
- Both Armor Shop and Weapon Shop auto-buy filter out items the player can't equip (level/stat requirements) so they're never offered.
- If equipping still fails for any reason, gold is refunded immediately.

---

## Balance Changes

### XP Curve Steepened
The experience curve has been steepened from `level^1.8` to `level^2.2`. Early levels (1-5) are barely affected, but mid and late game require significantly more XP to level up. This prevents players from reaching level 40+ in a matter of hours.

| Level | Old XP/Level | New XP/Level | Change |
|-------|-------------|-------------|--------|
| 5 | 831 | 1,700 | 2.0x |
| 10 | 3,155 | 7,943 | 2.5x |
| 20 | 11,615 | 36,288 | 3.1x |
| 40 | ~43,000 | ~193,000 | 4.5x |

### Quest Rewards Reduced
Quest XP and gold rewards have been significantly reduced. Quests are now a supplement to dungeon grinding, not a replacement for it.

- XP rewards reduced by ~60% across all tiers
- Gold rewards reduced proportionally
- A high-difficulty quest now awards ~12% of a level instead of ~60%

### Monster Defenses Increased
Monsters are no longer defensively neutered. Their defense and armor power multipliers have been raised, making fights take meaningfully longer and reducing the feeling that players are untouchable.

- Monster defense: +60% (multiplier 0.5x -> 0.8x)
- Monster armor power: +75% (multiplier 0.4x -> 0.7x)

### Dungeon Loot Drop Rates Reduced
Equipment drops from dungeon monsters are now less frequent, making each drop feel more impactful.

- Regular monster drops: 15% base -> 8% base, cap 40% -> 25%
- Mini-boss (Champion) drops: 100% -> 60%
- Named monster (Lord/Chief/King) drops: 60% -> 35%
- Floor boss drops: unchanged (100%)

### Loot Power Scaling Reduced
Dungeon loot power scaling with level has been slowed down. Equipment from drops is still better than shop gear, but no longer dramatically outpaces it.

- Level scaling factor reduced from `level/25` to `level/40`
- At level 20: dropped gear is ~17% weaker than before
- At level 50: dropped gear is ~25% weaker than before

### Teammate XP Share Increased
NPC teammates (spouses, lovers, team members) now receive 75% of the player's XP instead of 50%. This helps companions keep pace with the player's progression so they don't get one-shot by monsters on higher dungeon floors.

---

## Combat Math Rebalance

A comprehensive rebalance of the D20 hit/miss system, damage formulas, and stat scaling to address players becoming "untouchable" by mid-game.

### Hit Chance Rebalanced (Player Attacks)
Players were auto-hitting (95% hit rate) by level 8. The attack modifier grew far too fast relative to monster AC. Now players maintain a meaningful 65-80% hit rate through mid-game.

- Player attack modifier: STR contribution reduced from `(STR-10)/2` to `(STR-10)/3`
- Diminishing returns strengthened: half-rate above +6 changed to third-rate
- Monster AC scaling increased: `Level/3 + DEF/15` -> `Level/2 + DEF/10`

| Level | Old Hit% | New Hit% |
|-------|---------|---------|
| 1 | 55% | 55% |
| 5 | 90% | 70% |
| 10 | 95% | 75% |
| 20 | 95% | 80% |

### Monster Hit Chance Improved
Monsters were barely hitting geared players (5-10% hit rate by level 15). The armor stacking problem made high-ArmPow players nearly immune to monster attacks.

- Monster attack modifier: `Level/3` -> `Level/2` (stronger level scaling)
- Player AC: DEX contribution reduced from `(DEX-10)/2` to `(DEX-10)/3`
- Player AC: ArmPow contribution reduced from `ArmPow/15` to `ArmPow/25`

### Armor Absorption Diminishing Returns (The "Untouchable" Fix)
The core fix for player invincibility. Previously, armor absorbed `random(0 to ArmPow)` damage -- with ArmPow 300, this averaged 150 absorption against monster attacks of ~160, reducing damage to 1. Now uses square-root diminishing returns so armor still helps but can't completely negate damage.

- Old formula: `random(0, ArmPow)` -- linear, averages ArmPow/2
- New formula: `random(0, sqrt(ArmPow) * 5)` -- diminishing returns

| ArmPow | Old Avg Absorb | New Avg Absorb |
|--------|---------------|---------------|
| 50 | 25 | 17 |
| 200 | 100 | 35 |
| 500 | 250 | 56 |
| 1000 | 500 | 79 |

This means armor still reduces damage meaningfully, but monsters always deal significant damage. No more "monsters hit for 1 damage" at mid-game.

### Player Damage Reduced
Players killed same-level monsters in 2-3 hits at every level. Level scaling in the damage formula was too aggressive.

- Level contribution to attack power: `Level * 2` -> `Level * 1`
- At level 20: ~20 less damage per hit
- At level 100: ~100 less damage per hit

### Monster HP Increased
Combined with reduced player damage, monsters now survive 4-5 hits instead of 2-3, making combat feel more like a real fight.

- HP formula: `25*level + level^1.1 * 8` -> `35*level + level^1.15 * 10`
- ~45% more HP across all levels

| Level | Old HP | New HP | Change |
|-------|--------|--------|--------|
| 1 | 33 | 45 | +36% |
| 20 | 716 | 1,028 | +44% |
| 50 | 1,600 | 2,350 | +47% |
| 100 | 3,768 | 5,500 | +46% |

### Multi-Monster Combat Formula Improved
Multi-monster combat used a drastically simplified damage formula missing Level scaling and STR bonus. This created an inconsistent experience where multi-monster fights felt much weaker than single combat.

- Added Level scaling (`+ Level`) to match single combat
- Added STR damage bonus (`+ STR/4`) to match single combat
- Still uses simplified system (no D20 roll) for faster multi-combat flow

### Constitution HP Scaling Reduced
CON provided quadratic HP growth that made tanky characters exponentially harder to kill at high levels. The scaling factor has been reduced by 33%.

- HP bonus formula: `Level * (CON/10)` -> `Level * (CON/15)`
- At level 40 with CON 164: ~1,120 HP bonus (was ~1,560)
- At level 100 with CON 404: ~2,693 HP bonus (was ~4,040)

---

## Files Changed

| File | Changes |
|------|---------|
| `Scripts/Core/GameConfig.cs` | Version bump to 0.41.4; reduced quest XP/gold reward multipliers |
| `Scripts/Locations/ArmorShopLocation.cs` | Added `CanEquip()` filter to auto-buy; added gold refund on equip failure |
| `Scripts/Locations/WeaponShopLocation.cs` | Added `CanEquip()` filter to auto-buy |
| `Scripts/Locations/BaseLocation.cs` | XP curve `1.8 -> 2.2` |
| `Scripts/Locations/LevelMasterLocation.cs` | XP curve `1.8 -> 2.2` |
| `Scripts/Locations/MainStreetLocation.cs` | XP curve `1.8 -> 2.2` |
| `Scripts/Locations/DungeonLocation.cs` | XP curve `1.8 -> 2.2` |
| `Scripts/Core/GameEngine.cs` | XP curve `1.8 -> 2.2` |
| `Scripts/Core/NPC.cs` | XP curve `1.8 -> 2.2` |
| `Scripts/Systems/CombatEngine.cs` | XP curve; loot drops; teammate XP 50->75%; monster AC `Level/3->Level/2, DEF/15->DEF/10`; ArmPow absorption sqrt diminishing returns; player damage `Level*2->Level`; multi-monster formula adds Level+STR/4 |
| `Scripts/Systems/CompanionSystem.cs` | XP curve `1.8 -> 2.2` |
| `Scripts/Systems/NPCSpawnSystem.cs` | XP curve `1.8 -> 2.2` |
| `Scripts/Systems/WorldInitializerSystem.cs` | XP curve `1.8 -> 2.2` |
| `Scripts/Systems/WorldSimulator.cs` | XP curve `1.8 -> 2.2` |
| `Scripts/Systems/WorldSimService.cs` | XP curve `1.8 -> 2.2` |
| `Scripts/Systems/MonsterGenerator.cs` | Monster defence `0.5x->0.8x`; armor power `0.4x->0.7x`; HP formula `25*L+L^1.1*8 -> 35*L+L^1.15*10` |
| `Scripts/Systems/LootGenerator.cs` | Level power scaling `level/25 -> level/40` (5 locations) |
| `Scripts/Systems/TrainingSystem.cs` | Player attack mod `(STR-10)/2->(STR-10)/3`, diminishing returns `/2->/3`; monster attack mod `Level/3->Level/2`; player AC `DEX/2->DEX/3`, `ArmPow/15->ArmPow/25` |
| `Scripts/Systems/StatEffectsSystem.cs` | CON HP scaling `CON/10->CON/15` |
