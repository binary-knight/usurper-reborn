using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.0 lost stat rewards, piece 2: the PERMANENT stat rewards and penalties in Systems go
/// through GrantPermanentStat. Per system group a real site is driven, and its change survives
/// the full RecalculateStats at the start of a monster fight and a save round trip. A source scan
/// checks that no converted method still writes a derived stat.
/// </summary>
[Collection("SharedGameSingletons")]
public class StatRewardsSystems1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags FS = BindingFlags.NonPublic | BindingFlags.Static;

    private static TerminalEmulator Term(string script) =>
        new TerminalEmulator(new ScriptedStream(script + string.Concat(Enumerable.Repeat("\n", 40))), new MemoryStream());

    /// <summary>Starts a monster fight (its full RecalculateStats) and escapes with a smoke bomb.</summary>
    private static async Task FightStart(Character c)
    {
        c.SmokeBombs = 1;
        c.CombatSpeed = CombatSpeed.Instant;
        var engine = new CombatEngine(new TerminalEmulator(new ScriptedStream(string.Concat(Enumerable.Repeat("R\n", 8))), new MemoryStream()));
        engine.SeedRandomForTests(1115);
        var dummy = new Monster { Name = "Training Dummy", Level = 1, HP = 5_000_000, MaxHP = 5_000_000, Strength = 0, Defence = 0, Experience = 1, Gold = 0 };
        await engine.PlayerVsMonsters(c, new List<Monster> { dummy });
    }

    /// <summary>The change is in Base, and the derived value survives a fight start and a save round trip.</summary>
    private static async Task ShouldLast(Character c, StatKind stat, long expectedBase, string what)
    {
        c.GetBaseStat(stat).Should().Be(expectedBase, $"{what} is written to the Base field");
        long derived = StatRewards1115Tests.Derived(c, stat);
        await FightStart(c);
        StatRewards1115Tests.Derived(c, stat).Should().Be(derived, $"{what} survives the start of a monster fight");
        var r = StatRewards1115Tests.RoundTrip(c);
        r.GetBaseStat(stat).Should().Be(expectedBase);
        StatRewards1115Tests.Derived(r, stat).Should().Be(derived, $"{what} survives a save round trip");
    }

    /// <summary>A Random whose rolls are fixed: NextDouble returns <c>d</c>, Next returns its lower bound.</summary>
    private sealed class FixedRandom : Random
    {
        private readonly double _d;
        public FixedRandom(double d) { _d = d; }
        public override double NextDouble() => _d;
        public override int Next(int maxValue) => 0;
        public override int Next(int minValue, int maxValue) => minValue;
    }

    private static async Task Encounter(string method, Character c, string script, double roll = 0.0)
    {
        var field = typeof(RareEncounters).GetField("random", FS)!;
        var old = field.GetValue(null);
        field.SetValue(null, new FixedRandom(roll));
        try
        {
            var m = typeof(RareEncounters).GetMethod(method, FS);
            m.Should().NotBeNull($"{method} must exist");
            try { await (Task)m!.Invoke(null, new object[] { Term(script), c, 10 })!; }
            catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
        }
        finally { field.SetValue(null, old); }
    }

    // ---------------- RareEncounters ----------------

    [Fact]
    public async Task RareEncounter_TheObsidianMirrorGrant_Lasts()
    {
        var c = StatRewards1115Tests.Fresh("SrsMirror");
        await Encounter("ObsidianMirrorEncounter", c, "");
        await ShouldLast(c, StatKind.Strength, 12, "the mirror Strength");
        await ShouldLast(c, StatKind.Intelligence, 22, "the mirror Intelligence");
        await ShouldLast(c, StatKind.Dexterity, 12, "the mirror Dexterity");
    }

    [Fact]
    public async Task RareEncounter_TheFireElementalMaxMana_Lasts_ForACaster()
    {
        var c = StatRewards1115Tests.Fresh("SrsFire", CharacterClass.Magician);
        await Encounter("FireElementalEncounter", c, "");
        await ShouldLast(c, StatKind.Intelligence, 23, "the elemental Intelligence");
        await ShouldLast(c, StatKind.MaxMana, 120, "the elemental Max Mana");
    }

    [Fact]
    public async Task RareEncounter_TheMadnessPoolPenalty_Lasts()
    {
        var c = StatRewards1115Tests.Fresh("SrsMadness");
        await Encounter("MadnessPoolEncounter", c, "L\n", roll: 0.9);
        await ShouldLast(c, StatKind.Intelligence, 15, "the madness Intelligence penalty");
    }

    [Fact]
    public async Task RareEncounter_APenalty_StopsAtTheFloor()
    {
        var c = StatRewards1115Tests.Fresh("SrsFloor");
        c.GrantPermanentStat(StatKind.Intelligence, -17);   // Base 3
        await Encounter("MadnessPoolEncounter", c, "L\n", roll: 0.9);
        await ShouldLast(c, StatKind.Intelligence, 1, "a penalty floored at 1");
    }

    // ---------------- source scan ----------------

    private static readonly Regex DerivedWrite = new(
        @"\b(player|prisoner)\.(Strength|Dexterity|Constitution|Intelligence|Wisdom|Charisma|Defence|Stamina|Agility|MaxHP|MaxMana)\s*(\+=|-=|\+\+|--|=\s*Math\.(Max|Min)\()");

    [Fact]
    public void RareEncounters_WritesNoDerivedStat()
    {
        string src = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts/Systems/RareEncounters.cs"));
        DerivedWrite.Matches(src).Select(m => m.Value).Should().BeEmpty("every rare encounter stat change goes through GrantPermanentStat");
        Regex.Matches(src, @"GrantPermanentStat\(").Count.Should().Be(58, "54 grants and 4 penalties");
    }

    // ---------------- helpers ----------------

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "UsurperReborn.sln"))) dir = Path.GetDirectoryName(dir);
        return dir!;
    }

    /// <summary>The text of a method from its signature to the next member at the same indent.</summary>
    private static string Body(string src, string signature)
    {
        int start = src.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"{signature} must exist");
        int lineStart = src.LastIndexOf('\n', start) + 1;
        string indent = src.Substring(lineStart, start - lineStart);
        var next = new Regex(@"\n" + indent + @"(private|public|internal|protected) ").Match(src, start + signature.Length);
        return next.Success ? src.Substring(start, next.Index - start) : src.Substring(start);
    }

    /// <summary>Serves the scripted bytes once, then reports end of input.</summary>
    private sealed class ScriptedStream : Stream
    {
        private readonly byte[] _data;
        private int _pos;
        public ScriptedStream(string script) { _data = Encoding.UTF8.GetBytes(script); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos >= _data.Length) return 0;
            int n = Math.Min(count, _data.Length - _pos);
            Array.Copy(_data, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => Task.FromResult(Read(buffer, offset, count));
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
