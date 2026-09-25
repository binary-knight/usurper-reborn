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

    // ─── X4: a forget edit is marked applied only once both records hold it ───

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

    /// <summary>The owner's world sim, holding the lock, with its npcs baseline at this version.</summary>
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

    private const string BlockMarriages =
        "CREATE TRIGGER block_marriages_upd BEFORE UPDATE ON world_state WHEN NEW.key = 'marriages' BEGIN SELECT RAISE(ABORT, 'blocked'); END; " +
        "CREATE TRIGGER block_marriages_ins BEFORE INSERT ON world_state WHEN NEW.key = 'marriages' BEGIN SELECT RAISE(ABORT, 'blocked'); END;";

    [Fact]
    public async Task AForgetEdit_IsNotMarkedApplied_UntilTheMarriagesRecordIsSaved()
    {
        var wife = Npc("npc_x4_w", "X4 Wife");
        wife.SpouseName = "X4 Bob"; wife.Married = true; wife.IsMarried = true;
        NPCMarriageRegistry.Instance.RegisterMarriage("player_x4_bob", wife.ID, "X4 Bob", "X4 Wife");
        try
        {
            await _db.SaveWorldState(OnlineStateManager.KEY_NPCS, JsonSerializer.Serialize(OnlineStateManager.SerializeCurrentNPCs(), Json));

            // a door process (not the owner) deletes X4 Bob: the edit, the local clean-up, the versioned write
            await PermadeathHelper.PurgeDeletedCharacterAsync(_db, "x4_bob_account", "X4 Bob");
            Scalar("SELECT COUNT(*) FROM world_edits WHERE kind = 'forget_character';").Should().Be("1");
            long clean = _db.GetWorldStateVersion(OnlineStateManager.KEY_NPCS);

            // the owner's save: the npcs write lands, the marriages record does not
            var sim = OwnerSim("owner_x4", clean);
            Exec(BlockMarriages);
            await SimSave(sim);
            _db.GetWorldStateVersion(OnlineStateManager.KEY_NPCS).Should().BeGreaterThan(clean, "the npcs write landed");
            Scalar("SELECT applied_at FROM world_edits WHERE kind = 'forget_character';").Should().BeNull(
                "the marriages record does not hold the edit yet, so a restart must still re-apply it");

            // the next save writes both, then marks it
            Exec("DROP TRIGGER block_marriages_upd; DROP TRIGGER block_marriages_ins;");
            await SimSave(sim);
            Scalar("SELECT applied_at FROM world_edits WHERE kind = 'forget_character';").Should().NotBeNull();
            Scalar("SELECT applied_by FROM world_edits WHERE kind = 'forget_character';").Should().Be("owner_x4");
            (await _db.LoadWorldState(OnlineStateManager.KEY_MARRIAGES))!.Should().NotContain("npc_x4_w", "the stored record has no such marriage");
        }
        finally { NPCMarriageRegistry.Instance.EndMarriage(wife.ID); }
    }

    // ─── M2: the sim adopts a version its own process wrote ───

    [Fact]
    public async Task ThisProcesssPurgeWrite_IsAdopted_AndTheSimsUnsavedChangesSurvive()
    {
        var keeper = Npc("npc_m2_k", "M2 Keeper");
        var target = Npc("npc_m2_t", "M2 Target");
        await _db.SaveWorldState(OnlineStateManager.KEY_NPCS, JsonSerializer.Serialize(OnlineStateManager.SerializeCurrentNPCs(), Json));
        long loaded = _db.GetWorldStateVersion(OnlineStateManager.KEY_NPCS);
        OnlineStateManager.NoteRosterRestored(loaded);   // the sim loaded the live roster at this version
        var sim = OwnerSim("owner_m2", loaded);
        WorldEditLog.OwnerOverride = true;

        // a purge in this process writes the live roster at once (loaded + 1)
        (await OnlineStateManager.PersistNpcWorldNow(_db, () => { target.Gold = 4242; return 1; }, new HashSet<string>())).Should().Be(1);
        _db.GetWorldStateVersion(OnlineStateManager.KEY_NPCS).Should().Be(loaded + 1);
        OnlineStateManager.LiveRosterVersion.Should().Be(loaded + 1);

        // the sim changes an NPC after that write, then saves
        keeper.Level = 42;
        await SimSave(sim);

        Find("M2 Keeper")!.Level.Should().Be(42, "the version came from this process's own roster, so it is not reloaded over the sim's change");
        var stored = JsonSerializer.Deserialize<List<NPCData>>((await _db.LoadWorldState(OnlineStateManager.KEY_NPCS))!, Json)!;
        stored.Single(d => d.Name == "M2 Keeper").Level.Should().Be(42, "the sim's change is written");
        stored.Single(d => d.Name == "M2 Target").Gold.Should().Be(4242, "the purge's change is kept");
        _db.GetWorldStateVersion(OnlineStateManager.KEY_NPCS).Should().Be(loaded + 2);
        ((long)typeof(WorldSimService).GetField("lastNpcVersion", Priv)!.GetValue(sim)!).Should().Be(loaded + 2);

        // another process's write is still reloaded
        var other = JsonSerializer.Deserialize<List<NPCData>>(JsonSerializer.Serialize(stored, Json), Json)!;
        other.Single(d => d.Name == "M2 Target").Gold = 99;
        await _db.SaveWorldState(OnlineStateManager.KEY_NPCS, JsonSerializer.Serialize(other, Json));
        await SimSave(sim);
        Find("M2 Target")!.Gold.Should().Be(99, "a version this process did not write is reloaded");
    }

    // ─── M1: the world sim heartbeat is a compare-and-swap ───

    [Fact]
    public void TheHeartbeat_KeepsOnlyALockThatIsFreeStaleOrOurs()
    {
        _db.UpdateWorldSimHeartbeat("sim_a").Should().BeTrue("a free lock is taken");
        _db.WorldSimLockOwner().Should().Be("sim_a");
        WorldEditLog.NoteLockOwnerId("sim_a");
        WorldEditLog.IsOwnerProcess(_db).Should().BeTrue();

        // a second world sim on the same database beats: it does not take a held, fresh lock
        _db.UpdateWorldSimHeartbeat("sim_b").Should().BeFalse();
        _db.WorldSimLockOwner().Should().Be("sim_a");
        WorldEditLog.IsOwnerProcess(_db).Should().BeTrue("the owner does not flip with the other sim's beat");
        _db.UpdateWorldSimHeartbeat("sim_a").Should().BeTrue();
        _db.UpdateWorldSimHeartbeat("sim_b").Should().BeFalse();
        _db.WorldSimLockOwner().Should().Be("sim_a");

        // a stale lock (its holder stopped beating) is taken
        var stale = JsonSerializer.Serialize(new { owner = "sim_a", heartbeat = DateTime.UtcNow.AddMinutes(-5).ToString("o"), pid = 1, acquired = DateTime.UtcNow.AddMinutes(-60).ToString("o") });
        Exec($"UPDATE world_state SET value = '{stale}' WHERE key = 'worldsim_lock';");
        _db.UpdateWorldSimHeartbeat("sim_b").Should().BeTrue();
        _db.WorldSimLockOwner().Should().Be("sim_b");
        WorldEditLog.IsOwnerProcess(_db).Should().BeFalse("sim_a lost it");
        _db.UpdateWorldSimHeartbeat("sim_a").Should().BeFalse("and cannot beat it back while sim_b beats");

        // the MUD (or the standalone world sim) takes a fresh lock over at its start; the loser stops claiming it
        _db.TakeOverWorldSimLock("mud_1");
        _db.WorldSimLockOwner().Should().Be("mud_1");
        _db.UpdateWorldSimHeartbeat("sim_b").Should().BeFalse();
        _db.UpdateWorldSimHeartbeat("mud_1").Should().BeTrue();
        _db.WorldSimLockOwner().Should().Be("mud_1");
    }

    [Fact]
    public void TheMudAndTheStandaloneSim_TakeOverAHeldLock_AtTheirStart()
    {
        var mud = Source("Server", "MudServer.cs");
        int acquire = mud.IndexOf("if (!sqlBackend.TryAcquireWorldSimLock(worldSimOwnerId))", StringComparison.Ordinal);
        acquire.Should().BeGreaterThan(0);
        mud.Substring(acquire, 300).Should().Contain("sqlBackend.TakeOverWorldSimLock(worldSimOwnerId);");

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Console"))) dir = dir.Parent;
        var program = File.ReadAllText(Path.Combine(dir!.FullName, "Console", "Bootstrap", "Program.cs"));
        int standalone = program.IndexOf("if (!sqlBackend.TryAcquireWorldSimLock(ownerId))", StringComparison.Ordinal);
        standalone.Should().BeGreaterThan(0);
        program.Substring(standalone, 300).Should().Contain("sqlBackend.TakeOverWorldSimLock(ownerId);");
        // a door's embedded world sim never takes a held lock over
        program.Should().NotContain("TakeOverWorldSimLock(worldSimOwnerId)");
    }

    // ─── X1: admin commands a stopped game server left 'executing' ───

    private UsurperRemake.Server.MudServer AdminOnlyServer()
    {
        var t = typeof(UsurperRemake.Server.MudServer);
        var server = (UsurperRemake.Server.MudServer)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t);
        t.GetField("_sqlBackend", Priv)!.SetValue(server, _db);
        t.GetField("<ActiveSessions>k__BackingField", Priv)!.SetValue(server,
            new System.Collections.Concurrent.ConcurrentDictionary<string, UsurperRemake.Server.PlayerSession>());
        return server;
    }

    private void Account(string user, string name2, string id) =>
        Exec($"INSERT INTO players (username, display_name, player_data) VALUES ('{user}', '{name2}', " +
             $"'{{\"player\":{{\"name2\":\"{name2}\",\"id\":\"{id}\"}}}}');");

    private int Command(string command, string? target, string status, string createdAgo)
    {
        Exec($"INSERT INTO admin_commands (command, target_username, status, created_at) VALUES " +
             $"('{command}', {(target == null ? "NULL" : $"'{target}'")}, '{status}', datetime('now', '{createdAgo}'));");
        return int.Parse(Scalar("SELECT MAX(id) FROM admin_commands;")!);
    }

    private string? Status(int id) => Scalar($"SELECT status FROM admin_commands WHERE id = {id};");

    [Fact]
    public async Task StuckAdminCommands_AreRecovered_ToAFinalStatus_Once()
    {
        // the delete never ran: the account still holds its save
        Account("x1_unrun", "X1 Unrun", "id_x1_unrun");
        int unrun = Command("delete_player", "x1_unrun", "executing", "-5 minutes");
        // the delete landed (archived, emptied), and the game server stopped before its purge was marked
        Account("x1_landed", "X1 Landed", "id_x1_landed");
        int landed = Command("delete_player", "x1_landed", "executing", "-5 minutes");
        _db.DeleteGameData("x1_landed").Should().BeTrue();
        // the delete stopped between its archive and emptying the save: the save is still there
        Account("x1_half", "X1 Half", "id_x1_half");
        int half = Command("delete_player", "x1_half", "executing", "-5 minutes");
        Exec("INSERT INTO deleted_characters (username, display_name, player_data, expires_at) " +
             "SELECT username, display_name, player_data, datetime('now', '+7 days') FROM players WHERE username = 'x1_half';");
        // nothing to delete: no save and no archive
        Exec("INSERT INTO players (username, display_name, player_data) VALUES ('x1_empty', 'X1 Empty', '{}');");
        int empty = Command("delete_player", "x1_empty", "executing", "-5 minutes");
        // another command is not run again late
        int kick = Command("kick", "x1_unrun", "executing", "-5 minutes");
        // claimed moments ago (a live claim), or running in this process: left alone
        Account("x1_fresh", "X1 Fresh", "id_x1_fresh");
        int fresh = Command("delete_player", "x1_fresh", "executing", "-2 seconds");
        int mine = Command("delete_player", "x1_fresh", "executing", "-5 minutes");

        var server = AdminOnlyServer();
        var reran = new List<int>();
        Task Rerun(AdminCommand cmd)
        {
            reran.Add(cmd.Id);
            return (Task)typeof(UsurperRemake.Server.MudServer).GetMethod("RunClaimedAdminCommand", Priv)!.Invoke(server, new object[] { cmd })!;
        }

        (await UsurperRemake.Server.MudServer.RecoverStuckAdminCommandsAsync(_db, id => id == mine, Rerun)).Should().Be(5);

        Status(unrun).Should().Be("executed", "the delete that never ran is run now");
        Scalar("SELECT player_data FROM players WHERE username = 'x1_unrun';").Should().Be("{}");
        reran.Should().Equal(new[] { unrun, half }, "only a delete whose save is still there is run again");
        Status(half).Should().Be("executed", "an archive row beside a live save is a delete that did not land");
        Scalar("SELECT player_data FROM players WHERE username = 'x1_half';").Should().Be("{}");

        Status(landed).Should().Be("executed", "the delete had landed");
        Scalar("SELECT COUNT(*) FROM pending_purges WHERE username = 'x1_landed';").Should().Be("1", "its world purge is queued for the drain");
        Scalar("SELECT name2 FROM pending_purges WHERE username = 'x1_landed';").Should().Be("X1 Landed", "named from the archived save");
        Scalar("SELECT player_id FROM pending_purges WHERE username = 'x1_landed';").Should().Be("id_x1_landed");
        Scalar("SELECT COUNT(*) FROM deleted_characters WHERE username = 'x1_landed';").Should().Be("1", "not deleted and archived a second time");

        Status(empty).Should().Be("failed");
        Status(kick).Should().Be("failed");
        Status(fresh).Should().Be("executing");
        Status(mine).Should().Be("executing");

        // a second sweep finds nothing left to recover; the queued purge drains once
        (await UsurperRemake.Server.MudServer.RecoverStuckAdminCommandsAsync(_db, id => id == mine, Rerun)).Should().Be(0);
        (await UsurperRemake.Server.MudServer.DrainPendingPurgesAsync(_db)).Should().Be(1);
    }

    [Fact]
    public void AStuckCommand_IsTakenOverOnce()
    {
        int id = Command("delete_player", "x1_twice", "executing", "-5 minutes");
        _db.TryClaimStuckAdminCommand(id, 60).Should().BeTrue();
        _db.TryClaimStuckAdminCommand(id, 60).Should().BeFalse("another recovery stamped it moments ago");
        _db.GetStuckAdminCommands(60).Should().NotContain(c => c.Id == id);
        // a recovery that stopped too is taken over once its stamp is old
        Exec($"UPDATE admin_commands SET executed_at = datetime('now', '-5 minutes') WHERE id = {id};");
        _db.GetStuckAdminCommands(60).Should().Contain(c => c.Id == id);
        _db.TryClaimStuckAdminCommand(id, 60).Should().BeTrue();
    }

    [Fact]
    public void TheAdminPoller_RecoversStuckCommands_BeforeItDrainsQueuedPurges_OnceTheWorldIsLoaded()
    {
        var mud = Source("Server", "MudServer.cs");
        int poller = mud.IndexOf("private async Task AdminCommandPollerAsync", StringComparison.Ordinal);
        int gate = mud.IndexOf("InitializationComplete.Task.IsCompletedSuccessfully == true", poller, StringComparison.Ordinal);
        int recover = mud.IndexOf("await RecoverStuckAdminCommandsAsync(_sqlBackend,", poller, StringComparison.Ordinal);
        int drain = mud.IndexOf("await DrainPendingPurgesAsync(_sqlBackend);", poller, StringComparison.Ordinal);
        recover.Should().BeGreaterThan(gate).And.BeLessThan(drain);
        mud.Should().Contain("AdminCommandsRunningHere[cmd.Id] = 0;");
    }
}
