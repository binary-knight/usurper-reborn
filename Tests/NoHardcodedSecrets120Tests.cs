using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.0: the sprite generator reads its PixelLab key only from the PIXELLAB_API_KEY environment
/// variable. A key was hard-coded in electron-client/generate-sprites.js; no key literal may return.
/// </summary>
public class NoHardcodedSecrets120Tests
{
    [Fact]
    public void SpriteGenerator_ReadsTheKeyFromTheEnvironmentOnly()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "electron-client", "generate-sprites.js")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        string src = File.ReadAllText(Path.Combine(dir!.FullName, "electron-client", "generate-sprites.js"));

        src.Should().Contain("const API_KEY = process.env.PIXELLAB_API_KEY;");
        Regex.IsMatch(src, @"PIXELLAB_API_KEY\s*\|\|").Should().BeFalse("no fallback key after the environment variable");
        Regex.IsMatch(src, @"['""][0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}['""]")
            .Should().BeFalse("no key-shaped literal in the script");
    }
}
