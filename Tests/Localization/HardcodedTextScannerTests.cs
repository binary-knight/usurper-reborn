using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace UsurperReborn.Tests.Localization;

/// <summary>
/// Checks the hardcoded text scanner on a fixture with known hits and misses, and holds the command
/// that prints the full inventory:
///   Tests/Localization/loc-scan.sh [out-dir]
/// (runs this class's Inventory test with LOC_SCAN_OUT set), and with --write-baseline also rewrites
/// Tests/Localization/hardcoded-baseline.json for HardcodedTextRatchetTests.
/// </summary>
public class HardcodedTextScannerTests
{
    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test must run inside the repository");
        return dir!.FullName;
    }

    internal static List<HardcodedTextScanner.Exclusion> Exclusions() =>
        HardcodedTextScanner.ParseExclusions(File.ReadAllText(Path.Combine(RepoRoot(), "Tests/Localization/hardcoded-exclusions.txt")));

    [Fact]
    public void Fixture_FindsExactlyTheKnownSites()
    {
        string source = File.ReadAllText(Path.Combine(RepoRoot(), "Tests/Localization/Fixtures/HardcodedFixture.cs.txt"));
        var sites = HardcodedTextScanner.ScanSource("Fixture.cs", source, Exclusions());

        var got = sites.Select(s => $"{s.Line} {s.Sink} {s.Literal}").ToList();
        var expected = new List<string>
        {
            "12 terminal.WriteLine \"You enter the fixture room.\"",
            "13 terminal.WriteLine $\"Your HP: {hp}/{player.MaxHP}\"",
            "14 terminal.WriteLine \"Gold: \"",
            "14 terminal.WriteLine \" pieces\"",
            "17 terminal.WriteLine \"It worked.\"",
            "18 terminal.WriteLine \"It failed.\"",
            "20 terminal.Write @\"Verbatim words here\"",
            "21 terminal.WriteLine \"Victory\"",
            "37 UIHelper.DrawBoxLine \"Boxed words\"",
            "38 UIHelper.DrawMenuOption \"Attack the foe\"",
            "39 Newsy \"Fixture headline for the town\"",
            "40 NotifyGroup \"The group hears words\"",
            "41 (helper) WriteMenuRow \" Rings\"",
            "41 (helper) WriteMenuRow \"nchant Equipment\"",
            "42 terminal.GetInput \"Choose wisely: \"",
            "43 terminal.WriteLine \"Formatted {0} words\"",
            "44 terminal.WriteLine \"  Name\"",
            "45 terminal.WriteLine \"[AUTO]\"",
            "51 return (bool, string) \"You cannot do that.\"",
        };
        got.Should().Equal(expected);
    }

    [Theory]
    [InlineData("Attack", true)]
    [InlineData("HP", true)]
    [InlineData("[A]", false)]
    [InlineData("[bright_red][/]", false)]
    [InlineData("[bright_red]Burn[/]", true)]
    [InlineData("\x1b[1;31m\x1b[0m", false)]
    [InlineData("`2`4", false)]
    [InlineData("{0} {1:N0}", false)]
    [InlineData("═══ 12 / 34 ═══", false)]
    [InlineData("combat.menu.attack", false)]
    [InlineData("Combat over.", true)]
    public void WordRule(string text, bool expected)
    {
        HardcodedTextScanner.IsWordy(text).Should().Be(expected);
    }

    [Fact]
    public void ExclusionsFile_EveryEntryHasAReasonAndTheFilesExist()
    {
        var list = Exclusions();
        list.Should().NotBeEmpty();
        foreach (var e in list.Where(e => e.Kind != "class"))
        {
            string path = Path.Combine(RepoRoot(), e.Target);
            (e.Kind == "file" ? File.Exists(path) : Directory.Exists(path)).Should().BeTrue($"the exclusions file names {e.Target}");
        }
    }

    /// <summary>
    /// The command. Does nothing unless LOC_SCAN_OUT names a folder; then writes inventory.md and
    /// rollup.md there. With LOC_SCAN_WRITE_BASELINE=1 it also rewrites the ratchet baseline.
    /// </summary>
    [Fact]
    public void Inventory()
    {
        string? outDir = Environment.GetEnvironmentVariable("LOC_SCAN_OUT");
        if (string.IsNullOrEmpty(outDir)) return;
        string root = RepoRoot();
        int files = Directory.EnumerateFiles(Path.Combine(root, "Scripts"), "*.cs", SearchOption.AllDirectories).Count();
        var sites = HardcodedTextScanner.ScanRepo(root, Exclusions());
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "inventory.md"), HardcodedTextScanner.InventoryMarkdown(sites, files));
        File.WriteAllText(Path.Combine(outDir, "rollup.md"), HardcodedTextScanner.RollupMarkdown(sites));
        if (Environment.GetEnvironmentVariable("LOC_SCAN_WRITE_BASELINE") == "1")
            File.WriteAllText(Path.Combine(root, HardcodedTextRatchetTests.BaselinePath),
                HardcodedTextRatchetTests.SerializeBaseline(HardcodedTextRatchetTests.BuildBaseline(sites)));
    }
}
