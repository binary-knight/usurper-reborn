# Usurper Reborn v0.47.5 — Combat Balance, Enchantments & Companion Combat

## Enchantments Now Apply to Abilities

Equipment enchantments previously only triggered on regular attacks. Class abilities — which most builds rely on at higher levels — bypassed all post-hit effects entirely. A Warrior using Power Strike with a Lifedrinker weapon got zero HP drained, while a weaker regular attack would heal them.

Now all six post-hit enchantment effects apply to ability damage:

- **Lifesteal** (equipment Lifedrinker, divine blessing, divine boon) — heals attacker based on damage dealt
- **Elemental procs** (fire, frost, lightning enchants) — chance to proc bonus damage or debuffs
- **Sunforged Blade ally healing** — heals lowest-HP teammate for 10% of damage
- **Poison coating on-hit** — applies poison status effects (Nightshade sleep, Hemlock weaken, etc.)

All damage paths — single-monster attacks, multi-monster attacks, single-target abilities, multi-target abilities, and AoE abilities — now use a unified `ApplyPostHitEnchantments()` pipeline.

## Abilities Can Critically Hit

Class abilities now roll for critical hits using the same DEX-based crit system as regular attacks. Equipment crit chance and crit damage bonuses apply. Critical ability hits display "CRITICAL ABILITY!" and are tracked in combat statistics.

## Companion Dual-Wield Fixed

Companions and teammates equipped with two one-handed weapons now properly attack twice per round — a main-hand strike followed by an off-hand strike at 50% damage. Previously, companions always attacked exactly once regardless of their weapon setup. This applies to both single-monster and multi-monster combat. In multi-monster combat, if the companion's target dies mid-swing, they retarget to the next weakest monster.

## Multi-Monster Enchantment Gaps Fixed

Regular attacks in multi-monster combat were missing lifesteal (all 3 sources), elemental enchant procs, and Sunforged Blade ally healing — only poison coating was applied. AoE abilities were missing divine lifesteal, divine boon lifesteal, poison on-hit, and Sunforged healing. All paths now use the same unified pipeline.

## Conway Neighbor Pressure (NPC Behavior)

NPCs now react to population density at their current location, inspired by Conway's Game of Life. Simple neighbor-count rules create emergent clustering, dispersal, and migration patterns:

- **Isolation** (0-1 allies, ≤2 total neighbors) — NPC is more likely to move and seek company
- **Stability** (2-3 allies at location) — social activities boosted; NPCs settle in and socialize
- **Overcrowding** (6+ neighbors) — NPCs disperse; social activities suppressed
- **Hostile territory** (2+ rivals present) — NPCs flee or channel aggression into dungeon runs
- **Safe haven** (2+ allies, no rivals) — NPCs rest and train

Movement is also density-aware: when relocating, NPCs prefer destinations with small groups (1-3 NPCs) and avoid overcrowded locations (6+).

## Group Dungeon: Player Echoes No Longer Spawn

When two or more real players grouped up and entered the dungeon together, the leader's game also loaded AI-controlled "player echoes" of those same players from the database. This meant each group member appeared twice — once as a live player with full control, and once as an AI echo fighting alongside them. Now when a player is in a real group, player echo loading is skipped entirely. The real players join live via the group follower system as intended. NPC companions and mercenaries still load normally and are capped to make room for group members.

## Combat Balance: Fights Last Longer

Live balance data showed 31% of all combats ending in a single hit and an average fight length of only 2.7 rounds. Warriors in particular could one-shot monsters 5-10 levels above them. The root cause: player damage scaled quadratically (STR + STR/4 stacking) while monster HP only scaled linearly.

Four changes address this:

- **Monster HP increased ~45%**: Formula changed from `35×level + level^1.15×10` to `50×level + level^1.2×15`. A level 25 monster now has ~2,058 HP instead of ~1,353.
- **Monster defense penalty removed**: Monsters had a hidden 0.8x multiplier on defense, making their already-low defense even weaker. Removed entirely.
- **Warrior STR growth reduced**: +4 → +3 per level (still highest among non-Barbarian classes). Barbarian reduced from +5 → +4.
- **Weapon damage double-dip fixed**: Weapon power was added to damage twice — once as base, once as a random bonus up to weapon power. Now adds base + random(half base) for more consistent, lower damage.

Expected result: fights should now average 4-6 rounds instead of 2-3, with one-hit kills reserved for significantly weaker monsters. Bosses and mini-bosses benefit from the same HP increase via their multipliers.

## Shadows Faction: Standing Check Relaxed

Players with enough Darkness (200+) could still be blocked from joining The Shadows if their faction standing was even slightly negative (e.g., -1 from helping The Crown). The Shadows now only reject at Hostile (-50) or worse, and high Darkness overrides even that — a criminal gang doesn't care about your reputation if you're dark enough.

## Flaky Loot Drop Tests Fixed

Two CI tests (`GenerateBossLoot_CanDropAllTypes` and `GenerateMiniBossLoot_CanDropAllTypes`) intermittently failed because ring and necklace drops each have only ~2.4% chance per loot roll (4% of the armor pool, which is 60% of total drops). With 100 iterations, the tests had roughly a 9% chance of never seeing a ring. Increased to 500 iterations with early exit on success, reducing false failure probability to under 0.01%.

## Files Changed

- `GameConfig.cs` — Version 0.47.5; Conway neighbor pressure constants
- `Scripts/Systems/CombatEngine.cs` — New `ApplyPostHitEnchantments()` helper method consolidating all 6 post-hit effects; crit roll added to `ApplyAbilityEffects` (single-monster) and `ApplyAbilityEffectsMultiMonster` (multi-monster); single-monster regular attack path refactored from inline code to helper call; multi-monster regular attack path upgraded from poison-only to full helper; AoE `ApplyAoEDamage` upgraded from partial to full helper; `ProcessTeammateAction` (single-monster) now loops through `GetAttackCount()` with dual-wield off-hand support; `ProcessTeammateActionMultiMonster` same dual-wield loop with retargeting on kill; weapon damage double-dip fixed
- `Scripts/Systems/WorldSimulator.cs` — New `ApplyNeighborPressure()` Conway-inspired weight modifier in NPC activity pipeline; density-aware movement in `MoveNPCToRandomLocation()`
- `Scripts/Locations/DungeonLocation.cs` — Skip `RestorePlayerTeammates()` (AI player echoes) when the player is in a real group; real group members join live via `EnterAsGroupFollower()` instead
- `Scripts/Systems/MonsterGenerator.cs` — Monster HP formula increased ~45% (`50×level + level^1.2×15`); removed 0.8x defense multiplier
- `Scripts/Locations/LevelMasterLocation.cs` — Warrior STR per level +4→+3; Barbarian STR per level +5→+4
- `Scripts/Systems/FactionSystem.cs` — Shadows faction standing check relaxed: only blocks at Hostile (-50) or worse; high Darkness (200+) overrides standing
- `Tests/LootGeneratorTests.cs` — Increased iterations from 100 to 500 in both `CanDropAllTypes` tests to eliminate flaky failures from low-probability ring/necklace drops
