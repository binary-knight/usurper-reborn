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
    public void Classic_ClientMenu_ListsTheDrawnKeys_EachOneTheHandlerTakes(int level)
    {
        var hero = Hero(level, classic: true);
        var keys = MenuKeys(hero);
        var view = new MainStreetLocation.StreetView(MainStreetLocation.TierForLevel(level), false, false);
        var drawn = MainStreetLocation.ClassicVisualRows.SelectMany(r => MainStreetLocation.ClassicShown(r, view)).Select(i => i.Key).ToList();
        keys.Should().BeEquivalentTo(drawn, "the classic menu is what its rows draw");
        var handled = CaseLabels("Scripts/Locations/MainStreetClassic.cs", "ProcessClassicChoice").Concat(GlobalKeys).ToHashSet();
        keys.Should().OnlyContain(k => handled.Contains(k), "every listed key is a case of ProcessClassicChoice or a global key");
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
    public async Task DungeonFight_ClientMenu_ListsExactlyTheKeysTheHandlerTakes()
    {
        var hero = Hero();
        hero.Quickbar[0] = ClassAbilitySystem.GetAvailableAbilities(hero).First().Id;
        var (_, bytes, _) = await MultiPrompt("web", hero, "D\n");
        var items = Items(Of(bytes, "Client.Menu").Single());
        var labels = CaseLabels("Scripts/Systems/CombatEngine.cs", "GetPlayerActionMultiMonster");
        var digits = Enumerable.Range(1, 9).Select(i => i.ToString()).ToList();
        labels.Should().Contain(digits);
        var expected = labels.Where(l => l != "" && !digits.Contains(l)).Append("1").ToList();
        items.Select(i => i.Key).Should().BeEquivalentTo(expected, "every case of the handler, the filled quickbar slot for the digits");
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
            taken.Should().Be(enabled[key], "{0} {1}: the menu says enabled={2}, the handler gave {3}", cls, key, enabled[key], type);
        }
    }

    [Fact]
    public async Task PvpFight_ClientMenu_ListsTheHandlersKeys_PowerPreciseDeathMercyRefused()
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
        items.Keys.Where(k => k.Length == 1 && !char.IsDigit(k[0]) || k == "SPD").Should().BeEquivalentTo(arms.Append("SPD"),
            "the menu holds every ParseCombatAction key (S is one) and SPD");
        foreach (var k in new[] { "P", "E", "F", "M" })
        {
            items[k].Enabled.Should().BeFalse("{0} is refused in PvP", k);
            var (action, _, text) = await PvpPrompt("door", Hero(cls: CharacterClass.Barbarian), k + "\nD\n");
            action.Type.Should().Be(CombatActionType.Defend, "{0} was refused and the D after it taken", k);
            text.Should().Contain(Loc.Get("combat.pvp_action_unavailable"));
        }
        foreach (var k in new[] { "A", "H", "Q", "R", "I", "T", "L", "V", "G", "B", "J" })
        {
            items[k].Enabled.Should().BeTrue();
            var h = Hero(cls: CharacterClass.Barbarian);
            h.PoisonVials = 1; h.HerbHealing = 1;
            var (action, _, _) = await PvpPrompt("door", h, k + "\nD\n");
            action.Type.Should().NotBe(CombatActionType.Defend, "{0} is taken", k);
        }
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
            ["Scripts/Server/GmcpBridge.cs"] = new[] { "EmitWebClient" },
            ["Scripts/Server/WebClientMenu.cs"] = new[] { "Emit" },
            ["Scripts/Locations/MainStreetWebMenu.cs"] = new[] { "EmitWebMenu" },
            ["Scripts/Locations/DungeonLocation.cs"] = new[] { "EmitRoomDetail", "EmitRoomMenu" },
            ["Scripts/Systems/CombatEngine.cs"] = new[] { "GetPlayerAction", "GetPlayerActionMultiMonster" },
        };
        foreach (var file in Directory.GetFiles(Path.Combine(RepoRoot(), "Scripts"), "*.cs", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(RepoRoot(), file).Replace('\\', '/');
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
            foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                string callee = call.Expression.ToString();
                bool sends = callee.EndsWith("EmitWebClient") || callee.EndsWith("WebClientMenu.Emit")
                    || (callee.EndsWith("Emit") || callee.EndsWith("EmitTo")) && call.ArgumentList.ToString().Contains("\"Client.Menu\"")
                    || (callee.EndsWith("Emit") || callee.EndsWith("EmitTo")) && call.ArgumentList.ToString().Contains("\"Room.Detail\"");
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

    /// <summary>A Random whose trap detection roll always misses (NextDouble 0.99).</summary>
    private sealed class NoTrapRoll : Random
    {
        public override double NextDouble() => 0.99;
        public override int Next(int maxValue) => 0;
    }
}
