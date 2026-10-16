using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Locations;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using Xunit;
using static UsurperReborn.Tests.PartySurvival133Harness;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.3 party survival (PS-2 section 3 and the class A sites outside CombatEngine): the world sim never
/// acts on a Downed NPC ally (no respawn queue, no respawn of an NPC alive again), the paid resurrection
/// lists and the dead counts leave it out, the NPC HP writers save it alive, and GMCP Char.Combat.Party and
/// the Electron payloads carry a downed flag.
/// </summary>
[Collection("SharedGameSingletons")]
public class Feeds_PartySurvival133Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static NPC Npc(string name, long hp, string team = "") => new NPC
    {
        ID = "npc_ps133_" + name.ToLowerInvariant(), Name1 = name, Name2 = name, Level = 12,
        HP = hp, MaxHP = 300, BaseMaxHP = 300, Strength = 20, BaseStrength = 20, Defence = 5, BaseDefence = 5,
        Gold = 1000, CurrentLocation = "Dungeon", Team = team, Memory = new MemorySystem(),
    };

    private static NPC DownedNpc(string name, string team = "")
    {
        var n = Npc(name, 0, team);
        n.Lifecycle = AllyLifecycle.Downed;
        return n;
    }

    /// <summary>The roster holds only the given NPCs while the body runs; news goes to a buffer.</summary>
    private static T InTown<T>(IEnumerable<NPC> town, Func<T> body)
    {
        var roster = NPCSpawnSystem.Instance.ActiveNPCs;
        var saved = roster.ToList();
        var news = new List<string>();
        roster.Clear();
        roster.AddRange(town);
        NewsSystem.Instance.SetCatchUpBuffer(news);
        try { return body(); }
        finally
        {
            NewsSystem.Instance.ClearCatchUpBuffer();
            roster.Clear();
            roster.AddRange(saved);
        }
    }

    private static Dictionary<string, int> Queue(WorldSimulator sim) =>
        (Dictionary<string, int>)typeof(WorldSimulator).GetField("deadNPCRespawnTimers", F)!.GetValue(sim)!;

    // ---------- 18. the world sim sweeps skip a Downed ally; a queued NPC alive again is dropped ----------

    [Fact]
    public void TheTickSweep_SkipsADownedAlly_AndStillQueuesADeadNpc()
    {
        var downed = DownedNpc("Mira");
        var dead = Npc("Kade", 0);
        InTown(new[] { downed, dead }, () =>
        {
            var sim = new WorldSimulator();
            sim.SetActive(true);
            try { sim.SimulateStep(); } finally { sim.SetActive(false); }
            Queue(sim).Should().ContainKey(dead.Id ?? dead.Name, "an NPC at 0 HP that is not Downed is still queued");
            Queue(sim).Should().NotContainKey(downed.Id ?? downed.Name, "a Downed ally is not dead");
            downed.HP.Should().Be(0, "the sim did not respawn or heal it");
            downed.Gold.Should().Be(1000);
            return 0;
        });
    }

    [Fact]
    public void TheLoadSweep_SkipsADownedAlly()
    {
        var downed = DownedNpc("Mira");
        var dead = Npc("Kade", 0);
        InTown(new[] { downed, dead }, () =>
        {
            var sim = new WorldSimulator();
            sim.ProcessDeadNPCsOnLoad();
            Queue(sim).Should().ContainKey(dead.Id ?? dead.Name);
            Queue(sim).Should().NotContainKey(downed.Id ?? downed.Name);
            return 0;
        });
    }

    [Fact]
    public void Respawn_DropsAQueuedNpcThatIsAliveAgain_AndStillRaisesADeadOne()
    {
        var back = Npc("Mira", 0);
        var dead = Npc("Kade", 0);
        var news = InTown(new[] { back, dead }, () =>
        {
            var sim = new WorldSimulator();
            sim.ProcessDeadNPCsOnLoad();
            Queue(sim).Should().HaveCount(2);
            back.HP = 150;   // up again (a Downed ally recovered at the fight's end)
            for (int i = 0; i < 3; i++) typeof(WorldSimulator).GetMethod("ProcessNPCRespawns", F)!.Invoke(sim, null);
            Queue(sim).Should().BeEmpty();
            return 0;
        });
        back.HP.Should().Be(150, "no full heal for an NPC that is alive");
        back.Gold.Should().Be(1000, "no death penalty");
        back.CurrentLocation.Should().Be("Dungeon", "not moved to Main Street");
        dead.HP.Should().Be(dead.MaxHP, "a dead NPC still respawns");
        dead.Gold.Should().Be(500);
    }

    [Fact]
    public void Respawn_DropsAQueuedNpcThatIsDowned()
    {
        var npc = Npc("Mira", 0);
        InTown(new[] { npc }, () =>
        {
            var sim = new WorldSimulator();
            sim.ProcessDeadNPCsOnLoad();
            npc.Lifecycle = AllyLifecycle.Downed;
            for (int i = 0; i < 3; i++) typeof(WorldSimulator).GetMethod("ProcessNPCRespawns", F)!.Invoke(sim, null);
            Queue(sim).Should().BeEmpty();
            npc.HP.Should().Be(0, "a Downed ally is left to its fight");
            return 0;
        });
    }

    // ---------- 19. the paid resurrection lists exclude a Downed NPC ----------

    private static string RunLocation(BaseLocation loc, Character hero, string method, string script)
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new ScriptedStream(script), output);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(loc, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(loc, hero);
        var t = (Task)loc.GetType().GetMethod(method, F)!.Invoke(loc, null)!;
        t.GetAwaiter().GetResult();
        term.StreamWriterInternal?.Flush();
        return Encoding.UTF8.GetString(output.ToArray());
    }

    [Fact]
    public void TeamCorner_ListsADeadMember_NotADownedOne()
    {
        var hero = Hero(); hero.Team = "PS133 Wolves"; hero.Gold = 0;
        var downed = DownedNpc("Mira", hero.Team);
        string onlyDowned = InTown(new[] { downed }, () => RunLocation(new TeamCornerLocation(), hero, "ResurrectTeammate", "\n\n\n"));
        onlyDowned.Should().Contain(Loc.Get("team.all_alive"), "a Downed ally is no one to raise");
        onlyDowned.Should().NotContain(Loc.Get("team.dead_members_header"));

        var dead = Npc("Kade", 0, hero.Team); dead.IsDead = true;
        string withDead = InTown(new NPC[] { DownedNpc("Mira", hero.Team), dead }, () => RunLocation(new TeamCornerLocation(), hero, "ResurrectTeammate", "\n\n\n"));
        withDead.Should().Contain("Kade", "a dead member is still listed");
        withDead.Should().NotContain("Mira");
    }

    [Fact]
    public void Home_ListsADeadSpouse_NotADownedOne()
    {
        var hero = Hero(); hero.Gold = 0;
        var romance = RomanceTracker.Instance;
        var savedSpouses = romance.Spouses.ToList();
        var savedLovers = romance.CurrentLovers.ToList();
        try
        {
            var downed = DownedNpc("Mira");
            var dead = Npc("Kade", 0); dead.IsDead = true;
            romance.Spouses.Clear(); romance.CurrentLovers.Clear();
            romance.Spouses.Add(new Spouse { NPCId = downed.ID!, NPCName = downed.Name2 });
            string onlyDowned = InTown(new[] { downed }, () => RunLocation(new HomeLocation(), hero, "ResurrectAlly", "\n\n\n"));
            onlyDowned.Should().NotContain("Mira", "a Downed spouse is no one to raise");
            romance.Spouses.Add(new Spouse { NPCId = dead.ID!, NPCName = dead.Name2 });
            string withDead = InTown(new NPC[] { downed, dead }, () => RunLocation(new HomeLocation(), hero, "ResurrectAlly", "\n\n\n"));
            withDead.Should().Contain("Kade");
            withDead.Should().NotContain("Mira");
        }
        finally
        {
            romance.Spouses.Clear(); romance.Spouses.AddRange(savedSpouses);
            romance.CurrentLovers.Clear(); romance.CurrentLovers.AddRange(savedLovers);
        }
    }

    // ---------- section 3: the NPC HP writers save a Downed ally alive ----------

    [Fact]
    public void TheNpcWriters_SaveADownedAllyAtOneHp_AndADeadOneAtZero()
    {
        var downed = DownedNpc("Mira");
        var dead = Npc("Kade", 0);
        var rows = InTown(new[] { downed, dead }, () => OnlineStateManager.SerializeCurrentNPCs());
        rows.Single(r => r.Name == "Mira").HP.Should().Be(1, "a restart must not read a Downed ally as dead");
        rows.Single(r => r.Name == "Kade").HP.Should().Be(0);
        string src = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts/Systems/SaveSystem.cs"));
        src.Should().Contain("HP = npc.IsDowned ? Math.Max(1, npc.HP) : npc.HP,", "the single player writer does the same");
    }

    // ---------- 20. GMCP Char.Combat.Party carries downed ----------

    [Fact]
    public void Gmcp_PartyFrame_CarriesDowned_AndAChangeOfItEmits()
    {
        var saved = SessionContext.Current;
        var stream = new MemoryStream();
        SessionContext.Current = new SessionContext { OutputStream = stream, GmcpEnabled = true };
        try
        {
            var ally = Ally("Mira", hp: 0);
            var party = new List<Character> { ally };
            GmcpBridge.EmitCombatPartyIfChanged(party);
            string first = Encoding.UTF8.GetString(stream.ToArray());
            first.Should().Contain("\"downed\":false").And.Contain("\"isAlive\":false");
            long len = stream.Length;
            ally.Lifecycle = AllyLifecycle.Downed;   // HP unchanged: only the downed flag moves
            GmcpBridge.EmitCombatPartyIfChanged(party);
            stream.Length.Should().BeGreaterThan(len, "a change of downed alone emits a frame");
            Encoding.UTF8.GetString(stream.ToArray()[(int)len..]).Should().Contain("\"downed\":true");
            len = stream.Length;
            GmcpBridge.EmitCombatPartyIfChanged(party);
            stream.Length.Should().Be(len, "nothing changed");
        }
        finally { SessionContext.Current = saved; }
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "usurper-reloaded.csproj"))) d = d.Parent;
        return d!.FullName;
    }
}
