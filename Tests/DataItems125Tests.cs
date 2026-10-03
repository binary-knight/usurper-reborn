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
using UsurperRemake.UI;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.5: the item tables (EquipmentData, the classic weapons and armour of ItemManager, NPCItemGenerator) are
/// shown in the reader's language through ItemNames, by key, while every stored item name stays English: what is
/// bought, picked up, saved, reloaded, matched by the gear set resolver or read by the Magic Shop's enchant code.
/// The Magic Shop's enchant parts (" +4 Dex", " (Blessed)") stay as stored after the shown base name.
/// Every inventory, equipment and shop row fits 79 columns in all five languages with the longest names.
/// </summary>
[Collection("SharedGameSingletons")]
public class DataItems125Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private const int MaxWidth = 79;

    private static readonly string[] AllLanguages = { "en", "es", "fr", "hu", "it" };
    private static readonly string[] OtherLanguages = { "es", "fr", "hu", "it" };

    // Names a language keeps as they are: proper names and words the language uses unchanged.
    private static readonly Dictionary<string, string[]> SameInLanguage = new()
    {
        ["Excalibur"] = new[] { "es", "fr", "hu", "it" },
        ["Gungnir"] = new[] { "es", "fr", "hu", "it" },
        ["Mjolnir"] = new[] { "es", "fr", "it" },
        ["Ragnarok"] = new[] { "es", "it" },
        ["Longinus"] = new[] { "fr", "hu" },
        ["Nunchaku"] = new[] { "es", "fr", "it" },
        ["Gi"] = new[] { "es", "fr", "hu", "it" },
        ["Estoc"] = new[] { "fr" },
        ["Carnage"] = new[] { "fr" },
        ["Barbute"] = new[] { "fr" },
        ["Stiletto"] = new[] { "hu", "it" },
        ["Claymore"] = new[] { "es", "fr", "hu", "it" },
        ["Zweihander"] = new[] { "fr", "hu", "it" },
        ["Bardiche"] = new[] { "es", "fr" },
        ["Flamberge"] = new[] { "es", "fr", "hu", "it" },
        ["Glaive"] = new[] { "fr" },
    };

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

    private static Screen NewScreen(params string[] lines)
    {
        var s = new Screen();
        s.Term = new TerminalEmulator(new LineStream(lines.Concat(Enumerable.Repeat("", 12)), _ =>
        {
            s.Term.StreamWriterInternal?.Flush();
            s.Output.WriteByte((byte)'\n');
        }), s.Output);
        return s;
    }

    private static Screen At(BaseLocation location, Character hero, params string[] lines)
    {
        var s = NewScreen(lines);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(location, s.Term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(location, hero);
        return s;
    }

    private static async Task<T> InLanguage<T>(string lang, Func<Task<T>> body)
    {
        var prev = GameConfig.Language;
        bool sr = GameConfig.ScreenReaderMode;
        try
        {
            GameConfig.Language = lang;
            GameConfig.ScreenReaderMode = false;
            return await body();
        }
        finally { GameConfig.Language = prev; GameConfig.ScreenReaderMode = sr; }
    }

    private static T InLang<T>(string lang, Func<T> body) =>
        InLanguage(lang, () => Task.FromResult(body())).GetAwaiter().GetResult();

    private static string L(string lang, string key, params object[] args) => Loc.GetIn(lang, key, args);

    private static List<string> Rows(string text) => text.Replace("\r", "").Split('\n').ToList();

    private static void Capture(string name, string text)
    {
        var dir = Environment.GetEnvironmentVariable("USURPER_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, name), text);
    }

    private static void EveryRowFits(string text, string screen)
    {
        foreach (var row in Rows(text))
            row.Length.Should().BeLessOrEqualTo(MaxWidth, $"every row of the {screen} fits in {MaxWidth} columns: \"{row}\"");
    }

    private static Character Hero() => new()
    {
        Name1 = "tester", Name2 = "Tester", Class = CharacterClass.Warrior, Level = 100, HP = 5000, MaxHP = 5000,
        AI = CharacterAI.Human, Gold = 2_000_000_000, Strength = 500,
    };

    private static Equipment BuiltIn(string name) => EquipmentDatabase.GetBuiltInTemplates().First(e => e.Name == name);

    /// <summary>A player's own copy of a built-in piece, as a purchase or a drop registers it.</summary>
    private static Equipment OwnCopy(string name)
    {
        var copy = BuiltIn(name).Clone();
        EquipmentDatabase.RegisterDynamic(copy);
        return copy;
    }

    private static string Key(string english) => LootGenerator.TemplateLocKey(english);

    /// <summary>Every English name the three owned tables hold.</summary>
    private static List<string> OwnedNames() =>
        EquipmentDatabase.GetBuiltInTemplates().Where(e => e.Id < EquipmentDatabase.ShopGeneratedStart).Select(e => e.Name)
            .Concat(ItemManager.ClassicTemplateNames)
            .Concat(NPCItemGenerator.WeaponTemplateNames)
            .Concat(NPCItemGenerator.ArmorTemplateNames)
            .Distinct().ToList();

    private static PlayerData Save(Character hero) =>
        (PlayerData)typeof(SaveSystem).GetMethod("SerializePlayer", F)!.Invoke(SaveSystem.Instance, new object[] { hero })!;

    private static Character Reload(Character hero)
    {
        var data = JsonSerializer.Deserialize<PlayerData>(JsonSerializer.Serialize(Save(hero)))!;
        var restore = typeof(GameEngine).GetMethod("RestorePlayerFromSaveData", F)!;
        try { return (Character)restore.Invoke(GameEngine.Instance, new object[] { data })!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    private static string RenderBackpack(Character hero)
    {
        var s = NewScreen();
        var inv = new InventorySystem(s.Term, hero);
        typeof(InventorySystem).GetMethod("DisplayBackpack", F)!.Invoke(inv, new object?[] { null });
        return s.Text;
    }

    private static string RenderEquipment(Character hero)
    {
        var s = NewScreen();
        var inv = new InventorySystem(s.Term, hero);
        typeof(InventorySystem).GetMethod("DisplayEquipmentOverview", F)!.Invoke(inv, Array.Empty<object>());
        return s.Text;
    }

    /// <summary>The classic pickup after a victory: the monster's weapon, taken with Y.</summary>
    private static async Task<(string Text, Item Picked)> PickUpMonsterWeapon(Character hero, long weapNr)
    {
        ItemManager.InitializeItems();
        var s = NewScreen("Y");
        var engine = new CombatEngine(s.Term);
        var monster = new Monster
        {
            Name = "Orc", Level = 5, HP = 0, MaxHP = 10, GrabWeap = true, WeapNr = weapNr,
            Weapon = ItemManager.GetClassicWeapon((int)weapNr)!.Name,
        };
        var result = new CombatResult { Player = hero };
        result.Monsters.Add(monster);
        int before = hero.Inventory.Count;
        await (Task)typeof(CombatEngine).GetMethod("OfferMonsterGearPickup", F)!.Invoke(engine, new object[] { result })!;
        hero.Inventory.Count.Should().Be(before + 1, "the weapon was taken");
        return (s.Text, hero.Inventory.Last());
    }

    // ---------- 1. the keys ----------

    [Fact]
    public void EveryOwnedName_HasItsKey_WithTheNameAsItsEnglish()
    {
        var names = OwnedNames();
        names.Should().HaveCountGreaterThan(500);
        foreach (var n in names)
        {
            ItemNames.KeyOf(n).Should().Be(Key(n), $"{n} shows through its key");
            Loc.GetIn("en", Key(n)).Should().Be(n, "the English text of the key is the stored name");
        }
    }

    // The longest English name in the tables ("Robes of the Grand Sorcerer"). No translation is longer, so a row
    // that fits with the stored English name fits with the shown one. One older loot key is longer and is listed.
    private const int LongestEnglishName = 27;
    private static readonly (string Lang, string Name)[] OlderLongerKeys = { ("fr", "Dragon Scale Shield") };

    private static bool OlderLonger(string lang, string name) => OlderLongerKeys.Contains((lang, name));

    [Theory]
    [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void EveryOwnedName_IsTranslated_AndNoLongerThanTheLongestEnglishName(string lang)
    {
        OwnedNames().Max(n => n.Length).Should().Be(LongestEnglishName);
        foreach (var n in OwnedNames())
        {
            Loc.HasIn(lang, Key(n)).Should().BeTrue($"{lang} has {Key(n)}");
            string shown = Loc.GetIn(lang, Key(n));
            shown.Length.Should().BeLessOrEqualTo(OlderLonger(lang, n) ? 30 : LongestEnglishName, $"{lang} {n} is \"{shown}\"");
            if (!(SameInLanguage.TryGetValue(n, out var same) && same.Contains(lang)))
                shown.Should().NotBe(n, $"{lang} translates {n}");
            shown.Should().NotContain("—").And.NotContain("–");
        }
    }

    [Theory]
    [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void TheQualityPrefixes_AreTranslatedTemplates(string lang)
    {
        foreach (var q in new[] { "quality", "masterwork", "rusted", "worn", "battered", "enchanted", "blessed" })
        {
            string en = Loc.GetIn("en", $"item.quality.{q}");
            string t = Loc.GetIn(lang, $"item.quality.{q}");
            en.Should().Contain("{0}");
            t.Should().Contain("{0}").And.NotBe(en);
        }
        // every prefix the NPC table stores is one the display maps
        var stored = (string[])typeof(NPCItemGenerator).GetField("QualityPrefixes", SNP)!.GetValue(null)!;
        ItemNames.MappedPrefixes.Should().BeEquivalentTo(stored.Where(p => p != "").Distinct());
    }

    // ---------- 2. the display ----------

    [Fact]
    public void Display_ReadsTheStoredName_InTheReadersLanguage()
    {
        string hu(string k) => Loc.GetIn("hu", k);
        InLang("hu", () => ItemNames.Display("Rusty Dagger")).Should().Be(hu("item.rusty_dagger"));
        InLang("hu", () => ItemNames.Display("Fine Long Sword")).Should().Be($"{hu("item.rarity.fine")} {hu("item.long_sword")}");
        InLang("hu", () => ItemNames.Display("Rusted Leather Vest")).Should().Be(L("hu", "item.quality.rusted", hu("item.leather_vest")));
        InLang("hu", () => ItemNames.Display("Leather Tunic")).Should().Be(hu("item.leather_tunic"));
        InLang("fr", () => ItemNames.Display("Cursed Ring")).Should().Be(Loc.GetIn("fr", "item.cursed_ring"), "a whole template name is read before a prefix");
        // what is not an English template underneath is shown as stored
        foreach (var stored in new[] { "Kiváló Hosszú Kard", "My Lucky Blade", "Purified Long Sword", "Long Swordfish", "" })
            InLang("hu", () => ItemNames.Display(stored)).Should().Be(stored);
        // English is the stored name, byte for byte
        foreach (var n in OwnedNames().Take(200))
            InLang("en", () => ItemNames.Display(n)).Should().Be(n);
    }

    [Theory]
    [InlineData("hu")] [InlineData("es")]
    public void AnEnchantedItem_ShowsItsBaseInTheReadersLanguage_WithItsEnchantPartsAsStored(string lang)
    {
        string baseShown = Loc.GetIn(lang, "item.studded_leather_cap");
        foreach (var tail in new[] { " +4 Dex", " (Blessed)", " +4 Dex (Frostbite)", " +2 +6 Str (Phoenix Fire)" })
        {
            var cap = OwnCopy("Studded Leather Cap");
            cap.Name += tail;
            InLang(lang, () => ItemNames.Display(cap)).Should().Be(baseShown + tail);
            cap.Name.Should().Be("Studded Leather Cap" + tail, "the stored name is untouched");
        }
        // and on the equipment screen
        var hero = Hero();
        var worn = OwnCopy("Studded Leather Cap");
        worn.Name += " +4 Dex (Blessed)";
        hero.EquippedItems[EquipmentSlot.Head] = worn.Id;
        string text = InLang(lang, () => RenderEquipment(hero));
        Capture($"data-items-enchanted-{lang}.txt", text);
        text.Should().Contain(baseShown + " +4 Dex (Blessed)").And.NotContain("Studded Leather Cap");
    }

    // ---------- 3. a Hungarian player sees Hungarian names: inventory, shop, loot ----------

    [Fact]
    public void Inventory_ShowsHungarianNames_AndStoresEnglish()
    {
        var hero = Hero();
        hero.Inventory.Add(hero.ConvertEquipmentToLegacyItem(OwnCopy("Iron Dagger")));
        hero.Inventory.Add(new Item { Name = "Fine Long Sword", Type = ObjType.Weapon, Attack = 12, Value = 100, IsIdentified = true });
        hero.Inventory.Add(new Item { Name = "Leather Tunic", Type = ObjType.Body, Armor = 3, Value = 100, IsIdentified = true });
        hero.EquippedItems[EquipmentSlot.MainHand] = OwnCopy("Rusty Sword").Id;
        hero.EquippedItems[EquipmentSlot.Body] = BuiltIn("Hardened Leather").Id;   // a built-in worn by its template id

        string bag = InLang("hu", () => RenderBackpack(hero));
        string worn = InLang("hu", () => RenderEquipment(hero));
        Capture("data-items-backpack-hu.txt", bag);
        Capture("data-items-equipment-hu.txt", worn);

        bag.Should().Contain(L("hu", "item.iron_dagger")).And.NotContain("Iron Dagger");
        bag.Should().Contain($"{L("hu", "item.rarity.fine")} {L("hu", "item.long_sword")}").And.NotContain("Fine Long Sword");
        bag.Should().Contain(L("hu", "item.leather_tunic")).And.NotContain("Leather Tunic");
        worn.Should().Contain(L("hu", "item.rusty_sword")).And.NotContain("Rusty Sword");
        worn.Should().Contain(L("hu", "item.hardened_leather")).And.NotContain("Hardened Leather");

        hero.Inventory.Select(i => i.Name).Should().Equal("Iron Dagger", "Fine Long Sword", "Leather Tunic");
        hero.GetEquipment(EquipmentSlot.MainHand)!.Name.Should().Be("Rusty Sword");
    }

    [Theory]
    [InlineData("hu")] [InlineData("it")]
    public async Task ArmorShop_ListsTheShopItemsInTheReadersLanguage(string lang)
    {
        var first = EquipmentDatabase.GetShopArmor(EquipmentSlot.Head).First();
        string text = await InLanguage(lang, async () =>
        {
            var shop = new ArmorShopLocation();
            var s = At(shop, Hero());
            typeof(ArmorShopLocation).GetMethod("ShowSlotItems", F)!.Invoke(shop, new object[] { EquipmentSlot.Head });
            await Task.CompletedTask;
            return s.Text;
        });
        Capture($"data-items-armor-shop-{lang}.txt", text);
        string shown = Loc.GetIn(lang, Key(first.Name));
        shown.Should().NotBe(first.Name);
        text.Should().Contain(shown);
        Rows(text).Should().NotContain(r => r.Contains(first.Name + " "), "the row shows the translated name");
        first.Name.Should().Be(first.Family, "the shop item keeps its English name");
    }

    [Theory]
    [InlineData("hu")] [InlineData("fr")]
    public async Task MonsterWeaponPickup_ShowsTheNameInTheReadersLanguage_AndStoresEnglish(string lang)
    {
        var hero = Hero();
        var (text, picked) = await InLanguage(lang, () => PickUpMonsterWeapon(hero, 6));
        Capture($"data-items-pickup-{lang}.txt", text);
        string shown = Loc.GetIn(lang, "item.long_sword");
        text.Should().Contain(L(lang, "combat.pickup_weapon", shown));
        text.Should().Contain(L(lang, "combat.picked_up", shown));
        text.Should().NotContain("Long Sword");
        picked.Name.Should().Be("Long Sword", "the picked-up item carries the English name");
    }

    // ---------- 4. stored names survive a save and reload in every language ----------

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public async Task StoredNames_StayEnglish_ThroughASaveAndReload(string lang)
    {
        var hero = Hero();
        await InLanguage(lang, async () =>
        {
            await PickUpMonsterWeapon(hero, 7);                                     // classic table: "Broad Sword"
            var npcItem = NPCItemGenerator.GenerateWeapon(CharacterClass.Warrior, 20); // NPC table, maybe prefixed
            hero.Inventory.Add(npcItem);
            hero.Inventory.Add(hero.ConvertEquipmentToLegacyItem(OwnCopy("Steel Dagger")));
            var worn = OwnCopy("Studded Leather Cap");
            worn.Name += " +4 Dex";
            hero.EquippedItems[EquipmentSlot.Head] = worn.Id;
            hero.EquippedItems[EquipmentSlot.MainHand] = OwnCopy("Knight's Blade").Id;
            return 0;
        });

        var npcName = hero.Inventory[1].Name;
        Regex.IsMatch(npcName, "^[A-Za-z' ]+$").Should().BeTrue($"an NPC item is stored in English, not \"{npcName}\"");
        var expected = new[] { "Broad Sword", npcName, "Steel Dagger" };
        hero.Inventory.Select(i => i.Name).Should().Equal(expected);

        var back = InLang(lang, () => Reload(hero));
        back.Inventory.Select(i => i.Name).Should().Equal(expected, $"the save keeps the English names ({lang})");
        back.GetEquipment(EquipmentSlot.Head)!.Name.Should().Be("Studded Leather Cap +4 Dex");
        back.GetEquipment(EquipmentSlot.MainHand)!.Name.Should().Be("Knight's Blade");

        // and the reloaded items show in the reader's language
        InLang(lang, () => ItemNames.Display(back.Inventory[0])).Should().Be(Loc.GetIn(lang, "item.broad_sword"));
        InLang(lang, () => ItemNames.Display(back.GetEquipment(EquipmentSlot.MainHand))).Should().Be(Loc.GetIn(lang, "item.knights_blade"));
        InLang(lang, () => ItemNames.Display(back.GetEquipment(EquipmentSlot.Head)))
            .Should().Be(Loc.GetIn(lang, "item.studded_leather_cap") + " +4 Dex");
    }

    // ---------- 5. the gear set resolver still reads a localized player's gear ----------

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void GearSetResolver_ReadsTheGearOfAPlayerInAnyLanguage(string lang)
    {
        // every built-in piece that counts toward a set, as a player in this language holds it after a reload
        var setPieces = EquipmentDatabase.GetBuiltInTemplates()
            .Where(e => e.Id < EquipmentDatabase.ShopGeneratedStart)
            .Select(e => (Template: e, Family: GearSetFamilyResolver.FamilyOf(e)))
            .Where(p => p.Family != null).ToList();
        setPieces.Should().NotBeEmpty();
        foreach (var (template, family) in setPieces)
        {
            var hero = Hero();
            Equipment copy = null!;
            InLang(lang, () => { copy = OwnCopy(template.Name); copy.Name += " +4 Dex"; hero.EquippedItems[template.Slot] = copy.Id; return 0; });
            var back = InLang(lang, () => Reload(hero)).GetEquipment(template.Slot)!;
            back.Name.Should().Be(template.Name + " +4 Dex");
            InLang(lang, () => GearSetFamilyResolver.FamilyOf(back)).Should().Be(family, $"{template.Name} in {lang}");
            InLang(lang, () => GearSetFamilyResolver.FamilyOf(hero.ConvertEquipmentToLegacyItem(back))).Should().Be(family);
        }
    }

    // ---------- 6. widths ----------

    private static Equipment LongestShown(string lang, IEnumerable<Equipment> pieces) =>
        pieces.OrderByDescending(e => Loc.GetIn(lang, Key(e.Name)).Length).ThenBy(e => e.Id).First();

    /// <summary>The backpack row and the equipment row of one piece with one stat (its power or armour),
    /// so the row is the name and the fixed parts around it, at a realistic high value.</summary>
    private static (List<string> Bag, List<string> Worn) RowsOf(string lang, Equipment piece)
    {
        var copy = new Equipment
        {
            Name = piece.Name, Slot = piece.Slot, Handedness = piece.Handedness, WeaponType = piece.WeaponType,
            WeaponPower = piece.WeaponPower, ArmorClass = piece.ArmorClass, ShieldBonus = piece.ShieldBonus,
            BlockChance = piece.BlockChance, WeightClass = piece.WeightClass, Rarity = piece.Rarity, Value = 1_300_000,
        };
        EquipmentDatabase.RegisterDynamic(copy);
        var hero = Hero();
        hero.EquippedItems[copy.Slot] = copy.Id;
        var item = hero.ConvertEquipmentToLegacyItem(copy);
        item.IsIdentified = true;
        hero.Inventory.Add(item);
        string bag = InLang(lang, () => RenderBackpack(hero));
        string worn = InLang(lang, () => RenderEquipment(hero));
        string shown = Loc.GetIn(lang, Key(piece.Name));
        var bagRows = Rows(bag).Where(r => r.Contains(shown)).ToList();
        var wornRows = Rows(worn).Where(r => r.Contains(shown)).ToList();
        bagRows.Should().NotBeEmpty($"the backpack shows {shown}");
        wornRows.Should().NotBeEmpty($"the equipment screen shows {shown}");
        return (bagRows, wornRows);
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void BackpackAndEquipmentRows_Fit_WithTheLongestNameOfEverySlot(string lang)
    {
        var builtIns = EquipmentDatabase.GetBuiltInTemplates().Where(e => e.Id < EquipmentDatabase.ShopGeneratedStart).ToList();
        var sb = new StringBuilder();
        foreach (var group in builtIns.GroupBy(e => e.Slot))
        {
            var longest = LongestShown(lang, group);
            var (bag, worn) = RowsOf(lang, longest);
            foreach (var row in bag.Concat(worn))
            {
                sb.AppendLine(row);
                row.Length.Should().BeLessOrEqualTo(MaxWidth, $"the row of {longest.Name} ({lang}): \"{row}\"");
            }
        }
        Capture($"data-items-width-{lang}.txt", sb.ToString());
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void ShopLists_KeepTheNameColumn_InEveryLanguage_OnEveryPage(string lang)
    {
        var armor = new ArmorShopLocation();
        foreach (var slot in new[] { EquipmentSlot.Head, EquipmentSlot.Body, EquipmentSlot.Arms, EquipmentSlot.Hands, EquipmentSlot.Legs,
                                     EquipmentSlot.Feet, EquipmentSlot.Waist, EquipmentSlot.Face, EquipmentSlot.Cloak })
        {
            int pages = (EquipmentDatabase.GetShopArmor(slot).Count + 14) / 15;
            for (int p = 0; p < pages; p++)
            {
                var s = At(armor, Hero());
                typeof(ArmorShopLocation).GetField("currentPage", F)!.SetValue(armor, p);
                string text = InLang(lang, () => { typeof(ArmorShopLocation).GetMethod("ShowSlotItems", F)!.Invoke(armor, new object[] { slot }); return s.Text; });
                if (slot == EquipmentSlot.Body && p == 0) Capture($"data-items-armor-shop-body-{lang}.txt", text);
                NameColumnHolds(text, $"armor shop {slot} page {p + 1} ({lang})");
            }
        }

        var weapons = new WeaponShopLocation();
        var categoryType = typeof(WeaponShopLocation).GetNestedType("WeaponCategory", BindingFlags.NonPublic)!;
        foreach (var category in Enum.GetValues(categoryType))
        {
            var list = (System.Collections.IList)typeof(WeaponShopLocation).GetMethod("GetShopItemsForCategory", F)!.Invoke(weapons, new[] { category })!;
            for (int p = 0; p < (list.Count + 14) / 15; p++)
            {
                var s = At(weapons, Hero());
                typeof(WeaponShopLocation).GetField("currentPage", F)!.SetValue(weapons, p);
                string text = InLang(lang, () => { typeof(WeaponShopLocation).GetMethod("ShowCategoryItems", F)!.Invoke(weapons, new[] { category }); return s.Text; });
                if (p == 0) Capture($"data-items-weapon-shop-{category}-{lang}.txt", text);
                NameColumnHolds(text, $"weapon shop {category} page {p + 1} ({lang})");
            }
        }
    }

    /// <summary>Every item row ("  4. name...") has its level column right after the 26-column name.</summary>
    private static void NameColumnHolds(string text, string screen)
    {
        var itemRows = Rows(text).Where(r => Regex.IsMatch(r, @"^[ \d]{3}\. ") && r.Length > 5 + 26).ToList();
        itemRows.Should().NotBeEmpty(screen);
        foreach (var row in itemRows)
            Regex.IsMatch(row.Substring(5 + 26), @"^\s*(\d+|--)\s").Should().BeTrue($"the name column of the {screen} holds: \"{row}\"");
    }

    [Fact]
    public void ShopColumn_CutsOnlyATranslationLongerThanTheColumn_NeverTheStoredName()
    {
        ItemNames.Fit("Short", "Short", 10).Should().Be("Short     ");
        ItemNames.Fit("Armatura del Guardiano della Foresta", "Forest Guardian Armor", 26).Should().Be("Armatura del Guardiano de.");
        ItemNames.Fit("Leather Cap +4 Dex (Frostbite) +6 Str", "Leather Cap +4 Dex (Frostbite) +6 Str", 30)
            .Should().Be("Leather Cap +4 Dex (Frostbite) +6 Str", "the stored name is never cut, so English rows are as they were");
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void CompanionWeaponRefusal_IsInTheReadersLanguage_AndFits(string lang)
    {
        foreach (var id in Enum.GetValues<CompanionId>())
        {
            var companion = new Character { Name2 = id.ToString(), IsCompanion = true, CompanionId = id, Level = 100, Class = CharacterClass.Warrior, Strength = 500 };
            var bow = BuiltIn("Rusty Dagger");   // Aldric and Mira refuse daggers
            string reason = "";
            bool ok = InLang(lang, () => bow.CanEquip(companion, out reason));
            if (ok) continue;
            if (!reason.StartsWith(companion.DisplayName)) continue;   // another rule refused first
            reason.Should().Be(L(lang, "ui.cannot_use_weapon_type", companion.DisplayName));
        }
        foreach (var name in Enum.GetNames<CompanionId>())
        {
            string reason = L(lang, "ui.cannot_use_weapon_type", name);
            L(lang, "team.cannot_use_item", name, reason).Length.Should().BeLessOrEqualTo(MaxWidth);
            L(lang, "home.cannot_use_item", name, reason).Length.Should().BeLessOrEqualTo(MaxWidth);
            ("  " + L(lang, "dungeon.cannot_use_item", name, reason)).Length.Should().BeLessOrEqualTo(MaxWidth);
        }
        if (lang != "en") L(lang, "ui.cannot_use_weapon_type", "X").Should().NotContain("can't use");
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public void ACursedBagItem_IsTaggedInTheReadersLanguage(string lang)
    {
        var item = new Item { Name = "Cursed Ring", Cursed = true, Durability = 100 };
        InLang(lang, () => item.GetDisplayName())
            .Should().Be($"{Loc.GetIn(lang, "item.cursed_ring")} ({Loc.GetIn(lang, "item.rarity.cursed")})");
        if (lang == "en") InLang(lang, () => item.GetDisplayName()).Should().Be("Cursed Ring (Cursed)", "English is as it was");
    }

    // ---------- 8. every message row that carries an item name ----------

    // GameConfig.MaxNameLength (30) characters, the longest name a player can have.
    private const string LongName = "Aranyszivu Hosszunevu Kalandor";

    /// <summary>The message rows the converted sites print, as (indent, key, arguments): I the item, N a
    /// 30-character player, NPC, owner or god name, S the longest slot name, G a large gold amount, # a small
    /// number, P a percentage, D the longest auction duration label.</summary>
    private static readonly (string Indent, string Key, string Args)[] ItemMessages =
    {
        ("", "armor_shop.autobuy_already_best", "SI"), ("", "armor_shop.autobuy_current", "I#"), ("", "armor_shop.autobuy_purchased", "I"),
        ("", "armor_shop.autobuy_upgrade", "I"), ("", "armor_shop.buy_prompt_name", "I"), ("", "armor_shop.couldnt_equip", "I"),
        ("", "base.auction_list_confirm", "IGDG"), ("", "base.auction_listed", "IGGD"), ("  ", "base.auction_push_listed", "NIGD"),
        ("", "combat.currently_equipped", "I"), ("", "combat.inventory_full_dropped", "I"), ("", "combat.loot_added_inventory", "I"),
        ("", "combat.loot_ally_picks_up", "NIP"), ("", "combat.loot_ally_upgrade_prompt", "NIP"), ("", "combat.loot_equipped_on_companion", "IN"),
        ("", "combat.loot_teammate_equips", "NI"), ("", "combat.loot_teammate_passes", "NI"), ("", "combat.loot_teammate_takes", "NI"),
        ("", "combat.other_equips", "NI"), ("  ", "combat.other_passes", "NI"), ("  ", "combat.other_takes", "NI"),
        ("", "combat.other_takes_to_inventory", "NI"), ("", "combat.picked_up", "I"), ("", "combat.pickup_armor", "I"),
        ("", "combat.pickup_weapon", "I"), ("", "dark_alley.bm_gear_purchased", "IG"), ("", "dungeon.cannot_equip_item", "I"),
        ("", "dungeon.equipped_item_self", "I"), ("  ", "dungeon.equipped_item", "NI"), ("  ", "dungeon.follower_cannot_be_equipped", "I"),
        ("  ", "dungeon.follower_unequipped", "I"), ("", "dungeon.took_item_from", "IN"), ("", "equip.equipped_in_slot", "IS"),
        ("", "equip.moved_to_inventory", "I"), ("", "equip.moved_to_offhand", "I"), ("", "equip.cannot_offhand_with_2h", "II"),
        ("", "healer.disintegrates", "I"), ("", "home.cursed_no_remove", "I"), ("", "home.equipped_item", "NI"), ("", "home.took_item", "IN"),
        ("", "home.chest_stored", "I##"), ("", "home.chest_retrieved", "I"), ("", "home.equip_used", "I"), ("", "home.equip_equipped", "I"),
        ("", "inn.cursed_cannot_remove", "I"), ("", "inn.equip_best_equipped", "SI"), ("", "inn.equip_best_upgraded", "SII"),
        ("", "inn.equipped_item", "NI"), ("", "inn.took_from", "IN"), ("", "inventory.cannot_equip", "I"), ("", "inventory.cursed_cant_drop", "I"),
        ("", "inventory.cursed_cant_unequip", "I"), ("", "inventory.dropped_item", "I"), ("", "inventory.equipped_item", "I"),
        ("", "inventory.equipped", "I"), ("", "inventory.unequipped_item", "I"), ("", "inventory.unequipped", "I"),
        ("", "magic_shop.curse_confirm", "IG"), ("", "magic_shop.curse_confirm_team", "NIG"), ("", "magic_shop.cursed_item_entry", "#IG"),
        ("", "magic_shop.cursed_team_entry", "#INSG"), ("", "magic_shop.cursed_worn_entry", "#ISG"), ("", "magic_shop.curse_success", "I"),
        ("", "magic_shop.curse_team_success", "NI"), ("", "magic_shop.curse_scene_1", "NI"), ("", "magic_shop.curse_team_scene_1", "NNI"),
        ("", "magic_shop.enchant_anvil", "NI"), ("", "magic_shop.enchant_result", "II"), ("", "magic_shop.identify_result", "I"),
        ("  ", "magic_shop.now_wearing", "I"), ("", "magic_shop.old_enchant_blessed", "I"), ("", "magic_shop.old_enchant_confirm", "IG"),
        ("", "magic_shop.old_enchant_flows", "I"), ("", "magic_shop.old_enchant_ocean", "I"), ("", "magic_shop.old_enchant_protect", "I"),
        ("", "magic_shop.remove_enchant_confirm", "I"), ("", "marketplace.news_npc_bought", "NIN"), ("", "marketplace.news_npc_listed", "NI"),
        ("", "marketplace.news_purchased", "NIN"), ("", "music_shop.buy_confirm", "I"), ("", "party_inv.taken", "IN"),
        ("", "shop.couldnt_equip", "I"), ("", "shop.cursed_item_healer", "I"), ("", "shop.cursed_warning", "I"),
        ("  ", "shop.purchased_equipped", "I"), ("  ", "shop.purchased_inventory", "I"), ("", "shop.purchased_inventory_alt", "I"),
        ("  ", "shop.sold_single", "IG"), ("", "street_encounter.bounty_hunter.victory_loot", "I"),
        ("", "street_encounter.bounty_hunter.victory_loot_dropped", "I"), ("", "team.cursed_cannot_remove", "I"),
        ("", "team.equip_item_gone", "I"), ("", "team.equipped_success", "NI"), ("", "team.took_item", "IN"),
        ("", "temple.sacrifice_refused_item", "IN"), ("", "weapon_shop.autobuy_already_best", "I#"), ("", "weapon_shop.autobuy_current", "I#"),
        ("", "weapon_shop.autobuy_purchased", "I"), ("", "weapon_shop.buy_prompt_name", "I"), ("", "weapon_shop.sell_main_hand", "I"),
        ("", "weapon_shop.sell_off_hand", "I"),
    };

    private static string RenderMessage(string lang, (string Indent, string Key, string Args) m, string item)
    {
        string slot = Enum.GetValues<EquipmentSlot>().Where(s => s != EquipmentSlot.None)
            .Select(s => InLang(lang, () => s.GetDisplayName())).OrderByDescending(s => s.Length).First();
        string duration = new[] { 12, 24, 48, 72 }.Select(h => BaseLocation.AuctionDurationLabel(h, lang)).OrderByDescending(s => s.Length).First();
        object[] args = m.Args.Select(c => (object)(c switch
        {
            'I' => item, 'N' => LongName, 'S' => slot, 'G' => "2,000,000,000", '#' => "999", 'P' => "999", 'D' => duration,
            _ => throw new ArgumentException(m.Args),
        })).ToArray();
        return m.Indent + L(lang, m.Key, args);
    }

    [Fact]
    public void EveryItemMessage_ThatFitsWithTheStoredName_FitsWithTheShownName_InEveryLanguage()
    {
        // Before this piece a row showed the stored English name; now it shows the translation. In each language a
        // row that fit with the longest English name must still fit with the longest shown name. A row too long
        // even with the English name is the template's own width, older than this piece, and is listed.
        LongName.Length.Should().Be(GameConfig.MaxNameLength);
        var names = OwnedNames();
        string longestEnglish = names.OrderByDescending(n => n.Length).First();
        var older = new List<string>();
        var tooWide = new List<string>();
        var sb = new StringBuilder();
        foreach (var m in ItemMessages)
        {
            Loc.HasIn("en", m.Key).Should().BeTrue(m.Key);
            foreach (var lang in AllLanguages)
            {
                string before = RenderMessage(lang, m, longestEnglish);
                if (before.Length > MaxWidth) { older.Add($"{lang} {m.Key} {before.Length}"); continue; }
                string longest = names.Where(n => !OlderLonger(lang, n)).Select(n => Loc.GetIn(lang, Key(n)))
                    .OrderByDescending(n => n.Length).First();
                string row = RenderMessage(lang, m, longest);
                sb.AppendLine($"{lang} {row.Length,3} {row}");
                if (row.Length > MaxWidth) tooWide.Add($"{m.Key} ({lang}, {row.Length}): \"{row}\"");
            }
        }
        Capture("data-items-messages.txt", sb + "\nover 79 with the English name already (not asserted):\n" + string.Join("\n", older));
        tooWide.Should().BeEmpty("the shown name never makes a fitting row too wide");
        older.Should().NotBeEmpty("the older overflows are listed, not hidden");
    }

    // ---------- 7. the wiki export ----------

    [Fact]
    public void WikiItems_KeepTheirEnglishName_AndAddEachLanguagesOwn()
    {
        var items = (System.Collections.IEnumerable)typeof(WikiDataExporter).GetMethod("Items", SNP)!.Invoke(null, null)!;
        var names = typeof(WikiDataExporter).GetMethod("Names", SNP)!;
        var templates = EquipmentDatabase.GetBuiltInTemplates().ToDictionary(e => e.Id);
        int count = 0;
        foreach (var entry in items)
        {
            int id = (int)entry.GetType().GetProperty("id")!.GetValue(entry)!;
            var name = (Dictionary<string, string>)entry.GetType().GetProperty("name")!.GetValue(entry)!;
            var template = templates[id];
            name["en"].Should().Be(template.Name, "the English export is the stored name");
            // the English export is what it was before: the name the exporter wrote without a key
            var englishOnly = (Dictionary<string, string>)names.Invoke(null, new object?[] { null, template.Name })!;
            englishOnly.Should().Equal(new Dictionary<string, string> { ["en"] = name["en"] });
            if (id < EquipmentDatabase.ShopGeneratedStart)
                foreach (var lang in OtherLanguages)
                    name[lang].Should().Be(Loc.GetIn(lang, Key(template.Name)));
            count++;
        }
        count.Should().Be(templates.Count);
    }
}
