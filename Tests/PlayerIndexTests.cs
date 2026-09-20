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
/// live server the stats rebuild stalled the whole process for up to 83 seconds every 115, so the
/// news feed, the API and the browser terminal were frozen for most of that. These tests hold the schema to the
/// indexes that fix it, prove the planner uses them once a table has rows, and cover the two
/// hazards the fix introduces: an expression index is also a constraint on what may be written,
/// and a server upgrading from an older release, or from indexes added by hand, must converge on
/// one definition.
/// </summary>
[Collection("SharedGameSingletons")]
public class PlayerIndexTests : IDisposable
{
    private readonly List<string> _paths = new();
    private readonly Xunit.Abstractions.ITestOutputHelper _out;

    public PlayerIndexTests(Xunit.Abstractions.ITestOutputHelper output) { _out = output; }

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
            sql.Should().NotContain("\"$.", $"{name}: one spelling has to win so servers do not drift; both builds accept either");
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
        // A server as it really is before this release: the three hand-made indexes in the older
        // double-quoted spelling, and the four added by hand during the incident, typed across
        // several lines. Both differ from this release's definitions, the second only in layout.
        var old = NewDbPath();
        _ = new SqlSaveBackend(old);
        using (var c = Open(old))
        {
            foreach (var name in Required)
            {
                using var drop = c.CreateCommand();
                drop.CommandText = $"DROP INDEX IF EXISTS {name};";
                drop.ExecuteNonQuery();
            }
            using var make = c.CreateCommand();
            make.CommandText = @"
                CREATE INDEX idx_players_level ON players(json_extract(player_data, ""$.player.level"") DESC);
                CREATE INDEX idx_players_class ON players(json_extract(player_data, ""$.player.class""));
                CREATE INDEX idx_players_xp ON players(json_extract(player_data, ""$.player.experience"") DESC);
                CREATE INDEX idx_players_stats_cover ON players(
                  is_banned, username,
                  json_extract(player_data,'$.player.level'),
                  json_extract(player_data,'$.player.gold'),
                  json_extract(player_data,'$.player.bankGold'),
                  json_extract(player_data,'$.player.statistics.totalMonstersKilled'),
                  json_extract(player_data,'$.player.statistics.deepestDungeonLevel'),
                  json_extract(player_data,'$.player.class')
                );
                CREATE INDEX idx_players_immortal ON players(json_extract(player_data, '$.player.isImmortal'));
                CREATE INDEX idx_players_murder_weight ON players(json_extract(player_data, '$.player.murderWeight'));
                CREATE INDEX idx_players_worshipped_god ON players(json_extract(player_data, '$.player.worshippedGod'));";
            make.ExecuteNonQuery();
        }
        Seed(old, 50);
        var before = PlayerIndexes(old);
        before.Values.Count(sql => sql.Contains("\"$.")).Should().Be(3, "the legacy spelling is in place");
        before["idx_players_stats_cover"].Should().Contain("\n", "and the hand-typed one spans several lines");

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
        // The fix moves work from the read path to the write path: a save now maintains seven index
        // entries, twelve json_extract evaluations, over an 84 KB blob. Measured here against the
        // same database with the indexes dropped, under the SQLite the game server itself uses.
        var path = NewDbPath();
        _ = new SqlSaveBackend(path);
        Seed(path, 100);
        var blob = PlayerJson(77, 3, 123456);
        blob.Length.Should().BeGreaterThan(80_000, "the live server averages 84 KB per player");

        double Time100Saves(string db)
        {
            using var c = Open(db);
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
            return sw.Elapsed.TotalMilliseconds / 100;
        }

        double withIndexes = Time100Saves(path);

        var bare = NewDbPath();
        File.Copy(path, bare);
        using (var c = Open(bare))
            foreach (var name in Required)
            {
                using var drop = c.CreateCommand();
                drop.CommandText = $"DROP INDEX IF EXISTS {name};";
                drop.ExecuteNonQuery();
            }
        double withoutIndexes = Time100Saves(bare);

        _out.WriteLine($"save of an {blob.Length / 1024} KB player: {withIndexes:F2} ms with the seven indexes, " +
                       $"{withoutIndexes:F2} ms without, cost {withIndexes - withoutIndexes:F2} ms per save");
        withIndexes.Should().BeLessThan(50, $"a save costs {withIndexes:F2} ms with the indexes and {withoutIndexes:F2} ms without; " +
                                            "the autosave writes a few hundred of these every five minutes");
    }
}
