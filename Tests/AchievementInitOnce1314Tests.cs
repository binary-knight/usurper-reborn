using System;
using System.Collections.Generic;
using System.Reflection;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.14: login stability. AchievementSystem.Initialize marks the table loaded only once every achievement is
/// in, so a load that stops part way is loaded again by the next call instead of being left short.
/// </summary>
[Collection("SharedGameSingletons")]
public class AchievementInitOnce1314Tests
{
    private static readonly FieldInfo InitializedField =
        typeof(AchievementSystem).GetField("_initialized", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly FieldInfo TableField =
        typeof(AchievementSystem).GetField("_achievements", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly PropertyInfo LoadedProperty =
        typeof(GameDataLoader).GetProperty(nameof(GameDataLoader.Achievements), BindingFlags.Public | BindingFlags.Static)!;

    [Fact]
    public void ALoadThatStopsPartWay_IsLoadedAgainByTheNextCall()
    {
        var loaded = GameDataLoader.Achievements;
        int full = (loaded ?? AchievementSystem.GetBuiltInAchievements()).Count;
        try
        {
            InitializedField.SetValue(null, false);
            ((Dictionary<string, Achievement>)TableField.GetValue(null)!).Clear();
            LoadedProperty.SetValue(null, new List<Achievement> { new Achievement { Id = "only_one" }, new Achievement { Id = null! } });

            FluentActions.Invoking(AchievementSystem.Initialize).Should().Throw<ArgumentNullException>();

            LoadedProperty.SetValue(null, loaded);
            ((Dictionary<string, Achievement>)TableField.GetValue(null)!).Clear();
            AchievementSystem.Initialize();

            AchievementSystem.TotalAchievements.Should().Be(full, "the stopped load was not marked done");
        }
        finally
        {
            LoadedProperty.SetValue(null, loaded);
            if (AchievementSystem.TotalAchievements != full)
            {
                InitializedField.SetValue(null, false);
                AchievementSystem.Initialize();
            }
        }
    }

    [Fact]
    public void Initialize_CalledAgain_KeepsTheSameTable()
    {
        AchievementSystem.Initialize();
        int count = AchievementSystem.TotalAchievements;

        AchievementSystem.Initialize();

        count.Should().BeGreaterThan(0);
        AchievementSystem.TotalAchievements.Should().Be(count);
    }
}
