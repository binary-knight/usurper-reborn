using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.15: every yes/no prompt now goes through TerminalEmulator.AskYesNoAsync / AskYesNoKeyAsync
/// (and ConfirmAsync, which uses them), which re-asks on anything but a localized yes or no. This is a
/// repo-wide guard, modelled on PauseTypeahead1114Tests, that nothing hand-rolled has crept back in
/// outside the reviewed exceptions (real menus where Y is one option among several), so YesNoConvert
/// 1115A/B do not need their own file-scoped source sweeps kept in step by hand.
/// </summary>
[Collection("SharedGameSingletons")]
public class YesNoGuard1115Tests
{
    /// <summary>A line carrying this marker is a reviewed exception (see ExemptSites).</summary>
    internal const string ExemptMarker = "v1.1.15: yesno-exempt";

    /// <summary>
    /// The reviewed exceptions today: ArmorShopLocation 1, WeaponShopLocation 1 (three-way haggle
    /// menus), CombatEngine 3 (the spare/finish legacy alias chain, one explanatory comment line plus
    /// the two lines of the boolean it documents), OnlineAdminConsole 3 (its own strict-loop building
    /// blocks, plus a blank-or-Y "take the suggestion" branch), OnlinePlaySystem 1 and
    /// StreetEncounterSystem 1 (a third accept key alongside Y/N), VisualNovelDialogueSystem 1 (a kept
    /// legacy "1" alias). A marker counts here whether or not its own line is itself a hit, so a new
    /// exemption anywhere (even a bare explanatory comment) needs a deliberate edit to this constant.
    /// </summary>
    internal const int ExemptSites = 11;

    /// <summary>The shared prompt and the plumbing it is built on; these implement the real yes/no test.</summary>
    private static bool IsHelper(string rel) =>
        rel == "Scripts/UI/TerminalEmulator.cs" ||
        rel == "Scripts/Core/GameConfig.cs" ||
        rel == "Scripts/Utils/CompatLayer.cs" ||
        rel.StartsWith("Scripts/BBS/");

    /// <summary>The code part of a line: everything before "//", so a commented-out check does not count.</summary>
    private static string CodeOnly(string line)
    {
        int c = line.IndexOf("//", StringComparison.Ordinal);
        return c >= 0 ? line.Substring(0, c) : line;
    }

    /// <summary>
    /// The hand-rolled yes/no shapes: GameConfig.IsAffirmative (bare; nothing else in the tree is
    /// called IsAffirmative), a StartsWith/=="Y"/'Y' read, and the localized "si"/"igen" chain pieces.
    /// GameConfig.IsNegative is qualified: the bare name collides with StatusEffect.IsNegative() and
    /// RelationshipManager.IsNegative(), unrelated debuff/relationship checks called the same way
    /// (s.IsNegative()) six times in CombatEngine alone. == "O" and == "I" are deliberately left out of
    /// the chain: on the current tree they are common multi-way menu letters unrelated to yes/no
    /// (Info/Insult in RareEncounters and StreetEncounterSystem, Open in SettlementLocation, DungeonLocation
    /// x2 and RareEncounters, Item in CombatEngine and DungeonLocation, Inn in MainStreetLocation), nine
    /// hits that are not a yes/no read; the one real chain (CombatEngine's spare/finish aliases) already
    /// carries a plain == "Y" on the same line, so nothing is lost by dropping the bare O/I forms.
    /// </summary>
    internal static readonly Regex HandRolledYesNo = new(
        @"IsAffirmative\(|GameConfig\.IsNegative\(|StartsWith\(\s*[""']Y[""']|==\s*""Y""|""Y""\s*==|==\s*'Y'|==\s*""SI""|==\s*""IGEN""",
        RegexOptions.Compiled);

    /// <summary>
    /// The hand-rolled yes/no sites in one file's source: (line number, text, exempt). A marked line
    /// counts as exempt whether or not its own code matches the shapes above (see ExemptSites); an
    /// unmarked line counts only when it matches.
    /// </summary>
    internal static List<(int line, string text, bool exempt)> FindHits(string source)
    {
        var hits = new List<(int, string, bool)>();
        var lines = source.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            bool marked = lines[i].Contains(ExemptMarker);
            if (marked)
                hits.Add((i + 1, lines[i].Trim(), true));
            else if (HandRolledYesNo.IsMatch(CodeOnly(lines[i])))
                hits.Add((i + 1, lines[i].Trim(), false));
        }
        return hits;
    }

    private static List<string> ScanScripts(out int exempt)
    {
        string root = FloorAndStun1113Tests.RepoRoot();
        var found = new List<string>();
        exempt = 0;
        foreach (var path in Directory.GetFiles(Path.Combine(root, "Scripts"), "*.cs", SearchOption.AllDirectories).OrderBy(p => p))
        {
            string rel = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (IsHelper(rel)) continue;
            foreach (var (line, text, isExempt) in FindHits(File.ReadAllText(path)))
            {
                if (isExempt) exempt++;
                else found.Add($"{rel}:{line}: {text}");
            }
        }
        return found;
    }

    [Fact]
    public void NoYesNoCheckIsHandRolled_OutsideTheSharedPrompt()
    {
        var found = ScanScripts(out int exempt);
        found.Should().BeEmpty(
            "a hand-rolled yes/no test is a re-ask waiting to not happen; use " +
            "TerminalEmulator.AskYesNoAsync/AskYesNoKeyAsync, or add // v1.1.15: yesno-exempt: <reason> " +
            "if the line is a real menu where Y is one option among several");
        exempt.Should().Be(ExemptSites, "a new exemption is reviewed here, not added with a marker alone");
    }

    [Fact]
    public void TheCheck_CatchesEveryShapeOfAHandRolledYesNo_AndNothingElse()
    {
        // canary: each of these is a hand-rolled yes/no test
        string bad = string.Join("\n", new[]
        {
            "        if (GameConfig.IsAffirmative(input)) return true;",
            "        if (GameConfig.IsNegative(input)) return false;",
            "        if (answer.StartsWith(\"Y\")) return true;",
            "        if (answer.StartsWith('Y')) return true;",
            "        if (choice == \"Y\") return true;",
            "        if (\"Y\" == choice) return true;",
            "        if (choice == 'Y') return true;",
            "        bool spared = choice == \"Y\" || choice == \"SI\" || choice == \"IGEN\";",
        });
        FindHits(bad).Should().HaveCount(8);
        FindHits(bad).Should().OnlyContain(h => !h.exempt);

        // each of these is not a plain yes/no read, is commented out, or is the shared prompt itself
        string good = string.Join("\n", new[]
        {
            "        await terminal.AskYesNoAsync(\"Sure? \");",
            "        await terminal.AskYesNoKeyAsync();",
            "        // if (GameConfig.IsAffirmative(input)) return true;",
            "        if (choice.ToUpper() == \"I\") ShowInfo();", // Info, not yes/no (RareEncounters, StreetEncounterSystem, CombatEngine, DungeonLocation, MainStreetLocation)
            "        if (choice == \"O\") OpenChest();", // Open, not yes/no (SettlementLocation, DungeonLocation x2, RareEncounters)
            "        var negated = status.IsNegative();", // StatusEffect.IsNegative(), not GameConfig's
            "        if (rel.IsNegative()) return;", // RelationshipManager.IsNegative(), not GameConfig's
        });
        FindHits(good).Should().BeEmpty();

        // a marker exempts the line, whether or not the line itself is a hit
        var exemptHit = FindHits("        if (GameConfig.IsAffirmative(input)) return true; // v1.1.15: yesno-exempt: reason");
        exemptHit.Should().ContainSingle().Which.exempt.Should().BeTrue();

        var exemptNoHit = FindHits("        // v1.1.15: yesno-exempt: explained on the lines below");
        exemptNoHit.Should().ContainSingle().Which.exempt.Should().BeTrue();
    }
}
