using Xunit;
using FluentAssertions;

namespace UsurperReborn.Tests;

/// <summary>
/// Unit tests for GameConfig constants and configuration
/// </summary>
public class GameConfigTests
{
    [Fact]
    public void GameConfig_HasValidVersion()
    {
        GameConfig.Version.Should().NotBeNullOrEmpty();
        GameConfig.Version.Should().MatchRegex(@"\d+\.\d+");
    }

    [Fact]
    public void GameConfig_HasVersionName()
    {
        GameConfig.VersionName.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GameConfig_PlayerLimits_AreReasonable()
    {
        GameConfig.MaxPlayers.Should().BeGreaterThan(0);
        GameConfig.MaxTeamMembers.Should().BeGreaterThan(0);
        GameConfig.MaxLevel.Should().BeGreaterThanOrEqualTo(100);
    }

    [Fact]
    public void GameConfig_CombatSettings_ArePositive()
    {
        GameConfig.CriticalHitChance.Should().BeGreaterThan(0);
        GameConfig.CriticalHitMultiplier.Should().BeGreaterThan(1.0f);
        GameConfig.BackstabMultiplier.Should().BeGreaterThan(1.0f);
        GameConfig.BerserkMultiplier.Should().BeGreaterThan(1.0f);
    }

    [Fact]
    public void GameConfig_InventoryLimits_AreReasonable()
    {
        GameConfig.MaxInventoryItems.Should().BeGreaterThan(10);
        GameConfig.MaxItems.Should().BeGreaterThan(100);
        GameConfig.MaxWeapons.Should().BeGreaterThan(10);
        GameConfig.MaxArmor.Should().BeGreaterThan(10);
    }

    [Fact]
    public void GameConfig_DungeonSettings_AreReasonable()
    {
        GameConfig.MaxLevels.Should().BeGreaterThan(10);
        GameConfig.MaxMonsters.Should().BeGreaterThan(10);
    }

    [Fact]
    public void GameConfig_SpellSettings_AreReasonable()
    {
        GameConfig.MaxSpells.Should().BeGreaterThan(10);
        GameConfig.MaxClasses.Should().BeGreaterThan(5);
        GameConfig.MaxRaces.Should().BeGreaterThan(5);
    }

    [Fact]
    public void GameConfig_DailyLimits_ArePositive()
    {
        GameConfig.DefaultGymSessions.Should().BeGreaterThan(0);
        GameConfig.DefaultDrinksAtOrbs.Should().BeGreaterThan(0);
        GameConfig.DefaultIntimacyActs.Should().BeGreaterThan(0);
        GameConfig.DefaultPickPocketAttempts.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GameConfig_TurnsPerDay_IsReasonable()
    {
        GameConfig.TurnsPerDay.Should().BeGreaterThan(100);
    }

    [Fact]
    public void GameConfig_MaxChildren_IsPositive()
    {
        GameConfig.MaxChildren.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GameConfig_DataPath_IsSet()
    {
        GameConfig.DataPath.Should().NotBeNullOrEmpty();
    }

    // v0.62.1: indefinite-article helper for English encounter messages.
    // Player report on Steam (Lv.15 Dwarf Barbarian) flagged "A Ooze", "A Imp",
    // "A Ossuary Priest" -- hardcoded article ignoring vowel rules.

    [Theory]
    [InlineData("Ooze", "An")]
    [InlineData("Imp", "An")]
    [InlineData("Orc", "An")]
    [InlineData("Elf", "An")]
    [InlineData("Ogre", "An")]
    [InlineData("Archer", "An")]
    [InlineData("Ancient Dragon", "An")]
    [InlineData("Ossuary Priest", "An")]
    [InlineData("Wolf", "A")]
    [InlineData("Dwarf", "A")]
    [InlineData("Hobgoblin", "A")]
    [InlineData("Troll", "A")]
    [InlineData("Goblin", "A")]
    [InlineData("Bandit", "A")]
    public void GetIndefiniteArticle_HandlesVowelAndConsonantInitials(string noun, string expected)
    {
        GameConfig.GetIndefiniteArticle(noun).Should().Be(expected);
    }

    [Theory]
    [InlineData("honest mistake", "An")]
    [InlineData("hour-long fight", "An")]
    [InlineData("heir of the throne", "An")]
    [InlineData("honor guard", "An")]
    public void GetIndefiniteArticle_SilentHReturnsAn(string noun, string expected)
    {
        GameConfig.GetIndefiniteArticle(noun).Should().Be(expected);
    }

    [Theory]
    [InlineData("user", "A")]
    [InlineData("uniform", "A")]
    [InlineData("unicorn", "A")]
    [InlineData("one-eyed bandit", "A")]
    [InlineData("European", "A")]
    public void GetIndefiniteArticle_YuSoundReturnsA(string noun, string expected)
    {
        GameConfig.GetIndefiniteArticle(noun).Should().Be(expected);
    }

    [Theory]
    [InlineData("Ooze", "an")]
    [InlineData("Wolf", "a")]
    public void GetIndefiniteArticle_LowercaseFormWorks(string noun, string expected)
    {
        GameConfig.GetIndefiniteArticle(noun, capitalize: false).Should().Be(expected);
    }

    [Fact]
    public void GetIndefiniteArticle_SkipsColorMarkupWrapper()
    {
        // Combat messages prefix monster names with "[color]Name[/]" markup.
        // The helper must read past the markup to the real first letter.
        GameConfig.GetIndefiniteArticle("[red]Ooze[/]").Should().Be("An");
        GameConfig.GetIndefiniteArticle("[bright_red]Wolf[/]").Should().Be("A");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void GetIndefiniteArticle_EmptyInputReturnsEmpty(string? noun)
    {
        GameConfig.GetIndefiniteArticle(noun).Should().Be("");
    }

    [Fact]
    public void ArticulateForLanguage_EnglishPrependsArticle()
    {
        GameConfig.Language = "en";
        GameConfig.ArticulateForLanguage("Ooze").Should().Be("An Ooze");
        GameConfig.ArticulateForLanguage("Wolf").Should().Be("A Wolf");
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("it")]
    public void ArticulateForLanguage_NonEnglishReturnsBareNoun(string lang)
    {
        // Non-English languages have their own article rules baked into the loc
        // template (un / una, az / a, etc.). The helper must NOT prepend an
        // English article when those templates run.
        GameConfig.Language = lang;
        try
        {
            GameConfig.ArticulateForLanguage("Ooze").Should().Be("Ooze");
            GameConfig.ArticulateForLanguage("Wolf").Should().Be("Wolf");
        }
        finally
        {
            GameConfig.Language = "en";
        }
    }

    // v0.62.1 stat-order consistency. Player report (Lv.6 Human Sage):
    // "current might say Int, Def, Wis. New might list Def, Int, Wis. Hard to
    // visually compare." Verify the alphabetical Ordinal sort the comparison
    // and shop renderers now apply produces a stable, predictable order
    // regardless of which subset of stats the items carry.

    [Fact]
    public void StatBonusList_OrdinalSort_GivesPredictableOrder()
    {
        // Simulate two items with the same stat keys in different insertion
        // orders -- matches the bug shape from CombatEngine.ShowEquipmentComparison
        // (current item built Str/Dex/Agi/Con/Int/Wis/Cha/HP/Mana/Def while the new
        // item built Str/Dex/Agi/Wis/Cha/Def then later Con/Int/HP/Mana from a
        // different LootEffects code path).
        var currentItem = new List<string>
        {
            "Int +3", "Def +5", "Wis +2"
        };
        var newItem = new List<string>
        {
            "Def +4", "Int +5", "Wis +3"
        };

        currentItem.Sort(System.StringComparer.Ordinal);
        newItem.Sort(System.StringComparer.Ordinal);

        currentItem.Should().Equal("Def +5", "Int +3", "Wis +2");
        newItem.Should().Equal("Def +4", "Int +5", "Wis +3");
    }

    [Fact]
    public void StatBonusList_OrdinalSort_HandlesFullStatRoster()
    {
        // Exercise every stat label the renderers emit. After Ordinal sort the
        // order should match a plain string sort on the 3-letter prefix:
        // Agi, Cha, Con, Crit, Def, Dex, HP, Int, Mana, MP, Str, Sta, Wis
        // (note that "HP" sorts before "Int" because H < I in ASCII).
        var stats = new List<string>
        {
            "Str +5", "Wis +2", "Int +3", "Con +1", "Def +7",
            "HP +10", "Mana +5", "Dex +4", "Agi +2", "Cha +1"
        };
        stats.Sort(System.StringComparer.Ordinal);
        stats.Should().Equal(
            "Agi +2", "Cha +1", "Con +1", "Def +7", "Dex +4",
            "HP +10", "Int +3", "Mana +5", "Str +5", "Wis +2");
    }
}
