using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.12: the one rest per dungeon floor is kept in the floor's saved state, so it
/// survives a save and restore, and resets only when the floor is regenerated fresh.
/// </summary>
[Collection("SharedGameSingletons")]
public class DungeonRestPersistence1112Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int Floor = 1;

    private static readonly JsonSerializerOptions SaveJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IncludeFields = true,
        MaxDepth = 256
    };

    private static Character Hero() => new()
    {
        Name1 = "rest", Name2 = "Rest", Class = CharacterClass.Warrior, Level = 8, HP = 100, MaxHP = 500,
        AI = CharacterAI.Human
    };

    private static (DungeonLocation dungeon, TerminalEmulator term, MemoryStream output) Dungeon(Character hero)
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes("\n\n\n\n\n\n")), output);
        var dungeon = new DungeonLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(dungeon, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(dungeon, hero);
        typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.SetValue(dungeon, Floor);
        return (dungeon, term, output);
    }

    private static string Shown(TerminalEmulator term, MemoryStream output)
    {
        term.StreamWriterInternal?.Flush();
        return Encoding.UTF8.GetString(output.ToArray());
    }

    /// <summary>Enter the floor the way the dungeon does: generate or restore, then take the rest flag from the result.</summary>
    private static (bool wasRestored, bool didRespawn) EnterFloor(DungeonLocation dungeon, Character hero)
    {
        object result = typeof(DungeonLocation).GetMethod("GenerateOrRestoreFloor", F)!.Invoke(dungeon, new object[] { hero, Floor })!;
        var t = result.GetType();
        typeof(DungeonLocation).GetField("currentFloor", F)!.SetValue(dungeon, t.GetField("Floor")!.GetValue(result));
        typeof(DungeonLocation).GetField("hasCampedThisFloor", F)!.SetValue(dungeon, (bool)t.GetField("RestedOnThisFloor")!.GetValue(result)!);
        return ((bool)t.GetField("WasRestored")!.GetValue(result)!, (bool)t.GetField("DidRespawn")!.GetValue(result)!);
    }

    private static async Task<string> Sanctuary(DungeonLocation dungeon, TerminalEmulator term, MemoryStream output)
    {
        await (Task)typeof(DungeonLocation).GetMethod("RestSpotEncounter", F)!.Invoke(dungeon, null)!;
        return Shown(term, output);
    }

    /// <summary>The real save and restore of the floor states, through the save JSON.</summary>
    private static Dictionary<int, DungeonFloorState> SaveAndRestore(Character hero)
    {
        var saved = (Dictionary<int, DungeonFloorStateData>)typeof(SaveSystem).GetMethod("SerializeDungeonFloorStates", F)!
            .Invoke(SaveSystem.Instance, new object[] { hero })!;
        string json = JsonSerializer.Serialize(saved, SaveJson);
        var loaded = JsonSerializer.Deserialize<Dictionary<int, DungeonFloorStateData>>(json, SaveJson)!;
        return (Dictionary<int, DungeonFloorState>)typeof(GameEngine).GetMethod("RestoreDungeonFloorStates", F)!
            .Invoke(GameEngine.Instance, new object[] { loaded })!;
    }

    [Fact]
    public async Task ARestOnAFloor_SurvivesSaveAndRestore_AndTheSecondRestIsRefused()
    {
        var hero = Hero();
        var (dungeon, term, output) = Dungeon(hero);
        EnterFloor(dungeon, hero).wasRestored.Should().BeFalse("the floor has no saved state yet");

        string first = await Sanctuary(dungeon, term, output);
        first.Should().Contain(Loc.Get("dungeon.sanctuary_camp"));
        hero.DungeonFloorStates[Floor].RestedOnThisFloor.Should().BeTrue("the rest is written to the floor's saved state");

        // log out and back in: a new character object and a new dungeon, from the save
        var back = Hero();
        back.DungeonFloorStates = SaveAndRestore(hero);
        var (dungeon2, term2, output2) = Dungeon(back);
        var entry = EnterFloor(dungeon2, back);
        entry.wasRestored.Should().BeTrue();
        entry.didRespawn.Should().BeFalse();

        long hpBefore = back.HP;
        string second = await Sanctuary(dungeon2, term2, output2);
        second.Should().Contain(Loc.Get("dungeon.sanctuary_already_rested"));
        second.Should().NotContain(Loc.Get("dungeon.sanctuary_camp"));
        back.HP.Should().Be(hpBefore, "the second rest on the same floor is refused");
    }

    [Fact]
    public async Task AFreshlyGeneratedFloor_AllowsARest()
    {
        var hero = Hero();
        var (dungeon, term, output) = Dungeon(hero);
        EnterFloor(dungeon, hero).wasRestored.Should().BeFalse();

        long hpBefore = hero.HP;
        string shown = await Sanctuary(dungeon, term, output);
        shown.Should().Contain(Loc.Get("dungeon.sanctuary_camp"));
        hero.HP.Should().BeGreaterThan(hpBefore);
    }

    [Fact]
    public async Task ARespawnedFloor_AllowsARestAgain()
    {
        var hero = Hero();
        hero.DungeonFloorStates[Floor] = new DungeonFloorState
        {
            FloorLevel = Floor,
            LastVisitedAt = DateTime.Now.AddHours(-(DungeonFloorState.RESPAWN_HOURS + 1)),
            RestedOnThisFloor = true
        };
        hero.DungeonFloorStates = SaveAndRestore(hero);
        var (dungeon, term, output) = Dungeon(hero);
        var entry = EnterFloor(dungeon, hero);
        entry.wasRestored.Should().BeTrue();
        entry.didRespawn.Should().BeTrue();
        hero.DungeonFloorStates[Floor].RestedOnThisFloor.Should().BeFalse("a respawn regenerates the floor");

        string shown = await Sanctuary(dungeon, term, output);
        shown.Should().Contain(Loc.Get("dungeon.sanctuary_camp"));
    }

    [Fact]
    public void ARestoredFloorThatDidNotRespawn_KeepsTheRest()
    {
        var hero = Hero();
        hero.DungeonFloorStates[Floor] = new DungeonFloorState
        {
            FloorLevel = Floor,
            LastVisitedAt = DateTime.Now.AddMinutes(-5),
            RestedOnThisFloor = true
        };
        var (dungeon, _, _) = Dungeon(hero);
        EnterFloor(dungeon, hero).didRespawn.Should().BeFalse();
        ((bool)typeof(DungeonLocation).GetField("hasCampedThisFloor", F)!.GetValue(dungeon)!)
            .Should().BeTrue("leaving the floor and coming back keeps the spent rest");
    }

    [Fact]
    public void NoFloorEntry_ClearsTheRestWithoutTheSavedState()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        string src = File.ReadAllText(Path.Combine(dir!.FullName, "Scripts", "Locations", "DungeonLocation.cs"));
        System.Text.RegularExpressions.Regex.IsMatch(src, @"(?<!bool )hasCampedThisFloor = false;")
            .Should().BeFalse("every floor entry takes the flag from the floor's saved state");
    }
}
