using System.Linq;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.7: an enchant adds what it cost to an item's value and nothing more, so reselling always
/// loses gold; and the enchant count and kinds survive the backpack, an unequip, and a save, so the
/// five-enchant limit and the one-of-each-kind rule hold for items that are not being worn.
/// </summary>
[Collection("SharedGameSingletons")]
public class EnchantValueTests
{
    private const double BestResaleShare = 0.8;   // the best fence in the game pays 80 percent of value

    [Theory]
    [InlineData(100L, 2_000L)]
    [InlineData(1_000_000L, 2_000L)]
    [InlineData(4_000_000L, 25_000L)]
    [InlineData(19_999_000L, 25_000L)]
    public void AThousandEnchants_AlwaysLoseGoldOnResale(long startValue, long charge)
    {
        var e = new Equipment { Name = "Blade", WeaponPower = 50, Value = startValue };
        long paid = 0;
        for (int i = 0; i < 1000; i++)
        {
            long before = e.Value;
            MagicShopLocation.AddEnchantValue(e, charge);
            paid += charge;
            (e.Value - before).Should().BeLessThanOrEqualTo(charge, "value grows by the charge, never by a multiple of the item");
        }
        e.Value.Should().BeLessThanOrEqualTo(GameConfig.MaxItemValue);
        double resaleGain = (e.Value - startValue) * BestResaleShare;
        resaleGain.Should().BeLessThan(paid, "even at the best fence, the enchants cost more than they added");
    }

    [Fact]
    public void TwoEnchants_AddExactlyTwoCharges_AndANegativeOrAbsurdInputIsSafe()
    {
        var e = new Equipment { Name = "Blade", Value = 10_000 };
        MagicShopLocation.AddEnchantValue(e, 2_000);
        MagicShopLocation.AddEnchantValue(e, 2_000);
        e.Value.Should().Be(14_000, "the old rule made this 22,500");
        MagicShopLocation.AddEnchantValue(e, -5_000);
        e.Value.Should().Be(14_000, "a negative charge adds nothing");
        var corrupt = new Equipment { Name = "Corrupt", Value = long.MaxValue - 5 };
        MagicShopLocation.AddEnchantValue(corrupt, 2_000);
        corrupt.Value.Should().Be(GameConfig.MaxItemValue, "saturates, never wraps");
    }

    [Fact]
    public void TheEnchantCountAndKinds_SurviveTheBackpack_AnUnequip_AndASave()
    {
        var hero = new Character { Name1 = "ench", Name2 = "Ench", Class = CharacterClass.Warrior, Level = 40 };
        var worn = new Equipment { Name = "Long Sword", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Sword, WeaponPower = 40, Value = 5_000, Description = "A fine blade." };
        string[] kinds = { "str", "dex", "fire", "holy", "crit" };
        foreach (var k in kinds) { worn.IncrementEnchantmentCount(); worn.AddEnchantedKind(k); }
        worn.GetEnchantmentCount().Should().Be(5);
        worn.GetEnchantedKinds().Should().BeEquivalentTo(kinds);

        // into the bag (the unequip and the Magic Shop's bag write-back both use this converter)
        var inBag = hero.ConvertEquipmentToLegacyItem(worn);
        inBag.EnchantMarkers.Should().Contain("[E:5]");

        // through a save
        var reloaded = InventoryItemData.FromItem(inBag).ToItem();
        reloaded.EnchantMarkers.Should().Contain("[E:5]");

        // back out of the bag, the way the Magic Shop reads a bag item and the way an equip does
        var again = Character.BuildEquipmentFromItem(reloaded, EquipmentSlot.MainHand, WeaponHandedness.OneHanded, WeaponType.Sword);
        again.GetEnchantmentCount().Should().Be(5, "a sixth enchant is refused for a bag item too");
        again.GetEnchantedKinds().Should().BeEquivalentTo(kinds, "the kinds ride along, so the same kind is still refused twice");
        (again.GetEnchantmentCount() >= GameConfig.MaxEnchantments).Should().BeTrue();

        // and it does not double up on a second trip
        var twice = Character.BuildEquipmentFromItem(hero.ConvertEquipmentToLegacyItem(again), EquipmentSlot.MainHand, WeaponHandedness.OneHanded, WeaponType.Sword);
        twice.GetEnchantmentCount().Should().Be(5);
        System.Text.RegularExpressions.Regex.Matches(twice.Description, @"\[E:\d+\]").Count.Should().Be(1);
    }

    [Fact]
    public void AnUnenchantedItem_CarriesNoMarkers_AndAnOldSaveLoadsClean()
    {
        var hero = new Character { Name1 = "plain", Name2 = "Plain", Class = CharacterClass.Warrior, Level = 10 };
        var plain = new Equipment { Name = "Dagger", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponPower = 8, Value = 100, Description = "Sharp." };
        hero.ConvertEquipmentToLegacyItem(plain).EnchantMarkers.Should().BeEmpty();
        InventoryItemData.FromItem(hero.ConvertEquipmentToLegacyItem(plain)).EnchantMarkers.Should().BeNull("nothing written for an unenchanted item");
        new InventoryItemData { Name = "Old", Type = ObjType.Weapon, Attack = 5 }.ToItem().EnchantMarkers.Should().BeEmpty("a save from before 1.1.7 has no field");
    }

    [Fact]
    public void LosingOrPayingToRemoveAnEnchant_FreesItsKindAgain()
    {
        var e = new Equipment { Name = "Blade", WeaponPower = 40, Value = 5_000, Description = "A fine blade." };
        foreach (var k in new[] { "str", "dex", "fire" }) { e.IncrementEnchantmentCount(); e.AddEnchantedKind(k); }

        // a failed enchant destroys one: the count drops and the kind is freed with it
        e.TrimEnchantedKindsTo(2);
        e.GetEnchantedKinds().Should().HaveCount(2, "the lost enchant's kind can be applied again");

        // paid removal strips the item back to nothing: both markers go
        e.ClearEnchantMarkers();
        e.GetEnchantmentCount().Should().Be(0);
        e.GetEnchantedKinds().Should().BeEmpty("a player who paid to clear the item is not barred from re-enchanting it");
        e.Description.Should().Be("A fine blade.", "the item's own text survives");

        // and the cleared state persists, rather than being hidden by a conversion
        var hero = new Character { Name1 = "clear", Name2 = "Clear", Class = CharacterClass.Warrior, Level = 40 };
        var round = Character.BuildEquipmentFromItem(InventoryItemData.FromItem(hero.ConvertEquipmentToLegacyItem(e)).ToItem(), EquipmentSlot.MainHand, WeaponHandedness.OneHanded, WeaponType.Sword);
        round.GetEnchantmentCount().Should().Be(0);
        round.GetEnchantedKinds().Should().BeEmpty();
    }
}
