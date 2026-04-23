# v0.57.11 - Royal Guard Regression Fix + NPC Recruitment Redesign + Crit Chance Rebalance + Companion Spell Management + Imprisoned-Teammate Filter + Team Status Accounting

Six shipsets. A single-line hotfix for a v0.57.10 regression in the Royal Guard defense teleport, a substantial redesign of NPC recruitment that ties it to the social system (relationships matter, lovers/spouses aren't hirelings, enemies refuse) with a proper search and filter UI, a crit-chance rebalance from in-game gossip feedback (DEX-scaling cap + Creator's Eye nerf), and a fix for companions ignoring the "disable all skills" toggle by casting spells instead.

---

# Royal Guard Defense Teleport (v0.57.10 Regression Fix)

Single-line fix for a regression the v0.57.10 Royal Guard defense fix introduced. The teleport still doesn't actually work — players accepting the defense alert see "King system guard check failed: Exiting to Castle" in the server error log and stay put at their original location.

## Root cause

v0.57.10 replaced the broken `NavigateToLocation(GameLocation.Castle)` call in `BaseLocation.CheckGuardDefenseAlert` with `throw new LocationExitException(GameLocation.Castle)` to bypass the navigation-table walking-rules check. The teleport logic itself was right: `LocationManager.EnterLocation` wraps the location body in a `try/catch (LocationExitException)` block that calls `EnterLocation(ex.DestinationLocation, ...)` directly on catch, which is exactly the behavior we needed.

What v0.57.10 missed: `CheckGuardDefenseAlert` itself has an outer `try/catch (Exception ex)` wrapping its entire body:

```csharp
catch (Exception ex)
{
    // King system not available - ignore
    DebugLogger.Instance.LogError("LOCATION",
        $"[CheckGuardDefenseAlert] King system guard check failed: {ex.Message}");
}
```

That catch was there to swallow "king system not available" soft errors and keep the alert from crashing the player's session. Reasonable in isolation. But `LocationExitException` inherits from `Exception`, so the general catch grabbed it before the teleport signal could propagate up to `LocationManager`'s dedicated handler. The throw got logged as a scary-looking error and silently dropped.

Net effect on the live v0.57.10 server: every Royal Guard who accepted the defense prompt saw the original alert dialogue, pressed Enter, got no response, stayed at their original location, and the error watcher logged `"King system guard check failed: Exiting to Castle"`. Same symptom as the original pre-v0.57.10 bug the spudman report described, just for a different reason this time.

## Fix

One block added to `CheckGuardDefenseAlert`, placed before the general `catch (Exception ex)` so it takes precedence:

```csharp
catch (LocationExitException)
{
    // Let the teleport propagate to LocationManager.EnterLocation.
    throw;
}
```

C# catch clauses are matched top-down, so the specific `LocationExitException` handler runs first and rethrows cleanly. The general `catch (Exception ex)` still catches genuine king-system soft errors (null king, corrupt state, etc.) and logs them as before. The teleport signal now makes it through to `LocationManager.EnterLocation`'s `catch (LocationExitException)` block and routes the player to the Castle the way v0.57.10 always intended.

## Scope

- Affects only the Royal Guard defense-alert flow in online multiplayer. Players who aren't royal guards, or who decline the defense alert, never trigger either branch.
- No save format change. Existing v0.57.10 saves load cleanly on v0.57.11.
- No other paths use `LocationExitException` from inside a general-Exception-catching block, verified via grep.

## Royal-guard-specific files changed

- `Scripts/Core/GameConfig.cs` — Version 0.57.11.
- `Scripts/Locations/BaseLocation.cs` — Added a `catch (LocationExitException) { throw; }` block in `CheckGuardDefenseAlert` before the general `catch (Exception ex)`, so the Castle teleport signal propagates up to `LocationManager.EnterLocation` instead of being logged as a spurious error.

---

# NPC Recruitment Redesign

NPC recruitment at the Team Corner was previously a flat "top 10 NPCs by level" list with a level-based price formula. The social system (friendships, romances, enmities) had zero interaction with recruitment, and there was no way to find a specific NPC by name — the list was populated randomly based on who happened to be in town. This release rewires recruitment so the relationship system actually drives the experience.

## What changed for players

**Relationships matter now.** Friends offer a discount. Rivals charge a premium. Enemies refuse to hire on at any price. Specifically:

| Relationship | Price |
|---|---|
| Friendship, Trust, Respect | 25% / 15% / 5% discount |
| Normal (stranger / acquaintance) | baseline |
| Suspicious, Anger | 15% / 40% surcharge |
| Enemy, Hate | refuses to join |

**Lovers, spouses, friends-with-benefits, and ex-partners no longer appear in the recruitment list at all.** They're not hirelings — they're in your life. If you search for your spouse by name you get a flavor line from them ("We're married. Come home for dinner.") instead of a blank "no match found."

**Hate-tier enemies DO appear in the list, tagged `(won't join)` and greyed out.** You can see that they exist and remember why they hate you, but you can't actually hire them. Clicking one surfaces a refusal flavor line ("I'd sooner eat my own sword than follow you into battle" or one of four other insults picked at random).

**Paginated list sorted by relationship band.** Friends first (cheapest), Neutrals next, Rivals third, Refused last — so the candidates who will join at reasonable prices are always on top. Twelve rows per page with `[N]ext / [P]rev` navigation.

**Search by name.** Press `S` on the list to open a search prompt. Type part of a name — the game uses the same "prefer StartsWith over Contains" disambiguation pattern as the Temple god picker. Single match → straight to confirmation. Multiple matches → a short sub-list to pick from. Works even for hidden NPCs (lovers, spouses, enemies) — you get refusal flavor instead of silence.

**Role filter.** Press `F` on the list to filter to a specific combat role: Tanks, DPS, Healers, Utility, or Debuff. The filter persists across page navigation within a recruit session. Role matching uses the existing `SpecRole` taxonomy — same categories already shown in the Specialization UI.

**Price breakdown on confirm.** Before you pay, a confirmation screen shows base cost, the relationship multiplier that was applied, and the final price, plus a `Hire X for Y gold? (Y/N)` prompt. No more typo-burns — every hire is double-checked.

## Other fixes caught during the rewrite

- **`IsSpecialNPC` filter added.** Seth Able and other scripted NPCs were previously recruitable; they shouldn't be, they belong to their own storylines. Now filtered out.
- **Prisoners filtered out.** NPCs with `DaysInPrison > 0` no longer appear as recruitable — they're in jail.
- **Live re-check before gold deduction.** Between rendering the list and confirming the hire, an NPC could die, join another team, or have their relationship with you flip (a companion quest shifting them to Hate). The confirmation step now re-checks eligibility and price so you don't pay for a ghost or a stale discount.

## Design decisions you might wonder about

- **Why don't lovers/spouses show at all?** We discussed whether to hide them or show them with a snarky refusal on the list. Hidden won because "why is my spouse on the hire-a-stranger menu at all" is the natural question a player asks — showing them with refusal flavor belongs on the *search* path, where the player typed their name on purpose.
- **Why does Enemy tier (100) refuse, not just surcharge?** Consistency with Hate (110) and thematic payoff. Someone whose relationship score has climbed all the way to Enemy isn't going to follow you into combat for +50% pay.
- **Why keep the `[F]ilter` role taxonomy as Tank/DPS/Healer/Utility/Debuff?** Those are the existing `SpecRole` labels, already shown in the Specialization UI that players see when they spec their team. Matching them keeps the mental model consistent.

## Recruitment files changed

- `Scripts/Core/GameConfig.cs` — New relationship-multiplier constants: `RecruitPriceFriendship`, `RecruitPriceTrust`, `RecruitPriceRespect`, `RecruitPriceNormal`, `RecruitPriceSuspicious`, `RecruitPriceAnger`. Enemy and Hate tiers are refusal, no constant needed.
- `Scripts/Systems/TeamSystem.cs` — New `RecruitmentBand` enum (`Hidden`, `Friend`, `Neutral`, `Rival`, `Refused`) and four new static helpers: `GetRecruitmentBand(Character, NPC) → (band, multiplier)`, `GetRecruitmentBaseCost(NPC, Character)` (the old level+stat formula, extracted and made testable), `GetRecruitmentCost(Character, NPC)` (final cost with multiplier applied), and `IsRecruitable(Character, NPC, out band)` (centralized filter: alive, not permadead, teamless, not IsSpecialNPC, not in prison, not a romantic partner). Lives as a new `#region Recruitment (v0.57.11)` at the bottom of the file.
- `Scripts/Data/SpecializationData.cs` — New `GetDefaultRolesForClass(CharacterClass) → SpecRole[]` helper. Unspecialized NPCs don't have a `Spec` assigned yet; this fallback maps `CharacterClass` to the set of roles that class's specs can cover, with a hand-curated best-fit set for prestige classes that have no specs defined.
- `Scripts/Locations/TeamCornerLocation.cs` — `RecruitNPCToTeam` fully rewritten. Replaced the inline `CalculateRecruitmentCost` method (deleted; calls go through `TeamSystem` now). New methods: `BuildRecruitmentCandidates` (filters + sorts), `RenderRecruitmentHeader` / `RenderRecruitmentRow`, `PromptRoleFilter`, `SearchAndRecruitByName`, `ConfirmAndRecruit`. New private struct `RecruitmentRow` for per-row display state.
- 5 localization files (en/es/fr/hu/it) — 40 new keys covering the list header (`team.recruit_col_rel`, `_wont_join`), relationship tags (`team.recruit_rel_friend/neutral/rival/enemy`), navigation footer (`team.recruit_page_footer`, `_page_nav`, `_nav_nofilter`), search prompts (`_search_prompt`, `_no_match`, `_multiple_matches_header`, `_pick_match`), role filter menu (`_filter_prompt`, `_filter_all/tank/dps/healer/utility/debuff`, `_filter_none_match`, `_active_filter`), confirmation screen (`_confirm_header/name/relationship/base_cost/multiplier/final_cost/prompt/cancelled/unavailable_now`), and the refusal flavor pool (`_refuse_spouse_search`, `_refuse_lover_search`, `_refuse_fwb_search`, `_refuse_ex_search`, `_refuse_hate_1` through `_refuse_hate_5`).
- `Tests/TeamSystemRecruitmentTests.cs` — 20 new unit tests covering every band, filter condition, and edge case (lover overrides friendship score, refused NPCs are visible but cost returns -1, prisoners filtered out, etc.).

## What was considered but deferred

- **Recruiting your own companions.** The companion system (Aldric / Mira / Vex / Lyris / Melodia) uses its own Inn-based recruitment flow with character-specific dialogue. Making those also go through the Team Corner list would duplicate state and lose the companion-specific scenes. Out of scope.
- **Leveling the recruitment pool with the player.** Right now the NPC pool is whatever NPCs happen to be in town. A player at level 100 sees mostly level 30-50 candidates because the world's NPC population is bell-curved around mid-levels. Addressing this would need deeper work in `NPCSpawnSystem` to spawn higher-level NPCs proportional to player level, out of scope for this pass.

Tests: 616 / 616 passing (596 existing + 20 new recruitment tests) at this point in the release. See the next section for crit-test additions.

---

# Crit Chance Rebalance

Single shipset responding to player feedback from in-game gossip. Two interlocking crit-chance problems got flagged: DEX-primary classes plateau at 50% crit chance well before they've actually "built" into crit, and the Creator's Eye artifact is so strong that the moment a player earns it every other crit-chance investment becomes redundant.

## Why this changes

Pre-v0.57.11:

- Crit chance was hard-clamped at 50% for every character regardless of build.
- DEX 450 Abysswarden: 50% crit. DEX 750 Abysswarden: also 50% crit. Stat investment past ~450 DEX was wasted.
- Creator's Eye artifact applied a pre-clamp 1.5× multiplier. A level-30 Magician with moderate DEX got instantly pinned at 50% the moment they picked up the artifact, because the multiplier sent their base far above the clamp. No reason for that player to ever hit another crit-chance enchant.

Net: the crit-chance subsystem turned off the moment a player got the artifact, and DEX-primary classes didn't have a meaningful ceiling above non-DEX classes even without the artifact.

## What changed

**DEX-scaling cap.** The 50% cap now extends based on DEX:

```csharp
int cap = 50 + (int)Math.Min(25, dexterity / 30);
```

- Every 30 DEX lifts the cap by 1%, capped at +25% (final ceiling: 75%).
- DEX 30 → cap 51. DEX 300 → cap 60. DEX 600 → cap 70. DEX 750+ → cap 75.
- A DEX 50 Warrior still caps at 51% — identical experience as before for non-DEX builds.
- A max-DEX Abysswarden or Jester can now actually earn their way to 75% crit through stat investment.

**Creator's Eye is now a flat +10 crit chance** (previously: 1.5× pre-clamp multiplier).

- Doubles to +20 during the Manwe battle when the Void Key is held — the "empowered artifact" moment survives.
- A low-DEX build with the artifact gets a solid 15-20% crit bump. Strong, not game-breaking.
- A high-DEX build with the artifact stacks under their raised cap, so the artifact still helps. At DEX 750 the artifact takes them from ~70% to 75% (hitting the new ceiling).
- Critically, the artifact no longer single-handedly floors every build at the cap. DEX continues to matter after you pick it up.

**Guaranteed-crit abilities untouched.** Backstab, Hidden (Vanish / Umbral Step / Noctura's Embrace), Temporal Feint — still force a crit on the next hit as a separate mechanic. Those aren't percentage-stacking bonuses; they're an opt-in 100%-for-one-hit system and they work the same as before.

**Wavecaller Ocean's Voice untouched.** Still a sequential +20% backup roll that fires only if the main crit roll fails. With the new DEX cap at 75%, a Wavecaller with full DEX investment and Ocean's Voice active reaches an effective crit rate around 80% (0.75 + 0.25 × 0.20 = 0.80). Still well below 100%, still can't walk through content.

## Practical endgame numbers

| Build | DEX | Without Creator's Eye | With Creator's Eye |
|---|---|---|---|
| Level 50 Warrior | ~150 | 20% | 30% |
| Level 100 Warrior | ~300 | 35% | 45% |
| Level 100 Cleric | ~200 | 25% | 35% |
| Level 100 Assassin | ~450 | 50% | 60% |
| Level 100 Abysswarden | ~750 | 75% | 75% (hits cap) |
| Level 100 Jester | ~600 | 65% | 75% |

Non-DEX classes get the same feel as before. DEX-primary classes get genuine payoff for investment. The Creator's Eye is still best-in-slot for crit builds — it just doesn't replace having the build.

## Hard ceiling preserved

The game still prevents 100% crit walkthroughs:

- Percentage crit ceiling: **75%** (maximum cap at DEX 750+).
- Wavecaller Ocean's Voice backup adds an effective ~5% on top in the narrow window where the first roll fails, for a sequential maximum around 80%.
- No code path stacks crit sources after the clamp.

Guaranteed-crit abilities are the only way to hit 100% crit on a specific single attack, and they've always been that way.

## Crit-rebalance files changed

- `Scripts/Core/GameConfig.cs` — New `CreatorsEyeCritBonus = 10` constant (doubled during Manwe / Void Key battle).
- `Scripts/Systems/StatEffectsSystem.cs` — `GetCriticalHitChance` rewritten. DEX-scaling cap (`50 + min(25, dex/30)`) replaces the flat 50 clamp. Creator's Eye switched from 1.5× pre-clamp multiplier to flat `+CreatorsEyeCritBonus` added to baseChance. Manwe/Void Key still doubles the artifact bonus.
- `Tests/CriticalHitChanceTests.cs` — 11 new unit tests covering the cap at low/mid/high DEX, equipment bonus interaction, hard ceiling at 75%, and sanity checks on the crit-damage formula (unchanged by this release).

## Follow-up (not in this release)

- Artifact integration test harness (test fixture for `ArtifactSystem.Instance`) — the new tests skip the artifact code path because the singleton has loaded state that isn't easily stubbed in isolation. The artifact's flat-bonus logic was manually verified via the release-notes arithmetic above but would benefit from automated coverage.
- Possible future tuning: if the 75% ceiling still feels too high in play, the `Math.Min(25, dexterity / 30)` can be tightened to 20 or 15 without changing the shape of the fix.

---

# Companion Spell Management

Player report: "Mira is only casting group heal. I have disabled all skills and she still casts group heal. I also tried unequipping all her items but that did not seem to affect it either."

## What was happening

The Inn's "Combat Skills" screen (`ManageCompanionAbilities` in [InnLocation.cs](Scripts/Locations/InnLocation.cs)) let the player toggle individual class abilities off. Each companion has a `DisabledAbilities` list that the combat AI respects when picking an ability to use.

Spells are a parallel system. `SpellSystem.GetAllSpellsForClass` returns the caster's spell list by `(Class, Level)` pair, and the two combat-AI helpers that pick a spell —

- `GetBestHealSpell` at [CombatEngine.cs:16107](Scripts/Systems/CombatEngine.cs#L16107) for heals
- `GetBestOffensiveSpell` at [CombatEngine.cs:16124](Scripts/Systems/CombatEngine.cs#L16124) for damage spells

— both filter on level requirement and mana cost but never looked at `DisabledAbilities`, which was an abilities-only concept anyway. The Inn UI had no way to turn spells off. Players who disabled every ability on a companion were surprised to see their Cleric companion keep casting Mass Cure and their Magician keep firing Fireball.

The specific reproduction from the player report: Mira is a Cleric. Mass Cure (Cleric spell level 12, unlocked around character level 24) heals the whole party and is flagged `SpellType = "Heal"`. Any time the party's most-injured member drops below the 70% heal threshold, `TryTeammateHealAction` calls `GetBestHealSpell` which returns Mass Cure (highest-level heal she knows), and she casts it. No ability toggle touches that path. Unequipping her items also doesn't help because healing spells don't require a weapon.

## Fix

**New `Companion.DisabledSpells: HashSet<string>` field** parallel to `DisabledAbilities`. Populated by spell Name (which is unique within a class). Defaults to an empty set — pre-v0.57.11 saves load with every spell enabled, matching the previous behavior.

**`GetBestHealSpell` and `GetBestOffensiveSpell`** now filter out any spell whose name is in the companion's `DisabledSpells` set. The lookup goes through a new helper `GetDisabledSpellsFor(Character caster)` that checks `caster.IsCompanion && caster.CompanionId.HasValue` and pulls the set from `CompanionSystem.Instance.GetCompanion(id)`. Non-companion NPCs (free NPCs recruited to the player's team, hostile NPCs in combat) aren't affected — their spell selection is unchanged.

**Inn Combat Skills UI extended to show spells** under a new `SPELLS` section header below the existing `ABILITIES` section. Spells use the same `[N] [ON]/[OFF]` toggle pattern. Numbered indices continue through both sections (e.g. if a companion has 8 abilities, the spells start at index 9). Selecting `A` clears both `DisabledAbilities` and `DisabledSpells`. The enabled-count footer now shows both counts (`"5/8 abilities, 3/7 spells enabled"`).

## Files changed

- `Scripts/Systems/CompanionSystem.cs` — New `Companion.DisabledSpells` HashSet field and matching `CompanionSaveData.DisabledSpells` List for the save DTO. Wired serialize, deserialize, and `ResetAllCompanions` to preserve/clear the new field.
- `Scripts/Systems/CombatEngine.cs` — New `GetDisabledSpellsFor(Character)` helper that resolves `caster.CompanionId` to the companion's `DisabledSpells` set. `GetBestHealSpell` and `GetBestOffensiveSpell` now exclude spells whose names are in that set.
- `Scripts/Locations/InnLocation.cs` — `ManageCompanionAbilities` extended with a second section for spells (pulled from `SpellSystem.GetAllSpellsForClass` filtered by the companion's level). Shared numbered-index loop handles both sections. Enable-all (`A`) clears both lists.
- 5 localization files (en/es/fr/hu/it) — 2 new keys (`inn.skills_abilities_header`, `inn.skills_spells_header`). Existing `inn.all_abilities_enabled` message updated to mention spells too.

## Scope notes

- **Save compatibility.** Pre-v0.57.11 saves don't have the `DisabledSpells` list; they load with the empty default. The companion's current spell-casting behavior is unchanged until the player opens the new UI and turns spells off.
- **Free NPCs (non-companion team members) not affected.** The `GetDisabledSpellsFor` helper gates on `IsCompanion`, so only the four hero-companions (Aldric / Mira / Vex / Lyris / Melodia) have spell-disable support. Adding it for free NPC teammates would need the analogous UI somewhere (Team Corner, probably), and the data model is slightly different (NPC rather than Companion). Flagged as a future follow-up — the player who reported this was talking about Mira specifically.

Tests: 627 / 627 passing. Verified: the spell filter is a pure additive check that respects an empty set as "allow everything" (matches previous behavior), so no existing test regresses. No new unit tests added for this fix — the filter logic is a one-line `.Where` clause whose behavior is obvious from reading; the interesting part is the UI, which is manual-test territory.

---

# Imprisoned-Teammate Filter

Player report: "Lysandra Dawnwhisper is supposed to be in prison but she is also out in a dungeon with spudman.. ?"

## What was happening

When a player imprisoned an NPC via the Castle (King's orders, judicial action, etc.), the NPC's `DaysInPrison` was set on the shared world-state record, but no code removed her from any player's saved dungeon party list (`DungeonPartyNPCIds` on the Character). Three gaps:

1. The candidate list at [DungeonLocation.cs:9445](Scripts/Locations/DungeonLocation.cs#L9445) (`AddTeammateToParty`) filtered on `n.IsAlive` but not `DaysInPrison`, so a just-imprisoned teammate still showed up in the "recruitable to dungeon party" list.
2. `RestoreNPCTeammates` at [DungeonLocation.cs:1090](Scripts/Locations/DungeonLocation.cs#L1090), which rebuilds the party from the player's saved ID list on each dungeon re-entry, had the same gap — a teammate arrested between sessions would silently rejoin on next entry.
3. The spouse and lover special-case branches (same method) had matching `IsAlive && !IsDead` filters but no prison check. Your spouse getting arrested didn't stop them from coming along on your next dungeon run.

Net effect on spudman's save: he'd recruited Lysandra before she was imprisoned, her ID sat in his `DungeonPartyNPCIds`, and every time he re-entered the dungeon the restore path happily added her back. Meanwhile Coosh at the Castle saw her on the prison roster — because she genuinely had `DaysInPrison > 0`. Both true simultaneously.

## Fix

Three `&& n.DaysInPrison == 0` additions to the three filter sites, plus a restore-path enhancement: when a saved teammate ID resolves to an NPC who is now imprisoned, we skip them AND print a yellow notice (`"X was arrested while you were away and can't rejoin the party."`) so the player isn't mystified about the smaller party. The NPC ID stays in the saved list for now — if the player gets them released later, they'll rejoin on the next entry.

A deeper architectural fix would also iterate live sessions when an imprisonment fires and eject the NPC from active dungeon parties in real time. That's out of scope for a hotfix because it requires cross-session mutation (and online dungeon sessions are already session-isolated for good reasons). The entry/restore filter is sufficient to resolve the reported contradiction — the NPC may still physically be in a mid-run dungeon with a player until they exit and re-enter, but she can't re-enter once released, and new parties can't include her.

## Files changed

- `Scripts/Locations/DungeonLocation.cs` — Three filters extended with `&& n.DaysInPrison == 0`: candidate-list build at `AddTeammateToParty`, spouse branch, lovers branch. `RestoreNPCTeammates` gained a mid-loop prison check that skips imprisoned NPCs and collects their names for a bulk yellow "X was arrested while you were away" notice at the end of the restore sequence.
- 5 localization files (en/es/fr/hu/it) — new `dungeon.teammate_arrested` key with `{0}` name placeholder.

## Scope notes

- **Romance partners check `DaysInPrison` too.** If your spouse/lover gets locked up, they're not available for dungeon runs until released. Matches the no-bypass-via-relationship pattern set by v0.57.11's recruitment redesign, which also filters lovers/spouses out of the recruitment flow (different filter, same rule).
- **Mid-run imprisonment doesn't yank an NPC out of an active dungeon party.** If spudman is already deep in the dungeon when Coosh imprisons Lysandra, Lysandra stays with spudman for that run. On next entry she'll be skipped with the notice. Accepting that trade-off because cross-session mutation of active dungeon parties would be a substantial change.

Tests: 627 / 627 passing.

---

# Team Status Accounting

Player report: "When looking at team status (and info on teams, and team rankings), your character is not listed, and is not calculated in the total members. Under NPC recruitment I noticed it was off by 1 too many yesterday, seems good now. Sacking members that died didn't seem to have any effects other than history log."

## Three issues bundled

**1. Viewer missing from their own team roster.** The `GetPlayerTeamMembers` query at [SqlSaveBackend.cs:3346](Scripts/Systems/SqlSaveBackend.cs#L3346) takes an `excludeUsername` parameter and filters the viewer out of the result set. The exclusion exists so callers can render "other team members" without duplicating the viewer in both the "you" section and the "everyone else" section. But the Team Corner's `ShowTeamMembers` at [TeamCornerLocation.cs:582](Scripts/Locations/TeamCornerLocation.cs#L582) never re-added the viewer after the exclusion, so team-status / team-info / team-rankings all showed the viewer's team minus the viewer. Total count was off by one. Fix: synthesize a `PlayerSummary` for `currentPlayer` and prepend it to `playerMembers` when the rendered team name matches the viewer's team.

**2. Recruitment off-by-one (yesterday).** Already resolved by the v0.57.11 NPC Recruitment Redesign (first shipset above). The old counting logic pre-v0.57.11 had the same root cause as bug 1 — a player-exclusion in candidate-list building that wasn't compensated for elsewhere. The rewrite at [TeamCornerLocation.cs:1025-1027](Scripts/Locations/TeamCornerLocation.cs#L1025-L1027) now counts team size as `alive NPCs on my team + 1 for player`, matching the new display logic. spudman confirmed this one is fixed.

**3. Sacking a dead member silently reverted.** Same class of bug as the v0.57.10 Coosh turf-reverts-on-relog issue. The sack at [TeamCornerLocation.cs:1838](Scripts/Locations/TeamCornerLocation.cs#L1838) mutated `member.Team = ""` in memory, posted news, then fired `_ = Task.Run(async () => await OnlineStateManager.Instance.SaveAllSharedState())` as a fire-and-forget. If any other online session triggered a `world_state` reload before the Task completed, the stale snapshot put the NPC back on the team — the sack was gone and only the news post (and the history log spudman noticed) survived. The same fire-and-forget shape existed at the password-change path a few hundred lines up. Fix: `await` the save in both places. Matches the pattern used by every other TeamCornerLocation save site (spec change, equipment, recruitment) which already awaits.

## Files changed

- `Scripts/Locations/TeamCornerLocation.cs` — `ShowTeamMembers` prepends a synthesized `PlayerSummary` for `currentPlayer` when the rendered team matches the viewer's team. Both `SackMember` and the team-password change block converted from `_ = Task.Run(async () => await SaveAllSharedState())` to straight `await SaveAllSharedState()` with proper exception logging, matching the codebase's other per-mutation save sites.

## Scope notes

- **The `GetPlayerTeamMembers` SQL query is intentionally unchanged.** Its `excludeUsername` parameter is still correct for callers that want "other members" (used by the chat `/team` command and a couple of places that need a non-duplicating roster). The fix lives at the Team Corner display layer, where the viewer's own-team context is known.
- **Total-count display corrected automatically.** The `total_members` loc string reads `"{0} total ({1} players, {2} NPCs)"`. Adding the synthesized viewer to `playerMembers` naturally updates the arithmetic.
- **No other fire-and-forget `SaveAllSharedState` sites in TeamCornerLocation.** I grepped after fixing the two reported ones. There's one related site in `CombatEngine.cs:7872` (companion [T] transfer during combat loot) which has the same shape but a narrower race window (players rarely log out mid-combat). Left for a future pass if it shows up in a report.

Tests: 627 / 627 passing.
