using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake;
using UsurperRemake.BBS;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.7 (T3): the telemetry question, the Preferences line, the operator switch and their text in five
/// languages (T3-tests.md rows 1 to 14, and the OperatorTurnedOff row carried from T1-tests.md). Every
/// folder is a temp folder; no test sends anything (the uploader here has a fake sender).
/// </summary>
[Collection("SharedGameSingletons")]
public class TelemetryPrompt127Tests : IDisposable
{
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags INP = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly FieldInfo SaveField = typeof(SaveSystem).GetField("instance", SNP)!;
    private static readonly FieldInfo DoorField = typeof(DoorMode).GetField("_sessionInfo", SNP)!;
    private static readonly FieldInfo OnlineField = typeof(DoorMode).GetField("_onlineMode", SNP)!;
    private static readonly string[] Languages = { "en", "es", "fr", "hu", "it" };
    private static readonly string[] Keys =
    {
        "telemetry.prompt_title", "telemetry.prompt_body_single", "telemetry.prompt_body_shared", "telemetry.prompt_ask",
        "telemetry.pref_row", "telemetry.pref_on", "telemetry.pref_off", "telemetry.pref_turn_off",
        "telemetry.pref_new_id", "telemetry.not_saved",
    };

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"usurper-telemetry-t3-{Guid.NewGuid():N}");
    private readonly object? _saveBefore = SaveField.GetValue(null);
    private readonly object? _doorBefore = DoorField.GetValue(null);
    private readonly object? _onlineBefore = OnlineField.GetValue(null);
    private readonly bool _srBefore = GameConfig.ScreenReaderMode;
    private readonly bool _switchBefore = GameConfig.TelemetryPromptEnabled;
    private readonly DateTime _now = new(2026, 10, 5, 12, 0, 0);
    private SqlSaveBackend? _db;

    public TelemetryPrompt127Tests()
    {
        Directory.CreateDirectory(_dir);
        TelemetryConsent.ResetForTests();
        TelemetryPrompt.StoreOverride = null;
        TelemetryPrompt.InputIsInteractive = TelemetryPrompt.DefaultInputIsInteractive;
        GameConfig.TelemetryPromptEnabled = false;
    }

    public void Dispose()
    {
        SaveField.SetValue(null, _saveBefore);
        DoorField.SetValue(null, _doorBefore);
        OnlineField.SetValue(null, _onlineBefore);
        GameConfig.ScreenReaderMode = _srBefore;
        GameConfig.TelemetryPromptEnabled = _switchBefore;
        TelemetryConsent.ResetForTests();
        TelemetryPrompt.StoreOverride = null;
        TelemetryPrompt.InputIsInteractive = TelemetryPrompt.DefaultInputIsInteractive;
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    // ---------- helpers ----------

    private SqlSaveBackend Db => _db ??= new SqlSaveBackend(Path.Combine(_dir, "game.db"));

    private TelemetryStore Store(TelemetrySource source = TelemetrySource.Single, string? dir = null) =>
        new(dir ?? _dir, () => source, () => _db, () => _now);

    private static (TerminalEmulator term, MemoryStream output) Term(params string[] lines)
    {
        var output = new MemoryStream();
        return (new TerminalEmulator(new LineStream(lines), output), output);
    }

    private static string Plain(TerminalEmulator term, MemoryStream output)
    {
        term.StreamWriterInternal?.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "").Replace("\r\n", "\n");
    }

    private void Door(string name = "Player")
    {
        DoorField.SetValue(null, new BBSSessionInfo { SourceType = DropFileType.DoorSys, UserName = name, UserAlias = name });
        OnlineField.SetValue(null, true);
        DoorMode.IsInDoorMode.Should().BeTrue();
    }

    private void ServerMode()
    {
        DoorField.SetValue(null, null);
        OnlineField.SetValue(null, true);
        DoorMode.IsInDoorMode.Should().BeFalse();
        DoorMode.IsOnlineMode.Should().BeTrue();
    }

    private static TelemetryRow Row(int rounds = 6) =>
        TelemetryRow.From(new CombatEventRow("A Player", 10, "Warrior", 600, 60, 30, 25, 18, "Brute", 8, 260, 40, 5, false, "victory",
            rounds, 520, 140, 95, 30, 8, 2, false,
            new CombatRowTally(8, 1, 3, 2, 0, 120, 20, 0, 0, 80, 520, 200, 40, 1, 2, 0, 0, 460)), CharacterClass.Warrior, "Undead")!;

    private static string[] QueueLines(TelemetryStore s) => File.Exists(s.QueuePath) ? File.ReadAllLines(s.QueuePath) : Array.Empty<string>();

    private static string Source(string rel) => File.ReadAllText(Path.Combine(MainStreetDistricts1113Tests.RepoRoot(), rel));

    /// <summary>The text of one method of a source file, from its signature to the next member at the same indent.</summary>
    private static string MethodBody(string rel, string signature)
    {
        string src = Source(rel);
        int start = src.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, signature);
        int end = src.IndexOf("\n    private ", start + signature.Length, StringComparison.Ordinal);
        int endPublic = src.IndexOf("\n    public ", start + signature.Length, StringComparison.Ordinal);
        if (end < 0 || (endPublic > 0 && endPublic < end)) end = endPublic;
        return end > 0 ? src.Substring(start, end - start) : src.Substring(start);
    }

    /// <summary>Opens the Preferences menu on a scripted terminal and returns what it printed.</summary>
    private static async Task<string> RunPreferences(bool screenReader, params string[] input)
    {
        var street = new MainStreetLocation();
        var (term, output) = Term(input);
        typeof(BaseLocation).GetField("terminal", INP)!.SetValue(street, term);
        var hero = new Character { Name1 = "Prefs", Name2 = "Prefs", Level = 5, HP = 50, MaxHP = 50, AI = CharacterAI.Human, ScreenReaderMode = screenReader };
        typeof(BaseLocation).GetField("currentPlayer", INP)!.SetValue(street, hero);
        GameConfig.ScreenReaderMode = screenReader;
        await (Task)typeof(BaseLocation).GetMethod("ShowPreferencesMenu", INP)!.Invoke(street, null)!;
        return Plain(term, output);
    }

    private static string Label(bool on) => $"{Loc.Get("telemetry.pref_row")}: {Loc.Get(on ? "telemetry.pref_on" : "telemetry.pref_off")}";

    /// <summary>Holds the queue lock of <paramref name="s"/> as another node would.</summary>
    private static FileStream HoldLock(TelemetryStore s)
    {
        Directory.CreateDirectory(s.Folder);
        return new FileStream(s.LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private sealed class NoSendSender : ITelemetrySender
    {
        public int Calls;
        public Task<TelemetryReply> SendAsync(byte[] body) { Calls++; return Task.FromResult(new TelemetryReply(200, "{\"ok\":true}")); }
    }

    // ======================================================================
    // Row 1: asked once on single player and Steam, before the main menu
    // ======================================================================

    [Theory]
    [InlineData("Y", true)]
    [InlineData("N", false)]
    public async Task Row1_Single_NotAsked_AskedBeforeTheMenu_TheAnswerStored_NotAskedAtTheNextStart(string key, bool yes)
    {
        var store = Store();
        store.State.Asked.Should().BeFalse();
        var (term, output) = Term(key);
        int drawn = TelemetryPrompt.Drawn;
        await TelemetryPrompt.AskInstallIfNeededAsync(term, store);
        TelemetryPrompt.Drawn.Should().Be(drawn + 1);
        Plain(term, output).Should().Contain(Loc.Get("telemetry.prompt_title")).And.Contain(Loc.Get("telemetry.prompt_ask"));
        store.State.Asked.Should().BeTrue();
        store.State.Yes.Should().Be(yes);
        if (yes) store.State.InstallId.Should().MatchRegex("^[0-9a-f]{32}$", "a yes makes the install_id");
        else store.State.InstallId.Should().BeNull();

        // the next start: a new process reads state.json and asks nothing
        var next = Store();
        var (term2, output2) = Term("Y");
        await TelemetryPrompt.AskInstallIfNeededAsync(term2, next);
        TelemetryPrompt.Drawn.Should().Be(drawn + 1, "asked once");
        Plain(term2, output2).Should().BeEmpty();
        next.State.Yes.Should().Be(yes, "nothing changed at the next start");
    }

    [Fact]
    public void Row1_TheQuestion_ComesAfterTheUpdatePrompt_AndBeforeTheMainMenu()
    {
        string loop = MethodBody("Scripts/Core/GameEngine.cs", "private async Task RunMainGameLoop()");
        int update = loop.IndexOf("PromptAndInstallUpdate", StringComparison.Ordinal);
        int ask = loop.IndexOf("TelemetryPrompt.AskInstallIfNeededAsync(terminal)", StringComparison.Ordinal);
        int menu = loop.IndexOf("await MainMenu();", StringComparison.Ordinal);
        update.Should().BeGreaterThan(0);
        ask.Should().BeGreaterThan(update);
        menu.Should().BeGreaterThan(ask);
    }

    // ======================================================================
    // Row 2: no answer is never a yes, and never blocks the start
    // ======================================================================

    [Theory]
    [InlineData("", "", "")]                // end of input on a local console reads empty lines
    [InlineData("maybe", "x", "q")]         // MaxInvalidChoiceAttempts
    public async Task Row2_Single_NoAnswer_StaysNotAsked_TheStartGoesOn_AskedAgainNextStart(params string[] lines)
    {
        var store = Store();
        var (term, _) = Term(lines);
        await TelemetryPrompt.AskInstallIfNeededAsync(term, store);    // returns: the start goes on
        store.State.Asked.Should().BeFalse("no answer is stored as not asked");
        store.State.Yes.Should().BeFalse();
        store.State.InstallId.Should().BeNull();

        var next = Store();
        next.State.Asked.Should().BeFalse("on disk too");
        var (term2, output2) = Term("Y");
        await TelemetryPrompt.AskInstallIfNeededAsync(term2, next);
        Plain(term2, output2).Should().Contain(Loc.Get("telemetry.prompt_title"), "asked again at the next start");
        next.State.Yes.Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Row2_Shared_NoAnswerOrAMudHangUp_StaysNotAsked_AskedAgainNextLogin(bool hangUp)
    {
        Door();
        TelemetryConsent.OperatorResolver = () => true;
        var store = Store(TelemetrySource.BbsDoor);
        var (term, _) = hangUp ? Term() : Term("", "", "");
        if (hangUp)
        {
            // the MUD stream closed: GetInput throws, the answer stays not asked, the session still ends
            Func<Task> act = () => TelemetryPrompt.AskPlayerIfNeededAsync(term, "Player", store);
            await act.Should().ThrowAsync<ConnectionClosedException>();
        }
        else
        {
            await TelemetryPrompt.AskPlayerIfNeededAsync(term, "Player", store);
        }
        store.PlayerWasAsked("Player").Should().BeFalse();
        store.ShouldQueue("Player").Should().BeFalse("no answer is never a yes");

        var (term2, output2) = Term("Y");
        await TelemetryPrompt.AskPlayerIfNeededAsync(term2, "Player", store);
        Plain(term2, output2).Should().Contain(Loc.Get("telemetry.prompt_title"), "asked again at the next login");
        store.ShouldQueue("Player").Should().BeTrue();
    }

    [Fact]
    public async Task Row2_NothingIsWrittenBeforeTheAnswer_SoADoorHangUpLeavesNotAsked()
    {
        // A door hang up exits the process inside GetInput. Nothing may be on disk at that moment.
        Door();
        TelemetryConsent.OperatorResolver = () => true;
        var single = Store(TelemetrySource.Single, Path.Combine(_dir, "single"));
        var shared = Store(TelemetrySource.BbsDoor, Path.Combine(_dir, "door"));
        bool singleBefore = true, sharedBefore = true;
        var output = new MemoryStream();
        var t1 = new TerminalEmulator(new LineStream(new[] { "Y" }, _ => singleBefore = File.Exists(single.StatePath)), output);
        await TelemetryPrompt.AskInstallIfNeededAsync(t1, single);
        var t2 = new TerminalEmulator(new LineStream(new[] { "Y" }, _ => sharedBefore = Directory.Exists(shared.Folder)), new MemoryStream());
        await TelemetryPrompt.AskPlayerIfNeededAsync(t2, "Player", shared);
        singleBefore.Should().BeFalse("state.json is written only after the answer");
        sharedBefore.Should().BeFalse("the player's answer is written only after the answer");
        single.State.Yes.Should().BeTrue();
        shared.ShouldQueue("Player").Should().BeTrue();
    }

    // ======================================================================
    // Row 3: only Y or N answer; Enter and other keys ask again
    // ======================================================================

    [Fact]
    public async Task Row3_EnterAndOtherKeys_AskAgain_OnlyYOrNAnswers_NoAnswerIsNotNo()
    {
        var (t1, o1) = Term("", "Y");
        (await t1.AskYesNoOrNoAnswerAsync("? ")).Should().BeTrue("Enter asked again, then Y");
        Plain(t1, o1).Should().Contain(Loc.Get("ui.answer_yes_no"));
        var (t2, _) = Term("", "x", "N");
        (await t2.AskYesNoOrNoAnswerAsync("? ")).Should().BeFalse();
        var (t3, _) = Term("", "", "");
        (await t3.AskYesNoOrNoAnswerAsync("? ")).Should().BeNull("three Enters are no answer, not a No");

        // through the prompt: Enter then N stores no; Enter alone three times stores nothing
        var store = Store();
        var (t4, _) = Term("", "N");
        await TelemetryPrompt.AskInstallIfNeededAsync(t4, store);
        store.State.Asked.Should().BeTrue();
        store.State.Yes.Should().BeFalse();
    }

    // ======================================================================
    // Row 4: door and server ask each player once after login, only with the switch on
    // ======================================================================

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Row4_Shared_SwitchOn_EachPlayerAskedOnce_TheirOwnAnswer_SwitchOff_NoQuestion(bool door)
    {
        if (door) Door(); else ServerMode();
        if (!door) { _ = Db; SaveSystem.InitializeWithBackend(Db); }
        var store = Store(door ? TelemetrySource.BbsDoor : TelemetrySource.Server);
        int drawn = TelemetryPrompt.Drawn;

        // switch off: nobody is asked and nothing is created
        await TelemetryPrompt.AskPlayerIfNeededAsync(Term("Y").term, "Alpha", store);
        TelemetryPrompt.Drawn.Should().Be(drawn);
        Directory.Exists(store.Folder).Should().BeFalse();

        TelemetryConsent.OperatorResolver = () => true;
        var (ta, oa) = Term("Y");
        await TelemetryPrompt.AskPlayerIfNeededAsync(ta, "Alpha", store);
        Plain(ta, oa).Should().Contain(Loc.Get("telemetry.prompt_body_shared").Split('\n')[0]);
        var (tb, _) = Term("N");
        await TelemetryPrompt.AskPlayerIfNeededAsync(tb, "Beta", store);
        TelemetryPrompt.Drawn.Should().Be(drawn + 2);

        // a fresh store reads each player's own answer
        var again = Store(door ? TelemetrySource.BbsDoor : TelemetrySource.Server);
        again.LoadPlayerAnswer("Alpha");
        again.LoadPlayerAnswer("Beta");
        again.ShouldQueue("Alpha").Should().BeTrue();
        again.ShouldQueue("Beta").Should().BeFalse();

        // asked never again
        await TelemetryPrompt.AskPlayerIfNeededAsync(Term("N").term, "Alpha", again);
        await TelemetryPrompt.AskPlayerIfNeededAsync(Term("Y").term, "Beta", again);
        TelemetryPrompt.Drawn.Should().Be(drawn + 2);
        again.ShouldQueue("Beta").Should().BeFalse();
    }

    [Fact]
    public void Row4_TheDoorQuestion_ComesAfterTheMotd_AndBeforeTheCharacterLookup_OnceASession()
    {
        string door = MethodBody("Scripts/Core/GameEngine.cs", "private async Task RunBBSDoorMode()");
        int login = door.IndexOf("TelemetryConsent.OnLogin(playerName)", StringComparison.Ordinal);
        int motd = door.IndexOf("MessageOfTheDayText()", StringComparison.Ordinal);
        int ask = door.IndexOf("TelemetryPrompt.AskPlayerIfNeededAsync(terminal, playerName)", StringComparison.Ordinal);
        int lookup = door.IndexOf("GetMostRecentSave(accountName)", StringComparison.Ordinal);
        login.Should().BeGreaterThan(0);
        motd.Should().BeGreaterThan(login);
        ask.Should().BeGreaterThan(motd);
        lookup.Should().BeGreaterThan(ask);
        door.Should().Contain("if (!_telemetryAskedThisSession)");
    }

    // ======================================================================
    // Row 5: the operator switch
    // ======================================================================

    [Fact]
    public void Row5_TheSwitch_IsARegistryBool_DefaultOff_AFreshDatabaseReadsOff_NothingWritten()
    {
        var d = ServerSettingsRegistry.Get("telemetry_prompt");
        d.Should().NotBeNull();
        d!.Type.Should().Be(ServerSettingsRegistry.SettingType.Bool);
        d.DefaultValue.Should().Be("false");
        d.CurrentValue().Should().Be("false");
        ServerSettingsRegistry.Validate("telemetry_prompt", "true").ok.Should().BeTrue();

        TelemetryConsent.UseServerSwitch();
        _ = Db;    // a fresh database: server_config has no row, the switch reads off
        TelemetryConsent.OperatorAllows().Should().BeFalse();
        Db.GetServerConfig("telemetry_prompt").Should().BeNull("loading the settings writes no row (no mirror)");

        // a database where the operator turned it on
        Db.SetServerConfig("telemetry_prompt", "true", "test");
        GameConfig.TelemetryPromptEnabled = false;
        SqliteConnection.ClearAllPools();
        _ = new SqlSaveBackend(Path.Combine(_dir, "game.db"));
        GameConfig.TelemetryPromptEnabled.Should().BeTrue("server_config is loaded at the backend's start");
        TelemetryConsent.OperatorAllows().Should().BeTrue("the resolver reads the switch");
    }

    [Theory]
    [InlineData("Scripts/Server/MudServer.cs", "public async Task RunAsync(CancellationToken cancellationToken)")]
    [InlineData("Scripts/Core/GameEngine.cs", "public static async Task RunConsoleAsync()")]
    public void Row5_TheResolver_IsSetBeforeTheStartUpload(string rel, string signature)
    {
        string body = MethodBody(rel, signature);
        int set = body.IndexOf("TelemetryConsent.UseServerSwitch();", StringComparison.Ordinal);
        int start = body.IndexOf("TelemetryUploader.StartInBackground();", StringComparison.Ordinal);
        set.Should().BeGreaterThan(0);
        start.Should().BeGreaterThan(set);
        if (rel.EndsWith("MudServer.cs"))
            body.IndexOf("new SqlSaveBackend(", StringComparison.Ordinal).Should().BeLessThan(set, "server_config is loaded first");
    }

    // ======================================================================
    // Row 6 and the T1 carried row: the switch turned off, with and without the queue lock
    // ======================================================================

    private (TelemetryStore store, string key) SharedWithRows()
    {
        ServerMode();
        SaveSystem.InitializeWithBackend(Db);
        TelemetryConsent.UseServerSwitch();
        ServerSettingsRegistry.ApplyConfigValue("telemetry_prompt", "true");
        var store = TelemetryConsent.CurrentStore()!;
        store.SetPlayerAnswer("Player", true).Should().BeTrue();
        string key = TelemetryConsent.PlayerKey("Player")!;
        store.Append(Row(1), key);
        store.Append(Row(2), key);
        QueueLines(store).Should().HaveCount(2);
        store.State.InstallId.Should().NotBeNull();
        return (store, key);
    }

    [Fact]
    public void Row6_SwitchOffThroughTheRegistry_DeletesTheQueueAndTheId()
    {
        var (store, key) = SharedWithRows();
        ServerSettingsRegistry.ApplyConfigValue("telemetry_prompt", "false");
        GameConfig.TelemetryPromptEnabled.Should().BeFalse();
        File.Exists(store.QueuePath).Should().BeFalse();
        store.ReReadStateLocked().InstallId.Should().BeNull();
        store.Append(Row(3), key);
        QueueLines(store).Should().BeEmpty("nothing is queued with the switch off");
    }

    [Fact]
    public async Task Row6_SwitchOffWithTheQueueLockHeld_NothingQueued_AnswersCleared_DeletionRetriedAndLogged()
    {
        var (store, key) = SharedWithRows();
        store.LockTimeout = TimeSpan.FromMilliseconds(200);
        using (HoldLock(store))
        {
            Action off = () => ServerSettingsRegistry.ApplyConfigValue("telemetry_prompt", "false");
            off.Should().NotThrow("the switch going off never throws");
            store.OperatorOffFailures.Should().Be(1, "the failure is counted and logged");
        }
        GameConfig.TelemetryPromptEnabled.Should().BeFalse();

        // the cached answers were cleared before the lock: with the switch back on, no session keeps a yes
        GameConfig.TelemetryPromptEnabled = true;
        store.ShouldQueue("Player").Should().BeFalse("the cached yes was cleared even though the lock was busy");
        GameConfig.TelemetryPromptEnabled = false;

        // nothing is queued after
        store.Append(Row(3), key);
        QueueLines(store).Should().HaveCount(2, "the two rows from before, nothing new");

        // the deletion is owed, in this process and on disk for the next start of any node
        store.ReReadStateLocked().InstallId.Should().NotBeNull("the lock was busy, so nothing was deleted yet");
        store.OperatorOffOwed.Should().BeTrue();
        File.Exists(store.OperatorOffMarkerPath).Should().BeTrue();

        // the next start (a new process: a new store over the same folder) does it at its upload, logged
        var nextStart = new TelemetryStore(Path.GetDirectoryName(store.Folder)!, () => TelemetrySource.Server, () => Db, () => _now);
        nextStart.OperatorOffOwed.Should().BeTrue();
        var sender = new NoSendSender();
        var uploader = new TelemetryUploader(nextStart, sender, () => _now.ToUniversalTime(), () => TelemetrySource.Server);
        (await uploader.UploadOnceAsync()).Should().Be(TelemetryUploadOutcome.NotAllowed);
        sender.Calls.Should().Be(0);
        File.Exists(store.QueuePath).Should().BeFalse();
        nextStart.ReReadStateLocked().InstallId.Should().BeNull();
        nextStart.OperatorOffRetries.Should().Be(1);
        File.Exists(store.OperatorOffMarkerPath).Should().BeFalse();

        // the first process clears its own flag at its next upload (deleting nothing more)
        var first = new TelemetryUploader(store, sender, () => _now.ToUniversalTime(), () => TelemetrySource.Server);
        (await first.UploadOnceAsync()).Should().Be(TelemetryUploadOutcome.NotAllowed);
        store.OperatorOffOwed.Should().BeFalse();
        (await uploader.UploadOnceAsync()).Should().Be(TelemetryUploadOutcome.NotAllowed);
        nextStart.OperatorOffRetries.Should().Be(1, "nothing more is owed");
    }

    // ======================================================================
    // Row 7: the Preferences line
    // ======================================================================

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Row7_Single_TheLine_TurnsOffAndOn_NewRandomId(bool screenReader)
    {
        var store = Store();
        store.SetInstallAnswer(true).Should().BeTrue();
        string firstId = store.State.InstallId!;
        store.Append(Row(1));
        QueueLines(store).Should().HaveCount(1);
        TelemetryPrompt.StoreOverride = store;

        // U, 2: a new id; U, 1: off (the queue goes); U then Y: the question, back on
        string text = await RunPreferences(screenReader, "U", "2", "U", "1", "U", "Y", "0");
        text.Should().Contain(Loc.Get("telemetry.pref_turn_off")).And.Contain(Loc.Get("telemetry.pref_new_id"));
        text.Should().Contain(Label(true)).And.Contain(Label(false));
        text.Should().Contain(Loc.Get("telemetry.prompt_body_single").Split('\n')[0], "turning it on asks the question again");
        store.State.Yes.Should().BeTrue();
        store.State.InstallId.Should().NotBeNull().And.NotBe(firstId);
        QueueLines(store).Should().BeEmpty("turning it off deleted the queue");
    }

    [Fact]
    public async Task Row7_Single_TurnOff_DeletesTheQueueAndTheId()
    {
        var store = Store();
        store.SetInstallAnswer(true).Should().BeTrue();
        store.Append(Row(1));
        TelemetryPrompt.StoreOverride = store;
        await RunPreferences(false, "U", "1", "0");
        store.State.Asked.Should().BeTrue();
        store.State.Yes.Should().BeFalse();
        store.State.InstallId.Should().BeNull();
        File.Exists(store.QueuePath).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Row7_Door_TheLineTurnsOff_NoNewIdOffered_HiddenWhileTheSwitchIsOff(bool screenReader)
    {
        Door();
        TelemetryConsent.OperatorResolver = () => true;
        var store = Store(TelemetrySource.BbsDoor);
        store.SetPlayerAnswer("Player", true).Should().BeTrue();
        string id = store.State.InstallId!;
        store.Append(Row(1), TelemetryConsent.PlayerKey("Player"));
        QueueLines(store).Should().HaveCount(1);
        TelemetryPrompt.StoreOverride = store;

        string text = await RunPreferences(screenReader, "U", "0");
        text.Should().Contain(Label(true));
        text.Should().NotContain(Loc.Get("telemetry.pref_new_id"), "the id belongs to the operator");
        store.ShouldQueue("Player").Should().BeFalse("U turned it off");
        QueueLines(store).Should().BeEmpty("a player's No deletes the queue");
        store.ReReadStateLocked().InstallId.Should().Be(id, "the operator's id stays");

        TelemetryConsent.OperatorResolver = () => false;
        string hidden = await RunPreferences(screenReader, "U", "0");
        hidden.Should().NotContain(Loc.Get("telemetry.pref_row"), "hidden while the switch is off");
    }

    // ======================================================================
    // Row 8: a write that failed is shown
    // ======================================================================

    [Fact]
    public async Task Row8_AFailedWrite_ShowsTheNotSavedLine_AndANoStillClosesTheGate()
    {
        var store = Store();
        store.SetInstallAnswer(true).Should().BeTrue();
        store.LockTimeout = TimeSpan.FromMilliseconds(100);
        TelemetryPrompt.StoreOverride = store;
        string text;
        using (HoldLock(store))
            text = await RunPreferences(false, "U", "1", "0");
        text.Should().Contain(Loc.Get("telemetry.not_saved"));
        store.ShouldQueue(null).Should().BeFalse("the No holds in this process even though it was not written");

        // the start question: a yes that could not be written
        var fresh = Store(TelemetrySource.Single, Path.Combine(_dir, "fresh"));
        fresh.LockTimeout = TimeSpan.FromMilliseconds(100);
        var (term, output) = Term("Y");
        using (HoldLock(fresh))
            await TelemetryPrompt.AskInstallIfNeededAsync(term, fresh);
        Plain(term, output).Should().Contain(Loc.Get("telemetry.not_saved"));
        fresh.ShouldQueue(null).Should().BeFalse();
    }

    // ======================================================================
    // Row 9: five languages, 79 columns
    // ======================================================================

    [Fact]
    public async Task Row9_EveryLine_InFiveLanguages_FitsIn79Columns()
    {
        foreach (string lang in Languages)
            foreach (string key in Keys)
            {
                Loc.HasIn(lang, key).Should().BeTrue($"{lang} has {key}");
                Loc.GetIn(lang, key).Trim().Should().NotBeEmpty($"{lang} {key}");
            }

        foreach (string lang in Languages)
        {
            using var _ = Loc.RenderLanguage(lang);
            foreach (bool sr in new[] { false, true })
                foreach (bool shared in new[] { false, true })
                {
                    GameConfig.ScreenReaderMode = sr;
                    var (term, output) = Term("N");    // T3b: N answers in every language; Y is not offered in es or it
                    await TelemetryPrompt.AskAsync(term, shared);
                    var lines = Plain(term, output).Split('\n');
                    lines.Should().OnlyContain(l => l.Length <= 79, $"{lang} sr={sr} shared={shared}");
                    TelemetryPrompt.Lines(shared).Count.Should().BeLessThanOrEqualTo(18, "the prompt and the MOTD fit a 24 line BBS page");
                }
            var rows = new List<string>
            {
                $"[U] {Label(true)}", $"[U] {Label(false)}", $"  U. {Label(true)}", $"  U. {Label(false)}",
                $" {Label(true)}", $"[1] {Loc.Get("telemetry.pref_turn_off")}", $"  2. {Loc.Get("telemetry.pref_new_id")}",
                $" {Loc.Get("telemetry.not_saved")}", $" {TelemetryPrompt.Question()}",
            };
            rows.Should().OnlyContain(r => r.Length <= 79, lang);
        }
    }

    // ======================================================================
    // Row 10: the English text is the approved text, byte for byte
    // ======================================================================

    private static Dictionary<string, string> Approved()
    {
        var lines = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "telemetry-approved-en.txt")).Replace("\r\n", "\n").Split('\n');
        var blocks = new Dictionary<string, string>();
        string? current = null;
        var acc = new List<string>();
        void Close()
        {
            if (current == null) return;
            while (acc.Count > 0 && acc[0].Length == 0) acc.RemoveAt(0);
            while (acc.Count > 0 && acc[^1].Length == 0) acc.RemoveAt(acc.Count - 1);
            blocks[current] = string.Join("\n", acc);
            acc.Clear();
        }
        foreach (var l in lines)
        {
            if (l.StartsWith("Prompt, single player and Steam:")) { Close(); current = "single"; continue; }
            if (l.StartsWith("Prompt, BBS door and server:")) { Close(); current = "shared"; continue; }
            if (l.StartsWith("Preferences row: ")) { Close(); current = null; blocks["row"] = l["Preferences row: ".Length..]; continue; }
            if (l.StartsWith("Preferences sub choices (single and Steam only): ")) { blocks["sub"] = l["Preferences sub choices (single and Steam only): ".Length..]; continue; }
            if (l.StartsWith("Not saved line: ")) { blocks["notsaved"] = l["Not saved line: ".Length..]; continue; }
            if (current != null) acc.Add(l);
        }
        Close();
        return blocks;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Row10_TheEnglishPrompt_IsTheApprovedText_ByteForByte(bool shared)
    {
        string expected = Approved()[shared ? "shared" : "single"];
        expected.Should().StartWith(" Share combat data to help balance the game?").And.EndWith(" Share combat data? (Y/N):");
        using var _ = Loc.RenderLanguage("en");
        foreach (bool sr in new[] { false, true })
        {
            GameConfig.ScreenReaderMode = sr;
            var (term, output) = Term("Y");
            await TelemetryPrompt.AskAsync(term, shared);
            string text = Plain(term, output);
            int at = text.IndexOf(" Share combat data to help", StringComparison.Ordinal);
            at.Should().BeGreaterThanOrEqualTo(0);
            text.Length.Should().BeGreaterThanOrEqualTo(at + expected.Length);
            text.Substring(at, expected.Length).Should().Be(expected, $"screen reader {sr}");
        }
    }

    [Fact]
    public void Row10_TheEnglishKeys_AreTheApprovedLines()
    {
        var a = Approved();
        string en(string k) => Loc.GetIn("en", k);
        string Unindent(string block) => string.Join("\n", block.Split('\n').Select(l => l.Length > 0 ? l[1..] : l));
        string single = Unindent(a["single"]), shared = Unindent(a["shared"]);
        single.Should().Be($"{en("telemetry.prompt_title")}\n\n{en("telemetry.prompt_body_single")}\n\n{en("telemetry.prompt_ask")} {en("ui.yn_prompt").TrimEnd()}");
        shared.Should().Be($"{en("telemetry.prompt_title")}\n\n{en("telemetry.prompt_body_shared")}\n\n{en("telemetry.prompt_ask")} {en("ui.yn_prompt").TrimEnd()}");
        a["row"].Should().Be($"{en("telemetry.pref_row")}: {en("telemetry.pref_on")} / {en("telemetry.pref_off")}");
        a["sub"].Should().Be($"{en("telemetry.pref_turn_off")} / {en("telemetry.pref_new_id")}");
        a["notsaved"].Should().Be(en("telemetry.not_saved"));
    }

    // ======================================================================
    // Row 11: a deleted name is asked again
    // ======================================================================

    [Fact]
    public async Task Row11_ADeletedDoorCharacter_IsAskedAgain()
    {
        Door();
        TelemetryConsent.OperatorResolver = () => true;
        var backend = new FileSaveBackend();
        typeof(FileSaveBackend).GetField("baseSaveDirectory", INP)!.SetValue(backend, _dir);
        var store = TelemetryConsent.StoreFor(backend.GetSaveDirectory());
        await TelemetryPrompt.AskPlayerIfNeededAsync(Term("Y").term, "Player", store);
        store.PlayerWasAsked("Player").Should().BeTrue();

        // the save backend's delete path removes the answer
        backend.DeleteGameData("Player");
        store.PlayerWasAsked("Player").Should().BeFalse();

        var (term, output) = Term("N");
        await TelemetryPrompt.AskPlayerIfNeededAsync(term, "Player", store);
        Plain(term, output).Should().Contain(Loc.Get("telemetry.prompt_title"), "the same name is asked again");
    }

    // ======================================================================
    // Row 12: the web admin control says what it does
    // ======================================================================

    [Fact]
    public void Row12_TheDescription_SaysTurningItOnAsksPlayersToSendCombatDataToTheSite()
    {
        var d = ServerSettingsRegistry.Get("telemetry_prompt")!;
        d.Label.Should().NotBeNullOrWhiteSpace();
        d.Description.Should().Contain("When ON, each player is asked once whether to send combat data to usurper-reborn.net");
        d.Description.Should().Contain("When OFF, nobody is asked and nothing is sent");
        d.ChangeImpact.Should().NotBeNullOrWhiteSpace();

        // the schema the web admin renders carries it
        using var conn = new SqliteConnection($"Data Source={Path.Combine(_dir, "game.db")}");
        _ = Db;
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT schema_json FROM server_config_schema WHERE id = 1";
        string json = (string)cmd.ExecuteScalar()!;
        using var doc = JsonDocument.Parse(json);
        var entry = doc.RootElement.EnumerateArray().Single(e => e.GetProperty("key").GetString() == "telemetry_prompt");
        entry.GetProperty("description").GetString().Should().Be(d.Description);
        entry.GetProperty("type").GetString().Should().Be("Bool");
    }

    // ======================================================================
    // Row 13: never asked in a non interactive run
    // ======================================================================

    [Fact]
    public async Task Row13_ANonInteractiveRun_DrawsNothing_AndStaysNotAsked()
    {
        TelemetryPrompt.InputIsInteractive = _ => false;
        int drawn = TelemetryPrompt.Drawn;
        var store = Store();
        var (term, output) = Term("Y");
        await TelemetryPrompt.AskInstallIfNeededAsync(term, store);
        Plain(term, output).Should().BeEmpty();
        store.State.Asked.Should().BeFalse();
        Directory.Exists(store.Folder).Should().BeFalse();

        Door();
        TelemetryConsent.OperatorResolver = () => true;
        var shared = Store(TelemetrySource.BbsDoor, Path.Combine(_dir, "door"));
        var (t2, o2) = Term("Y");
        await TelemetryPrompt.AskPlayerIfNeededAsync(t2, "Player", shared);
        Plain(t2, o2).Should().BeEmpty();
        shared.PlayerWasAsked("Player").Should().BeFalse();
        TelemetryPrompt.Drawn.Should().Be(drawn);
    }

    [Fact]
    public void Row13_TheDefaultCheck_AStreamIsInteractive_ARedirectedConsoleIsNot()
    {
        DoorField.SetValue(null, null);
        OnlineField.SetValue(null, false);
        TelemetryPrompt.DefaultInputIsInteractive(Term().term).Should().BeTrue("a MUD or BBS stream has a person on it");
        TelemetryPrompt.DefaultInputIsInteractive(new TerminalEmulator()).Should().Be(!Console.IsInputRedirected);
    }

    // ======================================================================
    // Row 14: a screen reader reads plain lines
    // ======================================================================

    [Fact]
    public async Task Row14_ScreenReader_ThePromptAndTheLine_ArePlainLines()
    {
        var store = Store();
        TelemetryPrompt.StoreOverride = store;
        string text = await RunPreferences(true, "0");
        text.Should().Contain($"  U. {Label(false)}");
        text.Should().NotContain("[U]");

        store.SetInstallAnswer(true).Should().BeTrue();
        string sub = await RunPreferences(true, "U", "0", "0");
        sub.Should().Contain($"  1. {Loc.Get("telemetry.pref_turn_off")}").And.Contain($"  2. {Loc.Get("telemetry.pref_new_id")}");
        sub.Should().NotContain("[1]").And.NotContain("[2]");

        GameConfig.ScreenReaderMode = true;
        var (term, output) = Term("Y");
        await TelemetryPrompt.AskAsync(term, false);
        string prompt = Plain(term, output);
        prompt.IndexOfAny("═║╔╗╚╝─│┌┐[]".ToCharArray()).Should().Be(-1, "no box or bracket art");
    }
}
