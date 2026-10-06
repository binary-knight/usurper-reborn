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
    private DungeonLocation InDungeon(Character hero, int level = Floor)
    {
        var dungeon = (DungeonLocation)_lm.GetLocation(GameLocation.Dungeons)!;
        var term = new TerminalEmulator(new MemoryStream(), new MemoryStream());
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(dungeon, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(dungeon, hero);
        typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.SetValue(dungeon, level);
        object result = typeof(DungeonLocation).GetMethod("GenerateOrRestoreFloor", F)!.Invoke(dungeon, new object[] { hero, level })!;
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

    // ---------- 5: the room fight sets the room cleared at the win (follow-up) ----------

    private sealed class ScriptedStream : Stream
    {
        private readonly byte[] _data;
        private int _pos;
        public ScriptedStream(string script) { _data = Encoding.UTF8.GetBytes(script); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos >= _data.Length) return 0;
            int n = Math.Min(count, _data.Length - _pos);
            Array.Copy(_data, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => Task.FromResult(Read(buffer, offset, count));
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // attacks, then P (pass on a loot drop) and N (no thanks to the monk) for the screens after the
    // win, which ask again on any other key, then plain keys for the closing prompts
    private static string FightScript() =>
        string.Concat(Enumerable.Repeat("A\n\n", 40)) + string.Concat(Enumerable.Repeat("P\nN\n", 40))
        + string.Concat(Enumerable.Repeat("\n", 40));

    private static Character Fighter(string name) => new()
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Human,
        Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 30,
        HP = 9000, MaxHP = 9000, BaseMaxHP = 9000,
        Strength = 900, BaseStrength = 900, Defence = 300, BaseDefence = 300,
        Dexterity = 200, BaseDexterity = 200, Agility = 200, BaseAgility = 200,
        Constitution = 200, BaseConstitution = 200, Intelligence = 20, BaseIntelligence = 20,
        Wisdom = 20, BaseWisdom = 20, Stamina = 300, BaseStamina = 300,
        Mental = 100, Gold = 0, Healing = 0, AutoHeal = false,
        CombatSpeed = CombatSpeed.Instant,
    };

    /// <summary>The hero in the dungeon with a scripted terminal, the room to fight, and the number of
    /// monsters the fight will meet (the dungeon's random seeded, the same group generated here).</summary>
    private (DungeonLocation dungeon, DungeonRoom room, int monsterCount) RoomFight(Character hero, int seed = 11)
    {
        var dungeon = InDungeon(hero);
        var term = new TerminalEmulator(new ScriptedStream(FightScript()), new MemoryStream());
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(dungeon, term);
        typeof(DungeonLocation).GetField("dungeonRandom", F)!.SetValue(dungeon, new Random(seed));
        int count = MonsterGenerator.GenerateMonsterGroup(Floor, new Random(seed), MonsterGenerator.OldGodApproachScale(Floor)).Count;
        var floor = CurrentFloor(dungeon);
        var room = floor.Rooms.First(r => r.HasMonsters && !r.IsBossRoom);
        room.IsExplored = true;
        floor.CurrentRoomId = room.Id;
        return (dungeon, room, count);
    }

    private static Task Fight(DungeonLocation dungeon, DungeonRoom room) =>
        (Task)typeof(DungeonLocation).GetMethod("FightRoomMonsters", F)!.Invoke(dungeon, new object[] { room, false })!;

    /// <summary>The connection dies at the victory autosave: that write throws, as a dead socket
    /// would end the session there. Every other call goes to the real database.</summary>
    private sealed class DropAtVictorySave : ISaveBackend
    {
        private readonly SqlSaveBackend _inner;
        public int AutoSaves;
        public DropAtVictorySave(SqlSaveBackend inner) { _inner = inner; }
        public Task<bool> WriteAutoSave(string playerName, SaveGameData data)
        {
            AutoSaves++;
            throw new IOException("connection lost at the victory save");
        }
        public Task<bool> WriteGameData(string playerName, SaveGameData data) => _inner.WriteGameData(playerName, data);
        public Task<SaveGameData?> ReadGameData(string playerName) => _inner.ReadGameData(playerName);
        public Task<SaveGameData?> ReadGameDataByFileName(string fileName) => _inner.ReadGameDataByFileName(fileName);
        public bool GameDataExists(string playerName) => _inner.GameDataExists(playerName);
        public bool DeleteGameData(string playerName) => _inner.DeleteGameData(playerName);
        public List<SaveInfo> GetAllSaves() => _inner.GetAllSaves();
        public List<SaveInfo> GetPlayerSaves(string playerName) => _inner.GetPlayerSaves(playerName);
        public SaveInfo? GetMostRecentSave(string playerName) => _inner.GetMostRecentSave(playerName);
        public List<string> GetAllPlayerNames() => _inner.GetAllPlayerNames();
        public bool IsDisplayNameTaken(string displayName, string excludeUsername) => _inner.IsDisplayNameTaken(displayName, excludeUsername);
        public void CreateBackup(string playerName) => _inner.CreateBackup(playerName);
        public string GetSaveDirectory() => _inner.GetSaveDirectory();
    }

    [Fact]
    public async Task ADisconnectAtTheVictorySave_KeepsTheRoomCleared_AndAReloadDoesNotRespawnIt()
    {
        var hero = Fighter("flushwin");
        var (dungeon, room, _) = RoomFight(hero);
        var drop = new DropAtVictorySave(_db);
        SaveSystem.InitializeWithBackend(drop);

        await FluentActions.Awaiting(() => Fight(dungeon, room)).Should().ThrowAsync<IOException>("the session died at the victory autosave");
        drop.AutoSaves.Should().Be(1, "the drop came at the victory autosave");

        // the disconnect save of the dead session
        var engine = (GameEngine)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(GameEngine));
        engine.CurrentPlayer = hero;
        var session = new PlayerSession("flushwin", "MUD", null!, new MemoryStream(), _db, NoServer(), CancellationToken.None);
        await session.FinishDisconnectPersistAsync(engine);

        RoomStillCleared(await ReadBack("flushwin"), "flushwin", room.Id);
    }

    [Fact]
    public async Task ARoomFightWon_ClearsTheRoom_AndCountsItsMonstersOnce()
    {
        var hero = Fighter("flushcount");
        var (dungeon, room, count) = RoomFight(hero);
        count.Should().BeGreaterThan(0);
        int killedBefore = CurrentFloor(dungeon).MonstersKilled;
        long xpBefore = hero.Experience;

        await Fight(dungeon, room);

        room.IsCleared.Should().BeTrue();
        CurrentFloor(dungeon).MonstersKilled.Should().Be(killedBefore + count, "the win counts the room's monsters once");
        hero.Experience.Should().BeGreaterThan(xpBefore, "the fight's rewards are still paid");
        // and the victory autosave already held the room cleared
        RoomStillCleared(await ReadBack("flushcount"), "flushcount", room.Id);
    }

    [Fact]
    public async Task TheLastRoomOfAFloorWon_NotifiesTheFloorClearQuestOnce()
    {
        var hero = Fighter("flushlast");
        var quest = new Quest
        {
            Title = "Clear floor test", Initiator = "Test", QuestTarget = QuestTarget.ClearFloor,
            Occupier = hero.Name2, Date = DateTime.Now, DaysToComplete = 30
        };
        quest.Objectives.Add(new QuestObjective(QuestObjectiveType.ClearDungeonFloor, "Clear floor", 5, Floor.ToString(), $"Floor {Floor}"));
        QuestSystem.AddQuestToDatabase(quest);
        try
        {
            var (dungeon, room, _) = RoomFight(hero);
            foreach (var r in CurrentFloor(dungeon).Rooms.Where(r => r.HasMonsters && r != room))
                r.IsCleared = true;

            await Fight(dungeon, room);
            room.IsCleared.Should().BeTrue();
            quest.Objectives[0].CurrentProgress.Should().Be(0, "winning the room is not leaving the floor");

            SaveFloorStateOnLeaving(dungeon, hero);
            quest.Objectives[0].CurrentProgress.Should().Be(1);
        }
        finally
        {
            quest.Deleted = true;
        }
    }

    private static Monster Rat(string name) => new()
    {
        Name = name, Level = 1, HP = 1, MaxHP = 1, Strength = 1, Defence = 0, Experience = 1, Gold = 0, IsActive = true,
    };

    private static async Task<CombatResult> EngineFight(CombatEngine engine, Character hero)
    {
        var task = engine.PlayerVsMonsters(hero, new List<Monster> { Rat("Rat A") }, offerMonkEncounter: false);
        var done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(60)));
        done.Should().BeSameAs(task, "the fight ends");
        return await task;
    }

    [Fact]
    public async Task TheVictoryPreSaveHook_IsClearedAfterItsFight_SoALaterFightNeverRunsIt()
    {
        var hero = Fighter("flushlater");
        var engine = new CombatEngine(new TerminalEmulator(new ScriptedStream(FightScript() + FightScript()), new MemoryStream()));
        engine.SeedRandomForTests(128);
        int runs = 0;
        engine.VictoryPreSaveHook = _ => runs++;

        (await EngineFight(engine, hero)).Outcome.Should().Be(CombatOutcome.Victory);
        runs.Should().Be(1, "the hook runs at its own fight's win");
        engine.VictoryPreSaveHook.Should().BeNull("the fight cleared it");

        // a later fight on the same engine (no dungeon room behind it)
        (await EngineFight(engine, hero)).Outcome.Should().Be(CombatOutcome.Victory);
        runs.Should().Be(1, "a later fight never runs the room's hook");
    }

    [Fact]
    public async Task TheVictoryPreSaveHook_OfAFightThatNeverStarted_IsNotRunByTheNextFight()
    {
        // a fight refused at Mental 0 returns before any round; its hook must not carry over
        var hero = Fighter("flushrefused");
        var engine = new CombatEngine(new TerminalEmulator(new ScriptedStream(FightScript()), new MemoryStream()));
        engine.SeedRandomForTests(128);
        int runs = 0;
        engine.VictoryPreSaveHook = _ => runs++;
        hero.Mental = 0;
        MentalSystem.CollapseDue(hero).Should().BeTrue();
        (await EngineFight(engine, hero)).MentalCollapseNotFought.Should().BeTrue();
        engine.VictoryPreSaveHook.Should().BeNull();

        hero.Mental = 100;
        (await EngineFight(engine, hero)).Outcome.Should().Be(CombatOutcome.Victory);
        runs.Should().Be(0, "the next fight is not the one the hook was set for");
    }

    [Fact]
    public void TheVictoryPreSaveHook_RunsOnlyOnAVictoryWithEveryMonsterDead()
    {
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        int runs = 0;
        // the hook as PlayerVsMonsters holds it for the fight under way
        Action<CombatResult> hook = _ => runs++;
        typeof(CombatEngine).GetField("_victoryPreSaveHookThisFight", F)!.SetValue(engine, hook);
        var dead = Rat("Rat A"); dead.HP = 0;
        var alive = Rat("Rat B");

        // a Victory with a monster still standing (the wizard god mode rescue sets Victory so)
        engine.RunVictoryPreSaveHook(new CombatResult { Outcome = CombatOutcome.Victory, Monsters = new List<Monster> { dead, alive } });
        runs.Should().Be(0);
        // not a Victory
        engine.RunVictoryPreSaveHook(new CombatResult { Outcome = CombatOutcome.PlayerEscaped, Monsters = new List<Monster> { dead } });
        runs.Should().Be(0);
        // every monster dead, a Victory
        engine.RunVictoryPreSaveHook(new CombatResult { Outcome = CombatOutcome.Victory, Monsters = new List<Monster> { dead } });
        runs.Should().Be(1);
    }

    // ---------- 6: a boss room won keeps the boss defeated (follow-up 3) ----------

    /// <summary>The hero in the floor's boss room with a scripted terminal; the transcript stream is returned.</summary>
    private (DungeonLocation dungeon, DungeonRoom room, MemoryStream output) BossFight(Character hero, int level = Floor)
    {
        var dungeon = InDungeon(hero, level);
        var output = new MemoryStream();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(dungeon, new TerminalEmulator(new ScriptedStream(FightScript()), output));
        typeof(DungeonLocation).GetField("dungeonRandom", F)!.SetValue(dungeon, new Random(11));
        var floor = CurrentFloor(dungeon);
        var room = floor.Rooms.Single(r => r.IsBossRoom);
        room.IsExplored = true;
        floor.CurrentRoomId = room.Id;
        return (dungeon, room, output);
    }

    private static int Occurrences(string text, string part)
    {
        int n = 0;
        for (int i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal)) n++;
        return n;
    }

    [Fact]
    public async Task ADisconnectAtTheVictorySaveOfABossFight_KeepsTheBossDefeated_AndAReloadDoesNotRespawnIt()
    {
        var hero = Fighter("flushboss");
        var (dungeon, room, _) = BossFight(hero);
        var drop = new DropAtVictorySave(_db);
        SaveSystem.InitializeWithBackend(drop);

        await FluentActions.Awaiting(() => Fight(dungeon, room)).Should().ThrowAsync<IOException>("the session died at the victory autosave");
        drop.AutoSaves.Should().Be(1);

        var engine = (GameEngine)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(GameEngine));
        engine.CurrentPlayer = hero;
        var session = new PlayerSession("flushboss", "MUD", null!, new MemoryStream(), _db, NoServer(), CancellationToken.None);
        await session.FinishDisconnectPersistAsync(engine);

        var data = await ReadBack("flushboss");
        data.Player.DungeonFloorStates[Floor].BossDefeated.Should().BeTrue("the save holds the boss defeated");
        // a restore forces a boss room uncleared without BossDefeated; it stays cleared, the boss does not return
        RoomStillCleared(data, "flushboss", room.Id);
    }

    [Fact]
    public async Task ABossRoomWon_SetsBothBossFlags_AndStillPaysTheBossBonusOnce()
    {
        var hero = Fighter("flushbonus");
        var (dungeon, room, output) = BossFight(hero);
        long goldBefore = hero.Gold;

        await Fight(dungeon, room);

        room.IsCleared.Should().BeTrue();
        CurrentFloor(dungeon).BossDefeated.Should().BeTrue();
        hero.DungeonFloorStates[Floor].BossDefeated.Should().BeTrue();
        hero.Gold.Should().BeGreaterThanOrEqualTo(goldBefore + Floor * 500L, "the boss bonus (floor x 500 and up) is paid after the fight as before");
        string shown = Encoding.UTF8.GetString(output.ToArray());
        Occurrences(shown, Loc.Get("dungeon.boss_defeated")).Should().Be(1, "the boss lines after the fight show once");
    }

    [Fact]
    public async Task ABossRoomWonOnASealFloor_QuestAndSealMarksOnce_AndTheNextFloorOpensAsBefore()
    {
        const int SealFloor = 15;
        var hero = Fighter("flushseal");
        hero.ClearedSpecialFloors.Add(SealFloor);   // the floor's seal was found (TryDiscoverSeal marks it)
        var quest = new Quest
        {
            Title = "Boss and floor test", Initiator = "Test", QuestTarget = QuestTarget.ClearFloor,
            Occupier = hero.Name2, Date = DateTime.Now, DaysToComplete = 30
        };
        QuestSystem.AddQuestToDatabase(quest);
        try
        {
            var (dungeon, room, _) = BossFight(hero, SealFloor);
            // the floor's boss by name (an objective with no target also matches the boss's tier name,
            // so it would count one kill twice; that is OnMonsterKilled's own rule, not this fight's)
            string bossName = (string)typeof(DungeonLocation).GetMethod("GetBossName", F)!.Invoke(dungeon, new object[] { CurrentFloor(dungeon).Theme })!;
            string bossId = bossName.ToLower().Replace(" ", "_");
            quest.Objectives.Add(new QuestObjective(QuestObjectiveType.KillBoss, "Kill the boss", 5, bossId, bossName));
            quest.Objectives.Add(new QuestObjective(QuestObjectiveType.ClearDungeonFloor, "Clear floor", 5, SealFloor.ToString(), $"Floor {SealFloor}"));
            foreach (var r in CurrentFloor(dungeon).Rooms.Where(r => r.HasMonsters && r != room))
                r.IsCleared = true;

            await Fight(dungeon, room);

            var state = hero.DungeonFloorStates[SealFloor];
            state.BossDefeated.Should().BeTrue();
            quest.Objectives[0].CurrentProgress.Should().Be(1, "the boss kill counts once");
            quest.Objectives[1].CurrentProgress.Should().Be(0, "winning the room is not leaving the floor");
            state.IsPermanentlyClear.Should().BeFalse("the seal floor's permanent clear waits for leaving the floor");
            state.EverCleared.Should().BeFalse();
            ((bool)typeof(DungeonLocation).GetMethod("IsFloorCleared", F)!.Invoke(dungeon, null)!).Should().BeTrue();
            ((int)typeof(DungeonLocation).GetMethod("GetMaxAccessibleFloor", F)!.Invoke(dungeon, new object[] { hero, SealFloor + 1 })!)
                .Should().Be(SealFloor + 1, "the next floor opens as before");

            SaveFloorStateOnLeaving(dungeon, hero);
            quest.Objectives[0].CurrentProgress.Should().Be(1);
            quest.Objectives[1].CurrentProgress.Should().Be(1, "leaving the cleared floor notifies once");
            state.IsPermanentlyClear.Should().BeTrue();
            hero.ClearedSpecialFloors.Count(f => f == SealFloor).Should().Be(1);
        }
        finally
        {
            quest.Deleted = true;
        }
    }
}
