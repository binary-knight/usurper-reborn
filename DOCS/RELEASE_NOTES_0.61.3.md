# v0.61.3 -- Beta

Hotfix on top of v0.61.2. Two player-reported bugs.

## Fix: wilderness monster intro lines leaked dungeon-family flavor

Player report (Lv.30 Elf Sage, post-v0.61.2 deploy): "I fought a black bear crackling with elemental fury and a bandit scout breathing fire."

The v0.61.2 wilderness-monster fix patched `WildernessLocation.CombatEncounter` to override `FamilyName`, `TierName`, `AttackType`, `MonsterColor`, `CanSpeak`, `MonsterClass`, `Undead`, and `SpecialAbilities` against a per-name profile table after `MonsterGenerator.GenerateMonster` had already picked a random dungeon family / tier. What the fix MISSED: `monster.Phrase`, the intro flavor line that `MonsterGenerator` set from the randomly-picked family before the wilderness override ran.

`MonsterGenerator.GetMonsterPhrase` returns family-specific intro flavor:

- Elemental: `"crackles with raw elemental fury."`
- Draconic / Drake: `"roars and breathes a gout of fire!"`
- Beast: `"snarls and growls menacingly."`

So when the generator rolled a Black Bear's underlying family as Elemental and the player's swing kicked off, the intro line read "Black Bear crackling with elemental fury" -- mechanically the bear was now correctly a Beast with `CrushingBlow`, but the leftover phrase betrayed the bug. Same shape for the Bandit Scout that rolled an underlying Draconic Drake.

**Fix.** Reset `monster.Phrase` based on the WILDERNESS profile's family right after the rest of the field overrides, using a family-keyed switch that mirrors `MonsterGenerator.GetMonsterPhrase`'s defaults. Maps each wilderness family (Beast, Humanoid, Undead, Insectoid, Construct, Elemental, Aquatic, Draconic, Plant, Fey, Giant) to a sensible default phrase. Unknown families fall back to silent (empty phrase).

After the fix: Bandit Scout draws steel and prepares to fight. Black Bear snarls and growls menacingly. Storm Elemental (a legitimately Elemental-family wilderness creature) still crackles with raw elemental fury -- the flavor line correctly matches the actual family the creature has now, not whatever the random generator picked first.

## Fix: floor bosses had less HP than champions on the same floor

Player report (post-cleared-floor observation): "On the last few levels I've cleared the champions typically have more HP than the floor boss, regular enemies often have equal to the boss. Is that how it's supposed to be?"

No, that wasn't the design. `MonsterGenerator.CalculateMonsterStats` defines three HP tiers at construction time, branching on the `isBoss` / `isMiniBoss` parameters:

| Type | HP Multiplier (vs base) |
| --- | --- |
| Regular | 1.0x |
| Champion (mini-boss) | 2.2x |
| Floor boss | 2.8x |

So the intended ordering is regular < champion < boss. What the player actually saw on cleared floors was champion > boss = regular (in the boss room). Same observation, three measurements.

Root cause in `DungeonLocation.PerformCombat` boss-room block. The pre-fix code:

1. Called `MonsterGenerator.GenerateMonsterGroup(floor, random)` for every room including the boss room. `GenerateMonsterGroup` rolls one of two outcomes: 10% chance of a single champion (calls `GenerateMonster` with `isMiniBoss: true`, gets the 2.2x HP multiplier from `CalculateMonsterStats`), or 90% chance of 1-5 regulars (calls `GenerateMonster` with `isBoss: false, isMiniBoss: false`, gets the 1.0x baseline). **Never `isBoss: true`.** No code path in `GenerateMonsterGroup` ever called for a boss-tier monster.
2. In the boss room, multiplied every monster's HP by 1.5x as a post-hoc "boss room HP boost."
3. If no monster had `IsBoss = true` (which was 100% of the time, since step 1 never sets it), flipped `monsters[0].IsBoss = true` AFTER stats were calculated AND after the 1.5x boost, and renamed it to the theme-appropriate boss name.

Because `CalculateMonsterStats` branches on the `isBoss` parameter at construction time, flipping `IsBoss = true` AFTER construction missed the 2.8x boss HP / 1.25x damage / 1.2x defense multipliers entirely. The post-hoc 1.5x HP boost was the boss's ONLY scaling over a regular monster.

Net effect:

| Type | Actual Multiplier | Intended |
| --- | --- | --- |
| Regular (any room) | 1.0x | 1.0x |
| Champion (regular room) | 2.2x | 2.2x |
| **Regular in boss room** | **1.5x** | 1.0x (unintended buff) |
| **Floor "boss"** | **1.5x** | 2.8x |
| **Champion in boss room** | **3.3x** (2.2 x 1.5) | 2.2x (unintended super-buff) |

So a champion in a boss room ended up outclassing the boss by more than 2x HP, and a regular standing next to the boss in the boss room had identical HP to the boss. The boss room felt tough mostly because of bystander buffs, not the boss itself.

**Fix.** Replace the post-hoc rename in `DungeonLocation.PerformCombat` with a proper boss regenerate: call `MonsterGenerator.GenerateMonster(level, isBoss: true, random: dungeonRandom)` so `CalculateMonsterStats` applies the boss-tier 2.8x HP / 1.25x damage / 1.2x defense multipliers at construction time. Override the lead monster slot with the regenerated boss, copy the theme-appropriate name and phrase onto it. Drop the universal 1.5x HP boost to bystanders entirely -- the boss room is dangerous because the BOSS is dangerous, not because every minion is also buffed.

After the fix:

| Type | HP Multiplier |
| --- | --- |
| Regular (any room, including boss room) | 1.0x |
| Champion (any room, including boss room) | 2.2x |
| Floor boss | 2.8x |

Boss > champion > regular, matching the design tiers. Knock-on: bystander monsters in boss rooms are now slightly easier than before (no more 1.5x band-aid buff), but the boss itself is meaningfully tougher (~87% more HP), so the boss room overall feels more boss-focused and the boss fight reads correctly as the climax of the floor instead of a slightly-renamed regular.

## Fix: companion / NPC-teammate potion stashes never refilled after recruit-time

Player question: "How do I refill companion healing/mana potion count?" Honest answer was: you couldn't. Real gap, not user-side missing knowledge.

`CompanionSystem.RefillCompanionPotions` had a comment claiming "called on recruit, rest, new day." Only the **recruit** caller existed. The bulk method `RefillAllCompanionPotions()` was defined but had zero callers anywhere in the codebase. Daily reset didn't touch it. Inn sleep didn't touch it. Home rest didn't touch it. Healer purchases didn't touch it.

So a companion's internal HP/mana potion stash was whatever they had at recruit time (role-based: 5 healing for tank/DPS or 2 for healer/bard, plus level/2 bonus; mana potions for casters at 3 + level/3) and then it monotonically decreased as the AI self-heals at <30% HP during combat. Once it hit zero, it stayed at zero for the rest of the character's life. Same gap for NPC teammates -- they spawned with `NPCSpawnSystem`-rolled potion counts but had no refill path.

**Fix.** New `CompanionSystem.RefillAllPartyPotions(Character player)` helper that:

1. Calls the existing `RefillAllCompanionPotions()` for the player's active companions (uses the per-role formula already there: tank/DPS get base 5 + level/2 healing, healer/bard get base 2 + level/2 healing, casters get 3 + level/3 mana).
2. Walks `NPCSpawnSystem.Instance.ActiveNPCs` and refills any NPC on the player's `Team` OR in their `DungeonPartyNPCIds` list. NPCs use the same role-scaled formula: caster NPCs (`ClassAbilitySystem.IsSpellcaster(npc.Class)`) get base 2 healing + 3+Level/3 mana; non-casters get base 5 healing + Level/2 bonus, no mana potions. Refill is one-directional (only fills up, never reduces a stash already above the formula's target).

Three wired call sites:

- `InnLocation.SleepAtInn` -- after the existing HP/Mana/Stamina restore.
- `HomeLocation.DoRest` -- after the existing HP/Mana restore.
- `DailySystemManager.ProcessPlayerDailyEvents` -- at the top, before grief / drugs / prison processing.

Companion potion counts already round-tripped through save/load correctly (CompanionSaveData has the fields; SaveSystem and CompanionSystem read/write both sides). NPC teammate potion counts did NOT round-trip -- that's a separate fix in this release (see below).

## Fix: NPC teammate potion counts never persisted

Audit-found gap surfaced while wiring `RefillAllPartyPotions`. NPCs inherit `Character.Healing` (long) and `Character.ManaPotions` (long), and `NPCSpawnSystem.InitializeClassicNPCs` rolls initial values (`npc.Healing = random.Next(npc.Level, npc.Level * 3)` plus mana potions for spellcaster classes). The AI consumes these mid-combat at the <30% HP threshold. Save round-trip for those fields was completely missing across all four sites:

| Site | Pre-fix | Post-fix |
| --- | --- | --- |
| Single-player WRITE: `SaveSystem.SerializeNPCs` | ✗ field not set on NPCData | ✓ `HealingPotions = (int)npc.Healing, ManaPotions = (int)npc.ManaPotions` |
| Single-player READ: `GameEngine.RestoreNPCs` | ✗ field not read back | ✓ `Healing = data.HealingPotions, ManaPotions = data.ManaPotions` |
| Online WRITE: `OnlineStateManager.SerializeCurrentNPCs` | ✗ field not set | ✓ same write fields |
| Online READ: `WorldSimService.RestoreNPCsFromData` | ✗ field not read | ✓ same read fields |

Also added the matching `HealingPotions` / `ManaPotions` fields to `NPCData` in `SaveDataStructures.cs` (they didn't exist before -- only the Companion equivalent did). Default-zero on pre-v0.61.3 saves; the next refill (daily reset / Inn rest) tops them back up via the new helper above.

Net: any potion consumption during combat, any player gift via the new dungeon menu option (below), and any refill via Inn/Home/daily-reset now persists across save reloads in single-player AND across world-sim reloads in online mode.

## Feature: give potions to a teammate's stash from the dungeon potion menu

Companion to the refill fixes. The existing dungeon potion menu had `[T] Heal teammate` and `[G] Give mana to teammate`, but both DRINK the player's potion to instantly restore the teammate's HP / MP. Neither transferred the potion to the teammate's own stash for later AI self-heal use.

New `[I] Issue potions to teammate's stash` option. Walks a three-step flow:

1. Pick healing potion or mana potion (only the types the player actually has are shown).
2. Pick the target teammate from a filtered list. Mana potions filter to caster teammates only (mana classes); healing potions are useful to everyone. Targets with no room in their stash (already at the role-scaled cap) are also filtered out, with a specific "no candidates" message if nobody qualifies.
3. Pick how many to give: 1, or fill their stash up to the cap.

On confirm: decrements the player's stash (`player.Healing` or `player.ManaPotions`), increments the teammate's stash (companion's `HealingPotions` / `ManaPotions` via `CompanionSystem.SyncCompanionPotions(wrapper)` to flow back to the underlying `Companion` object; NPC teammates are real `NPC` references in `NPCSpawnSystem.ActiveNPCs` so the mutation persists directly on the live object). In online mode, fire-and-forget `SaveAllSharedState()` runs so the NPC potion change reaches `world_state.npcs` before the next world-sim reload could clobber it (relies on the v0.61.3 serialization fix above).

14 new English loc keys for the menu flow (pick-type / pick-teammate / no-candidates / give-one / give-full / gave-healing / gave-mana / stash-now / stash-count / etc.), full translation pass into Hungarian, Spanish, French, and Italian.

## Fix: Druid Shrine pilgrimage buff used real-time hours in single-player

Player question (Lv.8 Elf Sage, single-player): "In single player buff from Wilderness pilgrimage lasts for real time 24 hours, not in game time. Is that intentional?"

No, that was a real inconsistency. The shrine attunement was implemented in v0.61.0 as `Character.AttunedShrineExpiresUtc = DateTime.UtcNow.AddHours(24)` -- a wall-clock 24-hour timer. That's correct for online mode (server runs 24/7 regardless of player presence; real-time is the natural reference), but wrong for single-player where every other timed system tracks in-game time: daily counters reset on sleep, fatigue accumulates per dungeon room, dungeon respawn ticks on actual play time, etc. A single-player who attuned at a shrine, played for 30 minutes, quit, and came back the next real-time day found their buff expired even though they only spent ~30 in-game minutes on it. Conversely, a marathon session that crossed multiple in-game days (sleep / `[Z] Wait`) would keep the buff active despite the in-fiction time elapsed.

**Fix.** Two parallel expirations, one per game mode:

- **Online:** unchanged. `AttunedShrineExpiresUtc` still gates `HasActiveShrineAttunement`.
- **Single-player:** new `Character.AttunedShrineExpiresGameDay` (int) tracks the in-game day the buff expires on. `HasActiveShrineAttunement` branches on `DoorMode.IsOnlineMode`: online checks UTC, single-player checks `StoryProgressionSystem.CurrentGameDay <= AttunedShrineExpiresGameDay`. Both fields are set when attuning, so a save loaded under either game mode has the right timer ready.

The 24-hour `DruidShrineData.AttunementHours` constant maps to 1 game day in single-player (`Math.Ceiling(24/24.0) = 1`). If the constant rises in a future tuning pass, the math still rounds up to whole game days (minimum 1). So a single-player who attunes and then sleeps once at the Inn or Home (advancing to the next game day) will find the buff expired -- matching the "lasts one day" feel of the in-fiction description.

**Display.** New `Character.GetShrineTimeRemainingLabel()` helper returns the right unit per mode: "12.5h" online, or "2 days" / "1 day" / "expires today" in single-player. The four display sites (BaseLocation `/health`, WildernessLocation main menu / BBS menu / pilgrimage screen) all route through the helper. Five new loc keys per language (`shrine.time_hours`, `shrine.time_today`, `shrine.time_one_day`, `shrine.time_n_days`, `shrine.remaining_suffix`); existing pilgrimage-active templates dropped their hardcoded "h" suffix so the helper's label can supply the unit.

**Save compatibility.** Pre-v0.61.3 saves have `AttunedShrineExpiresGameDay = 0`. If those saves are loaded into single-player mode, `HasActiveShrineAttunement` returns false (the guard `AttunedShrineExpiresGameDay > 0` fails) -- the player loses their active attunement, but can attune again immediately at zero cost beyond the standard pilgrimage cooldown. Acceptable trade-off for the small window of affected saves; the alternative (auto-migrate UTC-based expiry into a single-player game-day count at load time) would need to know what "today" was at the moment the save was written, which isn't preserved.

## Files Changed

- `Scripts/Core/GameConfig.cs` -- Version 0.61.3.
- `Scripts/Locations/WildernessLocation.cs` -- `CombatEncounter` now resets `monster.Phrase` after the wilderness profile is applied, using a family-keyed switch (Beast / Humanoid / Undead / Insectoid / Construct / Elemental / Aquatic / Draconic / Plant / Fey / Giant) that mirrors `MonsterGenerator.GetMonsterPhrase`'s defaults. Closes the leak where Black Bear inherited Elemental's "crackles with raw elemental fury" and Bandit Scout inherited Draconic Drake's "roars and breathes a gout of fire!"
- `Scripts/Locations/DungeonLocation.cs` -- Boss room block in `PerformCombat` rewritten. Pre-fix: 1.5x HP boost to every monster in the room plus post-hoc `IsBoss = true` flip on monsters[0]. Post-fix: regenerate the lead slot via `MonsterGenerator.GenerateMonster(level, isBoss: true, random)` so `CalculateMonsterStats` applies the 2.8x HP / 1.25x damage / 1.2x defense boss-tier multipliers at construction time. Theme-appropriate boss name and phrase copied onto the regenerated boss. No more universal HP boost for bystander monsters in the boss room.
- `Scripts/Systems/CompanionSystem.cs` -- New `RefillAllPartyPotions(Character player)` helper. Calls the existing `RefillAllCompanionPotions()` and additionally walks `NPCSpawnSystem.Instance.ActiveNPCs` to refill any NPC on the player's `Team` or in `DungeonPartyNPCIds`. Caster NPCs (`ClassAbilitySystem.IsSpellcaster`) get base 2 healing + 3+Level/3 mana; non-casters get base 5 healing + Level/2 bonus, no mana. Refill is one-directional (won't reduce a stash already above the formula's target).
- `Scripts/Locations/InnLocation.cs` -- `SleepAtInn` now calls `CompanionSystem.Instance?.RefillAllPartyPotions(currentPlayer)` after HP/Mana/Stamina recovery.
- `Scripts/Locations/HomeLocation.cs` -- `DoRest` now calls `CompanionSystem.Instance?.RefillAllPartyPotions(currentPlayer)` after HP/Mana recovery.
- `Scripts/Systems/DailySystemManager.cs` -- `ProcessPlayerDailyEvents` calls `CompanionSystem.Instance?.RefillAllPartyPotions(player)` at the top, before grief / drugs / prison processing.
- `Scripts/Systems/SaveDataStructures.cs` -- New `HealingPotions` (int) and `ManaPotions` (int) fields on `NPCData`. Pre-fix NPCData had no potion fields; only the parallel `CompanionSaveData` did.
- `Scripts/Systems/SaveSystem.cs` -- `SerializeNPCs` now writes `HealingPotions = (int)npc.Healing, ManaPotions = (int)npc.ManaPotions` so single-player NPC saves preserve potion stashes.
- `Scripts/Core/GameEngine.cs` -- `RestoreNPCs` now reads `Healing = data.HealingPotions, ManaPotions = data.ManaPotions` so single-player loads restore potion stashes. Pre-v0.61.3 saves default to 0 (refilled on next daily reset / Inn rest).
- `Scripts/Systems/OnlineStateManager.cs` -- `SerializeCurrentNPCs` now writes the same potion fields so world_state.npcs preserves NPC potion stashes across world-sim reloads.
- `Scripts/Systems/WorldSimService.cs` -- `RestoreNPCsFromData` now reads the matching fields when rebuilding NPCs from the world_state JSON.
- `Scripts/Locations/DungeonLocation.cs` -- New `[I] Issue potions to teammate's stash` option on the dungeon potion menu (SR + visual). Calls new `IssuePotionToTeammateStash` method that prompts for healing/mana, picks a teammate (filtered to caster targets for mana, and stash-not-full for both), prompts for 1-or-fill, and transfers from player stash to teammate stash. Wires through `CompanionSystem.SyncCompanionPotions` for companions; NPC teammates persist directly via the live reference. Online mode fires `SaveAllSharedState()` to push the change to world_state. New helpers `GetStashState(target, mana)` and `HasStashRoom(target, mana)`.
- `Localization/{en,hu,es,fr,it}.json` -- 14 new keys per language for the issue-potion flow (`dungeon.issue_potion_teammate`, `dungeon.issue_pick_type`, `dungeon.issue_type_healing`, `dungeon.issue_type_mana`, `dungeon.issue_pick_teammate`, `dungeon.issue_stash_count`, `dungeon.issue_no_healing_candidates`, `dungeon.issue_no_mana_candidates`, `dungeon.issue_give_one`, `dungeon.issue_give_full`, `dungeon.issue_gave_healing`, `dungeon.issue_gave_mana`, `dungeon.issue_stash_now`).
- `Scripts/Core/Character.cs` -- New `AttunedShrineExpiresGameDay` (int) field for single-player shrine attunement. `HasActiveShrineAttunement` branches on `DoorMode.IsOnlineMode`: online uses existing UTC check, single-player checks `StoryProgressionSystem.CurrentGameDay <= AttunedShrineExpiresGameDay`. New `GetShrineTimeRemainingLabel()` helper returns mode-appropriate unit string ("12.5h" online, "2 days" / "1 day" / "expires today" single-player).
- `Scripts/Systems/SaveDataStructures.cs` -- `PlayerData.AttunedShrineExpiresGameDay` int field added.
- `Scripts/Systems/SaveSystem.cs` -- writes `AttunedShrineExpiresGameDay = player.AttunedShrineExpiresGameDay`.
- `Scripts/Core/GameEngine.cs` -- restores `player.AttunedShrineExpiresGameDay = playerData.AttunedShrineExpiresGameDay`.
- `Scripts/Locations/WildernessLocation.cs` -- attunement application sets BOTH `AttunedShrineExpiresUtc = UtcNow + AttunementHours` and `AttunedShrineExpiresGameDay = CurrentGameDay + ceil(AttunementHours/24)` so saves loaded under either mode have the right timer. Three display sites updated to call `GetShrineTimeRemainingLabel()` instead of computing hours-left from UTC inline.
- `Scripts/Locations/BaseLocation.cs` -- `/health` shrine display uses `GetShrineTimeRemainingLabel()`.
- `Localization/{en,hu,es,fr,it}.json` -- 5 new shrine-time keys per language (`shrine.time_hours`, `shrine.time_today`, `shrine.time_one_day`, `shrine.time_n_days`, `shrine.remaining_suffix`). Existing `wilderness.pilgrimage_active*` / `wilderness.bbs_pilgrimage_active` templates dropped their hardcoded "h" suffix so the helper's label supplies the unit.
