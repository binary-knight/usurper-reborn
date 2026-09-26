using System;
using System.IO;
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
/// v1.1.15: yesno-convert-b -- converting the hand-rolled "read a line, test GameConfig.IsAffirmative"
/// yes/no sites in a second batch of files to TerminalEmulator.AskYesNoAsync / AskYesNoKeyAsync (see
/// YesNoPrompt1115Tests for the shared helper's own tests, and DiscoverySystem.RunRisk for the first
/// converted call site). This file covers: behaviour at a representative handful of the converted
/// call sites in this batch, and the AskYesNoKeyAsync single-key sibling. YesNoGuard1115Tests carries
/// the repo-wide source sweep that nothing in this batch's files, or any other, still tests
/// GameConfig.IsAffirmative, StartsWith("Y") or =="Y" outside a line that carries a "yesno-exempt"
/// marker explaining why it is not a plain yes/no.
/// </summary>
[Collection("SharedGameSingletons")]
public class YesNoConvert1115BTests
{
    private static (TerminalEmulator term, MemoryStream output) Stream(params string[] lines)
    {
        var output = new MemoryStream();
        return (new TerminalEmulator(new LineStream(lines), output), output);
    }

    private static string Plain(TerminalEmulator term, MemoryStream output)
    {
        term.StreamWriterInternal?.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");
    }

    private static Character Hero() => new Character
    {
        Name1 = "yn2", Name2 = "Yn Two", Class = CharacterClass.Warrior, Level = 10,
        HP = 100, MaxHP = 100, Gold = 1000, AI = CharacterAI.Human, Charisma = 250, Dexterity = 50, Wisdom = 50
    };

    // ---------- a converted call site: HagglingEngine.Haggle (Scripts/Systems/HagglingEngine.cs) ----------

    [Fact]
    public async Task Haggle_JunkThenYes_AcceptsTheOfferedPrice()
    {
        var player = Hero();
        var (term, output) = Stream("90", "zzz", "Y");
        var result = await HagglingEngine.Haggle(player, HagglingEngine.ShopType.Weapon, 100, "Keeper", term);
        result.Price.Should().Be(90, "a junk answer re-asks, then the deliberate Y accepts the haggled price");
        Plain(term, output).Should().Contain(Loc.Get("ui.answer_yes_no"));
    }

    [Fact]
    public async Task Haggle_JunkThenNo_KeepsTheOriginalPrice()
    {
        var player = Hero();
        var (term, _) = Stream("90", "zzz", "N");
        var result = await HagglingEngine.Haggle(player, HagglingEngine.ShopType.Weapon, 100, "Keeper", term);
        result.Price.Should().Be(100, "declining the haggled price keeps the original cost");
    }

    // ---------- a converted call site: VersionChecker.PromptForUpdate (Scripts/Systems/VersionChecker.cs) ----------

    [Fact]
    public async Task VersionChecker_PromptForUpdate_JunkThenYes_ReturnsTrue()
    {
        var checker = new VersionChecker();
        typeof(VersionChecker).GetProperty(nameof(VersionChecker.NewVersionAvailable))!.SetValue(checker, true);
        var (term, output) = Stream("zzz", "Y");
        (await checker.PromptForUpdate(term)).Should().BeTrue();
        Plain(term, output).Should().Contain(Loc.Get("ui.answer_yes_no"));
    }

    [Fact]
    public async Task VersionChecker_PromptForUpdate_JunkThenNo_ReturnsFalse()
    {
        var checker = new VersionChecker();
        typeof(VersionChecker).GetProperty(nameof(VersionChecker.NewVersionAvailable))!.SetValue(checker, true);
        var (term, _) = Stream("zzz", "N");
        (await checker.PromptForUpdate(term)).Should().BeFalse();
    }

    // ---------- TerminalEmulator.AskYesNoKeyAsync (the single-key sibling added in this piece) ----------

    [Fact]
    public async Task AskYesNoKeyAsync_JunkAnswer_AsksAgain_ThenAccepts()
    {
        var (term, output) = Stream("z", "Y");
        (await term.AskYesNoKeyAsync()).Should().BeTrue("a stray key re-asks instead of silently reading as No");
        Plain(term, output).Should().Contain(Loc.Get("ui.answer_yes_no"));
    }

    [Fact]
    public async Task AskYesNoKeyAsync_FalseOnNo()
    {
        var (term, _) = Stream("N");
        (await term.AskYesNoKeyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task AskYesNoKeyAsync_BareEnter_TakesTheDefault_WhenGiven()
    {
        var (term, _) = Stream("");
        (await term.AskYesNoKeyAsync(enterDefault: true)).Should().BeTrue();

        var (term2, _) = Stream("");
        (await term2.AskYesNoKeyAsync(enterDefault: false)).Should().BeFalse();
    }

    [Fact]
    public async Task AskYesNoKeyAsync_FallsBackToFalse_AfterThreeInvalidAnswers()
    {
        var (term, _) = Stream("x", "x", "x");
        (await term.AskYesNoKeyAsync()).Should().BeFalse("a dead or confused peer cannot spin the prompt; No is the safe fallback");
    }

    // Source-level sweep: YesNoGuard1115Tests carries the repo-wide check that nothing in this
    // batch's files (or any other) still tests GameConfig.IsAffirmative, StartsWith("Y")/'Y', or
    // =="Y"/'Y' outside a line that carries a "yesno-exempt" marker.
}
