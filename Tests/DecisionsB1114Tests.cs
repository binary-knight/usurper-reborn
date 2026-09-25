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
}
