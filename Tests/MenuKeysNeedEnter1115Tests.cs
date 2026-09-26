using System.Reflection;
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
