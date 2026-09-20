using System;
using System.Linq;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.7: the audit sees the signature it missed. The old rule compared wealth to lifetime
/// earnings, and an account that sold a duped item for billions passed, because the sale was the
/// earnings. These rules alert and never correct, once per player per subject per session.
/// </summary>
[Collection("SharedGameSingletons")]
public class GoldAuditTests
{
    private static Character Player(string name, int level, long gold = 0, long bank = 0)
    {
        var p = new Character { Name1 = name, Name2 = name, Class = CharacterClass.Warrior, Level = level, Gold = gold, BankGold = bank };
        GoldAudit.Reset(p.DisplayName ?? name);
        return p;
    }

    [Fact]
    public void TheIncidentsFingerprint_Alerts_WhereTheOldRuleWasSilent()
    {
        var p = Player("suspect", 25, gold: 29_864_426);
        p.Statistics.TotalGoldEarned = 3_755_813_640;
        p.Statistics.TotalGoldFromSelling = 3_755_754_521;
        p.Statistics.TotalItemsSold = 2;
        p.Statistics.HighestSingleHit = 4_140_950_498;

        (p.Gold + p.BankGold > p.Statistics.TotalGoldEarned * 5).Should().BeFalse("the old rule saw nothing: the sale was the earnings");
        GoldAudit.Inspect(p).Should().Be(3, "gold per item, lifetime sales, and the impossible hit");
    }

    [Fact]
    public void AnItemOverTheBounds_Alerts_AndIsNotChanged()
    {
        var p = Player("holder", 25);
        var item = new Item { Name = "Short Sword of Testing", Type = ObjType.Weapon, Attack = 847_913_257, Value = 6_324_554_610 };
        item.MagicProperties.MagicResistance = 4_000;
        item.LootEffects.Add(((int)LootGenerator.SpecialEffect.Constitution, 900_000));
        p.Inventory.Add(item);

        GoldAudit.Inspect(p).Should().Be(1);
        item.Attack.Should().Be(847_913_257, "the audit reports, it does not correct");
        item.Value.Should().Be(6_324_554_610);
        item.MagicProperties.MagicResistance.Should().Be(4_000, "a memberwise clone shares this object; the check must not touch it");
        item.LootEffects.Should().ContainSingle().Which.Value.Should().Be(900_000);

        var worn = new Equipment { Name = "Spear of Testing", Slot = EquipmentSlot.MainHand, WeaponPower = 1_542_327, Value = 80_972_167 };
        int id = EquipmentDatabase.RegisterDynamic(worn);   // the registry heals on the way in
        worn.WeaponPower.Should().Be(GameConfig.MaxItemPower, "so a worn item over the bounds means a hole that is still open");
        worn.WeaponPower = 1_542_327;                        // simulate one that got past
        var p2 = Player("wearer", 25);
        p2.EquippedItems[EquipmentSlot.MainHand] = id;
        GoldAudit.Inspect(p2).Should().Be(1);
        worn.WeaponPower.Should().Be(1_542_327, "reported, not corrected");
    }

    [Fact]
    public void AnHonestPlayer_IsSilent_IncludingABulkSaleAtTheBestFence()
    {
        var p = Player("honest", 100, gold: 1_223_850, bank: 22_554_667);
        p.Statistics.TotalGoldEarned = 40_000_000;
        p.Statistics.TotalGoldFromSelling = 9_909_363;
        p.Statistics.TotalItemsSold = 84;
        p.Statistics.HighestSingleHit = 60_905;
        p.Inventory.Add(new Item { Name = "Blade of the Righteous", Type = ObjType.Weapon, Attack = 1_397, Value = 1_812_375 });
        GoldAudit.Inspect(p).Should().Be(0);

        // one legitimate item at the ceiling, sold at the best fence in the game
        var seller = Player("fence", 100);
        seller.Statistics.TotalGoldFromSelling = (long)(GameConfig.MaxItemValue * 0.8);
        seller.Statistics.TotalItemsSold = 1;
        seller.Statistics.TotalGoldEarned = seller.Statistics.TotalGoldFromSelling;
        GoldAudit.Inspect(seller).Should().Be(0, "the per-item rule allows the most a single item can fetch");
    }

    [Fact]
    public void ABulkSale_CountsItsItems_SoTheAverageIsHonest()
    {
        var stats = new PlayerStatistics();
        stats.RecordSale(400_000, 40);
        stats.TotalItemsSold.Should().Be(40, "a bulk sale used to count as one item");
        stats.TotalGoldFromSelling.Should().Be(400_000);
        stats.RecordSale(5_000);
        stats.TotalItemsSold.Should().Be(41);
        stats.RecordSale(1_000, 0);
        stats.TotalItemsSold.Should().Be(42, "a zero count still means at least one item");
    }

    [Fact]
    public void EachSubjectIsReportedOncePerSession_ButANewSubjectStillReports()
    {
        var p = Player("repeat", 10);
        p.Statistics.HighestSingleHit = 5_000_000;
        GoldAudit.Inspect(p).Should().Be(1);
        GoldAudit.Inspect(p).Should().Be(0, "three autosaves a minute do not repeat it");
        GoldAudit.Inspect(p).Should().Be(0);

        p.Inventory.Add(new Item { Name = "New Problem", Type = ObjType.Weapon, Attack = 9_000_000 });
        GoldAudit.Inspect(p).Should().Be(1, "a new item is a new subject");
        GoldAudit.Inspect(p).Should().Be(0);
    }

    [Fact]
    public void MissingStatistics_ZeroCounts_AndExtremeTotals_DoNotThrow()
    {
        var p = Player("edge", 1);
        p.Statistics.TotalItemsSold = 0;
        p.Statistics.TotalGoldFromSelling = 0;
        GoldAudit.Inspect(p).Should().Be(0);

        var big = Player("big", int.MaxValue, gold: long.MaxValue, bank: long.MaxValue);
        big.Statistics.TotalGoldEarned = long.MaxValue;
        big.Statistics.TotalGoldFromSelling = long.MaxValue;
        big.Statistics.TotalItemsSold = 1;
        big.Statistics.HighestSingleHit = long.MaxValue;
        big.Invoking(x => GoldAudit.Inspect(x)).Should().NotThrow("no overflow on any threshold");
    }
}
