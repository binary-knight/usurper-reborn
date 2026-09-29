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
/// 1.2.0 Temple gods piece 5b: a player-god's bless of a follower who is not online gives at most
/// GodBlessFavorDailyCap Favor each world day. While the follower's own daily reset is still due,
/// the saved ImmortalBlessing count is kept per world day (ImmortalDeedSystem.OfflineBlessWorldDayKey)
/// and starts over when the world day changes; the follower's LastDailyResetBoundary is not changed.
/// </summary>
[Collection("SharedGameSingletons")]
public class GodBlessOffline1115Tests : IDisposable
{
    private const string God = "Korvessa";
    private static readonly string Key = nameof(FavorSource.ImmortalBlessing);

    // 15:00 UTC is 11:00 Eastern, well away from the 7 PM Eastern reset.
    private static readonly DateTime DayN = new DateTime(2026, 9, 10, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime DayN1 = DayN.AddHours(24);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-bless-offline-{Guid.NewGuid():N}.db");
    private SqlSaveBackend? _db;
    private SqlSaveBackend Db => _db ??= new SqlSaveBackend(_path);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    private static PlayerData Saved(string name, int favor = 20) =>
        new PlayerData { Name1 = name, Name2 = name, Level = 7, WorshippedGod = God, GodFavor = favor, GodFavorGod = God, GodFavorSchema = GameConfig.GodFavorSchemaCurrent, Language = "en" };

    private static BlessOutcome Bless(PlayerData p, DateTime now) =>
        ImmortalDeedSystem.BlessSaved(p, new Dictionary<string, string>(), God, now);

    [Fact]
    public void WorldDays_OfTheTestTimes_AreConsecutive()
    {
        DailySystemManager.WorldDayAt(DayN1).Should().Be(DailySystemManager.WorldDayAt(DayN) + 1);
    }

    [Fact]
    public void DayN_FirstBless_GivesTwo_AndCountsItOnThatWorldDay()
    {
        var p = Saved("GboFirst");
        p.GodFavorDayGains[Key] = 2;  // spent on an earlier day, before the follower went offline
        var o = Bless(p, DayN);
        o.FavorGained.Should().Be(GameConfig.GodBlessFavorGain).And.Be(2);
        p.GodFavor.Should().Be(22);
        p.GodFavorDayGains[Key].Should().Be(2);
        p.GodFavorDayGains[ImmortalDeedSystem.OfflineBlessWorldDayKey].Should().Be(DailySystemManager.WorldDayAt(DayN));
        p.LastDailyResetBoundary.Should().Be(default(DateTime), "the follower's own daily reset still runs at login");
    }

    [Fact]
    public void DayN_SecondBless_GivesNothing()
    {
        var p = Saved("GboSecond");
        Bless(p, DayN).FavorGained.Should().Be(2);
        var o = Bless(p, DayN.AddHours(2));
        o.Refused.Should().BeFalse();
        o.FavorGained.Should().Be(0, "the world day's cap is spent");
        p.GodFavor.Should().Be(22);
    }

    [Fact]
    public void DayN_ThreeBlesses_GiveTwoInTotal()
    {
        var p = Saved("GboThree");
        int total = Bless(p, DayN).FavorGained + Bless(p, DayN.AddHours(1)).FavorGained + Bless(p, DayN.AddHours(3)).FavorGained;
        total.Should().Be(GameConfig.GodBlessFavorDailyCap);
        p.GodFavor.Should().Be(22);
    }

    [Fact]
    public void DayN1_Bless_OfAFollowerStillOffline_GivesTwoAgain()
    {
        var p = Saved("GboNext");
        Bless(p, DayN).FavorGained.Should().Be(2);
        Bless(p, DayN).FavorGained.Should().Be(0);
        var o = Bless(p, DayN1);
        o.FavorGained.Should().Be(2, "a new world day, the follower's reset still due");
        p.GodFavor.Should().Be(24);
        p.GodFavorDayGains[Key].Should().Be(2);
        p.GodFavorDayGains[ImmortalDeedSystem.OfflineBlessWorldDayKey].Should().Be(DailySystemManager.WorldDayAt(DayN1));
        p.LastDailyResetBoundary.Should().Be(default(DateTime));
    }

    [Fact]
    public void ResetNotDue_TheSavedCountIsToday_AndIsKept()
    {
        var p = Saved("GboToday");
        p.LastDailyResetBoundary = DailySystemManager.ResetBoundaryAt(DayN);  // logged in today, then left
        p.GodFavorDayGains[Key] = 2;  // blessed live today
        Bless(p, DayN).FavorGained.Should().Be(0, "today's live bless already spent the cap");
        p.GodFavor.Should().Be(20);
        p.GodFavorDayGains.Should().NotContainKey(ImmortalDeedSystem.OfflineBlessWorldDayKey);
    }

    [Fact]
    public void Marker_IsNotASource_TheLiveCapStillHolds()
    {
        Enum.GetNames(typeof(FavorSource)).Should().NotContain(ImmortalDeedSystem.OfflineBlessWorldDayKey);
        var p = Saved("GboLive");
        Bless(p, DayN).FavorGained.Should().Be(2);

        var (c, gods) = GodMiracles1115Tests.Worshipper("GboLive", God, p.GodFavor);
        c.GodFavorDayGains = new Dictionary<string, int>(p.GodFavorDayGains);  // as the load copies it
        FavorSystem.GainedToday(c, FavorSource.ImmortalBlessing).Should().Be(2);
        FavorSystem.GainCapped(c, FavorSource.ImmortalBlessing, GameConfig.GodBlessFavorGain, GameConfig.GodBlessFavorDailyCap, gods)
            .Should().Be(0, "the offline bless counts against the same cap");
        FavorSystem.Prayer(c, gods).Should().Be(GameConfig.GodFavorPrayerGain, "other sources are untouched");
    }

    [Fact]
    public void Marker_IsClearedByTheFollowersLoginReset()
    {
        var p = Saved("GboReset");
        Bless(p, DayN).FavorGained.Should().Be(2);
        var (c, gods) = GodMiracles1115Tests.Worshipper("GboReset", God, p.GodFavor);
        c.GodFavorDayGains = new Dictionary<string, int>(p.GodFavorDayGains);
        FavorSystem.ApplyDailyReset(c, gods);
        c.GodFavorDayGains.Should().BeEmpty();
        FavorSystem.GainCapped(c, FavorSource.ImmortalBlessing, GameConfig.GodBlessFavorGain, GameConfig.GodBlessFavorDailyCap, gods)
            .Should().Be(2);
    }

    // ---------------- Through the SQL write ----------------

    private Task Save(string key, PlayerData p) =>
        Db.WriteGameData(key, new SaveGameData
        {
            Version = GameConfig.SaveVersion,
            Player = p,
            StorySystems = new StorySystemsData { PlayerGods = new Dictionary<string, string>() }
        });

    private async Task<BlessOutcome> BlessOffline(string key, DateTime now)
    {
        var r = await Db.UpdateFollowerSaveOffline<BlessOutcome?>(key, (p, gods) =>
        {
            var o = ImmortalDeedSystem.BlessSaved(p, gods, God, now);
            return (!o.Refused, o);
        });
        r.Failed.Should().BeFalse();
        r.Result.Should().NotBeNull();
        return r.Result!.Value;
    }

    [Fact]
    public async Task Sql_ConsecutiveWorldDays_EachGiveTwo_AndTheSaveLoadsWithTheMarker()
    {
        var saved = Saved("GboSql");
        saved.GodFavorDayGains[Key] = 2;
        await Save("acct_gbo", saved);

        (await BlessOffline("acct_gbo", DayN)).FavorGained.Should().Be(2);
        (await BlessOffline("acct_gbo", DayN.AddHours(1))).FavorGained.Should().Be(0);
        (await BlessOffline("acct_gbo", DayN1)).FavorGained.Should().Be(2);

        var back = (await Db.ReadGameData("acct_gbo"))!.Player;
        back.GodFavor.Should().Be(24);
        back.GodFavorDayGains[Key].Should().Be(2);
        back.GodFavorDayGains[ImmortalDeedSystem.OfflineBlessWorldDayKey].Should().Be(DailySystemManager.WorldDayAt(DayN1));
        back.LastDailyResetBoundary.Should().Be(default(DateTime));
    }
}
