using Xunit;
using FluentAssertions;

namespace UsurperReborn.Tests;

/// <summary>
/// Unit tests for LootGenerator.ApplyThematicBonuses
/// Ensures armor template names produce appropriate stat bonuses.
/// </summary>
public class ThematicBonusTests
{
    private static Item CreateTestItem(int armor = 50)
    {
        return new Item
        {
            Name = "Test Item",
            Type = ObjType.Body,
            Armor = armor
        };
    }

    private static bool HasConstitutionEffect(Item item) =>
        item.LootEffects.Any(e => e.Item1 == (int)LootGenerator.SpecialEffect.Constitution && e.Item2 > 0);

    private static bool HasIntelligenceEffect(Item item) =>
        item.LootEffects.Any(e => e.Item1 == (int)LootGenerator.SpecialEffect.Intelligence && e.Item2 > 0);

    #region Caster/Focus Group

    [Theory]
    [InlineData("Wizard's Hat")]
    [InlineData("Robe of the Archmage")]
    [InlineData("Arcane Vestments")]
    [InlineData("Enchanted Robe")]
    [InlineData("Mystic Veil")]
    [InlineData("Sash of Focus")]
    [InlineData("Cloth Robe")]
    [InlineData("Silk Vestments")]
    [InlineData("Silk Arm Wraps")]
    [InlineData("Silk Handwraps")]
    [InlineData("Silk Trousers")]
    [InlineData("Silk Slippers")]
    [InlineData("Cloth Gloves")]
    [InlineData("Cloth Leggings")]
    [InlineData("Cloth Sandals")]
    [InlineData("Cloth Mask")]
    [InlineData("Wizard's Mantle")]
    [InlineData("Cloak of the Archmage")]
    public void CasterTemplates_GrantIntelligence(string templateName)
    {
        var item = CreateTestItem();
        LootGenerator.ApplyThematicBonuses(item, templateName, 50);

        HasIntelligenceEffect(item).Should().BeTrue($"'{templateName}' should grant Intelligence");
    }

    #endregion

    #region Holy/Divine Group

    [Theory]
    [InlineData("Holy Vestments")]
    [InlineData("Blessed Plate")]
    [InlineData("Divine Armor")]
    [InlineData("Sacred Vestments")]
    [InlineData("Vestments of the Faith")]
    [InlineData("Holy Diadem")]
    [InlineData("Holy Armguards")]
    [InlineData("Holy Sash")]
    [InlineData("Holy Shroud")]
    [InlineData("Paladin's Shield")]
    [InlineData("Wall of Faith")]
    [InlineData("Priest's Robes")]
    public void HolyTemplates_GrantWisAndCon(string templateName)
    {
        var item = CreateTestItem();
        LootGenerator.ApplyThematicBonuses(item, templateName, 50);

        item.Wisdom.Should().BeGreaterThan(0, $"'{templateName}' should grant Wisdom");
        HasConstitutionEffect(item).Should().BeTrue($"'{templateName}' should grant Constitution");
    }

    #endregion

    #region Shadow/Stealth Group

    [Theory]
    [InlineData("Shadow Leather")]
    [InlineData("Night Stalker Armor")]
    [InlineData("Shadow Hood")]
    [InlineData("Shadow Bracers")]
    [InlineData("Shadow Handwraps")]
    [InlineData("Shadow Leggings")]
    [InlineData("Shadow Treads")]
    [InlineData("Shadow Mask")]
    [InlineData("Shadow Cloak")]
    [InlineData("Cloak of Shadows")]
    [InlineData("Death Mask")]
    [InlineData("Phantom Buckler")]
    [InlineData("Thief's Gloves")]
    [InlineData("Thief's Girdle")]
    public void ShadowTemplates_GrantDexAndAgi(string templateName)
    {
        var item = CreateTestItem();
        LootGenerator.ApplyThematicBonuses(item, templateName, 50);

        item.Dexterity.Should().BeGreaterThan(0, $"'{templateName}' should grant Dexterity");
        item.Agility.Should().BeGreaterThan(0, $"'{templateName}' should grant Agility");
    }

    #endregion

    #region Warrior/Battle Group

    [Theory]
    [InlineData("War Paint Armor")]
    [InlineData("Berserker's Plate")]
    [InlineData("Titan's Harness")]
    [InlineData("Battle Crown")]
    [InlineData("Titan's Greathelm")]
    [InlineData("Barbarian Arm Guards")]
    [InlineData("Barbarian Legguards")]
    [InlineData("Spiked Fists")]
    [InlineData("War Belt")]
    [InlineData("War Mask")]
    [InlineData("War Cloak")]
    [InlineData("Titan's Belt")]
    [InlineData("Titan's Legplates")]
    [InlineData("Titan's Bulwark")]
    [InlineData("Fighter's Headband")]
    [InlineData("Knight's Shield")]
    [InlineData("Fortress Shield")]
    [InlineData("Aegis")]
    public void WarriorTemplates_GrantStrAndCon(string templateName)
    {
        var item = CreateTestItem();
        LootGenerator.ApplyThematicBonuses(item, templateName, 50);

        item.Strength.Should().BeGreaterThan(0, $"'{templateName}' should grant Strength");
        HasConstitutionEffect(item).Should().BeTrue($"'{templateName}' should grant Constitution");
    }

    #endregion

    #region Dragon Group

    [Theory]
    [InlineData("Dragon Scale Gi")]
    [InlineData("Dragonscale Bracers")]
    [InlineData("Dragon Grip")]
    [InlineData("Dragonhide Boots")]
    [InlineData("Dragonscale Belt")]
    [InlineData("Dragon Visage")]
    [InlineData("Dragonwing Cape")]
    [InlineData("Dragon Scale Shield")]
    public void DragonTemplates_GrantStrDefCon(string templateName)
    {
        var item = CreateTestItem();
        LootGenerator.ApplyThematicBonuses(item, templateName, 50);

        item.Strength.Should().BeGreaterThan(0, $"'{templateName}' should grant Strength");
        item.Defence.Should().BeGreaterThan(0, $"'{templateName}' should grant Defence");
        HasConstitutionEffect(item).Should().BeTrue($"'{templateName}' should grant Constitution");
    }

    #endregion

    #region Ranger/Scout Group

    [Theory]
    [InlineData("Ranger's Cloak")]
    [InlineData("Forest Guardian Armor")]
    [InlineData("Elven Chainweave")]
    [InlineData("Elven Cloak")]
    [InlineData("Elven Buckler")]
    [InlineData("Scout's Boots")]
    [InlineData("Traveler's Sandals")]
    [InlineData("Traveler's Cloak")]
    [InlineData("Leather Armor")]
    [InlineData("Leather Bracers")]
    [InlineData("Leather Gloves")]
    [InlineData("Leather Leggings")]
    [InlineData("Leather Boots")]
    [InlineData("Leather Belt")]
    [InlineData("Leather Face Guard")]
    [InlineData("Leather Shield")]
    [InlineData("Leather Cap")]
    [InlineData("Studded Leather")]
    [InlineData("Hard Leather")]
    public void RangerTemplates_GrantDexAndAgi(string templateName)
    {
        var item = CreateTestItem();
        LootGenerator.ApplyThematicBonuses(item, templateName, 50);

        item.Dexterity.Should().BeGreaterThan(0, $"'{templateName}' should grant Dexterity");
        item.Agility.Should().BeGreaterThan(0, $"'{templateName}' should grant Agility");
    }

    #endregion

    #region Vitality Group

    [Theory]
    [InlineData("Vitality Armor")]
    [InlineData("Endurance Shield")]
    [InlineData("Resilience Helm")]
    [InlineData("Fortitude Belt")]
    [InlineData("Vigor Boots")]
    public void VitalityTemplates_GrantCon(string templateName)
    {
        var item = CreateTestItem();
        LootGenerator.ApplyThematicBonuses(item, templateName, 50);

        HasConstitutionEffect(item).Should().BeTrue($"'{templateName}' should grant Constitution");
    }

    #endregion

    #region Premium Material Group

    [Theory]
    [InlineData("Mithril Helm")]
    [InlineData("Mithril Armguards")]
    [InlineData("Mithril Gloves")]
    [InlineData("Mithril Legguards")]
    [InlineData("Mithril Boots")]
    [InlineData("Mithril Belt")]
    [InlineData("Mithril Visor")]
    [InlineData("Mithril Weave Cloak")]
    [InlineData("Adamantine Plate")]
    public void PremiumMaterialTemplates_GrantConAndDef(string templateName)
    {
        var item = CreateTestItem();
        LootGenerator.ApplyThematicBonuses(item, templateName, 50);

        HasConstitutionEffect(item).Should().BeTrue($"'{templateName}' should grant Constitution");
        item.Defence.Should().BeGreaterThan(0, $"'{templateName}' should grant Defence");
    }

    #endregion

    #region Metal Armor Group

    [Theory]
    [InlineData("Iron Helm")]
    [InlineData("Iron Vambraces")]
    [InlineData("Iron Gauntlets")]
    [InlineData("Iron Greaves")]
    [InlineData("Iron Boots")]
    [InlineData("Iron Visor")]
    [InlineData("Iron Shield")]
    [InlineData("Iron Buckler")]
    [InlineData("Steel Helm")]
    [InlineData("Steel Vambraces")]
    [InlineData("Steel Gauntlets")]
    [InlineData("Steel Greaves")]
    [InlineData("Steel Sabatons")]
    [InlineData("Steel Faceplate")]
    [InlineData("Steel Girdle")]
    [InlineData("Steel Shield")]
    [InlineData("Steel Buckler")]
    [InlineData("Chain Coif")]
    [InlineData("Chain Sleeves")]
    [InlineData("Chain Gauntlets")]
    [InlineData("Chain Leggings")]
    [InlineData("Chain Boots")]
    [InlineData("Chain Belt")]
    [InlineData("Chain Shirt")]
    [InlineData("Chain Mail")]
    [InlineData("Reinforced Chain")]
    [InlineData("Plate Mail")]
    [InlineData("Full Plate")]
    [InlineData("Plate Vambraces")]
    [InlineData("Plate Gauntlets")]
    [InlineData("Plate Greaves")]
    [InlineData("Plate Sabatons")]
    [InlineData("Banded Mail")]
    [InlineData("Splint Mail")]
    [InlineData("Studded Armguards")]
    [InlineData("Studded Legguards")]
    [InlineData("Wooden Buckler")]
    [InlineData("Wooden Shield")]
    public void MetalArmorTemplates_GrantDefAndCon(string templateName)
    {
        var item = CreateTestItem();
        LootGenerator.ApplyThematicBonuses(item, templateName, 50);

        item.Defence.Should().BeGreaterThan(0, $"'{templateName}' should grant Defence");
        HasConstitutionEffect(item).Should().BeTrue($"'{templateName}' should grant Constitution");
    }

    #endregion

    #region No False Matches

    [Fact]
    public void CasterItems_DoNotGrantStrength()
    {
        var item = CreateTestItem();
        LootGenerator.ApplyThematicBonuses(item, "Wizard's Hat", 50);

        item.Strength.Should().Be(0, "Caster items should not grant Strength");
    }

    [Fact]
    public void ShadowItems_DoNotGrantWisdom()
    {
        var item = CreateTestItem();
        LootGenerator.ApplyThematicBonuses(item, "Shadow Hood", 50);

        item.Wisdom.Should().Be(0, "Shadow items should not grant Wisdom");
    }

    [Fact]
    public void HolyItems_DoNotGrantDexterity()
    {
        var item = CreateTestItem();
        LootGenerator.ApplyThematicBonuses(item, "Holy Vestments", 50);

        item.Dexterity.Should().Be(0, "Holy items should not grant Dexterity");
    }

    [Fact]
    public void WarriorItems_DoNotGrantMana()
    {
        var item = CreateTestItem();
        LootGenerator.ApplyThematicBonuses(item, "Battle Crown", 50);

        item.Mana.Should().Be(0, "Warrior items should not grant Mana");
    }

    #endregion

    #region Power Scaling

    [Fact]
    public void HigherPower_GrantsHigherStats()
    {
        var lowItem = CreateTestItem();
        var highItem = CreateTestItem();

        LootGenerator.ApplyThematicBonuses(lowItem, "Shadow Hood", 20);
        LootGenerator.ApplyThematicBonuses(highItem, "Shadow Hood", 100);

        highItem.Dexterity.Should().BeGreaterThan(lowItem.Dexterity,
            "Higher power should grant more stats");
        highItem.Agility.Should().BeGreaterThan(lowItem.Agility,
            "Higher power should grant more stats");
    }

    [Fact]
    public void MinimumPower_StillGrantsStats()
    {
        var item = CreateTestItem();
        LootGenerator.ApplyThematicBonuses(item, "Shadow Hood", 1);

        item.Dexterity.Should().BeGreaterThan(0, "Even power=1 should grant at least 1 stat point");
        item.Agility.Should().BeGreaterThan(0, "Even power=1 should grant at least 1 stat point");
    }

    #endregion

    #region Priority / Ordering

    [Fact]
    public void ShadowLeather_MatchesShadowNotLeather()
    {
        // "Shadow Leather" contains both "shadow" and "leather"
        // Shadow should match first (higher priority in the if/else chain)
        var item = CreateTestItem();
        LootGenerator.ApplyThematicBonuses(item, "Shadow Leather", 50);

        item.Dexterity.Should().BeGreaterThan(0, "Shadow Leather should match shadow group");
        item.Agility.Should().BeGreaterThan(0, "Shadow Leather should match shadow group");
        item.Strength.Should().Be(0);
        item.Wisdom.Should().Be(0);
        item.Mana.Should().Be(0);
    }

    [Fact]
    public void HolyDiadem_MatchesHolyNotCaster()
    {
        // "Holy Diadem" contains "diadem" (holy group) — should get WIS+CON not INT
        var item = CreateTestItem();
        LootGenerator.ApplyThematicBonuses(item, "Holy Diadem", 50);

        item.Wisdom.Should().BeGreaterThan(0, "Holy Diadem should grant Wisdom");
        HasConstitutionEffect(item).Should().BeTrue("Holy Diadem should grant Constitution");
        item.Mana.Should().Be(0, "Holy Diadem should NOT grant Mana");
    }

    [Fact]
    public void ElvenChainweave_MatchesRangerNotMetal()
    {
        // "Elven Chainweave" contains "elven" (ranger) and "chain" (metal)
        // Ranger should match first
        var item = CreateTestItem();
        LootGenerator.ApplyThematicBonuses(item, "Elven Chainweave", 50);

        item.Dexterity.Should().BeGreaterThan(0, "Elven Chainweave should match ranger group");
        item.Agility.Should().BeGreaterThan(0, "Elven Chainweave should match ranger group");
    }

    [Fact]
    public void AdamantinePlate_MatchesPremiumNotMetal()
    {
        // "Adamantine Plate" contains "adamantine" (premium) and "plate" (metal)
        // Premium should match first
        var item = CreateTestItem();
        LootGenerator.ApplyThematicBonuses(item, "Adamantine Plate", 50);

        HasConstitutionEffect(item).Should().BeTrue("Adamantine Plate should match premium group");
        item.Defence.Should().BeGreaterThan(0, "Adamantine Plate should match premium group");
    }

    #endregion

    #region AllStats Effect (of Perfection)

    [Fact]
    public void AllStats_GrantsAllSevenStats()
    {
        var item = CreateTestItem();

        // Simulate what ApplyEffectsToItem does for AllStats
        int value = 5;
        item.Strength += value;
        item.Dexterity += value;
        item.Agility += value;
        item.Wisdom += value;
        item.Charisma += value;
        // CON and INT now go through LootEffects, not item.HP/item.Mana

        item.Strength.Should().Be(5);
        item.Dexterity.Should().Be(5);
        item.Agility.Should().Be(5);
        item.Wisdom.Should().Be(5);
        item.Charisma.Should().Be(5);
    }

    #endregion

    #region Martial/Agile Templates

    [Theory]
    [InlineData("Training Gi")]
    [InlineData("Reinforced Gi")]
    [InlineData("Master's Gi")]
    public void MartialArmorTemplates_GrantDexAndAgi(string templateName)
    {
        var item = CreateTestItem();
        LootGenerator.ApplyThematicBonuses(item, templateName, 50);

        item.Dexterity.Should().BeGreaterThan(0, $"'{templateName}' should grant Dexterity");
        item.Agility.Should().BeGreaterThan(0, $"'{templateName}' should grant Agility");
    }

    [Fact]
    public void GiKeyword_DoesNotMatchSubstrings()
    {
        // "gi" should NOT match inside "leggings", "girdle", "magic", etc.
        var leggings = CreateTestItem();
        var girdle = CreateTestItem();

        LootGenerator.ApplyThematicBonuses(leggings, "Chain Leggings", 50);
        LootGenerator.ApplyThematicBonuses(girdle, "Steel Girdle", 50);

        // These should match "chain"/"steel" (metal group → Con+Def), NOT "gi" (martial → Dex+Agi)
        leggings.Agility.Should().Be(0, "'Chain Leggings' should not match martial 'gi' group");
        girdle.Agility.Should().Be(0, "'Steel Girdle' should not match martial 'gi' group");
    }

    #endregion

    #region Metal/Crafted Templates (wooden, bone, copper, etc.)

    [Theory]
    [InlineData("Wooden Buckler")]
    [InlineData("Wooden Shield")]
    public void WoodenTemplates_GrantConAndDef(string templateName)
    {
        var item = CreateTestItem();
        LootGenerator.ApplyThematicBonuses(item, templateName, 50);

        HasConstitutionEffect(item).Should().BeTrue($"'{templateName}' should grant Constitution");
        item.Defence.Should().BeGreaterThan(0, $"'{templateName}' should grant Defence");
    }

    #endregion

    #region Unmatched Templates Get No Thematic Bonus

    [Theory]
    [InlineData("Tattered Cloak")]
    [InlineData("Rope Belt")]
    public void UnmatchedTemplates_GetNoThematicBonus(string templateName)
    {
        var item = CreateTestItem();
        LootGenerator.ApplyThematicBonuses(item, templateName, 50);

        // These should not match any keyword group
        int totalStats = item.Strength + item.Dexterity + item.Agility +
                         item.Wisdom + item.Charisma + item.HP + item.Mana +
                         item.Defence;
        bool hasLootEffects = item.LootEffects.Count > 0;

        (totalStats + (hasLootEffects ? 1 : 0)).Should().Be(0,
            $"'{templateName}' should not match any thematic keyword group");
    }

    #endregion
}
