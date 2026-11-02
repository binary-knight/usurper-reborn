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
/// 1.3.8: the roadmap site after 1.3.8 (its release Facts moved to Bump139Tests at the 1.3.9 bump; this Fact goes with the
/// 1.3.9 roadmap). Nothing here touches the network.
/// </summary>
public class Bump138Tests
{
    private static readonly string[] Languages = { "en", "es", "fr", "hu", "it" };
    private static readonly Regex DashOrEmoji = new(@"[\u2013\u2014]|[\u2600-\u27BF]|\uFE0F|[\uD83C-\uD83E][\uDC00-\uDFFF]");


    private static string Repo(string relative) => Bump130Tests.Repo(relative);

    private static Dictionary<string, string> WebLang(string lang) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(Repo($"web/lang/{lang}.json"))!;



    [Fact]
    public void Roadmap_138Site_NowThenNextThenRecentlyShipped_InEveryLanguage()
    {
        string html = Bump130Tests.RoadmapHtml();
        var keys = Regex.Matches(html, "data-i18n=\"(r1\\.road_[a-z0-9_]+)\"").Select(m => m.Groups[1].Value).ToList();
        var numbered = keys.Where(k => k.EndsWith("_t", StringComparison.Ordinal) && k != "r1.road_now_t" && k != "r1.road_title").ToList();
        string[] next = { "20", "beyond" };
        string[] shipped = { "138", "137", "136", "135", "134", "133" };
        numbered.Should().Equal(next.Concat(shipped).Select(n => "r1.road_" + n + "_t"));
        foreach (string t in numbered) keys.Should().Contain(t.Substring(0, t.Length - 1) + "d");
        int At(string k) { int i = keys.IndexOf(k); i.Should().BeGreaterThan(-1, k); return i; }
        At("r1.road_now_t").Should().BeLessThan(At("r1.road_next_h"), "NOW comes first");
        At("r1.road_next_h").Should().BeLessThan(At("r1.road_20_t"));
        At("r1.road_beyond_d").Should().BeLessThan(At("r1.road_shipped_h"));
        At("r1.road_shipped_h").Should().BeLessThan(At("r1.road_138_t"));
        var after = new Dictionary<string, string> { ["20"] = "1.3.8", ["beyond"] = "2.0" };
        foreach (string n in next) At("r1.road_" + n + "_when").Should().Be(At("r1.road_" + n + "_d") + 1, n + " has its place in the order");
        foreach (string n in shipped) keys.Should().NotContain("r1.road_" + n + "_when", n + " is shipped");
        var en = WebLang("en");
        string[] changed = { "r1.road_now_t", "r1.road_now_d", "r1.road_138_t", "r1.road_138_d" };
        foreach (string lang in Languages)
        {
            var dict = WebLang(lang);
            foreach (string key in keys) dict[key].Should().NotBeNullOrWhiteSpace($"{lang} {key}");
            dict.Keys.Where(k => k.StartsWith("r1.road_", StringComparison.Ordinal)).Should().BeSubsetOf(keys, lang);
            foreach (string gone in new[] { "r1.road_14_t", "r1.road_14_d", "r1.road_14_when", "r1.road_132_t", "r1.road_132_d" })
                dict.ContainsKey(gone).Should().BeFalse($"{lang} {gone}");
            dict["r1.road_now_t"].Should().Contain("1.3.8", lang);
            dict["r1.road_now_d"].Should().Contain("1.3.8", lang).And.NotContain("TODO", lang);
            dict["r1.road_138_t"].Should().MatchRegex(@"^1\.3\.8 ?: \S", lang);
            dict["r1.road_138_d"].Should().Contain("11", lang);
            foreach (string n in next) dict["r1.road_" + n + "_when"].Should().Contain(after[n], $"{lang} {n}");
            foreach (var pair in dict.Where(p => p.Key.StartsWith("r1.road_", StringComparison.Ordinal)))
                DashOrEmoji.IsMatch(pair.Value).Should().BeFalse($"{lang} {pair.Key}");
            if (lang != "en") foreach (string key in changed)
                dict[key].Should().NotBe(en[key], $"{lang} {key} is translated");
        }
        foreach (string key in keys)
            html.Should().Contain($"data-i18n=\"{key}\">{en[key]}<", "the page's own English matches en.json");
        foreach (string key in new[] { "r1.road_now_d", "r1.road_138_d" })
            en[key].Should().NotContain("withdraw").And.NotContain("pull back").And.NotContain("1.4", key);
        en["r1.road_now_d"].Should().Be("1.3.8 is out: you can tell an ally to wait in a cleared dungeon room, allies who see a teammate "
            + "finished remember it, and a single player load now starts from a clean story, with NPC opinions and companion settings kept "
            + "as they were saved. Fixes to the stance menu, the news, saves, the SSH relay and the English spelling round it out. The "
            + "roadmap below says what comes next.");
        en["r1.road_138_d"].Should().Be("Shipped: an order to tell an ally to wait in a cleared room, allies who remember a fallen "
            + "teammate, single player loads that start clean, NPC opinions that hold across a load and an SSH relay that keeps the game's "
            + "last lines, with 11 changes in all.");
    }
}
