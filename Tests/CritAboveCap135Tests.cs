using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.5: the 3x cap on the crit damage multiplier holds the Dexterity part only; the gear's crit damage adds above it.
/// </summary>
public class CritAboveCap135Tests
{
    [Fact]
    public void T1_Dex100_Gear10_Gives3Point1() =>
        StatEffectsSystem.GetCriticalDamageMultiplier(100, 10).Should().BeApproximately(3.1f, 0.001f, "gear adds above the Dexterity cap");

    [Fact]
    public void T2_Dex100_Gear100_Gives4() =>
        StatEffectsSystem.GetCriticalDamageMultiplier(100, 100).Should().BeApproximately(4.0f, 0.001f, "gear is not capped");

    [Fact]
    public void T3_Dex60_Gear10_Gives2Point6() =>
        StatEffectsSystem.GetCriticalDamageMultiplier(60, 10).Should().BeApproximately(2.6f, 0.001f, "below the cap the sum is as before");

    [Fact]
    public void T4_DexCapHolds()
    {
        StatEffectsSystem.GetCriticalDamageMultiplier(10, 0).Should().BeApproximately(1.5f, 0.001f, "Dex 10 is the base");
        StatEffectsSystem.GetCriticalDamageMultiplier(150, 0).Should().BeApproximately(3.0f, 0.001f, "the Dexterity part is capped at 3");
    }

    [Theory]
    [InlineData(10)]
    [InlineData(100)]
    public void T5_RealItemSum_AddsToThree(int critDamage)
    {
        var hero = new Character { Name1 = "tester", Name2 = "Tester", Level = 50, AI = CharacterAI.Human, AutoEquipDisabled = true };
        var item = new Equipment
        {
            Name = critDamage == 100 ? "Deathedge" : "Predator Test Blade", Slot = EquipmentSlot.MainHand, WeaponPower = 10,
            CriticalDamageBonus = critDamage, MinLevel = 1, Value = 100,
        };
        hero.EquippedItems[item.Slot] = EquipmentDatabase.RegisterDynamic(item);
        hero.GetEquipmentCritDamageBonus().Should().Be(critDamage, "the item sum is read from the worn gear");
        StatEffectsSystem.GetCriticalDamageMultiplier(100, hero.GetEquipmentCritDamageBonus())
            .Should().BeApproximately(3.0f + critDamage / 100f, 0.001f, "3.0 plus the gear sum / 100");
    }
}
