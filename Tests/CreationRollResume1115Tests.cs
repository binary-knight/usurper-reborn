using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.15: a character creation stat roll is kept per save key (online mode) until the character is accepted,
/// so a player who drops at the roll and reconnects resumes the same roll with the same rerolls left instead of
/// getting a fresh roll and a fresh count.
/// </summary>
[Collection("SharedGameSingletons")]
public class CreationRollResume1115Tests : IDisposable
{
    private const string Key = "roller";
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-roll-{Guid.NewGuid():N}.db");
    private readonly SqlSaveBackend _db;

    public CreationRollResume1115Tests() { _db = new SqlSaveBackend(_path); }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    private sealed class Session
    {
        public readonly TerminalEmulator Term;
        public readonly MemoryStream Output = new();
        public readonly CharacterCreationSystem Ccs;
        public Session(SqlSaveBackend db, params string[] lines)
        {
            Term = new TerminalEmulator(new LineStream(lines), Output);
            Ccs = new CharacterCreationSystem(Term) { RollStore = db };
        }
        public string Plain()
        {
            Term.StreamWriterInternal?.Flush();
            return Regex.Replace(Encoding.UTF8.GetString(Output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");
        }
    }

    private static Character Hero() => new Character
    {
        Name1 = Key, Name2 = "Roller", Race = CharacterRace.Human, Class = CharacterClass.Warrior, Level = 1, AI = CharacterAI.Human
    };

    /// <summary>Runs the stat roll screen until the scripted input ends (the connection drops) or it returns.</summary>
    private static async Task<bool> RollScreen(Session s, Character c)
    {
        var mi = typeof(CharacterCreationSystem).GetMethod("RollCharacterStats", BindingFlags.NonPublic | BindingFlags.Instance)!;
        try { await (Task)mi.Invoke(s.Ccs, new object[] { c })!; return true; }
        catch (Exception) { return false; }   // the input ran out: the connection dropped
    }

    private CharacterCreationSystem.CreationRoll? Stored()
    {
        var json = _db.LoadCreationRoll(Key);
        return json == null ? null : JsonSerializer.Deserialize<CharacterCreationSystem.CreationRoll>(json);
    }

    private static long[] Stats(Character c) =>
        new[] { c.Strength, c.Defence, c.Stamina, c.Agility, c.Charisma, c.Dexterity, c.Wisdom, c.Intelligence, c.Constitution, c.HP };

    [Fact]
    public async Task EachRoll_IsStoredWithTheRerollsLeft()
    {
        var first = Hero();
        (await RollScreen(new Session(_db, "R", "R"), first)).Should().BeFalse("the input runs out: a dropped connection");
        var stored = Stored();
        stored.Should().NotBeNull();
        stored!.RerollsRemaining.Should().Be(3, "two of the five rerolls were used");
        stored.Dice.Should().HaveCount(9);
    }

    [Fact]
    public async Task Reconnect_ResumesTheSameRoll()
    {
        var first = Hero();
        await RollScreen(new Session(_db, "R", "R"), first);

        var again = Hero();
        var s2 = new Session(_db, "zzz");
        (await RollScreen(s2, again)).Should().BeFalse();
        Stats(again).Should().Equal(Stats(first), "the roll shown before the drop is the roll shown after it");
        s2.Plain().Should().Contain(Loc.Get("character_creation.roll_resumed"));
    }

    [Fact]
    public async Task Reconnect_DoesNotResetTheRerollCount()
    {
        await RollScreen(new Session(_db, "R", "R"), Hero());

        var s2 = new Session(_db, "R");
        await RollScreen(s2, Hero());
        s2.Plain().Should().Contain(Loc.Get("character_creation.rerolls_remaining", "3"));
        Stored()!.RerollsRemaining.Should().Be(2, "the reroll after the reconnect counts against the same five");
    }

    [Fact]
    public async Task NoRerollsLeft_StaysSpentAfterAReconnect()
    {
        await RollScreen(new Session(_db, "R", "R", "R", "R", "R"), Hero());
        Stored()!.RerollsRemaining.Should().Be(0);

        var s2 = new Session(_db, "");
        (await RollScreen(s2, Hero())).Should().BeTrue("with none left the roll is accepted at the pause");
        s2.Plain().Should().Contain(Loc.Get("character_creation.no_rerolls"));
    }

    [Fact]
    public async Task ADifferentClassAfterTheReconnect_KeepsTheDice()
    {
        await RollScreen(new Session(_db, "R"), Hero());
        var dice = Stored()!.Dice;

        var mage = Hero();
        mage.Race = CharacterRace.Elf;
        mage.Class = CharacterClass.Magician;
        await RollScreen(new Session(_db, "zzz"), mage);
        var cls = GameConfig.ClassStartingAttributes[CharacterClass.Magician];
        mage.Agility.Should().Be(dice[2] + cls.Agility, "the stored dice are applied with the new class");
        Stored()!.Dice.Should().Equal(dice);
    }

    [Fact]
    public async Task AcceptingTheStats_KeepsTheRollUntilTheCharacterIsAccepted()
    {
        await RollScreen(new Session(_db, "A"), Hero());
        Stored().Should().NotBeNull("the character is not accepted yet; declining it at the summary must not be a reroll");
    }

    [Fact]
    public async Task QuickStart_UsesTheStoredRoll_AndAcceptingClearsIt()
    {
        var dice = new[] { 10, 11, 12, 13, 14, 15, 16, 17, 7 };
        _db.SaveCreationRoll(Key, JsonSerializer.Serialize(new CharacterCreationSystem.CreationRoll { Dice = dice, RerollsRemaining = 4 }));

        var s = new Session(_db, "Q", "1", "1", "");
        var made = await s.Ccs.CreateNewCharacter(Key);
        made.Should().NotBeNull();
        var cls = GameConfig.ClassStartingAttributes[CharacterClass.Warrior];
        made!.Agility.Should().Be(dice[2] + cls.Agility, "Quick Start after a drop uses the stored roll, not a fresh one");
        _db.LoadCreationRoll(Key).Should().BeNull("the character was accepted");
    }

    [Fact]
    public void EveryAcceptedCharacter_ClearsTheRoll()
    {
        // The custom path is not driven here (ten screens); in source, each return of the finished character
        // in CreateNewCharacter clears the roll, and nothing else does.
        var src = File.ReadAllText(Path.Combine(FloorAndStun1113Tests.RepoRoot(), "Scripts/Systems/CharacterCreationSystem.cs"));
        int start = src.IndexOf("public async Task<Character> CreateNewCharacter(", StringComparison.Ordinal);
        int end = src.IndexOf("private async Task<bool> TryQuickStart(", StringComparison.Ordinal);
        var body = src.Substring(start, end - start);
        var returns = Regex.Matches(body, @"return character;");
        returns.Count.Should().Be(2, "Quick Start and the custom path");
        foreach (Match r in returns)
        {
            var before = body.Substring(0, r.Index);
            before.Substring(before.LastIndexOf("return ", StringComparison.Ordinal))
                .Should().Contain("ClearRoll(character);", "the roll is cleared between the last way out and the return of the character");
        }
        Regex.Matches(src, @"ClearRoll\(character\);").Count.Should().Be(2, "only an accepted character clears the roll");
    }

    [Fact]
    public void OutsideOnlineMode_NoRollIsKept()
    {
        UsurperRemake.BBS.DoorMode.IsOnlineMode.Should().BeFalse();
        new CharacterCreationSystem(new TerminalEmulator(new LineStream(Array.Empty<string>()), new MemoryStream()))
            .RollStore.Should().BeNull("single-player keeps no roll");
    }
}
