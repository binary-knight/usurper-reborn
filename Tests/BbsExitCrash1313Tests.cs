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
/// 1.3.13: a BBS door crashed on Quit (Windows, 0xC0000005 in sqlite3_step under PruneOrphanedPlayerData). The door
/// cancels its embedded world sim, whose final save ended with the orphan prune, and exits about five seconds later.
/// Since 1.3.10 the prune called the ulower SQL function once per row, so an exit mid-prune had SQLite calling back
/// into a .NET runtime that was shutting down. The prune now folds names in C#, and the shutdown save skips the
/// maintenance. Synthetic names only.
/// </summary>
[Collection("SharedGameSingletons")]
public class BbsExitCrash1313Tests : IDisposable
{
    private const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-exit1313-{Guid.NewGuid():N}.db");
    private SqlSaveBackend? _db;
    private SqlSaveBackend Db => _db ??= new SqlSaveBackend(_path);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    private void Exec(string sql)
    {
        _ = Db;
        using var c = new SqliteConnection($"Data Source={_path}"); c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private long Count(string sql)
    {
        _ = Db;
        using var c = new SqliteConnection($"Data Source={_path}"); c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private void AddPlayer(string name, string display)
    {
        _ = Db;
        using var c = new SqliteConnection($"Data Source={_path}"); c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO players (username, display_name, player_data) VALUES (@u, @d, '{}');";
        cmd.Parameters.AddWithValue("@u", name.ToLowerInvariant());
        cmd.Parameters.AddWithValue("@d", display);
        cmd.ExecuteNonQuery();
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
        start.Should().BeGreaterThan(0, signature);
        int next = source.IndexOf("\n        public ", start + signature.Length, StringComparison.Ordinal);
        return source.Substring(start, next - start);
    }

    [Fact]
    public void ThePrune_RunsNoCallbackFromInsideSqlite()
    {
        var body = MethodBody(Source("Systems", "SqlSaveBackend.cs"), "public async Task PruneOrphanedPlayerData()");
        body.Should().NotContain("ulower(", "SQLite must not call back into .NET once per row while a door may exit");
        body.Should().NotContain("OpenConnection()", "OpenConnection registers the ulower callback");
        body.Should().Contain("ToLowerInvariant()", "names still fold as ulower folds them");
    }

    [Fact]
    public async Task ThePrune_FoldsCaseInCSharp_InEveryTable()
    {
        AddPlayer("Élan", "Élan the Bold");
        AddPlayer("plainone", "PlainOne");
        await Db.RegisterOnline("ÉLAN", "Élan", "Inn", "Web");
        await Db.RegisterOnline("Gone", "Gone", "Inn", "Web");
        Exec("INSERT INTO sleeping_players (username) VALUES ('PLAINONE'), ('gone');");
        Exec("INSERT INTO pending_gold_transfers (recipient_username, sender_display, amount) VALUES ('élan', 'x', 5), ('gone', 'x', 5);");
        Exec("INSERT INTO pending_inheritance (player_username, source_npc_name, item_json) VALUES ('Élan', 'n', '{}'), ('gone', 'n', '{}');");
        Exec("INSERT INTO combat_events (player_name, player_level, player_class, player_max_hp, player_str, player_dex, player_weap_pow, player_arm_pow, outcome) VALUES " +
             "('ÉLAN THE BOLD', 1, 'Warrior', 1, 1, 1, 1, 1, 'win'), ('PlainOne', 1, 'Warrior', 1, 1, 1, 1, 1, 'death'), ('Gone', 1, 'Warrior', 1, 1, 1, 1, 1, 'win');");

        await Db.PruneOrphanedPlayerData();

        Count("SELECT COUNT(*) FROM online_players").Should().Be(1, "the non-ASCII capital row is a live player");
        Count("SELECT COUNT(*) FROM online_players WHERE username = 'Gone'").Should().Be(0);
        Count("SELECT COUNT(*) FROM sleeping_players").Should().Be(1);
        Count("SELECT COUNT(*) FROM sleeping_players WHERE username = 'PLAINONE'").Should().Be(1);
        Count("SELECT COUNT(*) FROM pending_gold_transfers").Should().Be(1);
        Count("SELECT COUNT(*) FROM pending_inheritance").Should().Be(1);
        Count("SELECT COUNT(*) FROM combat_events").Should().Be(2, "rows match a username or a display name, in any case");
        Count("SELECT COUNT(*) FROM combat_events WHERE player_name = 'Gone'").Should().Be(0);
    }

    [Fact]
    public async Task TheShutdownSave_SkipsTheMaintenance_AndThePeriodicSaveRunsIt()
    {
        AddPlayer("stillhere", "StillHere");
        Exec("INSERT INTO sleeping_players (username) VALUES ('stillhere'), ('orphan1313');");
        var sim = new WorldSimService(Db, saveIntervalMinutes: 0);

        await sim.ShutdownAsync();
        Count("SELECT COUNT(*) FROM sleeping_players WHERE username = 'orphan1313'").Should().Be(1,
            "the shutdown save must not start a prune a door exit could cut off");

        await (Task)typeof(WorldSimService).GetMethod("SaveWorldState", Priv)!.Invoke(sim, null)!;
        Count("SELECT COUNT(*) FROM sleeping_players WHERE username = 'orphan1313'").Should().Be(0, "the periodic save prunes");
        Count("SELECT COUNT(*) FROM sleeping_players WHERE username = 'stillhere'").Should().Be(1);
    }
}
