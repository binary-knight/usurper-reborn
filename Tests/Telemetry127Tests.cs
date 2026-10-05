using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake;
using UsurperRemake.BBS;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.7: the opt-in combat telemetry row, its bounds, the consent store and the local queue
/// (DESIGN.md sections 1 to 3, T1-tests.md rows 1 to 21 and A, B, C, D, F, G). Fights are real
/// PlayerVsMonsters fights through a scripted terminal; every folder is a temp folder.
/// </summary>
[Collection("SharedGameSingletons")]
public class Telemetry127Tests : IDisposable
{
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly FieldInfo OsmField = typeof(OnlineStateManager).GetField("_fallbackInstance", SNP)!;
    private static readonly FieldInfo SaveField = typeof(SaveSystem).GetField("instance", SNP)!;
    private static readonly FieldInfo DoorField = typeof(DoorMode).GetField("_sessionInfo", SNP)!;
    private static readonly FieldInfo OnlineField = typeof(DoorMode).GetField("_onlineMode", SNP)!;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"usurper-telemetry-{Guid.NewGuid():N}");
    private readonly object? _osmBefore = OsmField.GetValue(null);
    private readonly object? _saveBefore = SaveField.GetValue(null);
    private readonly object? _doorBefore = DoorField.GetValue(null);
    private readonly object? _onlineBefore = OnlineField.GetValue(null);
    private DateTime _now = new(2026, 10, 5, 12, 0, 0);
    private SqlSaveBackend? _db;

    public Telemetry127Tests()
    {
        Directory.CreateDirectory(_dir);
        TelemetryConsent.ResetForTests();
    }

    public void Dispose()
    {
        OsmField.SetValue(null, _osmBefore);
        SaveField.SetValue(null, _saveBefore);
        DoorField.SetValue(null, _doorBefore);
        OnlineField.SetValue(null, _onlineBefore);
        TelemetryConsent.ResetForTests();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    // ---------- folders and stores ----------

    private string Tel => Path.Combine(_dir, "telemetry");
    private string QueueFile => Path.Combine(Tel, "queue.jsonl");
    private string StateFile => Path.Combine(Tel, "state.json");
    private string[] Lines() => File.Exists(QueueFile) ? File.ReadAllLines(QueueFile) : Array.Empty<string>();
    private static string[] LinesIn(TelemetryStore store) => File.Exists(store.QueuePath) ? File.ReadAllLines(store.QueuePath) : Array.Empty<string>();

    /// <summary>The folder a save directory's store uses in this test run: under the run's temp root
    /// (TelemetryTestRoot), never the directory itself.</summary>
    private static string MappedTel(string saveDirectory) => Path.Combine(TelemetryConsent.FullSaveDirectory(saveDirectory), "telemetry");

    private SqlSaveBackend Db => _db ??= new SqlSaveBackend(Path.Combine(_dir, "game.db"));

    private TelemetryStore Store(TelemetrySource source = TelemetrySource.Single, string? dir = null) =>
        new(dir ?? _dir, () => source, () => _db, () => _now);

    private TelemetryStore YesStore()
    {
        var s = Store();
        s.SetInstallAnswer(true);
        return s;
    }

    private static void OperatorOn() => TelemetryConsent.OperatorResolver = () => true;

    private void Door()
    {
        DoorField.SetValue(null, new BBSSessionInfo { SourceType = DropFileType.DoorSys, UserName = "Player", UserAlias = "Player" });
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

    /// <summary>Online mode as PlayerSession sets it (CombatEventsDb126Tests): the SQL backend and an
    /// OnlineStateManager, so the combat_events row is written beside the telemetry row.</summary>
    private void GoOnline()
    {
        SaveSystem.InitializeWithBackend(Db);
        var osm = (OnlineStateManager)Activator.CreateInstance(typeof(OnlineStateManager),
            BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { Db, "tester" }, null)!;
        OsmField.SetValue(null, osm);
    }

    private FileSaveBackend FileBackend(string dir)
    {
        var backend = new FileSaveBackend();
        typeof(FileSaveBackend).GetField("baseSaveDirectory", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(backend, dir);
        return backend;
    }

    private List<Dictionary<string, object?>> Rows(string sql)
    {
        using var c = new SqliteConnection($"Data Source={Db.DatabasePath};Pooling=false");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        var list = new List<Dictionary<string, object?>>();
        while (r.Read())
        {
            var d = new Dictionary<string, object?>();
            for (int i = 0; i < r.FieldCount; i++) d[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
            list.Add(d);
        }
        return list;
    }

    private bool ConsentTableExists() =>
        Rows("SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'telemetry_consent';").Count == 1;

    private static Dictionary<string, JsonElement> RowOf(string line)
    {
        using var doc = JsonDocument.Parse(line);
        return doc.RootElement.GetProperty("r").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    // ---------- fight harness (as CombatEventsDb126Tests) ----------

    private sealed class ScriptedStream : Stream
    {
        private readonly byte[] _data;
        private int _pos;
        public ScriptedStream(string script) { _data = Encoding.UTF8.GetBytes(script); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos >= _data.Length) return 0;
            int n = Math.Min(count, _data.Length - _pos);
            Array.Copy(_data, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => Task.FromResult(Read(buffer, offset, count));
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static string Attacks(int n = 30) => string.Concat(Enumerable.Repeat("A\n\n", n)) + string.Concat(Enumerable.Repeat("P\n", 10));
    private static string Retreats(int n = 30) => string.Concat(Enumerable.Repeat("R\n", n)) + string.Concat(Enumerable.Repeat("\n", 10));

    private static Character Hero(string name = "Tester", long hp = 600, int level = 10, CharacterClass cls = CharacterClass.Warrior) => new Character
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Human,
        Class = cls, Race = CharacterRace.Human, Level = level,
        HP = hp, MaxHP = 600, BaseMaxHP = 600,
        Strength = 60, BaseStrength = 60, Defence = 20, BaseDefence = 20,
        Dexterity = 30, BaseDexterity = 30, Agility = 20, BaseAgility = 20,
        Constitution = 30, BaseConstitution = 30, Intelligence = 20, BaseIntelligence = 20,
        Wisdom = 20, BaseWisdom = 20, Stamina = 100, BaseStamina = 100,
        Mental = 80, Gold = 0, Healing = 0, AutoHeal = false,
        CombatSpeed = CombatSpeed.Instant,
    };

    private static Monster Brute(string name = "Brute", long hp = 260, long str = 40, string family = "Undead") => new Monster
    {
        Name = name, Level = 8, HP = hp, MaxHP = hp, Strength = str, Defence = 5, Experience = 1, Gold = 0,
        IsActive = true, FamilyName = family,
    };

    private sealed class Fight
    {
        public CombatEngine Engine = null!;
        public MemoryStream Output = new();
        public Character Hero = null!;
        public List<Monster> Monsters = null!;
        public CombatResult? Result;
        public Exception? Error;
        public TimeSpan Elapsed;
        public string Transcript => Encoding.UTF8.GetString(Output.ToArray());
    }

    private static async Task<Fight> RunFight(TelemetryStore? store, Character? hero = null, List<Monster>? monsters = null, string? script = null,
        Action<CombatEngine>? prepare = null, int seed = 127)
    {
        var f = new Fight { Hero = hero ?? Hero(), Monsters = monsters ?? new List<Monster> { Brute("Brute A"), Brute("Brute B") } };
        var term = new TerminalEmulator(new ScriptedStream(script ?? Attacks()), f.Output);
        f.Engine = new CombatEngine(term) { TelemetryStoreOverride = store };
        prepare?.Invoke(f.Engine);
        f.Engine.SeedRandomForTests(seed);
        var sw = Stopwatch.StartNew();
        try
        {
            var task = f.Engine.PlayerVsMonsters(f.Hero, f.Monsters, offerMonkEncounter: false);
            var done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(60)));
            if (done != task) throw new TimeoutException("the fight did not end");
            f.Result = await task;
        }
        catch (Exception ex) { f.Error = ex; }
        f.Elapsed = sw.Elapsed;
        return f;
    }

    private static async Task Appended(Fight f)
    {
        f.Engine.LastTelemetryAppend.Should().NotBeNull("the fight queued a row");
        var t = f.Engine.LastTelemetryAppend!;
        (await Task.WhenAny(t, Task.Delay(TimeSpan.FromSeconds(30)))).Should().BeSameAs(t, "the background append ends");
    }

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

    private static CombatEventRow SampleRow(string outcome = "victory", bool boss = false, bool team = false, CombatRowTally? tally = null, string? monsterName = "Brute") =>
        new("A Player", 10, "Warrior", 600, 60, 30, 25, 18, monsterName, 8, 260, 40, 5, boss, outcome, 6, 520, 140, 95, 30, 8, 2, team,
            tally ?? new CombatRowTally(8, 1, 3, 2, 0, 120, 20, 0, 0, 80, 520, 200, 40, 1, 2, 0, 0, 460));

    // ======================================================================
    // Row 1: never asked, nothing queued (the real path through SaveSystem)
    // ======================================================================

    [Fact]
    public async Task Row1_NeverAsked_RealFight_QueuesNothing()
    {
        SaveSystem.InitializeWithBackend(FileBackend(_dir));
        var store = TelemetryConsent.CurrentStore()!;
        store.Folder.Should().Be(MappedTel(_dir)).And.StartWith(TelemetryTestRoot.Root);
        var f = await RunFight(null);
        f.Error.Should().BeNull("{0}", f.Transcript);
        f.Result!.Tally.Ended.Should().BeTrue();
        f.Engine.LastTelemetryAppend.Should().BeNull();
        File.Exists(store.QueuePath).Should().BeFalse();
        Directory.Exists(store.Folder).Should().BeFalse("a never asked install creates nothing");

        // the same path with a stored yes queues the fight
        TelemetryConsent.StoreFor(_dir).SetInstallAnswer(true).Should().BeTrue();
        var g = await RunFight(null);
        g.Error.Should().BeNull("{0}", g.Transcript);
        await Appended(g);
        LinesIn(store).Should().HaveCount(1);
        Directory.Exists(Tel).Should().BeFalse("the save directory itself is never touched in a test run");
    }

    // ======================================================================
    // Row 2: a stored No, nothing queued
    // ======================================================================

    [Fact]
    public async Task Row2_StoredNo_QueuesNothing()
    {
        Directory.CreateDirectory(Tel);
        File.WriteAllText(StateFile, "{\"asked\":1,\"yes\":0,\"install_id\":\"0123456789abcdef0123456789abcdef\"}");
        var store = Store();
        store.State.Asked.Should().BeTrue();
        store.State.Yes.Should().BeFalse();
        var f = await RunFight(store);
        f.Error.Should().BeNull("{0}", f.Transcript);
        f.Result!.Tally.Ended.Should().BeTrue();
        f.Engine.LastTelemetryAppend.Should().BeNull();
        File.Exists(QueueFile).Should().BeFalse();
    }

    // ======================================================================
    // Row 3: a stored yes, one row per fight, equal to the combat_events row
    // ======================================================================

    private async Task YesFightMatchesCombatEventsRow(string outcome, Character hero, List<Monster> monsters, string script, CombatOutcome? expected)
    {
        GoOnline();
        ServerMode();
        OperatorOn();
        var store = Store(TelemetrySource.Server);
        store.SetPlayerAnswer(TelemetryConsent.CurrentLoginName(), true);
        var f = await RunFight(store, hero, monsters, script);
        f.Error.Should().BeNull("{0}", f.Transcript);
        if (expected != null) f.Result!.Outcome.Should().Be(expected.Value, f.Transcript);
        await Appended(f);
        await f.Engine.LastCombatRowWrite!;

        var lines = Lines();
        lines.Should().HaveCount(1, "one row per fight");
        var row = RowOf(lines[0]);
        var db = Rows("SELECT * FROM combat_events;").Single();
        db["outcome"].Should().Be(outcome);
        row["outcome"].GetInt64().Should().Be(outcome switch { "victory" => 0, "fled" => 1, _ => 2 });
        row["player_class"].GetInt64().Should().Be((long)Enum.Parse<CharacterClass>((string)db["player_class"]!));
        row["monster_family"].GetInt64().Should().Be(2, "the monster is Undead");
        foreach (var key in RowKeys.Except(new[] { "outcome", "player_class", "monster_family" }))
            row[key].GetInt64().Should().Be(Convert.ToInt64(db[key]), key);
        var t = f.Result!.Tally;
        row["dmg_by_player"].GetInt64().Should().Be(t.DmgByPlayer);
        row["player_hp_end"].GetInt64().Should().Be(t.PlayerHpEnd);
    }

    [Fact]
    public Task Row3_StoredYes_Victory_OneRowEqualToTheCombatEventsRow() =>
        YesFightMatchesCombatEventsRow("victory", Hero(), new List<Monster> { Brute("Brute A"), Brute("Brute B") }, Attacks(), CombatOutcome.Victory);

    [Fact]
    public Task Row3_StoredYes_Fled_OneRowEqualToTheCombatEventsRow() =>
        YesFightMatchesCombatEventsRow("fled", Hero(), new List<Monster> { Brute("Brute A"), Brute("Brute B") }, Retreats(), CombatOutcome.PlayerEscaped);

    /// <summary>A death that reaches the dashboard row: with resurrections left (online, none left is the
    /// permadeath path, which writes neither row).</summary>
    [Fact]
    public Task Row3_StoredYes_Death_OneRowEqualToTheCombatEventsRow()
    {
        var hero = Hero(hp: 20);
        hero.Resurrections = 3;
        return YesFightMatchesCombatEventsRow("death", hero, new List<Monster> { Brute("Brute A", 5000, 400) }, Attacks(), null);   // a resurrection turns the outcome into an escape; the row says death
    }

    // ======================================================================
    // Rows 4 and F: the key list, and a wrapper that holds the day only
    // ======================================================================

    [Fact]
    public async Task Row4_RowF_KeyListFixed_WrapperHoldsTheDayOnly()
    {
        TelemetryRow.Columns.Select(c => c.Key).Should().Equal(RowKeys);
        var f = await RunFight(YesStore());
        f.Error.Should().BeNull("{0}", f.Transcript);
        await Appended(f);
        var line = Lines().Single();
        using var doc = JsonDocument.Parse(line);
        doc.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal(new[] { "d", "r" }, "the wrapper holds the queue day and the row");
        doc.RootElement.GetProperty("d").GetInt64().Should().Be((long)(_now.Date - new DateTime(1970, 1, 1)).TotalDays, "the day is the local day number");
        doc.RootElement.GetProperty("r").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(RowKeys);
        doc.RootElement.GetProperty("r").EnumerateObject().Should().HaveCount(39);
        string? key = TelemetryConsent.PlayerKey(TelemetryConsent.CurrentLoginName());
        line.Should().NotContain(key!, "no player key in the wrapper or the row");
    }

    // ======================================================================
    // Row 5: no names, no free text
    // ======================================================================

    [Fact]
    public async Task Row5_NoNamesNoFreeText_EveryValueAnInteger()
    {
        const string heroMark = "QXZPLAYERMARK";
        const string monsterMark = "QXZMONSTERMARK";
        var built = TelemetryRow.From(SampleRow(monsterName: monsterMark), CharacterClass.Warrior, "Undead")!;
        built.ToJson().ToJsonString().Should().NotContain(monsterMark);

        var f = await RunFight(YesStore(), Hero(heroMark), new List<Monster> { Brute(monsterMark + " A"), Brute(monsterMark + " B") });
        f.Error.Should().BeNull("{0}", f.Transcript);
        f.Transcript.Should().Contain(monsterMark, "the markers were really in the fight");
        await Appended(f);
        Lines().Should().HaveCount(1);
        foreach (var file in Directory.GetFiles(Tel, "*", SearchOption.AllDirectories))
        {
            string bytes = Encoding.UTF8.GetString(File.ReadAllBytes(file));
            bytes.Should().NotContain(heroMark, file).And.NotContain(monsterMark, file);
        }
        using var doc = JsonDocument.Parse(Lines()[0]);
        doc.RootElement.GetProperty("d").ValueKind.Should().Be(JsonValueKind.Number);
        foreach (var p in doc.RootElement.GetProperty("r").EnumerateObject())
        {
            p.Value.ValueKind.Should().Be(JsonValueKind.Number, p.Name);
            p.Value.TryGetInt64(out _).Should().BeTrue(p.Name);
        }
    }

    // ======================================================================
    // Row 6: conversions
    // ======================================================================

    [Fact]
    public void Row6_Conversions_OutcomeClassFamilyFlags()
    {
        foreach (var (text, n) in new[] { ("victory", 0L), ("fled", 1L), ("death", 2L) })
            TelemetryRow.From(SampleRow(text), CharacterClass.Warrior, null)!["outcome"].Should().Be(n, text);

        var classes = Enum.GetValues<CharacterClass>();
        classes.Should().HaveCount(17);
        string[] classOrder =
        {
            "Alchemist", "Assassin", "Barbarian", "Bard", "Cleric", "Jester", "Magician", "Paladin", "Ranger", "Sage",
            "Warrior", "Tidesworn", "Wavecaller", "Cyclebreaker", "Abysswarden", "Voidreaver", "MysticShaman",
        };
        for (int i = 0; i < classOrder.Length; i++)
            TelemetryRow.From(SampleRow(), Enum.Parse<CharacterClass>(classOrder[i]), null)!["player_class"].Should().Be(i, classOrder[i]);

        string[] families =
        {
            "Goblinoid", "Undead", "Orcish", "Draconic", "Demonic", "Giant", "Beast", "Elemental",
            "Aberration", "Insectoid", "Construct", "Fey", "Aquatic", "Celestial", "Shadow",
        };
        MonsterFamilies.GetBuiltInFamilies().Select(f => f.FamilyName).Should().Equal(families);
        for (int i = 0; i < families.Length; i++)
            TelemetryRow.From(SampleRow(), CharacterClass.Warrior, families[i])!["monster_family"].Should().Be(i + 1, families[i]);
        foreach (var other in new[] { "Summoned", "Unknown Family", "", null })
            TelemetryRow.From(SampleRow(), CharacterClass.Warrior, other)!["monster_family"].Should().Be(0, other ?? "null");

        TelemetryRow.From(SampleRow(boss: false, team: false), CharacterClass.Warrior, null)!["is_boss"].Should().Be(0);
        TelemetryRow.From(SampleRow(boss: true, team: false), CharacterClass.Warrior, null)!["is_boss"].Should().Be(1);
        TelemetryRow.From(SampleRow(boss: false, team: false), CharacterClass.Warrior, null)!["has_teammates"].Should().Be(0);
        TelemetryRow.From(SampleRow(boss: false, team: true), CharacterClass.Warrior, null)!["has_teammates"].Should().Be(1);
    }

    // ======================================================================
    // Row 7: bounds before queueing, with the shared fixture
    // ======================================================================

    private static JsonDocument Fixture() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "telemetry-rows.json")));

    [Fact]
    public void Row7_Fixture_GoodRowsAccepted_BadRowsRefused()
    {
        using var doc = Fixture();
        var root = doc.RootElement;
        root.GetProperty("keys").EnumerateArray().Select(k => k.GetString()).Should().Equal(RowKeys);
        var good = root.GetProperty("good").EnumerateArray().ToList();
        var bad = root.GetProperty("bad").EnumerateArray().ToList();
        good.Should().HaveCountGreaterThan(2);
        bad.Should().HaveCountGreaterThan(80, "each bound is tried on both sides");
        foreach (var g in good) TelemetryRow.IsValid(g).Should().BeTrue(g.GetRawText());
        foreach (var b in bad) TelemetryRow.IsValid(b.GetProperty("row")).Should().BeFalse(b.GetProperty("why").GetString());
        // a row the builder makes reads back the same
        var built = TelemetryRow.From(SampleRow(), CharacterClass.Warrior, "Undead")!;
        built.IsValid().Should().BeTrue();
    }

    [Fact]
    public async Task Row7_AFightOutsideTheBounds_IsNotQueued()
    {
        var store = YesStore();
        var f = await RunFight(store, Hero(level: 101));
        f.Error.Should().BeNull("{0}", f.Transcript);
        f.Result!.Tally.Ended.Should().BeTrue();
        f.Engine.LastTelemetryAppend.Should().BeNull("player_level 101 is outside 1 to 100");
        File.Exists(QueueFile).Should().BeFalse();

        var g = await RunFight(store, Hero(level: 100));
        g.Error.Should().BeNull("{0}", g.Transcript);
        await Appended(g);
        Lines().Should().HaveCount(1, "level 100 is inside");
    }

    // ======================================================================
    // Row 8: a fight not entered or not ended is not queued
    // ======================================================================

    private static readonly MethodInfo QueueHook = typeof(CombatEngine).GetMethod("QueueTelemetryRow", BindingFlags.NonPublic | BindingFlags.Instance)!;

    [Fact]
    public void Row8_FightNotEnteredOrNotEnded_IsNotQueued()
    {
        var store = YesStore();
        var notEntered = new CombatResult { Player = Hero(), Outcome = CombatOutcome.PlayerDied };
        notEntered.Tally.Started.Should().BeFalse();
        TelemetryRow.From(CombatEngine.BuildCombatEventRow(notEntered, "death", 0, 0), CharacterClass.Warrior, null).Should().BeNull();
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream())) { TelemetryStoreOverride = store };
        QueueHook.Invoke(engine, new object[] { notEntered, "death", 0L, 0L });
        engine.LastTelemetryAppend.Should().BeNull();

        var notEnded = new CombatResult { Player = Hero(), Outcome = CombatOutcome.PlayerEscaped };
        notEnded.Tally.OnFightStart(new CombatFightStart(3, 1, 1, 1, 0));
        notEnded.Tally.Ended.Should().BeFalse();
        TelemetryRow.From(CombatEngine.BuildCombatEventRow(notEnded, "fled", 0, 0), CharacterClass.Warrior, null).Should().BeNull();
        QueueHook.Invoke(engine, new object[] { notEnded, "fled", 0L, 0L });
        engine.LastTelemetryAppend.Should().BeNull();
        File.Exists(QueueFile).Should().BeFalse();
    }

    // ======================================================================
    // Rows 10 and G: off the combat thread, never throws into the fight
    // ======================================================================

    /// <summary>Holds the queue lock from another store instance until released.</summary>
    private sealed class LockHolder : IDisposable
    {
        private readonly ManualResetEventSlim _release = new();
        private readonly Task _task;
        public readonly ManualResetEventSlim Held = new();
        public LockHolder(TelemetryStore other)
        {
            _task = Task.Run(() => other.WithQueueLock(() => { Held.Set(); _release.Wait(); }));
            Held.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue("the holder got the lock");
        }
        public void Release() => _release.Set();
        public void Dispose() { _release.Set(); _task.Wait(TimeSpan.FromSeconds(10)); }
    }

    [Fact]
    public async Task Row10_LockedQueue_TheFightEndsOnTime_AndOneLogLine()
    {
        var store = YesStore();
        store.LockTimeout = TimeSpan.FromSeconds(2);
        Fight f;
        using (var holder = new LockHolder(Store()))
        {
            f = await RunFight(store, script: Retreats());
            f.Error.Should().BeNull("{0}", f.Transcript);
            f.Result!.Outcome.Should().Be(CombatOutcome.PlayerEscaped, f.Transcript);
            f.Elapsed.Should().BeLessThan(store.LockTimeout, "the fight does not wait on the queue lock");
            await Appended(f);
        }
        store.AppendFailures.Should().Be(1);
        store.AppendErrorLogs.Should().Be(1, "one failed append, one log line");
        File.Exists(QueueFile).Should().BeFalse();
    }

    [Fact]
    public async Task RowG_TheFightReturnsWhileTheTestStillHoldsTheQueueLock()
    {
        var store = YesStore();
        store.LockTimeout = TimeSpan.FromSeconds(5);
        Fight f;
        using (var holder = new LockHolder(Store()))
        {
            f = await RunFight(store, script: Retreats());
            f.Error.Should().BeNull("{0}", f.Transcript);
            f.Engine.LastTelemetryAppend.Should().NotBeNull();
            f.Engine.LastTelemetryAppend!.IsCompleted.Should().BeFalse("the fight returned while the append still waits on the held lock");
            holder.Release();
            await Appended(f);
        }
        store.AppendFailures.Should().Be(0);
        Lines().Should().HaveCount(1, "the append went through once the lock was released");
    }

    private sealed class ThrowingStore : TelemetryStore
    {
        public ThrowingStore(string dir) : base(dir, () => TelemetrySource.Single) { }
        public override void Append(TelemetryRow row, string? playerKey = null) => throw new IOException("appender fails");
    }

    [Fact]
    public async Task Row10_AThrowingAppender_TheFightFinishes()
    {
        var store = new ThrowingStore(_dir);
        store.SetInstallAnswer(true);
        var f = await RunFight(store);
        f.Error.Should().BeNull("{0}", f.Transcript);
        f.Result!.Outcome.Should().Be(CombatOutcome.Victory);
        f.Engine.LastTelemetryAppend.Should().NotBeNull();
        await ((Func<Task>)(() => f.Engine.LastTelemetryAppend!)).Should().ThrowAsync<IOException>();
    }

    // ======================================================================
    // Row 11: the row is a snapshot taken on the combat thread
    // ======================================================================

    [Fact]
    public async Task Row11_Snapshot_ChangingTheCharacterAfterTheCall_DoesNotChangeTheRow()
    {
        var store = YesStore();
        var hold = new TaskCompletionSource();
        var f = await RunFight(store, script: Retreats(),
            prepare: e => e.TelemetryBackground = work => hold.Task.ContinueWith(_ => work(), TaskScheduler.Default));
        f.Error.Should().BeNull("{0}", f.Transcript);
        f.Engine.LastTelemetryAppend!.IsCompleted.Should().BeFalse("the append is held back");
        (long Level, long Strength, long MaxHP, long MLevel, long MMaxHP) expected = (f.Hero.Level, f.Hero.Strength, f.Hero.MaxHP, f.Result!.Monster!.Level, f.Result.Monster.MaxHP);
        f.Hero.Level = 77;
        f.Hero.Strength = 999;
        f.Hero.MaxHP = 12345;
        foreach (var m in f.Monsters) { m.Level = 66; m.MaxHP = 4321; }
        hold.SetResult();
        await Appended(f);
        var row = RowOf(Lines().Single());
        row["player_level"].GetInt64().Should().Be(expected.Level);
        row["player_str"].GetInt64().Should().Be(expected.Strength);
        row["player_max_hp"].GetInt64().Should().Be(expected.MaxHP);
        row["monster_level"].GetInt64().Should().Be(expected.MLevel);
        row["monster_max_hp"].GetInt64().Should().Be(expected.MMaxHP);
    }

    // ======================================================================
    // Row 12: the queue keeps 2000 rows and 30 days
    // ======================================================================

    private static TelemetryRow RowWithRounds(int rounds)
    {
        var r = SampleRow();
        return TelemetryRow.From(r with { Rounds = rounds }, CharacterClass.Warrior, "Undead")!;
    }

    [Fact]
    public void Row12_QueueCap_2001Appends_KeepTheNewest2000()
    {
        TelemetryStore.MaxRows.Should().Be(2000);
        var store = YesStore();
        for (int i = 1; i <= 2001; i++) store.Append(RowWithRounds(i));
        var rows = store.ReadQueue();
        rows.Should().HaveCount(2000);
        rows.First().Row["rounds"].Should().Be(2, "the oldest row went");
        rows.Last().Row["rounds"].Should().Be(2001);
        Lines().Should().HaveCount(2000);
        store.AppendFailures.Should().Be(0);
    }

    [Fact]
    public void Row12_QueueAge_ALineQueued31DaysAgo_IsDroppedAtTheNextAppend()
    {
        TelemetryStore.MaxAgeDays.Should().Be(30);
        var store = YesStore();
        var today = _now;
        _now = today.AddDays(-31);
        store.Append(RowWithRounds(1));
        _now = today.AddDays(-30);
        store.Append(RowWithRounds(2));
        _now = today;
        store.ReadQueue().Should().HaveCount(2, "nothing is dropped before the next append");
        store.Append(RowWithRounds(3));
        store.ReadQueue().Select(r => r.Row["rounds"]).Should().Equal(2L, 3L);
    }

    // ======================================================================
    // Row 13: two writers on one folder (BBS nodes)
    // ======================================================================

    [Fact]
    public async Task Row13_TwoWriters_TheSecondBlocksOnTheOsFileLock_ThenParallelAppendsAllLand()
    {
        var a = YesStore();
        var b = Store();
        b.LockTimeout = TimeSpan.FromSeconds(20);
        var order = new List<string>();
        Task? bAppend = null;
        a.WithQueueLock(() =>
        {
            bAppend = Task.Run(() => { b.Append(RowWithRounds(1)); lock (order) order.Add("B appended"); });
            Thread.Sleep(500);
            bAppend.IsCompleted.Should().BeFalse("B waits for the lock A holds");
            lock (order) order.Add("A released");
        });
        await bAppend!;
        order.Should().Equal("A released", "B appended");
        Lines().Should().HaveCount(1);

        const int n = 40;
        var all = Enumerable.Range(0, n).SelectMany(i => new[]
        {
            Task.Run(() => a.Append(RowWithRounds(100 + i))),
            Task.Run(() => b.Append(RowWithRounds(200 + i))),
        }).ToArray();
        await Task.WhenAll(all);
        var rows = a.ReadQueue();
        rows.Should().HaveCount(2 * n + 1);
        Lines().Should().HaveCount(2 * n + 1, "every line is a valid row");
        rows.Select(r => r.Row["rounds"]).Distinct().Should().HaveCount(2 * n + 1);
    }

    // ======================================================================
    // Row 14: the save listings and saves do not change
    // ======================================================================

    private sealed class EofStream : Stream
    {
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.FromResult(0);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 0;
        public override long Position { get => 0; set { } }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => 0;
        public override void SetLength(long value) { }
        public override void Write(byte[] buffer, int offset, int count) { }
    }

    private static async Task<string> SysOpScreen(string method)
    {
        var output = new MemoryStream();
        var console = new SysOpConsoleManager(new TerminalEmulator(new EofStream(), output));
        var m = typeof(SysOpConsoleManager).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!;
        var task = (Task)m.Invoke(console, null)!;
        (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(20)))).Should().BeSameAs(task, method);
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), @"Last played: [^,]*, ", "");
    }

    private async Task<string> Listings(FileSaveBackend backend)
    {
        SaveSystem.InitializeWithBackend(backend);
        var sb = new StringBuilder();
        sb.AppendLine(string.Join("|", backend.GetAllSaves().Select(s => $"{s.PlayerName}:{s.FileName}").OrderBy(x => x)));
        sb.AppendLine(string.Join("|", backend.GetPlayerSaves("Hero One").Select(s => $"{s.PlayerName}:{s.FileName}").OrderBy(x => x)));
        sb.AppendLine(string.Join("|", backend.GetPlayerSaves("a").Select(s => $"{s.PlayerName}:{s.FileName}").OrderBy(x => x)));
        sb.AppendLine(string.Join("|", backend.GetPlayerSaves("state").Select(s => $"{s.PlayerName}:{s.FileName}").OrderBy(x => x)));
        sb.AppendLine(string.Join("|", backend.GetAllPlayerNames().OrderBy(x => x)));
        sb.AppendLine(await SysOpScreen("ViewAllPlayers"));
        sb.AppendLine(await SysOpScreen("ViewLocalStatistics"));
        return sb.ToString();
    }

    private static SaveGameData Save(string name) => new()
    {
        Version = GameConfig.SaveVersion,
        SaveTime = new DateTime(2026, 10, 1, 10, 0, 0),
        CurrentDay = 3,
        Player = new PlayerData { Name1 = name, Name2 = name, Level = 4 },
    };

    [Fact]
    public async Task Row14_TelemetryFiles_LeaveTheSaveListingsAndSavesUnchanged()
    {
        string plain = Path.Combine(_dir, "plain");
        string withTel = Path.Combine(_dir, "with");
        Directory.CreateDirectory(plain);
        Directory.CreateDirectory(withTel);
        var plainBackend = FileBackend(plain);
        var telBackend = FileBackend(withTel);
        foreach (var b in new[] { plainBackend, telBackend })
        {
            (await b.WriteGameData("Hero One", Save("Hero One"))).Should().BeTrue();
            (await b.WriteGameData("Second", Save("Second"))).Should().BeTrue();
        }
        string expected = await Listings(plainBackend);
        expected.Should().Contain("Hero One").And.Contain("Second");

        // the telemetry files, made by the store as the game makes them
        var single = Store(TelemetrySource.Single, withTel);
        single.SetInstallAnswer(true);
        single.Append(RowWithRounds(1));
        OperatorOn();
        Store(TelemetrySource.BbsDoor, withTel).SetPlayerAnswer("a", true);
        string tel = Path.Combine(withTel, "telemetry");
        File.Exists(Path.Combine(tel, "state.json")).Should().BeTrue();
        File.Exists(Path.Combine(tel, "queue.jsonl")).Should().BeTrue();
        Directory.GetFiles(Path.Combine(tel, "players"), "*.json").Should().HaveCount(1);

        string actual = await Listings(telBackend);
        actual.Replace(withTel, plain).Should().Be(expected);

        // a save then load round trip is byte equal with and without the folder
        (await telBackend.WriteGameData("Third", Save("Third"))).Should().BeTrue();
        (await plainBackend.WriteGameData("Third", Save("Third"))).Should().BeTrue();
        File.ReadAllBytes(Path.Combine(withTel, "Third.json")).Should().Equal(File.ReadAllBytes(Path.Combine(plain, "Third.json")));
        var back = await telBackend.ReadGameData("Third");
        back!.Player.Name2.Should().Be("Third");
        (await telBackend.WriteGameData("Third", back)).Should().BeTrue();
        (await plainBackend.WriteGameData("Third", (await plainBackend.ReadGameData("Third"))!)).Should().BeTrue();
        File.ReadAllBytes(Path.Combine(withTel, "Third.json")).Should().Equal(File.ReadAllBytes(Path.Combine(plain, "Third.json")));
    }

    // ======================================================================
    // Row 15: install_id
    // ======================================================================

    [Fact]
    public void Row15_InstallId_AbsentBeforeYes_32Hex_NewIdDiffers_NoDeletesQueueAndId()
    {
        var store = Store();
        store.State.InstallId.Should().BeNull();
        store.NewInstallId().Should().BeNull("no id before a yes");
        File.Exists(StateFile).Should().BeFalse();

        store.SetInstallAnswer(true);
        string id = store.State.InstallId!;
        id.Should().MatchRegex("^[0-9a-f]{32}$");
        File.ReadAllText(StateFile).Should().Contain(id);
        Store().State.InstallId.Should().Be(id, "the id is on disk");

        string other = store.NewInstallId()!;
        other.Should().MatchRegex("^[0-9a-f]{32}$").And.NotBe(id);
        Store().State.InstallId.Should().Be(other);

        store.Append(RowWithRounds(1));
        File.Exists(QueueFile).Should().BeTrue();
        store.SetInstallAnswer(false);
        File.Exists(QueueFile).Should().BeFalse("No deletes the queue");
        store.State.InstallId.Should().BeNull("No deletes the id");
        var fresh = Store();
        fresh.State.InstallId.Should().BeNull();
        fresh.State.Asked.Should().BeTrue();
        fresh.State.Yes.Should().BeFalse();
        File.ReadAllText(StateFile).Should().NotContain(other);
    }

    // ======================================================================
    // Row 16: withdrawal on a shared install
    // ======================================================================

    [Fact]
    public void Row16_SharedInstall_OnePlayersNo_DeletesTheQueue_KeepsTheId_OperatorOffDeletesBoth()
    {
        OperatorOn();
        var store = Store(TelemetrySource.BbsDoor);
        store.SetPlayerAnswer("alice", true);
        string alice = TelemetryConsent.PlayerKey("alice")!;
        string id = store.State.InstallId!;
        id.Should().MatchRegex("^[0-9a-f]{32}$");
        store.Append(RowWithRounds(1), alice);
        store.Append(RowWithRounds(2), alice);
        Lines().Should().HaveCount(2);

        store.SetPlayerAnswer("bob", false);
        File.Exists(QueueFile).Should().BeFalse("one player's No deletes the whole local queue");
        Store(TelemetrySource.BbsDoor).State.InstallId.Should().Be(id, "the id belongs to the operator setting");
        store.ShouldQueue("alice").Should().BeTrue("alice's own yes stands");

        store.Append(RowWithRounds(3), alice);
        Lines().Should().HaveCount(1, "alice's row after bob's No is queued");
        TelemetryConsent.OperatorResolver = () => false;
        store.OperatorTurnedOff();
        File.Exists(QueueFile).Should().BeFalse();
        Store(TelemetrySource.BbsDoor).State.InstallId.Should().BeNull("operator off deletes the id");
        store.ShouldQueue("alice").Should().BeFalse();
    }

    // ======================================================================
    // Row 17: per player answers, removed with the character
    // ======================================================================

    [Fact]
    public void Row17_Bbs_AnswerFile_StaysInsidePlayers_AndGoesWithTheCharacter()
    {
        Door();
        OperatorOn();
        SaveSystem.InitializeWithBackend(Db);
        var store = TelemetryConsent.StoreFor(_dir);
        store.Folder.Should().Be(MappedTel(_dir));
        string players = Path.Combine(store.Folder, "players");

        store.SetPlayerAnswer("../../Escaper", true);
        store.SetPlayerAnswer("Keeper", true);
        Directory.GetFiles(players).Should().HaveCount(2, "a name with path characters stays inside players/");
        Directory.GetFiles(Path.GetDirectoryName(store.Folder)!, "*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".json") && !p.EndsWith("state.json"))
            .Should().OnlyContain(p => Path.GetDirectoryName(p) == players);

        // SaveSystem.DeleteSave on the SQL backend of a door
        store.ShouldQueue("Keeper").Should().BeTrue();
        SaveSystem.Instance.DeleteSave("Keeper");
        store.ShouldQueue("Keeper").Should().BeFalse("the cached answer went too");
        store.LoadPlayerAnswer("Keeper");
        store.ShouldQueue("Keeper").Should().BeFalse("the same name reads as not asked");
        Directory.GetFiles(players).Should().HaveCount(1);

        // the permadeath path (SqlSaveBackend.DeleteGameData, archive kept)
        Db.DeleteGameData("../../Escaper", bypassArchive: false);
        Directory.GetFiles(players).Should().BeEmpty();

        // a file backend in a door: its own save directory, its own store
        var files = FileBackend(Path.Combine(_dir, "files"));
        var fileStore = TelemetryConsent.StoreFor(files.GetSaveDirectory());
        fileStore.SetPlayerAnswer("Filer", true);
        fileStore.Folder.Should().Be(MappedTel(files.GetSaveDirectory()));
        string filePlayers = Path.Combine(fileStore.Folder, "players");
        Directory.GetFiles(filePlayers).Should().HaveCount(1);
        files.DeleteGameData("Filer");
        Directory.GetFiles(filePlayers).Should().BeEmpty("FileSaveBackend.DeleteGameData removes the answer too");
        fileStore.ShouldQueue("Filer").Should().BeFalse();
    }

    [Fact]
    public void Row17_Server_AnswerRow_GoesWithTheCharacter_OnEveryDeletePath()
    {
        ServerMode();
        OperatorOn();
        SaveSystem.InitializeWithBackend(Db);
        var store = TelemetryConsent.StoreFor(_dir);
        foreach (var n in new[] { "one", "two", "three" }) store.SetPlayerAnswer(n, true);
        Rows("SELECT * FROM telemetry_consent;").Should().HaveCount(3);

        SaveSystem.Instance.DeleteSave("one");
        Db.DeleteGameData("two", bypassArchive: false);
        Db.DeleteAccountCompletely("three");
        Rows("SELECT * FROM telemetry_consent;").Should().BeEmpty();
        foreach (var n in new[] { "one", "two", "three" })
        {
            store.ShouldQueue(n).Should().BeFalse(n);
            store.LoadPlayerAnswer(n);
            store.ShouldQueue(n).Should().BeFalse(n + " reads as not asked");
        }
        Directory.Exists(Path.Combine(store.Folder, "players")).Should().BeFalse("a server keeps answers in the table");
    }

    /// <summary>A database given by a bare file name has "" as its save directory: the answer is still
    /// removed, and the store is the current directory's (as sysop_config.json resolves it).</summary>
    [Fact]
    public void Row17_ARelativeDatabase_EmptySaveDirectory_StillRemovesTheAnswer()
    {
        ServerMode();
        OperatorOn();
        _ = Db;
        Store(TelemetrySource.Server).SetPlayerAnswer("relative", true);
        Rows("SELECT * FROM telemetry_consent;").Should().HaveCount(1);
        TelemetryConsent.RemoveAnswer("", "relative", Db);
        Rows("SELECT * FROM telemetry_consent;").Should().BeEmpty();
        TelemetryConsent.StoreFor("").Folder.Should().Be(MappedTel(Path.GetFullPath(".")), "an empty save directory is the current directory");
    }

    // ======================================================================
    // Row 18: the operator switch defaults off
    // ======================================================================

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Row18_OperatorDefaultOff_AYesQueuesNothing_AndNoFolderIsMade(bool door)
    {
        TelemetryConsent.OperatorResolver.Should().BeNull();
        TelemetryConsent.OperatorAllows().Should().BeFalse();
        if (door) Door(); else ServerMode();
        SaveSystem.InitializeWithBackend(Db);
        var store = TelemetryConsent.StoreFor(_dir);
        string login = TelemetryConsent.CurrentLoginName()!;
        store.SetPlayerAnswer(login, true);
        TelemetryConsent.OnLogin(login);
        store.ShouldQueue(login).Should().BeFalse();
        var f = await RunFight(null);
        f.Error.Should().BeNull("{0}", f.Transcript);
        f.Engine.LastTelemetryAppend.Should().BeNull();
        store.Append(RowWithRounds(1), TelemetryConsent.PlayerKey(login));
        store.OperatorTurnedOff();
        store.Folder.Should().Be(MappedTel(_dir));
        Directory.Exists(store.Folder).Should().BeFalse("nothing is created while the operator switch is off");
    }

    // ======================================================================
    // Row 19: the official database is untouched while the switch is off
    // ======================================================================

    [Fact]
    public async Task Row19_OperatorOff_NoTable_OperatorOn_TheTableHoldsOneAnswer()
    {
        GoOnline();
        ServerMode();
        var store = TelemetryConsent.StoreFor(_dir);
        string login = TelemetryConsent.CurrentLoginName()!;
        TelemetryConsent.OnLogin(login);
        store.SetPlayerAnswer(login, true);
        var f = await RunFight(null);
        f.Error.Should().BeNull("{0}", f.Transcript);
        await f.Engine.LastCombatRowWrite!;
        Db.DeleteGameData("somebody", bypassArchive: false);
        ConsentTableExists().Should().BeFalse("the switch is off: no telemetry_consent table");

        OperatorOn();
        TelemetryConsent.OnLogin(login);
        store.SetPlayerAnswer(login, true);
        ConsentTableExists().Should().BeTrue();
        Rows("SELECT * FROM telemetry_consent;").Should().ContainSingle()
            .Which["yes"].Should().Be(1L);
    }

    // ======================================================================
    // Row 20: the answer is cached at login, never read per fight
    // ======================================================================

    [Theory]
    [InlineData(TelemetrySource.Server)]
    [InlineData(TelemetrySource.BbsDoor)]
    public async Task Row20_AnswerReadOnceAtLogin_FightsReadNothing_SettingsUpdateTheCache(TelemetrySource source)
    {
        _ = Db;
        OperatorOn();
        Store(source).SetPlayerAnswer("Player", true);
        var store = Store(source);    // a later process: nothing cached yet
        store.LoadPlayerAnswer("Player");
        store.AnswerReads.Should().Be(1);
        for (int i = 0; i < 2; i++)
        {
            var f = await RunFight(store);
            f.Error.Should().BeNull("{0}", f.Transcript);
            await Appended(f);
        }
        store.AnswerReads.Should().Be(1, "a fight reads no file or table");
        Lines().Should().HaveCount(2);

        store.SetPlayerAnswer("Player", false);
        store.ShouldQueue("Player").Should().BeFalse("the settings call updated the cache");
        store.SetPlayerAnswer("Player", true);
        store.ShouldQueue("Player").Should().BeTrue();
        store.AnswerReads.Should().Be(1);
    }

    // ======================================================================
    // Rows 21 and C: no answer, or a damaged file, is never a yes
    // ======================================================================

    [Fact]
    public void Row21_AnInterruptedAsk_IsStoredAsNotAsked()
    {
        var store = Store();
        store.InstallAskInterrupted();
        store.State.Asked.Should().BeFalse();
        store.State.Yes.Should().BeFalse();
        store.ShouldQueue(null).Should().BeFalse();
        var fresh = Store();
        fresh.State.Asked.Should().BeFalse();
        fresh.State.Yes.Should().BeFalse();
        fresh.State.InstallId.Should().BeNull();

        OperatorOn();
        var shared = Store(TelemetrySource.BbsDoor);
        shared.PlayerAskInterrupted("Player");
        shared.ShouldQueue("Player").Should().BeFalse();
        var later = Store(TelemetrySource.BbsDoor);
        later.LoadPlayerAnswer("Player");
        later.ShouldQueue("Player").Should().BeFalse();
        using var doc = JsonDocument.Parse(File.ReadAllText(Directory.GetFiles(Path.Combine(Tel, "players")).Single()));
        doc.RootElement.GetProperty("asked").GetInt32().Should().Be(0);
        doc.RootElement.GetProperty("yes").GetInt32().Should().Be(0);
    }

    [Theory]
    [InlineData("{\"asked\":1,\"yes\":1,\"install_id\":\"0123456789abcdef0123")]
    [InlineData("{\"asked\":1,\"yes\":1")]
    [InlineData("not json at all")]
    [InlineData("{\"asked\":1,\"yes\":2,\"install_id\":\"0123456789abcdef0123456789abcdef\"}")]
    [InlineData("{\"asked\":1,\"yes\":\"1\",\"install_id\":\"0123456789abcdef0123456789abcdef\"}")]
    [InlineData("[1,1]")]
    [InlineData("")]
    public void RowC_DamagedStateOrAnswerFile_ReadsAsNotAsked(string damaged)
    {
        Directory.CreateDirectory(Tel);
        File.WriteAllText(StateFile, damaged);
        var store = Store();
        store.State.Asked.Should().BeFalse();
        store.State.Yes.Should().BeFalse();
        store.ShouldQueue(null).Should().BeFalse();

        OperatorOn();
        string key = TelemetryConsent.PlayerKey("Player")!;
        Directory.CreateDirectory(Path.Combine(Tel, "players"));
        File.WriteAllText(Path.Combine(Tel, "players", key + ".json"), damaged.Replace("install_id", "x"));
        var shared = Store(TelemetrySource.BbsDoor);
        shared.LoadPlayerAnswer("Player");
        shared.ShouldQueue("Player").Should().BeFalse();
    }

    [Fact]
    public async Task RowC_ADamagedState_DoesNotBlockAFight()
    {
        Directory.CreateDirectory(Tel);
        File.WriteAllText(StateFile, "{\"asked\":1,\"ye");
        var f = await RunFight(Store());
        f.Error.Should().BeNull("{0}", f.Transcript);
        f.Engine.LastTelemetryAppend.Should().BeNull();
    }

    // ======================================================================
    // Row A: one process, many players
    // ======================================================================

    [Fact]
    public async Task RowA_TwoSessionsInOneProcess_EachFightUsesItsOwnPlayersAnswer()
    {
        ServerMode();
        OperatorOn();
        _ = Db;
        var setup = Store(TelemetrySource.Server);
        setup.SetPlayerAnswer("alice", true);
        setup.SetPlayerAnswer("bob", false);
        var store = Store(TelemetrySource.Server);
        store.LoadPlayerAnswer("alice");
        store.LoadPlayerAnswer("bob");    // the later login must not decide for alice

        async Task<Fight> As(string user) => await Task.Run(async () =>
        {
            var ctx = new SessionContext { Username = user, CharacterKey = user };
            SessionContext.Current = ctx;
            ctx.InitializeSystems();
            try { return await RunFight(store, script: Retreats()); }
            finally { SessionContext.Current = null; }
        });

        var bob = await As("bob");
        bob.Error.Should().BeNull("{0}", bob.Transcript);
        bob.Engine.LastTelemetryAppend.Should().BeNull("bob said no");
        File.Exists(QueueFile).Should().BeFalse();

        var alice = await As("alice");
        alice.Error.Should().BeNull("{0}", alice.Transcript);
        await Appended(alice);
        Lines().Should().HaveCount(1, "alice said yes");

        var bobAgain = await As("bob");
        bobAgain.Engine.LastTelemetryAppend.Should().BeNull();
        Lines().Should().HaveCount(1);
    }

    // ======================================================================
    // Row B: the consent key is injective
    // ======================================================================

    private static string Sanitised(string name) => string.Join("_", name.Split(Path.GetInvalidFileNameChars()));

    [Theory]
    [InlineData(TelemetrySource.BbsDoor)]
    [InlineData(TelemetrySource.Server)]
    public void RowB_ConsentKey_SanitiserCollisionsGetTwoAnswers_CaseSharesOne(TelemetrySource source)
    {
        Sanitised("a/b").Should().Be(Sanitised("a_b"), "the pair collides under the save sanitiser");
        TelemetryConsent.PlayerKey("a/b").Should().NotBe(TelemetryConsent.PlayerKey("a_b"));
        TelemetryConsent.PlayerKey("a/b").Should().NotBe(TelemetryConsent.PlayerKey("a:b"));
        TelemetryConsent.PlayerKey("Ann").Should().Be(TelemetryConsent.PlayerKey("ANN"));

        _ = Db;
        OperatorOn();
        var store = Store(source);
        store.SetPlayerAnswer("a/b", true);
        store.SetPlayerAnswer("a_b", false);
        store.SetPlayerAnswer("a:b", false);
        store.SetPlayerAnswer("Ann", true);
        var later = Store(source);
        foreach (var n in new[] { "a/b", "a_b", "a:b", "Ann", "ANN" }) later.LoadPlayerAnswer(n);
        later.ShouldQueue("a/b").Should().BeTrue();
        later.ShouldQueue("a_b").Should().BeFalse();
        later.ShouldQueue("a:b").Should().BeFalse();
        later.ShouldQueue("ANN").Should().BeTrue("a case pair shares one answer");
        int stored = source == TelemetrySource.Server
            ? Rows("SELECT * FROM telemetry_consent;").Count
            : Directory.GetFiles(Path.Combine(Tel, "players")).Length;
        stored.Should().Be(4, "a/b, a_b, a:b and one for Ann/ANN");
    }

    // ======================================================================
    // Row D: a damaged queue line
    // ======================================================================

    [Fact]
    public void RowD_ADamagedQueueLine_IsSkippedAndDropped_TheGoodLinesStay()
    {
        var store = YesStore();
        store.Append(RowWithRounds(1));
        string good = Lines().Single();
        File.WriteAllText(QueueFile, good + "\n" + good[..(good.Length / 2)] + "\n" + "{\"d\":1,\"r\":{\"rounds\":\"x\"}}\n" + good.Replace("\"rounds\":1", "\"rounds\":2") + "\n");

        var read = store.ReadQueue();
        read.Select(r => r.Row["rounds"]).Should().Equal(1L, 2L);
        store.Append(RowWithRounds(3));
        Lines().Should().HaveCount(3, "the damaged lines are dropped at the next append");
        store.ReadQueue().Select(r => r.Row["rounds"]).Should().Equal(1L, 2L, 3L);
        store.AppendFailures.Should().Be(0);
    }

    // ======================================================================
    // T1a2 P1: a test run never touches a real save folder
    // ======================================================================

    [Fact]
    public async Task P1_WithNoOverride_TheStoreAFightResolves_LiesUnderTheTestRunsTempFolder()
    {
        TelemetryConsent.RootOverride.Should().NotBeNull("the Tests module initializer sets it for the whole run");
        TelemetryConsent.RootOverride.Should().Be(TelemetryTestRoot.Root);
        string root = Path.GetFullPath(TelemetryTestRoot.Root) + Path.DirectorySeparatorChar;
        root.Should().StartWith(Path.GetFullPath(Path.GetTempPath()));

        // the game's own backend: its save directory is the developer's real saves
        var real = new FileSaveBackend();
        SaveSystem.InitializeWithBackend(real);
        string saves = Path.GetFullPath(real.GetSaveDirectory());
        var store = TelemetryConsent.CurrentStore()!;
        store.Folder.Should().StartWith(root, "checked before anything is written");
        store.Folder.Should().NotStartWith(saves + Path.DirectorySeparatorChar);
        TelemetryConsent.StoreFor(saves).Should().BeSameAs(store);
        try
        {
            store.SetInstallAnswer(true).Should().BeTrue();
            var f = await RunFight(null);    // no TelemetryStoreOverride: the engine resolves the store itself
            f.Error.Should().BeNull("{0}", f.Transcript);
            await Appended(f);
            LinesIn(store).Should().NotBeEmpty("the fight queued its row in the temp folder");
            store.QueuePath.Should().StartWith(root);
        }
        finally
        {
            store.SetInstallAnswer(false);
            try { Directory.Delete(Path.GetDirectoryName(store.Folder)!, true); } catch { }
        }
    }

    // ======================================================================
    // T1a2 P2: player_hp_end is clamped at 0 in the row; real fights of every outcome all queue
    // ======================================================================

    private static Monster Drainer(long hp = 5000, long str = 400) => new Monster
    {
        Name = "Drainer", Level = 200, HP = hp, MaxHP = hp, Strength = str, Defence = 5, Experience = 1, Gold = 0,
        IsActive = true, FamilyName = "Undead", SpecialAbilities = new List<string> { "LifeDrain" },
    };

    /// <summary>Online, as Row 3: a death there resurrects without a menu (the single player Veil of Death
    /// menu waits for input).</summary>
    [Fact]
    public async Task P2_ASeededBatchOfRealFights_EveryOutcome_NoRowIsDropped_AndADrainDeathEndsAtZero()
    {
        GoOnline();
        ServerMode();
        OperatorOn();
        var store = Store(TelemetrySource.Server);
        store.SetPlayerAnswer(TelemetryConsent.CurrentLoginName(), true).Should().BeTrue();
        Character Mortal(long hp) { var h = Hero(hp: hp); h.Resurrections = 3; return h; }
        var outcomes = new List<long>();
        int drainDeaths = 0;
        async Task One(Fight f, long outcome, string what)
        {
            f.Error.Should().BeNull("{0}", f.Transcript);
            f.Result!.Tally.Ended.Should().BeTrue(what);
            await Appended(f);
            outcomes.Add(outcome);
            var lines = Lines();
            lines.Should().HaveCount(outcomes.Count, "{0}: no row is dropped", what);
            var row = RowOf(lines[^1]);
            row["outcome"].GetInt64().Should().Be(outcome, what);
            row["player_hp_end"].GetInt64().Should().Be(Math.Max(0, f.Result.Tally.PlayerHpEnd), what);
        }

        for (int seed = 1; seed <= 4; seed++)
        {
            var win = await RunFight(store, seed: seed);
            win.Result!.Outcome.Should().Be(CombatOutcome.Victory, win.Transcript);
            await One(win, 0, $"victory seed {seed}");

            var fled = await RunFight(store, script: Retreats(), seed: seed);
            fled.Result!.Outcome.Should().Be(CombatOutcome.PlayerEscaped, fled.Transcript);
            await One(fled, 1, $"fled seed {seed}");

            var died = await RunFight(store, Mortal(20), new List<Monster> { Brute("Brute A", 5000, 400) }, seed: seed);
            await One(died, 2, $"death seed {seed}");

            var drained = await RunFight(store, Mortal(300), new List<Monster> { Drainer() }, seed: seed);
            await One(drained, 2, $"drain death seed {seed}");
            if (drained.Result!.Tally.PlayerHpEnd < 0)
            {
                drained.Transcript.Should().Contain("drain", "the killing blow was the life drain");
                drainDeaths++;
            }
        }
        drainDeaths.Should().BeGreaterThan(0, "a life drain death leaves HP below 0 in the fight, and its row still queues");
        outcomes.Should().HaveCount(16);
        store.AppendFailures.Should().Be(0);
    }

    // ======================================================================
    // T1a2 M1: a No closes the gate even when its disk work fails
    // ======================================================================

    [Fact]
    public async Task M1_ANoWhileTheLockIsHeld_ClosesTheGate_Install()
    {
        var store = YesStore();
        store.LockTimeout = TimeSpan.FromMilliseconds(300);
        using (new LockHolder(Store()))
            store.SetInstallAnswer(false).Should().BeFalse("the lock is held: the No is not written, and the caller learns it");
        store.ShouldQueue(null).Should().BeFalse("the No holds in memory at once");
        Store().State.Yes.Should().BeTrue("the old yes is still on disk");

        var f = await RunFight(store);
        f.Error.Should().BeNull("{0}", f.Transcript);
        f.Engine.LastTelemetryAppend.Should().BeNull("the next fight queues nothing");
        File.Exists(QueueFile).Should().BeFalse();

        // a row that reached the append anyway: the disk's old yes never turns this process's No back
        store.Append(RowWithRounds(1));
        File.Exists(QueueFile).Should().BeFalse();
        store.ShouldQueue(null).Should().BeFalse();
    }

    [Fact]
    public async Task M1_AnInterruptedAskWhileTheLockIsHeld_ClosesTheGate_Install()
    {
        var store = YesStore();
        store.LockTimeout = TimeSpan.FromMilliseconds(300);
        using (new LockHolder(Store()))
            store.InstallAskInterrupted().Should().BeFalse();
        store.ShouldQueue(null).Should().BeFalse();
        var f = await RunFight(store);
        f.Error.Should().BeNull("{0}", f.Transcript);
        f.Engine.LastTelemetryAppend.Should().BeNull();
        File.Exists(QueueFile).Should().BeFalse();
    }

    [Theory]
    [InlineData(TelemetrySource.BbsDoor)]
    [InlineData(TelemetrySource.Server)]
    public async Task M1_ANoWhileTheLockIsHeld_ClosesTheGate_Player(TelemetrySource source)
    {
        _ = Db;
        OperatorOn();
        if (source == TelemetrySource.BbsDoor) Door(); else ServerMode();
        string login = TelemetryConsent.CurrentLoginName()!;
        var store = Store(source);
        store.SetPlayerAnswer(login, true).Should().BeTrue();
        store.LockTimeout = TimeSpan.FromMilliseconds(300);
        using (new LockHolder(Store(source)))
            store.SetPlayerAnswer(login, false).Should().BeFalse("the lock is held: the No is not written, and the caller learns it");
        store.ShouldQueue(login).Should().BeFalse("the No holds in the cache at once");
        var later = Store(source);
        later.LoadPlayerAnswer(login);
        later.ShouldQueue(login).Should().BeTrue("the old yes is still stored");

        var f = await RunFight(store);
        f.Error.Should().BeNull("{0}", f.Transcript);
        f.Engine.LastTelemetryAppend.Should().BeNull("the next fight queues nothing");
        File.Exists(QueueFile).Should().BeFalse();
    }

    // ======================================================================
    // T1a2 M2: a row scheduled before a No never lands after it
    // ======================================================================

    private static Action<CombatEngine> HoldAppend(TaskCompletionSource hold) =>
        e => e.TelemetryBackground = work => hold.Task.ContinueWith(_ => work(), TaskScheduler.Default);

    [Theory]
    [InlineData(TelemetrySource.Single)]
    [InlineData(TelemetrySource.BbsDoor)]
    [InlineData(TelemetrySource.Server)]
    public async Task M2_ARowHeldBackBeforeANo_DoesNotLandAfterIt(TelemetrySource source)
    {
        _ = Db;
        string? login = null;
        TelemetryStore store;
        if (source == TelemetrySource.Single) store = YesStore();
        else
        {
            OperatorOn();
            if (source == TelemetrySource.BbsDoor) Door(); else ServerMode();
            login = TelemetryConsent.CurrentLoginName()!;
            store = Store(source);
            store.SetPlayerAnswer(login, true).Should().BeTrue();
        }
        var hold = new TaskCompletionSource();
        var f = await RunFight(store, script: Retreats(), prepare: HoldAppend(hold));
        f.Error.Should().BeNull("{0}", f.Transcript);
        f.Engine.LastTelemetryAppend!.IsCompleted.Should().BeFalse("the append is held back");

        (source == TelemetrySource.Single ? store.SetInstallAnswer(false) : store.SetPlayerAnswer(login, false)).Should().BeTrue();
        hold.SetResult();
        await Appended(f);
        File.Exists(QueueFile).Should().BeFalse("the row was scheduled before the No");
        store.AppendFailures.Should().Be(0);
    }

    [Fact]
    public async Task M2_ARowHeldBackBeforeASecondCopysNo_DoesNotLandAfterIt_Single()
    {
        var store = YesStore();
        var hold = new TaskCompletionSource();
        var f = await RunFight(store, script: Retreats(), prepare: HoldAppend(hold));
        f.Error.Should().BeNull("{0}", f.Transcript);
        f.Engine.LastTelemetryAppend!.IsCompleted.Should().BeFalse("the append is held back");

        Store().SetInstallAnswer(false).Should().BeTrue("another running copy of the game says No");
        store.ShouldQueue(null).Should().BeTrue("this copy has not read the other copy's No yet");
        hold.SetResult();
        await Appended(f);
        File.Exists(QueueFile).Should().BeFalse("the append read state.json again under the lock");
        store.ShouldQueue(null).Should().BeFalse("and the cache follows what it read");
        store.AppendFailures.Should().Be(0);
    }

    /// <summary>The other columns real play can push past a bound (the T1a2 audit in REPORT): held at the
    /// bound, so the fight is still queued. player_level 101 (admin only) is still refused.</summary>
    [Fact]
    public void P2_ColumnsPlayCanPushPastABound_AreHeldAtTheBound_PlayerLevelIsNot()
    {
        TelemetryRow.Saturating.Should().BeEquivalentTo(new[]
        {
            "player_str", "player_dex", "monster_str", "monster_level", "monster_count",
            "rounds", "potions_used", "abilities_used", "spells_used",
        });
        var tally = new CombatRowTally(8, 1, 3, 2, 0, 120, 20, 0, 0, 80, 520, 200, 40, 10001, 10002, 10003, 0, -75);
        var wild = SampleRow(tally: tally) with
        {
            PlayerSTR = -12, PlayerDEX = -4, MonsterSTR = -15, MonsterLevel = 260, MonsterCount = 61, Rounds = 12000,
        };
        var row = TelemetryRow.From(wild, CharacterClass.Warrior, "Undead")!;
        row.IsValid().Should().BeTrue("a fight real play can produce is never dropped");
        row["player_str"].Should().Be(0);
        row["player_dex"].Should().Be(0);
        row["monster_str"].Should().Be(0);
        row["monster_level"].Should().Be(200);
        row["monster_count"].Should().Be(50);
        row["rounds"].Should().Be(10000);
        row["potions_used"].Should().Be(10000);
        row["abilities_used"].Should().Be(10000);
        row["spells_used"].Should().Be(10000);
        row["player_hp_end"].Should().Be(0);
        foreach (var key in RowKeys.Except(TelemetryRow.Saturating).Except(new[] { "player_hp_end" }))
            row[key].Should().Be(TelemetryRow.From(SampleRow(), CharacterClass.Warrior, "Undead")![key], key);

        TelemetryRow.From(SampleRow() with { PlayerLevel = 101 }, CharacterClass.Warrior, "Undead")!.IsValid()
            .Should().BeFalse("player_level is not held: only the admin command takes it past 100");
    }
}
