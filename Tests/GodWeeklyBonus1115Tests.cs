using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.0 Temple gods piece 6: the strongest god of the week. It is picked once per week at the
/// weekly reset (online the world week, recorded in world_state; single-player the game day's week,
/// recorded in the save), ties go to the name first in ordinal order ignoring case, and the
/// followers of that god get +5% XP in TeamHQBonus.ApplyXP while the week lasts.
/// </summary>
[Collection("SharedGameSingletons")]
public class GodWeeklyBonus1115Tests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-weekgod-{Guid.NewGuid():N}.db");
    private readonly SqlSaveBackend _db;
    private readonly Func<bool> _isOnline = WeeklyGodSystem.IsOnline;
    private readonly Func<SqlSaveBackend?> _backend = WeeklyGodSystem.Backend;
    private readonly Func<DateTime> _now = WeeklyGodSystem.UtcNow;
    private readonly Func<int> _day = WeeklyGodSystem.GameDay;
    private readonly List<NPC> _rosterBefore;

    public GodWeeklyBonus1115Tests()
    {
        _db = new SqlSaveBackend(_path);
        WeeklyGodSystem.ResetForTests();
        _rosterBefore = NPCSpawnSystem.Instance.ActiveNPCs.ToList();
        NPCSpawnSystem.Instance.ActiveNPCs.Clear();
    }

    public void Dispose()
    {
        WeeklyGodSystem.IsOnline = _isOnline;
        WeeklyGodSystem.Backend = _backend;
        WeeklyGodSystem.UtcNow = _now;
        WeeklyGodSystem.GameDay = _day;
        WeeklyGodSystem.ResetForTests();
        var roster = NPCSpawnSystem.Instance.ActiveNPCs;
        roster.Clear();
        roster.AddRange(_rosterBefore);
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    private static string Source(string folder, string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Scripts"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "Scripts", folder, file));
    }

    private Task Save(string key, string name, string canonGod, int favor) =>
        _db.WriteGameData(key, new SaveGameData
        {
            Version = GameConfig.SaveVersion,
            Player = new PlayerData { Name1 = name, Name2 = name, Level = 5, GodFavor = favor, GodFavorGod = canonGod, GodFavorSchema = GameConfig.GodFavorSchemaCurrent },
            StorySystems = new StorySystemsData { PlayerGods = new Dictionary<string, string> { [name] = canonGod } }
        });

    private static (Character C, GodSystem Gods) Worshipper(string name, string god, int favor)
    {
        var gods = new GodSystem();
        var c = new Character { Name1 = name, Name2 = name, AI = CharacterAI.Human, Level = 5 };
        GodRegistry.SetWorshippedGod(c, god, gods);
        FavorSystem.Change(c, favor, gods);
        return (c, gods);
    }

    // ---------------- The pick and its tie rule ----------------

    [Fact]
    public void TopGod_IsTheHighestStanding_TiesToTheFirstNameIgnoringCase()
    {
        var s = new Dictionary<string, GodStanding>(StringComparer.OrdinalIgnoreCase)
        {
            ["Umbrath"] = new("Umbrath", 40, 2),
            ["amara"] = new("amara", 40, 1),
            ["Zephyrine"] = new("Zephyrine", 40, 3),
            ["Mortis"] = new("Mortis", 12, 1),
        };
        WeeklyGodSystem.TopGod(s).Should().Be("amara", "a tie goes to the name first in ordinal order ignoring case");
        s["Zephyrine"] = new("Zephyrine", 41, 0);
        WeeklyGodSystem.TopGod(s).Should().Be("Zephyrine", "a player-god can win");
        WeeklyGodSystem.TopGod(new Dictionary<string, GodStanding> { ["Mortis"] = new("Mortis", 0, 3) }).Should().Be("", "no standing, no god");
        WeeklyGodSystem.TopGod(null).Should().Be("");
    }

    // ---------------- Online: once per world week ----------------

    [Fact]
    public async Task Online_ThePickIsRecordedOncePerWeek_WhateverTheStandingDoesAfter()
    {
        await Save("acct_wa", "WgAna", "Amara", 30);
        await Save("acct_wb", "WgBo", "Mortis", 20);
        var first = WeeklyGodSystem.OnlinePick(_db, 90);
        first.Should().Be(new WeeklyGodPick(90, "Amara", 30));

        await Save("acct_wc", "WgCy", "Mortis", 40);   // Mortis now leads
        WeeklyGodSystem.ResetForTests();               // another session or process, same week
        WeeklyGodSystem.OnlinePick(_db, 90).Should().Be(first, "the week is decided once");
        _db.GetWeeklyGod().Should().Be(first);

        WeeklyGodSystem.ResetForTests();
        WeeklyGodSystem.OnlinePick(_db, 91).Should().Be(new WeeklyGodPick(91, "Mortis", 60), "the next week picks again");
    }

    [Fact]
    public void Online_TheRecordOnlyMovesToANewerWeek()
    {
        _db.RecordWeeklyGod(new WeeklyGodPick(50, "Amara", 10));
        _db.RecordWeeklyGod(new WeeklyGodPick(50, "Mortis", 99));
        _db.GetWeeklyGod().Should().Be(new WeeklyGodPick(50, "Amara", 10), "a second write for the same week is refused");
        _db.RecordWeeklyGod(new WeeklyGodPick(49, "Terran", 5));
        _db.GetWeeklyGod().Should().Be(new WeeklyGodPick(50, "Amara", 10), "an older week never replaces it");
        _db.RecordWeeklyGod(new WeeklyGodPick(51, "Terran", 5));
        _db.GetWeeklyGod().Should().Be(new WeeklyGodPick(51, "Terran", 5));
    }

    [Fact]
    public async Task Online_TheWeekIsTheWorldWeek_NotTheSessionsGameDay()
    {
        await Save("acct_wd", "WgDee", "Terran", 25);
        WeeklyGodSystem.IsOnline = () => true;
        WeeklyGodSystem.Backend = () => _db;
        var t = new DateTime(2026, 9, 20, 23, 30, 0, DateTimeKind.Utc);   // a Sunday, 7:30 PM Eastern
        WeeklyGodSystem.UtcNow = () => t;
        WeeklyGodSystem.GameDay = () => 3;
        var a = WeeklyGodSystem.Current(null);
        WeeklyGodSystem.ResetForTests();
        WeeklyGodSystem.GameDay = () => 700;   // another session's save is on another game day
        var b = WeeklyGodSystem.Current(null);
        a.Should().Be(b);
        a!.Value.Week.Should().Be(DailySystemManager.WorldWeekAt(t));
        a.Value.God.Should().Be("Terran");
    }

    [Fact]
    public async Task Online_ThePickCountsTheEndingWeeksDesecrations()
    {
        await Save("acct_we", "WgEd", "Amara", 30);
        await Save("acct_wf", "WgFi", "Umbrath", 28);
        _db.AddGodStandingPenalty("Amara", 69, 5);
        WeeklyGodSystem.OnlinePick(_db, 70)!.Value.God.Should().Be("Umbrath", "Amara stood at 25 when the week ended");
    }

    // ---------------- Single-player: once per game week, in the save ----------------

    [Fact]
    public void SinglePlayer_ThePickIsRecordedInTheSave_OncePerWeek()
    {
        var (c, gods) = Worshipper("WgSolo", "Sylvana", 20);
        var npcs = new List<NPC>();
        WeeklyGodSystem.LocalPick(c, 12, gods, npcs).God.Should().Be("Sylvana");
        c.WeeklyGodWeek.Should().Be(12);
        c.WeeklyGod.Should().Be("Sylvana");

        var mortisNpcs = Enumerable.Range(0, 6).Select(i => new NPC { Name1 = $"WgM{i}", Name2 = $"WgM{i}", WorshippedGod = "Mortis" }).ToList();
        WeeklyGodSystem.LocalPick(c, 12, gods, mortisNpcs).God.Should().Be("Sylvana", "the week is decided once");
        WeeklyGodSystem.LocalPick(c, 13, gods, mortisNpcs).God.Should().Be("Mortis", "30 from six NPC followers beats 20");
    }

    [Fact]
    public void SinglePlayer_TheWeeklyPickIsSaved_ThroughTheFiveSites()
    {
        var c = new Character { Name1 = "WgSave", Name2 = "WgSave", AI = CharacterAI.Human, WeeklyGodWeek = 33, WeeklyGod = "Judicar" };
        var method = typeof(SaveSystem).GetMethod("SerializePlayer", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var data = (PlayerData)method.Invoke(SaveSystem.Instance, new object[] { c })!;
        data.WeeklyGodWeek.Should().Be(33);
        data.WeeklyGod.Should().Be("Judicar");
        var back = JsonSerializer.Deserialize<PlayerData>(JsonSerializer.Serialize(data))!;
        back.WeeklyGodWeek.Should().Be(33);
        new PlayerData().WeeklyGodWeek.Should().Be(-1, "an old save has no pick yet");
        Source("Core", "GameEngine.cs").Should().Contain("WeeklyGodWeek = Math.Max(-1, playerData.WeeklyGodWeek),")
            .And.Contain("WeeklyGod = playerData.WeeklyGod ?? \"\",");
        Source("Editor", "PlayerSaveEditor.cs").Should().Contain("p.WeeklyGodWeek");
    }

    // ---------------- The XP bonus ----------------

    [Fact]
    public void Xp_TheWinnersFollowersGetFivePercent_OthersNone()
    {
        WeeklyGodSystem.IsOnline = () => false;
        WeeklyGodSystem.GameDay = () => 7 * 20;
        var (follower, _) = Worshipper("WgXpF", "Valorian", 30);
        follower.WeeklyGodWeek = 20;
        follower.WeeklyGod = "Valorian";
        var gods = UsurperRemake.GodSystemSingleton.Instance;
        GodRegistry.SetWorshippedGod(follower, "Valorian", gods);
        try
        {
            GameConfig.GodWeeklyXpBonusPct.Should().Be(5);
            WeeklyGodSystem.XpMultiplier(follower).Should().Be(1.05);
            TeamHQBonus.ApplyXP(follower, 1000).Should().Be(1050);

            GodRegistry.SetWorshippedGod(follower, "Amara", gods);   // at the time XP is awarded
            follower.WeeklyGodWeek = 20;
            follower.WeeklyGod = "Valorian";
            WeeklyGodSystem.XpMultiplier(follower).Should().Be(1.0);
            TeamHQBonus.ApplyXP(follower, 1000).Should().Be(1000);

            var npc = new NPC { Name1 = "WgXpNpc", Name2 = "WgXpNpc", WorshippedGod = "Valorian" };
            WeeklyGodSystem.XpMultiplier(npc).Should().Be(1.0, "only players get the bonus");
        }
        finally { GodRegistry.SetWorshippedGod(follower, "", gods); }
    }

    [Fact]
    public void Xp_TheBonusIsAppliedOnce_InTheOneXpPath()
    {
        var hq = Source("Systems", "TeamHQBonus.cs");
        int start = hq.IndexOf("public static long ApplyXP(Character c, long xp)", StringComparison.Ordinal);
        var body = hq.Substring(start, hq.IndexOf("public static long ApplyPotionHeal", start, StringComparison.Ordinal) - start);
        body.Should().Contain("WeeklyGodSystem.XpMultiplier(c)");
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Scripts"))) dir = dir.Parent;
        int uses = Directory.GetFiles(Path.Combine(dir!.FullName, "Scripts"), "*.cs", SearchOption.AllDirectories)
            .Sum(p => File.ReadAllText(p).Split("WeeklyGodSystem.XpMultiplier(").Length - 1);
        uses.Should().Be(2, "ApplyXP multiplies it once, and the XP tally names it");
        Source("Systems", "CombatEngine.cs").Should().Contain("if (WeeklyGodSystem.XpMultiplier(player) > 1.0 && !Sources.Contains(\"god_week\")) Sources.Add(\"god_week\");");
    }

    // ---------------- The Temple ranking ----------------

    [Fact]
    public void TempleRanking_ShowsThisWeeksGodAndTheBonus()
    {
        var src = Source("Locations", "TempleLocation.cs");
        int start = src.IndexOf("private async Task DisplayGodRanking()", StringComparison.Ordinal);
        var body = src.Substring(start, src.IndexOf("private async Task DisplayHolyNews()", start, StringComparison.Ordinal) - start);
        body.Should().Contain("WeeklyGodSystem.Current(currentPlayer)");
        body.Should().Contain("Loc.Get(\"temple.week_god\", week.God, GameConfig.GodWeeklyXpBonusPct)");
        body.Should().Contain("Loc.Get(\"temple.week_god_none\")");
        body.Should().Contain("Loc.Get(\"temple.week_god_yours\", week.God, GameConfig.GodWeeklyXpBonusPct)");
    }

    [Fact]
    public void WeeklyGodLines_AreInAllFiveLanguages()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Localization"))) dir = dir.Parent;
        foreach (var lang in new[] { "en", "es", "fr", "hu", "it" })
        {
            var keys = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "Localization", lang + ".json"))).RootElement;
            foreach (var k in new[] { "temple.week_god", "temple.week_god_none", "temple.week_god_yours", "combat.xp_mod.god_week" })
                keys.TryGetProperty(k, out _).Should().BeTrue($"{lang} has {k}");
        }
    }
}
