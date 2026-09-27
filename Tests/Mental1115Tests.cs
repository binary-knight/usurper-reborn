using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>v1.1.15: the pure Mental core (bands, cap, change, dungeon strain, daily reset, combat penalty).</summary>
public class Mental1115Tests
{
    private static Character Hero(int mental = 100, CharacterClass cls = CharacterClass.Warrior,
        CharacterRace race = CharacterRace.Human, int addict = 0) => new Character
    {
        Name1 = "Hero", Name2 = "Hero", AI = CharacterAI.Human,
        Class = cls, Race = race, Mental = mental, Addict = addict,
    };

    private static Character Npc(int mental = 100, int addict = 0) => new Character
    {
        Name1 = "Townsfolk", Name2 = "Townsfolk", AI = CharacterAI.Computer,
        Class = CharacterClass.Warrior, Race = CharacterRace.Human, Mental = mental, Addict = addict,
    };

    // Bands

    [Theory]
    [InlineData(0, MentalBand.Broken)]
    [InlineData(1, MentalBand.Breaking)]
    [InlineData(24, MentalBand.Breaking)]
    [InlineData(25, MentalBand.Shaken)]
    [InlineData(49, MentalBand.Shaken)]
    [InlineData(50, MentalBand.Strained)]
    [InlineData(74, MentalBand.Strained)]
    [InlineData(75, MentalBand.Stable)]
    [InlineData(100, MentalBand.Stable)]
    public void Band_boundaries_follow_the_table(int mental, MentalBand expected) =>
        MentalSystem.GetBand(mental).Should().Be(expected);

    // Cap

    [Theory]
    [InlineData(0, 100)]
    [InlineData(40, 80)]
    [InlineData(100, 50)]
    public void Cap_is_max_minus_half_the_addiction(int addict, int cap) =>
        MentalSystem.GetCap(Hero(addict: addict)).Should().Be(cap);

    // Change

    [Fact]
    public void Change_applies_a_plain_delta_and_returns_it()
    {
        var c = Hero(60);
        MentalSystem.Change(c, -7).Should().Be(-7);
        c.Mental.Should().Be(53);
        MentalSystem.Change(c, 12).Should().Be(12);
        c.Mental.Should().Be(65);
    }

    [Fact]
    public void Change_stops_at_zero()
    {
        var c = Hero(5);
        MentalSystem.Change(c, -20).Should().Be(-5);
        c.Mental.Should().Be(0);
    }

    [Fact]
    public void Change_stops_at_the_maximum()
    {
        var c = Hero(95);
        MentalSystem.Change(c, 20).Should().Be(5);
        c.Mental.Should().Be(100);
    }

    [Fact]
    public void Change_gain_stops_at_the_addiction_cap()
    {
        var c = Hero(70, addict: 40);   // cap 80
        MentalSystem.Change(c, 30).Should().Be(10);
        c.Mental.Should().Be(80);
    }

    [Fact]
    public void Change_gain_never_lowers_mental_already_above_the_cap()
    {
        var c = Hero(90, addict: 40);   // cap 80, addiction rose after Mental was set
        MentalSystem.Change(c, 5).Should().Be(0);
        c.Mental.Should().Be(90);
    }

    [Fact]
    public void Change_loss_above_the_cap_ignores_the_cap()
    {
        var c = Hero(90, addict: 80);   // cap 60
        MentalSystem.Change(c, -4).Should().Be(-4);
        c.Mental.Should().Be(86);
    }

    [Fact]
    public void Change_loss_crossing_the_cap_does_not_snap_to_it()
    {
        var c = Hero(65, addict: 80);   // cap 60
        MentalSystem.Change(c, -10).Should().Be(-10);
        c.Mental.Should().Be(55);
    }

    [Fact]
    public void Change_skips_npcs()
    {
        var n = Npc(60);
        MentalSystem.Change(n, -30).Should().Be(0);
        MentalSystem.Change(n, 30).Should().Be(0);
        n.Mental.Should().Be(60);
    }

    // Strain rollover and carry

    [Fact]
    public void Strain_below_1000_costs_nothing_and_is_carried()
    {
        var c = Hero();
        MentalSystem.AddStrain(c, 999, 0).Should().Be(0);
        c.Mental.Should().Be(100);
        c.MentalStrainRemainder.Should().Be(99_900);
    }

    [Fact]
    public void Strain_of_exactly_1000_costs_one_point_and_leaves_no_remainder()
    {
        var c = Hero();
        MentalSystem.AddStrain(c, 1000, 0).Should().Be(1);
        c.Mental.Should().Be(99);
        c.MentalStrainRemainder.Should().Be(0);
    }

    [Fact]
    public void Strain_remainder_carries_across_calls()
    {
        var c = Hero();
        MentalSystem.AddStrain(c, 600, 0).Should().Be(0);
        MentalSystem.AddStrain(c, 600, 0).Should().Be(1);   // 1200: one point, 200 carried
        c.MentalStrainRemainder.Should().Be(20_000);
        MentalSystem.AddStrain(c, 800, 0).Should().Be(1);   // 1000 exactly
        c.MentalStrainRemainder.Should().Be(0);
        MentalSystem.AddStrain(c, 2500, 0).Should().Be(2);
        c.MentalStrainRemainder.Should().Be(50_000);
        c.Mental.Should().Be(96);
    }

    [Fact]
    public void Strain_loss_at_zero_mental_returns_what_was_applied()
    {
        var c = Hero(0);
        MentalSystem.AddStrain(c, 3000, 0).Should().Be(0);
        c.Mental.Should().Be(0);
    }

    [Fact]
    public void Strain_loss_above_the_cap_stays_above_it()
    {
        var c = Hero(90, addict: 80);   // cap 60
        MentalSystem.AddStrain(c, 2000, 0).Should().Be(2);
        c.Mental.Should().Be(88);
    }

    [Fact]
    public void Non_positive_strain_does_nothing()
    {
        var c = Hero(90);
        MentalSystem.AddStrain(c, 0, 0).Should().Be(0);
        MentalSystem.AddStrain(c, -2500, 0).Should().Be(0);
        c.Mental.Should().Be(90, "negative strain is not a way to heal");
        c.MentalStrainRemainder.Should().Be(0);
    }

    // Class and race multipliers

    [Theory]
    [InlineData(CharacterClass.Cleric, CharacterRace.Human, 80)]
    [InlineData(CharacterClass.Paladin, CharacterRace.Human, 80)]
    [InlineData(CharacterClass.Tidesworn, CharacterRace.Human, 80)]
    [InlineData(CharacterClass.Sage, CharacterRace.Human, 85)]
    [InlineData(CharacterClass.Barbarian, CharacterRace.Human, 90)]
    [InlineData(CharacterClass.Warrior, CharacterRace.Troll, 90)]
    [InlineData(CharacterClass.Warrior, CharacterRace.Orc, 90)]
    [InlineData(CharacterClass.Warrior, CharacterRace.Gnoll, 90)]
    [InlineData(CharacterClass.MysticShaman, CharacterRace.Troll, 90)]
    [InlineData(CharacterClass.Warrior, CharacterRace.Human, 100)]
    [InlineData(CharacterClass.Magician, CharacterRace.Elf, 100)]
    public void Each_multiplier_group_scales_strain(CharacterClass cls, CharacterRace race, int pct)
    {
        MentalSystem.GetStrainPct(cls, race).Should().Be(pct);
        var c = Hero(cls: cls, race: race);
        MentalSystem.AddStrain(c, 1000, 0).Should().Be(pct == 100 ? 1 : 0);
        c.MentalStrainRemainder.Should().Be(pct == 100 ? 0 : pct * 1000);
    }

    [Fact]
    public void Cleric_loses_a_point_on_the_second_1000()
    {
        var c = Hero(cls: CharacterClass.Cleric);
        MentalSystem.AddStrain(c, 1000, 0).Should().Be(0);   // 80_000
        MentalSystem.AddStrain(c, 1000, 0).Should().Be(1);   // 160_000
        c.MentalStrainRemainder.Should().Be(60_000);
    }

    [Theory]
    [InlineData(CharacterClass.Cleric, CharacterRace.Troll, 80)]
    [InlineData(CharacterClass.Sage, CharacterRace.Orc, 85)]
    [InlineData(CharacterClass.Barbarian, CharacterRace.Gnoll, 90)]
    [InlineData(CharacterClass.Paladin, CharacterRace.Human, 80)]
    public void Class_and_race_both_qualifying_take_the_lower(CharacterClass cls, CharacterRace race, int pct) =>
        MentalSystem.GetStrainPct(cls, race).Should().Be(pct);

    // Companion cut

    [Theory]
    [InlineData(0, 0, 100_000)]
    [InlineData(1, 10, 90_000)]
    [InlineData(2, 20, 80_000)]
    [InlineData(3, 20, 80_000)]
    [InlineData(5, 20, 80_000)]
    public void Companion_cut_is_10_each_up_to_20(int companions, int cutPct, int units)
    {
        MentalSystem.GetCompanionCutPct(companions).Should().Be(cutPct);
        var c = Hero();
        MentalSystem.AddStrain(c, 1000, companions);
        (c.MentalStrainRemainder + (100 - c.Mental) * MentalSystem.StrainUnitsPerPoint).Should().Be(units);
    }

    [Fact]
    public void Companion_cut_stacks_with_the_class_multiplier()
    {
        var c = Hero(cls: CharacterClass.Cleric);
        MentalSystem.AddStrain(c, 1000, 2).Should().Be(0);
        c.MentalStrainRemainder.Should().Be(64_000);
    }

    // NPC

    [Fact]
    public void Strain_skips_npcs()
    {
        var n = Npc(60);
        MentalSystem.AddStrain(n, 5500, 0).Should().Be(0);
        n.Mental.Should().Be(60);
        n.MentalStrainRemainder.Should().Be(0);
    }

    // Daily reset

    [Fact]
    public void Daily_reset_above_the_cap_drops_to_it_and_skips_the_gain()
    {
        var c = Hero(90, addict: 80);   // cap 60
        MentalSystem.ApplyDailyReset(c).Should().Be(-30);
        c.Mental.Should().Be(60);
    }

    [Fact]
    public void Daily_reset_below_the_cap_gains_ten()
    {
        var c = Hero(50);   // cap 100
        MentalSystem.ApplyDailyReset(c).Should().Be(10);
        c.Mental.Should().Be(60);
    }

    [Fact]
    public void Daily_reset_near_the_cap_stops_at_it()
    {
        var c = Hero(55, addict: 80);   // cap 60
        MentalSystem.ApplyDailyReset(c).Should().Be(5);
        c.Mental.Should().Be(60);
    }

    [Fact]
    public void Daily_reset_exactly_at_the_cap_gains_nothing()
    {
        var c = Hero(60, addict: 80);   // cap 60
        MentalSystem.ApplyDailyReset(c).Should().Be(0);
        c.Mental.Should().Be(60);
    }

    [Fact]
    public void Daily_reset_skips_npcs()
    {
        var n = Npc(90, addict: 80);   // cap 60, above the cap
        MentalSystem.ApplyDailyReset(n).Should().Be(0);
        n.Mental.Should().Be(90);
    }

    // Combat penalty

    [Theory]
    [InlineData(100, 0f)]
    [InlineData(75, 0f)]
    [InlineData(74, 0f)]
    [InlineData(50, 0f)]
    [InlineData(49, 0.05f)]
    [InlineData(25, 0.05f)]
    [InlineData(24, 0.10f)]
    [InlineData(1, 0.10f)]
    [InlineData(0, 0.10f)]
    public void Combat_penalty_per_band(int mental, float penalty) =>
        MentalSystem.GetCombatPenalty(mental).Should().Be(penalty);

    [Fact]
    public void Strain_remainder_is_not_serialized()
    {
        var prop = typeof(Character).GetProperty(nameof(Character.MentalStrainRemainder))!;
        prop.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonIgnoreAttribute), false)
            .Should().NotBeEmpty();
    }
}
