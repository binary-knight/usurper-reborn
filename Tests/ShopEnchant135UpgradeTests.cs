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
/// 1.3.5 (EF-11): a stat tier with a higher bonus than the one an item carries for that stat replaces it. The lower
/// enchant's bonus, name tag and kind come off whole, the higher one goes on, the count stays, no 4th or 5th failure
/// roll is made and the new tier's gold is charged. Equal or lower is refused with nothing charged, as is an item whose
/// own record does not say which bonus it carries. Named enchants still never go on twice.
/// State lives in memory on Character instances; the language, the king and the shop's random source are put back in
/// Dispose and the real save folder is neither read nor written.
/// </summary>
[Collection("SharedGameSingletons")]
public class ShopEnchant135UpgradeTests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int Minor = 1, Superior = 4, Dex = 3, Str = 2;
    private const int BaseDex = 7;

    private readonly string _prevLanguage = GameConfig.Language;
    private readonly King _prevKing = CastleLocation.GetCurrentKing();
    private readonly Random _prevRandom = MagicShopLocation.EnchantRandom;

    public ShopEnchant135UpgradeTests()
    {
        GameConfig.Language = "en";
        CastleLocation.SetKing(null!);   // no sales tax leaves the test
        MagicShopLocation.EnchantRandom = Succeeds;
    }

    public void Dispose()
    {
        GameConfig.Language = _prevLanguage;
        CastleLocation.SetKing(_prevKing);
        MagicShopLocation.EnchantRandom = _prevRandom;
    }

    // ---------- helpers ----------

    private sealed class FixedRandom : Random
    {
        private readonly double _roll;
        public FixedRandom(double roll) { _roll = roll; }
        public override double NextDouble() => _roll;
        public override int Next(int maxValue) => 0;
        public override int Next(int minValue, int maxValue) => minValue;
    }

    private static readonly Random Succeeds = new FixedRandom(0.99);
    /// <summary>Any failure roll made with this fails, so an upgrade that rolled would show it.</summary>
    private static readonly Random AlwaysFails = new FixedRandom(0.0);

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

    private static Task<string> Enchant(Character hero, int tier, int stat = 0) =>
        stat > 0 ? Run(hero, "EnchantEquipment", "1", $"{tier}", $"{stat}", "Y")
                 : Run(hero, "EnchantEquipment", "1", $"{tier}", "Y");

    private static Character Hero()
    {
        var hero = new Character
        {
            Name1 = "tester", Name2 = "Tester", Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 100,
            HP = 5000, MaxHP = 5000, BaseMaxHP = 5000, BaseStrength = 100, Strength = 100,
            AI = CharacterAI.Human, Gold = 9_000_000_000, AutoEquipDisabled = true,
        };
        hero.Achievements.Unlock("first_enchant");
        hero.Achievements.Unlock("master_enchanter");
        return hero;
    }

    private static Equipment Blade() => new()
    {
        Name = "Plain Blade", Slot = EquipmentSlot.MainHand, WeaponPower = 10, StrengthBonus = 7, DexterityBonus = BaseDex,
        DefenceBonus = 7, WisdomBonus = 7, ArmorClass = 7, MinLevel = 1, Value = 100,
    };

    private static Equipment Wear(Character hero, Equipment item)
    {
        hero.EquippedItems[item.Slot] = EquipmentDatabase.RegisterDynamic(item);
        hero.RecalculateStats();
        return item;
    }

    private static Equipment Worn(Character hero) => hero.GetEquipment(EquipmentSlot.MainHand)!;

    private static string Fields(Equipment e) =>
        $"pow {e.WeaponPower} ac {e.ArmorClass} str {e.StrengthBonus} dex {e.DexterityBonus} con {e.ConstitutionBonus} "
        + $"int {e.IntelligenceBonus} wis {e.WisdomBonus} cha {e.CharismaBonus} def {e.DefenceBonus} sta {e.StaminaBonus} "
        + $"agi {e.AgilityBonus} crit {e.CriticalChanceBonus} critdmg {e.CriticalDamageBonus} mr {e.MagicResistance} "
        + $"ls {e.LifeSteal} fire {e.HasFireEnchant} frost {e.HasFrostEnchant} hp {e.MaxHPBonus} mana {e.MaxManaBonus}";

    private static async Task<Character> HeroWithMinorDex()
    {
        var hero = Hero();
        Wear(hero, Blade());
        await Enchant(hero, Minor, Dex);
        Worn(hero).DexterityBonus.Should().Be(BaseDex + 2);
        Worn(hero).GetEnchantedKinds().Should().Equal("dex");
        return hero;
    }

    /// <summary>The blade with Minor Dexterity first, then the named enchants given, each through the shop.</summary>
    private static async Task<Character> HeroWithDexAnd(params int[] namedTiers)
    {
        var hero = await HeroWithMinorDex();
        foreach (var tier in namedTiers) await Enchant(hero, tier);
        Worn(hero).GetEnchantmentCount().Should().Be(1 + namedTiers.Length);
        return hero;
    }

    private static int Count(string text, string part) => Regex.Matches(text, Regex.Escape(part)).Count;

    // ---------- the upgrade ----------

    [Fact]
    public async Task ShopEnchant135_Upgrade_MinorDexThenSuperior_HasEightNotTen()
    {
        var hero = await HeroWithMinorDex();
        long goldBefore = hero.Gold;
        await Enchant(hero, Superior, Dex);
        var after = Worn(hero);
        after.DexterityBonus.Should().Be(BaseDex + 8, "the Superior +8 replaces the Minor +2, it is not added to it (+10)");
        hero.Gold.Should().BeLessThan(goldBefore, "the new tier's gold is charged");
    }

    [Fact]
    public async Task ShopEnchant135_Upgrade_KeepsTheCount_OneKind_AndTheSuffixOnce()
    {
        var hero = await HeroWithMinorDex();
        int countBefore = Worn(hero).GetEnchantmentCount();
        await Enchant(hero, Superior, Dex);
        var after = Worn(hero);
        after.GetEnchantmentCount().Should().Be(countBefore, "an upgrade does not add to the enchant count");
        after.GetEnchantedKinds().Should().Equal(new[] { "dex" }, "one Dexterity kind code");
        after.Name.Should().Be("Plain Blade +8 Dex", "the name carries the new tag once and the old one is gone");
        Count(after.Name, " Dex").Should().Be(1);
    }

    [Fact]
    public async Task ShopEnchant135_LowerOrEqual_IsRefused_NothingCharged()
    {
        var hero = Hero();
        Wear(hero, Blade());
        await Enchant(hero, Superior, Dex);
        var before = Worn(hero);
        string fieldsBefore = Fields(before), nameBefore = before.Name;
        long goldBefore = hero.Gold;

        foreach (var tier in new[] { Minor, Superior })
        {
            string text = await Enchant(hero, tier, Dex);
            text.Should().Contain("already carries that enchantment", $"tier {tier} is not higher than +8 and is refused as before");
            hero.Gold.Should().Be(goldBefore, $"nothing is charged for a refused tier {tier}");
            Fields(Worn(hero)).Should().Be(fieldsBefore);
            Worn(hero).Name.Should().Be(nameBefore);
            Worn(hero).GetEnchantmentCount().Should().Be(1);
        }
    }

    [Fact]
    public async Task ShopEnchant135_Upgrade_AtCountFive_MakesNoFailureRoll_AndKeepsFive_AndTheOthersStay()
    {
        var hero = await HeroWithDexAnd(5, 7, 8, 9);   // Divine Blessing, Ward, Predator, Lifedrinker
        Worn(hero).GetEnchantmentCount().Should().Be(GameConfig.MaxEnchantments);
        var kindsBefore = Worn(hero).GetEnchantedKinds();

        MagicShopLocation.EnchantRandom = AlwaysFails;
        string text = await Enchant(hero, Superior, Dex);
        text.Should().NotContain(Loc.Get("magic_shop.fail_destroyed"), "an upgrade makes no failure roll");
        var after = Worn(hero);
        after.GetEnchantmentCount().Should().Be(GameConfig.MaxEnchantments, "the upgrade keeps 5");
        after.GetEnchantedKinds().Should().BeEquivalentTo(kindsBefore, "the same five kinds");

        var expected = Blade();
        foreach (var tier in new[] { 5, 7, 8, 9 }) MagicShopLocation.ApplyNamedEnchant(expected, tier);
        expected.DexterityBonus += 8;
        Fields(after).Should().Be(Fields(expected), "every other enchant keeps its full effect and Dexterity carries +8 once");
    }

    [Fact]
    public async Task ShopEnchant135_FullItem_NewKind_IsStillRefused()
    {
        var hero = await HeroWithDexAnd(5, 7, 8, 9);
        long goldBefore = hero.Gold;
        string text = await Enchant(hero, Minor, Str);
        text.Should().Contain(Loc.Get("shop.max_enchantments", GameConfig.MaxEnchantments));
        hero.Gold.Should().Be(goldBefore);
        Worn(hero).GetEnchantmentCount().Should().Be(GameConfig.MaxEnchantments, "the cap of 5 holds");
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")] [InlineData("es")] [InlineData("fr")] [InlineData("it")]
    public async Task ShopEnchant135_Upgrade_OnThreeEnchants_ShowsTheReplaceLine_NotTheFailurePercent(string lang)
    {
        var hero = await HeroWithDexAnd(5, 7);
        GameConfig.Language = lang;
        MagicShopLocation.EnchantRandom = AlwaysFails;
        string text = await Enchant(hero, Superior, Dex);

        int at = text.IndexOf("Plain Blade", text.IndexOf(Loc.Get("magic_shop.choose_stat"), StringComparison.Ordinal), StringComparison.Ordinal);
        at.Should().BeGreaterThan(0, "the confirm line names the item");
        string afterConfirm = text.Substring(at);
        var rows = MagicShopLocation.WrapRows($"  {Loc.Get("magic_shop.enchant_upgrade_replaces", 2, MagicShopLocation.StatLabel(Dex))}");
        foreach (var row in rows)
        {
            afterConfirm.Should().Contain(row.Trim(), $"{lang}: the replace line is shown");
            if (lang == "en" || lang == "hu") row.Length.Should().BeLessOrEqualTo(79, $"{lang} row \"{row}\"");
        }
        afterConfirm.Should().NotContain(Loc.Get("magic_shop.enchant_risk_1", 3), $"{lang}: no 4th enchant warning on an upgrade");
        afterConfirm.Should().NotContain(Loc.Get("magic_shop.enchant_risk_2", $"{GameConfig.FourthEnchantFailChance * 100f:F0}"),
            $"{lang}: no failure percent on an upgrade");
        afterConfirm.Should().NotContain("%", $"{lang}: no percent at all after the confirm line");
        Worn(hero).GetEnchantmentCount().Should().Be(3);
        Worn(hero).DexterityBonus.Should().Be(BaseDex + 3 + 8, "Divine Blessing's 3 plus the Superior 8");
    }

    [Fact]
    public async Task ShopEnchant135_Upgrade_SurvivesASaveRoundTrip()
    {
        var hero = await HeroWithDexAnd(7);
        await Enchant(hero, Superior, Dex);
        Worn(hero).DexterityBonus.Should().Be(BaseDex + 8);

        var data = (PlayerData)typeof(SaveSystem).GetMethod("SerializePlayer", F)!.Invoke(SaveSystem.Instance, new object[] { hero })!;
        var json = System.Text.Json.JsonSerializer.Serialize(data);
        data = System.Text.Json.JsonSerializer.Deserialize<PlayerData>(json)!;
        Character loaded;
        try { loaded = (Character)typeof(GameEngine).GetMethod("RestorePlayerFromSaveData", F)!.Invoke(GameEngine.Instance, new object[] { data })!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }

        var back = loaded.GetEquipment(EquipmentSlot.MainHand)!;
        back.Should().NotBeNull();
        back.DexterityBonus.Should().Be(BaseDex + 8, "the saved upgrade keeps +8 Dexterity");
        back.GetEnchantmentCount().Should().Be(2, "and its count");
        back.GetEnchantedKinds().Should().BeEquivalentTo(new[] { "dex", "ward" });
        MagicShopLocation.CarriedStatBonus(back, Dex).Should().Be(8, "the loaded item still records which bonus it carries");
    }

    // ---------- what is still refused ----------

    [Fact]
    public async Task ShopEnchant135_NoBaseForm_IsRefusedAsBefore_NothingCharged()
    {
        var hero = Hero();
        var old = new Equipment
        {
            Name = "Rolled Blade of Nowhere +2 Dex", Slot = EquipmentSlot.MainHand, WeaponPower = 33, DexterityBonus = 9,
            MinLevel = 1, Value = 5000,
        };
        old.IncrementEnchantmentCount();
        old.AddEnchantedKind("dex");
        Wear(hero, old);
        MagicShopLocation.EnchantBaseOf(Worn(hero)).Should().BeNull("this item does not record its own form");
        MagicShopLocation.CarriedStatBonus(Worn(hero), Dex).Should().BeNull();
        string fieldsBefore = Fields(Worn(hero));
        long goldBefore = hero.Gold;

        string text = await Enchant(hero, Superior, Dex);
        text.Should().Contain("already carries that enchantment", "an item that does not record its bonus is refused as before");
        hero.Gold.Should().Be(goldBefore);
        Fields(Worn(hero)).Should().Be(fieldsBefore);
        Worn(hero).GetEnchantmentCount().Should().Be(1);
    }

    [Theory]
    [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(13)] [InlineData(14)]
    public async Task ShopEnchant135_NamedEnchant_NeverGoesOnTwice(int tier)
    {
        var hero = Hero();
        Wear(hero, Blade());
        var kind = MagicShopLocation.NamedEnchantTag(tier);
        kind.Should().NotBeEmpty();
        // the materials and the awakening a tier needs are not this test's subject: a named kind already on the list
        var worn = Worn(hero);
        worn.RecordEnchantBase();
        worn.IncrementEnchantmentCount();
        string code = new[] { (5, "bless"), (6, "ocean"), (7, "ward"), (8, "pred"), (9, "life"), (13, "fire"), (14, "frost") }
            .Single(p => p.Item1 == tier).Item2;
        worn.AddEnchantedKind(code);
        MagicShopLocation.ApplyNamedEnchant(worn, tier);
        string fieldsBefore = Fields(worn);
        long goldBefore = hero.Gold;
        foreach (var m in new[] { "fading_starlight_dust", "crimson_war_shard", "shadow_silk_thread" }) hero.AddMaterial(m, 2);

        string text = await Enchant(hero, tier);
        if (tier != 6 || (OceanPhilosophySystem.Instance?.AwakeningLevel ?? 0) >= 2)
            text.Should().Contain("already carries that enchantment", $"named tier {tier} is never put on twice");
        hero.Gold.Should().Be(goldBefore);
        Fields(Worn(hero)).Should().Be(fieldsBefore);
        Worn(hero).GetEnchantmentCount().Should().Be(1);
    }
}
