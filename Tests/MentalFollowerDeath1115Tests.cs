using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.15 Mental: a grouped human follower who dies in the leader's monster fight takes the same
/// loss as a dead leader (death plus the fight's strain and boss loss, no flee or near death), once
/// per fight, as one net change with one announcement on the follower's own terminal, applied at
/// the death site before GroupFollowerDeath.Mark hands the death to the follower's session.
/// </summary>
[Collection("SharedGameSingletons")]
public class MentalFollowerDeath1115Tests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        return dir!.FullName;
    }

    private static Character Hero(string name = "Hero", int mental = 100, long hp = 100) => new Character
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Human, Class = CharacterClass.Warrior, Race = CharacterRace.Human,
        Mental = mental, HP = hp, MaxHP = 100, MentalHintShown = true,
    };

    private static TerminalEmulator Term(MemoryStream output) => new TerminalEmulator(new LineStream(Array.Empty<string>()), output);

    private static string Text(MemoryStream output) =>
        Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");

    private static (Character follower, MemoryStream output) DeadFollower(string name = "Fallen", int mental = 100)
    {
        var output = new MemoryStream();
        var f = Hero(name, mental, hp: 0);
        f.RemoteTerminal = Term(output);   // a grouped human player
        return (f, output);
    }

    private static CombatResult Fight(Character leader, List<Character>? mates = null, params Monster[] monsters) => new CombatResult
    {
        Player = leader,
        Teammates = mates ?? new List<Character>(),
        Monsters = monsters.ToList(),
    };

    private static Monster Rat() => new Monster { Name = "Rat", Level = 1, HP = 10, MaxHP = 10 };
    private static Monster Warden() => new Monster { Name = "Warden", Level = 1, HP = 10, MaxHP = 10, IsBoss = true };

    private static Task Dispatch(CombatEngine engine, Character tm, CombatResult result) =>
        (Task)typeof(CombatEngine).GetMethod("HandleTeammateDeathDispatch", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(engine, new object[] { tm, "Doom Engine", result })!;

    [Fact]
    public void A_dead_follower_takes_death_strain_and_boss_as_one_change()
    {
        var (follower, output) = DeadFollower(mental: 80);
        var result = Fight(Hero(), null, Warden());
        CombatEngine.ApplyMentalFollowerDeath(result, follower, 100, false);
        // death 12; 100 x 12 = 1200 per mille: 1 point and 200 carried; boss 3
        follower.Mental.Should().Be(64);
        follower.MentalStrainRemainder.Should().Be(20_000);
        Regex.Matches(Text(output), "Mental Health worsens").Count.Should().Be(1);
    }

    [Fact]
    public void A_dead_follower_in_an_old_god_fight_takes_the_old_god_loss()
    {
        var (follower, _) = DeadFollower();
        CombatEngine.ApplyMentalFollowerDeath(Fight(Hero(), null, Warden()), follower, 0, true);
        follower.Mental.Should().Be(80);   // 12 + 8, the Old God replaces the boss loss
    }

    [Fact]
    public async Task A_dead_follower_takes_the_death_loss_once_whatever_paths_run()
    {
        var (follower, output) = DeadFollower(mental: 80);   // 80 to 68 crosses a band, so it announces
        var leader = Hero();
        var result = Fight(leader, new List<Character> { follower }, Rat());
        var engine = new CombatEngine(Term(new MemoryStream()));
        await Dispatch(engine, follower, result);
        result.Teammates.Should().NotContain(follower);
        follower.PendingGroupDeath.Should().Be("Doom Engine");
        follower.Mental.Should().Be(68);

        await Dispatch(engine, follower, result);
        CombatEngine.ApplyMentalFollowerDeath(result, follower, 0, false);
        follower.HP = 50;   // even if something put them back on their feet in the party
        result.Teammates.Add(follower);
        CombatEngine.ApplyMentalFightEnd(result, 0, true, false, null, 100);
        follower.Mental.Should().Be(68);
        Regex.Matches(Text(output), "Mental Health (worsens|improves)").Count.Should().Be(1);
    }

    [Fact]
    public void Pvp_arrest_and_exhibition_deaths_cost_a_follower_nothing()
    {
        var (f1, _) = DeadFollower("One");
        var pvp = Fight(Hero(), null, Warden());
        pvp.Opponent = Hero("Rival");
        CombatEngine.ApplyMentalFollowerDeath(pvp, f1, 100, true);
        f1.Mental.Should().Be(100);

        var (f2, _) = DeadFollower("Two");
        var arrest = Hero(); arrest.IsArrestCombat = true;
        CombatEngine.ApplyMentalFollowerDeath(Fight(arrest, null, Warden()), f2, 100, true);
        f2.Mental.Should().Be(100);

        var (f3, _) = DeadFollower("Three");
        var show = Hero(); show.IsExhibitionCombat = true;
        CombatEngine.ApplyMentalFollowerDeath(Fight(show, null, Warden()), f3, 100, true);
        f3.Mental.Should().Be(100);
        f3.MentalStrainRemainder.Should().Be(0);
    }

    [Fact]
    public void A_dead_leader_still_takes_only_strain_and_boss_at_fight_end()
    {
        var leader = Hero();
        var (follower, _) = DeadFollower();
        var result = Fight(leader, null, Warden());
        result.PlayerActuallyDied = true;   // the leader's 12 is taken in HandlePlayerDeath
        CombatEngine.ApplyMentalFollowerDeath(result, follower, 0, false);
        CombatEngine.ApplyMentalFightEnd(result, 0, true, false, null, 100);
        leader.Mental.Should().Be(97);
        follower.Mental.Should().Be(85);
    }

    [Fact]
    public void Dead_npc_teammates_and_companions_are_skipped()
    {
        var companion = Hero("Lyris", hp: 0);
        companion.IsCompanion = true;
        companion.CompanionId = CompanionId.Lyris;
        var npc = Hero("Hired Blade", hp: 0);
        npc.AI = CharacterAI.Computer;
        var result = Fight(Hero(), null, Warden());
        CombatEngine.ApplyMentalFollowerDeath(result, companion, 100, true);
        CombatEngine.ApplyMentalFollowerDeath(result, npc, 100, true);
        companion.Mental.Should().Be(100);
        companion.MentalStrainRemainder.Should().Be(0);
        npc.Mental.Should().Be(100);
        result.MentalDeadFollowers.Should().BeEmpty();
    }

    [Fact]
    public void A_living_follower_is_charged_as_before_when_another_follower_died()
    {
        var hero = Hero();
        var living = Hero("Ally", 90);
        living.RemoteTerminal = Term(new MemoryStream());
        var (dead, _) = DeadFollower();
        var result = Fight(hero, new List<Character> { living }, Rat());
        CombatEngine.ApplyMentalFollowerDeath(result, dead, 0, false);
        CombatEngine.ApplyMentalFightEnd(result, 0, true, false, null, 100);
        living.Mental.Should().Be(88);   // flee 2, no death
        dead.Mental.Should().Be(88);
    }

    [Fact]
    public void Every_grouped_death_site_applies_the_loss_before_handing_the_death_over()
    {
        var lines = File.ReadAllLines(Path.Combine(RepoRoot(), "Scripts", "Systems", "CombatEngine.cs"));
        var sites = Enumerable.Range(0, lines.Length).Where(i => lines[i].Contains("GroupFollowerDeath.Mark(")).ToList();
        // v1.1.15 piece 7: the fourth site is a Mental collapse death (ApplyFollowerCollapse); the
        // collapse is the Mental cost, so it sets the Broken aftermath there instead of the death loss
        sites.Should().HaveCount(4);
        foreach (var i in sites)
            (lines[i - 1].Contains("ApplyMentalFollowerDeath(") || lines[i - 1].Contains("MentalSystem.ApplyCollapseDeathAftermath(follower);"))
                .Should().BeTrue($"line {i + 1}");
        sites.Count(i => lines[i - 1].Contains("ApplyCollapseDeathAftermath(")).Should().Be(1);
    }
}
