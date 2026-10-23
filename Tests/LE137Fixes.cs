using System;
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
using G = UsurperReborn.Tests.HitRoll128Golden;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.7 (LE): three small game fixes. Titan's Resolve shows in max HP at once after an equip or unequip; the
/// Magic Shop's enchant name suffix is always added; a loot item's effect line survives the trip through Equipment.
/// State lives in memory on Character instances; the language, the king and the shop's random source are put back
/// in Dispose and the real save folder is neither read nor written.
/// </summary>
[Collection("SharedGameSingletons")]
public class LE137Fixes : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private readonly string _prevLanguage = GameConfig.Language;
    private readonly King _prevKing = CastleLocation.GetCurrentKing();
    private readonly Random _prevRandom = MagicShopLocation.EnchantRandom;

    public LE137Fixes()
    {
        GameConfig.Language = "en";
        CastleLocation.SetKing(null!);
        MagicShopLocation.EnchantRandom = new FixedRandom(0.99);
    }

    public void Dispose()
    {
        GameConfig.Language = _prevLanguage;
        CastleLocation.SetKing(_prevKing);
        MagicShopLocation.EnchantRandom = _prevRandom;
    }

    private sealed class FixedRandom : Random
    {
        private readonly double _roll;
        public FixedRandom(double roll) { _roll = roll; }
        public override double NextDouble() => _roll;
        public override int Next(int maxValue) => 0;
        public override int Next(int minValue, int maxValue) => minValue;
    }

    private static Equipment Plate(bool titan, string tag) => new()
    {
        Name = $"LE137 Plate {tag}", Slot = EquipmentSlot.Body, MinLevel = 1, Rarity = EquipmentRarity.Epic, HasTitanResolve = titan,
    };

    [Fact]
    public void LE137_Titan_EquipAndUnequip_LeaveMaxHPRightAtOnce()
    {
        var hero = G.Hero(5003, 100, 20);
        hero.RecalculateStats();
        long plain = hero.MaxHP;
        var plate = Plate(true, "a");
        EquipmentDatabase.RegisterDynamic(plate);

        hero.EquipItem(plate, out _).Should().BeTrue();
        hero.MaxHP.Should().Be((long)(plain * Character.TitanResolveMaxHPMultiplier), "equipping a Titan item shows the 5 percent at once");
        long shown = hero.MaxHP;
        hero.RecalculateStats();
        hero.MaxHP.Should().Be(shown, "the fight-start recalculation changes nothing");

        hero.UnequipSlot(EquipmentSlot.Body).Should().NotBeNull();
        hero.MaxHP.Should().Be(plain, "taking it off takes the 5 percent off at once");

        var other = Plate(true, "b");
        EquipmentDatabase.RegisterDynamic(other);
        hero.EquipItem(other, out _).Should().BeTrue();
        var plain2 = Plate(false, "c");
        EquipmentDatabase.RegisterDynamic(plain2);
        hero.EquipItem(plain2, out _).Should().BeTrue();
        hero.MaxHP.Should().Be(plain, "swapping the Titan piece for a plain one in the same slot drops the bonus");
    }

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

    private static Character ShopHero(string bladeName)
    {
        var hero = new Character
        {
            Name1 = "tester", Name2 = "Tester", Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 100,
            HP = 5000, MaxHP = 5000, BaseMaxHP = 5000, BaseStrength = 100, Strength = 100,
            AI = CharacterAI.Human, Gold = 9_000_000_000, AutoEquipDisabled = true,
        };
        hero.Achievements.Unlock("first_enchant");
        var blade = new Equipment { Name = bladeName, Slot = EquipmentSlot.MainHand, WeaponPower = 10, MinLevel = 1, Value = 100 };
        hero.EquippedItems[EquipmentSlot.MainHand] = EquipmentDatabase.RegisterDynamic(blade);
        hero.RecalculateStats();
        return hero;
    }

    [Fact]
    public async Task LE137_EnchantTag_LeftOffALongName_AndThePlayerIsTold()
    {
        const string longName = "Exceedingly Long Named Leech Blade";   // 34 characters: the tag would reach 41
        var hero = ShopHero(longName);
        string screen = await Run(hero, "EnchantEquipment", "1", "1", "2", "Y");

        var worn = hero.GetEquipment(EquipmentSlot.MainHand)!;
        worn.GetEnchantmentCount().Should().Be(1, "the enchant is applied either way");
        worn.StrengthBonus.Should().BeGreaterThan(0);
        worn.Name.Should().Be(longName, "the tag would push the name to 40 or more, so it stays off");
        screen.Should().Contain(Loc.GetIn("en", "magic_shop.enchant_tag_skipped"), "and the player is told");

        var shortHero = ShopHero("Short Blade");
        string shortScreen = await Run(shortHero, "EnchantEquipment", "1", "1", "2", "Y");
        shortHero.GetEquipment(EquipmentSlot.MainHand)!.Name.Should().MatchRegex(@"^Short Blade \+\d+ Str$");
        shortScreen.Should().NotContain(Loc.GetIn("en", "magic_shop.enchant_tag_skipped"), "a name that fits is told nothing");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public void LE137_EnchantTagSkippedText_IsTranslated_AndFits79(string lang)
    {
        const string key = "magic_shop.enchant_tag_skipped";
        Loc.HasIn(lang, key).Should().BeTrue();
        string text = Loc.GetIn(lang, key);
        text.Should().NotBe(key);
        if (lang != "en") text.Should().NotBe(Loc.GetIn("en", key), "a real translation");
        text.Length.Should().BeLessThanOrEqualTo(79);
    }

    [Fact]
    public void LE137_LootEffectLine_SurvivesBothConversions()
    {
        var loot = new Item { Name = "Flaming Test Blade", Type = ObjType.Weapon, Attack = 20, MinLevel = 1 };
        loot.Description[0] = "Fire Damage +5, Life Steal +3";
        loot.LootEffects.Add(((int)LootGenerator.SpecialEffect.FireDamage, 5));
        var hero = G.Hero(5003, 100, 20);

        var eq = Character.BuildEquipmentFromItem(loot, EquipmentSlot.MainHand, WeaponHandedness.OneHanded, WeaponType.Sword);
        var back = hero.ConvertEquipmentToLegacyItem(eq);

        back.Description[0].Should().Be("Fire Damage +5, Life Steal +3");
        back.GetFullDescription().Should().Contain("Fire Damage +5, Life Steal +3");
        // enchant markers and the line live together
        eq.IncrementEnchantmentCount();
        eq.GetEnchantmentCount().Should().Be(1);
        hero.ConvertEquipmentToLegacyItem(eq).Description[0].Should().Be("Fire Damage +5, Life Steal +3");
        // a save written before the fix: no line on the equipment, nothing is invented
        var old = new Equipment { Name = "Old Blade", Slot = EquipmentSlot.MainHand, Description = "[E:1]" };
        hero.ConvertEquipmentToLegacyItem(old).Description[0].Should().BeNullOrEmpty();
        old.GetEnchantmentCount().Should().Be(1);
    }
}
