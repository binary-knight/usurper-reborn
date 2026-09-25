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
using G = MainStreetLocation.StreetGroup;
using P = MainStreetLocation.StreetPlace;

namespace UsurperReborn.Tests;

/// <summary>v1.1.13: Main Street as districts and hubs: what each tier shows, what each key does, and the texts.</summary>
[Collection("SharedGameSingletons")]
public class MainStreetDistricts1113Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly FieldInfo OnlineFlag =
        typeof(UsurperRemake.BBS.DoorMode).GetField("_onlineMode", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static MainStreetLocation.StreetView View(int tier, bool online = false, bool settlement = false) => new(tier, online, settlement);

    private static string Keys(IEnumerable<string> keys) => string.Join(" ", keys);

    private static string StreetKeys(int tier, bool online) =>
        Keys(MainStreetLocation.MainStreetLines(View(tier, online), 0).Select(l => l.Key));

    private static string PlaceKeys(G group, int tier, bool online = false, bool settlement = false) =>
        Keys(MainStreetLocation.PlacesIn(group, View(tier, online, settlement)).Select(e => e.Key));

    // ---------- what each tier shows ----------

    [Theory]
    [InlineData(1, false, "D I M G S ~ ? Q")]
    [InlineData(2, false, "D I E M G H C S N ~ ? Q")]
    [InlineData(3, false, "D I E T A M G H C S N ~ ? Q")]
    [InlineData(1, true, "D I M G S O ~ ? Q")]
    [InlineData(2, true, "D I E M G H C S N O ~ ? Q")]
    [InlineData(3, true, "D I E T A M G H C S N O ~ ? Q")]
    public void MainStreet_ShowsPerTier(int tier, bool online, string expected) =>
        StreetKeys(tier, online).Should().Be(expected);

    [Theory]
    [InlineData("MerchantRow", 1, "W A M U")]
    [InlineData("MerchantRow", 2, "W A M U")]
    [InlineData("MerchantRow", 3, "W A M U J")]
    [InlineData("GuildRow", 1, "H V U")]
    [InlineData("GuildRow", 2, "H T O B V U")]
    [InlineData("GuildRow", 3, "H T O B V U")]
    [InlineData("HomeHearth", 1, "")]
    [InlineData("HomeHearth", 2, "H")]
    [InlineData("HomeHearth", 3, "H L X")]
    [InlineData("CastleGrounds", 1, "")]
    [InlineData("CastleGrounds", 2, "K")]
    [InlineData("CastleGrounds", 3, "K C S")]
    [InlineData("StatusHub", 1, "S G")]
    [InlineData("StatusHub", 2, "S F G")]
    [InlineData("StatusHub", 3, "S P T F G")]
    [InlineData("NoticeBoard", 1, "")]
    [InlineData("NoticeBoard", 2, "N W")]
    [InlineData("NoticeBoard", 3, "N W")]
    public void Districts_ShowPerTier_TheSameOnlineAndOffline(string district, int tier, string expected)
    {
        var group = Enum.Parse<G>(district);
        PlaceKeys(group, tier).Should().Be(expected);
        PlaceKeys(group, tier, online: true).Should().Be(expected);
    }

    [Fact]
    public void Settlement_ShowsOnlyOnceFounded_AtTier3()
    {
        PlaceKeys(G.CastleGrounds, 3, settlement: true).Should().Be("K C S E");
        PlaceKeys(G.CastleGrounds, 2, settlement: true).Should().Be("K");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void OnlineHub_OnlyOnline(int tier)
    {
        PlaceKeys(G.Online, tier).Should().BeEmpty("the offline game never shows an online option");
        PlaceKeys(G.Online, tier, online: true).Should().Be("W C A B G");
        MainStreetLocation.MainStreetLines(View(tier, true), 12).Single(l => l.Key == "O").Label
            .Should().Be(Loc.Get("main_street.district_online_count", 12)).And.Contain("12");
    }

    [Fact]
    public void EveryScreen_HasDistinctKeys_AndNoneTakesReturnOrQuit()
    {
        var street = MainStreetLocation.MainStreetLines(View(3, true, true), 0).Select(l => l.Key).ToList();
        street.Should().OnlyHaveUniqueItems();
        foreach (G g in Enum.GetValues(typeof(G)))
        {
            var keys = MainStreetLocation.StreetEntries.Where(e => e.Group == g).Select(e => e.Key).ToList();
            keys.Should().OnlyHaveUniqueItems($"{g} keys must not clash");
            if (g != G.MainStreet)
                keys.Should().NotContain(new[] { MainStreetLocation.ReturnKey, "Q" }, $"{g}: R returns and Q never lands in a place");
        }
        Enum.GetValues(typeof(P)).Cast<P>().Should().OnlyContain(p => MainStreetLocation.StreetEntries.Count(e => e.Place == p) == 1);
        MainStreetLocation.StreetEntries.Single(e => e.Place == P.StatsRecord).Key.Should().Be("T");
        MainStreetLocation.StreetEntries.Single(e => e.Place == P.QuestHall).Key.Should().Be("U");
    }

    // ---------- what each key does ----------

    [Fact]
    public void EveryPlace_OpensWhatItsLabelSays()
    {
        var expected = new Dictionary<P, GameLocation?>
        {
            [P.Dungeons] = GameLocation.Dungeons, [P.Inn] = GameLocation.TheInn, [P.Explore] = GameLocation.Wilderness,
            [P.TeamCorner] = GameLocation.TeamCorner, [P.DarkAlley] = GameLocation.DarkAlley,
            [P.WeaponShop] = GameLocation.WeaponShop, [P.ArmorShop] = GameLocation.ArmorShop, [P.MagicShop] = GameLocation.MagicShop,
            [P.MusicShop] = GameLocation.MusicShop, [P.Auctions] = GameLocation.AuctionHouse,
            [P.Healer] = GameLocation.Healer, [P.Temple] = GameLocation.Temple, [P.OldChurch] = GameLocation.Church,
            [P.Bank] = GameLocation.Bank, [P.LevelMaster] = GameLocation.Master, [P.QuestHall] = GameLocation.QuestHall,
            [P.Home] = GameLocation.Home, [P.Lodging] = GameLocation.Dormitory, [P.LoveStreet] = GameLocation.LoveCorner,
            [P.Castle] = GameLocation.Castle, [P.Challenges] = GameLocation.AnchorRoad, [P.Sanctum] = GameLocation.Sanctum,
            [P.Settlement] = GameLocation.Settlement, [P.Arena] = GameLocation.Arena,
            // shown on Main Street itself
            [P.Status] = null, [P.Progress] = null, [P.StatsRecord] = null, [P.Fame] = null, [P.GoodDeeds] = null,
            [P.News] = null, [P.WorldEvents] = null, [P.WhosOnline] = null, [P.Chat] = null, [P.WorldBoss] = null, [P.Guilds] = null,
        };
        foreach (P p in Enum.GetValues(typeof(P)))
            MainStreetLocation.DestinationOf(p, false).Should().Be(expected[p], p.ToString());
        MainStreetLocation.DestinationOf(P.Auctions, true).Should().BeNull("online the auction board opens in place, as before");

        var labels = new Dictionary<P, string>
        {
            [P.Dungeons] = "menu.action.dungeon", [P.Inn] = "menu.action.inn", [P.Explore] = "menu.action.explore",
            [P.WeaponShop] = "menu.action.weapon_shop", [P.Healer] = "menu.action.healer", [P.LevelMaster] = "menu.action.level_master",
            [P.QuestHall] = "menu.action.quest_hall", [P.Challenges] = "menu.action.challenges", [P.Lodging] = "menu.action.lodging_short",
            [P.StatsRecord] = "menu.action.stats_record", [P.News] = "menu.action.news", [P.WorldEvents] = "menu.action.world_events",
            [P.WhosOnline] = "main_street.whos_online", [P.Guilds] = "menu.action.guilds",
        };
        foreach (var (p, key) in labels)
            MainStreetLocation.StreetEntries.Single(e => e.Place == p).LabelKey.Should().Be(key);
    }

    private static (MainStreetLocation street, MemoryStream output) Rig(int level, params string[] lines)
    {
        var street = new MainStreetLocation();
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(lines), output);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(street, term);
        var hero = new Character { Name1 = "Street", Name2 = "Street", Level = level, HP = 100, MaxHP = 100, MKills = 3, AI = CharacterAI.Human };
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(street, hero);
        return (street, output);
    }

    private static string Plain(MainStreetLocation street, MemoryStream output)
    {
        var term = (TerminalEmulator)typeof(BaseLocation).GetField("terminal", F)!.GetValue(street)!;
        term.StreamWriterInternal?.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "");
    }

    private static Task<bool> Choose(MainStreetLocation street, string key) =>
        (Task<bool>)typeof(MainStreetLocation).GetMethod("ProcessChoice", F)!.Invoke(street, new object[] { key })!;

    private static async Task<GameLocation?> Leads(int level, string key, params string[] lines)
    {
        var (street, _) = Rig(level, lines);
        try { await Choose(street, key); return null; }
        catch (LocationExitException ex) { return ex.DestinationLocation; }
    }

    [Theory]
    [InlineData("D", new string[0], GameLocation.Dungeons)]
    [InlineData("A", new string[0], GameLocation.DarkAlley)]
    [InlineData("M", new[] { "W" }, GameLocation.WeaponShop)]
    [InlineData("M", new[] { "J" }, GameLocation.AuctionHouse)]
    [InlineData("G", new[] { "V" }, GameLocation.Master)]
    [InlineData("G", new[] { "U" }, GameLocation.QuestHall)]
    [InlineData("G", new[] { "O" }, GameLocation.Church)]
    [InlineData("H", new[] { "L" }, GameLocation.Dormitory)]
    [InlineData("C", new[] { "K" }, GameLocation.Castle)]
    [InlineData("C", new[] { "C" }, GameLocation.AnchorRoad)]
    public async Task ShownKeys_GoWhereTheySay(string key, string[] then, GameLocation where) =>
        (await Leads(10, key, then)).Should().Be(where);

    [Fact]
    public async Task District_Typo_AsksAgain_ThenTakesTheValidKey()
    {
        var (street, output) = Rig(1, "Z", "J", "W");
        var ex = await Assert.ThrowsAsync<LocationExitException>(() => Choose(street, "M"));
        ex.DestinationLocation.Should().Be(GameLocation.WeaponShop);
        string text = Plain(street, output);
        Regex.Matches(text, Regex.Escape(Loc.Get("ui.invalid_choice_choose", "W, A, M, U, R"))).Count
            .Should().Be(2, "a typo and a locked place's key (J at level 1) both ask again");
        text.Should().Contain(Loc.Get("main_street.district_merchant_row")).And.Contain(Loc.Get("main_street.district_merchant_row_desc"));
    }

    [Fact]
    public async Task District_ReturnKey_AndThreeTypos_LandOnMainStreet()
    {
        (await Leads(10, "G", "R")).Should().BeNull();
        (await Leads(10, "G", "Q", "Q", "Q", "U")).Should().BeNull("Q never lands in the Quest Hall, and three typos return");
        (await Leads(10, "S", "R")).Should().BeNull();
    }

    // ---------- negative checks ----------

    private static readonly string[] OldKeys =
        { "1", "2", "3", "4", "5", "6", "7", "8", "9", "W", "U", "J", "B", "V", "L", "X", "K", "F", "P", "=", "$", ">", "+", "Y", "Z", "R" };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovedLegacyKeys_AreNotOnMainStreet(bool online)
    {
        var shown = MainStreetLocation.MainStreetLines(View(3, online, true), 0).Select(l => l.Key).ToHashSet();
        shown.Should().NotIntersectWith(OldKeys);
    }

    [Theory]
    [InlineData(10, "V")]
    [InlineData(10, "9")]
    [InlineData(10, "O")]
    [InlineData(1, "A")]
    [InlineData(1, "N")]
    public async Task RemovedAndLockedKeys_DoNothing(int level, string key)
    {
        var (street, output) = Rig(level);
        (await Choose(street, key)).Should().BeFalse();
        Plain(street, output).Should().Contain(Loc.Get("main_street.invalid_choice"));
    }

    [Fact]
    public async Task OnlineKey_WorksOnlyOnline()
    {
        object old = OnlineFlag.GetValue(null)!;
        try
        {
            OnlineFlag.SetValue(null, true);
            var (street, output) = Rig(1, "R");
            (await Choose(street, "O")).Should().BeFalse();
            string text = Plain(street, output);
            text.Should().Contain(Loc.Get("main_street.whos_online")).And.Contain(Loc.Get("main_street.district_online_desc"));
            text.Should().NotContain(Loc.Get("main_street.invalid_choice"));
            (await Leads(1, "O", "A")).Should().Be(GameLocation.Arena);
        }
        finally { OnlineFlag.SetValue(null, old); }
    }

    [Fact]
    public void ProcessChoice_KeepsOnlyTheHiddenUtilities()
    {
        string src = Source("Scripts/Locations/MainStreetLocation.cs");
        string body = Body(src, "protected override async Task<bool> ProcessChoice");
        Regex.Matches(body, "case \"([^\"]+)\":").Select(m => m.Groups[1].Value)
            .Should().BeEquivalentTo(new[] { "MAIL", "CTRL+M", "!M", "DEV", "CHEATER", "DEVMENU" });
        body.Should().Contain("TryProcessGlobalCommand(choice)").And.Contain("await ShowHelp()").And.NotContain("CompactMode");
        Source("Scripts/Locations/MainStreetDistricts.cs").Should().Contain("protected override void ShowQuickCommandBar() { }",
            "Main Street shows only its own keys");
        string global = Body(Source("Scripts/Locations/BaseLocation.cs"), "protected async Task<(bool handled, bool shouldExit)> TryProcessGlobalCommand");
        foreach (string k in new[] { "case \"*\"", "case \"0\"", "case \"TALK\"", "case \"~\"", "case \"PREFS\"", "case \"PREFERENCES\"", "choice.StartsWith(\"/\")" })
            global.Should().Contain(k, "the hidden utilities still work through the global handler");
    }

    // ---------- unlock lines and the one-time notice ----------

    [Fact]
    public void UnlockLines_NameTheDistrict()
    {
        string L(string k) => Loc.Get(k);
        MainStreetLocation.UnlockLines(1, 2).Should().Equal(
            Loc.Get("main_street.unlock_announce", string.Join(", ", L("menu.action.explore"), L("main_street.district_home_hearth"),
                L("main_street.district_castle_grounds"), L("main_street.district_notice_board"))),
            Loc.Get("main_street.unlock_announce_in", L("main_street.district_guild_row"),
                string.Join(", ", L("menu.action.temple"), L("menu.action.old_church"), L("menu.action.bank"))),
            Loc.Get("main_street.unlock_announce_in", L("main_street.district_status"), L("menu.action.fame")));
        MainStreetLocation.UnlockLines(2, 3).Should().Equal(
            Loc.Get("main_street.unlock_announce", string.Join(", ", L("menu.action.team_corner"), L("menu.action.dark_alley"))),
            Loc.Get("main_street.unlock_announce_in", L("main_street.district_merchant_row"), L("menu.action.auction_house")),
            Loc.Get("main_street.unlock_announce_in", L("main_street.district_home_hearth"),
                string.Join(", ", L("menu.action.lodging_short"), L("menu.action.love_street"))),
            Loc.Get("main_street.unlock_announce_in", L("main_street.district_castle_grounds"),
                string.Join(", ", L("menu.action.challenges"), L("menu.action.sanctum"))),
            Loc.Get("main_street.unlock_announce_in", L("main_street.district_status"),
                string.Join(", ", L("menu.action.progress"), L("menu.action.stats_record"))));
        string.Join("\n", MainStreetLocation.UnlockLines(1, 3)).Should().NotContain(L("menu.action.settlement"))
            .And.NotContain(L("main_street.district_online")).And.Contain("Guild Row: Temple");
        Loc.Get("main_street.unlock_announce_in", "Guild Row", "Temple").Should().Be("New in Guild Row: Temple.");
    }

    [Fact]
    public void TierRise_AnnouncesByDistrict_Once()
    {
        var hero = new Character { Name1 = "Rise", Name2 = "Rise", Level = 1 };
        MainStreetLocation.TakeTierUnlockAnnouncement(hero).Should().BeEmpty();
        hero.Level = GameConfig.MenuTier3Level; // two tiers at once
        var lines = MainStreetLocation.TakeTierUnlockAnnouncement(hero);
        lines.Should().Equal(MainStreetLocation.UnlockLines(1, 3));
        lines.First().Should().StartWith("New on Main Street:").And.Contain("Castle Grounds");
        MainStreetLocation.TakeTierUnlockAnnouncement(hero).Should().BeEmpty();
    }

    [Fact]
    public void DistrictsNotice_OnceForExistingCharacters_NeverForNewOnes()
    {
        var veteran = new Character { Name1 = "Vet", Name2 = "Vet", Level = 7, MKills = 40 };
        MainStreetLocation.TakeDistrictsNotice(veteran).Should().Be(Loc.Get("main_street.districts_notice"));
        MainStreetLocation.TakeDistrictsNotice(veteran).Should().BeNull("it is shown once");
        veteran.HintsShown.Should().Contain(MainStreetLocation.DistrictsNoticeHint);

        var fresh = new Character { Name1 = "New", Name2 = "New", Level = 1, MKills = 0 };
        MainStreetLocation.TakeDistrictsNotice(fresh).Should().BeNull("a new character never knew the old menu");
        fresh.MKills = 5; fresh.Level = 4;
        MainStreetLocation.TakeDistrictsNotice(fresh).Should().BeNull("and is not told later either");

        var lowButPlayed = new Character { Name1 = "Low", Name2 = "Low", Level = 1, MKills = 2 };
        MainStreetLocation.TakeDistrictsNotice(lowButPlayed).Should().NotBeNull();
        Loc.Get("main_street.districts_notice").Should().Be("Main Street has been reorganised into districts. Press ? for help, or choose the classic layout in Settings (~)."); // v1.1.14: names the classic setting
    }

    // ---------- the three renderers ----------

    private static void Call(MainStreetLocation street, string method, params object[] args) =>
        typeof(MainStreetLocation).GetMethod(method, F)!.Invoke(street, args);

    [Theory]
    [InlineData(1, false)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    public void ScreenReader_Bbs_AndVisual_DrawTheSameItems(int tier, bool online)
    {
        object old = OnlineFlag.GetValue(null)!;
        try
        {
            OnlineFlag.SetValue(null, online);
            int level = tier == 3 ? 10 : 1;
            var lines = MainStreetLocation.MainStreetLines(View(tier, online), 0);
            var expected = lines.Select(l => $"{l.Key}={l.Label}").ToList();

            var (sr, srOut) = Rig(level);
            Call(sr, "WriteStreetMenuPlain", lines);
            var srItems = Regex.Matches(Plain(sr, srOut), @"^  (\S) - (.+?)\s*$", RegexOptions.Multiline)
                .Select(m => $"{m.Groups[1].Value}={m.Groups[2].Value}").ToList();

            var (bbs, bbsOut) = Rig(level);
            Call(bbs, "WriteStreetMenuCompact", lines);
            var (vis, visOut) = Rig(level);
            Call(vis, "WriteStreetMenuGrid", lines);

            srItems.Should().Equal(expected);
            Items(Plain(bbs, bbsOut)).Should().Equal(expected);
            Items(Plain(vis, visOut)).Should().Equal(expected);
        }
        finally { OnlineFlag.SetValue(null, old); }
    }

    [Theory]
    [InlineData(1, false, 3)]
    [InlineData(2, false, 4)]
    [InlineData(3, false, 7)]
    [InlineData(3, true, 8)]
    public void VisualMenu_IsCompact_WithNoOrphanBorder(int tier, bool online, int rows)
    {
        object old = OnlineFlag.GetValue(null)!;
        try
        {
            OnlineFlag.SetValue(null, online);
            var (street, output) = Rig(tier == 3 ? 10 : tier == 2 ? 3 : 1);
            Call(street, "ShowClassicMenu");
            string text = Plain(street, output).Replace("\r", "");
            text.Should().NotContain("╚").And.NotContain("═").And.NotContain("\n\n\n").And.NotStartWith("\n");
            text.TrimEnd('\n').Split('\n').Length.Should().Be(rows, "groups share no blank lines until the menu is tall");
            text.TrimEnd('\n').Split('\n').Should().OnlyContain(l => l.Length <= 80);
        }
        finally { OnlineFlag.SetValue(null, old); }
    }

    private static List<string> Items(string text) =>
        Regex.Matches(text, @"\[(\S)\] (.+?)(?:\s\.\.\.)?(?=\s{2,}\[|\s*$)", RegexOptions.Multiline)
            .Select(m => $"{m.Groups[1].Value}={m.Groups[2].Value.Trim()}").ToList();

    [Fact]
    public void DistrictScreens_DrawTheSameItemsInAllThreeModes()
    {
        var guild = MainStreetLocation.Districts.Single(d => d.Group == G.GuildRow);
        var places = MainStreetLocation.PlacesIn(G.GuildRow, View(3));
        var expected = places.Select(p => $"{p.Key}={Loc.Get(p.LabelKey)}").Append($"R={Loc.Get("location.return_main_street")}").ToList();

        var (vis, visOut) = Rig(10);
        Call(vis, "DrawDistrict", guild, places);
        string visual = Plain(vis, visOut);
        Items(visual).Should().Equal(expected);
        visual.Should().Contain(Loc.Get("main_street.district_guild_row_desc"));

        var (sr, srOut) = Rig(10);
        ((Character)typeof(BaseLocation).GetField("currentPlayer", F)!.GetValue(sr)!).ScreenReaderMode = true;
        Call(sr, "DrawDistrict", guild, places);
        Regex.Matches(Plain(sr, srOut), @"^  (\S) - (.+?)\s*$", RegexOptions.Multiline)
            .Select(m => $"{m.Groups[1].Value}={m.Groups[2].Value}").Should().Equal(expected);

        bool compact = GameConfig.CompactMode;
        try
        {
            GameConfig.CompactMode = true; // a BBS session
            var (bbs, bbsOut) = Rig(10);
            Call(bbs, "DrawDistrict", guild, places);
            Items(Plain(bbs, bbsOut)).Should().Equal(expected);
        }
        finally { GameConfig.CompactMode = compact; }
    }

    [Fact]
    public void HelpMap_ListsOnlyUnlockedPlaces_UnderTheirKeys()
    {
        var low = MainStreetLocation.HelpMapLines(View(1), 0);
        string.Join("\n", low).Should().Contain($"[G] {Loc.Get("main_street.district_guild_row")}: [H] {Loc.Get("menu.action.healer")}")
            .And.NotContain(Loc.Get("menu.action.temple")).And.NotContain(Loc.Get("main_street.district_online"));
        var high = string.Join("\n", MainStreetLocation.HelpMapLines(View(3, true), 4));
        high.Should().Contain("[O] Online (4): [W] Who's Online").And.Contain("[V] Level Master").And.Contain("[T] Stats Record");
    }

    // ---------- no text points at an old Main Street key ----------

    private static readonly string[] Prefixes = { "hint.", "journal.", "base.", "castle.", "church.", "help.", "main_street.", "quest.", "tutorial." };
    private static readonly string[] Unrelated =
    {
        "hint.first_combat", "hint.class_combat.", "base.quest_pager_nav", "base.pick_nav_pages", "base.mail_",
        "main_street.achieve_", "main_street.attack_cancel", "castle.court_menu",
    };

    [Fact]
    public void NoText_PointsAtAnOldMainStreetKey()
    {
        var problems = new List<string>();
        foreach (string lang in new[] { "en", "es", "fr", "hu", "it" })
        {
            var table = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Source($"Localization/{lang}.json"))!;
            foreach (var (key, v) in table)
            {
                if (v.ValueKind != JsonValueKind.String || !Prefixes.Any(key.StartsWith) || Unrelated.Any(key.StartsWith)) continue;
                string s = v.GetString()!;
                foreach (string old in new[] { "[1]", "[2]", "[$]", "[=]", "[Z]", "[Y]", "[>]", "[+]", "[K]", "[J]", "[L]", "[W/A]" })
                    if (s.Contains(old)) problems.Add($"{lang} {key}: {old}");
                if ((s.Contains("[V]") || s.Contains("[U]") || s.Contains("[O]") || s.Contains("[B]")) && !s.Contains("[G]"))
                    problems.Add($"{lang} {key}: a Guild Row key without [G]");
                if (s.Contains("[P]") && !s.Contains("[S]")) problems.Add($"{lang} {key}: [P] without Status [S]");
                if (s.Contains("[X]") && !s.Contains("[H]")) problems.Add($"{lang} {key}: [X] without Home & Hearth [H]");
                if (Regex.IsMatch(s, @"\[[^QDI\]]\] (on|in|en|dans|nella|sulla|a) (the |la )?(Main Street|Calle|Rue|Grand-Rue|Főutc|Fő utc|Via Principale)"))
                    problems.Add($"{lang} {key}: a key that is no longer on Main Street itself");
            }
        }
        problems.Should().BeEmpty();
        Loc.Get("base.train_how").Should().Contain("[G]").And.Contain("[V]").And.Contain("/train");
        Loc.Get("hint.getting_started.msg").Should().Contain("Merchant Row [M]").And.Contain("Guild Row [G]");
    }

    // ---------- evidence: the real screens, ANSI stripped (runs only with USURPER_SCREENS_OUT set) ----------

    [Fact]
    public void RenderScreens_ForTheMaintainer()
    {
        string? path = Environment.GetEnvironmentVariable("USURPER_SCREENS_OUT");
        if (string.IsNullOrEmpty(path)) return;
        object old = OnlineFlag.GetValue(null)!;
        var sb = new StringBuilder();
        void Screen(string title, int level, bool online, bool screenReader, bool compact, Action<MainStreetLocation> draw)
        {
            bool oldCompact = GameConfig.CompactMode;
            try
            {
                OnlineFlag.SetValue(null, online);
                GameConfig.CompactMode = compact;
                var (street, output) = Rig(level, "", "", "");
                var hero = (Character)typeof(BaseLocation).GetField("currentPlayer", F)!.GetValue(street)!;
                hero.ScreenReaderMode = screenReader;
                hero.HintsShown.UnionWith(new[] { "menu_tier_1", "menu_tier_2", "menu_tier_3", MainStreetLocation.DistrictsNoticeHint });
                draw(street);
                sb.AppendLine($"==================== {title} ====================");
                sb.AppendLine(Plain(street, output).Replace("\r", ""));
            }
            finally { GameConfig.CompactMode = oldCompact; OnlineFlag.SetValue(null, old); }
        }
        void Main(MainStreetLocation s) => Call(s, "DisplayLocation");
        void Guild(MainStreetLocation s) => Call(s, "DrawDistrict", MainStreetLocation.Districts.Single(d => d.Group == G.GuildRow),
            MainStreetLocation.PlacesIn(G.GuildRow, View(3)));
        void Help(MainStreetLocation s) => ((Task)typeof(MainStreetLocation).GetMethod("ShowHelp", F)!.Invoke(s, null)!).GetAwaiter().GetResult();

        Screen("Main Street, level 1 (tier 1), single-player, visual", 1, false, false, false, Main);
        Screen("Main Street, level 10 (tier 3), single-player, visual", 10, false, false, false, Main);
        Screen("Main Street, level 10 (tier 3), online, visual", 10, true, false, false, Main);
        Screen("Guild Row, level 10 (tier 3), visual", 10, false, false, false, Guild);
        Screen("Main Street, level 10 (tier 3), online, screen reader", 10, true, true, false, Main);
        Screen("Main Street, level 10 (tier 3), online, BBS / compact", 10, true, false, true, Main);
        Screen("Guild Row, level 10 (tier 3), BBS / compact", 10, false, false, true, Guild);
        Screen("Help (?), level 10 (tier 3), online, visual", 10, true, false, false, Help);
        var fresh = new Character { Name1 = "Vet", Name2 = "Vet", Level = 3, MKills = 9 };
        fresh.HintsShown.UnionWith(new[] { "menu_tier_1", "menu_tier_2" });
        sb.AppendLine("==================== Shown once: an existing level 3 character's first visit, then its rise to level 5 ====================");
        sb.AppendLine(MainStreetLocation.TakeDistrictsNotice(fresh));
        fresh.Level = 5;
        foreach (string l in MainStreetLocation.TakeTierUnlockAnnouncement(fresh)) sb.AppendLine(l);
        File.WriteAllText(path, sb.ToString());
    }

    // ---------- helpers ----------

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Scripts"))) dir = dir.Parent;
        return dir!.FullName;
    }

    private static string Source(string relative) => File.ReadAllText(Path.Combine(RepoRoot(), relative));

    private static string Body(string src, string signature)
    {
        int start = src.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, signature);
        int open = src.IndexOf('{', start), depth = 0;
        for (int i = open; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0) return src.Substring(open, i - open + 1);
        }
        return src.Substring(open);
    }
}
