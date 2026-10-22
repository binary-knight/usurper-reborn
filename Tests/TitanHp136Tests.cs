using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;
using G = UsurperReborn.Tests.HitRoll128Golden;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.6: Titan's Resolve (world boss loot) gives 5 percent more max HP, once however many equipped items carry
/// it, applied in RecalculateStats after the Fountain bonus and before the floor of 1. The old defence uses are
/// gone: the dungeon defence x1.05 and the world boss defence add of 5 percent of max HP.
/// </summary>
[Collection("SharedGameSingletons")]
public class TitanHp136Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static int Armor(EquipmentSlot slot, bool titan, string tag) => EquipmentDatabase.RegisterDynamic(new Equipment
    {
        Name = $"TitanHp136 {slot} {titan} {tag}", Slot = slot, ArmorClass = 0, MinLevel = 1, Rarity = EquipmentRarity.Epic,
        HasTitanResolve = titan,
    });

    /// <summary>The hero with a body and a head piece, each with or without Titan's Resolve, recalculated.</summary>
    private static Character Wear(Character c, bool body, bool head, string tag)
    {
        c.EquippedItems[EquipmentSlot.Body] = Armor(EquipmentSlot.Body, body, tag);
        c.EquippedItems[EquipmentSlot.Head] = Armor(EquipmentSlot.Head, head, tag);
        c.RecalculateStats();
        return c;
    }

    private static long MaxHP(bool body, bool head, long bonusMaxHP = 0) =>
        Wear(Fresh(bonusMaxHP), body, head, $"{body} {head} {bonusMaxHP}").MaxHP;

    private static Character Fresh(long bonusMaxHP = 0)
    {
        var c = G.Hero(5003, 100, 20);
        c.BonusMaxHP = bonusMaxHP;
        return c;
    }

    [Fact]
    public void TitanHp136_OneItem_MaxHPTimes105()
    {
        long plain = MaxHP(false, false);
        plain.Should().BeGreaterThan(100);
        MaxHP(true, false).Should().Be((long)(plain * Character.TitanResolveMaxHPMultiplier), "one Titan item gives x1.05 max HP");
        MaxHP(false, true).Should().Be((long)(plain * 1.05), "on any slot");
    }

    [Fact]
    public void TitanHp136_TwoItems_StillTimes105Once()
    {
        long plain = MaxHP(false, false);
        MaxHP(true, true).Should().Be((long)(plain * 1.05), "two Titan items still give x1.05 once, not x1.05 twice");
    }

    [Fact]
    public void TitanHp136_AfterBonusMaxHP_AndTruncated()
    {
        long plain = MaxHP(false, false);
        MaxHP(false, false, 1000).Should().Be(plain + 1000);
        MaxHP(true, false, 1000).Should().Be((long)((plain + 1000) * 1.05),
            "Titan's Resolve multiplies the max HP after the Fountain bonus, rounded down");
    }

    [Fact]
    public void TitanHp136_BeforeTheFloorOfOne()
    {
        var c = G.Hero(5003, 100, 20);
        c.BaseMaxHP = -100_000;
        Wear(c, true, false, "floor").MaxHP.Should().Be(1, "the floor of 1 comes after Titan's Resolve");
    }

    [Fact]
    public void TitanHp136_Companion_ThroughItsOwnRecalculateStats()
    {
        long CompanionMax(bool titan)
        {
            var companion = new Companion { Id = CompanionId.Mira, Name = "Tester", Level = 30, Constitution = 40 };
            companion.BaseStats.HP = 4001;
            companion.EquippedItems[EquipmentSlot.Body] = Armor(EquipmentSlot.Body, titan, "companion");
            return CompanionSystem.Instance.GetCompanionMaxHP(companion);
        }
        long plain = CompanionMax(false);
        CompanionMax(true).Should().Be((long)(plain * 1.05), "a companion's max HP goes through its own RecalculateStats");

        // a teammate is a Character with its own gear
        var mate = G.Mate("Ally One", 7001, 30);
        long matePlain = Wear(mate, false, false, "mate plain").MaxHP;
        Wear(G.Mate("Ally One", 7001, 30), true, false, "mate").MaxHP.Should().Be((long)(matePlain * 1.05));
    }

    [Fact]
    public void TitanHp136_EquipDoesNotHeal_UnequipClamps()
    {
        var c = Wear(Fresh(), false, false, "equip");
        long plain = c.MaxHP;
        c.HP = plain;
        var titan = EquipmentDatabase.GetById(Armor(EquipmentSlot.Body, true, "equip new"))!;
        c.EquipItem(titan, out _).Should().BeTrue();
        c.RecalculateStats();
        c.MaxHP.Should().Be((long)(plain * 1.05));
        c.HP.Should().Be(plain, "equipping raises the max, never the HP");

        c.HP = c.MaxHP;
        c.UnequipSlot(EquipmentSlot.Body).Should().NotBeNull();
        c.RecalculateStats();
        c.MaxHP.Should().Be(plain);
        c.HP.Should().Be(plain, "unequipping clamps HP to the new max");
    }

    [Fact]
    public void TitanHp136_BoonShare_EqualsAFullRecalculation()
    {
        foreach (bool titan in new[] { true, false })
        {
            var c = Fresh(37);
            c.CachedBoonEffects = new ActiveBoonEffects { MaxHPPercent = 0.10f };
            Wear(c, titan, false, $"boon {titan}");
            c.CachedBoonEffects.MaxHPPercent = 0.50f;
            c.RecalculateBoonShare();
            long share = c.MaxHP;
            c.RecalculateStats();
            share.Should().Be(c.MaxHP, $"the boon share update equals a full recalculation (Titan {titan})");
        }
    }

    // ---------------- the old defence uses are gone ----------------

    private sealed class FlatRandom : Random
    {
        public override double NextDouble() => 0.5;
        public override int Next(int maxValue) => maxValue / 2;
        public override int Next(int minValue, int maxValue) => minValue + (maxValue - minValue) / 2;
    }

    private static long BossHit(bool titan)
    {
        var p = Wear(G.Hero(50_000, 100, 40), titan, false, $"wb {titan}");
        var data = new WorldBossRuntimeData { ScaledStrength = 200_000 };
        return (long)typeof(WorldBossSystem).GetMethod("CalculateBossBasicDamage", F)!
            .Invoke(new WorldBossSystem(), new object[] { data, p, new FlatRandom(), 0 })!;
    }

    [Fact]
    public void TitanHp136_WorldBoss_NoMaxHPDefenceAdd()
    {
        BossHit(false).Should().BeLessThan(200_000).And.BeGreaterThan(40_000, "the defence curve, not the minimum, decides");
        BossHit(true).Should().Be(BossHit(false), "Titan's Resolve adds no defence against a world boss");
    }

    /// <summary>Ten basic hits of a strong monster on the real engine, seeded: the HP the hero loses.</summary>
    private static long DungeonHits(bool titan)
    {
        var hero = G.Hero(1_000_000, 100, 40);
        hero.BaseDefence = 2000;
        Wear(hero, titan, false, $"dungeon {titan}");
        var foe = G.Foe("Orc", 1_000_000, 20_000, level: 40);
        var rig = G.NewRig(hero, new G.RecordingRandom(136), null, foe);
        long lost = 0;
        for (int i = 0; i < 10; i++)
        {
            hero.HP = 900_000;
            ((Task)typeof(CombatEngine).GetMethod("ProcessMonsterAction", F)!
                .Invoke(rig.Engine, new object?[] { foe, hero, rig.Result, rig.Monsters })!).GetAwaiter().GetResult();
            lost += 900_000 - hero.HP;
        }
        return lost;
    }

    [Fact]
    public void TitanHp136_DungeonDefence_SameAsWithout()
    {
        long plain = DungeonHits(false);
        plain.Should().BeGreaterThan(0);
        DungeonHits(true).Should().Be(plain, "Titan's Resolve adds no dungeon defence");
    }
}
