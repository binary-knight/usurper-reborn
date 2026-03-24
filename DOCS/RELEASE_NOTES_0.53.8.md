# Usurper Reborn v0.53.8 Release Notes

**Version Name:** Ancestral Spirits

## Companion Mana Potion Improvements

Two quality-of-life fixes for mana-using companions (Mira, spellcaster NPC teammates):

- **Companions now drink multiple mana potions per turn** — Previously, companions like Mira would drink one mana potion per combat round, wasting turns when they needed several potions to get back to casting. Now they drink as many as needed to fill up in a single turn.
- **[G] Give mana potion to teammate** — New option in the dungeon Potions menu (`[M]` from room menu). Select a mana-using teammate, then give them 1 potion or [F] fully restore. Only appears when you have mana potions and a teammate needs mana. Team status display now shows MP for caster teammates.

## Combat Loot Party Switching (by Xykier)

Players can now cycle through party members with `<`/`>` on the equipment drop screen to preview stats and equip loot to any party member. Contributed by Xykier in PR #77.

**Bug fixes on PR #77:**
- **Stale party index** — `_droppedEquipmentPartyIdx` was never reset between loot drops. Second drop could start on a dead teammate or wrong party member. Now resets to 0 (player) at the start of each drop.
- **Class restrictions not recalculated on switch** — `canPlayerUseItem` was computed once for the initial character but never updated when pressing `<`/`>`. A Warrior could be told "can't equip" a sword because the initial character was a Magician, or vice versa. Now recalculates on every character switch.
- **Solo guard** — `(</>) Change character` option and key handling now only shown when party has 2+ members.
- Removed unused `using System.Numerics` import.

## AoE Ability Skip List Fix (Critical)

The v0.53.7 AoE double-damage fix broke multiple AoE abilities by adding them to a skip list without verifying they had dedicated damage handlers. Abilities on the skip list had their base damage suppressed, but if they had no external handler, they dealt zero damage.

**Abilities broken in v0.53.7, now fixed:**
- **Whirlwind** (Warrior/Barbarian) — no dedicated handler, relies on base damage
- **Volley / Arrow Storm** (Ranger) — uses `"aoe"` effect whose handler was inside the skipped block
- **Chain Lightning** (Mystic Shaman) — no dedicated handler
- **Corrosive Cloud** (Alchemist) — no dedicated handler
- **Harmonic Crescendo** (Wavecaller) — no dedicated handler

The skip list now only contains effects with verified external damage loops: `crescendo_aoe`, `aoe_holy`, `fire`, `void_rupture`, `dissonant_wave`, `aoe_taunt`, `grand_finale_jester`, `execute_all`, `shaman_chain_lightning`.

## Harmonic Shield Party Fix

Harmonic Shield (Wavecaller ability) had two bugs:
- **Player didn't receive +40 DEF** — the comment said "handled" by the generic ability handler but it wasn't actually applied
- **Damage reflection only on caster** — teammates received the DEF bonus but not the `Reflecting` status effect

Now properly applies +40 DEF AND damage reflection to the entire party (caster + all living teammates) in both single-monster and multi-monster combat paths.

## Two-Handed Weapon Shield Loot Fix

Companions using two-handed weapons (Lyris with a bow) could auto-pick up shields from loot, which unequipped their two-handed weapon and left them weaponless. Shield/off-hand loot is now skipped for teammates who are two-handing.

## ATK/DEF Buff Overwrite Fix

The single-monster `ApplyAbilityEffects` path used `=` (overwrite) for attack and defense buffs instead of the "keep higher buff" pattern already used in the multi-monster path. A weaker buff (e.g., Battle Cry +40 ATK) would overwrite a stronger one (e.g., Berserker Rage +60 ATK). Now both paths use `Math.Max` — a weaker buff refreshes duration but never reduces the bonus.

## Barbarian Ability Audit

**Primal Scream differentiation**: Base damage increased from 45 to 90 (was nearly identical to Whirlwind at 40). Now adds 25% confusion chance on surviving targets for 2 rounds. Properly distinct from Whirlwind as a higher-level AoE with CC.

## Jester Ability Audit (5 bugs fixed)

- **Vicious Mockery** — Distraction penalty was flat -5 to hit regardless of level. Now scales: `5 + level/5 + CHA/10`. Companion miss chance scales from 27% to 60% (capped). Hardcoded English localized.
- **Charming Performance** — Description said "confuse" but effect is "charm" (different mechanic). Description corrected. Hardcoded English in single-monster path localized.
- **Deadly Joke** — Hardcoded English in single-monster path ("bewildered by the joke" / "doesn't get the joke") localized.
- **Grand Finale** — 60 base damage at level 72 was weaker than Whirlwind (40 at level 55) despite being a capstone. Buffed to 120 base with new dedicated `"grand_finale_jester"` handler for AoE diminishing damage + party inspire (+15 ATK for 2 rounds).
- **Carnival of Chaos** — Confusion effect was missing in single-monster combat path. AoE confusion now applied in both paths.

## Cleric Spell Audit (25 spells)

All 25 Cleric spells audited. Every spell had an inaccurate description (original v1.0 values never updated when spells were rebalanced) and hardcoded English combat messages. All 25 descriptions corrected to show actual base values and scaling. All 25 combat messages localized with `Loc.Get()`. 15 new localization keys added to all 5 languages.

Notable description corrections:
- Cure Light: 4-7 → 12-22 hp
- Cure Wounds: 15-25 → 25-40 hp
- Cure Critical: 40-55 → 50-75 hp
- Holy Smite: 35-50 → 45-65
- Armor of Faith: +25 → +28
- Divine Intervention: +80 → +85
- God's Finger: 300-400 → 320-450

## Assassin Ability Audit (8 bugs fixed)

- **Backstab** — `SpecialEffect` was `"critical"` (prints text only) instead of `"backstab"` (actual damage multiplier). Never dealt bonus damage despite "Guaranteed critical hit" description. Fixed: now uses `"backstab"` effect with 2x guaranteed crit damage in both combat paths.
- **Poison Blade** — Hardcoded English in single-monster path localized.
- **Shadow Step** — Description claimed CON scaling but DEF bonus was flat 50. Added CON scaling: `50 + CON/5`. Hardcoded English localized.
- **Death Mark** — Hardcoded English in single-monster path localized.
- **Assassinate** — Description said "below 15% HP" but code checks 25%. Hidden 50% success roll not documented. Description corrected. Hardcoded English in single-monster path localized. Single-monster path now applies full damage above threshold too (was doing nothing).
- **Vanish** — Description promised "next attack from stealth crits" but Hidden status had no crit mechanic. Added: Hidden status now guarantees auto-crit on next basic attack (consumed on use). CON scaling added: `80 + CON/4`. Hardcoded English localized.
- **Noctura's Embrace** — Description claimed CHA scaling but bonuses are flat. Description corrected.
- **Blade Dance** — 38 base damage at level 78 was weaker than Whirlwind (40 at level 55). Buffed to 110 base.
- **Death Blossom** — Multiple critical bugs: damage was SPLIT among targets (not per-target), was in skip list causing double-damage, description said 15% execute threshold but code uses 30%, single-monster path only worked below 30% (did nothing above). All fixed: per-target damage with diminishing, correct skip list, single-monster always applies damage, 5 hardcoded strings localized.

## Mystic Shaman Ability Audit (1 bug fixed)

- **Chain Lightning** — Primary target took double damage (base damage + handler both applied). Added to AoE skip list so only the chain handler deals damage. Hardcoded English chain message localized.

All other 11 Shaman abilities (4 enchants, 5 totems, Lightning Bolt, Ancestral Guidance) verified working correctly.

## Hidden Status Stealth Crit

New combat mechanic: the `StatusEffect.Hidden` status (granted by Vanish, Noctura's Embrace, Shadow abilities) now guarantees a critical hit on the next basic attack. The Hidden status is consumed when the attack lands. This makes stealth-based abilities genuinely rewarding — use Vanish, then follow up with a guaranteed crit next round.

## Magician Spell Audit (25 spells, 5 bugs fixed)

- **Spark / Lightning Bolt / Chain Lightning** — All three lightning damage spells had `SpecialEffect = "lightning"`, which shares a handler with the `"stun"` effect. Every cast auto-stunned the target (or ALL targets for Chain Lightning AoE) for free, making dedicated CC spells (Sleep, Web, Power Word: Stun) completely redundant. Removed the stun side-effect from damage-focused lightning spells. Spark description updated to remove "stuns" claim.
- **Arcane Immunity** — `SpecialEffect = "immunity"` had no handler in either the single-monster or PvP spell paths (only existed in the ability handler). The spell's status immunity never applied — players only got the protection AC bonus. Added `"immunity"` handler to both paths granting `HasStatusImmunity` for rest of combat.
- **Manwe's Creation** — `SpecialEffect = "creation"` had no handler anywhere. The effect was silently dropped. Added handler: reduces target defense by 30% and heals caster for 20% of damage dealt.
- **Pillar of Fire** — Description said "penetrates all armor" but used the generic `"fire"` effect (burn DoT only). Changed to new `"piercing_fire"` effect that deals bonus damage equal to target's armor value (compensating for defense subtraction) plus burn DoT.
- **Time Stop** — Double-buff bug. The spell set `AttackBonus`/`ProtectionBonus` (applied by `ApplySpellEffects`), then the `"timestop"` handler added ANOTHER +35 ATK/+35 DEF on top. Removed redundant handler bonuses; handler now only provides DodgeNextAttack.
- **Wish** — Double-buff bug. The spell set `AttackBonus = (100 + Level) * profMult` and scaled `ProtectionBonus` (applied by `ApplySpellEffects`), then the `"wish"` handler ALSO added Strength to ATK and Defense to DEF. At level 100 this effectively tripled stats instead of doubling. Removed spell result bonuses; handler's stat-doubling is now the sole source (matching "All stats doubled" description).

## Bard Ability Audit (3 bugs fixed)

- **Party Song buff overwrite** — `ApplyBardSongToParty` used `=` (assignment) for `TempAttackBonus`/`TempDefenseBonus` on teammates instead of `+=`. Any existing buff (e.g., from Bardic Inspiration +20 ATK) was overwritten by the song's 60% share (e.g., +15 ATK from Inspiring Tune). Changed to `+=` with `Math.Max` for duration. Affects all 5 party_song abilities: Inspiring Tune, Song of Rest, War Drummer's Cadence, Veloura's Serenade, Legend Incarnate.
- **Grand Finale teammate buff missing** — Single-monster combat path only buffed the player with +15 ATK inspire. Multi-monster path correctly buffed all teammates. Added teammate loop to single-monster path.
- **Cutting Words description** — Said "strips 25% of the target's defense" but actually applies -30% ATK and -20% DEF (the weaken effect). Description corrected.

## Quest System Overhaul (27 issues fixed)

Comprehensive audit and overhaul of the quest system addressing critical gameplay bugs, 15+ hardcoded English strings, design issues, and dead code.

### Critical Gameplay Bugs

- **FindArtifact quests impossible to complete** — `CreateRoyalAudienceQuest` still created `QuestTarget.FindArtifact` quests (removed in v0.52.11 with no tracking/completion code). Now creates ReachFloor quests instead.
- **Seduce quest auto-completes** — Legacy validation checked `player.IntimacyActs > 0` (lifetime counter), so any player who ever had intimacy auto-completed the quest. Now requires objective-based tracking.
- **Assassin quest auto-completes** — Same issue with `player.Assa > 0` lifetime counter. Fixed same way.
- **ClearBoss quest boss name mismatch** — Quest created synthetic boss names like "Zombie Champion" but actual dungeon bosses could be named differently. Kill tracking used `targetId = "zombie_champion"` which never matched the tier-based `"zombie"` reported by `OnMonsterKilled`. Now uses the base tier name as targetId.
- **NPC Bounty used wrong QuestTarget** — King bounties on NPCs used `QuestTarget.Assassin` instead of `QuestTarget.DefeatNPC`, falling back to the broken lifetime-stat validation. Changed to DefeatNPC with proper objective tracking.
- **Quest failure had zero consequences** — `ApplyQuestPenalty` calculated a penalty but never applied it. Penalty fields now enabled in `SetDefaultRewards`; failures are logged and announced via news system.
- **Legacy Monster validation unreachable** — Fallback path re-checked empty Objectives list, always returning false via a different dead code path. Simplified to return false directly.
- **DefeatNPC missing from GetTargetDescription** — After changing bounties to use DefeatNPC, the target description showed "Unknown Mission". Added `DefeatNPC => "Bounty Hunt"` case.
- **Royal Audience criminal/hunt quests used QuestTarget.Assassin** — Inconsistent with bounty tracking. Changed to DefeatNPC.

### Backward Compatibility

- **GetActiveBountyInitiator** now checks both `QuestTarget.DefeatNPC` and `QuestTarget.Assassin` so old save data with Assassin-type bounties is still recognized by the blood price system.
- **CompleteQuest bounty tracking** includes both DefeatNPC and Assassin for achievement/stat tracking on old saves.

### Localization (67 new keys × 5 languages)

- 8 bounty crime descriptions ("wanted for crimes against the Crown", "accused of treason", etc.)
- 5 generic bounty types with titles and descriptions ("Bandit Leader", "Escaped Prisoner", etc.)
- 11 starter quest titles and descriptions (Wolf Pack, Goblin Menace, Undead Rising, etc.)
- Quest reward type names (Experience, Gold, Potions, Darkness, Chivalry)
- Bounty comment templates, news announcements, failure news
- Quest Hall name/description, Royal Commission prefix, dungeon name
- `GetRewardDescription()` no longer shows raw enum names like "Money" — uses localized type names

### Design Fixes

- **Quest dedup by Title** — Was grouping by Comment field, which could hide valid quests with the same description. Now deduplicates by Title (more specific).
- **QuestObjective.Id collision** — Generated from `DateTime.Now` + 3-digit random, which collided when creating multiple objectives in the same second. Now uses `Guid`.
- **Bounty XP scaling** — Auto-complete bounty XP was `reward/10` regardless of player level. Now scales: `Math.Max(player.Level * 50, reward / 5)`.
- **Auto-complete bounty gold logging** — Added `DebugLogger` GOLD tracking for bounty rewards.

### Dead Code Removed

- `GetEquipmentQuests()`, `RefreshEquipmentQuests()`, `CreateEquipmentPurchaseQuest()` — ~150 lines of disabled equipment quest generation (broken with procedural shop inventory since v0.53.0).

## Team XP Redistribution on Teammate Death

When NPC teammates died during combat, their XP share was silently lost. The redistribution logic (`RedistributeDeadTeammateXP`) relied on iterating the teammates list to find dead members, but dead teammates were already **removed from the list** during combat. This caused slot misalignment — surviving teammates shifted to lower list positions but their XP allocations stayed in their original slots.

**Example**: Player(25%) NPC-A(25%) NPC-B(25%) NPC-C(25%) — NPC-B dies mid-combat → removed from list → at victory, only 2 teammates in list but 3 slots allocated → slot 3's 25% goes to nobody.

**Fix**: The redistribution now compares the count of living teammates against the count of allocated XP slots. When more slots have XP than there are living teammates, orphaned slots are detected and reclaimed. The reclaimed XP is distributed evenly among the player and surviving teammates.

## Combat Damage Clarity

Player feedback: the three-stage combat output ("attacks", "strikes for X", "hits for Y") was confusing because both "strikes" and "hits" showed damage numbers without explaining what changed between them. Now all damage paths show a `[X damage vs Y defense]` calculation line (matching the existing `[Roll: X + Y = Z vs AC W]` pattern for attack rolls), and the final damage message reads "X damage gets through your/their defenses!" instead of the ambiguous "hits for X damage!".

Applies to: regular attacks (both directions), monster special abilities (DirectDamage and DamageMultiplier), companion damage, and `ApplySingleMonsterDamage` (multi-monster player attacks/spells). The defense calc line is suppressed when defense is zero.

## Combat Status Display Fix

The multi-monster combat status bar showed `Status: {0}PWR(965)` — the `{0}` format placeholder was displayed literally because `Loc.Get("combat.status_label")` was called without the format argument. Added `combat.status_prefix` key ("Status: ") for the inline-display path.

## Monster Self-Heal Companion Damage Fix (6 abilities)

Six monster self-healing abilities were dealing phantom damage to companions. The abilities set `IsSelfOnly = true` and `SkipNormalAttack = true` but forgot to set `DamageMultiplier = 0` (default is 1.0). The `MonsterAttacksCompanion` DamageMultiplier path checked `DamageMultiplier > 0` and applied a full-power melee attack alongside the self-heal.

**Affected abilities**: Heal (Angel, Archangel), Phylactery (Lich), Phoenix Rebirth (Phoenix), Cocoon (Spider, Worm), SelfRepair (Golem, Construct), Sanctuary (Celestial).

## Troll Regeneration Scaling

Troll racial passive regeneration was effectively flat: `Math.Min(1 + level/20, 3)` — capped at 3 HP/round regardless of level. At level 50+ with thousands of HP, 3 HP/round was ~0.1% and completely irrelevant. Now scales at **2% of max HP per round** (minimum 3 HP). Drug CON bonus still stacks on top.

## Date Format Preference

New `[D]` option in preferences lets players choose their preferred date format:
- **MM/DD/YYYY** (US, default)
- **DD/MM/YYYY** (EU/international)
- **YYYY-MM-DD** (ISO)

Applied to all player-facing date displays: arena fight history, coronation/marriage dates, mail inbox and detail view, character creation date, achievement unlock dates, save timestamps, Inn event history, pantheon ascension date, and chat history. Pre-login screens (character slot selection) use ISO format as a universal fallback.

Date format is saved per-character and session-scoped for MUD mode (each concurrent player sees their own format).

## Preferences Menu Reorganization

The preferences menu was a flat list of 12+ options with no logical grouping. Now organized into 4 labeled categories:
- **Gameplay**: Combat Speed, Auto-Heal, Auto-Level, Auto-Equip
- **Display**: Color Theme, Compact Mode, Date Format, Terminal Font
- **Accessibility**: Screen Reader, Language
- **Character**: Skip Intimate Scenes, Orientation, Title

Same key bindings, same functionality — just easier to scan. Screen reader menu also reorganized with category labels.

## Loot Drop Party Message Fixes

When using the `<`/`>` party switching on loot drops, several messages incorrectly said "You/Your" instead of the selected party member's name:
- **Cursed item equip** — "You equip the item... and feel a dark presence" → now shows the character's name
- **Ability weapon requirement** — "Some of your abilities require a Bow" → "Some of Lyris's abilities require a Bow"
- **Spell weapon requirement** — "Your Magician spells require a Staff" → "Magician spells require a Staff"

## Companion Ability Message Fix

"Your aim is true!" (Precise Shot guaranteed hit) now shows the companion's name when used by a teammate (e.g., "Lyris's aim is true!"). The `combat.ranged_aim_true` key now takes a `{0}` name parameter.

## Wavecaller Resonance Cascade Fallthrough Fix

Resonance Cascade (Wavecaller ability) had no proper `case` label in single-monster combat — it fell through to `grand_finale_jester`, executing Grand Finale's code instead (wrong damage, wrong inspire buff, wrong message). The actual Resonance Cascade handler was in an orphaned code block after the `break` and was completely unreachable (this was the `CS0162: Unreachable code` build warning). Separated the cases so each ability has its own handler.

## Sage Spell Audit (25 spells, 4 bugs fixed)

- **Giant Form** — `SpecialEffect = "giant"` had no handler anywhere. The attack bonus was applied via `ApplySpellEffects` but the "giant" special effect was silently dropped. Added handler: grants DEF bonus (`20 + Level/3`) and temporary HP (`Level * 3`).
- **Soul Rend** — `SpecialEffect = "soul"` had no handler anywhere. Effect silently dropped. Added handler: reduces target defense by 25% and applies fear for 2 rounds.
- **Temporal Paradox** — `SpecialEffect = "temporal"` had no handler anywhere. Effect silently dropped. Added handler: stuns target for 2 rounds (trapped in time loop). Respects stun immunity.
- **Steal Life double-heal** — Set `result.Healing = result.Damage / 2` AND used `"drain"` special effect which also heals 50% of damage. In single-monster combat, `ApplySpellEffects` applied both healing sources, doubling the heal. Removed `result.Healing` — the `"drain"` handler is now the sole healing source.
- **Roast description** — Claimed "Hellfire pierces all armor" but used generic `"fire"` effect (burn DoT only). Description corrected to "Hellfire scorches the target."

All 3 new handlers added to multi-monster, single-monster, and PvP combat paths.

## Mystic Shaman Audit (3 bugs fixed)

- **Weapon enchant rounds never decrement in PvP** — `ShamanEnchantRounds--` only existed in `ProcessEndOfRoundAbilityEffects`, which was only called from the multi-monster loop. PvP combat (`PlayerVsPlayer`) never called it, so weapon enchants lasted forever in PvP fights. Added enchant round decrement for both combatants in the PvP end-of-round block.
- **Ancestral Guidance heals only player, not party** — Description says "25% of damage dealt is converted to healing for the party" but all 4 heal locations (2 per combat path) capped healing at `player.MaxHP - player.HP` and only healed the player. Extracted `ApplyAncestralGuidanceHealing` helper that heals the player AND all living teammates.
- **No HP per level growth** — Mystic Shaman was the only class with zero `BaseMaxHP` increase per level (every other class gets 5-13). Added +6 BaseMaxHP per level, consistent with other caster-hybrid classes.

## Wavecaller Spell Audit (1 bug fixed)

- **Symphony of the Depths permanent +999 ATK** — The capstone spell set `TempAttackBonus += 999` with `TempAttackBonusDuration = 2` (intended 2-round crit window), but also set `result.AttackBonus` with `result.Duration = 999`. `ApplySpellEffects` overwrote the duration to `Math.Max(2, 999) = 999`, making the +999 ATK permanent for the rest of combat. Replaced the +999 ATK hack with `StatusEffect.Hidden` (auto-crit on next attack), matching the description "guaranteed crit on next hit."

## Cyclebreaker Spell Audit (1 missing feature implemented)

- **Paradox Collapse fight damage bonus** — Description said "+10% of all damage dealt this fight" but the bonus was never implemented. Added `"paradox_collapse"` handler that reads `result.TotalDamageDealt` and applies 10% as bonus damage to the target. The longer the fight, the more devastating the capstone spell becomes.

## Abysswarden Spell Audit (1 structural bug fixed)

- **Attack spells with healing silently dropped in PvE** — The single-target attack spell path in multi-monster combat applied damage and special effects but never checked `spellResult.Healing`. Attack spells that set self-healing (Abysswarden's Prison Siphon heal 50%, Devour Essence heal 75%) had their heals silently dropped. Added healing check after damage application in the single-target attack spell path. This structural fix covers all current and future attack spells with lifesteal.

## Voidreaver Spell Audit (1 bug fixed)

- **Blood Pact permanent +999 ATK** — Identical pattern to the Wavecaller Symphony bug. `TempAttackBonus += 999` with 2-round duration was overwritten to permanent by `result.Duration = 999`. Replaced with `StatusEffect.Hidden` for a clean one-time guaranteed crit.

## Classes audited with no bugs found

- **Alchemist** (19 abilities) — All party effects use `+=` correctly, Potion Mastery consistently applied, armor pierce handled inline. Cleanest class in the codebase.
- **Ranger** (9 abilities) — All handlers present in both paths, weapon requirements correct, Hunter's Mark dual benefit (player buff + target debuff) working.
- **Warrior** (11 abilities) — All handlers present, Execute tiered damage correct, Thundering Roar AoE taunt works in both paths.
- **Tidesworn** (11 abilities + 5 spells) — All handlers present, every mechanic works as described (taunts, weakens, invulnerability, instant kill, lifesteal, party heals, mana restore).

---

## Files Changed

- `Scripts/Core/GameConfig.cs` — Version 0.53.8; `FormatDate()`, `FormatShortDate()`, `GetNewsDateFormat()` helpers; `DateFormat` session-scoped static property
- `Scripts/Core/Monster.cs` — `DistractedPenalty` property for scaled Vicious Mockery penalty
- `Scripts/Systems/CombatEngine.cs` — AoE skip list corrected; Harmonic Shield party fix; two-handed shield loot skip; ATK/DEF buff overwrite fix (single-monster path); Vicious Mockery scaled penalty; Charming Performance localized; Deadly Joke localized; Grand Finale Jester handler; Carnival of Chaos single-monster confusion; Backstab guaranteed 2x crit; Poison Blade localized; Shadow Step CON scaling + localized; Death Mark localized; Assassinate description + localized; Vanish CON scaling + stealth crit + localized; Death Blossom complete rewrite (per-target + skip list + localized); Chain Lightning skip list + localized; Hidden status auto-crit on basic attacks; Magician `"creation"` handler (defense reduction + lifetap); `"piercing_fire"` handler (armor bypass + burn); `"immunity"` handler in single-monster and PvP paths; Time Stop handler redundant +35 ATK/DEF removed; Wish handler redundant +35 ATK/DEF removed; Bard party song `+=` fix; Grand Finale single-monster teammate buff; Resonance Cascade fallthrough fix; Sage `"giant"`, `"soul"`, `"temporal"` handlers added (all 3 paths); Shaman enchant decrement in PvP; `ApplyAncestralGuidanceHealing` party heal helper; Cyclebreaker `"paradox_collapse"` handler; Abysswarden attack spell healing fix in single-target path; Wavecaller Symphony +999 ATK → Hidden status; Voidreaver Blood Pact +999 ATK → Hidden status; companion `TeammateUseManaPotion` drinks multiple potions per turn; PR #77 loot party switching: index reset + class restriction recalc on switch + solo guard; `RedistributeDeadTeammateXP` rewrite to detect orphaned XP slots from removed dead teammates; `[X damage vs Y defense]` calc line in all damage paths (regular attacks, special abilities, companion damage); `combat.status_prefix` key for inline status display; Troll regen 2% MaxHP/round
- `Scripts/Systems/MonsterAbilities.cs` — `DamageMultiplier = 0` added to 6 self-heal abilities (Heal, Phylactery, PhoenixRebirth, Cocoon, SelfRepair, Sanctuary) that were dealing phantom damage to companions
- `Scripts/Systems/ClassAbilitySystem.cs` — Primal Scream base 45→90 + confusion; Grand Finale base 60→120 + new effect; Blade Dance base 38→110; Death Blossom description corrected; Assassinate description corrected; Vanish description updated; Noctura's Embrace description corrected; Backstab effect "critical"→"backstab"; Charming Performance description corrected; Cutting Words description corrected
- `Scripts/Systems/SpellSystem.cs` — All 25 Cleric spell descriptions corrected; all 25 combat messages localized; Magician Spark/Lightning Bolt/Chain Lightning `"lightning"` SpecialEffect removed; Pillar of Fire effect `"fire"`→`"piercing_fire"`; Wish spell result bonuses removed (handler-only); Spark description updated; Sage Steal Life `result.Healing` removed (drain handler is sole source); Roast description corrected; Wavecaller Symphony `TempAttackBonus += 999` → `StatusEffect.Hidden`; Cyclebreaker Paradox Collapse `"paradox_collapse"` effect added; Voidreaver Blood Pact `TempAttackBonus += 999` → `StatusEffect.Hidden`
- `Scripts/Locations/LevelMasterLocation.cs` — Mystic Shaman +6 BaseMaxHP per level
- `Scripts/Locations/DungeonLocation.cs` — [G] Give mana potion to teammate in potions menu; team status shows MP for casters
- `Scripts/Systems/QuestSystem.cs` — Quest system overhaul: FindArtifact→ReachFloor in Royal Audience; Seduce/Assassin legacy validation returns false; quest failure penalties enabled; ClearBoss uses tier name as targetId; NPC bounties use DefeatNPC; bounty crime/type/comment/news localized; starter quest titles/descriptions localized; dungeon objective descriptions localized; equipment quest dead code removed; dedup by Title; backward compat for Assassin bounties; bounty XP scaling; gold logging; dungeon name localized; Royal Audience criminal→DefeatNPC
- `Scripts/Core/Quest.cs` — `GetRewardDescription()` localized reward type names; `GetTargetDescription()` DefeatNPC case added; `QuestObjective.Id` uses Guid
- `Scripts/Locations/QuestHallLocation.cs` — Localized name and description
- `Scripts/Locations/BaseLocation.cs` — Preferences menu reorganized into 4 categories; `[D]` date format toggle; `FormatShortDate` for mail inbox; `FormatDate` for mail detail; `combat.status_prefix` fix
- `Scripts/Locations/MainStreetLocation.cs` — Character creation date, achievement unlock dates, save timestamps use `FormatDate`
- `Scripts/Locations/InnLocation.cs` — Event history uses `FormatShortDate`
- `Scripts/Locations/ArenaLocation.cs` — Fight history uses `FormatDate`
- `Scripts/Locations/PantheonLocation.cs` — Ascension date uses `FormatDate`
- `Scripts/Core/Character.cs` — `DateFormatPreference` property
- `Scripts/Core/GameEngine.cs` — `DateFormatPreference` save/load; `GameConfig.DateFormat` set on login
- `Scripts/Server/SessionContext.cs` — `DateFormat` per-session property
- `Scripts/Systems/SaveDataStructures.cs` — `DateFormatPreference` field
- `Scripts/Systems/SaveSystem.cs` — `DateFormatPreference` serialization
- `Scripts/Systems/MailSystem.cs` — Mail detail date uses `FormatDate`
- `Scripts/Systems/OnlineChatSystem.cs` — Chat history uses `FormatShortDate`
- `Tests/QuestSystemTests.cs` — Updated ClearBoss test assertion for localized title
- `Localization/en.json` — 20+ combat keys + 67 quest system keys + `combat.status_prefix` + `quest.target.defeat_npc` + `quest.dungeon_name` + `quest.wanted_criminal` + combat damage clarity keys (`combat.monster_hits`, `combat.target_damage`, `combat.monster_hits_companion` reworded)
- `Localization/es.json` — All new keys translated + combat damage clarity
- `Localization/hu.json` — All new keys translated + combat damage clarity
- `Localization/it.json` — All new keys translated + combat damage clarity
- `Localization/fr.json` — All new keys translated + combat damage clarity
