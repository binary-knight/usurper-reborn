using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using UsurperReborn.Tests.Localization;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.5: GameEngine, TerminalEmulator and SplashScreen in the player's language. The main menu rules, the
/// save list and save slot menu, the load failure recovery menu and the save repair, the void return, the
/// dead quest target notice, the catch-up summary, the spectate notices and realm announcements (each in the
/// reader's language), the door idle and time limit notices, the terminal prompts and the splash screen.
/// What is matched or stored stays English: the save backend's error text the bloat detector reads, the
/// SaveInfo.SaveType words FileSaveBackend writes, typed menu keys and chat commands. Every changed row fits
/// 79 columns in English and Hungarian with a 30-character name.
/// </summary>
[Collection("SharedGameSingletons")]
public class RestEngine125Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private const int MaxWidth = 79;

    // GameConfig.MaxNameLength (30) characters, the longest name a player can have.
    private const string LongName = "Aranyszivu Hosszunevu Kalandor";

    private static readonly string[] AllLanguages = { "en", "es", "fr", "hu", "it" };

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"usurper-rest-{Guid.NewGuid():N}");
    private readonly MudServer? _oldServer = MudServer.Instance;
    private readonly ISaveBackend _oldBackend = SaveSystem.Instance.Backend;
    private SqlSaveBackend? _db;

    public RestEngine125Tests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        typeof(MudServer).GetField("_instance", SNP)!.SetValue(null, _oldServer);
        // The tests that follow in the collection keep the save backend they had (TeamSlots1114Tests counts
        // team members through it).
        SaveSystem.InitializeWithBackend(_oldBackend);
        if (_db != null) SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
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

    private static GameEngine Engine(Screen s)
    {
        var engine = (GameEngine)RuntimeHelpers.GetUninitializedObject(typeof(GameEngine));
        typeof(GameEngine).GetField("terminal", F)!.SetValue(engine, s.Term);
        return engine;
    }

    private static async Task Run(object target, string method, params object?[] args)
    {
        var m = target.GetType().GetMethod(method, F)!;
        var r = m.Invoke(target, args);
        if (r is Task t) await t;
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

    /// <summary>A framed box row (starts with a box glyph) is at most 80 wide; every other row fits in 79.</summary>
    private static void EveryRowFits(IEnumerable<string> rows, string screen)
    {
        foreach (var row in rows)
        {
            if (row.Length > 0 && "╔║╚+|".IndexOf(row[0]) >= 0)
                row.Length.Should().BeLessOrEqualTo(MaxWidth + 1, $"the {screen} box keeps its width: \"{row}\"");
            else
                row.Length.Should().BeLessOrEqualTo(MaxWidth, $"every row of the {screen} fits in {MaxWidth} columns: \"{row}\"");
        }
    }

    private static void EveryRowFits(string text, string screen) => EveryRowFits(Rows(text), screen);

    /// <summary>The Hungarian screen holds none of the English text of these keys (each literal piece of the
    /// English value with 5 letters or more, unless the Hungarian value has it too).</summary>
    private static void NoEnglishLeft(string huText, IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            string en = Loc.GetIn("en", key), hu = Loc.GetIn("hu", key);
            hu.Should().NotBe(en, $"{key} has a Hungarian text");
            foreach (var piece in Regex.Split(en, @"\{\d+(?:,-?\d+)?\}").Select(p => p.Trim()).Where(p => p.Count(char.IsLetter) >= 5))
                if (!hu.Contains(piece))
                    huText.Should().NotContain(piece, $"{key} is shown in Hungarian");
        }
    }

    private static string Src(params string[] path) =>
        File.ReadAllText(Path.Combine(new[] { HardcodedTextScannerTests.RepoRoot(), "Scripts" }.Concat(path).ToArray()));

    private PlayerSession Online(MudServer server, string username, string lang, bool inGame = true)
    {
        var s = (PlayerSession)RuntimeHelpers.GetUninitializedObject(typeof(PlayerSession));
        typeof(PlayerSession).GetField("<Username>k__BackingField", F)!.SetValue(s, username);
        typeof(PlayerSession).GetField("<IncomingMessages>k__BackingField", F)!.SetValue(s, new ConcurrentQueue<string>());
        typeof(PlayerSession).GetField("<Spectators>k__BackingField", F)!.SetValue(s, new List<PlayerSession>());
        typeof(PlayerSession).GetField("_server", F)!.SetValue(s, server);
        s.IsInGame = inGame;
        var ctx = (SessionContext)RuntimeHelpers.GetUninitializedObject(typeof(SessionContext));
        ctx.Language = lang;
        typeof(PlayerSession).GetField("<Context>k__BackingField", F)!.SetValue(s, ctx);
        server.ActiveSessions[username] = s;
        return s;
    }

    private static MudServer NewServer()
    {
        var server = (MudServer)RuntimeHelpers.GetUninitializedObject(typeof(MudServer));
        typeof(MudServer).GetField("<ActiveSessions>k__BackingField", F)!
            .SetValue(server, new ConcurrentDictionary<string, PlayerSession>());
        typeof(MudServer).GetField("_instance", SNP)!.SetValue(null, server);
        return server;
    }

    private static List<string> Drain(PlayerSession s)
    {
        var rows = new List<string>();
        while (s.IncomingMessages.TryDequeue(out var m)) rows.Add(Regex.Replace(m, "\u001b\\[[0-9;?]*[A-Za-z]", ""));
        return rows;
    }

    // ---------- tests ----------

    [Fact]
    public void LongName_IsTheLongestName()
    {
        LongName.Length.Should().Be(GameConfig.MaxNameLength);
    }

    [Fact]
    public void MainMenuRules_AreTheOldEnglishRows_AndFitInEveryLanguage()
    {
        InLang("en", () => GameEngine.SectionRule("engine.section_play_visual"))
            .Should().Be("  ── PLAY ─────────────────────────────────────────────────────────────────");
        InLang("en", () => GameEngine.SectionRule("engine.section_info_visual"))
            .Should().Be("  ── INFO ─────────────────────────────────────────────────────────────────");
        InLang("en", () => GameEngine.SectionRule("engine.section_accessibility_visual"))
            .Should().Be("  ── ACCESSIBILITY ────────────────────────────────────────────────────────");
        foreach (var lang in AllLanguages)
            foreach (var key in new[] { "engine.section_play_visual", "engine.section_info_visual", "engine.section_accessibility_visual" })
            {
                var row = InLang(lang, () => GameEngine.SectionRule(key));
                row.Should().StartWith(L(lang, key));
                row.Length.Should().BeLessOrEqualTo(MaxWidth, $"[{lang}] {key}");
            }
        InLang("hu", () => GameEngine.SectionRule("engine.section_accessibility_visual")).Should().Contain("KISEGÍTŐ");
    }

    [Fact]
    public void SaveTypes_AreShownInThePlayersLanguage_AndTheBackendWordsStayEnglish()
    {
        // FileSaveBackend fills SaveInfo.SaveType with these English words; each one is mapped at display.
        var src = Src("Systems", "FileSaveBackend.cs");
        var words = Regex.Matches(src, @"SaveType = ([^\n]*)").SelectMany(m => Regex.Matches(m.Groups[1].Value, "\"([^\"]+)\"").Select(x => x.Groups[1].Value)).Distinct().ToList();
        words.Should().BeEquivalentTo(new[] { "Emergency", "Backup", "Autosave", "Manual Save", "Recovery" });
        foreach (var word in words)
        {
            InLang("en", () => GameEngine.SaveTypeLabel(word)).Should().Be(word, "the English label is the word itself");
            foreach (var lang in AllLanguages)
            {
                var label = InLang(lang, () => GameEngine.SaveTypeLabel(word));
                label.Length.Should().BeLessOrEqualTo(11, $"[{lang}] \"{label}\" leaves a space in the 12 column type slot");
                if (lang != "en") label.Should().NotBe(word, $"[{lang}] {word} is translated");
            }
        }
        InLang("hu", () => GameEngine.SaveTypeLabel("Manual Save")).Should().Be(L("hu", "engine.save_type_manual"));
        // The online backend's label is already localized and passes through.
        InLang("hu", () => GameEngine.SaveTypeLabel(L("hu", "save.type_online"))).Should().Be(L("hu", "save.type_online"));
    }

    [Fact]
    public void SaveSlotRows_FitInEveryLanguage()
    {
        string time = new DateTime(2026, 10, 3, 23, 59, 59).ToString("yyyy-MM-dd HH:mm:ss");
        foreach (var lang in AllLanguages)
        {
            foreach (var word in new[] { "Emergency", "Backup", "Autosave", "Manual Save", "Recovery" })
            {
                string type = InLang(lang, () => GameEngine.SaveTypeLabel(word)).PadRight(12);
                string unparsed = $"[10] {type}{L(lang, "engine.save_unparsed")} | {time}";
                unparsed.Length.Should().BeLessOrEqualTo(MaxWidth, $"[{lang}] {unparsed}");
                string detail = $"[10] {type}{L(lang, "engine.save_detail", 9999, 100, 999)} | {time}";
                detail.Length.Should().BeLessOrEqualTo(MaxWidth, $"[{lang}] {detail}");
            }
            if (lang != "en")
                (L(lang, "engine.save_tag_emergency") + L(lang, "engine.save_tag_recovery")).Should().NotContain("EMERGENCY SAVE").And.NotContain("RECOVERY");
        }
    }

    private async Task<string> LoadFailureScreen(string lang, string reason, bool assumeBloat, bool withFiles, bool screenReader = false)
    {
        _db ??= new SqlSaveBackend(Path.Combine(_dir, "saves.db"));
        SaveSystem.InitializeWithBackend(_db);
        foreach (var f in Directory.GetFiles(_dir, "rest_*.json")) File.Delete(f);
        if (withFiles)
        {
            File.WriteAllText(Path.Combine(_dir, "rest_backup.json"), "{}");
            File.WriteAllText(Path.Combine(_dir, "rest_autosave_1.json"), "{}");
            File.WriteAllText(Path.Combine(_dir, "emergency_autosave.json"), "{}");
        }
        var s = NewScreen("Q");
        var engine = Engine(s);
        await InLanguage(lang, async () => { await Run(engine, "ShowLoadFailureWithRecovery", "rest.json", reason, assumeBloat); return 0; }, screenReader);
        try { File.Delete(Path.Combine(_dir, "emergency_autosave.json")); } catch { }
        return s.Text;
    }

    [Fact]
    public async Task LoadFailure_WithRecoveryFiles_IsInHungarian_AndFits()
    {
        string hu = await LoadFailureScreen("hu", L("hu", "engine.reason_unparsed"), assumeBloat: true, withFiles: true);
        Capture("load-failure-hu.txt", hu);
        hu.Should().Contain(L("hu", "engine.lf_title").Trim());
        hu.Should().Contain(L("hu", "engine.lf_opt_load", 3).Trim());
        hu.Should().Contain(L("hu", "engine.lf_opt_repair").Trim(), "the listing could not parse the save, so it is assumed bloated");
        hu.Should().Contain(L("hu", "engine.lf_label_backup", "").Trim());
        hu.Should().Contain(L("hu", "engine.lf_label_autosave", "").Trim());
        hu.Should().Contain(L("hu", "engine.lf_label_emergency", "").Trim());
        NoEnglishLeft(hu, new[] { "engine.lf_title", "engine.lf_intro_1", "engine.lf_intro_2", "engine.lf_save_folder",
            "engine.lf_label_backup", "engine.lf_label_autosave", "engine.lf_label_emergency", "engine.lf_found", "engine.lf_options",
            "engine.lf_opt_load", "engine.lf_opt_repair", "engine.lf_opt_new", "engine.lf_opt_quit", "engine.reason_unparsed", "ui.your_choice" });
        EveryRowFits(Rows(hu).Where(r => !r.Contains(_dir)), "load failure menu (hu)");

        string en = await LoadFailureScreen("en", L("en", "engine.reason_unparsed"), assumeBloat: true, withFiles: true);
        Capture("load-failure-en.txt", en);
        en.Should().Contain("  SAVE LOAD FAILED").And.Contain("The game could not load your save. Your save file is still on disk --")
            .And.Contain("  [1-3] Try to load a recovery file").And.Contain("  [R]    Auto-repair the bloated save file (recommended)")
            .And.Contain("Backup from ").And.Contain("Your choice: ");
        EveryRowFits(Rows(en).Where(r => !r.Contains(_dir)), "load failure menu (en)");
    }

    [Fact]
    public async Task LoadFailure_WithoutRecoveryFiles_IsInHungarian_AndFits()
    {
        string hu = await LoadFailureScreen("hu", L("hu", "engine.reason_no_player"), assumeBloat: false, withFiles: false);
        Capture("load-failure-none-hu.txt", hu);
        NoEnglishLeft(hu, new[] { "engine.lf_none_found", "engine.lf_manual", "engine.lf_manual_1", "engine.lf_manual_2",
            "engine.lf_manual_3", "engine.lf_opt_new", "engine.lf_opt_quit", "engine.reason_no_player", "engine.lf_error" });
        hu.Should().NotContain(L("hu", "engine.lf_opt_repair").Trim(), "nothing says the save is bloated");
        EveryRowFits(Rows(hu).Where(r => !r.Contains(_dir)), "load failure menu without files (hu)");
        foreach (var lang in AllLanguages)
        {
            string text = await LoadFailureScreen(lang, L(lang, "engine.reason_unparsed"), assumeBloat: true, withFiles: false);
            EveryRowFits(Rows(text).Where(r => !r.Contains(_dir)), $"load failure menu ({lang})");
        }
    }

    [Fact]
    public async Task LoadFailure_TheBackendsEnglishErrorText_StillOffersTheRepair_InHungarian()
    {
        // The save backend's error text stays English (FileSaveBackend, SaveSystem); the bloat detector reads it.
        string hu = await LoadFailureScreen("hu", "Not enough memory to deserialize the save file.", assumeBloat: false, withFiles: false);
        hu.Should().Contain(L("hu", "engine.lf_opt_repair").Trim());
        hu.Should().Contain("Not enough memory to deserialize the save file.", "the backend's own error is shown as it came");
        // The Hungarian reason has none of the English words, so the flag carries the bloat assumption.
        string flagless = await LoadFailureScreen("hu", L("hu", "engine.reason_unparsed"), assumeBloat: false, withFiles: false);
        flagless.Should().NotContain(L("hu", "engine.lf_opt_repair").Trim());
    }

    [Fact]
    public async Task SaveRepair_IsInHungarian_AndFits()
    {
        _db ??= new SqlSaveBackend(Path.Combine(_dir, "saves.db"));
        SaveSystem.InitializeWithBackend(_db);
        File.WriteAllText(Path.Combine(_dir, "fix.json"), "{ not json");
        File.WriteAllText(Path.Combine(_dir, "fix_backup.json"), "{ not json");
        var s = NewScreen();
        var engine = Engine(s);
        await InLanguage("hu", async () => { await Run(engine, "RunSaveRepair", "fix.json", "x"); return 0; });
        string hu = s.Text;
        Capture("save-repair-hu.txt", hu);
        hu.Should().Contain(L("hu", "engine.rp_repairing", L("hu", "engine.rp_label_primary")));
        hu.Should().Contain(L("hu", "engine.rp_repairing", L("hu", "engine.rp_label_backup")));
        NoEnglishLeft(hu, new[] { "engine.rp_title", "engine.rp_intro_1", "engine.rp_intro_2", "engine.rp_intro_3", "engine.rp_intro_4",
            "engine.rp_label_primary", "engine.rp_label_backup", "engine.rp_all_failed_1", "engine.rp_all_failed_2", "engine.rp_all_failed_3" });
        EveryRowFits(Rows(hu).Where(r => !r.Contains(_dir)), "save repair (hu)");

        var en = NewScreen();
        await InLanguage("en", async () => { await Run(Engine(en), "RunSaveRepair", "fix.json", "x"); return 0; });
        en.Text.Should().Contain("  AUTOMATIC SAVE REPAIR").And.Contain("Repairing: Primary save...").And.Contain("Repairing: Backup...")
            .And.Contain("All repair attempts failed. The save may have damage beyond bloat");
        EveryRowFits(Rows(en.Text).Where(r => !r.Contains(_dir)), "save repair (en)");
        foreach (var lang in AllLanguages)
            foreach (var key in new[] { "engine.rp_intro_1", "engine.rp_intro_2", "engine.rp_intro_3", "engine.rp_intro_4",
                "engine.rp_all_failed_1", "engine.rp_all_failed_2", "engine.rp_all_failed_3", "engine.lf_intro_1", "engine.lf_intro_2",
                "engine.lf_manual_1", "engine.lf_manual_2", "engine.lf_manual_3", "engine.lf_opt_new", "engine.lf_opt_repair" })
                L(lang, key).Length.Should().BeLessOrEqualTo(MaxWidth, $"[{lang}] {key}");
    }

    [Fact]
    public void QuestAndVoidRows_FitWithALongNameAndALongTitle()
    {
        string title = new string('x', 20) + " " + new string('y', 25) + " " + new string('z', 20);
        foreach (var lang in new[] { "en", "hu" })
        {
            var rows = InLang(lang, () => GameEngine.WrapRows(L(lang, "engine.quest_perished", LongName))
                .Concat(GameEngine.WrapRows(L(lang, "engine.quest_auto_completed", title, 2_000_000_000L.ToString("N0")))).ToList());
            EveryRowFits(rows, $"dead quest target notice ({lang})");
            string.Join(" ", rows.Select(r => r.Trim())).Should().Contain(title);
            foreach (var key in new[] { "engine.void_awaken", "engine.void_release" })
                L(lang, key).Length.Should().BeLessOrEqualTo(MaxWidth);
            L(lang, "engine.void_levels", 5, 100).Length.Should().BeLessOrEqualTo(MaxWidth);
            L(lang, "engine.void_gold", 2_000_000_000L.ToString("N0")).Length.Should().BeLessOrEqualTo(MaxWidth);
        }
        InLang("en", () => GameEngine.WrapRows(L("en", "engine.quest_perished", "Grim"))).Should().Equal("  Quest Update: Grim has perished.");
        L("hu", "engine.quest_perished", "Grim").Should().NotContain("Quest Update");
    }

    [Fact]
    public void SpectateNotices_AreInTheWatchedPlayersLanguage()
    {
        var server = NewServer();
        var hu = Online(server, "watched_hu", "hu");
        var en = Online(server, "watched_en", "en");
        foreach (var (s, lang) in new[] { (hu, "hu"), (en, "en") })
        {
            InLang("fr", () => { GameEngine.NotifySpectateRequest(s, LongName); GameEngine.NotifySpectateStarted(s, LongName); GameEngine.NotifySpectateStopped(s, LongName); return 0; });
            var rows = Drain(s);
            rows.Should().Equal(
                MudServer.NoticeRows(L(lang, "engine.spectate_request", LongName), "* ")
                .Concat(MudServer.NoticeRows(L(lang, "engine.spectate_request_hint"), "* "))
                .Concat(MudServer.NoticeRows(L(lang, "engine.spectate_started", LongName), "* "))
                .Concat(MudServer.NoticeRows(L(lang, "mud.stopped_watching", LongName), "* ")));
            EveryRowFits(rows, $"spectate notices ({lang})");
            string.Join("\n", rows).Should().Contain("/accept").And.Contain("/deny");
        }
        Drain(hu).Should().BeEmpty();
        InLang("en", () => { GameEngine.NotifySpectateRequest(en, "Grim"); return 0; });
        Drain(en).Should().Equal("  * Grim wants to watch your session (Spectator Mode).", "  * Type /accept to allow or /deny to refuse.");
    }

    [Fact]
    public void RealmAnnouncements_AreInEachReadersLanguage_AndFit()
    {
        var server = NewServer();
        var me = Online(server, "newcomer", "fr");
        var hu = Online(server, "reader_hu", "hu");
        var en = Online(server, "reader_en", "en");
        var lobby = Online(server, "lobby", "hu", inGame: false);
        InLang("fr", () =>
        {
            GameEngine.AnnounceToRealm(server, "NewComer", "1;33", lang => Loc.GetIn(lang, "engine.entered_realm", LongName, "Web"));
            GameEngine.AnnounceToRealm(server, "NewComer", "1;36", lang => Loc.GetIn(lang, "engine.new_adventurer_welcome", LongName));
            return 0;
        });
        Drain(me).Should().BeEmpty("the newcomer is not told about themselves");
        Drain(lobby).Should().BeEmpty("a player still at the login menu gets no broadcast");
        var huRows = Drain(hu);
        string.Join(" ", huRows.Select(r => r.Trim())).Should().Contain(L("hu", "engine.entered_realm", LongName, "Web"))
            .And.Contain(L("hu", "engine.new_adventurer_welcome", LongName));
        EveryRowFits(huRows, "realm announcements (hu)");
        var enRows = Drain(en);
        enRows[0].Should().Be($"  {LongName} has entered the realm. [Web]");
        string.Join(" ", enRows.Skip(1).Select(r => r.Trim())).Should().Be($"*** {LongName} is a new adventurer! Type /gos to welcome them! ***");
        EveryRowFits(enRows, "realm announcements (en)");
    }

    [Fact]
    public async Task CatchUpScreens_AreInHungarian()
    {
        var s = NewScreen();
        await InLanguage("hu", async () => { await Run(Engine(s), "ShowCatchUpSummary", new List<string>()); return 0; });
        s.Text.Should().Contain(L("hu", "engine.realm_quiet").Trim());
        NoEnglishLeft(s.Text, new[] { "engine.realm_quiet" });

        var t = NewScreen();
        var many = Enumerable.Range(1, 30).Select(i => $"Esemény {i}").ToList();
        await InLanguage("hu", async () => { await Run(Engine(t), "ShowCatchUpSummary", many); return 0; });
        t.Text.Should().Contain(L("hu", "engine.catchup_more", 30 - GameConfig.CatchUpMaxEventsPerCategory).Trim());
        NoEnglishLeft(t.Text, new[] { "engine.catchup_more" });
        foreach (var lang in AllLanguages)
        {
            L(lang, "engine.away_header", L(lang, "engine.away_minutes", 59)).Length.Should().BeLessOrEqualTo(MaxWidth);
            L(lang, "engine.away_world").Length.Should().BeLessOrEqualTo(MaxWidth);
        }
        L("en", "engine.away_header", L("en", "engine.away_days", "2.5")).Should().Be("  While you were away (2.5 days)...");
    }

    [Fact]
    public void DoorNotices_AreInThePlayersLanguage_AndFit()
    {
        InLang("en", () => TerminalEmulator.FramedRows(Loc.Get("ui.idle_warning")))
            .Should().Equal("*** WARNING: You will be disconnected in 1 minute due to inactivity! ***");
        InLang("en", () => TerminalEmulator.FramedRows(Loc.Get("ui.idle_timeout_title"))).Should().Equal("*** IDLE TIMEOUT ***");
        InLang("en", () => TerminalEmulator.FramedRows(Loc.Get("ui.time_limit_title"))).Should().Equal("*** TIME LIMIT REACHED ***");
        foreach (var lang in AllLanguages)
            foreach (var key in new[] { "ui.idle_warning", "ui.idle_timeout_title", "ui.time_limit_title" })
            {
                var rows = InLang(lang, () => TerminalEmulator.FramedRows(Loc.Get(key)));
                EveryRowFits(rows, $"{key} ({lang})");
                string.Join(" ", rows).Should().Contain(L(lang, key));
            }
        InLang("hu", () => TerminalEmulator.FramedRows(Loc.Get("ui.idle_warning"))).Single().Should().NotContain("WARNING");
        foreach (var lang in AllLanguages)
            foreach (var key in new[] { "ui.auto_saving_disconnecting", "ui.session_expired" })
                L(lang, key).Length.Should().BeLessOrEqualTo(MaxWidth);
        L("hu", "ui.idle_no_input", 120).Should().NotContain("No input");
    }

    [Fact]
    public void TitleBox_KeepsTheEnglishRows_AndCentresALongTranslation()
    {
        InLang("en", () => TerminalEmulator.TitleBoxRow(Loc.Get("ui.title_tagline_1"), 26))
            .Should().Be("║                          A Classic BBS Door Game Remake                     ║");
        InLang("en", () => TerminalEmulator.TitleBoxRow(Loc.Get("ui.title_tagline_2"), 30))
            .Should().Be("║                              With Advanced NPC AI                           ║");
        foreach (var lang in AllLanguages)
        {
            var row = InLang(lang, () => TerminalEmulator.TitleBoxRow(Loc.Get("ui.title_tagline_1"), 26));
            row.Length.Should().Be(79, $"[{lang}] the title box row keeps its width");
            row.Should().Contain(L(lang, "ui.title_tagline_1"));
            InLang(lang, () => TerminalEmulator.TitleBoxRow(Loc.Get("ui.title_tagline_2"), 30)).Length.Should().Be(79);
        }
        TerminalEmulator.TitleBoxRow(new string('x', 70), 26).Should().Be("║" + new string(' ', 3) + new string('x', 70) + new string(' ', 4) + "║");
    }

    [Fact]
    public async Task TerminalPrompts_AreInHungarian()
    {
        var s = NewScreen("9", "0");
        int pick = await InLanguage("hu", () => s.Term.GetMenuChoice(new List<MenuOption> { new() { Key = "A", Text = "Egy" } }));
        pick.Should().Be(-1, "0 is still the typed key for going back");
        s.Text.Should().Contain($"0. {L("hu", "ui.go_back")}").And.Contain(L("hu", "engine.invalid_choice"));
        NoEnglishLeft(s.Text, new[] { "ui.go_back", "engine.invalid_choice" });

        var n = NewScreen("abc", "99", "5");
        int value = await InLanguage("hu", () => n.Term.GetNumberInput("", 1, 10));
        value.Should().Be(5);
        n.Text.Should().Contain(L("hu", "ui.enter_valid_number")).And.Contain(L("hu", "ui.number_between", 1, 10));
        NoEnglishLeft(n.Text, new[] { "ui.enter_valid_number", "ui.number_between" });

        var w = NewScreen("");
        await InLanguage("hu", async () => { await w.Term.WaitForKeyPress(); return 0; });
        w.Text.Should().Contain(L("hu", "ui.press_enter"));
        NoEnglishLeft(w.Text, new[] { "ui.press_enter" });
        var we = NewScreen("");
        await InLanguage("en", async () => { await we.Term.WaitForKeyPress(); return 0; });
        we.Text.Should().Contain("Press Enter to continue...");

        var st = NewScreen();
        InLang("hu", () => { st.Term.SetStatusLine("x"); return 0; });
        st.Text.Should().Contain(L("hu", "ui.status_line", "x"));
    }

    [Fact]
    public async Task SplashScreen_IsInThePlayersLanguage_AndFits()
    {
        foreach (var sr in new[] { true, false })
        {
            var s = NewScreen("");
            await InLanguage("hu", async () => { await SplashScreen.Show(s.Term); return 0; }, screenReader: sr);
            s.Text.Should().Contain(L("hu", "splash.press_any_key"));
            if (sr) s.Text.Should().Contain(L("hu", "splash.tagline").Trim());
            NoEnglishLeft(s.Text, sr ? new[] { "splash.tagline", "splash.press_any_key" } : new[] { "splash.press_any_key" });
            Capture($"splash-hu-{(sr ? "sr" : "art")}.txt", s.Text);
        }
        foreach (var lang in AllLanguages)
        {
            var s = NewScreen("");
            await InLanguage(lang, async () => { await SplashScreen.Show(s.Term); return 0; }, screenReader: true);
            EveryRowFits(s.Text, $"splash ({lang})");
        }
        var en = NewScreen("");
        await InLanguage("en", async () => { await SplashScreen.Show(en.Term); return 0; }, screenReader: true);
        en.Text.Should().Contain("  USURPER REBORN").And.Contain("  A modern recreation of the classic 1993 BBS door game").And.Contain("Press any key...");
    }

    [Fact]
    public void NewsAndFeedTexts_HaveEveryLanguage_AndTheElectronIdsStayEnglish()
    {
        var src = Src("Core", "GameEngine.cs");
        src.Should().Contain("Title = Loc.Get(\"engine.feed_world_news\"), Icon = \"scroll\"");
        src.Should().Contain("Title = Loc.Get(\"engine.feed_messages\"), Icon = \"letter\"");
        src.Should().Contain("Type = \"pvp_attack\"");
        foreach (var lang in AllLanguages)
        {
            L(lang, "engine.news_new_adventurer", LongName, "x").Should().Contain(LongName);
            L(lang, "engine.feed_pvp_won", LongName).Should().Contain(LongName);
        }
        L("hu", "engine.feed_world_news").Should().NotBe("World News");
        L("en", "engine.news_new_adventurer", "Grim", "Warrior").Should().Be("A new adventurer arrives! Grim the Warrior begins their journey.");
    }

    // ---------- catch-up buckets (leftover from the online systems piece) ----------

    private static readonly (string Key, object[] Args, int Bucket)[] CatchUpNews =
    {
        ("news.death", new object[] { "Xaver", "Yrsa", "Zub" }, 0),
        ("news.natural_death", new object[] { "Xaver", "Xq", 80 }, 0),
        ("news.birth", new object[] { "Xaver", "Yrsa", "Zub" }, 1),
        ("news.coming_of_age", new object[] { "Zub", "Xaver", "Yrsa" }, 1),
        ("news.royal_proclaims", new object[] { "Xaver", "Xq" }, 2),
        ("castle.news_abdicated", new object[] { "Xaver" }, 2),
        ("castle.news_throne_seized", new object[] { "Xaver", "Xq" }, 2),
        ("street_encounter.news.throne_guards", new object[] { "Xaver", "Yrsa" }, 2),
        ("news.marriage", new object[] { "Xaver", "Yrsa", "Xq" }, 3),
        ("news.divorce", new object[] { "Xaver", "Yrsa" }, 3),
        ("news.affair", new object[] { "Xaver", "Yrsa" }, 3),
        ("news.birthday", new object[] { "Xaver", "Xq", 30, "" }, 3),
        ("dungeon.settlement_founded_news", new object[0], 4),
        ("settlement.news_proposes", new object[] { "Xaver", "Xq" }, 4),
        ("settlement.news_settler_joined", new object[] { "Xaver" }, 4),
        ("engine.news_new_adventurer", new object[] { "Xaver", "Xq" }, 5),
        ("level_master.reached_level_news", new object[] { "Xaver", 12 }, 5),
    };

    [Fact]
    public void CatchUpNews_LandsInItsBucket_InEveryLanguage()
    {
        foreach (var lang in AllLanguages)
            foreach (var (key, args, bucket) in CatchUpNews)
            {
                string news = L(lang, key, args);
                InLang("en", () => GameEngine.CatchUpBucket(news)).Should().Be(bucket, $"[{lang}] {key}: \"{news}\"");
            }
        GameEngine.CatchUpBucket("Something odd happened.").Should().Be(5, "anything else is a world event");
    }

    [Fact]
    public async Task CatchUpSummary_PutsHungarianNewsUnderItsHeadings()
    {
        var events = CatchUpNews.Select(n => L("hu", n.Key, n.Args)).Prepend($"═══ {L("hu", "news.new_day")} ═══").ToList();
        var s = NewScreen();
        await InLanguage("hu", async () => { await Run(Engine(s), "ShowCatchUpSummary", events); return 0; });
        Capture("catch-up-hu.txt", s.Text);
        var rows = Rows(s.Text);
        int Heading(string key) => rows.FindIndex(r => r.Trim() == $"{L("hu", key)}:");
        var headings = new[] { "engine.cat_deaths", "engine.cat_births", "engine.cat_royal", "engine.cat_love", "engine.cat_outskirts", "engine.cat_world_events" }
            .Select(Heading).ToList();
        headings.Should().OnlyContain(i => i >= 0, "every bucket has Hungarian news");
        headings.Should().BeInAscendingOrder();
        foreach (var (key, args, bucket) in CatchUpNews)
        {
            int row = rows.FindIndex(r => r.Contains(L("hu", key, args)));
            row.Should().BeGreaterThan(headings[bucket], key);
            if (bucket < 5) row.Should().BeLessThan(headings[bucket + 1], $"{key} is under {headings[bucket]}");
        }
        s.Text.Should().NotContain(L("hu", "news.new_day"), "the New Day marker is still skipped");
    }

    // ---------- the save list row (leftover from the online systems piece) ----------

    private static List<string> SaveRowText(string lang, int number, bool emergency, string saveType)
    {
        var pieces = InLang(lang, () => GameEngine.SaveListRow(number, LongName,
            emergency ? Loc.Get("engine.save_tag_emergency") : $" ({GameConfig.GetLocalizedClassNameFromString("MysticShaman")})",
            emergency ? "bright_red" : "cyan", Loc.Get("engine.save_slot_level", 100), GameEngine.SaveTypeLabel(saveType),
            false, "2026-10-03 23:59:59"));
        return string.Concat(pieces.Select(p => p.Text)).Split('\n').ToList();
    }

    [Fact]
    public void SaveListRow_FitsWithALongName_InEnglishAndHungarian()
    {
        foreach (var lang in AllLanguages)
            foreach (var emergency in new[] { false, true })
                foreach (var type in new[] { "Manual Save", "Recovery", "Online Save" })
                    foreach (var n in new[] { 1, 10 })
                    {
                        var rows = SaveRowText(lang, n, emergency, type == "Online Save" ? Loc.GetIn(lang, "save.type_online") : type);
                        EveryRowFits(rows, $"save list ({lang})");
                        string.Join(" ", rows).Should().Contain(LongName).And.Contain("2026-10-03 23:59:59");
                    }
        // The English row with the long name: the type and time continue under the name.
        var en = SaveRowText("en", 1, false, "Manual Save");
        en.Should().Equal($"[1] {LongName} (Mystic Shaman) - Level 100", "    Manual Save | 2026-10-03 23:59:59");
        // A short name keeps the one row it always had.
        var shortRow = InLang("en", () => GameEngine.SaveListRow(2, "Grim", " (Warrior)", "cyan", Loc.Get("engine.save_slot_level", 7),
            GameEngine.SaveTypeLabel("Autosave"), true, "2026-10-03 23:59:59"));
        string.Concat(shortRow.Select(p => p.Text)).Should().Be("[2] Grim (Warrior) - Level 7 | Autosave | 2026-10-03 23:59:59");
        foreach (var lang in new[] { "en", "hu" })
            InLang(lang, () => GameEngine.WrapRows(Loc.Get("engine.save_slot_sr_display", 10, LongName, "Mystic Shaman", 100,
                Loc.Get("save.type_online"), "2026-10-03 23:59:59") + Loc.Get("engine.save_tag_emergency")))
                .Should().OnlyContain(r => r.Length <= MaxWidth, $"[{lang}] the screen reader row wraps");
    }

    // ---------- rows of other engine screens that ran past 79 columns ----------

    [Fact]
    public async Task StorySupportAndBbsScreens_FitInEveryLanguage()
    {
        foreach (var lang in AllLanguages)
            foreach (var method in new[] { "ShowStoryIntroduction", "ShowSupportPage", "ShowBBSList" })
            {
                var s = NewScreen();
                await InLanguage(lang, async () => { await Run(Engine(s), method); return 0; });
                EveryRowFits(s.Text, $"{method} ({lang})");
                if (lang == "en" && method == "ShowStoryIntroduction")
                    s.Text.Should().Contain(Loc.GetIn("en", "engine.story_golden_1"), "an English row that fits is written as it is");
            }
    }

    [Fact]
    public void LongEngineNotices_WrapAt79()
    {
        var notices = new (string Key, object[] Args)[]
        {
            ("engine.alt_level_required", new object[] { 25 }),
            ("engine.inheritance_waiting", new object[] { 12 }),
            ("engine.inheritance_overflow", new object[] { 12 }),
            ("engine.legacy_claimed", new object[] { LongName, 100, 2_000_000_000L.ToString("N0") }),
            ("engine.spectator_consent", new object[0]),
            ("engine.innkeeper_quote", new object[0]),
        };
        var src = Src("Core", "GameEngine.cs");
        foreach (var (key, args) in notices)
        {
            src.Should().MatchRegex($"(WriteRows|WrapRows)\\(Loc\\.Get\\(\"{Regex.Escape(key)}\"", $"{key} is written through WrapRows");
            foreach (var lang in AllLanguages)
            {
                var rows = GameEngine.WrapRows(L(lang, key, args));
                EveryRowFits(rows, $"{key} ({lang})");
                string.Join(" ", rows).Split(' ', StringSplitOptions.RemoveEmptyEntries).Should()
                    .Equal(L(lang, key, args).Split(' ', StringSplitOptions.RemoveEmptyEntries), $"[{lang}] {key} loses no words");
            }
        }
        src.Should().Contain("WriteRows($\"  {Loc.Get(\"aldric_quest.need_someone\")}\");");
        foreach (var lang in AllLanguages)
            L(lang, "engine.use_resurrection_prompt").Length.Should().BeLessOrEqualTo(MaxWidth, $"[{lang}] the resurrection question fits");
    }

    // ---------- NPC activity news (review follow-up) ----------

    private static readonly string[] NpcNewsKeys =
    {
        "engine.npc_news_lurking", "engine.npc_news_threatened", "engine.npc_news_watched", "engine.npc_news_lost_child",
        "engine.npc_news_donated", "engine.npc_news_protected", "engine.npc_news_partners", "engine.npc_news_duel",
        "engine.npc_news_tomes", "engine.npc_news_target"
    };

    [Fact]
    public void NpcActivityNews_IsWrittenInTheWritersLanguage_AndSortsUnderWorldEvents()
    {
        var npc = new NPC { Name1 = "Xaver", Name2 = "Xaver", Class = CharacterClass.Sage, Darkness = 1000, Chivalry = 0, CurrentLocation = "Magic Shop" };
        var engine = (GameEngine)RuntimeHelpers.GetUninitializedObject(typeof(GameEngine));
        foreach (var lang in new[] { "hu", "en" })
        {
            var written = InLang(lang, () =>
            {
                var buffer = new List<string>();
                NewsSystem.Instance.SetCatchUpBuffer(buffer);
                try
                {
                    for (int seed = 0; seed < 40; seed++)
                        typeof(GameEngine).GetMethod("GenerateNPCNews", F)!.Invoke(engine, new object[] { npc, new Random(seed) });
                }
                finally { NewsSystem.Instance.ClearCatchUpBuffer(); }
                return buffer.Distinct().ToList();
            });
            var expected = new[] { "engine.npc_news_lurking", "engine.npc_news_threatened", "engine.npc_news_watched", "engine.npc_news_tomes" }
                .Select(k => L(lang, k, "Xaver")).ToList();
            written.Should().BeEquivalentTo(expected, $"[{lang}] dark sage news in the writer's language");
        }
        // Every fragment sorts under World Events in every language, as the English ones do.
        foreach (var lang in AllLanguages)
        {
            foreach (var key in NpcNewsKeys)
                GameEngine.CatchUpBucket(L(lang, key, "Xaver")).Should().Be(5, $"[{lang}] {key}");
            GameEngine.CatchUpBucket(L(lang, "engine.npc_news_seen_at", "Xaver", InLang(lang, () => GameEngine.NpcPlaceLabel("Main Street"))))
                .Should().Be(5, $"[{lang}] seen at Main Street");
        }
        // English keeps the stored place text; another language shows the place's own name.
        InLang("en", () => L("en", "engine.npc_news_seen_at", "Xaver", GameEngine.NpcPlaceLabel("Inn"))).Should().Be("Xaver was seen at the Inn");
        InLang("hu", () => GameEngine.NpcPlaceLabel("Magic Shop")).Should().Be(L("hu", "location.name.MagicShop"));
        InLang("hu", () => GameEngine.NpcPlaceLabel("The Divine Realm")).Should().Be("The Divine Realm", "a place with no key is shown as stored");
    }

    [Fact]
    public void CatchUpWords_MatchAtTheStartOfAWord()
    {
        GameEngine.CatchUpBucket(L("en", "engine.npc_news_lurking", "Xaver")).Should().Be(5, "\"lurking\" is not \"king\"");
        GameEngine.CatchUpBucket("King Xaver proclaims: taxes").Should().Be(2);
        GameEngine.CatchUpBucket("Xaver blessed the kingdom").Should().Be(2);
    }

    // ---------- "Unknown" fallbacks (review follow-up) ----------

    [Fact]
    public void UnknownFallbacks_AreShownInTheReadersLanguage_AndTheStoredTypeStaysEnglish()
    {
        var entry = System.Text.Json.Nodes.JsonNode.Parse("{\"result\":\"attacker_won\"}")!;
        InLang("hu", () => GameEngine.SleepAttackerName(entry)).Should().Be(L("hu", "combat.unknown_name")).And.NotBe("Unknown");
        InLang("en", () => GameEngine.SleepAttackerName(entry)).Should().Be("Unknown");
        InLang("hu", () => GameEngine.SleepAttackerName(System.Text.Json.Nodes.JsonNode.Parse("{\"attacker\":\"Grim\"}")!)).Should().Be("Grim");

        GameEngine.ConnectionLabel("hu", "Unknown").Should().Be(L("hu", "combat.unknown_name"));
        GameEngine.ConnectionLabel("hu", null).Should().Be(L("hu", "combat.unknown_name"));
        GameEngine.ConnectionLabel("hu", "Local").Should().Be(L("hu", "chat.via_local"));
        GameEngine.ConnectionLabel("hu", "SSH").Should().Be("SSH");
        foreach (var t in new[] { "Unknown", "Local", "Web", "SSH", "MUD", "BBS", "Steam", "Electron" })
            GameEngine.ConnectionLabel("en", t).Should().Be(t, "English shows the stored type as before");

        // The realm announcement shows it in each reader's language; the stored value is untouched.
        var server = NewServer();
        var hu = Online(server, "reader_hu", "hu");
        var en = Online(server, "reader_en", "en");
        string stored = "Unknown";
        GameEngine.AnnounceToRealm(server, "newcomer", "1;33", lang => Loc.GetIn(lang, "engine.entered_realm", "Grim", GameEngine.ConnectionLabel(lang, stored)));
        Drain(hu).Single().Should().Be("  " + L("hu", "engine.entered_realm", "Grim", L("hu", "combat.unknown_name")));
        Drain(en).Single().Should().Be("  Grim has entered the realm. [Unknown]");
        stored.Should().Be("Unknown");
        Src("Core", "GameEngine.cs").Should().Contain("ConnectionLabel(lang, connType)").And.Contain("ConnectionLabel(lang, ngConnType)")
            .And.Contain("var connType = ctx?.ConnectionType ?? \"Unknown\";", "the type passed to SwitchIdentity is stored, so it stays English");
    }

    // ---------- yes/no letters (review follow-up) ----------

    [Fact]
    public async Task ConfirmPrompt_ShowsOnlyLettersTheAnswerAccepts_InTheReadersLanguage()
    {
        foreach (var lang in AllLanguages)
            foreach (var key in new[] { "ui.yn_default_yes", "ui.yn_default_no" })
            {
                var letters = Regex.Match(L(lang, key), @"\((\w)/(\w)\)");
                letters.Success.Should().BeTrue($"[{lang}] {key} shows two letters");
                GameConfig.IsAffirmative(letters.Groups[1].Value).Should().BeTrue($"[{lang}] {key} yes letter is accepted");
                GameConfig.IsNegative(letters.Groups[2].Value).Should().BeTrue($"[{lang}] {key} no letter is accepted");
                (key == "ui.yn_default_yes" ? letters.Groups[1].Value : letters.Groups[2].Value).Should()
                    .Be((key == "ui.yn_default_yes" ? letters.Groups[1].Value : letters.Groups[2].Value).ToUpperInvariant(), "the default is the capital");
            }

        var hu = NewScreen("I");
        (await InLanguage("hu", () => hu.Term.ConfirmAsync("Biztos?", false))).Should().BeTrue("I (Igen) is a yes");
        hu.Text.Should().Contain("Biztos? (i/N): ").And.NotContain("(y/N)");
        var huDefault = NewScreen("");
        (await InLanguage("hu", () => huDefault.Term.ConfirmAsync("Biztos?", true))).Should().BeTrue();
        huDefault.Text.Should().Contain("Biztos? (I/n): ");

        var en = NewScreen("n");
        (await InLanguage("en", () => en.Term.ConfirmAsync("Sure?", true))).Should().BeFalse();
        en.Text.Should().Contain("Sure? (Y/n): ");
        var enNo = NewScreen("y");
        (await InLanguage("en", () => enNo.Term.ConfirmAsync("Sure?", false))).Should().BeTrue();
        enNo.Text.Should().Contain("Sure? (y/N): ");
    }
}
