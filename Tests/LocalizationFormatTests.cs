using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;
using FluentAssertions;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.1: every Loc.Get("key", args...) in the source must pass at least as many
/// arguments as the English template has {n} placeholders (string.Format throws
/// otherwise and the player sees a raw "{0}"), and every literal key must exist.
/// Found ten live cases in the 1.1.1 bug pass; this keeps them from coming back.
/// </summary>
public class LocalizationFormatTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test must run inside the repository");
        return dir!.FullName;
    }

    private static int Placeholders(string s) => Regex.Matches(s, @"\{(\d+)").Select(m => int.Parse(m.Groups[1].Value)).DefaultIfEmpty(-1).Max() + 1;

    // Keys whose template is deliberately fetched raw and formatted later by the caller.
    private static readonly HashSet<string> FormattedByCaller = new(StringComparer.Ordinal)
    {
        "inn.drinking_howdy4", "inn.drinking_howdy6",
    };

    [Fact]
    public void Every_literal_loc_key_exists_and_receives_enough_arguments()
    {
        string root = RepoRoot();
        var en = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(Path.Combine(root, "Localization", "en.json")))!;
        var problems = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "Scripts"), "*.cs", SearchOption.AllDirectories))
        {
            string src = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(src, "Loc\\.Get\\(\\s*\"([^\"]+)\"(\\s*\\+)?"))
            {
                if (m.Groups[2].Success) continue; // dynamic key built by concatenation
                string key = m.Groups[1].Value;
                int line = src.Take(m.Index).Count(c => c == '\n') + 1;
                if (!en.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.String)
                {
                    problems.Add($"{Path.GetRelativePath(root, file)}:{line} key '{key}' missing from en.json");
                    continue;
                }
                if (FormattedByCaller.Contains(key)) continue;
                int need = Placeholders(el.GetString()!);
                if (need == 0) continue;
                int passed = CountArguments(src, m.Index + m.Length);
                if (passed < need)
                    problems.Add($"{Path.GetRelativePath(root, file)}:{line} '{key}' needs {need} args, passes {passed}");
            }
        }
        problems.Should().BeEmpty(string.Join("\n", problems));
    }

    private static int CountArguments(string src, int i)
    {
        int depth = 1, n = 0; bool inStr = false;
        for (; i < src.Length && depth > 0; i++)
        {
            char c = src[i];
            if (inStr) { if (c == '\\') i++; else if (c == '"') inStr = false; continue; }
            if (c == '"') inStr = true;
            else if (c == '(' || c == '[' || c == '{') depth++;
            else if (c == ')' || c == ']' || c == '}') depth--;
            else if (c == ',' && depth == 1) n++;
        }
        return n;
    }
}
