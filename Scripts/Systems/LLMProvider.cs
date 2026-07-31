using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace UsurperRemake.Systems;

/// <summary>
/// v0.64.0 Brain v2 Slice 5: LLM provider abstraction.
///
/// Slice 5 ships one implementation: OpenAI-compatible HTTP chat-completions.
/// That shape works against OpenAI directly, against Anthropic via their
/// OpenAI-compatible endpoint, against OpenRouter, and against local Ollama
/// (which supports OpenAI-compatible mode). Configured via env vars (see
/// LLMSettings).
///
/// Slice 5b can add provider-specific implementations (Anthropic native API
/// shape, local stdio-based Ollama, etc) -- the interface lets us swap.
///
/// Single instance per server process, reused across all LLM moment calls.
/// Internally uses a cached HttpClient (the standard .NET pattern) to avoid
/// socket exhaustion under burst usage.
/// </summary>
public interface ILLMProvider
{
    /// <summary>
    /// Sends a chat-completion request, returns the response (text + usage)
    /// or null on any failure (timeout, network error, budget exceeded,
    /// bad response). Callers MUST handle null and fall back to templated
    /// text -- LLM is decorative, never load-bearing.
    /// </summary>
    Task<LLMResponse?> CompleteAsync(LLMRequest request, CancellationToken ct);
}

public class LLMRequest
{
    public string SystemPrompt { get; set; } = "";
    public string UserPrompt { get; set; } = "";
    public int MaxTokens { get; set; } = 200;
    public double Temperature { get; set; } = 0.8;

    /// <summary>
    /// v0.64.1 model tiering: optional per-request model override. When null,
    /// the provider uses its default model (LLMSettings.Model). When set, the
    /// provider uses this instead -- typically used to route low-stakes
    /// decorations (dialogue mood prefixes, fork decisions, avenge flavor) to
    /// a cheaper model (LLMSettings.CheapModel, e.g. Haiku) while keeping
    /// narrative-depth generations (strategic goals, topic responses,
    /// personality summaries) on the premium default (Sonnet). Falls back to
    /// default model if the env-configured cheap model is unset.
    /// </summary>
    public string? Model { get; set; }
}

/// <summary>
/// v0.64.0 Brain v2 Slice 10: rich LLM response carrying tokens + latency
/// for the balance-dashboard LLM stats card. Callers read Text for display
/// and pass PromptTokens / CompletionTokens / TotalTokens / ResponseMs to
/// the persistence layer for telemetry.
/// </summary>
public class LLMResponse
{
    public string Text { get; set; } = "";
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
    public int ResponseMs { get; set; }

    // v0.65.10 prompt caching (native Anthropic provider only; 0 elsewhere).
    // Cache reads bill at ~10% of the input price, writes at ~125%. Both are
    // FOLDED INTO PromptTokens for telemetry/back-compat; these fields carry
    // the split for logging and future dashboard cost refinement.
    public int CacheReadTokens { get; set; }
    public int CacheWriteTokens { get; set; }
}

/// <summary>
/// Factory + cached singleton. Returns null when LLM is disabled / misconfigured /
/// not in online mode (LLMSettings.IsActive() == false). Callers check for null
/// and skip the LLM path entirely.
/// </summary>
public static class LLMProvider
{
    private static ILLMProvider? _cached;
    private static readonly object _initLock = new();

    public static ILLMProvider? Get()
    {
        if (!LLMSettings.IsActive()) return null;
        if (_cached != null) return _cached;
        lock (_initLock)
        {
            if (_cached != null) return _cached;
            try
            {
                // v0.65.10: native Anthropic Messages provider (with prompt
                // caching) when the endpoint is api.anthropic.com or the sysop
                // forces it; OpenAI-compat shape for everything else
                // (OpenAI, OpenRouter, Ollama, other proxies).
                if (LLMSettings.UseNativeAnthropic)
                {
                    _cached = new AnthropicMessagesProvider(
                        LLMSettings.Endpoint!,
                        LLMSettings.ApiKey!,
                        LLMSettings.Model!,
                        LLMSettings.TimeoutMs);
                    DebugLogger.Instance.LogInfo("LLM",
                        "Using native Anthropic Messages provider (prompt caching enabled).");
                }
                else
                {
                    _cached = new HttpChatCompletionsProvider(
                        LLMSettings.Endpoint!,
                        LLMSettings.ApiKey!,
                        LLMSettings.Model!,
                        LLMSettings.TimeoutMs);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("LLM",
                    $"Failed to construct LLM provider: {ex.Message}. LLM disabled until restart.");
                _cached = null;
            }
        }
        return _cached;
    }

    /// <summary>
    /// Test-only: clear the cached provider so the next Get() reads fresh
    /// settings. Production code never calls this.
    /// </summary>
    internal static void ResetForTests()
    {
        lock (_initLock) { _cached = null; }
    }
}

/// <summary>
/// OpenAI-compatible HTTP provider. Speaks the standard /v1/chat/completions
/// request shape. Works against any endpoint that implements it (OpenAI,
/// OpenRouter, Ollama in compat mode, Anthropic via their OpenAI proxy).
/// </summary>
internal class HttpChatCompletionsProvider : ILLMProvider
{
    private static readonly HttpClient _httpClient = new HttpClient();

    private readonly string _endpoint;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly int _timeoutMs;

    public HttpChatCompletionsProvider(string endpoint, string apiKey, string model, int timeoutMs)
    {
        _endpoint = endpoint;
        _apiKey = apiKey;
        _model = model;
        _timeoutMs = timeoutMs;
    }

    public async Task<LLMResponse?> CompleteAsync(LLMRequest request, CancellationToken ct)
    {
        // Budget gate: skip the API call entirely if we're over the daily cap.
        // Conservative estimate: requested max + roughly the prompt size in tokens
        // (4 chars per token is the usual heuristic).
        int estimatedInput = (request.SystemPrompt.Length + request.UserPrompt.Length) / 4;
        int estimatedTotal = estimatedInput + request.MaxTokens;
        if (!LLMBudget.CanSpend(estimatedTotal))
        {
            DebugLogger.Instance.LogInfo("LLM",
                $"Daily token budget exhausted ({LLMBudget.TokensUsedToday}/{LLMBudget.DailyTokenCap}). Skipping LLM call.");
            return null;
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(_timeoutMs));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            // v0.64.1 model tiering: per-request override beats provider default.
            // Callers that opt into the cheap tier pass request.Model =
            // LLMSettings.GetCheapModelOrDefault(); narrative-depth callers
            // leave it null and fall through to _model.
            string modelForRequest = !string.IsNullOrWhiteSpace(request.Model)
                ? request.Model!
                : _model;

            var requestBody = new ChatCompletionRequest
            {
                Model = modelForRequest,
                Messages = new List<ChatMessage>
                {
                    new() { Role = "system", Content = request.SystemPrompt },
                    new() { Role = "user", Content = request.UserPrompt }
                },
                MaxTokens = request.MaxTokens,
                Temperature = request.Temperature,
            };
            var json = JsonSerializer.Serialize(requestBody);

            using var httpReq = new HttpRequestMessage(HttpMethod.Post, _endpoint);
            httpReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            httpReq.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var resp = await _httpClient.SendAsync(httpReq, linkedCts.Token);
            if (!resp.IsSuccessStatusCode)
            {
                DebugLogger.Instance.LogError("LLM",
                    $"HTTP {(int)resp.StatusCode} from LLM endpoint. Falling back to template.");
                return null;
            }

            var respBody = await resp.Content.ReadAsStringAsync(linkedCts.Token);
            var parsed = JsonSerializer.Deserialize<ChatCompletionResponse>(respBody);
            if (parsed?.Choices == null || parsed.Choices.Count == 0)
            {
                DebugLogger.Instance.LogError("LLM",
                    "LLM response had no choices. Falling back to template.");
                return null;
            }

            int promptTokens = parsed.Usage?.PromptTokens ?? estimatedInput;
            int completionTokens = parsed.Usage?.CompletionTokens ?? (request.MaxTokens / 2);
            int totalTokens = parsed.Usage?.TotalTokens ?? (promptTokens + completionTokens);
            LLMBudget.RecordUsage(totalTokens);

            stopwatch.Stop();
            return new LLMResponse
            {
                Text = parsed.Choices[0].Message?.Content?.Trim() ?? "",
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                TotalTokens = totalTokens,
                ResponseMs = (int)stopwatch.ElapsedMilliseconds,
            };
        }
        catch (OperationCanceledException)
        {
            DebugLogger.Instance.LogInfo("LLM",
                $"LLM call timed out after {_timeoutMs}ms. Falling back to template.");
            return null;
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("LLM",
                $"LLM call failed: {ex.GetType().Name}: {ex.Message}. Falling back to template.");
            return null;
        }
    }

    // --- OpenAI chat-completions request/response DTOs ---
    private class ChatCompletionRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("messages")] public List<ChatMessage> Messages { get; set; } = new();
        [JsonPropertyName("max_tokens")] public int MaxTokens { get; set; }
        [JsonPropertyName("temperature")] public double Temperature { get; set; }
    }

    private class ChatMessage
    {
        [JsonPropertyName("role")] public string Role { get; set; } = "";
        [JsonPropertyName("content")] public string Content { get; set; } = "";
    }

    private class ChatCompletionResponse
    {
        [JsonPropertyName("choices")] public List<Choice>? Choices { get; set; }
        [JsonPropertyName("usage")] public TokenUsage? Usage { get; set; }
    }

    private class Choice
    {
        [JsonPropertyName("message")] public ChatMessage? Message { get; set; }
    }

    private class TokenUsage
    {
        [JsonPropertyName("prompt_tokens")] public int PromptTokens { get; set; }
        [JsonPropertyName("completion_tokens")] public int CompletionTokens { get; set; }
        [JsonPropertyName("total_tokens")] public int TotalTokens { get; set; }
    }
}

/// <summary>
/// v0.65.10: native Anthropic Messages API provider (/v1/messages) with prompt
/// caching -- the Slice 5b follow-up the v0.64.0 notes deferred ("native impl
/// for prompt caching"). Selected automatically when the endpoint host is
/// api.anthropic.com (see LLMSettings.UseNativeAnthropic).
///
/// Caching strategy: an EXPLICIT cache_control breakpoint on the system block.
/// The game's request shape is a static per-call-type system prompt plus a
/// per-NPC user prompt that varies every call; per the caching docs, a
/// breakpoint must sit on the last block that stays identical across requests
/// -- for us, the system block. (Top-level "automatic" caching would place the
/// breakpoint on the varying user message: the documented common mistake that
/// pays for a cache write on every request and never gets a read.)
///
/// When a system prompt is below the model's minimum cacheable length (1024+
/// tokens on Sonnet-class models) the API silently processes the request
/// uncached -- no error, no behavior change -- so this is strictly
/// better-or-equal to the compat path. Cache read/write token counts are
/// surfaced on LLMResponse and logged so the dashboard's LLM tab data can
/// show whether caching is actually engaging.
/// </summary>
internal class AnthropicMessagesProvider : ILLMProvider
{
    private static readonly HttpClient _httpClient = new HttpClient();
    private const string AnthropicVersion = "2023-06-01";

    private readonly string _endpoint;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly int _timeoutMs;

    public AnthropicMessagesProvider(string endpoint, string apiKey, string model, int timeoutMs)
    {
        _endpoint = NormalizeEndpoint(endpoint);
        _apiKey = apiKey;
        _model = model;
        _timeoutMs = timeoutMs;
    }

    /// <summary>
    /// Sysops configure the OpenAI-compat URL (/v1/chat/completions) per the
    /// existing docs; the native API lives at /v1/messages on the same host.
    /// Accept either form so switching providers needs no config change.
    /// </summary>
    internal static string NormalizeEndpoint(string endpoint)
    {
        if (endpoint.Contains("/chat/completions", StringComparison.OrdinalIgnoreCase))
            return endpoint.Replace("/chat/completions", "/messages", StringComparison.OrdinalIgnoreCase);
        return endpoint;
    }

    public async Task<LLMResponse?> CompleteAsync(LLMRequest request, CancellationToken ct)
    {
        // Same conservative budget gate as the compat provider.
        int estimatedInput = (request.SystemPrompt.Length + request.UserPrompt.Length) / 4;
        int estimatedTotal = estimatedInput + request.MaxTokens;
        if (!LLMBudget.CanSpend(estimatedTotal))
        {
            DebugLogger.Instance.LogInfo("LLM",
                $"Daily token budget exhausted ({LLMBudget.TokensUsedToday}/{LLMBudget.DailyTokenCap}). Skipping LLM call.");
            return null;
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(_timeoutMs));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            string modelForRequest = !string.IsNullOrWhiteSpace(request.Model)
                ? request.Model!
                : _model;

            var requestBody = new MessagesRequest
            {
                Model = modelForRequest,
                MaxTokens = request.MaxTokens,
                Temperature = request.Temperature,
                System = new List<SystemBlock>
                {
                    new()
                    {
                        Text = request.SystemPrompt,
                        CacheControl = new CacheControl(),
                    }
                },
                Messages = new List<MessageParam>
                {
                    new() { Role = "user", Content = request.UserPrompt }
                },
            };
            var json = JsonSerializer.Serialize(requestBody,
                new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });

            using var httpReq = new HttpRequestMessage(HttpMethod.Post, _endpoint);
            // Native API auths with x-api-key, not Bearer.
            httpReq.Headers.Add("x-api-key", _apiKey);
            httpReq.Headers.Add("anthropic-version", AnthropicVersion);
            httpReq.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var resp = await _httpClient.SendAsync(httpReq, linkedCts.Token);
            if (!resp.IsSuccessStatusCode)
            {
                DebugLogger.Instance.LogError("LLM",
                    $"HTTP {(int)resp.StatusCode} from Anthropic Messages endpoint. Falling back to template.");
                return null;
            }

            var respBody = await resp.Content.ReadAsStringAsync(linkedCts.Token);
            var parsed = JsonSerializer.Deserialize<MessagesResponse>(respBody);
            string? text = null;
            if (parsed?.Content != null)
            {
                foreach (var block in parsed.Content)
                {
                    if (block?.Type == "text" && !string.IsNullOrEmpty(block.Text))
                    {
                        text = block.Text;
                        break;
                    }
                }
            }
            if (text == null)
            {
                DebugLogger.Instance.LogError("LLM",
                    "Anthropic response had no text content. Falling back to template.");
                return null;
            }

            int inputTokens = parsed!.Usage?.InputTokens ?? estimatedInput;
            int cacheRead = parsed.Usage?.CacheReadInputTokens ?? 0;
            int cacheWrite = parsed.Usage?.CacheCreationInputTokens ?? 0;
            int outputTokens = parsed.Usage?.OutputTokens ?? (request.MaxTokens / 2);

            // PromptTokens carries the FULL input (uncached + cache reads +
            // cache writes) so budget accounting and the dashboard's token
            // math stay comparable with the compat provider. The token cap is
            // a token cap, not a dollar cap -- cached tokens still count.
            int promptTokens = inputTokens + cacheRead + cacheWrite;
            int totalTokens = promptTokens + outputTokens;
            LLMBudget.RecordUsage(totalTokens);

            if (cacheRead > 0 || cacheWrite > 0)
            {
                DebugLogger.Instance.LogDebug("LLM",
                    $"Prompt cache: read={cacheRead} write={cacheWrite} fresh={inputTokens} tokens.");
            }

            stopwatch.Stop();
            return new LLMResponse
            {
                Text = text.Trim(),
                PromptTokens = promptTokens,
                CompletionTokens = outputTokens,
                TotalTokens = totalTokens,
                ResponseMs = (int)stopwatch.ElapsedMilliseconds,
                CacheReadTokens = cacheRead,
                CacheWriteTokens = cacheWrite,
            };
        }
        catch (OperationCanceledException)
        {
            DebugLogger.Instance.LogInfo("LLM",
                $"LLM call timed out after {_timeoutMs}ms. Falling back to template.");
            return null;
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("LLM",
                $"LLM call failed: {ex.GetType().Name}: {ex.Message}. Falling back to template.");
            return null;
        }
    }

    // --- Anthropic Messages API request/response DTOs ---
    private class MessagesRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("max_tokens")] public int MaxTokens { get; set; }
        [JsonPropertyName("temperature")] public double Temperature { get; set; }
        [JsonPropertyName("system")] public List<SystemBlock> System { get; set; } = new();
        [JsonPropertyName("messages")] public List<MessageParam> Messages { get; set; } = new();
    }

    private class SystemBlock
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "text";
        [JsonPropertyName("text")] public string Text { get; set; } = "";
        [JsonPropertyName("cache_control")] public CacheControl? CacheControl { get; set; }
    }

    private class CacheControl
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "ephemeral";
    }

    private class MessageParam
    {
        [JsonPropertyName("role")] public string Role { get; set; } = "";
        [JsonPropertyName("content")] public string Content { get; set; } = "";
    }

    private class MessagesResponse
    {
        [JsonPropertyName("content")] public List<ContentBlock>? Content { get; set; }
        [JsonPropertyName("usage")] public MessagesUsage? Usage { get; set; }
    }

    private class ContentBlock
    {
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("text")] public string? Text { get; set; }
    }

    private class MessagesUsage
    {
        [JsonPropertyName("input_tokens")] public int InputTokens { get; set; }
        [JsonPropertyName("output_tokens")] public int OutputTokens { get; set; }
        [JsonPropertyName("cache_read_input_tokens")] public int CacheReadInputTokens { get; set; }
        [JsonPropertyName("cache_creation_input_tokens")] public int CacheCreationInputTokens { get; set; }
    }
}
