using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake;
using UsurperRemake.BBS;
using UsurperRemake.Locations;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.8: room progress on the dungeon floor the player stands on lives in DungeonLocation's
/// currentFloor and reached DungeonFloorStates only on leaving the floor. A save taken mid-floor
/// (the disconnect save above all) wrote nothing for it, so a reconnect rebuilt the floor fresh.
/// Every save entry that serializes DungeonFloorStates (SaveGame, AutoSave, the team vault
/// deposit save) now copies the live floor first, without the clear step's quest notice, and
/// only while the player is really on that floor. Saves go to a temporary SQLite database and are
/// read back from it; the disconnect is driven through PlayerSession's own seam, with no socket.
/// </summary>
[Collection("SharedGameSingletons")]
public class FloorFlush128Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private const int Floor = 2;

    private static readonly FieldInfo SaveField = typeof(SaveSystem).GetField("instance", SNP)!;
    private static readonly FieldInfo LmField = typeof(LocationManager).GetField("_fallbackInstance", SNP)!;
    private static readonly FieldInfo DoorField = typeof(DoorMode).GetField("_sessionInfo", SNP)!;
    private static readonly FieldInfo OnlineField = typeof(DoorMode).GetField("_onlineMode", SNP)!;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"usurper-floorflush-{Guid.NewGuid():N}");
    private readonly object? _saveBefore = SaveField.GetValue(null);
    private readonly object? _lmBefore = LmField.GetValue(null);
    private readonly object? _doorBefore = DoorField.GetValue(null);
    private readonly object? _onlineBefore = OnlineField.GetValue(null);
    private readonly SqlSaveBackend _db;
    private readonly LocationManager _lm;

    public FloorFlush128Tests()
    {
        Directory.CreateDirectory(_dir);
        // single player rules (no autosave throttle), saves into a temporary SQLite database
        DoorField.SetValue(null, null);
        OnlineField.SetValue(null, false);
        DoorMode.IsOnlineMode.Should().BeFalse();
        SessionContext.Current.Should().BeNull();
        _db = new SqlSaveBackend(Path.Combine(_dir, "game.db"));
        SaveSystem.InitializeWithBackend(_db);
        // this test's location manager is the one a save asks where the player is
        _lm = new LocationManager(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        LmField.SetValue(null, _lm);
        LocationManager.Existing.Should().BeSameAs(_lm);
    }

    public void Dispose()
    {
        SaveField.SetValue(null, _saveBefore);
        LmField.SetValue(null, _lmBefore);
        DoorField.SetValue(null, _doorBefore);
        OnlineField.SetValue(null, _onlineBefore);
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    // ---------- helpers ----------

    private static Character Hero(string name) => new()
    {
        Name1 = name, Name2 = name, Class = CharacterClass.Warrior, Level = 6, HP = 200, MaxHP = 200,
        AI = CharacterAI.Human
    };

    private void SetLocation(GameLocation where) =>
        typeof(LocationManager).GetField("currentLocationId", F)!.SetValue(_lm, where);

    private static DungeonFloor CurrentFloor(DungeonLocation dungeon) =>
        (DungeonFloor)typeof(DungeonLocation).GetField("currentFloor", F)!.GetValue(dungeon)!;

    private static void SetFloorLive(DungeonLocation dungeon, bool live) =>
        typeof(DungeonLocation).GetField("floorActiveForSave", F)!.SetValue(dungeon, live);

    /// <summary>The session's dungeon with the hero standing on a freshly generated floor, as
    /// EnterLocation leaves it (floor generated, the floor live, the location the dungeon).</summary>
    private DungeonLocation InDungeon(Character hero)
    {
        var dungeon = (DungeonLocation)_lm.GetLocation(GameLocation.Dungeons)!;
        var term = new TerminalEmulator(new MemoryStream(), new MemoryStream());
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(dungeon, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(dungeon, hero);
        typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.SetValue(dungeon, Floor);
        object result = typeof(DungeonLocation).GetMethod("GenerateOrRestoreFloor", F)!.Invoke(dungeon, new object[] { hero, Floor })!;
        ((bool)result.GetType().GetField("WasRestored")!.GetValue(result)!).Should().BeFalse("a fresh floor");
        typeof(DungeonLocation).GetField("currentFloor", F)!.SetValue(dungeon, result.GetType().GetField("Floor")!.GetValue(result));
        SetFloorLive(dungeon, true);
        SetLocation(GameLocation.Dungeons);
        return dungeon;
    }

    /// <summary>Fight and win one monster room: it is explored, cleared, and the player stands in it.</summary>
    private static string ClearOneRoom(DungeonLocation dungeon)
    {
        var floor = CurrentFloor(dungeon);
        var room = floor.Rooms.First(r => r.HasMonsters && !r.IsBossRoom);
        room.IsExplored = true;
        room.IsCleared = true;
        floor.CurrentRoomId = room.Id;
        return room.Id;
    }

    private static void SaveFloorStateOnLeaving(DungeonLocation dungeon, Character hero) =>
        typeof(DungeonLocation).GetMethod("SaveFloorState", F)!.Invoke(dungeon, new object[] { hero });

    private async Task<SaveGameData> ReadBack(string key)
    {
        var data = await _db.ReadGameData(key);
        data.Should().NotBeNull("the save landed in the database");
        return data!;
    }

    /// <summary>Log back in from the save: a new character and a new dungeon restore the floor.</summary>
    private static (DungeonFloor floor, bool wasRestored) Reload(SaveGameData data, string name)
    {
        var back = Hero(name);
        back.DungeonFloorStates = (Dictionary<int, DungeonFloorState>)typeof(GameEngine).GetMethod("RestoreDungeonFloorStates", F)!
            .Invoke(GameEngine.Instance, new object[] { data.Player.DungeonFloorStates })!;
        var fresh = new DungeonLocation();
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(fresh, back);
        typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.SetValue(fresh, Floor);
        object result = typeof(DungeonLocation).GetMethod("GenerateOrRestoreFloor", F)!.Invoke(fresh, new object[] { back, Floor })!;
        return ((DungeonFloor)result.GetType().GetField("Floor")!.GetValue(result)!,
                (bool)result.GetType().GetField("WasRestored")!.GetValue(result)!);
    }

    private static void RoomStillCleared(SaveGameData data, string name, string roomId)
    {
        data.Player.DungeonFloorStates.Should().ContainKey(Floor);
        var (floor, wasRestored) = Reload(data, name);
        wasRestored.Should().BeTrue("the same floor comes back from the save");
        floor.CurrentRoomId.Should().Be(roomId, "the player is back in the room they stood in");
        var room = floor.Rooms.Single(r => r.Id == roomId);
        room.HasMonsters.Should().BeTrue();
        room.IsExplored.Should().BeTrue();
        room.IsCleared.Should().BeTrue("a cleared room's monsters do not come back on a reload");
    }

    private static string FloorStatesJson(Dictionary<int, DungeonFloorStateData> states) =>
        JsonSerializer.Serialize(states.OrderBy(k => k.Key).ToDictionary(k => k.Key, k => k.Value));

    private static Dictionary<int, DungeonFloorStateData> SerializedNow(Character hero) =>
        (Dictionary<int, DungeonFloorStateData>)typeof(SaveSystem).GetMethod("SerializeDungeonFloorStates", F)!
            .Invoke(SaveSystem.Instance, new object[] { hero })!;

    private static MudServer NoServer()
    {
        var t = typeof(MudServer);
        var server = (MudServer)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t);
        t.GetField("<ActiveSessions>k__BackingField", F)!.SetValue(server, new System.Collections.Concurrent.ConcurrentDictionary<string, PlayerSession>());
        return server;
    }

    // ---------- 1: the disconnect save ----------

    [Fact]
    public async Task ARoomClearedMidFloor_SurvivesTheDisconnectSave_AndAReload()
    {
        var hero = Hero("flushdrop");
        var dungeon = InDungeon(hero);
        string roomId = ClearOneRoom(dungeon);
        hero.DungeonFloorStates[Floor].RoomStates.Should().BeEmpty("the progress is only on the live floor until a copy");

        // the connection drops: RunAsync's finally runs this seam with the session's engine
        var engine = (GameEngine)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(GameEngine));
        engine.CurrentPlayer = hero;
        var session = new PlayerSession("flushdrop", "MUD", null!, new MemoryStream(), _db, NoServer(), CancellationToken.None);
        await session.FinishDisconnectPersistAsync(engine);

        RoomStillCleared(await ReadBack("flushdrop"), "flushdrop", roomId);
    }

    // ---------- 2: the quest notice stays with leaving the floor ----------

    [Fact]
    public async Task AFloorClearedThenSavedMidFloor_NotifiesTheFloorClearQuestOnce()
    {
        var hero = Hero("flushquest");
        var quest = new Quest
        {
            Title = "Clear floor test", Initiator = "Test", QuestTarget = QuestTarget.ClearFloor,
            Occupier = hero.Name2, Date = DateTime.Now, DaysToComplete = 30
        };
        quest.Objectives.Add(new QuestObjective(QuestObjectiveType.ClearDungeonFloor, "Clear floor", 5, Floor.ToString(), $"Floor {Floor}"));
        QuestSystem.AddQuestToDatabase(quest);
        try
        {
            var dungeon = InDungeon(hero);
            foreach (var room in CurrentFloor(dungeon).Rooms.Where(r => r.HasMonsters))
            {
                room.IsExplored = true;
                room.IsCleared = true;
            }

            // saves taken while still on the cleared floor copy it, and notify nothing
            (await SaveSystem.Instance.AutoSave(hero)).Should().BeTrue();
            (await SaveSystem.Instance.SaveGame("flushquest", hero)).Should().BeTrue();
            hero.DungeonFloorStates[Floor].RoomStates.Should().NotBeEmpty("the saves copied the floor");
            quest.Objectives[0].CurrentProgress.Should().Be(0, "a save is not a floor clear");

            // leaving the floor notifies the quest, once
            SaveFloorStateOnLeaving(dungeon, hero);
            quest.Objectives[0].CurrentProgress.Should().Be(1);
            hero.DungeonFloorStates[Floor].EverCleared.Should().BeTrue();
        }
        finally
        {
            quest.Deleted = true;
        }
    }

    // ---------- 3: a save outside the dungeon writes the floor states as they were ----------

    /// <summary>The floor left behind, then the Dungeon Reset Scroll in the Magic Shop: the
    /// floor's saved rooms are cleared and its visit back-dated (MagicShopLocation), while the
    /// dungeon still caches the cleared floor.</summary>
    private static void ResetScroll(Character hero)
    {
        var state = hero.DungeonFloorStates[Floor];
        state.RoomStates.Clear();
        state.LastVisitedAt = DateTime.Now.AddHours(-(DungeonFloorState.RESPAWN_HOURS + 1));
    }

    private async Task SaveWritesTheFloorStatesAsTheyWere(Character hero)
    {
        string before = FloorStatesJson(SerializedNow(hero));
        (await SaveSystem.Instance.SaveGame(hero.Name2, hero)).Should().BeTrue();
        var data = await ReadBack(hero.Name2);
        FloorStatesJson(data.Player.DungeonFloorStates).Should().Be(before, "a save away from the floor copies nothing into it");
        data.Player.DungeonFloorStates[Floor].Rooms.Should().BeEmpty("the reset scroll's reset is kept");
    }

    [Fact]
    public async Task ASaveInTownAfterLeavingTheDungeon_WritesTheFloorStatesAsTheyWere()
    {
        var hero = Hero("flushtown");
        var dungeon = InDungeon(hero);
        ClearOneRoom(dungeon);

        // leave through the dungeon's own exit, which saves the floor and ends the live floor
        var leave = (Task)typeof(DungeonLocation).GetMethod("NavigateToLocation", F)!.Invoke(dungeon, new object[] { GameLocation.MagicShop })!;
        await FluentActions.Awaiting(() => leave).Should().ThrowAsync<LocationExitException>();
        SetLocation(GameLocation.MagicShop);
        hero.DungeonFloorStates[Floor].RoomStates.Should().NotBeEmpty("leaving saved the floor");

        ResetScroll(hero);
        await SaveWritesTheFloorStatesAsTheyWere(hero);
    }

    [Fact]
    public async Task ASaveInTownAfterAnExitThatSkippedTheFloorSave_WritesTheFloorStatesAsTheyWere()
    {
        // some exits (a death, a forced move) throw past NavigateToLocation, so the floor is still
        // marked live; the session's location is what says the player is no longer on it
        var hero = Hero("flushforced");
        var dungeon = InDungeon(hero);
        ClearOneRoom(dungeon);
        SaveFloorStateOnLeaving(dungeon, hero);
        SetLocation(GameLocation.MagicShop);

        ResetScroll(hero);
        await SaveWritesTheFloorStatesAsTheyWere(hero);
    }

    [Fact]
    public async Task ASaveInTheDungeonWithoutALiveFloor_WritesTheFloorStatesAsTheyWere()
    {
        // a group follower is in the dungeon location but on the leader's floor; its own cached
        // floor from an earlier visit is not live (EnterLocation clears the mark at entry)
        var hero = Hero("flushfollow");
        var dungeon = InDungeon(hero);
        ClearOneRoom(dungeon);
        SaveFloorStateOnLeaving(dungeon, hero);
        SetFloorLive(dungeon, false);

        ResetScroll(hero);
        await SaveWritesTheFloorStatesAsTheyWere(hero);
    }

    // ---------- 4: the other save entries ----------

    [Fact]
    public async Task AnAutoSaveMidFloor_KeepsTheCurrentFloor()
    {
        var hero = Hero("flushauto");
        var dungeon = InDungeon(hero);
        string roomId = ClearOneRoom(dungeon);

        (await SaveSystem.Instance.AutoSave(hero)).Should().BeTrue();

        RoomStillCleared(await ReadBack("flushauto"), "flushauto", roomId);
    }

    [Fact]
    public async Task ATeamVaultDepositSaveMidFloor_KeepsTheCurrentFloor()
    {
        var hero = Hero("flushvault");
        var dungeon = InDungeon(hero);
        string roomId = ClearOneRoom(dungeon);

        (await SaveSystem.Instance.SaveWithTeamVaultDeposit(hero, "Flushers", 100)).Should().BeTrue();
        (await _db.GetTeamVaultGold("Flushers")).Should().Be(100);

        RoomStillCleared(await ReadBack("flushvault"), "flushvault", roomId);
    }
}
