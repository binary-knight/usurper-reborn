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
using UsurperReborn.Tests.Localization;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.5: the Magic Shop in the player's language. The menu, the enchant list, the enchant menu and its confirm,
/// the curse, removal, love spell, dark arts and scrying screens, and the Electron menu labels. Enchant tier, spell
/// and trait names are keys resolved when shown. What an enchant writes into the item's stored name (" +6 Dex",
/// " (Blessed)", "Purified ") stays English in every language, so the readers that parse it still match. Every
/// changed row fits 79 columns in English and Hungarian with a 30-character name and the longest names.
/// </summary>
[Collection("SharedGameSingletons")]
public class TownMagic125Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags FS = BindingFlags.NonPublic | BindingFlags.Static;
    private const int MaxWidth = 79;

    // GameConfig.MaxNameLength (30) characters, the longest name a player or NPC can have.
    private const string LongName = "Aranyszivu Hosszunevu Kalandor";
    // The longest name an enchant leaves on an item: a suffix is added only while the name stays under 40.
    private const string LongItem = "Abyssal Studded Leather Cap of Fortitu";

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
        // Input is not echoed here; a terminal shows the typed line and its Enter, so each read ends the prompt's row.
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

    private static string L(string lang, string key, params object[] args) => Loc.GetIn(lang, key, args);

    private static List<string> Rows(string text) => text.Replace("\r", "").Split('\n').ToList();

    private static void Capture(string name, string text)
    {
        var dir = Environment.GetEnvironmentVariable("USURPER_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, name), text);
    }

    // A framed box (WriteBoxHeader) is drawn exactly 80 columns wide; every other row fits in 79.
    private static void EveryRowFits(string text, string screen)
    {
        foreach (var row in Rows(text))
        {
            if (row.Length > 0 && "╔║╚".IndexOf(row[0]) >= 0)
                row.Length.Should().Be(MaxWidth + 1, $"the {screen} box keeps its width: \"{row}\"");
            else
                row.Length.Should().BeLessOrEqualTo(MaxWidth, $"every row of the {screen} fits in {MaxWidth} columns: \"{row}\"");
        }
    }

    /// <summary>The Hungarian screen holds none of the English text of these keys (each literal piece of the
    /// English value of 5 letters or more, unless the Hungarian value has it too).</summary>
    private static void NoEnglishLeft(string huText, IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            string en = Loc.GetIn("en", key), hu = Loc.GetIn("hu", key);
            foreach (var piece in Regex.Split(en, @"\{\d+\}").Select(p => p.Trim()).Where(p => p.Count(char.IsLetter) >= 5))
                if (!hu.Contains(piece))
                    huText.Should().NotContain(piece, $"{key} is shown in Hungarian");
        }
    }

    private static Character Hero(int level = 100) => new()
    {
        Name1 = "tester", Name2 = LongName, Class = CharacterClass.Magician, Race = CharacterRace.Human, Level = level,
        HP = 5000, MaxHP = 5000, BaseMaxHP = 5000, Mana = 5000, MaxMana = 5000, Intelligence = 200,
        AI = CharacterAI.Human, Gold = 9_000_000_000, AutoEquipDisabled = true,
    };

    /// <summary>A worn item with every stat the enchant list shows, so the stats row is as long as it gets.</summary>
    private static int Worn(string name, EquipmentSlot slot, int enchants = 0)
    {
        var e = new Equipment
        {
            Name = name, Slot = slot, WeaponPower = slot == EquipmentSlot.MainHand ? 999 : 0, ArmorClass = 999,
            StrengthBonus = 99, DexterityBonus = 99, AgilityBonus = 99, ConstitutionBonus = 99, IntelligenceBonus = 99,
            WisdomBonus = 99, CharismaBonus = 99, DefenceBonus = 99, MaxHPBonus = 9999, MaxManaBonus = 9999, StaminaBonus = 99,
            MinLevel = 1, Value = 1000,
        };
        for (int i = 0; i < enchants; i++) e.IncrementEnchantmentCount();
        return EquipmentDatabase.RegisterDynamic(e);
    }

    private static readonly EquipmentSlot[] AllSlots =
    {
        EquipmentSlot.MainHand, EquipmentSlot.OffHand, EquipmentSlot.Head, EquipmentSlot.Body, EquipmentSlot.Arms,
        EquipmentSlot.Hands, EquipmentSlot.Legs, EquipmentSlot.Feet, EquipmentSlot.Waist, EquipmentSlot.Face,
        EquipmentSlot.Cloak, EquipmentSlot.Neck, EquipmentSlot.LFinger, EquipmentSlot.RFinger,
    };

    private static string LongestItemName() =>
        EquipmentDatabase.GetAll().Select(e => e.Name).Append(LongItem).OrderByDescending(n => n.Length).First();

    private static T WithNpcs<T>(IEnumerable<NPC> npcs, Func<T> body)
    {
        var list = NPCSpawnSystem.Instance.ActiveNPCs;
        var added = npcs.ToList();
        list.AddRange(added);
        try { return body(); }
        finally { foreach (var n in added) list.Remove(n); }
    }

    private static NPC Npc(string name, CharacterClass cls) => new()
    {
        Name1 = name, Name2 = name, ID = "magic-125-" + name.GetHashCode(), Level = 100, Class = cls, HP = 500, MaxHP = 500,
        AI = CharacterAI.Computer,
    };

    private static CharacterClass LongestClass(string lang)
    {
        var prev = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            return Enum.GetValues<CharacterClass>().OrderByDescending(c => GameConfig.GetLocalizedClassName(c).Length).First();
        }
        finally { GameConfig.Language = prev; }
    }

    [Fact]
    public void LongName_IsTheLongestPlayerName_AndLongItem_TheLongestEnchantedName()
    {
        LongName.Length.Should().Be(GameConfig.MaxNameLength);
        LongItem.Length.Should().Be(38, "a suffix of at least one character still leaves the name under 40");
    }

    // ---------- 1. the shop menu ----------

    private static readonly string[] MenuKeys =
    {
        "magic_shop.menu_sec_shopping", "magic_shop.menu_sec_enchanting", "magic_shop.menu_sec_potions", "magic_shop.menu_sec_arcane",
        "magic_shop.rings", "magic_shop.necklaces", "magic_shop.sell", "magic_shop.identify", "magic_shop.enchant",
        "magic_shop.remove_enchant", "magic_shop.menu_curse_removal", "magic_shop.healing_potions", "magic_shop.mana_potions",
        "magic_shop.reset_scroll", "magic_shop.love_spells", "magic_shop.dark_arts", "magic_shop.study_spells",
        "magic_shop.menu_scrying", "shop.return",
    };

    private static Task<string> Menu(string lang) => InLanguage(lang, () =>
    {
        var s = Open(Hero());
        typeof(MagicShopLocation).GetMethod("DisplayMagicShopMenu", F)!.Invoke(s.Location, new object[] { Hero() });
        return Task.FromResult(s.Text);
    });

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task Menu_Fits_AndShowsEveryLabel(string lang)
    {
        string shown = await Menu(lang);
        Capture($"town-magic-menu-{lang}.txt", shown);
        EveryRowFits(shown, $"{lang} magic shop menu");
        shown.Should().Contain(InLang(lang, () => MagicShopLocation.MenuSectionRow("magic_shop.menu_sec_shopping", "magic_shop.menu_sec_enchanting")));
        if (lang == "hu")
        {
            NoEnglishLeft(shown, MenuKeys);
            shown.Should().Contain($"[1] {L("hu", "magic_shop.rings")}").And.Contain($"[E] {L("hu", "magic_shop.enchant")}")
                .And.Contain($"[R] {L("hu", "shop.return")}");
        }
    }

    [Fact]
    public async Task Menu_English_IsUnchanged()
    {
        string shown = await Menu("en");
        foreach (var row in new[]
        {
            "  ═══ Shopping ═══                      ═══ Enchanting ═══",
            "  [1] Rings                             [E]nchant Equipment",
            "  [2] Necklaces                         [W] Remove Enchantment",
            "  [S]ell Accessories                    [C]urse Removal",
            "  [I]dentify Item",
            "  ═══ Potions & Scrolls ═══             ═══ Arcane Arts ═══",
            "  [H]ealing Potions                     [V] Love Spells",
            "  [M]ana Potions                        [K] Dark Arts",
            "  [D]ungeon Reset Scroll                [Y] Study Spells",
            "                                          [G] Scrying (NPC Info)",
        })
            Rows(shown).Should().Contain(row);
        shown.Should().Contain($"  [T]alk to {MagicShopLocation.GetOwnerName()}").And.Contain("[R]eturn to street");
    }

    private static T InLang<T>(string lang, Func<T> body) => InLanguage(lang, () => Task.FromResult(body())).GetAwaiter().GetResult();

    // ---------- 2. the enchant list, the enchant menu and the confirm ----------

    private static readonly string[] EnchantKeys =
    {
        "magic_shop.enchanting_title", "magic_shop.col_slot", "magic_shop.col_tier", "magic_shop.col_effect",
        "magic_shop.slot_weapon", "magic_shop.slot_bag", "magic_shop.tier_divine_blessing", "magic_shop.tier_oceans_touch",
        "magic_shop.tier_lifedrinker", "magic_shop.tier_godforged", "magic_shop.tier_phoenix_fire", "magic_shop.tier_frostbite",
        "magic_shop.tier_desc_one_stat", "magic_shop.tier_desc_all_stats", "magic_shop.tier_desc_ward", "magic_shop.tier_desc_predator",
        "magic_shop.tier_desc_phoenix", "magic_shop.tier_desc_frostbite", "magic_shop.tier_desc_lifedrinker", "magic_shop.req_awakening",
        "magic_shop.enchant_confirm_line", "magic_shop.materials_line", "magic_shop.enchant_risk_1", "magic_shop.enchant_risk_2",
        "magic_shop.enchant_risk_3", "magic_shop.enchant_risk_4", "ui.weapon_power", "combat.status_defence_label", "ui.cancel",
    };

    /// <summary>The enchant flow up to the confirm, declined: every slot worn with a long name and every stat, a bag
    /// item, item 1 (three enchants already, so the risk warning shows), the tier given, stat 3 if asked.</summary>
    private static Task<string> Enchant(string lang, int level, int tier, string name) => InLanguage(lang, async () =>
    {
        var hero = Hero(level);
        foreach (var slot in AllSlots) hero.EquippedItems[slot] = Worn(name, slot, slot == EquipmentSlot.MainHand ? 3 : 0);
        hero.Inventory.Add(new Item { Name = name, Type = ObjType.Body, Armor = 50, Value = 100, IsIdentified = true });
        foreach (var m in GameConfig.CraftingMaterials) hero.AddMaterial(m.Id, 5);
        var s = Open(hero, "1", $"{tier}", "3", "N");
        await Run(s, "EnchantEquipment", hero);
        return s.Text;
    });

    [Theory]
    [InlineData("en", 100, 5)] [InlineData("hu", 100, 5)]
    [InlineData("en", 100, 13)] [InlineData("hu", 100, 13)]
    [InlineData("en", 1, 2)] [InlineData("hu", 1, 2)]
    [InlineData("en", 100, 2)] [InlineData("hu", 100, 2)]
    public async Task EnchantScreens_Fit_WithTheLongestNames(string lang, int level, int tier)
    {
        foreach (var name in new[] { LongItem, LongestItemName() })
        {
            string shown = await Enchant(lang, level, tier, name);
            Capture($"town-magic-enchant-{lang}-{level}-{tier}.txt", shown);
            EveryRowFits(shown, $"{lang} enchant screens (level {level}, tier {tier})");
        }
    }

    [Fact]
    public async Task EnchantScreens_Hungarian_HaveNoEnglishLeft()
    {
        string all = await Enchant("hu", 100, 13, LongItem) + await Enchant("hu", 100, 5, LongItem)
                     + await Enchant("hu", 1, 2, LongItem) + await Enchant("hu", 100, 2, LongItem);
        NoEnglishLeft(all, EnchantKeys);
        for (int i = 0; i < 14; i++)
            all.Should().Contain(InLang("hu", () => MagicShopLocation.TierName(i)));
        all.Should().Contain(L("hu", "magic_shop.req_level", 10)).And.Contain(L("hu", "magic_shop.req_awakening", 2).Split(' ')[0]);
        all.Should().Contain(L("hu", "ui.stat_dexterity")).And.Contain(L("hu", "magic_shop.slot_bag"));
    }

    [Fact]
    public async Task EnchantMenu_English_KeepsItsWords()
    {
        string shown = await Enchant("en", 100, 5, "Steel Helm");
        foreach (var w in new[] { "Divine Blessing", "+3 to all stats", "Ocean's Touch", "+30 mana, +4 wisdom", "+20 magic resist, +2 defence",
                     "Phoenix Fire", "+20 power + chance to slow enemies", "Weapon  ", "L.Ring", "Bag", "Enchanting: Steel Helm",
                     "  Enchant Steel Helm with Divine Blessing (+3 to all stats)", "  WARNING: this item already carries 3 enchantments.",
                     "  On failure: gold and materials are consumed, AND one of the", "  [0] Cancel" })
            shown.Should().Contain(w);
        string stat = await Enchant("en", 100, 2, "Steel Helm");
        foreach (var w in new[] { "  [1] Weapon Power", "  [4] Defence", "  [6] Armor Power", "  [11] Stamina", "  Enchant Steel Helm with Standard (+4 Dexterity)" })
            stat.Should().Contain(w);
    }

    // ---------- 3. the stored name: English whatever the language ----------

    private static PlayerData Save(Character hero) =>
        (PlayerData)typeof(SaveSystem).GetMethod("SerializePlayer", F)!.Invoke(SaveSystem.Instance, new object[] { hero })!;

    /// <summary>Enchants a worn "Leather Cap" in the given language (tier, stat), confirms, and returns the hero.</summary>
    private static Task<Character> EnchantCap(string lang, int tier, int stat) => InLanguage(lang, async () =>
    {
        var hero = Hero();
        hero.EquippedItems[EquipmentSlot.Head] = EquipmentDatabase.RegisterDynamic(new Equipment
        {
            Name = "Leather Cap", Slot = EquipmentSlot.Head, ArmorClass = 3, MinLevel = 1, Value = 100,
        });
        foreach (var m in GameConfig.CraftingMaterials) hero.AddMaterial(m.Id, 5);
        var s = stat > 0 ? Open(hero, "1", $"{tier}", $"{stat}", "Y") : Open(hero, "1", $"{tier}", "Y");
        await Run(s, "EnchantEquipment", hero);
        return hero;
    });

    [Theory]
    [InlineData("en")] [InlineData("hu")] [InlineData("fr")]
    public async Task EnchantedName_IsStoredInEnglish_AndSurvivesTheSave(string lang)
    {
        var dex = await EnchantCap(lang, 2, 3);
        var blessed = await EnchantCap(lang, 5, 0);
        var frost = await EnchantCap(lang, 14, 0);
        foreach (var (hero, expected) in new[] { (dex, "Leather Cap +4 Dex"), (blessed, "Leather Cap (Blessed)"), (frost, "Leather Cap (Frostbite)") })
        {
            var cap = hero.GetEquipment(EquipmentSlot.Head)!;
            cap.Name.Should().Be(expected, $"the enchant writes the English form in {lang}");
            var data = Save(hero);
            string json = JsonSerializer.Serialize(data);
            var back = JsonSerializer.Deserialize<PlayerData>(json)!;
            back.DynamicEquipment.Should().Contain(d => d.Name == expected, "the save keeps the stored form");
            GearSetFamilyResolver.FamilyOf(null, expected, GearSetSlotKind.Head).Should().Be("Leather Cap", "the gear set resolver reads it");
        }
    }

    [Fact]
    public async Task RemoveEnchantment_InHungarian_StripsTheStoredStatCode()
    {
        var hero = await EnchantCap("hu", 2, 3);
        hero.GetEquipment(EquipmentSlot.Head)!.Name.Should().Be("Leather Cap +4 Dex");
        string shown = await InLanguage("hu", async () =>
        {
            var s = Open(hero, "1", "Y");
            await Run(s, "RemoveEnchantment", hero);
            return s.Text;
        });
        hero.GetEquipment(EquipmentSlot.Head)!.Name.Should().Be("Leather Cap", "RemoveEnchantment strips the English code by pattern");
        shown.Should().Contain(L("hu", "magic_shop.remove_restored", "Leather Cap"));
        EveryRowFits(shown, "hu enchant removal");
    }

    [Fact]
    public void StatCodes_AreTheEnglishThreeLetterForms_TheResolverAndTheStripRead()
    {
        var codes = (string[])typeof(MagicShopLocation).GetField("StatNames", FS)!.GetValue(null)!;
        codes.Should().Equal("Wea", "Str", "Dex", "Def", "Wis", "Arm", "Con", "Int", "Cha", "Agi", "Sta");
        foreach (var lang in AllLanguages)
            for (int stat = 1; stat <= codes.Length; stat++)
            {
                string suffix = InLang(lang, () => MagicShopLocation.StatSuffix(6, stat));
                suffix.Should().Be($" +6 {codes[stat - 1]}", $"the stored suffix is the same in {lang}");
                Regex.Replace("Leather Cap" + suffix, @"\s\+\d+\s\w{3}", "").Should().Be("Leather Cap");
            }
    }

    // ---------- 4. curse removal ----------

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task CurseList_Fits_AndIsInThePlayersLanguage(string lang)
    {
        var hero = Hero();
        int id = EquipmentDatabase.RegisterDynamic(new Equipment
        {
            Name = LongItem, Slot = EquipmentSlot.Body, ArmorClass = 9, StrengthBonus = -99, DefenceBonus = -99, DexterityBonus = -99,
            WisdomBonus = -99, IsCursed = true, MinLevel = 1, Value = 999_999,
        });
        hero.EquippedItems[EquipmentSlot.Body] = id;
        string shown = await InLanguage(lang, async () =>
        {
            var s = Open(hero, "0");
            await Run(s, "RemoveCurse", hero);
            return s.Text;
        });
        Capture($"town-magic-curse-{lang}.txt", shown);
        EveryRowFits(shown, $"{lang} curse list");
        shown.Should().Contain(L(lang, "magic_shop.cursed_worn_list"));
        shown.Should().Contain(L(lang, "magic_shop.curse_effect", $"{L(lang, "ui.stat_str")}-99, {L(lang, "ui.stat_def")}-99, {L(lang, "ui.stat_dex")}-99, {L(lang, "ui.stat_wis")}-99"));
        if (lang == "en") shown.Should().Contain($"(your {GameConfig.GetLocalizedSlotName(EquipmentSlot.Body)}) -- ");
        else NoEnglishLeft(shown, new[] { "magic_shop.cursed_worn_list", "magic_shop.cursed_worn_entry", "magic_shop.curse_effect" });
    }

    // ---------- 5. love spells and the NPC picker ----------

    private static readonly string[] LoveKeys =
    {
        "magic_shop.love_intro_vials", "magic_shop.love_intro_power", "magic_shop.col_spell", "magic_shop.col_effect",
        "magic_shop.love_spell_fondness", "magic_shop.love_spell_attraction", "magic_shop.love_spell_desire", "magic_shop.love_spell_binding",
        "magic_shop.love_spell_fondness_effect", "magic_shop.love_spell_attraction_effect", "magic_shop.love_spell_desire_effect",
        "magic_shop.love_spell_binding_effect", "magic_shop.picker_casting", "magic_shop.col_relationship", "magic_shop.love_cast_confirm",
        "magic_shop.relationship_label", "magic_shop.love_shatter", "magic_shop.love_connection", "magic_shop.love_improved",
        "magic_shop.love_whisper", "magic_shop.picker_next", "magic_shop.picker_clear", "magic_shop.picker_search_key",
    };

    /// <summary>The love spell flow: list, spell, picker (one search, then the first NPC), confirm, cast.</summary>
    private static Task<string> Love(string lang, int level, string spell) => InLanguage(lang, async () =>
    {
        var npcs = Enumerable.Range(0, 20).Select(i => Npc(LongName.Substring(0, 26) + $"{i,4}", LongestClass(lang))).ToList();
        return await WithNpcs(npcs, async () =>
        {
            var hero = Hero(level);
            hero.Mana = level == 1 ? 0 : 5000;
            var s = Open(hero, spell, "S", "Hosszunevu", "1", "Y");
            await Run(s, "CastLoveSpell", hero);
            return s.Text;
        });
    });

    [Theory]
    [InlineData("en", 100)] [InlineData("hu", 100)] [InlineData("en", 1)] [InlineData("hu", 1)]
    public async Task LoveSpells_Fit_AndAreInThePlayersLanguage(string lang, int level)
    {
        string shown = await Love(lang, level, "4");
        Capture($"town-magic-love-{lang}-{level}.txt", shown);
        EveryRowFits(shown, $"{lang} love spells (level {level})");
        foreach (var k in new[] { "magic_shop.love_spell_binding", "magic_shop.love_spell_binding_effect", "magic_shop.love_spell_attraction" })
            shown.Should().Contain(L(lang, k));
        if (level == 1) shown.Should().Contain(L(lang, "magic_shop.req_level", 35));
        if (lang == "hu" && level == 100) NoEnglishLeft(shown, LoveKeys);
        if (lang == "en" && level == 100)
            shown.Should().Contain("  Cast Binding of Souls on ").And.Contain("   [N]ext   [P]rev").And.Contain("   [S]earch")
                .And.Contain("  Relationship: ");
    }

    // ---------- 6. dark arts ----------

    private static readonly string[] DarkKeys =
    {
        "magic_shop.dark_eyes", "magic_shop.dark_price", "magic_shop.col_spell", "magic_shop.col_chance", "magic_shop.col_dark",
        "magic_shop.dark_spell_hex", "magic_shop.dark_spell_touch", "magic_shop.dark_spell_severance", "magic_shop.dark_spell_hex_desc",
        "magic_shop.dark_spell_touch_desc", "magic_shop.dark_spell_severance_desc", "magic_shop.picker_dark_arts",
        "magic_shop.dark_confirm_title", "magic_shop.dark_circle", "magic_shop.dark_gathers", "arena.col_status",
    };

    private static Task<string> Dark(string lang, int level) => InLanguage(lang, async () =>
    {
        var npcs = Enumerable.Range(0, 3).Select(i => Npc(LongName.Substring(0, 29) + i, LongestClass(lang))).ToList();
        return await WithNpcs(npcs, async () =>
        {
            var hero = Hero(level);
            var s = Open(hero, "3", "1", "N");
            await Run(s, "CastDeathSpell", hero);
            return s.Text;
        });
    });

    [Theory]
    [InlineData("en", 100)] [InlineData("hu", 100)] [InlineData("en", 1)] [InlineData("hu", 1)]
    public async Task DarkArts_Fit_AndAreInThePlayersLanguage(string lang, int level)
    {
        string shown = await Dark(lang, level);
        Capture($"town-magic-dark-{lang}-{level}.txt", shown);
        EveryRowFits(shown, $"{lang} dark arts (level {level})");
        shown.Should().Contain(L(lang, "magic_shop.dark_spell_severance")).And.Contain(L(lang, "magic_shop.dark_spell_hex_desc"));
        if (level == 1) shown.Should().Contain($"[{L(lang, "magic_shop.lv_tag", 40)}]");
        if (lang == "hu" && level == 100) NoEnglishLeft(shown, DarkKeys);
    }

    // ---------- 7. scrying ----------

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task Scrying_Fits_AndIsInThePlayersLanguage(string lang)
    {
        var npc = Npc(LongName, LongestClass(lang));
        var profile = PersonalityProfile.GenerateForArchetype("commoner");
        profile.Romanticism = 0.9f;
        profile.Greed = 0.9f;
        npc.Personality = profile;
        npc.Brain = new NPCBrain(npc, profile);
        string shown = await InLanguage(lang, () => WithNpcs(new[] { npc }, async () =>
        {
            var hero = Hero();
            var s = Open(hero, "1");
            await Run(s, "ScryNPC", hero);
            return s.Text;
        }));
        Capture($"town-magic-scry-{lang}.txt", shown);
        EveryRowFits(shown, $"{lang} scrying");
        shown.Should().Contain($"{L(lang, "magic_shop.trait_romantic")}, {L(lang, "magic_shop.trait_greedy")}");
        if (lang == "hu")
            NoEnglishLeft(shown, new[] { "magic_shop.scry_gaze", "magic_shop.scry_name_soul", "magic_shop.scry_reveal", "magic_shop.picker_scry_title",
                "magic_shop.trait_romantic", "magic_shop.trait_greedy" });
    }

    // ---------- 8. identify and haggling ----------

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task Identify_Fits_AndIsInThePlayersLanguage(string lang)
    {
        var hero = Hero();
        hero.Inventory.Add(new Item { Name = LongItem, Type = ObjType.Body, Armor = 50, Value = 100, IsIdentified = false });
        string shown = await InLanguage(lang, async () =>
        {
            var s = Open(hero, "1", "Y");
            await Run(s, "IdentifyItem", hero);
            return s.Text;
        });
        Capture($"town-magic-identify-{lang}.txt", shown);
        EveryRowFits(shown, $"{lang} identify");
        shown.Should().Contain(L(lang, "magic_shop.item_properties"));
        if (lang == "hu") NoEnglishLeft(shown, new[] { "magic_shop.item_properties", "magic_shop.identify_prompt", "magic_shop.identify_cost" });
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task HaggleOffer_IsInThePlayersLanguage(string lang)
    {
        var hero = Hero(20);
        hero.Gold = 10_000_000;
        string shown = await InLanguage(lang, async () =>
        {
            var s = Open(hero, "N");
            var t = typeof(MagicShopLocation).GetNestedType("AccessoryCategory", BindingFlags.NonPublic)!;
            var backend = typeof(SaveSystem).GetField("backend", F)!;
            var real = backend.GetValue(SaveSystem.Instance);
            try
            {
                backend.SetValue(SaveSystem.Instance, DispatchProxy.Create<ISaveBackend, MagicHaggle124Tests.NullBackend>());
                await Run(s, "BuyAccessoryItem", Enum.Parse(t, "Rings"), 1, hero);
            }
            catch (LocationExitException) { }
            finally { backend.SetValue(SaveSystem.Instance, real); }
            return s.Text;
        });
        EveryRowFits(shown, $"{lang} haggle offer");
        string head = Loc.GetIn(lang, "shop.buy_prompt_haggle").Split('{')[0].Trim();
        head.Should().NotBeEmpty();
        shown.Should().Contain(head, "the 1.2.4 haggle offer is in the player's language");
    }

    // ---------- 9. the Electron menu ----------

    [Fact]
    public void ElectronMenu_LabelsAreKeys()
    {
        string src = File.ReadAllText(Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Scripts", "Locations", "MagicShopLocation.cs"));
        string emit = src.Substring(src.IndexOf("private void EmitElectronEvents()"));
        foreach (Match m in Regex.Matches(emit, @"Label = ([^,]+),"))
            m.Groups[1].Value.Should().StartWith("Loc.Get(", "every Electron menu label is keyed");
    }

    // ---------- 10. widths of the parts any language can make longer ----------

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void TemplateRows_And_Headers_Fit_WithTheLongestNames(string lang)
    {
        InLang(lang, () =>
        {
            for (int t = 0; t < 14; t++)
                foreach (var row in MagicShopLocation.TemplateRows("  ", Loc.Get("magic_shop.enchant_confirm_line"), "yellow",
                             (LongestItemName(), "white"), (MagicShopLocation.TierName(t), "white"), (MagicShopLocation.TierDescription(t), "white")))
                    row.Sum(p => p.text.Length).Should().BeLessOrEqualTo(MaxWidth);
            foreach (var key in new[] { "magic_shop.love_spell_attraction", "magic_shop.love_spell_binding" })
                foreach (var row in MagicShopLocation.TemplateRows("  ", Loc.Get("magic_shop.love_cast_confirm"), "white",
                             (Loc.Get(key), "white"), (LongName, "white")))
                    row.Sum(p => p.text.Length).Should().BeLessOrEqualTo(MaxWidth);
            MagicShopLocation.EnchantListHeader(MagicShopLocation.EnchantSlotWidth()).Length.Should().BeLessOrEqualTo(MaxWidth);
            MagicShopLocation.PickerHeader(MagicShopLocation.PickerClassWidth(new[] { Npc(LongName, LongestClass(lang)) }), true, true)
                .Length.Should().BeLessOrEqualTo(MaxWidth);
            MagicShopLocation.MenuSectionRow("magic_shop.menu_sec_potions", "magic_shop.menu_sec_arcane").Length.Should().BeLessOrEqualTo(MaxWidth);
            return 0;
        });
    }

    // ---------- 11. the keys ----------

    [Fact]
    public void NewKeys_AreInEveryLanguage_WithTheSamePlaceholders_AndNoDashes()
    {
        var langs = AllLanguages.ToDictionary(l => l, l => JsonDocument.Parse(File.ReadAllText(
            Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Localization", l + ".json"))).RootElement);
        string src = File.ReadAllText(Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Scripts", "Locations", "MagicShopLocation.cs"));
        var used = Regex.Matches(src, "\"(magic_shop\\.[a-z0-9_.]+)\"").Select(m => m.Groups[1].Value)
            .Where(k => k != "magic_shop.death_spell" && k != "magic_shop.death_spell_failed").Distinct().ToList();
        used.Count.Should().BeGreaterThan(200);
        var sameInHu = new HashSet<string> { "magic_shop.picker_max" };
        foreach (var key in used)
        {
            langs["en"].TryGetProperty(key, out var e).Should().BeTrue($"{key} is in en.json");
            var holes = Regex.Matches(e.GetString()!, @"\{\d+\}").Select(m => m.Value).OrderBy(x => x).ToList();
            foreach (var lang in AllLanguages.Skip(1))
            {
                langs[lang].TryGetProperty(key, out var v).Should().BeTrue($"{key} is in {lang}.json");
                v.GetString().Should().NotBeNullOrWhiteSpace();
                Regex.Matches(v.GetString()!, @"\{\d+\}").Select(m => m.Value).OrderBy(x => x).Should().Equal(holes, $"{lang} {key} keeps the placeholders");
            }
            foreach (var lang in AllLanguages)
                langs[lang].GetProperty(key).GetString().Should().NotContain("\u2014").And.NotContain("\u2013", $"{lang} {key}");
        }
        foreach (var key in used.Where(k => k.Contains("tier_") || k.Contains("spell_") || k.Contains("trait_") || k.StartsWith("magic_shop.picker_")))
            if (!sameInHu.Contains(key))
                langs["hu"].GetProperty(key).GetString().Should().NotBe(langs["en"].GetProperty(key).GetString(), $"{key} is translated");
    }
}
