using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.0 Temple gods: the desecration standing penalty is kept per WORLD week online
/// (DailySystemManager.WorldWeekAt, the 7 PM Eastern daily reset boundaries counted from a fixed
/// epoch, over 7), so every session reads and writes the same week whatever game day its save
/// loaded. Single-player keeps game day / 7.
/// </summary>
[Collection("SharedGameSingletons")]
public class GodWorldWeek1115Tests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-worldweek-{Guid.NewGuid():N}.db");
    private readonly SqlSaveBackend _db;

    public GodWorldWeek1115Tests() { _db = new SqlSaveBackend(_path); }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    private static DateTime Utc(int y, int mo, int d, int h, int mi, int s) => new DateTime(y, mo, d, h, mi, s, DateTimeKind.Utc);

    // Sunday 27 September 2026, 7 PM EDT = 23:00 UTC, is world day 630, the start of world week 90.
    private static readonly DateTime SummerBoundary = Utc(2026, 9, 27, 23, 0, 0);

    [Fact]
    public void Online_SessionsWithDifferentGameDays_AgreeOnTheWeek()
    {
        int day12 = GodStandingPenalty.CurrentWeek(true, 12, SummerBoundary);
        int day700 = GodStandingPenalty.CurrentWeek(true, 700, SummerBoundary);
        day12.Should().Be(day700, "online the week is the world's, not the loaded save's game day");
        day12.Should().Be(DailySystemManager.WorldWeekAt(SummerBoundary));
        day12.Should().Be(90);
    }

    [Theory]
    [InlineData(2025, 1, 12, 23, 59, 59, 6, 0)]   // winter: 7 PM EST is 00:00 UTC the next day
    [InlineData(2025, 1, 13, 0, 0, 0, 7, 1)]
    [InlineData(2026, 9, 27, 22, 59, 59, 629, 89)] // summer: 7 PM EDT is 23:00 UTC the same day
    [InlineData(2026, 9, 27, 23, 0, 0, 630, 90)]
    [InlineData(2025, 3, 16, 22, 59, 59, 69, 9)]   // the week after the spring clock change
    [InlineData(2025, 3, 16, 23, 0, 0, 70, 10)]
    public void WorldWeek_TurnsAtTheSundayResetBoundary(int y, int mo, int d, int h, int mi, int s, int day, int week)
    {
        var t = Utc(y, mo, d, h, mi, s);
        DailySystemManager.WorldDayAt(t).Should().Be(day);
        DailySystemManager.WorldWeekAt(t).Should().Be(week);
    }

    [Fact]
    public void WorldDay_IsNeverNegative_BeforeTheEpoch()
    {
        DailySystemManager.WorldDayAt(Utc(2020, 1, 1, 12, 0, 0)).Should().Be(0);
        DailySystemManager.WorldCalendarEpochEastern.Should().Be(new DateTime(2025, 1, 5));
    }

    [Fact]
    public void SinglePlayer_WeekIsStillTheGameDayOverSeven()
    {
        GodStandingPenalty.CurrentWeek(false, 700, SummerBoundary).Should().Be(100);
        GodStandingPenalty.CurrentWeek(false, 12, SummerBoundary).Should().Be(1);
        GodStandingPenalty.CurrentWeek(false, 12, Utc(2031, 6, 1, 0, 0, 0)).Should().Be(1, "the clock plays no part single-player");
    }

    private Task Save(string key, string name, string canonGod, int favor) =>
        _db.WriteGameData(key, new SaveGameData
        {
            Version = GameConfig.SaveVersion,
            Player = new PlayerData { Name1 = name, Name2 = name, Level = 5, GodFavor = favor, GodFavorGod = canonGod, GodFavorSchema = GameConfig.GodFavorSchemaCurrent },
            StorySystems = new StorySystemsData { PlayerGods = new Dictionary<string, string> { [name] = canonGod } }
        });

    [Fact]
    public async Task Online_APenaltyWrittenByOneSession_IsVisibleToAnother_AndNotErased()
    {
        await Save("acct_ww_a", "Wendel", "Solarius", 30);
        var online = typeof(UsurperRemake.BBS.DoorMode).GetField("_onlineMode", BindingFlags.NonPublic | BindingFlags.Static)!;
        var dayField = typeof(DailySystemManager).GetField("currentDay", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var dm = DailySystemManager.Instance;
        bool wasOnline = (bool)online.GetValue(null)!;
        int wasDay = (int)dayField.GetValue(dm)!;
        try
        {
            online.SetValue(null, true);

            dayField.SetValue(dm, 12);   // a new character's save loaded last
            _db.AddGodStandingPenalty("Solarius", GodStandingPenalty.CurrentWeek(), GameConfig.GodDesecrationStandingPenalty);

            dayField.SetValue(dm, 700);  // a veteran's save loaded last
            _db.GetGodStandings()["Solarius"].Standing.Should().Be(30 - GameConfig.GodDesecrationStandingPenalty,
                "the day 12 session's penalty counts for the day 700 session");

            _db.AddGodStandingPenalty("Solarius", GodStandingPenalty.CurrentWeek(), GameConfig.GodDesecrationStandingPenalty);
            dayField.SetValue(dm, 12);
            _db.GetGodStandings()["Solarius"].Standing.Should().Be(30 - 2 * GameConfig.GodDesecrationStandingPenalty,
                "the veteran's write adds to this week's row and erases nothing");
        }
        finally
        {
            online.SetValue(null, wasOnline);
            dayField.SetValue(dm, wasDay);
        }
    }
}
