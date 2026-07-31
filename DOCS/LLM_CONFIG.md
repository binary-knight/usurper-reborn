# LLM Moments: Sysop Configuration

Usurper Reborn's online server can optionally use a large language model to generate flavor
"moments" (death epitaphs, avenge news, NPC first-impression summaries, personality-driven
dialogue variety, and NPC strategic goals). This is entirely optional. If no key is configured,
the game is fully functional and every LLM moment falls through to a hand-written templated
fallback. The LLM only ever adds flavor; it never gates gameplay.

The feature is **online-mode only**. Single-player and BBS door mode skip the LLM path entirely.
The API key lives on the server and is never sent to clients or logged.

## Enabling it

Set these environment variables on the game-server process (`usurper-mud`). The recommended place
is a systemd drop-in, e.g. `/etc/systemd/system/usurper-mud.service.d/llm.conf` (mode 600, since it
holds the API key):

```ini
[Service]
Environment=USURPER_LLM_ENABLED=true
Environment=USURPER_LLM_ENDPOINT=https://api.anthropic.com/v1/chat/completions
Environment=USURPER_LLM_API_KEY=sk-your-key-here
Environment=USURPER_LLM_MODEL=claude-haiku-4-5-20251001
Environment=USURPER_LLM_DAILY_TOKEN_CAP=500000
```

Then `sudo systemctl daemon-reload && sudo systemctl restart usurper-mud`.

Note: put each `Environment=` directive on its own line. A past incident had them space-separated
on one line, which systemd parsed as a single giant variable, so `USURPER_LLM_ENABLED`'s value was
not literally `"true"` and every call recorded `llm_disabled`.

## Variables

| Variable                       | Required | Default    | Notes |
|--------------------------------|----------|------------|-------|
| `USURPER_LLM_ENABLED`          | yes      | `false`    | Must be exactly `true` to enable. |
| `USURPER_LLM_ENDPOINT`         | yes      | (none)     | OpenAI-compatible chat-completions URL. Works against OpenAI, the Anthropic OpenAI-compat / native endpoint, OpenRouter, or a local Ollama. |
| `USURPER_LLM_API_KEY`          | yes      | (none)     | Bearer token. Never logged. |
| `USURPER_LLM_MODEL`            | yes      | (none)     | Model id, e.g. `gpt-4o-mini`, `claude-haiku-4-5-20251001`, `llama3.1`. |
| `USURPER_LLM_DAILY_TOKEN_CAP`  | no       | `500000`   | Per-server daily cap on combined input+output tokens. Shared across all players; when reached, further calls fall back to templates until the UTC day rolls over. |
| `USURPER_LLM_TIMEOUT_MS`       | no       | `3000`     | Per-request timeout. Most moments are fire-and-forget; timing out and using the template is safer than hanging a world-sim tick. Recommended: `10000` when strategic goals are in play -- historical successful goal calls averaged 2.4s, so the 3s default clips the tail (v0.65.6 operational note). |
| `USURPER_LLM_MODEL_CHEAP`      | no       | (unset)    | Optional cheaper model for low-stakes decoration (dialogue layers, fork decisions, avenge news). When set, those callers use it while narrative-depth callers stay on `USURPER_LLM_MODEL`. |
| `USURPER_LLM_NATIVE_ANTHROPIC` | no       | (auto)     | v0.65.10: force the native Anthropic Messages provider on (`true`) or off (`false`). Unset = auto-detect: on when the endpoint host is `api.anthropic.com`. The native provider adds prompt caching (a `cache_control` breakpoint on the system prompt), so repeated calls sharing a system prompt read it from cache at ~10% of the input price. Either endpoint form works -- `/v1/chat/completions` is rewritten to `/v1/messages` automatically. Non-Anthropic endpoints keep the OpenAI-compat shape. |

If any of the four required variables is missing, the LLM is treated as disabled and everything
falls back to templates. There is no partial-configuration failure mode.

## Cost

At full population the five-plus moment types come to roughly 40k tokens/day, which is well under
the default 500k cap (about 12x headroom) and typically under a dollar a day on a small model. The
cap is a hard ceiling: once the day's tokens are spent, calls fall back to templates rather than
continuing to spend.

## Monitoring

Every LLM attempt (success or failure) is recorded in the `llm_usage` SQLite table (moment type,
tokens, latency, rendered text, failure reason). The sysop dashboard's "LLM Moments" tab
(`web/balance.html`) surfaces call counts, success rate, token usage against the cap, per-moment-type
breakdown, failures, and recent renders. The table is pruned to the last 30 days by the world-sim
maintenance pass, so it stays bounded.

## Output hygiene

Model output is sanitized before it reaches players: wrapping quotes and "Here's the news flash:"
style preambles are stripped, length is capped, and Unicode punctuation (em-dashes, en-dashes,
ellipsis, curly quotes) is collapsed to ASCII to match the project's player-facing text rules. The
system prompts also instruct the model to use ASCII punctuation only.
