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
/// 1.3.2: a Troll pays 10 percent less for a weapon at the Weapon Shop (HagglingEngine.ApplyRaceDiscount),
/// on the buy path and the auto-buy path, after every other price modifier and before the tax. The Weapon
/// Shop still refuses a Troll's haggle. The category list, the client browse rows and the auto-buy pre-filter
/// keep the raw item value. Every test runs with a court taxing 10 percent.
/// </summary>
[Collection("SharedGameSingletons")]
public class TrollDiscount132Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private readonly King? _oldKing;
    private readonly DifficultyMode _oldDifficulty;
    private readonly object? _oldFaction;
    private readonly object? _oldRank;
    private readonly float _oldWorldEvent;
    private readonly object? _oldBackend;
    private readonly string _oldLanguage;

    private static readonly PropertyInfo FactionProp = typeof(FactionSystem).GetProperty("PlayerFaction")!;
    private static readonly PropertyInfo RankProp = typeof(FactionSystem).GetProperty("FactionRank")!;
    private static readonly PropertyInfo WorldEventProp = typeof(WorldEventSystem).GetProperty("GlobalPriceModifier")!;
    private static readonly FieldInfo BackendField = typeof(SaveSystem).GetField("backend", F)!;

    public TrollDiscount132Tests()
    {
        _oldKing = CastleLocation.GetCurrentKing();
        _oldDifficulty = DifficultySystem.CurrentDifficulty;
        _oldFaction = FactionProp.GetValue(FactionSystem.Instance);
        _oldRank = RankProp.GetValue(FactionSystem.Instance);
        _oldWorldEvent = WorldEventSystem.Instance.GlobalPriceModifier;
        _oldBackend = BackendField.GetValue(SaveSystem.Instance);
        _oldLanguage = GameConfig.Language;

        GameConfig.Language = "en";
        CastleLocation.SetKing(new King { Name = "Test Crown", KingTaxPercent = 10, CityTaxPercent = 0 });
        BackendField.SetValue(SaveSystem.Instance, DispatchProxy.Create<ISaveBackend, MagicHaggle124Tests.NullBackend>());
        Apply(Scenario.Plain, null);
    }

    public void Dispose()
    {
        CastleLocation.SetKing(_oldKing);
        DifficultySystem.CurrentDifficulty = _oldDifficulty;
        FactionProp.SetValue(FactionSystem.Instance, _oldFaction);
        RankProp.SetValue(FactionSystem.Instance, _oldRank);
        WorldEventProp.SetValue(WorldEventSystem.Instance, _oldWorldEvent);
        BackendField.SetValue(SaveSystem.Instance, _oldBackend);
        GameConfig.Language = _oldLanguage;
    }

    // ---------- scenarios: the same modifiers for both races ----------

    public enum Scenario
    {
        /// <summary>Normal difficulty, no faction, neutral alignment, no event, no boon.</summary>
        Plain,
        /// <summary>Easy (0.85) and the Crown at rank 4 (0.9).</summary>
        EasyCrown,
        /// <summary>Hard (1.15) and the Crown at rank 4 (0.9).</summary>
        HardCrown,
        /// <summary>Good alignment at Paragon renown (0.9 x 0.9), a world event markup (1.2) and a god boon (5 percent off).</summary>
        GoodEventBoon,
    }

    private static void Apply(Scenario s, Character? hero)
    {
        DifficultySystem.CurrentDifficulty = s switch
        {
            Scenario.EasyCrown => DifficultyMode.Easy,
            Scenario.HardCrown => DifficultyMode.Hard,
            _ => DifficultyMode.Normal,
        };
        bool crown = s is Scenario.EasyCrown or Scenario.HardCrown;
        FactionProp.SetValue(FactionSystem.Instance, crown ? (Faction?)Faction.TheCrown : null);
        RankProp.SetValue(FactionSystem.Instance, crown ? 4 : 0);
        WorldEventProp.SetValue(WorldEventSystem.Instance, s == Scenario.GoodEventBoon ? 1.2f : 1.0f);
        if (hero != null && s == Scenario.GoodEventBoon)
        {
            hero.Chivalry = 500;
            hero.Darkness = 0;
            hero.CachedBoonEffects = new ActiveBoonEffects { ShopDiscountPercent = 0.05f };
        }
    }

    private static Character Hero(CharacterRace race, long gold, int level = 20) => new Character
    {
        Name1 = "td", Name2 = "Weapon Buyer", Class = CharacterClass.Warrior, Race = race,
        Level = level, HP = 200, MaxHP = 200, BaseMaxHP = 200, AI = CharacterAI.Human, Charisma = 250,
        Strength = 50, Gold = gold, AutoEquipDisabled = true,
    };

    // ---------- driving the shops ----------

    private static string Plain(TerminalEmulator term, MemoryStream output)
    {
        term.StreamWriterInternal?.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");
    }

    private sealed class Run
    {
        public long Charged;
        public string Text = "";
        public Equipment? Item;
    }

    private static T Open<T>(Character hero, out TerminalEmulator term, out MemoryStream output, params string[] lines) where T : BaseLocation, new()
    {
        var shop = new T();
        output = new MemoryStream();
        term = new TerminalEmulator(new LineStream(lines), output);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(shop, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(shop, hero);
        return shop;
    }

    private static object OneHanded()
    {
        var t = typeof(WeaponShopLocation).GetNestedType("WeaponCategory", BindingFlags.NonPublic)!;
        return Enum.Parse(t, "OneHanded");
    }

    private static List<Equipment> OneHandedList(WeaponShopLocation shop) =>
        ((System.Collections.IEnumerable)typeof(WeaponShopLocation).GetMethod("GetShopItemsForCategory", F)!
            .Invoke(shop, new[] { OneHanded() })!).Cast<Equipment>().ToList();

    /// <summary>Buys the named one-handed weapon of the given value on the buy path.</summary>
    private static async Task<Run> Buy(Character hero, string name, long value, params string[] lines)
    {
        var shop = Open<WeaponShopLocation>(hero, out var term, out var output, lines.Length > 0 ? lines : new[] { "Y", "", "" });
        var list = OneHandedList(shop);
        int index = list.FindIndex(w => w.Name == name && w.Value == value);
        index.Should().BeGreaterThanOrEqualTo(0, $"the shop sells a {name} at {value}");
        long start = hero.Gold;
        try
        {
            await (Task)typeof(WeaponShopLocation).GetMethod("BuyItem", F)!.Invoke(shop, new[] { OneHanded(), (object)(index + 1) })!;
        }
        catch (LocationExitException) { }
        return new Run { Charged = start - hero.Gold, Text = Plain(term, output), Item = list[index] };
    }

    /// <summary>Takes the first weapon auto-buy offers, in the main hand.</summary>
    private static async Task<Run> AutoBuy(Character hero)
    {
        var shop = Open<WeaponShopLocation>(hero, out var term, out var output, "Y", "M", "", "", "");
        long start = hero.Gold;
        try
        {
            await (Task)typeof(WeaponShopLocation).GetMethod("AutoBuyBestWeapon", F)!.Invoke(shop, Array.Empty<object>())!;
        }
        catch (LocationExitException) { }
        return new Run { Charged = start - hero.Gold, Text = Plain(term, output), Item = hero.GetEquipment(EquipmentSlot.MainHand) };
    }

    private static long Taxed(long price) => CityControlSystem.CalculateTaxedPrice(price).total;
    private static long TrollOf(long humanPreTax) => humanPreTax - (long)(humanPreTax * 0.1);

    /// <summary>The buy-path cases: a weapon and the Human's pre-tax price at 9c7b0b0e under the scenario.</summary>
    public static IEnumerable<object[]> BuyCases() => new[]
    {
        new object[] { Scenario.Plain, "Short Sword", 300L, 300L },
        new object[] { Scenario.Plain, "Dagger", 3354L, 3354L },
        new object[] { Scenario.EasyCrown, "Short Sword", 300L, 229L },
        new object[] { Scenario.HardCrown, "Dagger", 3354L, 3470L },
        new object[] { Scenario.GoodEventBoon, "Short Sword", 300L, 276L },
    };

    // ---------- 1. a Troll's price on each path ----------

    [Theory]
    [MemberData(nameof(BuyCases))]
    public async Task BuyPath_TrollPaysTenPercentLessThanAHuman_AndTheScreenShowsWhatIsCharged(Scenario s, string name, long value, long humanPre)
    {
        var human = Hero(CharacterRace.Human, 1_000_000);
        Apply(s, human);
        var h = await Buy(human, name, value);

        var troll = Hero(CharacterRace.Troll, 1_000_000);
        Apply(s, troll);
        var t = await Buy(troll, name, value);

        h.Charged.Should().Be(Taxed(humanPre));
        long trollPre = TrollOf(humanPre);
        t.Charged.Should().Be(Taxed(trollPre), "the 10 percent comes off after every other modifier and before the tax");
        t.Text.Should().Contain($"{ItemNames.Display(t.Item!)}: {trollPre:N0}", "the tax breakdown starts from the discounted price");
        t.Text.Should().Contain(Loc.Get("shop.buy_prompt_haggle", $"{t.Charged:N0}", 3), "the prompt shows what is charged");
    }

    /// <summary>C9-2: on these two cases the 10 percent taken before the faction or difficulty step gives a
    /// different number than taken after it, so the order is under test.</summary>
    [Theory]
    [InlineData(Scenario.EasyCrown, 300L, 207L, 206L)]
    [InlineData(Scenario.HardCrown, 3354L, 3123L, 3124L)]
    public void OrderCases_DifferWhenTheDiscountMovesBeforeFactionOrDifficulty(Scenario s, long value, long after, long before)
    {
        Apply(s, null);
        float faction = FactionSystem.Instance.GetShopPriceModifier();
        long human = DifficultySystem.ApplyShopPriceMultiplier((long)(value * faction));
        TrollOf(human).Should().Be(after);
        DifficultySystem.ApplyShopPriceMultiplier((long)(TrollOf(value) * faction)).Should().Be(before, "before the faction step");
        DifficultySystem.ApplyShopPriceMultiplier(TrollOf((long)(value * faction))).Should().Be(before, "before the difficulty step");
        after.Should().NotBe(before);
    }

    [Theory]
    [InlineData(Scenario.Plain)]
    [InlineData(Scenario.EasyCrown)]
    [InlineData(Scenario.HardCrown)]
    [InlineData(Scenario.GoodEventBoon)]
    public async Task AutoBuyPath_TrollPaysTenPercentLessThanAHuman_AndTheScreenShowsWhatIsCharged(Scenario s)
    {
        const long gold = 1_000_000;
        var human = Hero(CharacterRace.Human, gold, level: 1);
        Apply(s, human);
        var h = await AutoBuy(human);

        var troll = Hero(CharacterRace.Troll, gold, level: 1);
        Apply(s, troll);
        var t = await AutoBuy(troll);

        h.Item.Should().NotBeNull();
        t.Item!.Name.Should().Be(h.Item!.Name, "both are offered the same weapon");
        long humanPre = AutoBuyHumanPre(s);
        h.Charged.Should().Be(Taxed(humanPre));
        long trollPre = TrollOf(humanPre);
        t.Charged.Should().Be(Taxed(trollPre));
        t.Text.Should().Contain(Loc.Get("weapon_shop.autobuy_price", $"{trollPre:N0}"), "the price line shows the discounted price");
        t.Text.Should().Contain($"{ItemNames.Display(t.Item)}: {trollPre:N0}", "the tax breakdown starts from it");
        t.Text.Should().Contain(Loc.Get("weapon_shop.autobuy_gold_after", $"{gold - t.Charged:N0}"), "gold after is what is charged");
    }

    /// <summary>The Human's auto-buy pre-tax price at 9c7b0b0e: the 300 gold level 1 weapon through city,
    /// faction and difficulty only (auto-buy has no alignment, event or boon step).</summary>
    private static long AutoBuyHumanPre(Scenario s) => s switch
    {
        Scenario.EasyCrown => 229,
        Scenario.HardCrown => 310,
        _ => 300,
    };

    // ---------- 2. the gold check on the buy path ----------

    [Theory]
    [InlineData(Scenario.Plain, "Short Sword", 300L, 300L)]
    [InlineData(Scenario.HardCrown, "Dagger", 3354L, 3470L)]
    public async Task BuyPath_TrollWithExactlyTheDiscountedTotal_Buys_AndOneGoldLessCannot(Scenario s, string name, long value, long humanPre)
    {
        long total = Taxed(TrollOf(humanPre));

        var rich = Hero(CharacterRace.Troll, total);
        Apply(s, rich);
        var bought = await Buy(rich, name, value);
        rich.Gold.Should().Be(0, "exactly the discounted price plus tax buys it");
        rich.Inventory.Should().Contain(i => i.Name == name);
        bought.Charged.Should().Be(total);

        var poor = Hero(CharacterRace.Troll, total - 1);
        Apply(s, poor);
        var refused = await Buy(poor, name, value);
        poor.Gold.Should().Be(total - 1, "one gold less buys nothing");
        poor.Inventory.Should().NotContain(i => i.Name == name);
        refused.Text.Should().Contain(Loc.Get("shop.insufficient_gold", $"{total:N0}", $"{total - 1:N0}"));
    }

    // ---------- 3. a non-Troll's price is unchanged ----------

    [Theory]
    [MemberData(nameof(BuyCases))]
    public async Task BuyPath_NonTrollPrices_AreThePinnedOnes(Scenario s, string name, long value, long humanPre)
    {
        foreach (var race in new[] { CharacterRace.Human, CharacterRace.Orc, CharacterRace.Gnoll, CharacterRace.Elf })
        {
            var hero = Hero(race, 1_000_000);
            Apply(s, hero);
            var run = await Buy(hero, name, value);
            run.Charged.Should().Be(Taxed(humanPre), $"a {race} pays the price at 9c7b0b0e");
        }
    }

    [Theory]
    [InlineData(Scenario.Plain, 330L)]
    [InlineData(Scenario.EasyCrown, 251L)]
    [InlineData(Scenario.HardCrown, 341L)]
    [InlineData(Scenario.GoodEventBoon, 330L)]
    public async Task AutoBuyPath_NonTrollPrices_AreThePinnedOnes(Scenario s, long charged)
    {
        foreach (var race in new[] { CharacterRace.Human, CharacterRace.Orc })
        {
            var hero = Hero(race, 1_000_000, level: 1);
            Apply(s, hero);
            var run = await AutoBuy(hero);
            run.Charged.Should().Be(charged, $"a {race} pays the price at 9c7b0b0e");
        }
    }

    // ---------- 4. the haggle refusal stays ----------

    [Fact]
    public async Task WeaponShop_StillRefusesATrollsHaggle_WithTheTwoLines()
    {
        var troll = Hero(CharacterRace.Troll, 1_000_000);
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(new[] { "90", "Y" }), output);
        var result = await HagglingEngine.Haggle(troll, HagglingEngine.ShopType.Weapon, 100, "Keeper", term);
        result.Price.Should().Be(100);
        result.Kicked.Should().BeFalse();
        troll.WeapHag.Should().Be(3, "the refusal spends no attempt");
        string text = Plain(term, output);
        text.Should().Contain(Loc.Get("haggle.troll_already_discount"));
        text.Should().Contain(Loc.Get("haggle.troll_get_out"));
    }

    [Fact]
    public async Task WeaponShop_BuyScreen_RefusesATrollsHaggle()
    {
        var troll = Hero(CharacterRace.Troll, 1_000_000);
        var run = await Buy(troll, "Short Sword", 300, "H", "N");
        run.Text.Should().Contain(Loc.Get("haggle.troll_already_discount"));
        run.Charged.Should().Be(0);
    }

    [Fact]
    public async Task WeaponShop_StillHagglesWithANonTroll()
    {
        var human = Hero(CharacterRace.Human, 1_000_000);
        var term = new TerminalEmulator(new LineStream(new[] { "90", "Y" }), new MemoryStream());
        var result = await HagglingEngine.Haggle(human, HagglingEngine.ShopType.Weapon, 100, "Keeper", term);
        result.Price.Should().Be(90);
        human.WeapHag.Should().Be(2);
    }

    // ---------- 5. the Armor Shop is not touched ----------

    [Theory]
    [InlineData(Scenario.Plain)]
    [InlineData(Scenario.HardCrown)]
    public async Task ArmorShop_TrollPaysWhatAHumanPays(Scenario s)
    {
        async Task<long> ArmorCharge(CharacterRace race)
        {
            var hero = Hero(race, 1_000_000);
            Apply(s, hero);
            var shop = Open<ArmorShopLocation>(hero, out _, out _, "Y", "", "");
            long start = hero.Gold;
            try
            {
                await (Task)typeof(ArmorShopLocation).GetMethod("BuyItem", F)!.Invoke(shop, new object[] { EquipmentSlot.Body, 1 })!;
            }
            catch (LocationExitException) { }
            return start - hero.Gold;
        }

        long human = await ArmorCharge(CharacterRace.Human);
        human.Should().BeGreaterThan(0);
        (await ArmorCharge(CharacterRace.Troll)).Should().Be(human);
    }

    // ---------- 6. the main menu line ----------

    [Fact]
    public void MainMenu_ShowsTheTrollLine_OnlyToATroll()
    {
        string Menu(CharacterRace race)
        {
            var shop = Open<WeaponShopLocation>(Hero(race, 1000), out var term, out var output);
            typeof(WeaponShopLocation).GetMethod("ShowMainMenu", F)!.Invoke(shop, null);
            return Plain(term, output);
        }

        Loc.Has("weapon_shop.troll_discount").Should().BeTrue();
        string line = Loc.Get("weapon_shop.troll_discount", 10);
        Menu(CharacterRace.Troll).Should().Contain(line);
        Menu(CharacterRace.Human).Should().NotContain(line);
        Menu(CharacterRace.Orc).Should().NotContain(line);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public void TrollLine_IsTranslated_AndFitsIn79Columns(string lang)
    {
        Loc.HasIn(lang, "weapon_shop.troll_discount").Should().BeTrue(lang);
        string row = Loc.GetIn(lang, "weapon_shop.troll_discount", 10);
        row.Should().Contain("10");
        if (lang != "en") row.Should().NotBe(Loc.GetIn("en", "weapon_shop.troll_discount", 10), "a real translation");
        row.Length.Should().BeLessThanOrEqualTo(79, row);
    }
}
