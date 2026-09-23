using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.10: teams founded before v1.1.10 recorded the founder's display name, lowercased, as their
/// leader key. It matches no character, so a dying NPC member's bequest was queued under it, never
/// delivered, and deleted by the orphan sweep. Nothing in the saves says who founded a team, so the
/// admin console sets each one by hand, to a current member, with a confirmation.
/// </summary>
[Collection("SharedGameSingletons")]
public class TeamLeaderFixTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-tlf-{Guid.NewGuid():N}.db");
    private readonly SqlSaveBackend _db;

    public TeamLeaderFixTests() { _db = new SqlSaveBackend(_path); }

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

    private void Player(string key, string display, string? team, int level = 30) =>
        Exec($"INSERT INTO players (username, display_name, player_data) VALUES ('{key}', '{display}', " +
             $"'{{\"player\":{{\"level\":{level}{(team != null ? $",\"team\":\"{team}\"" : "")}}}}}');");

    private void Team(string name, string createdBy) =>
        Exec($"INSERT INTO player_teams (team_name, password_hash, created_by) VALUES ('{name}', 'x', '{createdBy}');");

    private string? Leader(string team) => Scalar($"SELECT created_by FROM player_teams WHERE team_name = '{team}'");

    private int Queued(string key) => int.Parse(Scalar($"SELECT COUNT(*) FROM pending_inheritance WHERE player_username = '{key}'")!);

    [Fact]
    public async Task OnlyTeamsWhoseKeyMatchesNoCharacter_AreListed_WithTheirMembers()
    {
        Player("bran", "Bran Holloway", "Right Already");
        Player("mira__alt", "Mira Vale", "Old Guard");
        Player("tomas", "Tomas", "Old Guard");
        Team("Right Already", "bran");
        Team("Old Guard", "mira vale");
        _db.QueueInheritance("mira vale", "Aldric", "{\"name\":\"Aldric's sword\"}").Should().BeTrue();

        var teams = await _db.GetTeamsWithUnknownLeader();
        teams.Should().ContainSingle().Which.TeamName.Should().Be("Old Guard");
        teams[0].OldKey.Should().Be("mira vale");
        teams[0].QueuedBequests.Should().Be(1);
        teams[0].Members.Select(m => m.Username).Should().BeEquivalentTo(new[] { "mira__alt", "tomas" });
        OnlineAdminConsole.SuggestTeamLeader(teams[0])!.Username.Should().Be("mira__alt", "her display name is the old key");
    }

    [Fact]
    public void SettingTheLeader_MovesTheWaitingBequests()
    {
        Player("mira__alt", "Mira Vale", "Old Guard");
        Team("Old Guard", "mira vale");
        _db.QueueInheritance("mira vale", "Aldric", "{\"name\":\"Aldric's sword\"}").Should().BeTrue();

        _db.SetTeamLeaderKey("Old Guard", "mira vale", "mira__alt").Should().BeTrue();
        Leader("Old Guard").Should().Be("mira__alt");
        _db.GetPendingInheritance("mira__alt").Should().ContainSingle("the waiting bequest follows the key instead of being swept");
        Queued("mira vale").Should().Be(0);
    }

    [Fact]
    public void TheLeader_MustBeACurrentMemberOfTheTeam()
    {
        // The reverted automatic repair's failure: a stranger who takes a founder's old display name.
        Player("stranger", "Mira Vale", "Other Team");
        Player("tomas", "Tomas", "Old Guard");
        Team("Old Guard", "mira vale");

        _db.SetTeamLeaderKey("Old Guard", "mira vale", "stranger").Should().BeFalse("not a member of the team");
        _db.SetTeamLeaderKey("Old Guard", "mira vale", "nobody").Should().BeFalse("not a character");
        Leader("Old Guard").Should().Be("mira vale");
        _db.SetTeamLeaderKey("Old Guard", "mira vale", "tomas").Should().BeTrue();
    }

    [Fact]
    public void AKeyChangedMeanwhile_IsNotOverwritten()
    {
        Player("tomas", "Tomas", "Old Guard");
        Team("Old Guard", "tomas");   // already set, by another admin or a new founding
        _db.SetTeamLeaderKey("Old Guard", "mira vale", "tomas").Should().BeFalse();
        Leader("Old Guard").Should().Be("tomas");
    }

    [Fact]
    public void AnOldKeyTwoTeamsShare_NeitherTeamTakesTheOthersBequests()
    {
        // Two teams founded under the same name: what waits under the key could be either team's. Codex
        // round 10: leaving it there let the second team fixed take both teams' bequests.
        Player("mira__alt", "Mira Vale", "Old Guard");
        Player("mira", "Mira Vale", "New Guard");
        Team("Old Guard", "mira vale");
        Team("New Guard", "mira vale");
        _db.QueueInheritance("mira vale", "Aldric", "{\"name\":\"Aldric's sword\"}").Should().BeTrue();

        _db.SetTeamLeaderKey("Old Guard", "mira vale", "mira__alt").Should().BeTrue();
        Queued("mira vale").Should().Be(0, "set aside, not left for the next fix");
        Queued("mira__alt").Should().Be(0);

        _db.QueueTeamInheritance("New Guard", "Bryn", "{\"name\":\"Bryn's shield\"}").Should().BeTrue();   // only New Guard's
        _db.SetTeamLeaderKey("New Guard", "mira vale", "mira").Should().BeTrue();
        _db.GetPendingInheritance("mira").Should().ContainSingle().Which.ItemJson.Should().Contain("Bryn", "New Guard's own bequest follows it, and only that");
    }

    [Fact]
    public void ABequestQueuedForATeam_GoesToTheLeaderKeyAtThatMoment()
    {
        // Codex round 10: the estate is queued item by item after the leader was read, so an admin fix in
        // between left the rest under the old key for the sweep. Each row now reads the key as it is queued.
        Player("mira__alt", "Mira Vale", "Old Guard");
        Team("Old Guard", "mira vale");
        _db.QueueTeamInheritance("Old Guard", "Aldric", "{\"name\":\"before\"}").Should().BeTrue();
        _db.SetTeamLeaderKey("Old Guard", "mira vale", "mira__alt").Should().BeTrue();
        _db.QueueTeamInheritance("Old Guard", "Aldric", "{\"name\":\"after\"}").Should().BeTrue();

        _db.GetPendingInheritance("mira__alt").Should().HaveCount(2, "the row from before is moved by the fix, the one after is queued under the new key");
        _db.QueueTeamInheritance("No Such Team", "Aldric", "{}").Should().BeFalse();
    }

    [Fact]
    public async Task AMalformedSave_DoesNotHideTheMembersOfEveryTeam()
    {
        // A fresh database's expression indexes refuse a malformed blob; an upgraded one whose index build
        // hit such a blob started without them (Codex round 10), so this drops them first.
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
        Player("mira__alt", "Mira Vale", "Old Guard");
        Team("Old Guard", "mira vale");
        var teams = await _db.GetTeamsWithUnknownLeader();
        teams.Should().ContainSingle().Which.Members.Should().ContainSingle(m => m.Username == "mira__alt");
    }

    [Fact]
    public void TwoMembersWithTheKeysName_GiveNoSuggestion()
    {
        var team = new SqlSaveBackend.TeamWithUnknownLeader
        {
            TeamName = "Old Guard", OldKey = "twin",
            Members = { new PlayerSummary { Username = "twin_a", DisplayName = "Twin" }, new PlayerSummary { Username = "twin_b", DisplayName = "Twin" } }
        };
        OnlineAdminConsole.SuggestTeamLeader(team).Should().BeNull();
    }

    // ─── the console flow ───

    private sealed class ScriptedStream : Stream
    {
        private readonly byte[] _data; private int _pos;
        public ScriptedStream(string script) { _data = Encoding.UTF8.GetBytes(script); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos >= _data.Length) return 0;
            int n = Math.Min(count, _data.Length - _pos); Array.Copy(_data, _pos, buffer, offset, n); _pos += n; return n;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.FromResult(Read(buffer, offset, count));
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => _data.Length; public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    private async Task<string> RunConsole(string script)
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new ScriptedStream(script), output);
        await new OnlineAdminConsole(term, _db).FixTeamLeaders();
        term.StreamWriterInternal!.Flush();
        return Encoding.UTF8.GetString(output.ToArray());
    }

    [Fact]
    public async Task TheConsole_SetsTheSuggestedLeader_OnlyAfterConfirmation()
    {
        Player("mira__alt", "Mira Vale", "Old Guard");
        Team("Old Guard", "mira vale");

        await RunConsole("\nN\n\n");            // accept the suggestion, then decline the confirmation
        Leader("Old Guard").Should().Be("mira vale", "nothing is written without a yes");

        var shown = await RunConsole("\nY\n\n\n");
        Leader("Old Guard").Should().Be("mira__alt");
        shown.Should().Contain("Leaders set: 1 of 1");
    }

    [Fact]
    public async Task TheConsole_WithTwoMatchingMembers_StillAllowsANumberedPick()
    {
        Player("twin_a", "Twin", "Old Guard", level: 40);
        Player("twin_b", "Twin", "Old Guard", level: 30);
        Team("Old Guard", "twin");

        var shown = await RunConsole("\n\n");   // Enter alone does nothing without a suggestion
        shown.Should().Contain("No single member's name matches the key");
        Leader("Old Guard").Should().Be("twin", "Enter skipped the team: there was nothing to accept");

        await RunConsole("2\nY\n\n\n");
        Leader("Old Guard").Should().Be("twin_b", "members are listed by level, highest first");
    }
}
