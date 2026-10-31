using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// O2: the hourly world_snapshots row. The table is made at start on a database that lacks it, with no other table
/// changed; the MUD's world sim writes one row per UTC hour (INSERT OR IGNORE, so a restart in the same hour adds
/// nothing) and prunes rows older than 90 days in the same write; births, NPC deaths and marriages are counted in
/// memory by their code paths and read and reset by the write. Against a temp database.
/// </summary>
[Collection("SharedGameSingletons")]
public class WorldSnapshotsO2Tests : IDisposable
{
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags INP = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly FieldInfo KingField = typeof(CastleLocation).GetField("currentKing", SNP)!;

    private static readonly string[] Columns =
    {
        "taken_at", "npcs_alive", "npcs_dead", "npcs_married", "children", "treasury",
        "players_online", "births", "deaths", "marriages",
    };

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-snapshots-{Guid.NewGuid():N}.db");
    private SqlSaveBackend? _db;
    private SqlSaveBackend Db => _db ??= new SqlSaveBackend(_path);

    public WorldSnapshotsO2Tests() => WorldSnapshotCounters.TakeAndReset();

    public void Dispose()
    {
        WorldSnapshotCounters.TakeAndReset();
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _path, _path + "-wal", _path + "-shm" })
            try { File.Delete(f); } catch { }
    }

    private static DateTime Utc(int day, int hour, int minute = 0, int second = 0) => new(2026, 10, day, hour, minute, second, DateTimeKind.Utc);

    private SqliteConnection Open()
    {
        var c = new SqliteConnection($"Data Source={_path};Pooling=false");
        c.Open();
        return c;
    }

    private List<Dictionary<string, object?>> Rows(string sql)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        var rows = new List<Dictionary<string, object?>>();
        while (r.Read())
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < r.FieldCount; i++) row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }

    private void Exec(string sql)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private List<Dictionary<string, object?>> Snapshots() => Rows("SELECT * FROM world_snapshots ORDER BY taken_at;");

    private void PutRow(string takenAt, long births = 0) =>
        Exec($"INSERT INTO world_snapshots VALUES ('{takenAt}', 1, 1, 1, 1, 1, 1, {births}, 0, 0);");

    private static WorldSnapshotRow Row(long births = 0) => new(10, 2, 4, 3, 500, births, 0, 0);

    /// <summary>The roster replaced by the given NPCs (and the king by none) for the body, then put back.</summary>
    private static T WithRoster<T>(IEnumerable<NPC> roster, Func<T> body)
    {
        var active = NPCSpawnSystem.Instance.ActiveNPCs;
        var saved = active.ToList();
        var king = KingField.GetValue(null);
        try
        {
            active.Clear();
            active.AddRange(roster);
            KingField.SetValue(null, null);
            return body();
        }
        finally
        {
            active.Clear();
            active.AddRange(saved);
            KingField.SetValue(null, king);
        }
    }

    private static NPC Npc(string id, CharacterRace race = CharacterRace.Human, int hp = 100)
    {
        return new NPC
        {
            ID = id, Name1 = "Snap " + id, Name2 = "Snap " + id, Race = race, Sex = CharacterSex.Male, Age = 30,
            Level = 5, HP = hp, MaxHP = 100, BaseMaxHP = 100, CurrentLocation = "Main Street",
        };
    }

    // ---------- the table ----------

    [Fact]
    public void Table_IsMade_OnAnExistingDatabaseWithoutIt_AndNoOtherTableChanges()
    {
        _ = Db;
        SqliteConnection.ClearAllPools();
        Exec("DROP TABLE world_snapshots;");
        Exec("INSERT INTO news (message, category) VALUES ('kept', 'general');");
        List<string> Schema() => Rows("SELECT type, name, sql FROM sqlite_master WHERE tbl_name <> 'world_snapshots' ORDER BY type, name;")
            .Select(r => $"{r["type"]} {r["name"]} {r["sql"]}").ToList();
        var before = Schema();
        Rows("SELECT name FROM sqlite_master WHERE name = 'world_snapshots';").Should().BeEmpty();

        _ = new SqlSaveBackend(_path);   // a start on the old database
        SqliteConnection.ClearAllPools();

        var info = Rows("PRAGMA table_info(world_snapshots);");
        info.Select(r => (string)r["name"]!).Should().Equal(Columns);
        info.Single(r => (string)r["name"]! == "taken_at")["type"].Should().Be("TEXT");
        info.Single(r => (string)r["name"]! == "taken_at")["pk"].Should().Be(1L);
        info.Where(r => (string)r["name"]! != "taken_at").Select(r => (string)r["type"]!).Should().OnlyContain(t => t == "INTEGER");
        Schema().Except(before).Should().BeEmpty("the migration only adds the new table");
        before.Except(Schema()).Should().BeEmpty("the migration changes no other table");
        Rows("SELECT message FROM news;").Select(r => (string)r["message"]!).Should().Equal("kept");

        _ = new SqlSaveBackend(_path);   // a second start changes nothing
        SqliteConnection.ClearAllPools();
        Rows("PRAGMA table_info(world_snapshots);").Should().HaveCount(10);
    }

    [Fact]
    public void HourKey_IsTheTopOfTheUtcHour()
    {
        SqlSaveBackend.WorldSnapshotHourKey(new DateTime(2026, 10, 31, 14, 59, 59, DateTimeKind.Utc)).Should().Be("2026-10-31T14:00:00Z");
        SqlSaveBackend.WorldSnapshotHourKey(new DateTime(2026, 1, 2, 3, 0, 0, DateTimeKind.Utc)).Should().Be("2026-01-02T03:00:00Z");
    }

    // ---------- one row per hour ----------

    [Fact]
    public void Writer_WritesOneRowPerUtcHour()
    {
        var sim = new WorldSimService(Db);
        WithRoster(new[] { Npc("o2_hour") }, () =>
        {
            sim.WriteHourlySnapshotIfDue(Utc(31, 14, 5)).Should().BeTrue();
            sim.WriteHourlySnapshotIfDue(Utc(31, 14, 40)).Should().BeFalse("the hour already has its row");
            sim.WriteHourlySnapshotIfDue(Utc(31, 15, 1)).Should().BeTrue();
            return 0;
        });
        Snapshots().Select(r => (string)r["taken_at"]!).Should().Equal("2026-10-31T14:00:00Z", "2026-10-31T15:00:00Z");
    }

    [Fact]
    public void ASecondWriteInTheSameHour_IsIgnored_AndTheFirstRowStands()
    {
        Db.WriteWorldSnapshot(Utc(31, 14, 5), Row(births: 7)).Should().Be(WorldSnapshotWrite.Inserted);
        Db.WriteWorldSnapshot(Utc(31, 14, 50), Row(births: 99)).Should().Be(WorldSnapshotWrite.AlreadyExists);
        var rows = Snapshots();
        rows.Should().HaveCount(1);
        rows[0]["births"].Should().Be(7L, "INSERT OR IGNORE keeps the first row of the hour");
    }

    /// <summary>
    /// A second world sim service in the same process and hour (the static counters are shared, as in one MUD
    /// process): its row is already there, so it adds none and its counts wait for the next row. A real process
    /// restart starts with empty counters and loses the counts not yet written.
    /// </summary>
    [Fact]
    public void ASecondServiceInTheSameProcessAndHour_AddsNoRow_AndKeepsTheCountsForTheNextRow()
    {
        WithRoster(new[] { Npc("o2_restart") }, () =>
        {
            new WorldSimService(Db).WriteHourlySnapshotIfDue(Utc(31, 14, 5)).Should().BeTrue();
            WorldSnapshotCounters.AddBirth();
            var second = new WorldSimService(Db);
            second.WriteHourlySnapshotIfDue(Utc(31, 14, 30)).Should().BeFalse();
            WorldSnapshotCounters.Births.Should().Be(1, "a row not written gives its counts back");
            second.WriteHourlySnapshotIfDue(Utc(31, 14, 45)).Should().BeFalse("the hour is done once its row is found");
            second.WriteHourlySnapshotIfDue(Utc(31, 15, 0)).Should().BeTrue();
            return 0;
        });
        Snapshots().Select(r => (long)r["births"]!).Should().Equal(0L, 1L);
    }

    [Fact]
    public void Writer_WritesNothing_WhenNotStarted()
    {
        // only the hourly call writes; constructing the service (as every door and test does) adds no row
        _ = new WorldSimService(Db);
        Snapshots().Should().BeEmpty();
    }

    [Fact]
    public void Writer_RecordsTheGauges()
    {
        var alive = Npc("o2_alive");
        var married = Npc("o2_married"); married.IsMarried = true;
        var dead = Npc("o2_dead", hp: 0); dead.IsDead = true;
        var downed = Npc("o2_downed", hp: 0); downed.Lifecycle = AllyLifecycle.Downed;
        _ = Db;
        Exec("INSERT INTO online_players (username, display_name, last_heartbeat) VALUES ('o2_online', 'Online', datetime('now'));");
        Exec("INSERT INTO online_players (username, display_name, last_heartbeat) VALUES ('o2_stale', 'Stale', datetime('now', '-1 hour'));");
        var family = FamilySystem.Instance;
        var kids = new[] { new Child { Name = "O2 Kid A", Mother = "o2m", Father = "o2f" }, new Child { Name = "O2 Kid B", Mother = "o2m", Father = "o2f", Deleted = true } };
        var savedKids = family.AllChildren.ToList();
        try
        {
            family.AllChildren.Clear();
            family.AllChildren.AddRange(kids);
            WithRoster(new[] { alive, married, dead, downed }, () =>
            {
                KingField.SetValue(null, new King { Name = "O2 King", Treasury = 123_456 });
                return new WorldSimService(Db).WriteHourlySnapshotIfDue(Utc(31, 9));
            }).Should().BeTrue();
        }
        finally
        {
            family.AllChildren.Clear();
            family.AllChildren.AddRange(savedKids);
        }
        var row = Snapshots().Single();
        row["npcs_alive"].Should().Be(2L);
        row["npcs_dead"].Should().Be(1L, "a Downed ally is not dead");
        row["npcs_married"].Should().Be(1L);
        row["children"].Should().Be(1L, "a deleted child is not counted");
        row["treasury"].Should().Be(123_456L);
        row["players_online"].Should().Be(1L, "a stale heartbeat is not online");
    }

    // ---------- counters ----------

    [Fact]
    public void Counters_AddAndReset_WhenTheRowIsWritten()
    {
        WorldSnapshotCounters.AddBirth(); WorldSnapshotCounters.AddBirth();
        WorldSnapshotCounters.AddDeath();
        WorldSnapshotCounters.AddMarriage(); WorldSnapshotCounters.AddMarriage(); WorldSnapshotCounters.AddMarriage();
        var sim = new WorldSimService(Db);
        WithRoster(Array.Empty<NPC>(), () =>
        {
            sim.WriteHourlySnapshotIfDue(Utc(31, 10)).Should().BeTrue();
            (WorldSnapshotCounters.Births, WorldSnapshotCounters.Deaths, WorldSnapshotCounters.Marriages).Should().Be((0L, 0L, 0L));
            sim.WriteHourlySnapshotIfDue(Utc(31, 11)).Should().BeTrue();
            return 0;
        });
        var rows = Snapshots();
        (rows[0]["births"], rows[0]["deaths"], rows[0]["marriages"]).Should().Be((2L, 1L, 3L));
        (rows[1]["births"], rows[1]["deaths"], rows[1]["marriages"]).Should().Be((0L, 0L, 0L), "the counts were reset by the first row");
    }

    [Fact]
    public void Counters_AreThreadSafe()
    {
        Parallel.For(0, 20_000, i =>
        {
            WorldSnapshotCounters.AddBirth();
            if (i % 2 == 0) WorldSnapshotCounters.AddDeath();
        });
        WorldSnapshotCounters.TakeAndReset().Should().Be((20_000L, 10_000L, 0L));
        WorldSnapshotCounters.TakeAndReset().Should().Be((0L, 0L, 0L));
    }

    // ---------- retention ----------

    [Fact]
    public void TheWrite_PrunesRowsOlderThan90Days_AndKeepsTheRest()
    {
        var now = Utc(31, 12, 20);
        string Key(DateTime t) => SqlSaveBackend.WorldSnapshotHourKey(t);
        _ = Db;
        PutRow(Key(now.AddDays(-120)));
        PutRow(Key(now.AddDays(-90).AddHours(-1)));
        PutRow(Key(now.AddDays(-90)));   // exactly 90 days: kept
        PutRow(Key(now.AddDays(-30)));
        Db.WriteWorldSnapshot(now, Row()).Should().Be(WorldSnapshotWrite.Inserted);
        Snapshots().Select(r => (string)r["taken_at"]!).Should().Equal(
            Key(now.AddDays(-90)), Key(now.AddDays(-30)), Key(now));
    }

    [Fact]
    public void TheWrite_StillPrunes_WhenTheHourAlreadyHasItsRow()
    {
        var now = Utc(31, 12, 20);
        _ = Db;
        Db.WriteWorldSnapshot(now, Row()).Should().Be(WorldSnapshotWrite.Inserted);
        PutRow(SqlSaveBackend.WorldSnapshotHourKey(now.AddDays(-100)));
        Db.WriteWorldSnapshot(now.AddMinutes(10), Row()).Should().Be(WorldSnapshotWrite.AlreadyExists);
        Snapshots().Select(r => (string)r["taken_at"]!).Should().Equal(SqlSaveBackend.WorldSnapshotHourKey(now));
    }

    // ---------- each counter path adds one ----------

    [Fact]
    public void ABirth_AddsOne_AndTheSameChildTwiceAddsNothing()
    {
        var family = FamilySystem.Instance;
        var child = new Child { Name = "O2 Newborn Zyx", Mother = "O2 Mother", Father = "O2 Father", MotherID = "o2m", FatherID = "o2f", BirthDate = new DateTime(2026, 10, 31, 8, 0, 0) };
        try
        {
            family.RegisterChild(child);
            WorldSnapshotCounters.Births.Should().Be(1);
            family.RegisterChild(child);
            WorldSnapshotCounters.Births.Should().Be(1, "a duplicate is not a birth");
        }
        finally { family.AllChildren.Remove(child); }
    }

    [Fact]
    public void AnNpcDeath_AddsOne_AndABlockedOrRepeatedDeathAddsNothing()
    {
        var sim = new WorldSimulator();
        var victim = Npc("o2_victim");
        var engaged = Npc("o2_engaged"); engaged.IsInConversation = true;
        var party = GameEngine.Instance.DungeonPartyNPCIds.ToList();
        try
        {
            GameEngine.Instance.SetDungeonPartyNPCs(Array.Empty<string>());
            sim.MarkNPCDead(victim, 0f, "A Killer", "Inn");
            victim.IsDead.Should().BeTrue();
            WorldSnapshotCounters.Deaths.Should().Be(1);
            sim.MarkNPCDead(victim, 0f, "A Killer", "Inn");
            WorldSnapshotCounters.Deaths.Should().Be(1, "an NPC already dead does not die twice");
            sim.MarkNPCDead(engaged, 0f, "A Killer", "Inn");
            engaged.IsDead.Should().BeFalse();
            WorldSnapshotCounters.Deaths.Should().Be(1, "a death the guards refuse is no death");
        }
        finally { GameEngine.Instance.SetDungeonPartyNPCs(party); }
    }

    [Fact]
    public void ADeathOfOldAge_AddsOne_AndTheYoungAddNothing()
    {
        var old = Npc("o2_old");
        old.BirthDate = DateTime.Now.AddHours(-200 * GameConfig.NpcLifecycleHoursPerYear);
        var roster = Enumerable.Range(0, 5).Select(i =>
        {
            var n = Npc($"o2_young_{i}");
            n.BirthDate = DateTime.Now.AddHours(-20 * GameConfig.NpcLifecycleHoursPerYear);
            return n;
        }).Append(old).ToList();
        WithRoster(roster, () =>
        {
            typeof(WorldSimulator).GetMethod("ProcessNPCAging", INP)!.Invoke(new WorldSimulator(), null);
            return 0;
        });
        old.IsAgedDeath.Should().BeTrue();
        WorldSnapshotCounters.Deaths.Should().Be(1);
    }

    [Fact]
    public void AWedding_AddsOne_AndARefusedWeddingAddsNothing()
    {
        var player = new Character { Name1 = "O2WedP", Name2 = "O2WedP", Sex = CharacterSex.Male, Age = 30, AI = CharacterAI.Human, Level = 10, HP = 50, MaxHP = 50, ID = "o2wedp" };
        var npc = new NPC { Name1 = "O2WedN", Name2 = "O2WedN", Sex = CharacterSex.Female, Age = 30, Level = 10, HP = 50, MaxHP = 50, ID = "o2wedn", IntimacyActs = 3 };
        var stranger = new NPC { Name1 = "O2WedS", Name2 = "O2WedS", Sex = CharacterSex.Female, Age = 30, Level = 10, HP = 50, MaxHP = 50, ID = "o2weds" };
        var rel = RelationshipSystem.GetOrCreateRelationship(npc, player);
        rel.Relation1 = GameConfig.RelationLove;
        rel.Relation2 = GameConfig.RelationLove;
        rel.CreatedOnGameDay = -1000;
        try
        {
            RelationshipSystem.PerformMarriage(stranger, player, out _).Should().BeFalse();
            WorldSnapshotCounters.Marriages.Should().Be(0, "a refused wedding is no marriage");
            RelationshipSystem.PerformMarriage(npc, player, out string message).Should().BeTrue(message);
            WorldSnapshotCounters.Marriages.Should().Be(1, "one wedding, not one per spouse");
        }
        finally
        {
            NPCMarriageRegistry.Instance.EndMarriage(npc.ID);
            RomanceTracker.Instance.Spouses.RemoveAll(s => s.NPCId == npc.ID);
        }
    }

    [Fact]
    public void AnNpcWedding_AddsOne_AndARefusedNpcWeddingAddsNothing()
    {
        var execute = typeof(EnhancedNPCBehaviors).GetMethod("ExecuteNPCMarriage", BindingFlags.NonPublic | BindingFlags.Static)!;
        var a = Npc("o2_npc_bride"); a.Sex = CharacterSex.Female;
        var b = Npc("o2_npc_groom");
        var wed = Npc("o2_npc_wed"); wed.IsMarried = true; wed.Married = true;
        try
        {
            execute.Invoke(null, new object[] { wed, b });   // already married and not poly: refused
            WorldSnapshotCounters.Marriages.Should().Be(0);
            execute.Invoke(null, new object[] { a, b });
            a.IsMarried.Should().BeTrue();
            WorldSnapshotCounters.Marriages.Should().Be(1);
        }
        finally { NPCMarriageRegistry.Instance.EndMarriage(a.ID); }
    }

    // ---------- retries, failures and the lock ----------

    [Fact]
    public void AFailedWrite_GivesTheCountsBack_AndATryLaterInTheHourWritesTheRow()
    {
        _ = Db;
        Exec("DROP TABLE world_snapshots;");   // the write fails
        var sim = new WorldSimService(Db, simIntervalSeconds: 60);
        WorldSnapshotCounters.AddBirth();
        WithRoster(new[] { Npc("o2_fail") }, () =>
        {
            sim.WriteHourlySnapshotIfDue(Utc(31, 16, 0)).Should().BeFalse();
            WorldSnapshotCounters.Births.Should().Be(1, "a failed write gives the counts back");
            _ = new SqlSaveBackend(_path);   // the table is back
            SqliteConnection.ClearAllPools();
            sim.WriteHourlySnapshotIfDue(Utc(31, 16, 0, 30)).Should().BeFalse("the next try waits one sim interval");
            sim.WriteHourlySnapshotIfDue(Utc(31, 16, 1)).Should().BeTrue("the hour is tried again after a failure");
            return 0;
        });
        var row = Snapshots().Single();
        row["taken_at"].Should().Be("2026-10-31T16:00:00Z");
        row["births"].Should().Be(1L);
        WorldSnapshotCounters.Births.Should().Be(0);
    }

    [Fact]
    public void AFailingHour_IsTriedAtMostFiveTimes()
    {
        _ = Db;
        Exec("DROP TABLE world_snapshots;");
        var sim = new WorldSimService(Db, simIntervalSeconds: 1);
        int tries = 0;
        sim.BeforeSnapshotGaugeRead = () => tries++;
        WithRoster(Array.Empty<NPC>(), () =>
        {
            for (int s = 0; s < 3600; s++) sim.WriteHourlySnapshotIfDue(Utc(31, 17, 0).AddSeconds(s));
            return 0;
        });
        tries.Should().Be(WorldSimService.SnapshotMaxTriesPerHour);
    }

    [Fact]
    public void AGaugeReadFailure_WritesNoRow_KeepsTheCounts_AndIsTriedAgain()
    {
        var sim = new WorldSimService(Db, simIntervalSeconds: 60);
        bool fail = true;
        sim.BeforeSnapshotGaugeRead = () => { if (fail) throw new InvalidOperationException("Collection was modified"); };
        WorldSnapshotCounters.AddMarriage();
        WithRoster(new[] { Npc("o2_gauge") }, () =>
        {
            sim.WriteHourlySnapshotIfDue(Utc(31, 18, 0)).Should().BeFalse();
            Snapshots().Should().BeEmpty("no row from a gauge that could not be read");
            WorldSnapshotCounters.Marriages.Should().Be(1);
            fail = false;
            sim.WriteHourlySnapshotIfDue(Utc(31, 18, 2)).Should().BeTrue();
            return 0;
        });
        var row = Snapshots().Single();
        (row["npcs_alive"], row["marriages"]).Should().Be((1L, 1L));
    }

    [Fact]
    public void TwoOwners_OnlyTheLockHolderWrites_AndTheOtherKeepsItsCounts()
    {
        Db.TryAcquireWorldSimLock("mud_a").Should().BeTrue();
        var a = new WorldSimService(Db, heartbeatOwnerId: "mud_a");
        var b = new WorldSimService(Db, heartbeatOwnerId: "mud_b");
        WithRoster(new[] { Npc("o2_owner") }, () =>
        {
            Db.TakeOverWorldSimLock("mud_b");   // b takes the lock over; a has lost it
            WorldSnapshotCounters.AddDeath();
            a.WriteHourlySnapshotIfDue(Utc(31, 19, 0)).Should().BeFalse();
            Snapshots().Should().BeEmpty("a process that lost the lock writes nothing");
            WorldSnapshotCounters.Deaths.Should().Be(1, "and keeps its counters");
            b.WriteHourlySnapshotIfDue(Utc(31, 19, 0)).Should().BeTrue();
            return 0;
        });
        Snapshots().Single()["deaths"].Should().Be(1L);
    }

    // ---------- a lethal duel ----------

    private static async Task PvPOutcome(Character player, NPC opponent, bool lethal = true, CombatOutcome outcome = CombatOutcome.Victory)
    {
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        typeof(CombatEngine).GetField("_pvpLethal", INP)!.SetValue(engine, lethal);
        await (Task)typeof(CombatEngine).GetMethod("DeterminePvPOutcome", INP)!
            .Invoke(engine, new object[] { new CombatResult { Player = player, Opponent = opponent, Outcome = outcome } })!;
    }

    private static Character Hunter() =>
        new() { Name1 = "o2_hunter", Name2 = "O2 Hunter", Level = 30, HP = 500, MaxHP = 500, Gold = 0 };

    /// <summary>
    /// The Anchor Road bounty hunt: a lethal PlayerVsPlayer against a roster NPC that ends at 0 HP. One death,
    /// not counted again by the world sim's dead scan or a later world sim kill of the corpse; after the respawn
    /// brings it back, its next death counts, with no scan between.
    /// </summary>
    [Fact]
    public async Task ALethalBountyKill_OfARosterNpc_CountsOnce_AndTheDeadScanDoesNotCountItAgain()
    {
        var target = Npc("o2_bounty_mark", hp: 0);
        var sim = new WorldSimulator();
        var active = NPCSpawnSystem.Instance.ActiveNPCs;
        var saved = active.ToList();
        var party = GameEngine.Instance.DungeonPartyNPCIds.ToList();
        try
        {
            active.Clear();
            active.Add(target);
            GameEngine.Instance.SetDungeonPartyNPCs(Array.Empty<string>());
            await PvPOutcome(Hunter(), target);
            WorldSnapshotCounters.Deaths.Should().Be(1);
            sim.TrackDeadNPCsForRespawn();
            sim.TrackDeadNPCsForRespawn();
            WorldSnapshotCounters.Deaths.Should().Be(1, "the dead scan counts no death");
            sim.MarkNPCDead(target, 0f, "A Killer", "Inn");
            WorldSnapshotCounters.Deaths.Should().Be(1, "the same death seen by a second path counts once");

            Respawn(sim, target);
            target.IsAlive.Should().BeTrue();
            target.HP = 0;
            await PvPOutcome(Hunter(), target);
            WorldSnapshotCounters.Deaths.Should().Be(2, "a new death after the NPC lived again counts");
        }
        finally
        {
            active.Clear();
            active.AddRange(saved);
            GameEngine.Instance.SetDungeonPartyNPCs(party);
        }
    }

    [Fact]
    public async Task ASparedOrNonlethalDuel_OrAnOpponentOutsideTheRoster_CountsNoDeath()
    {
        var spared = Npc("o2_spared", hp: 1);
        var duelled = Npc("o2_duelled", hp: 0);
        var stranger = Npc("o2_stranger", hp: 0);
        var active = NPCSpawnSystem.Instance.ActiveNPCs;
        var saved = active.ToList();
        try
        {
            active.Clear();
            active.Add(spared);
            active.Add(duelled);
            await PvPOutcome(Hunter(), spared, outcome: CombatOutcome.OpponentSpared);
            await PvPOutcome(Hunter(), duelled, lethal: false);
            await PvPOutcome(Hunter(), stranger);
            WorldSnapshotCounters.Deaths.Should().Be(0);
        }
        finally
        {
            active.Clear();
            active.AddRange(saved);
        }
    }

    // ---------- the death mark is reset where the NPC is brought back ----------

    private static void Respawn(WorldSimulator sim, NPC npc)
    {
        var timers = (Dictionary<string, int>)typeof(WorldSimulator).GetField("deadNPCRespawnTimers", INP)!.GetValue(sim)!;
        timers[npc.Id ?? npc.Name] = 1;
        typeof(WorldSimulator).GetMethod("ProcessNPCRespawns", INP)!.Invoke(sim, null);
    }

    private static T InRoster<T>(IEnumerable<NPC> roster, Func<T> body)
    {
        var active = NPCSpawnSystem.Instance.ActiveNPCs;
        var saved = active.ToList();
        var party = GameEngine.Instance.DungeonPartyNPCIds.ToList();
        try
        {
            active.Clear();
            active.AddRange(roster);
            GameEngine.Instance.SetDungeonPartyNPCs(Array.Empty<string>());
            return body();
        }
        finally
        {
            active.Clear();
            active.AddRange(saved);
            GameEngine.Instance.SetDungeonPartyNPCs(party);
        }
    }

    [Fact]
    public void ARespawn_ThenADeathInTheSameTickBeforeAnyScan_CountsTwo()
    {
        var npc = Npc("o2_twice");
        var sim = new WorldSimulator();
        InRoster(new[] { npc }, () =>
        {
            sim.MarkNPCDead(npc, 0f, "A Killer", "Inn");
            Respawn(sim, npc);
            npc.IsDead.Should().BeFalse();
            sim.MarkNPCDead(npc, 0f, "A Killer", "Inn");
            return 0;
        });
        WorldSnapshotCounters.Deaths.Should().Be(2);
    }

    [Fact]
    public async Task ATeamCornerResurrection_ThenADeath_CountsTwo()
    {
        var npc = TeamCornerRig.Npc("o2_tc_res", "Resa Mark", "O2 Crew", level: 3);
        var sim = new WorldSimulator();
        var active = NPCSpawnSystem.Instance.ActiveNPCs;
        active.Add(npc);
        try
        {
            sim.MarkNPCDead(npc, 0f, "A Killer", "Inn");
            npc.IsDead.Should().BeTrue();
            var hero = TeamCornerRig.Hero(team: "O2 Crew", gold: 10000);
            await new TeamCornerRig(hero, new[] { "resa", "y", "" }).Run("ResurrectTeammate");
            npc.IsDead.Should().BeFalse();
            hero.Gold.Should().Be(7000);
            sim.MarkNPCDead(npc, 0f, "A Killer", "Inn");
        }
        finally { active.Remove(npc); }
        WorldSnapshotCounters.Deaths.Should().Be(2);
    }

    [Fact]
    public void AHomeResurrection_ThenADeath_CountsTwo()
    {
        var npc = Npc("o2_home_res"); npc.Level = 3;
        var hero = new Character { Name1 = "O2 Home Hero", Name2 = "O2 Home Hero", Level = 20, HP = 300, MaxHP = 300, Gold = 10000 };
        var sim = new WorldSimulator();
        var romance = RomanceTracker.Instance;
        var spouses = romance.Spouses.ToList();
        var lovers = romance.CurrentLovers.ToList();
        try
        {
            romance.Spouses.Clear(); romance.CurrentLovers.Clear();
            romance.Spouses.Add(new Spouse { NPCId = npc.ID!, NPCName = npc.Name2 });
            InRoster(new[] { npc }, () =>
            {
                sim.MarkNPCDead(npc, 0f, "A Killer", "Inn");
                var home = new HomeLocation();
                var term = new TerminalEmulator(new LineStream(new[] { "1", "", "" }), new MemoryStream());
                typeof(BaseLocation).GetField("terminal", INP)!.SetValue(home, term);
                typeof(BaseLocation).GetField("currentPlayer", INP)!.SetValue(home, hero);
                ((Task)typeof(HomeLocation).GetMethod("ResurrectAlly", INP)!.Invoke(home, null)!).GetAwaiter().GetResult();
                npc.IsDead.Should().BeFalse();
                hero.Gold.Should().Be(7000);
                sim.MarkNPCDead(npc, 0f, "A Killer", "Inn");
                return 0;
            });
        }
        finally
        {
            romance.Spouses.Clear(); romance.Spouses.AddRange(spouses);
            romance.CurrentLovers.Clear(); romance.CurrentLovers.AddRange(lovers);
        }
        WorldSnapshotCounters.Deaths.Should().Be(2);
    }

    [Fact]
    public void AScanBetweenTheCountAndTheDeadFlag_DoesNotAllowASecondCount()
    {
        // an execution or a dark magic kill counts before it sets IsDead; the dead scan may run in between
        var npc = Npc("o2_between");
        var sim = new WorldSimulator();
        InRoster(new[] { npc }, () =>
        {
            WorldSnapshotCounters.CountNpcDeath(npc).Should().BeTrue();
            npc.IsAlive.Should().BeTrue("the flag is not set yet");
            sim.TrackDeadNPCsForRespawn();
            WorldSnapshotCounters.CountNpcDeath(npc).Should().BeFalse("the same death, seen again");
            npc.IsDead = true;
            return 0;
        });
        WorldSnapshotCounters.Deaths.Should().Be(1);
    }

    [Fact]
    public void ThreadsCountingTheSameDeathAtOnce_CountIt_Once()
    {
        const int Deaths = 2000, Threads = 8;
        var npcs = Enumerable.Range(0, Deaths).Select(i => Npc($"o2_race_{i}")).ToArray();
        using var barrier = new System.Threading.Barrier(Threads);
        var workers = Enumerable.Range(0, Threads).Select(_ => new System.Threading.Thread(() =>
        {
            foreach (var n in npcs)
            {
                barrier.SignalAndWait();
                WorldSnapshotCounters.CountNpcDeath(n);
            }
        })).ToList();
        workers.ForEach(t => t.Start());
        workers.ForEach(t => t.Join());
        WorldSnapshotCounters.Deaths.Should().Be(Deaths);
    }

    // ---------- the hooks inside interactive flows ----------

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "web"))))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test must run inside the repository");
        return dir!.FullName;
    }

    /// <summary>
    /// The death and wedding sites inside terminal flows (the dark magic kill, an execution, a street murder, the
    /// royal wedding, the dialogue proposal) cannot be driven here; each must call its counter on the line next to
    /// the flag it flips.
    /// </summary>
    [Theory]
    [InlineData("Scripts/Locations/MagicShopLocation.cs", "WorldSnapshotCounters.CountNpcDeath(targetNPC)", 1, "targetNPC.IsDead = true;")]
    [InlineData("Scripts/Locations/CastleLocation.cs", "WorldSnapshotCounters.CountNpcDeath(npc)", 1, "npc.IsDead = true;")]
    [InlineData("Scripts/Systems/StreetEncounterSystem.cs", "WorldSnapshotCounters.CountNpcDeath(realNpc)", 1, "realNpc.IsDead = true;")]
    [InlineData("Scripts/Locations/CastleLocation.cs", "WorldSnapshotCounters.AddMarriage()", -2, "candidate.SpouseName = currentPlayer.Name2;")]
    [InlineData("Scripts/Systems/VisualNovelDialogueSystem.cs", "WorldSnapshotCounters.AddMarriage()", -2, "npc.SpouseName = player.Name2;")]
    public void AnInteractiveSite_CallsItsCounter_NextToItsFlag(string file, string call, int offset, string flag)
    {
        var lines = File.ReadAllLines(Path.Combine(RepoRoot(), file));
        var at = lines.Select((l, i) => (l, i)).Where(x => x.l.Contains(call) && !x.l.TrimStart().StartsWith("//")).Select(x => x.i).ToList();
        at.Should().ContainSingle($"{call} is called once in {file}");
        lines[at[0] + offset].Should().Contain(flag, $"the counter sits next to {flag}");
    }
}
