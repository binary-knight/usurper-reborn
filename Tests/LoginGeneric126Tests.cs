using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using UsurperReborn.Tests.Localization;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.6: a login never shows whether an account exists. An unknown name, an account with no usable
/// password hash and a wrong password give one message after the same PBKDF2 work; a banned account
/// shows its ban only after the right password; the IP ban still refuses first. Screens with an [R]
/// register key add the register hint on its own row; the AUTH protocol line carries the sentence
/// only. Operator logs keep the name and the real reason, never the password. Registration attempts
/// count in the per-IP login throttle, which stays keyed by IP.
/// </summary>
[Collection("SharedGameSingletons")]
public class LoginGeneric126Tests : IDisposable
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private const int MaxWidth = 79;
    private static readonly string[] AllLanguages = { "en", "es", "fr", "hu", "it" };

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-login-126-{Guid.NewGuid():N}.db");
    private SqlSaveBackend? _db;
    private SqlSaveBackend Db => _db ??= new SqlSaveBackend(_path);
    private readonly List<string> _throttleIps = new();

    public void Dispose()
    {
        foreach (var ip in _throttleIps) FailedLogins().TryRemove(ip, out _);
        if (_db == null) return;
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    // ---------- helpers ----------

    private static string L(string lang, string key, params object[] args) => Loc.GetIn(lang, key, args);

    private void Exec(string sql, params (string Name, object Value)[] args)
    {
        _ = Db;
        using var c = new SqliteConnection($"Data Source={_path}"); c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

    private string StoredHash(string username)
    {
        _ = Db;
        using var c = new SqliteConnection($"Data Source={_path}"); c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT password_hash FROM players WHERE username = @u;";
        cmd.Parameters.AddWithValue("@u", username.ToLowerInvariant());
        return cmd.ExecuteScalar()?.ToString() ?? "";
    }

    private void NoHashAccount(string key, string hash) =>
        Exec("INSERT INTO players (username, display_name, password_hash, player_data) VALUES (@u, @u, @h, '{}');",
            ("@u", key), ("@h", hash));

    private async Task Register(string name, string password) =>
        (await Db.RegisterPlayer(name, password)).success.Should().BeTrue();

    private void Ban(string name, string reason) =>
        Exec("UPDATE players SET is_banned = 1, ban_reason = @r WHERE username = @u;",
            ("@r", reason), ("@u", name.ToLowerInvariant()));

    private static string Src(params string[] path) =>
        File.ReadAllText(Path.Combine(new[] { HardcodedTextScannerTests.RepoRoot(), "Scripts" }.Concat(path).ToArray()));

    private static string Visible(string row) => Regex.Replace(row, "\u001b\\[[0-9;]*[A-Za-z]", "");

    private static ConcurrentDictionary<string, (int Fails, DateTime LastFail, DateTime LockUntil)> FailedLogins() =>
        (ConcurrentDictionary<string, (int, DateTime, DateTime)>)typeof(MudServer).GetField("_failedLogins", SNP)!.GetValue(null)!;

    private static bool IsLoginThrottled(string ip)
    {
        var args = new object?[] { ip, 0 };
        return (bool)typeof(MudServer).GetMethod("IsLoginThrottled", SNP)!.Invoke(null, args)!;
    }

    private string ThrottleIp(int n)
    {
        var ip = $"198.51.100.{n}";
        FailedLogins().TryRemove(ip, out _);
        _throttleIps.Add(ip);
        return ip;
    }

    /// <summary>Copies what is written to stderr while a test runs, still passing it through.</summary>
    private sealed class StderrTap : TextWriter
    {
        private readonly TextWriter _inner;
        private readonly StringBuilder _seen = new();
        public StderrTap(TextWriter inner) => _inner = inner;
        public override Encoding Encoding => _inner.Encoding;
        public override void Write(char value) { lock (_seen) _seen.Append(value); _inner.Write(value); }
        public override void Write(string? value) { lock (_seen) _seen.Append(value); _inner.Write(value); }
        public override void Write(char[] buffer, int index, int count) { lock (_seen) _seen.Append(buffer, index, count); _inner.Write(buffer, index, count); }
        public string Seen { get { lock (_seen) return _seen.ToString(); } }
    }

    private static MudServer NewServer()
    {
        var server = (MudServer)RuntimeHelpers.GetUninitializedObject(typeof(MudServer));
        typeof(MudServer).GetField("<ActiveSessions>k__BackingField", NP)!
            .SetValue(server, new ConcurrentDictionary<string, PlayerSession>());
        return server;
    }

    /// <summary>
    /// One real loopback connection into MudServer.HandleConnectionAsync. <paramref name="client"/>
    /// drives the client end; returns what the client read and what the server wrote to stderr.
    /// </summary>
    private async Task<(string Output, string Stderr)> Connect(Func<NetworkStream, StringBuilder, Task> client)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var oldErr = Console.Error;
        var tap = new StderrTap(oldErr);
        Console.SetError(tap);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var output = new StringBuilder();
        try
        {
            using var tcp = new TcpClient();
            var acceptTask = listener.AcceptTcpClientAsync();
            await tcp.ConnectAsync(IPAddress.Loopback, port);
            var accepted = await acceptTask;
            var handle = typeof(MudServer).GetMethod("HandleConnectionAsync", NP)!;
            var serverTask = (Task)handle.Invoke(NewServer(), new object[] { accepted, Db, cts.Token })!;
            var stream = tcp.GetStream();
            await client(stream, output);
            await serverTask.WaitAsync(TimeSpan.FromSeconds(20));
        }
        finally
        {
            Console.SetError(oldErr);
            listener.Stop();
        }
        return (output.ToString(), tap.Seen);
    }

    private static async Task Send(NetworkStream s, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await s.WriteAsync(bytes, 0, bytes.Length);
        await s.FlushAsync();
    }

    /// <summary>Reads until <paramref name="marker"/> shows up (or the server closes the connection).</summary>
    private static async Task ReadUntil(NetworkStream s, StringBuilder output, string? marker, bool once = false)
    {
        var buffer = new byte[4096];
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (marker != null && output.ToString().Contains(marker)) return;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            int read;
            try { read = await s.ReadAsync(buffer, 0, buffer.Length, cts.Token); }
            catch (Exception) { return; }
            if (read == 0) return;
            output.Append(Encoding.UTF8.GetString(buffer, 0, read));
            if (once) return;
        }
    }

    private static async Task ReadUntilCount(NetworkStream s, StringBuilder output, string marker, int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (Regex.Matches(output.ToString(), Regex.Escape(marker)).Count < count && DateTime.UtcNow < deadline)
        {
            int before = output.Length;
            await ReadUntil(s, output, null, once: true);
            if (output.Length == before) return; // closed
        }
    }

    /// <summary>The AUTH protocol line for one login (or registration) from <paramref name="ip"/>.</summary>
    private Task<(string Output, string Stderr)> AuthLine(string ip, string authLine) =>
        Connect(async (s, output) =>
        {
            await Send(s, $"X-IP:{ip}\nAUTH:{authLine}\n");
            await ReadUntil(s, output, null);
        });

    private static string ErrText(string output)
    {
        var line = output.Split('\n').Select(l => l.TrimEnd('\r')).First(l => l.StartsWith("ERR:"));
        return line.Substring(4);
    }

    // ================= one message for every "no such login" =================

    [Fact]
    public async Task UnknownName_NoHash_AndWrongPassword_GiveOneMessage_InEveryLanguage()
    {
        await Register("Real Name", "secret");
        NoHashAccount("nohash", "");
        NoHashAccount("badhash", "x");
        await Register("Banned One", "rightpw");
        Ban("Banned One", "griefing");

        foreach (var lang in AllLanguages)
        {
            var expected = L(lang, "auth.err_bad_login");
            expected.Should().NotBe("auth.err_bad_login", $"[{lang}] the key exists");
            var cases = new (string Name, string Password, SqlSaveBackend.LoginFailure Reason)[]
            {
                ("nobody here", "secret", SqlSaveBackend.LoginFailure.UnknownName),
                ("Real Name", "wrong", SqlSaveBackend.LoginFailure.WrongPassword),
                ("nohash", "secret", SqlSaveBackend.LoginFailure.NoUsableHash),
                ("badhash", "x", SqlSaveBackend.LoginFailure.NoUsableHash),
                ("Banned One", "wrong", SqlSaveBackend.LoginFailure.BannedWrongPassword),
            };
            foreach (var (name, password, reason) in cases)
            {
                var r = await Db.AuthenticatePlayer(name, password, null, lang);
                r.success.Should().BeFalse();
                r.message.Should().Be(expected, $"[{lang}] {name}: the same text whether or not the account exists");
                r.reason.Should().Be(reason);
                r.displayName.Should().BeEmpty();
            }
        }
        (await Db.AuthenticatePlayer("Real Name", "secret", null, "en")).success.Should().BeTrue();
    }

    // ================= the same PBKDF2 work on every path =================

    [Fact]
    public async Task EveryFailedPath_RunsOneVerify_AtTheRealIterationCount()
    {
        SqlSaveBackend.Pbkdf2Iterations.Should().Be(100000, "the work factor stored hashes were made with");
        SqlSaveBackend.IsUsableHash(SqlSaveBackend.DummyPasswordHash).Should().BeTrue("so the dummy verify runs PBKDF2 instead of returning early");
        await Register("Real Name", "secret");
        var real = StoredHash("Real Name").Split(':');
        var dummy = SqlSaveBackend.DummyPasswordHash.Split(':');
        Convert.FromBase64String(dummy[0]).Length.Should().Be(Convert.FromBase64String(real[0]).Length, "the same salt size as a stored hash");
        Convert.FromBase64String(dummy[1]).Length.Should().Be(Convert.FromBase64String(real[1]).Length, "the same hash size as a stored hash");

        NoHashAccount("nohash", "");
        await Register("Banned One", "rightpw");
        Ban("Banned One", "griefing");

        async Task Expect(string name, string password, int verifies, int dummies)
        {
            int v0 = Db.LoginVerifies, d0 = Db.LoginDummyVerifies;
            await Db.AuthenticatePlayer(name, password, null, "en");
            (Db.LoginVerifies - v0).Should().Be(verifies, $"{name}: one PBKDF2 verify");
            (Db.LoginDummyVerifies - d0).Should().Be(dummies, $"{name}: against the dummy hash when there is no usable one");
        }
        await Expect("nobody here", "secret", 1, 1);
        await Expect("nohash", "secret", 1, 1);
        await Expect("Banned One", "wrong", 1, 0);
        await Expect("Real Name", "wrong", 1, 0);

        // the dummy hash never lets anyone in, whatever is typed
        (await Db.AuthenticatePlayer("nobody here", "no password matches this login dummy", null, "en")).success.Should().BeFalse();

        // and it costs about the same: medians over several runs (the counter above is the real check)
        async Task<double> Median(string name)
        {
            var times = new List<double>();
            for (int i = 0; i < 7; i++)
            {
                var sw = Stopwatch.StartNew();
                await Db.AuthenticatePlayer(name, "wrong", null, "en");
                times.Add(sw.Elapsed.TotalMilliseconds);
            }
            times.Sort();
            return times[3];
        }
        double wrong = await Median("Real Name");
        double unknown = await Median("nobody here");
        unknown.Should().BeGreaterThan(wrong * 0.25, $"an unknown name ({unknown:F1} ms) does the same work as a wrong password ({wrong:F1} ms)");
    }

    // ================= banned: the ban only after the right password =================

    [Fact]
    public async Task BannedAccount_ShowsTheBan_OnlyAfterTheRightPassword_AndStillCannotLogIn()
    {
        await Register("Banned One", "rightpw");
        Ban("Banned One", "griefing the newbies");

        var wrong = await Db.AuthenticatePlayer("Banned One", "wrong", null, "hu");
        wrong.success.Should().BeFalse();
        wrong.message.Should().Be(L("hu", "auth.err_bad_login"));
        wrong.message.Should().NotContain("griefing");

        foreach (var lang in AllLanguages)
        {
            var right = await Db.AuthenticatePlayer("Banned One", "rightpw", null, lang);
            right.success.Should().BeFalse("a ban still refuses the login");
            right.displayName.Should().BeEmpty();
            right.reason.Should().Be(SqlSaveBackend.LoginFailure.Banned);
            right.message.Should().Be(L(lang, "auth.err_account_banned") + " " + L(lang, "auth.err_ban_reason", "griefing the newbies"));
        }
    }

    // ================= the IP ban stays first and unchanged =================

    [Fact]
    public async Task IpBan_StillRefusesFirst_BeforeAnyAccountLookup()
    {
        await Register("Real Name", "secret");
        Db.BanIp("203.0.113.9", "spam", "admin", null);

        foreach (var (name, password) in new[] { ("Real Name", "secret"), ("Real Name", "wrong"), ("nobody here", "x") })
        {
            int v0 = Db.LoginVerifies;
            var r = await Db.AuthenticatePlayer(name, password, "203.0.113.9", "hu");
            r.success.Should().BeFalse();
            r.message.Should().Be(L("hu", "auth.err_login_ip_banned"));
            r.reason.Should().Be(SqlSaveBackend.LoginFailure.IpBanned);
            Db.LoginVerifies.Should().Be(v0, "the IP check comes before the account and the password");
        }

        // the accept-time refusal is unchanged and still runs before either login path
        string mud = Src("Server", "MudServer.cs");
        int refusal = mud.IndexOf("Connection refused: this address is banned from the server.", StringComparison.Ordinal);
        refusal.Should().BeGreaterThan(0);
        refusal.Should().BeLessThan(mud.IndexOf("await InteractiveAuthAsync(", StringComparison.Ordinal));
        refusal.Should().BeLessThan(mud.IndexOf("sqlBackend.AuthenticatePlayer(username, password, effectiveIp)", StringComparison.Ordinal));

        // over the wire too: the refusal text and a closed socket, no ERR line
        var (output, _) = await AuthLine("203.0.113.9", "Real Name:secret:Web");
        output.Should().Contain("Connection refused: this address is banned from the server.").And.Contain("Reason: spam");
        output.Should().NotContain("ERR:");
    }

    // ================= the old keys are gone =================

    [Fact]
    public void OldKeys_AreRemoved_InAllFiveLanguages_AndNothingReadsThem()
    {
        var root = HardcodedTextScannerTests.RepoRoot();
        foreach (var lang in AllLanguages)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "Localization", lang + ".json")));
            var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();
            keys.Should().NotContain(new[] { "auth.err_unknown_username", "auth.err_wrong_password" }, $"[{lang}]");
            keys.Should().Contain(new[] { "auth.err_bad_login", "auth.err_bad_login_hint" }, $"[{lang}]");
            if (lang != "en")
            {
                L(lang, "auth.err_bad_login").Should().NotBe(L("en", "auth.err_bad_login"), $"[{lang}] translated");
                L(lang, "auth.err_bad_login_hint").Should().NotBe(L("en", "auth.err_bad_login_hint"), $"[{lang}] translated");
            }
        }
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "Scripts"), "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            text.Should().NotContain("auth.err_unknown_username", file);
            text.Should().NotContain("auth.err_wrong_password", file);
        }
    }

    // ================= operator logs: the name and the reason, never the password =================

    [Fact]
    public async Task OperatorLog_KeepsTheNameAndTheRealReason_NeverThePassword()
    {
        await Register("Real Name", "secret");
        const string typed = "Pw-Never-Logged-7731";

        var (outWrong, errWrong) = await AuthLine(ThrottleIp(31), $"Real Name:{typed}:Web");
        var (outUnknown, errUnknown) = await AuthLine(ThrottleIp(32), $"Ghost Name:{typed}:Web");

        errWrong.Should().Contain("[MUD] Auth failed for 'Real Name': WrongPassword");
        errUnknown.Should().Contain("[MUD] Auth failed for 'Ghost Name': UnknownName");
        (errWrong + errUnknown).Should().NotContain(typed);

        // the AUTH protocol line: the one sentence for both, no register hint
        ErrText(outWrong).Should().Be(ErrText(outUnknown));
        AllLanguages.Select(l => L(l, "auth.err_bad_login")).Should().Contain(ErrText(outWrong));
        foreach (var lang in AllLanguages) outWrong.Should().NotContain(L(lang, "auth.err_bad_login_hint"));

        // the interactive gate logs the same way
        var (_, errInteractive) = await Interactive(ThrottleIp(33), "L", "Real Name", typed);
        errInteractive.Should().Contain("[MUD] Auth failed for 'Real Name': WrongPassword");
        errInteractive.Should().NotContain(typed);

        MudServer.AuthFailedLogLine("Someone", SqlSaveBackend.LoginFailure.BannedWrongPassword)
            .Should().Be("[MUD] Auth failed for 'Someone': BannedWrongPassword");
    }

    /// <summary>The interactive (telnet and web terminal) gate: menu choice, two or three answers, then quit.</summary>
    private Task<(string Output, string Stderr)> Interactive(string ip, string choice, params string[] answers) =>
        Connect(async (s, output) =>
        {
            await Send(s, $"X-IP:{ip}\n");
            await ReadUntil(s, output, L("en", "auth.choice"));
            await Send(s, choice + "\n");
            foreach (var answer in answers)
            {
                await Task.Delay(150);
                await Send(s, answer + "\n");
            }
            // the failure (or refusal) rows, then the menu again
            await ReadUntilCount(s, output, L("en", "auth.choice"), 2);
            await Send(s, "Q\n");
            await ReadUntil(s, output, null);
        });

    // ================= the register hint only where R registers =================

    [Fact]
    public async Task InteractiveGate_ShowsTheMessage_ThenTheHintOnItsOwnRow()
    {
        await Register("Real Name", "secret");
        foreach (var (name, password) in new[] { ("Real Name", "wrong"), ("Ghost Name", "wrong") })
        {
            var (output, _) = await Interactive(ThrottleIp(name.Length + 40), "L", name, password);
            var rows = output.Split('\n').Select(r => Visible(r.TrimEnd('\r'))).ToList();
            int msg = rows.FindIndex(r => r == "  " + L("en", "auth.err_bad_login"));
            msg.Should().BeGreaterThan(-1, $"{name}: the generic message on its own row");
            rows[msg + 1].Should().Be("  " + L("en", "auth.err_bad_login_hint"), "the register hint follows on the next row");
            output.Should().Contain("[R]").And.Contain(L("en", "auth.register"), "R registers on this menu");
        }

        // the hint follows only the generic message
        SqlSaveBackend.LoginFailureRows("en", "m", SqlSaveBackend.LoginFailure.WrongPassword).Should().Equal("m", L("en", "auth.err_bad_login_hint"));
        foreach (var other in new[] { SqlSaveBackend.LoginFailure.Banned, SqlSaveBackend.LoginFailure.IpBanned, SqlSaveBackend.LoginFailure.Error })
            SqlSaveBackend.LoginFailureRows("en", "m", other).Should().Equal(new[] { "m" }, other.ToString());

        // the stdio screen (its menu has [R] Register) uses the same rows; the AUTH line has no hint
        // (the relay and the game client print it under their own [R] menus)
        string stdio = Src("Systems", "OnlineAuthScreen.cs");
        stdio.Should().Contain("SqlSaveBackend.LoginFailureRows(null, message, reason)").And.Contain("case \"R\":");
        Src("Server", "MudServer.cs").Should().Contain("await WriteLineAsync(stream, $\"ERR:{message}\");");
    }

    [Fact]
    public void LoginFailureRows_Fit79Columns_InEveryLanguage()
    {
        foreach (var lang in AllLanguages)
        {
            var msg = L(lang, "auth.err_bad_login");
            var hint = L(lang, "auth.err_bad_login_hint");
            var rendered = new[]
            {
                "  " + msg,                          // ANSI gate, stdio screen
                L(lang, "auth.err_prefix", msg),     // plain-text gate
                "  " + hint, hint,                   // the hint row, ANSI and plain
                "ERR:" + msg,                        // the AUTH line
                "  " + L(lang, "auth.err_account_banned") + " " + L(lang, "auth.err_ban_reason", new string('x', 20)),
            };
            foreach (var row in rendered)
                row.Length.Should().BeLessOrEqualTo(MaxWidth, $"[{lang}] {row}");
        }
    }

    // ================= stored hashes are left alone =================

    [Fact]
    public async Task Logins_NeverChangeAStoredHash()
    {
        await Register("Real Name", "secret");
        await Register("Banned One", "rightpw");
        Ban("Banned One", "griefing");
        NoHashAccount("nohash", "");
        var before = new[] { "Real Name", "Banned One", "nohash" }.Select(StoredHash).ToList();

        await Db.AuthenticatePlayer("Real Name", "wrong");
        (await Db.AuthenticatePlayer("Real Name", "secret")).success.Should().BeTrue();
        await Db.AuthenticatePlayer("Banned One", "wrong");
        await Db.AuthenticatePlayer("Banned One", "rightpw");
        await Db.AuthenticatePlayer("nohash", "anything");
        await Db.AuthenticatePlayer("nobody here", "anything");

        new[] { "Real Name", "Banned One", "nohash" }.Select(StoredHash).Should().Equal(before);
        (await Db.AuthenticatePlayer("Real Name", "secret")).success.Should().BeTrue("an existing password still works");
    }

    // ================= registration counts in the per-IP throttle =================

    [Fact]
    public async Task RegistrationAttempts_CountInThePerIpThrottle_KeyedByIp()
    {
        await Register("Real Name", "secret");

        // AUTH protocol: a taken name is refused and counts as a failure for that IP
        string ipA = ThrottleIp(51);
        var (outTaken, _) = await AuthLine(ipA, "Real Name:newpass:REGISTER:Web");
        AllLanguages.Select(l => L(l, "auth.err_username_taken")).Should().Contain(ErrText(outTaken), "the name-taken message stays");
        FailedLogins()[ipA].Fails.Should().Be(1);

        // the interactive gate counts it too
        string ipB = ThrottleIp(52);
        await Interactive(ipB, "R", "Real Name", "newpass", "newpass");
        FailedLogins()[ipB].Fails.Should().Be(1);

        // a throttled IP cannot register
        string ipC = ThrottleIp(53);
        FailedLogins()[ipC] = (10, DateTime.UtcNow, DateTime.UtcNow.AddSeconds(60));
        var (outLocked, _) = await AuthLine(ipC, "Brand New:newpass:REGISTER:Web");
        ErrText(outLocked).Should().StartWith("Too many failed logins.");
        Db.PlayerExists("Brand New").Should().BeFalse();

        // keyed by IP, not by account: failures from one address never lock out the account elsewhere
        string ipD = ThrottleIp(54);
        await AuthLine(ipD, "Real Name:wrong:Web");
        FailedLogins().Keys.Should().Contain(ipD).And.NotContain(k => k.Contains("real", StringComparison.OrdinalIgnoreCase));
        IsLoginThrottled(ipC).Should().BeTrue();
        IsLoginThrottled(ThrottleIp(55)).Should().BeFalse("another address is not throttled by ipC's failures");
    }
}
