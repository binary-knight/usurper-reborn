using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.8: the website reads player statistics out of a JSON blob that averages 84 KB. Without
/// indexes on the hot paths SQLite re-parses every blob for every json_extract call, and on the
/// live server the stats rebuild burned 75 seconds of CPU every 115 seconds, freezing the news
/// feed, the API and the browser terminal for that whole time. These tests hold the schema to the
/// indexes that fix it, prove the planner uses them once a table has rows, and cover the two
/// hazards the fix introduces: an expression index is also a constraint on what may be written,
/// and a server upgrading from an older release must converge on one spelling of the index.
/// </summary>
[Collection("SharedGameSingletons")]
public class PlayerIndexTests : IDisposable
{
    private readonly List<string> _paths = new();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var p in _paths) { try { File.Delete(p); } catch { } }
    }

    private string NewDbPath()
    {
        var p = Path.Combine(Path.GetTempPath(), $"usurper-idx-{Guid.NewGuid():N}.db");
        _paths.Add(p);
        return p;
    }

    private static SqliteConnection Open(string path)
    {
        var c = new SqliteConnection($"Data Source={path}");
        c.Open();
        return c;
    }

    /// <summary>A save blob shaped like a real one, including the padding that makes it ~84 KB.</summary>
    private static string PlayerJson(int level, int cls, long gold, bool immortal = false)
    {
        var filler = new StringBuilder();
        for (int i = 0; i < 1500; i++) filler.Append($"\"item{i}\":\"the quick brown fox jumps over the lazy dog\",");
        return $"{{\"player\":{{\"level\":{level},\"class\":{cls},\"gold\":{gold},\"bankGold\":{gold / 2}," +
               $"\"experience\":{level * 1000},\"murderWeight\":{level % 7},\"worshippedGod\":\"god{cls}\"," +
               $"\"isImmortal\":{(immortal ? 1 : 0)},\"nobleTitle\":null," +
               $"\"statistics\":{{\"totalMonstersKilled\":{level * 3},\"deepestDungeonLevel\":{level}}}," +
               $"{filler}\"end\":true}}}}";
    }

    private static void Seed(string path, int rows)
    {
        using var c = Open(path);
        using var tx = c.BeginTransaction();
        for (int i = 0; i < rows; i++)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO players (username, display_name, password_hash, player_data) VALUES (@u, @d, '', @p);";
            cmd.Parameters.AddWithValue("@u", $"player{i}");
            cmd.Parameters.AddWithValue("@d", $"Player{i}");
            cmd.Parameters.AddWithValue("@p", PlayerJson(1 + i % 100, i % 11, i * 1000L, immortal: i % 97 == 0));
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
        using var an = c.CreateCommand();
        an.CommandText = "ANALYZE;";
        an.ExecuteNonQuery();
    }

    private static string Plan(string path, string sql)
    {
        using var c = Open(path);
        using var cmd = c.CreateCommand();
        cmd.CommandText = "EXPLAIN QUERY PLAN " + sql;
        var rows = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) rows.Add(r.GetString(r.FieldCount - 1));
        return string.Join(" | ", rows);
    }

    private static Dictionary<string, string> PlayerIndexes(string path)
    {
        using var c = Open(path);
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT name, sql FROM sqlite_master WHERE type='index' AND tbl_name='players' AND sql IS NOT NULL ORDER BY name;";
        var found = new Dictionary<string, string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) found[r.GetString(0)] = r.GetString(1);
        return found;
    }

    private static readonly string[] Required =
    {
        "idx_players_stats_cover", "idx_players_immortal", "idx_players_murder_weight",
        "idx_players_worshipped_god", "idx_players_level", "idx_players_class", "idx_players_xp",
    };

    [Fact]
    public void AFreshDatabase_CarriesEveryPlayerIndex_WithSingleQuotedPaths()
    {
        var path = NewDbPath();
        _ = new SqlSaveBackend(path);
        var found = PlayerIndexes(path);
        foreach (var name in Required)
            found.Should().ContainKey(name, "without it every operator gets the slow website");
        foreach (var (name, sql) in found)
            sql.Should().NotContain("\"$.", $"{name}: a double-quoted JSON path is a string in one SQLite build and an identifier in another");
    }

    [Fact]
    public void TheStatisticsAggregate_IsAnsweredFromTheIndexAlone()
    {
        var path = NewDbPath();
        _ = new SqlSaveBackend(path);
        Seed(path, 200);
        var plan = Plan(path, @"SELECT COUNT(*),
              SUM(json_extract(player_data,'$.player.statistics.totalMonstersKilled')),
              AVG(json_extract(player_data,'$.player.level')),
              MAX(json_extract(player_data,'$.player.statistics.deepestDungeonLevel')),
              SUM(json_extract(player_data,'$.player.gold')),
              SUM(json_extract(player_data,'$.player.bankGold'))
            FROM players WHERE is_banned = 0 AND username NOT LIKE 'emergency_%'");
        plan.Should().Contain("idx_players_stats_cover");
        plan.Should().NotContain("SCAN players", "touching the table means re-parsing every blob");
    }

    [Theory]
    [InlineData("$.player.isImmortal", "idx_players_immortal")]
    [InlineData("$.player.murderWeight", "idx_players_murder_weight")]
    [InlineData("$.player.worshippedGod", "idx_players_worshipped_god")]
    public void EachLookupThatWasAFullScan_NowUsesItsIndex(string path, string index)
    {
        var db = NewDbPath();
        _ = new SqlSaveBackend(db);
        Seed(db, 200);
        Plan(db, $"SELECT display_name FROM players WHERE is_banned = 0 AND json_extract(player_data,'{path}') = 1")
            .Should().Contain(index);
    }

    [Fact]
    public void TheLeaderboardOrdersFromTheLevelIndex()
    {
        var path = NewDbPath();
        _ = new SqlSaveBackend(path);
        Seed(path, 200);
        Plan(path, @"SELECT display_name FROM players WHERE is_banned = 0
               AND json_extract(player_data,'$.player.level') IS NOT NULL
               ORDER BY json_extract(player_data,'$.player.level') DESC LIMIT 25")
            .Should().Contain("idx_players_level");
    }

    [Fact]
    public void AnUpgradedServer_ConvergesOnTheSameSchemaAsAFreshOne()
    {
        // a server from before this release: the three hand-made indexes, double-quoted
        var old = NewDbPath();
        _ = new SqlSaveBackend(old);
        using (var c = Open(old))
        {
            foreach (var name in new[] { "idx_players_level", "idx_players_class", "idx_players_xp" })
            {
                using var drop = c.CreateCommand();
                drop.CommandText = $"DROP INDEX IF EXISTS {name};";
                drop.ExecuteNonQuery();
            }
            using var make = c.CreateCommand();
            make.CommandText = @"
                CREATE INDEX idx_players_level ON players(json_extract(player_data, ""$.player.level"") DESC);
                CREATE INDEX idx_players_class ON players(json_extract(player_data, ""$.player.class""));
                CREATE INDEX idx_players_xp ON players(json_extract(player_data, ""$.player.experience"") DESC);";
            make.ExecuteNonQuery();
        }
        Seed(old, 50);
        PlayerIndexes(old).Values.Count(sql => sql.Contains("\"$.")).Should().Be(3, "the legacy spelling is in place");

        SqliteConnection.ClearAllPools();
        _ = new SqlSaveBackend(old);   // upgrade: open it again with this release

        var upgraded = PlayerIndexes(old);
        var fresh = NewDbPath();
        _ = new SqlSaveBackend(fresh);
        var clean = PlayerIndexes(fresh);

        upgraded.Keys.Should().BeEquivalentTo(clean.Keys, "an upgraded server and a fresh one carry the same indexes");
        foreach (var (name, sql) in clean)
            upgraded[name].Should().Be(sql, $"{name} must be defined identically on both");

        SqliteConnection.ClearAllPools();
        _ = new SqlSaveBackend(old);   // and again: convergence is idempotent
        PlayerIndexes(old).Should().BeEquivalentTo(upgraded);
    }

    [Fact]
    public void EveryValueTheGameWrites_IsStillAccepted_AndTheIndexRejectsOnlyBrokenJson()
    {
        var path = NewDbPath();
        _ = new SqlSaveBackend(path);
        using var c = Open(path);

        // the values the game's own writers produce
        foreach (var (name, data) in new[]
        {
            ("a real save", PlayerJson(42, 10, 5000)),
            ("the empty placeholder the delete and wipe paths write", "{}"),
            ("a save with nulls where the indexed paths are", "{\"player\":{\"level\":null,\"gold\":null,\"statistics\":{}}}"),
            ("a save with no player object at all", "{\"other\":1}"),
        })
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO players (username, display_name, password_hash, player_data) VALUES (@u, @u, '', @p);";
            cmd.Parameters.AddWithValue("@u", $"w{Guid.NewGuid():N}".Substring(0, 12));
            cmd.Parameters.AddWithValue("@p", data);
            cmd.Invoking(x => x.ExecuteNonQuery()).Should().NotThrow($"the game writes {name}");
        }

        // and the constraint the indexes add: text that is not JSON can no longer be stored.
        // No writer produces this today (every one serializes or writes '{}'), but a future one
        // must not, and this test is where that shows up.
        using var bad = c.CreateCommand();
        bad.CommandText = "INSERT INTO players (username, display_name, password_hash, player_data) VALUES ('brokenjson', 'brokenjson', '', 'not json at all');";
        bad.Invoking(x => x.ExecuteNonQuery()).Should().Throw<SqliteException>("an expression index over json_extract is also a constraint");
    }

    [Fact]
    public void MaintainingTheIndexes_DoesNotMakeSavingExpensive()
    {
        // the fix moves work from the read path to the write path: every save now updates eleven
        // index entries over an 84 KB blob. This runs under the same SQLite the game server uses.
        var path = NewDbPath();
        _ = new SqlSaveBackend(path);
        Seed(path, 100);
        using var c = Open(path);
        var blob = PlayerJson(77, 3, 123456);
        blob.Length.Should().BeGreaterThan(80_000, "the live server averages 84 KB per player");

        var sw = Stopwatch.StartNew();
        using (var tx = c.BeginTransaction())
        {
            for (int i = 0; i < 100; i++)
            {
                using var cmd = c.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE players SET player_data = @p WHERE username = @u;";
                cmd.Parameters.AddWithValue("@p", blob);
                cmd.Parameters.AddWithValue("@u", $"player{i}");
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        sw.Stop();
        var perSave = sw.Elapsed.TotalMilliseconds / 100;
        perSave.Should().BeLessThan(50, $"a save costs {perSave:F1} ms with the indexes; the autosave writes a few hundred of these every five minutes");
    }
}
