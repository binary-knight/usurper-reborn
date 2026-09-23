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
    public void TheClaimTable_IsAlsoAMigration()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Scripts"))) dir = dir.Parent;
        string src = File.ReadAllText(Path.Combine(dir!.FullName, "Scripts", "Systems", "SqlSaveBackend.cs"));
        int migrations = src.IndexOf("MigrateWorldBossTables(connection);", StringComparison.Ordinal);
        src.LastIndexOf("CREATE TABLE IF NOT EXISTS bounty_claims", migrations, StringComparison.Ordinal)
            .Should().BeGreaterThan(src.IndexOf("ALTER TABLE player_teams ADD COLUMN last_join_at", StringComparison.Ordinal));
    }
}
