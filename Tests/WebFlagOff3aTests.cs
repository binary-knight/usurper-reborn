using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 2.0 phase 3a: with the web client capability off, the screens phase 3a touches write the same bytes as before it.
///
/// Method, as in WebFlagOff200Tests. One scripted run draws the status line, the Character Status sheet (all its
/// pages), the dungeon map screen (the M key) and a whole fight against two foes of a known family, with terminal text
/// and GMCP frames on one stream as on a live connection. The run is made for the same six sessions that are not a web
/// client (door, door compact, telnet MUD with GMCP, SSH, screen reader with GMCP, web terminal without GMCP).
///
/// The expected bytes in Tests/Fixtures/web200p3 were written by this same test against the Scripts tree of origin/main
/// at a6a8a85e (a copy of the branch's first commit, 8cee362b, which changes nothing under Scripts), with
/// USURPER_WEB3A_CAPTURE set to the fixture folder. The test reads only methods that existed there, by reflection. A byte match says the old code and the new
/// code wrote the same terminal text and the same GMCP frames for these sessions, and no new package appeared. The
/// second test says the same without the fixtures: none of the new packages in any stream.
/// </summary>
[Collection("SharedGameSingletons")]
public class WebFlagOff3aTests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    internal static readonly string[] NewPackages = { "Client.Enemies", "Client.Footer", "Client.Sheet", "Client.Map" };

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "UsurperReborn.sln"))) dir = Path.GetDirectoryName(dir);
        return dir!;
    }

    /// <summary>Serves the scripted bytes once, then reports end of input.</summary>
    internal sealed class ScriptedStream : Stream
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
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.FromResult(Read(buffer, offset, count));
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

    /// <summary>Two foes of a family that has art, so a web client run would name it.</summary>
    internal static List<Monster> Foes() => new()
    {
        new Monster { Name = "Bone Guard A", Level = 8, HP = 260, MaxHP = 260, Strength = 40, Defence = 5, Experience = 1, Gold = 0, IsActive = true, FamilyName = "Undead" },
        new Monster { Name = "Bone Guard B", Level = 8, HP = 260, MaxHP = 260, Strength = 40, Defence = 5, Experience = 1, Gold = 0, IsActive = true, FamilyName = "Undead" },
    };

    /// <summary>Attack, Enter for a target, and Pass at the loot prompts after the fight.</summary>
    internal static string Attacks(int n = 30) => string.Concat(Enumerable.Repeat("A\n\n", n)) + string.Concat(Enumerable.Repeat("P\n", 10));

    /// <summary>A hero with gear in some slots and none in others, so the sheet prints names, stats and Empty.</summary>
    internal static Character Hero(bool screenReader)
    {
        var h = WebFlagOff200Tests.Hero(screenReader, classic: false);
        h.Experience = 1500;
        h.Intelligence = 20; h.BaseIntelligence = 20; h.Wisdom = 20; h.BaseWisdom = 20; h.Charisma = 15; h.BaseCharisma = 15;
        h.Age = 22; h.Height = 180; h.Weight = 80;
        h.AutoHeal = false;
        return h;
    }

    private static object? Invoke(object target, Type type, string method, params object?[] args)
    {
        try { return type.GetMethod(method, F)!.Invoke(target, args); }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    private static async Task Await(object? task, string what)
    {
        var t = (Task)task!;
        var done = await Task.WhenAny(t, Task.Delay(TimeSpan.FromSeconds(60)));
        done.Should().Be(t, "{0} must finish on the scripted input", what);
        await t;
    }

    /// <summary>The scripted run for one kind of session: every byte the session's connection would carry.</summary>
    internal static async Task<byte[]> Run(string kind)
    {
        var stream = new MemoryStream();
        bool sr = kind == "screenreader";
        var savedCtx = SessionContext.Current;
        string savedLang = GameConfig.Language;
        bool savedSrGlobal = GameConfig.ScreenReaderMode;
        bool savedCompact = GameConfig.CompactMode;
        bool savedPacing = Pacing.Disabled;
        try
        {
            // The fight unlocks achievements once their table is loaded, which another test may or may not have done
            // first; load it here so the run writes the same bytes in any test order.
            AchievementSystem.Initialize();
            Pacing.Disabled = true;
            GameConfig.CompactMode = kind == "door_compact";
            var hero = Hero(sr);
            var ctx = WebFlagOff200Tests.Session(kind, stream, hero);
            ctx?.InitializeSystems();   // the per-session systems the sheet and the fight read
            SessionContext.Current = ctx;
            GameConfig.Language = "en";
            GameConfig.ScreenReaderMode = sr;
            TerminalEmulator Term(string input)
            {
                var t = new TerminalEmulator(new ScriptedStream(input), stream);
                if (sr) t.IsPlainText = true;
                return t;
            }

            // The status line, on Main Street
            {
                var t = Term("");
                var street = new MainStreetLocation();
                typeof(BaseLocation).GetField("terminal", F)!.SetValue(street, t);
                typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(street, hero);
                Invoke(street, typeof(BaseLocation), "ShowStatusLine");
                t.StreamWriterInternal?.Flush();
            }

            // The Character Status sheet, all four pages
            {
                var t = Term(string.Concat(Enumerable.Repeat("\n", 8)));
                var street = new MainStreetLocation();
                typeof(BaseLocation).GetField("terminal", F)!.SetValue(street, t);
                typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(street, hero);
                await Await(Invoke(street, typeof(BaseLocation), "ShowStatus"), "the status sheet");
                t.StreamWriterInternal?.Flush();
            }

            // The dungeon map screen
            {
                var t = Term(string.Concat(Enumerable.Repeat("\n", 8)));
                var (d, _) = WebFlagOff200Tests.Dungeon(hero, t);
                await Await(Invoke(d, typeof(DungeonLocation), "ShowDungeonMap"), "the map screen");
                t.StreamWriterInternal?.Flush();
            }

            // A whole fight against two foes
            {
                var t = Term(Attacks());
                var engine = new CombatEngine(t);
                typeof(CombatEngine).GetMethod("SeedRandomForTests", F)!.Invoke(engine, new object[] { 314 });
                var fight = engine.PlayerVsMonsters(hero, Foes(), offerMonkEncounter: false);
                await Await(fight, "the fight");
                t.StreamWriterInternal?.Flush();
            }
            return stream.ToArray();
        }
        finally
        {
            SessionContext.Current = savedCtx;
            GameConfig.Language = savedLang;
            GameConfig.ScreenReaderMode = savedSrGlobal;
            GameConfig.CompactMode = savedCompact;
            Pacing.Disabled = savedPacing;
        }
    }

    [Theory]
    [InlineData("door")]
    [InlineData("door_compact")]
    [InlineData("mud_gmcp")]
    [InlineData("ssh")]
    [InlineData("screenreader")]
    [InlineData("web_nogmcp")]
    public async Task FlagOff3a_TheRunIsByteIdenticalToTheCodeBeforeTheChange(string kind)
    {
        var bytes = await Run(kind);
        string fixtures = Path.Combine(RepoRoot(), "Tests", "Fixtures", "web200p3");
        string? capture = Environment.GetEnvironmentVariable("USURPER_WEB3A_CAPTURE");
        if (!string.IsNullOrEmpty(capture))
        {
            Directory.CreateDirectory(capture);
            File.WriteAllBytes(Path.Combine(capture, kind + ".bin"), bytes);
            return;
        }
        var expected = File.ReadAllBytes(Path.Combine(fixtures, kind + ".bin"));
        if (!bytes.SequenceEqual(expected))
        {
            string actual = Path.Combine(Path.GetTempPath(), "web3a-" + kind + ".actual.bin");
            File.WriteAllBytes(actual, bytes);
            bytes.Should().Equal(expected, "the {0} run must match the bytes from before the change; this run was written to {1}", kind, actual);
        }
    }

    [Theory]
    [InlineData("door")]
    [InlineData("door_compact")]
    [InlineData("mud_gmcp")]
    [InlineData("ssh")]
    [InlineData("screenreader")]
    [InlineData("web_nogmcp")]
    public async Task FlagOff3a_NoNewPackageAppears(string kind)
    {
        var packages = WebFlagOff200Tests.Packages(await Run(kind));
        foreach (var p in NewPackages.Concat(new[] { "Client.Menu", "Room.Detail" })) packages.Should().NotContain(p);
        if (kind == "mud_gmcp" || kind == "screenreader")
            packages.Should().Contain("Char.Combat.Start").And.Contain("Char.Combat.Enemies", "the run does reach the fight's GMCP paths");
        else
            packages.Should().BeEmpty("no GMCP was negotiated");
    }

    [Fact]
    public async Task FlagOff3a_TheRunIsDeterministic()
    {
        foreach (var kind in WebFlagOff200Tests.Kinds)
            (await Run(kind)).Should().Equal(await Run(kind), "two runs of {0} must write the same bytes, or a fixture compare means nothing", kind);
    }
}
