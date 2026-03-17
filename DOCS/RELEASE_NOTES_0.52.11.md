# Usurper Reborn v0.52.11 Release Notes

**Release Date:** March 16, 2026
**Version Name:** The Hook

## Guild Tag in /who

The `/who` command now shows each player's guild affiliation after their name. Guild names appear in dark cyan `<brackets>` between the player's title and their location. A lightweight display name cache avoids expensive database queries per player in the who list.

## Weekly Rival System Fix

The weekly rival assignment was only considering players who happened to be online at the time of the Monday update. A level 13 player could be assigned a level 51 rival simply because no one closer in level was logged in.

Rivals are now assigned from ALL players in the database, finding the closest match by level regardless of online status. The weekly rank is also now computed from the actual database (count of players with higher level) instead of the `101 - level` estimate.

Two new `SqlSaveBackend` methods support this:
- `GetPlayerRank()` — returns the player's 1-based rank among all players by level
- `GetClosestPlayerByLevel()` — finds the nearest player by level, excluding the current player, banned players, and emergency saves

## Localization Pass

Four areas that were still showing hardcoded English have been converted to use the `Loc.Get()` localization system:

- **Seven Seals**: All 7 seal names, titles, location hints, and full lore text (170+ keys) now localize. Lore lines are loaded dynamically via `GetLocalizedLore()` helper.
- **Quests**: Quest difficulty labels, target descriptions, reward descriptions, status text, and objective progress strings all localized. Starter quest titles and bounty descriptions converted. ~76 new keys.
- **NPC City Stories**: The 6 memorable NPCs (Marcus, Elena, Bartholomew, Greta, Pip, Ezra) — all dialogue, choice prompts, titles, descriptions, and notifications now resolve through localization helpers with English fallback. ~125 keys.
- **NPC Petitions**: All 6 petition types (betrayal, romance/matchmaker, custody dispute, royal petition, dying wish, rivalry warning) fully localized with ~176 keys covering headers, dialogue, menu options, and response text.

All new keys translated into Spanish, Hungarian, and Italian.

## Item Effect Key Fix

Equipment special effect suffixes (e.g., "+10% Max HP") were displaying as raw localization keys like `item.effect.max_h_p.suffix` because the PascalCase-to-snake_case converter was splitting acronyms letter-by-letter (`MaxHP` → `max_h_p` instead of `max_hp`). Fixed with a two-pass regex that handles both regular PascalCase and uppercase acronyms correctly.

## Inventory Color Consistency

Weapons in the backpack inventory were displayed in bright red, which felt alarming and inconsistent with how the same items appeared in companion equipment screens (white). Weapon color changed to bright yellow to match the gold/yellow theme used for weapons elsewhere. The duplicate `GetRarityColor()` method in InventorySystem was also corrected to match the canonical rarity colors (Rare: cyan, Artifact: bright yellow).

## Color Theme Instant Apply Fix

Changing the color theme in Preferences now takes effect immediately when returning to the location. Previously in MUD streaming mode, the location banner/menu would not redraw after exiting preferences because the `_locationEntryDisplayed` flag stayed true — the new theme colors only appeared after logging out and back in. Now exiting preferences forces a full location redraw. This also fixes the same delayed-update issue for language changes and compact mode toggles.

## CK-Style Parenting System

Parents can now actively shape their children's development through scenario-based interactions at Home. Select `[C] Spend Time with Child` to be presented with an age-appropriate moral dilemma — a toddler crying at night, a child caught stealing, a teenager questioning the gods. Each scenario offers 2-3 responses that affect the child's Soul alignment differently.

**24 unique scenarios** across three age groups:
- **Toddler (ages 0-4)**: 8 scenarios covering sharing, fears, creativity, trust, and tantrums
- **Child (ages 5-12)**: 8 scenarios covering theft, bullying, honesty, compassion, death, and responsibility
- **Teen (ages 13-17)**: 8 scenarios covering career choices, romance, faith, corruption, independence, and inheritance

**Parent alignment influence**: Your own Chivalry/Darkness modifies the lesson's effectiveness. A virtuous parent teaching kindness gets a bonus; a dark parent teaching virtue is less convincing. Conversely, a dark parent encouraging cruelty amplifies the negative impact.

**One interaction per child per day** prevents grinding — children develop gradually over time, not in a single session.

## Birth Alignment Inheritance

Children now inherit alignment tendencies from their parents at birth. A Paladin with high Chivalry married to a virtuous spouse will tend to have children who start with positive Soul values, while parents steeped in Darkness will produce children who lean toward mischief. A random variance of ±20 ensures no two children are identical even from the same parents.

## Soul Range Fix

The Soul label system was asymmetric — a child born at Soul 0 (the default) was labeled "bad kid" even though they had done nothing wrong. The ranges have been rebalanced:

| Soul Range | Old Label | New Label |
|---|---|---|
| -500 to -250 | evil | evil |
| -249 to -100 | naughty | naughty |
| -99 to -25 | (was "bad kid" at -99..0) | mischievous |
| -24 to 24 | (was split across "bad kid" and "normal") | normal |
| 25 to 100 | (was "normal" at 1..100) | well-behaved |
| 101 to 250 | well-behaved | virtuous |
| 251 to 500 | angel-heart | angel-heart |

## Quest System Cleanup

Removed two broken dungeon quest types that were impossible to complete:

- **FindArtifact quests** — `OnArtifactFound()` was defined but never called from any code path. Players who accepted these quests could never complete the "retrieve artifact" objective. Removed from the bounty board quest pool.
- **RescueNPC quests** — Used fake NPC names ("Lady Elara", "Sir Marcus") that don't exist in the game world. The TalkToNPC objective had no legitimate completion path. Removed from the bounty board quest pool.

Dead code removed: `OnArtifactFound()`, `OnNPCInteraction()` (duplicate of the working `OnNPCTalkedTo`), `OnLocationVisited()` (no quests ever generated VisitLocation objectives).

Remaining quest types all verified working: kill quests (3 combat paths), dungeon floor quests (5+ trigger sites), equipment purchase quests (5 shop locations), king bounties (DefeatNPC), expedition quests, and floor clear quests.

## Weapon Loot Elemental Theming

Weapon elemental enchantment effects (Fire, Ice, Lightning, Poison, Holy, Shadow) are now weighted by class affinity instead of being purely random. An Alchemist's Blade is 3x more likely to roll Poison or Fire than Ice or Lightning. Non-thematic effects can still appear, just less often.

| Class | Favored Elements |
|---|---|
| Alchemist | Poison, Fire |
| Paladin, Cleric | Holy, Fire |
| Assassin | Poison, Shadow |
| Magician, Sage | Fire, Ice, Lightning |
| Ranger | Poison, Ice |
| Bard, Jester | Shadow, Lightning |
| Warrior, Barbarian | No preference (all equal) |
| "All" class weapons | No preference (all equal) |

## Ranger's Cloak Body Armor Rename

The body armor template "Ranger's Cloak" was renamed to "Ranger's Leather" to avoid confusion with the actual cloak-slot item of the same name.

## Random.Shared Fix

All `new Random()` calls in `Child.cs`, `FamilySystem.cs`, and `Quest.cs` replaced with `Random.Shared`. Multiple `new Random()` calls in quick succession could produce identical sequences due to seed collision; `Random.Shared` is thread-safe and properly seeded.

## Church Marriage Lover Fix

Players whose lovers appeared in the RomanceTracker system but hadn't reached Love level in the separate RelationshipSystem were unable to marry at the church. The church marriage candidate list now checks both systems — if an NPC is a current lover via RomanceTracker, they appear as eligible regardless of RelationshipSystem level. The RelationshipSystem is also synced to Love level before the marriage ceremony so validation passes.

## Unicode Arrow Fix for BBS Terminals

The `->` arrow character was displaying as `?` on BBS terminals using CP437 encoding. Replaced all `→` characters in localization files across all 4 languages with ASCII `->`.

## Paladin Class Improvements

- **Aura of Protection WIS scaling**: Paladin defense abilities now scale with Wisdom in addition to Constitution, reflecting the holy warrior's faith-based fighting style. Cooldown reduced from 5 to 4.
- **Judgment Day damage buff**: Base damage increased from 60 to 75 to better reflect its capstone status.
- **Divine Resolve passive**: New Paladin class passive — +10% damage vs undead/demons, 15% chance to resist negative status effects. Displayed in `/health` Active Buffs.

## DoT Level Scaling

Status effect damage over time (Bleed, Burn, Frozen, Cursed, Diseased) was dealing flat damage with no level scaling — 1-8 damage per tick regardless of level, which became completely trivial against 1000+ HP at high levels. All DoTs now scale with character level:

| Effect | Old Damage | New Damage (Level 50) | New Damage (Level 100) |
|---|---|---|---|
| Bleed | 1-6 flat | 11-16 | 21-26 |
| Burn | 2-8 flat | 14-20 | 27-33 |
| Frozen | 1-3 flat | 7-9 | 13-15 |
| Cursed | 1-2 flat | 6-7 | 11-12 |
| Diseased | 1 flat | 4 | 7 |

## Relationship System Audit & Fixes

Comprehensive audit and fix pass across all romance/relationship subsystems (RomanceTracker, RelationshipSystem, IntimacySystem, VisualNovelDialogueSystem). 13 bugs fixed:

- **Jealousy divorce desync**: When an NPC's jealousy reached 90 (infidelity), the RomanceTracker removed the spouse record but never called `RelationshipSystem.ProcessDivorce()`, leaving the NPC stuck as "Married" in the relationship system. Now calls the full divorce flow across both systems and clears player marriage flags.
- **PersonalQuestionsAsked never incremented**: The dialogue system tracked `PersonalQuestionsAsked` for depth progression but never incremented it, so players saw the same shallow dialogue every time. Now increments at the start of each personal conversation.
- **AddSpouse silent duplicate exit**: If `AddSpouse()` was called for an NPC already in the spouse list (e.g., after a church remarriage), it silently returned without updating the marriage date or name. Now updates the existing record's metadata.
- **Spouse flirt counts double**: Spouse/lover flirt interactions incremented `flirtCountThisSession` in both the branch handler and the caller, doubling the count and causing cooldown to trigger after half the expected interactions. Removed the duplicate increment.
- **Affair pregnancy father lost on save/reload**: The `_pregnancyFathers` dictionary in WorldSimulator tracked which NPC fathered an affair pregnancy, but it was never serialized — reloading the game lost the father attribution, assigning all affair children to the mother's spouse. Replaced with a `PregnancyFatherName` property on the NPC class that serializes with NPC data. Also preserved during world-sim reload cycles.
- **Daily relationship cap bypassed**: `DifficultySystem.ApplyRelationshipMultiplier()` multiplied intimacy step counts before the RelationshipSystem's daily cap check, allowing intimacy to blow past the 2-step/day limit. Removed the difficulty multiplier from intimacy paths entirely.
- **Asymmetric divorce penalty**: When `RelationshipSystem.ProcessDivorce()` was called, the initiator got reset to Normal (70) while the other party got set to Hate (110). Both parties should be hurt — changed initiator to Anger (90) for a more balanced outcome.
- **Fade-to-black grants too much relationship**: The "implied" intimacy path (fade-to-black) granted 3 relationship steps — the same as a moderately successful full scene. Reduced to 2 steps to differentiate from the more involved path.
- **Asymmetric intimacy progression**: Intimacy only progressed the NPC→player direction with `overrideMaxFeeling = true`, while player→NPC was capped at Friendship. Both directions now use `overrideMaxFeeling = true` so intimacy can progress relationships past Friendship in both directions.
- **Charisma romance scaling too strong**: CHA modifier applied a 1.5x multiplier to both flirt receptiveness and confession success, making high-CHA characters trivially succeed at romance. Removed the 1.5x multiplier (CHA still contributes its base percentage bonus).
- **Castle divorce doesn't sync RelationshipSystem**: Royal divorces via the Castle throne only called `RomanceTracker.Divorce()` without also calling `RelationshipSystem.ProcessDivorce()`, leaving the relationship level stuck at Love/Married. Now calls both systems.

### Second Pass (5 additional fixes)

- **LoveCorner divorce desync**: Love Corner's `HandleDivorce` only cleared `player.IsMarried` and `player.SpouseName` — never called `RelationshipSystem.ProcessDivorce()` or `RomanceTracker.Divorce()`, leaving ghost marriage records in both systems. Now performs full sync including clearing the NPC's marriage state.
- **LoveCorner marriage not tracked**: The fallback marriage path in `HandleMarry` never registered the spouse with `RomanceTracker.AddSpouse()`, so the marriage existed in `Character.IsMarried` but not in the romance tracker. Also replaced `new Random()` with `Random.Shared`.
- **Dead NPC intimate scenes**: `IntimacySystem` had no dead-NPC guards — players could initiate intimate scenes with dead NPCs. Added `IsDead` checks to all 4 public entry points (`StartIntimateScene`, `InitiateIntimateScene`, `ApplyIntimacyBenefitsOnly`, `StartGroupScene`).
- **Dead spouse at Home**: `SpendTimeWithSpouse` in HomeLocation didn't filter dead NPCs from the partner selection list, allowing interaction with dead spouses. Added `npc.IsDead` filter.
- **Divorce clears affair pregnancy**: When NPCs divorced, pregnancy was cleared unconditionally — even if the pregnancy came from an affair (different father). Now checks `PregnancyFatherName`: if set, the pregnancy came from someone other than the divorcing spouse and is preserved. Also clears `PregnancyDueDate` and `PregnancyFatherName` on both natural death and permadeath paths.

### Third Pass (5 additional fixes)

- **Daily relationship cap bypass via name order**: The daily cap key used `"{name1}_{name2}_{date}"` — since name order is caller-dependent, swapping directions created a different key, allowing double the daily limit. Now normalizes key order alphabetically.
- **Dead spouse returns "Ex" instead of "Spouse"**: `RomanceTracker.GetRelationType()` checked `Exes` but not `ExSpouses`, so a widowed player's dead spouse (moved to ExSpouses on death) returned `None` instead of `Ex`. Added `ExSpouses` check.
- **Jealousy at 90 never decays**: The jealousy decay check was `if (jealousy > 0 && jealousy < 90)`, meaning an NPC stuck at exactly 90 jealousy would never decay — permanently angry. Changed to `if (jealousy > 0)`. Also replaced `new Random()` with `Random.Shared`.
- **ConfessionAccepted never set**: The `HasConfessed` and `ConfessionAccepted` flags on `ConversationState` were serialized but never written — confessions couldn't progress the relationship pipeline properly. Now sets `HasConfessed = true` after any confession attempt and `ConfessionAccepted = true` on success.
- **PregnancyFatherName not cleared on permadeath**: The permadeath path in `WorldSimulator.MarkNPCDead()` cleared HP and set `IsDead` but left pregnancy data intact, meaning dead NPCs could still "give birth" during world sim ticks. Now clears both `PregnancyDueDate` and `PregnancyFatherName` on permadeath.

## Armor Damage Variance Tightened

Armor power (ArmPow) damage reduction was wildly inconsistent — each hit randomly applied 0-100% of your armor value, meaning identical attacks could deal vastly different damage. Tightened to 75-100% of armor value, making defense feel more reliable without being completely deterministic.

## Ability Scaling Rebalance

Class abilities were dealing 3-5x less damage than equivalent-level spells at endgame due to lower stat scaling rates and a restrictive hard cap. Rebalanced:

- **Attack ability stat scaling**: Primary stat contribution increased from 3% to 4% per point (spells scale at 5% per INT point). Secondary stat (DEX) contribution also increased.
- **Healing ability scaling**: CON contribution increased from 2.5% to 3% per point, WIS from 1.5% to 2%.
- **Defense ability scaling**: CON contribution increased from 2% to 2.5%.
- **Buff/debuff scaling**: CHA and INT contributions increased slightly.
- **Stat scale cap**: Raised from 5.0x to 6.0x (spells cap at 8.0x but cost mana, so abilities should be slightly behind).

At Level 100 with STR 50, a Warrior ability now deals ~1,350 damage (was ~1,008) vs a Magician spell dealing ~5,280. The gap narrows from 5.2x to 3.9x — spells still win (they cost mana) but abilities are now competitive.

---

## Files Changed

- `Scripts/Core/GameConfig.cs` — Version 0.52.11; parenting system constants; Paladin Divine Resolve passive constants (`PaladinDivineResolveDamageBonus`, `PaladinDivineResolveStatusResist`)
- `Scripts/Server/MudChatSystem.cs` — Guild tag display in `/who` command output; looks up guild display name per player via `GuildSystem.GetPlayerGuildDisplayName()`
- `Scripts/Systems/GuildSystem.cs` — `guildDisplayNameCache` ConcurrentDictionary; `GetPlayerGuildDisplayName()` method; `LoadMembershipCache()` populates display name cache; `CreateGuild()` updates display name cache
- `Scripts/Systems/SqlSaveBackend.cs` — `GetPlayerRank()` returns actual rank among all players by level; `GetClosestPlayerByLevel()` finds closest player by level from full database
- `Scripts/Systems/DailySystemManager.cs` — `UpdateWeeklyRankings()` queries database for real rank and rival instead of iterating online sessions only
- `Scripts/Systems/SevenSealsSystem.cs` — `GetLocalizedLore()` helper; all 7 seal definitions use `Loc.Get()` for name, title, location, hint, lore text
- `Scripts/Core/Quest.cs` — `GetDifficultyString()`, `GetTargetDescription()`, `GetRewardDescription()`, `GetDisplayInfo()`, `QuestObjective.GetDisplayString()` all localized; removed FindArtifact and RescueNPC from `GetTargetDescription()`; `new Random()` → `Random.Shared` in QuestObjective constructors
- `Scripts/Systems/QuestSystem.cs` — Starter quest titles, bounty titles, objective descriptions, initiator names converted to `Loc.Get()`; removed FindArtifact and RescueNPC from bounty board quest pool; removed dead methods `OnArtifactFound()`, `OnNPCInteraction()`, `OnLocationVisited()`; removed RescueNPC auto-complete logic from `OnDungeonFloorReached()`
- `Scripts/Systems/TownNPCStorySystem.cs` — `NPCLocKeyMap` dictionary; 6 helper methods (`GetLocalizedName`, `GetLocalizedTitle`, `GetLocalizedDescription`, `GetLocalizedDialogue`, `GetLocalizedChoicePrompt`, `GetLocalizedChoiceText`); notification text localized
- `Scripts/Systems/NPCPetitionSystem.cs` — All 6 petition types (betrayal, romance, custody, royal, dying wish, rivalry) converted from hardcoded English to `Loc.Get("petition.*")` keys
- `Scripts/Core/Child.cs` — `LastParentingDay` property for per-child interaction cooldown; `GetSoulDescription()` rebalanced ranges with "mischievous" and "virtuous" tiers; `CreateChild()` birth alignment inheritance from parent Chivalry/Darkness; all `new Random()` → `Random.Shared`
- `Scripts/Systems/FamilySystem.cs` — `ParentingScenario`, `ParentingChoice`, `ChildAgeGroup` types; `ParentingScenarios` static class with 24 scenarios (8 per age group); `GetRandomScenario()` and `CalculateAlignmentModifier()` methods; `SerializeChildren()`/`DeserializeChildren()` include `LastParentingDay`; all `new Random()` → `Random.Shared`
- `Scripts/Systems/LootGenerator.cs` — `GetEffectKey()` two-pass regex fix for PascalCase-to-snake_case conversion (MaxHP → max_hp, not max_h_p); class-weighted elemental effect rolls via `GetThematicWeights()` and `WeightedSelect()`; "Ranger's Cloak" body armor renamed to "Ranger's Leather"
- `Scripts/Locations/HomeLocation.cs` — `[C] Spend Time with Child` menu option in visual and BBS menus; `InteractWithChild()` method with child selection, cooldown check, scenario display, choice handling, alignment modifier, soul change display
- `Scripts/Locations/QuestHallLocation.cs` — Removed FindArtifact hint
- `Scripts/Systems/SaveDataStructures.cs` — `LastParentingDay` field added to `ChildData`
- `Scripts/Locations/BaseLocation.cs` — Reset `_locationEntryDisplayed` on preferences exit so theme/language/compact changes redraw immediately
- `Scripts/Systems/InventorySystem.cs` — Backpack weapon color bright_red → bright_yellow; `GetRarityColor()` Rare and Artifact colors corrected to match canonical Equipment.GetRarityColor()
- `Scripts/Core/Character.cs` — DoT level scaling: Bleed (+Level/5), Burn (+Level/4), Frozen (+Level/8), Cursed (+Level/10), Diseased (+Level/15)
- `Scripts/Core/NPC.cs` — `PregnancyFatherName` property for affair father attribution (replaces unserialized dictionary)
- `Scripts/Systems/CombatEngine.cs` — Armor variance tightened to 75-100% of ArmPow (was 0-100%) across 4 damage paths; Paladin Divine Resolve +10% damage vs undead/demons in single and multi-monster paths; 15% status resist after Cyclebreaker check
- `Scripts/Systems/ClassAbilitySystem.cs` — Ability stat scaling increased (Attack 3%->4%, Heal CON 2.5%->3%, Defense CON 2%->2.5%, secondary stats increased); stat cap raised 5.0x->6.0x; Judgment Day base damage 60->75; Aura of Protection cooldown 5->4 with WIS scaling note
- `Scripts/Systems/RomanceTracker.cs` — Jealousy divorce calls full `RelationshipSystem.ProcessDivorce()` flow and clears player marriage flags; `AddSpouse()` updates existing records instead of silent exit; `GetRelationType()` checks `ExSpouses` before `Exes`; jealousy decay condition `< 90` removed (decays at all levels); `new Random()` → `Random.Shared`
- `Scripts/Systems/VisualNovelDialogueSystem.cs` — `PersonalQuestionsAsked` incremented at conversation start; duplicate `flirtCountThisSession++` removed from spouse/lover branch; CHA 1.5x multiplier removed from flirt receptiveness and confession success; `HasConfessed` and `ConfessionAccepted` now set in `HandleConfessionOption()`
- `Scripts/Systems/RelationshipSystem.cs` — `ProcessDivorce()` changed from asymmetric (Normal/Hate) to balanced (Anger/Hate); daily cap key normalized alphabetically to prevent directional bypass
- `Scripts/Systems/IntimacySystem.cs` — Difficulty multiplier removed from step counts; fade-to-black steps 3->2; both relationship directions use `overrideMaxFeeling = true`; dead NPC guards on all 4 public entry points
- `Scripts/Systems/WorldSimulator.cs` — `_pregnancyFathers` dictionary replaced with `NPC.PregnancyFatherName` property usage; divorce preserves affair pregnancies (checks `PregnancyFatherName`); pregnancy cleared on natural death and permadeath paths
- `Scripts/Locations/LoveCornerLocation.cs` — `HandleDivorce` calls `RelationshipSystem.ProcessDivorce()` and `RomanceTracker.Divorce()`; `HandleMarry` fallback registers with `RomanceTracker.AddSpouse()`; `new Random()` → `Random.Shared`
- `Scripts/Locations/HomeLocation.cs` — Dead NPC filter in `SpendTimeWithSpouse` partner selection
- `Scripts/Systems/WorldSimService.cs` — Pregnancy preservation during world-sim reload includes `PregnancyFatherName`
- `Scripts/Systems/SaveDataStructures.cs` — `PregnancyFatherName` field on `NPCData`; `LastParentingDay` on `ChildData`
- `Scripts/Systems/SaveSystem.cs` — `PregnancyFatherName` serialization in NPC save path
- `Scripts/Systems/OnlineStateManager.cs` — `PregnancyFatherName` in shared NPC data serialization
- `Scripts/Core/GameEngine.cs` — `PregnancyFatherName` restored in NPC restore path
- `Scripts/Locations/BaseLocation.cs` — Paladin Divine Resolve passive display in `/health` Active Buffs
- `Scripts/Locations/ChurchLocation.cs` — Marriage candidate list checks RomanceTracker lovers; RelationshipSystem sync before PerformMarriage
- `Scripts/Locations/CastleLocation.cs` — Royal divorce calls `RelationshipSystem.ProcessDivorce()` before `RomanceTracker.Divorce()`
- `Localization/en.json` — ~547 new keys (seals, quests, npc_story, petition) + ~179 new parenting keys; `->` arrow fix
- `Localization/es.json` — Spanish translations for all new keys; `->` arrow fix
- `Localization/hu.json` — Hungarian translations for all new keys; `->` arrow fix
- `Localization/it.json` — Italian translations for all new keys; `->` arrow fix
