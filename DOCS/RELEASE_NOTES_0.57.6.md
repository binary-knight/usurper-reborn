# v0.57.6 - Auto-Pickup Downgrade Hotfix

Follow-up for a report from Lumina (Lv.72 Elf Magician, online mode) immediately after v0.57.5 shipped.

## The report

> "Aldric just auto equipped worst weapon than he was using. I choose Pass after a fight and he grabbed it.
> Used: Soldier's Sword of Thunder (Blessed) [WP:407 AP:3 Def:+3 Str:+3 Dex:+3 Wis:+3 Crit:5% Leech:5%]
> Exchanged to: Fine Tempered Blade [WP:123] No bonuses.
> I fixed that, thankfully. The original weapon was in my inventory."

The auto-pickup code in `CombatEngine.TryTeammatePickupItem` is supposed to only equip items on teammates when they're strict upgrades. WP 123 < WP 407 — clearly not an upgrade — so something was bypassing the comparison.

## Root cause

The upgrade check computes `currentPower` from `teammate.GetEquipment(slot)`. If that returns null, `currentPower` stays 0 and `slotIsEmpty = true`, which then treats ANY item with non-zero power as a "100% upgrade."

`GetEquipment` can return null for two reasons:
1. The slot has no entry in `EquippedItems` (truly empty — correct to treat as upgrade).
2. The slot HAS an entry, but `EquipmentDatabase.GetById()` can't resolve it (phantom slot).

Case 2 happens in MUD mode when dynamic-equipment registration drifts between session ticks — the ID is still stored on the teammate but the lookup table no longer has a matching Equipment object. The auto-pickup logic couldn't tell the difference, so a slot that actually held Aldric's Sword of Thunder looked "empty" and the first weak weapon to drop displaced it.

The displacement logic at the call site moved the phantom's real item (Sword of Thunder) to the player's inventory — which is why Lumina was able to recover it manually. Without that safety net it would have been silently lost.

## Fix

`TryTeammatePickupItem` now distinguishes the two null cases:

- If `GetEquipment(slot)` returns null AND `EquippedItems[slot]` has a non-zero ID, the slot is treated as occupied-but-unknown. Auto-pickup refuses to touch that slot for this loot item. Better to skip a legitimate upgrade than replace a valuable item we can't introspect.
- If `GetEquipment(slot)` returns null AND `EquippedItems[slot]` is absent or 0, the slot is genuinely empty and auto-pickup proceeds as before.

A warning-level log fires whenever the phantom-slot path triggers, under category `COMPANION_EQUIP`, naming the teammate, slot, and unresolved ID. If this log line starts appearing in production we have concrete evidence of a registration gap somewhere upstream — the actual fix for the gap is a separate investigation, but this guard prevents item-loss in the meantime.

## Why it didn't show up earlier

The phantom-slot case needs all three conditions:
1. A teammate with a dynamic-equipment item equipped (not default starting gear).
2. Loot dropping that the player declines via `Pass`.
3. The registration gap being active at the moment of comparison.

Dynamic gear on companions is common. Pass-on-loot is common. The registration gap appears sporadic in MUD mode, tied to concurrent player activity or world-sim tick timing. Most playthroughs never line up all three. Lumina did, and reported it in enough detail to reconstruct the chain.

### Files Changed

- `Scripts/Core/GameConfig.cs` — Version 0.57.6.
- `Scripts/Systems/CombatEngine.cs` — `TryTeammatePickupItem` now checks `teammate.EquippedItems[slot]` before treating a null `GetEquipment` result as "empty slot." Phantom-slot case logs a COMPANION_EQUIP warning and skips the pickup. Fixes the Lumina Aldric-downgrade report.
