using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.2: every framed header of the Magic Shop is at most 79 columns wide (it was 80), and enchant removal
/// refuses a cursed item the way enchanting does: the list marks it, the refusal lines are the enchant ones,
/// no gold is taken and nothing changes on the item.
/// State lives in memory on Character instances; the language and the king are put back in Dispose and the
/// real save folder is neither read nor written.
/// </summary>
[Collection("SharedGameSingletons")]
public class MagicShop132Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int MaxWidth = 79;
    private const int StatTier = 1, DefenceStat = 4;

    private readonly string _prevLanguage = GameConfig.Language;
    private readonly King _prevKing = CastleLocation.GetCurrentKing();

    public MagicShop132Tests()
    {
        GameConfig.Language = "en";
        CastleLocation.SetKing(null!);
    }

    public void Dispose()
    {
        GameConfig.Language = _prevLanguage;
        CastleLocation.SetKing(_prevKing);
    }

    // ---------- helpers ----------

    private static async Task<string> Run(Character hero, string method, params string[] lines)
    {
        var shop = new MagicShopLocation();
        using var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(lines.Concat(Enumerable.Repeat("", 12))), output);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(shop, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(shop, hero);
        var r = typeof(MagicShopLocation).GetMethod(method, F)!.Invoke(shop, new object[] { hero });
        if (r is Task t) await t;
        term.StreamWriterInternal?.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "");
    }

    private static Character Hero()
    {
        var hero = new Character
        {
            Name1 = "tester", Name2 = "Tester", Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 100,
            HP = 5000, MaxHP = 5000, BaseMaxHP = 5000, BaseStrength = 100, Strength = 100, Mana = 100, MaxMana = 100,
            AI = CharacterAI.Human, Gold = 9_000_000_000, AutoEquipDisabled = true,
        };
        hero.Achievements.Unlock("first_enchant");
        return hero;
    }

    private static Equipment Wear(Character hero, EquipmentSlot slot, Equipment item)
    {
        hero.EquippedItems[slot] = EquipmentDatabase.RegisterDynamic(item);
        hero.RecalculateStats();
        return item;
    }

    private static Equipment Cap(string name = "Plain Cap") =>
        new() { Name = name, Slot = EquipmentSlot.Head, ArmorClass = 3, MinLevel = 1, Value = 100 };

    /// <summary>A worn cap with one Defence enchant, written by the shop's own enchant flow. Head is the first row.</summary>
    private static async Task<Character> EnchantedHero(bool cursed)
    {
        var hero = Hero();
        Wear(hero, EquipmentSlot.Head, Cap());
        await Run(hero, "EnchantEquipment", "1", $"{StatTier}", $"{DefenceStat}", "Y");
        hero.GetEquipment(EquipmentSlot.Head)!.GetEnchantmentCount().Should().Be(1);
        if (cursed)
        {
            hero.GetEquipment(EquipmentSlot.Head)!.IsCursed = true;
            hero.RecalculateStats();
        }
        return hero;
    }

    private static List<string> BoxRows(string text) =>
        text.Replace("\r", "").Split('\n').Where(r => r.Length > 0 && "╔║╚".IndexOf(r[0]) >= 0).ToList();

    private static string Stats(Character h) =>
        $"{h.Strength}|{h.Dexterity}|{h.Constitution}|{h.Intelligence}|{h.Wisdom}|{h.Charisma}|{h.MaxHP}|{h.MaxMana}|"
        + $"{h.Defence}|{h.Stamina}|{h.Agility}|{h.WeapPow}|{h.ArmPow}|{h.HP}|{h.Mana}";

    // ---------- 1. the framed headers ----------

    private static async Task<(string screen, string text)[]> EveryBox()
    {
        var shown = new List<(string, string)>();

        var hero = Hero();
        var shop = new MagicShopLocation();
        using var menuOut = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(Enumerable.Repeat("", 12)), menuOut);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(shop, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(shop, hero);
        typeof(MagicShopLocation).GetMethod("DisplayMagicShopMenu", F)!.Invoke(shop, new object[] { hero });
        term.StreamWriterInternal?.Flush();
        shown.Add(("shop menu", Regex.Replace(Encoding.UTF8.GetString(menuOut.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "")));

        // The enchant list box, then the enchanting box named with the longest name an enchant leaves on an item.
        var longHero = Hero();
        Wear(longHero, EquipmentSlot.Head, Cap("Abyssal Studded Leather Cap of Fortitu"));
        shown.Add(("enchant list and enchanting title", await Run(longHero, "EnchantEquipment", "1", "0")));

        shown.Add(("love spell", await Run(Hero(), "CastLoveSpell", "0")));
        shown.Add(("dark arts", await Run(Hero(), "CastDeathSpell", "0")));
        return shown.ToArray();
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("it")]
    public async Task EveryMagicShopBox_IsAtMost79ColumnsWide(string lang)
    {
        GameConfig.Language = lang;
        foreach (var (screen, text) in await EveryBox())
        {
            var rows = BoxRows(text);
            rows.Count.Should().BeGreaterOrEqualTo(3, $"[{lang}] the {screen} drew its box: {text.Length} characters shown");
            foreach (var row in rows)
                row.Length.Should().BeLessOrEqualTo(MaxWidth, $"[{lang}] the {screen} box fits 79 columns: \"{row}\"");
        }
    }

    // ---------- 2. a cursed item in enchant removal ----------

    [Fact]
    public async Task Removal_RefusesAWornCursedItem_LikeEnchanting()
    {
        var hero = await EnchantedHero(cursed: true);
        var item = hero.GetEquipment(EquipmentSlot.Head)!;
        long gold = hero.Gold; var id = item.Id;
        string stats = Stats(hero), name = item.Name;

        string text = await Run(hero, "RemoveEnchantment", "1", "Y");

        string tag = Loc.GetIn("en", "shop.cursed_tag");
        text.Should().Contain($"{Loc.GetIn("en", "shop.cursed_tag")}", "the list marks the cursed item as enchanting does");
        text.Should().Contain(Loc.GetIn("en", "magic_shop.cursed_no_enchant"));
        text.Should().Contain(Loc.GetIn("en", "magic_shop.remove_curse_first"));
        text.Should().NotContain(Loc.GetIn("en", "magic_shop.remove_dissolve"));
        hero.Gold.Should().Be(gold, "nothing is charged");
        hero.GetEquipment(EquipmentSlot.Head)!.Id.Should().Be(id, "the same item is still worn");
        item.GetEnchantmentCount().Should().Be(1, "the enchant stays");
        item.IsCursed.Should().BeTrue();
        item.Name.Should().Be(name);
        Stats(hero).Should().Be(stats);
        tag.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Removal_ListMarksTheCursedItem_InHungarianToo()
    {
        GameConfig.Language = "hu";
        var hero = await EnchantedHero(cursed: true);
        string text = await Run(hero, "RemoveEnchantment", "0");
        text.Should().Contain(Loc.GetIn("hu", "shop.cursed_tag"));
    }

    // ---------- 3. an uncursed item is stripped as before ----------

    [Fact]
    public async Task Removal_StillStripsAWornUncursedEnchantedItem()
    {
        var hero = await EnchantedHero(cursed: false);
        long gold = hero.Gold;
        long price = (long)typeof(MagicShopLocation).GetMethod("ApplyAllPriceModifiers", F)!
            .Invoke(new MagicShopLocation(), new object[] { 5000L + hero.Level * 200L, hero })!;

        string text = await Run(hero, "RemoveEnchantment", "1", "Y");

        text.Should().NotContain(Loc.GetIn("en", "shop.cursed_tag"));
        text.Should().NotContain(Loc.GetIn("en", "magic_shop.cursed_no_enchant"));
        text.Should().Contain(Loc.GetIn("en", "magic_shop.remove_dissolve"));
        hero.GetEquipment(EquipmentSlot.Head)!.GetEnchantmentCount().Should().Be(0);
        (gold - hero.Gold).Should().Be(price);
    }
}
