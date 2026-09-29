using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.0 Temple gods piece 4: desecration. A desecrated altar gives a follower of a dark domain
/// (Shadow, Death, Chaos; canon or player-god) +3 Favor under the shared daily deed cap, stays a
/// -10 taboo for Earth, and lowers that god's standing by 5 until the next weekly reset (a stored
/// penalty, online in SQL, single-player in the save); followers' own Favor is untouched and the
/// standing never goes below 0.
/// </summary>
[Collection("SharedGameSingletons")]
public class GodDesecration1115Tests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-desecration-{Guid.NewGuid():N}.db");
    private readonly SqlSaveBackend _db;

    public GodDesecration1115Tests() { _db = new SqlSaveBackend(_path); }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    private static Character Hero(string name) =>
        new Character { Name1 = name, Name2 = name, AI = CharacterAI.Human, Level = 5, HP = 50, MaxHP = 50, Class = CharacterClass.Warrior };

    private static (Character c, GodSystem gods) Worshipper(string name, string god, int favor)
    {
        var gods = new GodSystem();
        var c = Hero(name);
        GodRegistry.SetWorshippedGod(c, god, gods).Should().BeTrue();
        c.GodFavor = favor;
        FavorSystem.Bind(c, gods);
        c.GodFavor = favor;
        return (c, gods);
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "UsurperReborn.sln"))) dir = Path.GetDirectoryName(dir);
        return dir!;
    }

    private static string Source(string file) => File.ReadAllText(Path.Combine(RepoRoot(), file));

    private static int Count(string src, string needle) => Regex.Matches(src, Regex.Escape(needle)).Count;

    private Task Save(string key, string name, string canonGod, int favor) =>
        _db.WriteGameData(key, new SaveGameData
        {
            Version = GameConfig.SaveVersion,
            Player = new PlayerData { Name1 = name, Name2 = name, Level = 5, GodFavor = favor, GodFavorGod = canonGod, GodFavorSchema = GameConfig.GodFavorSchemaCurrent },
            StorySystems = new StorySystemsData { PlayerGods = new Dictionary<string, string> { [name] = canonGod } }
        });

    // ---------------- Favor: the dark gods' deed, Earth's taboo ----------------

    [Theory]
    [InlineData("Umbrath")]
    [InlineData("Mortis")]
    [InlineData("Discordia")]
    public void Desecration_GivesADarkGodsFollower_Three(string god)
    {
        GameConfig.GodDeedDesecration.Should().Be(3);
        var (c, gods) = Worshipper("GdsDark" + god, god, 20);
        GodDeedSystem.Apply(c, GodAct.Desecration, gods).Should().Be(3);
        c.GodFavor.Should().Be(23);
        FavorSystem.GainedToday(c, FavorSource.Deed).Should().Be(3, "it counts toward the shared deed cap");
    }

    [Theory]
    [InlineData(GodDomain.Shadow)]
    [InlineData(GodDomain.Death)]
    [InlineData(GodDomain.Chaos)]
    public void Desecration_GivesAPlayerGodsFollower_OfADarkDomain_Three(GodDomain domain)
    {
        var c = Hero("GdsPg" + domain);
        GodRegistry.SetWorshippedGod(c, "Zephyrine").Should().BeTrue();
        try
        {
            c.GodFavor = 20;
            GodBoonSystem.SetPlayerGodBoon(c, "Zephyrine", domain, 50);
            GodDeedSystem.Apply(c, GodAct.Desecration).Should().Be(3);
            c.GodFavor.Should().Be(23);
        }
        finally { GodRegistry.SetWorshippedGod(c, null); }
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(2, 2)]
    [InlineData(3, 1)]
    [InlineData(4, 0)]
    public void Desecration_StaysUnderTheSharedDeedCap(int deedsToday, int gained)
    {
        var (c, gods) = Worshipper("GdsCap" + deedsToday, "Mortis", 20);
        c.GodFavorDayGains[FavorSource.Deed.ToString()] = deedsToday;
        GodDeedSystem.Apply(c, GodAct.Desecration, gods).Should().Be(gained);
        FavorSystem.GainedToday(c, FavorSource.Deed).Should().Be(Math.Min(GameConfig.GodFavorDeedDailyCap, deedsToday + 3));
    }

    [Fact]
    public void Desecration_ThenAnotherDeed_ShareOneCap()
    {
        var (c, gods) = Worshipper("GdsShare", "Umbrath", 20);
        GodDeedSystem.Apply(c, GodAct.Desecration, gods).Should().Be(3);
        GodDeedSystem.Apply(c, GodAct.Theft, gods).Should().Be(1);
        GodDeedSystem.Apply(c, GodAct.StealthKill, gods).Should().Be(0, "the deed cap of 4 is spent");
        c.GodFavor.Should().Be(24);
    }

    [Fact]
    public void Desecration_IsStillEarthsTaboo_OfTen_Once()
    {
        var (c, gods) = Worshipper("GdsEarth", "Terran", 30);
        GodDeedSystem.Worth(GodAct.Desecration, GodDomain.Earth).Should().Be(-10);
        GodDeedSystem.Apply(c, GodAct.Desecration, gods).Should().Be(-10);
        c.GodFavor.Should().Be(20, "one taboo, not doubled");
        FavorSystem.GainedToday(c, FavorSource.Deed).Should().Be(0);
    }

    [Theory]
    [InlineData("Solarius")]
    [InlineData("Valorian")]
    [InlineData("Amara")]
    [InlineData("Judicar")]
    [InlineData("Arcanus")]
    [InlineData("Sylvana")]
    public void Desecration_MeansNothing_ToTheOtherGods(string god)
    {
        var (c, gods) = Worshipper("GdsNone" + god, god, 30);
        GodDeedSystem.Apply(c, GodAct.Desecration, gods).Should().Be(0);
        c.GodFavor.Should().Be(30);
    }

    [Fact]
    public void TheTemple_RecordsDesecrationOnce_AndPenalisesOnce()
    {
        string temple = Source("Scripts/Locations/TempleLocation.cs");
        Count(temple, "GodAct.Desecration").Should().Be(1, "one Record call, no second hook");
        Count(temple, "GodStandingPenalty.RecordDesecration(currentPlayer, god.Name);").Should().Be(1);
        var body = temple.Substring(temple.IndexOf("private async Task PerformEnhancedDesecration(", StringComparison.Ordinal));
        body.IndexOf("GodStandingPenalty.RecordDesecration", StringComparison.Ordinal)
            .Should().BeGreaterThan(body.IndexOf("GodDeedSystem.Record(currentPlayer, GodAct.Desecration, terminal);", StringComparison.Ordinal));
        // Your own god's altar stays refused, as before
        temple.Should().Contain("Loc.Get(\"temple.not_allowed_abuse_own\")");
    }

    // ---------------- Standing penalty: pure rules ----------------

    [Fact]
    public void Penalty_LowersStanding_NeverBelowZero_AndLeavesFollowersAlone()
    {
        var standings = GodRegistry.ComputeStandings(new[] { ("Solarius", 30), ("Solarius", 4), ("Mortis", 3) });
        var result = GodStandingPenalty.Apply(standings, new Dictionary<string, int> { ["solarius"] = 5, ["Mortis"] = 10, ["Amara"] = 5 });
        result["Solarius"].Should().Be(new GodStanding("Solarius", 29, 2));
        result["Mortis"].Should().Be(new GodStanding("Mortis", 0, 1), "never below 0");
        result.ContainsKey("Amara").Should().BeFalse("a god with no standing stays at none");
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(6, 0)]
    [InlineData(7, 1)]
    [InlineData(13, 1)]
    [InlineData(14, 2)]
    public void Week_IsTheGameDayOverSeven(int day, int week)
    {
        GameConfig.GodStandingWeekDays.Should().Be(7);
        GodStandingPenalty.WeekOf(day).Should().Be(week);
    }

    [Fact]
    public void SinglePlayer_PenaltyCounts_ThisWeek_AndLapsesAtTheWeekBoundary()
    {
        var (c, gods) = Worshipper("GdsSp", "Umbrath", 12);
        int week = GodStandingPenalty.CurrentWeek();
        GodStandingPenalty.AddLocal(c, "umbrath", week);
        GodStandingPenalty.AddLocal(c, "Umbrath", week);
        c.GodStandingPenalties.Should().HaveCount(1);
        GodStandingPenalty.LocalPenalties(c, week)["Umbrath"].Should().Be(2 * GameConfig.GodDesecrationStandingPenalty);
        GodRegistry.SinglePlayerStandings(c, gods, Array.Empty<NPC>())["Umbrath"].Standing.Should().Be(2, "12 less 10");
        c.GodFavor.Should().Be(12, "the follower's own Favor is untouched");

        GodStandingPenalty.LocalPenalties(c, week + 1).Should().BeEmpty("it lapses at the next week");
        GodStandingPenalty.AddLocal(c, "Mortis", week + 1);
        c.GodStandingPenaltyWeek.Should().Be(week + 1);
        c.GodStandingPenalties.Keys.Should().BeEquivalentTo(new[] { "Mortis" }, "last week's penalties are dropped");

        GodStandingPenalty.AddLocal(c, "Mortis", week + 1, 100);
        c.GodStandingPenalties["Mortis"].Should().Be(105);
    }

    [Fact]
    public void SinglePlayer_Penalty_NeverTakesStandingBelowZero()
    {
        var (c, gods) = Worshipper("GdsSpFloor", "Discordia", 3);
        GodStandingPenalty.AddLocal(c, "Discordia", GodStandingPenalty.CurrentWeek());
        GodRegistry.SinglePlayerStandings(c, gods, Array.Empty<NPC>())["Discordia"].Standing.Should().Be(0);
    }

    // ---------------- Standing penalty: online (SQL) ----------------

    [Fact]
    public async Task Online_PenaltyIsStored_AndAppliedAtTheStandingRead_ForItsWeekOnly()
    {
        await Save("acct_a", "Aldo", "Solarius", 30);
        await Save("acct_b", "Bree", "Solarius", 20);
        await Save("acct_c", "Cato", "Mortis", 4);
        _db.AddGodStandingPenalty("Solarius", 40, GameConfig.GodDesecrationStandingPenalty);
        _db.AddGodStandingPenalty("Solarius", 40, GameConfig.GodDesecrationStandingPenalty);
        _db.AddGodStandingPenalty("Mortis", 40, GameConfig.GodDesecrationStandingPenalty);

        var s = _db.GetGodStandings(40);
        s["Solarius"].Should().Be(new GodStanding("Solarius", 40, 2), "50 less two desecrations");
        s["Mortis"].Should().Be(new GodStanding("Mortis", 0, 1), "never below 0");

        var next = _db.GetGodStandings(41);
        next["Solarius"].Standing.Should().Be(50, "the penalty lapses at the next week");
        next["Mortis"].Standing.Should().Be(4);
    }

    [Fact]
    public void Online_AWriteInANewWeek_KeepsTheWeekBefore_AndDropsOlderWeeks()
    {
        int Rows(int week)
        {
            using var conn = new SqliteConnection($"Data Source={_path}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM god_standing_penalties WHERE week = @w;";
            cmd.Parameters.AddWithValue("@w", week);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
        _db.AddGodStandingPenalty("Amara", 10, 5);
        _db.AddGodStandingPenalty("Amara", 11, 5);
        Rows(10).Should().Be(1, "the ending week's penalties are kept for the new week's weekly god pick");
        _db.AddGodStandingPenalty("Amara", 12, 5);
        Rows(10).Should().Be(0);
        Rows(11).Should().Be(1);
    }

    [Fact]
    public void Online_TheStandingRead_IsOneRead_AndTheResetClearsThePenalties()
    {
        string sql = Source("Scripts/Systems/SqlSaveBackend.cs");
        int start = sql.IndexOf("public Dictionary<string, GodStanding> GetGodStandings(int week)", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0);
        string body = sql.Substring(start, sql.IndexOf("public void AddGodStandingPenalty(", start, StringComparison.Ordinal) - start);
        Count(body, "OpenConnection()").Should().Be(1, "the penalties are read on the standings' own connection");
        body.Should().Contain("GodStandingPenalty.Apply(GodRegistry.AddNpcFollowers(GodRegistry.ComputeStandings(entries), npcCounts), penalties)",
            "piece 6: NPC followers join before the penalties");
        sql.Should().Contain("DELETE FROM god_standing_penalties;");
    }

    // ---------------- Saves ----------------

    [Fact]
    public void SinglePlayerPenalties_RoundTripThroughTheSave()
    {
        var c = Hero("GdsSave");
        GodStandingPenalty.AddLocal(c, "Terran", 9);
        var method = typeof(SaveSystem).GetMethod("SerializePlayer", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var data = (PlayerData)method.Invoke(SaveSystem.Instance, new object[] { c })!;
        data.GodStandingPenaltyWeek.Should().Be(9);
        data.GodStandingPenalties.Should().Equal(new Dictionary<string, int> { ["Terran"] = GameConfig.GodDesecrationStandingPenalty });
        var back = JsonSerializer.Deserialize<PlayerData>(JsonSerializer.Serialize(data))!;
        var restored = MenuKeysNeedEnterPref1115Tests.Restore(back);
        restored.GodStandingPenaltyWeek.Should().Be(9);
        GodStandingPenalty.LocalPenalties(restored, 9)["terran"].Should().Be(GameConfig.GodDesecrationStandingPenalty);

        var old = MenuKeysNeedEnterPref1115Tests.Restore(JsonSerializer.Deserialize<PlayerData>("{}")!);
        old.GodStandingPenaltyWeek.Should().Be(-1);
        old.GodStandingPenalties.Should().BeEmpty();
        Source("Scripts/Editor/PlayerSaveEditor.cs").Should().Contain("Clear the desecration standing penalties");
    }
}
