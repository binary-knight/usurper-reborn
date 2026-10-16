using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.UI;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.3: GameConfig.MomentOfSilenceEnabled holds the pause of UIHelper.MomentOfSilence. On by default, so players
/// keep the pause; the balance simulator turns it off so a companion's death scene does not wait on the clock.
/// </summary>
[Collection("SharedGameSingletons")]
public class MomentOfSilence133Tests
{
    private static TerminalEmulator Term() =>
        new TerminalEmulator(new LineStream(Enumerable.Repeat("", 4)), new MemoryStream());

    private static async Task<long> TimedSilence(bool enabled, int durationMs)
    {
        bool savedSwitch = GameConfig.MomentOfSilenceEnabled, savedReader = GameConfig.ScreenReaderMode;
        try
        {
            GameConfig.MomentOfSilenceEnabled = enabled;
            GameConfig.ScreenReaderMode = false;
            var sw = Stopwatch.StartNew();
            await UIHelper.MomentOfSilence(Term(), durationMs);
            return sw.ElapsedMilliseconds;
        }
        finally
        {
            GameConfig.MomentOfSilenceEnabled = savedSwitch;
            GameConfig.ScreenReaderMode = savedReader;
        }
    }

    [Fact]
    public void TheSwitch_IsOnByDefault_AndNotASysopSetting()
    {
        var p = typeof(GameConfig).GetProperty(nameof(GameConfig.MomentOfSilenceEnabled))!;
        GameConfig.MomentOfSilenceEnabled.Should().BeTrue();
        p.SetMethod!.IsAssembly.Should().BeTrue("the setter is internal");
        string root = Leftovers1114BTests.RepoRoot();
        foreach (var file in Directory.GetFiles(Path.Combine(root, "Scripts"), "*.cs", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(file);
            if (name is "GameConfig.cs" or "UIHelper.cs") continue;
            File.ReadAllText(file).Should().NotContain("MomentOfSilenceEnabled", $"{name} must not read or set the switch");
        }
    }

    [Fact]
    public async Task On_ThePauseIsHeld()
    {
        (await TimedSilence(enabled: true, durationMs: 400)).Should().BeGreaterThanOrEqualTo(350);
    }

    [Fact]
    public async Task Off_ThePauseIsSkipped()
    {
        (await TimedSilence(enabled: false, durationMs: 5000)).Should().BeLessThan(1000);
    }
}
