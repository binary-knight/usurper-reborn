using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake.Systems;
using UsurperReborn.Tests.Localization;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.3: the shared location chrome (BaseLocation.ShowStatusLine and ShowQuickCommandBar) wraps to a second
/// row when it would pass 79 columns, breaking only between segments. Rows that already fit are unchanged, and
/// the screen reader forms stay one plain line.
/// </summary>
[Collection("SharedGameSingletons")]
public class ChromeWidth123Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int MaxWidth = 79;
    private static readonly string[] Languages = { "en", "es", "fr", "hu", "it" };

    // GameConfig.MaxNameLength (30) characters, the longest name a player can have.
    private const string LongName = "Aranyszivu Hosszunevu Kalandor";
    private const long Big = 123456;

    /// <summary>Character.Gold has no clamp; the only enforced bound is the bank's overflow guard.</summary>
    private static long GoldCap =>
        (long)typeof(BankLocation).GetField("MaxGold", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

    private sealed class CrowdedInn : InnLocation
    {
        public List<NPC> Here = new();
        protected override List<NPC> GetLiveNPCsAtLocation() => Here;
    }

    private sealed class CrowdedShop : WeaponShopLocation
    {
        public List<NPC> Here = new();
        protected override List<NPC> GetLiveNPCsAtLocation() => Here;
    }

    /// <summary>A location whose id is Main Street, so the bar leaves out [R], drawn by BaseLocation's own bar.</summary>
    private sealed class StreetChrome : BaseLocation
    {
        public List<NPC> Here = new();
        public StreetChrome() : base(GameLocation.MainStreet, "Main Street", "") { }
        protected override List<NPC> GetLiveNPCsAtLocation() => Here;
    }

    /// <summary>A plain location with [R] whose ProcessChoice is BaseLocation's.</summary>
    private sealed class PlainChrome : BaseLocation
    {
        public PlainChrome() : base(GameLocation.TheInn, "The Inn", "") { }
    }

    private static List<NPC> Crowd(int n) => Enumerable.Range(0, n).Select(_ => new NPC()).ToList();

    private static Character Hero(bool mana, bool worst)
    {
        int level = worst ? GameConfig.MaxLevel - 1 : 5;
        var hero = new Character
        {
            Name1 = "tester", Name2 = worst ? LongName : "Short", Level = level, AI = CharacterAI.Human,
            Class = mana ? CharacterClass.Magician : CharacterClass.Warrior,
            HP = worst ? Big : 60, MaxHP = worst ? Big : 100, Gold = worst ? GoldCap : 999,
            Resurrections = worst ? 10 : 3, MaxResurrections = worst ? 10 : 3,
        };
        long prev = GameConfig.GetExperienceForLevel(level), next = GameConfig.GetExperienceForLevel(level + 1);
        hero.Experience = prev + (next - prev) * (worst ? 95 : 40) / 100;
        if (mana) { hero.Mana = worst ? Big : 20; hero.MaxMana = worst ? Big : 40; }
        else
        {
            hero.Stamina = worst ? 61000 : 10;
            hero.CurrentCombatStamina = hero.MaxCombatStamina;
        }
        return hero;
    }

    private static (T location, MemoryStream output) At<T>(T location, Character hero) where T : BaseLocation
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new MemoryStream(), output);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(location, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(location, hero);
        return (location, output);
    }

    private static string Raw(BaseLocation location, MemoryStream output)
    {
        var term = (TerminalEmulator)typeof(BaseLocation).GetField("terminal", F)!.GetValue(location)!;
        term.StreamWriterInternal?.Flush();
        return Encoding.UTF8.GetString(output.ToArray()).Replace("\r", "");
    }

    private static string Strip(string s) => Regex.Replace(s, "\u001b\\[[0-9;?]*[A-Za-z]", "");

    private static List<string> Rows(string text) => text.Replace("\r", "").Split('\n').ToList();

    private static T InLanguage<T>(string lang, Func<T> body)
    {
        var prev = GameConfig.Language;
        bool sr = GameConfig.ScreenReaderMode;
        var diff = DifficultySystem.CurrentDifficulty;
        try
        {
            GameConfig.Language = lang;
            GameConfig.ScreenReaderMode = false;
            DifficultySystem.CurrentDifficulty = DifficultyMode.Normal;
            return body();
        }
        finally { GameConfig.Language = prev; GameConfig.ScreenReaderMode = sr; DifficultySystem.CurrentDifficulty = diff; }
    }

    /// <summary>Writes a render to USURPER_EVIDENCE_DIR when set, for a by-eye check.</summary>
    private static void Capture(string name, string text)
    {
        var dir = Environment.GetEnvironmentVariable("USURPER_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, name), text);
    }

    private static string L(string lang, string key, params object[] args) => Loc.GetIn(lang, key, args);

    /// <summary>The raw (ANSI) output of ShowStatusLine, which draws the status line and then the Quick Commands bar.</summary>
    private static string Chrome(string lang, string where, bool mana, bool worst, bool screenReader = false, int npcs = 12) =>
        InLanguage(lang, () =>
        {
            var hero = Hero(mana, worst);
            hero.ScreenReaderMode = screenReader;
            BaseLocation loc = where switch
            {
                "inn" => new CrowdedInn { Here = Crowd(npcs) },
                "shop" => new CrowdedShop { Here = Crowd(npcs) },
                _ => new StreetChrome { Here = Crowd(npcs) },
            };
            var (l, output) = At(loc, hero);
            typeof(BaseLocation).GetMethod("ShowStatusLine", F)!.Invoke(l, null);
            return Raw(l, output);
        });

    private static void EveryRowFits(string plain, string because)
    {
        foreach (var row in Rows(plain))
            row.Length.Should().BeLessOrEqualTo(MaxWidth, $"{because}: \"{row}\"");
    }

    /// <summary>The Quick Commands rows: the prefixed row and the continuation rows after it, up to the blank row.</summary>
    private static List<string> BarRows(string plain, string lang)
    {
        var rows = Rows(plain);
        int at = rows.FindIndex(r => r.StartsWith(L(lang, "ui.quick_commands") + ": "));
        at.Should().BeGreaterOrEqualTo(0, "the Quick Commands bar is drawn");
        return rows.Skip(at).TakeWhile(r => r.Length > 0).ToList();
    }

    /// <summary>The status rows: from the HP row up to the blank row.</summary>
    private static List<string> StatusRows(string plain, string lang)
    {
        var rows = Rows(plain);
        int at = rows.FindIndex(r => r.StartsWith(L(lang, "status.hp") + ": "));
        at.Should().BeGreaterOrEqualTo(0, "the status line is drawn");
        return rows.Skip(at).TakeWhile(r => r.Length > 0).ToList();
    }

    [Fact]
    public void WorstCaseInputs_AreTheGameBounds()
    {
        LongName.Length.Should().Be(GameConfig.MaxNameLength);
        GoldCap.Should().Be(long.MaxValue / 100 * 99, "BankLocation.MaxGold is the bound gold reaches");
        GoldCap.ToString("N0").Length.Should().Be(25);
        Hero(false, true).MaxCombatStamina.Should().BeGreaterOrEqualTo(100000, "six-digit stamina");
        UsurperRemake.UI.UIHelper.WrapWidth.Should().Be(MaxWidth);
    }

    public static IEnumerable<object[]> WorstCases() =>
        from lang in new[] { "en", "hu" }
        from loc in new[] { "inn", "shop", "street" }
        from mana in new[] { true, false }
        select new object[] { lang, loc, mana };

    [Theory]
    [MemberData(nameof(WorstCases))]
    public void WorstCase_EveryChromeRowFits(string lang, string where, bool mana)
    {
        string plain = Strip(Chrome(lang, where, mana, worst: true));
        Capture($"chrome-{where}-{(mana ? "mana" : "stamina")}-worst-{lang}.txt", plain);
        EveryRowFits(plain, $"{lang} {where} chrome");
        StatusRows(plain, lang).Count.Should().BeGreaterThan(1, "the worst-case status line needs a second row");
        BarRows(plain, lang).Count.Should().BeGreaterThan(1, "the worst-case bar needs a second row");
        string bar = string.Join(" ", BarRows(plain, lang));
        bar.Contains("[R]").Should().Be(where != "street", "Main Street leaves out [R]");
        bar.Should().Contain($"[0] {L(lang, "base.qc_talk")} (12)").And.Contain($"[!]{L(lang, "base.qc_bug")}");
        string status = string.Join(" ", StatusRows(plain, lang));
        status.Should().Contain(GoldCap.ToString("N0")).And.Contain($"{Big}/{Big}").And.Contain($"{L(lang, "status.revives")}: 10/10");
        Regex.IsMatch(status, @"10/10 \(\d\d%\)").Should().BeTrue("the two-digit XP suffix follows the last segment");
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void QuickCommandBar_FitsInEveryLanguage_WithLabelIndent(string lang)
    {
        foreach (var where in new[] { "inn", "street" })
        {
            string plain = Strip(Chrome(lang, where, true, worst: true));
            var bar = BarRows(plain, lang);
            EveryRowFits(string.Join("\n", bar), $"{lang} {where} Quick Commands");
            string indent = new string(' ', UsurperRemake.UI.UIHelper.VisibleLength(L(lang, "ui.quick_commands") + ": "));
            foreach (var row in bar.Skip(1))
            {
                row.Should().StartWith(indent + "[", "continuation keys line up under the first segment");
                row.TrimEnd().Should().Be(row, "a wrapped row carries no trailing gap");
            }
            // every key is drawn once, in order
            string keys = string.Concat(Regex.Matches(string.Join(" ", bar), @"\[(.)\]").Select(m => m.Groups[1].Value));
            keys.Should().Be(where == "street" ? "%*?0~/!" : "%R*?0~/!");
        }
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public void StatusLine_ContinuationRows_IndentTwo_WithoutTheBar(string lang)
    {
        string plain = Strip(Chrome(lang, "inn", false, worst: true));
        var rows = StatusRows(plain, lang);
        foreach (var row in rows.Skip(1))
        {
            row.Should().StartWith("  ").And.NotStartWith("   ", "continuation rows are indented 2");
            row.TrimStart().Should().NotStartWith("|", "the leading separator is dropped on a new row");
        }
        // nothing lost: joined back with the separator the rows read as one status line
        string joined = string.Join(" | ", rows.Select(r => r.Trim()));
        joined.Should().Contain($"{L(lang, "status.gold_label")}: {GoldCap:N0}").And.Contain($"{L(lang, "status.sta")}: ");
        Regex.IsMatch(joined, @"\| \(").Should().BeFalse("the XP suffix never starts a row");
    }

    // ---------- rows that fit today are byte-identical (golden captured from the code before the wrap) ----------

    private static readonly string GoldenPath = Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Tests/Golden/ChromeWidth123Before.txt");

    private static IEnumerable<(string name, string raw)> ShortCases()
    {
        foreach (var lang in new[] { "en", "hu" })
            foreach (var where in new[] { "inn", "street" })
                foreach (var mana in new[] { true, false })
                    yield return ($"{lang}-{where}-{(mana ? "mana" : "stamina")}", Chrome(lang, where, mana, worst: false, npcs: 2));
        foreach (var lang in new[] { "en", "hu" })
            yield return ($"{lang}-inn-sr-worst", Chrome(lang, "inn", true, worst: true, screenReader: true));
    }

    private static Dictionary<string, string> ReadGolden()
    {
        var result = new Dictionary<string, string>();
        string all = File.ReadAllText(GoldenPath).Replace("\r", "");
        foreach (Match m in Regex.Matches(all, @"=== (\S+) ===\n(.*?)\n=== end ===", RegexOptions.Singleline))
            result[m.Groups[1].Value] = m.Groups[2].Value.Replace("^[", "\u001b");
        return result;
    }

    /// <summary>
    /// The bar's pieces with every whitespace run collapsed, and a color code that colors only whitespace (the
    /// continuation indent at the start of a row) dropped: equal when only the line breaks moved.
    /// </summary>
    private static string Pieces(string raw) =>
        Regex.Replace(Regex.Replace(raw, "(?<=\n)(\u001b\\[[0-9;]*m)+(?= )", ""), @"\s+", " ").Trim();

    [Fact]
    public void ShortCase_StatusLineIsByteIdentical_AndTheBarKeepsEveryPieceAndColor()
    {
        var golden = ReadGolden();
        foreach (var (name, raw) in ShortCases())
        {
            golden.Should().ContainKey(name);
            string before = golden[name];
            Capture($"chrome-short-{name}.txt", Strip(raw));
            if (name.Contains("-sr-"))
            {
                raw.Should().Be(before, $"{name}: the screen reader form is untouched");
                continue;
            }
            // the status line fits in the short case: everything before the bar's divider is unchanged, byte for byte
            int cutBefore = before.IndexOf('─'), cutAfter = raw.IndexOf('─');
            cutAfter.Should().BeGreaterThan(0);
            raw[..cutAfter].Should().Be(before[..cutBefore], $"{name}: a status line that fits is unchanged");
            string barRaw = raw[cutAfter..];
            Pieces(barRaw).Should().Be(Pieces(before[cutBefore..]), $"{name}: the bar keeps every piece, color and order");
            Strip(barRaw).Split('\n').Length.Should().BeGreaterThan(Strip(before[cutBefore..]).Split('\n').Length,
                $"{name}: the bar wraps (it is wider than 79 in every language)");
        }
    }

    [Fact]
    public void ScreenReader_StaysOnePlainLine()
    {
        foreach (var lang in new[] { "en", "hu" })
        {
            var rows = Rows(Strip(Chrome(lang, "inn", true, worst: true, screenReader: true))).Where(r => r.Length > 0).ToList();
            rows.Should().HaveCount(2, "one status line and one quick commands line");
            rows[0].Length.Should().BeGreaterThan(MaxWidth, "the screen reader line is not wrapped");
        }
    }

    [Fact]
    public void ClassicMainStreet_BarHasNoReturnKey_AndFits()
    {
        var (street, output, hero) = ClassicMainStreetLayout1114Tests.Rig(10, true, "", "", "");
        hero.Gold = GoldCap; hero.HP = Big; hero.MaxHP = Big; hero.Name2 = LongName;
        typeof(MainStreetLocation).GetMethod("DisplayLocation", F)!.Invoke(street, null);
        string plain = ClassicMainStreetLayout1114Tests.Plain(street, output);
        var bar = BarRows(plain, "en");
        bar.Count.Should().BeGreaterThan(1);
        EveryRowFits(string.Join("\n", bar), "classic Main Street Quick Commands");
        EveryRowFits(string.Join("\n", StatusRows(plain, "en")), "classic Main Street status line");
        string.Join(" ", bar).Should().NotContain("[R]");
    }

    // ---------- the invalid-choice hint (BaseLocation.ProcessChoice default) ----------

    private static string Hint(string lang, bool street) => InLanguage(lang, () =>
    {
        BaseLocation loc = street ? new StreetChrome() : new PlainChrome();
        var (l, output) = At(loc, Hero(true, worst: true));
        ((System.Threading.Tasks.Task<bool>)typeof(BaseLocation).GetMethod("ProcessChoice", F)!.Invoke(l, new object[] { "QQQ" })!)
            .GetAwaiter().GetResult();
        return Raw(l, output);
    });

    /// <summary>The hint as BaseLocation drew it before v1.2.3, call for call.</summary>
    private static string HintBefore(string lang, bool street) => InLanguage(lang, () =>
    {
        var output = new MemoryStream();
        var terminal = new TerminalEmulator(new MemoryStream(), output);
        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("base.invalid_choice", "QQQ"));
        terminal.SetColor("gray");
        terminal.Write($"{Loc.Get("base.try_hint")}: [");
        terminal.SetColor("bright_yellow");
        terminal.Write("%");
        terminal.SetColor("gray");
        terminal.Write("]");
        terminal.Write(Loc.Get("base.qc_status_suffix"));
        terminal.Write(", [");
        terminal.SetColor("bright_yellow");
        terminal.Write("*");
        terminal.SetColor("gray");
        terminal.Write("] ");
        terminal.Write(Loc.Get("base.qc_inventory"));
        if (!street)
        {
            terminal.Write(", [");
            terminal.SetColor("bright_yellow");
            terminal.Write("R");
            terminal.SetColor("gray");
            terminal.Write("]");
            terminal.Write(Loc.Get("base.qc_return_suffix"));
        }
        terminal.Write($", {Loc.Get("base.or")} [");
        terminal.SetColor("bright_yellow");
        terminal.Write("?");
        terminal.SetColor("gray");
        terminal.WriteLine($"] {Loc.Get("base.for_help")}");
        terminal.StreamWriterInternal?.Flush();
        return Encoding.UTF8.GetString(output.ToArray()).Replace("\r", "");
    });

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void InvalidChoiceHint_Fits_AndIsUnchangedWhereItFit(string lang)
    {
        foreach (bool street in new[] { false, true })
        {
            string raw = Hint(lang, street), before = HintBefore(lang, street);
            string plain = Strip(raw);
            Capture($"chrome-hint-{(street ? "street" : "inn")}-{lang}.txt", plain);
            EveryRowFits(plain, $"{lang} invalid-choice hint");
            plain.Contains("[R]").Should().Be(!street, "Main Street leaves out [R]");
            bool fitBefore = Rows(Strip(before)).All(r => r.Length <= MaxWidth);
            if (fitBefore) raw.Should().Be(before, $"{lang}: a hint that fits is drawn as before, byte for byte");
            else
            {
                Rows(plain).Count.Should().BeGreaterThan(Rows(Strip(before)).Count, $"{lang}: the hint wraps");
                foreach (var row in Rows(plain).Skip(2).Where(r => r.Length > 0))
                    row.Should().StartWith("  ").And.NotStartWith("  , [", "a continuation row drops the leading separator");
            }
        }
    }

    [Fact]
    public void NoLooseWidthBound_InTheChromeHelpers()
    {
        string src = File.ReadAllText(Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Scripts/Locations/BaseLocation.cs"));
        Regex.Matches(src, @"<=\s*80\b").Count.Should().Be(0, "the chrome wraps at UIHelper.WrapWidth (79)");
    }
}
