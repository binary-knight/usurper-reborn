using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.0 Temple gods piece 6 follow-ups: an NPC loosely devout to its god (a weak fit for its
/// alignment and class) is recruited as a pagan and can be converted by another NPC; a player-god's
/// boon scale compares players' standing only, not NPC followers.
/// </summary>
[Collection("SharedGameSingletons")]
public class GodFaithFixes1115Tests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-faithfix-{Guid.NewGuid():N}.db");
    private SqlSaveBackend? _db;
    private SqlSaveBackend Db => _db ??= new SqlSaveBackend(_path);
    private readonly List<NPC> _rosterBefore;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IncludeFields = true,
    };

    public GodFaithFixes1115Tests()
    {
        _rosterBefore = NPCSpawnSystem.Instance.ActiveNPCs.ToList();
        NPCSpawnSystem.Instance.ActiveNPCs.Clear();
    }

    public void Dispose()
    {
        var roster = NPCSpawnSystem.Instance.ActiveNPCs;
        roster.Clear();
        roster.AddRange(_rosterBefore);
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    private static NPC Npc(string name, CharacterClass cls, long chivalry = 0, long darkness = 0, string god = "", string location = "Main Street")
    {
        var npc = new NPC { ID = "id_" + name, Id = "id_" + name, Name1 = name, Name2 = name, Level = 10, HP = 100, MaxHP = 100, Class = cls, WorshippedGod = god };
        npc.Chivalry = chivalry;
        npc.Darkness = darkness;
        npc.CurrentLocation = location;
        return npc;
    }

    private static string Source(string folder, string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Scripts"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "Scripts", folder, file));
    }

    // ---------------- Loosely devout ----------------

    [Fact]
    public void LooselyDevout_IsAGodNeitherOfTheNpcsSideNorItsClass()
    {
        var goodPaladin = Npc("FfGoodPal", CharacterClass.Paladin, chivalry: 500);
        goodPaladin.WorshippedGod = "Solarius";
        NpcFaithSystem.IsLooselyDevout(goodPaladin).Should().BeFalse("its side and its class god");
        goodPaladin.WorshippedGod = "Amara";
        NpcFaithSystem.IsLooselyDevout(goodPaladin).Should().BeFalse("a god of its own side");
        goodPaladin.WorshippedGod = "Arcanus";
        NpcFaithSystem.IsLooselyDevout(goodPaladin).Should().BeTrue("the neutral god is the weakest nonzero weight for a good NPC");
        goodPaladin.WorshippedGod = "Umbrath";
        NpcFaithSystem.IsLooselyDevout(goodPaladin).Should().BeTrue("an opposed god after its alignment moved is weaker still");

        var neutralWarrior = Npc("FfNeuWar", CharacterClass.Warrior);
        neutralWarrior.WorshippedGod = "Arcanus";
        NpcFaithSystem.IsLooselyDevout(neutralWarrior).Should().BeFalse("the neutral god is a neutral NPC's own side");
        neutralWarrior.WorshippedGod = "valorian";
        NpcFaithSystem.IsLooselyDevout(neutralWarrior).Should().BeFalse("the warrior's class god, in any letter case");
        neutralWarrior.WorshippedGod = "Amara";
        NpcFaithSystem.IsLooselyDevout(neutralWarrior).Should().BeTrue();

        var evilMage = Npc("FfEvilMage", CharacterClass.Magician, darkness: 500);
        evilMage.WorshippedGod = "Arcanus";
        NpcFaithSystem.IsLooselyDevout(evilMage).Should().BeFalse("its class god");
        evilMage.WorshippedGod = "Mortis";
        NpcFaithSystem.IsLooselyDevout(evilMage).Should().BeFalse();

        NpcFaithSystem.IsLooselyDevout(Npc("FfNone", CharacterClass.Warrior)).Should().BeFalse("no god is a pagan, not loosely devout");
        NpcFaithSystem.IsLooselyDevout(Npc("FfPg", CharacterClass.Warrior, god: "Zephyrine")).Should().BeFalse("a player-god's follower is no weak fit");
        NpcFaithSystem.IsLooselyDevout(null!).Should().BeFalse();
    }

    [Fact]
    public void Recruit_ALooselyDevoutNpc_IsTakenAsAPagan_AStrongFitIsASteal()
    {
        PantheonLocation.RecruitsLikePagan(Npc("FfRcNone", CharacterClass.Warrior)).Should().BeTrue("a pagan");
        PantheonLocation.RecruitsLikePagan(Npc("FfRcLoose", CharacterClass.Warrior, chivalry: 500, god: "Arcanus")).Should().BeTrue("loosely devout");
        PantheonLocation.RecruitsLikePagan(Npc("FfRcStrong", CharacterClass.Warrior, chivalry: 500, god: "Valorian")).Should().BeFalse("a strong fit is a steal");
        PantheonLocation.RecruitsLikePagan(Npc("FfRcSide", CharacterClass.Warrior, chivalry: 500, god: "Amara")).Should().BeFalse("its own side is a steal");
        PantheonLocation.RecruitsLikePagan(Npc("FfRcPg", CharacterClass.Warrior, god: "Zephyrine")).Should().BeFalse("another player-god's follower is a steal");

        var src = Source("Locations", "PantheonLocation.cs");
        int start = src.IndexOf("private async Task DeedRecruitBeliever()", StringComparison.Ordinal);
        var body = src.Substring(start, src.IndexOf("private async Task DeedBlessFollower()", start, StringComparison.Ordinal) - start);
        body.Should().Contain("RecruitsLikePagan = RecruitsLikePagan(npc)");
        body.Should().Contain("bool isPagan = target.RecruitsLikePagan;");
        body.Should().NotContain("Status.Contains(\"Pagan\")", "the branch reads the target, not its translated label");
        body.Should().Contain("float chance = GameConfig.GodRecruitPaganChance;").And.Contain("int expGain = GameConfig.GodRecruitPaganExp;");
        GameConfig.GodRecruitPaganExp.Should().Be(150);
    }

    // ---------------- NPC to NPC conversion ----------------

    [Fact]
    public void Proselytize_ConvertsOnlyLooselyDevoutOrGodlessNpcs_NeverAStrongFitOrTheSameGod()
    {
        var believer = Npc("FfBeliever", CharacterClass.Cleric, chivalry: 500, god: "Solarius");
        var loose = Npc("FfLoose", CharacterClass.Warrior, chivalry: 500, god: "Arcanus");
        var godless = Npc("FfGodless", CharacterClass.Warrior);
        var strong = Npc("FfStrong", CharacterClass.Warrior, chivalry: 500, god: "Valorian");
        var sameGodLoose = Npc("FfSame", CharacterClass.Warrior, god: "solarius");   // weak fit, but already the believer's god
        var elsewhere = Npc("FfAway", CharacterClass.Warrior, chivalry: 500, god: "Arcanus", location: "Inn");
        var dead = Npc("FfDead", CharacterClass.Warrior, chivalry: 500, god: "Arcanus");
        dead.HP = 0;
        dead.IsDead = true;
        NpcFaithSystem.IsLooselyDevout(sameGodLoose).Should().BeTrue();

        var all = new List<NPC> { believer, loose, godless, strong, sameGodLoose, elsewhere, dead };
        EnhancedNPCBehaviors.ProselytizeCandidates(believer, all).Should().BeEquivalentTo(new[] { loose, godless });
        EnhancedNPCBehaviors.ProselytizeCandidates(Npc("FfNoFaith", CharacterClass.Warrior), all).Should().BeEmpty("a godless NPC preaches nothing");
    }

    // ---------------- Boon scale: players only ----------------

    [Fact]
    public void BoonScale_NpcFollowersOfACanonGod_DoNotChangeAPlayerGodsScale()
    {
        var standings = GodRegistry.ComputeStandings(new[] { ("Amara", 80), ("Zephyrine", 30), ("Zephyrine", 30) });
        long before = GodBoonSystem.StrongestCanon(standings);
        int scaleBefore = GodBoonSystem.PlayerGodScalePct(GodBoonSystem.StandingOf(standings, "Zephyrine"), before, 0);
        before.Should().Be(80);
        scaleBefore.Should().Be(75);

        GodRegistry.AddNpcFollowers(standings, new Dictionary<string, int> { ["Amara"] = 10, ["Mortis"] = 40 });
        standings["Amara"].Standing.Should().Be(80 + 10 * GameConfig.GodNpcFollowerStanding, "rankings, altars and the weekly god still count NPCs");
        WeeklyGodSystem.TopGod(standings).Should().Be("Mortis");
        GodBoonSystem.StrongestCanon(standings).Should().Be(80, "the boon compares players only");
        GodBoonSystem.PlayerGodScalePct(GodBoonSystem.StandingOf(standings, "Zephyrine"), GodBoonSystem.StrongestCanon(standings), 0).Should().Be(scaleBefore);

        GodRegistry.AddNpcFollowers(standings, new Dictionary<string, int> { ["Zephyrine"] = 20 });
        GodBoonSystem.StandingOf(standings, "Zephyrine").Should().Be(60, "the player-god's own NPC followers are left out too");
    }

    [Fact]
    public void PlayerStanding_WithPenalties_IsFavorLessPenaltyFlooredAtZero()
    {
        var s = GodRegistry.AddNpcFollowers(GodRegistry.ComputeStandings(new[] { ("Amara", 4) }), new Dictionary<string, int> { ["Amara"] = 2 });
        GodStandingPenalty.Apply(s, new Dictionary<string, int> { ["Amara"] = 5 });
        s["Amara"].Standing.Should().Be(9);
        s["Amara"].PlayerStanding.Should().Be(0, "4 less 5, never below 0");

        var t = GodRegistry.AddNpcFollowers(GodRegistry.ComputeStandings(new[] { ("Amara", 30) }), new Dictionary<string, int> { ["Amara"] = 2 });
        GodStandingPenalty.Apply(t, new Dictionary<string, int> { ["Amara"] = 5 });
        t["Amara"].PlayerStanding.Should().Be(25);
        t["Amara"].Should().Be(new GodStanding("Amara", 35, 1) { NpcFollowers = 2 }, "the record's equality is unchanged");
    }

    private Task Save(string key, PlayerData p, Dictionary<string, string>? canon = null) =>
        Db.WriteGameData(key, new SaveGameData
        {
            Version = GameConfig.SaveVersion,
            Player = p,
            StorySystems = new StorySystemsData { PlayerGods = canon ?? new Dictionary<string, string>() }
        });

    private static PlayerData Mortal(string name, string playerGod, int favor) =>
        new PlayerData { Name1 = name, Name2 = name, Level = 5, WorshippedGod = playerGod, GodFavor = favor, GodFavorGod = playerGod, GodFavorSchema = GameConfig.GodFavorSchemaCurrent };

    [Fact]
    public async Task Online_NpcFollowersOfACanonGod_DoNotChangeAPlayerGodsBoon()
    {
        await Save("zeph", new PlayerData { Name1 = "FfZeph", Name2 = "FfZeph", Level = 90, IsImmortal = true, DivineName = "Zephyrine", DivineDomain = "Chaos" });
        await Save("a1", Mortal("FfA1", "Zephyrine", 30));
        await Save("a2", Mortal("FfA2", "Zephyrine", 30));
        var b1 = Mortal("FfB1", "", 80);
        b1.GodFavorGod = "Amara";
        await Save("b1", b1, new Dictionary<string, string> { ["FfB1"] = "Amara" });
        var now = DateTime.UtcNow;
        (await GodBoonSystem.PlayerGodBoonAsync("Zephyrine", Db, now)).ScalePct.Should().Be(75, "standing 60 against Amara's 80");

        var npcs = Enumerable.Range(0, 12).Select(i => new NPCData { Id = "ff" + i, Name = "FfNpc" + i, WorshippedGod = "Amara" }).ToList();
        await Db.SaveWorldState("npcs", JsonSerializer.Serialize(npcs, Json));
        Db.GetGodStandings()["Amara"].Standing.Should().Be(80 + 12 * GameConfig.GodNpcFollowerStanding);
        (await GodBoonSystem.PlayerGodBoonAsync("Zephyrine", Db, now)).ScalePct.Should().Be(75, "NPC followers do not count against a player-god's boon");
    }

    [Fact]
    public void TempleListing_ScalesThePlayerGodOnPlayersOnly()
    {
        var src = Source("Locations", "TempleLocation.cs");
        src.Should().Contain("GodBoonSystem.StandingOf(standings, god.DivineName), strongestCanon,");
        src.Should().NotContain("out var st) ? st.Standing : 0, strongestCanon");
    }
}
