using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.12: the awakening points model, its saved insights, the wired moments, the announcement,
/// the Ocean Journal and the stage boosts.
/// </summary>
[Collection("SharedGameSingletons")]
public class Awakening1112Tests : IDisposable
{
    private static OceanPhilosophySystem Ocean => OceanPhilosophySystem.Instance;

    public Awakening1112Tests() => Ocean.Reset();
    public void Dispose() => Ocean.Reset();

    // ---------- 1. points model ----------

    [Fact]
    public void Points_AreThreePerMoment_TwoPerFragment_OnePerDistinctInsight()
    {
        Ocean.ExperienceMoment(AwakeningMoment.SparedAnEnemy);
        Ocean.CollectFragment(WaveFragment.Origin);
        Ocean.GainInsight("dream:a");
        Ocean.GainInsight("dream:a");
        Ocean.GainInsight("dream:b");
        Ocean.Points.Should().Be(3 + 2 + 2, "the same insight counts once");
    }

    [Fact]
    public void Stages_FollowTheThresholds()
    {
        Ocean.GainInsight("i1"); Ocean.GainInsight("i2"); Ocean.GainInsight("i3");
        Ocean.AwakeningLevel.Should().Be(0);
        Ocean.GainInsight("i4");
        Ocean.AwakeningLevel.Should().Be(1, "4 points is stage 1");
        for (int i = 5; i <= 12; i++) Ocean.GainInsight("i" + i);
        Ocean.AwakeningLevel.Should().Be(2, "12 points is stage 2");
        for (int i = 13; i <= 62; i++) Ocean.GainInsight("i" + i);
        Ocean.AwakeningLevel.Should().Be(6, "stage 7 needs more than points");
    }

    [Fact]
    public void Stage7_NeedsTheStage6Points_AndAllSeals()
    {
        Ocean.ExperienceMoment(AwakeningMoment.AllSealsCollected);
        Ocean.AwakeningLevel.Should().BeLessThan(7, "the seals alone are not enough");
        for (int i = 0; Ocean.Points < OceanPhilosophySystem.StageThresholds[6]; i++) Ocean.GainInsight("i" + i);
        Ocean.AwakeningLevel.Should().Be(7);
    }

    [Fact]
    public void TrueIdentityRevealed_StillGrants7AtOnce()
    {
        Ocean.ExperienceMoment(AwakeningMoment.TrueIdentityRevealed);
        Ocean.AwakeningLevel.Should().Be(7);
    }

    [Fact]
    public void Restore_NeverLowersTheSavedLevel_AndAnnouncesNothing()
    {
        // an old save: level 4 from the old formula (4 fragments + 2 moments), 14 points now
        Ocean.RestoreFromSave(
            new[] { WaveFragment.Origin, WaveFragment.FirstSeparation, WaveFragment.TheForgetting, WaveFragment.TheCycle },
            new[] { AwakeningMoment.FirstCompanionDeath, AwakeningMoment.SacrificedForAnother },
            null, savedLevel: 4);
        Ocean.AwakeningLevel.Should().Be(4);
        Ocean.PendingAnnouncementStage.Should().Be(0, "a restore replays stages already announced");
        Ocean.TakePendingAnnouncement().Should().BeNull();
    }

    [Fact]
    public void Restore_RaisesAboveTheSavedLevel_WhenThePointsReachHigher()
    {
        var insights = Enumerable.Range(0, 30).Select(i => "i" + i).ToList();
        Ocean.RestoreFromSave(Array.Empty<WaveFragment>(), Array.Empty<AwakeningMoment>(), insights, savedLevel: 1);
        Ocean.AwakeningLevel.Should().Be(3, "30 points is stage 3");
        Ocean.InsightIds.Should().HaveCount(30);
    }

    [Fact]
    public void Insights_SurviveTheSave()
    {
        Ocean.GainInsight("dream:dream_mirror");
        Ocean.GainInsight("feature:wave_self");
        Ocean.CollectFragment(WaveFragment.Origin);
        var data = SaveSystem.Instance.SerializeStorySystemsPublic();
        data.OceanInsightIds.Should().BeEquivalentTo(new[] { "dream:dream_mirror", "feature:wave_self" });

        var json = JsonSerializer.Serialize(data);
        var back = JsonSerializer.Deserialize<StorySystemsData>(json)!;
        Ocean.Reset();
        SaveSystem.Instance.RestoreStorySystems(back);
        Ocean.InsightIds.Should().BeEquivalentTo(new[] { "dream:dream_mirror", "feature:wave_self" });
        Ocean.Points.Should().Be(4);
        Ocean.AwakeningLevel.Should().Be(1);
    }

    [Fact]
    public void AnOldSave_WithoutInsightIds_Loads()
    {
        var back = JsonSerializer.Deserialize<StorySystemsData>("{\"AwakeningLevel\":2,\"CollectedFragments\":[0],\"ExperiencedMoments\":[0]}")!;
        SaveSystem.Instance.RestoreStorySystems(back);
        Ocean.AwakeningLevel.Should().Be(2);
        Ocean.InsightIds.Should().BeEmpty();
    }

    [Fact]
    public void Reset_ClearsInsights()
    {
        Ocean.GainInsight("x");
        Ocean.Reset();
        Ocean.InsightIds.Should().BeEmpty();
        Ocean.Points.Should().Be(0);
    }
}
