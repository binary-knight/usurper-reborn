using System.Text.Json;
using Xunit;
using FluentAssertions;
using UsurperRemake.Data;
using UsurperRemake.Systems;

namespace UsurperReborn.Tests;

/// <summary>
/// Unit tests for GameDataLoader and the moddable game data system.
/// Verifies built-in data sources, balance config, and JSON serialization options.
/// </summary>
public class GameDataLoaderTests
{
    #region Built-in Data Source Tests

    [Fact]
    public void GetBuiltInNPCs_Returns59NPCs()
    {
        // ClassicNPCs.GetClassicNPCs() checks GameDataLoader.NPCs first;
        // with no GameData/ directory, it returns the hardcoded 59 NPCs.
        var npcs = ClassicNPCs.GetClassicNPCs();

        npcs.Should().NotBeNull();
        npcs.Should().HaveCount(59);
    }

    [Fact]
    public void GetBuiltInMonsterFamilies_Returns15Families()
    {
        var families = MonsterFamilies.AllFamilies;

        families.Should().NotBeNull();
        families.Should().HaveCount(15);
    }

    [Fact]
    public void GetBuiltInDreams_ReturnsNonEmpty()
    {
        var dreams = DreamSystem.GetBuiltInDreams();

        dreams.Should().NotBeNull();
        dreams.Should().NotBeEmpty();
    }

    [Fact]
    public void GetBuiltInAchievements_ReturnsNonEmpty()
    {
        var achievements = AchievementSystem.GetBuiltInAchievements();

        achievements.Should().NotBeNull();
        achievements.Should().NotBeEmpty();
    }

    [Fact]
    public void GetBuiltInDialogue_ReturnsNonEmpty()
    {
        var lines = NPCDialogueDatabase.GetAllBuiltInLines();

        lines.Should().NotBeNull();
        lines.Should().NotBeEmpty();
    }

    #endregion

    #region BalanceConfig Tests

    [Fact]
    public void BalanceConfig_DefaultsMatchGameConfig()
    {
        var config = new BalanceConfig();

        config.CriticalHitChance.Should().Be(GameConfig.CriticalHitChance);
        config.CriticalHitMultiplier.Should().Be(GameConfig.CriticalHitMultiplier);
        config.BackstabMultiplier.Should().Be(GameConfig.BackstabMultiplier);
        config.BerserkMultiplier.Should().Be(GameConfig.BerserkMultiplier);
        config.BossPotionCooldownRounds.Should().Be(GameConfig.BossPotionCooldownRounds);
        config.DefaultGymSessions.Should().Be(GameConfig.DefaultGymSessions);
        config.DefaultDrinksAtOrbs.Should().Be(GameConfig.DefaultDrinksAtOrbs);
    }

    [Fact]
    public void BalanceConfig_ApplyToGameConfig_SetsModValues()
    {
        var config = new BalanceConfig
        {
            CriticalHitChance = 25,
            DefaultGymSessions = 10
        };

        config.ApplyToGameConfig();

        GameConfig.ModCriticalHitChance.Should().Be(25);
        GameConfig.ModDefaultGymSessions.Should().Be(10);

        // Reset to defaults so other tests are not affected
        var defaults = new BalanceConfig();
        defaults.ApplyToGameConfig();
    }

    [Fact]
    public void BalanceConfig_ApplyToGameConfig_ClampsOutOfRange()
    {
        var config = new BalanceConfig
        {
            CriticalHitChance = 999  // max is 100
        };

        config.ApplyToGameConfig();

        GameConfig.ModCriticalHitChance.Should().Be(100);

        // Reset to defaults
        var defaults = new BalanceConfig();
        defaults.ApplyToGameConfig();
    }

    #endregion

    #region JSON Serialization Tests

    [Fact]
    public void JsonOptions_SerializesEnumsAsStrings()
    {
        var npc = new NPCTemplate
        {
            Name = "Test NPC",
            Class = CharacterClass.Warrior,
            Race = CharacterRace.Human
        };

        var json = JsonSerializer.Serialize(npc, GameDataLoader.JsonOptions);

        // Enum values should be written as strings, not numeric.
        // TolerantEnumConverter writes the enum name via .ToString() (PascalCase).
        json.Should().Contain("\"Warrior\"");
        json.Should().Contain("\"Human\"");
        json.Should().NotContain(":10", "Warrior (enum 10) should not appear as a number");
    }

    [Fact]
    public void JsonOptions_DeserializesEnumsFromStrings()
    {
        var json = """{"name":"Test NPC","class":"Warrior","race":"Human","personality":"","alignment":""}""";

        var npc = JsonSerializer.Deserialize<NPCTemplate>(json, GameDataLoader.JsonOptions);

        npc.Should().NotBeNull();
        npc!.Class.Should().Be(CharacterClass.Warrior);
        npc.Race.Should().Be(CharacterRace.Human);
    }

    #endregion
}
