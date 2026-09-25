using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.14 maintainer decisions, balance and package rows: the SSH.NET upgrade, the alt slot
/// level, Engulf on a held target, the goblin critical strike and area freeze.
/// </summary>
[Collection("SharedGameSingletons")]
public class DecisionsB1114Tests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test must run inside the repository");
        return dir!.FullName;
    }

    // ---- O4: SSH.NET ----

    [Fact]
    public void SshNet_IsPinnedToThePatchedRelease()
    {
        // v1.1.14: 2025.1.0 and earlier carry GHSA-q939-rpr3-3284 and GHSA-mggc-4xg6-vcxf (NU1903); 2026.0.0 fixes both.
        string csproj = File.ReadAllText(Path.Combine(RepoRoot(), "usurper-reloaded.csproj"));
        var m = Regex.Match(csproj, "<PackageReference Include=\"SSH.NET\" Version=\"([^\"]+)\"");
        m.Success.Should().BeTrue("the game references SSH.NET for the online client");
        m.Groups[1].Value.Should().Be("2026.0.0");

        var loaded = typeof(Renci.SshNet.SshClient).Assembly.GetName().Version!;
        loaded.Major.Should().BeGreaterOrEqualTo(2026, "the build must bind the patched assembly");
    }

    // ---- B4: alt slot at level 25 ----

    [Theory]
    [InlineData(false, false, 1, false)]
    [InlineData(false, false, 24, false)]   // one level short
    [InlineData(false, false, 25, true)]    // the threshold
    [InlineData(false, false, 60, true)]
    [InlineData(true, false, 1, true)]      // an immortal main still opens it
    [InlineData(false, true, 3, true)]      // an earned slot survives renouncing and a low level
    public void AltSlot_OpensAtLevel25_OrForAnImmortal(bool immortal, bool earned, int level, bool expected)
    {
        GameConfig.AltSlotUnlockLevel.Should().Be(25);
        GameEngine.AltSlotUnlocked(immortal, earned, level).Should().Be(expected);
    }

    [Fact]
    public void AltSlot_MenuUsesTheRule_AndKeepsOneAlt()
    {
        string src = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "Core", "GameEngine.cs"));
        src.Should().Contain("bool canCreateAlt = AltSlotUnlocked(mainIsImmortal, hasAltSlot, mainLevel) && altSave == null",
            "the menu gate uses the level rule and still refuses a second alt");
        src.Should().NotContain("engine.immortal_required");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public void AltSlot_RefusalText_NamesTheLevel(string lang)
    {
        string json = File.ReadAllText(Path.Combine(RepoRoot(), "Localization", lang + ".json"));
        var m = Regex.Match(json, "\"engine\\.alt_level_required\": \"([^\"]*)\"");
        m.Success.Should().BeTrue($"{lang} has the new refusal line");
        m.Groups[1].Value.Should().Contain("{0}", "the level is filled in from GameConfig");
        json.Should().NotContain("\"engine.immortal_required\"");
    }
}
