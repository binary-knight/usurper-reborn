using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.11: Team HQ upgrade levels were read at login and after the buyer's own upgrade only, so a
/// player who joined a team had none until the next login, a teammate's upgrade reached nobody else,
/// and a player who left kept the bonus for the rest of the session. They are read by team from the
/// database (TeamHQBonus.RefreshLevels) and count only while that is still the player's team.
/// </summary>
[Collection("SharedGameSingletons")]
public class TeamHQBonusTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-hq-{Guid.NewGuid():N}.db");
    private readonly SqlSaveBackend _db;

    public TeamHQBonusTests()
    {
        _db = new SqlSaveBackend(_path);
        Exec("INSERT INTO team_upgrades (team_name, upgrade_type, level) VALUES " +
             "('Iron Wolves', 'armory', 3), ('Iron Wolves', 'barracks', 2), ('Iron Wolves', 'training', 1), ('Iron Wolves', 'infirmary', 4), ('Iron Wolves', 'vault', 5);");
    }

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

    private static Character Member(string team) => new Character { Name1 = "wolf", Name2 = "Wolf", Team = team, Level = 20 };

    [Fact]
    public void TheLevels_AreReadForTheTeam()
    {
        var c = Member("Iron Wolves");
        TeamHQBonus.RefreshLevels(c, _db);
        TeamHQBonus.Armory(c).Should().Be(3);
        TeamHQBonus.Barracks(c).Should().Be(2);
        TeamHQBonus.Training(c).Should().Be(1);
        TeamHQBonus.Infirmary(c).Should().Be(4);
        TeamHQBonus.ApplyAttack(c, 1000).Should().Be(1150);
        TeamHQBonus.ApplyXP(c, 1000).Should().Be(1050);
        TeamHQBonus.ApplyPotionHeal(c, 1000).Should().Be(1400);
        TeamHQBonus.ApplyDefense(c, 1100).Should().Be(1000);
    }

    [Fact]
    public void APlayerWhoLeavesOrChangesTeam_LosesTheBonusAtOnce()
    {
        var c = Member("Iron Wolves");
        TeamHQBonus.RefreshLevels(c, _db);
        c.Team = "";
        TeamHQBonus.Armory(c).Should().Be(0, "leaving the team took the bonus with it");
        c.Team = "Other Team";
        TeamHQBonus.Armory(c).Should().Be(0, "these are another team's levels");
        TeamHQBonus.ApplyAttack(c, 1000).Should().Be(1000);
    }

    [Fact]
    public void ATeammatesUpgrade_IsSeenAtTheNextRead()
    {
        var c = Member("Iron Wolves");
        TeamHQBonus.RefreshLevels(c, _db);
        Exec("UPDATE team_upgrades SET level = 5 WHERE team_name = 'Iron Wolves' AND upgrade_type = 'armory';");
        TeamHQBonus.RefreshLevels(c, _db);
        TeamHQBonus.Armory(c).Should().Be(5);
    }

    [Fact]
    public void ADuelDefenderLoadedFromASave_GetsItsOwnTeamsLevels()
    {
        // A defender built from its save has no levels in memory; its team's are read by name.
        var defender = Member("Iron Wolves");
        defender.HQArmoryLevel.Should().Be(0);
        TeamHQBonus.RefreshLevels(defender, _db);
        TeamHQBonus.Barracks(defender).Should().Be(2);
    }

    [Fact]
    public void NobodyWithoutATeam_AndNoNPC_GetsTheLevels()
    {
        var loner = Member("");
        TeamHQBonus.RefreshLevels(loner, _db);
        TeamHQBonus.Armory(loner).Should().Be(0);

        var npc = new NPC { Name1 = "Hold", Name2 = "Hold", Team = "Iron Wolves" };
        TeamHQBonus.RefreshLevels(npc, _db);
        TeamHQBonus.Armory(npc).Should().Be(0, "an NPC teammate is not the team's player");

        var offline = Member("Iron Wolves");
        TeamHQBonus.RefreshLevels(offline, backend: null);   // no online database in this process
        TeamHQBonus.Armory(offline).Should().Be(0);
    }

    [Fact]
    public void TheLevels_AreNeverInTheSave()
    {
        // They are the team's, read from the database; a save would carry a stale copy.
        typeof(PlayerData).GetMembers(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name.StartsWith("HQ", StringComparison.Ordinal)).Should().BeEmpty();
    }
}
