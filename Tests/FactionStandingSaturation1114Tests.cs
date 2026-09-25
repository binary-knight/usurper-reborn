using System;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>v1.1.14: faction standing saturates at the int range instead of wrapping around.</summary>
public class FactionStandingSaturation1114Tests
{
    [Fact]
    public void AHugeCrownDonation_GivesTheMaximumStanding_NeverANegative()
    {
        var factions = new FactionSystem();
        factions.FactionStanding[Faction.TheCrown] = 100;
        factions.FactionStanding[Faction.TheShadows] = -100;
        factions.FactionStanding[Faction.TheFaith] = 100;

        // the Castle donation's gain for 107,374,182,350 gold (+1 per 50 gold, bounded before the cast)
        int gain = (int)Math.Min(int.MaxValue, 107_374_182_350L / 50);
        factions.ModifyReputation(Faction.TheCrown, gain);

        factions.FactionStanding[Faction.TheCrown].Should().Be(int.MaxValue);
        factions.FactionStanding[Faction.TheShadows].Should().BeNegative("the cascade lowers the Shadows");
        factions.FactionStanding[Faction.TheFaith].Should().BePositive("the cascade raises the Faith");

        // a second one stays at the maximum
        factions.ModifyReputation(Faction.TheCrown, gain);
        factions.FactionStanding[Faction.TheCrown].Should().Be(int.MaxValue);
        factions.FactionStanding[Faction.TheShadows].Should().BeNegative();
        factions.FactionStanding[Faction.TheFaith].Should().Be(int.MaxValue / 5 * 2 + 100);
    }

    [Fact]
    public void StandingAtEitherEnd_Saturates_ForEveryAddition()
    {
        var factions = new FactionSystem();
        factions.FactionStanding[Faction.TheShadows] = int.MinValue + 10;
        factions.ModifyReputation(Faction.TheFaith, int.MaxValue);   // the cascade takes the Shadows down
        factions.FactionStanding[Faction.TheShadows].Should().Be(int.MinValue);

        factions.FactionStanding[Faction.TheFaith] = int.MaxValue - 10;
        factions.ModifyReputation(Faction.TheFaith, 50);
        factions.FactionStanding[Faction.TheFaith].Should().Be(int.MaxValue);

        factions.FactionStanding[Faction.TheCrown] = int.MinValue + 100;
        factions.ModifyReputation(Faction.TheShadows, int.MaxValue);   // the cascade takes the Crown down
        factions.FactionStanding[Faction.TheCrown].Should().Be(int.MinValue);
    }
}
