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
/// 1.3.2: Main Street's status screen shows the Wisdom of completed grief whenever it is above 0, with or
/// without a live grief. With a live grief the rows are as before. The stat itself is unchanged.
/// State is in memory only: the grief singleton is filled through Deserialize and reset in Dispose.
/// </summary>
[Collection("SharedGameSingletons")]
public class GriefLine132Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private static GriefSystem Grief => GriefSystem.Instance;

    public GriefLine132Tests() { Grief.Reset(); OceanPhilosophySystem.Instance.Reset(); }
    public void Dispose() { Grief.Reset(); OceanPhilosophySystem.Instance.Reset(); }

    private static Character Caster(string name) => new()
    {
        Name1 = name, Name2 = name, Class = CharacterClass.Magician, Race = CharacterRace.Human, Level = 20,
        AI = CharacterAI.Human, BaseStrength = 10, BaseDexterity = 10, BaseConstitution = 10,
        BaseIntelligence = 20, BaseWisdom = 20, BaseCharisma = 10, BaseMaxHP = 400, BaseMaxMana = 200,
        BaseDefence = 5, BaseStamina = 10, BaseAgility = 10, HP = 400, Mana = 200, Mental = 80
    };

    private static T AsPlayer<T>(Character hero, Func<T> body)
    {
        var engine = GameEngine.Instance;
        var old = engine.CurrentPlayer;
        engine.CurrentPlayer = hero;
        try { return body(); }
        finally { engine.CurrentPlayer = old; }
    }

    private static GriefState Done(CompanionId id) => new()
    {
        CompanionId = id, CompanionName = id.ToString(), DeathType = DeathType.Combat,
        CurrentStage = GriefStage.Acceptance, IsComplete = true
    };

    private static GriefState Running(CompanionId id) => new()
    {
        CompanionId = id, CompanionName = id.ToString(), DeathType = DeathType.Combat,
        CurrentStage = GriefStage.Denial, StageStartDay = 1, GriefStartDay = 1
    };

    private static void Hold(IEnumerable<GriefState> companions) =>
        Grief.Deserialize(new GriefSystemData
        {
            ActiveGrief = companions.ToList(),
            ActiveNpcGrief = new List<GriefState>()
        });

    private static string MainStreetJourney(Character hero)
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(Enumerable.Repeat("", 10)), output);
        var street = new MainStreetLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(street, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(street, hero);
        var task = (Task)typeof(MainStreetLocation).GetMethod("ShowStoryProgress", F)!.Invoke(street, null)!;
        Task.WhenAny(task, Task.Delay(10000)).GetAwaiter().GetResult();
        term.StreamWriterInternal?.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "").Replace("\r", "");
    }

    // 1
    [Fact]
    public void NoLiveGrief_OneCompletedCompanionGrief_ShowsTheWisdomLine()
    {
        var hero = Caster("Gl1");
        AsPlayer(hero, () =>
        {
            Hold(new[] { Done(CompanionId.Lyris) });
            hero.RecalculateStats();
            Grief.GetActiveGriefDetails().Should().BeEmpty("no grief is live");
            string text = MainStreetJourney(hero);
            text.Should().Contain(Loc.Get("main_street.grief_wisdom", 5));
            text.Should().Contain(Loc.Get("main_street.grief_combat_effects", Loc.Get("main_street.grief_wisdom", 5)).Trim());
            return 0;
        });
    }

    // 2
    [Fact]
    public void NoLiveGrief_NoCompletedGrief_ShowsNoGriefWisdomLine()
    {
        var hero = Caster("Gl2");
        AsPlayer(hero, () =>
        {
            Hold(Array.Empty<GriefState>());
            hero.RecalculateStats();
            string text = MainStreetJourney(hero);
            text.Should().NotContain(Loc.Get("main_street.grief_wisdom", 5));
            text.Should().NotContain(Loc.Get("main_street.grief_wisdom", 0));
            text.Should().NotContain(Loc.Get("main_street.grief_combat_effects", "").Trim());
            text.Should().Contain(Loc.Get("main_street.grief_label"));
            return 0;
        });
    }

    // 3
    [Fact]
    public void ALiveGriefAndACompletedOne_ShowTheRowsAsBefore()
    {
        var hero = Caster("Gl3");
        AsPlayer(hero, () =>
        {
            Hold(new[] { Done(CompanionId.Lyris), Running(CompanionId.Aldric) });
            hero.RecalculateStats();
            string text = MainStreetJourney(hero);
            var lines = text.Split('\n');
            lines.Should().Contain(l => l.TrimEnd() == $"  {Loc.Get("grief.health_grieving", 1)}");
            lines.Should().Contain(l => l.TrimEnd().StartsWith("    Aldric: "));
            string effects = Loc.Get("main_street.grief_combat_effects", Loc.Get("main_street.grief_wisdom", 5));
            lines.Count(l => l.Contains(Loc.Get("main_street.grief_wisdom", 5))).Should().Be(1, "one Wisdom line");
            lines.Should().Contain(l => l.TrimEnd().EndsWith(Loc.Get("main_street.grief_wisdom", 5)));
            lines.Should().NotContain(l => l.Contains(Loc.Get("main_street.grief_label")) && l.Contains("Aldric"));
            return 0;
        });
    }

    // 4
    [Fact]
    public void TheCompletedGriefWisdom_StillReachesTheStat()
    {
        var hero = Caster("Gl4");
        AsPlayer(hero, () =>
        {
            hero.RecalculateStats();
            long wis = hero.Wisdom;
            Hold(new[] { Done(CompanionId.Lyris), Done(CompanionId.Mira) });
            hero.RecalculateStats();
            hero.Wisdom.Should().Be(wis + 10);
            hero.BaseWisdom.Should().Be(20);
            Grief.GetCurrentEffects().PermanentWisdomBonus.Should().Be(10);
            return 0;
        });
    }
}
