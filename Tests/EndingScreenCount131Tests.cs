using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.1 piece C1-R: the ending screen's world impact counts the gods the ending choice counted, from the
/// story flags (the six Old Gods; an allied Noctura is saved; a paradox-woken Terravok and Manwe are not
/// counted), and a Noctura defeated after her betrayal is destroyed in both. The screen is rendered through a
/// captured terminal by calling the private ShowWorldImpact. State is built in memory only; no save folder is
/// read or written.
/// </summary>
[Collection("SharedGameSingletons")]
public class EndingScreenCount131Tests : IDisposable
{
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly Type[] Swapped =
    {
        typeof(StoryProgressionSystem), typeof(OceanPhilosophySystem), typeof(GriefSystem), typeof(CycleSystem),
    };
    private readonly List<(FieldInfo Field, object? Old)> _old;
    private readonly bool _pacingBefore = Pacing.Disabled;

    public EndingScreenCount131Tests()
    {
        _old = Swapped.Select(t => t.GetField("_fallbackInstance", SNP)!).Select(f => (f, f.GetValue(null))).ToList();
        foreach (var t in Swapped)
            t.GetField("_fallbackInstance", SNP)!.SetValue(null, Activator.CreateInstance(t));
        Pacing.Disabled = true;
    }

    public void Dispose()
    {
        foreach (var (f, v) in _old) f.SetValue(null, v);
        Pacing.Disabled = _pacingBefore;
    }

    private static Character Hero() => new Character { Name2 = "EndingsHero", Chivalry = 0, Darkness = 0 };

    private static void Flags(params string[] flags)
    {
        foreach (var f in flags) StoryProgressionSystem.Instance.SetStoryFlag(f, true);
    }

    private static void Status(OldGodType god, GodStatus status) =>
        StoryProgressionSystem.Instance.OldGodStates[god].Status = status;

    private static EndingType Choose(params string[] flags)
    {
        Flags(flags);
        return EndingsSystem.Instance.DetermineEnding(Hero());
    }

    // The ending screen's world impact, drawn on a captured terminal in `lang`.
    private static async Task<string> Screen(string lang = "en")
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(Enumerable.Repeat("", 10)), output);
        var m = typeof(EndingsSystem).GetMethod("ShowWorldImpact", BindingFlags.NonPublic | BindingFlags.Instance)!;
        using (Loc.RenderLanguage(lang))
        {
            await (Task)m.Invoke(EndingsSystem.Instance,
                new object[] { Hero(), EndingType.Defiant, StoryProgressionSystem.Instance, term })!;
            term.StreamWriterInternal?.Flush();
            return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "").Replace("\r", "");
        }
    }

    private static string SavedRow(int saved, int destroyed) => Loc.Get("ending.world_gods_saved", saved, destroyed);
    private static string DestroyedRow(int destroyed, int saved) => Loc.Get("ending.world_gods_destroyed", destroyed, saved);

    [Fact]
    public async Task Screen_VelouraSavedAndNocturaAllied_ShowsTwoSavedZeroDestroyed()
    {
        Flags("veloura_saved", "noctura_ally");
        Status(OldGodType.Veloura, GodStatus.Saved);
        Status(OldGodType.Noctura, GodStatus.Allied);
        var text = await Screen();
        text.Should().Contain(SavedRow(2, 0));
        text.Should().NotContain(Loc.Get("ending.world_gods_uncertain"));
    }

    [Fact]
    public async Task Screen_TerravokAwakenedWithNoFlag_IsUncertain()
    {
        Status(OldGodType.Terravok, GodStatus.Awakened);
        var text = await Screen();
        text.Should().Contain(Loc.Get("ending.world_gods_uncertain"));
        text.Should().NotContain(SavedRow(1, 0));
    }

    [Fact]
    public async Task Screen_ManweDefeated_IsNotCounted()
    {
        Flags("manwe_destroyed", "veloura_saved", "maelketh_destroyed");
        Status(OldGodType.Manwe, GodStatus.Defeated);
        Status(OldGodType.Veloura, GodStatus.Saved);
        Status(OldGodType.Maelketh, GodStatus.Defeated);
        var text = await Screen();
        text.Should().Contain(Loc.Get("ending.world_gods_uncertain"));
        text.Should().NotContain(DestroyedRow(2, 1));
    }

    public static IEnumerable<object[]> FlagTable() => new[]
    {
        new object[] { new[] { "veloura_saved", "aurelion_saved", "terravok_saved" }, 3, 0, EndingType.Savior },
        new object[] { new[] { "veloura_saved", "aurelion_saved", "noctura_ally" }, 3, 0, EndingType.Savior },
        new object[] { new[] { "veloura_saved", "maelketh_saved" }, 2, 0, EndingType.Defiant },
        new object[] { new[] { "veloura_saved", "noctura_ally", "maelketh_destroyed" }, 2, 1, EndingType.Defiant },
        new object[] { new[] { "veloura_destroyed", "aurelion_destroyed", "terravok_destroyed", "maelketh_destroyed", "thorgrim_destroyed" }, 0, 5, EndingType.Usurper },
        new object[] { new[] { "veloura_destroyed", "aurelion_destroyed", "terravok_destroyed", "noctura_destroyed", "thorgrim_destroyed" }, 0, 5, EndingType.Usurper },
        new object[] { new[] { "manwe_destroyed", "manwe_saved", "veloura_saved" }, 1, 0, EndingType.Defiant },
    };

    [Theory]
    [MemberData(nameof(FlagTable))]
    public void Helper_CountsWhatDetermineEndingUsed(string[] flags, int saved, int destroyed, EndingType ending)
    {
        Flags(flags);
        EndingsSystem.CountGodsByFlags(StoryProgressionSystem.Instance).Should().Be((saved, destroyed));
        EndingsSystem.Instance.DetermineEnding(Hero()).Should().Be(ending);
    }

    [Fact]
    public async Task NocturaDefeatedAfterBetrayal_IsDestroyed_InTheChoiceAndOnTheScreen()
    {
        var four = new[] { "veloura_destroyed", "aurelion_destroyed", "terravok_destroyed", "maelketh_destroyed" };
        Flags(four);
        Flags("noctura_betrayed", "noctura_betrayal_defeated");
        StoryProgressionSystem.Instance.SetStoryFlag("noctura_ally", false);
        Status(OldGodType.Noctura, GodStatus.Defeated);
        EndingsSystem.Instance.DetermineEnding(Hero()).Should().Be(EndingType.Usurper);
        EndingsSystem.CountGodsByFlags(StoryProgressionSystem.Instance).Should().Be((0, 5));
        (await Screen()).Should().Contain(DestroyedRow(5, 0));
    }

    [Fact]
    public async Task NocturaEscaped_IsNotDestroyed()
    {
        Flags("veloura_destroyed", "aurelion_destroyed", "terravok_destroyed", "maelketh_destroyed");
        Flags("noctura_betrayed", "noctura_escaped_with_power");
        StoryProgressionSystem.Instance.SetStoryFlag("noctura_ally", false);
        EndingsSystem.Instance.DetermineEnding(Hero()).Should().NotBe(EndingType.Usurper);
        EndingsSystem.CountGodsByFlags(StoryProgressionSystem.Instance).Should().Be((0, 4));
        (await Screen()).Should().Contain(DestroyedRow(4, 0));
    }

    [Fact]
    public void NocturaDestroyedAndBetrayalDefeated_CountsOnce()
    {
        Flags("noctura_destroyed", "noctura_betrayal_defeated");
        EndingsSystem.CountGodsByFlags(StoryProgressionSystem.Instance).Should().Be((0, 1));
    }

    [Fact]
    public void ScreenIsOneHelperCall_AndHoldsNoGodStatusRead()
    {
        var src = File.ReadAllText(Path.Combine(
            UsurperReborn.Tests.Localization.HardcodedTextScannerTests.RepoRoot(), "Scripts", "Systems", "EndingsSystem.cs"));
        string Body(string signature)
        {
            int a = src.IndexOf(signature, StringComparison.Ordinal);
            a.Should().BeGreaterThan(0);
            int b = src.IndexOf("        /// <summary>", a, StringComparison.Ordinal);
            return src.Substring(a, b - a);
        }
        var screen = Body("private async Task ShowWorldImpact(");
        screen.Should().Contain("CountGodsByFlags(story)");
        screen.Should().NotContain("GodStatus");
        screen.Should().NotContain("OldGodStates");
        Body("public EndingType DetermineEnding(").Should().Contain("CountGodsByFlags(story)");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void Rows_FitSeventyNineColumns_WithBothNumbersAtSix(string lang)
    {
        using (Loc.RenderLanguage(lang))
        {
            ("  " + SavedRow(6, 6)).Length.Should().BeLessThanOrEqualTo(79);
            ("  " + DestroyedRow(6, 6)).Length.Should().BeLessThanOrEqualTo(79);
            ("  " + Loc.Get("ending.world_gods_saved_desc")).Length.Should().BeLessThanOrEqualTo(79);
            ("  " + Loc.Get("ending.world_gods_destroyed_desc")).Length.Should().BeLessThanOrEqualTo(79);
            ("  " + Loc.Get("ending.world_gods_uncertain")).Length.Should().BeLessThanOrEqualTo(79);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public async Task RenderedScreenRows_FitSeventyNineColumns(string lang)
    {
        Flags("veloura_saved", "noctura_ally", "aurelion_saved");
        (await Screen(lang)).Split('\n').Should().OnlyContain(l => l.Length <= 79);
    }
}
