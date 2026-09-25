using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
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

/// <summary>v1.1.14 (T1): two players joining a team with one free slot at once cannot take it to six.</summary>
[Collection("SharedGameSingletons")]
public class TeamJoinSlotClaim1114Tests
{
    private static long Members(string path, string team) => Convert.ToInt64(TeamCornerRig.Scalar(path,
        $"SELECT COUNT(*) FROM players WHERE json_extract(player_data, '$.player.team') = '{team}';"));

    [Fact]
    public async Task TwoBackends_ClaimingTheLastSlotAtOnce_OnlyOneGetsIt()
    {
        string path = Path.Combine(Path.GetTempPath(), $"usurper-t1-{Guid.NewGuid():N}.db");
        var one = new SqlSaveBackend(path);
        var two = new SqlSaveBackend(path);   // a second backend on the same database, as a second process has
        try
        {
            foreach (var k in new[] { "memba", "membb", "membc", "membd" }) TeamCornerRig.PlayerRow(path, k, "Full House");
            TeamCornerRig.PlayerRow(path, "joinx", "");
            TeamCornerRig.PlayerRow(path, "joiny", "");
            for (int round = 0; round < 20; round++)
            {
                TeamCornerRig.Exec(path, "UPDATE players SET player_data = json_set(player_data, '$.player.team', '') WHERE username IN ('joinx', 'joiny');");
                using var start = new ManualResetEventSlim(false);
                var a = Task.Run(async () => { start.Wait(); return await one.TryClaimTeamSlot("Full House", "joinx", 0, 5); });
                var b = Task.Run(async () => { start.Wait(); return await two.TryClaimTeamSlot("Full House", "joiny", 0, 5); });
                start.Set();
                var won = await Task.WhenAll(a, b);
                won.Count(w => w).Should().Be(1, $"one free slot, round {round}");
                Members(path, "Full House").Should().Be(5, $"never six, round {round}");
            }
            // NPC slots count too: four players and one NPC leave no slot
            TeamCornerRig.Exec(path, "UPDATE players SET player_data = json_set(player_data, '$.player.team', '') WHERE username IN ('joinx', 'joiny');");
            (await one.TryClaimTeamSlot("Full House", "joinx", 1, 5)).Should().BeFalse();
            Members(path, "Full House").Should().Be(4);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public async Task ASecondJoin_BeforeTheFirstJoinersSaveLands_IsRefused()
    {
        // the first join's full save has not landed (in a test it never does); the claim wrote the team already
        var npc = TeamCornerRig.Npc("tc_t1_npc", "Crowd Npc", "Crowded");
        NPCSpawnSystem.Instance.ActiveNPCs.Add(npc);
        var saved = UsurperRemake.Server.SessionContext.Current;
        try
        {
            await TeamCornerRig.Online(async (db, path) =>
            {
                (await db.CreatePlayerTeam("Crowded", SqlSaveBackend.HashTeamPassword("pw"), "crowda")).Should().BeTrue();
                foreach (var k in new[] { "crowda", "crowdb", "crowdc" }) TeamCornerRig.PlayerRow(path, k, "Crowded");
                TeamCornerRig.PlayerRow(path, "firstj", "");
                TeamCornerRig.PlayerRow(path, "secondj", "");

                async Task<(Character hero, string shown)> Join(string key)
                {
                    UsurperRemake.Server.SessionContext.Current = new UsurperRemake.Server.SessionContext
                        { InputStream = Stream.Null, OutputStream = Stream.Null, Username = key, CharacterKey = key };
                    var hero = TeamCornerRig.Hero(name: char.ToUpper(key[0]) + key.Substring(1));
                    string shown = await new TeamCornerRig(hero, new[] { "crowded", "pw", "" }).Run("JoinTeam");
                    return (hero, shown);
                }

                var (first, firstShown) = await Join("firstj");
                first.Team.Should().Be("Crowded");
                firstShown.Should().Contain(Loc.Get("team.joined_team", "Crowded"));
                var (second, secondShown) = await Join("secondj");
                secondShown.Should().Contain(Loc.Get("team.join_team_full", "Crowded", 5));
                second.Team.Should().BeEmpty();
                Members(path, "Crowded").Should().Be(4, "three players and the first joiner, with the NPC five");
                WorldSimulator.UnregisterPlayerTeam("Crowded");
            });
        }
        finally
        {
            UsurperRemake.Server.SessionContext.Current = saved;
            NPCSpawnSystem.Instance.ActiveNPCs.Remove(npc);
        }
    }
}

/// <summary>v1.1.14 (D2): a monster's stun on a teammate follows the hold rules the player has had since 1.1.13.</summary>
[Collection("SharedGameSingletons")]
public class TeammateHoldRules1114Tests
{
    private const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

    private static (CombatEngine engine, Character mate, Func<bool> blow) OgreOnTeammate()
    {
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, new LowRandom());   // every roll passes
        var mate = new Character { Name1 = "Mate", Name2 = "Mate", Class = CharacterClass.Warrior, Level = 6, HP = 100_000, MaxHP = 100_000 };
        var ogre = new Monster { Name = "Ogre", Level = 5, HP = 200, MaxHP = 200, Strength = 20 };
        ogre.SpecialAbilities = new List<string> { "CrushingBlow" };   // stuns 2 rounds, 25%
        var m = typeof(CombatEngine).GetMethod("MonsterAttacksCompanion", F)!;
        var result = new CombatResult { Player = new Character { Name1 = "Lead", Name2 = "Lead", HP = 100, MaxHP = 100 } };
        bool Blow()
        {
            bool held = mate.HasStatus(StatusEffect.Stunned);
            ((Task)m.Invoke(engine, new object?[] { ogre, mate, result, null })!).GetAwaiter().GetResult();
            return !held && mate.HasStatus(StatusEffect.Stunned);
        }
        return (engine, mate, Blow);
    }

    private static void EndRound(CombatEngine engine, Character c) { c.ProcessStatusEffects(); engine.TickPvPControl(c); }

    [Fact]
    public void Ogre_CannotReStunATeammate_WhileHeld_OrInTheImmunityWindow()
    {
        var (engine, mate, blow) = OgreOnTeammate();
        blow().Should().BeTrue("the first stun lands");
        EndRound(engine, mate);
        mate.HasStatus(StatusEffect.Stunned).Should().BeTrue();
        mate.ActiveStatuses[StatusEffect.Stunned] = 1;
        blow();
        mate.ActiveStatuses[StatusEffect.Stunned].Should().Be(1, "a new stun does not reset the clock while one holds");
        EndRound(engine, mate);                       // the stun runs out; immunity starts
        mate.HasStatus(StatusEffect.Stunned).Should().BeFalse();
        for (int r = 0; r < GameConfig.StunImmunityRoundsAfterRecovery; r++)
        {
            blow().Should().BeFalse($"immune round {r + 1}");
            EndRound(engine, mate);
        }
        blow().Should().BeTrue("the immunity has run out");
    }

    [Fact]
    public void EveryTeammateTurn_TicksTheHoldState()
    {
        // comments stripped, so a commented-out call fails the check as a deleted one does
        var src = string.Join("\n", File.ReadAllText(Path.Combine(Leftovers1114BTests.RepoRoot(), "Scripts", "Systems", "CombatEngine.cs"))
            .Split('\n').Select(l => { int c = l.IndexOf("//", StringComparison.Ordinal); return c >= 0 ? l.Substring(0, c) : l; }));
        foreach (var method in new[] { "private async Task ProcessTeammateAction(", "private async Task ProcessTeammateActionMultiMonster(", "private async Task ProcessGroupedPlayerTurn(" })
        {
            int start = src.IndexOf(method, StringComparison.Ordinal);
            start.Should().BeGreaterThan(0, method);
            int tick = src.IndexOf("teammate.ProcessStatusEffects()", start, StringComparison.Ordinal);
            tick.Should().BeGreaterThan(start, method);
            src.Substring(tick, 400).Should().Contain("TickPvPControl(teammate);", method);
        }
    }
}

/// <summary>v1.1.14 (D8): a test can seed the combat engine's RNG; play keeps Random.Shared.</summary>
[Collection("SharedGameSingletons")]
public class CombatSeed1114Tests
{
    /// <summary>A poisoned player swings at a dummy that cannot hurt them until the input runs out.</summary>
    internal static async Task<string> PoisonedFight(int? seed)
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(Enumerable.Repeat("A", 8)), output);
        var engine = new CombatEngine(term);
        if (seed.HasValue) engine.SeedRandomForTests(seed.Value);
        var p = new Character
        {
            Name1 = "seedy", Name2 = "Seedy", Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 10,
            HP = 5000, MaxHP = 5000, BaseMaxHP = 5000, Strength = 80, BaseStrength = 80, Defence = 40, BaseDefence = 40,
            Dexterity = 30, BaseDexterity = 30, Agility = 25, BaseAgility = 25, Constitution = 30, BaseConstitution = 30,
            Stamina = 100, CombatSpeed = CombatSpeed.Instant, Poison = 40,
        };
        var dummy = new Monster { Name = "Straw Dummy", Level = 1, HP = 5_000_000, MaxHP = 5_000_000, Strength = 0, Defence = 0, Experience = 1, Gold = 0 };
        try { await engine.PlayerVsMonsters(p, new List<Monster> { dummy }, offerMonkEncounter: false); }
        catch (IOException) { }   // the scripted input ran out
        term.StreamWriterInternal?.Flush();
        return System.Text.RegularExpressions.Regex.Replace(System.Text.Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "")
               + $"\nHP left {p.HP}, dummy HP left {dummy.HP}";
    }

    [Fact]
    public async Task TheSameSeed_GivesTheSameFight()
    {
        string first = await PoisonedFight(7);
        first.Should().Contain("Straw Dummy");
        for (int i = 0; i < 5; i++)
            (await PoisonedFight(7)).Should().Be(first, "every roll of the fight, the poison ticks included, comes from the seeded RNG");
    }

    [Fact]
    public void AnUnseededEngine_UsesTheSharedRandom()
    {
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        typeof(CombatEngine).GetField("random", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(engine).Should().BeSameAs(Random.Shared, "play is not seeded");
    }

    [Fact]
    public void TheEngine_RollsOnlyThroughItsOwnRng()
    {
        // comments stripped; a Random.Shared roll inside the engine would escape a test seed
        var lines = File.ReadAllLines(Path.Combine(Leftovers1114BTests.RepoRoot(), "Scripts", "Systems", "CombatEngine.cs"))
            .Select(l => { int c = l.IndexOf("//", StringComparison.Ordinal); return c >= 0 ? l.Substring(0, c) : l; })
            .Where(l => l.Contains("Random.Shared") || l.Contains("new Random(")).Select(l => l.Trim()).ToList();
        lines.Should().BeEquivalentTo(new[] { "private Random random = Random.Shared;", "internal void SeedRandomForTests(int seed) => random = new Random(seed);" });
    }
}
