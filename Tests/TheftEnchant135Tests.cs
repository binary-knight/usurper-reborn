using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>An item stolen from a sleeper arrives with every field it had: enchant flags, procs, crit, curse.</summary>
[Collection("SharedGameSingletons")]
public class TheftEnchant135Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static PlayerData Save(Character hero) =>
        (PlayerData)typeof(SaveSystem).GetMethod("SerializePlayer", F)!.Invoke(SaveSystem.Instance, new object[] { hero })!;

    private static PropertyInfo[] Fields() => typeof(Equipment).GetProperties()
        .Where(p => p.CanRead && p.CanWrite && p.Name != nameof(Equipment.Id)).ToArray();

    [Fact]
    public void TheftEnchant135_StolenItemEqualsVictimsItemFieldForField()
    {
        var item = new Equipment { Name = "Cursed Blade of Embers", Slot = EquipmentSlot.MainHand, WeaponType = WeaponType.Sword,
            Handedness = WeaponHandedness.OneHanded, Rarity = EquipmentRarity.Rare, Family = "Blade", MinLevel = 4, Value = 900 };
        // every enchant, proc, crit and restriction field set to a non-default value, found by reflection
        foreach (var p in Fields())
        {
            if (p.Name is "Name" or "Slot" or "WeaponType" or "Handedness" or "Rarity" or "Family" or "MinLevel" or "Value") continue;
            var t = p.PropertyType;
            if (t == typeof(bool)) p.SetValue(item, true);
            else if (t == typeof(int)) p.SetValue(item, 3);
            else if (t == typeof(long)) p.SetValue(item, 5L);
            else if (t == typeof(string)) p.SetValue(item, "x");
            else if (t.IsEnum) p.SetValue(item, Enum.GetValues(t).Cast<object>().Last());
        }
        item.IsCursed = true;
        item.IsIdentified = true;
        item.ClassRestrictions = new() { CharacterClass.Warrior };
        item.EnchantBase = "";   // the record is a JSON string read back by the game; not part of this check

        var victim = new Character { Name1 = "sleeper", Name2 = "sleeper", Class = CharacterClass.Warrior, Race = CharacterRace.Human,
            Level = 50, HP = 100, MaxHP = 100, AI = CharacterAI.Human, AutoEquipDisabled = true };
        victim.EquippedItems[EquipmentSlot.MainHand] = EquipmentDatabase.RegisterDynamic(item);
        var saved = Save(victim).DynamicEquipment.Single(d => d.Name == item.Name);

        var stolen = DormitoryLocation.StolenEquipmentFrom(saved, item.Name);

        foreach (var p in Fields())
        {
            var want = p.GetValue(item);
            var got = p.GetValue(stolen);
            if (want is IEnumerable e && want is not string)
                (got as IEnumerable)!.Cast<object>().Should().Equal(e.Cast<object>(), $"the stolen item keeps {p.Name}");
            else
                got.Should().Be(want, $"the stolen item keeps {p.Name}");
        }
        stolen.IsCursed.Should().BeTrue("the curse comes with the item");
    }
}
