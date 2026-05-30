using System;
using System.Threading;

namespace UsurperRemake.Systems;

/// <summary>
/// v0.64.0 Brain v2 Slice 5: per-server LLM token budget tracking.
///
/// Tracks combined input+output tokens consumed across the current UTC day.
/// Daily reset triggered by date rollover detection (cheap; checked before
/// each TryReserve call). When the daily cap is hit, moment generators fall
/// back to templated text until midnight UTC.
///
/// Slice 5 uses an in-memory counter. Server restart resets the counter to
/// zero (acceptable for MVP -- restart frees up the daily budget; if anyone
/// abuses it by restarting, the per-server cap is already conservative).
/// Slice 5b will persist to SQLite via a `llm_usage` table for true daily
/// accounting across restarts.
///
/// Thread-safe via Interlocked on the long counter. The date-rollover check
/// uses a lock to avoid a race between two callers crossing midnight.
/// </summary>
public static class LLMBudget
{
    private static readonly object _lock = new();
    private static long _tokensUsedToday = 0;
    private static DateTime _currentDayUtc = DateTime.UtcNow.Date;

    /// <summary>
    /// Current tokens-used counter for today (UTC). Includes input + output.
    /// </summary>
    public static long TokensUsedToday
    {
        get
        {
            RollDateIfNeeded();
            return Interlocked.Read(ref _tokensUsedToday);
        }
    }

    /// <summary>
    /// Daily cap from settings.
    /// </summary>
    public static long DailyTokenCap => LLMSettings.DailyTokenCap;

    /// <summary>
    /// Remaining tokens before hitting the cap. Negative if already over
    /// (which shouldn't happen with normal flow but is defensive).
    /// </summary>
    public static long Remaining => DailyTokenCap - TokensUsedToday;

    /// <summary>
    /// Try to reserve a chunk of tokens for an upcoming LLM call. Returns true
    /// if the budget can accommodate the estimate; false if would exceed.
    /// Doesn't decrement on its own -- caller commits via RecordUsage after
    /// the call completes (so we can record actual usage, not the estimate).
    /// </summary>
    public static bool CanSpend(int estimatedTokens)
    {
        if (estimatedTokens < 0) return true;
        RollDateIfNeeded();
        long current = Interlocked.Read(ref _tokensUsedToday);
        return current + estimatedTokens <= DailyTokenCap;
    }

    /// <summary>
    /// Commit actual usage after an LLM call returns. Sum of input + output
    /// tokens from the API response (prompt_tokens + completion_tokens in
    /// OpenAI shape).
    /// </summary>
    public static void RecordUsage(int totalTokens)
    {
        if (totalTokens <= 0) return;
        RollDateIfNeeded();
        Interlocked.Add(ref _tokensUsedToday, totalTokens);
    }

    /// <summary>
    /// Manual reset (sysop console command in Slice 5b). For Slice 5,
    /// exposed for tests and for the date-rollover path.
    /// </summary>
    public static void Reset()
    {
        lock (_lock)
        {
            Interlocked.Exchange(ref _tokensUsedToday, 0);
            _currentDayUtc = DateTime.UtcNow.Date;
        }
    }

    /// <summary>
    /// Roll the counter to zero if the UTC day has changed since the last
    /// call. Cheap (date comparison) for most calls; locks only on the
    /// rare rollover event.
    /// </summary>
    private static void RollDateIfNeeded()
    {
        var today = DateTime.UtcNow.Date;
        if (today != _currentDayUtc)
        {
            lock (_lock)
            {
                if (today != _currentDayUtc)
                {
                    _currentDayUtc = today;
                    Interlocked.Exchange(ref _tokensUsedToday, 0);
                }
            }
        }
    }
}
