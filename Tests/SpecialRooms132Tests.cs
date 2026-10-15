using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;
using Xunit.Abstractions;
using static UsurperReborn.Tests.MentalRecovery1115Tests;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.2: floor generation keeps the special room events. PlaceSpecialRooms gives outposts, lore libraries,
/// meditation chambers, memory rooms and the secret boss vault their own event; PopulateEvents must not
/// overwrite those with one of the seven ordinary random events.
/// </summary>
[Collection("SharedGameSingletons")]
public class SpecialRooms132Tests
{
    private readonly ITestOutputHelper _out;
    public SpecialRooms132Tests(ITestOutputHelper output) => _out = output;

    private static readonly int[] SecretBossFloors = { 25, 50, 75, 99 };
    private static readonly int[] MemoryFloors = { 30, 45, 60, 75, 90 };

    private static readonly DungeonEventType[] Ordinary =
    {
        DungeonEventType.TreasureChest, DungeonEventType.Shrine, DungeonEventType.Trap, DungeonEventType.NPCEncounter,
        DungeonEventType.Puzzle, DungeonEventType.RestSpot, DungeonEventType.MysteryEvent,
    };

    /// <summary>The event the special pass gives this room by its type or its secret boss; None for an ordinary room.</summary>
    private static DungeonEventType SpecialEvent(DungeonRoom room, int level)
    {
        if (room.IsBossRoom) return DungeonEventType.None;
        if (room.HasSecretBoss) return DungeonEventType.SecretBoss;
        return room.Type switch
        {
            RoomType.Settlement when DungeonSettlementData.GetSettlement(level) != null => DungeonEventType.Settlement,
            RoomType.PuzzleRoom => DungeonEventType.Puzzle,
            RoomType.RiddleGate => DungeonEventType.Riddle,
            RoomType.LoreLibrary => DungeonEventType.LoreDiscovery,
            RoomType.MeditationChamber => DungeonEventType.RestSpot,
            RoomType.MerchantDen => DungeonEventType.TreasureChest,
            RoomType.MemoryFragment => DungeonEventType.MemoryFlash,
            _ => DungeonEventType.None,
        };
    }

    // B18. GenerateFloor seeds by floor level only (level * 31337 + 42), so floors 1 to 100 are 100 seeds.
    [Fact]
    public void Every_floor_keeps_the_special_events_its_special_pass_places()
    {
        var placed = new SortedDictionary<DungeonEventType, int>();
        var kept = new SortedDictionary<DungeonEventType, int>();
        var lost = new List<string>();
        var missing = new List<string>();

        for (int level = 1; level <= 100; level++)
        {
            var floor = DungeonGenerator.GenerateFloor(level);

            foreach (var room in floor.Rooms)
            {
                var special = SpecialEvent(room, level);
                if (special == DungeonEventType.None)
                {
                    if (room.HasEvent && !room.IsBossRoom)
                        Ordinary.Should().Contain(room.EventType, $"floor {level} {room.Id} is an ordinary event room");
                    continue;
                }
                room.HasEvent.Should().BeTrue($"floor {level} {room.Id} ({room.Type}) has a special event");
                placed[special] = placed.GetValueOrDefault(special) + 1;
                if (room.EventType == special) kept[special] = kept.GetValueOrDefault(special) + 1;
                else lost.Add($"{level}:{room.Type}->{room.EventType}");
            }

            // the floor rules of the special pass
            int outposts = floor.Rooms.Count(r => r.Type == RoomType.Settlement);
            if (DungeonSettlementData.IsSettlementFloor(level)) { if (outposts != 1) missing.Add($"{level}: outpost"); }
            else if (outposts != 0) missing.Add($"{level}: outpost off its floor");
            if (SecretBossFloors.Contains(level))
            {
                if (!floor.HasSecretBoss || !floor.Rooms.Any(r => r.Id == floor.SecretBossRoomId && r.HasSecretBoss))
                    missing.Add($"{level}: secret boss");
            }
            else if (floor.HasSecretBoss || floor.Rooms.Any(r => r.HasSecretBoss)) missing.Add($"{level}: secret boss off its floor");
            if (MemoryFloors.Contains(level) != floor.Rooms.Any(r => r.Type == RoomType.MemoryFragment))
                missing.Add($"{level}: memory room");
            if (level >= 15 && !floor.Rooms.Any(r => r.Type == RoomType.LoreLibrary)) missing.Add($"{level}: lore library");
            if (level >= 10 && !floor.Rooms.Any(r => r.Type == RoomType.MeditationChamber)) missing.Add($"{level}: meditation chamber");
        }

        string counts = string.Join(", ", placed.Select(p => $"{p.Key} {kept.GetValueOrDefault(p.Key)}/{p.Value}"));
        _out.WriteLine($"kept/placed: {counts}");

        missing.Should().BeEmpty("each special room appears on the floors its rule names");
        placed.Keys.Should().Contain(new[]
        {
            DungeonEventType.Settlement, DungeonEventType.SecretBoss, DungeonEventType.LoreDiscovery,
            DungeonEventType.MemoryFlash, DungeonEventType.RestSpot,
        });
        placed[DungeonEventType.Settlement].Should().Be(DungeonSettlementData.SettlementFloors.Length);
        placed[DungeonEventType.SecretBoss].Should().Be(SecretBossFloors.Length);
        placed[DungeonEventType.MemoryFlash].Should().Be(MemoryFloors.Length);
        lost.Should().BeEmpty($"every special event survives generation (kept/placed: {counts})");
    }

    // ---------- each kept special room opens its own event in DungeonLocation, reached from a generated floor ----------

    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static DungeonLocation Dungeon(Character hero, int floor, MemoryStream output, params string[] lines)
    {
        var term = new TerminalEmulator(new LineStream(lines), output);
        var d = new DungeonLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(d, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(d, hero);
        typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.SetValue(d, floor);
        return d;
    }

    private static Task HandleRoomEvent(DungeonLocation d, DungeonRoom room) =>
        (Task)typeof(DungeonLocation).GetMethod("HandleRoomEvent", F)!.Invoke(d, new object[] { room })!;

    private static DungeonRoom GeneratedRoom(int floor, DungeonEventType type) =>
        DungeonGenerator.GenerateFloor(floor).Rooms.Single(r => r.EventType == type);

    private static string[] Keys(int n) => Enumerable.Repeat("", n).ToArray();

    [Fact]
    public async Task The_outpost_room_opens_the_outpost()
    {
        var settlement = DungeonSettlementData.GetSettlement(10)!;
        var room = GeneratedRoom(10, DungeonEventType.Settlement);
        room.Type.Should().Be(RoomType.Settlement);
        var hero = Hero("Visitor");
        var output = new MemoryStream();

        await HandleRoomEvent(Dungeon(hero, 10, output, "R", "", ""), room);

        hero.VisitedSettlements.Should().Contain(settlement.Id, "SettlementEncounter records the first visit");
        Text(output).Should().Contain(settlement.Name);
    }

    [Fact]
    public async Task The_lore_library_room_opens_the_lore_library()
    {
        var ocean = OceanPhilosophySystem.Instance;
        var saved = ocean.Serialize();
        try
        {
            var room = GeneratedRoom(15, DungeonEventType.LoreDiscovery);
            room.Type.Should().Be(RoomType.LoreLibrary);
            var output = new MemoryStream();

            await HandleRoomEvent(Dungeon(Hero("Reader"), 15, output, Keys(5)), room);

            Text(output).Should().Contain(Loc.Get("dungeon.lore_library_header"));
            room.EventCompleted.Should().BeTrue();
        }
        finally { ocean.Deserialize(saved); }
    }

    [Fact]
    public async Task The_memory_room_opens_the_memory_fragment()
    {
        var room = GeneratedRoom(30, DungeonEventType.MemoryFlash);
        room.Type.Should().Be(RoomType.MemoryFragment);
        var output = new MemoryStream();

        await HandleRoomEvent(Dungeon(Hero("Dreamer"), 30, output, Keys(5)), room);

        Text(output).Should().Contain(Loc.Get("dungeon.memory_chamber_header"));
        room.EventCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task The_secret_boss_room_opens_the_secret_boss_encounter()
    {
        StoryProgressionSystem.Instance.SetStoryFlag("memory_flash_pending", false);
        var floor = DungeonGenerator.GenerateFloor(75);
        var room = floor.Rooms.Single(r => r.EventType == DungeonEventType.SecretBoss);
        room.Id.Should().Be(floor.SecretBossRoomId);
        room.SecretBossType.Should().Be(SecretBossType.EchoOfSelf);
        var output = new MemoryStream();

        // Mental 0: the encounter starts and the fight is not entered, so no combat input is needed
        await HandleRoomEvent(Dungeon(Hero("Seeker", 0), 75, output, Keys(10)), room);

        Text(output).Should().Contain(Loc.Get("dungeon.secret_boss_remains"));
        room.EventCompleted.Should().BeFalse("only a win spends the secret boss");
    }
}
