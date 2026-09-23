using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
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
        QuestSystem.RecordNPCDefeat(hunter, target);
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
        src.Substring(start, end - start).Should().Contain("QuestSystem.RecordNPCDefeat(currentPlayer, npc)");
    }
}
