using System.Linq;
using Xunit;
using FluentAssertions;
using UsurperRemake.Systems;

namespace UsurperReborn.Tests;

/// <summary>v1.1: the Black Market rarity floor and rarity premium.</summary>
public class BlackMarketFloorTests
{
    [Fact]
    public void Floor_lifts_the_low_end_and_leaves_the_high_end_alone()
    {
        var items = Enumerable.Range(0, 300)
            .Select(_ => LootGenerator.GenerateDungeonLootWithMinRarity(30, CharacterClass.Warrior, LootGenerator.ItemRarity.Rare))
            .ToList();
        items.Should().OnlyContain(i => i.Rarity >= EquipmentRarity.Rare);
        items.Should().Contain(i => i.Rarity > EquipmentRarity.Rare, "the natural curve still rolls above the floor");
    }

    [Fact]
    public void Forced_rarity_pins_the_tier()
    {
        var item = LootGenerator.GenerateDungeonLootWithMinRarity(30, CharacterClass.Magician, LootGenerator.ItemRarity.Common, LootGenerator.ItemRarity.Legendary);
        item.Rarity.Should().Be(EquipmentRarity.Legendary);
    }

    [Fact]
    public void Config_tables_cover_every_tier_and_rise_monotonically()
    {
        GameConfig.BlackMarketRarityFloorByDreadTier.Length.Should().Be(GameConfig.BlackMarketGearSlotsByDreadTier.Length);
        GameConfig.BlackMarketRarityFloorByDreadTier.Should().BeInAscendingOrder();
        GameConfig.BlackMarketRarityMarkup.Length.Should().Be(6, "one entry per EquipmentRarity");
        GameConfig.BlackMarketRarityMarkup.Should().BeInAscendingOrder();
        GameConfig.BlackMarketRarityMarkup[0].Should().Be(1.0f, "Common price is unchanged");
    }
}
