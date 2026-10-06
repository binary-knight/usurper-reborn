using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.BBS;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.7 (T3b): two fixes to the telemetry consent. (1) At the question only the yes the session's
/// language offers stores a yes; another language's yes letter or word is asked again like junk and,
/// when the tries run out, leaves "not asked". (2) A BBS door or server that starts with the operator
/// switch off deletes the queue, batch files and install id left from when it was on. Every folder is a
/// temp folder; no test sends anything (fake senders only, and the run's network guards stay in place).
/// </summary>
[Collection("SharedGameSingletons")]
public class TelemetryConsentT3b127Tests : IDisposable
{
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly FieldInfo SaveField = typeof(SaveSystem).GetField("instance", SNP)!;
    private static readonly FieldInfo DoorField = typeof(DoorMode).GetField("_sessionInfo", SNP)!;
    private static readonly FieldInfo OnlineField = typeof(DoorMode).GetField("_onlineMode", SNP)!;

    /// <summary>The brief's table: each language's own yes letter and word, and the letter its prompt shows.</summary>
    private static readonly Dictionary<string, string[]> OwnYes = new()
    {
        ["en"] = new[] { "Y", "Yes", "y", "yes" },
        ["es"] = new[] { "S", "Si", "Sí", "s", "sí" },
        ["fr"] = new[] { "O", "Oui", "Y", "oui", "y" },
        ["hu"] = new[] { "I", "Igen", "Y", "igen" },
        ["it"] = new[] { "S", "Si", "Sì", "s", "sì" },
    };

    /// <summary>The localized no of each language, beside N and No which every language takes.</summary>
    private static readonly Dictionary<string, string> LocalNo = new()
    {
        ["en"] = "No", ["es"] = "No", ["fr"] = "Non", ["hu"] = "Nem", ["it"] = "No",
    };

    /// <summary>Every language's yes letters and words that are not this language's own.</summary>
    private static string[] ForeignYes(string lang)
    {
        var own = new HashSet<string>(OwnYes[lang].Select(w => w.ToUpperInvariant()));
        return OwnYes.Values.SelectMany(w => w).Select(w => w.ToUpperInvariant()).Distinct()
            .Where(w => !own.Contains(w)).ToArray();
    }

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"usurper-telemetry-t3b-{Guid.NewGuid():N}");
    private readonly object? _saveBefore = SaveField.GetValue(null);
    private readonly object? _doorBefore = DoorField.GetValue(null);
    private readonly object? _onlineBefore = OnlineField.GetValue(null);
    private readonly bool _switchBefore = GameConfig.TelemetryPromptEnabled;
    private readonly DateTime _now = new(2026, 10, 6, 12, 0, 0);

    public TelemetryConsentT3b127Tests()
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
        GameConfig.TelemetryPromptEnabled = _switchBefore;
        TelemetryConsent.ResetForTests();
        TelemetryPrompt.StoreOverride = null;
        TelemetryPrompt.InputIsInteractive = TelemetryPrompt.DefaultInputIsInteractive;
        try { Directory.Delete(_dir, true); } catch { }
    }

    // ---------- helpers ----------

    private int _sub;
    private string NewDir() => Path.Combine(_dir, $"d{++_sub}");

    private TelemetryStore Store(TelemetrySource source, string dir) => new(dir, () => source, () => null, () => _now);

    private static TerminalEmulator Term(params string[] lines) => new(new LineStream(lines), new MemoryStream());

    private void Door(string name = "Player")
    {
        DoorField.SetValue(null, new BBSSessionInfo { SourceType = DropFileType.DoorSys, UserName = name, UserAlias = name });
        OnlineField.SetValue(null, true);
        DoorMode.IsInDoorMode.Should().BeTrue();
    }

    private static TelemetryRow Row(int rounds = 6) =>
        TelemetryRow.From(new CombatEventRow("A Player", 10, "Warrior", 600, 60, 30, 25, 18, "Brute", 8, 260, 40, 5, false, "victory",
            rounds, 520, 140, 95, 30, 8, 2, false,
            new CombatRowTally(8, 1, 3, 2, 0, 120, 20, 0, 0, 80, 520, 200, 40, 1, 2, 0, 0, 460)), CharacterClass.Warrior, "Undead")!;

    private sealed class NoSendSender : ITelemetrySender
    {
        public int Calls;
        public Task<TelemetryReply> SendAsync(byte[] body) { Calls++; return Task.FromResult(new TelemetryReply(200, "{\"ok\":true}")); }
    }

    private static readonly string[] Languages = { "en", "es", "fr", "hu", "it" };

    // ======================================================================
    // 1. Only the offered yes stores a yes
    // ======================================================================

    [Fact]
    public void TheOfferedYes_IsReadFromTheLocStrings_PerLanguage()
    {
        var expected = new Dictionary<string, string[]>
        {
            ["en"] = new[] { "Y", "YES" },
            ["es"] = new[] { "S", "SI", "SÍ" },
            ["fr"] = new[] { "Y", "O", "OUI" },
            ["hu"] = new[] { "Y", "I", "IGEN" },
            ["it"] = new[] { "S", "SI", "SÌ" },
        };
        foreach (string lang in Languages)
        {
            using var _ = Loc.RenderLanguage(lang);
            GameConfig.OfferedYesWords().Should().BeEquivalentTo(expected[lang], lang);
            // the letter the prompt shows is among them
            string shown = Loc.Get("ui.yn_prompt");
            GameConfig.IsOfferedYes(shown.Substring(shown.IndexOf('(') + 1, 1)).Should().BeTrue(lang);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public async Task TheVariant_TakesOnlyThisLanguagesYes_AnotherLanguagesYesIsNoAnswer(string lang)
    {
        using var _ = Loc.RenderLanguage(lang);
        var foreign = ForeignYes(lang);
        foreign.Should().NotBeEmpty();
        foreach (string word in foreign)
        {
            (await Term(word, word, word).AskYesNoOrNoAnswerAsync("? ", offeredYesOnly: true))
                .Should().BeNull($"{lang}: {word} is another language's yes, asked again like junk");
            (await Term(word, "N").AskYesNoOrNoAnswerAsync("? ", offeredYesOnly: true))
                .Should().BeFalse($"{lang}: {word} asked again, then N");
        }
        foreach (string word in OwnYes[lang])
            (await Term(word).AskYesNoOrNoAnswerAsync("? ", offeredYesOnly: true)).Should().BeTrue($"{lang}: {word}");
        foreach (string word in new[] { "N", "n", "No", "no", LocalNo[lang], LocalNo[lang].ToLowerInvariant() })
            (await Term(word).AskYesNoOrNoAnswerAsync("? ", offeredYesOnly: true)).Should().BeFalse($"{lang}: {word}");
    }

    [Fact]
    public async Task OtherPrompts_KeepEveryLanguagesYes()
    {
        using var _ = Loc.RenderLanguage("en");
        foreach (string word in new[] { "Y", "S", "O", "I", "Oui", "Igen", "Si" })
        {
            (await Term(word).AskYesNoAsync("? ")).Should().BeTrue(word);
            (await Term(word).AskYesNoOrNoAnswerAsync("? ")).Should().BeTrue($"{word}: the variant without the parameter");
            (await Term(word).AskYesNoKeyAsync()).Should().BeTrue($"{word}: the key prompt");
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public async Task TheSinglePrompt_AnotherLanguagesYes_StaysNotAsked_ItsOwnYesStoresYes_NoStoresNo(string lang)
    {
        using var _ = Loc.RenderLanguage(lang);
        foreach (string word in ForeignYes(lang))
        {
            var store = Store(TelemetrySource.Single, NewDir());
            await TelemetryPrompt.AskInstallIfNeededAsync(Term(word, word, word), store);
            store.State.Asked.Should().BeFalse($"{lang}: {word} leaves not asked");
            store.State.Yes.Should().BeFalse();
            Store(TelemetrySource.Single, Path.GetDirectoryName(store.Folder)!).State.Asked.Should().BeFalse("on disk too, so asked again next start");
        }
        foreach (string word in OwnYes[lang])
        {
            var store = Store(TelemetrySource.Single, NewDir());
            await TelemetryPrompt.AskInstallIfNeededAsync(Term(word), store);
            store.State.Asked.Should().BeTrue($"{lang}: {word}");
            store.State.Yes.Should().BeTrue($"{lang}: {word} stores yes");
        }
        foreach (string word in new[] { "N", "No", LocalNo[lang] })
        {
            var store = Store(TelemetrySource.Single, NewDir());
            await TelemetryPrompt.AskInstallIfNeededAsync(Term(word), store);
            store.State.Asked.Should().BeTrue($"{lang}: {word}");
            store.State.Yes.Should().BeFalse($"{lang}: {word} stores no");
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public async Task TheDoorPrompt_AnotherLanguagesYes_StaysNotAsked_ItsOwnYesStoresYes_NoStoresNo(string lang)
    {
        Door();
        TelemetryConsent.OperatorResolver = () => true;
        using var _ = Loc.RenderLanguage(lang);
        var store = Store(TelemetrySource.BbsDoor, NewDir());
        foreach (string word in ForeignYes(lang))
        {
            await TelemetryPrompt.AskPlayerIfNeededAsync(Term(word, word, word), "Player", store);
            store.PlayerWasAsked("Player").Should().BeFalse($"{lang}: {word} leaves not asked");
            store.ShouldQueue("Player").Should().BeFalse();
        }
        int n = 0;
        foreach (string word in OwnYes[lang])
        {
            string name = $"Yes{++n}";
            await TelemetryPrompt.AskPlayerIfNeededAsync(Term(word), name, store);
            store.PlayerWasAsked(name).Should().BeTrue($"{lang}: {word}");
            store.ShouldQueue(name).Should().BeTrue($"{lang}: {word} stores yes");
        }
        foreach (string word in new[] { "N", "No", LocalNo[lang] })
        {
            string name = $"No{++n}";
            await TelemetryPrompt.AskPlayerIfNeededAsync(Term(word), name, store);
            store.PlayerWasAsked(name).Should().BeTrue($"{lang}: {word}");
            store.ShouldQueue(name).Should().BeFalse($"{lang}: {word} stores no");
        }
    }

    // ======================================================================
    // 2. A switch set off with no process running still deletes at the next start
    // ======================================================================

    [Fact]
    public async Task SwitchOffAtStart_TheQueueBatchAndIdGo_NothingSent_NoFolderMadeWhenNoneExisted()
    {
        // a door with the switch on: a player's yes, rows queued, a batch file left by a crash
        Door();
        var backend = new FileSaveBackend();
        typeof(FileSaveBackend).GetField("baseSaveDirectory", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(backend, _dir);
        SaveSystem.InitializeWithBackend(backend);
        GameConfig.TelemetryPromptEnabled = true;
        TelemetryConsent.UseServerSwitch();
        var before = TelemetryConsent.CurrentStore()!;
        before.Folder.Should().StartWith(TelemetryTestRoot.Root);
        before.IsSharedInstall.Should().BeTrue();
        before.SetPlayerAnswer("Player", true).Should().BeTrue();
        string key = TelemetryConsent.PlayerKey("Player")!;
        before.Append(Row(), key);
        before.Append(Row(7), key);
        File.ReadAllLines(before.QueuePath).Should().HaveCount(2);
        string batch = Path.Combine(before.Folder, TelemetryStore.NewBatchFileName(0));
        File.WriteAllText(batch, File.ReadAllLines(before.QueuePath)[0] + "\n");
        before.ReReadStateLocked().InstallId.Should().NotBeNull();

        // the operator sets the switch off in server_config while no process runs; the next start loads it off
        TelemetryConsent.ResetForTests();
        GameConfig.TelemetryPromptEnabled = false;
        TelemetryConsent.UseServerSwitch();
        var factoryBefore = TelemetrySenderFactory.Create;
        var startedField = typeof(TelemetryUploader).GetField("_started", SNP)!;
        object? startedBefore = startedField.GetValue(null);
        var sender = new NoSendSender();
        TelemetryStore? start = null;
        try
        {
            TelemetrySenderFactory.Create = () => sender;
            startedField.SetValue(null, 0);
            TelemetryUploader.StartInBackground();
            await TelemetryUploader.StartTask!;
            start = TelemetryConsent.CurrentStore()!;
        }
        finally
        {
            TelemetrySenderFactory.Create = factoryBefore;
            startedField.SetValue(null, startedBefore);
            if (start != null) start.Uploader = null;
        }
        start.Folder.Should().Be(before.Folder);
        sender.Calls.Should().Be(0, "nothing is sent with the switch off");
        File.Exists(before.QueuePath).Should().BeFalse("the queue is deleted at the start");
        File.Exists(batch).Should().BeFalse("the batch file too");
        start.ReReadStateLocked().InstallId.Should().BeNull("and the id");
        start.OperatorOffOwed.Should().BeFalse();

        // a server that never had telemetry: the start with the switch off creates nothing
        var serverDir = NewDir();
        Directory.CreateDirectory(serverDir);
        var server = Store(TelemetrySource.Server, serverDir);
        (await new TelemetryUploader(server, sender, () => _now.ToUniversalTime(), () => TelemetrySource.Server).UploadOnceAsync())
            .Should().Be(TelemetryUploadOutcome.NotAllowed);
        Directory.Exists(server.Folder).Should().BeFalse("no folder is created when none existed");
        sender.Calls.Should().Be(0);
        try { Directory.Delete(Path.GetDirectoryName(before.Folder)!, true); } catch { }
    }

    [Fact]
    public async Task SwitchOffAtStart_OnlyAnIdLeft_ItGoes_ALockHeldAtStart_IsOwedAndRetried()
    {
        string dir = NewDir();
        TelemetryConsent.OperatorResolver = () => true;
        var s = Store(TelemetrySource.BbsDoor, dir);
        s.SetPlayerAnswer("Player", true).Should().BeTrue();
        s.ReReadStateLocked().InstallId.Should().NotBeNull();
        File.Exists(s.QueuePath).Should().BeFalse("only the id is on disk");

        TelemetryConsent.OperatorResolver = () => false;
        var next = Store(TelemetrySource.BbsDoor, dir);
        next.LockTimeout = TimeSpan.FromMilliseconds(200);
        var sender = new NoSendSender();
        var uploader = new TelemetryUploader(next, sender, () => _now.ToUniversalTime(), () => TelemetrySource.BbsDoor);
        using (new FileStream(next.LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            (await uploader.UploadOnceAsync()).Should().Be(TelemetryUploadOutcome.NotAllowed);
            next.OperatorOffFailures.Should().Be(1, "the lock was busy: logged and owed, as T3 built it");
            next.OperatorOffOwed.Should().BeTrue();
        }
        next.ReReadStateLocked().InstallId.Should().NotBeNull("nothing deleted while the lock was held");
        (await uploader.UploadOnceAsync()).Should().Be(TelemetryUploadOutcome.NotAllowed);
        next.OperatorOffRetries.Should().Be(1, "done at the next upload or start");
        next.ReReadStateLocked().InstallId.Should().BeNull();
        next.OperatorOffOwed.Should().BeFalse();
        sender.Calls.Should().Be(0);
    }
}
