using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.10: teams founded before the save-key fix recorded the founder's lowercased display name as
/// their leader key, which matches no account for an alt or a married founder; a dying NPC member's
/// bequest was queued under it and then deleted by the orphan sweep. The sweep now first repairs
/// such keys, for teams and for already-queued bequests, where exactly one player has that display
/// name, and leaves anything else as it is.
/// </summary>
[Collection("SharedGameSingletons")]
public class LeaderKeyRepairTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-lkr-{Guid.NewGuid():N}.db");
    private readonly SqlSaveBackend _db;

    public LeaderKeyRepairTests() { _db = new SqlSaveBackend(_path); }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    private void Exec(string sql)
    {
        using var conn = new SqliteConnection($"Data Source={_path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private string? Scalar(string sql)
    {
        using var conn = new SqliteConnection($"Data Source={_path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar()?.ToString();
    }

    private string? Leader(string team) => Scalar($"SELECT created_by FROM player_teams WHERE team_name = '{team}'");

    private static string OnTeam(string team) => "{\"player\":{\"team\":\"" + team + "\"}}";

    [Fact]
    public async Task OnlyAKeyWhoseOnePlayerIsStillOnTheTeam_IsRepaired_AndASecondRunChangesNothing()
    {
        Exec("INSERT INTO players (username, display_name, player_data) VALUES " +
             $"('rage', 'Rage', '{OnTeam("Right Already")}'), " +
             $"('kaela', 'Kaela Stormborn', '{OnTeam("Married Founder")}'), " +
             $"('twin_a', 'Twin', '{OnTeam("Two Twins")}'), ('twin_b', 'Twin', '{OnTeam("Two Twins")}'), " +
             $"('stranger', 'Raven', '{OnTeam("Somewhere Else")}'), " +
             $"('elodie', 'Élodie', '{OnTeam("Accents")}');");
        Exec("INSERT INTO player_teams (team_name, password_hash, created_by) VALUES " +
             "('Right Already', 'x', 'rage'), ('Married Founder', 'x', 'kaela stormborn'), ('Two Twins', 'x', 'twin'), " +
             "('Long Gone', 'x', 'ghost'), ('Name Taken Since', 'x', 'raven'), ('Accents', 'x', 'élodie');");
        _db.QueueInheritance("kaela stormborn", "Aldric", "{\"name\":\"Aldric's sword\"}").Should().BeTrue();
        _db.QueueInheritance("ghost", "Mira", "{\"name\":\"Mira's ring\"}").Should().BeTrue();
        _db.QueueInheritance("élodie", "Bram", "{\"name\":\"Bram's axe\"}").Should().BeTrue();

        for (int run = 1; run <= 2; run++)
        {
            await _db.PruneOrphanedPlayerData();

            Leader("Right Already").Should().Be("rage", "a key that already matches an account is untouched");
            Leader("Married Founder").Should().Be("kaela", "the one player with that display name, still on the team");
            Leader("Two Twins").Should().Be("twin", "two players share the name, so it is left alone");
            Leader("Long Gone").Should().Be("ghost", "nobody has the name, so it is left alone");
            Leader("Name Taken Since").Should().Be("raven", "someone else uses the name now but is not on the team: no proof, no repair");
            Leader("Accents").Should().Be("elodie", "É is lowered the way the key was written, which SQLite's lower() does not do");
            _db.GetPendingInheritance("kaela").Should().ContainSingle($"run {run}: the queued bequest follows its repaired key instead of being swept");
            _db.GetPendingInheritance("elodie").Should().ContainSingle($"run {run}");
            Scalar("SELECT COUNT(*) FROM pending_inheritance WHERE player_username = 'ghost'").Should().Be("0", "an unknown key is still swept, as before");
        }
    }
}
