using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.15: yesno-loc. Four AskYesNoAsync prompts (CastleLocation crown join, HomeLocation upgrade
/// confirm, MagicShopLocation team curse removal and the good-alignment dark-magic proceed check)
/// passed hardcoded English straight into TerminalEmulator.AskYesNoAsync, bypassing Loc entirely.
/// The repo sweep below found three more of the same shape in SysOpConsoleManager (ban, unban,
/// kick confirm). All seven now go through Loc.Get. This is a source check, modelled on
/// YesNoGuard1115Tests, that no AskYesNoAsync/AskYesNoKeyAsync/ConfirmAsync call anywhere in Scripts
/// (outside TerminalEmulator.cs and Scripts/BBS/, the plumbing itself) passes a raw string literal
/// containing a letter as its prompt; a Loc.Get(...) call, a plain variable, or an empty/"&gt; "/
/// punctuation-only literal are all fine. A second test confirms every key this change added exists
/// in all five Localization/*.json files.
/// </summary>
[Collection("SharedGameSingletons")]
public class YesNoLoc1115Tests
{
    /// <summary>The shared prompt itself; not in scope for its own check.</summary>
    private static bool IsHelper(string rel) =>
        rel == "Scripts/UI/TerminalEmulator.cs" ||
        rel.StartsWith("Scripts/BBS/");

    /// <summary>The code part of a line: everything before "//", so a commented-out call does not count.</summary>
    private static string CodeOnly(string line)
    {
        int c = line.IndexOf("//", StringComparison.Ordinal);
        return c >= 0 ? line.Substring(0, c) : line;
    }

    /// <summary>
    /// Repeatedly removes the innermost {...} interpolation hole until none remain, so a Loc.Get(...)
    /// call (or any other expression, including one with its own nested interpolated string) embedded
    /// in an interpolated prompt cannot leave stray letters or quotes behind for the literal check
    /// below; only the literal wrapper text (spacing, punctuation) around the hole is left to inspect.
    /// </summary>
    private static string StripInterpolationHoles(string line)
    {
        string prev;
        do
        {
            prev = line;
            line = Regex.Replace(line, @"\{[^{}]*\}", "");
        } while (line != prev);
        return line;
    }

    /// <summary>Matches the prompt call with a raw string literal (plain or interpolated) as its first argument.</summary>
    private static readonly Regex CallWithLiteral = new(
        @"(?:AskYesNoAsync|AskYesNoKeyAsync|ConfirmAsync)\(\s*\$?""([^""]*)""",
        RegexOptions.Compiled);

    internal static List<(int line, string text)> FindHits(string source)
    {
        var hits = new List<(int, string)>();
        var lines = source.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string stripped = StripInterpolationHoles(CodeOnly(lines[i]));
            foreach (Match m in CallWithLiteral.Matches(stripped))
            {
                if (Regex.IsMatch(m.Groups[1].Value, "[A-Za-z]"))
                    hits.Add((i + 1, lines[i].Trim()));
            }
        }
        return hits;
    }

    [Fact]
    public void NoYesNoPromptCall_TakesAHardcodedEnglishLiteral()
    {
        string root = FloorAndStun1113Tests.RepoRoot();
        var found = new List<string>();
        foreach (var path in Directory.GetFiles(Path.Combine(root, "Scripts"), "*.cs", SearchOption.AllDirectories).OrderBy(p => p))
        {
            string rel = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (IsHelper(rel)) continue;
            foreach (var (line, text) in FindHits(File.ReadAllText(path)))
                found.Add($"{rel}:{line}: {text}");
        }
        found.Should().BeEmpty(
            "a yes/no prompt should come from Loc.Get (or an empty/\"> \"/punctuation-only literal), " +
            "not hardcoded English, so every supported language shows its own hint");
    }

    [Fact]
    public void TheCheck_CatchesAHardcodedLiteral_AndLeavesLocGetAndPlaceholdersAlone()
    {
        // canary: each of these still carries hardcoded English into the prompt
        string bad = string.Join("\n", new[]
        {
            "        if (await terminal.AskYesNoAsync(\"Join The Crown? (Y/N) \"))",
            "        if (await terminal.AskYesNoAsync($\" Ban '{target.DisplayName}'? (Y/N): \")) return;",
            "        bool c = await terminal.ConfirmAsync(\"Type Y to confirm: \");",
        });
        FindHits(bad).Should().HaveCount(3);

        // each of these is a real converted site, a helper prompt, or not a yes/no prompt at all
        string good = string.Join("\n", new[]
        {
            "        if (await terminal.AskYesNoAsync(Loc.Get(\"castle.crown_join_yn\"))) { }",
            "        if (await terminal.AskYesNoAsync($\"  {Loc.Get(\"magic_shop.proceed_yn\")}\")) { }",
            "        if (!await terminal.AskYesNoAsync($\"  {Loc.Get(\"magic_shop.buy_confirm\", $\"{cost:N0}\")}\")) return;",
            "        return await terminal.AskYesNoAsync($\"{message}? ({hint}): \", enterDefault: defaultYes);",
            "        if (await terminal.AskYesNoAsync(\"\")) { }",
            "        if (await terminal.AskYesNoAsync(\"> \")) { }",
            "        if (await terminal.AskYesNoAsync(\"  \")) { }",
            "        // if (await terminal.AskYesNoAsync(\"Commented out\")) { }",
        });
        FindHits(good).Should().BeEmpty();
    }

    /// <summary>The Loc keys this change added, one per new/reused-with-new-args prompt site.</summary>
    private static readonly string[] NewKeys =
    {
        "castle.crown_join_yn",
        "ui.yn_prompt",
        "magic_shop.curse_confirm_team",
        "sysop.ban_confirm",
        "sysop.unban_confirm",
    };

    [Fact]
    public void EveryNewYesNoLocKey_ExistsInAllFiveLanguages()
    {
        string root = FloorAndStun1113Tests.RepoRoot();
        foreach (var lang in new[] { "en", "es", "fr", "hu", "it" })
        {
            string text = File.ReadAllText(Path.Combine(root, "Localization", lang + ".json"));
            foreach (var key in NewKeys)
                text.Should().Contain($"\"{key}\":", $"{lang}.json should carry {key}");
        }
    }
}
