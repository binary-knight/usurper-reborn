using FluentAssertions;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.14: login stability. The classic weapon and armour tables of ItemManager are shared by every session;
/// they are built once and only read after, so a login's InitializeItems call changes nothing.
/// </summary>
public class ItemTablesOnce1314Tests
{
    [Fact]
    public void InitializeItems_CalledAgain_KeepsTheSameWeaponAndArmourObjects()
    {
        ItemManager.InitializeItems();
        var weapon = ItemManager.GetClassicWeapon(1);
        var armour = ItemManager.GetClassicArmor(1);

        ItemManager.InitializeItems();

        weapon.Should().NotBeNull();
        armour.Should().NotBeNull();
        ReferenceEquals(ItemManager.GetClassicWeapon(1), weapon).Should().BeTrue("the weapon table is built once");
        ReferenceEquals(ItemManager.GetClassicArmor(1), armour).Should().BeTrue("the armour table is built once");
    }

    [Fact]
    public void Tables_AreFilledWithoutAnyInitializeCall_AndKeepTheirKeys()
    {
        ItemManager.GetClassicWeapon(0)!.Name.Should().Be("Fists");
        ItemManager.GetClassicArmor(0).Should().BeNull("armour is keyed from 1");
        ItemManager.GetClassicArmor(1)!.Name.Should().Be("Skin");
        int armours = ItemManager.GetShopArmors().Count;
        ItemManager.GetClassicArmor(armours)!.Name.Should().Be("Ultimate Defense");
        ItemManager.GetClassicArmor(armours + 1).Should().BeNull();
    }
}
