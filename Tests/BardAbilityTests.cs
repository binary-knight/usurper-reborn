using Xunit;
using FluentAssertions;

namespace UsurperReborn.Tests;

/// <summary>
/// Tests for Bard class abilities added in v0.52.5
/// </summary>
public class BardAbilityTests
{
    [Fact]
    public void BardHas12Abilities()
    {
        var abilities = ClassAbilitySystem.GetClassAbilities(CharacterClass.Bard);
        abilities.Count.Should().BeGreaterThanOrEqualTo(12,
            "Bard should have at least 12 class abilities after v0.52.5 overhaul");
    }

    [Fact]
    public void CuttingWords_ExistsWithWeakenEffect()
    {
        var abilities = ClassAbilitySystem.GetClassAbilities(CharacterClass.Bard);
        var cuttingWords = abilities.FirstOrDefault(a => a.Id == "cutting_words");

        cuttingWords.Should().NotBeNull("Cutting Words should exist as a Bard ability");
        cuttingWords!.LevelRequired.Should().Be(14);
        cuttingWords.SpecialEffect.Should().Be("weaken");
        cuttingWords.Type.Should().Be(ClassAbilitySystem.AbilityType.Attack);
        cuttingWords.BaseDamage.Should().BeGreaterThan(0, "Cutting Words should deal damage");
    }

    [Fact]
    public void BalladOfBlades_ExistsAtLevel34()
    {
        var abilities = ClassAbilitySystem.GetClassAbilities(CharacterClass.Bard);
        var ballad = abilities.FirstOrDefault(a => a.Id == "ballad_of_blades");

        ballad.Should().NotBeNull("Ballad of Blades should exist");
        ballad!.LevelRequired.Should().Be(34);
        ballad.Type.Should().Be(ClassAbilitySystem.AbilityType.Attack);
        ballad.BaseDamage.Should().BeGreaterThanOrEqualTo(75);
    }

    [Fact]
    public void Countercharm_ExistsAtLevel42_RequiresInstrument()
    {
        var abilities = ClassAbilitySystem.GetClassAbilities(CharacterClass.Bard);
        var countercharm = abilities.FirstOrDefault(a => a.Id == "countercharm");

        countercharm.Should().NotBeNull("Countercharm should exist");
        countercharm!.LevelRequired.Should().Be(42);
        countercharm.SpecialEffect.Should().Be("party_cleanse");
        countercharm.RequiredWeaponTypes.Should().Contain(WeaponType.Instrument);
    }

    [Fact]
    public void DissonantWhispers_ExistsAtLevel50_CausesFear()
    {
        var abilities = ClassAbilitySystem.GetClassAbilities(CharacterClass.Bard);
        var whispers = abilities.FirstOrDefault(a => a.Id == "dissonant_whispers");

        whispers.Should().NotBeNull("Dissonant Whispers should exist");
        whispers!.LevelRequired.Should().Be(50);
        whispers.SpecialEffect.Should().Be("fear");
        whispers.BaseDamage.Should().BeGreaterThan(0);
    }

    [Fact]
    public void WarDrummersCadence_ExistsAtLevel65_PartyBuff()
    {
        var abilities = ClassAbilitySystem.GetClassAbilities(CharacterClass.Bard);
        var cadence = abilities.FirstOrDefault(a => a.Id == "war_drummers_cadence");

        cadence.Should().NotBeNull("War Drummer's Cadence should exist");
        cadence!.LevelRequired.Should().Be(65);
        cadence.SpecialEffect.Should().Be("party_song");
        cadence.RequiredWeaponTypes.Should().Contain(WeaponType.Instrument);
    }

    [Fact]
    public void BardAbilities_CoverAllLevelRanges()
    {
        var abilities = ClassAbilitySystem.GetClassAbilities(CharacterClass.Bard);

        // Should have abilities at early, mid, and late game
        abilities.Where(a => a.LevelRequired <= 20).Should().HaveCountGreaterThanOrEqualTo(3,
            "Bard should have at least 3 abilities by level 20");
        abilities.Where(a => a.LevelRequired <= 50).Should().HaveCountGreaterThanOrEqualTo(7,
            "Bard should have at least 7 abilities by level 50");
        abilities.Where(a => a.LevelRequired <= 100).Should().HaveCountGreaterThanOrEqualTo(10,
            "Bard should have at least 10 abilities by level 100");
    }

    [Fact]
    public void BardAbilities_NoLevelGapGreaterThan20()
    {
        var abilities = ClassAbilitySystem.GetClassAbilities(CharacterClass.Bard)
            .OrderBy(a => a.LevelRequired)
            .ToList();

        for (int i = 1; i < abilities.Count; i++)
        {
            int gap = abilities[i].LevelRequired - abilities[i - 1].LevelRequired;
            gap.Should().BeLessThanOrEqualTo(20,
                $"Gap between {abilities[i - 1].Name} (lv{abilities[i - 1].LevelRequired}) and {abilities[i].Name} (lv{abilities[i].LevelRequired}) should not exceed 20 levels");
        }
    }
}
