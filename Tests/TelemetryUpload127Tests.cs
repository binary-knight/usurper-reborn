using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.7: the telemetry uploader (DESIGN.md section 3, T1-tests.md rows 22 to 29, E and the T1b
/// additions). Every uploader gets its own fake sender; the run's guard (TelemetryNoNetwork) makes any
/// other sender throw. Every folder is a temp folder, every clock injected.
/// </summary>
[Collection("SharedGameSingletons")]
public class TelemetryUpload127Tests : IDisposable
{
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly FieldInfo SaveField = typeof(SaveSystem).GetField("instance", SNP)!;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"usurper-upload-{Guid.NewGuid():N}");
    private readonly object? _saveBefore = SaveField.GetValue(null);
    private readonly DateTime _local = new(2026, 10, 5, 12, 0, 0);
    private readonly DateTime _utc = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    public TelemetryUpload127Tests()
    {
        Directory.CreateDirectory(_dir);
        TelemetryConsent.ResetForTests();
    }

    public void Dispose()
    {
        SaveField.SetValue(null, _saveBefore);
        TelemetryConsent.ResetForTests();
        try { Directory.Delete(_dir, true); } catch { }
    }

    // ---------- folders, stores, rows ----------

    private string Tel => Path.Combine(_dir, "telemetry");
    private string QueueFile => Path.Combine(Tel, "queue.jsonl");
    private string StateFile => Path.Combine(Tel, "state.json");
    private string[] Lines() => File.Exists(QueueFile) ? File.ReadAllLines(QueueFile) : Array.Empty<string>();
    private string[] Batches() => Directory.Exists(Tel) ? Directory.GetFiles(Tel, "batch-*") : Array.Empty<string>();
    private long Today => TelemetryStore.DayNumber(_local);

    private TelemetryStore Store(TelemetrySource source = TelemetrySource.Single) =>
        new(_dir, () => source, () => null, () => _local);

    private TelemetryStore YesStore()
    {
        var s = Store();
        s.SetInstallAnswer(true).Should().BeTrue();
        return s;
    }

    private TelemetryUploader Uploader(TelemetryStore store, FakeSender sender, double hoursLater = 0,
        TelemetrySource source = TelemetrySource.Steam) =>
        new(store, sender, () => _utc.AddHours(hoursLater), () => source);

    private static void OperatorOn() => TelemetryConsent.OperatorResolver = () => true;

    private static readonly string[] RowKeys =
    {
        "outcome", "player_class", "monster_family", "is_boss", "has_teammates",
        "player_level", "player_max_hp", "player_str", "player_dex", "player_weap_pow", "player_arm_pow",
        "monster_level", "monster_max_hp", "monster_str", "monster_def", "rounds",
        "damage_dealt", "damage_taken", "xp_gained", "gold_gained", "monster_count",
        "floor_actual", "difficulty", "party_size", "encounter_size", "first_actor",
        "dmg_to_player_basic", "dmg_to_player_ability", "dmg_to_player_spell", "dmg_to_player_dot",
        "dmg_to_team", "dmg_by_player", "dmg_by_team", "heal_player",
        "potions_used", "abilities_used", "spells_used", "teammates_lost", "player_hp_end",
    };

    private static readonly string[] BodyKeys = { "schema", "version", "source", "install_id", "rows" };

    /// <summary>A good row whose player_max_hp is <paramref name="id"/>, so each row can be told apart.</summary>
    private static JsonObject RowJson(long id, long level = 1)
    {
        var o = new JsonObject();
        foreach (var k in RowKeys) o[k] = 0;
        o["player_level"] = level;
        o["player_max_hp"] = id;
        return o;
    }

    private static TelemetryRow Row(long id)
    {
        using var doc = JsonDocument.Parse(RowJson(id).ToJsonString());
        return TelemetryRow.FromJson(doc.RootElement)!;
    }

    private string QueueLine(long id, long? day = null, long level = 1) =>
        new JsonObject { ["d"] = day ?? Today, ["r"] = RowJson(id, level) }.ToJsonString();

    private static void Append(TelemetryStore store, int from, int to, string? playerKey = null)
    {
        for (int i = from; i <= to; i++) store.Append(Row(i), playerKey);
    }

    private static long[] Ids(byte[] body)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("player_max_hp").GetInt64()).ToArray();
    }

    private static long[] QueueIds(IEnumerable<string> lines) =>
        lines.Select(l => { using var d = JsonDocument.Parse(l); return d.RootElement.GetProperty("r").GetProperty("player_max_hp").GetInt64(); }).ToArray();

    private static long[] Range(int from, int to) => Enumerable.Range(from, to - from + 1).Select(i => (long)i).ToArray();

    private static TelemetryReply Ok => new(200, "{\"ok\":true}");

    /// <summary>A sender that records each body and answers with the next scripted reply (a reply, or an
    /// exception to throw). The last reply repeats.</summary>
    private sealed class FakeSender : ITelemetrySender
    {
        private readonly List<Func<Task<TelemetryReply>>> _replies = new();
        private readonly object _gate = new();
        public readonly List<byte[]> Bodies = new();
        public Action? DuringSend;

        public FakeSender(params object[] replies)
        {
            foreach (var r in replies.Length == 0 ? new object[] { Ok } : replies)
            {
                switch (r)
                {
                    case TelemetryReply reply: _replies.Add(() => Task.FromResult(reply)); break;
                    case Exception ex: _replies.Add(() => Task.FromException<TelemetryReply>(ex)); break;
                    case Func<Task<TelemetryReply>> f: _replies.Add(f); break;
                    default: throw new ArgumentException("reply");
                }
            }
        }

        public int Calls { get { lock (_gate) return Bodies.Count; } }

        public Task<TelemetryReply> SendAsync(byte[] body)
        {
            int n;
            lock (_gate)
            {
                Bodies.Add(body);
                n = Bodies.Count - 1;
            }
            DuringSend?.Invoke();
            return _replies[Math.Min(n, _replies.Count - 1)]();
        }
    }

    // ======================================================================
    // Row 22: no network in tests
    // ======================================================================

    [Fact]
    public void Row22_AnUploaderWithoutAFake_ThrowsTheGuard_NoNetwork()
    {
        TelemetrySenderFactory.Create.Should().BeSameAs(TelemetryNoNetwork.Guard, "the test run's module initializer installed the guard");
        var store = YesStore();
        Append(store, 1, 3);
        Action build = () => new TelemetryUploader(store);
        build.Should().Throw<TelemetryNoNetwork.NetworkInTestException>("the uploader takes its sender from the factory");
        Action attach = () => TelemetryUploader.AttachTo(store);
        attach.Should().Throw<TelemetryNoNetwork.NetworkInTestException>();
        store.Uploader.Should().BeNull();
        Lines().Should().HaveCount(3, "nothing was taken");
    }

    [Fact]
    public async Task Row22_ARealSenderMadeWithoutTheFactory_RefusesToSend()
    {
        HttpTelemetrySender.Blocked.Should().NotBeNull("the test run's module initializer blocked the real sender");
        // a loopback target with nothing listening (the discard port): even if the block were gone, nothing
        // would leave the machine
        var sender = new HttpTelemetrySender(new Uri("http://127.0.0.1:9/"));
        try
        {
            Func<Task> send = () => sender.SendAsync(Encoding.UTF8.GetBytes("{}"));
            await send.Should().ThrowAsync<TelemetryNoNetwork.NetworkInTestException>();
        }
        finally { sender.Client.Dispose(); }
        new HttpTelemetrySender().Target.Should().Be(new Uri(HttpTelemetrySender.Endpoint), "the game's sender has the one fixed target");
    }

    // ======================================================================
    // Row 23: the real sender's settings, built without sending
    // ======================================================================

    [Fact]
    public void Row23_TheRealSender_FixedHttpsHost_Tls12And13_NormalCertificates_10Seconds_NoRedirects()
    {
        var sender = new HttpTelemetrySender();
        try
        {
            HttpTelemetrySender.Endpoint.Should().Be("https://usurper-reborn.net/api/telemetry/v1/combat");
            new Uri(HttpTelemetrySender.Endpoint).Scheme.Should().Be("https");
            sender.Handler.SslProtocols.Should().Be(SslProtocols.Tls12 | SslProtocols.Tls13);
            sender.Handler.ServerCertificateCustomValidationCallback.Should().BeNull("the normal certificate checks apply");
            sender.Handler.AllowAutoRedirect.Should().BeFalse();
            sender.Client.Timeout.Should().Be(TimeSpan.FromSeconds(10));
            sender.Client.BaseAddress.Should().BeNull();
            sender.Client.DefaultRequestHeaders.Should().BeEmpty("no header carries anything about the player");
        }
        finally { sender.Client.Dispose(); }
    }

    [Fact]
    public void N2_TheRealSender_KeepsNoCookies_SoAReplyCannotPlantAnIdentifier()
    {
        var sender = new HttpTelemetrySender();    // built, never sent
        try
        {
            sender.Handler.UseCookies.Should().BeFalse();
        }
        finally { sender.Client.Dispose(); }
    }

    // ======================================================================
    // Rows 24 and E: the batch and the body
    // ======================================================================

    [Fact]
    public async Task Row24_RowE_AtMost100Rows_TheBodyHasFixedKeys_NoWrapperNoTimeNoNames()
    {
        const string heroMark = "QXZBODYPLAYERMARK";
        const string monsterMark = "QXZBODYMONSTERMARK";
        var store = YesStore();
        var tally = new CombatRowTally(8, 1, 3, 2, 0, 120, 20, 0, 0, 80, 520, 200, 40, 1, 2, 0, 0, 460);
        var named = TelemetryRow.From(new CombatEventRow(heroMark, 10, "Warrior", 600, 60, 30, 25, 18, monsterMark, 8, 260, 40, 5,
            false, "victory", 6, 520, 140, 95, 30, 8, 2, false, tally), CharacterClass.Warrior, "Undead")!;
        store.Append(named);
        Append(store, 1, 149);
        Lines().Should().HaveCount(150);

        var fake = new FakeSender();
        (await Uploader(store, fake).UploadOnceAsync()).Should().Be(TelemetryUploadOutcome.Sent);
        fake.Calls.Should().Be(1);
        byte[] body = fake.Bodies[0];
        string text = Encoding.UTF8.GetString(body);
        text.Should().NotContain(heroMark).And.NotContain(monsterMark);
        text.Should().NotContain("\"d\"").And.NotContain("\"r\"", "the local wrapper stays local");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        root.EnumerateObject().Select(p => p.Name).Should().Equal(BodyKeys);
        root.GetProperty("schema").GetInt32().Should().Be(1);
        var expected = GameConfig.Version.Split('.').Select(long.Parse).ToArray();
        expected.Should().HaveCount(3);
        root.GetProperty("version").EnumerateArray().Select(v => v.GetInt64()).Should().Equal(expected);
        root.GetProperty("source").GetInt32().Should().Be(2, "the uploader's source (Steam here)");
        string id = root.GetProperty("install_id").GetString()!;
        id.Should().MatchRegex("^[0-9a-f]{32}$").And.Be(store.State.InstallId);
        var rows = root.GetProperty("rows").EnumerateArray().ToList();
        rows.Should().HaveCount(100, "at most 100 rows a request");
        foreach (var r in rows)
        {
            r.EnumerateObject().Select(p => p.Name).Should().Equal(RowKeys);
            foreach (var p in r.EnumerateObject())
            {
                p.Value.ValueKind.Should().Be(JsonValueKind.Number, p.Name);
                p.Value.TryGetInt64(out _).Should().BeTrue(p.Name);
            }
        }
        rows[0].GetProperty("player_max_hp").GetInt64().Should().Be(600, "the named fight's row, oldest first");
        Ids(body).Skip(1).Should().Equal(Range(1, 99));
        QueueIds(Lines()).Should().Equal(Range(100, 149), "the other 50 rows stay queued");
        Batches().Should().BeEmpty("the batch is deleted on a 2xx");
    }

    // ======================================================================
    // Row 25: two uploaders on one queue
    // ======================================================================

    [Fact]
    public async Task Row25_TwoUploaders_EveryRowSentExactlyOnce_RowsAppendedDuringAnUploadStayQueued()
    {
        var storeA = YesStore();
        Append(storeA, 1, 150);
        var inFlight = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fakeA = new FakeSender(new Func<Task<TelemetryReply>>(async () =>
        {
            inFlight.SetResult(true);
            await release.Task;
            throw new HttpRequestException("network down");
        }));
        var upA = Uploader(storeA, fakeA);
        var taskA = upA.UploadOnceAsync();
        (await Task.WhenAny(inFlight.Task, Task.Delay(TimeSpan.FromSeconds(30)))).Should().BeSameAs(inFlight.Task);
        Batches().Should().HaveCount(1, "A's rows are in its batch file while the request is out");

        var fakeB = new FakeSender();
        (await Uploader(Store(), fakeB, hoursLater: 2).UploadOnceAsync()).Should().Be(TelemetryUploadOutcome.Sent);
        Ids(fakeB.Bodies.Single()).Should().Equal(Range(101, 150), "B leaves A's batch in flight alone");

        Append(storeA, 151, 155);
        QueueIds(Lines()).Should().Equal(Range(151, 155), "rows appended during an upload stay queued");

        release.SetResult(true);
        (await taskA).Should().Be(TelemetryUploadOutcome.Kept);
        Ids(fakeA.Bodies.Single()).Should().Equal(Range(1, 100));
        QueueIds(Lines()).Should().Equal(Range(1, 100).Concat(Range(151, 155)), "A's rows are back at the head of the queue");
        Batches().Should().BeEmpty();

        var fakeC = new FakeSender();
        (await Uploader(Store(), fakeC, hoursLater: 4).UploadOnceAsync()).Should().Be(TelemetryUploadOutcome.Sent);
        var fakeD = new FakeSender();
        (await Uploader(Store(), fakeD, hoursLater: 6).UploadOnceAsync()).Should().Be(TelemetryUploadOutcome.Sent);
        var delivered = fakeB.Bodies.Concat(fakeC.Bodies).Concat(fakeD.Bodies).SelectMany(Ids).OrderBy(i => i).ToArray();
        delivered.Should().Equal(Range(1, 155), "every row delivered exactly once");
        Lines().Should().BeEmpty();
        Batches().Should().BeEmpty();
    }

    // ======================================================================
    // T1b additions: the answer read again under the lock; the operator switch; a No during the send
    // ======================================================================

    [Fact]
    public async Task Added_TheAnswerIsReadAgainUnderTheLock_ANoOnDiskSendsNothing()
    {
        var store = YesStore();
        Append(store, 1, 3);
        store.State.Yes.Should().BeTrue("this process cached the yes");
        // another copy of the game wrote a No (its queue delete did not reach this queue)
        File.WriteAllText(StateFile, "{\"asked\":1,\"yes\":0,\"last_upload\":0}");
        var fake = new FakeSender();
        (await Uploader(store, fake).UploadOnceAsync()).Should().Be(TelemetryUploadOutcome.NotAllowed);
        fake.Calls.Should().Be(0);
        Batches().Should().BeEmpty();
    }

    [Fact]
    public async Task Added_OperatorSwitchOff_SendsNothing_AndCreatesNoFolder()
    {
        OperatorOn();
        var store = Store(TelemetrySource.BbsDoor);
        store.SetPlayerAnswer("Player", true).Should().BeTrue();
        string key = TelemetryConsent.PlayerKey("Player")!;
        Append(store, 1, 3, key);
        Lines().Should().HaveCount(3);

        TelemetryConsent.OperatorResolver = () => false;
        var fake = new FakeSender();
        (await Uploader(store, fake, source: TelemetrySource.BbsDoor).UploadOnceAsync()).Should().Be(TelemetryUploadOutcome.NotAllowed);
        fake.Calls.Should().Be(0);
        Batches().Should().BeEmpty();

        // switch on again: the same rows go
        OperatorOn();
        (await Uploader(store, fake, source: TelemetrySource.BbsDoor).UploadOnceAsync()).Should().Be(TelemetryUploadOutcome.Sent);
        Ids(fake.Bodies.Single()).Should().Equal(Range(1, 3));

        // a server with the switch off and no telemetry folder: nothing is created
        TelemetryConsent.OperatorResolver = null;
        string other = Path.Combine(_dir, "server");
        Directory.CreateDirectory(other);
        var server = new TelemetryStore(other, () => TelemetrySource.Server, () => null, () => _local);
        (await new TelemetryUploader(server, fake, () => _utc, () => TelemetrySource.Server).UploadOnceAsync())
            .Should().Be(TelemetryUploadOutcome.NotAllowed);
        Directory.Exists(Path.Combine(other, "telemetry")).Should().BeFalse();
        fake.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Added_ANoDuringTheSend_TheRowsAreNotPutBack_AndNothingIsSentAfter()
    {
        var store = YesStore();
        Append(store, 1, 5);
        var fake = new FakeSender(new HttpRequestException("network down"));
        fake.DuringSend = () => store.SetInstallAnswer(false).Should().BeTrue();
        (await Uploader(store, fake).UploadOnceAsync()).Should().Be(TelemetryUploadOutcome.Kept);
        Lines().Should().BeEmpty("the No deleted the queue and the network failure put nothing back");
        Batches().Should().BeEmpty();
        var later = new FakeSender();
        (await Uploader(Store(), later, hoursLater: 2).UploadOnceAsync()).Should().Be(TelemetryUploadOutcome.NotAllowed);
        later.Calls.Should().Be(0);
    }

    // ======================================================================
    // T1b2 N1: a withdrawn player's rows never return, even when the batch in flight cannot be deleted
    // ======================================================================

    private long GenerationOf(string dir) =>
        new TelemetryStore(dir, () => TelemetrySource.Single, () => null, () => _local).State.Generation;

    [Fact]
    public async Task N1_APlayerNoDuringTheSend_ABatchThatCannotBeDeleted_IsNotPutBack_AndNeverSent()
    {
        OperatorOn();
        var store = Store(TelemetrySource.BbsDoor);
        store.SetPlayerAnswer("Player", true).Should().BeTrue();
        Append(store, 1, 5, TelemetryConsent.PlayerKey("Player"));
        long before = GenerationOf(_dir);
        var fake = new FakeSender(new HttpRequestException("network down"));
        fake.DuringSend = () =>
        {
            // on Windows the sending node holds its batch open, so the No cannot delete it
            store.DeleteBatchFile = _ => throw new IOException("held open by another node");
            store.SetPlayerAnswer("Player", false).Should().BeTrue();
            Batches().Should().ContainSingle("the batch in flight could not be deleted");
        };
        (await Uploader(store, fake, source: TelemetrySource.BbsDoor).UploadOnceAsync()).Should().Be(TelemetryUploadOutcome.Kept);
        store.DeleteBatchFile = File.Delete;

        GenerationOf(_dir).Should().Be(before + 1, "the No advanced the queue generation");
        Lines().Should().BeEmpty("the batch was taken under the old generation, so its rows are not put back");
        Batches().Should().BeEmpty("the sending node deleted its batch once it let go of it");

        // the switch is still on and another player says yes: the withdrawn rows are never sent
        store.SetPlayerAnswer("Other", true).Should().BeTrue();
        var later = new FakeSender();
        (await Uploader(store, later, hoursLater: 2, source: TelemetrySource.BbsDoor).UploadOnceAsync())
            .Should().Be(TelemetryUploadOutcome.NothingQueued);
        later.Calls.Should().Be(0);
        fake.Calls.Should().Be(1);
    }

    [Fact]
    public async Task N1_EveryWithdrawal_AdvancesTheQueueGeneration_AYesDoesNot()
    {
        var store = YesStore();
        GenerationOf(_dir).Should().Be(0);
        store.SetInstallAnswer(true).Should().BeTrue();
        GenerationOf(_dir).Should().Be(0, "a yes is not a withdrawal");
        store.SetInstallAnswer(false).Should().BeTrue();
        GenerationOf(_dir).Should().Be(1, "an install No");
        store.SetInstallAnswer(true).Should().BeTrue();
        store.InstallAskInterrupted().Should().BeTrue();
        GenerationOf(_dir).Should().Be(2, "an interrupted ask deletes the queue too");
        store.SetInstallAnswer(true).Should().BeTrue();
        Append(store, 1, 3);
        (await Uploader(store, new FakeSender(new TelemetryReply(200, "{\"stop\":true}"))).UploadOnceAsync())
            .Should().Be(TelemetryUploadOutcome.Stopped);
        GenerationOf(_dir).Should().Be(3, "the server's stop");
        JsonNode.Parse(File.ReadAllText(StateFile))!["generation"]!.GetValue<long>().Should().Be(3);

        OperatorOn();
        string shared = Path.Combine(_dir, "bbs");
        Directory.CreateDirectory(shared);
        var bbs = new TelemetryStore(shared, () => TelemetrySource.BbsDoor, () => null, () => _local);
        bbs.SetPlayerAnswer("Player", true).Should().BeTrue();
        GenerationOf(shared).Should().Be(0);
        bbs.SetPlayerAnswer("Player", false).Should().BeTrue();
        GenerationOf(shared).Should().Be(1, "a shared player's No");
        bbs.OperatorTurnedOff();
        GenerationOf(shared).Should().Be(2, "the operator switch off");
    }

    // ======================================================================
    // T1b addition and row D: a damaged queue line is never sent
    // ======================================================================

    [Fact]
    public async Task Added_ADamagedQueueLine_IsNeverSent()
    {
        var store = YesStore();
        File.WriteAllLines(QueueFile, new[]
        {
            QueueLine(1),
            "{\"d\":" + Today + ",\"r\":{\"outcome\":0",                // half written
            QueueLine(2, level: 500),                                    // outside the bounds
            "{\"d\":" + Today + ",\"r\":" + RowJson(3).ToJsonString() + ",\"who\":1}",   // extra wrapper key
            QueueLine(4),
        });
        var fake = new FakeSender();
        (await Uploader(store, fake).UploadOnceAsync()).Should().Be(TelemetryUploadOutcome.Sent);
        Ids(fake.Bodies.Single()).Should().Equal(1, 4);
        using var doc = JsonDocument.Parse(fake.Bodies[0]);    // by structure: the random install_id may hold any digits
        foreach (var row in doc.RootElement.GetProperty("rows").EnumerateArray())
        {
            row.GetProperty("player_level").GetInt64().Should().Be(1, "the out of bounds row is not sent");
            row.EnumerateObject().Select(p => p.Name).Should().Equal(RowKeys);
        }
        Encoding.UTF8.GetString(fake.Bodies[0]).Should().NotContain("\"who\"");
        Lines().Should().BeEmpty("damaged lines are dropped, not kept for later");
    }

    [Fact]
    public async Task Added_ALineOlderThan30Days_IsNeverSent()
    {
        var store = YesStore();
        File.WriteAllLines(QueueFile, new[] { QueueLine(1, Today - 31), QueueLine(2, Today - 30), QueueLine(3) });
        var fake = new FakeSender();
        (await Uploader(store, fake).UploadOnceAsync()).Should().Be(TelemetryUploadOutcome.Sent);
        Ids(fake.Bodies.Single()).Should().Equal(2, 3);
    }

    // ======================================================================
    // Row 26: failure handling
    // ======================================================================

    [Fact]
    public async Task Row26_NetworkErrorOrTimeout_RowsBackInTheQueue_OneLogLineASession()
    {
        var store = YesStore();
        Append(store, 1, 5);
        var fake = new FakeSender(new HttpRequestException("network down"), new TaskCanceledException("timeout"));
        double hours = 0;
        var up = new TelemetryUploader(store, fake, () => _utc.AddHours(hours), () => TelemetrySource.Steam);
        (await up.UploadOnceAsync()).Should().Be(TelemetryUploadOutcome.Kept);
        QueueIds(Lines()).Should().Equal(Range(1, 5));
        Batches().Should().BeEmpty();
        up.FailureLogs.Should().Be(1);

        hours = 2;    // the same session, later
        (await up.UploadOnceAsync()).Should().Be(TelemetryUploadOutcome.Kept, "a timeout keeps the rows too");
        fake.Calls.Should().Be(2);
        QueueIds(Lines()).Should().Equal(Range(1, 5));
        up.FailureLogs.Should().Be(1, "one log line a session");
        Store().State.StoppedVersion.Should().BeNull("a failure is never a stop");
    }

    [Fact]
    public async Task Row26_Http400_TheBatchIsDropped_NotRetried()
    {
        var store = YesStore();
        Append(store, 1, 5);
        var fake = new FakeSender(new TelemetryReply(400, "{\"error\":\"bad\"}"));
        (await Uploader(store, fake).UploadOnceAsync()).Should().Be(TelemetryUploadOutcome.Dropped);
        Lines().Should().BeEmpty();
        Batches().Should().BeEmpty();
        (await Uploader(store, fake, hoursLater: 2).UploadOnceAsync()).Should().Be(TelemetryUploadOutcome.NothingQueued);
        fake.Calls.Should().Be(1, "never retried");
    }

    [Fact]
    public async Task Row26_Http429_RowsKept_TheNextAttemptAfterOneHour()
    {
        var store = YesStore();
        Append(store, 1, 5);
        var fake = new FakeSender(new TelemetryReply(429, "{\"error\":\"slow down\"}"), Ok);
        (await Uploader(store, fake).UploadOnceAsync()).Should().Be(TelemetryUploadOutcome.Kept);
        QueueIds(Lines()).Should().Equal(Range(1, 5));
        (await Uploader(Store(), fake, hoursLater: 0.5).UploadOnceAsync()).Should().Be(TelemetryUploadOutcome.TooSoon);
        fake.Calls.Should().Be(1);
        (await Uploader(Store(), fake, hoursLater: 1.02).UploadOnceAsync()).Should().Be(TelemetryUploadOutcome.Sent);
        Ids(fake.Bodies[1]).Should().Equal(Range(1, 5));
        Lines().Should().BeEmpty();
    }

    [Fact]
    public async Task Row26_Http500AndBadReplies_RowsKept_NeverAStop()
    {
        var store = YesStore();
        Append(store, 1, 5);
        var replies = new[]
        {
            new TelemetryReply(500, "{\"ok\":true}"),
            new TelemetryReply(200, "not json"),
            new TelemetryReply(200, "{\"ok\":false}"),
            new TelemetryReply(204, ""),
            new TelemetryReply(302, "{\"ok\":true}"),
        };
        var fake = new FakeSender(replies.Cast<object>().ToArray());
        for (int i = 0; i < replies.Length; i++)
        {
            (await Uploader(Store(), fake, hoursLater: 2 * i).UploadOnceAsync()).Should().Be(TelemetryUploadOutcome.Kept, "reply {0}", i);
            QueueIds(Lines()).Should().Equal(Range(1, 5));
        }
        fake.Calls.Should().Be(replies.Length);
        Batches().Should().BeEmpty();
        Store().State.StoppedVersion.Should().BeNull();
    }

    // ======================================================================
    // Row 27: the remote stop
    // ======================================================================

    [Fact]
    public async Task Row27_Stop_DeletesTheQueue_RefusesAppends_UntilTheVersionChanges()
    {
        var store = YesStore();
        Append(store, 1, 5);
        var fake = new FakeSender(new TelemetryReply(200, "{\"stop\":true}"));
        var up = Uploader(store, fake);
        (await up.UploadOnceAsync()).Should().Be(TelemetryUploadOutcome.Stopped);
        File.Exists(QueueFile).Should().BeFalse();
        Batches().Should().BeEmpty();
        Store().State.StoppedVersion.Should().Be(GameConfig.Version);
        up.StopNotWritten.Should().Be(0);

        Append(store, 6, 6);
        File.Exists(QueueFile).Should().BeFalse("appends are refused in this process");
        var nextStart = Store();
        nextStart.ShouldQueue(null).Should().BeTrue("the answer is still yes");
        Append(nextStart, 7, 7);
        File.Exists(QueueFile).Should().BeFalse("and in the next process of the same version");
        (await Uploader(nextStart, fake, hoursLater: 2).UploadOnceAsync()).Should().Be(TelemetryUploadOutcome.NotAllowed);
        fake.Calls.Should().Be(1);

        // another game version: the stop no longer holds
        var s = JsonNode.Parse(File.ReadAllText(StateFile))!.AsObject();
        s["stopped_version"] = "0.0.1";
        File.WriteAllText(StateFile, s.ToJsonString());
        var newVersion = Store();
        Append(newVersion, 8, 8);
        QueueIds(Lines()).Should().Equal(8);
        (await Uploader(newVersion, new FakeSender(), hoursLater: 4).UploadOnceAsync()).Should().Be(TelemetryUploadOutcome.Sent);
    }

    [Fact]
    public async Task Row27_AnErrorATimeoutA5xxStopOrOk_NeverStops()
    {
        var store = YesStore();
        var fake = new FakeSender(
            new HttpRequestException("network down"),
            new TaskCanceledException("timeout"),
            new TelemetryReply(503, "{\"stop\":true}"),
            new TelemetryReply(400, "{\"stop\":true}"),
            Ok);
        for (int i = 0; i < 5; i++)
        {
            Append(store, 10 * i + 1, 10 * i + 2);
            var outcome = await Uploader(Store(), fake, hoursLater: 2 * i).UploadOnceAsync();
            outcome.Should().NotBe(TelemetryUploadOutcome.Stopped, "reply {0}", i);
            outcome.Should().NotBe(TelemetryUploadOutcome.NotAllowed, "reply {0}", i);
            Store().State.StoppedVersion.Should().BeNull("reply {0}", i);
            int before = Lines().Length;
            Append(store, 100 + i, 100 + i);
            Lines().Should().HaveCount(before + 1, "appends go on after reply {0}", i);
        }
        fake.Calls.Should().Be(5);
    }

    // ======================================================================
    // Row 28: cadence
    // ======================================================================

    [Fact]
    public async Task Row28_At100Rows_AtMostOnceAnHour_ByLastUploadOnDisk()
    {
        var store = YesStore();
        var fake = new FakeSender();
        double hours = 0;
        var up = new TelemetryUploader(store, fake, () => _utc.AddHours(hours), () => TelemetrySource.Steam);
        store.Uploader = up;

        Append(store, 1, 99);
        up.LastTriggered.Should().BeNull("99 rows do not start an upload");
        Append(store, 100, 100);
        up.LastTriggered.Should().NotBeNull();
        (await up.LastTriggered!).Should().Be(TelemetryUploadOutcome.Sent);
        Ids(fake.Bodies.Single()).Should().Equal(Range(1, 100));
        var stamp = JsonNode.Parse(File.ReadAllText(StateFile))!["last_upload"]!.GetValue<long>();
        stamp.Should().Be(TelemetryUploader.Unix(_utc), "last_upload is on disk");

        var first = up.LastTriggered;
        hours = 0.5;
        Append(store, 101, 200);
        if (up.LastTriggered != first) (await up.LastTriggered!).Should().Be(TelemetryUploadOutcome.TooSoon);
        fake.Calls.Should().Be(1, "not twice inside the hour");

        // a second launch inside the hour (a new process: nothing in memory) does not post either
        var relaunch = Uploader(Store(), fake, hoursLater: 0.9);
        (await relaunch.UploadOnceAsync()).Should().Be(TelemetryUploadOutcome.TooSoon);
        fake.Calls.Should().Be(1);

        hours = 1.01;
        Append(store, 201, 201);
        up.LastTriggered.Should().NotBeSameAs(first);
        (await up.LastTriggered!).Should().Be(TelemetryUploadOutcome.Sent);
        Ids(fake.Bodies[1]).Should().Equal(Range(101, 200));
        QueueIds(Lines()).Should().Equal(201);
    }

    [Fact]
    public async Task Row28_TheStartUpload_RunsInTheBackground_OncePerProcess()
    {
        var startedField = typeof(TelemetryUploader).GetField("_started", SNP)!;
        object? startedBefore = startedField.GetValue(null);
        var factoryBefore = TelemetrySenderFactory.Create;
        var backend = new FileSaveBackend();
        typeof(FileSaveBackend).GetField("baseSaveDirectory", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(backend, _dir);
        SaveSystem.InitializeWithBackend(backend);
        var store = TelemetryConsent.CurrentStore()!;
        store.Folder.Should().StartWith(TelemetryTestRoot.Root);
        store.SetInstallAnswer(true).Should().BeTrue();
        Append(store, 1, 3);

        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fake = new FakeSender(new Func<Task<TelemetryReply>>(async () =>
        {
            entered.SetResult(true);
            await release.Task;
            return Ok;
        }));
        try
        {
            TelemetrySenderFactory.Create = () => fake;
            startedField.SetValue(null, 0);
            TelemetryUploader.StartInBackground();
            var start = TelemetryUploader.StartTask!;
            (await Task.WhenAny(entered.Task, Task.Delay(TimeSpan.FromSeconds(30)))).Should().BeSameAs(entered.Task);
            start.IsCompleted.Should().BeFalse("the caller went on while the request was out");
            TelemetryUploader.StartInBackground();
            TelemetryUploader.StartTask.Should().BeSameAs(start, "once a process");
            release.SetResult(true);
            await start;
            Ids(fake.Bodies.Single()).Should().Equal(Range(1, 3));
            store.Uploader.Should().NotBeNull("later appends tell this uploader");
        }
        finally
        {
            TelemetrySenderFactory.Create = factoryBefore;
            startedField.SetValue(null, startedBefore);
            store.Uploader = null;
        }
    }

    // ======================================================================
    // Row 29: a batch left by a crash is sent once at start
    // ======================================================================

    [Fact]
    public async Task Row29_ALeftoverBatch_IsSentOnceAtStart()
    {
        var store = YesStore();
        Append(store, 10, 11);
        File.WriteAllLines(Path.Combine(Tel, "batch-0123456789abcdef0123456789abcdef.jsonl"), new[] { QueueLine(1), QueueLine(2), QueueLine(3) });
        var fake = new FakeSender();
        (await Uploader(Store(), fake).UploadOnceAsync()).Should().Be(TelemetryUploadOutcome.Sent);
        Ids(fake.Bodies.Single()).Should().Equal(1, 2, 3);
        Batches().Should().BeEmpty();
        QueueIds(Lines()).Should().Equal(10, 11);
        (await Uploader(Store(), fake, hoursLater: 2).UploadOnceAsync()).Should().Be(TelemetryUploadOutcome.Sent);
        Ids(fake.Bodies[1]).Should().Equal(10, 11);
        fake.Bodies.SelectMany(Ids).Should().OnlyHaveUniqueItems();
    }

    // ======================================================================
    // T1b addition: a false from the store is checked, not swallowed
    // ======================================================================

    private void BlockStateWrites() => Directory.CreateDirectory(StateFile + ".tmp");

    [Fact]
    public async Task Added_TheUploadTimeNotWritten_IsHeldInMemory_NoSecondPostInsideTheHour()
    {
        var store = YesStore();
        Append(store, 1, 3);
        BlockStateWrites();
        var fake = new FakeSender();
        double hours = 0;
        var up = new TelemetryUploader(store, fake, () => _utc.AddHours(hours), () => TelemetrySource.Steam);
        (await up.UploadOnceAsync()).Should().Be(TelemetryUploadOutcome.Sent);
        up.UploadTimeNotWritten.Should().Be(1);
        JsonNode.Parse(File.ReadAllText(StateFile))!["last_upload"]!.GetValue<long>().Should().Be(0, "the write failed");
        Append(store, 4, 6);
        hours = 0.5;
        (await up.UploadOnceAsync()).Should().Be(TelemetryUploadOutcome.TooSoon);
        fake.Calls.Should().Be(1);
        hours = 1.01;
        (await up.UploadOnceAsync()).Should().Be(TelemetryUploadOutcome.Sent);
        Ids(fake.Bodies[1]).Should().Equal(Range(4, 6));
    }

    [Fact]
    public async Task Added_TheStopNotWritten_IsReported_AndHoldsInThisProcess()
    {
        var store = YesStore();
        Append(store, 1, 3);
        BlockStateWrites();
        var fake = new FakeSender(new TelemetryReply(200, "{\"stop\":true}"));
        var up = Uploader(store, fake);
        (await up.UploadOnceAsync()).Should().Be(TelemetryUploadOutcome.Stopped);
        up.StopNotWritten.Should().Be(1, "the store's false is reported");
        File.Exists(QueueFile).Should().BeFalse();
        Append(store, 4, 4);
        File.Exists(QueueFile).Should().BeFalse("the stop holds in this process");
        (await Uploader(store, fake, hoursLater: 2).UploadOnceAsync()).Should().Be(TelemetryUploadOutcome.NotAllowed);
        fake.Calls.Should().Be(1);
    }
}
