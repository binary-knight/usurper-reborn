# Usurper Reborn v1.0.3

Removes live AI-generated content, adds a persistent dungeon auto-map, fixes
four player-reported bugs, and lands a combat-systems audit.

## Removed: live AI-generated content

The runtime language-model layer is gone. It is not disabled or gated behind a
setting; it is deleted. The game no longer contacts any AI service in any mode,
and there is no API key for a server operator to configure.

Removed: the provider, settings, and budget systems, the moment generators, the
`llm_usage` telemetry table and its writer, the balance dashboard's tab and
endpoint, the background health monitor, eleven per-NPC caches, and every call
site across nine files.

## What players will notice

NPC dialogue, greetings, topic replies, news asides, epitaphs and first
impressions now come from the authored pools rather than being written per-NPC
at runtime.

Two additions that existed only because of the generator are gone: NPCs starting
a flirt on their own, and NPCs volunteering a comment about a recent news entry.

Two others were kept, by lifting their authored text out of the generator that
had been holding it as a fallback:

- the Team Corner first impression, now in `NPCImpressionText`
- the news entry posted when an NPC completes a family revenge

**Nothing lost a decision path.** Marriage proposals, confessions, walking out of
a conversation, and a beaten NPC choosing to beg or die are all still branching
outcomes. They are now decided by the personality heuristics that were already
the fallback, rather than being arbitrated live.

## Why it was safe to remove

Every call site already had a hand-written fallback, because the feature was
built online-only from the start and single player has always run without it.
Removing the generator means every site now takes the path single player has
always taken. That is a well-exercised configuration, not a new one.

## Website

Player-facing copy on the roadmap claimed the NPCs "wear painted portraits."
That became false when the generated portraits were removed in v1.0.2, and it
was wrong in all five languages. Corrected.

The claim that the population "runs on real AI brains" was kept: that is the
goal, memory, and personality decision system, which is still in the game and is
ordinary game AI rather than generated content.

## Steam

The AI content disclosure is updated in `DOCS/STEAM_AI_DISCLOSURE.txt`. The
live-generated section now reads "None," and the file also carries answers to
Steam's follow-up questions about generated code, user-generated copyrighted
material, and indemnification services.

## New: persistent dungeon auto-map

A compact floor map that redraws with every room view instead of only when you
press `[M]`. Toggle it with `[P]` in the dungeon map footer or `[M]` in the `~`
preferences menu; the setting is saved with your character.

Visual clients only. The BBS/compact room view has a 25-row budget it cannot
spend on a map, screen-reader players have the navigator, and the Electron client
draws its own graphical overlay, so the option is only offered where it applies.

## Player-reported fixes

**Team membership was invisible to the other player.** Reported online: two
players on the same team each saw a different roster. One player's screen listed
the other as a member; the other player's screen showed themselves alone.
Recreating the team did not help.

Player team membership is stored in each player's own save, and the roster screen
builds its list by querying that field across all players. Joining or creating a
team only set it in memory, so until that player's next autosave every *other*
player's roster query still read their previous value. That produces exactly the
reported asymmetry: the friend's roster sees you (you were already saved), yours
does not see them (they were not). Remaking the team did not help because the
remake had the identical gap.

Confirmed against live data: both players held identical team names and the
roster query returned both rows by the time it was inspected, so the records had
converged on their own once autosaves landed. The window before that was the
whole bug. All four team transitions (create, both join paths, leave) now write
immediately.

**Character HP could go negative.** Reported as "HP: 1/-9" with a healer
insisting the character was already at full health. Stacked penalties (cursed
gear draining Constitution on a low-level character) could push maximum HP to
zero or below, which inverts every "is this character at full health" check and
made the Last Stand rescue fire every round, so combat could neither be won nor
lost. Maximum HP now floors at 1 and maximum mana at 0, in both the stat
recalculation and the legacy item-removal path.

**Auto-combat could hang on MUD connections.** The check for "has the player
pressed a key" used a call that blocks on network-backed streams, so auto-combat
froze for any session whose input was wrapped for connection handling. It now
tests for readable data without blocking, and the input flush drains raw bytes
only while data is genuinely available.

## Combat systems audit

All eight critical findings and fourteen of fifteen high-severity findings.

**Exploits closed**
- The Escape spell in PvP resolved as a *win*, which let a player claim a throne,
  win an arena match, or kill a sleeping player without fighting. It now resolves
  as an escape or a stalemate.
- Killing a sleeping player duplicated items: the theft wrote back the victim's
  whole pre-combat save, reverting the gold deduction that had already been
  applied. The write order is now theft first, deduction second.
- A permanent death no longer runs the victory pipeline afterward, so a character
  cannot be resurrected by the autosave that followed their own erasure.

**Crashes and stuck states**
- Combat now works from a snapshot of the party roster, so another player joining
  or disconnecting mid-fight can no longer abort the battle.
- Grouped followers no longer get prompted for targeting on a stream the group
  loop owns, which could deadlock the party.
- Losing a PvP fight leaves you at 1 HP rather than walking around at zero.
- The Manwe battle flag is per-session, so one player's boss fight cannot alter
  another player's combat.

**Combat behaving as described**
- Defend actually halves incoming damage in multi-monster fights and in both PvP
  directions, and expires at the end of the round.
- Power Attack and Precise Strike no longer subtract armor twice.
- Boss damage-over-time effects, taunts, and charm and confusion timers tick once
  per round regardless of how many times the boss attacks.
- Bosses no longer announce an ability they then do not use, and now draw from
  the abilities matching their current phase. Previously only first-phase
  abilities could ever be selected, so every god's later-phase kit was unreachable.
- Named god abilities now go through the same defensive layers as ordinary
  attacks (difficulty scaling, solo adjustment, Shield Wall, early-round caps).
- Self-damaging abilities no longer apply their cost twice.
- Status effects in PvP no longer tick twice per turn, and grouped players are
  correctly stunned when stunned.
- AI opponents' spells and abilities deal their damage and respect Defend.
- A teammate killed by poison or bleed now routes through the real death handling
  instead of quietly vanishing.
- Teammate spellcasting uses one mana source of truth: no double deduction, and
  they no longer try to cast heals they never learned.

**Deferred by design:** players do not roll to-hit on the live combat path, so
the related finding does not apply.

## Review follow-ups

Four fixes found while reviewing the audit before merge.

The audit added eighteen effects to the list that tells the damage system "this
ability applies its own damage, do not apply it again." Correct for the seven
that hit multiple targets. But eleven hit a single target, and that flag skips
the entire shared damage block, which is where critical hits, the guaranteed
critical from stealth, the bonus against Marked targets, damage reporting, and
weapon enchantment procs all live.

So eleven prestige abilities silently stopped critting, silently wasted the
guaranteed critical from Umbral Step and Temporal Feint, and silently stopped
proccing Lifedrinker, Siphon and elemental enchants. Nothing failed and nothing
crashed, which is why it needed catching before release rather than after.
Reverting the list would have brought back the double damage, so those eleven now
run through a shared pipeline that applies everything exactly once.

Also fixed: the guard that stops a permanently-dead character running the victory
pipeline read a flag that only the single-player death path ever set, so it did
nothing in online play, which is the only place groups exist and therefore the
only place it mattered. A god ability could drive HP negative before the clamp.
The auto-map preference was offered to players whose display never shows a map.

## Deploy notes

Server redeploy. The only save-format change is the additive dungeon auto-map
preference, which defaults to off on existing characters. No migration.

The `llm_usage` table is left in place on existing databases; nothing writes to
or reads from it, and dropping it is optional cleanup.

**Two systemd drop-ins carrying API keys should be deleted from the server:**
`/etc/systemd/system/usurper-mud.service.d/llm.conf` and `portraits.conf`. The
binary no longer reads either. Both keys should be rotated at the provider.
Leave `memory.conf` alone; that is the heap limit.

## Tests

**929/929 pass.** The LLM test files were removed with the generator they covered
(`BrainV2SlicesSevenEightNineTests` now exercises the authored first-impression
text instead), and new coverage was added for the maximum-HP floor, the
item-removal clamp, the auto-map preference round-trip, the ability damage
pipeline, and team membership persistence.

The pipeline and team tests are structural rather than behavioural on purpose:
both regressions were invisible to ordinary tests. Nothing threw, nothing was
corrupted, the numbers were just quietly wrong.

## Files Changed
- `Scripts/Core/GameConfig.cs` - Version 1.0.3
- `Scripts/Systems/LLMMoments.cs`, `LLMProvider.cs`, `LLMSettings.cs`, `LLMBudget.cs` - Deleted
- `Scripts/Systems/NPCImpressionText.cs` - New; the authored Team Corner first-impression text, lifted out of the deleted generator
- `Scripts/Systems/VisualNovelDialogueSystem.cs` - 11 call sites now use their authored content directly; NPC-initiated flirt and news-comment features removed
- `Scripts/Systems/DialogueEnhancer.cs` - Three background prewarm generators and their caches removed; the localized template pools are the only path
- `Scripts/Systems/CombatEngine.cs` - Combat audit fixes; new `ApplyHandlerAbilityDamage` shared pipeline; permadeath flag stamped on the online path; HP clamped at two boss/monster ability sites; surrender fork decided by the courage heuristic with an authored plea line
- `Scripts/AI/GoalSystem.cs` - Strategic-goal refresh removed; avenge news posts directly; dead handoff queue and stagger RNG removed
- `Scripts/Locations/TeamCornerLocation.cs` - New `PersistTeamMembershipChange`, called at all four team transitions; examine screen uses `NPCImpressionText`
- `Scripts/Locations/BaseLocation.cs` - Auto-map preference, gated to visual clients; cached goal-greeting re-emit removed
- `Scripts/Core/NPC.cs` - Eleven transient LLM cache fields removed
- `Scripts/Server/MudServer.cs`, `Scripts/Systems/WorldSimService.cs`, `WorldSimulator.cs`, `JournalSystem.cs`, `Data/NPCDialogueDatabase.cs`, `Core/Character.cs` - Budget rehydrate, prune call, and stale comments removed
- `Scripts/Systems/SqlSaveBackend.cs` - Roster exclusion parameter renamed to `excludeDisplayName` to match what it compares; `llm_usage` table, writer, and prune removed
- `web/ssh-proxy.js`, `web/balance.html` - Dashboard tab, stats endpoint, and health monitor removed
- `web/index.html`, `web/lang/*.json` - Roadmap copy corrected in all 5 languages
- `Tests/LLMTests.cs`, `Tests/AnthropicProviderTests.cs` - Deleted
- `Tests/BrainV2SlicesSevenEightNineTests.cs` - Now covers the authored first-impression text
- `DOCS/STEAM_AI_DISCLOSURE.txt` - Live-generated section is now "None"
- `Scripts/Core/Character.cs` - Maximum HP floors at 1, maximum mana at 0; `DungeonAutoMap` preference
- `Scripts/Core/Items.cs` - Legacy item-removal path clamps the same way
- `Scripts/UI/TerminalEmulator.cs` - Non-blocking input check; raw-byte input flush
- `Scripts/Locations/DungeonLocation.cs` - Auto-map renderer and toggle
- `Scripts/Locations/InnLocation.cs`, `DormitoryLocation.cs` - Sleeper-kill write ordering; attacker no longer left at 0 HP
- `Scripts/Locations/AnchorRoadLocation.cs`, `Scripts/Systems/OldGodBossSystem.cs`, `Scripts/Core/GameEngine.cs` - Audit fixes
- `Scripts/Systems/SaveDataStructures.cs`, `SaveSystem.cs` - Auto-map preference round-trip
- `Localization/*.json` - New auto-map and combat strings (5 languages)
- `Tests/CharacterTests.cs`, `SaveRoundTripTests.cs`, `AbilityDamagePipelineTests.cs`, `TeamMembershipPersistenceTests.cs` - New coverage
