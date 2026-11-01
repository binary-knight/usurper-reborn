using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 2.0 phase 1: with the web client capability off, every other kind of session sees the same bytes as before it.
///
/// Method. One scripted run draws the screens the change touches: the Main Street menu in the districts layout and in
/// the classic layout (and the classic compact rows), the dungeon room view for two rooms (one quiet with treasure,
/// stairs, an event, features and three kinds of exit; one with monsters), the dungeon fight prompt against one foe and
/// against two, and the PvP fight prompt. Terminal text and GMCP frames go to one stream, as they do on a live
/// connection. The run is made for six sessions that are not a web client: a door or stdio game (no session), the same
/// in the compact (BBS) layout, a telnet MUD session with GMCP on, an SSH session, a screen reader session with GMCP on, and a web terminal session
/// that did not negotiate GMCP.
///
/// The expected bytes in Tests/Fixtures/web200 were written by this same test on origin/main at 0a3b1855, before the
/// change, with USURPER_WEB200_CAPTURE set to the fixture folder (door_compact.bin later, from the same Scripts tree). The test reads only methods that existed there, by
/// reflection, so it builds and runs on both trees. A byte match therefore says the old code and the new code wrote the
/// same terminal text and the same GMCP frames (Room.Info, Char.Vitals and the rest) for these sessions, and that no new
/// package (Client.Menu, Room.Detail) appeared. The second test says the same without the fixtures, so a later text
/// change that makes the fixtures stale still leaves a working guard: no new package name in any of the five streams.
/// To refresh the fixtures after a deliberate text change, run the test on the parent commit with the variable set.
/// </summary>
[Collection("SharedGameSingletons")]
public class WebFlagOff200Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    internal static readonly string[] Kinds = { "door", "door_compact", "mud_gmcp", "ssh", "screenreader", "web_nogmcp" };

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "UsurperReborn.sln"))) dir = Path.GetDirectoryName(dir);
        return dir!;
    }

    internal static Character Hero(bool screenReader, bool classic)
    {
        var c = new Character
        {
            Name1 = "webtest", Name2 = "Webtest", AI = CharacterAI.Human, Class = CharacterClass.Warrior, Race = CharacterRace.Human,
            Level = 12, HP = 400, MaxHP = 400, BaseMaxHP = 400, Strength = 60, BaseStrength = 60, Defence = 20, BaseDefence = 20,
            Dexterity = 30, BaseDexterity = 30, Agility = 20, BaseAgility = 20, Constitution = 30, BaseConstitution = 30,
            Stamina = 100, BaseStamina = 100, Mental = 80, Gold = 1234, Healing = 3, MKills = 25, Experience = 0,
            CombatSpeed = CombatSpeed.Instant, ScreenReaderMode = screenReader, ClassicMainStreet = classic,
        };
        c.HintsShown.Add("menu_tier_1"); c.HintsShown.Add("menu_tier_2"); c.HintsShown.Add("menu_tier_3");
        return c;
    }

    internal static (DungeonLocation d, DungeonFloor floor) Dungeon(Character hero, TerminalEmulator term)
    {
        DungeonRoom R(string id, bool explored, bool cleared) => new()
        {
            Id = id, Name = "Room " + id, Description = "A cold stone room.", AtmosphereText = "Water drips somewhere.",
            DangerRating = 2, IsExplored = explored, IsCleared = cleared,
        };
        var quiet = R("q1", true, true);
        quiet.HasTreasure = true; quiet.HasStairsDown = true; quiet.HasEvent = true; quiet.EventType = DungeonEventType.Shrine;
        quiet.Features.Add(new RoomFeature("a cracked altar", "Old stone.", FeatureInteraction.Examine));
        quiet.Features.Add(new RoomFeature("a rusted lever", "It might move.", FeatureInteraction.Use));
        quiet.Features.Add(new RoomFeature("a used thing", "Done.", FeatureInteraction.Examine) { IsInteracted = true });
        quiet.Exits[Direction.North] = new RoomExit("c2", "a stone arch");
        quiet.Exits[Direction.East] = new RoomExit("u3", "a dark tunnel");
        quiet.Exits[Direction.West] = new RoomExit("e4", "a narrow stair");
        quiet.Exits[Direction.South] = new RoomExit("h5", "a crack in the wall") { IsHidden = true, IsRevealed = false };
        var fight = R("m6", true, false);
        fight.HasMonsters = true; fight.DangerRating = 3;
        fight.Exits[Direction.South] = new RoomExit("q1", "a stone arch");
        var floor = new DungeonFloor { Level = 6, Theme = DungeonTheme.Catacombs, CurrentRoomId = "q1", EntranceRoomId = "q1", DangerLevel = 4 };
        floor.Rooms.Add(quiet);
        floor.Rooms.Add(R("c2", true, true));
        var unknown = R("u3", false, false);
        unknown.HasTrap = true; unknown.HasMonsters = true; unknown.HasTreasure = true; unknown.Name = "Secret Treasury";
        floor.Rooms.Add(unknown);
        floor.Rooms.Add(R("e4", true, false));
        floor.Rooms.Add(R("h5", false, false));
        floor.Rooms.Add(fight);
        var d = new DungeonLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(d, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(d, hero);
        typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.SetValue(d, 6);
        typeof(DungeonLocation).GetField("currentFloor", F)!.SetValue(d, floor);
        typeof(DungeonLocation).GetField("inRoomMode", F)!.SetValue(d, true);
        typeof(DungeonLocation).GetField("dungeonRandom", F)!.SetValue(d, new Random(7));
        return (d, floor);
    }

    internal static Monster Foe(string name) => new Monster
    {
        Name = name, Level = 10, HP = 300, MaxHP = 300, Strength = 20, Defence = 5, WeapPow = 10, ArmPow = 5, IsActive = true,
    };

    /// <summary>A session of the given kind, current for the run; null for the door or stdio game.</summary>
    internal static SessionContext? Session(string kind, Stream stream, Character hero) => kind switch
    {
        "door" => null,
        "door_compact" => null,
        "mud_gmcp" => new SessionContext { Username = "webtest", CharacterKey = "webtest", OutputStream = stream, ConnectionType = "MUD", GmcpEnabled = true, Player = hero },
        "ssh" => new SessionContext { Username = "webtest", CharacterKey = "webtest", OutputStream = stream, ConnectionType = "SSH", GmcpEnabled = false, Player = hero },
        "screenreader" => new SessionContext { Username = "webtest", CharacterKey = "webtest", OutputStream = stream, ConnectionType = "MUD", GmcpEnabled = true, Player = hero },
        "web_nogmcp" => new SessionContext { Username = "webtest", CharacterKey = "webtest", OutputStream = stream, ConnectionType = "Web", GmcpEnabled = false, Player = hero },
        "web" => new SessionContext { Username = "webtest", CharacterKey = "webtest", OutputStream = stream, ConnectionType = "Web", GmcpEnabled = true, Player = hero },
        _ => throw new ArgumentException(kind),
    };

    private static object? Invoke(object target, Type type, string method, params object?[] args)
    {
        try { return type.GetMethod(method, F)!.Invoke(target, args); }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
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
        try
        {
            GameConfig.CompactMode = kind == "door_compact"; // the compact (BBS) room view and fight menus
            var hero = Hero(sr, classic: false);
            SessionContext.Current = Session(kind, stream, hero);
            GameConfig.Language = "en";
            GameConfig.ScreenReaderMode = sr;
            TerminalEmulator Term(string input)
            {
                var t = new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes(input)), stream);
                if (sr) t.IsPlainText = true;
                return t;
            }

            // Main Street, districts then classic, and the classic compact rows
            foreach (bool classic in new[] { false, true })
            {
                hero.ClassicMainStreet = classic;
                var street = new MainStreetLocation();
                var t = Term("");
                typeof(BaseLocation).GetField("terminal", F)!.SetValue(street, t);
                typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(street, hero);
                Invoke(street, typeof(MainStreetLocation), "ShowMainStreetMenu");
                if (classic) Invoke(street, typeof(MainStreetLocation), "WriteClassicLayoutBBSMenu");
                t.StreamWriterInternal?.Flush();
            }
            hero.ClassicMainStreet = false;

            // The dungeon room view: the quiet room, then the room with monsters
            {
                var t = Term("");
                var (d, floor) = Dungeon(hero, t);
                Invoke(d, typeof(DungeonLocation), "DisplayLocation");
                floor.CurrentRoomId = "m6";
                Invoke(d, typeof(DungeonLocation), "DisplayLocation");
                t.StreamWriterInternal?.Flush();
            }

            // The dungeon fight prompt, one foe then two; D (defend) is taken and returns
            foreach (int foes in new[] { 1, 2 })
            {
                var t = Term("D\n");
                var engine = new CombatEngine(t);
                var monsters = Enumerable.Range(1, foes).Select(i => Foe("Ghoul " + i)).ToList();
                typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, hero);
                typeof(CombatEngine).GetField("_combatOwner", F)!.SetValue(engine, hero);
                typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, new List<Character>());
                var result = new CombatResult { Player = hero, Teammates = new List<Character>(), CombatLog = new List<string>(), CurrentRound = 1 };
                var task = (Task<(CombatAction action, bool enableAutoCombat)>)Invoke(engine, typeof(CombatEngine), "GetPlayerActionMultiMonster", hero, monsters, result)!;
                (await task).action.Type.Should().Be(CombatActionType.Defend);
                t.StreamWriterInternal?.Flush();
            }

            // The PvP fight prompt
            {
                var t = Term("D\n");
                var engine = new CombatEngine(t);
                var foe = Hero(false, false);
                foe.Name1 = foe.Name2 = "Rival";
                typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, hero);
                var result = new CombatResult { Player = hero, Teammates = new List<Character>(), CombatLog = new List<string>(), CurrentRound = 1 };
                var task = (Task<CombatAction>)Invoke(engine, typeof(CombatEngine), "GetPlayerAction", hero, null, result, foe)!;
                (await task).Type.Should().Be(CombatActionType.Defend);
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
        }
    }

    /// <summary>The GMCP package names in a byte stream, in order.</summary>
    internal static List<string> Packages(byte[] bytes)
    {
        var names = new List<string>();
        for (int i = 0; i + 2 < bytes.Length; i++)
        {
            if (bytes[i] != 0xFF || bytes[i + 1] != 0xFA || bytes[i + 2] != 0xC9) continue;
            int start = i + 3, end = start;
            while (end < bytes.Length && bytes[end] != 0x20 && bytes[end] != 0xFF) end++;
            names.Add(Encoding.UTF8.GetString(bytes, start, end - start));
            i = end;
        }
        return names;
    }

    [Theory]
    [InlineData("door")]
    [InlineData("door_compact")]
    [InlineData("mud_gmcp")]
    [InlineData("ssh")]
    [InlineData("screenreader")]
    [InlineData("web_nogmcp")]
    public async Task FlagOff_TheRunIsByteIdenticalToTheCodeBeforeTheChange(string kind)
    {
        var bytes = await Run(kind);
        string fixtures = Path.Combine(RepoRoot(), "Tests", "Fixtures", "web200");
        string? capture = Environment.GetEnvironmentVariable("USURPER_WEB200_CAPTURE");
        if (!string.IsNullOrEmpty(capture))
        {
            Directory.CreateDirectory(capture);
            File.WriteAllBytes(Path.Combine(capture, kind + ".bin"), bytes);
            return;
        }
        var expected = File.ReadAllBytes(Path.Combine(fixtures, kind + ".bin"));
        if (!bytes.SequenceEqual(expected))
        {
            string actual = Path.Combine(Path.GetTempPath(), "web200-" + kind + ".actual.bin");
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
    public async Task FlagOff_NoNewPackageAppears(string kind)
    {
        var packages = await Run(kind).ContinueWith(t => Packages(t.Result));
        packages.Should().NotContain("Client.Menu").And.NotContain("Room.Detail");
        var before = new[] { "Room.Info", "Char.Vitals", "Char.Combat.Party", "Char.Status", "Char.StatusVars" };
        packages.Where(p => !before.Contains(p)).Should().BeEmpty("only packages that existed before the change");
        if (kind == "mud_gmcp" || kind == "screenreader")
            packages.Should().Contain("Room.Info").And.Contain("Char.Vitals", "the run does reach the GMCP paths");
        else
            packages.Should().BeEmpty("no GMCP was negotiated");
    }

    [Fact]
    public async Task TheRunIsDeterministic()
    {
        foreach (var kind in Kinds)
            (await Run(kind)).Should().Equal(await Run(kind), "two runs of {0} must write the same bytes, or a fixture compare means nothing", kind);
    }
}
