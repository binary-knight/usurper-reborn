using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.15: OnlineStateManager.Shutdown records the logout and announces the departure only for a session whose
/// tracking started. A session dropped before that (during character creation) does neither.
/// </summary>
[Collection("SharedGameSingletons")]
public class OnlineShutdown1115Tests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-shutdown-{Guid.NewGuid():N}.db");
    private readonly SqlSaveBackend _db;
    private static readonly FieldInfo BridgeConn =
        typeof(DiscordBridge).GetField("_connectionString", BindingFlags.NonPublic | BindingFlags.Static)!;
    private readonly object? _bridgeBefore;

    public OnlineShutdown1115Tests()
    {
        _db = new SqlSaveBackend(_path);
        _bridgeBefore = BridgeConn.GetValue(null);
        DiscordBridge.Initialize(_path);
        Exec("INSERT INTO players (username, display_name, player_data) VALUES ('leaver', 'Leaver', '{}');");
    }

    public void Dispose()
    {
        BridgeConn.SetValue(null, _bridgeBefore);
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    private void Exec(string sql)
    {
        using var conn = new SqliteConnection($"Data Source={_path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private object? Scalar(string sql)
    {
        using var conn = new SqliteConnection($"Data Source={_path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var v = cmd.ExecuteScalar();
        return v is DBNull ? null : v;
    }

    private OnlineStateManager NewOsm() =>
        (OnlineStateManager)Activator.CreateInstance(typeof(OnlineStateManager),
            BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { _db, "leaver" }, null)!;

    private long Posts(string text) =>
        (long)Scalar($"SELECT COUNT(*) FROM discord_gossip WHERE author = '{DiscordBridge.SystemAuthor}' AND message LIKE '%{text}%';")!;

    private object? LastLogout(string user = "leaver") => Scalar($"SELECT last_logout FROM players WHERE username = '{user}';");

    [Fact]
    public async Task Shutdown_WithoutTracking_RecordsNoLogout()
    {
        await NewOsm().Shutdown();
        LastLogout().Should().BeNull("no login was recorded, so there is no logout to record");
    }

    [Fact]
    public async Task Shutdown_WithoutTracking_AnnouncesNoDeparture()
    {
        await NewOsm().Shutdown();
        Posts("has left the world").Should().Be(0, "the arrival was never announced");
    }

    [Fact]
    public async Task Shutdown_AfterTracking_RecordsTheLogout()
    {
        var osm = NewOsm();
        await osm.StartOnlineTracking("Leaver", "Test");
        await osm.Shutdown();
        LastLogout().Should().NotBeNull();
    }

    [Fact]
    public async Task Shutdown_AfterTracking_AnnouncesTheDeparture()
    {
        var osm = NewOsm();
        await osm.StartOnlineTracking("Leaver", "Test");
        await osm.Shutdown();
        Posts("Leaver has entered the world").Should().Be(1);
        Posts("Leaver has left the world").Should().Be(1);
    }

    [Fact]
    public async Task Shutdown_AfterSwitchIdentityOnly_RecordsTheLogoutButAnnouncesNothing()
    {
        Exec("INSERT INTO players (username, display_name, player_data) VALUES ('leaver__alt', 'Alt', '{}');");
        var osm = NewOsm();
        await osm.SwitchIdentity("leaver__alt", "Alt", "Test");
        await osm.Shutdown();
        LastLogout("leaver__alt").Should().NotBeNull("SwitchIdentity recorded a login");
        Posts("has left the world").Should().Be(0, "no arrival was announced");
    }
}
