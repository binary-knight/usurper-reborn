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
/// v1.1.11: a menu line whose code writes the key marker ("[0] ", "L. ", or WriteSRMenuOption's own
/// "[1]") around a localized label whose text also starts with one showed it twice, e.g. the Inn's
/// "[0] [0] Return to inn menu". Where the code writes the marker, the label must not carry one, in
/// any language.
/// </summary>
public class MenuMarkerTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test must run inside the repository");
        return dir!.FullName;
    }

    private static readonly Regex StartsWithMarker = new(@"^\s*(\[[^\]]{1,3}\]|[0-9A-Z]\.)\s");

    // the code writes the marker: "[X] {Loc.Get("key"" or "X. {Loc.Get("key"" in an interpolated string,
    // or the label of WriteSRMenuOption("X", Loc.Get("key"
    private static readonly Regex MarkerInCode = new(
        "(?:(?:\\[[^\\]\\s\"{}]{1,3}\\]|\\b[0-9A-Z]\\.) ?\\{Loc\\.Get\\(\\s*\"([^\"]+)\")|(?:WriteSRMenuOption\\(\\s*\"[^\"]{1,3}\"\\s*,\\s*Loc\\.Get\\(\\s*\"([^\"]+)\")");

    [Fact]
    public void WhereTheCodeWritesTheMarker_TheLabelDoesNotCarryOne()
    {
        string root = RepoRoot();
        var languages = new[] { "en", "es", "fr", "hu", "it" }.ToDictionary(l => l,
            l => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(Path.Combine(root, "Localization", l + ".json")))!);
        var problems = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "Scripts"), "*.cs", SearchOption.AllDirectories))
        {
            string src = File.ReadAllText(file);
            foreach (Match m in MarkerInCode.Matches(src))
            {
                string key = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                int line = src.Take(m.Index).Count(c => c == '\n') + 1;
                foreach (var (lang, table) in languages)
                    if (table.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String && StartsWithMarker.IsMatch(v.GetString()!))
                        problems.Add($"{Path.GetRelativePath(root, file)}:{line} {key} ({lang}): \"{v.GetString()}\"");
            }
        }
        problems.Should().BeEmpty();
    }
}
