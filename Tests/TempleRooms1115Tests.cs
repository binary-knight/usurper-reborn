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
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.0 Temple gods piece 7: the Temple's rooms. The hall screen lists the Nave of the Gods, the
/// Undercroft, the Faith, the Old Stones and the Halls of Memory (plus the Chapel and the Deep
/// Temple where they apply), each room routes its own keys, an old hall key prints a pointer and
/// does nothing else, the Temple Confession is gone, the Nave's W, O and Altars screen cover every
/// god (canon and player-gods, never Manwe), Evil players are refused offerings to good gods in the
/// Nave and welcomed in the Undercroft, desecration lives only in the Undercroft, and the three
/// menu modes carry the same keys within 80 columns in all five languages.
/// </summary>
[Collection("SharedGameSingletons")]
public class TempleRooms1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };
    private static readonly TempleLocation.TempleRoom[] Rooms =
    {
        TempleLocation.TempleRoom.Nave, TempleLocation.TempleRoom.Undercroft, TempleLocation.TempleRoom.Faith,
        TempleLocation.TempleRoom.OldStones, TempleLocation.TempleRoom.Memory
    };

    // ---------------- Rig ----------------

    /// <summary>
    /// A Temple on a scripted terminal. The Temple's own switch helper uses the GodSystem singleton
    /// (as the game does), so the rig uses it too and clears the hero's god when disposed.
    /// </summary>
    private sealed class Rig : IDisposable
    {
        public TempleLocation Temple = null!;
        public MemoryStream Output = null!;
        public TerminalEmulator Term = null!;
        public Character Hero = null!;
        public GodSystem Gods = null!;

        public string Text()
        {
            Term.StreamWriterInternal?.Flush();
            return Regex.Replace(Encoding.UTF8.GetString(Output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "").Replace("\r", "");
        }

        public void Dispose() => GodRegistry.SetWorshippedGod(Hero, null, Gods);
    }

    private static Rig Make(string name, params string[] lines)
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(lines.Concat(Enumerable.Repeat("", 40))), output);
        var gods = UsurperRemake.GodSystemSingleton.Instance;
        var temple = new TempleLocation(term, null!, gods);
        var hero = StatRewards1115Tests.Fresh(name);
        hero.Gold = 100_000;
        hero.ChivNr = 3;
        hero.DarkNr = 3;
        hero.Healing = 5;
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(temple, hero);
        return new Rig { Temple = temple, Output = output, Term = term, Hero = hero, Gods = gods };
    }

    private static void MakeEvil(Character c) { c.Chivalry = 0; c.Darkness = 900; }

    private static object? Invoke(object target, string method, params object[] args)
    {
        var m = typeof(TempleLocation).GetMethod(method, F);
        m.Should().NotBeNull($"{method} must exist");
        try { return m!.Invoke(target, args); }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    private static async Task Run(object target, string method, params object[] args)
    {
        var task = (Task)Invoke(target, method, args)!;
        await task;
    }

    private static async Task<T> Get<T>(object target, string method, params object[] args)
    {
        var task = (Task<T>)Invoke(target, method, args)!;
        return await task;
    }

    private static List<TempleLocation.TempleMenuItem> Top(Rig r) =>
        (List<TempleLocation.TempleMenuItem>)Invoke(r.Temple, "TopMenuItems")!;

    private static Task<List<TempleLocation.TempleMenuItem>> Items(Rig r, TempleLocation.TempleRoom room) =>
        Get<List<TempleLocation.TempleMenuItem>>(r.Temple, "RoomItems", room);

    private static Func<Task>? RoomAction(Rig r, TempleLocation.TempleRoom room, string key) =>
        (Func<Task>?)Invoke(r.Temple, "RoomAction", room, key);

    private static TempleLocation.ImmortalGodInfo PlayerGod(string name, string alignment) =>
        new() { DivineName = name, GodAlignment = alignment, GodLevel = 3, Believers = 2 };

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj"))) dir = dir.Parent;
        return dir!.FullName;
    }

    private static string Source() => File.ReadAllText(Path.Combine(Root(), "Scripts/Locations/TempleLocation.cs"));

    private static string Body(string src, string signature)
    {
        int start = src.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, signature);
        var next = new Regex(@"\n    (private|public|internal|protected) ").Match(src, start + signature.Length);
        return next.Success ? src.Substring(start, next.Index - start) : src.Substring(start);
    }

    // ---------------- The hall screen and its rooms ----------------

    [Fact]
    public void TheHall_ListsTheRooms_AndEachListedKeyOpensOne()
    {
        using var r = Make("TrHall");
        var keys = Top(r).Select(i => i.Key).ToList();
        keys.Should().ContainInOrder("A", "U", "F", "P");
        keys.Last().Should().Be("R");
        foreach (var k in keys.Where(k => k != "R"))
            Invoke(r.Temple, "TopAction", k).Should().NotBeNull($"hall key {k} opens its room");
        var src = Source();
        Body(src, "private Func<Task>? TopAction(").Should()
            .Contain("\"A\" => () => RunRoom(TempleRoom.Nave)")
            .And.Contain("\"U\" => () => RunRoom(TempleRoom.Undercroft)")
            .And.Contain("\"F\" => () => RunRoom(TempleRoom.Faith)")
            .And.Contain("\"P\" => () => RunRoom(TempleRoom.OldStones)")
            .And.Contain("\"H\" => () => RunRoom(TempleRoom.Memory)")
            .And.Contain("\"M\" => VisitMeditationChapel")
            .And.Contain("\"T\" => EnterDeepTemple");
    }

    [Fact]
    public async Task EachRoom_RoutesEveryKeyItLists()
    {
        using var r = Make("TrRoute");
        GodRegistry.SetWorshippedGod(r.Hero, "Mortis", r.Gods).Should().BeTrue();   // so Y shows in both halls
        foreach (var room in Rooms)
        {
            var items = await Items(r, room);
            items.Last().Key.Should().Be("R", $"{room} ends with its way back");
            foreach (var item in items.Where(i => i.Key != "R"))
                RoomAction(r, room, item.Key).Should().NotBeNull($"{room} routes its listed key {item.Key}");
        }
        (await Items(r, TempleLocation.TempleRoom.Nave)).Select(i => i.Key).Should().Equal("W", "Y", "O", "A", "R");
        (await Items(r, TempleLocation.TempleRoom.Undercroft)).Select(i => i.Key).Should().Equal("W", "Y", "O", "D", "R");
        (await Items(r, TempleLocation.TempleRoom.OldStones)).Select(i => i.Key).Should().Contain("P");
        (await Items(r, TempleLocation.TempleRoom.Faith)).Select(i => i.Key).Should().Contain("M");

        var map = Body(Source(), "private Func<Task>? RoomAction(");
        map.Should().Contain("(TempleRoom.Nave, \"W\") => () => ProcessWorship(TempleRoom.Nave)")
            .And.Contain("(TempleRoom.Nave, \"Y\") => ProcessDailyPrayer")
            .And.Contain("(TempleRoom.Nave, \"O\") => () => ProcessOffering(TempleRoom.Nave)")
            .And.Contain("(TempleRoom.Nave, \"A\") => ShowAltarsScreen")
            .And.Contain("(TempleRoom.Undercroft, \"W\") => () => ProcessWorship(TempleRoom.Undercroft)")
            .And.Contain("(TempleRoom.Undercroft, \"O\") => () => ProcessOffering(TempleRoom.Undercroft)")
            .And.Contain("(TempleRoom.Undercroft, \"D\") => ProcessDesecrateAltar")
            .And.Contain("(TempleRoom.Faith, \"M\") => ShowFaithRecruitment")
            .And.Contain("(TempleRoom.Faith, \"C\") => VisitInnerSanctum")
            .And.Contain("(TempleRoom.OldStones, \"P\") => DisplayOldGodsProphecies")
            .And.Contain("(TempleRoom.OldStones, \"E\") => ExamineAncientStones")
            .And.Contain("(TempleRoom.Memory, \"K\") => ShowHallOfTheFallen")
            .And.Contain("(TempleRoom.Memory, \"U\") => ProcessRiteOfReturn")
            .And.Contain("(TempleRoom.Memory, \"V\") => ShowAscendedStatues");
    }

    [Fact]
    public async Task ARoom_IgnoresAKeyItDoesNotList()
    {
        // Y is the Undercroft's key only for a dark god's follower; with no god it is not listed
        using var r = Make("TrRoomOnly", "Y", "R");
        await Run(r.Temple, "RunRoom", TempleLocation.TempleRoom.Undercroft);
        string text = r.Text();
        text.Should().Contain(Loc.Get("temple.invalid_choice"));
        text.Should().NotContain(Loc.Get("temple.must_worship_to_pray"), "the unlisted key does nothing");
    }

    // ---------------- Desecration: the Undercroft only ----------------

    [Fact]
    public void Desecration_IsOnlyInTheUndercroft()
    {
        using var r = Make("TrDesecrate");
        RoomAction(r, TempleLocation.TempleRoom.Undercroft, "D").Should().NotBeNull();
        foreach (var room in Rooms.Where(x => x != TempleLocation.TempleRoom.Undercroft))
            RoomAction(r, room, "D").Should().BeNull($"no desecration in {room}");
        var src = Source();
        Regex.Matches(src, @"\bProcessDesecrateAltar\b").Count.Should().Be(2, "its definition and the Undercroft's D");
    }

    // ---------------- Old keys: a pointer, never an action ----------------

    [Theory]
    [InlineData("C", "temple.moved.offer")]
    [InlineData("I", "temple.moved.offer")]
    [InlineData("$", "temple.moved.offer")]
    [InlineData("J", "temple.moved.worship")]
    [InlineData("L", "temple.moved.worship")]
    [InlineData("W", "temple.moved.worship")]
    [InlineData("Y", "temple.moved.pray")]
    [InlineData("S", "temple.moved.altars")]
    [InlineData("G", "temple.moved.altars")]
    [InlineData("E", "temple.moved.stones")]
    [InlineData("K", "temple.moved.memory")]
    [InlineData("V", "temple.moved.memory")]
    [InlineData("N", "temple.moved.faith")]
    [InlineData("D", "temple.moved.desecrate")]
    [InlineData("O", "temple.moved.confession")]
    public async Task AnOldHallKey_PrintsItsPointer_AndDoesNothingElse(string key, string pointerKey)
    {
        using var r = Make("TrOld" + (key == "$" ? "Gold" : key));
        GodRegistry.SetWorshippedGod(r.Hero, "Solarius", r.Gods).Should().BeTrue();
        r.Hero.Chivalry = 500;
        var before = (r.Hero.Gold, r.Hero.Chivalry, r.Hero.Darkness, r.Hero.DesecrationsToday, r.Hero.ConfessionsToday,
                      r.Hero.Healing, r.Hero.GodFavor, GodRegistry.GetWorshippedGod(r.Hero, r.Gods));
        bool leaves = await Get<bool>(r.Temple, "RouteTopLevel", key);
        leaves.Should().BeFalse();
        string text = r.Text();
        text.Should().Contain(Loc.Get(pointerKey).Split(' ')[0]);
        Regex.Replace(text, @"\s+", " ").Should().Contain(Regex.Replace(Loc.Get(pointerKey), @"\s+", " "));
        (r.Hero.Gold, r.Hero.Chivalry, r.Hero.Darkness, r.Hero.DesecrationsToday, r.Hero.ConfessionsToday,
         r.Hero.Healing, r.Hero.GodFavor, GodRegistry.GetWorshippedGod(r.Hero, r.Gods)).Should().Be(before, "a pointer acts on nothing");
    }

    // ---------------- Temple Confession: gone, its save field kept ----------------

    [Fact]
    public void TheTempleConfession_IsGone_AndItsSaveFieldStaysReadable()
    {
        var src = Source();
        src.Should().NotContain("ProcessConfession").And.NotContain("ConfessionsToday").And.NotContain("temple.menu_confess");
        src.Should().NotContain("GodAct.Confession", "the confession taboo is recorded at the Church");
        File.ReadAllText(Path.Combine(Root(), "Scripts/Locations/ChurchLocation.cs"))
            .Should().Contain("GodDeedSystem.Record(currentPlayer, GodAct.Confession, terminal);");

        var c = StatRewards1115Tests.Fresh("TrConfessSave");
        c.ConfessionsToday = 2;
        StatRewards1115Tests.RoundTrip(c).ConfessionsToday.Should().Be(2, "an old save's counter still loads");
    }

    // ---------------- Worship: one path, through GodSwitchSystem ----------------

    [Fact]
    public async Task NaveWorship_LeavingACanonGodForAnother_GoesThroughTheSwitchRule()
    {
        // lost faith? yes; send a note? no; choose Mortis; confirm
        using var r = Make("TrWorship", "Y", "N", "Mortis", "Y");
        GodRegistry.SetWorshippedGod(r.Hero, "Solarius", r.Gods).Should().BeTrue();
        FavorSystem.Bind(r.Hero, r.Gods);
        r.Hero.GodFavor = 40;
        await Run(r.Temple, "ProcessWorship", TempleLocation.TempleRoom.Nave);
        GodRegistry.GetWorshippedGod(r.Hero, r.Gods)!.Value.Name.Should().Be("Mortis");
        FavorSystem.GetFavor(r.Hero, r.Gods).Should().Be(0, "a new god starts at Favor 0");
        r.Hero.AngeredGodName.Should().Be("Solarius", "the left god's wrath follows");
        r.Hero.DivineWrathLevel.Should().BeGreaterThan(0);
        r.Hero.BetrayedForGodName.Should().Be("Mortis");
        r.Hero.LastGodSwitchDay.Should().Be(GodSwitchSystem.Today());
    }

    [Fact]
    public async Task NaveWorship_ListsPlayerGods_AndChoosingOneGoesThroughTheSwitchRule()
    {
        // lost faith in Solarius? yes; note? no; choose the player-god; confirm
        using var r = Make("TrWorshipPg", "Y", "N", "Zephyrine", "Y");
        r.Temple.ImmortalGodsForTests = new() { PlayerGod("Zephyrine", "Light") };
        GodRegistry.SetWorshippedGod(r.Hero, "Solarius", r.Gods).Should().BeTrue();
        FavorSystem.Bind(r.Hero, r.Gods);
        r.Hero.GodFavor = 40;
        await Run(r.Temple, "ProcessWorship", TempleLocation.TempleRoom.Nave);
        var now = GodRegistry.GetWorshippedGod(r.Hero, r.Gods)!.Value;
        (now.Name, now.IsCanon).Should().Be(("Zephyrine", false));
        r.Hero.AngeredGodName.Should().Be("Solarius");
        r.Hero.BetrayedForGodName.Should().Be("Zephyrine", "the wrath names the player-god chosen next");
        r.Text().Should().Contain("Zephyrine");
    }

    [Fact]
    public void Worship_TheOldJAndLPathsAreFoldedIntoW_AndEveryWriteIsTheSwitchHelper()
    {
        var src = Source();
        src.Should().NotContain("LeaveImmortalFaith");
        Regex.Matches(src, @"\bWorshipImmortalGod\(").Count.Should().Be(2, "its definition and the call from W");
        Body(src, "private async Task ProcessWorship(").Should().Contain("await WorshipImmortalGod(chosenPlayerGod);");
        Regex.Matches(src, "GodChangeBy.Player").Count.Should().Be(1, "only SwitchGodAsync switches by the player");
    }

    // ---------------- Offerings: gold and goods, canon and player-gods ----------------

    [Fact]
    public async Task Offer_GoldAtACanonAltar()
    {
        using var r = Make("TrOfferGold", "Solarius", "G", "1000", "Y");
        GodRegistry.SetWorshippedGod(r.Hero, "Solarius", r.Gods).Should().BeTrue();
        FavorSystem.Bind(r.Hero, r.Gods);
        await Run(r.Temple, "ProcessOffering", TempleLocation.TempleRoom.Nave);
        r.Hero.Gold.Should().Be(99_000);
    }

    [Fact]
    public async Task Offer_GoodsAtYourOwnCanonAltar()
    {
        using var r = Make("TrOfferGoods", "Solarius", "I", "H", "2", "Y");
        GodRegistry.SetWorshippedGod(r.Hero, "Solarius", r.Gods).Should().BeTrue();
        FavorSystem.Bind(r.Hero, r.Gods);
        await Run(r.Temple, "ProcessOffering", TempleLocation.TempleRoom.Nave);
        r.Hero.Healing.Should().Be(3, "two potions laid on the altar");
    }

    [Fact]
    public async Task Offer_GoldAndGoodsAtYourOwnPlayerGodsAltar()
    {
        using var r = Make("TrOfferPg", "Zephyrine", "G", "500", "", "Zephyrine", "I", "H", "1", "Y");
        r.Temple.ImmortalGodsForTests = new() { PlayerGod("Zephyrine", "Light") };
        GodRegistry.SetWorshippedGod(r.Hero, "Zephyrine", r.Gods).Should().BeTrue();
        FavorSystem.Bind(r.Hero, r.Gods);
        await Run(r.Temple, "ProcessOffering", TempleLocation.TempleRoom.Nave);
        r.Hero.Gold.Should().Be(99_500, "gold at the player-god's altar");
        await Run(r.Temple, "ProcessOffering", TempleLocation.TempleRoom.Nave);
        r.Hero.Healing.Should().Be(4, "a potion at the player-god's altar");
    }

    [Fact]
    public async Task Offer_AnotherPlayerGodsAltar_TakesOnlyItsFollowers()
    {
        using var r = Make("TrOfferPgNo", "Zephyrine", "G", "500");
        r.Temple.ImmortalGodsForTests = new() { PlayerGod("Zephyrine", "Light") };
        GodRegistry.SetWorshippedGod(r.Hero, "Solarius", r.Gods).Should().BeTrue();
        await Run(r.Temple, "ProcessOffering", TempleLocation.TempleRoom.Nave);
        r.Hero.Gold.Should().Be(100_000);
        r.Text().Should().Contain(Loc.Get("temple.offer.followers_only", "Zephyrine"));
    }

    // ---------------- Evil players: refused in the Nave, welcome in the Undercroft ----------------

    [Theory]
    [InlineData("Solarius", false)]
    [InlineData("Zephyrine", true)]
    public async Task Evil_TheNaveRefusesAnOfferingToAGoodGod(string god, bool playerGod)
    {
        // 1.2.0 Temple gods piece 7 leftover: the Nave now exempts an Evil follower's own good
        // god, so this worships a DIFFERENT good god than the one offered to, to keep testing
        // the refusal for a god that is not the player's own.
        using var r = Make("TrEvilNave" + god, god, "G", "1000", "Y");
        r.Temple.ImmortalGodsForTests = new() { PlayerGod("Zephyrine", "Light") };
        MakeEvil(r.Hero);
        GodRegistry.SetWorshippedGod(r.Hero, "Amara", r.Gods).Should().BeTrue();
        await Run(r.Temple, "ProcessOffering", TempleLocation.TempleRoom.Nave);
        r.Hero.Gold.Should().Be(100_000, "nothing is taken");
        var flat = Regex.Replace(r.Text(), @"\s+", " ");
        flat.Should().Contain(Regex.Replace(Loc.Get("temple.offer.refused_evil", god), @"\s+", " "));
        flat.Should().Contain(Loc.Get("temple.offer.refused_evil_hint"));
        playerGod.Should().Be(!GodRegistry.IsCanon(god));
    }

    [Fact]
    public async Task Evil_TheUndercroftWelcomes_AndTakesTheirOffering()
    {
        using var r = Make("TrEvilUnder", "Umbrath", "G", "1000", "Y");
        MakeEvil(r.Hero);
        await Run(r.Temple, "ProcessOffering", TempleLocation.TempleRoom.Undercroft);
        r.Hero.Gold.Should().Be(99_000, "the dark altar takes an Evil hand's gold");

        using var w = Make("TrEvilWelcome");
        MakeEvil(w.Hero);
        Invoke(w.Temple, "DrawRoom", TempleLocation.TempleRoom.Undercroft, await Items(w, TempleLocation.TempleRoom.Undercroft));
        w.Text().Should().Contain(Loc.Get("temple.undercroft.welcome_dark"));
        Invoke(w.Temple, "DrawRoom", TempleLocation.TempleRoom.Nave, await Items(w, TempleLocation.TempleRoom.Nave));
        // The line may wrap at 80 columns; compare with runs of whitespace collapsed.
        static string Flat(string t) => System.Text.RegularExpressions.Regex.Replace(t, @"\s+", " ");
        Flat(w.Text()).Should().Contain(Flat(Loc.Get("temple.nave.evil_unwelcome")));
    }

    [Fact]
    public async Task TheUndercroft_ServesOnlyTheDarkAltars()
    {
        using var r = Make("TrUnderAltars");
        r.Temple.ImmortalGodsForTests = new() { PlayerGod("Zephyrine", "Light"), PlayerGod("Nyxara", "Dark") };
        var altars = await Get<List<TempleLocation.AltarPick>>(r.Temple, "AltarsFor", TempleLocation.TempleRoom.Undercroft);
        altars.Select(a => a.Name).Should().BeEquivalentTo("Discordia", "Mortis", "Umbrath", "Nyxara");
    }

    [Fact]
    public void TheEvilWard_NoLongerBarsTheTemple_TheChurchKeepsIt()
    {
        var c = StatRewards1115Tests.Fresh("TrWard");
        MakeEvil(c);
        AlignmentSystem.Instance.CanAccessLocation(c, GameLocation.Temple).canAccess.Should().BeTrue();
        AlignmentSystem.Instance.CanAccessLocation(c, GameLocation.Church).canAccess.Should().BeFalse();
    }

    // ---------------- Manwe: never on an altar or a ranking ----------------

    [Fact]
    public async Task Manwe_IsOnNoAltarAndNoRanking()
    {
        using var r = Make("TrManwe", "");
        var nave = await Get<List<TempleLocation.AltarPick>>(r.Temple, "AltarsFor", TempleLocation.TempleRoom.Nave);
        nave.Select(a => a.Name).Should().BeEquivalentTo(GameConfig.CanonGodNames);
        nave.Select(a => a.Name).Should().NotContain(GameConfig.SupremeCreatorName);
        r.Gods.GetGod(GameConfig.SupremeCreatorName).Should().NotBeNull("Manwe exists in the god system");
        await Run(r.Temple, "ShowAltarsScreen");
        r.Text().Should().NotContain(GameConfig.SupremeCreatorName);
        r.Text().Should().Contain("Solarius");
    }

    [Fact]
    public async Task TheAltarsScreen_ShowsTheDevotion_TheRankingAndTheWeek()
    {
        using var r = Make("TrAltars", "");
        GodRegistry.SetWorshippedGod(r.Hero, "Judicar", r.Gods).Should().BeTrue();
        FavorSystem.Bind(r.Hero, r.Gods);
        r.Hero.GodFavor = 60;
        await Run(r.Temple, "ShowAltarsScreen");
        string text = r.Text();
        text.Should().Contain(Loc.Get("temple.devotion.favor", "Judicar", 60, Loc.Get("temple.tier.zealot")));
        text.Should().Contain(Loc.Get("temple.god_ranking_header"));
        text.Should().Contain(MiracleSystem.TempleLine(r.Hero, r.Gods));
    }

    // ---------------- Menus: the same keys in every mode, 80 columns, five languages ----------------

    private static string RenderTop(Rig r, string mode)
    {
        bool oldCompact = GameConfig.CompactMode;
        try
        {
            GameConfig.CompactMode = mode == "bbs";
            r.Hero.ScreenReaderMode = mode == "screenreader";
            ((Task)Invoke(r.Temple, "DisplayMenu", true)!).GetAwaiter().GetResult();
            return r.Text();
        }
        finally { GameConfig.CompactMode = oldCompact; r.Hero.ScreenReaderMode = false; }
    }

    private static async Task<string> RenderRoom(Rig r, TempleLocation.TempleRoom room, string mode)
    {
        bool oldCompact = GameConfig.CompactMode;
        try
        {
            GameConfig.CompactMode = mode == "bbs";
            r.Hero.ScreenReaderMode = mode == "screenreader";
            Invoke(r.Temple, "DrawRoom", room, await Items(r, room));
            return r.Text();
        }
        finally { GameConfig.CompactMode = oldCompact; r.Hero.ScreenReaderMode = false; }
    }

    [Theory]
    [InlineData("visual")]
    [InlineData("screenreader")]
    [InlineData("bbs")]
    public async Task EveryMenu_CarriesEveryListedKey(string mode)
    {
        using var r = Make("TrKeys" + mode);
        GodRegistry.SetWorshippedGod(r.Hero, "Mortis", r.Gods).Should().BeTrue();
        string pattern = mode == "screenreader" ? @"^([A-Z$])\. " : @"\[([A-Z$])\]";
        var shown = Regex.Matches(RenderTop(r, mode), pattern, RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();
        shown.Should().Equal(Top(r).Select(i => i.Key), "the hall screen lists exactly its keys, in order");
        foreach (var room in Rooms)
        {
            using var rr = Make("TrKeys" + mode + room);
            GodRegistry.SetWorshippedGod(rr.Hero, "Mortis", rr.Gods).Should().BeTrue();
            var roomShown = Regex.Matches(await RenderRoom(rr, room, mode), pattern, RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();
            roomShown.Should().Equal((await Items(rr, room)).Select(i => i.Key), $"{room} lists exactly its keys");
        }
    }

    [Fact]
    public async Task EveryMenuAndPointer_FitsEightyColumns_InAllFiveLanguages()
    {
        string prev = GameConfig.Language;
        try
        {
            foreach (var lang in Langs)
            {
                GameConfig.Language = lang;
                foreach (var mode in new[] { "visual", "screenreader", "bbs" })
                {
                    using var r = Make("TrWidth" + lang + mode);
                    GodRegistry.SetWorshippedGod(r.Hero, "Mortis", r.Gods).Should().BeTrue();
                    MakeEvil(r.Hero);
                    AtMost80(RenderTop(r, mode), $"{lang} {mode} hall");
                    foreach (var room in Rooms)
                    {
                        using var rr = Make("TrWidth" + lang + mode + room);
                        GodRegistry.SetWorshippedGod(rr.Hero, "Mortis", rr.Gods).Should().BeTrue();
                        MakeEvil(rr.Hero);
                        AtMost80(await RenderRoom(rr, room, mode), $"{lang} {mode} {room}");
                    }
                }
                foreach (var key in new[] { "C", "J", "S", "E", "K", "N", "D", "O", "Y" })
                {
                    using var p = Make("TrWidthPtr" + lang + key);
                    await Get<bool>(p.Temple, "RouteTopLevel", key);
                    AtMost80(p.Text(), $"{lang} pointer {key}");
                }
                using var e = Make("TrWidthRefuse" + lang, "Solarius");
                MakeEvil(e.Hero);
                await Run(e.Temple, "ProcessOffering", TempleLocation.TempleRoom.Nave);
                AtMost80(e.Text(), $"{lang} refusal");
            }
        }
        finally { GameConfig.Language = prev; }
    }

    private static void AtMost80(string text, string context)
    {
        foreach (var line in text.Split('\n'))
            line.Length.Should().BeLessThanOrEqualTo(80, $"{context}: \"{line}\"");
    }

    [Fact]
    public void TheHallOfTheAscended_HasALocLabel()
    {
        var src = Source();
        src.Should().NotContain("\" Hall of the Ascended\"");
        Body(src, "private async Task<List<TempleMenuItem>> RoomItems(").Should()
            .Contain("Item(\"V\", \"temple.memory.ascended\", \"temple.memory.ascended\")");
    }

    // ---------------- Loc: every new key in all five files; French Temple text uses vous ----------------

    [Fact]
    public void EveryRoomKey_IsInAllFiveFiles_AndTheFrenchUsesVous()
    {
        var src = Source();
        var keys = Regex.Matches(src, "\"(temple\\.(?:top|room|nave|undercroft|faith_hall|stones|memory|moved|offer|devotion|tier|align|pgod)\\.[a-z_]+|temple\\.selected_altar)\"")
            .Select(m => m.Groups[1].Value).Distinct().ToList();
        keys.Count.Should().BeGreaterThan(40);
        foreach (var lang in Langs)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), "Localization", lang + ".json")));
            foreach (var k in keys)
            {
                doc.RootElement.TryGetProperty(k, out var v).Should().BeTrue($"{lang} has {k}");
                if (lang == "fr")
                    Regex.IsMatch(v.GetString()!, @"(?i)\b(tu|te|ton|ta|tes|toi)\b|\bt'").Should().BeFalse($"fr {k} uses vous: {v.GetString()}");
            }
        }
    }
}
