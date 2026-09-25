using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.14: with the preference on, Main Street is the layout at git tag mainstreet-classic: the same rows in all
/// three modes (compared with a render of the tagged code, Tests/Golden/MainStreetClassicTag.txt) and the same keys.
/// </summary>
[Collection("SharedGameSingletons")]
public class ClassicMainStreetLayout1114Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    internal static readonly FieldInfo OnlineFlag =
        typeof(UsurperRemake.BBS.DoorMode).GetField("_onlineMode", BindingFlags.NonPublic | BindingFlags.Static)!;
    internal static readonly FieldInfo ChatFallback =
        typeof(OnlineChatSystem).GetField("_fallbackInstance", BindingFlags.NonPublic | BindingFlags.Static)!;

    internal static (MainStreetLocation street, MemoryStream output, Character hero) Rig(int level, bool classic, params string[] lines)
    {
        var street = new MainStreetLocation();
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(lines), output);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(street, term);
        var hero = new Character { Name1 = "Golden", Name2 = "Golden", Level = level, HP = 100, MaxHP = 100, MKills = 3, AI = CharacterAI.Human,
                                   ClassicMainStreet = classic };
        hero.HintsShown.UnionWith(new[] { "menu_tier_1", "menu_tier_2", "menu_tier_3", MainStreetLocation.DistrictsNoticeHint });
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(street, hero);
        return (street, output, hero);
    }

    internal static string Plain(MainStreetLocation street, MemoryStream output)
    {
        var term = (TerminalEmulator)typeof(BaseLocation).GetField("terminal", F)!.GetValue(street)!;
        term.StreamWriterInternal?.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "").Replace("\r", "");
    }

    private static Task<bool> Choose(MainStreetLocation street, string key) =>
        (Task<bool>)typeof(MainStreetLocation).GetMethod("ProcessChoice", F)!.Invoke(street, new object[] { key })!;

    private static async Task<GameLocation?> Leads(int level, string key, params string[] lines)
    {
        var (street, _, _) = Rig(level, true, lines);
        try { await Choose(street, key); return null; }
        catch (LocationExitException ex) { return ex.DestinationLocation; }
    }

    /// <summary>The menu the classic layout draws in one mode, captured the way the golden file captured the tagged code.</summary>
    internal static string RenderClassic(int level, bool online, string mode)
    {
        object oldOnline = OnlineFlag.GetValue(null)!;
        object? oldChat = ChatFallback.GetValue(null);
        bool oldCompact = GameConfig.CompactMode;
        try
        {
            OnlineFlag.SetValue(null, online);
            ChatFallback.SetValue(null, online ? System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(OnlineChatSystem)) : null);
            GameConfig.CompactMode = mode == "bbs";
            var (street, output, hero) = Rig(level, true, "", "", "");
            hero.ScreenReaderMode = mode == "screenreader";
            string method = mode switch { "visual" => "ShowClassicLayoutMenu", "screenreader" => "ShowClassicLayoutScreenReaderMenu", _ => "DisplayLocationBBS" };
            typeof(MainStreetLocation).GetMethod(method, F)!.Invoke(street, null);
            string text = Plain(street, output);
            if (mode == "bbs")
            {
                // only the menu: the lines with a [key] (the header, description and status line vary with time and weather)
                var keep = new StringBuilder();
                foreach (string line in text.Split('\n'))
                    if (Regex.IsMatch(line, @"\[.\]") || line.Contains("Online") || line.Contains("Numpad")) keep.Append(line).Append('\n');
                text = keep.ToString();
            }
            return text;
        }
        finally
        {
            OnlineFlag.SetValue(null, oldOnline);
            ChatFallback.SetValue(null, oldChat);
            GameConfig.CompactMode = oldCompact;
        }
    }

    private static string Golden(int tier, bool online, string mode)
    {
        string all = File.ReadAllText(Path.Combine(MainStreetDistricts1113Tests.RepoRoot(), "Tests/Golden/MainStreetClassicTag.txt")).Replace("\r", "");
        string head = $"##### tier {tier} {(online ? "online" : "offline")} {mode}\n";
        int start = all.IndexOf(head, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, head);
        start += head.Length;
        return all.Substring(start, all.IndexOf("##### end\n", start, StringComparison.Ordinal) - start);
    }

    [Theory]
    [InlineData(1, false, "visual")] [InlineData(1, false, "screenreader")] [InlineData(1, false, "bbs")]
    [InlineData(2, false, "visual")] [InlineData(2, false, "screenreader")] [InlineData(2, false, "bbs")]
    [InlineData(3, false, "visual")] [InlineData(3, false, "screenreader")] [InlineData(3, false, "bbs")]
    [InlineData(1, true, "visual")] [InlineData(1, true, "screenreader")] [InlineData(1, true, "bbs")]
    [InlineData(2, true, "visual")] [InlineData(2, true, "screenreader")] [InlineData(2, true, "bbs")]
    [InlineData(3, true, "visual")] [InlineData(3, true, "screenreader")] [InlineData(3, true, "bbs")]
    public void ClassicMenu_IsTheTaggedLayout_PerTier(int tier, bool online, string mode)
    {
        int level = tier == 3 ? 10 : tier == 2 ? 3 : 1;
        string expected = Golden(tier, online, mode);
        // the two documented adaptations: the visual menu's orphan bottom border is left out, and the tiers come from
        // the district table, so the Old Church (tier 2 there, as in the tagged visual menu) is also listed at tier 2
        // by the screen reader and BBS menus, which in the tagged code only listed it from tier 3
        if (mode == "visual") expected = Regex.Replace(expected, "^╚═+╝\n", "", RegexOptions.Multiline);
        if (tier == 2 && mode == "screenreader") expected = expected.Replace("  H - Home\n", "  H - Home\n  O - Church\n");
        if (tier == 2 && mode == "bbs") expected = expected.Replace("World Events\n", "World Events\n [O] Old Church \n");
        RenderClassic(level, online, mode).Should().Be(expected);
    }

    [Fact]
    public void Golden_IsTheTaggedRender_WithItsKnownQuirks()
    {
        Golden(1, false, "visual").Should().Contain("╚═══", "the tagged visual menu ended with a stray border");
        Golden(2, false, "screenreader").Should().NotContain("O - Church");
        Golden(3, false, "screenreader").Should().Contain("O - Church");
        Golden(3, true, "visual").Should().Contain("[R]Guild Board").And.Contain("[3]Who's Online (0 players)")
            .And.Contain("[N]News").And.Contain("[5]News Feed", "classic keeps both news keys");
    }

    [Fact]
    public void ClassicMenu_ShowsOnlyWhatTheDistrictTableUnlocks()
    {
        foreach (int tier in new[] { 1, 2, 3 })
        foreach (bool settlement in new[] { false, true })
        {
            var view = new MainStreetLocation.StreetView(tier, false, settlement);
            foreach (var item in MainStreetLocation.ClassicVisualRows.SelectMany(r => r)
                         .Concat(MainStreetLocation.ClassicBbsRows.SelectMany(r => r))
                         .Concat(MainStreetLocation.ClassicScreenReaderSections.SelectMany(s => s.Items)))
            {
                if (item.Place == null) { MainStreetLocation.ClassicShows(item, view).Should().BeTrue(item.Key); continue; }
                var entry = MainStreetLocation.StreetEntries.Single(e => e.Place == item.Place);
                MainStreetLocation.ClassicShows(item, view).Should().Be(MainStreetLocation.IsUnlocked(entry, view), $"{item.Key} at tier {tier}");
            }
        }
        string src = File.ReadAllText(Path.Combine(MainStreetDistricts1113Tests.RepoRoot(), "Scripts/Locations/MainStreetClassic.cs"));
        src.Should().NotContain("tier >=").And.NotContain("GetMenuTier()", "the classic menu keeps no tier list of its own");
    }

    [Fact]
    public void ClassicMenu_SettlementShowsOnceFounded()
    {
        MainStreetLocation.ClassicShown(MainStreetLocation.ClassicVisualRows[5], new MainStreetLocation.StreetView(3, false, true))
            .Select(i => i.Key).Should().Equal("=", "P", ">");
        MainStreetLocation.ClassicShown(MainStreetLocation.ClassicVisualRows[5], new MainStreetLocation.StreetView(3, false, false))
            .Select(i => i.Key).Should().Equal("=", "P");
    }

    // ---------- the classic keys ----------

    [Theory]
    [InlineData("W", GameLocation.WeaponShop)]
    [InlineData("A", GameLocation.ArmorShop)]
    [InlineData("M", GameLocation.MagicShop)]
    [InlineData("U", GameLocation.MusicShop)]
    [InlineData("1", GameLocation.Healer)]
    [InlineData("2", GameLocation.QuestHall)]
    [InlineData("V", GameLocation.Master)]
    [InlineData("D", GameLocation.Dungeons)]
    [InlineData("I", GameLocation.TheInn)]
    [InlineData("B", GameLocation.Bank)]
    [InlineData("K", GameLocation.Castle)]
    [InlineData("H", GameLocation.Home)]
    [InlineData("O", GameLocation.Church)]
    [InlineData("C", GameLocation.AnchorRoad)]
    [InlineData("L", GameLocation.Dormitory)]
    [InlineData("E", GameLocation.Wilderness)]
    [InlineData("J", GameLocation.AuctionHouse)]
    [InlineData("T", GameLocation.Temple)]
    [InlineData("Y", GameLocation.DarkAlley)]
    [InlineData("+", GameLocation.Sanctum)]
    [InlineData("X", GameLocation.LoveCorner)]
    public async Task ClassicKeys_GoWhereTheyWent(string key, GameLocation where) =>
        (await Leads(10, key)).Should().Be(where);

    [Fact]
    public async Task ClassicKeys_WorkAtEveryLevel_AsTheyDid_ButTeamCornerWaitsForLevel5()
    {
        (await Leads(1, "K")).Should().Be(GameLocation.Castle, "the tagged menu gated only the display, not the keys");
        var (street, output, _) = Rig(1, true);
        (await Choose(street, "Z")).Should().BeFalse();
        Plain(street, output).Should().Contain(Loc.Get("main_street.team_corner_level_req"));
    }

    [Fact]
    public async Task Dollar_OpensWorldEvents_Equals_Statistics_R_OfflineSaysOnlineOnly()
    {
        var (street, output, _) = Rig(10, true, "", "", "");
        (await Choose(street, "$")).Should().BeFalse();
        Plain(street, output).Should().Contain("WORLD EVENTS");

        (street, output, _) = Rig(10, true, "", "", "");
        (await Choose(street, "=")).Should().BeFalse();
        Plain(street, output).Should().Contain(Loc.Get("main_street.statistics"));

        (street, output, _) = Rig(10, true);
        (await Choose(street, "R")).Should().BeFalse();
        Plain(street, output).Should().Contain(Loc.Get("guild.online_only"));
    }

    [Fact]
    public async Task Three_OpensWhosOnline_Online_Six_TheArena()
    {
        object oldOnline = OnlineFlag.GetValue(null)!;
        object? oldChat = ChatFallback.GetValue(null);
        string db = Path.Combine(Path.GetTempPath(), $"usurper-classic-{Guid.NewGuid():N}.db");
        try
        {
            var backend = new SqlSaveBackend(db);
            var osm = (OnlineStateManager)Activator.CreateInstance(typeof(OnlineStateManager), F, null, new object[] { backend, "golden" }, null)!;
            var chat = (OnlineChatSystem)Activator.CreateInstance(typeof(OnlineChatSystem), F, null, new object[] { osm }, null)!;
            ChatFallback.SetValue(null, chat);
            OnlineFlag.SetValue(null, true);
            var (street, output, _) = Rig(10, true, "", "", "");
            (await Choose(street, "3")).Should().BeFalse();
            Plain(street, output).Should().Contain("WHO'S ONLINE").And.NotContain(Loc.Get("main_street.invalid_choice"));
            (await Leads(1, "6")).Should().Be(GameLocation.Arena);
        }
        finally
        {
            OnlineFlag.SetValue(null, oldOnline);
            ChatFallback.SetValue(null, oldChat);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(db); } catch { }
        }
    }

    [Fact]
    public async Task CompactNumbers_Offline_MapAsTheyDid()
    {
        bool compact = GameConfig.CompactMode;
        try
        {
            GameConfig.CompactMode = true;
            (await Leads(10, "3")).Should().Be(GameLocation.WeaponShop);
            (await Leads(10, "8")).Should().Be(GameLocation.Master);
        }
        finally { GameConfig.CompactMode = compact; }
    }

    [Fact]
    public async Task Question_IsTheQuickCommandsHelp_AsItWas_NotTheDistrictMap()
    {
        var (street, output, _) = Rig(10, true, "", "", "");
        (await Choose(street, "?")).Should().BeFalse();
        Plain(street, output).Should().Contain(Loc.Get("base.quick_commands")).And.NotContain(Loc.Get("main_street.help_intro"));
    }

    [Fact]
    public async Task G_IsGoodDeeds_InClassic_NotGuildRow()
    {
        var (street, output, _) = Rig(10, true, "", "", "", "");
        (await Choose(street, "G")).Should().BeFalse();
        Plain(street, output).Should().Contain(Loc.Get("main_street.good_deeds_title")).And.NotContain(Loc.Get("main_street.district_guild_row_desc"));
    }

    [Fact]
    public async Task Off_TheSameKeysAreTheDistricts()
    {
        var (street, output, _) = Rig(10, false);
        (await Choose(street, "1")).Should().BeFalse();
        Plain(street, output).Should().Contain(Loc.Get("main_street.invalid_choice"));
        var (street2, _, _) = Rig(10, false, "W");
        (await Assert.ThrowsAsync<LocationExitException>(() => Choose(street2, "M"))).DestinationLocation
            .Should().Be(GameLocation.WeaponShop, "M opens Merchant Row, then W");
    }

    [Fact]
    public void VisualScreen_HasTheQuickCommandBar_OnlyInClassic()
    {
        foreach (bool classic in new[] { true, false })
        {
            var (street, output, _) = Rig(10, classic, "", "", "");
            typeof(MainStreetLocation).GetMethod("DisplayLocation", F)!.Invoke(street, null);
            string text = Plain(street, output);
            if (classic) text.Should().Contain($"{Loc.Get("ui.quick_commands")}: ").And.Contain("[D]Dungeons");
            else text.Should().NotContain($"{Loc.Get("ui.quick_commands")}: ").And.NotContain("[D]Dungeons");
        }
    }
}
