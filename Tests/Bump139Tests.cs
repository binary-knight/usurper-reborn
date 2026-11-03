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
/// 1.3.9: the roadmap site after 1.3.9 (its release Facts moved to Bump1310Tests at the 1.3.10 bump; this Fact goes with the
/// 1.3.10 roadmap). Nothing here touches the network.
/// </summary>
public class Bump139Tests
{
    private static readonly string[] Languages = { "en", "es", "fr", "hu", "it" };
    private static readonly Regex DashOrEmoji = new(@"[\u2013\u2014]|[\u2600-\u27BF]|\uFE0F|[\uD83C-\uD83E][\uDC00-\uDFFF]");

    private static string Repo(string relative) => Bump130Tests.Repo(relative);

    private static Dictionary<string, string> WebLang(string lang) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(Repo($"web/lang/{lang}.json"))!;



    [Fact]
    public void Roadmap_139Site_NowThenNextThenRecentlyShipped_InEveryLanguage()
    {
        string html = Bump130Tests.RoadmapHtml();
        var keys = Regex.Matches(html, "data-i18n=\"(r1\\.road_[a-z0-9_]+)\"").Select(m => m.Groups[1].Value).ToList();
        var numbered = keys.Where(k => k.EndsWith("_t", StringComparison.Ordinal) && k != "r1.road_now_t" && k != "r1.road_title").ToList();
        string[] next = { "20", "beyond" };
        string[] shipped = { "139", "138", "137", "136", "135", "134" };
        numbered.Should().Equal(next.Concat(shipped).Select(n => "r1.road_" + n + "_t"));
        foreach (string t in numbered) keys.Should().Contain(t.Substring(0, t.Length - 1) + "d");
        int At(string k) { int i = keys.IndexOf(k); i.Should().BeGreaterThan(-1, k); return i; }
        At("r1.road_now_t").Should().BeLessThan(At("r1.road_next_h"), "NOW comes first");
        At("r1.road_next_h").Should().BeLessThan(At("r1.road_20_t"));
        At("r1.road_beyond_d").Should().BeLessThan(At("r1.road_shipped_h"));
        At("r1.road_shipped_h").Should().BeLessThan(At("r1.road_139_t"));
        var after = new Dictionary<string, string> { ["20"] = "1.3.9", ["beyond"] = "2.0" };
        foreach (string n in next) At("r1.road_" + n + "_when").Should().Be(At("r1.road_" + n + "_d") + 1, n + " has its place in the order");
        foreach (string n in shipped) keys.Should().NotContain("r1.road_" + n + "_when", n + " is shipped");
        var en = WebLang("en");
        string[] changed = { "r1.road_now_t", "r1.road_now_d", "r1.road_20_d", "r1.road_139_t", "r1.road_139_d" };
        foreach (string lang in Languages)
        {
            var dict = WebLang(lang);
            foreach (string key in keys) dict[key].Should().NotBeNullOrWhiteSpace($"{lang} {key}");
            dict.Keys.Where(k => k.StartsWith("r1.road_", StringComparison.Ordinal)).Should().BeSubsetOf(keys, lang);
            foreach (string gone in new[] { "r1.road_133_t", "r1.road_133_d", "r1.road_14_t", "r1.road_14_d", "r1.road_14_when", "r1.road_132_t", "r1.road_132_d" })
                dict.ContainsKey(gone).Should().BeFalse($"{lang} {gone}");
            dict["r1.road_now_t"].Should().Contain("1.3.9", lang);
            dict["r1.road_now_d"].Should().Contain("1.3.9", lang).And.NotContain("TODO", lang);
            dict["r1.road_139_t"].Should().MatchRegex(@"^1\.3\.9 ?: \S", lang);
            dict["r1.road_139_d"].Should().Contain("32", lang);
            foreach (string n in next) dict["r1.road_" + n + "_when"].Should().Contain(after[n], $"{lang} {n}");
            foreach (var pair in dict.Where(p => p.Key.StartsWith("r1.road_", StringComparison.Ordinal)))
                DashOrEmoji.IsMatch(pair.Value).Should().BeFalse($"{lang} {pair.Key}");
            if (lang != "en") foreach (string key in changed)
                dict[key].Should().NotBe(en[key], $"{lang} {key} is translated");
        }
        foreach (string key in keys)
            html.Should().Contain($"data-i18n=\"{key}\">{en[key]}<", "the page's own English matches en.json");
        en["r1.road_now_d"].Should().Be("1.3.9 is out: the play page in your browser now has the Chronicle view, a book page layout for "
            + "fights, dungeon rooms and Main Street with monster pictures, floor scenes and a room map, and menus on the web take a single "
            + "key press. The classic Main Street is the default again, Auto-Combat and Speed have single keys, and the game window opens at "
            + "the size you left it. Fixes to the website, the play page and the game round it out. The roadmap below says what comes next.");
        en["r1.road_139_d"].Should().Be("Shipped: the play page with its Chronicle view, a book page layout with monster pictures, floor "
            + "scenes and a room map, single key menus on the web, the Observatory page of public world data and the classic Main Street "
            + "back as the default, with 32 changes in all.");
        en["r1.road_20_d"].Should().Be("The Chronicle view on the play page grows into a full graphical client for the same living world, "
            + "in the browser and in a desktop app on Steam, with book pages for the shops, the town and the screens that still show as text.");
    }
}
