using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.14: the texts around the two layouts: the classic unlock line, the reorganisation notice that now points at
/// Settings, and the "switch back to classic" tip on the first ten district Main Street draws.
/// </summary>
[Collection("SharedGameSingletons")]
public class ClassicMainStreetNotices1114Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    // ---------- the classic unlock line ----------

    [Fact]
    public void ClassicUnlockLine_NamesPlacesWithoutDistricts_WithTheClassicLabels()
    {
        string L(string k) => Loc.Get(k);
        MainStreetLocation.ClassicUnlockLines(1, 2).Should().Equal(Loc.Get("main_street.unlock_announce", string.Join(", ",
            L("menu.action.temple"), L("menu.action.old_church"), L("menu.action.bank"), L("menu.action.castle"), L("menu.action.home"),
            L("menu.action.news"), L("menu.action.fame"), L("main_street.classic_explore"), L("menu.action.world_events"))));
        MainStreetLocation.ClassicUnlockLines(2, 3).Should().Equal(Loc.Get("main_street.unlock_announce", string.Join(", ",
            L("menu.action.auction_house"), L("menu.action.challenges"), L("menu.action.lodging_short"), L("menu.action.team_corner"),
            L("menu.action.stats_record"), L("menu.action.progress"), L("menu.action.dark_alley"), L("menu.action.sanctum"), L("menu.action.love_street"))));
        MainStreetLocation.ClassicUnlockLines(1, 2).Single().Should()
            .Be("New on Main Street: Temple, Old Church, Bank, Castle, Home, News, Fame, Wilderness, World Events.");
        string both = string.Join("\n", MainStreetLocation.ClassicUnlockLines(1, 3));
        both.Should().NotContain(L("menu.action.settlement")).And.NotContain(L("main_street.district_guild_row"))
            .And.NotContain(L("main_street.district_castle_grounds")).And.NotContain("New in");
        MainStreetLocation.ClassicUnlockLines(3, 3).Should().BeEmpty();
    }

    [Fact]
    public void TierRise_InClassic_AnnouncesTheClassicLine_Once()
    {
        var hero = new Character { Name1 = "Rise", Name2 = "Rise", Level = 1, ClassicMainStreet = true };
        MainStreetLocation.TakeTierUnlockAnnouncement(hero).Should().BeEmpty();
        hero.Level = GameConfig.MenuTier3Level;
        MainStreetLocation.TakeTierUnlockAnnouncement(hero).Should().Equal(MainStreetLocation.ClassicUnlockLines(1, 3));
        MainStreetLocation.TakeTierUnlockAnnouncement(hero).Should().BeEmpty();

        var districts = new Character { Name1 = "Dist", Name2 = "Dist", Level = 1 };
        MainStreetLocation.TakeTierUnlockAnnouncement(districts);
        districts.Level = GameConfig.MenuTier2Level;
        MainStreetLocation.TakeTierUnlockAnnouncement(districts).Should().Equal(MainStreetLocation.UnlockLines(1, 2), "the districts keep their own line");
    }

    // ---------- the reorganisation notice ----------

    [Fact]
    public void DistrictsNotice_PointsAtTheClassicLayout_InFiveLanguages()
    {
        Loc.Get("main_street.districts_notice").Should()
            .Be("Main Street has been reorganised into districts. Press ? for help, or choose the classic layout in Settings (~).");
        var table = MainStreetDistricts1113Tests.RepoRoot();
        foreach (string lang in new[] { "es", "fr", "hu", "it" })
        {
            var json = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement>>(
                System.IO.File.ReadAllText(System.IO.Path.Combine(table, $"Localization/{lang}.json")))!;
            json["main_street.districts_notice"].GetString().Should().Contain("(~)", lang);
            json["main_street.classic_tip"].GetString().Should().Contain("[~]", lang);
        }
    }

    [Fact]
    public void DistrictsNotice_IsNotShownInClassic_AndWaitsForTheDistricts()
    {
        var (street, output, hero) = ClassicMainStreetLayout1114Tests.Rig(7, true, "", "", "");
        hero.HintsShown.Remove(MainStreetLocation.DistrictsNoticeHint);
        typeof(MainStreetLocation).GetMethod("DisplayLocation", F)!.Invoke(street, null);
        ClassicMainStreetLayout1114Tests.Plain(street, output).Should().NotContain(Loc.Get("main_street.districts_notice"));
        hero.HintsShown.Should().NotContain(MainStreetLocation.DistrictsNoticeHint, "a classic player who switches later is still told once");
    }

    // ---------- the classic tip on the first ten district draws ----------

    private static int Draws(Character hero, int count, string mode, out string text)
    {
        bool compact = GameConfig.CompactMode;
        try
        {
            GameConfig.CompactMode = mode == "bbs";
            hero.ScreenReaderMode = mode == "screenreader";
            var street = new MainStreetLocation();
            var output = new System.IO.MemoryStream();
            var term = new TerminalEmulator(new LineStream(new[] { "", "", "" }), output);
            typeof(BaseLocation).GetField("terminal", F)!.SetValue(street, term);
            typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(street, hero);
            for (int i = 0; i < count; i++)
                typeof(MainStreetLocation).GetMethod("DisplayLocation", F)!.Invoke(street, null);
            text = ClassicMainStreetLayout1114Tests.Plain(street, output);
            return Regex.Matches(text, Regex.Escape(Loc.Get("main_street.classic_tip"))).Count;
        }
        finally { GameConfig.CompactMode = compact; }
    }

    private static Character Fresh() => new() { Name1 = "Tip", Name2 = "Tip", Level = 1, HP = 100, MaxHP = 100, AI = CharacterAI.Human };

    [Theory]
    [InlineData("visual")]
    [InlineData("screenreader")]
    [InlineData("bbs")]
    public void Tip_ShowsOnTheFirstTenDraws_ThenNeverAgain(string mode)
    {
        var hero = Fresh();
        Draws(hero, 10, mode, out _).Should().Be(10);
        hero.ClassicTipDraws.Should().Be(10);
        Draws(hero, 1, mode, out _).Should().Be(0, "the eleventh draw has no tip");
        Draws(hero, 5, mode, out _).Should().Be(0);
        hero.ClassicTipDraws.Should().Be(10);
    }

    [Fact]
    public void Tip_TenthDrawIsTheLast()
    {
        var hero = Fresh();
        Draws(hero, 9, "visual", out _).Should().Be(9);
        Draws(hero, 1, "visual", out _).Should().Be(1, "the tenth draw still shows it");
        Draws(hero, 1, "visual", out _).Should().Be(0);
    }

    [Fact]
    public void Tip_ForAVeteran_FollowsTheNotice_StillTenTimes()
    {
        var vet = new Character { Name1 = "Vet", Name2 = "Vet", Level = 7, MKills = 40, HP = 100, MaxHP = 100, AI = CharacterAI.Human };
        vet.HintsShown.UnionWith(new[] { "menu_tier_1", "menu_tier_2", "menu_tier_3" });
        Draws(vet, 1, "visual", out string first).Should().Be(0, "the first draw shows the notice, which names Settings, and no tip");
        first.Should().Contain(Loc.Get("main_street.districts_notice"));
        vet.ClassicTipDraws.Should().Be(0);
        Draws(vet, 11, "visual", out string text).Should().Be(10, "then the tip on the next ten draws");
        text.Should().NotContain(Loc.Get("main_street.districts_notice"));
        vet.ClassicTipDraws.Should().Be(10);
    }

    [Fact]
    public void Tip_IsNeverShownInClassic_AndSwitchingDoesNotResetIt()
    {
        var hero = Fresh();
        Draws(hero, 4, "visual", out _).Should().Be(4);
        hero.ClassicMainStreet = true;
        Draws(hero, 5, "visual", out string classicText).Should().Be(0);
        classicText.Should().Contain("[D]Dungeons");
        hero.ClassicTipDraws.Should().Be(4, "classic draws do not count");
        hero.ClassicMainStreet = false;
        Draws(hero, 10, "visual", out _).Should().Be(6, "back on districts the count goes on from four");
    }

    [Fact]
    public void TipCount_SurvivesSaveAndLoad()
    {
        var hero = Fresh();
        Draws(hero, 7, "visual", out _).Should().Be(7);
        var ser = typeof(SaveSystem).GetMethod("SerializePlayer", F)!;
        var data = (PlayerData)ser.Invoke(SaveSystem.Instance, new object[] { hero })!;
        data.ClassicTipDraws.Should().Be(7);
        var back = System.Text.Json.JsonSerializer.Deserialize<PlayerData>(System.Text.Json.JsonSerializer.Serialize(data))!;
        var restore = typeof(GameEngine).GetMethod("RestorePlayerFromSaveData", F)!;
        Character restored;
        try { restored = (Character)restore.Invoke(GameEngine.Instance, new object[] { back })!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
        restored.ClassicTipDraws.Should().Be(7);
        Draws(restored, 5, "visual", out _).Should().Be(3, "three draws were left before logout");

        new Character().ClassicTipDraws.Should().Be(0);
        System.Text.Json.JsonSerializer.Deserialize<PlayerData>("{}")!.ClassicTipDraws.Should().Be(0, "an old save starts the count");
    }
}
