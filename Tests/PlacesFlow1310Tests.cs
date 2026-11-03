using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.BBS;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.10 audit, places and session flow: paying bail walks the player out to Main Street; a no or too little gold at
/// the Lodging bunk keeps the player there; single player Lodging sleep only once it is late enough, with the night
/// passing; the good and dark deed counts load as saved; GMCP town exits come from the menus the player really has.
/// </summary>
[Collection("SharedGameSingletons")]
public class PlacesFlow1310Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly FieldInfo OnlineField =
        typeof(DoorMode).GetField("_onlineMode", BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly King? _kingBefore = CastleLocation.GetCurrentKing();
    private readonly bool _onlineBefore = (bool)OnlineField.GetValue(null)!;

    public void Dispose()
    {
        CastleLocation.SetKing(_kingBefore!);
        OnlineField.SetValue(null, _onlineBefore);
    }

    private sealed class Screen
    {
        public TerminalEmulator Term = null!;
        public MemoryStream Output = new();
        public string Text
        {
            get
            {
                Term.StreamWriterInternal?.Flush();
                return Regex.Replace(Encoding.UTF8.GetString(Output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "");
            }
        }
    }

    private static Screen NewScreen(params string[] lines)
    {
        var s = new Screen();
        s.Term = new TerminalEmulator(new LineStream(lines.Concat(Enumerable.Repeat("", 12)), _ =>
        {
            s.Term.StreamWriterInternal?.Flush();
            s.Output.WriteByte((byte)'\n');
        }), s.Output);
        return s;
    }

    private static T At<T>(T loc, Character p, TerminalEmulator term) where T : BaseLocation
    {
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(loc, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(loc, p);
        return loc;
    }

    private static async Task<object?> Call(object target, string method, params object[] args)
    {
        var m = target.GetType().GetMethod(method, F) ?? typeof(BaseLocation).GetMethod(method, F);
        m.Should().NotBeNull($"{method} must exist");
        try
        {
            var task = (Task)m!.Invoke(target, args)!;
            await task;
            return task.GetType().GetProperty("Result")?.GetValue(task);
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    private static Character Hero(string name, long gold = 5_000) => new()
    {
        Name1 = name, Name2 = name, Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 10,
        HP = 40, MaxHP = 200, BaseMaxHP = 200, Mana = 0, MaxMana = 0, AI = CharacterAI.Human, Gold = gold,
        Sex = CharacterSex.Male,
    };

    private static King AKing()
    {
        var king = King.CreateNewKing("Test Monarch", CharacterAI.Computer, CharacterSex.Female);
        king.Treasury = 1_000;
        return king;
    }

    // ================= E1: bail =================

    [Fact]
    public async Task PayingBail_WalksTheFreedPlayerOutToMainStreet()
    {
        CastleLocation.SetKing(AKing());
        var hero = Hero("Bail Payer", 2_000);
        hero.DaysInPrison = 5;
        CastleLocation.GetCurrentKing().Prisoners[hero.Name2] = new PrisonRecord { CharacterName = hero.Name2, Crime = "Theft", Sentence = 5, BailAmount = 300 };
        var s = NewScreen("B", "Y", "");
        var prison = new PrisonLocation(null!, s.Term);

        var exit = await Assert.ThrowsAsync<LocationExitException>(() => prison.EnterLocation(hero, s.Term));

        exit.DestinationLocation.Should().Be(GameLocation.MainStreet, "a paid bail walks the player out to Main Street, as the escape does");
        hero.DaysInPrison.Should().Be(0);
        hero.Gold.Should().Be(1_700, "the bail was paid");
        CastleLocation.GetCurrentKing().Prisoners.Should().NotContainKey(hero.Name2);
        s.Text.Should().Contain(Loc.Get("prison.bail_free"));
    }

    [Fact]
    public async Task DecliningBail_StaysInTheCell()
    {
        CastleLocation.SetKing(AKing());
        var hero = Hero("Bail Keeper", 2_000);
        hero.DaysInPrison = 5;
        CastleLocation.GetCurrentKing().Prisoners[hero.Name2] = new PrisonRecord { CharacterName = hero.Name2, Crime = "Theft", Sentence = 5, BailAmount = 300 };
        var s = NewScreen("N");
        var prison = new PrisonLocation(null!, s.Term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(prison, hero);

        var stays = await Call(prison, "HandlePayBail", hero);

        stays.Should().Be(false, "a declined bail keeps the prison loop going");
        hero.DaysInPrison.Should().Be(5);
        hero.Gold.Should().Be(2_000);
    }

    // ================= E2: the Lodging bunk declined =================

    [Fact]
    public async Task SinglePlayer_DecliningTheBunk_StaysInTheDormitory()
    {
        OnlineField.SetValue(null, false);
        var hero = Hero("Bunk Decliner");
        hero.GameTimeMinutes = 22 * 60;   // late enough, so the question is asked
        var s = NewScreen("N");
        var dorm = At(new DormitoryLocation(), hero, s.Term);

        var exit = await Call(dorm, "ProcessChoice", "G");

        exit.Should().Be(false, "saying no keeps the player in the Dormitory");
        hero.HP.Should().Be(40, "no sleep happened");
        hero.GameTimeMinutes.Should().Be(22 * 60);
        s.Text.Should().Contain(Loc.Get("dormitory.stay_night").Trim());
    }

    [Fact]
    public async Task Online_DecliningTheBunk_StaysInTheDormitory()
    {
        OnlineField.SetValue(null, true);
        var hero = Hero("Bunk Decliner Online", 500);
        var s = NewScreen("N");
        var dorm = At(new DormitoryLocation(), hero, s.Term);

        var exit = await Call(dorm, "ProcessChoice", "G");

        exit.Should().Be(false, "saying no keeps the player in the game");
        hero.Gold.Should().Be(500, "no bunk was paid for");
        s.Text.Should().Contain(Loc.Get("dormitory.sleep_logout").Trim());
    }

    [Fact]
    public async Task Online_TooLittleGoldForTheBunk_StaysInTheDormitory()
    {
        OnlineField.SetValue(null, true);
        var hero = Hero("Bunk Pauper", 0);
        hero.BankGold = 0;
        var s = NewScreen("Y");
        var dorm = At(new DormitoryLocation(), hero, s.Term);

        var exit = await Call(dorm, "ProcessChoice", "G");

        exit.Should().Be(false, "a bunk the player cannot pay for keeps the player in the game");
        hero.HP.Should().Be(40, "no sleep happened");
        s.Text.Should().Contain(Loc.Get("dormitory.cant_afford"));
    }

    // ================= E3: single player Lodging sleep in the evening, the night passing =================

    [Fact]
    public async Task SinglePlayer_LodgingSleep_InTheDay_IsRefused_LikeTheInn()
    {
        OnlineField.SetValue(null, false);
        var hero = Hero("Day Sleeper");
        hero.GameTimeMinutes = 8 * 60;
        int dayBefore = DailySystemManager.Instance.CurrentDay;
        var s = NewScreen("Y", "");
        var dorm = At(new DormitoryLocation(), hero, s.Term);

        var exit = await Call(dorm, "ProcessChoice", "G");

        exit.Should().Be(false);
        s.Text.Should().Contain(Loc.Get("inn.sleep_not_late")).And.NotContain(Loc.Get("dormitory.stay_night").Trim());
        hero.HP.Should().Be(40, "no free full heal in the day");
        hero.GameTimeMinutes.Should().Be(8 * 60, "no night passed");
        DailySystemManager.Instance.CurrentDay.Should().Be(dayBefore, "no new day in the morning");
    }

    [Fact]
    public async Task SinglePlayer_LodgingSleep_AtNight_PassesTheNightToMorning()
    {
        OnlineField.SetValue(null, false);
        var hero = Hero("Night Sleeper");
        hero.GameTimeMinutes = 22 * 60;
        int dayBefore = DailySystemManager.Instance.CurrentDay;
        // the engine's periodic update runs at most every 30 seconds; the test engine has no daily manager for it
        typeof(GameEngine).GetField("lastPeriodicCheck", F)!.SetValue(GameEngine.Instance, DateTime.Now);
        var s = NewScreen("Y", "", "");
        var dorm = At(new DormitoryLocation(), hero, s.Term);

        var exit = await Assert.ThrowsAsync<LocationExitException>(() => Call(dorm, "ProcessChoice", "G"));

        exit.DestinationLocation.Should().Be(GameLocation.MainStreet);
        hero.GameTimeMinutes.Should().Be(GameConfig.DayStartHour * 60, "the night passed to morning, as at the Inn and Home");
        hero.HP.Should().Be(hero.MaxHP);
        DailySystemManager.Instance.CurrentDay.Should().BeGreaterThan(dayBefore, "a new day after the night");
        s.Text.Should().Contain(Loc.Get("dormitory.awaken_refreshed"));
    }

    // ================= E6: deed counts load as saved =================

    private static Character Restore(PlayerData data)
    {
        var restore = typeof(GameEngine).GetMethod("RestorePlayerFromSaveData", F)!;
        try { return (Character)restore.Invoke(GameEngine.Instance, new object[] { data })!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    private static PlayerData RoundTrip(Character hero)
    {
        var ser = typeof(SaveSystem).GetMethod("SerializePlayer", F)!;
        var data = (PlayerData)ser.Invoke(SaveSystem.Instance, new object[] { hero })!;
        return System.Text.Json.JsonSerializer.Deserialize<PlayerData>(System.Text.Json.JsonSerializer.Serialize(data))!;
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 2)]
    [InlineData(1, 0)]
    [InlineData(5, 4)]
    public void DeedCounts_LoadAsSaved_AZeroStaysZero(int good, int dark)
    {
        var hero = Hero("Deed Doer");
        hero.ChivNr = good;
        hero.DarkNr = dark;

        var loaded = Restore(RoundTrip(hero));

        loaded.ChivNr.Should().Be(good, "good deeds left today are not refilled by a load");
        loaded.DarkNr.Should().Be(dark, "dark deeds left today are not refilled by a load");
    }

    // ================= E9: GMCP town exits =================

    private static Dictionary<string, int> GmcpExits(GameLocation at, Character player)
    {
        var lm = LocationManager.Instance;
        typeof(LocationManager).GetField("currentPlayer", F)!.SetValue(lm, player);
        return (Dictionary<string, int>)typeof(LocationManager).GetMethod("BuildGmcpExitsForLocation", F)!.Invoke(lm, new object[] { at })!;
    }

    [Fact]
    public void GmcpMainStreetExits_AreThePlacesItsMenuOpens()
    {
        OnlineField.SetValue(null, true);
        var exits = GmcpExits(GameLocation.MainStreet, Hero("Mapper"));

        exits.Keys.Should().Contain(new[] { "temple", "castle", "questhall", "lovecorner", "theinn", "dungeons", "anchorroad", "arena", "dormitory" },
            "Main Street's menu reaches these");
        exits.Keys.Should().NotContain(new[] { "darkness", "reportroom", "foodstore", "bountyroom", "outsidecastle", "lovestreet" },
            "none of these is a registered place");
        exits["temple"].Should().Be((int)GameLocation.Temple);
        exits.Keys.Should().NotContain("auctionhouse", "online the auctions are a Main Street screen, not a place");
    }

    [Fact]
    public void GmcpMainStreetExits_FollowTheMenuTier()
    {
        OnlineField.SetValue(null, true);
        var exits = GmcpExits(GameLocation.MainStreet, new Character { Name2 = "Newcomer", Level = 1, AI = CharacterAI.Human });

        exits.Keys.Should().Contain(new[] { "dungeons", "theinn", "healer", "master", "questhall" });
        exits.Keys.Should().NotContain(new[] { "castle", "anchorroad", "darkalley" }, "a level 1 Main Street does not show these yet");
    }

    [Theory]
    [InlineData(GameLocation.Home)]
    [InlineData(GameLocation.AnchorRoad)]
    [InlineData(GameLocation.TheInn)]
    [InlineData(GameLocation.Temple)]
    [InlineData(GameLocation.Dormitory)]
    public void GmcpTownScreenExits_LeadBackToMainStreet(GameLocation at)
    {
        OnlineField.SetValue(null, true);
        var exits = GmcpExits(at, Hero("Mapper"));

        exits.Should().BeEquivalentTo(new Dictionary<string, int> { ["mainstreet"] = (int)GameLocation.MainStreet },
            "every town screen's menu leaves to Main Street");
    }

    [Fact]
    public void GmcpPrisonHasNoWalkingExit()
    {
        OnlineField.SetValue(null, true);
        GmcpExits(GameLocation.Prison, Hero("Mapper")).Should().BeEmpty();
    }

    [Fact]
    public void TrainAndWizardGoto_StillUseTheWalkingTable()
    {
        BaseLocation.CanTravelToLevelMaster(GameLocation.MainStreet).Should().BeTrue("/train walks from Main Street");
        BaseLocation.CanTravelToLevelMaster(GameLocation.TheInn).Should().BeFalse();
        LocationManager.Instance.CanNavigateTo(GameLocation.MainStreet, GameLocation.Bank).Should().BeTrue("wizard goto keeps its table");
    }
}
