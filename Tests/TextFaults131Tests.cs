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
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.3.1: six texts say what the code does (the Ocean's Touch and Divine Blessing enchant lines, the Mountain
/// Goat passive, the Ocean's Blessing, Harmonic Resonance and Probability Manipulation Active Buffs lines), and the
/// Active Buffs block opens for every class that has a passive line in it (Bard, Assassin, Mystic Shaman too).
/// </summary>
[Collection("SharedGameSingletons")]
public class TextFaults131Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int MaxWidth = 79;
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };

    private static readonly string[] SixKeys =
    {
        "magic_shop.tier_desc_ocean", "magic_shop.tier_desc_all_stats", "beast.mountain_goat.passive",
        "base.buff_oceans_blessing", "base.buff_harmonic_resonance", "base.buff_probability",
    };

    private static string L(string lang, string key, params object[] args) => Loc.GetIn(lang, key, args);

    private static string[] Placeholders(string text) =>
        Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).Distinct().OrderBy(x => x).ToArray();

    // ---------- 1. no stale claim, rendered with its arguments ----------

    private static readonly Dictionary<string, string[]> Stale = new()
    {
        ["en"] = new[] { "mana", "all stats", "descend", "spell" },
        ["es"] = new[] { "maná", "todos los atributos", "descenso", "hechizo" },
        ["fr"] = new[] { "mana", "toutes les caract", "descente", "et sorts" },
        ["hu"] = new[] { "mana", "minden jellemz", "ereszked", "varázslat" },
        ["it"] = new[] { "mana", "tutte le statistiche", "discesa", "incantesim" },
    };

    private static readonly Dictionary<string, string> RoomWord = new()
    {
        ["en"] = "room", ["es"] = "sala", ["fr"] = "salle", ["hu"] = "terem", ["it"] = "stanza",
    };

    private static object[] ArgsFor(string key) => key switch
    {
        "magic_shop.tier_desc_all_stats" => new object[] { 3 },
        "base.buff_oceans_blessing" => new object[] { (int)(GameConfig.TideswornOceansBlessingBonus * 100) },
        "base.buff_harmonic_resonance" => new object[] { (int)(GameConfig.WavecallerHarmonicResonanceBonus * 100) },
        "base.buff_probability" => new object[] { (int)(GameConfig.CyclebreakerDebuffResistChance * 100), GameConfig.CyclebreakerPassiveEvadePercent },
        _ => Array.Empty<object>(),
    };

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void SixTexts_HoldNoStaleClaim_AndRenderWithTheirArguments(string lang)
    {
        var stale = Stale[lang];
        foreach (var key in SixKeys)
        {
            string raw = Loc.GetIn(lang, key);
            string shown = L(lang, key, ArgsFor(key));
            shown.Should().NotContain("{", $"{lang} {key} renders with its arguments filled");
            shown.Should().NotBe(key);
            // "mana" is checked in the ocean line, the others per the key they could appear in
            switch (key)
            {
                case "magic_shop.tier_desc_ocean":
                    raw.ToLowerInvariant().Should().NotContain(stale[0], $"{lang} {key}: no mana, the code grants none");
                    shown.Should().Contain("6").And.Contain("4");
                    break;
                case "magic_shop.tier_desc_all_stats":
                    raw.ToLowerInvariant().Should().NotContain(stale[1], $"{lang} {key}: not every stat");
                    shown.Should().Contain("+3");
                    break;
                case "beast.mountain_goat.passive":
                    raw.ToLowerInvariant().Should().NotContain(stale[2], $"{lang} {key}: a room, not a descent");
                    raw.ToLowerInvariant().Should().Contain(RoomWord[lang]);
                    break;
                case "base.buff_oceans_blessing":
                case "base.buff_harmonic_resonance":
                    raw.ToLowerInvariant().Should().NotContain(stale[3], $"{lang} {key}: abilities only, no spell path reads it");
                    shown.Should().Contain("25%");
                    break;
                case "base.buff_probability":
                    shown.Should().Contain("25%").And.Contain("18%");
                    break;
            }
        }
    }

    // ---------- R2: the same placeholders in every language as in English ----------

    [Theory]
    [InlineData("magic_shop.tier_desc_ocean")] [InlineData("magic_shop.tier_desc_all_stats")]
    [InlineData("beast.mountain_goat.passive")] [InlineData("base.buff_oceans_blessing")]
    [InlineData("base.buff_harmonic_resonance")] [InlineData("base.buff_probability")]
    public void SixTexts_CarrySameWordsAsEnglish_InPlaceholders(string key)
    {
        var en = Placeholders(Loc.GetIn("en", key));
        foreach (var lang in Langs)
            Placeholders(Loc.GetIn(lang, key)).Should().Equal(en, $"{lang} {key} takes the same arguments as English");
        if (key == "base.buff_probability") en.Should().Equal("{0}", "{1}");
    }

    // ---------- 2. fit 79 columns ----------

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public void BuffRows_Wrapped_FitIn79Columns(string lang)
    {
        foreach (var key in SixKeys.Where(k => k.StartsWith("base.")))
        {
            var rows = BaseLocation.BuffRows(L(lang, key, ArgsFor(key)));
            foreach (var r in rows) r.Length.Should().BeLessOrEqualTo(MaxWidth, $"{lang} {key}: \"{r}\"");
        }
    }

    // ---------- 3. the Status screen ----------

    private sealed class Plain : BaseLocation
    {
        public Plain() : base(GameLocation.TheInn, "The Inn", "") { }
        protected override List<NPC> GetLiveNPCsAtLocation() => new();
    }

    private static Character Hero(CharacterClass cls) => new()
    {
        Name1 = "tester", Name2 = "Tester", Class = cls, Level = 50, HP = 5000, MaxHP = 5000, Mana = 500, MaxMana = 500,
        AI = CharacterAI.Human, Mental = 100, Gold = 1000,
    };

    private static string Strip(string s) => Regex.Replace(s, "\u001b\\[[0-9;?]*[A-Za-z]", "");

    private static string Status(string lang, Character hero)
    {
        var prev = GameConfig.Language;
        bool sr = GameConfig.ScreenReaderMode;
        try
        {
            GameConfig.Language = lang;
            GameConfig.ScreenReaderMode = false;
            var output = new MemoryStream();
            var term = new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("\n", 20)))), output);
            var loc = new Plain();
            typeof(BaseLocation).GetField("terminal", F)!.SetValue(loc, term);
            typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(loc, hero);
            var r = typeof(BaseLocation).GetMethod("ShowStatus", F)!.Invoke(loc, null);
            if (r is Task t) t.GetAwaiter().GetResult();
            term.StreamWriterInternal?.Flush();
            return Strip(Encoding.UTF8.GetString(output.ToArray()));
        }
        finally { GameConfig.Language = prev; GameConfig.ScreenReaderMode = sr; }
    }

    private static List<string> Rows(string text) => text.Replace("\r", "").Split('\n').ToList();

    private static List<string> BuffBlock(string shown, string lang)
    {
        var rows = Rows(shown);
        int at = rows.FindIndex(r => r == L(lang, "base.stat_active_buffs"));
        at.Should().BeGreaterOrEqualTo(0, "the Active Buffs block is drawn");
        return rows.Skip(at + 1).TakeWhile(r => r.Length > 0).ToList();
    }

    [Theory]
    [InlineData(CharacterClass.Bard, "base.buff_bardic")]
    [InlineData(CharacterClass.Assassin, "base.buff_lethal_precision")]
    [InlineData(CharacterClass.MysticShaman, "base.buff_elemental_mastery")]
    public void Status_ShowsThePassiveLine_ForAClassWithNoOtherBuff(CharacterClass cls, string key)
    {
        foreach (var lang in new[] { "en", "hu" })
        {
            string shown = Status(lang, Hero(cls));
            var block = BuffBlock(shown, lang);
            string expected = key switch
            {
                "base.buff_bardic" => L(lang, key, GameConfig.BardInspirationChance, GameConfig.BardInspirationAttackBonus),
                "base.buff_lethal_precision" => L(lang, key, (int)(GameConfig.AssassinLethalPrecisionCritBonus * 100), (int)(GameConfig.AssassinLethalPrecisionPoisonBonus * 100)),
                _ => L(lang, key, (int)(GameConfig.ShamanElementalMastery * 100)),
            };
            block.Should().Contain(BaseLocation.BuffRows(expected)[0], $"{lang} {cls} sees its passive line");
        }
    }

    [Fact]
    public void Status_ACyclebreakerSeesTheEvadeNumber()
    {
        string shown = Status("en", Hero(CharacterClass.Cyclebreaker));
        string joined = string.Join(" ", BuffBlock(shown, "en").Select(r => r.Trim()));
        joined.Should().Contain("18% to evade each attack");
    }

    [Fact]
    public void Status_AClassWithNoPassiveLineAndNoBuff_GetsNoBuffBlock()
    {
        string shown = Status("en", Hero(CharacterClass.Warrior));
        Rows(shown).Should().NotContain(L("en", "base.stat_active_buffs"));
    }

    // ---------- 4. the enchant shop row for the two tiers ----------

    private static string EnchantRows(string lang, int level)
    {
        var prev = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            var output = new MemoryStream();
            var term = new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("\n", 20)))), output);
            var shop = new MagicShopLocation();
            var hero = Hero(CharacterClass.Magician);
            hero.Level = level; hero.Gold = 9_000_000_000;
            typeof(BaseLocation).GetField("terminal", F)!.SetValue(shop, term);
            typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(shop, hero);
            typeof(MagicShopLocation).GetMethod("WriteTierSection", F)!.Invoke(shop, new object[] { hero, 4, 9, false });
            term.StreamWriterInternal?.Flush();
            return Strip(Encoding.UTF8.GetString(output.ToArray()));
        }
        finally { GameConfig.Language = prev; }
    }

    [Theory]
    [InlineData("en", 100)] [InlineData("hu", 100)] [InlineData("en", 1)] [InlineData("hu", 1)]
    [InlineData("es", 100)] [InlineData("fr", 100)] [InlineData("it", 100)]
    public void EnchantList_ShowsTheNewTierTexts(string lang, int level)
    {
        string shown = EnchantRows(lang, level);
        shown.Should().Contain(L(lang, "magic_shop.tier_desc_all_stats", 3));
        shown.Should().Contain(L(lang, "magic_shop.tier_desc_ocean"));
        if (lang == "en" || lang == "hu")
            foreach (var row in Rows(shown))
                row.Length.Should().BeLessOrEqualTo(MaxWidth, $"{lang} enchant list row: \"{row}\"");
    }

    // ---------- the Mountain Goat ----------

    [Fact]
    public void MountainGoat_FallbackAndEnglishKey_NameARoomNotADescent()
    {
        var goat = BeastData.GetById("mountain_goat")!;
        goat.PassiveDescription.Should().Be("-1 fatigue for each new dungeon room");
        Loc.GetIn("en", "beast.mountain_goat.passive").Should().Be(goat.PassiveDescription);
        goat.PassiveDescription.Should().NotContain("descend");
    }

    [Fact]
    public void ConfigComments_NoLongerClaimSpells()
    {
        string root = AppContext.BaseDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "Scripts", "Core", "GameConfig.cs")))
            root = Path.GetDirectoryName(root)!;
        root.Should().NotBeNull();
        var lines = File.ReadAllLines(Path.Combine(root!, "Scripts", "Core", "GameConfig.cs"));
        foreach (var name in new[] { "TideswornOceansBlessingBonus", "WavecallerHarmonicResonanceBonus" })
            lines.Single(l => l.Contains("const float " + name)).Should().NotContain("spells");
    }
}
