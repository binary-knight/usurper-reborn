using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Server;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.4.0: when the relay's stdin ends (a real end of file, a read of 0 at once), the relay shuts its socket's send
/// side and goes on writing what the server sends until the server closes, for at most RelayClient.StdinEndDrain.
/// Each test runs RelayClient.ConnectAndBridge against a fake MUD on a loopback listener (TestLoopback, 127.0.0.1,
/// an ephemeral port) with stdin and stdout swapped through the relay's seams. The seams are static, so the class
/// runs in the collection RelayEcho128Tests and RelayToken128Tests use.
/// </summary>
[Collection("SharedGameSingletons")]
public class RelayDrain140Tests
{
    /// <summary>Sent by the fake MUD right after OK, before it has seen the relay's end.</summary>
    private const string AfterOk = "RELAY-DRAIN-AFTER-OK";

    /// <summary>Sent by the fake MUD after it read 0 (the relay's half close).</summary>
    private const string AfterEnd = "RELAY-DRAIN-AFTER-END";

    /// <summary>Only a stop for a hung relay in a passing run.</summary>
    private static readonly TimeSpan SafetyStop = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task EndOfFileStdin_ServerOutputUntilItCloses_IsWritten()
    {
        using var mud = new FakeMud(closeAfterEnd: true);
        var captured = new Capture();
        var relay = Run(mud.Port, captured, CancellationToken.None, out var restore);
        try
        {
            (await relay.WaitAsync(SafetyStop)).Should().BeTrue("the session ran");
            await mud.Served.WaitAsync(SafetyStop);
            mud.SawEnd.Task.IsCompleted.Should().BeTrue("the relay told the server it is done");
            var text = captured.Text();
            text.Should().StartWith("OK\r\n", "the passthrough reply line comes first");
            text.Should().Contain(AfterOk, "the line sent right after OK is written even when stdin ends at once");
            text.Should().Contain(AfterEnd, "the relay reads on after its half close until the server closes");
        }
        finally { restore(); }
    }

    [Fact]
    public async Task EndOfFileStdin_ServerThatNeverCloses_IsLetGoAfterTheDrainBound()
    {
        using var mud = new FakeMud(closeAfterEnd: false);
        var captured = new Capture();
        var clock = Stopwatch.StartNew();
        var relay = Run(mud.Port, captured, CancellationToken.None, out var restore);
        try
        {
            // With no bound the relay would wait on this server forever; this timeout is the test's own.
            var ended = await Task.WhenAny(relay, Task.Delay(RelayClient.StdinEndDrain + TimeSpan.FromSeconds(20)));
            ended.Should().BeSameAs(relay, "the drain bound lets go of a server that never closes");
            (await relay).Should().BeTrue();
            clock.Elapsed.Should().BeGreaterThan(RelayClient.StdinEndDrain - TimeSpan.FromMilliseconds(100),
                "the relay kept reading for the drain bound instead of ending at stdin's end");
            captured.Text().Should().Contain(AfterOk).And.Contain(AfterEnd);
        }
        finally { mud.Release(); restore(); }
    }

    [Fact]
    public async Task EndOfFileStdin_OuterCancel_EndsTheDrainAtOnce()
    {
        using var mud = new FakeMud(closeAfterEnd: false);
        using var cts = new CancellationTokenSource();
        var captured = new Capture();
        var clock = Stopwatch.StartNew();
        var relay = Run(mud.Port, captured, cts.Token, out var restore);
        try
        {
            (await Task.WhenAny(mud.SawEnd.Task, Task.Delay(SafetyStop))).Should().BeSameAs(mud.SawEnd.Task, "the relay half closed");
            cts.Cancel();
            var ended = await Task.WhenAny(relay, Task.Delay(SafetyStop));
            ended.Should().BeSameAs(relay);
            await relay;
            clock.Elapsed.Should().BeLessThan(RelayClient.StdinEndDrain, "the outer token ends the bridge without waiting for the bound");
        }
        finally { mud.Release(); restore(); }
    }

    /// <summary>Start ConnectAndBridge on the passthrough path with stdin at end of file and stdout captured.</summary>
    private static Task<bool> Run(int port, Capture captured, CancellationToken ct, out Action restore)
    {
        var savedOut = RelayClient.OpenStdout;
        var savedIn = RelayClient.OpenStdin;
        RelayClient.OpenStdout = () => captured;
        RelayClient.OpenStdin = () => Stream.Null; // a read returns 0 at once
        restore = () => { RelayClient.OpenStdout = savedOut; RelayClient.OpenStdin = savedIn; };
        var method = typeof(RelayClient).GetMethod("ConnectAndBridge", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (Task<bool>)method.Invoke(null, new object[] { "tester", "secret", port, "Test", ct, false, false, "en" })!;
    }

    /// <summary>
    /// Accept one connection, read lines up to the AUTH line, send OK and AfterOk, read until 0, send AfterEnd, then
    /// close (or, when <c>closeAfterEnd</c> is false, hold the connection until Release).
    /// </summary>
    private sealed class FakeMud : IDisposable
    {
        private readonly TcpListener _listener = TestLoopback.Listen();
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource SawEnd = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly Task Served;
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public FakeMud(bool closeAfterEnd)
        {
            Served = Task.Run(async () =>
            {
                using var conn = await _listener.AcceptTcpClientAsync();
                var stream = conn.GetStream();
                var reader = new StreamReader(stream, Encoding.UTF8);
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                    if (line.StartsWith("AUTH:", StringComparison.Ordinal)) break;
                await Send(stream, "OK\n" + AfterOk + "\n");
                while (await reader.ReadLineAsync() != null) { }
                SawEnd.TrySetResult();
                await Send(stream, AfterEnd + "\n");
                if (!closeAfterEnd) await _release.Task;
                conn.Close();
            });
        }

        private static async Task Send(NetworkStream stream, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            await stream.WriteAsync(bytes, 0, bytes.Length);
            await stream.FlushAsync();
        }

        public void Release() => _release.TrySetResult();

        public void Dispose()
        {
            Release();
            _listener.Stop();
        }
    }

    /// <summary>The stdout seam; the bridge writes from another thread, so reads take the same lock.</summary>
    private sealed class Capture : MemoryStream
    {
        private readonly object _gate = new();
        public override void Write(byte[] buffer, int offset, int count) { lock (_gate) base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { lock (_gate) base.Write(buffer); }
        public string Text() { lock (_gate) return Encoding.UTF8.GetString(ToArray()); }
    }
}
