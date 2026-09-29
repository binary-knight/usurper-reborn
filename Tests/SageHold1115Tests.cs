using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>v1.1.15: one shared monster hold budget; Freeze and Magician Sleep go through it.</summary>
[Collection("SharedGameSingletons")]
public class SageHold1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static (CombatEngine engine, MemoryStream output) Engine(Random? rng = null)
    {
        var output = new MemoryStream();
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), output));
        if (rng != null) typeof(CombatEngine).GetField("random", F)!.SetValue(engine, rng);
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, Sage());
        return (engine, output);
    }

    private static Character Sage() => new Character
    {
        Name1 = "Sage", Name2 = "Sage", Class = CharacterClass.Sage, Level = 30,
        HP = 100_000, MaxHP = 100_000, CombatSpeed = CombatSpeed.Instant,
    };

    private static Monster Ogre(bool boss = false, bool mini = false) =>
        new Monster { Name = "Ogre", Level = 30, HP = 5000, MaxHP = 5000, Strength = 50, Defence = 20, IsBoss = boss, IsMiniBoss = mini };

    private static bool Stun(CombatEngine engine, Monster m, int rounds) =>
        (bool)typeof(CombatEngine).GetMethod("TryStunMonster", F)!.Invoke(engine, new object[] { m, rounds })!;

    private static void SpellEffect(CombatEngine engine, Monster m, string effect, int duration) =>
        typeof(CombatEngine).GetMethod("HandleSpecialSpellEffectOnMonster", F)!
            .Invoke(engine, new object[] { m, effect, duration, Sage(), 0L, new CombatResult() });

    /// <summary>One monster turn, the way combat runs it: statuses tick, a held monster skips.</summary>
    private static void MonsterTurn(CombatEngine engine, Monster m)
    {
        m.StatusTickedThisRound = false;
        var result = new CombatResult { Player = Sage() };
        ((Task)typeof(CombatEngine).GetMethod("ProcessMonsterAction", F)!
            .Invoke(engine, new object?[] { m, Sage(), result, null })!).GetAwaiter().GetResult();
    }

    private static string Text(CombatEngine engine, MemoryStream output)
    {
        var term = (TerminalEmulator)typeof(CombatEngine).GetField("terminal", F)!.GetValue(engine)!;
        term.StreamWriterInternal?.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");
    }

    [Fact]
    public void AHeldMonster_CannotBeHeldAgain_NorRefreshed()
    {
        var (engine, output) = Engine(new LowRandom());
        var m = Ogre();
        engine.TryHoldMonster(m, CombatEngine.HoldKind.Freeze, 2).Should().BeTrue();
        m.FrozenDuration.Should().Be(2);
        engine.TryHoldMonster(m, CombatEngine.HoldKind.Freeze, 3).Should().BeFalse("a freeze does not refresh a freeze");
        engine.TryHoldMonster(m, CombatEngine.HoldKind.Sleep, 3).Should().BeFalse("nor does a sleep land on top");
        m.FrozenDuration.Should().Be(2);
        m.IsSleeping.Should().BeFalse();

        SpellEffect(engine, m, "freeze", 3);
        m.FrozenDuration.Should().Be(2, "the spell goes through the same rule");
        Text(engine, output).Should().Contain(Loc.Get("combat.spell_freeze_resist", "Ogre"), "the Sage is told the freeze did not take");
    }

    [Fact]
    public void AFrozenOrSleepingMonster_CannotBeStunnedOnTop()
    {
        var (engine, _) = Engine(new LowRandom());
        var frozen = Ogre();
        engine.TryHoldMonster(frozen, CombatEngine.HoldKind.Freeze, 2).Should().BeTrue();
        Stun(engine, frozen, 2).Should().BeFalse();
        frozen.IsStunned.Should().BeFalse();

        var asleep = Ogre();
        engine.TryHoldMonster(asleep, CombatEngine.HoldKind.Sleep, 2).Should().BeTrue();
        Stun(engine, asleep, 2).Should().BeFalse();
        asleep.IsStunned.Should().BeFalse();

        var stunned = Ogre();
        Stun(engine, stunned, 2).Should().BeTrue();
        engine.TryHoldMonster(stunned, CombatEngine.HoldKind.Freeze, 2).Should().BeFalse("and no freeze lands on a stun");
    }

    [Fact]
    public void WhenAFreezeEnds_ThePostHoldImmunityStarts()
    {
        var (engine, _) = Engine(new LowRandom());
        var m = Ogre();
        engine.TryHoldMonster(m, CombatEngine.HoldKind.Freeze, 1).Should().BeTrue();
        MonsterTurn(engine, m);
        m.IsFrozen.Should().BeFalse();
        m.StunImmunityRounds.Should().Be(GameConfig.StunImmunityRoundsAfterRecovery);
        engine.TryHoldMonster(m, CombatEngine.HoldKind.Freeze, 2).Should().BeFalse();
        Stun(engine, m, 1).Should().BeFalse();
    }

    [Fact]
    public void WhenASleepEnds_ThePostHoldImmunityStarts()
    {
        var (engine, _) = Engine(new LowRandom());
        var m = Ogre();
        engine.TryHoldMonster(m, CombatEngine.HoldKind.Sleep, 1).Should().BeTrue();
        MonsterTurn(engine, m);
        m.IsSleeping.Should().BeFalse();
        m.StunImmunityRounds.Should().Be(GameConfig.StunImmunityRoundsAfterRecovery);
        engine.TryHoldMonster(m, CombatEngine.HoldKind.Sleep, 2).Should().BeFalse();
        Stun(engine, m, 1).Should().BeFalse();
    }

    [Fact]
    public void Holds_DiminishFullHalfQuarter_ThenImmuneForTheFight()
    {
        var (engine, _) = Engine(new LowRandom());
        var m = Ogre();
        var lengths = new List<int>();
        for (int i = 0; i < 4; i++)
        {
            bool landed = engine.TryHoldMonster(m, i % 2 == 0 ? CombatEngine.HoldKind.Freeze : CombatEngine.HoldKind.Sleep, 3);
            lengths.Add(landed ? Math.Max(m.FrozenDuration, m.SleepDuration) : 0);
            // the hold runs out and the immunity passes
            m.IsFrozen = false; m.FrozenDuration = 0; m.IsSleeping = false; m.SleepDuration = 0; m.StunImmunityRounds = 0;
        }
        lengths.Should().Equal(new[] { 3, 2, 1, 0 });
        m.RoundsSinceLastStun = 100;   // no window forgives it
        engine.TryHoldMonster(m, CombatEngine.HoldKind.Freeze, 3).Should().BeFalse("immune for the rest of the fight");
    }

    [Fact]
    public void AStun_SpendsTheSameBudget()
    {
        var (engine, _) = Engine(new LowRandom());
        var m = Ogre();
        Stun(engine, m, 1).Should().BeTrue();
        m.IsStunned = false; m.StunDuration = 0;
        engine.TryHoldMonster(m, CombatEngine.HoldKind.Freeze, 3).Should().BeTrue();
        m.FrozenDuration.Should().Be(2, "the second hold of the fight is half length");
    }

    [Fact]
    public void AHold_IsCappedAtThreeRounds()
    {
        var (engine, _) = Engine(new LowRandom());
        var m = Ogre();
        SpellEffect(engine, m, "freeze", 9);
        m.IsFrozen.Should().BeTrue();
        m.FrozenDuration.Should().Be(3);

        var n = Ogre();
        SpellEffect(engine, n, "sleep", 7);
        n.IsSleeping.Should().BeTrue();
        n.SleepDuration.Should().Be(3);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Bosses_ResistHalfTheTime_AndAreHeldOneRound(bool boss, bool mini)
    {
        var (resisting, _) = Engine(new LowRandom());   // rolls 0: under the 50% resist
        var a = Ogre(boss, mini);
        resisting.TryHoldMonster(a, CombatEngine.HoldKind.Freeze, 3).Should().BeFalse();
        resisting.TryHoldMonster(a, CombatEngine.HoldKind.Sleep, 3).Should().BeFalse();
        a.IsFrozen.Should().BeFalse();
        a.IsSleeping.Should().BeFalse();

        var (landing, _) = Engine(new HighRandom());    // rolls 99: past the resist
        var b = Ogre(boss, mini);
        SpellEffect(landing, b, "freeze", 3);
        b.IsFrozen.Should().BeTrue();
        b.FrozenDuration.Should().Be(1);
    }

    [Fact]
    public void Bosses_ResistAboutHalf_WithASeededEngine()
    {
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        engine.SeedRandomForTests(1115);
        int landed = 0;
        for (int i = 0; i < 400; i++)
        {
            var god = Ogre(boss: true);
            if (engine.TryHoldMonster(god, CombatEngine.HoldKind.Freeze, 3)) { landed++; god.FrozenDuration.Should().Be(1); }
        }
        landed.Should().BeInRange(160, 240);
    }

    [Fact]
    public void TheFreezeSpell_AsksForOneToThreeRounds()
    {
        string src = File.ReadAllText(Path.Combine(Leftovers1114BTests.RepoRoot(), "Scripts", "Systems", "SpellSystem.cs"));
        var m = Regex.Match(src, @"case 4: // Freeze\b(.*?)break;", RegexOptions.Singleline);
        m.Success.Should().BeTrue();
        m.Groups[1].Value.Should().Contain("result.Duration = 1 + random.Next(2) + (caster.Level / 40);");
    }
}
