# v0.60.3 -- Beta

Bug-fix and feature release on top of v0.60.2. Multiple combat / world-sim bugs surfaced during continued beta playtesting (each from a different player report), plus a comprehensive GMCP package set expansion driven by a Mudlet/TT++ user's feedback, plus diagnostic instrumentation for the recurring CI deploy failures.

---

## Tank taunts: locked-on-for-the-fight bug + soft-stick rebalance

Two issues, fixed together. The first is the bug; the second is a design change to keep tanks from being an "invincible wall of flesh, win" button.

### The bug -- taunt duration only decremented when the player was the target

`Monster.TauntRoundsLeft` had exactly one decrement site: in `ProcessMonsterAction`, AFTER the `SelectMonsterTarget` -> branch-to-companion-attack early-return point. So the flow was:

```
SelectMonsterTarget returns the tank (TauntedBy match)
-> targetChoice != player
-> MonsterAttacksCompanion(...)
-> return     <-- never reaches the decrement below
```

When the taunter was the *player* (basic [T] action, or player casting an AoE taunt themselves), the player branch fell through to the decrement and it ticked normally -- which is why solo testing didn't catch it. The bug only triggered when an *NPC tank teammate* (Aldric Warrior with Thundering Roar / Shield Wall Formation, NPC Paladin with Divine Mandate, NPC Barbarian with Rage Challenge, NPC Tidesworn with Abyssal Anchor / Undertow Stance) was the taunter. In that case `TauntRoundsLeft` stayed at its initial value forever, `TauntedBy` never cleared, and the monster locked onto the NPC tank for the entire fight regardless of the configured duration.

This is why every tank-main playtester thought the AoE taunts felt overtuned -- a "3-round" taunt was effectively unlimited if cast by a teammate. Fix: extracted target selection from action dispatch in `ProcessMonsterAction`, decrement runs after `SelectMonsterTarget` returns but before either the companion-attack early-return or the player-attack continuation. Both branches now tick the duration correctly.

Files: `Scripts/Systems/CombatEngine.cs` (`ProcessMonsterAction`).

### The design change -- soft AoE taunt (75% stick per round)

Even with the duration bug fixed, a 100% guaranteed AoE taunt for 3 rounds combined with Shield Wall Formation's 30% damage reduction made the rest of the party untouchable for the duration. Keep stacking taunts on cooldown and the tank is essentially the only legal target for the whole fight -- "invincible wall of flesh, win." Same shape complaint as the original feedback that drove v0.56.0's tank rebalance.

New mechanic: per-round probabilistic taunt stick.

- New field `Monster.TauntStickChance` (0-100, default 100). Each round, `SelectMonsterTarget` rolls `random.Next(100) < TauntStickChance` -- if it passes, taunt forces the target; if it fails, the monster falls through to the normal weighted-target selection (which still favors the tank via threat weight, just not absolutely).
- New constant `GameConfig.SoftTauntStickChance = 75`. AoE class-ability taunts set the field to this value.
- **Soft taunts (75% stick, "threatened, not locked")**: Thundering Roar (`aoe_taunt`), Undertow Stance (`undertow`), Abyssal Anchor (`abyssal_anchor`), Shield Wall Formation / Divine Mandate / Rage Challenge (all routed through `ApplyTankTauntAndBuff`). Single-monster and multi-monster ability handlers both updated.
- **Hard taunts (100% stick, kept as-is)**: basic [T] action (1-2 round emergency lock), Eternal Vigil (Tidesworn invuln cooldown -- the entire point of that cooldown is absolute aggro on the invulnerable tank, leaving it at 100% preserves the design intent).
- Hard-taunt sites also explicitly write `TauntStickChance = 100` so a previously-soft-taunted monster getting hard-taunted resets correctly. The decrement-to-zero path also resets `TauntStickChance` to 100 default to keep monster state consistent.

Effective new behavior with Shield Wall Formation against 5 monsters for 3 rounds: ~75% of monster-rounds attack the tank, ~25% slip through to the rest of the party. Tank still bears the bulk of damage and remains the threatening target, but the rest of the party is no longer in safe-pause for the duration. Healers and DPS have to actually pay attention.

Tunable from one site -- if 75% feels too sticky raise to 85, too leaky drop to 60. The field is per-monster so individual taunts can override the default if a future ability wants stronger or weaker stick.

Files: `Scripts/Core/Monster.cs` (new `TauntStickChance` field), `Scripts/Core/GameConfig.cs` (new `SoftTauntStickChance` constant), `Scripts/Systems/CombatEngine.cs` (`SelectMonsterTarget` roll, plus `aoe_taunt` / `undertow` / `abyssal_anchor` / `ApplyTankTauntAndBuff` / hard-taunt sites in both single-monster and multi-monster paths).

---

## Queen challenges herself for the throne

Player screenshot showed an in-world impossibility: the royal-guard defense alert read **"Thalia Ravenswood (Level 26) is challenging Queen Thalia Ravenswood for the throne!"** Same name, same level. The player got the standard "rush to defend" prompt anyway.

Root cause is single-line: `CastleLocation.SetCurrentKing(NPC npc)` builds a fresh `King` object from the source NPC and stores it in the static `currentKing`, but **never sets `npc.King = true`** on the source NPC. So `currentKing` says Thalia is the queen, but her record in `NPCSpawnSystem.ActiveNPCs` still has the default `King = false`. The challenger candidate filter in `ChallengeSystem` (both player-king branch and NPC-king branch) does `.Where(n => !n.King ...)` -- which evaluates `false` against the queen's NPC record (her flag is false because nothing ever set it true), so she stays in the candidate pool. RNG eventually picks her as a challenger against herself.

Two-layer fix:

1. **Architectural** at `CastleLocation.SetCurrentKing`: now sets `npc.King = true` on the source NPC, AND clears `King = true` on any other NPC in `ActiveNPCs` so a stale flag from a previous reign doesn't linger after a throne transfer.
2. **Defensive** at both `ChallengeSystem` filter sites: also exclude by name match against `king.Name`. Belt-and-suspenders in case any other code path constructs a king state without going through `SetCurrentKing`.

Side benefit beyond the visible bug: any other system that checks `npc.King` (court roster lookups, dialogue gates, taxation logic) now works correctly with NPC kings instead of silently returning empty results because the flag was never set.

Files: `Scripts/Locations/CastleLocation.cs` (`SetCurrentKing` flag bookkeeping), `Scripts/Systems/ChallengeSystem.cs` (both candidate filters get name-based exclusion).

---

## Monster status display: Freeze (and 8 other statuses) silently invisible

Player report (Lumina Starbloom, Lv.14 Sage, floor 12): "After I cast Freeze at an enemy, it does not show on the status. The status shows only poison if active, but not frozen. I only know Freeze is still active when the enemy is unable to act."

Confirmed and broader than just Freeze. `GetMonsterStatusString` in `CombatEngine.cs` only displayed 4 statuses (Poisoned, CorruptingDot, Stun, Weaken). The `Monster` class has 11+ status flags. **Freeze, Sleep, Fear, Confused, Slowed, Marked, Corroded, Burning, and Taunted were all silently invisible** to the player. Visual and screen-reader status displays both share this helper, so the bug hit every full-width playthrough.

Bonus latent bug caught in the same audit: `PoisonRounds` is shared between actual poison and fire DoT (the `IsBurning` flag disambiguates). Pre-fix, a burning enemy displayed `PSN(3)` instead of `BURN(3)` -- same wrong-icon issue v0.57.17 fixed for the player-side burn DoT but never propagated to monster display.

Now visible on the enemy status line:

- `BURN(N)` / `PSN(N)` / `CRPT(N)` -- DoTs. BURN and PSN distinguished by `IsBurning` against the shared timer.
- `FRZ(N)` -- Frozen (the reported case).
- `SLP(N)` -- Sleeping.
- `FEAR(N)` -- Feared.
- `STN(N)` -- Stunned. Consolidates `StunRounds` + `StunDuration` + `IsStunned` (some abilities set rounds, others set duration; player only cares about the longer of the two).
- `CONF(N)` -- Confused.
- `WEK(N)` -- Weakened (existing, kept).
- `SLOW(N)` -- Slowed.
- `MARK(N)` -- Marked.
- `COR(N)` -- Corroded armor (note: `CRPT` for corrupting DoT was disambiguated from `COR` for corroded armor -- different mechanics with previously-overlapping abbreviations).
- `TNT(N)` -- Taunted.

BBS compact layout (two-monster-per-line) doesn't show monster status effects at all due to space constraints; out of scope for this fix.

Files: `Scripts/Systems/CombatEngine.cs` (`GetMonsterStatusString`).

---

## Ring loot equip rejected on Right Finger (regression from v0.60.2)

Player screenshot: looted a Ring of the Mage of the Arcane, picked `[E]quip immediately` -> got prompted "Both ring slots are occupied. Which ring would you like to replace? (L) Left / (R) Right / (I) Inventory" -> picked `R` -> got rejected with "Could not equip: This item belongs in the LFinger slot, not RFinger."

Regression from the v0.60.2 slot-type validation I added to fix the Chain-Shirt-in-MainHand bug. That check rejected any non-weapon item where `slot != item.Slot` -- but rings have `Slot = LFinger` by default while being legally wearable on either finger. The L/R prompt fed `EquipmentSlot.RFinger` to `EquipItem`, the strict-equality check refused, and the player got the misleading error.

Fix: carved out the ring case in `Character.EquipItem`. When `item.Slot` is `LFinger` OR `RFinger`, accept either finger slot as the target. Mirrors the existing weapon multi-slot handling (Handedness == OneHanded/TwoHanded -> MainHand or OffHand). Single-slot items (body armor, head, hands, feet, neck, etc.) still hit the strict check since those have exactly one legal slot, so the original Chain-Shirt-in-MainHand fix is preserved.

Files: `Scripts/Core/Character.cs` (`EquipItem` non-weapon branch).

---

Every emit now goes through a centralized `TryWriteFrame` helper that:

- Validates the package name against `^[A-Za-z][A-Za-z0-9._-]*$`. Bad input is a programmer error; refuse to put garbage on the wire instead of relying on the client to ignore it.
- Catches JSON serialization failures (circular graphs, MaxDepth violations) with structured logging. Pre-pass these threw and were caught generically with no useful context.
- Caps payload size at 16KB. Frames larger than the cap are dropped with a warning rather than streamed at the client (which could lock up clients with small read buffers).
- Tracks per-session consecutive emit failures on `SessionContext.GmcpConsecutiveErrors`. After 5 successive failures the session's `GmcpEnabled` flag is flipped off so a broken connection stops spamming the warning log. Successful emits reset the counter to zero.
- `JsonSerializerOptions` now includes `MaxDepth=8` (defense against pathological object graphs) and `DefaultIgnoreCondition=WhenWritingNull` (compact frames, IRE convention).

`Emit` and `EmitTo` are now thin wrappers that delegate to `TryWriteFrame`, so all paths get the same hardening with no duplication.

### `Char.Vitals` during combat (the original report)

Pre-fix vitals only emitted from `BaseLocation.LocationLoop` once per loop iteration. Multi-round fights happen inside a single LocationLoop iteration, so HP/MP/SP gauges in Mudlet/MUSHclient/TT++ froze at pre-combat values for the whole fight.

Hoisted the vitals emit into `GmcpBridge.EmitVitalsIfChanged(player)` with delta-tracking state moved to `SessionContext` (`LastGmcpHp`, `LastGmcpMaxHp`, `LastGmcpMana`, `LastGmcpMaxMana`, `LastGmcpStamina`). Two new combat emit points: end of every multi-monster round (after status ticks and end-of-round ability effects), and the top of every single-monster `GetPlayerAction` prompt (after status ticks, before the action menu). Both deduplicated -- a round where nothing changed doesn't spam the wire.

### `Room.Info` real exits

The v0.57.21 placeholder `exits = new {}` is replaced with a real exits map keyed by destination name. Mudlet's mapper script consumes this shape natively to draw the navigation graph. Two paths:

- **Dungeon rooms**: emit cardinal exits (`north`, `south`, `east`, `west`) with the target room ID as the value. Re-emitted at every `MoveToRoom` call so per-room navigation updates land in the client immediately, not just at top-level location entry.
- **Town locations**: emit a map keyed by destination location name (`inn`, `bank`, `church`, etc.) drawn from the static `LocationManager.navigationTable`.

`DungeonLocation.CurrentRoom` is now public so `LocationManager` can read the live navigation graph without reaching into private state.

### `Char.StatusVars` schema descriptor

Achaea / Iron Realms convention: a one-shot message that tells the client what each `Char.Status` field means. Emitted once per session right before the first `Char.Status` and tracked via `SessionContext.GmcpStatusVarsSent` so it doesn't repeat. Generic GMCP mappers (Mudlet's StatusVars plugin, etc.) can render fresh fields without bespoke per-server scripting.

### Combat lifecycle events

Five new combat-flow events:

- **`Char.Combat.Start`** at the top of `PlayerVsMonsters`. Carries the enemy list (`name`, `level`, `hp`, `maxHp`, `isBoss`) and an `ambush` flag. MUD client scripts can flip into combat-mode triggers (status panes, sound effects, hotbar expansion) without text-pattern matching.
- **`Char.Combat.End`** at the single return point of `PlayerVsMonsters`. Carries `outcome` (matches `CombatOutcome` enum string), `kills`, `xpGained`, `goldGained`. Clean "leaving combat" signal regardless of win / flee / subdue / death.
- **`Char.Combat.Killed`** in `HandleVictoryMultiMonster`, one frame per defeated monster. Carries `name`, `level`, `isBoss`. Lets scripts attribute XP/loot per-kill, tick counters, play victory sounds per-kill instead of once-per-fight.
- **`Char.Death`** at the top of `HandlePlayerDeath`. Carries `killer` and `resurrectionsLeft`. Emits BEFORE the resurrection / permadeath branch runs so MUD clients see the death event regardless of which path handles it.
- (Per-hit `Char.Combat.HitDealt` / `HitTaken` events deferred. Damage application is split across many sites in `CombatEngine.cs` and instrumenting them all risks silent gaps. Lifecycle events cover most scripting use cases without that risk.)

### `Char.Items.List`

Full backpack + equipped items emit. Backpack array carries `idx`, `name`, `type`, `minLevel` per item. `equipped` map keyed by lowercase slot name (`mainhand`, `body`, `head`, etc.) with item names. Emitted at character login and on every `Char.Combat.End` so loot picked up during the fight is reflected immediately. Subsequent inventory changes outside combat are picked up on the next location transition.

### `Char.Skills.List`

Class abilities and spells, both as separate arrays. Abilities carry `id`, `name`, `type`, `cooldown` (max), `staminaCost`, `manaCost`, `requiresLevel`. Spells carry `name`, `manaCost`, `requiresLevel`. Emitted at character login, on level-up (newly-unlocked abilities show up immediately), and on `Char.Combat.End` (post-fight cooldown state). Per-fight cooldown remaining is not yet tracked on `Character` for general abilities so the field reports the static max; this can be extended once `Character.AbilityCooldowns` is wired.

### Deferred to next pass: inbound GMCP parsing

`Core.Supports.Set` / `Add` / `Remove` (the IRE convention for clients to declare which packages they accept) are still not parsed inbound. The current input read path uses a UTF-8 `StreamReader` which decodes the IAC byte (0xFF) as `?` and silently drops it; parsing IAC SB GMCP frames out of the player input stream requires a TCP-layer `Stream` wrapper, which is a larger refactor than fits this release. `SessionContext.GmcpSubscribedPackages` is already in place to receive the parsed list once the wire-up lands. Practical impact is small: most GMCP clients (Mudlet, MUSHclient, TinTin++) accept whatever the server sends without requiring explicit subscription.

---

## Files Changed

### Modified Files

- `Scripts/Core/GameConfig.cs` -- version bump to 0.60.3 (carried over from v0.60.2 work), `SoftTauntStickChance = 75` constant for the soft-taunt design.
- `Scripts/Core/Monster.cs` -- new `TauntStickChance` field (default 100) for per-monster soft-taunt stickiness.
- `Scripts/Core/Character.cs` -- `EquipItem` non-weapon branch carves out rings (`item.Slot == LFinger` OR `RFinger`) so the v0.60.2 strict slot-type validation accepts either finger as the target.
- `Scripts/Locations/CastleLocation.cs` -- `SetCurrentKing` now sets `npc.King = true` on the source NPC and clears stale `King = true` flags on other NPCs from previous reigns.
- `Scripts/Systems/ChallengeSystem.cs` -- both throne challenger candidate filters (player-king branch and NPC-king branch) exclude the current king by name as defense-in-depth.
- `Scripts/Systems/CombatEngine.cs` -- `ProcessMonsterAction` taunt-decrement-before-branch fix; `SelectMonsterTarget` per-round soft-taunt roll; soft-taunt application at `aoe_taunt` / `undertow` / `abyssal_anchor` / `ApplyTankTauntAndBuff` sites in both single-monster and multi-monster paths; explicit hard-taunt resets at basic-[T] and Eternal Vigil sites; `GetMonsterStatusString` expanded from 4 to 13 displayed statuses with BURN/PSN disambiguation; new GMCP `Char.Vitals` emit at end of multi-monster round and at start of every single-monster player-turn prompt.
- `Scripts/Server/GmcpBridge.cs` -- centralized `TryWriteFrame` with package-name validation, frame-size cap (16KB), per-session error counter with auto-disable after 5 consecutive failures, JSON serializer hardening (MaxDepth=8, null skipping); new `EmitVitalsIfChanged`, `EmitItemsList`, `EmitSkillsList` static helpers.
- `Scripts/Server/SessionContext.cs` -- 5 `LastGmcp*` vitals delta fields; `GmcpConsecutiveErrors`; `GmcpStatusVarsSent`; `GmcpSubscribedPackages` HashSet (placeholder for inbound parsing).
- `Scripts/Systems/LocationManager.cs` -- `BuildGmcpExitsForLocation` real exits builder for both dungeon (cardinal directions) and town (navigation table) emit paths; `Char.StatusVars` one-shot at first emit.
- `Scripts/Locations/DungeonLocation.cs` -- public `CurrentRoom` accessor; per-room `Room.Info` re-emit at `MoveToRoom`.
- `Scripts/Locations/BaseLocation.cs` -- `EmitGmcpVitals` thin wrapper; `Char.Skills.List` re-emit on level-up.
- `Scripts/Core/GameEngine.cs` -- `Char.Items.List` + `Char.Skills.List` one-shot at character login.
- `.github/workflows/ci-cd.yml` -- diagnostic instrumentation on the deploy step.

---

## Deploy notes

- Standard recipe: publish linux-x64, tar, scp, restart `usurper-mud` and `sshd-usurper`.
- No DB schema changes -- all changes are code-only.
- `Monster.TauntStickChance` is in-memory only (transient per fight), not serialized. Existing player saves work unchanged.
- The NPC `King` flag bookkeeping fix self-applies on next king transition; pre-fix world states with a missing flag heal as soon as `SetCurrentKing` is called for any reason.

For testing post-deploy:

- Cast Freeze on a dungeon monster -> verify `FRZ(N)` appears next to the enemy's HP bar in the multi-monster status display.
- Solo a dungeon room with Aldric (Warrior NPC) tanking. Watch a 5-monster fight: a 3-round Shield Wall Formation should now visibly tick down each round, and ~25% of attacks should leak past the tank to the rest of the party.
- Talk to a royal guard NPC at the castle while a king is on the throne. Confirm the guard sees the throne as "occupied" via dialogue (whichever code paths check `npc.King`).
- Trigger an NPC throne challenge via world-sim tick -> confirm the challenger is never the current king.
- Loot a ring with both finger slots already occupied -> pick `[E]quip` -> pick `[R]ight Finger` -> confirm the ring lands on the right finger without the "belongs in the LFinger slot" error.
- Connect via Mudlet (or any GMCP client), enter combat, watch the HP/MP/SP gauges update each round and at every player-turn prompt instead of freezing pre-combat.
- In Mudlet, enable GMCP debug (`#config DEBUG GMCP ON` for TT++ or the Settings -> Debug GMCP toggle) and verify these frames arrive: `Char.StatusVars` once at login, `Char.Status` on every location change, `Char.Items.List` at login + each combat end, `Char.Skills.List` at login + level-up + combat end, `Room.Info` with real exits on every move (including dungeon room transitions), `Char.Combat.Start` / `Char.Combat.End` / `Char.Combat.Killed` / `Char.Death` at the corresponding combat events.
