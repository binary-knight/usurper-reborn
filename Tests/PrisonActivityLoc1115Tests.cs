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
/// prison-loc: PrisonActivitySystem's player activity path (PerformActivity and the
/// private PerformXxx helpers it calls) used to return hardcoded English sentences.
/// Those are now Loc.Get keys. This locks two things in place: the player path never
/// goes back to a raw literal, and every key it uses exists, with matching {n}
/// placeholders, in all five shipped languages. The NPC world-simulation path
/// (ProcessNPCPrisonerActivity) and the NewsSystem release line are out of scope by
/// design (NPC activities are silent; news is stored in English) and are not touched
/// by this slice.
/// </summary>
public class PrisonActivityLoc1115Tests
{
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test must run inside the repository");
        return dir!.FullName;
    }

    private static string PlayerActivityPathSource()
    {
        string path = Path.Combine(RepoRoot(), "Scripts", "Systems", "PrisonActivitySystem.cs");
        string src = File.ReadAllText(path);

        int start = src.IndexOf("public async Task<string> PerformActivity(", StringComparison.Ordinal);
        int end = src.IndexOf("public List<PrisonActivity> GetAvailableActivities(", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "PerformActivity must still exist");
        end.Should().BeGreaterThan(start, "GetAvailableActivities must still follow PerformActivity");

        // Slice covers PerformActivity plus every private PerformXxx helper it dispatches to
        // (the player's activity path). It excludes the ActivityInfo menu table above it and
        // the NPC path / NewsSystem line below GetAvailableActivities.
        return src.Substring(start, end - start);
    }

    [Fact]
    public void Player_activity_path_has_no_english_string_literal()
    {
        string slice = PlayerActivityPathSource();

        var offenders = new List<string>();
        foreach (Match m in Regex.Matches(slice, "\"(?:[^\"\\\\]|\\\\.)*\""))
        {
            string literal = m.Value[1..^1]; // strip surrounding quotes
            if (literal.Length == 0) continue; // string result = ""; is a scratch accumulator, not player text

            // A Loc.Get key looks like "prison.activity_something": lowercase, digits,
            // underscore and dot only, no spaces. Anything else quoted in this slice is
            // an English literal that slipped back in.
            bool looksLikeLocKey = Regex.IsMatch(literal, "^[a-z][a-z0-9_.]*$");
            if (!looksLikeLocKey)
                offenders.Add(literal);
        }

        offenders.Should().BeEmpty("every player-visible string in PerformActivity and its PerformXxx helpers must come from Loc.Get, not a raw literal");
    }

    [Fact]
    public void Player_activity_path_keys_exist_in_all_languages_with_matching_placeholders()
    {
        string slice = PlayerActivityPathSource();
        var keys = Regex.Matches(slice, "Loc\\.Get\\(\\s*\"(prison\\.activity_[a-z0-9_]+)\"")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        keys.Count.Should().BeGreaterThanOrEqualTo(12, "pushups, yoga, yoga+agility, reading, meditation, shadow boxing, stretching, planning, both prayers, rest and exhausted");

        var byLang = Langs.ToDictionary(l => l, l =>
        {
            string p = Path.Combine(RepoRoot(), "Localization", l + ".json");
            var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(p))!;
            return doc.Where(kv => kv.Value.ValueKind == JsonValueKind.String).ToDictionary(kv => kv.Key, kv => kv.Value.GetString() ?? "");
        });

        static HashSet<string> Placeholders(string s) => Regex.Matches(s, "\\{\\d+\\}").Select(m => m.Value).ToHashSet();

        var problems = new List<string>();
        foreach (var key in keys)
        {
            if (!byLang["en"].TryGetValue(key, out var enText))
            {
                problems.Add($"{key}: missing from en.json");
                continue;
            }
            var enPlaceholders = Placeholders(enText);

            foreach (var lang in Langs)
            {
                if (!byLang[lang].TryGetValue(key, out var text))
                {
                    problems.Add($"{key}: missing from {lang}.json");
                    continue;
                }
                var placeholders = Placeholders(text);
                if (!placeholders.SetEquals(enPlaceholders))
                    problems.Add($"{key}: {lang}.json has placeholders [{string.Join(",", placeholders)}], english has [{string.Join(",", enPlaceholders)}]");
            }
        }

        problems.Should().BeEmpty(string.Join("\n", problems));
    }
}
