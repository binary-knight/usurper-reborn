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
            await InLanguage(lang, async () =>
            {
                var s = stat > 0 ? Open(hero, "1", $"{tier}", $"{stat}", "Y") : Open(hero, "1", $"{tier}", "Y");
                await Run(s, "EnchantEquipment", hero);
                return 0;
            });
            string enchanted = hero.GetEquipment(EquipmentSlot.Head)!.Name;
            enchanted.Should().Be("Leather Cap" + tag, $"tier {tier} writes its English tag in {lang}");
            Family(enchanted).Should().Be("Leather Cap", "the resolver reads the enchanted name");

            await InLanguage(lang, async () => { await Remove(hero); return 0; });
            string stripped = hero.GetEquipment(EquipmentSlot.Head)!.Name;
            stripped.Should().Be("Leather Cap", $"removal strips{tag} in {lang}");
            Family(stripped).Should().Be("Leather Cap", "the resolver reads the stripped name");
        }
    }

    [Theory]
    [MemberData(nameof(EveryEnchant))]
    public async Task Removal_CleansATagAnItemAlreadyCarries(int tier, int stat, string tag)
    {
        _ = tier; _ = stat;
        // An item saved with this tag (an older removal left Phoenix Fire and Frostbite behind) and enchanted again.
        var hero = Wearing("Leather Cap" + tag, 1);
        await Remove(hero);
        hero.GetEquipment(EquipmentSlot.Head)!.Name.Should().Be("Leather Cap", $"removal strips{tag} from a stored name");
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
                v.Should().NotBeNullOrWhiteSpace().And.NotBe(key).And.NotContain("—").And.NotContain("–");
            }
            Loc.GetIn("hu", key).Should().NotBe(Loc.GetIn("en", key), $"{key} is translated");
        }
        // the first-letter fragments are gone
        foreach (var old in new[] { "sell", "identify", "healing_pots", "mana_pots", "dungeon_reset", "enchant", "curse_removal", "talk_to" })
            foreach (var lang in AllLanguages)
                Loc.GetIn(lang, "magic_shop.bbs_" + old).Should().Be("magic_shop.bbs_" + old, $"{lang} no longer holds the fragment key");
    }
}
