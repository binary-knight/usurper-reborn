using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.4 (design item C, magic shop part): the magic shop accessory buy confirm offers [H]aggle
/// like the weapon and armor shops, with its own persisted attempt counter (MagicHag) and its own
/// day-stamped bar (MagicShopBarredUntilDay) that the shop entry gate reads.
/// </summary>
[Collection("SharedGameSingletons")]
public class MagicHaggle124Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int Day = 40;

    private static Character Hero(long gold = 10_000_000) => new Character
    {
        Name1 = "mh", Name2 = "Magic Haggler", Class = CharacterClass.Warrior, Race = CharacterRace.Human,
        Level = 20, HP = 200, MaxHP = 200, BaseMaxHP = 200, AI = CharacterAI.Human, Charisma = 250,
        Gold = gold, AutoEquipDisabled = true,
    };

    private static string Plain(TerminalEmulator term, MemoryStream output)
    {
        term.StreamWriterInternal?.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");
    }

    private sealed class Shop
    {
        public MagicShopLocation Location = new();
        public TerminalEmulator Term = null!;
        public MemoryStream Output = new();
        public string Text => Plain(Term, Output);
    }

    private static Shop Open(Character hero, params string[] lines)
    {
        var s = new Shop();
        s.Term = new TerminalEmulator(new LineStream(lines), s.Output);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(s.Location, s.Term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(s.Location, hero);
        return s;
    }

    /// <summary>Runs body with a court taxing 10 percent and a fixed session day, restoring both.</summary>
    private static async Task WithTaxAndDay(Func<Task> body)
    {
        var oldKing = CastleLocation.GetCurrentKing();
        int oldDay = GameEngine.Instance.SessionCurrentDay;
        try
        {
            CastleLocation.SetKing(new King { Name = "Test Crown", KingTaxPercent = 10, CityTaxPercent = 0 });
            GameEngine.Instance.SessionCurrentDay = Day;
            await body();
        }
        finally
        {
            CastleLocation.SetKing(oldKing);
            GameEngine.Instance.SessionCurrentDay = oldDay;
        }
    }

    /// <summary>The first ring on page one and its pre-tax adjusted price for this hero.</summary>
    private static (Equipment item, long price) FirstRing(Shop s, Character hero)
    {
        var items = (System.Collections.IList)typeof(MagicShopLocation).GetMethod("GetShopItemsForCategory", F)!
            .Invoke(s.Location, new object[] { RingsCategory() })!;
        var item = (Equipment)items[0]!;
        long price = (long)typeof(MagicShopLocation).GetMethod("ApplyAllPriceModifiers", F)!
            .Invoke(s.Location, new object[] { item.Value, hero })!;
        return (item, price);
    }

    private static object RingsCategory()
    {
        var t = typeof(MagicShopLocation).GetNestedType("AccessoryCategory", BindingFlags.NonPublic)!;
        return Enum.Parse(t, "Rings");
    }

    /// <summary>A save backend that writes nothing, so the purchase's autosave stays off disk.</summary>
    public class NullBackend : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            var rt = method!.ReturnType;
            if (rt == typeof(Task<bool>)) return Task.FromResult(true);
            if (rt == typeof(Task)) return Task.CompletedTask;
            if (rt.IsGenericType && rt.GetGenericTypeDefinition() == typeof(Task<>))
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(rt.GetGenericArguments()[0])
                    .Invoke(null, new object?[] { null });
            if (rt == typeof(string)) return "";
            if (rt == typeof(bool)) return false;
            if (rt == typeof(void)) return null;
            return Activator.CreateInstance(rt);
        }
    }

    private static async Task<Exception?> Buy(Shop s, Character hero)
    {
        var field = typeof(SaveSystem).GetField("backend", F)!;
        var real = field.GetValue(SaveSystem.Instance);
        try
        {
            field.SetValue(SaveSystem.Instance, DispatchProxy.Create<ISaveBackend, NullBackend>());
            await (Task)typeof(MagicShopLocation).GetMethod("BuyAccessoryItem", F)!
                .Invoke(s.Location, new object[] { RingsCategory(), 1, hero })!;
            return null;
        }
        catch (LocationExitException ex) { return ex; }
        finally { field.SetValue(SaveSystem.Instance, real); }
    }

    private static long Taxed(long price) => CityControlSystem.CalculateTaxedPrice(price).total;

    // ---------- the prompt ----------

    [Fact]
    public async Task Prompt_OffersHaggle_WithTheCount()
    {
        await WithTaxAndDay(async () =>
        {
            var hero = Hero();
            hero.MagicHag = 2;
            var s = Open(hero, "N");
            var (_, price) = FirstRing(s, hero);
            await Buy(s, hero);
            s.Text.Should().Contain(Loc.Get("shop.buy_prompt_haggle", $"{Taxed(price):N0}", 2));
            hero.MagicHag.Should().Be(2, "declining spends nothing");
        });
    }

    [Fact]
    public async Task Prompt_AtZeroAttempts_DoesNotOfferHaggle()
    {
        await WithTaxAndDay(async () =>
        {
            var hero = Hero();
            hero.MagicHag = 0;
            var s = Open(hero, "N");
            var (_, price) = FirstRing(s, hero);
            await Buy(s, hero);
            s.Text.Should().Contain(Loc.Get("shop.buy_prompt_no_haggle", $"{Taxed(price):N0}"));
            s.Text.Should().NotContain("[H]");
        });
    }

    // ---------- haggling ----------

    [Fact]
    public async Task Haggle_SpendsOneAttempt_AndChargesTheAgreedPriceWithTaxOnIt()
    {
        await WithTaxAndDay(async () =>
        {
            var hero = Hero();
            var probe = Open(hero);
            var (item, price) = FirstRing(probe, hero);
            long offer = price - price / 10;
            offer.Should().BeLessThan(price);
            long start = hero.Gold;

            var s = Open(hero, "H", offer.ToString(), "Y", "Y", "", "");
            (await Buy(s, hero)).Should().BeNull();

            hero.MagicHag.Should().Be(2, "one H spends exactly one attempt");
            hero.Gold.Should().Be(start - Taxed(offer), "the agreed price is charged with tax on the agreed amount");
            (Taxed(price) - price).Should().NotBe(Taxed(offer) - offer, "the check must tell the two tax bases apart");
            hero.Inventory.Should().Contain(i => i.Name == item.Name);
            var (kt, ct, _) = CityControlSystem.CalculateTaxedPrice(offer);
            s.Text.Should().Contain(Loc.Get("shop.haggle_price_agreed", $"{offer:N0}", $"{kt + ct:N0}"));
        });
    }

    [Fact]
    public async Task Haggle_TwoHaggles_SpendTwoAttempts()
    {
        await WithTaxAndDay(async () =>
        {
            var hero = Hero();
            var s = Open(hero, "H", "junk", "H", "junk", "N");
            await Buy(s, hero);
            hero.MagicHag.Should().Be(1);
            hero.WeapHag.Should().Be(3); hero.ArmHag.Should().Be(3);
        });
    }

    [Fact]
    public async Task Haggle_PlayerWhoCanAffordOnlyTheDiscount_StillBuys()
    {
        await WithTaxAndDay(async () =>
        {
            var probeHero = Hero();
            var (item, price) = FirstRing(Open(probeHero), probeHero);
            long offer = price - price / 10;
            var hero = Hero(gold: Taxed(offer));
            hero.Gold.Should().BeLessThan(Taxed(price));

            var s = Open(hero, "H", offer.ToString(), "Y", "Y", "", "");
            await Buy(s, hero);

            hero.Gold.Should().Be(0);
            hero.Inventory.Should().Contain(i => i.Name == item.Name);
        });
    }

    [Fact]
    public async Task NoHaggle_PlayerShortOfTheFullPrice_IsRefusedAfterConfirm()
    {
        await WithTaxAndDay(async () =>
        {
            var probeHero = Hero();
            var (_, price) = FirstRing(Open(probeHero), probeHero);
            long offer = price - price / 10;
            var hero = Hero(gold: Taxed(offer));

            var s = Open(hero, "Y", "");
            await Buy(s, hero);

            hero.Gold.Should().Be(Taxed(offer), "affordability is checked again on the final price");
            hero.Inventory.Should().BeEmpty();
        });
    }

    // ---------- kick-out, gate and day ----------

    [Fact]
    public async Task KickOut_SetsTheMagicBarDay_AndLeavesToMainStreet()
    {
        await WithTaxAndDay(async () =>
        {
            var hero = Hero();
            hero.MagicHag = 0;
            var s = Open(hero, "H", "Y");
            var exit = await Buy(s, hero);

            exit.Should().BeOfType<LocationExitException>();
            ((LocationExitException)exit!).DestinationLocation.Should().Be(GameLocation.MainStreet);
            hero.MagicShopBarredUntilDay.Should().Be(Day + 1);
            hero.IsBarredFromMagicShop(Day).Should().BeTrue();
            hero.IsBarredFromMagicShop(Day + 1).Should().BeFalse("the bar expires with the day");
            hero.WeaponShopBarredUntilDay.Should().Be(0); hero.ArmorShopBarredUntilDay.Should().Be(0);
        });
    }

    [Fact]
    public async Task Gate_BlocksEntry_OnTheBarDay()
    {
        await WithTaxAndDay(async () =>
        {
            var hero = Hero();
            hero.MagicShopBarredUntilDay = Day + 1;
            var s = Open(hero);
            typeof(MagicShopLocation).GetMethod("DisplayLocation", F)!.Invoke(s.Location, null);
            s.Text.Should().Contain(Loc.Get("armor_shop.kicked_out_1"));

            Exception? thrown = null;
            try { await (Task<bool>)typeof(MagicShopLocation).GetMethod("ProcessChoice", F)!.Invoke(s.Location, new object[] { "1" })!; }
            catch (LocationExitException ex) { thrown = ex; }
            thrown.Should().BeOfType<LocationExitException>();
            ((LocationExitException)thrown!).DestinationLocation.Should().Be(GameLocation.MainStreet);
        });
    }

    [Fact]
    public async Task Gate_ReadsTheBarDay_NotTheAttemptCount()
    {
        await WithTaxAndDay(async () =>
        {
            var hero = Hero();
            hero.MagicHag = 0;
            hero.MagicShopBarredUntilDay = Day; // yesterday's bar, expired
            var s = Open(hero);
            try { typeof(MagicShopLocation).GetMethod("DisplayLocation", F)!.Invoke(s.Location, null); }
            catch (TargetInvocationException) { /* the full menu may need more world; the gate runs first */ }
            s.Text.Should().NotContain(Loc.Get("armor_shop.kicked_out_1"), "spending the attempts must not bar the shop");
        });
    }

    [Fact]
    public void NextDay_ClearsTheMagicBar_AndRefillsAttempts()
    {
        var hero = Hero();
        hero.MagicHag = 0; hero.MagicShopBarredUntilDay = 9;
        HagglingEngine.ResetDailyHaggling(hero);
        hero.MagicHag.Should().Be(3);
        hero.MagicShopBarredUntilDay.Should().Be(0);
    }

    // ---------- persistence ----------

    [Fact]
    public void MagicAttemptsAndBar_RoundTripThroughTheSave()
    {
        var p = Hero();
        p.MagicHag = 1; p.MagicShopBarredUntilDay = 17; p.Level = 3; p.HP = 30; p.MaxHP = 30; p.BaseMaxHP = 30;

        var serialize = typeof(SaveSystem).GetMethod("SerializePlayer", F)!;
        var data = (PlayerData)serialize.Invoke(SaveSystem.Instance, new object[] { p })!;
        var back = JsonSerializer.Deserialize<PlayerData>(JsonSerializer.Serialize(data))!;
        back.MagicHag.Should().Be(1); back.MagicShopBarredUntilDay.Should().Be(17);

        var restore = typeof(GameEngine).GetMethod("RestorePlayerFromSaveData", F)!;
        Character restored;
        try { restored = (Character)restore.Invoke(GameEngine.Instance, new object[] { back })!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
        restored.MagicHag.Should().Be(1);
        restored.MagicShopBarredUntilDay.Should().Be(17);
    }

    [Fact]
    public void LegacySave_WithoutTheMagicFields_GetsThreeAttemptsAndNoBar()
    {
        var back = JsonSerializer.Deserialize<PlayerData>("{\"WeapHag\":1,\"ArmHag\":2}")!;
        back.MagicHag.Should().Be(3);
        back.MagicShopBarredUntilDay.Should().Be(0);
    }

    // ---------- weapon and armor unchanged ----------

    [Fact]
    public void ShopTypes_KeepTheirValues_AndCountersStaySeparate()
    {
        ((int)HagglingEngine.ShopType.Weapon).Should().Be('W');
        ((int)HagglingEngine.ShopType.Armor).Should().Be('A');
        var p = Hero();
        p.MagicHag = 0;
        HagglingEngine.CanHaggle(p, HagglingEngine.ShopType.Weapon).Should().BeTrue();
        HagglingEngine.CanHaggle(p, HagglingEngine.ShopType.Armor).Should().BeTrue();
        HagglingEngine.CanHaggle(p, HagglingEngine.ShopType.Magic).Should().BeFalse();
        p.WeapHag = 0; p.ArmHag = 0; p.MagicHag = 2;
        HagglingEngine.CanHaggle(p, HagglingEngine.ShopType.Magic).Should().BeTrue();
        HagglingEngine.GetHagglingAttemptsLeft(p, HagglingEngine.ShopType.Weapon).Should().Be(0);
        HagglingEngine.GetHagglingAttemptsLeft(p, HagglingEngine.ShopType.Magic).Should().Be(2);
        p.IsBarredFromWeaponShop(Day).Should().BeFalse(); p.IsBarredFromArmorShop(Day).Should().BeFalse();
    }

    [Fact]
    public async Task WeaponHaggle_StillSpendsOnlyTheWeaponCounter()
    {
        var p = Hero();
        var term = new TerminalEmulator(new LineStream(new[] { "90", "Y" }), new MemoryStream());
        var result = await HagglingEngine.Haggle(p, HagglingEngine.ShopType.Weapon, 100, "Keeper", term);
        result.Price.Should().Be(90);
        p.WeapHag.Should().Be(2); p.ArmHag.Should().Be(3); p.MagicHag.Should().Be(3);
    }

    // ---------- width ----------

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void PromptRows_FitIn79Columns(string lang)
    {
        // The dearest shop accessory at double price (alignment, event and difficulty markups) with 25+25 percent tax.
        long dearest = EquipmentDatabase.GetShopRings().Concat(EquipmentDatabase.GetShopNecklaces()).Max(e => e.Value);
        string total = $"{dearest * 2 * 3 / 2:N0}";
        string tax = $"{dearest:N0}";
        foreach (var row in new[]
        {
            "  " + Loc.GetIn(lang, "shop.buy_prompt_haggle", total, 3),
            "  " + Loc.GetIn(lang, "shop.buy_prompt_no_haggle", total),
            "  " + Loc.GetIn(lang, "shop.haggle_price_agreed", total, tax),
        })
            row.Length.Should().BeLessThanOrEqualTo(79, row);
    }
}
