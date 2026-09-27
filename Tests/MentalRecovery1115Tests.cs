using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.15 Mental piece 5a: recovery sources, part A. The daily reset wired into
/// RunBasicDailyReset (the random maintenance gain removed), dungeon camp and Safe Haven rests
/// shared with grouped followers.
/// </summary>
[Collection("SharedGameSingletons")]
public class MentalRecovery1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }

    internal static string Src(params string[] parts) => File.ReadAllText(Path.Combine(new[] { RepoRoot(), "Scripts" }.Concat(parts).ToArray()));

    /// <summary>The brace-balanced body of the first method named method in src.</summary>
    internal static string Body(string src, string method)
    {
        var m = Regex.Match(src, @"(?:Task|void|int|bool)\s+" + Regex.Escape(method) + @"\s*\(");
        m.Success.Should().BeTrue($"{method} must be defined");
        int brace = src.IndexOf('{', m.Index); int depth = 0;
        for (int i = brace; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0) return src.Substring(brace, i - brace + 1);
        }
        throw new InvalidOperationException("unbalanced braces");
    }

    internal static Character Hero(string name = "Hero", int mental = 50, int addict = 0) => new Character
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Human, Class = CharacterClass.Warrior, Race = CharacterRace.Human,
        Mental = mental, Addict = addict, HP = 100, MaxHP = 100, MentalHintShown = true,
    };

    internal static TerminalEmulator Term(MemoryStream output) => new TerminalEmulator(new LineStream(Array.Empty<string>()), output);

    internal static string Text(MemoryStream output) =>
        Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");

    // Daily reset wiring

    [Fact]
    public void ApplyDailyReset_has_exactly_one_production_caller_in_RunBasicDailyReset()
    {
        var root = Path.Combine(RepoRoot(), "Scripts");
        var callers = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"MentalSystem\.ApplyDailyReset\(").Select(_ => Path.GetFileName(f)))
            .ToList();
        callers.Should().Equal(new[] { "DailySystemManager.cs" }, "one call, from the daily reset");
        Body(Src("Systems", "DailySystemManager.cs"), "RunBasicDailyReset").Should().Contain("MentalSystem.ApplyDailyReset(player)");
        Body(Src("Systems", "DailySystemManager.cs"), "RunCatchUpDailyReset").Should().NotContain("Mental");
    }

    [Fact]
    public void Maintenance_no_longer_raises_mental()
    {
        Src("Systems", "MaintenanceSystem.cs").Should().NotContain("Mental", "the random daily gain is replaced by ApplyDailyReset");
    }

    [Fact]
    public void The_daily_reset_restores_mental_and_the_once_a_day_flags()
    {
        var c = Hero(mental: 40);
        MentalSystem.MarkUsed(c, MentalDailySource.InnTable | MentalDailySource.HomeRest);
        MentalSystem.ApplyDailyReset(c).Should().Be(GameConfig.MentalDailyReset);
        c.Mental.Should().Be(40 + GameConfig.MentalDailyReset);
        c.MentalRecoveryUsedToday.Should().Be(MentalDailySource.None);
    }

    // Dungeon rest

    private static (DungeonLocation d, Character hero, MemoryStream output) Dungeon(int mental = 50, int addict = 0)
    {
        var (d, _, output) = DungeonDangerLabel1114Tests.Rig(new DungeonRoom { Id = "r1", Name = "Hall" });
        var hero = (Character)typeof(BaseLocation).GetField("currentPlayer", F)!.GetValue(d)!;
        hero.Mental = mental; hero.Addict = addict; hero.MentalHintShown = true;
        return (d, hero, output);
    }

    [Fact]
    public void A_camp_rest_gains_six_and_prints_the_amount()
    {
        var (d, hero, output) = Dungeon(50);
        d.ApplyRestMentalRecovery(GameConfig.MentalDungeonCampGain);
        GameConfig.MentalDungeonCampGain.Should().Be(6);
        hero.Mental.Should().Be(56);
        Text(output).Should().Contain(Loc.Get("mental.gain", 6));
    }

    [Fact]
    public void A_dungeon_rest_stops_at_the_cap_and_is_quiet_there()
    {
        var (d, hero, output) = Dungeon(60, addict: 80);   // cap 60
        d.ApplyRestMentalRecovery(GameConfig.MentalSafeHavenGain);
        hero.Mental.Should().Be(60);
        Text(output).Should().NotContain(Loc.Get("mental.gain", GameConfig.MentalSafeHavenGain));
    }

    [Fact]
    public void Dungeon_rest_is_shared_by_grouped_followers_only()
    {
        var (d, hero, _) = Dungeon(50);
        var followerOut = new MemoryStream();
        var follower = Hero("Ally", 30);
        follower.RemoteTerminal = Term(followerOut);
        var npc = Hero("Hired Blade", 30); npc.AI = CharacterAI.Computer;
        var deadFollower = Hero("Fallen", 30); deadFollower.RemoteTerminal = Term(new MemoryStream()); deadFollower.HP = 0;
        d.teammates.AddRange(new[] { follower, npc, deadFollower });
        d.ApplyRestMentalRecovery(GameConfig.MentalSafeHavenGain);
        hero.Mental.Should().Be(60);
        follower.Mental.Should().Be(40);
        Text(followerOut).Should().Contain(Loc.Get("mental.gain", 10));
        npc.Mental.Should().Be(30);
        deadFollower.Mental.Should().Be(30);
    }

    [Fact]
    public void The_camp_applies_the_camp_gain()
    {
        GameConfig.MentalDungeonCampGain.Should().Be(6);
        Body(Src("Locations", "DungeonLocation.cs"), "RestInRoom").Should().Contain("ApplyRestMentalRecovery(GameConfig.MentalDungeonCampGain);");
    }

    [Fact]
    public void The_safe_haven_applies_its_gain_only_inside_the_once_per_floor_branch()
    {
        GameConfig.MentalSafeHavenGain.Should().Be(10);
        var body = Body(Src("Locations", "DungeonLocation.cs"), "RestSpotEncounter");
        int guard = body.IndexOf("if (!hasCampedThisFloor)", StringComparison.Ordinal);
        int gain = body.IndexOf("ApplyRestMentalRecovery(GameConfig.MentalSafeHavenGain);", StringComparison.Ordinal);
        int sleep = body.IndexOf("RestAndAdvanceToMorning", StringComparison.Ordinal);
        int alreadyRested = body.IndexOf("dungeon.sanctuary_already_rested", StringComparison.Ordinal);
        guard.Should().BeGreaterThan(0);
        gain.Should().BeGreaterThan(guard);
        gain.Should().BeLessThan(sleep, "the gain lands before a night's sleep can run the daily reset");
        gain.Should().BeLessThan(alreadyRested);
        Regex.Matches(body, "ApplyRestMentalRecovery").Count.Should().Be(1);
    }
}
