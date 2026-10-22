using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake;
using UsurperRemake.BBS;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.8 piece D1: a fight that ends outside the death row at the end of HandlePlayerDeath (an online
/// permadeath, an arrest or exhibition loss, a Last Stand, Death's Door or Mortis's Miracle rescue, a
/// Divine Favor save) writes one combat_events row and, with a stored yes, one telemetry row. Fights are
/// real PlayerVsMonsters fights on a self hosted server (the SQL backend, an OnlineStateManager, the
/// operator switch on, the player's yes cached), with the ending forced; the rows are read back from the
/// database and from the queue of the save directory's own store.
/// </summary>
[Collection("SharedGameSingletons")]
public class DeathRows128Tests : IDisposable
{
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly FieldInfo OsmField = typeof(OnlineStateManager).GetField("_fallbackInstance", SNP)!;
    private static readonly FieldInfo SaveField = typeof(SaveSystem).GetField("instance", SNP)!;
    private static readonly FieldInfo DoorField = typeof(DoorMode).GetField("_sessionInfo", SNP)!;
    private static readonly FieldInfo OnlineField = typeof(DoorMode).GetField("_onlineMode", SNP)!;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"usurper-deathrows-{Guid.NewGuid():N}");
    private readonly object? _osmBefore = OsmField.GetValue(null);
    private readonly object? _saveBefore = SaveField.GetValue(null);
    private readonly object? _doorBefore = DoorField.GetValue(null);
    private readonly object? _onlineBefore = OnlineField.GetValue(null);
    private readonly bool _permadeathBefore = GameConfig.OnlinePermadeathEnabled;
    private readonly SqlSaveBackend _db;
    private readonly TelemetryStore _store;
    private readonly string _login;

    public DeathRows128Tests()
    {
        Directory.CreateDirectory(_dir);
        TelemetryConsent.ResetForTests();
        _db = new SqlSaveBackend(Path.Combine(_dir, "game.db"));
        // online as PlayerSession sets it: the SQL backend in SaveSystem and an OnlineStateManager
        SaveSystem.InitializeWithBackend(_db);
        var osm = (OnlineStateManager)Activator.CreateInstance(typeof(OnlineStateManager),
            BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { _db, "tester" }, null)!;
        OsmField.SetValue(null, osm);
        // a self hosted server: online, no door
        DoorField.SetValue(null, null);
        OnlineField.SetValue(null, true);
        DoorMode.IsOnlineMode.Should().BeTrue();
        GameConfig.OnlinePermadeathEnabled = true;
        TelemetryConsent.OperatorResolver = () => true;
        // the store of the active save directory, the one the erase removes the answer from
        _store = TelemetryConsent.CurrentStore()!;
        _store.Should().BeSameAs(TelemetryConsent.StoreFor(_dir));
        _store.Folder.Should().StartWith(TelemetryTestRoot.Root);
        _login = TelemetryConsent.CurrentLoginName()!;
        _store.SetPlayerAnswer(_login, true).Should().BeTrue();
        _store.ShouldQueue(_login).Should().BeTrue("a stored yes");
    }

    public void Dispose()
    {
        SqlSaveBackend.ClearErasedMark(_login);
        OsmField.SetValue(null, _osmBefore);
        SaveField.SetValue(null, _saveBefore);
        DoorField.SetValue(null, _doorBefore);
        OnlineField.SetValue(null, _onlineBefore);
        GameConfig.OnlinePermadeathEnabled = _permadeathBefore;
        TelemetryConsent.ResetForTests();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    // ---------- reads ----------

    private List<Dictionary<string, object?>> Rows(string sql)
    {
        using var c = new SqliteConnection($"Data Source={_db.DatabasePath};Pooling=false");
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

    private string[] Lines() => File.Exists(_store.QueuePath) ? File.ReadAllLines(_store.QueuePath) : Array.Empty<string>();

    // ---------- fight harness (as Telemetry127Tests) ----------

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

    private static Character Hero(string name = "Tester", long hp = 600, int resurrections = 3) => new Character
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Human,
        Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 10,
        HP = hp, MaxHP = 600, BaseMaxHP = 600,
        Strength = 60, BaseStrength = 60, Defence = 20, BaseDefence = 20,
        Dexterity = 30, BaseDexterity = 30, Agility = 20, BaseAgility = 20,
        Constitution = 30, BaseConstitution = 30, Intelligence = 20, BaseIntelligence = 20,
        Wisdom = 20, BaseWisdom = 20, Stamina = 100, BaseStamina = 100,
        Mental = 80, Gold = 0, Healing = 0, AutoHeal = false,
        CombatSpeed = CombatSpeed.Instant, Resurrections = resurrections,
    };

    /// <summary>A monster the hero cannot kill in time that kills a hero at 20 HP over a few rounds.</summary>
    private static Monster Brute(long hp = 5000, long str = 400, int level = 8) => new Monster
    {
        Name = "Brute A", Level = level, HP = hp, MaxHP = hp, Strength = str, Defence = 5, Experience = 1, Gold = 0,
        IsActive = true, FamilyName = "Undead",
    };

    /// <summary>One blow is held at the engine's per hit cap (510 against this hero, whose MaxHP the fight
    /// start makes 680), which kills a hero at 400 HP (above half: Last Stand) or at 200 (above a quarter:
    /// Death's Door).</summary>
    private static Monster Crusher() => Brute(str: 60000);

    private sealed class Fight
    {
        public CombatEngine Engine = null!;
        public MemoryStream Output = new();
        public Character Hero = null!;
        public CombatResult? Result;
        public Exception? Error;
        public string Transcript => Encoding.UTF8.GetString(Output.ToArray());
    }

    private static async Task<Fight> RunFight(Character hero, List<Monster> monsters, float divineFavor = 0f,
        bool ambush = false, Func<Action, Task>? background = null, int seed = 128)
    {
        var f = new Fight { Hero = hero };
        var term = new TerminalEmulator(new ScriptedStream(Attacks()), f.Output);
        f.Engine = new CombatEngine(term) { DivineFavorChanceOverride = () => divineFavor };
        if (background != null) f.Engine.TelemetryBackground = background;
        f.Engine.SeedRandomForTests(seed);
        try
        {
            var task = f.Engine.PlayerVsMonsters(hero, monsters, offerMonkEncounter: false, isAmbush: ambush);
            var done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(60)));
            if (done != task) throw new TimeoutException("the fight did not end");
            f.Result = await task;
        }
        catch (Exception ex) { f.Error = ex; }
        return f;
    }

    private static async Task Settled(Fight f)
    {
        foreach (var t in new[] { f.Engine.LastCombatRowWrite, f.Engine.LastTelemetryAppend })
            if (t != null) (await Task.WhenAny(t, Task.Delay(TimeSpan.FromSeconds(30)))).Should().BeSameAs(t, "the background write ends");
    }

    /// <summary>
    /// The fight's one combat_events row and its one telemetry line: the label, the telemetry outcome number,
    /// every key of the line inside the bounds the server applies (TelemetryRow.IsValid, as T1b checks a
    /// body), and the line equal to the database row. Returns the database row.
    /// </summary>
    private async Task<Dictionary<string, object?>> OneRow(Fight f, string label)
    {
        f.Error.Should().BeNull("{0}", f.Transcript);
        await Settled(f);
        var rows = Rows("SELECT * FROM combat_events;");
        rows.Should().HaveCount(1, "one combat_events row for the fight");
        var row = rows[0];
        row["outcome"].Should().Be(label);

        var lines = Lines();
        lines.Should().HaveCount(1, "one telemetry row for the fight");
        using var doc = JsonDocument.Parse(lines[0]);
        var r = doc.RootElement.GetProperty("r");
        TelemetryRow.IsValid(r).Should().BeTrue("the row passes the bounds the server applies");
        long outcome = r.GetProperty("outcome").GetInt64();
        outcome.Should().BeInRange(0, 2).And.Be(TelemetryRow.OutcomeNumber(label), "never -1");
        r.GetProperty("player_hp_end").GetInt64().Should().Be(Math.Max(0, Convert.ToInt64(row["player_hp_end"])));
        r.GetProperty("floor_actual").GetInt64().Should().Be(Convert.ToInt64(row["floor_actual"]));
        r.GetProperty("rounds").GetInt64().Should().Be(Convert.ToInt64(row["rounds"]));
        return row;
    }

    // ---------- the endings ----------

    private async Task<Fight> Permadeath(Func<Action, Task>? background = null)
    {
        var hero = Hero(_login, hp: 20, resurrections: 0);
        var f = await RunFight(hero, new List<Monster> { Brute() }, background: background);
        f.Error.Should().BeNull("{0}", f.Transcript);
        f.Result!.IsPermadeath.Should().BeTrue("the online permadeath path ran: {0}", f.Transcript);
        return f;
    }

    private async Task<Fight> Arrest()
    {
        var hero = Hero(hp: 20);
        hero.Location = (int)GameLocation.MainStreet;
        hero.IsArrestCombat = true;
        try { return await RunFight(hero, new List<Monster> { Brute() }); }
        finally { hero.IsArrestCombat = false; }
    }

    private async Task<Fight> Exhibition()
    {
        var hero = Hero(hp: 20);
        hero.IsExhibitionCombat = true;
        try { return await RunFight(hero, new List<Monster> { Brute() }); }
        finally { hero.IsExhibitionCombat = false; }
    }

    private Task<Fight> LastStand() => RunFight(Hero(hp: 400), new List<Monster> { Crusher() });

    private Task<Fight> DeathsDoor() => RunFight(Hero(hp: 200), new List<Monster> { Crusher() });

    private async Task<Fight> Mortis()
    {
        var hero = Hero("D1Mortis", hp: 20);
        GodRegistry.SetWorshippedGod(hero, "Mortis").Should().BeTrue();
        hero.GodFavor = GameConfig.GodFavorTierChosenMin;
        FavorSystem.Bind(hero);
        hero.GodFavor = GameConfig.GodFavorTierChosenMin;
        hero.MiracleUsedToday = false;
        MiracleSystem.GetMiracle(hero).Should().Be(GodDomain.Death);
        try { return await RunFight(hero, new List<Monster> { Brute() }); }
        finally { GodRegistry.SetWorshippedGod(hero, ""); }
    }

    private Task<Fight> DivineFavor() => RunFight(Hero(hp: 20), new List<Monster> { Brute() }, divineFavor: 1f);

    // ======================================================================
    // Row 1: online permadeath writes one death row, queued before the save is erased
    // ======================================================================

    [Fact]
    public async Task Row1_OnlinePermadeath_OneDeathRow_QueuedBeforeTheErase()
    {
        // the append is held back a little: the erase, which removes this player's answer, must wait for it
        var f = await Permadeath(work => Task.Run(async () => { await Task.Delay(400); work(); }));
        var row = await OneRow(f, "death");
        row["player_name"].Should().Be(_login);

        // the erase ran after the row: the player's answer is gone (TelemetryConsent.RemoveAnswer)
        _store.ShouldQueue(_login).Should().BeFalse("the erase removed the answer");
        Rows("SELECT * FROM telemetry_consent;").Should().BeEmpty();
        // the queue is the save directory's, not the character's: the row is still there to send
        var queued = _store.ReadQueue();
        queued.Should().ContainSingle();
        queued[0].Row["outcome"].Should().Be(2);
    }

    // ======================================================================
    // Rows 2 and 3: arrest and exhibition losses
    // ======================================================================

    [Fact]
    public async Task Row2_ArrestLoss_OneDeathRow_HP1_NoResurrectionUsed()
    {
        var f = await Arrest();
        var row = await OneRow(f, "death");
        f.Hero.HP.Should().Be(1);
        f.Hero.Resurrections.Should().Be(3, "no resurrection is used");
        f.Result!.Outcome.Should().Be(CombatOutcome.PlayerEscaped, "as before");
        row["floor_actual"].Should().Be(0L, "a fight in town has no dungeon floor");
        Convert.ToInt64(row["player_hp_end"]).Should().BeLessThanOrEqualTo(0, "the row keeps the HP the fight left");
    }

    [Fact]
    public async Task Row3_ExhibitionLoss_OneDeathRow_HP1_NoResurrectionUsed()
    {
        var f = await Exhibition();
        await OneRow(f, "death");
        f.Hero.HP.Should().Be(1);
        f.Hero.Resurrections.Should().Be(3, "no resurrection is used");
        f.Result!.Outcome.Should().Be(CombatOutcome.PlayerDied, "as before");
    }

    // ======================================================================
    // Rows 4, 5 and 6: a rescue with a monster alive is a flight at 1 HP
    // ======================================================================

    [Fact]
    public async Task Row4_LastStand_MonsterAlive_OneFledRow_HpEnd1()
    {
        var f = await LastStand();
        f.Transcript.Should().Contain(Loc.Get("combat.last_stand"));
        f.Result!.Outcome.Should().Be(CombatOutcome.PlayerEscaped);
        var row = await OneRow(f, "death");
        row["player_hp_end"].Should().Be(0L);
        f.Hero.HP.Should().Be(340, "1.3.6: Last Stand keeps the fight going, the next blow kills and a resurrection raises the hero");
        f.Hero.Resurrections.Should().Be(2);
    }

    [Fact]
    public async Task Row5_DeathsDoor_MonsterAlive_OneFledRow_HpEnd1()
    {
        var f = await DeathsDoor();
        f.Transcript.Should().Contain(Loc.Get("combat.deaths_door"));
        f.Transcript.Should().NotContain(Loc.Get("combat.last_stand"));
        f.Result!.Outcome.Should().Be(CombatOutcome.PlayerEscaped);
        var row = await OneRow(f, "death");
        row["player_hp_end"].Should().Be(0L);
        f.Hero.Resurrections.Should().Be(2, "1.3.6: Death's Door keeps the fight going and the next blow is a death");
    }

    [Fact]
    public async Task Row6_MortisMiracle_MonsterAlive_OneFledRow_HpEnd1()
    {
        var f = await Mortis();
        f.Transcript.Should().Contain(Loc.Get("miracle.mortis_fires"));
        f.Result!.Outcome.Should().Be(CombatOutcome.PlayerEscaped);
        f.Hero.MiracleUsedToday.Should().BeTrue();
        var row = await OneRow(f, "fled");
        row["player_hp_end"].Should().Be(1L);
        f.Hero.Resurrections.Should().Be(3);
    }

    // ======================================================================
    // Row 7: a rescue on a mutual kill writes the victory pipeline's row only
    // ======================================================================

    [Fact]
    public async Task Row7_LastStandOnTheKillingRound_ExactlyOneVictoryRow()
    {
        // the monster wins the ambush, its blow kills the hero from full HP, and the Scales of Law send back
        // enough to kill it: both sides fall in the same round, then Last Stand rescues the hero
        var artifacts = StoryProgressionSystem.Instance.CollectedArtifacts;
        bool had = artifacts.Contains(ArtifactType.ScalesOfLaw);
        artifacts.Add(ArtifactType.ScalesOfLaw);
        Fight f;
        try { f = await RunFight(Hero(hp: 400), new List<Monster> { Brute(hp: 50, str: 60000, level: 200) }, ambush: true); }
        finally { if (!had) artifacts.Remove(ArtifactType.ScalesOfLaw); }
        f.Error.Should().BeNull("{0}", f.Transcript);
        f.Transcript.Should().Contain(Loc.Get("combat.last_stand"));
        f.Result!.Outcome.Should().Be(CombatOutcome.Victory);
        f.Result.DefeatedMonsters.Should().ContainSingle();
        await OneRow(f, "victory");
    }

    // ======================================================================
    // Row 8: a Divine Favor save with a monster alive is a flight at MaxHP/10
    // ======================================================================

    [Fact]
    public async Task Row8_DivineFavor_MonsterAlive_OneFledRow_HpEndMaxHpOverTen()
    {
        var f = await DivineFavor();
        f.Transcript.Should().Contain(Loc.Get("combat.divine_light_saves"));
        var row = await OneRow(f, "fled");
        long expected = Math.Max(1, f.Hero.MaxHP / 10);
        expected.Should().BeGreaterThan(1);
        row["player_hp_end"].Should().Be(expected, "the HP the favor left, not 1");
        f.Hero.HP.Should().Be(expected);
        f.Hero.Resurrections.Should().Be(3);
    }

    // ======================================================================
    // Row 9: every new row maps to telemetry outcome 0 to 2 and passes the bounds
    // ======================================================================

    [Fact]
    public async Task Row9_EveryNewEnding_MapsToOutcome0To2_AndPassesTheBounds()
    {
        var endings = new (string Name, Func<Task<Fight>> Run, string Label)[]
        {
            ("arrest", Arrest, "death"),
            ("exhibition", Exhibition, "death"),
            ("last stand", LastStand, "death"),
            ("death's door", DeathsDoor, "death"),
            ("mortis", Mortis, "fled"),
            ("divine favor", DivineFavor, "fled"),
            ("permadeath", () => Permadeath(), "death"),   // last: it removes the answer
        };
        int n = 0;
        foreach (var (name, run, label) in endings)
        {
            var f = await run();
            f.Error.Should().BeNull("{0}: {1}", name, f.Transcript);
            await Settled(f);
            n++;
            var rows = Rows("SELECT outcome FROM combat_events ORDER BY id;");
            rows.Should().HaveCount(n, "{0}: one combat_events row", name);
            rows[^1]["outcome"].Should().Be(label, name);
            var lines = Lines();
            lines.Should().HaveCount(n, "{0}: one telemetry row", name);
            using var doc = JsonDocument.Parse(lines[^1]);
            var r = doc.RootElement.GetProperty("r");
            TelemetryRow.IsValid(r).Should().BeTrue(name);
            r.GetProperty("outcome").GetInt64().Should().BeInRange(0, 2, name).And.Be(TelemetryRow.OutcomeNumber(label), name);
        }
        _store.ReadQueue().Should().HaveCount(endings.Length, "no row is dropped");
    }

    // ======================================================================
    // Row 10: an ordinary death still writes one row; a Mental collapse death keeps its row
    // ======================================================================

    [Fact]
    public async Task Row10_OrdinaryDeath_OneDeathRow()
    {
        var f = await RunFight(Hero(hp: 20), new List<Monster> { Brute() });
        f.Result!.PlayerActuallyDied.Should().BeTrue();
        f.Result.IsPermadeath.Should().BeFalse();
        await OneRow(f, "death");
    }

    [Fact]
    public async Task Row10_MentalCollapseDeath_KeepsItsOneRow()
    {
        var hero = Hero(hp: 600);
        var engine = new CombatEngine(new TerminalEmulator(new ScriptedStream(Attacks()), new MemoryStream()));
        var result = await engine.HandleMentalCollapseDeath(hero);
        result.IsPermadeath.Should().BeFalse();
        engine.LastCombatRowWrite.Should().NotBeNull();
        await engine.LastCombatRowWrite!;
        var rows = Rows("SELECT * FROM combat_events;");
        rows.Should().ContainSingle().Which["outcome"].Should().Be("death");
        hero.Resurrections.Should().Be(2, "the normal death rules: a resurrection is used");
    }
}
