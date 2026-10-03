using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.5: UsurperHistorySystem, EndingsSystem and AmnesiaSystem in the player's language. The history pages
/// (a static lore screen; nothing in it is stored), the ending title boxes, the choose again notice, the
/// ascension rows, news and announcement, the prestige class rows and the dream's closing rows. What is
/// stored or matched stays English: the divine news marker, the abdication reason CastleLocation matches,
/// the god alignment the boons compare, the DISSOLVE word, the memory keys and flags and the saved enums.
/// Every changed row fits 79 columns in English and Hungarian with a 30-character name.
/// </summary>
[Collection("SharedGameSingletons")]
public class RestStory125Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private const int MaxWidth = 79;

    // GameConfig.MaxNameLength (30) characters, the longest name a player can have.
    private const string LongName = "Aranyszivu Hosszunevu Kalandor";

    private static readonly string[] AllLanguages = { "en", "es", "fr", "hu", "it" };

    private readonly MudServer? _oldServer = MudServer.Instance;

    public void Dispose()
    {
        typeof(MudServer).GetField("_instance", SNP)!.SetValue(null, _oldServer);
    }

    // ---------- helpers ----------

    private sealed class Screen
    {
        public TerminalEmulator Term = null!;
        public MemoryStream Output = new();
        public string Text
        {
            get
            {
                Term.StreamWriterInternal?.Flush();
                return Regex.Replace(Encoding.UTF8.GetString(Output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "");
            }
        }
    }

    private static Screen NewScreen(params string[] lines)
    {
        var s = new Screen();
        s.Term = new TerminalEmulator(new LineStream(lines.Concat(Enumerable.Repeat("", 12)), _ =>
        {
            s.Term.StreamWriterInternal?.Flush();
            s.Output.WriteByte((byte)'\n');
        }), s.Output);
        return s;
    }

    private static async Task<T> InLanguage<T>(string lang, Func<Task<T>> body, bool screenReader = false)
    {
        var prev = GameConfig.Language;
        bool sr = GameConfig.ScreenReaderMode;
        try
        {
            GameConfig.Language = lang;
            GameConfig.ScreenReaderMode = screenReader;
            return await body();
        }
        finally { GameConfig.Language = prev; GameConfig.ScreenReaderMode = sr; }
    }

    private static T InLang<T>(string lang, Func<T> body) => InLanguage(lang, () => Task.FromResult(body())).GetAwaiter().GetResult();

    private static string L(string lang, string key, params object[] args) => Loc.GetIn(lang, key, args);

    private static List<string> Rows(string text) => text.Replace("\r", "").Split('\n').ToList();

    private static void Capture(string name, string text)
    {
        var dir = Environment.GetEnvironmentVariable("USURPER_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, name), text);
    }

    private static void EveryRowFits(string text, string screen)
    {
        foreach (var row in Rows(text))
            row.Length.Should().BeLessOrEqualTo(MaxWidth, $"every row of the {screen} fits in {MaxWidth} columns: \"{row}\"");
    }

    /// <summary>
    /// The Hungarian screen holds none of the English text of these keys: the Hungarian value differs, and no
    /// line of the English value (split at its line breaks and placeholders, 5 letters or more) is on screen
    /// unless the Hungarian value has it too.
    /// </summary>
    private static void NoEnglishLeft(string huText, IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            string en = Loc.GetIn("en", key), hu = Loc.GetIn("hu", key);
            hu.Should().NotBe(en, $"{key} has a Hungarian text");
            foreach (var piece in Regex.Split(en, @"\{\d+(?:,-?\d+)?\}|\n").Select(p => p.Trim()).Where(p => p.Count(char.IsLetter) >= 5))
                if (!hu.Contains(piece))
                    huText.Should().NotContain(piece, $"{key} is shown in Hungarian");
        }
    }

    private static readonly string[] HistoryParagraphs =
    {
        "history.bbs_p1", "history.bbs_p2", "history.bbs_p3", "history.bbs_p4",
        "history.doors_p1", "history.doors_p2", "history.doors_p3", "history.doors_p4", "history.doors_p5",
        "history.origin_p1", "history.origin_p2", "history.origin_p3", "history.origin_p4",
        "history.creators_intro", "history.creators_together", "history.creators_honor",
        "history.remake_p1", "history.remake_p2", "history.remake_features", "history.remake_p3", "history.remake_welcome",
    };
    private static readonly string[] HistoryLists = { "history.doors_list", "history.origin_list", "history.remake_list" };
    private static readonly string[] HistoryBios = { "history.jakob_bio", "history.rick_bio", "history.dan_bio" };
    private static readonly string[] HistoryLabels =
    {
        "history.title", "history.bbs_heading", "history.doors_heading", "history.origin_heading", "history.creators_heading",
        "history.remake_years", "history.jakob_role", "history.rick_role", "history.dan_role", "history.jason_role",
    };

    private static IEnumerable<string> HistoryKeys =>
        HistoryParagraphs.Concat(HistoryLists).Concat(HistoryBios).Concat(HistoryLabels).Append("engine.press_enter_return");

    private static async Task<string> HistoryScreen(string lang, bool screenReader)
    {
        var s = NewScreen();
        await InLanguage(lang, async () => { await UsurperHistorySystem.Instance.ShowHistory(s.Term); return 0; }, screenReader);
        return s.Text;
    }

    private static async Task<string> DissolutionScreen(string lang, bool screenReader)
    {
        var s = NewScreen("");
        var player = new Character { Name1 = LongName, Name2 = LongName, Level = 100 };
        var m = typeof(EndingsSystem).GetMethod("PlayDissolutionEnding", F)!;
        await InLanguage(lang, async () => { await (Task)m.Invoke(new EndingsSystem(), new object[] { player, s.Term })!; return 0; }, screenReader);
        return s.Text;
    }

    private static async Task<string> DreamScreen(string lang, bool screenReader)
    {
        var s = NewScreen();
        var amnesia = new AmnesiaSystem();
        var player = new Character { Name1 = LongName, Name2 = LongName, Level = 100 };
        var m = typeof(AmnesiaSystem).GetMethod("PlayDreamSequence", F)!;
        await InLanguage(lang, async () => { await (Task)m.Invoke(amnesia, new object[] { s.Term, player })!; return 0; }, screenReader);
        return s.Text;
    }

    // ---------- tests ----------

    [Fact]
    public void LongName_IsTheLongestName()
    {
        LongName.Length.Should().Be(GameConfig.MaxNameLength);
    }

    [Fact]
    public async Task History_EveryRowFits_InEveryLanguage()
    {
        foreach (var lang in AllLanguages)
            foreach (var sr in new[] { false, true })
            {
                var text = await HistoryScreen(lang, sr);
                EveryRowFits(text, $"[{lang}{(sr ? " sr" : "")}] history");
                // The name boxes keep their 75 columns; the page frame keeps its 79.
                foreach (var row in Rows(text).Where(r => r.StartsWith("  |") || r.StartsWith("  +")))
                    row.Length.Should().Be(75, $"[{lang}] the name box keeps its width: \"{row}\"");
                foreach (var row in Rows(text).Where(r => r.StartsWith("|") || r.StartsWith("+")))
                    row.Length.Should().Be(79, $"[{lang}] the page frame keeps its width: \"{row}\"");
            }
    }

    [Fact]
    public async Task History_InHungarian_HasNoEnglishLeft()
    {
        foreach (var sr in new[] { false, true })
        {
            var text = await HistoryScreen("hu", sr);
            Capture($"history-hu{(sr ? "-sr" : "")}.txt", text);
            NoEnglishLeft(text, HistoryKeys);
            foreach (var key in HistoryParagraphs)
                text.Should().Contain(UsurperHistorySystem.ParagraphRows(L("hu", key))[0], $"{key} is shown in Hungarian");
            text.Should().Contain(L("hu", "history.title"));
            text.Should().Contain("Az Usurper megalkotója (1993)");
        }
    }

    [Fact]
    public async Task History_InEnglish_KeepsItsRows()
    {
        var en = await HistoryScreen("en", false);
        Capture("history-en.txt", en);
        var rows = Rows(en);
        foreach (var key in HistoryParagraphs)
            foreach (var line in L("en", key).Split('\n'))
                rows.Should().Contain("  " + line, $"{key} keeps its English row");
        foreach (var key in HistoryLists)
            foreach (var line in L("en", key).Split('\n'))
                rows.Should().Contain("    - " + line, $"{key} keeps its English row");
        foreach (var key in HistoryBios)
            foreach (var line in L("en", key).Split('\n'))
                rows.Should().Contain("  |  " + line.PadRight(69) + "|", $"{key} keeps its English box row");
        rows.Should().Contain("  Before the World Wide Web, before social media, before smartphones...");
        rows.Should().Contain("    - Slow connections (2400-14400 baud - slower than a single image today)");
        rows.Should().Contain("  |  Jakob created the original masterpiece in Turbo Pascal. His vision   |");
        rows.Should().Contain("+=============================================================================+");
    }

    [Fact]
    public async Task History_ScreenReader_IsPlainText_AndNamesStayAsWritten()
    {
        foreach (var lang in new[] { "en", "hu" })
        {
            var text = await HistoryScreen(lang, true);
            Capture($"history-{lang}-sr.txt", text);
            foreach (var row in Rows(text))
            {
                row.TrimStart().Should().NotStartWith("+", $"[{lang}] no frames for a screen reader");
                row.TrimStart().Should().NotStartWith("|", $"[{lang}] no frames for a screen reader");
            }
            text.Should().Contain(L(lang, "history.title"));
            text.Should().Contain("  " + L(lang, "history.sr_name_role", UsurperHistorySystem.JakobName, L(lang, "history.jakob_role")));
            text.Should().Contain("  " + L(lang, "history.sr_name_role", UsurperHistorySystem.JasonName, L(lang, "history.jason_role")));
        }
        var en = await HistoryScreen("en", true);
        en.Should().Contain("  JAKOB DANGARDEN - Creator of Usurper (1993)");
        var hu = Regex.Replace(await HistoryScreen("hu", false), @"\s+", " ");
        foreach (var name in new[] { "JAKOB DANGARDEN", "RICK PARRISH", "DANIEL ZINGARO", "JASON KNIGHT", "Trade Wars 2002", "Turbo Pascal", "GameSrv" })
            hu.Should().Contain(name, "proper names are not translated");
    }

    [Fact]
    public void EndingTitleBoxes_AreCentred_AndFitInEveryLanguage()
    {
        foreach (var lang in AllLanguages)
            foreach (var (title, sub) in new[] { ("ending.awakening_header", "ending.awakening_sr_title_2"), ("ending.dissolution_header", "ending.dissolution_sr_title_2") })
            {
                var rows = EndingsSystem.TitleBoxRows(L(lang, title), L(lang, sub));
                rows.Should().HaveCountGreaterOrEqualTo(4);
                foreach (var row in rows) row.Length.Should().Be(69, $"[{lang}] {title}: \"{row}\"");
                rows[1].Should().Contain(L(lang, title), "the spaced title keeps its spaces");
            }
        InLang("en", () => EndingsSystem.TitleBoxRows(L("en", "ending.awakening_header"), L("en", "ending.awakening_sr_title_2")))
            .Should().Equal(
                "╔═══════════════════════════════════════════════════════════════════╗",
                "║                T H E   T R U E   A W A K E N I N G                ║",
                "║           \"You are the Ocean, dreaming of being a wave\"           ║",
                "╚═══════════════════════════════════════════════════════════════════╝");
        // A subtitle longer than the box is wrapped inside it.
        var wrapped = EndingsSystem.TitleBoxRows("T I T L E", string.Join(" ", Enumerable.Repeat("word", 30)));
        wrapped.Count.Should().BeGreaterThan(4);
        foreach (var row in wrapped) row.Length.Should().Be(69);
    }

    [Fact]
    public async Task Dissolution_InHungarian_ShowsBothBoxesInHungarian()
    {
        var text = await DissolutionScreen("hu", false);
        Capture("dissolution-hu.txt", text);
        foreach (var row in Rows(text).Where(r => r.StartsWith("╔") || r.StartsWith("║") || r.StartsWith("╚")))
            row.Length.Should().Be(69, $"the title box keeps its width: \"{row}\"");
        NoEnglishLeft(text, new[] { "ending.dissolution_header", "ending.dissolution_sr_title_2", "ending.awakening_header", "ending.awakening_sr_title_2" });
        text.Should().Contain(L("hu", "ending.dissolution_header")).And.Contain(L("hu", "ending.awakening_header"));
        text.Should().NotContain("T H E   T R U E").And.NotContain("D I S S O L U T I O N");
        // The screen reader version has no box.
        var sr = await DissolutionScreen("hu", true);
        sr.Should().NotContain("╔").And.NotContain("║");
        sr.Should().Contain(L("hu", "ending.dissolution_sr_title_1"));
    }

    [Fact]
    public async Task ChooseAgain_IsInThePlayersLanguage_AndFits()
    {
        foreach (var lang in AllLanguages)
        {
            var s = NewScreen();
            await InLanguage(lang, async () => { await EndingsSystem.ChooseAgain(s.Term); return 0; });
            EveryRowFits(s.Text, $"[{lang}] choose again");
            s.Text.Should().Contain(L(lang, "ending.choose_again_must")).And.Contain(L(lang, "ending.choose_again_no_return"));
            s.Text.Should().Contain(L(lang, "ending.press_enter_choose_again").Trim());
            if (lang == "hu")
                NoEnglishLeft(s.Text, new[] { "ending.choose_again_must", "ending.choose_again_no_return", "ending.press_enter_choose_again" });
        }
        InLang("en", () => L("en", "ending.choose_again_must")).Should().Be("You must choose: ascend to godhood or begin the cycle anew.");
    }

    [Fact]
    public void AscensionRows_FitWithALongName_InEveryLanguage()
    {
        foreach (var lang in AllLanguages)
        {
            foreach (var key in new[] { "ending.immortal_team_dissolved", "ending.immortal_guild_lost" })
            {
                ("  " + L(lang, key)).Length.Should().BeLessOrEqualTo(MaxWidth, $"[{lang}] {key}");
                if (lang != "en") L(lang, key).Should().NotBe(L("en", key));
            }
            foreach (var align in new[] { "Light", "Dark", "Balance" })
            {
                var shown = InLang(lang, () => EndingsSystem.AlignmentIn(lang, align));
                ("  " + L(lang, "ending.immortal_ascended", LongName, shown)).Length.Should().BeLessOrEqualTo(MaxWidth);
                foreach (var row in Rows(Regex.Replace(EndingsSystem.AscensionBroadcast(lang, LongName, align), "\u001b\\[[0-9;?]*[A-Za-z]", "")))
                    row.Length.Should().BeLessOrEqualTo(MaxWidth, $"[{lang}] {row}");
                var news = InLang(lang, () => EndingsSystem.AscensionNews(LongName, LongName));
                news.Length.Should().BeLessOrEqualTo(MaxWidth * 2, "a news line wraps on the news screen");
            }
        }
        L("hu", "ending.immortal_team_dissolved").Should().Be("Halandó csapatod kötelékei feloldódnak az isteni felemelkedésben.");
    }

    [Fact]
    public void AscensionAnnouncement_IsInEachReadersLanguage()
    {
        // English stays the old announcement word for word.
        EndingsSystem.AscensionBroadcast("en", "Aeterna", "Light")
            .Should().Be("\r\n\x1b[1;33m  Aeterna, Lesser Spirit of Light\r\n  has ascended to the Divine Realm!\x1b[0m\r\n");
        var hu = EndingsSystem.AscensionBroadcast("hu", "Aeterna", "Light");
        hu.Should().Contain(L("hu", "ending.immortal_ascended", "Aeterna", "Fény")).And.Contain(L("hu", "ending.immortal_ascended_msg"));
        hu.Should().NotContain("Light").And.NotContain("Lesser Spirit");

        // Broadcast to two online players: each gets it in their own language.
        var server = (MudServer)RuntimeHelpers.GetUninitializedObject(typeof(MudServer));
        typeof(MudServer).GetField("<ActiveSessions>k__BackingField", F)!.SetValue(server, new ConcurrentDictionary<string, PlayerSession>());
        var huReader = Online(server, "hureader", "hu");
        var enReader = Online(server, "enreader", "en");
        server.BroadcastLocalized(lang => EndingsSystem.AscensionBroadcast(lang, "Aeterna", "Dark"));
        string Got(PlayerSession p) { p.IncomingMessages.TryDequeue(out var m).Should().BeTrue(); return m!; }
        Got(huReader).Should().Contain(L("hu", "temple.align.dark")).And.NotContain("Lesser Spirit");
        Got(enReader).Should().Contain("Lesser Spirit of Dark");
    }

    private static PlayerSession Online(MudServer server, string username, string lang)
    {
        var s = (PlayerSession)RuntimeHelpers.GetUninitializedObject(typeof(PlayerSession));
        typeof(PlayerSession).GetField("<Username>k__BackingField", F)!.SetValue(s, username);
        typeof(PlayerSession).GetField("<IncomingMessages>k__BackingField", F)!.SetValue(s, new ConcurrentQueue<string>());
        typeof(PlayerSession).GetField("<Spectators>k__BackingField", F)!.SetValue(s, new List<PlayerSession>());
        typeof(PlayerSession).GetField("_server", F)!.SetValue(s, server);
        s.IsInGame = true;
        var ctx = (SessionContext)RuntimeHelpers.GetUninitializedObject(typeof(SessionContext));
        ctx.Language = lang;
        typeof(PlayerSession).GetField("<Context>k__BackingField", F)!.SetValue(s, ctx);
        server.ActiveSessions[username] = s;
        return s;
    }

    [Fact]
    public void AscensionNews_IsInTheWritersLanguage_AndKeepsTheDivineMarker()
    {
        InLang("en", () => EndingsSystem.AscensionNews("Hero", "Aeterna")).Should().Be("[DIVINE] Hero has ascended to godhood as Aeterna!");
        var hu = InLang("hu", () => EndingsSystem.AscensionNews("Hero", "Aeterna"));
        hu.Should().StartWith(PantheonLocation.DivineNewsTag + " ", "the Pantheon news screen colours a line holding the marker");
        hu.Should().Contain(L("hu", "ending.news_ascended", "Hero", "Aeterna")).And.NotContain("has ascended");
    }

    [Fact]
    public void StoredWords_StayEnglish_AndStillMatchTheirReaders()
    {
        // The abdication reason: CastleLocation.ReignEndedNews matches it to write the godhood news.
        EndingsSystem.AscensionAbdicationReason.Should().Be("abdicated the throne to ascend to godhood");
        InLang("hu", () => CastleLocation.ReignEndedNews(LongName, EndingsSystem.AscensionAbdicationReason))
            .Should().Be(L("hu", "castle.news_reign_godhood", LongName));
        // The god alignment: stored English, compared by the boons; shown in the player's language.
        foreach (var lang in AllLanguages)
        {
            InLang(lang, () => EndingsSystem.AscensionAlignment(EndingType.Savior)).Should().Be("Light");
            InLang(lang, () => EndingsSystem.AscensionAlignment(EndingType.Usurper)).Should().Be("Dark");
            InLang(lang, () => EndingsSystem.AscensionAlignment(EndingType.Defiant)).Should().Be("Balance");
        }
        DivineBoonRegistry.GetAvailableBoons("Light").Count.Should()
            .BeGreaterThan(DivineBoonRegistry.GetAvailableBoons(L("hu", "temple.align.light")).Count, "the boons match the English word");
        EndingsSystem.AlignmentIn("hu", "Light").Should().Be(L("hu", "temple.align.light"));
        EndingsSystem.AlignmentIn("hu", "Unknown").Should().Be("Unknown");
    }

    [Fact]
    public void PrestigeClassRows_AreInThePlayersLanguage()
    {
        InLang("en", () => EndingsSystem.PrestigeClassRows(EndingType.TrueEnding)).Should().Equal(
            "Tidesworn (Holy)", "Wavecaller (Good)", "Cyclebreaker (Neutral)", "Abysswarden (Dark)", "Voidreaver (Evil)");
        InLang("en", () => EndingsSystem.PrestigeClassRows(EndingType.Savior)).Should().Equal("Tidesworn (Holy)", "Wavecaller (Good)");
        InLang("en", () => EndingsSystem.PrestigeClassRows(EndingType.Defiant)).Should().Equal("Cyclebreaker (Neutral)");
        InLang("en", () => EndingsSystem.PrestigeClassRows(EndingType.Usurper)).Should().Equal("Abysswarden (Dark)", "Voidreaver (Evil)");
        var hu = InLang("hu", () => EndingsSystem.PrestigeClassRows(EndingType.Secret));
        hu.Should().HaveCount(5);
        hu[0].Should().Be($"{L("hu", "class.tidesworn")} ({L("hu", "alignment.holy")})");
        string.Join(" ", hu).Should().NotContain("Holy").And.NotContain("Good").And.NotContain("Neutral").And.NotContain("Evil");
        foreach (var lang in AllLanguages)
            foreach (var row in InLang(lang, () => EndingsSystem.PrestigeClassRows(EndingType.Secret)))
                ("      " + row).Length.Should().BeLessOrEqualTo(MaxWidth);
    }

    [Fact]
    public async Task Dream_ClosingRows_AreInThePlayersLanguage()
    {
        var hu = await DreamScreen("hu", false);
        Capture("dream-hu.txt", hu);
        EveryRowFits(hu, "[hu] dream");
        hu.Should().Contain("  " + L("hu", "amnesia.dream_wake")).And.Contain(L("hu", "ending.press_enter").Trim());
        hu.Should().NotContain("You wake with tears").And.NotContain("Press Enter");
        var en = await DreamScreen("en", false);
        Rows(en).Should().Contain("  ...You wake with tears on your face.");
        en.Should().Contain("  Press Enter to continue...");
        foreach (var lang in AllLanguages)
            ("  " + L(lang, "amnesia.dream_wake")).Length.Should().BeLessOrEqualTo(MaxWidth);
    }

    [Fact]
    public void AmnesiaState_IsSavedAsEnums_AndMemoryKeysStayEnglish()
    {
        var amnesia = new AmnesiaSystem();
        var story = StoryProgressionSystem.Instance;
        bool had = story.HasStoryFlag("memory_the_decision");
        try
        {
            InLang("hu", () => { amnesia.RevealMajorMemory("the_decision"); return 0; });
            amnesia.RecoveredMemories.Should().Contain(MemoryFragment.TheDecision, "the English key is matched");
            story.HasStoryFlag("memory_the_decision").Should().BeTrue("the flag name is English");

            var json = JsonSerializer.Serialize(amnesia.Serialize());
            var back = new AmnesiaSystem();
            back.Deserialize(JsonSerializer.Deserialize<AmnesiaData>(json)!);
            back.RecoveredMemories.Should().BeEquivalentTo(amnesia.RecoveredMemories);
            back.ExperiencedDreams.Should().BeEquivalentTo(amnesia.ExperiencedDreams);
            back.RestCount.Should().Be(amnesia.RestCount);
        }
        finally { story.SetStoryFlag("memory_the_decision", had); }
    }

    [Fact]
    public void EndingRecords_KeepTheirEnglishNames()
    {
        // TriggerEnding records the ending under its enum name and sets ending_<name>_achieved; the Electron
        // payload carries the enum name too. None of them goes through Loc.
        var src = File.ReadAllText(Path.Combine(UsurperReborn.Tests.Localization.HardcodedTextScannerTests.RepoRoot(), "Scripts", "Systems", "EndingsSystem.cs"));
        src.Should().Contain("RecordChoice(\"final_ending\", ending.ToString(), 0)");
        src.Should().Contain("SetStoryFlag($\"ending_{ending.ToString().ToLower()}_achieved\", true)");
        src.Should().Contain("EndingType = ending.ToString(),");
        var story = new StoryProgressionSystem();
        InLang("hu", () => { story.RecordChoice("final_ending", EndingType.Savior.ToString(), 0); return 0; });
        story.MajorChoices["final_ending"].SelectedOption.Should().Be("Savior");
    }
}
