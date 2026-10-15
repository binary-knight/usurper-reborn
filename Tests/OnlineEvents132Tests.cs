using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.2: online world events. The world sim's daily reset passes the world day, so events count down and
/// roll online; the world events are saved right after that reset; a session's save keeps the event effects
/// and the global flags; and a world boss kill never leaves more than one Realm Celebrates, whether it is
/// forced again or several are restored. Single player is unchanged. In memory, on a temp database only.
/// </summary>
[Collection("SharedGameSingletons")]
public class OnlineEvents132Tests : IDisposable
{
    private const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
    private static readonly WorldEventSystem.EventType Victory = WorldEventSystem.EventType.WorldBossVictory;

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-oeb-{Guid.NewGuid():N}.db");
    private readonly SqlSaveBackend _db;
    private readonly List<string> _news = new();

    public OnlineEvents132Tests()
    {
        _db = new SqlSaveBackend(_path);
        WorldEventSystem.Instance.ClearAllEvents();   // the same-day guard starts at 0 whatever ran before
        NewsSystem.Instance.SetCatchUpBuffer(_news);  // news lines are kept here, not written anywhere
    }

    public void Dispose()
    {
        NewsSystem.Instance.ClearCatchUpBuffer();
        WorldEventSystem.Instance.ClearAllEvents();
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    // ---- helpers ----

    private static List<WorldEventSystem.WorldEvent> Active() => WorldEventSystem.Instance.GetActiveEvents();

    private static int Celebrations() => Active().Count(e => e.Type == Victory);

    private static void Invoke(object target, string method) =>
        target.GetType().GetMethod(method, Priv)!.Invoke(target, null);

    private static Task InvokeAsync(object target, string method) =>
        (Task)target.GetType().GetMethod(method, Priv)!.Invoke(target, null)!;

    private static OnlineStateManager Osm(SqlSaveBackend db) =>
        (OnlineStateManager)Activator.CreateInstance(typeof(OnlineStateManager), Priv, null, new object[] { db, "oeb_session" }, null)!;

    private static WorldEventData Stored(WorldEventSystem.EventType type, int days, params (string Key, object Value)[] extra)
    {
        var data = new WorldEventData
        {
            Id = Guid.NewGuid().ToString(), Type = type.ToString(), Title = type.ToString(), Description = type.ToString(),
            Parameters = new Dictionary<string, object> { ["DaysRemaining"] = days, ["StartDay"] = 1 },
        };
        foreach (var (k, v) in extra) data.Parameters[k] = v;
        return data;
    }

    private static string Source(string folder, string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Scripts"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "Scripts", folder, file));
    }

    private static string MethodBody(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, $"{signature} is in the source");
        int open = source.IndexOf('{', start), depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source.Substring(open, i - open + 1);
        }
        throw new InvalidOperationException($"no end to {signature}");
    }

    // ---- T1: the world daily reset counts events down and ends The Realm Celebrates ----

    [Fact]
    public void TheWorldDailyReset_EndsTheRealmCelebrates_AndStartsAtMostOneOtherEvent()
    {
        WorldEventSystem.Instance.ForceEvent(Victory, DateTime.UtcNow.DayOfYear);
        Celebrations().Should().Be(1);

        Invoke(new WorldSimService(_db), "ProcessWorldDailyReset");

        Celebrations().Should().Be(0, "the reset counts the one-day celebration down to its end");
        Active().Count.Should().BeLessThanOrEqualTo(1, "the day's roll starts at most one event, and never this one");
    }

    // ---- T2: the same-day guard (pin) ----

    [Fact]
    public async Task TwoResetsOnOneWorldDay_CountAnEventDownOnce()
    {
        var events = WorldEventSystem.Instance;
        events.RestoreFromSaveData(new List<WorldEventData> { Stored(WorldEventSystem.EventType.KingPardon, 5) }, 0);
        int day = DailySystemManager.WorldDayAt(DateTime.UtcNow);
        day.Should().BeGreaterThan(0);

        await events.ProcessDailyEvents(day);
        await events.ProcessDailyEvents(day);

        Active().Single(e => e.Type == WorldEventSystem.EventType.KingPardon).DaysRemaining.Should().Be(4);
    }

    // ---- T3: the reset saves the world events at once ----

    [Fact]
    public async Task TheDailyReset_SavesTheWorldEventsAtOnce()
    {
        var sim = new WorldSimService(_db);
        WorldEventSystem.Instance.ForceEvent(Victory, DateTime.UtcNow.DayOfYear);
        await InvokeAsync(sim, "SaveWorldEventsState");
        var osm = Osm(_db);
        (await osm.LoadSharedWorldEvents())!.Count(d => d.Type == Victory.ToString()).Should().Be(1, "the row holds the celebration before the reset");

        Invoke(sim, "CheckWorldDailyReset");

        (await _db.LoadWorldState("last_daily_reset")).Should().NotBeNullOrEmpty("the reset ran and was recorded");
        var row = (await osm.LoadSharedWorldEvents())!;
        row.Count(d => d.Type == Victory.ToString()).Should().Be(0, "a login now restores the state after the roll");
        row.Count(d => d.Type != "GlobalState").Should().BeLessThanOrEqualTo(1);
    }

    // ---- T4: a session's save keeps the effects and the flags ----

    [Fact]
    public async Task ASessionSave_KeepsEventEffectsAndFlags()
    {
        var events = WorldEventSystem.Instance;
        events.RestoreFromSaveData(new List<WorldEventData>
        {
            new WorldEventData { Id = "g", Type = "GlobalState", Title = "Global State",
                Parameters = new Dictionary<string, object> { ["PlaguActive"] = true, ["WarActive"] = false, ["FestivalActive"] = false } },
            Stored(WorldEventSystem.EventType.EconomyRecession, 6, ("Effect_price", 1.3f), ("Effect_gold", 0.8f)),
        }, 0);
        events.PlaguActive.Should().BeTrue();
        events.GlobalPriceModifier.Should().BeApproximately(1.3f, 0.001f);

        var osm = Osm(_db);
        var list = (List<WorldEventData>)typeof(OnlineStateManager).GetMethod("SerializeCurrentWorldEvents", Any)!.Invoke(osm, null)!;
        await osm.SaveSharedWorldEvents(list);
        events.ClearAllEvents();
        events.RestoreFromSaveData((await osm.LoadSharedWorldEvents())!, 0);

        events.PlaguActive.Should().BeTrue("the plague flag survives a session's save");
        events.GlobalPriceModifier.Should().BeApproximately(1.3f, 0.001f, "the price effect survives");
        events.GlobalGoldModifier.Should().BeApproximately(0.8f, 0.001f, "the gold effect survives");
        Active().Single().DaysRemaining.Should().Be(6);
    }

    [Fact]
    public void ASessionsSharedSave_WritesTheWorldEventsThroughThatSerializer()
    {
        var body = MethodBody(Source("Systems", "OnlineStateManager.cs"), "public async Task SaveAllSharedState()");
        body.Should().Contain("var events = SerializeCurrentWorldEvents();").And.Contain("await SaveSharedWorldEvents(events);");
    }

    // ---- T5: single player is unchanged (source pin) ----

    [Fact]
    public void SinglePlayer_StillRollsWorldEventsOnItsOwnDay_AndOnlinePlayersDoNot()
    {
        var daily = Source("Systems", "DailySystemManager.cs").Replace("\r\n", "\n");
        MethodBody(daily, "private async Task ProcessDailyEvents()")
            .Should().Contain("await WorldEventSystem.Instance.ProcessDailyEvents(currentDay);");
        daily.Should().Contain("if (DoorMode.IsOnlineMode)\n            await ProcessPlayerDailyEvents();\n        else\n            await ProcessDailyEvents();");
        MethodBody(daily, "private async Task ProcessPlayerDailyEvents()")
            .Should().NotContain("WorldEventSystem.Instance.ProcessDailyEvents", "online players never roll the world's events");
    }

    // ---- the dedupe: one Realm Celebrates at most ----

    [Fact]
    public void TwoBossKills_LeaveOneRealmCelebrates()
    {
        var events = WorldEventSystem.Instance;
        events.ForceEvent(Victory, 100);
        int headlines = _news.Count;
        events.ForceEvent(Victory, 101);

        Celebrations().Should().Be(1, "a second kill refreshes the celebration that runs");
        Active().Single().StartDay.Should().Be(101, "the refresh takes the later kill's day");
        Active().Single().DaysRemaining.Should().Be(1, "it still lasts to the next daily reset, not longer");
        events.GlobalXPModifier.Should().BeApproximately(1.1f, 0.001f, "the bonus does not multiply");
        _news.Count.Should().Be(headlines, "no second celebration headline");
    }

    [Fact]
    public void ThreeStoredCelebrations_RestoreAsOne()
    {
        var events = WorldEventSystem.Instance;
        events.RestoreFromSaveData(new List<WorldEventData>
        {
            Stored(Victory, 1, ("Effect_xp", 1.1f)), Stored(Victory, 2, ("Effect_xp", 1.1f)), Stored(Victory, 1, ("Effect_xp", 1.1f)),
        }, 0);

        Celebrations().Should().Be(1, "several stored celebrations collapse to one");
        Active().Single().DaysRemaining.Should().Be(2, "the one kept has the most days left");
        events.GlobalXPModifier.Should().BeApproximately(1.1f, 0.001f);
    }

    [Fact]
    public void OtherForcedEvents_AreNotDeduped()
    {
        var events = WorldEventSystem.Instance;
        events.ForceEvent(WorldEventSystem.EventType.MerchantCaravan, 7);
        events.ForceEvent(WorldEventSystem.EventType.MerchantCaravan, 14);
        Active().Count(e => e.Type == WorldEventSystem.EventType.MerchantCaravan).Should().Be(2, "single player's forced events behave as before");
    }
}
