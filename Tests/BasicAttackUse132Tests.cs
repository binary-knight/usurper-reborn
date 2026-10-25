using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;
using G = UsurperReborn.Tests.HitRoll128Golden;
using P = TrainingSystem.ProficiencyLevel;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.2: the player's basic attack proficiency improves by use on the live combat path. One roll of
/// TrainingSystem.TryImproveFromUse per [A] Attack action, after the swing loop of the Attack case of
/// ProcessPlayerActionMultiMonster, with the engine's random, capped at Good; past Good stays the Level
/// Master's. The line is combat.proficiency_up. Power Attack, PvP and the dead single-monster path do not count.
/// </summary>
[Collection("SharedGameSingletons")]
public class BasicAttackUse132Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const string Skill = "basic_attack";

    // ---------------------------------------------------------------- harness

    /// <summary>A seeded random whose Next(100) is fixed (0: every improvement roll succeeds; 99: none does)
    /// and counted; every other draw is the seeded one.</summary>
    private sealed class Forced100 : Random
    {
        private readonly int _value;
        public int Hundreds;
        public Forced100(int value, int seed = 132) : base(seed) { _value = value; }
        public override int Next(int maxValue)
        {
            if (maxValue == 100) { Hundreds++; return _value; }
            return base.Next(maxValue);
        }
    }

    private static T InEnglish<T>(Func<T> body, bool roll = true)
    {
        string lang = GameConfig.Language;
        bool saved = GameConfig.PlayerHitRollEnabled;
        try { GameConfig.Language = "en"; GameConfig.PlayerHitRollEnabled = roll; return body(); }
        finally { GameConfig.Language = lang; GameConfig.PlayerHitRollEnabled = saved; }
    }

    private static G.Rig Attacks(Character hero, Random random, int times, Monster? foe = null, Action<G.Rig>? setup = null)
    {
        var rig = G.NewRig(hero, random, null, foe ?? G.Dummy());
        setup?.Invoke(rig);
        for (int i = 0; i < times; i++) G.Act(rig, G.A(CombatActionType.Attack));
        return rig;
    }

    private static string Plain(string transcript) => Regex.Replace(transcript, "\u001b\\[[0-9;?]*[A-Za-z]", "").Replace("\r", "");
    private static List<string> Rows(string transcript) => Plain(transcript).Split('\n').Select(r => r.TrimEnd()).Where(r => r.Length > 0).ToList();

    private static int Progress(Character c) => TrainingSystem.GetTrainingProgress(c, Skill);
    private static P Tier(Character c) => TrainingSystem.GetSkillProficiency(c, Skill);

    private static Character At(P tier, int progress = 0, Character? c = null)
    {
        c ??= G.Hero();
        TrainingSystem.SetSkillProficiency(c, Skill, tier);
        c.SkillTrainingProgress[Skill] = progress;
        return c;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj"))) dir = dir.Parent;
        dir.Should().NotBeNull("the test must run inside the repository");
        return dir!.FullName;
    }

    private static string Engine() => File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "Systems", "CombatEngine.cs"));

    /// <summary>The brace-balanced body of the first method named method.</summary>
    private static string Method(string src, string method)
    {
        var m = Regex.Match(src, @"\b(?:Task(?:<[^>\n]*>)?|void|int|bool)\s+" + Regex.Escape(method) + @"\s*\(");
        m.Success.Should().BeTrue($"{method} must be defined");
        int brace = src.IndexOf('{', m.Index); int depth = 0;
        for (int i = brace; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0) return src.Substring(brace, i - brace + 1);
        }
        throw new InvalidOperationException("unbalanced braces");
    }

    private static string AttackCase()
    {
        var body = Method(Engine(), "ProcessPlayerActionMultiMonster");
        int start = body.IndexOf("case CombatActionType.Attack:", StringComparison.Ordinal);
        int end = body.IndexOf("case CombatActionType.Defend:", start, StringComparison.Ordinal);
        start.Should().BeGreaterThan(0);
        end.Should().BeGreaterThan(start);
        return body.Substring(start, end - start);
    }

    private static int Count(string text, string what) => Regex.Matches(text, Regex.Escape(what)).Count;

    private static readonly Regex BasicImprove = new(@"TryImproveFromUse\(\s*\w+\s*,\s*""basic_attack""");

    private static string ProficiencyLine(P tier) => Loc.Get("combat.proficiency_up", TrainingSystem.GetProficiencyName(tier));

    // ---------------------------------------------------------------- T1: the live swing improves it

    [Fact]
    public void T1_OneAttackAction_FinishesTheTier_AndShowsTheLine()
    {
        InEnglish(() =>
        {
            var hero = At(P.Average, 2);
            var rig = Attacks(hero, new Forced100(0), 1);
            Tier(hero).Should().Be(P.Good, "the third step of Average makes Good");
            Progress(hero).Should().Be(0);
            var rows = Rows(rig.Shown());
            rows.Should().Contain("Your combat experience grows! Basic Attack is now Good!");
            rows.Should().OnlyContain(r => r.Length <= 79);
            return 0;
        });
    }

    [Fact]
    public void T1_ARollThatFails_LeavesItAsItWas_AndShowsNoLine()
    {
        InEnglish(() =>
        {
            var hero = At(P.Average, 2);
            var rig = Attacks(hero, new Forced100(99), 20);
            Tier(hero).Should().Be(P.Average);
            Progress(hero).Should().Be(2);
            Rows(rig.Shown()).Should().NotContain(r => r.Contains("combat experience grows"));
            return 0;
        });
    }

    // ---------------------------------------------------------------- T2: each action counts once

    [Fact]
    public void T2_AnActionOfManySwings_CountsOnce()
    {
        InEnglish(() =>
        {
            var hero = At(P.Average, 0, G.Hero(level: 30, cls: CharacterClass.Warrior, main: 40, off: 35));
            hero.IsDualWielding.Should().BeTrue();
            var rig = Attacks(hero, new Forced100(0), 1);
            Rows(rig.Shown()).Count(r => r.StartsWith("You attack ") || r.StartsWith("Off-hand strike at ")).Should().BeGreaterThan(1, "the action makes several swings");
            Tier(hero).Should().Be(P.Average);
            Progress(hero).Should().Be(1, "one action is one roll, however many swings it makes");
            return 0;
        });
    }

    // ---------------------------------------------------------------- T3: the cap is Good

    [Fact]
    public void T3_AtGood_UseStops_AndDrawsNoImprovementRoll()
    {
        InEnglish(() =>
        {
            var good = At(P.Good, 0);
            Attacks(good, new Forced100(0), 200);
            Tier(good).Should().Be(P.Good, "past Good is the Level Master's");
            Progress(good).Should().Be(0);

            var skilled = At(P.Skilled, 0);
            Attacks(skilled, new Forced100(0), 200);
            Tier(skilled).Should().Be(P.Skilled, "a trained tier is not lowered");
            Progress(skilled).Should().Be(0);

            // the same 50 actions with no roll succeeding: an Average hero draws one roll an action more than a Good one
            var below = new Forced100(99);
            Attacks(At(P.Average, 0), below, 50);
            var atCap = new Forced100(99);
            Attacks(At(P.Good, 0), atCap, 50);
            (below.Hundreds - atCap.Hundreds).Should().Be(50, "under the cap one improvement roll an action, at the cap none");
            return 0;
        });
    }

    [Fact]
    public void T3_FromAverage_UseReachesGood_AndNoFurther()
    {
        InEnglish(() =>
        {
            var hero = At(P.Average, 0);
            var rig = Attacks(hero, new Forced100(0), 200);
            Tier(hero).Should().Be(P.Good);
            Progress(hero).Should().Be(0);
            Rows(rig.Shown()).Count(r => r.Contains("combat experience grows")).Should().Be(1, "one tier, one line");
            return 0;
        });
    }

    // ---------------------------------------------------------------- T4: a miss and an evaded swing count

    [Fact]
    public void T4_AnActionWhoseEverySwingMisses_StillCounts()
    {
        InEnglish(() =>
        {
            var hero = At(P.Average, 2);
            var foe = G.Dummy(60, 600);   // ten levels above, out of reach: the roll needs 8
            var random = new MissRandom();
            var rig = Attacks(hero, random, 1, foe);
            foe.HP.Should().Be(foe.MaxHP, "no swing landed");
            Rows(rig.Shown()).Should().Contain(r => r.StartsWith("You miss ") || r.StartsWith("CRITICAL MISS!"));
            Tier(hero).Should().Be(P.Good);
            return 0;
        });
    }

    [Fact]
    public void T4_AnActionWhoseEverySwingIsEvaded_StillCounts()
    {
        InEnglish(() =>
        {
            var hero = At(P.Average, 2);
            var foe = G.Dummy();
            foe.EvasionRounds = 1000;
            foe.EvasionMissChance = 100;
            Attacks(hero, new Forced100(0), 1, foe);
            foe.HP.Should().Be(foe.MaxHP, "every swing was evaded");
            Tier(hero).Should().Be(P.Good);
            return 0;
        });
    }

    /// <summary>The twenty-sided die shows 1 (every swing misses), Next(100) gives 0.</summary>
    private sealed class MissRandom : Random
    {
        public MissRandom() : base(132) { }
        public override int Next(int maxValue) => maxValue == 100 ? 0 : base.Next(maxValue);
        public override int Next(int minValue, int maxValue) => minValue == 1 && maxValue == 21 ? 1 : base.Next(minValue, maxValue);
    }

    // ---------------------------------------------------------------- T5: the dead path stays dead

    [Fact]
    public void T5_TheDeadSingleMonsterPath_StillThrows()
    {
        var hero = G.Hero();
        var rig = G.NewRig(hero, new Random(132), null, G.Dummy());
        var task = (Task)typeof(CombatEngine).GetMethod("ProcessPlayerAction", F)!
            .Invoke(rig.Engine, new object?[] { G.A(CombatActionType.Attack), hero, rig.Monsters[0], rig.Result })!;
        Action run = () => task.GetAwaiter().GetResult();
        run.Should().Throw<InvalidOperationException>().WithMessage("Dead single-monster combat path invoked: ProcessPlayerAction*");
        Tier(hero).Should().Be(P.Average);
        Progress(hero).Should().Be(0);
    }

    [Fact]
    public void T5_TheAttackCase_HoldsExactlyOneCall_AfterTheSwingLoop_AndTheOldCallsAreUnchanged()
    {
        var attack = AttackCase();
        BasicImprove.Matches(attack).Count.Should().Be(1, "one improvement roll an Attack action");
        int loop = attack.IndexOf("for (int s = 0; s < swings", StringComparison.Ordinal);
        int call = BasicImprove.Match(attack).Index;
        loop.Should().BeGreaterThan(0);
        call.Should().BeGreaterThan(attack.IndexOf("ApplyPlayerSwingOnHitEffects(", loop, StringComparison.Ordinal), "the call is after the swing loop");
        attack.Should().Contain("TrainingSystem.TryImproveFromUse(player, \"basic_attack\", random, basicProfCap)");
        attack.Should().Contain("(int)TrainingSystem.ProficiencyLevel.Good");
        attack.Should().Contain("Loc.Get(\"combat.proficiency_up\"");

        var src = Engine();
        BasicImprove.Matches(src).Count.Should().Be(4, "the dead path's two, the teammate call and the new one");
        Count(Method(src, "ExecuteSingleAttack"), "TrainingSystem.TryImproveFromUse(attacker, \"basic_attack\", random)").Should().Be(2);
        Count(Method(src, "ProcessTeammateActionMultiMonster"), "TrainingSystem.TryImproveFromUse(teammate, \"basic_attack\", random, profCap)").Should().Be(1);
        Method(src, "ProcessPlayerAction").Should().Contain("DeadCombatPathGuard();");
        // ExecuteSingleAttack is reached only from the dead ExecuteAttack and ExecutePowerAttack
        Count(src, "await ExecuteSingleAttack(").Should().Be(2);
        Method(src, "ExecuteAttack").Should().Contain("await ExecuteSingleAttack(");
        Method(src, "ExecutePowerAttack").Should().Contain("await ExecuteSingleAttack(");
        Count(src, "await ExecuteAttack(").Should().Be(1);
        Method(src, "ProcessPlayerAction").Should().Contain("await ExecuteAttack(");
    }

    // ---------------------------------------------------------------- T6: teammates unchanged

    [Fact]
    public void T6_ATeammate_StillImprovesSilently_PastGood_WithItsOwnCap()
    {
        InEnglish(() =>
        {
            var hero = G.Hero();
            var mate = G.Mate("Ally One");
            At(P.Good, 3, mate);
            var rig = G.NewRig(hero, new Forced100(0), new List<Character> { mate }, G.Dummy());
            mate.CurrentCombatStamina = 0;
            G.TeammateTurn(rig, mate);
            Tier(mate).Should().Be(P.Skilled, "a teammate's cap is its own, not Good");
            Rows(rig.Shown()).Should().NotContain(r => r.Contains("combat experience grows"), "a teammate improves silently");
            Tier(hero).Should().Be(P.Average, "the leader did not act");
            return 0;
        });
    }

    // ---------------------------------------------------------------- T7: training unchanged

    [Fact]
    public void T7_UseSpendsNoTrainingPoints_AndShares_TheProgressTheLevelMasterSells()
    {
        TrainingSystem.GetTrainingCostPerPoint(P.Average).Should().Be(2);
        TrainingSystem.GetTrainingCostPerPoint(P.Good).Should().Be(2);
        TrainingSystem.GetPointsForNextLevel(P.Average).Should().Be(3);
        TrainingSystem.GetPointsForNextLevel(P.Good).Should().Be(4);
        TrainingSystem.GetCombatImprovementChance(P.Average).Should().Be(7);
        TrainingSystem.GetCombatImprovementChance(P.Good).Should().Be(5);

        InEnglish(() =>
        {
            var hero = At(P.Average, 0);
            hero.TrainingPoints = 17;
            Attacks(hero, new Forced100(0), 200);
            hero.TrainingPoints.Should().Be(17, "use never spends or gives training points");
            Tier(hero).Should().Be(P.Good);
            TrainingSystem.CalculateTotalPointsInvested(hero, Skill).Should().Be(6, "a respec refunds a Good reached by use as one bought");

            var partial = At(P.Average, 0);
            partial.TrainingPoints = 5;
            var once = G.NewRig(partial, new Forced100(0), null, G.Dummy());
            G.Act(once, G.A(CombatActionType.Attack));
            Progress(partial).Should().Be(1, "a step by use is progress at the Level Master");
            TrainingSystem.GetTotalCostToNextLevel(Tier(partial), Progress(partial)).Should().Be((3 - 1) * 2);
            TrainingSystem.CalculateTotalPointsInvested(partial, Skill).Should().Be(2);
            TrainingSystem.ResetSkillProficiency(partial, Skill).Should().Be(2);
            Progress(partial).Should().Be(0);
            Tier(partial).Should().Be(P.Average);
            partial.TrainingPoints.Should().Be(5);
            return 0;
        });
    }

    // ---------------------------------------------------------------- T8: the line fits

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void T8_TheLine_Fits79Columns_AtEveryTier(string lang)
    {
        string saved = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            foreach (P tier in Enum.GetValues(typeof(P)))
            {
                string line = ProficiencyLine(tier);
                line.Should().NotContain("combat.proficiency_up");
                line.Length.Should().BeLessThanOrEqualTo(79, "{0} at {1}", lang, tier);
            }
        }
        finally { GameConfig.Language = saved; }
    }

    // ---------------------------------------------------------------- T9: only the Attack action counts

    [Fact]
    public void T9_PowerAttack_DoesNotTrainTheBasicAttack()
    {
        InEnglish(() =>
        {
            var hero = At(P.Average, 2);
            var rig = G.NewRig(hero, new Forced100(0), null, G.Dummy());
            for (int i = 0; i < 50; i++) G.Act(rig, G.A(CombatActionType.PowerAttack));
            rig.Monsters[0].HP.Should().BeLessThan(rig.Monsters[0].MaxHP, "the Power Attacks landed");
            Tier(hero).Should().Be(P.Average);
            Progress(hero).Should().Be(2);
            return 0;
        });
    }

    [Fact]
    public void T9_APvPAttack_DoesNotTrainTheBasicAttack()
    {
        InEnglish(() =>
        {
            var hero = At(P.Average, 2);
            var other = G.Hero(hp: 1_000_000, name: "Other");
            var rig = G.NewRig(hero, new Forced100(0), null, G.Dummy());
            var pvp = typeof(CombatEngine).GetMethod("ExecutePvPAttack", F)!;
            for (int i = 0; i < 50; i++)
                ((Task)pvp.Invoke(rig.Engine, new object?[] { hero, other, rig.Result })!).GetAwaiter().GetResult();
            other.HP.Should().BeLessThan(other.MaxHP, "the PvP swings landed");
            Tier(hero).Should().Be(P.Average);
            Progress(hero).Should().Be(2);
            return 0;
        });
    }
}
