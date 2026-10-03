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
/// v1.2.5: two Magic Shop fixes. Removing an enchantment strips every tag an enchant can write into the item's
/// stored name, Phoenix Fire and Frostbite included, and the gear set family still resolves. The compact BBS
/// menu shows each key next to a whole word in every language ("[S]ell", "[S] Eladás"), never a label split
/// after its first letter, and every row fits in 79 columns.
/// </summary>
[Collection("SharedGameSingletons")]
public class MagicFix125Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int MaxWidth = 79;

    // GameConfig.MaxNameLength (30) characters.
    private const string LongName = "Aranyszivu Hosszunevu Kalandor";

    private static readonly string[] AllLanguages = { "en", "es", "fr", "hu", "it" };

    // ---------- helpers ----------

    private sealed class Shop
    {
        public MagicShopLocation Location = new();
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

    private static Shop Open(Character hero, params string[] lines)
    {
        var s = new Shop();
        s.Term = new TerminalEmulator(new LineStream(lines.Concat(Enumerable.Repeat("", 12)), _ =>
        {
            s.Term.StreamWriterInternal?.Flush();
            s.Output.WriteByte((byte)'\n');
        }), s.Output);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(s.Location, s.Term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(s.Location, hero);
        return s;
    }

    private static async Task Run(Shop s, string method, params object?[] args)
    {
        var r = typeof(MagicShopLocation).GetMethod(method, F)!.Invoke(s.Location, args);
        if (r is Task t) await t;
    }

    private static async Task<T> InLanguage<T>(string lang, Func<Task<T>> body)
    {
        var prev = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            return await body();
        }
        finally { GameConfig.Language = prev; }
    }

    private static List<string> Rows(string text) => text.Replace("\r", "").Split('\n').ToList();

    private static void Capture(string name, string text)
    {
        var dir = Environment.GetEnvironmentVariable("USURPER_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, name), text);
    }

    private static Character Hero() => new()
    {
        Name1 = "tester", Name2 = LongName, Class = CharacterClass.Magician, Race = CharacterRace.Human, Level = 100,
        HP = 5000, MaxHP = 5000, BaseMaxHP = 5000, Mana = 5000, MaxMana = 5000, Intelligence = 200,
        AI = CharacterAI.Human, Gold = 9_000_000_000, AutoEquipDisabled = true,
    };

    private static Character Wearing(string name, int enchants)
    {
        var hero = Hero();
        var cap = new Equipment { Name = name, Slot = EquipmentSlot.Head, ArmorClass = 3, MinLevel = 1, Value = 100 };
        for (int i = 0; i < enchants; i++) cap.IncrementEnchantmentCount();
        hero.EquippedItems[EquipmentSlot.Head] = EquipmentDatabase.RegisterDynamic(cap);
        foreach (var m in GameConfig.CraftingMaterials) hero.AddMaterial(m.Id, 5);
        return hero;
    }

    private static async Task Remove(Character hero)
    {
        var s = Open(hero, "1", "Y");
        await Run(s, "RemoveEnchantment", hero);
    }

    private static string Family(string name) => GearSetFamilyResolver.FamilyOf(null, name, GearSetSlotKind.Head)!;

    // ---------- 1. enchant removal strips every tag ----------

    // The tags as the enchant writes them, written out here so a tag dropped from the code's list shows.
    public static IEnumerable<object[]> EveryEnchant() => new[]
    {
        new object[] { 2, 3, " +4 Dex" },
        new object[] { 5, 0, " (Blessed)" },
        new object[] { 6, 0, " (Ocean-Touched)" },
        new object[] { 7, 0, " (Warded)" },
        new object[] { 8, 0, " (Predator)" },
        new object[] { 9, 0, " (Lifedrinker)" },
        new object[] { 13, 0, " (Phoenix Fire)" },
        new object[] { 14, 0, " (Frostbite)" },
    };

    [Theory]
    [MemberData(nameof(EveryEnchant))]
    public async Task EnchantThenRemove_LeavesTheOriginalName_AndTheFamilyResolves(int tier, int stat, string tag)
    {
        Family("Leather Cap").Should().Be("Leather Cap");
        // Ocean's Touch asks for awakening 2; the level is set for the run and put back after.
        var awakening = typeof(OceanPhilosophySystem).GetProperty("AwakeningLevel")!;
        int prevAwakening = OceanPhilosophySystem.Instance.AwakeningLevel;
        try
        {
            awakening.SetValue(OceanPhilosophySystem.Instance, 2);
            await EnchantAndRemove(tier, stat, tag);
        }
        finally { awakening.SetValue(OceanPhilosophySystem.Instance, prevAwakening); }
    }

    private static async Task EnchantAndRemove(int tier, int stat, string tag)
    {
        foreach (var lang in new[] { "en", "hu" })
        {
            var hero = Wearing("Leather Cap", 0);
            string before = Form(hero.GetEquipment(EquipmentSlot.Head)!);
            await InLanguage(lang, async () =>
            {
                var s = stat > 0 ? Open(hero, "1", $"{tier}", $"{stat}", "Y") : Open(hero, "1", $"{tier}", "Y");
                await Run(s, "EnchantEquipment", hero);
                return 0;
            });
            string enchanted = hero.GetEquipment(EquipmentSlot.Head)!.Name;
            enchanted.Should().Be("Leather Cap" + tag, $"tier {tier} writes its English tag in {lang}");
            Family(enchanted).Should().Be("Leather Cap", "the resolver reads the enchanted name");

            Form(hero.GetEquipment(EquipmentSlot.Head)!).Should().NotBe(before, $"tier {tier} changes the item");
            long gold = hero.Gold;

            await InLanguage(lang, async () => { await Remove(hero); return 0; });
            var cap = hero.GetEquipment(EquipmentSlot.Head)!;
            cap.Name.Should().Be("Leather Cap", $"removal strips{tag} in {lang}");
            Family(cap.Name).Should().Be("Leather Cap", "the resolver reads the stripped name");
            Form(cap).Should().Be(before, $"removal returns every stat, flag, the value and the name of tier {tier} to base");
            cap.HasFireEnchant.Should().BeFalse(); cap.HasFrostEnchant.Should().BeFalse();
            cap.GetEnchantmentCount().Should().Be(0); cap.GetEnchantedKinds().Should().BeEmpty();
            cap.EnchantBase.Should().BeEmpty("the record goes with the enchants");
            hero.Gold.Should().BeLessThan(gold, "removal is paid for, never refunded");
        }
    }

    /// <summary>Everything an enchant can change (name, value, stats, fire and frost), plus the fields it never
    /// touches, as one comparable string.</summary>
    private static string Form(Equipment e) => System.Text.Json.JsonSerializer.Serialize(e.ToEnchantBaseRecord())
        + $"|{e.MinLevel}|{e.Rarity}|{e.MaxHPBonus}|{e.MaxManaBonus}|{e.ShieldBonus}|{e.BlockChance}|{e.Slot}";

    [Theory]
    [MemberData(nameof(EveryEnchant))]
    public async Task Removal_CleansATagAnItemAlreadyCarries(int tier, int stat, string tag)
    {
        _ = tier; _ = stat;
        // An older removal left this tag on the name and cleared the count. Enchanted again now, the record keeps
        // the stale name, and removal strips the tag from it too.
        var hero = Wearing("Leather Cap" + tag, 0);
        var s = Open(hero, "1", "7", "Y");
        await Run(s, "EnchantEquipment", hero);
        hero.GetEquipment(EquipmentSlot.Head)!.Name.Should().Be("Leather Cap" + tag + " (Warded)");
        await Remove(hero);
        hero.GetEquipment(EquipmentSlot.Head)!.Name.Should().Be("Leather Cap", $"removal strips{tag} from a stored name");
        hero.GetEquipment(EquipmentSlot.Head)!.MagicResistance.Should().Be(0);
    }

    // ---------- 1b. full undo through the bag, the save, the enchant limit, and items with no record ----------

    private static PlayerData Save(Character hero) =>
        (PlayerData)typeof(SaveSystem).GetMethod("SerializePlayer", F)!.Invoke(SaveSystem.Instance, new object[] { hero })!;

    private static Character Reload(Character hero)
    {
        var data = System.Text.Json.JsonSerializer.Deserialize<PlayerData>(System.Text.Json.JsonSerializer.Serialize(Save(hero)))!;
        var restore = typeof(GameEngine).GetMethod("RestorePlayerFromSaveData", F)!;
        try
        {
            var back = (Character)restore.Invoke(GameEngine.Instance, new object[] { data })!;
            back.Gold = hero.Gold;
            return back;
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    private static async Task Enchant(Character hero, int tier, int stat = 0)
    {
        var s = stat > 0 ? Open(hero, "1", $"{tier}", $"{stat}", "Y") : Open(hero, "1", $"{tier}", "Y");
        await Run(s, "EnchantEquipment", hero);
    }

    [Fact]
    public async Task TheRecord_SurvivesTheBag_AndASaveAndReload()
    {
        var hero = Wearing("Leather Cap", 0);
        string before = Form(hero.GetEquipment(EquipmentSlot.Head)!);
        await Enchant(hero, 13);
        await Enchant(hero, 2, 3);
        var cap = hero.GetEquipment(EquipmentSlot.Head)!;
        cap.EnchantBase.Should().NotBeEmpty("the first enchant records the base form");

        // through the bag: Equipment to Item, the inventory save form, and back
        var item = hero.ConvertEquipmentToLegacyItem(cap);
        var saved = InventoryItemData.FromItem(item);
        var json = System.Text.Json.JsonSerializer.Deserialize<InventoryItemData>(System.Text.Json.JsonSerializer.Serialize(saved))!;
        var back = Character.BuildEquipmentFromItem(json.ToItem(), EquipmentSlot.Head, WeaponHandedness.None, WeaponType.None);
        back.EnchantBase.Should().Be(cap.EnchantBase, "the bag carries the record");

        // through a save and reload
        var loaded = Reload(hero);
        var reloaded = loaded.GetEquipment(EquipmentSlot.Head)!;
        reloaded.EnchantBase.Should().Be(cap.EnchantBase, "DynamicEquipment[].EnchantBase is saved and restored");
        Form(reloaded).Should().Be(Form(cap));
        await Remove(loaded);
        Form(loaded.GetEquipment(EquipmentSlot.Head)!).Should().Be(before);
    }

    [Fact]
    public async Task AnEnchantedItemStolenInTheDormitory_KeepsItsRecordAndCount_AndRemovalLandsAtBase()
    {
        var victim = Wearing("Leather Cap", 0);
        string before = Form(victim.GetEquipment(EquipmentSlot.Head)!);
        await Enchant(victim, 13);
        await Enchant(victim, 2, 3);
        var cap = victim.GetEquipment(EquipmentSlot.Head)!;
        var saved = Save(victim).DynamicEquipment.Single(d => d.Name == cap.Name);

        // the thief's side: the stolen item, into the bag, and worn
        var stolen = DormitoryLocation.StolenEquipmentFrom(saved, cap.Name);
        EquipmentDatabase.RegisterDynamic(stolen);
        var thief = Hero();
        var bagItem = thief.ConvertEquipmentToLegacyItem(stolen);
        var worn = Character.BuildEquipmentFromItem(bagItem, EquipmentSlot.Head, WeaponHandedness.None, WeaponType.None);
        thief.EquippedItems[EquipmentSlot.Head] = EquipmentDatabase.RegisterDynamic(worn);
        var onThief = thief.GetEquipment(EquipmentSlot.Head)!;
        onThief.EnchantBase.Should().Be(cap.EnchantBase, "the record comes with the stolen item");
        onThief.GetEnchantmentCount().Should().Be(2, "the enchant count comes with it, so the limit holds");
        onThief.GetEnchantedKinds().Should().BeEquivalentTo(cap.GetEnchantedKinds());

        await Remove(thief);
        Form(thief.GetEquipment(EquipmentSlot.Head)!).Should().Be(before, "removal lands at the item's base");
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public async Task EnchantThenReforgeThenRemove_GivesTheReforgedBase(int seed)
    {
        var hero = Hero();
        var sword = new Equipment
        {
            Name = "Broadsword", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Sword,
            WeaponPower = 40, StrengthBonus = 10, DexterityBonus = 10, MinLevel = 1, Value = 1000, Rarity = EquipmentRarity.Rare,
        };
        hero.EquippedItems[EquipmentSlot.MainHand] = EquipmentDatabase.RegisterDynamic(sword);
        foreach (var m in GameConfig.CraftingMaterials) hero.AddMaterial(m.Id, 5);
        await Enchant(hero, 2, 3);     // +4 Dex
        await Enchant(hero, 13);       // Phoenix Fire, +20 power and the fire flag
        var weapon = hero.GetEquipment(EquipmentSlot.MainHand)!;
        weapon.Name.Should().Be("Broadsword +4 Dex (Phoenix Fire)");

        WeaponShopLocation.ApplyReforge(weapon, WeaponShopLocation.RollReforge(weapon, hero.Level, new Random(seed), out _));
        var reforged = weapon.Clone();

        await Remove(hero);
        var back = hero.GetEquipment(EquipmentSlot.MainHand)!;
        back.Name.Should().Be("Broadsword");
        back.HasFireEnchant.Should().BeFalse("the fire enchant goes");
        back.DexterityBonus.Should().Be(Math.Max(0, reforged.DexterityBonus - 4), "the reforged Dexterity less the enchant's 4");
        back.WeaponPower.Should().Be(Math.Max(0, reforged.WeaponPower - 20), "the reforged power less Phoenix Fire's 20");
        back.StrengthBonus.Should().Be(reforged.StrengthBonus, "a stat no enchant touched keeps its reforged value");
        back.Rarity.Should().Be(reforged.Rarity, "the reforge's rarity stays");
        foreach (var (got, max) in new[] { (back.DexterityBonus, reforged.DexterityBonus), (back.WeaponPower, reforged.WeaponPower),
                     (back.StrengthBonus, reforged.StrengthBonus), (back.ArmorClass, reforged.ArmorClass) })
            got.Should().BeLessOrEqualTo(max, "removal never adds anything");
        back.Value.Should().BeLessOrEqualTo(reforged.Value);
        back.GetEnchantmentCount().Should().Be(0);
    }

    [Fact]
    public async Task AfterRemoval_TheEnchantLimitStillHolds_AndTheSameKindCanGoOnOnce()
    {
        var hero = Wearing("Leather Cap", 0);
        var cap = hero.GetEquipment(EquipmentSlot.Head)!;
        await Enchant(hero, 2, 3);
        var once = hero.GetEquipment(EquipmentSlot.Head)!;
        // a sixth enchant is refused at the limit
        for (int i = once.GetEnchantmentCount(); i < GameConfig.MaxEnchantments; i++) once.IncrementEnchantmentCount();
        string full = Form(once);
        await Enchant(hero, 5);
        Form(hero.GetEquipment(EquipmentSlot.Head)!).Should().Be(full, "an item at the limit takes no more");

        await Remove(hero);
        hero.GetEquipment(EquipmentSlot.Head)!.DexterityBonus.Should().Be(cap.DexterityBonus);
        await Enchant(hero, 2, 3);
        var again = hero.GetEquipment(EquipmentSlot.Head)!;
        again.Name.Should().Be("Leather Cap +4 Dex");
        again.DexterityBonus.Should().Be(cap.DexterityBonus + 4, "the bonus goes on once over the base");
        again.GetEnchantmentCount().Should().Be(1);
        await Enchant(hero, 2, 3);
        hero.GetEquipment(EquipmentSlot.Head)!.DexterityBonus.Should().Be(cap.DexterityBonus + 4, "the same kind twice is still refused");
    }

    private static Equipment Template(string name) =>
        EquipmentDatabase.GetBuiltInTemplates().Single(t => t.Name == name && t.Slot == EquipmentSlot.Head);

    /// <summary>An item enchanted before 1.2.5: the template plus +4 Dex and Frostbite, two markers, no record.</summary>
    private static Equipment LegacyCap(int extraDex = 0)
    {
        var e = Template("Leather Cap").Clone();
        e.DexterityBonus += 4 + extraDex;
        MagicShopLocation.ApplyNamedEnchant(e, 14);
        e.Name = "Leather Cap +4 Dex (Frostbite)";
        e.Value += 9000;
        e.IncrementEnchantmentCount(); e.IncrementEnchantmentCount();
        e.AddEnchantedKind("dex"); e.AddEnchantedKind("frost");
        return e;
    }

    [Fact]
    public async Task ALegacyStackedItem_IsUnchangedByASaveAndReload_AndRemovalLandsOnItsTemplate()
    {
        var hero = Hero();
        hero.EquippedItems[EquipmentSlot.Head] = EquipmentDatabase.RegisterDynamic(LegacyCap());
        var cap = hero.GetEquipment(EquipmentSlot.Head)!;
        cap.EnchantBase.Should().BeEmpty("enchanted before 1.2.5, it has no record");

        var loaded = Reload(hero);
        var reloaded = loaded.GetEquipment(EquipmentSlot.Head)!;
        Form(reloaded).Should().Be(Form(cap), "a reload leaves the stacked item as it was");
        reloaded.Description.Should().Be(cap.Description);
        reloaded.EnchantBase.Should().BeEmpty();

        await Remove(loaded);
        var stripped = loaded.GetEquipment(EquipmentSlot.Head)!;
        Form(stripped).Should().Be(Form(Template("Leather Cap")), "removal returns it exactly to its template");
        stripped.GetEnchantmentCount().Should().Be(0);
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task AnItemWithNoRecordAndNoMatchingTemplate_IsLeftAlone_AndNothingIsCharged(string lang)
    {
        foreach (var item in new[] { RolledLegacy(), LegacyCap(extraDex: 1) })
        {
            var hero = Hero();
            int id = EquipmentDatabase.RegisterDynamic(item);
            hero.EquippedItems[EquipmentSlot.Head] = id;
            string before = Form(item) + item.Description;
            long gold = hero.Gold;
            string shown = await InLanguage(lang, async () =>
            {
                var s = Open(hero, "1", "Y");
                await Run(s, "RemoveEnchantment", hero);
                return s.Text;
            });
            hero.EquippedItems[EquipmentSlot.Head].Should().Be(id, "the item is not replaced");
            var still = hero.GetEquipment(EquipmentSlot.Head)!;
            (Form(still) + still.Description).Should().Be(before, "nothing on it changes");
            hero.Gold.Should().Be(gold, "nothing is charged");
            shown.Should().Contain(Loc.GetIn(lang, "magic_shop.remove_no_base"));
            Capture($"magic-fix-no-base-{lang}.txt", shown);
            foreach (var row in Rows(shown))
                if (row.Length > 0 && "╔║╚".IndexOf(row[0]) < 0)
                    row.Length.Should().BeLessOrEqualTo(MaxWidth, $"every {lang} row fits: \"{row}\"");
        }
    }

    /// <summary>A rolled loot item (no built-in template of that name) enchanted before 1.2.5.</summary>
    private static Equipment RolledLegacy()
    {
        var e = new Equipment
        {
            Name = "Abyssal Studded Leather Cap of Fortitu +4 Dex", Slot = EquipmentSlot.Head, ArmorClass = 41, DexterityBonus = 13,
            MinLevel = 1, Value = 5000,
        };
        e.IncrementEnchantmentCount();
        e.AddEnchantedKind("dex");
        return e;
    }

    [Fact]
    public void StripEnchantTags_TakesOutStackedTags_AndTheLegacyBarePlus()
    {
        MagicShopLocation.StripEnchantTags("Leather Cap +4 Dex (Phoenix Fire) +6 Str (Frostbite)").Should().Be("Leather Cap");
        MagicShopLocation.StripEnchantTags("Leather Cap +3").Should().Be("Leather Cap", "the legacy flow wrote a bare +N");
        MagicShopLocation.StripEnchantTags("Leather Cap").Should().Be("Leather Cap");
        MagicShopLocation.AllNamedEnchantTags().Should().BeEquivalentTo(
            EveryEnchant().Select(e => (string)e[2]).Where(t => t.StartsWith(" (")),
            "every named tag the enchant writes is in the strip list");
    }

    // ---------- 2. the compact BBS menu ----------

    // Each key with the key of its label, as DisplayLocationBBS shows them.
    private static readonly (string Key, string LocKey)[] MenuItems =
    {
        ("1", "magic_shop.rings"), ("2", "magic_shop.necklaces"), ("S", "shop.sell"), ("I", "magic_shop.bbs_word_identify"),
        ("H", "magic_shop.bbs_word_healing_pots"), ("M", "magic_shop.bbs_word_mana_pots"), ("D", "magic_shop.bbs_word_dungeon_reset"),
        ("E", "magic_shop.bbs_word_enchant"), ("W", "magic_shop.bbs_remove_ench"), ("C", "magic_shop.menu_curse_removal"),
        ("V", "magic_shop.bbs_love_spells"), ("K", "magic_shop.bbs_dark_arts"), ("Y", "magic_shop.bbs_study"), ("G", "magic_shop.bbs_scry"),
        ("T", "magic_shop.talk_to"), ("R", "shop.return"),
    };

    private static Task<string> Bbs(string lang) => InLanguage(lang, () =>
    {
        var owner = typeof(MagicShopLocation).GetField("_ownerName", BindingFlags.NonPublic | BindingFlags.Static)!;
        var prevOwner = (string)owner.GetValue(null)!;
        try
        {
            MagicShopLocation.SetOwnerName(LongName);
            var s = Open(Hero());
            typeof(MagicShopLocation).GetMethod("DisplayLocationBBS", F)!.Invoke(s.Location, null);
            return Task.FromResult(s.Text);
        }
        finally { owner.SetValue(null, prevOwner); }
    });

    /// <summary>The key and its whole label: "[S]ell" when the label starts with the key letter, else "[S] Eladás".</summary>
    private static string Expected(string key, string label) =>
        label.StartsWith(key, StringComparison.OrdinalIgnoreCase) ? $"[{label[..key.Length]}]{label[key.Length..]}" : $"[{key}] {label}";

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public async Task BbsMenu_ShowsEachKeyNextToAWholeWord_AndFits(string lang)
    {
        string shown = await Bbs(lang);
        Capture($"magic-fix-bbs-{lang}.txt", shown);
        var menuRows = new List<string>();
        foreach (var (key, locKey) in MenuItems)
        {
            string label = locKey == "magic_shop.talk_to" ? Loc.GetIn(lang, locKey, LongName) : Loc.GetIn(lang, locKey);
            label.Should().NotBeNullOrWhiteSpace();
            char.IsLetterOrDigit(label[0]).Should().BeTrue($"{lang} {locKey} is a whole word, not a fragment");
            if (key.All(char.IsLetter) && label.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                char.IsUpper(label[0]).Should().BeTrue($"{lang} {locKey} starts with its capital, so it is the whole word");
            string item = Expected(key, label);
            shown.Should().Contain(item, $"{lang} shows [{key}] next to the whole label");
            menuRows.AddRange(Rows(shown).Where(r => r.Contains(item)));
        }
        menuRows.Should().HaveCountGreaterOrEqualTo(6);
        foreach (var row in menuRows.Distinct())
        {
            row.Length.Should().BeLessOrEqualTo(MaxWidth, $"every {lang} menu row fits: \"{row}\"");
            // no key glued to a capitalised word ("[S]Eladás", "[R]Return"), nothing split off after it
            Regex.IsMatch(row, @"\[\w\]\p{Lu}").Should().BeFalse($"no {lang} key is glued to a word: \"{row}\"");
        }
    }

    [Fact]
    public async Task BbsMenu_English_KeepsItsKeyLetters()
    {
        string shown = await Bbs("en");
        foreach (var w in new[] { " [1] Rings [2] Necklaces [S]ell [I]dentify", " [H]ealing Pots [M]ana Pots [D]ungeon Reset",
                     " [E]nchant [W] Remove Ench [C]urse Removal", " [V] Love Spells [K] Dark Arts [Y] Study [G] Scry",
                     $" [T]alk to {LongName} [R]eturn to street" })
            shown.Should().Contain(w);
        string hu = await Bbs("hu");
        hu.Should().Contain(" [S] Eladás").And.Contain($" [T] Beszélgetés vele: {LongName} [R] Visszatérés az utcára");
    }

    [Fact]
    public void NewKeys_AreInFiveLanguages_Translated_WithoutDashes()
    {
        var keys = new[] { "identify", "healing_pots", "mana_pots", "dungeon_reset", "enchant" }.Select(k => "magic_shop.bbs_word_" + k);
        foreach (var key in keys)
        {
            foreach (var lang in AllLanguages)
            {
                string v = Loc.GetIn(lang, key);
                v.Should().NotBeNullOrWhiteSpace().And.NotBe(key).And.NotContain(((char)0x2014).ToString()).And.NotContain(((char)0x2013).ToString());
            }
            Loc.GetIn("hu", key).Should().NotBe(Loc.GetIn("en", key), $"{key} is translated");
        }
        // the first-letter fragments are gone
        foreach (var old in new[] { "sell", "identify", "healing_pots", "mana_pots", "dungeon_reset", "enchant", "curse_removal", "talk_to" })
            foreach (var lang in AllLanguages)
                Loc.GetIn(lang, "magic_shop.bbs_" + old).Should().Be("magic_shop.bbs_" + old, $"{lang} no longer holds the fragment key");
    }
}
