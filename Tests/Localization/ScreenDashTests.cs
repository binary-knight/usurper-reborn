using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace UsurperReborn.Tests.Localization;

/// <summary>
/// No string literal that reaches a screen sink (terminal, news, mail, broadcast, menu helper, see
/// HardcodedTextScanner) may hold an em-dash (U+2014), en-dash (U+2013) or ellipsis (U+2026). Write
/// "--" and "..." instead. Allowed count: zero. Comments and DebugLogger / Console strings are not sinks.
/// </summary>
public class ScreenDashTests
{
    [Fact]
    public void ScreenStrings_HoldNoEmDashEnDashOrEllipsis()
    {
        string root = HardcodedTextScannerTests.RepoRoot();
        var sites = HardcodedTextScanner.ScanRepo(root, HardcodedTextScannerTests.Exclusions(), dashes: true);
        string list = string.Join("\n", sites.Select(s => $"{s.File}:{s.Line} {s.Sink} {s.Literal}"));
        Assert.True(sites.Count == 0, "screen strings must use \"--\" and \"...\" instead of U+2014, U+2013 and U+2026:\n" + list);
    }

    [Fact]
    public void Scanner_FlagsADashInAScreenStringOnly()
    {
        string src = "class C { void M(){ terminal.WriteLine(\"a \\u2014 b\"); terminal.WriteLine(\"a \\u2026\"); DebugLogger.Instance.LogInfo(\"X\", \"a \\u2013 b\"); terminal.WriteLine(\"a -- b\"); } }";
        var sites = HardcodedTextScanner.ScanSource("T.cs", src, new List<HardcodedTextScanner.Exclusion>(), dashes: true);
        sites.Should().HaveCount(2);
    }

    [Fact]
    public void Scanner_FlagsACombatLogAddDash_InDashModeOnly()
    {
        string src = "class C { void M(){ result.CombatLog.Add(\"a \\u2014 b\"); list.Add(\"a \\u2014 b\"); result.CombatLog.Add(\"Player wins the fight\"); } }";
        var none = new List<HardcodedTextScanner.Exclusion>();
        var dashes = HardcodedTextScanner.ScanSource("T.cs", src, none, dashes: true);
        dashes.Should().ContainSingle().Which.Sink.Should().Be("CombatLog.Add");
        HardcodedTextScanner.ScanSource("T.cs", src, none).Should().BeEmpty("CombatLog.Add is not counted as hardcoded text");
    }

    /// <summary>CombatLog entries are shown as "- entry" in the combat test summary (MainStreetLocation).</summary>
    [Fact]
    public void CombatLogDashFixLines_Fit79Columns()
    {
        string src = File.ReadAllText(Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Scripts/Systems/CombatEngine.cs"));
        var lines = new[]
        {
            "Manwe uses Split Form -- Shadow of Manwe appears",
            "Creation's End -- instant kill (no Worldstone)",
            "Player accepts The Offer -- Manwe spared",
            "Player refuses The Offer -- combat continues",
        };
        foreach (var l in lines)
        {
            src.Should().Contain($"CombatLog.Add(\"{l}\")");
            ("- " + l).Length.Should().BeLessThanOrEqualTo(79, l);
        }
    }
}
