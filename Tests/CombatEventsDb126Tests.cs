using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
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
/// 1.2.6: the combat_events row carries the fight's accumulator (CombatResult.Tally) in new INTEGER
/// columns, is built on the combat thread and inserted in the background, and is pruned at
/// 30 days or 15000 rows (deaths 90 days). Fights are real PlayerVsMonsters fights against a temp
/// database in online mode (OnlineStateManager and SaveSystem set as a server session sets them).
/// </summary>
[Collection("SharedGameSingletons")]
public class CombatEventsDb126Tests : IDisposable
{
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly FieldInfo OsmField = typeof(OnlineStateManager).GetField("_fallbackInstance", SNP)!;
    private static readonly FieldInfo SaveField = typeof(SaveSystem).GetField("instance", SNP)!;
    private static readonly FieldInfo DoorField = typeof(DoorMode).GetField("_sessionInfo", SNP)!;

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-combatrow-{Guid.NewGuid():N}.db");
    private readonly object? _osmBefore = OsmField.GetValue(null);
    private readonly object? _saveBefore = SaveField.GetValue(null);
    private SqlSaveBackend? _db;

    public void Dispose()
    {
        OsmField.SetValue(null, _osmBefore);
        SaveField.SetValue(null, _saveBefore);
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _path, _path + "-wal", _path + "-shm" })
            try { File.Delete(f); } catch { }
    }

    private SqlSaveBackend Db => _db ??= new SqlSaveBackend(_path);

    /// <summary>Online mode as PlayerSession sets it: the SQL backend in SaveSystem and an OnlineStateManager.</summary>
    private void GoOnline()
    {
        SaveSystem.InitializeWithBackend(Db);
        var osm = (OnlineStateManager)Activator.CreateInstance(typeof(OnlineStateManager),
            BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { Db, "tester" }, null)!;
        OsmField.SetValue(null, osm);
    }

    /// <summary>Single-player or a BBS door: the backend may be SQL, but no OnlineStateManager exists.</summary>
    private void GoOffline()
    {
        SaveSystem.InitializeWithBackend(Db);
        OsmField.SetValue(null, null);
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection($"Data Source={_path};Pooling=false");
        c.Open();
        return c;
    }

    private void Exec(string sql)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private object? Scalar(string sql)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        var v = cmd.ExecuteScalar();
        return v is DBNull ? null : v;
    }

    private List<Dictionary<string, object?>> Rows(string sql)
    {
        using var c = Open();
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

    private static readonly string[] NewColumns =
    {
        "floor_actual", "difficulty", "party_size", "encounter_size", "first_actor",
        "dmg_to_player_basic", "dmg_to_player_ability", "dmg_to_player_spell", "dmg_to_player_dot",
        "dmg_to_team", "dmg_by_player", "dmg_by_team", "heal_player",
        "potions_used", "abilities_used", "spells_used", "teammates_lost", "player_hp_end",
    };

    private static object?[] TallyValues(CombatTally t) => new object?[]
    {
        (long)t.FloorActual, (long)t.Difficulty, (long)t.PartySize, (long)t.EncounterSize, (long)t.FirstActor,
        t.DmgToPlayerBasic, t.DmgToPlayerAbility, t.DmgToPlayerSpell, t.DmgToPlayerDot,
        t.DmgToTeam, t.DmgByPlayer, t.DmgByTeam, t.HealPlayer,
        (long)t.PotionsUsed, (long)t.AbilitiesUsed, (long)t.SpellsUsed, (long)t.TeammatesLost, t.PlayerHpEnd,
    };

    // ---------- fight harness (as CombatEvents126Tests) ----------

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

    private static Character Hero(long hp = 600) => new Character
    {
        Name1 = "Tester", Name2 = "Tester", AI = CharacterAI.Human,
        Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 10,
        HP = hp, MaxHP = hp, BaseMaxHP = hp,
        Strength = 60, BaseStrength = 60, Defence = 20, BaseDefence = 20,
        Dexterity = 30, BaseDexterity = 30, Agility = 20, BaseAgility = 20,
        Constitution = 30, BaseConstitution = 30, Intelligence = 20, BaseIntelligence = 20,
        Wisdom = 20, BaseWisdom = 20, Stamina = 100, BaseStamina = 100,
        Mental = 80, Gold = 0, Healing = 0, AutoHeal = false,
        CombatSpeed = CombatSpeed.Instant,
    };

    private static Monster Brute(string name = "Brute", long hp = 260) => new Monster
    {
        Name = name, Level = 8, HP = hp, MaxHP = hp, Strength = 40, Defence = 5, Experience = 1, Gold = 0,
        IsActive = true,
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

    /// <summary>[R]etreat until the flight succeeds. Used where the database is locked: a victory's autosave
    /// (CombatEngine HandleVictoryMultiMonster) waits on a locked database as it always has, a flight
    /// with no kill writes nothing but the combat row.</summary>
    private static string Retreats(int n = 30) => string.Concat(Enumerable.Repeat("R\n", n)) + string.Concat(Enumerable.Repeat("\n", 10));

    private static async Task<Fight> RunFight(Character? hero = null, List<Monster>? monsters = null, string? script = null)
    {
        var f = new Fight { Hero = hero ?? Hero(), Monsters = monsters ?? new List<Monster> { Brute("Brute A"), Brute("Brute B") } };
        var term = new TerminalEmulator(new ScriptedStream(script ?? Attacks()), f.Output);
        f.Engine = new CombatEngine(term);
        f.Engine.SeedRandomForTests(126);
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

    private static async Task Written(Fight f)
    {
        f.Engine.LastCombatRowWrite.Should().NotBeNull("the fight logged a row");
        var t = f.Engine.LastCombatRowWrite!;
        (await Task.WhenAny(t, Task.Delay(TimeSpan.FromSeconds(30)))).Should().BeSameAs(t, "the background insert ends");
    }

    // ---------- migration ----------

    private const string OldCombatEvents = @"
        CREATE TABLE combat_events (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            player_name TEXT NOT NULL,
            player_level INTEGER NOT NULL,
            player_class TEXT NOT NULL,
            player_max_hp INTEGER NOT NULL,
            player_str INTEGER NOT NULL,
            player_dex INTEGER NOT NULL,
            player_weap_pow INTEGER NOT NULL,
            player_arm_pow INTEGER NOT NULL,
            monster_name TEXT,
            monster_level INTEGER,
            monster_max_hp INTEGER,
            monster_str INTEGER,
            monster_def INTEGER,
            is_boss INTEGER DEFAULT 0,
            outcome TEXT NOT NULL,
            rounds INTEGER DEFAULT 0,
            damage_dealt INTEGER DEFAULT 0,
            damage_taken INTEGER DEFAULT 0,
            xp_gained INTEGER DEFAULT 0,
            gold_gained INTEGER DEFAULT 0,
            dungeon_floor INTEGER DEFAULT 0,
            monster_count INTEGER DEFAULT 1,
            has_teammates INTEGER DEFAULT 0,
            created_at TEXT DEFAULT (datetime('now'))
        );
        CREATE INDEX idx_ce_player ON combat_events(player_name, created_at DESC);
        CREATE INDEX idx_ce_outcome ON combat_events(outcome, created_at DESC);
        CREATE INDEX idx_ce_class ON combat_events(player_class, outcome);";

    private const string OldRow = @"INSERT INTO combat_events (player_name, player_level, player_class, player_max_hp, player_str, player_dex,
        player_weap_pow, player_arm_pow, monster_name, monster_level, monster_max_hp, monster_str, monster_def, is_boss, outcome, rounds,
        damage_dealt, damage_taken, xp_gained, gold_gained, dungeon_floor, monster_count, has_teammates, created_at)
        VALUES ('Old One', 12, 'Warrior', 300, 40, 25, 30, 20, 'Orc', 11, 150, 30, 10, 0, '{0}', 4, 500, 120, 900, 60, 11, 2, 1, '{1}');";

    private void MakeOldDatabase()
    {
        Exec(OldCombatEvents);
        Exec(string.Format(OldRow, "victory", "2026-09-01 10:00:00"));
        Exec(string.Format(OldRow, "death", "2026-09-02 11:00:00"));
    }

    private List<string> Columns() => Rows("PRAGMA table_info(combat_events);").Select(r => (string)r["name"]!).ToList();

    private string Snapshot(IEnumerable<string>? only = null) => string.Join("\n", Rows("SELECT * FROM combat_events ORDER BY id;")
        .Select(r => string.Join("|", r.Where(kv => only == null || only.Contains(kv.Key)).Select(kv => $"{kv.Key}={kv.Value ?? "NULL"}"))));

    [Fact]
    public void Migration_OldDatabase_GainsTheColumnsAsNull_KeepsItsRows_AndIsIdempotent()
    {
        MakeOldDatabase();
        var oldColumns = Columns();
        string oldRows = Snapshot();

        _ = Db;   // the game starts on the old database
        SqliteConnection.ClearAllPools();

        var columns = Columns();
        columns.Should().Contain(NewColumns);
        columns.Should().HaveCount(25 + NewColumns.Length);
        foreach (var c in NewColumns)
            Rows("PRAGMA table_info(combat_events);").Single(r => (string)r["name"]! == c)["type"].Should().Be("INTEGER", c);

        var rows = Rows("SELECT * FROM combat_events ORDER BY id;");
        rows.Should().HaveCount(2);
        foreach (var r in rows)
            foreach (var c in NewColumns) r[c].Should().BeNull($"{c} is NULL on a row written before 1.2.6");
        rows[0]["player_name"].Should().Be("Old One");
        rows[0]["damage_taken"].Should().Be(120L);
        rows[1]["outcome"].Should().Be("death");
        rows[1]["created_at"].Should().Be("2026-09-02 11:00:00");

        Snapshot(oldColumns).Should().Be(oldRows, "every old value is unchanged");
        string migrated = Snapshot();

        // a second start changes nothing
        _ = new SqlSaveBackend(_path);
        SqliteConnection.ClearAllPools();
        Columns().Should().Equal(columns);
        Snapshot().Should().Be(migrated);
    }

    [Fact]
    public void Migration_CreatesBothIndexes()
    {
        MakeOldDatabase();
        _ = Db;
        string IndexColumns(string name) => string.Join(",",
            Rows($"PRAGMA index_info({name});").OrderBy(r => (long)r["seqno"]!).Select(r => (string)r["name"]!));
        IndexColumns("idx_ce_created").Should().Be("created_at");
        IndexColumns("idx_ce_floor").Should().Be("floor_actual,created_at");
        Rows("EXPLAIN QUERY PLAN SELECT COUNT(*) FROM combat_events WHERE floor_actual = 3 AND created_at > '2026-01-01';")
            .Select(r => (string)r["detail"]!).Should().Contain(d => d.Contains("idx_ce_floor"));
    }

    // ---------- the row of a real fight ----------

    [Fact]
    public async Task RealFight_Online_WritesOneRow_WhoseNewColumnsEqualTheTally()
    {
        GoOnline();
        var f = await RunFight();
        f.Error.Should().BeNull("{0}", f.Transcript);
        f.Result!.Outcome.Should().Be(CombatOutcome.Victory);
        await Written(f);
        Db.CombatRowFailures.Should().Be(0);

        var rows = Rows("SELECT * FROM combat_events;");
        rows.Should().HaveCount(1);
        var row = rows[0];
        var t = f.Result.Tally;
        t.Started.Should().BeTrue();
        t.DmgByPlayer.Should().BeGreaterThan(0, "the values compared are real");
        t.DmgToPlayerBasic.Should().BeGreaterThan(0);
        NewColumns.Select(c => row[c]).Should().Equal(TallyValues(t));
        row["encounter_size"].Should().Be(2L);
        // the old columns keep their meaning
        row["outcome"].Should().Be("victory");
        row["player_name"].Should().Be(f.Hero.DisplayName);
        row["rounds"].Should().Be((long)f.Result.CurrentRound);
        row["damage_dealt"].Should().Be(f.Result.TotalDamageDealt);
        row["damage_taken"].Should().Be(f.Result.TotalDamageTaken);
        row["dungeon_floor"].Should().Be(8L, "dungeon_floor stays the monster level");
    }

    [Fact]
    public void FightNotEntered_WritesNullForTheNewColumns_NotZero()
    {
        _ = Db;
        var result = new CombatResult { Player = Hero(), Outcome = CombatOutcome.PlayerDied };
        result.Tally.Started.Should().BeFalse();
        Db.LogCombatEvent(CombatEngine.BuildCombatEventRow(result, "death", 0, 0));
        var row = Rows("SELECT * FROM combat_events;").Single();
        foreach (var c in NewColumns) row[c].Should().BeNull(c);
        row["outcome"].Should().Be("death");
    }

    // ---------- a locked database ----------

    private SqliteConnection Lock()
    {
        _ = Db;
        var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "BEGIN EXCLUSIVE;";
        cmd.ExecuteNonQuery();
        return c;
    }

    [Fact]
    public async Task LockedDatabase_TheFightEndsOnTime_AndOneErrorIsLogged()
    {
        GoOnline();
        Db.CombatRowTimeoutSeconds = 3;
        Fight f;
        using (var locker = Lock())
        {
            f = await RunFight(script: Retreats());
            f.Error.Should().BeNull("{0}", f.Transcript);
            f.Result!.Outcome.Should().Be(CombatOutcome.PlayerEscaped, f.Transcript);
            f.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(Db.CombatRowTimeoutSeconds), "the fight does not wait on the locked database");
            f.Engine.LastCombatRowWrite!.IsCompleted.Should().BeFalse("the insert is still waiting on the lock after the fight ended");
            await Written(f);
        }
        Db.CombatRowFailures.Should().Be(1);
        Db.CombatRowErrorLogs.Should().Be(1, "one failed write, one error line");
        Scalar("SELECT COUNT(*) FROM combat_events;").Should().Be(0L);
    }

    [Fact]
    public async Task LockedDatabase_RepeatedFailures_DoNotFloodTheLog()
    {
        GoOnline();
        Db.CombatRowTimeoutSeconds = 1;
        using var locker = Lock();
        for (int i = 0; i < 3; i++)
        {
            var f = await RunFight(script: Retreats());
            f.Error.Should().BeNull("{0}", f.Transcript);
            f.Result!.Outcome.Should().Be(CombatOutcome.PlayerEscaped, f.Transcript);
            await Written(f);
        }
        Db.CombatRowFailures.Should().Be(3);
        Db.CombatRowErrorLogs.Should().Be(1, "failures inside the window are counted, not logged");
    }

    // ---------- the snapshot ----------

    [Fact]
    public async Task Snapshot_ChangingTheCharacterAfterTheCall_DoesNotChangeTheRow()
    {
        GoOnline();
        Db.CombatRowTimeoutSeconds = 20;
        Fight f;
        (string Name, int Level, long Str, long MaxHP, string MName, int MLevel, long MMaxHP) expected;
        using (var locker = Lock())
        {
            f = await RunFight(script: Retreats());
            f.Error.Should().BeNull("{0}", f.Transcript);
            f.Result!.Outcome.Should().Be(CombatOutcome.PlayerEscaped, f.Transcript);
            f.Engine.LastCombatRowWrite.Should().NotBeNull();
            // the insert waits on the lock; the fight's objects change meanwhile
            string name = f.Hero.DisplayName;
            expected = (name, f.Hero.Level, f.Hero.Strength, f.Hero.MaxHP,
                f.Result.Monster!.Name, f.Result.Monster.Level, f.Result.Monster.MaxHP);
            f.Hero.Name1 = f.Hero.Name2 = "Changed";
            f.Hero.Level = 77;
            f.Hero.Strength = 999;
            f.Hero.MaxHP = 12345;
            f.Hero.DisplayName.Should().NotBe(name);
            foreach (var m in f.Monsters) { m.Name = "Changed Monster"; m.Level = 66; m.MaxHP = 4321; }
            using var rollback = locker.CreateCommand();
            rollback.CommandText = "ROLLBACK;";
            rollback.ExecuteNonQuery();
        }
        await Written(f);
        Db.CombatRowFailures.Should().Be(0);
        var row = Rows("SELECT * FROM combat_events;").Single();
        row["player_name"].Should().Be(expected.Name);
        row["player_level"].Should().Be((long)expected.Level);
        row["player_str"].Should().Be(expected.Str);
        row["player_max_hp"].Should().Be(expected.MaxHP);
        row["monster_name"].Should().Be(expected.MName);
        row["monster_level"].Should().Be((long)expected.MLevel);
        row["monster_max_hp"].Should().Be(expected.MMaxHP);
        NewColumns.Select(c => row[c]).Should().Equal(TallyValues(f.Result!.Tally));
    }

    // ---------- retention ----------

    private void Insert(string outcome, string age)
    {
        Exec($@"INSERT INTO combat_events (player_name, player_level, player_class, player_max_hp, player_str, player_dex,
            player_weap_pow, player_arm_pow, outcome, created_at)
            VALUES ('{outcome} {age}', 1, 'Warrior', 1, 1, 1, 1, 1, '{outcome}', datetime('now', '{age}'));");
    }

    private List<string> Names() => Rows("SELECT player_name FROM combat_events ORDER BY id;").Select(r => (string)r["player_name"]!).ToList();

    [Fact]
    public async Task Retention_NonDeaths30Days_Deaths90Days()
    {
        _ = Db;
        foreach (var o in new[] { "victory", "fled", "death" })
            foreach (var age in new[] { "-29 days", "-31 days", "-89 days", "-91 days" })
                Insert(o, age);
        await Db.PruneCombatEvents();
        Names().Should().BeEquivalentTo(new[]
        {
            "victory -29 days", "fled -29 days",
            "death -29 days", "death -31 days", "death -89 days",
        });
        SqlSaveBackend.CombatEventKeepDays.Should().Be(30);
        SqlSaveBackend.CombatDeathKeepDays.Should().Be(90);
        SqlSaveBackend.CombatEventMaxRows.Should().Be(15000);
    }

    [Fact]
    public async Task Retention_RowCap_KeepsTheNewestNonDeaths_AndExemptsDeaths()
    {
        _ = Db;
        for (int i = 5; i >= 1; i--) Insert("victory", $"-{i} hours");
        for (int i = 4; i >= 1; i--) Insert("death", $"-{i} minutes");
        await Db.PruneCombatEvents(maxRows: 3);
        Names().Should().BeEquivalentTo(new[]
        {
            "victory -3 hours", "victory -2 hours", "victory -1 hours",
            "death -4 minutes", "death -3 minutes", "death -2 minutes", "death -1 minutes",
        });
    }

    [Fact]
    public void Retention_TheDailyResetUsesTheDefaults()
    {
        string src = File.ReadAllText(Path.Combine(Leftovers1114BTests.RepoRoot(), "Scripts", "Systems", "WorldSimService.cs"));
        src.Should().Contain("await sqlBackend.PruneCombatEvents();");
    }

    // ---------- dashboard compatibility ----------

    /// <summary>Every combat_events statement of the /api/balance routes in web/ssh-proxy.js, read from the file.</summary>
    private static List<string> DashboardStatements()
    {
        string js = File.ReadAllText(Path.Combine(Leftovers1114BTests.RepoRoot(), "web", "ssh-proxy.js"));
        int start = js.IndexOf("url === '/api/balance/overview'", StringComparison.Ordinal);
        int end = js.IndexOf("async function handleDashRequest", start, StringComparison.Ordinal);
        start.Should().BePositive();
        end.Should().BeGreaterThan(start);
        string block = js[start..end];
        return Regex.Matches(block, @"db\.prepare\(\s*(?:`([^`]*)`|'([^']*)'|""([^""]*)"")\s*\)")
            .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value)
            .Where(s => s.Contains("combat_events"))
            .ToList();
    }

    [Fact]
    public async Task Dashboard_EveryBalanceStatement_RunsOnTheMigratedDatabase_WithOldAndNewRows()
    {
        MakeOldDatabase();
        GoOnline();
        var f = await RunFight();
        f.Error.Should().BeNull("{0}", f.Transcript);
        await Written(f);
        Db.LogCombatEvent(CombatEngine.BuildCombatEventRow(new CombatResult { Player = Hero() }, "death", 0, 0));
        Scalar("SELECT COUNT(*) FROM combat_events WHERE floor_actual IS NULL;").Should().Be(3L, "two old rows and one row of a fight not entered");
        Scalar("SELECT COUNT(*) FROM combat_events WHERE floor_actual IS NOT NULL;").Should().Be(1L);

        var statements = DashboardStatements();
        statements.Should().HaveCountGreaterThan(15, "the balance routes were found");
        using var c = Open();
        foreach (var sql in statements)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            if (sql.Contains('?'))
            {
                cmd.CommandText = sql.Replace("?", "$player");
                cmd.Parameters.AddWithValue("$player", "Tester");
            }
            var act = () => { using var r = cmd.ExecuteReader(); while (r.Read()) { } };
            act.Should().NotThrow(sql);
        }
    }

    // ---------- online only ----------

    [Fact]
    public async Task SinglePlayer_WritesNoRow()
    {
        GoOffline();
        var f = await RunFight();
        f.Error.Should().BeNull("{0}", f.Transcript);
        f.Result!.Tally.Started.Should().BeTrue();
        f.Engine.LastCombatRowWrite.Should().BeNull();
        Scalar("SELECT COUNT(*) FROM combat_events;").Should().Be(0L);
    }

    [Fact]
    public async Task DoorMode_WritesNoRow()
    {
        GoOffline();
        var before = DoorField.GetValue(null);
        Fight f;
        try
        {
            DoorField.SetValue(null, new BBSSessionInfo { SourceType = DropFileType.DoorSys });
            DoorMode.IsInDoorMode.Should().BeTrue();
            f = await RunFight();
        }
        finally { DoorField.SetValue(null, before); }
        f.Error.Should().BeNull("{0}", f.Transcript);
        f.Engine.LastCombatRowWrite.Should().BeNull();
        Scalar("SELECT COUNT(*) FROM combat_events;").Should().Be(0L);
    }
}
