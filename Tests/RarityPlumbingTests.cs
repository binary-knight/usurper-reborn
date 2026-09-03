using System.Linq;
using System.Text.Json;
using Xunit;
using FluentAssertions;
using UsurperRemake.Systems;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1 slice: rarity is stored on generated items instead of living only in the
/// localized name prefix, and the template family travels with the item.
/// </summary>
public class RarityPlumbingTests
{
    [Fact]
    public void Generated_loot_stores_its_rolled_rarity_and_family()
    {
        var items = Enumerable.Range(0, 300).Select(_ => LootGenerator.GenerateDungeonLoot(40, CharacterClass.Warrior)).ToList();
        items.Should().Contain(i => i.Rarity > EquipmentRarity.Common, "300 rolls at level 40 include non-Common items");
        foreach (var item in items.Where(i => i.Rarity > EquipmentRarity.Common))
            LootGenerator.GetItemRarity(item).Should().Be((LootGenerator.ItemRarity)(int)item.Rarity, item.Name);
        items.Where(i => !string.IsNullOrEmpty(i.Family)).Should().NotBeEmpty("template items carry their English family name");
    }

    [Fact]
    public void Forced_rarity_is_authoritative_regardless_of_name()
    {
        var ring = LootGenerator.GenerateRing(40, LootGenerator.ItemRarity.Legendary);
        ring.Rarity.Should().Be(EquipmentRarity.Legendary);
        LootGenerator.GetItemRarity(ring).Should().Be(LootGenerator.ItemRarity.Legendary);
    }

    [Fact]
    public void Legacy_common_items_still_use_the_name_and_power_heuristic()
    {
        var byPower = new Item { Name = "Old Sword", Attack = 150, Rarity = EquipmentRarity.Common };
        LootGenerator.GetItemRarity(byPower).Should().Be(LootGenerator.ItemRarity.Epic);
        var plain = new Item { Name = "Old Sword", Attack = 5, Rarity = EquipmentRarity.Common };
        LootGenerator.GetItemRarity(plain).Should().Be(LootGenerator.ItemRarity.Common);
    }

    [Fact]
    public void Inventory_record_round_trips_family()
    {
        var item = new Item { Name = "Casque renforce", Family = "Reinforced Helm", Rarity = EquipmentRarity.Rare, Type = ObjType.Head, Armor = 12 };
        var json = JsonSerializer.Serialize(InventoryItemData.FromItem(item));
        var back = JsonSerializer.Deserialize<InventoryItemData>(json)!.ToItem();
        back.Family.Should().Be("Reinforced Helm");
        back.Rarity.Should().Be(EquipmentRarity.Rare);
    }

    [Fact]
    public void Old_inventory_record_without_family_loads_with_empty_family()
    {
        var json = "{\"Name\":\"Iron Helm\",\"Value\":50,\"Type\":2,\"Armor\":4,\"Rarity\":0}";
        var back = JsonSerializer.Deserialize<InventoryItemData>(json)!.ToItem();
        back.Family.Should().BeEmpty();
    }

    [Fact]
    public void Supreme_items_carry_a_stored_rarity()
    {
        ItemManager.InitializeItems();
        var staff = ItemManager.GetItem(1004);
        staff.Should().NotBeNull();
        staff!.Rarity.Should().Be(EquipmentRarity.Artifact);
    }
}
