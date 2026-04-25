# v0.57.16 - NPC Memory Bloat Fix + Potion Cost Rebalance

## NPC Memory Bloat (Single-Player Save Size Inflation)

Root cause in `Scripts/AI/MemorySystem.cs`:

```csharp
private const int MAX_MEMORIES = 100;
private const int MEMORY_DECAY_DAYS = 7;

public void RecordEvent(MemoryEvent memoryEvent)
{
    memoryEvent.Timestamp = DateTime.Now;
    memories.Add(memoryEvent);

    if (memories.Count > MAX_MEMORIES)
    {
        var toRemove = memories
            .Where(m => m.GetAge().TotalDays > MEMORY_DECAY_DAYS / 2)  // ← BUG
            .OrderBy(m => m.Importance)
            .Take(memories.Count - MAX_MEMORIES)
            .ToList();
        foreach (var memory in toRemove) memories.Remove(memory);
    }
}
```

The cap looks strict, but the trim is gated on `m.GetAge().TotalDays > 3.5`. During an active play session no memory is 3.5 wall-clock-days old yet, so `toRemove` is empty and the cap never fires. Memories grow unboundedly. Across 130 NPCs in single-player (the player save serializes them all), every world-sim tick + every player action accumulates ~one memory per NPC. At ~150-300 bytes per JSON memory entry × 130 NPCs that's ~30-40 KB per minute, which matches Xander's observation almost exactly.

Selling loot increases save size for the same reason — the act of saving runs a world-sim tick, NPCs witness shop activity, more memories. Every save grows the file.

**Fix, three layers:**

1. **Strict cap.** `RecordEvent` now drops the lowest-importance + oldest memories whenever `memories.Count > MAX_MEMORIES`, regardless of age. The age filter was the bug; removing it makes the cap actually fire.

2. **Lower cap.** `MAX_MEMORIES` 100 → 30. With 130 NPCs that's still 3,900 memories total (~780 KB serialized) instead of 13,000 (~2.6 MB). Trim is by ascending importance + timestamp, so the keepers are always the most narratively meaningful.

3. **Save-time defensive trim.** `SaveSystem.SerializeMemories` independently keeps top-30-by-importance even if the in-memory list is larger. Pre-fix saves loaded with bloated memory lists self-heal on next save: load → trim down to 30 highest-importance → write back at the new size.

**For existing saves:** load it once, save it (manually or via autosave), and the file shrinks. From his reported 1,615 KB it should drop back close to the 312 KB baseline plus actual story content. New saves stay small permanently.

This complements the v0.57.14 save-resilience fixes (which let players see and recover saves regardless of size) — together: bloated saves load and self-heal, fresh saves never grow this way again.

## Potion Cost Rebalance + Carry Cap

### Why

Potions were trivially affordable at endgame. Linear scaling at `100 + 10×level` meant a Lv 100 character paid 1,100g per healing potion — nothing against the millions in their bank. Players stockpiled to the 50-potion cap and chugged through fights instead of leveraging healer classes (Cleric, Paladin, Sage, Bard, Mystic Shaman). With the actual healing kit those classes now have, potions should be a complement, not a substitute.

The goal: keep potions accessible at low levels (no punishing new players), make them a real expense at endgame, and break the stockpile pattern entirely.

### Two Changes

**1. Quadratic level scaling.** Cost formula switched from `base + level × 10` to `base + level² × 0.5`. New helper methods on `GameConfig`:

```csharp
public static long GetHealingPotionCost(int level) =>
    HealingPotionBaseCost + (long)((long)level * level * HealingPotionQuadraticFactor);

public static long GetManaPotionCost(int level) =>
    ManaPotionBaseCost + (long)((long)level * level * ManaPotionQuadraticFactor);
```

Healing potion cost across the curve (mana mirrors with +50g flat):

| Level | Old (linear) | New (quadratic) | Δ |
|---|---|---|---|
| 1   |   110g |    100g | -10g |
| 10  |   200g |    150g | -50g |
| 20  |   300g |    300g | even |
| 30  |   400g |    550g | +150g |
| 50  |   600g |  1,350g | +750g |
| 75  |   850g |  2,912g | +2,062g |
| 100 | 1,100g |  5,100g | +4,000g (~4.6×) |

Below ~Lv 25 the new price is the same or slightly cheaper than before. The break point sits inside normal-progression range so most active players notice the increase as their character grows. By Lv 100 a potion costs almost five times the old rate, which is what's needed against bank balances of 1M+.

**2. Carry cap reduced 50 → 20.** `GameConfig.MaxHealingPotions = 20`. Stockpiling to chug-through-content-without-thinking is now structurally impossible regardless of how rich the player is. Forces visits to the healer / shop, and at high levels makes Cleric / Paladin spell heals a meaningfully better option for sustained dungeon runs.

## What This Means For Players

- **Low-level (Lv 1-20):** essentially unchanged. Same or slightly lower prices.
- **Mid-game (Lv 30-50):** noticeably more expensive. A Lv 50 player paying 1,350g/potion is paying ~2-3 kills' worth, vs the old 1 kill. Inventory cap of 20 forces rebuying.
- **Endgame (Lv 75+):** potions become a real gold sink. At Lv 100, filling the 20-potion cap costs 102,000g (vs the old 22,000g). Healer classes become the cheap-and-renewable option for sustained healing.
- **Existing inventory:** if a player already has 35-50 potions stockpiled, those don't disappear — they just won't refill above 20 at vendors. Natural decay.

## Files Changed

- `Scripts/Core/GameConfig.cs` — version bump 0.57.15 → 0.57.16. Replaced `HealingPotionLevelMultiplier` (10) and `ManaPotionLevelMultiplier` (10) with `HealingPotionQuadraticFactor` (0.5) and `ManaPotionQuadraticFactor` (0.5). New helper methods `GetHealingPotionCost(level)` and `GetManaPotionCost(level)` consolidate the formula. `MaxHealingPotions` 50 → 20.
- `Scripts/AI/MemorySystem.cs` — `MAX_MEMORIES` 100 → 30. `RecordEvent` cap is now strict: drops lowest-importance + oldest regardless of age (was previously gated on age and never fired during a session).
- `Scripts/Systems/SaveSystem.cs` — `SerializeMemories` keeps top-30-by-importance defensively, so pre-fix bloated saves shrink on first save after the upgrade.
- `Scripts/Locations/HealerLocation.cs` — three call sites updated to use helpers. Healer mana-potion menu, mana-potion purchase price, and the existing private `GetHealingPotionCost` shim.
- `Scripts/Locations/MagicShopLocation.cs` — two call sites updated (healing potion + mana potion purchase paths).
- `Scripts/Locations/DungeonLocation.cs` — three call sites updated. Dungeon merchant healing potion now uses `GetHealingPotionCost(currentDungeonLevel) - 15` to preserve the small dungeon-deep discount. Two monk-vendor mana-potion display sites + one mana-cost calculation also moved to the helper.
- `Scripts/Systems/CombatEngine.cs` — combat-pause "buy from monk" mana-potion cost moved to the helper.

## Deploy Notes

Game binary only. No save format change. Existing saves load fine; existing potion stockpiles are preserved but won't refill above the new cap. Bloated single-player saves (from before the memory cap fix) will shrink on the first save after upgrading.
