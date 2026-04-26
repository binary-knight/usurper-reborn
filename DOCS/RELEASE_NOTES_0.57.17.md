# v0.57.17 - Bug Fix Pass

## Manwe Already Dead After Renouncing Immortality (NG+ Stuck)

Player report: beat Manwe → chose immortality → became a god → renounced godhood from the Pantheon → played NG+ → Manwe was already dead and could not be fought. Same for every other Old God the player had resolved in the previous cycle.

**Root cause: two NG+ paths, only one resets god states.**

The normal NG+ path (beat Manwe, choose NG+ at the ending screen) routes through `StoryProgressionSystem.StartNewCycle(EndingType)`, which calls the inner `StartNewCycle(carryoverItems)` overload at [StoryProgressionSystem.cs:525](Scripts/Systems/StoryProgressionSystem.cs#L525). That overload resets cycle state, then calls `InitializeOldGods()` to reset every god back to its starting status (Maelketh→Corrupted, Veloura→Dying, Manwe→whatever the start state is) and `InitializeKeyNPCs()` to reset relationships.

The renounce-immortality path in `PantheonLocation.RenounceImmortality` ([line 1206](Scripts/Locations/PantheonLocation.cs#L1206)) didn't route through that. It was a hand-written legacy migration block:

```csharp
if (story.CurrentCycle <= 1 || story.CompletedEndings.Count == 0)
{
    EndingType inferredEnding = godAlignment switch { ... };
    if (!story.CompletedEndings.Contains(inferredEnding))
        story.CompletedEndings.Add(inferredEnding);
    if (story.CurrentCycle <= 1)
        story.CurrentCycle = 2;
}
GameEngine.Instance.PendingNewGamePlus = true;
```

Two problems with that:

1. **No state reset.** Even when the block fired (cycle-1 player ascending pre-v0.47.0), it only fixed the ending-record and cycle-counter. Never called `InitializeOldGods()`. Every god the player had resolved stayed resolved. `OldGodBossSystem.CanEncounterBoss` ([line 136-140](Scripts/Systems/OldGodBossSystem.cs#L136-L140)) explicitly returns false for any god in Defeated / Saved / Allied / Consumed status, so the dungeon refused to spawn them at their floors. Manwe at floor 100 was already "dead" with no way to retrigger him.

2. **Cycle-2+ players got it worse.** The `if` guard required `CurrentCycle <= 1 || CompletedEndings.Count == 0`. A player who already finished one NG+ via the normal path had `CurrentCycle >= 2` AND `CompletedEndings.Count >= 1`, so the entire block was skipped. They got ZERO state reset on renounce — not even the partial fixup that cycle-1 players got.

The block was originally written as a one-time data migration for old saves; it was never intended to be the full NG+ reset path, but it was the ONLY thing the renounce flow did before flipping `PendingNewGamePlus`. The actual reset was missing.

**Fix:** route renouncing through `StoryProgressionSystem.StartNewCycle(EndingType)`, the same method the normal NG+ path uses. Both paths now produce identical post-renounce/post-NG+ state: cycle bumped, ending recorded, every Old God reset to starting status, key NPC relationships reset. Dedup guard added so a second renounce with the same alignment doesn't pad `CompletedEndings` with duplicates.

**For affected players:** if your character is currently stuck post-renounce with all gods resolved, the next renounce (or new character via NG+ → renounce flow) on the patched binary will reset god states correctly. Sorry to anyone who hit this before today.

## Child-Bonus Darkness/Chivalry Slammed Into the Cap

Player report: "NG+ I start with 1k darkness, which seems like its a bug, unless it was supposed to carry over from a previous life?" Their previous-cycle ending was Savior, which by design grants `+100 Chivalry` and zero Darkness on NG+ start. So 1000 Darkness is definitely not the intended bonus.

**Root cause: `FamilySystem.ApplyChildBonuses` permanently mutates `Chivalry` and `Darkness` on every `RecalculateStats()` call.**

The method computes per-child bonuses (`ChivalryBonus = goodChildren * 10`, `DarknessBonus = evilChildren * 10` for minor children only) and then applies them to the player:

```csharp
// Apply stat bonuses (these are temporary, recalculated each time)
player.MaxHP    += bonuses.BonusMaxHP;
player.Strength += bonuses.BonusStrength;
player.Charisma += bonuses.BonusCharisma;
player.Chivalry += bonuses.ChivalryBonus;   // BUG
player.Darkness += bonuses.DarknessBonus;   // BUG
```

The comment claims these are "temporary, recalculated each time." That's true for the first three because `RecalculateStats()` resets `MaxHP = BaseMaxHP`, `Strength = BaseStrength`, `Charisma = BaseCharisma` at the top of the method ([Character.cs:1100-1113](Scripts/Core/Character.cs#L1100-L1113)) BEFORE this method runs. So those three accumulate from 0 → BonusValue every recalc.

Chivalry and Darkness aren't reset by `RecalculateStats()` — they're persistent alignment, not derived stats. So every `RecalculateStats()` call permanently adds `evilChildren * 10` to Darkness (and `goodChildren * 10` to Chivalry). The v0.57.12 setter clamp at 1000 stops them from going past the cap, which is exactly why they pile up there instead of overflowing visibly.

`RecalculateStats()` fires on equip, level-up, login restore, world-sim NPC update, and a dozen other paths. The reporter has two minor evil children registered with him as the father (both age 15, souls -150 / -112). Each recalc added +20 Darkness; ~50 calls hit the cap. Over an hour of play that's trivial.

Confirmed in their actual save: Darkness=150, Chivalry=1000 (capped). Consistent with: NG+ start → child bug raced Darkness to 1000 → did good deeds → v0.57.0 paired movement drained Darkness down to 150 while pushing Chivalry into its cap.

The same bug affects players with good minor children — their Chivalry would race to 1000 the same way.

**Fix:** removed the `Chivalry +=` and `Darkness +=` lines from `ApplyChildBonuses`, removed the corresponding fields from `ChildBonuses`, and removed the misleading "+X Chivalry/Darkness (good/evil children)" lines from `GetBonusSummary`. Children's moral influence on the player should be a one-shot alignment event when a child is born or reaches a milestone — routed through `AlignmentSystem.ChangeAlignment` so v0.57.0 paired movement applies — not a continuously-applied recalc bonus.

**For affected players:** existing alignment values aren't healed. Whatever Darkness or Chivalry your character has now is where it sits — but it stops drifting upward from this point on, and you can adjust normally through gameplay.

## NG+ Children Not Disowned in Online Mode

Player report on a Lv.20 NG+ character: "I still have children they were not reset." Walked into Home as a fresh NG+ character and saw all the previous-life kids still parented to them.

**Root cause: in online mode `FamilySystem` is shared world data and isn't reset on NG+, but the child→parent linkage matches by name string — and the new NG+ character keeps the same name.**

`CreateNewGame` in [GameEngine.cs:3858-3869](Scripts/Core/GameEngine.cs#L3858-L3869) splits cleanly:

```csharp
if (!UsurperRemake.BBS.DoorMode.IsOnlineMode)
{
    UsurperRemake.Systems.FamilySystem.Instance.Reset();   // wipes _children entirely
    ...
}
else
{
    // Online mode: only clear this player's relationships, preserve NPC-to-NPC
    RelationshipSystem.Instance.ResetPlayerRelationships(playerName);
}
```

Single-player NG+ is fine — `FamilySystem.Reset()` clears `_children`, no kids to find. Online NG+ skipped that call (correct — children are world-shared, can't be globally wiped without nuking other players' families) but never disowned the NG+'ing player either. `FamilySystem.GetChildrenOf(parent)` matches via `c.Mother == parent.Name || c.Father == parent.Name` ([FamilySystem.cs:58-60](Scripts/Systems/FamilySystem.cs#L58-L60)) — and the new NG+ character has the same `Name` as the old one (BBS account name + display name carry over). Every child of the previous life still resolves to the new character.

`HomeLocation` queries via `GetChildrenOf(currentPlayer)` so the kids appear at the dinner table. Same name-keyed query backs the parenting cooldown menu, the family bonus calculator (which is the cousin bug we fixed earlier this version), child interactions, and the orphan check.

**Fix:** new `FamilySystem.DisownChildrenOf(string playerName)` method — finds every non-deleted child where `Mother == playerName || Father == playerName`, blanks the matching parent slot (`Mother`/`MotherID` or `Father`/`FatherID` set to `""`), and if both parent slots are now empty AND the child is still a minor AND not already in the orphanage, sets `Location = ChildLocationOrphanage` so the existing orphan pipeline picks them up. `OriginalMother`/`OriginalFather` stay populated so orphan narrative continuity survives (the game knows who the kid's biological parent was, even though that parent is gone).

`CreateNewGame` captures `previousLifeName = currentPlayer?.Name` at the top of the method (before any reset block touches state), and the online-mode branch now calls `FamilySystem.Instance.DisownChildrenOf(previousLifeName)` after `RelationshipSystem.ResetPlayerRelationships`. After the new save is written, online mode also fires `OnlineStateManager.SaveAllSharedState()` so the child mutations land in `world_state.children` immediately instead of waiting for the next periodic world-sim snapshot — protects against logout / restart eating the change.

Children parented by NPC partners or other players are untouched. Adult children (already independent) just get the parent slot cleared with no orphanage move; the bookkeeping change is enough.

**For affected players:** next NG+ disowns cleanly. Children already inherited from a previous bugged NG+ stay parented as they are — no retroactive heal — but you can manually disown them via existing in-game paths if needed, or just live with the previous-life family.

## Loot Level-Mismatch Not Surfaced in Group Combat

Player report from a Lv.20 character in a live-player party: "when in a party with a live player, a drop happens and it's over leveled, it doesn't seem to notify about level issues and pass/equip doesn't work."

**Root cause: all three group-loot prompt paths gated `[E] Equip` on class restriction only, never on level requirement.**

Three loot prompt sites:

1. `GetEquipmentDropInput` — leader prompt for items the round-robin assigned to them (or selected via `</>` party cycler).
2. `DisplayEquipmentDrop`'s grouped-follower section — prompt sent to a follower's `RemoteTerminal` when the round-robin gave them the drop.
3. `OfferLootToOtherPlayers` — cascade prompt sent to other grouped players when the primary recipient passed.

All three checked `LootGenerator.CanClassUseLootItem(recipient.Class, lootItem)` for class eligibility and used the result to gate the `[E]` option. None checked `lootItem.MinLevel` against `recipient.Level`. So a drop with `MinLevel=100` that landed on a Lv.20 character displayed `[E] Equip Item` as if it was usable. Pressing E then ran:

```
EquipItem → Equipment.CanEquip → "Requires Level 100" → returns false
                                                         ↓
                                  Item dumped to inventory with the
                                  failure message tucked into a yellow
                                  "Failed to equip: ..." line.
```

That looked from the player's seat exactly like "equip doesn't work." `[P]` Pass also didn't help much: cascade went through `OfferLootToOtherPlayers` without level checks either, so the offer would re-render `[E]` for the next player even when they couldn't equip it either; eventually the item would land in someone's inventory through `[T]` Take, again without context.

There was a second related gap on the follower prompt specifically: the leader's prompt builds its item display via `RenderEquipment`, which already shows a red "Requires Level: X (Y is level Z)" warning when the recipient is under-leveled (added in v0.57.9). The follower's prompt didn't reuse `RenderEquipment` — it manually re-rendered name/power/value to the follower terminal and skipped the level line entirely. Same for the cascade path.

**Fix:** new `GetLootEquipEligibility(Character recipient, Item lootItem)` helper that combines the class restriction check and the level requirement check into a single (canEquip, reason) tuple. All three prompt paths now call it instead of `CanClassUseLootItem` directly:

- Leader path's initial check + both `<` / `>` party-cycle re-checks now use the combined gate.
- Follower path's manual render block now also emits the same red "Requires Level: X" warning the leader sees, before the prompt is shown.
- Cascade path likewise renders the warning to each cascaded recipient's terminal.

In all three paths, when `MinLevel > recipient.Level` the `[E] Equip` option is no longer offered (matching how class restriction was already handled), and the warning explains why. `[T] Take` and `[P] Pass` still work as before — players can take an over-leveled drop into inventory if they want to hold it for later.

`Item.MinLevel` is set by `LootGenerator` and re-floored to power tier via `Item.EnforceMinLevelFromPower()` at drop time (the v0.57.9 honesty fix), so this gate matches what `Equipment.CanEquip` will check after `ConvertLootItemToEquipment` — no lying display vs equip.

## Combat Broadcasts Spammed Followers Who Left to Town

Player report: "When in a player group, if the non-leader leaves to town, the battle still spams the non-leader in town."

**Root cause: leaving the dungeon doesn't (and shouldn't) remove a player from the group, but combat / loot / dungeon broadcasts iterate the full group member list with no filter for who's actually still in the dungeon.**

By design (see `CleanupGroupFollower`): when a follower types `Q` to leave the dungeon, they're removed from the leader's `teammates` list and their session's `IsGroupFollower` flag is cleared, but they stay in `DungeonGroup.MemberUsernames`. Comment in the code: *"follower left the dungeon, not necessarily the group"* — they can rejoin without re-inviting.

But `GroupSystem.BroadcastToAllGroupSessions(group, message, excludeUsername)` and `BroadcastToGroupSessions(group, actor, ...)` blindly iterate `MemberUsernames` and `EnqueueMessage` to every session it finds. So the leader's combat round announcements, loot drops, dungeon room descriptions, monster-hits-companion damage lines, and grouped-player action captures all kept flowing to the follower's terminal while they were trying to vendor / heal / browse the shop in town. The reporter saw the back-and-forth of an active dungeon fight on Main Street.

Five broadcast call sites were affected:

- `CombatEngine.BroadcastGroupCombatEvent` — every per-round combat announcement.
- `CombatEngine.BroadcastGroupedPlayerAction` — third-person captures of grouped player actions for other group members to see.
- `CombatEngine.DisplayEquipmentDrop` loot detail broadcast.
- `CombatEngine.MonsterAttacksCompanion` perspective-correct damage line ("you take X" / "X takes Y damage").
- `CombatEngine` companion-death notification ("YOU HAVE FALLEN" / "X has been slain").
- `DungeonLocation.BroadcastDungeonEvent` — room/feature/event narration.

**Fix:** new `inDungeonOnly` parameter on `GroupSystem.BroadcastToAllGroupSessions` and `BroadcastToGroupSessions`, defaulting to false so non-dungeon callers (none currently exist via these paths, but the safer default is "include everyone"). When true, the broadcast skips members who aren't in the active dungeon session — checked via a new `IsInDungeonGroupSession` helper that returns true only for: (a) the leader when `group.IsInDungeon == true`, or (b) followers whose `PlayerSession.IsGroupFollower == true`. All five callsites above now pass `inDungeonOnly: true`.

`NotifyGroup` (a separate method used for "X has joined/left the group" announcements) is intentionally unchanged — those messages SHOULD reach members regardless of where they are. Group bookkeeping behavior is unchanged: a follower who leaves the dungeon stays in the group and can rejoin. They just no longer get spammed with combat events while they're not participating.

## Inventory Equip-to-Off-Hand Replaced the Main Hand

Player report from a Lv.100 Barbarian: "When equipping from inventory, selecting 2 (offhand) and selecting a single handed item, rather than replacing the offhand as you would assume, it replaces the main hand weapon."

**Root cause: `EquipFromBackpack` ignored its caller-supplied `slot` parameter for weapons, hardcoding `targetSlot = MainHand` from the item-type switch and passing `null` to `EquipItem`.**

The flow when a player picks the off-hand slot from the inventory menu:

1. `ManageSlot(EquipmentSlot.OffHand)` → `ProcessSlotInventoryChoice(OffHand, ...)` → `ManageBackpackItem(index, slot=OffHand)` → `EquipFromBackpack(index, slot=OffHand)`. The `slot` parameter is threaded all the way through.

2. Inside `EquipFromBackpack`, `targetSlot` was set purely from `item.Type`:
   ```csharp
   targetSlot = item.Type switch
   {
       ObjType.Weapon => EquipmentSlot.MainHand,   // ignores caller's slot
       ObjType.Shield => EquipmentSlot.OffHand,
       ...
   };
   ```

3. The slot-prompt block was guarded on `slot == null`:
   ```csharp
   if (slot == null && Character.RequiresSlotSelection(equipment))
   {
       finalSlot = await PromptForWeaponSlot();
       ...
   }
   ```
   With `slot != null` (caller picked OffHand), the prompt was skipped — but `finalSlot` stayed `null`.

4. `player.EquipItem(equipment, finalSlot=null, ...)` — with `targetSlot = null`, `EquipItem`'s auto-determine path routes one-handed weapons to MainHand by default.

So picking `[2] OffHand` for a one-handed weapon: comparison screen showed the main hand item ("Currently equipped: <main hand weapon>"), confirmation dumped the new weapon into MainHand, original main hand weapon went to inventory. The off-hand was untouched. From the player's seat: "I picked 2 (off-hand) and it replaced my main hand."

**Fix:** one-line — `EquipmentSlot? finalSlot = slot;` initializes `finalSlot` from the caller's pick, then the existing `Character.RequiresSlotSelection` prompt only fires as a fallback when no slot was provided. `Character.EquipItem` already validates the slot (passing OffHand for a two-handed weapon is rejected with "Two-handed weapons must be equipped in MainHand"; passing MainHand for a shield is rejected similarly), so explicit user picks land in the right slot when valid and produce a clear error when not.

The slot comparison header (line ~1213, `compareSlot = finalSlot ?? targetSlot`) now also reads correctly — picking OffHand shows "Currently equipped: <whatever's in OffHand>" instead of misleadingly showing the main hand item. Same applies for picking RFinger over LFinger from the rings flow.

## Companion Auto-Pickup Displaced Signature Weapons

Player report: "Is Vex supposed to grab swords when auto equipping? Melodia grabbed the dagger and moved the instrument to offhand."

**Root cause: nothing in the auto-pickup pipeline kept companions on their signature weapon when the displaced item still satisfied ability requirements via the off-hand.**

The companion equipment whitelist in [Items.cs:1027-1045](Scripts/Core/Items.cs#L1027-L1045) deliberately allows secondary weapon types per companion — Vex/Assassin can equip swords (intended for v0.53.4 dual-wield builds), Melodia/Bard can equip daggers and rapiers, Lyris/Hybrid Ranger can equip swords and daggers alongside her bow. The intent was *flexibility*, not *replacement*.

But `TryTeammatePickupItem` only filtered by:

- Class restriction (whitelist).
- Equipment.CanEquip (which delegates to the same whitelist).
- Ability weapon requirements via `breaksAbilities`. This check evaluates the loot's weapon type against every ability that has a weapon requirement — but ability requirements ([ClassAbilitySystem.cs:2477-2478](Scripts/Systems/ClassAbilitySystem.cs#L2477-L2478)) check BOTH hands. So an instrument bumped to off-hand still satisfies Bard ability requirements, the displacement isn't visible to the auto-pickup eligibility check, and a higher-WP dagger reads as a clean upgrade.

Result: when a dagger with higher attack power than Melodia's instrument dropped, the auto-pickup prompt fired with "Melodia could equip this — X% upgrade." Player approves. Dagger goes to MainHand, instrument moves to OffHand. Bardic abilities still resolve (instrument is in either hand) — but Melodia's now stabbing things with a knife in the front rank. Same shape for Vex: a sword with higher WP than his dagger fires the prompt; if approved, sword to MainHand, dagger to OffHand. Vex's Assassin abilities check both hands too, so Backstab still works — but the signature dagger is no longer the primary weapon.

**Fix:** new "preferred weapon types" guard in `TryTeammatePickupItem`, after the existing ability + spell weapon checks. Each companion has a defined preferred-weapon-types list, narrowed to **signature one-handed/main-weapon types** rather than the broader equipment whitelist (a follow-up player report flagged that Mira swapping her mace for a staff displaced her shield slot — the whitelist allowed Staff but the tank/healer + shield archetype doesn't want it auto-picked up):

- Aldric (Tank): Sword / Axe / Mace / Hammer / Flail / Maul (one-handed only — keeps shield slot free)
- Mira (Healer): Mace / Hammer / Flail (one-handed only — same shield reasoning)
- Lyris (Hybrid Ranger): Bow
- Vex (Assassin): Dagger / Rapier
- Melodia (Bard): Instrument

If the loot's weapon type is **not** on the companion's preferred list AND the companion already has a preferred-type weapon equipped in either hand, the auto-pickup is skipped. The player can still manually equip the off-spec weapon (Greatsword for Aldric, Staff for Mira, etc.) from inventory if they want to deviate from the companion's signature loadout — this only changes the auto-pickup prompt path, not the equip permission.

The existing `breaksAbilities` check stays in place as a defense-in-depth guard. Companions without a defined preferred list (no current case but futureproofing) fall through to existing behavior.

## Ring Loot Compared Against Only One Finger Slot

Player report: "when finding a ring, it should compare both right and left slot not just one."

**Root cause: every loot path's `targetSlot` switch hardcoded `Fingers => LFinger`, so ring comparisons ignored the right finger entirely.**

Three sites all defaulted to `EquipmentSlot.LFinger` for ring loot:

- `ShowEquipmentComparison` (player loot prompt) — compared the new ring against whatever was on LFinger.
- `CalculateItemUpgradeValue` (cross-target upgrade scoring) — same.
- `TryTeammatePickupItem` (companion auto-pickup eligibility) — same.

The actual equip path (`Character.EquipItem`) is smart about ring placement — if LFinger is full and RFinger is empty, the ring auto-routes to RFinger; if both are full the player gets a `PromptForRingSlot` choice. So the equip *result* was correct. But the comparison and auto-pickup eligibility checks were lying:

- LFinger weak ring (10 stat), RFinger strong ring (50 stat), new ring drops at 30 stat. Comparison shows "Currently equipped: <weak ring>" — looks like a +20 upgrade. Player accepts. New ring replaces the weak LFinger one. Net result: +20. Honest by accident.
- LFinger strong ring (50 stat), RFinger empty, new ring drops at 30 stat. Comparison shows "Currently equipped: <strong ring>" — looks like a -20 downgrade. Player passes. But the ring would have *filled the empty RFinger slot* for a clean +30. Missed upgrade.
- Companion auto-pickup with LFinger=strong (50), RFinger=weak (10), new ring 30 stat. Auto-eligibility check: 30 < 50 (LFinger) → not an upgrade → no prompt. But it was a clear +20 upgrade for RFinger. Companion never got asked.

**Fix:** new `GetBestRingSlotForComparison(character)` helper that mirrors `Character.EquipItem`'s ring-placement logic:

1. If LFinger is empty, prefer LFinger (auto-fill).
2. Else if RFinger is empty, prefer RFinger (auto-fill).
3. Else both occupied — return the slot with the WEAKER ring by stat-total, so the comparison shows what the user would actually replace if they accept.

All three loot paths now call this helper for `Fingers` items instead of hardcoding LFinger. The companion auto-pickup also picks the per-teammate best ring slot (each companion's L/R rings can differ).

The slot-name display in the comparison header changed from `"Ring"` for both slots to `"Left Ring"` / `"Right Ring"` so the player can see which slot the comparison is targeting.

Necklaces are unaffected — they only have one slot.

## Team Wars Free-Money Exploit

Player report from a Lv.100 Barbarian: "in team corner, team wars, you gamble 20k to win 40k. There's no limit and once you've found a team you can beat, it's free money."

**Root cause: three holes stacked.**

1. **No daily cap on challenges.** `ChallengeTeamWar` only blocked when an active war was in progress.
2. **No per-opponent cooldown.** A challenger who identified a beatable defender team could challenge them again immediately after the war finished.
3. **The reward was created from thin air.** `wager = max(1000, level * 200)` — at Lv.100 that's 20k. On victory `currentPlayer.Gold += wager * 2` — winner gets 40k. Net profit per win: +20k. The losing team contributed nothing — the 2x reward isn't redistributed gold, it's gold spawned out of nothing.

Combined: pick on the weakest team in the world, spam wars, print 20k/win indefinitely.

**Fix — three layers:**

1. **Daily cap.** New `Character.TeamWarsToday` counter (mirrors the `MurdersToday` plumbing pattern from v0.57.6 — full Character / PlayerData / SaveSystem / GameEngine restore / DailySystemManager reset / PlayerSaveEditor wiring). New `GameConfig.MaxTeamWarsPerDay = 3`. `ChallengeTeamWar` blocks when the cap is hit; counter increments on every completed war regardless of outcome (running a war = burning a slot). The Team Wars menu surfaces "Wars remaining today: X/3" so the player sees the budget.

2. **Per-opponent cooldown.** New `GameConfig.TeamWarOpponentCooldownHours = 6`. Before the wager prompt, the challenger queries `SqlSaveBackend.GetTeamWarHistory(myTeam, limit: 20)` and refuses if any war between these two specific teams started within the cooldown window. Reads from existing data — no schema change. Cooldown counts wars in either direction (defender-side wars also block re-challenging).

3. **Reduced reward.** New `GameConfig.TeamWarRewardMultiplier = 1.5f` (was hardcoded 2.0). Winner gets `wager * 1.5` back instead of `wager * 2`. Net profit per win: +50% of wager (was +100%). At Lv.100 that's +10k per win instead of +20k. Combined with the 3/day cap, max profit is +30k/day from team wars instead of unlimited.

The gold-from-thin-air problem isn't fully solved (the 1.5x multiplier still creates 50% wager out of nothing per win), but redesigning the war pot to actually move gold between team members would require writing back to multiple player saves mid-flight in online mode — a non-trivial concurrency change that deserves its own pass. The cap + cooldown + reduced multiplier remove the exploit's usefulness in the meantime.

## Removed Player Echoes Re-Materialized on Dungeon Re-Entry

Player report from a Lv.80 character: "when you remove echo's from the party, leave the dungeon and come back, they automatically get re-added."

**Root cause: `SyncNPCTeammatesToGameEngine` only synced NPC IDs, not the parallel player-echo name list.**

Player echoes are AI-controlled clones of teammate players' characters, recruited from Team Corner via `[A] Recruit Echo`. The recruitment writes the echo's display name to `GameEngine.DungeonPartyPlayerNames`, which is persisted in the player save. On every dungeon entry, `DungeonLocation.RestorePlayerTeammates` reads that list and re-materializes each named echo from the database via `PlayerCharacterLoader.CreateFromSaveData(..., isEcho: true)`.

When the player removed an echo via the in-dungeon `[Remove from Party]` action, `RemoveTeammateFromParty` stripped the echo from the local `teammates` list and called `SyncNPCTeammatesToGameEngine()` — but that helper only rebuilt the NPC ID list:

```csharp
private void SyncNPCTeammatesToGameEngine()
{
    var npcIds = teammates.OfType<NPC>().Select(n => n.ID).ToList();
    GameEngine.Instance?.SetDungeonPartyNPCs(npcIds);
    // DungeonPartyPlayerNames was never touched — the removed echo's name stayed there.
}
```

So the echo vanished from the active party but their name remained in `DungeonPartyPlayerNames`. On next dungeon entry, `RestorePlayerTeammates` re-read the still-present list and the echo materialized again. From the player's seat: "I removed them, why are they back?"

**Fix:** `SyncNPCTeammatesToGameEngine` now rebuilds BOTH lists from the live `teammates` collection — NPC IDs (existing behavior) plus echo names. Echo names are filtered as `IsEcho && !IsCompanion && !IsGroupedPlayer` so companions (managed by `CompanionSystem`) and grouped players (live followers, not echoes) are excluded. Every existing call site of this helper now keeps both lists consistent automatically — no per-callsite changes needed.

## Hidden Status Wore Off Before the Player Could Use It

Player report: "Symphony of the Depths possibly not working. The 'Hid' status seems to wear off before I can choose the next action after casting the spell. Status Effects are read twice, once with 'Hid' and the next time with the message 'Hid wears off'."

**Root cause: every "auto-crit on next attack" Hidden grant set duration = 1, but `ProcessStatusEffects` ticks before the player's next action and decrements it to zero — so the auto-crit was always lost.**

`Character.ProcessStatusEffects` is called in two combat hot paths:

- Multi-monster combat: `CombatEngine.cs:1013`, at the start of each round (before any player action).
- Single-monster combat: `CombatEngine.cs:1647`, inside `GetPlayerAction`'s while-loop, on every action prompt.

Both paths run `ActiveStatuses[Hidden] = ActiveStatuses[Hidden] - 1` and remove the status when it reaches zero (logging "Hidden wears off" to the player's status feed). For Hidden granted with duration 1, that meant:

```
Round N:   Cast Symphony of the Depths → Hidden = 1
Round N+1: ProcessStatusEffects ticks 1 → 0 → "Hid wears off"
           Player picks attack — Hidden gone, no auto-crit fires.
```

The "Status Effects are read twice" the report mentions is exactly this — the active-buffs panel briefly shows "Hid" at the start of round N+1, then `ProcessStatusEffects` immediately removes it with the "wears off" line. Two displays, no usable buff in between.

The `shadow` ability (CombatEngine.cs:13072 / :20232) already used `Hidden = 2` and worked correctly. Every other auto-crit-setup site used 1.

**Fix:** all 9 "auto-crit on next attack" Hidden grants bumped from `= 1` to `= 2`. The status now survives the next-round tick (2 → 1) and the player's attack consumes it for the auto-crit before the round-after-that tick (1 → 0). Affected:

- `SpellSystem.ExecuteWavecallerSpell` case 5 — Symphony of the Depths (the report).
- `SpellSystem.ExecuteVoidreaverSpell` case 2 — Blood Pact.
- `CombatEngine` `vanish` ability (multi + single-monster paths).
- `CombatEngine` `temporal_feint` ability (multi + single).
- `CombatEngine` `umbral_step` ability (multi + single).
- `CombatEngine.ExecuteHide` — the basic `[H]ide` combat action.

The `shadow` ability and `CombatEngine.cs:11870` (Backstab/Noctura grant) were already at `= 2` and unchanged. No new mechanic — Hidden still gets consumed by attack via `RemoveStatus` when the auto-crit fires; the only change is the time-decay duration.

## Spell Level Thresholds Were Inconsistent Across Menus

Player report from a Lv.18 Wavecaller: "I also do not see Symphony of the Depths on the list of skills to train."

**Root cause: two sources of truth for spell-level thresholds disagreed.**

The codebase had two parallel mechanisms answering "what character level is needed to cast spell N for class C":

1. **`SpellSystem.GetLevelRequired(class, spellLevel)`** — a hardcoded universal table mapping spell index to character level (e.g. `5 => 16`). Class-agnostic. Used by the spell library, combat checks, and AI weapon checks.
2. **`SpellInfo.LevelRequired`** — a per-class field on each spell definition. Used by `GetAvailableSpells`, which the training menu reads from.

The intent of each table was different and they were tuned independently:

- For **25-spell classes** (Cleric / Magician / Sage), `SpellInfo.LevelRequired` was set equal to the spell's index — `Cure Wounds (level 5)` had `LevelRequired = 5`. That's effectively meaningless. The table's `5 => 16` was the real intent — slow ramp pacing.
- For **5-spell prestige classes** (Wavecaller, etc.), `SpellInfo.LevelRequired` was the genuine intended ramp — Wavecaller spells unlock at character levels `1, 6, 11, 16, 22`. The table's `5 => 16` for Symphony of the Depths was lower than the intended `22`.

So for the reporter at Lv.18 Wavecaller:

- Spell library used the table → showed Symphony as learnable (16 ≤ 18). Player learned it.
- Training menu used `GetAvailableSpells` → which checked `character.Level (18) < spell.LevelRequired (22)` → skipped Symphony entirely.
- Combat `CanCastSpell` used the table → let them cast Symphony at Lv.18 (which is how the Hidden-status bug from the previous report was even reachable).

Three menus, three answers, one confused player.

**Fix — three layers:**

1. **`GetLevelRequired` now takes the stricter of the two sources** — `Math.Max(table[spellLevel], SpellInfo.LevelRequired)`. For 25-spell classes the table's slow ramp wins (table 16 > SpellInfo 5). For prestige 5-spell classes, the per-class SpellInfo wins when stricter (SpellInfo 22 > table 16 for Symphony). Single source of truth from the caller's perspective. The original table is preserved as `TableLevelRequired` so the structure is still readable.

2. **`GetAvailableSpells` routes through `GetLevelRequired`** instead of reading `spell.LevelRequired` directly. Same threshold the spell library and combat checks use.

3. **`TrainingSystem.GetTrainableSkills` no longer level-gates LEARNED spells.** Iterates `GetAllSpellsForClass` and shows every spell the player has actually marked as learned. The level requirement matters at cast time (`CanCastSpell` still enforces it), not at proficiency-training time. So if the reporter previously learned Symphony when the spell library let them at Lv.16 (now corrected to Lv.22), they can still see and train its proficiency at Lv.18 — they just can't cast it in combat until Lv.22.

After the fix, Wavecaller spell pacing matches the SpellInfo's class-specific ramp (1/6/11/16/22) — Symphony correctly requires Lv.22 to learn from the library. The training menu respects what the player has already learned regardless of current level.

## Noctura Betrayal Crashed on Phase Transition

Player report (Lv.100 Barbarian, post-Manwe): "I had just beaten the last boss, and a second fight started. First round went fine, but on the second round I got 'Error loading save: Object reference not set to an instance of an object' when the enemy went to use their second attack. I was then disconnected from server and relogged back in town."

This is the v0.57.16 Noctura-betrayal NRE returning in a different code path. The v0.57.16 fix added a try/catch around `HandleNocturaBetrayal` to handle this kind of crash gracefully (treat as escape, fire ending sequence). But the new throw is happening on a path that doesn't propagate up cleanly.

**Root cause: the Noctura betrayal `BossContext` was constructed without `BossData`.**

Compare the two BossContext construction sites:

```csharp
// Regular Old God boss — OldGodBossSystem.cs:825
combatEngine.BossContext = new BossCombatContext
{
    BossData = boss,                        // ← present
    GodType = boss.Type,
    AttacksPerRound = boss.AttacksPerRound,
    ...
};

// Noctura betrayal — OldGodBossSystem.cs:1331 (the bug)
combatEngine.BossContext = new BossCombatContext
{
    GodType = OldGodType.Noctura,
    AttacksPerRound = 1,
    CanSave = false,
    // BossData missing — null
};
```

With `BossData = null`, every code path that bare-dereferenced `ctx.BossData.X` would NPE. The trigger sequence:

1. Round 1: Lv.100 Barbarian deals burst damage to Noctura (300k HP). Boss HP drops below 50% (Phase2Threshold).
2. Round 2 start: `CheckPhase` sees `currentHP < 50% maxHP` → returns 2. CurrentPhase = 1. Phase transition fires.
3. `PlayBossPhaseTransition` runs `ctx.BossData.Phase2Dialogue` (line 25094) → NPE.
4. The NPE propagates async through `PlayerVsMonsters` → `HandleNocturaBetrayal` → caller. The inner try/catch from v0.57.16 should catch it... but the outer LoadSaveByFileName catch at GameEngine.cs:2883 fires first because the player session was inside `EnterLocation` at the time, and that's wrapped by the LoadSaveByFileName try block (which spans the entire location-loop lifetime).
5. "Error loading save: Object reference not set to an instance of an object" — the player thinks the save broke. Disconnect. Relog → main street.

The v0.57.16 fix only catches exceptions from inside `HandleNocturaBetrayal` itself; it doesn't help when the exception escapes via the awaited `PlayerVsMonsters` call inside it. (Actually it should — `await` does propagate. But the "Error loading save" rendering means something in the GameEngine catch path also fired. Either way the visible symptom is identical and the user sees a crash.)

**Fix — two layers:**

1. **Primary**: add `BossData = betrayalData` to the Noctura betrayal `BossContext` so `ctx.BossData` is never null on this code path. Same data the regular Old God boss uses, same scaling/dialogue/phase logic now applies. Phase 2 plays Noctura's "shadow copies" dialogue, Phase 3 plays the desperate "I WILL have this power" dialogue, ability-name display works correctly.

2. **Defense in depth**: null-check `ctx.BossData` in the bare-dereference sites — `PlayBossPhaseTransition` (Phase2Dialogue / Phase3Dialogue / ThemeColor / Abilities), `SelectBossAbility`, and `AttemptBossSave`. If a future caller forgets to set `BossData`, we fall back to no-dialogue + "Divine Strike" + abort-save instead of crashing the combat loop.

**For affected players**: any character currently soft-locked from a prior crashed Noctura betrayal can now re-trigger the encounter cleanly when the v0.57.16 try/catch's escape-and-continue-ending-sequence path fires correctly. The NPE no longer happens at all on the patched binary.

## Trade Cancel/Accept Race Duplicated Gold

Player report: "There is a bug in the trade system. If one player sends a trade with money, then cancels the trade at the same time the other player accepts the trade, the money is returned and the receiving player receives the money — duplicating it."

The live-server log confirms the exploit was being used: `spudman__alt Lv9 gold=1,399,785,713 bank=0 earned=12,390 (ratio 112977x)`. A Lv.9 alt with 1.4 billion gold from a player whose lifetime earnings were 12,390.

**Root cause: `UpdateTradeOfferStatus` blindly wrote the new status without checking the current state.**

```csharp
// BEFORE — racy
public async Task UpdateTradeOfferStatus(long offerId, string status)
{
    cmd.CommandText = "UPDATE trade_offers SET status = @status, resolved_at = datetime('now') WHERE id = @id;";
    ...
}
```

The trade-resolution sequence was:

```csharp
// AcceptTradeOffer (BaseLocation.cs:7532)
currentPlayer.Gold += offer.Gold;          // give to receiver
await UpdateTradeOfferStatus(offer.Id, "accepted");

// CancelTradeOffer (BaseLocation.cs:7616)
await UpdateTradeOfferStatus(offer.Id, "cancelled");
currentPlayer.Gold += offer.Gold;          // return to sender
```

If both fire concurrently, both UPDATEs succeed (last-write-wins on the row), AND both gold operations execute. Net effect: the receiver gets the gold AND the sender gets it back. Free `gold * N` per round-trip — repeat until rich. Same race shape covers the expire-vs-accept and decline-vs-cancel cases too.

**Fix — atomic compare-and-set:**

`UpdateTradeOfferStatus` now adds `WHERE id = @id AND status = 'pending'` to the UPDATE and returns `bool` indicating whether it actually changed a row (i.e. won the race). Caller contract: only do the gold/item movement if the resolve returned true.

```csharp
// AFTER — atomic
public async Task<bool> UpdateTradeOfferStatus(long offerId, string status)
{
    cmd.CommandText = "UPDATE trade_offers SET status = @status, resolved_at = datetime('now') WHERE id = @id AND status = 'pending';";
    int rowsAffected = await Task.Run(() => cmd.ExecuteNonQuery());
    return rowsAffected == 1;
}
```

All four callers (`AcceptTradeOffer`, `DeclineTradeOffer`, `CancelTradeOffer`, expire-loop in `SqlSaveBackend`) now resolve the offer FIRST and only proceed with gold/item movement if the atomic update won the race. The losing call shows "This trade was already resolved by the other party (or it expired). Nothing to do." and bails. Gold can no longer be duplicated.

**Existing duped gold on the live server is not auto-clawed-back.** A sysop with the in-game admin console (or direct DB access) can adjust suspect balances; the `[GOLD_AUDIT] SUSPICIOUS:` log lines surface candidate accounts (the spudman__alt save in the report was already flagged with `ratio 112977x`).

## Inn Drinking Game Was a Free Leveling Track

Player report: "The drinking game at the inn has no limit per day and no carry over. With enough str/con mid game character can level purely at the inn pretty quickly."

**Root cause: no daily cap.** `PlayDrinkingGame` charges 20g per entry, then on win pays `currentPlayer.Level * 700` XP and `50 + Level * 10` gold. At Lv.50 that's 35,000 XP per ~30 seconds of clicking through the minigame. With moderate STR/CON the player's `playerSoberness` calculation `(Stamina + Strength + Constitution + 10) / 10` outscales NPCs whose stats are notably lower, so wins are near-automatic. There was no per-day limit and no cooldown — the inn was effectively a dedicated leveling track.

**Fix:** new `Character.DrinkingGamesToday` daily counter (mirrors the `MurdersToday` / `TeamWarsToday` plumbing pattern from v0.57.6 / earlier-this-version) capped at `GameConfig.MaxDrinkingGamesPerDay = 5`. `PlayDrinkingGame` checks the counter at the top, refuses with "You've already played N drinking games today. The barkeep waves you off — 'Come back tomorrow, friend.'" once the cap is hit, and increments after the gold deduction. The remaining-today count is shown right after entry so the player can pace themselves: "(Drinking games remaining today: X/5)". Resets on the daily reset. Editor's "Reset daily counters" action handles it too.

5 wins/day × `Lv*700` XP = `Lv*3500` XP/day from the inn (Lv.50 = 175k XP/day, Lv.100 = 350k XP/day) — meaningful but not a leveling primary. Players who want more XP need to actually fight monsters.

## Files Changed

- `Scripts/Core/GameConfig.cs` — version bump 0.57.16 → 0.57.17.
- `Scripts/Locations/PantheonLocation.cs` — `RenounceImmortality` now calls `StoryProgressionSystem.StartNewCycle(EndingType)` instead of the legacy-only manual migration block. Behaves identically to the normal post-Manwe NG+ path: resets god states, NPC relationships, cycle bookkeeping, and records the ending. Dedup guard prevents duplicate ending entries.
- `Scripts/Systems/FamilySystem.cs` — `ApplyChildBonuses` no longer mutates `Chivalry`/`Darkness`. `CalculateChildBonuses` no longer computes `ChivalryBonus`/`DarknessBonus`. The MaxHP/Strength/Charisma stat bonuses still apply correctly because those fields ARE reset to base on each `RecalculateStats()`. New `DisownChildrenOf(string playerName)` method blanks parent slots on children of the named character and orphans both-parents-empty minors.
- `Scripts/Core/Child.cs` — removed `ChivalryBonus` and `DarknessBonus` fields from `ChildBonuses`; removed the matching display lines in `GetBonusSummary`.
- `Scripts/Core/GameEngine.cs` — `CreateNewGame` now captures `previousLifeName` before reset, calls `FamilySystem.DisownChildrenOf` in the online NG+ branch, and fires a `SaveAllSharedState` after the new save lands so the disown change persists to `world_state.children` immediately.
- `Scripts/Systems/CombatEngine.cs` — new `GetLootEquipEligibility(recipient, lootItem)` helper combines class + level checks. Leader prompt (`GetEquipmentDropInput`), grouped follower prompt (`DisplayEquipmentDrop`), and cascade-to-other-player prompt (`OfferLootToOtherPlayers`) all use it so `[E] Equip` is correctly hidden for over-leveled drops instead of silently failing into inventory. Follower and cascade prompts now also surface the red "Requires Level: X" warning the leader path already showed via `RenderEquipment`. Five group broadcast callsites (`BroadcastGroupCombatEvent`, `BroadcastGroupedPlayerAction`, loot drop broadcast in `DisplayEquipmentDrop`, perspective-correct damage line and companion-death line in `MonsterAttacksCompanion`) now pass `inDungeonOnly: true` so combat doesn't spam members who left to town.
- `Scripts/Server/GroupSystem.cs` — `BroadcastToAllGroupSessions` and `BroadcastToGroupSessions` gained an `inDungeonOnly` parameter (default false); new `IsInDungeonGroupSession` helper returns true only for the leader-while-in-dungeon or followers with `IsGroupFollower == true`. `NotifyGroup` (membership announcements) intentionally still reaches all members.
- `Scripts/Locations/DungeonLocation.cs` — `BroadcastDungeonEvent` now uses `inDungeonOnly: true` so dungeon room/event narration doesn't reach members in town.
- `Scripts/Systems/InventorySystem.cs` — `EquipFromBackpack` now initializes `finalSlot` from the caller-supplied `slot` parameter so picking `[2] OffHand` (or any explicit slot) actually targets that slot. `EquipItem` still validates legality. The slot-comparison header now reads from the correct slot too.
- `Scripts/Systems/CombatEngine.cs` — `TryTeammatePickupItem` gains a per-companion preferred-weapon-types guard after the existing ability + spell checks: if the loot's weapon type isn't on the companion's preferred list AND the companion already has a preferred weapon in either hand, the auto-pickup is skipped. Stops Vex from grabbing swords over his dagger and Melodia from displacing her instrument with a dagger. Equip permission via inventory unchanged — manual override still works. New `GetBestRingSlotForComparison` helper picks the per-character ring slot for comparison (empty first, else weaker ring); applied in `ShowEquipmentComparison`, `CalculateItemUpgradeValue`, and `TryTeammatePickupItem` so ring loot reads as an upgrade vs the slot it would actually displace. Comparison header now distinguishes "Left Ring" / "Right Ring".
- `Scripts/Core/GameConfig.cs` — new constants: `MaxTeamWarsPerDay = 3`, `TeamWarOpponentCooldownHours = 6`, `TeamWarRewardMultiplier = 1.5f`.
- `Scripts/Core/Character.cs` — new `TeamWarsToday` daily counter (mirrors `MurdersToday` plumbing).
- `Scripts/Systems/SaveDataStructures.cs` / `Scripts/Systems/SaveSystem.cs` / `Scripts/Core/GameEngine.cs` / `Scripts/Systems/DailySystemManager.cs` / `Scripts/Editor/PlayerSaveEditor.cs` — `TeamWarsToday` save / load / restore / daily-reset / editor-reset wiring.
- `Scripts/Locations/TeamCornerLocation.cs` — `ChallengeTeamWar` enforces daily cap and per-opponent cooldown (queried from `team_wars` history). Reward multiplier moved to `GameConfig.TeamWarRewardMultiplier` (was hardcoded `wager * 2`). `TeamWarMenu` now displays "Wars remaining today: X/3".
- `Localization/{en,es,fr,hu,it}.json` — three new keys: `team.war_daily_cap`, `team.war_daily_remaining`, `team.war_opponent_cooldown`.
- `Scripts/Locations/DungeonLocation.cs` — `SyncNPCTeammatesToGameEngine` now rebuilds the player-echo name list (`DungeonPartyPlayerNames`) alongside the NPC ID list, so removing an echo from the party actually persists the removal across dungeon re-entry.
- `Scripts/Systems/SpellSystem.cs` / `Scripts/Systems/CombatEngine.cs` — every "auto-crit on next attack" Hidden grant bumped from duration `= 1` to `= 2` so the status survives the start-of-next-round `ProcessStatusEffects` tick. Affects Symphony of the Depths, Blood Pact, `vanish`, `temporal_feint`, `umbral_step`, and the basic `[H]ide` action (each in their multi- and single-monster combat variants).
- `Scripts/Systems/SpellSystem.cs` — `GetLevelRequired` now returns `Math.Max(table, SpellInfo.LevelRequired)` so the spell library, combat-cast check, and training menu all share one threshold. `GetAvailableSpells` routes through `GetLevelRequired` instead of reading `spell.LevelRequired` directly. Original table extracted to `TableLevelRequired`.
- `Scripts/Systems/TrainingSystem.cs` — `GetTrainableSkills` shows every LEARNED spell regardless of current level (cast-time check still enforces the level requirement separately). Lets players train proficiency for spells they've already learned even if a stricter threshold elsewhere would block casting.
- `Scripts/Systems/OldGodBossSystem.cs` — `HandleNocturaBetrayal` now sets `BossData = betrayalData` on the `BossContext`. This was missing in v0.57.2 when the betrayal-fight context was first wired up; round-2 phase transition NPE'd on `ctx.BossData.Phase2Dialogue`.
- `Scripts/Systems/CombatEngine.cs` — `PlayBossPhaseTransition`, `SelectBossAbility`, and `AttemptBossSave` now null-check `ctx.BossData` instead of bare-dereferencing it. Fall back to no-dialogue / "Divine Strike" / abort-save if a future caller forgets to set it.
- `Scripts/Systems/SqlSaveBackend.cs` — `UpdateTradeOfferStatus` is now atomic compare-and-set (`WHERE id = @id AND status = 'pending'`) and returns `bool` indicating race-winner. Trade expiry loop now skips gold/item refund when the atomic update reports another resolver beat it.
- `Scripts/Locations/BaseLocation.cs` — `AcceptTradeOffer`, `DeclineTradeOffer`, and `CancelTradeOffer` resolve the offer atomically FIRST, only proceed with gold/item movement on win, and surface `base.trade_already_resolved` to the loser.
- `Localization/{en,es,fr,hu,it}.json` — new key `base.trade_already_resolved`.
- `Scripts/Core/GameConfig.cs` / `Scripts/Core/Character.cs` / `Scripts/Systems/SaveDataStructures.cs` / `Scripts/Systems/SaveSystem.cs` / `Scripts/Core/GameEngine.cs` / `Scripts/Systems/DailySystemManager.cs` / `Scripts/Editor/PlayerSaveEditor.cs` — `DrinkingGamesToday` daily counter (mirrors `MurdersToday` / `TeamWarsToday` plumbing) + `MaxDrinkingGamesPerDay = 5` constant.
- `Scripts/Locations/InnLocation.cs` — `PlayDrinkingGame` enforces the cap and shows remaining count after entry.
- `Localization/{en,es,fr,hu,it}.json` — `inn.drinking_daily_cap`, `inn.drinking_remaining_today`.

## Deploy Notes

Game binary only. No save format change. Existing player alignment values are not adjusted on load — the bug stops accumulating from this version onward, but historical drift stays where it landed. Children inherited from a previous bugged NG+ are not retroactively disowned — only future NG+ transitions disown cleanly.
