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
                     "DOCS/release-notes/README.md", "DOCS/wiki/reference/changelog.md" })
            DashOrEmoji.IsMatch(Repo(path)).Should().BeFalse(path);
        DashOrEmoji.IsMatch(Note()).Should().BeFalse("the fixture");
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
