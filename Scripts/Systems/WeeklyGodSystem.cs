using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace UsurperRemake.Systems;

/// <summary>The god recorded as the strongest for a week (God "" when no god had standing).</summary>
public readonly record struct WeeklyGodPick(int Week, string God, long Standing);

/// <summary>
/// 1.2.0 Temple gods piece 6: the strongest god of the week. At the weekly reset (online the world
/// week, DailySystemManager.WorldWeek; single-player the game day / GodStandingWeekDays, as
/// GodStandingPenalty.CurrentWeek) the god with the highest standing is recorded for the new week:
/// online in world_state (WorldStateKey), single-player in the save (Character.WeeklyGodWeek and
/// WeeklyGod). The pick reads the standing as it stands at the pick with the desecration penalties
/// of the week that just ended. Ties go to the name first in ordinal order ignoring case; a week
/// where no god has standing above 0 records no god. The record is written once per week: online
/// by one conditional upsert that only a newer week passes (SqlSaveBackend.RecordWeeklyGod), then
/// read back, so every session and process agrees; single-player by the week stamp. The followers
/// of the recorded god (players who worship it when the XP is awarded) get GodWeeklyXpBonusPct more
/// XP, applied once in TeamHQBonus.ApplyXP (XpMultiplier).
/// </summary>
public static class WeeklyGodSystem
{
    /// <summary>The world_state key of the online record.</summary>
    public const string WorldStateKey = "god_of_week";

    // Test seams, as TeamHQBonus
    internal static Func<bool> IsOnline = () => UsurperRemake.BBS.DoorMode.IsOnlineMode;
    internal static Func<SqlSaveBackend?> Backend = () => SaveSystem.Instance?.Backend as SqlSaveBackend;
    internal static Func<DateTime> UtcNow = () => DateTime.UtcNow;
    internal static Func<int> GameDay = () => DailySystemManager.Instance.CurrentDay;

    private static readonly object Gate = new();
    private static WeeklyGodPick? _online;   // this process's copy of the online record

    /// <summary>This week: online the world week, single-player the game day's week.</summary>
    public static int CurrentWeek() => GodStandingPenalty.CurrentWeek(IsOnline(), GameDay(), UtcNow());

    /// <summary>
    /// The strongest god in standings: the highest Standing above 0; a tie goes to the name first in
    /// ordinal order ignoring case. "" when no god has standing.
    /// </summary>
    public static string TopGod(IReadOnlyDictionary<string, GodStanding>? standings)
    {
        if (standings == null) return "";
        var top = standings.Values
            .Where(s => s.Standing > 0 && !string.IsNullOrWhiteSpace(s.God) && !GodRegistry.IsManwe(s.God))
            .OrderByDescending(s => s.Standing)
            .ThenBy(s => s.God, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return top.God ?? "";
    }

    /// <summary>The record's JSON value.</summary>
    public static string ToJson(WeeklyGodPick p) =>
        JsonSerializer.Serialize(new Dictionary<string, object> { ["week"] = p.Week, ["god"] = p.God ?? "", ["standing"] = p.Standing });

    /// <summary>A record read back, or null for missing or unreadable JSON.</summary>
    public static WeeklyGodPick? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("week", out var w) || !w.TryGetInt32(out int week)) return null;
            string god = r.TryGetProperty("god", out var g) && g.ValueKind == JsonValueKind.String ? g.GetString() ?? "" : "";
            long standing = r.TryGetProperty("standing", out var s) && s.TryGetInt64(out long sv) ? sv : 0;
            return new WeeklyGodPick(week, god, standing);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// Online: this week's record. The process copy serves while it is for this week; otherwise the
    /// saved record is read, and when it is for an earlier week (or missing) the pick is made from
    /// GetGodStandings(week - 1) and offered with one conditional write that only a newer week
    /// passes, then read back, so the first session to cross the boundary decides it and every other
    /// session gets the same. Null when the database cannot be read.
    /// </summary>
    public static WeeklyGodPick? OnlinePick(SqlSaveBackend backend, int week)
    {
        if (backend == null) return null;
        lock (Gate)
        {
            if (_online is { } cached && cached.Week == week) return cached;
            var saved = backend.GetWeeklyGod();
            if (saved is not { } rec || rec.Week < week)
            {
                var standings = backend.GetGodStandings(week - 1);
                string god = TopGod(standings);
                long standing = god.Length > 0 && standings.TryGetValue(god, out var s) ? s.Standing : 0;
                backend.RecordWeeklyGod(new WeeklyGodPick(week, god, standing));
                saved = backend.GetWeeklyGod();
            }
            if (saved is { } now && now.Week == week)
            {
                _online = now;
                return now;
            }
            return saved;
        }
    }

    /// <summary>
    /// Single-player: this week's record in the save, picked from the character's standings (its own
    /// Favor, the NPC roster, the ending week's desecration penalties) the first time the week is
    /// asked for; later asks that week return the saved pick unchanged.
    /// </summary>
    public static WeeklyGodPick LocalPick(Character c, int week, GodSystem? gods = null, IEnumerable<NPC>? npcs = null)
    {
        if (c.WeeklyGodWeek == week) return new WeeklyGodPick(week, c.WeeklyGod ?? "", 0);
        var standings = GodRegistry.SinglePlayerStandings(c, week - 1, gods, npcs);
        string god = TopGod(standings);
        c.WeeklyGodWeek = week;
        c.WeeklyGod = god;
        return new WeeklyGodPick(week, god, god.Length > 0 && standings.TryGetValue(god, out var s) ? s.Standing : 0);
    }

    /// <summary>This week's record for the character's world (online the shared one, single-player its save); null when unavailable.</summary>
    public static WeeklyGodPick? Current(Character? c)
    {
        try
        {
            int week = CurrentWeek();
            if (IsOnline())
            {
                var backend = Backend();
                return backend == null ? null : OnlinePick(backend, week);
            }
            return c == null || c.IsNPC ? null : LocalPick(c, week);
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogWarning("FAITH", $"Weekly god unavailable: {ex.Message}");
            return null;
        }
    }

    /// <summary>True when the player worships this week's god now.</summary>
    public static bool IsFollowerOfWeek(Character? c, WeeklyGodPick? pick, GodSystem? gods = null)
    {
        if (c == null || c.IsNPC || pick is not { } p || string.IsNullOrEmpty(p.God)) return false;
        var god = GodRegistry.GetWorshippedGod(c, gods);
        return god != null && god.Value.Name.Equals(p.God, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The XP factor for a character: 1 + GodWeeklyXpBonusPct/100 for a follower of this week's god, else 1. NPCs get 1.</summary>
    public static double XpMultiplier(Character? c)
    {
        if (c == null || c.IsNPC) return 1.0;
        return IsFollowerOfWeek(c, Current(c)) ? 1.0 + GameConfig.GodWeeklyXpBonusPct / 100.0 : 1.0;
    }

    /// <summary>Test seam: forgets this process's copy of the online record.</summary>
    internal static void ResetForTests() { lock (Gate) _online = null; }
}
