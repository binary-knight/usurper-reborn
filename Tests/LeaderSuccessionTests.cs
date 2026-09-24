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
/// v1.1.11: when a team's or a guild's leader leaves, is deleted or dies for good, leadership passes to
/// the highest-level remaining player member: the same level goes to the earliest joiner where a join
/// time is recorded (guilds), then the username in ordinal order. Banned players and emergency accounts
/// are never chosen. The world save also passes on teams whose leader is a known character no longer on
/// them, but never a team whose leader key matches no character (the admin's Fix Team Leaders screen).
/// </summary>
[Collection("SharedGameSingletons")]
public class LeaderSuccessionTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-lst-{Guid.NewGuid():N}.db");
    private readonly SqlSaveBackend _db;

    public LeaderSuccessionTests() { _db = new SqlSaveBackend(_path); }

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

    private string? Scalar(string sql)
    {
        using var conn = new SqliteConnection($"Data Source={_path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar()?.ToString();
    }

    private void Player(string key, string? team, int level = 10, bool banned = false) =>
        Exec($"INSERT INTO players (username, display_name, player_data, is_banned) VALUES ('{key}', '{key}', " +
             $"'{{\"player\":{{\"level\":{level}{(team != null ? $",\"team\":\"{team}\"" : "")}}}}}', {(banned ? 1 : 0)});");

    private void Team(string name, string createdBy) =>
        Exec($"INSERT INTO player_teams (team_name, password_hash, created_by) VALUES ('{name}', 'x', '{createdBy}');");

    private string? Leader(string team) => Scalar($"SELECT created_by FROM player_teams WHERE team_name = '{team}'");

    private string? GuildLeader(string guild) => Scalar($"SELECT leader_username FROM guilds WHERE name = '{guild}'");

    private void Guild(string name, string leader) =>
        Exec($"INSERT INTO guilds (name, display_name, leader_username) VALUES ('{name}', '{name}', '{leader}');");

    private void GuildMember(string user, string guild, string joinedAt, string rank = "Member") =>
        Exec($"INSERT INTO guild_members (username, guild_name, rank, joined_at) VALUES ('{user}', '{guild}', '{rank}', '{joinedAt}');");

    [Fact]
    public void TheHighestLevelMember_TakesOver()
    {
        Player("founder", null, level: 50);
        Player("bran", "Black Band", level: 20);
        Player("tomas", "Black Band", level: 35);
        Team("Black Band", "founder");

        _db.TryPassTeamLeadership("Black Band", "founder", "founder", requireOldLeaderGone: true, out var next).Should().BeTrue();
        next.Should().Be("tomas");
        Leader("Black Band").Should().Be("tomas");
    }

    [Fact]
    public void TheTieBreak_IsLevel_ThenEarliestJoin_ThenOrdinalUsername()
    {
        SqlSaveBackend.PickSuccessor(new[] { ("zed", 30, (string?)null), ("amy", 30, null), ("Zoe", 30, null) })
            .Should().Be("Zoe", "ordinal order puts upper case before lower case");
        SqlSaveBackend.PickSuccessor(new[] { ("amy", 30, (string?)"2026-05-01 00:00:00"), ("zed", 30, "2026-01-01 00:00:00") })
            .Should().Be("zed", "the earlier joiner wins a tie");
        SqlSaveBackend.PickSuccessor(new[] { ("amy", 30, (string?)null), ("zed", 30, "2026-01-01 00:00:00") })
            .Should().Be("zed", "a recorded join time comes before none");
        SqlSaveBackend.PickSuccessor(new[] { ("amy", 30, (string?)"2026-01-01 00:00:00"), ("zed", 31, "2026-05-01 00:00:00") })
            .Should().Be("zed", "level comes first");
        SqlSaveBackend.PickSuccessor(Array.Empty<(string, int, string?)>()).Should().BeNull();

        // a team records no join time per member, so the same level goes by username
        Player("founder", null);
        Player("mira", "Tied", level: 30);
        Player("bran", "Tied", level: 30);
        Team("Tied", "founder");
        _db.TryPassTeamLeadership("Tied", "founder", "founder", requireOldLeaderGone: true, out _).Should().BeTrue();
        Leader("Tied").Should().Be("bran");
    }

    [Fact]
    public void BannedAndEmergencyMembers_AreSkipped()
    {
        Player("founder", null);
        Player("exiled", "Watch", level: 90, banned: true);
        Player("emergency_1", "Watch", level: 80);
        Player("tomas", "Watch", level: 5);
        Team("Watch", "founder");
        _db.TryPassTeamLeadership("Watch", "founder", "founder", requireOldLeaderGone: true, out _).Should().BeTrue();
        Leader("Watch").Should().Be("tomas");

        Player("founder2", null);
        Player("exiled2", "Only Banned", level: 90, banned: true);
        Team("Only Banned", "founder2");
        _db.TryPassTeamLeadership("Only Banned", "founder2", "founder2", requireOldLeaderGone: true, out _).Should().BeFalse();
        Leader("Only Banned").Should().Be("founder2", "with no successor the key stays");
    }

    [Fact]
    public void TheWorldSave_PassesOnATeamWhoseKnownLeaderLeft()
    {
        Player("founder", "Other Team", level: 60);   // a real account, now elsewhere
        Player("bran", "Black Band", level: 20);
        Team("Black Band", "founder");
        Player("stays", "Kept", level: 20);
        Player("second", "Kept", level: 40);
        Team("Kept", "stays");

        Exec("INSERT INTO players (username, display_name, player_data) VALUES ('gone', 'gone', '{}');");   // deleted: DeleteGameData leaves '{}'
        Player("tomas", "Gone Leader", level: 20);
        Team("Gone Leader", "gone");

        new WorldSimService(_db).PassLeadershipOfDepartedLeaders().Should().Be(2);
        Leader("Black Band").Should().Be("bran");
        Leader("Gone Leader").Should().Be("tomas");
        Leader("Kept").Should().Be("stays", "a leader still on the team keeps it");
    }

    [Fact]
    public void TheWorldSave_LeavesAnUnknownLeaderKeyAlone()
    {
        Player("bran", "Old Guard", level: 20);
        Team("Old Guard", "mira vale");   // an old display-name key: the admin's Fix Team Leaders screen maps it

        new WorldSimService(_db).PassLeadershipOfDepartedLeaders().Should().Be(0);
        Leader("Old Guard").Should().Be("mira vale");
    }

    [Fact]
    public void TheWorldSave_SkipsATeamJoinedWithinTheGrace()
    {
        Player("founder", null, level: 60);
        Player("bran", "Just Joined", level: 20);
        Team("Just Joined", "founder");
        Exec("UPDATE player_teams SET last_join_at = datetime('now') WHERE team_name = 'Just Joined';");

        var service = new WorldSimService(_db);
        service.PassLeadershipOfDepartedLeaders().Should().Be(0, "the joiner may be the leader, whose save has not landed");
        Leader("Just Joined").Should().Be("founder");

        Exec($"UPDATE player_teams SET last_join_at = datetime('now', '-{GameConfig.EmptyTeamJoinGraceMinutes + 1} minutes') WHERE team_name = 'Just Joined';");
        service.PassLeadershipOfDepartedLeaders().Should().Be(1);
        Leader("Just Joined").Should().Be("bran");
    }

    [Fact]
    public void TheWorldSavesUpdate_RechecksTheJoinGrace()
    {
        // v1.1.11: the pass listed the team before the founder rejoined; the rejoin stamped last_join_at but
        // the founder's save (naming the team) has not landed. The update itself must see the stamp.
        Player("founder", null, level: 60);
        Player("bran", "Rejoined", level: 20);
        Team("Rejoined", "founder");
        Exec("UPDATE player_teams SET last_join_at = datetime('now') WHERE team_name = 'Rejoined';");

        _db.TryPassTeamLeadership("Rejoined", "founder", "founder", requireOldLeaderGone: true, out var next, respectJoinGrace: true)
            .Should().BeFalse();
        next.Should().BeNull();
        Leader("Rejoined").Should().Be("founder");

        Exec($"UPDATE player_teams SET last_join_at = datetime('now', '-{GameConfig.EmptyTeamJoinGraceMinutes + 1} minutes') WHERE team_name = 'Rejoined';");
        _db.TryPassTeamLeadership("Rejoined", "founder", "founder", requireOldLeaderGone: true, out _, respectJoinGrace: true).Should().BeTrue();
        Leader("Rejoined").Should().Be("bran");

        CodeOnly(Source("Systems", "WorldSimService.cs")).Should().Contain("respectJoinGrace: true");
    }

    [Fact]
    public void AGuildSuccessor_WhoLeftBeforeTheSuccession_IsNotAppointed()
    {
        var guilds = new GuildSystem(_path, register: false);
        Player("boss", null, level: 90);
        Player("top", null, level: 80);
        Player("next", null, level: 40);
        Guild("oakhall", "boss");
        GuildMember("top", "oakhall", "2026-01-01 00:00:00");
        GuildMember("next", "oakhall", "2026-01-02 00:00:00");

        Exec("DELETE FROM guild_members WHERE username = 'top';");   // the first choice left
        guilds.PassLeadership("oakhall", "boss").Should().Be("next");
        GuildLeader("oakhall").Should().Be("next");
        Scalar("SELECT rank FROM guild_members WHERE username = 'next'").Should().Be("Leader");

        // the candidates are read inside the transaction, and a rank update that changes no row rolls back
        string src = CodeOnly(Source("Systems", "GuildSystem.cs"));
        int start = src.IndexOf("public string? PassLeadership(", StringComparison.Ordinal);
        string body = src.Substring(start, src.IndexOf("public int PassLeadershipOf(", start, StringComparison.Ordinal) - start);
        body.IndexOf("BeginTransaction(", StringComparison.Ordinal).Should().BeLessThan(body.IndexOf("SELECT gm.username", StringComparison.Ordinal));
        body.Should().Contain("cmd.Transaction = tx;");
        body.Should().Contain("if (rank.ExecuteNonQuery() != 1) { tx.Rollback(\"succession\"); return null; }");
    }

    [Fact]
    public void AGuildLeaderWhoLeaves_IsSucceededByTheHighestLevelMember()
    {
        var guilds = new GuildSystem(_path, register: false);
        Player("boss", null, level: 90);
        Player("officer", null, level: 20);
        Player("veteran", null, level: 40);
        Player("newbie", null, level: 40);
        Player("exiled", null, level: 99, banned: true);
        Guild("ironhand", "boss");
        GuildMember("boss", "ironhand", "2026-01-01 00:00:00", "Leader");
        GuildMember("officer", "ironhand", "2026-01-02 00:00:00", "Officer");
        GuildMember("newbie", "ironhand", "2026-03-01 00:00:00");
        GuildMember("veteran", "ironhand", "2026-02-01 00:00:00");
        GuildMember("exiled", "ironhand", "2026-01-01 00:00:00");

        Exec("DELETE FROM guild_members WHERE username = 'boss';");   // as the delete purge does
        guilds.PassLeadershipOf("boss").Should().Be(1);
        GuildLeader("ironhand").Should().Be("veteran", "level first, then the earlier joiner; the banned player is skipped");
        Scalar("SELECT rank FROM guild_members WHERE username = 'veteran'").Should().Be("Leader");

        guilds.PassLeadership("ironhand", "someone else").Should().BeNull("the guild is no longer theirs");
        GuildLeader("ironhand").Should().Be("veteran");
    }

    [Fact]
    public void AGuildWithNoMembersLeft_IsLeftAlone()
    {
        var guilds = new GuildSystem(_path, register: false);
        Player("boss", null, level: 90);
        Guild("empty hall", "boss");

        guilds.PassLeadershipOf("boss").Should().Be(0);
        GuildLeader("empty hall").Should().Be("boss");
        Scalar("SELECT COUNT(*) FROM guilds WHERE name = 'empty hall'").Should().Be("1", "succession never deletes a guild");
    }

    [Fact]
    public async Task TheDeletePurge_PassesOnTheTeam_AndItsGuildToo()
    {
        Player("leader", "Black Band", level: 60);   // the row is still there when the purge runs
        Player("bran", "Black Band", level: 20);
        Team("Black Band", "leader");

        await PermadeathHelper.PurgeDeletedCharacterAsync(_db, "leader", "Leader");
        Leader("Black Band").Should().Be("bran");

        CodeOnly(Source("Systems", "PermadeathHelper.cs")).Should().Contain("guilds?.PassLeadershipOf(username!)");
    }

    [Fact]
    public async Task TheDeletePurge_InADoorProcessWithNoGuildSystem_StillPassesOnTheGuild()
    {
        // v1.1.11: only the MUD server makes a GuildSystem; a --online --stdio door process has none
        _ = new GuildSystem(_path, register: false);   // creates the guild tables
        Player("gm", null, level: 70);
        Player("heir", null, level: 30);
        Guild("oak hall", "gm");
        GuildMember("gm", "oak hall", "2026-01-01 00:00:00", "Leader");
        GuildMember("heir", "oak hall", "2026-01-02 00:00:00");

        var instance = typeof(GuildSystem).GetProperty("Instance")!;
        var before = instance.GetValue(null);
        instance.SetValue(null, null);
        try
        {
            await PermadeathHelper.PurgeDeletedCharacterAsync(_db, "gm", "Gm");
        }
        finally { instance.SetValue(null, before); }
        GuildLeader("oak hall").Should().Be("heir");
        Scalar("SELECT rank FROM guild_members WHERE username = 'heir'").Should().Be("Leader");
    }

    [Fact]
    public void TheQuitPath_AndTheLeaveCommand_PassOnLeadership()
    {
        string quit = CodeOnly(Source("Locations", "TeamCornerLocation.cs"));
        int start = quit.IndexOf("private async Task QuitTeam()", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0);
        int end = quit.IndexOf("\n    private ", start + 10, StringComparison.Ordinal);
        string body = quit.Substring(start, end - start);
        body.Should().Contain("GameEngine.InheritanceKey(currentPlayer)");
        body.Should().Contain("TryPassTeamLeadership(oldTeam, myKey, myKey");
        body.IndexOf("TryPassTeamLeadership(", StringComparison.Ordinal)
            .Should().BeGreaterThan(body.IndexOf("AutoSave(currentPlayer)", StringComparison.Ordinal), "after the save records the quit");

        string guild = CodeOnly(Source("Systems", "GuildSystem.cs"));
        int rm = guild.IndexOf("public string? RemoveMember(", StringComparison.Ordinal);
        guild.IndexOf("PassLeadershipInTx(conn, tx, guildName, leader!)", rm, StringComparison.Ordinal).Should().BeGreaterThan(rm);

        string sim = CodeOnly(Source("Systems", "WorldSimService.cs"));
        int prune = sim.IndexOf("PruneEmptyTeams();", StringComparison.Ordinal);
        sim.IndexOf("PassLeadershipOfDepartedLeaders();", prune, StringComparison.Ordinal).Should().BeGreaterThan(prune, "after the empty-team cleanup");
    }

    [Fact]
    public void CodeOnly_IgnoresACommentedOutCall()
    {
        CodeOnly("        // backend.TryPassTeamLeadership(oldTeam, myKey, myKey, true, out _);").Should().NotContain("TryPassTeamLeadership(");
    }

    // ─── v1.1.11: review round 13 ───

    [Fact]
    public void ALeavingMember_WhoseGuildLeaderIsNoLongerAMember_TriggersSuccession()
    {
        // the leader left (and passed the guild to b) while b was leaving: b's delete finds a leader who is
        // not a member and passes it on, in the same transaction
        _ = new GuildSystem(_path, register: false);   // creates the guild tables
        Player("b", null, level: 50);
        Player("c", null, level: 30);
        Guild("racehall", "b");
        GuildMember("b", "racehall", "2026-01-01 00:00:00", "Leader");
        GuildMember("c", "racehall", "2026-01-02 00:00:00");
        var guilds = new GuildSystem(_path, register: false);   // the membership cache is read at start
        guilds.RemoveMember("b").Should().BeNull();
        GuildLeader("racehall").Should().Be("c");
        Scalar("SELECT rank FROM guild_members WHERE username = 'c'").Should().Be("Leader");

        // a stale leader (not a member) is repaired when anyone leaves
        Player("gone", null, level: 90);
        Player("d", null, level: 20);
        Player("e", null, level: 40);
        Guild("stalehall", "gone");
        GuildMember("d", "stalehall", "2026-01-01 00:00:00");
        GuildMember("e", "stalehall", "2026-01-02 00:00:00");
        guilds = new GuildSystem(_path, register: false);
        guilds.RemoveMember("d").Should().BeNull();
        GuildLeader("stalehall").Should().Be("e");

        GuildSystem.NeedsGuildSuccession("a", leaderIsMember: false).Should().BeTrue();
        GuildSystem.NeedsGuildSuccession("a", leaderIsMember: true).Should().BeFalse();
        GuildSystem.NeedsGuildSuccession(null, leaderIsMember: false).Should().BeFalse();

        // the interleaving itself is not deterministic: the order is checked in the source instead
        string src = CodeOnly(Source("Systems", "GuildSystem.cs"));
        int rm = src.IndexOf("public string? RemoveMember(", StringComparison.Ordinal);
        string body = src.Substring(rm, src.IndexOf("public string? DepositGold(", rm, StringComparison.Ordinal) - rm);
        int begin = body.IndexOf("BeginTransaction(deferred: false)", StringComparison.Ordinal);
        int del = body.IndexOf("DELETE FROM guild_members", StringComparison.Ordinal);
        int read = body.IndexOf("SELECT g.leader_username", StringComparison.Ordinal);
        int pass = body.IndexOf("PassLeadershipInTx(conn, tx,", StringComparison.Ordinal);
        int commit = body.IndexOf("tx.Commit();", StringComparison.Ordinal);
        begin.Should().BeGreaterThan(0);
        del.Should().BeGreaterThan(begin);
        read.Should().BeGreaterThan(del, "the leader is read after the delete, in the transaction");
        pass.Should().BeGreaterThan(read);
        commit.Should().BeGreaterThan(pass);
        body.Should().NotContain("bool isLeader");
    }

    [Fact]
    public void ATeamSuccessor_BannedBetweenSelectionAndUpdate_DoesNotGetTheTeam()
    {
        Player("founder", null, level: 50);
        Player("tomas", "Ravens", level: 40);
        Team("Ravens", "founder");
        SqlSaveBackend.BeforeTeamLeaderUpdateForTests = chosen => Exec($"UPDATE players SET is_banned = 1 WHERE username = '{chosen}';");
        try
        {
            _db.TryPassTeamLeadership("Ravens", "founder", "founder", requireOldLeaderGone: true, out var next).Should().BeFalse();
            next.Should().BeNull();
            Leader("Ravens").Should().Be("founder", "the banned successor is refused by the update itself");
        }
        finally { SqlSaveBackend.BeforeTeamLeaderUpdateForTests = null; }

        // one who left the team in that window is refused too
        Player("founder2", null, level: 50);
        Player("mira", "Owls", level: 40);
        Team("Owls", "founder2");
        SqlSaveBackend.BeforeTeamLeaderUpdateForTests = chosen => Exec($"UPDATE players SET player_data = '{{\"player\":{{\"level\":40}}}}' WHERE username = '{chosen}';");
        try { _db.TryPassTeamLeadership("Owls", "founder2", "founder2", requireOldLeaderGone: true, out _).Should().BeFalse(); }
        finally { SqlSaveBackend.BeforeTeamLeaderUpdateForTests = null; }
        Leader("Owls").Should().Be("founder2");

        // with nothing in between it passes as before
        _db.TryPassTeamLeadership("Ravens", "founder", "founder", requireOldLeaderGone: true, out _).Should().BeFalse("tomas is banned now");
        Player("bran", "Ravens", level: 10);
        _db.TryPassTeamLeadership("Ravens", "founder", "founder", requireOldLeaderGone: true, out var after).Should().BeTrue();
        after.Should().Be("bran");
    }

    private static string Source(string folder, string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Scripts"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "Scripts", folder, file));
    }

    /// <summary>The source with // comments removed, as in CharacterRecreationTests.</summary>
    private static string CodeOnly(string src) =>
        string.Join("\n", src.Split('\n').Select(line =>
        {
            int c = line.IndexOf("//", StringComparison.Ordinal);
            return c >= 0 ? line.Substring(0, c) : line;
        }));
}
