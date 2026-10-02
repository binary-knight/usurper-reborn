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
}
