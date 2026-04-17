# v0.57.2 - Party Inventory Viewer

Two recent reports traced to the same root cause: when a player used the `<`/`>` party cycler in the combat loot prompt and picked `[T] Take to inventory`, the item went to the currently-selected companion's inventory, not the player's. Since players couldn't see companion inventories, items felt like they'd vanished.

Rather than patching the loot flow to ignore the cycler, this release introduces Phase 1 of a "mule" pattern: party members (companions, spouse/lover, team NPCs) carry items intentionally, and the player can now see and recover those items through a new viewer.

## Party Inventory Viewer

New `[V]` / `[I]` menu option at three locations shows the inventory of each party member and lets the player take items back to their own inventory (subject to the normal 50-item cap).

- **Team Corner: `[V] View Inventories`** — shows all team NPCs and active companions.
- **Home: `[V] View Party Bags`** — shows spouse(s), lover(s), and active companions.
- **Dungeon party-management menu: `[I] Party Bags`** — shows every teammate in your current dungeon party.

The viewer lists each member with their item count, lets the player drill into one NPC's bag, and supports taking any item back to the player's inventory one at a time. Cursed items are flagged. Grouped players (in `/group` co-op) and echo characters (offline-player snapshots) are automatically filtered out — only characters the player actually controls and can recover items from are listed.

Transfers persist immediately through `SaveSystem.AutoSave` and `OnlineStateManager.SaveAllSharedState` (for online mode), matching the per-action save cadence Team Corner already uses.

## Explicit Transfer Labeling on the Combat Loot Prompt

When the player uses the `<`/`>` cycler in the combat loot prompt to select a companion, the `[T]` option now reads `"(T)ransfer to {companion}'s inventory"` instead of the ambiguous `"(T)ake to inventory"`. The confirmation message after a T-press targeting a companion reads `"Added X to {companion}'s inventory."` instead of `"Added X to your inventory."`.

Two new localization keys (`combat.loot_take_to_companion_option`, `combat.loot_added_to_companion`) translated across all 5 languages. The original labels remain in place for when the real player is selected, so single-character pickups read exactly as before.

## Inn Auto-Equip is Now Class-Aware

Root cause: `InnLocation.ScoreEquipment` just summed raw stats — highest weapon power wins, class fantasy ignored. For Warriors (Aldric), a high-power 1H mace outscored every shield in the off-hand slot. For Assassins (Vex), a high-power sword outscored a mid-power dagger even though Backstab + Lethal Precision both key off the Dagger weapon type. For Clerics (Mira), a 2H staff outscored a mace+shield combo because the 2H staff had higher raw weapon power. The slot loop's `IsTwoHanding` check correctly skipped the off-hand once the staff was equipped, leaving Mira with no shield.

Fix: `ScoreEquipment` now takes the target character and applies a class-role multiplier on top of the raw score. Summary:

- **Warrior / Paladin**: off-hand shields score ×3.0, 1H weapons ×0.3 (strong shield preference).
- **Cleric**: mace ×1.4 in main, 2H main-hand weapons ×0.5 (mace+shield preference), off-hand shields ×2.5.
- **Assassin**: daggers ×1.5, non-dagger 1H weapons ×0.7.
- **Ranger**: bows ×1.5 in main-hand.
- **Barbarian**: 2H main-hand weapons ×1.3.
- **Magician / Sage / MysticShaman**: staves ×1.4 in main-hand.
- **Bard**: instruments ×1.5.
- **Alchemist**: staves ×1.2 (mild INT-caster preference).

Multipliers are applied AFTER raw stat scoring, so within each class's preferred category the highest-stat item still wins. Picking the wrong weapon type now requires that wrong type to be raw-stat dominant by a huge margin, which is rare in practice.

## Umbral Step Guaranteed Crit Applies to Abilities

Umbral Step applies the `Hidden` status, which guarantees a critical hit on the next attack AND consumes Hidden. The basic-attack damage path at [CombatEngine.cs:2901](Scripts/Systems/CombatEngine.cs#L2901) correctly honored it — but the ability damage paths (single-monster and multi-monster) only did a standard RNG crit roll and never checked `Hidden`. So Umbral Step's crit promise worked on basic attacks but silently failed on every ability follow-up, which is almost always what an Abysswarden actually does after stealthing.

Fix: both ability damage paths now check `HasStatus(StatusEffect.Hidden)` before the random crit roll. If set, the ability auto-crits (using the same DEX-based crit multiplier as other crits), `Hidden` is consumed, and `abilityAlreadyCrit` is flagged so the subsequent random crit roll doesn't stack on top. The "stealth_crit" message fires inline. Same fix applied to the single-target ability path used by Backstab and the multi-target ability path. The same logic also covers Assassin stealth-crit attacks via abilities (previously only worked on basic-attack path).

## Main Street [P] Progress Screen Recognizes Old God Encounters

Root cause: Main Street's Progress screen at [MainStreetLocation.cs:3124](Scripts/Locations/MainStreetLocation.cs#L3124) gates each god's display behind `godState.HasBeenEncountered`. The flag was defined on `OldGodState` and checked here, but the ONLY production code that ever set it to `true` was the online admin console — `UpdateGodState`, `StartBossEncounter`, and the boss-defeat/save/ally paths all skipped it. Every player who actually fought, saved, or allied with an Old God still saw "???? Unknown" on their Progress screen because the flag stayed false.

Additionally, even when we now fix the setter, the flag wasn't in the save payload — `OldGodStates` serializes only `Status` as `Dictionary<int, int>`. A flag set in memory would be lost on logout.

Three-part fix:

1. `StoryProgressionSystem.UpdateGodState` sets `HasBeenEncountered = true` on any status change. Covers every code path that resolves an Old God (combat victory, dialogue save, alliance, moral paradox choices, admin edits).
2. `OldGodBossSystem.StartBossEncounter` sets `HasBeenEncountered = true` the moment the player enters the room — so even a player who flees the encounter gets proper progress-screen tracking.
3. New `OldGodsEncountered` list added to save data + serialize/restore wiring. Existing saves migrate: on load, any god already in a terminal status (`Defeated`/`Saved`/`Allied`/`Consumed`/`Awakened`) has the flag back-filled, so Aura's save gets Maelketh and Veloura marked as encountered on her next login without her needing to re-fight them.

## Abysswarden Corrupting Touch Heals Per Tick

The ability description reads "40 damage/round for 5 rounds. Each tick heals you", but the implementation used the generic `Poisoned` status for the DoT and granted the caster a 15% `Lifesteal` status on attacks. The lifesteal only fired when the player actually attacked the target; the per-round DoT damage never healed. So the "each tick heals you" promise only delivered intermittently — it looked to the player like the total heal only materialized over the course of the combat rather than being synchronized with the damage ticks.

Fix: Corrupting Touch now uses a dedicated DoT system (`Monster.CorruptingDotRounds` + `CorruptingDotTickDamage`) ticked in `ProcessMonsterAction` alongside standard poison. Each tick deals the ability's damage AND heals the player for 50% of that damage, shown inline as "Corruption gnaws at X for Y damage — you draw Z HP back." The status bar gains a `COR(n)` indicator alongside `PSN(n)` so the player can see the remaining rounds. Monster `Reset()` zeroes the new fields so nothing leaks between encounters.

## Abysswarden Shadow Harvest Bonus Actually Applies Now

Two bugs in the same ability, both in both combat paths (single-monster and multi-monster):

1. **Damage was double-applied.** The base damage block applied `actualDamage` (with defense/crit/cap), then a separate `case "shadow_harvest":` handler applied raw `abilityResult.Damage` *again*, ignoring defense. The handler's "+50% when below half HP" check operated on POST-first-hit HP, not pre-hit HP.
2. **Bonus silently skipped on killing blows.** If the base damage killed the target, the handler's `target.IsAlive` check returned false and the entire bonus+lifesteal branch was bypassed. Mystic's typical scenario: hitting an already-weakened enemy where base damage finished them off, so neither the 1.5x nor the 25% heal ever fired.

Fix: moved the `+50% vs. below-half-HP` multiplier and the 25% lifesteal heal into the base damage block, next to Execute's `×2 below 30% HP` pattern. Check now runs on PRE-damage HP, the multiplier is applied consistently with defense/crit/cap, the lifesteal uses actual damage dealt (including crit and other multipliers), and the heal fires even when the target dies to the hit. The old post-damage handler is deleted — no more double-application. Two new localization keys (`combat.shadow_harvest_feast`, `combat.shadow_harvest_drain`) for the inline bonus and heal messages, translated across 5 languages.

## XP Distribution Respects "Keep 100% for Myself"

Root cause was [CombatEngine.cs:23268](Scripts/Systems/CombatEngine.cs#L23268) explicitly treating a `100/0/0/0/0` XP split as "stale solo default" and overriding it to even-distribute. The original code comment admitted the system couldn't distinguish "I want 100% even with teammates" from "this is just left over from when I was solo," so it defaulted to the safer-for-new-players behavior. That meant players who genuinely wanted to keep the XP couldn't — even manually setting 100/0 would get overridden the moment combat started.

Fix: added a `Character.TeamXPIsExplicit` flag. Flag is false by default. The XP distribution UI (both the `[E] Even Split` and the per-slot set-to-N% paths) sets it to true on any edit. `AutoDistributeTeamXP` now honors the player's configured split unconditionally when the flag is true, and only auto-distributes when the flag is false (so first-time-with-a-teammate still gets the friendly auto behavior, and explicit settings stick across sessions).

Flag is serialized in the player save so the intent persists through logout/NG+.

## NPC Duels and Street Challenges No Longer Spawn Dead Opponents

Root cause: two sites that convert an `NPC` into a `Monster` for combat passed `npc.HP` (current HP) to `Monster.CreateMonster` instead of `npc.MaxHP`. If the NPC had been defeated recently in world-sim NPC-vs-NPC combat and was awaiting respawn — or if a race condition let `FindChallengerNPC`'s `IsAlive` filter pass an NPC whose HP dropped to 0 between the filter and the fight — the resulting combat monster would spawn with `HP=0/0` and die in round one, paying no XP or gold. Fixed:

- [InnLocation.cs:1257](Scripts/Locations/InnLocation.cs#L1257) — player-initiated Inn duel.
- [StreetEncounterSystem.cs:1388](Scripts/Systems/StreetEncounterSystem.cs#L1388) — NPC-initiated street challenge / mugging / brawl (the path Mystic hit).

Both now use `npc.MaxHP` with a fallback to `Math.Max(1, npc.HP)` for the (unlikely) case where `MaxHP` is somehow 0. Thematically consistent — a player entering combat always does so at their own MaxHP, and the NPC should fight the duel in full fighting shape, not at whatever HP a recent-but-unrelated brawl left them at.

## Dormitory Darkness Gains Use Paired Alignment

Three direct `currentPlayer.Darkness +=` mutations in the Dormitory (attacking a sleeping NPC, attacking a sleeping player, the "thunderous shout" action) bypassed `AlignmentSystem.ChangeAlignment` — so no paired Chivalry loss fired and the news pipeline stayed silent on significant evil acts. All three now route through `ChangeAlignment(amount, isGood: false, reason)` for consistency with the v0.57.0 paired-movement system that murder and altar desecration already use. Net effect: killing a sleeping player now correctly also burns ~12 Chivalry (half the +25 Darkness gain) and generates a dark-act news entry if the actor had enough Chivalry to lose.

## Fire DoT Kills No Longer Mislabel As Poison Deaths

Root cause: `ProcessMonsterAction`'s DoT tick block cleared `monster.IsBurning = false` when `PoisonRounds` hit 0, **before** the `!monster.IsAlive` death-message check read `IsBurning` to pick between "consumed by flames" / "succumbs to poison." On a final tick that both reduced the counter to 0 AND killed the monster, the burning flag was already cleared by the time the death text rendered — so every fire-DoT killing blow on the final tick misreported as poison.

Fix: capture `IsBurning` into a local `wasBurning` before any state mutation; render both the tick text and the death text off the captured value; only clear the flags after the death check has run. Monsters that survive the final tick still get `Poisoned` / `IsBurning` cleared as before.

## NG+ No Longer Inherits the Prior Cycle's Worshipped God

Reported during the v0.57.2 cycle: starting New Game+ rolls a fresh character, but the player keeps worshipping whichever god they picked in the previous cycle. The fresh `Character` does default `WorshippedGod = ""`, so the player-side field was already clean — the residual state was on the world-side `GodSystem.playerGods` dictionary (keyed by player name). That dict gets cleared when the player's save is deleted (via `SaveSystem.DeleteSave` → `GodSystem.SetPlayerGod(playerName, "")`), but NG+ doesn't delete the save — it just overwrites it — so the cleanup path never fires. End result: temple, pantheon, `/health`, and the target god's believer count all still reflected the prior-cycle worship.

Fix: the NG+ branch of `CreateNewGame` now calls `GodSystem.SetPlayerGod(playerName, "")` after `StartNewCycle` runs. `SetPlayerGod` with an empty name removes the dictionary entry AND decrements the old god's believer count, matching the DeleteSave flow for non-NG+ fresh characters.

## Manwe No Longer Blocks Physical Damage With Magical Immunity

While Manwe was in his magical-immunity phase, physical attacks (backstab, power attack, precise strike, ranged, off-hand strikes, teammate melee) were also being reduced to ~10% residual. Manwe has alternating immunity phases by design — phase 2 blocks physical, phase 3 blocks magical — but both types were eating the magical reduction in phase 3.

Root cause: `ApplySingleMonsterDamage` (the damage-application funnel used by ~15 attack paths including backstab, power/precise strike, ranged, smite, soul strike, off-hand, teammate attacks, and spell cast) hardcoded `ApplyPhaseImmunityDamage(target, damage, isMagicalDamage: true)` regardless of what type of damage was actually coming in. It also did not check `IsPhysicalImmune` at all, so physical attacks routed through this function bypassed physical-immunity reduction during Manwe phase 2. The sibling regular-attack path at [CombatEngine.cs:3239](Scripts/Systems/CombatEngine.cs#L3239) correctly checked the physical flag with `isMagicalDamage: false` — only the ability/spell funnel had the inversion.

Fix: `ApplySingleMonsterDamage` gained an `isSpellDamage` parameter (default `false`). The immunity block now branches on it — `isSpellDamage: true` checks `IsMagicalImmune`, `isSpellDamage: false` checks `IsPhysicalImmune`. Two callers pass `true`: the spell-cast path at line 14458 and Soul Strike at line 11472 (holy damage from Chivalry-scaling channel). All other callers (regular attack, backstab, power attack, precise strike, smite, ranged attack, off-hand strike, teammate attacks) are physical and take the default. The on-screen `"magical immunity absorbs most of the spell damage"` message now only fires for actual spells, matching what the code claims to do.

## Companion / NPC Teammate Spells No Longer Bypass Boss Immunity

Companion-cast spells ignored magical immunity entirely. Same problem on the player's own single-monster spell cast path, and the NPC teammate AoE branch.

Root cause: three direct-HP-subtraction spell paths bypassed every boss-layer reduction. Specifically:
- `ApplySpellEffects` (player single-monster spell cast at [CombatEngine.cs:22313](Scripts/Systems/CombatEngine.cs#L22313)) did `target.HP = Math.Max(0, target.HP - spellResult.Damage)`.
- `TryTeammateOffensiveSpell` AoE branch at [CombatEngine.cs:15995](Scripts/Systems/CombatEngine.cs#L15995) did `monster.HP -= (int)actualDamage` per target.
- `TryTeammateOffensiveSpell` single-target branch at [CombatEngine.cs:16027](Scripts/Systems/CombatEngine.cs#L16027) did the same.

Unlike the ability/weapon paths that run through `ApplySingleMonsterDamage` (which applies magical immunity, divine armor, armor piercing, and defense), these three direct-HP paths skipped everything. Jorund's Power Word: Kill therefore dealt its scaled base damage straight into Manwe's HP regardless of phase.

Fix: new `ApplyBossSpellProtections(monster, damage, announce)` helper applies magical-immunity reduction (with the standard absorbs message) and divine armor reduction, mirroring what `ApplySingleMonsterDamage` does. Wired into all three direct-HP paths. The AoE branch announces the immunity message once per cast, not per target. Also fixed a latent `(int)actualDamage` cast-to-int on a `long` HP subtraction in the AoE branch that would have overflowed for damage above ~2.1B.

## Old God Fights: 75% Per-Hit Cap on NPC Teammates

Maelketh critted Aldric for ~90% of his MaxHP in a single hit and nearly one-shot him. Existing companion caps were 85% for bosses (+ a 15% first-3-rounds cap), but several Old-God-specific damage paths had NO per-hit cap at all:

- `MonsterAttacksCompanion`'s "Old God custom ability" fallback at [CombatEngine.cs:16734](Scripts/Systems/CombatEngine.cs#L16734) — uncapped.
- `ProcessBossAoE` per-target damage at [CombatEngine.cs:24868](Scripts/Systems/CombatEngine.cs#L24868) — the party-wide AoE (Creation's End, Maelstrom, Maelketh's equivalent) subtracted raw damage from teammate HP with no cap.
- `ProcessBossChannel` per-teammate damage at [CombatEngine.cs:24772](Scripts/Systems/CombatEngine.cs#L24772) — Unmake Reality / Veloura's channeled blast were uncapped on teammates.

And the existing capped paths (ability DirectDamage / DamageMultiplier / basic attack) still permitted 85% per hit, which is a second-shot kill on any squishy support companion.

Fix: new `GameConfig.OldGodTeammateDamageCapPercent = 0.75f` + `CapTeammateDamageInOldGodFight(target, damage)` helper on `CombatEngine`. The helper no-ops outside Old God fights (`BossContext == null`) and caps at 75% MaxHP otherwise. Wired into all five teammate-damage sites: the two previously uncapped boss paths (AoE + Channel), the Old-God custom-ability fallback, the boss ability DirectDamage / DamageMultiplier caps (tightens 85% → 75% during Old God fights), and the basic monster attack cap (same tightening). Regular boss fights outside the Old God system are unaffected; the cap only narrows when `BossContext` is set, which only happens during Old God encounters.

### Files Changed

- `Scripts/Core/GameConfig.cs` — Version 0.57.2.
- `Scripts/Locations/BaseLocation.cs` — New `ShowPartyInventoryViewer(List<Character>)` and `ShowSinglePartyMemberInventory(Character)` helpers that render a party member list, drill into one member's bag, and handle the take-back transaction with save persistence.
- `Scripts/Locations/TeamCornerLocation.cs` — New `[V] View Inventories` menu option routing to `ViewTeamInventories()` which pulls team NPCs + active companions into the viewer.
- `Scripts/Locations/HomeLocation.cs` — New `[V] View Party Bags` menu option routing to `ViewHomePartyInventories()` which pulls spouses, lovers, and active companions into the viewer.
- `Scripts/Locations/DungeonLocation.cs` — Party-management menu gained `[I] Party Bags` option that passes the current dungeon teammates to the viewer.
- `Scripts/Systems/CombatEngine.cs` — Combat loot prompt now shows `(T)ransfer to {name}'s inventory` when a companion is selected via `<`/`>`, and the post-transfer confirmation names the companion explicitly instead of the ambiguous `"added to inventory"`.
- `Scripts/Locations/DormitoryLocation.cs` — Three direct `Darkness +=` mutations (attacking a sleeping NPC, attacking a sleeping player, the thunderous-shout action) now route through `AlignmentSystem.ChangeAlignment` so paired chivalry loss fires and the news pipeline sees them.
- `Scripts/Locations/InnLocation.cs` — Player-initiated Inn duel: NPC→Monster conversion uses `npc.MaxHP` instead of `npc.HP`. Prevents fighting a "dead" NPC who's awaiting respawn.
- `Scripts/Systems/StreetEncounterSystem.cs` — Same fix for the street challenge / mugging / brawl NPC→Monster conversion. Fixes Mystic's 0/0 Astrid Juniper report.
- `Scripts/Core/Character.cs` — New `TeamXPIsExplicit` bool property.
- `Scripts/Systems/SaveDataStructures.cs` + `Scripts/Systems/SaveSystem.cs` + `Scripts/Core/GameEngine.cs` — Save/load plumbing for `TeamXPIsExplicit`.
- `Scripts/Systems/CombatEngine.cs` — `AutoDistributeTeamXP` now short-circuits to "honor player's configured split" when `TeamXPIsExplicit` is true, only dead-teammate redistribution happens. Legacy non-explicit saves still fall through to the old has-custom-distribution heuristic.
- `Scripts/Locations/DungeonLocation.cs` — `[E] Even Split` button and per-slot percentage editor both set `player.TeamXPIsExplicit = true` so subsequent auto-distribute runs honor the player's intent.
- `Scripts/Systems/CombatEngine.cs` — Abysswarden Shadow Harvest: `+50%` bonus (when target below half HP) and `25%` lifesteal heal are now inlined in both the single-monster and multi-monster base damage blocks, next to Execute. Post-damage `case "shadow_harvest"` handlers deleted — they were double-applying damage and skipping entirely when base damage killed the target. Abysswarden Corrupting Touch: `case "corrupting_dot"` handlers in both paths now flag `monster.CorruptingDotRounds` + `monster.CorruptingDotTickDamage` instead of using the generic `Poisoned` + attack-lifesteal hack. `ProcessMonsterAction` got a new tick block next to poison that damages the target and heals the caster 50% per round, matching the ability description. Monster status bar gains `COR(n)` indicator.
- `Scripts/Core/Monster.cs` — New `CorruptingDotRounds` (int) + `CorruptingDotTickDamage` (long) fields; `Reset()` clears both.
- `Scripts/Systems/StoryProgressionSystem.cs` — `UpdateGodState` now also sets `state.HasBeenEncountered = true` so every status change (combat, dialogue, save, ally) is reflected on the Main Street Progress screen.
- `Scripts/Systems/OldGodBossSystem.cs` — `StartBossEncounter` marks `HasBeenEncountered = true` the moment the player enters the encounter so fled/aborted encounters still count.
- `Scripts/Systems/SaveDataStructures.cs` + `Scripts/Systems/SaveSystem.cs` — New `OldGodsEncountered` list in save data; save/restore wired; existing saves back-fill the flag for gods in terminal-resolution statuses so Aura and anyone else who'd already beaten gods sees the correct Progress screen on next login.
- `Scripts/Core/GameConfig.cs` — New `OldGodTeammateDamageCapPercent = 0.75f` constant.
- `Scripts/Systems/CombatEngine.cs` — New `CapTeammateDamageInOldGodFight(target, damage)` helper applies a 75%-MaxHP per-hit cap on companions/NPC teammates only when `BossContext != null` (Old God fights). Wired into 5 sites: `ProcessBossAoE` teammate damage, `ProcessBossChannel` teammate damage, the Old-God custom-ability fallback in `MonsterAttacksCompanion`, and the existing ability + basic-attack caps (which tighten from 85% to 75% during Old God fights). Fixes the Maelketh-critting-Aldric-for-90% report.
- `Scripts/Systems/CombatEngine.cs` — New `ApplyBossSpellProtections(monster, damage, announce)` helper applies magical-immunity reduction and divine armor reduction. Wired into `ApplySpellEffects` (player single-monster spell cast), `TryTeammateOffensiveSpell` AoE branch, and `TryTeammateOffensiveSpell` single-target branch. These three paths previously subtracted spell damage directly from monster HP with no boss-layer reductions, letting companion-cast spells ignore Manwe's magical immunity. AoE branch announces the absorbs message once per cast; latent `(int)` cast on the AoE `monster.HP -=` removed so large damage can't wrap.
- `Scripts/Systems/CombatEngine.cs` — `ApplySingleMonsterDamage` gained `bool isSpellDamage = false` parameter and its boss-immunity block now branches on it: `true` applies magical-immunity reduction with the magical-immunity message, `false` applies physical-immunity reduction with the physical-immunity message. Spell cast and Soul Strike callers now pass `isSpellDamage: true`; all 13 other callers (backstab, power attack, precise strike, smite, ranged, off-hand strike, teammate attacks, regular attack) default to `false`. Fixes Thornwood's Manwe report where magical immunity was eating physical-ability damage. Collided local variable renamed to `suppressMeleeFlavor` to avoid shadowing the new parameter.
- `Scripts/Core/GameEngine.cs` — NG+ branch of `CreateNewGame` now clears `GodSystem.playerGods[playerName]` (by calling `SetPlayerGod(playerName, "")`) so the prior cycle's god worship, and its believer count, do not carry over. Non-NG+ fresh characters already got this via `SaveSystem.DeleteSave`; NG+ overwrites the save instead of deleting it, so the cleanup path needs to fire explicitly.
- `Scripts/Systems/CombatEngine.cs` — `ProcessMonsterAction` DoT tick block now captures `IsBurning` into a local before any mutation, renders both the tick message and the death message off the captured value, and only clears `Poisoned`/`IsBurning` after the death-check path has run. Fixes Lumina's fire-DoT-mislabeled-as-poison death message.
- `Scripts/Systems/CombatEngine.cs` — Ability damage paths (both single-monster and multi-monster) now check `HasStatus(StatusEffect.Hidden)` before the random crit roll and apply a DEX-based guaranteed crit on match, consuming the Hidden status. Fixes Mystic's Umbral Step report; also covers Assassin stealth-via-ability scenarios that previously only worked on basic attacks.
- `Scripts/Locations/InnLocation.cs` — `ScoreEquipment` takes a target character and applies class-based weapon preferences via `ApplyClassWeaponPreference`. Multipliers for 10 classes: tanks boost shields / de-prioritize 1H-offhand, Cleric boosts mace+shield over 2H staff, Assassin boosts daggers, Ranger bows, Barbarian 2H, casters staves, Bard instruments. Fixes Lumina's report of Aldric getting a mace instead of shield, Vex getting a sword instead of a dagger, and Mira over-preferring 2H staff.
- `Localization/en.json`, `es.json`, `fr.json`, `hu.json`, `it.json` — 17 new keys: `home.view_party_inv`, `team.menu_view_inventories`, `ui.cursed`, 12 `party_inv.*` keys covering the viewer UI, and 2 `combat.loot_*_to_companion*` keys for the explicit transfer labels on the loot prompt, translated across all five supported languages.
