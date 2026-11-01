using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using UsurperReborn.Tests.Localization;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.9: the dungeon room view sends GMCP Room.Info with the room's exits every time it is drawn,
/// not only when the player walks through a door. A level's first room, a teleport, a load and the
/// redraw after a fight all reach the client with the exits; the floor overview sends none.
/// </summary>
[Collection("SharedGameSingletons")]
public class RoomInfo139Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static DungeonRoom Room(string id) => new()
    {
        Id = id, Name = "Room " + id, Description = "A room.", DangerRating = 1, IsExplored = true, IsCleared = true,
    };

    private static DungeonLocation Dungeon(bool inRoomMode)
    {
        var entrance = Room("r1");
        entrance.Exits[Direction.North] = new RoomExit("r2", "a door");
        entrance.Exits[Direction.East] = new RoomExit("r3", "a hall");
        var floor = new DungeonFloor { Level = 6, Theme = DungeonTheme.Catacombs, CurrentRoomId = "r1", EntranceRoomId = "r1" };
        floor.Rooms.Add(entrance);
        var north = Room("r2");
        north.Exits[Direction.South] = new RoomExit("r1", "a door");
        floor.Rooms.Add(north);
        floor.Rooms.Add(Room("r3"));
        var hero = new Character
        {
            Name1 = "tester", Name2 = "Tester", ID = "ri139_hero", Class = CharacterClass.Warrior, Level = 20, HP = 900, MaxHP = 900,
            AI = CharacterAI.Human,
        };
        var d = new DungeonLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(d, new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(d, hero);
        typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.SetValue(d, 6);
        typeof(DungeonLocation).GetField("currentFloor", F)!.SetValue(d, floor);
        typeof(DungeonLocation).GetField("inRoomMode", F)!.SetValue(d, inRoomMode);
        return d;
    }

    /// <summary>Draws the dungeon view once with a GMCP session of the given state and returns the GMCP bytes.</summary>
    private static string Draw(DungeonLocation d, bool gmcp) =>
        WithGmcp(gmcp, () => { typeof(DungeonLocation).GetMethod("DisplayLocation", F)!.Invoke(d, null); return Task.CompletedTask; }).GetAwaiter().GetResult();

    /// <summary>Runs an action inside a session with GMCP on or off and returns the GMCP bytes it sent.</summary>
    private static async Task<string> WithGmcp(bool gmcp, Func<Task> act)
    {
        var saved = UsurperRemake.Server.SessionContext.Current;
        var sink = new MemoryStream();
        var ctx = new UsurperRemake.Server.SessionContext { Username = "ri139", CharacterKey = "ri139", OutputStream = sink, GmcpEnabled = gmcp };
        UsurperRemake.Server.SessionContext.Current = ctx;
        try
        {
            await act();
            return Encoding.UTF8.GetString(sink.ToArray());
        }
        finally { UsurperRemake.Server.SessionContext.Current = saved; }
    }

    [Fact]
    public void TheRoomView_SendsRoomInfo_WithTheRoomsExits()
    {
        var frames = Draw(Dungeon(inRoomMode: true), gmcp: true);
        frames.Should().Contain("Room.Info", "the room view tells the client where it is");
        frames.Should().Contain("\"num\":\"r1\"");
        frames.Should().Contain("\"north\":\"r2\"");
        frames.Should().Contain("\"east\":\"r3\"");
        frames.Should().NotContain("\"south\"", "only the room's own exits are sent");
        frames.Should().Contain("Dungeon Floor 6");
    }

    [Fact]
    public void TheRoomView_SendsRoomInfo_EachTimeItIsDrawn()
    {
        var d = Dungeon(inRoomMode: true);
        Draw(d, gmcp: true).Should().Contain("Room.Info");
        Draw(d, gmcp: true).Should().Contain("Room.Info", "the redraw after a fight or a menu carries the exits again");
    }

    [Fact]
    public void TheFloorOverview_SendsNoRoomInfo()
    {
        Draw(Dungeon(inRoomMode: false), gmcp: true).Should().NotContain("Room.Info", "the overview is not a room");
    }

    [Fact]
    public void WithoutGmcp_NothingIsSent()
    {
        Draw(Dungeon(inRoomMode: true), gmcp: false).Should().BeEmpty("GMCP was not negotiated");
    }

    [Fact]
    public async Task AMove_StillSendsRoomInfo_ForTheNewRoom()
    {
        var d = Dungeon(inRoomMode: true);
        var frames = await WithGmcp(true, async () =>
        {
            var res = typeof(DungeonLocation).GetMethod("MoveToRoom", F)!.Invoke(d, new object[] { "r2" });
            await (Task)res!;
        });
        frames.Should().Contain("Room.Info", "the client sees the room at the threshold");
        frames.Should().Contain("\"num\":\"r2\"");
        frames.Should().Contain("\"south\":\"r1\"");
    }

    [Fact]
    public void TheMoveAndTheRoomView_ShareOneRoomInfoPayload()
    {
        var src = File.ReadAllText(Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Scripts", "Locations", "DungeonLocation.cs"));
        int first = src.IndexOf("\"Room.Info\"", StringComparison.Ordinal);
        first.Should().BeGreaterThan(0);
        src.IndexOf("\"Room.Info\"", first + 1, StringComparison.Ordinal).Should().Be(-1, "one helper builds the dungeon Room.Info payload");
        src.Should().Contain("EmitRoomInfo(targetRoom);", "the move sends through the helper");
        src.Should().Contain("EmitRoomInfo(room);", "the room view sends through the helper");
    }
}
