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
/// v1.1.14: the shared-record leftovers of 1.1.13 (the v1114 leftovers inventory, rows C5, C12, N4, X1, X4,
/// M1, M2, M3), one fixture over a scratch database and the live roster.
/// </summary>
[Collection("SharedGameSingletons")]
public class Leftovers1114ATests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-left-a-{Guid.NewGuid():N}.db");
    private readonly SqlSaveBackend _db;
    private readonly List<NPC> _rosterBefore;

    private static readonly BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IncludeFields = true,
    };

    public Leftovers1114ATests()
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

    private static List<NPCData> RoundTrip(List<NPCData> data) =>
        JsonSerializer.Deserialize<List<NPCData>>(JsonSerializer.Serialize(data, Json), Json)!;

    private static string Source(string folder, string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Scripts"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "Scripts", folder, file));
    }

    // ─── M3: a restored NPC saved with no Id ───

    [Fact]
    public async Task ARestoredNpcWithNoId_GetsAStableIdOnce_InBothLoaders()
    {
        Npc("npc_m3_a", "Idless Ada");
        Npc("npc_m3_b", "Idless Bo");
        var data = RoundTrip(OnlineStateManager.SerializeCurrentNPCs());
        data.Single(d => d.Name == "Idless Ada").Id = null!;
        data.Single(d => d.Name == "Idless Bo").Id = "";

        async Task Check(Func<List<NPCData>, Task> restore)
        {
            NPCSpawnSystem.Instance.ActiveNPCs.Clear();
            await restore(RoundTrip(data));
            string ada = Find("Idless Ada")!.Id, bo = Find("Idless Bo")!.Id;
            ada.Should().NotBeNullOrEmpty();
            bo.Should().NotBeNullOrEmpty();
            ada.Should().NotBe(bo);
            Guid.TryParse(ada, out _).Should().BeTrue();

            // the same Id in every serialize from now on, so the overlay keys the NPC by it, never by name
            var first = OnlineStateManager.SerializeCurrentNPCs();
            var second = OnlineStateManager.SerializeCurrentNPCs();
            first.Single(d => d.Name == "Idless Ada").Id.Should().Be(ada).And.Be(second.Single(d => d.Name == "Idless Ada").Id);
            first.Single(d => d.Name == "Idless Bo").Id.Should().Be(bo).And.Be(second.Single(d => d.Name == "Idless Bo").Id);

            // once written with it, a reload keeps it
            NPCSpawnSystem.Instance.ActiveNPCs.Clear();
            await restore(RoundTrip(first));
            Find("Idless Ada")!.Id.Should().Be(ada);
        }

        await Check(d => GameEngine.Instance.RestoreNPCs(d));
        var sim = new WorldSimService(_db);
        await Check(d =>
        {
            typeof(WorldSimService).GetMethod("RestoreNPCsFromData", Priv)!.Invoke(sim, new object[] { d });
            return Task.CompletedTask;
        });
    }
}
