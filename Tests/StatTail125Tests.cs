using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.5: the shop, equipment, backpack and status rows. The layout is compacted first (shop columns one space
/// apart, short weight and handedness tags) and a row that still passes 79 columns wraps its stat tail: under
/// the bonus column in the shops, under the item name elsewhere. Rows that fit are one row. The class tags and
/// the weapon-type column are shown through keys; what is stored and matched stays the enum or the English name.
/// A BBS page of a shop list fits 23 lines and the prompt.
/// </summary>
[Collection("SharedGameSingletons")]
public class StatTail125Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int MaxWidth = 79;
    private static readonly string[] AllLanguages = { "en", "es", "fr", "hu", "it" };

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

    private static Screen NewScreen()
    {
        var s = new Screen();
        s.Term = new TerminalEmulator(new LineStream(Enumerable.Repeat("", 12), _ =>
        {
            s.Term.StreamWriterInternal?.Flush();
            s.Output.WriteByte((byte)'\n');
        }), s.Output);
        return s;
    }

    private static Screen At(BaseLocation location, Character hero)
    {
        var s = NewScreen();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(location, s.Term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(location, hero);
        return s;
    }

    private static T InLang<T>(string lang, Func<T> body)
    {
        var prev = GameConfig.Language;
        bool sr = GameConfig.ScreenReaderMode;
        try { GameConfig.Language = lang; GameConfig.ScreenReaderMode = false; return body(); }
        finally { GameConfig.Language = prev; GameConfig.ScreenReaderMode = sr; }
    }

    private static List<string> Rows(string text) => text.Replace("\r", "").Split('\n').ToList();

    private static Character Hero() => new()
    {
        Name1 = "tester", Name2 = "Tester", Class = CharacterClass.Warrior, Level = 100, HP = 5000, MaxHP = 5000,
        AI = CharacterAI.Human, Gold = 2_000_000_000, Strength = 500,
    };

    private static string Key(string english) => LootGenerator.TemplateLocKey(english);

    private static List<Equipment> BuiltIns() =>
        EquipmentDatabase.GetBuiltInTemplates().Where(e => e.Id < EquipmentDatabase.ShopGeneratedStart && e.Slot != EquipmentSlot.None).ToList();

    /// <summary>A copy of the piece with every stat at a wide value and the price at 9,999,999.</summary>
    private static Equipment Maxed(Equipment piece)
    {
        var c = piece.Clone();
        c.WeaponPower = piece.WeaponPower > 0 ? 999 : 0; c.ArmorClass = piece.ArmorClass > 0 ? 999 : 0;
        c.ShieldBonus = piece.ShieldBonus > 0 ? 99 : 0;
        c.StrengthBonus = 99; c.DexterityBonus = 99; c.ConstitutionBonus = 99; c.IntelligenceBonus = 99; c.WisdomBonus = 99;
        c.CharismaBonus = 99; c.AgilityBonus = 99; c.MaxHPBonus = 999; c.MaxManaBonus = 999; c.DefenceBonus = 99; c.StaminaBonus = 99;
        c.MagicResistance = 99; c.CriticalChanceBonus = 99; c.LifeSteal = 99; c.PoisonDamage = 99; c.Value = 9_999_999;
        return c;
    }

    private static Equipment LongestShown(string lang, IEnumerable<Equipment> pieces) =>
        pieces.OrderByDescending(e => Loc.GetIn(lang, Key(e.Name)).Length).ThenBy(e => e.Id).First();

    private static string Call(BaseLocation loc, Character hero, string method, params object?[] args)
    {
        var s = At(loc, hero);
        typeof(BaseLocation).GetMethods(F).First(m => m.Name == method && m.GetParameters().Length == args.Length).Invoke(loc, args);
        return s.Text;
    }

    private static string RenderInventory(Character hero, string method, params object?[] args)
    {
        var s = NewScreen();
        var inv = new InventorySystem(s.Term, hero);
        typeof(InventorySystem).GetMethod(method, F)!.Invoke(inv, args);
        return s.Text;
    }

    /// <summary>Every equipment, backpack and status screen row of one piece (worn and carried).</summary>
    private static Dictionary<string, string> EquipmentScreens(string lang, Equipment piece)
    {
        var copy = piece.Clone();
        EquipmentDatabase.RegisterDynamic(copy);
        var hero = Hero();
        hero.Class = CharacterClass.Magician;   // so a class-restricted piece shows its refusal note too
        hero.EquippedItems[copy.Slot] = copy.Id;
        var item = hero.ConvertEquipmentToLegacyItem(copy);
        item.IsIdentified = true;
        hero.Inventory.Add(item);
        var home = new HomeLocation();
        var list = new List<(Equipment item, bool isEquipped, EquipmentSlot? fromSlot, Item? source)> { (copy, true, copy.Slot, null) };
        return InLang(lang, () => new Dictionary<string, string>
        {
            ["backpack"] = RenderInventory(hero, "DisplayBackpack", new object?[] { null }),
            ["equipment"] = RenderInventory(hero, "DisplayEquipmentOverview"),
            ["equipment totals"] = RenderInventory(hero, "DisplayStatsSummary"),
            ["slot with stats"] = Call(home, hero, "DisplayEquipmentSlotWithStats", hero, copy.Slot, ""),
            ["item list"] = Call(home, hero, "DisplayEquipmentItemList", list, hero),
            ["status slot"] = StatusSlot(home, hero, copy.Slot),
            ["status totals"] = Call(home, hero, "DisplayEquipmentTotals"),
        });
    }

    /// <summary>The status screen's slot row: its label, then the item and stats, as ShowStatus writes it.</summary>
    private static string StatusSlot(BaseLocation home, Character hero, EquipmentSlot slot)
    {
        var s = At(home, hero);
        s.Term.Write(Loc.Get("base.slot_main_hand") + " ");
        typeof(BaseLocation).GetMethod("DisplayEquipmentSlot", F)!.Invoke(home, new object[] { slot });
        return s.Text;
    }

    // ---------- 1. the tail layout ----------

    [Fact]
    public void ATailThatFits_IsOneRow_Unchanged()
    {
        var segs = new (string?, string)[] { ("gray", " (WP:30 Dex:+8)"), ("darkgray", " [1H]") };
        var rows = UIHelper.TailRows(40, 20, segs);
        rows.Should().HaveCount(1);
        rows[0].Should().Equal(segs);
    }

    [Fact]
    public void ALongTail_WrapsUnderTheIndent_AndAWideWordMovesLeft()
    {
        var rows = UIHelper.TailRows(60, 48, new (string?, string)[] { ("green", "Con+30 Def+21 Str+31"), ("gray", " [Asn/Bar/Sha]"), ("cyan", " [Med]") });
        var text = rows.Select(r => string.Concat(r.Select(p => p.Text))).ToList();
        text.Should().HaveCountGreaterThan(1);
        (60 + text[0].Length).Should().BeLessOrEqualTo(MaxWidth);
        text.Skip(1).Should().OnlyContain(r => r.StartsWith(new string(' ', 48)) && r[48] != ' ' && r.Length <= MaxWidth);
        var wide = UIHelper.TailRows(70, 55, new (string?, string)[] { ("gray", " [Clr/Pal/War/Jst/Alc/Sha/Brd]") })
            .Select(r => string.Concat(r.Select(p => p.Text))).ToList();
        wide.Should().OnlyContain(r => r.Length <= MaxWidth);
    }

    // ---------- 2. the shops ----------

    private static IEnumerable<(string Shop, string What, int Page, string Header, string Text)> ShopPages(string lang, bool bbs = false)
    {
        bool compact = GameConfig.CompactMode;
        GameConfig.CompactMode = bbs;
        try
        {
            var armor = new ArmorShopLocation();
            foreach (var slot in new[] { EquipmentSlot.Head, EquipmentSlot.Body, EquipmentSlot.Arms, EquipmentSlot.Hands, EquipmentSlot.Legs,
                                         EquipmentSlot.Feet, EquipmentSlot.Waist, EquipmentSlot.Face, EquipmentSlot.Cloak })
            {
                for (int p = 0; ; p++)
                {
                    var s = At(armor, Hero());
                    typeof(ArmorShopLocation).GetField("currentSlotCategory", F)!.SetValue(armor, slot);
                    typeof(ArmorShopLocation).GetField("currentPage", F)!.SetValue(armor, p);
                    string text = InLang(lang, () => { s.Term.ClearScreen(); typeof(ArmorShopLocation).GetMethod("DisplayLocation", F)!.Invoke(armor, null); return s.Text; });
                    yield return ("armor", slot.ToString(), p, InLang(lang, () => Loc.Get("armor_shop.item_header")), text);
                    var starts = (List<int>)typeof(ArmorShopLocation).GetField("pageStarts", F)!.GetValue(armor)!;
                    if (p >= starts.Count - 1) break;
                }
            }
            var weapons = new WeaponShopLocation();
            var categoryType = typeof(WeaponShopLocation).GetNestedType("WeaponCategory", BindingFlags.NonPublic)!;
            foreach (var category in Enum.GetValues(categoryType))
            {
                for (int p = 0; ; p++)
                {
                    var s = At(weapons, Hero());
                    typeof(WeaponShopLocation).GetField("currentCategory", F)!.SetValue(weapons, category);
                    typeof(WeaponShopLocation).GetField("currentPage", F)!.SetValue(weapons, p);
                    string text = InLang(lang, () => { s.Term.ClearScreen(); typeof(WeaponShopLocation).GetMethod("DisplayLocation", F)!.Invoke(weapons, null); return s.Text; });
                    string header = InLang(lang, () => Loc.Get(category.ToString() == "Shields" ? "weapon_shop.shield_header" : "weapon_shop.weapon_header"));
                    yield return ("weapon", category.ToString()!, p, header, text);
                    var starts = (List<int>)typeof(WeaponShopLocation).GetField("pageStarts", F)!.GetValue(weapons)!;
                    if (p >= starts.Count - 1) break;
                }
            }
        }
        finally { GameConfig.CompactMode = compact; }
    }

    private static readonly Regex ItemRow = new(@"^[ \d]{3}\. ");

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void EveryShopRow_Fits79_WithItsColumnsUnderTheHeader(string lang)
    {
        int pages = 0;
        foreach (var (shop, what, page, header, text) in ShopPages(lang))
        {
            pages++;
            var rows = Rows(text);
            string where = $"{shop} {what} page {page + 1} ({lang})";
            // the box header is the 80-column chrome of every screen (UIHelper.TotalWidth), not a list row
            rows.Where(r => !"╔║╚".Contains(r.Length > 0 ? r[0] : ' ')).Should().OnlyContain(r => r.Length <= MaxWidth, where);
            string head = rows.First(r => r.TrimStart().StartsWith("#"));
            var words = header.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // each column's header word ends where its values end (right aligned) or starts where they start (left)
            var cells = Regex.Matches(head, @"\S+").Cast<Match>().ToList();
            cells.Select(m => m.Value).Should().Equal(words, where);
            int bonusColumn = cells[^1].Index;
            foreach (var row in rows.Where(r => ItemRow.IsMatch(r)))
            {
                row.Length.Should().BeGreaterThan(5 + 26, where);
                for (int c = 2; c < cells.Count - 1; c++)
                {
                    var cell = cells[c];
                    bool left = shop == "weapon" && what != "Shields" && c == 4;
                    int edge = left ? cell.Index : cell.Index + cell.Length - 1;
                    row[edge].Should().NotBe(' ', $"{where}: \"{row}\" under \"{cell.Value}\"");
                    (edge + 1 >= row.Length || row[left ? edge - 1 : edge + 1] == ' ').Should().BeTrue($"{where}: \"{row}\"");
                }
                if (row.Length > bonusColumn) row[bonusColumn - 1].Should().Be(' ', $"{where}: \"{row}\"");
            }
            // a wrapped tail continues under the bonus column (or left of it for one wide tag)
            foreach (var row in rows.Where((r, i) => i > 0 && ItemRow.IsMatch(rows[i - 1]) && r.Length > 0 && r.StartsWith("     ") && r.Trim().Length > 0))
                (row.Length - row.TrimStart().Length).Should().BeLessOrEqualTo(bonusColumn, where);
        }
        pages.Should().BeGreaterThan(100);
    }

    [Fact]
    public void AShopTailThatDoesNotFit_WrapsUnderTheBonusColumn()
    {
        var page = ShopPages("en").First(p => p.Shop == "armor" && p.What == "Body" && Rows(p.Text).Any(r => r.StartsWith(new string(' ', 40))));
        var rows = Rows(page.Text);
        int i = rows.FindIndex(r => r.StartsWith(new string(' ', 40)) && r.Trim().Length > 0);
        ItemRow.IsMatch(rows[i - 1]).Should().BeTrue();
        string head = rows.First(r => r.TrimStart().StartsWith("#"));
        int bonusColumn = head.LastIndexOf(' ') + 1;
        (rows[i].Length - rows[i].TrimStart().Length).Should().Be(bonusColumn, $"\"{rows[i]}\" under the bonus column of \"{head}\"");
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")] [InlineData("fr")]
    public void ABbsShopPage_FitsTwentyThreeLinesAndThePrompt_AndListsEveryItemOnce(string lang)
    {
        var seen = new Dictionary<string, int>();
        int worst = 0;
        foreach (var (shop, what, page, _, text) in ShopPages(lang, bbs: true))
        {
            var rows = Rows(text.TrimEnd('\n'));
            worst = Math.Max(worst, rows.Count);
            rows.Count.Should().BeLessOrEqualTo(22, $"{shop} {what} page {page + 1} ({lang}) with the prompt fits 23 lines:\n{text}");
            string key = $"{shop} {what}";
            seen[key] = seen.GetValueOrDefault(key) + rows.Count(r => ItemRow.IsMatch(r));
        }
        foreach (var slot in new[] { EquipmentSlot.Head, EquipmentSlot.Body, EquipmentSlot.Cloak })
            seen[$"armor {slot}"].Should().Be(EquipmentDatabase.GetShopArmor(slot).Count, $"every {slot} item is on a page once");
        worst.Should().BeGreaterThan(15);
    }

    [Fact]
    public void OffBbs_AShopPage_StillHoldsFifteenItems()
    {
        var page = ShopPages("en").First(p => p.Shop == "weapon" && p.What == "OneHanded");
        Rows(page.Text).Count(r => ItemRow.IsMatch(r)).Should().Be(15);
    }

    [Fact]
    public void PageStarts_FillByLinesOnBbs_AndByFifteenOtherwise()
    {
        var lines = Enumerable.Repeat(1, 20).Concat(Enumerable.Repeat(2, 10)).ToList();
        BaseLocation.PageStarts(lines, 15, null).Should().Equal(0, 15);
        BaseLocation.PageStarts(lines, 15, 10).Should().Equal(0, 10, 20, 25);
        BaseLocation.PageStarts(new List<int> { 12 }, 15, 10).Should().Equal(0);
    }

    // ---------- 3. equipment, backpack and status ----------

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void EveryEquipmentBackpackAndStatusRow_Fits79_AtTheLongestNameAndWidestValues(string lang)
    {
        var pieces = BuiltIns();
        var widest = pieces.GroupBy(e => e.Slot).Select(g => Maxed(LongestShown(lang, g)));
        var tooWide = new List<string>();
        foreach (var piece in pieces.Concat(widest))
            foreach (var (screen, text) in EquipmentScreens(lang, piece))
                foreach (var row in Rows(text).Where(r => r.Length > MaxWidth))
                    tooWide.Add($"{screen} {piece.Name} ({lang}) {row.Length}: {row}");
        tooWide.Should().BeEmpty();
    }

    [Fact]
    public void AnEquipmentTailThatDoesNotFit_WrapsUnderTheItemName()
    {
        var piece = Maxed(LongestShown("en", BuiltIns().Where(e => e.Slot == EquipmentSlot.MainHand)));
        string text = EquipmentScreens("en", piece)["slot with stats"];
        var rows = Rows(text);
        int i = rows.FindIndex(r => r.Contains(Loc.GetIn("en", Key(piece.Name))));
        int nameColumn = rows[i].IndexOf(Loc.GetIn("en", Key(piece.Name)));
        rows[i].Length.Should().BeLessOrEqualTo(MaxWidth);
        (rows[i + 1].Length - rows[i + 1].TrimStart().Length).Should().Be(nameColumn, $"\"{rows[i + 1]}\" under the name in \"{rows[i]}\"");
    }

    [Fact]
    public void ScreenReaderMode_KeepsTheTailOnOneLine()
    {
        var piece = Maxed(LongestShown("en", BuiltIns().Where(e => e.Slot == EquipmentSlot.MainHand)));
        var copy = piece.Clone();
        EquipmentDatabase.RegisterDynamic(copy);
        var hero = Hero();
        hero.EquippedItems[copy.Slot] = copy.Id;
        bool sr = GameConfig.ScreenReaderMode;
        try
        {
            GameConfig.ScreenReaderMode = true;
            string text = RenderInventory(hero, "DisplayEquipmentOverview");
            Rows(text).Count(r => r.Contains(Loc.GetIn("en", Key(piece.Name))) && r.Contains("[One-Handed]")).Should().Be(1,
                "screen reader mode keeps the row on one line and reads the whole word");
            ArmorWeightClass.Heavy.ShortTag().Should().Be("Heavy");
        }
        finally { GameConfig.ScreenReaderMode = sr; }
    }

    // ---------- 4. keyed tags; stored and matched values stay ids or English ----------

    [Theory]
    [InlineData("en", "War/Pal", "Greatsword")] [InlineData("hu", "Har/Pal", "Pallos")] [InlineData("es", "Gue/Pal", "Mandoble")]
    public void ClassTagsAndWeaponTypes_AreShownThroughKeys(string lang, string tag, string type)
    {
        var item = new Equipment { Name = "Test Blade", Slot = EquipmentSlot.MainHand, WeaponType = WeaponType.Greatsword,
            ClassRestrictions = new List<CharacterClass> { CharacterClass.Warrior, CharacterClass.Paladin } };
        InLang(lang, () => BaseLocation.ClassRestrictionTag(item)).Should().Be(tag);
        InLang(lang, () => BaseLocation.WeaponTypeLabel(item.WeaponType)).Should().Be(type);
        item.ClassRestrictions.Should().Equal(CharacterClass.Warrior, CharacterClass.Paladin);
        item.WeaponType.Should().Be(WeaponType.Greatsword);
        foreach (var l in AllLanguages)
        {
            foreach (var c in Enum.GetValues<CharacterClass>())
                Loc.HasIn(l, $"class_short.{c.ToString().ToLowerInvariant()}").Should().BeTrue($"{l} {c}");
            foreach (var t in Enum.GetValues<WeaponType>().Where(t => t != WeaponType.None))
                Loc.HasIn(l, $"weapon_type.{t.ToString().ToLowerInvariant()}").Should().BeTrue($"{l} {t}");
        }
    }

    [Theory]
    [InlineData("hu")] [InlineData("es")]
    public void TheBackpackHandednessTag_IsMatchedFromTheStoredEnglishName(string lang)
    {
        var greatsword = BuiltIns().First(e => e.Name.Contains("Greatsword") && !Loc.GetIn(lang, Key(e.Name)).Contains("Greatsword")).Clone();
        EquipmentDatabase.RegisterDynamic(greatsword);
        var hero = Hero();
        var item = hero.ConvertEquipmentToLegacyItem(greatsword);
        item.IsIdentified = true;
        hero.Inventory.Add(item);
        string shown = Loc.GetIn(lang, Key(greatsword.Name));
        shown.Should().NotContain("Greatsword", "the shown name would not match the two-handed pattern");
        item.Name.Should().Be(greatsword.Name, "the carried item keeps its English name");
        string text = InLang(lang, () => RenderInventory(hero, "DisplayBackpack", new object?[] { null }));
        Rows(text).Single(r => r.Contains(shown)).Should().Contain($"[{Loc.GetIn(lang, "equip.class_short.two_handed")}]");
    }

    [Theory]
    [InlineData("en", "Lgt")] [InlineData("hu", "Kön")] [InlineData("fr", "Lég")]
    public void WeightTags_AreShortAndKeyed(string lang, string light)
    {
        InLang(lang, () => ArmorWeightClass.Light.ShortTag()).Should().Be(light);
        InLang(lang, () => ArmorWeightClass.None.ShortTag()).Should().BeEmpty();
    }
}
