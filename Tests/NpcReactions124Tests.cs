using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Data;
using UsurperRemake.Editor;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.4: the editor's Export Defaults prompt counts the files the export writes, and an NPC ally's
/// victory reaction in group combat reaches each member in their own language.
/// </summary>
[Collection("SharedGameSingletons")]
public class NpcReactions124Tests
{
    [Fact]
    public void ExportDefaultsPrompt_CountsTheFilesTheExportWrites()
    {
        var m = Regex.Match(EditorMain.ExportPrompt, @"Write all (\d+) default JSON files");
        m.Success.Should().BeTrue(EditorMain.ExportPrompt);
        int promised = int.Parse(m.Groups[1].Value);

        var dir = Path.Combine(Path.GetTempPath(), "npcr124-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            GameDataLoader.ExportDefaults(dir);
            var written = Directory.GetFiles(dir, "*.json");
            written.Should().HaveCount(GameDataLoader.DefaultExports.Count);
            promised.Should().Be(written.Length, "the prompt names the number of files written");
            foreach (var f in written) new FileInfo(f).Length.Should().BeGreaterThan(2, Path.GetFileName(f));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
