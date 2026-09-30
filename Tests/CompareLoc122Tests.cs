using System;
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
/// v1.2.2: the gear comparison views read in the player's language: equipping from the backpack,
/// the slot screen, the loot pickup comparison, the Magic Shop accessory list and the companion
/// equip screen at the Inn.
/// </summary>
[Collection("SharedGameSingletons")]
public class CompareLoc122Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static (TerminalEmulator term, MemoryStream output) Terminal(string input = "")
    {
        var output = new MemoryStream();
        return (new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes(input)), output), output);
    }

    private static string Shown(TerminalEmulator term, MemoryStream output)
    {
        term.StreamWriterInternal?.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");
    }

    /// <summary>Writes a render to USURPER_EVIDENCE_DIR when set, for a by-eye check.</summary>
    private static void Capture(string name, string text)
    {
        var dir = Environment.GetEnvironmentVariable("USURPER_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, name), text);
    }

    private static Character Hero() => new()
    {
        Name1 = "tester", Name2 = "Tester", Class = CharacterClass.Warrior, Level = 8, HP = 500, MaxHP = 500,
        AI = CharacterAI.Human, Gold = 1_000_000,
    };

    /// <summary>A body armour already worn, with bonuses so the bonus lines print.</summary>
    private static int WornPlate() => EquipmentDatabase.RegisterDynamic(new Equipment
    {
        Name = "Old Plate", Slot = EquipmentSlot.Body, ArmorClass = 5, StrengthBonus = 2, DefenceBonus = 1, MinLevel = 1,
    });

    private static Item NewPlate() => new()
    {
        Name = "New Plate", Type = ObjType.Body, Armor = 9, Strength = 3, Defence = 2, Dexterity = 1, Value = 100,
    };

    private static async Task<string> WithLanguage(string lang, Func<Task<string>> render)
    {
        var prev = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            return await render();
        }
        finally { GameConfig.Language = prev; }
    }

    private static void ShowsTheStatAbbreviations(string text, string lang)
    {
        text.Should().Contain($"{Loc.Get("ui.stat_str")} +", lang);
        text.Should().Contain($"{Loc.Get("ui.stat_def")} +", lang);
        // only the abbreviations this language spells differently can be checked for absence
        foreach (var (key, en) in new[] { ("ui.stat_str", "Str"), ("ui.stat_def", "Def"), ("ui.stat_dex", "Dex") })
            if (Loc.Get(key) != en) text.Should().NotContain($"{en} +", lang);
    }

    // ---------- 1. equipping from the backpack ----------

    private static async Task<string> RenderEquipFromBackpack()
    {
        var hero = Hero();
        hero.EquippedItems[EquipmentSlot.Body] = WornPlate();
        hero.Inventory.Add(NewPlate());
        var (term, output) = Terminal("n\n\n");
        var inv = new InventorySystem(term, hero);
        await (Task)typeof(InventorySystem).GetMethod("EquipFromBackpack", F)!.Invoke(inv, new object?[] { 0, null })!;
        return Shown(term, output);
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("es")]
    public async Task EquipFromBackpack_Comparison_IsLocalized(string lang)
    {
        string text = await WithLanguage(lang, async () =>
        {
            string t = await RenderEquipFromBackpack();
            Capture($"compare-backpack-{lang}.txt", t);
            t.Should().Contain(Loc.Get("combat.compare_armor", 5, 9).Trim());
            t.Should().Contain(Loc.Get("combat.upgrade", 4));
            t.Should().Contain(Loc.Get("combat.current_bonuses", "").Trim());
            t.Should().Contain(Loc.Get("combat.new_bonuses", "").Trim());
            t.Should().Contain(Loc.Get("inventory.currently_equipped") + "Old Plate");
            ShowsTheStatAbbreviations(t, lang);
            return t;
        });
        text.Should().NotContain("UPGRADE").And.NotContain("Armor:").And.NotContain("Current bonuses").And.NotContain("New bonuses");
        text.Should().NotContain(": :", "the label already ends in a colon");
        if (lang == "hu") text.Should().Contain("FEJLESZTÉS").And.Contain("Páncél: 5 -> 9").And.Contain("Erő +3");
    }

    // ---------- 2. the slot screen ----------

    private static async Task<string> RenderSlotScreen()
    {
        var hero = Hero();
        hero.EquippedItems[EquipmentSlot.Body] = WornPlate();
        var (term, output) = Terminal("Q\n\n");
        var inv = new InventorySystem(term, hero);
        await (Task)typeof(InventorySystem).GetMethod("ManageSlot", F)!.Invoke(inv, new object?[] { EquipmentSlot.Body })!;
        return Shown(term, output);
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("fr")]
    public async Task SlotScreen_HeaderTypeAndRarity_AreLocalized(string lang)
    {
        string text = await WithLanguage(lang, async () =>
        {
            string t = await RenderSlotScreen();
            Capture($"compare-slot-{lang}.txt", t);
            t.Should().Contain(Loc.Get("inventory.slot_screen_header", Loc.Get("inventory.slot_body").ToUpper()));
            t.Should().Contain(Loc.Get("inventory.item_type_rarity", Loc.Get("inventory.slot_body"), Loc.Get("inventory.rarity_common")));
            t.Should().Contain(Loc.Get("inventory.currently_equipped") + "Old Plate");
            return t;
        });
        text.Should().NotContain(" SLOT").And.NotContain("Type:").And.NotContain("Rarity:").And.NotContain("Common");
        if (lang == "hu") text.Should().Contain("Ritkaság").And.Contain("Gyakori");
    }

    // ---------- 3. the loot pickup comparison ----------

    private static string RenderLootComparison()
    {
        var hero = Hero();
        hero.EquippedItems[EquipmentSlot.Body] = WornPlate();
        var (term, output) = Terminal();
        CombatEngine.ShowEquipmentComparison(term, NewPlate(), hero);
        return Shown(term, output);
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("it")]
    public async Task LootComparison_StatAbbreviations_AreLocalized(string lang)
    {
        string text = await WithLanguage(lang, () =>
        {
            string t = RenderLootComparison();
            Capture($"compare-loot-{lang}.txt", t);
            t.Should().Contain(Loc.Get("combat.upgrade", 4));
            ShowsTheStatAbbreviations(t, lang);
            return Task.FromResult(t);
        });
        text.Should().NotContain("UPGRADE").And.NotContain("Current bonuses");
        if (lang == "hu") text.Should().Contain("Erő +3").And.Contain("Véd +2").And.Contain("Ügy +1");
    }

    // ---------- 4. the Magic Shop accessory list ----------

    private static string RenderRingList()
    {
        var hero = Hero();
        hero.Level = 20;
        var ring = EquipmentDatabase.RegisterDynamic(new Equipment
        {
            Name = "Plain Band", Slot = EquipmentSlot.LFinger, StrengthBonus = 1, MinLevel = 1,
        });
        hero.EquippedItems[EquipmentSlot.LFinger] = ring;
        var (term, output) = Terminal();
        var shop = new MagicShopLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(shop, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(shop, hero);
        var category = typeof(MagicShopLocation).GetNestedType("AccessoryCategory", BindingFlags.NonPublic)!;
        typeof(MagicShopLocation).GetMethod("ShowAccessoryCategoryItems", F)!
            .Invoke(shop, new object[] { Enum.Parse(category, "Rings") });
        return Shown(term, output);
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("es")]
    public async Task MagicShopRingList_EquippedAndLegend_AreLocalized(string lang)
    {
        string text = await WithLanguage(lang, () =>
        {
            string t = RenderRingList();
            Capture($"compare-magicshop-{lang}.txt", t);
            t.Should().Contain($"{Loc.Get("inventory.slot_left_ring")}: Plain Band");
            t.Should().Contain($"{Loc.Get("ui.gold")}: ");
            t.Should().Contain(Loc.Get("magic_shop.legend_upgrade"));
            t.Should().Contain(Loc.Get("magic_shop.legend_downgrade"));
            t.Should().Contain($"{Loc.Get("ui.stat_str")}+1");
            return Task.FromResult(t);
        });
        text.Should().NotContain("L.Finger").And.NotContain("Gold:").And.NotContain(" upgrade ")
            .And.NotContain("downgrade vs equipped").And.NotContain("Str+");
        if (lang == "hu") text.Should().Contain("Bal gyűrű: Plain Band").And.Contain("Erő+1");
    }

    // ---------- 5. the companion equip screen at the Inn ----------

    private static async Task<string> RenderCompanionEquip()
    {
        var hero = Hero();
        hero.Inventory.Add(NewPlate());
        var ally = new Character
        {
            Name1 = "Ally", Name2 = "Ally", Class = CharacterClass.Warrior, Level = 8, HP = 300, MaxHP = 300,
        };
        ally.EquippedItems[EquipmentSlot.Body] = WornPlate();
        // slot 4 (Body): shows the current item and the list, then cancels; slot 3 (Head): nothing to offer; Q leaves
        var (term, output) = Terminal("4\nx\n3\nQ\n");
        var inn = new InnLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(inn, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(inn, hero);
        await (Task)typeof(InnLocation).GetMethod("CompanionEquipItemToCharacter", F)!.Invoke(inn, new object[] { ally })!;
        return Shown(term, output);
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("fr")]
    public async Task InnCompanionEquip_HeaderCurrentAndNoItems_AreLocalized(string lang)
    {
        string text = await WithLanguage(lang, async () =>
        {
            string t = await RenderCompanionEquip();
            Capture($"compare-inn-{lang}.txt", t);
            t.Should().Contain(Loc.Get("home.equip_to_header", "ALLY"));
            t.Should().Contain($"{Loc.Get("home.equip_current")} Old Plate");
            t.Should().Contain(Loc.Get("home.no_items_slot"));
            return t;
        });
        text.Should().NotContain("EQUIP ITEM TO").And.NotContain("Current:").And.NotContain("No items available");
        if (lang == "hu") text.Should().Contain("Jelenlegi: Old Plate");
    }
}
