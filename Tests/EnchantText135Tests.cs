using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Data;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.5 (EF-9): the enchant text rows. The gear detail labels, the teammate summary's mana steal label and the
/// Black Market's enchant names are keyed in five languages; the two enchant achievements say what they count;
/// Terravok's Stone Skin line carries the code's cut. Every changed row fits 79 columns in English and Hungarian.
/// </summary>
[Collection("SharedGameSingletons")]
public class EnchantText135Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags FS = BindingFlags.NonPublic | BindingFlags.Static;
    private const int MaxWidth = 79;
    private static readonly string[] Others = { "es", "fr", "hu", "it" };
    private static readonly string[] All = { "en", "es", "fr", "hu", "it" };

    private static readonly string[] GearKeys =
    {
        "base.gear_sp_critdmg", "base.gear_sp_lifesteal", "base.gear_sp_manasteal", "base.gear_sp_armorpen", "base.gear_sp_thorns",
        "base.gear_sp_hpregen", "base.gear_sp_mpregen", "base.gear_sp_poison_value", "base.gear_sp_magres", "base.gear_sp_fire",
        "base.gear_sp_frost", "base.gear_sp_lightning", "base.gear_sp_poison", "base.gear_sp_holy", "base.gear_sp_shadow",
        "base.gear_sp_cursed",
    };

    private static readonly string[] ChangedKeys = GearKeys.Concat(new[]
    {
        "ui.stat_siphon", "achievement.first_enchant.desc", "achievement.master_enchanter.desc",
    }).ToArray();

    // ---------- helpers ----------

    private static string L(string lang, string key, params object[] args) => Loc.GetIn(lang, key, args);

    private static (TerminalEmulator term, MemoryStream output) Term(string input = "")
    {
        var output = new MemoryStream();
        return (new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes(input)), output), output);
    }

    private static string Shown(TerminalEmulator term, MemoryStream output)
    {
        term.StreamWriterInternal?.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "");
    }

    private static List<string> Rows(string text) => text.Replace("\r", "").Split('\n').ToList();

    private static T InLanguage<T>(string lang, Func<T> body)
    {
        var prev = GameConfig.Language;
        bool sr = GameConfig.ScreenReaderMode;
        try
        {
            GameConfig.Language = lang;
            GameConfig.ScreenReaderMode = false;
            return body();
        }
        finally { GameConfig.Language = prev; GameConfig.ScreenReaderMode = sr; }
    }

    private static T At<T>(T location, TerminalEmulator term, Character hero) where T : BaseLocation
    {
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(location, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(location, hero);
        return location;
    }

    private sealed class Plain : BaseLocation
    {
        public Plain() : base(GameLocation.TheInn, "The Inn", "") { }
        protected override List<NPC> GetLiveNPCsAtLocation() => new();
    }

    private static Character Hero() => new()
    {
        Name1 = "tester", Name2 = "Aranyszivu Hosszunevu Kalandor", Class = CharacterClass.Warrior, Level = 100,
        HP = 1000, MaxHP = 1000, Mana = 100, MaxMana = 100, AI = CharacterAI.Human,
    };

    /// <summary>A hero whose gear carries every special the gear detail screen labels, spread over four items as
    /// enchanted loot carries them (no single item holds them all).</summary>
    private static Character Enchanted()
    {
        var h = Hero();
        void Wear(EquipmentSlot slot, Equipment e)
        {
            e.Name = "Test " + slot; e.Slot = slot; e.MinLevel = 1; e.IsIdentified = true;
            h.EquippedItems[slot] = EquipmentDatabase.RegisterDynamic(e);
        }
        Wear(EquipmentSlot.MainHand, new Equipment
        {
            WeaponPower = 300, CriticalChanceBonus = 5, CriticalDamageBonus = 10, LifeSteal = 6, ManaSteal = 4,
            HasFireEnchant = true, HasFrostEnchant = true,
        });
        Wear(EquipmentSlot.OffHand, new Equipment { WeaponPower = 100, ArmorPiercing = 12, PoisonDamage = 245, HasPoisonEnchant = true, HasHolyEnchant = true });
        Wear(EquipmentSlot.Body, new Equipment { ArmorClass = 100, Thorns = 15, HPRegen = 17, ManaRegen = 9, MagicResistance = 20 });
        Wear(EquipmentSlot.LFinger, new Equipment { HasLightningEnchant = true, HasShadowEnchant = true, IsCursed = true });
        return h;
    }

    private static string GearScreen(string lang) => InLanguage(lang, () =>
    {
        var (term, output) = Term();
        var loc = At(new Plain(), term, Enchanted());
        typeof(BaseLocation).GetMethod("ShowDetailedGear", F)!.Invoke(loc, new object?[] { null });
        return Shown(term, output);
    });

    private static readonly (string key, object[] args)[] GearLabels =
    {
        ("base.gear_sp_critdmg", new object[] { 10 }), ("base.gear_sp_lifesteal", new object[] { 6 }),
        ("base.gear_sp_manasteal", new object[] { 4 }), ("base.gear_sp_armorpen", new object[] { 12 }),
        ("base.gear_sp_thorns", new object[] { 15 }), ("base.gear_sp_hpregen", new object[] { "1.7" }),
        ("base.gear_sp_mpregen", new object[] { "0.9" }), ("base.gear_sp_poison_value", new object[] { 245 }),
        ("base.gear_sp_magres", new object[] { 20 }), ("base.gear_sp_fire", Array.Empty<object>()),
        ("base.gear_sp_frost", Array.Empty<object>()), ("base.gear_sp_lightning", Array.Empty<object>()),
        ("base.gear_sp_poison", Array.Empty<object>()), ("base.gear_sp_holy", Array.Empty<object>()),
        ("base.gear_sp_shadow", Array.Empty<object>()), ("base.gear_sp_cursed", Array.Empty<object>()),
    };

    // ---------- the keys ----------

    [Fact]
    public void EveryChangedKey_IsFilledInFiveLanguages_AndTranslated()
    {
        foreach (var key in ChangedKeys)
        {
            string en = L("en", key);
            foreach (var lang in All)
            {
                Loc.HasIn(lang, key).Should().BeTrue($"{key} is in {lang}");
                L(lang, key).Should().NotBeNullOrWhiteSpace($"{key} is filled in {lang}");
                Regex.Matches(L(lang, key), @"\{\d\}").Count.Should().Be(Regex.Matches(en, @"\{\d\}").Count, $"{key} {lang} keeps the placeholders");
            }
            foreach (var lang in Others)
                L(lang, key).Should().NotBe(en, $"{key} is translated in {lang}");
        }
    }

    [Fact]
    public void GearLabels_KeepTheirEnglish()
    {
        // The labels moved from the screen code: the English is what the screen showed before.
        var expected = new Dictionary<string, string>
        {
            ["base.gear_sp_critdmg"] = "CritDmg +{0}%", ["base.gear_sp_lifesteal"] = "Lifesteal {0}%",
            ["base.gear_sp_manasteal"] = "Manasteal {0}%", ["base.gear_sp_armorpen"] = "ArmorPen {0}%",
            ["base.gear_sp_thorns"] = "Thorns {0}%", ["base.gear_sp_hpregen"] = "HPRegen {0}%/rd",
            ["base.gear_sp_mpregen"] = "MPRegen {0}%/rd", ["base.gear_sp_poison_value"] = "Poison {0}",
            ["base.gear_sp_magres"] = "MagRes {0}%", ["base.gear_sp_fire"] = "Fire", ["base.gear_sp_frost"] = "Frost",
            ["base.gear_sp_lightning"] = "Lightning", ["base.gear_sp_poison"] = "Poison", ["base.gear_sp_holy"] = "Holy",
            ["base.gear_sp_shadow"] = "Shadow", ["base.gear_sp_cursed"] = "CURSED", ["ui.stat_siphon"] = "Siphon",
        };
        foreach (var (key, en) in expected)
            Loc.GetIn("en", key).Should().Be(en, key);
    }

    // ---------- the gear detail screen ----------

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public void GearDetail_ShowsEveryKeyedLabel_AndFits(string lang)
    {
        string shown = GearScreen(lang);
        foreach (var (key, args) in GearLabels)
            shown.Should().Contain(L(lang, key, args), key);
        shown.Should().Contain($"{L(lang, "ui.stat_crit")} +5%");
        foreach (var row in Rows(shown).Where(r => r.Length > 0 && "╔║╠╚═".IndexOf(r[0]) < 0))
            row.Length.Should().BeLessOrEqualTo(MaxWidth, $"every gear row fits in {lang}: \"{row}\"");
    }

    [Fact]
    public void GearDetail_InHungarian_ShowsNoEnglishLabel()
    {
        string shown = GearScreen("hu");
        foreach (var english in new[] { "CritDmg", "Lifesteal", "Manasteal", "ArmorPen", "Thorns", "HPRegen", "MagRes", "Fire", "Frost",
                     "Lightning", "Poison", "Holy", "Shadow", "CURSED", "/rd" })
            shown.Should().NotContain(english);
    }

    // ---------- the teammate auto-equip summary ----------

    private static string Summary(string lang, Equipment e) => InLanguage(lang, () =>
        (string)typeof(CombatEngine).GetMethod("BuildEquipmentStatSummary", FS)!.Invoke(null, new object[] { e })!);

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public void TeammateSummary_ShowsTheKeyedManaStealLabel(string lang)
    {
        string s = Summary(lang, new Equipment { Name = "Siphoning Blade", WeaponPower = 50, LifeSteal = 6, ManaSteal = 4 });
        s.Should().Contain($"{L(lang, "ui.stat_siphon")}:4%");
        if (lang != "en") s.Should().NotContain("Siphon");
        s.Length.Should().BeLessOrEqualTo(MaxWidth);
    }

    // ---------- the Black Market inspect ----------

    private static string Inspect(string lang, Item item) => InLanguage(lang, () =>
    {
        var (term, output) = Term("\n\n\n");
        var alley = At(new DarkAlleyLocation(), term, Hero());
        var r = typeof(DarkAlleyLocation).GetMethod("ShowBlackMarketItemDetail", F)!.Invoke(alley, new object[] { item, 1000L });
        if (r is Task t) t.GetAwaiter().GetResult();
        return Shown(term, output);
    });

    [Fact]
    public void EveryLootEffect_HasANameKey_InFiveLanguages()
    {
        foreach (LootGenerator.SpecialEffect e in Enum.GetValues(typeof(LootGenerator.SpecialEffect)))
        {
            if (e == LootGenerator.SpecialEffect.None) continue;
            string key = LootGenerator.EffectWordKey(e, "name");
            foreach (var lang in All)
                Loc.HasIn(lang, key).Should().BeTrue($"{key} in {lang}");
        }
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public void BlackMarketInspect_ShowsTheEffectNames_NotTheEnumNames(string lang)
    {
        var effects = new[]
        {
            LootGenerator.SpecialEffect.FireDamage, LootGenerator.SpecialEffect.LifeSteal, LootGenerator.SpecialEffect.MagicResist,
            LootGenerator.SpecialEffect.CriticalStrike, LootGenerator.SpecialEffect.TitanResolve,
        };
        var item = new Item { Name = "Test Blade", Type = ObjType.Weapon, Attack = 50, Value = 1000 };
        item.LootEffects = effects.Select(e => ((int)e, 5)).ToList();
        string shown = Inspect(lang, item);
        foreach (var e in effects)
        {
            shown.Should().Contain($"- {L(lang, LootGenerator.EffectWordKey(e, "name"))} (5)", e.ToString());
            shown.Should().NotContain($"- {e} (", "the enum identifier is not shown");
        }
        foreach (var row in Rows(shown))
            row.Length.Should().BeLessOrEqualTo(MaxWidth, $"\"{row}\"");
    }

    // ---------- the achievements ----------

    [Fact]
    public void EnchantAchievements_SayWhatTheyCount_AndStayKeyed()
    {
        var all = AchievementSystem.GetBuiltInAchievements();
        foreach (var id in new[] { "first_enchant", "master_enchanter" })
        {
            var a = all.Single(x => x.Id == id);
            Loc.GetIn("en", $"achievement.{id}.desc").Should().Be(a.Description, "the English key equals the data, so the key is shown");
            a.Description.Should().NotContain("equipped", "the Magic Shop counts bag items as well as worn ones");
            InLanguage("hu", () => a.LocDescription()).Should().Be(L("hu", $"achievement.{id}.desc"));
            foreach (var lang in Others)
                L(lang, $"achievement.{id}.desc").Should().NotContain(lang switch
                {
                    "es" => "equipad", "fr" => "équipé", "hu" => "felszerelt", _ => "equipaggiat",
                }, $"{id} {lang} no longer says equipped");
        }
        L("en", "achievement.master_enchanter.desc").Should().Contain("10");
        foreach (var lang in Others) L(lang, "achievement.master_enchanter.desc").Should().Contain("10");
    }

    // ---------- Terravok's Stone Skin ----------

    [Fact]
    public void StoneSkinLine_CarriesTheCodesCut_AndTheCounter()
    {
        var terravok = OldGodsData.GetGodBossData(OldGodType.Terravok);
        string line = terravok.SpecialMechanics["StoneSkin"];
        int percent = (int)Math.Round(GameConfig.TerravokStoneSkin * 100);
        line.Should().Contain($"{percent}%");
        line.Should().NotContain("50%");
        line.Should().Contain(Loc.GetIn("en", "magic_shop.tier_stonebane"));
    }
}
