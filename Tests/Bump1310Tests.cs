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
/// 1.3.10: the roadmap site after 1.3.10 (its release Facts moved to Bump1311Tests at the 1.3.11 bump; this Fact goes with the
/// 1.3.11 roadmap). Nothing here touches the network.
/// </summary>
public class Bump1310Tests
{
    private static readonly string[] Languages = { "en", "es", "fr", "hu", "it" };
    private static readonly Regex DashOrEmoji = new(@"[\u2013\u2014]|[\u2600-\u27BF]|\uFE0F|[\uD83C-\uD83E][\uDC00-\uDFFF]");

    private static string Repo(string relative) => Bump130Tests.Repo(relative);

    private static Dictionary<string, string> WebLang(string lang) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(Repo($"web/lang/{lang}.json"))!;



    [Fact]
    public void Roadmap_1310Site_NowThenNextThenRecentlyShipped_InEveryLanguage()
    {
        string html = Bump130Tests.RoadmapHtml();
        var keys = Regex.Matches(html, "data-i18n=\"(r1\\.road_[a-z0-9_]+)\"").Select(m => m.Groups[1].Value).ToList();
        var numbered = keys.Where(k => k.EndsWith("_t", StringComparison.Ordinal) && k != "r1.road_now_t" && k != "r1.road_title").ToList();
        string[] next = { "20", "beyond" };
        string[] shipped = { "1310", "139", "138", "137", "136", "135" };
        numbered.Should().Equal(next.Concat(shipped).Select(n => "r1.road_" + n + "_t"));
        foreach (string t in numbered) keys.Should().Contain(t.Substring(0, t.Length - 1) + "d");
        int At(string k) { int i = keys.IndexOf(k); i.Should().BeGreaterThan(-1, k); return i; }
        At("r1.road_now_t").Should().BeLessThan(At("r1.road_next_h"), "NOW comes first");
        At("r1.road_next_h").Should().BeLessThan(At("r1.road_20_t"));
        At("r1.road_beyond_d").Should().BeLessThan(At("r1.road_shipped_h"));
        At("r1.road_shipped_h").Should().BeLessThan(At("r1.road_1310_t"));
        var after = new Dictionary<string, string> { ["20"] = "1.3.10", ["beyond"] = "2.0" };
        foreach (string n in next) At("r1.road_" + n + "_when").Should().Be(At("r1.road_" + n + "_d") + 1, n + " has its place in the order");
        foreach (string n in shipped) keys.Should().NotContain("r1.road_" + n + "_when", n + " is shipped");
        var en = WebLang("en");
        string[] changed = { "r1.road_now_t", "r1.road_now_d", "r1.road_1310_t", "r1.road_1310_d" };
        foreach (string lang in Languages)
        {
            var dict = WebLang(lang);
            foreach (string key in keys) dict[key].Should().NotBeNullOrWhiteSpace($"{lang} {key}");
            dict.Keys.Where(k => k.StartsWith("r1.road_", StringComparison.Ordinal)).Should().BeSubsetOf(keys, lang);
            foreach (string gone in new[] { "r1.road_134_t", "r1.road_134_d", "r1.road_133_t", "r1.road_133_d", "r1.road_14_t", "r1.road_14_d", "r1.road_14_when", "r1.road_132_t", "r1.road_132_d" })
                dict.ContainsKey(gone).Should().BeFalse($"{lang} {gone}");
            dict["r1.road_now_t"].Should().Contain("1.3.10", lang);
            dict["r1.road_now_d"].Should().Contain("1.3.10", lang).And.NotContain("TODO", lang);
            dict["r1.road_1310_t"].Should().MatchRegex(@"^1\.3\.10 ?: \S", lang);
            dict["r1.road_1310_d"].Should().Contain("85", lang);
            foreach (string n in next) dict["r1.road_" + n + "_when"].Should().Contain(after[n], $"{lang} {n}");
            foreach (var pair in dict.Where(p => p.Key.StartsWith("r1.road_", StringComparison.Ordinal)))
                DashOrEmoji.IsMatch(pair.Value).Should().BeFalse($"{lang} {pair.Key}");
            if (lang != "en") foreach (string key in changed)
                dict[key].Should().NotBe(en[key], $"{lang} {key} is translated");
        }
        foreach (string key in keys)
            html.Should().Contain($"data-i18n=\"{key}\">{en[key]}<", "the page's own English matches en.json");
        en["r1.road_now_d"].Should().Be("1.3.10 is out: quest targets now wait on named dungeon floors, bandits come three times as often during a bandit contract or raid, Manwe's final answer weighs the ending, and the moral paradoxes are offered on their floors. A fifth dungeon outpost opens on floor 85, the services of the Outskirts work, the bank pays its stated rate, and all 17 classes have a portrait. Fixes to the play page, combat, the town and the living world round it out. The roadmap below says what comes next.");
        en["r1.road_1310_d"].Should().Be("Shipped: quest targets on named dungeon floors, bandits three times as often during a bandit contract or raid, Manwe's final answer weighing the ending, a fifth dungeon outpost on floor 85, working services in the Outskirts, a bank that pays its stated rate and portraits for all 17 classes, with 85 changes in all.");
        en["r1.road_20_when"].Should().Be("After 1.3.10");
    }
}
