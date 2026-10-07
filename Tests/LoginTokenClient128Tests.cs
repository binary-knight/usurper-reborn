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
/// 1.2.8: the remember-me token, game client side (design C3 rows 23-33 and 35). The client never stores a
/// password: after a remembered login it keeps the server's token for that one server and port, in the per-user
/// data directory, mode 0600, never in BBS door mode. It probes with AUTH:CAPS and sends the token, the remember
/// request or the old saved password only as the design allows; the old file is converted and its password erased
/// whatever the outcome. Replies are matched by whole lines and never logged. The flow runs over a scripted fake
/// relay (no sockets) or, end to end, over the real relay branches into the real MudServer on loopback. Network:
/// guard 1 (the loopback-only transport factory for the whole test run) and guard 2 (the real transports refuse a
/// non-loopback host) are proven here first.
/// </summary>
[Collection("SharedGameSingletons")]
public class LoginTokenClient128Tests : IDisposable
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;

    private const string HostA = "server-a.invalid";
    private const string HostB = "server-b.invalid";
    private const string Version = "1.2.8";
    private const string Menu = "\u001b[2J\u001b[H\r\n  Usurper Reborn\r\n  [L] Login  [R] Register  [Q] Quit\r\n  Choice: ";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"usurper-c3c-{Guid.NewGuid():N}");
    private readonly string _dir;
    private readonly string _legacyDir;
    private readonly Func<bool> _savedDoor;
    private readonly string _issued;
    private readonly long _issuedExp = DateTimeOffset.UtcNow.AddDays(90).ToUnixTimeSeconds();

    public LoginTokenClient128Tests()
    {
        _dir = Path.Combine(_root, "userdata");
        _legacyDir = Path.Combine(_root, "install");
        Directory.CreateDirectory(_dir);
        Directory.CreateDirectory(_legacyDir);
        _savedDoor = OnlineLoginStore.DoorModeCheck;
        OnlineLoginStore.DirectoryOverride = () => _dir;
        OnlineLoginStore.LegacyDirectoryOverride = () => _legacyDir;
        OnlineLoginStore.DoorModeCheck = () => false;
        _issued = NewToken();
    }

    public void Dispose()
    {
        OnlineLoginStore.DirectoryOverride = null;
        OnlineLoginStore.LegacyDirectoryOverride = null;
        OnlineLoginStore.DoorModeCheck = _savedDoor;
        try
        {
            if (!OperatingSystem.IsWindows() && Directory.Exists(_legacyDir))
                File.SetUnixFileMode(_legacyDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.Delete(_root, true);
        }
        catch { }
    }

    // ---------------- helpers ----------------

    private static string NewToken()
    {
        var t = LoginTokenProtocol.NewToken();
        return $"{t.Selector}.{t.Verifier}";
    }

    private static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));

    private static readonly Regex Ansi = new(@"\x1b\[[0-9;?]*[A-Za-z]", RegexOptions.Compiled);

    private static string L(string key, params object[] args) => Loc.Get(key, args);

    /// <summary>A scripted relay: each line written is recorded in the shared list and answered by the responder.</summary>
    internal sealed class FakeRelay : IOnlineAuthTransport
    {
        private readonly List<string> _sent;
        private readonly Func<string, string> _reply;
        private readonly Queue<string> _in = new();
        public FakeRelay(List<string> sent, Func<string, string> reply) { _sent = sent; _reply = reply; }
        public void Write(string text)
        {
            foreach (var line in text.Split('\n'))
            {
                if (line.Length == 0) continue;
                _sent.Add(line);
                var r = _reply(line);
                if (r.Length > 0) _in.Enqueue(r);
            }
        }
        public Task<string?> ReadAsync(TimeSpan timeout) => Task.FromResult(_in.Count > 0 ? _in.Dequeue() : null);
        public void Drain() => _in.Clear();
        public void Dispose() { }
    }

    /// <summary>A new relay and server: CAPS:token=1; the good token logs in; a good password logs in and, asked, issues a token.</summary>
    private Func<string, string> NewRelay(string goodPassword, string? goodToken = null, string? tokenReply = null) => line =>
    {
        if (line == LoginTokenProtocol.CapsLine) return LoginTokenProtocol.CapsReply + "\r\n" + Menu;
        if (LoginTokenProtocol.IsTokenLoginLine(line))
        {
            if (tokenReply != null) return tokenReply + "\r\n\r\n  ...\r\n\r\n" + Menu;
            return line.Split(' ')[1] == goodToken
                ? "OK\r\nWELCOME BACK\r\n"
                : LoginTokenProtocol.TokenRefusedPrefix + "Player doesn't exist or wrong password.\r\n\r\n  refused\r\n\r\n" + Menu;
        }
        if (line.StartsWith("AUTH:", StringComparison.Ordinal))
        {
            var parts = line.Split(':');
            if (parts.Length < 4 || parts[2] != goodPassword) return "ERR:Player doesn't exist or wrong password.\r\n\r\n" + Menu;
            string type = parts[3] == "REGISTER" ? parts[4] : parts[3];
            return "OK\r\n" + (type.Contains(";token=1") ? $"TOKEN:{_issued}:{_issuedExp}\r\n" : "") + "WELCOME\r\n";
        }
        return Menu;
    };

    /// <summary>A 1.0 to 1.2.7 relay: AUTH:CAPS and a token line are 1 part (menu redraw only); a password logs in, never a TOKEN line.</summary>
    private static Func<string, string> OldRelay(string goodPassword) => line =>
    {
        if (line.StartsWith("AUTH:", StringComparison.Ordinal) && line.Split(':').Length >= 4)
            return line.Split(':')[2] == goodPassword ? "OK\r\nWELCOME\r\n" : "ERR:Player doesn't exist or wrong password.\r\n\r\n" + Menu;
        return Menu;
    };

    private sealed class Run
    {
        public readonly List<string> Sent = new();
        public readonly MemoryStream Output = new();
        public int Reconnects;
        public OnlineAuthFlow Flow = null!;
        public OnlineAuthOutcome Outcome;
        public string Screen => Ansi.Replace(Encoding.UTF8.GetString(Output.ToArray()), "");
        public string AllSent => string.Join("\n", Sent);
    }

    private static async Task<Run> Play(Func<string, string> relay, IEnumerable<string> input, string host = HostA, int port = 4000, bool directTcp = false)
    {
        var run = new Run();
        var term = new TerminalEmulator(new LineStream(input.Concat(Enumerable.Repeat("", 6))), run.Output);
        run.Flow = new OnlineAuthFlow(term, new FakeRelay(run.Sent, relay), host, port, directTcp, "Local", Version, () =>
        {
            run.Reconnects++;
            return Task.FromResult<IOnlineAuthTransport?>(new FakeRelay(run.Sent, relay));
        });
        run.Outcome = await run.Flow.RunAsync();
        return run;
    }

    private void WriteLegacy(string server, int port, string user, string password, bool neverAsk = false) =>
        File.WriteAllText(Path.Combine(_legacyDir, OnlineLoginStore.LegacyFileName),
            $"{{\n  \"Server\": \"{server}\",\n  \"Port\": {port},\n  \"Username\": \"{user}\",\n  \"NeverAskToSave\": {(neverAsk ? "true" : "false")},\n  \"Password\": \"{B64(password)}\"\n}}");

    private string LegacyPath => Path.Combine(_legacyDir, OnlineLoginStore.LegacyFileName);

    /// <summary>Every byte of every file in the login directory and the install directory.</summary>
    private string AllFiles() =>
        string.Join("\n", Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).Select(File.ReadAllText));

    private static void Remember(string server, int port, string user, string token, int days = 90) =>
        OnlineLoginStore.SetToken(server, port, user, token, DateTime.UtcNow.AddDays(days));

    // ================= the two network guards, proven first =================

    [Fact]
    public async Task Guard1_TransportFactory_IsLoopbackOnly_ForTheWholeTestRun()
    {
        OnlineTransport.Factory.Should().BeSameAs(OnlineNoNetwork.LoopbackOnly, "the module initializer installs it");
        var started = DateTime.UtcNow;
        foreach (var host in new[] { "192.0.2.1", "play.usurper-reborn.net", "localhost" })
        {
            Func<Task> connect = () => OnlineTransport.Factory(host, 4000, false).WaitAsync(TimeSpan.FromSeconds(2));
            await connect.Should().ThrowAsync<InvalidOperationException>().WithMessage("*loopback*");
        }
        (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(1), "refused before any DNS or connect");

        var listener = TestLoopback.Listen();
        try
        {
            var accept = listener.AcceptTcpClientAsync();
            using var transport = await OnlineTransport.Factory("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port, false);
            using var peer = await accept;
            IPAddress.IsLoopback(((IPEndPoint)peer.Client.RemoteEndPoint!).Address).Should().BeTrue();
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task Row33_Guard2_RealTransports_RefuseANonLoopbackHost_BeforeConnecting()
    {
        OnlineTestGuard.Active.Should().BeTrue("the module initializer sets it");
        var started = DateTime.UtcNow;
        // TEST-NET-1 (reserved, never routed) and names (never resolved): the check runs before any DNS or connect
        foreach (var host in new[] { "192.0.2.1", "server-a.invalid" })
        {
            Func<Task> tcp = () => OnlineTransport.ConnectRealAsync(host, 4000, directTcp: true).WaitAsync(TimeSpan.FromSeconds(2));
            await tcp.Should().ThrowAsync<InvalidOperationException>().WithMessage("*loopback*");
            Func<Task> ssh = () => Task.Run(() => OnlineTransport.ConnectRealAsync(host, 4000, directTcp: false)).WaitAsync(TimeSpan.FromSeconds(2));
            await ssh.Should().ThrowAsync<InvalidOperationException>().WithMessage("*loopback*");
        }
        (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(1), "refused before any connect attempt");

        var listener = TestLoopback.Listen();
        try
        {
            var accept = listener.AcceptTcpClientAsync();
            using var transport = await OnlineTransport.ConnectRealAsync("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port, directTcp: true);
            using var peer = await accept;
            transport.Should().BeOfType<TcpAuthTransport>("a loopback address is allowed");
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public void Guard2_SitsBeforeTheConnect_InBothRealTransports()
    {
        var src = File.ReadAllText(Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Scripts", "Systems", "OnlinePlaySystem.cs"));
        int ssh = src.IndexOf("internal static SshAuthTransport Connect(", StringComparison.Ordinal);
        src.IndexOf("OnlineTestGuard.CheckHost(host);", ssh, StringComparison.Ordinal).Should()
            .BeLessThan(src.IndexOf("new SshClient(host", ssh, StringComparison.Ordinal));
        int tcp = src.IndexOf("internal static async Task<TcpAuthTransport> ConnectAsync(", StringComparison.Ordinal);
        src.IndexOf("OnlineTestGuard.CheckHost(host);", tcp, StringComparison.Ordinal).Should()
            .BeLessThan(src.IndexOf("client.ConnectAsync(host, port)", tcp, StringComparison.Ordinal));
        src.Should().Contain("var transport = await OnlineTransport.Factory(server, port, directTcp);",
            "the game opens every login connection through the factory");
    }

    // ================= row 23: the token only after CAPS:, password fallback on the same session =================

    [Fact]
    public async Task Row23_NewRelay_ProbeFirst_ThenTheToken_LogsIn()
    {
        var token = NewToken();
        Remember(HostA, 4000, "Wanderer", token);

        var run = await Play(NewRelay("pw", goodToken: token), Array.Empty<string>());

        run.Outcome.Should().Be(OnlineAuthOutcome.Authenticated, run.Screen);
        run.Sent.Should().Equal(LoginTokenProtocol.CapsLine, LoginTokenProtocol.FormatTokenLogin(token, "Local", Version));
        run.Flow.Leftover.Should().Be("WELCOME BACK\r\n");
        run.Reconnects.Should().Be(0);
    }

    [Theory]
    [InlineData("menu")]   // a 1.0 to 1.2.7 relay: only redraws its menu
    [InlineData("err")]    // a 1.2.7 server's static ERR (never echoed by a relay, but refused all the same)
    public async Task Row23_OldRelayOrServer_NeverGetsTheToken_PasswordOnTheSameSession(string kind)
    {
        var token = NewToken();
        Remember(HostA, 4000, "Wanderer", token);
        var old = OldRelay("pw");
        Func<string, string> relay = kind == "menu" ? old
            : line => line == LoginTokenProtocol.CapsLine ? "ERR:Invalid auth format. Expected AUTH:username:connectionType\r\n" + Menu : old(line);

        var run = await Play(relay, new[] { "L", "", "pw" });

        run.Outcome.Should().Be(OnlineAuthOutcome.Authenticated, run.Screen);
        run.AllSent.Should().NotContain(token).And.NotContain("TOKEN1");
        run.Sent.Should().Equal(LoginTokenProtocol.CapsLine, $"AUTH:Wanderer:pw:Local:{Version}");
        run.Reconnects.Should().Be(0, "the old relay is still at its menu: same session");
        run.Screen.Should().Contain(L("online.no_remember_server").Trim());
        OnlineLoginStore.Find(HostA, 4000)!.Token.Should().Be(token, "a server that cannot check it does not end the token");
    }

    /// <summary>A relay whose chunks reach the client at set times after the line that caused them, in order (an SSH link's pace).</summary>
    private sealed class TimedRelay : IOnlineAuthTransport
    {
        private readonly List<string> _sent;
        private readonly Func<string, (int DelayMs, string Text)[]> _reply;
        private readonly List<(DateTime At, string Text)> _in = new();
        public TimedRelay(List<string> sent, Func<string, (int, string)[]> reply) { _sent = sent; _reply = reply; }
        public void Write(string text)
        {
            var line = text.TrimEnd('\n');
            _sent.Add(line);
            var now = DateTime.UtcNow;
            lock (_in) foreach (var (d, t) in _reply(line)) _in.Add((now.AddMilliseconds(d), t));
        }
        public async Task<string?> ReadAsync(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                lock (_in)
                {
                    if (_in.Count > 0 && _in[0].At <= DateTime.UtcNow)
                    {
                        var t = _in[0].Text;
                        _in.RemoveAt(0);
                        return t;
                    }
                }
                if (DateTime.UtcNow >= deadline) return null;
                await Task.Delay(10);
            }
        }
        public void Drain() { lock (_in) while (_in.Count > 0 && _in[0].At <= DateTime.UtcNow) _in.RemoveAt(0); }
        public void Dispose() { }
    }

    [Fact]
    public async Task Row23_TheMenuTailArrivingLate_IsReadBeforeTheTokenIsSent()
    {
        var token = NewToken();
        Remember(HostA, 4000, "Wanderer", token);
        var sent = new List<string>();
        var relay = new TimedRelay(sent, line => line == LoginTokenProtocol.CapsLine
            ? new[] { (0, "CAPS:token=1\r\n\u001b[2J\u001b[H\r\n  Usurper Reborn\r\n"), (120, "  Choice: ") }
            : line == LoginTokenProtocol.FormatTokenLogin(token, "Local", Version)
                ? new[] { (0, "OK\r\nWELCOME BACK\r\n") }
                : new[] { (0, Menu) });
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(Enumerable.Repeat("", 6)), output);
        var flow = new OnlineAuthFlow(term, relay, HostA, 4000, false, "Local", Version, () => Task.FromResult<IOnlineAuthTransport?>(null));

        (await flow.RunAsync()).Should().Be(OnlineAuthOutcome.Authenticated, "the prompt is not glued to the OK line");
        sent.Should().Equal(LoginTokenProtocol.CapsLine, LoginTokenProtocol.FormatTokenLogin(token, "Local", Version));
        flow.Leftover.Should().Be("WELCOME BACK\r\n");
    }

    // ================= row 24: bound to its own server and port =================

    [Theory]
    [InlineData(HostB, 4000)]
    [InlineData(HostA, 4001)]
    public async Task Row24_TokenAndLegacyPassword_GoOnlyToTheirOwnServerAndPort(string host, int port)
    {
        var token = NewToken();
        Remember(HostA, 4000, "Wanderer", token);
        WriteLegacy(HostA, 4000, "Wanderer", "legacy-secret");

        var run = await Play(NewRelay("pw", goodToken: token), new[] { "L", "Other", "pw", "N" }, host, port);

        run.Outcome.Should().Be(OnlineAuthOutcome.Authenticated, run.Screen);
        run.AllSent.Should().NotContain(token).And.NotContain("legacy-secret").And.NotContain(B64("legacy-secret"));
        run.Sent.Should().Equal(LoginTokenProtocol.CapsLine, $"AUTH:Other:pw:Local:{Version}");
        File.ReadAllText(LegacyPath).Should().Contain(B64("legacy-secret"), "kept for its own server");
        OnlineLoginStore.Find(HostA, 4000)!.Token.Should().Be(token);
    }

    // ================= row 25: ;token=1 and the question only after CAPS: token=1 =================

    [Fact]
    public async Task Row25_OldRelay_NoRememberQuestion_NoTokenParam()
    {
        var run = await Play(OldRelay("pw"), new[] { "L", "Wanderer", "pw" });

        run.Outcome.Should().Be(OnlineAuthOutcome.Authenticated, run.Screen);
        run.Screen.Should().NotContain(L("online.remember_prompt").Trim());
        run.AllSent.Should().NotContain(";token=1");
        File.Exists(OnlineLoginStore.FilePath).Should().BeFalse("nothing to remember");
    }

    [Fact]
    public async Task Row25_NewRelay_AsksBeforeSending_AndOnlyYesAddsTheParam()
    {
        var yes = await Play(NewRelay("pw"), new[] { "L", "Wanderer", "pw", "Y" });
        yes.Screen.Should().Contain(L("online.remember_prompt").Trim());
        yes.Sent.Last().Should().Be($"AUTH:Wanderer:pw:Local;token=1:{Version}");

        var no = await Play(NewRelay("pw"), new[] { "L", "Other", "pw", "N" }, HostB);
        no.Sent.Last().Should().Be($"AUTH:Other:pw:Local:{Version}");

        var reg = await Play(NewRelay("newpw"), new[] { "R", "Newcomer", "newpw", "newpw", "Y" }, HostA, 4002);
        reg.Sent.Last().Should().Be("AUTH:Newcomer:newpw:REGISTER:Local;token=1");

        var never = await Play(NewRelay("pw"), new[] { "L", "Other", "pw", "D" }, HostB, 4003);
        never.Sent.Last().Should().Be($"AUTH:Other:pw:Local:{Version}");
        OnlineLoginStore.Load().NeverAskToSave.Should().BeTrue();
        var after = await Play(NewRelay("pw"), new[] { "L", "Other", "pw" }, HostB, 4003);
        after.Outcome.Should().Be(OnlineAuthOutcome.Authenticated, after.Screen);
        after.Screen.Should().NotContain(L("online.remember_prompt").Trim(), "D: never asked again");
        after.Sent.Should().NotContain(LoginTokenProtocol.CapsLine, "nothing to probe for");
    }

    // ================= row 26: the token is saved, never the password; the TOKEN line is never rendered =================

    [Fact]
    public async Task Row26_SavesTheToken_NeverThePassword_AndTheTokenLineIsNotRendered()
    {
        var run = await Play(NewRelay("hunter2-secret"), new[] { "L", "Wanderer", "hunter2-secret", "Y" });

        run.Outcome.Should().Be(OnlineAuthOutcome.Authenticated, run.Screen);
        var entry = OnlineLoginStore.Find(HostA, 4000)!;
        entry.Username.Should().Be("Wanderer");
        entry.Token.Should().Be(_issued);
        entry.ExpiresUtc.Should().Be(DateTimeOffset.FromUnixTimeSeconds(_issuedExp).UtcDateTime);
        var files = AllFiles();
        files.Should().Contain(_issued);
        files.Should().NotContain("hunter2-secret").And.NotContain(B64("hunter2-secret"));
        run.Flow.Leftover.Should().Be("WELCOME\r\n", "the TOKEN line is taken out before the session draws");
        run.Screen.Should().NotContain(_issued).And.NotContain("TOKEN:");
        run.Screen.Should().Contain(L("online.remembered").Trim());
    }

    [Fact]
    public async Task Row26_TokenLineArrivingLater_IsStillTaken_AndTokenDash_SavesTheUsernameOnly()
    {
        // OK alone first, the TOKEN line in the next chunk
        var sent = new List<string>();
        var parts = new Queue<string>(new[] { "OK\r\n", $"TOKEN:{_issued}:{_issuedExp}\r\nWELCOME\r\n" });
        Func<string, string> relay = line => line == LoginTokenProtocol.CapsLine ? LoginTokenProtocol.CapsReply + "\r\n" + Menu : "";
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(new[] { "L", "Wanderer", "pw", "Y", "", "" }), output);
        var fake = new SplitRelay(sent, relay, parts);
        var flow = new OnlineAuthFlow(term, fake, HostA, 4000, false, "Local", Version, () => Task.FromResult<IOnlineAuthTransport?>(null));
        (await flow.RunAsync()).Should().Be(OnlineAuthOutcome.Authenticated);
        OnlineLoginStore.Find(HostA, 4000)!.Token.Should().Be(_issued);
        flow.Leftover.Should().Be("WELCOME\r\n");

        // TOKEN:- : the server could not issue one
        Func<string, string> dash = line => line == LoginTokenProtocol.CapsLine ? LoginTokenProtocol.CapsReply + "\r\n" + Menu
            : line.StartsWith("AUTH:", StringComparison.Ordinal) ? "OK\r\nTOKEN:-\r\nWELCOME\r\n" : "";
        var run = await Play(dash, new[] { "L", "Other", "pw", "Y" }, HostB);
        run.Outcome.Should().Be(OnlineAuthOutcome.Authenticated);
        OnlineLoginStore.Find(HostB, 4000)!.Token.Should().BeNull();
        OnlineLoginStore.Find(HostB, 4000)!.Username.Should().Be("Other");
        run.Flow.Leftover.Should().Be("WELCOME\r\n");
        run.Screen.Should().Contain(L("online.remember_failed").Trim());
    }

    /// <summary>A relay whose AUTH reply arrives in the given chunks, one per read.</summary>
    private sealed class SplitRelay : IOnlineAuthTransport
    {
        private readonly List<string> _sent;
        private readonly Func<string, string> _reply;
        private readonly Queue<string> _parts;
        private readonly Queue<string> _in = new();
        public SplitRelay(List<string> sent, Func<string, string> reply, Queue<string> parts) { _sent = sent; _reply = reply; _parts = parts; }
        public void Write(string text)
        {
            var line = text.TrimEnd('\n');
            _sent.Add(line);
            var r = _reply(line);
            if (r.Length > 0) _in.Enqueue(r);
            else while (_parts.Count > 0) _in.Enqueue(_parts.Dequeue());
        }
        public Task<string?> ReadAsync(TimeSpan timeout) => Task.FromResult(_in.Count > 0 ? _in.Dequeue() : null);
        public void Drain() => _in.Clear();
        public void Dispose() { }
    }

    // ================= row 27: the file's place, mode and the door gate =================

    [Fact]
    public void Row27_File_IsInThePerUserDirectory_NotBesideTheExecutable()
    {
        OnlineLoginStore.DirectoryOverride = null;
        OnlineLoginStore.LegacyDirectoryOverride = null;
        try
        {
            OnlineLoginStore.FilePath.Should().Be(Path.Combine(UserDataPaths.Get(), "online_login.json"));
            Path.GetDirectoryName(OnlineLoginStore.FilePath).Should().NotBe(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
            OnlineLoginStore.LegacyPath.Should().Be(Path.Combine(AppContext.BaseDirectory, "online_credentials.json"));
        }
        finally
        {
            OnlineLoginStore.DirectoryOverride = () => _dir;
            OnlineLoginStore.LegacyDirectoryOverride = () => _legacyDir;
        }
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            UserDataPaths.Get().Should().Be(Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "/tmp", ".local", "share", "UsurperReloaded"),
                "the saves' directory since before 1.2.8: no save moves");
    }

    [Fact]
    public void Row27_File_IsWritten0600_ThenMoved_NoTemporaryLeft()
    {
        Remember(HostA, 4000, "Wanderer", NewToken());
        File.Exists(OnlineLoginStore.FilePath).Should().BeTrue();
        if (!OperatingSystem.IsWindows())
            File.GetUnixFileMode(OnlineLoginStore.FilePath).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Directory.EnumerateFiles(_dir).Select(Path.GetFileName).Should().Equal("online_login.json");
        var src = File.ReadAllText(Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Scripts", "Systems", "OnlineLoginStore.cs"));
        src.Should().Contain("options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;")
            .And.Contain("File.Move(tmp, path, overwrite: true);");
    }

    [Fact]
    public async Task Row27_DoorMode_NeverReadsOrWrites_AndNeverProbes()
    {
        var token = NewToken();
        Remember(HostA, 4000, "Wanderer", token);
        WriteLegacy(HostA, 4000, "Wanderer", "legacy-secret");
        var before = File.ReadAllText(OnlineLoginStore.FilePath);

        OnlineLoginStore.DoorModeCheck = () => true;
        OnlineLoginStore.Load().Logins.Should().BeEmpty("door mode reads nothing");
        OnlineLoginStore.PrepareLegacy().Should().BeNull();
        Remember(HostA, 4000, "Someone", NewToken());
        OnlineLoginStore.Save(new OnlineLoginFile()).Should().BeFalse();
        File.ReadAllText(OnlineLoginStore.FilePath).Should().Be(before, "door mode writes nothing");
        File.ReadAllText(LegacyPath).Should().Contain(B64("legacy-secret"), "door mode does not touch the old file either");

        // the BBS door itself (direct TCP): no probe, no token, no saved password, no question
        OnlineLoginStore.DoorModeCheck = () => false;
        var bbs = await Play(NewRelay("pw", goodToken: token), new[] { "L", "Wanderer", "pw" }, directTcp: true);
        bbs.Outcome.Should().Be(OnlineAuthOutcome.Authenticated, bbs.Screen);
        bbs.Sent.Should().Equal($"AUTH:Wanderer:pw:Local:{Version}");
        bbs.Screen.Should().NotContain(L("online.remember_prompt").Trim());
    }

    // ================= rows 28-30: conversion of the old file =================

    [Fact]
    public async Task Row28_Conversion_NewServer_TokenStored_LegacyFileDeleted_PasswordNowhere()
    {
        WriteLegacy(HostA, 4000, "Wanderer", "legacy-secret");

        var run = await Play(NewRelay("legacy-secret"), Array.Empty<string>());

        run.Outcome.Should().Be(OnlineAuthOutcome.Authenticated, run.Screen);
        run.Sent.Should().Equal(LoginTokenProtocol.CapsLine, $"AUTH:Wanderer:legacy-secret:Local;token=1:{Version}");
        File.Exists(LegacyPath).Should().BeFalse("the old file is gone");
        OnlineLoginStore.Find(HostA, 4000)!.Token.Should().Be(_issued);
        var files = AllFiles();
        files.Should().NotContain("legacy-secret").And.NotContain(B64("legacy-secret"));
        run.Flow.Leftover.Should().Be("WELCOME\r\n");
    }

    [Fact]
    public async Task Row29_Conversion_OldServer_LoginWorks_PasswordErased_UsernameKept_NoticeShown()
    {
        WriteLegacy(HostA, 4000, "Wanderer", "legacy-secret");

        var run = await Play(OldRelay("legacy-secret"), Array.Empty<string>());

        run.Outcome.Should().Be(OnlineAuthOutcome.Authenticated, run.Screen);
        run.Sent.Should().Equal(LoginTokenProtocol.CapsLine, $"AUTH:Wanderer:legacy-secret:Local:{Version}");
        File.Exists(LegacyPath).Should().BeFalse();
        var entry = OnlineLoginStore.Find(HostA, 4000)!;
        entry.Username.Should().Be("Wanderer");
        entry.Token.Should().BeNull();
        AllFiles().Should().NotContain("legacy-secret").And.NotContain(B64("legacy-secret"));
        run.Screen.Should().Contain(L("online.no_remember_server").Trim());

        // next time the username is the prompt's default
        var next = await Play(OldRelay("pw"), new[] { "L", "", "pw" });
        next.Sent.Last().Should().Be($"AUTH:Wanderer:pw:Local:{Version}");
    }

    [Fact]
    public async Task Row30_Conversion_AfterErr_PasswordErased_UndeletableFileOverwritten_NeverAskCarried()
    {
        WriteLegacy(HostA, 4000, "Wanderer", "legacy-secret", neverAsk: true);
        bool readOnly = !OperatingSystem.IsWindows();
        if (readOnly) File.SetUnixFileMode(_legacyDir, UnixFileMode.UserRead | UnixFileMode.UserExecute); // the file cannot be deleted

        var run = await Play(NewRelay("changed-since"), new[] { "Q" });

        run.Sent.Should().Equal(LoginTokenProtocol.CapsLine, $"AUTH:Wanderer:legacy-secret:Local;token=1:{Version}");
        run.Screen.Should().Contain(L("online.saved_login_failed", "Player doesn't exist or wrong password."));
        run.Reconnects.Should().Be(1);
        var legacy = File.ReadAllText(LegacyPath);
        if (readOnly) legacy.Should().Be("{\"NeverAskToSave\":true}", "an undeletable file is overwritten without the password");
        AllFiles().Should().NotContain("legacy-secret").And.NotContain(B64("legacy-secret"));
        var file = OnlineLoginStore.Load();
        file.NeverAskToSave.Should().BeTrue("carried from the old file");
        OnlineLoginStore.Find(file, HostA, 4000)!.Username.Should().Be("Wanderer");

        // the overwritten file is read as nothing to convert from then on
        var again = await Play(NewRelay("pw"), new[] { "L", "", "pw" });
        again.Sent.Should().NotContain(s => s.Contains("legacy-secret"));
    }

    // ================= row 31: a refused token =================

    [Fact]
    public async Task Row31_RefusedToken_Cleared_UsernamePrefilled_PasswordAsked_RememberOfferedAgain()
    {
        var stale = NewToken();
        Remember(HostA, 4000, "Wanderer", stale);

        var run = await Play(NewRelay("pw", goodToken: "something-else"), new[] { "L", "", "pw", "Y" });

        run.Outcome.Should().Be(OnlineAuthOutcome.Authenticated, run.Screen);
        run.Screen.Should().Contain(L("online.token_refused").Trim());
        run.Reconnects.Should().Be(1, "as after any ERR: a fresh connection");
        run.Sent.Last().Should().Be($"AUTH:Wanderer:pw:Local;token=1:{Version}", "username prefilled, remember offered again");
        OnlineLoginStore.Find(HostA, 4000)!.Token.Should().Be(_issued);
        run.AllSent.Split('\n').Count(l => l.Contains(stale)).Should().Be(1, "the refused token is sent once and then forgotten");
    }

    [Theory]
    [InlineData("ERR:Too many failed logins. Try again in 30 seconds.")]
    [InlineData("ERR:Your account has been banned.")]
    public async Task Row31_ThrottleOrBanReply_KeepsTheToken(string reply)
    {
        var token = NewToken();
        Remember(HostA, 4000, "Wanderer", token);

        var run = await Play(NewRelay("pw", goodToken: token, tokenReply: reply), new[] { "Q" });

        run.Screen.Should().Contain(reply.Substring(4));
        run.Screen.Should().NotContain(L("online.token_refused").Trim());
        OnlineLoginStore.Find(HostA, 4000)!.Token.Should().Be(token, "only the refused mark ends a token (ruling 2026-10-07)");
    }

    // ================= row 32: exact matching, nothing secret in the client log =================

    [Theory]
    [InlineData("AUTH:Wanderer:OK:Local:1.2.8\r\n")]
    [InlineData("NOT OK\r\nOKAY\r\nok\r\n")]
    [InlineData("  the server said OK\r\n")]
    [InlineData("OK")]  // no line end yet
    public void Row32_OnlyAWholeOkLineMatches(string text) =>
        OnlineAuthFlow.MatchReply(text, complete: false).Should().BeNull();

    [Fact]
    public void Row32_MatchReply_LeftoverStartsAfterTheMatchedLine_AndErrMarksAreRead()
    {
        var ok = OnlineAuthFlow.MatchReply("echo says OK here\r\nOK\r\nTOKEN:x\r\nrest", complete: false)!;
        ok.Kind.Should().Be(OnlineAuthReply.ReplyKind.Ok);
        ok.Leftover.Should().Be("TOKEN:x\r\nrest");
        OnlineAuthFlow.MatchReply("OK", complete: true)!.Kind.Should().Be(OnlineAuthReply.ReplyKind.Ok);

        var refused = OnlineAuthFlow.MatchReply(LoginTokenProtocol.TokenRefusedPrefix + "Nope.\r\n", complete: false)!;
        refused.Kind.Should().Be(OnlineAuthReply.ReplyKind.Err);
        refused.TokenRefused.Should().BeTrue();
        refused.ErrText.Should().Be("Nope.");
        var plain = OnlineAuthFlow.MatchReply("ERR:Too many failed logins.\r\n", complete: false)!;
        plain.TokenRefused.Should().BeFalse();
        plain.ErrText.Should().Be("Too many failed logins.");
        // a localized text that happens to read like the mark's words is not the mark
        OnlineAuthFlow.MatchReply("ERR:token=refused\r\n", complete: false)!.TokenRefused.Should().BeFalse();
    }

    [Fact]
    public async Task Row32_AnEchoedOk_IsNotALogin_AndTheClientLogHoldsNoSecret()
    {
        using var log = new LoginToken128Tests.LogCapture();
        // a relay that only echoes the line back: the password is "OK"
        Func<string, string> echo = line => line == LoginTokenProtocol.CapsLine ? LoginTokenProtocol.CapsReply + "\r\n" + Menu : line + "\r\n";
        var run = await Play(echo, new[] { "L", "Wanderer", "OK", "Y" });
        run.Outcome.Should().Be(OnlineAuthOutcome.Failed, "an echoed line holding OK is not the OK line");
        run.Screen.Should().Contain(L("online.unexpected_response").Trim());

        // every other path, with secrets in play
        var token = NewToken();
        Remember(HostA, 4001, "Wanderer", token);
        await Play(NewRelay("pw", goodToken: token), Array.Empty<string>(), HostA, 4001);
        await Play(NewRelay("hunter2-secret"), new[] { "L", "Wanderer", "hunter2-secret", "Y" }, HostB);
        WriteLegacy(HostA, 4002, "Wanderer", "legacy-secret");
        await Play(NewRelay("wrong"), new[] { "Q" }, HostA, 4002);

        var all = log.All;
        all.Should().Contain("Unexpected auth response (", "the byte count only");
        all.Should().NotContain("AUTH:").And.NotContain("TOKEN:").And.NotContain(token).And.NotContain(_issued)
            .And.NotContain("hunter2-secret").And.NotContain("legacy-secret").And.NotContain(B64("legacy-secret"));
    }

    // ================= row 31 and 28 end to end: the real relay branches into the real MudServer =================

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"usurper-c3c-{Guid.NewGuid():N}.db");

    /// <summary>The real MudServer on a loopback listener; every connection goes to HandleConnectionAsync, login replies only.</summary>
    private sealed class LoopbackMud : IDisposable
    {
        public readonly TcpListener Listener = TestLoopback.Listen();
        public readonly SqlSaveBackend Db;
        private readonly CancellationTokenSource _cts = new();
        public int Port => ((IPEndPoint)Listener.LocalEndpoint).Port;

        public LoopbackMud(SqlSaveBackend db)
        {
            Db = db;
            var server = (MudServer)RuntimeHelpers.GetUninitializedObject(typeof(MudServer));
            typeof(MudServer).GetField("<ActiveSessions>k__BackingField", NP)!
                .SetValue(server, new ConcurrentDictionary<string, PlayerSession>());
            server.StopAfterLoginReply = true;
            var handle = typeof(MudServer).GetMethod("HandleConnectionAsync", NP)!;
            _ = Task.Run(async () =>
            {
                while (!_cts.IsCancellationRequested)
                {
                    TcpClient c;
                    try { c = await Listener.AcceptTcpClientAsync(_cts.Token); }
                    catch { break; }
                    _ = (Task)handle.Invoke(server, new object[] { c, Db, _cts.Token })!;
                }
            });
        }

        public void Dispose()
        {
            _cts.Cancel();
            Listener.Stop();
        }
    }

    /// <summary>A stdout for the relay that hands every byte to the transport.</summary>
    private sealed class Capture : Stream
    {
        private readonly Action<string> _sink;
        public Capture(Action<string> sink) => _sink = sink;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;
        public override long Position { get => 0; set { } }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => _sink(Encoding.UTF8.GetString(buffer, offset, count));
    }

    private sealed class EndlessStdin : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 0;
        public override long Position { get => 0; set { } }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) { Thread.Sleep(Timeout.Infinite); return 0; }
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>
    /// sshd plus the relay's passthrough loop, in process: each line the client writes goes through the real
    /// RelayClient branches (TokenPassthrough, then ParseDirectAuth and ConnectAndBridge) to the real MudServer on
    /// loopback; after a refusal the relay's menu is drawn again, as RunAsync's loop does.
    /// </summary>
    private sealed class RelayLoopback : IOnlineAuthTransport
    {
        private readonly int _port;
        private readonly List<string> _sent;
        private readonly StringBuilder _in = new();
        public RelayLoopback(int port, List<string> sent) { _port = port; _sent = sent; Append(Menu); }

        private void Append(string s) { lock (_in) _in.Append(s); }

        public void Write(string text)
        {
            foreach (var line in text.Split('\n'))
            {
                if (line.Length == 0) continue;
                _sent.Add(line);
                Task.Run(() => Handle(line)).GetAwaiter().GetResult();
            }
        }

        private async Task Handle(string line)
        {
            var savedOut = RelayClient.OpenStdout;
            var savedIn = RelayClient.OpenStdin;
            RelayClient.OpenStdout = () => new Capture(Append);
            RelayClient.OpenStdin = () => new EndlessStdin();
            try
            {
                bool? done = await RelayClient.TokenPassthrough(line, _port, "en", CancellationToken.None);
                if (done == null)
                {
                    var auth = RelayClient.ParseDirectAuth(line);
                    done = false;
                    if (auth.HasValue)
                    {
                        var bridge = typeof(RelayClient).GetMethod("ConnectAndBridge", SNP)!;
                        done = await (Task<bool>)bridge.Invoke(null, new object[] {
                            auth.Value.username, auth.Value.password, _port, auth.Value.connectionType,
                            CancellationToken.None, auth.Value.isRegistration, false, "en" })!;
                    }
                }
                if (done == false) Append(Menu);
            }
            finally
            {
                RelayClient.OpenStdout = savedOut;
                RelayClient.OpenStdin = savedIn;
            }
        }

        public Task<string?> ReadAsync(TimeSpan timeout)
        {
            lock (_in)
            {
                if (_in.Length == 0) return Task.FromResult<string?>(null);
                var s = _in.ToString();
                _in.Clear();
                return Task.FromResult<string?>(s);
            }
        }

        public void Drain() { lock (_in) _in.Clear(); }
        public void Dispose() { }
    }

    private static ConcurrentDictionary<string, (int Fails, DateTime LastFail, DateTime LockUntil)> FailedLogins() =>
        (ConcurrentDictionary<string, (int, DateTime, DateTime)>)typeof(MudServer).GetField("_failedLogins", SNP)!.GetValue(null)!;

    private async Task<(Run Run, int Connections)> PlayEndToEnd(LoopbackMud mud, IEnumerable<string> input)
    {
        var run = new Run();
        var term = new TerminalEmulator(new LineStream(input.Concat(Enumerable.Repeat("", 6))), run.Output);
        // as ConnectAndPlay does: the relay's first menu is drained before the flow starts, and after a reconnect
        var first = new RelayLoopback(mud.Port, run.Sent);
        first.Drain();
        run.Flow = new OnlineAuthFlow(term, first, HostA, 4000, false, "Local", Version, () =>
        {
            run.Reconnects++;
            var next = new RelayLoopback(mud.Port, run.Sent);
            next.Drain();
            return Task.FromResult<IOnlineAuthTransport?>(next);
        });
        run.Outcome = await run.Flow.RunAsync();
        return (run, run.Reconnects);
    }

    private async Task WithEndToEnd(Func<LoopbackMud, Task> body)
    {
        const string ip = "198.51.100.181";
        var savedSsh = Environment.GetEnvironmentVariable("SSH_CLIENT");
        Environment.SetEnvironmentVariable("SSH_CLIENT", $"{ip} 50000 4000"); // the relay forwards it as X-IP
        FailedLogins().TryRemove(ip, out _);
        var db = new SqlSaveBackend(_dbPath);
        try
        {
            using var mud = new LoopbackMud(db);
            RelayClient.MudHost.Should().Be("127.0.0.1", "the relay's own loopback seam");
            await body(mud);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SSH_CLIENT", savedSsh);
            FailedLogins().TryRemove(ip, out _);
            SqliteConnection.ClearAllPools();
            try { File.Delete(_dbPath); } catch { }
        }
    }

    private long TokenRows(string user)
    {
        using var c = new SqliteConnection($"Data Source={_dbPath}");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM login_tokens WHERE username = @u;";
        cmd.Parameters.AddWithValue("@u", user.ToLowerInvariant());
        return (long)cmd.ExecuteScalar()!;
    }

    [Fact]
    public async Task Row31_EndToEnd_Issue_ChangePassword_TokenRefused_PasswordLoginRemembersAgain()
    {
        await WithEndToEnd(async mud =>
        {
            (await mud.Db.RegisterPlayer("Far Traveler", "firstpw")).success.Should().BeTrue();

            var (first, _) = await PlayEndToEnd(mud, new[] { "L", "Far Traveler", "firstpw", "Y" });
            first.Outcome.Should().Be(OnlineAuthOutcome.Authenticated, first.Screen);
            first.Sent.Should().Equal(LoginTokenProtocol.CapsLine, $"AUTH:Far Traveler:firstpw:Local;token=1:{Version}");
            var issued = OnlineLoginStore.Find(HostA, 4000)!.Token;
            issued.Should().NotBeNull(first.Screen);
            TokenRows("Far Traveler").Should().Be(1);
            first.Flow.Leftover.Should().BeNull("the TOKEN line is taken; the test server stops after the login replies");

            var (again, _) = await PlayEndToEnd(mud, Array.Empty<string>());
            again.Outcome.Should().Be(OnlineAuthOutcome.Authenticated, again.Screen);
            again.Sent.Should().Equal(LoginTokenProtocol.CapsLine, LoginTokenProtocol.FormatTokenLogin(issued!, "Local", Version));

            (await mud.Db.ChangePassword("Far Traveler", "firstpw", "secondpw")).success.Should().BeTrue();

            var (third, reconnects) = await PlayEndToEnd(mud, new[] { "L", "", "secondpw", "Y" });
            third.Outcome.Should().Be(OnlineAuthOutcome.Authenticated, third.Screen);
            third.Screen.Should().Contain(L("online.token_refused").Trim());
            reconnects.Should().Be(1);
            third.Sent.Last().Should().Be($"AUTH:Far Traveler:secondpw:Local;token=1:{Version}");
            var renewed = OnlineLoginStore.Find(HostA, 4000)!.Token;
            renewed.Should().NotBeNull().And.NotBe(issued);
            TokenRows("Far Traveler").Should().Be(1);
            AllFiles().Should().NotContain("firstpw").And.NotContain("secondpw");
        });
    }

    [Fact]
    public async Task Row28_EndToEnd_LegacyFile_ConvertedAgainstTheRealServer()
    {
        await WithEndToEnd(async mud =>
        {
            (await mud.Db.RegisterPlayer("Old Hand", "legacy-secret")).success.Should().BeTrue();
            WriteLegacy(HostA, 4000, "Old Hand", "legacy-secret");

            var (run, _) = await PlayEndToEnd(mud, Array.Empty<string>());

            run.Outcome.Should().Be(OnlineAuthOutcome.Authenticated, run.Screen);
            File.Exists(LegacyPath).Should().BeFalse();
            OnlineLoginStore.Find(HostA, 4000)!.Token.Should().NotBeNull();
            TokenRows("Old Hand").Should().Be(1);
            AllFiles().Should().NotContain("legacy-secret").And.NotContain(B64("legacy-secret"));
        });
    }

    // ================= the server's refused mark (supervisor ruling, 2026-10-07) =================

    [Fact]
    public void ServerMark_OnlyTheNoGoodTokenReasons_CarryIt()
    {
        foreach (var r in new[] { SqlSaveBackend.LoginFailure.TokenMalformed, SqlSaveBackend.LoginFailure.TokenUnknown,
                     SqlSaveBackend.LoginFailure.TokenMismatch, SqlSaveBackend.LoginFailure.TokenExpired, SqlSaveBackend.LoginFailure.TokenStale })
            MudServer.TokenIsNoGood(r).Should().BeTrue(r.ToString());
        foreach (var r in new[] { SqlSaveBackend.LoginFailure.Banned, SqlSaveBackend.LoginFailure.IpBanned, SqlSaveBackend.LoginFailure.Error })
            MudServer.TokenIsNoGood(r).Should().BeFalse(r.ToString());
        LoginTokenProtocol.TokenRefusedLine("x").Should().Be("ERR:[token=refused] x");
    }

    // ================= row 35: the new text =================

    private static readonly string[] NewKeys =
        { "online.remember_prompt", "online.token_refused", "online.no_remember_server", "online.remember_failed", "online.remembered" };

    [Fact]
    public void Row35_NewKeys_InFiveLanguages_RealTranslations_Within79InEnglishAndHungarian()
    {
        foreach (var key in NewKeys)
        {
            foreach (var lang in new[] { "en", "es", "fr", "hu", "it" })
            {
                Loc.HasIn(lang, key).Should().BeTrue($"{lang} {key}");
                Loc.GetIn(lang, key).Trim().Should().NotBeEmpty();
                if (lang != "en") Loc.GetIn(lang, key).Should().NotBe(Loc.GetIn("en", key), $"{lang} {key} is translated");
            }
            foreach (var lang in new[] { "en", "hu" })
            {
                // the prompt is drawn after two spaces; every other row carries its own indent
                string row = key == "online.remember_prompt" ? "  " + Loc.GetIn(lang, key) + "D" : Loc.GetIn(lang, key);
                row.Length.Should().BeLessThanOrEqualTo(79, $"{lang} {key}: {row}");
            }
        }
        foreach (var gone in new[] { "online.save_credentials_prompt", "online.credentials_saved", "online.authenticated_prompt" })
            Loc.HasIn("en", gone).Should().BeFalse($"{gone} has no use left");
    }
}
