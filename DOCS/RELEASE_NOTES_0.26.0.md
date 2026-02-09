# Usurper Reborn - v0.26.0 Release Notes

## Magic Shop Comprehensive Overhaul

The Magic Shop has been completely overhauled from a basic curio store into a full-featured magical destination with unique services, deep system integration, and story content.

### New Features

#### Equipment Enchanting (Crown Jewel Feature)
- **Enchant equipped weapons and armor** at the Magic Shop via Ravanella's services
- **9 enchantment tiers** ranging from Minor (+2 one stat) to Lifedrinker (+3% lifesteal)
  - Tiers 1-4: Choose a stat to boost (WeaponPower, Strength, Dexterity, Defence, Wisdom, ArmorClass)
  - Tier 5 (Divine Blessing): +3 to all stats
  - Tier 6 (Ocean's Touch): +30 mana, +4 wisdom (requires Awakening >= 2)
  - Tier 7 (Ward): +20 magic resistance, +2 defence
  - Tier 8 (Predator): +5% crit chance, +10% crit damage
  - Tier 9 (Lifedrinker): +3% lifesteal
- **Maximum 3 enchantments per item** - tracked via `[E:N]` in item description
- Cursed items cannot be enchanted
- Uses clone-register-equip pattern: clones equipment, applies bonuses, registers as dynamic equipment
- Enchanted items persist across save/load via existing dynamic equipment system
- Price scales with player level and is modified by alignment, world events, faction, city control, and loyalty discount

#### Enchantment Removal
- **Remove enchantments** from previously enchanted equipment to re-enchant with different stats
- Costs 50% of the original enchantment price
- Strips all enchantment bonuses and name suffixes, resetting to base item

#### Modern Accessory Shop
- **Buy rings, necklaces, and belts** from the modern Equipment system (not just legacy curios)
- Paginated category browser with rarity-colored item listings
- Filter by player level - only shows affordable, equippable items
- Full price modifier chain (alignment, world event, faction, city control, loyalty)
- Integrates with quest completion and achievement tracking

#### Love Spells (Relationship Magic)
- **Cast enchantments on NPCs** to improve relationships
- 4 spell tiers:
  - Charm of Fondness (+1 step, 300g base, 10 mana, Level 3+)
  - Enchantment of Attraction (+2 steps, 1,000g base, 20 mana, Level 8+)
  - Heart's Desire (+3 steps, 3,000g base, 40 mana, Level 18+)
  - Binding of Souls (+2 steps, bypasses daily cap, 8,000g base, 80 mana, Level 35+)
- Standard spells respect daily relationship cap (max 2 steps/NPC/day)
- Binding of Souls bypasses cap but only once per NPC per day
- High-Commitment NPCs (> 0.7) have 25% chance to resist
- Cannot target dead NPCs or NPCs already at Love relationship
- Evil alignment: 20% surcharge

#### Death Spells (Dark Arts)
- **Kill NPCs outright** via dark magic - expensive, consequential, alignment-shifting
- 3 spell tiers:
  - Weakening Hex (40% base, 3,000g base, 30 mana, Level 15+, +5 Darkness)
  - Death's Touch (60% base, 10,000g base, 60 mana, Level 25+, +15 Darkness)
  - Soul Severance (80% base, 30,000g base, 100 mana, Level 40+, +25 Darkness)
- INT bonus uses diminishing returns (first 30 INT: +1 per 3, after 30: +1 per 6)
- All capped at 90% max success rate
- Protected NPCs cannot be targeted (shopkeepers, companions, player's spouse, current King)
- Good alignment characters get double darkness penalty + warning prompt
- On failure: NPC becomes hostile, relationship drops sharply (-5 steps)
- On success: NPC dies, queued for respawn, nearby NPCs' relationships worsen
- Tracks death spell count for achievements and story integration

#### Mana Potions
- **New consumable unique to the Magic Shop** - mana potions restore MP in and out of combat
- Price: level * 8 gold per potion
- Maximum carry: 20 mana potions
- Restores 30 + (level * 2) mana per potion
- **Combat integration**: When using items in combat, players choose between healing and mana potions
- Fully persisted through save/load system

#### Scrying Service (NPC Information)
- **Pay gold to reveal NPC details** via Ravanella's crystal orb
- Cost: 1,000 + (level * 50) gold
- Reveals: class, level, alive/dead status, relationship level, spouse, personality traits

#### Wave Fragment - The Corruption
- **New story content**: Earn `WaveFragment.TheCorruption` through the Magic Shop
- Trigger conditions: Awakening >= 3, 3+ fragments collected, at least 1 death spell cast
- Multi-page dramatic dialogue scene with Ravanella about the nature of corruption
- Thematically tied to dark magic usage: "The corruption is not evil. It is forgetting."

#### Enhanced Shopkeeper Dialogue (Ravanella)
- **Completely rewritten talk-to-owner** with state-aware dialogue system
- **Class-specific**: All 11 character classes get unique commentary from Ravanella
- **Story-aware**: References Old God defeats, king status, marriage
- **Alignment-reactive**: Different greetings for good vs evil characters
- **Awakening-aware**: At level 5+, uses Ocean Philosophy ambient wisdom
- **Death-spell aware**: Somber tone if player has used dark arts
- **Purchase-history aware**: "Most valued patron" after 50,000+ gold spent
- **Post-fragment**: Calls player "Dreamer" after collecting TheCorruption
- **Corruption fragment trigger**: New conversation option when conditions are met

#### Loyalty Discount System
- Track total gold spent at Magic Shop
- 10,000+ gold: 5% discount
- 50,000+ gold: 10% discount
- 200,000+ gold: 15% discount
- Discount shown in menu header

### New Achievements (8 total)

| Achievement | Description | Tier |
|-------------|-------------|------|
| Apprentice Enchanter | Enchant an equipped item | Bronze |
| Master Enchanter | Enchant 10 items | Silver |
| Lovelorn Mage | Cast 5 love spells | Silver |
| Shadow Caster | Cast first death spell | Silver (Secret) |
| Angel of Death | Kill 5 NPCs via death spells | Gold (Secret) |
| Magical Patron | Spend 100,000g at Magic Shop | Gold |
| The Corruption Remembered | Collect wave fragment from Ravanella | Gold (Secret) |
| Bejeweled | Wear rings in both finger slots + necklace | Bronze |

### New Statistics Tracking
- `TotalEnchantmentsApplied` - Equipment enchantments applied
- `TotalLoveSpellsCast` - Love spells cast
- `TotalDeathSpellsCast` - Death spells cast
- `TotalMagicShopGoldSpent` - Total gold spent at Magic Shop
- `TotalAccessoriesPurchased` - Accessories purchased

### New Menu Layout
```
 Run by Ravanella the gnome              (You have X gold coins)
                                        Loyalty Discount: X%

 === Shopping ===                       === Enchanting ===
  [A]ccessories (Rings/Necklaces)        [E]nchant Equipment
  [S]ell Item                            [W] Remove Enchantment
  [I]dentify Item                        [C]urse Removal

 === Potions & Scrolls ===             === Arcane Arts ===
  [H]ealing Potions                      [V] Love Spells
  [M]ana Potions                         [K] Dark Arts
  [D]ungeon Reset Scroll                 [Y] Study Spells
                                         [G] Scrying (NPC Info)

  [T]alk to Ravanella                     [F] Fortify Curio
  [R]eturn to street
```

### Unidentified Items System
- **Rare+ dungeon loot may drop unidentified**: Items now have a chance to be unidentified based on rarity:
  - Common/Uncommon: Always identified (basic gear is recognizable)
  - Rare: 40% chance unidentified
  - Epic: 60% chance unidentified
  - Legendary: 80% chance unidentified
  - Artifact: Always unidentified
- **Mystery names by power level**: Unidentified items show type hints with power-based descriptors:
  - "Unidentified Weapon", "Ornate Unidentified Armor", "Shimmering Unidentified Ring", "Glowing Unidentified Amulet"
- **Hidden stats**: Unidentified items hide their name, stats, value, and bonuses until identified
- **Combat loot display**: When finding unidentified items, only the type hint is shown - no stats or comparison
- **Inventory display**: Unidentified items appear in magenta with "???" instead of stats/value
- **Magic Shop identification**: Pay gold (100 + level*50) to reveal full item properties with dramatic reveal
- **Persistence**: IsIdentified state saved and loaded with inventory items (backwards compatible - old saves default to identified)

### Accessory Shop UI/UX Overhaul
- **Fixed dynamic equipment leak**: Shop no longer shows enchanted item clones (filtered items with ID >= 100000)
- **Column headers** with separator line matching Armor Shop style
- **Currently equipped item** shown at top for easy comparison
- **Single-line items** with stats inline (not on separate lines)
- **Rarity-colored names** when affordable, dimmed when not
- **Upgrade/downgrade indicators** (`[+]`/`[-]`) comparing to equipped accessory
- **15 items per page** (up from 10) with page-relative numbering
- **Item detail on purchase** showing rarity, level requirement, and full stats before confirming
- **Polished category menu** with styled number keys and descriptions

### NPC Target Picker
- **Paginated NPC selection** for Love Spells, Death Spells, and Scrying services
- 15 NPCs per page with `[N]ext`/`[P]rev` navigation
- `[S]earch` to filter NPCs by name, `[C]lear` to reset
- Configurable columns: relationship level (color-coded), class, level, alive/dead status
- Shared `PickNPCTarget()` method used across all three services

### Dark Arts Overhaul
- **Full confirmation screen** before casting showing target, success %, cost, darkness shift, and consequences
- **Diminishing returns on INT**: First 30 INT gives full bonus (+1 per 3), after 30 scales at half rate (+1 per 6). Prevents high-INT builds from trivializing all tiers.
- **Current King protected**: Cannot target the current King with death spells
- **All living NPCs affected**: On successful kill, all living NPCs' relationships worsen (previously capped at first 10)
- **Box header UI** with column-aligned spell listing showing descriptive effects

### Bug Fixes & Polish
- **Unicode rendering fix**: Replaced `→`, `↑`, `↓` with ASCII-safe `-->`, `[+]`, `[-]` for BBS terminal compatibility
- **Healing potion duplicate display**: Price and gold lines were printed twice
- **Healing potion price inconsistency**: Displayed raw price but charged a different modified price. Now uses `ApplyAllPriceModifiers()` consistently.
- **Missing gold tracking**: Healing potions, curse removal, and curio enchanting now properly call `RecordGoldSpent()` and `RecordMagicShopPurchase()` for loyalty discount accumulation
- **Blocking Thread.Sleep**: Curse removal and curio enchanting used `Thread.Sleep()` which blocks the thread in BBS door mode. Converted to async `Task.Delay()`.
- **Scrying relationship names**: Were inconsistent with the rest of the Magic Shop (e.g., "Devoted" vs "Passion"). Now uses shared `GetRelationshipDisplayName()` with color-coded output.
- **Love spell direction bug**: Fixed critical bug where love spells used `direction=0` (no-op) instead of `direction=1` (improve) in `UpdateRelationship()`
- **Dead code cleanup**: Removed 5 unreachable legacy methods (~350 lines) replaced by modern async implementations

### Files Changed
- `MagicShopLocation.cs` - Complete overhaul with ~1200 lines of new features, accessory UI rewrite, NPC picker, Dark Arts rewrite, bug fixes, dead code removal
- `LootGenerator.cs` - Added `ShouldBeUnidentified()` rarity-based identification, `GetUnidentifiedName()` display helper
- `CombatEngine.cs` - Unidentified loot display (hides stats), mana potion combat usage
- `InventorySystem.cs` - Unidentified item display in backpack and manage item screen
- `Items.cs` - Added `Equipment.Clone()`, `GetEnchantmentCount()`, `IncrementEnchantmentCount()`
- `Character.cs` - Added `ManaPotions` and `MaxManaPotions` properties
- `StatisticsSystem.cs` - 5 new tracking properties and recording methods
- `AchievementSystem.cs` - 8 new achievement definitions
- `SaveDataStructures.cs` - Added `ManaPotions` to `PlayerData`, `IsIdentified` to `InventoryItemData`
- `SaveSystem.cs` - Save ManaPotions and IsIdentified fields
- `GameEngine.cs` - Load ManaPotions and IsIdentified fields
- `DailySystemManager.cs` - Unicode fix (grief stage display)
- `GameConfig.cs` - Version 0.26.0-alpha, "Magic Shop Overhaul"
