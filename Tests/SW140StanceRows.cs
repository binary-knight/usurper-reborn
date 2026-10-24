using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.4.0 SW: the stance picker rows ("  [N] name  description") fit 79 columns in every language, in the dungeon party
/// menu and in the Inn. A description that is too long continues on rows indented under its start; a row that fits is
/// the one row it always was.
/// </summary>
[Collection("SharedGameSingletons")]
public class SW140StanceRows
{
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };
    private static readonly (string, TeammateStance)[] Stances =
    {
        ("1", TeammateStance.Aggressive), ("2", TeammateStance.Balanced), ("3", TeammateStance.Cautious),
    };

    public static IEnumerable<object[]> LangData() => Langs.Select(l => new object[] { l });

    private static string Strip(string s) => Regex.Replace(s, "\u001b\\[[0-9;?]*[A-Za-z]", "");

    private static List<string> Rows(string lang, string key, TeammateStance stance)
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new MemoryStream(), output);
        var prev = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            DungeonLocation.WriteStanceRow(term, key, stance);
        }
        finally { GameConfig.Language = prev; }
        term.StreamWriterInternal?.Flush();
        return Strip(Encoding.UTF8.GetString(output.ToArray())).Replace("\r", "").Split('\n').Where(l => l.Length > 0).ToList();
    }

    [Theory]
    [MemberData(nameof(LangData))]
    public void EveryStanceRow_Fits79_AndKeepsAllTheWords(string lang)
    {
        foreach (var (key, stance) in Stances)
        {
            var rows = Rows(lang, key, stance);
            foreach (var r in rows) r.Length.Should().BeLessThanOrEqualTo(79, "{0} {1}: '{2}'", lang, key, r);
            string name = Loc.GetIn(lang, TeammateStances.NameKey(stance));
            rows[0].Should().StartWith($"  [{key}] {name}  ");
            int lead = $"  [{key}] {name}  ".Length;
            foreach (var r in rows.Skip(1)) r.Substring(0, lead).Trim().Should().BeEmpty("a continuation sits under the description");
            string joined = string.Join(" ", rows.Select((r, i) => r.Substring(lead).Trim()));
            joined.Should().Be(Loc.GetIn(lang, TeammateStances.NameKey(stance) + ".desc"));
        }
    }

    [Fact]
    public void AShortRow_IsOneRowAsItWas()
    {
        var rows = Rows("en", "2", TeammateStance.Balanced);
        rows.Should().ContainSingle();
        rows[0].Should().Be($"  [2] {Loc.GetIn("en", "party.stance.balanced")}  {Loc.GetIn("en", "party.stance.balanced.desc")}");
    }

    [Fact]
    public void BothPickers_DrawTheirRowsThroughTheSharedWriter()
    {
        string root = AppContext.BaseDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "Scripts", "Locations", "InnLocation.cs"))) root = Path.GetDirectoryName(root)!;
        var dungeon = File.ReadAllText(Path.Combine(root!, "Scripts", "Locations", "DungeonLocation.cs"));
        var inn = File.ReadAllText(Path.Combine(root!, "Scripts", "Locations", "InnLocation.cs"));
        int p = dungeon.IndexOf("internal async Task<TeammateStance?> PromptStanceChoice", StringComparison.Ordinal);
        dungeon.Substring(p, 1200).Should().Contain("WriteStanceRow(terminal, key, stance);");
        int q = inn.IndexOf("private async Task SetCompanionTactics", StringComparison.Ordinal);
        inn.Substring(q, 3200).Should().Contain("DungeonLocation.WriteStanceRow(terminal, key, stance);");
    }
}
