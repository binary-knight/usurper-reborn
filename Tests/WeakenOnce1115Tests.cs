using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>v1.1.15: weaken on a monster is the timed -30% attack / -20% defence only, counted once.</summary>
[Collection("SharedGameSingletons")]
public class WeakenOnce1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static Character Caster() => new Character
    {
        Name1 = "Caster", Name2 = "Caster", Class = CharacterClass.Wavecaller, Level = 30,
        HP = 100_000, MaxHP = 100_000, CombatSpeed = CombatSpeed.Instant,
    };

    private static CombatEngine Engine()
    {
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, Caster());
        return engine;
    }

    private static Monster Brute() => new Monster { Name = "Brute", Level = 30, HP = 5000, MaxHP = 5000, Strength = 200, Defence = 100 };

    private static void SpellWeaken(CombatEngine engine, Monster m, int duration) =>
        typeof(CombatEngine).GetMethod("HandleSpecialSpellEffectOnMonster", F)!
            .Invoke(engine, new object[] { m, "weaken", duration, Caster(), 0L, new CombatResult() });

    private static void OldSpellWeaken(CombatEngine engine, Monster m, int duration) =>
        typeof(CombatEngine).GetMethod("HandleSpecialSpellEffect", F)!
            .Invoke(engine, new object?[] { Caster(), m, "weaken", duration });

    private static void AbilityWeaken(CombatEngine engine, Monster m, int duration)
    {
        var ability = new ClassAbilityResult
        {
            Success = true, SpecialEffect = "weaken", Duration = duration,
            AbilityUsed = ClassAbilitySystem.GetAbility("cutting_words"),   // the Bard's weaken
        };
        ((Task)typeof(CombatEngine).GetMethod("ApplyAbilityEffectsMultiMonster", F)!
            .Invoke(engine, new object[] { Caster(), m, new List<Monster> { m }, ability, new CombatResult() })!).GetAwaiter().GetResult();
    }

    private static void CheckOnce(System.Action<CombatEngine, Monster, int> weaken)
    {
        var engine = Engine();
        var m = Brute();
        long attack = m.GetAttackPower(), defence = m.GetDefensePower();

        weaken(engine, m, 4);
        m.Strength.Should().Be(200, "the base stat is not cut");
        m.Defence.Should().Be(100);
        m.WeakenRounds.Should().Be(4);
        m.GetAttackPower().Should().Be((long)(attack * 0.70f), "the timed cut applies while it lasts");
        m.GetDefensePower().Should().Be((long)(defence * 0.80f));

        weaken(engine, m, 4);   // a recast refreshes the timer; it does not stack
        m.Strength.Should().Be(200);
        m.GetAttackPower().Should().Be((long)(attack * 0.70f));

        m.WeakenRounds = 0;     // the timer runs out
        m.GetAttackPower().Should().Be(attack);
        m.GetDefensePower().Should().Be(defence);
    }

    [Fact]
    public void SpellWeaken_CountsOnce_AndEnds() => CheckOnce(SpellWeaken);

    [Fact]
    public void OlderSpellHandlerWeaken_CountsOnce_AndEnds() => CheckOnce(OldSpellWeaken);

    [Fact]
    public void AbilityWeaken_CountsOnce_AndEnds() => CheckOnce(AbilityWeaken);

    [Fact]
    public void WeakenRunsOut_OnTheMonstersTurns()
    {
        var engine = Engine();
        var m = Brute();
        m.IsFrozen = true; m.FrozenDuration = 5;   // held, so its turns do not swing at anyone
        long attack = m.GetAttackPower();
        SpellWeaken(engine, m, 2);
        var process = typeof(CombatEngine).GetMethod("ProcessMonsterAction", F)!;
        for (int i = 0; i < 2; i++)
        {
            m.StatusTickedThisRound = false;
            ((Task)process.Invoke(engine, new object?[] { m, Caster(), new CombatResult { Player = Caster() }, null })!).GetAwaiter().GetResult();
        }
        m.WeakenRounds.Should().Be(0);
        m.Strength.Should().Be(200);
        m.GetAttackPower().Should().Be(attack);
    }
}
