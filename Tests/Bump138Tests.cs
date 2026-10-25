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
/// 1.3.8: the version, its release notes in the Changelist form (one line per change, Fixed, Adjusted, Improved or Added, under four
/// group headings, no Known issues section), the Steam text and the README lines and bullet. Nothing here touches the network.
/// </summary>
public class Bump138Tests
{
    private static readonly string[] Languages = { "en", "es", "fr", "hu", "it" };
    private const string NotesPath = "DOCS/release-notes/RELEASE_NOTES_1.3.8.md";
    private const string SteamPath = "DOCS/release-notes/steam/STEAM_RELEASE_NOTES_1.3.8.txt";
    private static readonly Regex DashOrEmoji = new(@"[\u2013\u2014]|[\u2600-\u27BF]|\uFE0F|[\uD83C-\uD83E][\uDC00-\uDFFF]");
    private static readonly Regex ChangeLine = new(@"^\u2022 (Fixed|Adjusted|Improved|Added) [^:\n]+[^.\n]$");

    private static readonly string[] Groups = { "The party", "Saves", "Online play", "Texts" };

    private static string Repo(string relative) => Bump130Tests.Repo(relative);

    private static Dictionary<string, string> WebLang(string lang) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(Repo($"web/lang/{lang}.json"))!;

    private static string[] Bullets(string text) =>
        text.Split('\n').Where(l => l.StartsWith("\u2022 ", StringComparison.Ordinal)).ToArray();


    [Fact]
    public void Version_Is138_NamedReckoning_AndTheTelemetryBodySays138()
    {
        GameConfig.Version.Should().Be("1.3.8");
        GameConfig.VersionName.Should().Be("Reckoning");
        byte[] body = TelemetryUploader.BuildBody(new List<string> { TelemetryBody127Tests.MaxLine() },
            TelemetryBody127Tests.FixtureInstallId, TelemetrySource.Single);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("version").EnumerateArray().Select(v => v.GetInt64()).Should().Equal(1L, 3L, 8L);
    }
    [Fact]
    public void Notes_AreOneChangelist_OfShortLines_InGroups_WithNoKnownIssues()
    {
        string notes = Repo(NotesPath);
        notes.Split('\n').Where(l => l.Length > 0).Take(2).Should().Equal("# Usurper Reborn v1.3.8", "## Changelist");
        Regex.Matches(notes, "(?m)^#{1,2} ").Count.Should().Be(2, "the title and the Changelist heading only");
        Regex.Matches(notes, "(?m)^### (.+)$").Select(m => m.Groups[1].Value).Should().Equal(Groups);
        notes.Should().NotContain("Known issues").And.NotContain("Known Issues");
        string[] bullets = Bullets(notes);
        bullets.Length.Should().Be(11);
        foreach (string b in bullets) ChangeLine.IsMatch(b).Should().BeTrue(b);
        bullets.Distinct().Count().Should().Be(bullets.Length);
        notes.Split('\n').Where(l => l.Length > 0).Skip(2).Should().OnlyContain(l =>
            l.StartsWith("\u2022 ", StringComparison.Ordinal) || l.StartsWith("### ", StringComparison.Ordinal));
        Repo("DOCS/release-notes/README.md").Should().Contain("\n- [v1.3.8](RELEASE_NOTES_1.3.8.md)\n- [v1.3.7](RELEASE_NOTES_1.3.7.md)\n");
    }

    [Fact]
    public void SteamText_CarriesTheSameChangelist_WithTheKeysInParentheses()
    {
        string steam = Repo(SteamPath);
        steam.Should().StartWith("[h1]Usurper Reborn 1.3.8[/h1]\n").And.Contain("\n[h2]Changelist[/h2]\n")
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
        readme.Should().Contain("\n**v1.3.8 \"Reckoning\"** | ").And.Contain("\n## Known Issues (v1.3.8)\n")
            .And.Contain("\n**Status:** v1.3.8 \"Reckoning\". ");
        string[] lines = readme.Split('\n');
        int bullet = Array.FindIndex(lines, l => l.StartsWith("- **v1.3.8:**", StringComparison.Ordinal));
        bullet.Should().BeGreaterThan(0);
        lines[bullet].Should().Be("- **v1.3.8:** 11 changes; the Changelist is in " + NotesPath);
        lines[bullet + 1].Should().StartWith("- **v1.3.7:** 18 changes;", "the newest bullet comes first");
    }
}
