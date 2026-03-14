using Xunit;
using FluentAssertions;

namespace UsurperReborn.Tests;

/// <summary>
/// Tests for QuestSystem, focusing on expedition quest floor targeting (v0.52.5)
/// </summary>
public class QuestSystemTests
{
    [Fact]
    public void ExpeditionQuest_TargetsFloorBeyondDeepest()
    {
        // Player at level 20 who has cleared floor 15
        for (int i = 0; i < 20; i++)
        {
            var quest = QuestSystem.CreateDungeonQuest(
                QuestTarget.ReachFloor, 1, "The Dungeon", playerLevel: 20, deepestFloor: 15);

            var floorObjective = quest.Objectives.FirstOrDefault(o =>
                o.ObjectiveType == QuestObjectiveType.ReachDungeonFloor);

            floorObjective.Should().NotBeNull("Expedition quest should have a floor objective");
            floorObjective!.RequiredProgress.Should().BeGreaterThan(15,
                "Target floor should exceed player's deepest cleared floor (15)");
        }
    }

    [Fact]
    public void ExpeditionQuest_HasMandatoryKillObjective()
    {
        var quest = QuestSystem.CreateDungeonQuest(
            QuestTarget.ReachFloor, 1, "The Dungeon", playerLevel: 20, deepestFloor: 10);

        var killObjective = quest.Objectives.FirstOrDefault(o =>
            o.ObjectiveType == QuestObjectiveType.KillMonsters);

        killObjective.Should().NotBeNull("Expedition quest should have a mandatory kill objective");
        killObjective!.IsOptional.Should().BeFalse("Kill objective should not be optional");
        killObjective.RequiredProgress.Should().BeGreaterThanOrEqualTo(3,
            "Should require at least 3 monster kills");
    }

    [Fact]
    public void ExpeditionQuest_KillCount_ScalesWithDifficulty()
    {
        var easyQuest = QuestSystem.CreateDungeonQuest(
            QuestTarget.ReachFloor, 1, "The Dungeon", playerLevel: 30, deepestFloor: 0);
        var hardQuest = QuestSystem.CreateDungeonQuest(
            QuestTarget.ReachFloor, 4, "The Dungeon", playerLevel: 30, deepestFloor: 0);

        var easyKills = easyQuest.Objectives.First(o => o.ObjectiveType == QuestObjectiveType.KillMonsters);
        var hardKills = hardQuest.Objectives.First(o => o.ObjectiveType == QuestObjectiveType.KillMonsters);

        hardKills.RequiredProgress.Should().BeGreaterThan(easyKills.RequiredProgress,
            "Higher difficulty should require more kills");
    }

    [Fact]
    public void ExpeditionQuest_FloorCappedToAccessibleRange()
    {
        // Level 10 player — accessible range is 1-20
        for (int i = 0; i < 20; i++)
        {
            var quest = QuestSystem.CreateDungeonQuest(
                QuestTarget.ReachFloor, 4, "The Dungeon", playerLevel: 10, deepestFloor: 0);

            var floorObjective = quest.Objectives.First(o =>
                o.ObjectiveType == QuestObjectiveType.ReachDungeonFloor);

            floorObjective.RequiredProgress.Should().BeLessThanOrEqualTo(20,
                "Target floor should not exceed player level + 10");
            floorObjective.RequiredProgress.Should().BeGreaterThanOrEqualTo(1,
                "Target floor should not be less than 1");
        }
    }

    [Fact]
    public void ExpeditionQuest_AtMaxAccessible_StillCreatesQuest()
    {
        // Player at level 10 who has already cleared floor 20 (their max)
        var quest = QuestSystem.CreateDungeonQuest(
            QuestTarget.ReachFloor, 1, "The Dungeon", playerLevel: 10, deepestFloor: 20);

        quest.Should().NotBeNull("Should still create a quest even when at max accessible floor");
        quest.Objectives.Should().HaveCountGreaterThanOrEqualTo(2,
            "Should have both floor and kill objectives");
    }

    [Fact]
    public void CreateDungeonQuest_InvalidTarget_ThrowsException()
    {
        var act = () => QuestSystem.CreateDungeonQuest(
            QuestTarget.Monster, 1, "The Dungeon", playerLevel: 10);

        act.Should().Throw<ArgumentException>("Monster is not a valid dungeon quest target");
    }

    [Fact]
    public void ClearBossQuest_HasKillBossObjective()
    {
        var quest = QuestSystem.CreateDungeonQuest(
            QuestTarget.ClearBoss, 2, "The Dungeon", playerLevel: 25);

        var bossObjective = quest.Objectives.FirstOrDefault(o =>
            o.ObjectiveType == QuestObjectiveType.KillBoss);

        bossObjective.Should().NotBeNull("Clear Boss quest should have a KillBoss objective");
        quest.Title.Should().StartWith("Slay the ");
    }

    [Fact]
    public void SurviveDungeonQuest_HasKillRequirement()
    {
        var quest = QuestSystem.CreateDungeonQuest(
            QuestTarget.SurviveDungeon, 2, "The Dungeon", playerLevel: 25);

        var killObjective = quest.Objectives.FirstOrDefault(o =>
            o.ObjectiveType == QuestObjectiveType.KillMonsters);

        killObjective.Should().NotBeNull("Survive dungeon quest should require killing monsters");
        killObjective!.RequiredProgress.Should().BeGreaterThanOrEqualTo(10);
    }
}
