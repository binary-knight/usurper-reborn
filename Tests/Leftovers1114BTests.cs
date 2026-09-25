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
}
