using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 2.0: the classic Main Street is the default for every character, new or loaded; the districts are a choice in
/// Settings (S), stored as DistrictsMainStreet. ClassicMainStreet is still written but no longer read for the layout.
/// </summary>
[Collection("SharedGameSingletons")]
public class ClassicMainStreetDefault200Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const string ClassicMark = "[D]Dungeons";
    private static string DistrictsMark => Loc.Get("main_street.district_merchant_row");

    private static Character Restore(PlayerData data)
    {
        var restore = typeof(GameEngine).GetMethod("RestorePlayerFromSaveData", F)!;
        try { return (Character)restore.Invoke(GameEngine.Instance, new object[] { data })!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    private static PlayerData RoundTrip(Character hero)
    {
        var ser = typeof(SaveSystem).GetMethod("SerializePlayer", F)!;
        var data = (PlayerData)ser.Invoke(SaveSystem.Instance, new object[] { hero })!;
        return System.Text.Json.JsonSerializer.Deserialize<PlayerData>(System.Text.Json.JsonSerializer.Serialize(data))!;
    }

    /// <summary>The visual Main Street drawn the given number of times for this character, ANSI stripped.</summary>
    private static string Draw(Character hero, int times = 1)
    {
        bool compact = GameConfig.CompactMode;
        try
        {
            GameConfig.CompactMode = false;
            var street = new MainStreetLocation();
            var output = new MemoryStream();
            var term = new TerminalEmulator(new LineStream(new[] { "", "", "" }), output);
            typeof(BaseLocation).GetField("terminal", F)!.SetValue(street, term);
            typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(street, hero);
            for (int i = 0; i < times; i++)
                typeof(MainStreetLocation).GetMethod("DisplayLocation", F)!.Invoke(street, null);
            return ClassicMainStreetLayout1114Tests.Plain(street, output);
        }
        finally { GameConfig.CompactMode = compact; }
    }

    private static Character Hero() => new() { Name1 = "Plain", Name2 = "Plain", Level = 7, MKills = 3, HP = 100, MaxHP = 100, AI = CharacterAI.Human };

    [Fact]
    public void ANewCharacter_SeesTheClassicMainStreet()
    {
        var hero = Hero();
        string text = Draw(hero);
        text.Should().Contain(ClassicMark, "classic is the default layout").And.NotContain(DistrictsMark);

        hero.DistrictsMainStreet = true;
        Draw(hero).Should().Contain(DistrictsMark, "the districts are drawn once chosen").And.NotContain(ClassicMark);
    }

    [Fact]
    public void ASaveWithClassicOff_AndNoDistrictsField_LoadsToClassic()
    {
        var loaded = Restore(System.Text.Json.JsonSerializer.Deserialize<PlayerData>(
            "{\"Name1\":\"Old\",\"Name2\":\"Old\",\"Level\":7,\"HP\":100,\"MaxHP\":100,\"ClassicMainStreet\":false}")!);
        loaded.DistrictsMainStreet.Should().BeFalse();
        loaded.AI = CharacterAI.Human;
        Draw(loaded).Should().Contain(ClassicMark).And.NotContain(DistrictsMark);

        var chose = Restore(System.Text.Json.JsonSerializer.Deserialize<PlayerData>(
            "{\"Name1\":\"New\",\"Name2\":\"New\",\"Level\":7,\"HP\":100,\"MaxHP\":100,\"ClassicMainStreet\":false,\"DistrictsMainStreet\":true}")!);
        chose.DistrictsMainStreet.Should().BeTrue("a save that chose the districts keeps them");
        chose.AI = CharacterAI.Human;
        Draw(chose).Should().Contain(DistrictsMark);
    }

    private static async Task PressS(Character hero)
    {
        var street = new MainStreetLocation();
        var term = new TerminalEmulator(new LineStream(new[] { "S", "0" }), new MemoryStream());
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(street, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(street, hero);
        await (Task)typeof(BaseLocation).GetMethod("ShowPreferencesMenu", F)!.Invoke(street, null)!;
    }

    [Fact]
    public async Task TheSToggle_TurnsDistrictsOn_ThenOff_AndEachSurvivesASaveAndLoad()
    {
        var hero = new Character { Name1 = "Toggle", Name2 = "Toggle", Level = 5, HP = 50, MaxHP = 50, AI = CharacterAI.Human };
        hero.DistrictsMainStreet.Should().BeFalse();

        await PressS(hero);
        hero.DistrictsMainStreet.Should().BeTrue("one press of S turns the districts on");
        var on = RoundTrip(hero);
        on.DistrictsMainStreet.Should().BeTrue();
        on.ClassicMainStreet.Should().BeFalse("the old field says what an older build should show");
        var loadedOn = Restore(on);
        loadedOn.DistrictsMainStreet.Should().BeTrue("the choice survives a save and load");

        await PressS(loadedOn);
        loadedOn.DistrictsMainStreet.Should().BeFalse("a second press turns them off");
        var off = RoundTrip(loadedOn);
        off.DistrictsMainStreet.Should().BeFalse();
        off.ClassicMainStreet.Should().BeTrue();
        Restore(off).DistrictsMainStreet.Should().BeFalse();
    }

    [Fact]
    public void TheClassicTip_ShowsOnlyToAPlayerWhoChoseTheDistricts()
    {
        string tip = Loc.Get("main_street.classic_tip");
        var classic = Hero();
        classic.HintsShown.Add(MainStreetLocation.DistrictsNoticeHint);
        string classicText = Draw(classic, 10);
        Regex.Matches(classicText, Regex.Escape(tip)).Count.Should().Be(0, "a classic player never sees the tip");
        classic.ClassicTipDraws.Should().Be(0);

        var opted = Hero();
        opted.HintsShown.Add(MainStreetLocation.DistrictsNoticeHint);
        opted.DistrictsMainStreet = true;
        string optedText = Draw(opted, 3);
        optedText.Should().Contain(DistrictsMark);
        Regex.Matches(optedText, Regex.Escape(tip)).Count.Should().Be(3, "a player who chose the districts sees its tip");
        opted.ClassicTipDraws.Should().Be(3);
    }
}
