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
/// v1.1.14: the Main Street layout preference, saved per character and toggled in the prefs menu. Since 2.0 the stored
/// choice is DistrictsMainStreet (off by default, so classic); ClassicMainStreet is still written for older builds.
/// </summary>
[Collection("SharedGameSingletons")]
public class ClassicMainStreetPref1114Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    [Fact]
    public void Default_IsClassic_ForANewCharacter_AndAnOldSave()
    {
        new Character().DistrictsMainStreet.Should().BeFalse("a new character gets the classic layout");
        new Character().ClassicMainStreet.Should().BeTrue();
        new PlayerData().DistrictsMainStreet.Should().BeFalse();
        System.Text.Json.JsonSerializer.Deserialize<PlayerData>("{}")!.DistrictsMainStreet
            .Should().BeFalse("a save written before the field existed reads as classic");
        Restore(new PlayerData()).DistrictsMainStreet.Should().BeFalse("an existing save without the field restores as classic");
        foreach (string json in new[] { "{\"ClassicMainStreet\":false}", "{\"ClassicMainStreet\":true}", "{\"AutoCombatHealPercent\":40}" })
        {
            var back = Restore(System.Text.Json.JsonSerializer.Deserialize<PlayerData>(json)!);
            back.DistrictsMainStreet.Should().BeFalse(json + " has no DistrictsMainStreet, so it loads to classic");
            back.ClassicMainStreet.Should().BeTrue(json);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Preference_RoundTripsThroughTheSave(bool classic)
    {
        var p = new Character { Name1 = "Round", Name2 = "Round", Level = 5, HP = 50, MaxHP = 50, DistrictsMainStreet = !classic };
        var ser = typeof(SaveSystem).GetMethod("SerializePlayer", F)!;
        var data = (PlayerData)ser.Invoke(SaveSystem.Instance, new object[] { p })!;
        data.DistrictsMainStreet.Should().Be(!classic);
        data.ClassicMainStreet.Should().Be(classic, "the old field is still written, true when classic is shown");
        var json = System.Text.Json.JsonSerializer.Serialize(data);
        json.Should().Contain($"\"DistrictsMainStreet\":{(classic ? "false" : "true")}")
            .And.Contain($"\"ClassicMainStreet\":{(classic ? "true" : "false")}");
        var back = System.Text.Json.JsonSerializer.Deserialize<PlayerData>(json)!;
        Restore(back).DistrictsMainStreet.Should().Be(!classic);
        Restore(back).ClassicMainStreet.Should().Be(classic);
    }

    private static Character Restore(PlayerData data)
    {
        var restore = typeof(GameEngine).GetMethod("RestorePlayerFromSaveData", F)!;
        try { return (Character)restore.Invoke(GameEngine.Instance, new object[] { data })!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrefsMenu_TogglesIt_InTheVisualAndScreenReaderMenus(bool screenReader)
    {
        var street = new MainStreetLocation();
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(new[] { "S", "S", "S", "0" }), output);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(street, term);
        var hero = new Character { Name1 = "Prefs", Name2 = "Prefs", Level = 5, HP = 50, MaxHP = 50, AI = CharacterAI.Human, ScreenReaderMode = screenReader };
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(street, hero);
        bool oldSr = GameConfig.ScreenReaderMode;
        try
        {
            GameConfig.ScreenReaderMode = screenReader;
            // three presses of S: on, off, on
            await (Task)typeof(BaseLocation).GetMethod("ShowPreferencesMenu", F)!.Invoke(street, null)!;
            hero.DistrictsMainStreet.Should().BeTrue("S pressed three times leaves the districts on");
            term.StreamWriterInternal?.Flush();
            string text = Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "");
            string label = Loc.Get("prefs.main_street_layout");
            text.Should().Contain(screenReader
                ? $"  S. {label} ({Loc.Get("prefs.main_street_districts")})"
                : $"S] {label}: {Loc.Get("prefs.main_street_districts")}");
            text.Should().Contain(screenReader
                ? $"  S. {label} ({Loc.Get("prefs.main_street_classic")})"
                : $"S] {label}: {Loc.Get("prefs.main_street_classic")}");
            text.Should().Contain(Loc.Get("base.pref_main_street_layout_set", Loc.Get("prefs.main_street_classic")))
                .And.Contain(Loc.Get("base.pref_main_street_layout_set", Loc.Get("prefs.main_street_districts")));
        }
        finally { GameConfig.ScreenReaderMode = oldSr; }
    }

    [Fact]
    public void Label_IsLocalized_InFiveLanguages()
    {
        foreach (string lang in new[] { "en", "es", "fr", "hu", "it" })
        {
            var table = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(
                File.ReadAllText(Path.Combine(MainStreetDistricts1113Tests.RepoRoot(), $"Localization/{lang}.json")))!;
            foreach (string key in new[] { "prefs.main_street_layout", "prefs.main_street_districts", "prefs.main_street_classic", "base.pref_main_street_layout_set" })
                table.Should().ContainKey(key, lang);
        }
        Loc.Get("prefs.main_street_layout").Should().Be("Main Street layout");
        BaseLocation.MainStreetLayoutName(false).Should().Be("Districts");
        BaseLocation.MainStreetLayoutName(true).Should().Be("Classic");
    }
}
