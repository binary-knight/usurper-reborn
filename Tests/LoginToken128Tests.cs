using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
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
/// The test sockets of the 1.2.8 token tests: every listener and every client goes through here. Two guards:
/// the host is a seam that defaults to loopback, and <see cref="Guard"/> refuses any other address before a
/// socket is made. Both are proven by <see cref="LoginToken128Tests"/> before any negative runs.
/// </summary>
internal static class TestLoopback
{
    /// <summary>Guard 1: the address tests listen on and connect to.</summary>
    internal static IPAddress Host = IPAddress.Loopback;

    /// <summary>Guard 2: any address that is not loopback throws here, before a socket exists.</summary>
    internal static IPAddress Guard(IPAddress address)
    {
        if (!IPAddress.IsLoopback(address))
            throw new InvalidOperationException($"test sockets stay on loopback; refused {address}");
        return address;
    }

    internal static TcpListener Listen()
    {
        var listener = new TcpListener(Guard(Host), 0);
        listener.Start();
        return listener;
    }

    internal static async Task<TcpClient> Connect(int port)
    {
        var address = Guard(Host);
        var tcp = new TcpClient();
        await tcp.ConnectAsync(address, port);
        return tcp;
    }
}

/// <summary>
/// 1.2.8: the remember-me token, server side (design C3, rows 1-21 of the server and relay piece). Old client
/// shapes are unchanged; AUTH:CAPS answers the capability; ";token=1" on a password or REGISTER login issues one
/// token line after OK; AUTH:TOKEN1 logs in with it. Only SHA-256 of the verifier is stored, with a stamp of the
/// password hash; 90 days, five per account; revoked at a password change or reset, a ban and an erase; pruned
/// at start and in the admin poller. No token is ever logged or echoed.
/// </summary>
[Collection("SharedGameSingletons")]
public class LoginToken128Tests : IDisposable
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-token-128-{Guid.NewGuid():N}.db");
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

    private static string L(string key, params object[] args) => Loc.GetIn(GameConfig.Language, key, args);

    private SqliteConnection Open()
    {
        _ = Db;
        var c = new SqliteConnection($"Data Source={_path}");
        c.Open();
        return c;
    }

    private void Exec(string sql, params (string Name, object Value)[] args)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

    private object? Scalar(string sql, params (string Name, object Value)[] args)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        return cmd.ExecuteScalar();
    }

    private long TokenRows(string username) =>
        (long)Scalar("SELECT COUNT(*) FROM login_tokens WHERE username = @u;", ("@u", username.ToLowerInvariant()))!;

    private Dictionary<string, object?> Row(string selector)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM login_tokens WHERE selector = @s;";
        cmd.Parameters.AddWithValue("@s", selector);
        using var r = cmd.ExecuteReader();
        r.Read().Should().BeTrue($"a row for the selector");
        var row = new Dictionary<string, object?>();
        for (int i = 0; i < r.FieldCount; i++) row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
        return row;
    }

    private async Task Register(string name, string password) =>
        (await Db.RegisterPlayer(name, password)).success.Should().BeTrue();

    private string Issue(string name, string type = "Steam")
    {
        var issued = Db.IssueLoginToken(name, type);
        issued.Should().NotBeNull("a token is issued to an account with a password");
        return issued!.Value.Token;
    }

    private static (string Selector, string Verifier) Split(string token)
    {
        LoginTokenProtocol.TrySplitToken(token, out var s, out var v).Should().BeTrue();
        return (s, v);
    }

    private static string HashPassword(string password) =>
        (string)typeof(SqlSaveBackend).GetMethod("HashPassword", SNP)!.Invoke(null, new object[] { password })!;

    private static string WrongVerifier(string verifier) =>
        (verifier[0] == 'A' ? "B" : "A") + verifier.Substring(1);

    private static ConcurrentDictionary<string, (int Fails, DateTime LastFail, DateTime LockUntil)> FailedLogins() =>
        (ConcurrentDictionary<string, (int, DateTime, DateTime)>)typeof(MudServer).GetField("_failedLogins", SNP)!.GetValue(null)!;

    private string ThrottleIp(int n)
    {
        var ip = $"198.51.100.{n}";
        FailedLogins().TryRemove(ip, out _);
        _throttleIps.Add(ip);
        return ip;
    }

    private static string Src(params string[] path) =>
        File.ReadAllText(Path.Combine(new[] { HardcodedTextScannerTests.RepoRoot(), "Scripts" }.Concat(path).ToArray()));

    /// <summary>Copies what is written to stderr while a test runs, still passing it through.</summary>
    internal sealed class StderrTap : TextWriter
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

    /// <summary>A timer-less logger swapped in as the DebugLogger instance, so every line stays readable.</summary>
    internal sealed class LogCapture : IDisposable
    {
        private static readonly FieldInfo InstanceField = typeof(DebugLogger).GetField("_instance", SNP)!;
        private readonly object? _previous;
        private readonly ConcurrentQueue<string> _queue = new();

        public LogCapture()
        {
            _previous = InstanceField.GetValue(null);
            var logger = (DebugLogger)RuntimeHelpers.GetUninitializedObject(typeof(DebugLogger));
            typeof(DebugLogger).GetField("logQueue", NP)!.SetValue(logger, _queue);
            typeof(DebugLogger).GetField("isEnabled", NP)!.SetValue(logger, true);
            typeof(DebugLogger).GetField("minimumLevel", NP)!.SetValue(logger, DebugLogger.LogLevel.Debug);
            InstanceField.SetValue(null, logger);
        }

        public string All => string.Join("\n", _queue.ToArray());

        public void Dispose() => InstanceField.SetValue(null, _previous);
    }

    private static MudServer NewServer()
    {
        var server = (MudServer)RuntimeHelpers.GetUninitializedObject(typeof(MudServer));
        typeof(MudServer).GetField("<ActiveSessions>k__BackingField", NP)!
            .SetValue(server, new ConcurrentDictionary<string, PlayerSession>());
        server.StopAfterLoginReply = true; // the replies only: no game session starts in these tests
        return server;
    }

    /// <summary>
    /// One real loopback connection into MudServer.HandleConnectionAsync: send <paramref name="request"/>, read
    /// until the server closes or, after an OK, until <paramref name="afterOkMs"/> passes with nothing new; then
    /// close and cancel. Returns the lines the client read, the server's stderr and the server object.
    /// </summary>
    private async Task<(List<string> Lines, string Raw, string Stderr, MudServer Server)> Exchange(string request, int afterOkMs = 700)
    {
        using var listener = new DisposableListener(TestLoopback.Listen());
        var port = ((IPEndPoint)listener.Inner.LocalEndpoint).Port;
        var oldErr = Console.Error;
        var tap = new StderrTap(oldErr);
        Console.SetError(tap);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var output = new StringBuilder();
        var server = NewServer();
        try
        {
            var acceptTask = listener.Inner.AcceptTcpClientAsync();
            using var tcp = await TestLoopback.Connect(port);
            var accepted = await acceptTask;
            var handle = typeof(MudServer).GetMethod("HandleConnectionAsync", NP)!;
            var serverTask = (Task)handle.Invoke(server, new object[] { accepted, Db, cts.Token })!;
            var stream = tcp.GetStream();
            var bytes = Encoding.UTF8.GetBytes(request);
            await stream.WriteAsync(bytes, 0, bytes.Length);
            await stream.FlushAsync();

            var buffer = new byte[4096];
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                bool ok = output.ToString().StartsWith("OK\r\n", StringComparison.Ordinal);
                using var readCts = new CancellationTokenSource(ok ? TimeSpan.FromMilliseconds(afterOkMs) : TimeSpan.FromSeconds(15));
                int read;
                try { read = await stream.ReadAsync(buffer, 0, buffer.Length, readCts.Token); }
                catch (Exception) { break; }
                if (read == 0) break;
                output.Append(Encoding.UTF8.GetString(buffer, 0, read));
            }
            tcp.Close();
            cts.Cancel();
            try { await serverTask.WaitAsync(TimeSpan.FromSeconds(20)); } catch { /* the session after OK may end any way */ }
        }
        finally
        {
            Console.SetError(oldErr);
        }
        var raw = output.ToString();
        var lines = raw.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        return (lines, raw, tap.Seen, server);
    }

    private sealed class DisposableListener : IDisposable
    {
        public readonly TcpListener Inner;
        public DisposableListener(TcpListener inner) => Inner = inner;
        public void Dispose() => Inner.Stop();
    }

    private static string? LineAfterOk(List<string> lines)
    {
        int i = lines.IndexOf("OK");
        return i >= 0 && i + 1 < lines.Count ? lines[i + 1] : null;
    }

    // ================= the two guards on every test socket, proven first =================

    [Fact]
    public async Task Guard1_TestSockets_DefaultToLoopback()
    {
        TestLoopback.Host.Should().Be(IPAddress.Loopback);
        using var listener = new DisposableListener(TestLoopback.Listen());
        ((IPEndPoint)listener.Inner.LocalEndpoint).Address.Should().Be(IPAddress.Loopback);
        var accept = listener.Inner.AcceptTcpClientAsync();
        using var tcp = await TestLoopback.Connect(((IPEndPoint)listener.Inner.LocalEndpoint).Port);
        using var peer = await accept;
        IPAddress.IsLoopback(((IPEndPoint)peer.Client.RemoteEndPoint!).Address).Should().BeTrue();
    }

    [Fact]
    public async Task Guard2_TestSockets_RefuseANonLoopbackHost_BeforeConnecting()
    {
        var unroutable = IPAddress.Parse("192.0.2.1"); // TEST-NET-1: reserved, never routed
        var saved = TestLoopback.Host;
        TestLoopback.Host = unroutable;
        try
        {
            var started = DateTime.UtcNow;
            Func<Task> connect = () => TestLoopback.Connect(4000).WaitAsync(TimeSpan.FromSeconds(2));
            await connect.Should().ThrowAsync<InvalidOperationException>().WithMessage("*loopback*");
            Action listen = () => TestLoopback.Listen();
            listen.Should().Throw<InvalidOperationException>();
            (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(1), "refused before any connect attempt");
        }
        finally { TestLoopback.Host = saved; }
    }

    // ================= row 1, 2: old client shapes unchanged, no TOKEN line without the request =================

    [Theory]
    [InlineData("AUTH:Old Timer:oldpass:Steam\n")]                                  // 4 parts
    [InlineData("AUTH:Old Timer:oldpass:Steam:1.2.7\n")]                            // 5 parts with version
    [InlineData("X-IP:198.51.100.71\nAUTH:Old Timer:oldpass:Local:1.0.0\n")]        // relay X-IP
    [InlineData("X-IP:198.51.100.72\nX-Client:Web\nAUTH:Old Timer:oldpass:Web\n")]  // X-IP and X-Client
    [InlineData("AUTH:Old Timer:oldpass:SSH;echo=1\n")]                             // 1.0.6 echo parameter
    [InlineData("AUTH:Old Timer:Steam\n")]                                          // 3 parts, trusted from loopback
    [InlineData("AUTH:CAPS:capspass:Steam\n")]                                      // an account named CAPS
    [InlineData("AUTH:TOKEN1 x:tokpass:Steam:1.2.8\n")]                             // an account named "TOKEN1 x"
    [InlineData("AUTH:Fresh One:freshpw:REGISTER:Steam\n")]                         // registration
    public async Task Row01_OldClientShapes_GetOkAsBefore_AndNoTokenLine(string request)
    {
        await Register("Old Timer", "oldpass");
        await Register("CAPS", "capspass");
        await Register("TOKEN1 x", "tokpass");
        foreach (var ip in new[] { "198.51.100.71", "198.51.100.72" }) { FailedLogins().TryRemove(ip, out _); _throttleIps.Add(ip); }

        var (lines, raw, stderr, _) = await Exchange(request);

        lines[0].Should().Be("OK", $"the old shape logs in as it did before 1.2.8: {raw}");
        (LineAfterOk(lines) ?? "").Should().NotStartWith("TOKEN:", "row 2: no token line without ;token=1");
        raw.Should().NotContain("TOKEN:").And.NotContain("CAPS:token");
        stderr.Should().NotContain("auth=token");
        ((long)Scalar("SELECT COUNT(*) FROM login_tokens;")!).Should().Be(0, "nothing is issued without the request");
    }

    // ================= row 4: the probe =================

    [Fact]
    public async Task Row04_Probe_AnswersCaps_AndCloses_NoSessionNoThrottleNoLog()
    {
        var ip = ThrottleIp(41);
        var (lines, raw, stderr, server) = await Exchange($"X-IP:{ip}\nAUTH:CAPS\n");

        raw.Should().Be("CAPS:token=1\r\n", "one line, then the server closes");
        server.ActiveSessions.Should().BeEmpty();
        FailedLogins().ContainsKey(ip).Should().BeFalse("the probe is not a failed login");
        stderr.Should().NotContain("Connection from").And.NotContain("Auth failed").And.NotContain("Invalid auth");
    }

    // ================= row 7: issuance after ;token=1 =================

    [Fact]
    public async Task Row07_PasswordLoginWithTokenRequest_SendsOk_ThenOneTokenLine_AndMakesTheRow()
    {
        await Register("Remember Me", "rememberpw");
        var (lines, raw, stderr, _) = await Exchange("AUTH:Remember Me:rememberpw:Steam;token=1:1.2.8\n");

        lines[0].Should().Be("OK", $"OK stays the first line: {raw}");
        var tokenLine = lines[1];
        tokenLine.Should().StartWith("TOKEN:");
        var parts = tokenLine.Split(':');
        parts.Should().HaveCount(3, "TOKEN:<selector>.<verifier>:<expiresUnix>");
        var (selector, verifier) = Split(parts[1]);
        var row = Row(selector);
        row["username"].Should().Be("remember me");
        row["client_label"].Should().Be("Steam", "the label is the bare type name");
        long.Parse(parts[2]).Should().Be(DateTimeOffset.Parse((string)row["expires_at"]!).ToUnixTimeSeconds());
        lines.Count(l => l.StartsWith("TOKEN:")).Should().Be(1, "exactly one token line");
        stderr.Should().Contain("type=Steam,").And.Contain("auth=password");
    }

    [Fact]
    public async Task Row07_RegistrationWithTokenRequest_SendsOk_ThenOneTokenLine()
    {
        var (lines, raw, _, _) = await Exchange("AUTH:New Comer:newpw1:REGISTER:Steam;token=1\n");
        lines[0].Should().Be("OK", raw);
        lines[1].Should().StartWith("TOKEN:");
        var (selector, _) = Split(lines[1].Split(':')[1]);
        Row(selector)["username"].Should().Be("new comer");
        TokenRows("New Comer").Should().Be(1);
    }

    [Fact]
    public async Task Row07_TokenRequest_WhenNoRowCanBeMade_SendsTokenDash()
    {
        await Register("No Table", "notablepw");
        Exec("DROP TABLE login_tokens;");
        var (lines, raw, _, _) = await Exchange("AUTH:No Table:notablepw:Steam;token=1:1.2.8\n");
        lines[0].Should().Be("OK", raw);
        lines[1].Should().Be("TOKEN:-", "a failure still answers with one line");
    }

    [Fact]
    public async Task Row07_WrongPasswordWithTokenRequest_IssuesNothing()
    {
        await Register("Careful", "rightpw");
        var ip = ThrottleIp(77);
        var (lines, raw, _, _) = await Exchange($"X-IP:{ip}\nAUTH:Careful:wrongpw:Steam;token=1:1.2.8\n");
        lines[0].Should().Be("ERR:" + L("auth.err_bad_login"));
        raw.Should().NotContain("TOKEN:");
        TokenRows("Careful").Should().Be(0);
    }

    // ================= row 8: only SHA-256 of the verifier is stored =================

    [Fact]
    public async Task Row08_NoColumnHoldsTheTokenOrVerifier_AndTheHashIsSha256OfTheVerifier()
    {
        await Register("Hashed", "hashedpw");
        var token = Issue("Hashed");
        var (selector, verifier) = Split(token);
        var row = Row(selector);
        foreach (var (column, value) in row)
        {
            var text = value?.ToString() ?? "";
            text.Should().NotContain(verifier, $"column {column} never holds the verifier");
            text.Should().NotContain(token, $"column {column} never holds the token");
        }
        var raw = LoginTokenProtocol.DecodeVerifier(verifier)!;
        row["verifier_hash"].Should().Be(Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(raw)));
        row["pw_stamp"].Should().Be(Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes((string)Scalar("SELECT password_hash FROM players WHERE username = 'hashed';")!))));
        selector.Should().MatchRegex("^[0-9a-f]{32}$");
        verifier.Should().MatchRegex("^[A-Za-z0-9_-]{43}$");
        token.Should().NotContain(":", "the token never holds the AUTH separator");
    }

    // ================= row 10, 11: token login =================

    [Fact]
    public void Server_StopAfterLoginReply_IsOffUnlessATestSetsIt()
    {
        var src = Src("Server", "MudServer.cs");
        src.Should().Contain("internal bool StopAfterLoginReply;", "no initializer: false on every real server");
        System.Text.RegularExpressions.Regex.Matches(src, @"StopAfterLoginReply\s*=").Count.Should().Be(0, "the server never sets it");
    }

    [Fact]
    public async Task Row10_TokenLogin_LogsInAsTheRowsAccount_AndSetsLastUsed()
    {
        await Register("Token User", "tokenpw");
        var token = Issue("Token User");
        var (selector, _) = Split(token);
        Row(selector)["last_used_at"].Should().BeNull();
        var ip = ThrottleIp(10);

        var (lines, raw, stderr, _) = await Exchange($"X-IP:{ip}\n{LoginTokenProtocol.FormatTokenLogin(token, "Steam", "1.2.8")}\n");

        lines[0].Should().Be("OK", raw);
        (LineAfterOk(lines) ?? "").Should().NotStartWith("TOKEN:", "no rotation: a token login issues nothing");
        stderr.Should().Contain("user=token user, type=Steam, auth=token");
        Row(selector)["last_used_at"].Should().NotBeNull("the use is recorded");
        TokenRows("Token User").Should().Be(1);
    }

    [Fact]
    public async Task Row11_RightSelectorWrongVerifier_IsRefused_AndCountsInTheThrottle()
    {
        await Register("Guarded", "guardedpw");
        var (selector, verifier) = Split(Issue("Guarded"));
        var ip = ThrottleIp(11);

        var (lines, raw, stderr, server) = await Exchange(
            $"X-IP:{ip}\n{LoginTokenProtocol.FormatTokenLogin($"{selector}.{WrongVerifier(verifier)}", "Steam", "1.2.8")}\n");

        lines[0].Should().Be(LoginTokenProtocol.TokenRefusedPrefix + L("auth.err_bad_login"), raw); // 1.2.8 C3c: the refused mark
        server.ActiveSessions.Should().BeEmpty();
        FailedLogins()[ip].Fails.Should().Be(1, "a wrong token counts as a failed login");
        stderr.Should().Contain(MudServer.AuthFailedLogLine("guarded", SqlSaveBackend.LoginFailure.TokenMismatch));
        Row(selector)["last_used_at"].Should().BeNull();
    }

    [Theory]
    [InlineData("AUTH:TOKEN1 ")]
    [InlineData("AUTH:TOKEN1 short.short Steam")]
    [InlineData("AUTH:TOKEN1 {0} Steam 1.2.8 extra")]
    [InlineData("AUTH:TOKEN1 {0} St/eam 1.2.8")]
    [InlineData("AUTH:TOKEN1 {0}")]
    public async Task Row11_MalformedTokenLine_GenericErr_CountsAsAFailure(string shape)
    {
        var ip = ThrottleIp(12);
        var t = LoginTokenProtocol.NewToken();
        var ver = t.Verifier;
        var line = string.Format(shape, $"{t.Selector}.{t.Verifier}");
        var (lines, raw, stderr, _) = await Exchange($"X-IP:{ip}\n{line}\n");
        lines[0].Should().Be(LoginTokenProtocol.TokenRefusedPrefix + L("auth.err_bad_login"), raw); // 1.2.8 C3c: the refused mark
        FailedLogins()[ip].Fails.Should().Be(1);
        stderr.Should().Contain(MudServer.AuthFailedLogLine("?", SqlSaveBackend.LoginFailure.TokenMalformed));
        stderr.Should().NotContain(ver);
    }

    [Fact]
    public async Task Row11_ThrottledIp_IsRefusedBeforeTheLookup()
    {
        await Register("Throttled", "throttledpw");
        var token = Issue("Throttled");
        var ip = ThrottleIp(13);
        FailedLogins()[ip] = (99, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(5));
        int before = Db.TokenVerifies;
        var (lines, raw, _, _) = await Exchange($"X-IP:{ip}\n{LoginTokenProtocol.FormatTokenLogin(token, "Steam", null)}\n");
        lines[0].Should().StartWith("ERR:").And.NotBe("ERR:" + L("auth.err_bad_login"))
            .And.NotStartWith(LoginTokenProtocol.TokenRefusedPrefix, "1.2.8 C3c: a throttle keeps the client's token");
        Db.TokenVerifies.Should().Be(before, "no lookup while the IP is locked");
    }

    // ================= row 12: constant time, dummy compare =================

    [Fact]
    public async Task Row12_UnknownSelector_StillRunsOneCompare_AgainstTheDummy()
    {
        await Register("Known", "knownpw");
        var (selector, verifier) = Split(Issue("Known"));
        var fresh = LoginTokenProtocol.NewToken();

        int v0 = Db.TokenVerifies, d0 = Db.TokenDummyVerifies;
        var unknown = Db.AuthenticateByToken(fresh.Selector, fresh.Verifier);
        unknown.success.Should().BeFalse();
        unknown.reason.Should().Be(SqlSaveBackend.LoginFailure.TokenUnknown);
        unknown.message.Should().Be(L("auth.err_bad_login"));
        (Db.TokenVerifies - v0).Should().Be(1, "an unknown selector does the same compare");
        (Db.TokenDummyVerifies - d0).Should().Be(1, "against the dummy hash");

        v0 = Db.TokenVerifies; d0 = Db.TokenDummyVerifies;
        var wrong = Db.AuthenticateByToken(selector, WrongVerifier(verifier));
        wrong.reason.Should().Be(SqlSaveBackend.LoginFailure.TokenMismatch);
        wrong.message.Should().Be(unknown.message, "the same text whether or not the selector exists");
        (Db.TokenVerifies - v0).Should().Be(1);
        (Db.TokenDummyVerifies - d0).Should().Be(0);

        var src = Src("Systems", "SqlSaveBackend.cs");
        int at = src.IndexOf("private bool TokenVerify(", StringComparison.Ordinal);
        at.Should().BeGreaterThan(0);
        src.Substring(at, 600).Should().Contain("CryptographicOperations.FixedTimeEquals(presented, expected)");
    }

    // ================= row 13: 90 days, fixed =================

    [Fact]
    public async Task Row13_ExpiresNinetyDaysAfterIssue_ThenIsRefused_AndItsRowDeleted()
    {
        await Register("Clocked", "clockedpw");
        var issuedAt = new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
        Db.UtcNow = () => issuedAt;
        var issued = Db.IssueLoginToken("Clocked", "Local")!.Value;
        var (selector, verifier) = Split(issued.Token);
        var row = Row(selector);
        row["created_at"].Should().Be("2026-10-07T12:00:00Z");
        row["expires_at"].Should().Be("2027-01-05T12:00:00Z", "created + 90 days exactly");
        issued.ExpiresUnix.Should().Be(new DateTimeOffset(issuedAt.AddDays(90)).ToUnixTimeSeconds());

        Db.UtcNow = () => issuedAt.AddDays(90).AddSeconds(-1);
        Db.AuthenticateByToken(selector, verifier).success.Should().BeTrue("one second before expiry it works");
        Row(selector)["expires_at"].Should().Be("2027-01-05T12:00:00Z", "a use never extends it");

        Db.UtcNow = () => issuedAt.AddDays(90);
        var late = Db.AuthenticateByToken(selector, verifier);
        late.success.Should().BeFalse("at 90 days the token is expired");
        late.reason.Should().Be(SqlSaveBackend.LoginFailure.TokenExpired);
        late.message.Should().Be(L("auth.err_bad_login"));
        Scalar("SELECT COUNT(*) FROM login_tokens WHERE selector = @s;", ("@s", selector)).Should().Be(0L, "the expired row is deleted");
    }

    // ================= rows 14-18: revocation =================

    [Fact]
    public async Task Row14_ChangePassword_RevokesEveryToken()
    {
        await Register("Changer", "oldpw1");
        var (s1, v1) = Split(Issue("Changer"));
        Issue("Changer");
        TokenRows("Changer").Should().Be(2);
        (await Db.ChangePassword("Changer", "oldpw1", "newpw2")).success.Should().BeTrue();
        TokenRows("Changer").Should().Be(0, "a password change deletes the rows");
        Db.AuthenticateByToken(s1, v1).success.Should().BeFalse();
    }

    [Fact]
    public async Task Row15_AdminResetPassword_RevokesEveryToken()
    {
        await Register("Resetee", "oldpw1");
        var (s1, v1) = Split(Issue("Resetee"));
        Db.AdminResetPassword("Resetee", "newpw2").success.Should().BeTrue();
        TokenRows("Resetee").Should().Be(0, "an admin reset deletes the rows");
        Db.AuthenticateByToken(s1, v1).success.Should().BeFalse();
    }

    [Fact]
    public async Task Row16_AnyDirectWriteOfThePasswordHash_RefusesTheToken()
    {
        await Register("Stamped", "stampedpw");
        var (selector, verifier) = Split(Issue("Stamped"));
        Db.AuthenticateByToken(selector, verifier).success.Should().BeTrue();
        // as the web reset route or any future path that forgets to revoke
        Exec("UPDATE players SET password_hash = @h WHERE username = 'stamped';", ("@h", HashPassword("another1")));
        TokenRows("Stamped").Should().Be(1, "nothing revoked the row");
        var r = Db.AuthenticateByToken(selector, verifier);
        r.success.Should().BeFalse("the password stamp no longer matches");
        r.reason.Should().Be(SqlSaveBackend.LoginFailure.TokenStale);
        r.message.Should().Be(L("auth.err_bad_login"));
        TokenRows("Stamped").Should().Be(0, "the stale row is deleted");
    }

    [Fact]
    public async Task Row17_BanPlayer_RevokesEveryToken_AndUnbanDoesNotBringThemBack()
    {
        await Register("Banned Soon", "bannedpw");
        var (selector, verifier) = Split(Issue("Banned Soon"));
        await Db.BanPlayer("Banned Soon", "testing");
        TokenRows("Banned Soon").Should().Be(0, "a ban deletes the rows");
        await Db.UnbanPlayer("Banned Soon");
        TokenRows("Banned Soon").Should().Be(0);
        var r = Db.AuthenticateByToken(selector, verifier);
        r.success.Should().BeFalse("an unban does not revive a token");
        r.reason.Should().Be(SqlSaveBackend.LoginFailure.TokenUnknown);
    }

    [Fact]
    public async Task Row17_ABannedAccountsLeftoverRow_IsStillRefused_WithTheBanText()
    {
        await Register("Leftover", "leftoverpw");
        var (selector, verifier) = Split(Issue("Leftover"));
        // a ban written straight to the table (the web route, or an older server): the row stays
        Exec("UPDATE players SET is_banned = 1, ban_reason = 'griefing' WHERE username = 'leftover';");
        var r = Db.AuthenticateByToken(selector, verifier);
        r.success.Should().BeFalse();
        r.reason.Should().Be(SqlSaveBackend.LoginFailure.Banned);
        r.message.Should().Be(L("auth.err_account_banned") + " " + L("auth.err_ban_reason", "griefing"));
    }

    [Fact]
    public async Task Row18_HardDelete_RemovesTheRows_AndAReRegisteredNameRefusesTheOldToken()
    {
        await Register("Erased", "erasedpw");
        var (selector, verifier) = Split(Issue("Erased"));
        Db.DeleteAccountCompletely("Erased").Should().BeTrue();
        TokenRows("Erased").Should().Be(0, "the erase deletes the rows");
        ((ConcurrentDictionary<string, byte>)typeof(SqlSaveBackend).GetField("RageEventErasedUsernames", SNP)!.GetValue(null)!)
            .TryRemove("erased", out _);
        await Register("Erased", "someoneelse");
        var r = Db.AuthenticateByToken(selector, verifier);
        r.success.Should().BeFalse("a new account of the same name is not the old one");
    }

    // ================= row 19: five per account =================

    [Fact]
    public async Task Row19_AtMostFiveRowsPerAccount_TheLeastRecentlyUsedGoes()
    {
        await Register("Many Devices", "manypw");
        var start = new DateTime(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
        var tokens = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            Db.UtcNow = () => start.AddMinutes(tokens.Count);
            tokens.Add(Issue("Many Devices", $"Dev{i}"));
        }
        // the oldest one is used later, so the second oldest is the least recently used
        Db.UtcNow = () => start.AddMinutes(10);
        var (s0, v0) = Split(tokens[0]);
        Db.AuthenticateByToken(s0, v0).success.Should().BeTrue();

        Db.UtcNow = () => start.AddMinutes(11);
        tokens.Add(Issue("Many Devices", "Dev5"));
        TokenRows("Many Devices").Should().Be(5, "a sixth drops one");
        var (s1, v1) = Split(tokens[1]);
        Scalar("SELECT COUNT(*) FROM login_tokens WHERE selector = @s;", ("@s", s1)).Should().Be(0L, "the least recently used went");
        Db.AuthenticateByToken(s1, v1).success.Should().BeFalse();
        Db.AuthenticateByToken(s0, v0).success.Should().BeTrue("the oldest issued was used recently and stays");
        var (s5, v5) = Split(tokens[5]);
        Db.AuthenticateByToken(s5, v5).success.Should().BeTrue();
    }

    // ================= row 20: prune at start and in the poller =================

    [Fact]
    public async Task Row20_ExpiredRows_ArePruned_ByThePrune_AtStart_AndByThePoller()
    {
        await Register("Pruned", "prunedpw");
        await Register("Still Here", "stillpw");
        var now = DateTime.UtcNow;
        Db.UtcNow = () => now.AddDays(-100);
        var (old1, _) = Split(Issue("Pruned"));
        Db.UtcNow = () => now;
        var (live, _) = Split(Issue("Still Here")); // another account: an issue also clears its own account's expired rows
        Db.PruneExpiredLoginTokens().Should().Be(1);
        Scalar("SELECT COUNT(*) FROM login_tokens WHERE selector = @s;", ("@s", old1)).Should().Be(0L);
        Scalar("SELECT COUNT(*) FROM login_tokens WHERE selector = @s;", ("@s", live)).Should().Be(1L);

        // at database start
        Db.UtcNow = () => now.AddDays(-100);
        var (old2, _) = Split(Issue("Pruned"));
        Db.UtcNow = () => now;
        _ = new SqlSaveBackend(_path);
        Scalar("SELECT COUNT(*) FROM login_tokens WHERE selector = @s;", ("@s", old2)).Should().Be(0L, "pruned when the database starts");

        // in the admin poller's periodic cleanup
        Db.UtcNow = () => now.AddDays(-100);
        var (old3, _) = Split(Issue("Pruned"));
        Db.UtcNow = () => now;
        MudServer.PeriodicCleanup(Db);
        Scalar("SELECT COUNT(*) FROM login_tokens WHERE selector = @s;", ("@s", old3)).Should().Be(0L, "pruned by the poller");
        Scalar("SELECT COUNT(*) FROM login_tokens WHERE selector = @s;", ("@s", live)).Should().Be(1L);
        Src("Server", "MudServer.cs").Should().Contain("PeriodicCleanup(_sqlBackend);", "the poller runs the cleanup");
    }

    // ================= row 21: no token in any log line or ERR line =================

    [Fact]
    public async Task Row21_NoTokenInAnyServerLogOrErrLine_AcrossEveryPath()
    {
        using var log = new LogCapture();
        var secrets = new List<string>();
        var stderr = new StringBuilder();
        var replies = new StringBuilder();
        async Task Run(string request)
        {
            var (_, raw, err, _) = await Exchange(request);
            stderr.Append(err);
            replies.Append(raw);
        }

        await Register("Quiet One", "quietpw");
        var ip = ThrottleIp(21);
        // issue over the wire
        var (lines, raw0, err0, _) = await Exchange($"X-IP:{ip}\nAUTH:Quiet One:quietpw:Steam;token=1:1.2.8\n");
        stderr.Append(err0);
        var issued = lines[1].Split(':')[1];
        secrets.Add(issued);
        var (sel, ver) = Split(issued);
        secrets.Add(ver);
        // success
        await Run($"X-IP:{ip}\n{LoginTokenProtocol.FormatTokenLogin(issued, "Steam", "1.2.8")}\n");
        // mismatch, unknown, malformed
        await Run($"X-IP:{ip}\n{LoginTokenProtocol.FormatTokenLogin($"{sel}.{WrongVerifier(ver)}", "Steam", null)}\n");
        var stranger = LoginTokenProtocol.NewToken();
        secrets.Add(stranger.Verifier);
        await Run($"X-IP:{ip}\n{LoginTokenProtocol.FormatTokenLogin($"{stranger.Selector}.{stranger.Verifier}", "Steam", null)}\n");
        await Run($"X-IP:{ip}\nAUTH:TOKEN1 {sel}.{ver} Ste/am\n");
        FailedLogins().TryRemove(ip, out _);
        // banned leftover, stale, expired
        Exec("UPDATE players SET is_banned = 1 WHERE username = 'quiet one';");
        await Run($"X-IP:{ip}\n{LoginTokenProtocol.FormatTokenLogin(issued, "Steam", null)}\n");
        Exec("UPDATE players SET is_banned = 0, password_hash = @h WHERE username = 'quiet one';", ("@h", HashPassword("changed1")));
        await Run($"X-IP:{ip}\n{LoginTokenProtocol.FormatTokenLogin(issued, "Steam", null)}\n");
        var later = Issue("Quiet One");
        secrets.Add(later);
        Db.UtcNow = () => DateTime.UtcNow.AddDays(91);
        await Run($"X-IP:{ip}\n{LoginTokenProtocol.FormatTokenLogin(later, "Steam", null)}\n");
        Db.UtcNow = () => DateTime.UtcNow;
        // throttled
        FailedLogins()[ip] = (99, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(5));
        await Run($"X-IP:{ip}\n{LoginTokenProtocol.FormatTokenLogin(later, "Steam", null)}\n");

        stderr.ToString().Should().Contain("TokenMismatch").And.Contain("TokenUnknown").And.Contain("TokenMalformed")
            .And.Contain("Banned").And.Contain("TokenStale").And.Contain("TokenExpired").And.Contain("throttled");
        var logs = stderr + "\n" + log.All;
        foreach (var secret in secrets.Concat(new[] { "TOKEN:", "TOKEN1" }))
            logs.Should().NotContain(secret, "no log line holds a token, a verifier or a token line");
        foreach (var errLine in replies.ToString().Split('\n').Where(l => l.StartsWith("ERR:")))
            foreach (var secret in secrets)
                errLine.Should().NotContain(secret, "no ERR line echoes the token");
    }

    // ================= row 3: protocol strings stay English and the 1.1 literals are untouched =================

    [Fact]
    public void Row03_NewProtocolLines_AreEnglishConstants()
    {
        var src = Src("Server", "LoginTokenProtocol.cs");
        src.Should().Contain("CapsLine = \"AUTH:CAPS\"").And.Contain("CapsReply = \"CAPS:token=1\"")
            .And.Contain("TokenLoginPrefix = \"AUTH:TOKEN1 \"").And.Contain("TokenReplyPrefix = \"TOKEN:\"")
            .And.Contain("TokenReplyNone = \"TOKEN:-\"").And.Contain("OkLine = \"OK\"").And.Contain("ErrPrefix = \"ERR:\"");
        LoginTokenProtocol.CapsReply.Should().Be("CAPS:token=1");
    }
}
