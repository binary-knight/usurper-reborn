# Usurper Reborn - v0.40.0 The Price of Power

Gold is no longer something you accumulate and forget about. Every coin matters now — whether you're hiring mercenaries, training with the Master, gambling in the den beneath the Inn, or pushing your equipment beyond its limits with mythic enchantments. Power has a price, and it's always gold.

---

## New Feature: Stat Training at the Inn

A grizzled Master Trainer has set up shop in the Inn, offering to push your body and mind beyond their natural limits — for a steep price.

- **[W] Train with the Master** — new Inn menu option
- Train any of 8 stats (STR, DEX, CON, INT, WIS, CHA, AGI, STA) up to 5 times each
- Each training grants a permanent +1 to the chosen stat
- Costs escalate quadratically: your level × 500 × (times trained + 1)²
- At level 30, maxing one stat costs 825,000 gold. All 8 stats: 6.6 million gold
- Separate from the existing Training Points system (those are earned from leveling)

---

## New Feature: Gambling Den at the Inn

Descend the narrow staircase beneath the Inn to find three gambling games, each with different risk profiles.

### High-Low Dice
- Dealer rolls a die, you guess if the next roll is higher or lower
- Bet any amount of gold on hand
- Correct guess pays 1.8x your bet
- Ties return your bet
- Double-or-nothing up to 3 times on a win

### Skull & Bones (Blackjack)
- Draw bone tiles trying to reach 21 without busting
- Face tiles (Skull, Crown, Sword) = 10
- Natural 21 in two tiles pays 2.5x, normal wins pay 2x
- Dealer stands on 17

### Arm Wrestling
- Challenge a random NPC to arm wrestle for gold
- STR-based contest with randomness (your STR × 0.7-1.3 vs theirs)
- Bet based on NPC level (level × 200 gold)
- Win or lose affects NPC's impression of you
- Maximum 3 matches per day

---

## Enchanting Expansion

The enchantment system has been dramatically expanded with 5 new tiers and a risk/reward mechanic for pushing equipment to its limits.

### New Enchantment Tiers
| Tier | Name | Effect | Base Cost | Min Level |
|------|------|--------|-----------|-----------|
| 10 | Mythic | +24 to one stat | 180,000g | 55 |
| 11 | Legendary | +30 to one stat | 300,000g | 65 |
| 12 | Godforged | +38 to one stat | 500,000g | 75 |
| 13 | Phoenix Fire | +20 power + fire damage on hit | 400,000g | 60 |
| 14 | Frostbite | +20 power + chance to slow enemies | 400,000g | 60 |

### Max Enchantments: 3 → 5
Items can now hold up to 5 enchantments, but the 4th and 5th come with risk:
- **4th enchantment**: 25% chance of failure
- **5th enchantment**: 50% chance of failure
- On failure: gold is consumed AND a random existing enchantment is destroyed
- The enchanter warns you before you commit

### Elemental Enchantments
Two new elemental enchant types that add combat effects:
- **Phoenix Fire**: 20% chance per attack to deal bonus fire damage (15% of weapon damage)
- **Frostbite**: 15% chance per attack to reduce enemy defence

---

## New Feature: Rare Crafting Materials

Eight lore-themed materials drop from specific dungeon floor ranges, tied to the domains of the Old Gods. Collect them to unlock the most powerful enchantments and push your stat training to its limits.

### Materials

| Material | Floors | Tied To | Description |
|----------|--------|---------|-------------|
| Crimson War Shard | 20-35 | Maelketh (War) | A fragment of crystallized battle fury, still warm to the touch |
| Withered Heart Petal | 35-50 | Veloura (Love) | A flower petal that weeps when held, preserved from love's corruption |
| Iron Judgment Link | 50-65 | Thorgrim (Law) | A single link from the chains that bound fallen gods |
| Shadow Silk Thread | 60-75 | Noctura (Shadow) | Thread spun from living shadow, visible only in darkness |
| Fading Starlight Dust | 75-90 | Aurelion (Light) | Luminescent powder from a dying god's halo |
| Terravok's Heartstone | 85-100 | Terravok (Earth) | A stone that pulses like a sleeping giant's heartbeat |
| Eye of Manwe | 95-100 | Manwe (Creator) | An obsidian orb that shows reflections of things that never were |
| Heart of the Ocean | 50-100 | Ocean Philosophy | An iridescent pearl that hums with the memory of every wave |

### Drop Rates
- **Regular monsters**: 3% chance (within floor range)
- **Mini-bosses**: 25% chance
- **Floor bosses / Old Gods**: Guaranteed 1-2 random + thematic material
- **Treasure chests**: 8% chance

### Viewing Materials
- Type `/materials` (or `/mat`) from any location to see your collection
- Each material shows its colored name, quantity, description, and floor range

### Material Requirements for Enchantments (Tiers 10-14)
High-tier enchantments now require rare materials in addition to gold:

| Tier | Enchantment | Materials Required |
|------|-------------|-------------------|
| Mythic (+24) | 1x Fading Starlight Dust |
| Legendary (+30) | 1x Heart of the Ocean + 1x Shadow Silk Thread |
| Godforged (+38) | 1x Eye of Manwe + 1x Terravok's Heartstone |
| Phoenix Fire | 1x Crimson War Shard + 1x Fading Starlight Dust |
| Frostbite | 1x Shadow Silk Thread + 1x Fading Starlight Dust |

Material requirements are shown in the enchantment menu (green if you have them, red if missing). Materials are consumed when the enchantment is applied — even on failure for 4th/5th enchants.

### Material Requirements for Stat Training (4th/5th)
- **4th training**: Requires 1x Heart of the Ocean
- **5th training**: Requires 1x Heart of the Ocean + 1x Eye of Manwe

---

## Boss Difficulty Tuning

The final three Old Gods have been significantly buffed and now feature divine protection mechanics.

### HP Increases
| Boss | Floor | Old HP | New HP |
|------|-------|--------|--------|
| Aurelion, The Fading Light | 85 | 6,500 | 10,000 |
| Terravok, The Sleeping Mountain | 95 | 9,000 | 16,000 |
| Manwe, The Weary Creator | 100 | 12,000 | 22,000 |

### Divine Armor (New Mechanic)
Late-game Old Gods are protected by divine armor that resists unenchanted weapons:

| Boss | Protection | Damage Reduction |
|------|-----------|-----------------|
| Aurelion | Divine Shield | 25% if weapon has 0 enchantments |
| Terravok | Stone Skin | 35% if weapon has 0 enchantments |
| Manwe | Creator's Ward | 50% if weapon has 0 enchantments |

- Having **any** enchantment on your main-hand weapon removes the penalty entirely
- A warning is displayed during the boss introduction if your weapon is unenchanted
- Combat text tells you when divine armor is reducing your damage

### Artifact Damage Bonus
- Each collected artifact now grants +3% bonus damage against all Old Gods
- All 7 artifacts = +21% bonus damage
- Rewards thorough exploration before taking on the final bosses

---

## Bug Fixes

- **Equipment drops with too-high level requirements**: Dungeon loot MinLevel was based on monster level, not player level. Monsters on floor 52 could be level 75+, dropping gear requiring level 65 that the player couldn't use. Now all dungeon drops cap MinLevel to the player's current level — if you killed the monster, you earned the loot.
- **Throne abdication crashes the game**: Abdicating the throne or losing a throne challenge caused the game to shut down entirely instead of returning to Main Street. The location exit wasn't navigating anywhere after processing.

---

## Economy Rebalancing

Gold income has been significantly reduced while costs have increased, ensuring gold remains a scarce and valuable resource throughout the game.

### Alive Bonus Nerfed
- Daily alive bonus reduced from 350 to 100 gold per level
- Level 30 player: 10,500/day → 3,000/day (71% reduction)

### NPC Recruitment Costs Increased
- Recruitment cost per NPC level increased from 500 to 2,000 gold
- Level 30 NPC now costs ~60,000 gold to recruit (was ~15,000)
- Daily wage costs are displayed during recruitment

### Daily NPC Team Wages (New)
- Each NPC on your team now costs daily wages: NPC level × 100 gold per day
- Wages are automatically deducted during daily maintenance
- If you can't afford wages for 3 consecutive days, NPCs leave your team
- Departing NPCs send you a mail message explaining why they left
- News feed announces the departure

---

## Files Changed

| File | Changes |
|------|---------|
| `Scripts/Core/GameConfig.cs` | Version bump, AliveBonus 350→100, new economy/training/gambling/enchanting/material constants, `CraftingMaterialDef` class, 8 material definitions, divine armor constants, helper methods |
| `Scripts/Core/Character.cs` | New `StatTrainingCounts`, `UnpaidWageDays`, `CraftingMaterials` dictionaries; `AddMaterial()`, `HasMaterial()`, `ConsumeMaterial()` helpers |
| `Scripts/Locations/InnLocation.cs` | New `[W]` stat training menu + `HandleStatTraining()`, new `[L]` gambling den menu, material requirements for 4th/5th stat training, `GetTrainingMaterialRequirements()` |
| `Scripts/Locations/MagicShopLocation.cs` | 5 new enchant tiers, max enchants 3→5, failure mechanic, `EnchantMaterialRequirements` dictionary, material display/check/consume in enchanting flow |
| `Scripts/Locations/BaseLocation.cs` | `/materials` (and `/mat`, `/mats`) quick command, `ShowMaterials()` display method |
| `Scripts/Locations/DungeonLocation.cs` | `CheckForMaterialDrop()` and `DisplayMaterialDrop()` methods, material drops after combat and from treasure chests |
| `Scripts/Locations/TeamCornerLocation.cs` | Recruitment cost uses `GameConfig.NpcRecruitmentCostPerLevel`, daily wage displayed on hire |
| `Scripts/Systems/OldGodBossSystem.cs` | Guaranteed Old God material drops in `HandleBossDefeated()`/`HandleBossSaved()`, `GetDivineArmorReduction()`, divine armor in `PlayerAttack()`/`PlayerSpecialAttack()`, artifact damage bonus, unenchanted weapon warning in boss intro |
| `Scripts/Data/OldGodsData.cs` | HP buffs: Aurelion 6500→10000, Terravok 9000→16000, Manwe 12000→22000 |
| `Scripts/Systems/MaintenanceSystem.cs` | New `ProcessTeamWages()` — daily NPC wage deduction, unpaid wage tracking, NPC departure with mail notification |
| `Scripts/Systems/CombatEngine.cs` | New `CheckElementalEnchantProcs()` and `CheckElementalEnchantProcsMonster()` — Phoenix Fire bonus damage, Frostbite defence reduction; fixed equipment drops capping MinLevel to player level |
| `Scripts/Locations/CastleLocation.cs` | Fixed throne abdication and throne challenge defeat not navigating back to Main Street (caused game shutdown) |
| `Scripts/Core/Items.cs` | New `HasFireEnchant` and `HasFrostEnchant` bool properties on Equipment |
| `Scripts/Systems/SaveDataStructures.cs` | Added `StatTrainingCounts`, `UnpaidWageDays`, `CraftingMaterials` to PlayerData; added `HasFireEnchant`, `HasFrostEnchant` to DynamicEquipmentData |
| `Scripts/Systems/SaveSystem.cs` | Serialize/restore stat training counts, unpaid wage days, crafting materials, and elemental enchant flags |
| `Scripts/Core/GameEngine.cs` | Restore `StatTrainingCounts`, `UnpaidWageDays`, `CraftingMaterials`, and elemental enchant flags on load |
