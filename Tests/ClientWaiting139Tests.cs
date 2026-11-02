using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using UsurperRemake;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.9: Client.Waiting, an empty web only frame sent at the start of each stream input read, so the web page knows
/// the game is waiting for input in any language. The guard scan fails on any stream read that does not send it first.
/// </summary>
[Collection("SharedGameSingletons")]
public class ClientWaiting139Tests
{
    private sealed class Scope : IDisposable
    {
        private readonly SessionContext? _saved = SessionContext.Current;
        private readonly bool _pacing = Pacing.Disabled;
        private readonly string _lang = GameConfig.Language;
        public readonly MemoryStream Stream = new();
        public readonly SessionContext? Ctx;
        public Scope(string kind)
        {
            Pacing.Disabled = true;
            GameConfig.Language = "en";
            Ctx = WebFlagOff200Tests.Session(kind, Stream, WebFlagOff3aTests.Hero(false));
            SessionContext.Current = Ctx;
        }
        public TerminalEmulator Term(string input)
        {
            var t = new TerminalEmulator(new WebFlagOff3aTests.ScriptedStream(input), Stream);
            if (Ctx != null) Ctx.Terminal = t;
            return t;
        }
        public byte[] Bytes(TerminalEmulator t) { t.StreamWriterInternal?.Flush(); return Stream.ToArray(); }
        public void Dispose()
        {
            SessionContext.Current = _saved;
            Pacing.Disabled = _pacing;
            GameConfig.Language = _lang;
        }
    }

    private static int Waiting(byte[] bytes) => WebMenu200Tests.Frames(bytes).Count(f => f.Package == "Client.Waiting");

    private static readonly byte[] EmptyFrame =
        new byte[] { 0xFF, 0xFA, 0xC9 }.Concat(Encoding.UTF8.GetBytes("Client.Waiting")).Concat(new byte[] { 0xFF, 0xF0 }).ToArray();

    /// <summary>Each public way into a stream read, with the frames one call must send.</summary>
    public static IEnumerable<object[]> Reads() => new[]
    {
        new object[] { "GetInput" }, new object[] { "PressAnyKey" }, new object[] { "GetKeyInput" }, new object[] { "GetMaskedInput" },
        new object[] { "ReadKeyAsync" }, new object[] { "GetValidChoice" }, new object[] { "ReadLineAsync" }, new object[] { "GetInputAsync" },
        new object[] { "GetStringInput" }, new object[] { "WaitForKeyPress" }, new object[] { "WaitForKey" }, new object[] { "GetCharAsync" },
        new object[] { "GetNumberInput" }, new object[] { "GetStringAsync" },
    };

    private static async Task Read(TerminalEmulator t, string how)
    {
        switch (how)
        {
            case "GetInput": await t.GetInput("> "); break;
            case "PressAnyKey": await t.PressAnyKey(); break;
            case "GetKeyInput": await t.GetKeyInput(); break;
            case "GetMaskedInput": await t.GetMaskedInput("pw: "); break;
            case "ReadKeyAsync": await t.ReadKeyAsync(); break;
            case "GetValidChoice": await t.GetValidChoice("> ", new[] { "1" }, "1"); break;
            case "ReadLineAsync": await t.ReadLineAsync(); break;
            case "GetInputAsync": await t.GetInputAsync("> "); break;
            case "GetStringInput": await t.GetStringInput("> "); break;
            case "WaitForKeyPress": await t.WaitForKeyPress(); break;
            case "WaitForKey": await t.WaitForKey(); break;
            case "GetCharAsync": await t.GetCharAsync(); break;
            case "GetNumberInput": await t.GetNumberInput("> ", 0, 9); break;
            case "GetStringAsync": await t.GetStringAsync("> "); break;
            default: throw new ArgumentException(how);
        }
    }

    [Theory]
    [MemberData(nameof(Reads))]
    public async Task EachRead_SendsOneFrame_ToTheWebClient(string how)
    {
        using var s = new Scope("web");
        var t = s.Term("1\n1\n");
        await Read(t, how);
        Waiting(s.Bytes(t)).Should().Be(1, "one read, one frame");
    }

    [Theory]
    [MemberData(nameof(Reads))]
    public async Task EachRead_SendsNoFrame_ToAnyOtherSession(string how)
    {
        foreach (var kind in new[] { "mud_gmcp", "ssh", "screenreader", "web_nogmcp", "door" })
        {
            using var s = new Scope(kind);
            var t = s.Term("1\n1\n");
            await Read(t, how);
            Waiting(s.Bytes(t)).Should().Be(0, "{0} is not the web client", kind);
        }
    }

    [Fact]
    public async Task TheFrame_HasAnEmptyBody()
    {
        using var s = new Scope("web");
        var t = s.Term("1\n");
        await t.GetInput("> ");
        var b = s.Bytes(t);
        Enumerable.Range(0, b.Length - EmptyFrame.Length + 1).Any(i => b.Skip(i).Take(EmptyFrame.Length).SequenceEqual(EmptyFrame))
            .Should().BeTrue("the package name and nothing after it");
    }

    [Fact]
    public async Task TwoReads_SendTwoFrames_AndAKeyTypedOnALineDoesNotSendMore()
    {
        using var s = new Scope("web");
        var t = s.Term("abcdef\nxyz\n");
        await t.GetInput("> ");
        await t.GetInput("> ");
        Waiting(s.Bytes(t)).Should().Be(2);
    }

    [Fact]
    public async Task APromptAnsweredWithoutAStreamRead_SendsNothingMore()
    {
        using var s = new Scope("web");
        var t = s.Term("look\n");
        await t.PressAnyKey();   // a command typed at a pause is kept for the next prompt
        (await t.GetInput("> ")).Should().Be("look");
        Waiting(s.Bytes(t)).Should().Be(1, "the kept line needs no read, so only the pause sent the frame");
    }

    [Fact]
    public async Task APauseWithNoRead_SendsNothing()
    {
        using var s = new Scope("web");
        var t = s.Term("");
        await Pacing.Wait(10);
        Waiting(s.Bytes(t)).Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScreenReaderOutput_DoesNotChange(bool plain)
    {
        async Task<string> Text(string kind)
        {
            using var s = new Scope(kind);
            var t = s.Term("1\n");
            t.IsPlainText = plain;
            await t.GetInput("> ");
            var frames = s.Bytes(t);
            var o = new List<byte>();
            for (int i = 0; i < frames.Length; i++)
            {
                if (i + 2 < frames.Length && frames[i] == 0xFF && frames[i + 1] == 0xFA && frames[i + 2] == 0xC9)
                {
                    int end = i + 3;
                    while (end + 1 < frames.Length && !(frames[end] == 0xFF && frames[end + 1] == 0xF0)) end++;
                    i = end + 1;
                    continue;
                }
                o.Add(frames[i]);
            }
            return Encoding.UTF8.GetString(o.ToArray());
        }
        (await Text("web")).Should().Be(await Text("ssh"), "the frame is out of band; the terminal text is the same");
    }

    [Fact]
    public async Task ASpectator_GetsNoFrame()
    {
        using var s = new Scope("web");
        var t = s.Term("1\n");
        var mirror = new MemoryStream();
        var spectator = new TerminalEmulator(new MemoryStream(), mirror);
        t.AddSpectatorStream(spectator);
        await t.GetInput("> ");
        spectator.StreamWriterInternal?.Flush();
        Waiting(s.Bytes(t)).Should().Be(1);
        mirror.Length.Should().BeGreaterThan(0, "the spectator does see the prompt");
        Encoding.UTF8.GetString(mirror.ToArray()).Should().NotContain("Client.Waiting");
    }

    // ------------------------------------------------------------------ the session that owns the terminal

    /// <summary>A stream that fails the test when written outside the owning terminal's writer lock.</summary>
    private sealed class LockCheckStream : MemoryStream
    {
        public TerminalEmulator? Terminal;
        public bool WrittenOutsideLock;
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (Terminal != null && !Monitor.IsEntered(Terminal._streamWriterLock)) WrittenOutsideLock = true;
            base.Write(buffer, offset, count);
        }
    }

    private static (SessionContext Ctx, TerminalEmulator Term, LockCheckStream Stream) Follower(string kind)
    {
        var stream = new LockCheckStream();
        var ctx = WebFlagOff200Tests.Session(kind, stream, WebFlagOff3aTests.Hero(false))!;
        var term = new TerminalEmulator(new MemoryStream(), stream);
        ctx.Terminal = term;
        stream.Terminal = term;
        return (ctx, term, stream);
    }

    [Fact]
    public void AFollowersRead_OnTheLeadersSession_ReachesTheFollower_UnderItsWriterLock()
    {
        using var leader = new Scope("web");
        var leaderTerm = leader.Term("");
        var f = Follower("web");
        GmcpBridge.EmitWebWaiting(f.Term, t => ReferenceEquals(t, f.Term) ? f.Ctx : null);
        Waiting(f.Stream.ToArray()).Should().Be(1, "the frame goes to the session that reads");
        Waiting(leader.Bytes(leaderTerm)).Should().Be(0, "the leader's own client is not waiting");
        f.Stream.WrittenOutsideLock.Should().BeFalse("a frame must not land inside the follower's text");
    }

    [Fact]
    public void AFollowersRead_OnATelnetFollower_SendsNothing()
    {
        using var leader = new Scope("web");
        var leaderTerm = leader.Term("");
        var f = Follower("mud_gmcp");
        GmcpBridge.EmitWebWaiting(f.Term, _ => f.Ctx);
        f.Stream.Length.Should().Be(0);
        Waiting(leader.Bytes(leaderTerm)).Should().Be(0);
    }

    [Fact]
    public void ANewLoginUnderTheSameName_DoesNotGetTheOldTerminalsFrame()
    {
        using var leader = new Scope("web");
        leader.Term("");
        var f = Follower("web");
        var stale = new TerminalEmulator(new MemoryStream(), new MemoryStream());
        GmcpBridge.EmitWebWaiting(stale, _ => f.Ctx);
        f.Stream.Length.Should().Be(0, "that session's terminal is not the one reading");
    }

    [Fact]
    public void AFollowerWhoLeft_SendsNothing_AndAResolverThatThrows_IsSwallowed()
    {
        using var leader = new Scope("web");
        var leaderTerm = leader.Term("");
        var other = new TerminalEmulator(new MemoryStream(), new MemoryStream());
        GmcpBridge.EmitWebWaiting(other, _ => null);
        GmcpBridge.EmitWebWaiting(other, _ => throw new InvalidOperationException("gone"));
        Waiting(leader.Bytes(leaderTerm)).Should().Be(0);
    }

    [Fact]
    public async Task AFollowersRead_ThroughThePublicPath_IsNotSentToTheLeader()
    {
        using var leader = new Scope("web");
        var leaderTerm = leader.Term("");
        var followerOut = new MemoryStream();
        var followerTerm = new TerminalEmulator(new WebFlagOff3aTests.ScriptedStream("1\n"), followerOut);
        await followerTerm.GetInput("> ");   // no live server here, so no session owns it
        Waiting(leader.Bytes(leaderTerm)).Should().Be(0);
        Waiting(followerOut.ToArray()).Should().Be(0);
    }

    // ------------------------------------------------------------------ the guard: no stream read without the frame

    private static string TerminalSource()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "UsurperReborn.sln"))) dir = Path.GetDirectoryName(dir);
        return File.ReadAllText(Path.Combine(dir!, "Scripts", "UI", "TerminalEmulator.cs"));
    }

    /// <summary>
    /// A drain, not a wait: FlushPendingInput throws away what is already there and never blocks, and IsInputAvailable
    /// only asks whether a byte is there.
    /// </summary>
    private static readonly string[] NotAWait = { "FlushPendingInput", "IsInputAvailable" };

    private static bool IsStreamRead(InvocationExpressionSyntax call)
    {
        string callee = call.Expression.ToString();
        return callee.StartsWith("_streamReader.", StringComparison.Ordinal)
            && callee.Split('.').Last().StartsWith("Read", StringComparison.Ordinal);
    }

    private static bool SendsWaiting(InvocationExpressionSyntax call) => call.Expression.ToString().EndsWith("EmitWebWaiting", StringComparison.Ordinal);

    /// <summary>The methods that read the stream, and the ones of them with a path that does not send the frame first.</summary>
    internal static (List<string> Readers, List<string> Unguarded) Scan(string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();
        var calls = methods.ToDictionary(m => m, m => m.DescendantNodes().OfType<InvocationExpressionSyntax>().ToList());

        int FirstSend(MethodDeclarationSyntax m) =>
            calls[m].Where(SendsWaiting).Select(c => c.SpanStart).DefaultIfEmpty(int.MaxValue).Min();

        bool Guarded(MethodDeclarationSyntax m, int at, HashSet<MethodDeclarationSyntax> seen)
        {
            if (FirstSend(m) < at) return true;
            if (!seen.Add(m)) return true;
            // not sent here: every caller in the file must have sent it before the call
            var callers = methods.SelectMany(c => calls[c].Where(x => x.Expression.ToString() == m.Identifier.Text).Select(x => (c, x))).ToList();
            return callers.Count > 0 && callers.All(p => Guarded(p.c, p.x.SpanStart, seen));
        }

        var readers = new List<string>();
        var unguarded = new List<string>();
        foreach (var m in methods)
        {
            var reads = calls[m].Where(IsStreamRead).ToList();
            // ReadLineWithBackspace reads the console; on a stream path it would be a read of the wrong source too
            reads.AddRange(calls[m].Where(c => c.Expression.ToString().EndsWith("ReadLineWithBackspace", StringComparison.Ordinal)
                && c.Ancestors().OfType<IfStatementSyntax>().Any(i => i.Statement.Span.Contains(c.Span) && i.Condition.ToString().Contains("_streamReader != null"))));
            // a read through a local copy of the reader counts as a read
            var aliases = m.DescendantNodes().OfType<VariableDeclaratorSyntax>().Where(v => v.Initializer?.Value.ToString() == "_streamReader").ToList();
            if (reads.Count == 0 && aliases.Count == 0) continue;
            if (Array.IndexOf(NotAWait, m.Identifier.Text) >= 0) continue;
            readers.Add(m.Identifier.Text);
            int first = reads.Select(r => r.SpanStart).Concat(aliases.Select(a => a.SpanStart)).Min();
            if (!Guarded(m, first, new HashSet<MethodDeclarationSyntax>())) unguarded.Add(m.Identifier.Text);
        }
        return (readers, unguarded);
    }

    [Fact]
    public void EveryStreamRead_SendsClientWaitingFirst()
    {
        var (readers, unguarded) = Scan(TerminalSource());
        unguarded.Should().BeEmpty("a missed read path would lock a web player's keys");
        readers.Should().BeEquivalentTo(new[] { "GetMaskedInput", "ReadLineInteractiveCore" }, "the scan sees the two methods that read the stream");
    }

    [Fact]
    public void TheGuard_FailsOnAReadWithoutTheFrame()
    {
        string source = TerminalSource();
        const string send = "UsurperRemake.Server.GmcpBridge.EmitWebWaiting(this);";
        source.Should().Contain(send);

        int at = source.LastIndexOf(send, StringComparison.Ordinal);
        string noFrameOnLines = source.Remove(at, send.Length);
        Scan(noFrameOnLines).Unguarded.Should().Contain("ReadLineInteractiveCore", "the line read lost its frame");

        int masked = source.IndexOf(send, StringComparison.Ordinal);
        string noFrameOnMasked = source.Remove(masked, send.Length);
        Scan(noFrameOnMasked).Unguarded.Should().Contain("GetMaskedInput", "the masked read lost its frame");

        const string close = "public async Task<string> ReadLineAsync()";
        string newRead = source.Replace(close, "private async Task<string> NewRead() { return await _streamReader.ReadLineAsync(); }\n    " + close);
        Scan(newRead).Unguarded.Should().Contain("NewRead", "a new stream read must send the frame");

        string afterRead = source.Replace(close, "private async Task<string> Late() { var l = await _streamReader.ReadLineAsync(); " + send + " return l; }\n    " + close);
        Scan(afterRead).Unguarded.Should().Contain("Late", "the frame must come before the read");

        string alias = source.Replace(close, "private async Task<int> Aliased() { var r = _streamReader; return await r.ReadAsync(new char[1], 0, 1); }\n    " + close);
        Scan(alias).Unguarded.Should().Contain("Aliased", "a read through a local copy of the reader is a read");

        string backspace = source.Replace(close, "private string Back() { if (_streamWriter != null && _streamReader != null) { return ReadLineWithBackspace(); } return \"\"; }\n    " + close);
        Scan(backspace).Unguarded.Should().Contain("Back", "ReadLineWithBackspace on a stream path");
    }
}
