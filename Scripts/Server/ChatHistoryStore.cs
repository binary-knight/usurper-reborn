using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace UsurperRemake.Server;

/// <summary>One chat line as the player saw it: the channel, when it arrived, and the text.</summary>
public sealed record ChatHistoryEntry(string Channel, DateTime TimeUtc, string Text);

/// <summary>
/// v1.2.4: the last chat lines each account was shown (tells, gossip, say, shout, emote, guild
/// chat, and the player's own lines), for /history. Kept in server memory only: nothing here is
/// saved, logged or sent anywhere. A ring outlives its session by RetainAfterSessionEnd so a
/// reconnect picks it up; after that it is dropped.
/// </summary>
public sealed class ChatHistoryStore
{
    public const int Capacity = 50;
    public static readonly TimeSpan RetainAfterSessionEnd = TimeSpan.FromMinutes(15);

    private static ChatHistoryStore _instance = new();
    public static ChatHistoryStore Instance => _instance;

    /// <summary>Tests: start from an empty store with the given clock (UTC).</summary>
    internal static ChatHistoryStore ResetForTests(Func<DateTime>? clock = null) => _instance = new ChatHistoryStore(clock);

    private sealed class Ring
    {
        public readonly Queue<ChatHistoryEntry> Lines = new();
        public DateTime? SessionEndedUtc;
        public int Unseen;
    }

    private readonly Func<DateTime> _clock;
    private readonly ConcurrentDictionary<string, Ring> _rings = new();

    public ChatHistoryStore(Func<DateTime>? clock = null)
    {
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    private static string Norm(string key) => (key ?? "").ToLowerInvariant();

    /// <summary>Record a line shown to <paramref name="accountKey"/>. Incoming lines (from other
    /// players) count toward the missed-message hint; the player's own lines do not.</summary>
    public void RecordChatLine(string accountKey, string channel, string text, bool incoming)
    {
        if (string.IsNullOrEmpty(accountKey) || string.IsNullOrEmpty(text)) return;
        var ring = _rings.GetOrAdd(Norm(accountKey), _ => new Ring());
        lock (ring)
        {
            ring.Lines.Enqueue(new ChatHistoryEntry(channel, _clock(), text));
            while (ring.Lines.Count > Capacity) ring.Lines.Dequeue();
            if (incoming) ring.Unseen++;
        }
    }

    /// <summary>The account's lines, oldest first, optionally only one channel.</summary>
    public IReadOnlyList<ChatHistoryEntry> ReadChatHistory(string accountKey, string? channel = null)
    {
        if (!_rings.TryGetValue(Norm(accountKey), out var ring)) return Array.Empty<ChatHistoryEntry>();
        lock (ring)
        {
            return ring.Lines
                .Where(e => channel == null || string.Equals(e.Channel, channel, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    /// <summary>The player read /history: nothing is new any more.</summary>
    public void MarkChatHistoryViewed(string accountKey)
    {
        if (!_rings.TryGetValue(Norm(accountKey), out var ring)) return;
        lock (ring) ring.Unseen = 0;
    }

    /// <summary>Incoming lines since the last /history or the last hint; resets the count.</summary>
    public int TakeUnseenChatCount(string accountKey)
    {
        if (!_rings.TryGetValue(Norm(accountKey), out var ring)) return 0;
        lock (ring)
        {
            int n = ring.Unseen;
            ring.Unseen = 0;
            return n;
        }
    }

    /// <summary>A session for the account is registered. A ring whose session ended longer
    /// than RetainAfterSessionEnd ago starts empty; a younger one is kept.</summary>
    public void ChatSessionStarted(string accountKey)
    {
        PruneExpiredChatRings();
        if (_rings.TryGetValue(Norm(accountKey), out var ring))
            lock (ring) ring.SessionEndedUtc = null;
    }

    /// <summary>The account has no session any more: start its retention window.</summary>
    public void ChatSessionEnded(string accountKey)
    {
        if (_rings.TryGetValue(Norm(accountKey), out var ring))
            lock (ring) ring.SessionEndedUtc = _clock();
        PruneExpiredChatRings();
    }

    private void PruneExpiredChatRings()
    {
        var now = _clock();
        foreach (var kv in _rings)
        {
            bool expired;
            lock (kv.Value)
                expired = kv.Value.SessionEndedUtc is DateTime ended && now - ended >= RetainAfterSessionEnd;
            if (expired) _rings.TryRemove(kv);
        }
    }
}
