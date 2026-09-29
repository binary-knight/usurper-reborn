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
/// 1.2.0 Temple gods piece 6: NPC townsfolk worship a god (NpcFaithSystem), saved as the NPC's
/// WorshippedGod, given once and the same on every load; each living NPC follower adds
/// GodNpcFollowerStanding to its god's standing, online and single-player; the first talk of the day
/// with an NPC of the player's god warms it and with an NPC of an opposed god cools it.
/// </summary>
[Collection("SharedGameSingletons")]
public class GodNpcFaith1115Tests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-npcfaith-{Guid.NewGuid():N}.db");
    private readonly SqlSaveBackend _db;
    private readonly List<NPC> _rosterBefore;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IncludeFields = true,
    };

    public GodNpcFaith1115Tests()
    {
        _db = new SqlSaveBackend(_path);
        _rosterBefore = NPCSpawnSystem.Instance.ActiveNPCs.ToList();
        NPCSpawnSystem.Instance.ActiveNPCs.Clear();
        NpcFaithSystem.ResetMeetingsForTests();
    }

    public void Dispose()
    {
        var roster = NPCSpawnSystem.Instance.ActiveNPCs;
        roster.Clear();
        roster.AddRange(_rosterBefore);
        NpcFaithSystem.ResetMeetingsForTests();
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    private static NPC Npc(string name, CharacterClass cls = CharacterClass.Warrior, long chivalry = 0, long darkness = 0, string god = "")
    {
        var npc = new NPC { ID = "id_" + name, Id = "id_" + name, Name1 = name, Name2 = name, Level = 10, HP = 100, MaxHP = 100, Class = cls, WorshippedGod = god };
        npc.Chivalry = chivalry;
        npc.Darkness = darkness;
        return npc;
    }

    private static string Source(string folder, string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Scripts"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "Scripts", folder, file));
    }

    // ---------------- The pick ----------------

    [Fact]
    public void Weight_FollowsAlignmentAndClass_AndNeverAnOpposedGod()
    {
        var goodPaladin = Npc("NfWeightA", CharacterClass.Paladin, chivalry: 500);
        NpcFaithSystem.Weight(goodPaladin, "Solarius").Should().Be(GameConfig.NpcFaithAlignedWeight + GameConfig.NpcFaithClassWeight);
        NpcFaithSystem.Weight(goodPaladin, "Amara").Should().Be(GameConfig.NpcFaithAlignedWeight);
        NpcFaithSystem.Weight(goodPaladin, "Arcanus").Should().Be(GameConfig.NpcFaithNeutralWeight);
        NpcFaithSystem.Weight(goodPaladin, "Umbrath").Should().Be(0);

        var evilAssassinGoodClass = Npc("NfWeightB", CharacterClass.Paladin, darkness: 500);
        NpcFaithSystem.Weight(evilAssassinGoodClass, "Solarius").Should().Be(0, "a class never pulls an NPC to an opposed god");
        NpcFaithSystem.Weight(evilAssassinGoodClass, "Mortis").Should().Be(GameConfig.NpcFaithAlignedWeight);

        var neutralMage = Npc("NfWeightC", CharacterClass.Magician);
        NpcFaithSystem.Weight(neutralMage, "Arcanus").Should().Be(GameConfig.NpcFaithAlignedWeight + GameConfig.NpcFaithClassWeight);
        NpcFaithSystem.Weight(neutralMage, "Discordia").Should().Be(GameConfig.NpcFaithNeutralWeight);
    }

    [Fact]
    public void PickGod_IsTheSameForTheSameNpc_AndNeverAnOpposedGod()
    {
        for (int i = 0; i < 200; i++)
        {
            var good = Npc($"NfGood{i}", CharacterClass.Assassin, chivalry: 900, darkness: 10);
            NpcFaithSystem.PickGod(good).Should().Be(NpcFaithSystem.PickGod(Npc($"NfGood{i}", CharacterClass.Assassin, chivalry: 900, darkness: 10)));
            NpcFaithSystem.CanonSide(NpcFaithSystem.PickGod(good)).Should().NotBe(FaithSide.Evil);
            var evil = Npc($"NfEvil{i}", CharacterClass.Cleric, darkness: 900);
            NpcFaithSystem.CanonSide(NpcFaithSystem.PickGod(evil)).Should().NotBe(FaithSide.Good);
            GameConfig.CanonGodNames.Should().Contain(NpcFaithSystem.PickGod(evil));
        }
        var picks = Enumerable.Range(0, 300).Select(i => NpcFaithSystem.PickGod(Npc($"NfSpread{i}"))).Distinct().ToList();
        picks.Should().HaveCountGreaterThan(5, "a neutral warrior can follow any god, Valorian and Arcanus more often");
    }

    [Fact]
    public void EnsureAssigned_FillsAnEmptyGod_AndKeepsAGodItHas()
    {
        var blank = Npc("NfBlank");
        NpcFaithSystem.EnsureAssigned(blank).Should().BeTrue();
        blank.WorshippedGod.Should().Be(NpcFaithSystem.PickGod(blank));
        NpcFaithSystem.EnsureAssigned(blank).Should().BeFalse("once given, never changed");

        var follower = Npc("NfFollower", god: "Zephyrine");
        NpcFaithSystem.EnsureAssigned(follower).Should().BeFalse();
        follower.WorshippedGod.Should().Be("Zephyrine", "a player-god follower keeps the one faith field");

        var manwe = Npc("NfManwe", god: "Manwe");
        NpcFaithSystem.EnsureAssigned(manwe).Should().BeTrue();
        manwe.WorshippedGod.Should().NotBe("Manwe");
    }

    [Fact]
    public void AnNpcEnteringTheRoster_IsGivenAGod()
    {
        var npc = Npc("NfRoster");
        NPCSpawnSystem.Instance.AddRestoredNPC(npc);
        npc.WorshippedGod.Should().Be(NpcFaithSystem.PickGod(npc));
    }

    [Fact]
    public async Task TheGodIsSaved_AndTwoLoadsGiveTheSameGod()
    {
        NPCSpawnSystem.Instance.AddRestoredNPC(Npc("NfSaveA"));
        NPCSpawnSystem.Instance.AddRestoredNPC(Npc("NfSaveB", god: "Zephyrine"));
        string godA = NPCSpawnSystem.Instance.ActiveNPCs.Single(n => n.Name2 == "NfSaveA").WorshippedGod;
        godA.Should().NotBeEmpty();

        var stored = JsonSerializer.Deserialize<List<NPCData>>(JsonSerializer.Serialize(OnlineStateManager.SerializeCurrentNPCs(), Json), Json)!;
        stored.Single(d => d.Name == "NfSaveA").WorshippedGod.Should().Be(godA, "saved with the NPC");

        async Task<(string A, string B)> Load(List<NPCData> data)
        {
            NPCSpawnSystem.Instance.ActiveNPCs.Clear();
            await GameEngine.Instance.RestoreNPCs(JsonSerializer.Deserialize<List<NPCData>>(JsonSerializer.Serialize(data, Json), Json)!);
            var r = NPCSpawnSystem.Instance.ActiveNPCs;
            return (r.Single(n => n.Name2 == "NfSaveA").WorshippedGod, r.Single(n => n.Name2 == "NfSaveB").WorshippedGod);
        }
        (await Load(stored)).Should().Be((godA, "Zephyrine"));
        (await Load(stored)).Should().Be((godA, "Zephyrine"));

        // a save from before NPC faith: the first load gives the god, the second the same one
        foreach (var d in stored) d.WorshippedGod = "";
        var first = await Load(stored);
        first.A.Should().Be(godA);
        (await Load(stored)).Should().Be(first);
    }

    // ---------------- Standing ----------------

    [Fact]
    public void SinglePlayer_EachLivingNpcFollowerAddsFive()
    {
        var gods = new GodSystem();
        var c = new Character { Name1 = "NfSpStand", Name2 = "NfSpStand", AI = CharacterAI.Human };
        GodRegistry.SetWorshippedGod(c, "Terran", gods);
        FavorSystem.Change(c, 12, gods);
        var dead = Npc("NfDead", god: "Terran");
        dead.IsDead = true;
        var npcs = new List<NPC> { Npc("NfT1", god: "terran"), Npc("NfT2", god: "Terran"), dead, Npc("NfM1", god: "Mortis"), Npc("NfNone") };

        var s = GodRegistry.SinglePlayerStandings(c, gods, npcs);
        s["Terran"].Standing.Should().Be(12 + 2 * GameConfig.GodNpcFollowerStanding);
        s["Terran"].Followers.Should().Be(1);
        s["Terran"].NpcFollowers.Should().Be(2);
        s["Terran"].AllFollowers.Should().Be(3);
        s["Mortis"].Should().Be(new GodStanding("Mortis", GameConfig.GodNpcFollowerStanding, 0) { NpcFollowers = 1 });
        GameConfig.GodNpcFollowerStanding.Should().Be(5);

        GodStandingPenalty.AddLocal(c, "Mortis", GodStandingPenalty.CurrentWeek());
        GodRegistry.SinglePlayerStandings(c, gods, npcs)["Mortis"].Standing.Should().Be(0, "the penalty comes after the NPCs, never below 0");
    }

    [Fact]
    public async Task Online_TheWorldsNpcFollowersCount_InTheOneStandingsRead()
    {
        var npcs = new List<NPCData>
        {
            new() { Id = "a", Name = "NfOa", WorshippedGod = "Amara" },
            new() { Id = "b", Name = "NfOb", WorshippedGod = "amara" },
            new() { Id = "c", Name = "NfOc", WorshippedGod = "Amara", IsDead = true },
            new() { Id = "d", Name = "NfOd", WorshippedGod = "" },
            new() { Id = "e", Name = "NfOe", WorshippedGod = "Zephyrine" },
        };
        string json = JsonSerializer.Serialize(npcs, Json);
        json.Should().Contain("\"worshippedGod\"").And.Contain("\"isDead\"", "the paths the standings read uses");
        await _db.SaveWorldState("npcs", json);
        await _db.WriteGameData("acct_nf", new SaveGameData
        {
            Version = GameConfig.SaveVersion,
            Player = new PlayerData { Name1 = "NfAna", Name2 = "NfAna", Level = 5, GodFavor = 20, GodFavorGod = "Amara", GodFavorSchema = GameConfig.GodFavorSchemaCurrent },
            StorySystems = new StorySystemsData { PlayerGods = new Dictionary<string, string> { ["NfAna"] = "Amara" } }
        });

        var s = _db.GetGodStandings();
        s["Amara"].Should().Be(new GodStanding("Amara", 20 + 2 * GameConfig.GodNpcFollowerStanding, 1) { NpcFollowers = 2 });
        s["Zephyrine"].Should().Be(new GodStanding("Zephyrine", GameConfig.GodNpcFollowerStanding, 0) { NpcFollowers = 1 });
        s.Should().HaveCount(2);
    }

    [Fact]
    public void Online_StandingsRead_CountsNpcsInTheSameReadWithoutAPerGodScan()
    {
        var src = Source("Systems", "SqlSaveBackend.cs");
        int start = src.IndexOf("public Dictionary<string, GodStanding> GetGodStandings(int week)", StringComparison.Ordinal);
        int end = src.IndexOf("public void AddGodStandingPenalty", start, StringComparison.Ordinal);
        var body = src.Substring(start, end - start);
        body.Split("OpenConnection()").Length.Should().Be(2, "one connection for the whole read");
        body.Should().Contain("json_each(w.value)").And.Contain("GROUP BY god");
        body.Should().Contain("GodRegistry.AddNpcFollowers(GodRegistry.ComputeStandings(entries), npcCounts)");
    }

    [Fact]
    public void TempleCounts_ReadTheStandings_NotTheLocalRoster()
    {
        var src = Source("Locations", "TempleLocation.cs");
        src.Should().Contain("Loc.Get(\"temple.believers_count\", standing.AllFollowers)");
        src.Should().Contain("ranking.Add((entry.Name, title, standing.AllFollowers, standing.Standing, !entry.IsCanon));");
    }

    // ---------------- Talks ----------------

    private static (Character Player, GodSystem Gods) Worshipper(string name, string god, int favor)
    {
        var gods = new GodSystem();
        var c = new Character { Name1 = name, Name2 = name, AI = CharacterAI.Human, Level = 5 };
        GodRegistry.SetWorshippedGod(c, god, gods);
        FavorSystem.Change(c, favor, gods);
        return (c, gods);
    }

    [Fact]
    public void ASharedGod_WarmsTheNpc_OnceADay()
    {
        var (p, gods) = Worshipper("NfTalkShared", "Amara", 10);
        var npc = Npc("NfTalkAmara", god: "Amara");
        int before = RelationshipSystem.GetRelationshipStatus(npc, p);

        var m = NpcFaithSystem.OnTalk(p, npc, 40, gods);
        m.Kind.Should().Be(FaithMeetingKind.Shared);
        m.Applied.Should().BeTrue();
        int after = RelationshipSystem.GetRelationshipStatus(npc, p);
        after.Should().BeLessThan(before, "lower is warmer");

        NpcFaithSystem.OnTalk(p, npc, 40, gods).Applied.Should().BeFalse();
        RelationshipSystem.GetRelationshipStatus(npc, p).Should().Be(after, "a second talk that day changes nothing");
        NpcFaithSystem.Lines(NpcFaithSystem.OnTalk(p, npc, 40, gods), npc.Name2).Should().BeEmpty();
    }

    [Fact]
    public void AnOpposedGod_CoolsTheNpc_OnceADay_AndANeutralOneDoesNothing()
    {
        var (p, gods) = Worshipper("NfTalkOpp", "Solarius", 10);
        var dark = Npc("NfTalkMortis", god: "Mortis");
        int before = RelationshipSystem.GetRelationshipStatus(dark, p);
        NpcFaithSystem.OnTalk(p, dark, 41, gods).Kind.Should().Be(FaithMeetingKind.Opposed);
        int after = RelationshipSystem.GetRelationshipStatus(dark, p);
        after.Should().BeGreaterThan(before, "higher is cooler");
        NpcFaithSystem.OnTalk(p, dark, 41, gods);
        RelationshipSystem.GetRelationshipStatus(dark, p).Should().Be(after);
        NpcFaithSystem.OnTalk(p, dark, 42, gods).Applied.Should().BeTrue("a new day");

        var mage = Npc("NfTalkArcanus", god: "Arcanus");
        int mb = RelationshipSystem.GetRelationshipStatus(mage, p);
        NpcFaithSystem.OnTalk(p, mage, 41, gods).Kind.Should().Be(FaithMeetingKind.None);
        RelationshipSystem.GetRelationshipStatus(mage, p).Should().Be(mb);

        var (godless, g2) = (new Character { Name1 = "NfTalkNone", Name2 = "NfTalkNone", AI = CharacterAI.Human }, new GodSystem());
        NpcFaithSystem.OnTalk(godless, Npc("NfTalkAny", god: "Mortis"), 41, g2).Kind.Should().Be(FaithMeetingKind.None);
    }

    [Fact]
    public void APlayerGodFollower_IsOpposedByTheSideOfItsDomain()
    {
        var gods = new GodSystem();
        var p = new Character { Name1 = "NfTalkPg", Name2 = "NfTalkPg", AI = CharacterAI.Human };
        GodRegistry.SetWorshippedGod(p, "Zephyrine", gods);
        GodBoonSystem.SetPlayerGodBoon(p, "Zephyrine", GodDomain.Chaos, 100);
        NpcFaithSystem.PlayerGodSide(p, gods).Should().Be(FaithSide.Evil);
        NpcFaithSystem.MeetingKind(p, Npc("NfTalkSol", god: "Solarius"), gods).Should().Be(FaithMeetingKind.Opposed);
        NpcFaithSystem.MeetingKind(p, Npc("NfTalkZep", god: "Zephyrine"), gods).Should().Be(FaithMeetingKind.Shared);
    }

    [Fact]
    public void AChosenPlayersMark_DrawsARemark_AndOnlyAtChosen()
    {
        var (chosen, gods) = Worshipper("NfMarkChosen", "Valorian", GameConfig.GodFavorTierChosenMin);
        var m = NpcFaithSystem.OnTalk(chosen, Npc("NfMarkA", god: "Valorian"), 50, gods);
        m.PlayerChosen.Should().BeTrue();
        var lines = NpcFaithSystem.Lines(m, "NfMarkA");
        lines.Should().HaveCount(2);
        lines[1].Should().Be(Loc.Get("faith.npc_mark_shared", "NfMarkA", "Valorian"));
        NpcFaithSystem.Lines(NpcFaithSystem.OnTalk(chosen, Npc("NfMarkB", god: "Arcanus"), 50, gods), "NfMarkB")
            .Should().Equal(Loc.Get("faith.npc_mark_other", "NfMarkB", "Valorian"));

        var (zealot, g2) = Worshipper("NfMarkZealot", "Valorian", GameConfig.GodFavorTierChosenMin - 1);
        var z = NpcFaithSystem.OnTalk(zealot, Npc("NfMarkC", god: "Arcanus"), 50, g2);
        z.PlayerChosen.Should().BeFalse();
        NpcFaithSystem.Lines(z, "NfMarkC").Should().BeEmpty();
    }

    [Fact]
    public void TheTalk_RunsTheFaithMeeting_AfterTheGreeting()
    {
        var src = Source("Systems", "VisualNovelDialogueSystem.cs");
        int greet = src.IndexOf("await ShowGreeting(npc, relationLevel, romanceType);", StringComparison.Ordinal);
        int faith = src.IndexOf("ShowFaithMeeting(npc);", StringComparison.Ordinal);
        greet.Should().BeGreaterThan(0);
        faith.Should().BeGreaterThan(greet);
        faith.Should().BeLessThan(src.IndexOf("while (continueConversation)", StringComparison.Ordinal));
        src.Should().Contain("NpcFaithSystem.OnTalk(player, npc, DailySystemManager.Instance.CurrentDay)");
    }

    // ---------------- The Pascal gods are gone ----------------

    [Fact]
    public void ThePascalGodNames_AndTheirField_AreGone()
    {
        typeof(Character).GetProperty("God").Should().BeNull("an NPC's god is WorshippedGod");
        foreach (var (folder, file) in new[] { ("AI", "NPCBrain.cs"), ("AI", "EnhancedNPCBehaviors.cs"), ("AI", "EnhancedNPCBehaviorSystem.cs"), ("Core", "NPC.cs") })
        {
            var src = Source(folder, file);
            src.Should().NotContain("Nosferatu").And.NotContain("Darkcloak").And.NotContain("\"Druid\"");
        }
        Source("AI", "NPCBrain.cs").Should().Contain("NpcFaithSystem.EnsureAssigned(owner)");
        Source("Core", "NPC.cs").Should().Contain("NpcFaithSystem.EnsureAssigned(this)");
    }

    [Fact]
    public void FaithLines_AreInAllFiveLanguages()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Localization"))) dir = dir.Parent;
        foreach (var lang in new[] { "en", "es", "fr", "hu", "it" })
        {
            var keys = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "Localization", lang + ".json"))).RootElement;
            foreach (var k in new[] { "faith.npc_shared", "faith.npc_opposed", "faith.npc_mark_shared", "faith.npc_mark_other" })
                keys.TryGetProperty(k, out _).Should().BeTrue($"{lang} has {k}");
        }
    }
}
