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

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.5 (EF-10): Lifedrinker gives +6 percent life steal. The shop's tier table holds the number once: the enchant
/// adds it, a destroyed or removed Lifedrinker takes the same amount off, and the tier description shows it.
/// State lives in memory on Character instances; the language, the king and the shop's random source are put back in
/// Dispose and the real save folder is neither read nor written.
/// </summary>
[Collection("SharedGameSingletons")]
public class ShopEnchant135Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int LifedrinkerTier = 9;

    private readonly string _prevLanguage = GameConfig.Language;
    private readonly King _prevKing = CastleLocation.GetCurrentKing();
    private readonly Random _prevRandom = MagicShopLocation.EnchantRandom;

    public ShopEnchant135Tests()
    {
        GameConfig.Language = "en";
        CastleLocation.SetKing(null!);   // no sales tax leaves the test
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
        private readonly int _pick;
        public FixedRandom(double roll, int pick = 0) { _roll = roll; _pick = pick; }
        public override double NextDouble() => _roll;
        public override int Next(int maxValue) => Math.Min(_pick, Math.Max(0, maxValue - 1));
        public override int Next(int minValue, int maxValue) => Math.Clamp(minValue + _pick, minValue, Math.Max(minValue, maxValue - 1));
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

    private static Character Hero()
    {
        var hero = new Character
        {
            Name1 = "tester", Name2 = "Tester", Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 100,
            HP = 5000, MaxHP = 5000, BaseMaxHP = 5000, BaseStrength = 100, Strength = 100,
            AI = CharacterAI.Human, Gold = 9_000_000_000, AutoEquipDisabled = true,
        };
        hero.Achievements.Unlock("first_enchant");
        return hero;
    }

    /// <summary>A weapon with life steal of its own, so the enchant's amount shows on top of it.</summary>
    private static Equipment Blade() => new()
    {
        Name = "Leech Blade", Slot = EquipmentSlot.MainHand, WeaponPower = 10, LifeSteal = 2, MinLevel = 1, Value = 100,
    };

    private static Equipment Wear(Character hero, Equipment item)
    {
        hero.EquippedItems[item.Slot] = EquipmentDatabase.RegisterDynamic(item);
        hero.RecalculateStats();
        return item;
    }

    private static Equipment Worn(Character hero) => hero.GetEquipment(EquipmentSlot.MainHand)!;

    private static async Task<Character> HeroWithLifedrinker()
    {
        var hero = Hero();
        Wear(hero, Blade());
        await Run(hero, "EnchantEquipment", "1", $"{LifedrinkerTier}", "Y");
        Worn(hero).GetEnchantedKinds().Should().Equal("life");
        return hero;
    }

    [Fact]
    public async Task Lifedrinker_AddsSixLifeSteal_ToAWeapon()
    {
        var hero = await HeroWithLifedrinker();
        Worn(hero).LifeSteal.Should().Be(2 + 6, "Lifedrinker adds 6 life steal on top of the blade's own 2");
        Worn(hero).GetEnchantmentCount().Should().Be(1);
    }

    [Fact]
    public async Task Lifedrinker_DestroyedByAFailure_TakesSixOff()
    {
        var hero = await HeroWithLifedrinker();
        var (after, kind) = MagicShopLocation.DestroyOneEnchant(Worn(hero), new FixedRandom(0.0));
        kind.Should().Be("life");
        after.LifeSteal.Should().Be(2, "the destroyed Lifedrinker takes its 6 off and the blade keeps its own 2");
        after.GetEnchantmentCount().Should().Be(0);
    }

    [Fact]
    public async Task Lifedrinker_PaidRemoval_TakesSixOff()
    {
        var hero = await HeroWithLifedrinker();
        await Run(hero, "RemoveEnchantment", "1", "Y");
        Worn(hero).LifeSteal.Should().Be(2, "removal returns the blade to its own 2 life steal");
        Worn(hero).GetEnchantmentCount().Should().Be(0);
        Worn(hero).Name.Should().Be("Leech Blade");
    }

    [Fact]
    public void Lifedrinker_EnglishDescription_SaysSix()
    {
        MagicShopLocation.TierDescription(LifedrinkerTier - 1).Should().Be("+6% lifesteal");
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void Lifedrinker_Description_ShowsTheAmountTheEnchantAdds(string lang)
    {
        var e = new Equipment();
        MagicShopLocation.ApplyNamedEnchant(e, LifedrinkerTier);
        GameConfig.Language = lang;
        string shown = MagicShopLocation.TierDescription(LifedrinkerTier - 1);
        shown.Should().StartWith($"+{e.LifeSteal}%", $"{lang}: the description and the apply line read one number");
        shown.Should().NotContain("{");
    }
}
