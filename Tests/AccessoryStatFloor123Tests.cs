using System.Linq;
using FluentAssertions;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.3: shop rings and necklaces from the basic material templates got armor and no stat bonus
/// below about level 53. Each one now carries a very small floor: Dexterity on rings, Wisdom on
/// necklaces, +1, or +2 from level 30. Items that already had a bonus are unchanged.
/// </summary>
[Collection("SharedGameSingletons")]
public class AccessoryStatFloor123Tests
{
    [Fact]
    public void EveryShopAccessory_HasAStatBonusBesidesArmor()
    {
        var bare = EquipmentDatabase.GetShopRings().Concat(EquipmentDatabase.GetShopNecklaces())
            .Where(e => !ShopItemGenerator.HasStatBonus(e))
            .Select(e => $"{e.Name} L{e.MinLevel}")
            .ToList();

        bare.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Leather Cord", 1)]
    [InlineData("Bone Necklace", 1)]
    [InlineData("Silver Chain", 10)]
    public void BasicNecklace_GetsASmallWisdomFloor(string name, int level)
    {
        var item = EquipmentDatabase.GetShopNecklaces().First(e => e.Name == name && e.MinLevel == level);

        item.WisdomBonus.Should().Be(1);
        item.StrengthBonus.Should().Be(0);
        item.DexterityBonus.Should().Be(0);
        item.ArmorClass.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData("Copper Ring", 1)]
    [InlineData("Silver Ring", 5)]
    [InlineData("Gold Ring", 15)]
    public void BasicRing_GetsASmallDexterityFloor(string name, int level)
    {
        var item = EquipmentDatabase.GetShopRings().First(e => e.Name == name && e.MinLevel == level);

        item.DexterityBonus.Should().Be(1);
        item.WisdomBonus.Should().Be(0);
        item.ArmorClass.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Floor_IsTwoFromLevel30_AndNeverMore()
    {
        var floored = new Equipment { Slot = EquipmentSlot.LFinger };
        ShopItemGenerator.ApplyAccessoryStatFloor(floored, 30);
        floored.DexterityBonus.Should().Be(2);

        var low = new Equipment { Slot = EquipmentSlot.Neck };
        ShopItemGenerator.ApplyAccessoryStatFloor(low, 29);
        low.WisdomBonus.Should().Be(1);

        var top = EquipmentDatabase.GetShopRings().Concat(EquipmentDatabase.GetShopNecklaces())
            .Where(e => e.Name is "Copper Ring" or "Silver Ring" or "Gold Ring" or "Leather Cord" or "Bone Necklace" or "Silver Chain")
            .Max(e => e.DexterityBonus + e.WisdomBonus);
        top.Should().BeLessThanOrEqualTo(2);
    }

    [Fact]
    public void Floor_LeavesItemsThatAlreadyHaveABonusAlone()
    {
        var item = new Equipment { Slot = EquipmentSlot.LFinger, StrengthBonus = 3 };
        ShopItemGenerator.ApplyAccessoryStatFloor(item, 20);

        item.DexterityBonus.Should().Be(0);
        item.StrengthBonus.Should().Be(3);
    }
}
