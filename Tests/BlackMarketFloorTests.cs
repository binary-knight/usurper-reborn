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

public class BlackMarketSlotFillTests
{
    private static Item Stub(LootGenerator.ItemRarity min, LootGenerator.ItemRarity? forced, LootGenerator.ItemRarity natural)
        => new Item { Name = "stub", Rarity = (EquipmentRarity)(int)(forced ?? (LootGenerator.ItemRarity)System.Math.Max((int)natural, (int)min)) };

    [Fact]
    public void Nightmare_has_exactly_one_legendary_even_when_the_curve_rolls_more()
    {
        // every natural roll is Legendary
        var stock = UsurperRemake.Locations.DarkAlleyLocation.FillBlackMarketSlots(5, LootGenerator.ItemRarity.Epic, nightmare: true, legendaryCap: 1,
            (min, forced) => Stub(min, forced, LootGenerator.ItemRarity.Legendary));
        stock.Should().HaveCount(5);
        stock.Count(i => i.Rarity >= EquipmentRarity.Legendary).Should().Be(1);
        stock.Should().OnlyContain(i => i.Rarity >= EquipmentRarity.Epic);
        stock.Should().NotContainNulls();
    }

    [Fact]
    public void Below_nightmare_nothing_is_forced_and_the_floor_holds()
    {
        var stock = UsurperRemake.Locations.DarkAlleyLocation.FillBlackMarketSlots(4, LootGenerator.ItemRarity.Rare, nightmare: false, legendaryCap: 1,
            (min, forced) => { forced.Should().BeNull(); return Stub(min, forced, LootGenerator.ItemRarity.Common); });
        stock.Should().HaveCount(4);
        stock.Should().OnlyContain(i => i.Rarity == EquipmentRarity.Rare);
    }

    [Fact]
    public void A_null_from_the_generator_never_reaches_the_rotation()
    {
        int calls = 0;
        var stock = UsurperRemake.Locations.DarkAlleyLocation.FillBlackMarketSlots(3, LootGenerator.ItemRarity.Epic, nightmare: true, legendaryCap: 1,
            (min, forced) => (++calls % 2 == 0) ? null : Stub(min, forced, LootGenerator.ItemRarity.Legendary));
        stock.Should().NotContainNulls();
        stock.Count(i => i.Rarity >= EquipmentRarity.Legendary).Should().BeLessOrEqualTo(1);
    }
}
