using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.5: an item keeps what it shows through the backpack. Each test takes an item through five equip and
/// unequip trips and one Magic Shop enchant made while the item is in the bag, and checks both the item's own
/// number and the stat the character gets from it, so a value is neither lost nor counted twice.
/// </summary>
[Collection("SharedGameSingletons")]
public class EnchantRoundTrip135Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int Trips = 5;

    // ---------- helpers ----------

    private static Character Hero() => new()
    {
        Name1 = "tripper", Name2 = "Tripper", Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 100,
        HP = 5000, MaxHP = 5000, BaseMaxHP = 5000, Mana = 500, MaxMana = 500, BaseMaxMana = 500,
        BaseStrength = 60, BaseDexterity = 40, BaseConstitution = 40, BaseIntelligence = 30, BaseWisdom = 30,
        BaseCharisma = 30, BaseAgility = 30, BaseStamina = 30, BaseDefence = 10,
        AI = CharacterAI.Human, Gold = 9_000_000_000, AutoEquipDisabled = true,
    };

    /// <summary>Equip a bag item the way the inventory screen does: build it, register it, equip it.</summary>
    private static Equipment Wear(Character hero, global::Item bagItem, EquipmentSlot slot, WeaponHandedness hand, WeaponType type)
    {
        hero.Inventory.Remove(bagItem);
        var eq = Character.BuildEquipmentFromItem(bagItem, slot, hand, type);
        EquipmentDatabase.RegisterDynamic(eq);
        hero.EquipItem(eq, slot, out var message).Should().BeTrue(message);
        hero.RecalculateStats();
        return hero.GetEquipment(slot)!;
    }

    /// <summary>Unequip into the bag the way the inventory screen does.</summary>
    private static global::Item TakeOff(Character hero, EquipmentSlot slot)
    {
        var off = hero.UnequipSlot(slot);
        off.Should().NotBeNull();
        var item = hero.ConvertEquipmentToLegacyItem(off!);
        hero.Inventory.Add(item);
        hero.RecalculateStats();
        return item;
    }

    /// <summary>Run the Magic Shop enchant menu with the given answers; with nothing worn, target 1 is the first bag item.</summary>
    private static async Task ShopEnchant(Character hero, params string[] answers)
    {
        var shop = new MagicShopLocation();
        var output = new MemoryStream();
        TerminalEmulator term = null!;
        term = new TerminalEmulator(new LineStream(answers.Concat(Enumerable.Repeat("", 12)), _ =>
        {
            term.StreamWriterInternal?.Flush();
            output.WriteByte((byte)'\n');
        }), output);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(shop, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(shop, hero);
        var r = typeof(MagicShopLocation).GetMethod("EnchantEquipment", F)!.Invoke(shop, new object[] { hero });
        if (r is Task t) await t;
    }

    /// <summary>A bag enchant that touches none of the values under test: Minor (+2) Strength.</summary>
    private static Task BagEnchantMinorStrength(Character hero) => ShopEnchant(hero, "1", "1", "2", "Y");

    /// <summary>A loot item built by the loot generator's own effect code, as a dungeon drop is.</summary>
    private static global::Item Loot(string name, ObjType type, bool isWeapon, params (LootGenerator.SpecialEffect effect, int value)[] effects)
    {
        var item = new global::Item { Name = name, Type = type, Value = 5_000, MinLevel = 1 };
        if (isWeapon) item.Attack = 40; else item.Armor = 20;
        typeof(LootGenerator).GetMethod("ApplyEffectsToItem", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { item, effects.ToList(), isWeapon });
        return item;
    }

    private static int Effect(global::Item item, LootGenerator.SpecialEffect effect) =>
        item.LootEffects.Where(e => e.EffectType == (int)effect).Select(e => e.Value).DefaultIfEmpty(0).Max();

    // ---------- X2: life steal and mana steal ----------

    private static global::Item WornBlade(Character hero, int leech)
    {
        var blade = new Equipment
        {
            Name = "Trip Blade", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded,
            WeaponType = WeaponType.Sword, WeaponPower = 40, Value = 5_000, MinLevel = 1, LifeSteal = leech, ManaSteal = leech,
        };
        return hero.ConvertEquipmentToLegacyItem(blade);
    }

    private static async Task LeechSurvives(Character hero, global::Item start, int leech)
    {
        hero.Inventory.Add(start);
        var bag = start;
        for (int trip = 1; trip <= Trips; trip++)
        {
            var worn = Wear(hero, bag, EquipmentSlot.MainHand, WeaponHandedness.OneHanded, WeaponType.Sword);
            worn.LifeSteal.Should().Be(leech, "trip {0}: the worn blade gives the life steal it shows", trip);
            worn.ManaSteal.Should().Be(leech, "trip {0}: the worn blade gives the mana steal it shows", trip);
            hero.GetEquipmentLifeSteal().Should().Be(leech, "trip {0}: the character's life steal is the item's, once", trip);
            hero.GetEquipmentManaSteal().Should().Be(leech, "trip {0}: the character's mana steal is the item's, once", trip);
            bag = TakeOff(hero, EquipmentSlot.MainHand);
            Effect(bag, LootGenerator.SpecialEffect.LifeSteal).Should().Be(leech, "trip {0}: the bag keeps the life steal as written", trip);
            Effect(bag, LootGenerator.SpecialEffect.ManaSteal).Should().Be(leech, "trip {0}: the bag keeps the mana steal as written", trip);
            hero.GetEquipmentLifeSteal().Should().Be(0, "nothing is worn");
        }

        await BagEnchantMinorStrength(hero);
        bag = hero.Inventory.Single();
        bag.Strength.Should().Be(2, "the bag enchant landed");
        Effect(bag, LootGenerator.SpecialEffect.LifeSteal).Should().Be(leech, "an enchant made in the bag keeps the life steal");
        Effect(bag, LootGenerator.SpecialEffect.ManaSteal).Should().Be(leech, "an enchant made in the bag keeps the mana steal");
        var after = Wear(hero, bag, EquipmentSlot.MainHand, WeaponHandedness.OneHanded, WeaponType.Sword);
        after.LifeSteal.Should().Be(leech, "the bag enchant did not change the life steal");
        hero.GetEquipmentLifeSteal().Should().Be(leech, "after the bag enchant the character's life steal is the item's");
        hero.GetEquipmentManaSteal().Should().Be(leech, "after the bag enchant the character's mana steal is the item's");
    }

    [Theory]
    [InlineData(3)]    // Lifedrinker's +3
    [InlineData(20)]
    public async Task X2_WornLeech_KeepsItsValue_ThroughFiveTripsAndABagEnchant(int leech)
    {
        var hero = Hero();
        await LeechSurvives(hero, WornBlade(hero, leech), leech);
    }

    [Theory]
    [InlineData(3)]    // the lowest loot roll
    [InlineData(25)]   // the loot cap
    public async Task X2_FreshLootLeech_GivesWhatItShows_FromTheFirstEquip(int leech)
    {
        var hero = Hero();
        var drop = Loot("Vampiric Trip Blade", ObjType.Weapon, true,
            (LootGenerator.SpecialEffect.LifeSteal, leech), (LootGenerator.SpecialEffect.ManaSteal, leech));
        drop.Description[0].Should().Be($"Life Steal +{leech}, Mana Steal +{leech}", "the drop shows its values");
        await LeechSurvives(hero, drop, leech);
    }

    // ---------- X3: a shield's armour class from an enchant ----------

    private const int ShieldBlock = 10;

    private static Equipment TripShield() => new()
    {
        Name = "Trip Shield", Slot = EquipmentSlot.OffHand, Handedness = WeaponHandedness.OffHandOnly,
        WeaponType = WeaponType.Shield, ShieldBonus = ShieldBlock, BlockChance = 10, Value = 5_000, MinLevel = 1,
    };

    private static Equipment WearShield(Character hero, global::Item bag) =>
        Wear(hero, bag, EquipmentSlot.OffHand, WeaponHandedness.OffHandOnly, WeaponType.Shield);

    private static void ShieldTrips(Character hero, global::Item bag, int armour, long bareArmPow)
    {
        for (int trip = 1; trip <= Trips; trip++)
        {
            var worn = WearShield(hero, bag);
            worn.ArmorClass.Should().Be(armour, "trip {0}: the worn shield keeps the armour class its enchant gave", trip);
            worn.ShieldBonus.Should().Be(ShieldBlock, "trip {0}: the block value is unchanged", trip);
            (hero.ArmPow - bareArmPow).Should().Be(ShieldBlock + armour, "trip {0}: the character gets block plus armour class, once", trip);
            bag = TakeOff(hero, EquipmentSlot.OffHand);
            bag.Armor.Should().Be(armour, "trip {0}: the bag keeps the shield's armour class", trip);
            bag.ShieldBonus.Should().Be(ShieldBlock, "trip {0}: the bag keeps the block value", trip);
            hero.ArmPow.Should().Be(bareArmPow, "nothing is worn");
        }
    }

    [Fact]
    public async Task X3_DivineBlessingOnABagShield_KeepsItsArmourClass_ThroughFiveTrips()
    {
        var hero = Hero();
        hero.RecalculateStats();
        long bare = hero.ArmPow;
        hero.Inventory.Add(hero.ConvertEquipmentToLegacyItem(TripShield()));

        await ShopEnchant(hero, "1", "5", "Y");   // Divine Blessing, made while the shield is in the bag
        var bag = hero.Inventory.Single();
        bag.Name.Should().Be("Trip Shield (Blessed)", "the bag enchant landed");
        bag.Armor.Should().Be(3, "Divine Blessing's +3 armour class is kept by the bag write-back");

        ShieldTrips(hero, bag, 3, bare);
    }

    [Fact]
    public async Task X3_ChosenArmourClassOnAWornShield_IsKept_ThroughFiveTripsAndABagEnchant()
    {
        var hero = Hero();
        hero.RecalculateStats();
        long bare = hero.ArmPow;
        var bag = hero.ConvertEquipmentToLegacyItem(TripShield());
        hero.Inventory.Add(bag);
        WearShield(hero, bag);

        await ShopEnchant(hero, "1", "1", "6", "Y");   // Minor, armour class, on the worn shield
        hero.GetEquipment(EquipmentSlot.OffHand)!.ArmorClass.Should().Be(2, "the worn enchant landed");
        hero.RecalculateStats();
        (hero.ArmPow - bare).Should().Be(ShieldBlock + 2, "today's worn value: block plus the enchant's armour class");

        bag = TakeOff(hero, EquipmentSlot.OffHand);
        ShieldTrips(hero, bag, 2, bare);

        await BagEnchantMinorStrength(hero);
        bag = hero.Inventory.Single();
        bag.Strength.Should().Be(2, "the bag enchant landed");
        bag.Armor.Should().Be(2, "an enchant made in the bag keeps the shield's armour class");
        var worn = WearShield(hero, bag);
        worn.ArmorClass.Should().Be(2, "after the bag enchant the shield still has its armour class");
        (hero.ArmPow - bare).Should().Be(ShieldBlock + 2, "after the bag enchant the character gets block plus armour class, once");
    }
}
