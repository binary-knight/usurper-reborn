# v0.61.4 -- Beta

Hotfix on top of v0.61.3. Phase 1.5 of the NPC dialogue dynamics plan (see `memory/project_npc_dialogue.md` for the full design).

v0.61.3 shipped the initial slice of the dynamic-dialogue system: `DialogueEnhancer.cs` static helper with three contextual accessors (mood / memory / faction tension) layered probabilistically on top of `VisualNovelDialogueSystem.ShowGreeting` and `ShowFarewell`. Zero LLM cost, pure substitution on already-tracked NPC state.

v0.61.4 hardens that slice. Six new layers, a coherence pipeline that stops contradictory stacks, a recent-line variety cache, and two more dialogue surfaces wired in. No save format change, no behavior change for any non-VN-chat path.

## What v0.61.3 actually shipped vs what it looked like in play

The 0.61.3 cut was a working proof-of-concept but had three rough edges. Surfaced now because each was visible enough that anyone running 0.61.3 for an evening would have noticed:

1. **Contradictory layer stacking.** Each layer rolled independently. The mood layer could prepend `*beaming,*`, the memory layer could append "you and I have unfinished business," and the faction layer could append "Don't tell your priests we spoke" -- all on the same line. Three contradictory beats produce nonsense ("Beaming, the merchant says: 'How can I help you? You and I have unfinished business. Don't tell your priests we spoke.'"). Nothing in the original Phase 1 design coordinated layer tone.

2. **Repetition.** Each layer picked from a flat pool with `_random.Next(pool.Length)`. Two `*scowls,*` in a row, three `*beaming,*` in five lines. The pools were small (3-4 lines each per emotion) so collisions hit fast.

3. **Layer coverage too narrow.** Only `ShowGreeting` and `ShowFarewell` carried the Enhance() call. The bulk of player-NPC chat (chat-topic replies, personal-question replies) was raw template text -- the system was technically live but most NPC chat looked exactly like it did before.

## What 0.61.4 changes

### Tone coherence pipeline

`Enhance()` now operates as a 7-step pipeline where step 1 (mood) sets a `Tone` (Positive / Negative / Neutral) that downstream layers consult. Tone derives from the dominant emotion:

| Emotion | Tone |
| --- | --- |
| Joy / Gratitude / Hope / Peace / Confidence / Pride | Positive |
| Anger / Fear / Sadness / Envy | Negative |
| Greed / Loneliness / no dominant emotion | Neutral |

Downstream layers gate on tone:

- **Memory layer.** Splits its variant pool into positive / neutral / negative phrasings. Under Positive tone, force-fallback to neutral phrasing rather than pulling "you and I have unfinished business" against a `*beaming,*` opener. Same in reverse: under Negative tone, drop "I owe you" lines.
- **Witness layer.** Skipped entirely under Positive tone (you can't surface "I saw you slit that man's throat" while beaming).
- **Player-state hurt** (new this version). Hostile phrasing for Negative tone ("Should've thought about that before bleeding at me"), friendly phrasing for everything else ("Gods, you look terrible -- are you alright?").
- **Personality layer.** Greed and Compassion lines suppressed under Negative tone (callous "coin talks" doesn't land if the NPC is grieving; "hope you're keeping well" doesn't land if angry).
- **Faction layer.** Softer phrasing under Positive tone ("Even between our paths, there can be civility") instead of the standard hostile beat.

The faction layer additionally suppresses entirely for romance partners (Spouse / Lover / FWB) regardless of tone. A spouse opening with "Don't tell your priests we spoke" was one of the most visible rough edges in 0.61.3.

### Recent-line variety cache

New `PickFresh(npc, layer, options[])` replaces every direct random-from-pool call. Per-(npc, layer) sliding window of the last 3 lines used; subsequent picks skip anything in the window. Falls back to full pool if every option is in recent (small pools could legitimately exhaust). Soft cap at 1024 keys with half-drop eviction so the static doesn't leak across long-running MUD sessions. Keyed on `{npc.ID}|{layer}` so different NPCs don't share each other's recent windows.

Net effect: same NPC won't fire the same `*beaming,*` twice in a row, and won't fire the same memory beat back-to-back.

### Three new contextual layers

1. **Witness layer.** Reads `MemorySystem.GetMemoriesOfType(MemoryType.WitnessedEvent)`, filters to memories whose Description starts with `"Saw {playerName} "` (matches the shape that `SocialInfluenceSystem.RecordWitnesses` writes). Surfaces an oblique allusion ("I saw what you did. Don't think no one noticed.") rather than reading the raw memory description, because the memory descriptions are flat ("Saw Lumina murder Vex") and reading them verbatim would feel robotic. Positive vs negative EmotionalImpact branches to different variant pools. 14% gated, suppressed under Positive tone.

2. **Player-state awareness.** NPC notices visible cues about the player. Two branches:
   - **Hurt:** `HP / MaxHP < 30%` AND both are positive (guards against div-by-zero on test-shaped Characters). Hostile-tone or friendly-tone variants depending on the mood.
   - **Wealthy:** `Gold >= 100,000` AND NPC `Personality.Greed > 0.55`. Gates wealth-comments to merchant-like NPCs so a paladin doesn't suddenly start ogling the player's purse.
   - 25% gated (higher than other layers because the trigger is concrete world state, not pure probability).

3. **NPC-side grief.** `EmotionalState.Sadness` intensity >= 0.5 AND a `MemoryType.SawDeath` within the past 7 days. Flavor is intentionally non-specific (no spouse name) because we don't have an NPC-side bereavement registry to confirm who died; full Phase 2 work would add that. 18% gated. This is the lightweight detector promised in the Phase 1 plan as "Phase 1.5 work."

### Personality flavor layer

Picks the most-deviated trait from `PersonalityProfile.{Greed, Trustworthiness, Compassion, Courage, Caution, Ambition}` and produces a 1-liner. "Compassion" is derived from the inverse of Aggression and Vengefulness.

**Compassion formula corrected mid-pass.** The original draft used `(1 - Aggression - Vengefulness) / 2 - 0.25` which required A+V <= 0.2 total to fire. That excluded basically every NPC that wasn't a literal saint. Replaced with `((1-A) + (1-V)) / 2 - 0.5`, which fires for typical peaceful NPCs (A=V=0.3 now reads as 0.2 strength, above the 0.15 salience threshold).

Greed and Compassion suppressed under Negative tone (the only two trait lines where tone matters -- "I'll be straight with you" or "Speak plain" land fine regardless of mood). 10% gated.

### Two new dialogue surfaces wired

`VisualNovelDialogueSystem.HandleChatTopic` (the response generated when the player picks a chat topic) and `HandlePersonalQuestion` (both branches: the relationship-tier 1-40 intimate-story branch AND the 41-70 surface-level branch) now route their NPC reply through `Enhance()` before display.

The flirt / compliment / breakup-line surfaces are intentionally NOT wired. Those templates already carry romantic tone and emotional context; layering generic mood / witness / faction beats on top would break the atmosphere. Flirt variants are explicitly Phase 2 work in the plan file (LLM-generated pools per relationship tier).

`NPCDialogueGenerator.GenerateGreeting` also deliberately not wired. That system has its own internal mood / memory / personality / context layering (lines 449-483 of `NPCDialogueGenerator.cs`); double-layering would stack two `*beaming,*` prefixes on the same line.

## Pipeline summary

| Step | Layer | Probability | Tone-gated? |
| --- | --- | --- | --- |
| 1 | Mood prepend | 22% | sets tone |
| 2 | Memory append | 18% | yes (valence-matched) |
| 3 | Witness append | 14% | yes (skipped on Positive) |
| 4 | Player-state append | 25% | yes (hurt has hostile/friendly variants) |
| 5 | Grief aside | 18% | no |
| 6 | Personality aside | 10% | yes (Greed/Compassion suppressed on Negative) |
| 7 | Faction append | 12% | yes (softened on Positive, suppressed for romance) |

Per-line stacking is bounded by tone coherence + the upper bound of all seven at ~93% (no enhancement at all on ~7% of lines if every layer rolls true). Practical mean is much lower because most NPCs don't have strong memory / witness / grief / personality salience to surface on any given line, and tone coherence drops layers that would clash.

## Smoke tests

New `Tests/DialogueEnhancerTests.cs` with 12 tests covering:

- Null / empty `baseLine`, null `npc`, null `player` -- all return safely without exceptions, base line preserved when present.
- Bare NPC (no Brain / Personality / Memory / EmotionalState -- the deserialization shape): 200 iterations, no crashes regardless of which layer rolls.
- 1000-call hammer on the recent-line cache: no concurrent-modification / unbounded-growth issues.
- All public accessors (`GetMoodFlavor`, `GetMemoryFlavor`, `GetFactionTensionFlavor`) null-safe with null NPC.

Tests don't lock specific flavor strings -- the variant pools shift between releases. They lock the contract: null-safe inputs, no exceptions on bare NPCs, base line preserved when no layer fires.

## Localization: Hungarian end-to-end

Phase 1.5 of the dialogue dynamics plan ships with full Hungarian support. Both the VN dialogue templates (greetings, farewells, chat-topic replies, personal-stories, flirts, compliments, personal-question replies) AND the enhancer flavor pools (mood, memory, witness, player-state, grief, personality, faction) are localized into Hungarian. Hungarian players get the complete contextual-dialogue experience: localized base templates with localized contextual flavor layered on top.

**Surface 1: VN dialogue templates (175 new keys).**

`VisualNovelDialogueSystem.cs` previously had 175 inline English strings across 14 method-level pools: 9 relationship-tier greetings (spouse / lover / fwb / intimate / friendly / respectful / neutral / wary / hostile), 7 farewells, personal-question pool, flirt pools across 5 progression tiers, compliment pools (class-keyed + personal), personal-story pools (intimate + casual), chat-topic-response pools across ~30 case branches, and inline flirt-response pools (lover-response, strong/med/mild positive, neutral, neg-variants for not-attracted / married / persistent / too-soon / general).

All 175 strings extracted to `dialogue.vn.*` keys in `en.json`. Refactor pattern: `random.Next(pool.Length)` becomes `random.Next(N) + 1` followed by `Loc.Get($"key_prefix_{idx}")`. Format-arg interpolations (e.g. `{player.Name}` in greetings, `{npc.ClassName}` in personal stories) converted to `{0}` placeholders resolved at lookup time.

Hungarian translations follow the existing `hu.json` voice: informal "te" register, asterisk action conventions preserved (`*összevonja a szemöldökét*`), no foreign-style subject pronouns (Hungarian verb conjugation carries the subject), noun-suffix possessives instead of separate possessive pronouns. Gender-neutral 3rd person throughout (Hungarian doesn't gender pronouns). 175/175 keys translated.

**Surface 2: Enhancer flavor pools (114 new keys).**

`DialogueEnhancer.cs` had 89 hardcoded strings in `PickFresh(npc, "layer", "*scowls,*", ...)` calls, plus 5 hardcoded time-reference strings ("earlier today", "the other day", etc.), plus the 6 memory pool variants with `{when}` interpolation. All extracted to `dialogue.enhance.*` keys.

New `PickFreshLoc(npc, keyPrefix, count)` helper builds numbered keys `{keyPrefix}_1` through `{keyPrefix}_{count}`, resolves each via `Loc.Get`, then delegates to existing `PickFresh` for variety / cache. The recent-line cache continues to work correctly because PickFresh stores the resolved (localized) strings, not the raw template keys.

Memory pool keys carry `{0}` format args for the localized `when` string, resolved at pool-build time so PickFresh's variety cache sees the final formatted string (otherwise the cache would treat `"I haven't forgotten {0}"` and `"I haven't forgotten earlier today"` as different lines and let exact repeats slip through).

114/114 keys translated to Hungarian.

**Language gate widened to en + hu.**

`DialogueEnhancer.IsEnglish()` renamed to `IsSupportedLanguage()` (legacy `IsEnglish` kept as delegating shim). Returns true for "en" or "hu" or null/empty (defaults to English). Other languages (es / fr / it) still see the raw base line because their VN templates and flavor pools haven't been translated yet; the gate widens per future loc passes.

Smoke tests updated: 12 existing tests still pass, plus the language-gate test now confirms `Enhance` returns the exact base line under es / fr / it (50 iterations each) -- hu is no longer in the blocked list. `GetMoodFlavor` returns null under es (not hu).

**Translation infrastructure used.**

A focused worktree-isolated agent handled the 175-string VN extraction + Hungarian translation in a single batch (no `DialogueEnhancer.cs` in its worktree because it predated this branch; merged via file copy + Enhance() re-wiring afterward). The 114-string enhancer pool extraction + Hungarian translation done directly in main worktree.

**Build:** 0 errors, 675/675 tests pass (14 dialogue-enhancer tests + 661 existing). No save format change.

## Files Changed

- `Scripts/Core/GameConfig.cs` -- Version 0.61.4
- `Scripts/Systems/DialogueEnhancer.cs` -- Full rewrite of Phase 1 helper. New Tone enum, `PickFresh()` variety cache, `PickFreshLoc()` localized-key helper, `EvaluateMood()` (replaces `GetMoodFlavor` internals, returns tone), `GetMemoryFlavor` with tone-gated valence-matched phrasing, `GetWitnessFlavor`, `GetPlayerStateFlavor`, `GetGriefFlavor`, `GetPersonalityFlavor`, `GetFactionTensionFlavor` with romance suppression. All hardcoded English flavor pools refactored to `Loc.Get` calls via `PickFreshLoc`. New `IsSupportedLanguage()` private helper gates `Enhance()` and the public accessors to en + hu sessions (legacy `IsEnglish` kept as delegating shim). Legacy 2-arg signatures preserved for any external callers.
- `Scripts/Systems/VisualNovelDialogueSystem.cs` -- All 14 inline string pools (greetings, farewells, personal-question, flirt, compliment, personal-story, chat-topic, flirt-response) extracted to `dialogue.vn.*` keys via `Loc.Get` calls. `Enhance()` wired into `ShowGreeting`, `ShowFarewell`, `HandleChatOption`, and `HandlePersonalOption` (both intimate-story and surface-level branches).
- `Localization/en.json` -- 175 new `dialogue.vn.*` keys + 114 new `dialogue.enhance.*` keys + 2 section comment markers (289 net additions).
- `Localization/hu.json` -- 289 matching Hungarian translations.

---

# Post-release-notes hotfix work (rolled into v0.61.4 without version bump)

The following items shipped on top of the initial v0.61.4 dialogue-dynamics work without bumping the version, on the principle that v0.61.4 had only just been deployed and re-tagging would have been noise.

## Fix: 12 corrupted loc keys from v0.60.0 (Babel release)

Player report (Lv.24 Elf Cleric): "I asked Ursula about the dungeons" -- the NPC replied with `"  \"  "` on two lines, garbled output instead of dialogue.

Root cause: 12 localization keys shipped in v0.60.0 (the Babel localization release) with the placeholder value `"  \\"` (literally two spaces and a backslash) and never received their actual content. `Loc.Get` returned the broken value verbatim. The keys lived on hot-path code in three different player interactions: asking an NPC about the dungeons, declining a duel, and the next-Old-God breadcrumb after a kill.

Affected keys (all fixed):
- 6 dungeon-advice keys (`base.dungeon_not_ready`, `_get_stronger`, `_upper_ok`, `_watch_floor`, `_you_experienced`, `_good_luck`) -- NPC advice when asked about the dungeons
- 3 duel-decline keys (`base.duel_decline_strong`, `_weak`, `_busy`)
- 1 duel-accept key (`base.duel_honorably`)
- 1 attack key (`base.attack_treacherous`)
- 1 next-god breadcrumb (`dungeon.breadcrumb_next_god`)

Fix approach:
- `en.json` -- restored proper English text recovered from git history (the v0.60.0 commit's diff showed the original hardcoded strings being replaced)
- `hu.json` -- provided Hungarian translations
- `es.json` / `fr.json` / `it.json` -- removed the corrupted entries via surgical line-removal (Python regex against the literal corrupt-value line, preserving JSON formatting and any duplicate keys). With the entries removed, `Loc.Get` falls back to the now-fixed English text.

The es/fr/it cleanup was done with a regex script rather than `json.dump` because the latter would have re-encoded the entire file, deduplicating any historical duplicate keys and reformatting unrelated content. Surgical line-removal preserves the files exactly except for the 25 corrupted lines.

## Fix: stuck player-echo recruitment ("the game lies")

Player report (Lv.12 Elf Sage): "I tried to recruit a player echo. I do not see it in the dungeon party, but when I try to recruit again, I am told the echo is in the party. The game lies."

Root cause: `RestorePlayerTeammates` in `DungeonLocation` silently skipped echoes that couldn't be loaded for non-cap reasons (the echo's save data missing, off-team, or load exception). The name stayed permanently in `DungeonPartyPlayerNames` (the persistent recruit list). The "already recruited" check at `TeamCornerLocation.RecruitPlayerAlly:2114` saw the stuck name and refused re-recruit. The player was permanently locked out of recruiting that echo with no in-game recovery path.

Three-layer fix:

1. **Auto-clean stuck echoes** (`DungeonLocation.RestorePlayerTeammates`). Tracks names that fail to load for non-cap reasons (save missing surfaces a new "save not found" yellow line; team mismatch already had a yellow line; load exception surfaces a new "could not be loaded" yellow line) and prunes them from `DungeonPartyPlayerNames` after the loop via `SetDungeonPartyPlayers(cleaned)`. Shows a "N stuck echoes removed from your recruit list" summary. Cap-skipped echoes are kept (they are valid recruits, just deferred -- the existing v0.57.9 "echoes_skipped" warning continues to fire for those).

2. **Visible recruited-list display** (`TeamCornerLocation.RecruitPlayerAlly`). Shows `Currently recruited: A, B, C` at the top of the echo-recruit menu. Each teammate row now also shows a `[recruited]` tag if they are in the recruit list. Player can now see the state instead of guessing.

3. **`[U]` un-recruit option** (new `UnrecruitPlayerAlly` method). Manual recovery path: type `U` at the ally-selection prompt to open a numbered dismissal menu. Removes the chosen echo from `DungeonPartyPlayerNames` without entering the dungeon. Recovery path for both this bug and players who simply change their mind. The "echo already in party" refusal message also gains a hint pointing to the `U` un-recruit flow.

14 new loc keys in en + hu.

## Fix: dungeon feature gold-split shrinks player rewards

Player report (Lv.52 Human Barbarian): "When examining items in dungeon room the gold found is not added correctly, for example it may say you found 12000g, ball [small] portion of that."

Root cause: `DungeonLocation.InteractWithFeature` at line 5694 ran a post-hoc gold split across *all alive teammates*, including companions, NPC mercs, echoes, royal bodyguards, and pets. Those entities have their own `Gold` field separate from the player -- giving them gold "vanished" from the player's wallet. The display path emitted the full feature gold up front (e.g. "+12,000g") but a chunk was then subtracted from the player and given to companions whose gold never returns to the player's pocket.

Math for a Lv.52 Barbarian with one companion + 3 other teammates: 12,000g -> split among 5 -> each member gets 2,400g -> player keeps 2,400g, the other 9,600g goes to companion piles (lost to the player) despite the "+12,000g" display.

Fix: filter the gold-split list to `IsGroupedPlayer` only, mirroring the established `ShareEventRewardsWithGroup` pattern. Companions and NPCs are extensions of the player's party, not independent recipients -- they no longer eat the player's gold reward. Solo players (no co-op group) now keep 100% of feature gold matching the displayed amount. Grouped players still split equally with each other. XP-split logic unchanged (companion XP exclusion + 0.75x NPC mate XP both preserved).

## Fix: class names not translated on the character-select screen

Player report (Lv.24 Elf Cleric): "When you log in, and need to select a character, the class names aren't translated. Example: `[1] druidah - 24. szintű Cleric`"

Root cause: `SaveInfo.ClassName` was populated by the backend (`SqlSaveBackend.GetAllSaves`) with `saveData.Player.Class.ToString()` -- the raw enum name in English ("Cleric", "MysticShaman"). The character-select screen displayed this string directly. Loc keys for all 17 classes existed (`class.warrior`, ..., `class.mystic_shaman`) with translations in all 5 languages, but they were never being consulted on this path.

Worse, the player-facing `Character.ClassName` property read from `GameConfig.ClassNames` (a hardcoded English array), so any of the ~15 display sites in the codebase that used `.ClassName` (Inn, Team Corner, status screens, dialogue narrations, etc.) showed English class names regardless of session language.

Fix:
- New `GameConfig.GetLocalizedClassName(CharacterClass)` and overload `GetLocalizedClassName(int)` -- return the session-language class name via `Loc.Get("class.<key>")` with English fallback to the existing `ClassNames` array.
- New `GameConfig.GetLocalizedClassNameFromString(string)` -- parses an enum-as-string back to enum and returns the localized name. Used by display sites that only have the string (like `SaveInfo.ClassName` from the backend).
- `Character.ClassName` property rewritten to use the localized helper. Fixes ~15 downstream display sites in one change.
- Character-select screen (`GameEngine.cs`) explicitly localizes via `GetLocalizedClassNameFromString` since it reads `SaveInfo.ClassName` (raw enum string from DB).

Coverage at ship time: en/hu/es full; fr missing Tidesworn / Wavecaller / Cyclebreaker; it missing all 5 prestige class names. Missing keys fall back to English. The base 11 classes are localized in all 5 languages.

## Fix: bug-report cancel discoverable

Player report (Lv.24 Elf Cleric): "As far as i can tell, you can't cancel a bug report. I think it would be good if you could."

The cancel path was already implemented -- empty or whitespace input at the description prompt triggered "Bug report cancelled." -- but undocumented. Players didn't know.

Fix: added a gray hint line below the "Describe the bug briefly..." prompt: `"(Press Enter on a blank line to cancel.)"`. Translated into all 5 languages.

## Localization: login streak messages

Player report (Lv.24 Elf Cleric): "The two strings when you end a day streak and a new day streak begins aren't translated."

The streak-broken notification ("Your N-day login streak has ended!") and the 9 milestone congratulation messages (day 1, 2, 3, 7, 14, 30, 60, 90, 100+) were all hardcoded English `$"..."` interpolations in `GameEngine.ProcessLoginStreak`.

Fix: extracted 10 strings to `engine.streak_*` loc keys. Refactored both the streak-broken branch and all milestone branches to use `Loc.Get`. Translated into all 5 languages (50 new translations total).

## Steam achievement wiring

Found one in-game achievement defined in `AchievementSystem.cs` but never triggered: `big_spender_magic` (Magical Patron, Gold tier, 100k gold spent at Magic Shop). The `StatisticsSystem` already tracked `TotalMagicShopGoldSpent` so the missing piece was the threshold check in `CheckStatAchievements`. Added: `if (stats.TotalMagicShopGoldSpent >= 100000) TryUnlock(player, "big_spender_magic");`. All 79 in-game achievements are now reachable via at least one `TryUnlock` call path.

Also produced three new docs:
- `DOCS/STEAM_ACHIEVEMENTS.md` -- catalog of all 79 in-game achievements with tier, secret flag, and trigger type (auto / manual)
- `DOCS/STEAM_ACHIEVEMENTS_MISSING.md` -- diff against the 50 currently-registered Steamworks Partner entries, lists the 30 that need to be added (plus a flagged typo: `bouny_hunter` registered on Steam should be `bounty_hunter`)
- `DOCS/STEAM_ACHIEVEMENT_ICON_PROMPTS.md` -- image-generation prompts for the 30 missing icons with tier-appropriate frames

The Steam unlock bridge is already in place: every call to `AchievementSystem.TryUnlock(player, id)` automatically calls `SteamIntegration.UnlockAchievement(id)` at line 1196. The achievement ID is used 1:1 as the Steam API Name. No additional per-achievement Steam code is needed for the 30 missing ones -- just create them on the Partner site with matching API names and the in-game unlock will fire automatically.

## Files changed (post-initial-release-notes work)

- `Scripts/Locations/DungeonLocation.cs` -- `RestorePlayerTeammates` stuck-echo cleanup; `InteractWithFeature` gold-split fix.
- `Scripts/Locations/TeamCornerLocation.cs` -- `RecruitPlayerAlly` recruited-list display + `[U]` hotkey + `UnrecruitPlayerAlly` method.
- `Scripts/Systems/BugReportSystem.cs` -- cancel hint line.
- `Scripts/Systems/AchievementSystem.cs` -- `big_spender_magic` threshold check.
- `Scripts/Core/GameConfig.cs` -- new `GetLocalizedClassName` overloads and `GetLocalizedClassNameFromString` helper. `using UsurperRemake.Systems;` added.
- `Scripts/Core/Character.cs` -- `ClassName` property rewritten to use the localized helper.
- `Scripts/Core/GameEngine.cs` -- character-select screen uses `GetLocalizedClassNameFromString` for SaveInfo.ClassName. Login-streak messages converted to `Loc.Get` (10 strings).
- `Localization/en.json` -- 12 corrupted keys fixed; 14 echo keys; 1 bug-report-cancel key; 10 login-streak keys.
- `Localization/hu.json` -- matching translations.
- `Localization/es.json` / `fr.json` / `it.json` -- 12 corrupted entries removed (so they fall through to fixed English); bug-report-cancel + login-streak keys added.
- `DOCS/STEAM_ACHIEVEMENTS.md` -- new full catalog.
- `DOCS/STEAM_ACHIEVEMENTS_MISSING.md` -- new diff list.
- `DOCS/STEAM_ACHIEVEMENT_ICON_PROMPTS.md` -- new icon generation prompts.
- `Scripts/Systems/VisualNovelDialogueSystem.cs` -- Wired `DialogueEnhancer.Enhance()` into `HandleChatTopic` (NPC topic-reply display) and `HandlePersonalQuestion` (both intimate-story and surface-level branches).
- `Tests/DialogueEnhancerTests.cs` -- New file. 12 smoke tests.
