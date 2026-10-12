using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

/// <summary>
/// 1.3.1 piece C1: the ending count reads "terravok_saved", the flag that saving Terravok sets. The three
/// readers are DetermineEnding (Savior at three saved gods), the enhanced True Ending check (two spared gods)
/// and CycleSystem.QualifiesForTrueEnding (two saved gods). State is built in memory only; no save folder is
/// read or written.
/// </summary>
[Collection("SharedGameSingletons")]
public class EndingsTerravok131Tests : IDisposable
{
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;

    private static readonly Type[] Swapped =
    {
        typeof(StoryProgressionSystem), typeof(OceanPhilosophySystem), typeof(GriefSystem), typeof(CycleSystem),
    };
    private readonly List<(FieldInfo Field, object? Old)> _old;

    public EndingsTerravok131Tests()
    {
        _old = Swapped.Select(t => t.GetField("_fallbackInstance", SNP)!).Select(f => (f, f.GetValue(null))).ToList();
        foreach (var t in Swapped)
            t.GetField("_fallbackInstance", SNP)!.SetValue(null, Activator.CreateInstance(t));
    }

    public void Dispose()
    {
        foreach (var (f, v) in _old) f.SetValue(null, v);
    }

    private static Character Hero() => new Character { Name2 = "EndingsHero", Chivalry = 0, Darkness = 0 };

    private static void Flags(params string[] flags)
    {
        foreach (var f in flags) StoryProgressionSystem.Instance.SetStoryFlag(f, true);
    }

    private static void Seals()
    {
        foreach (var s in Enum.GetValues<SealType>()) StoryProgressionSystem.Instance.CollectSeal(s);
    }

    private static void FullAwakening()
    {
        OceanPhilosophySystem.Instance.RestoreFromSave(
            Array.Empty<WaveFragment>(), new[] { AwakeningMoment.FirstCompanionDeath }, null, 7);
    }

    // Site 1: the saved count of DetermineEnding. Cycle 1, no seals, awakening 0: the earlier exits are not met.
    private static EndingType SaviorProbe(params string[] flags)
    {
        Flags(flags);
        return EndingsSystem.Instance.DetermineEnding(Hero());
    }

    // Site 2: the spared count of the enhanced check. Every other condition is met.
    private static EndingType EnhancedProbe(params string[] flags)
    {
        Seals();
        FullAwakening();
        Flags(flags);
        return EndingsSystem.Instance.DetermineEnding(Hero());
    }

    // Site 3: the saved count of the legacy check. Awakening stays 0 so the enhanced check is false.
    private static bool LegacyProbe(params string[] flags)
    {
        StoryProgressionSystem.Instance.CurrentCycle = 3;
        Seals();
        Flags("noctura_ally");
        Flags(flags);
        OceanPhilosophySystem.Instance.AwakeningLevel.Should().Be(0);
        return CycleSystem.Instance.QualifiesForTrueEnding(Hero());
    }

    [Fact]
    public void DetermineEnding_CountsTerravokSaved_AsThirdSavedGod()
    {
        SaviorProbe("veloura_saved", "aurelion_saved", "terravok_saved").Should().Be(EndingType.Savior);
    }

    [Fact]
    public void DetermineEnding_WithoutTerravokSaved_IsDefiant()
    {
        SaviorProbe("veloura_saved", "aurelion_saved").Should().Be(EndingType.Defiant);
    }

    [Fact]
    public void EnhancedTrueEnding_CountsTerravokSaved_AsSparedGod()
    {
        EnhancedProbe("terravok_saved", "veloura_saved").Should().Be(EndingType.TrueEnding);
    }

    [Fact]
    public void EnhancedTrueEnding_WithoutTerravokSaved_IsNotTrueEnding()
    {
        EnhancedProbe("veloura_saved").Should().NotBe(EndingType.TrueEnding);
    }

    [Fact]
    public void LegacyTrueEnding_CountsTerravokSaved_AsSavedGod()
    {
        LegacyProbe("terravok_saved", "veloura_saved").Should().BeTrue();
        LegacyProbe("terravok_saved", "aurelion_saved").Should().BeTrue();
    }

    [Fact]
    public void LegacyTrueEnding_WithoutTerravokSaved_IsFalse()
    {
        LegacyProbe("veloura_saved").Should().BeFalse();
    }

    [Fact]
    public void OnlyTheOldAwakenedFlag_CountsAtNoSite()
    {
        SaviorProbe("veloura_saved", "aurelion_saved", "terravok_awakened").Should().Be(EndingType.Defiant);
    }

    [Fact]
    public void OnlyTheOldAwakenedFlag_CountsAtNoEnhancedSite()
    {
        EnhancedProbe("veloura_saved", "terravok_awakened").Should().NotBe(EndingType.TrueEnding);
    }

    [Fact]
    public void OnlyTheOldAwakenedFlag_CountsAtNoLegacySite()
    {
        LegacyProbe("veloura_saved", "terravok_awakened").Should().BeFalse();
    }

    [Fact]
    public void StatusAwakenedWithNoFlag_ThePathOfTheParadoxChoice_CountsAtNoSite()
    {
        StoryProgressionSystem.Instance.UpdateGodState(OldGodType.Terravok, GodStatus.Awakened);
        StoryProgressionSystem.Instance.OldGodStates[OldGodType.Terravok].Status.Should().Be(GodStatus.Awakened);
        SaviorProbe("veloura_saved", "aurelion_saved").Should().Be(EndingType.Defiant);
    }

    [Fact]
    public void StatusAwakenedWithNoFlag_CountsAtNoEnhancedSite()
    {
        StoryProgressionSystem.Instance.UpdateGodState(OldGodType.Terravok, GodStatus.Awakened);
        EnhancedProbe("veloura_saved").Should().NotBe(EndingType.TrueEnding);
    }

    [Fact]
    public void StatusAwakenedWithNoFlag_CountsAtNoLegacySite()
    {
        StoryProgressionSystem.Instance.UpdateGodState(OldGodType.Terravok, GodStatus.Awakened);
        LegacyProbe("veloura_saved").Should().BeFalse();
    }
}
