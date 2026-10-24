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
/// 1.3.0 "Reckoning": the version and its on-screen name, the three start-up rows that name the release in
/// five languages, the release notes, the Steam text and the README bullet (the facts tied to GameConfig, the
/// framing sentences, no dashes or emojis, no other version number for this release), and the website roadmap
/// in five languages. The 1.2.7 privacy note stays word for word in the README, the 1.2.7 notes and the
/// Steam store file. Nothing here touches the network.
/// </summary>
public class Bump130Tests
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

    /// <summary>A text with every run of spaces and line ends read as one space.</summary>
    internal static string Flat(string text) => Regex.Replace(text, @"\s+", " ");

    internal const string NotesPath = "DOCS/release-notes/RELEASE_NOTES_1.3.0.md";
    internal const string SteamNotesPath = "DOCS/release-notes/steam/STEAM_RELEASE_NOTES_1.3.0.txt";

    private static readonly string[] Languages = { "en", "es", "fr", "hu", "it" };

    private static int Count(string text, string part)
    {
        int n = 0;
        for (int i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal)) n++;
        return n;
    }

    /// <summary>The README bullet of this release.</summary>
    internal static string ReadmeBullet() =>
        Repo("README.md").Split('\n').Single(l => l.StartsWith("- **v1.3.0 \"Reckoning\":**", StringComparison.Ordinal));

    /// <summary>The roadmap section of the website page.</summary>
    internal static string RoadmapHtml()
    {
        string html = Repo("web/index.html");
        int start = html.IndexOf("<section id=\"roadmap\">", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, "the page has a roadmap section");
        return html.Substring(start, html.IndexOf("</section>", start, StringComparison.Ordinal) - start);
    }

    private static Dictionary<string, string> WebLang(string lang) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(Repo($"web/lang/{lang}.json"))!;

    private static string GameText(string lang, string key)
    {
        using var doc = JsonDocument.Parse(Repo($"Localization/{lang}.json"));
        return doc.RootElement.GetProperty(key).GetString()!;
    }

    // ---------- 1. Version ----------

    [Fact]
    public void Version_Is130Reckoning_AndTheTelemetryBodySays130()
    {
        GameConfig.Version.Should().StartWith("1.3.");   // the exact patch is pinned in Bump131Tests
        GameConfig.VersionName.Should().Be("Reckoning");
        byte[] body = TelemetryUploader.BuildBody(new List<string> { TelemetryBody127Tests.MaxLine() },
            TelemetryBody127Tests.FixtureInstallId, TelemetrySource.Single);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("version").EnumerateArray().Select(v => v.GetInt64()).Should().Equal(1L, 3L, long.Parse(GameConfig.Version.Split('.')[2]));
    }

    // ---------- 2. The start-up rows that name the release ----------

    [Fact]
    public void StartUpRows_Name13AndTheReleaseName_InEveryLanguage_AndFit()
    {
        foreach (string lang in Languages)
        {
            string compact = GameText(lang, "engine.alpha_compact"), sr = GameText(lang, "engine.alpha_sr"),
                box = GameText(lang, "engine.alpha_box_title");
            foreach (string row in new[] { compact, sr, box })
            {
                row.Should().Contain("1.3", lang).And.NotContain("1.2", lang);
                row.Should().NotContain("Devotion", lang).And.NotContain("hítat", lang).And.NotContain("HÍTAT", lang);
            }
            // Hungarian translates the name in these two rows; engine.title_version prints VersionName as it is
            sr.Should().Contain(lang == "hu" ? "\"Számadás\"" : "\"Reckoning\"", lang);
            box.Should().Be(lang == "hu" ? "USURPER REBORN 1.3 -- \"SZÁMADÁS\"" : "USURPER REBORN 1.3 -- \"Reckoning\"", lang);
            box.Length.Should().BeLessOrEqualTo(72, $"{lang}: the box row is 72 columns (BoxInner in GameEngine)");
            compact.Length.Should().BeLessOrEqualTo(79, lang);
            sr.Length.Should().BeLessOrEqualTo(79, lang);
            GameText(lang, "engine.title_version").Should().Be("v{0} \"{1}\"", lang);
        }
        $"v{GameConfig.Version} \"{GameConfig.VersionName}\"".Length.Should().BeLessOrEqualTo(80, "the splash row is centred on 80 columns");
    }

    // ---------- 3. No dashes, no emojis ----------

    private static readonly Regex DashOrEmoji = new(@"[\u2013\u2014]|[\u2600-\u27BF]|\uFE0F|[\uD83C-\uD83E][\uDC00-\uDFFF]");

    [Fact]
    public void NotesAndSteamText_Exist_AndTheNewTextHasNoDashesOrEmojis()
    {
        Repo(NotesPath).Should().StartWith("# Usurper Reborn v1.3.0 \"Reckoning\"\n");
        Repo(SteamNotesPath).Should().StartWith("[h1]Usurper Reborn 1.3.0 \"Reckoning\"[/h1]\n").And.Contain("\nFull notes: " + NotesPath + "\n");
        Repo("DOCS/release-notes/README.md").Should().Contain("\n- [v1.3.0](RELEASE_NOTES_1.3.0.md)\n- [v1.2.7](RELEASE_NOTES_1.2.7.md)\n");
        foreach (string path in new[] { NotesPath, SteamNotesPath, "README.md", "DOCS/release-notes/README.md",
                     "DOCS/release-notes/RELEASE_NOTES_1.2.7.md", StorePath, "DOCS/release-notes/steam/STEAM_RELEASE_NOTES_1.2.7.txt" })
            DashOrEmoji.IsMatch(Repo(path)).Should().BeFalse(path);
        DashOrEmoji.IsMatch(Note()).Should().BeFalse("the fixture");
        foreach (string lang in Languages)
            foreach (var pair in WebLang(lang).Where(p => p.Key.StartsWith("r1.road_", StringComparison.Ordinal)))
                DashOrEmoji.IsMatch(pair.Value).Should().BeFalse($"{lang} {pair.Key}");
    }

    // ---------- 4. This release is 1.3.0 in every new text ----------

    /// <summary>The number this release was built under, composed so that this file does not hold it.</summary>
    private const string BuiltAs = "1.2." + "8";

    [Fact]
    public void NoNewText_CallsThisRelease128()
    {
        Repo(NotesPath).Should().NotContain(BuiltAs);
        Repo(SteamNotesPath).Should().NotContain(BuiltAs);
        ReadmeBullet().Should().NotContain(BuiltAs);
        RoadmapHtml().Should().NotContain(BuiltAs);
        foreach (string lang in Languages)
            foreach (var pair in WebLang(lang).Where(p => p.Key.StartsWith("r1.road_", StringComparison.Ordinal)))
            {
                pair.Key.Should().NotContain("128", lang);
                pair.Value.Should().NotContain(BuiltAs, $"{lang} {pair.Key}");
            }
    }

    // ---------- 5. The numbers in the notes are the game's ----------

    [Fact]
    public void TheAtonementAndToHitNumbers_InTheNotes_AreTheGameConfigConstants()
    {
        string notes = Flat(Repo(NotesPath)), steam = Flat(Repo(SteamNotesPath));
        string atonement = $"{GameConfig.BetrayalAtonementCostPerLevel} gold per level, at least {GameConfig.BetrayalAtonementMinCost}, once a day";
        atonement.Should().Be("100 gold per level, at least 100, once a day");
        Count(notes, "an atonement at the Temple costing " + atonement).Should().Be(1);
        Count(steam, "an atonement costs " + atonement).Should().Be(1);

        string atLevel = $"you always need {GameConfig.PlayerHitMaxNeededAtLevel} or more";
        atLevel.Should().Be("you always need 2 or more");
        string above = $"you need up to {GameConfig.PlayerHitMaxNeededAbove} or more";
        foreach (string text in new[] { notes, steam })
        {
            Count(text, "Against a monster at your level or up to 5 levels above " + atLevel).Should().Be(1);
            Count(text, "more than 5 levels above " + above).Should().Be(1);
        }
        notes.Should().Contain($"\"[You roll 14; you need {GameConfig.PlayerHitMaxNeededAtLevel} or more]\"")
            .And.Contain($"Precise Strike now needs {GameConfig.PreciseStrikeNeededBonus} less on the hit roll")
            .And.Contain($"The roll never needs less than {GameConfig.PlayerHitMinNeeded},");
    }

    // ---------- 6. The facts the notes must carry ----------

    [Fact]
    public void TheNotes_CarryTheSysopItem_AndTheOtherRequiredFacts()
    {
        string notes = Flat(Repo(NotesPath));
        notes.Should().Contain("Sysops: back up the database before upgrading. The clean roll-back is restoring that backup. "
            + "Running 1.2.7 on a 1.3.0 database loads but loses betrayal state, the hired-NPC ageing grace and the remembered login. "
            + "Two versions on one database at once is not supported.");
        notes.Should().Contain("the combat log table (combat_events) gains three columns when the server first starts on 1.3.0: "
            + "player_swings, player_misses and elite_count.");
        notes.Should().Contain("A Seth win no longer adds the small combat reward it paid before (about 17 XP and some gold) or a loot roll.");
        notes.Should().Contain("An online permadeath waits up to 10 seconds for the fight's telemetry row to be written");
        notes.Should().Contain("The website's Content-Security-Policy is enforced (it was report-only since 1.2.6), with the same policy.")
            .And.Contain("were checked with a violation observer: 0 violations.")
            .And.Contain("The 1.2.6 check had read the browser console, which does not show these notices.")
            .And.Contain("The enforcing header reaches the live site when the server's nginx snippet is updated at release.");
        notes.Should().Contain("Rooms you cleared on a dungeon floor stay cleared after a disconnect, a reconnect or a save taken on the floor");
        notes.Should().Contain("A server older than 1.3.0 cannot remember logins; the game asks for the password there.");
        notes.Should().Contain("In a party some monster families prefer a badly wounded target, and some prefer casters and healers; "
            + "a taunt still comes first.").And.NotContain("lightly armoured");
        notes.Should().Contain("What a sysop can do: serve every player from one game process, the single-process server "
            + "(--mud-server, with sessions through --mud-relay; see DOCS/SERVER_DEPLOYMENT.md), which runs one world simulation.");
        Repo(NotesPath).Should().Contain("\n## Known issues\n");
        Flat(Repo(SteamNotesPath)).Should().Contain("Sysops: back up the database before upgrading. The clean roll-back is restoring that backup.");
    }

    // ---------- 7. The framing and the level sentence ----------

    internal const string FirstStep = "1.3.0 is the first step of a longer rework of dungeon difficulty.";
    internal const string Framing = "Its targets were set for dungeon floors 1 to 10, measured with a combat simulator. "
        + "Elites, monster tactics, the hit roll and scaled potions are also active on deeper floors, which have not been tuned yet.";
    internal const string LevelSentence = "A character whose level equals the dungeon floor will find floors 6 to 10 harder than before, "
        + "most of all with a companion. The first time you enter the dungeon the game starts you on the floor equal to your level; "
        + "you can pick a lower floor at the entrance.";

    [Fact]
    public void TheFraming_StandsInTheNotes_TheSteamText_AndTheReadmeBullet_WithNoComingNextHeading()
    {
        var texts = new (string Name, string Text)[] { (NotesPath, Flat(Repo(NotesPath))), (SteamNotesPath, Flat(Repo(SteamNotesPath))),
            ("the README bullet", ReadmeBullet()) };
        foreach (var (name, text) in texts)
        {
            Count(text, FirstStep).Should().Be(1, name);
            Count(text, Framing).Should().Be(1, name);
            text.Should().NotContain("The new mechanics are active", name);
            text.IndexOf(FirstStep, StringComparison.Ordinal).Should().BeLessThan(text.IndexOf(Framing, StringComparison.Ordinal), name);
            foreach (string step in new[] { "1.3.1", "1.3.2", "1.3.3", "1.3.4", "floors 11 to 25", "floors 26 to 50", "floors 51 to 100" })
                text.Should().Contain(step, name);
            Regex.IsMatch(text, "(?i)coming (next|in)\\b").Should().BeFalse(name);
            Regex.IsMatch(text, "(?i)live level|level players have|level that players have").Should().BeFalse(name);
        }
        // the notes open with it: nothing but the title and one heading stands before the first sentence
        string notes = Repo(NotesPath);
        notes.Substring(0, notes.IndexOf("1.3.0 is the first step", StringComparison.Ordinal)).Split('\n').Where(l => l.Length > 0)
            .Should().Equal("# Usurper Reborn v1.3.0 \"Reckoning\"", "## The first step of a difficulty rework");
        Regex.IsMatch(notes, "(?im)^#+ .*coming").Should().BeFalse("no Coming next or Coming in heading");
        Count(Flat(notes), LevelSentence).Should().Be(1, "the level sentence, in the notes");
        Count(Flat(Repo(SteamNotesPath)), LevelSentence).Should().Be(1, "the same two sentences, in the Steam text");
    }

    // ---------- 9. The 1.2.7 privacy note stays word for word ----------

    internal static string Note() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "telemetry-privacy-note-en.txt"), Encoding.UTF8).Replace("\r\n", "\n");

    internal const string StorePath = "DOCS/release-notes/steam/STEAM_STORE_PRIVACY_1.2.7.txt";

    /// <summary>The README's Telemetry section and its v1.2.7 bullet.</summary>
    internal static string ReadmeTelemetry()
    {
        string readme = Repo("README.md");
        int start = readme.IndexOf("\n## Telemetry\n", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, "the README has a Telemetry section");
        int end = readme.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        string section = readme.Substring(start, end - start);
        string bullet = readme.Split('\n').Single(l => l.StartsWith("- **v1.2.7:**", StringComparison.Ordinal));
        return section + "\n" + bullet;
    }

    [Fact]
    public void PrivacyNote_IsTheApprovedText_InTheReadme_The127Notes_AndTheStoreFile()
    {
        string note = Note();
        note.Should().StartWith("Telemetry (opt in). From 1.2.7 the game asks once whether to share\n");
        note.Should().EndWith("sharing off. Turning it off deletes anything not yet sent.\n");
        Count(Repo("README.md"), note).Should().Be(1, "the README quotes the note once, word for word");
        Count(ReadmeTelemetry(), note).Should().Be(1, "inside its Telemetry section");
        Count(Repo("DOCS/release-notes/RELEASE_NOTES_1.2.7.md"), note).Should().Be(1, "the 1.2.7 notes quote the note once, word for word");
        Repo(StorePath).Should().Be(note, "the Steam store file is the note and nothing else");
    }

    /// <summary>Words that say or imply something about addresses or logging.</summary>
    private static readonly Regex AddressWords = new(@"(?i)address|anonym|\bIPs?\b|\blog(s|ged|ging)?\b|nginx|track");

    [Fact]
    public void NoTelemetrySentence_SpeaksOfAddressesOrLogging_ButTheApprovedOne()
    {
        string note = Note();
        Count(note, "Addresses\nare not stored with the rows.").Should().Be(1, "the approved sentence");
        AddressWords.Matches(note).Select(m => m.Value).Should().Equal(new[] { "anonym", "Address" }, "the note's own two");
        var texts = new (string Name, string Text)[]
        {
            ("DOCS/release-notes/RELEASE_NOTES_1.2.7.md", Repo("DOCS/release-notes/RELEASE_NOTES_1.2.7.md")),
            ("DOCS/release-notes/steam/STEAM_RELEASE_NOTES_1.2.7.txt", Repo("DOCS/release-notes/steam/STEAM_RELEASE_NOTES_1.2.7.txt")),
            (StorePath, Repo(StorePath)),
            ("README.md (Telemetry section and v1.2.7 bullet)", ReadmeTelemetry()),
        };
        foreach (var (name, text) in texts)
            AddressWords.Matches(text.Replace(note, "")).Select(m => m.Value).Should().BeEmpty(name);
    }

    // ---------- 11. The guides at 1.3.0 ----------

    /// <summary>Every guide of the wiki: its path in the repository and its text.</summary>
    private static IEnumerable<(string File, string Text)> Guides()
    {
        string root = RepoRoot();
        foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "DOCS", "wiki"), "*.md", SearchOption.AllDirectories)
                     .OrderBy(f => f, StringComparer.Ordinal))
            yield return (Path.GetRelativePath(root, file).Replace('\\', '/'),
                File.ReadAllText(file, Encoding.UTF8).Replace("\r\n", "\n"));
    }

    /// <summary>The guides whose sources changed in 1.3.0 while nothing the page describes did.</summary>
    private static readonly string[] UnchangedAt130 =
    {
        "DOCS/wiki/characters/classes.md", "DOCS/wiki/characters/index.md", "DOCS/wiki/characters/leveling.md",
        "DOCS/wiki/characters/mental.md", "DOCS/wiki/characters/specializations.md", "DOCS/wiki/combat/fleeing.md",
        "DOCS/wiki/getting-started/connections.md", "DOCS/wiki/getting-started/reporting-bugs.md", "DOCS/wiki/gods/index.md",
        "DOCS/wiki/gods/player-gods.md", "DOCS/wiki/reference/balance.md", "DOCS/wiki/world/anchor-road.md",
        "DOCS/wiki/world/rare-encounters.md",
    };

    [Fact]
    public void EveryGuideLineFor130_IsAPlainSentence_OrTheHiddenMarkerOnThe13UnchangedPages_AndTheReleaseHistoryNames130()
    {
        const string prefix = "history: 1.3.0 | ";
        int lines = 0;
        var marked = new List<string>();
        foreach (var (file, text) in Guides())
        {
            foreach (string line in text.Split('\n').Where(l => l.StartsWith("history: 1.3.0", StringComparison.Ordinal)))
            {
                lines++;
                line.Should().StartWith(prefix, file);
                // "none" is the marker the page does not show; any other line is shown: a sentence in plain words
                if (line.Substring(prefix.Length) == "none") marked.Add(file);
                else line.Should().EndWith(".", file).And.NotContain("Nothing this page describes changed", file);
                DashOrEmoji.IsMatch(line).Should().BeFalse(file);
                text.Should().MatchRegex(@"\nchecked: 1\.3\.[0-7]\n", file);
            }
            text.Should().NotContain(BuiltAs, file);
        }
        lines.Should().BeGreaterThanOrEqualTo(44, "every guide re-checked at the bump has its line");
        marked.OrderBy(f => f, StringComparer.Ordinal)
            .Should().Equal(UnchangedAt130, "the marker stands on the 13 pages whose subject did not change and on no other");

        string log = Repo("DOCS/wiki/reference/changelog.md");
        log.Should().Contain("DOCS/release-notes/RELEASE_NOTES_1.3.0.md, ").And.Contain("Version **1.3.0** ")
            .And.Contain("blob/v1.3.0/" + NotesPath + ")");
        DashOrEmoji.IsMatch(log).Should().BeFalse("the release history");
    }

    [Fact]
    public void TheAccountsGuide_DescribesTheLoginToken_AndTheServerKeyCheck_At130()
    {
        string accounts = Repo("DOCS/wiki/getting-started/accounts.md");
        accounts.Should().MatchRegex(@"\nchecked: 1\.3\.[0-7]\n").And.Contain("\nhistory: 1.3.0 | ").And.Contain("\n## Staying logged in\n");
        accounts.Should().Contain("saves a login token from the server, not your password").And.Contain("before the login is sent");
        accounts.Should().Contain($"A token expires after {SqlSaveBackend.LoginTokenDays} days.");
        // the question as the game asks it
        string question = GameText("en", "online.remember_prompt");
        accounts.Should().Contain("\"" + question.Substring(0, question.IndexOf('?') + 1) + "\"");
        accounts.Should().Contain("any other key is refused there").And.Contain("remembered on the first connection");
        // the 1.2.7 section stays
        accounts.Should().Contain("\n## The combat data question\n").And.Contain("press U, the Share combat data line");
        DashOrEmoji.IsMatch(accounts).Should().BeFalse("the accounts guide");
    }
}
