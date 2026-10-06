using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.7 bump (T5): the version, the approved privacy note word for word in the README, the 1.2.7
/// release notes and the Steam store file, no dashes or emojis in the new text, and no sentence about
/// addresses except the approved one. The note is read from the fixed copy
/// Tests/Fixtures/telemetry-privacy-note-en.txt (the user approved text, its one space indent removed).
/// Nothing here touches the network.
/// </summary>
public class Bump127Tests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test must run inside the repository");
        return dir!.FullName;
    }

    /// <summary>A repository text file, with Windows line ends read as plain ones.</summary>
    internal static string Repo(string relative) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relative), Encoding.UTF8).Replace("\r\n", "\n");

    internal static string Note() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "telemetry-privacy-note-en.txt"), Encoding.UTF8).Replace("\r\n", "\n");

    internal const string NotesPath = "DOCS/release-notes/RELEASE_NOTES_1.2.7.md";
    internal const string SteamNotesPath = "DOCS/release-notes/steam/STEAM_RELEASE_NOTES_1.2.7.txt";
    internal const string StorePath = "DOCS/release-notes/steam/STEAM_STORE_PRIVACY_1.2.7.txt";

    private static int Count(string text, string part)
    {
        int n = 0;
        for (int i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal)) n++;
        return n;
    }

    /// <summary>The README text this release added: its Telemetry section and its v1.2.7 bullet.</summary>
    internal static string ReadmeAdded()
    {
        string readme = Repo("README.md");
        int start = readme.IndexOf("\n## Telemetry\n", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, "the README has a Telemetry section");
        int end = readme.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        string section = readme.Substring(start, end - start);
        string bullet = readme.Split('\n').Single(l => l.StartsWith("- **v1.2.7:**", StringComparison.Ordinal));
        return section + "\n" + bullet;
    }

    // ---------- 1. Version ----------

    [Fact]
    public void Version_Is127_AndTheTelemetryBodySays127()
    {
        GameConfig.Version.Should().Be("1.2.7");
        GameConfig.VersionName.Should().Be("Devotion");
        byte[] body = TelemetryUploader.BuildBody(new List<string> { TelemetryBody127Tests.MaxLine() },
            TelemetryBody127Tests.FixtureInstallId, TelemetrySource.Single);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("version").EnumerateArray().Select(v => v.GetInt64()).Should().Equal(1L, 2L, 7L);
    }

    // ---------- 2. The privacy note word for word ----------

    [Fact]
    public void PrivacyNote_IsTheApprovedText_InTheReadme_TheNotes_AndTheStoreFile()
    {
        string note = Note();
        note.Should().StartWith("Telemetry (opt in). From 1.2.7 the game asks once whether to share\n");
        note.Should().EndWith("sharing off. Turning it off deletes anything not yet sent.\n");
        Count(Repo("README.md"), note).Should().Be(1, "the README quotes the note once, word for word");
        Count(ReadmeAdded(), note).Should().Be(1, "inside its Telemetry section");
        Count(Repo(NotesPath), note).Should().Be(1, "the 1.2.7 notes quote the note once, word for word");
        Repo(StorePath).Should().Be(note, "the Steam store file is the note and nothing else");
    }

    // ---------- 3. No dashes, no emojis ----------

    private static readonly Regex DashOrEmoji = new(@"[\u2013\u2014]|[\u2600-\u27BF]|\uFE0F|[\uD83C-\uD83E][\uDC00-\uDFFF]");

    [Fact]
    public void NewText_HasNoDashesOrEmojis()
    {
        foreach (string path in new[] { NotesPath, SteamNotesPath, StorePath, "README.md", "DOCS/release-notes/RELEASE_NOTES_1.2.6.md",
                     "DOCS/release-notes/README.md", "DOCS/wiki/reference/changelog.md", "DOCS/wiki/getting-started/accounts.md" })
            DashOrEmoji.IsMatch(Repo(path)).Should().BeFalse(path);
        DashOrEmoji.IsMatch(Note()).Should().BeFalse("the fixture");
    }

    // ---------- 4. Website roadmap in five languages ----------

    private static readonly string[] WebLanguages = { "en", "es", "fr", "hu", "it" };

    [Fact]
    public void Roadmap_127Shipped_128Next_InEveryLanguage()
    {
        string html = Repo("web/index.html");
        var keys = Regex.Matches(html, "data-i18n=\"(r1\\.road_[a-z0-9_]+)\"").Select(m => m.Groups[1].Value).ToList();
        keys.Should().Contain(new[] { "r1.road_127_t", "r1.road_127_d", "r1.road_128_t", "r1.road_128_d" });
        foreach (string lang in WebLanguages)
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(Repo($"web/lang/{lang}.json"))!;
            foreach (string key in keys)
            {
                dict.Should().ContainKey(key, lang);
                dict[key].Should().NotBeNullOrWhiteSpace($"{lang} {key}");
            }
            dict["r1.road_127_t"].Should().StartWith("1.2.7 -- ", lang);
            dict["r1.road_128_t"].Should().StartWith("1.2.8 -- ", lang);
            foreach (string key in new[] { "r1.road_127_t", "r1.road_127_d", "r1.road_128_t", "r1.road_128_d" })
                DashOrEmoji.IsMatch(dict[key]).Should().BeFalse($"{lang} {key}");
            if (lang == "en")
                foreach (string key in new[] { "r1.road_127_t", "r1.road_127_d", "r1.road_128_t", "r1.road_128_d" })
                    html.Should().Contain($"data-i18n=\"{key}\">{dict[key]}<", "the page's own English matches en.json");
        }
    }

    // ---------- S4. The only sentence about addresses is the approved one ----------

    /// <summary>Words that say or imply something about addresses or logging.</summary>
    private static readonly Regex AddressWords = new(@"(?i)address|anonym|\bIPs?\b|\blog(s|ged|ging)?\b|nginx|track");

    /// <summary>The text this release added, the note itself taken out.</summary>
    internal static IEnumerable<(string Name, string Text)> AddedTexts()
    {
        yield return (NotesPath, Repo(NotesPath));
        yield return (SteamNotesPath, Repo(SteamNotesPath));
        yield return (StorePath, Repo(StorePath));
        yield return ("README.md (Telemetry section and v1.2.7 bullet)", ReadmeAdded());
        string log = Repo("DOCS/wiki/reference/changelog.md");
        int from = log.IndexOf("Version **1.2.7**", StringComparison.Ordinal), to = log.IndexOf("Version **1.2.6**", StringComparison.Ordinal);
        from.Should().BeGreaterThan(0);
        yield return ("DOCS/wiki/reference/changelog.md (the 1.2.7 sentence)", log.Substring(from, to - from));
        string accounts = Repo("DOCS/wiki/getting-started/accounts.md");
        int q = accounts.IndexOf("\n## The combat data question\n", StringComparison.Ordinal);
        q.Should().BeGreaterThan(0, "the accounts guide describes the question");
        int qEnd = accounts.IndexOf("\n## ", q + 1, StringComparison.Ordinal);
        yield return ("DOCS/wiki/getting-started/accounts.md (the question section)", accounts.Substring(q, qEnd - q));
        yield return ("DOCS/wiki/getting-started/accounts.md (its settings lines)",
            string.Join("\n", accounts.Split('\n').Where(l => l.Contains("ombat data") || l.StartsWith("history: 1.2.7", StringComparison.Ordinal))));
    }

    // ---------- 5. Guides ----------

    [Fact]
    public void TheAccountsGuide_DescribesTheQuestion_InTheStartAndLoginFlow_WithoutThePrivacyNote()
    {
        string accounts = Repo("DOCS/wiki/getting-started/accounts.md");
        accounts.Should().Contain("\nchecked: 1.2.7\n").And.Contain("\nhistory: 1.2.7 | ");
        accounts.Should().Contain("before the main menu").And.Contain("asked once after login, after the message of the day");
        accounts.Should().Contain("press U, the Share combat data line").And.Contain("telemetry_prompt, under Privacy): default off");
        accounts.Should().Contain("SysOp console turns combat data sharing on or off with E");
        // the wiki is the website: the note stays in the README and the Steam text (the user's ruling)
        foreach (string line in Note().Split('\n').Where(l => l.Length > 20))
            accounts.Should().NotContain(line);
        Repo("DOCS/wiki/reference/changelog.md").Should().NotContain("Telemetry (opt in).");
    }

    [Fact]
    public void NoAddedSentence_SpeaksOfAddressesOrLogging_ButTheApprovedOne()
    {
        string note = Note();
        Count(note, "Addresses\nare not stored with the rows.").Should().Be(1, "the approved sentence");
        AddressWords.Matches(note).Select(m => m.Value).Should().Equal(new[] { "anonym", "Address" }, "the note's own two");
        foreach (var (name, text) in AddedTexts())
        {
            string rest = text.Replace(note, "");
            AddressWords.Matches(rest).Select(m => m.Value).Should().BeEmpty(name);
        }
    }
}
