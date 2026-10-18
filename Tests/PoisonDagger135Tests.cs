using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.5: Shadowfang, Venomstrike and Serpent's Fang carry a PoisonDamage value, but the poison proc in combat
/// asks for HasPoisonEnchant, which the three never set, so they never poisoned. Only these three set the flag;
/// at d5369c90 no catalog weapon had it.
/// </summary>
public class PoisonDagger135Tests
{
    private static readonly Dictionary<string, int> Daggers = new()
    {
        ["Shadowfang"] = 5,
        ["Venomstrike"] = 15,
        ["Serpent's Fang"] = 25,
    };

    [Fact]
    public void PoisonDaggers135_ThreeDaggersHavePoisonFlagAndValue()
    {
        EquipmentDatabase.Initialize();
        foreach (var kv in Daggers)
        {
            var e = EquipmentDatabase.GetByName(kv.Key);
            e.Should().NotBeNull(kv.Key);
            e!.HasPoisonEnchant.Should().BeTrue(kv.Key + " must have HasPoisonEnchant so the poison proc runs");
            e.PoisonDamage.Should().Be(kv.Value, kv.Key);
        }
    }

    [Fact]
    public void PoisonDaggers135_NoOtherWeaponChangesPoisonFlag()
    {
        EquipmentDatabase.Initialize();
        // List taken at d5369c90: no catalog weapon had HasPoisonEnchant.
        var baseline = new HashSet<string>();
        var now = EquipmentDatabase.GetAll()
            .Where(e => e.HasPoisonEnchant && !Daggers.ContainsKey(e.Name))
            .Select(e => e.Name).ToHashSet();
        now.Should().BeEquivalentTo(baseline, "only the three daggers gain the flag");
    }
}
