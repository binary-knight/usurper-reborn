using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.7: the wiki guides spell the stat "defense" (and "Defense" at a sentence start or in a title), as the
/// game's own English text does. The only kept "Defence" in a guide is a code identifier inside a balance
/// token, listed below with its file.
/// </summary>
public class SP137Spelling
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test must run inside the repository");
        return dir!.FullName;
    }

    /// <summary>Kept text: file (relative to DOCS/wiki, forward slashes) and the exact text that may say Defence.</summary>
    private static readonly (string File, string Text)[] Kept =
    {
        ("gods/boons.md", "{{balance:GodBoonJudicarDefencePct}}"),
        ("monsters/index.md", "{{balance:EliteDefenceMultiplier}}"),
    };

    [Fact]
    public void Guides_say_defense_not_defence()
    {
        var wiki = Path.Combine(RepoRoot(), "DOCS", "wiki");
        var files = Directory.GetFiles(wiki, "*.md", SearchOption.AllDirectories);
        files.Should().NotBeEmpty();
        var bad = new List<string>();
        foreach (var f in files)
        {
            var rel = Path.GetRelativePath(wiki, f).Replace('\\', '/');
            var lines = File.ReadAllText(f, Encoding.UTF8).Replace("\r\n", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                foreach (var k in Kept.Where(k => k.File == rel))
                    line = line.Replace(k.Text, "");
                if (line.IndexOf("defence", StringComparison.OrdinalIgnoreCase) >= 0)
                    bad.Add($"{rel}:{i + 1}");
            }
        }
        bad.Should().BeEmpty("the guides spell it defense: " + string.Join(", ", bad.Take(20)));
    }

    [Fact]
    public void Kept_texts_are_still_in_their_guides()
    {
        var wiki = Path.Combine(RepoRoot(), "DOCS", "wiki");
        foreach (var k in Kept)
            File.ReadAllText(Path.Combine(wiki, k.File), Encoding.UTF8).Should().Contain(k.Text);
    }

    /// <summary>Kept text in the built pages (relative to web/wiki): a pattern that may say Defence, and why.
    /// items/armor: the gear set bonus rows are the game's own en.json item.set.bonus.defence ("+{0} Defence").
    /// reference/balance: the name of the C# constant GodBoonJudicarDefencePct.</summary>
    private static readonly (string File, string Pattern)[] KeptBuilt =
    {
        ("en/items/armor/index.html", @"\+\d+ Defence"),
        ("en/reference/balance/index.html", @"God Boon Judicar Defence Pct"),
    };

    [Fact]
    public void Built_pages_say_defense_not_defence()
    {
        // web/wiki is made by the wiki build (npm run build --prefix tools/wiki-build); without it there is nothing to read.
        var built = Path.Combine(RepoRoot(), "web", "wiki");
        if (!Directory.Exists(built)) return;
        var files = Directory.GetFiles(built, "*.html", SearchOption.AllDirectories);
        files.Should().NotBeEmpty();
        var bad = new List<string>();
        foreach (var f in files)
        {
            var rel = Path.GetRelativePath(built, f).Replace('\\', '/');
            var html = File.ReadAllText(f, Encoding.UTF8);
            html = Regex.Replace(html, @"<(script|style)[\s\S]*?</\1>", " ", RegexOptions.IgnoreCase);
            var text = Regex.Replace(html, @"<[^>]*>", " ");
            foreach (var k in KeptBuilt.Where(k => k.File == rel))
                text = Regex.Replace(text, k.Pattern, "");
            if (text.IndexOf("defence", StringComparison.OrdinalIgnoreCase) >= 0)
                bad.Add(rel);
        }
        bad.Should().BeEmpty("the built pages spell it defense: " + string.Join(", ", bad.Take(20)));
    }
}
