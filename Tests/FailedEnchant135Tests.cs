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
using UsurperRemake.UI;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.5: a failed 4th or 5th enchant destroys one existing enchant picked at random, as enchant_failure_warning and
/// enchant_risk_4 say: its stats, flag, name tag and kind go and nothing else moves, so the freed kind can be bought
/// again once and the count never passes the cap. Enchant removal lists enchanted backpack items too, labels each row
/// as the enchant list does and names the restored item as it is shown.
/// State lives in memory on Character instances; the language, the king and the shop's random source are put back in
/// Dispose and the real save folder is neither read nor written.
/// </summary>
[Collection("SharedGameSingletons")]
public class FailedEnchant135Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private readonly string _prevLanguage = GameConfig.Language;
    private readonly King _prevKing = CastleLocation.GetCurrentKing();
    private readonly Random _prevRandom = MagicShopLocation.EnchantRandom;

    public FailedEnchant135Tests()
    {
        GameConfig.Language = "en";
        CastleLocation.SetKing(null!);   // no sales tax leaves the test
    }

    public void Dispose()
    {
        GameConfig.Language = _prevLanguage;
        CastleLocation.SetKing(_prevKing);
        MagicShopLocation.EnchantRandom = _prevRandom;
    }

    // ---------- helpers ----------

    /// <summary>A random source with a fixed roll and a fixed pick (the pick is held inside the range asked for).</summary>
    private sealed class FixedRandom : Random
    {
        private readonly double _roll;
        private readonly int _pick;
        public FixedRandom(double roll, int pick = 0) { _roll = roll; _pick = pick; }
        public override double NextDouble() => _roll;
        public override int Next(int maxValue) => Math.Min(_pick, Math.Max(0, maxValue - 1));
        public override int Next(int minValue, int maxValue) => Math.Clamp(minValue + _pick, minValue, Math.Max(minValue, maxValue - 1));
    }

    private static readonly Random Succeeds = new FixedRandom(0.99);

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

    /// <summary>Every one of the six stats the old failure took 5 from is set, so a stray loss shows.</summary>
    private static Equipment Blade() => new()
    {
        Name = "Plain Blade", Slot = EquipmentSlot.MainHand, WeaponPower = 10, StrengthBonus = 7, DexterityBonus = 7,
        DefenceBonus = 7, WisdomBonus = 7, ArmorClass = 7, ConstitutionBonus = 4, MaxHPBonus = 9, MinLevel = 1, Value = 100,
    };

    private static Equipment Wear(Character hero, Equipment item)
    {
        hero.EquippedItems[item.Slot] = EquipmentDatabase.RegisterDynamic(item);
        hero.RecalculateStats();
        return item;
    }

    private static Equipment Worn(Character hero) => hero.GetEquipment(EquipmentSlot.MainHand)!;

    // The four enchants put on: (kind, tier, stat, what it adds to the record, its name tag).
    private sealed record Ench(string Kind, int Tier, int Stat, Action<Equipment> Adds, string Tag);

    private static readonly Ench[] Four =
    {
        new("str", 1, 2, e => e.StrengthBonus += 2, " +2 Str"),
        new("bless", 5, 0, e => { e.StrengthBonus += 3; e.DexterityBonus += 3; e.DefenceBonus += 3; e.WisdomBonus += 3; e.WeaponPower += 3; e.ArmorClass += 3; }, " (Blessed)"),
        new("ward", 7, 0, e => { e.MagicResistance += 20; e.DefenceBonus += 2; }, " (Warded)"),
        new("life", 9, 0, e => e.LifeSteal += 3, " (Lifedrinker)"),
    };

    private static Task<string> Enchant(Character hero, int row, int tier, int stat) =>
        stat > 0 ? Run(hero, "EnchantEquipment", $"{row}", $"{tier}", $"{stat}", "Y")
                 : Run(hero, "EnchantEquipment", $"{row}", $"{tier}", "Y");

    private static async Task<Character> HeroWithFour()
    {
        var hero = Hero();
        Wear(hero, Blade());
        MagicShopLocation.EnchantRandom = Succeeds;
        foreach (var e in Four) await Enchant(hero, 1, e.Tier, e.Stat);
        Worn(hero).GetEnchantmentCount().Should().Be(4);
        Worn(hero).GetEnchantedKinds().Should().Equal(Four.Select(e => e.Kind));
        return hero;
    }

    /// <summary>Every field a Magic Shop enchant can touch, plus the fields none touches, as one string.</summary>
    private static string Fields(Equipment e) =>
        $"pow {e.WeaponPower} ac {e.ArmorClass} str {e.StrengthBonus} dex {e.DexterityBonus} con {e.ConstitutionBonus} "
        + $"int {e.IntelligenceBonus} wis {e.WisdomBonus} cha {e.CharismaBonus} def {e.DefenceBonus} sta {e.StaminaBonus} "
        + $"agi {e.AgilityBonus} crit {e.CriticalChanceBonus} critdmg {e.CriticalDamageBonus} mr {e.MagicResistance} "
        + $"ls {e.LifeSteal} fire {e.HasFireEnchant} frost {e.HasFrostEnchant} hp {e.MaxHPBonus} mana {e.MaxManaBonus}";

    // ---------- X4: the failed enchant ----------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task FailedEnchant_OnFour_DestroysThePickedOneWhole_AndMovesNothingElse(int pick)
    {
        var hero = await HeroWithFour();
        var before = Worn(hero);
        long goldBefore = hero.Gold;

        MagicShopLocation.EnchantRandom = new FixedRandom(0.0, pick);
        string text = await Enchant(hero, 1, 8, 0);   // Predator, the 5th, fails
        text.Should().Contain(Loc.Get("magic_shop.fail_destroyed"));
        hero.Gold.Should().BeLessThan(goldBefore, "the gold is spent on a failure as before");

        var after = Worn(hero);
        var gone = Four[pick];
        var kept = Four.Where((_, i) => i != pick).ToList();
        after.GetEnchantmentCount().Should().Be(3, "exactly 3 enchants remain");
        after.GetEnchantedKinds().Should().Equal(kept.Select(k => k.Kind), "the destroyed kind's marker is gone and the rest stay");

        // the expected item: the blade plus each kept enchant at its full effect, and nothing of the destroyed one
        var expected = Blade();
        foreach (var k in kept) k.Adds(expected);
        Fields(after).Should().Be(Fields(expected), "no stat outside the destroyed enchant moved and each kept one keeps its full effect");
        after.Value.Should().Be(before.Value, "the value keeps what was paid");
        after.Name.Should().NotContain(gone.Tag, "the destroyed enchant's tag is gone");
        foreach (var k in kept) after.Name.Should().Contain(k.Tag);
        after.EnchantBase.Should().Be(before.EnchantBase, "removal still returns the item to the same base form");
    }

    [Fact]
    public async Task FreedKind_CanBeBoughtAgainOnce_AndTheCountNeverPassesFive()
    {
        foreach (int pick in Enumerable.Range(0, Four.Length))
        {
            var hero = await HeroWithFour();
            MagicShopLocation.EnchantRandom = new FixedRandom(0.0, pick);
            await Enchant(hero, 1, 8, 0);
            var gone = Four[pick];

            MagicShopLocation.EnchantRandom = Succeeds;
            await Enchant(hero, 1, gone.Tier, gone.Stat);
            Worn(hero).GetEnchantmentCount().Should().Be(4, $"the freed {gone.Kind} is bought again");
            Worn(hero).GetEnchantedKinds().Should().Contain(gone.Kind);

            string twice = await Enchant(hero, 1, gone.Tier, gone.Stat);
            twice.Should().Contain("already carries that enchantment", $"{gone.Kind} cannot be stacked a second time");
            Worn(hero).GetEnchantmentCount().Should().Be(4);

            var expected = Blade();
            foreach (var k in Four) k.Adds(expected);
            Fields(Worn(hero)).Should().Be(Fields(expected), $"{gone.Kind} counts once after the rebuy");

            await Enchant(hero, 1, 8, 0);   // Predator, the 5th, succeeds
            Worn(hero).GetEnchantmentCount().Should().Be(GameConfig.MaxEnchantments);
            await Enchant(hero, 1, 2, 3);   // a 6th is refused
            Worn(hero).GetEnchantmentCount().Should().Be(GameConfig.MaxEnchantments, "the count never passes 5");
            Worn(hero).GetEnchantedKinds().Should().HaveCount(GameConfig.MaxEnchantments);
        }
    }

    [Fact]
    public async Task SeededRuns_PickDifferentEnchants()
    {
        var hero = await HeroWithFour();
        var item = Worn(hero);
        var picked = Enumerable.Range(0, 60).Select(seed => MagicShopLocation.DestroyOneEnchant(item, new Random(seed)).Kind).ToList();
        picked.Distinct().Should().HaveCount(Four.Length, "a seeded run picks different enchants across seeds (it is random, not the last)");
    }

    // ---------- G4: an item with no recorded base form ----------

    /// <summary>An item enchanted before 1.2.5 that matches no template: no EnchantBase, four counted enchants.</summary>
    private static Equipment OldItem(string kinds, int count, string name)
    {
        var e = Blade();
        foreach (var k in Four) k.Adds(e);
        e.Name = name;
        e.Description = $"[E:{count}] [ES:{kinds}]";
        e.EnchantBase.Should().BeEmpty();
        return e;
    }

    [Fact]
    public void NoBase_NamedKind_LosesItsOwnAmounts_AndAStatKindTheAmountInItsTag()
    {
        var old = OldItem("str,bless,ward,life", 4, "Plain Blade +2 Str (Blessed) (Warded) (Lifedrinker)");
        MagicShopLocation.EnchantBaseOf(old).Should().BeNull("this item's base form is not known");
        for (int pick = 0; pick < Four.Length; pick++)
        {
            var (after, kind) = MagicShopLocation.DestroyOneEnchant(old, new FixedRandom(0.0, pick));
            kind.Should().Be(Four[pick].Kind);
            after.GetEnchantmentCount().Should().Be(3);
            after.GetEnchantedKinds().Should().Equal(Four.Where((_, i) => i != pick).Select(k => k.Kind));
            var expected = Blade();
            foreach (var k in Four.Where((_, i) => i != pick)) k.Adds(expected);
            Fields(after).Should().Be(Fields(expected), $"{kind} is taken off by its own amounts");
            after.Name.Should().NotContain(Four[pick].Tag);
        }
    }

    [Fact]
    public void NoBase_StatKindWithoutTag_LosesTheLargestStatTierItsStatHolds_NeverBelowZero()
    {
        var old = OldItem("str,bless,ward,life", 4, "Plain Blade");
        var (after, kind) = MagicShopLocation.DestroyOneEnchant(old, new FixedRandom(0.0, 0));
        kind.Should().Be("str");
        // Strength 7 + 2 + 3 = 12: the largest stat tier bonus not above 12 is 8 (Superior), so 4 is left.
        after.StrengthBonus.Should().Be(4);
        after.GetEnchantmentCount().Should().Be(3);
        after.GetEnchantedKinds().Should().Equal("bless", "ward", "life");

        var small = OldItem("str,bless,ward,life", 4, "Plain Blade");
        small.StrengthBonus = 1;
        MagicShopLocation.DestroyOneEnchant(small, new FixedRandom(0.0, 0)).Item.StrengthBonus.Should().Be(0, "never below zero");
    }

    [Fact]
    public void NoBase_AnEnchantTheKindListNeverRecorded_LowersTheCountOnly()
    {
        var old = OldItem("str,bless,ward", 4, "Plain Blade +2 Str (Blessed) (Warded) (Lifedrinker)");
        var (after, kind) = MagicShopLocation.DestroyOneEnchant(old, new FixedRandom(0.0, 3));
        kind.Should().BeEmpty();
        after.GetEnchantmentCount().Should().Be(3);
        after.GetEnchantedKinds().Should().Equal("str", "bless", "ward");
        Fields(after).Should().Be(Fields(old), "an effect never recorded cannot be taken off");
    }

    [Fact]
    public void EveryPath_LowersTheCountByOne_SoTheCountNeverPassesFive()
    {
        var items = new[]
        {
            OldItem("str,bless,ward,life", 4, "Plain Blade"),
            OldItem("str,bless,ward,life", 5, "Plain Blade"),
            OldItem("str,bless", 5, "Plain Blade"),
            OldItem("str,bless,ward,life", 3, "Plain Blade"),
        };
        foreach (var item in items)
            for (int seed = 0; seed < 20; seed++)
            {
                var (after, _) = MagicShopLocation.DestroyOneEnchant(item, new Random(seed));
                after.GetEnchantmentCount().Should().Be(item.GetEnchantmentCount() - 1);
                after.GetEnchantmentCount().Should().BeLessOrEqualTo(GameConfig.MaxEnchantments);
            }
    }

    // ---------- R5: removal of a backpack item ----------

    private static long RemovalPrice(Character hero) =>
        (long)typeof(MagicShopLocation).GetMethod("ApplyAllPriceModifiers", F)!
            .Invoke(new MagicShopLocation(), new object[] { 5000L + hero.Level * 200L, hero })!;

    [Fact]
    public async Task Removal_ListsABackpackItem_AndRestoresItFromItsBase()
    {
        var hero = Hero();
        var blade = Blade();
        blade.Name = "Bag Blade";
        hero.Inventory.Add(hero.ConvertEquipmentToLegacyItem(blade));
        string plain = Fields(Character.BuildEquipmentFromItem(hero.Inventory[0], EquipmentSlot.MainHand, WeaponHandedness.None, WeaponType.None));

        MagicShopLocation.EnchantRandom = Succeeds;
        await Enchant(hero, 1, 1, 4);   // +2 Defence on the bag item
        await Enchant(hero, 1, 5, 0);   // Divine Blessing
        var enchanted = Character.BuildEquipmentFromItem(hero.Inventory[0], EquipmentSlot.MainHand, WeaponHandedness.None, WeaponType.None);
        enchanted.GetEnchantmentCount().Should().Be(2);
        enchanted.EnchantBase.Should().NotBeEmpty();

        long gold = hero.Gold;
        int worn = hero.EquippedItems.Count(k => k.Value > 0);
        string text = await Run(hero, "RemoveEnchantment", "1", "Y");
        text.Should().Contain($"(1) {Loc.Get("magic_shop.slot_bag")}: Bag Blade", "the bag item is listed for removal");
        text.Should().Contain(Loc.Get("magic_shop.remove_restored", "Bag Blade"));

        hero.Inventory.Should().HaveCount(1, "the stripped item goes back to its place in the backpack");
        var stripped = Character.BuildEquipmentFromItem(hero.Inventory[0], EquipmentSlot.MainHand, WeaponHandedness.None, WeaponType.None);
        stripped.GetEnchantmentCount().Should().Be(0);
        stripped.GetEnchantedKinds().Should().BeEmpty();
        stripped.EnchantBase.Should().BeEmpty();
        stripped.Name.Should().Be("Bag Blade");
        Fields(stripped).Should().Be(plain, "the item is back at its base form");
        hero.Gold.Should().Be(gold - RemovalPrice(hero));
        hero.EquippedItems.Count(k => k.Value > 0).Should().Be(worn, "nothing is equipped");
    }

    [Fact]
    public async Task Removal_LabelsAWornRow_AsTheEnchantListDoes()
    {
        var hero = Hero();
        Wear(hero, Blade());
        MagicShopLocation.EnchantRandom = Succeeds;
        await Enchant(hero, 1, 1, 4);
        string text = await Run(hero, "RemoveEnchantment", "0");
        text.Should().Contain($"(1) {MagicShopLocation.EnchantSlotLabel(EquipmentSlot.MainHand)}: Plain Blade");
        text.Should().NotContain("MainHand:", "the raw slot name is not shown");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void RemovalRow_FitsSeventyNineColumns(string lang)
    {
        GameConfig.Language = lang;
        var item = Blade();
        item.Name = new string('W', 40) + " +38 Str";
        item.Description = "[E:5] [ES:str]";
        item.IsCursed = true;
        var labels = Enum.GetValues<EquipmentSlot>().Where(s => s != EquipmentSlot.None)
            .Select(MagicShopLocation.EnchantSlotLabel).Append(Loc.Get("magic_shop.slot_bag"));
        foreach (var label in labels)
        {
            string row = MagicShopLocation.RemovalRow(99, label, item);
            var rows = MagicShopLocation.WrapRows(row);
            rows.Should().OnlyContain(r => UIHelper.VisibleLength(r) <= 79, $"{lang} {label}");
            Regex.Replace(string.Join(" ", rows), @"\s+", " ").Trim().Should().Be(Regex.Replace(row, @"\s+", " ").Trim());
        }
    }
}
