using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.0 Temple gods piece 5b: a bless or chastise of a follower who is not online, when the
/// database cannot be read or written, is a failure (FollowerSaveUpdate.Failed, and Failed on the
/// outcome), told apart from a refusal (no save, or not a follower). No deed is spent either way;
/// the god is told the heavens did not answer (pantheon.deed_no_answer).
/// </summary>
[Collection("SharedGameSingletons")]
public class GodDeedOfflineFailure1115Tests : IDisposable
{
    private const string God = "Korvessa";
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-deed-fail-{Guid.NewGuid():N}.db");
    private SqlSaveBackend? _db;
    private SqlSaveBackend Db => _db ??= new SqlSaveBackend(_path);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    private static string Body(string file, string signature) => GodMiracles1115Tests.Body(file, signature);

    private Task Save(string key, string god = God) =>
        Db.WriteGameData(key, new SaveGameData
        {
            Version = GameConfig.SaveVersion,
            Player = new PlayerData { Name1 = key, Name2 = key, Level = 5, WorshippedGod = god, GodFavor = 30, GodFavorGod = god, GodFavorSchema = GameConfig.GodFavorSchemaCurrent },
            StorySystems = new StorySystemsData { PlayerGods = new Dictionary<string, string>() }
        });

    private Task<FollowerSaveUpdate<BlessOutcome?>> Bless(string key) =>
        Db.UpdateFollowerSaveOffline<BlessOutcome?>(key, (p, gods) =>
        {
            var o = ImmortalDeedSystem.BlessSaved(p, gods, God);
            return (!o.Refused, o);
        });

    private Task<FollowerSaveUpdate<ChastiseOutcome?>> Chastise(string key) =>
        Db.UpdateFollowerSaveOffline<ChastiseOutcome?>(key, (p, gods) =>
        {
            var o = ImmortalDeedSystem.ChastiseSaved(p, gods, God);
            return (!o.Refused, o);
        });

    private void Sql(string sql)
    {
        using var conn = new SqliteConnection($"Data Source={_path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private string PlayerJson(string key)
    {
        using var conn = new SqliteConnection($"Data Source={_path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT player_data FROM players WHERE username = @k;";
        cmd.Parameters.AddWithValue("@k", key);
        return (string)cmd.ExecuteScalar()!;
    }

    // ---------------- The backend ----------------

    [Fact]
    public async Task Success_IsNotAFailure()
    {
        await Save("acct_ok");
        var r = await Bless("acct_ok");
        r.Failed.Should().BeFalse();
        r.Result!.Value.Refused.Should().BeFalse();
        r.Result.Value.FavorGained.Should().Be(2);
    }

    [Fact]
    public async Task NoSave_IsARefusal_NotAFailure()
    {
        await Save("acct_other");  // the database works
        var r = await Bless("nobody_here");
        r.Failed.Should().BeFalse();
        r.Result.Should().BeNull();
    }

    [Fact]
    public async Task AnotherGodsFollower_IsARefusal_NotAFailure()
    {
        await Save("acct_else", "Solarius");
        var r = await Chastise("acct_else");
        r.Failed.Should().BeFalse();
        r.Result!.Value.Refused.Should().BeTrue();
    }

    [Fact]
    public async Task ADatabaseThatCannotBeRead_IsAFailure()
    {
        await Save("acct_gone");
        SqliteConnection.ClearAllPools();
        Sql("DROP TABLE players;");
        var b = await Bless("acct_gone");
        b.Failed.Should().BeTrue();
        b.Result.Should().BeNull();
        var c = await Chastise("acct_gone");
        c.Failed.Should().BeTrue();
        c.Result.Should().BeNull();
    }

    [Fact]
    public async Task AFailedWrite_IsAFailure_AndWritesNothing()
    {
        await Save("acct_ro");
        string before = PlayerJson("acct_ro");
        SqliteConnection.ClearAllPools();
        Sql("CREATE TRIGGER no_update BEFORE UPDATE ON players BEGIN SELECT RAISE(ABORT, 'refused'); END;");
        var r = await Bless("acct_ro");
        r.Failed.Should().BeTrue("the change ran but could not be written");
        r.Result.Should().BeNull();
        PlayerJson("acct_ro").Should().Be(before);
    }

    // ---------------- The outcomes ----------------

    [Fact]
    public void Outcomes_AreNotFailedUnlessSaid()
    {
        new BlessOutcome(true, 0, 0, 0f, 0).Failed.Should().BeFalse();
        new ChastiseOutcome(true, 0, 0).Failed.Should().BeFalse();
        var failed = new BlessOutcome(true, 0, 0, 0f, 0) with { Failed = true };
        failed.Refused.Should().BeTrue("a failure spends no deed either");
    }

    // ---------------- Pantheon ----------------

    [Theory]
    [InlineData("private async Task<BlessOutcome> ApplyBlessToPlayer(")]
    [InlineData("private async Task<ChastiseOutcome> ApplyChastiseToPlayer(")]
    public void Callers_TellAFailureFromARefusal(string signature)
    {
        string body = Body("Scripts/Locations/PantheonLocation.cs", signature);
        int failed = body.IndexOf("if (saved.Failed) return refused with { Failed = true };", StringComparison.Ordinal);
        int refused = body.IndexOf("if (saved.Result is not { } s || s.Outcome.Refused) return refused;", StringComparison.Ordinal);
        failed.Should().BeGreaterThan(0);
        refused.Should().BeGreaterThan(failed);
    }

    [Theory]
    [InlineData("private async Task DeedBlessFollower(")]
    [InlineData("private async Task DeedChastiseFollower(")]
    public void Deeds_ShowTheNoAnswerLine_AndSpendNoDeed(string signature)
    {
        string body = Body("Scripts/Locations/PantheonLocation.cs", signature);
        string line = "terminal.WriteLine(outcome.Failed ? Loc.Get(\"pantheon.deed_no_answer\") : Loc.Get(\"pantheon.not_your_follower\", target.Name), \"gray\");";
        int check = body.IndexOf("if (outcome.Refused)", StringComparison.Ordinal);
        int shown = body.IndexOf(line, StringComparison.Ordinal);
        int spend = body.IndexOf("currentPlayer.DeedsLeft--;", StringComparison.Ordinal);
        check.Should().BeGreaterThan(0);
        shown.Should().BeGreaterThan(check);
        spend.Should().BeGreaterThan(shown);
        body.Substring(shown, spend - shown).Should().Contain("return;");
    }

    [Fact]
    public void Loc_TheNoAnswerLine_IsInAllFiveLanguages()
    {
        GodBlessChastise1115Tests.KeysInAllLanguages(new[] { "pantheon.deed_no_answer" });
    }
}
