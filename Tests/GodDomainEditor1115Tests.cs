using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake.Editor;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.0 Temple gods piece 2 follow-up: the save editor's god domain prompt takes blank or one of
/// the ten domain names and asks again on anything else, which the load would silently drop.
/// </summary>
public class GodDomainEditor1115Tests
{
    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "UsurperReborn.sln"))) dir = Path.GetDirectoryName(dir);
        return dir!;
    }

    [Theory]
    [InlineData("Earth", true)]
    [InlineData("chaos", true)]
    [InlineData(" Nature ", true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("Eart", false)]
    [InlineData("Fire", false)]
    [InlineData("3", false)]
    [InlineData("None", false)]
    public void DomainInput_BlankOrOneOfTheTen(string input, bool valid) =>
        PlayerSaveEditor.IsValidDomainInput(input).Should().Be(valid);

    [Fact]
    public void DomainPrompt_AsksAgainOnAnUnknownDomain()
    {
        string src = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "Editor", "PlayerSaveEditor.cs"));
        src.Should().Contain("p.DivineDomain = PromptDivineDomain(p.DivineDomain);");
        Regex.IsMatch(src, @"while \(true\)\s*\{\s*string input = EditorIO\.PromptString\(.*, shown\);\s*if \(IsValidDomainInput\(input\)\) return input;\s*EditorIO\.Warn\(")
            .Should().BeTrue("an unknown domain is warned about and asked again");
    }
}
