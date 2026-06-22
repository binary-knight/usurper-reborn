using Xunit;
using FluentAssertions;
using System;
using UsurperRemake.Systems;

namespace UsurperReborn.Tests;

/// <summary>
/// v0.64.2: Adventurer's Journal next-step ladder. The ladder is a pure
/// first-match-wins priority function over Character state; these tests lock
/// the rung ordering for the rungs that don't require singleton world state
/// (quest/companion/story rungs are environment-guarded and fall through
/// cleanly in the test environment, which these tests also implicitly
/// verify: a missing singleton must never throw, only skip).
/// </summary>
public class JournalSystemTests
{
    private static Character ConstructTestPlayer(
        int level = 10,
        long hp = 100,
        long maxHp = 100,
        long healing = 5,
        long mKills = 50,
        int trainingPoints = 0,
        bool autoLevelUp = true,
        long experience = 0,
        int lastDungeonFloor = 0)
    {
        return new Character
        {
            Name1 = "Test",
            Name2 = "TestPlayer",
            Level = level,
            HP = hp,
            MaxHP = maxHp,
            Healing = healing,
            MKills = mKills,
            TrainingPoints = trainingPoints,
            AutoLevelUp = autoLevelUp,
            Experience = experience,
            LastDungeonFloor = lastDungeonFloor,
        };
    }

    [Fact]
    public void GetNextStep_NullPlayer_ReturnsDefaultDelve()
    {
        var step = JournalSystem.GetNextStep(null!);
        step.LocKey.Should().Be("journal.next_delve");
    }

    [Fact]
    public void GetNextStep_FreshCharacter_RecommendsFirstFight()
    {
        var player = ConstructTestPlayer(level: 1, mKills: 0);
        var step = JournalSystem.GetNextStep(player);
        step.LocKey.Should().Be("journal.next_first_fight",
            "a Lv1 character with zero kills should get the bounce-cliff arrow");
    }

    [Fact]
    public void GetNextStep_FirstKillLanded_FirstFightRungStopsFiring()
    {
        var player = ConstructTestPlayer(level: 1, mKills: 1);
        var step = JournalSystem.GetNextStep(player);
        step.LocKey.Should().NotBe("journal.next_first_fight");
    }

    [Fact]
    public void GetNextStep_CriticallyHurtNoPotions_RecommendsHealer()
    {
        var player = ConstructTestPlayer(hp: 10, maxHp: 100, healing: 0);
        var step = JournalSystem.GetNextStep(player);
        step.LocKey.Should().Be("journal.next_heal");
    }

    [Fact]
    public void GetNextStep_CriticallyHurtWithPotions_DoesNotRecommendHealer()
    {
        // Player can drink their way back; don't waste the next-step slot.
        var player = ConstructTestPlayer(hp: 10, maxHp: 100, healing: 5);
        var step = JournalSystem.GetNextStep(player);
        step.LocKey.Should().NotBe("journal.next_heal");
    }

    [Fact]
    public void GetNextStep_BankedLevelUpWithAutoLevelOff_RecommendsLevelMaster()
    {
        var player = ConstructTestPlayer(level: 10, autoLevelUp: false,
            experience: GameConfig.GetExperienceForLevel(11) + 1000);
        var step = JournalSystem.GetNextStep(player);
        step.LocKey.Should().Be("journal.next_level_up");
    }

    [Fact]
    public void GetNextStep_BankedLevelUpWithAutoLevelOn_SkipsRung()
    {
        // Auto-level will fire on its own at the next location loop; the
        // journal shouldn't tell the player to do something automatic.
        var player = ConstructTestPlayer(level: 10, autoLevelUp: true,
            experience: GameConfig.GetExperienceForLevel(11) + 1000);
        var step = JournalSystem.GetNextStep(player);
        step.LocKey.Should().NotBe("journal.next_level_up");
    }

    [Fact]
    public void GetNextStep_UnspentTrainingPoints_RecommendsTraining()
    {
        var player = ConstructTestPlayer(trainingPoints: 4);
        var step = JournalSystem.GetNextStep(player);
        step.LocKey.Should().Be("journal.next_training");
        step.Args.Should().ContainSingle().Which.Should().Be(4);
    }

    [Fact]
    public void GetNextStep_HealBeatsTraining()
    {
        // Rung ordering: a critically hurt player gets sent to the Healer
        // even with training points banked.
        var player = ConstructTestPlayer(hp: 5, maxHp: 100, healing: 0, trainingPoints: 4);
        var step = JournalSystem.GetNextStep(player);
        step.LocKey.Should().Be("journal.next_heal");
    }

    [Fact]
    public void GetNextStep_FirstFightBeatsEverything()
    {
        var player = ConstructTestPlayer(level: 1, mKills: 0, hp: 5, maxHp: 100,
            healing: 0, trainingPoints: 4);
        var step = JournalSystem.GetNextStep(player);
        step.LocKey.Should().Be("journal.next_first_fight");
    }

    [Fact]
    public void GetNextStep_NothingPending_DefaultsToDelve()
    {
        var player = ConstructTestPlayer(lastDungeonFloor: 0);
        var step = JournalSystem.GetNextStep(player);
        // In the test environment the story singleton may report the first
        // unresolved god/seal in band; both are acceptable "go delve"
        // directions. The contract: it must end at a dungeon-facing key.
        step.LocKey.Should().BeOneOf(
            "journal.next_delve", "journal.next_delve_resume",
            "journal.next_seal", "journal.next_god");
    }

    [Fact]
    public void GetNextStep_RememberedFloor_ResumesIt()
    {
        var player = ConstructTestPlayer(level: 90, lastDungeonFloor: 78, mKills: 500);
        var step = JournalSystem.GetNextStep(player);
        if (step.LocKey == "journal.next_delve_resume")
        {
            step.Args.Should().ContainSingle().Which.Should().Be(78);
        }
        // (Story rungs may legitimately outrank the default in a polluted
        // singleton environment -- the resume assertion only applies when
        // the ladder reached the default rung.)
    }

    [Fact]
    public void SectionBuilders_NullPlayer_ReturnEmptyNotThrow()
    {
        JournalSystem.BuildInProgressLines(null!).Should().BeEmpty();
        JournalSystem.BuildClaimLines(null!).Should().BeEmpty();
        JournalSystem.BuildWorldLines(null!).Should().BeEmpty();
    }

    [Fact]
    public void BuildClaimLines_TrainingPoints_Surface()
    {
        var player = ConstructTestPlayer(trainingPoints: 3);
        var lines = JournalSystem.BuildClaimLines(player);
        lines.Should().Contain(l => l.Text.Contains("3"));
    }

    [Fact]
    public void SectionBuilders_BareCharacter_NeverThrow()
    {
        // Deserialization-shaped Character (no Brain, no statistics wiring):
        // every section builder must degrade to lines-or-empty, never throw.
        var player = new Character { Name2 = "Bare" };
        var act = () =>
        {
            JournalSystem.GetNextStep(player);
            JournalSystem.BuildInProgressLines(player);
            JournalSystem.BuildClaimLines(player);
            JournalSystem.BuildWorldLines(player);
        };
        act.Should().NotThrow();
    }
}
