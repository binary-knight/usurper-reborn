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
/// 1.3.1: the Magic Shop could lose or mishandle a worn item when it wrote an enchanted or stripped item back.
/// The changed item now ends in the slot it came from, still worn, whatever the equip check says of it now:
/// nothing else worn moves, nothing reaches the pack, the gold is charged as before, and the stats equal a
/// fresh recalculation. Every test counts the worn slots and the pack before and after.
/// State lives in memory on Character instances; the language and the king are put back in Dispose and the
/// real save folder is neither read nor written.
/// </summary>
[Collection("SharedGameSingletons")]
public class EnchantKeepItem131Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int StatTier = 1, DefenceStat = 4;

    private readonly string _prevLanguage = GameConfig.Language;
    private readonly King _prevKing = CastleLocation.GetCurrentKing();

    public EnchantKeepItem131Tests()
    {
        GameConfig.Language = "en";
        CastleLocation.SetKing(null!);   // no sales tax leaves the test
    }

    public void Dispose()
    {
        GameConfig.Language = _prevLanguage;
        CastleLocation.SetKing(_prevKing);
    }

    // ---------- helpers ----------

    private static readonly EquipmentSlot[] ShopOrder =
    {
        EquipmentSlot.MainHand, EquipmentSlot.OffHand, EquipmentSlot.Head, EquipmentSlot.Body,
        EquipmentSlot.Arms, EquipmentSlot.Hands, EquipmentSlot.Legs, EquipmentSlot.Feet,
        EquipmentSlot.Waist, EquipmentSlot.Face, EquipmentSlot.Cloak,
        EquipmentSlot.Neck, EquipmentSlot.LFinger, EquipmentSlot.RFinger,
    };

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

    /// <summary>The shop's own enchant flow on the item worn in that slot (a Defence enchant of the first tier).</summary>
    private static Task<string> Enchant(Character hero, EquipmentSlot slot)
    {
        int row = ShopOrder.Where(s => hero.GetEquipment(s) != null).ToList().IndexOf(slot) + 1;
        row.Should().BeGreaterThan(0);
        return Run(hero, "EnchantEquipment", $"{row}", $"{StatTier}", $"{DefenceStat}", "Y");
    }

    /// <summary>The shop's own removal flow on the item worn in that slot.</summary>
    private static Task<string> Strip(Character hero, EquipmentSlot slot)
    {
        int row = ShopOrder.Where(s => (hero.GetEquipment(s)?.GetEnchantmentCount() ?? 0) > 0).ToList().IndexOf(slot) + 1;
        row.Should().BeGreaterThan(0);
        return Run(hero, "RemoveEnchantment", $"{row}", "Y");
    }

    private static long RemovalPrice(Character hero) =>
        (long)typeof(MagicShopLocation).GetMethod("ApplyAllPriceModifiers", F)!
            .Invoke(new MagicShopLocation(), new object[] { 5000L + hero.Level * 200L, hero })!;

    private static Character Hero()
    {
        var hero = new Character
        {
            Name1 = "tester", Name2 = "Tester", Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 100,
            HP = 5000, MaxHP = 5000, BaseMaxHP = 5000, BaseStrength = 100, Strength = 100,
            AI = CharacterAI.Human, Gold = 9_000_000_000, AutoEquipDisabled = true,
        };
        // Already held, so no achievement gold lands in the middle of a price check.
        hero.Achievements.Unlock("first_enchant");
        return hero;
    }

    private static Equipment Wear(Character hero, EquipmentSlot slot, Equipment item)
    {
        hero.EquippedItems[slot] = EquipmentDatabase.RegisterDynamic(item);
        hero.RecalculateStats();
        return item;
    }

    private static (int worn, int pack) Counts(Character hero) =>
        (hero.EquippedItems.Count(k => k.Value > 0), hero.Inventory.Count);

    private static string Stats(Character h) =>
        $"{h.Strength}|{h.Dexterity}|{h.Constitution}|{h.Intelligence}|{h.Wisdom}|{h.Charisma}|{h.MaxHP}|{h.MaxMana}|"
        + $"{h.Defence}|{h.Stamina}|{h.Agility}|{h.WeapPow}|{h.ArmPow}|{h.HP}|{h.Mana}";

    private static string WornIds(Character h) =>
        string.Join(",", h.EquippedItems.Where(k => k.Value > 0).OrderBy(k => k.Key).Select(k => $"{k.Key}:{k.Value}"));

    // One case per equip check that a worn item can stop passing. Build: the item and the slot it is worn in.
    // Turn: the change after which the check refuses the item ("none" never refuses).
    private sealed record Case(EquipmentSlot Slot, Func<Equipment> Build, Action<Character> Start, Action<Character> Turn);

    private static Equipment Cap(Action<Equipment>? with = null)
    {
        var e = new Equipment { Name = "Plain Cap", Slot = EquipmentSlot.Head, ArmorClass = 3, MinLevel = 1, Value = 100 };
        with?.Invoke(e);
        return e;
    }

    private static readonly Dictionary<string, Case> Cases = new()
    {
        ["none"] = new(EquipmentSlot.Head, () => Cap(), _ => { }, _ => { }),
        ["level"] = new(EquipmentSlot.Head, () => Cap(e => e.MinLevel = 50), _ => { }, h => h.Level = 40),
        ["strength"] = new(EquipmentSlot.Head, () => Cap(e => e.StrengthRequired = 90), _ => { },
            h => { h.BaseStrength = 50; h.RecalculateStats(); }),
        ["strength_own_bonus"] = new(EquipmentSlot.Head,
            () => Cap(e => { e.StrengthRequired = 110; e.StrengthBonus = 20; }), _ => { }, _ => { }),
        ["heavy_strength"] = new(EquipmentSlot.Body,
            () => new Equipment { Name = "Plain Plate", Slot = EquipmentSlot.Body, ArmorClass = 9, MinLevel = 50, Value = 100, WeightClass = ArmorWeightClass.Heavy },
            _ => { }, h => { h.BaseStrength = 10; h.RecalculateStats(); }),
        ["good"] = new(EquipmentSlot.Head, () => Cap(e => e.RequiresGood = true),
            h => { h.Chivalry = 500; h.Darkness = 0; }, h => { h.Chivalry = 0; h.Darkness = 500; }),
        ["evil"] = new(EquipmentSlot.Head, () => Cap(e => e.RequiresEvil = true),
            h => { h.Chivalry = 0; h.Darkness = 500; }, h => { h.Chivalry = 500; h.Darkness = 0; }),
        ["class_list"] = new(EquipmentSlot.Head,
            () => Cap(e => e.ClassRestrictions = new List<CharacterClass> { CharacterClass.Magician }),
            h => h.Class = CharacterClass.Magician, h => h.Class = CharacterClass.Warrior),
    };

    public static IEnumerable<object[]> Refusals() => Cases.Keys.Where(k => k != "none").Select(k => new object[] { k });
    public static IEnumerable<object[]> EveryCase() => Cases.Keys.Select(k => new object[] { k });

    private static (Character hero, Equipment item, Case c) Dressed(string name)
    {
        var c = Cases[name];
        var hero = Hero();
        c.Start(hero);
        var item = Wear(hero, c.Slot, c.Build());
        item.CanEquip(hero, out _).Should().BeTrue("the item starts as one the wearer may put on");
        return (hero, item, c);
    }

    /// <summary>The equip check refuses the item once it is off (the bonus case passes only while it is worn).</summary>
    private static void ShouldBeRefused(Character hero, Equipment item)
    {
        bool passes = item.CanEquip(hero, out _) && hero.Strength - item.StrengthBonus >= item.StrengthRequired;
        passes.Should().BeFalse("the case must be one the equip check refuses");
    }

    // ---------- 1. enchanting an item the equip check now refuses ----------

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task Enchant_KeepsAWornItemTheEquipCheckNowRefuses(string name)
    {
        var (hero, item, c) = Dressed(name);
        c.Turn(hero);
        ShouldBeRefused(hero, item);
        var before = Counts(hero);
        long gold = hero.Gold;

        await Enchant(hero, c.Slot);

        Counts(hero).Should().Be(before, "nothing leaves a slot and nothing reaches the pack");
        var worn = hero.GetEquipment(c.Slot);
        worn.Should().NotBeNull("the enchanted item is still worn in its slot");
        worn!.GetEnchantmentCount().Should().Be(1);
        worn.GetEnchantedKinds().Should().Contain("def");
        worn.DefenceBonus.Should().BeGreaterThan(item.DefenceBonus, "the enchant is applied");
        long price = worn.Value - item.Value;
        price.Should().BeGreaterThan(0);
        (gold - hero.Gold).Should().Be(price, "the gold falls by the price");
    }

    // ---------- 2. stripping an item the equip check now refuses ----------

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task Removal_KeepsAWornItemTheEquipCheckNowRefuses(string name)
    {
        var (hero, item, c) = Dressed(name);
        await Enchant(hero, c.Slot);
        (hero.GetEquipment(c.Slot)?.GetEnchantmentCount() ?? 0).Should().Be(1, "the setup enchant lands and the item stays worn");
        c.Turn(hero);
        ShouldBeRefused(hero, hero.GetEquipment(c.Slot)!);
        var before = Counts(hero);
        long gold = hero.Gold, price = RemovalPrice(hero);

        await Strip(hero, c.Slot);

        Counts(hero).Should().Be(before, "nothing leaves a slot and nothing reaches the pack");
        var worn = hero.GetEquipment(c.Slot);
        worn.Should().NotBeNull("the stripped item is still worn in its slot");
        worn!.GetEnchantmentCount().Should().Be(0);
        worn.Name.Should().Be(item.Name);
        worn.DefenceBonus.Should().Be(item.DefenceBonus, "the enchant is gone");
        (gold - hero.Gold).Should().Be(price, "the gold falls by the price");
    }

    // ---------- 3. an item that passes: as before; nothing else worn moves ----------

    [Fact]
    public async Task APassingItem_EndsAsBefore_SlotStatsAndGold()
    {
        var (hero, item, c) = Dressed("none");
        var before = Counts(hero);
        long gold = hero.Gold, defence = hero.Defence, armPow = hero.ArmPow;

        await Enchant(hero, c.Slot);
        var worn = hero.GetEquipment(c.Slot)!;
        int bonus = worn.DefenceBonus - item.DefenceBonus;
        bonus.Should().BeGreaterThan(0);
        Counts(hero).Should().Be(before);
        worn.Name.Should().StartWith(item.Name);
        hero.Defence.Should().Be(defence + bonus, "the wearer gains the enchant once");
        hero.ArmPow.Should().Be(armPow);
        (gold - hero.Gold).Should().Be(worn.Value - item.Value);

        gold = hero.Gold;
        long price = RemovalPrice(hero);
        await Strip(hero, c.Slot);
        Counts(hero).Should().Be(before);
        hero.GetEquipment(c.Slot)!.Name.Should().Be(item.Name);
        hero.Defence.Should().Be(defence, "the wearer is back where it started");
        hero.ArmPow.Should().Be(armPow);
        (gold - hero.Gold).Should().Be(price);
    }

    private static Equipment Weapon(string name, WeaponHandedness hands, WeaponType type) => new()
    {
        Name = name, Slot = EquipmentSlot.MainHand, Handedness = hands, WeaponType = type, WeaponPower = 10, MinLevel = 1, Value = 100,
    };

    private static Equipment Buckler() => new()
    {
        Name = "Plain Buckler", Slot = EquipmentSlot.OffHand, Handedness = WeaponHandedness.OffHandOnly,
        WeaponType = WeaponType.Buckler, ShieldBonus = 2, MinLevel = 1, Value = 100,
    };

    [Theory]
    [InlineData(EquipmentSlot.MainHand, EquipmentSlot.OffHand)]
    [InlineData(EquipmentSlot.OffHand, EquipmentSlot.MainHand)]
    public async Task TheOtherHand_IsUntouchedByTheWriteBack(EquipmentSlot changed, EquipmentSlot other)
    {
        var hero = Hero();
        Wear(hero, EquipmentSlot.MainHand, Weapon("Plain Greatsword", WeaponHandedness.TwoHanded, WeaponType.Greatsword));
        Wear(hero, EquipmentSlot.OffHand, Buckler());
        var before = Counts(hero);
        int otherId = hero.EquippedItems[other];

        await Enchant(hero, changed);
        Counts(hero).Should().Be(before, "nothing leaves a slot and nothing reaches the pack");
        hero.EquippedItems.GetValueOrDefault(other).Should().Be(otherId, "the other hand keeps its item");
        (hero.GetEquipment(changed)?.GetEnchantmentCount() ?? 0).Should().Be(1);
        Stats(hero).Should().Be(Fresh(hero));

        await Strip(hero, changed);
        Counts(hero).Should().Be(before);
        hero.EquippedItems.GetValueOrDefault(other).Should().Be(otherId, "the other hand keeps its item");
        (hero.GetEquipment(changed)?.GetEnchantmentCount() ?? 9).Should().Be(0);
        Stats(hero).Should().Be(Fresh(hero));
    }

    // ---------- 4. stats equal a fresh recalculation ----------

    private static string Fresh(Character hero)
    {
        string ids = WornIds(hero);
        hero.RecalculateStats();
        WornIds(hero).Should().Be(ids, "a recalculation finds nothing to repair");
        return Stats(hero);
    }

    [Theory]
    [MemberData(nameof(EveryCase))]
    public async Task StatsAfterTheWriteBack_EqualAFreshRecalculation(string name)
    {
        var (hero, _, c) = Dressed(name);
        var before = Counts(hero);
        await Enchant(hero, c.Slot);
        c.Turn(hero);
        await Strip(hero, c.Slot);
        Counts(hero).Should().Be(before);
        hero.GetEquipment(c.Slot).Should().NotBeNull();
        Stats(hero).Should().Be(Fresh(hero), "nothing is counted twice or dropped by the removal");

        await Enchant(hero, c.Slot);
        Counts(hero).Should().Be(before);
        hero.GetEquipment(c.Slot).Should().NotBeNull();
        Stats(hero).Should().Be(Fresh(hero), "nothing is counted twice or dropped by the enchant");
    }

    // ---------- 5. the screens are the same rows (no row is added or changed, so no width to hold) ----------

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public async Task TheScreens_AreTheSameRowsForARefusedItem(string lang)
    {
        GameConfig.Language = lang;
        var texts = new List<string>();
        foreach (var name in new[] { "none", "strength" })
        {
            var (hero, _, c) = Dressed(name);
            var before = Counts(hero);
            string enchantSetup = await Enchant(hero, c.Slot);
            c.Turn(hero);
            string strip = await Strip(hero, c.Slot);
            string enchant = await Enchant(hero, c.Slot);
            Counts(hero).Should().Be(before);
            hero.GetEquipment(c.Slot).Should().NotBeNull();
            _ = enchantSetup;
            texts.Add(strip + "\n=====\n" + enchant);
        }
        texts[1].Should().Be(texts[0], "a refused item shows the rows a passing item shows");
        texts[1].Should().Contain("Plain Cap", "the screens were drawn");
    }

    // ---------- 6. the written item is the same kind of item in the same slot ----------

    public static IEnumerable<object[]> Kinds() => new[]
    {
        new object[] { EquipmentSlot.MainHand, WeaponHandedness.OneHanded, WeaponType.Sword, EquipmentSlot.MainHand },
        new object[] { EquipmentSlot.MainHand, WeaponHandedness.TwoHanded, WeaponType.Greatsword, EquipmentSlot.MainHand },
        new object[] { EquipmentSlot.OffHand, WeaponHandedness.OneHanded, WeaponType.Dagger, EquipmentSlot.MainHand },
        new object[] { EquipmentSlot.OffHand, WeaponHandedness.OffHandOnly, WeaponType.Buckler, EquipmentSlot.OffHand },
        new object[] { EquipmentSlot.RFinger, WeaponHandedness.None, WeaponType.None, EquipmentSlot.LFinger },
        new object[] { EquipmentSlot.Head, WeaponHandedness.None, WeaponType.None, EquipmentSlot.Head },
    };

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task TheWrittenItem_HasTheSlotKindAndHandednessOfTheOneItReplaces(
        EquipmentSlot wornIn, WeaponHandedness hands, WeaponType type, EquipmentSlot itemSlot)
    {
        var hero = Hero();
        var item = Wear(hero, wornIn, new Equipment
        {
            Name = "Plain Thing", Slot = itemSlot, Handedness = hands, WeaponType = type, WeaponPower = 4, MinLevel = 1, Value = 100,
        });
        var before = Counts(hero);

        await Enchant(hero, wornIn);
        Same(hero, wornIn, item, 1);
        Counts(hero).Should().Be(before);

        await Strip(hero, wornIn);
        Same(hero, wornIn, item, 0);
        Counts(hero).Should().Be(before);
    }

    private static void Same(Character hero, EquipmentSlot wornIn, Equipment was, int enchants)
    {
        var now = hero.GetEquipment(wornIn);
        now.Should().NotBeNull("the item is worn where it was");
        now!.GetEnchantmentCount().Should().Be(enchants);
        (now.Slot, now.Handedness, now.WeaponType, now.ArmorType, now.WeightClass)
            .Should().Be((was.Slot, was.Handedness, was.WeaponType, was.ArmorType, was.WeightClass));
    }

    // ---------- 7. a cursed item: written back once, still cursed ----------

    public static IEnumerable<object[]> CursedShapes() => new[]
    {
        new object[] { EquipmentSlot.Head, false },
        new object[] { EquipmentSlot.MainHand, false },
        new object[] { EquipmentSlot.MainHand, true },
        new object[] { EquipmentSlot.OffHand, true },
    };

    [Theory]
    [MemberData(nameof(CursedShapes))]
    public async Task Removal_WritesACursedItemBackOnce_StillCursed(EquipmentSlot slot, bool bothHands)
    {
        var hero = Hero();
        if (slot == EquipmentSlot.Head) Wear(hero, slot, Cap());
        else Wear(hero, slot, Weapon("Plain Sword", WeaponHandedness.OneHanded, WeaponType.Sword));
        if (bothHands)
            Wear(hero, slot == EquipmentSlot.MainHand ? EquipmentSlot.OffHand : EquipmentSlot.MainHand,
                Weapon("Plain Dagger", WeaponHandedness.OneHanded, WeaponType.Dagger));
        await Enchant(hero, slot);
        hero.GetEquipment(slot)!.IsCursed = true;
        hero.RecalculateStats();
        var before = Counts(hero);
        string others = string.Join(",", WornIds(hero).Split(',').Where(p => !p.StartsWith($"{slot}:")));
        long gold = hero.Gold, price = RemovalPrice(hero);

        await Strip(hero, slot);

        Counts(hero).Should().Be(before, "exactly one item stands where one stood, none added to the pack");
        string.Join(",", WornIds(hero).Split(',').Where(p => !p.StartsWith($"{slot}:"))).Should().Be(others);
        var worn = hero.GetEquipment(slot)!;
        worn.GetEnchantmentCount().Should().Be(0, "the item in the slot is the stripped one");
        worn.IsCursed.Should().BeTrue("removing an enchant does not lift a curse");
        (gold - hero.Gold).Should().Be(price);
        Stats(hero).Should().Be(Fresh(hero));
    }

    [Fact]
    public async Task Enchant_LeavesACursedItemAsItIs_StillCursed()
    {
        var hero = Hero();
        var item = Wear(hero, EquipmentSlot.Head, Cap(e => e.IsCursed = true));
        var before = Counts(hero);
        string ids = WornIds(hero), stats = Stats(hero);
        long gold = hero.Gold;

        await Enchant(hero, EquipmentSlot.Head);

        Counts(hero).Should().Be(before);
        WornIds(hero).Should().Be(ids, "the same item is worn");
        item.IsCursed.Should().BeTrue();
        item.GetEnchantmentCount().Should().Be(0);
        hero.Gold.Should().Be(gold, "nothing is charged");
        Stats(hero).Should().Be(stats);
    }
}
