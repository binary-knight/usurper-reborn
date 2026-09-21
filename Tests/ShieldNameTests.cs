using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.9: a player report showed body armor and rings labelled [Shield]. The name test matched
/// "Shield" inside "Shielding", the magic-resist suffix that any item can roll. Real shields must
/// still be recognised; armor, rings and weapons carrying the suffix must not.
/// </summary>
[Collection("SharedGameSingletons")]
public class ShieldNameTests
{
    [Theory]
    [InlineData("Steel Shield")]
    [InlineData("Tower Shield")]
    [InlineData("Kite Shield")]
    [InlineData("Heater Shield")]
    [InlineData("Round Wood Shield")]
    [InlineData("Fine Steel Buckler")]
    [InlineData("Aegis of Dawn")]
    [InlineData("Fortress Bulwark")]
    [InlineData("Ward of the Faithful")]
    [InlineData("Leather Shield of Shielding")]
    public void RealShields_AreStillRecognised(string name) =>
        ShopItemGenerator.LooksLikeShieldByName(name).Should().BeTrue(name);

    [Theory]
    [InlineData("Leather Armor of Shielding")]      // both from the player's screenshots
    [InlineData("Ring of Protection of Shielding")]
    [InlineData("Signet Ring of Shielding")]        // and more of the kind found on the live server
    [InlineData("Silk Slippers of Shielding")]
    [InlineData("Herald's Armguards of Shielding")]
    [InlineData("Dagger of Shielding")]             // the latent case: a weapon held in the off-hand
    [InlineData("Towering Greatsword")]             // the exception the old code guarded by hand
    [InlineData("Target Practice Bow")]             // "Targe" inside "Target"
    [InlineData("Theater Mask")]                    // "Heater" inside "Theater"
    public void NamesThatOnlyContainAKeyword_AreNotShields(string name) =>
        ShopItemGenerator.LooksLikeShieldByName(name).Should().BeFalse(name);

    [Fact]
    public void ArmorOfShielding_CarriesNoShieldTag_InTheBackpack()
    {
        // the exact call the backpack line makes for a non-weapon
        GameConfig.GetWeaponClassTag("Leather Armor of Shielding", WeaponType.None, WeaponHandedness.None, 0, 0)
            .Should().BeEmpty("body armor is not a shield");
        GameConfig.GetWeaponClassTag("Fine Steel Buckler", WeaponType.None, WeaponHandedness.None, 0, 0)
            .Should().NotBeEmpty("a real buckler keeps its tag");
    }
}
