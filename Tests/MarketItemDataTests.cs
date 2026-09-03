using System.Collections.Generic;
using System.Text.Json;
using Xunit;
using FluentAssertions;
using UsurperRemake.Systems;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.0.7: an item that passes through NPC market stock or a marketplace listing
/// must come back whole. The legacy eight-field record stripped rarity, level
/// requirement, loot effects, identification and shield bonus.
/// </summary>
public class MarketItemDataTests
{
    private static Item MakeLegendary() => new Item
    {
        Name = "Legendary Flaming Sword",
        Value = 12345,
        Type = ObjType.Weapon,
        Attack = 80,
        Strength = 5,
        Dexterity = 3,
        MinLevel = 40,
        Rarity = EquipmentRarity.Legendary,
        IsIdentified = true,
        ShieldBonus = 0,
        LootEffects = new List<(int EffectType, int Value)> { (2, 15) }
    };

    [Fact]
    public void Round_trip_keeps_every_field_after_json()
    {
        var data = MarketItemData.FromItem(MakeLegendary());
        var json = JsonSerializer.Serialize(data);
        var back = JsonSerializer.Deserialize<MarketItemData>(json)!;
        var item = back.ToItem();

        item.Rarity.Should().Be(EquipmentRarity.Legendary);
        item.MinLevel.Should().Be(40);
        item.IsIdentified.Should().BeTrue();
        item.Dexterity.Should().Be(3);
        item.LootEffects.Should().ContainSingle().Which.Should().Be((2, 15));
        item.Name.Should().Be("Legendary Flaming Sword");
        item.Attack.Should().Be(80);
    }

    [Fact]
    public void Legacy_fields_match_the_detail_record_for_older_binaries()
    {
        var data = MarketItemData.FromItem(MakeLegendary());
        data.Detail.Should().NotBeNull();
        var d = data.Detail!;
        // An older binary reads only the legacy fields; they must never drift from Detail.
        data.ItemName.Should().Be(d.Name);
        data.ItemValue.Should().Be(d.Value);
        data.ItemType.Should().Be(d.Type);
        data.Attack.Should().Be(d.Attack);
        data.Armor.Should().Be(d.Armor);
        data.Strength.Should().Be(d.Strength);
        data.Defence.Should().Be(d.Defence);
        data.IsCursed.Should().Be(d.IsCursed);
    }

    [Fact]
    public void Old_save_without_detail_still_loads_the_legacy_fields()
    {
        var json = "{\"ItemName\":\"Iron Sword\",\"ItemValue\":50,\"ItemType\":1,\"Attack\":10,\"Armor\":0,\"Strength\":0,\"Defence\":0,\"IsCursed\":false}";
        var back = JsonSerializer.Deserialize<MarketItemData>(json)!;
        back.Detail.Should().BeNull();
        var item = back.ToItem();
        item.Name.Should().Be("Iron Sword");
        item.Attack.Should().Be(10);
        item.Rarity.Should().Be(EquipmentRarity.Common);
    }
}
