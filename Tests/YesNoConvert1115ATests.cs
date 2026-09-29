using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.15: yesno-convert-a. Nine location/system files (TempleLocation, CastleLocation,
/// DarkAlleyLocation, DungeonLocation, MagicShopLocation, CharacterCreationSystem, TeamCornerLocation,
/// HealerLocation, AnchorRoadLocation) had their hand-rolled yes/no reads (GameConfig.IsAffirmative
/// alone, or the StartsWith("Y") / == "Y" equivalents) converted to TerminalEmulator.AskYesNoAsync, so
/// a stray key or a typo re-asks instead of silently counting as No. A handful of the converted sites
/// are exercised end to end: a junk answer re-asks (and the loc'd re-ask line is shown), then a real Y
/// or N is honored. YesNoGuard1115Tests carries the repo-wide source check that none of the old bare
/// checks remain, across this file's sites and every other one.
/// </summary>
[Collection("SharedGameSingletons")]
public class YesNoConvert1115ATests
{
    private const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

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

    // ---------- CharacterCreationSystem.ConfirmChoice (a shared helper, several call sites) ----------

    private static Task<bool> ConfirmChoice(TerminalEmulator term, string message, bool defaultYes)
    {
        var ccs = new CharacterCreationSystem(term);
        var mi = typeof(CharacterCreationSystem).GetMethod("ConfirmChoice", NonPublicInstance)!;
        return (Task<bool>)mi.Invoke(ccs, new object[] { message, defaultYes })!;
    }

    [Fact]
    public async Task ConfirmChoice_JunkThenY_GoesThrough()
    {
        var (term, output) = Stream("zzz", "Y");
        (await ConfirmChoice(term, "Sure", false)).Should().BeTrue();
        Plain(term, output).Should().Contain(Loc.Get("ui.answer_yes_no"), "a stray key re-asks instead of silently declining");
    }

    [Fact]
    public async Task ConfirmChoice_JunkThenN_Declines()
    {
        var (term, output) = Stream("zzz", "N");
        (await ConfirmChoice(term, "Sure", true)).Should().BeFalse();
        Plain(term, output).Should().Contain(Loc.Get("ui.answer_yes_no"));
    }

    [Fact]
    public async Task ConfirmChoice_BareEnter_TakesTheCallersDefault()
    {
        var (term, _) = Stream("");
        (await ConfirmChoice(term, "Sure", true)).Should().BeTrue();

        var (term2, _) = Stream("");
        (await ConfirmChoice(term2, "Sure", false)).Should().BeFalse();
    }

    // ---------- AnchorRoadLocation.OfferGauntletSurrender (a (y/N) default site) ----------

    private static async Task<bool> OfferGauntletSurrender(TerminalEmulator term)
    {
        var loc = new AnchorRoadLocation();
        typeof(BaseLocation).GetField("terminal", NonPublicInstance)!.SetValue(loc, term);
        var mi = typeof(AnchorRoadLocation).GetMethod("OfferGauntletSurrender", NonPublicInstance)!;
        return await (Task<bool>)mi.Invoke(loc, new object[] { 3, 2, 100L, 50L, 5 })!;
    }

    [Fact]
    public async Task GauntletSurrender_JunkThenY_GoesThrough()
    {
        var (term, output) = Stream("zzz", "Y");
        (await OfferGauntletSurrender(term)).Should().BeTrue();
        Plain(term, output).Should().Contain(Loc.Get("ui.answer_yes_no"));
    }

    [Fact]
    public async Task GauntletSurrender_JunkThenN_Declines()
    {
        var (term, output) = Stream("zzz", "N");
        (await OfferGauntletSurrender(term)).Should().BeFalse();
        Plain(term, output).Should().Contain(Loc.Get("ui.answer_yes_no"));
    }

    [Fact]
    public async Task GauntletSurrender_BareEnter_TakesTheOldDefaultOfNo()
    {
        var (term, _) = Stream("");
        (await OfferGauntletSurrender(term)).Should().BeFalse("the prompt shows (y/N)");
    }

    // ---------- DungeonLocation.RunDungeonTutorial ----------

    private static async Task<bool> RunDungeonTutorial(TerminalEmulator term, Character player)
    {
        var loc = new DungeonLocation();
        var mi = typeof(DungeonLocation).GetMethod("RunDungeonTutorial", NonPublicInstance)!;
        return await (Task<bool>)mi.Invoke(loc, new object[] { player, term })!;
    }

    private static Character TutorialHero() => new Character
    {
        Name1 = "yn2", Name2 = "Yn2", Class = CharacterClass.Warrior, Level = 1, HP = 20, MaxHP = 20
    };

    [Fact]
    public async Task DungeonTutorial_JunkThenY_GoesThrough()
    {
        // accepting runs the full 8-page tutorial, each page paused on a key; feed enough blank lines
        var (term, output) = Stream("zzz", "Y", "", "", "", "", "", "", "", "");
        (await RunDungeonTutorial(term, TutorialHero())).Should().BeTrue();
        Plain(term, output).Should().Contain(Loc.Get("ui.answer_yes_no"));
    }

    [Fact]
    public async Task DungeonTutorial_JunkThenN_Declines()
    {
        var (term, output) = Stream("zzz", "N");
        var player = TutorialHero();
        (await RunDungeonTutorial(term, player)).Should().BeFalse();
        var shown = Plain(term, output);
        shown.Should().Contain(Loc.Get("ui.answer_yes_no"));
        shown.Should().Contain(Loc.Get("dungeon.tut.declined"));
        player.HintsShown.Should().Contain("dungeon_tutorial_v1", "seen is marked regardless of the answer");
    }
}
