using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using FluentAssertions;
using UsurperRemake.Systems;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.3: magic shop rings and necklaces built from the basic material templates (Leather Cord,
/// Bone Necklace, Silver Chain, Copper Ring, Silver Ring, Gold Ring and others) carry only armor,
/// which the listing, the purchase detail and the upgrade score ignored, so their rows were blank.
/// </summary>
[Collection("SharedGameSingletons")]
public class MagicShopAccessoryStats123Tests
{
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };

    public static IEnumerable<object[]> AllLanguages() => Langs.Select(l => new object[] { l });

    private static List<Equipment> ShopAccessories() =>
        EquipmentDatabase.GetShopRings().Concat(EquipmentDatabase.GetShopNecklaces()).ToList();

    private static T WithLanguage<T>(string lang, Func<T> body)
    {
        var prev = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            return body();
        }
        finally { GameConfig.Language = prev; }
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void EveryShopAccessory_ShowsAStatInTheListing(string lang)
    {
        var blank = WithLanguage(lang, () => ShopAccessories()
            .Where(e => string.IsNullOrEmpty(MagicShopLocation.GetAccessoryBonusDescription(e)))
            .Select(e => $"{e.Name} L{e.MinLevel}")
            .ToList());

        blank.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void ReportedItems_ShowTheirArmorInListingAndDetail(string lang)
    {
        var names = new[] { "Leather Cord", "Bone Necklace", "Silver Chain", "Copper Ring", "Silver Ring", "Gold Ring" };
        foreach (var name in names)
        {
            var item = ShopAccessories().First(e => e.Name == name);
            item.ArmorClass.Should().BeGreaterThan(0, name);
            string ac = $"{Loc.GetIn(lang, "ui.stat_ac")}+{item.ArmorClass}";
            WithLanguage(lang, () => MagicShopLocation.GetAccessoryBonusDescription(item)).Should().StartWith(ac, name);
            WithLanguage(lang, () => MagicShopLocation.GetAccessoryDetailedStats(item)).Should().Contain(ac, name);
        }
    }

    [Theory]
    [MemberData(nameof(AllLanguages))]
    public void ListingRow_FitsIn79Columns(string lang)
    {
        // Row layout in DisplayAccessoryItems: " 12. " + name padded to 26 + level (5) + price (12) + bonuses + " [+]".
        var tooWide = WithLanguage(lang, () => ShopAccessories()
            .Select(e => (e, width: 5 + Math.Max(26, e.Name.Length) + 5 + 12
                                   + MagicShopLocation.GetAccessoryBonusDescription(e).Length + 4))
            .Where(x => x.width > 79)
            .Select(x => $"{x.e.Name} L{x.e.MinLevel} = {x.width}")
            .ToList());

        tooWide.Should().BeEmpty();
    }

    [Fact]
    public void UpgradeScore_CountsArmor()
    {
        var low = new Equipment { Name = "Copper Ring", ArmorClass = 1 };
        var high = new Equipment { Name = "Copper Ring", ArmorClass = 4 };

        MagicShopLocation.GetAccessoryScore(high).Should().BeGreaterThan(MagicShopLocation.GetAccessoryScore(low));
    }
}
