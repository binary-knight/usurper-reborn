using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.11 K3: the Arena, throne room, Dormitory and Love Street menus lay their rows out with MenuGridRows, so no label
/// touches the next key and no row passes 79 columns in any language, and the French higher or lower prompt reads whole.
/// </summary>
[Collection("SharedGameSingletons")]
public class MenuRows1311Tests
{
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };

    private static T Lang<T>(string lang, Func<T> f)
    {
        string prev = GameConfig.Language;
        GameConfig.Language = lang;
        try { return f(); }
        finally { GameConfig.Language = prev; }
    }

    private static List<string> Render(List<List<(string Key, string Label)>> rows) =>
        HealerLocation.MenuGridRows(rows, 79)
            .Select(r => string.Concat(r.Select(c => " [" + c.Key + "] " + c.Label + new string(' ', c.Pad))).TrimEnd()).ToList();

    private static IEnumerable<(string Name, Func<List<string>> Rows)> Menus()
    {
        yield return ("arena", () => Render(ArenaLocation.MenuRows()));
        yield return ("throne room", () => Render(CastleLocation.RoyalMenuRows()));
        yield return ("dormitory online", () => Render(DormitoryLocation.MenuRows(true)));
        yield return ("dormitory single", () => Render(DormitoryLocation.MenuRows(false)));
        int g = 0;
        foreach (var _ in new[] { 0, 1, 2 })
        {
            int idx = g++;
            yield return ("love street group " + idx, () => Render(LoveStreetLocation.MenuGroups()[idx]));
        }
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void EveryMenuRow_HasAGapBeforeEachKey_AndFits79(string lang)
    {
        foreach (var (name, rows) in Menus())
        {
            var lines = Lang(lang, rows);
            lines.Should().NotBeEmpty(name);
            foreach (var line in lines)
            {
                line.Length.Should().BeLessOrEqualTo(79, "{0} {1} row \"{2}\"", lang, name, line);
                Regex.IsMatch(line, @"\S\[[A-Z0-9]\]").Should().BeFalse("{0} {1} row \"{2}\" runs a label into the next key", lang, name, line);
                Regex.IsMatch(line, @"\] \S").Should().BeTrue("{0} {1} row \"{2}\" keeps a space after each key", lang, name, line);
            }
        }
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void EveryMenuLabel_IsAWholeWord_NotALabelMissingItsFirstLetter(string lang)
    {
        foreach (var (name, rows) in Menus())
            foreach (var line in Lang(lang, rows))
                foreach (Match m in Regex.Matches(line, @"\[[A-Z0-9]\] (\S+)"))
                    char.IsLower(m.Groups[1].Value[0]).Should().BeFalse("{0} {1}: \"{2}\" starts in lower case, a letter went missing", lang, name, m.Groups[1].Value);
    }

    [Fact]
    public void TheFrenchHigherOrLowerPrompt_ReadsWhole()
    {
        string line = Loc.GetIn("fr", "inn.hl_will_next_be") + "[H]" + Loc.GetIn("fr", "inn.hl_igher_or") + "[L]" + Loc.GetIn("fr", "inn.hl_ower");
        line.Should().Be("Le prochain lancer sera [H] plus haut ou [L] plus bas? ");
    }

    [Fact]
    public void TheMenusAreDrawnByWriteMenuCells()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !(Directory.Exists(Path.Combine(root.FullName, "Scripts")) && Directory.Exists(Path.Combine(root.FullName, "Localization"))))
            root = root.Parent;
        root.Should().NotBeNull();
        string dir = Path.Combine(root!.FullName, "Scripts", "Locations");
        foreach (var (file, call) in new[] { ("ArenaLocation.cs", "WriteMenuCells(MenuRows())"), ("CastleLocation.cs", "WriteMenuCells(RoyalMenuRows()"),
                     ("DormitoryLocation.cs", "WriteMenuCells(MenuRows(DoorMode.IsOnlineMode))"), ("LoveStreetLocation.cs", "WriteMenuCells(group)") })
            File.ReadAllText(Path.Combine(dir, file)).Should().Contain(call, file);
    }
}
