using System;

namespace UsurperRemake.Systems;

/// <summary>
/// v0.64.0 Brain v2 Slice 5: server-side LLM configuration.
///
/// Per the design constraint (memory/project_npc_brain_v2.md section 6): LLM is
/// online-mode only, self-hosters bring their own API key, the key lives on the
/// server and is never sent to clients. Slice 5 uses environment variables for
/// the simplest possible deployment story; Slice 5b will add a sysop console
/// surface for live reconfiguration.
///
/// Environment variables (all optional; if any required one is missing, LLM is
/// effectively disabled and moment generators fall through to templated text):
///   USURPER_LLM_ENABLED         "true" to enable. Default false.
///   USURPER_LLM_ENDPOINT        OpenAI-compatible chat-completions URL.
///                               Examples:
///                                 https://api.openai.com/v1/chat/completions
///                                 https://api.anthropic.com/v1/messages (with Anthropic provider)
///                                 https://openrouter.ai/api/v1/chat/completions
///                                 http://localhost:11434/v1/chat/completions (Ollama)
///   USURPER_LLM_API_KEY         Bearer-token API key. Never logged.
///   USURPER_LLM_MODEL           Model identifier (e.g., "gpt-4o-mini",
///                               "claude-haiku-4-5-20251001", "llama3.1").
///   USURPER_LLM_DAILY_TOKEN_CAP Daily cap on combined input+output tokens.
///                               Default 500000 (conservative; ~$0.50/day on Haiku).
///                               Cap is per-server, not per-player, so multi-player
///                               servers naturally share the budget.
///   USURPER_LLM_TIMEOUT_MS      Per-request timeout in ms. Default 3000.
///                               LLM moments are fire-and-forget from world-sim
///                               ticks; timing out and falling back to template
///                               is safer than hanging.
///
/// Single-player and BBS door mode skip LLM entirely (see LLMProvider.Get()).
/// </summary>
public static class LLMSettings
{
    public static bool Enabled => GetBool("USURPER_LLM_ENABLED", false);
    public static string? Endpoint => GetString("USURPER_LLM_ENDPOINT");
    public static string? ApiKey => GetString("USURPER_LLM_API_KEY");
    public static string? Model => GetString("USURPER_LLM_MODEL");
    public static long DailyTokenCap => GetLong("USURPER_LLM_DAILY_TOKEN_CAP", 500_000);
    public static int TimeoutMs => (int)GetLong("USURPER_LLM_TIMEOUT_MS", 3000);

    /// <summary>
    /// v0.64.1 model tiering: optional cheaper model identifier for low-stakes
    /// decoration calls (dialogue mood/memory/witness/state/grief/trait/faction
    /// layers, fork decisions, avenge news). When unset, all calls use Model.
    /// When set (e.g. "claude-haiku-4-5-20251001" while Model is
    /// "claude-sonnet-4-6"), routed callers pass GetCheapModelOrDefault() in
    /// LLMRequest.Model and the provider sends that model for the call.
    /// Narrative-depth callers (strategic goals, topic responses, personality
    /// summaries) leave the field null and stay on Model.
    /// </summary>
    public static string? CheapModel => GetString("USURPER_LLM_MODEL_CHEAP");

    /// <summary>
    /// Returns CheapModel if set, otherwise falls back to Model. Callers that
    /// want the cheap tier when available but tolerate the premium tier when
    /// no cheap is configured set LLMRequest.Model to this helper's return.
    /// </summary>
    public static string? GetCheapModelOrDefault() => CheapModel ?? Model;

    /// <summary>
    /// True when LLM is fully configured AND we're running in online mode.
    /// Moment generators check this and skip the LLM path entirely when false.
    /// </summary>
    public static bool IsActive()
    {
        if (!Enabled) return false;
        if (string.IsNullOrWhiteSpace(Endpoint)) return false;
        if (string.IsNullOrWhiteSpace(ApiKey)) return false;
        if (string.IsNullOrWhiteSpace(Model)) return false;
        // LLM only in online mode (per design constraint; single-player + BBS
        // never reach the LLM provider).
        if (!UsurperRemake.BBS.DoorMode.IsOnlineMode) return false;
        return true;
    }

    private static string? GetString(string name)
    {
        try
        {
            var v = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        }
        catch { return null; }
    }

    private static bool GetBool(string name, bool def)
    {
        var v = GetString(name);
        if (v == null) return def;
        return v.Equals("true", StringComparison.OrdinalIgnoreCase)
            || v.Equals("1", StringComparison.OrdinalIgnoreCase)
            || v.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || v.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    private static long GetLong(string name, long def)
    {
        var v = GetString(name);
        if (v == null) return def;
        return long.TryParse(v, out long parsed) ? parsed : def;
    }
}
