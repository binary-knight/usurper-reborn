using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>v1.1.15: the "menu keys need Enter" preference: saved per character, on by default.</summary>
[Collection("SharedGameSingletons")]
public class MenuKeysNeedEnterPref1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    [Fact]
    public void Default_IsOn_ForANewCharacter_AnEmptySave_AndAnOldSave()
    {
        new Character().MenuKeysNeedEnter.Should().BeTrue("a new character's menus wait for Enter");
        new PlayerData().MenuKeysNeedEnter.Should().BeTrue();
        System.Text.Json.JsonSerializer.Deserialize<PlayerData>("{}")!.MenuKeysNeedEnter
            .Should().BeTrue("an empty save reads as on");
        var old = System.Text.Json.JsonSerializer.Deserialize<PlayerData>("{\"AutoCombatHealPercent\":40,\"ClassicMainStreet\":true}")!;
        old.MenuKeysNeedEnter.Should().BeTrue("a save written before the field existed reads as on");
        bool before = GameConfig.MenuKeysNeedEnter;
        try
        {
            GameConfig.MenuKeysNeedEnter = false;
            Restore(System.Text.Json.JsonSerializer.Deserialize<PlayerData>("{}")!).MenuKeysNeedEnter.Should().BeTrue();
            Restore(old).MenuKeysNeedEnter.Should().BeTrue("an existing save without the field restores as on");
            GameConfig.MenuKeysNeedEnter.Should().BeTrue("the restored character's choice is mirrored to the terminal flag");
        }
        finally { GameConfig.MenuKeysNeedEnter = before; }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Preference_RoundTripsThroughTheSave(bool needEnter)
    {
        bool before = GameConfig.MenuKeysNeedEnter;
        try
        {
            var p = new Character { Name1 = "Round", Name2 = "Round", Level = 5, HP = 50, MaxHP = 50, MenuKeysNeedEnter = needEnter };
            var ser = typeof(SaveSystem).GetMethod("SerializePlayer", F)!;
            var data = (PlayerData)ser.Invoke(SaveSystem.Instance, new object[] { p })!;
            data.MenuKeysNeedEnter.Should().Be(needEnter);
            var json = System.Text.Json.JsonSerializer.Serialize(data);
            json.Should().Contain($"\"MenuKeysNeedEnter\":{(needEnter ? "true" : "false")}");
            var back = System.Text.Json.JsonSerializer.Deserialize<PlayerData>(json)!;
            GameConfig.MenuKeysNeedEnter = !needEnter;
            Restore(back).MenuKeysNeedEnter.Should().Be(needEnter);
            GameConfig.MenuKeysNeedEnter.Should().Be(needEnter, "the load mirrors the preference to the terminal flag");
        }
        finally { GameConfig.MenuKeysNeedEnter = before; }
    }

    internal static Character Restore(PlayerData data)
    {
        var restore = typeof(GameEngine).GetMethod("RestorePlayerFromSaveData", F)!;
        try { return (Character)restore.Invoke(GameEngine.Instance, new object[] { data })!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }
}

/// <summary>v1.1.15: the local console branch of GetKeyInput reads a line when the flag is on, one key when off.</summary>
[Collection("SharedGameSingletons")]
public class MenuKeysNeedEnterConsole1115Tests
{
    private static async Task<(string result, int keyReads, string leftOver)> Drive(bool needEnter, Func<TerminalEmulator, Task<string>> read)
    {
        bool oldFlag = GameConfig.MenuKeysNeedEnter;
        var oldSeam = TerminalEmulator.ConsoleReadKey;
        var oldIn = Console.In;
        var oldOut = Console.Out;
        int keyReads = 0;
        var input = new StringReader("qx\n");
        try
        {
            GameConfig.MenuKeysNeedEnter = needEnter;
            TerminalEmulator.ConsoleReadKey = () => { keyReads++; return new ConsoleKeyInfo('k', ConsoleKey.K, false, false, false); };
            Console.SetIn(input);
            Console.SetOut(new StringWriter());
            // no streams, no BBS adapter, no door mode: the local console branch
            string result = await read(new TerminalEmulator());
            return (result, keyReads, input.ReadToEnd());
        }
        finally
        {
            GameConfig.MenuKeysNeedEnter = oldFlag;
            TerminalEmulator.ConsoleReadKey = oldSeam;
            Console.SetIn(oldIn);
            Console.SetOut(oldOut);
        }
    }

    [Fact]
    public async Task FlagOn_ReadsALine_AndTakesItsFirstKey()
    {
        var (result, keyReads, leftOver) = await Drive(true, t => t.GetKeyInput());
        result.Should().Be("q", "the typed line's first character is the key");
        keyReads.Should().Be(0, "no single-key read when menu keys need Enter");
        leftOver.Should().BeEmpty("the whole line, Enter included, is consumed");
    }

    [Fact]
    public async Task FlagOff_ActsOnOneKeypress()
    {
        var (result, keyReads, leftOver) = await Drive(false, t => t.GetKeyInput());
        result.Should().Be("k");
        keyReads.Should().Be(1);
        leftOver.Should().Be("qx\n", "no line is read when the setting is off");
    }

    [Fact]
    public async Task Pause_StillContinuesOnOneKey_WithTheFlagOn()
    {
        var (_, keyReads, leftOver) = await Drive(true, async t => { await t.PressAnyKey("pause"); return ""; });
        // a real console reads one key through the seam; redirected stdin reads one character
        if (keyReads == 1) leftOver.Should().Be("qx\n");
        else { keyReads.Should().Be(0); leftOver.Should().Be("x\n", "the pause consumed a single key, not a line"); }
    }
}

/// <summary>v1.1.15: the Settings entry (key E) toggles the preference, mirrors it to the terminal flag, and saves.</summary>
[Collection("SharedGameSingletons")]
public class MenuKeysNeedEnterSettings1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrefsMenu_TogglesIt_InTheVisualAndScreenReaderMenus(bool screenReader)
    {
        var street = new MainStreetLocation();
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(new[] { "E", "E", "E", "0" }), output);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(street, term);
        var hero = new Character { Name1 = "Prefs", Name2 = "Prefs", Level = 5, HP = 50, MaxHP = 50, AI = CharacterAI.Human, ScreenReaderMode = screenReader };
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(street, hero);
        bool oldSr = GameConfig.ScreenReaderMode;
        bool oldFlag = GameConfig.MenuKeysNeedEnter;
        try
        {
            GameConfig.ScreenReaderMode = screenReader;
            GameConfig.MenuKeysNeedEnter = true;
            BaseLocation.MenuKeysSettingShown.Should().BeTrue("the test runs as single-player, not online or door");
            // three presses of E: off, on, off
            await (Task)typeof(BaseLocation).GetMethod("ShowPreferencesMenu", F)!.Invoke(street, null)!;
            hero.MenuKeysNeedEnter.Should().BeFalse("E pressed three times from the default leaves it off");
            GameConfig.MenuKeysNeedEnter.Should().BeFalse("the toggle takes effect on the next menu, not after a reload");
            term.StreamWriterInternal?.Flush();
            string text = System.Text.RegularExpressions.Regex.Replace(System.Text.Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "");
            string label = Loc.Get("prefs.menu_keys_need_enter");
            text.Should().Contain(screenReader
                ? $"  E. {Loc.Get("prefs.toggle", label)}"
                : $"E] {label}: {Loc.Get("prefs.on")}");
            if (screenReader)
                text.Should().Contain($"  {label}: {Loc.Get("prefs.enabled")}").And.Contain($"  {label}: {Loc.Get("prefs.disabled")}");
            else
                text.Should().Contain($"E] {label}: {Loc.Get("prefs.off")}");
            text.Should().Contain(Loc.Get("base.pref_menu_keys_need_enter_set", Loc.Get("prefs.off")))
                .And.Contain(Loc.Get("base.pref_menu_keys_need_enter_set", Loc.Get("prefs.on")));

            // the saved record carries the new value
            var data = (PlayerData)typeof(SaveSystem).GetMethod("SerializePlayer", F)!.Invoke(SaveSystem.Instance, new object[] { hero })!;
            data.MenuKeysNeedEnter.Should().BeFalse();
        }
        finally { GameConfig.ScreenReaderMode = oldSr; GameConfig.MenuKeysNeedEnter = oldFlag; }
    }

    [Fact]
    public void Label_IsLocalized_InFiveLanguages()
    {
        foreach (string lang in new[] { "en", "es", "fr", "hu", "it" })
        {
            var table = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement>>(
                File.ReadAllText(Path.Combine(MainStreetDistricts1113Tests.RepoRoot(), $"Localization/{lang}.json")))!;
            foreach (string key in new[] { "prefs.menu_keys_need_enter", "base.pref_menu_keys_need_enter_set" })
                table.Should().ContainKey(key, lang);
        }
        Loc.Get("prefs.menu_keys_need_enter").Should().Be("Menu keys need Enter");
    }
}
