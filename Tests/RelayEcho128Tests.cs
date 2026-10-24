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
                var bytes = Encoding.UTF8.GetBytes(reply + "\n");
                await stream.WriteAsync(bytes, 0, bytes.Length);
                await stream.FlushAsync();
                conn.Close();
            });

            var captured = new SeamStream();
            var saved = RelayClient.OpenStdout;
            RelayClient.OpenStdout = () => captured;
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
                    // The bridge then reads stdin, which cannot be cancelled; if the test host's stdin never
                    // reaches end of file the bridge stays open, so the assertions are on the captured output.
                    var finished = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5)));
                    if (finished == task)
                        (await task).Should().BeTrue();
                    else
                        Console.Error.WriteLine("[RelayEcho128Tests] relay bridge did not wind down; stdin gave no end of file");
                }
            }
            finally
            {
                cts.Cancel();
                RelayClient.OpenStdout = saved;
                listener.Stop();
            }

            return Encoding.UTF8.GetString(captured.ToArray());
        }

        /// <summary>The seam stream; FirstWrite completes when the relay first writes to it.</summary>
        private sealed class SeamStream : MemoryStream
        {
            private readonly TaskCompletionSource _firstWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public Task FirstWrite => _firstWrite.Task;

            public override void Write(byte[] buffer, int offset, int count)
            {
                base.Write(buffer, offset, count);
                _firstWrite.TrySetResult();
            }

            public override void Write(ReadOnlySpan<byte> buffer)
            {
                base.Write(buffer);
                _firstWrite.TrySetResult();
            }
        }
    }
}
