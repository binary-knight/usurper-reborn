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
/// v1.1.13: deleting a character clears NPC grudges and marriages naming it in the live roster and writes
/// them at once under the record's version, so a login's RestoreNPCs(LoadSharedNPCs()) or a conflicting
/// writer cannot bring them back.
/// </summary>
[Collection("SharedGameSingletons")]
public class OwnerProcessTier1Tests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-owner1-{Guid.NewGuid():N}.db");
    private readonly SqlSaveBackend _db;
    private readonly List<NPC> _rosterBefore;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IncludeFields = true,
    };

    public OwnerProcessTier1Tests()
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

    private void Exec(string sql)
    {
        using var conn = new SqliteConnection($"Data Source={_path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static NPC Npc(string id, string name)
    {
        var npc = new NPC { ID = id, Id = id, Name1 = name, Name2 = name, Level = 10, HP = 100, MaxHP = 100 };
        npc.EnsureSystemsInitialized();
        NPCSpawnSystem.Instance.ActiveNPCs.Add(npc);
        return npc;
    }

    private static void Remember(NPC npc, MemoryType type, string who, DateTime at, float impact = 0f)
    {
        var m = new MemoryEvent { Type = type, InvolvedCharacter = who, Description = type.ToString(), EmotionalImpact = impact, Importance = 0.9f };
        npc.Brain!.Memory.RecordEvent(m);
        m.Timestamp = at;   // RecordEvent stamps the time of recording
    }

    private static NPC? Find(string name) => NPCSpawnSystem.Instance.ActiveNPCs.FirstOrDefault(n => n.Name2 == name);

    private async Task SeedStoredRoster() =>
        await _db.SaveWorldState(OnlineStateManager.KEY_NPCS, JsonSerializer.Serialize(OnlineStateManager.SerializeCurrentNPCs(), Json));

    private async Task<List<NPCData>> StoredRoster() =>
        JsonSerializer.Deserialize<List<NPCData>>((await _db.LoadWorldState(OnlineStateManager.KEY_NPCS))!, Json)!;

    private static OnlineStateManager NewOsm(SqlSaveBackend db) =>
        (OnlineStateManager)Activator.CreateInstance(typeof(OnlineStateManager),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, new object[] { db, "owner1" }, null)!;

    [Fact]
    public void GrudgeTypes_AreTheNegativeOnes()
    {
        MemorySystem.IsGrudge(MemoryType.Attacked, 0f).Should().BeTrue();
        MemorySystem.IsGrudge(MemoryType.KilledMyParent, 0f).Should().BeTrue();
        MemorySystem.IsGrudge(MemoryType.HeardGossip, -0.4f).Should().BeTrue("a negative memory of any type");
        MemorySystem.IsGrudge(MemoryType.Helped, 0.4f).Should().BeFalse();
        MemorySystem.IsGrudge(MemoryType.Traded, 0f).Should().BeFalse();
    }

    [Fact]
    public void TheCutOff_KeepsTheGrudgesOfALaterSameNameCharacter()
    {
        var deletedAt = DateTime.Now;
        var npc = Npc("npc_cut_1", "Grudger");
        Remember(npc, MemoryType.Attacked, "Bob", deletedAt.AddHours(-1));
        Remember(npc, MemoryType.Insulted, "bob", deletedAt);
        Remember(npc, MemoryType.Helped, "Bob", deletedAt.AddHours(-2), 0.4f);
        Remember(npc, MemoryType.Betrayed, "Bob", deletedAt.AddHours(1));   // the new Bob, after the delete
        Remember(npc, MemoryType.Attacked, "Alice", deletedAt.AddHours(-1));

        PermadeathHelper.ForgetNpcGrudgesAgainst("Bob", deletedAt).Should().Be(2);

        var left = npc.Brain!.Memory.AllMemories;
        left.Where(m => m.InvolvedCharacter.Equals("Bob", StringComparison.OrdinalIgnoreCase)).Select(m => m.Type)
            .Should().BeEquivalentTo(new[] { MemoryType.Helped, MemoryType.Betrayed }, "at or before the cut-off goes, after it stays");
        left.Should().Contain(m => m.InvolvedCharacter == "Alice");
        npc.Brain.Memory.GetCharacterImpression("Bob").Should().BeLessThan(0f, "rebuilt from the later Bob's betrayal");
    }

    [Fact]
    public async Task EnemiesAndKnownCharacters_AreKept_WhenAnotherPlayerRowUsesTheName()
    {
        var npc = Npc("npc_cut_2", "Rival");
        npc.Enemies.Add("Bob");
        npc.KnownCharacters.Add("Bob");
        Remember(npc, MemoryType.Attacked, "Bob", DateTime.Now.AddMinutes(-5));

        // a later character of the same name, on another account
        Exec("INSERT INTO players (username, display_name, player_data) VALUES ('bob_new', 'Bob', '{}');");
        await PermadeathHelper.PurgeDeletedCharacterAsync(_db, "bob_old", "Bob");
        npc.Enemies.Should().Contain("Bob", "untimed entries may belong to the later Bob");
        npc.KnownCharacters.Should().Contain("Bob");
        npc.Brain!.Memory.AllMemories.Should().NotContain(m => m.InvolvedCharacter == "Bob", "the old grudge is timed, so it goes");

        // with no later character, the untimed entries go too
        var other = Npc("npc_cut_3", "Other");
        other.Enemies.Add("Carl");
        other.KnownCharacters.Add("carl");
        await PermadeathHelper.PurgeDeletedCharacterAsync(_db, "carl_account", "Carl");
        other.Enemies.Should().BeEmpty();
        other.KnownCharacters.Should().BeEmpty();
    }

    [Fact]
    public async Task ThePurge_ThenALogin_LeavesTheGrudgeAndTheSpouseGone()
    {
        var reg = NPCMarriageRegistry.Instance;
        var wife = Npc("npc_t1_wife", "Wife");
        wife.SpouseName = "Bob"; wife.Married = true; wife.IsMarried = true;
        Remember(wife, MemoryType.Attacked, "Bob", DateTime.Now.AddMinutes(-5));
        reg.RegisterMarriage("player_bob_id", wife.ID, "Bob", "Wife");
        await SeedStoredRoster();
        await _db.SaveWorldState(OnlineStateManager.KEY_MARRIAGES,
            "{\"marriages\":[{\"npc1Id\":\"player_bob_id\",\"npc2Id\":\"npc_t1_wife\"},{\"npc1Id\":\"npc_x\",\"npc2Id\":\"npc_y\"}],\"affairs\":[]}");
        try
        {
            await PermadeathHelper.PurgeDeletedCharacterAsync(_db, "bob_account", "Bob");

            // the simulated login: every MUD login does this
            var osm = NewOsm(_db);
            await GameEngine.Instance.RestoreNPCs((await osm.LoadSharedNPCs())!);

            var back = Find("Wife")!;
            back.Should().NotBeSameAs(wife, "the roster was rebuilt from world_state");
            back.SpouseName.Should().BeEmpty();
            back.IsMarried.Should().BeFalse();
            back.Married.Should().BeFalse();
            back.Brain!.Memory.AllMemories.Should().NotContain(m => m.InvolvedCharacter == "Bob");
            var (marriages, _) = await osm.LoadSharedMarriages();
            marriages!.Select(m => m.Npc2Id).Should().Equal(new[] { "npc_y" }, "only the deleted player's marriage left the record");
        }
        finally { reg.EndMarriage("npc_t1_wife"); }
    }

    [Fact]
    public async Task AConflictingWrite_IsReloaded_AndTheCleanUpIsAppliedAgain()
    {
        var wife = Npc("npc_t1_c", "Grudger");
        Remember(wife, MemoryType.Attacked, "Bob", DateTime.Now.AddMinutes(-5));
        await SeedStoredRoster();
        // another writer's roster, still with the grudge, plus an NPC this process has not seen
        var theirs = OnlineStateManager.SerializeCurrentNPCs();
        var newcomer = Npc("npc_t1_new", "Newcomer");
        theirs.AddRange(OnlineStateManager.SerializeCurrentNPCs().Where(d => d.Name == "Newcomer"));
        NPCSpawnSystem.Instance.ActiveNPCs.Remove(newcomer);
        long v0 = _db.GetWorldStateVersion(OnlineStateManager.KEY_NPCS);

        int hooks = 0;
        int changed = await PermadeathHelper.ForgetCharacterInNpcWorldAsync(_db, new[] { "Bob" }, DateTime.Now, true,
            beforeWrite: async () => { if (hooks++ == 0) await _db.SaveWorldState(OnlineStateManager.KEY_NPCS, JsonSerializer.Serialize(theirs, Json)); });

        changed.Should().BeGreaterThan(0);
        _db.GetWorldStateVersion(OnlineStateManager.KEY_NPCS).Should().Be(v0 + 2, "the conflicting write, then ours on the retry");
        var stored = await StoredRoster();
        stored.Select(d => d.Name).Should().Contain("Newcomer", "the other writer's roster was reloaded, not overwritten");
        stored.Single(d => d.Name == "Grudger").Memories.Should().NotContain(m => m.InvolvedCharacter == "Bob",
            "the clean-up was applied again to the reloaded roster");
        Find("Grudger")!.Brain!.Memory.AllMemories.Should().NotContain(m => m.InvolvedCharacter == "Bob");
    }

    [Fact]
    public void APlayerNpcMarriage_IsCleared_AnNpcNpcMarriage_IsKept()
    {
        var reg = NPCMarriageRegistry.Instance;
        var wife = Npc("npc_r13_wife", "Wife");
        var ann = Npc("npc_r13_ann", "Ann");
        var npcBob = Npc("npc_r13_bob", "Bob");
        foreach (var (n, s) in new[] { (wife, "Bob"), (ann, "Bob"), (npcBob, "Ann") }) { n.SpouseName = s; n.Married = true; n.IsMarried = true; }
        reg.RegisterMarriage("player_bob_id", wife.ID, "Bob", "Wife");   // the player Bob married Wife
        reg.RegisterMarriage(ann.ID, npcBob.ID, "Ann", "Bob");           // Ann married the NPC Bob
        try
        {
            var ended = new HashSet<string>();
            PermadeathHelper.ClearNpcSpousesOf("Bob", ended).Should().Be(1);
            wife.SpouseName.Should().BeEmpty("a player-NPC marriage is in the registry too, and is cleared");
            wife.IsMarried.Should().BeFalse();
            reg.IsMarriedToNPC(wife.ID).Should().BeFalse("the registry entry goes as in a divorce");
            reg.IsMarriedToNPC("player_bob_id").Should().BeFalse();
            ended.Should().Equal(wife.ID);
            ann.SpouseName.Should().Be("Bob", "Ann's partner is an NPC");
            ann.IsMarried.Should().BeTrue();
            reg.GetSpouseId(ann.ID).Should().Be(npcBob.ID);

            // an NPC-NPC marriage the registry lost is still recognised by the partner naming it back
            reg.EndMarriage(ann.ID);
            PermadeathHelper.ClearNpcSpousesOf("Bob").Should().Be(0);
            ann.SpouseName.Should().Be("Bob");
        }
        finally
        {
            reg.EndMarriage(wife.ID);
            reg.EndMarriage(ann.ID);
        }
    }

    [Fact]
    public async Task TheStoredMarriages_LoseOnlyTheEndedOnes_UnderTheirVersion()
    {
        const string record = "{\"marriages\":[{\"npc1Id\":\"player_bob_id\",\"npc2Id\":\"npc_w\"},{\"npc1Id\":\"npc_a\",\"npc2Id\":\"npc_b\"}]," +
                              "\"affairs\":[{\"marriedNpcId\":\"npc_a\",\"seducerId\":\"npc_z\"}],\"updatedAt\":\"x\"}";
        await _db.SaveWorldState(OnlineStateManager.KEY_MARRIAGES, record);
        long v0 = _db.GetWorldStateVersion(OnlineStateManager.KEY_MARRIAGES);
        int hooks = 0;
        (await OnlineStateManager.RemoveStoredMarriagesAsync(_db, new[] { "npc_w" },
            async () => { if (hooks++ == 0) await _db.SaveWorldState(OnlineStateManager.KEY_MARRIAGES, record.Replace("npc_z", "npc_q")); }))
            .Should().Be(1);
        _db.GetWorldStateVersion(OnlineStateManager.KEY_MARRIAGES).Should().Be(v0 + 2);
        var saved = System.Text.Json.Nodes.JsonNode.Parse((await _db.LoadWorldState(OnlineStateManager.KEY_MARRIAGES))!)!;
        saved["marriages"]!.AsArray().Select(m => (string)m!["npc1Id"]!).Should().Equal("npc_a");
        ((string)saved["affairs"]![0]!["seducerId"]!).Should().Be("npc_q", "the other writer's record was re-read and edited");
    }

    [Fact]
    public void ThePurgePaths_HaveNoUnconditionalNpcsWrite()
    {
        var osm = Source("Systems", "OnlineStateManager.cs");
        int start = osm.IndexOf("public static async Task<int> PersistNpcWorldNow", StringComparison.Ordinal);
        int end = osm.IndexOf("private static string IdOf", start, StringComparison.Ordinal);
        start.Should().BeGreaterThan(0);
        var persist = osm.Substring(start, end - start);
        persist.Should().Contain("SaveWorldStateIfVersion(KEY_NPCS");
        persist.Should().Contain("SaveWorldStateIfVersion(KEY_MARRIAGES");
        persist.Should().NotContain("SaveWorldState(KEY_").And.NotContain("SaveSharedNPCs(").And.NotContain(".SaveWorldState(");

        var helper = Source("Systems", "PermadeathHelper.cs");
        int p = helper.IndexOf("public static async Task PurgeDeletedCharacterAsync", StringComparison.Ordinal);
        var purge = helper.Substring(p, helper.IndexOf("public static bool QuestLeftByCharacter", p, StringComparison.Ordinal) - p);
        purge.Should().NotContain("SaveSharedNPCs(").And.NotContain("SaveAllSharedState(").And.NotContain("RemoveDeletedCharacterFromNpcJson");
        purge.Should().Contain("ForgetCharacterInNpcWorldAsync(");
    }

    private static string Source(string folder, string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Scripts"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "Scripts", folder, file));
    }
}
