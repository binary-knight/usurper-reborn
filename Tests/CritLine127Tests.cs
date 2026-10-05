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
/// v1.2.7: a monster's critical hit line no longer passes an empty second argument, so it reads as a
/// whole sentence in every language (the damage shows on the lines below it).
/// </summary>
[Collection("SharedGameSingletons")]
public class CritLine127Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>A Random whose d20 comes up 20 and whose chance rolls all fail (no dodge, no ability).</summary>
    private sealed class NaturalTwentyRandom : Random
    {
        public override int Next() => int.MaxValue - 1;
        public override int Next(int maxValue) => Math.Max(0, maxValue - 1);
        public override int Next(int minValue, int maxValue) => maxValue - 1;
        public override double NextDouble() => 0.999;
        protected override double Sample() => 0.999;
    }

    private static string CriticalHitScreen(string lang)
    {
        var prev = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            var output = new MemoryStream();
            var term = new TerminalEmulator(new MemoryStream(), output);
            var engine = new CombatEngine(term);
            typeof(CombatEngine).GetField("random", F)!.SetValue(engine, new NaturalTwentyRandom());
            var player = new Character
            {
                Name1 = "tester", Name2 = "Tester", Class = CharacterClass.Warrior, Level = 5,
                HP = 100_000, MaxHP = 100_000, AI = CharacterAI.Human, CombatSpeed = CombatSpeed.Instant,
            };
            var kobold = new Monster { Name = "Kobold", Level = 5, HP = 50, MaxHP = 50, Strength = 12 };
            var result = new CombatResult { Player = player };
            typeof(CombatEngine).GetField("currentPlayer", F)?.SetValue(engine, player);
            ((Task)typeof(CombatEngine).GetMethod("ProcessMonsterAction", F)!
                .Invoke(engine, new object?[] { kobold, player, result, null })!).GetAwaiter().GetResult();
            term.StreamWriterInternal?.Flush();
            return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");
        }
        finally { GameConfig.Language = prev; }
    }

    [Theory]
    [InlineData("en", "{0} lands a CRITICAL HIT!")]
    [InlineData("es", "¡{0} te asesta un GOLPE CRÍTICO!")]
    [InlineData("fr", "{0} réussit un COUP CRITIQUE!")]
    [InlineData("it", "{0} mette a segno un COLPO CRITICO!")]
    [InlineData("hu", "KRITIKUS TALÁLAT! {0} eltalál!")]
    public void AMonsterCriticalHit_PrintsAWholeLine_InEachLanguage(string lang, string expectedWithName)
    {
        var screen = CriticalHitScreen(lang);
        var prev = GameConfig.Language;
        string name;
        try { GameConfig.Language = lang; name = MonsterNames.Display(new Monster { Name = "Kobold", Level = 5 }); }
        finally { GameConfig.Language = prev; }
        var expected = expectedWithName.Replace("{0}", name);

        // the critical row is the one printed right after the roll row ("[Name rolls: 20 + ...]")
        var rows = screen.Split('\n').Select(r => r.TrimEnd('\r').Trim()).ToList();
        int roll = rows.FindIndex(r => r.StartsWith("[") && r.Contains("20"));
        roll.Should().BeGreaterThanOrEqualTo(0, "the natural 20 roll row is shown; screen was:\n" + screen);
        var crit = rows[roll + 1];
        crit.Should().Be(expected, "screen was:\n" + screen);
        crit.Should().Contain(name);
        crit.Should().NotContain("{");
        crit.Should().NotContain("  ");
    }
}
