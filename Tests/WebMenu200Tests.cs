using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using UsurperRemake;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 2.0 phase 1: the web client capability (SessionContext.WebClient) and its two GMCP packages, Client.Menu (the keys a
/// screen takes) and Room.Detail (what the dungeon room text shows). The flag off case is WebFlagOff200Tests; the key
/// behaviour after the dungeon refactor is WebKeys200Tests.
/// </summary>
[Collection("SharedGameSingletons")]
public class WebMenu200Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly string[] GlobalKeys = { "%", "*", "~", "0", "?", "!" };

    // ------------------------------------------------------------------ plumbing

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "UsurperReborn.sln"))) dir = Path.GetDirectoryName(dir);
        return dir!;
    }

    private static string Source(string file) => File.ReadAllText(Path.Combine(RepoRoot(), file));

    private static string Plain(byte[] bytes) =>
        Regex.Replace(Encoding.UTF8.GetString(StripFrames(bytes)), "\u001b\\[[0-9;?]*[A-Za-z]", "").Replace("\r", "");

    /// <summary>The bytes with every GMCP frame removed: the terminal text alone.</summary>
    private static byte[] StripFrames(byte[] bytes)
    {
        var o = new List<byte>(bytes.Length);
        for (int i = 0; i < bytes.Length; i++)
        {
            if (i + 2 < bytes.Length && bytes[i] == 0xFF && bytes[i + 1] == 0xFA && bytes[i + 2] == 0xC9)
            {
                int end = i + 3;
                while (end + 1 < bytes.Length && !(bytes[end] == 0xFF && bytes[end + 1] == 0xF0)) end++;
                i = end + 1;
                continue;
            }
            o.Add(bytes[i]);
        }
        return o.ToArray();
    }

    /// <summary>Every GMCP frame in a byte stream: its package and its JSON.</summary>
    internal static List<(string Package, JsonElement Json)> Frames(byte[] bytes)
    {
        var frames = new List<(string, JsonElement)>();
        for (int i = 0; i + 2 < bytes.Length; i++)
        {
            if (bytes[i] != 0xFF || bytes[i + 1] != 0xFA || bytes[i + 2] != 0xC9) continue;
            int start = i + 3, end = start;
            while (end + 1 < bytes.Length && !(bytes[end] == 0xFF && bytes[end + 1] == 0xF0)) end++;
            string body = Encoding.UTF8.GetString(bytes, start, end - start);
            int sp = body.IndexOf(' ');
            string pkg = sp < 0 ? body : body.Substring(0, sp);
            var json = sp < 0 ? default : JsonDocument.Parse(body.Substring(sp + 1)).RootElement.Clone();
            frames.Add((pkg, json));
            i = end + 1;
        }
        return frames;
    }

    private static List<JsonElement> Of(byte[] bytes, string package) => Frames(bytes).Where(f => f.Package == package).Select(f => f.Json).ToList();

    private static List<(string Key, string Label, bool Enabled, string Group)> Items(JsonElement menu) =>
        menu.GetProperty("groups").EnumerateArray().SelectMany(g => g.GetProperty("items").EnumerateArray().Select(i =>
            (i.GetProperty("key").GetString()!, i.GetProperty("label").GetString()!, i.GetProperty("enabled").GetBoolean(), g.GetProperty("label").GetString()!))).ToList();

    /// <summary>A session of the given kind made current for the scope; the stream carries both text and GMCP.</summary>
    private sealed class Scope : IDisposable
    {
        private readonly SessionContext? _saved = SessionContext.Current;
        private readonly string _lang = GameConfig.Language;
        private readonly bool _pacing = Pacing.Disabled;
        private readonly bool _sr = GameConfig.ScreenReaderMode;
        public readonly MemoryStream Stream = new();
        public readonly SessionContext? Ctx;
        public Scope(string kind, Character hero, string lang = "en")
        {
            Pacing.Disabled = true;
            Ctx = WebFlagOff200Tests.Session(kind, Stream, hero);
            SessionContext.Current = Ctx;
            GameConfig.Language = lang;
            GameConfig.ScreenReaderMode = false;
        }
        public TerminalEmulator Term(string input = "") =>
            new(new MemoryStream(Encoding.UTF8.GetBytes(input + string.Concat(Enumerable.Repeat("R\n", 20)))), Stream);
        public byte[] Bytes(TerminalEmulator t) { t.StreamWriterInternal?.Flush(); return Stream.ToArray(); }
        public void Dispose()
        {
            SessionContext.Current = _saved;
            GameConfig.Language = _lang;
            Pacing.Disabled = _pacing;
            GameConfig.ScreenReaderMode = _sr;
        }
    }

    private static object? Invoke(object target, Type type, string method, params object?[] args)
    {
        try { return type.GetMethod(method, F)!.Invoke(target, args); }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    private static MainStreetLocation Street(Character hero, TerminalEmulator t)
    {
        var street = new MainStreetLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(street, t);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(street, hero);
        return street;
    }

    private static Character Hero(int level = 12, bool classic = false, CharacterClass cls = CharacterClass.Warrior)
    {
        var h = WebFlagOff200Tests.Hero(false, classic);
        h.Level = level;
        h.Class = cls;
        return h;
    }

    /// <summary>The string case labels of every switch statement in a method (Roslyn), in source order.</summary>
    internal static List<string> CaseLabels(string file, string method)
    {
        var root = CSharpSyntaxTree.ParseText(Source(file)).GetRoot();
        var m = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(x => x.Identifier.Text == method);
        return m.DescendantNodes().OfType<SwitchStatementSyntax>()
            .SelectMany(s => s.Sections.SelectMany(sec => sec.Labels.OfType<CaseSwitchLabelSyntax>()))
            .Select(l => l.Value).OfType<LiteralExpressionSyntax>().Select(l => l.Token.ValueText).ToList();
    }

    // ------------------------------------------------------------------ the flag

    [Theory]
    [InlineData("Web", true, true)]
    [InlineData("Web", false, false)]
    [InlineData("MUD", true, false)]
    [InlineData("SSH", true, false)]
    [InlineData("BBS", true, false)]
    [InlineData("web", true, false)]
    [InlineData("Electron", true, false)]
    public void WebClient_IsWebAndGmcp_Only(string type, bool gmcp, bool expected)
    {
        new SessionContext { ConnectionType = type, GmcpEnabled = gmcp }.WebClient.Should().Be(expected);
    }

    [Fact]
    public void WebClient_FollowsGmcp_WhenEmitErrorsTurnItOff()
    {
        var ctx = new SessionContext { ConnectionType = "Web", GmcpEnabled = true };
        ctx.WebClient.Should().BeTrue();
        ctx.GmcpEnabled = false;
        ctx.WebClient.Should().BeFalse("the flag is computed, so it follows GmcpEnabled");
    }

    [Theory]
    [InlineData("mud_gmcp")]
    [InlineData("ssh")]
    [InlineData("screenreader")]
    [InlineData("web_nogmcp")]
    [InlineData("door")]
    public void EmitWebClient_SendsNothing_ToASessionThatIsNotTheWebClient(string kind)
    {
        using var s = new Scope(kind, Hero());
        GmcpBridge.IsWebClientActive.Should().BeFalse();
        GmcpBridge.EmitWebClient("Client.Menu", new { screen = "x" });
        WebClientMenu.Emit("x", () => throw new InvalidOperationException("the builder must not run for this session"));
        s.Stream.Length.Should().Be(0);
    }

    [Fact]
    public void EmitWebClient_SendsTheFrame_ToTheWebClient()
    {
        using var s = new Scope("web", Hero());
        GmcpBridge.IsWebClientActive.Should().BeTrue();
        GmcpBridge.EmitWebClient("Client.Menu", new { screen = "x" });
        Of(s.Stream.ToArray(), "Client.Menu").Should().ContainSingle().Which.GetProperty("screen").GetString().Should().Be("x");
    }

    [Fact]
    public void TheWebClientPackages_AreRefusedByEmitTo()
    {
        GmcpBridge.IsWebClientPackage("Client.Menu").Should().BeTrue();
        GmcpBridge.IsWebClientPackage("Room.Detail").Should().BeTrue();
        GmcpBridge.IsWebClientPackage("Client.Waiting").Should().BeTrue();
        GmcpBridge.IsWebClientPackage("Room.Info").Should().BeFalse();
        GmcpBridge.IsWebClientPackage("Comm.Channel.Text").Should().BeFalse();
        var emitTo = Source("Scripts/Server/GmcpBridge.cs");
        var body = emitTo.Substring(emitTo.IndexOf("public static void EmitTo(", StringComparison.Ordinal));
        body = body.Substring(0, body.IndexOf("TryWriteFrame(", StringComparison.Ordinal));
        body.Should().Contain("if (IsWebClientPackage(package)) return;", "EmitTo turns the web client packages away before any write");
    }

    [Fact]
    public void EmitWebClient_TakesNoSession_AndNoGameRuleReadsTheFlag()
    {
        var m = typeof(GmcpBridge).GetMethod("EmitWebClient")!;
        m.GetParameters().Select(p => p.ParameterType).Should().Equal(new[] { typeof(string), typeof(object) }, "it can only write to the current session");
        // the flag is read only by the bridge (to decide whether to send); no game code branches on it
        foreach (var file in Directory.GetFiles(Path.Combine(RepoRoot(), "Scripts"), "*.cs", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(RepoRoot(), file).Replace('\\', '/');
            string src = File.ReadAllText(file);
            if (rel != "Scripts/Server/GmcpBridge.cs" && rel != "Scripts/Server/SessionContext.cs")
                Regex.IsMatch(src, @"\.WebClient\b").Should().BeFalse("{0} must not read SessionContext.WebClient", rel);
        }
    }

    // ------------------------------------------------------------------ flag on: each screen sends its menu

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MainStreet_SendsOneClientMenu_ToTheWebClient(bool classic)
    {
        var hero = Hero(classic: classic);
        using var s = new Scope("web", hero);
        var t = s.Term();
        Invoke(Street(hero, t), typeof(MainStreetLocation), "ShowMainStreetMenu");
        var menus = Of(s.Bytes(t), "Client.Menu");
        menus.Should().ContainSingle();
        menus[0].GetProperty("screen").GetString().Should().Be(classic ? "main_street_classic" : "main_street");
        Items(menus[0]).Select(i => i.Key).Should().Contain(new[] { "D", "I", "Q", "~" });
        Of(s.Bytes(t), "Room.Detail").Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MainStreet_OnTheClassicDefault_SendsTheClassicClientMenu(bool choseDistricts)
    {
        // 2.0: a character whose layout preference was never set; the web client gets the classic menu
        var hero = new Character
        {
            Name1 = "webdefault", Name2 = "Webdefault", AI = CharacterAI.Human, Class = CharacterClass.Warrior, Race = CharacterRace.Human,
            Level = 12, HP = 400, MaxHP = 400, BaseMaxHP = 400, MKills = 25, CombatSpeed = CombatSpeed.Instant,
        };
        hero.HintsShown.UnionWith(new[] { "menu_tier_1", "menu_tier_2", "menu_tier_3" });
        if (choseDistricts) hero.DistrictsMainStreet = true;
        using var s = new Scope("web", hero);
        var t = s.Term();
        Invoke(Street(hero, t), typeof(MainStreetLocation), "ShowMainStreetMenu");
        var screens = Of(s.Bytes(t), "Client.Menu").Select(m => m.GetProperty("screen").GetString()).ToList();
        if (choseDistricts)
            screens.Should().Equal(new[] { "main_street" }, "a player who chose the districts gets the districts menu");
        else
            screens.Should().Equal(new[] { "main_street_classic" }, "the classic default sends the classic menu and never main_street");
    }

    [Fact]
    public void DungeonRoom_SendsRoomDetailAndClientMenu_ToTheWebClient()
    {
        var hero = Hero();
        using var s = new Scope("web", hero);
        var t = s.Term();
        var (d, _) = WebFlagOff200Tests.Dungeon(hero, t);
        Invoke(d, typeof(DungeonLocation), "DisplayLocation");
        var bytes = s.Bytes(t);
        Of(bytes, "Client.Menu").Should().ContainSingle().Which.GetProperty("screen").GetString().Should().Be("dungeon_room");
        var detail = Of(bytes, "Room.Detail").Should().ContainSingle().Subject;
        detail.GetProperty("header").GetString().Should().Be("Level 6 | " + DungeonLocation.GetThemeShortName(DungeonTheme.Catacombs));
        detail.GetProperty("danger").GetProperty("rating").GetInt32().Should().Be(2);
        detail.GetProperty("danger").GetProperty("max").GetInt32().Should().Be(3);
        Of(bytes, "Room.Info").Should().ContainSingle("Room.Info is sent as before");
    }

    private static async Task<(CombatAction action, byte[] bytes, string text)> MultiPrompt(string kind, Character hero, string script, int foes = 1,
        Action<CombatEngine>? setup = null, string lang = "en")
    {
        using var s = new Scope(kind, hero, lang);
        var t = new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes(script)), s.Stream);
        var engine = new CombatEngine(t);
        var monsters = Enumerable.Range(1, foes).Select(i => WebFlagOff200Tests.Foe("Ghoul " + i)).ToList();
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, hero);
        typeof(CombatEngine).GetField("_combatOwner", F)!.SetValue(engine, hero);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, new List<Character>());
        setup?.Invoke(engine);
        var result = new CombatResult { Player = hero, Teammates = new List<Character>(), CombatLog = new List<string>(), CurrentRound = 1 };
        var task = (Task<(CombatAction action, bool enableAutoCombat)>)Invoke(engine, typeof(CombatEngine), "GetPlayerActionMultiMonster", hero, monsters, result)!;
        var done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(20)));
        done.Should().Be(task, "the prompt must take the scripted keys");
        var (action, _) = await task;
        var bytes = s.Bytes(t);
        return (action, bytes, Plain(bytes));
    }

    private static async Task<(CombatAction action, byte[] bytes, string text)> PvpPrompt(string kind, Character hero, string script)
    {
        using var s = new Scope(kind, hero);
        var t = new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes(script)), s.Stream);
        var engine = new CombatEngine(t);
        var foe = WebFlagOff200Tests.Hero(false, false);
        foe.Name1 = foe.Name2 = "Rival";
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, hero);
        var result = new CombatResult { Player = hero, Teammates = new List<Character>(), CombatLog = new List<string>(), CurrentRound = 1 };
        var task = (Task<CombatAction>)Invoke(engine, typeof(CombatEngine), "GetPlayerAction", hero, null, result, foe)!;
        var done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(20)));
        done.Should().Be(task, "the prompt must take the scripted keys");
        var action = await task;
        var bytes = s.Bytes(t);
        return (action, bytes, Plain(bytes));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task DungeonFight_SendsClientMenu_ToTheWebClient(int foes)
    {
        var (_, bytes, _) = await MultiPrompt("web", Hero(), "D\n", foes);
        Of(bytes, "Client.Menu").Should().ContainSingle().Which.GetProperty("screen").GetString().Should().Be("combat");
        Of(bytes, "Room.Detail").Should().BeEmpty();
    }

    [Fact]
    public async Task PvpFight_SendsClientMenu_ToTheWebClient()
    {
        var (_, bytes, _) = await PvpPrompt("web", Hero(), "D\n");
        Of(bytes, "Client.Menu").Should().ContainSingle().Which.GetProperty("screen").GetString().Should().Be("combat_pvp");
    }

    [Fact]
    public void Labels_AreInTheSessionsLanguage()
    {
        var hero = Hero();
        using var s = new Scope("web", hero, "es");
        var t = s.Term();
        var (d, _) = WebFlagOff200Tests.Dungeon(hero, t);
        Invoke(d, typeof(DungeonLocation), "DisplayLocation");
        var menu = Of(s.Bytes(t), "Client.Menu").Single();
        var groups = menu.GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("label").GetString()).ToList();
        groups.Should().Equal(Loc.GetIn("es", "web.menu.exits"), Loc.GetIn("es", "web.menu.actions"));
        groups.Should().Equal("Salidas", "Acciones");
        Items(menu).Single(i => i.Key == "M").Label.Should().Be(Loc.GetIn("es", "dungeon.map"));
    }

    [Fact]
    public void NewKeys_AreInAllFiveLanguages()
    {
        string[] keys = { "web.menu.places", "web.menu.districts", "web.menu.system", "web.menu.actions", "web.menu.exits",
            "web.menu.tactics", "web.menu.quickbar", "web.menu.options", "web.menu.miracle", "web.menu.quick_heal" };
        foreach (var lang in new[] { "en", "es", "fr", "hu", "it" })
        {
            var d = JsonSerializer.Deserialize<Dictionary<string, string>>(Source($"Localization/{lang}.json"))!;
            foreach (var k in keys)
            {
                d.Should().ContainKey(k, "{0} must have {1}", lang, k);
                d[k].Should().NotBeNullOrWhiteSpace();
                d[k].Should().NotContainAny(((char)0x2014).ToString(), ((char)0x2013).ToString());
            }
        }
    }

    // ------------------------------------------------------------------ parity: Main Street

    private static async Task<bool> StreetRefuses(Character hero, string key)
    {
        using var s = new Scope("door", hero);
        var t = s.Term();
        var street = Street(hero, t);
        try
        {
            var task = (Task<bool>)typeof(MainStreetLocation).GetMethod("ProcessChoice", F)!.Invoke(street, new object[] { key })!;
            var done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10)));
            if (done != task) return false;
            await task;
        }
        catch (LocationExitException) { return false; }
        return Plain(s.Bytes(t)).Contains(Loc.Get("main_street.invalid_choice"));
    }

    private static List<string> MenuKeys(Character hero)
    {
        using var s = new Scope("web", hero);
        var t = s.Term();
        Invoke(Street(hero, t), typeof(MainStreetLocation), "ShowMainStreetMenu");
        return Items(Of(s.Bytes(t), "Client.Menu").Single()).Select(i => i.Key).ToList();
    }

    private static readonly string[] Universe =
        "ABCDEFGHIJKLMNOPRSTUVWXYZ0123456789".Select(c => c.ToString()).Concat(new[] { "$", "=", "+", ">", "@", "#", "&" }).ToArray();

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(12)]
    public async Task Districts_ClientMenu_ListsExactlyTheKeysTheHandlerTakes(int level)
    {
        var hero = Hero(level);
        var keys = MenuKeys(hero);
        var view = new MainStreetLocation.StreetView(MainStreetLocation.TierForLevel(level), false, false);
        keys.Should().Equal(MainStreetLocation.MainStreetLines(view, 0).Select(l => l.Key), "the menu is the handler's own list, in order");
        keys.Should().OnlyHaveUniqueItems();
        // the handler: every other key is refused, every menu key is taken (Quit, Settings and Help are not run here)
        foreach (var key in Universe.Except(GlobalKeys).Except(new[] { "Q" }))
        {
            bool refused = await StreetRefuses(Hero(level), key);
            refused.Should().Be(!keys.Contains(key), "key {0} at level {1}: listed {2}", key, level, keys.Contains(key));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(12)]
    public void Classic_ClientMenu_ListsExactlyTheDrawnKeys_EachOneTheHandlerTakes(int level)
    {
        var hero = Hero(level, classic: true);
        string text;
        List<string> keys;
        using (var s = new Scope("web", hero))
        {
            var t = s.Term();
            Invoke(Street(hero, t), typeof(MainStreetLocation), "ShowMainStreetMenu");
            var bytes = s.Bytes(t);
            keys = Items(Of(bytes, "Client.Menu").Single()).Select(i => i.Key).ToList();
            text = Plain(bytes);
        }
        // listed == drawn: the keys the classic text menu prints as [K], read from the text itself
        var drawn = Regex.Matches(text, @"\[(.)\]").Select(m => m.Groups[1].Value).ToList();
        keys.Should().BeEquivalentTo(drawn, "the classic Client.Menu lists exactly what the classic menu draws");
        keys.Should().OnlyHaveUniqueItems();

        // listed is a subset of accepted
        var handled = CaseLabels("Scripts/Locations/MainStreetClassic.cs", "ProcessClassicChoice").Concat(GlobalKeys).ToHashSet();
        keys.Should().OnlyContain(k => handled.Contains(k), "every listed key is a case of ProcessClassicChoice or a global key");

        // Accepted but intentionally not listed. The classic layout gates only its display (v1.1.14: "every classic key
        // does what it did, at every level"), so ProcessClassicChoice still takes a place's key below the tier that
        // draws it, and two keys no classic row has ever drawn: G (Good Deeds) and 9 (does nothing). The menu follows
        // the screen, not the switch, so these stay out of it.
        foreach (var hidden in new[] { "G", "9" })
        {
            handled.Should().Contain(hidden);
            keys.Should().NotContain(hidden, "{0} is taken by the handler but never drawn", hidden);
        }
        if (level == 1)
            foreach (var belowTier in new[] { "T", "B", "O", "K", "H", "N", "F" })
            {
                handled.Should().Contain(belowTier);
                keys.Should().NotContain(belowTier, "{0} is taken at level 1 but drawn only from its tier", belowTier);
                drawn.Should().NotContain(belowTier);
            }

        // online, every online key the row can draw is a case too
        MainStreetLocation.ClassicOnlineItems.Select(o => o.Key).Should().OnlyContain(k => handled.Contains(k));
        var online = MainStreetLocation.ClassicMenuGroups(new MainStreetLocation.StreetView(3, true, false), true).SelectMany(g => g.Items).Select(i => i.Key);
        online.Should().Contain(new[] { "3", "4", "5", "6", "7", "R" });
        online.Should().OnlyContain(k => handled.Contains(k));
    }

    // ------------------------------------------------------------------ parity: the dungeon room

    [Fact]
    public void DungeonRoom_ClientMenu_ListsExactlyTheKeysTheHandlerTakes()
    {
        var hero = Hero();
        using var s = new Scope("web", hero);
        var t = s.Term();
        var (d, floor) = WebFlagOff200Tests.Dungeon(hero, t);
        Invoke(d, typeof(DungeonLocation), "DisplayLocation");
        var items = Items(Of(s.Bytes(t), "Client.Menu").Single());
        var room = floor.GetCurrentRoom()!;
        var exitGroup = Loc.Get("web.menu.exits");
        items.Where(i => i.Group == exitGroup).Select(i => i.Key).Should().BeEquivalentTo(room.Exits.Keys.Select(k => k.ToString().Substring(0, 1)));
        var actionKeys = items.Where(i => i.Group != exitGroup).Select(i => i.Key).ToList();
        actionKeys.Should().BeEquivalentTo(CaseLabels("Scripts/Locations/DungeonLocation.cs", "ProcessRoomChoice"),
            "the action keys are exactly the room handler's cases");
        var list = (System.Collections.IEnumerable)Invoke(d, typeof(DungeonLocation), "RoomActions", room)!;
        actionKeys.Should().Equal(list.Cast<object>().Select(a => (string)a.GetType().GetProperty("Key")!.GetValue(a)!), "and they are RoomActions, the list the handler reads");
    }

    [Fact]
    public void DungeonRoom_Enabled_IsTheHandlersOwnCondition()
    {
        // a room where fight, treasure, event, examine, descend and camp are all refused; no herbs, no draught, no party
        var hero = Hero();
        using var s = new Scope("web", hero);
        var t = s.Term();
        var (d, floor) = WebFlagOff200Tests.Dungeon(hero, t);
        var room = floor.GetCurrentRoom()!;
        room.HasTreasure = false; room.HasStairsDown = false; room.HasEvent = false; room.Features.Clear();
        typeof(DungeonLocation).GetField("hasCampedThisFloor", F)!.SetValue(d, true);
        Invoke(d, typeof(DungeonLocation), "DisplayLocation");
        var items = Items(Of(s.Bytes(t), "Client.Menu").Single()).Where(i => i.Group == Loc.Get("web.menu.actions")).ToDictionary(i => i.Key);
        foreach (var k in new[] { "F", "T", "V", "X", "D", "R", "J", "U", "Y" })
            items[k].Enabled.Should().BeFalse("{0} is refused in this room", k);
        foreach (var k in new[] { "M", "G", "I", "P", "=", "Q" })
            items[k].Enabled.Should().BeTrue("{0} is always taken", k);
        // the room text draws only what is enabled here
        var text = Plain(s.Bytes(t));
        text.Should().NotContain("[F]").And.NotContain("[T]").And.NotContain("[R]");
    }

    // ------------------------------------------------------------------ parity: combat

    [Fact]
    public async Task DungeonFight_ClientMenu_ListsTheKeysTheTextMenuDrew_EachAHandlerCase()
    {
        var hero = Hero();
        hero.Quickbar[0] = ClassAbilitySystem.GetAvailableAbilities(hero).First().Id;
        var (_, bytes, text) = await MultiPrompt("web", hero, "D\n");
        var items = Items(Of(bytes, "Client.Menu").Single());
        var labels = CaseLabels("Scripts/Systems/CombatEngine.cs", "GetPlayerActionMultiMonster");
        var digits = Enumerable.Range(1, 9).Select(i => i.ToString()).ToList();
        labels.Should().Contain(digits);
        // 1.3.9: AUTO and SPD typed out are the long forms of O and X, which the menu lists
        labels.Should().Contain(new[] { "O", "AUTO", "X", "SPD" });
        items.Select(i => i.Key).Should().OnlyContain(k => labels.Contains(k), "every listed key is a case of the handler");
        // 1.3.10 (C3): exactly the keys the text menu drew this round, no key it did not draw
        var drawn = Regex.Matches(text, @"\u2551 \[([A-Z0-9])\]").Select(m => m.Groups[1].Value).ToList();
        items.Select(i => i.Key).Should().BeEquivalentTo(drawn, "Client.Menu lists what the text menu shows");
        items.Select(i => i.Key).Should().Contain(new[] { "P", "E", "T", "W", "L", "1" }).And.NotContain(new[] { "H", "U", "M", "V", "G", "B", "J", "K", "Z" },
            "a lone Warrior with no ally, no Miracle and nothing to coat or brew");
    }

    /// <summary>Sends a key then D; the key was refused when the prompt went on to take the D.</summary>
    private static async Task<(bool taken, CombatActionType type)> MultiKey(Character hero, string key, Action<CombatEngine>? setup = null)
    {
        var (action, _, _) = await MultiPrompt("door", hero, key + "\nD\n", 1, setup);
        return (action.Type != CombatActionType.Defend || key == "D", action.Type);
    }

    private static async Task<Dictionary<string, bool>> MultiEnabled(Character hero, Action<CombatEngine>? setup = null)
    {
        var (_, bytes, _) = await MultiPrompt("web", hero, "D\n", 1, setup);
        return Items(Of(bytes, "Client.Menu").Single()).ToDictionary(i => i.Key, i => i.Enabled);
    }

    [Theory]
    [InlineData(CharacterClass.Warrior)]
    [InlineData(CharacterClass.Assassin)]
    [InlineData(CharacterClass.Barbarian)]
    [InlineData(CharacterClass.Paladin)]
    public async Task DungeonFight_Enabled_IsWhatTheHandlerDecides(CharacterClass cls)
    {
        Character Make()
        {
            var h = Hero(cls: cls);
            h.HP = 200; // below max, so a potion is usable
            if (cls == CharacterClass.Assassin) { h.PoisonVials = 2; h.HerbHealing = 1; }
            if (cls == CharacterClass.Paladin) h.SmiteChargesRemaining = 1;
            if (cls == CharacterClass.Warrior) h.Healing = 0;
            return h;
        }
        var enabled = await MultiEnabled(Make());
        foreach (var key in new[] { "A", "P", "E", "T", "W", "L", "R", "I", "K", "Z", "G", "B", "J", "M", "V", "U", "H" })
        {
            var (taken, type) = await MultiKey(Make(), key);
            // 1.3.10 (C3): a key that cannot be taken this round is not listed; typed, the handler still refuses it
            bool listedAndEnabled = enabled.TryGetValue(key, out var on) && on;
            taken.Should().Be(listedAndEnabled, "{0} {1}: listed={2} enabled={3}, the handler gave {4}", cls, key, enabled.ContainsKey(key), on, type);
        }
    }

    [Fact]
    public async Task PvpFight_ClientMenu_ListsTheHandlersKeys_PowerPreciseDeathMercyNotListed()
    {
        var hero = Hero(cls: CharacterClass.Barbarian);
        hero.PoisonVials = 1; hero.HerbHealing = 1;
        var (_, bytes, _) = await PvpPrompt("web", hero, "D\n");
        var items = Items(Of(bytes, "Client.Menu").Single()).ToDictionary(i => i.Key);
        // ParseCombatAction's arms plus the keys GetPlayerAction takes before it
        var root = CSharpSyntaxTree.ParseText(Source("Scripts/Systems/CombatEngine.cs")).GetRoot();
        var parse = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.Text == "ParseCombatAction");
        var arms = parse.DescendantNodes().OfType<SwitchExpressionArmSyntax>()
            .Select(a => a.Pattern).OfType<ConstantPatternSyntax>().Select(p => ((LiteralExpressionSyntax)p.Expression).Token.ValueText).ToList();
        // B and J parse as Coat Blade and herbs, but the PvP round has no case for either: they make a plain attack
        items.Keys.Where(k => !char.IsDigit(k[0])).Should().BeEquivalentTo(arms.Except(new[] { "B", "J", "P", "E", "F", "M" }).Append("X"),
            "the menu holds every ParseCombatAction key (S is one) that does what it says in PvP, and X (1.3.9: Speed)");
        var pvpRound = CSharpSyntaxTree.ParseText(Source("Scripts/Systems/CombatEngine.cs")).GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(m => m.Identifier.Text == "ProcessPlayerVsPlayerAction").ToString();
        pvpRound.Should().NotContain("CombatActionType.CoatBlade").And.NotContain("CombatActionType.UseHerb", "if the PvP round learns them, list B and J");
        pvpRound.Should().Contain("CombatActionType.Rage").And.Contain("CombatActionType.RangedAttack").And.Contain("CombatActionType.QuickHeal");
        foreach (var k in new[] { "P", "E", "F", "M" })
        {
            items.Should().NotContainKey(k, "{0} is refused in PvP every round, so 1.3.10 does not list it", k);
            var (action, _, text) = await PvpPrompt("door", Hero(cls: CharacterClass.Barbarian), k + "\nD\n");
            action.Type.Should().Be(CombatActionType.Defend, "{0} was refused and the D after it taken", k);
            text.Should().Contain(Loc.Get("combat.pvp_action_unavailable"));
        }
        foreach (var k in new[] { "A", "H", "Q", "R", "I", "T", "L", "V", "G" })
        {
            items[k].Enabled.Should().BeTrue();
            var h = Hero(cls: CharacterClass.Barbarian);
            h.PoisonVials = 1; h.HerbHealing = 1;
            h.HP = h.MaxHP / 2;   // 1.3.10 (B14): a potion at full health is refused before the turn
            var (action, _, _) = await PvpPrompt("door", h, k + "\nD\n");
            action.Type.Should().NotBe(CombatActionType.Defend, "{0} is taken", k);
        }
    }

    // ------------------------------------------------------------------ 1.3.10: G3 the tactics shown, C3 only what can be taken

    private static string DrawDungeonMenu(Character hero, string kind, string lang)
    {
        using var s = new Scope("door", hero, lang);
        var t = new TerminalEmulator(new MemoryStream(), s.Stream);
        var engine = new CombatEngine(t);
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, hero);
        var classInfo = (List<(string key, string name, bool available)>)Invoke(engine, typeof(CombatEngine), "GetClassSpecificActions", hero)!;
        Invoke(engine, typeof(CombatEngine), "ShowDungeonCombatMenu" + kind, hero, false, false, classInfo, false, GodDomain.None);
        return Plain(s.Bytes(t));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void DungeonMenus_ListTheTactics_AndRageOnlyForAClassThatCanRage(string lang)
    {
        foreach (var cls in new[] { CharacterClass.Warrior, CharacterClass.Barbarian, CharacterClass.Paladin, CharacterClass.Assassin })
        foreach (bool raging in new[] { false, true })
        {
            var hero = Hero(cls: cls);
            hero.IsRaging = raging;
            bool rage = cls == CharacterClass.Barbarian && !raging;
            string std = DrawDungeonMenu(hero, "Standard", lang);
            string sr = DrawDungeonMenu(hero, "ScreenReader", lang);
            string bbs = DrawDungeonMenu(hero, "BBS", lang);
            foreach (var k in new[] { "P", "E", "T", "W", "L" })
            {
                std.Should().Contain($"[{k}] ", "{0} {1}: the standard menu lists {2}", lang, cls, k);
                sr.Should().Contain($"  {k} - ", "{0} {1}: the screen reader menu lists {2}", lang, cls, k);
                bbs.Should().Contain($"[{k}]", "{0} {1}: the compact menu lists {2}", lang, cls, k);
            }
            std.Contains("[G] ").Should().Be(rage, "{0} {1} raging={2}: Rage only while it can be taken", lang, cls, raging);
            sr.Contains("  G - ").Should().Be(rage);
            bbs.Contains("[G]").Should().Be(rage);
            foreach (var text in new[] { std, sr, bbs })
                text.Split('\n').Should().OnlyContain(r => r.Length <= 79, "{0} {1}: every row fits 79 columns", lang, cls);
        }
    }

    [Fact]
    public async Task ASoloPaladin_SeesNoActionItCannotTake_InTextOrClientMenu()
    {
        var hero = Hero(cls: CharacterClass.Paladin);
        hero.SmiteChargesRemaining = 0; hero.PoisonVials = 0; hero.HerbHealing = 0;
        var (_, bytes, text) = await MultiPrompt("web", hero, "D\n");
        var keys = Items(Of(bytes, "Client.Menu").Single()).Select(i => i.Key).ToList();
        foreach (var k in new[] { "H", "U", "M", "V", "G", "B", "J", "K", "Z" })
        {
            keys.Should().NotContain(k, "{0} cannot be taken by a solo Paladin this round", k);
            text.Should().NotContain($"[{k}] ", "and the text menu does not draw {0}", k);
        }
        keys.Should().Contain(new[] { "A", "D", "I", "R", "P", "E", "T", "W", "L", "O", "X" });
    }

    [Fact]
    public async Task AKeyTheMenuNoLongerShows_StillDoesWhatItDidWhenTyped()
    {
        Character Pal() { var h = Hero(cls: CharacterClass.Paladin); h.SmiteChargesRemaining = 0; h.PoisonVials = 0; h.HerbHealing = 0; return h; }
        var expect = new (string Key, string Line)[]
        {
            ("G", Loc.Get("combat.invalid_action")),
            ("V", Loc.Get("combat.invalid_action")),
            ("K", Loc.Get("combat.invalid_action")),
            ("M", Loc.Get("miracle.not_ready")),
            ("B", Loc.Get("ui.no_poison_vials")),
            ("J", Loc.Get("combat.herb_empty")),
            ("Z", Loc.Get("combat.out_of_smites")),
        };
        foreach (var (key, line) in expect)
        {
            var (action, _, text) = await MultiPrompt("web", Pal(), key + "\nD\n");
            action.Type.Should().Be(CombatActionType.Defend, "{0} was refused and the D after it taken", key);
            text.Should().Contain(line, "{0} says what it said before", key);
        }
        foreach (var key in new[] { "H", "U" })
        {
            var (action, _, _) = await MultiPrompt("web", Pal(), key + "\nD\n");
            action.Type.Should().Be(CombatActionType.Defend, "{0} with no ally is refused, as before", key);
        }
    }

    // ------------------------------------------------------------------ 1.3.10 B14: a heal that would restore little asks first

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public async Task APotionForLittleHp_AsksFirst_NoSpendsNothing(string lang)
    {
        Character Hurt(long hp) { var h = Hero(); h.HP = hp; h.MaxHP = 400; h.Healing = 5; h.ManaPotions = 0; return h; }
        var hero = Hurt(361);   // 39 missing, under 40 (10 percent)
        var (no, _, text) = await MultiPrompt("door", hero, "I\nN\nD\n", lang: lang);
        no.Type.Should().Be(CombatActionType.Defend, "No went back to the menu");
        hero.Healing.Should().Be(5, "nothing was spent");
        text.Should().Contain(Loc.GetIn(lang, "combat.potion_small_heal", 39, 400).Substring(0, 20));
        // the question's rows (the scripted keys are not echoed, so the first row follows the action prompt here)
        text.Split('\n').Select(r => r.Replace(Loc.GetIn(lang, "combat.choose_action"), "")).Should().OnlyContain(r => r.Length <= 79);
        var (yes, _, _) = await MultiPrompt("door", Hurt(361), "I\nY\n", lang: lang);
        yes.Type.Should().Be(CombatActionType.UseItem, "Yes drinks it");
        var (plain, _, plainText) = await MultiPrompt("door", Hurt(360), "I\n", lang: lang);
        plain.Type.Should().Be(CombatActionType.UseItem, "40 missing is 10 percent: no question");
        plainText.Should().NotContain(Loc.GetIn(lang, "combat.potion_small_heal", 40, 400).Substring(0, 20));
        var (full, _, fullText) = await MultiPrompt("door", Hurt(400), "I\nD\n", lang: lang);
        full.Type.Should().Be(CombatActionType.Defend, "at full health the potion is refused, as before");
        fullText.Should().Contain(Loc.GetIn(lang, "ui.no_usable_potions"));
    }

    private static Character Paladin(long hp)
    {
        var h = Hero(cls: CharacterClass.Paladin);
        h.MaxHP = 400; h.HP = hp;
        h.Quickbar[0] = "lay_on_hands";
        h.CurrentCombatStamina = 200;
        return h;
    }

    [Fact]
    public async Task LayOnHands_AtFullHealth_IsRefusedBeforeItCostsAnything_AndAskesFirstForLittle()
    {
        var full = Paladin(400);
        long st = full.CurrentCombatStamina;
        var (a, _, text) = await MultiPrompt("door", full, "1\nD\n");
        a.Type.Should().Be(CombatActionType.Defend, "refused, the menu came back");
        full.CurrentCombatStamina.Should().Be(st, "no stamina spent");
        text.Should().Contain(Loc.Get("combat.heal_no_one_hurt", ClassAbilitySystem.GetAbility("lay_on_hands")!.DisplayName));

        var (no, _, noText) = await MultiPrompt("door", Paladin(395), "1\nN\nD\n");
        no.Type.Should().Be(CombatActionType.Defend);
        noText.Should().Contain(Loc.Get("combat.heal_small_confirm", ClassAbilitySystem.GetAbility("lay_on_hands")!.DisplayName, 5));
        var (yes, _, _) = await MultiPrompt("door", Paladin(395), "1\nY\n");
        yes.Type.Should().Be(CombatActionType.ClassAbility);
        var (hurt, _, hurtText) = await MultiPrompt("door", Paladin(200), "1\n");
        hurt.Type.Should().Be(CombatActionType.ClassAbility, "a real wound: no question");
        hurtText.Should().NotContain(Loc.Get("combat.heal_small_confirm", ClassAbilitySystem.GetAbility("lay_on_hands")!.DisplayName, 200));
    }

    [Fact]
    public async Task LayOnHands_ReadsTheAllies_ItCanHeal()
    {
        static Action<CombatEngine> With(Character ally) => e => typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(e, new List<Character> { ally });
        var ally = new Character { Name2 = "Ally", HP = 380, MaxHP = 400 };
        var (asked, _, text) = await MultiPrompt("door", Paladin(400), "1\nN\nD\n", setup: With(ally));
        asked.Type.Should().Be(CombatActionType.Defend, "the ally misses 20 of 400: asked, and No");
        text.Should().Contain(Loc.Get("combat.heal_small_confirm", ClassAbilitySystem.GetAbility("lay_on_hands")!.DisplayName, 20));
        var wounded = new Character { Name2 = "Ally", HP = 100, MaxHP = 400 };
        var (go, _, _) = await MultiPrompt("door", Paladin(400), "1\n", setup: With(wounded));
        go.Type.Should().Be(CombatActionType.ClassAbility, "an ally with a real wound: no question");
    }

    [Fact]
    public async Task Pvp_QuickHealForLittle_AsksFirst_AndAPotionAtFullHealthSpendsNoTurn()
    {
        var h = Hero(); h.MaxHP = 400; h.HP = 395; h.Healing = 3; h.ManaPotions = 0;
        var (no, _, text) = await PvpPrompt("door", h, "Q\nN\nD\n");
        no.Type.Should().Be(CombatActionType.Defend);
        text.Should().Contain(Loc.Get("combat.potion_small_heal", 5, 400));
        var f = Hero(); f.MaxHP = 400; f.HP = 400; f.Healing = 3; f.ManaPotions = 0;
        var (full, _, fullText) = await PvpPrompt("door", f, "H\nD\n");
        full.Type.Should().Be(CombatActionType.Defend, "refused before the turn");
        fullText.Should().Contain(Loc.Get("combat.full_health"));
        f.Healing.Should().Be(3);
    }

    // ------------------------------------------------------------------ the quickbar hint

    private static Character QuickbarHero(bool screenReader, bool filled)
    {
        var h = Hero();
        h.ScreenReaderMode = screenReader;
        if (filled) h.Quickbar[0] = ClassAbilitySystem.GetAvailableAbilities(h).First().Id;
        return h;
    }

    /// <summary>The hint rides on Client.Menu exactly when the text menu printed the quickbar hint line.</summary>
    private static void HintMatchesText(byte[] bytes, string text, bool expectLine)
    {
        string line = "  " + Loc.Get("combat.quickbar_info_hint");
        text.Contains(line).Should().Be(expectLine, "the text menu {0} the hint line", expectLine ? "prints" : "does not print");
        var menu = Of(bytes, "Client.Menu").Single();
        if (expectLine)
        {
            var hint = menu.GetProperty("hint");
            hint.GetProperty("text").GetString().Should().Be(Loc.Get("combat.quickbar_info_hint"));
            hint.GetProperty("label").GetString().Should().Be(Loc.Get("web.menu.quickbar"));
        }
        else
            menu.TryGetProperty("hint", out _).Should().BeFalse("no hint unless the text drew it");
    }

    [Theory]
    [InlineData(true, true, true)]    // the screen reader PvP menu prints the line under a filled quickbar
    [InlineData(true, false, false)]  // no quickbar, no line
    [InlineData(false, true, false)]  // the boxed menu has no such line
    public async Task PvpFight_Hint_IsSentOnlyWhenTheTextDrawsIt(bool screenReader, bool filled, bool expectLine)
    {
        var (_, bytes, text) = await PvpPrompt("web", QuickbarHero(screenReader, filled), "D\n");
        HintMatchesText(bytes, text, expectLine);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DungeonFight_Hint_IsNotSent_AsNoDungeonMenuDrawsTheLine(bool screenReader)
    {
        var (_, bytes, text) = await MultiPrompt("web", QuickbarHero(screenReader, filled: true), "D\n");
        HintMatchesText(bytes, text, expectLine: false);
    }

    // ------------------------------------------------------------------ cadence

    [Fact]
    public async Task OneClientMenu_PerScreenDraw_NonePerKeystroke()
    {
        var hero = Hero();
        using (var s = new Scope("web", hero))
        {
            var t = s.Term();
            var (d, _) = WebFlagOff200Tests.Dungeon(hero, t);
            Invoke(d, typeof(DungeonLocation), "DisplayLocation");
            long afterDraw = s.Bytes(t).Length;
            Of(s.Bytes(t), "Client.Menu").Should().ContainSingle();
            Of(s.Bytes(t), "Room.Detail").Should().ContainSingle();
            foreach (var key in new[] { "Z", "F", "Y", "K", "J", "U" })
                await (Task<bool>)typeof(DungeonLocation).GetMethod("ProcessRoomChoice", F)!.Invoke(d, new object[] { key })!;
            Frames(s.Bytes(t)).Where(f => f.Package == "Client.Menu" || f.Package == "Room.Detail").Should().HaveCount(2, "keystrokes send no menu");
        }
        using (var s = new Scope("web", hero))
        {
            var t = s.Term();
            var street = Street(hero, t);
            Invoke(street, typeof(MainStreetLocation), "ShowMainStreetMenu");
            foreach (var key in new[] { "W", "@", "9" })
                await (Task<bool>)typeof(MainStreetLocation).GetMethod("ProcessChoice", F)!.Invoke(street, new object[] { key })!;
            Of(s.Bytes(t), "Client.Menu").Should().ContainSingle();
        }
        // the fight prompt draws its menu again after a refused key: one Client.Menu per draw
        var (_, bytes, text) = await MultiPrompt("web", Hero(), "K\nD\n");
        text.Should().Contain(Loc.Get("combat.invalid_action"));
        Of(bytes, "Client.Menu").Should().HaveCount(2, "two menu draws, two frames");
    }

    // ------------------------------------------------------------------ no frame reaches another session

    [Fact]
    public void Spectator_GetsTheText_ButNoWebClientFrame()
    {
        var hero = Hero();
        using var s = new Scope("web", hero);
        var t = s.Term();
        s.Ctx!.Terminal = t; // the session's own terminal, the one a spectator is attached to
        var spectatorStream = new MemoryStream();
        var spectator = new TerminalEmulator(new MemoryStream(), spectatorStream);
        t.AddSpectatorStream(spectator);
        var (d, _) = WebFlagOff200Tests.Dungeon(hero, t);
        Invoke(d, typeof(DungeonLocation), "DisplayLocation");
        Invoke(Street(hero, t), typeof(MainStreetLocation), "ShowMainStreetMenu");
        spectator.StreamWriterInternal?.Flush();
        var own = s.Bytes(t);
        Of(own, "Client.Menu").Should().HaveCount(2);
        var mirrored = spectatorStream.ToArray();
        Plain(mirrored).Should().Contain("Room q1", "the spectator does see the screen");
        Frames(mirrored).Should().BeEmpty("no GMCP frame is mirrored to a spectator");
        Encoding.UTF8.GetString(mirrored).Should().NotContain("Client.Menu").And.NotContain("Room.Detail");
    }

    [Fact]
    public void AGroupFollowersMenu_DrawnOnTheLeadersSession_SendsNoClientMenu()
    {
        var follower = Hero(cls: CharacterClass.Assassin);
        using var s = new Scope("web", Hero());
        var t = s.Term();
        var engine = new CombatEngine(t);
        foreach (var kind in new[] { "Standard", "ScreenReader", "BBS" })
            Invoke(engine, typeof(CombatEngine), "ShowDungeonCombatMenu" + kind, follower, false, false,
                new List<(string key, string name, bool available)>(), true, GodDomain.None);
        Frames(s.Bytes(t)).Should().BeEmpty("the menu drawers send nothing; only the owner's own prompt does");
    }

    [Fact]
    public void OnlyTheScreensOwnDrawSendsTheWebClientPackages()
    {
        // Every place that can send Client.Menu or Room.Detail, and nothing in the spectate, group invite or idle
        // watchdog code, which write to other sessions.
        var allowed = new Dictionary<string, string[]>
        {
            ["Scripts/Server/GmcpBridge.cs"] = new[] { "EmitWebClient", "EmitWebEnemies", "EmitWebFooterIfChanged", "EmitCombatEnemiesIfChanged", "EmitWebWaiting", "EmitWebPollWaiting" },
            ["Scripts/Server/WebClientMenu.cs"] = new[] { "Emit" },
            ["Scripts/UI/TerminalEmulator.cs"] = new[] { "ReadLineInteractiveAsync", "GetMaskedInput" },   // 1.3.9: Client.Waiting, at the start of each stream read
            ["Scripts/Locations/MainStreetWebMenu.cs"] = new[] { "EmitWebMenu" },
            ["Scripts/Locations/DungeonLocation.cs"] = new[] { "EmitRoomDetail", "EmitRoomMenu", "EmitWebMap" },
            ["Scripts/Locations/BaseLocation.cs"] = new[] { "EmitWebSheet", "EmitWebFooter", "EmitWebFooterFor" },   // 1.3.9: EmitWebFooterFor, the status line and the prompt refresh
            ["Scripts/Systems/CombatEngine.cs"] = new[] { "GetPlayerAction", "GetPlayerActionMultiMonster", "PlayerVsMonsters", "AutoCombatStopRequested" },   // 1.3.9: Client.Waiting at the stop key
            ["Scripts/Systems/OpeningStorySystem.cs"] = new[] { "SkippableDelay" },   // 1.3.9: Client.Waiting at the story skip
        };
        foreach (var file in Directory.GetFiles(Path.Combine(RepoRoot(), "Scripts"), "*.cs", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(RepoRoot(), file).Replace('\\', '/');
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
            foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                string callee = call.Expression.ToString();
                bool sends = callee.EndsWith("EmitWebClient") || callee.EndsWith("WebClientMenu.Emit")
                    || callee.EndsWith("EmitWebEnemies") || callee.EndsWith("EmitWebFooterIfChanged")   // 2.0 phase 3a
                    || callee.EndsWith("EmitWebWaiting") || callee.EndsWith("EmitWebPollWaiting")   // 1.3.9
                    || (callee.EndsWith("Emit") || callee.EndsWith("EmitTo"))
                        && GmcpBridge.WebClientPackages.Any(pkg => call.ArgumentList.ToString().Contains("\"" + pkg + "\""));
                if (!sends) continue;
                var method = call.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.Identifier.Text ?? "";
                allowed.ContainsKey(rel).Should().BeTrue("{0} sends a web client package from {1}", rel, method);
                allowed[rel].Should().Contain(method, "{0}.{1} sends a web client package", rel, method);
            }
        }
        foreach (var rel in new[] { "Scripts/Core/GameEngine.cs", "Scripts/Server/MudChatSystem.cs", "Scripts/Server/MudServer.cs" })
        {
            var src = Source(rel);
            src.Should().NotContain("Client.Menu").And.NotContain("Room.Detail").And.NotContain("EmitWebClient").And.NotContain("WebClientMenu");
            foreach (var pkg in GmcpBridge.WebClientPackages) src.Should().NotContain(pkg);   // 2.0 phase 3a
            src.Should().NotContain("EmitWebEnemies").And.NotContain("EmitWebFooter").And.NotContain("EmitWebSheet").And.NotContain("EmitWebMap");
        }
    }

    // ------------------------------------------------------------------ Room.Detail shows only what the text shows

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoomDetail_ShowsWhatTheTextShows_AndNothingMore(bool screenReader)
    {
        var hero = Hero();
        hero.ScreenReaderMode = screenReader;
        using var s = new Scope("web", hero);
        GameConfig.ScreenReaderMode = screenReader; // as PlayerSession sets it from the account preference
        var t = s.Term();
        if (screenReader) t.IsPlainText = true;
        var (d, floor) = WebFlagOff200Tests.Dungeon(hero, t);
        var room = floor.GetCurrentRoom()!;
        room.HasTrap = true; // an undiscovered trap in this room
        typeof(DungeonLocation).GetField("dungeonRandom", F)!.SetValue(d, new NoTrapRoll());
        Invoke(d, typeof(DungeonLocation), "DisplayLocation");
        var bytes = s.Bytes(t);
        string text = Plain(bytes);
        var detail = Of(bytes, "Room.Detail").Single();
        string json = detail.GetRawText();

        // nothing about the unexplored neighbour (u3, "Secret Treasury", trapped, guarded, with treasure), the hidden
        // exit's target (h5), or the trap
        foreach (var leak in new[] { "u3", "h5", "c2", "e4", "Secret Treasury", "Room h5", "trap", "Trap", "hidden", "Hidden", "Revealed", "monster", "treasure" })
            json.Should().NotContain(leak);
        text.Should().NotContain(Loc.Get("dungeon.trap_detect_general"), "the roll was set so the text shows no trap");

        // the header, as the text prints it
        string header = detail.GetProperty("header").GetString()!;
        text.Should().Contain(header);
        detail.GetProperty("name").GetString().Should().Be("Room q1");
        text.Should().Contain(screenReader ? Loc.Get("dungeon.danger_of", 2, 3) : Loc.Get("dungeon.bbs_danger") + "**.");

        // each exit with the state its line shows
        foreach (var e in detail.GetProperty("exits").EnumerateArray())
        {
            string key = e.GetProperty("key").GetString()!, desc = e.GetProperty("description").GetString()!;
            string label = e.GetProperty("stateLabel").GetString()!;
            string line = screenReader ? $"{key}. {desc} ({label})" : $"[{key}] {desc} ({label})";
            text.Should().Contain(line);
        }
        detail.GetProperty("exits").EnumerateArray().Select(e => e.GetProperty("state").GetString())
            .Should().Equal(screenReader ? new[] { "fully_cleared", "unknown", "explored", "unknown" } : new[] { "cleared", "unknown", "explored", "unknown" });

        // the features as listed under "You notice", and no more
        var features = detail.GetProperty("features").EnumerateArray().Select(f => f.GetString()!).ToList();
        features.Should().Equal("a cracked altar", "a rusted lever");
        foreach (var f in features) text.Should().Contain("  - " + f);
        json.Should().NotContain("a used thing", "an examined feature is not listed in the text");
    }

    [Fact]
    public void RoomDetail_InTheCompactView_HasNoExitDescriptions_AsItsTextHasNone()
    {
        var hero = Hero();
        using var s = new Scope("web", hero);
        GameConfig.CompactMode = true; // this session's compact (BBS) layout
        var t = s.Term();
        var (d, _) = WebFlagOff200Tests.Dungeon(hero, t);
        Invoke(d, typeof(DungeonLocation), "DisplayLocation");
        var bytes = s.Bytes(t);
        string text = Plain(bytes);
        foreach (var desc in new[] { "a stone arch", "a dark tunnel", "a narrow stair", "a crack in the wall" })
            text.Should().NotContain(desc, "the compact exit line prints keys and states only");
        var detail = Of(bytes, "Room.Detail").Single();
        string json = detail.GetRawText();
        foreach (var desc in new[] { "a stone arch", "a dark tunnel", "a narrow stair", "a crack in the wall" })
            json.Should().NotContain(desc);
        detail.TryGetProperty("header", out _).Should().BeFalse("the compact view prints no Level and theme line");
        foreach (var e in detail.GetProperty("exits").EnumerateArray())
            text.Should().Contain($"[{e.GetProperty("key").GetString()}]{e.GetProperty("stateLabel").GetString()}");
        var menu = Of(bytes, "Client.Menu").Single();
        Items(menu).Where(i => i.Group == Loc.Get("web.menu.exits")).Select(i => i.Label).Should().Equal("north", "east", "west", "south");
    }

    [Fact]
    public void EmitTo_SendsOtherPackages_ButNeverTheWebClientOnes()
    {
        var other = new MemoryStream();
        var session = new PlayerSession("webother", "Web", null!, other, null!, null!, System.Threading.CancellationToken.None, gmcpEnabled: true);
        typeof(PlayerSession).GetProperty("Context")!.SetValue(session,
            new SessionContext { Username = "webother", OutputStream = other, ConnectionType = "Web", GmcpEnabled = true });
        GmcpBridge.EmitTo(session, "Client.Menu", new { screen = "x" });
        GmcpBridge.EmitTo(session, "Room.Detail", new { name = "x" });
        other.Length.Should().Be(0, "a web client package never goes to another session");
        GmcpBridge.EmitTo(session, "Comm.Channel.Text", new { text = "hi" });
        Frames(other.ToArray()).Select(f => f.Package).Should().Equal("Comm.Channel.Text");
    }

    /// <summary>A Random whose trap detection roll always misses (NextDouble 0.99).</summary>
    private sealed class NoTrapRoll : Random
    {
        public override double NextDouble() => 0.99;
        public override int Next(int maxValue) => 0;
    }
}
