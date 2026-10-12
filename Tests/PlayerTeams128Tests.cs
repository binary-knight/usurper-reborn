using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.8 (F5): the world simulator knows every player's team. Rows 1 to 6 and 8 of the brief (row 7 is in
/// PlayerTeams128Golden.cs). Every store is a temporary SQLite file made by the test; a second "process" is a
/// second copy of the game (IsolatedGame) or a second backend over the same file. No row is written to
/// player_teams for a team an NPC founded: the simulator reads which teams the players' saves name.
/// </summary>
[Collection("SharedGameSingletons")]
public class PlayerTeams128Tests
{
    private static string Run(string scenario, string arg = "") => IsolatedGame.Run(scenario, arg);

    private static void WithStore(Action<string> body)
    {
        string path = Path.Combine(Path.GetTempPath(), $"usurper-f5-{Guid.NewGuid():N}.db");
        try { body(path); }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch { }
        }
    }

    [Fact] public void Row1_ATeamWrittenByAnotherProcess_IsKnownAfterTheNextTick_AndItsHireIsSafe() => Run(nameof(RecruitPoolRuns.F5Row1));

    [Fact]
    public void Row2_APlayerJoinsAnNpcFoundedTeam_TheStoreNamesIt_AndARestartedSimulatorKnowsIt() => WithStore(path =>
    {
        Run(nameof(RecruitPoolRuns.F5Row2Join), path).Should().Be("joined");
        // another copy of the game: a simulator started later, with that player logged out
        Run(nameof(RecruitPoolRuns.F5Row2Restart), path).Should().Be("known died=False left=False aged=False joined=False");
    });

    [Fact] public void Row2_ASaveFromBeforeThisFix_NamesItsTeam_SoTheStartKnowsItWithoutARepair() => Run(nameof(RecruitPoolRuns.F5Row2OldSave));
    [Fact] public void Row3_AFailedRefresh_KeepsTheList_AndLogsOnce() => Run(nameof(RecruitPoolRuns.F5Row3Refresh));
    [Fact] public void Row3_AFailedFirstLoad_HoldsTheTick_UntilAReadSucceeds() => Run(nameof(RecruitPoolRuns.F5Row3FirstLoad));
    [Fact] public void Row4_TheLastPlayerLeaves_TheTeamIsAnNpcTeamAgain_AndItsRowStays() => Run(nameof(RecruitPoolRuns.F5Row4));
    [Fact] public void Row4_ASessionName_TheStoreNeverConfirms_LeavesAtTheSecondRefresh() => Run(nameof(RecruitPoolRuns.F5Row4Session));
    [Fact] public void Row5_ATeamCreatedInThisProcess_IsKnownAtOnce_AndSurvivesTheRefreshBeforeTheStoreHasIt() => Run(nameof(RecruitPoolRuns.F5Row5));
    [Fact] public void Row5_ARegistrationDuringTheRead_SurvivesThatRefreshAndTheNext() => Run(nameof(RecruitPoolRuns.F5Row5Race));
    [Fact] public void Row6_TheReadersOfPlayerTeams_AreUnchanged_AndAJoinedNpcTeamGetsNoRow() => Run(nameof(RecruitPoolRuns.F5Row6));
    [Fact] public void Read_AFreshStamp_CountsBeforeTheSaveLands_AndAStaleOneDoesNot() => Run(nameof(RecruitPoolRuns.F5ReadStamp));
    [Fact] public void Read_AMalformedSave_MakesEveryRowCount() => Run(nameof(RecruitPoolRuns.F5ReadMalformed));
    [Fact] public void Read_AnArchivedCharacterInItsWindow_AndABannedPlayer_StillCount() => Run(nameof(RecruitPoolRuns.F5ReadArchivedAndBanned));
    [Fact] public void Row8_SinglePlayer_NoStore_NoRefresh_TheRegisteredTeamStays() => Run(nameof(RecruitPoolRuns.F5Row8SinglePlayer));
    [Fact] public void Row9_ASessionsOwnLoop_Online_HoldsItsStep_WhileNoReadHasSucceeded() => Run(nameof(RecruitPoolRuns.F5Row9Hold));
    [Fact] public void Row9_ASessionsOwnLoop_SinglePlayer_ReadsNoStore() => Run(nameof(RecruitPoolRuns.F5Row9SinglePlayer));
    [Fact] public void Row10_ALiveSessionsTeam_StaysProtected_WhenTheStoreDoesNotNameIt() => Run(nameof(RecruitPoolRuns.F5Row10));

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
    public void Row8_SinglePlayerOrder_ThePlayerIsRestoredAndItsTeamRegistered_BeforeTheNpcsAreRestored()
    {
        string engine = Code("Core", "GameEngine.cs");
        // the one registration of the engine sits in the player restore
        Regex.Matches(engine, @"WorldSimulator\.RegisterPlayerTeam\(").Count.Should().Be(1);
        int core = engine.IndexOf("private Character RestorePlayerFromSaveDataCore(PlayerData playerData)", StringComparison.Ordinal);
        int register = engine.IndexOf("WorldSimulator.RegisterPlayerTeam(player.Team);", StringComparison.Ordinal);
        int next = engine.IndexOf("private async Task RestoreWorldState(WorldStateData worldState)", StringComparison.Ordinal);
        core.Should().BeGreaterThan(0);
        register.Should().BeInRange(core, next, "the registration is inside the player restore");
        // both load paths restore the player first, then the NPCs
        var players = Regex.Matches(engine, @"currentPlayer = RestorePlayerFromSaveData\(saveData\.Player\);").Select(m => m.Index).ToList();
        var npcs = Regex.Matches(engine, @"await RestoreNPCs\(saveData\.NPCs\);").Select(m => m.Index).ToList();
        players.Should().HaveCount(2);
        npcs.Should().HaveCount(2);
        for (int i = 0; i < 2; i++)
        {
            players[i].Should().BeLessThan(npcs[i], "load path {0}", i);
            if (i == 0) npcs[i].Should().BeLessThan(players[1], "the first path ends before the second begins");
            engine.Substring(players[i], npcs[i] - players[i]).Should().NotContain("RestoreNPCs(", "nothing restores NPCs in between");
        }
    }

    [Fact]
    public void TheSimulator_ReadsTheStore_AtStartAndBeforeEveryTick_AndTheGuardsReadOneList()
    {
        string service = Code("Systems", "WorldSimService.cs");
        int tick = service.IndexOf("internal async Task<bool> RunOneTickAsync()", StringComparison.Ordinal);
        int beat = service.IndexOf("if (!await BeatWorldSimLockAsync()) return false;", tick, StringComparison.Ordinal);
        int refresh = service.IndexOf("if (!RefreshPlayerTeamNames()) return false;", tick, StringComparison.Ordinal);
        int step = service.IndexOf("worldSimulator?.SimulateStep();", tick, StringComparison.Ordinal);
        beat.Should().BeGreaterThan(tick);
        refresh.Should().BeGreaterThan(beat, "a sim paused without the lock does not read");
        step.Should().BeGreaterThan(refresh, "the read comes before the tick");
        Regex.Matches(service, @"\bRefreshPlayerTeamNames\(\)").Count.Should().Be(3, "the definition, the start load and the tick");
        // H2's helper and every guard read the same list: no second list, no second predicate
        string sim = Code("Systems", "WorldSimulator.cs");
        // a session's own loop goes through the step that reads the store first when online
        int loop = sim.IndexOf("while (isRunning)", StringComparison.Ordinal);
        int delay = sim.IndexOf("Task.Delay(TimeSpan.FromSeconds(SIMULATION_INTERVAL))", loop, StringComparison.Ordinal);
        loop.Should().BeGreaterThan(0);
        sim.Substring(loop, delay - loop).Should().Contain("SessionLoopStep();").And.NotContain("SimulateStep();");
        sim.Should().Contain("public static bool IsOnPlayerTeam(NPC? npc) => npc != null && IsPlayerTeam(npc.Team);");
        Regex.Matches(sim, @"_playerTeamNames\.Contains\(").Count.Should().Be(2, "IsPlayerTeam, and the refresh dropping a confirmed session name");
        // the store read writes nothing
        string sql = SqlSaveBackend.PlayerTeamNamesSql.ToUpperInvariant();
        Regex.IsMatch(sql, @"\b(INSERT|UPDATE|DELETE|ALTER|CREATE|REPLACE|DROP)\b").Should().BeFalse();
    }

    /// <summary>
    /// The performance receipt (F5_MEASURE names the output file): the read the simulator makes before every
    /// tick, on a store of 500 player saves in 300 teams, at a small save size and at 76 KB a save (38 MB).
    /// </summary>
    [Fact]
    public void Measure_TheReadBeforeEveryTick()
    {
        string? outFile = Environment.GetEnvironmentVariable("F5_MEASURE");
        if (string.IsNullOrEmpty(outFile)) return;
        var sb = new StringBuilder();
        foreach (int pad in new[] { 2_000, 76_000 })
            WithStore(path =>
            {
                var db = new SqlSaveBackend(path);
                using (var conn = new SqliteConnection($"Data Source={path}"))
                {
                    conn.Open();
                    using var tx = conn.BeginTransaction();
                    string filler = new string('x', pad);
                    for (int i = 0; i < 500; i++)
                    {
                        using var cmd = conn.CreateCommand();
                        cmd.Transaction = tx;
                        cmd.CommandText = "INSERT INTO players (username, display_name, player_data) VALUES (@u, @u, @d);";
                        cmd.Parameters.AddWithValue("@u", $"measure{i}");
                        cmd.Parameters.AddWithValue("@d", $"{{\"player\":{{\"level\":10,\"notes\":\"{filler}\",\"team\":\"Team {i % 300}\"}}}}");
                        cmd.ExecuteNonQuery();
                    }
                    for (int t = 0; t < 300; t += 2)
                    {
                        using var cmd = conn.CreateCommand();
                        cmd.Transaction = tx;
                        cmd.CommandText = "INSERT INTO player_teams (team_name, password_hash, created_by, last_join_at) VALUES (@t, 'x', 'measure0', datetime('now', '-1 day'));";
                        cmd.Parameters.AddWithValue("@t", $"Team {t}");
                        cmd.ExecuteNonQuery();
                    }
                    tx.Commit();
                }
                long bytes = new FileInfo(path).Length;
                var service = new WorldSimService(db);
                service.RefreshPlayerTeamNames().Should().BeTrue();
                var times = new List<double>();
                int names = 0;
                for (int i = 0; i < 40; i++)
                {
                    var sw = Stopwatch.StartNew();
                    names = db.ReadPlayerTeamNames(out _)!.Count;
                    service.RefreshPlayerTeamNames();
                    sw.Stop();
                    times.Add(sw.Elapsed.TotalMilliseconds / 2);
                }
                times.Sort();
                sb.AppendLine($"500 saves of about {pad / 1000} KB, 300 teams (150 with a row), file {bytes / 1_000_000.0:F1} MB: {names} names; " +
                              $"one read: median {times[20]:F1} ms, max {times[39]:F1} ms, min {times[0]:F1} ms (40 pairs)");
            });
        File.AppendAllText(outFile!, sb.ToString());
    }
}

internal static partial class RecruitPoolRuns
{
    private static void F5Sql(string path, string sql)
    {
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static string F5Text(string path, string sql)
    {
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToString(cmd.ExecuteScalar()) ?? "";
    }

    /// <summary>A player's save that names a team (or none), as another process would have written it.</summary>
    private static void F5Save(string path, string key, string team, bool banned = false) =>
        F5Sql(path, $"INSERT OR REPLACE INTO players (username, display_name, player_data, is_banned) VALUES ('{key}', '{key}', " +
                    $"'{{\"player\":{{\"level\":10,\"team\":\"{team}\"}}}}', {(banned ? 1 : 0)});");

    /// <summary>The team's join stamp set a day back: only a save can make it a player's team now.</summary>
    private static void F5StaleStamp(string path, string team) =>
        F5Sql(path, $"UPDATE player_teams SET last_join_at = datetime('now', '-1 day') WHERE team_name = '{team}';");

    /// <summary>A simulator service that neither saves nor runs the daily reset in its tick; `sim` is the world it steps.</summary>
    private static WorldSimService F5Service(SqlSaveBackend db, WorldSimulator? sim = null)
    {
        var service = new WorldSimService(db, saveIntervalMinutes: int.MaxValue);
        typeof(WorldSimService).GetField("_lastWorldDailyReset", F)!.SetValue(service, DateTime.MaxValue);
        if (sim != null) typeof(WorldSimService).GetField("worldSimulator", F)!.SetValue(service, sim);
        return service;
    }

    private static bool F5Tick(WorldSimService service) => service.RunOneTickAsync().GetAwaiter().GetResult();

    private static void F5Store(Action<string, SqlSaveBackend> body)
    {
        string path = Path.Combine(Path.GetTempPath(), $"usurper-f5-{Guid.NewGuid():N}.db");
        try { body(path, new SqlSaveBackend(path)); }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch { }
            NewsSystem.Instance.ClearCatchUpBuffer();
        }
    }

    /// <summary>
    /// What the world simulator does to a level 5 hire of `team` at four of its guards: a death in the world
    /// (MarkNPCDead), walking out (CheckTeamBetrayals), old age (ProcessNPCAging, H2) and an NPC joining the
    /// team on its own (NPCTryJoinOrFormTeam). An NPC team gives True four times; a player's team False.
    /// </summary>
    private static string F5Harm(RecruitPoolWorld w, string team)
    {
        w.Npcs.Clear();
        for (int i = 0; i < 8; i++) Make($"Young Gnoll {team} {i}", CharacterRace.Gnoll, 40, age: 20, location: "Inn");
        var elder = Social(team + " Elder", 40, team);
        var hire = Social(team + " Hire", 5, team);
        elder.Loyalty = 100;
        hire.Loyalty = 0;

        w.Sim.MarkNPCDead(hire, 0f, "a monster", "Dungeon");
        bool died = hire.IsDead;
        hire.IsDead = false;
        hire.HP = hire.MaxHP;

        bool left = Ever(20000, () => w.Call("CheckTeamBetrayals"), () => hire.Team != team);
        hire.Team = team; hire.TeamPW = "pw";
        elder.Team = team; elder.TeamPW = "pw";

        var old = Make(team + " Old Hire", CharacterRace.Gnoll, 5, team: team, age: 55, location: "Inn");
        AgeCheck(w);
        bool aged = old.IsAgedDeath;
        w.Npcs.Remove(old);

        var joiner = Social(team + " Joiner", 40);
        bool joined = Ever(400, () => w.Call("NPCTryJoinOrFormTeam", joiner), () => joiner.Team == team);
        return $"died={died} left={left} aged={aged} joined={joined}";
    }

    private const string AllHarm = "died=True left=True aged=True joined=True";
    private const string NoHarm = "died=False left=False aged=False joined=False";

    // ---------- row 1 (H-a) ----------

    public static string F5Row1(string _)
    {
        using var w = Empty(71);
        F5Store((path, db) =>
        {
            db.CreatePlayerTeam("Early Team", "hash", "early player").GetAwaiter().GetResult().Should().BeTrue();
            F5Save(path, "early player", "Early Team");
            var service = F5Service(db, w.Sim);
            service.RefreshPlayerTeamNames().Should().BeTrue("the simulator starts");
            WorldSimulator.IsPlayerTeam("Early Team").Should().BeTrue();

            // the control: what this world does to a team it takes for an NPC team
            F5Harm(w, NpcTeam).Should().Be(AllHarm);

            // another process (a door beside the simulator's process) makes a team after the simulator started
            var door = new SqlSaveBackend(path);
            door.CreatePlayerTeam("Door Team", "hash", "door player").GetAwaiter().GetResult().Should().BeTrue();
            F5Save(path, "door player", "Door Team");
            F5StaleStamp(path, "Door Team");
            WorldSimulator.IsPlayerTeam("Door Team").Should().BeFalse("no session of this process registered it");

            w.Npcs.Clear();
            F5Tick(service).Should().BeTrue();
            WorldSimulator.IsPlayerTeam("Door Team").Should().BeTrue("the tick read the store first");
            WorldSimulator.IsOnPlayerTeam(new NPC { Team = "Door Team" }).Should().BeTrue("H2's helper reads the same list");
            F5Harm(w, "Door Team").Should().Be(NoHarm);
        });
        return "ok";
    }

    // ---------- row 2 (H-b) ----------

    /// <summary>A door process: a player joins a team two NPCs founded (the Team Corner and street gang join path).</summary>
    public static string F5Row2Join(string path)
    {
        using var w = Empty(72);
        var field = typeof(SaveSystem).GetField("instance", BindingFlags.NonPublic | BindingFlags.Static)!;
        var saveSystemBefore = field.GetValue(null);
        var db = new SqlSaveBackend(path);
        SaveSystem.InitializeWithBackend(db);
        try
        {
            Social("Tide One", 40, NpcTeam); Social("Tide Two", 40, NpcTeam);
            var hero = TeamCornerRig.Hero(name: "Joiner");
            string key = GameEngine.InheritanceKey(hero);            // the save key of this session
            TeamCornerRig.PlayerRow(path, key, "");
            TeamCornerLocation.TryTakeTeamSlot(hero, NpcTeam).GetAwaiter().GetResult().Should().BeTrue();
            // the join itself put the team into the joiner's save, before anything is confirmed to the player
            F5Text(path, $"SELECT json_extract(player_data, '$.player.team') FROM players WHERE username = '{key}';").Should().Be(NpcTeam);
            db.HasPlayerTeamRow(NpcTeam).Should().BeFalse("no row is written for a team an NPC founded");
            WorldSimulator.IsPlayerTeam(NpcTeam).Should().BeTrue("this process knows at once");
            return "joined";
        }
        finally
        {
            field.SetValue(null, saveSystemBefore);
            SqliteConnection.ClearAllPools();
        }
    }

    /// <summary>The simulator's process, started after the join, the joiner logged out: nothing registered here.</summary>
    public static string F5Row2Restart(string path)
    {
        using var w = Empty(73);
        try
        {
            WorldSimulator.IsPlayerTeam(NpcTeam).Should().BeFalse("a fresh process");
            var service = F5Service(new SqlSaveBackend(path), w.Sim);
            service.RefreshPlayerTeamNames().Should().BeTrue();
            string known = WorldSimulator.IsPlayerTeam(NpcTeam) ? "known" : "unknown";
            return known + " " + F5Harm(w, NpcTeam);
        }
        finally { SqliteConnection.ClearAllPools(); NewsSystem.Instance.ClearCatchUpBuffer(); }
    }

    public static string F5Row2OldSave(string _)
    {
        using var w = Empty(74);
        F5Store((path, db) =>
        {
            // a store as 1.2.7 left it: a player on a team an NPC founded, no player_teams row, nothing stamped
            F5Save(path, "old joiner", NpcTeam);
            F5Text(path, "SELECT COUNT(*) FROM player_teams;").Should().Be("0");
            var service = F5Service(db, w.Sim);
            service.RefreshPlayerTeamNames().Should().BeTrue();
            WorldSimulator.IsPlayerTeam(NpcTeam).Should().BeTrue();
            F5Harm(w, NpcTeam).Should().Be(NoHarm);
            F5Text(path, "SELECT COUNT(*) FROM player_teams;").Should().Be("0", "the start writes no row");
        });
        return "ok";
    }

    // ---------- row 3 (H-c) ----------

    private static void F5BreakStore(string path) => F5Sql(path, "ALTER TABLE players RENAME TO players_gone;");
    private static void F5MendStore(string path) => F5Sql(path, "ALTER TABLE players_gone RENAME TO players;");

    public static string F5Row3Refresh(string _)
    {
        using var w = Empty(75);
        F5Store((path, db) =>
        {
            F5Save(path, "early player", "Early Team");
            var service = F5Service(db, w.Sim);
            F5Tick(service).Should().BeTrue();
            WorldSimulator.IsPlayerTeam("Early Team").Should().BeTrue();

            F5BreakStore(path);
            db.ReadPlayerTeamNames(out string? error).Should().BeNull("a failed read is not an empty list");
            error.Should().NotBeNullOrEmpty();
            db.GetPlayerTeams().GetAwaiter().GetResult().Should().BeEmpty("the existing reader still answers empty");
            int before = w.Tick;
            for (int i = 0; i < 5; i++) F5Tick(service).Should().BeTrue("the world goes on with the list it has");
            w.Tick.Should().BeGreaterThan(before, "the ticks ran");
            WorldSimulator.IsPlayerTeam("Early Team").Should().BeTrue("the list is kept");
            F5Harm(w, "Early Team").Should().Be(NoHarm);
            service.PlayerTeamFailureLines.Should().Be(1, "five failed reads, one line");

            // sixty failed reads in all: the second line
            for (int i = 5; i < 60; i++) service.RefreshPlayerTeamNames().Should().BeTrue();
            service.PlayerTeamFailureLines.Should().Be(1);
            service.RefreshPlayerTeamNames().Should().BeTrue();
            service.PlayerTeamFailureLines.Should().Be(2);

            F5MendStore(path);
            F5Save(path, "late player", "Late Team");
            F5Tick(service).Should().BeTrue();
            WorldSimulator.IsPlayerTeam("Late Team").Should().BeTrue();
            WorldSimulator.IsPlayerTeam("Early Team").Should().BeTrue();
            service.PlayerTeamFailureLines.Should().Be(2);
        });
        return "ok";
    }

    public static string F5Row3FirstLoad(string _)
    {
        using var w = Empty(76);
        F5Store((path, db) =>
        {
            F5Save(path, "early player", "Early Team");
            Social("Early Elder", 40, "Early Team");
            var hire = Social("Early Hire", 5, "Early Team");
            F5BreakStore(path);

            var service = F5Service(db, w.Sim);
            service.RefreshPlayerTeamNames().Should().BeFalse("the start load failed: the list cannot be relied on");
            int tick = w.Tick;
            long draws = w.SimRandom.Draws;
            for (int i = 0; i < 4; i++) F5Tick(service).Should().BeFalse("held");
            w.Tick.Should().Be(tick, "no tick ran");
            w.SimRandom.Draws.Should().Be(draws, "the world drew nothing");
            hire.Team.Should().Be("Early Team");
            hire.LastOnPlayerTeamAt.Should().BeNull("no NPC was stamped as a player's hire meanwhile");
            service.PlayerTeamFailureLines.Should().Be(1, "five failed reads, one line");

            F5MendStore(path);
            F5Tick(service).Should().BeTrue("the first read that works lets the world run");
            w.Tick.Should().BeGreaterThan(tick);
            WorldSimulator.IsPlayerTeam("Early Team").Should().BeTrue();
        });
        return "ok";
    }

    // ---------- row 4 ----------

    public static string F5Row4(string _)
    {
        using var w = Empty(77);
        F5Store((path, db) =>
        {
            db.CreatePlayerTeam("Leaving Hall", "hash", "leaver").GetAwaiter().GetResult().Should().BeTrue();
            F5Save(path, "leaver", "Leaving Hall");
            F5StaleStamp(path, "Leaving Hall");
            var service = F5Service(db, w.Sim);
            F5Tick(service).Should().BeTrue();
            WorldSimulator.IsPlayerTeam("Leaving Hall").Should().BeTrue();
            F5Harm(w, "Leaving Hall").Should().Be(NoHarm);

            // the last player leaves it in another process; its NPC members stay, so the row stays, as before
            F5Save(path, "leaver", "");
            w.Npcs.Clear();
            F5Tick(service).Should().BeTrue();
            db.HasPlayerTeamRow("Leaving Hall").Should().BeTrue("the row of a team its NPCs still hold is kept, as before");
            WorldSimulator.IsPlayerTeam("Leaving Hall").Should().BeFalse("no player is on it: an NPC team again");
            F5Harm(w, "Leaving Hall").Should().Be(AllHarm);
        });
        return "ok";
    }

    public static string F5Row4Session(string _)
    {
        using var w = Empty(78);
        F5Store((path, db) =>
        {
            var service = F5Service(db, w.Sim);
            F5Tick(service).Should().BeTrue();
            // a session of this process loaded a player whose team no save in the store names
            WorldSimulator.RegisterPlayerTeam("Ghost Hall");
            F5Tick(service).Should().BeTrue();
            WorldSimulator.IsPlayerTeam("Ghost Hall").Should().BeTrue("the first refresh after it keeps it");
            F5Tick(service).Should().BeTrue();
            WorldSimulator.IsPlayerTeam("Ghost Hall").Should().BeFalse("the store had a whole interval and does not name it");
        });
        return "ok";
    }

    // ---------- row 5 ----------

    public static string F5Row5(string _)
    {
        using var w = Empty(79);
        F5Store((path, db) =>
        {
            var service = F5Service(db, w.Sim);
            F5Tick(service).Should().BeTrue();
            // Team Corner in this process: the name is registered when the team is made
            WorldSimulator.RegisterPlayerTeam("Fresh Hall");
            WorldSimulator.IsPlayerTeam("Fresh Hall").Should().BeTrue("at once, before any refresh");
            F5Harm(w, "Fresh Hall").Should().Be(NoHarm);
            w.Npcs.Clear();
            db.ReadPlayerTeamNames(out _).Should().NotContain("Fresh Hall", "the store does not hold it yet");
            F5Tick(service).Should().BeTrue();
            WorldSimulator.IsPlayerTeam("Fresh Hall").Should().BeTrue("the refresh keeps a name registered since the last one");
            // the save lands; from then on the store carries the name
            F5Save(path, "fresh player", "Fresh Hall");
            for (int i = 0; i < 10; i++) { F5Tick(service).Should().BeTrue(); WorldSimulator.IsPlayerTeam("Fresh Hall").Should().BeTrue(); }
        });
        return "ok";
    }

    public static string F5Row5Race(string _)
    {
        var none = new List<string>();
        long first = WorldSimulator.BeginPlayerTeamRefresh();
        WorldSimulator.RegisterPlayerTeam("Racing Hall");            // lands while the store is being read
        WorldSimulator.ReplaceStorePlayerTeams(none, first);
        WorldSimulator.IsPlayerTeam("Racing Hall").Should().BeTrue();
        WorldSimulator.ReplaceStorePlayerTeams(none, WorldSimulator.BeginPlayerTeamRefresh());
        WorldSimulator.IsPlayerTeam("Racing Hall").Should().BeTrue("the read of the first refresh could not have seen it");
        WorldSimulator.ReplaceStorePlayerTeams(none, WorldSimulator.BeginPlayerTeamRefresh());
        WorldSimulator.IsPlayerTeam("Racing Hall").Should().BeFalse();
        // the list ignores case, and leaving a team takes the name out of both sets at once
        WorldSimulator.ReplaceStorePlayerTeams(new[] { "Stored Hall" }, WorldSimulator.BeginPlayerTeamRefresh());
        WorldSimulator.IsPlayerTeam("stored hall").Should().BeTrue();
        WorldSimulator.UnregisterPlayerTeam("STORED HALL");
        WorldSimulator.IsPlayerTeam("Stored Hall").Should().BeFalse();
        return "ok";
    }

    // ---------- row 6 ----------

    private static string F5Readers(string path, SqlSaveBackend db, string team, string password)
    {
        var (exists, correct) = db.VerifyPlayerTeam(team, password).GetAwaiter().GetResult();
        var listed = db.GetPlayerTeams().GetAwaiter().GetResult().FirstOrDefault(t => t.TeamName == team);
        var ranked = db.GetTeamRankingStats(null).GetAwaiter().GetResult().Single(t => t.TeamName == team);
        return $"row={db.HasPlayerTeamRow(team)} join={exists}/{correct} leader={db.GetTeamLeaderUsername(team).GetAwaiter().GetResult() ?? "none"} " +
               $"taken={db.IsTeamNameTaken(team)} warlist={(listed == null ? "absent" : listed.MemberCount.ToString())} " +
               $"ranking={ranked.MemberCount}/{ranked.HasTeamRow} bequest={db.QueueTeamInheritance(team, "an npc", null, 5)}:" +
               F5Text(path, "SELECT COUNT(*) FROM pending_inheritance;") + " siege=" + db.CanTeamSiege(team);
    }

    public static string F5Row6(string _)
    {
        using var w = Empty(80);
        var field = typeof(SaveSystem).GetField("instance", BindingFlags.NonPublic | BindingFlags.Static)!;
        var saveSystemBefore = field.GetValue(null);
        F5Store((path, db) =>
        {
            SaveSystem.InitializeWithBackend(db);
            try
            {
                // a team a player created: every reader as before
                db.CreatePlayerTeam("Made Hall", SqlSaveBackend.HashTeamPassword("secret"), "maker").GetAwaiter().GetResult().Should().BeTrue();
                TeamCornerRig.PlayerRow(path, "maker", "Made Hall");
                F5Readers(path, db, "Made Hall", "secret").Should().Be("row=True join=True/True leader=maker taken=True warlist=1 ranking=1/True bequest=True:1 siege=True");

                // a team NPCs founded, joined by a player through the join path
                Social("Tide One", 40, NpcTeam); Social("Tide Two", 40, NpcTeam);
                var hero = TeamCornerRig.Hero(name: "Joiner");
                TeamCornerRig.PlayerRow(path, GameEngine.InheritanceKey(hero), "");
                TeamCornerLocation.TryTakeTeamSlot(hero, NpcTeam).GetAwaiter().GetResult().Should().BeTrue();
                string rows = F5Text(path, "SELECT group_concat(team_name || '|' || password_hash || '|' || created_by || '|' || created_at, ';') FROM player_teams;");
                // no row: the password join falls through to the password its NPCs hold, it is no war target, it has
                // no leader for a bequest, its wars are not cut off at a creation date, and the rankings show it as before
                const string asBefore = "row=False join=False/False leader=none taken=False warlist=absent ranking=1/False bequest=False:1 siege=True";
                F5Readers(path, db, NpcTeam, "pw").Should().Be(asBefore);

                // the simulator starts, ticks and a session logs that player in: it reads, and writes no row
                var service = F5Service(db, w.Sim);
                service.RefreshPlayerTeamNames().Should().BeTrue();
                WorldSimulator.RegisterPlayerTeam(NpcTeam);
                for (int i = 0; i < 3; i++) F5Tick(service).Should().BeTrue();
                WorldSimulator.IsPlayerTeam(NpcTeam).Should().BeTrue();
                WorldSimulator.IsPlayerTeam("Made Hall").Should().BeTrue();
                F5Text(path, "SELECT group_concat(team_name || '|' || password_hash || '|' || created_by || '|' || created_at, ';') FROM player_teams;").Should().Be(rows);
                F5Readers(path, db, NpcTeam, "pw").Should().Be(asBefore);
            }
            finally { field.SetValue(null, saveSystemBefore); }
        });
        return "ok";
    }

    // ---------- the read ----------

    public static string F5ReadStamp(string _)
    {
        F5Store((path, db) =>
        {
            // a team just created or joined: the stamp is written before the membership reaches the save
            db.CreatePlayerTeam("Stamped Hall", "hash", "a player").GetAwaiter().GetResult().Should().BeTrue();
            db.ReadPlayerTeamNames(out _).Should().BeEquivalentTo(new[] { "Stamped Hall" });
            F5Sql(path, "UPDATE player_teams SET last_join_at = datetime('now', '-9 minutes');");
            db.ReadPlayerTeamNames(out _).Should().BeEquivalentTo(new[] { "Stamped Hall" });
            F5Sql(path, "UPDATE player_teams SET last_join_at = datetime('now', '-11 minutes');");
            db.ReadPlayerTeamNames(out _).Should().BeEmpty("ten minutes on, with no save naming it, it is nobody's team");
            F5Sql(path, "UPDATE player_teams SET last_join_at = NULL;");
            db.ReadPlayerTeamNames(out _).Should().BeEmpty();
        });
        return "ok";
    }

    public static string F5ReadMalformed(string _)
    {
        F5Store((path, db) =>
        {
            db.CreatePlayerTeam("Rowed Hall", "hash", "a player").GetAwaiter().GetResult().Should().BeTrue();
            F5StaleStamp(path, "Rowed Hall");
            F5Save(path, "someone", "Named Hall");
            db.ReadPlayerTeamNames(out _).Should().BeEquivalentTo(new[] { "Named Hall" });
            // a save that cannot be read cannot say which team it names: every team with a row stays a player's
            // (a store from before 1.1.8 can hold one: its players table has no index over the save's fields)
            foreach (string index in F5Text(path, "SELECT group_concat(name, ',') FROM sqlite_master WHERE type = 'index' AND tbl_name = 'players' AND sql LIKE '%json%';").Split(',', StringSplitOptions.RemoveEmptyEntries))
                F5Sql(path, $"DROP INDEX \"{index}\";");
            F5Sql(path, "INSERT INTO players (username, display_name, player_data) VALUES ('broken', 'Broken', '{not json');");
            db.ReadPlayerTeamNames(out string? error).Should().BeEquivalentTo(new[] { "Named Hall", "Rowed Hall" });
            error.Should().BeNull("a malformed save is not a failed read");
            // a team value that is not text is ignored
            F5Sql(path, "DELETE FROM players WHERE username = 'broken';");
            F5Sql(path, "INSERT INTO players (username, display_name, player_data) VALUES ('odd', 'Odd', '{\"player\":{\"team\":7}}');");
            F5Sql(path, "INSERT INTO players (username, display_name, player_data) VALUES ('gone', 'Gone', '{}');");
            db.ReadPlayerTeamNames(out _).Should().BeEquivalentTo(new[] { "Named Hall" });
        });
        return "ok";
    }

    public static string F5ReadArchivedAndBanned(string _)
    {
        F5Store((path, db) =>
        {
            F5Save(path, "banned one", "Banned Hall", banned: true);
            F5Sql(path, "INSERT INTO deleted_characters (username, display_name, player_data, expires_at) VALUES " +
                        "('fallen', 'Fallen', '{\"player\":{\"team\":\"Restorable Hall\"}}', datetime('now', '+1 day'));");
            F5Sql(path, "INSERT INTO deleted_characters (username, display_name, player_data, expires_at) VALUES " +
                        "('lost', 'Lost', '{\"player\":{\"team\":\"Expired Hall\"}}', datetime('now', '-1 day'));");
            db.ReadPlayerTeamNames(out _).Should().BeEquivalentTo(new[] { "Banned Hall", "Restorable Hall" });
        });
        return "ok";
    }

    // ---------- row 9: a session's own loop ----------

    private static void F5WithSaveBackend(SqlSaveBackend db, Action body)
    {
        var field = typeof(SaveSystem).GetField("instance", BindingFlags.NonPublic | BindingFlags.Static)!;
        var before = field.GetValue(null);
        SaveSystem.InitializeWithBackend(db);
        try { body(); }
        finally { field.SetValue(null, before); }
    }

    public static string F5Row9Hold(string _)
    {
        using var w = Empty(82);
        F5Store((path, db) => F5WithSaveBackend(db, () =>
        {
            F5Save(path, "other player", "Other Hall");
            F5BreakStore(path);
            int tick = w.Tick;
            long draws = w.SimRandom.Draws;
            for (int i = 0; i < 3; i++) w.Sim.SessionLoopStep().Should().BeFalse("no read has succeeded: the step is held");
            w.Tick.Should().Be(tick);
            w.SimRandom.Draws.Should().Be(draws);
            w.Sim.SessionLoopFailureLines.Should().Be(1, "three failed reads, one line");
            F5MendStore(path);
            w.Sim.SessionLoopStep().Should().BeTrue();
            w.Tick.Should().BeGreaterThan(tick);
            WorldSimulator.IsPlayerTeam("Other Hall").Should().BeTrue();
            // a later failure keeps the list and the loop goes on
            F5BreakStore(path);
            w.Sim.SessionLoopStep().Should().BeTrue();
            WorldSimulator.IsPlayerTeam("Other Hall").Should().BeTrue();
            F5Harm(w, "Other Hall").Should().Be(NoHarm);
        }));
        return "ok";
    }

    public static string F5Row9SinglePlayer(string _)
    {
        using var w = Empty(83, online: false);
        F5Store((path, db) => F5WithSaveBackend(db, () =>
        {
            // even with a store at hand, a single-player loop reads none
            F5Save(path, "other player", "Stored Hall");
            int tick = w.Tick;
            w.Sim.SessionLoopStep().Should().BeTrue();
            w.Tick.Should().BeGreaterThan(tick, "the step ran");
            WorldSimulator.IsPlayerTeam("Stored Hall").Should().BeFalse("nothing was read");
            F5BreakStore(path);
            w.Sim.SessionLoopStep().Should().BeTrue("and a store that cannot be read holds nothing");
            w.Sim.SessionLoopFailureLines.Should().Be(0);
        }));
        return "ok";
    }

    // ---------- row 10: a live session whose team the store does not name ----------

    public static string F5Row10(string _)
    {
        using var w = Empty(84);
        var engine = GameEngine.Instance;
        var playerBefore = engine.CurrentPlayer;
        try
        {
            F5Store((path, db) =>
            {
                var service = F5Service(db, w.Sim);
                F5Tick(service).Should().BeTrue();
                // a session of this process: its player is on a team, and no save row names it (the character's
                // saves fail, or its first row was never written)
                var player = new Character { Name1 = "Unsaved One", Name2 = "Unsaved One", Team = "Unsaved Hall" };
                engine.CurrentPlayer = player;
                WorldSimulator.RegisterPlayerTeam("Unsaved Hall");
                for (int i = 0; i < 6; i++)
                {
                    F5Tick(service).Should().BeTrue();
                    WorldSimulator.IsPlayerTeam("Unsaved Hall").Should().BeTrue("refresh {0}: the session lives and is on the team", i + 1);
                }
                db.ReadPlayerTeamNames(out _).Should().BeEmpty("the store never named it");
                F5Harm(w, "Unsaved Hall").Should().Be(NoHarm);
                w.Npcs.Clear();

                // the store names it for a while (the creation stamp) and then stops: still kept
                db.CreatePlayerTeam("Unsaved Hall", "hash", "unsaved one").GetAwaiter().GetResult().Should().BeTrue();
                F5Tick(service).Should().BeTrue();
                F5StaleStamp(path, "Unsaved Hall");
                for (int i = 0; i < 3; i++) F5Tick(service).Should().BeTrue();
                WorldSimulator.IsPlayerTeam("Unsaved Hall").Should().BeTrue("the stamp ran out; the session is still on the team");

                // the player leaves the team (or the session ends): two refreshes later the name is gone
                player.Team = "";
                F5Tick(service).Should().BeTrue();
                F5Tick(service).Should().BeTrue();
                WorldSimulator.IsPlayerTeam("Unsaved Hall").Should().BeFalse();
            });
        }
        finally { engine.CurrentPlayer = playerBefore; }
        return "ok";
    }

    // ---------- row 8 ----------

    public static string F5Row8SinglePlayer(string _)
    {
        using var w = Empty(81, online: false);
        (SaveSystem.Instance.Backend is SqlSaveBackend).Should().BeFalse("single-player has no shared store");
        WorldSimulator.RegisterPlayerTeam("Solo Hall");             // the player restore does this at load
        var elder = Social("Solo Elder", 40, "Solo Hall");
        var hire = Social("Solo Hire", 5, "Solo Hall");
        hire.Loyalty = 0;
        for (int i = 0; i < 300; i++) w.Step();
        WorldSimulator.IsPlayerTeam("Solo Hall").Should().BeTrue("nothing refreshes the list in single-player: it stays");
        hire.Team.Should().Be("Solo Hall");
        elder.Team.Should().Be("Solo Hall");
        hire.IsDead.Should().BeFalse();
        w.Npcs.Count(n => n.Team == "Solo Hall").Should().Be(2, "no NPC joined it");
        F5Harm(w, "Solo Hall").Should().Be(NoHarm);
        WorldSimulator.UnregisterPlayerTeam("Solo Hall");           // the player quits the team
        WorldSimulator.IsPlayerTeam("Solo Hall").Should().BeFalse("at once, as before");
        NewsSystem.Instance.ClearCatchUpBuffer();
        return "ok";
    }
}
