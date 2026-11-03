using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Data;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.10 audit, piece FS (settlements), his picks P6A and P12A:
/// P12. Outpost and street merchant healing and mana potions cost the town price for the buyer's level x1.25
///      (they cost 25 or 30 x (1 + floor / 10), or 40 + 3 x level, a few percent of the town price late on).
/// All in memory.
/// </summary>
[Collection("SharedGameSingletons")]
public class Settlement1310Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private readonly bool _pacingWas = Pacing.Disabled;
    private readonly bool _srWas = GameConfig.ScreenReaderMode;

    public Settlement1310Tests()
    {
        Pacing.Disabled = true;
        GameConfig.ScreenReaderMode = false;
    }

    public void Dispose()
    {
        Pacing.Disabled = _pacingWas;
        GameConfig.ScreenReaderMode = _srWas;
    }

    // ---------- helpers ----------

    private sealed class Screen
    {
        public TerminalEmulator Term = null!;
        public MemoryStream Output = new();
        public string Text
        {
            get
            {
                Term.StreamWriterInternal?.Flush();
                return Regex.Replace(Encoding.UTF8.GetString(Output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "");
            }
        }
    }

    private static Screen At(BaseLocation loc, Character hero, params string[] lines)
    {
        var s = new Screen();
        s.Term = new TerminalEmulator(new LineStream(lines.Concat(Enumerable.Repeat("", 30))), s.Output);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(loc, s.Term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(loc, hero);
        return s;
    }

    private static async Task Run(object target, string method, params object?[] args)
    {
        var m = target.GetType().GetMethod(method, F);
        m.Should().NotBeNull(method);
        try
        {
            var r = m!.Invoke(target, args);
            if (r is Task t)
            {
                (await Task.WhenAny(t, Task.Delay(TimeSpan.FromSeconds(60)))).Should().BeSameAs(t, "{0} must return", method);
                await t;
            }
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    private static T Call<T>(object target, string method, params object?[] args)
    {
        var m = target.GetType().GetMethod(method, F);
        m.Should().NotBeNull(method);
        return (T)m!.Invoke(target, args)!;
    }

    private static Character Hero(string name, int level = 30, CharacterClass cls = CharacterClass.Warrior) => new Character
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Human, Class = cls, Race = CharacterRace.Human, Level = level,
        HP = 100000, MaxHP = 100000, Gold = 100_000_000, Experience = 1000000,
        Agility = 1, Dexterity = 1, Constitution = 1, Wisdom = 1, Mental = 50, MentalHintShown = true,
    };

    private static (DungeonLocation d, Screen s) Dungeon(Character hero, int floor, params string[] lines)
    {
        var d = new DungeonLocation();
        var s = At(d, hero, lines);
        typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.SetValue(d, floor);
        return (d, s);
    }

    private static List<(string id, string name, long cost)> Goods(Character hero, int floor)
    {
        var (d, _) = Dungeon(hero, floor);
        return Call<List<(string id, string name, long cost)>>(d, "GetSettlementTradeItems", DungeonSettlementData.GetSettlement(floor));
    }

    private static long Cost(List<(string id, string name, long cost)> goods, string id) => goods.Single(g => g.id == id).cost;

    private static List<(string name, long price)> StreetWares(int level, bool shady)
    {
        var list = (IEnumerable)typeof(StreetEncounterSystem).GetMethod("GenerateMerchantItems", F)!
            .Invoke(StreetEncounterSystem.Instance, new object[] { level, shady })!;
        var result = new List<(string, long)>();
        foreach (var item in list)
        {
            var t = item.GetType();
            result.Add(((string)t.GetField("Name")!.GetValue(item)!, (long)t.GetField("Price")!.GetValue(item)!));
        }
        return result;
    }

    // ---------- P12: potion prices away from town ----------

    [Theory]
    [InlineData(65, 80)]
    [InlineData(10, 5)]
    [InlineData(35, 100)]
    public void Outpost_potions_cost_the_town_price_for_the_buyers_level_times_one_and_a_quarter(int floor, int level)
    {
        var goods = Goods(Hero("Buyer", level), floor);
        Cost(goods, "heal_potion").Should().Be((long)(GameConfig.GetHealingPotionCost(level) * 1.25));
        Cost(goods, "mana_potion").Should().Be((long)(GameConfig.GetManaPotionCost(level) * 1.25));
    }

    [Fact]
    public void A_level_80_buyer_at_the_last_hearth_pays_4125_for_a_healing_potion_not_175()
    {
        var goods = Goods(Hero("Late Buyer", 80), 65);
        Cost(goods, "heal_potion").Should().Be(4125);
        Cost(goods, "mana_potion").Should().Be(4187);
    }

    [Fact]
    public void Outpost_antidotes_herbs_and_other_goods_keep_their_floor_price()
    {
        var goods = Goods(Hero("Herb Buyer", 80), 65);
        Cost(goods, "antidote").Should().Be(20 * 7);
        Cost(goods, "firebloom").Should().Be(60 * 7);
        var forge = Goods(Hero("Forge Buyer", 80), 10);
        Cost(forge, "torch").Should().Be(15 * 2);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(40)]
    [InlineData(80)]
    public void Street_merchant_potions_cost_the_town_price_times_one_and_a_quarter_and_the_bundles_follow(int level)
    {
        long one = (long)(GameConfig.GetHealingPotionCost(level) * 1.25);
        var normal = StreetWares(level, false);
        normal.Single(w => w.name == "Healing Potion").price.Should().Be(one);
        normal.Single(w => w.name == "Healing Potions (x5)").price.Should().Be((long)(one * 4.5));
        normal.Single(w => w.name == "Antidote").price.Should().Be(50 + level * 2, "only potions move to the town curve");
        var shady = StreetWares(level, true);
        shady.Single(w => w.name == "Healing Potions (x3)").price.Should().Be(one * 3);
    }
}
