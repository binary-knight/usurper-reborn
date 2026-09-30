using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// bug-guide (1.2.2): the in-game bug report posts to the developer through the
/// website; it no longer opens a GitHub issue. No bug-related string, in any of the
/// five shipped languages, may say it opens or goes to GitHub.
/// </summary>
public class BugReportLoc122Tests
{
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };

    // Keys for the bug report screen, its help and menu labels, and the title-screen report hints.
    private static readonly Regex BugKey = new(@"(^|[._])bug([._]|$)|^engine\.alpha_(compact|report|box_report)$");

    private static string LocDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Localization", "en.json")))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test must run inside the repository");
        return Path.Combine(dir!.FullName, "Localization");
    }

    private static Dictionary<string, string> BugStrings(string lang)
    {
        var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            File.ReadAllText(Path.Combine(LocDir(), lang + ".json")))!;
        return doc.Where(kv => kv.Value.ValueKind == JsonValueKind.String && BugKey.IsMatch(kv.Key))
                  .ToDictionary(kv => kv.Key, kv => kv.Value.GetString() ?? "");
    }

    [Fact]
    public void Bug_related_strings_never_mention_GitHub()
    {
        foreach (var lang in Langs)
        {
            var strings = BugStrings(lang);
            strings.Keys.Should().Contain(new[] { "base.help_bug", "base.help_key_bug", "bug_report.sent_success", "engine.alpha_report" },
                $"{lang}.json must still carry the bug report strings this test guards");
            var offenders = strings.Where(kv => kv.Value.Contains("github", StringComparison.OrdinalIgnoreCase))
                                   .Select(kv => $"{kv.Key}: {kv.Value}").ToList();
            offenders.Should().BeEmpty($"{lang}.json bug strings must not claim the report opens GitHub");
        }
    }

    [Fact]
    public void Help_entry_says_the_report_goes_to_the_developer()
    {
        BugStrings("en")["base.help_bug"].Should().Contain("developer");
    }
}
