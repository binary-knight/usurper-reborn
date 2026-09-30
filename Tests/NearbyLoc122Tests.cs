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
/// v1.2.2: the screens next to the gear comparisons read in the player's language: the backpack
/// bonus summary, the loot bonus line and the loot passed to another player, the Magic Shop menu,
/// purchase detail and ring choice, the companion equipment headers at the Inn, the slot list
/// shown at Home, and the slot picker, which now fits 80 columns in every language.
/// </summary>
[Collection("SharedGameSingletons")]
public class NearbyLoc122Tests
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
        Name1 = "tester", Name2 = "Tester", Class = CharacterClass.Warrior, Level = 20, HP = 500, MaxHP = 500,
        AI = CharacterAI.Human, Gold = 10_000_000,
    };

    private static async Task<T> WithLanguage<T>(string lang, Func<Task<T>> body)
    {
        var prev = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            return await body();
        }
        finally { GameConfig.Language = prev; }
    }

    private static T At<T>(T location, TerminalEmulator term, Character hero) where T : BaseLocation
    {
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(location, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(location, hero);
        return location;
    }

    // ---------- 1. the backpack bonus summary ----------

    [Theory]
    [InlineData("hu")]
    [InlineData("fr")]
    public async Task BackpackBonusSummary_IsLocalized(string lang)
    {
        string text = await WithLanguage(lang, () =>
        {
            var hero = Hero();
            hero.EquippedItems[EquipmentSlot.Neck] = EquipmentDatabase.RegisterDynamic(new Equipment
            {
                Name = "Test Amulet", Slot = EquipmentSlot.Neck, StrengthBonus = 2, WisdomBonus = 1,
                MaxHPBonus = 10, MaxManaBonus = 5, MagicResistance = 4, StaminaBonus = 3, MinLevel = 1,
            });
            var (term, output) = Terminal();
            var inv = new InventorySystem(term, hero);
            typeof(InventorySystem).GetMethod("DisplayStatsSummary", F)!.Invoke(inv, null);
            string t = Shown(term, output);
            Capture($"nearby-backpack-{lang}.txt", t);
            t.Should().Contain($"{Loc.Get("ui.stat_str")} +2").And.Contain($"{Loc.Get("ui.stat_wis")} +1");
            t.Should().Contain($"{Loc.Get("ui.max_hp")} +10").And.Contain($"{Loc.Get("ui.max_mana")} +5");
            t.Should().Contain($"{Loc.Get("ui.stat_mr")} +4").And.Contain($"{Loc.Get("ui.stat_sta")} +3");
            return Task.FromResult(t);
        });
        text.Should().NotContain("Str +").And.NotContain("Wis +").And.NotContain("MaxHP").And.NotContain("MaxMP")
            .And.NotContain("MagicRes").And.NotContain("Sta +");
        text.Should().NotContain(": :").And.NotContain(":  :").And.NotContain("═══ ═══").And.NotContain("═══ ===", "one frame and one colon");
        if (lang == "hu") text.Should().Contain("Erő +2").And.Contain("Max ÉP +10");
    }

    // ---------- 2. the loot bonus line ----------

    [Theory]
    [InlineData("hu")]
    [InlineData("it")]
    public async Task LootBonusLine_IsLocalized(string lang)
    {
        string text = await WithLanguage(lang, async () =>
        {
            var (term, output) = Terminal();
            var engine = new CombatEngine(term);
            var loot = new Item { Name = "Test Plate", Type = ObjType.Body, Armor = 9, Strength = 3, Wisdom = 2, HP = 4, Stamina = 1, Value = 100, IsIdentified = true };
            var monster = new Monster { Name = "Kobold", Level = 3, HP = 0, MaxHP = 10 };
            await (Task)typeof(CombatEngine).GetMethod("RenderEquipment", F)!
                .Invoke(engine, new object?[] { loot, monster, Hero(), new CombatEngine.LocalizedLines() })!;
            string t = Shown(term, output);
            Capture($"nearby-loot-{lang}.txt", t);
            t.Should().Contain($"{Loc.Get("ui.stat_str")} +3").And.Contain($"{Loc.Get("ui.stat_hp")} +4");
            return t;
        });
        text.Should().NotContain("Str +3").And.NotContain("Wis +2").And.NotContain("HP +4").And.NotContain("Sta +1");
    }

    // ---------- 3. loot passed to another player ----------

    [Theory]
    [InlineData("hu")]
    [InlineData("fr")]
    public async Task LootPassedToAnotherPlayer_IsLocalized(string lang)
    {
        string text = await WithLanguage(lang, async () =>
        {
            var (leaderTerm, _) = Terminal();
            var engine = new CombatEngine(leaderTerm);
            var (otherTerm, otherOut) = Terminal("P");
            var other = Hero();
            other.Name2 = "Other"; other.RemoteTerminal = otherTerm;
            var loot = new Item { Name = "Test Blade", Type = ObjType.Weapon, Attack = 12, Value = 2500, IsIdentified = true };
            var monster = new Monster { Name = "Kobold", Level = 3, HP = 0, MaxHP = 10 };
            await (Task<bool>)typeof(CombatEngine).GetMethod("OfferLootToOtherPlayers", F)!
                .Invoke(engine, new object?[] { loot, Hero(), new System.Collections.Generic.List<Character> { other }, monster })!;
            string t = Shown(otherTerm, otherOut);
            Capture($"nearby-loot-passed-{lang}.txt", t);
            t.Should().Contain(Loc.Get("combat.loot_passed_from", "Kobold").Trim());
            t.Should().Contain(Loc.Get("combat.loot_attack_power", 12).Trim());
            t.Should().Contain(Loc.Get("combat.loot_value", "2,500").Trim());
            return t;
        });
        text.Should().NotContain("LOOT PASSED").And.NotContain("Attack Power").And.NotContain("Value:");
    }

    // ---------- 4. the Magic Shop menu, purchase detail and ring choice ----------

    private static string RenderRingList(string lang)
    {
        var (term, output) = Terminal();
        var shop = At(new MagicShopLocation(), term, Hero());
        var category = typeof(MagicShopLocation).GetNestedType("AccessoryCategory", BindingFlags.NonPublic)!;
        typeof(MagicShopLocation).GetMethod("ShowAccessoryCategoryItems", F)!
            .Invoke(shop, new object[] { Enum.Parse(category, "Rings") });
        return Shown(term, output);
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("es")]
    public async Task MagicShopMenu_IsLocalized(string lang)
    {
        string text = await WithLanguage(lang, () =>
        {
            string t = RenderRingList(lang);
            Capture($"nearby-magicshop-menu-{lang}.txt", t);
            t.Should().Contain($"]{Loc.Get("shop.buy")}").And.Contain($"]{Loc.Get("shop.sell")}")
                .And.Contain($"]{Loc.Get("inventory.back")}");
            return Task.FromResult(t);
        });
        text.Should().NotContain("]Buy").And.NotContain("]Sell").And.NotContain("]Back");
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("it")]
    public async Task MagicShopPurchaseDetailAndRingChoice_AreLocalized(string lang)
    {
        string text = await WithLanguage(lang, async () =>
        {
            var hero = Hero();
            hero.EquippedItems[EquipmentSlot.LFinger] = EquipmentDatabase.RegisterDynamic(new Equipment { Name = "Left Band", Slot = EquipmentSlot.LFinger, MinLevel = 1 });
            hero.EquippedItems[EquipmentSlot.RFinger] = EquipmentDatabase.RegisterDynamic(new Equipment { Name = "Right Band", Slot = EquipmentSlot.RFinger, MinLevel = 1 });
            // yes to buy, equip, then cancel at the ring choice
            var (term, output) = Terminal("Y\nE\nC\n\n\n");
            var shop = At(new MagicShopLocation(), term, hero);
            var category = typeof(MagicShopLocation).GetNestedType("AccessoryCategory", BindingFlags.NonPublic)!;
            await (Task)typeof(MagicShopLocation).GetMethod("BuyAccessoryItem", F)!
                .Invoke(shop, new object[] { Enum.Parse(category, "Rings"), 1, hero })!;
            string t = Shown(term, output);
            Capture($"nearby-magicshop-buy-{lang}.txt", t);
            t.Should().Contain($"{Loc.Get("weapon_shop.reforge_rarity")}: ");
            t.Should().Contain($"{Loc.Get("magic_shop.ring_left")}:").And.Contain("Left Band");
            t.Should().Contain($"{Loc.Get("magic_shop.ring_right")}:").And.Contain("Right Band");
            t.Should().Contain(Loc.Get("magic_shop.cancel_purchase"));
            return t;
        });
        text.Should().NotContain("Rarity:").And.NotContain("] Left:").And.NotContain("] Right:").And.NotContain("Cancel purchase");
        if (lang == "hu") text.Should().Contain("[L] Bal:").And.Contain("[R] Jobb:");
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("fr")]
    public async Task MagicShopDetailedStats_AreLocalized(string lang)
    {
        string text = await WithLanguage(lang, () =>
        {
            var item = new Equipment { Name = "Test Ring", Slot = EquipmentSlot.LFinger, StrengthBonus = 2, MaxHPBonus = 10, MagicResistance = 3, CriticalDamageBonus = 5, LifeSteal = 4 };
            string t = (string)typeof(MagicShopLocation).GetMethod("GetAccessoryDetailedStats", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { item })!;
            Capture($"nearby-magicshop-stats-{lang}.txt", t);
            t.Should().Contain($"{Loc.Get("ui.stat_str")}+2").And.Contain($"{Loc.Get("ui.stat_hp")}+10").And.Contain($"{Loc.Get("ui.stat_mr")}+3");
            t.Should().Contain($"{Loc.Get("ui.crit_damage")}+5%").And.Contain($"{Loc.Get("ui.life_steal")}+4%");
            return Task.FromResult(t);
        });
        text.Should().NotContain("Str+").And.NotContain("HP+").And.NotContain("MR+").And.NotContain("CritDmg").And.NotContain("Lifesteal");
    }

    // ---------- 5. companion equipment headers at the Inn ----------

    [Theory]
    [InlineData("hu")]
    [InlineData("es")]
    public async Task InnCompanionHeaders_AreLocalized(string lang)
    {
        string text = await WithLanguage(lang, async () =>
        {
            var ally = new Character { Name1 = "Ally", Name2 = "Ally", Class = CharacterClass.Warrior, Level = 8, HP = 300, MaxHP = 300 };
            var (term, output) = Terminal("\n\n");
            var inn = At(new InnLocation(), term, Hero());
            await (Task)typeof(InnLocation).GetMethod("CompanionUnequipItemFromCharacter", F)!.Invoke(inn, new object[] { ally })!;
            var (term2, output2) = Terminal("Q\n\n\n");
            var inn2 = At(new InnLocation(), term2, Hero());
            await (Task)typeof(InnLocation).GetMethod("ManageCompanionCharacterEquipment", F)!.Invoke(inn2, new object[] { ally })!;
            string t = Shown(term, output) + Shown(term2, output2);
            Capture($"nearby-inn-{lang}.txt", t);
            t.Should().Contain(Loc.Get("team.unequip_header", "ALLY"));
            t.Should().Contain(Loc.Get("team.no_equipment_unequip", "Ally"));
            t.Should().Contain(Loc.Get("team.equip_header_label", "ALLY"));
            return t;
        });
        text.Should().NotContain("UNEQUIP FROM").And.NotContain("has no equipment to unequip").And.NotContain("EQUIPMENT: ALLY");
    }

    // ---------- 6. the slot list shown at Home ----------

    [Theory]
    [InlineData("hu")]
    [InlineData("es")]
    public async Task HomeSlotList_UnidentifiedAndLabels_AreLocalized(string lang)
    {
        string text = await WithLanguage(lang, () =>
        {
            var ally = new Character { Name1 = "Ally", Name2 = "Ally", Class = CharacterClass.Warrior, Level = 8, HP = 300, MaxHP = 300 };
            var mystery = new Equipment { Name = "Odd Ring", Slot = EquipmentSlot.LFinger, MinLevel = 1 };
            EquipmentDatabase.RegisterDynamic(mystery);
            mystery.IsIdentified = false;
            ally.EquippedItems[EquipmentSlot.LFinger] = mystery.Id;
            var (term, output) = Terminal();
            var home = At(new HomeLocation(), term, Hero());
            var show = typeof(BaseLocation).GetMethod("DisplayEquipmentSlotWithStats", F)!;
            foreach (var slot in new[] { EquipmentSlot.MainHand, EquipmentSlot.OffHand, EquipmentSlot.LFinger })
                show.Invoke(home, new object[] { ally, slot, GameConfig.GetLocalizedSlotName(slot) });
            string t = Shown(term, output);
            Capture($"nearby-home-slots-{lang}.txt", t);
            t.Should().Contain(Loc.Get("inn.equip_slot_unidentified"));
            // the colons line up: every label is padded to the longest slot name in this language
            var colons = t.Split('\n').Where(l => l.Contains(": ")).Select(l => l.IndexOf(": ", StringComparison.Ordinal)).Distinct().ToList();
            colons.Should().HaveCount(1, "the slot labels are padded to one width");
            return Task.FromResult(t);
        });
        text.Should().NotContain("Unidentified");
    }

    // ---------- 7. the slot picker fits 80 columns ----------

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public async Task SlotPicker_FitsEightyColumns(string lang)
    {
        string text = await WithLanguage(lang, async () =>
        {
            var ally = new Character { Name1 = "Ally", Name2 = "Ally", Class = CharacterClass.Warrior, Level = 8, HP = 300, MaxHP = 300 };
            // the longest names sit in both columns: Main Hand (left, row 1) and Right Ring (right, row 7)
            ally.EquippedItems[EquipmentSlot.MainHand] = EquipmentDatabase.RegisterDynamic(new Equipment
                { Name = "Sword of the Thirty Character Name", Slot = EquipmentSlot.MainHand, WeaponPower = 5, MinLevel = 1 });
            foreach (var slot in new[] { EquipmentSlot.LFinger, EquipmentSlot.RFinger, EquipmentSlot.Feet })
                ally.EquippedItems[slot] = EquipmentDatabase.RegisterDynamic(new Equipment
                    { Name = "Band of the Very Long Ring Name Here", Slot = slot, MinLevel = 1 });
            var (term, output) = Terminal("Q\n");
            var home = At(new HomeLocation(), term, Hero());
            var picked = await (Task<EquipmentSlot?>)typeof(BaseLocation).GetMethod("PromptForEquipmentSlot", F)!.Invoke(home, new object[] { ally })!;
            picked.Should().BeNull();
            string t = Shown(term, output);
            Capture($"nearby-slot-picker-{lang}.txt", t);
            return t;
        });
        var rows = text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => Regex.IsMatch(l, @"^\s+\d+\. ")).ToList();
        rows.Should().HaveCount(7);
        foreach (var row in rows)
            row.Length.Should().BeLessThanOrEqualTo(80, $"{lang}: \"{row}\"");
        // both slot labels of each row start at the same column on every row
        var labelsAt = rows.Select(r => Regex.Matches(r, @"\d+\. ").Select(m => m.Index + m.Length).ToArray()).ToList();
        labelsAt.Select(a => a[1]).Distinct().Should().HaveCount(1, "the right column lines up");
        foreach (var slot in new[] { EquipmentSlot.MainHand, EquipmentSlot.OffHand, EquipmentSlot.LFinger, EquipmentSlot.RFinger })
        {
            string label = Loc.GetIn(lang, $"equip.slot.{slot}");
            text.Should().Contain(label, "every slot name is shown whole");
        }
    }
}
