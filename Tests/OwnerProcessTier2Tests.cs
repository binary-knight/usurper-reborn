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
/// v1.1.13 Tier 2: NPC memories keep their real times, and the world edits log that the owner process
/// re-applies so a delete's clean-up survives stale and old-binary writes of the shared records.
/// </summary>
[Collection("SharedGameSingletons")]
public class OwnerProcessTier2Tests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-owner2-{Guid.NewGuid():N}.db");
    private readonly SqlSaveBackend _db;
    private readonly List<NPC> _rosterBefore;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IncludeFields = true,
    };

    public OwnerProcessTier2Tests()
    {
        _db = new SqlSaveBackend(_path);
        _rosterBefore = NPCSpawnSystem.Instance.ActiveNPCs.ToList();
        NPCSpawnSystem.Instance.ActiveNPCs.Clear();
        OnlineStateManager.NoteRosterRestored(null);   // v1.1.13: no stored roster held yet
    }

    public void Dispose()
    {
        var roster = NPCSpawnSystem.Instance.ActiveNPCs;
        roster.Clear();
        roster.AddRange(_rosterBefore);
        WorldEditLog.NoteLockOwnerId(null);
        WorldEditLog.OwnerOverride = null;
        foreach (var id in _marriedIds) NPCMarriageRegistry.Instance.EndMarriage(id);
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

    private static MemoryEvent Remember(NPC npc, MemoryType type, string who, DateTime at, float impact = 0f, float importance = 0.9f)
    {
        var m = new MemoryEvent { Type = type, InvolvedCharacter = who, Description = type.ToString(), EmotionalImpact = impact, Importance = importance };
        npc.Brain!.Memory.RecordEvent(m);
        m.Timestamp = at;
        return m;
    }

    private static NPC? Find(string name) => NPCSpawnSystem.Instance.ActiveNPCs.FirstOrDefault(n => n.Name2 == name);

    private static List<NPCData> RoundTrip(List<NPCData> data) =>
        JsonSerializer.Deserialize<List<NPCData>>(JsonSerializer.Serialize(data, Json), Json)!;

    private static List<MemoryEvent> MemoriesOf(string name) => Find(name)!.Brain!.Memory.AllMemories;

    // ─── Step 0: memory times ───

    [Fact]
    public async Task AMemory_SurvivesSaveAndLoad_WithItsRealTime()
    {
        var npc = Npc("npc_mt_1", "Keeper");
        var at = DateTime.Now.AddDays(-3);
        Remember(npc, MemoryType.Attacked, "Bob", at);

        var data = RoundTrip(OnlineStateManager.SerializeCurrentNPCs());
        data.Single().MemoryTimesKept.Should().BeTrue("a save of this version marks its times as real");
        await GameEngine.Instance.RestoreNPCs(data);

        MemoriesOf("Keeper").Single().Timestamp.Should().BeCloseTo(at, TimeSpan.FromMilliseconds(5), "the load keeps the recorded time");

        // the world sim's own loader keeps it too
        var sim = new WorldSimService(_db);
        typeof(WorldSimService).GetMethod("RestoreNPCsFromData", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(sim, new object[] { RoundTrip(OnlineStateManager.SerializeCurrentNPCs()) });
        MemoriesOf("Keeper").Single().Timestamp.Should().BeCloseTo(at, TimeSpan.FromMilliseconds(5));
    }

    [Fact]
    public async Task TheFirstLoadUnderThisVersion_StampsOldMemories_AndNothingIsForgotten()
    {
        var npc = Npc("npc_mt_2", "Elder");
        Remember(npc, MemoryType.Attacked, "Bob", DateTime.Now.AddDays(-30), importance: 0.15f);
        var recent = DateTime.Now.AddDays(-2);
        Remember(npc, MemoryType.Helped, "Ann", recent, 0.4f);
        Remember(npc, MemoryType.Insulted, "Cid", default);
        var data = RoundTrip(OnlineStateManager.SerializeCurrentNPCs());
        data.Single().MemoryTimesKept = false;   // written by an older release

        var before = DateTime.Now;
        await GameEngine.Instance.RestoreNPCs(data);

        var memories = MemoriesOf("Elder");
        memories.Single(m => m.InvolvedCharacter == "Bob").Timestamp.Should().BeOnOrAfter(before, "older than 7 days on the first load: stamped now");
        memories.Single(m => m.InvolvedCharacter == "Cid").Timestamp.Should().BeOnOrAfter(before, "a missing time is stamped now");
        memories.Single(m => m.InvolvedCharacter == "Ann").Timestamp.Should().BeCloseTo(recent, TimeSpan.FromMilliseconds(5), "a recent time is kept");

        Find("Elder")!.Brain!.Memory.DecayMemories();
        MemoriesOf("Elder").Should().HaveCount(3, "nothing is forgotten on the day of the update");

        // the next save carries the mark, so the grace runs once
        OnlineStateManager.SerializeCurrentNPCs().Single().MemoryTimesKept.Should().BeTrue();
    }

    [Fact]
    public async Task AfterTheFirstLoad_AMemoryOlderThanSevenDays_Decays()
    {
        var npc = Npc("npc_mt_3", "Fader");
        Remember(npc, MemoryType.Attacked, "Bob", DateTime.Now.AddDays(-8), importance: 0.15f);
        Remember(npc, MemoryType.Helped, "Ann", DateTime.Now.AddDays(-1), 0.4f, importance: 0.15f);
        var data = RoundTrip(OnlineStateManager.SerializeCurrentNPCs());

        await GameEngine.Instance.RestoreNPCs(data);
        Find("Fader")!.Brain!.Memory.DecayMemories();

        MemoriesOf("Fader").Select(m => m.InvolvedCharacter).Should().Equal(new[] { "Ann" }, "the 8-day-old memory decayed, the recent one stays");
    }

    [Fact]
    public async Task ARetriedPurge_CutsOffAtTheDeleteTime_NotTheRetryTime()
    {
        var deletedAt = DateTime.Now.AddMinutes(-10);
        var npc = Npc("npc_mt_4", "Grudger");
        Remember(npc, MemoryType.Attacked, "Bob", deletedAt.AddMinutes(-5));
        await _db.SaveWorldState(OnlineStateManager.KEY_NPCS, JsonSerializer.Serialize(OnlineStateManager.SerializeCurrentNPCs(), Json));
        OnlineStateManager.NoteRosterRestored(_db.GetWorldStateVersion(OnlineStateManager.KEY_NPCS));   // held at that version
        // another writer's roster: the old grudge, and one the new Bob earned after the delete
        var theirsNpc = Find("Grudger")!;
        var newGrudge = Remember(theirsNpc, MemoryType.Insulted, "Bob", deletedAt.AddMinutes(5));
        var theirs = OnlineStateManager.SerializeCurrentNPCs();
        theirsNpc.Brain!.Memory.AllMemories.Remove(newGrudge);

        int hooks = 0;
        await PermadeathHelper.ForgetCharacterInNpcWorldAsync(_db, new[] { "Bob" }, deletedAt, true,
            beforeWrite: async () => { if (hooks++ == 0) await _db.SaveWorldState(OnlineStateManager.KEY_NPCS, JsonSerializer.Serialize(theirs, Json)); });

        MemoriesOf("Grudger").Select(m => m.Type).Should().Equal(new[] { MemoryType.Insulted },
            "the retry after the reload clears only what was recorded by the delete");
    }

    // ─── The web delete's claim race ───

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

    private UsurperRemake.Server.MudServer AdminOnlyServer()
    {
        var t = typeof(UsurperRemake.Server.MudServer);
        var server = (UsurperRemake.Server.MudServer)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t);
        const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        t.GetField("_sqlBackend", F)!.SetValue(server, _db);
        t.GetField("<ActiveSessions>k__BackingField", F)!.SetValue(server,
            new System.Collections.Concurrent.ConcurrentDictionary<string, UsurperRemake.Server.PlayerSession>());
        return server;
    }

    private static Task Invoke(UsurperRemake.Server.MudServer server, string method, object cmd) =>
        (Task)typeof(UsurperRemake.Server.MudServer).GetMethod(method, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(server, new[] { cmd })!;

    private const string WithdrawStart = "UPDATE admin_commands SET status = 'expired', result = 'No answer; deleted by the web server'";

    /// <summary>The web server's withdrawal, the statement as ssh-proxy.js has it; returns the rows it changed.</summary>
    private int WebWithdraw(long id)
    {
        var js = WebSource();
        int at = js.IndexOf(WithdrawStart, StringComparison.Ordinal);
        at.Should().BeGreaterThan(0, "the withdrawal statement is in the web server");
        string sql = js.Substring(at, js.IndexOf('"', at) - at);
        using var conn = new SqliteConnection($"Data Source={_path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql.Replace("?", "@id");
        cmd.Parameters.AddWithValue("@id", id);
        return cmd.ExecuteNonQuery();
    }

    private async Task<(NPC npc, AdminCommand cmd)> QueuedWebDelete()
    {
        var npc = Npc("npc_claim_1", "Grudger");
        Remember(npc, MemoryType.Attacked, "Bob", DateTime.Now.AddMinutes(-5));
        await _db.SaveWorldState(OnlineStateManager.KEY_NPCS, JsonSerializer.Serialize(OnlineStateManager.SerializeCurrentNPCs(), Json));
        Exec("INSERT INTO players (username, display_name, player_data) VALUES ('bob_account', 'Bob', '{\"player\":{\"name2\":\"Bob\"}}');");
        Exec("INSERT INTO admin_commands (command, target_username, args, created_by) VALUES ('delete_player', 'bob_account', NULL, 'admin-web');");
        return (npc, _db.GetPendingAdminCommands().Single());
    }

    [Fact]
    public async Task TheGameServersClaim_LandsFirst_TheWithdrawalFails_AndOnePurgeRuns()
    {
        var (npc, cmd) = await QueuedWebDelete();
        var server = AdminOnlyServer();

        _db.TryClaimAdminCommand(cmd.Id).Should().BeTrue("the game server claims the pending command");
        WebWithdraw(cmd.Id).Should().Be(0, "the web server cannot withdraw a claimed command");
        Scalar($"SELECT status FROM admin_commands WHERE id = {cmd.Id};").Should().Be("executing", "the web server keeps waiting on this");
        await Invoke(server, "RunClaimedAdminCommand", cmd);
        Scalar($"SELECT status FROM admin_commands WHERE id = {cmd.Id};").Should().Be("executed");
        npc.Brain!.Memory.AllMemories.Should().NotContain(m => m.InvolvedCharacter == "Bob", "the purge ran");

        // an overlapping poll that read the row while pending does not run it again: a second purge would
        // clear this grudge too, since it is recorded before that purge's cut-off
        Remember(npc, MemoryType.Insulted, "Bob", DateTime.Now.AddSeconds(-1));
        await Invoke(server, "ExecuteAdminCommand", cmd);
        Scalar($"SELECT status FROM admin_commands WHERE id = {cmd.Id};").Should().Be("executed");
        npc.Brain!.Memory.AllMemories.Should().Contain(m => m.InvolvedCharacter == "Bob", "exactly one purge ran");
        Scalar("SELECT COUNT(*) FROM deleted_characters WHERE username = 'bob_account';").Should().Be("1");
    }

    [Fact]
    public async Task AWithdrawnCommand_IsNotRunByTheGameServer()
    {
        var (npc, cmd) = await QueuedWebDelete();

        WebWithdraw(cmd.Id).Should().Be(1);
        await Invoke(AdminOnlyServer(), "ExecuteAdminCommand", cmd);

        Scalar($"SELECT status FROM admin_commands WHERE id = {cmd.Id};").Should().Be("expired");
        Scalar("SELECT player_data FROM players WHERE username = 'bob_account';").Should().NotBe("{}", "the game server did not delete it");
        npc.Brain!.Memory.AllMemories.Should().Contain(m => m.InvolvedCharacter == "Bob", "nor purge it; the web server's direct delete does");
    }

    [Fact]
    public void TheWebDelete_WaitsForAClaimedCommand_AndNeverDeletesItToo()
    {
        var js = WebSource();
        int start = js.IndexOf("// DELETE /api/admin/players/:username", StringComparison.Ordinal);
        var route = js.Substring(start, js.IndexOf("// Fallback: queue the world purge", start, StringComparison.Ordinal) - start);
        route.Should().Contain("r.status === 'pending' || r.status === 'executing'", "the first wait keeps waiting on a claimed command");
        route.Should().Contain(WithdrawStart).And.Contain("WHERE id = ? AND status = 'pending'", "only a still-pending command is withdrawn");
        route.Should().Contain("while (row && row.status === 'executing'");
        route.Should().Contain("if (row && row.status === 'executing') { sendJson(res, 504", "a command still running is never followed by the direct delete");
        Source("Server", "MudServer.cs").Should().Contain("if (!_sqlBackend.TryClaimAdminCommand(cmd.Id)) return;");
    }

    // ─── The world_edits table ───

    [Fact]
    public void TheWorldEditsTable_IsMade_OnANewDatabase_AndOnAnOlderOne()
    {
        Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'world_edits';").Should().Be("1");
        Exec("DROP TABLE world_edits;");
        SqliteConnection.ClearAllPools();
        _ = new SqlSaveBackend(_path);   // an older release's database, opened by this one
        Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'world_edits';").Should().Be("1");
        Scalar("SELECT group_concat(name) FROM pragma_table_info('world_edits');")
            .Should().Be("id,kind,payload,created_at,created_by,applied_at,applied_by");
    }

    [Fact]
    public void TheOwnersSet_IsEveryUnappliedEdit_AndEveryAppliedEditNotYetPruned()
    {
        long oldUnapplied = _db.AppendWorldEdit("forget_character", "{}", "t");
        long oldApplied = _db.AppendWorldEdit("forget_character", "{}", "t");
        long recentApplied = _db.AppendWorldEdit("forget_character", "{}", "t");
        long recent = _db.AppendWorldEdit("vacate_throne", "{}", "t");
        long pruneDue = _db.AppendWorldEdit("forget_character", "{}", "t");
        _db.MarkWorldEditsApplied(new[] { oldApplied, recentApplied, pruneDue }, "owner").Should().Be(3);
        Exec($"UPDATE world_edits SET created_at = datetime('now', '-3 days') WHERE id IN ({oldUnapplied}, {oldApplied});");
        Exec($"UPDATE world_edits SET applied_at = datetime('now', '-2 days') WHERE id = {oldApplied};");
        Exec($"UPDATE world_edits SET created_at = datetime('now', '-9 days'), applied_at = datetime('now', '-8 days') WHERE id = {pruneDue};");

        // v1.1.14: re-applied until pruned (7 days after it was applied), not only for 24 hours
        _db.GetWorldEditsToApply().Select(e => e.Id).Should().Equal(oldUnapplied, oldApplied, recentApplied, recent);
        _db.GetUnappliedWorldEditsOlderThan(24).Select(e => e.Id).Should().Equal(oldUnapplied);
        _db.PruneAppliedWorldEdits(WorldEditLog.PruneAppliedDays).Should().Be(1, "the one edit left out of the set is the one the prune deletes");
        _db.GetWorldEditsToApply().Select(e => e.Id).Should().Equal(oldUnapplied, oldApplied, recentApplied, recent);

        string first = Scalar($"SELECT applied_at FROM world_edits WHERE id = {oldApplied};")!;
        _db.MarkWorldEditsApplied(new[] { oldApplied }, "other").Should().Be(0, "an applied edit keeps its first mark");
        Scalar($"SELECT applied_by FROM world_edits WHERE id = {oldApplied};").Should().Be("owner");
        Scalar($"SELECT applied_at FROM world_edits WHERE id = {oldApplied};").Should().Be(first);
    }

    [Fact]
    public void AppliedEdits_ArePrunedAfterSevenDays_UnappliedOnesNever()
    {
        long applied = _db.AppendWorldEdit("forget_character", "{}", "t");
        long unapplied = _db.AppendWorldEdit("forget_character", "{}", "t");
        long fresh = _db.AppendWorldEdit("forget_character", "{}", "t");
        _db.MarkWorldEditsApplied(new[] { applied, fresh }, "owner");
        Exec($"UPDATE world_edits SET created_at = datetime('now', '-9 days') WHERE id IN ({applied}, {unapplied});");
        Exec($"UPDATE world_edits SET applied_at = datetime('now', '-8 days') WHERE id = {applied};");

        _db.PruneAppliedWorldEdits(7).Should().Be(1);

        Scalar("SELECT group_concat(id) FROM world_edits;").Should().Be($"{unapplied},{fresh}");
    }

    [Fact]
    public void ALaterCharacter_IsARowWithASave_MadeOrSavedAfterTheEdit_OrOnTheSameAccount()
    {
        long id = _db.AppendWorldEdit("forget_character", "{}", "t");
        string at = Scalar($"SELECT created_at FROM world_edits WHERE id = {id};")!;
        var bob = new[] { "Bob" };
        const string save = "'{\"player\":{\"name2\":\"Bob\"}}'";

        Exec($"INSERT INTO players (username, display_name, player_data, created_at, last_login) VALUES ('old_acct', 'Bob', '{{}}', datetime('now', '-9 days'), datetime('now', '-9 days'));");
        _db.LaterCharacterUsesName(bob, at, "bob_account").Should().BeFalse("an emptied row is no character");

        Exec($"UPDATE players SET player_data = {save} WHERE username = 'old_acct';");
        _db.LaterCharacterUsesName(bob, at, "bob_account").Should().BeFalse("made and last saved before the edit");

        Exec("UPDATE players SET last_login = datetime('now', '+1 minute') WHERE username = 'old_acct';");
        _db.LaterCharacterUsesName(bob, at, "bob_account").Should().BeTrue("saved after the edit: a new character on an old account");

        Exec("DELETE FROM players;");
        Exec($"INSERT INTO players (username, display_name, player_data, created_at) VALUES ('new_acct', 'Robert', {save}, datetime('now', '+1 minute'));");
        _db.LaterCharacterUsesName(bob, at, "bob_account").Should().BeTrue("a row created after the edit, matched by its Name2");

        Exec("DELETE FROM players;");
        Exec($"INSERT INTO players (username, display_name, player_data, created_at, last_login) VALUES ('bob_account', 'Bob', {save}, datetime('now', '-9 days'), datetime('now', '-9 days'));");
        _db.LaterCharacterUsesName(bob, at, "bob_account").Should().BeTrue("the deleted character's own account with a save again");
    }

    // ─── The owner loop ───

    private readonly List<string> _marriedIds = new();

    private NPC MarriedToBob(string id, string name)
    {
        var wife = Npc(id, name);
        wife.SpouseName = "Bob"; wife.Married = true; wife.IsMarried = true;
        NPCMarriageRegistry.Instance.RegisterMarriage("player_bob_id", wife.ID, "Bob", name);
        _marriedIds.Add(wife.ID);
        return wife;
    }

    private string RosterJson() => JsonSerializer.Serialize(OnlineStateManager.SerializeCurrentNPCs(), Json);

    private async Task<List<NPCData>> StoredRoster() =>
        JsonSerializer.Deserialize<List<NPCData>>((await _db.LoadWorldState(OnlineStateManager.KEY_NPCS))!, Json)!;

    private static readonly System.Reflection.BindingFlags Priv = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

    /// <summary>The owner's world sim, holding the lock, with its baselines at the given versions.</summary>
    private WorldSimService OwnerSim(string ownerId, long npcVersion)
    {
        _db.TryAcquireWorldSimLock(ownerId).Should().BeTrue();
        WorldEditLog.NoteLockOwnerId(ownerId);
        var sim = new WorldSimService(_db, heartbeatOwnerId: ownerId);
        typeof(WorldSimService).GetField("lastNpcVersion", Priv)!.SetValue(sim, npcVersion);
        OnlineStateManager.NoteRoyalCourtVersion(_db.GetWorldStateVersion("royal_court"));   // v1.1.13: the sim tracks the process-wide court version
        return sim;
    }

    private static Task SimSave(WorldSimService sim) => (Task)typeof(WorldSimService).GetMethod("SaveWorldState", Priv)!.Invoke(sim, null)!;

    private (string? AppliedAt, string? AppliedBy) EditMark(string kind) =>
        (Scalar($"SELECT applied_at FROM world_edits WHERE kind = '{kind}';"), Scalar($"SELECT applied_by FROM world_edits WHERE kind = '{kind}';"));

    [Fact]
    public async Task AnOldBinarysWrite_LandsAnyway_AndTheOwnersSave_ReappliesTheEdit_ThenMarksIt()
    {
        var npc = Npc("npc_o_1", "Grudger");
        Remember(npc, MemoryType.Attacked, "Bob", DateTime.Now.AddMinutes(-5));
        npc.Enemies.Add("Bob");
        MarriedToBob("npc_o_w", "Wife");
        string stale = RosterJson();
        await _db.SaveWorldState(OnlineStateManager.KEY_NPCS, stale);

        // a door process (not the owner) deletes Bob: the edit, the local clean-up, the versioned write
        await PermadeathHelper.PurgeDeletedCharacterAsync(_db, "bob_account", "Bob");
        Scalar("SELECT COUNT(*) FROM world_edits WHERE kind = 'forget_character';").Should().Be("1");
        EditMark("forget_character").AppliedAt.Should().BeNull("only the owner marks an edit, after its own write");
        (await StoredRoster()).Single(d => d.Name == "Grudger").Memories.Should().NotContain(m => m.InvolvedCharacter == "Bob");
        long clean = _db.GetWorldStateVersion(OnlineStateManager.KEY_NPCS);

        // an old binary's unconditional write of its stale roster lands anyway
        await _db.SaveWorldState(OnlineStateManager.KEY_NPCS, stale);
        (await StoredRoster()).Single(d => d.Name == "Wife").SpouseName.Should().Be("Bob");

        // the owner's next save reloads on the version change, re-applies, writes, then marks
        var sim = OwnerSim("owner_t", clean);
        await SimSave(sim);

        var stored = await StoredRoster();
        stored.Single(d => d.Name == "Grudger").Memories.Should().NotContain(m => m.InvolvedCharacter == "Bob", "the grudge is gone again");
        stored.Single(d => d.Name == "Grudger").Enemies.Should().NotContain("Bob");
        stored.Single(d => d.Name == "Wife").SpouseName.Should().BeEmpty("and so is the marriage");
        NPCMarriageRegistry.Instance.GetSpouseId("npc_o_w").Should().BeNull();
        var marriages = await _db.LoadWorldState(OnlineStateManager.KEY_MARRIAGES);
        marriages.Should().NotContain("npc_o_w", "the owner's registry, written after the re-apply, has no such marriage");
        var mark = EditMark("forget_character");
        mark.AppliedAt.Should().NotBeNull();
        mark.AppliedBy.Should().Be("owner_t");
    }

    [Fact]
    public async Task AProcessNotHoldingTheLock_DoesNotReapply_NorMark()
    {
        var npc = Npc("npc_o_2", "Grudger");
        Remember(npc, MemoryType.Attacked, "Bob", DateTime.Now.AddMinutes(-5));
        string stale = RosterJson();
        await _db.SaveWorldState(OnlineStateManager.KEY_NPCS, stale);
        await PermadeathHelper.PurgeDeletedCharacterAsync(_db, "bob_account", "Bob");
        long clean = _db.GetWorldStateVersion(OnlineStateManager.KEY_NPCS);
        await _db.SaveWorldState(OnlineStateManager.KEY_NPCS, stale);

        var sim = OwnerSim("owner_t", clean);
        _db.TakeOverWorldSimLock("someone_else");   // the lock has moved on (v1.1.14: a heartbeat no longer takes a held lock)
        sim.ReapplyWorldEdits().Should().BeEmpty();
        await SimSave(sim);

        EditMark("forget_character").AppliedAt.Should().BeNull();
        (await StoredRoster()).Single(d => d.Name == "Grudger").Memories.Should().Contain(m => m.InvolvedCharacter == "Bob",
            "a world sim that lost the lock is not the owner");

        // applying an edit locally never marks it either
        WorldEditLog.Apply(_db, _db.GetWorldEditsToApply());
        EditMark("forget_character").AppliedAt.Should().BeNull();
    }

    [Fact]
    public async Task ANewSameNameCharacter_KeepsItsGrudgesAndSpouse_ThroughAReapply()
    {
        var npc = Npc("npc_o_3", "Grudger");
        Remember(npc, MemoryType.Attacked, "Bob", DateTime.Now.AddMinutes(-5));
        npc.Enemies.Add("Bob");
        await PermadeathHelper.PurgeDeletedCharacterAsync(_db, "bob_account", "Bob");
        npc.Enemies.Should().NotContain("Bob");
        WorldEditLog.OwnerOverride = true;

        // control: a stale copy of the old entries is cleared by the re-apply
        npc.Enemies.Add("Bob");
        WorldEditLog.Apply(_db, _db.GetWorldEditsToApply()).Should().BeGreaterThan(0);
        npc.Enemies.Should().NotContain("Bob");
        WorldEditLog.Apply(_db, _db.GetWorldEditsToApply()).Should().Be(0, "a second pass changes nothing");

        // a new Bob, on a new account made after the delete, earns a grudge, an enemy and a wife
        Exec("INSERT INTO players (username, display_name, player_data, created_at) VALUES ('bob_two', 'Bob', " +
             "'{\"player\":{\"name2\":\"Bob\"}}', datetime('now', '+1 minute'));");
        await Task.Delay(20);
        Remember(npc, MemoryType.Insulted, "Bob", DateTime.Now);
        npc.Enemies.Add("Bob");
        var wife = MarriedToBob("npc_o_w3", "Wife");

        WorldEditLog.Apply(_db, _db.GetWorldEditsToApply()).Should().Be(0);

        npc.Brain!.Memory.AllMemories.Should().Contain(m => m.InvolvedCharacter == "Bob" && m.Type == MemoryType.Insulted,
            "recorded after the delete, so past the cut-off");
        npc.Enemies.Should().Contain("Bob", "a later character uses the name");
        wife.SpouseName.Should().Be("Bob");
        NPCMarriageRegistry.Instance.GetSpouseId(wife.ID).Should().Be("player_bob_id");
    }

    [Fact]
    public void AnEditNoOwnerApplied_IsReported_AndNotPruned()
    {
        long id = WorldEditLog.AppendForgetCharacter(_db, new[] { "Bob" }, "bob_account", DateTime.Now, true);
        Exec($"UPDATE world_edits SET created_at = datetime('now', '-10 days') WHERE id = {id};");
        WorldEditLog.ReportUnapplied(_db).Should().Be(1);
        _db.PruneAppliedWorldEdits(WorldEditLog.PruneAppliedDays).Should().Be(0);
        _db.GetWorldEditsToApply().Select(e => e.Id).Should().Contain(id, "the owner still owes it");
    }

    [Fact]
    public void TheOwner_TakesTheLock_AndReappliesAtLoadAndBeforeEverySave()
    {
        var mud = Source("Server", "MudServer.cs");
        mud.Should().Contain("sqlBackend.TryAcquireWorldSimLock(worldSimOwnerId)").And.Contain("heartbeatOwnerId: worldSimOwnerId");
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Console"))) dir = dir.Parent;
        var program = File.ReadAllText(Path.Combine(dir!.FullName, "Console", "Bootstrap", "Program.cs"));
        int ws = program.IndexOf("private static async Task RunWorldSimMode()", StringComparison.Ordinal);
        program.Substring(ws, 2500).Should().Contain("sqlBackend.TryAcquireWorldSimLock(ownerId)").And.Contain("heartbeatOwnerId: ownerId");

        var sim = Source("Systems", "WorldSimService.cs");
        int run = sim.IndexOf("public async Task RunAsync", StringComparison.Ordinal);
        // v1.1.14: the loads and the re-apply moved to LoadSharedRecordsAsync, which RunAsync calls before its loop
        sim.IndexOf("await LoadSharedRecordsAsync();", run, StringComparison.Ordinal)
            .Should().BeLessThan(sim.IndexOf("while (!cancellationToken.IsCancellationRequested)", run, StringComparison.Ordinal));
        int records = sim.IndexOf("private async Task LoadSharedRecordsAsync()", StringComparison.Ordinal);
        int loaded = sim.IndexOf("LoadUsedNamesState();", records, StringComparison.Ordinal);
        sim.IndexOf("ReapplyWorldEdits();", records, StringComparison.Ordinal).Should().BeGreaterThan(loaded, "after every record is loaded");
        int save = sim.IndexOf("private async Task SaveWorldState()", StringComparison.Ordinal);
        int reload = sim.IndexOf("await LoadWorldState();", save, StringComparison.Ordinal);
        int courtReload = sim.IndexOf("Royal court modified by player", save, StringComparison.Ordinal);
        int reapply = sim.IndexOf("var editsInPass = ReapplyWorldEdits();", save, StringComparison.Ordinal);
        reapply.Should().BeGreaterThan(reload).And.BeGreaterThan(courtReload, "after every version-triggered reload")
            .And.BeLessThan(sim.IndexOf("OnlineStateManager.KEY_NPCS, json, lastNpcVersion", save, StringComparison.Ordinal), "before the write");
        int mark = sim.IndexOf("MarkEditsApplied(editsInPass, WorldEditLog.ForgetCharacter);", save, StringComparison.Ordinal);
        mark.Should().BeGreaterThan(sim.IndexOf("lastNpcVersion = lastNpcVersion + 1;", save, StringComparison.Ordinal), "marked only after the versioned write");
    }

    // ─── Door writers: two backends on one file ───

    private static OnlineStateManager NewOsm(SqlSaveBackend db) =>
        (OnlineStateManager)Activator.CreateInstance(typeof(OnlineStateManager),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, new object[] { db, "door_a" }, null)!;

    /// <summary>Door process A: loads the stored roster as a login does and holds it at that version.</summary>
    private static async Task<OnlineStateManager> DoorLogin(SqlSaveBackend db)
    {
        var osm = NewOsm(db);
        await GameEngine.Instance.RestoreNPCs((await osm.LoadSharedNPCs())!, osm.NpcsVersion);   // v1.1.13: as a login does
        osm.NoteNpcBaseline();
        return osm;
    }

    private const string StaleMarriages =
        "{\"marriages\":[{\"npc1Id\":\"player_bob_id\",\"npc2Id\":\"npc_d_w\"},{\"npc1Id\":\"npc_x\",\"npc2Id\":\"npc_y\"}],\"affairs\":[]}";

    [Fact]
    public async Task ADoorsStaleSave_FailsTheVersionCheck_AndRetries_KeepingItsOwnChanges_AndThePurge()
    {
        var dbA = new SqlSaveBackend(_path);   // door A; _db is process B, the deleting one
        var grudger = Npc("npc_d_g", "Grudger");
        Remember(grudger, MemoryType.Attacked, "Bob", DateTime.Now.AddMinutes(-5));
        grudger.Enemies.Add("Bob");
        MarriedToBob("npc_d_w", "Wife");
        Npc("npc_d_s", "Smith");
        Npc("npc_d_b", "Baker");
        await dbA.SaveWorldState(OnlineStateManager.KEY_NPCS, RosterJson());
        await dbA.SaveWorldState(OnlineStateManager.KEY_MARRIAGES, StaleMarriages);
        string staleNpcs = (await dbA.LoadWorldState(OnlineStateManager.KEY_NPCS))!;
        WorldEditLog.OwnerOverride = false;

        var osmA = await DoorLogin(dbA);
        long v1 = osmA.NpcsVersion!.Value;
        Find("Smith")!.Level = 42;   // A's own unsaved change

        // B deletes Bob: the edit, then its versioned writes of the clean roster (with a change of its own) and marriages
        var b = await StoredRoster();
        b.Single(d => d.Name == "Grudger").Memories.RemoveAll(m => m.InvolvedCharacter == "Bob");
        b.Single(d => d.Name == "Grudger").Enemies.Remove("Bob");
        var bw = b.Single(d => d.Name == "Wife"); bw.SpouseName = ""; bw.IsMarried = false; bw.Married = false;
        b.Single(d => d.Name == "Baker").Gold = 777;
        WorldEditLog.AppendForgetCharacter(_db, new[] { "Bob" }, "bob_account", DateTime.Now, true);
        (await _db.SaveWorldStateIfVersion(OnlineStateManager.KEY_NPCS, JsonSerializer.Serialize(b, Json), v1)).Should().BeTrue();
        (await OnlineStateManager.RemoveStoredMarriagesAsync(_db, new[] { "npc_d_w" })).Should().Be(1);

        // A, still holding the grudge and the marriage, saves through the ordinary path
        int writes = 0;
        (await osmA.SaveSharedNPCsVersionedAsync(dbA, OnlineStateManager.SerializeCurrentNPCs(), beforeWrite: () => { writes++; return Task.CompletedTask; }))
            .Should().BeTrue();
        writes.Should().Be(2, "the stale write failed its version check; the retry landed");
        dbA.GetWorldStateVersion(OnlineStateManager.KEY_NPCS).Should().Be(v1 + 2);
        osmA.NpcsVersion.Should().Be(v1 + 2);

        var stored = await StoredRoster();
        stored.Single(d => d.Name == "Grudger").Memories.Should().NotContain(m => m.InvolvedCharacter == "Bob");
        stored.Single(d => d.Name == "Grudger").Enemies.Should().NotContain("Bob");
        stored.Single(d => d.Name == "Wife").SpouseName.Should().BeEmpty();
        stored.Single(d => d.Name == "Smith").Level.Should().Be(42, "A's own change was laid over the reloaded roster");
        stored.Single(d => d.Name == "Baker").Gold.Should().Be(777, "B's change to another NPC was kept");
        Find("Grudger")!.Brain!.Memory.AllMemories.Should().NotContain(m => m.InvolvedCharacter == "Bob");
        EditMark("forget_character").AppliedAt.Should().BeNull("a door never marks an edit");
        (await _db.LoadWorldState(OnlineStateManager.KEY_MARRIAGES)).Should().NotContain("npc_d_w", "doors do not write the marriages record");

        // an old binary's unconditional writes land anyway; the owner's save re-applies and they are gone again
        long beforeOld = _db.GetWorldStateVersion(OnlineStateManager.KEY_NPCS);
        await _db.SaveWorldState(OnlineStateManager.KEY_NPCS, staleNpcs);
        await _db.SaveWorldState(OnlineStateManager.KEY_MARRIAGES, StaleMarriages);
        WorldEditLog.OwnerOverride = null;
        await GameEngine.Instance.RestoreNPCs(JsonSerializer.Deserialize<List<NPCData>>(staleNpcs, Json)!);   // what the owner held
        await SimSave(OwnerSim("owner_t", beforeOld));

        stored = await StoredRoster();
        stored.Single(d => d.Name == "Grudger").Memories.Should().NotContain(m => m.InvolvedCharacter == "Bob");
        stored.Single(d => d.Name == "Wife").SpouseName.Should().BeEmpty();
        var marriages = (await _db.LoadWorldState(OnlineStateManager.KEY_MARRIAGES))!;
        marriages.Should().NotContain("npc_d_w", "the owner's registry lost the marriage in the re-apply and replaced the record");
        EditMark("forget_character").AppliedBy.Should().Be("owner_t");
    }

    [Fact]
    public async Task ANonOwner_NeverWritesTheRosterUnconditionally()
    {
        Npc("npc_d_2", "Smith");
        await _db.SaveWorldState(OnlineStateManager.KEY_NPCS, RosterJson());
        WorldEditLog.OwnerOverride = false;
        var osm = await DoorLogin(_db);
        await _db.SaveWorldState(OnlineStateManager.KEY_NPCS, "[]");   // another writer empties it (a stored roster of none)
        var other = new List<NPCData> { OnlineStateManager.SerializeCurrentNPCs().Single() };
        other[0].Name = "Newcomer"; other[0].Id = "npc_d_new";
        await _db.SaveWorldState(OnlineStateManager.KEY_NPCS, JsonSerializer.Serialize(other, Json));

        await osm.SaveSharedNPCs(OnlineStateManager.SerializeCurrentNPCs());

        (await StoredRoster()).Select(d => d.Name).Should().Equal(new[] { "Newcomer" },
            "the stored roster is reloaded; Smith, unchanged by this session, is not written over it");

        var src = Source("Systems", "OnlineStateManager.cs");
        int save = src.IndexOf("public async Task SaveSharedNPCs(", StringComparison.Ordinal);
        src.IndexOf("!WorldEditLog.IsOwnerProcess(sql)", save, StringComparison.Ordinal)
            .Should().BeLessThan(src.IndexOf("await backend.SaveWorldState(KEY_NPCS, json);", save, StringComparison.Ordinal));
        Source("Systems", "SaveSystem.cs").Should().Contain("await OnlineStateManager.Instance.SaveSharedNPCs(sharedNpcData, generation);");   // v1.1.14: with its generation
        Source("Core", "GameEngine.cs").Split("OnlineStateManager.Instance.NoteNpcBaseline();").Length.Should().Be(3, "both online loads record the baseline");
    }

    // ─── The throne edit ───

    private static async Task WithKing(King? king, Func<Task> body)
    {
        var before = CastleLocation.GetCurrentKing();
        var history = CastleLocation.GetMonarchHistory().ToList();
        var version = OnlineStateManager.RoyalCourtVersion;
        bool loaded = CastleLocation.RoyalCourtLoadedFromShared;
        CastleLocation.SetKing(king);
        OnlineStateManager.NoteRoyalCourtVersion(null);
        try { await body(); }
        finally
        {
            CastleLocation.SetKing(before);
            CastleLocation.SetMonarchHistory(history);
            OnlineStateManager.NoteRoyalCourtVersion(version);
            CastleLocation.RoyalCourtLoadedFromShared = loaded;
        }
    }

    private static string Court(string king) =>
        JsonSerializer.Serialize(new RoyalCourtSaveData { KingName = king, KingAI = (int)CharacterAI.Human, Treasury = 100 }, Json);

    private async Task<RoyalCourtSaveData> StoredCourt() =>
        JsonSerializer.Deserialize<RoyalCourtSaveData>((await _db.LoadWorldState("royal_court"))!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    [Fact]
    public async Task ADeletedKing_StaysDeposed_ThroughAStaleDoorSave_AndAnOldBinarysCourtWrite()
    {
        Npc("npc_k_1", "Commoner");
        await _db.SaveWorldState(OnlineStateManager.KEY_NPCS, RosterJson());
        await WithKing(King.CreateNewKing("Bob", CharacterAI.Human, CharacterSex.Male), async () =>
        {
            var dbA = new SqlSaveBackend(_path);
            await dbA.SaveWorldState("royal_court", Court("Bob"));
            var osmA = NewOsm(dbA);
            await osmA.LoadRoyalCourtFromWorldState();   // door A holds Bob's court at v1
            long v1 = dbA.GetWorldStateVersion("royal_court");
            OnlineStateManager.RoyalCourtVersion.Should().Be(v1);

            // B deletes Bob while he reigns: the edit, then its versioned write of the vacancy
            WorldEditLog.AppendVacateThrone(_db, "Bob", Array.Empty<string>(), "bob_account");
            (await _db.SaveWorldStateIfVersion("royal_court", OnlineStateManager.EmptyRoyalCourtJson(throneVacated: true), v1)).Should().BeTrue();

            // A's stale court save fails its version check and loads the stored vacancy instead
            await osmA.SaveRoyalCourtToWorldState();
            (await StoredCourt()).ThroneVacant.Should().BeTrue();
            (CastleLocation.GetCurrentKing()?.Name).Should().NotBe("Bob");
            EditMark("vacate_throne").AppliedAt.Should().BeNull();

            // an old binary writes Bob's court back unconditionally; the owner's save reloads, vacates, writes, marks
            var sim = OwnerSim("owner_t", _db.GetWorldStateVersion(OnlineStateManager.KEY_NPCS));
            await _db.SaveWorldState("royal_court", Court("Bob"));
            await SimSave(sim);

            var court = await StoredCourt();
            court.KingName.Should().NotBe("Bob", "the re-applied edit deposed him again and the owner wrote it");
            (CastleLocation.GetCurrentKing()?.Name).Should().NotBe("Bob");
            EditMark("vacate_throne").AppliedBy.Should().Be("owner_t");
            WorldEditLog.Apply(_db, _db.GetWorldEditsToApply()).Should().Be(0, "a second pass changes nothing");
        });
    }

    [Fact]
    public async Task ANewSameNameKing_IsNotDeposedByTheEdit()
    {
        await WithKing(King.CreateNewKing("Bob", CharacterAI.Human, CharacterSex.Male), async () =>
        {
            await PermadeathHelper.PurgeDeletedCharacterAsync(_db, "bob_account", "Bob");
            Scalar("SELECT COUNT(*) FROM world_edits WHERE kind = 'vacate_throne';").Should().Be("1", "the reigning character's delete logs it");
            (CastleLocation.GetCurrentKing()?.Name).Should().NotBe("Bob");

            // control: a stale copy of the old Bob back on the throne is deposed by the re-apply
            CastleLocation.SetKing(King.CreateNewKing("Bob", CharacterAI.Human, CharacterSex.Male));
            WorldEditLog.Apply(_db, _db.GetWorldEditsToApply()).Should().BeGreaterThan(0);
            (CastleLocation.GetCurrentKing()?.Name).Should().NotBe("Bob");

            // a new Bob, on an account made after the delete, takes the throne and keeps it
            Exec("INSERT INTO players (username, display_name, player_data, created_at) VALUES ('bob_two', 'Bob', " +
                 "'{\"player\":{\"name2\":\"Bob\"}}', datetime('now', '+1 minute'));");
            CastleLocation.SetKing(King.CreateNewKing("Bob", CharacterAI.Human, CharacterSex.Male));
            WorldEditLog.Apply(_db, _db.GetWorldEditsToApply()).Should().Be(0);
            CastleLocation.GetCurrentKing()!.Name.Should().Be("Bob");
        });

        await WithKing(King.CreateNewKing("Carol", CharacterAI.Human, CharacterSex.Female), async () =>
        {
            await PermadeathHelper.PurgeDeletedCharacterAsync(_db, "dave_account", "Dave");
            Scalar("SELECT COUNT(*) FROM world_edits WHERE kind = 'vacate_throne';").Should().Be("1", "no edit for a character who did not reign");
        });
    }

    private static string WebSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "web"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "web", "ssh-proxy.js"));
    }

    private static string Source(string folder, string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Scripts"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "Scripts", folder, file));
    }
}
