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
using UsurperRemake;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.4: a failed password login over the AUTH line (SSH relay, desktop and Steam Online Play) gets the website reset
/// address on its one ERR line while the server sends mail; with mail off the line is the 1.3.3 line byte for byte.
/// Unknown name and wrong password get the same line. The relay and the Online Play login show the whole text after
/// "ERR:", the URL's colon kept.
/// </summary>
[Collection("SharedGameSingletons")]
public class AuthLinePointer_PwReset134Tests : IDisposable
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    /// <summary>The ERR line a failed English password login got at 7ef83c36 (1.3.3).</summary>
    private const string OldLine = "ERR:Player doesn't exist or wrong password.";
    private const string Url = "https://usurper-reborn.net/account/forgot";
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-pw134-auth-{Guid.NewGuid():N}.db");
    private SqlSaveBackend? _db;
    private SqlSaveBackend Db => _db ??= new SqlSaveBackend(_path);
    private readonly Func<DateTime> _clock = MailSwitch.UtcNow;
    private readonly List<string> _ips = new();

    public void Dispose()
    {
        MailSwitch.UtcNow = _clock;
        foreach (var ip in _ips) FailedLogins().TryRemove(ip, out _);
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    private static string L(string lang, string key) => Loc.GetIn(lang, key);

    private static ConcurrentDictionary<string, (int Fails, DateTime LastFail, DateTime LockUntil)> FailedLogins() =>
        (ConcurrentDictionary<string, (int, DateTime, DateTime)>)typeof(MudServer).GetField("_failedLogins", SNP)!.GetValue(null)!;

    private string Ip(int n)
    {
        var ip = $"198.51.100.{n}";
        FailedLogins().TryRemove(ip, out _);
        _ips.Add(ip);
        return ip;
    }

    /// <summary>The raw bytes the server sends back for one AUTH line from <paramref name="ip"/>.</summary>
    private async Task<string> AuthReply(string ip, string authLine)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var output = new MemoryStream();
        try
        {
            using var tcp = new TcpClient();
            var acceptTask = listener.AcceptTcpClientAsync();
            await tcp.ConnectAsync(IPAddress.Loopback, port);
            var accepted = await acceptTask;
            var server = (MudServer)RuntimeHelpers.GetUninitializedObject(typeof(MudServer));
            typeof(MudServer).GetField("<ActiveSessions>k__BackingField", NP)!.SetValue(server, new ConcurrentDictionary<string, PlayerSession>());
            server.StopAfterLoginReply = true;
            var serverTask = (Task)typeof(MudServer).GetMethod("HandleConnectionAsync", NP)!.Invoke(server, new object[] { accepted, Db, cts.Token })!;
            var s = tcp.GetStream();
            var bytes = Encoding.UTF8.GetBytes($"X-IP:{ip}\nAUTH:{authLine}\n");
            await s.WriteAsync(bytes, 0, bytes.Length);
            await s.CopyToAsync(output, cts.Token);
            await serverTask.WaitAsync(TimeSpan.FromSeconds(20));
        }
        finally { listener.Stop(); }
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private async Task Setup(string mail)
    {
        (await Db.RegisterPlayer("Real Name", "right-pass-1")).success.Should().BeTrue();
        AccountMailData_PwReset134Tests.SetMailSwitch(_path, mail, 1);
    }

    // ================= (a) mail on: the pointer, on the one line =================

    [Fact]
    public async Task MailOn_ErrLine_EndsWithThePointer_OnOneLine()
    {
        await Setup("1");
        var raw = await AuthReply(Ip(90), "Real Name:wrong-pass-1:Steam:1.3.4");
        string expected = $"{OldLine} {L("en", "auth.forgot_pointer")}";
        raw.Should().Be(expected + "\r\n", "the ERR line with the pointer, one line, and nothing else");
        expected.Should().EndWith(Url);
        expected.IndexOfAny(new[] { '\r', '\n' }).Should().Be(-1);
        MudServer.WithForgotPointer("m", "hu").Should().Be("m " + L("hu", "auth.forgot_pointer"));
        foreach (var lang in new[] { "en", "es", "fr", "hu", "it" })
            L(lang, "auth.forgot_pointer").Should().EndWith(" " + Url, lang);
    }

    // ================= (b) mail off: the 1.3.3 line =================

    [Fact]
    public async Task MailOff_ErrLine_IsTheOldLine_ByteForByte()
    {
        await Setup("0");
        (await AuthReply(Ip(91), "Real Name:wrong-pass-1:Steam:1.3.4")).Should().Be(OldLine + "\r\n");
        AccountMailData_PwReset134Tests.SetMailSwitch(_path, "1", 11);
        (await AuthReply(Ip(92), "Ghost Name:wrong-pass-1:Steam")).Should().Be(OldLine + "\r\n", "a stale beat is mail off");
    }

    // ================= (c) one reply for an unknown name and a wrong password =================

    [Fact]
    public async Task UnknownName_AndWrongPassword_GetTheSameReply()
    {
        await Setup("1");
        var wrong = await AuthReply(Ip(93), "Real Name:wrong-pass-1:Steam");
        var unknown = await AuthReply(Ip(94), "Ghost Name:wrong-pass-1:Steam");
        unknown.Should().Be(wrong);
        wrong.Should().Contain(Url);
        AccountMailData_PwReset134Tests.SetMailSwitch(_path, "0", 1);
        (await AuthReply(Ip(95), "Ghost Name:wrong-pass-1:Steam")).Should().Be(await AuthReply(Ip(96), "Real Name:wrong-pass-1:Steam"));
    }

    // ================= (d) the relay and Online Play show it =================

    private static string PointerLine() => "ERR:" + MudServer.WithForgotPointer(L("en", "auth.err_bad_login"), "en");

    [Fact]
    public void OnlineAuthFlow_ShowsTheWholeTextAfterErr()
    {
        string line = PointerLine();
        var reply = OnlineAuthFlow.MatchReply(line + "\r\n", complete: false)!;
        reply.Kind.Should().Be(OnlineAuthReply.ReplyKind.Err);
        reply.ErrText.Should().Be(line.Substring(4));
        reply.ErrText.Should().EndWith(Url, "the URL's colon is not a field separator here");
        reply.TokenRefused.Should().BeFalse();
    }

    [Fact]
    public async Task Relay_ShowsTheWholeTextAfterErr()
    {
        string line = PointerLine();
        var listener = TestLoopback.Listen();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var served = Task.Run(async () =>
        {
            using var conn = await listener.AcceptTcpClientAsync();
            var stream = conn.GetStream();
            var reader = new StreamReader(stream, Encoding.UTF8);
            string? l;
            while ((l = await reader.ReadLineAsync()) != null)
                if (l.StartsWith("AUTH:", StringComparison.Ordinal)) break;
            var bytes = Encoding.UTF8.GetBytes(line + "\n");
            await stream.WriteAsync(bytes, 0, bytes.Length);
            await stream.FlushAsync();
            conn.Close();
        });
        var captured = new MemoryStream();
        var savedOut = RelayClient.OpenStdout;
        var oldErr = Console.Error;
        RelayClient.OpenStdout = () => captured;
        Console.SetError(new StringWriter());
        bool result;
        try
        {
            var method = typeof(RelayClient).GetMethod("ConnectAndBridge", SNP)!;
            result = await ((Task<bool>)method.Invoke(null, new object[] { "Real Name", "wrong-pass-1", port, "SSH", CancellationToken.None, false, true, "en" })!)
                .WaitAsync(TimeSpan.FromSeconds(15));
            await served;
        }
        finally
        {
            RelayClient.OpenStdout = savedOut;
            Console.SetError(oldErr);
            listener.Stop();
        }
        result.Should().BeFalse();
        string shown = System.Text.RegularExpressions.Regex.Replace(Encoding.UTF8.GetString(captured.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");
        shown.Should().Contain("  " + line.Substring(4) + "\r\n");
        shown.Should().Contain(Url);
    }
}
