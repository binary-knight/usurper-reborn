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

/// <summary>v1.1.14: the maintainer's decisions on the 1.1.13 leftovers (court, teams, world edits, shared records).</summary>
public partial class OwnerProcessConflictTests
{
    // ─── C6: sales tax that gives up is carried into the next court change that lands ───

    [Fact]
    public async Task SalesTaxThatGivesUp_IsCreditedByTheNextCourtChange_ExactlyOnce()
    {
        await WithKing("Kim", 1000, async _ =>
        {
            OnlineStateManager.PendingSalesTax = 0;
            try
            {
                var dbA = new SqlSaveBackend(_path);
                await dbA.SaveWorldState("royal_court", Court("Kim", 1000));
                var osmA = NewOsm(dbA);
                await osmA.LoadRoyalCourtFromWorldState();

                // every write of the tax meets another process's write first: it gives up
                long b = 1000;
                (await CityControlSystem.AddSalesTaxAsync(50, osmA, () => OtherCourtWrite("Kim", b += 10))).Should().BeFalse();
                OnlineStateManager.PendingSalesTax.Should().Be(50, "the buyer paid; the share waits for the next court change");
                (await StoredCourt()).Treasury.Should().Be(b);

                // the next court change meets one conflict and is retried: the carried share is added once
                var player = new Character { Name2 = "Pat", Gold = 500 };
                int writes = 0;
                (await CastleLocation.MoveTreasuryGoldAsync(osmA, player, -100, async () =>
                {
                    if (writes++ == 0) await OtherCourtWrite("Kim", b);
                })).Should().BeTrue();
                writes.Should().Be(2);
                (await StoredCourt()).Treasury.Should().Be(b + 100 + 50, "the deposit and the carried tax, each once");
                OnlineStateManager.PendingSalesTax.Should().Be(0);

                // and the change after it adds nothing more
                (await CastleLocation.MoveTreasuryGoldAsync(osmA, player, -100)).Should().BeTrue();
                (await StoredCourt()).Treasury.Should().Be(b + 200 + 50, "no second credit");
                CastleLocation.GetCurrentKing()!.Treasury.Should().Be(b + 250);

                // a refused change keeps the carried share for the one after
                OnlineStateManager.CarrySalesTax(30);
                (await CastleLocation.MoveTreasuryGoldAsync(osmA, player, 1_000_000)).Should().BeFalse();
                OnlineStateManager.PendingSalesTax.Should().Be(30);
            }
            finally { OnlineStateManager.PendingSalesTax = 0; }
        });
    }

    // ─── X6: admin flags are kept by account name through a delete and recreate ───

    [Fact]
    public async Task AFrozenAndMutedAccount_KeepsItsFlags_ThroughACharacterDeleteAndRecreate()
    {
        await _db.SetFrozen("Bob", true, "admin");
        await _db.SetMuted("bob", true, "admin");
        _db.PurgePlayerWorldState("bob", "Bob");
        _db.DeleteGameData("bob");
        (await _db.GetWizardFlags("BOB")).Should().Be((true, true), "the flags are the account's, not the character's");

        // the web delete removes the rows of the account's character, never its flags
        var web = File.ReadAllText(Path.Combine(RepoRoot(), "web", "ssh-proxy.js"));
        int at = web.IndexOf("const tables = ['players'", StringComparison.Ordinal);
        at.Should().BeGreaterThan(0);
        web.Substring(at, web.IndexOf('\n', at) - at).Should().NotContain("wizard_flags");
    }

    // ─── X2: no later character uses the name: grudges are forgotten with no time cut-off ───

    [Fact]
    public void AReapply_ForgetsAReStampedGrudge_WhenNoLaterCharacterUsesTheName_AndKeepsItWhenOneDoes()
    {
        var npc = Npc("npc_x2_1", "Grudger");
        WorldEditLog.AppendForgetCharacter(_db, new[] { "Bob" }, "bob_account", DateTime.Now.AddMinutes(-30), untimed: true);
        // an old binary wrote the grudge back with a time after the delete
        Remember(npc, MemoryType.Attacked, "Bob", DateTime.Now.AddMinutes(-1));

        WorldEditLog.Apply(_db, _db.GetWorldEditsToApply()).Should().BeGreaterThan(0);
        npc.Brain!.Memory.AllMemories.Should().NotContain(m => m.InvolvedCharacter == "Bob", "no one else is Bob: the grudge is the deleted Bob's");

        // a later Bob on a new account: its grudges after the delete are its own
        Exec("INSERT INTO players (username, display_name, player_data, created_at) VALUES ('bob_two', 'Bob', " +
             "'{\"player\":{\"name2\":\"Bob\"}}', datetime('now', '+1 minute'));");
        Remember(npc, MemoryType.Attacked, "Bob", DateTime.Now.AddMinutes(-1));
        WorldEditLog.Apply(_db, _db.GetWorldEditsToApply());
        npc.Brain!.Memory.AllMemories.Should().Contain(m => m.InvolvedCharacter == "Bob", "the time cut-off holds once a later character uses the name");
    }

    // ─── C11: a royal decree is refused while the NPC roster is not trustworthy ───

    [Fact]
    public void ARoyalDecree_IsRefused_WhileTheRosterRebuilds_BeforeAnyGoldLeavesTheTreasury()
    {
        for (int i = 0; i < 20; i++) Npc($"npc_c11_{i}", $"Citizen{i}");
        bool trusted = NPCSpawnSystem.Instance.IsRosterTrustworthy;
        NPCSpawnSystem.Instance.IsRebuilding = true;
        CastleLocation.DecreeMustWaitForRoster().Should().BeTrue("a roster being rebuilt cannot tell a player from a missing NPC");
        NPCSpawnSystem.Instance.IsRebuilding = false;
        CastleLocation.DecreeMustWaitForRoster().Should().Be(!trusted);

        // the refusal comes before the treasury's court change, and says so in the player's language
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "Locations", "CastleLocation.cs"));
        int at = src.IndexOf("private async Task PlaceBounty()", StringComparison.Ordinal);
        var body = src.Substring(at, src.IndexOf("await Task.Delay(2500);", at, StringComparison.Ordinal) - at);
        int wait = body.IndexOf("else if (DecreeMustWaitForRoster())", StringComparison.Ordinal);
        wait.Should().BeGreaterThan(0);
        wait.Should().BeLessThan(body.IndexOf("CourtChangeAsync(", StringComparison.Ordinal));
        body.Substring(wait, 400).Should().Contain("Loc.Get(\"castle.bounty_roster_busy\")");
        foreach (var lang in new[] { "en", "es", "fr", "hu", "it" })
            File.ReadAllText(Path.Combine(RepoRoot(), "Localization", lang + ".json")).Should().Contain("\"castle.bounty_roster_busy\"");
    }

    // ─── O1: the unique display-name index, made only when no display name is shared ───

    private string? IndexSql() => Scalar1($"SELECT sql FROM sqlite_master WHERE type = 'index' AND name = '{SqlSaveBackend.DisplayNameUniqueIndex}';");

    private string? Scalar1(string sql)
    {
        using var conn = new SqliteConnection($"Data Source={_path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar()?.ToString();
    }

    [Fact]
    public void TheDisplayNameIndex_IsMade_OnlyWhenNoDisplayNameIsShared()
    {
        // a new database gets the live index, as defined live
        IndexSql().Should().Be("CREATE UNIQUE INDEX idx_players_display_name_unique ON players(LOWER(display_name))");
        Action second = () => Exec("INSERT INTO players (username, display_name, player_data) VALUES ('a1', 'Ann', '{}'), ('a2', 'ANN', '{}');");
        second.Should().Throw<SqliteException>("a second player named Ann, in any case, is refused");

        // an older database whose players share a name: the index is not made, and the start goes on
        Exec($"DROP INDEX {SqlSaveBackend.DisplayNameUniqueIndex};");
        Exec("INSERT INTO players (username, display_name, player_data) VALUES ('b1', 'Bob', '{}'), ('b2', 'bob', '{}');");
        SqliteConnection.ClearAllPools();
        _ = new SqlSaveBackend(_path);
        IndexSql().Should().BeNull("two players share 'bob'");

        // once they no longer do, the next start makes it
        Exec("UPDATE players SET display_name = 'Bobby' WHERE username = 'b2';");
        SqliteConnection.ClearAllPools();
        _ = new SqlSaveBackend(_path);
        IndexSql().Should().NotBeNull();
    }

    // ─── M5: the shared quests are written under the version read, merged and retried ───

    private static QuestData Quest(string id, string title) => new QuestData { Id = id, Title = title, Initiator = "Board" };

    private async Task<List<string>> StoredQuestTitles() =>
        JsonSerializer.Deserialize<List<QuestData>>((await _db.LoadWorldState(OnlineStateManager.KEY_QUESTS))!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!.Select(q => q.Title).OrderBy(t => t).ToList();

    [Fact]
    public async Task TwoProcessesSavingQuests_KeepEachOthersQuests_ThroughAConflict()
    {
        var osmA = NewOsm(new SqlSaveBackend(_path));
        var osmB = NewOsm(new SqlSaveBackend(_path));

        await osmA.SaveSharedQuests(new List<QuestData> { Quest("q1", "Rats") });
        await osmB.SaveSharedQuests(new List<QuestData> { Quest("q2", "Wolves") });
        (await StoredQuestTitles()).Should().Equal("Rats", "Wolves");

        // A changes its quest while B adds one between A's read and A's write: A reads again and retries
        int writes = 0;
        (await osmA.SaveSharedQuestsVersionedAsync(_db, new List<QuestData> { Quest("q1", "Rats II") }, async () =>
        {
            if (writes++ == 0) await osmB.SaveSharedQuests(new List<QuestData> { Quest("q2", "Wolves"), Quest("q3", "Bandits") });
        })).Should().BeTrue();
        writes.Should().Be(2);
        (await StoredQuestTitles()).Should().Equal("Bandits", "Rats II", "Wolves");

        // a quest another process removed stays removed; one this process dropped goes
        await osmB.RemoveSharedQuestsAsync(q => q.Id == "q1");
        await osmA.SaveSharedQuests(new List<QuestData> { Quest("q1", "Rats II"), Quest("q4", "Ghosts") });
        (await StoredQuestTitles()).Should().Equal("Bandits", "Ghosts", "Wolves");
        await osmA.SaveSharedQuests(new List<QuestData> { Quest("q1", "Rats II") });
        (await StoredQuestTitles()).Should().Equal("Bandits", "Wolves");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Scripts"))) dir = dir.Parent;
        return dir!.FullName;
    }
}

/// <summary>v1.1.14 T6: a guild left with no leader passes to a member once one can lead.</summary>
[Collection("SharedGameSingletons")]
public class LeaderlessGuildTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-lg-{Guid.NewGuid():N}.db");
    private readonly SqlSaveBackend _db;

    public LeaderlessGuildTests() { _db = new SqlSaveBackend(_path); }

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

    private string? Scalar(string sql)
    {
        using var conn = new SqliteConnection($"Data Source={_path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar()?.ToString();
    }

    private void Player(string key, int level, bool banned = false) =>
        Exec($"INSERT INTO players (username, display_name, player_data, is_banned) VALUES ('{key}', '{key}', " +
             $"'{{\"player\":{{\"level\":{level}}}}}', {(banned ? 1 : 0)});");

    /// <summary>A guild whose leader was deleted while every other member is banned: the succession found no one.</summary>
    private GuildSystem LeaderlessGuild()
    {
        var guilds = new GuildSystem(_path, register: false);
        Player("boss", 90);
        Player("exiled", 50, banned: true);
        Player("outcast", 70, banned: true);
        Exec("INSERT INTO guilds (name, display_name, leader_username) VALUES ('ironhand', 'Ironhand', 'boss');");
        Exec("INSERT INTO guild_members (username, guild_name, rank, joined_at) VALUES ('exiled', 'ironhand', 'Member', '2026-01-01 00:00:00');");
        Exec("INSERT INTO guild_members (username, guild_name, rank, joined_at) VALUES ('outcast', 'ironhand', 'Member', '2026-01-02 00:00:00');");
        guilds.PassLeadershipOf("boss").Should().Be(0, "every remaining member is banned");
        Scalar("SELECT leader_username FROM guilds WHERE name = 'ironhand'").Should().Be("boss");
        return guilds;
    }

    [Fact]
    public async Task AnUnbannedMember_TakesOverAGuildWithNoLeader()
    {
        LeaderlessGuild();
        await _db.UnbanPlayer("exiled");
        Scalar("SELECT leader_username FROM guilds WHERE name = 'ironhand'").Should().Be("exiled", "the unbanned member can lead; the other is still banned");
        Scalar("SELECT rank FROM guild_members WHERE username = 'exiled'").Should().Be("Leader");

        // a second unban does not move the leadership again
        await _db.UnbanPlayer("outcast");
        Scalar("SELECT leader_username FROM guilds WHERE name = 'ironhand'").Should().Be("exiled");
    }

    [Fact]
    public void ANonBannedJoiner_TakesOverAGuildWithNoLeader()
    {
        var guilds = LeaderlessGuild();
        Player("recruit", 10);
        guilds.AddMember("recruit", "ironhand").Should().BeNull();
        Scalar("SELECT leader_username FROM guilds WHERE name = 'ironhand'").Should().Be("recruit");
        Scalar("SELECT rank FROM guild_members WHERE username = 'recruit'").Should().Be("Leader");
    }

    [Fact]
    public void AWebUnban_IsPickedUpByTheSweep_AndAGuildWithALeaderIsLeftAlone()
    {
        var guilds = LeaderlessGuild();
        Exec("UPDATE players SET is_banned = 0 WHERE username = 'outcast';");   // the web's unban writes the row directly
        Player("chief", 20);
        Exec("INSERT INTO guilds (name, display_name, leader_username) VALUES ('stonehall', 'Stonehall', 'chief');");
        Exec("INSERT INTO guild_members (username, guild_name, rank) VALUES ('chief', 'stonehall', 'Leader');");

        guilds.FillLeaderlessGuilds().Should().Be(1);
        Scalar("SELECT leader_username FROM guilds WHERE name = 'ironhand'").Should().Be("outcast");
        Scalar("SELECT leader_username FROM guilds WHERE name = 'stonehall'").Should().Be("chief");
        guilds.FillLeaderlessGuilds().Should().Be(0, "nothing left to fill");
    }
}
