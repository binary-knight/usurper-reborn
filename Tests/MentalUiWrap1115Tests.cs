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
/// v1.1.15 Mental piece 3 follow-up: DungeonLocation.ShowQuickStatus's trailing tags (fatigue,
/// mental, danger) could push the compact status line past the 80-column terminal width, once a
/// player at Breaking or Broken (or Strained/Shaken with a long tag in es/fr/hu/it) also carried a
/// fatigue tag and a deadly floor-danger tag. RoomTextWrap1114Tests caught one concrete case; this
/// wraps the trailing tags onto a second line whenever appending one would cross 80 columns, and
/// locks it down across every Mental band and every supported language, with fatigue and a deadly
/// danger gap both present at once. Also confirms CombatEngine's fight-start display (each tag its
/// own WriteLine) and BaseLocation's location header (measured: longest name + time + fatigue +
/// mental tag tops out around 69 columns, in Spanish) do not need the same fix.
///
/// Stats here are deliberately modest (level 1, single-digit HP, no gold) so the core HP/Potions/
/// Gold/XP segment stays well under 80 on its own in every language and the tag wrap is what is
/// being exercised. A separate, pre-existing finding: with a higher-level character (level 12, HP
/// 390/390, XP 0/40900) the Hungarian core segment alone reaches 82 columns with zero tags at all
/// (long "Főzetek"/"Arany" labels plus the wider bar). That is not a Mental or tag-wrap issue -- it
/// reproduces on release-1.1.15 before this branch existed -- so it is left to whoever owns the
/// core status bar's layout budget rather than folded into this fix.
/// </summary>
[Collection("SharedGameSingletons")]
public class MentalUiWrap1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };
    private static readonly int[] NonStableMental = { 60, 40, 15, 0 }; // Strained, Shaken, Breaking, Broken

    private static Character ModestHero(int mental) => new Character
    {
        Name1 = "dng", Name2 = "Dng", Class = CharacterClass.Warrior, Level = 1,
        HP = 9, MaxHP = 9, AI = CharacterAI.Human,
        Mental = mental,
        Fatigue = GameConfig.FatigueExhaustedThreshold, // always carries a fatigue tag too
        Healing = 0, Gold = 0,
    };

    private static (DungeonLocation d, TerminalEmulator term, MemoryStream output, Character hero) Rig(int mental)
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(Array.Empty<string>()), output);
        var hero = ModestHero(mental);
        var d = new DungeonLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(d, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(d, hero);
        // A deadly danger gap (>= 6): the longest of the three floor-danger tags.
        typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.SetValue(d, hero.Level + 20);
        return (d, term, output, hero);
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

    public static readonly object[][] LangAndBandCases =
        Langs.SelectMany(lang => NonStableMental.Select(m => new object[] { lang, m })).ToArray();

    [Theory]
    [MemberData(nameof(LangAndBandCases))]
    public void QuickStatus_MentalPlusFatiguePlusDeadlyDanger_NeverOverflows80Columns(string lang, int mental)
    {
        string prevLang = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            var (d, term, output, hero) = Rig(mental);
            ShowQuickStatus(d, hero);
            EachLineAtMost80Columns(Plain(term, output), $"{lang}, mental={mental}");
        }
        finally { GameConfig.Language = prevLang; }
    }

    [Theory]
    [MemberData(nameof(LangAndBandCases))]
    public void QuickStatus_ScreenReaderMode_AlsoNeverOverflows80Columns(string lang, int mental)
    {
        string prevLang = GameConfig.Language;
        bool prevSr = GameConfig.ScreenReaderMode;
        try
        {
            GameConfig.Language = lang;
            GameConfig.ScreenReaderMode = true;
            var (d, term, output, hero) = Rig(mental);
            ShowQuickStatus(d, hero);
            EachLineAtMost80Columns(Plain(term, output), $"{lang}, mental={mental}, screen reader");
        }
        finally { GameConfig.Language = prevLang; GameConfig.ScreenReaderMode = prevSr; }
    }

    [Fact]
    public void QuickStatus_WhenTagsFit_StaysOnOneLine()
    {
        // Stable, no fatigue, no danger gap: nothing to wrap, so the whole bar is one line.
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(Array.Empty<string>()), output);
        var hero = new Character
        {
            Name1 = "dng", Name2 = "Dng", Class = CharacterClass.Warrior, Level = 1,
            HP = 9, MaxHP = 9, AI = CharacterAI.Human, Mental = 100, Fatigue = 0,
            Healing = 0, Gold = 0,
        };
        var d = new DungeonLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(d, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(d, hero);
        typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.SetValue(d, 1);
        ShowQuickStatus(d, hero);
        var lines = ContentLines(Plain(term, output));
        lines.Should().HaveCount(1, "nothing needed wrapping");
    }

    [Fact]
    public void QuickStatus_WithDeadlyTagsThatOverflow_WrapsToASecondLine()
    {
        // Sanity check that the wrap actually engages for this rig, not just that nothing overflows.
        var (d, term, output, hero) = Rig(0); // Broken, plus fatigue and a deadly danger gap
        ShowQuickStatus(d, hero);
        var lines = ContentLines(Plain(term, output));
        lines.Length.Should().BeGreaterThan(1, "fatigue + Broken + a deadly danger tag do not fit on one line");
    }

    // ─── CombatEngine fight-start and BaseLocation header: measured, do not need the fix ───

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public void CombatFightStart_FatigueAndMentalLines_EachFitOneLine(string lang)
    {
        // Each is printed on its own WriteLine (never appended to another tag), so there is
        // nothing to wrap; this locks that shape down. Longest sentence in any language.
        string prevLang = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            Loc.Get("combat.fatigue_exhaustion").Length.Should().BeLessThanOrEqualTo(80);
            Loc.Get("combat.fatigue_dull").Length.Should().BeLessThanOrEqualTo(80);
            foreach (var key in new[] { "status.mental_strained", "status.mental_shaken", "status.mental_breaking", "status.mental_broken" })
                Loc.Get("combat.mental_tag", Loc.Get(key)).Length.Should().BeLessThanOrEqualTo(80);
        }
        finally { GameConfig.Language = prevLang; }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public void LocationHeader_LongestNamePlusTimePlusFatiguePlusMental_FitsUnder80(string lang)
    {
        // Worst realistic single header line: the longest location name, the longest time-of-day
        // string, the longest fatigue tag and the longest Mental tag, all on the header's one line.
        string prevLang = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            int maxNameLen = Enum.GetValues<GameLocation>()
                .Select(l => BaseLocation.GetLocationName(l).Length)
                .DefaultIfEmpty(0).Max();
            int maxTimeLen = new[] { "daily.time_dawn", "daily.time_morning", "daily.time_afternoon", "daily.time_evening", "daily.time_night", "daily.time_day" }
                .Select(k => Loc.Get(k).Length).Max();
            int maxFatigueLen = new[] { Loc.Get("status.fatigue_tired"), Loc.Get("status.fatigue_exhausted") }.Max(s => s.Length);
            int maxMentalLen = new[] { "status.mental_strained", "status.mental_shaken", "status.mental_breaking", "status.mental_broken" }
                .Select(k => Loc.Get(k).Length).Max();
            int worstCase = maxNameLen + 3 + maxTimeLen + 3 + maxFatigueLen + 3 + maxMentalLen;
            worstCase.Should().BeLessThanOrEqualTo(80, $"{lang}: worst-case header line would be {worstCase} columns");
        }
        finally { GameConfig.Language = prevLang; }
    }
}
