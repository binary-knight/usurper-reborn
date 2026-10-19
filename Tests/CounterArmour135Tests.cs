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
using UsurperRemake.Data;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.5 (CT-3): divine armour (Aurelion 25, Terravok 35, Manwe 50 percent off every player hit) falls only to that
/// god's counter enchant (Sunbane, Stonebane, Godbane) on the main or off hand. A plain shop enchant, a loot proc
/// flag, another god's counter or a weapon named Artifact, Godforged, Sunforged or Voidtouched leaves the full cut.
/// The pre-fight warning names the full cut, that it reaches spells, and the counter that removes it; with the
/// counter held it says the counter breaks the armour and shows no cut.
/// </summary>
[Collection("SharedGameSingletons")]
public class CounterArmour135Tests : IDisposable
{
    private const BindingFlags S = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };

    private static readonly Dictionary<OldGodType, double> Base = new()
    {
        [OldGodType.Aurelion] = GameConfig.AurelionDivineShield,
        [OldGodType.Terravok] = GameConfig.TerravokStoneSkin,
        [OldGodType.Manwe] = GameConfig.ManweCreatorsWard,
    };

    private readonly string _prevLanguage = GameConfig.Language;
    private readonly bool _prevPacing = Pacing.Disabled;

    public CounterArmour135Tests()
    {
        GameConfig.Language = "en";
        Pacing.Disabled = true;
    }

    public void Dispose()
    {
        GameConfig.Language = _prevLanguage;
        Pacing.Disabled = _prevPacing;
    }

    // ---------- helpers ----------

    private static string Code(int tier, int stat) =>
        (string)typeof(MagicShopLocation).GetMethod("GetEnchantKindCode", S)!.Invoke(null, new object[] { tier, stat })!;

    private static readonly Dictionary<string, int> Registered = new();

    /// <summary>A sword in the slot. tier 0 is plain; any other tier is written the way the shop writes it: the
    /// kind code (stat 2, Strength, for a stat tier), the enchant count and a named tier's effect.</summary>
    private static int Weapon(EquipmentSlot slot, string name = "Plain Blade", int tier = 0, bool fire = false)
    {
        string key = $"{slot}:{name}:{tier}:{fire}";
        if (Registered.TryGetValue(key, out int id)) return id;
        var item = new Equipment
        {
            Name = name, Slot = slot, WeaponType = WeaponType.Sword, WeaponPower = 100, MinLevel = 1,
            Rarity = EquipmentRarity.Common, Description = "", HasFireEnchant = fire,
        };
        if (tier > 0)
        {
            item.AddEnchantedKind(Code(tier, 2));
            item.IncrementEnchantmentCount();
            MagicShopLocation.ApplyNamedEnchant(item, tier);
        }
        id = EquipmentDatabase.RegisterDynamic(item);
        Registered[key] = id;
        return id;
    }

    private static int CounterTier(OldGodType god) =>
        MagicShopLocation.CounterTiers[Array.IndexOf(MagicShopLocation.CounterKindCodes, OldGodBossSystem.CounterKindCode(god))];

    private static Character Hero(int main, int? off = null)
    {
        var hero = OldGodBuild134Support.Hero();
        hero.EquippedItems[EquipmentSlot.MainHand] = main;
        if (off is int o) hero.EquippedItems[EquipmentSlot.OffHand] = o;
        hero.RecalculateStats();
        return hero;
    }

    private static Character Plain() => Hero(Weapon(EquipmentSlot.MainHand));

    private static Character WithCounter(OldGodType god, bool offHand = false) => offHand
        ? Hero(Weapon(EquipmentSlot.MainHand), Weapon(EquipmentSlot.OffHand, "Plain Dagger", CounterTier(god)))
        : Hero(Weapon(EquipmentSlot.MainHand, tier: CounterTier(god)));

    private static double Divine(OldGodType god, Character hero) =>
        (double)typeof(OldGodBossSystem).GetMethod("DivineArmorReduction", S)!.Invoke(null, new object[] { god, hero })!;

    public static IEnumerable<object[]> ArmouredGods() =>
        new[] { OldGodType.Aurelion, OldGodType.Terravok, OldGodType.Manwe }.Select(g => new object[] { g });

    // ---------- the armour ----------

    [Theory]
    [MemberData(nameof(ArmouredGods))]
    public void CounterArmour135_PlainAndPlainEnchantedWeapons_TakeTheFullCut(OldGodType god)
    {
        Base[god].Should().BeGreaterThan(0);
        Divine(god, Plain()).Should().Be(Base[god], $"{god}: a plain weapon");
        Divine(god, Hero(Weapon(EquipmentSlot.MainHand, tier: 1))).Should().Be(Base[god], $"{god}: a Minor (stat) enchant");
        Divine(god, Hero(Weapon(EquipmentSlot.MainHand, tier: 12))).Should().Be(Base[god], $"{god}: a Godforged (stat) enchant");
        Divine(god, Hero(Weapon(EquipmentSlot.MainHand, tier: 13))).Should().Be(Base[god], $"{god}: a Phoenix Fire (named) enchant");
        Divine(god, Hero(Weapon(EquipmentSlot.MainHand, tier: 9))).Should().Be(Base[god], $"{god}: a Lifedrinker (named) enchant");
        Divine(god, Hero(Weapon(EquipmentSlot.MainHand, fire: true))).Should().Be(Base[god], $"{god}: a loot fire proc");
        Divine(god, Hero(Weapon(EquipmentSlot.MainHand), Weapon(EquipmentSlot.OffHand, "Plain Dagger", 13)))
            .Should().Be(Base[god], $"{god}: an enchanted off hand");
    }

    [Theory]
    [MemberData(nameof(ArmouredGods))]
    public void CounterArmour135_TheGodsOwnCounter_OnEitherHand_RemovesIt(OldGodType god)
    {
        Divine(god, WithCounter(god)).Should().Be(0, $"{god}: its counter on the main hand");
        Divine(god, WithCounter(god, offHand: true)).Should().Be(0, $"{god}: its counter on the off hand");
        // the fight reads the same figure
        OldGodBossSystem.BuildBossFight(god, 0, BossDialogue.None, WithCounter(god)).Context.DivineArmorReduction.Should().Be(0);
        OldGodBossSystem.BuildBossFight(god, 0, BossDialogue.None, Plain()).Context.DivineArmorReduction.Should().Be(Base[god]);
    }

    [Theory]
    [MemberData(nameof(ArmouredGods))]
    public void CounterArmour135_AnotherGodsCounter_LeavesTheFullCut(OldGodType god)
    {
        foreach (var other in Base.Keys.Where(g => g != god))
        {
            Divine(god, WithCounter(other)).Should().Be(Base[god], $"{god} against {other}'s counter on the main hand");
            Divine(god, WithCounter(other, offHand: true)).Should().Be(Base[god], $"{god} against {other}'s counter on the off hand");
        }
    }

    [Theory]
    [MemberData(nameof(ArmouredGods))]
    public void CounterArmour135_AWeaponsName_NoLongerBypassesIt(OldGodType god)
    {
        foreach (var name in new[] { "Artifact Blade", "Godforged Edge", "Sunforged Sword", "Voidtouched Axe" })
        {
            Divine(god, Hero(Weapon(EquipmentSlot.MainHand, name))).Should().Be(Base[god], $"{god}: a main hand named {name}");
            Divine(god, Hero(Weapon(EquipmentSlot.MainHand), Weapon(EquipmentSlot.OffHand, name)))
                .Should().Be(Base[god], $"{god}: an off hand named {name}");
        }
    }

    [Fact]
    public void CounterArmour135_GodsWithoutABase_StayAtZero()
    {
        foreach (OldGodType god in Enum.GetValues(typeof(OldGodType)))
        {
            if (Base.ContainsKey(god)) continue;
            Divine(god, Plain()).Should().Be(0, god.ToString());
            Divine(god, Hero(Weapon(EquipmentSlot.MainHand, "Artifact Blade", 13))).Should().Be(0, god.ToString());
            foreach (var counter in Base.Keys)
                Divine(god, WithCounter(counter)).Should().Be(0, $"{god} with {counter}'s counter");
        }
    }

    // ---------- the warning ----------

    private static string Intro(OldGodType god, Character hero)
    {
        var data = OldGodsData.GetGodBossData(god);
        var output = new MemoryStream();
        var term = new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes("\n")), output);
        ((Task)typeof(OldGodBossSystem).GetMethod("PlayBossIntroduction", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(OldGodBossSystem.Instance, new object[] { data, hero, term })!).GetAwaiter().GetResult();
        term.StreamWriterInternal!.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "").Replace("\r", "");
    }

    private static string Norm(string s) => Regex.Replace(s, @"\s+", " ").Trim();

    private static string CutLine(OldGodType god) => Loc.Get("old_god.warning_less_damage", $"{Base[god] * 100:N0}");
    private static string CounterLine(OldGodType god) => Loc.Get("old_god.warning_enchant", OldGodBossSystem.CounterTierName(god));
    private static string BreaksLine(OldGodType god) => Loc.Get("old_god.counter_breaks_armour", OldGodBossSystem.CounterTierName(god));

    [Theory]
    [InlineData("en", "spells included")]
    [InlineData("hu", "varázslatokat is")]
    public void CounterArmour135_Warning_WithoutTheCounter_NamesTheFullCut_Spells_AndTheCounter(string lang, string spells)
    {
        GameConfig.Language = lang;
        foreach (var god in Base.Keys)
        {
            string tier = OldGodBossSystem.CounterTierName(god);
            tier.Should().NotBeEmpty();
            foreach (var hero in new[] { Plain(), Hero(Weapon(EquipmentSlot.MainHand, tier: 13)), WithCounter(Base.Keys.First(g => g != god)) })
            {
                string shown = Norm(Intro(god, hero));
                CutLine(god).Should().Contain($"{Base[god] * 100:N0}%").And.Contain(spells, $"{lang} {god}: the cut reaches spells");
                CounterLine(god).Should().Contain(tier).And.Contain(Loc.Get("location.magic_shop"), $"{lang} {god}: the counter by name, at the Magic Shop");
                shown.Should().Contain(Norm(CutLine(god)), $"{lang} {god}: the full cut");
                shown.Should().Contain(Norm(CounterLine(god)), $"{lang} {god}: the counter that removes it");
                shown.Should().NotContain(Norm(BreaksLine(god)), $"{lang} {god}: no counter held");
            }
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void CounterArmour135_Warning_WithTheCounter_SaysItBreaksTheArmour_AndShowsNoCut(string lang)
    {
        GameConfig.Language = lang;
        foreach (var god in Base.Keys)
            foreach (bool offHand in new[] { false, true })
            {
                string shown = Norm(Intro(god, WithCounter(god, offHand)));
                BreaksLine(god).Should().Contain(OldGodBossSystem.CounterTierName(god));
                shown.Should().Contain(Norm(BreaksLine(god)), $"{lang} {god}: the breaks line");
                shown.Should().NotContain(Norm(CutLine(god)), $"{lang} {god}: no cut line");
                shown.Should().NotContain(Norm(CounterLine(god)), $"{lang} {god}: no counter hint");
            }
    }

    [Fact]
    public void CounterArmour135_Warning_GodsWithoutABase_ShowNoArmourLines()
    {
        foreach (var god in new[] { OldGodType.Maelketh, OldGodType.Veloura, OldGodType.Thorgrim })
        {
            string shown = Norm(Intro(god, WithCounter(OldGodType.Terravok)));
            shown.Should().NotContain("spells included", god.ToString());
            shown.Should().NotContain(Norm(CounterLine(OldGodType.Terravok)), god.ToString());
            shown.Should().NotContain(Norm(BreaksLine(OldGodType.Terravok)), god.ToString());
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public void CounterArmour135_WarningRows_FitWithTheLongestTierName(string lang)
    {
        GameConfig.Language = lang;
        foreach (var god in Base.Keys)
            foreach (var row in new[] { CutLine(god), CounterLine(god), BreaksLine(god) })
            {
                row.Should().NotContain("{0}", lang);
                if (lang == "en" || lang == "hu")
                    row.TrimEnd().Length.Should().BeLessOrEqualTo(79, $"{lang} row fits: [{row}]");
            }
    }

    // ---------- the text ----------

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test must run inside the repository");
        return dir!.FullName;
    }

    [Fact]
    public void CounterArmour135_Keys_AreInEveryLanguage_FilledAndTranslated()
    {
        var files = Langs.ToDictionary(l => l, l =>
            JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(Path.Combine(RepoRoot(), "Localization", l + ".json")))!);
        foreach (var key in new[] { "old_god.warning_less_damage", "old_god.warning_enchant", "old_god.counter_breaks_armour" })
        {
            string en = files["en"][key].GetString()!;
            foreach (var lang in Langs)
            {
                files[lang].Should().ContainKey(key, lang);
                string text = files[lang][key].GetString()!;
                text.Should().Contain("{0}", $"{lang} {key}");
                text.Should().NotMatchRegex("[\u2013\u2014]", $"{lang} {key}: no dash");
                if (lang != "en") text.Should().NotBe(en, $"{lang} {key} is translated");
            }
        }
        files["en"]["old_god.warning_enchant"].GetString().Should().NotContain("unenchanted").And.NotContain("Find an enchanted");
        files["en"]["old_god.warning_less_damage"].GetString().Should().NotContain("unenchanted");
    }
}
