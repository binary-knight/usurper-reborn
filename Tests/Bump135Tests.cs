using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.5 website: the roadmap in five languages, with NOW for 1.3.5 first, then Next (1.3.6, 1.3.7, 1.4, 2.0, Beyond, each
/// with its place in the order), then Recently shipped (1.3.5 back to 1.3.0 and 1.2.7). Nothing here touches the network.
/// </summary>
public class Bump135Tests
{
    private static readonly string[] Languages = { "en", "es", "fr", "hu", "it" };
    private static readonly Regex DashOrEmoji = new(@"[\u2013\u2014]|[\u2600-\u27BF]|\uFE0F|[\uD83C-\uD83E][\uDC00-\uDFFF]");

    private static Dictionary<string, string> WebLang(string lang) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(Bump130Tests.Repo($"web/lang/{lang}.json"))!;

    [Fact]
    public void Roadmap_135Site_NowThenNextThenRecentlyShipped_InEveryLanguage()
    {
        string html = Bump130Tests.RoadmapHtml();
        var keys = Regex.Matches(html, "data-i18n=\"(r1\\.road_[a-z0-9_]+)\"").Select(m => m.Groups[1].Value).ToList();
        var numbered = keys.Where(k => k.EndsWith("_t", StringComparison.Ordinal) && k != "r1.road_now_t" && k != "r1.road_title").ToList();
        string[] next = { "136", "137", "14", "20", "beyond" };
        string[] shipped = { "135", "134", "133", "132", "130", "127" };
        numbered.Should().Equal(next.Concat(shipped).Select(n => "r1.road_" + n + "_t"));
        foreach (string t in numbered) keys.Should().Contain(t.Substring(0, t.Length - 1) + "d");
        int At(string k) { int i = keys.IndexOf(k); i.Should().BeGreaterThan(-1, k); return i; }
        At("r1.road_now_t").Should().BeLessThan(At("r1.road_next_h"), "NOW comes first");
        At("r1.road_next_h").Should().BeLessThan(At("r1.road_136_t"));
        At("r1.road_beyond_d").Should().BeLessThan(At("r1.road_shipped_h"));
        At("r1.road_shipped_h").Should().BeLessThan(At("r1.road_135_t"));
        var after = new Dictionary<string, string> { ["136"] = "1.3.5", ["137"] = "1.3.6", ["14"] = "1.3.7", ["20"] = "1.4", ["beyond"] = "2.0" };
        foreach (string n in next) At("r1.road_" + n + "_when").Should().Be(At("r1.road_" + n + "_d") + 1, n + " has its place in the order");
        foreach (string n in shipped) keys.Should().NotContain("r1.road_" + n + "_when", n + " is shipped");
        var en = WebLang("en");
        // The words today's road_14_d (the Player District text) used for district, Dark Alley and banner, per language.
        var districtWords = new Dictionary<string, string[]>
        {
            ["en"] = new[] { "District", "Dark Alley", "banner" },
            ["es"] = new[] { "distrito", "Callejón Oscuro", "estandarte" },
            ["fr"] = new[] { "quartier", "Ruelle Sombre", "bannière" },
            ["hu"] = new[] { "negyed", "Sötét sikátor", "zászló" },
            ["it"] = new[] { "quartiere", "Vicolo Oscuro", "vessillo" },
        };
        string[] changed = { "r1.road_now_t", "r1.road_now_d", "r1.road_136_t", "r1.road_136_d", "r1.road_136_when", "r1.road_137_t",
            "r1.road_137_d", "r1.road_137_when", "r1.road_14_t", "r1.road_14_d", "r1.road_14_when", "r1.road_beyond_d",
            "r1.road_135_t", "r1.road_135_d", "r1.road_134_t", "r1.road_134_d" };
        foreach (string lang in Languages)
        {
            var dict = WebLang(lang);
            foreach (string key in keys) dict[key].Should().NotBeNullOrWhiteSpace($"{lang} {key}");
            dict.Keys.Where(k => k.StartsWith("r1.road_", StringComparison.Ordinal)).Should().BeSubsetOf(keys, lang);
            foreach (string gone in new[] { "r1.road_15_t", "r1.road_15_d", "r1.road_15_when", "r1.road_135_when",
                "r1.road_126_t", "r1.road_126_d", "r1.road_12x_t", "r1.road_12x_d" })
                dict.ContainsKey(gone).Should().BeFalse($"{lang} {gone}");
            dict["r1.road_now_t"].Should().Contain("1.3.5", lang);
            dict["r1.road_now_d"].Should().Contain("1.3.5", lang).And.NotContain("TODO", lang);
            dict["r1.road_14_t"].Should().StartWith("1.4 -- ", lang);
            dict["r1.road_136_t"].Should().StartWith("1.3.6 -- ", lang);
            dict["r1.road_137_t"].Should().StartWith("1.3.7 -- ", lang);
            dict["r1.road_135_t"].Should().StartWith("1.3.5 -- ", lang);
            dict["r1.road_134_t"].Should().StartWith("1.3.4 -- ", lang);
            dict["r1.road_135_d"].Should().Contain("11", lang).And.Contain("34", lang);
            foreach (string word in districtWords[lang]) dict["r1.road_14_d"].Should().NotContainEquivalentOf(word, $"{lang} road_14_d has no Player District text");
            dict["r1.road_14_d"].Should().Contain("1.3.3", lang);
            foreach (string n in next) dict["r1.road_" + n + "_when"].Should().Contain(after[n], $"{lang} {n}");
            foreach (var pair in dict.Where(p => p.Key.StartsWith("r1.road_", StringComparison.Ordinal)))
                DashOrEmoji.IsMatch(pair.Value).Should().BeFalse($"{lang} {pair.Key}");
            if (lang != "en") foreach (string key in changed)
                dict[key].Should().NotBe(en[key], $"{lang} {key} is translated");
        }
        foreach (string key in keys)
            html.Should().Contain($"data-i18n=\"{key}\">{en[key]}<", "the page's own English matches en.json");
        en["r1.road_now_d"].Should().Be("1.3.5 is out: three counter enchants, Sunbane, Stonebane and Godbane, break the divine armor of the last "
            + "three Old Gods and hold back two of each god's abilities; Maelketh scales with party size and Manwe's HP came down so a full party "
            + "holding Godbane can win. Many enchant and loot effect faults were fixed, and gear perks were reworked: crit damage from gear "
            + "adds above the Dexterity cap, regeneration restores a share of max HP and mana for companions too, and armor piercing cuts "
            + "monster Defense on abilities and area attacks as well. Cycle Rewind and the Cyclebreaker's evade now do what their texts say. "
            + "The roadmap below says what comes next.");
        en["r1.road_136_d"].Should().Be("First, five enchant rules: magic resistance becomes a real defense against magical attacks, poison deals "
            + "damage over several rounds, Titan's Resolve adds 5% max HP, more enchant and loot effects work for companions, on abilities, in "
            + "PvP and against world bosses, and Phoenix Fire and Frostbite grow stronger. Then every ability and spell of all seventeen classes "
            + "is checked against what it really does, texts are fixed where they are wrong, and every class is measured at every depth and "
            + "against every Old God, together with how gear grows from floor to floor.");
        en["r1.road_135_d"].Should().Be("Shipped: counter enchants against the last three Old Gods, Maelketh scaled to party size and Manwe made "
            + "winnable for a full party with Godbane, many enchant and loot effect fixes, and reworked gear crit damage, regeneration and "
            + "armor piercing. Dungeon floors 11 to 34 were measured and kept as they are; how gear grows from floor to floor is measured and "
            + "tuned with the classes in 1.3.6 and 1.3.7.");
    }
}
