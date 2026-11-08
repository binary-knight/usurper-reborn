using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.3.12: an arrival (the two immigration phases and the recruit pool) comes as the sex the living town
/// has fewer of, so a town that leans one way moves back toward even as it turns over. Before, the first
/// arrival of an extinction top-up was always male. Every scenario runs in a copy of the game of its own
/// (IsolatedGame) with seeded random numbers and a world built in the test.
/// </summary>
[Collection("SharedGameSingletons")]
public class TownBalance1312Tests
{
    private static string Run(string scenario, string arg = "") => IsolatedGame.Run(scenario, arg);

    [Theory]
    [InlineData(10, 5, CharacterSex.Male, CharacterSex.Female)]
    [InlineData(10, 5, CharacterSex.Female, CharacterSex.Female)]
    [InlineData(5, 10, CharacterSex.Female, CharacterSex.Male)]
    [InlineData(5, 10, CharacterSex.Male, CharacterSex.Male)]
    [InlineData(7, 7, CharacterSex.Male, CharacterSex.Male)]
    [InlineData(7, 7, CharacterSex.Female, CharacterSex.Female)]
    [InlineData(0, 0, CharacterSex.Female, CharacterSex.Female)]
    public void FewerSex_PicksTheSexTheTownHasFewerOf_AndTheTieBreakOnlyOnATie(int male, int female, CharacterSex tie, CharacterSex expected)
    {
        WorldSimulator.FewerSex(male, female, tie).Should().Be(expected);
    }

    // The old extinction top-up gave a race one short a man every time; this is the test that fails on it.
    [Fact] public void ExtinctionTopUp_OneShort_InAnAllMaleTown_BringsAWoman() => Run(nameof(RecruitPoolRuns.TownBalanceOneShort), "male").Should().Be("F");
    [Fact] public void ExtinctionTopUp_OneShort_InAnAllFemaleTown_BringsAMan() => Run(nameof(RecruitPoolRuns.TownBalanceOneShort), "female").Should().Be("M");
    // A tie is a coin: over twenty seeds an even town's one arrival is a man at least once and a woman at least once.
    [Fact]
    public void ExtinctionTopUp_OneShort_InAnEvenTown_IsACoin_EitherSex()
    {
        var seen = Enumerable.Range(1, 20).Select(seed => Run(nameof(RecruitPoolRuns.TownBalanceOneShort), $"even,{seed}")).ToList();
        seen.Should().Contain("M").And.Contain("F");
    }

    [Fact]
    public void FewerSex_WithACoin_DrawsOnlyOnATie()
    {
        var rng = new CountingCoin(5);
        WorldSimulator.FewerSex(10, 5, rng).Should().Be(CharacterSex.Female);
        WorldSimulator.FewerSex(5, 10, rng).Should().Be(CharacterSex.Male);
        rng.Draws.Should().Be(0);
        var ties = Enumerable.Range(0, 40).Select(_ => WorldSimulator.FewerSex(7, 7, rng)).ToList();
        rng.Draws.Should().Be(40);
        ties.Should().Contain(CharacterSex.Male).And.Contain(CharacterSex.Female);
    }

    private sealed class CountingCoin : Random
    {
        public int Draws;
        public CountingCoin(int seed) : base(seed) { }
        public override int Next(int maxValue) { Draws++; return base.Next(maxValue); }
    }
    [Fact] public void ExtinctionTopUp_ThreeShort_InAnAllMaleTown_BringsOnlyWomen() => Run(nameof(RecruitPoolRuns.TownBalanceThreeShort)).Should().Be("FFFF");
    // 24 men and 23 women: a woman (to 24 and 24), a coin, then the sex the coin left fewer: FFM or FMF.
    [Fact] public void ExtinctionTopUp_CountsAgainAfterEachArrival() => Run(nameof(RecruitPoolRuns.TownBalanceCountsAgain)).Should().BeOneOf("FFM", "FMF");
    [Fact] public void DiversityArrival_InAnAllMaleTown_IsAWoman() => Run(nameof(RecruitPoolRuns.TownBalanceDiversity)).Should().Be("F");
    [Fact] public void RecruitPoolArrivals_InAnAllMaleTown_AreWomen() => Run(nameof(RecruitPoolRuns.TownBalancePool)).Should().Be("FFFFFF");

    /// <summary>
    /// The measurement, run only when USURPER_TOWN_BALANCE_OUT names a folder: "seed,ticks,start" with start
    /// classic (the classic roster) or skewed (the classic roster with men added to about 75 percent).
    /// </summary>
    [Fact]
    public void Measure_TheShareOfWomen_OverALongRun()
    {
        var dir = Environment.GetEnvironmentVariable("USURPER_TOWN_BALANCE_OUT");
        if (string.IsNullOrEmpty(dir)) return;
        var ticks = Environment.GetEnvironmentVariable("USURPER_TOWN_BALANCE_TICKS") ?? "20000";
        var sb = new StringBuilder();
        foreach (var start in new[] { "classic", "skewed" })
            foreach (var seed in new[] { 1, 2, 3 })
                sb.AppendLine(Run(nameof(RecruitPoolRuns.TownBalanceMeasure), $"{seed},{ticks},{start}"));
        File.WriteAllText(Path.Combine(dir, "measure.txt"), sb.ToString());
    }
}

internal static partial class RecruitPoolRuns
{
    private static string Sexes(IEnumerable<NPC> arrivals) =>
        string.Concat(arrivals.Select(n => n.Sex == CharacterSex.Female ? "F" : n.Sex == CharacterSex.Male ? "M" : "?"));

    /// <summary>A quiet world of five of each race, every NPC of the given sex ("male", "female", or "even").</summary>
    private static RecruitPoolWorld TownOf(string sexes, int perRace = 5, int seed = 31)
    {
        var w = Quiet(seed, perRace);
        int i = 0;
        foreach (var n in w.Npcs)
            n.Sex = sexes == "female" || (sexes == "even" && i++ % 2 == 1) ? CharacterSex.Female : CharacterSex.Male;
        return w;
    }

    private static List<NPC> ArrivalsOf(RecruitPoolWorld w, Action step)
    {
        var before = new HashSet<NPC>(w.Npcs, ReferenceEqualityComparer.Instance);
        step();
        return w.Npcs.Where(n => !before.Contains(n)).ToList();
    }

    /// <summary>
    /// One race one short of five. "even": the last NPC (a woman) is removed and a woman of another race added,
    /// so the town is 25 and 25 with one race at four.
    /// </summary>
    public static string TownBalanceOneShort(string arg)
    {
        var a = arg.Split(',');
        string sexes = a[0];
        SetSwitch(false);
        using var w = TownOf(sexes, seed: a.Length > 1 ? int.Parse(a[1]) : 31);
        var race = w.Npcs[^1].Race;
        w.Npcs.RemoveAt(w.Npcs.Count - 1);
        if (sexes == "even")
        {
            Make("Extra Woman", race == CharacterRace.Human ? CharacterRace.Elf : CharacterRace.Human, 40).Sex = CharacterSex.Female;
            w.Alive().Count(n => n.Sex == CharacterSex.Male).Should().Be(w.Alive().Count(n => n.Sex == CharacterSex.Female));
        }
        var arrivals = ArrivalsOf(w, () => w.Call("ProcessNPCImmigration"));
        arrivals.Should().ContainSingle().Which.Race.Should().Be(race);
        return Sexes(arrivals);
    }

    /// <summary>
    /// One race three short in an all male town of 47: three extinction arrivals, then one diversity arrival in
    /// the same step (the race was at two, under the threshold of three, when the step began).
    /// </summary>
    public static string TownBalanceThreeShort(string _)
    {
        SetSwitch(false);
        using var w = TownOf("male");
        var race = w.Npcs[^1].Race;
        foreach (var n in w.Npcs.Where(n => n.Race == race).Take(3).ToList()) w.Npcs.Remove(n);
        var arrivals = ArrivalsOf(w, () => w.Call("ProcessNPCImmigration"));
        arrivals.Should().HaveCount(4).And.OnlyContain(n => n.Race == race);
        return Sexes(arrivals);
    }

    /// <summary>
    /// A town of 24 men and 23 women with one race three short: a woman (24 and 24), a coin for the tie, then
    /// the sex the coin left fewer: the count is taken again after each arrival.
    /// </summary>
    public static string TownBalanceCountsAgain(string _)
    {
        SetSwitch(false);
        using var w = TownOf("male");
        var race = w.Npcs[^1].Race;
        foreach (var n in w.Npcs.Where(n => n.Race == race).Take(3).ToList()) w.Npcs.Remove(n);
        foreach (var n in w.Npcs.Where(n => n.Race != race).Take(23).ToList()) n.Sex = CharacterSex.Female;
        w.Alive().Count(n => n.Sex == CharacterSex.Male).Should().Be(24);
        w.Alive().Count(n => n.Sex == CharacterSex.Female).Should().Be(23);
        var arrivals = ArrivalsOf(w, () => w.Call("ProcessNPCImmigration"));
        // the fourth is the diversity arrival of the same step, at 25 and 25 decided by its coin
        arrivals.Should().HaveCount(4).And.OnlyContain(n => n.Race == race);
        arrivals.Take(3).Count(n => n.Sex == CharacterSex.Female).Should().Be(2);
        return Sexes(arrivals.Take(3));
    }

    /// <summary>Fifteen of each race, one race at six (141 living): above the extinction floor, below the diversity threshold (eight).</summary>
    public static string TownBalanceDiversity(string _)
    {
        SetSwitch(false);
        using var w = TownOf("male", perRace: 15);
        var race = w.Npcs[^1].Race;
        foreach (var n in w.Npcs.Where(n => n.Race == race).Take(9).ToList()) w.Npcs.Remove(n);
        var arrivals = ArrivalsOf(w, () => w.Call("ProcessNPCImmigration"));
        arrivals.Should().ContainSingle().Which.Race.Should().Be(race);
        return Sexes(arrivals);
    }

    /// <summary>The recruit pool in an all male town of sixty: its first six arrivals.</summary>
    public static string TownBalancePool(string _)
    {
        SetSwitch(true);
        using var w = TownOf("male", perRace: 6);
        var arrivals = new List<NPC>();
        for (int t = 0; t < 400 && arrivals.Count < 6; t++)
            arrivals.AddRange(ArrivalsOf(w, () => ImmigrationTick(w)));
        arrivals.Should().HaveCountGreaterThanOrEqualTo(6);
        return Sexes(arrivals.Take(6));
    }

    private static string Share(RecruitPoolWorld w)
    {
        var alive = w.Alive();
        int f = alive.Count(n => n.Sex == CharacterSex.Female), m = alive.Count(n => n.Sex == CharacterSex.Male);
        return $"alive={alive.Count} men={m} women={f} womenShare={(alive.Count == 0 ? 0 : 100.0 * f / alive.Count):F1}%";
    }

    /// <summary>
    /// "seed,ticks,start": the whole world simulation, a step every 30 seconds of world time; every tenth of the
    /// run the share of women is written. The model has no players and no daily reset.
    /// </summary>
    public static string TownBalanceMeasure(string arg)
    {
        var a = arg.Split(',');
        int seed = int.Parse(a[0]), ticks = int.Parse(a[1]);
        string start = a[2];
        SetSwitch(true);
        SilenceNews();
        using var w = new RecruitPoolWorld(seed);
        if (start == "skewed")
        {
            int made = 0;
            while (w.Alive().Count(n => n.Sex == CharacterSex.Male) * 4 < w.Alive().Count * 3)
            {
                int avg = (int)w.Alive().Average(n => n.Level);
                var npc = NPCSpawnSystem.Instance.GenerateImmigrantNPC((CharacterRace)(made++ % 10), CharacterSex.Male, avg);
                if (npc != null) NPCSpawnSystem.Instance.AddRestoredNPC(npc);
            }
        }
        var sb = new StringBuilder();
        sb.Append($"seed={seed} start={start} ticks={ticks} gameYears={ticks / 120.0 / 9.6:F1} | t=0 {Share(w)}");
        var tick30 = TimeSpan.FromSeconds(30);
        int otherNew = 0, arrivals = 0;
        for (int t = 1; t <= ticks; t++)
        {
            var before = new HashSet<NPC>(w.Npcs, ReferenceEqualityComparer.Instance);
            w.Elapse(tick30);
            w.Step();
            foreach (var n in w.Npcs.Where(n => !before.Contains(n)))
                if (n.ID != null && n.ID.StartsWith("npc_imm_")) arrivals++; else otherNew++;
            if (t % Math.Max(1, ticks / 10) == 0) sb.Append($" | t={t} {Share(w)}");
        }
        sb.Append($" | arrivals={arrivals} otherNew={otherNew}");
        return sb.ToString();
    }
}
