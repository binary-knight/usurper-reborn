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
/// canon gods and player-gods. Single-player reads the current character; online reads every saved
/// character's god and Favor from player_data when the standing is asked for, and sums it per god.
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
        _db.DeleteGameData("acct_c", bypassArchive: true);

        _db.GetGodStandings()["Solarius"].Should().Be(new GodStanding("Solarius", 30, 1));
    }

    private void InsertRaw(string username, string playerDataJson)
    {
        using var conn = new SqliteConnection($"Data Source={_path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO players (username, display_name, player_data) VALUES (@u, @u, @d);";
        cmd.Parameters.AddWithValue("@u", username);
        cmd.Parameters.AddWithValue("@d", playerDataJson);
        cmd.ExecuteNonQuery();
    }

    private object? Scalar(string sql)
    {
        using var conn = new SqliteConnection($"Data Source={_path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    [Fact]
    public void Online_SavedFavorOutOfRange_CountsClamped()
    {
        InsertRaw("acct_z", "{\"player\":{\"name2\":\"Zed\",\"worshippedGod\":\"Arcanus\",\"godFavor\":500,\"godFavorGod\":\"Arcanus\",\"godFavorSchema\":1}}");
        InsertRaw("acct_y", "{\"player\":{\"name2\":\"Yan\",\"worshippedGod\":\"Arcanus\",\"godFavor\":-40,\"godFavorGod\":\"Arcanus\",\"godFavorSchema\":1}}");
        _db.GetGodStandings()["Arcanus"].Should().Be(new GodStanding("Arcanus", GameConfig.GodFavorMax, 2));
    }

    // ---------------- Characters never saved since the upgrade ----------------

    [Fact]
    public void Online_LegacyRowWithNoFavorFields_CountsLegacyStart()
    {
        // A character last saved before Favor: no godFavor, godFavorGod or godFavorSchema at all
        InsertRaw("acct_old", "{\"player\":{\"name2\":\"Olwen\",\"level\":9,\"worshippedGod\":\"Zephyrine\"}}");
        InsertRaw("acct_oldc", "{\"player\":{\"name2\":\"Orrin\",\"level\":9},\"storySystems\":{\"playerGods\":{\"Orrin\":\"Solarius\",\"Olwen\":\"Mortis\"}}}");
        var s = _db.GetGodStandings();
        s["Zephyrine"].Should().Be(new GodStanding("Zephyrine", GameConfig.GodFavorLegacyStart, 1));
        s["Solarius"].Should().Be(new GodStanding("Solarius", GameConfig.GodFavorLegacyStart, 1), "the canon god under the character's own key");
        s.ContainsKey("Mortis").Should().BeFalse("another character's entry in the save is not this character's god");
    }

    [Fact]
    public void Online_AnImmortalIsNotAFollower()
    {
        InsertRaw("acct_god", "{\"player\":{\"name2\":\"Ivor\",\"isImmortal\":true,\"worshippedGod\":\"Zephyrine\",\"godFavorSchema\":1}}");
        InsertRaw("acct_mortal", "{\"player\":{\"name2\":\"Mael\",\"isImmortal\":false,\"worshippedGod\":\"Zephyrine\",\"godFavorSchema\":1}}");
        _db.GetGodStandings()["Zephyrine"].Followers.Should().Be(1, "a player-god is not counted as anyone's follower");
    }

    [Fact]
    public void Online_OldGodFavorTableIsDropped()
    {
        using (var conn = new SqliteConnection($"Data Source={_path}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE IF NOT EXISTS god_favor (username TEXT PRIMARY KEY, god_name TEXT NOT NULL, favor INTEGER NOT NULL DEFAULT 0);";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
        _ = new SqlSaveBackend(_path);
        Convert.ToInt64(Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'god_favor';")).Should().Be(0);
    }

    // ---------------- Writes that are not saves ----------------

    [Fact]
    public async Task Online_OfflineRecruitOfAPlayerGodFollower_MovesTheFollower()
    {
        await Save("acct_a", Player("Arla", "Zephyrine", 40, "Zephyrine"));
        await _db.SetPlayerWorshippedGod("acct_a", "Korvessa");
        var s = _db.GetGodStandings();
        s.ContainsKey("Zephyrine").Should().BeFalse();
        s["Korvessa"].Should().Be(new GodStanding("Korvessa", 0, 1), "a new god starts at Favor 0");
    }

    [Fact]
    public async Task Online_OfflineRecruitOfACanonFollower_MovesTheFollower()
    {
        await Save("acct_a", Player("Arla", "", 30, "Solarius"), new Dictionary<string, string> { ["ARLA"] = "Solarius", ["Brom"] = "Mortis" });
        await _db.SetPlayerWorshippedGod("acct_a", "Korvessa");
        var s = _db.GetGodStandings();
        s.ContainsKey("Solarius").Should().BeFalse("the recruit ends the canon worship");
        s["Korvessa"].Followers.Should().Be(1);
        Scalar("SELECT json_extract(player_data, '$.storySystems.playerGods.Brom') FROM players WHERE username = 'acct_a';")
            .Should().Be("Mortis", "another character's entry is left alone");
    }

    [Fact]
    public async Task Online_TempleLeavingAPlayerGod_WithoutASave_Counts()
    {
        await Save("acct_a", Player("Arla", "Zephyrine", 40, "Zephyrine"));
        await _db.SetPlayerWorshippedGod("acct_a", "");
        _db.GetGodStandings().Should().BeEmpty();
    }

    // ---------------- Temple and Pantheon agree ----------------

    [Fact]
    public async Task Online_PantheonBelieverCount_IsTheTempleFollowerCount()
    {
        await Save("acct_a", Player("Arla", "Zephyrine", 40, "Zephyrine"));
        await Save("acct_b", Player("Brom", "zephyrine", 10, "zephyrine"));
        // A row where the player-god field and the canon entry disagree: the canon god wins in both
        InsertRaw("acct_c", "{\"player\":{\"name2\":\"Cyra\",\"worshippedGod\":\"Zephyrine\",\"godFavorSchema\":1},\"storySystems\":{\"playerGods\":{\"Cyra\":\"Amara\"}}}");
        InsertRaw("acct_d", "{\"player\":{\"name2\":\"Dova\",\"worshippedGod\":\"Zephyrine\"}}");
        await _db.BanPlayer("acct_d", "test");

        var s = _db.GetGodStandings();
        s["Zephyrine"].Followers.Should().Be(2);
        (await _db.CountPlayerBelievers("Zephyrine")).Should().Be(s["Zephyrine"].Followers);
        (await _db.CountPlayerBelievers("Amara")).Should().Be(s["Amara"].Followers);
        (await _db.CountPlayerBelievers("Nobody")).Should().Be(0);
    }

    [Fact]
    public void PantheonCountBelievers_UsesTheTempleCounts()
    {
        var body = SourceBody("PantheonLocation.cs", "public static int CountBelievers(string divineName)", "private async Task<List<BelieverInfo>> GetBelieverListAsync");
        body.Should().Contain("GodRegistry.CountNpcFollowers(divineName)");
        body.Should().Contain("backend.CountPlayerBelievers(divineName)");
    }

    [Fact]
    public void PantheonOnlineRecruit_GoesThroughTheGodRegistry()
    {
        var body = SourceBody("PantheonLocation.cs", "private async Task ApplyRecruitToPlayer", "#endregion");
        body.Should().Contain("GodRegistry.SetWorshippedGod(player, godName)");
        body.Should().NotContain("player.WorshippedGod = godName");
    }

    // ---------------- One standings read per listing, not one per god ----------------

    [Fact]
    public void PantheonRankingLoop_ReadsStandingsOnce_NotPerGod()
    {
        var body = SourceBody("PantheonLocation.cs", "private async Task ShowImmortalRankings()", "#region News");
        int readIdx = body.IndexOf("ReadStandingsOnce()", StringComparison.Ordinal);
        int loopIdx = body.IndexOf("foreach (var god in gods.OrderByDescending", StringComparison.Ordinal);
        readIdx.Should().BeGreaterThan(-1, "the ranking reads standings once");
        loopIdx.Should().BeGreaterThan(-1);
        readIdx.Should().BeLessThan(loopIdx, "the standings read happens once, before the per-god loop");

        var loopBody = body.Substring(loopIdx);
        loopBody.Should().NotContain("GetGodStandings", "no full standings read inside the per-god loop");
        loopBody.Should().NotContain("backend.CountPlayerBelievers", "no per-god database read inside the loop");
        loopBody.Should().Contain("CountBelievers(god.DivineName, standings)", "each god looks itself up in the standings read once");
    }

    [Fact]
    public void TempleImmortalsLoop_ReadsStandingsOnce_NotPerGod()
    {
        var body = SourceBody("TempleLocation.cs", "private async Task<List<ImmortalGodInfo>> GetImmortalGodsAsync()", "private async Task WorshipImmortalGod()");
        int readIdx = body.IndexOf("backend.GetGodStandings()", StringComparison.Ordinal);
        int loopIdx = body.IndexOf("foreach (var god in immortals)", StringComparison.Ordinal);
        readIdx.Should().BeGreaterThan(-1, "the immortals listing reads standings once");
        loopIdx.Should().BeGreaterThan(-1);
        readIdx.Should().BeLessThan(loopIdx, "the standings read happens once, before the per-god loop");

        var loopBody = body.Substring(loopIdx);
        loopBody.Should().NotContain("GetGodStandings", "no full standings read inside the per-god loop");
        loopBody.Should().NotContain("backend.CountPlayerBelievers", "no per-god database read inside the loop");
        loopBody.Should().Contain("PantheonLocation.CountBelievers(god.DivineName, standings)", "each god looks itself up in the standings read once");
    }

    [Fact]
    public async Task PantheonCountBelievers_TwoArgOverload_MatchesTheSingleCallCount()
    {
        await Save("acct_a", Player("Arla", "Zephyrine", 40, "Zephyrine"));
        await Save("acct_b", Player("Brom", "zephyrine", 10, "zephyrine"));
        var standings = _db.GetGodStandings();
        int npcCount = GodRegistry.CountNpcFollowers("Zephyrine");

        PantheonLocation.CountBelievers("Zephyrine", standings).Should().Be(npcCount + standings["Zephyrine"].Followers,
            "the precomputed lookup gives the same number the per-call backend query would");
        PantheonLocation.CountBelievers("Nobody", standings).Should().Be(GodRegistry.CountNpcFollowers("Nobody"));
        PantheonLocation.CountBelievers("Zephyrine", (Dictionary<string, GodStanding>)null).Should().Be(npcCount,
            "null standings (single player, or the DB unavailable) counts NPCs only");
    }

    private static string SourceBody(string file, string startMarker, string endMarker)
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj")))
            dir = dir.Parent;
        var src = File.ReadAllText(Path.Combine(dir!.FullName, "Scripts", "Locations", file));
        int start = src.IndexOf(startMarker, StringComparison.Ordinal);
        int end = src.IndexOf(endMarker, start, StringComparison.Ordinal);
        return src.Substring(start, end - start);
    }

    // ---------------- Single-player ----------------

    [Fact]
    public async Task SinglePlayer_GetStandingsAsync_IsThePlayersOwn()
    {
        var c = new Character { Name1 = "GsSolo2", Name2 = "GsSolo2", AI = CharacterAI.Human };
        var gods = UsurperRemake.GodSystemSingleton.Instance;
        GodRegistry.SetWorshippedGod(c, "Arcanus", gods);
        FavorSystem.Change(c, 19, gods);
        try
        {
            (await GodRegistry.GetStandingsAsync(c)).Should().ContainSingle()
                .Which.Value.Should().Be(new GodStanding("Arcanus", 19, 1));
        }
        finally { GodRegistry.SetWorshippedGod(c, "", gods); }
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
