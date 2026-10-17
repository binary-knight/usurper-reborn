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
/// 1.3.3: the version, its release notes in the Changelist form (one line per change, Fixed, Adjusted or Improved,
/// under four group headings, no Known issues section), the Steam text, the README lines and bullet, and the website roadmap in five languages,
/// where 1.3.3 is shipped, 1.3.4 is the Old Gods and prestige classes, and 1.5 comes before 1.4. Nothing here touches the network.
/// </summary>
public class Bump133Tests
{
    private static readonly string[] Languages = { "en", "es", "fr", "hu", "it" };
    private const string NotesPath = "DOCS/release-notes/RELEASE_NOTES_1.3.3.md";
    private const string SteamPath = "DOCS/release-notes/steam/STEAM_RELEASE_NOTES_1.3.3.txt";
    private static readonly Regex DashOrEmoji = new(@"[\u2013\u2014]|[\u2600-\u27BF]|\uFE0F|[\uD83C-\uD83E][\uDC00-\uDFFF]");
    private static readonly Regex ChangeLine = new(@"^\u2022 (Fixed|Adjusted|Improved) [^:\n]+[^.\n]$");

    private static readonly string[] Groups = { "Monsters and difficulty", "Party and NPCs", "Online", "Wiki and Discord bot" };

    private static string Repo(string relative) => Bump130Tests.Repo(relative);

    private static Dictionary<string, string> WebLang(string lang) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(Repo($"web/lang/{lang}.json"))!;

    private static string[] Bullets(string text) =>
        text.Split('\n').Where(l => l.StartsWith("\u2022 ", StringComparison.Ordinal)).ToArray();

    [Fact]
    public void Version_Is133_NamedReckoning_AndTheTelemetryBodySays133()
    {
        GameConfig.Version.Should().Be("1.3.3");
        GameConfig.VersionName.Should().Be("Reckoning");
        byte[] body = TelemetryUploader.BuildBody(new List<string> { TelemetryBody127Tests.MaxLine() },
            TelemetryBody127Tests.FixtureInstallId, TelemetrySource.Single);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("version").EnumerateArray().Select(v => v.GetInt64()).Should().Equal(1L, 3L, 3L);
    }

    [Fact]
    public void Notes_AreOneChangelist_OfShortLines_InGroups_WithNoKnownIssues()
    {
        string notes = Repo(NotesPath);
        notes.Split('\n').Where(l => l.Length > 0).Take(2).Should().Equal("# Usurper Reborn v1.3.3", "## Changelist");
        Regex.Matches(notes, "(?m)^#{1,2} ").Count.Should().Be(2, "the title and the Changelist heading only");
        Regex.Matches(notes, "(?m)^### (.+)$").Select(m => m.Groups[1].Value).Should().Equal(Groups);
        notes.Should().NotContain("Known issues").And.NotContain("Known Issues");
        string[] bullets = Bullets(notes);
        bullets.Length.Should().Be(15);
        foreach (string b in bullets) ChangeLine.IsMatch(b).Should().BeTrue(b);
        bullets.Distinct().Count().Should().Be(bullets.Length);
        notes.Split('\n').Where(l => l.Length > 0).Skip(2).Should().OnlyContain(l =>
            l.StartsWith("\u2022 ", StringComparison.Ordinal) || l.StartsWith("### ", StringComparison.Ordinal));
        Repo("DOCS/release-notes/README.md").Should().Contain("\n- [v1.3.3](RELEASE_NOTES_1.3.3.md)\n- [v1.3.2](RELEASE_NOTES_1.3.2.md)\n");
    }

    [Fact]
    public void SteamText_CarriesTheSameChangelist_WithTheKeysInParentheses()
    {
        string steam = Repo(SteamPath);
        steam.Should().StartWith("[h1]Usurper Reborn 1.3.3[/h1]\n").And.Contain("\n[h2]Changelist[/h2]\n")
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
        readme.Should().Contain("\n**v1.3.3 \"Reckoning\"** | ").And.Contain("\n## Known Issues (v1.3.3)\n")
            .And.Contain("\n**Status:** v1.3.3 \"Reckoning\". ");
        string[] lines = readme.Split('\n');
        int bullet = Array.FindIndex(lines, l => l.StartsWith("- **v1.3.3:**", StringComparison.Ordinal));
        bullet.Should().BeGreaterThan(0);
        lines[bullet].Should().Be("- **v1.3.3:** 15 changes; the Changelist is in " + NotesPath);
        lines[bullet + 1].Should().StartWith("- **v1.3.2:**", "the newest bullet comes first");
    }

    [Fact]
    public void Roadmap_133Shipped_134IsOldGodsAndPrestige_15Before14_InEveryLanguage()
    {
        string html = Bump130Tests.RoadmapHtml();
        var keys = Regex.Matches(html, "data-i18n=\"(r1\\.road_[a-z0-9_]+)\"").Select(m => m.Groups[1].Value).ToList();
        var numbered = keys.Where(k => k.EndsWith("_t", StringComparison.Ordinal) && k != "r1.road_now_t" && k != "r1.road_title").ToList();
        numbered.Should().Equal(new[] { "r1.road_12x_t", "r1.road_126_t", "r1.road_127_t", "r1.road_130_t", "r1.road_132_t", "r1.road_133_t",
            "r1.road_134_t", "r1.road_15_t", "r1.road_14_t", "r1.road_20_t", "r1.road_beyond_t" });
        foreach (string t in numbered) keys.Should().Contain(t.Substring(0, t.Length - 1) + "d");
        var en = WebLang("en");
        foreach (string lang in Languages)
        {
            var dict = WebLang(lang);
            foreach (string key in keys) dict[key].Should().NotBeNullOrWhiteSpace($"{lang} {key}");
            dict.Keys.Where(k => k.StartsWith("r1.road_", StringComparison.Ordinal)).Should().BeSubsetOf(keys, lang);
            foreach (string gone in new[] { "r1.road_135_t", "r1.road_135_d" })
                dict.ContainsKey(gone).Should().BeFalse($"{lang} {gone}");
            dict["r1.road_133_t"].Should().StartWith("1.3.3 -- ", lang);
            foreach (string part in new[] { "_t", "_d" })
                dict["r1.road_133" + part].Should().Contain("25", lang).And.Contain("100", lang).And.NotContain("11", lang);
            dict["r1.road_134_t"].Should().StartWith("1.3.4 -- ", lang);
            foreach (string part in new[] { "_t", "_d" })
                Regex.IsMatch(dict["r1.road_134" + part].Substring(part == "_t" ? 6 : 0), @"\d").Should().BeFalse($"{lang} 1.3.4 names no floor");
            dict["r1.road_15_t"].Should().StartWith("1.5 -- ", lang);
            dict["r1.road_15_d"].Should().Contain("1.3.3", lang);
            string now = dict["r1.road_now_d"];
            now.Should().Contain("1.3.3", lang).And.Contain("1.3.0", lang).And.NotContain("1.3.1", lang);
            Regex.Matches(now, @"[.!?](\s|$)").Count.Should().Be(3, $"{lang}: three sentences");
            if (lang != "en") foreach (string key in new[] { "r1.road_now_d", "r1.road_133_d", "r1.road_134_t", "r1.road_134_d", "r1.road_15_d" })
                dict[key].Should().NotBe(en[key], $"{lang} {key} is translated");
        }
        foreach (string key in keys)
            html.Should().Contain($"data-i18n=\"{key}\">{en[key]}<", "the page's own English matches en.json");
        en["r1.road_133_t"].Should().Be("1.3.3 -- Dungeon floors 25 to 100");
        en["r1.road_133_d"].Should().StartWith("Shipped: ");
        en["r1.road_134_t"].Should().Be("1.3.4 -- The Old Gods and prestige classes");
        en["r1.road_15_d"].Should().StartWith("Shipped in part in 1.3.3 ");
    }
}
