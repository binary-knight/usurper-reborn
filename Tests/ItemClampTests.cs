using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.7: no item enters play above the corruption bounds. Clamps are signed and idempotent, a
/// legitimate endgame item is untouched, and every ingress heals: the registry, the saved-item
/// converter, and both Equipment/Item converters. The heal mutates only the caller's fresh copy.
/// </summary>
[Collection("SharedGameSingletons")]
public class ItemClampTests
{
    private static Equipment InflatedSpear() => new Equipment
    {
        Name = "Spear of Testing", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded,
        WeaponPower = 1_542_327, StrengthBonus = 13_185, DexterityBonus = 15_426, Value = 80_972_167,
        CriticalChanceBonus = 900, LifeSteal = 450, MaxHPBonus = 5_000_000, DefenceBonus = -9_999_999,
    };

    [Fact]
    public void Equipment_ClampsEveryClass_Signed_AndIsIdempotent()
    {
        var e = InflatedSpear();
        e.ClampStats(out var changes).Should().BeTrue();
        changes.Should().Contain("power 1,542,327->5,000").And.Contain("value 80,972,167->20,000,000");
        e.WeaponPower.Should().Be(GameConfig.MaxItemPower);
        e.StrengthBonus.Should().Be(GameConfig.MaxItemStatBonus);
        e.DexterityBonus.Should().Be(GameConfig.MaxItemStatBonus);
        e.CriticalChanceBonus.Should().Be(GameConfig.MaxItemPercent);
        e.LifeSteal.Should().Be(GameConfig.MaxItemPercent);
        e.MaxHPBonus.Should().Be(GameConfig.MaxItemVitalBonus);
        e.DefenceBonus.Should().Be(-GameConfig.MaxItemStatBonus, "a penalty stays a penalty");
        e.Value.Should().Be(GameConfig.MaxItemValue);
        e.ClampStats().Should().BeFalse("idempotent");
    }

    [Fact]
    public void Item_ClampsStats_MagicProperties_AndTheSummedLootEffects()
    {
        var item = new Item
        {
            Name = "Short Sword of Testing", Type = ObjType.Weapon, Attack = 847_913_257, Dexterity = 609_147_534,
            Value = 6_324_554_610, Strength = -50, BlockChance = 5_000, HP = int.MaxValue,
        };
        item.MagicProperties.MagicResistance = 4_000;
        for (int i = 0; i < 50; i++) item.LootEffects.Add(((int)LootGenerator.SpecialEffect.Constitution, 1_000));
        item.LootEffects.Add(((int)LootGenerator.SpecialEffect.LifeSteal, 700));
        item.LootEffects.Add(((int)LootGenerator.SpecialEffect.FireDamage, 1));

        item.ClampStats().Should().BeTrue();
        item.Attack.Should().Be(GameConfig.MaxItemPower);
        item.Dexterity.Should().Be(GameConfig.MaxItemStatBonus);
        item.Value.Should().Be(GameConfig.MaxItemValue);
        item.Strength.Should().Be(-50, "inside the bound, untouched");
        item.BlockChance.Should().Be(GameConfig.MaxItemPercent);
        item.HP.Should().Be(GameConfig.MaxItemVitalBonus);
        item.MagicProperties.MagicResistance.Should().Be(GameConfig.MaxItemPercent);
        item.LootEffects.Should().HaveCount(3, "fifty entries of one kind merge into one");
        item.LootEffects.Single(e => e.EffectType == (int)LootGenerator.SpecialEffect.Constitution).Value.Should().Be(GameConfig.MaxItemStatBonus, "the sum is bounded, not each entry");
        item.LootEffects.Single(e => e.EffectType == (int)LootGenerator.SpecialEffect.LifeSteal).Value.Should().Be(GameConfig.MaxItemPercent);
        item.LootEffects.Single(e => e.EffectType == (int)LootGenerator.SpecialEffect.FireDamage).Value.Should().Be(1);
        item.ClampStats().Should().BeFalse("idempotent");
    }

    [Fact]
    public void ALegitimateEndgameItem_AndEveryTemplate_AreUntouched()
    {
        // the strongest drop the loot formula can make: 135 x (1 + 100/80) x 4.0 x 1.15
        var artifact = new Equipment { Name = "Blade", WeaponPower = 1_397, StrengthBonus = 206, CriticalChanceBonus = 30, MaxHPBonus = 106, Value = 1_812_375 };
        artifact.ClampStats().Should().BeFalse();
        var cursed = new Item { Name = "Cursed", Attack = 300, Strength = -40, Value = 50_000 };
        cursed.LootEffects.Add(((int)LootGenerator.SpecialEffect.Strength, -25));
        cursed.ClampStats().Should().BeFalse();
        cursed.LootEffects.Should().ContainSingle().Which.Value.Should().Be(-25);

        foreach (var t in EquipmentDatabase.GetAll().Where(e => !EquipmentDatabase.IsDynamic(e.Id)))
            t.Clone().ClampStats(out var why).Should().BeFalse($"template {t.Id} {t.Name} is legitimate: {why}");
    }

    [Fact]
    public void TheRegistry_HealsTheCallersCopy_AndNothingElse()
    {
        var bystander = new Equipment { Name = "Spear of Testing", WeaponPower = 900, Value = 300_000 };
        EquipmentDatabase.RegisterDynamic(bystander);
        var template = EquipmentDatabase.GetOneHandedWeapons().First();
        int templatePower = template.WeaponPower; long templateValue = template.Value;
        long healedBefore = ItemLimits.HealedCount;

        var inflated = InflatedSpear();
        int id = EquipmentDatabase.RegisterDynamic(inflated);
        EquipmentDatabase.GetById(id)!.WeaponPower.Should().Be(GameConfig.MaxItemPower);
        ItemLimits.HealedCount.Should().Be(healedBefore + 1);

        var withId = InflatedSpear();
        EquipmentDatabase.RegisterDynamicWithId(withId, id + 5000);
        withId.WeaponPower.Should().Be(GameConfig.MaxItemPower, "clamped before the saved id is registered");

        bystander.WeaponPower.Should().Be(900, "another session's same-named item is not touched");
        template.WeaponPower.Should().Be(templatePower); template.Value.Should().Be(templateValue);
    }

    [Fact]
    public void ASavedItem_IsHealedOnTheWayOut_AndBothConvertersClamp()
    {
        var dto = new InventoryItemData { Name = "Short Sword of Testing", Type = ObjType.Weapon, Attack = 847_913_257, Dexterity = 609_147_534, Value = 6_324_554_610 };
        var item = dto.ToItem();
        item.Attack.Should().Be(GameConfig.MaxItemPower);
        item.Dexterity.Should().Be(GameConfig.MaxItemStatBonus);
        item.Value.Should().Be(GameConfig.MaxItemValue);

        var hero = new Character { Name1 = "clamp", Name2 = "Clamp", Class = CharacterClass.Warrior, Level = 25 };
        var asItem = hero.ConvertEquipmentToLegacyItem(InflatedSpear());
        asItem.Attack.Should().Be(GameConfig.MaxItemPower);
        asItem.Value.Should().Be(GameConfig.MaxItemValue);

        var raw = new Item { Name = "Raw", Type = ObjType.Weapon, Attack = 9_000_000, Value = 9_000_000_000 };
        var asEquip = Character.BuildEquipmentFromItem(raw, EquipmentSlot.MainHand, WeaponHandedness.OneHanded, WeaponType.Sword);
        asEquip.WeaponPower.Should().Be(GameConfig.MaxItemPower);
        asEquip.Value.Should().Be(GameConfig.MaxItemValue);
    }

    [Fact]
    public void AHealNamesTheAccount_OnlyInsideItsOwnScope()
    {
        using (ItemLimits.OwnerScope("someaccount"))
        {
            ItemLimits.Heal(new Item { Name = "X", Attack = 99_999 }, "test").Should().BeTrue();
            ItemLimits.Heal(new Item { Name = "Y", Attack = 10 }, "test").Should().BeFalse("nothing to heal, nothing logged");
        }
    }
}
