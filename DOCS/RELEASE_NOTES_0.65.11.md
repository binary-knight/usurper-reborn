# Release Notes - v0.65.11 (Countdown)

Thirteenth release of the Beta -> 1.0 "Countdown" cycle. One telemetry-driven tweak.

## Strategic goals move to the cheap model tier

A read of the live LLM telemetry (7 days) showed the spend was inverted relative to
player visibility: NPC strategic-goal generation was 2,346 of ~2,432 total calls
(96.5%, ~200k tokens/day, ~40% of the daily cap) and ran on the PREMIUM model --
while every player-facing moment combined (dialogue flavor, romance beats, fork
decisions, topic responses) totaled ~86 calls/week. Strategic-goal output is a tiny
structured JSON list ("Find Better Weapons", Economic, priority 0.8); the parser is
junk-tolerant and the template fallback always works. Cheap-tier work on a premium
meter.

One-line fix: `GenerateStrategicGoalsAsync` now passes
`Model = LLMSettings.GetCheapModelOrDefault()` (Haiku on the live server), joining
the other decoration-tier callers from the v0.64.1 tiering scheme. Effect at
current volume: the dominant LLM cost drops ~3x, goal-refresh latency improves,
and cap headroom roughly doubles -- reserving the premium model for the calls a
player actually reads (topic responses, personality summaries stay premium).

Telemetry context recorded with this change (for later comparison): strategic_goals
99.2% success at ~2.8s / 503 in / 109 out; total failures 19/week (all generic
nulls); NPC dungeon deaths at 0.6% of runs (down from ~14% pre-v0.65.6 -- the
predictive-flee fix holding), 41% flee / 46% win.

## Files Changed

- `Scripts/Core/GameConfig.cs` -- Version 0.65.11
- `Scripts/Systems/LLMMoments.cs` -- strategic goals request carries the cheap-tier
  model override
