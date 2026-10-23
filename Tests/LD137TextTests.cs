using System;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.7 LD: the Wisdom line on the character creation stat screen. Magic resistance comes from equipment only
/// (1.3.6 E1); Wisdom feeds the mana pool, spell mana cost and healing. The text must not claim magic resistance
/// in any of the five languages, and the rendered row must fit 79 columns.
/// </summary>
public class LD137Text
{
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };
    // the words each language uses for resistance
    private static readonly string[] Banned = { "resist", "résist", "ellenállás" };

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj"))) dir = dir.Parent;
        dir.Should().NotBeNull();
        return dir!.FullName;
    }

    private static string Get(string lang, string key)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), "Localization", lang + ".json")));
        return doc.RootElement.GetProperty(key).GetString()!;
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void StatDescWis_DoesNotClaimMagicResistance(string lang)
    {
        string text = Get(lang, "character_creation.stat_desc_wis").ToLowerInvariant();
        foreach (var b in Banned) text.Should().NotContain(b, lang);
        text.Trim().Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void StatDescWis_RowFits79Columns(string lang)
    {
        // CharacterCreationSystem: "  " + name padded to 15 + " " + value (3 wide) + "  - " + description
        string row = "  " + Get(lang, "ui.stat_wisdom").PadRight(15) + " " + "100" + "  - " + Get(lang, "character_creation.stat_desc_wis");
        row.Length.Should().BeLessOrEqualTo(79, lang + ": " + row);
    }
}
