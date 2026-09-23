using System.Linq;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.10: a tank ally picked its opening taunt by looking for the "aoe_taunt" effect only, so from
/// level 40 it kept taunting bare with Thundering Roar and never raised Shield Wall Formation (which
/// taunts and cuts incoming damage), Divine Mandate or Rage Challenge. And a Cautious ally, told not
/// to taunt, still could with those, because their effect names do not contain "taunt".
/// </summary>
[Collection("SharedGameSingletons")]
public class TankTauntChoiceTests
{
    private static ClassAbilitySystem.ClassAbility A(string id) => ClassAbilitySystem.GetAbility(id)!;

    [Theory]
    [InlineData("shield_wall_formation")]
    [InlineData("divine_mandate")]
    [InlineData("rage_challenge")]
    public void ALevel40TauntThatProtects_IsChosenOverThunderingRoar(string protective)
    {
        var affordable = new[] { A("thundering_roar"), A("shield_wall"), A(protective) };
        CombatEngine.PreferredTaunt(affordable)!.Id.Should().Be(protective);
    }

    [Fact]
    public void BelowLevel40_ThunderingRoarIsStillTheChoice()
    {
        CombatEngine.PreferredTaunt(new[] { A("shield_wall"), A("thundering_roar") })!.Id.Should().Be("thundering_roar");
    }

    [Fact]
    public void BelowLevel40_ATankRaisesShieldWallFirst_ThenTaunts()
    {
        // Turn one: Roar and Shield Wall both ready, so the wall goes up first.
        CombatEngine.OpeningTankMove(new[] { A("thundering_roar"), A("shield_wall") })!.Id.Should().Be("shield_wall");
        // Turn two: the defensive-spread rule has dropped Shield Wall while it is up, so Roar goes out.
        CombatEngine.OpeningTankMove(new[] { A("thundering_roar") })!.Id.Should().Be("thundering_roar");
    }

    [Fact]
    public void FromLevel40_TheProtectiveTaunt_GoesOutAtOnce()
    {
        CombatEngine.OpeningTankMove(new[] { A("thundering_roar"), A("shield_wall"), A("shield_wall_formation") })!.Id.Should().Be("shield_wall_formation");
    }

    [Fact]
    public void WithNoTaunt_ThereIsNoOpeningMove()
    {
        // A Cautious ally's taunts are filtered out before this; Shield Wall alone is not an opening move here
        CombatEngine.OpeningTankMove(new[] { A("shield_wall") }).Should().BeNull();
    }

    [Theory]
    [InlineData("thundering_roar", true)]
    [InlineData("shield_wall_formation", true)]
    [InlineData("divine_mandate", true)]
    [InlineData("rage_challenge", true)]
    [InlineData("shield_wall", false)]   // a defence, not a taunt
    public void ACautiousAlly_HoldsEveryTaunt(string id, bool isTaunt)
    {
        CombatEngine.IsTauntAbility(A(id)).Should().Be(isTaunt, id);
    }
}
