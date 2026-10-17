using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.4: Shadow Incarnate (TryManweBossAbility) deals its damage to you and heals Manwe by 30 percent of it. Its message
/// told the player "You recover {1} HP" in every language; the heal is Manwe's, so each line now says Manwe heals.
/// </summary>
public class ShadowIncarnateText134Tests
{
    private const string Key = "combat.manwe_darkness_damage";

    // the second-person recovery each locale used before 1.3.4
    private static readonly Dictionary<string, string> YouRecover = new()
    {
        ["en"] = "You recover", ["es"] = "Recuperas", ["fr"] = "récupérez", ["hu"] = "visszanyersz", ["it"] = "Recuperi",
    };

    private static string Line(string lang)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Localization", "en.json")))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test must run inside the repository");
        var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            File.ReadAllText(Path.Combine(dir!.FullName, "Localization", lang + ".json")))!;
        return doc[Key].GetString() ?? "";
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public void ShadowIncarnateLine_SaysManweHeals_InEveryLanguage(string lang)
    {
        string line = Line(lang);
        line.Should().Contain("{0}", "the damage to you").And.Contain("{1}", "Manwe's heal");
        string heal = line.Substring(line.IndexOf("{0}", StringComparison.Ordinal) + 3);
        heal.Should().Contain("Manwe", $"{lang}: the heal is named as Manwe's").And.Contain("{1}");
        line.Should().NotContain(YouRecover[lang], $"{lang}: the player does not recover the HP");
    }

    [Fact]
    public void ShadowIncarnateLine_English_IsExact()
    {
        Line("en").Should().Be("The darkness burns for {0} damage! Manwe heals for {1} HP!");
    }
}
