using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;
using G = UsurperReborn.Tests.HitRoll128Golden;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.8 difficulty pass, piece P8 (DESIGN rows H1 to H4, and the draws): combat_events gains
/// player_swings, player_misses and elite_count. The counts ride the combat event stream
/// (CombatEvent.IsRoll, raised in CombatEngine.ShowPlayerHitRoll, the one place a to-hit die is rolled);
/// the telemetry row and the remote ingest keep the 1.2.7 column list.
/// </summary>
[Collection("SharedGameSingletons")]
public class HitColumns128Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly FieldInfo OsmField = typeof(OnlineStateManager).GetField("_fallbackInstance", SNP)!;
    private static readonly FieldInfo SaveField = typeof(SaveSystem).GetField("instance", SNP)!;
    private static readonly FieldInfo TallyField = typeof(CombatEngine).GetField("_eventTally", F)!;
    private static readonly FieldInfo StoryField = typeof(StoryProgressionSystem).GetField("_fallbackInstance", SNP)!;

    private static readonly string[] Three = { "player_swings", "player_misses", "elite_count" };

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-hitcolumns-{Guid.NewGuid():N}.db");
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

    // ---------------------------------------------------------------- database harness

    private SqlSaveBackend Db => _db ??= new SqlSaveBackend(_path);

    /// <summary>Online mode as a server session sets it: the SQL backend in SaveSystem and an OnlineStateManager.</summary>
    private void GoOnline()
    {
        SaveSystem.InitializeWithBackend(Db);
        var osm = (OnlineStateManager)Activator.CreateInstance(typeof(OnlineStateManager),
            BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { Db, "tester" }, null)!;
        OsmField.SetValue(null, osm);
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

    private List<string> Columns() => Rows("PRAGMA table_info(combat_events);").Select(r => (string)r["name"]!).ToList();

    private string Snapshot(IEnumerable<string>? only = null) => string.Join("\n", Rows("SELECT * FROM combat_events ORDER BY id;")
        .Select(r => string.Join("|", r.Where(kv => only == null || only.Contains(kv.Key)).Select(kv => $"{kv.Key}={kv.Value ?? "NULL"}"))));

    /// <summary>The 18 accumulator columns of 1.2.6 and 1.2.7, in the order that release added them.</summary>
    private static readonly string[] Tally127 =
    {
        "floor_actual", "difficulty", "party_size", "encounter_size", "first_actor",
        "dmg_to_player_basic", "dmg_to_player_ability", "dmg_to_player_spell", "dmg_to_player_dot",
        "dmg_to_team", "dmg_by_player", "dmg_by_team", "heal_player",
        "potions_used", "abilities_used", "spells_used", "teammates_lost", "player_hp_end",
    };

    /// <summary>combat_events as a 1.2.7 server left it: the table of before 1.2.6, the 18 columns and the two
    /// indexes that 1.2.6 added, a row of before 1.2.6 and two rows written by 1.2.7 (a win and a death).</summary>
    private void MakeDatabaseOf127()
    {
        Exec(@"
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
        CREATE INDEX idx_ce_class ON combat_events(player_class, outcome);");
        foreach (var c in Tally127) Exec($"ALTER TABLE combat_events ADD COLUMN {c} INTEGER;");
        Exec("CREATE INDEX idx_ce_created ON combat_events(created_at);" +
             "CREATE INDEX idx_ce_floor ON combat_events(floor_actual, created_at);");
        const string old = @"INSERT INTO combat_events (player_name, player_level, player_class, player_max_hp, player_str, player_dex,
            player_weap_pow, player_arm_pow, monster_name, monster_level, monster_max_hp, monster_str, monster_def, is_boss, outcome, rounds,
            damage_dealt, damage_taken, xp_gained, gold_gained, dungeon_floor, monster_count, has_teammates, created_at{0})
            VALUES ('Old One', 12, 'Warrior', 300, 40, 25, 30, 20, 'Orc', 11, 150, 30, 10, 0, '{1}', 4, 500, 120, 900, 60, 11, 2, 1, '{2}'{3});";
        Exec(string.Format(old, "", "victory", "2026-09-01 10:00:00", ""));
        string cols = string.Concat(Tally127.Select(c => ", " + c));
        Exec(string.Format(old, cols, "victory", "2026-10-06 10:00:00", ", 11, 1, 2, 2, 0, 90, 20, 10, 0, 40, 400, 100, 15, 1, 2, 0, 0, 180"));
        Exec(string.Format(old, cols, "death", "2026-10-07 11:00:00", ", 12, 2, 1, 3, 1, 250, 40, 0, 10, 0, 300, 0, 0, 0, 1, 0, 0, 0"));
    }

    // ---------------------------------------------------------------- fight harness

    private static async Task<T> WithRoll<T>(bool on, Func<Task<T>> body)
    {
        bool saved = GameConfig.PlayerHitRollEnabled;
        string lang = GameConfig.Language;
        try { GameConfig.PlayerHitRollEnabled = on; GameConfig.Language = "en"; return await body(); }
        finally { GameConfig.PlayerHitRollEnabled = saved; GameConfig.Language = lang; }
    }

    private static T WithRoll<T>(bool on, Func<T> body)
    {
        bool saved = GameConfig.PlayerHitRollEnabled;
        string lang = GameConfig.Language;
        try { GameConfig.PlayerHitRollEnabled = on; GameConfig.Language = "en"; return body(); }
        finally { GameConfig.PlayerHitRollEnabled = saved; GameConfig.Language = lang; }
    }

    private sealed class Recorder : ICombatObserver
    {
        public readonly List<CombatEvent> Events = new();
        public CombatFightStart? Start;
        public void OnFightStart(CombatFightStart start) { Start = start; }
        public void OnEvent(CombatEvent e) { Events.Add(e); }
        public void OnFightEnd(long playerHpEnd) { }
        public int Rolls(CombatSide side) => Events.Count(e => e.IsRoll && e.Actor == side);
        public int Misses(CombatSide side) => Events.Count(e => e.IsRoll && e.Missed && e.Actor == side);
    }

    private static string Plain(string transcript) => Regex.Replace(transcript, "\u001b\\[[0-9;?]*[A-Za-z]", "").Replace("\r", "");
    private static List<string> RowsOf(string transcript) => Plain(transcript).Split('\n').Select(r => r.Trim()).Where(r => r.Length > 0).ToList();

    /// <summary>What a reader counts on a screen: the roll lines and the miss lines, the player's (second
    /// person) apart from everyone else's (third person, with the name).</summary>
    private sealed class Lines
    {
        public List<string> All = new();
        public int YouRoll => All.Count(r => r.StartsWith("[You roll"));
        public int YouMiss => All.Count(r => r.StartsWith("You miss ") || r.StartsWith("CRITICAL MISS!"));
        public int OthersRoll => All.Count(r => !r.StartsWith("[You roll") && Regex.IsMatch(r, @"^\[.+ rolls? "));
        /// <summary>The miss lines of one teammate, by name (a monster that misses reads the same way).</summary>
        public int MissesOf(string name) => All.Count(r => r.StartsWith(name + " misses "));
        public int Evaded => All.Count(r => r.StartsWith("Your attack passes harmlessly through"));
    }

    private static Lines Read(string transcript) => new() { All = RowsOf(transcript) };

    /// <summary>A level 10 fighter that outlasts the fight.</summary>
    private static Character Hero(CharacterClass cls = CharacterClass.Warrior, int? main = null, int? off = null)
        => G.Hero(60_000, 60, 10, cls, 30, main, off);

    /// <summary>Foes 10 levels above the hero: the roll needed is the clamp of 8, so about a third of the dice miss.</summary>
    private static Monster Tough(string tier = "Wolf", long hp = 1500) => G.Foe(tier, hp, 30, 20, 5);

    private sealed class Fought
    {
        public G.Run Run = null!;
        public CombatEngine Engine = null!;
        public Recorder Rec = new();
        public CombatTally Tally => Run.Result!.Tally;
        public Lines Lines => Read(Run.Transcript);
    }

    private static Task<Fought> Fight(bool rollOn, Character hero, List<Monster> foes, List<Character>? team = null,
        bool events = true, int seed = 128) => WithRoll(rollOn, async () =>
    {
        var f = new Fought();
        f.Run = await G.Fight(hero, foes, team, seed, null, e => { f.Engine = e; e.CombatEventsEnabled = events; e.Observer = f.Rec; });
        f.Run.Error.Should().BeNull("{0}", f.Run.Transcript);
        return f;
    });

    private static async Task Written(Fought f)
    {
        f.Engine.LastCombatRowWrite.Should().NotBeNull("the fight logged a row");
        var t = f.Engine.LastCombatRowWrite!;
        (await Task.WhenAny(t, Task.Delay(TimeSpan.FromSeconds(30)))).Should().BeSameAs(t, "the background insert ends");
    }

    /// <summary>A single action on a rig (HitRoll128Golden) with the fight's accumulator attached, as
    /// BeginFightEvents attaches it in a whole fight.</summary>
    private static G.Rig RigWithTally(Character hero, Random random, List<Character>? team, Monster foe, ICombatObserver? observer = null)
    {
        var rig = G.NewRig(hero, random, team, foe);
        TallyField.SetValue(rig.Engine, rig.Result.Tally);
        rig.Engine.Observer = observer;
        return rig;
    }

    /// <summary>A seeded random whose twenty-sided die (Next(1, 21)) is set by its number (0 for the first).</summary>
    private sealed class DieRandom : Random
    {
        private readonly Func<int, int> _die;
        public int Dice;
        public DieRandom(Func<int, int> die, int seed = 128) : base(seed) { _die = die; }
        public override int Next(int minValue, int maxValue)
        {
            int v = base.Next(minValue, maxValue);
            if (minValue != 1 || maxValue != 21) return v;
            return _die(Dice++);
        }
    }

    // ---------------------------------------------------------------- H1: the columns and the migration

    [Fact]
    public async Task H1_ADatabaseOf127_GainsTheThreeColumnsAsNull_TwiceIsHarmless_AndAFightWritesAllThree()
    {
        MakeDatabaseOf127();
        var before = Columns();
        before.Should().HaveCount(25 + 18).And.NotContain(Three);
        string oldRows = Snapshot();
        var indexesBefore = Rows("SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'combat_events' ORDER BY name;").Select(r => (string)r["name"]!).ToList();

        _ = Db;   // 1.2.8 starts on the 1.2.7 database
        SqliteConnection.ClearAllPools();

        var columns = Columns();
        columns.Should().Equal(before.Concat(Three), "the three columns are added after the old ones, in the order of CombatTallyColumns");
        foreach (var c in Three)
        {
            var info = Rows("PRAGMA table_info(combat_events);").Single(r => (string)r["name"]! == c);
            info["type"].Should().Be("INTEGER", c);
            info["notnull"].Should().Be(0L, c);
            info["dflt_value"].Should().BeNull(c);
        }
        var rows = Rows("SELECT * FROM combat_events ORDER BY id;");
        rows.Should().HaveCount(3);
        foreach (var r in rows)
            foreach (var c in Three) r[c].Should().BeNull($"{c} is NULL on a row written before 1.2.8");
        Snapshot(before).Should().Be(oldRows, "every old value is unchanged");
        rows[1]["player_hp_end"].Should().Be(180L);
        rows[2]["outcome"].Should().Be("death");
        Rows("SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'combat_events' ORDER BY name;").Select(r => (string)r["name"]!)
            .Should().Equal(indexesBefore, "no index is added for the three columns");
        string migrated = Snapshot();

        // a second and a third start change nothing
        _ = new SqlSaveBackend(_path);
        _ = new SqlSaveBackend(_path);
        SqliteConnection.ClearAllPools();
        Columns().Should().Equal(columns);
        Snapshot().Should().Be(migrated);

        // a fight then writes all three
        GoOnline();
        var elite = Tough("Wolf", 400);
        elite.IsElite = true;
        var f = await Fight(true, Hero(), new List<Monster> { elite, Tough("Wolf", 400) });
        await Written(f);
        Db.CombatRowFailures.Should().Be(0);
        var row = Rows("SELECT * FROM combat_events ORDER BY id;").Last();
        f.Tally.PlayerSwings.Should().BeGreaterThan(0);
        f.Tally.PlayerMisses.Should().BeGreaterThan(0, "the seeded fight has misses, so the value compared is real");
        row["player_swings"].Should().Be((long)f.Tally.PlayerSwings);
        row["player_misses"].Should().Be((long)f.Tally.PlayerMisses);
        row["elite_count"].Should().Be(1L);
        row["encounter_size"].Should().Be(2L);
        Rows("SELECT * FROM combat_events WHERE player_swings IS NULL;").Should().HaveCount(3, "the old rows stay NULL");
    }

    [Fact]
    public void H1_ANewDatabase_HasTheThreeColumns_AndTheListHoldsThemLast()
    {
        _ = Db;
        SqliteConnection.ClearAllPools();
        var columns = Columns();
        columns.Should().HaveCount(25 + 18 + 3);
        columns.Skip(25).Should().Equal(Tally127.Concat(Three));
        SqlSaveBackend.CombatTallyColumns.Select(c => c.Column).Should().Equal(Tally127.Concat(Three));
        var tally = new CombatRowTally(1, 1, 1, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 5, PlayerSwings: 31, PlayerMisses: 7, EliteCount: 2);
        var values = SqlSaveBackend.CombatTallyColumns.ToDictionary(c => c.Column, c => c.Value(tally));
        values["player_swings"].Should().Be(31);
        values["player_misses"].Should().Be(7);
        values["elite_count"].Should().Be(2);
    }

    // ---------------------------------------------------------------- H2: the player's dice, and nobody else's

    [Fact]
    public async Task H2_ASoloFight_SwingsAndMissesEqualThePlayersRollLinesAndMissLines()
    {
        var f = await Fight(true, Hero(), new List<Monster> { Tough(), Tough() });
        var lines = f.Lines;
        lines.YouRoll.Should().BeGreaterThan(10, f.Run.Transcript);
        lines.YouMiss.Should().BeGreaterThan(2, "foes 10 levels above are missed about one time in three");
        lines.YouMiss.Should().BeLessThan(lines.YouRoll);
        lines.OthersRoll.Should().Be(0);
        f.Tally.PlayerSwings.Should().Be(lines.YouRoll);
        f.Tally.PlayerMisses.Should().Be(lines.YouMiss);
        f.Tally.EliteCount.Should().Be(0);
        // the observer saw the same dice, and a roll event carries no amount
        f.Rec.Rolls(CombatSide.Player).Should().Be(lines.YouRoll);
        f.Rec.Misses(CombatSide.Player).Should().Be(lines.YouMiss);
        f.Rec.Events.Where(e => e.IsRoll).Should().OnlyContain(e => e.Amount == 0 && e.Target == CombatSide.Monster && !e.IsUse && !e.TargetFell);
        f.Tally.DmgByPlayer.Should().Be(f.Run.Result!.TotalDamageDealt, "the roll events add nothing to the damage");
    }

    [Fact]
    public async Task H2_AFightWithATeammate_TheTeammatesDiceAreNotInThePlayersCount()
    {
        var mate = G.Mate("Ally One", 30_000, 10, 60, 20);
        var f = await Fight(true, Hero(), new List<Monster> { Tough(), Tough() }, new List<Character> { mate });
        var lines = f.Lines;
        lines.YouRoll.Should().BeGreaterThan(5, f.Run.Transcript);
        lines.YouMiss.Should().BeGreaterThan(0);
        lines.OthersRoll.Should().BeGreaterThan(5, "the teammate rolled");
        lines.MissesOf("Ally One").Should().BeGreaterThan(0, "the teammate missed");
        f.Tally.PlayerSwings.Should().Be(lines.YouRoll);
        f.Tally.PlayerMisses.Should().Be(lines.YouMiss);
        f.Rec.Rolls(CombatSide.Team).Should().Be(lines.OthersRoll, "the teammate's dice are in the event stream as the team's");
        f.Rec.Misses(CombatSide.Team).Should().Be(lines.MissesOf("Ally One"));
        f.Rec.Rolls(CombatSide.Player).Should().Be(f.Tally.PlayerSwings);
    }

    private static Character Follower(string name, string username, MemoryStream screen) => new Character
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Human, Level = 10,
        HP = 30_000, MaxHP = 30_000, BaseMaxHP = 30_000,
        Strength = 60, BaseStrength = 60, Defence = 15, BaseDefence = 15,
        Dexterity = 25, BaseDexterity = 25, Agility = 15, BaseAgility = 15,
        Constitution = 25, BaseConstitution = 25, Stamina = 80, BaseStamina = 80, Mental = 80,
        Class = CharacterClass.Warrior, Race = CharacterRace.Human, CombatSpeed = CombatSpeed.Instant,
        GroupPlayerUsername = username,
        RemoteTerminal = new TerminalEmulator(new MemoryStream(), screen),
        CombatInputChannel = System.Threading.Channels.Channel.CreateBounded<string>(1),
    };

    [Fact]
    public async Task H2_AGroupedFollower_RollsOnItsOwnScreen_AndIsNotInTheLeadersCount_AndWritesNoRow()
    {
        var prevSink = CombatEngine.GroupBroadcastSink;
        var prevLang = CombatEngine.LanguageOf;
        CombatEngine.GroupBroadcastSink = (_, _) => { };
        CombatEngine.LanguageOf = _ => "en";
        try
        {
            GoOnline();
            var screen = new MemoryStream();
            var follower = Follower("Grouped Friend", "p128follower", screen);
            using var stop = new CancellationTokenSource();
            var feeder = Task.Run(async () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    if (follower.IsAwaitingCombatInput && follower.CombatInputChannel!.Reader.Count == 0)
                        follower.CombatInputChannel.Writer.TryWrite("A");
                    await Task.Delay(2);
                }
            });
            var hero = Hero();
            var f = await Fight(true, hero, new List<Monster> { Tough(), Tough() }, new List<Character> { follower });
            stop.Cancel();
            await feeder;
            await Written(f);
            follower.RemoteTerminal!.StreamWriterInternal?.Flush();
            var own = Read(System.Text.Encoding.UTF8.GetString(screen.ToArray()));
            var leader = f.Lines;

            own.YouRoll.Should().BeGreaterThan(5, "the follower rolls in the second person on its own screen");
            leader.YouRoll.Should().BeGreaterThan(5);
            f.Tally.PlayerSwings.Should().Be(leader.YouRoll, "the leader's row holds the leader's dice only");
            f.Tally.PlayerMisses.Should().Be(leader.YouMiss);
            f.Rec.Rolls(CombatSide.Team).Should().Be(own.YouRoll, "the follower's dice are the team's in the event stream");
            f.Rec.Misses(CombatSide.Team).Should().Be(own.YouMiss);

            // one row for the fight, the leader's: a grouped follower writes no row of its own
            var rows = Rows("SELECT * FROM combat_events;");
            rows.Should().HaveCount(1);
            rows[0]["player_name"].Should().Be(hero.DisplayName);
            rows[0]["player_swings"].Should().Be((long)leader.YouRoll);
            rows[0]["player_misses"].Should().Be((long)leader.YouMiss);
        }
        finally
        {
            CombatEngine.GroupBroadcastSink = prevSink;
            CombatEngine.LanguageOf = prevLang;
        }
    }

    [Fact]
    public async Task H2_AnEvasion_IsNotASwingAndNotAMiss()
    {
        Monster Ghost()
        {
            var m = Tough();
            m.EvasionRounds = 1_000_000;
            m.EvasionMissChance = 50;
            return m;
        }
        var f = await Fight(true, Hero(), new List<Monster> { Ghost(), Ghost() });
        var lines = f.Lines;
        lines.Evaded.Should().BeGreaterThan(5, f.Run.Transcript);
        lines.YouRoll.Should().BeGreaterThan(5);
        f.Tally.PlayerSwings.Should().Be(lines.YouRoll, "an evaded swing rolls no die and is not counted");
        f.Tally.PlayerMisses.Should().Be(lines.YouMiss, "an evasion is not a miss");
        f.Rec.Events.Count(e => e.IsRoll).Should().Be(lines.YouRoll);
    }

    [Fact]
    public void H2_ACertainSwing_FromHidingOrWithAMiracleCrit_RollsNoDieAndIsNotCounted()
    {
        foreach (bool hiding in new[] { true, false })
        {
            var tally = WithRoll(true, () =>
            {
                var rig = RigWithTally(G.Hero(50_000, 300, 50), new DieRandom(_ => 1), null, G.Dummy(60, 600));
                for (int i = 0; i < 30; i++)
                {
                    if (hiding) rig.Hero.ApplyStatus(StatusEffect.Hidden, 2); else rig.Hero.MiracleCritPending = true;
                    G.Act(rig, G.A(CombatActionType.Attack));
                }
                Read(rig.Shown()).YouRoll.Should().Be(0);
                return rig.Result.Tally;
            });
            tally.PlayerSwings.Should().Be(0, hiding ? "from hiding" : "with a Miracle crit");
            tally.PlayerMisses.Should().Be(0);
        }
    }

    [Fact]
    public void H2_WithTheSunforgedBlade_ASavedDieIsASwingAndNotAMiss()
    {
        object? saved = StoryField.GetValue(null);
        try
        {
            var story = new StoryProgressionSystem();
            story.CollectArtifact(ArtifactType.SunforgedBlade);
            StoryField.SetValue(null, story);
            var (tally, lines) = WithRoll(true, () =>
            {
                // every face in turn against a foe that needs 8: the faces 2 to 7 are saved by the blade
                var rig = RigWithTally(G.Hero(50_000, 300, 50), new DieRandom(i => 1 + i % 20), null, G.Dummy(60, 600));
                for (int i = 0; i < 200; i++) G.Act(rig, G.A(CombatActionType.Attack));
                return (rig.Result.Tally, Read(rig.Shown()));
            });
            lines.YouRoll.Should().Be(200);
            lines.All.Count(r => r == "The Sunforged Blade guides your strike true!").Should().Be(60);
            tally.PlayerSwings.Should().Be(200);
            tally.PlayerMisses.Should().Be(10, "only the natural 1s miss; a die the blade saved landed");
            tally.PlayerMisses.Should().Be(lines.YouMiss);
        }
        finally { StoryField.SetValue(null, saved); }
    }

    // ---------------------------------------------------------------- H2b: every hand and every follow-up

    [Fact]
    public void H2b_ADualWieldRound_CountsTheDieOfEachHand()
    {
        var hero = G.Hero(50_000, 300, 50, CharacterClass.Assassin, 30, 40, 35);
        hero.IsDualWielding.Should().BeTrue();
        var rec = new Recorder();
        var (tally, lines) = WithRoll(true, () =>
        {
            G.Rig rig = null!;
            // every main-hand die a 10 (a hit), every off-hand die a 2 (a miss)
            var random = new DieRandom(_ => RowsOf(rig.Shown()).Last().StartsWith("Off-hand") ? 2 : 10);
            rig = RigWithTally(hero, random, null, G.Dummy(50, 600), rec);
            for (int i = 0; i < 20; i++) G.Act(rig, G.A(CombatActionType.Attack));
            return (rig.Result.Tally, Read(rig.Shown()));
        });
        int mains = lines.All.Count(r => r.StartsWith("You attack")), offs = lines.All.Count(r => r.StartsWith("Off-hand"));
        mains.Should().BeGreaterThanOrEqualTo(20);
        offs.Should().BeGreaterThanOrEqualTo(20);
        lines.YouRoll.Should().Be(mains + offs);
        tally.PlayerSwings.Should().Be(mains + offs, "each hand's die is counted");
        tally.PlayerMisses.Should().Be(offs, "the off hand missed every time");
        rec.Rolls(CombatSide.Player).Should().Be(mains + offs);
    }

    [Theory]
    [InlineData(CombatActionType.UseAbility, "execute")]
    [InlineData(CombatActionType.PowerAttack, "")]
    public void H2b_ARolledAbilityAndPowerAttack_CountTheStrikeAndItsOffHandFollowUp(CombatActionType type, string ability)
    {
        var hero = G.Hero(50_000, 300, 50, CharacterClass.Assassin, 30, 40, 35);
        var (tally, lines, dice) = WithRoll(true, () =>
        {
            // first die (the main strike) misses, second (the off-hand follow-up) hits, and so on
            var random = new DieRandom(i => i % 2 == 0 ? 2 : 10);
            var rig = RigWithTally(hero, random, null, G.Dummy(50, 600));
            for (int i = 0; i < 10; i++)
            {
                rig.Hero.ActiveStatuses.Remove(StatusEffect.PowerStance);
                G.Act(rig, G.A(type, ability));
            }
            return (rig.Result.Tally, Read(rig.Shown()), random.Dice);
        });
        dice.Should().Be(20, "the strike and its off-hand follow-up each roll");
        lines.YouRoll.Should().Be(20);
        tally.PlayerSwings.Should().Be(20, "the follow-up's die is counted with the strike's");
        tally.PlayerMisses.Should().Be(10);
        if (type == CombatActionType.UseAbility) tally.AbilitiesUsed.Should().Be(10, "a roll is not a use");
    }

    // ---------------------------------------------------------------- H2c: the roll switch off

    [Fact]
    public async Task H2c_WithTheRollOff_TheRowHoldsZeroAndZero_NotNull()
    {
        GoOnline();
        var f = await Fight(false, Hero(), new List<Monster> { Tough("Wolf", 600), Tough("Wolf", 600) });
        await Written(f);
        var lines = f.Lines;
        lines.All.Count(r => r.StartsWith("You attack")).Should().BeGreaterThan(5, "the player swung, with no roll");
        lines.YouRoll.Should().Be(0);
        f.Tally.Started.Should().BeTrue();
        f.Tally.PlayerSwings.Should().Be(0);
        f.Tally.PlayerMisses.Should().Be(0);
        f.Rec.Events.Should().NotContain(e => e.IsRoll);
        var row = Rows("SELECT * FROM combat_events;").Single();
        row["player_swings"].Should().Be(0L, "0, not NULL: the roll was off in a fight that 1.2.8 wrote");
        row["player_misses"].Should().Be(0L);
        row["elite_count"].Should().Be(0L);
    }

    [Fact]
    public void H2c_AFightNotEntered_WritesNullForTheThree_AsForEveryAccumulatorColumn()
    {
        _ = Db;
        var result = new CombatResult { Player = Hero(), Outcome = CombatOutcome.PlayerDied };
        Db.LogCombatEvent(CombatEngine.BuildCombatEventRow(result, "death", 0, 0));
        var row = Rows("SELECT * FROM combat_events;").Single();
        foreach (var c in Three) row[c].Should().BeNull(c);
        row["floor_actual"].Should().BeNull();
    }

    // ---------------------------------------------------------------- H2d: elite_count

    [Fact]
    public async Task H2d_TwoElitesOfFour_Writes2_AndABossRoomWithoutElitesWrites0()
    {
        GoOnline();
        var foes = new List<Monster> { Tough("Wolf", 300), Tough("Wolf", 300), Tough("Wolf", 300), Tough("Wolf", 300) };
        foes[1].IsElite = true;
        foes[3].IsElite = true;
        var f = await Fight(true, Hero(), foes);
        await Written(f);
        f.Tally.EncounterSize.Should().Be(4);
        f.Tally.EliteCount.Should().Be(2);
        f.Rec.Start!.Value.EliteCount.Should().Be(2);
        f.Rec.Start.Value.EncounterSize.Should().Be(4);

        var boss = Tough("Wolf", 600);
        boss.IsBoss = true;
        var b = await Fight(true, Hero(), new List<Monster> { boss, Tough("Wolf", 300) });
        await Written(b);
        b.Tally.EliteCount.Should().Be(0);
        b.Tally.EncounterSize.Should().Be(2);

        var rows = Rows("SELECT * FROM combat_events ORDER BY id;");
        rows.Should().HaveCount(2);
        rows[0]["elite_count"].Should().Be(2L);
        rows[0]["encounter_size"].Should().Be(4L);
        rows[1]["elite_count"].Should().Be(0L);
        rows[1]["is_boss"].Should().Be(1L);
    }

    [Fact]
    public void H2d_TheCountIsTakenOnce_AtTheStartOfTheFight_WithTheEncounterSize()
    {
        var tally = new CombatTally();
        tally.OnFightStart(new CombatFightStart(3, 1, 1, 4, 0, 2));
        tally.EliteCount.Should().Be(2);
        new CombatFightStart(3, 1, 1, 4, 0).EliteCount.Should().Be(0, "a start with no count given holds none");
        // nothing after the start moves it: a hit, a use, a roll
        tally.OnEvent(new CombatEvent(CombatSide.Player, CombatSide.Monster, 10, CombatEventKind.Basic));
        tally.OnEvent(new CombatEvent(CombatSide.Player, CombatSide.Monster, 0, CombatEventKind.Basic, IsRoll: true, Missed: true));
        tally.EliteCount.Should().Be(2);
        tally.EncounterSize.Should().Be(4);
        string src = File.ReadAllText(Path.Combine(Leftovers1114BTests.RepoRoot(), "Scripts", "Systems", "CombatEvents.cs"));
        Regex.Matches(src, @"\bm\.IsElite\b").Count.Should().Be(1, "the elites are counted in one place, StartFightEvents");
    }

    // ---------------------------------------------------------------- H3: the telemetry row is the 1.2.7 row

    private static readonly string[] TelemetryKeys127 =
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

    [Fact]
    public async Task H3_TheTelemetryRow_KeepsThe127ColumnList_WithoutTheThree()
    {
        TelemetryRow.Columns.Select(c => c.Key).Should().Equal(TelemetryKeys127);
        TelemetryRow.Columns.Should().HaveCount(39);
        TelemetryRow.Columns.Select(c => c.Key).Should().NotContain(Three);

        // a fight that rolled and met an elite: its telemetry row still holds the 39 values
        var elite = Tough("Wolf", 400);
        elite.IsElite = true;
        var hero = Hero();
        var f = await Fight(true, hero, new List<Monster> { elite });
        f.Tally.PlayerSwings.Should().BeGreaterThan(0);
        f.Tally.EliteCount.Should().Be(1);
        var row = TelemetryRow.From(CombatEngine.BuildCombatEventRow(f.Run.Result!, "victory", 0, 0), hero.Class, null);
        row.Should().NotBeNull();
        row!.Values.Should().HaveCount(39);
        row["player_hp_end"].Should().Be(Math.Max(0, f.Tally.PlayerHpEnd), "the last value is still the HP at the end");
    }

    // ---------------------------------------------------------------- H5: the counters draw nothing

    private static Character DualHero() => G.Hero(60_000, 60, 10, CharacterClass.Assassin, 30, 40, 35);

    [Theory]
    [InlineData("solo")]
    [InlineData("teammate")]
    [InlineData("dual wield")]
    [InlineData("roll off")]
    public async Task H5_TheSameSeededFight_DrawsTheSameNumbers_WithTheCountersAttachedAndWithout(string kind)
    {
        Task<Fought> One(bool events) => Fight(kind != "roll off", kind == "dual wield" ? DualHero() : Hero(),
            new List<Monster> { Tough(), Tough() },
            kind == "teammate" ? new List<Character> { G.Mate("Ally One", 30_000, 10, 60, 20) } : null, events);
        var off = await One(false);
        var off2 = await One(false);
        var on = await One(true);
        off.Run.Draws.Should().NotBeEmpty();
        off2.Run.Draws.Should().Equal(off.Run.Draws, "the seeded fight repeats exactly");
        on.Run.Draws.Should().Equal(off.Run.Draws, "the counters draw nothing and move no draw");
        G.Signature(on.Run).Should().Be(G.Signature(off.Run));
        on.Run.Result!.TotalDamageDealt.Should().Be(off.Run.Result!.TotalDamageDealt);
        on.Run.Result.TotalDamageTaken.Should().Be(off.Run.Result.TotalDamageTaken);
        // the counters were really attached in one run and not in the other
        off.Tally.Started.Should().BeFalse();
        off.Tally.PlayerSwings.Should().Be(0);
        on.Tally.Started.Should().BeTrue();
        if (kind == "roll off") on.Tally.PlayerSwings.Should().Be(0);
        else on.Tally.PlayerSwings.Should().Be(on.Lines.YouRoll).And.BeGreaterThan(5);
    }
}
