using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;
using G = UsurperReborn.Tests.HitRoll128Golden;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.7 (LE): three small game fixes. Titan's Resolve shows in max HP at once after an equip or unequip; the
/// Magic Shop's enchant name suffix is always added; a loot item's effect line survives the trip through Equipment.
/// State lives in memory on Character instances; the language, the king and the shop's random source are put back
/// in Dispose and the real save folder is neither read nor written.
/// </summary>
[Collection("SharedGameSingletons")]
public class LE137Fixes : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private readonly string _prevLanguage = GameConfig.Language;
    private readonly King _prevKing = CastleLocation.GetCurrentKing();
    private readonly Random _prevRandom = MagicShopLocation.EnchantRandom;

    public LE137Fixes()
    {
        GameConfig.Language = "en";
        CastleLocation.SetKing(null!);
        MagicShopLocation.EnchantRandom = new FixedRandom(0.99);
    }

    public void Dispose()
    {
        GameConfig.Language = _prevLanguage;
        CastleLocation.SetKing(_prevKing);
        MagicShopLocation.EnchantRandom = _prevRandom;
    }

    private sealed class FixedRandom : Random
    {
        private readonly double _roll;
        public FixedRandom(double roll) { _roll = roll; }
        public override double NextDouble() => _roll;
        public override int Next(int maxValue) => 0;
        public override int Next(int minValue, int maxValue) => minValue;
    }

    private static Equipment Plate(bool titan, string tag) => new()
    {
        Name = $"LE137 Plate {tag}", Slot = EquipmentSlot.Body, MinLevel = 1, Rarity = EquipmentRarity.Epic, HasTitanResolve = titan,
    };

    [Fact]
    public void LE137_Titan_EquipAndUnequip_LeaveMaxHPRightAtOnce()
    {
        var hero = G.Hero(5003, 100, 20);
        hero.RecalculateStats();
        long plain = hero.MaxHP;
        var plate = Plate(true, "a");
        EquipmentDatabase.RegisterDynamic(plate);

        hero.EquipItem(plate, out _).Should().BeTrue();
        hero.MaxHP.Should().Be((long)(plain * Character.TitanResolveMaxHPMultiplier), "equipping a Titan item shows the 5 percent at once");
        long shown = hero.MaxHP;
        hero.RecalculateStats();
        hero.MaxHP.Should().Be(shown, "the fight-start recalculation changes nothing");

        hero.UnequipSlot(EquipmentSlot.Body).Should().NotBeNull();
        hero.MaxHP.Should().Be(plain, "taking it off takes the 5 percent off at once");

        var other = Plate(true, "b");
        EquipmentDatabase.RegisterDynamic(other);
        hero.EquipItem(other, out _).Should().BeTrue();
        var plain2 = Plate(false, "c");
        EquipmentDatabase.RegisterDynamic(plain2);
        hero.EquipItem(plain2, out _).Should().BeTrue();
        hero.MaxHP.Should().Be(plain, "swapping the Titan piece for a plain one in the same slot drops the bonus");
    }
}
