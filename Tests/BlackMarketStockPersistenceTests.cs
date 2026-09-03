using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.1: the Black Market rotation is persisted. A null cache means "re-roll" at the
/// Dark Alley, so an empty rotation (every slot bought today) must survive a save and load
/// as empty, not as null; only a legacy save with no field re-rolls.
/// </summary>
[Collection("SharedGameSingletons")]
public class BlackMarketStockPersistenceTests
{
    private static PlayerData Serialize(Character player)
    {
        var m = typeof(SaveSystem).GetMethod("SerializePlayer", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (PlayerData)m.Invoke(SaveSystem.Instance, new object[] { player })!;
    }

    private static Character Restore(PlayerData data)
    {
        var m = typeof(GameEngine).GetMethod("RestorePlayerFromSaveData", BindingFlags.NonPublic | BindingFlags.Instance)!;
        try
        {
            return (Character)m.Invoke(GameEngine.Instance, new object[] { data })!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw ex.InnerException;
        }
    }

    private static Character MakePlayer() => new Character
    {
        Name1 = "tester", Name2 = "Tester", Class = CharacterClass.Warrior, Race = CharacterRace.Human,
        Level = 5, HP = 50, MaxHP = 50, BaseMaxHP = 50, BaseStrength = 20, BaseDefence = 10,
    };

    private static Item MakeItem(string name) => new Item { Name = name, Value = 100 };

    [Fact]
    public void EmptyRotation_RoundTripsAsEmpty_NotNull()
    {
        var player = MakePlayer();
        player.CachedBlackMarketStock = new List<Item>(); // bought every slot today

        var data = Serialize(player);
        data.BlackMarketStock.Should().NotBeNull().And.BeEmpty();

        var json = JsonSerializer.Serialize(data);
        var back = JsonSerializer.Deserialize<PlayerData>(json)!;
        back.BlackMarketStock.Should().NotBeNull().And.BeEmpty("an empty list must not become null in JSON");

        var restored = Restore(back);
        restored.CachedBlackMarketStock.Should().NotBeNull("null is the re-roll trigger; a sold-out day must stay sold out");
        restored.CachedBlackMarketStock.Should().BeEmpty();
    }

    [Fact]
    public void LegacySaveWithoutStock_RestoresNull_SoTheMarketRerollsOnce()
    {
        var player = MakePlayer();
        player.CachedBlackMarketStock = null;

        var data = Serialize(player);
        data.BlackMarketStock.Should().BeNull();

        var json = JsonSerializer.Serialize(data);
        json.Should().NotContain("\"BlackMarketStock\":[]");
        var back = JsonSerializer.Deserialize<PlayerData>(json)!;
        back.BlackMarketStock.Should().BeNull();

        Restore(back).CachedBlackMarketStock.Should().BeNull();
    }

    [Fact]
    public void StockedRotation_RoundTripsEveryItem()
    {
        var player = MakePlayer();
        player.CachedBlackMarketStock = new List<Item> { MakeItem("Shadow Blade"), MakeItem("Night Cloak") };

        var back = JsonSerializer.Deserialize<PlayerData>(JsonSerializer.Serialize(Serialize(player)))!;
        var restored = Restore(back);
        restored.CachedBlackMarketStock.Should().NotBeNull();
        restored.CachedBlackMarketStock!.Select(i => i.Name).Should().Equal("Shadow Blade", "Night Cloak");
    }
}
