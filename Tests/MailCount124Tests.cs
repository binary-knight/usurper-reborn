using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.4: every mail row has exactly one owner (SqlSaveBackend.MailOwnedBy): a display name first, else
/// a save key no other player goes by, else nobody. The login count, inbox, read marking, deletion and
/// the chat poll all use that rule, and new mail is written under the recipient's display name.
/// </summary>
[Collection("SharedGameSingletons")]
public class MailCount124Tests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-mailcount-{Guid.NewGuid():N}.db");
    private readonly SqlSaveBackend _db;

    public MailCount124Tests() { _db = new SqlSaveBackend(_path); }
    public void Dispose() { SqliteConnection.ClearAllPools(); try { File.Delete(_path); } catch { } }

    private void Exec(string sql, params (string Name, object Value)[] args)
    {
        using var c = new SqliteConnection($"Data Source={_path}"); c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

    private string? Scalar(string sql)
    {
        using var c = new SqliteConnection($"Data Source={_path}"); c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar()?.ToString();
    }

    private void Player(string key, string display, string name2, string team = "") =>
        Exec("INSERT INTO players (username, display_name, player_data) VALUES (@u, @d, @p);",
            ("@u", key), ("@d", display), ("@p", $"{{\"player\":{{\"name2\":\"{name2}\",\"team\":\"{team}\"}}}}"));

    /// <summary>A row exactly as an older build wrote it (no addressing at send time).</summary>
    private int Row(string to, string type = "mail", string from = "Someone")
    {
        Exec("INSERT INTO messages (from_player, to_player, message_type, message) VALUES (@f, @t, @y, 'hello');",
            ("@f", from), ("@t", to), ("@y", type));
        return int.Parse(Scalar("SELECT MAX(id) FROM messages")!);
    }

    private static FieldInfo OnlineUsernameField =>
        typeof(UsurperRemake.BBS.DoorMode).GetField("_onlineUsername", BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>The count the login summary shows: GetUnreadMailCount on DoorMode.OnlineUsername.</summary>
    private int LoginCount(string key)
    {
        var was = OnlineUsernameField.GetValue(null);
        OnlineUsernameField.SetValue(null, key);
        try { return _db.GetUnreadMailCount(UsurperRemake.BBS.DoorMode.OnlineUsername!); }
        finally { OnlineUsernameField.SetValue(null, was); }
    }

    private async Task<int> InboxUnread(string key) => (await _db.GetMailInbox(key, 100)).Count(m => !m.IsRead);

    [Fact]
    public async Task CollidingNames_EachPlayerSeesOnlyTheirOwnMail()
    {
        // account "bob" plays "Alice"; account "b_acct" plays "Bob"
        Player("bob", "Alice", "Alice");
        Player("b_acct", "Bob", "Bob");
        int toBob = Row("Bob");            // Bob's player mail
        Row("bob", "divine", "Ares");       // older row under the key "bob": Bob's display name comes first
        int toAlice = Row("Alice");

        LoginCount("bob").Should().Be(1, "only the row addressed to Alice is hers");
        LoginCount("b_acct").Should().Be(2);
        (await InboxUnread("bob")).Should().Be(1);
        (await _db.GetMailInbox("bob", 100)).Select(m => m.Id).Should().Equal(toAlice);
        (await _db.GetUnreadMessages("bob")).Select(m => m.Id).Should().Equal(toAlice);
        _db.ResolveMailOwner("bob").Should().Be("b_acct");
        _db.ResolveMailOwner("Alice").Should().Be("bob");

        await _db.DeleteMessage(toBob, "bob");
        Scalar($"SELECT COUNT(*) FROM messages WHERE id = {toBob}").Should().Be("1", "Alice cannot delete Bob's mail");
        await _db.MarkMessagesRead("bob");
        LoginCount("b_acct").Should().Be(2, "Alice marking her mail read leaves Bob's unread");
        LoginCount("bob").Should().Be(0);

        await _db.DeleteMessage(toBob, "b_acct");
        Scalar($"SELECT COUNT(*) FROM messages WHERE id = {toBob}").Should().Be("0");
    }

    [Fact]
    public async Task AnAmbiguousOlderRow_IsNobodys()
    {
        // account "cara" plays "Wren"; another player's character name is "Cara", married as "Cara Moss"
        Player("cara", "Wren", "Wren");
        Player("moss_1", "Cara Moss", "Cara");
        int legacy = Row("cara", "world_boss", "System");

        _db.ResolveMailOwner("cara").Should().BeNull();
        LoginCount("cara").Should().Be(0);
        LoginCount("moss_1").Should().Be(0);
        (await _db.GetUnreadMessages("cara")).Should().BeEmpty();
        await _db.MarkMessagesRead("moss_1");
        await _db.DeleteMessage(legacy, "cara");
        Scalar($"SELECT is_read FROM messages WHERE id = {legacy}").Should().Be("0", "the row is left as written");
    }

    [Fact]
    public async Task DifferingNames_LoginCountEqualsTheInboxUnreadRows()
    {
        Player("acct_7", "Rowan Vale", "Rowan");
        Row("Rowan Vale");
        Row("rowan vale");
        Row("acct_7", "team_renamed", "System");
        Row("Unrelated");

        int login = LoginCount("acct_7");
        login.Should().Be(3);
        (await InboxUnread("acct_7")).Should().Be(login, "the login count is what the inbox shows unread");
    }

    [Fact]
    public async Task MatchingNames_AreUnchanged()
    {
        Player("brenna", "Brenna", "Brenna");
        Player("acct_7", "Rowan Vale", "Rowan");
        Row("Brenna");
        Row("brenna", "system", "System");
        Row("Rowan Vale");

        LoginCount("brenna").Should().Be(2);
        (await InboxUnread("brenna")).Should().Be(2);
        LoginCount("acct_7").Should().Be(1);
    }

    [Fact]
    public async Task AnOlderAccountKeyedRow_ReachesItsOwner()
    {
        Player("acct_7", "Rowan Vale", "Rowan");
        int id = Row("acct_7", "sleep_attack", "Grim");

        _db.ResolveMailOwner("acct_7").Should().Be("acct_7");
        LoginCount("acct_7").Should().Be(1);
        (await _db.GetMailInbox("acct_7", 100)).Should().ContainSingle(m => m.Id == id);
        await _db.DeleteMessage(id, "acct_7");
        LoginCount("acct_7").Should().Be(0);
    }

    [Fact]
    public async Task NewMail_IsWrittenUnderTheDisplayName()
    {
        Player("bob", "Alice", "Alice");
        Player("b_acct", "Bob", "Bob");
        Player("acct_7", "Rowan Vale", "Rowan");

        await _db.SendMessageToKey("Ares", "bob", "divine", "x");        // by save key
        await _db.SendMessage("System", "Rowan", "team_departure", "x"); // by character name
        await _db.SendMessage("System", "acct_7", "system", "x");        // a key passed as a name
        await _db.SendMessage("System", "Mira", "mail", "x");            // no player goes by it
        Scalar("SELECT group_concat(to_player, '|') FROM (SELECT to_player FROM messages ORDER BY id)")
            .Should().Be("Alice|Rowan Vale|Rowan Vale|Mira");
        LoginCount("bob").Should().Be(1);
        LoginCount("b_acct").Should().Be(0, "mail to the save key bob is not Bob's");
        LoginCount("acct_7").Should().Be(2);
    }

    [Fact]
    public void TheTeamRenameNotice_IsWrittenUnderTheDisplayName()
    {
        Exec("INSERT INTO player_teams (team_name, password_hash, created_by, created_at) VALUES " +
             "('Wolves', 'x', 'k1', datetime('now', '-3 days')), ('wolves', 'x', 'acct_7', datetime('now', '-1 days'));");
        Player("acct_7", "Rowan Vale", "Rowan", "wolves");

        _db.RenameCaseVariantTeams().Should().Be(1);
        Scalar("SELECT to_player FROM messages WHERE message_type = 'team_renamed'").Should().Be("Rowan Vale");
        LoginCount("acct_7").Should().Be(1);
    }

    [Fact]
    public void TheNeglectLetter_IsCountedAtLogin()
    {
        Player("acct_7", "Rowan Vale", "Rowan");
        var player = new Player { Name2 = "Rowan", FamilySurname = "Vale", ID = "player-rowan" };

        var online = typeof(UsurperRemake.BBS.DoorMode).GetField("_onlineMode", BindingFlags.NonPublic | BindingFlags.Static)!;
        var backendField = typeof(SaveSystem).GetField("backend", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var (wasOnline, wasBackend) = (online.GetValue(null), backendField.GetValue(SaveSystem.Instance));
        online.SetValue(null, true);
        backendField.SetValue(SaveSystem.Instance, _db);
        try { MailSystem.SendSpouseNeglectLetter(player, "Mira"); }
        finally
        {
            online.SetValue(null, wasOnline);
            backendField.SetValue(SaveSystem.Instance, wasBackend);
            player.PendingSpouseLetters?.Clear();
        }

        LoginCount("acct_7").Should().Be(1);
        _db.GetMailInbox("acct_7", 50).GetAwaiter().GetResult()
            .Should().ContainSingle(m => m.FromPlayer == "Mira" && m.ToPlayer == "Rowan Vale");
    }

    [Fact]
    public void KeyWriters_AddressBySaveKey_AndTheMailboxReadsByIt()
    {
        string Src(params string[] p) => File.ReadAllText(Path.Combine(new[] { FindRepoRoot(), "Scripts" }.Concat(p).ToArray()));
        Src("Locations", "PantheonLocation.cs").Should().NotContain("SendMessage(godName, target.Username");
        Src("Locations", "TempleLocation.cs").Should().Contain("SendMessageToKeyLocalized(\"Temple\", godInfo.Username");   // 1.2.5: localized
        Src("Locations", "InnLocation.cs").Should().Contain("SendMessageToKeyLocalized(murderer, target.Username");   // 1.2.5: localized
        Src("Locations", "DormitoryLocation.cs").Should().Contain("SendMessageToKeyLocalized(murderer, target.Username");   // 1.2.5: localized
        Src("Locations", "ArenaLocation.cs").Should().Contain("SendMessageToKey(myUsername, defenderUsername");
        Src("Systems", "WorldBossSystem.cs").Should().NotContain("SendMessageToKey(", "1.2.5: the boss notice is no longer mailed");
        Src("Systems", "WorldSimulator.cs").Should().Contain("SendMessageToKeyLocalized(\"The Town Crier\", username").And.Contain("SendMessageToKeyLocalized(attackerNPC.Name2, sleeper.Username");   // 1.2.5: localized
        Src("Server", "MudServer.cs").Should().Contain("SendMessageToKey(\"Admin\", target");

        string mailbox = Src("Locations", "BaseLocation.cs");
        mailbox.Should().Contain("string mailKey = UsurperRemake.BBS.DoorMode.OnlineUsername");
        mailbox.Should().Contain("backend.GetUnreadMailCount(mailKey)").And.Contain("backend.GetMailInbox(mailKey,");
        mailbox.Should().Contain("backend.DeleteMessage(inbox[delIdx - 1].Id, mailKey)");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj"))) dir = dir.Parent;
        return dir!.FullName;
    }
}
