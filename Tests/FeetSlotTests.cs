using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// A player report: boots in the backpack are not offered for the feet slot, and weapons are
/// offered instead; the same was reported for companions. Both paths are driven here with boots
/// and a weapon in the backpack.
/// </summary>
[Collection("SharedGameSingletons")]
public class FeetSlotTests
{
    private static readonly BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static Character HeroWithBootsAndADagger()
    {
        var hero = new Character { Name1 = "feet", Name2 = "Feet", Class = CharacterClass.Warrior, Level = 30, HP = 500, MaxHP = 500 };
        hero.Inventory.Add(new Item { Name = "Leather Boots", Type = ObjType.Feet, Armor = 6, Value = 120 });
        hero.Inventory.Add(new Item { Name = "Spiked Iron Boots", Type = ObjType.Feet, Armor = 32, Value = 5120 });
        hero.Inventory.Add(new Item { Name = "Vampiric Dagger", Type = ObjType.Weapon, Attack = 60, Value = 8400 });
        return hero;
    }

    [Fact]
    public void ThePlayersFeetSlot_OffersTheBoots_AndNotTheDagger()
    {
        var hero = HeroWithBootsAndADagger();
        var inv = new InventorySystem(new TerminalEmulator(new MemoryStream(), new MemoryStream()), hero);
        var offered = (List<Item>)typeof(InventorySystem).GetMethod("FilterInventoryBySlot", F)!.Invoke(inv, new object[] { EquipmentSlot.Feet })!;
        offered.Select(i => i.Name).Should().BeEquivalentTo(new[] { "Leather Boots", "Spiked Iron Boots" });
    }

    [Fact]
    public void ACompanionsFeetSlot_OffersTheBoots_AndNotTheDagger()
    {
        var hero = HeroWithBootsAndADagger();
        var inn = new InnLocation();
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(inn, hero);
        var offered = (System.Collections.IList)typeof(BaseLocation).GetMethod("GetItemsForSlot", F)!.Invoke(inn, new object[] { EquipmentSlot.Feet })!;
        var names = offered.Cast<object>().Select(t => ((Equipment)t.GetType().GetField("Item1")!.GetValue(t)!).Name).ToList();
        names.Should().BeEquivalentTo(new[] { "Leather Boots", "Spiked Iron Boots" });
    }
}
