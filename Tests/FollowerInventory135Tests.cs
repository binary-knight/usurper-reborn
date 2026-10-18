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
using UsurperRemake.Data;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.3.5: a rolled loot name in the follower inventory (DungeonLocation.ShowFollowerInventory). A loot name is
/// composed (a template with a rarity, curse or effect word, a world boss drop's element word in front), so it has
/// no single key; it shows through ItemNames in the reader's language. A world boss drop in Spanish runs past the
/// row: the name is cut with a closing period, so every row fits 79 columns, and the messages that carry it wrap.
/// </summary>
[Collection("SharedGameSingletons")]
public class FollowerInventory135Tests
{
    private const BindingFlags FS = BindingFlags.NonPublic | BindingFlags.Static;
    private const int MaxWidth = 79;

    private static readonly EquipmentSlot[] AllSlots =
    {
        EquipmentSlot.MainHand, EquipmentSlot.OffHand, EquipmentSlot.Head, EquipmentSlot.Body, EquipmentSlot.Arms,
        EquipmentSlot.Hands, EquipmentSlot.Legs, EquipmentSlot.Feet, EquipmentSlot.Cloak, EquipmentSlot.Waist, EquipmentSlot.Neck,
        EquipmentSlot.LFinger, EquipmentSlot.RFinger,
    };

    private static string Strip(string s) => Regex.Replace(s, "\u001b\\[[0-9;?]*[A-Za-z]", "");

    private static IEnumerable<string> Rows(string text) => text.Replace("\r", "").Split('\n');

    private static void EveryRowFits(string text, string screen)
    {
        foreach (var row in Rows(text))
            row.Length.Should().BeLessOrEqualTo(MaxWidth, $"every row of the {screen} fits in {MaxWidth} columns: \"{row}\"");
    }

    /// <summary>A loot drop as the generator stores it: its English name and its English template as Family.</summary>
    private static Item Loot(string name, string family) => new Item { Name = name, Family = family, Type = ObjType.Weapon };

    /// <summary>The follower inventory with `equipped` in every slot and `backpack` in the bag, through the game's
    /// own path (Character.BuildEquipmentFromItem, EquipmentDatabase.RegisterDynamic). The input unequips the weapon
    /// and then tries backpack item 1, so both messages that carry a name are shown.</summary>
    private static async Task<string> Render(string lang, Item equipped, Item backpack)
    {
        var eq = Character.BuildEquipmentFromItem(equipped, EquipmentSlot.MainHand, WeaponHandedness.OneHanded, WeaponType.Sword);
        int id = EquipmentDatabase.RegisterDynamic(eq);
        var player = new Character { Name1 = "Follower", Name2 = "Follower", Class = CharacterClass.Magician, Level = 100, HP = 10, MaxHP = 10,
            AI = CharacterAI.Human };
        foreach (var slot in AllSlots) player.EquippedItems[slot] = id;
        player.Inventory.Add(backpack);

        var prev = GameConfig.Language;
        bool sr = GameConfig.ScreenReaderMode;
        try
        {
            GameConfig.Language = lang;
            GameConfig.ScreenReaderMode = false;
            var output = new MemoryStream();
            var term = new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes("U\n1\n1\n\n")), output);
            await (Task)typeof(DungeonLocation).GetMethod("ShowFollowerInventory", FS)!.Invoke(null, new object[] { player, term })!;
            term.StreamWriterInternal?.Flush();
            // A player's Enter after a prompt ends that row on screen; the test input is not echoed, so the row
            // end is put back after each of the two prompts.
            string unequipPrompt = "  " + Loc.GetIn(lang, "dungeon.follower_unequip_prompt");
            return Strip(Encoding.UTF8.GetString(output.ToArray())).Replace("  > ", "  > \n").Replace(unequipPrompt, unequipPrompt + "\n");
        }
        finally { GameConfig.Language = prev; GameConfig.ScreenReaderMode = sr; }
    }

    /// <summary>Each list row that ends in the item's name (after `label`) shows that name in full, or cut with a
    /// closing period; the rows are found by their label.</summary>
    private static void ListRowsShow(string text, string label, string shown)
    {
        var rows = Rows(text).Where(r => r.Contains(label)).ToList();
        rows.Should().NotBeEmpty(label);
        foreach (var row in rows)
        {
            string part = row.Substring(row.IndexOf(label, StringComparison.Ordinal) + label.Length);
            if (part == shown) continue;
            part.Should().EndWith(".", $"a cut name ends with a period: \"{row}\"");
            shown.Should().StartWith(part.Substring(0, part.Length - 1), $"the cut name is the start of the shown one: \"{row}\"");
        }
    }

    /// <summary>The equipped row of the weapon and the unequip row of the off hand (the widest slot label in Spanish)
    /// show the name in full or cut.</summary>
    private static void NameRowsShow(string text, string lang, string shown)
    {
        int slotWidth = Math.Max(10, new[] { "inn.equip_slot_weapon", "inn.equip_slot_off_hand", "ui.head", "ui.body", "ui.arms", "ui.hands",
            "ui.legs", "ui.feet", "ui.cloak", "ui.waist", "ui.neck", "dungeon.slot_l_ring", "dungeon.slot_r_ring" }
            .Max(k => Loc.GetIn(lang, k).Length) + 1);
        ListRowsShow(text, "    " + Loc.GetIn(lang, "inn.equip_slot_weapon").PadRight(slotWidth), shown);
        ListRowsShow(text, $"2. {Loc.GetIn(lang, "inn.equip_slot_off_hand")}: ", shown);
    }

    // A world boss drop the generator rolled (GenerateWorldBossLoot over every class and level): an element word, a
    // template and an effect word. Sixty columns in Spanish, past the follower inventory's rows.
    private const string RolledName = "Venomsteel Forest Guardian Armor of the Salamander";
    private const string RolledFamily = "Forest Guardian Armor";

    [Fact]
    public void RolledName_IsOneTheGeneratorWrites()
    {
        LootGenerator.WorldBossNamePrefixes.Should().Contain("Venomsteel");
        LootGenerator.AllNameFormsFor(RolledFamily, "en").Select(f => "Venomsteel " + f).Should().Contain(RolledName);
        LootGenerator.AllNameFormsFor("Fine Dagger", "en").Should().Contain("Fine Dagger of the World Slayer");
        ItemNames.KeyOf(RolledName).Should().BeNull("a composed loot name has no single key");
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("es")]
    public async Task RolledLoot_ShowsInLanguage_AndFits(string lang)
    {
        var rolled = Loot(RolledName, RolledFamily);
        var dagger = Loot("Fine Dagger of the World Slayer", "Fine Dagger");
        string shown = ItemNames.DisplayIn(lang, rolled);
        string daggerShown = ItemNames.DisplayIn(lang, dagger);
        shown.Should().NotBe(RolledName).And.Contain(Loc.GetIn(lang, "item.forest_guardian_armor"));
        daggerShown.Should().NotBe(dagger.Name).And.Contain(Loc.GetIn(lang, "item.fine_dagger").Split(' ')[0]);
        if (lang == "es") shown.Length.Should().BeGreaterThan(MaxWidth - 20, "the Spanish name runs past the equipped row");

        string text = await Render(lang, rolled, dagger);
        text.Should().NotContain(RolledName).And.NotContain(dagger.Name);
        text.Should().Contain(Loc.GetIn(lang, "item.forest_guardian_armor"));
        NameRowsShow(text, lang, shown);
        text.Should().Contain("    1. " + daggerShown, "the backpack shows the dagger in full");
        EveryRowFits(text, "follower inventory");
    }

    // Every form the generator can write on every loot template, with every world boss element word: the longest one
    // in each language still fits the follower inventory.
    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    [InlineData("es")]
    public async Task LongestLootForm_Fits(string lang)
    {
        var (stored, family, shown) = ItemNames.LootTemplates()
            .SelectMany(t => LootGenerator.AllNameFormsFor(t, "en").Distinct()
                .SelectMany(f => LootGenerator.WorldBossNamePrefixes.Select(p => p + " " + f).Prepend(f))
                .Select(s => (stored: s, family: t, shown: ItemNames.DisplayIn(lang, s, t))))
            .OrderByDescending(x => x.shown.Length).ThenBy(x => x.stored, StringComparer.Ordinal).First();
        if (lang != "en") shown.Should().NotBe(stored);

        string text = await Render(lang, Loot(stored, family), Loot(stored, family));
        if (lang != "en") text.Should().NotContain(stored);
        NameRowsShow(text, lang, shown);
        EveryRowFits(text, "follower inventory");
        if (lang == "en")
            Rows(text).Should().Contain(r => r.EndsWith($"2. {Loc.GetIn(lang, "inn.equip_slot_off_hand")}: {shown}"), "no English name is cut");
    }
}
