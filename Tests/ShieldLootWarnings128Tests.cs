using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.8: the loot comparison for a shield says when the shield would move out the weapon the
/// class's spells or abilities need, prints the shield's own Shield Bonus against an off-hand
/// weapon, and compares a new shield's own Shield Bonus and Block Chance against the one worn.
/// </summary>
[Collection("SharedGameSingletons")]
public class ShieldLootWarnings128Tests
{
    private static string Render(Character hero, Item loot, string lang)
    {
        var prev = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            var output = new MemoryStream();
            var term = new TerminalEmulator(new MemoryStream(), output);
            CombatEngine.ShowEquipmentComparison(term, loot, hero);
            term.StreamWriterInternal?.Flush();
            string text = Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");
            foreach (var line in text.Replace("\r", "").Split('\n'))
                line.Length.Should().BeLessThanOrEqualTo(79, "{0} row fits 79 columns: [{1}]", lang, line);
            return text;
        }
        finally { GameConfig.Language = prev; }
    }

    private static string InLang(string lang, Func<string> f)
    {
        var prev = GameConfig.Language;
        try { GameConfig.Language = lang; return f(); }
        finally { GameConfig.Language = prev; }
    }

    private static Character Hero(CharacterClass cls) => new()
    {
        Name1 = "tester", Name2 = "Tester", Class = cls, Level = 12, HP = 500, MaxHP = 500, AI = CharacterAI.Human,
    };

    private static int Weapon(string name, WeaponType type, WeaponHandedness hands, int power, EquipmentSlot slot) =>
        EquipmentDatabase.RegisterDynamic(new Equipment
        {
            Name = name, Slot = slot, WeaponType = type, Handedness = hands, WeaponPower = power, MinLevel = 1,
        });

    private static Item LootShield() => new()
    {
        Name = "Leather Shield", Type = ObjType.Shield, ShieldBonus = 10, BlockChance = 8, Armor = 3, Value = 100,
    };

    private static Character MagicianWithStaff()
    {
        var hero = Hero(CharacterClass.Magician);
        hero.EquippedItems[EquipmentSlot.MainHand] = Weapon("Fine Wooden Staff", WeaponType.Staff, WeaponHandedness.TwoHanded, 12, EquipmentSlot.MainHand);
        return hero;
    }

    private static Character AssassinWithOffhandDagger(WeaponType mainType)
    {
        var hero = Hero(CharacterClass.Assassin);
        hero.EquippedItems[EquipmentSlot.MainHand] = Weapon("Main Blade", mainType, WeaponHandedness.OneHanded, 9, EquipmentSlot.MainHand);
        hero.EquippedItems[EquipmentSlot.OffHand] = Weapon("Dagger", WeaponType.Dagger, WeaponHandedness.OneHanded, 6, EquipmentSlot.OffHand);
        return hero;
    }

    private static string SpellWarning(string lang) => InLang(lang, () =>
        Loc.Get("combat.loot_spell_req_warning", GameConfig.GetLocalizedClassName(CharacterClass.Magician), BaseLocation.WeaponTypeLabel(WeaponType.Staff)));
    private static string SpellBlock(string lang) => InLang(lang, () =>
        Loc.Get("combat.loot_spell_req_block_shield", BaseLocation.WeaponTypeLabel(WeaponType.Staff)));
    private static string AbilityWarning(string lang) => InLang(lang, () =>
        Loc.Get("combat.loot_ability_req_warning", "Tester", BaseLocation.WeaponTypeLabel(WeaponType.Dagger)));
    private static string AbilityBlock(string lang) => InLang(lang, () =>
        Loc.Get("combat.loot_ability_req_block_shield", BaseLocation.WeaponTypeLabel(WeaponType.Dagger)));

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void Shield_DisplacingTheTwoHandedStaff_ShowsTheSpellWarning(string lang)
    {
        string text = Render(MagicianWithStaff(), LootShield(), lang);
        text.Should().Contain(SpellWarning(lang));
        text.Should().Contain(SpellBlock(lang));
        text.Should().NotContain(AbilityBlock(lang));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void Shield_DisplacingTheOnlyDagger_ShowsTheAbilityWarning(string lang)
    {
        string text = Render(AssassinWithOffhandDagger(WeaponType.Sword), LootShield(), lang);
        text.Should().Contain(AbilityWarning(lang));
        text.Should().Contain(AbilityBlock(lang));
        text.Should().NotContain(SpellBlock(lang));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void Shield_LeavingADaggerInTheMainHand_ShowsNeitherWarning(string lang)
    {
        string text = Render(AssassinWithOffhandDagger(WeaponType.Dagger), LootShield(), lang);
        text.Should().NotContain(AbilityWarning(lang));
        text.Should().NotContain(AbilityBlock(lang));
        text.Should().NotContain(SpellBlock(lang));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void Shield_ReplacingAnOffhandWeapon_PrintsItsShieldBonus(string lang)
    {
        var hero = Hero(CharacterClass.Warrior);
        hero.EquippedItems[EquipmentSlot.MainHand] = Weapon("Short Sword", WeaponType.Sword, WeaponHandedness.OneHanded, 9, EquipmentSlot.MainHand);
        hero.EquippedItems[EquipmentSlot.OffHand] = Weapon("Off Sword", WeaponType.Sword, WeaponHandedness.OneHanded, 7, EquipmentSlot.OffHand);
        string text = Render(hero, LootShield(), lang);
        text.Should().Contain(InLang(lang, () => Loc.Get("combat.offhand_replaced_by_shield", 7, 10)), "the shield's Shield Bonus is 10");
        text.Should().NotContain(InLang(lang, () => Loc.Get("combat.offhand_replaced_by_shield", 7, 3)), "Armor 3 is not the shield's value");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void ShieldVsShield_ComparesTheNewShieldsOwnValues(string lang)
    {
        var hero = Hero(CharacterClass.Warrior);
        hero.EquippedItems[EquipmentSlot.OffHand] = EquipmentDatabase.RegisterDynamic(new Equipment
        {
            Name = "Old Buckler", Slot = EquipmentSlot.OffHand, WeaponType = WeaponType.Buckler,
            Handedness = WeaponHandedness.OffHandOnly, ShieldBonus = 5, BlockChance = 5, MinLevel = 1,
        });
        string text = Render(hero, LootShield(), lang);
        // worn 5 + 5 = 10; new 10 + 8 = 18
        text.Should().Contain(InLang(lang, () => Loc.Get("combat.compare_stat_total", 10, 18)));
        text.Should().Contain(InLang(lang, () => Loc.Get("combat.upgrade", 8)));
    }
}
