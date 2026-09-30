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
/// v1.2.2: the gear comparison views read in the player's language: equipping from the backpack
/// and the slot screen.
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
}
