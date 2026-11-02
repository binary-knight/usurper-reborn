using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.9 (play test B5): a random street encounter on the way back to Main Street took the keys the player had
/// already typed for Main Street (G for Guild Row went into the encounter, B into the fight). An encounter's own
/// prompt now discards what was typed before it and waits for a key typed on it: keys still on the connection,
/// a line the reader already holds, and a line kept from a pause.
/// </summary>
[Collection("SharedGameSingletons")]
public class EncounterInput139Tests
{
    /// <summary>
    /// A connection: <paramref name="typedBefore"/> has already arrived and reads as available now; the fresh
    /// bytes are what the player types once the encounter is on the screen, one line per read, and never read as
    /// available early.
    /// </summary>
    private sealed class Connection : Stream, IReadableDataStream
    {
        private byte[] _before;
        private readonly byte[] _after;
        private int _b, _a;
        public Connection(string typedBefore, string typedAfter)
        {
            _before = Encoding.UTF8.GetBytes(typedBefore);
            _after = Encoding.UTF8.GetBytes(typedAfter);
        }
        /// <summary>Bytes the player types now: they arrive on the connection and read as available.</summary>
        public void Arrive(string typed)
        {
            var rest = _before.Skip(_b).Concat(Encoding.UTF8.GetBytes(typed)).ToArray();
            _before = rest; _b = 0;
        }
        public bool HasReadableData => _b < _before.Length;
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_b < _before.Length)
            {
                int n = Math.Min(count, _before.Length - _b);
                Array.Copy(_before, _b, buffer, offset, n);
                _b += n;
                return n;
            }
            if (_a >= _after.Length) return 0;
            // one typed line per read: the player types the next line later
            int nl = Array.IndexOf(_after, (byte)'\n', _a);
            int m = Math.Min(count, (nl < 0 ? _after.Length : nl + 1) - _a);
            Array.Copy(_after, _a, buffer, offset, m);
            _a += m;
            return m;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.FromResult(Read(buffer, offset, count));
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var tmp = new byte[buffer.Length];
            int n = Read(tmp, 0, tmp.Length);
            tmp.AsSpan(0, n).CopyTo(buffer.Span);
            return ValueTask.FromResult(n);
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>The screen: when the text <paramref name="trigger"/> is drawn, <paramref name="typed"/> arrives.</summary>
    private sealed class Screen : MemoryStream
    {
        private readonly Connection _c; private readonly string _trigger, _typed; private bool _done;
        public Screen(Connection c, string trigger, string typed) { _c = c; _trigger = trigger; _typed = typed; }
        public override void Write(byte[] buffer, int offset, int count)
        {
            base.Write(buffer, offset, count);
            if (!_done && Encoding.UTF8.GetString(buffer, offset, count).Contains(_trigger)) { _done = true; _c.Arrive(_typed); }
        }
        public override void Write(ReadOnlySpan<byte> buffer) => Write(buffer.ToArray(), 0, buffer.Length);
    }

    private static Character Walker() => new Character
    {
        Name1 = "walker", Name2 = "Walker", AI = CharacterAI.Human, Class = CharacterClass.Warrior, Race = CharacterRace.Human,
        Level = 5, HP = 100, MaxHP = 100, Gold = 500, Chivalry = 0,
    };

    /// <summary>A beggar encounter: G gives 10 gold, I walks past. G is also Guild Row on Main Street.</summary>
    private static async Task<(Character who, EncounterResult result)> Beggar(TerminalEmulator t)
    {
        bool savedPacing = Pacing.Disabled;
        Pacing.Disabled = true;
        try
        {
            var who = Walker();
            var result = new EncounterResult();
            // the beggar, as the encounter roll starts it (ProcessEncounter, where every random encounter starts)
            var m = typeof(StreetEncounterSystem).GetMethod("ProcessEncounter", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var task = (Task)m.Invoke(StreetEncounterSystem.Instance,
                new object[] { who, StreetEncounterSystem.EncounterType.BeggarEncounter, GameLocation.MainStreet, result, t })!;
            (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(30)))).Should().Be(task, "the encounter must finish on the scripted input");
            await task;
            return (who, result);
        }
        finally { Pacing.Disabled = savedPacing; }
    }

    [Fact]
    public async Task KeysTypedBeforeTheEncounter_DoNotAnswerIt()
    {
        var t = new TerminalEmulator(new Connection("G\n", "I\n"), new MemoryStream());
        var (who, result) = await Beggar(t);
        who.Gold.Should().Be(500, "the G typed for Guild Row is not the beggar's G");
        result.Message.Should().Be(Loc.Get("street_encounter.beggar.msg_ignored"), "the answer is the I typed on the prompt");
    }

    [Fact]
    public async Task ALineTheReaderAlreadyHolds_DoesNotAnswerIt()
    {
        // R and G arrived in one read: the R line answered the screen before, the G line waits in the reader's buffer
        var t = new TerminalEmulator(new Connection("R\nG\n", "I\n"), new MemoryStream());
        (await t.GetInput("> ")).Should().Be("R");
        var (who, result) = await Beggar(t);
        who.Gold.Should().Be(500);
        result.Message.Should().Be(Loc.Get("street_encounter.beggar.msg_ignored"));
    }

    [Fact]
    public async Task ALineKeptFromAPause_DoesNotAnswerIt()
    {
        // G typed at the pause before the encounter is kept for the next prompt; the encounter's prompt is not it
        var t = new TerminalEmulator(new Connection("", "G\nI\n"), new MemoryStream());
        await t.PressAnyKey();
        t.HasPendingLine.Should().BeTrue("the pause kept the G for the next prompt");
        var (who, result) = await Beggar(t);
        who.Gold.Should().Be(500);
        result.Message.Should().Be(Loc.Get("street_encounter.beggar.msg_ignored"));
    }

    [Fact]
    public async Task AKeyTypedOnThePrompt_StillAnswersIt()
    {
        var t = new TerminalEmulator(new Connection("", "G\n"), new MemoryStream());
        var (who, result) = await Beggar(t);
        who.Gold.Should().Be(490, "a G typed on the beggar's own prompt gives");
        result.Message.Should().Be(Loc.Get("street_encounter.beggar.msg_gave"));
    }

    [Fact]
    public async Task AKeyTypedWhileTheOptionsDraw_AnswersIt()
    {
        // the encounter has begun; the player reads the options as they appear and types I before the prompt waits
        var c = new Connection("G\n", "");
        var screen = new Screen(c, Loc.Get("street_encounter.beggar.opt_rob"), "I\n");
        var t = new TerminalEmulator(c, screen);
        var (who, result) = await Beggar(t);
        who.Gold.Should().Be(500, "the G from before the encounter is gone");
        result.Message.Should().Be(Loc.Get("street_encounter.beggar.msg_ignored"), "the I typed while the options drew is the answer");
    }

    [Fact]
    public void EveryEncounter_DiscardsEarlierInput_BeforeItsFirstLine()
    {
        var src = File.ReadAllText(Path.Combine(FloorAndStun1113Tests.RepoRoot(), "Scripts", "Systems", "StreetEncounterSystem.cs"));
        int dispatch = src.IndexOf("private async Task ProcessEncounter(");
        int firstCase = src.IndexOf("switch (type)", dispatch);
        src.IndexOf("BeginEncounter(terminal);", dispatch).Should().BeInRange(dispatch, firstCase, "every random encounter starts here");
        Regex.Matches(src, @"await ProcessEncounter\(").Count.Should().Be(1, "the roll is the one way in");
        foreach (var call in new[] { "await ExecuteGrudgeConfrontation(", "await ExecuteSpouseConfrontation(" })
        {
            int at = src.IndexOf(call);
            at.Should().BeGreaterThan(0);
            src.LastIndexOf("BeginEncounter(terminal);", at).Should().BeGreaterThan(src.LastIndexOf("MarkConsequenceFired();", at), "{0} starts after the discard", call);
        }
        Regex.Matches(src, @"FlushPendingInput\(").Count.Should().Be(1, "only BeginEncounter discards; the prompts read what was typed after it");
    }
}
