using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.5 (CB): Cycle Rewind heals exactly 30 percent of the caster's max HP, as its text says, and the
/// Cyclebreaker's passive evade rolls only for an action aimed at the living player, after the once-a-round
/// taunt and distraction ticks.
/// </summary>
[Collection("SharedGameSingletons")]
public class CbFix135Tests
{
    private const BindingFlags SF = BindingFlags.NonPublic | BindingFlags.Static;
    private const int Many = 2000;

    private static Character Caster(int level, long wisdom, long maxHp) => new Character
    {
        Name1 = "cbf135", Name2 = "Breaker", Class = CharacterClass.Cyclebreaker, Race = CharacterRace.Human,
        Level = level, Wisdom = wisdom, HP = 1, MaxHP = maxHp, Mana = 100_000, MaxMana = 100_000,
        AI = CharacterAI.Human, CombatSpeed = CombatSpeed.Instant,
    };

    private static SpellSystem.SpellResult Rewind(Character caster, Character? target, int seed, float profMult)
    {
        var r = new SpellSystem.SpellResult { Success = true };
        typeof(SpellSystem).GetMethod("ExecuteCyclebreakerSpell", SF)!
            .Invoke(null, new object?[] { caster, 4, target, null, r, new Random(seed), profMult });
        return r;
    }

    public static IEnumerable<object[]> Builds()
    {
        foreach (int level in new[] { 20, 68, 100 })
            foreach (long wis in new long[] { 10, 300, 600 })
                yield return new object[] { level, wis };
    }

    // T1
    [Theory]
    [MemberData(nameof(Builds))]
    public void CbFix135_CycleRewind_HealsExactlyThirtyPercentOfCasterMaxHp(int level, long wis)
    {
        foreach (long maxHp in new long[] { 1234, 9382, 31_007 })
            foreach (float prof in new[] { 1.0f, 1.25f, 1.5f })
                for (int seed = 1; seed <= 25; seed++)
                {
                    var caster = Caster(level, wis, maxHp);
                    var r = Rewind(caster, null, seed, prof);
                    r.Healing.Should().Be((int)(maxHp * 0.30), $"L{level} WIS {wis} MaxHP {maxHp} prof {prof} seed {seed}");
                    r.SpecialEffect.Should().Be("cure_disease", "the cure stays");
                    r.Message.Should().Contain(Loc.Get("combat.spell_cycle_rewind_cast", caster.Name2, r.Healing));

                    var ally = Caster(level, wis, maxHp * 3);
                    ally.Name1 = "cbf135ally";
                    var onAlly = Rewind(caster, ally, seed, prof);
                    onAlly.Healing.Should().Be((int)(maxHp * 0.30), "cast on an ally, the amount is 30 percent of the CASTER's max HP, not the ally's");
                }
    }

    // ---- the passive evade ----

    [Theory]
    [InlineData(5)]
    [InlineData(77)]
    [InlineData(901)]
    public async Task CbFix135_Evade_ActionAimedAtAlly_IsNeverEvaded(int seed)
    {
        var on = await CbFix135Support.Turns(seed, Many, CbFix135Support.Aim.Ally);
        on.Evades.Should().Be(0, "an action aimed at a companion is never evaded");
        on.Codes.Should().NotContain("P", "every action went to the ally");
    }

    [Theory]
    [InlineData(5)]
    [InlineData(77)]
    [InlineData(901)]
    public async Task CbFix135_Evade_ActionAimedAtPlayer_EvadedAboutEighteenPercent(int seed)
    {
        var on = await CbFix135Support.Turns(seed, Many, CbFix135Support.Aim.Player);
        double rate = (double)on.Evades / Many;
        rate.Should().BeInRange(0.14, 0.22, "the evade is 18 percent of the actions aimed at the player");
        on.Codes.Should().NotContain("A", "every action went to the player");
        on.Text.Should().Contain(CbFix135Support.EvadeLine(CbFix135Support.Brute()), "with the evade line");
    }

    [Theory]
    [InlineData(5)]
    [InlineData(77)]
    public async Task CbFix135_Evade_NaturalTargets_EvadesOnlyPlayerAimedActions(int seed)
    {
        var on = await CbFix135Support.Turns(seed, Many, CbFix135Support.Aim.Natural);
        int playerAimed = on.PlayerAimed;
        int allyAimed = on.Codes.Count(c => c == 'A');
        allyAimed.Should().BeGreaterThan(Many / 5, "the ally is chosen often");
        double rate = (double)on.Evades / playerAimed;
        rate.Should().BeInRange(0.13, 0.23, "18 percent of the actions aimed at the player (the evade or the attacks-you line)");
    }

    // T4
    [Theory]
    [InlineData(5)]
    [InlineData(77)]
    public async Task CbFix135_Evade_DeadPlayer_RedirectToAlly_IsNotEvaded(int seed)
    {
        var on = await CbFix135Support.Turns(seed, Many, CbFix135Support.Aim.Natural, playerDead: true);
        on.Evades.Should().Be(0, "a dead player's strike is redirected to the ally and not evaded");
    }

    // T5
    [Fact]
    public async Task CbFix135_Evade_SoloPlayer_EvadedAboutEighteenPercent()
    {
        var hero = CbFix135Support.Hero();
        var (engine, output) = CbFix135Support.Engine(hero, new Random(31));
        var m = CbFix135Support.Brute();
        for (int i = 0; i < Many; i++)
        {
            hero.HP = hero.MaxHP;
            m.StatusTickedThisRound = false;
            await (Task)CbFix135Support.Call(engine, "ProcessMonsterAction", m, hero, new CombatResult { Player = hero }, null)!;
        }
        string text = CbFix135Support.Text(engine, output);
        string line = CbFix135Support.EvadeLine(m);
        int n = 0, i2 = 0;
        while ((i2 = text.IndexOf(line, i2, StringComparison.Ordinal)) >= 0) { n++; i2 += line.Length; }
        ((double)n / Many).Should().BeInRange(0.14, 0.22, "no teammates: every action is aimed at the player");
    }

    // T6
    [Theory]
    [InlineData(5)]
    [InlineData(77)]
    [InlineData(901)]
    public async Task CbFix135_Evade_EvadedFirstActionOfARound_StillTicksTauntAndDistraction(int seed)
    {
        var hero = CbFix135Support.Hero();
        var ally = CbFix135Support.Ally();
        var (engine, output) = CbFix135Support.Engine(hero, new Random(seed));
        var m = CbFix135Support.Brute();
        string evade = CbFix135Support.EvadeLine(m);
        int checkedEvades = 0;
        for (int i = 0; i < 400 && checkedEvades < 5; i++)
        {
            hero.HP = hero.MaxHP;
            ally.HP = ally.MaxHP;
            m.StatusTickedThisRound = false;
            m.TauntedBy = hero.DisplayName;
            m.TauntRoundsLeft = 2;
            m.TauntStickChance = 100;
            m.Distracted = false;
            m.DistractedRounds = 2;
            m.DistractedRoundsPenalty = 7;
            m.DistractedPenalty = 0;
            var result = new CombatResult { Player = hero, Teammates = new List<Character> { ally } };
            int start = CbFix135Support.Text(engine, output).Length;
            await (Task)CbFix135Support.Call(engine, "ProcessMonsterAction", m, hero, result, null)!;
            string turn = CbFix135Support.Text(engine, output).Substring(start);
            if (!turn.Contains(evade, StringComparison.Ordinal)) continue;
            checkedEvades++;
            m.TauntRoundsLeft.Should().Be(1, "an evaded first action of the round still ticks the taunt down by one");
            m.DistractedRounds.Should().Be(1, "and still re-arms the distraction");
            m.Distracted.Should().BeTrue("the distraction is armed again");
        }
        checkedEvades.Should().BeGreaterThan(0, "the seed produced evaded first actions");
    }
}

internal static class CbFix135Support
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    internal static (CombatEngine engine, MemoryStream output) Engine(Character player, Random rng)
    {
        var output = new MemoryStream();
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), output));
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, rng);
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, player);
        return (engine, output);
    }

    internal static string Text(CombatEngine engine, MemoryStream output)
    {
        var term = (TerminalEmulator)typeof(CombatEngine).GetField("terminal", F)!.GetValue(engine)!;
        term.StreamWriterInternal?.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");
    }

    internal static object? Call(CombatEngine engine, string name, params object?[] args)
    {
        var m = typeof(CombatEngine).GetMethod(name, F)!;
        var all = m.GetParameters().Select((p, i) => i < args.Length ? args[i] : Type.Missing).ToArray();
        try { return m.Invoke(engine, all); }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    internal static Character Hero() => new Character
    {
        Name1 = "cbf135", Name2 = "Breaker", Class = CharacterClass.Cyclebreaker, Race = CharacterRace.Human, Level = 60,
        HP = 100_000, MaxHP = 100_000, Agility = 10, Dexterity = 10, AI = CharacterAI.Human, CombatSpeed = CombatSpeed.Instant,
    };

    internal static Character Ally() => new Character
    {
        Name1 = "cbf135a", Name2 = "Ally", Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 60,
        HP = 100_000, MaxHP = 100_000, Agility = 10, Dexterity = 10, AI = CharacterAI.Computer, CombatSpeed = CombatSpeed.Instant,
    };

    internal static Monster Brute() => new Monster
    {
        Name = "Ogre", Level = 30, HP = 1_000_000, MaxHP = 1_000_000, Strength = 2000, Defence = 20,
    };

    internal static string EvadeLine(Monster m) => Loc.Get("combat.cyclebreaker_evade", MonsterNames.Display(m));

    internal enum Aim { Natural, Player, Ally }

    internal sealed record TurnsResult(string Codes, long PlayerLost, long AllyLost, int Evades, string Text, int PlayerAimed);

    /// <summary>n monster turns, each a new round, against a Cyclebreaker with one ally at its side. Aim forces the
    /// target by a hard taunt (Player or Ally) or leaves the choice to SelectMonsterTarget (Natural). Each turn
    /// is coded E (the evade line), P (the player lost HP), A (the ally lost HP) or - (none of these).</summary>
    internal static async Task<TurnsResult> Turns(int seed, int n, Aim aim, bool playerDead = false)
    {
        var hero = Hero();
        var ally = Ally();
        var (engine, output) = Engine(hero, new Random(seed));
        var m = Brute();
        string evade = EvadeLine(m);
        var codes = new StringBuilder();
        long pLost = 0, aLost = 0;
        int evades = 0, playerAimed = 0;
        string attacksYou = Loc.Get("combat.monster_attacks", MonsterNames.Display(m));
        for (int i = 0; i < n; i++)
        {
            hero.HP = playerDead ? 0 : hero.MaxHP;
            ally.HP = ally.MaxHP;
            m.StatusTickedThisRound = false;
            if (aim != Aim.Natural)
            {
                m.TauntedBy = aim == Aim.Player ? hero.DisplayName : ally.DisplayName;
                m.TauntRoundsLeft = 99;
                m.TauntStickChance = 100;
            }
            var result = new CombatResult { Player = hero, Teammates = new List<Character> { ally } };
            int start = Text(engine, output).Length;
            long pb = hero.HP, ab = ally.HP;
            await (Task)Call(engine, "ProcessMonsterAction", m, hero, result, null)!;
            string turn = Text(engine, output).Substring(start);
            long pl = pb - hero.HP, al = ab - ally.HP;
            pLost += pl; aLost += al;
            bool ev = turn.Contains(evade, StringComparison.Ordinal);
            if (ev) evades++;
            if (ev || turn.Contains(attacksYou, StringComparison.Ordinal)) playerAimed++;
            codes.Append(ev ? 'E' : pl > 0 ? 'P' : al > 0 ? 'A' : '-');
        }
        return new TurnsResult(codes.ToString(), pLost, aLost, evades, Text(engine, output), playerAimed);
    }
}
