# v0.57.1 - Disappearing Epic Loot + CombatEngine Audit Hotfix

Three bug fixes: one from a player report (Lumina, floor 61) about epic loot vanishing after a companion equip cancellation, and two from a comprehensive 4-agent audit of `CombatEngine.cs`.

## Disappearing Epic Loot on Companion Equip Cancel

Reproduced flow from Lumina's report:
1. Monster drop offers an epic loot item (e.g. Tempered Blade of Penetration, +115 ATK, requires L54).
2. Player selects a companion (Vex) via the party selector in the loot prompt.
3. Player hits `[E]` to equip the weapon on Vex.
4. For one-handed weapons, the game prompts "which slot?" (main / off-hand).
5. Player cancels the slot prompt (e.g. after seeing a level-requirement warning).
6. Game says "Added to inventory" — but the item is nowhere in the player's inventory when they next open it.

Root cause: at [CombatEngine.cs:7461](Scripts/Systems/CombatEngine.cs#L7461) the local `player` variable is reassigned to the selected companion when the party selector is used. The successful-equip path already accounted for this (tracking `actualPlayer = currentPlayer ?? player` at the bottom of the method) but the TWO slot-cancellation fallback branches — one-handed weapon slot cancel at ~line 7656, and two-finger-full ring slot cancel at ~line 7675 — both did `player.Inventory.Add(lootItem)`, which at that point was the companion's inventory. The item wasn't destroyed: it landed in the companion's private inventory where the player's main inventory menu doesn't show it.

Same bug applies to regular rarity drops — players just notice it more on epic/legendary drops because those get visual emphasis in combat and the player immediately tries to manage them.

Fix: moved the `actualPlayer = currentPlayer ?? player` resolution to the TOP of the equip branch and changed the two slot-cancellation `player.Inventory.Add` calls to `actualPlayer.Inventory.Add`. Cancelled-slot items now always land in the real player's inventory, matching the displayed "Added to inventory" message.

## Weaken Debuff: Broken in Both Directions

- The "weaken" special-effect handler in multi-monster combat (Cleric and various AoE utility spells) did `monster.Strength -= 30%` and `monster.Defence -= 20%` — **directly mutating base stats permanently**, with no restoration when the debuff expired. Since `Monster.Reset()` doesn't backup/restore base stats, a repeatedly-weakened respawning boss would eventually have its stats clamped to near-zero.
- Meanwhile, every OTHER path that applied Weaken (single-monster abilities, AoE spells setting `WeakenRounds` directly, Maelstrom of the Faithful, Tidal Strike, and many more) only set the `WeakenRounds` counter without mutating stats. Those paths' Weaken was a **no-op on damage and defense** — the counter's only consumers were Tidesworn Riptide's +40% bonus, Assassinate's "weakened target" check, and the status display.
- On top of that, `WeakenRounds` was never decremented anywhere, so the status-bar `WEK(n)` indicator was cosmetic — Weaken never expired.

Fix: Weaken now works transiently through `Monster.GetAttackPower()` and `Monster.GetDefensePower()` — if `WeakenRounds > 0`, attack is multiplied by 0.70 and defense by 0.80. The direct stat-mutation in the multi-monster handler is removed. `WeakenRounds` now decrements once per monster-action cycle (same site as `CorrodedDuration` and `ConfusedDuration`) and falls off correctly.

## Teammate Combat Init: Only Reset Tank Buffs

The per-teammate combat-init block at [CombatEngine.cs:676+](Scripts/Systems/CombatEngine.cs#L676) only cleared the v0.56.0 tank-ability buffs (TempDamageReductionPercent, TempThornReflectPercent, TempPercentRegenPerRound). The legacy transient buffs — TempAttackBonus, TempDefenseBonus, MagicACBonus, DodgeNextAttack, HasBloodlust, HasStatusImmunity, DeathsEmbraceActive, StatusLifestealPercent, CalmWatersRounds — weren't reset for teammates. A Bard's song buff on an NPC teammate, a Shaman Calm Waters shield, or a Paladin-cast Protection spell would silently leak across combats on the teammate side.

Fix: teammate init now mirrors the player-init block and resets every transient buff, not just the new tank ones.

## Inventory Cap Bypass via Companion Displacement

Reported by Aura Maximillion (L45 Mutant Assassin, Floor 40) with the receipt "inventory full 127/50 which definitly is wrong." Confirmed: three sites in `CombatEngine.cs` moved items displaced from a companion's equipment slots (during a loot auto-equip) into the player's inventory without checking `IsInventoryFull`, letting carry grow past `MaxInventoryItems`:

- [CombatEngine.cs:7725](Scripts/Systems/CombatEngine.cs#L7725) — manual `[E]` equip on a companion via the loot prompt's `<`/`>` selector.
- [CombatEngine.cs:7804](Scripts/Systems/CombatEngine.cs#L7804) — companion auto-pickup on the default (pass) branch when they want to upgrade.
- [CombatEngine.cs:8239](Scripts/Systems/CombatEngine.cs#L8239) — grouped-follower auto-pickup (same pattern in the group-combat path).

Every time a companion auto-equipped a dungeon drop, their old slot item cascaded into the player's bag unconditionally. Over a long dungeon run this compounded into the 127/50 Aura reported, and downstream shop code correctly treated the inventory as over-full.

Fix: each of the three sites now honors `IsInventoryFull`. If the player's bag is full, the displaced item is dropped with a "couldn't fit" message instead of silently added. New localization key `combat.loot_displaced_dropped` added in all 5 language files.

## Companion Loot Pickup Save Race

Two sites in `CombatEngine` (around [line 7820](Scripts/Systems/CombatEngine.cs#L7820) and [line 8257](Scripts/Systems/CombatEngine.cs#L8257)) persist companion loot pickups via fire-and-forget `_ = Task.Run(async () => { ... save ... })`. Both sites are inside an `async` method, so the fire-and-forget was gratuitous — and it opened a window where a player who disconnected immediately after their companion picked up rare loot would lose that equipment because the background save hadn't completed yet.

Fix: await the save directly on the enclosing async path. No behavioral change under normal play (the save is quick), but the loot is now guaranteed to persist before combat continues.

## Auction Buyer Item Loss on Corrupt JSON

Auction purchase flow at [BaseLocation.cs:8138+](Scripts/Locations/BaseLocation.cs#L8138) had a commit-order bug. The original sequence:

1. `backend.BuyAuctionListing()` marks the auction `status='sold'` in the DB.
2. Buyer's gold deducted.
3. Listing JSON deserialized to an `Item`. If JSON is corrupt or null, the catch just logs and shows an error message.

Result: corrupt listings cost the buyer gold AND destroyed the item (auction marked sold with no buyer receipt).

Fix: deserialize the item FIRST. If it's null or throws, call a new `RefundAuctionListing` backend method that flips the status back to `'active'` — buyer keeps gold, listing returns to the market. Only after successful deserialization do we deduct gold, add the item, and force a save so the purchase persists through immediate disconnect.

## HomeLocation NPC Equipment Save Cadence

A follow-on equipment-system audit surfaced one more real bug. `TeamCornerLocation` saves after every NPC-equipment edit (per-action: equip, unequip, take-all — saves fire individually). `HomeLocation`'s equivalent spouse/lover-equipment flow only saved AFTER the player exited the menu via `Q`. If the MUD session disconnected or the process died mid-menu after several edits, everything since the last auto-save tick was lost.

Fix: the `ManageCharacterEquipment` loop in HomeLocation now saves after each edit action, matching TeamCornerLocation's pattern.

### Files Changed

- `Scripts/Core/GameConfig.cs` — Version 0.57.1
- `Scripts/Systems/CombatEngine.cs` — `HandleEquipmentDropInput` slot-cancellation fallbacks now route items to `actualPlayer.Inventory` instead of the (possibly reassigned) `player.Inventory` (epic loot vanish fix); multi-monster "weaken" handler no longer mutates `monster.Strength`/`Defence` (just sets `WeakenRounds`); added `WeakenRounds` per-round decrement alongside the existing `CorrodedDuration`/`ConfusedDuration` ticks; teammate combat init now resets all transient buffs (TempAttackBonus, TempDefenseBonus, MagicACBonus, DodgeNextAttack, HasBloodlust, HasStatusImmunity, DeathsEmbraceActive, StatusLifestealPercent, CalmWatersRounds) in addition to the v0.56.0 tank-buff subset.
- `Scripts/Core/Monster.cs` — `GetAttackPower` applies 0.70 multiplier when `WeakenRounds > 0`; `GetDefensePower` applies 0.80 multiplier when `WeakenRounds > 0`. Weaken is now a transient debuff that self-expires.
- `Scripts/Locations/HomeLocation.cs` — `ManageCharacterEquipment` now saves after each edit action (equip/unequip/take-all) instead of only on menu exit, matching TeamCornerLocation's per-action save cadence. Prevents spouse/lover equipment edits from being lost if the session disconnects mid-menu.
- `Scripts/Locations/BaseLocation.cs` — `ShowAuctionItemDetails` purchase flow now deserializes the auction item JSON BEFORE deducting gold; on failure the listing is un-sold via `RefundAuctionListing` and the buyer keeps their gold. On success, a forced save persists the new item immediately.
- `Scripts/Systems/SqlSaveBackend.cs` — New `RefundAuctionListing(listingId)` method flips a sold listing back to `active` status so a corrupt-JSON purchase can be safely rolled back.
- `Scripts/Systems/CombatEngine.cs` — Companion loot-pickup save paths at lines 7820 and 8257 converted from fire-and-forget `_ = Task.Run(...)` to awaited calls. Prevents companion-equipped loot from being lost if the player disconnects immediately after the pickup. Three companion-displacement paths (manual `[E]` equip, default-branch auto-pickup, grouped-follower auto-pickup) now honor `IsInventoryFull` before adding displaced gear to the player's inventory — closes the cap-bypass Aura reported.
- `Localization/*.json` — New `combat.loot_displaced_dropped` key for the "pack full, dropped" message, translated across all 5 languages.
