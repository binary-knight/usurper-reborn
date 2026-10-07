using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Server;
using UsurperReborn.Tests.Localization;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.8: the SSH relay's new passthrough branches (design C3 rows 5, 9, 34 and the relay half of 21). AUTH:CAPS
/// is forwarded once and its CAPS: reply echoed for the programmatic client only; a well formed AUTH:TOKEN1 line
/// is forwarded unchanged with the OK and ERR handling of a password line; after OK the TOKEN line is bridged and
/// never logged; every other shape goes to ParseDirectAuth as before. The relay connects only to loopback: a
/// host seam that defaults to 127.0.0.1 and a hard check that refuses any other host before connecting, both
/// proven here. Each test runs against a fake MUD on a loopback listener (TestLoopback).
/// </summary>
[Collection("SharedGameSingletons")]
public class RelayToken128Tests
{
    /// <summary>A stdin that never ends until the bridge cancels it, so the bridge ends when the fake MUD closes.</summary>
    private sealed class BlockingStdin : Stream
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

    private sealed class FakeMud : IDisposable
    {
        public readonly TcpListener Listener = TestLoopback.Listen();
        public int Port => ((IPEndPoint)Listener.LocalEndpoint).Port;
        public readonly List<string> Lines = new();
        public IPAddress? Peer;
        public Task Served = Task.CompletedTask;

        /// <summary>Accept one connection, read lines up to the AUTH line, write <paramref name="reply"/>, close.</summary>
        public FakeMud Serve(string reply)
        {
            Served = Task.Run(async () =>
            {
                using var conn = await Listener.AcceptTcpClientAsync();
                Peer = ((IPEndPoint)conn.Client.RemoteEndPoint!).Address;
                var stream = conn.GetStream();
                var reader = new StreamReader(stream, Encoding.UTF8);
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    lock (Lines) Lines.Add(line);
                    if (line.StartsWith("AUTH:", StringComparison.Ordinal)) break;
                }
                var bytes = Encoding.UTF8.GetBytes(reply);
                await stream.WriteAsync(bytes, 0, bytes.Length);
                await stream.FlushAsync();
                conn.Close();
            });
            return this;
        }

        public void Dispose() => Listener.Stop();
    }

    /// <summary>Run <paramref name="relay"/> with stdout, stdin and stderr captured. Returns stdout and stderr.</summary>
    private static async Task<(string Stdout, string Stderr, T Result)> Captured<T>(Func<Task<T>> relay)
    {
        var captured = new MemoryStream();
        var savedOut = RelayClient.OpenStdout;
        var savedIn = RelayClient.OpenStdin;
        var oldErr = Console.Error;
        var tap = new LoginToken128Tests.StderrTap(oldErr);
        RelayClient.OpenStdout = () => captured;
        RelayClient.OpenStdin = () => new BlockingStdin();
        Console.SetError(tap);
        T result;
        try
        {
            result = await relay().WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            Console.SetError(oldErr);
            RelayClient.OpenStdout = savedOut;
            RelayClient.OpenStdin = savedIn;
        }
        return (Encoding.UTF8.GetString(captured.ToArray()), tap.Seen, result);
    }

    private static Task<bool> ConnectAndBridge(string user, string password, int port, string type, bool rawTerminal)
    {
        var method = typeof(RelayClient).GetMethod("ConnectAndBridge", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (Task<bool>)method.Invoke(null, new object[] { user, password, port, type, CancellationToken.None, false, rawTerminal, "en" })!;
    }

    private static (string Line, string Token, string Verifier) TokenLine()
    {
        var t = LoginTokenProtocol.NewToken();
        string token = $"{t.Selector}.{t.Verifier}";
        return (LoginTokenProtocol.FormatTokenLogin(token, "Steam", "1.2.8"), token, t.Verifier);
    }

    private static string Src(string file) =>
        File.ReadAllText(Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Scripts", "Server", file));

    // ================= the relay's two loopback guards, proven first =================

    [Fact]
    public async Task RelayGuard1_TheHostSeam_DefaultsToLoopback_AndTheFakeMudSeesALoopbackPeer()
    {
        RelayClient.MudHost.Should().Be("127.0.0.1");
        IPAddress.IsLoopback(RelayClient.LoopbackOnly(RelayClient.MudHost)).Should().BeTrue();
        using var mud = new FakeMud().Serve("CAPS:token=1\n");
        await Captured(() => RelayClient.ProbeCapabilities(mud.Port, CancellationToken.None, rawTerminal: false));
        await mud.Served.WaitAsync(TimeSpan.FromSeconds(10));
        IPAddress.IsLoopback(mud.Peer!).Should().BeTrue();
    }

    [Fact]
    public async Task RelayGuard2_ANonLoopbackHost_IsRefusedBeforeConnecting()
    {
        foreach (var host in new[] { "192.0.2.1", "localhost", "example.invalid", "0.0.0.0", "" })
        {
            Action guard = () => RelayClient.LoopbackOnly(host);
            guard.Should().Throw<InvalidOperationException>($"{host} is not a loopback IP");
        }
        var saved = RelayClient.MudHost;
        RelayClient.MudHost = "192.0.2.1"; // TEST-NET-1: reserved, never routed
        try
        {
            var started = DateTime.UtcNow;
            Func<Task> probe = () => RelayClient.ProbeCapabilities(4000, CancellationToken.None, rawTerminal: false);
            await probe.Should().ThrowAsync<InvalidOperationException>();
            var (line, _, _) = TokenLine();
            Func<Task> forward = () => RelayClient.TokenPassthrough(line, 4000, "en", CancellationToken.None);
            await forward.Should().ThrowAsync<InvalidOperationException>();
            Func<Task> password = () => ConnectAndBridge("tester", "secret", 4000, "Steam", rawTerminal: false);
            await password.Should().ThrowAsync<InvalidOperationException>();
            (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(1), "refused before any connect attempt");
        }
        finally { RelayClient.MudHost = saved; }
    }

    // ================= row 34: every relay connect goes through the guard =================

    [Fact]
    public void Row34_EveryRelayConnect_UsesTheLoopbackGuard()
    {
        var src = Src("RelayClient.cs");
        src.Should().Contain("internal static string MudHost = \"127.0.0.1\";");
        int connects = Regex.Matches(src, @"\.ConnectAsync\(").Count;
        connects.Should().BeGreaterThanOrEqualTo(2, "the password or token connect and the probe");
        Regex.Matches(src, @"\.ConnectAsync\(LoopbackOnly\(MudHost\), port, ").Count.Should().Be(connects,
            "every connect goes through LoopbackOnly(MudHost)");
        src.Should().Contain("System.Net.IPAddress.TryParse(host, out var ip) && System.Net.IPAddress.IsLoopback(ip)");
    }

    // ================= row 5: the probe =================

    [Fact]
    public async Task Row05_Probe_IsForwardedOnce_AndItsReplyEchoedOnThePassthrough()
    {
        using var mud = new FakeMud().Serve("CAPS:token=1\n");
        var (stdout, stderr, result) = await Captured(() => RelayClient.TokenPassthrough("AUTH:CAPS", mud.Port, "en", CancellationToken.None));
        await mud.Served.WaitAsync(TimeSpan.FromSeconds(10));
        result.Should().Be(false, "the relay shows its menu again after the probe");
        mud.Lines.Last().Should().Be("AUTH:CAPS");
        mud.Lines.Count(l => l.StartsWith("AUTH:")).Should().Be(1);
        stdout.Should().Be("CAPS:token=1\r\n");
        stderr.Should().NotContain("Invalid AUTH format");
    }

    [Fact]
    public async Task Row05_Probe_OnTheRawTerminalPath_EchoesNothing()
    {
        using var mud = new FakeMud().Serve("CAPS:token=1\n");
        var (stdout, _, _) = await Captured(() => RelayClient.ProbeCapabilities(mud.Port, CancellationToken.None, rawTerminal: true));
        await mud.Served.WaitAsync(TimeSpan.FromSeconds(10));
        mud.Lines.Last().Should().Be("AUTH:CAPS", "forwarded");
        stdout.Should().BeEmpty("a person typing never sees protocol lines");
    }

    [Fact]
    public async Task Row05_Probe_AReplyThatIsNotCaps_IsNotEchoed()
    {
        using var mud = new FakeMud().Serve("\r\n  Connection refused: this address is banned from the server.\r\n");
        var (stdout, _, _) = await Captured(() => RelayClient.TokenPassthrough("AUTH:CAPS", mud.Port, "en", CancellationToken.None));
        stdout.Should().BeEmpty("no capability: the client reads none");
    }

    // ================= the token login forward =================

    [Fact]
    public async Task TokenLine_IsForwardedUnchanged_AndOkHandledAsForAPassword()
    {
        var (line, token, verifier) = TokenLine();
        using var mud = new FakeMud().Serve("OK\n");
        var (stdout, stderr, result) = await Captured(() => RelayClient.TokenPassthrough(line, mud.Port, "en", CancellationToken.None));
        await mud.Served.WaitAsync(TimeSpan.FromSeconds(10));
        mud.Lines.Last().Should().Be(line, "the relay forwards the token line byte for byte");
        result.Should().Be(true, "a session ran");
        stdout.Should().Be("OK\r\n");
        stderr.Should().Contain("Connected as '(token)'");
        stderr.Should().NotContain(verifier).And.NotContain(token).And.NotContain("TOKEN1");
    }

    [Fact]
    public async Task TokenLine_Refused_EchoesTheServersErr_AndLogsOnlyIt()
    {
        var (line, token, verifier) = TokenLine();
        using var mud = new FakeMud().Serve("ERR:Invalid username or password.\n");
        var (stdout, stderr, result) = await Captured(() => RelayClient.TokenPassthrough(line, mud.Port, "en", CancellationToken.None));
        result.Should().Be(false);
        stdout.Should().StartWith("ERR:Invalid username or password.\r\n");
        stderr.Should().Contain("[RELAY] Auth failed: ERR:Invalid username or password.");
        stderr.Should().NotContain(verifier).And.NotContain(token);
    }

    [Theory]
    [InlineData("AUTH:TOKEN1 ")]
    [InlineData("AUTH:TOKEN1 abc.def Steam")]
    [InlineData("AUTH:TOKEN1 {0} Ste/am 1.2.8")]
    [InlineData("AUTH:TOKEN1 {0} Steam 1.2.8 more")]
    [InlineData("AUTH:TOKEN1 {0}")]
    public async Task MalformedTokenLine_LogsOnlyThatItWasAToken_AndConnectsNowhere(string shape)
    {
        var (_, token, verifier) = TokenLine();
        var line = string.Format(shape, token);
        using var mud = new FakeMud();
        var (stdout, stderr, result) = await Captured(() => RelayClient.TokenPassthrough(line, mud.Port, "en", CancellationToken.None));
        result.Should().Be(false);
        stdout.Should().BeEmpty();
        stderr.Should().Contain("[RELAY] Invalid AUTH format: token");
        stderr.Should().NotContain(verifier);
        mud.Listener.Pending().Should().BeFalse("nothing was forwarded");
    }

    [Theory]
    [InlineData("AUTH:Rage:hunter2:Steam:1.0.0")]
    [InlineData("AUTH:NewGuy:pw:REGISTER:Local")]
    [InlineData("AUTH:TOKEN1 x:pw:Steam")]
    [InlineData("AUTH:CAPS:pw:Steam")]
    [InlineData("AUTH:onlyuser")]
    public async Task EveryOtherShape_GoesOnToParseDirectAuth(string line)
    {
        (await RelayClient.TokenPassthrough(line, 1, "en", CancellationToken.None)).Should().BeNull();
    }

    // ================= row 9: the TOKEN line after OK is bridged, never logged =================

    [Fact]
    public async Task Row09_TokenLineAfterOk_ReachesStdout_AndNoLog()
    {
        var (_, token, verifier) = TokenLine();
        string tokenLine = $"TOKEN:{token}:1800000000";
        using var mud = new FakeMud().Serve($"OK\n{tokenLine}\n");
        var (stdout, stderr, result) = await Captured(() => ConnectAndBridge("tester", "secret", mud.Port, "Steam;token=1", rawTerminal: false));
        await mud.Served.WaitAsync(TimeSpan.FromSeconds(10));
        mud.Lines.Last().Should().Be("AUTH:tester:secret:Steam;token=1", "the request parameter passes through untouched");
        result.Should().Be(true);
        stdout.Should().Be($"OK\r\n{tokenLine}\n", "OK first, then the token line as bridged session bytes");
        stderr.Should().NotContain(verifier).And.NotContain(token).And.NotContain("TOKEN:");
    }
}
