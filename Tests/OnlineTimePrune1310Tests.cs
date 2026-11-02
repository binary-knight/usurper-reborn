using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.10: the world save prune compared stored names case sensitively, so a login typed with a capital lost its
/// online row every 5 minutes and the heartbeat re-registered it with a new connected_at. The prune and the
/// unknown leader list now compare through ulower, as the rest of the backend does. Synthetic names only.
/// </summary>
public class OnlineTimePrune1310Tests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-online1310-{Guid.NewGuid():N}.db");
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

    // players.username is stored with the C# fold, as the save path does
    private void AddPlayer(string name, string display = "")
    {
        _ = Db;
        using var c = new SqliteConnection($"Data Source={_path}"); c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO players (username, display_name, player_data) VALUES (@u, @d, '{}');";
        cmd.Parameters.AddWithValue("@u", name.ToLowerInvariant());
        cmd.Parameters.AddWithValue("@d", display.Length > 0 ? display : name);
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public async Task MixedCaseOnlineRow_SurvivesPrune_AndKeepsConnectedAt()
    {
        AddPlayer("mixedcase");
        await Db.RegisterOnline("MixedCase", "MixedCase", "Main Street", "Web");
        Exec("UPDATE online_players SET connected_at = '2026-01-01 10:00:00';");

        await Db.PruneOrphanedPlayerData();

        Count("SELECT COUNT(*) FROM online_players WHERE connected_at = '2026-01-01 10:00:00'").Should().Be(1);
    }

    [Fact]
    public async Task OnlineRowWithNoPlayer_IsStillPruned()
    {
        AddPlayer("someoneelse");
        await Db.RegisterOnline("Ghost", "Ghost", "Main Street", "Web");

        await Db.PruneOrphanedPlayerData();

        Count("SELECT COUNT(*) FROM online_players").Should().Be(0);
    }

    [Fact]
    public async Task NonAsciiCapitalName_OnlineAndSleepingRowsSurvive()
    {
        AddPlayer("Élan");
        await Db.RegisterOnline("Élan", "Élan", "Inn", "Web");
        // stored the way the sleep insert stores it: SQLite LOWER, which keeps a non-ASCII capital
        Exec("INSERT OR REPLACE INTO sleeping_players (username, sleep_location) VALUES (LOWER('Élan'), 'dormitory');");

        await Db.PruneOrphanedPlayerData();

        Count("SELECT COUNT(*) FROM online_players").Should().Be(1);
        Count("SELECT COUNT(*) FROM sleeping_players").Should().Be(1);
    }

    [Fact]
    public async Task SleepingPendingAndCombatRows_ForExistingPlayer_Survive_AndMissingPlayer_Pruned()
    {
        AddPlayer("realone", "RealOne");
        Exec("INSERT INTO sleeping_players (username) VALUES ('realone'), ('missingone');");
        Exec("INSERT INTO pending_gold_transfers (recipient_username, sender_display, amount) VALUES ('realone', 'x', 5), ('missingone', 'x', 5);");
        Exec("INSERT INTO pending_inheritance (player_username, source_npc_name, item_json) VALUES ('realone', 'n', '{}'), ('missingone', 'n', '{}');");
        Exec("INSERT INTO combat_events (player_name, player_level, player_class, player_max_hp, player_str, player_dex, player_weap_pow, player_arm_pow, outcome) VALUES " +
             "('REALONE', 1, 'Warrior', 1, 1, 1, 1, 1, 'win'), ('Missing', 1, 'Warrior', 1, 1, 1, 1, 1, 'win');");

        await Db.PruneOrphanedPlayerData();

        Count("SELECT COUNT(*) FROM sleeping_players WHERE username = 'realone'").Should().Be(1);
        Count("SELECT COUNT(*) FROM sleeping_players WHERE username = 'missingone'").Should().Be(0);
        Count("SELECT COUNT(*) FROM pending_gold_transfers WHERE recipient_username = 'realone'").Should().Be(1);
        Count("SELECT COUNT(*) FROM pending_gold_transfers WHERE recipient_username = 'missingone'").Should().Be(0);
        Count("SELECT COUNT(*) FROM pending_inheritance WHERE player_username = 'realone'").Should().Be(1);
        Count("SELECT COUNT(*) FROM pending_inheritance WHERE player_username = 'missingone'").Should().Be(0);
        Count("SELECT COUNT(*) FROM combat_events WHERE player_name = 'REALONE'").Should().Be(1);
        Count("SELECT COUNT(*) FROM combat_events WHERE player_name = 'Missing'").Should().Be(0);
    }

    [Fact]
    public async Task UnknownLeaderList_DoesNotListTeamWhoseLeaderDiffersOnlyByCase()
    {
        AddPlayer("leaderone");
        Exec("INSERT INTO player_teams (team_name, password_hash, created_by) VALUES ('Alpha', 'h', 'LeaderOne'), ('Beta', 'h', 'nobodyhere');");

        var teams = await Db.GetTeamsWithUnknownLeader();

        teams.Select(t => t.TeamName).Should().Equal("Beta");
    }
}
