using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake.BBS;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.8: the --npc-xp line of the door help states the range the parser accepts, the default it starts
/// from and the recruit pool rule of the world simulator. The help said "0.01-2.0 (default: 0.25)".
/// </summary>
public class NpcXpHelp128Tests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj"))) dir = dir.Parent;
        return dir!.FullName;
    }

    private static string Src(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray()));

    private static string Number(float v) => v.ToString("0.0#", CultureInfo.InvariantCulture);

    /// <summary>The help rows of --npc-xp as the sysop reads them: the option row and the rows under it.</summary>
    private static string[] HelpRows()
    {
        var rows = Regex.Matches(Src("Scripts", "BBS", "DoorMode.cs"), "Console\\.WriteLine\\(\"([^\"]*)\"\\);")
            .Select(m => m.Groups[1].Value).ToList();
        int at = rows.FindIndex(r => r.TrimStart().StartsWith("--npc-xp ", StringComparison.Ordinal));
        at.Should().BeGreaterThan(0, "the help has a --npc-xp row");
        return rows.Skip(at).TakeWhile((r, i) => i == 0 || r.StartsWith("       ", StringComparison.Ordinal)).ToArray();
    }

    /// <summary>The bounds of the parser and the starting value, read from the code.</summary>
    private static (float min, float max, float start) ParserFacts()
    {
        string src = Src("Scripts", "BBS", "DoorMode.cs");
        var parser = Regex.Match(src, "arg == \"--npc-xp\"[\\s\\S]{0,400}?mult >= ([0-9.]+)f && mult <= ([0-9.]+)f\\)\\s*_npcXpMultiplier = mult;");
        parser.Success.Should().BeTrue("the --npc-xp parser keeps a value inside its two bounds");
        var start = Regex.Match(src, "private static float _npcXpMultiplier = ([0-9.]+)f;");
        start.Success.Should().BeTrue();
        float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
        return (F(parser.Groups[1].Value), F(parser.Groups[2].Value), F(start.Groups[1].Value));
    }

    [Fact]
    public void TheHelpRow_StatesTheParsersRangeAndTheStartingValue()
    {
        var (min, max, start) = ParserFacts();
        string row = HelpRows()[0];

        row.Should().Contain($"{Number(min)} to {Number(max)}", "the range is the parser's");
        row.Should().Contain($"(default: {Number(start)})", "the default is the value the flag starts from");
        DoorMode.NpcXpMultiplier.Should().Be(start, "no test passes --npc-xp, so the running value is the default");
        row.Should().NotContain("0.25").And.NotContain("2.0");
    }

    [Fact]
    public void TheHelp_StatesTheRecruitPoolRule_AsTheSimulatorHasIt()
    {
        string sim = Src("Scripts", "Systems", "WorldSimulator.cs");
        var rule = Regex.Match(sim, @"GameConfig\.RecruitPoolEnabled && IsRecruitPoolMember\(npc\) \? ([0-9.]+)f : NpcXpMultiplier;");
        rule.Success.Should().BeTrue("a pool member earns at a fixed multiplier, every other NPC at the flag's");
        GameConfig.RecruitPoolEnabled.Should().BeTrue();
        sim.Should().Contain("string.IsNullOrEmpty(npc.Team)").And.Contain("npc.Level >= 1 && npc.Level <= GameConfig.RecruitPoolMaxLevel");

        string rows = string.Join(" ", HelpRows().Skip(1).Select(r => r.Trim()));
        string fixedRate = Number(float.Parse(rule.Groups[1].Value, CultureInfo.InvariantCulture));
        rows.Should().Contain("recruit pool")
            .And.Contain("no team")
            .And.Contain($"level 1 to {GameConfig.RecruitPoolMaxLevel}")
            .And.Contain($"x{fixedRate}");
    }

    [Fact]
    public void TheHelpRows_FitTheScreen_AndHaveNoDash()
    {
        var rows = HelpRows();
        rows.Length.Should().Be(2);
        foreach (var row in rows)
        {
            row.Length.Should().BeLessThanOrEqualTo(79, row);
            Regex.IsMatch(row, "[0-9]-[0-9]").Should().BeFalse("a range is written with \"to\": {0}", row);
        }
    }
}
