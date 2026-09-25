using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>v1.1.14: leftover fixes (localization, names, teams, dungeon events, combat holds, combat seeding).</summary>
public class Leftovers1114BTests
{
    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        return dir!.FullName;
    }

    private static Dictionary<string, string> Lang(string lang) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(RepoRoot(), "Localization", lang + ".json")))!;

    [Theory]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("it")]
    [InlineData("hu")]
    public void ShadowCloak_AndCloakOfShadows_HaveDistinctNames(string lang)
    {
        var en = Lang("en");
        en["item.shadow_cloak"].Should().NotBe(en["item.cloak_of_shadows"]);
        var d = Lang(lang);
        d["item.shadow_cloak"].Should().NotBe(d["item.cloak_of_shadows"], "two different items must not share a name");
    }

    [Fact]
    public void French_ShadowCloak_IsSingular_CloakOfShadows_IsPlural()
    {
        var fr = Lang("fr");
        fr["item.shadow_cloak"].Should().Be("Cape d'Ombre");
        fr["item.cloak_of_shadows"].Should().Be("Cape des Ombres");
    }

    // ---------- N3: surnames from names with a legacy numeral ----------

    private static string? FamilySurname(string name) =>
        (string?)typeof(UsurperRemake.Systems.FamilySystem)
            .GetMethod("ExtractSurname", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { name });

    [Theory]
    [InlineData("Nimble Nick II", null)]
    [InlineData("Nimble Nick", null)]
    [InlineData("Scarface Sam IV", null)]
    [InlineData("Halvar Copperfield II", "Copperfield")]
    [InlineData("Borin Hammerhand", "Hammerhand")]
    public void FamilySurname_StripsTheNumeral_BeforeTheAliasCheck(string name, string? expected) =>
        FamilySurname(name).Should().Be(expected);

    [Theory]
    [InlineData("Ansel II VI", "")]
    [InlineData("Ansel VI", "")]
    [InlineData("Wren Copperfield II", "Copperfield")]
    [InlineData("Wren Copperfield", "Copperfield")]
    [InlineData("VI", "")]
    [InlineData("Wren", "")]
    public void MarriageSurname_NeverOffersARomanNumeral(string name, string expected) =>
        MarriageSurnameHelper.ExtractSurname(name).Should().Be(expected);
}

/// <summary>v1.1.14 (T5): a new team does not inherit the wars and sieges of a removed team of its name.</summary>
[Collection("SharedGameSingletons")]
public class TeamRecordsSinceCreation1114Tests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-t5-{Guid.NewGuid():N}.db");
    private readonly UsurperRemake.Systems.SqlSaveBackend _db;

    public TeamRecordsSinceCreation1114Tests() { _db = new UsurperRemake.Systems.SqlSaveBackend(_path); }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    private void Exec(string sql) => TeamCornerRig.Exec(_path, sql);

    private void War(string challenger, string defender, string startedAt) =>
        Exec($"INSERT INTO team_wars (challenger_team, defender_team, status, challenger_wins, defender_wins, gold_wagered, started_at) " +
             $"VALUES ('{challenger}', '{defender}', 'challenger_won', 2, 1, 0, {startedAt});");

    private Task<UsurperRemake.Systems.TeamWarInfo?> Recent(string mine, string enemy) =>
        TeamCornerLocation.RecentWarAgainst(_db, mine, enemy, DateTime.UtcNow.AddHours(-24));

    [Fact]
    public async Task ANewTeam_DoesNotInherit_TheWarsAndSiegeOfARemovedTeamOfItsName()
    {
        Exec("INSERT INTO player_teams (team_name, password_hash, created_by, created_at) VALUES ('Owls', 'x', 'owner', datetime('now', '-2 days'));");
        Exec("INSERT INTO player_teams (team_name, password_hash, created_by, created_at) VALUES ('Ravens', 'x', 'first', datetime('now', '-2 days'));");
        War("Ravens", "Owls", "datetime('now', '-10 minutes')");
        Exec("INSERT INTO castle_sieges (team_name, total_guards, result, started_at) VALUES ('Ravens', 5, 'failed', datetime('now', '-10 minutes'));");

        // the old team holds its records
        (await _db.GetTeamWarHistory("Ravens")).Should().HaveCount(1);
        _db.CanTeamSiege("Ravens").Should().BeFalse();
        (await Recent("Owls", "Ravens")).Should().NotBeNull();

        // the team is removed (DeleteEmptyTeam leaves team_wars and castle_sieges) and a new one takes the name
        Exec("DELETE FROM player_teams WHERE team_name = 'Ravens';");
        (await _db.CreatePlayerTeam("Ravens", "hash", "second")).Should().BeTrue();

        (await _db.GetTeamWarHistory("Ravens")).Should().BeEmpty("the new team fought no war");
        _db.CanTeamSiege("Ravens").Should().BeTrue("the new team laid no siege");
        (await Recent("Ravens", "Owls")).Should().BeNull();
        (await Recent("Owls", "Ravens")).Should().BeNull("the old team's war does not hold the cooldown against the new team either");
        (await _db.GetTeamWarHistory("Owls")).Should().ContainSingle("the other team keeps its history")
            .Which.ChallengerTeam.Should().Be("Ravens");

        // a war the new team fights counts as before
        War("Ravens", "Owls", "datetime('now')");
        (await _db.GetTeamWarHistory("Ravens")).Should().HaveCount(1);
        (await Recent("Owls", "Ravens")).Should().NotBeNull();
    }

    [Fact]
    public async Task AnNpcTeam_WithNoPlayerTeamRow_KeepsEveryRecord()
    {
        War("Wolves", "Bears", "datetime('now', '-3 days')");
        Exec("INSERT INTO castle_sieges (team_name, total_guards, result, started_at) VALUES ('Wolves', 5, 'failed', datetime('now', '-1 hours'));");
        (await _db.GetTeamWarHistory("Wolves")).Should().HaveCount(1);
        _db.CanTeamSiege("Wolves").Should().BeFalse();
    }
}

/// <summary>v1.1.14 (D1): a chest or shrine picked by the random room event is spent by the player's choice.</summary>
[Collection("SharedGameSingletons")]
public class RandomRoomEventChoice1114Tests
{
    private const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

    private static DungeonLocation Dungeon(Random rnd, params string[] lines)
    {
        var term = new TerminalEmulator(new LineStream(lines), new MemoryStream());
        var hero = new Character { Name1 = "rre", Name2 = "Rre", Class = CharacterClass.Warrior, Level = 8, HP = 300, MaxHP = 300, Gold = 1000,
                                   AI = CharacterAI.Human, Dexterity = 60, Wisdom = 60 };
        var d = new DungeonLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(d, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(d, hero);
        typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.SetValue(d, 5);
        typeof(DungeonLocation).GetField("dungeonRandom", F)!.SetValue(d, rnd);
        return d;
    }

    private static Task HandleRoomEvent(DungeonLocation d, DungeonRoom room) =>
        (Task)typeof(DungeonLocation).GetMethod("HandleRoomEvent", F)!.Invoke(d, new object[] { room })!;

    // Trap has no case of its own in RunRoomEvent, so it goes to RandomDungeonEvent; roll 0 is a chest, 2 a shrine
    private static DungeonRoom Room() => new DungeonRoom { Id = "r1", HasEvent = true, EventType = DungeonEventType.Trap };

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ARandomChestOrShrine_IsNotSpent_WhenTheConnectionDropsAtThePrompt(int roll)
    {
        var room = Room();
        Func<Task> act = () => HandleRoomEvent(Dungeon(new ScriptRandom(roll)), room);   // no lines: the stream ends at the prompt
        await act.Should().ThrowAsync<IOException>();
        room.EventCompleted.Should().BeFalse("no choice was made");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ARandomChestOrShrine_IsSpent_ByAValidChoice(int roll)
    {
        var room = Room();
        await HandleRoomEvent(Dungeon(new ScriptRandom(roll), "L", "", "", ""), room);
        room.EventCompleted.Should().BeTrue("leaving is a valid choice");
    }
}
