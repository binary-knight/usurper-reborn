using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake.BBS;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.8 (H2): the recruit pool. Rows 1 to 8, 10 and 11 of the brief. Every scenario runs in a copy of the
/// game of its own (IsolatedGame), with seeded random numbers and a world built in the test; no live data.
/// Time passes in these tests by setting every date back (RecruitPoolWorld.Elapse), which is what the clock
/// moving forward looks like to the simulator. The expected numbers are literals, not the GameConfig
/// constants, so a changed constant shows here. Rows 9 and 12 are measurements (RecruitPool128Measure).
/// </summary>
[Collection("SharedGameSingletons")]
public class RecruitPool128Tests
{
    private static void Run(string scenario, string arg = "") => IsolatedGame.Run(scenario, arg);

    [Fact] public void Row1_AnEmptyPool_FillsEachBandToSix_AndStops() => Run(nameof(RecruitPoolRuns.Row1));
    [Fact] public void Row2_OneArrivalAtMost_PerTwentyTicks() => Run(nameof(RecruitPoolRuns.Row2));
    [Fact] public void Row3_TheBandFurthestUnder_IsServedFirst_AtALevelInsideIt() => Run(nameof(RecruitPoolRuns.Row3));
    [Fact] public void Row4_ThePoolFillsTheTownTo190_AndNoFurther() => Run(nameof(RecruitPoolRuns.Row4));
    [Fact] public void Row4_LeaversCountAgainstTheLimit_SoThePoolNeverLiftsTheTownPast190() => Run(nameof(RecruitPoolRuns.Row4Leavers));
    [Fact] public void Row5_APoolArrival_IsRecruitable_AndStandsWhereTheListLooks() => Run(nameof(RecruitPoolRuns.Row5));
    [Fact] public void Row6_APoolMember_EarnsOneFifth_SoloWinAndFlee() => Run(nameof(RecruitPoolRuns.Row6Dungeon));
    [Fact] public void Row6_APoolMember_EarnsOneFifth_Training() => Run(nameof(RecruitPoolRuns.Row6Train));
    [Fact] public void Row6_TheHelper_DecidesByTeamLevelAndSwitch() => Run(nameof(RecruitPoolRuns.Row6Helper));
    [Fact] public void Row7_TheHireList_IsNearestThePlayersLevelFirst_InsideABand() => Run(nameof(RecruitPoolRuns.Row7));
    [Fact] public void Row7_SwitchOff_TheHireListIsLevelDescending() => Run(nameof(RecruitPoolRuns.Row7Off));
    [Fact] public void Row10_APoolArrivalIs18To22_AnOrdinaryImmigrant20To35() => Run(nameof(RecruitPoolRuns.Row10));
    [Fact] public void Row11_OnAPlayersTeam_AnNpcDoesNotAge_ForFortyGameYears() => Run(nameof(RecruitPoolRuns.Row11NoAgeing));
    [Fact] public void Row11_PastItsLifespan_TheHiredNpcSurvives_ItsTwinsDie_Online() => Run(nameof(RecruitPoolRuns.Row11Survives), "online");
    [Fact] public void Row11_PastItsLifespan_TheHiredNpcSurvives_ItsTwinsDie_SinglePlayer() => Run(nameof(RecruitPoolRuns.Row11Survives), "single");
    [Fact] public void Row11_Sacked_ItSurvivesSevenDays_AndDiesAtTheFirstCheckAfter() => Run(nameof(RecruitPoolRuns.Row11Grace));
    [Fact] public void Row11_Rehired_ItKeepsTheAgeItGainedAway() => Run(nameof(RecruitPoolRuns.Row11Rehired));
    [Fact] public void Row11_TheOnlineTeamTable_MakesAPlayersTeam() => Run(nameof(RecruitPoolRuns.Row11OnlineTable));
    [Fact] public void Row11_AnOldSave_LoadsWithNoGrace() => Run(nameof(RecruitPoolRuns.Row11OldSave));
    [Fact] public void Row11_TheStamp_SurvivesTheOnlineSaveAndLoad() => Run(nameof(RecruitPoolRuns.Row11RoundTripOnline));
    [Fact] public void Row11_TheStamp_SurvivesTheSinglePlayerSaveAndLoad() => Run(nameof(RecruitPoolRuns.Row11RoundTripSingle));
    [Fact] public void Row11_SwitchOff_TheHiredNpcDiesAsBefore_AndNoStampIsWritten() => Run(nameof(RecruitPoolRuns.Row11Off));

    [Fact] public void Row14_At189AnArrivalComes_At190None_WithTheRespawnWaitingCounted() => Run(nameof(RecruitPoolRuns.Row14));

    /// <summary>
    /// Row 21: with the town held at 195 the pool adds nobody, and the gates at 200 are as open as at the base:
    /// the same seeded steps start the same pregnancies with the switch on as with it off, a child of 18
    /// becomes an NPC and an orphan comes of age.
    /// </summary>
    [Fact]
    public void Row21_ATownAt195_GetsNoPoolArrival_AndPregnanciesAndComingsOfAgeGoOnAsAtTheBase()
    {
        string on = IsolatedGame.Run(nameof(RecruitPoolRuns.Row21), "on");
        string off = IsolatedGame.Run(nameof(RecruitPoolRuns.Row21), "off");
        on.Should().Be(off);
        on.Should().Contain("poolArrivals=0").And.Contain("pregnant=True").And.Contain("childBecameNpc=True").And.Contain("orphanCameOfAge=True");
    }

    // ---------- the surplus rule (rows 13 and 15) ----------

    /// <summary>The trace of the seeded steps through every join site, made on 4966f1b4, before the surplus rule
    /// existed (its join sites are those of the base 622a752f): what happened and the draws it took.</summary>
    private const string JoinSitesTrace =
        "joined=True draws=5 hash=21e300701d44ff9b Tide One:T,Tide Two:T,Five:T\n" +
        "founded=True draws=4 hash=53de93d66ca70e47 Five:T,Other Five:T\n" +
        "recruited=True draws=3 hash=1b178736a903e129 Recruiter:T,Five:T\n" +
        "worldCreation=True Low 0:T,Low 1:T,Low 2:T,Low 3:T,Low 4:T,Low 5:T,Low 6:T,Low 7:T,Low 8:T,Low 9:T,Low 10:T,Low 11:T\n" +
        "activity=True Member:T,Five:T\n" +
        "gang=True pascalGang=True Ganger:T,Five:T,Second Five:T\n" +
        "stays=True draws=0\n";

    [Fact]
    public void Row13_SwitchOff_EveryJoinSiteDrawsAndEndsAsBeforeTheRule() =>
        IsolatedGame.Run(nameof(RecruitPoolRuns.Row13Joins), "off,5").Should().Be(JoinSitesTrace);

    [Fact]
    public void Row13_Level16_JoinsFoundsAndIsRecruited_AsAtTheBase_WithTheSwitchOn() =>
        IsolatedGame.Run(nameof(RecruitPoolRuns.Row13Joins), "on,16").Should().Be(JoinSitesTrace);

    [Fact] public void Row13_ALowLevelNpc_IsRefusedAtEverySite_WhileItsBandHoldsSixOrFewer_AndDrawsNothing() => Run(nameof(RecruitPoolRuns.Row13Refused));
    [Fact] public void Row13_ARefusedNpc_TakesNoPickFromAnotherWhoMayJoin() => Run(nameof(RecruitPoolRuns.Row13Lists));
    [Fact] public void Row13_AnExistingLowLevelMemberStays_AndAPlayersHireIsUntouched() => Run(nameof(RecruitPoolRuns.Row13StaysAndHire));
    [Fact] public void Row15_ABandAtSeven_LetsOneJoin_AndRefusesAtSix() => Run(nameof(RecruitPoolRuns.Row15));
    [Fact] public void Row15_WhereOneCallCanTakeSeveral_ABandIsNeverTakenBelowSix() => Run(nameof(RecruitPoolRuns.Row15Several));

    [Fact]
    public void Row13_TheSurplusRule_IsOneHelper_AskedAtTheJoinSites_AndNowhereInTheHire()
    {
        int Asks(params string[] file) => Regex.Matches(Code(file), @"MayJoinNpcTeam\(").Count;
        Asks("Systems", "WorldSimulator.cs").Should().Be(6);            // the helper; the joiner or founder; two first-recruit lists; the recruit as it joins; the recruiter's candidates
        Asks("Systems", "WorldInitializerSystem.cs").Should().Be(5);    // leaders (list, as each founds); recruits (list, as each joins); SimulateTeamActivity
        Asks("AI", "EnhancedNPCBehaviors.cs").Should().Be(2);           // the list of three, and as each joins
        Asks("AI", "EnhancedNPCBehaviorSystem.cs").Should().Be(1);      // as each joins
        Asks("Systems", "TeamSystem.cs").Should().Be(0);                // hiring by a player does not ask
        Asks("Locations", "TeamCornerLocation.cs").Should().Be(0);
        var askers = Directory.GetFiles(Path.Combine(RepoRoot(), "Scripts"), "*.cs", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("MayJoinNpcTeam")).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToList();
        askers.Should().Equal("EnhancedNPCBehaviorSystem.cs", "EnhancedNPCBehaviors.cs", "GameConfig.cs", "WorldInitializerSystem.cs", "WorldSimulator.cs");
        // nothing releases a member, and no test static switches the rule
        foreach (string name in new[] { "ReleaseLowLevelNpcTeamMembers", "NoLowLevelNpcTeams", "SurplusJoins" })
            Directory.GetFiles(Path.Combine(RepoRoot(), "Scripts"), "*.cs", SearchOption.AllDirectories)
                .Where(f => File.ReadAllText(f).Contains(name)).Should().BeEmpty(name);
    }

    /// <summary>
    /// Row 8. The golden lines were made by this harness on the base commit 622a752f
    /// (receipts/golden-base-four-parts.txt): seed 1234, 2,000 ticks. The world simulator reads the hour of the
    /// day (the activity weights change at 06, 12, 18 and 23), so there is one golden for each part of the day;
    /// a run that crosses one of those hours is run again.
    /// </summary>
    [Fact]
    public void Row8_SwitchOff_ASeededWorldRun_DrawsTheSameNumbers_AndEndsInTheSameNpcList_AsTheBaseCommit()
    {
        var golden = new Dictionary<string, string>
        {
            ["morning"] = "shared=51063c66f7d39a10 npcs=116 alive=93 roster=e7e77483f557596a simDraws=416365 simHash=e217551507661f24 spawnDraws=3606 spawnHash=7f2428cc4cc9d7cf",
            ["afternoon"] = "shared=f4ad0bdf5b3e4288 npcs=107 alive=90 roster=576c7d06d9cf0f45 simDraws=425994 simHash=baea68649fcd11a3 spawnDraws=3471 spawnHash=aa3c583dce9f78ca",
            ["evening"] = "shared=68bba18688272d89 npcs=109 alive=95 roster=1b15359570f22be3 simDraws=418772 simHash=e4162e8696fc5a48 spawnDraws=3503 spawnHash=dce387b8bc09a2e6",
            ["night"] = "shared=89aed11475c209c7 npcs=112 alive=97 roster=f4cd162c7a0cf8a1 simDraws=405276 simHash=b501826792b16047 spawnDraws=3549 spawnHash=3e403ac3c8564b14",
        };
        // The run reads the wall clock, so it is not part of the always-run suite: without H2_GOLDEN set the
        // test passes without running anything. The receipts of the four forms are taken with it set.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("H2_GOLDEN"))) return;
        string got = "";
        for (int attempt = 0; attempt < 3; attempt++)
        {
            got = IsolatedGame.Run(nameof(RecruitPoolRuns.Golden), "1234,2000");
            if (!got.StartsWith("straddle")) break;
        }
        // H2_GOLDEN_OUT names a file that receives the line of this run (the receipt of the four forms)
        if (Environment.GetEnvironmentVariable("H2_GOLDEN_OUT") is string receipt && receipt.Length > 0) File.AppendAllText(receipt, got + "\n");
        string part = got.Split(' ')[0];
        golden.Should().ContainKey(part);
        got.Should().Be(part + " " + golden[part]);
    }

    [Fact]
    public void Row8_SwitchOn_TheSameRunDiffers_SoTheGoldenSeesTheSwitch()
    {
        string off = IsolatedGame.Run(nameof(RecruitPoolRuns.Golden), "1234,300");
        string on = IsolatedGame.Run(nameof(RecruitPoolRuns.Golden), "1234,300,on");
        if (off.Split(' ')[0] == on.Split(' ')[0]) on.Should().NotBe(off);
    }

    // ---------- source checks ----------

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        return dir!.FullName;
    }

    private static string Code(params string[] parts)
    {
        string src = File.ReadAllText(Path.Combine(new[] { RepoRoot(), "Scripts" }.Concat(parts).ToArray()));
        src = Regex.Replace(src, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Replace(src, @"//[^\n]*", "");
    }

    [Fact]
    public void Row6_EveryExperienceSite_MultipliesThroughTheOneHelper()
    {
        string sim = Code("Systems", "WorldSimulator.cs");
        // the property, and the helper that returns it: nothing else reads the multiplier
        Regex.Matches(sim, @"\bNpcXpMultiplier\b").Count.Should().Be(2);
        sim.Should().Contain("public static float NpcXpMultiplier { get; set; } = 5.0f;");
        sim.Should().Contain("GameConfig.RecruitPoolEnabled && IsRecruitPoolMember(npc) ? 1.0f : NpcXpMultiplier;");
        // six sites: team win, team retreat, solo win (real combat), solo win, solo flee, training
        Regex.Matches(sim, @"\* WorldSimXpMultiplier\((npc|member)\)").Count.Should().Be(6);
        Regex.Matches(sim, @"\bWorldSimXpMultiplier\(").Count.Should().Be(7);
        // no other file multiplies world-simulator experience by it
        var others = Directory.GetFiles(Path.Combine(RepoRoot(), "Scripts"), "*.cs", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f) != "WorldSimulator.cs")
            .Where(f => Regex.IsMatch(Regex.Replace(File.ReadAllText(f), @"//[^\n]*", ""), @"\*\s*(WorldSimulator\.)?NpcXpMultiplier\b"))
            .Select(Path.GetFileName).ToList();
        others.Should().BeEmpty();
    }

    [Fact]
    public void TheSwitch_IsOnByDefault_Internal_AndNotASysopOrBalanceSetting()
    {
        var p = typeof(GameConfig).GetProperty("RecruitPoolEnabled", BindingFlags.Static | BindingFlags.Public)!;
        p.SetMethod!.IsAssembly.Should().BeTrue();
        Code("Core", "GameConfig.cs").Should().Contain("public static bool RecruitPoolEnabled { get; internal set; } = true;");
        var readers = Directory.GetFiles(Path.Combine(RepoRoot(), "Scripts"), "*.cs", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("RecruitPoolEnabled")).Select(Path.GetFileName).OrderBy(n => n).ToList();
        readers.Should().Equal("GameConfig.cs", "TeamCornerLocation.cs", "WorldSimulator.cs");
        GameConfig.RecruitPoolMaxLevel.Should().Be(15);
        GameConfig.RecruitPoolBandTopLevels.Should().Equal(5, 10, 15);
        GameConfig.RecruitPoolPerBand.Should().Be(6);
        GameConfig.RecruitPoolSize.Should().Be(18);
        GameConfig.RecruitPoolTownLimit.Should().Be(190);
        GameConfig.RecruitPoolTicksBetweenArrivals.Should().Be(20);
        GameConfig.RecruitPoolMinAge.Should().Be(18);
        GameConfig.RecruitPoolMaxAge.Should().Be(22);
        GameConfig.RecruitOldAgeGraceDays.Should().Be(7);
        GameConfig.MaxNPCPopulation.Should().Be(200);
    }

    [Fact]
    public void TheMeasurementSeam_IsInternal_OnByDefault_AndReadNowhereElse()
    {
        // run in a copy of the game of its own, so a measurement that turned one off cannot be what is read here
        IsolatedGame.Run(nameof(RecruitPoolRuns.SeamDefaults), "");
        foreach (string name in new[] { "PoolCapCountsWaiting" })
        {
            var p = typeof(WorldSimulator).GetProperty(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
            p.GetMethod!.IsAssembly.Should().BeTrue(name);
            p.SetMethod!.IsAssembly.Should().BeTrue(name);
            // no config file, save, sysop screen or command line reaches it: only the world simulator names it
            var users = Directory.GetFiles(RepoRoot(), "*.*", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                    && !f.Contains(Path.DirectorySeparatorChar + ".git") && !f.Contains("node_modules") && !f.Contains(Path.DirectorySeparatorChar + "Tests" + Path.DirectorySeparatorChar)
                    && (f.EndsWith(".cs") || f.EndsWith(".json") || f.EndsWith(".js") || f.EndsWith(".md")))
                .Where(f => File.ReadAllText(f).Contains(name)).Select(Path.GetFileName).ToList();
            users.Should().Equal(new[] { "WorldSimulator.cs" }, name);
        }
    }

    [Fact]
    public void TheHireListAndTheNameSearch_ReadTheOneLocationList()
    {
        string corner = Code("Locations", "TeamCornerLocation.cs");
        Regex.Matches(corner, @"var townLocations = TeamSystem\.RecruitTownLocations;").Count.Should().Be(2);
        corner.Should().NotContain("\"Auction House\", \"Inn\"");
        TeamSystem.RecruitTownLocations.Should().Equal("Main Street", "Auction House", "Inn", "Temple", "Church",
            "Weapon Shop", "Armor Shop", "Castle", "Bank", "Team Corner");
    }
}

internal static partial class RecruitPoolRuns
{
    private const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly CharacterRace[] Races = Enumerable.Range(0, 10).Select(i => (CharacterRace)i).ToArray();
    private const double HoursPerYear = 9.6;

    private static int _made;

    /// <summary>A plain living NPC; the birth date sits half a year past the birthday, away from a boundary.</summary>
    private static NPC Make(string name, CharacterRace race, int level, string team = "", int age = 30, string location = "Main Street")
    {
        _made++;
        var npc = new NPC
        {
            ID = "npc_h2_" + _made, Id = "h2-" + name, Name1 = name, Name2 = name, Race = race, Sex = CharacterSex.Male,
            Class = CharacterClass.Warrior, Level = level, HP = 500, MaxHP = 500, BaseMaxHP = 500,
            Strength = 30, Defence = 30, Agility = 30, Stamina = 30, Gold = 200, Team = team,
            CurrentLocation = location, AI = CharacterAI.Computer, Age = age,
            BirthDate = DateTime.Now.AddHours(-(age + 0.5) * HoursPerYear),
        };
        NPCSpawnSystem.Instance.ActiveNPCs.Add(npc);
        return npc;
    }

    /// <summary>A quiet world: `perRace` NPCs of each race at level 40, on no team; the ordinary phases add nothing.</summary>
    private static RecruitPoolWorld Quiet(int seed, int perRace, bool online = true)
    {
        SilenceNews();
        var w = new RecruitPoolWorld(seed, online: online, classic: false);
        foreach (var race in Races)
            for (int i = 0; i < perRace; i++) Make($"Elder {race} {i}", race, 40);
        return w;
    }

    /// <summary>One world tick of the two immigration steps only (no NPC acts, so no level or team changes).</summary>
    private static void ImmigrationTick(RecruitPoolWorld w)
    {
        w.Call("ProcessNPCImmigration");
        w.Call("ProcessRecruitPoolTopUp");
        w.Tick++;
    }

    private static int[] Bands(RecruitPoolWorld w)
    {
        var bands = new int[3];
        foreach (var n in w.Npcs.Where(WorldSimulator.IsRecruitPoolMember))
            bands[n.Level <= 5 ? 0 : n.Level <= 10 ? 1 : 2]++;
        return bands;
    }

    // ---------- row 1 ----------

    public static string Row1(string _)
    {
        using var w = Quiet(11, 6);
        Bands(w).Should().Equal(0, 0, 0);
        int before = w.Npcs.Count;
        for (int tick = 0; tick < 1200; tick++)
        {
            ImmigrationTick(w);
            Bands(w).Should().OnlyContain(c => c <= 6, "never seven in a band (tick {0})", tick);
        }
        Bands(w).Should().Equal(6, 6, 6);
        (w.Npcs.Count - before).Should().Be(18);
        // the eighteen fill in 17 intervals of 20 ticks: the last arrives at tick 340, about 3 hours at 30 seconds
        return "ok";
    }

    // ---------- row 2 ----------

    public static string Row2(string _)
    {
        using var w = Quiet(12, 6);
        var arrivals = new List<int>();
        for (int tick = 0; tick < 400; tick++)
        {
            int before = w.Npcs.Count;
            ImmigrationTick(w);
            (w.Npcs.Count - before).Should().BeLessThanOrEqualTo(1);
            if (w.Npcs.Count > before) arrivals.Add(tick);
        }
        arrivals.Should().Equal(Enumerable.Range(0, 18).Select(i => i * 20));
        return "ok";
    }

    // ---------- row 3 ----------

    public static string Row3(string _)
    {
        using var w = Quiet(13, 6);
        // band 1 to 5 holds five, the other two hold two each: the tie goes to the lower band (6 to 10)
        for (int i = 0; i < 5; i++) Make($"Low {i}", Races[i], 3);
        for (int i = 0; i < 2; i++) Make($"Mid {i}", Races[i], 8);
        for (int i = 0; i < 2; i++) Make($"High {i}", Races[i], 13);
        var known = new HashSet<NPC>(w.Npcs);
        var served = new List<int>();
        for (int tick = 0; tick < 400; tick++)
        {
            ImmigrationTick(w);
            foreach (var n in w.Npcs.Where(n => known.Add(n)))
            {
                n.Level.Should().BeInRange(1, 15);
                served.Add(n.Level <= 5 ? 0 : n.Level <= 10 ? 1 : 2);
            }
        }
        // 5/2/2: 6-10, 11-15, 6-10, 11-15, 6-10, 11-15 (now 5/5/5: a three-way tie, the lowest band), 6-10, 11-15
        served.Should().Equal(1, 2, 1, 2, 1, 2, 0, 1, 2);
        Bands(w).Should().Equal(6, 6, 6);

        // levels inside the band, every level of a band drawn, across many arrivals
        var seen = new HashSet<int>();
        for (int round = 0; round < 40; round++)
        {
            foreach (var n in w.Npcs.Where(WorldSimulator.IsRecruitPoolMember).ToList())
                if (n.Level >= 6 && n.Level <= 10) w.Npcs.Remove(n);
            var before = new HashSet<NPC>(w.Npcs);
            for (int tick = 0; tick < 120; tick++) ImmigrationTick(w);
            foreach (var n in w.Npcs.Where(n => !before.Contains(n)))
            {
                n.Level.Should().BeInRange(6, 10);
                seen.Add(n.Level);
            }
        }
        seen.Should().BeEquivalentTo(new[] { 6, 7, 8, 9, 10 });
        return "ok";
    }

    // ---------- row 4 ----------

    public static string Row4(string _)
    {
        // 180 alive, 18 of each race: the ordinary phases have nothing to do; the pool adds ten and stops at 190
        SilenceNews();
        using var w = new RecruitPoolWorld(14, classic: false);
        foreach (var race in Races)
            for (int i = 0; i < 18; i++) Make($"Elder {race} {i}", race, 40);
        w.Alive().Count.Should().Be(180);
        var elders = new HashSet<NPC>(w.Npcs);

        for (int tick = 0; tick < 1000; tick++) ImmigrationTick(w);
        var arrived = w.Npcs.Where(n => !elders.Contains(n)).ToList();
        arrived.Should().HaveCount(10);
        arrived.Should().OnlyContain(n => n.Level >= 1 && n.Level <= 15);
        w.Alive().Count.Should().Be(190);
        Bands(w).Sum().Should().Be(10, "the pool is under its eighteen, and the town limit comes first");

        // 190 with places free in the pool: nothing arrives
        foreach (var n in arrived.Take(5)) n.Level = 16;
        for (int tick = 0; tick < 300; tick++) ImmigrationTick(w);
        w.Npcs.Count.Should().Be(190);

        // 189: one arrives
        var gone = elders.First();
        gone.HP = 0; gone.IsDead = true; gone.IsPermaDead = true;
        for (int tick = 0; tick < 300; tick++) ImmigrationTick(w);
        w.Alive().Count.Should().Be(190);
        w.Npcs.Count.Should().Be(191);

        // a town already at 200 and at 218 gets nobody
        for (int i = 0; i < 10; i++) Make("Late Elder " + i, Races[i], 40);
        w.Alive().Count.Should().Be(200);
        for (int tick = 0; tick < 100; tick++) ImmigrationTick(w);
        w.Alive().Count.Should().Be(200);
        return "ok";
    }

    public static string Row4Leavers(string _)
    {
        // a world whose pool keeps emptying: members level past 15 or join an NPC team, and stay alive
        SilenceNews();
        using var w = new RecruitPoolWorld(15, classic: false);
        foreach (var race in Races)
            for (int i = 0; i < 17; i++) Make($"Elder {race} {i}", race, 40);
        int peak = 0, leavers = 0;
        for (int tick = 0; tick < 4000; tick++)
        {
            ImmigrationTick(w);
            if (tick % 10 == 0)
            {
                var member = w.Npcs.FirstOrDefault(WorldSimulator.IsRecruitPoolMember);
                if (member != null)
                {
                    if (leavers % 2 == 0) member.Level = 16; else member.Team = "The Tidal Tide";
                    leavers++;
                }
            }
            peak = Math.Max(peak, w.Alive().Count);
        }
        // the leavers count: the town stops at 190 and the pool then waits for a death; 200 is never reached
        peak.Should().Be(190);
        w.Alive().Count.Should().Be(190);
        leavers.Should().Be(20);
        Bands(w).Sum().Should().Be(0);
        return "ok";
    }

    // ---------- row 5 ----------

    public static string Row5(string _)
    {
        using var w = Quiet(16, 6);
        var known = new HashSet<NPC>(w.Npcs);
        for (int tick = 0; tick < 400; tick++) ImmigrationTick(w);
        var arrivals = w.Npcs.Where(n => !known.Contains(n)).ToList();
        arrivals.Should().HaveCount(18);
        // and many more straight from the generator, as the top-up calls it
        for (int i = 0; i < 300; i++)
            arrivals.Add(NPCSpawnSystem.Instance.GenerateImmigrantNPC(Races[i % 10], i % 2 == 0 ? CharacterSex.Male : CharacterSex.Female, 1 + i % 15, forRecruitPool: true)!);

        var listed = new[] { "Main Street", "Auction House", "Inn", "Temple", "Church", "Weapon Shop", "Armor Shop", "Castle", "Bank", "Team Corner" };
        var fresh = new Character { Name1 = "Fresh Player", Name2 = "Fresh Player", ID = "fresh_player", Level = 1, HP = 50, MaxHP = 50 };
        foreach (var n in arrivals)
        {
            listed.Should().Contain(n.CurrentLocation);
            TeamSystem.IsRecruitable(fresh, n, out var band).Should().BeTrue();
            band.Should().Be(TeamSystem.RecruitmentBand.Neutral);
            WorldSimulator.IsRecruitPoolMember(n).Should().BeTrue();
        }
        arrivals.Select(n => n.CurrentLocation).Distinct().Should().HaveCount(10);

        // the Team Corner list shows all eighteen at once
        var rig = new TeamCornerRig(fresh, Array.Empty<string>());
        var build = typeof(TeamCornerLocation).GetMethod("BuildRecruitmentCandidates", F)!;
        var rows = ((IEnumerable)build.Invoke(rig.Loc, new object?[] { w.Npcs, null })!).Cast<object>()
            .Select(r => (NPC)r.GetType().GetProperty("Npc")!.GetValue(r)!).ToList();
        rows.Where(n => n.Level <= 15).Should().HaveCount(18);
        return "ok";
    }

    // ---------- row 6 ----------

    private static NPC Twin(RecruitPoolWorld w, int seed, int level, string team, long frailHp = 0)
    {
        // the same NPC every time: the generator and the shared generator are seeded alike before it is made
        NPCNameRegistry.Reset();
        typeof(NPCSpawnSystem).GetField("random", F)!.SetValue(NPCSpawnSystem.Instance, new Random(seed));
        RecruitPoolWorld.SeedSharedRandomOnThisThread(seed);
        var npc = NPCSpawnSystem.Instance.GenerateImmigrantNPC(CharacterRace.Human, CharacterSex.Male, level, forRecruitPool: true)!;
        npc.Team = team;
        npc.Brain!.Personality.Courage = 0.5f;
        npc.Brain.Personality.Ambition = 0.5f;
        if (frailHp > 0)
        {
            // little health and no armour: it hurts the monster and then has to run
            npc.HP = npc.MaxHP = npc.BaseMaxHP = frailHp;
            npc.Defence = 0; npc.BaseDefence = 0; npc.ArmPow = 0; npc.BaseArmPow = 0;
        }
        w.Npcs.Add(npc);
        return npc;
    }

    /// <summary>One solo dungeon visit of a twin; returns (experience gained, gold gained, alive).</summary>
    private static (long Xp, long Gold, bool Alive) Dungeon(RecruitPoolWorld w, int seed, int level, string team, float multiplier, long frailHp = 0)
    {
        var npc = Twin(w, seed, level, team, frailHp);
        WorldSimulator.NpcXpMultiplier = multiplier;
        typeof(WorldSimulator).GetField("random", F)!.SetValue(w.Sim, new Random(seed));
        w.ReseedOtherRandoms(seed);                    // the monster generator has a generator of its own
        RecruitPoolWorld.SeedSharedRandomOnThisThread(seed + 500);
        long xp = npc.Experience, gold = npc.Gold;
        w.Call("NPCExploreDungeon", npc);
        var result = (npc.Experience - xp, npc.Gold - gold, npc.IsAlive && !npc.IsDead);
        w.Npcs.Remove(npc);
        return result;
    }

    public static string Row6Dungeon(string _)
    {
        using var w = Quiet(17, 6);
        int wins = 0, flees = 0;
        Dungeon(w, 999, 15, "", 5f, 300);   // the first visit of a process builds tables as it goes: not compared
        for (int seed = 1; seed <= 400; seed++)
        {
            // even seeds: level 5 as generated; odd seeds: level 15 with little health (200 to 560) and no armour, to see flights
            int level = seed % 2 == 0 ? 5 : 15;
            long frail = seed % 2 == 0 ? 0 : 200 + 40 * (seed / 2 % 10);
            var pool = Dungeon(w, seed, level, "", 5f, frail);            // unteamed, level 1 to 15: a pool member
            var poolAtOne = Dungeon(w, seed, level, "", 1f, frail);
            var teamed = Dungeon(w, seed, level, "The Tidal Tide", 5f, frail);
            var teamedAtOne = Dungeon(w, seed, level, "The Tidal Tide", 1f, frail);
            teamed.Alive.Should().Be(pool.Alive);              // the same fight, the same seed
            pool.Xp.Should().Be(poolAtOne.Xp, "a pool member earns at x1.0 whatever the server multiplier (seed {0})", seed);
            pool.Xp.Should().Be(teamedAtOne.Xp);
            if (pool.Gold > 0 && pool.Xp > 0)
            {
                wins++;
                teamed.Xp.Should().Be(pool.Xp * 5, "a win pays a whole number: one fifth exactly (seed {0})", seed);
            }
            else if (pool.Xp > 0 || teamed.Xp > 0)
            {
                flees++;
                // the flee share is cut to a whole number after the multiplier: within the rounding of one fifth
                Math.Abs(teamed.Xp - pool.Xp * 5).Should().BeLessThan(5);
                teamed.Xp.Should().BeGreaterThan(pool.Xp);
            }
        }
        wins.Should().BeGreaterThan(100);
        flees.Should().BeGreaterThan(20);

        // level 16, on no team: the full multiplier
        int sixteens = 0;
        for (int seed = 1; seed <= 150; seed++)
        {
            var atFive = Dungeon(w, seed, 16, "", 5f);
            var atOne = Dungeon(w, seed, 16, "", 1f);
            if (atOne.Gold > 0 && atOne.Xp > 0) { sixteens++; atFive.Xp.Should().Be(atOne.Xp * 5); }
        }
        sixteens.Should().BeGreaterThan(10);
        return "ok";
    }

    private static long Train(RecruitPoolWorld w, int seed, int level, string team, float multiplier)
    {
        var npc = Twin(w, seed, level, team);
        WorldSimulator.NpcXpMultiplier = multiplier;
        RecruitPoolWorld.SeedSharedRandomOnThisThread(seed + 900);
        long xp = npc.Experience;
        w.Call("ExecuteTrain", npc);
        w.Npcs.Remove(npc);
        return npc.Experience - xp;
    }

    public static string Row6Train(string _)
    {
        using var w = Quiet(18, 6);
        int trained = 0;
        for (int seed = 1; seed <= 200; seed++)
        {
            long pool = Train(w, seed, 5, "", 5f);
            long teamed = Train(w, seed, 5, "The Tidal Tide", 5f);
            long sixteen = Train(w, seed, 16, "", 5f);
            teamed.Should().Be(pool * 5);
            sixteen.Should().Be(pool * 5);
            if (pool > 0) { trained++; pool.Should().BeInRange(10, 30); }
        }
        trained.Should().BeGreaterThan(30);
        return "ok";
    }

    public static string Row6Helper(string _)
    {
        using var w = Quiet(19, 1);
        WorldSimulator.NpcXpMultiplier = 5f;
        float Of(Action<NPC> change, int level = 5)
        {
            var npc = Make("Helper " + _made, CharacterRace.Human, level);
            change(npc);
            return (float)typeof(WorldSimulator).GetMethod("WorldSimXpMultiplier", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { npc })!;
        }
        Of(n => { }).Should().Be(1f);
        Of(n => { }, 1).Should().Be(1f);
        Of(n => { }, 15).Should().Be(1f);
        Of(n => { }, 16).Should().Be(5f);
        Of(n => n.Team = "The Tidal Tide").Should().Be(5f);            // an NPC team
        WorldSimulator.RegisterPlayerTeam("Helper Hall");
        Of(n => n.Team = "Helper Hall").Should().Be(5f);               // hired
        Of(n => n.DaysInPrison = 2).Should().Be(5f);                   // not a pool member: not hireable
        Of(n => n.IsSpecialNPC = true).Should().Be(5f);
        SetSwitch(false);
        Of(n => { }).Should().Be(5f);
        SetSwitch(true);
        return "ok";
    }

    // ---------- row 7 ----------

    private static void SetScore(Character a, Character b, int score)
    {
        var rec = RelationshipSystem.GetOrCreateRelationship(a, b);
        if (rec.Name1 == a.Name) rec.Relation1 = score; else rec.Relation2 = score;
    }

    private static List<NPC> HireList(Character player, List<NPC> npcs)
    {
        var rig = new TeamCornerRig(player, Array.Empty<string>());
        var build = typeof(TeamCornerLocation).GetMethod("BuildRecruitmentCandidates", F)!;
        return ((IEnumerable)build.Invoke(rig.Loc, new object?[] { npcs, null })!).Cast<object>()
            .Select(r => (NPC)r.GetType().GetProperty("Npc")!.GetValue(r)!).ToList();
    }

    private static (Character Player, List<NPC> Npcs, NPC Friend) Row7World(RecruitPoolWorld w)
    {
        var player = new Character { Name1 = "List Reader", Name2 = "List Reader", ID = "list_reader", Level = 6, HP = 80, MaxHP = 80 };
        var npcs = new List<NPC>();
        foreach (int level in new[] { 26, 9, 4, 7, 5 }) npcs.Add(Make($"Neutral L{level}", CharacterRace.Human, level));
        var friend = Make("Friend L26", CharacterRace.Human, 26);
        SetScore(player, friend, 40);   // the Friendship tier
        npcs.Add(friend);
        return (player, npcs, friend);
    }

    public static string Row7(string _)
    {
        using var w = Quiet(20, 0);
        var (player, npcs, friend) = Row7World(w);
        TeamSystem.GetRecruitmentBand(player, friend).Item1.Should().Be(TeamSystem.RecruitmentBand.Friend);
        // the band first, then 5 and 7 (one level away, the lower first), 4, 9, and 26 last
        HireList(player, npcs).Select(n => n.Name2).Should().Equal(
            "Friend L26", "Neutral L5", "Neutral L7", "Neutral L4", "Neutral L9", "Neutral L26");

        // thirty more with many ties: the order is fixed by distance, level and stored name, so a page of
        // twelve is the same twelve however often the list is built, and page two follows page one
        var names = new[] { "Mara", "Aldo", "Zed", "Bryn", "Odo", "Cai" };
        for (int i = 0; i < 30; i++) npcs.Add(Make($"{names[i % 6]} {(char)('z' - i % 5)}{i}", CharacterRace.Human, 3 + i % 7));
        var first = HireList(player, npcs);
        var reversed = HireList(player, Enumerable.Reverse(npcs).ToList());
        first.Should().HaveCount(36);
        reversed.Select(n => n.Name2).Should().Equal(first.Select(n => n.Name2), "the order does not depend on the roster order");
        var neutral = first.Skip(1).ToList();
        for (int i = 1; i < neutral.Count; i++)
        {
            var a = neutral[i - 1]; var b = neutral[i];
            int da = Math.Abs(a.Level - 6), db = Math.Abs(b.Level - 6);
            bool ordered = da < db || (da == db && (a.Level < b.Level || (a.Level == b.Level && string.CompareOrdinal(a.Name2, b.Name2) < 0)));
            ordered.Should().BeTrue("row {0} ({1} L{2}) comes before row {3} ({4} L{5}), also across the page break at 12", i, a.Name2, a.Level, i + 1, b.Name2, b.Level);
        }
        first.Skip(12).Take(12).Should().Equal(HireList(player, npcs).Skip(12).Take(12));
        return "ok";
    }

    public static string Row7Off(string _)
    {
        using var w = Quiet(21, 0);
        SetSwitch(false);
        var (player, npcs, _) = Row7World(w);
        HireList(player, npcs).Select(n => n.Name2).Should().Equal(
            "Friend L26", "Neutral L26", "Neutral L9", "Neutral L7", "Neutral L5", "Neutral L4");
        SetSwitch(true);
        return "ok";
    }

    // ---------- row 10 ----------

    public static string Row10(string _)
    {
        using var w = Quiet(22, 6);
        var known = new HashSet<NPC>(w.Npcs);
        for (int tick = 0; tick < 400; tick++) ImmigrationTick(w);
        var pool = w.Npcs.Where(n => !known.Contains(n)).ToList();
        pool.Should().HaveCount(18);
        for (int i = 0; i < 400; i++)
            pool.Add(NPCSpawnSystem.Instance.GenerateImmigrantNPC(Races[i % 10], CharacterSex.Female, 1 + i % 15, forRecruitPool: true)!);
        foreach (var n in pool)
        {
            n.Age.Should().BeInRange(18, 22);
            double years = (DateTime.Now - n.BirthDate).TotalHours / HoursPerYear;
            years.Should().BeInRange(n.Age - 0.001, n.Age + 0.01, "the birth date matches the age");
        }
        pool.Select(n => n.Age).Distinct().OrderBy(a => a).Should().Equal(18, 19, 20, 21, 22);

        var ordinary = new List<NPC>();
        for (int i = 0; i < 800; i++)
            ordinary.Add(NPCSpawnSystem.Instance.GenerateImmigrantNPC(Races[i % 10], CharacterSex.Male, 20)!);
        ordinary.Should().OnlyContain(n => n.Age >= 20 && n.Age <= 35);
        ordinary.Min(n => n.Age).Should().Be(20);
        ordinary.Max(n => n.Age).Should().Be(35);
        foreach (var n in ordinary.Take(50))
            ((DateTime.Now - n.BirthDate).TotalHours / HoursPerYear).Should().BeInRange(n.Age - 0.001, n.Age + 0.01);

        // the ordinary phases still call the old rule: a race under five gets immigrants aged 20 to 35 at the average level
        var before = new HashSet<NPC>(w.Npcs);
        foreach (var n in w.Npcs.Where(n => n.Race == CharacterRace.Elf && n.Level == 40).Skip(1).ToList()) w.Npcs.Remove(n);
        w.Call("ProcessNPCImmigration");
        var phaseOne = w.Npcs.Where(n => !before.Contains(n)).ToList();
        phaseOne.Should().NotBeEmpty();
        phaseOne.Should().OnlyContain(n => n.Race == CharacterRace.Elf && n.Age >= 20 && n.Level > 15);
        return "ok";
    }

    // ---------- row 11 ----------

    private const string PlayerTeam = "Wardens of the Hall";
    private const string NpcTeam = "The Tidal Tide";

    /// <summary>A world for the ageing check: young Gnolls so the race floor (3) is far, and the news kept.</summary>
    private static (RecruitPoolWorld W, List<string> News) AgeWorld(int seed, bool online = true, int fillerAge = 20)
    {
        var news = new List<string>();
        NewsSystem.Instance.SetCatchUpBuffer(news);
        var w = new RecruitPoolWorld(seed, online: online, classic: false);
        for (int i = 0; i < 8; i++) Make($"Young Gnoll {i}", CharacterRace.Gnoll, 40, age: fillerAge);
        WorldSimulator.RegisterPlayerTeam(PlayerTeam);
        return (w, news);
    }

    private static void AgeCheck(RecruitPoolWorld w) => w.Call("ProcessNPCAging");
    private static bool Living(NPC n) => n.IsAlive && !n.IsDead && !n.IsAgedDeath;
    private static readonly TimeSpan GameYear = TimeSpan.FromHours(HoursPerYear);

    public static string Row11NoAgeing(string _)
    {
        var (w, news) = AgeWorld(23);
        using var world = w;
        // a Gnoll lives 50 years. All three are 30.
        var hired = Make("Hired Gnoll", CharacterRace.Gnoll, 12, team: PlayerTeam, age: 30);
        var onNpcTeam = Make("Tide Gnoll", CharacterRace.Gnoll, 12, team: NpcTeam, age: 30);
        var unteamed = Make("Lone Gnoll", CharacterRace.Gnoll, 12, age: 30);
        AgeCheck(w);                                   // the first check after hiring only stamps
        hired.LastOnPlayerTeamAt.Should().NotBeNull();
        onNpcTeam.LastOnPlayerTeamAt.Should().BeNull();
        unteamed.LastOnPlayerTeamAt.Should().BeNull();
        var born = hired.BirthDate;

        // forty game years of checks, ten a year
        for (int check = 0; check < 400; check++)
        {
            w.Elapse(GameYear / 10);
            AgeCheck(w);
            hired.Age.Should().Be(30, "check {0}", check);
            Living(hired).Should().BeTrue();
        }
        // forty years of clock have passed for the world and none for the hired NPC: its birth date kept its
        // distance from now (here: its value, since the test moves the dates and not the clock)
        (hired.BirthDate - born).Duration().Should().BeLessThan(TimeSpan.FromSeconds(5));
        news.Should().NotContain(line => line.Contains("Hired Gnoll"));

        // the twins aged: birthdays in the news until 50, then death of old age
        onNpcTeam.IsAgedDeath.Should().BeTrue();
        unteamed.IsAgedDeath.Should().BeTrue();
        news.Count(line => line.Contains("Lone Gnoll") && line.Contains("31")).Should().Be(1);
        news.Count(line => line.Contains("Tide Gnoll") && line.Contains("49")).Should().Be(1);
        NewsSystem.Instance.ClearCatchUpBuffer();
        return "ok";
    }

    public static string Row11Survives(string mode)
    {
        var (w, news) = AgeWorld(24, online: mode == "online");
        using var world = w;
        DoorMode.IsOnlineMode.Should().Be(mode == "online");
        // already past the Gnoll's 50 years when the check first sees them
        var hired = Make("Hired Gnoll", CharacterRace.Gnoll, 12, team: PlayerTeam, age: 55);
        var onNpcTeam = Make("Tide Gnoll", CharacterRace.Gnoll, 12, team: NpcTeam, age: 55);
        var unteamed = Make("Lone Gnoll", CharacterRace.Gnoll, 12, age: 55);
        var poolMember = Make("Pool Gnoll", CharacterRace.Gnoll, 3, age: 55);
        WorldSimulator.IsOnPlayerTeam(hired).Should().BeTrue();
        WorldSimulator.IsOnPlayerTeam(onNpcTeam).Should().BeFalse();
        WorldSimulator.IsOnPlayerTeam(unteamed).Should().BeFalse();
        WorldSimulator.IsOnPlayerTeam(null).Should().BeFalse();
        WorldSimulator.IsRecruitPoolMember(poolMember).Should().BeTrue();

        AgeCheck(w);
        Living(hired).Should().BeTrue();
        onNpcTeam.IsAgedDeath.Should().BeTrue();
        unteamed.IsAgedDeath.Should().BeTrue();
        poolMember.IsAgedDeath.Should().BeTrue("a pool member dies of old age as before");
        for (int check = 0; check < 50; check++) { w.Elapse(GameYear); AgeCheck(w); }
        Living(hired).Should().BeTrue();
        hired.Age.Should().Be(55);
        NewsSystem.Instance.ClearCatchUpBuffer();
        return "ok";
    }

    public static string Row11Grace(string _)
    {
        var (w, news) = AgeWorld(25, fillerAge: 2);   // 14 days are 35 game years: the young must outlive the test
        using var world = w;
        var hired = Make("Hired Gnoll", CharacterRace.Gnoll, 12, team: PlayerTeam, age: 55);
        AgeCheck(w);
        w.Elapse(TimeSpan.FromHours(1)); AgeCheck(w);
        hired.Age.Should().Be(55);

        hired.Team = "";                               // sacked (any route: the check only sees the team gone)
        // a check every 2 hours for 6 days 22 hours: it survives each one, and it ages again
        for (int check = 0; check < 83; check++)
        {
            w.Elapse(TimeSpan.FromHours(2));
            AgeCheck(w);
            Living(hired).Should().BeTrue("check {0}, {1} hours after it left", check, (check + 1) * 2);
        }
        hired.Age.Should().Be(55 + 17);                // 166 hours at 9.6 hours a year
        w.Elapse(TimeSpan.FromHours(1.9)); AgeCheck(w);   // 6 days 23.9 hours
        Living(hired).Should().BeTrue();
        w.Elapse(TimeSpan.FromHours(0.2)); AgeCheck(w);   // 7 days 0.1 hours: the first check after the grace
        hired.IsAgedDeath.Should().BeTrue();
        hired.IsDead.Should().BeTrue();

        // the grace is the same when the player's team is dissolved under it (the name leaves the list)
        var second = Make("Second Gnoll", CharacterRace.Gnoll, 12, team: PlayerTeam, age: 55);
        AgeCheck(w);
        WorldSimulator.UnregisterPlayerTeam(PlayerTeam);
        w.Elapse(TimeSpan.FromDays(6.9)); AgeCheck(w);
        Living(second).Should().BeTrue();
        w.Elapse(TimeSpan.FromDays(0.2)); AgeCheck(w);
        second.IsAgedDeath.Should().BeTrue();
        NewsSystem.Instance.ClearCatchUpBuffer();
        return "ok";
    }

    public static string Row11Rehired(string _)
    {
        var (w, news) = AgeWorld(26);
        using var world = w;
        var hired = Make("Hired Gnoll", CharacterRace.Gnoll, 12, team: PlayerTeam, age: 30);
        AgeCheck(w);
        w.Elapse(GameYear * 3); AgeCheck(w);
        hired.Age.Should().Be(30);
        hired.Team = "";
        w.Elapse(GameYear * 4); AgeCheck(w);            // four years away: it ages
        hired.Age.Should().Be(34);
        hired.Team = PlayerTeam;                        // hired again
        w.Elapse(GameYear * 2); AgeCheck(w);            // the first check after hiring only stamps: two more years
        hired.Age.Should().Be(36, "the years away are kept: the old stamp must not move the birth date");
        w.Elapse(GameYear * 5); AgeCheck(w);
        hired.Age.Should().Be(36);

        // after a restart the process has not seen it yet: the time the server was down does not age a hired NPC
        hired.SeenOnPlayerTeamAtLastAgeCheck = null;
        w.Elapse(GameYear * 2); AgeCheck(w);
        hired.Age.Should().Be(36);
        NewsSystem.Instance.ClearCatchUpBuffer();
        return "ok";
    }

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, IncludeFields = true };

    private static string TempDb() => Path.Combine(Path.GetTempPath(), $"usurper-h2-{Guid.NewGuid():N}.db");

    public static string Row11OnlineTable(string _)
    {
        // online: the world simulator learns the players' teams from the player_teams table
        var (w, news) = AgeWorld(27);
        using var world = w;
        WorldSimulator.UnregisterPlayerTeam(PlayerTeam);
        string path = TempDb();
        try
        {
            var db = new SqlSaveBackend(path);
            db.CreatePlayerTeam("Table Team", "hash", "a player").GetAwaiter().GetResult().Should().BeTrue();
            var hired = Make("Table Gnoll", CharacterRace.Gnoll, 12, team: "Table Team", age: 55);
            var other = Make("Tide Gnoll", CharacterRace.Gnoll, 12, team: NpcTeam, age: 55);
            WorldSimulator.IsOnPlayerTeam(hired).Should().BeFalse();
            var service = new WorldSimService(db);
            service.RefreshPlayerTeamNames().Should().BeTrue();   // v1.2.8 (F5): the start load is the refresh
            WorldSimulator.IsOnPlayerTeam(hired).Should().BeTrue();
            WorldSimulator.IsOnPlayerTeam(other).Should().BeFalse();
            AgeCheck(w);
            Living(hired).Should().BeTrue();
            other.IsAgedDeath.Should().BeTrue();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch { }
            NewsSystem.Instance.ClearCatchUpBuffer();
        }
        return "ok";
    }

    private static List<NPCData> ThroughJson(List<NPCData> data) =>
        JsonSerializer.Deserialize<List<NPCData>>(JsonSerializer.Serialize(data, Json), Json)!;

    private static void RestoreOnline(List<NPCData> data, string dbPath)
    {
        var service = new WorldSimService(new SqlSaveBackend(dbPath));
        typeof(WorldSimService).GetMethod("RestoreNPCsFromData", F)!.Invoke(service, new object[] { data });
    }

    public static string Row11OldSave(string _)
    {
        var (w, news) = AgeWorld(28);
        using var world = w;
        var old = Make("Old Save Gnoll", CharacterRace.Gnoll, 12, age: 55);
        old.EnsureSystemsInitialized();
        foreach (var n in w.Npcs) n.EnsureSystemsInitialized();

        // a save written before 1.2.8 has no such property at all
        string json = JsonSerializer.Serialize(OnlineStateManager.SerializeCurrentNPCs(), Json);
        json.Should().Contain("\"lastOnPlayerTeamAt\":null", "the field is empty by default");
        string before128 = json.Replace("\"lastOnPlayerTeamAt\":null,", "");
        before128.Should().NotContain("lastOnPlayerTeamAt");
        string path = TempDb();
        try
        {
            foreach (string loader in new[] { "single-player", "online" })
            {
                var data = JsonSerializer.Deserialize<List<NPCData>>(before128, Json)!;
                w.Npcs.Clear();
                if (loader == "online") RestoreOnline(data, path);
                else GameEngine.Instance.RestoreNPCs(data).GetAwaiter().GetResult();
                var loaded = w.Npcs.Single(n => n.Name2 == "Old Save Gnoll");
                loaded.LastOnPlayerTeamAt.Should().BeNull(loader);
                loaded.SeenOnPlayerTeamAtLastAgeCheck.Should().BeNull();
                AgeCheck(w);
                loaded.IsAgedDeath.Should().BeTrue("no stamp, no grace: it dies of old age as before ({0})", loader);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch { }
            NewsSystem.Instance.ClearCatchUpBuffer();
        }
        return "ok";
    }

    private static (RecruitPoolWorld W, NPC Stamped, NPC Plain, DateTime Stamp) RoundTripWorld(int seed, bool online)
    {
        var (w, _) = AgeWorld(seed, online);
        var stamp = DateTime.UtcNow.AddDays(-3);
        var stamped = Make("Stamped Gnoll", CharacterRace.Gnoll, 12, age: 55);
        stamped.LastOnPlayerTeamAt = stamp;
        var plain = Make("Plain Gnoll", CharacterRace.Gnoll, 12, age: 30);
        foreach (var n in w.Npcs) n.EnsureSystemsInitialized();
        return (w, stamped, plain, stamp);
    }

    public static string Row11RoundTripOnline(string _)
    {
        var (w, stamped, plain, stamp) = RoundTripWorld(29, online: true);
        using var world = w;
        string path = TempDb();
        try
        {
            var saved = ThroughJson(OnlineStateManager.SerializeCurrentNPCs());        // the online writer
            saved.Single(d => d.Name == "Stamped Gnoll").LastOnPlayerTeamAt.Should().Be(stamp);
            saved.Single(d => d.Name == "Plain Gnoll").LastOnPlayerTeamAt.Should().BeNull();
            w.Npcs.Clear();
            RestoreOnline(saved, path);                                                // the world simulator's loader
            w.Npcs.Single(n => n.Name2 == "Stamped Gnoll").LastOnPlayerTeamAt.Should().Be(stamp);
            w.Npcs.Single(n => n.Name2 == "Plain Gnoll").LastOnPlayerTeamAt.Should().BeNull();
            w.Npcs.Clear();
            GameEngine.Instance.RestoreNPCs(ThroughJson(saved)).GetAwaiter().GetResult();   // a player session's loader
            var loaded = w.Npcs.Single(n => n.Name2 == "Stamped Gnoll");
            loaded.LastOnPlayerTeamAt.Should().Be(stamp);

            // the loaded stamp is read: seven days after it the NPC, past its lifespan, dies at the next check
            w.Elapse(TimeSpan.FromDays(3.9));   // now the stamp is 6.9 days old
            AgeCheck(w);
            Living(loaded).Should().BeTrue();
            w.Elapse(TimeSpan.FromDays(0.2)); AgeCheck(w);
            loaded.IsAgedDeath.Should().BeTrue();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch { }
            NewsSystem.Instance.ClearCatchUpBuffer();
        }
        return "ok";
    }

    public static string Row11RoundTripSingle(string _)
    {
        var (w, stamped, plain, stamp) = RoundTripWorld(30, online: false);
        using var world = w;
        DoorMode.IsOnlineMode.Should().BeFalse();
        var task = (Task<List<NPCData>>)typeof(SaveSystem).GetMethod("SerializeNPCs", F)!.Invoke(SaveSystem.Instance, null)!;
        var saved = ThroughJson(task.GetAwaiter().GetResult());                        // the single-player writer
        saved.Single(d => d.Name == "Stamped Gnoll").LastOnPlayerTeamAt.Should().Be(stamp);
        saved.Single(d => d.Name == "Plain Gnoll").LastOnPlayerTeamAt.Should().BeNull();
        w.Npcs.Clear();
        GameEngine.Instance.RestoreNPCs(saved).GetAwaiter().GetResult();               // the single-player loader
        w.Npcs.Single(n => n.Name2 == "Stamped Gnoll").LastOnPlayerTeamAt.Should().Be(stamp);
        w.Npcs.Single(n => n.Name2 == "Plain Gnoll").LastOnPlayerTeamAt.Should().BeNull();
        NewsSystem.Instance.ClearCatchUpBuffer();
        return "ok";
    }

    public static string Row11Off(string _)
    {
        var (w, news) = AgeWorld(31);
        using var world = w;
        SetSwitch(false);
        var hired = Make("Hired Gnoll", CharacterRace.Gnoll, 12, team: PlayerTeam, age: 30);
        var past = Make("Past Gnoll", CharacterRace.Gnoll, 12, team: PlayerTeam, age: 55);
        var graced = Make("Graced Gnoll", CharacterRace.Gnoll, 12, age: 55);
        graced.LastOnPlayerTeamAt = DateTime.UtcNow.AddHours(-1);   // a stamp from when the switch was on
        AgeCheck(w);
        past.IsAgedDeath.Should().BeTrue("off: a hired NPC past its lifespan dies as before 1.2.8");
        graced.IsAgedDeath.Should().BeTrue("off: the stamp is not read");
        hired.LastOnPlayerTeamAt.Should().BeNull("off: the stamp is not written");
        hired.SeenOnPlayerTeamAtLastAgeCheck.Should().BeNull();
        w.Elapse(GameYear * 3); AgeCheck(w);
        hired.Age.Should().Be(33, "off: a hired NPC ages");
        SetSwitch(true);
        NewsSystem.Instance.ClearCatchUpBuffer();
        return "ok";
    }

    // ---------- rows 13 and 14 (the amendment) ----------

    public static string SeamDefaults(string _)
    {
        WorldSimulator.PoolCapCountsWaiting.Should().BeTrue();
        return "ok";
    }

    /// <summary>An NPC with a brain whose traits make it willing to team up and compatible with its like.</summary>
    private static NPC Social(string name, int level, string team = "", string location = "Main Street")
    {
        var npc = Make(name, CharacterRace.Human, level, team: team, location: location);
        npc.EnsureSystemsInitialized();
        var p = npc.Brain!.Personality;
        p.Loyalty = 0.8f; p.Sociability = 0.8f; p.Ambition = 0.8f; p.Courage = 0.8f; p.Aggression = 0.5f;
        npc.TeamPW = team == "" ? "" : "pw";
        return npc;
    }

    private static RecruitPoolWorld Empty(int seed, bool online = true)
    {
        SilenceNews();
        return new RecruitPoolWorld(seed, online: online, classic: false);
    }

    /// <summary>Calls a team step up to `tries` times; true as soon as `joined` holds.</summary>
    private static bool Ever(int tries, Action step, Func<bool> joined)
    {
        for (int i = 0; i < tries; i++) { step(); if (joined()) return true; }
        return false;
    }

    /// <summary>
    /// Row 13: the same seeded steps through every site that puts an NPC on an NPC team, with the switch as
    /// asked; returns what happened and the draws it took, to be compared between "on" and "off".
    /// </summary>
    public static string Row13Joins(string arg)
    {
        var parts = arg.Split(',');
        int lv = int.Parse(parts[1]);
        SetSwitch(parts[0] == "on");
        var sb = new StringBuilder();
        string Teams(RecruitPoolWorld w) => string.Join(",", w.Npcs.Select(n => $"{n.Name2}:{(n.Team == "" ? "" : "T")}"));
        // a level 5 joins the NPC team that stands beside it
        using (var w = Empty(40))
        {
            Social("Tide One", 40, NpcTeam); Social("Tide Two", 40, NpcTeam);
            var five = Social("Five", lv);
            bool joined = Ever(400, () => w.Call("NPCTryJoinOrFormTeam", five), () => five.Team == NpcTeam);
            sb.Append($"joined={joined} draws={w.SimRandom.Draws} hash={w.SimRandom.Hash:x} {Teams(w)}\n");
        }
        // a level 5 founds a team with another level 5
        using (var w = Empty(41))
        {
            var five = Social("Five", lv); var other = Social("Other Five", lv);
            bool founded = Ever(400, () => w.Call("NPCTryJoinOrFormTeam", five), () => five.Team != "" && other.Team == five.Team);
            sb.Append($"founded={founded} draws={w.SimRandom.Draws} hash={w.SimRandom.Hash:x} {Teams(w)}\n");
        }
        // a level 5 is recruited by a member of an NPC team
        using (var w = Empty(43))
        {
            var recruiter = Social("Recruiter", 40, NpcTeam); var five = Social("Five", lv);
            bool recruited = Ever(400, () => w.Call("NPCTryRecruitForTeam", recruiter), () => five.Team == NpcTeam);
            sb.Append($"recruited={recruited} draws={w.SimRandom.Draws} hash={w.SimRandom.Hash:x} {Teams(w)}\n");
        }
        // world creation: level 5 to 15 lead and join the first teams, and the first days' team activity takes one
        var init = WorldInitializerSystem.Instance;
        using (var w = Empty(45))
        {
            typeof(WorldInitializerSystem).GetField("random", F)!.SetValue(init, new Random(45));
            for (int i = 0; i < 12; i++) Social("Low " + i, lv + i % (lv <= 15 ? 16 - lv : 11));
            ((Task)typeof(WorldInitializerSystem).GetMethod("SimulateTeamFormation", F)!.Invoke(init, new object[] { w.Npcs, 30 })!).GetAwaiter().GetResult();
            sb.Append($"worldCreation={w.Npcs.Any(n => n.Team != "")} {Teams(w)}\n");
        }
        using (var w = Empty(46))
        {
            typeof(WorldInitializerSystem).GetField("random", F)!.SetValue(init, new Random(46));
            var member = Social("Member", 40, NpcTeam); var five = Social("Five", lv);
            bool activity = Ever(3000, () => typeof(WorldInitializerSystem).GetMethod("SimulateTeamActivity", F)!.Invoke(init, new object[] { member, 1 }), () => five.Team == NpcTeam);
            sb.Append($"activity={activity} {Teams(w)}\n");
        }
        // the two gang recruitments
        using (var w = Empty(47))
        {
            Social("Ganger", 40, NpcTeam); var five = Social("Five", lv);
            var staticRecruit = typeof(EnhancedNPCBehaviors).GetMethod("RecruitGangMembers", BindingFlags.Static | BindingFlags.NonPublic)!;
            var staticRandom = typeof(EnhancedNPCBehaviors).GetField("random", BindingFlags.Static | BindingFlags.NonPublic)!;
            var kept = staticRandom.GetValue(null);
            staticRandom.SetValue(null, new Random(47));
            bool gang = Ever(300, () => staticRecruit.Invoke(null, new object[] { NpcTeam, w.Npcs }), () => five.Team == NpcTeam);
            staticRandom.SetValue(null, kept);
            var second = Social("Second Five", lv);
            var system = new EnhancedNPCBehaviorSystem();
            typeof(EnhancedNPCBehaviorSystem).GetField("random", F)!.SetValue(system, new Random(48));
            var recruit = typeof(EnhancedNPCBehaviorSystem).GetMethod("RecruitGangMembers", F)!;
            var all = w.Npcs.Cast<Character>().ToList();
            bool pascal = Ever(300, () => ((Task)recruit.Invoke(system, new object[] { NpcTeam, all, 1 })!).GetAwaiter().GetResult(), () => second.Team == NpcTeam);
            sb.Append($"gang={gang} pascalGang={pascal} {Teams(w)}\n");
        }
        // a low-level member stays on its NPC team through the team steps: nothing releases it
        using (var w = Empty(49))
        {
            var low = Social("Old Member", Math.Min(lv, 10), NpcTeam); Social("Old Mate", 40, NpcTeam); Social("Third", 40, NpcTeam);
            low.Loyalty = 100;
            foreach (var n in w.Npcs) n.Loyalty = 100;
            for (int tick = 0; tick < 200; tick++) w.Call("CheckTeamBetrayals");
            sb.Append($"stays={low.Team == NpcTeam} draws={w.SimRandom.Draws}\n");
        }
        SetSwitch(true);
        return sb.ToString();
    }

    private static readonly FieldInfo InitRandom = typeof(WorldInitializerSystem).GetField("random", F)!;
    private static void TeamFormation(RecruitPoolWorld w, int seed)
    {
        InitRandom.SetValue(WorldInitializerSystem.Instance, new Random(seed));
        ((Task)typeof(WorldInitializerSystem).GetMethod("SimulateTeamFormation", F)!.Invoke(WorldInitializerSystem.Instance, new object[] { w.Npcs, 30 })!).GetAwaiter().GetResult();
    }
    private static void TeamActivity(NPC member) =>
        typeof(WorldInitializerSystem).GetMethod("SimulateTeamActivity", F)!.Invoke(WorldInitializerSystem.Instance, new object[] { member, 1 });
    private static void Gang(RecruitPoolWorld w, int seed, int times)
    {
        var recruit = typeof(EnhancedNPCBehaviors).GetMethod("RecruitGangMembers", BindingFlags.Static | BindingFlags.NonPublic)!;
        var field = typeof(EnhancedNPCBehaviors).GetField("random", BindingFlags.Static | BindingFlags.NonPublic)!;
        var kept = field.GetValue(null);
        field.SetValue(null, new Random(seed));
        for (int i = 0; i < times; i++) recruit.Invoke(null, new object[] { NpcTeam, w.Npcs });
        field.SetValue(null, kept);
    }
    private static void PascalGang(RecruitPoolWorld w, int seed, int times, int maxRecruits)
    {
        var system = new EnhancedNPCBehaviorSystem();
        typeof(EnhancedNPCBehaviorSystem).GetField("random", F)!.SetValue(system, new Random(seed));
        var recruit = typeof(EnhancedNPCBehaviorSystem).GetMethod("RecruitGangMembers", F)!;
        var all = w.Npcs.Cast<Character>().ToList();
        for (int i = 0; i < times; i++) ((Task)recruit.Invoke(system, new object[] { NpcTeam, all, maxRecruits })!).GetAwaiter().GetResult();
    }

    /// <summary>Every asking site refuses a level 5 NPC whose band holds six or fewer; with the switch on.</summary>
    public static string Row13Refused(string _)
    {
        foreach (bool online in new[] { true, false })
        {
            // joining the NPC team that stands beside it: refused before anything is drawn
            using (var w = Empty(40, online))
            {
                Social("Tide One", 40, NpcTeam); Social("Tide Two", 40, NpcTeam);
                var five = Social("Five", 5);
                Ever(400, () => w.Call("NPCTryJoinOrFormTeam", five), () => five.Team != "").Should().BeFalse("a level 5 of a band of one does not join ({0})", online);
                w.SimRandom.Draws.Should().Be(0, "a refused NPC draws nothing");
            }
            // founding a team with a level 40 beside it
            using (var w = Empty(41, online))
            {
                var partner = Social("Partner", 40); var five = Social("Five", 5);
                Ever(400, () => w.Call("NPCTryJoinOrFormTeam", five), () => five.Team != "" || partner.Team != "").Should().BeFalse();
                w.SimRandom.Draws.Should().Be(0);
            }
            // the only free NPC anywhere is a level 5: a founder finds nobody, and draws nothing
            using (var w = Empty(42, online))
            {
                var founder = Social("Founder", 40); var far = Social("Far Five", 5, location: "Inn");
                Ever(400, () => w.Call("NPCTryJoinOrFormTeam", founder), () => far.Team != "" || founder.Team != "").Should().BeFalse();
                w.SimRandom.Draws.Should().Be(0, "nobody may be recruited, so the founding is not even drawn");
            }
            // recruited by a member of an NPC team
            using (var w = Empty(43, online))
            {
                var recruiter = Social("Recruiter", 40, NpcTeam); Social("Mate", 40, NpcTeam); var five = Social("Five", 5);
                Ever(400, () => w.Call("NPCTryRecruitForTeam", recruiter), () => five.Team != "").Should().BeFalse();
                w.SimRandom.Draws.Should().Be(0);
            }
        }
        // world creation: four each of level 5, 8 and 13 (no band above six): no leader, no team
        using (var w = Empty(45))
        {
            for (int i = 0; i < 12; i++) Social("Low " + i, new[] { 5, 8, 13 }[i % 3]);
            TeamFormation(w, 45);
            w.Npcs.Should().OnlyContain(n => n.Team == "");
        }
        // the first days' team activity
        using (var w = Empty(46))
        {
            InitRandom.SetValue(WorldInitializerSystem.Instance, new Random(46));
            var member = Social("Member", 40, NpcTeam); var five = Social("Five", 5);
            Ever(3000, () => TeamActivity(member), () => five.Team != "").Should().BeFalse();
        }
        // the two gang recruitments
        using (var w = Empty(47))
        {
            Social("Ganger", 40, NpcTeam); var five = Social("Five", 5);
            Gang(w, 47, 300);
            five.Team.Should().Be("");
            PascalGang(w, 48, 300, 1);
            five.Team.Should().Be("");
        }
        // the helper by itself
        using (var w = Empty(44))
        {
            for (int level = 1; level <= 15; level++) WorldSimulator.MayJoinNpcTeam(Social("L" + level, level)).Should().BeFalse("five in each band");
            WorldSimulator.MayJoinNpcTeam(Social("L16", 16)).Should().BeTrue();
            WorldSimulator.MayJoinNpcTeam(Social("L100", 100)).Should().BeTrue();
            WorldSimulator.MayJoinNpcTeam(null).Should().BeFalse();
        }
        return "ok";
    }

    /// <summary>A refused low-level NPC on a site's list must not take the pick of one who may join.</summary>
    public static string Row13Lists(string _)
    {
        // a new team's first recruit: a level 5 stands beside the founder, a level 40 elsewhere: the level 40 is taken
        using (var w = Empty(60))
        {
            var founder = Social("Founder", 40); var near = Social("Near Five", 5); var far = Social("Far Forty", 40, location: "Inn");
            Ever(400, () => w.Call("NPCTryJoinOrFormTeam", founder), () => founder.Team != "" && far.Team == founder.Team).Should().BeTrue();
            near.Team.Should().Be("");
        }
        // world creation, the leaders: ten of level 13 with the most charisma would fill the ten places of the list
        using (var w = Empty(61))
        {
            for (int i = 0; i < 6; i++) { var low = Social("Charming Low " + i, 13); low.Charisma = 500; }
            for (int i = 0; i < 6; i++) { var low = Social("Charming Low B " + i, 8); low.Charisma = 500; }
            for (int i = 0; i < 6; i++) Social("High " + i, 30 + i);
            TeamFormation(w, 61);
            w.Npcs.Count(n => n.Level >= 16 && n.Team != "").Should().BeGreaterThan(0, "the level 30s lead teams: the refused do not hold the list");
            w.Npcs.Where(n => n.Level <= 15).Should().OnlyContain(n => n.Team == "");
        }
        // world creation, the recruits: one leader, four of level 40 and two hundred low: every team has recruits
        for (int seed = 62; seed < 82; seed++)
            using (var w = Empty(seed))
            {
                var leader = Social("Leader", 60); leader.Charisma = 500;
                for (int i = 0; i < 4; i++) { var h = Social("Recruit " + i, 4); h.Level = 40; h.Brain!.Personality.Ambition = 0.1f; }
                for (int i = 0; i < 200; i++) { var low = Make("Low " + i, Races[i % 10], new[] { 5, 8, 13 }[i % 3]); low.EnsureSystemsInitialized(); var p = low.Brain!.Personality; p.Loyalty = 0.8f; p.Sociability = 0.8f; p.Ambition = 0.1f; p.Courage = 0.8f; }
                foreach (var low in w.Npcs.Where(n => n.Level <= 15).Skip(18).ToList()) { low.Team = "Elsewhere"; }   // six free in each band
                TeamFormation(w, seed);
                w.Npcs.Count(n => n.Team == leader.Team && n != leader).Should().BeGreaterThan(0, "seed {0}: a recruit place is not spent on a refused NPC", seed);
            }
        // the gang recruitment looks at three: three of level 5 ahead in the roster must not hide the level 40
        using (var w = Empty(83))
        {
            Social("Ganger", 40, NpcTeam);
            for (int i = 0; i < 3; i++) Social("Five " + i, 5);
            var forty = Social("Forty", 40);
            Gang(w, 83, 300);
            forty.Team.Should().Be(NpcTeam);
        }
        return "ok";
    }

    public static string Row13StaysAndHire(string _)
    {
        using var w = Empty(49);
        // a level 10 already on an NPC team: nothing removes it, and it still recruits a level 16
        var low = Social("Old Member", 10, NpcTeam); Social("Old Mate", 40, NpcTeam);
        var sixteen = Social("Sixteen", 16);
        foreach (var n in w.Npcs) n.Loyalty = 100;
        for (int tick = 0; tick < 300; tick++) { w.Call("NPCTeamRecruitment", low); w.Call("CheckTeamBetrayals"); w.Tick++; }
        low.Team.Should().Be(NpcTeam, "nothing is released");
        sixteen.Team.Should().Be(NpcTeam);
        // through whole ticks of the world as well
        for (int tick = 0; tick < 50; tick++) w.Step();
        low.Team.Should().Be(NpcTeam);

        // a level 15 pool member is hired by a player: the list shows it, and on the player's team no NPC team step moves it
        var fifteen = Social("Fifteen", 15);
        WorldSimulator.MayJoinNpcTeam(fifteen).Should().BeFalse();
        var player = new Character { Name1 = "Hiring Player", Name2 = "Hiring Player", ID = "hiring_player", Level = 12, HP = 90, MaxHP = 90, Team = "Player Hall" };
        TeamSystem.IsRecruitable(player, fifteen, out var hireBand).Should().BeTrue();
        HireList(player, w.Npcs).Should().Contain(fifteen);
        WorldSimulator.RegisterPlayerTeam("Player Hall");
        fifteen.Team = "Player Hall";                                   // as the hire does
        WorldSimulator.IsOnPlayerTeam(fifteen).Should().BeTrue();
        for (int i = 0; i < 100; i++) { w.Call("NPCTeamRecruitment", fifteen); w.Call("CheckTeamBetrayals"); }
        fifteen.Team.Should().Be("Player Hall");
        return "ok";
    }

    public static string Row15(string _)
    {
        foreach (bool online in new[] { true, false })
        {
            // joining: seven free of level 12 stand by an NPC team: one joins, and at six the rest are refused
            using (var w = Empty(57, online))
            {
                Social("Tide One", 40, NpcTeam); Social("Tide Two", 40, NpcTeam);
                var sevens = Enumerable.Range(0, 7).Select(i => Social("Twelve " + i, 12)).ToList();
                WorldSimulator.MayJoinNpcTeam(sevens[0]).Should().BeTrue("seven in the band: one above the six");
                for (int round = 0; round < 300; round++)
                    foreach (var n in sevens.Where(n => n.Team == "")) w.Call("NPCTryJoinOrFormTeam", n);
                sevens.Count(n => n.Team != "").Should().Be(1, "one takes the band from seven to six ({0})", online);
                sevens.Where(n => n.Team == "").Should().OnlyContain(n => !WorldSimulator.MayJoinNpcTeam(n));
                Bands(w)[2].Should().Be(6);
                // another band is counted by itself: seven of level 12 say nothing for a level 8
                WorldSimulator.MayJoinNpcTeam(Social("Eight", 8)).Should().BeFalse();
            }
            // recruited by a member: the same
            using (var w = Empty(58, online))
            {
                var recruiter = Social("Recruiter", 40, NpcTeam); Social("Mate", 40, NpcTeam);
                var sevens = Enumerable.Range(0, 7).Select(i => Social("Three " + i, 3)).ToList();
                for (int round = 0; round < 600; round++) w.Call("NPCTryRecruitForTeam", recruiter);
                sevens.Count(n => n.Team != "").Should().Be(1);
                Bands(w)[0].Should().Be(6);
            }
        }
        // an NPC waiting for its respawn holds its place in the count; one dead for good does not
        using (var w = Empty(60))
        {
            var sevens = Enumerable.Range(0, 7).Select(i => Social("Twelve " + i, 12)).ToList();
            sevens[6].HP = 0; sevens[6].IsDead = true;
            WorldSimulator.MayJoinNpcTeam(sevens[0]).Should().BeTrue();
            sevens[6].IsPermaDead = true;
            WorldSimulator.MayJoinNpcTeam(sevens[0]).Should().BeFalse();
        }
        return "ok";
    }

    /// <summary>The four places where one call can take more than one NPC of a band: asked again as each joins.</summary>
    public static string Row15Several(string _)
    {
        foreach (bool online in new[] { true, false })
            // a founder and its first recruit of the same band: with seven only one could go, and a team of one
            // is undone; with eight both go
            using (var w = Empty(59, online))
            {
                var nines = Enumerable.Range(0, 7).Select(i => Social("Nine " + i, 9)).ToList();
                for (int round = 0; round < 300; round++)
                    foreach (var n in nines.Where(n => n.Team == "")) w.Call("NPCTryJoinOrFormTeam", n);
                nines.Should().OnlyContain(n => n.Team == "", "a pair would leave five");
                nines.Add(Social("Nine 7", 9));
                for (int round = 0; round < 300; round++)
                    foreach (var n in nines.Where(n => n.Team == "")) w.Call("NPCTryJoinOrFormTeam", n);
                nines.Count(n => n.Team != "").Should().Be(2, "eight: a founder and its recruit, and six stay");
                Bands(w)[1].Should().Be(6);
            }
        // world creation: nine of level 13 who could all lead: only as many found or join as leave six
        for (int seed = 90; seed < 100; seed++)
            using (var w = Empty(seed))
            {
                for (int i = 0; i < 9; i++) Social("Thirteen " + i, 13);
                TeamFormation(w, seed);
                Bands(w)[2].Should().BeGreaterThanOrEqualTo(6, "seed {0}", seed);
                w.Npcs.Count(n => n.Team != "").Should().BeLessThanOrEqualTo(3);
            }
        // world creation: one leader of level 60 and nine of level 13 to recruit
        int recruitedLows = 0;
        for (int seed = 100; seed < 120; seed++)
            using (var w = Empty(seed))
            {
                var leader = Social("Leader", 60); leader.Charisma = 500;
                for (int i = 0; i < 9; i++) { var low = Social("Thirteen " + i, 13); low.Brain!.Personality.Ambition = 0.1f; low.Level = 13; }
                foreach (var n in w.Npcs.Where(n => n.Level == 13)) n.Level = 4;   // below the leaders' level 5: recruits only
                TeamFormation(w, seed);
                Bands(w)[0].Should().BeGreaterThanOrEqualTo(6, "seed {0}", seed);
                recruitedLows += w.Npcs.Count(n => n.Level == 4 && n.Team != "");
            }
        recruitedLows.Should().BeGreaterThan(0, "the surplus of the band is recruited");
        // the gang recruitments: eight of level 3; a call can take several, and never takes the band below six
        for (int seed = 121; seed < 141; seed++)
        {
            using (var w = Empty(seed))
            {
                Social("Ganger", 40, NpcTeam);
                var threes = Enumerable.Range(0, 8).Select(i => Social("Three " + i, 3)).ToList();
                Gang(w, seed, 400);
                threes.Count(n => n.Team != "").Should().Be(2, "seed {0}", seed);
            }
            using (var w = Empty(seed))
            {
                Social("Ganger", 40, NpcTeam);
                var threes = Enumerable.Range(0, 8).Select(i => Social("Three " + i, 3)).ToList();
                PascalGang(w, seed, 400, 4);
                threes.Count(n => n.Team != "").Should().Be(2, "seed {0}", seed);
            }
        }
        return "ok";
    }

    public static string Row14(string _)
    {
        // 181 alive and 8 dead that will respawn: 189 counted. One arrival comes, and at 190 none.
        SilenceNews();
        using var w = new RecruitPoolWorld(51, classic: false);
        foreach (var race in Races)
            for (int i = 0; i < 18; i++) Make($"Elder {race} {i}", race, 40);
        Make("Elder Extra", CharacterRace.Human, 40);
        var waiting = new List<NPC>();
        for (int i = 0; i < 8; i++)
        {
            var n = Make($"Fallen {i}", Races[i], 40);
            n.HP = 0; n.IsDead = true;                      // an ordinary death: it respawns
            waiting.Add(n);
        }
        w.Alive().Count.Should().Be(181);
        w.Npcs.Count.Should().Be(189);
        for (int tick = 0; tick < 300; tick++) { w.Call("ProcessRecruitPoolTopUp"); w.Tick++; }
        w.Npcs.Count.Should().Be(190, "at 189 one arrival comes, and at 190 none");
        w.Alive().Count.Should().Be(182);

        // one of the fallen is dead for good: 189 counted again, one more arrives
        waiting[0].IsPermaDead = true;
        for (int tick = 0; tick < 300; tick++) { w.Call("ProcessRecruitPoolTopUp"); w.Tick++; }
        w.Npcs.Count(n => !n.IsPermaDead && !n.IsAgedDeath).Should().Be(190);
        w.Alive().Count.Should().Be(183);

        // a death of old age does not count either
        waiting[1].IsAgedDeath = true;
        for (int tick = 0; tick < 300; tick++) { w.Call("ProcessRecruitPoolTopUp"); w.Tick++; }
        w.Npcs.Count(n => !n.IsPermaDead && !n.IsAgedDeath).Should().Be(190);
        w.Alive().Count.Should().Be(184);
        return "ok";
    }

    /// <summary>Row 21: a town held at 195 living, the switch as asked; what the gates at 200 and the pool do.</summary>
    public static string Row21(string arg)
    {
        SetSwitch(arg == "on");
        SilenceNews();
        using var w = new RecruitPoolWorld(52, classic: false);
        // 195 alive: 135 men and 60 women of 30, level 40, no pool member among them
        for (int i = 0; i < 195; i++)
        {
            var n = Make($"Citizen {i}", Races[i % 10], 40, age: 30);
            if (i % 13 < 4) n.Sex = CharacterSex.Female;
            n.EnsureSystemsInitialized();
        }
        w.Alive().Count.Should().Be(195);
        var sb = new StringBuilder();

        // the pool: places free in every band, and nobody comes
        int before = w.Npcs.Count;
        for (int tick = 0; tick < 300; tick++) { w.Call("ProcessRecruitPoolTopUp"); w.Tick++; }
        sb.Append($"poolArrivals={w.Npcs.Count - before}\n");

        // pregnancies: the seeded step, until some have started
        for (int step = 0; step < 6000 && w.Npcs.Count(n => n.PregnancyDueDate.HasValue) < 1; step++) w.Call("ProcessNPCPregnancies");
        int pregnant = w.Npcs.Count(n => n.PregnancyDueDate.HasValue);
        sb.Append($"pregnant={pregnant >= 1} count={pregnant} draws={w.SimRandom.Draws} hash={w.SimRandom.Hash:x}\n");

        // a child of 18 becomes an NPC
        var child = new Child { Name = "Town Heir", Mother = "Citizen 0", Father = "Citizen 1", MotherID = "npc_a", FatherID = "npc_b", Sex = CharacterSex.Male,
            BirthDate = DateTime.Now.AddHours(-18.2 * HoursPerYear), Age = 17, Location = GameConfig.ChildLocationHome };
        FamilySystem.Instance.RegisterChild(child);
        FamilySystem.Instance.ProcessDailyAging();
        sb.Append($"childBecameNpc={child.Deleted && w.Npcs.Any(n => n.Name2.StartsWith("Town Heir"))}\n");

        // an orphan of 18 leaves the orphanage
        var king = King.CreateNewKing("Citizen 2", CharacterAI.Computer, CharacterSex.Male);
        CastleLocation.SetKing(king);
        var orphan = new RoyalOrphan { Name = "Orphan Ward", Age = 17, Sex = CharacterSex.Female, MotherName = "Gone Mother", FatherName = "Gone Father",
            MotherID = "npc_gone_m", FatherID = "npc_gone_f", IsRealOrphan = true, BirthDate = DateTime.Now.AddHours(-18.2 * HoursPerYear) };
        king.Orphans.Add(orphan);
        int npcsBefore = w.Npcs.Count, guardsBefore = king.Guards.Count;
        w.Call("ProcessOrphanAging");
        sb.Append($"orphanCameOfAge={!king.Orphans.Contains(orphan) && (w.Npcs.Count > npcsBefore || king.Guards.Count > guardsBefore)} realOrphan={orphan.IsRealOrphan}\n");
        sb.Append($"living={w.Alive().Count}\n");
        CastleLocation.SetKing(null!);
        SetSwitch(true);
        return sb.ToString();
    }
}
