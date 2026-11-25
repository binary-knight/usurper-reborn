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
/// 1.3.15: the version, its release notes in the Changelist form (one line per change, Fixed, Adjusted, Improved or Added, under
/// group headings, no Known issues section), the Steam text, the README lines and bullet, the roadmap site after 1.3.15 (the NOW
/// lines change, a 1.3.14 shipped card is added and the 1.3.8 card leaves), and billkinggithub in the game and website credits.
/// Nothing here touches the network.
/// </summary>
public class Bump1315Tests
{
    private static readonly string[] Languages = { "en", "es", "fr", "hu", "it" };
    private const string NotesPath = "DOCS/release-notes/RELEASE_NOTES_1.3.15.md";
    private const string SteamPath = "DOCS/release-notes/steam/STEAM_RELEASE_NOTES_1.3.15.txt";
    private static readonly Regex DashOrEmoji = new(@"[\u2013\u2014]|[\u2600-\u27BF]|\uFE0F|[\uD83C-\uD83E][\uDC00-\uDFFF]");
    private static readonly Regex ChangeLine = new(@"^\u2022 (Fixed|Adjusted|Improved|Added) [^:\n]+[^.\n]$");

    private static readonly string[] Groups = { "Characters", "Combat", "Interface" };

    private static string Repo(string relative) => Bump130Tests.Repo(relative);

    private static Dictionary<string, string> WebLang(string lang) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(Repo($"web/lang/{lang}.json"))!;

    private static string[] Bullets(string text) =>
        text.Split('\n').Where(l => l.StartsWith("\u2022 ", StringComparison.Ordinal)).ToArray();


    [Fact]
    public void Version_Is1315_NamedReckoning_AndTheTelemetryBodySays1315()
    {
        GameConfig.Version.Should().Be("1.3.15");
        GameConfig.VersionName.Should().Be("Reckoning");
        byte[] body = TelemetryUploader.BuildBody(new List<string> { TelemetryBody127Tests.MaxLine() },
            TelemetryBody127Tests.FixtureInstallId, TelemetrySource.Single);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("version").EnumerateArray().Select(v => v.GetInt64()).Should().Equal(1L, 3L, 15L);
    }
    [Fact]
    public void Notes_AreOneChangelist_OfShortLines_InGroups_WithNoKnownIssues()
    {
        string notes = Repo(NotesPath);
        notes.Split('\n').Where(l => l.Length > 0).Take(2).Should().Equal("# Usurper Reborn v1.3.15", "## Changelist");
        Regex.Matches(notes, "(?m)^#{1,2} ").Count.Should().Be(2, "the title and the Changelist heading only");
        Regex.Matches(notes, "(?m)^### (.+)$").Select(m => m.Groups[1].Value).Should().Equal(Groups);
        notes.Should().NotContain("Known issues").And.NotContain("Known Issues");
        string[] bullets = Bullets(notes);
        bullets.Length.Should().Be(5);
        foreach (string b in bullets) ChangeLine.IsMatch(b).Should().BeTrue(b);
        bullets.Distinct().Count().Should().Be(bullets.Length);
        notes.Split('\n').Where(l => l.Length > 0).Skip(2).Should().OnlyContain(l =>
            l.StartsWith("\u2022 ", StringComparison.Ordinal) || l.StartsWith("### ", StringComparison.Ordinal));
        Repo("DOCS/release-notes/README.md").Should().Contain("\n- [v1.3.15](RELEASE_NOTES_1.3.15.md)\n- [v1.3.14](RELEASE_NOTES_1.3.14.md)\n");
    }

    [Fact]
    public void SteamText_CarriesTheSameChangelist_WithTheKeysInParentheses()
    {
        string steam = Repo(SteamPath);
        steam.Should().StartWith("[h1]Usurper Reborn 1.3.15[/h1]\n").And.Contain("\n[h2]Changelist[/h2]\n")
            .And.Contain("\nFull notes: " + NotesPath + "\n");
        Regex.Matches(steam, @"\[h3\](.+)\[/h3\]\n\[list\]\n").Select(m => m.Groups[1].Value).Should().Equal(Groups);
        Regex.Matches(steam, @"\[/list\]").Count.Should().Be(Groups.Length);
        var items = steam.Split('\n').Where(l => l.StartsWith("[*]", StringComparison.Ordinal)).Select(l => l.Substring(3)).ToArray();
        var notes = Bullets(Repo(NotesPath)).Select(b => b.Substring(2).Replace("[U]", "(U)")).ToArray();
        items.Should().Equal(notes);
        Regex.IsMatch(steam, @"\[[A-Z]\]").Should().BeFalse("a one-letter key in brackets would read as a BBCode tag");
        foreach (string path in new[] { NotesPath, SteamPath, "README.md", "DOCS/release-notes/README.md" })
            DashOrEmoji.IsMatch(Repo(path)).Should().BeFalse(path);
        foreach (string lang in Languages)
            foreach (var pair in WebLang(lang).Where(p => p.Key.StartsWith("r1.road_", StringComparison.Ordinal)))
                DashOrEmoji.IsMatch(pair.Value).Should().BeFalse($"{lang} {pair.Key}");
    }

    [Fact]
    public void Readme_NamesThisRelease_InItsHeaderBulletKnownIssuesAndStatus()
    {
        string readme = Repo("README.md");
        readme.Should().Contain("\n**v1.3.15 \"Reckoning\"** | ").And.Contain("\n## Known Issues (v1.3.15)\n")
            .And.Contain("\n**Status:** v1.3.15 \"Reckoning\". ");
        string[] lines = readme.Split('\n');
        int bullet = Array.FindIndex(lines, l => l.StartsWith("- **v1.3.15:**", StringComparison.Ordinal));
        bullet.Should().BeGreaterThan(0);
        lines[bullet].Should().Be("- **v1.3.15:** 5 changes; the Changelist is in " + NotesPath);
        lines[bullet + 1].Should().StartWith("- **v1.3.14:** 1 change;", "the newest bullet comes first");
    }

    [Fact]
    public void Roadmap_1315Site_NowThenNextThenRecentlyShipped_InEveryLanguage()
    {
        string html = Bump130Tests.RoadmapHtml();
        var keys = Regex.Matches(html, "data-i18n=\"(r1\\.road_[a-z0-9_]+)\"").Select(m => m.Groups[1].Value).ToList();
        var numbered = keys.Where(k => k.EndsWith("_t", StringComparison.Ordinal) && k != "r1.road_now_t" && k != "r1.road_title").ToList();
        string[] next = { "maint" };
        string[] shipped = { "1314", "1313", "1312", "1311", "1310", "139" };
        numbered.Should().Equal(next.Concat(shipped).Select(n => "r1.road_" + n + "_t"));
        foreach (string t in numbered) keys.Should().Contain(t.Substring(0, t.Length - 1) + "d");
        int At(string k) { int i = keys.IndexOf(k); i.Should().BeGreaterThan(-1, k); return i; }
        At("r1.road_now_t").Should().BeLessThan(At("r1.road_next_h"), "NOW comes first");
        At("r1.road_next_h").Should().BeLessThan(At("r1.road_maint_t"));
        At("r1.road_maint_when").Should().BeLessThan(At("r1.road_shipped_h"));
        At("r1.road_shipped_h").Should().BeLessThan(At("r1.road_1314_t"));
        foreach (string n in next) At("r1.road_" + n + "_when").Should().Be(At("r1.road_" + n + "_d") + 1, n + " has its place in the order");
        foreach (string n in shipped) keys.Should().NotContain("r1.road_" + n + "_when", n + " is shipped");
        var en = WebLang("en");
        string[] changed = { "r1.road_now_t", "r1.road_now_d", "r1.road_maint_t", "r1.road_maint_d", "r1.road_maint_when", "r1.road_1314_t", "r1.road_1314_d" };
        foreach (string lang in Languages)
        {
            var dict = WebLang(lang);
            foreach (string key in keys) dict[key].Should().NotBeNullOrWhiteSpace($"{lang} {key}");
            dict.Keys.Where(k => k.StartsWith("r1.road_", StringComparison.Ordinal)).Should().BeSubsetOf(keys, lang);
            foreach (string gone in new[] { "r1.road_134_t", "r1.road_134_d", "r1.road_133_t", "r1.road_133_d", "r1.road_14_t", "r1.road_14_d", "r1.road_14_when", "r1.road_132_t", "r1.road_132_d", "r1.road_135_t", "r1.road_135_d",
                "r1.road_20_t", "r1.road_20_d", "r1.road_20_when", "r1.road_beyond_t", "r1.road_beyond_d", "r1.road_beyond_when", "r1.road_136_t", "r1.road_136_d", "r1.road_137_t", "r1.road_137_d", "r1.road_138_t", "r1.road_138_d" })
                dict.ContainsKey(gone).Should().BeFalse($"{lang} {gone}");
            dict["r1.road_now_t"].Should().Contain("1.3.15", lang);
            dict["r1.road_now_d"].Should().Contain("1.3.15", lang).And.NotContain("TODO", lang);
            dict["r1.road_1314_t"].Should().MatchRegex(@"^1\.3\.14 ?: \S", lang);
            dict["r1.road_1314_d"].Should().Contain("1", lang);
            dict["r1.road_maint_d"].Should().Contain("Discord", lang).And.Contain("!", lang);
            foreach (var pair in dict.Where(p => p.Key.StartsWith("r1.road_", StringComparison.Ordinal)))
                DashOrEmoji.IsMatch(pair.Value).Should().BeFalse($"{lang} {pair.Key}");
            if (lang != "en") foreach (string key in changed)
                dict[key].Should().NotBe(en[key], $"{lang} {key} is translated");
        }
        foreach (string key in keys)
            html.Should().Contain($"data-i18n=\"{key}\">{en[key]}<", "the page's own English matches en.json");
        en["r1.road_now_d"].Should().Be("1.3.15 is out: children keep their last names and a parent can change one at Home, the death menu shows a count instead of an erase warning when permadeath is off, and text lines up for Chinese, Japanese and Korean. The roadmap below says what comes next.");
        en["r1.road_1314_d"].Should().Be("Shipped: logins on the online server are steadier, with the shared item and achievement tables built once, with 1 change in all.");
        en["r1.road_maint_t"].Should().Be("Fixes as they come");
        en["r1.road_maint_d"].Should().Be("Usurper Reborn is in maintenance: reported bugs are fixed in occasional releases. Report them on Discord or press ! in the game.");
        en["r1.road_maint_when"].Should().Be("Ongoing");
    }

    [Fact]
    public void Credits_NameBillkinggithub_InTheGameAndOnTheWebsite_InEveryLanguage()
    {
        string engine = Repo("Scripts/Core/GameEngine.cs");
        engine.Should().Contain("terminal.WriteLine(Loc.Get(\"engine.credits_testingbytes\"));\n        terminal.WriteLine(Loc.Get(\"engine.credits_billkinggithub\"));");
        string html = Repo("web/index.html");
        html.Should().Contain("data-i18n=\"credits.billkinggithub_name\">billkinggithub<");
        foreach (string lang in Languages)
        {
            Loc.GetIn(lang, "engine.credits_billkinggithub").Should().StartWith("    billkinggithub ", lang);
            var web = WebLang(lang);
            web["credits.billkinggithub_name"].Should().Be("billkinggithub", lang);
            web["credits.billkinggithub_role"].Should().NotBeNullOrWhiteSpace(lang);
            web["credits.billkinggithub_desc"].Should().NotBeNullOrWhiteSpace(lang);
            DashOrEmoji.IsMatch(web["credits.billkinggithub_desc"]).Should().BeFalse(lang);
        }
    }
}
