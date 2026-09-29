using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.15 piece 2: Mental save plumbing. MentalSchema, MentalStrainRemainder, WillowDraughts,
/// MentalRecoveryUsedToday, MentalBroken, MentalHintShown and the drug-tolerance shape, through
/// the five save sites (Character, PlayerData, SaveSystem, GameEngine restore, PlayerSaveEditor).
/// No call sites and no gameplay wiring are exercised here; that is later pieces.
/// </summary>
[Collection("SharedGameSingletons")]
public class MentalSaves1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static PlayerData Serialize(Character c)
    {
        var method = typeof(SaveSystem).GetMethod("SerializePlayer", F)!;
        return (PlayerData)method.Invoke(SaveSystem.Instance, new object[] { c })!;
    }

    private static Character Restore(PlayerData data) => MenuKeysNeedEnterPref1115Tests.Restore(data);

    [Fact]
    public void MentalStrainRemainder_NoLongerHasJsonIgnore()
    {
        var prop = typeof(Character).GetProperty(nameof(Character.MentalStrainRemainder))!;
        prop.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonIgnoreAttribute), false)
            .Should().BeEmpty("piece 2 saves it through the five sites instead of ignoring it");
    }

    [Fact]
    public void AllNewMentalFields_RoundTripThroughSerializeAndRestore()
    {
        var c = new Character
        {
            Name1 = "Round", Name2 = "Round", AI = CharacterAI.Human, Level = 5, HP = 50, MaxHP = 50,
            Mental = 40,
            MentalSchema = GameConfig.MentalSchemaCurrent,
            MentalStrainRemainder = 12_345,
            WillowDraughts = 2,
            MentalRecoveryUsedToday = MentalDailySource.InnTable | MentalDailySource.Spouse,
            MentalBroken = true,
            MentalHintShown = true,
            MentalDrugBoost = 15,
            MentalDrugUses = 3,
            MentalLastDrugDay = 42,
        };

        var data = Serialize(c);
        data.Mental.Should().Be(40);
        data.MentalSchema.Should().Be(GameConfig.MentalSchemaCurrent);
        data.MentalStrainRemainder.Should().Be(12_345);
        data.WillowDraughts.Should().Be(2);
        data.MentalRecoveryUsedToday.Should().Be((int)(MentalDailySource.InnTable | MentalDailySource.Spouse));
        data.MentalBroken.Should().BeTrue();
        data.MentalHintShown.Should().BeTrue();
        data.MentalDrugBoost.Should().Be(15);
        data.MentalDrugUses.Should().Be(3);
        data.MentalLastDrugDay.Should().Be(42);

        var json = JsonSerializer.Serialize(data);
        var back = JsonSerializer.Deserialize<PlayerData>(json)!;
        var restored = Restore(back);

        restored.Mental.Should().Be(40);
        restored.MentalSchema.Should().Be(GameConfig.MentalSchemaCurrent);
        restored.MentalStrainRemainder.Should().Be(12_345);
        restored.WillowDraughts.Should().Be(2);
        restored.MentalRecoveryUsedToday.Should().Be(MentalDailySource.InnTable | MentalDailySource.Spouse);
        restored.MentalBroken.Should().BeTrue();
        restored.MentalHintShown.Should().BeTrue();
        restored.MentalDrugBoost.Should().Be(15);
        restored.MentalDrugUses.Should().Be(3);
        restored.MentalLastDrugDay.Should().Be(42);
    }

    [Fact]
    public void Serialize_AlwaysWritesTheCurrentSchema_RegardlessOfTheCharacters()
    {
        var c = new Character { Name1 = "Stale", Name2 = "Stale", AI = CharacterAI.Human, MentalSchema = 0 };
        Serialize(c).MentalSchema.Should().Be(GameConfig.MentalSchemaCurrent, "the save always writes the live schema");
    }

    [Fact]
    public void AnEmptySave_ReadsAsLegacy_AndResetsMentalOnRestore()
    {
        var empty = JsonSerializer.Deserialize<PlayerData>("{}")!;
        empty.MentalSchema.Should().Be(0, "a save written before the field existed reads as schema 0");
        empty.WillowDraughts.Should().Be(0);
        empty.MentalBroken.Should().BeFalse();

        var restored = Restore(empty);
        restored.Mental.Should().Be(GameConfig.DefaultMentalHealth, "schema 0 never had a real Mental value");
        restored.MentalSchema.Should().Be(GameConfig.MentalSchemaCurrent, "the load stamps the current schema");
    }

    [Fact]
    public void SchemaGuard_ResetsOnlyLegacySaves()
    {
        var legacy = new PlayerData { Mental = 40 };
        legacy.MentalSchema.Should().Be(0);
        var restoredLegacy = Restore(legacy);
        restoredLegacy.Mental.Should().Be(GameConfig.DefaultMentalHealth, "schema 0 never had a real Mental value");
        restoredLegacy.MentalSchema.Should().Be(GameConfig.MentalSchemaCurrent);

        var current = new PlayerData { Mental = 40, MentalSchema = GameConfig.MentalSchemaCurrent };
        var restoredCurrent = Restore(current);
        restoredCurrent.Mental.Should().Be(40, "a schema-current save keeps its Mental");
        restoredCurrent.MentalSchema.Should().Be(GameConfig.MentalSchemaCurrent);
    }

    [Fact]
    public void Restore_ClampsWillowDraughtsToTheRange()
    {
        Restore(new PlayerData { WillowDraughts = 99 }).WillowDraughts.Should().Be(GameConfig.MaxWillowDraughts);
        Restore(new PlayerData { WillowDraughts = -5 }).WillowDraughts.Should().Be(0);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(3, 3)]
    [InlineData(4, 3)]
    [InlineData(100, 3)]
    public void ClampWillowDraughts_KeepsWithinRange(int input, int expected) =>
        GameConfig.ClampWillowDraughts(input).Should().Be(expected);

    [Fact]
    public void ApplyDailyReset_ClearsTheRecoveryFlags()
    {
        var c = new Character
        {
            Name1 = "D", Name2 = "D", AI = CharacterAI.Human, Mental = 50,
            MentalRecoveryUsedToday = MentalDailySource.InnTable | MentalDailySource.Learning,
        };
        MentalSystem.ApplyDailyReset(c);
        c.MentalRecoveryUsedToday.Should().Be(MentalDailySource.None);
    }

    [Fact]
    public void ApplyDailyReset_SkipsNpcs_AndLeavesTheirFlags()
    {
        var n = new Character
        {
            Name1 = "N", Name2 = "N", AI = CharacterAI.Computer, Mental = 50,
            MentalRecoveryUsedToday = MentalDailySource.Spouse,
        };
        MentalSystem.ApplyDailyReset(n).Should().Be(0);
        n.MentalRecoveryUsedToday.Should().Be(MentalDailySource.Spouse);
    }

    [Fact]
    public void MarkUsed_SetsOnlyThatBit()
    {
        var c = new Character { Name1 = "M", Name2 = "M", AI = CharacterAI.Human };
        MentalSystem.UsedToday(c, MentalDailySource.Wilderness).Should().BeFalse();
        MentalSystem.MarkUsed(c, MentalDailySource.Wilderness);
        MentalSystem.UsedToday(c, MentalDailySource.Wilderness).Should().BeTrue();
        MentalSystem.UsedToday(c, MentalDailySource.Learning).Should().BeFalse();
    }

    [Fact]
    public void MarkUsed_MultipleSourcesAreIndependent()
    {
        var c = new Character { Name1 = "M2", Name2 = "M2", AI = CharacterAI.Human };
        MentalSystem.MarkUsed(c, MentalDailySource.InnTable);
        MentalSystem.MarkUsed(c, MentalDailySource.Confession);
        MentalSystem.UsedToday(c, MentalDailySource.InnTable).Should().BeTrue();
        MentalSystem.UsedToday(c, MentalDailySource.Confession).Should().BeTrue();
        MentalSystem.UsedToday(c, MentalDailySource.FriendTalk).Should().BeFalse();
        c.MentalRecoveryUsedToday.Should().Be(MentalDailySource.InnTable | MentalDailySource.Confession);
    }

    [Fact]
    public void UsedTodayAndMarkUsed_AreNullSafe()
    {
        MentalSystem.UsedToday(null!, MentalDailySource.Spouse).Should().BeFalse();
        var act = () => MentalSystem.MarkUsed(null!, MentalDailySource.Spouse);
        act.Should().NotThrow();
    }

    [Fact]
    public void NewCharacter_StartsAtTheCurrentMentalSchema()
    {
        var term = new TerminalEmulator(new MemoryStream(), new MemoryStream());
        var ccs = new CharacterCreationSystem(term);
        var method = typeof(CharacterCreationSystem).GetMethod("CreateBaseCharacter", F)!;
        var c = (Character)method.Invoke(ccs, new object[] { "Newbie" })!;
        c.Mental.Should().Be(GameConfig.DefaultMentalHealth);
        c.MentalSchema.Should().Be(GameConfig.MentalSchemaCurrent);
    }

    [Fact]
    public void Editor_HasEveryNewFieldWithItsClamp()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj")))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test must be able to find the repo root");
        var src = File.ReadAllText(Path.Combine(dir!.FullName, "Scripts", "Editor", "PlayerSaveEditor.cs"));

        src.Should().Contain("p.MentalSchema = EditorIO.PromptInt(");
        src.Should().Contain("p.MentalStrainRemainder = EditorIO.PromptInt(");
        src.Should().Contain(
            "p.WillowDraughts = EditorIO.PromptInt($\"Willow Draughts carried (0-{GameConfig.MaxWillowDraughts})\", p.WillowDraughts, min: 0, max: GameConfig.MaxWillowDraughts);",
            "the carried count must stay clamped to [0, MaxWillowDraughts]");
        src.Should().Contain("p.MentalRecoveryUsedToday = EditorIO.PromptInt(");
        src.Should().Contain("p.MentalBroken = EditorIO.PromptBool(");
        src.Should().Contain("p.MentalHintShown = EditorIO.PromptBool(");
        src.Should().Contain("p.MentalDrugBoost = EditorIO.PromptInt(");
        src.Should().Contain("p.MentalDrugUses = EditorIO.PromptInt(");
        src.Should().Contain("p.MentalLastDrugDay = EditorIO.PromptInt(");
    }
}
