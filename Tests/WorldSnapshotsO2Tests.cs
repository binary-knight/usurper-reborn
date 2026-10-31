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

    private static DateTime Utc(int day, int hour, int minute = 0) => new(2026, 10, day, hour, minute, 0, DateTimeKind.Utc);

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

    private static WorldSnapshotRow Row(long births = 0) => new(10, 2, 4, 3, 500, 1, births, 0, 0);

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
        Db.WriteWorldSnapshot(Utc(31, 14, 5), Row(births: 7)).Should().BeTrue();
        Db.WriteWorldSnapshot(Utc(31, 14, 50), Row(births: 99)).Should().BeFalse();
        var rows = Snapshots();
        rows.Should().HaveCount(1);
        rows[0]["births"].Should().Be(7L, "INSERT OR IGNORE keeps the first row of the hour");
    }

    [Fact]
    public void ARestartInTheSameHour_AddsNoRow_AndKeepsTheCountsForTheNextRow()
    {
        WithRoster(new[] { Npc("o2_restart") }, () =>
        {
            new WorldSimService(Db).WriteHourlySnapshotIfDue(Utc(31, 14, 5)).Should().BeTrue();
            WorldSnapshotCounters.AddBirth();
            var restarted = new WorldSimService(Db);
            restarted.WriteHourlySnapshotIfDue(Utc(31, 14, 30)).Should().BeFalse();
            WorldSnapshotCounters.Births.Should().Be(1, "a row not written gives its counts back");
            restarted.WriteHourlySnapshotIfDue(Utc(31, 15, 0)).Should().BeTrue();
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
        Db.WriteWorldSnapshot(now, Row()).Should().BeTrue();
        Snapshots().Select(r => (string)r["taken_at"]!).Should().Equal(
            Key(now.AddDays(-90)), Key(now.AddDays(-30)), Key(now));
    }

    [Fact]
    public void TheWrite_StillPrunes_WhenTheHourAlreadyHasItsRow()
    {
        var now = Utc(31, 12, 20);
        _ = Db;
        Db.WriteWorldSnapshot(now, Row()).Should().BeTrue();
        PutRow(SqlSaveBackend.WorldSnapshotHourKey(now.AddDays(-100)));
        Db.WriteWorldSnapshot(now.AddMinutes(10), Row()).Should().BeFalse();
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
    [InlineData("Scripts/Locations/MagicShopLocation.cs", "WorldSnapshotCounters.AddDeath()", 1, "targetNPC.IsDead = true;")]
    [InlineData("Scripts/Locations/CastleLocation.cs", "WorldSnapshotCounters.AddDeath()", 1, "npc.IsDead = true;")]
    [InlineData("Scripts/Systems/StreetEncounterSystem.cs", "WorldSnapshotCounters.AddDeath()", 1, "realNpc.IsDead = true;")]
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
