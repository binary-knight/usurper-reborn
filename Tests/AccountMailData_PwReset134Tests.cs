using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake;
using UsurperRemake.Systems;
using UsurperReborn.Tests.Localization;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.4 password self-reset, the game's data side: the migration, the mail switch (O3), the address rule (S1),
/// the one password minimum, the request rows (reset, verify, remove_notice) with the per-IP cap (S4), the
/// security log without addresses (S3), and an address kept only in the players row and request rows (G2).
/// Test addresses are on reserved domains only (G3).
/// </summary>
[Collection("SharedGameSingletons")]
public class AccountMailData_PwReset134Tests : IDisposable
{
    private static readonly string[] AllLanguages = { "en", "es", "fr", "hu", "it" };
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-pw134-{Guid.NewGuid():N}.db");
    private SqlSaveBackend? _db;
    private SqlSaveBackend Db => _db ??= new SqlSaveBackend(_path);
    private readonly Func<DateTime> _clock = MailSwitch.UtcNow;

    public void Dispose()
    {
        MailSwitch.UtcNow = _clock;
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    // ---------- helpers ----------

    internal static void Exec(string path, string sql, params (string Name, object? Value)[] args)
    {
        using var c = new SqliteConnection($"Data Source={path}"); c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    internal static object? Scalar(string path, string sql, params (string Name, object? Value)[] args)
    {
        using var c = new SqliteConnection($"Data Source={path}"); c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        var r = cmd.ExecuteScalar();
        return r is DBNull ? null : r;
    }

    internal static List<Dictionary<string, object?>> Rows(string path, string sql)
    {
        var list = new List<Dictionary<string, object?>>();
        using var c = new SqliteConnection($"Data Source={path}"); c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < r.FieldCount; i++) row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
            list.Add(row);
        }
        return list;
    }

    /// <summary>The mail switch row as the web sender writes it, <paramref name="minutesOld"/> before the test clock.</summary>
    internal static void SetMailSwitch(string path, string value, double minutesOld)
    {
        var now = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
        MailSwitch.UtcNow = () => now;
        Exec(path, "INSERT OR REPLACE INTO world_state (key, value, version, updated_at) VALUES ('mail_enabled', @v, 1, @t);",
            ("@v", value), ("@t", now.AddMinutes(-minutesOld).ToString("yyyy-MM-dd HH:mm:ss")));
    }

    internal static string HashOf(string password) =>
        (string)typeof(SqlSaveBackend).GetMethod("HashPassword", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { password })!;

    private static string L(string lang, string key, params object[] args) => Loc.GetIn(lang, key, args);

    private static string Src(params string[] path) =>
        File.ReadAllText(Path.Combine(new[] { HardcodedTextScannerTests.RepoRoot(), "Scripts" }.Concat(path).ToArray()));

    private async Task Register(string name, string password = "long-enough-1") =>
        (await Db.RegisterPlayer(name, password)).success.Should().BeTrue();

    private long Count(string sql) => Convert.ToInt64(Scalar(_path, sql));

    // ================= 1.1 migration =================

    private const string OldPlayersTable = @"
        CREATE TABLE players (
            username TEXT PRIMARY KEY, display_name TEXT NOT NULL, password_hash TEXT NOT NULL DEFAULT '',
            player_data TEXT NOT NULL, created_at TEXT DEFAULT (datetime('now')), last_login TEXT, last_logout TEXT,
            total_playtime_minutes INTEGER DEFAULT 0, is_banned INTEGER DEFAULT 0, ban_reason TEXT,
            last_login_ip TEXT, created_ip TEXT);
        INSERT INTO players (username, display_name, password_hash, player_data) VALUES ('old hand', 'Old Hand', 'x', '{}');";

    [Fact]
    public void Migration_IsIdempotent_OnAnOldDatabase()
    {
        Exec(_path, OldPlayersTable);
        for (int run = 0; run < 3; run++)
        {
            SqliteConnection.ClearAllPools();
            _ = new SqlSaveBackend(_path);
        }

        var columns = Rows(_path, "PRAGMA table_info(players);").Select(r => (string)r["name"]!).ToList();
        foreach (var col in new[] { "email", "email_verified_at", "email_prompted_at" })
            columns.Count(c => c == col).Should().Be(1, col);
        foreach (var table in new[] { "mail_requests", "account_tokens", "account_security_log" })
            Count($"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{table}';").Should().Be(1, table);
        Count("SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'idx_players_email';").Should().Be(1);

        // the old row is kept and starts with no address and not asked
        var old = Rows(_path, "SELECT display_name, email, email_verified_at, email_prompted_at FROM players WHERE username = 'old hand';").Single();
        old["display_name"].Should().Be("Old Hand");
        old["email"].Should().BeNull();
        old["email_verified_at"].Should().BeNull();
        old["email_prompted_at"].Should().BeNull();

        // the tables have the columns the web side reads and writes
        Rows(_path, "PRAGMA table_info(mail_requests);").Select(r => (string)r["name"]!).Should()
            .Equal("id", "kind", "username", "address", "ip", "lang", "created_at", "status", "processed_at");
        Rows(_path, "PRAGMA table_info(account_tokens);").Select(r => (string)r["name"]!).Should()
            .Equal("id", "username", "kind", "token_hash", "new_address", "created_at", "expires_at", "used_at", "ip");
        Rows(_path, "PRAGMA table_info(account_security_log);").Select(r => (string)r["name"]!).Should()
            .Equal("id", "username", "event", "ip", "created_at");
    }

    [Fact]
    public async Task EmailIndex_IsUnique_ButAllowsManyAccountsWithout()
    {
        await Register("First One");
        await Register("Second One");
        await Register("Third One");
        Exec(_path, "UPDATE players SET email = 'player.one@example.com' WHERE username = 'first one';");
        Action dup = () => Exec(_path, "UPDATE players SET email = 'player.one@example.com' WHERE username = 'second one';");
        dup.Should().Throw<SqliteException>().WithMessage("*UNIQUE*");
        Count("SELECT COUNT(*) FROM players WHERE email IS NULL;").Should().Be(2, "NULL is not a duplicate");
    }

    // ================= O3 the mail switch =================

    [Fact]
    public void MailSwitch_IsOn_OnlyForOne_WithAFreshBeat()
    {
        _ = Db;
        MailSwitch.IsOn(Db).Should().BeFalse("no row: off");
        SetMailSwitch(_path, "1", 0.5);
        MailSwitch.IsOn(Db).Should().BeTrue("'1' and a fresh beat");
        SetMailSwitch(_path, "1", 9.9);
        MailSwitch.IsOn(Db).Should().BeTrue("under ten minutes");
        SetMailSwitch(_path, "0", 0.5);
        MailSwitch.IsOn(Db).Should().BeFalse("'0': the sender has no key");
        SetMailSwitch(_path, "yes", 0.5);
        MailSwitch.IsOn(Db).Should().BeFalse("only '1' is on");
        SetMailSwitch(_path, "1", -5);
        MailSwitch.IsOn(Db).Should().BeFalse("a beat from the future past the skew is not trusted");
        Exec(_path, "UPDATE world_state SET updated_at = 'soon' WHERE key = 'mail_enabled';");
        MailSwitch.IsOn(Db).Should().BeFalse("an unreadable time: off");
        MailSwitch.IsOn((SqlSaveBackend?)null).Should().BeFalse();
    }

    [Fact]
    public void MailSwitch_OldHeartbeat_IsOff()
    {
        _ = Db;
        SetMailSwitch(_path, "1", 11);
        MailSwitch.IsOn(Db).Should().BeFalse("'1' with a beat 11 minutes old: the sender stopped");
        SetMailSwitch(_path, "1", 10);
        MailSwitch.IsOn(Db).Should().BeFalse("ten minutes is not under ten");
    }

    // ================= S1 the address rule =================

    [Fact]
    public void Address_TrimmedLowerCase_OneAt_NoControl_AtMost254()
    {
        AccountEmail.Normalize("  Player.One@Example.COM \t").Should().Be("player.one@example.com");
        AccountEmail.Normalize("a@b.invalid").Should().Be("a@b.invalid");
        string local = new string('a', 64);
        string fits = local + "@" + new string('d', 254 - 64 - 1 - 8) + ".example";
        fits.Length.Should().Be(254);
        AccountEmail.Normalize(fits).Should().Be(fits);
        AccountEmail.Normalize("x" + fits).Should().BeNull("255 characters");

        foreach (var bad in new[] { "", "   ", "no-at.example.com", "two@@example.com", "a@b@example.com", "@example.com",
                     "player@", "pla\nyer@example.com", "player@example.com\r\nBcc: x@example.net", "pla\ryer@example.com",
                     "pla\u0001yer@example.com", "pla\u007fyer@example.com", "pla yer@example.com", null })
            AccountEmail.Normalize(bad).Should().BeNull($"'{bad}' is not an address");
    }

    [Fact]
    public void Address_IsShownMasked_AndShort()
    {
        AccountEmail.Mask("alice@domain.example.net").Should().Be("a***@d***.net");
        AccountEmail.Mask("bob@example.invalid").Should().Be("b***@e***.invalid");
        AccountEmail.Mask("c@localhost").Should().Be("c***@l***");
        AccountEmail.Mask(null).Should().Be("");
        var longTld = "z@d." + new string('t', 200);
        AccountEmail.Mask(longTld).Length.Should().BeLessOrEqualTo(20);
    }

    // ================= the password minimum =================

    [Fact]
    public async Task MinPasswordLength_IsEight_ForEveryNewPassword()
    {
        GameConfig.MinPasswordLength.Should().Be(8);
        (await Db.RegisterPlayer("Short Pw", "seven77", null, "en")).message.Should().Be(L("en", "auth.err_password_len"));
        Db.PlayerExists("Short Pw").Should().BeFalse();
        (await Db.RegisterPlayer("Long Pw", "eight888", null, "en")).success.Should().BeTrue();

        (await Db.ChangePassword("Long Pw", "eight888", "seven77", "en")).message.Should().Be(L("en", "auth.err_new_password_len"));
        (await Db.AuthenticatePlayer("Long Pw", "eight888")).success.Should().BeTrue("the refused change kept the old password");
        Db.AdminResetPassword("Long Pw", "seven77").success.Should().BeFalse();
        Db.AdminResetPassword("Long Pw", "eight:88").success.Should().BeFalse("':' stays refused");
        (await Db.RegisterPlayer("Colon Pw", "eight:888", null, "en")).message.Should().Be(L("en", "auth.err_password_colon"));
        Db.AdminResetPassword("Long Pw", "nine99999").success.Should().BeTrue();

        // every text that states the minimum says 8, in every language
        foreach (var lang in AllLanguages)
            foreach (var key in new[] { "auth.err_password_len", "auth.err_new_password_len", "online.password_length", "engine.password_min_length", "engine.new_password_prompt" })
            {
                var text = L(lang, key);
                Regex.IsMatch(text, @"\b8\b").Should().BeTrue($"[{lang}] {key}: {text}");
                Regex.IsMatch(text, @"\b4\b").Should().BeFalse($"[{lang}] {key}: {text}");
            }
    }

    [Fact]
    public async Task ExistingShortPassword_StillLogsIn()
    {
        _ = Db;
        Exec(_path, "INSERT INTO players (username, display_name, password_hash, player_data) VALUES ('veteran', 'Veteran', @h, '{}');",
            ("@h", HashOf("abcd")));
        (await Db.AuthenticatePlayer("Veteran", "abcd")).success.Should().BeTrue("a login never checks the length");
    }

    [Fact]
    public void EverySite_UsesTheOneConstant()
    {
        var sites = new[]
        {
            ("Server", "MudServer.cs", 2), ("Server", "RelayClient.cs", 1), ("Systems", "OnlineAuthFlow.cs", 1),
            ("Systems", "SqlSaveBackend.cs", 3), ("Core", "GameEngine.cs", 1), ("Systems", "OnlineAdminConsole.cs", 1),
        };
        foreach (var (dir, file, uses) in sites)
        {
            var src = Src(dir, file);
            Regex.Matches(src, @"\.Length < GameConfig\.MinPasswordLength").Count.Should().Be(uses, file);
            Regex.IsMatch(src, @"[Pp]assword\.Length < 4").Should().BeFalse(file);
        }
    }

    // ================= 1.3 reset requests and S4 =================

    [Fact]
    public async Task ResetRequest_KnownAndUnknownName_WriteTheSameRow()
    {
        await Register("Real Name");
        Db.RequestPasswordReset("Real Name", "198.51.100.7", "hu").Should().BeTrue();
        Db.RequestPasswordReset("Ghost Name", "198.51.100.7", "hu").Should().BeTrue();
        var rows = Rows(_path, "SELECT kind, username, address, ip, lang, status, processed_at FROM mail_requests ORDER BY id;");
        rows.Should().HaveCount(2);
        rows[0]["username"].Should().Be("real name");
        rows[1]["username"].Should().Be("ghost name");
        foreach (var row in rows)
        {
            row.Remove("username");
            row.Should().BeEquivalentTo(new Dictionary<string, object?>
            {
                ["kind"] = "reset", ["address"] = null, ["ip"] = "198.51.100.7", ["lang"] = "hu", ["status"] = "pending", ["processed_at"] = null,
            });
        }
        Db.RequestPasswordReset("   ", "198.51.100.7", "en").Should().BeFalse("no name, no row");
        Count("SELECT COUNT(*) FROM account_security_log;").Should().Be(0, "a reset request names no account");
    }

    [Fact]
    public void RequestCap_FivePerIpPerHour_ThenNoRow()
    {
        _ = Db;
        for (int i = 0; i < 5; i++)
            Db.RequestPasswordReset($"name {i}", "203.0.113.5", "en").Should().BeTrue($"row {i + 1}");
        Db.RequestPasswordReset("name 6", "203.0.113.5", "en").Should().BeFalse("the sixth in an hour");
        Db.RequestEmailVerify("name 6", "six@example.com", "203.0.113.5", "en").Should().BeFalse("the cap counts every request kind");
        Count("SELECT COUNT(*) FROM mail_requests WHERE ip = '203.0.113.5';").Should().Be(5);

        Db.RequestPasswordReset("name 6", "203.0.113.6", "en").Should().BeTrue("another address has its own count");
        Exec(_path, "UPDATE mail_requests SET created_at = datetime('now', '-61 minutes') WHERE ip = '203.0.113.5';");
        Db.RequestPasswordReset("name 7", "203.0.113.5", "en").Should().BeTrue("rows older than an hour do not count");
    }

    // ================= 1.4 to 1.6 address requests, S3, S5 =================

    [Fact]
    public async Task VerifyRequest_HoldsTheAddressInTheRow_NotInPlayers()
    {
        await Register("Newcomer");
        Db.RequestEmailVerify("Newcomer", "  New.Comer@Example.com ", "198.51.100.8", "fr").Should().BeTrue();
        var row = Rows(_path, "SELECT kind, username, address, ip, lang FROM mail_requests;").Single();
        row["kind"].Should().Be("verify");
        row["username"].Should().Be("newcomer");
        row["address"].Should().Be("new.comer@example.com");
        row["lang"].Should().Be("fr");
        Db.GetAccountEmail("Newcomer").Email.Should().BeNull("players.email changes only when the link is opened");
        Db.RequestEmailVerify("Newcomer", "not an address", "198.51.100.8", "fr").Should().BeFalse();

        Db.GetAccountEmail("Newcomer").Prompted.Should().BeFalse();
        Db.MarkEmailPrompted("Newcomer");
        var first = Scalar(_path, "SELECT email_prompted_at FROM players WHERE username = 'newcomer';");
        first.Should().NotBeNull();
        Db.MarkEmailPrompted("Newcomer");
        Scalar(_path, "SELECT email_prompted_at FROM players WHERE username = 'newcomer';").Should().Be(first, "set once");
    }

    [Fact]
    public async Task ChangeOrRemove_WithoutTheCurrentPassword_IsRefused()
    {
        await Register("Owner", "owner-pass-1");
        Exec(_path, "UPDATE players SET email = 'owner@example.net', email_verified_at = datetime('now') WHERE username = 'owner';");

        (await Db.RequestEmailChange("Owner", "", "new@example.net", "198.51.100.9", "en")).Should().Be(SqlSaveBackend.EmailChangeResult.BadPassword);
        (await Db.RequestEmailChange("Owner", "wrong-pass-1", "new@example.net", "198.51.100.9", "en")).Should().Be(SqlSaveBackend.EmailChangeResult.BadPassword);
        (await Db.RemoveEmail("Owner", null, "198.51.100.9", "en")).Should().Be(SqlSaveBackend.EmailChangeResult.BadPassword);
        (await Db.RemoveEmail("Owner", "wrong-pass-1", "198.51.100.9", "en")).Should().Be(SqlSaveBackend.EmailChangeResult.BadPassword);
        Count("SELECT COUNT(*) FROM mail_requests;").Should().Be(0, "nothing written without the password");
        Db.GetAccountEmail("Owner").Email.Should().Be("owner@example.net");
        Count("SELECT COUNT(*) FROM account_security_log WHERE event IN ('email_change_refused', 'email_remove_refused');").Should().Be(4);

        (await Db.RequestEmailChange("Owner", "owner-pass-1", "bad address", "198.51.100.9", "en")).Should().Be(SqlSaveBackend.EmailChangeResult.BadAddress);
        (await Db.RequestEmailChange("Owner", "owner-pass-1", "New@Example.net", "198.51.100.9", "en")).Should().Be(SqlSaveBackend.EmailChangeResult.Requested);
        Rows(_path, "SELECT kind, address FROM mail_requests;").Single()["address"].Should().Be("new@example.net");
        Db.GetAccountEmail("Owner").Email.Should().Be("owner@example.net", "the old address stays until the new one is confirmed");
    }

    [Fact]
    public async Task Remove_ClearsTheAddress_AndWritesANoticeToIt()
    {
        await Register("Leaver", "leaver-pass-1");
        Exec(_path, "UPDATE players SET email = 'leaver@example.com', email_verified_at = datetime('now') WHERE username = 'leaver';");
        (await Db.RemoveEmail("Leaver", "leaver-pass-1", "198.51.100.10", "it")).Should().Be(SqlSaveBackend.EmailChangeResult.Removed);

        var player = Rows(_path, "SELECT email, email_verified_at FROM players WHERE username = 'leaver';").Single();
        player["email"].Should().BeNull();
        player["email_verified_at"].Should().BeNull();
        var notice = Rows(_path, "SELECT kind, username, address, ip, lang FROM mail_requests;").Single();
        notice["kind"].Should().Be("remove_notice");
        notice["address"].Should().Be("leaver@example.com");
        notice["username"].Should().Be("leaver");
        notice["lang"].Should().Be("it");

        (await Db.RemoveEmail("Leaver", "leaver-pass-1", "198.51.100.10", "it")).Should().Be(SqlSaveBackend.EmailChangeResult.NoAddress);
    }

    [Fact]
    public async Task SecurityLog_KeepsNoAddress()
    {
        await Register("Logged One", "logged-pass-1");
        Db.RequestEmailVerify("Logged One", "logged@example.com", "198.51.100.11", "en");
        await Db.RequestEmailChange("Logged One", "wrong-pass-1", "other@example.com", "198.51.100.11", "en");
        Exec(_path, "UPDATE players SET email = 'logged@example.com' WHERE username = 'logged one';");
        await Db.RemoveEmail("Logged One", "logged-pass-1", "198.51.100.11", "en");
        var rows = Rows(_path, "SELECT * FROM account_security_log;");
        rows.Select(r => r["event"]).Should().Equal("email_verify_requested", "email_change_refused", "email_removed");
        foreach (var row in rows)
            foreach (var value in row.Values)
                (value?.ToString() ?? "").Should().NotContain("@");
    }

    // ================= G2 the address stays in the players row =================

    [Fact]
    public async Task Address_NeverReachesPlayerData_TheArchive_OrALogLine()
    {
        const string address = "keeper@example.com";
        using var log = new LoginToken128Tests.LogCapture();
        var oldErr = Console.Error;
        var err = new StringWriter();
        Console.SetError(err);
        try
        {
            await Register("Keeper", "keeper-pass-1");
            Db.RequestEmailVerify("Keeper", address, "198.51.100.12", "en");
            Exec(_path, "UPDATE players SET email = @a, email_verified_at = datetime('now') WHERE username = 'keeper';", ("@a", address));
            (await Db.WriteGameData("keeper", new SaveGameData
            {
                Version = GameConfig.SaveVersion,
                Player = new PlayerData { Name1 = "keeper", Name2 = "Keeper", Level = 3 }
            })).Should().BeTrue();
            await Db.RequestEmailChange("Keeper", "keeper-pass-1", "keeper2@example.com", "198.51.100.12", "en");
            Db.DeleteGameData("keeper").Should().BeTrue();
            await Db.RemoveEmail("Keeper", "keeper-pass-1", "198.51.100.12", "en");
        }
        finally { Console.SetError(oldErr); }

        Count("SELECT COUNT(*) FROM deleted_characters;").Should().Be(1, "the delete archived the character");
        foreach (var sql in new[] { "SELECT player_data FROM players", "SELECT player_data FROM deleted_characters",
                     "SELECT * FROM deleted_characters", "SELECT * FROM pending_purges" })
            foreach (var row in Rows(_path, sql))
                foreach (var value in row.Values)
                    (value?.ToString() ?? "").Should().NotContain("@", sql);
        log.All.Should().NotContain("@").And.NotContain("keeper2");
        err.ToString().Should().NotContain("@");
    }

    [Fact]
    public void Address_IsReadOnlyByTheAccountMailCode()
    {
        // the exports, the admin lists and the telemetry never read the email columns
        foreach (var (dir, file) in new[] { ("Systems", "WikiDataExporter.cs"), ("Systems", "SqlSaveBackend.Telemetry.cs"),
                     ("Systems", "OnlineAdminConsole.cs"), ("Systems", "SysOpConsoleManager.cs"), ("Systems", "SqlSaveBackend.cs") })
            Regex.IsMatch(Src(dir, file), @"\bemail\b", RegexOptions.IgnoreCase).Should().BeFalse(file);
        Regex.IsMatch(Src("Systems", "SqlSaveBackend.cs"), @"SELECT\s+\*\s+FROM\s+players\b", RegexOptions.IgnoreCase).Should().BeFalse();
    }

    // ================= G3 reserved test addresses only =================

    [Fact]
    public void TestSources_UseReservedDomainsOnly()
    {
        var root = Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Tests");
        var address = new Regex(@"[A-Za-z0-9._%+-]+@([A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+)");
        var found = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            foreach (Match m in address.Matches(File.ReadAllText(file)))
            {
                string domain = m.Groups[1].Value.ToLowerInvariant();
                bool reserved = domain == "example.com" || domain.EndsWith(".example.com") || domain == "example.net"
                    || domain.EndsWith(".example.net") || domain.EndsWith(".invalid") || domain == "example";
                if (!reserved) found.Add($"{Path.GetFileName(file)}: {m.Value}");
            }
        }
        found.Should().BeEmpty("G3: test addresses only on example.com, example.net or .invalid");
    }
}
