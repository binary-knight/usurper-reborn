using System;
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

namespace UsurperRemake.Tests
{
    /// <summary>
    /// v1.2.8: the relay writes the server's raw auth reply ("ERR:..." or "OK") to stdout only for
    /// the AUTH: passthrough (the game's Online Play client reads it). A person typing at the login
    /// menu (rawTerminal true) sees the red message, not the protocol lines.
    ///
    /// Each test runs RelayClient.ConnectAndBridge against a fake MUD on a loopback TcpListener
    /// (127.0.0.1, port 0) and captures the auth reply through the RelayClient.OpenStdout seam.
    /// All four tests share one class so xunit runs them one at a time (the seam is static).
    /// 1.2.8: RelayToken128Tests swaps the same seam, so both run in one collection.
    /// </summary>
    [Collection("SharedGameSingletons")]
    public class RelayEcho128Tests
    {
        private const string ErrReason = "Invalid username or password.";

        /// <summary>Only a stop for a hung relay; a result never depends on it.</summary>
        private static readonly TimeSpan SafetyStop = TimeSpan.FromSeconds(60);

        /// <summary>
        /// 1.3.8: the human OK path writes nothing of its own, so the fake MUD sends this line right after OK. The
        /// relay bridges it to stdout only after it has read and handled the OK reply, so seeing it is the positive
        /// sign that the absence of a bare OK line is a result and not a relay that has not run yet. It holds no "OK".
        /// </summary>
        private const string HandledMarker = "RELAY-HANDLED-MARKER";

        [Fact]
        public async Task HumanPath_ErrReply_ShowsRedMessage_NotRawErrLine()
        {
            var output = await RunRelay("ERR:" + ErrReason, rawTerminal: true, expectConnected: false);
            output.Should().Contain(ErrReason, "the person still sees why the login failed");
            output.Should().NotContain("ERR:");
        }

        [Fact]
        public async Task HumanPath_OkReply_NoBareOkLine()
        {
            var output = await RunRelay("OK", rawTerminal: true, expectConnected: true);
            output.Should().Contain(HandledMarker, "the relay handled the OK reply before the absence is checked");
            output.Should().NotContain("OK\r\n");
            output.Should().NotContain("OK");
        }

        [Fact]
        public async Task Passthrough_ErrReply_KeepsRawErrLine()
        {
            var output = await RunRelay("ERR:" + ErrReason, rawTerminal: false, expectConnected: false);
            output.Should().StartWith("ERR:" + ErrReason + "\r\n", "the Online Play client reads the raw ERR line");
        }

        [Fact]
        public async Task Passthrough_OkReply_KeepsRawOkLine()
        {
            var output = await RunRelay("OK", rawTerminal: false, expectConnected: true);
            output.Should().Be("OK\r\n", "the Online Play client reads the bare OK line");
        }

        /// <summary>
        /// Stand up a fake MUD that reads lines until the AUTH line, answers with the reply and closes,
        /// then run ConnectAndBridge against it and return what it wrote to the seam stream.
        /// </summary>
        private static async Task<string> RunRelay(string reply, bool rawTerminal, bool expectConnected)
        {
            Console.IsInputRedirected.Should().BeTrue(
                "the OK path bridges stdin; a terminal on stdin would enter raw mode and wait for a keystroke");

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            string? authSeen = null;
            bool sendMarker = rawTerminal && expectConnected;

            var server = Task.Run(async () =>
            {
                using var conn = await listener.AcceptTcpClientAsync();
                var stream = conn.GetStream();
                var reader = new StreamReader(stream, Encoding.UTF8);
                string? line;
                // The relay may send an X-IP line first when SSH_CLIENT is set.
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (line.StartsWith("AUTH:", StringComparison.Ordinal)) { authSeen = line; break; }
                }
                var bytes = Encoding.UTF8.GetBytes(reply + "\n" + (sendMarker ? HandledMarker + "\n" : ""));
                await stream.WriteAsync(bytes, 0, bytes.Length);
                await stream.FlushAsync();
                conn.Close();
            });

            var captured = new SeamStream();
            var saved = RelayClient.OpenStdout;
            var savedIn = RelayClient.OpenStdin;
            RelayClient.OpenStdout = () => captured;
            // 1.3.8: the bridge's stdin side cancels the socket side when stdin ends. With the test host's stdin at end of
            // file that cancel could land before the socket side read the marker line, so the human OK path saw no marker
            // in about one run of ten after another class had run first. A stdin that stays open until the bridge is
            // cancelled leaves the socket side to end the bridge, as an open SSH session does (RelayToken128Tests does it).
            RelayClient.OpenStdin = () => new HeldStdin();
            // 1.3.7: no wall clock budget decides a result. On a loaded runner (the localization scans run beside
            // this class) the relay's continuations can run seconds late; a 10 s token then cancelled the relay
            // before it sent AUTH or before it read the reply (release run 40058954803, twice). The token is only a
            // safety stop now, cancelled when the test ends.
            using var cts = new CancellationTokenSource(SafetyStop);
            try
            {
                var method = typeof(RelayClient).GetMethod("ConnectAndBridge", BindingFlags.NonPublic | BindingFlags.Static);
                method.Should().NotBeNull();
                var task = (Task<bool>)method!.Invoke(null, new object[]
                {
                    "tester", "secret", port, "Test", cts.Token, false, rawTerminal, "en"
                })!;
                // The server task ends after it sent the reply and closed, so the relay had its answer. Bounded by
                // the safety stop: a relay that never connects fails here instead of hanging the test run.
                (await Task.WhenAny(server, Task.Delay(SafetyStop))).Should().BeSameAs(server, "the relay connects and sends AUTH");
                await server;
                authSeen.Should().StartWith("AUTH:tester:secret:Test");
                if (!expectConnected)
                {
                    // After ERR the relay writes its lines, flushes and returns false, so wait for it to finish.
                    var ended = await Task.WhenAny(task, Task.Delay(SafetyStop));
                    ended.Should().BeSameAs(task, "the relay returns after an ERR reply");
                    (await task).Should().BeFalse();
                }
                else
                {
                    // After OK the passthrough writes the OK line before the bridge starts; wait for that write.
                    if (!rawTerminal)
                        (await Task.WhenAny(captured.FirstWrite, Task.Delay(SafetyStop))).Should().BeSameAs(captured.FirstWrite);
                    // Human path: the relay prints no OK line, so wait (bounded) for the line the fake MUD sent after OK
                    // to be bridged to stdout; only then is a missing OK line a result.
                    if (sendMarker)
                    {
                        var deadline = DateTime.UtcNow + SafetyStop;
                        while (!captured.Contains(HandledMarker) && DateTime.UtcNow < deadline)
                            await Task.Delay(20);
                        captured.Contains(HandledMarker).Should().BeTrue("the relay handled the OK reply and bridged the next line");
                    }
                    // The fake MUD closed after its reply, so the socket side ends the bridge and cancels the stdin read.
                    // The assertions stay on the captured output; a bridge that does not end is only logged.
                    var finished = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));
                    if (finished == task)
                        (await task).Should().BeTrue();
                    else
                        Console.Error.WriteLine("[RelayEcho128Tests] relay bridge did not wind down after the fake MUD closed");
                }
            }
            finally
            {
                cts.Cancel();
                RelayClient.OpenStdout = saved;
                RelayClient.OpenStdin = savedIn;
                listener.Stop();
            }

            return captured.Text();
        }

        /// <summary>A stdin that gives no bytes and no end of file; a read ends only when the bridge cancels it.</summary>
        private sealed class HeldStdin : Stream
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

        /// <summary>The seam stream; FirstWrite completes when the relay first writes to it.</summary>
        private sealed class SeamStream : MemoryStream
        {
            private readonly TaskCompletionSource _firstWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public Task FirstWrite => _firstWrite.Task;

            private readonly object _gate = new();

            public override void Write(byte[] buffer, int offset, int count)
            {
                lock (_gate) base.Write(buffer, offset, count);
                _firstWrite.TrySetResult();
            }

            public override void Write(ReadOnlySpan<byte> buffer)
            {
                lock (_gate) base.Write(buffer);
                _firstWrite.TrySetResult();
            }

            /// <summary>Everything written so far, read under the write lock (the bridge writes from another thread).</summary>
            public string Text()
            {
                lock (_gate) return Encoding.UTF8.GetString(ToArray());
            }

            public bool Contains(string s) => Text().Contains(s, StringComparison.Ordinal);
        }
    }
}
