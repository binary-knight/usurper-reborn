using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 2.0 phase 3a: the web client's new GMCP data. Room.Detail gains themeId; four new packages, each sent only to the
/// current session and only when it is the web client: Client.Enemies (each foe's monster family, beside the enemy
/// list), Client.Footer (potions, lives and the XP percent of the status line), Client.Sheet (the Character Status
/// sheet) and Client.Map (the map screen). Each lists what its screen draws and nothing more. The flag off case is
/// WebFlagOff3aTests.
/// </summary>
[Collection("SharedGameSingletons")]
public class WebClient3aTests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    // ------------------------------------------------------------------ plumbing

    private static string Plain(byte[] bytes) =>
        Regex.Replace(Encoding.UTF8.GetString(StripFrames(bytes)), "\u001b\\[[0-9;?]*[A-Za-z]", "").Replace("\r", "");

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

    private static List<JsonElement> Of(byte[] bytes, string package) =>
        WebMenu200Tests.Frames(bytes).Where(f => f.Package == package).Select(f => f.Json).ToList();

    private static List<string> Names(JsonElement listHolder, string prop) =>
        listHolder.GetProperty(prop).EnumerateArray().Select(e => e.GetProperty("name").GetString()!).ToList();

    /// <summary>A session of the given kind, current for the scope, whose engine plays <paramref name="own"/>.</summary>
    private sealed class Scope : IDisposable
    {
        private readonly SessionContext? _saved = SessionContext.Current;
        private readonly string _lang = GameConfig.Language;
        private readonly bool _pacing = Pacing.Disabled;
        private readonly bool _sr = GameConfig.ScreenReaderMode;
        private readonly bool _compact = GameConfig.CompactMode;
        private readonly DifficultyMode _difficulty = DifficultySystem.CurrentDifficulty;
        private readonly bool _permadeath = GameConfig.OnlinePermadeathEnabled;
        public readonly MemoryStream Stream = new();
        public readonly SessionContext? Ctx;
        public Scope(string kind, Character own, string lang = "en", bool screenReader = false)
        {
            Pacing.Disabled = true;
            Ctx = WebFlagOff200Tests.Session(kind, Stream, own);
            if (Ctx != null)
            {
                Ctx.InitializeSystems();
                var engine = (GameEngine)RuntimeHelpers.GetUninitializedObject(typeof(GameEngine));
                engine.CurrentPlayer = own;
                Ctx.Engine = engine;
            }
            SessionContext.Current = Ctx;
            GameConfig.Language = lang;
            GameConfig.ScreenReaderMode = screenReader;
        }
        public TerminalEmulator Term(string input = "", bool plain = false)
        {
            var t = new TerminalEmulator(new WebFlagOff3aTests.ScriptedStream(input), Stream);
            if (plain) t.IsPlainText = true;
            return t;
        }
        public byte[] Bytes(TerminalEmulator t) { t.StreamWriterInternal?.Flush(); return Stream.ToArray(); }
        public void Dispose()
        {
            SessionContext.Current = _saved;
            GameConfig.Language = _lang;
            Pacing.Disabled = _pacing;
            GameConfig.ScreenReaderMode = _sr;
            GameConfig.CompactMode = _compact;
            DifficultySystem.CurrentDifficulty = _difficulty;
            GameConfig.OnlinePermadeathEnabled = _permadeath;
        }
    }

    private static object? Invoke(object target, Type type, string method, params object?[] args)
    {
        try { return type.GetMethod(method, F)!.Invoke(target, args); }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    private static async Task Await(object? task)
    {
        var t = (Task)task!;
        var done = await Task.WhenAny(t, Task.Delay(TimeSpan.FromSeconds(60)));
        done.Should().Be(t, "the screen must finish on the scripted input");
        await t;
    }

    private static MainStreetLocation Street(Character player, TerminalEmulator t)
    {
        var street = new MainStreetLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(street, t);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(street, player);
        return street;
    }

    private static Character Hero(bool screenReader = false) => WebFlagOff3aTests.Hero(screenReader);

    private static string Enters => string.Concat(Enumerable.Repeat("\n", 8));

    // ------------------------------------------------------------------ every new package stays with its own session

    [Theory]
    [InlineData("Client.Enemies")]
    [InlineData("Client.Footer")]
    [InlineData("Client.Sheet")]
    [InlineData("Client.Map")]
    public void EmitTo_RefusesEachPhase3aPackage(string package)
    {
        GmcpBridge.IsWebClientPackage(package).Should().BeTrue();
        var other = new MemoryStream();
        var session = new PlayerSession("webother", "Web", null!, other, null!, null!, System.Threading.CancellationToken.None, gmcpEnabled: true);
        typeof(PlayerSession).GetProperty("Context")!.SetValue(session,
            new SessionContext { Username = "webother", OutputStream = other, ConnectionType = "Web", GmcpEnabled = true });
        GmcpBridge.EmitTo(session, package, new { x = 1 });
        other.Length.Should().Be(0, "{0} describes the current session's screen and never goes to another session", package);
        GmcpBridge.EmitTo(session, "Comm.Channel.Text", new { text = "hi" });
        WebMenu200Tests.Frames(other.ToArray()).Select(f => f.Package).Should().Equal("Comm.Channel.Text");
    }

    [Theory]
    [InlineData("mud_gmcp")]
    [InlineData("ssh")]
    [InlineData("screenreader")]
    [InlineData("web_nogmcp")]
    [InlineData("door")]
    public void TheNewEmitters_SendNothing_ToASessionThatIsNotTheWebClient(string kind)
    {
        var hero = Hero();
        using var s = new Scope(kind, hero);
        GmcpBridge.EmitWebEnemies(WebFlagOff3aTests.Foes(), fightStart: true);
        GmcpBridge.EmitWebFooterIfChanged(1, 2, 3, 3, "lives", 4);
        s.Stream.Length.Should().Be(0);
    }

    // ------------------------------------------------------------------ Room.Detail themeId

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    [InlineData("hu")]
    public void RoomDetail_ThemeId_IsTheThemeName_TheSameInEveryLanguage(string lang)
    {
        var hero = Hero();
        using var s = new Scope("web", hero, lang);
        var t = s.Term();
        var (d, floor) = WebFlagOff200Tests.Dungeon(hero, t);
        floor.Theme = DungeonTheme.FrozenDepths;
        Invoke(d, typeof(DungeonLocation), "DisplayLocation");
        var detail = Of(s.Bytes(t), "Room.Detail").Single();
        detail.GetProperty("themeId").GetString().Should().Be("FrozenDepths");
        detail.GetProperty("theme").GetString().Should().Be(DungeonLocation.GetThemeShortName(DungeonTheme.FrozenDepths), "theme stays the translated name");
    }

    // ------------------------------------------------------------------ Client.Enemies

    private static async Task<byte[]> Fight(string kind, Character hero, List<Monster> foes)
    {
        using var s = new Scope(kind, hero);
        var t = s.Term(WebFlagOff3aTests.Attacks());
        var engine = new CombatEngine(t);
        typeof(CombatEngine).GetMethod("SeedRandomForTests", F)!.Invoke(engine, new object[] { 314 });
        await Await(engine.PlayerVsMonsters(hero, foes, offerMonkEncounter: false));
        return s.Bytes(t);
    }

    [Fact]
    public async Task ClientEnemies_AreTheEnemyListsMembersInItsOrder_WithEachFamily()
    {
        var foes = WebFlagOff3aTests.Foes();
        foes[1].FamilyName = "Goblinoid";
        foes.Add(new Monster { Name = "Nameless Thing", Level = 8, HP = 200, MaxHP = 200, Strength = 30, Defence = 5, Experience = 1, IsActive = true });
        var bytes = await Fight("web", Hero(), foes);
        var frames = WebMenu200Tests.Frames(bytes);
        var start = frames.First(f => f.Package == "Char.Combat.Start").Json;
        var first = Of(bytes, "Client.Enemies").First();
        Names(first, "enemies").Should().Equal(Names(start, "enemies"), "the same members in the same order as the fight's own list");
        first.GetProperty("enemies").EnumerateArray().Select(e => e.TryGetProperty("family", out var f) ? f.GetString() : null)
            .Should().Equal("Undead", "Goblinoid", "");
        // every Char.Combat.Enemies is matched by the last Client.Enemies before or beside it
        List<string>? last = null;
        int checkedLists = 0;
        foreach (var (pkg, json) in frames)
        {
            if (pkg == "Client.Enemies") last = Names(json, "enemies");
            if (pkg == "Char.Combat.Enemies") { Names(json, "enemies").Should().Equal(last); checkedLists++; }
        }
        checkedLists.Should().BeGreaterThan(0, "the fight sent its enemy list");
        Plain(bytes).Should().Contain("Bone Guard A");
    }

    [Fact]
    public async Task ClientEnemies_OncePerFight_AndAgainOnlyWhenTheListChanges()
    {
        var hero = Hero();
        var bytes = await Fight("web", hero, WebFlagOff3aTests.Foes());
        Of(bytes, "Char.Combat.Enemies").Count.Should().BeGreaterThan(1, "the enemy list went out more than once");
        Of(bytes, "Client.Enemies").Should().HaveCount(1, "the names and families did not change during the fight");
        // a second fight sends its own, even against foes of the same names
        var again = await Fight("web", hero, WebFlagOff3aTests.Foes());
        Of(again, "Client.Enemies").Should().HaveCount(1);
        // a change in the list is sent beside the enemy list that shows it
        using var s = new Scope("web", hero);
        var foes = WebFlagOff3aTests.Foes();
        GmcpBridge.EmitWebEnemies(foes, fightStart: true);
        GmcpBridge.EmitCombatEnemiesIfChanged(foes, 1);
        foes.Add(new Monster { Name = "Summoned Imp", Level = 3, HP = 20, MaxHP = 20, IsActive = true, FamilyName = "Demonic" });
        GmcpBridge.EmitCombatEnemiesIfChanged(foes, 2);
        var sent = WebMenu200Tests.Frames(s.Stream.ToArray()).Select(f => f.Package).ToList();
        sent.Should().Equal("Client.Enemies", "Char.Combat.Enemies", "Char.Combat.Enemies", "Client.Enemies");
        Names(Of(s.Stream.ToArray(), "Client.Enemies").Last(), "enemies").Should().Equal("Bone Guard A", "Bone Guard B", "Summoned Imp");
    }

    // ------------------------------------------------------------------ Client.Footer

    private static (byte[] bytes, string text) StatusLine(Character hero, Character? locationPlayer = null, bool screenReader = false)
    {
        using var s = new Scope("web", hero, screenReader: screenReader);
        var t = s.Term(plain: screenReader);
        Invoke(Street(locationPlayer ?? hero, t), typeof(BaseLocation), "ShowStatusLine");
        var bytes = s.Bytes(t);
        return (bytes, Plain(bytes));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClientFooter_CarriesTheValuesTheStatusLinePrints(bool screenReader)
    {
        var hero = Hero(screenReader);
        hero.Experience = GameConfig.GetExperienceForLevel(12) + ((GameConfig.GetExperienceForLevel(13) - GameConfig.GetExperienceForLevel(12)) * 37 + 99) / 100;
        hero.Resurrections = 2;
        hero.Healing = 7;
        var (bytes, text) = StatusLine(hero, screenReader: screenReader);
        var footer = Of(bytes, "Client.Footer").Single();
        footer.GetProperty("potions").GetInt64().Should().Be(7);
        footer.GetProperty("maxPotions").GetInt64().Should().Be(hero.MaxPotions);
        int xp = footer.GetProperty("xpPercent").GetInt32();
        xp.Should().Be(37);
        int lives = footer.GetProperty("lives").GetInt32(), max = footer.GetProperty("maxLives").GetInt32();
        string kind = footer.GetProperty("livesKind").GetString()!;
        string label = Loc.Get(kind == "lives" ? "status.lives" : "status.revives");
        text.Should().Contain($"{label}: {lives}/{max}", "the lives counter as the line prints it");
        text.Should().Contain(screenReader ? Loc.Get("status.xp_to_next", xp) : $"({xp}%)", "the XP percent as the line prints it");
        (lives, max).Should().Be((2, 3));
        // the potions are the potion line's own pair
        Loc.Get("base.potions_remaining").Should().NotBeNullOrEmpty();
        footer.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(new[] { "potions", "maxPotions", "lives", "maxLives", "livesKind", "xpPercent" });
    }

    [Fact]
    public void ClientFooter_LeavesOutWhatTheLineLeavesOut()
    {
        var hero = Hero();
        hero.Level = GameConfig.MaxLevel;
        using (var s = new Scope("web", hero))
        {
            DifficultySystem.CurrentDifficulty = DifficultyMode.Nightmare;   // offline Nightmare: no revives are offered
            var t = s.Term();
            Invoke(Street(hero, t), typeof(BaseLocation), "ShowStatusLine");
            var bytes = s.Bytes(t);
            string text = Plain(bytes);
            text.Should().NotContain(Loc.Get("status.revives")).And.NotContain(Loc.Get("status.lives") + ":").And.NotContain("%)");
            var footer = Of(bytes, "Client.Footer").Single();
            footer.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(new[] { "potions", "maxPotions" },
                "the line prints no lives counter and no XP percent here, so the frame has neither");
        }
    }

    [Fact]
    public void ClientFooter_IsSentOnlyWhenAValueChanged()
    {
        var hero = Hero();
        using var s = new Scope("web", hero);
        var t = s.Term();
        var street = Street(hero, t);
        Invoke(street, typeof(BaseLocation), "ShowStatusLine");
        Invoke(street, typeof(BaseLocation), "ShowStatusLine");
        Of(s.Bytes(t), "Client.Footer").Should().HaveCount(1, "the same values are not sent twice");
        hero.Healing = 1;
        Invoke(street, typeof(BaseLocation), "ShowStatusLine");
        Of(s.Bytes(t), "Client.Footer").Should().HaveCount(2);
        Of(s.Bytes(t), "Client.Footer").Last().GetProperty("potions").GetInt64().Should().Be(1);
        // and nothing but the draw sends it: no frame without a status line
        long before = s.Stream.Length;
        hero.Healing = 2;
        s.Bytes(t).Length.Should().Be((int)before);
    }

    [Fact]
    public void ClientFooter_IsNeverSentForACharacterTheSessionDoesNotPlay()
    {
        var own = Hero();
        var other = Hero();
        other.Name1 = other.Name2 = "Someone Else";
        var (bytes, text) = StatusLine(own, locationPlayer: other);
        text.Should().Contain(Loc.Get("status.hp"), "the line is drawn");
        Of(bytes, "Client.Footer").Should().BeEmpty();
    }

    // ------------------------------------------------------------------ Client.Sheet

    private static async Task<(byte[] bytes, string text)> Sheet(Character own, Character? shown = null, string lang = "en", bool screenReader = false)
    {
        using var s = new Scope("web", own, lang, screenReader);
        var t = s.Term(Enters, plain: screenReader);
        await Await(Invoke(Street(shown ?? own, t), typeof(BaseLocation), "ShowStatus"));
        var bytes = s.Bytes(t);
        return (bytes, Plain(bytes));
    }

    private static Character Geared()
    {
        var hero = Hero();
        hero.King = true;
        hero.MaxMana = 50; hero.Mana = 20;
        int sword = EquipmentDatabase.RegisterDynamic(new Equipment
        {
            Name = "Webtest Blade", Slot = EquipmentSlot.MainHand, WeaponType = WeaponType.Sword, WeaponPower = 19, StrengthBonus = 2,
            MinLevel = 1, Rarity = EquipmentRarity.Common,
        });
        int helm = EquipmentDatabase.RegisterDynamic(new Equipment
        {
            Name = "Webtest Helm", Slot = EquipmentSlot.Head, ArmorClass = 11, ConstitutionBonus = 1, MinLevel = 1, Rarity = EquipmentRarity.Common,
        });
        hero.EquippedItems[EquipmentSlot.MainHand] = sword;
        hero.EquippedItems[EquipmentSlot.Head] = helm;
        return hero;
    }

    [Theory]
    [InlineData("en", false)]
    [InlineData("fr", false)]
    [InlineData("en", true)]
    public async Task ClientSheet_ListsWhatTheSheetPrints_AndEveryValueIsOnTheText(string lang, bool screenReader)
    {
        var hero = Geared();
        hero.ScreenReaderMode = screenReader;
        var (bytes, text) = await Sheet(hero, lang: lang, screenReader: screenReader);
        var sheet = Of(bytes, "Client.Sheet").Should().ContainSingle().Subject;
        using var _ = new LangScope(lang);
        // headings and labels are the text's own words; every value is printed after its label
        sheet.GetProperty("title").GetString().Should().Be(Loc.Get("base.character_status"));
        foreach (var sec in new[] { "basic", "level", "combat", "equipment", "totals" })
            text.Should().Contain(sheet.GetProperty(sec).GetProperty("heading").GetString()!);
        void Rows(JsonElement rows)
        {
            foreach (var r in rows.EnumerateArray())
            {
                string label = r.GetProperty("label").GetString()!, value = r.GetProperty("value").GetString()!;
                Regex.IsMatch(text, Regex.Escape(label) + @"\s*:\s*" + Regex.Escape(value)).Should().BeTrue("{0}: {1} is printed", label, value);
                if (r.TryGetProperty("extra", out var extra) && extra.ValueKind == JsonValueKind.String)
                    text.Should().Contain(value + " " + extra.GetString(), "the row's extra follows its value");
            }
        }
        Rows(sheet.GetProperty("basic").GetProperty("rows"));
        Rows(sheet.GetProperty("level").GetProperty("rows"));
        Rows(sheet.GetProperty("combat").GetProperty("stats"));
        Rows(sheet.GetProperty("totals").GetProperty("rows"));
        sheet.GetProperty("basic").GetProperty("note").GetString().Should().Be(Loc.Get("base.stat_royal_authority").Trim(), "a king's sheet prints Royal Authority");
        text.Should().Contain(sheet.GetProperty("basic").GetProperty("note").GetString()!);
        var combat = sheet.GetProperty("combat");
        Regex.IsMatch(text, Regex.Escape(combat.GetProperty("hp").GetProperty("label").GetString()!) + ": " + Regex.Escape(combat.GetProperty("hp").GetProperty("value").GetString()!)).Should().BeTrue();
        Regex.IsMatch(text, Regex.Escape(combat.GetProperty("mana").GetProperty("label").GetString()!) + ": 20/50").Should().BeTrue();
        // each slot: its label, then its item (with stats) or its empty word
        var eq = sheet.GetProperty("equipment");
        Regex.IsMatch(text, Regex.Escape(eq.GetProperty("style").GetProperty("label").GetString()!) + @"\s*:\s*" + Regex.Escape(eq.GetProperty("style").GetProperty("value").GetString()!)).Should().BeTrue();
        var slots = eq.GetProperty("slots").EnumerateArray().ToList();
        slots.Select(x => x.GetProperty("slot").GetString()).Should().Equal(BaseLocation.SheetSlots.Select(x => x.Slot.ToString()));
        foreach (var sl in slots)
        {
            string label = sl.GetProperty("label").GetString()!, item = sl.GetProperty("item").GetString()!;
            string printed = sl.TryGetProperty("stats", out var st) ? item + " " + st.GetString() : item;
            Regex.IsMatch(text, Regex.Escape(label) + @"\s*:\s*" + Regex.Escape(printed)).Should().BeTrue("{0} {1} is printed", label, printed);
        }
        slots.Where(x => !x.GetProperty("empty").GetBoolean()).Select(x => x.GetProperty("item").GetString()).Should().Equal("Webtest Blade", "Webtest Helm");
        // the bonuses as the totals line prints them
        var totals = sheet.GetProperty("totals");
        var bonuses = totals.GetProperty("bonuses").EnumerateArray().Select(b => b.GetProperty("label").GetString() + " " + b.GetProperty("value").GetString()).ToList();
        bonuses.Should().Equal(Loc.Get("ui.stat_str") + " +2", Loc.Get("ui.stat_con") + " +1");
        foreach (var b in bonuses) text.Should().Contain(b);
        text.Should().Contain(totals.GetProperty("bonusLabel").GetString()!);
    }

    private sealed class LangScope : IDisposable
    {
        private readonly string _saved = GameConfig.Language;
        public LangScope(string lang) { GameConfig.Language = lang; }
        public void Dispose() { GameConfig.Language = _saved; }
    }

    [Fact]
    public async Task ClientSheet_IsOnlyEverTheSessionsOwnCharacter()
    {
        var own = Hero();
        var other = Geared();
        other.Name1 = other.Name2 = "Someone Else";
        var (bytes, text) = await Sheet(own, shown: other);
        text.Should().Contain("Someone Else", "the sheet text is drawn as before");
        Of(bytes, "Client.Sheet").Should().BeEmpty("another character's sheet never goes out as data");
        var (mine, _) = await Sheet(own);
        Of(mine, "Client.Sheet").Single().GetProperty("name").GetString().Should().Be(own.DisplayName);
    }

    [Fact]
    public async Task ClientSheet_IsSentOncePerDraw_AndNotToASpectator()
    {
        var hero = Hero();
        using var s = new Scope("web", hero);
        var t = s.Term(Enters);
        s.Ctx!.Terminal = t;
        var spectatorStream = new MemoryStream();
        var spectator = new TerminalEmulator(new MemoryStream(), spectatorStream);
        t.AddSpectatorStream(spectator);
        await Await(Invoke(Street(hero, t), typeof(BaseLocation), "ShowStatus"));
        Invoke(Street(hero, t), typeof(BaseLocation), "ShowStatusLine");
        spectator.StreamWriterInternal?.Flush();
        var own = s.Bytes(t);
        Of(own, "Client.Sheet").Should().HaveCount(1, "one frame for the four pages of one sheet");
        Of(own, "Client.Footer").Should().HaveCount(1);
        var mirrored = spectatorStream.ToArray();
        Plain(mirrored).Should().Contain(Loc.Get("base.basic_information"), "the spectator sees the sheet");
        WebMenu200Tests.Frames(mirrored).Should().BeEmpty("no GMCP frame is mirrored to a spectator");
    }

    // ------------------------------------------------------------------ Client.Map

    private static async Task<(byte[] bytes, string text, DungeonLocation d, DungeonFloor floor)> MapScreen(Character hero, Action<DungeonFloor>? setup = null, bool screenReader = false)
    {
        using var s = new Scope("web", hero, screenReader: screenReader);
        var t = s.Term(Enters, plain: screenReader);
        var (d, floor) = WebFlagOff200Tests.Dungeon(hero, t);
        setup?.Invoke(floor);
        await Await(Invoke(d, typeof(DungeonLocation), "ShowDungeonMap"));
        var bytes = s.Bytes(t);
        return (bytes, Plain(bytes), d, floor);
    }

    private static List<(int x, int y, string kind)> Rooms(JsonElement map) =>
        map.GetProperty("rooms").EnumerateArray().Select(r => (r.GetProperty("x").GetInt32(), r.GetProperty("y").GetInt32(), r.GetProperty("kind").GetString()!)).ToList();

    private static List<(int, int, int, int)> Links(JsonElement map) =>
        map.GetProperty("links").EnumerateArray().Select(l => (l.GetProperty("x1").GetInt32(), l.GetProperty("y1").GetInt32(), l.GetProperty("x2").GetInt32(), l.GetProperty("y2").GetInt32())).ToList();

    [Fact]
    public async Task ClientMap_LeavesOutAHiddenExit_AndAnUnvisitedNeighbour()
    {
        // q1 (the current room) has a visible exit east to u3, never visited, and a hidden, unrevealed exit south to h5
        var (bytes, text, _, floor) = await MapScreen(Hero());
        var map = Of(bytes, "Client.Map").Should().ContainSingle().Subject;
        string json = map.GetRawText();
        var rooms = Rooms(map);
        rooms.Should().BeEquivalentTo(new[] { (0, 0, "here"), (0, -1, "cleared"), (-1, 0, "safe") }, "only the rooms the player has seen");
        rooms.Should().NotContain(r => r.x == 1 && r.y == 0, "the unvisited neighbour east (u3) is not in the frame");
        rooms.Should().NotContain(r => r.x == 0 && r.y == 1, "the room behind the hidden exit (h5) is not in the frame");
        Links(map).Should().NotContain(l => l.Item4 == 1 || l.Item3 == 1, "no corridor to either");
        foreach (var leak in new[] { "u3", "h5", "Secret Treasury", "Room h5", "c2", "e4" }) json.Should().NotContain(leak);
        map.GetProperty("room").GetString().Should().Be("q1", "the current room's id, as Room.Info sends it");
        // the text map does draw them, as before: a "?" east and south, and the south corridor
        text.Should().Contain("@   ?");
        text.Should().Contain("│");
    }

    [Fact]
    public async Task ClientMap_AHiddenExitStaysOut_EvenWhenTheRoomBehindItWasSeen()
    {
        // h5 explored from elsewhere; q1's way to it is still hidden. The text map draws that corridor; the frame does not.
        var (bytes, text, _, _) = await MapScreen(Hero(), f => f.Rooms.Single(r => r.Id == "h5").IsExplored = true);
        var map = Of(bytes, "Client.Map").Single();
        Rooms(map).Should().Contain((0, 1, "safe"), "a room the player has been in is on the map");
        Links(map).Should().NotContain((0, 0, 0, 1), "the corridor through the hidden exit is not");
        text.Should().Contain("│", "the text map draws it, as before");
    }

    [Fact]
    public async Task ClientMap_IsWhatTheTextMapDraws_RoomByRoom()
    {
        var (bytes, text, d, floor) = await MapScreen(Hero(), f =>
        {
            var c2 = f.Rooms.Single(r => r.Id == "c2");
            c2.Exits[Direction.West] = new RoomExit("e4", "a bend");   // a corridor the map cannot draw (not beside it)
            var e4 = f.Rooms.Single(r => r.Id == "e4");
            e4.HasMonsters = true;
            f.Rooms.Single(r => r.Id == "q1").Exits[Direction.East].IsHidden = false;
        });
        var map = Of(bytes, "Client.Map").Single();
        var positions = (Dictionary<string, (int x, int y)>)Invoke(d, typeof(DungeonLocation), "BuildRoomPositionMap")!;
        int minX = positions.Values.Min(p => p.x), minY = positions.Values.Min(p => p.y);
        var lines = text.Split('\n');
        int top = Array.FindIndex(lines, l => l.Contains(Loc.Get("dungeon.map_legend_title"))) - 1;   // the first room row
        char At(int x, int y) { string l = lines[top + (y - minY) * 2]; int c = 4 + (x - minX) * 4; return c < l.Length ? l[c] : ' '; }
        var glyph = new Dictionary<string, char> { ["here"] = '@', ["cleared"] = '#', ["monsters"] = '█', ["safe"] = '·', ["stairs"] = '>', ["boss"] = 'B' };
        foreach (var (x, y, kind) in Rooms(map)) At(x, y).Should().Be(glyph[kind], "the room at {0},{1} is drawn as {2}", x, y, kind);
        foreach (var (x1, y1, x2, y2) in Links(map))
        {
            if (y1 == y2) lines[top + (y1 - minY) * 2].Substring(4 + (x2 - minX) * 4 + 1, 3).Should().Be("───");
            else lines[top + (y1 - minY) * 2 + 1][4 + (x1 - minX) * 4].Should().Be('│');
        }
        Links(map).Should().BeEquivalentTo(new[] { (0, 0, -1, 0) }, "q1 west to e4 is drawn; c2 has no corridor south drawn and its west exit is not beside it");
        map.GetProperty("explored").GetInt32().Should().Be(floor.Rooms.Count(r => r.IsExplored));
        map.GetProperty("total").GetInt32().Should().Be(floor.Rooms.Count);
        text.Should().Contain(Loc.Get("dungeon.map_header", 6, DungeonLocation.GetThemeShortName(DungeonTheme.Catacombs),
            map.GetProperty("explored").GetInt32(), map.GetProperty("total").GetInt32(), map.GetProperty("cleared").GetInt32()));
        text.Should().Contain(map.GetProperty("location").GetString()!);
    }

    [Fact]
    public async Task ClientMap_IsSentOnlyByTheMapScreen_NotTheScreenReaderNavigator()
    {
        var hero = Hero(screenReader: true);
        var (bytes, _, _, _) = await MapScreen(hero, screenReader: true);
        Of(bytes, "Client.Map").Should().BeEmpty("the screen reader's map is a text navigator; no map is drawn");
        // and a room draw sends none
        using var s = new Scope("web", Hero());
        var t = s.Term();
        var (d, _) = WebFlagOff200Tests.Dungeon(Hero(), t);
        Invoke(d, typeof(DungeonLocation), "DisplayLocation");
        Of(s.Bytes(t), "Client.Map").Should().BeEmpty();
    }
}
