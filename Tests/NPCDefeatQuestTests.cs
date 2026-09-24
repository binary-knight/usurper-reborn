using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.11 (player report: "Have a Wanted quest, found NPC, beat them 4 times but quest still counts
/// Defeat (NPC) 0/1"): only the street fights reported an NPC's defeat to the quest system. A duel (the
/// Dormitory, the Dark Alley and its pit, the Inn, the Arena), sparing an NPC who surrendered, and the
/// Inn's challenge did not, so the bounty was never paid and the objective never moved.
/// </summary>
[Collection("SharedGameSingletons")]
public class NPCDefeatQuestTests
{
    private static readonly BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static (Character hunter, NPC target, Quest bounty) Wanted(string name)
    {
        var hunter = new Character { Name1 = "hunter_" + name, Name2 = "Hunter " + name, Level = 32, HP = 500, MaxHP = 500, Gold = 0 };
        var target = new NPC { ID = "npc_" + name, Name1 = name, Name2 = name, Level = 30, HP = 0, MaxHP = 400 };
        var bounty = new Quest
        {
            Title = "WANTED: " + name, Initiator = "The Crown", QuestTarget = QuestTarget.DefeatNPC,
            TargetNPCName = target.Name, BountyGold = 5000, Occupier = hunter.Name2, Date = DateTime.Now, DaysToComplete = 30
        };
        bounty.Objectives.Add(new QuestObjective(QuestObjectiveType.DefeatNPC, "Defeat " + name, 1, target.Name, target.Name));
        QuestSystem.AddQuestToDatabase(bounty);
        return (hunter, target, bounty);
    }

    private static async Task Outcome(CombatResult result)
    {
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        await (Task)typeof(CombatEngine).GetMethod("DeterminePvPOutcome", F)!.Invoke(engine, new object[] { result })!;
    }

    [Fact]
    public async Task ADuelWonAgainstTheTarget_PaysTheBountyAndMeetsTheObjective()
    {
        var (hunter, target, bounty) = Wanted("Duel Mark");
        await Outcome(new CombatResult { Player = hunter, Opponent = target });
        bounty.Deleted.Should().BeTrue("the bounty is completed");
        hunter.Gold.Should().BeGreaterThanOrEqualTo(5000);
    }

    [Fact]
    public async Task SparingTheTargetAfterItSurrenders_StillCounts()
    {
        var (hunter, target, bounty) = Wanted("Spared Mark");
        target.HP = 1;
        await Outcome(new CombatResult { Player = hunter, Opponent = target, Outcome = CombatOutcome.OpponentSpared });
        bounty.Deleted.Should().BeTrue("the target was beaten; sparing it is not a failure to defeat it");
    }

    [Fact]
    public async Task AWinAgainstAPlayerLoadedFromASave_IsNotAnNPCDefeat()
    {
        var (hunter, _, bounty) = Wanted("Player Named Alike");
        var savedPlayer = new Character { Name1 = "Player Named Alike", Name2 = "Player Named Alike", Level = 30, HP = 0, MaxHP = 400 };
        await Outcome(new CombatResult { Player = hunter, Opponent = savedPlayer });
        bounty.Deleted.Should().BeFalse();
        bounty.Deleted = true;   // leave the shared database clean
    }

    [Fact]
    public void TheHelper_MeetsTheObjectiveOfAClaimedQuestThatIsNotABounty()
    {
        var hunter = new Character { Name1 = "hunter_q", Name2 = "Hunter Q", Level = 32 };
        var target = new NPC { ID = "npc_q", Name1 = "Quest Mark", Name2 = "Quest Mark", Level = 30 };
        var quest = new Quest { Title = "Teach them a lesson", QuestTarget = QuestTarget.DefeatNPC, Occupier = hunter.Name2, Date = DateTime.Now, DaysToComplete = 30 };
        quest.Objectives.Add(new QuestObjective(QuestObjectiveType.DefeatNPC, "Defeat Quest Mark", 1, target.Name, target.Name));
        QuestSystem.AddQuestToDatabase(quest);
        QuestSystem.RecordNPCDefeat(hunter, target, killed: true);
        quest.Objectives[0].IsComplete.Should().BeTrue();
        quest.Deleted = true;
    }

    [Fact]
    public void TheInnChallenge_ReportsTheDefeat()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Scripts"))) dir = dir.Parent;
        string src = File.ReadAllText(Path.Combine(dir!.FullName, "Scripts", "Locations", "InnLocation.cs"));
        int start = src.IndexOf("private async Task ChallengeNPC(NPC npc)", StringComparison.Ordinal);
        int end = src.IndexOf("\n    private ", start + 10, StringComparison.Ordinal);
        src.Substring(start, end - start).Should().Contain("QuestSystem.RecordNPCDefeat(currentPlayer, npc, killed: false)");
    }

    [Fact]
    public async Task SparingTheTarget_DoesNotMeetAnAssassinationContract()
    {
        var (hunter, target, bounty) = Wanted("Contract Mark");
        bounty.QuestTarget = QuestTarget.Assassin;
        target.HP = 1;
        await Outcome(new CombatResult { Player = hunter, Opponent = target, Outcome = CombatOutcome.OpponentSpared });
        bounty.Deleted.Should().BeFalse("the contract is for a kill; the target walked away");
        hunter.Gold.Should().Be(0);
        bounty.Deleted = true;
    }

    [Fact]
    public void TwoSessionsBeatingTheTargetAtOnce_ArePaidOnce()
    {
        var (hunter, target, _) = Wanted("Race Mark");
        long paid = 0;
        System.Threading.Tasks.Parallel.For(0, 16, _ =>
            System.Threading.Interlocked.Add(ref paid, QuestSystem.AutoCompleteBountyForNPC(new Character { Name2 = "Racer", Level = 30 }, target.Name)));
        paid.Should().Be(5000);
    }

    [Fact]
    public async Task ANonLethalDuel_WonAgainstAnAssassinationTarget_DoesNotMeetTheContract()
    {
        // The Dormitory wake-up brawl brings the guest back after the fight (review).
        var (hunter, target, bounty) = Wanted("Sleeper Mark");
        bounty.QuestTarget = QuestTarget.Assassin;
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        typeof(CombatEngine).GetField("_pvpLethal", F)!.SetValue(engine, false);
        await (Task)typeof(CombatEngine).GetMethod("DeterminePvPOutcome", F)!.Invoke(engine, new object[] { new CombatResult { Player = hunter, Opponent = target } })!;
        bounty.Deleted.Should().BeFalse();
        bounty.Deleted = true;
    }

    private static Quest BountyOnPlayer(string name, long gold)
    {
        var q = new Quest
        {
            Title = "WANTED: " + name, Initiator = "The Crown", QuestTarget = QuestTarget.DefeatNPC, TargetNPCName = name,
            BountyGold = gold, IsPlayerBounty = true, Date = DateTime.Now, DaysToComplete = 30
        };
        QuestSystem.AddQuestToDatabase(q);
        return q;
    }

    [Fact]
    public async Task BeatingAPlayerWithABountyInADuel_PaysTheWinnerOnce()
    {
        // Jason's decision: a Crown bounty on a player is paid to whoever beats them in a duel. The
        // Inn and Dormitory attacks on a sleeping player end in this same duel outcome.
        var bounty = BountyOnPlayer("Wanted Rogue", 4000);
        var winner = new Character { Name1 = "sheriff", Name2 = "Sheriff", Level = 30, Gold = 0, HP = 500, MaxHP = 500 };
        var rogue = new Character { Name1 = "wanted_rogue", Name2 = "Wanted Rogue", Level = 30, HP = 0, MaxHP = 400, IsLoadedPlayer = true };
        await Outcome(new CombatResult { Player = winner, Opponent = rogue });
        winner.Gold.Should().BeGreaterThanOrEqualTo(4000);
        bounty.Deleted.Should().BeTrue();

        long after = winner.Gold;
        rogue.HP = 0;
        await Outcome(new CombatResult { Player = winner, Opponent = rogue });
        (winner.Gold - after).Should().BeLessThan(4000, "the bounty is paid once");
    }

    [Fact]
    public void APlayer_CannotCollectTheBountyOnThemselves()
    {
        var bounty = BountyOnPlayer("Self Collector", 3000);
        var self = new Character { Name1 = "self_collector", Name2 = "Self Collector", Level = 30, IsLoadedPlayer = true };
        QuestSystem.CollectBountiesOnPlayer(self, self).Should().BeEmpty();
        bounty.Deleted.Should().BeFalse();
        bounty.Deleted = true;
    }

    [Fact]
    public async Task ADuelWonAgainstAnNPC_StillDoesNotPayABountyOnAPlayerOfThatName()
    {
        var bounty = BountyOnPlayer("Twin Name", 3000);
        var winner = new Character { Name1 = "hunter_tn", Name2 = "Hunter TN", Level = 30, Gold = 0, HP = 500, MaxHP = 500 };
        var npcTwin = new NPC { ID = "npc_twin_name", Name1 = "Twin Name", Name2 = "Twin Name", Level = 30, HP = 0, MaxHP = 400 };
        await Outcome(new CombatResult { Player = winner, Opponent = npcTwin });
        bounty.Deleted.Should().BeFalse();
        bounty.Deleted = true;
    }

    // ─── v1.1.11: a hired guard is not the player it is named like ───

    [Fact]
    public async Task AHiredGuardNamedLikeAPlayer_PaysNothing_TheLoadedPlayerDoes()
    {
        var bounty = BountyOnPlayer("Rookie Guard", 3000);
        var winner = new Character { Name1 = "raider", Name2 = "Raider", Level = 30, Gold = 0, HP = 500, MaxHP = 500 };
        var guard = new Character { Name1 = "Rookie Guard", Name2 = "Rookie Guard", Level = 10, HP = 0, MaxHP = 100 };   // as HeadlessCombatResolver builds one
        await Outcome(new CombatResult { Player = winner, Opponent = guard });
        winner.Gold.Should().BeLessThan(3000, "the guard is not the player with the bounty");
        bounty.Deleted.Should().BeFalse();

        var player = PlayerCharacterLoader.CreateFromSaveData(new PlayerData { Name1 = "rookie_guard", Name2 = "Rookie Guard", Level = 20, MaxHP = 300 }, "Rookie Guard");
        player.IsLoadedPlayer.Should().BeTrue();
        player.HP = 0;
        await Outcome(new CombatResult { Player = winner, Opponent = player });
        winner.Gold.Should().BeGreaterThanOrEqualTo(3000);
        bounty.Deleted.Should().BeTrue();

        PlayerCharacterLoader.CreateFromSaveData(new PlayerData { Name2 = "Echo" }, "Echo", isEcho: true).IsLoadedPlayer.Should().BeFalse();
    }

    // ─── v1.1.11: the one-time claim in the shared database ───

    private static async Task WithSqlBackend(Func<SqlSaveBackend, string, Task> body)
    {
        string path = Path.Combine(Path.GetTempPath(), $"usurper-claim-{Guid.NewGuid():N}.db");
        var field = typeof(SaveSystem).GetField("instance", BindingFlags.NonPublic | BindingFlags.Static)!;
        var before = field.GetValue(null);
        var db = new SqlSaveBackend(path);
        SaveSystem.InitializeWithBackend(db);
        try { await body(db, path); }
        finally
        {
            field.SetValue(null, before);
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public async Task TryClaimBounty_SucceedsOncePerQuestId()
    {
        await WithSqlBackend(async (db, path) =>
        {
            db.TryClaimBounty("Qclaim1", "alice").Should().BeTrue();
            db.TryClaimBounty("Qclaim1", "bob").Should().BeFalse("another process took it");
            db.TryClaimBounty("Qclaim2", "bob").Should().BeTrue();
            db.TryClaimBounty("", "bob").Should().BeFalse();

            using var conn = new SqliteConnection($"Data Source={path}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO bounty_claims (quest_id, claimed_by) VALUES ('Qclaim1', 'carol');";
            cmd.ExecuteNonQuery().Should().Be(0, "a second claim of the same id changes no row");
            cmd.CommandText = "SELECT claimed_by FROM bounty_claims WHERE quest_id = 'Qclaim1';";
            (cmd.ExecuteScalar() as string).Should().Be("alice");
            await Task.CompletedTask;
        });
    }

    [Fact]
    public async Task APlayerBounty_PaidInOneProcess_PaysNothingThroughAnotherProcesssCopy()
    {
        await WithSqlBackend(async (db, _) =>
        {
            var bounty = BountyOnPlayer("Stale Rogue", 4000);
            var winner = new Character { Name1 = "sheriff_a", Name2 = "Sheriff A", Level = 30, Gold = 0 };
            var rogue = new Character { Name1 = "stale_rogue", Name2 = "Stale Rogue", Level = 30, IsLoadedPlayer = true };
            QuestSystem.CollectBountiesOnPlayer(winner, rogue).Should().ContainSingle();
            winner.Gold.Should().BeGreaterThanOrEqualTo(4000);

            // the other process still holds its own copy of the same bounty (same id, another object)
            var copy = BountyOnPlayer("Stale Rogue", 4000);
            copy.Id = bounty.Id;
            var other = new Character { Name1 = "sheriff_b", Name2 = "Sheriff B", Level = 30, Gold = 0 };
            QuestSystem.CollectBountiesOnPlayer(other, rogue).Should().BeEmpty();
            other.Gold.Should().Be(0);
            copy.Deleted.Should().BeTrue("another process took it, so it is gone here too");
            await Task.CompletedTask;
        });
    }

    [Fact]
    public async Task AnNPCBounty_AnotherProcessClaimedFirst_PaysNothing()
    {
        await WithSqlBackend(async (db, _) =>
        {
            var (hunter, target, bounty) = Wanted("Claimed Mark");
            db.TryClaimBounty(bounty.Id, "another process").Should().BeTrue();
            QuestSystem.AutoCompleteBountyForNPC(hunter, target.Name).Should().Be(0);
            hunter.Gold.Should().Be(0);
            bounty.Deleted.Should().BeTrue();

            var (hunter2, target2, bounty2) = Wanted("Unclaimed Mark");
            QuestSystem.AutoCompleteBountyForNPC(hunter2, target2.Name).Should().Be(5000);
            db.TryClaimBounty(bounty2.Id, "late").Should().BeFalse("the payout claimed it first");
            await Task.CompletedTask;
        });
    }

    [Fact]
    public async Task AFailedClaimWrite_LeavesTheBountyOpen_AndUnpaid()
    {
        // v1.1.11: a busy database made the claim fail; the bounty was marked Deleted, unpaid, and lost
        await WithSqlBackend(async (db, path) =>
        {
            using (var conn = new SqliteConnection($"Data Source={path}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DROP TABLE bounty_claims;";   // every claim write now fails
                cmd.ExecuteNonQuery();
            }
            db.TryClaimBountyOrFail("Qfail", "x").Should().BeNull();

            var bounty = BountyOnPlayer("Busy Rogue", 4000);
            var winner = new Character { Name1 = "sheriff_f", Name2 = "Sheriff F", Level = 30, Gold = 0 };
            var rogue = new Character { Name1 = "busy_rogue", Name2 = "Busy Rogue", Level = 30, IsLoadedPlayer = true };
            try
            {
                QuestSystem.CollectBountiesOnPlayer(winner, rogue).Should().BeEmpty();
                bounty.Deleted.Should().BeFalse("the claim was not written, so the bounty is still open");
                winner.Gold.Should().Be(0);
            }
            finally { bounty.Deleted = true; }

            var (hunter, target, npcBounty) = Wanted("Busy Mark");
            try
            {
                QuestSystem.AutoCompleteBountyForNPC(hunter, target.Name).Should().Be(0);
                npcBounty.Deleted.Should().BeFalse();
            }
            finally { npcBounty.Deleted = true; }
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void AWinnersMarriedName_DoesNotBlock_TheBountyOnThePlayerOfThatName()
    {
        // v1.1.11: "Bob" took the surname Smith and beat the real "Bob Smith"; the bounty is Bob Smith's, and paid
        var bounty = BountyOnPlayer("Bob W Smith", 3000);
        var winner = new Character { Name1 = "bob_w", Name2 = "Bob W", FamilySurname = "Smith", Level = 30, Gold = 0 };
        winner.DisplayName.Should().Be("Bob W Smith");
        var target = new Character { Name1 = "bob_w_smith", Name2 = "Bob W Smith", Level = 30, IsLoadedPlayer = true };
        try
        {
            QuestSystem.CollectBountiesOnPlayer(winner, target).Should().ContainSingle();
            winner.Gold.Should().BeGreaterThanOrEqualTo(3000);
        }
        finally { bounty.Deleted = true; }
    }

    [Fact]
    public void TheNPCThroneChallenge_IsNotLethal()
    {
        // v1.1.11: the NPC king's HP is put back after the fight, so beating them is not a kill for a contract
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Scripts"))) dir = dir.Parent;
        string src = File.ReadAllText(Path.Combine(dir!.FullName, "Scripts", "Locations", "CastleLocation.cs"));
        int npcKing = src.IndexOf("kingCharacter = kingNpc;", StringComparison.Ordinal);
        npcKing.Should().BeGreaterThan(0);
        int call = src.IndexOf("PlayerVsPlayer(", npcKing, StringComparison.Ordinal);
        int restore = src.IndexOf("kingCharacter.HP = Math.Max(1, origHP);", npcKing, StringComparison.Ordinal);
        call.Should().BeLessThan(restore);
        src.Substring(call, src.IndexOf(';', call) - call).Should().Contain("lethal: false");
    }

    [Fact]
    public void TheClaimTable_IsAlsoAMigration()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Scripts"))) dir = dir.Parent;
        string src = File.ReadAllText(Path.Combine(dir!.FullName, "Scripts", "Systems", "SqlSaveBackend.cs"));
        int migrations = src.IndexOf("MigrateWorldBossTables(connection);", StringComparison.Ordinal);
        src.LastIndexOf("CREATE TABLE IF NOT EXISTS bounty_claims", migrations, StringComparison.Ordinal)
            .Should().BeGreaterThan(src.IndexOf("ALTER TABLE player_teams ADD COLUMN last_join_at", StringComparison.Ordinal));
    }

    // ─── v1.1.11: review round 13 ───

    [Fact]
    public void ALegacyQuestWithNoId_GetsTheSameClaimKey_InEveryProcess()
    {
        Quest Legacy(string target) => new Quest { Id = "", Title = "WANTED: " + target, Initiator = "The Crown", TargetNPCName = target, Date = DateTime.Now };
        var a = Legacy("Grim");
        System.Threading.Thread.Sleep(5);
        var b = Legacy("Grim");   // another process's copy, built on its own (its load time differs)
        QuestSystem.BountyClaimKey(a).Should().Be(QuestSystem.BountyClaimKey(b));
        QuestSystem.BountyClaimKey(a).Should().StartWith("legacy:").And.HaveLength("legacy:".Length + 64, "a SHA-256 in hex");
        QuestSystem.BountyClaimKey(a).Should().NotBe(QuestSystem.BountyClaimKey(Legacy("Other")));
        QuestSystem.BountyClaimKey(new Quest { Id = "Q42" }).Should().Be("Q42");
    }

    [Fact]
    public async Task ALegacyBountyWithNoId_IsPaidOnce_AcrossProcesses()
    {
        await WithSqlBackend(async (db, _) =>
        {
            var bounty = BountyOnPlayer("Idless Rogue", 3000);
            bounty.Id = "";
            var rogue = new Character { Name1 = "idless_rogue", Name2 = "Idless Rogue", Level = 30, IsLoadedPlayer = true };
            var winner = new Character { Name1 = "sheriff_c", Name2 = "Sheriff C", Level = 30, Gold = 0 };
            QuestSystem.CollectBountiesOnPlayer(winner, rogue).Should().ContainSingle();

            var copy = BountyOnPlayer("Idless Rogue", 3000);   // the other process's copy, also with no id
            copy.Id = "";
            var other = new Character { Name1 = "sheriff_d", Name2 = "Sheriff D", Level = 30, Gold = 0 };
            QuestSystem.CollectBountiesOnPlayer(other, rogue).Should().BeEmpty("the legacy key was claimed already");
            other.Gold.Should().Be(0);
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void BountyClaims_AreNeverPruned()
    {
        // v1.1.11: a stale save can bring an old bounty back after any retention, so the claims stay
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Scripts"))) dir = dir.Parent;
        string Src(string folder, string file) => File.ReadAllText(Path.Combine(dir!.FullName, "Scripts", folder, file));
        Src("Systems", "SqlSaveBackend.cs").Should().NotContain("PruneOldBountyClaims").And.NotContain("DELETE FROM bounty_claims");
        Src("Systems", "WorldSimService.cs").Should().NotContain("PruneOldBountyClaims");
        Src("Core", "GameConfig.cs").Should().NotContain("BountyClaimRetentionDays");
    }

    [Fact]
    public void AMarriedPlayer_LoadsWithTheSameDisplayName_AndTheirBountyIsPaid()
    {
        var bob = new Character { Name1 = "bob_acct", Name2 = "Bob", FamilySurname = "Smith", Level = 20, HP = 100, MaxHP = 100 };
        var serialize = typeof(SaveSystem).GetMethod("SerializePlayer", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var data = (PlayerData)serialize.Invoke(SaveSystem.Instance, new object[] { bob })!;
        var back = System.Text.Json.JsonSerializer.Deserialize<PlayerData>(System.Text.Json.JsonSerializer.Serialize(data))!;
        var loaded = PlayerCharacterLoader.CreateFromSaveData(back, "Bob");
        loaded.DisplayName.Should().Be(bob.DisplayName).And.Be("Bob Smith");

        var bounty = BountyOnPlayer("Bob Smith", 2500);
        try
        {
            var winner = new Character { Name1 = "sheriff_e", Name2 = "Sheriff E", Level = 30, Gold = 0 };
            QuestSystem.CollectBountiesOnPlayer(winner, loaded).Should().ContainSingle("the loaded defender carries the married name");
        }
        finally { bounty.Deleted = true; }
    }

    [Fact]
    public async Task BeatingMarriedBob_DoesNotCollectTheBountyOnALivingBobSmith()
    {
        // v1.1.11: "Bob Smith" is also another player's Name2, so the bounty names them, not the married Bob
        await WithSqlBackend(async (db, path) =>
        {
            void Row(string user, string display, string name2)
            {
                using var conn = new SqliteConnection($"Data Source={path}");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO players (username, display_name, player_data) VALUES (@u, @d, @j);";
                cmd.Parameters.AddWithValue("@u", user);
                cmd.Parameters.AddWithValue("@d", display);
                cmd.Parameters.AddWithValue("@j", "{\"player\":{\"name2\":\"" + name2 + "\"}}");
                cmd.ExecuteNonQuery();
            }
            Row("bob_ns", "Bob NS Smith", "Bob NS");   // the married Bob's own row
            var bob = new Character { Name1 = "bob_ns", Name2 = "Bob NS", FamilySurname = "Smith", Level = 20, IsLoadedPlayer = true };
            var winner = new Character { Name1 = "sheriff_ns", Name2 = "Sheriff NS", Level = 30, Gold = 0 };
            var bounty = BountyOnPlayer("Bob NS Smith", 3000);
            try
            {
                Row("bsmith_ns", "Robert NS", "Bob NS Smith");
                QuestSystem.CollectBountiesOnPlayer(winner, bob).Should().BeEmpty("the bounty names the living Bob NS Smith");
                bounty.Deleted.Should().BeFalse();
                winner.Gold.Should().Be(0);

                db.IsNameUsedByAnotherCharacter("Bob NS Smith", "Bob NS").Should().BeTrue();
                db.IsNameUsedByAnotherCharacter("Bob NS", "Bob NS").Should().BeFalse("the married Bob's own row is not another character");
                QuestSystem.CollectBountiesOnPlayer(winner, new Character { Name1 = "bob_ns", Name2 = "Bob NS Smith", Level = 20, IsLoadedPlayer = true })
                    .Should().ContainSingle("beating the living Bob NS Smith, whose Name2 it is, collects it");
            }
            finally { bounty.Deleted = true; }
        });
    }

    // ─── v1.1.11: review round 14 ───

    [Fact]
    public async Task CollectingOneIdlessBounty_RemovesOnlyThatOne_FromTheSharedRecord()
    {
        await WithSqlBackend(async (db, _) =>
        {
            var osm = (OnlineStateManager)Activator.CreateInstance(typeof(OnlineStateManager),
                BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { db, "r14" }, null)!;
            QuestData Stored(string target) => new QuestData
            {
                Id = "", Title = "WANTED: " + target, Initiator = "The Crown", TargetNPCName = target, IsPlayerBounty = true
            };
            await osm.SaveSharedQuests(new System.Collections.Generic.List<QuestData>
            {
                Stored("Idless Bob R14"), Stored("Idless Alice R14"), new QuestData { Id = "", Title = "Fetch the ale", Initiator = "Innkeeper" }
            });

            var bounty = BountyOnPlayer("Idless Bob R14", 2000);
            bounty.Id = "";
            var bob = new Character { Name1 = "idless_bob_r14", Name2 = "Idless Bob R14", Level = 30, IsLoadedPlayer = true };
            var winner = new Character { Name1 = "sheriff_r14", Name2 = "Sheriff R14", Level = 30, Gold = 0 };
            var paid = QuestSystem.CollectBountiesOnPlayer(winner, bob);
            paid.Should().ContainSingle();

            // the same match the duel path uses (CombatEngine.ReportDuelDefeat)
            var keys = new System.Collections.Generic.HashSet<string>();
            foreach (var q in paid) keys.Add(QuestSystem.BountyClaimKey(q));
            (await osm.RemoveSharedQuestsAsync(q => keys.Contains(QuestSystem.BountyClaimKey(q)))).Should().Be(1);

            var left = (await osm.LoadSharedQuests())!;
            left.Should().HaveCount(2);
            left.Should().NotContain(q => q.TargetNPCName == "Idless Bob R14");
            left.Should().Contain(q => q.TargetNPCName == "Idless Alice R14", "another id-less bounty stays");
            left.Should().Contain(q => q.Title == "Fetch the ale");
        });

        QuestSystem.BountyClaimKey(new QuestData { Id = "", Initiator = "The Crown", TargetNPCName = "X", Title = "WANTED: X" })
            .Should().Be(QuestSystem.BountyClaimKey(new Quest { Id = "", Initiator = "The Crown", TargetNPCName = "X", Title = "WANTED: X" }));
        QuestSystem.BountyClaimKey(new QuestData { Id = "Q7" }).Should().Be("Q7");

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Scripts"))) dir = dir.Parent;
        File.ReadAllText(Path.Combine(dir!.FullName, "Scripts", "Systems", "CombatEngine.cs"))
            .Should().Contain("RemoveSharedQuestsAsync(q => keys.Contains(QuestSystem.BountyClaimKey(q)))")
            .And.NotContain("RemoveSharedQuestsAsync(q => ids.Contains(q.Id))");
    }

    // ─── v1.1.11: a player bounty's gold survives a save and reload ───

    /// <summary>Runs the body, then puts the shared quest list back as it was (the readers replace parts of it).</summary>
    private static void KeepingTheQuestList(Action body)
    {
        var before = QuestSystem.GetAllQuests(includeCompleted: true);
        try { body(); }
        finally
        {
            QuestSystem.RestoreFromSaveData(new System.Collections.Generic.List<QuestData>());
            foreach (var q in before) QuestSystem.AddQuestToDatabase(q);
        }
    }

    private static Quest ByTarget(string target) =>
        System.Linq.Enumerable.Single(QuestSystem.GetAllQuests(includeCompleted: true), q => q.TargetNPCName == target && !q.Deleted);

    private static System.Collections.Generic.List<QuestData> ThroughJson(System.Collections.Generic.List<QuestData> list) =>
        System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<QuestData>>(System.Text.Json.JsonSerializer.Serialize(list))!;

    [Fact]
    public async Task APlayerBountysGold_SurvivesBothWriters_AndAllThreeReaders()
    {
        await WithSqlBackend(async (db, path) =>
        {
            var osm = (OnlineStateManager)Activator.CreateInstance(typeof(OnlineStateManager), F, null, new object[] { db, "r15" }, null)!;
            KeepingTheQuestList(() =>
            {
                var posted = BountyOnPlayer("Gold Rogue", 100_000);
                posted.Occupier = "Gold Hunter";

                var saveWriter = typeof(SaveSystem).GetMethod("SerializeQuestList", F)!;
                var fromSave = ThroughJson((System.Collections.Generic.List<QuestData>)saveWriter.Invoke(SaveSystem.Instance, new object[] { new System.Collections.Generic.List<Quest> { posted } })!);
                var fromOnline = ThroughJson((System.Collections.Generic.List<QuestData>)typeof(OnlineStateManager).GetMethod("SerializeCurrentQuests", F)!.Invoke(osm, null)!);
                fromSave.Should().ContainSingle().Which.BountyGold.Should().Be(100_000);
                System.Linq.Enumerable.Single(fromOnline, d => d.Id == posted.Id).BountyGold.Should().Be(100_000);

                QuestSystem.RestoreFromSaveData(fromSave);
                ByTarget("Gold Rogue").BountyGold.Should().Be(100_000, "RestoreFromSaveData");
                QuestSystem.MergePlayerQuests("Gold Hunter", fromSave);
                ByTarget("Gold Rogue").BountyGold.Should().Be(100_000, "MergePlayerQuests");
                QuestSystem.RestoreFromSaveData(new System.Collections.Generic.List<QuestData>());
                fromSave[0].Occupier = "";
                QuestSystem.MergeWorldQuests(fromSave);
                ByTarget("Gold Rogue").BountyGold.Should().Be(100_000, "MergeWorldQuests");

                // the reloaded bounty pays what was posted, not the 500 fallback
                var winner = new Character { Name1 = "gold_sheriff", Name2 = "Gold Sheriff", Level = 30, Gold = 0 };
                var rogue = new Character { Name1 = "gold_rogue", Name2 = "Gold Rogue", Level = 30, IsLoadedPlayer = true };
                QuestSystem.CollectBountiesOnPlayer(winner, rogue).Should().ContainSingle();
                winner.Gold.Should().Be(100_000);
            });
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void ALegacyPlayerBounty_WithNoStoredGold_PaysThe500Fallback()
    {
        KeepingTheQuestList(() =>
        {
            // a row written before BountyGold was stored
            var legacy = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<QuestData>>(
                "[{\"Id\":\"legacy_bounty\",\"Initiator\":\"The Crown\",\"TargetNPCName\":\"Legacy Rogue\",\"IsPlayerBounty\":true,\"QuestTarget\":" +
                (int)QuestTarget.DefeatNPC + ",\"DaysToComplete\":30,\"StartTime\":\"" + DateTime.Now.ToString("o") + "\"}]")!;
            legacy[0].BountyGold.Should().Be(0);
            QuestSystem.RestoreFromSaveData(legacy);
            var winner = new Character { Name1 = "legacy_sheriff", Name2 = "Legacy Sheriff", Level = 30, Gold = 0 };
            var rogue = new Character { Name1 = "legacy_rogue", Name2 = "Legacy Rogue", Level = 30, IsLoadedPlayer = true };
            QuestSystem.CollectBountiesOnPlayer(winner, rogue).Should().ContainSingle();
            winner.Gold.Should().Be(500);
        });
    }
}
