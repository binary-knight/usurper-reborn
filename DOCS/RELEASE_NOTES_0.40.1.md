# Usurper Reborn - v0.40.1 Procedural ASCII Art & Unified Combat

Adds Warsim-inspired procedural ASCII portraits for NPCs, monster family silhouettes during combat, and hand-crafted Old God boss art. All visual art is automatically skipped when Screen Reader Mode is enabled.

---

## New Features

### NPC Portraits
- **Procedural face generation**: Every NPC now has a unique ASCII portrait displayed when you talk to them. Portraits are assembled from interchangeable face parts (hair, eyes, nose, mouth) selected by hashing the NPC's name — the same NPC always gets the same face.
- **Race-aware features**: Each of the 10 races has distinct visual traits. Elves get pointed ears and almond eyes, Dwarves always have beards, Orcs and Trolls have fangs and scars, Gnolls have muzzles, Mutants have asymmetric features.
- **Sex-aware variants**: Male and female NPCs have different hair and jawline options.
- **Class-colored borders**: The portrait border color matches the NPC's class theme (red for Warriors, yellow for Paladins, magenta for Magicians, etc.).

### Monster Silhouettes
- **15 monster family art pieces**: Each monster family (Goblinoid, Undead, Draconic, Demonic, etc.) has a unique ASCII silhouette displayed at the start of combat.
- **Color-coded by family**: Each silhouette uses the monster family's theme color.
- **Smart display**: Silhouettes show for single monsters and small groups (up to 3). Skipped for large encounters to keep combat flowing.

### Old God Boss Art
- **7 unique hand-crafted art pieces**: Each Old God has dramatic, multi-color ASCII art displayed during their boss introduction with line-by-line animation.
  - **Maelketh**: Flaming sword and battle helm in crimson
  - **Veloura**: Twisted heart with thorned roses in magenta
  - **Thorgrim**: Scales of justice and iron chains in white/gold
  - **Noctura**: Shifting shadows and dual faces in dark purple
  - **Aurelion**: Cracked halo and fading starlight in gold
  - **Terravok**: Mountain form with crystal eyes in green
  - **Manwe**: Cosmic eye in a starfield in cyan/white
- **Animated reveal**: Boss art lines appear one at a time for dramatic effect.

### Accessibility
- **Screen Reader Mode respected**: All ASCII art (NPC portraits, monster silhouettes, Old God art) is completely skipped when Screen Reader Mode is enabled, ensuring a clean text-only experience for visually impaired players.

## Unified Boss Combat System

Old God boss fights now use the **same full combat system** as regular dungeon encounters. Previously, boss fights had a simplified 5-option menu ([A]ttack, [S]pecial Attack, [H]eal, [R] Save, [F]lee) with a generic "Special Attack" that ignored your actual spells and class abilities. Now you have access to your complete arsenal when fighting Old Gods:

- **All spells and class abilities** via quickbar slots [1]-[9]
- **Defend** to reduce incoming damage by 50%
- **Items** (health and mana potions)
- **Heal Ally** to support your teammates
- **Power Attack**, **Precise Strike**, **Backstab**, and other combat maneuvers
- **Auto-combat** mode for sustained fights
- **Combat speed** toggle (Normal/Fast/Instant)
- **Retreat** still available with the boss's 20% flat flee chance

### Boss-Specific Features Preserved

While the combat menu is now unified, all unique Old God boss mechanics are preserved:

- **Phase transitions** at 50% and 20% HP with dramatic dialogue
- **Boss multi-attack** (multiple attacks per round based on the god)
- **Maelketh's spectral soldiers** spawn as actual monsters you can target individually
- **[V] Attempt to Save** option appears when you have the Soulweaver's Loom and the boss is saveable
- **Phase indicator** shown next to the boss's HP bar (Phase 1/2/3)
- **Dialogue modifiers** from pre-combat choices still affect stats (rage boost, insight, boss weakened, etc.)
- **Boss confused** state (from Thorgrim's broken logic) causes the boss to miss attacks
- **Post-combat narrative** (defeated/saved/died) unchanged

### What This Means for Players

- Spellcasters can now cast their actual spells against Old Gods instead of using a generic "Special Attack"
- Assassins can backstab Old Gods
- Barbarians can rage during boss fights
- Paladins can smite, Warriors can power attack
- Everyone can use items, defend, and employ tactical combat
- Teammates (NPC companions and dungeon party members) now use the standard AI combat system

## Training Through Use

Spells and class abilities now have a small chance to improve in proficiency each time they're used in combat. When a skill improves, you'll see a bright yellow message like "Your Fireball proficiency improved to Good!" Higher proficiency levels increase the spell or ability's effectiveness (damage, healing) through the existing Training System multipliers. The chance to improve decreases as proficiency increases — you'll improve quickly at low levels but mastery takes sustained practice.

- Works for all spell casting (single-monster and multi-monster combat)
- Works for all class abilities (menu selection, quickbar slots [1]-[9])
- Proficiency multiplier now applies to class ability damage and healing
- Improvement chances range from 15% (Untrained) down to 0% (Master/Legendary)

## Artifact Special Abilities Now Work

All seven divine artifact special abilities are now mechanically implemented in combat. Previously, these were display-only text shown when collecting the artifact — the stat bonuses worked, but the unique combat powers did nothing.

| Artifact | Special Ability |
|----------|----------------|
| **Creator's Eye** | +50% critical hit chance (multiplicative bonus on your dex-based crit) |
| **Soulweaver's Loom** | Heals 25% of max HP after every battle (victory or partial victory) |
| **Scales of Absolute Law** | Reflects 15% of damage taken back at the attacker |
| **Shadow Crown** | 30% chance to dodge any attack + 1 extra attack per round |
| **Sunforged Blade** | Attacks cannot miss (except critical failures), +100% damage vs undead and demons, heals lowest-HP ally for 10% of damage dealt |
| **Worldstone** | Take 50% reduced damage from all attacks (stacks with Defend) |
| **Void Key** | All other artifact powers are doubled during the Manwe boss fight |

## Bug Fix: "Say Nothing" During Boss Dialogue

Selecting `[0] (Say nothing)` during an Old God's pre-combat dialogue no longer causes the boss to silently despawn. Previously, saying nothing set no story flags, so the encounter fell through without combat, alliance, or any narrative. Now if the player says nothing, the god forces combat with a brief reaction line (e.g., Maelketh: "You DARE stand before me and say NOTHING?!").

## Bug Fix: Level-Locked Equipment Drops

Fixed a bug where Epic and Legendary equipment dropped from monsters could have level requirements higher than the player's level. For example, a level 25 player could pick up an Epic "Regenerating Chain Mail" that required level 45 to equip. The root cause was that when converting loot Items to Equipment objects at equip time, the Equipment factory methods hardcoded level requirements by rarity (Epic = 45, Legendary = 65), overriding the already-capped level from the loot generator. Now the player's level cap is properly transferred to the Equipment.

## Bug Fix: Team Members Disappearing (Online/MUD Mode)

Fixed a critical bug where recruited NPC team members would disappear shortly after joining your team in online/MUD mode. The root cause was that the world simulator runs on a background thread where it cannot access the current player session, so all player-team protection checks silently failed. The world sim would treat player teams as NPC-only gangs and dissolve them, let NPCs defect or leave, and even recruit random NPCs into the player's team without consent.

**What was happening:**
- You recruit an NPC to your team at Team Corner
- The world sim's background thread checks team loyalty, betrayals, and solo-team cleanup
- It tries to look up "the current player" but gets null (wrong thread)
- With no player found, your team looks like an NPC-only gang with just 1 member
- The world sim dissolves your "solo NPC gang" — your teammate is gone

**The fix:** Player team names are now tracked in a thread-safe static registry (`WorldSimulator.IsPlayerTeam()`). All team protection checks across WorldSimulator, NPCMaintenanceEngine, and EnhancedNPCBehaviorSystem now use this registry instead of trying to look up the current player. Team names are registered when you create/join a team and unregistered when you leave. On server startup, existing player teams are loaded from the database.

## Bug Fix & Rebalance: Cursed Item Purification

Removing a curse from an inventory item at the Magic Shop only cleared the "cursed" flag but left all the negative stat penalties intact. The item would no longer say "CURSED" but still had the Strength, Dexterity, Wisdom, and HP penalties from the curse. This has been completely reworked.

**Cursed items now present a meaningful risk/reward decision:**

| Choice | Power | Drawbacks |
|--------|-------|-----------|
| **Equip cursed** | 125% base power | Stat penalties, can't unequip, must destroy at Healer to remove |
| **Purify at Magic Shop** | 80% base power | Clean stats, renamed "Purified", some value lost |
| **Normal (never cursed)** | 100% base power | Baseline |

Purification fully removes all negative stat penalties but the cleansing process costs ~20% of the item's base power. The curse's dark energy was intertwined with the item's strength, and extracting it weakens the item. This makes equipping a cursed weapon a genuine gamble — you get significantly more power, but you're stuck with the penalties until a Healer destroys it.

## Mana Potion & Spellcaster Overhaul

Spellcasters were starved for mana. Mana potions barely restored anything, you couldn't carry enough, and they were only sold at the Magic Shop. The entire mana economy has been overhauled:

**Mana Potion Changes:**
- **Restore amount tripled**: Mana potions now restore `30 + Level × 5` MP (was `30 + Level × 2`), matching healing potion scaling
- **Max carry scales with level**: You can now carry `20 + (Level - 1)` mana potions (was capped at 20)
- **Price reduced**: `75g` base or `Level × 3g`, whichever is higher (was `Level × 8g`)
- **Sold at Healer**: New `[M]ana Potions` option at the Healer alongside healing potions
- **Sold by Dungeon Monk**: The wandering monk now offers both healing and mana potions
- **Auto-use after combat**: Mana potions are now automatically used after winning a fight (like healing potions) if you're a spellcaster and your mana is below max

**Mana Regeneration:**
- **Combat mana regen**: `2 + Wisdom/10` MP per round, capped at 15 (was `1 + Wisdom/20`, capped at 4)
- **Inn rest**: Resting at the Inn now restores 25% of max mana in addition to HP
- **Healer full heal**: Now also restores mana to maximum

## Bug Fix: Enchantment Special Properties Now Work

Three enchantments from the Enchantment Forge were broken — they set values on your equipment but those values were never read by the combat system and were lost when saving/loading your game:

- **Predator** (+5% crit chance, +10% crit damage): Now properly increases your critical hit chance and critical damage multiplier. You'll see the difference in your crit chance display and feel it when crits land harder.
- **Lifedrinker** (+3% lifesteal): Now heals you for 3% of all damage you deal in combat. Stacks across multiple enchanted items. Shows "Your weapon drains X life! (Lifedrinker)" in combat.
- **Ward** (+20 magic resistance): Now properly adds to your magic resistance stat shown in your Wisdom tooltip.

Additionally, the **Fortify Curio** menu option in the Magic Shop was using an older enchantment system that modified inventory items (rings, amulets) but those modifications were never read by the stat system. This option now opens the **Enchantment Forge** instead, which works on your equipped gear and properly persists all bonuses.

---

## Files Changed

| File | Changes |
|------|---------|
| `Scripts/Core/GameConfig.cs` | Version bump to 0.40.1 |
| `Scripts/UI/PortraitGenerator.cs` | **NEW** - Procedural NPC portrait generation with DJB2 hashing, race/sex-aware variant pools, class-colored borders |
| `Scripts/UI/MonsterArtDatabase.cs` | **NEW** - 15 hand-crafted monster family ASCII silhouettes |
| `Scripts/UI/OldGodArtDatabase.cs` | **NEW** - 7 hand-crafted Old God boss art pieces with multi-color ANSI |
| `Scripts/Locations/BaseLocation.cs` | Added NPC portrait display in `InteractWithNPC()`, gated on Screen Reader Mode |
| `Scripts/Systems/CombatEngine.cs` | Added monster silhouette display; unified boss combat; training improvement messages; artifact combat effects (Shadow Crown dodge/extra attack, Worldstone damage reduction, Scales of Law reflection, Sunforged Blade miss prevention/holy damage/ally heal, Soulweaver's Loom post-battle heal, Void Key `IsManweBattle` doubling); fixed equipment MinLevel transfer; wired equipment lifesteal (Lifedrinker) into single and multi-monster combat; wired equipment crit damage bonus into crit multiplier; mana potion restore amount tripled (`30 + Level*5`); auto-use mana potions after combat (`AutoRestoreManaWithPotions`); monk now offers both healing and mana potions; combat mana regen uses `StatEffectsSystem.GetManaRegenPerRound()` |
| `Scripts/Systems/StatEffectsSystem.cs` | Creator's Eye artifact: +50% crit chance in `GetCriticalHitChance()`, doubled during Manwe fight; equipment crit chance bonus (`GetCriticalHitChance`), crit damage bonus (`GetCriticalDamageMultiplier`), and magic resistance (`GetMagicResistance`) now factor in equipment enchant bonuses; mana regen per round increased (`2 + Wisdom/10, cap 15`, was `1 + Wisdom/20, cap 4`) |
| `Scripts/Systems/ArtifactSystem.cs` | Added combat query helper methods (`HasCreatorsEye()`, `HasSoulweaversLoom()`, etc.) |
| `Scripts/Systems/ClassAbilitySystem.cs` | Added `SkillImproved` and `NewProficiencyLevel` fields to `ClassAbilityResult`; wired `TrainingSystem.TryImproveFromUse()` into `UseAbility()` with proficiency damage/healing multiplier |
| `Scripts/Systems/OldGodBossSystem.cs` | Added animated Old God art in `PlayBossIntroduction()`; rewrote `RunBossCombat()` as thin wrapper delegating to `CombatEngine.PlayerVsMonsters()`; added `CreateBossMonster()` to convert boss data to Monster objects; added modifier mapping methods; removed ~600 lines of dead code; fixed "Say Nothing" dialogue path to force combat instead of silent despawn |
| `Scripts/Systems/InventorySystem.cs` | Transfer Item.MinLevel to Equipment when equipping from inventory |
| `Scripts/Systems/SaveDataStructures.cs` | Added `CriticalChanceBonus`, `CriticalDamageBonus`, `MagicResistance`, `PoisonDamage`, `LifeSteal`, `StaminaBonus`, `AgilityBonus` to `DynamicEquipmentData` for save/load persistence |
| `Scripts/Systems/SaveSystem.cs` | Serialize new equipment enchant fields in player and NPC save paths |
| `Scripts/Core/GameEngine.cs` | Restore new equipment enchant fields in player and NPC load paths; register/unregister player teams with WorldSimulator |
| `Scripts/Systems/WorldSimService.cs` | Restore new equipment enchant fields in world sim NPC load path; load player team names from database on startup |
| `Scripts/Systems/WorldSimulator.cs` | Added thread-safe `RegisterPlayerTeam()`/`UnregisterPlayerTeam()`/`IsPlayerTeam()` static methods; fixed `CheckTeamBetrayals()`, `NPCTryJoinOrFormTeam()`, `NPCTryRecruitForTeam()`, and solo-team dissolution to use `IsPlayerTeam()` instead of broken `GameEngine.Instance?.CurrentPlayer` |
| `Scripts/Systems/NPCMaintenanceEngine.cs` | Fixed `IsNPCOnlyGang()`, `ProcessGangLoyalty()`, `FindBetterGang()` to use `WorldSimulator.IsPlayerTeam()` |
| `Scripts/AI/EnhancedNPCBehaviorSystem.cs` | Fixed `IsNPCOnlyGang()` to use `WorldSimulator.IsPlayerTeam()` |
| `Scripts/Locations/TeamCornerLocation.cs` | Register/unregister player teams when creating, joining, or leaving teams |
| `Scripts/Core/Character.cs` | Added `GetEquipmentCritChanceBonus()`, `GetEquipmentCritDamageBonus()`, `GetEquipmentLifeSteal()`, `GetEquipmentMagicResistance()` helper methods to sum enchant bonuses from all equipped items |
| `Scripts/Locations/MagicShopLocation.cs` | Redirected "Fortify Curio" menu to Enchantment Forge (fixes broken legacy item enchant system); mana potion restore display updated; mana potion price reduced; curse purification fully reworked with 80% power penalty |
| `Scripts/Locations/HealerLocation.cs` | Added `[M]ana Potions` menu option with `BuyManaPotions()` method; full heal now also restores mana to maximum |
| `Scripts/Locations/InnLocation.cs` | Rest at table now restores 25% max mana |
| `Scripts/Locations/DungeonLocation.cs` | Monk potion vendor now offers both healing and mana potions; updated buy menu display |
| `Scripts/Core/Character.cs` | `MaxManaPotions` now scales with level (`20 + (Level - 1)`, was hardcoded 20) |
