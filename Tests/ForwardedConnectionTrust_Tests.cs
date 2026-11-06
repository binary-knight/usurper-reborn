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
using UsurperRemake.BBS;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using UsurperReborn.Tests.Localization;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.11: the login trust rules of the game server. The password-free AUTH form is refused unless the server
/// was started with --allow-trusted-auth; with it, the form is accepted only from a loopback peer that sent no
/// forwarding header and no client-type line. A forwarding header's address is believed only from a loopback
/// peer; from any other peer the header is consumed and its address ignored. Password logins are unchanged.
/// Every socket is a loopback socket (<see cref="TestLoopback"/>); a peer outside this host is the server's
/// tests-only peer address.
/// </summary>
[Collection("SharedGameSingletons")]
public class ForwardedConnectionTrust_Tests : IDisposable
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;

    private const string Refused = "ERR:Authentication required. Trusted auth is only available from local relays.";
    private const string BannedLine = "  Connection refused: this address is banned from the server.";
    private const string OutsidePeer = "203.0.113.40"; // a documentation address, never loopback
    private const string Name = "Trust Row";
    private const string Password = "trustrow-pass-long";

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-trust-1311-{Guid.NewGuid():N}.db");
    private SqlSaveBackend? _db;
    private SqlSaveBackend Db => _db ??= new SqlSaveBackend(_path);
    private readonly bool _flagBefore = DoorMode.AllowTrustedAuth;
    private readonly List<string> _throttleIps = new();

    public void Dispose()
    {
        DoorMode.AllowTrustedAuth = _flagBefore;
        foreach (var ip in _throttleIps) FailedLogins().TryRemove(ip, out _);
        if (_db == null) return;
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    // ---------- helpers ----------

    private static ConcurrentDictionary<string, (int Fails, DateTime LastFail, DateTime LockUntil)> FailedLogins() =>
        (ConcurrentDictionary<string, (int, DateTime, DateTime)>)typeof(MudServer).GetField("_failedLogins", SNP)!.GetValue(null)!;

    private string Ip(int n)
    {
        var ip = $"198.51.100.{n}";
        FailedLogins().TryRemove(ip, out _);
        _throttleIps.Add(ip);
        return ip;
    }

    private async Task Register() => (await Db.RegisterPlayer(Name, Password)).success.Should().BeTrue();

    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    private static byte[] Join(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    /// <summary>The 12 byte signature of a version 2 forwarding header, from its public specification.</summary>
    private static readonly byte[] Signature = { 0x0D, 0x0A, 0x0D, 0x0A, 0x00, 0x0D, 0x0A, 0x51, 0x55, 0x49, 0x54, 0x0A };

    /// <summary>A version 2 forwarding header: signature, version and command, family and transport, length, payload.</summary>
    private static byte[] Header(byte verCmd, byte famProto, byte[] payload) =>
        Join(Signature, new[] { verCmd, famProto, (byte)(payload.Length >> 8), (byte)(payload.Length & 0xFF) }, payload);

    /// <summary>An IPv4 payload: source address, destination 127.0.0.1, source and destination ports.</summary>
    private static byte[] V4(string source) =>
        Join(IPAddress.Parse(source).GetAddressBytes(), new byte[] { 127, 0, 0, 1 }, new byte[] { 0xC3, 0x50, 0x0F, 0xA1 });

    /// <summary>A well formed header for a TCP over IPv4 connection from <paramref name="source"/>.</summary>
    private static byte[] GoodHeader(string source) => Header(0x21, 0x11, V4(source));

    private static MudServer NewServer(string? peer)
    {
        var server = (MudServer)RuntimeHelpers.GetUninitializedObject(typeof(MudServer));
        typeof(MudServer).GetField("<ActiveSessions>k__BackingField", NP)!
            .SetValue(server, new ConcurrentDictionary<string, PlayerSession>());
        server.StopAfterLoginReply = true; // the replies only: no game session starts in these tests
        server.PeerAddressForTests = peer;
        return server;
    }

    /// <summary>
    /// One real loopback connection into MudServer.HandleConnectionAsync. Each segment is written after its pause.
    /// Reads until the server closes; after an OK line until 700 ms pass with nothing new; before any reply for up
    /// to 15 s; after a reply that is not OK until 3 s pass with nothing new. Returns the lines read.
    /// </summary>
    private async Task<List<string>> Exchange(string? peer, params (int PauseMs, byte[] Data)[] segments)
    {
        using var listener = new DisposableListener(TestLoopback.Listen());
        var port = ((IPEndPoint)listener.Inner.LocalEndpoint).Port;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var output = new StringBuilder();
        var server = NewServer(peer);
        var acceptTask = listener.Inner.AcceptTcpClientAsync();
        using var tcp = await TestLoopback.Connect(port);
        var accepted = await acceptTask;
        var handle = typeof(MudServer).GetMethod("HandleConnectionAsync", NP)!;
        var serverTask = (Task)handle.Invoke(server, new object[] { accepted, Db, cts.Token })!;
        var stream = tcp.GetStream();
        foreach (var (pause, data) in segments)
        {
            if (pause > 0) await Task.Delay(pause);
            await stream.WriteAsync(data, 0, data.Length);
            await stream.FlushAsync();
        }

        var buffer = new byte[4096];
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var text = output.ToString();
            var wait = text.Length == 0 ? TimeSpan.FromSeconds(15)
                : text.StartsWith("OK\r\n", StringComparison.Ordinal) ? TimeSpan.FromMilliseconds(700)
                : TimeSpan.FromSeconds(3);
            using var readCts = new CancellationTokenSource(wait);
            int read;
            try { read = await stream.ReadAsync(buffer, 0, buffer.Length, readCts.Token); }
            catch (Exception) { break; }
            if (read == 0) break;
            output.Append(Encoding.UTF8.GetString(buffer, 0, read));
        }
        tcp.Close();
        cts.Cancel();
        try { await serverTask.WaitAsync(TimeSpan.FromSeconds(20)); } catch { /* the connection may end any way */ }
        return output.ToString().Split('\n').Select(l => l.TrimEnd('\r')).ToList();
    }

    private Task<List<string>> Exchange(string? peer, byte[] data) => Exchange(peer, (0, data));

    private sealed class DisposableListener : IDisposable
    {
        public readonly TcpListener Inner;
        public DisposableListener(TcpListener inner) => Inner = inner;
        public void Dispose() => Inner.Stop();
    }

    /// <summary>The bytes and the peer for one connection shape, with the password-free form or a password.</summary>
    private (string? Peer, byte[] Data) Shape(string shape, bool withPassword)
    {
        string Auth(string type) => withPassword ? $"AUTH:{Name}:{Password}:{type}\n" : $"AUTH:{Name}:{type}\n";
        return shape switch
        {
            "plain" => (null, Bytes(Auth("Local"))),
            "address line" => (null, Bytes($"X-IP:{Ip(11)}\n" + Auth("Local"))),
            "header" => (null, Join(GoodHeader(Ip(12)), Bytes(Auth("Local")))),
            "client type" => (null, Bytes($"X-IP:{Ip(13)}\nX-Client:Web\n" + Auth("Web"))),
            "outside peer" => (OutsidePeer, Bytes(Auth("Local"))),
            _ => throw new ArgumentException(shape),
        };
    }

    // ================= the flag =================

    [Fact]
    public void TheFlag_IsOffByDefault()
    {
        DoorMode.AllowTrustedAuth.Should().BeFalse("password-free logins are off unless the server is started with the flag");
    }

    [Fact]
    public void TheFlag_IsTurnedOnByItsArgument()
    {
        // ParseCommandLineArgs sets DoorMode's static state; every static field is put back after.
        var fields = typeof(DoorMode).GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(f => !f.IsLiteral && !f.IsInitOnly).ToArray();
        var saved = fields.Select(f => f.GetValue(null)).ToArray();
        var oldErr = Console.Error;
        Console.SetError(new StringWriter());
        try
        {
            DoorMode.ParseCommandLineArgs(new[] { "--allow-trusted-auth" });
            DoorMode.AllowTrustedAuth.Should().BeTrue();
        }
        finally
        {
            Console.SetError(oldErr);
            for (int i = 0; i < fields.Length; i++) fields[i].SetValue(null, saved[i]);
        }
    }

    // ================= flag off: the password-free form is refused in every shape =================

    [Theory]
    [InlineData("plain")]
    [InlineData("address line")]
    [InlineData("header")]
    [InlineData("client type")]
    [InlineData("outside peer")]
    public async Task FlagOff_PasswordFree_IsRefused(string shape)
    {
        await Register();
        var (peer, data) = Shape(shape, withPassword: false);
        var lines = await Exchange(peer, data);
        lines.Should().Contain(Refused).And.NotContain("OK");
    }

    // ================= flag on =================

    [Theory]
    [InlineData("plain")]
    [InlineData("address line")]
    public async Task FlagOn_LocalRelayShape_IsAccepted(string shape)
    {
        DoorMode.AllowTrustedAuth = true;
        await Register();
        var (peer, data) = Shape(shape, withPassword: false);
        var lines = await Exchange(peer, data);
        lines.Should().Contain("OK").And.NotContain(Refused);
    }

    [Theory]
    [InlineData("header")]
    [InlineData("client type")]
    [InlineData("outside peer")]
    public async Task FlagOn_PasswordFree_IsRefused(string shape)
    {
        DoorMode.AllowTrustedAuth = true;
        await Register();
        var (peer, data) = Shape(shape, withPassword: false);
        var lines = await Exchange(peer, data);
        lines.Should().Contain(Refused).And.NotContain("OK");
    }

    // ================= password logins are unchanged =================

    [Theory]
    [InlineData(false, "plain")]
    [InlineData(false, "address line")]
    [InlineData(false, "header")]
    [InlineData(false, "client type")]
    [InlineData(false, "outside peer")]
    [InlineData(true, "plain")]
    [InlineData(true, "address line")]
    [InlineData(true, "header")]
    [InlineData(true, "client type")]
    [InlineData(true, "outside peer")]
    public async Task PasswordLogin_IsAccepted(bool flag, string shape)
    {
        DoorMode.AllowTrustedAuth = flag;
        await Register();
        var (peer, data) = Shape(shape, withPassword: true);
        var lines = await Exchange(peer, data);
        lines.Should().Contain("OK").And.NotContain(Refused);
    }

    // ================= header edge cases never read OK for the password-free form =================

    [Theory]
    [InlineData("unparsed family")]
    [InlineData("datagram transport")]
    [InlineData("wrong version")]
    [InlineData("local command")]
    public async Task FlagOn_MatchedSignature_RefusesPasswordFree(string edge)
    {
        DoorMode.AllowTrustedAuth = true;
        await Register();
        var header = edge switch
        {
            "unparsed family" => Header(0x21, 0x00, Array.Empty<byte>()),
            "datagram transport" => Header(0x21, 0x12, V4(Ip(21))),
            "wrong version" => Header(0x11, 0x11, V4(Ip(22))),
            "local command" => Header(0x20, 0x11, V4(Ip(23))),
            _ => throw new ArgumentException(edge),
        };
        var lines = await Exchange(null, Join(header, Bytes($"AUTH:{Name}:Local\n")));
        lines.Should().Contain(Refused).And.NotContain("OK");
    }

    [Fact]
    public async Task FlagOn_HeaderSplitAcrossWrites_RefusesPasswordFree()
    {
        DoorMode.AllowTrustedAuth = true;
        await Register();
        var all = Join(GoodHeader(Ip(24)), Bytes($"AUTH:{Name}:Local\n"));
        var lines = await Exchange(null,
            (0, all[..6]), (40, all[6..14]), (40, all[14..20]), (40, all[20..]));
        lines.Should().Contain(Refused).And.NotContain("OK");
    }

    [Fact]
    public async Task FlagOn_HeaderSlowerThanTheReadWindow_NeverReadsOk()
    {
        DoorMode.AllowTrustedAuth = true;
        await Register();
        var header = GoodHeader(Ip(25));
        // The signature and the length arrive; the payload does not come within the read window.
        var lines = await Exchange(null, (0, header[..16]), (900, Bytes($"AUTH:{Name}:Local\n")));
        lines.Should().NotContain("OK");
    }

    // ================= a header's address is believed only from a loopback peer =================

    [Fact]
    public async Task OutsidePeer_HeaderAddressIsIgnored_PasswordLoginOk()
    {
        await Register();
        var banned = Ip(31);
        Db.BanIp(banned, "test row", "test", null);
        var lines = await Exchange(OutsidePeer, Join(GoodHeader(banned), Bytes($"AUTH:{Name}:{Password}:Local\n")));
        lines.Should().Contain("OK").And.NotContain(BannedLine);
    }

    [Fact]
    public async Task LoopbackPeer_HeaderAddressIsBelieved()
    {
        await Register();
        var banned = Ip(32);
        Db.BanIp(banned, "test row", "test", null);
        var lines = await Exchange(null, Join(GoodHeader(banned), Bytes($"AUTH:{Name}:{Password}:Local\n")));
        lines.Should().Contain(BannedLine).And.NotContain("OK");
    }

    // ================= the tests-only peer address =================

    [Fact]
    public void Server_PeerAddressForTests_IsNeverSetByTheServer()
    {
        var root = Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Scripts");
        var src = File.ReadAllText(Path.Combine(root, "Server", "MudServer.cs"));
        src.Should().Contain("internal string? PeerAddressForTests;", "no initializer: null on every real server");
        foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            Regex.Matches(File.ReadAllText(file), @"PeerAddressForTests\s*=[^=]").Count.Should().Be(0, file);
    }
}
