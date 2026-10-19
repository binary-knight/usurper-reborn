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
/// 1.3.5: armour piercing takes its percent off the armour or Defence that a hit subtracts, on the area core, Power
/// Attack, Precise Strike, Ranged Attack and the ability, desperate, holy area and frenzy hits as well as the basic
/// swing, and the sum over the gear is capped at GameConfig.MaxArmorPiercingPercent.
/// </summary>
[Collection("SharedGameSingletons")]
public class PierceCoverage135Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int Seeds = 5;

    private static Equipment Piece(EquipmentSlot slot, int pierce, WeaponType type = WeaponType.Sword, string tag = "") => new Equipment
    {
        Name = $"Pierce Cover {slot} {pierce} {tag}", Slot = slot, WeaponType = type,
        Handedness = type == WeaponType.Bow ? WeaponHandedness.TwoHanded : WeaponHandedness.OneHanded,
        WeaponPower = slot == EquipmentSlot.MainHand ? 150 : 0, MinLevel = 1, Rarity = EquipmentRarity.Common, ArmorPiercing = pierce,
    };

    /// <summary>A hero whose gear carries the given piercing: the first value on the weapon, the second on a helm.</summary>
    private static Character Armed(int pierce, int helmPierce = 0, WeaponType type = WeaponType.Sword, bool companion = false, string name = "Hero")
    {
        var c = G.Hero(hp: 1_000_000, strength: 300, level: 60, dexterity: 60, name: name);
        if (companion) { c.IsCompanion = true; c.AI = CharacterAI.Computer; }
        c.EquippedItems[EquipmentSlot.MainHand] = EquipmentDatabase.RegisterDynamic(Piece(EquipmentSlot.MainHand, pierce, type));
        if (helmPierce > 0)
            c.EquippedItems[EquipmentSlot.Head] = EquipmentDatabase.RegisterDynamic(Piece(EquipmentSlot.Head, helmPierce));
        c.RecalculateStats();
        c.HP = c.MaxHP;
        c.CurrentCombatStamina = 500;
        return c;
    }

    private static ClassAbilityResult Use(string id, int damage = 1000)
    {
        var ability = ClassAbilitySystem.GetAbility(id);
        ability.Should().NotBeNull(id);
        return new ClassAbilityResult { Success = true, AbilityUsed = ability, Damage = damage, SpecialEffect = ability!.SpecialEffect };
    }

    private static void Run(CombatEngine engine, string method, params object?[] args) =>
        ((Task)typeof(CombatEngine).GetMethod(method, F)!.Invoke(engine, args)!).GetAwaiter().GetResult();

    /// <summary>The monster each path meets: Defence for the Defence rows, ArmPow for the armour rows.</summary>
    private static Monster Foe(string path) => path switch
    {
        "power" or "precise" or "ranged" => G.Dummy(defence: 15),   // Defence / 8 is 1, so no random part
        "ability" or "ability_companion" or "desperate" or "frenzy" => G.Dummy(defence: 1000),
        _ => G.Dummy(armour: 500 * (path == "holy" ? 2 : 1)),
    };

    /// <summary>One hit on the path; the HP the monster lost. The attacking side carries pct (weapon) plus helmPct (helm).</summary>
    private static long Hit(string path, int pct, int seed, int helmPct = 0)
    {
        bool hitRoll = GameConfig.PlayerHitRollEnabled;
        try
        {
            GameConfig.PlayerHitRollEnabled = false;
            return HitRolled(path, pct, seed, helmPct);
        }
        finally { GameConfig.PlayerHitRollEnabled = hitRoll; }
    }

    private static long HitRolled(string path, int pct, int seed, int helmPct)
    {
        bool bySide = path is "aoe_companion" or "ability_companion";
        var hero = bySide ? Armed(0) : Armed(pct, helmPct, path == "ranged" ? WeaponType.Bow : WeaponType.Sword);
        var mate = bySide ? Armed(pct, helmPct, companion: true, name: "Companion") : Armed(0, companion: true, name: "Companion");
        if (path == "desperate") hero.HP = hero.MaxHP / 10;   // the desperate rider needs HP under 40 percent
        var foe = Foe(path);
        var rig = G.NewRig(hero, new Random(seed), new List<Character> { mate }, foe);
        rig.Result.Monsters.Add(foe);
        long before = foe.HP;
        switch (path)
        {
            case "aoe": Run(rig.Engine, "ApplyAoEDamageCore", rig.Monsters, 10_000L, rig.Result, "cover", false, hero, null, false); break;
            case "aoe_spell": Run(rig.Engine, "ApplyAoEDamageCore", rig.Monsters, 10_000L, rig.Result, "cover", true, null, hero, false); break;
            case "aoe_companion": Run(rig.Engine, "ApplyAoEDamageCore", rig.Monsters, 10_000L, rig.Result, "cover", false, mate, null, false); break;
            case "power": Run(rig.Engine, "ExecutePowerAttackMultiMonster", hero, rig.Monsters, (int?)0, rig.Result); break;
            case "precise": Run(rig.Engine, "ExecutePreciseStrikeMultiMonster", hero, rig.Monsters, (int?)0, rig.Result); break;
            case "ranged": Run(rig.Engine, "ExecuteRangedAttackMultiMonster", hero, rig.Monsters, (int?)0, rig.Result); break;
            case "ability": Run(rig.Engine, "ApplyAbilityEffectsMultiMonster", hero, foe, rig.Monsters, Use("power_strike"), rig.Result); break;
            case "ability_companion": Run(rig.Engine, "ApplyAbilityEffectsMultiMonster", mate, foe, rig.Monsters, Use("power_strike"), rig.Result); break;
            case "desperate": Run(rig.Engine, "ApplyAbilityEffectsMultiMonster", hero, foe, rig.Monsters, Use("desperate_strike"), rig.Result); break;
            case "holy": Run(rig.Engine, "ApplyAbilityEffectsMultiMonster", hero, foe, rig.Monsters, Use("judgment_day"), rig.Result); break;
            case "frenzy": Run(rig.Engine, "ApplyAbilityEffectsMultiMonster", hero, foe, rig.Monsters, Use("frenzy"), rig.Result); break;
            case "swing": G.Act(rig, G.A(CombatActionType.Attack)); break;
            default: throw new ArgumentException(path);
        }
        return before - Math.Max(0, foe.HP);
    }

    // T1: the cut a 20 percent pierce takes off what each path subtracts
    [Theory]
    [InlineData("aoe", 100)]                 // ArmPow 500
    [InlineData("aoe_spell", 100)]           // the area spell: attacker null, the caster's gear
    [InlineData("aoe_companion", 100)]       // a companion's area ability, from its own gear
    [InlineData("power", 3)]                 // Defence 15
    [InlineData("precise", 2)]               // Defence 15 x 0.75 = 11
    [InlineData("ranged", 3)]                // Defence 15
    [InlineData("ability", 100)]             // Defence 1000 / 2
    [InlineData("ability_companion", 100)]   // the same, a companion's gear
    [InlineData("desperate", 150)]           // the base hit's Defence / 2 (100) and the rider's Defence / 4 (50)
    [InlineData("holy", 100)]                // ArmPow 1000 / 2
    public void PierceCoverage135_T1_CutsTheSubtractedAmountBy20Percent(string path, long cut)
    {
        for (int seed = 0; seed < Seeds; seed++)
        {
            long plain = Hit(path, 0, seed);
            plain.Should().BeGreaterThan(0, path);
            (Hit(path, 20, seed) - plain).Should().Be(cut, $"{path} seed {seed}: 20 percent of the armour or Defence it subtracts");
        }
    }

    // T2
    [Fact]
    public void PierceCoverage135_T2_Frenzy_CutsTheBaseHitAndEveryExtraHit()
    {
        // the base hit: Defence 1000 / 2, cut 100; each of 2 to 4 extra hits: Defence / 4 = 250, cut 50, then x 0.8 to 1.2
        for (int seed = 0; seed < Seeds; seed++)
        {
            long gain = Hit("frenzy", 20, seed) - Hit("frenzy", 0, seed);
            gain.Should().BeInRange(100 + 2 * 40 - 4, 100 + 4 * 60 + 4, $"seed {seed}: the base cut and every extra hit's cut");
        }
    }

    // T3
    [Fact]
    public void PierceCoverage135_T3_TheSwing_RemovesItsPercentOfArmourOnce()
    {
        for (int seed = 0; seed < Seeds; seed++)
        {
            long plain = Hit("swing", 0, seed);
            plain.Should().BeGreaterThan(0);
            (Hit("swing", 20, seed) - plain).Should().Be(100, "20 percent of ArmPow 500, as the swing took it before");
        }
    }

    // T4
    [Theory]
    [InlineData("aoe")]
    [InlineData("aoe_spell")]
    [InlineData("aoe_companion")]
    [InlineData("power")]
    [InlineData("precise")]
    [InlineData("ranged")]
    [InlineData("ability")]
    [InlineData("ability_companion")]
    [InlineData("desperate")]
    [InlineData("holy")]
    [InlineData("frenzy")]
    [InlineData("swing")]
    public void PierceCoverage135_T4_NoPiercingGear_IsAsToday(string path)
    {
        var bare = Armed(0);
        bare.GetEquipmentArmorPiercing().Should().Be(0);
        foreach (long amount in new long[] { -5, 0, 1, 7, 500, 100_000 })
            CombatEngine.ApplyArmorPiercing(amount, bare).Should().Be(amount, "no piercing leaves the amount");
        CombatEngine.ApplyArmorPiercing(500, null).Should().Be(500, "no attacker leaves the amount");
        for (int seed = 0; seed < Seeds; seed++)
            Hit(path, 0, seed).Should().Be(Hit(path, 0, seed), $"{path}: a run with no piercing repeats exactly");
        Hit("aoe", 0, 0).Should().Be(10_000 - 500, "an area hit loses the whole armour, as before");
    }

    // T5: the cap
    [Fact]
    public void PierceCoverage135_T5_TwoItems30And30_AddUpTo50_AndTheSwingRemovesHalfTheArmour()
    {
        GameConfig.MaxArmorPiercingPercent.Should().Be(50);
        Armed(30, 30).GetEquipmentArmorPiercing().Should().Be(50, "30 and 30 is 60, capped at 50");
        for (int seed = 0; seed < Seeds; seed++)
            (Hit("swing", 30, seed, helmPct: 30) - Hit("swing", 0, seed)).Should().Be(250, "exactly half of ArmPow 500");
    }

    // T6
    [Fact]
    public void PierceCoverage135_T6_TwoItems30And15_Give45_UnderTheCap()
    {
        Armed(30, 15).GetEquipmentArmorPiercing().Should().Be(45);
        Armed(50).GetEquipmentArmorPiercing().Should().Be(50);
        Armed(10).GetEquipmentArmorPiercing().Should().Be(10);
        for (int seed = 0; seed < Seeds; seed++)
            (Hit("swing", 30, seed, helmPct: 15) - Hit("swing", 0, seed)).Should().Be(225, "45 percent of ArmPow 500");
    }
}
