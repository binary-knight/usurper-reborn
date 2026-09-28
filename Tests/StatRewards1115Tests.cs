using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.0 lost stat rewards, piece 1 commit 1: StatKind and Character.GrantPermanentStat. A grant
/// writes the Base* field, so it survives RecalculateStats, an equipment change, a level-up and a
/// save round trip; the other stats are unchanged; the floors and the optional cap hold.
/// </summary>
[Collection("SharedGameSingletons")]
public class StatRewards1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    internal static Character Hero(string name, CharacterClass cls = CharacterClass.Warrior) => new Character
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Human, Level = 10, HP = 100, MaxHP = 100,
        BaseMaxHP = 100, BaseStrength = 10, BaseDexterity = 10, BaseConstitution = 10, BaseIntelligence = 20,
        BaseWisdom = 20, BaseCharisma = 10, BaseDefence = 5, BaseStamina = 10, BaseAgility = 10,
        BaseMaxMana = cls == CharacterClass.Magician ? 100 : 0,
        Mental = 80, Class = cls, Race = CharacterRace.Human
    };

    internal static Character Fresh(string name, CharacterClass cls = CharacterClass.Warrior)
    {
        var c = Hero(name, cls);
        c.RecalculateStats();
        c.HP = c.MaxHP;
        c.Mana = c.MaxMana;
        return c;
    }

    internal static Character RoundTrip(Character c)
    {
        var ser = typeof(SaveSystem).GetMethod("SerializePlayer", F)!;
        PlayerData data;
        try { data = (PlayerData)ser.Invoke(SaveSystem.Instance, new object[] { c })!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
        var back = JsonSerializer.Deserialize<PlayerData>(JsonSerializer.Serialize(data))!;
        return MenuKeysNeedEnterPref1115Tests.Restore(back);
    }

    private static readonly StatKind[] Attributes =
    {
        StatKind.Strength, StatKind.Dexterity, StatKind.Constitution, StatKind.Intelligence, StatKind.Wisdom,
        StatKind.Charisma, StatKind.Defence, StatKind.Stamina, StatKind.Agility
    };

    internal static long Derived(Character c, StatKind s) => s switch
    {
        StatKind.Strength => c.Strength, StatKind.Dexterity => c.Dexterity, StatKind.Constitution => c.Constitution,
        StatKind.Intelligence => c.Intelligence, StatKind.Wisdom => c.Wisdom, StatKind.Charisma => c.Charisma,
        StatKind.Defence => c.Defence, StatKind.Stamina => c.Stamina, StatKind.Agility => c.Agility,
        StatKind.MaxHP => c.MaxHP, StatKind.MaxMana => c.MaxMana, _ => throw new ArgumentOutOfRangeException()
    };

    private static Dictionary<StatKind, (long b, long d)> Snapshot(Character c) =>
        Enum.GetValues<StatKind>().ToDictionary(s => s, s => (c.GetBaseStat(s), Derived(c, s)));

    // ---------------- survives ----------------

    [Theory]
    [InlineData(StatKind.Strength)]
    [InlineData(StatKind.Dexterity)]
    [InlineData(StatKind.Intelligence)]
    [InlineData(StatKind.Wisdom)]
    [InlineData(StatKind.Charisma)]
    [InlineData(StatKind.Defence)]
    [InlineData(StatKind.Stamina)]
    [InlineData(StatKind.Agility)]
    public void AGrant_RaisesBaseAndDerived_AndSurvivesRecalculateStats(StatKind stat)
    {
        var c = Fresh("SrRecalc");
        long b0 = c.GetBaseStat(stat), d0 = Derived(c, stat);
        c.GrantPermanentStat(stat, 3);
        c.GetBaseStat(stat).Should().Be(b0 + 3, "the grant goes to the Base field");
        Derived(c, stat).Should().Be(d0 + 3);
        c.RecalculateStats();
        Derived(c, stat).Should().Be(d0 + 3, "a full recalculation keeps the grant");
    }

    [Fact]
    public void AGrant_LeavesEveryOtherStatUnchanged()
    {
        var c = Fresh("SrOthers");
        var before = Snapshot(c);
        c.GrantPermanentStat(StatKind.Dexterity, 4);
        var after = Snapshot(c);
        foreach (var s in Enum.GetValues<StatKind>().Where(s => s != StatKind.Dexterity))
            after[s].Should().Be(before[s], $"{s} is not touched by a Dexterity grant");
        after[StatKind.Dexterity].Should().Be((before[StatKind.Dexterity].b + 4, before[StatKind.Dexterity].d + 4));
    }

    [Fact]
    public void AGrant_SurvivesASaveRoundTrip()
    {
        var c = Fresh("SrSave");
        c.GrantPermanentStat(StatKind.Strength, 5);
        c.GrantPermanentStat(StatKind.Wisdom, 2);
        var r = RoundTrip(c);
        r.BaseStrength.Should().Be(15);
        r.Strength.Should().Be(c.Strength, "the loaded Strength keeps the grant");
        r.BaseWisdom.Should().Be(22);
        r.Wisdom.Should().Be(c.Wisdom);
    }

    [Fact]
    public void AGrant_SurvivesAnEquipmentChange()
    {
        var c = Fresh("SrEquip");
        c.GrantPermanentStat(StatKind.Strength, 3);
        var ring = new Equipment { Name = "Test Stat Reward Ring", Slot = EquipmentSlot.LFinger, StrengthBonus = 4, MinLevel = 1 };
        EquipmentDatabase.RegisterDynamic(ring);
        c.EquipItem(ring, EquipmentSlot.LFinger, out _).Should().BeTrue();
        c.RecalculateStats();
        c.BaseStrength.Should().Be(13);
        c.Strength.Should().Be(13 + 4, "the grant and the ring both count");
        c.UnequipSlot(EquipmentSlot.LFinger);
        c.RecalculateStats();
        c.Strength.Should().Be(13, "taking the ring off keeps the grant");
    }

    [Fact]
    public void AGrant_SurvivesALevelUp()
    {
        var plain = Fresh("SrLevelPlain");
        var c = Fresh("SrLevel");
        c.GrantPermanentStat(StatKind.Strength, 3);
        c.GrantPermanentStat(StatKind.Defence, 2);
        LevelMasterLocation.ApplyClassStatIncreases(plain);
        LevelMasterLocation.ApplyClassStatIncreases(c);
        c.Strength.Should().Be(plain.Strength + 3, "the level-up adds to the grant");
        c.Defence.Should().Be(plain.Defence + 2);
    }

    [Fact]
    public void TheMultiStatOverload_AppliesEveryGrant()
    {
        var c = Fresh("SrMulti");
        var before = Snapshot(c);
        c.GrantPermanentStats((StatKind.Strength, 5), (StatKind.Stamina, 3));
        c.Strength.Should().Be(before[StatKind.Strength].d + 5);
        c.Stamina.Should().Be(before[StatKind.Stamina].d + 3);
        c.RecalculateStats();
        c.BaseStrength.Should().Be(15);
        c.BaseStamina.Should().Be(13);
        c.Dexterity.Should().Be(before[StatKind.Dexterity].d);
    }

    // ---------------- floors and cap ----------------

    [Fact]
    public void ANegativeGrant_StopsAtTheAttributeFloor()
    {
        foreach (var s in Attributes)
        {
            var c = Fresh("SrFloor");
            c.GrantPermanentStat(s, -1000);
            c.GetBaseStat(s).Should().Be(1, $"{s} cannot go below 1");
        }
    }

    [Fact]
    public void ANegativeGrant_StopsAtThePoolFloors()
    {
        var c = Fresh("SrFloorHp", CharacterClass.Magician);
        c.GrantPermanentStat(StatKind.MaxHP, -1000);
        c.BaseMaxHP.Should().Be(10, "MaxHP cannot go below 10");
        c.GrantPermanentStat(StatKind.MaxMana, -1000);
        c.BaseMaxMana.Should().Be(0, "MaxMana cannot go below 0");
        Character.PermanentStatFloor(StatKind.MaxHP).Should().Be(10);
        Character.PermanentStatFloor(StatKind.MaxMana).Should().Be(0);
        Character.PermanentStatFloor(StatKind.Charisma).Should().Be(1);
    }

    [Fact]
    public void ANegativeGrant_AboveTheFloor_Subtracts()
    {
        var c = Fresh("SrMinus");
        c.GrantPermanentStat(StatKind.Strength, -4);
        c.BaseStrength.Should().Be(6);
        c.Strength.Should().Be(6);
    }

    [Fact]
    public void TheCap_LimitsTheBaseField()
    {
        var c = Fresh("SrCap");
        c.BaseCharisma = 29;
        c.GrantPermanentStat(StatKind.Charisma, 5, cap: 30);
        c.BaseCharisma.Should().Be(30, "the cap of 30 applies to BaseCharisma");
        c.GrantPermanentStat(StatKind.Charisma, 1, cap: 30);
        c.BaseCharisma.Should().Be(30, "at the cap a grant adds nothing");
    }

    [Fact]
    public void TheCap_TestsBase_NotGearInflatedDerived_AndNeverLowersAboveIt()
    {
        var c = Fresh("SrCapGear");
        var amulet = new Equipment { Name = "Test Stat Reward Charm", Slot = EquipmentSlot.Neck, CharismaBonus = 50, MinLevel = 1 };
        EquipmentDatabase.RegisterDynamic(amulet);
        c.EquipItem(amulet, EquipmentSlot.Neck, out _).Should().BeTrue();
        c.RecalculateStats();
        c.Charisma.Should().BeGreaterThan(30);
        c.GrantPermanentStat(StatKind.Charisma, 1, cap: 30);
        c.BaseCharisma.Should().Be(11, "the cap tests Base, which is below 30");
        c.BaseCharisma = 45;
        c.GrantPermanentStat(StatKind.Charisma, 1, cap: 30);
        c.BaseCharisma.Should().Be(45, "a positive grant never lowers a Base already above the cap");
    }

    // ---------------- pools ----------------

    [Fact]
    public void RaisePool_AddsTheAmountToHp_ClampedToTheNewMax()
    {
        var c = Fresh("SrPool");
        c.HP = c.MaxHP - 50;
        long hp0 = c.HP, max0 = c.MaxHP;
        c.GrantPermanentStat(StatKind.MaxHP, 25, raisePool: true);
        c.BaseMaxHP.Should().Be(125);
        c.MaxHP.Should().BeGreaterThanOrEqualTo(max0 + 25);
        c.HP.Should().Be(hp0 + 25, "the pool rises by the amount granted");

        var full = Fresh("SrPoolFull");
        full.GrantPermanentStat(StatKind.MaxHP, 25, raisePool: true);
        full.HP.Should().Be(full.MaxHP, "clamped to the new max");

        var plain = Fresh("SrPoolNo");
        plain.HP = 40;
        plain.GrantPermanentStat(StatKind.MaxHP, 25);
        plain.HP.Should().Be(40, "without raisePool the current HP is unchanged");
    }

    [Fact]
    public void RaisePool_AddsTheAmountToMana()
    {
        var c = Fresh("SrMana", CharacterClass.Magician);
        c.Mana = 10;
        c.GrantPermanentStat(StatKind.MaxMana, 20, raisePool: true);
        c.BaseMaxMana.Should().Be(120);
        c.Mana.Should().Be(30);
    }

    [Fact]
    public void AConstitutionGrant_AlsoRaisesMaxHp_ThroughTheConBonus()
    {
        var c = Fresh("SrCon");
        long max0 = c.MaxHP;
        c.GrantPermanentStat(StatKind.Constitution, 10);
        c.BaseConstitution.Should().Be(20);
        c.BaseMaxHP.Should().Be(100, "Base MaxHP is not written");
        c.MaxHP.Should().Be(max0 - StatEffectsSystem.GetConstitutionHPBonus(10, c.Level) + StatEffectsSystem.GetConstitutionHPBonus(20, c.Level));
    }

    [Fact]
    public void AGrant_WorksOnAnNpc()
    {
        var npc = new NPC { Name1 = "SrNpc", Name2 = "SrNpc", Level = 5, BaseMaxHP = 80, BaseStrength = 10, BaseDefence = 5,
            BaseDexterity = 10, BaseConstitution = 10, BaseIntelligence = 10, BaseWisdom = 10, BaseCharisma = 10,
            BaseStamina = 10, BaseAgility = 10, Class = CharacterClass.Warrior, Race = CharacterRace.Human };
        npc.RecalculateStats();
        npc.GrantPermanentStat(StatKind.Strength, 2);
        npc.RecalculateStats();
        npc.BaseStrength.Should().Be(12);
        npc.Strength.Should().Be(12);
    }
}
