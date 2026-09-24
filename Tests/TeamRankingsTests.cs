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
/// v1.1.12: the team rankings read every player member in one pass over the saves (it was one scan per
/// team), show the real level and power sums (it was members x 50 and level 0), combine a team's NPC and
/// player members (the NPC side used to hide the players) and leave out teams with no members.
/// </summary>
[Collection("SharedGameSingletons")]
public class TeamRankingsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-rank-{Guid.NewGuid():N}.db");
    private readonly SqlSaveBackend _db;

    public TeamRankingsTests() { _db = new SqlSaveBackend(_path); }

    public void Dispose()
    {
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

    private void Raw(string key, string json, bool banned = false) =>
        Exec($"INSERT INTO players (username, display_name, player_data, is_banned) VALUES ('{key}', '{key}', '{json}', {(banned ? 1 : 0)});");

    private void Player(string key, string? team, int level, int str, int def, bool banned = false) =>
        Raw(key, $"{{\"player\":{{\"level\":{level},\"strength\":{str},\"defence\":{def}" +
                 $"{(team != null ? $",\"team\":\"{team}\"" : "")},\"filler\":\"xx\"}}}}", banned);

    private void Team(string name, bool turf = false) =>
        Exec($"INSERT INTO player_teams (team_name, password_hash, created_by, controls_turf) VALUES ('{name}', 'x', 'founder', {(turf ? 1 : 0)});");

    private static NPC Npc(string id, string team, int level, int str, int def, bool alive = true) =>
        new NPC { ID = id, Name1 = id, Name2 = id, Team = team, Level = level, Strength = str, Defence = def, HP = alive ? 50 : 0, MaxHP = 50 };

    [Fact]
    public async Task OnePass_CountsAndSumsThePlayers_WithoutBannedOrEmergencyRows()
    {
        Team("Grey Company");
        Player("alpha", "Grey Company", 10, 100, 50);
        Player("beta", "Grey Company", 20, 200, 80);
        Player("gamma", "Grey Company", 99, 999, 999, banned: true);
        Player("emergency_beta", "Grey Company", 99, 999, 999);
        Player("delta", null, 30, 1, 1);

        var team = (await _db.GetPlayerTeams()).Single(t => t.TeamName == "Grey Company");
        team.MemberCount.Should().Be(2);
        team.LevelSum.Should().Be(30);
        team.PowerSum.Should().Be(10 + 100 + 50 + 20 + 200 + 80);

        var row = TeamCornerLocation.BuildTeamRankings(Array.Empty<NPC>(), await _db.GetTeamRankingStats(null), null).Single();
        row.MemberCount.Should().Be(2);
        row.AverageLevel.Should().Be(15);
        row.TotalPower.Should().Be(460);
    }

    private void DropJsonIndexes()
    {
        using var conn = new SqliteConnection($"Data Source={_path}");
        conn.Open();
        var names = new System.Collections.Generic.List<string>();
        using (var q = conn.CreateCommand())
        {
            q.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'players' AND sql LIKE '%json%';";
            using var r = q.ExecuteReader();
            while (r.Read()) names.Add(r.GetString(0));
        }
        foreach (var n in names)
        {
            using var d = conn.CreateCommand();
            d.CommandText = $"DROP INDEX \"{n}\";";
            d.ExecuteNonQuery();
        }
    }

    [Fact]
    public async Task AMalformedSave_DoesNotBreakTheQuery()
    {
        // a fresh database's expression indexes refuse a malformed blob; an upgraded one may lack them
        DropJsonIndexes();
        Team("Grey Company");
        Player("alpha", "Grey Company", 10, 100, 50);
        Raw("broken", "{\"player\":{\"level\":5,\"team\":\"Grey Company\"");
        Raw("numeric", "{\"player\":{\"level\":5,\"team\":7}}");

        var team = (await _db.GetPlayerTeams()).Single(t => t.TeamName == "Grey Company");
        team.MemberCount.Should().Be(1);
        team.LevelSum.Should().Be(10);
        (await _db.GetTeamRankingStats(null)).Should().ContainSingle(t => t.TeamName == "Grey Company");
    }

    [Fact]
    public async Task AMixedTeam_CombinesLivingNPCs_AllPlayers_AndTheViewerOnce()
    {
        // an NPC team with no player_teams row; two other players and the viewer joined it
        Player("alpha", "Iron Circle", 10, 100, 100);
        Player("beta", "Iron Circle", 20, 100, 100);
        Player("viewer", "Iron Circle", 5, 10, 10);   // the save lags the in-memory level
        var npcs = new[]
        {
            Npc("npc_rank_a", "Iron Circle", 30, 300, 300),
            Npc("npc_rank_b", "Iron Circle", 40, 400, 400),
            Npc("npc_rank_dead", "Iron Circle", 90, 900, 900, alive: false),
        };
        var viewer = new Character { Name2 = "viewer", Team = "Iron Circle", Level = 25, Strength = 250, Defence = 250 };

        var rows = TeamCornerLocation.BuildTeamRankings(npcs, await _db.GetTeamRankingStats("VIEWER"), viewer);

        var row = rows.Single(r => r.TeamName == "Iron Circle");
        row.MemberCount.Should().Be(5, "two living NPCs, two players, the viewer once");
        row.AverageLevel.Should().Be((30 + 40 + 10 + 20 + 25) / 5);
        row.TotalPower.Should().Be(630 + 840 + 210 + 220 + 525);
        row.IsPlayerTeam.Should().BeTrue();
    }

    [Fact]
    public async Task TheViewerWhoseSaveHasNoTeamYet_IsCountedFromMemory()
    {
        Team("Grey Company");
        Player("alpha", "Grey Company", 10, 100, 50);
        Player("viewer", null, 5, 10, 10);
        var viewer = new Character { Name2 = "viewer", Team = "Grey Company", Level = 12, Strength = 20, Defence = 20 };

        var row = TeamCornerLocation.BuildTeamRankings(Array.Empty<NPC>(), await _db.GetTeamRankingStats("viewer"), viewer).Single();
        row.MemberCount.Should().Be(2);
        row.TotalPower.Should().Be(160 + 52);
    }

    [Fact]
    public async Task ATeamWithNoMembers_IsNotListed()
    {
        Team("Grey Company");
        Team("Nobody Left", turf: true);
        Team("Only Dead");
        Player("alpha", "Grey Company", 10, 100, 50);
        Player("exiled", "Nobody Left", 10, 100, 50, banned: true);
        var npcs = new[] { Npc("npc_rank_gone", "Only Dead", 30, 300, 300, alive: false) };

        var rows = TeamCornerLocation.BuildTeamRankings(npcs, await _db.GetTeamRankingStats(null), null);
        rows.Select(r => r.TeamName).Should().BeEquivalentTo(new[] { "Grey Company" });
    }
}
