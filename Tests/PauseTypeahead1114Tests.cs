using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.14: every pause goes through the shared pause (TerminalEmulator.PressAnyKey), which asks for Enter
/// in the line-based modes, for any key where a single key works, and keeps a line typed at it for the
/// next prompt. A read whose result is thrown away is a pause written by hand, which drops that line.
/// </summary>
[Collection("SharedGameSingletons")]
public class PauseTypeahead1114Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>The terminal reads whose result a caller can throw away.</summary>
    internal const string ReadMethods = "GetInput|GetInputAsync|ReadLineAsync|GetKeyInput|ReadKeyAsync|GetStringInput|GetCharAsync";

    /// <summary>
    /// A statement that awaits a terminal read and discards the result: the await starts the statement (at
    /// the start of the line, or after ';', '{', '}', an if's ')', 'else' or a case label), optionally as "_ = await".
    /// "x = await ...", "(await ...)", "return await ..." and "f(a, await ...)" use the input and do not match.
    /// </summary>
    internal static readonly Regex DiscardedRead = new(
        @"(?:^|[;{}]|\)|\belse\b|\bcase\s+[^:;]+:|\bdefault\s*:)\s*(?:_\s*=\s*)?await\s+[A-Za-z_][\w.!?]*\.(?:" + ReadMethods + @")\s*\(",
        RegexOptions.Compiled);

    /// <summary>A line carrying this marker is a reviewed exception (see ExemptSites).</summary>
    internal const string ExemptMarker = "v1.1.14: pause-exempt:";

    /// <summary>The reviewed exceptions: the Electron-only branches, where the overlay already got EmitPressAnyKey.</summary>
    internal const int ExemptSites = 2;

    /// <summary>The shared pause and the terminal plumbing it is built on; these read and discard by design.</summary>
    private static bool IsHelper(string rel) =>
        rel == "Scripts/UI/TerminalEmulator.cs" || rel == "Scripts/Utils/CompatLayer.cs" || rel.StartsWith("Scripts/BBS/");

    /// <summary>The code part of a line: everything before "//", so a commented-out read does not count.</summary>
    private static string CodeOnly(string line)
    {
        int c = line.IndexOf("//", StringComparison.Ordinal);
        return c >= 0 ? line.Substring(0, c) : line;
    }

    /// <summary>The discarded-input pauses in one file's source: (line number, text, exempt).</summary>
    internal static List<(int line, string text, bool exempt)> FindHandWrittenPauses(string source)
    {
        var hits = new List<(int, string, bool)>();
        var lines = source.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
            if (DiscardedRead.IsMatch(CodeOnly(lines[i])))
                hits.Add((i + 1, lines[i].Trim(), lines[i].Contains(ExemptMarker)));
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
            foreach (var (line, text, isExempt) in FindHandWrittenPauses(File.ReadAllText(path)))
            {
                if (isExempt) exempt++;
                else found.Add($"{rel}:{line}: {text}");
            }
        }
        return found;
    }

    [Fact]
    public void NoPauseIsWrittenByHand_OutsideTheSharedPause()
    {
        var found = ScanScripts(out int exempt);
        found.Should().BeEmpty("a read whose input is thrown away is a pause; use terminal.PressAnyKey(), which keeps a typed line for the next prompt");
        exempt.Should().Be(ExemptSites, "a new exception is reviewed here, not added with a marker alone");
    }

    [Fact]
    public void TheCheck_CatchesEveryShapeOfAHandWrittenPause_AndNothingElse()
    {
        // canary: each of these is a pause written by hand
        string bad = string.Join("\n", new[]
        {
            "        await terminal.GetInputAsync(Loc.Get(\"ui.press_enter\"));",
            "        await terminal.GetInput($\"  {Loc.Get(\"ui.press_enter\")}\");",
            "        await terminal.ReadKeyAsync();",
            "        await term.GetKeyInput();",
            "        await terminal.GetCharAsync();",
            "        await terminal.ReadLineAsync();",
            "        _ = await terminal.GetInput(\"\");",
            "        if (x != \"YES\") { terminal.WriteLine(\"Cancelled.\"); await terminal.GetInputAsync(\" Press Enter...\"); return; }",
            "        if (done) await terminal.GetStringInput(\"\");",
            "        else await TerminalEmulator.Instance!.GetInput(\"\");",
            "            case \"X\": await terminal.GetInput(\"\"); break;",
            "            default: await terminal.ReadKeyAsync(); break;",
        });
        FindHandWrittenPauses(bad).Should().HaveCount(12);

        // each of these uses the input, is commented out, or is the shared pause
        string good = string.Join("\n", new[]
        {
            "        string choice = await terminal.GetInput(\"Your choice: \");",
            "        var key = (await terminal.GetKeyInput()).ToUpperInvariant();",
            "        if ((await terminal.GetInputAsync(\"Y/N \")).Trim() == \"Y\") return;",
            "        return await terminal.GetInput(prompt);",
            "        Handle(player, await terminal.GetInput(\"> \"));",
            "        // await terminal.GetInputAsync(Loc.Get(\"ui.press_enter\"));",
            "        await terminal.PressAnyKey();",
            "        string pick = ok ? await terminal.GetInput(\"a\") : \"\";",
            "        await terminal.PressAnyKey(Loc.Get(\"pantheon.press_enter_return\"));",
        });
        FindHandWrittenPauses(good).Should().BeEmpty();

        var exempt = FindHandWrittenPauses("        await terminal.GetInput(\"\"); // " + ExemptMarker + " reason");
        exempt.Should().ContainSingle().Which.exempt.Should().BeTrue();
    }

    // ---- behaviour: a line typed at a converted screen's pause is the next prompt's answer ----

    private static (TerminalEmulator term, MemoryStream output) Stream(params string[] lines)
    {
        var output = new MemoryStream();
        return (new TerminalEmulator(new LineStream(lines), output), output);
    }

    private static string Plain(TerminalEmulator term, MemoryStream output)
    {
        term.StreamWriterInternal?.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");
    }

    private static async Task TypedLineRunsNext(TerminalEmulator term, MemoryStream output)
    {
        term.HasPendingLine.Should().BeTrue("the line typed at the pause is kept");
        (await term.GetInput("> ")).Should().Be("look", "the line typed at the pause is the next prompt's answer");
        (await term.GetInput("> ")).Should().Be("x");
        string shown = Plain(term, output);
        shown.Should().Contain(Loc.Get("ui.press_enter"), "the online pause asks for Enter");
        shown.Should().NotContain(Loc.Get("ui.press_any_key"));
        Regex.Matches(shown, Regex.Escape(Loc.Get("ui.press_enter"))).Count.Should().Be(1, "the screen no longer prints its own pause line as well");
    }

    [Fact]
    public async Task WeaponShopPause_KeepsATypedLine()
    {
        // was: Write(ui.press_enter) then GetInput("")
        var (term, output) = Stream("look", "x");
        var shop = new WeaponShopLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(shop, term);
        await (Task)typeof(WeaponShopLocation).GetMethod("Pause", F)!.Invoke(shop, null)!;
        await TypedLineRunsNext(term, output);
    }

    [Fact]
    public async Task PrisonWalkStatus_KeepsATypedLine()
    {
        // was: WriteAsync(ui.press_enter) then GetCharAsync()
        var (term, output) = Stream("look", "x");
        var walk = (PrisonWalkLocation)RuntimeHelpers.GetUninitializedObject(typeof(PrisonWalkLocation));
        typeof(PrisonWalkLocation).GetField("terminal", F)!.SetValue(walk, term);
        var hero = new Character { Name1 = "pw", Name2 = "Pw", Class = CharacterClass.Warrior, Level = 5, HP = 50, MaxHP = 50 };
        await (Task)typeof(PrisonWalkLocation).GetMethod("ShowCharacterStatus", F)!.Invoke(walk, new object[] { hero })!;
        await TypedLineRunsNext(term, output);
    }

    [Fact]
    public async Task SysOpConsoleDenied_KeepsATypedLine()
    {
        // was: GetInputAsync(Loc.Get("ui.press_enter"))
        UsurperRemake.BBS.DoorMode.IsInDoorMode.Should().BeFalse("the test takes the access-denied path");
        var (term, output) = Stream("look", "x");
        await new SysOpConsoleManager(term).Run();
        await TypedLineRunsNext(term, output);
    }
}
