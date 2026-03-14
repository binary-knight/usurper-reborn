using Xunit;
using FluentAssertions;

namespace UsurperReborn.Tests;

/// <summary>
/// Tests for ShopItemGenerator (v0.52.5 expansion)
/// </summary>
public class ShopItemGeneratorTests
{
    [Fact]
    public void ShopGeneratedItems_HaveValidIdRange()
    {
        ShopItemGenerator.ShopGeneratedMin.Should().Be(50000);
        ShopItemGenerator.ShopGeneratedMax.Should().Be(99999);
    }

    [Theory]
    [InlineData(50000, true)]
    [InlineData(75000, true)]
    [InlineData(99999, true)]
    [InlineData(49999, false)]
    [InlineData(100000, false)]
    [InlineData(1, false)]
    [InlineData(0, false)]
    public void IsShopGenerated_CorrectlyIdentifiesShopItems(int id, bool expected)
    {
        ShopItemGenerator.IsShopGenerated(id).Should().Be(expected);
    }

    [Fact]
    public void InferWeaponType_Dagger()
    {
        ShopItemGenerator.InferWeaponType("Shadow Dagger").Should().Be(WeaponType.Dagger);
        ShopItemGenerator.InferWeaponType("Assassin's Knife").Should().Be(WeaponType.Dagger);
        ShopItemGenerator.InferWeaponType("Stiletto of Shadows").Should().Be(WeaponType.Dagger);
    }

    [Fact]
    public void InferWeaponType_Bow()
    {
        ShopItemGenerator.InferWeaponType("Longbow of the Hunt").Should().Be(WeaponType.Bow);
        ShopItemGenerator.InferWeaponType("Shortbow").Should().Be(WeaponType.Bow);
    }

    [Fact]
    public void InferWeaponType_Staff()
    {
        ShopItemGenerator.InferWeaponType("Staff of Power").Should().Be(WeaponType.Staff);
    }

    [Fact]
    public void InferWeaponType_Instrument()
    {
        ShopItemGenerator.InferWeaponType("Battle Lute").Should().Be(WeaponType.Instrument);
        ShopItemGenerator.InferWeaponType("War Drum").Should().Be(WeaponType.Instrument);
    }

    [Fact]
    public void InferWeaponType_DefaultsToSword()
    {
        ShopItemGenerator.InferWeaponType("Enchanted Blade").Should().Be(WeaponType.Sword);
    }

    [Fact]
    public void InferHandedness_Bow_IsTwoHanded()
    {
        ShopItemGenerator.InferHandedness(WeaponType.Bow)
            .Should().Be(WeaponHandedness.TwoHanded);
    }

    [Fact]
    public void InferHandedness_Dagger_IsOneHanded()
    {
        ShopItemGenerator.InferHandedness(WeaponType.Dagger)
            .Should().Be(WeaponHandedness.OneHanded);
    }
}
