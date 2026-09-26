using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.15: the shared strict yes/no prompt (Sage/UI piece 1 of 3). About 220 call sites read a line
/// and test GameConfig.IsAffirmative alone, so any other key -- a typo, a stray Enter -- silently
/// counted as No (see DiscoverySystem.RunRisk, which used to walk a player away from a dungeon
/// discovery, and spend it, for the price of one wrong keystroke). GameConfig.IsNegative and
/// TerminalEmulator.AskYesNoAsync close that gap by re-asking on anything that is neither a
/// localized yes nor a localized no.
/// </summary>
[Collection("SharedGameSingletons")]
public class YesNoPrompt1115Tests
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

    // ---------- GameConfig.IsNegative ----------

    [Theory]
    [InlineData("N", true)]
    [InlineData("n", true)]
    [InlineData("No", true)]
    [InlineData("NO", true)]
    [InlineData("Non", true)]
    [InlineData("non", true)]
    [InlineData("Nem", true)]
    [InlineData("nem", true)]
    [InlineData("  n  ", true)]
    [InlineData("Y", false)]
    [InlineData("Yes", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("maybe", false)]
    public void IsNegative_MatchesEveryLanguagesNoWord_AndOnlyThose(string? input, bool expected)
    {
        GameConfig.IsNegative(input).Should().Be(expected);
    }

    // ---------- TerminalEmulator.AskYesNoAsync ----------

    [Theory]
    [InlineData("Y")]
    [InlineData("S")]
    [InlineData("O")]
    [InlineData("I")]
    [InlineData("Yes")]
    public async Task AskYesNoAsync_TrueOnAnyLanguagesYes(string answer)
    {
        var (term, _) = Stream(answer);
        (await term.AskYesNoAsync("? ")).Should().BeTrue();
    }

    [Theory]
    [InlineData("N")]
    [InlineData("No")]
    [InlineData("Non")]
    [InlineData("Nem")]
    public async Task AskYesNoAsync_FalseOnAnyLanguagesNo(string answer)
    {
        var (term, _) = Stream(answer);
        (await term.AskYesNoAsync("? ")).Should().BeFalse();
    }

    [Fact]
    public async Task AskYesNoAsync_JunkAnswer_AsksAgain_ThenAccepts()
    {
        var (term, output) = Stream("zzz", "Y");
        (await term.AskYesNoAsync("? ")).Should().BeTrue("the second, valid answer is the one that counts");
        Plain(term, output).Should().Contain(Loc.Get("ui.answer_yes_no"), "a stray answer gets a re-ask, not a silent No");
    }

    [Fact]
    public async Task AskYesNoAsync_BareEnter_AsksAgain_WhenStrict()
    {
        var (term, output) = Stream("", "Y");
        (await term.AskYesNoAsync("? ")).Should().BeTrue("a bare Enter with no default is invalid and re-asks");
        Plain(term, output).Should().Contain(Loc.Get("ui.answer_yes_no"));
    }

    [Fact]
    public async Task AskYesNoAsync_BareEnter_TakesTheDefault_WhenGiven()
    {
        var (term, output) = Stream("");
        (await term.AskYesNoAsync("? ", enterDefault: true)).Should().BeTrue();
        Plain(term, output).Should().NotContain(Loc.Get("ui.answer_yes_no"), "a set default on Enter does not re-ask");

        var (term2, _) = Stream("");
        (await term2.AskYesNoAsync("? ", enterDefault: false)).Should().BeFalse();
    }

    [Fact]
    public async Task AskYesNoAsync_FallsBackToFalse_AfterThreeInvalidAnswers()
    {
        var (term, _) = Stream("x", "x", "x");
        (await term.AskYesNoAsync("? ")).Should().BeFalse("a dead or confused peer cannot spin the prompt; No is the safe fallback");
    }

    // ---------- ConfirmAsync routes through AskYesNoAsync ----------

    [Fact]
    public async Task ConfirmAsync_NoDefault_JunkThenValid_AsksAgain_ViaTheSharedHelper()
    {
        var (term, output) = Stream("zzz", "Y");
        (await term.ConfirmAsync("Sure?")).Should().BeTrue();
        Plain(term, output).Should().Contain(Loc.Get("ui.answer_yes_no"));
    }

    [Fact]
    public async Task ConfirmAsync_WithDefault_TakesItOnEnter_AndStillRoutesThroughTheHelper()
    {
        var (term, _) = Stream("");
        (await term.ConfirmAsync("Sure?", true)).Should().BeTrue();

        var (term2, output2) = Stream("nope", "N");
        (await term2.ConfirmAsync("Sure?", false)).Should().BeFalse();
        Plain(term2, output2).Should().Contain(Loc.Get("ui.answer_yes_no"), "junk still re-asks on the defaulted overload");
    }

    // ---------- the discovery risk prompt (the only call-site conversion in this piece) ----------

    private static DiscoveryDefinition RiskDef(int basePct) => new DiscoveryDefinition
    {
        Id = "yesno1115_test_risk",
        Root = new DiscOutcome
        {
            Kind = DiscoveryKind.Risk,
            Intro = new List<string> { "A risky-looking thing sits here." },
            Prompt = "Take the risk?",
            RiskBasePercent = basePct,
            SuccessLines = new List<string> { "It works out." },
            FailLines = new List<string> { "It does not work out." },
            SuccessEffects = new List<DiscEffect>(),
            FailEffects = new List<DiscEffect>()
        }
    };

    private static Character Hero() => new Character
    {
        Name1 = "yn", Name2 = "Yn", Class = CharacterClass.Warrior, Level = 5,
        HP = 100, MaxHP = 100, Gold = 100, AI = CharacterAI.Human, Dexterity = 50, Wisdom = 50
    };

    [Fact]
    public async Task DiscoveryRisk_JunkAnswer_AsksAgain_InsteadOfWalkingAway()
    {
        // was: a stray key silently read as No (IsYes) and walked the player away, spending the discovery.
        var (term, output) = Stream("zzz", "N", "");
        await DiscoverySystem.Instance.RunDiscovery(RiskDef(100), Hero(), 3, term);
        string shown = Plain(term, output);
        shown.Should().Contain(Loc.Get("ui.answer_yes_no"), "the stray key re-asks instead of silently walking away");
        shown.Should().Contain(Loc.Get("discovery.walk_away"), "the deliberate N that follows still walks away");
    }

    [Fact]
    public async Task DiscoveryRisk_Yes_TakesTheRisk_AndDoesNotWalkAway()
    {
        var (term, output) = Stream("Y", "");
        var outcome = await DiscoverySystem.Instance.RunDiscovery(RiskDef(100), Hero(), 3, term);
        string shown = Plain(term, output);
        shown.Should().NotContain(Loc.Get("discovery.walk_away"));
        outcome.Success.Should().BeTrue("RiskBasePercent 100 always succeeds");
    }
}
