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

    // ─── N4: the login payout for a quest on a dead NPC ───

    /// <summary>Runs body with this fixture's database as the save backend (the claim is written there).</summary>
    private void WithSqlSaves(Action body)
    {
        var field = typeof(SaveSystem).GetField("instance", BindingFlags.NonPublic | BindingFlags.Static)!;
        var before = field.GetValue(null);
        SaveSystem.InitializeWithBackend(_db);
        try { body(); }
        finally { field.SetValue(null, before); }
    }

    private static (Character player, Quest quest) DeadTargetQuest(string questId)
    {
        var player = new Character { Name1 = "n4_hunter", Name2 = "N4 Hunter", Level = 20, Gold = 0 };
        var quest = new Quest
        {
            Id = questId, Title = "WANTED: N4 Mark", Initiator = "The Crown", QuestTarget = QuestTarget.DefeatNPC,
            TargetNPCName = "N4 Mark", Occupier = player.Name2, Date = DateTime.Now, DaysToComplete = 30
        };
        player.ActiveQuests.Add(quest);
        return (player, quest);
    }

    [Fact]
    public void TheDeadTargetQuestPayout_PaysOnce_AcrossTwoProcesses()
    {
        WithSqlSaves(() =>
        {
            // the same character's quest, held by two processes (or replayed after a crash before the save)
            var (first, quest) = DeadTargetQuest("q_n4_once");
            var (second, copy) = DeadTargetQuest("q_n4_once");

            GameEngine.SettleDeadNpcQuest(first, quest, out long paid).Should().BeTrue();
            paid.Should().BeGreaterThan(0);
            first.Gold.Should().Be(paid);
            first.ActiveQuests.Should().NotContain(quest);
            quest.Deleted.Should().BeTrue();

            GameEngine.SettleDeadNpcQuest(second, copy, out long again).Should().BeFalse("the other process's claim landed first");
            again.Should().Be(0);
            second.Gold.Should().Be(0, "paid once");
            second.RoyQuests.Should().Be(0);
            second.ActiveQuests.Should().NotContain(copy, "it is settled; removed unpaid");

            // the claim is the one the bounty payouts take: a bounty paid first leaves nothing to pay here
            _db.TryClaimBounty("q_n4_bounty", "N4 Other").Should().BeTrue();
            var (third, bounty) = DeadTargetQuest("q_n4_bounty");
            GameEngine.SettleDeadNpcQuest(third, bounty, out _).Should().BeFalse();
            third.Gold.Should().Be(0);
        });
    }

    [Fact]
    public void TheDeadTargetQuestPayout_WhenTheClaimCannotBeWritten_LeavesTheQuest_Unpaid()
    {
        WithSqlSaves(() =>
        {
            using (var conn = new SqliteConnection($"Data Source={_path}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DROP TABLE bounty_claims;";
                cmd.ExecuteNonQuery();
            }
            var (player, quest) = DeadTargetQuest("q_n4_busy");
            GameEngine.SettleDeadNpcQuest(player, quest, out _).Should().BeNull();
            player.Gold.Should().Be(0);
            player.ActiveQuests.Should().Contain(quest, "left for the next login");
            quest.Deleted.Should().BeFalse();
        });
    }
}
