using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake;
using UsurperRemake.BBS;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.10 quest picks (piece FB2), P9 in the dungeon: the real DungeonLocation on a generated floor, with a scripted
/// terminal and real combat. Arriving on the floor of a held placed target says it hides there; entering its room
/// starts the fight, and the win completes the objective; entering any other room does not. Single player rules,
/// saves into a temporary SQLite database; every singleton touched is put back.
/// </summary>
[Collection("SharedGameSingletons")]
public class QuestPicksDungeon1310Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private const int Floor = 2;

    private static readonly FieldInfo SaveField = typeof(SaveSystem).GetField("instance", SNP)!;
    private static readonly FieldInfo LmField = typeof(LocationManager).GetField("_fallbackInstance", SNP)!;
    private static readonly FieldInfo DoorField = typeof(DoorMode).GetField("_sessionInfo", SNP)!;
    private static readonly FieldInfo OnlineField = typeof(DoorMode).GetField("_onlineMode", SNP)!;
    private static readonly FieldInfo DbField = typeof(QuestSystem).GetField("questDatabase", SNP)!;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"usurper-fb2-{Guid.NewGuid():N}");
    private readonly object? _saveBefore = SaveField.GetValue(null);
    private readonly object? _lmBefore = LmField.GetValue(null);
    private readonly object? _doorBefore = DoorField.GetValue(null);
    private readonly object? _onlineBefore = OnlineField.GetValue(null);
    private readonly bool _pacingWas = Pacing.Disabled;
    private readonly SqlSaveBackend _db;
    private readonly LocationManager _lm;
    private readonly List<Quest> _made = new();

    public QuestPicksDungeon1310Tests()
    {
        Directory.CreateDirectory(_dir);
        Pacing.Disabled = true;
        DoorField.SetValue(null, null);
        OnlineField.SetValue(null, false);
        _db = new SqlSaveBackend(Path.Combine(_dir, "game.db"));
        SaveSystem.InitializeWithBackend(_db);
        _lm = new LocationManager(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        LmField.SetValue(null, _lm);
    }

    public void Dispose()
    {
        foreach (var q in _made) ((List<Quest>)DbField.GetValue(null)!).Remove(q);
        Pacing.Disabled = _pacingWas;
        SaveField.SetValue(null, _saveBefore);
        LmField.SetValue(null, _lmBefore);
        DoorField.SetValue(null, _doorBefore);
        OnlineField.SetValue(null, _onlineBefore);
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

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
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.FromResult(Read(buffer, offset, count));
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

    // attacks, then P (pass on a loot drop) and N (no thanks to the monk), then plain keys for the closing prompts
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

    private static DungeonFloor CurrentFloor(DungeonLocation dungeon) =>
        (DungeonFloor)typeof(DungeonLocation).GetField("currentFloor", F)!.GetValue(dungeon)!;

    /// <summary>The session's dungeon with the hero on a fresh floor and a scripted terminal; returns its output.</summary>
    private (DungeonLocation dungeon, MemoryStream output) InDungeon(Character hero)
    {
        var dungeon = (DungeonLocation)_lm.GetLocation(GameLocation.Dungeons)!;
        var output = new MemoryStream();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(dungeon, new TerminalEmulator(new ScriptedStream(FightScript()), output));
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(dungeon, hero);
        typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.SetValue(dungeon, Floor);
        typeof(DungeonLocation).GetField("dungeonRandom", F)!.SetValue(dungeon, new Random(5));
        object result = typeof(DungeonLocation).GetMethod("GenerateOrRestoreFloor", F)!.Invoke(dungeon, new object[] { hero, Floor })!;
        typeof(DungeonLocation).GetField("currentFloor", F)!.SetValue(dungeon, result.GetType().GetField("Floor")!.GetValue(result));
        typeof(DungeonLocation).GetField("floorActiveForSave", F)!.SetValue(dungeon, true);
        typeof(LocationManager).GetField("currentLocationId", F)!.SetValue(_lm, GameLocation.Dungeons);
        return (dungeon, output);
    }

    /// <summary>A Crown criminal bounty the hero holds, its criminal placed on this test's floor.</summary>
    private Quest HeldCriminal(Character hero)
    {
        var create = typeof(QuestSystem).GetMethod("CreateGenericBounty", SNP)!;
        var q = (Quest)create.Invoke(null, new object[] { "Queen Fbtwo" })!;
        q.Occupier = hero.Name2;
        var o = q.Objectives.Single();
        o.TargetFloor = Floor;
        o.DescriptionKey = QuestSystem.PlacedObjectiveKey;
        o.DescriptionArgs = new List<string> { o.DescriptionArgs[0], Floor.ToString() };
        QuestSystem.AddQuestToDatabase(q);
        _made.Add(q);
        return q;
    }

    /// <summary>Make a room quiet (explored, cleared, no monsters, trap or event) so entering it plays only the hook.</summary>
    private static void Quiet(DungeonRoom room)
    {
        room.IsExplored = true;
        room.IsCleared = true;
        room.HasMonsters = false;
        room.HasTrap = false;
        room.HasEvent = false;
        room.HasTreasure = false;
    }

    private static Task Enter(DungeonLocation dungeon, string roomId) =>
        (Task)typeof(DungeonLocation).GetMethod("MoveToRoom", F)!.Invoke(dungeon, new object[] { roomId })!;

    private static string Text(MemoryStream output) => Encoding.UTF8.GetString(output.ToArray());

    [Fact]
    public async Task EnteringTheTargetsRoom_StartsItsFight_AndTheWinCompletesTheBounty()
    {
        var hero = Fighter("Fbtwo Dungeon Win");
        var q = HeldCriminal(hero);
        var (dungeon, output) = InDungeon(hero);
        var floor = CurrentFloor(dungeon);
        string roomId = QuestSystem.PlacedTargetRoomId(floor, q);
        var room = floor.GetRoom(roomId)!;
        Quiet(room);
        long killsBefore = hero.MKills;

        await Enter(dungeon, roomId);

        string name = QuestSystem.PlacedTargetName(q.Objectives.Single());
        Text(output).Should().Contain(Loc.Get("quest.placed.appears", name), "the target meets the player in its room");
        hero.MKills.Should().BeGreaterThan(killsBefore, "the fight ran and was won");
        q.Objectives.Single().IsComplete.Should().BeTrue("killing the named target completes the bounty");
        Text(output).Should().Contain(Loc.Get("quest.placed.slain", name));
        QuestSystem.FindPlacedTarget(hero, Floor).Should().BeNull();
    }

    [Fact]
    public async Task EnteringAnyOtherRoom_DoesNotStartIt()
    {
        var hero = Fighter("Fbtwo Dungeon Miss");
        var q = HeldCriminal(hero);
        var (dungeon, output) = InDungeon(hero);
        var floor = CurrentFloor(dungeon);
        string roomId = QuestSystem.PlacedTargetRoomId(floor, q);
        var other = floor.Rooms.First(r => r.Id != roomId && !r.IsBossRoom && r.Id != floor.EntranceRoomId);
        Quiet(other);

        await Enter(dungeon, other.Id);

        Text(output).Should().NotContain(Loc.Get("quest.placed.appears", QuestSystem.PlacedTargetName(q.Objectives.Single())));
        q.Objectives.Single().IsComplete.Should().BeFalse();
        QuestSystem.FindPlacedTarget(hero, Floor).Should().NotBeNull("it still waits in its own room");
    }

    [Fact]
    public async Task ArrivingOnTheFloor_SaysTheTargetHidesThere()
    {
        var hero = Fighter("Fbtwo Dungeon Hint");
        var q = HeldCriminal(hero);
        var (dungeon, output) = InDungeon(hero);
        var term = (TerminalEmulator)typeof(BaseLocation).GetField("terminal", F)!.GetValue(dungeon)!;
        await (Task)typeof(DungeonLocation).GetMethod("CheckFloorStoryEvents", F)!.Invoke(dungeon, new object[] { hero, term })!;
        Text(output).Should().Contain(Loc.Get("quest.placed.floor_hint", QuestSystem.PlacedTargetName(q.Objectives.Single())));
    }
}
