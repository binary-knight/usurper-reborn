# Usurper Reborn v0.52.12 Release Notes

**Release Date:** March 17, 2026
**Version Name:** The Hook

## Online Mode Daily Reset Banner Fix

The "ENDLESS ADVENTURE CONTINUES!" banner with "Endless mode: Time flows differently here..." was displaying to online multiplayer players on every daily reset. This was a single-player feature leaking into online mode — the default `DailyCycleMode` is `Endless`, and the guard that was supposed to prevent Endless resets (`!DoorMode.IsOnlineMode && ...`) only blocked them in single-player, allowing online mode to fall through and show the full Endless reset sequence including the banner and `ProcessEndlessReset()`.

Online mode now performs silent daily resets — counter refreshes and maintenance run without any banner or mode-specific processing. The MUD server's world sim handles world-level resets independently.

---

## Home Menu Label Fix

Shortened "Master Craftsman's Renovations" to just "Renovations" in all languages. The long label was breaking the fixed-width 3-column menu grid at Home, causing column misalignment.

---

## Companion HP Equipment Bug Fix

Companions were starting with less than full HP on dungeon entry, login/logout, and after healing. `CompanionSystem` used `companion.BaseStats.HP` (base HP without equipment) everywhere instead of computing the actual MaxHP with Constitution bonuses from gear. Added `GetCompanionMaxHP()` helper that builds a temporary Character wrapper with equipment and calls `RecalculateStats()` to get the true MaxHP. Fixed 7 call sites in CompanionSystem plus display bugs in Inn and Dungeon companion HP readouts.

---

## Multi-Target Spell Target Prompt Fix

Wavecaller's Restorative Tide (party heal) and Tidecall Barrier (party AC buff) incorrectly prompted for a single target despite being `IsMultiTarget` spells that affect the entire party. The quickbar spell handler now checks `IsMultiTarget` and skips the target selection prompt for area buff/heal spells.

---

## Monster Ability Display Fix

When monsters used abilities like CriticalStrike against companions, the damage message showed the raw enum name ("takes 574 damage from CriticalStrike!") instead of clean text. The ability's descriptive message (e.g. "lands a critical strike!") already displays above the damage line, so the damage message now just says "takes X damage!" without the redundant raw ability name.

---

## Group Combat Victory Markup Fix

Group dungeon followers saw raw markup tags in victory messages (e.g. `[bright_green]Triple kill![/]` instead of colored text). The victory messages had embedded markup that rendered on the leader's terminal but passed through as literal text when broadcast to followers via ANSI. Removed embedded markup from `CombatMessages.GetVictoryMessage()` — callers already handle coloring.

---

## Group Loot Distribution Overhaul

When a player passed on loot in group dungeons, the item was offered to other human players first (with a 30-second timeout each), then NPCs. This caused the leader to sit waiting for timeouts even when the follower had already moved on. Reversed the priority: NPC/companion auto-pickup now happens first (instant evaluation), and only if no NPC wants the item does it get offered to other human players. Cascade offer timeout reduced from 30 seconds to 10 seconds.

---

## Team System Bug Fixes

Team Corner audit: `SackMember()` and `ChangeTeamPassword()` were missing `SaveAllSharedState()` calls, so NPC team removal and password changes would revert on world-sim reload in online mode. Removed unreachable duplicate case "!" (Resurrect) in ProcessChoice. All `new Random()` replaced with `Random.Shared` across TeamCornerLocation and TeamSystem (5 instances).

---

## Group Reward Fairness Fix

Group dungeon followers were missing several XP multipliers that the leader received — Blood Moon, Child XP bonus, Study/Library, Settlement Tavern/Library, Guild XP bonus, and HQ Training bonus were all skipped in the follower XP calculation path. Followers now receive the same set of multipliers as the leader, calculated independently per player. Gold distribution also fixed: was splitting raw base gold among players while the leader kept fully-multiplied gold, causing the leader to retain more than their fair share.

---

## Old God Boss Rebalance

All seven Old God boss fights rebalanced to be genuinely challenging endgame encounters. Player damage scales multiplicatively through levels (STR growth + weapon power + abilities + crits), but boss HP/STR were scaling linearly — by level 85+, bosses melted in under 10 rounds with no real threat to survival.

**HP increases** (matched to player DPS at each tier):
- Maelketh (Fl.25): 15,000 → 25,000
- Veloura (Fl.40): 25,000 → 50,000
- Thorgrim (Fl.55): 40,000 → 100,000
- Noctura (Fl.70): 50,000 → 150,000
- Aurelion (Fl.85): 75,000 → 250,000
- Terravok (Fl.95): 100,000 → 350,000
- Manwe (Fl.100): 150,000 → 500,000

**STR increases** (boss attacks should threaten player HP pools):
- Maelketh: 120→150, Veloura: 90→140, Thorgrim: 140→200, Noctura: 130→220, Aurelion: 160→280, Terravok: 200→350, Manwe: 220→400

**Attacks/round**: Aurelion 2→3, Terravok 1→2

**Enrage timers tightened** 15-20% across all bosses (Maelketh 30→25, Veloura 28→22, Thorgrim 25→20, Noctura 22→18, Aurelion 20→16, Terravok 18→14, Manwe 15→12). Enrage damage multiplier increased 2.0x→2.5x with +3 extra attacks (was +2).

**AoE/Channel damage doubled** to match player HP pools at each tier — Veloura AoE 150→300, Thorgrim channel 600→1200, Noctura channel 800→1800, Aurelion channel 1000→2500, Terravok channel 1200→3000, Manwe channel 1500→4000.

**Corruption damage per stack** increased: Noctura 20→35, Aurelion 25→45, Terravok 30→55, Manwe 40→70.

---

## Old God Boss System Hardening

Four fixes to the Old God boss encounter system addressing crash risks, state corruption, and story progression edge cases.

**Divine armor null reference fix** — `GetDivineArmorReduction()` checked if the player's weapon was null but not if `weapon.Name` was null before calling `.Contains()`. Now uses `weapon?.Name == null` guard to prevent crash with unnamed weapons.

**Combat modifier cleanup fix** — `ClearPlayerModifiers()` only cleared `HasBloodlust` and `DodgeNextAttack` after boss fights but left `TempAttackBonus`/`TempDefenseBonus` (set with duration 999) intact. If a player fought multiple bosses in one session, dialogue-granted bonuses from the first boss would carry over. Now clears all four temp bonus fields.

**MUD mode concurrent boss encounter fix** — `OldGodBossSystem` is a singleton with class-level `currentBoss`, `bossDefeated`, `dungeonTeammates`, and `activeCombatModifiers` fields. If two MUD players fought Old God bosses simultaneously, they would overwrite each other's state. `StartBossEncounter()` now serialized with `SemaphoreSlim` to prevent concurrent corruption.

**Awakened state recovery for non-saveable gods** — If a non-saveable god (Maelketh, Thorgrim) somehow reached `Awakened` status (no artifact exists to complete the save quest), `CanEncounterBoss()` would return false permanently, blocking floor progression. Now auto-recovers by setting status to `Defeated` when a non-saveable god is found in `Awakened` state.

---

## Old God State Deserialization Safety

`SaveSystem` deserialization of `OldGodStates` had two issues: it would overwrite meaningful initial god states (Maelketh starts as `Corrupted`, Veloura as `Dying`) with `Unknown` (0) if the save data contained default values, and it silently discarded states for god types not found in the initialized dictionary. Now skips `Unknown` status during restore and logs a warning for unrecognized god types.

---

## Files Changed

- `Scripts/Core/GameConfig.cs` — Version 0.52.12; boss enrage damage 2.0x→2.5x; enrage extra attacks 2→3
- `Scripts/Data/OldGodsData.cs` — All 7 Old God boss stats rebalanced (HP, STR, DEF, AGI, WIS, AttacksPerRound)
- `Scripts/Systems/OldGodBossSystem.cs` — All boss party mechanics retuned (enrage timers, AoE damage, channel damage, corruption per stack); `SemaphoreSlim` lock on `StartBossEncounter()`; `ClearPlayerModifiers()` clears all temp bonuses; `GetDivineArmorReduction()` null-safe weapon name check; `CanEncounterBoss()` auto-recovers non-saveable gods stuck in Awakened state
- `Scripts/Systems/SaveSystem.cs` — OldGodStates deserialization skips `Unknown` status; logs warning for unrecognized god types
- `Scripts/Systems/DailySystemManager.cs` — Online mode daily reset skips display banner and mode-specific processing; single-player path unchanged
- `Scripts/Systems/CompanionSystem.cs` — `GetCompanionMaxHP()` helper; fixed 7 `BaseStats.HP` references in `GetCompanionsAsCharacters()`, `DamageCompanion()`, `HealCompanion()`, `GetCompanionHP()`, `RestoreCompanionHP()`, and level-up
- `Scripts/Systems/CombatEngine.cs` — Multi-target spell skip target prompt; monster ability display fix; group loot NPC-first priority; cascade timeout 30s→10s; group follower XP multiplier parity (Blood Moon, Child, Study, Settlement, Guild, HQ Training); gold distribution uses post-multiplier amount
- `Scripts/Systems/CombatMessages.cs` — Removed markup tags from `GetVictoryMessage()`
- `Scripts/Systems/TeamSystem.cs` — `new Random()` → `Random.Shared` (4 instances)
- `Scripts/Locations/TeamCornerLocation.cs` — `SaveAllSharedState()` after SackMember and ChangeTeamPassword; removed dead duplicate case "!"; `new Random()` → `Random.Shared`
- `Scripts/Locations/InnLocation.cs` — Companion summary uses `GetCompanionMaxHP()` for display
- `Scripts/Locations/DungeonLocation.cs` — Party HP readout uses `GetCompanionMaxHP()`
- `Localization/en.json` — `home.upgrades` shortened to "Renovations"
- `Localization/es.json` — `home.upgrades` shortened to "Renovaciones"
- `Localization/it.json` — `home.upgrades` shortened to "Ristrutturazioni"
- `Localization/hu.json` — `home.upgrades` unchanged (already short: "Felújítások")
