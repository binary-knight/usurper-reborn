# Release Notes - v0.65.10 (Countdown)

Twelfth release of the Beta -> 1.0 "Countdown" cycle. One focused feature: the **native
Anthropic Messages API provider with prompt caching** -- the Slice 5b follow-up the
v0.64.0 notes explicitly deferred ("Slice 5b can add native impl for prompt caching").

## Native Anthropic provider + prompt caching

The LLM moments/goals/dialogue pipeline previously spoke only the OpenAI-compatible
`/v1/chat/completions` shape, including against Anthropic's compat endpoint -- which
meant no access to prompt caching. New `AnthropicMessagesProvider` speaks the native
`/v1/messages` API and is selected automatically when the configured endpoint host is
`api.anthropic.com` (override with `USURPER_LLM_NATIVE_ANTHROPIC=true/false`; either
endpoint path form works, `/v1/chat/completions` is rewritten to `/v1/messages`).

Caching design: an **explicit `cache_control` breakpoint on the system block**. The
game's request shape is a static per-call-type system prompt plus a per-NPC user prompt
that varies every call -- per the caching docs, the breakpoint must sit on the last
block that stays identical across requests, which for us is the system prompt.
(Top-level "automatic" caching would place the breakpoint on the varying user message:
the documented common mistake that pays for a cache write on every request and never
gets a read.) Repeated calls sharing a system prompt within the 5-minute TTL read it
from cache at ~10% of the input price. System prompts below the model's minimum
cacheable length (1024+ tokens on Sonnet-class models) are silently processed uncached
-- no error, no behavior change -- so the native path is strictly better-or-equal.

Honest expectations: most of the game's system prompts are currently SHORT (a few
hundred tokens), so caching will engage only for the larger call types until prompts
are restructured around a shared cacheable preamble. The point of this release is the
plumbing plus visibility: cache read/write token counts are surfaced on `LLMResponse`
(`CacheReadTokens` / `CacheWriteTokens`), folded into `PromptTokens` for dashboard
back-compat, and logged (`Prompt cache: read=X write=Y fresh=Z`) so the dashboard's
LLM data shows whether caching is actually paying before any prompt-restructuring work
is invested.

Everything else is unchanged: same budget gate, same timeout, same null-on-any-failure
contract (templates always work), same model tiering (`USURPER_LLM_MODEL_CHEAP` rides
through per-request), same telemetry wrapping. Non-Anthropic endpoints (OpenAI,
OpenRouter, Ollama) keep the OpenAI-compat provider untouched.

## Files Changed

- `Scripts/Core/GameConfig.cs` -- Version 0.65.10
- `Scripts/Systems/LLMProvider.cs` -- `AnthropicMessagesProvider` (native /v1/messages,
  x-api-key + anthropic-version headers, system-block cache breakpoint, cache-aware
  usage parsing); provider selection in `LLMProvider.Get()`; `LLMResponse` gains
  `CacheReadTokens` / `CacheWriteTokens`
- `Scripts/Systems/LLMSettings.cs` -- `UseNativeAnthropic` (env override + host
  auto-detect)
- `DOCS/LLM_CONFIG.md` -- `USURPER_LLM_NATIVE_ANTHROPIC` documented
- `Tests/AnthropicProviderTests.cs` -- **NEW** -- 5 tests pinning endpoint
  normalization and provider-selection routing
