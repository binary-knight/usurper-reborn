using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;
using static UsurperReborn.Tests.PartySurvival133Harness;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.8: no stance pulls back from a fight. The rows that remain pin what the stances play and show: a
/// Balanced fight on a fixed seed is byte equal to the base, and the Cautious stance line is the 1.3.7 line
/// and fits 79 columns.
/// </summary>
[Collection("SharedGameSingletons")]
public class W140Withdraw
{
    private const BindingFlags NPI = BindingFlags.NonPublic | BindingFlags.Instance;

    private static string Attacks(int n = 40) => string.Concat(Enumerable.Repeat("A\n\n", n)) + string.Concat(Enumerable.Repeat("P\n", 20));

    // ---------------------------------------------------------------- Balanced is unchanged

    private static string Sha(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).Substring(0, 16);

    /// <summary>A Balanced fight on a fixed seed: the outcome, the rounds, every HP at the end, and the count and a
    /// hash of the random draws until the monster falls (HitRoll128Golden's recording random). The screen text is
    /// left out: other classes of the shared collection change settings that only change how lines look.</summary>
    private static async Task<string> BalancedSignature()
    {
        var hero = Hero();
        var a = Ally("Mira", 15, 100);   // Balanced
        var b = Ally("Kade", 100, 100);
        var mob = Mob("Rat", "Goblinoid", hp: 600, str: 30);
        var output = new MemoryStream();
        var term = new TerminalEmulator(new ScriptedStream(Attacks()), output);
        var e = new CombatEngine(term) { DivineFavorChanceOverride = () => 0f };
        var rng = new HitRoll128Golden.RecordingRandom(1404) { Over = () => !mob.IsAlive || !hero.IsAlive };
        typeof(CombatEngine).GetField("random", NPI)!.SetValue(e, rng);
        var task = e.PlayerVsMonsters(hero, new List<Monster> { mob }, new List<Character> { a, b }, offerMonkEncounter: false);
        (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(60)))).Should().BeSameAs(task);
        var res = await task;
        return $"{res.Outcome}|{res.CurrentRound}|{hero.HP}|{a.HP}|{b.HP}|{a.IsDowned}|{b.IsDowned}|{mob.HP}|{rng.Calls.Count}|{Sha(string.Join("\n", rng.Calls))}";
    }

    /// <summary>Written by this test on release-1.3.8 at 54f1c3cd, before the piece.</summary>
    private const string BalancedOnTheBase = "Victory|4|600|50|50|False|False|0|57|E27EDBC212B3BCEF";

    [Fact]
    public async Task ABalancedFight_IsByteEqualToTheBase_OnAFixedSeed()
    {
        // The base was written with no artifact collected. The story's collected artifacts outlive a test (the fallback
        // StoryProgressionSystem when no session is set): OldGodPayoutTests runs HandleBossDefeated on Maelketh, which
        // collects the Creator's Eye, and its crit bonus made the hero's first swing a precision strike in this fight.
        // So the fight runs on an empty set, and the set is put back after it.
        var artifacts = StoryProgressionSystem.Instance.CollectedArtifacts;
        var held = artifacts.ToList();
        artifacts.Clear();
        try
        {
            string first = await BalancedSignature();
            string second = await BalancedSignature();
            second.Should().Be(first, "the fight is deterministic on its seed");
            first.Should().Be(BalancedOnTheBase);
        }
        finally
        {
            artifacts.Clear();
            foreach (var a in held) artifacts.Add(a);
        }
    }

    // ---------------------------------------------------------------- texts

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("it")]
    public void TheCautiousLine_IsThe137Line_AndFits79(string lang)
    {
        // the party menu's Cautious row, as PromptStanceChoice and the Inn print it
        string cautious = $"  [3] {Loc.GetIn(lang, "party.stance.cautious")}  {Loc.GetIn(lang, "party.stance.cautious.desc")}";
        if (lang == "en" || lang == "hu")
            cautious.Length.Should().BeLessThanOrEqualTo(79, cautious);
        // 1.3.8 WX: no stance pulls back in 1.4, so the stance line is the 1.3.7 line again
        Loc.GetIn(lang, "party.stance.cautious.desc").Should().Be(CautiousDesc137[lang]);
    }

    /// <summary>party.stance.cautious.desc as 1.3.7 (54f1c3cd) shipped it.</summary>
    private static readonly Dictionary<string, string> CautiousDesc137 = new()
    {
        ["en"] = "Drinks and braces early, never taunts, draws fewer hits.",
        ["es"] = "Bebe y se cubre pronto, nunca provoca, recibe menos golpes.",
        ["fr"] = "Boit et se couvre tôt, ne provoque jamais, attire moins de coups.",
        ["hu"] = "Korán iszik és fedezi magát, sosem provokál, kevesebb ütést kap.",
        ["it"] = "Beve e si copre presto, non provoca mai, attira meno colpi.",
    };
}
