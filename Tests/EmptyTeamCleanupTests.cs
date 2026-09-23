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
/// v1.1.11: the team rankings read a stored member count that was refreshed only when someone opened
/// that team's roster, so teams with players in them were listed with 0 members; and a team everyone
/// had left stayed listed for ever. The count is now counted when the teams are read, and a team that
/// no player's save, no NPC and no online player names is removed, with its upgrades and vault.
/// </summary>
[Collection("SharedGameSingletons")]
public class EmptyTeamCleanupTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-etc-{Guid.NewGuid():N}.db");
    private readonly SqlSaveBackend _db;

    public EmptyTeamCleanupTests() { _db = new SqlSaveBackend(_path); }

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

    private int Count(string sql)
    {
        using var conn = new SqliteConnection($"Data Source={_path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private void Player(string key, string? team, bool banned = false) =>
        Exec($"INSERT INTO players (username, display_name, player_data, is_banned) VALUES ('{key}', '{key}', " +
             $"'{{\"player\":{{\"level\":10{(team != null ? $",\"team\":\"{team}\"" : "")}}}}}', {(banned ? 1 : 0)});");

    private void Team(string name, int storedCount = 0) =>
        Exec($"INSERT INTO player_teams (team_name, password_hash, created_by, member_count) VALUES ('{name}', 'x', 'founder', {storedCount});");

    [Fact]
    public async Task TheRankings_CountTheMembers_NotTheStoredNumber()
    {
        Player("bran", "Black Band");
        Player("tomas", "Black Band");
        Team("Black Band", storedCount: 0);   // the stale column that showed 0
        var team = (await _db.GetPlayerTeams()).Single(t => t.TeamName == "Black Band");
        team.MemberCount.Should().Be(2);
    }

    [Fact]
    public void OnlyATeamNoPlayersSaveNames_IsACandidate_AndABannedMemberStillCounts()
    {
        Player("bran", "Kept");
        Player("exiled", "Banned Only", banned: true);
        Team("Kept");
        Team("Banned Only");
        Team("Nobody Left");
        _db.GetTeamsWithoutPlayerMembers().Should().BeEquivalentTo(new[] { "Nobody Left" });
    }

    [Fact]
    public void RemovingAnEmptyTeam_TakesItsUpgradesAndVault_AndOnlyIfStillEmpty()
    {
        Team("Nobody Left");
        Exec("INSERT INTO team_upgrades (team_name, upgrade_type, level) VALUES ('Nobody Left', 'armory', 2);");
        Exec("INSERT INTO team_vault (team_name, gold) VALUES ('Nobody Left', 100);");
        Team("Rejoined");
        Player("tomas", "Rejoined");   // joined after the list was read

        _db.DeleteEmptyTeam("Rejoined").Should().BeFalse("a player's save names it");
        Count("SELECT COUNT(*) FROM player_teams WHERE team_name = 'Rejoined'").Should().Be(1);

        _db.DeleteEmptyTeam("Nobody Left").Should().BeTrue();
        Count("SELECT COUNT(*) FROM player_teams WHERE team_name = 'Nobody Left'").Should().Be(0);
        Count("SELECT COUNT(*) FROM team_upgrades WHERE team_name = 'Nobody Left'").Should().Be(0, "a new team of that name must not inherit them");
        Count("SELECT COUNT(*) FROM team_vault WHERE team_name = 'Nobody Left'").Should().Be(0);
    }

    /// <summary>Runs the cleanup against a roster that is plausibly complete (NPCSpawnSystem.IsCountPlausible).</summary>
    private static T WithRoster<T>(Func<T> run, params NPC[] members)
    {
        var spawner = NPCSpawnSystem.Instance;
        var added = new System.Collections.Generic.List<NPC>(members);
        for (int i = added.Count; i < 60; i++)
            added.Add(new NPC { ID = $"npc_team_filler_{i}", Name1 = $"Filler {i}", Name2 = $"Filler {i}", Level = 5, HP = 50, MaxHP = 50 });
        foreach (var n in added) spawner.ActiveNPCs.Add(n);
        try { return run(); }
        finally { foreach (var n in added) spawner.ActiveNPCs.Remove(n); }
    }

    [Fact]
    public void TheWorldSave_RemovesATeamOnlyAfterItHasStayedEmpty_AndNeverOneAnNPCIsIn()
    {
        Team("Nobody Left");
        Team("NPC Held");
        var npc = new NPC { ID = "npc_empty_team_test", Name1 = "Hold", Name2 = "Hold", Team = "NPC Held", Level = 5, HP = 50, MaxHP = 50 };
        var service = new WorldSimService(_db);
        var t0 = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);

        WithRoster(() => service.PruneEmptyTeams(t0), npc).Should().Be(0, "the first sighting only starts the clock");
        WithRoster(() => service.PruneEmptyTeams(t0.AddMinutes(GameConfig.EmptyTeamGraceMinutes - 1)), npc).Should().Be(0);
        WithRoster(() => service.PruneEmptyTeams(t0.AddMinutes(GameConfig.EmptyTeamGraceMinutes)), npc).Should().Be(1);
        Count("SELECT COUNT(*) FROM player_teams WHERE team_name = 'Nobody Left'").Should().Be(0);
        Count("SELECT COUNT(*) FROM player_teams WHERE team_name = 'NPC Held'").Should().Be(1, "an NPC is still on the team");
    }

    [Fact]
    public void ATeamSomeoneJoinsBetweenPasses_IsKept()
    {
        Team("Rejoined Later");
        var service = new WorldSimService(_db);
        var t0 = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
        WithRoster(() => service.PruneEmptyTeams(t0));
        Player("tomas", "Rejoined Later");   // joined and saved during the grace period
        WithRoster(() => service.PruneEmptyTeams(t0.AddMinutes(GameConfig.EmptyTeamGraceMinutes + 5))).Should().Be(0);
        Player("tomas2", null);
        Exec("UPDATE players SET player_data = '{\"player\":{\"level\":10}}' WHERE username = 'tomas';");   // left again
        WithRoster(() => service.PruneEmptyTeams(t0.AddMinutes(GameConfig.EmptyTeamGraceMinutes + 10))).Should().Be(0, "the clock starts again");
        Count("SELECT COUNT(*) FROM player_teams WHERE team_name = 'Rejoined Later'").Should().Be(1);
    }

    [Fact]
    public void WhileTheNPCRosterIsBeingRebuilt_NothingIsRemoved()
    {
        Team("Nobody Left");
        var service = new WorldSimService(_db);
        var t0 = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
        WithRoster(() => service.PruneEmptyTeams(t0));
        var spawner = NPCSpawnSystem.Instance;
        spawner.IsRebuilding = true;
        try { WithRoster(() => service.PruneEmptyTeams(t0.AddHours(1))).Should().Be(0); }
        finally { spawner.IsRebuilding = false; }
        service.PruneEmptyTeams(t0.AddHours(2)).Should().Be(0, "an implausibly small roster proves nothing either");
        Count("SELECT COUNT(*) FROM player_teams WHERE team_name = 'Nobody Left'").Should().Be(1);
    }

    [Fact]
    public void WhileASaveIsMalformed_NoTeamIsRemoved()
    {
        // A malformed save cannot say which team it names; its player may be the last member.
        using (var conn = new SqliteConnection($"Data Source={_path}"))
        {
            conn.Open();
            var names = new System.Collections.Generic.List<string>();
            using (var q = conn.CreateCommand())
            {
                q.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'players' AND sql LIKE '%json%';";
                using var r = q.ExecuteReader();
                while (r.Read()) names.Add(r.GetString(0));
            }
            foreach (var name in names) { using var d = conn.CreateCommand(); d.CommandText = $"DROP INDEX \"{name}\";"; d.ExecuteNonQuery(); }
        }
        Exec("INSERT INTO players (username, display_name, player_data) VALUES ('broken', 'Broken', '{not json');");
        Team("Nobody Left");

        _db.GetTeamsWithoutPlayerMembers().Should().BeEmpty();
        _db.DeleteEmptyTeam("Nobody Left").Should().BeFalse();
        Count("SELECT COUNT(*) FROM player_teams WHERE team_name = 'Nobody Left'").Should().Be(1);
    }
}
