using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.4: the bank's robbery attempts counter is one persisted value for the world, kept where
/// the vault reserve lives (WorldStateData single-player, a world_state row online). It resets at
/// the day change and each attempt adds two guards.
/// </summary>
[Collection("SharedGameSingletons")]
public class BankRobberyCounter124Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>In-memory world_state rows, one per key.</summary>
    private sealed class KeyedStore : BankVaultSystem.IVaultStore
    {
        public readonly Dictionary<string, string> Rows = new();
        public Task<string?> Load(string key) => Task.FromResult(Rows.TryGetValue(key, out var v) ? v : null);
        public Task<bool> TryAtomicUpdate(string key, Func<string, string> transform)
        {
            Rows[key] = transform(Rows.TryGetValue(key, out var v) ? v : "");
            return Task.FromResult(true);
        }
    }

    private static readonly DateTime Day1 = new(2026, 10, 1, 23, 50, 0);
    private static readonly DateTime Day2 = new(2026, 10, 2, 0, 5, 0);
    private DateTime _now = Day1;

    public BankRobberyCounter124Tests()
    {
        BankVaultSystem.StoreOverride = null;
        BankVaultSystem.Load(GameConfig.BankVaultInitial);
        BankVaultSystem.LoadRobberies(0, "");
        BankVaultSystem.Clock = () => _now;
    }

    public void Dispose()
    {
        BankVaultSystem.StoreOverride = null;
        BankVaultSystem.Load(GameConfig.BankVaultInitial);
        BankVaultSystem.LoadRobberies(0, "");
        BankVaultSystem.Clock = () => DateTime.Now;
    }

    [Fact]
    public async Task SinglePlayer_CounterSurvivesASaveAndLoad()
    {
        await BankVaultSystem.RecordRobberyAttempt();
        await BankVaultSystem.RecordRobberyAttempt();

        var saved = (WorldStateData)typeof(SaveSystem).GetMethod("SerializeWorldState", F)!.Invoke(SaveSystem.Instance, null)!;
        var json = System.Text.Json.JsonSerializer.Serialize(saved);
        var loaded = System.Text.Json.JsonSerializer.Deserialize<WorldStateData>(json)!;
        loaded.BankRobberiesToday.Should().Be(2);
        loaded.BankRobberiesDate.Should().Be("2026-10-01");

        BankVaultSystem.LoadRobberies(0, "");   // a fresh process
        BankVaultSystem.RobberiesToday.Should().Be(0);
        var restore = typeof(GameEngine).GetMethod("RestoreWorldState", F)!;
        await (Task)restore.Invoke(GameEngine.Instance, new object[] { loaded })!;
        BankVaultSystem.RobberiesToday.Should().Be(2, "the count came back from the save");
    }

    [Fact]
    public void WorldState_LegacySave_HasNoRobberies()
    {
        var legacy = System.Text.Json.JsonSerializer.Deserialize<WorldStateData>("{}")!;
        legacy.BankRobberiesToday.Should().Be(0);
        legacy.BankRobberiesDate.Should().Be("");
    }

    [Fact]
    public async Task Online_CounterSurvivesARestart()
    {
        var store = new KeyedStore();
        store.Rows[BankVaultSystem.WorldStateKey] = "500000";
        BankVaultSystem.StoreOverride = store;
        await BankVaultSystem.RecordRobberyAttempt();
        store.Rows[BankVaultSystem.RobberyCounterKey].Should().Be("2026-10-01|1");
        store.Rows[BankVaultSystem.WorldStateKey].Should().Be("500000", "the reserve row is untouched");

        BankVaultSystem.LoadRobberies(0, "");   // the server restarted
        await BankVaultSystem.Refresh();
        BankVaultSystem.RobberiesToday.Should().Be(1, "the count is read back from world_state");
    }

    [Fact]
    public async Task TheCounter_ResetsOnANewDay()
    {
        await BankVaultSystem.RecordRobberyAttempt();
        await BankVaultSystem.RecordRobberyAttempt();
        BankVaultSystem.RobberiesToday.Should().Be(2);
        _now = Day2;
        BankVaultSystem.RobberiesToday.Should().Be(0, "a count from yesterday does not count today");
        await BankVaultSystem.RecordRobberyAttempt();
        BankVaultSystem.RobberiesToday.Should().Be(1, "the new day starts from zero");

        var store = new KeyedStore();
        store.Rows[BankVaultSystem.RobberyCounterKey] = "2026-10-01|5";
        BankVaultSystem.StoreOverride = store;
        await BankVaultSystem.RecordRobberyAttempt();
        store.Rows[BankVaultSystem.RobberyCounterKey].Should().Be("2026-10-02|1", "online the stale row restarts at the day change");
    }

    [Fact]
    public async Task TwoRobbers_ShareTheCounter()
    {
        var store = new KeyedStore();
        BankVaultSystem.StoreOverride = store;
        await BankVaultSystem.RecordRobberyAttempt();   // robber one's session

        BankVaultSystem.LoadRobberies(0, "");           // robber two's session has its own cache
        await BankVaultSystem.Refresh();
        BankVaultSystem.RobberiesToday.Should().Be(1, "robber two sees robber one's attempt");
        await BankVaultSystem.RecordRobberyAttempt();
        store.Rows[BankVaultSystem.RobberyCounterKey].Should().Be("2026-10-01|2", "both attempts are counted once");
    }

    [Fact]
    public async Task EachAttempt_AddsTwoGuards()
    {
        var bank = new BankLocation();
        var guards = typeof(BankLocation).GetMethod("CalculateGuardCount", F)!;
        int before = (int)guards.Invoke(bank, null)!;
        await BankVaultSystem.RecordRobberyAttempt();
        ((int)guards.Invoke(bank, null)!).Should().Be(before + 2);
        _now = Day2;
        ((int)guards.Invoke(bank, null)!).Should().Be(before, "the guards stand down at the day change");
    }
}
