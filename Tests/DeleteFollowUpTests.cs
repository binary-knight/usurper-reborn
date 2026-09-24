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

        PermadeathHelper.RemoveDeletedCharacterFromNpcJson(json, "Carol", out int none, out int noSpouse).Should().BeNull();
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

    // ─── v1.1.11: review round 12 ───

    private void DropJsonIndexes()
    {
        using var conn = new SqliteConnection($"Data Source={_path}");
        conn.Open();
        var names = new List<string>();
        using (var q = conn.CreateCommand())
        {
            q.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'players' AND sql LIKE '%json%';";
            using var r = q.ExecuteReader();
            while (r.Read()) names.Add(r.GetString(0));
        }
        foreach (var name in names) { using var d = conn.CreateCommand(); d.CommandText = $"DROP INDEX \"{name}\";"; d.ExecuteNonQuery(); }
    }

    private void PlayerWithBlob(string username, string displayName, string blob) =>
        Exec($"INSERT INTO players (username, display_name, player_data) VALUES ('{username}', '{displayName}', '{blob}');");

    [Fact]
    public async Task ANewBobsListing_SurvivesDeletingOldBobSmith()
    {
        PlayerWithBlob("old_bob", "Bob Smith", "{\"player\":{\"name2\":\"Bob\"}}");
        PlayerWithBlob("new_bob", "Bob Jones", "{\"player\":{\"name2\":\"Bob\"}}");   // Name2 Bob, since married
        DropJsonIndexes();   // a fresh database's expression indexes refuse a malformed blob; an upgraded one may lack them
        PlayerWithBlob("broken", "Zed", "not json {");                                    // a malformed blob in the table
        int newBobs = await _db.CreateAuctionListing("bob", "Axe", "{}", 100);
        int oldMarried = await _db.CreateAuctionListing("bob smith", "Shield", "{}", 100);

        WithCompleteRoster(() => _db.PurgePlayerWorldState("old_bob", "Bob"));

        Count($"SELECT COUNT(*) FROM auction_listings WHERE id = {newBobs};").Should().Be(1, "another player carries the name Bob now");
        Count($"SELECT COUNT(*) FROM auction_listings WHERE id = {oldMarried};").Should().Be(0, "the deleted character's own listing goes, malformed blob or not");
    }

    [Fact]
    public async Task AnNPCsListing_SurvivesDeletingAnAccountOfItsName()
    {
        // the username key pass gets the same NPC protection as the display-name pass
        Player("vesna", "Someone Else");
        int npcListing = await _db.CreateAuctionListing("vesna", "Staff", "{}", 100);
        var npc = new NPC { ID = "npc_vesna_key", Name1 = "Vesna", Name2 = "Vesna", Level = 20 };
        WithCompleteRoster(() => _db.PurgePlayerWorldState("vesna", "Someone Else"), npc);
        Count($"SELECT COUNT(*) FROM auction_listings WHERE id = {npcListing};").Should().Be(1, "an NPC carries the name");

        _db.PurgePlayerWorldState("vesna", "Someone Else");
        Count($"SELECT COUNT(*) FROM auction_listings WHERE id = {npcListing};").Should().Be(1, "an incomplete roster cannot rule out the NPC");

        int own = await _db.CreateAuctionListing("zanthor", "Ring", "{}", 100);
        Player("zanthor", "Zanthor");
        WithCompleteRoster(() => _db.PurgePlayerWorldState("zanthor", "Zanthor"));
        Count($"SELECT COUNT(*) FROM auction_listings WHERE id = {own};").Should().Be(0, "a name no NPC carries is purged");
    }

    [Fact]
    public async Task TheSharedNpcRecord_LosesTheSpouse_AndTheGrudges_InOneVersionedWrite()
    {
        string json = "[" +
            "{\"name\":\"Wife\",\"characterID\":\"npc_json_wife\",\"married\":true,\"isMarried\":true,\"spouseName\":\"Bob\",\"memories\":[" +
                "{\"type\":\"Attacked\",\"involvedCharacter\":\"Bob\",\"emotionalImpact\":-0.5}]}," +
            "{\"name\":\"Ann\",\"characterID\":\"npc_json_ann\",\"married\":true,\"isMarried\":true,\"spouseName\":\"Bob\",\"memories\":[]}," +
            "{\"name\":\"Bob\",\"characterID\":\"npc_json_bob\",\"married\":true,\"isMarried\":true,\"spouseName\":\"Ann\",\"memories\":[]}]";
        await _db.SaveWorldState(OnlineStateManager.KEY_NPCS, json);
        long version = _db.GetWorldStateVersion(OnlineStateManager.KEY_NPCS);

        var live = new NPC { ID = "npc_live_wife", Name1 = "Live Wife", Name2 = "Live Wife", Level = 10, SpouseName = "Bob", Married = true, IsMarried = true };
        NPCSpawnSystem.Instance.ActiveNPCs.Add(live);
        try
        {
            await PermadeathHelper.PurgeDeletedCharacterAsync(_db, "bob_account", "Bob");
            live.SpouseName.Should().BeEmpty("the in-memory clear still runs");
            live.IsMarried.Should().BeFalse();
        }
        finally { NPCSpawnSystem.Instance.ActiveNPCs.Remove(live); }

        var saved = System.Text.Json.Nodes.JsonNode.Parse((await _db.LoadWorldState(OnlineStateManager.KEY_NPCS))!)!.AsArray();
        var wife = saved[0]!;
        ((string)wife["spouseName"]!).Should().BeEmpty();
        ((bool)wife["married"]!).Should().BeFalse();
        ((bool)wife["isMarried"]!).Should().BeFalse();
        wife["memories"]!.AsArray().Should().BeEmpty("the grudge is gone too");
        ((string)saved[1]!["spouseName"]!).Should().Be("Bob", "Ann is married to the NPC Bob, who names her back");
        ((bool)saved[1]!["isMarried"]!).Should().BeTrue();
        _db.GetWorldStateVersion(OnlineStateManager.KEY_NPCS).Should().Be(version + 1, "one versioned write");

        // a record with only a marriage to the name (no grudge) is still edited
        string onlySpouse = "[{\"name\":\"W\",\"married\":true,\"isMarried\":true,\"spouseName\":\"Carl\"}]";
        PermadeathHelper.RemoveDeletedCharacterFromNpcJson(onlySpouse, "Carl", out int g, out int sp).Should().NotBeNull();
        g.Should().Be(0);
        sp.Should().Be(1);
    }

    [Fact]
    public void AFreshProcess_ReadsTheSharedRoyalCourt_BeforeDecidingTheDeletedKingsReign()
    {
        var king = King.CreateNewKing("Bob", CharacterAI.Human, CharacterSex.Male);
        CastleLocation.NeedsSharedCourtLoad(online: true, king: null, loadedFromShared: false).Should().BeTrue();
        CastleLocation.NeedsSharedCourtLoad(online: true, king: king, loadedFromShared: false).Should().BeTrue("a king not read from shared state may be stale");
        CastleLocation.NeedsSharedCourtLoad(online: true, king: king, loadedFromShared: true).Should().BeFalse();
        CastleLocation.NeedsSharedCourtLoad(online: false, king: null, loadedFromShared: false).Should().BeFalse("offline has no shared court");

        CastleLocation.IsDeletedCharactersReign(king, "Bob", null).Should().BeTrue();
        CastleLocation.IsDeletedCharactersReign(king, "x", "bob").Should().BeTrue();
        CastleLocation.IsDeletedCharactersReign(king, "Alice", "Alice").Should().BeFalse();
        CastleLocation.IsDeletedCharactersReign(King.CreateNewKing("Bob", CharacterAI.Computer, CharacterSex.Male), "Bob", "Bob").Should().BeFalse();
        CastleLocation.IsDeletedCharactersReign(null, "Bob", "Bob").Should().BeFalse();

        string castle = Source("Locations", "CastleLocation.cs");
        int start = castle.IndexOf("internal static async Task<bool> AbdicateDeletedKingAsync(", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0);
        string body = castle.Substring(start, castle.IndexOf("internal static bool SharedCourtNamesDeletedCharacter(", start, StringComparison.Ordinal) - start);
        int read = body.IndexOf("await readShared()", StringComparison.Ordinal);
        read.Should().BeGreaterThan(body.IndexOf("NeedsSharedCourtLoad(", StringComparison.Ordinal));
        body.IndexOf("await loadShared()", StringComparison.Ordinal)
            .Should().BeGreaterThan(body.IndexOf("SharedCourtNamesDeletedCharacter(", read, StringComparison.Ordinal), "the court is applied only on a match")
            .And.BeLessThan(body.IndexOf("IsDeletedCharactersReign(", StringComparison.Ordinal));
        body.Should().Contain("await saveShared()", "the ended reign is written to the shared royal_court");
        castle.Should().Contain("osm.ReadRoyalCourtFromWorldState, osm.LoadRoyalCourtFromWorldState, () => osm.SaveRoyalCourtToWorldState(throneVacated: true)");
        Source("Systems", "OnlineStateManager.cs").Should().Contain("CastleLocation.RoyalCourtLoadedFromShared = true;");
        Source("Systems", "PermadeathHelper.cs").Should().Contain("await global::CastleLocation.AbdicateDeletedKingAsync(");
    }

    // ─── v1.1.11: review round 13 ───

    private static string CourtJson(RoyalCourtSaveData court) =>
        System.Text.Json.JsonSerializer.Serialize(court, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });

    [Fact]
    public async Task AVacantThrone_IsHonouredByTheWorldSim_WhichDoesNotWriteTheKingBack()
    {
        bool loadedBefore = CastleLocation.RoyalCourtLoadedFromShared;
        try
        {
            var bob = King.CreateNewKing("Bob", CharacterAI.Human, CharacterSex.Male);
            await WithKing(bob, async () =>
            {
                // a fresh database with no court: absent changes nothing
                var sim = new WorldSimService(_db);
                var simVersion = typeof(WorldSimService).GetField("lastRoyalCourtVersion", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                sim.LoadRoyalCourtFromWorldState();
                CastleLocation.GetCurrentKing().Should().BeSameAs(bob);

                // control: the sim's court save does write its king when it holds the current version
                await _db.SaveWorldState("royal_court", CourtJson(new RoyalCourtSaveData { KingName = "Bob", KingAI = (int)CharacterAI.Human }));
                simVersion.SetValue(sim, _db.GetWorldStateVersion("royal_court"));
                await _db.SaveWorldState("royal_court", CourtJson(new RoyalCourtSaveData { KingName = "Nobody", KingAI = 1 }));
                simVersion.SetValue(sim, _db.GetWorldStateVersion("royal_court"));
                await sim.SaveRoyalCourtToWorldState();
                (await _db.LoadWorldState("royal_court")).Should().Contain("\"Bob\"", "the save path is live");

                // an empty name without the vacancy mark (an older write) is still ignored
                await _db.SaveWorldState("royal_court", CourtJson(new RoyalCourtSaveData { KingName = "", KingAI = 1 }));
                sim.LoadRoyalCourtFromWorldState();
                CastleLocation.GetCurrentKing().Should().BeSameAs(bob);

                // the deleting process ended the reign with no successor
                await _db.SaveWorldState("royal_court", CourtJson(new RoyalCourtSaveData { KingName = "", KingAI = 1, ThroneVacant = true }));
                sim.LoadRoyalCourtFromWorldState();
                (CastleLocation.GetCurrentKing()?.Name).Should().NotBe("Bob", "the world sim's copy of the deleted king is cleared");
                bob.IsActive.Should().BeFalse();

                simVersion.SetValue(sim, _db.GetWorldStateVersion("royal_court"));   // the sim holds the vacancy's version
                await sim.SaveRoyalCourtToWorldState();
                var stored = System.Text.Json.JsonSerializer.Deserialize<RoyalCourtSaveData>((await _db.LoadWorldState("royal_court"))!,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                stored.KingName.Should().NotBe("Bob", "the world sim's court save does not write the deleted king back");
            });
        }
        finally { CastleLocation.RoyalCourtLoadedFromShared = loadedBefore; }

        string osm = Source("Systems", "OnlineStateManager.cs");
        osm.Should().Contain("global::CastleLocation.ApplySharedThroneVacancy(royalCourt)", "the login loader honours it too");
        osm.Should().Contain("ThroneVacant = throneVacated");
        osm.Should().Contain("if (KeepsStoredVacancy(await ReadRoyalCourtFromWorldState(), throneVacated)) return;");
        OnlineStateManager.KeepsStoredVacancy(new RoyalCourtSaveData { ThroneVacant = true }, throneVacated: false)
            .Should().BeTrue("a later session's plain empty save does not hide the vacancy from the world sim");
        OnlineStateManager.KeepsStoredVacancy(new RoyalCourtSaveData { ThroneVacant = true }, throneVacated: true).Should().BeFalse();
        OnlineStateManager.KeepsStoredVacancy(new RoyalCourtSaveData { KingName = "Bob" }, throneVacated: false).Should().BeFalse();
        OnlineStateManager.KeepsStoredVacancy(null, throneVacated: false).Should().BeFalse();
        Source("Systems", "WorldSimService.cs").Should().Contain("ChallengeSystem.Instance.ClaimEmptyThroneIfVacant();");
    }

    [Fact]
    public async Task DeletingANonKing_ReadsOnlyTheName_AndLeavesTheCourtUntouched()
    {
        bool loadedBefore = CastleLocation.RoyalCourtLoadedFromShared;
        try
        {
            var alice = King.CreateNewKing("Alice", CharacterAI.Human, CharacterSex.Female);
            alice.Guards.Clear();   // she dismissed her guards
            var shared = new RoyalCourtSaveData
            {
                KingName = "Alice", KingAI = (int)CharacterAI.Human,
                Guards = new List<RoyalGuardSaveData> { new RoyalGuardSaveData { Name = "Old Guard", IsActive = true } }
            };
            await WithKing(alice, async () =>
            {
                CastleLocation.RoyalCourtLoadedFromShared = false;
                bool applied = false, saved = false;
                bool ended = await CastleLocation.AbdicateDeletedKingAsync("Bob", "Bob", "left", online: true,
                    () => Task.FromResult<RoyalCourtSaveData?>(shared),
                    () => { applied = true; return Task.CompletedTask; },
                    () => { saved = true; return Task.CompletedTask; });
                ended.Should().BeFalse();
                applied.Should().BeFalse("the court is not applied for a non-king");
                saved.Should().BeFalse();
                CastleLocation.GetCurrentKing().Should().BeSameAs(alice);
                alice.Guards.Should().BeEmpty("dismissed guards stay dismissed");
                CastleLocation.RoyalCourtLoadedFromShared.Should().BeFalse("a mere read does not count as a load");
            });

            // the shared court names the deleted character: it is loaded, the reign ends, and it is written back
            var bobCourt = new RoyalCourtSaveData { KingName = "Bob", KingAI = (int)CharacterAI.Human };
            await WithKing(alice, async () =>
            {
                CastleLocation.RoyalCourtLoadedFromShared = false;
                bool applied = false, saved = false;
                bool ended = await CastleLocation.AbdicateDeletedKingAsync("Bob", "Bob", "left", online: true,
                    () => Task.FromResult<RoyalCourtSaveData?>(bobCourt),
                    () => { applied = true; CastleLocation.SetKing(King.CreateNewKing("Bob", CharacterAI.Human, CharacterSex.Male)); return Task.CompletedTask; },
                    () => { saved = true; return Task.CompletedTask; });
                ended.Should().BeTrue();
                applied.Should().BeTrue();
                saved.Should().BeTrue();
                (CastleLocation.GetCurrentKing()?.Name).Should().NotBe("Bob");
            });
        }
        finally { CastleLocation.RoyalCourtLoadedFromShared = loadedBefore; }

        CastleLocation.SharedCourtNamesDeletedCharacter(new RoyalCourtSaveData { KingName = "Bob", KingAI = (int)CharacterAI.Computer }, "Bob", "Bob")
            .Should().BeFalse("an NPC king of the name");
        CastleLocation.SharedCourtNamesDeletedCharacter(new RoyalCourtSaveData { KingName = "", ThroneVacant = true }, "Bob", "Bob").Should().BeFalse();
        CastleLocation.SharedCourtNamesDeletedCharacter(new RoyalCourtSaveData { KingName = "Bob Smith", KingAI = (int)CharacterAI.Human }, "Bob", "Bob Smith").Should().BeTrue();
    }

    [Fact]
    public void APlayerNpcMarriage_IsCleared_InMemoryAndInTheJson_AndLeavesTheRegistry()
    {
        var reg = NPCMarriageRegistry.Instance;
        var wife = new NPC { ID = "npc_r13_wife", Name1 = "Wife", Name2 = "Wife", Level = 10, SpouseName = "Bob", Married = true, IsMarried = true };
        var ann = new NPC { ID = "npc_r13_ann", Name1 = "Ann", Name2 = "Ann", Level = 10, SpouseName = "Bob", Married = true, IsMarried = true };
        var npcBob = new NPC { ID = "npc_r13_bob", Name1 = "Bob", Name2 = "Bob", Level = 10, SpouseName = "Ann", Married = true, IsMarried = true };
        reg.RegisterMarriage("player_bob_id", wife.ID, "Bob", "Wife");   // the player Bob married Wife
        reg.RegisterMarriage(ann.ID, npcBob.ID, "Ann", "Bob");           // Ann married the NPC Bob
        NPCSpawnSystem.Instance.ActiveNPCs.AddRange(new[] { wife, ann, npcBob });
        try
        {
            PermadeathHelper.ClearNpcSpousesOf("Bob").Should().Be(1);
            wife.SpouseName.Should().BeEmpty("a player-NPC marriage is in the registry too, and is cleared");
            wife.IsMarried.Should().BeFalse();
            reg.IsMarriedToNPC(wife.ID).Should().BeFalse("the registry entry goes as in a divorce");
            reg.IsMarriedToNPC("player_bob_id").Should().BeFalse();
            ann.SpouseName.Should().Be("Bob", "Ann's partner is an NPC");
            reg.GetSpouseId(ann.ID).Should().Be(npcBob.ID);

            // the shared JSON, with the registry holding the player-NPC marriage again
            reg.RegisterMarriage("player_bob_id", wife.ID, "Bob", "Wife");
            string json = "[" +
                "{\"name\":\"Wife\",\"characterID\":\"npc_r13_wife\",\"married\":true,\"isMarried\":true,\"spouseName\":\"Bob\"}," +
                "{\"name\":\"Ann\",\"characterID\":\"npc_r13_ann\",\"married\":true,\"isMarried\":true,\"spouseName\":\"Bob\"}]";
            var edited = PermadeathHelper.RemoveDeletedCharacterFromNpcJson(json, "Bob", out _, out int spouses);
            spouses.Should().Be(1);
            var arr = System.Text.Json.Nodes.JsonNode.Parse(edited!)!.AsArray();
            ((string)arr[0]!["spouseName"]!).Should().BeEmpty();
            ((string)arr[1]!["spouseName"]!).Should().Be("Bob", "the registry marries Ann to an NPC");
            reg.IsMarriedToNPC(wife.ID).Should().BeFalse();
        }
        finally
        {
            foreach (var n in new[] { wife, ann, npcBob }) NPCSpawnSystem.Instance.ActiveNPCs.Remove(n);
            reg.EndMarriage(wife.ID);
            reg.EndMarriage(ann.ID);
        }
    }

    [Fact]
    public async Task TheMarriedNameAlias_IsCheckedAgainstTheNpcGuard_AliasByAlias()
    {
        // Ursula took the surname Ironheart; an NPC is called Ursula Ironheart
        Player("ursula", "Ursula Ironheart");
        int own = await _db.CreateAuctionListing("ursula", "Ring", "{}", 100);
        int npcs = await _db.CreateAuctionListing("ursula ironheart", "Axe", "{}", 100);
        var npc = new NPC { ID = "npc_ursula_ironheart", Name1 = "Ursula Ironheart", Name2 = "Ursula Ironheart", Level = 20 };
        WithCompleteRoster(() => _db.PurgePlayerWorldState("ursula", "Ursula"), npc);
        Count($"SELECT COUNT(*) FROM auction_listings WHERE id = {npcs};").Should().Be(1, "the stored display name is an NPC's name");
        Count($"SELECT COUNT(*) FROM auction_listings WHERE id = {own};").Should().Be(0, "the other aliases are still purged");

        SqlSaveBackend.AuctionSellerAliases("ursula", "Ursula", "Ursula Ironheart")
            .Should().Equal("ursula", "Ursula Ironheart");
        SqlSaveBackend.AuctionSellerAliases("bob_account", null, " ").Should().Equal("bob_account");

        string src = Source("Systems", "SqlSaveBackend.cs");
        int start = src.IndexOf("public void PurgePlayerWorldState(", StringComparison.Ordinal);
        string body = src.Substring(start, src.IndexOf("private const string SellerNotOtherPlayer", start, StringComparison.Ordinal) - start);
        body.Split("\"auction_listings\"").Length.Should().Be(2, "one auction DELETE, fed by the alias list the guard checks");
    }

    private static string Source(string folder, string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Scripts"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "Scripts", folder, file));
    }
}
