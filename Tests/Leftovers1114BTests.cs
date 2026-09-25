using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.14: the follow-ups to the 1.1.14 leftovers (the gaps the first pass left: every owner npcs write adopted
/// by the sim, a world sim without the lock paused, one Id for an Id-less NPC in every process, a stuck delete
/// with no archive, the court politics news localized, the readers of bounty_claims).
/// </summary>
[Collection("SharedGameSingletons")]
public class Leftovers1114BTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-left-b-{Guid.NewGuid():N}.db");
    private readonly SqlSaveBackend _db;
    private readonly List<NPC> _rosterBefore;

    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IncludeFields = true,
    };

    public Leftovers1114BTests()
    {
        _db = new SqlSaveBackend(_path);
        _rosterBefore = NPCSpawnSystem.Instance.ActiveNPCs.ToList();
        NPCSpawnSystem.Instance.ActiveNPCs.Clear();
        OnlineStateManager.NoteRosterRestored(null);
    }

    public void Dispose()
    {
        var roster = NPCSpawnSystem.Instance.ActiveNPCs;
        roster.Clear();
        roster.AddRange(_rosterBefore);
        WorldEditLog.NoteLockOwnerId(null);
        WorldEditLog.OwnerOverride = null;
        OnlineStateManager.NoteRosterRestored(null);
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    private static NPC Npc(string id, string name)
    {
        var npc = new NPC { ID = id, Id = id, Name1 = name, Name2 = name, Level = 10, HP = 100, MaxHP = 100 };
        npc.EnsureSystemsInitialized();
        NPCSpawnSystem.Instance.ActiveNPCs.Add(npc);
        return npc;
    }

    private static NPC? Find(string name) => NPCSpawnSystem.Instance.ActiveNPCs.FirstOrDefault(n => n.Name2 == name);

    private static string Source(string folder, string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Scripts"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "Scripts", folder, file));
    }

    private void Exec(string sql)
    {
        using var conn = new SqliteConnection($"Data Source={_path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private string? Scalar(string sql)
    {
        using var conn = new SqliteConnection($"Data Source={_path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var v = cmd.ExecuteScalar();
        return v == null || v == DBNull.Value ? null : Convert.ToString(v);
    }

    private WorldSimService OwnerSim(string ownerId, long npcVersion)
    {
        _db.TryAcquireWorldSimLock(ownerId).Should().BeTrue();
        WorldEditLog.NoteLockOwnerId(ownerId);
        var sim = new WorldSimService(_db, heartbeatOwnerId: ownerId);
        typeof(WorldSimService).GetField("lastNpcVersion", Priv)!.SetValue(sim, npcVersion);
        OnlineStateManager.NoteRoyalCourtVersion(_db.GetWorldStateVersion("royal_court"));
        return sim;
    }

    private static Task SimSave(WorldSimService sim) => (Task)typeof(WorldSimService).GetMethod("SaveWorldState", Priv)!.Invoke(sim, null)!;

    private static OnlineStateManager Osm(SqlSaveBackend db) =>
        (OnlineStateManager)Activator.CreateInstance(typeof(OnlineStateManager), Priv, null, new object[] { db, "owner_session" }, null)!;

    // ─── M2 follow-up: the owner's whole-roster save is adopted by the sim of its process ───

    [Fact]
    public async Task TheOwnersWholeRosterSave_IsAdopted_AndTheSimsUnsavedChangesSurvive()
    {
        var keeper = Npc("npc_m2b_k", "M2b Keeper");
        var smith = Npc("npc_m2b_s", "M2b Smith");
        await _db.SaveWorldState(OnlineStateManager.KEY_NPCS, JsonSerializer.Serialize(OnlineStateManager.SerializeCurrentNPCs(), Json));
        long loaded = _db.GetWorldStateVersion(OnlineStateManager.KEY_NPCS);
        OnlineStateManager.NoteRosterRestored(loaded);
        var sim = OwnerSim("owner_m2b", loaded);
        WorldEditLog.OwnerOverride = true;   // the MUD process: its sessions' saves are the owner's
        var osm = Osm(_db);

        // a player's save in the MUD process writes the whole live roster (SaveSystem, SaveAllSharedState)
        smith.Gold = 777;
        var (data, generation) = OnlineStateManager.SnapshotLiveRoster();
        await osm.SaveSharedNPCs(data, generation);
        _db.GetWorldStateVersion(OnlineStateManager.KEY_NPCS).Should().Be(loaded + 1);
        OnlineStateManager.LiveRosterVersion.Should().Be(loaded + 1, "the owner's write notes the version it wrote");

        // the sim changes an NPC after that write, then saves: adopted, not reloaded
        keeper.Level = 43;
        await SimSave(sim);
        Find("M2b Keeper")!.Level.Should().Be(43);
        var stored = JsonSerializer.Deserialize<List<NPCData>>((await _db.LoadWorldState(OnlineStateManager.KEY_NPCS))!, Json)!;
        stored.Single(d => d.Name == "M2b Keeper").Level.Should().Be(43);
        stored.Single(d => d.Name == "M2b Smith").Gold.Should().Be(777);

        // the other owner path (SaveAllSharedState) does the same
        keeper.Level = 44;
        await osm.SaveAllSharedState();
        OnlineStateManager.LiveRosterVersion.Should().Be(_db.GetWorldStateVersion(OnlineStateManager.KEY_NPCS));
        keeper.Level = 45;
        await SimSave(sim);
        Find("M2b Keeper")!.Level.Should().Be(45);
    }

    [Fact]
    public void EveryOwnerRosterWrite_PassesItsGeneration()
    {
        Source("Systems", "SaveSystem.cs").Should().Contain("await OnlineStateManager.Instance.SaveSharedNPCs(sharedNpcData, generation);")
            .And.Contain("var (sharedNpcData, generation) = OnlineStateManager.SnapshotLiveRoster();");
        var osm = Source("Systems", "OnlineStateManager.cs");
        osm.Should().Contain("await SaveSharedNPCs(npcData, generation);").And.Contain("var (npcData, generation) = SnapshotLiveRoster();");
        // the owner's write is the only unconditional npcs write, and it notes its version
        osm.Split("SaveWorldStateReturningVersion(KEY_NPCS, json)").Length.Should().Be(2);
    }

    // ─── M1 follow-up: a door's or the standalone world sim pauses while another process holds the lock ───

    [Fact]
    public async Task AWorldSimThatLostTheLock_PausesAndWritesNothing_ThenReloadsWhenItTakesItBack()
    {
        var npc = Npc("npc_m1b_1", "M1b Baker");
        await _db.SaveWorldState(OnlineStateManager.KEY_NPCS, JsonSerializer.Serialize(OnlineStateManager.SerializeCurrentNPCs(), Json));
        long loaded = _db.GetWorldStateVersion(OnlineStateManager.KEY_NPCS);
        _db.TryAcquireWorldSimLock("door_sim").Should().BeTrue();
        var sim = new WorldSimService(_db, heartbeatOwnerId: "door_sim", pauseWithoutLock: true);
        typeof(WorldSimService).GetField("lastNpcVersion", Priv)!.SetValue(sim, loaded);
        (await sim.BeatWorldSimLockAsync()).Should().BeTrue("it holds the lock");

        // the MUD takes the lock over at its start and writes the roster
        _db.TakeOverWorldSimLock("mud_1");
        (await sim.BeatWorldSimLockAsync()).Should().BeFalse("paused: another process holds the lock");
        sim.PausedWithoutLock.Should().BeTrue();
        _db.WorldSimLockOwner().Should().Be("mud_1", "the paused sim does not take it back while the MUD beats");
        var mudRoster = JsonSerializer.Deserialize<List<NPCData>>((await _db.LoadWorldState(OnlineStateManager.KEY_NPCS))!, Json)!;
        mudRoster.Single().Gold = 5150;
        await _db.SaveWorldState(OnlineStateManager.KEY_NPCS, JsonSerializer.Serialize(mudRoster, Json));
        long mudVersion = _db.GetWorldStateVersion(OnlineStateManager.KEY_NPCS);

        // the paused sim writes nothing, neither the periodic save nor the final one on shutdown
        npc.Level = 77;
        await SimSave(sim);
        _db.GetWorldStateVersion(OnlineStateManager.KEY_NPCS).Should().Be(mudVersion);
        (await _db.LoadWorldState(OnlineStateManager.KEY_MARRIAGES)).Should().BeNull("no record at all is written");
        (await sim.BeatWorldSimLockAsync()).Should().BeFalse();

        // the MUD stops beating: the lock goes stale, the next beat takes it through the normal CAS and the sim
        // loads the records the MUD wrote before it ticks again
        var stale = JsonSerializer.Serialize(new { owner = "mud_1", heartbeat = DateTime.UtcNow.AddMinutes(-5).ToString("o"), pid = 1, acquired = DateTime.UtcNow.AddMinutes(-60).ToString("o") });
        Exec($"UPDATE world_state SET value = '{stale}' WHERE key = 'worldsim_lock';");
        (await sim.BeatWorldSimLockAsync()).Should().BeTrue();
        sim.PausedWithoutLock.Should().BeFalse();
        _db.WorldSimLockOwner().Should().Be("door_sim");
        Find("M1b Baker")!.Gold.Should().Be(5150, "the MUD's roster was loaded");
        ((long)typeof(WorldSimService).GetField("lastNpcVersion", Priv)!.GetValue(sim)!).Should().Be(mudVersion);
    }

    /// <summary>Every world_state row but the lock, with its version and value: what a process has written.</summary>
    private Dictionary<string, string> WorldStateRows()
    {
        var rows = new Dictionary<string, string>();
        using var conn = new SqliteConnection($"Data Source={_path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT key, version, value FROM world_state WHERE key != 'worldsim_lock';";
        using var r = cmd.ExecuteReader();
        while (r.Read()) rows[r.GetString(0)] = r.GetInt64(1) + ":" + r.GetString(2);
        return rows;
    }

    [Fact]
    public async Task AWorldSimThatLosesTheLockMidRun_WritesNothingAfterwards_NotEvenOnShutdown()
    {
        var kingBefore = CastleLocation.GetCurrentKing();
        var courtVersionBefore = OnlineStateManager.RoyalCourtVersion;
        try
        {
            var npc = Npc("npc_m1c_1", "M1c Miller");
            await _db.SaveWorldState(OnlineStateManager.KEY_NPCS, JsonSerializer.Serialize(OnlineStateManager.SerializeCurrentNPCs(), Json));
            long loaded = _db.GetWorldStateVersion(OnlineStateManager.KEY_NPCS);
            _db.TryAcquireWorldSimLock("door_sim_c").Should().BeTrue();
            // a save is due on every pass, so an unpaused pass would write at once
            var sim = new WorldSimService(_db, saveIntervalMinutes: 0, heartbeatOwnerId: "door_sim_c", pauseWithoutLock: true);
            typeof(WorldSimService).GetField("lastNpcVersion", Priv)!.SetValue(sim, loaded);
            (await sim.BeatWorldSimLockAsync()).Should().BeTrue("running: it holds the lock");

            // mid-run the MUD takes the lock over and writes the roster and the court
            _db.TakeOverWorldSimLock("mud_c");
            var mudRoster = JsonSerializer.Deserialize<List<NPCData>>((await _db.LoadWorldState(OnlineStateManager.KEY_NPCS))!, Json)!;
            mudRoster.Single().Gold = 6060;
            await _db.SaveWorldState(OnlineStateManager.KEY_NPCS, JsonSerializer.Serialize(mudRoster, Json));
            await _db.SaveWorldState("royal_court", JsonSerializer.Serialize(new RoyalCourtSaveData
                { KingName = "Mud Monarch", KingAI = (int)CharacterAI.Computer, Treasury = 9000, TaxRate = 7 }, Json));
            var before = WorldStateRows();

            // the sim's own changes are pending; its passes and its shutdown write none of them
            npc.Level = 88;
            bool ticked = await sim.RunOneTickAsync();
            ticked |= await sim.RunOneTickAsync();
            await sim.ShutdownAsync();

            WorldStateRows().Should().BeEquivalentTo(before, "no world_state row (npcs, royal_court, marriages, children, events) is written by the paused sim");
            ticked.Should().BeFalse("paused: no tick");
            _db.WorldSimLockOwner().Should().Be("mud_c", "its shutdown does not release a lock it does not hold");
        }
        finally
        {
            CastleLocation.SetKing(kingBefore!);
            OnlineStateManager.NoteRoyalCourtVersion(courtVersionBefore);
        }
    }

    [Fact]
    public async Task TheMudsWorldSim_NeverPauses()
    {
        _db.TakeOverWorldSimLock("someone_else");
        var sim = new WorldSimService(_db, heartbeatOwnerId: "mud_2");
        (await sim.BeatWorldSimLockAsync()).Should().BeTrue("the MUD owns the shared records by design");
        sim.PausedWithoutLock.Should().BeFalse();
    }

    [Fact]
    public void TheLoop_BeatsBeforeItTicks_AndTheDoorAndStandaloneSimsPause()
    {
        var sim = Source("Systems", "WorldSimService.cs");
        int loop = sim.IndexOf("while (!cancellationToken.IsCancellationRequested)", StringComparison.Ordinal);
        int run = sim.IndexOf("public async Task RunAsync", StringComparison.Ordinal);
        sim.IndexOf("await RunOneTickAsync();", loop, StringComparison.Ordinal).Should().BeGreaterThan(loop);
        sim.IndexOf("await ShutdownAsync();", loop, StringComparison.Ordinal).Should().BeGreaterThan(loop, "the loop's finally");
        run.Should().BeGreaterThan(0);
        int tick = sim.IndexOf("internal async Task<bool> RunOneTickAsync()", StringComparison.Ordinal);
        int beat = sim.IndexOf("if (!await BeatWorldSimLockAsync()) return false;", tick, StringComparison.Ordinal);
        beat.Should().BeGreaterThan(tick).And.BeLessThan(sim.IndexOf("worldSimulator?.SimulateStep();", tick, StringComparison.Ordinal));
        sim.IndexOf("if (PausedWithoutLock)", sim.IndexOf("private async Task SaveWorldState()", StringComparison.Ordinal), StringComparison.Ordinal)
            .Should().BeGreaterThan(0);

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Console"))) dir = dir.Parent;
        var program = File.ReadAllText(Path.Combine(dir!.FullName, "Console", "Bootstrap", "Program.cs"));
        program.Split("pauseWithoutLock: true").Length.Should().Be(3, "the door's embedded sim and the standalone sim");
        Source("Server", "MudServer.cs").Should().NotContain("pauseWithoutLock");
    }

    // ─── M3 follow-up: every process gives an Id-less record the same Id ───

    private static List<NPCData> RoundTrip(List<NPCData> data) =>
        JsonSerializer.Deserialize<List<NPCData>>(JsonSerializer.Serialize(data, Json), Json)!;

    [Fact]
    public async Task TwoProcessesRestoringTheSameIdlessRecord_GiveItTheSameId()
    {
        Npc("npc_m3b_a", "M3b Ada");
        Npc("npc_m3b_b", "M3b Bo");
        var stored = RoundTrip(OnlineStateManager.SerializeCurrentNPCs());
        stored.Single(d => d.Name == "M3b Ada").Id = null!;
        stored.Single(d => d.Name == "M3b Bo").Id = "";

        async Task<(string Ada, string Bo)> Restore(Func<List<NPCData>, Task> restore)
        {
            NPCSpawnSystem.Instance.ActiveNPCs.Clear();
            await restore(RoundTrip(stored));
            return (Find("M3b Ada")!.Id, Find("M3b Bo")!.Id);
        }

        // a door process's loader, the world sim's loader in another process, and a later restore
        var door = await Restore(d => GameEngine.Instance.RestoreNPCs(d));
        var sim = new WorldSimService(_db);
        var worldSim = await Restore(d =>
        {
            typeof(WorldSimService).GetMethod("RestoreNPCsFromData", Priv)!.Invoke(sim, new object[] { d });
            return Task.CompletedTask;
        });
        var again = await Restore(d => GameEngine.Instance.RestoreNPCs(d));

        worldSim.Should().Be(door, "both processes derive the Id from the same record");
        again.Should().Be(door);
        door.Ada.Should().NotBe(door.Bo);
        door.Ada.Should().Be(NPC.LegacyIdFor("M3b Ada", "npc_m3b_a"));
        Guid.Parse(door.Ada).ToString("D")[14].Should().Be('5', "a name-based (version 5) UUID");

        // a record that has an Id keeps it
        NPC.LegacyIdFor("M3b Ada", "npc_m3b_a").Should().NotBe(NPC.LegacyIdFor("M3b Ada", "npc_other"));
        stored.Single(d => d.Name == "M3b Ada").Id = "kept_id";
        (await Restore(d => GameEngine.Instance.RestoreNPCs(d))).Ada.Should().Be("kept_id");
    }

    // ─── X1 follow-up: a stuck delete with no archive ───

    private int StuckDelete(string user, string? args)
    {
        Exec("INSERT INTO players (username, display_name, player_data) VALUES ('" + user + "', 'Shown " + user + "', '{}');");
        using var conn = new SqliteConnection($"Data Source={_path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO admin_commands (command, target_username, args, status, created_at) " +
                          "VALUES ('delete_player', @u, @a, 'executing', datetime('now', '-9 days'));";
        cmd.Parameters.AddWithValue("@u", user);
        cmd.Parameters.AddWithValue("@a", (object?)args ?? DBNull.Value);
        cmd.ExecuteNonQuery();
        return int.Parse(Scalar("SELECT MAX(id) FROM admin_commands;")!);
    }

    [Fact]
    public async Task AStuckDeleteWithNoArchive_QueuesItsPurgeFromTheRequest_OrFailsWithAReasonTheAdminCanRead()
    {
        // the save was emptied and the archive is gone (expired after 7 days, or never written)
        int named = StuckDelete("x1b_named", "{\"name2\":\"X1b Named\",\"display_name\":\"X1b Shown\",\"player_id\":\"id_x1b\"}");
        int bare = StuckDelete("x1b_bare", null);
        int noName = StuckDelete("x1b_noname", "{\"name2\":null,\"display_name\":\"Shown\",\"player_id\":null}");
        string createdAt = Scalar($"SELECT created_at FROM admin_commands WHERE id = {named};")!;

        (await UsurperRemake.Server.MudServer.RecoverStuckAdminCommandsAsync(_db, _ => false, _ => Task.CompletedTask)).Should().Be(3);

        Scalar($"SELECT status FROM admin_commands WHERE id = {named};").Should().Be("executed");
        Scalar("SELECT name2 FROM pending_purges WHERE username = 'x1b_named';").Should().Be("X1b Named", "named from the request's own record");
        Scalar("SELECT player_id FROM pending_purges WHERE username = 'x1b_named';").Should().Be("id_x1b");
        Scalar("SELECT display_name FROM pending_purges WHERE username = 'x1b_named';").Should().Be("X1b Shown");
        Scalar("SELECT deleted_at FROM pending_purges WHERE username = 'x1b_named';").Should().Be(createdAt, "the request's time");

        foreach (var id in new[] { bare, noName })
        {
            Scalar($"SELECT status FROM admin_commands WHERE id = {id};").Should().Be("failed");
            var reason = Scalar($"SELECT result FROM admin_commands WHERE id = {id};")!;
            reason.Should().Be(UsurperRemake.Server.MudServer.StuckDeleteUnrecoverable);
            reason.Should().Contain("world purge").And.Contain("Delete the account again");
        }
        Scalar("SELECT COUNT(*) FROM pending_purges WHERE username IN ('x1b_bare', 'x1b_noname');").Should().Be("0");
    }

    [Fact]
    public void TheWebDelete_WritesTheCharactersNamesAndId_IntoTheCommand()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "web"))) dir = dir.Parent;
        var js = File.ReadAllText(Path.Combine(dir!.FullName, "web", "ssh-proxy.js"));
        int route = js.IndexOf("// DELETE /api/admin/players/:username", StringComparison.Ordinal);
        var body = js.Substring(route, js.IndexOf("// Fallback: queue the world purge", route, StringComparison.Ordinal) - route);
        body.Should().Contain("purgeArgs = JSON.stringify({ name2: named.name2 || null, display_name: named.display_name || null, player_id: named.player_id || null });")
            .And.Contain(".run('delete_player', playerUsername, purgeArgs, 'admin-web');");
        // the game server reads the same field names
        UsurperRemake.Server.MudServer.DeletePurgeArgs("{\"name2\":\"A\",\"display_name\":\"B\",\"player_id\":\"C\"}").Should().Be(("A", "B", "C"));
        UsurperRemake.Server.MudServer.DeletePurgeArgs("{\"reason\":\"x\"}").Should().BeNull();
        UsurperRemake.Server.MudServer.DeletePurgeArgs("not json").Should().BeNull();
    }
}
