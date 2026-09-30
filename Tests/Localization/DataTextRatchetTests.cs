using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace UsurperReborn.Tests.Localization;

/// <summary>
/// Ratchet on player-visible English outside output calls (see DataTextScanner): data tables,
/// Electron payloads and shown throws. Kept apart from HardcodedTextRatchetTests and its baseline so
/// work on one does not conflict with the other. Each file and category under Scripts/ may hold
/// exactly as many sites as Tests/Localization/hardcoded-data-baseline.json says; anything not in the
/// baseline holds 0. More fails (the new sites are listed); fewer fails until the baseline is lowered.
///
/// Update the baseline after localizing strings:
///   Tests/Localization/loc-scan.sh --write-data-baseline
/// </summary>
public class DataTextRatchetTests
{
    internal const string BaselinePath = "Tests/Localization/hardcoded-data-baseline.json";
    private const string UpdateCommand = "Tests/Localization/loc-scan.sh --write-data-baseline";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    internal static string SiteHash(DataTextScanner.Site s)
    {
        byte[] h = SHA256.HashData(Encoding.UTF8.GetBytes(s.Category + "\n" + s.Slot + "\n" + s.Literal));
        return Convert.ToHexString(h, 0, 5).ToLowerInvariant();
    }

    internal static SortedDictionary<string, SortedDictionary<string, HardcodedTextRatchetTests.FileEntry>> BuildBaseline(IEnumerable<DataTextScanner.Site> sites)
    {
        var d = new SortedDictionary<string, SortedDictionary<string, HardcodedTextRatchetTests.FileEntry>>(StringComparer.Ordinal);
        foreach (var g in sites.GroupBy(s => s.File))
        {
            var cats = new SortedDictionary<string, HardcodedTextRatchetTests.FileEntry>(StringComparer.Ordinal);
            foreach (var c in g.GroupBy(s => s.Category))
                cats[c.Key] = new HardcodedTextRatchetTests.FileEntry { Count = c.Count(), Sites = c.Select(SiteHash).OrderBy(x => x, StringComparer.Ordinal).ToList() };
            d[g.Key] = cats;
        }
        return d;
    }

    internal static string SerializeBaseline(SortedDictionary<string, SortedDictionary<string, HardcodedTextRatchetTests.FileEntry>> baseline) =>
        JsonSerializer.Serialize(baseline, Json).Replace("\r\n", "\n") + "\n";

    internal static Dictionary<string, Dictionary<string, HardcodedTextRatchetTests.FileEntry>> LoadBaseline(string root) =>
        JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, HardcodedTextRatchetTests.FileEntry>>>(File.ReadAllText(Path.Combine(root, BaselinePath)), Json)
        ?? new Dictionary<string, Dictionary<string, HardcodedTextRatchetTests.FileEntry>>();

    /// <summary>Returns one message per file and category whose count differs from the baseline.</summary>
    internal static List<string> Compare(string root, IReadOnlyList<DataTextScanner.Site> sites,
        IReadOnlyDictionary<string, Dictionary<string, HardcodedTextRatchetTests.FileEntry>> baseline)
    {
        var problems = new List<string>();
        var actualKeys = sites.GroupBy(s => (s.File, s.Category)).ToDictionary(g => g.Key, g => g.ToList());
        var keys = actualKeys.Keys.Union(baseline.SelectMany(f => f.Value.Keys.Select(c => (File: f.Key, Category: c))))
            .OrderBy(k => k.File, StringComparer.Ordinal).ThenBy(k => k.Category, StringComparer.Ordinal);
        foreach (var (file, category) in keys)
        {
            int actual = actualKeys.TryGetValue((file, category), out var list) ? list.Count : 0;
            HardcodedTextRatchetTests.FileEntry? entry = null;
            if (baseline.TryGetValue(file, out var cats)) cats.TryGetValue(category, out entry);
            int allowed = entry?.Count ?? 0;
            if (actual > allowed)
            {
                var known = (entry?.Sites ?? new List<string>()).GroupBy(h => h).ToDictionary(g => g.Key, g => g.Count());
                var fresh = new List<DataTextScanner.Site>();
                foreach (var s in list!)
                {
                    string h = SiteHash(s);
                    if (known.TryGetValue(h, out int n) && n > 0) known[h] = n - 1;
                    else fresh.Add(s);
                }
                if (fresh.Count == 0) fresh = list!;
                var sb = new StringBuilder();
                sb.Append($"{file} [{category}]: {actual} hardcoded sites, baseline {allowed}. New sites (use Loc.Get or a keyed table):");
                foreach (var s in fresh)
                    sb.Append($"\n    {s.File}:{s.Line} {s.Category} {s.Slot} {s.Literal}");
                problems.Add(sb.ToString());
            }
            else if (actual < allowed)
            {
                if (!File.Exists(Path.Combine(root, file)))
                    problems.Add($"{file} [{category}]: in the baseline but the file no longer exists; remove it ({UpdateCommand}).");
                else
                    problems.Add($"{file} [{category}]: {actual} hardcoded sites, baseline {allowed}. Lower the baseline to {actual} ({UpdateCommand}).");
            }
        }
        return problems;
    }

    [Fact]
    public void DataText_MatchesBaselineExactly()
    {
        string root = HardcodedTextScannerTests.RepoRoot();
        var result = DataTextScanner.ScanRepo(root, HardcodedTextScannerTests.Exclusions(), DataTextScannerTests.Sources());
        var problems = Compare(root, result.Sites, LoadBaseline(root));
        problems.Should().BeEmpty(
            "player-visible text in data tables, Electron payloads and shown throws goes through Loc; the per file and category count may only go down, and the baseline must follow it down");
    }

    [Fact]
    public void Baseline_IsWellFormed()
    {
        var baseline = LoadBaseline(HardcodedTextScannerTests.RepoRoot());
        baseline.Should().NotBeEmpty();
        foreach (var (file, cats) in baseline)
        {
            file.Should().StartWith("Scripts/");
            cats.Should().NotBeEmpty();
            foreach (var (category, entry) in cats)
            {
                DataTextScanner.Categories.Should().Contain(category);
                entry.Count.Should().BePositive($"{file} [{category}] should be removed rather than kept at 0");
                entry.Sites.Should().HaveCount(entry.Count, $"{file} [{category}] keeps one hash per site");
            }
        }
    }

    [Fact]
    public void Compare_FlagsMoreAndFewerAndMissing()
    {
        string root = HardcodedTextScannerTests.RepoRoot();
        var site = new DataTextScanner.Site("Scripts/Data/DialogueLines_Greetings.cs", 10, DataTextScanner.Data, "init.Text", "\"New words\"");
        var baseline = new Dictionary<string, Dictionary<string, HardcodedTextRatchetTests.FileEntry>>
        {
            ["Scripts/Core/Pacing.cs"] = new() { ["electron"] = new HardcodedTextRatchetTests.FileEntry { Count = 2, Sites = new() { "a", "b" } } },
            ["Scripts/Gone.cs"] = new() { ["data"] = new HardcodedTextRatchetTests.FileEntry { Count = 1, Sites = new() { "c" } } },
        };
        var problems = Compare(root, new[] { site }, baseline);
        problems.Should().HaveCount(3);
        problems[0].Should().Be("Scripts/Core/Pacing.cs [electron]: 0 hardcoded sites, baseline 2. Lower the baseline to 0 (Tests/Localization/loc-scan.sh --write-data-baseline).");
        problems[1].Should().Contain("Scripts/Data/DialogueLines_Greetings.cs [data]: 1 hardcoded sites, baseline 0.")
            .And.Contain("Scripts/Data/DialogueLines_Greetings.cs:10 data init.Text \"New words\"");
        problems[2].Should().Be("Scripts/Gone.cs [data]: in the baseline but the file no longer exists; remove it (Tests/Localization/loc-scan.sh --write-data-baseline).");
    }
}
