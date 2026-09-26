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
