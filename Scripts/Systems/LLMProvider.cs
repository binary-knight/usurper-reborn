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
                _cached = new HttpChatCompletionsProvider(
                    LLMSettings.Endpoint!,
                    LLMSettings.ApiKey!,
                    LLMSettings.Model!,
                    LLMSettings.TimeoutMs);
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

            var requestBody = new ChatCompletionRequest
            {
                Model = _model,
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
