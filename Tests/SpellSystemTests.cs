using Xunit;
using FluentAssertions;

namespace UsurperReborn.Tests;

/// <summary>
/// Unit tests for SpellSystem
/// </summary>
public class SpellSystemTests
{
    [Theory]
    [InlineData(CharacterClass.Cleric, 1)]
    [InlineData(CharacterClass.Magician, 1)]
    [InlineData(CharacterClass.Sage, 1)]
    public void GetSpellInfo_ReturnsSpell_ForSpellcastingClasses(CharacterClass charClass, int spellLevel)
    {
        var spell = SpellSystem.GetSpellInfo(charClass, spellLevel);

        spell.Should().NotBeNull();
        spell!.Name.Should().NotBeNullOrEmpty();
        spell.Level.Should().Be(spellLevel);
    }

    [Fact]
    public void GetSpellInfo_ReturnsNull_ForNonSpellcasters()
    {
        var spell = SpellSystem.GetSpellInfo(CharacterClass.Warrior, 1);

        spell.Should().BeNull();
    }

    [Fact]
    public void ClericSpells_HaveCorrectTypes()
    {
        // Level 1 Cure Light should be a heal
        var cureLight = SpellSystem.GetSpellInfo(CharacterClass.Cleric, 1);
        cureLight.Should().NotBeNull();
        cureLight!.SpellType.Should().Be("Heal");

        // Level 2 Divine Shield should be a buff
        var shield = SpellSystem.GetSpellInfo(CharacterClass.Cleric, 2);
        shield.Should().NotBeNull();
        shield!.SpellType.Should().Be("Buff");
    }

    [Fact]
    public void MagicianSpells_HaveCorrectTypes()
    {
        // Level 1 Magic Missile should be an attack
        var magicMissile = SpellSystem.GetSpellInfo(CharacterClass.Magician, 1);
        magicMissile.Should().NotBeNull();
        magicMissile!.SpellType.Should().Be("Attack");
    }

    [Theory]
    [InlineData(1, 1)]    // Level 1 char can cast spell level 1 (requires level 1)
    [InlineData(16, 5)]   // Level 16 char can cast spell level 5 (requires level 16)
    [InlineData(4, 2)]    // Level 4 char can cast spell level 2 (requires level 4)
    public void CanCastSpell_WhenLevelSufficient(int charLevel, int spellLevel)
    {
        var mage = CreateTestMage(charLevel);

        var canCast = SpellSystem.CanCastSpell(mage, spellLevel);

        canCast.Should().BeTrue();
    }

    [Theory]
    [InlineData(1, 5)]    // Level 1 char cannot cast spell level 5 (requires level 16)
    [InlineData(10, 7)]   // Level 10 char cannot cast spell level 7 (requires level 25)
    public void CannotCastSpell_WhenLevelInsufficient(int charLevel, int spellLevel)
    {
        var mage = CreateTestMage(charLevel);

        var canCast = SpellSystem.CanCastSpell(mage, spellLevel);

        canCast.Should().BeFalse();
    }

    [Fact]
    public void CannotCastSpell_WhenNotEnoughMana()
    {
        var mage = CreateTestMage(50);
        mage.Mana = 1; // Almost no mana

        var canCast = SpellSystem.CanCastSpell(mage, 10);

        canCast.Should().BeFalse();
    }

    [Fact]
    public void CalculateManaCost_ReturnsPositiveValue()
    {
        var spell = SpellSystem.GetSpellInfo(CharacterClass.Cleric, 1);
        var cleric = CreateTestCleric(10);

        var cost = SpellSystem.CalculateManaCost(spell!, cleric);

        cost.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void CalculateManaCost_ReducedByWisdom()
    {
        var spell = SpellSystem.GetSpellInfo(CharacterClass.Cleric, 5);

        var lowWisCleric = CreateTestCleric(20);
        lowWisCleric.Wisdom = 8;

        var highWisCleric = CreateTestCleric(20);
        highWisCleric.Wisdom = 25;

        var lowWisCost = SpellSystem.CalculateManaCost(spell!, lowWisCleric);
        var highWisCost = SpellSystem.CalculateManaCost(spell!, highWisCleric);

        highWisCost.Should().BeLessThan(lowWisCost);
    }

    [Fact]
    public void CastSpell_ReturnsValidResult()
    {
        var cleric = CreateTestCleric(10);
        cleric.HP = 50; // Wounded for healing test

        // Set spell proficiency to Legendary (0% failure chance)
        string skillId = TrainingSystem.GetSpellSkillId(CharacterClass.Cleric, 1);
        TrainingSystem.SetSkillProficiency(cleric, skillId, TrainingSystem.ProficiencyLevel.Legendary);

        var result = SpellSystem.CastSpell(cleric, 1, null); // Cure Light on self

        // Note: Even with Legendary proficiency, a natural 1 (5% chance) causes critical failure
        // This test verifies the method returns a valid result structure, not that it always succeeds
        result.Should().NotBeNull();
        result.Message.Should().NotBeNullOrEmpty();
        result.ManaCost.Should().BeGreaterThan(0); // Mana is always consumed on cast attempt
    }

    [Fact]
    public void GetAvailableSpells_ReturnsListForSpellcaster()
    {
        var mage = CreateTestMage(20);

        var spells = SpellSystem.GetAvailableSpells(mage);

        // Method should return a list (may be empty if spells not learned yet)
        spells.Should().NotBeNull();
    }

    [Fact]
    public void GetAvailableSpells_ReturnsEmptyListForNonSpellcaster()
    {
        var warrior = new Character
        {
            Name2 = "TestWarrior",
            Class = CharacterClass.Warrior,
            Level = 50
        };

        var spells = SpellSystem.GetAvailableSpells(warrior);

        spells.Should().NotBeNull();
        spells.Should().BeEmpty();
    }

    [Fact]
    public void SpellInfo_HasMagicWords()
    {
        var spell = SpellSystem.GetSpellInfo(CharacterClass.Cleric, 1);

        spell.Should().NotBeNull();
        spell!.MagicWords.Should().NotBeNullOrEmpty();
    }

    private static Character CreateTestMage(int level)
    {
        var mage = new Character
        {
            Name2 = "TestMage",
            Class = CharacterClass.Magician,
            Level = level,
            Mana = 1000,
            MaxMana = 1000,
            HP = 100,
            MaxHP = 100,
            Wisdom = 15,
            Intelligence = 20
        };
        // Magicians require a Staff to cast spells
        var staff = new Equipment { Name = "Test Staff", WeaponType = WeaponType.Staff, Slot = EquipmentSlot.MainHand };
        int staffId = EquipmentDatabase.RegisterDynamic(staff);
        mage.EquippedItems[EquipmentSlot.MainHand] = staffId;
        return mage;
    }

    private static Character CreateTestCleric(int level)
    {
        return new Character
        {
            Name2 = "TestCleric",
            Class = CharacterClass.Cleric,
            Level = level,
            Mana = 1000,
            MaxMana = 1000,
            HP = 100,
            MaxHP = 100,
            Wisdom = 18,
            Intelligence = 12
        };
    }
}
