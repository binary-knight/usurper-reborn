using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.7: the roadmap site after 1.3.7 (its release Facts moved to Bump138Tests at the 1.3.8 bump; this Fact goes with the
/// 1.3.8 roadmap). Nothing here touches the network.
/// </summary>
public class Bump137Tests
{
    private static readonly string[] Languages = { "en", "es", "fr", "hu", "it" };
    private static readonly Regex DashOrEmoji = new(@"[\u2013\u2014]|[\u2600-\u27BF]|\uFE0F|[\uD83C-\uD83E][\uDC00-\uDFFF]");


    private static string Repo(string relative) => Bump130Tests.Repo(relative);

    private static Dictionary<string, string> WebLang(string lang) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(Repo($"web/lang/{lang}.json"))!;



    [Fact]
    public void Roadmap_137Site_NowThenNextThenRecentlyShipped_InEveryLanguage()
    {
        string html = Bump130Tests.RoadmapHtml();
        var keys = Regex.Matches(html, "data-i18n=\"(r1\\.road_[a-z0-9_]+)\"").Select(m => m.Groups[1].Value).ToList();
        var numbered = keys.Where(k => k.EndsWith("_t", StringComparison.Ordinal) && k != "r1.road_now_t" && k != "r1.road_title").ToList();
        string[] next = { "14", "20", "beyond" };
        string[] shipped = { "137", "136", "135", "134", "133", "132" };
        numbered.Should().Equal(next.Concat(shipped).Select(n => "r1.road_" + n + "_t"));
        foreach (string t in numbered) keys.Should().Contain(t.Substring(0, t.Length - 1) + "d");
        int At(string k) { int i = keys.IndexOf(k); i.Should().BeGreaterThan(-1, k); return i; }
        At("r1.road_now_t").Should().BeLessThan(At("r1.road_next_h"), "NOW comes first");
        At("r1.road_next_h").Should().BeLessThan(At("r1.road_14_t"));
        At("r1.road_beyond_d").Should().BeLessThan(At("r1.road_shipped_h"));
        At("r1.road_shipped_h").Should().BeLessThan(At("r1.road_137_t"));
        var after = new Dictionary<string, string> { ["14"] = "1.3.7", ["20"] = "1.4", ["beyond"] = "2.0" };
        foreach (string n in next) At("r1.road_" + n + "_when").Should().Be(At("r1.road_" + n + "_d") + 1, n + " has its place in the order");
        foreach (string n in shipped) keys.Should().NotContain("r1.road_" + n + "_when", n + " is shipped");
        var en = WebLang("en");
        string[] changed = { "r1.road_now_t", "r1.road_now_d", "r1.road_137_d" };
        foreach (string lang in Languages)
        {
            var dict = WebLang(lang);
            foreach (string key in keys) dict[key].Should().NotBeNullOrWhiteSpace($"{lang} {key}");
            dict.Keys.Where(k => k.StartsWith("r1.road_", StringComparison.Ordinal)).Should().BeSubsetOf(keys, lang);
            foreach (string gone in new[] { "r1.road_137_when", "r1.road_130_t", "r1.road_130_d" })
                dict.ContainsKey(gone).Should().BeFalse($"{lang} {gone}");
            dict["r1.road_now_t"].Should().Contain("1.3.7", lang);
            dict["r1.road_now_d"].Should().Contain("1.3.7", lang).And.NotContain("TODO", lang);
            dict["r1.road_137_t"].Should().StartWith("1.3.7 -- ", lang);
            dict["r1.road_14_t"].Should().StartWith("1.4 -- ", lang);
            dict["r1.road_137_d"].Should().Contain("18", lang);
            foreach (string n in next) dict["r1.road_" + n + "_when"].Should().Contain(after[n], $"{lang} {n}");
            foreach (var pair in dict.Where(p => p.Key.StartsWith("r1.road_", StringComparison.Ordinal)))
                DashOrEmoji.IsMatch(pair.Value).Should().BeFalse($"{lang} {pair.Key}");
            if (lang != "en") foreach (string key in changed)
                dict[key].Should().NotBe(en[key], $"{lang} {key} is translated");
        }
        foreach (string key in keys)
            html.Should().Contain($"data-i18n=\"{key}\">{en[key]}<", "the page's own English matches en.json");
        en["r1.road_now_d"].Should().Be("1.3.7 is out: Power Hat heals more, the Alchemist's late bombs and Corrosive Cloud hit harder, "
            + "the Voidreaver gains 1.5 Constitution a level and Corruption Harvest heals at each poisoned kill. Monsters on floors 76 to 100 "
            + "hit harder, and Manwe's Creator's Ward now stops part of what gear enchantments add, Godbane or not. Combat quickbar and menu "
            + "rows fit their boxes, and fixes to gear and loot display, the admin level edit and the wiki spelling round it out. The "
            + "roadmap below says what comes next.");
        en["r1.road_137_d"].Should().Be("Shipped: the classes tuned from the measurements of every class at every depth and against every "
            + "Old God (Power Hat, the Alchemist's late bombs, the Voidreaver's Constitution and Corruption Harvest), harder monsters on "
            + "floors 76 to 100, and Manwe's Creator's Ward against gear enchantments, with 18 changes in all.");
    }
}
