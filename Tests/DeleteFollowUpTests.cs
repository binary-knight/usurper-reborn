using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.11: the delete purge also ends a deleted king's reign (through the abdication path), drops NPC
/// grudges against the name, and removes mail and auction listings kept under the display name.
/// </summary>
[Collection("SharedGameSingletons")]
public class DeleteFollowUpTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-delfu-{Guid.NewGuid():N}.db");
    private readonly SqlSaveBackend _db;

    public DeleteFollowUpTests() { _db = new SqlSaveBackend(_path); }

    public void Dispose()
    {
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

    private long Count(string sql)
    {
        using var conn = new SqliteConnection($"Data Source={_path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private void Player(string username, string displayName) =>
        Exec($"INSERT INTO players (username, display_name, player_data) VALUES ('{username}', '{displayName}', '{{}}');");

    /// <summary>Runs the body with the given king, then puts the world's throne back as it was.</summary>
    private static async Task WithKing(King king, Func<Task> body)
    {
        var before = CastleLocation.GetCurrentKing();
        var history = CastleLocation.GetMonarchHistory().ToList();
        var flagged = NPCSpawnSystem.Instance.ActiveNPCs.Where(n => n.King).ToList();
        CastleLocation.SetKing(king);
        try { await body(); }
        finally
        {
            foreach (var n in NPCSpawnSystem.Instance.ActiveNPCs) n.King = flagged.Contains(n);
            CastleLocation.SetKing(before);
            CastleLocation.SetMonarchHistory(history);
        }
    }

    [Fact]
    public async Task ADeletedKing_Abdicates_ThroughTheNormalPath()
    {
        await WithKing(King.CreateNewKing("Bob", CharacterAI.Human, CharacterSex.Male), async () =>
        {
            await PermadeathHelper.PurgeDeletedCharacterAsync(null, "bob_account", "bob");

            var now = CastleLocation.GetCurrentKing();
            (now == null || now.Name != "Bob").Should().BeTrue("the deleted character no longer reigns");
            var last = CastleLocation.GetMonarchHistory().Last();
            last.Name.Should().Be("Bob", "the abdication path records the reign");
            last.EndReason.Should().Be("left the throne and the realm");
        });
    }

    [Fact]
    public async Task AMarriedKing_IsMatchedByTheStoredDisplayName()
    {
        Player("bob_account", "Bob Smith");
        await WithKing(King.CreateNewKing("Bob Smith", CharacterAI.Human, CharacterSex.Male), async () =>
        {
            await PermadeathHelper.PurgeDeletedCharacterAsync(_db, "bob_account", "Bob");
            CastleLocation.GetMonarchHistory().Last().Name.Should().Be("Bob Smith");
            (CastleLocation.GetCurrentKing()?.Name).Should().NotBe("Bob Smith");
        });
    }

    [Fact]
    public async Task AnNPCKingOfTheSameName_KeepsTheThrone()
    {
        var npcKing = King.CreateNewKing("Bob", CharacterAI.Computer, CharacterSex.Male);
        await WithKing(npcKing, async () =>
        {
            await PermadeathHelper.PurgeDeletedCharacterAsync(null, "bob_account", "Bob");
            CastleLocation.GetCurrentKing().Should().BeSameAs(npcKing);
            npcKing.IsActive.Should().BeTrue();
        });
        await WithKing(King.CreateNewKing("Alice", CharacterAI.Human, CharacterSex.Female), async () =>
        {
            CastleLocation.AbdicateDeletedKing("Bob", "Bob", "x").Should().BeFalse("another player's reign is untouched");
            await Task.CompletedTask;
        });
    }

    private static MemoryEvent Mem(MemoryType type, string who, float impact = 0f) =>
        new MemoryEvent { Type = type, InvolvedCharacter = who, Description = type.ToString(), EmotionalImpact = impact, Importance = 0.9f };

    [Fact]
    public void GrudgeTypes_AreTheNegativeOnes()
    {
        MemorySystem.IsGrudge(MemoryType.Attacked, 0f).Should().BeTrue();
        MemorySystem.IsGrudge(MemoryType.Murdered, 0f).Should().BeTrue();
        MemorySystem.IsGrudge(MemoryType.KilledMyParent, 0f).Should().BeTrue();
        MemorySystem.IsGrudge(MemoryType.HeardGossip, -0.4f).Should().BeTrue("a negative memory of any type");
        MemorySystem.IsGrudge(MemoryType.Helped, 0.4f).Should().BeFalse();
        MemorySystem.IsGrudge(MemoryType.Traded, 0f).Should().BeFalse();
    }

    [Fact]
    public async Task NpcGrudgesAgainstTheName_AreDropped_OthersKept()
    {
        var npc = new NPC { ID = "npc_grudge_test", Name1 = "Grudger", Name2 = "Grudger", Level = 10 };
        npc.Memory = new MemorySystem();
        npc.Memory.RecordEvent(Mem(MemoryType.Attacked, "Bob"));
        npc.Memory.RecordEvent(Mem(MemoryType.Insulted, "bob"));
        npc.Memory.RecordEvent(Mem(MemoryType.Helped, "Bob", 0.4f));
        npc.Memory.RecordEvent(Mem(MemoryType.Attacked, "Alice"));
        NPCSpawnSystem.Instance.ActiveNPCs.Add(npc);
        try
        {
            npc.Memory.GetCharacterImpression("Bob").Should().BeLessThan(0f, "the seed must be a real grudge");
            await PermadeathHelper.PurgeDeletedCharacterAsync(null, "bob_account", "Bob");

            var left = npc.Memory.AllMemories;
            left.Should().NotContain(m => m.InvolvedCharacter.Equals("Bob", StringComparison.OrdinalIgnoreCase) && m.Type != MemoryType.Helped);
            left.Should().Contain(m => m.InvolvedCharacter == "Bob" && m.Type == MemoryType.Helped, "a kind memory is kept");
            left.Should().Contain(m => m.InvolvedCharacter == "Alice", "another character's grudge is kept");
            npc.Memory.GetCharacterImpression("Bob").Should().BeGreaterThan(0f, "the impression is rebuilt from what remains");
            npc.Memory.GetCharacterImpression("Alice").Should().BeLessThan(0f);
        }
        finally { NPCSpawnSystem.Instance.ActiveNPCs.Remove(npc); }
    }

    [Fact]
    public async Task TheSharedNpcRecord_LosesOnlyTheGrudges_AndKeepsEverythingElse()
    {
        string json = "[{\"name\":\"Grudger\",\"level\":10,\"unknownField\":{\"x\":1},\"memories\":[" +
            "{\"type\":\"Attacked\",\"involvedCharacter\":\"Bob\",\"emotionalImpact\":-0.5}," +
            "{\"type\":\"Helped\",\"involvedCharacter\":\"Bob\",\"emotionalImpact\":0.4}," +
            "{\"type\":\"Betrayed\",\"involvedCharacter\":\"Alice\",\"emotionalImpact\":-0.9}]}," +
            "{\"name\":\"Other\",\"memories\":[]}]";
        await _db.SaveWorldState(OnlineStateManager.KEY_NPCS, json);
        long version = _db.GetWorldStateVersion(OnlineStateManager.KEY_NPCS);

        await PermadeathHelper.PurgeDeletedCharacterAsync(_db, "bob_account", "Bob");

        var saved = System.Text.Json.Nodes.JsonNode.Parse((await _db.LoadWorldState(OnlineStateManager.KEY_NPCS))!)!.AsArray();
        var memories = saved[0]!["memories"]!.AsArray();
        memories.Select(m => (string)m!["type"]!).Should().BeEquivalentTo(new[] { "Helped", "Betrayed" });
        ((int)saved[0]!["unknownField"]!["x"]!).Should().Be(1, "the record is edited in place, not rebuilt");
        saved.Count.Should().Be(2);
        _db.GetWorldStateVersion(OnlineStateManager.KEY_NPCS).Should().Be(version + 1, "a versioned write, so the world sim reloads it");

        PermadeathHelper.RemoveGrudgesFromNpcJson(json, "Carol", out int none).Should().BeNull();
        none.Should().Be(0);
    }

    [Fact]
    public async Task MailAndAuctions_UnderTheDisplayName_ArePurged_OthersKept()
    {
        Player("bob_account", "Bob Smith");
        Player("alice", "Alice");
        await _db.SendMessage("System", "Bob", "mail", "to the display name");
        await _db.SendMessage("System", "Bob Smith", "mail", "to the married name");
        await _db.SendMessage("System", "bob_account", "mail", "to the key");
        await _db.SendMessage("System", "Alice", "mail", "to someone else");
        await _db.SendMessage("Bob", "Alice", "mail", "sent by Bob, in Alice's inbox");

        int bobs = await _db.CreateAuctionListing("bob", "Sword", "{}", 100);
        int bobsMarried = await _db.CreateAuctionListing("Bob Smith", "Shield", "{}", 100);
        int alices = await _db.CreateAuctionListing("alice", "Helm", "{}", 100);
        int alicesSoldToBob = await _db.CreateAuctionListing("alice", "Ring", "{}", 500);
        (await _db.BuyAuctionListing(alicesSoldToBob, "bob")).Should().BeTrue();

        WithCompleteRoster(() => _db.PurgePlayerWorldState("bob_account", "Bob"));

        Count("SELECT COUNT(*) FROM messages WHERE to_player IN ('Bob', 'Bob Smith', 'bob_account');").Should().Be(0);
        Count("SELECT COUNT(*) FROM messages WHERE to_player = 'Alice';").Should().Be(2, "Alice's mail stays, even mail Bob sent");
        Count($"SELECT COUNT(*) FROM auction_listings WHERE id IN ({bobs}, {bobsMarried});").Should().Be(0);
        Count($"SELECT COUNT(*) FROM auction_listings WHERE id = {alices};").Should().Be(1);
        Count($"SELECT COUNT(*) FROM auction_listings WHERE id = {alicesSoldToBob} AND status = 'sold' AND COALESCE(gold_collected, 0) = 0;")
            .Should().Be(1, "the seller's uncollected gold must not vanish with the buyer");
    }

    [Fact]
    public async Task MailToANameThatIsAnotherAccountsKey_IsKept()
    {
        Player("bob", "Robert");                 // another account whose KEY is "bob"
        Player("bob_account", "Bob");
        await _db.SendMessage("System", "bob", "mail", "for the other account");
        _db.PurgePlayerWorldState("bob_account", "Bob");
        Count("SELECT COUNT(*) FROM messages WHERE to_player = 'bob';").Should().Be(1);
    }

    /// <summary>Runs the purge with a roster complete enough to rule out an NPC of the name (NPCSpawnSystem.IsCountPlausible).</summary>
    private static void WithCompleteRoster(Action run, params NPC[] members)
    {
        var spawner = NPCSpawnSystem.Instance;
        var added = new System.Collections.Generic.List<NPC>(members);
        for (int i = added.Count; i < 60; i++)
            added.Add(new NPC { ID = $"npc_auction_filler_{i}", Name1 = $"Filler {i}", Name2 = $"Filler {i}", Level = 5 });
        foreach (var n in added) spawner.ActiveNPCs.Add(n);
        try { run(); }
        finally { foreach (var n in added) spawner.ActiveNPCs.Remove(n); }
    }

    [Fact]
    public async Task AnNPCsListings_UnderTheSameName_AreKept()
    {
        // NPCs list auctions under their own name in lowercase; a player of that name being deleted must not
        // take them, and neither may a purge that cannot rule out such an NPC (an incomplete roster).
        Player("vesna_account", "Vesna");
        int npcListing = await _db.CreateAuctionListing("vesna", "Staff", "{}", 100);
        var npc = new NPC { ID = "npc_vesna", Name1 = "Vesna", Name2 = "Vesna", Level = 20 };
        WithCompleteRoster(() => _db.PurgePlayerWorldState("vesna_account", "Vesna"), npc);
        Count($"SELECT COUNT(*) FROM auction_listings WHERE id = {npcListing};").Should().Be(1, "an NPC carries the name");

        _db.PurgePlayerWorldState("vesna_account", "Vesna");   // the test roster alone is not complete
        Count($"SELECT COUNT(*) FROM auction_listings WHERE id = {npcListing};").Should().Be(1, "an incomplete roster cannot rule out the NPC");
    }
}
