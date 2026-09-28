using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.0 Temple gods piece 1: god standing is the sum of the followers' Favor, one ranking for
/// canon gods and player-gods. Single-player reads the save; online keeps each save key's god and
/// Favor in god_favor, written with every save, and sums it per god.
/// </summary>
[Collection("SharedGameSingletons")]
public class GodStanding1115Tests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-standing-{Guid.NewGuid():N}.db");
    private readonly SqlSaveBackend _db;

    public GodStanding1115Tests() { _db = new SqlSaveBackend(_path); }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    private static PlayerData Player(string name, string playerGod, int favor, string favorGod, int schema = GameConfig.GodFavorSchemaCurrent) =>
        new PlayerData { Name1 = name, Name2 = name, Level = 5, WorshippedGod = playerGod, GodFavor = favor, GodFavorGod = favorGod, GodFavorSchema = schema };

    private Task Save(string key, PlayerData p, Dictionary<string, string>? canon = null) =>
        _db.WriteGameData(key, new SaveGameData
        {
            Version = GameConfig.SaveVersion,
            Player = p,
            StorySystems = new StorySystemsData { PlayerGods = canon ?? new Dictionary<string, string>() }
        });

    // ---------------- Pure sums ----------------

    [Fact]
    public void ComputeStandings_SumsFavorAndCountsFollowers_PerGod()
    {
        var s = GodRegistry.ComputeStandings(new[]
        {
            ("Solarius", 30), ("solarius", 12), ("Zephyrine", 40), ("", 50), ("Manwe", 90), ("Mortis", 150)
        });
        s.Should().HaveCount(3);
        s["Solarius"].Should().Be(new GodStanding("Solarius", 42, 2));
        s["Zephyrine"].Should().Be(new GodStanding("Zephyrine", 40, 1));
        s["Mortis"].Standing.Should().Be(GameConfig.GodFavorMax, "a Favor out of range counts clamped");
        s.ContainsKey("Manwe").Should().BeFalse();
    }

    [Fact]
    public void StandingEntryFrom_ReadsTheSavedGodAndFavor()
    {
        GodRegistry.StandingEntryFrom(Player("Ana", "", 33, "Amara"), new Dictionary<string, string> { ["ana"] = "amara", ["Other"] = "Mortis" })
            .Should().Be(("Amara", 33), "the canon god under the character's key, any case");
        GodRegistry.StandingEntryFrom(Player("Ben", "Zephyrine", 21, "Zephyrine"), null)
            .Should().Be(("Zephyrine", 21));
        GodRegistry.StandingEntryFrom(Player("Cai", "Zephyrine", 21, "Korvath"), null)
            .Should().Be(("Zephyrine", 0), "Favor for another god does not count");
        GodRegistry.StandingEntryFrom(Player("Dee", "Zephyrine", 0, "", schema: 0), null)
            .Should().Be(("Zephyrine", GameConfig.GodFavorLegacyStart), "a save from before Favor counts what its first load gives");
        GodRegistry.StandingEntryFrom(Player("Eve", "", 50, "Amara"), new Dictionary<string, string> { ["Eve"] = "Manwe" })
            .Should().Be(("", 0));
    }

    [Fact]
    public void SinglePlayerStandings_IsThePlayersOwnGodAndFavor()
    {
        var gods = new GodSystem();
        var c = new Character { Name1 = "GsSolo", Name2 = "GsSolo", AI = CharacterAI.Human };
        GodRegistry.SinglePlayerStandings(c, gods).Should().BeEmpty();
        GodRegistry.SetWorshippedGod(c, "Terran", gods);
        FavorSystem.Change(c, 27, gods);
        GodRegistry.SinglePlayerStandings(c, gods).Should().ContainSingle()
            .Which.Value.Should().Be(new GodStanding("Terran", 27, 1));
    }

    [Fact]
    public void CanonGods_HaveNoInventedBelievers()
    {
        var gods = new GodSystem();
        foreach (var name in GameConfig.CanonGodNames)
        {
            gods.GetGod(name)!.BaselineBelievers.Should().Be(0);
            gods.GetGod(name)!.Believers.Should().Be(0);
        }
    }

    // ---------------- Online storage ----------------

    [Fact]
    public async Task Online_EverySaveRecordsItsGod_AndStandingSumsAcrossSaves()
    {
        await Save("acct_a", Player("Arla", "", 30, "Solarius"), new Dictionary<string, string> { ["Arla"] = "Solarius" });
        await Save("acct_b", Player("Brom", "", 25, "Solarius"), new Dictionary<string, string> { ["Brom"] = "Solarius", ["Arla"] = "Mortis" });
        await Save("acct_c", Player("Cyra", "Zephyrine", 60, "Zephyrine"));
        await Save("acct_d", Player("Dova", "", 0, ""));

        var s = _db.GetGodStandings();
        s["Solarius"].Should().Be(new GodStanding("Solarius", 55, 2), "another character's stale entry in a save is not counted");
        s["Zephyrine"].Should().Be(new GodStanding("Zephyrine", 60, 1));
        s.Should().HaveCount(2);
        s.ContainsKey("Mortis").Should().BeFalse();
    }

    [Fact]
    public async Task Online_ASwitchOrLeavingReplacesTheRow()
    {
        await Save("acct_a", Player("Arla", "", 30, "Solarius"), new Dictionary<string, string> { ["Arla"] = "Solarius" });
        await Save("acct_a", Player("Arla", "Zephyrine", 0, "Zephyrine"));
        var s = _db.GetGodStandings();
        s.ContainsKey("Solarius").Should().BeFalse();
        s["Zephyrine"].Should().Be(new GodStanding("Zephyrine", 0, 1));

        await Save("acct_a", Player("Arla", "", 0, ""));
        _db.GetGodStandings().Should().BeEmpty("no god, no row");
    }

    [Fact]
    public async Task Online_BannedAndDeletedCharactersDoNotCount()
    {
        await Save("acct_a", Player("Arla", "", 30, "Solarius"), new Dictionary<string, string> { ["Arla"] = "Solarius" });
        await Save("acct_b", Player("Brom", "", 25, "Solarius"), new Dictionary<string, string> { ["Brom"] = "Solarius" });
        await Save("acct_c", Player("Cyra", "", 40, "Solarius"), new Dictionary<string, string> { ["Cyra"] = "Solarius" });

        await _db.BanPlayer("acct_b", "test");
        _db.PurgePlayerWorldState("acct_c");

        _db.GetGodStandings()["Solarius"].Should().Be(new GodStanding("Solarius", 30, 1));
    }

    [Fact]
    public void Online_UpsertClampsFavor()
    {
        using (var conn = new SqliteConnection($"Data Source={_path}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO players (username, display_name, player_data) VALUES ('acct_z', 'Zed', '{\"player\":{\"name2\":\"Zed\"}}');";
            cmd.ExecuteNonQuery();
        }
        _db.UpsertGodFavor("acct_z", "Arcanus", 500);
        _db.GetGodStandings()["Arcanus"].Standing.Should().Be(GameConfig.GodFavorMax);
        StoredFavor("acct_z").Should().Be(GameConfig.GodFavorMax, "the stored row itself is clamped, not only the sum");

        _db.UpsertGodFavor("acct_z", "Arcanus", -40);
        StoredFavor("acct_z").Should().Be(GameConfig.GodFavorMin);
    }

    private long StoredFavor(string username)
    {
        using var conn = new SqliteConnection($"Data Source={_path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT favor FROM god_favor WHERE username = LOWER(@u);";
        cmd.Parameters.AddWithValue("@u", username);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    // ---------------- Temple ranking (source) ----------------

    [Fact]
    public void TempleRanking_UsesTheUnifiedListAndStanding()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj")))
            dir = dir.Parent;
        var src = File.ReadAllText(Path.Combine(dir!.FullName, "Scripts", "Locations", "TempleLocation.cs"));
        int start = src.IndexOf("private async Task DisplayGodRanking()", StringComparison.Ordinal);
        int end = src.IndexOf("private async Task DisplayHolyNews()", StringComparison.Ordinal);
        var body = src.Substring(start, end - start);
        body.Should().Contain("GodRegistry.AllGods(godNames)");
        body.Should().Contain("await GodRegistry.GetStandingsAsync(currentPlayer)");
        body.Should().Contain("OrderByDescending(r => r.Standing)");
        body.Should().NotContain(".Believers", "no invented believer numbers in the ranking");
    }
}
