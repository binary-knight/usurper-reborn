using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.10: a monster's special ability on a companion skipped three protections that a basic
/// attack on the same companion, and the same ability on the player, apply: the multi-hit reduction,
/// Shield Wall Formation, and the lower boss cap in a fight's first rounds. A tank that taunted four
/// Gelatinous Cubes took four full Engulfs (player report, about 4,000 damage). These drive the real
/// MonsterAttacksCompanion with Engulf (a damage-multiplier special) on a companion with no defence,
/// so the numbers are exact.
/// </summary>
[Collection("SharedGameSingletons")]
public class CompanionSpecialMitigationTests
{
    private static readonly BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    // Level 400 puts the special's chance (30 + level/5) past 100, so Engulf fires every time.
    private static Monster Cube(bool boss = false, string? ability = "Engulf", long power = 400) => new Monster
    {
        Name = "Gelatinous Cube", Level = 400, HP = 100_000, MaxHP = 100_000,
        Strength = power, WeapPow = power, Punch = 0, IsActive = true, IsBoss = boss,
        SpecialAbilities = ability == null ? new List<string>() : new List<string> { ability },
    };

    private static Character Tank(long maxHp = 10_000_000) => new Character
    {
        Name1 = "tank", Name2 = "Aldric", Class = CharacterClass.Warrior, Level = 35,
        HP = maxHp, MaxHP = maxHp, Defence = 0, ArmPow = 0,
    };

    private static async Task<long> Hit(CombatEngine engine, Monster cube, Character tank, CombatResult result)
    {
        long before = tank.HP;
        await (Task)typeof(CombatEngine).GetMethod("MonsterAttacksCompanion", F)!
            .Invoke(engine, new object?[] { cube, tank, result, null })!;
        return before - tank.HP;
    }

    private static CombatEngine Engine() => new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));

    [Fact]
    public async Task ASecondSpecialInOneRound_IsReducedLikeASecondBasicHit()
    {
        var engine = Engine();
        var tank = Tank();
        var result = new CombatResult { CurrentRound = 10 };
        long first = await Hit(engine, Cube(), tank, result);
        // v1.1.14: Engulf's 45 percent stun would make the second cube skip a held target
        // (FallsBackToNormalAttack), so the tank is freed before the second Engulf.
        tank.ClearAllStatuses();
        long second = await Hit(engine, Cube(), tank, result);

        first.Should().Be((long)(800 * 1.5), "Engulf is 1.5 times attack against no defence");
        second.Should().Be((long)(first * 0.75), "the second hit in a round takes 25 percent off, as a basic attack does");
    }

    [Theory]
    [InlineData(false, false, false, false, 10)]   // plain
    [InlineData(false, true,  false, false, 10)]   // Shield Wall Formation up
    [InlineData(false, false, true,  false, 10)]   // the second hit of a round
    [InlineData(true,  false, false, true,  1)]    // a braced tank against a boss's opening round
    public async Task ASpecialAtOneTimesAttack_IsNeverMitigatedMoreThanABasicHit(bool boss, bool formation, bool secondHit, bool braced, int round)
    {
        // The braced case uses a boss strong enough (5,200 a hit) that the first-rounds cap binds:
        // halving before the cap leaves the cap, halving after it would leave half of it.
        long power = braced ? 2_000 : 400;
        // BleedingWound is 1.0 times attack. A basic hit adds a 0-9 roll to attack before mitigation,
        // so the bound is the basic hit less that roll.
        async Task<long> Taken(string? ability)
        {
            var engine = Engine();
            var tank = Tank(maxHp: 10_000);
            if (formation) { tank.TempDamageReductionPercent = 30; tank.TempDamageReductionDuration = 3; }
            if (braced) tank.IsDefending = true;
            var result = new CombatResult { CurrentRound = round };
            if (secondHit) await Hit(engine, Cube(boss, null, power), tank, result);
            return await Hit(engine, Cube(boss, ability, power), tank, result);
        }
        long basic = await Taken(null);
        long special = await Taken("BleedingWound");
        special.Should().BeGreaterThanOrEqualTo(basic - 9, "a special is never mitigated more than a basic hit");
    }

    [Fact]
    public async Task ABasicHitAfterASpecial_CountsAsTheSecondHitOfTheRound()
    {
        // The special now counts as a hit, so the basic attack that follows it in the same round takes
        // the 25 percent second-hit reduction; it used to land in full. A basic hit is attack (800)
        // plus a 0-9 roll: 600-607 after the reduction, 800-809 without it.
        // 20,000 HP: the basic hit's minimum-damage floor scales with MaxHP and must stay below 600.
        var engine = Engine();
        var tank = Tank(maxHp: 20_000);
        var result = new CombatResult { CurrentRound = 10 };
        await Hit(engine, Cube(), tank, result);
        long basic = await Hit(engine, Cube(ability: null), tank, result);
        basic.Should().BeInRange(600, 607);
    }

    [Fact]
    public async Task AGodsNamedAbilityAimedAtACompanion_IsItsNormalAttack_NotAPoke()
    {
        // v1.1.10: "War Cry" is an Old God ability written against the player, not a MonsterAbility.
        // Aimed at a companion it became a poke of about twice the god's level (800-1,199 here) that
        // used up the god's turn. Now the god makes its normal attack: 1.3 x 4,000 = 5,200 plus a 0-9 roll.
        var god = Cube(boss: true, ability: "War Cry", power: 2_000);
        long taken = await Hit(Engine(), god, Tank(maxHp: 100_000), new CombatResult { CurrentRound = 10 });
        taken.Should().BeInRange(5_200, 5_209);
    }

    [Fact]
    public async Task ShieldWallFormation_CutsASpecial()
    {
        var tank = Tank();
        tank.TempDamageReductionPercent = 30;
        tank.TempDamageReductionDuration = 3;
        long taken = await Hit(Engine(), Cube(), tank, new CombatResult { CurrentRound = 10 });
        taken.Should().Be(1200 - (long)(1200 * 0.30));
    }

    [Fact]
    public async Task ABossSpecialInTheFirstRounds_HitsTheFirstRoundsCap()
    {
        // A boss's Engulf is 1.3 x 800 x 1.5 = 1,560 against no defence. With 10,000 HP the first-rounds
        // cap (15 percent) is 1,500, above the boss minimum (level x 1.5 = 600), so the cap is what shows;
        // past the first rounds the cap is 85 percent and the full 1,560 lands.
        long early = await Hit(Engine(), Cube(boss: true), Tank(maxHp: 10_000), new CombatResult { CurrentRound = 1 });
        early.Should().Be((long)(10_000 * GameConfig.BossFirstRoundsDamageCapPercent), "the same first-rounds cap the player and a basic hit get");
        long later = await Hit(Engine(), Cube(boss: true), Tank(maxHp: 10_000), new CombatResult { CurrentRound = 10 });
        later.Should().Be(1_560);
    }
}
