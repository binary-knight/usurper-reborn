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
/// v1.1.11: the delete purge also ends a deleted king's reign (through the abdication path) and removes
/// mail and auction listings kept under the display name.
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

    [Fact]
    public async Task DeletingMarriedBob_LeavesALivingBobSmith_OnTheThrone()
    {
        // v1.1.11: the deleted Bob's married name is also a living player's name; that reign is theirs
        Player("bob_account", "Bob Smith");
        Exec("INSERT INTO players (username, display_name, player_data) VALUES ('bsmith', 'Bob Smith Jr', '{\"player\":{\"name2\":\"Bob Smith\"}}');");
        await WithKing(King.CreateNewKing("Bob Smith", CharacterAI.Human, CharacterSex.Male), async () =>
        {
            await PermadeathHelper.PurgeDeletedCharacterAsync(_db, "bob_account", "Bob");
            (CastleLocation.GetCurrentKing()?.Name).Should().Be("Bob Smith", "the living Bob Smith still reigns");
        });
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

    [Fact]
    public async Task MailToANameAnotherCharacterGoesBy_IsKept()
    {
        // v1.1.12: deleting a married "Bob Smith" erased the mail of another character named "Bob Smith"
        Player("bob_account", "Bob Smith");
        Exec("INSERT INTO players (username, display_name, player_data) VALUES ('bsmith', 'Robert', '{\"player\":{\"name2\":\"Bob Smith\"}}');");
        await _db.SendMessage("System", "Bob Smith", "mail", "for the other Bob Smith");
        await _db.SendMessage("System", "Bob", "mail", "for the deleted Bob");
        WithCompleteRoster(() => _db.PurgePlayerWorldState("bob_account", "Bob"));
        Count("SELECT COUNT(*) FROM messages WHERE to_player = 'Bob Smith';").Should().Be(1);
        Count("SELECT COUNT(*) FROM messages WHERE to_player = 'Bob';").Should().Be(0);
    }

    [Fact]
    public async Task MailToTheKey_IsKept_WhenAnotherCharacterGoesByThatName()
    {
        // v1.1.12: account "bob" deleting its character "Alice" erased the mail of another account's character "Bob"
        Player("bob", "Alice");
        Exec("INSERT INTO players (username, display_name, player_data) VALUES ('robin', 'Robin', '{\"player\":{\"name2\":\"Bob\"}}');");
        await _db.SendMessage("System", "Bob", "mail", "for the other Bob");
        await _db.SendMessage("System", "Alice", "mail", "for the deleted Alice");
        await _db.SendMessage("bob", "Robin", "mail", "sent by the deleted key");
        WithCompleteRoster(() => _db.PurgePlayerWorldState("bob", "Alice"));
        Count("SELECT COUNT(*) FROM messages WHERE to_player = 'Bob';").Should().Be(1, "another character's name2 is Bob");
        Count("SELECT COUNT(*) FROM messages WHERE to_player = 'Alice';").Should().Be(0);
        Count("SELECT COUNT(*) FROM messages WHERE from_player = 'bob';").Should().Be(0);
    }

    [Fact]
    public async Task MailToTheKey_IsPurged_WhenNoOtherCharacterGoesByThatName()
    {
        Player("bob", "Alice");
        Player("robin", "Robin");
        await _db.SendMessage("System", "bob", "mail", "for the deleted key");
        WithCompleteRoster(() => _db.PurgePlayerWorldState("bob", "Alice"));
        Count("SELECT COUNT(*) FROM messages WHERE LOWER(to_player) = 'bob';").Should().Be(0);
    }

    [Fact]
    public void NoTeamOrSiegeScreen_LeavesTheViewerOutByDisplayName()
    {
        // v1.1.12: a display name can be a teammate's too; the viewer is left out by save key
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Scripts"))) dir = dir.Parent;
        foreach (var file in new[] { "TeamCornerLocation.cs", "CastleLocation.cs" })
        {
            string src = File.ReadAllText(Path.Combine(dir!.FullName, "Scripts", "Locations", file));
            System.Text.RegularExpressions.Regex.Matches(src, @"GetPlayerTeamMembers\([^)]*,").Count.Should().Be(0, file);
        }
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
    public void AFreshProcess_ReadsTheSharedRoyalCourt_BeforeDecidingTheDeletedKingsReign()
    {
        var king = King.CreateNewKing("Bob", CharacterAI.Human, CharacterSex.Male);

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
        read.Should().BeGreaterThan(0);
        body.Should().NotContain("RoyalCourtLoadedFromShared", "the shared court is read even by a process that loaded it earlier");
        int loop = body.IndexOf("for (int attempt", StringComparison.Ordinal);
        body.IndexOf("await loadShared()", loop, StringComparison.Ordinal)
            .Should().BeGreaterThan(body.IndexOf("SharedCourtNamesDeletedCharacter(", read, StringComparison.Ordinal), "the court is applied only on a match")
            .And.BeLessThan(body.IndexOf("IsDeletedCharactersReign(", loop, StringComparison.Ordinal));
        body.Should().Contain("await saveSharedIfVersion(version)", "the ended reign is written under the version read");
        castle.Should().Contain("osm.ReadRoyalCourtWithVersionAsync, osm.LoadRoyalCourtFromWorldState, v => osm.SaveRoyalCourtIfVersionAsync(v, throneVacated: true)");
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
                    () => Task.FromResult<(RoyalCourtSaveData?, long)>((shared, 1)),
                    () => { applied = true; return Task.CompletedTask; },
                    _ => { saved = true; return Task.FromResult(true); });
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
                    () => Task.FromResult<(RoyalCourtSaveData?, long)>((bobCourt, 1)),
                    () => { applied = true; CastleLocation.SetKing(King.CreateNewKing("Bob", CharacterAI.Human, CharacterSex.Male)); return Task.CompletedTask; },
                    _ => { saved = true; return Task.FromResult(true); });
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

    // ─── v1.1.11: review round 14 ───

    private static OnlineStateManager NewOsm(SqlSaveBackend db) =>
        (OnlineStateManager)Activator.CreateInstance(typeof(OnlineStateManager),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, new object[] { db, "r14" }, null)!;

    private RoyalCourtSaveData StoredCourt() =>
        System.Text.Json.JsonSerializer.Deserialize<RoyalCourtSaveData>(_db.LoadWorldState("royal_court").Result!,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    [Fact]
    public async Task AStaleCourt_DoesNotAbdicateAFormerKing_OverTheSharedNewKing()
    {
        bool loadedBefore = CastleLocation.RoyalCourtLoadedFromShared;
        try
        {
            var osm = NewOsm(_db);
            // this process loaded Bob as king; Alice took the throne in another process since
            var bob = King.CreateNewKing("Bob", CharacterAI.Human, CharacterSex.Male);
            await _db.SaveWorldState("royal_court", CourtJson(new RoyalCourtSaveData { KingName = "Alice", KingAI = (int)CharacterAI.Human, Treasury = 777 }));
            long version = _db.GetWorldStateVersion("royal_court");
            await WithKing(bob, async () =>
            {
                CastleLocation.RoyalCourtLoadedFromShared = true;
                bool ended = await CastleLocation.AbdicateDeletedKingAsync("Bob", "Bob", "left", online: true,
                    osm.ReadRoyalCourtWithVersionAsync, osm.LoadRoyalCourtFromWorldState, v => osm.SaveRoyalCourtIfVersionAsync(v, throneVacated: true));
                ended.Should().BeFalse("the shared king is Alice");
                StoredCourt().KingName.Should().Be("Alice");
                StoredCourt().Treasury.Should().Be(777);
                _db.GetWorldStateVersion("royal_court").Should().Be(version, "nothing was written");
            });
        }
        finally { CastleLocation.RoyalCourtLoadedFromShared = loadedBefore; }
    }

    [Fact]
    public async Task AVersionConflict_ReadsTheCourtAgain_AndDecidesAgain()
    {
        bool loadedBefore = CastleLocation.RoyalCourtLoadedFromShared;
        try
        {
            var osm = NewOsm(_db);
            // Alice takes the throne between the read and the write: the write fails, the re-read sees Alice
            await _db.SaveWorldState("royal_court", CourtJson(new RoyalCourtSaveData { KingName = "Bob", KingAI = (int)CharacterAI.Human }));
            await WithKing(King.CreateNewKing("Bob", CharacterAI.Human, CharacterSex.Male), async () =>
            {
                int saves = 0;
                bool ended = await CastleLocation.AbdicateDeletedKingAsync("Bob", "Bob", "left", online: true,
                    osm.ReadRoyalCourtWithVersionAsync, osm.LoadRoyalCourtFromWorldState, async v =>
                    {
                        if (saves++ == 0)
                            await _db.SaveWorldState("royal_court", CourtJson(new RoyalCourtSaveData { KingName = "Alice", KingAI = (int)CharacterAI.Human }));
                        return await osm.SaveRoyalCourtIfVersionAsync(v, throneVacated: true);
                    });
                ended.Should().BeFalse();
                saves.Should().Be(1);
                StoredCourt().KingName.Should().Be("Alice", "the newer court is not overwritten");
                (CastleLocation.GetCurrentKing()?.Name).Should().Be("Alice", "this process takes the shared court back");
            });

            // a concurrent write that keeps Bob (a treasury change): the retry ends the reign and is written
            await _db.SaveWorldState("royal_court", CourtJson(new RoyalCourtSaveData { KingName = "Bob", KingAI = (int)CharacterAI.Human }));
            await WithKing(King.CreateNewKing("Bob", CharacterAI.Human, CharacterSex.Male), async () =>
            {
                int saves = 0;
                int bobRecords = CastleLocation.GetMonarchHistory().Count(m => m.Name == "Bob");
                bool ended = await CastleLocation.AbdicateDeletedKingAsync("Bob", "Bob", "left", online: true,
                    osm.ReadRoyalCourtWithVersionAsync, osm.LoadRoyalCourtFromWorldState, async v =>
                    {
                        if (saves++ == 0)
                            await _db.SaveWorldState("royal_court", CourtJson(new RoyalCourtSaveData { KingName = "Bob", KingAI = (int)CharacterAI.Human, Treasury = 5 }));
                        return await osm.SaveRoyalCourtIfVersionAsync(v, throneVacated: true);
                    });
                ended.Should().BeTrue();
                saves.Should().Be(2);
                StoredCourt().KingName.Should().NotBe("Bob");
                CastleLocation.GetMonarchHistory().Count(m => m.Name == "Bob").Should().Be(bobRecords + 1, "the retry records the reign once");
            });
        }
        finally { CastleLocation.RoyalCourtLoadedFromShared = loadedBefore; }
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

    [Fact]
    public async Task AVacancy_CarriesTheMonarchHistory_AndBothLoadersImportIt()
    {
        bool loadedBefore = CastleLocation.RoyalCourtLoadedFromShared;
        var osm = (OnlineStateManager)Activator.CreateInstance(typeof(OnlineStateManager),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, new object[] { _db, "r15" }, null)!;
        try
        {
            await WithKing(null!, async () =>
            {
                // this process ended Bob's reign with no successor; the history records it
                CastleLocation.SetMonarchHistory(new List<MonarchRecord>
                {
                    new MonarchRecord { Name = "Old Queen", Title = "Queen", DaysReigned = 40, EndReason = "Abdicated" },
                    new MonarchRecord { Name = "Bob", Title = "King", DaysReigned = 12, EndReason = "left the throne and the realm" }
                });
                (await osm.SaveRoyalCourtIfVersionAsync(_db.GetWorldStateVersion("royal_court"), throneVacated: true)).Should().BeTrue();
                var stored = (await osm.ReadRoyalCourtFromWorldState())!;
                stored.ThroneVacant.Should().BeTrue();
                stored.MonarchHistory.Select(m => m.Name).Should().Equal("Old Queen", "Bob");

                // the login loader
                CastleLocation.SetMonarchHistory(new List<MonarchRecord>());
                await osm.LoadRoyalCourtFromWorldState();
                CastleLocation.GetMonarchHistory().Select(m => m.Name).Should().Equal("Old Queen", "Bob");
                CastleLocation.GetMonarchHistory()[1].DaysReigned.Should().Be(12);

                // the world sim's loader, which then fills the throne
                CastleLocation.SetMonarchHistory(new List<MonarchRecord>());
                new WorldSimService(_db).LoadRoyalCourtFromWorldState();
                CastleLocation.GetMonarchHistory().Select(m => m.Name).Should().StartWith(new[] { "Old Queen", "Bob" });

                // an older vacancy with no history leaves this process's history alone
                CastleLocation.SetMonarchHistory(new List<MonarchRecord> { new MonarchRecord { Name = "Kept" } });
                CastleLocation.ApplySharedThroneVacancy(new RoyalCourtSaveData { ThroneVacant = true }).Should().BeTrue();
                CastleLocation.GetMonarchHistory().Select(m => m.Name).Should().Equal("Kept");
            });
        }
        finally { CastleLocation.RoyalCourtLoadedFromShared = loadedBefore; }
    }

    private static string Source(string folder, string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Scripts"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "Scripts", folder, file));
    }
}
