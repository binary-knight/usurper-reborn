# Usurper Reborn v1.0.3

Removes live AI-generated content from the game.

## What changed

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

## Deploy notes

Server redeploy. No save format change and no migration.

The `llm_usage` table is left in place on existing databases; nothing writes to
or reads from it, and dropping it is optional cleanup.

**Two systemd drop-ins carrying API keys should be deleted from the server:**
`/etc/systemd/system/usurper-mud.service.d/llm.conf` and `portraits.conf`. The
binary no longer reads either. Both keys should be rotated at the provider.
Leave `memory.conf` alone; that is the heap limit.

## Tests

**921/921 pass.** The suite dropped from 946 as the LLM test files were removed;
`BrainV2SlicesSevenEightNineTests` now covers the authored first-impression text
instead of the generator's fallback path.

## Files Changed
- `Scripts/Core/GameConfig.cs` - Version 1.0.3
- `Scripts/Systems/LLMMoments.cs`, `LLMProvider.cs`, `LLMSettings.cs`, `LLMBudget.cs` - Deleted
- `Scripts/Systems/NPCImpressionText.cs` - New; the authored Team Corner first-impression text, lifted out of the deleted generator
- `Scripts/Systems/VisualNovelDialogueSystem.cs` - 11 call sites now use their authored content directly; NPC-initiated flirt and news-comment features removed
- `Scripts/Systems/DialogueEnhancer.cs` - Three background prewarm generators and their caches removed; the localized template pools are the only path
- `Scripts/Systems/CombatEngine.cs` - Surrender fork decided by the courage heuristic; authored plea line
- `Scripts/AI/GoalSystem.cs` - Strategic-goal refresh removed; avenge news posts directly; dead handoff queue and stagger RNG removed
- `Scripts/Locations/TeamCornerLocation.cs` - Examine screen uses `NPCImpressionText`
- `Scripts/Locations/BaseLocation.cs` - Cached goal-greeting re-emit removed
- `Scripts/Core/NPC.cs` - Eleven transient LLM cache fields removed
- `Scripts/Server/MudServer.cs`, `Scripts/Systems/WorldSimService.cs`, `WorldSimulator.cs`, `JournalSystem.cs`, `Data/NPCDialogueDatabase.cs`, `Core/Character.cs` - Budget rehydrate, prune call, and stale comments removed
- `Scripts/Systems/SqlSaveBackend.cs` - `llm_usage` table, writer, and prune removed
- `web/ssh-proxy.js`, `web/balance.html` - Dashboard tab, stats endpoint, and health monitor removed
- `web/index.html`, `web/lang/*.json` - Roadmap copy corrected in all 5 languages
- `Tests/LLMTests.cs`, `Tests/AnthropicProviderTests.cs` - Deleted
- `Tests/BrainV2SlicesSevenEightNineTests.cs` - Now covers the authored first-impression text
- `DOCS/STEAM_AI_DISCLOSURE.txt` - Live-generated section is now "None"
