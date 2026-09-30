using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace UsurperReborn.Tests.Localization;

/// <summary>
/// Checks DataTextScanner (data tables, Electron payloads, shown throws) on a fixture, checks the
/// sources file, and holds the command that writes the data inventory:
///   Tests/Localization/loc-scan.sh [--write-data-baseline] [out-dir]
/// writes inventory-data.md and rollup-data.md next to the output call inventory, and with the flag
/// rewrites Tests/Localization/hardcoded-data-baseline.json for DataTextRatchetTests.
/// </summary>
public class DataTextScannerTests
{
    internal const string SourcesPath = "Tests/Localization/hardcoded-data-sources.txt";

    internal static List<DataTextScanner.Source> Sources() =>
        DataTextScanner.ParseSources(File.ReadAllText(Path.Combine(HardcodedTextScannerTests.RepoRoot(), SourcesPath)));

    private const string FixtureSources = @"
table|Fixture.cs|decl:Taunts|shown as combat taunts
names|Fixture.cs|decl:MaleNames|proper nouns
table|Fixture.cs|init.Text|dialogue line text
table|Fixture.cs|init.[k]=|replies shown in a menu
nouns|Fixture.cs|decl:Abilities/tuple#0|ability names, common nouns
table|Fixture.cs|decl:Abilities/tuple#1|ability descriptions
keyed|Fixture.cs|init.Desc|discovery.{id}.desc
keyed|Fixture.cs|init.Name|discovery.{id}.name
";

    private static List<DataTextScanner.Site> FixtureSites(out List<DataTextScanner.Skipped> skipped)
    {
        string root = HardcodedTextScannerTests.RepoRoot();
        string source = File.ReadAllText(Path.Combine(root, "Tests/Localization/Fixtures/DataFixture.cs.txt"));
        var r = DataTextScanner.ScanSource("Fixture.cs", source, HardcodedTextScannerTests.Exclusions(), DataTextScanner.ParseSources(FixtureSources));
        skipped = r.Skipped;
        return r.Sites;
    }

    [Fact]
    public void Fixture_FindsExactlyTheKnownSites()
    {
        var got = FixtureSites(out _).Select(s => $"{s.Line} {s.Category} {s.Slot} {s.Literal}").ToList();
        got.Should().Equal(new List<string>
        {
            "7 data decl:Taunts \"You call that a swing?\"",
            "7 data decl:Taunts \"My grandmother hits harder.\"",
            "13 data init.Text \"There you are, {player_name}.\"",
            "14 data init.Text $\"Back again? {count} times now.\"",
            "19 data init.[k]= \"Well met, traveller.\"",
            "23 data decl:Abilities/tuple#0 \"Shield Wall\"",
            "23 data decl:Abilities/tuple#1 \"Raise your shield against the blow.\"",
            "23 data decl:Abilities/tuple#0 \"Cleave\"",
            "23 data decl:Abilities/tuple#1 \"Strike every foe.\"",
            "34 electron ElectronBridge.EmitNarration \"The mist rolls in.\"",
            "35 electron ElectronBridge.Emit \"Choose your move\"",
            "37 electron new MenuItemData \"Attack the foe\"",
            "40 throw throw new InvalidOperationException \"The gate will not open for you.\"",
        });
    }

    [Fact]
    public void Fixture_DataArrayShownToPlayers_Counts()
    {
        FixtureSites(out _).Should().Contain(s => s.Category == DataTextScanner.Data && s.Literal == "\"You call that a swing?\"");
    }

    [Fact]
    public void Fixture_ProperNounNameList_DoesNotCount()
    {
        var sites = FixtureSites(out var skipped);
        sites.Should().NotContain(s => s.Literal.Contains("Aldric"));
        skipped.Should().Contain(new DataTextScanner.Skipped("Fixture.cs", "names", "decl:MaleNames", 3));
    }

    [Fact]
    public void Fixture_KeyedUnlistedAndIdShaped_DoNotCount()
    {
        var sites = FixtureSites(out var skipped);
        sites.Should().NotContain(s => s.Literal.Contains("Weeping") || s.Literal.Contains("Nobody reads")
                                       || s.Literal.Contains("grove_shrine") || s.Literal.Contains("SAFE_TRAVELS"));
        skipped.Where(s => s.Kind == "keyed").Sum(s => s.Count).Should().Be(2);
    }

    [Fact]
    public void Fixture_InternalThrow_DoesNotCount_PlayerShownThrow_Does()
    {
        var throws = FixtureSites(out _).Where(s => s.Category == DataTextScanner.Throw).Select(s => s.Literal).ToList();
        throws.Should().Equal("\"The gate will not open for you.\"");
    }

    [Fact]
    public void Fixture_ElectronIdsSoundsAndLocKeys_DoNotCount()
    {
        var electron = FixtureSites(out _).Where(s => s.Category == DataTextScanner.Electron).Select(s => s.Literal).ToList();
        electron.Should().NotContain(new[] { "\"combat_menu\"", "\"mystery\"", "\"attack\"", "\"sfx.door_open\"", "\"A\"", "\"sword\"", "\"combat.menu\"" });
    }

    [Theory]
    [InlineData("grove_shrine", true)]
    [InlineData("SAFE_TRAVELS", true)]
    [InlineData("observer", true)]
    [InlineData("HP", true)]
    [InlineData("Victory", false)]
    [InlineData("Well met.", false)]
    [InlineData("two words", false)]
    public void IdentifierShape(string text, bool expected)
    {
        DataTextScanner.IsIdentifierShaped(text).Should().Be(expected);
    }

    [Fact]
    public void SourcesParser_RejectsANameSlotListedAsTable()
    {
        var act = () => DataTextScanner.ParseSources("table|Fixture.cs|decl:MaleNames|shown");
        act.Should().Throw<FormatException>().WithMessage("*names*nouns*");
        DataTextScanner.ParseSources("nouns|Fixture.cs|init.Name|ability names").Should().HaveCount(1);
    }

    [Fact]
    public void SourcesFile_EveryEntryHasAReason_TheFileExists_AndTheSlotStillHoldsText()
    {
        string root = HardcodedTextScannerTests.RepoRoot();
        var sources = Sources();
        sources.Should().NotBeEmpty();
        sources.GroupBy(s => (s.File, s.Slot)).Where(g => g.Count() > 1).Should().BeEmpty("each file and slot is listed once");
        var result = DataTextScanner.ScanRepo(root, HardcodedTextScannerTests.Exclusions(), sources);
        var seen = new HashSet<(string, string)>(result.Sites.Where(s => s.Category == DataTextScanner.Data).Select(s => (s.File, s.Slot))
            .Concat(result.Skipped.Select(s => (s.File, s.Slot))));
        sources.Where(s => !File.Exists(Path.Combine(root, s.File))).Select(s => s.File).Should().BeEmpty("the sources file names only existing files");
        sources.Where(s => !seen.Contains((s.File, s.Slot))).Select(s => $"{s.File} {s.Slot}").Should()
            .BeEmpty("a listed slot that holds no counted text any more should be removed from the sources file");
    }

    [Fact]
    public void ElectronPayloadTypes_MatchTheClassesInElectronBridge()
    {
        string text = File.ReadAllText(Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Scripts/UI/ElectronBridge.cs"));
        var declared = Regex.Matches(text, @"public class (\w+)").Select(m => m.Groups[1].Value).ToHashSet();
        declared.Should().BeEquivalentTo(DataTextScanner.ElectronPayloadTypes);
    }

    /// <summary>The command half: see the class summary. Does nothing unless LOC_SCAN_OUT is set.</summary>
    [Fact]
    public void Inventory()
    {
        string? outDir = Environment.GetEnvironmentVariable("LOC_SCAN_OUT");
        if (string.IsNullOrEmpty(outDir)) return;
        string root = HardcodedTextScannerTests.RepoRoot();
        int files = Directory.EnumerateFiles(Path.Combine(root, "Scripts"), "*.cs", SearchOption.AllDirectories).Count();
        var sources = Sources();
        var result = DataTextScanner.ScanRepo(root, HardcodedTextScannerTests.Exclusions(), sources);
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "inventory-data.md"), DataTextScanner.InventoryMarkdown(result.Sites, files));
        File.WriteAllText(Path.Combine(outDir, "rollup-data.md"), DataTextScanner.RollupMarkdown(result.Sites, result.Skipped, sources));
        if (Environment.GetEnvironmentVariable("LOC_SCAN_WRITE_DATA_BASELINE") == "1")
            File.WriteAllText(Path.Combine(root, DataTextRatchetTests.BaselinePath),
                DataTextRatchetTests.SerializeBaseline(DataTextRatchetTests.BuildBaseline(result.Sites)));
    }
}
