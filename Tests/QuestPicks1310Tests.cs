using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.10 quest picks (piece FB2). P8: bandits show up 3 times as often in the wilderness while the player holds a
/// bandit contract or a Bandit Raid runs. All in memory; every shared singleton touched is put back.
/// </summary>
[Collection("SharedGameSingletons")]
public class QuestPicks1310Tests : IDisposable
{
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags INP = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly FieldInfo DbField = typeof(QuestSystem).GetField("questDatabase", SNP)!;
    private static readonly FieldInfo EventsField = typeof(WorldEventSystem).GetField("_activeEvents", INP)!;
    private static List<Quest> Db => (List<Quest>)DbField.GetValue(null)!;

    private readonly List<Quest> _made = new();
    private readonly object _eventsWas;
    private readonly Dictionary<OldGodType, GodStatus> _godsWas;

    public QuestPicks1310Tests()
    {
        // No world event runs unless a test starts one; the list is put back in Dispose
        _eventsWas = EventsField.GetValue(WorldEventSystem.Instance)!;
        EventsField.SetValue(WorldEventSystem.Instance, new List<WorldEventSystem.WorldEvent>());
        _godsWas = StoryProgressionSystem.Instance.OldGodStates.ToDictionary(p => p.Key, p => p.Value.Status);
    }

    public void Dispose()
    {
        EventsField.SetValue(WorldEventSystem.Instance, _eventsWas);
        foreach (var (god, status) in _godsWas) StoryProgressionSystem.Instance.OldGodStates[god].Status = status;
        foreach (var q in _made) Db.Remove(q);
        Db.RemoveAll(q => (q.Occupier ?? "").StartsWith("Fbtwo", StringComparison.Ordinal)
            || (q.OfferedTo ?? "").StartsWith("Fbtwo", StringComparison.Ordinal));
    }

    private static Player Hero(string name, int level = 20) => new Player
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Human, Level = level,
        Class = CharacterClass.Warrior, Race = CharacterRace.Human,
        HP = 300, MaxHP = 300, BaseMaxHP = 300, Mental = 80,
    };

    /// <summary>Every Old God up to the given floor dealt with, every deeper one not.</summary>
    private static void GodsResolvedThrough(int floor)
    {
        var floors = new Dictionary<OldGodType, int>
        {
            [OldGodType.Maelketh] = 25, [OldGodType.Veloura] = 40, [OldGodType.Thorgrim] = 55, [OldGodType.Noctura] = 70,
            [OldGodType.Aurelion] = 85, [OldGodType.Terravok] = 95, [OldGodType.Manwe] = 100,
        };
        foreach (var (god, godFloor) in floors)
            if (StoryProgressionSystem.Instance.OldGodStates.TryGetValue(god, out var state))
                state.Status = godFloor <= floor ? GodStatus.Defeated : GodStatus.Unknown;
    }

    private Quest Track(Quest q) { _made.Add(q); return q; }

    /// <summary>A Random whose Next(max) walks 0, 1, 2 ... and wraps at max.</summary>
    private sealed class WalkingRandom : Random
    {
        private int _n;
        public int LastMax;
        public override int Next(int maxValue) { LastMax = maxValue; return _n++ % maxValue; }
    }

    private static Dictionary<string, int> Picks(WildernessRegion region, bool banditsAbout, out int total)
    {
        var rng = new WalkingRandom();
        WildernessData.PickMonsterName(region, banditsAbout, rng);   // learns the table's total weight
        total = rng.LastMax;
        rng = new WalkingRandom();
        var counts = region.MonsterNames.ToDictionary(n => n, _ => 0);
        for (int i = 0; i < total; i++) counts[WildernessData.PickMonsterName(region, banditsAbout, rng)]++;
        return counts;
    }

    private static WildernessRegion Region(string id) => WildernessData.Regions.Single(r => r.Id == id);

    private static void StartEvent(WorldEventSystem.EventType type) =>
        ((List<WorldEventSystem.WorldEvent>)EventsField.GetValue(WorldEventSystem.Instance)!)
            .Add(new WorldEventSystem.WorldEvent { Type = type, Title = "t", Description = "d", DaysRemaining = 3 });

    private Quest BanditContract(string occupier)
    {
        var q = new Quest { Title = "Fbtwo bandits", Initiator = "Crown Steward", Occupier = occupier, DaysToComplete = 14, Date = DateTime.Now, IsMercContract = true };
        q.Objectives.Add(new QuestObjective(QuestObjectiveType.KillSpecificMonster, "Slay bandits", 3, "Bandit", "Bandit"));
        QuestSystem.AddQuestToDatabase(q);
        return Track(q);
    }

    // ================= P8: bandits in the wilderness =================

    [Theory]
    [InlineData("forest", "Bandit Scout")]
    [InlineData("mountains", "Mountain Bandit")]
    public void P8_WhileBanditsAreAbout_TheBanditWeighsThree_AndEveryOtherNameOne(string region, string bandit)
    {
        var counts = Picks(Region(region), banditsAbout: true, out int total);
        total.Should().Be(Region(region).MonsterNames.Length + 2, "the bandit weighs 3 and the 7 others 1 each");
        counts[bandit].Should().Be(3);
        counts.Where(p => p.Key != bandit).Should().OnlyContain(p => p.Value == 1);
    }

    [Theory]
    [InlineData("forest")]
    [InlineData("mountains")]
    public void P8_WithoutBandits_EveryNameIsAsLikely(string region)
    {
        var counts = Picks(Region(region), banditsAbout: false, out int total);
        total.Should().Be(Region(region).MonsterNames.Length);
        counts.Values.Should().OnlyContain(v => v == 1);
    }

    [Fact]
    public void P8_HoldingABanditContract_BringsTheBanditsOut()
    {
        var hero = Hero("Fbtwo Holder");
        QuestSystem.BanditsAbout(hero).Should().BeFalse("no contract and no raid");
        BanditContract(hero.Name2);
        QuestSystem.BanditsAbout(hero).Should().BeTrue("the player holds the bandit contract");
    }

    [Fact]
    public void P8_AnotherPlayersContract_OrAnotherContract_DoesNot()
    {
        var hero = Hero("Fbtwo Other");
        BanditContract("Fbtwo Someone Else");
        var zombies = Track(new Quest { Title = "Fbtwo zombies", Initiator = "Faith Almoner", Occupier = hero.Name2, DaysToComplete = 7, Date = DateTime.Now });
        zombies.Objectives.Add(new QuestObjective(QuestObjectiveType.KillSpecificMonster, "Zombies", 3, "Zombie", "Zombie"));
        QuestSystem.AddQuestToDatabase(zombies);
        QuestSystem.BanditsAbout(hero).Should().BeFalse();
    }

    [Fact]
    public void P8_ABanditRaid_BringsTheBanditsOut_AndAnotherEventDoesNot()
    {
        var hero = Hero("Fbtwo Raid");
        StartEvent(WorldEventSystem.EventType.MerchantCaravan);
        QuestSystem.BanditsAbout(hero).Should().BeFalse("a caravan is not a raid");
        StartEvent(WorldEventSystem.EventType.BanditRaid);
        QuestSystem.BanditsAbout(hero).Should().BeTrue("a Bandit Raid runs");
    }

    [Fact]
    public void P8_TheExpeditionFight_PicksItsCreatureThroughTheWeightedTable()
    {
        string src = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "Locations", "WildernessLocation.cs"));
        src.Should().Contain("string monsterName = WildernessData.PickMonsterName(region, QuestSystem.BanditsAbout(currentPlayer), Random.Shared);");
        src.Should().NotContain("region.MonsterNames[Random.Shared.Next(region.MonsterNames.Length)]");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj"))) dir = dir.Parent;
        return dir!.FullName;
    }
}
