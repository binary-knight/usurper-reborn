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
/// bandit contract or a Bandit Raid runs. P9: the royal artifact quest, the Crown criminal bounties and the bounty
/// board's champion bounties place a named target on a floor in the player's range when taken; killing it completes
/// the objective, and the text names the floor. All in memory; every shared singleton touched is put back.
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

    // ================= P9: placed targets =================

    private Quest RoyalArtifact(Character hero, int difficulty = 2) =>
        Track(QuestSystem.CreateRoyalAudienceQuest(hero, "Queen Fbtwo", difficulty, 100, 100, CastleLocation.RoyalQuestTypes[1]));

    private Quest CrownBounty(string nameKey)
    {
        var create = typeof(QuestSystem).GetMethod("CreateGenericBounty", SNP)!;
        for (int i = 0; i < 500; i++)
        {
            var q = (Quest)create.Invoke(null, new object[] { "Queen Fbtwo" })!;
            if (q.TitleArgs.Single() == "loc:" + nameKey) { QuestSystem.AddQuestToDatabase(q); return Track(q); }
        }
        throw new InvalidOperationException(nameKey);
    }

    private Quest ChampionBounty(int level = 20, byte difficulty = 2)
    {
        var q = QuestSystem.CreateDungeonQuest(QuestTarget.ClearBoss, difficulty, null, level);
        if (!Db.Contains(q)) QuestSystem.AddQuestToDatabase(q);
        return Track(q);
    }

    private static QuestObjective Kill(Quest q) => q.Objectives.Single(o => o.ObjectiveType == QuestObjectiveType.KillBoss);

    private static void KillMonster(Character hero, Monster m) =>
        QuestSystem.OnMonsterKilled(hero, m.Name, m.IsBoss, m.TierName, m.IsMiniBoss);

    [Fact]
    public void P9_TheRoyalArtifactQuest_PlacesTheThief_OnAFloorInRange_AndNamesIt()
    {
        GodsResolvedThrough(100);
        var hero = Hero("Fbtwo Royal", 20);
        var q = RoyalArtifact(hero, difficulty: 2);
        var o = Kill(q);
        o.TargetFloor.Should().Be(26, "the player's level plus the difficulty times 3");
        o.RequiredProgress.Should().Be(1);
        q.Objectives.Should().ContainSingle("the thief is the one objective; reaching the floor no longer completes it");
        o.GetDisplayDescription().Should().Be("Defeat the Relic Thief on floor 26");
        QuestSystem.OnDungeonFloorReached(hero, 26);
        o.IsComplete.Should().BeFalse("reaching the floor is not the kill");
    }

    [Theory]
    [InlineData("quest.generic_bounty.bandit_leader", "Bandit Leader")]
    [InlineData("quest.generic_bounty.orc_warlord", "Orc Warlord")]
    [InlineData("quest.generic_bounty.cult_leader", "Cult Leader")]
    [InlineData("quest.generic_bounty.escaped_prisoner", "Escaped Prisoner")]
    [InlineData("quest.generic_bounty.rogue_mage", "Rogue Mage")]
    public void P9_ACrownBounty_NamesItsCriminal_AndPlacesItWhenTaken(string nameKey, string english)
    {
        GodsResolvedThrough(100);
        var q = CrownBounty(nameKey);
        q.Objectives.Should().ContainSingle("the criminal is the one objective; any kills counted before");
        var o = Kill(q);
        o.TargetId.Should().Be(english.ToLower().Replace(" ", "_"));
        o.TargetFloor.Should().Be(0, "nobody has taken it yet");
        o.GetDisplayDescription().Should().Be($"Defeat the {english}");

        var hero = Hero("Fbtwo Crown", 30);
        QuestSystem.ClaimQuest(hero, q).Should().Be(QuestClaimResult.CanClaim);
        o.TargetFloor.Should().Be(Math.Min(30 + q.Difficulty * 3, 40), "the level plus the difficulty times 3, within level 30's reach of 40");
        o.GetDisplayDescription().Should().Be($"Defeat the {english} on floor {o.TargetFloor}");
    }

    [Fact]
    public void P9_AChampionBounty_IsPlacedWhenTaken_InsideItsTier_WithinReach()
    {
        GodsResolvedThrough(100);
        var q = ChampionBounty(20, 2);
        var o = Kill(q);
        o.TargetFloor.Should().Be(0);
        var hero = Hero("Fbtwo Champ", 22);
        QuestSystem.ClaimQuest(hero, q).Should().Be(QuestClaimResult.CanClaim);
        var tier = MonsterFamilies.AllFamilies.SelectMany(f => f.Tiers).First(t => t.Name.ToLower().Replace(" ", "_") == o.TargetId);
        int expected = Math.Clamp(Math.Clamp(22 + q.Difficulty * 3, tier.MinLevel, tier.MaxLevel), 1, 32);
        o.TargetFloor.Should().Be(expected);
        o.TargetFloor.Should().BeInRange(1, 32, "level 22 reaches floor 32");
        o.GetDisplayDescription().Should().EndWith($"on floor {o.TargetFloor}");
    }

    [Fact]
    public void P9_TheFloor_NeverPassesAnOldGodThePlayerHasNotDealtWith()
    {
        GodsResolvedThrough(0);
        var hero = Hero("Fbtwo Gate", 40);
        QuestSystem.ReachableFloor(hero).Should().Be(25, "Maelketh bars the way past floor 25");
        Kill(RoyalArtifact(hero, 4)).TargetFloor.Should().Be(25);
        GodsResolvedThrough(25);
        QuestSystem.ReachableFloor(hero).Should().Be(40, "Veloura bars the way past 40");
        GodsResolvedThrough(100);
        QuestSystem.ReachableFloor(hero).Should().Be(50, "level plus 10");
    }

    public static IEnumerable<object[]> Kinds() => new[] { new object[] { "royal" }, new object[] { "crown" }, new object[] { "champion" } };

    private Quest Taken(string kind, Player hero)
    {
        Quest q = kind switch
        {
            "royal" => RoyalArtifact(hero),
            "crown" => CrownBounty("quest.generic_bounty.bandit_leader"),
            _ => ChampionBounty(hero.Level),
        };
        if (kind != "royal") QuestSystem.ClaimQuest(hero, q).Should().Be(QuestClaimResult.CanClaim);
        return q;
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void P9_KillingThePlacedTarget_CompletesTheObjective(string kind)
    {
        GodsResolvedThrough(100);
        var hero = Hero("Fbtwo Slayer " + kind, 20);
        var q = Taken(kind, hero);
        var o = Kill(q);
        var target = QuestSystem.CreatePlacedTargetMonster(q, o, new Random(7));
        target.IsMiniBoss.Should().BeTrue();
        target.IsBoss.Should().BeFalse("it is no floor boss");
        target.Level.Should().Be(o.TargetFloor);
        MonsterNames.Display(target).Should().Be(QuestSystem.PlacedTargetName(o), "the fight names the target the quest names");
        KillMonster(hero, target);
        o.IsComplete.Should().BeTrue();
        q.Objectives.Should().OnlyContain(x => x.IsComplete, "killing it completes the quest");
        QuestSystem.FindPlacedTarget(hero, o.TargetFloor).Should().BeNull("a slain target waits nowhere");
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void P9_OtherKillsOnTheFloor_DoNotComplete(string kind)
    {
        GodsResolvedThrough(100);
        var hero = Hero("Fbtwo Miss " + kind, 20);
        var q = Taken(kind, hero);
        var o = Kill(q);
        var rng = new Random(11);
        for (int i = 0; i < 30; i++)
        {
            var m = MonsterGenerator.GenerateMonster(o.TargetFloor, random: rng);
            if (m.TierName.ToLower().Replace(" ", "_") == o.TargetId) continue;
            KillMonster(hero, m);
        }
        var boss = MonsterGenerator.GenerateMonster(o.TargetFloor, isBoss: true, random: rng);
        boss.Name = "Bone Lord";
        if (boss.TierName.ToLower().Replace(" ", "_") != o.TargetId) KillMonster(hero, boss);
        o.IsComplete.Should().BeFalse("only the named target completes it");
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void P9_TheTargetIsFound_OnlyOnItsFloor(string kind)
    {
        GodsResolvedThrough(100);
        var hero = Hero("Fbtwo Find " + kind, 20);
        var q = Taken(kind, hero);
        var o = Kill(q);
        o.TargetFloor.Should().BeGreaterThan(0);
        QuestSystem.FindPlacedTarget(hero, o.TargetFloor)!.Value.Quest.Should().BeSameAs(q);
        QuestSystem.FindPlacedTarget(hero, o.TargetFloor + 1).Should().BeNull();
        QuestSystem.FindPlacedTarget(hero, o.TargetFloor - 1).Should().BeNull();
        QuestSystem.FindPlacedTarget(Hero("Fbtwo Stranger"), o.TargetFloor).Should().BeNull("it waits for the one who took the quest");
    }

    [Fact]
    public void P9_TheTargetsRoom_IsNeitherTheEntranceNorTheBossRoom_AndStaysTheSame()
    {
        var floor = new DungeonFloor { Level = 12 };
        for (int i = 0; i < 9; i++) floor.Rooms.Add(new DungeonRoom { Id = "r" + i, IsBossRoom = i == 8 });
        floor.EntranceRoomId = "r0";
        var seen = new HashSet<string>();
        for (int i = 0; i < 60; i++)
        {
            var q = new Quest();
            string room = QuestSystem.PlacedTargetRoomId(floor, q);
            room.Should().NotBe("r0").And.NotBe("r8");
            QuestSystem.PlacedTargetRoomId(floor, q).Should().Be(room, "the same quest waits in the same room");
            seen.Add(room);
        }
        seen.Count.Should().BeGreaterThan(3, "quests spread over the floor's rooms");
    }

    [Fact]
    public void P9_AnOldSavesChampionBounty_HasNoFloor_AndStillCompletesOnAnyChampionOfItsTier()
    {
        var hero = Hero("Fbtwo Old", 20);
        var q = ChampionBounty(20, 2);
        q.Occupier = hero.Name2;   // claimed before 1.3.10: never placed
        var o = Kill(q);
        o.TargetFloor.Should().Be(0);
        QuestSystem.FindPlacedTarget(hero, 26).Should().BeNull();
        var tier = MonsterFamilies.AllFamilies.SelectMany(f => f.Tiers.Select(t => (f, t))).First(p => p.t.Name.ToLower().Replace(" ", "_") == o.TargetId);
        var champion = MonsterGenerator.GenerateMonster(tier.t.MinLevel, isMiniBoss: true, random: new Random(3), forced: tier);
        KillMonster(hero, champion);
        o.IsComplete.Should().BeTrue("a champion of its tier anywhere still counts, as before");
    }

    [Fact]
    public void P9_TheFloor_SurvivesASave_AndASaveWithoutIt_LoadsAsNone()
    {
        GodsResolvedThrough(100);
        var hero = Hero("Fbtwo Saver", 20);
        var q = RoyalArtifact(hero);
        int floor = Kill(q).TargetFloor;
        var serialize = typeof(SaveSystem).GetMethod("SerializeQuestList", INP)!;
        var data = (List<QuestData>)serialize.Invoke(SaveSystem.Instance, new object[] { new List<Quest> { q } })!;
        data.Single().Objectives.Single().TargetFloor.Should().Be(floor);
        string json = JsonSerializer.Serialize(data);
        var back = JsonSerializer.Deserialize<List<QuestData>>(json)!;
        QuestSystem.MergePlayerQuests(hero.Name2, back);
        Db.Single(x => x.Id == q.Id).Objectives.Single().TargetFloor.Should().Be(floor);

        string old = json.Replace($"\"TargetFloor\":{floor}", "\"Fbtwo\":0");
        old.Should().NotContain("TargetFloor");
        QuestSystem.MergePlayerQuests(hero.Name2, JsonSerializer.Deserialize<List<QuestData>>(old)!);
        Db.Single(x => x.Id == q.Id).Objectives.Single().TargetFloor.Should().Be(0, "a save from before 1.3.10 has no floor");
    }

    [Fact]
    public void P9_EveryObjectiveSaveSite_CarriesTheFloor()
    {
        string root = RepoRoot();
        int Sites(string file, string needle) =>
            File.ReadAllText(Path.Combine(root, "Scripts", "Systems", file)).Split(needle).Length - 1;
        Sites("QuestSystem.cs", "BonusReward = objData.BonusReward,").Should().Be(3);
        Sites("QuestSystem.cs", "TargetFloor = objData.TargetFloor").Should().Be(3, "each restore path reads it");
        Sites("SaveSystem.cs", "TargetFloor = objective.TargetFloor").Should().Be(1);
        Sites("OnlineStateManager.cs", "TargetFloor = objective.TargetFloor").Should().Be(1);
    }

    [Fact]
    public void P9_TheTakersAreToldTheFloor_AtTheQuestHall_AndTheCastle_AndOnArrival()
    {
        string root = RepoRoot();
        string hall = File.ReadAllText(Path.Combine(root, "Scripts", "Locations", "QuestHallLocation.cs"));
        string castle = File.ReadAllText(Path.Combine(root, "Scripts", "Locations", "CastleLocation.cs"));
        string dungeon = File.ReadAllText(Path.Combine(root, "Scripts", "Locations", "DungeonLocation.cs"));
        hall.Should().Contain("QuestSystem.WritePlacedTargetLines(terminal, quest);");
        castle.Should().Contain("QuestSystem.WritePlacedTargetLines(terminal, quest);");
        dungeon.Should().Contain("Loc.Get(\"quest.placed.floor_hint\"");
        dungeon.Should().Contain("if (await TryFightPlacedTarget(targetRoom))");

        GodsResolvedThrough(100);
        var hero = Hero("Fbtwo Told", 20);
        var q = RoyalArtifact(hero);
        var output = new MemoryStream();
        var term = new TerminalEmulator(new MemoryStream(), output);
        QuestSystem.WritePlacedTargetLines(term, q);
        string text = System.Text.Encoding.UTF8.GetString(output.ToArray());
        text.Should().Contain("Your quarry, the Relic Thief, waits on dungeon floor 26.");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void P9_TheObjectiveRow_FitsSeventyNineColumns(string lang)
    {
        var names = new List<string>();
        foreach (var key in new[] { "quest.placed.relic_thief", "quest.generic_bounty.bandit_leader", "quest.generic_bounty.orc_warlord",
                     "quest.generic_bounty.cult_leader", "quest.generic_bounty.escaped_prisoner", "quest.generic_bounty.rogue_mage" })
            names.Add(Loc.GetIn(lang, key));
        foreach (var f in MonsterFamilies.AllFamilies)
            foreach (var t in f.Tiers)
                names.Add(MonsterNames.ChampionOfTierIn(lang, t.Name, f.FamilyName));
        foreach (var name in names)
        {
            string row = $"    [ ] {Loc.GetIn(lang, "quest.objective.defeat_on_floor", name, 100)} (0/1)";
            row.Length.Should().BeLessOrEqualTo(79, row);
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj"))) dir = dir.Parent;
        return dir!.FullName;
    }
}
