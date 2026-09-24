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
    }

    public void Dispose()
    {
        var roster = NPCSpawnSystem.Instance.ActiveNPCs;
        roster.Clear();
        roster.AddRange(_rosterBefore);
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
    public void TheOwnersSet_IsEveryUnappliedEdit_AndTheLastDaysApplied()
    {
        long oldUnapplied = _db.AppendWorldEdit("forget_character", "{}", "t");
        long oldApplied = _db.AppendWorldEdit("forget_character", "{}", "t");
        long recentApplied = _db.AppendWorldEdit("forget_character", "{}", "t");
        long recent = _db.AppendWorldEdit("vacate_throne", "{}", "t");
        _db.MarkWorldEditsApplied(new[] { oldApplied, recentApplied }, "owner").Should().Be(2);
        Exec($"UPDATE world_edits SET created_at = datetime('now', '-3 days') WHERE id IN ({oldUnapplied}, {oldApplied});");

        _db.GetWorldEditsToApply().Select(e => e.Id).Should().Equal(oldUnapplied, recentApplied, recent);
        _db.GetUnappliedWorldEditsOlderThan(24).Select(e => e.Id).Should().Equal(oldUnapplied);

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
