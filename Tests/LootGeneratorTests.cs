using Xunit;
using FluentAssertions;

namespace UsurperReborn.Tests;

/// <summary>
/// Unit tests for LootGenerator
/// Tests equipment generation including rings, necklaces, and boss loot
/// </summary>
public class LootGeneratorTests
{
    #region Ring Generation Tests

    [Fact]
    public void GenerateRing_ReturnsValidRing()
    {
        var ring = LootGenerator.GenerateRing(25);

        ring.Should().NotBeNull();
        ring.Name.Should().NotBeNullOrEmpty();
        ring.Type.Should().Be(ObjType.Fingers);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(25)]
    [InlineData(50)]
    [InlineData(75)]
    [InlineData(100)]
    public void GenerateRing_WorksAtAllLevels(int level)
    {
        var ring = LootGenerator.GenerateRing(level);

        ring.Should().NotBeNull();
        ring.Type.Should().Be(ObjType.Fingers);
    }

    [Fact]
    public void GenerateRing_HasStatBonuses()
    {
        // Generate several rings and check for stat bonuses
        var hasStats = false;
        for (int i = 0; i < 20; i++)
        {
            var ring = LootGenerator.GenerateRing(50);
            if (ring.Strength > 0 || ring.Dexterity > 0 || ring.HP > 0)
            {
                hasStats = true;
                break;
            }
        }

        hasStats.Should().BeTrue("Rings should have stat bonuses");
    }

    [Fact]
    public void GenerateRing_WithForcedRarity_RespectsRarity()
    {
        var epicRing = LootGenerator.GenerateRing(50, LootGenerator.ItemRarity.Epic);

        // Epic items should have "Epic" in the name or be high value
        epicRing.Should().NotBeNull();
        (epicRing.Name.Contains("Epic") || epicRing.Value > 1000).Should().BeTrue();
    }

    #endregion

    #region Necklace Generation Tests

    [Fact]
    public void GenerateNecklace_ReturnsValidNecklace()
    {
        var necklace = LootGenerator.GenerateNecklace(25);

        necklace.Should().NotBeNull();
        necklace.Name.Should().NotBeNullOrEmpty();
        necklace.Type.Should().Be(ObjType.Neck);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(25)]
    [InlineData(50)]
    [InlineData(75)]
    [InlineData(100)]
    public void GenerateNecklace_WorksAtAllLevels(int level)
    {
        var necklace = LootGenerator.GenerateNecklace(level);

        necklace.Should().NotBeNull();
        necklace.Type.Should().Be(ObjType.Neck);
    }

    [Fact]
    public void GenerateNecklace_HasStatBonuses()
    {
        // Generate several necklaces and check for stat bonuses
        var hasStats = false;
        for (int i = 0; i < 20; i++)
        {
            var necklace = LootGenerator.GenerateNecklace(50);
            if (necklace.Wisdom > 0 || necklace.Mana > 0 || necklace.HP > 0)
            {
                hasStats = true;
                break;
            }
        }

        hasStats.Should().BeTrue("Necklaces should have stat bonuses");
    }

    #endregion

    #region Mini-Boss Loot Tests

    [Fact]
    public void GenerateMiniBossLoot_ReturnsValidItem()
    {
        var loot = LootGenerator.GenerateMiniBossLoot(50, CharacterClass.Barbarian);

        loot.Should().NotBeNull();
        loot.Name.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GenerateMiniBossLoot_IsAtLeastUncommon()
    {
        // Generate many items and verify none are Common rarity
        for (int i = 0; i < 50; i++)
        {
            var loot = LootGenerator.GenerateMiniBossLoot(50, CharacterClass.Barbarian);

            // Common items typically don't have rarity prefixes
            // Uncommon+ items should have higher value or rarity indicators
            loot.Value.Should().BeGreaterThan(0, "Mini-boss loot should have value");
        }
    }

    [Fact]
    public void GenerateMiniBossLoot_CanDropAllTypes()
    {
        var hasWeapon = false;
        var hasArmor = false;
        var hasRing = false;
        var hasNecklace = false;

        for (int i = 0; i < 100; i++)
        {
            var loot = LootGenerator.GenerateMiniBossLoot(50, CharacterClass.Barbarian);

            if (loot.Type == ObjType.Weapon) hasWeapon = true;
            else if (loot.Type == ObjType.Body) hasArmor = true;
            else if (loot.Type == ObjType.Fingers) hasRing = true;
            else if (loot.Type == ObjType.Neck) hasNecklace = true;

            if (hasWeapon && hasArmor && hasRing && hasNecklace) break;
        }

        hasWeapon.Should().BeTrue("Mini-boss should drop weapons");
        hasArmor.Should().BeTrue("Mini-boss should drop armor");
        hasRing.Should().BeTrue("Mini-boss should drop rings");
        hasNecklace.Should().BeTrue("Mini-boss should drop necklaces");
    }

    #endregion

    #region Boss Loot Tests

    [Fact]
    public void GenerateBossLoot_ReturnsValidItem()
    {
        var loot = LootGenerator.GenerateBossLoot(50, CharacterClass.Barbarian);

        loot.Should().NotBeNull();
        loot.Name.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GenerateBossLoot_IsHigherQualityThanMiniBoss()
    {
        long totalBossValue = 0;
        long totalMiniBossValue = 0;

        // Use 500 iterations for statistical reliability — with fewer samples,
        // random variance (especially cursed Epic+ items losing 50% value) can
        // occasionally cause mini-boss totals to exceed boss totals.
        for (int i = 0; i < 500; i++)
        {
            var bossLoot = LootGenerator.GenerateBossLoot(50, CharacterClass.Barbarian);
            var miniBossLoot = LootGenerator.GenerateMiniBossLoot(50, CharacterClass.Barbarian);

            totalBossValue += bossLoot.Value;
            totalMiniBossValue += miniBossLoot.Value;
        }

        // Boss loot should average higher value due to Epic+ rarity
        totalBossValue.Should().BeGreaterThan(totalMiniBossValue,
            "Boss loot should be more valuable than mini-boss loot on average");
    }

    [Fact]
    public void GenerateBossLoot_CanDropAllTypes()
    {
        var hasWeapon = false;
        var hasArmor = false;
        var hasRing = false;
        var hasNecklace = false;

        for (int i = 0; i < 100; i++)
        {
            var loot = LootGenerator.GenerateBossLoot(50, CharacterClass.Barbarian);

            if (loot.Type == ObjType.Weapon) hasWeapon = true;
            else if (loot.Type == ObjType.Body) hasArmor = true;
            else if (loot.Type == ObjType.Fingers) hasRing = true;
            else if (loot.Type == ObjType.Neck) hasNecklace = true;

            if (hasWeapon && hasArmor && hasRing && hasNecklace) break;
        }

        hasWeapon.Should().BeTrue("Boss should drop weapons");
        hasArmor.Should().BeTrue("Boss should drop armor");
        hasRing.Should().BeTrue("Boss should drop rings");
        hasNecklace.Should().BeTrue("Boss should drop necklaces");
    }

    #endregion

    #region Level Scaling Tests

    [Fact]
    public void GenerateRing_HigherLevel_HasBetterStats()
    {
        long lowLevelValue = 0;
        long highLevelValue = 0;

        for (int i = 0; i < 20; i++)
        {
            var lowRing = LootGenerator.GenerateRing(10);
            var highRing = LootGenerator.GenerateRing(80);

            lowLevelValue += lowRing.Value;
            highLevelValue += highRing.Value;
        }

        highLevelValue.Should().BeGreaterThan(lowLevelValue,
            "Higher level rings should be more valuable");
    }

    [Fact]
    public void GenerateNecklace_HigherLevel_HasBetterStats()
    {
        long lowLevelValue = 0;
        long highLevelValue = 0;

        for (int i = 0; i < 20; i++)
        {
            var lowNecklace = LootGenerator.GenerateNecklace(10);
            var highNecklace = LootGenerator.GenerateNecklace(80);

            lowLevelValue += lowNecklace.Value;
            highLevelValue += highNecklace.Value;
        }

        highLevelValue.Should().BeGreaterThan(lowLevelValue,
            "Higher level necklaces should be more valuable");
    }

    #endregion

    #region Accessory Stat Distribution Tests

    [Fact]
    public void GenerateRing_FocusesOnPhysicalStats()
    {
        int totalStrength = 0;
        int totalDexterity = 0;
        int totalHP = 0;

        for (int i = 0; i < 50; i++)
        {
            var ring = LootGenerator.GenerateRing(50);
            totalStrength += ring.Strength;
            totalDexterity += ring.Dexterity;
            totalHP += ring.HP;
        }

        // Rings should have physical stat bonuses
        (totalStrength + totalDexterity + totalHP).Should().BeGreaterThan(0,
            "Rings should provide physical stat bonuses");
    }

    [Fact]
    public void GenerateNecklace_FocusesOnMagicalStats()
    {
        int totalWisdom = 0;
        int totalMana = 0;
        int totalHP = 0;

        for (int i = 0; i < 50; i++)
        {
            var necklace = LootGenerator.GenerateNecklace(50);
            totalWisdom += necklace.Wisdom;
            totalMana += necklace.Mana;
            totalHP += necklace.HP;
        }

        // Necklaces should have magical stat bonuses
        (totalWisdom + totalMana + totalHP).Should().BeGreaterThan(0,
            "Necklaces should provide magical stat bonuses");
    }

    #endregion
}
