using System;
using System.Collections.Generic;
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
/// 1.3.5 (CT-1): the three god counter enchants at the Magic Shop, Sunbane (Aurelion), Stonebane (Terravok) and
/// Godbane (Manwe), tiers 15 to 17 after Frostbite. Each costs its gold and crafting materials, needs its level, goes
/// on a main or off hand item only, one counter to a weapon, and writes its kind code without changing a stat or the
/// item's name. The cap of 5 and the 4th and 5th enchant failure hold for a counter as for any enchant. The kind code
/// survives a save and goes with an enchant removal.
/// State lives in memory on Character instances; the language, the king and the shop's random source are put back in
/// Dispose and the real save folder is neither read nor written.
/// </summary>
[Collection("SharedGameSingletons")]
public class CounterTiers135Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int Sunbane = 15, Stonebane = 16, Godbane = 17;
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };

    private readonly string _prevLanguage = GameConfig.Language;
    private readonly King _prevKing = CastleLocation.GetCurrentKing();
    private readonly Random _prevRandom = MagicShopLocation.EnchantRandom;

    public CounterTiers135Tests()
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
    private static readonly Random AlwaysFails = new FixedRandom(0.0);

    private static async Task<string> Run(Character hero, string method, params string[] lines)
    {
        var shop = new MagicShopLocation();
        using var output = new MemoryStream();
        // a terminal shows the typed line and its Enter, so each read ends the prompt's row (TownMagic125Tests' pattern)
        TerminalEmulator term = null!;
        term = new TerminalEmulator(new LineStream(lines.Concat(Enumerable.Repeat("", 12)), _ =>
        {
            term.StreamWriterInternal?.Flush();
            output.WriteByte((byte)'\n');
        }), output);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(shop, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(shop, hero);
        var r = typeof(MagicShopLocation).GetMethod(method, F)!.Invoke(shop, new object[] { hero });
        if (r is Task t) await t;
        term.StreamWriterInternal?.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "");
    }

    /// <summary>The first listed item, the tier, a stat when the tier asks for one, then yes.</summary>
    private static Task<string> Enchant(Character hero, int tier, int stat = 0, string item = "1") =>
        stat > 0 ? Run(hero, "EnchantEquipment", item, $"{tier}", $"{stat}", "Y")
                 : Run(hero, "EnchantEquipment", item, $"{tier}", "Y");

    private static Character Hero(int level = 100)
    {
        var hero = new Character
        {
            Name1 = "tester", Name2 = "Tester", Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = level,
            HP = 5000, MaxHP = 5000, BaseMaxHP = 5000, BaseStrength = 100, Strength = 100,
            AI = CharacterAI.Human, Gold = 9_000_000_000, AutoEquipDisabled = true,
        };
        hero.Achievements.Unlock("first_enchant");
        hero.Achievements.Unlock("master_enchanter");
        return hero;
    }

    private static Equipment Blade(string name = "Plain Blade", EquipmentSlot slot = EquipmentSlot.MainHand) => new()
    {
        Name = name, Slot = slot, WeaponPower = 10, StrengthBonus = 7, DexterityBonus = 7,
        DefenceBonus = 7, WisdomBonus = 7, ArmorClass = 7, MinLevel = 1, Value = 100,
    };

    private static Equipment Wear(Character hero, Equipment item)
    {
        hero.EquippedItems[item.Slot] = EquipmentDatabase.RegisterDynamic(item);
        hero.RecalculateStats();
        return item;
    }

    private static Equipment Worn(Character hero, EquipmentSlot slot = EquipmentSlot.MainHand) => hero.GetEquipment(slot)!;

    /// <summary>Every stat an enchant can touch, the name, the count, the kinds and the value.</summary>
    private static string Fields(Equipment e) =>
        $"pow {e.WeaponPower} ac {e.ArmorClass} str {e.StrengthBonus} dex {e.DexterityBonus} con {e.ConstitutionBonus} "
        + $"int {e.IntelligenceBonus} wis {e.WisdomBonus} cha {e.CharismaBonus} def {e.DefenceBonus} sta {e.StaminaBonus} "
        + $"agi {e.AgilityBonus} crit {e.CriticalChanceBonus} critdmg {e.CriticalDamageBonus} mr {e.MagicResistance} "
        + $"ls {e.LifeSteal} fire {e.HasFireEnchant} frost {e.HasFrostEnchant} hp {e.MaxHPBonus} mana {e.MaxManaBonus}";

    private static string Whole(Equipment e) =>
        Fields(e) + $" | {e.Name} | E{e.GetEnchantmentCount()} | {string.Join(",", e.GetEnchantedKinds())} | v{e.Value}";

    /// <summary>Each counter's 0 based materials as the brief lists them.</summary>
    private static readonly Dictionary<int, (string Id, int Count)[]> Materials = new()
    {
        [Sunbane] = new[] { ("fading_starlight_dust", 2), ("withered_heart_petal", 2) },
        [Stonebane] = new[] { ("terravoks_heartstone", 2), ("iron_judgment_link", 2) },
        [Godbane] = new[] { ("eye_of_manwe", 2), ("heart_of_the_ocean", 1), ("withered_heart_petal", 1), ("iron_judgment_link", 1) },
    };

    private static void GiveEveryMaterial(Character hero, int count = 10)
    {
        foreach (var m in GameConfig.CraftingMaterials) hero.AddMaterial(m.Id, count);
    }

    private static string Stock(Character hero) =>
        string.Join(" ", GameConfig.CraftingMaterials.Select(m => $"{m.Id}={hero.CraftingMaterials.GetValueOrDefault(m.Id)}"));

    private static long Price(Character hero, int tier)
    {
        var t = TierRow(tier);
        var shop = new MagicShopLocation();
        return (long)typeof(MagicShopLocation).GetMethod("ApplyAllPriceModifiers", F)!
            .Invoke(shop, new object[] { t.baseCost + hero.Level * t.levelScale, hero })!;
    }

    private static (string name, int bonus, long baseCost, int levelScale, int minLevel, string description) TierRow(int tier)
    {
        var rows = ((string name, int bonus, long baseCost, int levelScale, int minLevel, string description)[])typeof(MagicShopLocation)
            .GetField("EnchantTiers", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        return rows[tier - 1];
    }

    private static T InLang<T>(string lang, Func<T> body)
    {
        var prev = GameConfig.Language;
        try { GameConfig.Language = lang; return body(); }
        finally { GameConfig.Language = prev; }
    }

    private static async Task<T> InLangAsync<T>(string lang, Func<Task<T>> body)
    {
        var prev = GameConfig.Language;
        try { GameConfig.Language = lang; return await body(); }
        finally { GameConfig.Language = prev; }
    }

    private static string Norm(string s) => Regex.Replace(s, @"\s+", " ").Trim();

    /// <summary>The key's line in the current language is in the shown text, however it was wrapped.</summary>
    private static bool Shows(string text, string key) => Norm(text).Contains(Norm(Loc.Get(key)));

    // ---------- the three tiers ----------

    [Theory]
    [InlineData(Sunbane, "Sunbane", 300_000L, 3_000, 80)]
    [InlineData(Stonebane, "Stonebane", 400_000L, 4_000, 88)]
    [InlineData(Godbane, "Godbane", 500_000L, 5_000, 95)]
    public void CounterTiers135_Table_NamesCostsLevelsAndNoBonus(int tier, string name, long baseCost, int scale, int minLevel)
    {
        MagicShopLocation.TierName(tier - 1).Should().Be(name);
        var row = TierRow(tier);
        row.bonus.Should().Be(0);
        row.baseCost.Should().Be(baseCost);
        row.levelScale.Should().Be(scale);
        row.minLevel.Should().Be(minLevel);
        foreach (int level in new[] { 80, 88, 95, 100 })
            (row.baseCost + level * row.levelScale).Should().Be(baseCost + level * (long)scale, $"the gold at level {level}");
        MagicShopLocation.IsCounterTier(tier).Should().BeTrue();
        MagicShopLocation.NamedEnchantTag(tier).Should().BeEmpty("a counter writes no name tag");
    }

    [Fact]
    public void CounterTiers135_TheFourteenOlderTiersKeepTheirPlaces()
    {
        var names = Enumerable.Range(0, 17).Select(MagicShopLocation.TierName).ToArray();
        names.Should().Equal("Minor", "Standard", "Greater", "Superior", "Divine Blessing", "Ocean's Touch", "Ward", "Predator",
            "Lifedrinker", "Mythic", "Legendary", "Godforged", "Phoenix Fire", "Frostbite", "Sunbane", "Stonebane", "Godbane");
        Enumerable.Range(1, 14).Any(MagicShopLocation.IsCounterTier).Should().BeFalse("no older tier is a counter");
    }

    [Theory]
    [InlineData(Sunbane, 80)] [InlineData(Sunbane, 100)]
    [InlineData(Stonebane, 88)] [InlineData(Stonebane, 100)]
    [InlineData(Godbane, 95)] [InlineData(Godbane, 100)]
    public async Task CounterTiers135_Enchant_ChargesTheTierPrice_SpendsItsMaterials_WritesTheKind_ChangesNoStat(int tier, int level)
    {
        var hero = Hero(level);
        Wear(hero, Blade());
        GiveEveryMaterial(hero);
        string statsBefore = Fields(Worn(hero));
        long goldBefore = hero.Gold;
        long price = Price(hero, tier);
        var row = TierRow(tier);
        price.Should().BeGreaterThan(0);

        string text = await Enchant(hero, tier);

        text.Should().Contain(MagicShopLocation.TierName(tier - 1));
        (goldBefore - hero.Gold).Should().Be(price, $"the price of {row.baseCost:N0} + {level} x {row.levelScale:N0}");
        foreach (var m in GameConfig.CraftingMaterials)
        {
            int spent = Materials[tier].Where(r => r.Id == m.Id).Sum(r => r.Count);
            hero.CraftingMaterials.GetValueOrDefault(m.Id).Should().Be(10 - spent, $"{m.Id} spent {spent}");
        }
        var after = Worn(hero);
        string code = MagicShopLocation.CounterKindCodes[tier - Sunbane];
        after.GetEnchantedKinds().Should().Equal(new[] { code }, "the counter writes its kind code");
        after.GetEnchantmentCount().Should().Be(1, "and takes one enchant slot");
        Fields(after).Should().Be(statsBefore, "a counter changes no stat");
        after.Name.Should().Be("Plain Blade", "a counter writes no name tag");
    }

    [Fact]
    public async Task CounterTiers135_KindCodes_AreTheProbeCodes()
    {
        var codes = new List<string>();
        foreach (int tier in new[] { Sunbane, Stonebane, Godbane })
        {
            var hero = Hero();
            Wear(hero, Blade());
            GiveEveryMaterial(hero);
            await Enchant(hero, tier);
            codes.Add(Worn(hero).GetEnchantedKinds().Single());
        }
        codes.Should().Equal(new[] { "sunbane", "stonebane", "godbane" }, "the codes the simulator probe measured");
        MagicShopLocation.CounterKindCodes.Should().Equal(codes);
        codes.Should().OnlyContain(c => Regex.IsMatch(c, "^[a-z]+$"), "the [ES:] marker holds lowercase letters only");
    }

    [Theory]
    [InlineData(Sunbane)] [InlineData(Stonebane)] [InlineData(Godbane)]
    public async Task CounterTiers135_BelowTheLevel_IsRefused_NothingTaken(int tier)
    {
        var hero = Hero(TierRow(tier).minLevel - 1);
        Wear(hero, Blade());
        GiveEveryMaterial(hero);
        string before = Whole(Worn(hero)), stock = Stock(hero);
        long gold = hero.Gold;

        string text = await Enchant(hero, tier);

        text.Should().Contain(Loc.Get("shop.enchant_level_req", TierRow(tier).minLevel));
        hero.Gold.Should().Be(gold);
        Stock(hero).Should().Be(stock);
        Whole(Worn(hero)).Should().Be(before);
    }

    [Theory]
    [InlineData(Sunbane)] [InlineData(Stonebane)] [InlineData(Godbane)]
    public async Task CounterTiers135_MissingMaterial_IsRefused_NothingTaken(int tier)
    {
        foreach (var (id, count) in Materials[tier])
        {
            var hero = Hero();
            Wear(hero, Blade());
            foreach (var (other, n) in Materials[tier]) hero.AddMaterial(other, other == id ? n - 1 : n);
            string before = Whole(Worn(hero)), stock = Stock(hero);
            long gold = hero.Gold;

            string text = await Enchant(hero, tier);

            text.Should().Contain(Loc.Get("magic_shop.needs_materials"), $"one {id} short");
            hero.Gold.Should().Be(gold);
            Stock(hero).Should().Be(stock);
            Whole(Worn(hero)).Should().Be(before);
        }
    }

    [Fact]
    public async Task CounterTiers135_Counter_SurvivesASaveAndLoad()
    {
        var hero = Hero();
        Wear(hero, Blade());
        GiveEveryMaterial(hero);
        await Enchant(hero, 7);          // Ward first, so the marker holds two codes
        await Enchant(hero, Godbane);
        Worn(hero).GetEnchantedKinds().Should().Equal("ward", "godbane");

        var data = (PlayerData)typeof(SaveSystem).GetMethod("SerializePlayer", F)!.Invoke(SaveSystem.Instance, new object[] { hero })!;
        var json = JsonSerializer.Serialize(data);
        data = JsonSerializer.Deserialize<PlayerData>(json)!;
        Character loaded;
        try { loaded = (Character)typeof(GameEngine).GetMethod("RestorePlayerFromSaveData", F)!.Invoke(GameEngine.Instance, new object[] { data })!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }

        var back = loaded.GetEquipment(EquipmentSlot.MainHand)!;
        back.Should().NotBeNull();
        back.GetEnchantmentCount().Should().Be(2, "the count comes back");
        back.GetEnchantedKinds().Should().Equal(new[] { "ward", "godbane" }, "the [ES:] marker keeps the counter");
        MagicShopLocation.CarriedCounter(back).Should().Be("godbane");
        back.Name.Should().Be("Plain Blade (Warded)");
    }

    // ---------- weapon only ----------

    [Theory]
    [InlineData(EquipmentSlot.Body, Sunbane)] [InlineData(EquipmentSlot.Body, Godbane)]
    [InlineData(EquipmentSlot.LFinger, Stonebane)] [InlineData(EquipmentSlot.Head, Godbane)]
    public async Task CounterTiers135_OnArmourOrARing_IsRefused_NothingTaken(EquipmentSlot slot, int tier)
    {
        var hero = Hero();
        var piece = new Equipment { Name = "Plain Piece", Slot = slot, ArmorClass = 5, MinLevel = 1, Value = 100 };
        Wear(hero, piece);
        GiveEveryMaterial(hero);
        string before = Whole(Worn(hero, slot)), stock = Stock(hero);
        long gold = hero.Gold;

        string text = await Enchant(hero, tier);

        Shows(text, "magic_shop.counter_weapon_only").Should().BeTrue("a counter goes on a weapon only");
        hero.Gold.Should().Be(gold, "no gold is taken");
        Stock(hero).Should().Be(stock, "no material is taken");
        Whole(Worn(hero, slot)).Should().Be(before, "the item is unchanged");
    }

    [Fact]
    public async Task CounterTiers135_OnAnOffHandWeapon_GoesOn()
    {
        var hero = Hero();
        Wear(hero, Blade("Plain Dirk", EquipmentSlot.OffHand));
        GiveEveryMaterial(hero);
        string text = await Enchant(hero, Stonebane);
        Shows(text, "magic_shop.counter_weapon_only").Should().BeFalse();
        Worn(hero, EquipmentSlot.OffHand).GetEnchantedKinds().Should().Equal("stonebane");
    }

    [Fact]
    public async Task CounterTiers135_OnAWeaponInTheBackpack_GoesOn()
    {
        var hero = Hero();
        hero.Inventory.Add(hero.ConvertEquipmentToLegacyItem(Blade("Spare Blade")));
        GiveEveryMaterial(hero);
        await Enchant(hero, Sunbane);
        var item = hero.Inventory.Single();
        item.Name.Should().Be("Spare Blade");
        item.EnchantMarkers.Should().Contain("[ES:sunbane]");
    }

    // ---------- one per weapon ----------

    [Theory]
    [InlineData(Sunbane, Sunbane)] [InlineData(Sunbane, Stonebane)] [InlineData(Sunbane, Godbane)]
    [InlineData(Godbane, Godbane)] [InlineData(Stonebane, Sunbane)]
    public async Task CounterTiers135_ASecondCounter_IsRefused_NothingTaken(int first, int second)
    {
        var hero = Hero();
        Wear(hero, Blade());
        GiveEveryMaterial(hero);
        await Enchant(hero, first);
        Worn(hero).GetEnchantmentCount().Should().Be(1);
        string before = Whole(Worn(hero)), stock = Stock(hero);
        long gold = hero.Gold;

        string text = await Enchant(hero, second);

        Shows(text, "magic_shop.counter_one_per_weapon").Should().BeTrue("a weapon takes one counter");
        hero.Gold.Should().Be(gold, "no gold is taken");
        Stock(hero).Should().Be(stock, "no material is taken");
        Whole(Worn(hero)).Should().Be(before, "the weapon is unchanged");
    }

    // ---------- the cap of 5 and the failure roll ----------

    [Fact]
    public async Task CounterTiers135_AtFiveEnchants_ACounterIsRefused_NothingTaken()
    {
        var hero = Hero();
        Wear(hero, Blade());
        GiveEveryMaterial(hero);
        await Enchant(hero, 1, 3);       // Minor Dexterity, so the full weapon still opens the tier list
        foreach (var tier in new[] { 5, 7, 8, 9 }) await Enchant(hero, tier);
        Worn(hero).GetEnchantmentCount().Should().Be(GameConfig.MaxEnchantments);
        string before = Whole(Worn(hero)), stock = Stock(hero);
        long gold = hero.Gold;

        string text = await Enchant(hero, Sunbane);

        text.Should().Contain(Loc.Get("shop.max_enchantments", GameConfig.MaxEnchantments));
        hero.Gold.Should().Be(gold);
        Stock(hero).Should().Be(stock);
        Whole(Worn(hero)).Should().Be(before);
    }

    [Fact]
    public async Task CounterTiers135_AsAFourthEnchant_CanFail_AndSpendsItsGoldAndMaterials()
    {
        var hero = Hero();
        Wear(hero, Blade());
        GiveEveryMaterial(hero);
        foreach (var tier in new[] { 5, 7, 8 }) await Enchant(hero, tier);
        long gold = hero.Gold;
        long price = Price(hero, Sunbane);

        MagicShopLocation.EnchantRandom = AlwaysFails;
        string text = await Enchant(hero, Sunbane);

        text.Should().Contain(Loc.Get("magic_shop.fail_destroyed"), "the 4th enchant failure roll holds for a counter");
        (gold - hero.Gold).Should().Be(price, "the gold is spent");
        hero.CraftingMaterials["fading_starlight_dust"].Should().Be(8, "the materials are spent before the roll");
        hero.CraftingMaterials["withered_heart_petal"].Should().Be(8);
        Worn(hero).GetEnchantedKinds().Should().NotContain("sunbane");
        Worn(hero).GetEnchantmentCount().Should().Be(2, "one existing enchant is destroyed and the counter is not added");
    }

    // ---------- removal ----------

    [Theory]
    [InlineData(Sunbane)] [InlineData(Stonebane)] [InlineData(Godbane)]
    public async Task CounterTiers135_Removal_TakesTheCodeOff(int tier)
    {
        var hero = Hero();
        Wear(hero, Blade());
        GiveEveryMaterial(hero);
        string bare = Fields(Worn(hero));
        await Enchant(hero, 7);
        await Enchant(hero, tier);
        MagicShopLocation.CarriedCounter(Worn(hero)).Should().NotBeEmpty();

        await Run(hero, "RemoveEnchantment", "1", "Y");

        var back = Worn(hero);
        back.GetEnchantedKinds().Should().BeEmpty("removal takes the counter's code off with the rest");
        back.GetEnchantmentCount().Should().Be(0);
        back.Name.Should().Be("Plain Blade");
        Fields(back).Should().Be(bare);
        MagicShopLocation.CarriedCounter(back).Should().BeEmpty();

        // and the weapon can take a counter again
        await Enchant(hero, tier);
        MagicShopLocation.CarriedCounter(Worn(hero)).Should().Be(MagicShopLocation.CounterKindCodes[tier - Sunbane]);
    }

    [Fact]
    public async Task CounterTiers135_DestroyedByAFailure_TakesTheCodeOff_AndNoStat()
    {
        var hero = Hero();
        Wear(hero, Blade());
        GiveEveryMaterial(hero);
        string bare = Fields(Worn(hero));
        await Enchant(hero, Godbane);

        var (damaged, kind) = MagicShopLocation.DestroyOneEnchant(Worn(hero), new FixedRandom(0.0));

        kind.Should().Be("godbane");
        damaged.GetEnchantedKinds().Should().BeEmpty();
        damaged.GetEnchantmentCount().Should().Be(0);
        damaged.Name.Should().Be("Plain Blade");
        Fields(damaged).Should().Be(bare);
    }

    [Fact]
    public void CounterTiers135_AnOlderTemplateWeaponWithACounter_HasAKnownBaseForm()
    {
        var template = EquipmentDatabase.GetBuiltInTemplates()
            .First(t => t.Slot == EquipmentSlot.MainHand && t.WeaponPower > 0 && MagicShopLocation.StripEnchantTags(t.Name) == t.Name);
        var item = template.Clone();
        item.Name = template.Name;
        item.IncrementEnchantmentCount();
        item.AddEnchantedKind("sunbane");
        MagicShopLocation.VerifiedTemplate(item).Should().NotBeNull("a counter adds no stat and no tag, so the template still matches");
        MagicShopLocation.EnchantBaseOf(item).Should().NotBeNull("so its removal knows the form to return to");
    }

    // ---------- texts ----------

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test must run inside the repository");
        return dir!.FullName;
    }

    private static readonly string[] NewKeys =
    {
        "magic_shop.tier_sunbane", "magic_shop.tier_stonebane", "magic_shop.tier_godbane",
        "magic_shop.tier_desc_sunbane", "magic_shop.tier_desc_stonebane", "magic_shop.tier_desc_godbane",
        "magic_shop.counter_weapon_only", "magic_shop.counter_one_per_weapon",
    };

    [Fact]
    public void CounterTiers135_NewKeys_AreInEveryLanguage_AndTranslated()
    {
        var files = Langs.ToDictionary(l => l, l =>
            JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(Path.Combine(RepoRoot(), "Localization", l + ".json")))!);
        foreach (var key in NewKeys)
        {
            string en = files["en"][key].GetString()!;
            en.Should().NotBeNullOrWhiteSpace(key);
            foreach (var lang in Langs.Skip(1))
            {
                files[lang].Should().ContainKey(key, $"{lang} has {key}");
                string text = files[lang][key].GetString()!;
                text.Should().NotBeNullOrWhiteSpace($"{lang} {key}");
                text.Should().NotBe(en, $"{lang} {key} is translated");
            }
        }
        foreach (var lang in Langs)
            foreach (var (code, god) in new[] { ("sunbane", "Aurelion"), ("stonebane", "Terravok"), ("godbane", "Manwe") })
                files[lang][$"magic_shop.tier_desc_{code}"].GetString().Should().Contain(god, $"the {lang} description names the god");
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public async Task CounterTiers135_Menu_ShowsTheCountersAfterFrostbite_AndEveryRowFits(string lang)
    {
        var hero = Hero();
        Wear(hero, Blade());
        GiveEveryMaterial(hero);
        string shown = await InLangAsync(lang, () => Run(hero, "EnchantEquipment", "1", "0"));
        var rows = shown.Replace("\r", "").Split('\n');
        foreach (var row in rows)
            row.TrimEnd().Length.Should().BeLessOrEqualTo(79, $"{lang} row fits: [{row}]");
        int frost = Array.FindIndex(rows, r => r.Contains("[14]"));
        frost.Should().BeGreaterThan(0);
        for (int tier = Sunbane; tier <= Godbane; tier++)
        {
            int t = tier;
            int at = Array.FindIndex(rows, r => r.Contains($"[{t}]"));
            at.Should().BeGreaterThan(frost, $"tier {t} comes after Frostbite");
            rows[at].Should().Contain(InLang(lang, () => MagicShopLocation.TierName(t - 1)));
            rows[at].Should().Contain(InLang(lang, () => MagicShopLocation.TierDescription(t - 1)).TrimEnd());
        }
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public async Task CounterTiers135_RefusalLines_FitInEveryLanguage(string lang)
    {
        var hero = Hero();
        Wear(hero, new Equipment { Name = "Plain Piece", Slot = EquipmentSlot.Body, ArmorClass = 5, MinLevel = 1, Value = 100 });
        GiveEveryMaterial(hero);
        string text = await InLangAsync(lang, () => Enchant(hero, Sunbane));
        var hero2 = Hero();
        Wear(hero2, Blade());
        GiveEveryMaterial(hero2);
        await Enchant(hero2, Sunbane);
        text += await InLangAsync(lang, () => Enchant(hero2, Godbane));
        foreach (var row in text.Replace("\r", "").Split('\n'))
            row.TrimEnd().Length.Should().BeLessOrEqualTo(79, $"{lang} row fits: [{row}]");
        foreach (var key in new[] { "magic_shop.counter_weapon_only", "magic_shop.counter_one_per_weapon" })
            InLang(lang, () => Shows(text, key)).Should().BeTrue($"{lang} {key} shown");
    }
}
