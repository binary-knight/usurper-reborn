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
/// Ratchet on hardcoded player-visible English (see HardcodedTextScanner). Each file under Scripts/
/// may hold exactly as many sites as Tests/Localization/hardcoded-baseline.json says; a file not in
/// the baseline holds 0. More fails (the new sites are listed); fewer also fails until the baseline
/// is lowered, so the numbers stay exact and only go down.
///
/// Update the baseline after localizing strings:
///   Tests/Localization/loc-scan.sh --write-baseline
///
/// The baseline keeps a count and a short hash of each site (sink and literal, no line number) per
/// file. The hashes only pick out which sites are new for the failure message; the check is on counts.
/// </summary>
public class HardcodedTextRatchetTests
{
    internal const string BaselinePath = "Tests/Localization/hardcoded-baseline.json";
    private const string UpdateCommand = "Tests/Localization/loc-scan.sh --write-baseline";

    public sealed class FileEntry
    {
        public int Count { get; set; }
        public List<string> Sites { get; set; } = new();
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    internal static string SiteHash(HardcodedTextScanner.Site s)
    {
        byte[] h = SHA256.HashData(Encoding.UTF8.GetBytes(s.Sink + "\n" + s.Literal));
        return Convert.ToHexString(h, 0, 5).ToLowerInvariant();
    }

    internal static SortedDictionary<string, FileEntry> BuildBaseline(IEnumerable<HardcodedTextScanner.Site> sites)
    {
        var d = new SortedDictionary<string, FileEntry>(StringComparer.Ordinal);
        foreach (var g in sites.GroupBy(s => s.File))
            d[g.Key] = new FileEntry { Count = g.Count(), Sites = g.Select(SiteHash).OrderBy(x => x, StringComparer.Ordinal).ToList() };
        return d;
    }

    internal static string SerializeBaseline(SortedDictionary<string, FileEntry> baseline) =>
        JsonSerializer.Serialize(baseline, Json).Replace("\r\n", "\n") + "\n";

    internal static Dictionary<string, FileEntry> LoadBaseline(string root) =>
        JsonSerializer.Deserialize<Dictionary<string, FileEntry>>(File.ReadAllText(Path.Combine(root, BaselinePath)), Json)
        ?? new Dictionary<string, FileEntry>();

    /// <summary>Returns one message per file whose count differs from the baseline.</summary>
    internal static List<string> Compare(string root, IReadOnlyList<HardcodedTextScanner.Site> sites, IReadOnlyDictionary<string, FileEntry> baseline)
    {
        var problems = new List<string>();
        var byFile = sites.GroupBy(s => s.File).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        foreach (var file in byFile.Keys.Union(baseline.Keys).OrderBy(f => f, StringComparer.Ordinal))
        {
            int actual = byFile.TryGetValue(file, out var list) ? list.Count : 0;
            int allowed = baseline.TryGetValue(file, out var entry) ? entry.Count : 0;
            if (actual > allowed)
            {
                var known = (entry?.Sites ?? new List<string>()).GroupBy(h => h).ToDictionary(g => g.Key, g => g.Count());
                var fresh = new List<HardcodedTextScanner.Site>();
                foreach (var s in list!)
                {
                    string h = SiteHash(s);
                    if (known.TryGetValue(h, out int n) && n > 0) known[h] = n - 1;
                    else fresh.Add(s);
                }
                if (fresh.Count == 0) fresh = list!; // baseline hashes out of step: show the whole file
                var sb = new StringBuilder();
                sb.Append($"{file}: {actual} hardcoded sites, baseline {allowed}. New sites (use Loc.Get):");
                foreach (var s in fresh)
                    sb.Append($"\n    {s.File}:{s.Line} {s.Sink} {s.Literal}");
                problems.Add(sb.ToString());
            }
            else if (actual < allowed)
            {
                if (!File.Exists(Path.Combine(root, file)))
                    problems.Add($"{file}: in the baseline but the file no longer exists; remove it ({UpdateCommand}).");
                else
                    problems.Add($"{file}: {actual} hardcoded sites, baseline {allowed}. Lower the baseline to {actual} ({UpdateCommand}).");
            }
        }
        return problems;
    }

    [Fact]
    public void HardcodedText_MatchesBaselineExactly()
    {
        string root = HardcodedTextScannerTests.RepoRoot();
        var sites = HardcodedTextScanner.ScanRepo(root, HardcodedTextScannerTests.Exclusions());
        var problems = Compare(root, sites, LoadBaseline(root));
        problems.Should().BeEmpty(
            "player-visible text goes through Loc.Get; the per file count of hardcoded sites may only go down, and the baseline must follow it down");
    }

    [Fact]
    public void Baseline_IsWellFormed()
    {
        var baseline = LoadBaseline(HardcodedTextScannerTests.RepoRoot());
        baseline.Should().NotBeEmpty();
        foreach (var (file, entry) in baseline)
        {
            file.Should().StartWith("Scripts/");
            entry.Count.Should().BePositive($"{file} should be removed rather than kept at 0");
            entry.Sites.Should().HaveCount(entry.Count, $"{file} keeps one hash per site");
        }
    }

    [Fact]
    public void Compare_FlagsMoreAndFewerAndMissing()
    {
        string root = HardcodedTextScannerTests.RepoRoot();
        var site = new HardcodedTextScanner.Site("Scripts/Systems/CombatEngine.cs", 10, "terminal.WriteLine", "\"New words\"");
        var baseline = new Dictionary<string, FileEntry>
        {
            ["Scripts/Core/Pacing.cs"] = new FileEntry { Count = 2, Sites = new() { "a", "b" } },
            ["Scripts/Gone.cs"] = new FileEntry { Count = 1, Sites = new() { "c" } },
        };
        var problems = Compare(root, new[] { site }, baseline);
        problems.Should().HaveCount(3);
        problems[0].Should().Be("Scripts/Core/Pacing.cs: 0 hardcoded sites, baseline 2. Lower the baseline to 0 (Tests/Localization/loc-scan.sh --write-baseline).");
        problems[1].Should().Be("Scripts/Gone.cs: in the baseline but the file no longer exists; remove it (Tests/Localization/loc-scan.sh --write-baseline).");
        problems[2].Should().Contain("Scripts/Systems/CombatEngine.cs: 1 hardcoded sites, baseline 0.")
            .And.Contain("Scripts/Systems/CombatEngine.cs:10 terminal.WriteLine \"New words\"");
    }
}
