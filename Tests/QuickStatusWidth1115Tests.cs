using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.15 follow-up to MentalUiWrap1115Tests: that suite found the core HP/Potions/Gold/XP
/// segment of DungeonLocation.ShowQuickStatus can itself exceed 80 columns at high stat widths
/// (the Hungarian case there: level 12, HP 390/390, XP 0/40900, reached 82 columns with zero tags).
/// ShowQuickStatus now wraps every segment (HP bar, Potions, Gold, XP, then the trailing tags)
/// the same way piece 3 wrapped only the trailing tags: each segment starts a new line if
/// appending it would push past 80 visible columns.
///
/// This covers the true worst case for stat width: level 99 with experience one short of the
/// level 100 threshold (the widest XP digits the compact format ever shows; at level 100 the
/// display switches to the short "XP MAX" form), MaxHP in the tens of thousands, Gold in the
/// billions and potions at that level's cap.
/// </summary>
[Collection("SharedGameSingletons")]
public class QuickStatusWidth1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };

    // Level 99: MaxPotions = 20 + (99-1) = 118. Experience one short of the level-100
    // threshold is the widest the compact XP text ever gets (level 100 itself shows the
    // short "XP MAX" form instead).
    private static Character WorstCaseHero(bool withTags)
    {
        long xpForLevel100 = GameConfig.GetExperienceForLevel(100);
        return new Character
        {
            Name1 = "dng", Name2 = "Dng", Class = CharacterClass.Warrior, Level = 99,
            HP = 99999, MaxHP = 99999, AI = CharacterAI.Human,
            Experience = xpForLevel100 - 1,
            Mental = withTags ? 0 : 100, // Broken vs Stable (no tag)
            Fatigue = withTags ? GameConfig.FatigueExhaustedThreshold : 0,
            Healing = 118, Gold = 9_999_999_999,
        };
    }

    private static (DungeonLocation d, TerminalEmulator term, MemoryStream output) Rig(Character hero, bool withTags)
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(Array.Empty<string>()), output);
        var d = new DungeonLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(d, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(d, hero);
        // withTags: a deadly danger gap (>= 6), the longest of the three floor-danger tags.
        typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.SetValue(d, hero.Level + (withTags ? 20 : 0));
        return (d, term, output);
    }

    private static void ShowQuickStatus(DungeonLocation d, Character hero) =>
        typeof(DungeonLocation).GetMethod("ShowQuickStatus", F)!.Invoke(d, new object[] { hero });

    private static string Plain(TerminalEmulator term, MemoryStream output)
    {
        term.StreamWriterInternal!.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");
    }

    /// <summary>Non-empty lines only: the plain divider row of dashes is not a status line.</summary>
    private static string[] ContentLines(string text) =>
        text.Replace("\r", "").Split('\n').Where(l => l.Length > 0 && l.Trim('─').Length > 0).ToArray();

    private static void EachLineAtMost80Columns(string text, string context)
    {
        foreach (var line in ContentLines(text))
            line.Length.Should().BeLessThanOrEqualTo(80, $"{context}: line {line.Length} cols long: \"{line}\"");
    }

    public static readonly object[][] LangAndTagCases =
        Langs.SelectMany(lang => new[] { false, true }.Select(t => new object[] { lang, t })).ToArray();

    [Theory]
    [MemberData(nameof(LangAndTagCases))]
    public void QuickStatus_WorstCaseStats_NeverOverflows80Columns(string lang, bool withTags)
    {
        string prevLang = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            var hero = WorstCaseHero(withTags);
            var (d, term, output) = Rig(hero, withTags);
            ShowQuickStatus(d, hero);
            EachLineAtMost80Columns(Plain(term, output), $"{lang}, withTags={withTags}");
        }
        finally { GameConfig.Language = prevLang; }
    }

    [Theory]
    [MemberData(nameof(LangAndTagCases))]
    public void QuickStatus_WorstCaseStats_ScreenReaderMode_NeverOverflows80Columns(string lang, bool withTags)
    {
        string prevLang = GameConfig.Language;
        bool prevSr = GameConfig.ScreenReaderMode;
        try
        {
            GameConfig.Language = lang;
            GameConfig.ScreenReaderMode = true;
            var hero = WorstCaseHero(withTags);
            var (d, term, output) = Rig(hero, withTags);
            ShowQuickStatus(d, hero);
            EachLineAtMost80Columns(Plain(term, output), $"{lang}, withTags={withTags}, screen reader");
        }
        finally { GameConfig.Language = prevLang; GameConfig.ScreenReaderMode = prevSr; }
    }

    [Theory]
    [MemberData(nameof(LangAndTagCases))]
    public void QuickStatus_WorstCaseStats_WrapsAcrossMultipleLines(string lang, bool withTags)
    {
        // Sanity check that the worst-case core segment alone (let alone with tags) actually
        // needs the wrap for this rig, not just that nothing overflows.
        string prevLang = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            var hero = WorstCaseHero(withTags);
            var (d, term, output) = Rig(hero, withTags);
            ShowQuickStatus(d, hero);
            var lines = ContentLines(Plain(term, output));
            lines.Length.Should().BeGreaterThan(1, "worst-case HP/Potions/Gold/XP widths do not fit on one line");
        }
        finally { GameConfig.Language = prevLang; }
    }

    [Fact]
    public void QuickStatus_NormalLowLevelStats_StaysOnOneLine()
    {
        // A normal low-level line (small HP, no gold, no tags) is the common case and must
        // not be wrapped unnecessarily.
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(Array.Empty<string>()), output);
        var hero = new Character
        {
            Name1 = "dng", Name2 = "Dng", Class = CharacterClass.Warrior, Level = 3,
            HP = 24, MaxHP = 30, AI = CharacterAI.Human, Mental = 100, Fatigue = 0,
            Healing = 2, Gold = 150,
        };
        var d = new DungeonLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(d, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(d, hero);
        typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.SetValue(d, hero.Level);
        ShowQuickStatus(d, hero);
        var lines = ContentLines(Plain(term, output));
        lines.Should().HaveCount(1, "a normal low-level line has nothing that needs wrapping");
    }
}
