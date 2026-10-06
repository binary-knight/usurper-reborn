using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake.BBS;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.8 piece Q1: SaveSystem.AutoSave times the save's build and its write apart and, when either takes
/// longer than the threshold (2000 ms, an internal seam), logs one SAVE_SLOW warning with the save key,
/// the build ms, the write ms and the caller. Nothing else about the save changes. Saves run through
/// SaveSystem.AutoSave on a real SQL backend in a temp folder, online as a self hosted server; the lock
/// test holds a real exclusive lock from a second connection.
/// </summary>
[Collection("SharedGameSingletons")]
public class SaveSlow128Tests : IDisposable
{
    private const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags PrivStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly FieldInfo SaveField = typeof(SaveSystem).GetField("instance", PrivStatic)!;
    private static readonly FieldInfo DoorField = typeof(DoorMode).GetField("_sessionInfo", PrivStatic)!;
    private static readonly FieldInfo OnlineField = typeof(DoorMode).GetField("_onlineMode", PrivStatic)!;

    private const long Gold = 7_777_777;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"usurper-saveslow-{Guid.NewGuid():N}");
    private readonly object? _saveBefore = SaveField.GetValue(null);
    private readonly object? _doorBefore = DoorField.GetValue(null);
    private readonly object? _onlineBefore = OnlineField.GetValue(null);
    private readonly int _thresholdBefore = SaveSystem.SlowSaveThresholdMs;
    private readonly SqlSaveBackend _db;
    private readonly string _key;
    private readonly LogCapture _log;

    public SaveSlow128Tests()
    {
        Directory.CreateDirectory(_dir);
        _db = new SqlSaveBackend(Path.Combine(_dir, "game.db"));
        SaveSystem.InitializeWithBackend(_db);
        // a self hosted server: online, no door
        DoorField.SetValue(null, null);
        OnlineField.SetValue(null, true);
        DoorMode.IsOnlineMode.Should().BeTrue();
        _key = DoorMode.GetPlayerName().ToLowerInvariant();
        _log = new LogCapture();
    }

    public void Dispose()
    {
        _log.Dispose();
        SaveSystem.SlowSaveThresholdMs = _thresholdBefore;
        SaveField.SetValue(null, _saveBefore);
        DoorField.SetValue(null, _doorBefore);
        OnlineField.SetValue(null, _onlineBefore);
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    // ---------- helpers ----------

    /// <summary>A timer-less logger swapped in as the instance, so nothing is flushed away before the test reads it.</summary>
    private sealed class LogCapture : IDisposable
    {
        private static readonly FieldInfo InstanceField = typeof(DebugLogger).GetField("_instance", PrivStatic)!;
        private readonly object? _previous;
        private readonly ConcurrentQueue<string> _queue = new();

        public LogCapture()
        {
            _previous = InstanceField.GetValue(null);
            var logger = (DebugLogger)RuntimeHelpers.GetUninitializedObject(typeof(DebugLogger));
            typeof(DebugLogger).GetField("logQueue", Priv)!.SetValue(logger, _queue);
            typeof(DebugLogger).GetField("isEnabled", Priv)!.SetValue(logger, true);
            typeof(DebugLogger).GetField("minimumLevel", Priv)!.SetValue(logger, DebugLogger.LogLevel.Debug);
            InstanceField.SetValue(null, logger);
        }

        public string[] Lines(string text) => _queue.ToArray().Where(l => l.Contains(text)).ToArray();

        public void Dispose() => InstanceField.SetValue(null, _previous);
    }

    /// <summary>A backend whose autosave write returns at once (the build test: only the build can be slow).</summary>
    private sealed class InstantBackend : ISaveBackend
    {
        public int Writes;
        public Task<bool> WriteGameData(string playerName, SaveGameData data) => Task.FromResult(true);
        public Task<SaveGameData?> ReadGameData(string playerName) => Task.FromResult<SaveGameData?>(null);
        public Task<SaveGameData?> ReadGameDataByFileName(string fileName) => Task.FromResult<SaveGameData?>(null);
        public bool GameDataExists(string playerName) => false;
        public bool DeleteGameData(string playerName) => false;
        public List<SaveInfo> GetAllSaves() => new();
        public List<SaveInfo> GetPlayerSaves(string playerName) => new();
        public SaveInfo? GetMostRecentSave(string playerName) => null;
        public List<string> GetAllPlayerNames() => new();
        public bool IsDisplayNameTaken(string displayName, string excludeUsername) => false;
        public Task<bool> WriteAutoSave(string playerName, SaveGameData data) { Writes++; return Task.FromResult(true); }
        public void CreateBackup(string playerName) { }
        public string GetSaveDirectory() => Path.GetTempPath();
    }

    private static Character Hero(int items = 0)
    {
        var hero = new Character { Name1 = "Slow Hero", Name2 = "Slow Hero", Class = CharacterClass.Warrior, Level = 20, HP = 300, MaxHP = 300, Gold = Gold };
        for (int i = 0; i < items; i++)
            hero.Inventory.Add(new Item { Name = $"Pebble {i}", Type = ObjType.Weapon, Attack = i, Value = i });
        return hero;
    }

    private static int Here([CallerLineNumber] int line = 0) => line;

    private string[] SlowLines() => _log.Lines("[SAVE_SLOW]");

    private static long Ms(string line, string field)
    {
        var m = Regex.Match(line, $@"\b{field}=(\d+)ms\b");
        m.Success.Should().BeTrue($"the line names {field} ms: {line}");
        return long.Parse(m.Groups[1].Value);
    }

    private object? Scalar(string sql)
    {
        using var c = new SqliteConnection($"Data Source={_db.DatabasePath};Pooling=false");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    /// <summary>The stored player, with the fields each save stamps from the clock masked.</summary>
    private string StoredPlayer() => Regex.Replace(
        (string)Scalar($"SELECT json_extract(player_data, '$.player') FROM players WHERE username = '{_key}'")!,
        "\"(lastLogin|accountCreated|lastPlayed|totalPlayTime|longestSession)\":\"[^\"]*\"", "\"$1\":\"clock\"");

    // ---------- 1. a write held by a real lock ----------

    [Fact]
    public async Task AWriteHeldByAnExclusiveLock_LogsOneSlowLine_WithTheWriteMsAndTheCaller()
    {
        var hero = Hero();
        // warm up (JIT, schema), with the threshold out of reach, so the timed save below is only the wait
        SaveSystem.SlowSaveThresholdMs = int.MaxValue;
        (await SaveSystem.Instance.AutoSave(hero, force: true)).Should().BeTrue();
        SaveSystem.SlowSaveThresholdMs = 2000;
        SlowLines().Should().BeEmpty();

        var acquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var holder = Task.Run(() =>
        {
            using var c = new SqliteConnection($"Data Source={_db.DatabasePath};Pooling=false");
            c.Open();
            using (var begin = c.CreateCommand()) { begin.CommandText = "BEGIN EXCLUSIVE;"; begin.ExecuteNonQuery(); }
            acquired.SetResult();
            Thread.Sleep(2600);
            using (var commit = c.CreateCommand()) { commit.CommandText = "COMMIT;"; commit.ExecuteNonQuery(); }
        });
        await acquired.Task;

        // as the combat victory path saves: the throttle reset, then a plain AutoSave
        SaveSystem.Instance.ResetAutoSaveThrottle();
        bool ok = await SaveSystem.Instance.AutoSave(hero); int line = Here();
        await holder;

        ok.Should().BeTrue("the write waits out the lock and lands");
        Convert.ToInt64(Scalar($"SELECT json_extract(player_data, '$.player.gold') FROM players WHERE username = '{_key}'")).Should().Be(Gold);
        var lines = SlowLines();
        lines.Should().ContainSingle();
        Ms(lines[0], "write").Should().BeGreaterThanOrEqualTo(2000);
        lines[0].Should().Contain("[WRN]").And.Contain($"key={_key} ");
        lines[0].Should().Contain($"caller={nameof(AWriteHeldByAnExclusiveLock_LogsOneSlowLine_WithTheWriteMsAndTheCaller)} SaveSlow128Tests.cs:{line}");
    }

    // ---------- 2. a fast save, and a throttled return, log nothing ----------

    [Fact]
    public async Task AFastSave_LogsNoSlowLine()
    {
        var hero = Hero();
        SaveSystem.SlowSaveThresholdMs.Should().Be(2000, "the shipped threshold");
        (await SaveSystem.Instance.AutoSave(hero, force: true)).Should().BeTrue();
        (await SaveSystem.Instance.AutoSave(hero, force: true)).Should().BeTrue();
        SlowLines().Should().BeEmpty("neither the build nor the write took 2 s");
    }

    [Fact]
    public async Task AThrottledReturn_TimesNothing()
    {
        var hero = Hero();
        (await SaveSystem.Instance.AutoSave(hero, force: true)).Should().BeTrue();
        SlowLines().Should().BeEmpty();
        // any timed save would now log; the throttled return (within 60 s, online, not forced) must not
        SaveSystem.SlowSaveThresholdMs = -1;
        (await SaveSystem.Instance.AutoSave(hero)).Should().BeTrue("the throttle pretends success");
        SlowLines().Should().BeEmpty("a throttled return builds and writes nothing, so it times nothing");
    }

    // ---------- 3. a slow build ----------

    [Fact]
    public async Task ASlowBuild_LogsOneLine_NamingTheBuildMs()
    {
        var instant = new InstantBackend();
        SaveSystem.InitializeWithBackend(instant);
        // warm (JIT done), four thousand items build in 1 to 2 ms; fifty thousand take about 40 ms,
        // well over the 5 ms threshold, while the instant write stays at 0 ms
        var hero = Hero(items: 50_000);
        SaveSystem.SlowSaveThresholdMs = 5;

        (await SaveSystem.Instance.AutoSave(hero, force: true)).Should().BeTrue();

        instant.Writes.Should().Be(1);
        var lines = SlowLines();
        lines.Should().ContainSingle();
        Ms(lines[0], "build").Should().BeGreaterThan(5, "fifty thousand items take more than 5 ms to build");
        Ms(lines[0], "write").Should().BeLessThanOrEqualTo(5, "the write returns at once, so only the build is over");
    }

    // ---------- 4. the caller ----------

    private async Task<(bool Ok, int Line)> SaveFromANamedCaller(Character hero)
    {
        bool ok = await SaveSystem.Instance.AutoSave(hero, force: true); return (ok, Here());
    }

    [Fact]
    public async Task TheCaller_IsTheCallingMethod_FileNameOnly_AndLine()
    {
        SaveSystem.SlowSaveThresholdMs = -1;
        var (ok, line) = await SaveFromANamedCaller(Hero());
        ok.Should().BeTrue();
        var lines = SlowLines();
        lines.Should().ContainSingle();
        lines[0].Should().EndWith($" caller={nameof(SaveFromANamedCaller)} SaveSlow128Tests.cs:{line}");
        lines[0].Should().NotContain("/Tests/").And.NotContain("\\Tests\\", "the file name only, no directory");
    }

    // ---------- 5. no save contents, and the save itself unchanged ----------

    [Fact]
    public async Task TheLine_CarriesNoSaveContents_AndTheSaveIsTheSameWithOrWithoutIt()
    {
        var hero = Hero();
        hero.BankGold = 6_543_219;

        SaveSystem.SlowSaveThresholdMs = -1;
        bool loggedOk = await SaveSystem.Instance.AutoSave(hero, force: true);
        string logged = StoredPlayer();
        var lines = SlowLines();
        lines.Should().ContainSingle();

        SaveSystem.SlowSaveThresholdMs = int.MaxValue;
        bool quietOk = await SaveSystem.Instance.AutoSave(hero, force: true);
        string quiet = StoredPlayer();
        SlowLines().Should().HaveCount(1, "the second save logged nothing");

        loggedOk.Should().BeTrue();
        quietOk.Should().Be(loggedOk);
        logged.Should().Be(quiet, "the stored player is the same whether the save logged or not");
        logged.Should().Contain(Gold.ToString(), "the known value is in the save");

        foreach (var text in new[] { Gold.ToString(), Gold.ToString("N0"), "6543219", "6,543,219", "Slow Hero", "{", "gold" })
            lines[0].Should().NotContain(text, "the line carries no save contents");
    }
}
