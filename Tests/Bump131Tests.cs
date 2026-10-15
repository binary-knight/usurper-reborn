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
/// 1.3.1: the version, its release notes (one fix item and the carried Known issues), the Steam text, the
/// README lines and bullet, and the website roadmap in five languages, where the entry for tuning and fixes
/// is 1.3.2 and the deeper floors follow as 1.3.3 to 1.3.5. Nothing here touches the network.
/// </summary>
public class Bump131Tests
{
    private static readonly string[] Languages = { "en", "es", "fr", "hu", "it" };
    private const string NotesPath = "DOCS/release-notes/RELEASE_NOTES_1.3.1.md";
    private const string SteamPath = "DOCS/release-notes/steam/STEAM_RELEASE_NOTES_1.3.1.txt";
    private static readonly Regex DashOrEmoji = new(@"[\u2013\u2014]|[\u2600-\u27BF]|\uFE0F|[\uD83C-\uD83E][\uDC00-\uDFFF]");

    private static string Repo(string relative) => Bump130Tests.Repo(relative);

    private static Dictionary<string, string> WebLang(string lang) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(Repo($"web/lang/{lang}.json"))!;

    [Fact]
    public void Notes_Exist_HoldOneFixItem_CarryTheKnownIssues_AndHaveNoComingLine()
    {
        string notes = Repo(NotesPath);
        notes.Should().StartWith("# Usurper Reborn v1.3.1\n\n## What is new in 1.3.1\n");
        int items = notes.Substring(0, notes.IndexOf("\n## Known issues\n", StringComparison.Ordinal))
            .Split('\n').Count(l => l.StartsWith("- ", StringComparison.Ordinal));
        items.Should().Be(1, "the notes hold exactly one fix item");
        // the Known issues section is the 1.3.0 one, unchanged
        string old = Repo(Bump130Tests.NotesPath);
        notes.Substring(notes.IndexOf("## Known issues", StringComparison.Ordinal)).TrimEnd()
            .Should().Be(old.Substring(old.IndexOf("## Known issues", StringComparison.Ordinal)).TrimEnd());
        Regex.IsMatch(notes, "(?i)coming (next|in)\\b").Should().BeFalse();
        Regex.IsMatch(notes, "(?im)^#+ .*coming").Should().BeFalse();
        Repo("DOCS/release-notes/README.md").Should().Contain("\n- [v1.3.1](RELEASE_NOTES_1.3.1.md)\n- [v1.3.0](RELEASE_NOTES_1.3.0.md)\n");
    }

    [Fact]
    public void SteamText_Exists_AndNoNewTextHasDashesOrEmojis()
    {
        string steam = Repo(SteamPath);
        steam.Should().StartWith("[h1]Usurper Reborn 1.3.1[/h1]\n").And.Contain("\nFull notes: " + NotesPath + "\n");
        Regex.IsMatch(steam, "(?i)coming (next|in)\\b").Should().BeFalse();
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
        readme.Should().Contain("\n**v1.3.1 \"Reckoning\"** | ").And.Contain("\n## Known Issues (v1.3.1)\n")
            .And.Contain("\n**Status:** v1.3.1 \"Reckoning\". ");
        string[] lines = readme.Split('\n');
        int bullet = Array.FindIndex(lines, l => l.StartsWith("- **v1.3.1:**", StringComparison.Ordinal));
        bullet.Should().BeGreaterThan(0);
        lines[bullet + 1].Should().StartWith("- **v1.3.0 \"Reckoning\":**", "the newest bullet comes first");
        lines.Count(l => l.StartsWith("- **v1.3.1:**", StringComparison.Ordinal)).Should().Be(1);
    }

    [Fact]
    public void Roadmap_131Shipped_TuningIs132_FloorsAre133To135_InEveryLanguage()
    {
        string html = Bump130Tests.RoadmapHtml();
        var keys = Regex.Matches(html, "data-i18n=\"(r1\\.road_[a-z0-9_]+)\"").Select(m => m.Groups[1].Value).ToList();
        var numbered = keys.Where(k => k.EndsWith("_t", StringComparison.Ordinal) && k != "r1.road_now_t" && k != "r1.road_title").ToList();
        numbered.Should().Equal(new[] { "r1.road_12x_t", "r1.road_126_t", "r1.road_127_t", "r1.road_130_t", "r1.road_132_t", "r1.road_133_t",
            "r1.road_134_t", "r1.road_135_t", "r1.road_14_t", "r1.road_15_t", "r1.road_20_t", "r1.road_beyond_t" });
        keys.Should().NotContain("r1.road_131_t").And.NotContain("r1.road_131_d");
        foreach (string t in numbered) keys.Should().Contain(t.Substring(0, t.Length - 1) + "d");
        var en = WebLang("en");
        var titles = new (string Key, string Starts)[] { ("r1.road_130_t", "1.3.0 -- "), ("r1.road_132_t", "1.3.2 -- "),
            ("r1.road_133_t", "1.3.3 -- "), ("r1.road_134_t", "1.3.4 -- "), ("r1.road_135_t", "1.3.5 -- "),
            ("r1.road_14_t", "1.4 -- "), ("r1.road_15_t", "1.5 -- "), ("r1.road_20_t", "2.0 -- ") };
        foreach (string lang in Languages)
        {
            var dict = WebLang(lang);
            foreach (string key in keys) dict[key].Should().NotBeNullOrWhiteSpace($"{lang} {key}");
            dict.Keys.Where(k => k.StartsWith("r1.road_", StringComparison.Ordinal)).Should().BeSubsetOf(keys, lang);
            dict.ContainsKey("r1.road_131_t").Should().BeFalse(lang);
            foreach (var (key, starts) in titles) dict[key].Should().StartWith(starts, $"{lang} {key}");
            foreach (var (key, a, b) in new[] { ("r1.road_133", "11", "25"), ("r1.road_134", "26", "50"), ("r1.road_135", "51", "100") })
                foreach (string part in new[] { "_t", "_d" })
                    dict[key + part].Should().Contain(a, $"{lang} {key}{part}").And.Contain(b, $"{lang} {key}{part}");
            dict["r1.road_132_d"].Should().Contain("1.3.0", lang);
            if (lang != "en") foreach (string key in new[] { "r1.road_132_d", "r1.road_133_d", "r1.road_134_d", "r1.road_135_d" })
                dict[key].Should().NotBe(en[key], $"{lang} {key} is translated");
        }
        foreach (string key in keys)
            html.Should().Contain($"data-i18n=\"{key}\">{en[key]}<", "the page's own English matches en.json");
        en["r1.road_132_t"].Should().Be("1.3.2 -- Tuning and fixes");
        en["r1.road_14_t"].Should().Be("1.4 -- The Player District");
        en["r1.road_15_t"].Should().Be("1.5 -- Party survival");
    }

    private const string NowText = "1.3.1 is out: the first step of a longer rework of dungeon difficulty, and betrayal, with one load fix. "
        + "The 1.3.0 entry below says what it holds. The world is open, and it is running right now.";

    [Fact]
    public void TheNowText_Names131_AndThe130Entry_InEveryLanguage()
    {
        WebLang("en")["r1.road_now_d"].Should().Be(NowText);
        Repo("web/index.html").Should().Contain($"<p data-i18n=\"r1.road_now_d\">{NowText}</p>");
        foreach (string lang in Languages)
        {
            string text = WebLang(lang)["r1.road_now_d"];
            text.Should().Contain("1.3.1", lang).And.Contain("1.3.0", lang).And.NotContain("1.2", lang);
            Regex.Matches(text, @"[.!?](\s|$)").Count.Should().Be(3, $"{lang}: three sentences");
            if (lang != "en") text.Should().NotBe(NowText, $"{lang} is translated");
        }
    }
}
